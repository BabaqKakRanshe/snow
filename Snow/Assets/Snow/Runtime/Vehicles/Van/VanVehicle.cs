using System.Collections.Generic;
using Snow.Cargo;
using Snow.Doors;
using Snow.Interaction;
using Snow.Net;
using Snow.Seats;
using UazVan;
using Unity.Netcode;
using UnityEngine;
using UtilityPlane;

namespace Snow.Vehicles.Van
{
    /// <summary>
    /// The players' UAZ van (VAN3_INTEGRATION_UNITY.md §3): server simulation of the body, seats, doors, motion
    /// (<see cref="VanMotion"/>: four ray wheels, automatic drivetrain) and, stage by stage, systems (VanSystems) and the zone
    /// table (VanControls). Variant A like the plane and BenchCarrier: the server's Rigidbody moves, clients see a kinematic
    /// copy (NetworkTransform + NetworkRigidbody). It draws nothing: VanPresentation writes the replicated state into
    /// VanRig on every peer.
    /// Driver input (§4.4) arrives as the seat's axes: Pitch = W/S, Roll = A/D, Yaw = Q/E (turn signals), Brake = Space
    /// (hand brake lever, toggled); Z (the seat's autopilot command) = hazard lights.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class VanVehicle : SeatedVehicle
    {
        protected override string SitCue => "van.seat.sit";
        public enum StartState : byte
        {
            /// <summary>Bench (В1 §3): everything works, tank 30 l, battery 100 %.</summary>
            Bench,
            /// <summary>vehicle.json forestStart: empty tanks, weak battery, frost, snow on the roof.</summary>
            ForestStart,
        }

        public const string IgnitionZone = "Ignition_Key";

        public VanTuning tuning;
        public VanRig rig;
        [Tooltip("doorL, doorP, doorSalon, doorZL, dooZP (NetworkDoor.doorId = the part name = the zone id)")]
        public NetworkDoor[] doors;
        public StartState startState = StartState.Bench;

        readonly NetworkVariable<VanTelemetry> _telemetry = new NetworkVariable<VanTelemetry>(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        Rigidbody _rb;
        VanMotion _motion;
        readonly VanWheelInput[] _ground = new VanWheelInput[4];
        readonly Vector3[] _forces = new Vector3[4];
        readonly RaycastHit[] _hits = new RaycastHit[8];
        int _groundMask;
        float _telemetryTimer;
        int _massOccupants = -1;
        bool _engine, _hazard, _horn;
        /// <summary>Positions of the control table (VanControls.Table order), server-written, every peer reads (§3.2).</summary>
        readonly NetworkList<byte> _pos = new NetworkList<byte>();
        readonly Dictionary<string, VanInteractable> _zones = new Dictionary<string, VanInteractable>(System.StringComparer.Ordinal);
        readonly Dictionary<string, int> _cover = new Dictionary<string, int>(System.StringComparer.Ordinal);
        const string FoldSeatZone = "Sit_L4";
        const float FoldHoldSeconds = 0.6f;
        ulong _foldPressClient;
        float _foldPressTime = -1f;
        int _turn;                                                  // -1 left, 0 off, +1 right
        float _lastYaw, _lastBrakeKey;

        // ---- electrics (В4)
        public const byte FuseOk = 0, FuseBlown = 1, FuseEmpty = 2;
        /// <summary>Fuse_1…8: ok / blown / empty (every peer: the lamp circuits).</summary>
        readonly NetworkList<byte> _fuse = new NetworkList<byte>();
        /// <summary>Spare_Fuse_1…3 in the tool box: bit i = there.</summary>
        readonly NetworkVariable<byte> _spares = new NetworkVariable<byte>(0b111);
        readonly NetworkVariable<bool> _batteryIn = new NetworkVariable<bool>(true);
        VanElectrics _elec;
        [Tooltip("The kit's NetItems (VanItem_*_Net), filled by VanPrefabBuilder: what a part of the van becomes in a hand")]
        public GameObject[] itemPrefabs = new GameObject[0];
        [Tooltip("Bench only: electrics run this many times faster (battery drain tests, AT-V10)")]
        public float electricsTimeScale = 1f;
        public VanElectrics Electrics => _elec;

        public Rigidbody Body => _rb;
        public VanTelemetry Telemetry => _telemetry.Value;
        /// <summary>Server: the motion model (bench tests read it).</summary>
        public VanMotion Motion => _motion;

        protected override void Awake()
        {
            base.Awake();
            _rb = GetComponent<Rigidbody>();
            if (rig == null) rig = GetComponent<VanRig>();
            _rb.linearDamping = 0f;                                 // PHYS-01: no hidden stabiliser
            _rb.angularDamping = 0f;
            _rb.automaticCenterOfMass = false;
            _rb.automaticInertiaTensor = false;
            ApplyMass(0);
            _groundMask = ~(1 << LayerMask.NameToLayer("Interact"));
            foreach (var vi in GetComponentsInChildren<VanInteractable>(true))
            {
                if (!vi.gameObject.activeSelf) continue;            // zones the game does not use (VanPrefabBuilder.Keep)
                _zones[vi.id] = vi;
                int cover = VanControls.IndexOfPart(vi.coveredBy);
                if (cover >= 0) _cover[vi.id] = cover;
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (!IsServer) return;
            if (_pos.Count == 0)
                foreach (VanControl ctl in VanControls.Table)
                    _pos.Add((byte)(ctl.Zone == "MassSwitch_Key" || ctl.Zone == "Handbrake" ? 1 : 0));   // bench: mass on, hand brake set
            if (_fuse.Count == 0)
                for (int i = 0; i < 8; i++)
                    _fuse.Add(startState == StartState.ForestStart && i == 0 ? FuseBlown : FuseOk);   // forest start: the headlight fuse is blown (user 21.10)
            // forest start: the tool box has no spares, the spare fuse lies on the start post's desk (user 25.10, ForestOpening)
            if (startState == StartState.ForestStart) _spares.Value = 0;
            _motion = new VanMotion(tuning, tuning.massKg);
            _elec = new VanElectrics(tuning, 1f, tuning.benchBatteryTempC);
            BenchLog.Event("van_spawn", "state", startState.ToString(), "p", transform.position);
        }

        // ---------------------------------------------------------------- mass (§4.1, MASS-01)
        /// <summary>
        /// Body + 85 kg at the hip of every occupied seat (fuel joins in В5). Loose items in the van are their own bodies and
        /// are not added. Centre of mass and the explicit tensor are moved by the parallel-axis rule.
        /// </summary>
        void ApplyMass(int occupants)
        {
            float m0 = tuning.massKg;
            Vector3 c0 = tuning.centerOfMass;
            float mass = m0;
            Vector3 moment = c0 * m0;
            for (int i = 0; i < seats.Length; i++)
            {
                if (occupants == 0 || OccupantOf(i) == SeatRegistry.Empty) continue;
                Vector3 p = transform.InverseTransformPoint(seats[i].pelvis.position);
                mass += tuning.personMassKg;
                moment += p * tuning.personMassKg;
            }
            Vector3 com = moment / mass;
            Vector3 inertia = tuning.inertia + PointInertia(m0, c0 - com);
            for (int i = 0; i < seats.Length; i++)
            {
                if (occupants == 0 || OccupantOf(i) == SeatRegistry.Empty) continue;
                inertia += PointInertia(tuning.personMassKg, transform.InverseTransformPoint(seats[i].pelvis.position) - com);
            }
            _rb.mass = mass;
            _rb.centerOfMass = com;
            _rb.inertiaTensor = inertia;
            _rb.inertiaTensorRotation = Quaternion.identity;
        }

        static Vector3 PointInertia(float m, Vector3 d) =>
            new Vector3(m * (d.y * d.y + d.z * d.z), m * (d.x * d.x + d.z * d.z), m * (d.x * d.x + d.y * d.y));

        int OccupiedSeats()
        {
            int n = 0;
            for (int i = 0; i < seats.Length; i++) if (OccupantOf(i) != SeatRegistry.Empty) n++;
            return n;
        }

        // ---------------------------------------------------------------- simulation
        protected override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!IsServer || !IsSpawned || _motion == null) return;
            float dt = Time.fixedDeltaTime;
            int occ = OccupiedSeats();
            if (occ != _massOccupants) { _massOccupants = occ; ApplyMass(occ); }

            ControlState c = Controls;
            DriverSwitches(c);
            EngineSwitches();

            Vector3 up = transform.up, fwd = transform.forward;
            for (int i = 0; i < 4; i++)
            {
                VanWheel w = _motion.Wheels[i];
                Vector3 origin = transform.TransformPoint(tuning.wheelCentres[i] + Vector3.up * tuning.bumpTravel);
                var g = new VanWheelInput { Up = up, Radius = tuning.wheelRadius, Mu = tuning.muPackedSnow, Rolling = tuning.rollingResistanceSnow };
                if (CastWheel(origin, -up, w.RayLength, out RaycastHit hit))
                {
                    g.Hit = true;
                    g.Distance = hit.distance;
                    g.Point = hit.point;
                    g.Normal = hit.normal;
                    g.Velocity = _rb.GetPointVelocity(hit.point);
                    if (hit.rigidbody != null) g.Velocity -= hit.rigidbody.GetPointVelocity(hit.point);
                    Grip(VanSurface.Of(hit.collider), out g.Mu, out g.Rolling);
                }
                _ground[i] = g;
            }
            LastMu = 0.25f * (_ground[0].Mu + _ground[1].Mu + _ground[2].Mu + _ground[3].Mu);
            float vFwd = Vector3.Dot(_rb.linearVelocity, fwd);
            var u = new VanMotion.Inputs
            {
                Pedal = BenchIntent.HasValue ? BenchIntent.Value.x : c.Pitch,
                Steer = BenchIntent.HasValue ? BenchIntent.Value.y : c.Roll,
                Handbrake = HandbrakeOn ? 1f : 0f,
                Combustion = _engine,
                IgnitionOn = IgnitionOn,
                TorqueScale = 1f,
                ForwardSpeed = vFwd,
            };
            _motion.Step(u, _ground, fwd, dt, _forces);
            for (int i = 0; i < 4; i++)
                if (_ground[i].Hit) _rb.AddForceAtPosition(_forces[i], _ground[i].Point, ForceMode.Force);

            // air drag at the centre of mass, relative to still air (no EnvironmentProvider in the forest, §4.1)
            Vector3 v = _rb.linearVelocity;
            _rb.AddForce(-0.5f * tuning.airDensity * tuning.dragArea * v.magnitude * v, ForceMode.Force);

            _elec.Step(BusOn, LoadAmps(), false, _motion.EngineRpm, dt * Mathf.Max(1f, electricsTimeScale));

            _telemetryTimer -= dt;
            if (_telemetryTimer <= 0f)
            {
                _telemetryTimer = 1f / Mathf.Max(1f, tuning.telemetryHz);
                Publish(vFwd);
            }
            _logTimer -= dt;
            if (_logTimer <= 0f)
            {
                _logTimer = 0.5f;
                LogState(vFwd);
            }
        }

        float _logTimer;

        /// <summary>Bench evidence (AT-V02, V05, V06): pose, speed, drivetrain and tyres twice a second.</summary>
        void LogState(float vFwd)
        {
            VanWheel[] w = _motion.Wheels;
            int sliding = 0, locked = 0, air = 0;
            for (int i = 0; i < 4; i++)
            {
                if (w[i].Sliding) sliding |= 1 << i;
                if (w[i].Locked) locked |= 1 << i;
                if (!w[i].Grounded) air |= 1 << i;
            }
            BenchLog.Event("van_state", "p", transform.position, "kmh", vFwd * 3.6f, "rpm", _motion.EngineRpm, "gear", _motion.Gear,
                "gas", _motion.Gas, "brake", _motion.Brake, "hand", HandbrakeOn, "steer", _motion.SteerDeg, "clutch", _motion.ClutchTorque,
                "slide", sliding, "lock", locked, "air", air, "pitchDeg", Vector3.SignedAngle(Vector3.ProjectOnPlane(transform.forward, Vector3.up), transform.forward, transform.right),
                "mass", _rb.mass, "axle", _motion.FrontAxle, "low", _motion.LowRange);
        }

        void Grip(SurfaceKind kind, out float mu, out float rolling)
        {
            rolling = kind == SurfaceKind.Road ? tuning.rollingResistance : tuning.rollingResistanceSnow;
            switch (kind)
            {
                case SurfaceKind.Road: mu = tuning.muRoad; break;
                case SurfaceKind.LooseSnow: mu = tuning.muLooseSnow; break;
                case SurfaceKind.Ice: mu = tuning.muIce; break;
                case SurfaceKind.SandTrack: mu = tuning.muSandTrack; break;
                default: mu = tuning.muPackedSnow; break;
            }
        }

        bool CastWheel(Vector3 origin, Vector3 dir, float length, out RaycastHit best)
        {
            best = default;
            int n = Physics.RaycastNonAlloc(origin, dir, _hits, length, _groundMask, QueryTriggerInteraction.Ignore);
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                Rigidbody body = _hits[i].rigidbody;
                if (body == _rb) continue;                                           // own body
                if (body != null && !body.isKinematic && body.mass < 200f) continue;  // not on loose items
                if (_hits[i].collider is CharacterController) continue;
                if (!found || _hits[i].distance < best.distance) { best = _hits[i]; found = true; }
            }
            return found;
        }

        /// <summary>Space toggles the hand brake lever (it stays when the driver leaves).</summary>
        void DriverSwitches(ControlState c)
        {
            if (c.Brake > 0.5f && _lastBrakeKey <= 0.5f) SetHandbrake(!HandbrakeOn, "space");
            _lastBrakeKey = c.Brake;
            // no turn signals (user 21.10: "поворотники вообще не нужны"; E in the seat kept switching them on): Q / E do nothing
        }

        void SetHandbrake(bool on, string by)
        {
            SetPos("Handbrake", on ? 1 : 0);                        // the lever of the table: Space and the zone move the same one
            BenchLog.Event("van_handbrake", "on", on, "by", by);
        }

        bool HandbrakeOn => Pos("Handbrake") == 1;

        /// <summary>Mass on, the battery in its box and the clamps on: the bus has a battery (the electrics proper are В4).</summary>
        bool BusOn => Pos("MassSwitch_Key") == 1 && Pos("Battery_ClampPos") == 0 && _batteryIn.Value;

        /// <summary>The battery is in its box (it can be taken out, user 24.10).</summary>
        public bool BatteryIn => _batteryIn.Value;

        /// <summary>Server: the engine runs (the forest opening's X01 puts the hut lantern out when it starts).</summary>
        public bool EngineRunning => _engine;
        bool IgnitionOn => BusOn && Pos("Ignition_Key") >= 2;

        /// <summary>
        /// TEMPORARY (until В5, the real start chain): the key in III with the bus on starts the engine at once; the key
        /// out of II / III, no bus or the plug leads off stop it.
        /// </summary>
        void EngineSwitches()
        {
            bool spark = IgnitionOn && Pos("Engine_Lead_1") == 0;
            if (_engine && !spark)
            {
                _engine = false;
                BenchLog.Event("van_engine", "running", false, "why", IgnitionOn ? "leads" : "ignition");
            }
            else if (!_engine && spark && Pos("Ignition_Key") == 3)
            {
                _engine = true;
                if (_motion.EngineRpm < tuning.idleRpm) _motion.EngineOmega = tuning.idleRpm * 2f * Mathf.PI / 60f;
                BenchLog.Event("van_engine", "running", true, "why", "starter", "temporary", true);
            }
        }

        void SetTurn(int dir)
        {
            _turn = dir;
            BenchLog.Event("van_turn", "dir", dir);
        }

        void Publish(float vFwd)
        {
            VanWheel[] w = _motion.Wheels;
            VanFlags f = VanFlags.None;
            if (_engine) f |= VanFlags.Engine;
            if (IgnitionOn) f |= VanFlags.Ignition;
            if (HandbrakeOn) f |= VanFlags.Handbrake;
            if (Pos("Horn_Button") == 1) f |= VanFlags.Horn;
            if (_motion.Brake > 0.05f) f |= VanFlags.Brake;
            if (_motion.Gear < 0) f |= VanFlags.Reverse;
            if (_turn < 0) f |= VanFlags.TurnLeft;
            if (_turn > 0) f |= VanFlags.TurnRight;
            if (_hazard) f |= VanFlags.Hazard;
            if (_horn) f |= VanFlags.Horn;
            if (_motion.FrontAxle) f |= VanFlags.FrontAxle;
            if (_motion.LowRange) f |= VanFlags.LowRange;
            _telemetry.Value = new VanTelemetry
            {
                Rpm = _motion.EngineRpm,
                SpeedKmh = vFwd * 3.6f,
                SteerDeg = _motion.SteerDeg,
                W0 = w[0].Omega, W1 = w[1].Omega, W2 = w[2].Omega, W3 = w[3].Omega,
                S0 = w[0].Travel, S1 = w[1].Travel, S2 = w[2].Travel, S3 = w[3].Travel,
                Gear = (sbyte)_motion.Gear,
                Flags = f,
                Gas = VanTelemetry.Byte01(_motion.Gas),
                Brake = VanTelemetry.Byte01(_motion.Brake),
                BusVolts = _elec.BusVolts,
                Charge = VanTelemetry.Byte01(_elec.Charge),
            };
        }

        // ---------------------------------------------------------------- electrics (В4, SYS-01)
        /// <summary>
        /// Server: what the bus feeds now, A. Lamps: the rig's own load (its lamps lit by the circuits, at the bus voltage,
        /// lights.json watts), at the rated voltage; ignition 4 A from II; heater fan, wipers, radio by their switches.
        /// </summary>
        float LoadAmps()
        {
            if (!BusOn) return 0f;
            float watts = rig != null ? rig.LampLoadWatts() : 0f;
            if (IgnitionOn)
            {
                watts += tuning.heaterFanWatts[Mathf.Clamp(Pos("Heater_Fan"), 0, tuning.heaterFanWatts.Length - 1)];
                if (CircuitOn("Toggle_Wiper", "Fuse_8")) watts += tuning.wiperWatts;
            }
            if (Pos("Radio_Volume") > 0) watts += tuning.radioWatts;
            return watts / tuning.lampRatedVolts + (IgnitionOn ? tuning.ignitionAmps : 0f);
        }

        /// <summary>Every peer: a switched circuit has power — mass on, clamps on, its fuse in and whole, its switch on (SYS-01).</summary>
        public bool CircuitOn(string toggle, string fuse)
        {
            if (!BusOn) return false;
            if (!string.IsNullOrEmpty(fuse) && FuseState(fuse) != FuseOk) return false;
            return string.IsNullOrEmpty(toggle) || Pos(toggle) == 1;
        }

        /// <summary>Every peer: Fuse_1…8 → ok / blown / empty.</summary>
        public byte FuseState(string fuse)
        {
            int n = FuseIndex(fuse);
            return n >= 0 && n < _fuse.Count ? _fuse[n] : FuseOk;
        }

        public bool SparePresent(int i) => (_spares.Value & (1 << i)) != 0;

        static int FuseIndex(string id) =>
            id != null && id.StartsWith("Fuse_") && int.TryParse(id.Substring(5), out int n) && n >= 1 && n <= 8 ? n - 1 : -1;

        static int SpareIndex(string id) =>
            id != null && id.StartsWith("Spare_Fuse_") && int.TryParse(id.Substring(11), out int n) && n >= 1 && n <= 3 ? n - 1 : -1;

        /// <summary>
        /// Server: a fuse slot. With a fuse in hand: into an empty slot. Hands empty: out of the slot — a blown one is thrown
        /// away, a whole one goes into the hand. The forest start's blown headlight fuse is replaced from the tool box.
        /// </summary>
        bool ServerFuse(ulong client, int slot, UsePhase phase, out string reason)
        {
            reason = null;
            if (phase != UsePhase.Press) return true;
            CargoItem held = CargoItem.HeldBy(client);
            byte s = _fuse[slot];
            if (held != null)
            {
                if (held.itemKey != "Fuse") { reason = "Сюда — только предохранитель"; return false; }
                if (s != FuseEmpty) { reason = "Гнездо занято: сначала выньте предохранитель"; return false; }
                _fuse[slot] = FuseOk;
                ServerConsumeHeld(client, held);
                BenchLog.Event("van_fuse", "slot", slot + 1, "state", "in", "client", client);
                return true;
            }
            if (s == FuseEmpty) { reason = "Нужен предохранитель — запасные в ящике с инструментом"; return false; }
            if (Registry.SeatOf(client) >= 0) { reason = "Выйдите из машины, чтобы вынуть"; return false; }
            _fuse[slot] = FuseEmpty;
            BenchLog.Event("van_fuse", "slot", slot + 1, "state", s == FuseBlown ? "blown_out" : "out", "client", client);
            if (s == FuseBlown) return true;                        // a blown fuse is thrown away
            if (ServerGiveItem(client, "Fuse")) return true;
            _fuse[slot] = s;
            reason = "Не удалось взять";
            return false;
        }

        bool ServerSpareFuse(ulong client, int i, UsePhase phase, out string reason)
        {
            reason = null;
            if (phase != UsePhase.Press) return true;
            CargoItem held = CargoItem.HeldBy(client);
            if (held != null)
            {
                if (held.itemKey != "Fuse" || SparePresent(i)) { reason = "Сюда — только предохранитель, на пустое место"; return false; }
                _spares.Value = (byte)(_spares.Value | (1 << i));
                ServerConsumeHeld(client, held);
                return true;
            }
            if (!SparePresent(i)) { reason = "Здесь пусто"; return false; }
            if (Registry.SeatOf(client) >= 0) { reason = "Выйдите из машины, чтобы взять"; return false; }
            _spares.Value = (byte)(_spares.Value & ~(1 << i));
            if (ServerGiveItem(client, "Fuse")) return true;
            _spares.Value = (byte)(_spares.Value | (1 << i));
            reason = "Не удалось взять";
            return false;
        }

        /// <summary>
        /// Server: the battery in its box under seat L4 (user 24.10 «не могу взять аккумулятор из машины»), like a fuse. Hands
        /// empty: out into the hands (18 kg, carried in front) once both clamps are off; the battery in hand: back into the
        /// empty box. The lid over it is the zone rule (coveredBy). The charge stays with the van (TEMPORARY: the item does
        /// not carry it).
        /// </summary>
        bool ServerBattery(ulong client, UsePhase phase, out string reason)
        {
            reason = null;
            if (phase != UsePhase.Press) return true;
            CargoItem held = CargoItem.HeldBy(client);
            if (held != null)
            {
                if (held.itemKey != "Battery") { reason = "Сюда — только аккумулятор"; return false; }
                if (_batteryIn.Value) { reason = "Аккумулятор уже стоит"; return false; }
                _batteryIn.Value = true;
                ServerConsumeHeld(client, held);
                BenchLog.Event("van_battery", "state", "in", "client", client);
                return true;
            }
            if (!_batteryIn.Value) { reason = "Ящик пуст: поставьте аккумулятор (в руках)"; return false; }
            if (Pos("Battery_ClampPos") == 0) { reason = "Сначала снимите клеммы"; return false; }
            if (Registry.SeatOf(client) >= 0) { reason = "Выйдите из машины, чтобы вынуть"; return false; }
            _batteryIn.Value = false;
            if (ServerGiveItem(client, "Battery"))
            {
                BenchLog.Event("van_battery", "state", "out", "client", client);
                return true;
            }
            _batteryIn.Value = true;
            reason = "Не удалось взять";
            return false;
        }

        // ---------------------------------------------------------------- items in hand (В6 storage model, G4)
        public GameObject ItemPrefab(string type)
        {
            foreach (GameObject p in itemPrefabs)
                if (p != null && p.TryGetComponent(out CargoItem c) && c.itemKey == type) return p;
            return null;
        }

        /// <summary>Server: a part of the van becomes a NetItem in this player's hands.</summary>
        bool ServerGiveItem(ulong client, string type)
        {
            GameObject prefab = ItemPrefab(type);
            if (prefab == null || !NetworkManager.ConnectedClients.TryGetValue(client, out var cc) || cc.PlayerObject == null) return false;
            Transform p = cc.PlayerObject.transform;
            var go = Instantiate(prefab, p.position + Vector3.up * 1.4f + p.forward * 0.5f, p.rotation);
            go.GetComponent<NetworkObject>().Spawn();
            bool ok = cc.PlayerObject.GetComponent<Snow.Player.PlayerNet>().ServerGiveItem(go.GetComponent<CargoItem>(), 0.5f);
            if (!ok) go.GetComponent<NetworkObject>().Despawn(true);
            BenchLog.Event("van_item_out", "type", type, "client", client, "ok", ok);
            return ok;
        }

        /// <summary>Server: the item in this player's hands goes into the van (it becomes a part again).</summary>
        void ServerConsumeHeld(ulong client, CargoItem item)
        {
            item.ServerRelease(client);
            if (NetworkManager.ConnectedClients.TryGetValue(client, out var cc) && cc.PlayerObject != null)
                cc.PlayerObject.GetComponent<Snow.Player.PlayerNet>().ServerForgetHeld(item);
            BenchLog.Event("van_item_in", "type", item.itemKey, "client", client);
            item.NetworkObject.Despawn(true);
        }

        // ---------------------------------------------------------------- bench (VanBenchRunner, server only)
        /// <summary>Bench only: the driver's W/S (x) and A/D (y) without a seated driver; null = the seat's axes.</summary>
        public Vector2? BenchIntent;
        /// <summary>Server: mean μ under the four wheels in the last step (bench diagnostics).</summary>
        public float LastMu { get; private set; }

        public void ServerSetEngine(bool on)
        {
            SetPos("MassSwitch_Key", 1);
            SetPos("Ignition_Key", on ? 2 : 0);                     // the key goes with it, or the ignition rule stops the engine
            _engine = on;
            if (on && _motion.EngineRpm < tuning.idleRpm) _motion.EngineOmega = tuning.idleRpm * 2f * Mathf.PI / 60f;
            BenchLog.Event("van_engine", "running", on, "by", "bench");
        }

        public void ServerSetHandbrake(bool on) => SetHandbrake(on, "bench");

        /// <summary>Bench only: a control of the table straight to a position.</summary>
        public void ServerSetControl(string zone, int position) => SetPos(zone, position);

        /// <summary>Bench only: a fuse slot to a state (ok / blown / empty).</summary>
        public void ServerSetFuse(int slot, byte state) => _fuse[slot] = state;

        /// <summary>Bench only: battery charge and temperature.</summary>
        public void ServerSetBattery(float charge, float tempC)
        {
            _elec.Charge = Mathf.Clamp01(charge);
            _elec.BatteryTempC = tempC;
        }

        /// <summary>Bench only: the bus voltage the starter would leave (В5: below 8 V the relay only clicks).</summary>
        public float StarterBusVolts => _elec.Emf - _elec.StarterAmps * _elec.Rint;

        public void ServerSetFrontAxle(bool on)
        {
            _motion.FrontAxle = on;
            SetPos("TransferLever_Axle", on ? 1 : 0);
        }

        public bool ServerSetLowRange(bool low, out string reason)
        {
            if (!_motion.TrySetRange(low, Vector3.Dot(_rb.linearVelocity, transform.forward) * 3.6f, out reason)) { Grind(); return false; }
            SetPos("TransferLever_Range", low ? 1 : 0);
            return true;
        }

        /// <summary>Bench only: put the van at a pose, at rest (server; the NetworkTransform carries it to clients).</summary>
        public void ServerTeleport(Vector3 position, Quaternion rotation)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.position = position;
            _rb.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            foreach (VanWheel w in _motion.Wheels) w.Reset();
            BenchLog.Event("van_teleport", "p", position, "r", rotation.eulerAngles);
        }

        public NetworkDoor Door(string id)
        {
            foreach (var d in doors)
                if (d != null && d.doorId == id) return d;
            return null;
        }

        // ---------------------------------------------------------------- commands
        public override bool ServerPilotCommand(ulong client, PilotCommand command, out string reason)
        {
            reason = null;
            switch (command)
            {
                case PilotCommand.AutopilotToggle:
                    _hazard = !_hazard;                             // Z: the van has no autopilot, Z is the hazard switch (§4.4)
                    BenchLog.Event("van_hazard", "on", _hazard, "client", client);
                    return true;
                case PilotCommand.HornOn:
                case PilotCommand.HornOff:
                    _horn = command == PilotCommand.HornOn;         // G (§4.4); the sound is the presentation's (V-05)
                    BenchLog.Event("van_horn", "on", _horn, "client", client);
                    return true;
            }
            return base.ServerPilotCommand(client, command, out reason);
        }

        public override bool ServerUse(ulong clientId, string id, UsePhase phase, out string reason)
        {
            reason = null;
            _zones.TryGetValue(id, out VanInteractable zone);
            // user decision 21.10: the cab's switches, knobs, keys and levers only from a seat with the cursor (Tab)
            if (zone != null && IsCursorOnly(zone) && Registry.SeatOf(clientId) < 0)
            {
                reason = "Сядьте в машину и нажмите Tab";
                return false;
            }
            if (id == FoldSeatZone) return ServerFoldSeatUse(clientId, phase, out reason);
            int seat = SeatIndex(id);
            if (seat >= 0) return phase != UsePhase.Press || ServerSit(clientId, seat, out reason);
            NetworkDoor door = Door(id);
            if (door != null)
            {
                if (phase == UsePhase.Press) door.Toggle(DoorDrive.Manual);
                return true;
            }
            int c = VanControls.IndexOf(id);
            if (c >= 0) return ServerControl(clientId, c, phase, out reason);
            if (FuseIndex(id) >= 0) return ServerFuse(clientId, FuseIndex(id), phase, out reason);
            if (SpareIndex(id) >= 0) return ServerSpareFuse(clientId, SpareIndex(id), phase, out reason);
            if (id == "Battery") return ServerBattery(clientId, phase, out reason);
            if (phase != UsePhase.Press) return true;
            reason = "Пока не работает (предметы — следующие этапы)";
            return false;
        }

        /// <summary>Server: one use of a control of the table — next position, its consequence, BenchLog.</summary>
        bool ServerControl(ulong client, int c, UsePhase phase, out string reason)
        {
            reason = null;
            VanControl ctl = VanControls.Table[c];
            int pos = _pos[c];
            if (!VanControls.Next(ctl, pos, phase, out int next)) return true;
            switch (ctl.Zone)
            {
                case "Seat_L4":
                    if (next == 1 && OccupantOf(SeatIndex(FoldSeatZone)) != SeatRegistry.Empty) { reason = "На сиденье сидят"; return false; }
                    break;
                case "TransferLever_Range":
                    if (!_motion.TrySetRange(next == 1, Vector3.Dot(_rb.linearVelocity, transform.forward) * 3.6f, out reason)) { Grind(); return false; }
                    break;
                case "TransferLever_Axle":
                    _motion.FrontAxle = next == 1;
                    break;
                case "Battery_ClampPos":
                    if (next == 0 && !_batteryIn.Value) { reason = "Нет аккумулятора"; return false; }
                    break;
            }
            _pos[c] = (byte)next;
            BenchLog.Event("van_control", "zone", ctl.Zone, "from", pos, "to", next, "state", ctl.States[next], "client", client);
            return true;
        }

        /// <summary>
        /// Seat L4 (the battery box is under it, §3.3): a click on Sit_L4 sits down, holding the use 0.6 s folds the free seat.
        /// The server decides on the release, from how long the press was.
        /// </summary>
        bool ServerFoldSeatUse(ulong client, UsePhase phase, out string reason)
        {
            reason = null;
            if (phase == UsePhase.Press) { _foldPressClient = client; _foldPressTime = Time.time; return true; }
            if (phase != UsePhase.Release || _foldPressTime < 0f || _foldPressClient != client) return true;
            float held = Time.time - _foldPressTime;
            _foldPressTime = -1f;
            return held >= FoldHoldSeconds
                ? ServerControl(client, VanControls.IndexOf("Seat_L4"), UsePhase.Press, out reason)
                : ServerSit(client, SeatIndex(FoldSeatZone), out reason);
        }

        public override string Describe(string id)
        {
            int seat = SeatIndex(id);
            if (seat >= 0) return SeatText(seat);
            NetworkDoor door = Door(id);
            if (door != null) return DoorText(door.State);
            int c = VanControls.IndexOf(id);
            if (c >= 0 && c < _pos.Count) return VanControls.Table[c].States[_pos[c]];
            if (FuseIndex(id) >= 0)
                switch (FuseState(id)) { case FuseOk: return "цел"; case FuseBlown: return "сгорел"; default: return "гнездо пустое"; }
            if (SpareIndex(id) >= 0) return SparePresent(SpareIndex(id)) ? "лежит" : "взят";
            if (id == "Battery")                                     // user 21.10: the charge on the battery
                return _batteryIn.Value ? $"заряд {Mathf.RoundToInt(Telemetry.Charge / 2.55f)} %" : "ящик пуст";
            return _zones.ContainsKey(id) ? "пока не работает" : null;
        }

        public override string ActionText(string id)
        {
            if (id == FoldSeatZone) return "сесть; держать — сложить сиденье";
            if (SeatIndex(id) >= 0) return "сесть";
            NetworkDoor door = Door(id);
            if (door != null) return door.State == DoorState.Open || door.State == DoorState.Opening ? "закрыть" : "открыть";
            int c = VanControls.IndexOf(id);
            if (c >= 0 && c < _pos.Count) return VanControls.Table[c].Actions[_pos[c]];
            if (FuseIndex(id) >= 0) return FuseState(id) == FuseEmpty ? "вставить предохранитель (в руках)" : FuseState(id) == FuseBlown ? "вынуть и выбросить" : "вынуть";
            if (SpareIndex(id) >= 0) return SparePresent(SpareIndex(id)) ? "взять (держать E)" : "положить предохранитель";
            if (id == "Battery")
                return !_batteryIn.Value ? "поставить аккумулятор (в руках)" : Pos("Battery_ClampPos") == 0 ? "вынуть (сначала снять клеммы)" : "вынуть";
            return "использовать";
        }

        /// <summary>Server: the transfer case refused a shift on the move — the gears grind.</summary>
        void Grind() => Snow.Audio.SnowAudio.ServerPlay("van.transfer.grind", transform.position);

        float _nextHit;

        /// <summary>Server: the van hits the world — tank hedgehogs, concrete blocks and teeth, the barrier, the zone gate.</summary>
        void OnCollisionEnter(Collision c)
        {
            if (!IsServer || Time.time < _nextHit || c.relativeVelocity.magnitude < 2f || c.collider == null) return;
            string n = c.collider.name + " " + (c.collider.attachedRigidbody != null ? c.collider.attachedRigidbody.name : c.collider.transform.root.name);
            string cue = n.Contains("Hedgehog") ? "fence.hedgehog.hit"
                : n.Contains("Jersey") || n.Contains("FBS") || n.Contains("TankTooth") || n.Contains("Cube") || n.Contains("Concrete") ? "fence.concrete.hit"
                : n.Contains("Barrier") ? "kpp.barrier.hit"
                : n.Contains("ZoneGate") ? "road.zonegate.shake" : null;
            if (cue == null) return;
            _nextHit = Time.time + 0.5f;
            Snow.Audio.SnowAudio.ServerPlay(cue, c.contactCount > 0 ? c.GetContact(0).point : transform.position, Mathf.Clamp(c.relativeVelocity.magnitude / 8f, 0.5f, 1.5f));
        }

        public override float HoldSeconds(string id) => id == FoldSeatZone && Pos("Seat_L4") == 0 ? FoldHoldSeconds : 0f;

        // ---------------------------------------------------------------- zone rules (§3.3)
        /// <summary>
        /// A zone under a closed lid / cover (coveredBy) is not there for the ray; the seat L4 is sat on while down and
        /// unfolded while folded. Client ray and server both ask (the positions are replicated).
        /// </summary>
        public override bool ZoneOpen(UtilityPlaneInteractable zone, CargoItem held)
        {
            if (!_zones.ContainsKey(zone.id)) return true;
            if (_cover.TryGetValue(zone.id, out int cover) && cover < _pos.Count && !VanControls.Table[cover].IsOpen(_pos[cover])) return false;
            if (zone.id == FoldSeatZone) return Pos("Seat_L4") == 0;
            if (zone.id == "Seat_L4") return Pos("Seat_L4") == 1;
            return true;
        }

        /// <summary>
        /// The van has no side rule: its zones' side (VanInteractable.side) only says where they are. A lid, the fuse box or
        /// the engine cover are reached through an open door as well as from a seat; walls, glass and closed doors already
        /// stop the ray (user 21.10: "открыть" on the fuse box from the doorway said "С этой стороны не достать").
        /// </summary>
        public override bool SideAllows(string id, Vector3 eye) => _zones.ContainsKey(id) || base.SideAllows(id, eye);

        public override bool CursorOnly(UtilityPlaneInteractable zone) => _zones.TryGetValue(zone.id, out VanInteractable vi) && IsCursorOnly(vi);

        static readonly HashSet<string> CursorKinds = new HashSet<string> { "toggle", "selector", "rotary", "pull", "lever", "momentary", "lock" };
        // the battery box under seat L4 is worked on foot (user 24.10: its clamps were cursor-only, so the battery could not
        // be taken out standing); the 21.10 rule is for the cab's switches, knobs, keys and levers
        static bool IsCursorOnly(VanInteractable vi) => vi.side == "in" && CursorKinds.Contains(vi.kind) && vi.coveredBy != "BatteryBox_Lid";

        /// <summary>Current position of a control of the table (0 if there is none or before the first sync).</summary>
        public int Pos(string zone)
        {
            int c = VanControls.IndexOf(zone);
            return c >= 0 && c < _pos.Count ? _pos[c] : 0;
        }

        public int Pos(int control) => control >= 0 && control < _pos.Count ? _pos[control] : 0;

        void SetPos(string zone, int position)
        {
            int c = VanControls.IndexOf(zone);
            if (c >= 0 && c < _pos.Count && _pos[c] != position) _pos[c] = (byte)position;
        }

        public static string DoorText(DoorState s)
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
    }
}
