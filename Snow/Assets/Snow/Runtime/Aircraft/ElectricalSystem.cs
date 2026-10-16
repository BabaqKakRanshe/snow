using UnityEngine;

namespace Snow.Aircraft
{
    /// <summary>
    /// 24 V battery, starter-generator and bus (§9.5, SYS-01). Pure C#. The battery is an open-circuit voltage that falls
    /// with its charge, behind an internal resistance: a big load (the starter, 300 A) pulls the bus down, lights left on
    /// drain it, and the generator (online above generatorNg with the battery switch on) holds the bus and recharges it.
    /// Load currents are the nominal values of §9.5; they drop out below brownoutVolts.
    /// </summary>
    public class ElectricalSystem
    {
        readonly AircraftTuning _t;

        /// <summary>State of charge 0..1.</summary>
        public float Charge { get; private set; }
        public float BusVolts { get; private set; }
        /// <summary>A, + = charging the battery, - = discharging (the ammeter).</summary>
        public float BatteryAmps { get; private set; }
        public float LoadAmps { get; private set; }
        public bool GeneratorOnline { get; private set; }
        public bool BusPowered => BusVolts >= _t.brownoutVolts;
        /// <summary>0..1: lamps and lights dim with the bus below 24 V and go out at brownout.</summary>
        public float Brightness => Mathf.Clamp01((BusVolts - _t.brownoutVolts) / (24f - _t.brownoutVolts));
        /// <summary>0..1 starter speed from the bus voltage.</summary>
        public float StarterFactor => Mathf.Clamp01((BusVolts - _t.brownoutVolts) / 6f);

        public ElectricalSystem(AircraftTuning tuning, float charge)
        {
            _t = tuning;
            Charge = Mathf.Clamp01(charge);
        }

        /// <summary>V: linear over the useful charge, collapsing over the last 10 % (a flat battery puts the lights out).</summary>
        public float OpenCircuitVolts => Charge > 0.1f
            ? Mathf.Lerp(_t.batteryVoltsEmpty, _t.batteryVoltsFull, (Charge - 0.1f) / 0.9f)
            : Mathf.Lerp(10f, _t.batteryVoltsEmpty, Charge / 0.1f);

        /// <summary>batterySwitch: SW_Battery; generatorAvailable: the engine turns fast enough; loads: requested amps.</summary>
        public void Step(bool batterySwitch, bool generatorAvailable, float loads, float dt)
        {
            if (!batterySwitch)
            {
                BusVolts = 0f;
                BatteryAmps = 0f;
                LoadAmps = 0f;
                GeneratorOnline = false;
                return;
            }
            GeneratorOnline = generatorAvailable;
            float e = OpenCircuitVolts;
            float r = _t.batteryResistance;
            if (GeneratorOnline)
            {
                float v = _t.generatorVolts;
                float charge = Mathf.Clamp((v - e) / (r * 4f), 0f, 60f);            // charging current tapers as it fills
                float total = loads + charge;
                if (total > _t.generatorAmps)
                {
                    // overload: the battery helps
                    float deficit = total - _t.generatorAmps;
                    charge -= deficit;
                    v = charge < 0f ? e + charge * r : v;
                }
                BusVolts = v;
                BatteryAmps = charge;
                LoadAmps = loads;
            }
            else
            {
                float v = Mathf.Max(0f, e - loads * r);
                BusVolts = v;
                LoadAmps = v >= _t.brownoutVolts ? loads : 0f;
                BatteryAmps = -LoadAmps;
            }
            Charge = Mathf.Clamp01(Charge + BatteryAmps * dt / 3600f / _t.batteryAh);
        }
    }

    /// <summary>
    /// Two wing tanks with selectors, the shutoff pull and the boost pump (§9.6). Pure C#. The engine gets fuel if at
    /// least one open tank has fuel, the shutoff is in and there is pressure: the pump, or gravity from the high wing at
    /// a limited flow. Open tanks feed equally, so one closed selector makes an imbalance that moves the centre of mass.
    /// </summary>
    public class FuelSystem
    {
        readonly AircraftTuning _t;

        public float LeftL { get; private set; }
        public float RightL { get; private set; }
        public bool Supply { get; private set; }
        public float FeedLimitPph { get; private set; }

        public FuelSystem(AircraftTuning tuning, float left, float right)
        {
            _t = tuning;
            LeftL = Mathf.Clamp(left, 0f, tuning.tankCapacityL);
            RightL = Mathf.Clamp(right, 0f, tuning.tankCapacityL);
        }

        public float LeftKg => LeftL * _t.fuelDensity;
        public float RightKg => RightL * _t.fuelDensity;

        public void Evaluate(bool selectorL, bool selectorR, bool shutoffPulled, bool pumpRunning)
        {
            bool tank = (selectorL && LeftL > 0f) || (selectorR && RightL > 0f);
            Supply = tank && !shutoffPulled;
            FeedLimitPph = !Supply ? 0f : pumpRunning ? 10000f : _t.gravityFeedPph;
        }

        /// <summary>Take burned litres from the open tanks, equally; what one empty tank cannot give the other gives.</summary>
        public void Burn(float litres, bool selectorL, bool selectorR)
        {
            if (litres <= 0f) return;
            bool l = selectorL && LeftL > 0f, r = selectorR && RightL > 0f;
            if (l && r)
            {
                float half = litres * 0.5f;
                float fromL = Mathf.Min(half, LeftL), fromR = Mathf.Min(half, RightL);
                LeftL -= fromL; RightL -= fromR;
                float rest = litres - fromL - fromR;
                if (rest > 0f) { float x = Mathf.Min(rest, LeftL); LeftL -= x; RightL = Mathf.Max(0f, RightL - (rest - x)); }
            }
            else if (l) LeftL = Mathf.Max(0f, LeftL - litres);
            else if (r) RightL = Mathf.Max(0f, RightL - litres);
        }

        /// <summary>Add fuel to a tank up to its capacity; returns the litres actually taken (CONTAINER-01, P06).</summary>
        public float Add(bool left, float litres)
        {
            float room = _t.tankCapacityL - (left ? LeftL : RightL);
            float x = Mathf.Clamp(litres, 0f, room);
            if (left) LeftL += x; else RightL += x;
            return x;
        }
    }
}
