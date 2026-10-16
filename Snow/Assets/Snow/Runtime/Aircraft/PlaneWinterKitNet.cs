using System;
using System.Collections.Generic;
using Snow.Cargo;
using Snow.Net;
using Snow.Seats;
using Unity.Netcode;
using UnityEngine;
using UtilityPlane;

namespace Snow.Aircraft
{
    /// <summary>
    /// The winter preflight kit 1.2 on the game plane (docs/gdd/PLANE_WINTERKIT_UNITY.md K4, И4). The model's
    /// UtilityPlaneWinterKit (child WinterKit) only holds and shows state; here the server decides and replicates:
    ///  - Mask (bit i = KitItem i) on the plane's network object; every peer and late joiners ApplyMask it;
    ///  - zones IV_Kit_&lt;Item&gt; (kind kit): with a carry form in hand whose installs lists the item — install; with empty
    ///    hands on an installed item — remove, the carry form (the game variant found by its key) lands at the feet.
    ///    Only for a player outside, not seated, with the plane on the ground and still; held for installSeconds
    ///    (from the prefab slots, TEMPORARY), cancelled when the button is let go or the player walks off;
    ///  - the tie-down bag counts its sets (setsLeft); SetInstalled true/false decides, so nothing is doubled or lost;
    ///  - while the plane moves or flies the zones are off on every peer.
    /// Consequences are flags the plane systems read (StartBlocked, AirspeedBlocked, WingCoverL/R, MoveBlocked, heater).
    /// </summary>
    public class PlaneWinterKitNet : NetworkBehaviour
    {
        [Tooltip("Game variants of the carry forms (KitCarry_*), looked up by UtilityPlaneKitItem.key")] public GameObject[] carryPrefabs = Array.Empty<GameObject>();
        [Tooltip("Kit on the plane at the start of a level (TEMPORARY K4.6: everything but the heater)")] public bool startInstalled = true;
        [Tooltip("m from the zone a holding player may move before the action is cancelled")] public float holdReach = 3f;

        readonly NetworkVariable<int> _mask = new NetworkVariable<int>(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        PlaneVehicle _plane;
        UtilityPlaneWinterKit _kit;
        bool _zonesOn = true;
        float _snapAt = -1f;

        struct Hold { public KitItem Item; public bool Install; public float Done; public CargoItem Held; }
        readonly Dictionary<ulong, Hold> _holds = new Dictionary<ulong, Hold>();

        public UtilityPlaneWinterKit Kit => _kit;
        public int Mask => _mask.Value;

        void Awake()
        {
            _plane = GetComponent<PlaneVehicle>();
            _kit = GetComponentInChildren<UtilityPlaneWinterKit>(true);
        }

        public override void OnNetworkSpawn()
        {
            if (_kit == null) return;
            if (IsServer && startInstalled && _mask.Value == 0)
                _mask.Value = ((1 << UtilityPlaneWinterKit.ItemCount) - 1) & ~(1 << (int)KitItem.Heater);
            _mask.OnValueChanged += (_, m) => { _kit.ApplyMask(m); _snapAt = Time.time + 0.2f; };
            _kit.ApplyMask(_mask.Value);
            _snapAt = Time.time + 2f;                               // the chocks onto the ground once the struts settled (K4.4)
        }

        public static bool IsKitId(string id) => Enum.TryParse(id, out KitItem _);

        // ---------------------------------------------------------------- consequences (K4.3), any peer
        public bool StartBlocked => _kit != null && _kit.EngineStartBlocked;
        public bool AirspeedBlocked => _kit != null && _kit.AirspeedBlocked;
        public bool WingCoverL => _kit != null && _kit.IsInstalled(KitItem.WingCover_L);
        public bool WingCoverR => _kit != null && _kit.IsInstalled(KitItem.WingCover_R);
        public bool MoveBlocked => _kit != null && _kit.MoveBlocked;
        public bool Preheating => _kit != null && _kit.Preheating;
        public bool Insulated => _kit != null && _kit.EngineInsulated;

        /// <summary>"Снять: …" with the labels of what blocks the engine start.</summary>
        public string StartBlockText()
        {
            var names = new List<string>();
            foreach (KitItem i in _kit.InstalledItems())
                if ((UtilityPlaneWinterKit.EngineStartMask & (1 << (int)i)) != 0) names.Add(_kit.GetSlot(i)?.label ?? i.ToString());
            return "Стартер не крутит. Снять: " + string.Join(", ", names);
        }

        void Update()
        {
            if (_kit == null || !IsSpawned) return;
            // zones only while the plane stands (every peer, K4.2)
            PlaneTelemetry t = _plane.Telemetry;
            bool still = t.HeightAgl < 3f && t.GroundSpeed < 0.5f;
            if (still != _zonesOn) { _zonesOn = still; _kit.SetZonesEnabled(still); }
            if (_snapAt > 0f && Time.time >= _snapAt) { _snapAt = -1f; _kit.SnapChocks(); }
        }

        // ---------------------------------------------------------------- interaction, server
        public float HoldSeconds(string id) =>
            _kit != null && Enum.TryParse(id, out KitItem i) && _kit.GetSlot(i) is { } s ? Mathf.Max(0f, s.installSeconds) : 0f;

        /// <summary>Is the zone usable for a player carrying this item (client ray and server)?</summary>
        public bool ZoneOpen(string id, CargoItem held)
        {
            if (_kit == null || !Enum.TryParse(id, out KitItem i)) return false;
            if (held == null) return _kit.IsInstalled(i);
            if (!held.TryGetComponent(out UtilityPlaneKitItem k) || Array.IndexOf(k.installs, i) < 0) return false;
            return !_kit.IsInstalled(i) || k.key == "TieDownBag";     // the bag also takes an installed tie-down back
        }

        public bool ServerUse(ulong client, string id, Snow.Interaction.UsePhase phase, out string reason)
        {
            reason = null;
            if (!Enum.TryParse(id, out KitItem item)) { reason = "Нет действия"; return false; }
            if (phase == Snow.Interaction.UsePhase.Release) { _holds.Remove(client); return true; }
            if (phase != Snow.Interaction.UsePhase.Press) return true;
            if (!Allowed(client, out reason)) return false;
            CargoItem held = CargoItem.HeldBy(client);
            // with the tie-down bag in hand on an installed tie-down: remove it into the bag (K4.2)
            bool bagBack = held != null && _kit.IsInstalled(item) && held.TryGetComponent(out KitCarry inHand) && inHand.IsTieDownBag
                           && Array.IndexOf(inHand.Kit.installs, item) >= 0;
            bool install = held != null && !bagBack;
            if (install)
            {
                if (!held.TryGetComponent(out UtilityPlaneKitItem k) || Array.IndexOf(k.installs, item) < 0) { reason = "Это сюда не ставится"; return false; }
                if (_kit.IsInstalled(item)) { reason = "Уже стоит"; return false; }
                if (!_kit.CanInstall(item)) { reason = item == KitItem.Heater ? "Мешает заглушка воздухозаборника" : "Мешает рукав подогревателя"; return false; }
                if (k.key == "TieDownBag" && held.TryGetComponent(out KitCarry bag) && bag.SetsLeft <= 0) { reason = "Сумка пуста"; return false; }
            }
            else if (!_kit.IsInstalled(item)) { reason = "Возьмите вещь в руки"; return false; }
            float seconds = HoldSeconds(id);
            _holds[client] = new Hold { Item = item, Install = install, Done = Time.time + seconds, Held = held };
            if (seconds <= 0f) Complete(client);
            return true;
        }

        // K4.2: only from outside, on foot, with the aircraft on the ground and still
        bool Allowed(ulong client, out string reason)
        {
            reason = null;
            if (_plane.Telemetry.HeightAgl > 3f || _plane.Telemetry.GroundSpeed > 0.5f) { reason = "Самолёт движется"; return false; }
            if (!NetworkManager.ConnectedClients.TryGetValue(client, out var c) || c.PlayerObject == null) { reason = "Нет игрока"; return false; }
            for (int s = 0; s < _plane.seats.Length; s++) if (_plane.OccupantOf(s) == client) { reason = "Только снаружи"; return false; }
            if (_plane.IsInside(c.PlayerObject.transform.position + Vector3.up * 1.2f)) { reason = "Только снаружи"; return false; }
            return true;
        }

        void FixedUpdate()
        {
            if (!IsServer || _holds.Count == 0) return;
            List<ulong> done = null, cancel = null;
            foreach (var kv in _holds)
            {
                // cancelled when the player walks off or puts down / swaps what he holds
                if (!StillThere(kv.Key, kv.Value.Item) || CargoItem.HeldBy(kv.Key) != kv.Value.Held) (cancel ??= new List<ulong>()).Add(kv.Key);
                else if (Time.time >= kv.Value.Done) (done ??= new List<ulong>()).Add(kv.Key);
            }
            if (cancel != null) foreach (var c in cancel) { _holds.Remove(c); BenchLog.Event("kit_cancel", "client", c); }
            if (done != null) foreach (var c in done) Complete(c);
        }

        bool StillThere(ulong client, KitItem item)
        {
            if (!NetworkManager.ConnectedClients.TryGetValue(client, out var c) || c.PlayerObject == null) return false;
            if (!_plane.TryGetVolume(item.ToString(), out var zone)) return false;
            Collider col = zone.GetComponent<Collider>();
            Vector3 eye = c.PlayerObject.transform.position + Vector3.up * 1.4f;
            return col != null && Vector3.Distance(col.ClosestPoint(eye), eye) <= holdReach;
        }

        void Complete(ulong client)
        {
            if (!_holds.TryGetValue(client, out Hold h)) return;
            _holds.Remove(client);
            if (h.Install)
            {
                CargoItem held = CargoItem.HeldBy(client);
                if (held == null || held != h.Held || !held.TryGetComponent(out UtilityPlaneKitItem k) || Array.IndexOf(k.installs, h.Item) < 0) return;
                bool bag = k.key == "TieDownBag";
                KitCarry carry = held.GetComponent<KitCarry>();
                if (bag && (carry == null || carry.SetsLeft <= 0)) return;
                if (!_kit.SetInstalled(h.Item, true)) return;          // already so or excluded: nothing consumed (K4.1)
                _mask.Value = _kit.Mask;
                if (bag) carry.ServerSetSets(carry.SetsLeft - 1);       // the bag stays, one set less (K4.2)
                else { held.ServerRelease(client); held.NetworkObject.Despawn(true); }
                BenchLog.Event("kit_install", "client", client, "item", h.Item.ToString());
            }
            else
            {
                if (!_kit.SetInstalled(h.Item, false)) return;
                _mask.Value = _kit.Mask;
                GiveCarry(client, h.Item);
                BenchLog.Event("kit_remove", "client", client, "item", h.Item.ToString());
            }
        }

        // removed: the carry form at the player's feet; a tie-down set goes back into a bag in hand, else a bag with one set
        void GiveCarry(ulong client, KitItem item)
        {
            UtilityPlaneWinterKit.Slot slot = _kit.GetSlot(item);
            string key = slot != null && slot.carryPrefab != null && slot.carryPrefab.TryGetComponent(out UtilityPlaneKitItem src) ? src.key : null;
            if (key == null) return;
            CargoItem held = CargoItem.HeldBy(client);
            if (key == "TieDownBag" && held != null && held.TryGetComponent(out KitCarry inHand) && inHand.IsTieDownBag && inHand.SetsLeft < inHand.Kit.sets)
            {
                inHand.ServerSetSets(inHand.SetsLeft + 1);
                return;
            }
            GameObject prefab = Array.Find(carryPrefabs, p => p != null && p.TryGetComponent(out UtilityPlaneKitItem k) && k.key == key);
            if (prefab == null) { Debug.LogWarning($"[Snow] no game carry prefab for {key}"); return; }
            Transform player = NetworkManager.ConnectedClients[client].PlayerObject.transform;
            Vector3 at = player.position + player.forward * 0.6f + Vector3.up * 0.3f;
            GameObject go = Instantiate(prefab, at, Quaternion.Euler(0f, player.eulerAngles.y, 0f));
            go.name = prefab.name;
            go.GetComponent<NetworkObject>().Spawn();
            if (key == "TieDownBag" && go.TryGetComponent(out KitCarry newBag)) newBag.ServerSetSets(1);
        }
    }
}
