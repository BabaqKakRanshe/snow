using UnityEngine;

namespace Snow.Aircraft
{
    /// <summary>
    /// Free-turbine turboprop and constant-speed propeller (§9.3, §9.4, SYS-01). Pure C#, SI inside, gauge units out.
    ///
    /// Start: the starter turns the gas generator (Ng) towards starterNg at a rate that depends on bus voltage. With
    /// ignition, fuel to the engine and Ng ≥ lightOffNg it lights: ITT jumps, combustion helps the starter. Below idle
    /// the engine cannot sustain itself (a released starter lets it hang and run down); at idle Ng it runs on its own.
    /// A weak battery cranks slowly, so the start runs hot. No fuel (cut-off, closed valves, empty tanks, pulled
    /// shutoff after the line empties) flames it out. Over the ITT limit the power is reduced, reversibly.
    ///
    /// Propeller: governed rpm from Lever_Prop; below the power that holds it the blades sit on the fine-pitch stop and
    /// rpm follows the power. Thrust = min(static thrust from momentum theory, efficiency × power / airspeed); a
    /// stopped or windmilling propeller drags.
    /// </summary>
    public class TurbopropEngine
    {
        public struct Inputs
        {
            /// <summary>Power lever 0..1.</summary>
            public float Power;
            /// <summary>Lever_Condition in cut-off: no fuel to the engine.</summary>
            public bool CutOff;
            /// <summary>Lever_Prop 0..1: min .. max governed rpm.</summary>
            public float PropLever;
            /// <summary>Starter energised, and its speed factor 0..1 from bus voltage.</summary>
            public bool Starter;
            public float StarterVoltage;
            public bool Ignition;
            /// <summary>Tanks and valves deliver fuel; max flow they can feed, pph.</summary>
            public bool Supply;
            public float FeedLimitPph;
            /// <summary>Forward airspeed, m/s; air density kg/m³; outside air temperature °C.</summary>
            public float Airspeed, Rho, OatC;
            /// <summary>И4 K4.3: the winter-kit heater on (warms oil and engine), the insulated engine cover on.</summary>
            public bool Heater, Insulated;
        }

        readonly AircraftTuning _t;
        float _lineL;
        float _derate = 1f;

        public float Ng { get; private set; }
        public float IttC { get; private set; }
        public float OilPsi { get; private set; }
        public float OilC { get; private set; }
        public float PropRpm { get; private set; }
        public float ShaftPowerW { get; private set; }
        /// <summary>Propeller shaft torque, N·m (its reaction rolls the airframe).</summary>
        public float TorqueNm { get; private set; }
        public float ThrustN { get; private set; }
        public float FuelFlowPph { get; private set; }
        public bool Combusting { get; private set; }
        public bool StarterTurning { get; private set; }
        /// <summary>Litres burned in the last step (the fuel system takes them from the tanks).</summary>
        public float BurnedL { get; private set; }
        public float PropOmega => PropRpm * Mathf.PI / 30f;
        public float TorqueFtLb => TorqueNm * 0.7376f;
        public bool Hot => IttC > (Ng < _t.idleNg - 1f ? _t.ittStartLimit : _t.ittLimit);

        public TurbopropEngine(AircraftTuning tuning, float oatC)
        {
            _t = tuning;
            IttC = oatC;
            OilC = oatC;
            _lineL = tuning.lineFuelL;
        }

        public void Step(in Inputs u, float dt)
        {
            if (dt <= 0f) return;
            StarterTurning = u.Starter && u.StarterVoltage > 0f;
            if (u.Supply) _lineL = _t.lineFuelL;
            bool fuelToEngine = !u.CutOff && (u.Supply || _lineL > 0f);

            // light-off needs fuel pressure from the tanks; the fuel left in the line only keeps a running engine going
            if (!Combusting && u.Ignition && !u.CutOff && u.Supply && Ng >= _t.lightOffNg) Combusting = true;
            else if (Combusting && (!fuelToEngine || Ng < 8f)) Combusting = false;

            // gas generator
            float idle = _t.idleNg;
            if (Combusting && Ng >= idle - 0.5f)
            {
                float lever = Mathf.Clamp01(u.Power);
                // fuel feed limit (gravity feed with the pump off): the fuel control cannot schedule more than arrives
                float feedLever = (u.FeedLimitPph - _t.idleFuelPph) / Mathf.Max(1f, _t.maxFuelPph - _t.idleFuelPph);
                lever = Mathf.Min(lever, Mathf.Max(0f, feedLever));
                float target = idle + (_t.maxNg - idle) * lever;
                Ng += (target - Ng) * (1f - Mathf.Exp(-dt / _t.spoolTime));
            }
            else if (Combusting)
            {
                float x = Mathf.Clamp01((Ng - _t.lightOffNg) / (idle - _t.lightOffNg));
                float d = (StarterTurning ? _t.starterRate * u.StarterVoltage : -_t.subIdleDecay) + _t.startAcceleration * x * x;
                Ng = Mathf.Max(0f, Ng + d * dt);
            }
            else if (StarterTurning && Ng < _t.starterNg * u.StarterVoltage)
                Ng = Mathf.Min(_t.starterNg * u.StarterVoltage, Ng + _t.starterRate * u.StarterVoltage * dt);
            else
                Ng -= Ng * (1f - Mathf.Exp(-dt / _t.rundownTime));

            // power turbine
            float sigma = Mathf.Max(0.2f, u.Rho / 1.225f);
            float xp = Mathf.Clamp01((Ng - idle) / (_t.maxNg - idle));
            ShaftPowerW = Combusting && Ng >= idle - 2f
                ? _t.maxPowerW * Mathf.Pow(sigma, 0.7f) * (0.05f + 0.95f * Mathf.Pow(xp, 1.5f)) * _derate
                : 0f;

            // propeller rpm: governed, or on the fine-pitch stop, or windmilling
            float governed = Mathf.Lerp(_t.propMinRpm, _t.propMaxRpm, Mathf.Clamp01(u.PropLever));
            float fine = _t.propMaxRpm * Mathf.Pow(ShaftPowerW / _t.propFinePowerW, 1f / 3f);
            float windmill = Mathf.Max(0f, u.Airspeed) * 15f;
            float rpmTarget = Mathf.Min(governed, Mathf.Max(fine, windmill));
            PropRpm += (rpmTarget - PropRpm) * (1f - Mathf.Exp(-dt / (ShaftPowerW > 0f ? 0.6f : 3f)));
            TorqueNm = PropOmega > 10f ? ShaftPowerW / PropOmega : 0f;

            // thrust
            float v = Mathf.Max(0f, u.Airspeed);
            if (ShaftPowerW > 1f)
            {
                float stat = _t.staticThrustN * Mathf.Pow(ShaftPowerW / _t.maxPowerW, 2f / 3f) * Mathf.Pow(sigma, 1f / 3f);
                ThrustN = Mathf.Min(stat, _t.propEfficiency * ShaftPowerW / Mathf.Max(1f, v));
            }
            else ThrustN = -0.5f * u.Rho * v * v * _t.propDragArea;

            // ITT: light-off spike, falls towards idle as Ng rises, rises with power and thin or warm air
            float ittTarget;
            float tau;
            if (Combusting)
            {
                if (Ng < idle - 0.5f)
                {
                    float x = Mathf.Clamp01((Ng - _t.lightOffNg) / (idle - _t.lightOffNg));
                    ittTarget = 470f + 400f * (1f - x) + 180f * (1f - u.StarterVoltage) * (StarterTurning ? 1f : 1.5f);
                }
                else ittTarget = 450f + 330f * (ShaftPowerW / _t.maxPowerW) / Mathf.Sqrt(sigma) + 0.8f * (u.OatC - 15f);
                tau = 1.2f;
            }
            else { ittTarget = u.OatC; tau = 25f; }
            IttC += (ittTarget - IttC) * (1f - Mathf.Exp(-dt / tau));
            float limit = Ng < idle - 1f ? _t.ittStartLimit : _t.ittLimit;
            _derate = 1f - _t.overheatDerate * Mathf.Clamp01((IttC - limit) / 100f);

            // oil
            float oilPTarget = 110f * Mathf.Pow(Mathf.Clamp01(Ng / 60f), 1.2f);
            OilPsi += (oilPTarget - OilPsi) * (1f - Mathf.Exp(-dt / 1f));
            float oilTTarget = u.OatC + (Combusting ? 45f + 45f * ShaftPowerW / _t.maxPowerW : 0f);
            if (!Combusting && u.Heater && OilC < u.OatC + _t.preheatAboveOatC)
                OilC = Mathf.Min(u.OatC + _t.preheatAboveOatC, OilC + _t.preheatCPerMin / 60f * (u.Insulated ? 2f : 1f) * dt);   // TEMPORARY
            else if (!(u.Heater && oilTTarget < OilC))
                OilC += (oilTTarget - OilC) * (1f - Mathf.Exp(-dt / (u.Insulated && oilTTarget < OilC ? 180f : 90f)));   // insulated: cools half as fast

            // fuel flow and burn
            if (!Combusting) FuelFlowPph = 0f;
            else if (Ng < idle - 0.5f) FuelFlowPph = _t.startFuelPph * Mathf.Clamp01(Ng / idle);
            else FuelFlowPph = _t.idleFuelPph + (_t.maxFuelPph - _t.idleFuelPph) * Mathf.Clamp01(ShaftPowerW / _t.maxPowerW);
            float kg = FuelFlowPph * 0.45359f / 3600f * dt;
            BurnedL = kg / _t.fuelDensity;
            if (!u.Supply) _lineL = Mathf.Max(0f, _lineL - BurnedL);
        }

        /// <summary>Restore a running or stopped engine (checkpoint load, bench start).</summary>
        public void SetRunning(bool running, float oatC)
        {
            Combusting = running;
            Ng = running ? _t.idleNg : 0f;
            IttC = running ? 450f : oatC;
            OilPsi = running ? 100f : 0f;
            OilC = running ? oatC + 45f : oatC;
            PropRpm = running ? _t.propMaxRpm * Mathf.Pow(0.05f * _t.maxPowerW / _t.propFinePowerW, 1f / 3f) : 0f;
        }
    }
}
