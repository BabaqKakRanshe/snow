using System;
using System.Collections.Generic;
using System.Globalization;
using SkyIsland;
using Snow.Cargo;
using Snow.Doors;
using Snow.Interaction;
using Snow.Net;
using Snow.Player;
using Unity.Netcode;
using UnityEngine;
using Part = Snow.Levels.SkyIslandLocationHost.Part;
using Category = Snow.Levels.SkyIslandLocationHost.Category;

namespace Snow.Levels.Forest
{
    /// <summary>
    /// The Forest opening "forest -> base -> first night" (FOREST_U3 F5–F7, appendix B; numbers TEMPORARY): fuel (hand
    /// pump -> jerrycan -> day tank -> generator), keys and locks, the door bar, the boiler, the steps S2, S6–S10, the
    /// night (X09–X11) and the map scares (X01, X05–X07), cleaning. A location script of the host on the Forest root
    /// (<see cref="ILocationScript"/>): the server decides and writes the network state below; every peer applies it to
    /// the scene in LateUpdate by fields only (no core methods on a client, BASEV2 U4.2) — gauges, levers, the STÖRUNG
    /// lamp, locks, the hut lantern, the padlock, decals. The server alone drives the generator lever with the core's
    /// Open / Close, so the grid recounts its three switches. One-off sounds go to everyone by RPC (an event, not state).
    /// </summary>
    public class ForestOpening : NetworkBehaviour, ILocationScript
    {
        // ------------------------------------------------------------------ ids (Forest_Layout.json, Forest_Opening.json)
        const string GenStart = "IX_GenStart_86", FillCap = "IX_GenFillCap_300", PumpLever = "IX_PumpLever_301";
        const string HeatPump = "IX_HeatPump_304", Burner = "IX_HeatBurner_305", Thermostat = "IX_HeatThermostat_306";
        const string FillValve = "IX_HeatFill_307", BurnerReset = "IX_BurnerReset_308", PressureGauge = "IX_HeatGauge_309";
        const string TempGauge = "IX_HeatTempGauge_310", DoorBar = "IX_MainDoorBar_311", MainDoor = "IX_MainDoor_1";
        const string HeatingStop = "IX_HeatingStop_131", Barrier = "IX_BarrierArm_82", Gate = "IX_Gate_81";
        const string GarageGate = "IX_GarageGate_18", HutDoor = "IX_HutDoor_1", BarracksDoor = "IX_Door_10";
        const string KitchenSwitch = "IX_ShutterSwitch_Kueche_129";
        const string PadlockZone = "Padlock_Barrier", PhoneZone = "Phone_KPP", HutJerrycan = "Jerrycan_Hut";
        /// <summary>The checkpoint's own power panel on the booth wall (user 21.10: the barrier is motorised and needs it).</summary>
        public const string KppPower = "KPP_Power";
        const string RoomBarracks = "Kaserne", RoomCorridorOG = "Flur_OG", RoomKitchen = "Kueche";
        const string Rag = "Item_Rag", Mop = "Item_Mop", Schranke = "Schranke";

        enum Step { S1, S2, S3, S4, S5, S6, S7, S8, S9, S10 }
        enum Scare { X01, X02, X03, X04, X05, X06, X07, X08, X09, X10, X11 }
        enum Sfx : byte { PhoneRing, Bell, Breath, RoofSteps, Knock, CorridorSteps, DieselStart, DieselStop, LanternOut, Hiss, BurnerIgnite, BurnerFault }

        // ------------------------------------------------------------------ rules (TEMPORARY, appendix B)
        const float PumpRate = 0.5f, FillRate = 1f, TankL = 60f, BurnLph = 3.5f;
        const float NozzleRadius = 0.45f, NozzleDrop = 0.7f, NeckReach = 1.2f, PumpLimit = 60f, FillLimit = 70f;
        const float PressureRate = 0.1f, SafetyBar = 2.5f, BurnerMinBar = 1f, BurnerOffBar = 0.8f;
        const float HeatRate = 0.25f, CoolRate = 0.02f, ColdC = 10f, MaxBoilerC = 85f;
        const float GaugeMaxBar = 4f, GaugeMaxC = 120f, SetMin = 5f, SetMax = 30f;
        const float CleanSeconds = 2f, PhoneRadius = 8f, RingSeconds = 8f, SleepSeconds = 3f;
        const int GrimeCount = 33, PrintCount = 23, TrashTotal = 63;

        public static ForestOpening Instance { get; private set; }

        [Header("Links (written by Tools / Snow / Integrate Location Level / Forest)")]
        public SkyIslandLocationHost host;
        public ForestItems items;
        public ForestBunks bunks;
        [Tooltip("Assets/Locations/Forest/Data/Forest_Opening.json")] public TextAsset opening;

        [Header("X01: the hut lantern (found by material in the editor: SkyIslandFlicker swaps it in play)")]
        public Renderer lanternGlass;
        public int lanternSlot = -1;
        public Material lanternOff;
        public GameObject[] lanternGlows = new GameObject[0];
        public Light[] lanternLights = new Light[0];

        [Header("STÖRUNG lamp: M_HeatFault <-> M_HeatFaultOn")]
        public Renderer[] faultRenderers = new Renderer[0];
        public int[] faultSlots = new int[0];
        public Material faultOff, faultOn;

        [Header("Decals: 33 grime (Forest_Dressing.json), then 23 X11 prints (Forest_Opening.json)")]
        public GameObject[] decals = new GameObject[0];
        public string[] decalIds = new string[0];
        [Tooltip("Static copy of the padlock on the barrier until it is unlocked")] public GameObject padlockProxy;

        [Header("Sound slots (OPEN: the user gives the clips; empty = the effect without sound)")]
        public AudioClip phoneRing;
        public AudioClip phoneBell, phoneBreath, roofSteps, shutterKnock, corridorSteps, barracksDoorCreak, lanternOut;
        public AudioClip dieselStart, dieselLoop, dieselStop, pumpCreak, fuelPour, safetyHiss, burnerIgnite, burnerFault, boilerLoop, trashRustle;

        [Header("TEMPORARY")]
        [Tooltip("Debug time multiplier for fuel burn and the boiler (QF7, QF11); 1 in a build")] public float debugRate = 1f;

        // ------------------------------------------------------------------ network state (server writes)
        readonly NetworkVariable<float> _dayTankL = new NetworkVariable<float>();
        readonly NetworkVariable<bool> _genRunning = new NetworkVariable<bool>();
        readonly NetworkVariable<bool> _pumping = new NetworkVariable<bool>();
        readonly NetworkVariable<float> _pressureBar = new NetworkVariable<float>();
        readonly NetworkVariable<float> _boilerC = new NetworkVariable<float>(ColdC);
        readonly NetworkVariable<float> _setpointC = new NetworkVariable<float>(SetMin);
        readonly NetworkVariable<byte> _burner = new NetworkVariable<byte>();          // 0 off, 1 STÖRUNG, 2 burning
        readonly NetworkVariable<bool> _pumpOn = new NetworkVariable<bool>();
        readonly NetworkVariable<bool> _burnerSwitch = new NetworkVariable<bool>();
        readonly NetworkVariable<bool> _fillOpen = new NetworkVariable<bool>();
        readonly NetworkVariable<bool> _capOpen = new NetworkVariable<bool>();
        readonly NetworkVariable<double> _resetUntil = new NetworkVariable<double>(-1d);
        readonly NetworkVariable<uint> _locks = new NetworkVariable<uint>();            // bit per locks[] entry, 1 = locked
        readonly NetworkVariable<bool> _padlockOn = new NetworkVariable<bool>(true);
        readonly NetworkVariable<ulong> _decalsHidden = new NetworkVariable<ulong>();
        readonly NetworkVariable<uint> _stepsDone = new NetworkVariable<uint>();
        readonly NetworkVariable<uint> _stepsSkipped = new NetworkVariable<uint>();
        readonly NetworkVariable<uint> _scaresDone = new NetworkVariable<uint>();
        readonly NetworkVariable<byte> _phase = new NetworkVariable<byte>();            // 0 evening, 1 night, 2 morning
        readonly NetworkVariable<bool> _kppPower = new NetworkVariable<bool>();

        /// <summary>0 evening, 1 night, 2 morning (the day / night cycle jumps to the morning with it).</summary>
        public byte Phase => _phase.Value;
        readonly NetworkVariable<double> _nightStart = new NetworkVariable<double>(-1d);
        readonly NetworkVariable<bool> _phoneRinging = new NetworkVariable<bool>();
        readonly NetworkVariable<bool> _lanternOn = new NetworkVariable<bool>(true);
        readonly NetworkVariable<int> _trash = new NetworkVariable<int>();

        // ------------------------------------------------------------------ resolved on spawn
        OpeningData _data;
        readonly HashSet<string> _owned = new HashSet<string>(StringComparer.Ordinal);
        readonly Dictionary<string, int> _decalIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        Part _genStart, _fillCap, _pumpLever, _heatPump, _burnerPart, _thermostat, _fillValve, _reset, _pressureGauge, _tempGauge;
        Part _bar, _mainDoor, _heatingStop, _barrier, _gate, _garageGate, _hutDoor, _barracksDoor, _kitchenSwitch;
        Part[] _lockParts = new Part[0];
        string[] _lockKeys = new string[0], _lockBy = new string[0];
        int _barLockBit = -1, _padlockBit = -1;
        uint _barMask;
        readonly List<SkyIslandInteractable> _curtains = new List<SkyIslandInteractable>();
        readonly List<SkyIslandInteractable> _kitchenCurtains = new List<SkyIslandInteractable>();
        readonly List<string> _curtainNames = new List<string>();
        Vector3 _nozzle, _neck, _phonePos, _bellPos;
        OpeningScare _x06, _x07, _x09;
        bool _resolved;

        // server-only
        ulong _pumpClient, _fillClient, _cleanClient;
        float _pumpStart, _fillStart, _cleanStart, _ringStart, _sleepSince = -1f, _slowTick;
        FuelCan _fillCan;
        bool _filling, _cleaning, _faultDone, _corridorOff;
        int _cleanIndex = -1;
        float _tank, _pressure, _boiler;                              // exact values; the variables carry them rounded

        // applied state (every peer)
        ulong _decalsShown = ulong.MaxValue;
        int _faultShown = -1, _phaseShown = -1;
        bool _lanternApplied, _padlockShown = true;
        float _gaugeBar = -1f, _gaugeC = -1f, _thermoShown = -1f;
        AudioSource _walker;
        Vector3[] _walkPath;
        int _walkSeg;
        float _walkT;

        static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

        // ------------------------------------------------------------------ setup
        void Awake()
        {
            Instance = this;
            foreach (string id in new[] { GenStart, FillCap, PumpLever, HeatPump, Burner, Thermostat, FillValve, BurnerReset, PressureGauge, TempGauge, PadlockZone, PhoneZone, KppPower })
                _owned.Add(id);
            for (int i = 0; i < decalIds.Length; i++)
                if (!string.IsNullOrEmpty(decalIds[i]))
                {
                    _owned.Add(decalIds[i]);
                    _decalIndex[decalIds[i]] = i;
                }
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        /// <summary>Parts by id from the host and the opening data; after every Awake (the host indexes its parts there).</summary>
        void Resolve()
        {
            if (_resolved || host == null) return;
            _resolved = true;
            _data = ForestData.Read<OpeningData>(opening) ?? new OpeningData();
            _genStart = P(GenStart); _fillCap = P(FillCap); _pumpLever = P(PumpLever); _heatPump = P(HeatPump); _burnerPart = P(Burner);
            _thermostat = P(Thermostat); _fillValve = P(FillValve); _reset = P(BurnerReset); _pressureGauge = P(PressureGauge);
            _tempGauge = P(TempGauge); _bar = P(DoorBar); _mainDoor = P(MainDoor); _heatingStop = P(HeatingStop); _barrier = P(Barrier);
            _gate = P(Gate); _garageGate = P(GarageGate); _hutDoor = P(HutDoor); _barracksDoor = P(BarracksDoor); _kitchenSwitch = P(KitchenSwitch);

            OpeningLock[] locks = _data.locks ?? new OpeningLock[0];
            int n = Mathf.Min(locks.Length, 32);
            _lockParts = new Part[n];
            _lockKeys = new string[n];
            _lockBy = new string[n];
            for (int i = 0; i < n; i++)
            {
                _lockParts[i] = P(locks[i].part);
                _lockKeys[i] = string.IsNullOrEmpty(locks[i].key) ? null : locks[i].key;
                _lockBy[i] = string.IsNullOrEmpty(locks[i].by) ? null : locks[i].by;
                if (_lockBy[i] == DoorBar) { _barLockBit = i; _barMask |= 1u << i; }
                if (_lockBy[i] == PadlockZone) _padlockBit = i;
            }

            foreach (Part p in host.parts)
                if (p != null && p.part != null && p.category == Category.Shutter)
                {
                    _curtains.Add(p.part);
                    _curtainNames.Add(p.part.name);
                    if (p.part.name.StartsWith("IX_Shutter_" + RoomKitchen + "_", StringComparison.Ordinal)) _kitchenCurtains.Add(p.part);
                }

            foreach (OpeningInteractable ia in _data.interactables ?? new OpeningInteractable[0])
            {
                if (ia == null) continue;
                if (ia.child == PumpLever && ia.nozzle != null) _nozzle = ForestRelocation.Point(ForestData.V3(ia.nozzle));
                if (ia.child == FillCap && ia.neck != null) _neck = ForestRelocation.Point(ForestData.V3(ia.neck));
            }
            foreach (OpeningScare s in _data.scares ?? new OpeningScare[0])
            {
                if (s == null) continue;
                if (s.id.StartsWith("X05", StringComparison.Ordinal)) { _phonePos = ForestRelocation.Point(ForestData.V3(s.p)); _bellPos = ForestRelocation.Point(ForestData.V3(s.bell)); }
                else if (s.id.StartsWith("X06", StringComparison.Ordinal)) _x06 = s;
                else if (s.id.StartsWith("X07", StringComparison.Ordinal)) _x07 = s;
                else if (s.id.StartsWith("X09", StringComparison.Ordinal)) _x09 = s;
            }
        }

        Part P(string id)
        {
            if (id != null && host.TryGetPart(id, out Part p)) return p;
            if (id != null) Debug.LogWarning($"[Snow] Forest opening: part {id} not found on the host.");
            return null;
        }

        public override void OnNetworkSpawn()
        {
            Resolve();
            if (IsServer) ServerStart();
            // scripted parts show their state at once (the toggles' 0 is -25°, not the model's middle pose)
            Snap(_pumpLever, 0.5f);
            ApplyState(true);
        }

        /// <summary>Server, once per session: the start of the evening (F5.3).</summary>
        void ServerStart()
        {
            uint locks = 0u;
            for (int i = 0; i < _lockParts.Length; i++)
                if (i != _barLockBit) locks |= 1u << i;
            _locks.Value = locks;
            _padlockOn.Value = true;
            ulong prints = 0UL;
            for (int i = GrimeCount; i < GrimeCount + PrintCount && i < 64; i++) prints |= 1UL << i;
            _decalsHidden.Value = prints;                              // X11 prints wait for the morning
            _tank = 0f; _pressure = 0f; _boiler = ColdC;
            _dayTankL.Value = 0f;
            _genRunning.Value = false;
            _pumping.Value = false;
            _pressureBar.Value = 0f;
            _boilerC.Value = ColdC;
            _setpointC.Value = SetMin;
            _burner.Value = 0;
            _pumpOn.Value = false;
            _burnerSwitch.Value = false;
            _fillOpen.Value = false;
            _capOpen.Value = false;
            _resetUntil.Value = -1d;
            _stepsDone.Value = 0u;
            _stepsSkipped.Value = 0u;
            _scaresDone.Value = 0u;
            _phase.Value = 0;
            _kppPower.Value = false;
            _nightStart.Value = -1d;
            _phoneRinging.Value = false;
            _lanternOn.Value = true;
            _trash.Value = 0;
            _filling = _cleaning = _faultDone = _corridorOff = false;
            _sleepSince = -1f;
            // the hut door stands open by design; the builder and the menu leave doors closed
            if (_hutDoor?.door != null) _hutDoor.door.Command(true, DoorDrive.Manual);
            // the base looks dead at the start (user 21.10): every shutter down, its switches and straps at "down"
            int shut = 0;
            foreach (SkyIslandLocationHost.Part sp in host.parts)
            {
                if (sp?.part == null) continue;
                if (sp.category == SkyIslandLocationHost.Category.Shutter)
                {
                    sp.part.openAmount = 0f;
                    sp.part.target = 0f;
                    sp.part.Apply();
                    shut++;
                }
                else if (sp.category == SkyIslandLocationHost.Category.ShutterSwitch || sp.category == SkyIslandLocationHost.Category.ShutterWinder)
                {
                    sp.part.openAmount = 0f;
                    sp.part.target = 0f;
                    sp.part.Apply();
                }
            }
            BenchLog.Event("forest_step", "start", true, "locks", locks, "shutters_down", shut);
        }

        // ------------------------------------------------------------------ ILocationScript
        public bool Owns(string id) => id != null && _owned.Contains(id);

        public bool ServerUse(ulong clientId, string id, UsePhase phase, out string reason)
        {
            reason = null;
            Resolve();
            if (_decalIndex.TryGetValue(id, out int decal)) return UseDecal(clientId, decal, phase, out reason);
            switch (id)
            {
                case PumpLever:
                    if (phase == UsePhase.Press)
                    {
                        _pumpClient = clientId;
                        _pumpStart = Time.time;
                        _pumping.Value = true;
                    }
                    else if (phase == UsePhase.Release && clientId == _pumpClient) _pumping.Value = false;
                    return true;
                case FillCap: return UseFillCap(clientId, phase, out reason);
                case GenStart:
                    if (phase != UsePhase.Press) return true;
                    if (_genRunning.Value) { StopGenerator("lever"); return true; }
                    if (_tank <= 0f) { reason = "Бак пуст"; return false; }
                    _genStart?.part.Open();                            // the grid recounts its switches
                    _genRunning.Value = true;
                    Sound(Sfx.DieselStart, _genStart != null ? _genStart.part.transform.position : transform.position);
                    BenchLog.Event("forest_power", "generator", true, "tankL", _tank);
                    return true;
                case HeatPump:
                    if (phase != UsePhase.Press) return true;
                    if (!host.Powered) { reason = "Нет питания"; return false; }
                    _pumpOn.Value = !_pumpOn.Value;
                    BenchLog.Event("forest_heat", "pump", _pumpOn.Value);
                    return true;
                case Burner: return UseBurner(phase, out reason);
                case Thermostat:
                    if (phase == UsePhase.Release) return true;
                    _setpointC.Value = Mathf.Clamp(_setpointC.Value + (phase == UsePhase.Back ? -1f : 1f), SetMin, SetMax);
                    BenchLog.Event("forest_heat", "setpoint", _setpointC.Value);
                    return true;
                case FillValve:
                    if (phase != UsePhase.Press) return true;
                    _fillOpen.Value = !_fillOpen.Value;
                    BenchLog.Event("forest_heat", "fill", _fillOpen.Value, "bar", _pressure);
                    return true;
                case BurnerReset:
                    if (phase != UsePhase.Press) return true;
                    _resetUntil.Value = NetworkManager.ServerTime.Time + 0.3d;
                    if (_burner.Value == 1)
                    {
                        _burner.Value = 0;
                        _burnerSwitch.Value = false;
                        _faultDone = true;
                        BenchLog.Event("forest_heat", "reset", true);
                    }
                    return true;
                case PadlockZone: return UsePadlock(clientId, phase, out reason);
                case KppPower:
                    if (phase != UsePhase.Press) return true;
                    _kppPower.Value = !_kppPower.Value;
                    BenchLog.Event("forest_power", "kpp", _kppPower.Value, "client", clientId);
                    return true;
                case PhoneZone:
                    if (phase != UsePhase.Press) return true;
                    if (!_phoneRinging.Value) { reason = "Тишина в трубке"; return false; }
                    _phoneRinging.Value = false;
                    Sound(Sfx.Breath, _phonePos);
                    BenchLog.Event("forest_scare", "id", "X05", "picked_up", true);
                    return true;
                default:
                    if (phase == UsePhase.Release) return true;
                    reason = "Нет действия";
                    return false;
            }
        }

        bool UseFillCap(ulong clientId, UsePhase phase, out string reason)
        {
            reason = null;
            if (phase == UsePhase.Release)
            {
                if (_filling && clientId == _fillClient) StopFill("released");
                return true;
            }
            if (phase != UsePhase.Press) return true;
            CargoItem held = CargoItem.HeldBy(clientId);
            if (held != null && held.TryGetComponent(out FuelCan can))
            {
                // with a can in hand (the left button, F4.5): pour
                if (!_capOpen.Value) { reason = "Крышка закрыта"; return false; }
                if (can.FuelL <= 0.01f) { reason = "Канистра пуста"; return false; }
                if (_tank >= TankL - 0.01f) { reason = "Бак полон"; return false; }
                if (Vector3.Distance(can.transform.position, _neck) > NeckReach) { reason = "Поднесите канистру к горловине"; return false; }
                _filling = true;
                _fillClient = clientId;
                _fillCan = can;
                _fillStart = Time.time;
                BenchLog.Event("forest_fuel", "fill", true, "client", clientId, "can", can.FuelL, "tank", _tank);
                return true;
            }
            _capOpen.Value = !_capOpen.Value;
            if (!_capOpen.Value && _filling) StopFill("cap closed");
            BenchLog.Event("forest_fuel", "cap", _capOpen.Value);
            return true;
        }

        bool UseBurner(UsePhase phase, out string reason)
        {
            reason = null;
            if (phase != UsePhase.Press) return true;
            if (_burner.Value == 1) { reason = "Авария горелки — нажмите сброс"; return false; }
            if (_burnerSwitch.Value)
            {
                _burner.Value = 0;
                _burnerSwitch.Value = false;
                BenchLog.Event("forest_heat", "burner", "off");
                return true;
            }
            if (!host.Powered) { reason = "Нет питания"; return false; }
            if (_heatingStop != null && _heatingStop.part.target < 0.5f) { reason = "Аварийный выключатель отопления выключен"; return false; }
            if (_pressure < BurnerMinBar) { reason = "Мало давления"; return false; }
            if (!_pumpOn.Value) { reason = "Насос выключен"; return false; }
            _burnerSwitch.Value = true;
            Vector3 at = _burnerPart != null ? _burnerPart.part.transform.position : transform.position;
            if (!_faultDone)
            {
                _burner.Value = 1;                                     // the first start of the session goes to STÖRUNG
                Sound(Sfx.BurnerFault, at);
                BenchLog.Event("forest_heat", "burner", "fault");
            }
            else
            {
                _burner.Value = 2;
                Sound(Sfx.BurnerIgnite, at);
                BenchLog.Event("forest_heat", "burner", "on", "bar", _pressure);
            }
            return true;
        }

        bool UsePadlock(ulong clientId, UsePhase phase, out string reason)
        {
            reason = null;
            if (phase != UsePhase.Press) return true;
            if (!_padlockOn.Value) { reason = "Замок уже снят"; return false; }
            if (!HasKey(clientId, Schranke)) { reason = "Заперто на навесной замок"; return false; }
            _padlockOn.Value = false;
            if (_padlockBit >= 0) _locks.Value &= ~(1u << _padlockBit);
            // the static copy goes, a real padlock drops in its place
            OpeningItem pad = FindItem(ForestItems.PadlockId);
            if (pad != null && items != null)
                items.Spawn(pad.id, pad.model, ForestRelocation.Point(ForestData.V3(pad.p)), ForestRelocation.Rotation(ForestData.V3(pad.p), ForestData.Q(pad.q)), pad.massKg);
            BenchLog.Event("forest_lock", "part", Barrier, "by", PadlockZone, "client", clientId, "unlocked", true);
            return true;
        }

        bool UseDecal(ulong clientId, int index, UsePhase phase, out string reason)
        {
            reason = null;
            if (phase == UsePhase.Release)
            {
                if (_cleaning && clientId == _cleanClient && index == _cleanIndex) _cleaning = false;   // let go early: nothing
                return true;
            }
            if (phase != UsePhase.Press) return true;
            if ((_decalsHidden.Value & (1UL << index)) != 0) { reason = "Уже чисто"; return false; }
            if (!HoldsCleaner(clientId)) { reason = "Нужна тряпка или швабра"; return false; }
            _cleaning = true;
            _cleanClient = clientId;
            _cleanIndex = index;
            _cleanStart = Time.time;
            return true;
        }

        public bool TryUnlock(ulong clientId, string id, out string reason)
        {
            reason = null;
            Resolve();
            if (id == DoorBar) { reason = "Сначала закройте дверь"; return false; }
            if (id == Barrier && !_padlockOn.Value && !_kppPower.Value) { reason = "Нет питания КПП — щиток на стене будки"; return false; }
            uint locks = _locks.Value, before = locks;
            bool any = false;
            string key = HeldKey(clientId);
            for (int i = 0; i < _lockParts.Length; i++)
            {
                if (_lockParts[i] == null || _lockParts[i].id != id) continue;
                any = true;
                // a key opens only its own key entries; entries with "by" go through their mechanism (padlock, bar)
                if ((locks & (1u << i)) != 0 && _lockBy[i] == null && HasKey(clientId, _lockKeys[i])) locks &= ~(1u << i);
            }
            if (!any) return false;
            if (locks != before)
            {
                _locks.Value = locks;
                BenchLog.Event("forest_lock", "part", id, "key", key, "client", clientId, "unlocked", true);
            }
            for (int i = 0; i < _lockParts.Length; i++)
            {
                if (_lockParts[i] == null || _lockParts[i].id != id || (locks & (1u << i)) == 0) continue;
                reason = _lockBy[i] == DoorBar ? "Заперто на засов" : _lockBy[i] == PadlockZone ? "Заперто на навесной замок" : "Заперто";
                return false;
            }
            Part part = _lockParts.Length > 0 ? FindLockPart(id) : null;
            if (part != null) part.part.locked = false;                 // the host checks the lock again before it toggles
            return true;
        }

        Part FindLockPart(string id)
        {
            for (int i = 0; i < _lockParts.Length; i++)
                if (_lockParts[i] != null && _lockParts[i].id == id) return _lockParts[i];
            return null;
        }

        public float HoldSeconds(string id) => _decalIndex.ContainsKey(id) ? CleanSeconds : 0f;

        public string Describe(string id)
        {
            if (id == null) return null;
            if (_decalIndex.ContainsKey(id)) return null;
            switch (id)
            {
                case GenStart:
                    if (_genRunning.Value) return string.Format(Ru, "работает; в баке {0:0.#} л", _dayTankL.Value);
                    return _dayTankL.Value <= 0.01f ? "остановлен; бак пуст" : string.Format(Ru, "остановлен; в баке {0:0.#} л", _dayTankL.Value);
                case FillCap:
                    return string.Format(Ru, "{0}; в баке {1:0.#} л", _capOpen.Value ? "открыта" : "закрыта", _dayTankL.Value);
                case PumpLever:
                {
                    FuelCan can = CanUnderNozzle();
                    return can != null ? string.Format(Ru, "под пистолетом канистра {0:0.#} / {1:0} л", can.FuelL, can.capacityL)
                                       : "нет канистры под пистолетом";
                }
                case HeatPump: return _pumpOn.Value ? "включён" : "выключен";
                case Burner: return _burner.Value == 2 ? "горит" : _burner.Value == 1 ? "STÖRUNG" : "выключена";
                case Thermostat: return string.Format(Ru, "{0:0} °C", _setpointC.Value);
                case FillValve: return string.Format(Ru, "{0}; {1:0.0} бар", _fillOpen.Value ? "открыт" : "закрыт", _pressureBar.Value);
                case PadlockZone: return _padlockOn.Value ? "заперт" : "снят";
                case KppPower: return _kppPower.Value ? "включён" : "выключен";
                case PhoneZone: return _phoneRinging.Value ? "звонит" : null;
                case DoorBar:
                    if (_bar == null) return null;
                    if (_bar.part.target < 0.5f) return "опущен — дверь заперта";
                    return _mainDoor?.door != null && _mainDoor.door.State != DoorState.Closed ? "поднят (опустить — при закрытой двери)" : "поднят";
                case MainDoor:
                    return _barLockBit >= 0 && (_locks.Value & (1u << _barLockBit)) != 0 ? "заперта на засов" : null;
                default: return null;
            }
        }

        public string ActionText(string id)
        {
            if (id == null) return null;
            if (_decalIndex.ContainsKey(id)) return "держать: оттереть";
            switch (id)
            {
                case GenStart: return _genRunning.Value ? "остановить" : "запустить";
                case FillCap:
                {
                    PlayerNet me = PlayerNet.Local;
                    bool can = me != null && me.sensor != null && me.sensor.Held != null && me.sensor.Held.TryGetComponent(out FuelCan _);
                    if (can && _capOpen.Value) return "держать: залить";
                    return _capOpen.Value ? "закрыть" : "открыть";
                }
                case PumpLever: return "держать: качать";
                case HeatPump: return _pumpOn.Value ? "выключить" : "включить";
                case Burner: return _burnerSwitch.Value ? "выключить" : "включить";
                case Thermostat: return "колесо: больше / меньше";
                case FillValve: return _fillOpen.Value ? "закрыть" : "открыть";
                case BurnerReset: return "нажать";
                case PadlockZone: return "отпереть ключом";
                case KppPower: return _kppPower.Value ? "выключить" : "включить";
                case PhoneZone: return "снять трубку";
                default: return null;
            }
        }

        // ------------------------------------------------------------------ server tick
        void Update()
        {
            if (!IsSpawned) return;
            if (IsServer) ServerTick();
            Walk();
        }

        void ServerTick()
        {
            if (!_resolved) return;
            float dt = Time.deltaTime;
            float rateDt = dt * Mathf.Max(0f, debugRate);

            // the bar locks the main door while it is down (computed, no key opens it)
            if (_bar != null && _barLockBit >= 0)
            {
                uint locks = _locks.Value;
                uint want = _bar.part.target < 0.5f ? locks | _barMask : locks & ~_barMask;
                if (want != locks) _locks.Value = want;
            }

            // hand pump into a jerrycan under the nozzle (held is checked each tick: a lost Release does not pump on)
            if (_pumping.Value)
            {
                if (!Holding(_pumpClient, PumpLever) || Time.time - _pumpStart > PumpLimit) _pumping.Value = false;
                else
                {
                    FuelCan can = CanUnderNozzle();
                    if (can != null && can.ServerAdd(PumpRate * dt) > 0f && Time.frameCount % 30 == 0)
                        BenchLog.Event("forest_fuel", "pump", can.FuelL, "can", can.name);
                }
            }

            // pouring into the day tank
            if (_filling)
            {
                if (_fillCan == null || !_fillCan.IsSpawned || !Holding(_fillClient, FillCap) || CargoItem.HeldBy(_fillClient) != _fillCan.Item
                    || !_capOpen.Value || Vector3.Distance(_fillCan.transform.position, _neck) > NeckReach || Time.time - _fillStart > FillLimit)
                    StopFill("check");
                else
                {
                    float move = Mathf.Min(FillRate * dt, _fillCan.FuelL, TankL - _tank);
                    if (move <= 0f) StopFill(_fillCan.FuelL <= 0f ? "can empty" : "tank full");
                    else
                    {
                        _fillCan.ServerAdd(-move);
                        _tank += move;
                    }
                }
            }

            // the generator burns its day tank
            if (_genRunning.Value)
            {
                _tank = Mathf.Max(0f, _tank - BurnLph / 3600f * rateDt);
                if (_tank <= 0f) StopGenerator("empty");
            }
            else if (_genStart != null && _genStart.part.target > 0.5f && _tank <= 0f) _genStart.part.Close();

            // boiler (appendix B)
            if (_fillOpen.Value)
            {
                float was = _pressure;
                _pressure = Mathf.Min(SafetyBar, _pressure + PressureRate * rateDt);
                if (was < SafetyBar && _pressure >= SafetyBar) Sound(Sfx.Hiss, _fillValve != null ? _fillValve.part.transform.position : transform.position);
            }
            if (_burner.Value == 2 && (!host.Powered || (_heatingStop != null && _heatingStop.part.target < 0.5f) || _pressure < BurnerOffBar))
            {
                _burner.Value = 0;
                _burnerSwitch.Value = false;
                BenchLog.Event("forest_heat", "burner", "off", "power", host.Powered, "bar", _pressure);
            }
            if (_burner.Value == 2)
            {
                float top = Mathf.Min(MaxBoilerC, Mathf.Max(40f, 35f + 1.5f * _setpointC.Value));
                _boiler = Mathf.MoveTowards(_boiler, top, HeatRate * rateDt);
            }
            else _boiler = Mathf.MoveTowards(_boiler, ColdC, CoolRate * rateDt);

            // cleaning: done on the server as soon as the hold reaches 2 s
            if (_cleaning)
            {
                if (!Holding(_cleanClient, decalIds[_cleanIndex]) || !HoldsCleaner(_cleanClient)) _cleaning = false;
                else if (Time.time - _cleanStart >= CleanSeconds)
                {
                    _cleaning = false;
                    _decalsHidden.Value |= 1UL << _cleanIndex;
                    LogClean();
                }
            }

            // the rounded values out (no message every frame for a 0.001 change)
            Publish(_dayTankL, _tank, 0.05f);
            Publish(_pressureBar, _pressure, 0.01f);
            Publish(_boilerC, _boiler, 0.1f);

            // steps, scares, the night: 5 times a second
            _slowTick -= dt;
            if (_slowTick > 0f) return;
            _slowTick = 0.2f;
            Steps();
            Scares();
            Night();
        }

        static void Publish(NetworkVariable<float> v, float exact, float step)
        {
            if (Mathf.Abs(v.Value - exact) >= step || (exact == 0f && v.Value != 0f)) v.Value = exact;
        }

        void StopFill(string why)
        {
            _filling = false;
            BenchLog.Event("forest_fuel", "fill", false, "why", why, "tank", _tank, "can", _fillCan != null ? _fillCan.FuelL : 0f);
            _fillCan = null;
        }

        void StopGenerator(string why)
        {
            _genRunning.Value = false;
            _genStart?.part.Close();                                   // the grid goes down with it
            Sound(Sfx.DieselStop, _genStart != null ? _genStart.part.transform.position : transform.position);
            BenchLog.Event("forest_power", "generator", false, "why", why, "tankL", _tank);
        }

        void LogClean()
        {
            ulong bits = _decalsHidden.Value;
            int grime = 0, prints = 0;
            for (int i = 0; i < GrimeCount; i++) if ((bits & (1UL << i)) != 0) grime++;
            if (_phase.Value == 2)
                for (int i = GrimeCount; i < GrimeCount + PrintCount; i++) if ((bits & (1UL << i)) != 0) prints++;
            BenchLog.Event("forest_clean", "trash", _trash.Value, "of", TrashTotal, "grime", grime, "of_grime", GrimeCount, "prints", prints, "of_prints", PrintCount);
        }

        /// <summary>Server: a rubbish item went into the bag (TrashBag).</summary>
        public void ServerTrashed(CargoItem item)
        {
            _trash.Value++;
            LogClean();
        }

        // ------------------------------------------------------------------ steps (F5.7)
        void Steps()
        {
            uint done = _stepsDone.Value;
            NetworkObject can = items != null ? items.Find(HutJerrycan) : null;
            if (can != null && can.TryGetComponent(out CargoItem c) && c.State == CargoState.Carried) done = Mark(done, Step.S2);
            if (_barrier?.door != null && _barrier.door.State == DoorState.Open && _gate != null && _gate.part.target > 0.5f && _gate.part.openAmount > 0.99f)
                done = Mark(done, Step.S6);
            if (_genRunning.Value && host.Powered) done = Mark(done, Step.S7);
            if (_burner.Value == 2 && _setpointC.Value >= 18f) done = Mark(done, Step.S8);
            if (_bar != null && _bar.part.target < 0.5f && AllCurtainsDown() && _garageGate?.door != null && _garageGate.door.State == DoorState.Closed)
                done = Mark(done, Step.S9);
            if (done != _stepsDone.Value)
            {
                _stepsDone.Value = done;
                // the car's steps are DEFERRED: once the players are in, they are skipped
                if ((done & Bit(Step.S6)) != 0 && _stepsSkipped.Value == 0u)
                {
                    _stepsSkipped.Value = Bit(Step.S1) | Bit(Step.S3) | Bit(Step.S4) | Bit(Step.S5);
                    BenchLog.Event("forest_step", "skipped", "S1,S3,S4,S5");
                }
            }
        }

        uint Mark(uint done, Step s)
        {
            uint b = Bit(s);
            if ((done & b) != 0) return done;
            BenchLog.Event("forest_step", "step", s.ToString(), "t", Time.time);
            return done | b;
        }

        static uint Bit(Step s) => 1u << (int)s;
        static uint Bit(Scare s) => 1u << (int)s;

        bool AllCurtainsDown()
        {
            for (int i = 0; i < _curtains.Count; i++)
                if (_curtains[i] != null && _curtains[i].openAmount > 0.01f) return false;
            return _curtains.Count > 0;
        }

        // ------------------------------------------------------------------ scares on the map (F6.3)
        bool ScareDone(Scare s) => (_scaresDone.Value & Bit(s)) != 0;

        void MarkScare(Scare s)
        {
            _scaresDone.Value |= Bit(s);
            BenchLog.Event("forest_scare", "id", s.ToString(), "t", Time.time);
        }

        void Scares()
        {
            // X01: the first grab of the hut jerrycan puts the lantern out for good
            if (!ScareDone(Scare.X01))
            {
                NetworkObject can = items != null ? items.Find(HutJerrycan) : null;
                if (can != null && can.TryGetComponent(out CargoItem c) && c.State == CargoState.Carried)
                {
                    _lanternOn.Value = false;
                    Sound(Sfx.LanternOut, lanternGlass != null ? lanternGlass.transform.position : transform.position);
                    MarkScare(Scare.X01);
                }
            }

            // X05: someone within 8 m of the booth phone: two rings, inside and the bell outside
            if (!ScareDone(Scare.X05) && AnyPlayerNear(_phonePos, PhoneRadius))
            {
                _phoneRinging.Value = true;
                _ringStart = Time.time;
                Sound(Sfx.PhoneRing, _phonePos);
                Sound(Sfx.Bell, _bellPos);
                MarkScare(Scare.X05);
            }
            if (_phoneRinging.Value && Time.time - _ringStart > RingSeconds) _phoneRinging.Value = false;

            // X06: steps over the roof while someone is in the stair tower before the power is on
            if (!ScareDone(Scare.X06) && _x06?.box?.size != null && (_stepsDone.Value & Bit(Step.S7)) == 0 && AnyPlayerIn(_x06.box.p, _x06.box.q, _x06.box.size))
            {
                Sound(Sfx.RoofSteps, Vector3.zero);
                MarkScare(Scare.X06);
            }

            // X07: after S9, three knocks on a lowered shutter of a room nobody is in
            if (!ScareDone(Scare.X07) && (_stepsDone.Value & Bit(Step.S9)) != 0 && _x07?.rooms != null)
            {
                SkyIslandInteractable curtain = KnockTarget();
                if (curtain != null)
                {
                    Sound(Sfx.Knock, curtain.transform.position);
                    MarkScare(Scare.X07);
                }
            }
        }

        SkyIslandInteractable KnockTarget()
        {
            foreach (string room in _x07.rooms)
            {
                ForestOrientedRoom box = Room(room);
                if (box == null || AnyPlayerIn(box.p, box.q, box.size)) continue;
                string prefix = "IX_Shutter_" + room + "_";
                for (int i = 0; i < _curtains.Count; i++)
                    if (_curtains[i] != null && _curtainNames[i].StartsWith(prefix, StringComparison.Ordinal) && _curtains[i].openAmount <= 0.01f)
                        return _curtains[i];
            }
            return null;
        }

        ForestOrientedRoom Room(string name)
        {
            if (_data.rooms != null)
                foreach (ForestOrientedRoom r in _data.rooms)
                    if (r != null && r.name == name) return r;
            return null;
        }

        // ------------------------------------------------------------------ the night (F6.2)
        void Night()
        {
            NetworkManager nm = NetworkManager;
            if (_phase.Value == 0)
            {
                int players = 0, lying = 0;
                for (int i = 0; i < nm.ConnectedClientsList.Count; i++)
                {
                    if (nm.ConnectedClientsList[i].PlayerObject == null) continue;
                    players++;
                    if (bunks != null && bunks.InBunk(nm.ConnectedClientsList[i].ClientId)) lying++;
                }
                if (players == 0 || lying < players) { _sleepSince = -1f; return; }
                if (_sleepSince < 0f) _sleepSince = Time.time;
                if (Time.time - _sleepSince < SleepSeconds) return;
                _stepsDone.Value = Mark(_stepsDone.Value, Step.S10);
                _phase.Value = 1;
                _nightStart.Value = nm.ServerTime.Time;
                _corridorOff = false;
                SetRoom(RoomBarracks, false);
                SetRoom(RoomCorridorOG, false);
                BenchLog.Event("forest_step", "night", true, "players", players);
                return;
            }
            if (_phase.Value != 1) return;
            double t = nm.ServerTime.Time - _nightStart.Value;
            if (t >= 6d && !ScareDone(Scare.X09))
            {
                if (host.Powered) SetRoom(RoomCorridorOG, true);       // without power: only the steps
                Sound(Sfx.CorridorSteps, Vector3.zero);
                MarkScare(Scare.X09);
            }
            if (t >= 26d && !ScareDone(Scare.X10))
            {
                if (_barracksDoor?.door != null) _barracksDoor.door.Command(true, DoorDrive.Powered);   // whole and slow: 6 s
                MarkScare(Scare.X10);
            }
            if (t >= 31d && !_corridorOff)
            {
                _corridorOff = true;
                SetRoom(RoomCorridorOG, false);
            }
            if (t >= 45d)
            {
                _phase.Value = 2;                                      // morning: X11
                _decalsHidden.Value &= ~PrintMask();
                if (_kitchenSwitch != null) _kitchenSwitch.part.target = 1f;   // a bit of the host: replicated
                ShuttersUp();
                MarkScare(Scare.X11);
                BenchLog.Event("forest_step", "morning", true);
            }
        }

        static ulong PrintMask()
        {
            ulong m = 0UL;
            for (int i = GrimeCount; i < GrimeCount + PrintCount && i < 64; i++) m |= 1UL << i;
            return m;
        }

        void SetRoom(string room, bool on)
        {
            if (host.lights != null) host.lights.SetRoom(room, on);
        }

        /// <summary>Every peer: the kitchen curtains up by fields (with a motor and no power the core would not move them
        /// on a client), the switch to 1.</summary>
        void ShuttersUp()
        {
            foreach (SkyIslandInteractable c in _kitchenCurtains)
            {
                if (c == null) continue;
                c.openAmount = 1f;
                c.target = 1f;
                c.Apply();
                c.enabled = false;
            }
            if (_kitchenSwitch != null && !IsServer)
            {
                _kitchenSwitch.part.target = 1f;
                _kitchenSwitch.part.enabled = true;
            }
        }

        // ------------------------------------------------------------------ every peer: state into the scene
        void LateUpdate()
        {
            if (!IsSpawned) return;
            ApplyState(false);
        }

        void ApplyState(bool snap)
        {
            if (!_resolved) return;

            // gauges and the thermostat follow their values; the pump lever rocks while pumping
            float bar = _pressureBar.Value, c = _boilerC.Value, set = _setpointC.Value;
            if (snap || bar != _gaugeBar) { _gaugeBar = bar; Snap(_pressureGauge, bar / GaugeMaxBar); }
            if (snap || c != _gaugeC) { _gaugeC = c; Snap(_tempGauge, c / GaugeMaxC); }
            if (snap || set != _thermoShown) { _thermoShown = set; Snap(_thermostat, (set - SetMin) / (SetMax - SetMin)); }
            if (_pumping.Value) Snap(_pumpLever, 0.5f + 0.5f * Mathf.Sin(Time.time * 6f));
            else if (_pumpLever != null && !Mathf.Approximately(_pumpLever.part.openAmount, 0.5f)) Snap(_pumpLever, 0.5f);

            // toggles, cap and valve go by the core (the part's own Update); the generator lever only on clients
            Target(_fillCap, _capOpen.Value, snap);
            Target(_fillValve, _fillOpen.Value, snap);
            Target(_heatPump, _pumpOn.Value, snap);
            Target(_burnerPart, _burnerSwitch.Value, snap);
            Target(_reset, NetworkManager.ServerTime.Time < _resetUntil.Value, snap);
            if (!IsServer) Target(_genStart, _genRunning.Value, snap);

            // the STÖRUNG lamp
            int fault = _burner.Value == 1 ? 1 : 0;
            if (fault != _faultShown)
            {
                _faultShown = fault;
                Material want = fault == 1 ? faultOn : faultOff;
                for (int i = 0; i < faultRenderers.Length && i < faultSlots.Length; i++)
                {
                    Renderer r = faultRenderers[i];
                    if (r == null || want == null) continue;
                    Material[] mats = r.sharedMaterials;
                    if (faultSlots[i] < 0 || faultSlots[i] >= mats.Length) continue;
                    mats[faultSlots[i]] = want;
                    r.sharedMaterials = mats;
                }
            }

            // locks on every peer, so "заперта" is the same everywhere; the bar cannot go down on an open door
            uint locks = _locks.Value;
            for (int i = 0; i < _lockParts.Length; i++)
            {
                Part p = _lockParts[i];
                if (p == null) continue;
                bool locked = false;
                for (int k = 0; k < _lockParts.Length; k++)
                    if (_lockParts[k] == p && (locks & (1u << k)) != 0) { locked = true; break; }
                p.part.locked = locked;
            }
            if (_bar != null)
                _bar.part.locked = _bar.part.target > 0.5f && _mainDoor?.door != null && _mainDoor.door.State != DoorState.Closed;
            // the motorised barrier stays down without the checkpoint's power (closed state only: a raised arm can still be lowered)
            Part barrier = FindLockPart(Barrier);
            if (barrier != null && !_kppPower.Value && barrier.door != null && barrier.door.State == DoorState.Closed) barrier.part.locked = true;

            // X01: the lantern out, its flicker keeps changing only its own copy and a light that is off
            if (!_lanternOn.Value)
            {
                if (!_lanternApplied)
                {
                    _lanternApplied = true;
                    foreach (GameObject g in lanternGlows) if (g != null) g.SetActive(false);
                    if (lanternGlass != null && lanternOff != null && lanternSlot >= 0)
                    {
                        Material[] mats = lanternGlass.sharedMaterials;
                        if (lanternSlot < mats.Length) { mats[lanternSlot] = lanternOff; lanternGlass.sharedMaterials = mats; }
                    }
                }
                foreach (Light l in lanternLights) if (l != null && l.enabled) l.enabled = false;
            }

            // the padlock copy on the barrier
            bool pad = _padlockOn.Value;
            if (pad != _padlockShown || snap)
            {
                _padlockShown = pad;
                if (padlockProxy != null) padlockProxy.SetActive(pad);
            }

            // decals and their cleaning zones
            ulong hidden = _decalsHidden.Value;
            if (hidden != _decalsShown)
            {
                _decalsShown = hidden;
                for (int i = 0; i < decals.Length && i < 64; i++)
                    if (decals[i] != null) decals[i].SetActive((hidden & (1UL << i)) == 0);
            }

            // the morning: kitchen shutters up on every peer, once
            int phase = _phase.Value;
            if (phase != _phaseShown)
            {
                if (phase == 2 && _phaseShown >= 0) ShuttersUp();
                _phaseShown = phase;
            }
        }

        static void Snap(Part p, float amount)
        {
            if (p == null) return;
            amount = Mathf.Clamp01(amount);
            p.part.openAmount = amount;
            p.part.target = amount;
            p.part.Apply();
            p.part.enabled = false;
        }

        static void Target(Part p, bool on, bool snap)
        {
            if (p == null) return;
            float t = on ? 1f : 0f;
            if (snap) { Snap(p, t); return; }
            if (Mathf.Approximately(p.part.target, t)) return;
            p.part.target = t;
            p.part.enabled = true;
        }

        // ------------------------------------------------------------------ helpers (server)
        /// <summary>Server: the player still holds the use on this zone (connected, in reach as UseRpc checks it).</summary>
        bool Holding(ulong client, string zone)
        {
            if (!NetworkManager.ConnectedClients.TryGetValue(client, out NetworkClient cc) || cc.PlayerObject == null) return false;
            return host.InReach(zone, cc.PlayerObject.transform.position + Vector3.up * 1.4f);
        }

        /// <summary>The key in the player's hand or pockets (user 21.10: keys go into the pockets).</summary>
        bool HasKey(ulong client, string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            foreach (CargoItem item in CargoItem.All)
                if (item.IsSpawned && item.CarriedBy(client) && item.TryGetComponent(out ForestItemTag tag) && tag.key == key) return true;
            return HeldKey(client) == key;
        }

        string HeldKey(ulong client)
        {
            CargoItem held = CargoItem.HeldBy(client);
            return held != null && held.TryGetComponent(out ForestItemTag tag) && !string.IsNullOrEmpty(tag.key) ? tag.key : null;
        }

        bool HoldsCleaner(ulong client)
        {
            CargoItem held = CargoItem.HeldBy(client);
            return held != null && (held.itemKey == Rag || held.itemKey == Mop);
        }

        /// <summary>Any peer: the jerrycan whose collider centre is under the pump nozzle.</summary>
        FuelCan CanUnderNozzle()
        {
            FuelCan best = null;
            float bestD = NozzleRadius;
            IReadOnlyList<FuelCan> all = FuelCan.All;
            for (int i = 0; i < all.Count; i++)
            {
                Vector3 c = all[i].Center;
                float drop = _nozzle.y - c.y;
                if (drop < -0.1f || drop > NozzleDrop) continue;
                float d = new Vector2(c.x - _nozzle.x, c.z - _nozzle.z).magnitude;
                if (d > bestD) continue;
                bestD = d;
                best = all[i];
            }
            return best;
        }

        bool AnyPlayerNear(Vector3 p, float radius)
        {
            var list = NetworkManager.ConnectedClientsList;
            for (int i = 0; i < list.Count; i++)
                if (list[i].PlayerObject != null && (list[i].PlayerObject.transform.position - p).sqrMagnitude <= radius * radius) return true;
            return false;
        }

        bool AnyPlayerIn(float[] p, float[] q, float[] size)
        {
            Vector3 c = ForestData.V3(p), half = ForestData.V3(size) * 0.5f;
            Quaternion inv = Quaternion.Inverse(ForestData.Q(q));
            var list = NetworkManager.ConnectedClientsList;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].PlayerObject == null) continue;
                Vector3 l = inv * (list[i].PlayerObject.transform.position + Vector3.up * 0.9f - c);
                if (Mathf.Abs(l.x) <= half.x && Mathf.Abs(l.y) <= half.y && Mathf.Abs(l.z) <= half.z) return true;
            }
            return false;
        }

        OpeningItem FindItem(string id)
        {
            if (_data?.items != null)
                foreach (OpeningItem it in _data.items)
                    if (it != null && it.id == id) return it;
            return null;
        }

        // ------------------------------------------------------------------ sound (every peer; clips OPEN)
        void Sound(Sfx what, Vector3 at) => SoundRpc((byte)what, at);

        [Rpc(SendTo.ClientsAndHost)]
        void SoundRpc(byte what, Vector3 at)
        {
            Resolve();
            AudioClip clip = Clip((Sfx)what);
            switch ((Sfx)what)
            {
                case Sfx.RoofSteps: StartWalk(_x06?.soundPath, clip); break;
                case Sfx.CorridorSteps: StartWalk(_x09?.soundPath, clip); break;
                default:                                               // the knock slot holds all three knocks
                    if (clip != null) AudioSource.PlayClipAtPoint(clip, at);
                    break;
            }
            if (clip == null) BenchLog.Event("forest_sound", "id", ((Sfx)what).ToString(), "clip", "none", "p", at);
        }

        AudioClip Clip(Sfx s)
        {
            switch (s)
            {
                case Sfx.PhoneRing: return phoneRing;
                case Sfx.Bell: return phoneBell;
                case Sfx.Breath: return phoneBreath;
                case Sfx.RoofSteps: return roofSteps;
                case Sfx.Knock: return shutterKnock;
                case Sfx.CorridorSteps: return corridorSteps;
                case Sfx.DieselStart: return dieselStart;
                case Sfx.DieselStop: return dieselStop;
                case Sfx.LanternOut: return lanternOut;
                case Sfx.Hiss: return safetyHiss;
                case Sfx.BurnerIgnite: return burnerIgnite;
                case Sfx.BurnerFault: return burnerFault;
                default: return null;
            }
        }

        /// <summary>Steps along a path at ~1.4 m/s: a looping source carried over the points.</summary>
        void StartWalk(ForestPoint[] path, AudioClip clip)
        {
            if (path == null || path.Length < 2 || clip == null) return;
            _walkPath = new Vector3[path.Length];
            for (int i = 0; i < path.Length; i++) _walkPath[i] = ForestData.V3(path[i].p);
            if (_walker == null)
            {
                var go = new GameObject("ForestSteps");
                go.transform.SetParent(transform, false);
                _walker = go.AddComponent<AudioSource>();
                _walker.spatialBlend = 1f;
                _walker.loop = true;
            }
            _walker.clip = clip;
            _walker.transform.position = _walkPath[0];
            _walker.Play();
            _walkSeg = 0;
            _walkT = 0f;
        }

        void Walk()
        {
            if (_walker == null || _walkPath == null || !_walker.isPlaying) return;
            Vector3 a = _walkPath[_walkSeg], b = _walkPath[_walkSeg + 1];
            float len = Mathf.Max(0.01f, Vector3.Distance(a, b));
            _walkT += 1.4f * Time.deltaTime / len;
            if (_walkT >= 1f)
            {
                _walkT = 0f;
                _walkSeg++;
                if (_walkSeg >= _walkPath.Length - 1) { _walker.Stop(); return; }
                a = _walkPath[_walkSeg];
                b = _walkPath[_walkSeg + 1];
            }
            _walker.transform.position = Vector3.Lerp(a, b, _walkT);
        }

        // ------------------------------------------------------------------ the night's fade (each peer its own, IMGUI)
        void OnGUI()
        {
            if (Application.isBatchMode || !IsSpawned || _phase.Value == 0 || _nightStart.Value < 0d) return;
            float t = (float)(NetworkManager.ServerTime.Time - _nightStart.Value);
            float a;
            if (t < 2f) a = t / 2f;                                    // into black
            else if (t < 4f) a = 1f;
            else if (t < 6f) a = 1f - (t - 4f) / 2f;                   // "something woke you": the dark barracks
            else if (t < 34f) a = 0f;
            else if (t < 36f) a = (t - 34f) / 2f;
            else if (t < 45f) a = 1f;
            else if (t < 48f) a = 1f - (t - 45f) / 3f;                 // the morning
            else return;
            GUI.color = new Color(0f, 0f, 0f, Mathf.Clamp01(a));
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            if (t >= 45f && t < 48f)
            {
                GUI.color = new Color(0.9f, 0.88f, 0.8f, 1f);
                var style = new GUIStyle(GUI.skin.label) { fontSize = 40, alignment = TextAnchor.MiddleCenter };
                GUI.Label(new Rect(0f, Screen.height * 0.45f, Screen.width, 60f), "06:30", style);
            }
            GUI.color = Color.white;
        }
    }
}
