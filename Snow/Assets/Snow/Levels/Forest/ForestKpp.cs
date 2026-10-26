using System;
using SkyIsland;
using Snow.Cargo;
using Snow.Interaction;
using Snow.Net;
using Snow.Player;
using Snow.World;
using Unity.Netcode;
using UnityEngine;
using Part = Snow.Levels.SkyIslandLocationHost.Part;

namespace Snow.Levels.Forest
{
    /// <summary>
    /// The electric checkpoint (KPP_CONTRACT.md 2.1 «Как играется», MAIN_CONTRACT.md 1.2 «Пункт управления КПП»). A location
    /// script of the host on the Forest root; Tools / Snow / Integrate Location Level / Forest (ForestKppStep) fills the
    /// links. Power:
    ///  - the booth: the cabinet switch at BATTERIE with the battery on the shelf and both clamps on; at NETZ the line, live
    ///    when the base grid is (after S7);
    ///  - the barrier and gate drives, the cameras and floods: the booth's power or the base grid (the line feeds them);
    ///  - the Leitstand console and its monitors: the base grid.
    /// Buttons 312 / 319 raise or lower the barrier, 313 / 320 drive the gate; the lamp over a button is lit while its
    /// barrier / gate is open and the console has power. The barrier arm and the gate leaf are shared-way parts of the
    /// host (a bit each): the server sets their targets, the host replicates. The booth door's electric lock holds without
    /// the booth's power (from inside it always opens, user 23.10). The cabinet's state goes in network variables; every
    /// peer shows lamps, screens, the beacon and the booth's lights from them. TEMPORARY: the battery is a loose
    /// VanItem_Battery_Net spawned by the road before the barrier (no van on the map; user 23.10), sounds wait for clips.
    /// </summary>
    public class ForestKpp : NetworkBehaviour, ILocationScript
    {
        public const string BtnBarrier = "IX_ConsoleButton_312", BtnGate = "IX_ConsoleButton_313";
        public const string LeitBarrier = "IX_ConsoleButton_319", LeitGate = "IX_ConsoleButton_320";
        public const string CabinetDoor = "IX_CabinetDoor_314", Switch = "IX_CabinetSwitch_315";
        public const string ClampPos = "IX_CabinetClamp_Pos_316", ClampNeg = "IX_CabinetClamp_Neg_317";
        public const string Barrier = "IX_BarrierArm_82", Gate = "IX_Gate_81", BoothDoor = "IX_BoothDoor_74";
        public const string BatterySlot = "KPP_BatterySlot";
        const float PressSeconds = 0.25f, StrikeSeconds = 2.5f, CameraRange = 30f;
        const int CameraEvery = 4;                                     // a camera renders every 4th frame (~15 fps)

        public static ForestKpp Instance { get; private set; }

        [Header("Links (written by Tools / Snow / Integrate Location Level / Forest)")]
        public SkyIslandLocationHost host;
        [Tooltip("Structures/Checkpoint: the booth (inside test for the door lock)")] public Transform booth;
        public StowPoint batterySlot;
        [Tooltip("The battery item (VanItem_Battery_Net), spawned once by the server")] public NetworkObject batteryPrefab;
        public Transform batterySpawn;
        [Header("Kit instances")]
        public KitItem cabinet;
        public KitItem doorStrike, beacon, btnBarrier, btnGate, leitBtnBarrier, leitBtnGate;
        [Tooltip("0 barrier, 1 gate")] public KitItem[] boothMonitors = new KitItem[2], leitMonitors = new KitItem[2], cameraHeads = new KitItem[2], floods = new KitItem[2];
        [Tooltip("0 CAM_Barrier, 1 CAM_Gate")] public Camera[] cameras = new Camera[2];
        [Header("Booth lamps (kpp_lights.json: fluorescent, bulkhead)")]
        public Light[] boothLights = new Light[0];
        public GameObject[] boothGlows = new GameObject[0];
        public Renderer[] lampRenderers = new Renderer[0];
        public Material fluorOn, fluorOff, bulkOn, bulkOff;
        [Tooltip("min, max, speed: the batten as it strikes (Update 3's Light_booth flicker)")] public Vector3 flicker = new Vector3(0.08f, 1f, 5f);

        // ------------------------------------------------------------------ network state (server writes)
        readonly NetworkVariable<bool> _battery = new NetworkVariable<bool>();
        readonly NetworkVariable<bool> _clampPos = new NetworkVariable<bool>();
        readonly NetworkVariable<bool> _clampNeg = new NetworkVariable<bool>();
        readonly NetworkVariable<bool> _switchBatt = new NetworkVariable<bool>();
        readonly NetworkVariable<bool> _barrierUp = new NetworkVariable<bool>();
        readonly NetworkVariable<bool> _gateOpen = new NetworkVariable<bool>();
        readonly NetworkVariable<double> _pressedUntil0 = new NetworkVariable<double>(-1d);
        readonly NetworkVariable<double> _pressedUntil1 = new NetworkVariable<double>(-1d);
        readonly NetworkVariable<double> _pressedUntil2 = new NetworkVariable<double>(-1d);
        readonly NetworkVariable<double> _pressedUntil3 = new NetworkVariable<double>(-1d);

        Part _barrier, _gate, _door, _cabDoor, _switch, _clampP, _clampN, _b0, _b1, _b2, _b3;
        bool _resolved, _boothShown, _boothInit;
        float _strikeStart = -100f;
        float[] _lightIntensity = new float[0];
        RenderTexture[] _rt = new RenderTexture[2];
        readonly Collider[] _overlap = new Collider[16];

        // ------------------------------------------------------------------ derived (every peer)
        bool BasePowered => host != null && host.Powered;
        public bool BatteryIn => _battery.Value;
        public bool BoothPowered => _switchBatt.Value ? _battery.Value && _clampPos.Value && _clampNeg.Value : BasePowered;
        public bool DrivesPowered => BoothPowered || BasePowered;
        public bool BarrierUp => _barrierUp.Value;
        public bool GateOpen => _gateOpen.Value;

        void Awake()
        {
            Instance = this;
            if (GetComponent<ForestKppSounds>() == null) gameObject.AddComponent<ForestKppSounds>();   // power sounds, every peer
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            foreach (RenderTexture rt in _rt) if (rt != null) rt.Release();
            base.OnDestroy();
        }

        void Resolve()
        {
            if (_resolved || host == null) return;
            _resolved = true;
            _barrier = P(Barrier); _gate = P(Gate); _door = P(BoothDoor); _cabDoor = P(CabinetDoor); _switch = P(Switch);
            _clampP = P(ClampPos); _clampN = P(ClampNeg); _b0 = P(BtnBarrier); _b1 = P(BtnGate); _b2 = P(LeitBarrier); _b3 = P(LeitGate);
            _lightIntensity = new float[boothLights.Length];
            for (int i = 0; i < boothLights.Length; i++) _lightIntensity[i] = boothLights[i] != null ? boothLights[i].intensity : 0f;
            SetupCameras();
        }

        Part P(string id)
        {
            if (host.TryGetPart(id, out Part p)) return p;
            Debug.LogWarning($"[Snow] Forest KPP: part {id} not found on the host.");
            return null;
        }

        public override void OnNetworkSpawn()
        {
            Resolve();
            if (IsServer) ServerStart();
            Apply(true);
        }

        void ServerStart()
        {
            _battery.Value = false;
            _clampPos.Value = false;
            _clampNeg.Value = false;
            _switchBatt.Value = false;
            _barrierUp.Value = false;
            _gateOpen.Value = false;
            if (batteryPrefab != null && batterySpawn != null)
            {
                Vector3 at = batterySpawn.position + Vector3.up * 2f;
                if (Physics.Raycast(at, Vector3.down, out RaycastHit hit, 10f, ~0, QueryTriggerInteraction.Ignore)) at = hit.point + Vector3.up * 0.05f;
                NetworkObject no = Instantiate(batteryPrefab, at, batterySpawn.rotation);
                no.Spawn(true);
                BenchLog.Event("forest_kpp", "battery_spawn", at);
            }
            BenchLog.Event("forest_kpp", "start", true);
        }

        // ------------------------------------------------------------------ ILocationScript
        public bool Owns(string id) => id == BtnBarrier || id == BtnGate || id == LeitBarrier || id == LeitGate || id == Switch ||
                                       id == ClampPos || id == ClampNeg || id == BatterySlot;

        bool CabinetOpen => _cabDoor != null && _cabDoor.part.target > 0.5f;

        public bool ServerUse(ulong clientId, string id, UsePhase phase, out string reason)
        {
            reason = null;
            Resolve();
            if (phase != UsePhase.Press) return true;
            switch (id)
            {
                case BtnBarrier: return Press(0, BoothPowered, true, out reason);
                case BtnGate: return Press(1, BoothPowered, false, out reason);
                case LeitBarrier: return Press(2, BasePowered, true, out reason);
                case LeitGate: return Press(3, BasePowered, false, out reason);
                case Switch:
                    if (!CabinetOpen) { reason = "Дверца шкафа закрыта"; return false; }
                    _switchBatt.Value = !_switchBatt.Value;
                    BenchLog.Event("forest_kpp", "switch", _switchBatt.Value ? "BATTERIE" : "NETZ", "client", clientId);
                    return true;
                case ClampPos:
                case ClampNeg:
                {
                    if (!CabinetOpen) { reason = "Дверца шкафа закрыта"; return false; }
                    NetworkVariable<bool> c = id == ClampPos ? _clampPos : _clampNeg;
                    if (!c.Value && !_battery.Value) { reason = "Нет аккумулятора"; return false; }
                    c.Value = !c.Value;
                    BenchLog.Event("forest_kpp", "clamp", id == ClampPos ? "+" : "-", "on", c.Value, "client", clientId);
                    return true;
                }
                case BatterySlot: return UseSlot(clientId, out reason);
                default: reason = "Нет действия"; return false;
            }
        }

        bool Press(int button, bool powered, bool barrier, out string reason)
        {
            reason = null;
            Pressed(button).Value = NetworkManager.ServerTime.Time + PressSeconds;   // the cap goes in, with or without power
            if (!powered) { reason = "Нет тока"; Snow.Audio.SnowAudio.ServerPlay("kpp.console.button.dead", ButtonPos(button)); return false; }
            Part p = barrier ? _barrier : _gate;
            NetworkVariable<bool> v = barrier ? _barrierUp : _gateOpen;
            if (p == null) { reason = "Нет привода"; return false; }
            bool open = !v.Value;
            if (!open && !LeafFree(p, 0f)) { reason = "Мешает игрок"; return false; }
            v.Value = open;
            BenchLog.Event("forest_kpp", barrier ? "barrier" : "gate", open, "button", button);
            return true;
        }

        NetworkVariable<double> Pressed(int i) => i == 0 ? _pressedUntil0 : i == 1 ? _pressedUntil1 : i == 2 ? _pressedUntil2 : _pressedUntil3;

        bool UseSlot(ulong clientId, out string reason)
        {
            reason = null;
            if (!CabinetOpen) { reason = "Дверца шкафа закрыта"; return false; }
            if (batterySlot == null) { reason = "Нет полки"; return false; }
            CargoItem inSlot = batterySlot.Occupant;
            if (inSlot != null)
            {
                if (_clampPos.Value || _clampNeg.Value) { reason = "Сначала снимите клеммы"; return false; }
                inSlot.ServerUnstow();
                _battery.Value = false;
                BenchLog.Event("forest_kpp", "battery", "out", "client", clientId);
                return true;
            }
            CargoItem held = CargoItem.HeldBy(clientId);
            if (held == null || held.itemKey != batterySlot.accepts) { reason = "Нужен аккумулятор в руках"; return false; }
            if (!held.ServerStow(clientId, batterySlot, out reason)) return false;
            _battery.Value = true;
            BenchLog.Event("forest_kpp", "battery", "in", "client", clientId);
            return true;
        }

        public bool TryUnlock(ulong clientId, string id, out string reason)
        {
            reason = null;
            if (id != BoothDoor) return false;
            if (BoothPowered) return true;
            if (InsideBooth(clientId)) return true;                    // the lock's inside handle (user 23.10)
            reason = "Заперто электрозамком — нет тока";
            return false;
        }

        bool InsideBooth(ulong clientId)
        {
            if (booth == null || NetworkManager == null || !NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient c) || c.PlayerObject == null) return false;
            Vector3 p = booth.InverseTransformPoint(c.PlayerObject.transform.position);
            return Mathf.Abs(p.x) < 1.95f && Mathf.Abs(p.z) < 1.6f && p.y > -0.5f && p.y < 2.6f;
        }

        public float HoldSeconds(string id) => 0f;

        public string Describe(string id)
        {
            switch (id)
            {
                case BtnBarrier: return BoothPowered ? (BarrierUp ? "шлагбаум поднят" : "шлагбаум опущен") : "нет тока";
                case BtnGate: return BoothPowered ? (GateOpen ? "ворота открыты" : "ворота закрыты") : "нет тока";
                case LeitBarrier: return BasePowered ? (BarrierUp ? "шлагбаум поднят" : "шлагбаум опущен") : "нет тока";
                case LeitGate: return BasePowered ? (GateOpen ? "ворота открыты" : "ворота закрыты") : "нет тока";
                case Switch: return _switchBatt.Value ? "BATTERIE" : "NETZ";
                case ClampPos: return _clampPos.Value ? "надета" : "снята";
                case ClampNeg: return _clampNeg.Value ? "надета" : "снята";
                case BatterySlot: return _battery.Value ? "аккумулятор на полке" : "пусто";
                case BoothDoor: return host != null && host.TryGetPart(id, out Part d) && d.part.locked ? "заперта (нет тока)" : null;
                default: return null;
            }
        }

        public string ActionText(string id)
        {
            switch (id)
            {
                case BtnBarrier: case BtnGate: case LeitBarrier: case LeitGate: return "нажать";
                case Switch: return _switchBatt.Value ? "на NETZ" : "на BATTERIE";
                case ClampPos: return _clampPos.Value ? "снять" : "надеть";
                case ClampNeg: return _clampNeg.Value ? "снять" : "надеть";
                case BatterySlot: return _battery.Value ? "снять аккумулятор" : "поставить аккумулятор";
                default: return null;
            }
        }

        // ------------------------------------------------------------------ server
        void Update()
        {
            if (!IsSpawned) return;
            if (IsServer) ServerTick();
            Apply(false);
        }

        void ServerTick()
        {
            if (!_resolved) return;
            // the battery taken off the shelf some other way (grabbed): no battery, the clamps drop off
            if (_battery.Value && (batterySlot == null || batterySlot.Occupant == null))
            {
                _battery.Value = false;
                _clampPos.Value = false;
                _clampNeg.Value = false;
            }
            Drive(_barrier, _barrierUp.Value);
            Drive(_gate, _gateOpen.Value);
        }

        /// <summary>Server: the drive's target (the host publishes the bit). Without any power the part waits where it is.</summary>
        void Drive(Part p, bool open)
        {
            if (p?.part == null) return;
            float t = open ? 1f : 0f;
            if (!DrivesPowered)
            {
                // the power went mid-travel: the motor cuts out and the boom shudders where it is
                if (p.part.enabled && !Mathf.Approximately(p.part.openAmount, p.part.target) && p == _barrier)
                    Snow.Audio.SnowAudio.ServerPlay("kpp.barrier.arm.stall", p.part.transform.position);
                p.part.enabled = false;
                return;
            }
            if (!Mathf.Approximately(p.part.target, t)) p.part.target = t;
            if (!Mathf.Approximately(p.part.openAmount, p.part.target)) p.part.enabled = true;
        }

        /// <summary>Server: no player's capsule in the leaf's pose at <paramref name="amount"/> (as the host's L4.4 check).</summary>
        bool LeafFree(Part p, float amount)
        {
            SkyIslandInteractable d = p.part;
            Transform t = d.transform;
            Vector3 axis = d.axis.sqrMagnitude > 1e-8f ? d.axis.normalized : Vector3.up;
            float cur = Mathf.Lerp(d.min, d.max, d.openAmount), v = Mathf.Lerp(d.min, d.max, amount);
            Vector3 lp = t.localPosition;
            Quaternion lr = t.localRotation;
            if (d.kind == SkyIslandInteractable.Kind.Hinge) lr = Quaternion.AngleAxis(v - cur, axis) * lr;
            else if (d.kind == SkyIslandInteractable.Kind.Slide) lp += axis * (v - cur);
            Matrix4x4 parent = t.parent != null ? t.parent.localToWorldMatrix : Matrix4x4.identity;
            Matrix4x4 move = parent * Matrix4x4.TRS(lp, lr, t.localScale) * t.worldToLocalMatrix;
            foreach (Collider c in d.GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger || !c.enabled) continue;
                Bounds b = c is BoxCollider bc ? new Bounds(bc.center, bc.size) : c is MeshCollider mc && mc.sharedMesh != null ? mc.sharedMesh.bounds
                         : new Bounds(c.transform.InverseTransformPoint(c.bounds.center), c.bounds.size);
                Matrix4x4 m = move * c.transform.localToWorldMatrix;
                Vector3 s = c.transform.lossyScale;
                Vector3 half = Vector3.Scale(b.extents, new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z)));
                int n = Physics.OverlapBoxNonAlloc(m.MultiplyPoint3x4(b.center), half, _overlap, m.rotation, ~0, QueryTriggerInteraction.Ignore);
                for (int k = 0; k < n; k++)
                    if (_overlap[k] is CharacterController cc && cc.enabled) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ every peer: show the state
        void Apply(bool snap)
        {
            if (!_resolved) return;
            bool booth = BoothPowered, drives = DrivesPowered, base_ = BasePowered;
            double now = NetworkManager != null ? NetworkManager.ServerTime.Time : 0d;

            Show(_switch, _switchBatt.Value ? 1f : 0f, snap);
            Show(_clampP, _clampPos.Value ? 0f : 1f, snap);            // 1 = lifted off the post
            Show(_clampN, _clampNeg.Value ? 0f : 1f, snap);
            Show(_b0, now < _pressedUntil0.Value ? 1f : 0f, snap);
            Show(_b1, now < _pressedUntil1.Value ? 1f : 0f, snap);
            Show(_b2, now < _pressedUntil2.Value ? 1f : 0f, snap);
            Show(_b3, now < _pressedUntil3.Value ? 1f : 0f, snap);
            if (_door?.part != null) _door.part.locked = !booth;        // the hint says "заперта" on every peer

            Power(cabinet, booth);
            Power(doorStrike, booth);
            Power(boothMonitors[0], booth);
            Power(boothMonitors[1], booth);
            Power(leitMonitors[0], base_);
            Power(leitMonitors[1], base_);
            for (int i = 0; i < 2; i++) { Power(cameraHeads[i], drives); Power(floods[i], drives); }
            Power(btnBarrier, booth && BarrierUp);
            Power(btnGate, booth && GateOpen);
            Power(leitBtnBarrier, base_ && BarrierUp);
            Power(leitBtnGate, base_ && GateOpen);
            bool moving = _barrier?.part != null && !Mathf.Approximately(_barrier.part.openAmount, _barrier.part.target);
            if (beacon != null) { beacon.spinning = drives && moving; Power(beacon, drives && moving); }
            BoothLamps(booth);
            Cameras(booth, base_, drives);
        }

        static void Power(KitItem k, bool on) { if (k != null) k.SetPower(on); }

        Vector3 ButtonPos(int button)
        {
            KitItem k = button == 0 ? btnBarrier : button == 1 ? btnGate : button == 2 ? leitBtnBarrier : leitBtnGate;
            return k != null ? k.transform.position : transform.position;
        }

        static void Show(Part p, float t, bool snap)
        {
            if (p?.part == null) return;
            SkyIslandInteractable s = p.part;
            if (snap)
            {
                s.openAmount = t; s.target = t; s.Apply(); s.enabled = false;
            }
            else if (!Mathf.Approximately(s.target, t)) { s.target = t; s.enabled = true; }
        }

        /// <summary>The booth's batten and bulkhead with its power; the batten flickers a few seconds as it strikes.</summary>
        void BoothLamps(bool on)
        {
            if (!_boothInit || on != _boothShown)
            {
                if (on && _boothInit) _strikeStart = Time.time;
                _boothInit = true;
                _boothShown = on;
                foreach (GameObject g in boothGlows) if (g != null) g.SetActive(on);
                foreach (Light l in boothLights) if (l != null) l.enabled = on;
                SetLamps(on, on);
            }
            if (!on) return;
            float since = Time.time - _strikeStart;
            if (since < StrikeSeconds)
            {
                float k = Mathf.Lerp(flicker.x, flicker.y, Mathf.PerlinNoise(Time.time * flicker.z, 0.37f));
                bool lit = k > 0.5f;
                if (boothLights.Length > 0 && boothLights[0] != null) boothLights[0].intensity = _lightIntensity[0] * k;
                if (boothGlows.Length > 0 && boothGlows[0] != null) boothGlows[0].SetActive(lit);
                SetLamps(lit, true);
            }
            else if (boothLights.Length > 0 && boothLights[0] != null && boothLights[0].intensity != _lightIntensity[0])
            {
                boothLights[0].intensity = _lightIntensity[0];
                if (boothGlows.Length > 0 && boothGlows[0] != null) boothGlows[0].SetActive(true);
                SetLamps(true, true);
            }
        }

        void SetLamps(bool fluor, bool bulk)
        {
            foreach (Renderer r in lampRenderers)
            {
                if (r == null) continue;
                Material[] ms = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < ms.Length; i++)
                {
                    Material want = ms[i] == fluorOn || ms[i] == fluorOff ? (fluor ? fluorOn : fluorOff)
                                  : ms[i] == bulkOn || ms[i] == bulkOff ? (bulk ? bulkOn : bulkOff) : ms[i];
                    if (want != ms[i]) { ms[i] = want; changed = true; }
                }
                if (changed) r.sharedMaterials = ms;
            }
        }

        // ------------------------------------------------------------------ cameras -> monitors
        void SetupCameras()
        {
            for (int i = 0; i < 2; i++)
            {
                Camera cam = cameras[i];
                if (cam == null) continue;
                var rt = new RenderTexture(128, 96, 16) { name = "KPP_CAM_" + i, filterMode = FilterMode.Point, antiAliasing = 1 };
                rt.Create();
                _rt[i] = rt;
                cam.targetTexture = rt;
                cam.enabled = false;
                Material on = null;
                foreach (KitItem m in new[] { boothMonitors[i], leitMonitors[i] })
                {
                    if (m == null) continue;
                    if (on == null)
                    {
                        foreach (KitItem.Swap s in m.swaps)
                            if (s?.on != null && s.on.name.StartsWith("M_Kit_Screen_On", StringComparison.Ordinal)) { on = new Material(s.on) { name = "M_Kit_Screen_On_CAM" + i }; break; }
                        if (on == null) continue;
                        on.SetTexture("_BaseMap", rt);
                        on.SetTexture("_EmissionMap", rt);
                    }
                    m.ReplaceOn("M_Kit_Screen_On", on);
                }
            }
        }

        /// <summary>A camera renders (every 4th frame) while one of its monitors is lit and the local player is near it.</summary>
        void Cameras(bool booth, bool base_, bool drives)
        {
            PlayerNet me = PlayerNet.Local;
            Vector3 eye = me != null ? me.transform.position : Vector3.positiveInfinity;
            for (int i = 0; i < 2; i++)
            {
                Camera cam = cameras[i];
                if (cam == null) continue;
                bool want = drives && (Near(boothMonitors[i], booth, eye) || Near(leitMonitors[i], base_, eye));
                cam.enabled = want && (Time.frameCount + i * 2) % CameraEvery == 0;
            }
        }

        static bool Near(KitItem monitor, bool powered, Vector3 eye) =>
            monitor != null && powered && (monitor.transform.position - eye).sqrMagnitude < CameraRange * CameraRange;

        // ------------------------------------------------------------------ dev pult (host)
        /// <summary>Server, dev pult: the battery on the shelf, both clamps on, the switch at BATTERIE (or all back).</summary>
        public void DevBoothPower(bool on)
        {
            if (!IsServer) return;
            Resolve();
            if (on && batterySlot != null && batterySlot.Occupant == null)
                foreach (CargoItem c in CargoItem.All)
                    if (c != null && c.IsSpawned && c.itemKey == batterySlot.accepts && c.State != CargoState.Stowed && c.ServerStow(NetworkManager.ServerClientId, batterySlot, out _)) break;
            _battery.Value = batterySlot != null && batterySlot.Occupant != null;
            _clampPos.Value = on && _battery.Value;
            _clampNeg.Value = on && _battery.Value;
            _switchBatt.Value = on;
            BenchLog.Event("forest_kpp", "dev", on, "battery", _battery.Value);
        }
    }
}
