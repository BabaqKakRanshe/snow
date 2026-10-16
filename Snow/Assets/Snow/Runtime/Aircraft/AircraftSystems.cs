using UnityEngine;

namespace Snow.Aircraft
{
    /// <summary>
    /// The aircraft's systems as one causal chain (SYS-01, §6 AircraftSystems): switch position → power → what actually
    /// runs → what the gauges and lamps show. Pure C#, server only; the vehicle replicates the result.
    /// Order in a step: fuel valves → engine (uses last step's bus voltage for the starter) → electrics with every load
    /// that actually runs → lamps and lights from the new bus voltage.
    /// </summary>
    public class AircraftSystems
    {
        public struct Inputs
        {
            public PlaneSwitchState Switches;
            /// <summary>Power lever 0..1.</summary>
            public float Throttle;
            public float Airspeed, Rho, OatC;
            /// <summary>Electric actuators running this step (doors, flaps): they draw current.</summary>
            public int DoorActuatorsRunning;
            public bool FlapsMoving;
            public bool AutopilotOn;
            // lamp conditions the vehicle knows
            public bool CabinDoorOpen, CargoDoorOpen, CargoLoose, BeltWarning;
        }

        readonly AircraftTuning _t;
        public readonly TurbopropEngine Engine;
        public readonly ElectricalSystem Electrics;
        public readonly FuelSystem Fuel;

        public ushort Lamps { get; private set; }
        public float Nav { get; private set; }
        public float Strobe { get; private set; }
        public float Beacon { get; private set; }
        public float Landing { get; private set; }
        public float Cabin { get; private set; }
        public float Cargo { get; private set; }
        public float Panel { get; private set; }
        public float Flood { get; private set; }
        /// <summary>Avionics bus output 0..1: SW_Avionics with bus power, dimming with the voltage like the lights.</summary>
        public float Avionics { get; private set; }
        public bool PumpRunning { get; private set; }

        public AircraftSystems(AircraftTuning tuning, float oatC)
        {
            _t = tuning;
            Engine = new TurbopropEngine(tuning, oatC);
            Electrics = new ElectricalSystem(tuning, tuning.batteryStartCharge);
            Fuel = new FuelSystem(tuning, tuning.startFuelL, tuning.startFuelL);
        }

        public void Step(in Inputs u, float dt)
        {
            PlaneSwitchState s = u.Switches;
            bool bus = Electrics.BusPowered;
            PumpRunning = bus && s.On(PlaneSwitch.FuelPump);

            Fuel.Evaluate(s.On(PlaneSwitch.FuelSelL), s.On(PlaneSwitch.FuelSelR), s.On(PlaneSwitch.FuelShutoff), PumpRunning);
            bool starter = bus && s.On(PlaneSwitch.Starter);
            Engine.Step(new TurbopropEngine.Inputs
            {
                Power = u.Throttle,
                CutOff = s.On(PlaneSwitch.ConditionCutOff),
                PropLever = s.PropLever,
                Starter = starter,
                StarterVoltage = starter ? Electrics.StarterFactor : 0f,
                Ignition = bus && s.On(PlaneSwitch.Ignition),
                Supply = Fuel.Supply,
                FeedLimitPph = Fuel.FeedLimitPph,
                Airspeed = u.Airspeed,
                Rho = u.Rho,
                OatC = u.OatC,
            }, dt);
            Fuel.Burn(Engine.BurnedL, s.On(PlaneSwitch.FuelSelL), s.On(PlaneSwitch.FuelSelR));

            // loads that actually run (switch on; the bus decides whether they get current)
            float loads = 0f;
            if (starter) loads += _t.starterAmps;
            if (s.On(PlaneSwitch.Landing)) loads += _t.landingAmps;
            if (s.On(PlaneSwitch.Strobe)) loads += _t.strobeAmps;
            if (s.On(PlaneSwitch.NavLights)) loads += _t.navAmps;
            if (s.On(PlaneSwitch.Beacon)) loads += _t.beaconAmps;
            if (s.On(PlaneSwitch.Avionics)) loads += _t.avionicsAmps;
            if (s.On(PlaneSwitch.FuelPump)) loads += _t.pumpAmps;
            if (s.On(PlaneSwitch.CabinLight)) loads += 2f * _t.domeAmps;
            if (s.On(PlaneSwitch.CargoLight)) loads += _t.cargoLightAmps;
            if (s.On(PlaneSwitch.CabinHeat)) loads += _t.blowerAmps;
            if (s.On(PlaneSwitch.Defrost)) loads += _t.blowerAmps;
            loads += _t.panelLightAmps * s.RheoPanel + _t.floodAmps * s.RheoFlood;
            loads += _t.doorAmps * u.DoorActuatorsRunning;
            if (u.FlapsMoving) loads += _t.flapAmps;
            if (u.AutopilotOn) loads += _t.autopilotAmps;
            loads += _t.gyroAmps;
            bool genAvailable = Engine.Combusting && Engine.Ng >= _t.generatorNg;
            Electrics.Step(s.On(PlaneSwitch.Battery), genAvailable, loads, dt);

            // lamps (§10.1): conditions only; the Rig lights them with bus power, or all of them on test
            int m = 0;
            if (Engine.OilPsi < 40f && Engine.Ng > 20f) m |= 1 << 0;
            if (Engine.Hot) m |= 1 << 1;
            if (Fuel.LeftL < _t.fuelLowL) m |= 1 << 2;
            if (Fuel.RightL < _t.fuelLowL) m |= 1 << 3;
            if (!Electrics.GeneratorOnline) m |= 1 << 4;
            if (Electrics.BusVolts < _t.battLowVolts) m |= 1 << 5;
            if (starter && Electrics.BusPowered) m |= 1 << 6;
            if (s.On(PlaneSwitch.Ignition)) m |= 1 << 7;
            if (u.CabinDoorOpen) m |= 1 << 8;
            if (u.CargoDoorOpen) m |= 1 << 9;
            if (u.CargoLoose) m |= 1 << 10;
            if (u.BeltWarning) m |= 1 << 11;
            Lamps = (ushort)m;

            // lights: actual output with the bus voltage (they dim, then go out)
            float b = Electrics.Brightness;
            Nav = s.On(PlaneSwitch.NavLights) ? b : 0f;
            Strobe = s.On(PlaneSwitch.Strobe) ? b : 0f;
            Beacon = s.On(PlaneSwitch.Beacon) ? b : 0f;
            Landing = s.On(PlaneSwitch.Landing) ? b : 0f;
            Cabin = s.On(PlaneSwitch.CabinLight) ? b : 0f;
            Cargo = s.On(PlaneSwitch.CargoLight) ? b : 0f;
            Panel = s.RheoPanel * b;
            Flood = s.RheoFlood * b;
            Avionics = s.On(PlaneSwitch.Avionics) ? b : 0f;
        }
    }

    /// <summary>
    /// Instrument sensor models (§10.3). Pure C#. Pneumatic instruments (airspeed, altimeter, VSI) work without power;
    /// electric ones (attitude, heading, turn, engine and fuel gauges) do not. The attitude gyro spins up in
    /// gyroSpinUp and, unpowered, slowly runs down and topples. И2 (model v7 knobs):
    ///  - altimeter: pressure altitude against the setting in the Kollsman window (setting × 3386.39 Pa, §10.3);
    ///  - heading gyro: drifts headingDrift °/h and is NOT aligned automatically any more (the M3 workaround is gone): the
    ///    pilot sets it with Knob_DG_Set (Shift). Unpowered, the card stays where it was while the aircraft turns;
    ///  - magnetic compass: true heading through a first-order lag (TEMPORARY, no variation, deviation, turning errors);
    ///  - suction: the engine-driven pump gives suctionInHg above idle, falls off when the engine stops (TEMPORARY).
    /// </summary>
    public class FlightInstruments
    {
        readonly AircraftTuning _t;
        float _spin;
        float _vsi, _lastAlt;
        bool _hasAlt, _hasCompass;
        float _pitch, _bank, _heading, _headingDrift, _turn, _compass, _suction;

        public float IasKt { get; private set; }
        public float AltFt { get; private set; }
        public float VsiFpm => _vsi;
        public float PitchDeg => _pitch;
        public float BankDeg => _bank;
        public float HeadingDeg => _heading;
        public float TurnRate => _turn;
        public float Slip { get; private set; }
        public float CompassDeg => _compass;
        public float SuctionInHg => _suction;

        public FlightInstruments(AircraftTuning tuning) { _t = tuning; }

        /// <summary>
        /// pitotSpeed: air-relative speed along the aircraft's nose, m/s. staticPa: static pressure. truePitch, trueBank,
        /// trueHeading: degrees. yawRate: deg/s about world up. lateralG: specific force along the right wing, in g.
        /// settingInHg: altimeter setting. vacuum: the engine drives the vacuum pump (running above idle).
        /// </summary>
        public void Step(float pitotSpeed, float staticPa, float truePitch, float trueBank, float trueHeading, float yawRate,
                         float lateralG, bool bus, float settingInHg, bool vacuum, float dt)
        {
            if (dt <= 0f) return;
            // airspeed: dynamic pressure against sea-level density (IAS)
            float q = 0.5f * 1.225f * Mathf.Max(0f, pitotSpeed) * Mathf.Max(0f, pitotSpeed);
            IasKt = Mathf.Sqrt(2f * q / 1.225f) * 1.94384f;
            // altimeter: pressure altitude against the reference pressure set in the window
            float reference = Mathf.Clamp(settingInHg, 28.6f, 31.0f) * 3386.39f;
            float altM = 44330.8f * (1f - Mathf.Pow(Mathf.Max(1f, staticPa) / reference, 0.190263f));
            AltFt = altM * 3.28084f;
            if (!_hasAlt) { _lastAlt = AltFt; _hasAlt = true; }
            float rate = (AltFt - _lastAlt) / dt * 60f;
            _lastAlt = AltFt;
            _vsi += (rate - _vsi) * (1f - Mathf.Exp(-dt / _t.vsiLag));

            // attitude gyro
            _spin = Mathf.Clamp01(_spin + (bus ? dt / _t.gyroSpinUp : -dt / _t.gyroSpinDown));
            if (_spin >= 0.8f)
            {
                float erect = 30f * dt;                                 // erects quickly once up to speed
                _pitch = Mathf.MoveTowards(_pitch, truePitch, Mathf.Max(erect, Mathf.Abs(truePitch - _pitch) * 0.5f));
                _bank = Mathf.MoveTowardsAngle(_bank, trueBank, Mathf.Max(erect, Mathf.Abs(Mathf.DeltaAngle(_bank, trueBank)) * 0.5f));
            }
            else
            {
                float topple = (0.8f - _spin) * 6f * dt;                // slowly falls over
                _pitch = Mathf.MoveTowards(_pitch, -8f, topple);
                _bank = Mathf.MoveTowardsAngle(_bank, 35f, topple);
            }

            // heading gyro: drifts while powered; unpowered the card stands still, so the error grows as the aircraft turns
            if (bus)
            {
                _headingDrift += _t.headingDrift / 3600f * dt;
                _heading = Mathf.Repeat(trueHeading + _headingDrift, 360f);
                _turn = yawRate;
            }
            else
            {
                _headingDrift = Mathf.DeltaAngle(trueHeading, _heading);
                _turn = Mathf.MoveTowards(_turn, 0f, 2f * dt);
            }
            // magnetic compass: lagging card, works without power
            if (!_hasCompass) { _compass = trueHeading; _hasCompass = true; }
            _compass = Mathf.Repeat(_compass + Mathf.DeltaAngle(_compass, trueHeading) * (1f - Mathf.Exp(-dt / Mathf.Max(0.05f, _t.compassLag))), 360f);
            // suction from the engine-driven pump
            float target = vacuum ? _t.suctionInHg : 0f;
            _suction += (target - _suction) * (1f - Mathf.Exp(-dt / Mathf.Max(0.05f, target > _suction ? _t.suctionRise : _t.suctionFall)));
            // the ball is mechanical: it moves against the side force (full deflection ~0.15 g)
            Slip = Mathf.Clamp(-lateralG / 0.15f, -1f, 1f);
        }

        /// <summary>Knob_DG_Set: turn the heading card by this many degrees (the pilot sets it to the compass).</summary>
        public void ShiftHeading(float deg)
        {
            _headingDrift += deg;
            _heading = Mathf.Repeat(_heading + deg, 360f);
        }

        /// <summary>Bench start with the gyros already spun up.</summary>
        public void SpinUp() => _spin = 1f;
    }

    /// <summary>
    /// Glass fog (И2 G.1, TEMPORARY, docs/dev/Tuning/UtilityPlane.md). Pure C#. Below fogBelowOatC the people's breath fogs every
    /// pane by fogPerPerson per second each; with the engine running the defroster clears the windshield and the cabin
    /// heat clears all panes and stops the fogging. Without the engine neither heats. 0 = clear, 1 = fully fogged.
    /// </summary>
    public class CabinClimate
    {
        readonly AircraftTuning _t;
        public float Windshield { get; private set; }
        public float Side { get; private set; }

        public CabinClimate(AircraftTuning tuning) { _t = tuning; }

        public void Step(float oatC, int people, bool engineRunning, float defrost, float cabinHeat, float dt)
        {
            bool heat = engineRunning && cabinHeat > 0f;
            float grow = oatC < _t.fogBelowOatC && !heat ? _t.fogPerPerson * people * dt : 0f;
            float clearAll = heat ? _t.cabinHeatRate * cabinHeat * dt : 0f;
            float clearWind = engineRunning ? _t.defrostRate * defrost * dt : 0f;
            Windshield = Mathf.Clamp01(Windshield + grow - clearAll - clearWind);
            Side = Mathf.Clamp01(Side + grow - clearAll);
        }

        /// <summary>Checkpoint / bench state.</summary>
        public void Set(float windshield, float side)
        {
            Windshield = Mathf.Clamp01(windshield);
            Side = Mathf.Clamp01(side);
        }
    }
}
