using System;
using System.Collections.Generic;
using SkyIsland;
using Snow.Doors;
using Snow.Interaction;
using Snow.Net;
using Unity.Netcode;
using UnityEngine;

namespace Snow.Levels
{
    /// <summary>
    /// Bridge between the openable parts of a location built by the Sky Island core (SkyIslandInteractable, light manager,
    /// power grid) and the game's networked interaction (zones on the Interact layer, PlayerNet.UseRpc, ServerUse). Sits on
    /// the location root next to its NetworkObject; Tools / Snow / Integrate ... Level (LocationLevelBuilder) fills
    /// <see cref="parts"/>. The server runs the core's own logic (Toggle: linked shutter curtains, room switches, the main
    /// switch) and publishes the result: one bit per part without a NetworkDoor, the grid power and the room bits. Clients
    /// only copy that state into the parts' fields and call SetRoom / SetPower: the core's Toggle / Open / Close / Follow
    /// raise onToggled, which would flip a room or the grid a second time on a client (NET-01, NET-02). Doors with a
    /// NetworkDoor replicate themselves; doors without one (sliding gates, the thousands of doors of Town) go the shared way.
    /// A location script on the same object (<see cref="ILocationScript"/>, Forest) owns its Scripted parts and extra zones,
    /// may word any hint and opens locks (FOREST_U3 F5.2).
    /// </summary>
    public class SkyIslandLocationHost : InteractionHost
    {
        public enum Category : byte
        {
            /// <summary>NetworkDoor on the part: doors and gates (DOOR-01).</summary>
            Door,
            /// <summary>Drawers, cabinet and appliance doors: open / closed.</summary>
            Furniture,
            /// <summary>A position without consequences yet (breakers, isolators, heating stop: OPEN).</summary>
            Positional,
            /// <summary>The grid's switch (main switch, generator start): the core's power grid listens to it.</summary>
            PowerSwitch,
            /// <summary>Room light switch: the light manager flips the room on every toggle.</summary>
            LightSwitch,
            /// <summary>Electric shutter switch: drives the curtains of its room, needs the grid.</summary>
            ShutterSwitch,
            /// <summary>Shutter strap winder: drives one curtain.</summary>
            ShutterWinder,
            /// <summary>Shutter curtain: driven only, no zone.</summary>
            Shutter,
            /// <summary>A door without a NetworkDoor (sliding gates, Town's doors): a bit like furniture, the part stays on its
            /// own layer so a closed door holds the player. A player in the leaf's target pose refuses the command (L4.4).</summary>
            HostDoor,
            /// <summary>Driven by the location script (Forest's generator, pump, boiler): in parts, not in the bits; uses
            /// and texts go to the script (FOREST_U3 F5.1).</summary>
            Scripted,
        }

        [Serializable]
        public class Part
        {
            [Tooltip("Zone id: the part's name when names are unique in the location, else a short generated id (p<n>)")] public string id;
            public SkyIslandInteractable part;
            [Tooltip("Doors only: the NetworkDoor on the part's object")] public NetworkDoor door;
            public Category category;
            [Tooltip("Light / shutter switches and winders: room name (light manager room for light switches)")] public string room;
            [Tooltip("State text at target 0 / 1 (Furniture, Positional, PowerSwitch)")] public string state0, state1;
            [Tooltip("Action text at target 0 / 1 (Furniture, Positional, PowerSwitch)")] public string action0, action1;
            [Tooltip("State text of a locked part (empty: \"заперто\")")] public string lockedState;
            [Tooltip("PowerSwitch: state text when on but the grid is down (empty: state1)")] public string state1NoPower;
            [Tooltip("Doors: closed, opening, open, closing, blocked (empty: закрыта, открывается, открыта, закрывается, упёрлась)")]
            public string[] doorStates;
            [Tooltip("HostDoor: the leaf's local position and rotation when closed (written by the menu: doors are closed in the editor)")]
            public Vector3 restPosition;
            public Quaternion restRotation = Quaternion.identity;
        }

        public Part[] parts = new Part[0];
        [Tooltip("Room lights of the location (none: no room state is replicated)")] public SkyIslandLightManager lights;
        [Tooltip("Power grid of the location (none: no power state is replicated)")] public SkyIslandPowerGrid grid;
        [Tooltip("Large locations (Town): the server sends the elements a use changed and reconciles everything once a " +
                 "second; clients apply changed elements and reconcile once a second. Off: a full pass every frame")]
        public bool incremental;

        /// <summary>s: full reconcile period in incremental mode.</summary>
        const float ReconcilePeriod = 1f;

        // server writes, everyone reads
        // a bit per part without a NetworkDoor, in the order of parts: part i is bit i % 64 of element i / 64. Bits, not a
        // byte per part: Town has over 10 000 parts, and a byte list with the scene data would not fit one UnityTransport
        // message (MaxPayloadSize 6144 by default); one format for every location
        NetworkList<ulong> _bits;
        readonly NetworkVariable<bool> _power = new NetworkVariable<bool>(true);
        readonly NetworkVariable<uint> _rooms = new NetworkVariable<uint>();
        readonly NetworkVariable<uint> _layoutHash = new NetworkVariable<uint>();

        readonly Dictionary<string, int> _index = new Dictionary<string, int>(StringComparer.Ordinal);
        SkyIslandInteractable[] _synced;
        string[] _syncedIds;
        int[] _syncedOf;                                               // part index -> synced index, -1 for doors
        uint _hash;
        float _nextReconcile;
        ILocationScript[] _scripts = Array.Empty<ILocationScript>();
        readonly Collider[] _overlap = new Collider[16];

        /// <summary>Under a location root lie the capsules of every tree: the embedded-zone table (zone x convex collider)
        /// would take seconds to minutes on Forest and Town. Location zones stick out of their parts and need no embedding.</summary>
        protected override bool UseEmbeddedZones => false;

        protected override void Awake()
        {
            base.Awake();
            _scripts = GetComponents<ILocationScript>();
            _bits = new NetworkList<ulong>();                          // NetworkBehaviour.OnDestroy disposes it, spawned or not
            var synced = new List<int>();
            _syncedOf = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                Part p = parts[i];
                _syncedOf[i] = -1;
                if (p == null || string.IsNullOrEmpty(p.id)) continue;
                _index[p.id] = i;
                if (p.door == null && p.part != null && p.category != Category.Scripted) { _syncedOf[i] = synced.Count; synced.Add(i); }
            }
            _synced = new SkyIslandInteractable[synced.Count];
            _syncedIds = new string[synced.Count];
            for (int s = 0; s < synced.Count; s++)
            {
                _synced[s] = parts[synced[s]].part;
                _syncedIds[s] = parts[synced[s]].id;
            }
            _hash = LayoutHash();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                _bits.Clear();                                         // a second Host in the same Play session spawns the old list
                for (int e = 0; e < Words; e++) _bits.Add(Word(e));
                if (grid != null) _power.Value = grid.Powered;
                _rooms.Value = RoomBits();
                _layoutHash.Value = _hash;
                return;
            }
            bool same = _layoutHash.Value == _hash;
            BenchLog.Event("loc_hash", "host", _layoutHash.Value, "local", _hash, "match", same);
            if (!same)
                Debug.LogError($"[Snow] {name}: the host's and this client's scenes were built from different versions " +
                               $"(layout hash {_layoutHash.Value:X8} vs {_hash:X8}); interaction state will not match.");
            // the grid's Start forces startPowered: set before it runs, the lamps come up as on the host (no flash)
            if (grid != null) grid.startPowered = _power.Value;
            ApplyReplicated(true);
            if (incremental) _bits.OnListChanged += OnBitsChanged;
        }

        public override void OnNetworkDespawn()
        {
            if (!IsServer && incremental) _bits.OnListChanged -= OnBitsChanged;
        }

        // ------------------------------------------------------------------ for location scripts
        /// <summary>Part entry by zone id (any peer).</summary>
        public bool TryGetPart(string id, out Part part)
        {
            part = _index.TryGetValue(id, out int i) ? parts[i] : null;
            return part != null && part.part != null;
        }

        /// <summary>Grid power as this peer sees it (no grid: powered).</summary>
        public bool Powered => grid == null || grid.Powered;

        ILocationScript Owner(string id)
        {
            for (int k = 0; k < _scripts.Length; k++)
                if (_scripts[k] != null && _scripts[k].Owns(id)) return _scripts[k];
            return null;
        }

        // ------------------------------------------------------------------ server
        public override bool ServerUse(ulong clientId, string id, UsePhase phase, out string reason)
        {
            reason = null;
            ILocationScript owner = Owner(id);
            if (owner != null) return owner.ServerUse(clientId, id, phase, out reason);   // Release included (F5.2)
            if (phase == UsePhase.Release) return true;
            if (!_index.TryGetValue(id, out int i) || parts[i].part == null) { reason = "Нет действия"; return false; }
            Part p = parts[i];
            if (p.part.locked)
            {
                // a script may take the lock off (a key in hand); the first refusal with a reason is shown
                bool open = false;
                for (int k = 0; k < _scripts.Length && !open; k++)
                {
                    if (_scripts[k] == null) continue;
                    if (_scripts[k].TryUnlock(clientId, id, out string why)) open = true;
                    else if (reason == null) reason = why;
                }
                if (!open) { reason ??= "Заперто"; return false; }
                reason = null;
            }
            if (p.door != null)
            {
                p.door.Toggle(DoorDrive.Manual);
                return true;
            }
            if (p.part.driven) { reason = "Управляется изнутри"; return false; }
            if (!p.part.HasPower) { reason = "Нет питания"; return false; }
            if (p.category == Category.HostDoor && !LeafFree(p, p.part.target > 0.5f ? 0f : 1f)) { reason = "Мешает игрок"; return false; }
            p.part.Toggle();                                           // the core does the rest: linked curtains, rooms, grid
            if (!incremental) Publish();
            else
            {
                // the part and whatever it drives; anything else changes through the once-a-second reconcile
                PublishWord(_syncedOf[i] >> 6);
                if (p.part.linked != null)
                    foreach (SkyIslandInteractable l in p.part.linked)
                        if (l != null && _index.TryGetValue(IdOf(l), out int li) && _syncedOf[li] >= 0) PublishWord(_syncedOf[li] >> 6);
                PublishGrid();
            }
            return true;
        }

        void LateUpdate()
        {
            if (!IsSpawned) return;
            bool full = !incremental || Time.unscaledTime >= _nextReconcile;
            if (full) _nextReconcile = Time.unscaledTime + ReconcilePeriod;
            if (IsServer)
            {
                if (full) Publish();
                else PublishGrid();
            }
            else if (full) ApplyReplicated(false);
            else ApplyGrid();
        }

        /// <summary>Server: what changed since the last pass goes out, whoever changed it (players, later scripted events).
        /// No allocations in passes without changes.</summary>
        void Publish()
        {
            for (int e = 0; e < Words && e < _bits.Count; e++) PublishWord(e);
            PublishGrid();
        }

        void PublishWord(int e)
        {
            if (e < 0 || e >= _bits.Count) return;
            ulong word = Word(e);
            ulong changed = word ^ _bits[e];
            if (changed == 0UL) return;
            _bits[e] = word;
            for (int b = 0; b < 64; b++)
                if ((changed & (1UL << b)) != 0)
                    BenchLog.Event("loc_state", "word", e, "bit", b, "id", _syncedIds[e * 64 + b], "v", (word >> b) & 1UL);
        }

        void PublishGrid()
        {
            if (grid != null && _power.Value != grid.Powered)
            {
                _power.Value = grid.Powered;
                BenchLog.Event("loc_state", "power", grid.Powered);
            }
            uint rooms = RoomBits();
            if (_rooms.Value != rooms)
            {
                _rooms.Value = rooms;
                BenchLog.Event("loc_state", "rooms", rooms);
            }
        }

        // ------------------------------------------------------------------ client
        /// <summary>Client: parts by their fields only (no Toggle / Follow: no onToggled, no driving, no power check),
        /// grid and rooms through the core's setters. Compared on every pass, so the arrival order of the variables and
        /// whether the grid's Start has run do not matter. <paramref name="snap"/>: late join, no animation.</summary>
        void ApplyReplicated(bool snap)
        {
            for (int e = 0; e < _bits.Count; e++) ApplyWord(e, snap);
            ApplyGrid();
        }

        void ApplyWord(int e, bool snap)
        {
            if (e < 0 || e >= _bits.Count) return;
            ulong word = _bits[e];
            int end = Mathf.Min(_synced.Length, e * 64 + 64);
            for (int i = e * 64; i < end; i++)
            {
                SkyIslandInteractable p = _synced[i];
                if (p == null) continue;
                float t = ((word >> (i & 63)) & 1UL) != 0 ? 1f : 0f;
                if (snap)
                {
                    p.openAmount = t;
                    p.target = t;
                    p.Apply();
                    p.enabled = false;
                }
                else if (!Mathf.Approximately(p.target, t))
                {
                    p.target = t;
                    p.enabled = true;
                }
            }
        }

        void ApplyGrid()
        {
            if (grid != null && grid.Powered != _power.Value) grid.SetPower(_power.Value);
            if (lights == null) return;
            uint bits = _rooms.Value;
            for (int r = 0; r < lights.rooms.Count && r < 32; r++)
            {
                string room = lights.rooms[r].name;
                bool on = (bits & (1u << r)) != 0;
                if (lights.IsRoomOn(room) != on) lights.SetRoom(room, on);
            }
        }

        /// <summary>Incremental mode, client: an element changed on the server.</summary>
        void OnBitsChanged(NetworkListEvent<ulong> change)
        {
            if (change.Type == NetworkListEvent<ulong>.EventType.Value || change.Type == NetworkListEvent<ulong>.EventType.Add ||
                change.Type == NetworkListEvent<ulong>.EventType.Insert)
                ApplyWord(change.Index, false);
            else ApplyReplicated(false);
        }

        /// <summary>
        /// Server, L4.4: is the leaf's pose at <paramref name="amount"/> (0 closed, 1 open) free of players? The core moves
        /// the leaf by itself and cannot stop halfway (a bit is 0 or 1), so this is asked once, at the command. Each solid
        /// collider of the leaf is boxed in that pose (its local box carried by the part's change of pose, the same math as
        /// NetworkDoor.IsFree); a CharacterController inside refuses. Other players on the server have their controller on
        /// (seated ones excepted).
        /// </summary>
        bool LeafFree(Part p, float amount)
        {
            SkyIslandInteractable d = p.part;
            Transform t = d.transform;
            Vector3 axis = d.axis.sqrMagnitude > 1e-8f ? d.axis.normalized : Vector3.up;
            float v = Mathf.Lerp(d.min, d.max, amount);
            Vector3 lp = p.restPosition;
            Quaternion lr = p.restRotation;
            if (d.kind == SkyIslandInteractable.Kind.Hinge) lr = Quaternion.AngleAxis(v, axis) * p.restRotation;
            else if (d.kind == SkyIslandInteractable.Kind.Slide) lp = p.restPosition + axis * v;
            Matrix4x4 parent = t.parent != null ? t.parent.localToWorldMatrix : Matrix4x4.identity;
            Matrix4x4 move = parent * Matrix4x4.TRS(lp, lr, t.localScale) * t.worldToLocalMatrix;   // pose now -> target pose
            foreach (Collider c in d.GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger || !c.enabled) continue;
                Bounds b = LocalBox(c);
                Matrix4x4 m = move * c.transform.localToWorldMatrix;
                Vector3 scale = c.transform.lossyScale;
                Vector3 half = Vector3.Scale(b.extents, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                int n = Physics.OverlapBoxNonAlloc(m.MultiplyPoint3x4(b.center), half, _overlap, m.rotation, ~0, QueryTriggerInteraction.Ignore);
                for (int k = 0; k < n; k++)
                    if (_overlap[k] is CharacterController cc && cc.enabled) return false;
            }
            return true;
        }

        static Bounds LocalBox(Collider c)
        {
            switch (c)
            {
                case BoxCollider bc: return new Bounds(bc.center, bc.size);
                case MeshCollider mc when mc.sharedMesh != null: return mc.sharedMesh.bounds;
                case SphereCollider sc: return new Bounds(sc.center, Vector3.one * (2f * sc.radius));
                case CapsuleCollider cap: return new Bounds(cap.center, new Vector3(2f * cap.radius, cap.height, 2f * cap.radius));
                default: return new Bounds(c.transform.InverseTransformPoint(c.bounds.center), c.bounds.size);
            }
        }

        // ------------------------------------------------------------------ hints (client and server, replicated state)
        public override float HoldSeconds(string id)
        {
            ILocationScript owner = Owner(id);
            return owner != null ? owner.HoldSeconds(id) : 0f;
        }

        public override string Describe(string id)
        {
            for (int k = 0; k < _scripts.Length; k++)
            {
                string s = _scripts[k]?.Describe(id);
                if (s != null) return s;
            }
            if (!_index.TryGetValue(id, out int i)) return null;
            Part p = parts[i];
            if (p.part != null && p.part.locked) return string.IsNullOrEmpty(p.lockedState) ? "заперто" : p.lockedState;
            if (p.door != null) return DoorText(p.door.State, p.doorStates);
            if (p.part == null) return null;
            switch (p.category)
            {
                case Category.HostDoor:
                {
                    SkyIslandInteractable d = p.part;
                    DoorState s = d.target > 0.5f ? (d.openAmount >= 0.999f ? DoorState.Open : DoorState.Opening)
                                                  : (d.openAmount <= 0.001f ? DoorState.Closed : DoorState.Closing);
                    return DoorText(s, p.doorStates);
                }
                case Category.LightSwitch:
                {
                    bool on = lights != null && lights.IsRoomOn(p.room);
                    if (grid != null && !grid.Powered) return on ? "нет питания (включён)" : "нет питания (выключен)";
                    return on ? "свет горит" : "свет не горит";
                }
                case Category.ShutterSwitch:
                    if (!p.part.HasPower) return "нет питания";
                    return CurtainText(p.part, "опущены", "подняты", "едут");
                case Category.ShutterWinder:
                    return CurtainText(p.part, "опущена", "поднята", "едет");
                case Category.PowerSwitch:
                    if (p.part.target > 0.5f && grid != null && !grid.Powered && !string.IsNullOrEmpty(p.state1NoPower)) return p.state1NoPower;
                    return p.part.target > 0.5f ? p.state1 : p.state0;
                default:
                    return p.part.target > 0.5f ? p.state1 : p.state0;
            }
        }

        public override string ActionText(string id)
        {
            for (int k = 0; k < _scripts.Length; k++)
            {
                string s = _scripts[k]?.ActionText(id);
                if (s != null) return s;
            }
            if (!_index.TryGetValue(id, out int i)) return base.ActionText(id);
            Part p = parts[i];
            if (p.door != null)
            {
                bool opening = p.door.State == DoorState.Open || p.door.State == DoorState.Opening;
                return DoorAction(p, opening);
            }
            if (p.part == null) return base.ActionText(id);
            switch (p.category)
            {
                case Category.HostDoor:
                    return DoorAction(p, p.part.target > 0.5f);
                case Category.LightSwitch:
                    return lights != null && lights.IsRoomOn(p.room) ? "выключить" : "включить";
                case Category.ShutterSwitch:
                case Category.ShutterWinder:
                    // Toggle closes at target > 0.5 and the curtains get Follow(0): they go down
                    return p.part.target > 0.5f ? "опустить" : "поднять";
                default:
                    string a = p.part.target > 0.5f ? p.action1 : p.action0;
                    return string.IsNullOrEmpty(a) ? base.ActionText(id) : a;
            }
        }

        static readonly string[] DefaultDoorStates = { "закрыта", "открывается", "открыта", "закрывается", "упёрлась" };

        static string DoorText(DoorState s, string[] texts)
        {
            if (texts == null || texts.Length < 5) texts = DefaultDoorStates;
            switch (s)
            {
                case DoorState.Closed: return texts[0];
                case DoorState.Opening: return texts[1];
                case DoorState.Open: return texts[2];
                case DoorState.Closing: return texts[3];
                default: return texts[4];
            }
        }

        /// <summary>Open or opening: the close action (action1, else "закрыть"); otherwise the open one (action0, "открыть").</summary>
        static string DoorAction(Part p, bool openOrOpening)
        {
            string a = openOrOpening ? p.action1 : p.action0;
            return !string.IsNullOrEmpty(a) ? a : openOrOpening ? "закрыть" : "открыть";
        }

        /// <summary>State of the curtains a switch or winder drives: all down, all up, or moving / in between.</summary>
        static string CurtainText(SkyIslandInteractable driver, string down, string up, string moving)
        {
            bool allDown = true, allUp = true, any = false;
            if (driver.linked != null)
                foreach (var c in driver.linked)
                {
                    if (c == null || c == driver) continue;
                    any = true;
                    if (c.openAmount > 0.001f) allDown = false;
                    if (c.openAmount < 0.999f) allUp = false;
                }
            if (!any) return driver.target > 0.5f ? up : down;
            return allDown ? down : allUp ? up : moving;
        }

        // ------------------------------------------------------------------ helpers
        int Words => (_synced.Length + 63) >> 6;

        /// <summary>Id of a part the server toggled through a link (curtains): looked up once, linked parts are few.</summary>
        string IdOf(SkyIslandInteractable part)
        {
            for (int i = 0; i < parts.Length; i++)
                if (parts[i] != null && parts[i].part == part) return parts[i].id;
            return string.Empty;
        }

        /// <summary>Server: element e of the bit list from the parts' targets.</summary>
        ulong Word(int e)
        {
            ulong word = 0UL;
            int end = Mathf.Min(_synced.Length, e * 64 + 64);
            for (int i = e * 64; i < end; i++)
                if (_synced[i] != null && _synced[i].target > 0.5f) word |= 1UL << (i & 63);
            return word;
        }

        uint RoomBits()
        {
            if (lights == null) return 0u;
            uint bits = 0u;
            for (int r = 0; r < lights.rooms.Count && r < 32; r++)
                if (lights.IsRoomOn(lights.rooms[r].name)) bits |= 1u << r;
            return bits;
        }

        /// <summary>FNV-1a over the part ids, the parts' object names (generated ids p&lt;n&gt; alone would not see a different
        /// order) and room names in replication order: host and client must agree.</summary>
        uint LayoutHash()
        {
            uint h = 2166136261u;
            void Add(string s)
            {
                if (s != null) foreach (char c in s) { h ^= c; h *= 16777619u; }
                h ^= 0x1F; h *= 16777619u;                             // separator
            }
            foreach (Part p in parts)
            {
                Add(p?.id);
                Add(p?.part != null ? p.part.name : null);
            }
            if (lights != null) foreach (var r in lights.rooms) Add(r?.name);
            return h;
        }
    }
}
