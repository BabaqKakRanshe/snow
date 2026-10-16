using System.Collections.Generic;
using Snow.Cargo;
using Snow.Doors;
using Snow.Interaction;
using Snow.Net;
using Snow.Seats;
using Snow.World;
using Unity.Netcode;
using UnityEngine;

namespace Snow.Aircraft
{
    /// <summary>
    /// The Utility Plane as a networked vehicle (§8, §9, variant A of NET-03): the server owns and simulates it; clients
    /// send intentions (pilot axes every tick, use commands reliable with a sequence number) and see replicated state.
    ///  - Seats (SEAT-01/02): Seat_P flies, Seat_C never sends axes. Exit through the own-side cabin door.
    ///  - Every interaction volume of the model does something to the systems (NEW-02, §8.4); the passenger may use
    ///    everything but the flight axes and the power lever.
    ///  - Each FixedUpdate: controls → actuators (60°/s, flaps electric) → systems (SYS-01 chain) → forces
    ///    (AircraftPhysics) → mass properties (fuel, occupants; cargo acts through physics) → instruments → telemetry.
    /// </summary>
    public class PlaneVehicle : SeatedVehicle
    {
        public AircraftTuning tuning;
        public AircraftPhysics physics;
        public NetworkDoor pilotDoorL, pilotDoorR, cargoDoorL, cargoDoorR;
        [Tooltip("Socket_CenterOfMass: the empty aircraft's centre of mass, TEMPORARY")] public Transform centerOfMass;
        [Tooltip("Cargo bay box in prefab space (CARGO LOOSE lamp)")] public Vector3 bayCenter = new Vector3(0f, 1.8f, 0.3f), bayHalfExtents = new Vector3(0.66f, 0.75f, 1.65f);
        [Tooltip("s between bench log lines")] public float logInterval = 1f;

        /// <summary>A collider box in prefab space (centre, half extents, rotation).</summary>
        [System.Serializable]
        public struct BoxPose { public Vector3 center, half; public Quaternion rotation; public int seat; }

        [Header("И3 cargo bay (built by PlaneBenchBuilder from the model)")]
        [Tooltip("Pan and leg colliders of each troop seat in the down pose, prefab space (seat = PaxSeatNames index)")]
        public BoxPose[] paxDownBoxes = System.Array.Empty<BoxPose>();
        [Tooltip("Floor box the stowed ladder needs, prefab space (model_facts.json ladderStowed)")] public BoxPose ladderStowBox;
        public StowPoint[] stowPoints = System.Array.Empty<StowPoint>();
        [Tooltip("Socket_Exit_Cargo_L: troop seats L1/L2 get out here past a stowed ladder")] public Transform exitCargoL;
        [Tooltip("Rings the stowed ladder covers or strap (model_facts.json)")] public TieDownSocket[] ladderRings = System.Array.Empty<TieDownSocket>();
        [Tooltip("Ring half covered by the jerry can in its holder (Socket_TieDown_13)")] public TieDownSocket jerryCanRing;
        [Tooltip("s for a troop seat to fold or unfold (presentation, TEMPORARY)")] public float paxFoldTime = 0.5f;

        readonly NetworkVariable<PlaneSwitchState> _switches = new NetworkVariable<PlaneSwitchState>(PlaneSwitchState.ColdAndDark,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        readonly NetworkVariable<PlaneTelemetry> _telemetry = new NetworkVariable<PlaneTelemetry>(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        AircraftSystems _systems;
        FlightInstruments _instruments;
        CabinClimate _climate;
        PlaneWinterKitNet _kitNet;
        bool _held;                                    // И4: the kit holds the parked aircraft (anchor spring)
        Vector3 _holdPos;
        float _holdYaw;
        readonly Dictionary<ulong, bool> _pours = new Dictionary<ulong, bool>();   // client → left tank (jerry can)
        Autopilot _ap;
        float _elev, _ail, _rud, _flap;
        int _flapDirection = 1;
        ulong _starterHolder = SeatRegistry.Empty, _testHolder = SeatRegistry.Empty;
        bool _cargoLoose;
        float _nextLooseCheck, _nextLog;
        Vector3 _lastVelocity;
        float _appliedMass = -1f;
        int _appliedOccupants = -1;
        readonly Collider[] _bayHits = new Collider[48];
        readonly Collider[] _boxHits = new Collider[32];

        static readonly Dictionary<string, PlaneSwitch> Toggles = new Dictionary<string, PlaneSwitch>
        {
            { "SW_Battery", PlaneSwitch.Battery }, { "SW_Avionics", PlaneSwitch.Avionics }, { "SW_FuelPump", PlaneSwitch.FuelPump },
            { "SW_Ignition", PlaneSwitch.Ignition }, { "SW_Beacon", PlaneSwitch.Beacon }, { "SW_Strobe", PlaneSwitch.Strobe },
            { "SW_NavLights", PlaneSwitch.NavLights }, { "SW_Landing", PlaneSwitch.Landing }, { "SW_CabinLight", PlaneSwitch.CabinLight },
            { "SW_CargoLight", PlaneSwitch.CargoLight }, { "Pull_CabinHeat", PlaneSwitch.CabinHeat }, { "Pull_Defrost", PlaneSwitch.Defrost },
            { "Pull_ParkBrake", PlaneSwitch.ParkBrake }, { "Pull_FuelShutoff", PlaneSwitch.FuelShutoff },
            { "FuelSel_L", PlaneSwitch.FuelSelL }, { "FuelSel_R", PlaneSwitch.FuelSelR }, { "Visor_P", PlaneSwitch.VisorP },
            { "Visor_C", PlaneSwitch.VisorC }, { "Socket_FuelCap_L", PlaneSwitch.FuelCapL }, { "Socket_FuelCap_R", PlaneSwitch.FuelCapR },
            { "Socket_Belt_P", PlaneSwitch.BeltP }, { "Socket_Belt_C", PlaneSwitch.BeltC }, { "Lever_Condition", PlaneSwitch.ConditionCutOff },
            { "Glovebox_Lid", PlaneSwitch.Glovebox },
        };

        /// <summary>Knobs turned by click / wheel (rheostats, v7 instrument and radio knobs, И2 F): step per use.</summary>
        static bool IsKnob(string id) => id.StartsWith("Knob_", System.StringComparison.Ordinal) || id.StartsWith("Rheo_", System.StringComparison.Ordinal);

        public PlaneSwitchState Switches => _switches.Value;
        public PlaneTelemetry Telemetry => _telemetry.Value;
        /// <summary>Server only.</summary>
        public AircraftSystems Systems => _systems;
        public Rigidbody Body => physics != null ? physics.body : null;

        protected override void Awake()
        {
            base.Awake();
            _kitNet = GetComponent<PlaneWinterKitNet>();
            foreach (NetworkDoor d in Doors())
                if (d != null) d.PowerSource = () => _systems != null && _systems.Electrics.BusPowered;
        }

        IEnumerable<NetworkDoor> Doors()
        {
            yield return pilotDoorL; yield return pilotDoorR; yield return cargoDoorL; yield return cargoDoorR;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (!IsServer) return;
            float oat = EnvironmentProvider.TemperatureAt(transform.position.y);
            _systems = new AircraftSystems(tuning, oat);
            _instruments = new FlightInstruments(tuning);
            _climate = new CabinClimate(tuning);
            _ap = new Autopilot(tuning.AutopilotGains);
            Body.linearDamping = 0f;                                  // PHYS-01: no hidden stabiliser (§8.1)
            Body.angularDamping = 0f;
            Body.automaticCenterOfMass = false;
            Body.automaticInertiaTensor = false;
            UpdateMass(true);
            _lastVelocity = Body.linearVelocity;
            BenchLog.Event("plane_spawn", "p", transform.position, "mass", Body.mass, "com", Body.centerOfMass);
        }

        // ================================================================ server simulation
        protected override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!IsServer || !IsSpawned || _systems == null) return;
            float dt = Time.fixedDeltaTime;
            AircraftTuning t = tuning;
            ControlState c = Controls;
            PlaneSwitchState s = _switches.Value;

            // autopilot (a device on the avionics bus): its servos add to the pilot's input with limited authority; the
            // pilot moving the yoke firmly, a power loss or leaving the limits disengages it
            float apPitch = 0f, apRoll = 0f;
            if (_ap.Engaged)
            {
                if (!(_systems.Electrics.BusPowered && s.On(PlaneSwitch.Avionics))) _ap.Disengage(Autopilot.NoPower);
                else if (Mathf.Abs(c.Pitch) > 0.5f || Mathf.Abs(c.Roll) > 0.5f) _ap.Disengage(Autopilot.ByYoke);
                else
                {
                    Attitude(out float hdg, out float bank, out float pitch, out float rollRate, out float pitchRate, out float altFt, out float vs);
                    _ap.Step(hdg, bank, rollRate, pitch, pitchRate, altFt, vs, dt);
                    apPitch = _ap.PitchCommand;
                    apRoll = _ap.RollCommand;
                }
                if (!_ap.Engaged) BenchLog.Event("autopilot", "on", false, "reason", Autopilot.ReasonText(_ap.DisengageCode));
            }
            float pitchIn = Mathf.Clamp(c.Pitch + apPitch, -1f, 1f);
            float rollIn = Mathf.Clamp(c.Roll + apRoll, -1f, 1f);

            // flight controls are mechanical: keyboard command → surface at a limited rate (§9.1); trim shifts the balance
            float elevCmd = (pitchIn >= 0f ? -pitchIn * t.elevatorDownDeg : -pitchIn * t.elevatorUpDeg) + c.Trim * t.trimElevatorDeg;
            elevCmd = Mathf.Clamp(elevCmd, -t.elevatorDownDeg, t.elevatorUpDeg);
            float rate = t.actuatorRate * dt;
            _elev = Mathf.MoveTowards(_elev, elevCmd, rate);
            _ail = Mathf.MoveTowards(_ail, rollIn * t.aileronDeg, rate);
            _rud = Mathf.MoveTowards(_rud, c.Yaw * t.rudderDeg, rate);
            // flaps are electric: they stop without power and draw current while moving
            bool wingCover = _kitNet != null && (_kitNet.WingCoverL || _kitNet.WingCoverR);
            if (wingCover) _ail = Mathf.MoveTowards(_ail, 0f, rate);          // И4 K4.3: held in neutral under a wing cover
            float flapTarget = wingCover ? 0f : t.flapDetents[Mathf.Clamp(s.FlapLever, 0, t.flapDetents.Length - 1)];
            bool flapsMoving = _systems.Electrics.BusPowered && Mathf.Abs(_flap - flapTarget) > 0.01f;
            if (flapsMoving) _flap = Mathf.MoveTowards(_flap, flapTarget, t.flapRate * dt);

            if (Time.time >= _nextLooseCheck)
            {
                _nextLooseCheck = Time.time + 0.25f;
                _cargoLoose = CargoLooseInBay();
                UpdateBayStowage();
            }
            int doorsRunning = 0;
            foreach (NetworkDoor d in Doors()) if (d != null && d.ActuatorRunning) doorsRunning++;
            bool belt = (OccupantOf(0) != SeatRegistry.Empty && !s.On(PlaneSwitch.BeltP)) ||
                        (seats.Length > 1 && OccupantOf(1) != SeatRegistry.Empty && !s.On(PlaneSwitch.BeltC));

            Vector3 cg = Body.worldCenterOfMass;
            float oat = EnvironmentProvider.TemperatureAt(cg.y);
            float rho = EnvironmentProvider.DensityAt(cg.y);
            _systems.Step(new AircraftSystems.Inputs
            {
                Switches = s,
                Throttle = c.Throttle,
                Airspeed = physics.Last.Airspeed,
                Rho = rho,
                OatC = oat,
                DoorActuatorsRunning = doorsRunning,
                FlapsMoving = flapsMoving,
                AutopilotOn = _ap.Engaged,
                CabinDoorOpen = NotClosed(pilotDoorL) || NotClosed(pilotDoorR),
                CargoDoorOpen = NotClosed(cargoDoorL) || NotClosed(cargoDoorR),
                CargoLoose = _cargoLoose,
                BeltWarning = belt,
                Heater = _kitNet != null && _kitNet.Preheating,
                Insulated = _kitNet != null && _kitNet.Insulated,
            }, dt);

            // toe brakes split by the pedals (a full right pedal brakes only the right wheel); parking brake on both
            bool moveBlocked = _kitNet != null && _kitNet.MoveBlocked;
            float park = s.On(PlaneSwitch.ParkBrake) || moveBlocked ? 1f : 0f;   // И4: chocks, tie-downs, heater duct
            TurbopropEngine e = _systems.Engine;
            physics.Step(new AircraftPhysics.Inputs
            {
                ElevatorDeg = _elev, AileronDeg = _ail, RudderDeg = _rud, FlapDeg = _flap,
                TailSteerDeg = c.Yaw * t.tailSteerDeg,
                BrakeLeft = Mathf.Max(park, c.Brake * Mathf.Clamp01(1f - c.Yaw)),
                BrakeRight = Mathf.Max(park, c.Brake * Mathf.Clamp01(1f + c.Yaw)),
                ThrustN = e.ThrustN, ShaftTorqueNm = e.TorqueNm, PropOmega = e.PropOmega,
                WingCoverL = _kitNet != null && _kitNet.WingCoverL, WingCoverR = _kitNet != null && _kitNet.WingCoverR,
            }, dt);
            HoldParked(moveBlocked && physics.Last.OnGround);
            Pour(dt);

            UpdateMass(false);
            _climate.Step(oat, PeopleAboard(), e.Combusting, s.On(PlaneSwitch.Defrost) ? 1f : 0f, s.On(PlaneSwitch.CabinHeat) ? 1f : 0f, dt);
            Publish(dt, cg);
            if (Time.time >= _nextLog)
            {
                _nextLog = Time.time + logInterval;
                PlaneTelemetry tm = _telemetry.Value;
                BenchLog.Event("plane", "p", transform.position, "r", transform.eulerAngles, "ias", tm.IasKt, "alt", tm.AltFt,
                    "alpha", tm.AlphaDeg, "ng", tm.NgPct, "itt", tm.IttC, "bus", tm.BusVolts, "fuel", tm.FuelTotalL, "mass", tm.MassKg,
                    "thr", c.Throttle, "flap", _flap, "ground", physics.Last.OnGround);
            }
        }

        static bool NotClosed(NetworkDoor d) => d != null && d.State != DoorState.Closed;

        /// <summary>
        /// И4 K4.3 MoveBlocked (ENGINEERING): chocks, tie-downs or the heater duct hold the parked aircraft. Brakes are full,
        /// and a stiff horizontal spring-damper at the centre of mass plus one about the vertical keep it where it was when
        /// it got blocked: thrust does not push it off, the struts still work.
        /// </summary>
        void HoldParked(bool hold)
        {
            if (!hold) { _held = false; return; }
            if (!_held) { _held = true; _holdPos = Body.worldCenterOfMass; _holdYaw = transform.eulerAngles.y; }
            AircraftTuning t = tuning;
            Vector3 d = Body.worldCenterOfMass - _holdPos; d.y = 0f;
            Vector3 v = Body.linearVelocity; v.y = 0f;
            Body.AddForce(-t.holdSpring * d - t.holdDamper * v, ForceMode.Force);
            float yaw = Mathf.DeltaAngle(_holdYaw, transform.eulerAngles.y) * Mathf.Deg2Rad;
            Body.AddTorque(Vector3.up * (-t.holdYawSpring * yaw - t.holdYawDamper * Body.angularVelocity.y), ForceMode.Force);
        }

        /// <summary>
        /// И4 K4.5 (§9.6, M7): from the step ladder, the cap open, the jerry can in hand: fuel runs into that tank at
        /// jerryPourLpm (TEMPORARY) while the use button is held; the can gets lighter.
        /// </summary>
        bool ServerStartPour(ulong client, string id, out string reason)
        {
            reason = null;
            bool left = id.EndsWith("_L");
            CargoItem held = CargoItem.HeldBy(client);
            if (held == null || !held.TryGetComponent(out KitCarry can) || !can.IsJerryCan) return false;   // not pouring: the cap toggles
            if (!_switches.Value.On(left ? PlaneSwitch.FuelCapL : PlaneSwitch.FuelCapR)) { reason = "Сначала откройте крышку бака"; return true; }
            if (can.FuelLitres <= 0f) { reason = "Канистра пуста"; return true; }
            if (!OnLadder(client)) { reason = "Нужна стремянка"; return true; }
            _pours[client] = left;
            BenchLog.Event("pour_start", "client", client, "left", left, "can", can.FuelLitres);
            return true;
        }

        // the player stands on a step ladder (its platform or a tread) — the server's own check, owner movement is trusted (§11.3)
        bool OnLadder(ulong client)
        {
            if (!NetworkManager.ConnectedClients.TryGetValue(client, out var c) || c.PlayerObject == null) return false;
            Vector3 feet = c.PlayerObject.transform.position;
            foreach (var h in Physics.RaycastAll(feet + Vector3.up * 0.1f, Vector3.down, 0.4f, ~0, QueryTriggerInteraction.Ignore))
                if (h.collider.GetComponentInParent<StepLadderItem>() != null) return true;
            return false;
        }

        void Pour(float dt)
        {
            if (_pours.Count == 0) return;
            List<ulong> stop = null;
            foreach (var kv in _pours)
            {
                CargoItem held = CargoItem.HeldBy(kv.Key);
                bool cap = _switches.Value.On(kv.Value ? PlaneSwitch.FuelCapL : PlaneSwitch.FuelCapR);
                if (held == null || !held.TryGetComponent(out KitCarry can) || !can.IsJerryCan || can.FuelLitres <= 0f || !cap)
                {
                    (stop ??= new List<ulong>()).Add(kv.Key);
                    continue;
                }
                float taken = _systems.Fuel.Add(kv.Value, Mathf.Min(can.FuelLitres, tuning.jerryPourLpm / 60f * dt));
                can.ServerSetFuel(can.FuelLitres - taken);
                if (taken <= 0f) (stop ??= new List<ulong>()).Add(kv.Key);
            }
            if (stop != null) foreach (var c in stop) { _pours.Remove(c); BenchLog.Event("pour_stop", "client", c); }
        }

        /// <summary>Server: people breathing in the cabin and bay (glass fog): seated ones and players standing inside.</summary>
        int PeopleAboard()
        {
            int n = 0;
            for (int i = 0; i < seats.Length; i++) if (OccupantOf(i) != SeatRegistry.Empty) n++;
            foreach (var c in NetworkManager.ConnectedClientsList)
            {
                if (c.PlayerObject == null || Registry.SeatOf(c.ClientId) >= 0) continue;
                if (IsInside(c.PlayerObject.transform.position + Vector3.up * 1.2f)) n++;
            }
            return n;
        }

        /// <summary>Server: is this client in the cabin or bay (seated, or standing inside)?</summary>
        bool ClientInside(ulong client)
        {
            if (Registry.SeatOf(client) >= 0) return true;
            return NetworkManager.ConnectedClients.TryGetValue(client, out var c) && c.PlayerObject != null
                   && IsInside(c.PlayerObject.transform.position + Vector3.up * 1.4f);
        }

        // true attitude for the autopilot's own sensors: deg, bank + = right wing down, pitch + = nose up; rates in deg/s
        // with the same signs; altitude of the centre of mass in ft, vertical speed in fpm
        void Attitude(out float heading, out float bank, out float pitch, out float rollRate, out float pitchRate, out float altFt, out float vsFpm)
        {
            Vector3 f = transform.forward, r = transform.right, u = transform.up, w = Body.angularVelocity;
            pitch = Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg;
            bank = Mathf.Atan2(-r.y, u.y) * Mathf.Rad2Deg;
            heading = Mathf.Repeat(Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg, 360f);
            rollRate = -Vector3.Dot(w, f) * Mathf.Rad2Deg;     // rotation about +Z lifts the right wing: that is a left roll
            pitchRate = -Vector3.Dot(w, r) * Mathf.Rad2Deg;    // rotation about +X lowers the nose
            altFt = Body.worldCenterOfMass.y * 3.28084f;
            vsFpm = Body.linearVelocity.y * 196.85f;
        }

        /// <summary>Server: the autopilot key from the pilot seat (SEAT-01: never from the passenger).</summary>
        public override bool ServerPilotCommand(ulong client, PilotCommand command, out string reason)
        {
            reason = null;
            if (command != PilotCommand.AutopilotToggle) { reason = "Здесь этого нет"; return false; }
            if (!Registry.IsPilot(client)) { reason = "Автопилот — только из левого кресла"; return false; }
            if (_ap.Engaged)
            {
                _ap.Disengage(Autopilot.ByPilot);
                BenchLog.Event("autopilot", "on", false, "reason", "pilot");
                return true;
            }
            if (!(_systems.Electrics.BusPowered && _switches.Value.On(PlaneSwitch.Avionics))) { reason = "Автопилот: нужны АКБ и авионика"; return false; }
            if (physics.Last.OnGround) { reason = "Автопилот: только в полёте"; return false; }
            Attitude(out float hdg, out float bank, out float pitch, out _, out _, out float altFt, out _);
            if (!Autopilot.CanEngage(bank, pitch, out reason)) return false;
            _ap.Engage(hdg, altFt, pitch);
            BenchLog.Event("autopilot", "on", true, "hdg", hdg, "altFt", altFt);
            return true;
        }

        bool CargoLooseInBay()
        {
            int mask = ~(1 << LayerMask.NameToLayer("Interact"));
            int n = Physics.OverlapBoxNonAlloc(transform.TransformPoint(bayCenter), bayHalfExtents, _bayHits, transform.rotation,
                mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var item = _bayHits[i].GetComponentInParent<CargoItem>();
                if (item != null && item.State != CargoState.Secured) return true;
            }
            return false;
        }

        /// <summary>
        /// MASS-01: one described way. Rigidbody = empty aircraft + fuel in the two tanks + 85 kg per occupied seat, with the
        /// centre of mass and a diagonal inertia (parallel-axis point masses) from those. Cargo is never added here: loose
        /// items press on the floor through contacts, secured ones hang on FixedJoints, so it is never counted twice.
        /// </summary>
        void UpdateMass(bool force)
        {
            AircraftTuning t = tuning;
            int occupants = 0;
            for (int i = 0; i < seats.Length; i++) if (OccupantOf(i) != SeatRegistry.Empty) occupants++;
            float fuelL = _systems.Fuel.LeftKg, fuelR = _systems.Fuel.RightKg;
            float mass = t.emptyMassKg + fuelL + fuelR + occupants * t.personMassKg;
            if (!force && Mathf.Abs(mass - _appliedMass) < 0.5f && occupants == _appliedOccupants) return;
            _appliedMass = mass;
            _appliedOccupants = occupants;

            Vector3 comEmpty = centerOfMass != null ? transform.InverseTransformPoint(centerOfMass.position) : new Vector3(0f, 1.75f, 1.95f);
            Vector3 sum = comEmpty * t.emptyMassKg + t.tankCentreL * fuelL + t.tankCentreR * fuelR;
            for (int i = 0; i < seats.Length; i++)
                if (OccupantOf(i) != SeatRegistry.Empty) sum += transform.InverseTransformPoint(seats[i].pelvis.position) * t.personMassKg;
            Vector3 com = sum / mass;

            Vector3 inertia = t.emptyInertia + PointInertia(t.emptyMassKg, comEmpty - com)
                              + PointInertia(fuelL, t.tankCentreL - com) + PointInertia(fuelR, t.tankCentreR - com);
            for (int i = 0; i < seats.Length; i++)
                if (OccupantOf(i) != SeatRegistry.Empty)
                    inertia += PointInertia(t.personMassKg, transform.InverseTransformPoint(seats[i].pelvis.position) - com);
            physics.ApplyMass(mass, com, inertia);
        }

        static Vector3 PointInertia(float m, Vector3 d) => m * new Vector3(d.y * d.y + d.z * d.z, d.x * d.x + d.z * d.z, d.x * d.x + d.y * d.y);

        void Publish(float dt, Vector3 cg)
        {
            TurbopropEngine e = _systems.Engine;
            ElectricalSystem el = _systems.Electrics;
            FuelSystem fuel = _systems.Fuel;
            AircraftPhysics.Outputs o = physics.Last;

            // sensors: attitude from the airframe, side force for the ball, pressure at the static port
            Vector3 v = Body.linearVelocity;
            Vector3 accel = (v - _lastVelocity) / dt;
            _lastVelocity = v;
            Vector3 specific = accel - Physics.gravity;
            Vector3 fwd = transform.forward, right = transform.right, up = transform.up;
            float pitch = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;
            float bank = Mathf.Atan2(-right.y, up.y) * Mathf.Rad2Deg;
            float heading = Mathf.Repeat(Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg, 360f);
            float yawRate = Vector3.Dot(Body.angularVelocity, Vector3.up) * Mathf.Rad2Deg;
            bool bus = el.BusPowered;
            bool vacuum = e.Combusting && e.Ng >= tuning.idleNg - 1f;
            _instruments.Step(o.AirVelocityBody.z, EnvironmentProvider.PressureAt(cg.y), pitch, bank, heading, yawRate,
                Vector3.Dot(specific, right) / 9.81f, bus, _switches.Value.AltSettingInHg, vacuum, dt);

            float stallMargin = o.StallAlphaDeg - o.WingAlphaDeg;
            const float Gal = 3.78541f;
            var tm = new PlaneTelemetry
            {
                Elevator = _elev, Aileron = _ail, Rudder = _rud, Flap = _flap, TailSteer = Controls.Yaw * tuning.tailSteerDeg,
                GearL = o.GearL, GearR = o.GearR, GearT = o.GearT, WheelMain = o.WheelMain, WheelTail = o.WheelTail,
                PropRpm = e.PropRpm,
                IasKt = _kitNet != null && _kitNet.AirspeedBlocked ? 0f : _instruments.IasKt, AltFt = _instruments.AltFt, VsiFpm = _instruments.VsiFpm,
                PitchDeg = _instruments.PitchDeg, BankDeg = _instruments.BankDeg, HeadingDeg = _instruments.HeadingDeg,
                TurnRate = _instruments.TurnRate, Slip = _instruments.Slip,
                // electric gauges read zero without bus power (§9.5)
                TorqueFtLb = bus ? e.TorqueFtLb : 0f, PropGauge = bus ? e.PropRpm : 0f, IttC = bus ? e.IttC : 0f,
                NgPct = bus ? e.Ng : 0f, OilPsi = bus ? e.OilPsi : 0f, OilC = bus ? e.OilC : 0f,
                FuelFlowPph = bus ? e.FuelFlowPph : 0f, FuelLGal = bus ? fuel.LeftL / Gal : 0f, FuelRGal = bus ? fuel.RightL / Gal : 0f,
                Amps = el.BatteryAmps, OatC = EnvironmentProvider.TemperatureAt(cg.y), BusVolts = el.BusVolts,
                SuctionInHg = _instruments.SuctionInHg, CompassDeg = _instruments.CompassDeg,
                FogWindshield = _climate.Windshield, FogSide = _climate.Side,
                Lamps = _systems.Lamps, BusPowered = bus,
                StallWarning = bus && o.Airspeed > 8f && stallMargin < tuning.stallWarningMarginDeg,
                EngineRunning = e.Combusting,
                ApEngaged = _ap.Engaged, ApReason = _ap.DisengageCode, ApHeading = _ap.TargetHeading, ApAltFt = _ap.TargetAltitudeFt,
                Nav = B(_systems.Nav), Strobe = B(_systems.Strobe), Beacon = B(_systems.Beacon), LandingLight = B(_systems.Landing),
                Cabin = B(_systems.Cabin), Cargo = B(_systems.Cargo), Panel = B(_systems.Panel), Flood = B(_systems.Flood),
                Avionics = B(_systems.Avionics),
                AlphaDeg = o.WingAlphaDeg, TrueAirspeed = o.AirVelocityBody.magnitude,
                GroundSpeed = new Vector2(v.x, v.z).magnitude, HeightAgl = o.HeightAgl, MassKg = Body.mass,
                FuelTotalL = fuel.LeftL + fuel.RightL, BatteryCharge = el.Charge,
            };
            _telemetry.Value = tm;
        }

        static byte B(float x) => (byte)Mathf.RoundToInt(Mathf.Clamp01(x) * 255f);

        // ================================================================ interaction (server)
        public override bool ServerUse(ulong clientId, string id, UsePhase phase, out string reason)
        {
            reason = null;
            PlaneSwitchState s = _switches.Value;
            if (_kitNet != null && PlaneWinterKitNet.IsKitId(id)) return _kitNet.ServerUse(clientId, id, phase, out reason);   // И4
            if (id.StartsWith("Socket_FuelCap_", System.StringComparison.Ordinal))
            {
                if (phase == UsePhase.Release) { _pours.Remove(clientId); return true; }
                if (phase == UsePhase.Press && ServerStartPour(clientId, id, out reason)) return reason == null;
            }
            if (id == "SW_Starter" && phase == UsePhase.Press && _kitNet != null && _kitNet.StartBlocked)
            {
                reason = _kitNet.StartBlockText();                           // И4 K4.3: plugs, engine cover or heater on
                return false;
            }

            // momentary controls: held while pressed
            if (id == "SW_Starter" || id == "Btn_AnnunTest")
            {
                PlaneSwitch sw = id == "SW_Starter" ? PlaneSwitch.Starter : PlaneSwitch.AnnunTest;
                bool on = phase == UsePhase.Press;
                s.Set(sw, on);
                if (sw == PlaneSwitch.Starter) _starterHolder = on ? clientId : SeatRegistry.Empty;
                else _testHolder = on ? clientId : SeatRegistry.Empty;
                SetSwitches(s, id, clientId);
                return true;
            }
            if (phase == UsePhase.Release) return true;
            int dir = phase == UsePhase.Back ? -1 : 1;                    // Back: the wheel turned the other way (knobs only)
            if (IsKnob(id)) return ServerKnob(clientId, id, dir, out reason);
            if (phase != UsePhase.Press) return true;

            int seat = SeatIndex(id);
            if (seat >= 0)
            {
                int pax = PaxIndex(id);
                if (pax >= 0 && !s.PaxDown(pax)) { reason = "Кресло сложено"; return false; }
                return ServerSit(clientId, seat, out reason);
            }
            if (id.StartsWith("PaxSeatFold_", System.StringComparison.Ordinal)) return ServerPaxFold(clientId, PaxIndex(id.Replace("PaxSeatFold_", "PaxSeat_")), out reason);
            if (id.StartsWith("Stow_", System.StringComparison.Ordinal)) return ServerStowUse(clientId, id, out reason);

            if (Toggles.TryGetValue(id, out PlaneSwitch toggle))
            {
                s.Set(toggle, !s.On(toggle));
                SetSwitches(s, id, clientId);
                return true;
            }
            switch (id)
            {
                case "SW_CargoDoor_L":
                case "SW_CargoDoor_R":
                {
                    PlaneSwitch sw = id == "SW_CargoDoor_L" ? PlaneSwitch.CargoDoorL : PlaneSwitch.CargoDoorR;
                    bool open = !s.On(sw);
                    s.Set(sw, open);
                    SetSwitches(s, id, clientId);
                    (id == "SW_CargoDoor_L" ? cargoDoorL : cargoDoorR).Command(open, DoorDrive.Powered);   // runs when powered
                    return true;
                }
                case "SW_CabinDoor_L":
                case "SW_CabinDoor_R":
                {
                    // v7 door switches (M1 closed): like the cargo doors, the actuator runs only with bus power (§8.2)
                    PlaneSwitch sw = id == "SW_CabinDoor_L" ? PlaneSwitch.CabinDoorL : PlaneSwitch.CabinDoorR;
                    bool open = !s.On(sw);
                    s.Set(sw, open);
                    SetSwitches(s, id, clientId);
                    (id == "SW_CabinDoor_L" ? pilotDoorL : pilotDoorR).Command(open, DoorDrive.Powered);
                    return true;
                }
                case "Socket_Keyhole_PilotDoor_L":
                case "Socket_Keyhole_PilotDoor_R":
                    return ServerLock(clientId, id, out reason);
                case "Lever_Prop":
                    s.PropLever = s.PropLever > 0.75f ? 0.5f : s.PropLever > 0.25f ? 0f : 1f;
                    SetSwitches(s, id, clientId);
                    return true;
                case "Lever_Flap":
                    if (s.FlapLever >= tuning.flapDetents.Length - 1) _flapDirection = -1;
                    else if (s.FlapLever == 0) _flapDirection = 1;
                    s.FlapLever = (byte)Mathf.Clamp(s.FlapLever + _flapDirection, 0, tuning.flapDetents.Length - 1);
                    SetSwitches(s, id, clientId);
                    return true;
                case "Lever_Power":
                    reason = "РУД — клавиши Shift / Ctrl (только из левого кресла)";
                    return false;
                case "TrimWheel":
                    ServerSetTrim(0f);
                    BenchLog.Event("switch", "id", id, "client", clientId, "trim", 0f);
                    return true;
                // handles: inside always by hand (the M1 workaround "inner handle = button" is gone, И2 E.2);
                // outside only while the plane is not locked (§8.3)
                case "Socket_Handle_PilotDoor_L_In": pilotDoorL.Toggle(DoorDrive.Manual); return true;
                case "Socket_Handle_PilotDoor_R_In": pilotDoorR.Toggle(DoorDrive.Manual); return true;
                case "Socket_Handle_CargoDoor_L_In": cargoDoorL.Toggle(DoorDrive.Manual); return true;
                case "Socket_Handle_CargoDoor_R_In": cargoDoorR.Toggle(DoorDrive.Manual); return true;
                case "Socket_Handle_PilotDoor_L_Out":
                case "Socket_Handle_PilotDoor_R_Out":
                case "Socket_Handle_CargoDoor_L_Out":
                case "Socket_Handle_CargoDoor_R_Out":
                {
                    if (s.On(PlaneSwitch.Locked)) { reason = "Заперто"; BenchLog.Event("locked_refuse", "client", clientId, "id", id); return false; }
                    NetworkDoor d = id.Contains("PilotDoor_L") ? pilotDoorL : id.Contains("PilotDoor_R") ? pilotDoorR
                        : id.Contains("CargoDoor_L") ? cargoDoorL : cargoDoorR;
                    d.Toggle(DoorDrive.Manual);
                    return true;
                }
            }
            reason = "Нет действия";
            return false;
        }

        /// <summary>
        /// И1.4: the bay's stowage boxes (kind stow) and the troop seat zones (PaxSeat_*, kind seat) overlap the floor,
        /// the cargo and the tie-down rings; they are transparent until they can be used: stowage only with a fitting item
        /// in hand, a troop seat only when it is down. Until the bay is wired up (И3) both stay closed.
        /// </summary>
        public override bool ZoneOpen(UtilityPlane.UtilityPlaneInteractable zone, CargoItem held)
        {
            if (zone.kind == "stow") return StowFor(zone.id, held, out _) != null;
            if (zone.kind == "kit") return _kitNet != null && _kitNet.ZoneOpen(zone.id, held);
            if (zone.kind == "seat") { int i = PaxIndex(zone.id); return i < 0 || Switches.PaxDown(i); }
            return true;
        }

        // ================================================================ cargo bay: troop seats, stowage (И3)
        static int PaxIndex(string id) => System.Array.IndexOf(UtilityPlane.UtilityPlaneRig.PaxSeatNames, id);

        /// <summary>The stow point of a Stow_* zone that takes this held item now, or null (with the reason).</summary>
        StowPoint StowFor(string zoneId, CargoItem held, out string reason)
        {
            reason = "Сюда это не кладётся";
            if (held == null) return null;
            foreach (var p in stowPoints)
            {
                if (p == null || p.ZoneId != zoneId) continue;
                if (p.accepts != held.itemKey) return null;
                if (p.Occupant != null || (held.State == CargoState.Stowed && held.Stow != p)) { reason = "Место занято"; return null; }
                if (p.stowId == "Ladder" && (Switches.PaxDown(1) || Switches.PaxDown(3))) { reason = "Сначала сложите кресла 2 и 4"; return null; }
                reason = null;
                return p;
            }
            return null;
        }

        /// <summary>
        /// Server, И3 L.3: the player carries a fitting item and uses the zone: it goes onto the stow point. The ladder also
        /// needs its floor free of cargo and seats R1/R2 folded (L.5).
        /// </summary>
        bool ServerStowUse(ulong client, string zoneId, out string reason)
        {
            CargoItem held = CargoItem.HeldBy(client);
            StowPoint p = StowFor(zoneId, held, out reason);
            if (p == null) return false;
            if (p.stowId == "Ladder" && BoxHasCargo(ladderStowBox, held, out string what)) { reason = "Мешает " + what; return false; }
            if (!held.ServerStow(client, p, out reason)) return false;
            UpdateBayStowage();
            return true;
        }

        /// <summary>
        /// Server, И3 K.3: fold or unfold a troop seat. Only an empty seat; R1/R2 not while the ladder is stowed; down only
        /// if its pan and leg in the down pose would not hit cargo or a player.
        /// </summary>
        bool ServerPaxFold(ulong client, int i, out string reason)
        {
            reason = null;
            if (i < 0) { reason = "Нет действия"; return false; }
            PlaneSwitchState s = _switches.Value;
            int seat = SeatIndex(UtilityPlane.UtilityPlaneRig.PaxSeatNames[i]);
            if (seat >= 0 && OccupantOf(seat) != SeatRegistry.Empty) { reason = "Кресло занято"; return false; }
            bool down = !s.PaxDown(i);
            if (down && (i == 1 || i == 3) && s.LadderStowed) { reason = "Мешает стремянка"; return false; }
            if (down)
                foreach (var b in paxDownBoxes)
                    if (b.seat == i && BoxHasCargo(b, null, out string what)) { reason = "Мешает " + what; return false; }
            s.PaxSeats = (byte)(down ? s.PaxSeats | (1 << i) : s.PaxSeats & ~(1 << i));
            SetSwitches(s, "PaxSeatFold_" + i, client);
            return true;
        }

        /// <summary>Server: does a box (prefab space) overlap loose cargo or a player? Stowed items are in their places.</summary>
        bool BoxHasCargo(BoxPose b, CargoItem except, out string what)
        {
            what = null;
            int mask = ~(1 << LayerMask.NameToLayer("Interact"));
            int n = Physics.OverlapBoxNonAlloc(transform.TransformPoint(b.center), b.half, _boxHits, transform.rotation * b.rotation, mask, QueryTriggerInteraction.Ignore);
            for (int k = 0; k < n; k++)
            {
                Collider c = _boxHits[k];
                if (c is CharacterController) { what = "игрок"; return true; }
                var item = c.GetComponentInParent<CargoItem>();
                if (item == null || item == except || item.State == CargoState.Stowed) continue;
                what = "груз";
                return true;
            }
            return false;
        }

        /// <summary>Server: LadderStowed and the covered rings follow the stow points (a ladder taken out by grab frees them).</summary>
        void UpdateBayStowage()
        {
            PlaneSwitchState s = _switches.Value;
            bool ladder = false, jerry = false;
            foreach (var p in stowPoints)
            {
                if (p == null || p.Occupant == null) continue;
                if (p.stowId == "Ladder") ladder = true;
                if (p.stowId == "JerryCan") jerry = true;
            }
            foreach (var r in ladderRings) if (r != null) r.Blocked = ladder ? "Кольцо под стремянкой" : null;
            if (jerryCanRing != null) jerryCanRing.Blocked = jerry ? "Кольцо под канистрой" : null;
            if (s.LadderStowed == ladder) return;
            s.LadderStowed = ladder;
            SetSwitches(s, "ladder_stowed", 0);
        }

        /// <summary>
        /// Server, И3 K.7–K.8: troop seats get up inside. TEMPORARY: not while the aircraft moves (ground speed ≥ 0.5 m/s,
        /// walking in a moving aircraft is DEFERRED). With the ladder stowed, L1/L2 get out through the open left cargo door.
        /// </summary>
        protected override bool ServerInsideExit(ulong client, SeatAnchor a, out Vector3 feet, out string reason)
        {
            feet = a.exit.position;
            if (Telemetry.GroundSpeed >= 0.5f) { reason = "Самолёт движется"; return false; }
            if (Switches.LadderStowed && (a.seatId == "PaxSeat_L1" || a.seatId == "PaxSeat_L2"))
            {
                if (cargoDoorL == null || cargoDoorL.State != DoorState.Open) { reason = "Мешает стремянка: откройте левую грузовую дверь"; return false; }
                if (exitCargoL != null && FindExitAt(exitCargoL, out feet)) { reason = null; return true; }
                reason = "Нет места для выхода у левой грузовой двери";
                return false;
            }
            return base.ServerInsideExit(client, a, out feet, out reason);
        }

        /// <summary>
        /// Server: a knob turned one step (dir +1 = clockwise as the pilot sees it, -1 = back; a click is +1, the mouse
        /// wheel gives both). Rheostats 25 %, altimeter 0.01 inHg (28.60..31.00), heading bug 1°, DG set 1° (shifts the
        /// heading card), radio knobs 15° (they only turn: there are no radio systems yet, И2 F.3), transponder OFF..TST.
        /// </summary>
        bool ServerKnob(ulong client, string id, int dir, out string reason)
        {
            reason = null;
            PlaneSwitchState s = _switches.Value;
            switch (id)
            {
                case "Rheo_Panel": s.RheoPanel = Step(s.RheoPanel, dir); break;
                case "Rheo_Flood": s.RheoFlood = Step(s.RheoFlood, dir); break;
                case "Knob_ALT_Baro": s.AltSetting = (ushort)Mathf.Clamp(s.AltSetting + dir, 2860, 3100); break;
                case "Knob_HDG_Bug": s.HdgBug = (ushort)((s.HdgBug + dir + 360) % 360); break;
                case "Knob_DG_Set":
                    s.DgKnob = (short)(s.DgKnob + dir);
                    _instruments?.ShiftHeading(dir);
                    break;
                case "Knob_XPDR_Mode":
                    s.Xpdr = (byte)(dir > 0 ? (s.Xpdr + 1) % 5 : Mathf.Max(0, s.Xpdr - 1));    // a click past TST wraps to OFF
                    break;
                default:
                {
                    int i = System.Array.IndexOf(UtilityPlane.UtilityPlaneRig.RadioKnobNames, id);
                    if (i < 0) { reason = "Нет действия"; return false; }
                    s.SetRadioKnob(i, s.RadioKnob(i) + dir);
                    break;
                }
            }
            SetSwitches(s, id, client);
            return true;
        }

        // rheostat: a click steps up and wraps to off (P03 behaviour), the wheel back steps down
        static float Step(float v, int dir) => dir > 0 ? (v >= 0.99f ? 0f : Mathf.Min(1f, Mathf.Round(v * 4f + 1f) / 4f))
                                                       : Mathf.Max(0f, Mathf.Round(v * 4f - 1f) / 4f);

        /// <summary>
        /// Server: the key in a door lock (§8.3, model v7 Lock_PilotDoor_L/R). Only from outside (locking from inside is
        /// OPEN, by default no). With the key in the pocket or in hand it toggles Locked / Unlocked; without, "Заперто".
        /// </summary>
        bool ServerLock(ulong client, string id, out string reason)
        {
            reason = null;
            PlaneSwitchState s = _switches.Value;
            if (ClientInside(client)) { reason = "Изнутри замок не закрыть"; return false; }
            if (!PlaneKey.ClientHasKey(client)) { reason = s.On(PlaneSwitch.Locked) ? "Заперто: нужен ключ" : "Нужен ключ"; return false; }
            s.Set(PlaneSwitch.Locked, !s.On(PlaneSwitch.Locked));
            SetSwitches(s, id, client);
            BenchLog.Event("lock", "client", client, "locked", s.On(PlaneSwitch.Locked));
            return true;
        }

        // ================================================================ prints on the fogged glass (И2 G.2)
        /// <summary>Any peer: ask the server for a print at a point on the glass (plane space). Dev key for now (G.3).</summary>
        public void RequestFogPrint(Vector3 planePoint, bool clear) => FogPrintRequestRpc(planePoint, clear);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void FogPrintRequestRpc(Vector3 planePoint, bool clear, RpcParams rpc = default)
        {
            if (planePoint.sqrMagnitude > 100f) return;                     // not on this aircraft
            BenchLog.Event("fog_print", "client", rpc.Receive.SenderClientId, "p", planePoint, "clear", clear);
            if (clear) FogClearRpc();
            else FogPrintRpc(planePoint, (byte)UtilityPlane.UtilityPlaneRig.FogBrush.Hand, 0.18f, 0f, true, 1f);
        }

        // an event to every peer, host included; late joiners do not get old wiped spots (they regrow in 25 s anyway)
        [Rpc(SendTo.ClientsAndHost)]
        void FogPrintRpc(Vector3 planePoint, byte brush, float size, float angle, bool wipe, float strength)
        {
            var rig = GetComponent<UtilityPlane.UtilityPlaneRig>();
            if (rig != null) rig.StampFogPrint(rig.transform.TransformPoint(planePoint), (UtilityPlane.UtilityPlaneRig.FogBrush)brush, size, angle, wipe, strength);
        }

        [Rpc(SendTo.ClientsAndHost)]
        void FogClearRpc()
        {
            var rig = GetComponent<UtilityPlane.UtilityPlaneRig>();
            if (rig != null) rig.ClearFogPrints();
        }

        void SetSwitches(PlaneSwitchState s, string id, ulong client)
        {
            _switches.Value = s;
            BenchLog.Event("switch", "id", id, "client", client, "bits", s.Bits, "flap", s.FlapLever, "prop", s.PropLever);
        }

        protected override void OnSeatReleased(ulong client, int seat)
        {
            // O-05: the aircraft is not stopped and the autopilot is not left flying for a pilot who got out
            if (seats[seat].role == SeatRole.Pilot && _ap != null) _ap.Disengage(Autopilot.PilotLeft);
            // nothing stays held by a player who got out or dropped (INPUT-02: no stuck commands)
            PlaneSwitchState s = _switches.Value;
            bool changed = false;
            if (_starterHolder == client) { s.Set(PlaneSwitch.Starter, false); _starterHolder = SeatRegistry.Empty; changed = true; }
            if (_testHolder == client) { s.Set(PlaneSwitch.AnnunTest, false); _testHolder = SeatRegistry.Empty; changed = true; }
            if (changed) SetSwitches(s, "release_on_exit", client);
        }

        // ================================================================ hover hints (any peer, replicated state)
        public override string Describe(string id)
        {
            if (_kitNet != null && _kitNet.Kit != null && System.Enum.TryParse(id, out UtilityPlane.KitItem kitItem))
                return _kitNet.Kit.IsInstalled(kitItem) ? "стоит" : "снято";
            int seat = SeatIndex(id);
            if (seat >= 0) return SeatText(seat);
            if (id.StartsWith("PaxSeatFold_")) return Switches.PaxDown(PaxIndex(id.Replace("PaxSeatFold_", "PaxSeat_"))) ? "опущено" : "сложено";
            PlaneSwitchState s = _switches.Value;
            if (Toggles.TryGetValue(id, out PlaneSwitch t))
            {
                bool on = s.On(t);
                if (id.StartsWith("Pull_")) return on ? "вытянута" : "утоплена";
                if (id.StartsWith("FuelSel_")) return on ? "открыт" : "закрыт";
                if (id.StartsWith("Socket_Belt_")) return on ? "застёгнут" : "расстёгнут";
                if (id.StartsWith("Socket_FuelCap_")) return on ? "открыта" : "закрыта";
                if (id.StartsWith("Visor_")) return on ? "опущен" : "поднят";
                if (id == "Lever_Condition") return on ? "ОСТАНОВ" : "МАЛЫЙ ГАЗ";
                if (id == "Glovebox_Lid") return on ? "открыт" : "закрыт";
                return on ? "ВКЛ" : "ВЫКЛ";
            }
            switch (id)
            {
                case "SW_Starter": return s.On(PlaneSwitch.Starter) ? "крутит" : "отпущен";
                case "Btn_AnnunTest": return s.On(PlaneSwitch.AnnunTest) ? "нажата" : null;
                case "SW_CargoDoor_L": return (s.On(PlaneSwitch.CargoDoorL) ? "ОТКР" : "ЗАКР") + ", дверь " + DoorText(cargoDoorL);
                case "SW_CargoDoor_R": return (s.On(PlaneSwitch.CargoDoorR) ? "ОТКР" : "ЗАКР") + ", дверь " + DoorText(cargoDoorR);
                case "SW_CabinDoor_L": return (s.On(PlaneSwitch.CabinDoorL) ? "ОТКР" : "ЗАКР") + ", дверь " + DoorText(pilotDoorL);
                case "SW_CabinDoor_R": return (s.On(PlaneSwitch.CabinDoorR) ? "ОТКР" : "ЗАКР") + ", дверь " + DoorText(pilotDoorR);
                case "Socket_Keyhole_PilotDoor_L":
                case "Socket_Keyhole_PilotDoor_R": return s.On(PlaneSwitch.Locked) ? "заперто" : "открыто";
                case "Knob_ALT_Baro": return $"{s.AltSettingInHg:0.00} inHg";
                case "Knob_HDG_Bug": return $"метка {s.HdgBug:000}°";
                case "Knob_DG_Set": return $"курсовой {Telemetry.HeadingDeg:000}°, компас {Telemetry.CompassDeg:000}°";
                case "Knob_XPDR_Mode": return XpdrModes[Mathf.Clamp(s.Xpdr, 0, 4)];
                case "Rheo_Panel": return $"{s.RheoPanel * 100f:0}%";
                case "Rheo_Flood": return $"{s.RheoFlood * 100f:0}%";
                case "Lever_Prop": return s.PropLever > 0.75f ? "макс. обороты" : s.PropLever > 0.25f ? "средние" : "мин. обороты";
                case "Lever_Flap": return $"{tuning.flapDetents[Mathf.Clamp(s.FlapLever, 0, tuning.flapDetents.Length - 1)]:0}°";
                case "Lever_Power": return $"{Controls.Throttle * 100f:0}%";
                case "TrimWheel": return Mathf.Abs(Controls.Trim) < 0.02f ? "нейтраль" : Controls.Trim > 0f ? $"на кабрирование {Controls.Trim * 100f:0}%" : $"на пикирование {-Controls.Trim * 100f:0}%";
                case "Socket_Handle_PilotDoor_L_In":
                case "Socket_Handle_PilotDoor_L_Out": return DoorText(pilotDoorL);
                case "Socket_Handle_PilotDoor_R_In":
                case "Socket_Handle_PilotDoor_R_Out": return DoorText(pilotDoorR);
                case "Socket_Handle_CargoDoor_L_In":
                case "Socket_Handle_CargoDoor_L_Out": return DoorText(cargoDoorL);
                case "Socket_Handle_CargoDoor_R_In":
                case "Socket_Handle_CargoDoor_R_Out": return DoorText(cargoDoorR);
            }
            return null;
        }

        public override float HoldSeconds(string id) => _kitNet != null ? _kitNet.HoldSeconds(id) : 0f;

        public override string ActionText(string id)
        {
            if (_kitNet != null && PlaneWinterKitNet.IsKitId(id))
                return _kitNet.Kit != null && System.Enum.TryParse(id, out UtilityPlane.KitItem ki) && _kitNet.Kit.IsInstalled(ki) ? "снять (держать)" : "установить (держать)";
            if (SeatIndex(id) >= 0) return PaxIndex(id) >= 0 && !Switches.PaxDown(PaxIndex(id)) ? "сложено" : "сесть";
            if (id.StartsWith("PaxSeatFold_")) return Switches.PaxDown(PaxIndex(id.Replace("PaxSeatFold_", "PaxSeat_"))) ? "сложить" : "опустить";
            if (id.StartsWith("Stow_")) return "положить на место";
            PlaneSwitchState s = _switches.Value;
            if (Toggles.TryGetValue(id, out PlaneSwitch t))
            {
                bool on = s.On(t);
                if (id.StartsWith("Pull_")) return on ? "утопить" : "вытянуть";
                if (id.StartsWith("FuelSel_") || id.StartsWith("Socket_FuelCap_")) return on ? "закрыть" : "открыть";
                if (id.StartsWith("Socket_Belt_")) return on ? "расстегнуть" : "застегнуть";
                if (id.StartsWith("Visor_")) return on ? "поднять" : "опустить";
                if (id == "Lever_Condition") return on ? "малый газ" : "останов";
                return on ? "выключить" : "включить";
            }
            switch (id)
            {
                case "SW_Starter": return "держать (ЛКМ)";
                case "Btn_AnnunTest": return "держать — тест ламп";
                case "Rheo_Panel":
                case "Rheo_Flood": return "ярче   (колесо — туда/обратно)";
                case "Knob_XPDR_Mode": return "переключить   (колесо — туда/обратно)";
                case "Socket_Keyhole_PilotDoor_L":
                case "Socket_Keyhole_PilotDoor_R": return s.On(PlaneSwitch.Locked) ? "отпереть ключом" : "запереть ключом";
                case "Lever_Prop": return "переставить";
                case "Lever_Flap": return s.FlapLever >= tuning.flapDetents.Length - 1 || (_flapDirection < 0 && s.FlapLever > 0) ? "убрать" : "выпустить";
                case "Lever_Power": return "Shift / Ctrl";
                case "TrimWheel": return "в нейтраль   ([ ] — крутить)";
            }
            if (id.StartsWith("SW_CargoDoor_"))
                return s.On(id.EndsWith("_L") ? PlaneSwitch.CargoDoorL : PlaneSwitch.CargoDoorR) ? "закрыть" : "открыть";
            if (id.StartsWith("SW_CabinDoor_"))
                return s.On(id.EndsWith("_L") ? PlaneSwitch.CabinDoorL : PlaneSwitch.CabinDoorR) ? "закрыть" : "открыть";
            if (id.StartsWith("Knob_")) return "повернуть   (колесо — туда/обратно)";
            if (id.StartsWith("Socket_Handle_"))
            {
                NetworkDoor d = id.Contains("PilotDoor_L") ? pilotDoorL : id.Contains("PilotDoor_R") ? pilotDoorR
                    : id.Contains("CargoDoor_L") ? cargoDoorL : cargoDoorR;
                return d != null && (d.State == DoorState.Open || d.State == DoorState.Opening) ? "закрыть" : "открыть";
            }
            return "использовать";
        }

        static readonly string[] XpdrModes = { "OFF", "SBY", "ON", "ALT", "TST" };

        static string DoorText(NetworkDoor d)
        {
            if (d == null) return null;
            switch (d.State)
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
