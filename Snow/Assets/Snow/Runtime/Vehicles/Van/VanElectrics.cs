using UnityEngine;

namespace Snow.Vehicles.Van
{
    /// <summary>
    /// The van's battery, alternator and bus (VAN3_INTEGRATION_UNITY.md В4, SYS-01; numbers vehicle.json → electrics,
    /// TEMPORARY). Pure C#, server only. Lead-acid battery: open-circuit voltage by charge (11.6 V empty … 12.7 V full,
    /// falling steeply below the 20 % knee), internal resistance × cold factor (1.5 at −20 °C … 1.0 at +20 °C) and × the
    /// knee rise. Alternator from 900 rpm, regulator 14.2 V, 40 A. The starter (В5) is a 0.045 Ω load straight on the
    /// battery: its current and the bus under it come from the same two numbers.
    /// </summary>
    public class VanElectrics
    {
        readonly VanTuning _t;

        /// <summary>0..1 of batteryAh.</summary>
        public float Charge { get; set; }
        public float BatteryTempC { get; set; }
        public float BusVolts { get; private set; }
        /// <summary>A, + = out of the battery.</summary>
        public float BatteryAmps { get; private set; }
        public float AlternatorAmps { get; private set; }
        public float LoadAmps { get; private set; }

        public VanElectrics(VanTuning t, float charge, float tempC)
        {
            _t = t;
            Charge = Mathf.Clamp01(charge);
            BatteryTempC = tempC;
        }

        /// <summary>Open-circuit voltage now, V.</summary>
        public float Emf
        {
            get
            {
                // 11.6 V at the knee (20 %) … 12.7 V full; below the knee −9 V per unit of charge (vehicle.json electrics)
                float knee = _t.kneeCharge;
                return Charge >= knee
                    ? Mathf.Lerp(_t.batteryOcvEmpty, _t.batteryOcvFull, Mathf.InverseLerp(knee, 1f, Charge))
                    : Mathf.Max(0f, _t.batteryOcvEmpty - _t.ocvDropBelowKnee * (knee - Charge));
            }
        }

        /// <summary>Internal resistance now, Ω.</summary>
        public float Rint
        {
            get
            {
                float cold = Mathf.Lerp(_t.coldRintFactor, 1f, Mathf.InverseLerp(-20f, 20f, BatteryTempC));
                float knee = Charge < _t.kneeCharge ? 1f + _t.rintRiseBelowKnee * (_t.kneeCharge - Charge) : 1f;
                return _t.batteryRintOhm * cold * knee;
            }
        }

        /// <summary>Starter current if it were engaged now, A (В5).</summary>
        public float StarterAmps => Emf / (Rint + _t.starterOhm);

        /// <summary>
        /// One step. connected: mass on, clamps on, battery in. loadAmps: everything on the bus except the starter.
        /// starter: the starter is engaged. engineRpm: for the alternator.
        /// </summary>
        public void Step(bool connected, float loadAmps, bool starter, float engineRpm, float dt)
        {
            if (!connected)
            {
                BusVolts = 0f;
                BatteryAmps = AlternatorAmps = LoadAmps = 0f;
                return;
            }
            float e = Emf, r = Rint;
            LoadAmps = loadAmps;
            float alt = engineRpm >= _t.alternatorCutInRpm
                ? _t.alternatorAmps * Mathf.InverseLerp(_t.alternatorCutInRpm, _t.alternatorCutInRpm * 1.6f, engineRpm)
                : 0f;
            if (starter)
            {
                float i = StarterAmps + loadAmps;
                BusVolts = Mathf.Max(0f, e - i * r);
                AlternatorAmps = 0f;
                BatteryAmps = i;
            }
            else if (alt > 0f)
            {
                // regulated: the alternator holds the bus at the regulator voltage as far as its current allows; what is left
                // over charges the battery, what is missing the battery gives
                float charge = Mathf.Max(0f, (_t.regulatorVolts - e) / r);
                float want = loadAmps + charge;
                if (want <= alt)
                {
                    AlternatorAmps = want;
                    BatteryAmps = -charge;
                    BusVolts = _t.regulatorVolts;
                }
                else
                {
                    AlternatorAmps = alt;
                    BatteryAmps = loadAmps - alt;                       // + : the battery helps, − : it still charges a bit
                    BusVolts = e - BatteryAmps * r;
                }
            }
            else
            {
                AlternatorAmps = 0f;
                BatteryAmps = loadAmps;
                BusVolts = Mathf.Max(0f, e - loadAmps * r);
            }
            Charge = Mathf.Clamp01(Charge - BatteryAmps * dt / 3600f / _t.batteryAh);
        }
    }
}
