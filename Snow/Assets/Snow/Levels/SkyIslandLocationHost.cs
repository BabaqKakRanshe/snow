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
    /// the location root next to its NetworkObject; Tools / Snow / Integrate BaseV2 Level fills <see cref="parts"/>.
    /// The server runs the core's own logic (Toggle: linked shutter curtains, room switches, the main switch) and publishes
    /// the result: one bit per part without a NetworkDoor, the grid power and the room bits. Clients only copy that state
    /// into the parts' fields and call SetRoom / SetPower: the core's Toggle / Open / Close / Follow raise onToggled, which
    /// would flip a room or the grid a second time on a client (NET-01, NET-02). Doors replicate themselves (NetworkDoor).
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
            /// <summary>The grid's switch (main switch): the core's power grid listens to it.</summary>
            PowerSwitch,
            /// <summary>Room light switch: the light manager flips the room on every toggle.</summary>
            LightSwitch,
            /// <summary>Electric shutter switch: drives the curtains of its room, needs the grid.</summary>
            ShutterSwitch,
            /// <summary>Shutter strap winder: drives one curtain.</summary>
            ShutterWinder,
            /// <summary>Shutter curtain: driven only, no zone.</summary>
            Shutter,
        }

        [Serializable]
        public class Part
        {
            [Tooltip("Zone id: the part's name when names are unique in the location, else a short generated id")] public string id;
            public SkyIslandInteractable part;
            [Tooltip("Doors only: the NetworkDoor on the part's object")] public NetworkDoor door;
            public Category category;
            [Tooltip("Light / shutter switches and winders: room name (light manager room for light switches)")] public string room;
            [Tooltip("State text at target 0 / 1 (Furniture, Positional, PowerSwitch)")] public string state0, state1;
            [Tooltip("Action text at target 0 / 1 (Furniture, Positional, PowerSwitch)")] public string action0, action1;
        }

        public Part[] parts = new Part[0];
        [Tooltip("Room lights of the location (none: no room state is replicated)")] public SkyIslandLightManager lights;
        [Tooltip("Power grid of the location (none: no power state is replicated)")] public SkyIslandPowerGrid grid;

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

        protected override void Awake()
        {
            base.Awake();
            _bits = new NetworkList<ulong>();                          // NetworkBehaviour.OnDestroy disposes it, spawned or not
            var synced = new List<int>();
            _syncedOf = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                Part p = parts[i];
                _syncedOf[i] = -1;
                if (p == null || string.IsNullOrEmpty(p.id)) continue;
                _index[p.id] = i;
                if (p.door == null && p.part != null) { _syncedOf[i] = synced.Count; synced.Add(i); }
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
        }

        // ------------------------------------------------------------------ server
        public override bool ServerUse(ulong clientId, string id, UsePhase phase, out string reason)
        {
            reason = null;
            if (phase == UsePhase.Release) return true;
            if (!_index.TryGetValue(id, out int i) || parts[i].part == null) { reason = "Нет действия"; return false; }
            Part p = parts[i];
            if (p.part.locked) { reason = "Заперто"; return false; }
            if (p.door != null)
            {
                p.door.Toggle(DoorDrive.Manual);
                return true;
            }
            if (p.part.driven) { reason = "Управляется изнутри"; return false; }
            if (!p.part.HasPower) { reason = "Нет питания"; return false; }
            p.part.Toggle();                                           // the core does the rest: linked curtains, rooms, grid
            Publish();
            return true;
        }

        void LateUpdate()
        {
            if (!IsSpawned) return;
            if (IsServer) Publish();
            else ApplyReplicated(false);
        }

        /// <summary>Server: what changed since the last frame goes out, whoever changed it (players, later scripted events).
        /// No allocations in frames without changes.</summary>
        void Publish()
        {
            for (int e = 0; e < Words && e < _bits.Count; e++)
            {
                ulong word = Word(e);
                ulong changed = word ^ _bits[e];
                if (changed == 0UL) continue;
                _bits[e] = word;
                for (int b = 0; b < 64; b++)
                    if ((changed & (1UL << b)) != 0)
                        BenchLog.Event("loc_state", "word", e, "bit", b, "id", _syncedIds[e * 64 + b], "v", (word >> b) & 1UL);
            }
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
        /// grid and rooms through the core's setters. Compared every frame, so the arrival order of the variables and
        /// whether the grid's Start has run do not matter. <paramref name="snap"/>: late join, no animation.</summary>
        void ApplyReplicated(bool snap)
        {
            for (int i = 0; i < _synced.Length && (i >> 6) < _bits.Count; i++)
            {
                SkyIslandInteractable p = _synced[i];
                if (p == null) continue;
                float t = ((_bits[i >> 6] >> (i & 63)) & 1UL) != 0 ? 1f : 0f;
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

        // ------------------------------------------------------------------ hints (client and server, replicated state)
        public override string Describe(string id)
        {
            if (!_index.TryGetValue(id, out int i)) return null;
            Part p = parts[i];
            if (p.door != null) return DoorText(p.door.State);
            if (p.part == null) return null;
            switch (p.category)
            {
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
                default:
                    return p.part.target > 0.5f ? p.state1 : p.state0;
            }
        }

        public override string ActionText(string id)
        {
            if (!_index.TryGetValue(id, out int i)) return base.ActionText(id);
            Part p = parts[i];
            if (p.door != null)
            {
                bool opening = p.door.State == DoorState.Open || p.door.State == DoorState.Opening;
                return opening ? "закрыть" : "открыть";
            }
            if (p.part == null) return base.ActionText(id);
            switch (p.category)
            {
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

        static string DoorText(DoorState s)
        {
            switch (s)
            {
                case DoorState.Closed: return "закрыта";
                case DoorState.Opening: return "открывается";
                case DoorState.Open: return "открыта";
                case DoorState.Closing: return "закрывается";
                default: return "упёрлась";
            }
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

        /// <summary>FNV-1a over the part ids and room names in replication order: host and client must agree.</summary>
        uint LayoutHash()
        {
            uint h = 2166136261u;
            void Add(string s)
            {
                if (s != null) foreach (char c in s) { h ^= c; h *= 16777619u; }
                h ^= 0x1F; h *= 16777619u;                             // separator
            }
            foreach (Part p in parts) Add(p?.id);
            if (lights != null) foreach (var r in lights.rooms) Add(r?.name);
            return h;
        }
    }
}
