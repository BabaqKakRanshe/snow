using Snow.Doors;
using UazVan;
using UnityEngine;

namespace Snow.Vehicles.Van
{
    /// <summary>
    /// On every peer: the van's replicated state into VanRig (VAN3_INTEGRATION_UNITY.md §7, VIEW-01). It never computes
    /// physics or game rules. Door hinges have one writer: NetworkDoor poses its hinge; the rig gets the same value
    /// (ShownPosition × open angle) so that its own pass — and ResyncPose — write exactly that pose (audit R1).
    /// Wheels spin from the replicated wheel speed (each peer runs its own angle, §3.2); travel and steering are eased
    /// between telemetry samples so 15 Hz does not step.
    /// </summary>
    [RequireComponent(typeof(VanRig))]
    public class VanPresentation : MonoBehaviour
    {
        public VanVehicle vehicle;
        [Tooltip("s: easing of telemetry samples (suspension, steering)")] public float sampleLag = 0.06f;

        VanRig _rig;
        readonly float[] _spin = new float[4], _travel = new float[4];
        float _steer;

        void Awake() => _rig = GetComponent<VanRig>();

        void Update()
        {
            if (vehicle == null) return;
            foreach (NetworkDoor d in vehicle.doors)
                if (d != null) _rig.SetPart(d.doorId, d.ShownPosition * d.openAngle);

            VanTelemetry t = vehicle.Telemetry;
            float dt = Time.deltaTime, k = sampleLag > 0f ? 1f - Mathf.Exp(-dt / sampleLag) : 1f;
            _steer = Mathf.Lerp(_steer, t.SteerDeg, k);
            _rig.SetSteer(-_steer);                                  // the rig's + is left
            for (int i = 0; i < 4; i++)
            {
                _spin[i] = Mathf.Repeat(_spin[i] + t.Omega(i) * Mathf.Rad2Deg * dt, 360f);
                _travel[i] = Mathf.Lerp(_travel[i], t.Travel(i), k);
                _rig.SetWheel(i, _spin[i], _travel[i]);
            }
            _rig.engineRpm = t.Rpm;
            _rig.SetGauge(VanRig.Gauge.Speedo, Mathf.Abs(t.SpeedKmh));
            _rig.SetGauge(VanRig.Gauge.Tacho, t.Rpm);
            _rig.SetPedals(0f, t.Brake / 255f, t.Gas / 255f);
            _rig.SetGearLever(t.Gear < 0 ? "R" : t.Gear == 0 ? "N" : t.Gear.ToString());
            Controls(dt);
            MeasureView(dt);
        }

        // ---- the control table (VanControls): positions → rig parts, eased so lids and levers move instead of jumping
        [Tooltip("s for a lid, lever or knob to travel its whole range")] public float controlTravelTime = 0.35f;
        float[][] _shown;

        void Controls(float dt)
        {
            var table = VanControls.Table;
            if (_shown == null)
            {
                _shown = new float[table.Count][];
                for (int c = 0; c < table.Count; c++) _shown[c] = (float[])table[c].Values[vehicle.Pos(c)].Clone();
            }
            for (int c = 0; c < table.Count; c++)
            {
                VanControl ctl = table[c];
                int pos = vehicle.Pos(c);
                float[] target = ctl.Values[pos];
                for (int p = 0; p < ctl.Parts.Length; p++)
                {
                    float range = Mathf.Abs(ctl.Values[ctl.Count - 1][p] - ctl.Values[0][p]);
                    float step = range > 0f ? range / Mathf.Max(0.01f, controlTravelTime) * dt : float.MaxValue;
                    if (ctl.Parts[p].StartsWith("FuelCap_")) step = float.MaxValue;   // the cap is off or on, not spun on the screen
                    _shown[c][p] = Mathf.MoveTowards(_shown[c][p], target[p], step);
                    _rig.SetPart(ctl.Parts[p], _shown[c][p]);
                    if (ctl.Parts[p].StartsWith("FuelCap_")) _rig.SetVisible(ctl.Parts[p], pos != 2);   // unscrewed: in the hand
                }
                if (ctl.Zone == "Curtain_L1A")                                         // all six windows as one zone
                    foreach (string w in VanRig.CurtainNames) _rig.SetCurtain(w, pos);
            }
            _rig.radioPower = vehicle.Pos("Radio_Volume") > 0 && vehicle.Telemetry.BusVolts > 9f;
            Lights();
        }

        static readonly string[] FuseNames = { "Fuse_1", "Fuse_2", "Fuse_3", "Fuse_4", "Fuse_5", "Fuse_6", "Fuse_7", "Fuse_8" };
        static readonly string[] SpareNames = { "Spare_Fuse_1", "Spare_Fuse_2", "Spare_Fuse_3" };

        /// <summary>
        /// В4 lamps: each switched lamp on when its circuit is (mass × clamps × fuse × switch, from the replicated state);
        /// reverse lamps in R with the ignition; brake, turn signals and hazard handed to the rig, which blinks, boosts the
        /// tails and scales every lamp by the bus voltage itself (no doubling of what the rig does).
        /// </summary>
        void Lights()
        {
            VanTelemetry t = vehicle.Telemetry;
            bool ign = t.Has(VanFlags.Ignition);
            _rig.busVoltage = t.BusVolts;
            foreach (VanRig.Lamp lamp in _rig.lamps)
                if (!lamp.ownPower && !string.IsNullOrEmpty(lamp.toggle))
                    _rig.SetLamp(lamp.name, vehicle.CircuitOn(lamp.toggle, lamp.fuse) ? 1f : 0f);
            float reverse = ign && t.Gear < 0 ? 1f : 0f;
            _rig.SetLamp("Reverse_L", reverse);
            _rig.SetLamp("Reverse_R", reverse);
            _rig.brake = ign && t.Has(VanFlags.Brake);
            _rig.turnSignal = !ign ? 0 : t.Has(VanFlags.TurnLeft) ? -1 : t.Has(VanFlags.TurnRight) ? 1 : 0;
            _rig.hazard = t.Has(VanFlags.Hazard);
            _rig.panelLight = vehicle.CircuitOn("Toggle_Head", "Fuse_1") ? 1f : 0f;
            _rig.wiperSpeed = ign && vehicle.CircuitOn("Toggle_Wiper", "Fuse_8") ? vehicle.tuning.wiperRate : 0f;
            _rig.SetGauge(VanRig.Gauge.Volt, ign ? t.BusVolts : 0f);
            foreach (string f in FuseNames) _rig.SetVisible(f, vehicle.FuseState(f) != VanVehicle.FuseEmpty);
            for (int i = 0; i < SpareNames.Length; i++) _rig.SetVisible(SpareNames[i], vehicle.SparePresent(i));
        }

        // AT-V02 evidence on clients: how far the drawn van jumps from where its last motion said it would be
        Vector3 _lastPos, _lastVel;
        float _maxJump, _viewTimer, _lastDt, _dtAtMax, _prevDtAtMax;
        bool _hasLast;

        void MeasureView(float dt)
        {
            if (vehicle.IsServer || !vehicle.IsSpawned || dt <= 0f) return;
            Vector3 p = transform.position;
            if (_hasLast)
            {
                float jump = (p - (_lastPos + _lastVel * dt)).magnitude;
                if (jump > _maxJump) { _maxJump = jump; _dtAtMax = dt; _prevDtAtMax = _lastDt; }
                _lastVel = (p - _lastPos) / dt;
            }
            _lastPos = p;
            _lastDt = dt;
            _hasLast = true;
            _viewTimer += dt;
            if (_viewTimer < 1f) return;
            Snow.Net.BenchLog.Event("van_view", "p", p, "kmh", vehicle.Telemetry.SpeedKmh, "maxJump", _maxJump, "dtAtMax", _dtAtMax, "prevDtAtMax", _prevDtAtMax,
                "steeringWheelDeg", _rig.GetPart("SteeringWheel"), "wheelSpinFL", _spin[0], "travelFL", _travel[0],
                "tachoNeedle", _rig.GetPart("Needle_tacho"), "speedoNeedle", _rig.GetPart("Needle_speedo"), "gear", vehicle.Telemetry.Gear);
            _viewTimer = 0f;
            _maxJump = 0f;
        }
    }
}
