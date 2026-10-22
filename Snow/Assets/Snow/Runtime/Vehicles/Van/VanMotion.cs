using UnityEngine;

namespace Snow.Vehicles.Van
{
    /// <summary>
    /// Motion model of the van (VAN3_INTEGRATION_UNITY.md §4, PHYS-01/02, INPUT-01). Pure C#, server only, no network and
    /// no rig: engine → automatic clutch → 4-speed box with automatic logic → transfer H / L → rear axle (+ front axle by
    /// lever, no centre differential: torque split by axle load) → open differentials (equal torque per side) → four ray
    /// wheels (<see cref="VanWheel"/>). Brakes per wheel, hand brake on the transmission. Wheelspin, skids and locked wheels
    /// come out of the tyres; there is no traction control, no ABS and no hidden help.
    /// Gear: −1 = R, 0 = N, 1..4. Steering angle: + = right (yaw); the rig's SetSteer is + = left.
    /// </summary>
    public class VanMotion
    {
        public struct Inputs
        {
            /// <summary>Driver's W/S axis −1..1 (+ = W).</summary>
            public float Pedal;
            /// <summary>A/D −1..1 (+ = D, right).</summary>
            public float Steer;
            /// <summary>Hand brake 0..1 (Space).</summary>
            public float Handbrake;
            /// <summary>Hand throttle knob 0..1 (Knob_Throttle).</summary>
            public float HandThrottle;
            /// <summary>The engine fires (fuel, spark, mixture: VanSystems). False = it only turns if something turns it.</summary>
            public bool Combustion;
            /// <summary>Ignition at II: the push-start clutch window is armed while the engine stands.</summary>
            public bool IgnitionOn;
            /// <summary>rpm the starter or the crank handle drives the engine towards (0 = none).</summary>
            public float CrankRpm;
            /// <summary>N·m the cranking device can give at 0 rpm.</summary>
            public float CrankTorque;
            /// <summary>0..1: misfire / overheat derating of the combustion torque.</summary>
            public float TorqueScale;
            /// <summary>m/s along the van's forward, signed.</summary>
            public float ForwardSpeed;
        }

        public readonly VanTuning T;
        public readonly VanWheel[] Wheels = new VanWheel[4];
        readonly float[] _steer = new float[4];

        public float EngineOmega { get; set; }
        public float EngineRpm => EngineOmega * 60f / (2f * Mathf.PI);
        public int Gear { get; private set; } = 1;
        public bool FrontAxle { get; set; }
        public bool LowRange { get; private set; }
        public float SteerDeg { get; private set; }
        public float Gas { get; private set; }
        public float Brake { get; private set; }
        public float ClutchTorque { get; private set; }
        public float InputRpm { get; private set; }
        /// <summary>rpm the gear logic sees: road speed through the current ratio (no wheel slip).</summary>
        public float ShiftRpm { get; private set; }
        public bool Shifting => _shift > 0f;
        public int Shifts { get; private set; }
        public float CombustionTorque { get; private set; }

        float _shift, _lockout, _dirHold, _lastPedal;
        readonly float _peakTorque;

        public VanMotion(VanTuning t, float massKg)
        {
            T = t;
            float g = 9.81f;
            float rearZ = t.wheelCentres[2].z, frontZ = t.wheelCentres[0].z;
            float front = Mathf.Clamp01((t.centerOfMass.z - rearZ) / (frontZ - rearZ));
            for (int i = 0; i < 4; i++)
            {
                float share = (i < 2 ? front : 1f - front) * 0.5f;
                Wheels[i] = new VanWheel(t.wheelRadius, massKg * g * share, t);
            }
            foreach (float n in t.torqueNm) _peakTorque = Mathf.Max(_peakTorque, n);
        }

        /// <summary>Steered angle of wheel i, degrees, + = right (Ackermann on the front pair).</summary>
        public float WheelSteer(int i) => _steer[i];

        /// <summary>Range lever H / L: refused above rangeShiftMaxKmh (no synchroniser).</summary>
        public bool TrySetRange(bool low, float speedKmh, out string reason)
        {
            reason = null;
            if (low == LowRange) return true;
            if (Mathf.Abs(speedKmh) > T.rangeShiftMaxKmh)
            {
                reason = "Раздатка не переключается на ходу — остановитесь";
                return false;
            }
            LowRange = low;
            return true;
        }

        public float Torque(float rpm)
        {
            float[] x = T.torqueRpm, y = T.torqueNm;
            if (rpm <= x[0]) return y[0];
            for (int i = 1; i < x.Length; i++)
                if (rpm <= x[i]) return Mathf.Lerp(y[i - 1], y[i], (rpm - x[i - 1]) / (x[i] - x[i - 1]));
            return y[y.Length - 1];
        }

        /// <summary>Total ratio engine : wheel for a gear (negative in reverse).</summary>
        public float Ratio(int gear)
        {
            if (gear == 0) return 0f;
            float box = gear < 0 ? -T.reverseRatio : T.gearRatios[Mathf.Clamp(gear, 1, 4) - 1];
            return box * (LowRange ? T.transferLow : T.transferHigh) * T.finalDrive;
        }

        /// <summary>
        /// One physics step. ground[i] must hold the ray result, contact velocity and the van's up for wheel i; this fills in
        /// heading, torques and grip and writes the world force on the body into forces[i].
        /// </summary>
        public void Step(in Inputs u, VanWheelInput[] ground, Vector3 forward, float dt, Vector3[] forces)
        {
            float kmh = u.ForwardSpeed * 3.6f;
            Pedals(u.Pedal, kmh, dt);
            SteerStep(u.Steer, kmh, dt);

            // ---- drivetrain: the clutch couples the engine to the wheels it drives (implicit, see VanWheel)
            float ratio = Ratio(Gear);
            bool rear = true, front = FrontAxle;
            float wRear = 0.5f * (Wheels[2].Omega + Wheels[3].Omega), wFront = 0.5f * (Wheels[0].Omega + Wheels[1].Omega);
            float nRear = Wheels[2].NormalForce + Wheels[3].NormalForce, nFront = Wheels[0].NormalForce + Wheels[1].NormalForce;
            float frontShare = front ? (nRear + nFront > 1f ? nFront / (nRear + nFront) : 0.5f) : 0f;
            float wAxle = front ? Mathf.Lerp(wRear, wFront, frontShare) : wRear;
            float wIn = wAxle * ratio;
            InputRpm = wIn * 60f / (2f * Mathf.PI);
            float rpm = EngineRpm;

            float throttle = 0f;
            if (u.Combustion)
            {
                float gov = Mathf.Clamp01(T.idleThrottle + (T.idleRpm - rpm) * T.idleGain);
                throttle = Mathf.Max(Shifting ? 0f : Gas, u.HandThrottle, gov);   // the shift is made off the gas
                if (rpm > T.cutoffRpm) throttle = 0f;                 // ignition cut
            }
            CombustionTorque = u.Combustion ? throttle * Torque(rpm) * Mathf.Clamp01(u.TorqueScale) : 0f;
            float drag = T.engineFrictionNm + (1f - throttle) * T.engineBrakeNmPer1000 * rpm / 1000f;
            float crank = u.CrankRpm > 0f ? u.CrankTorque * Mathf.Clamp01(1f - rpm / u.CrankRpm) : 0f;

            float cap = 0f;
            if (Gear != 0 && !Shifting)
            {
                float window = u.Combustion
                    ? Smooth(T.clutchEngageFromRpm, T.clutchEngageToRpm, rpm)
                    : u.IgnitionOn ? Smooth(T.pushEngageFromRpm, T.pushEngageToRpm, Mathf.Abs(InputRpm)) : 0f;
                cap = T.clutchCapacity * _peakTorque * window;
            }
            float iWheels = 0f;
            for (int i = 0; i < 4; i++)
            {
                if (i < 2 && !front) continue;
                VanWheel w = Wheels[i];
                iWheels += w.Inertia + w.SlipStiffness * w.Radius * w.Radius * dt;
            }
            float clutch = 0f;
            if (cap > 0f && Mathf.Abs(ratio) > 0.01f)
            {
                float iIn = iWheels / (ratio * ratio);
                float sync = (EngineOmega - wIn) / (dt * (1f / T.engineInertia + 1f / Mathf.Max(1e-4f, iIn)));
                clutch = Mathf.Clamp(sync, -cap, cap);
            }
            ClutchTorque = clutch;

            // engine: friction never turns it backwards
            float net = CombustionTorque + crank - clutch;
            float omega = EngineOmega + net / T.engineInertia * dt;
            float lossStep = drag / T.engineInertia * dt;
            omega = omega > 0f ? Mathf.Max(0f, omega - lossStep) : Mathf.Min(0f, omega + lossStep);
            EngineOmega = Mathf.Max(0f, omega);

            float axle = clutch * ratio;                           // N·m at the wheels, all driven together
            float perRear = rear ? axle * (1f - frontShare) * 0.5f : 0f;
            float perFront = front ? axle * frontShare * 0.5f : 0f;

            // ---- brakes: pedal per wheel, hand brake on the transmission (rear, or all four with the front axle in)
            float hand = u.Handbrake * T.handbrakeNm * T.finalDrive;
            float handRear = front ? hand * (1f - frontShare) * 0.5f : hand * 0.5f;
            float handFront = front ? hand * frontShare * 0.5f : 0f;

            for (int i = 0; i < 4; i++)
            {
                VanWheelInput g = ground[i];
                g.Heading = Quaternion.AngleAxis(_steer[i], g.Up) * forward;
                g.DriveTorque = i < 2 ? perFront : perRear;
                g.BrakeTorque = Brake * (i < 2 ? T.brakeFrontNm : T.brakeRearNm) + (i < 2 ? handFront : handRear);
                ground[i] = g;
                forces[i] = Wheels[i].Step(g, dt);
            }
        }

        static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));

        // ---- W / S → gas, brake, reverse; automatic gear logic (§4.3)
        void Pedals(float pedal, float kmh, float dt)
        {
            float speedMs = kmh / 3.6f;
            bool reverse = Gear < 0;
            float fwdKey = Mathf.Max(0f, pedal), backKey = Mathf.Max(0f, -pedal);
            Gas = reverse ? backKey : fwdKey;
            Brake = reverse ? fwdKey : backKey;

            // standing: hold the "other" key 0.4 s to change direction
            float key = reverse ? fwdKey : backKey;
            if (Mathf.Abs(kmh) < T.reverseEngageKmh && key > 0.5f)
            {
                _dirHold += dt;
                if (_dirHold >= T.reverseHoldTime)
                {
                    SetGear(reverse ? 1 : -1);
                    _dirHold = 0f;
                    Gas = Brake = 0f;
                }
            }
            else _dirHold = 0f;

            if (_shift > 0f) _shift -= dt;
            if (_lockout > 0f) _lockout -= dt;
            // the box shifts on road speed (the input-shaft rpm without wheel slip): on the spinning wheels' own speed it
            // shifted up in wheelspin, then the wheels bit during the torque-free shift and it shifted down again (hunting,
            // VanMotionTests 21.10: 11 shifts / 4 downshifts in 60 s at full W)
            ShiftRpm = Mathf.Abs(speedMs / T.wheelRadius * Ratio(Gear)) * 60f / (2f * Mathf.PI);
            if (Gear >= 1 && _lockout <= 0f)
            {
                float rpm = ShiftRpm;
                bool kick = Gas >= T.kickdownPedal && _lastPedal < T.kickdownPedal;
                if (Gear < 4 && rpm > T.shiftUpRpm * (T.shiftUpFloor + (1f - T.shiftUpFloor) * Gas)) SetGear(Gear + 1);
                else if (Gear > 1 && rpm < T.shiftDownRpm) SetGear(Gear - 1);
                else if (kick && Gear > 1 && rpm * T.gearRatios[Gear - 2] / T.gearRatios[Gear - 1] <= T.kickdownMaxRpm) SetGear(Gear - 1);
            }
            if (Gear >= 1 && Mathf.Abs(kmh) < T.reverseEngageKmh && Gear != 1 && _lockout <= 0f) SetGear(1);
            _lastPedal = Gas;
        }

        void SetGear(int g)
        {
            if (g == Gear) return;
            Gear = g;
            _shift = T.shiftTime;
            _lockout = T.shiftLockout;
            Shifts++;
        }

        // ---- keyboard steering (§4.4): rate-limited, lock fades above 40 km/h (input shaping, PHYS-02.7); Ackermann
        void SteerStep(float key, float kmh, float dt)
        {
            float fade = Mathf.Lerp(1f, T.steerFadeMin, Mathf.InverseLerp(T.steerFadeFromKmh, T.steerFadeToKmh, Mathf.Abs(kmh)));
            float target = Mathf.Clamp(key, -1f, 1f) * T.maxSteerDeg * fade;
            bool back = Mathf.Abs(target) < Mathf.Abs(SteerDeg) || Mathf.Sign(target) != Mathf.Sign(SteerDeg);
            SteerDeg = Mathf.MoveTowards(SteerDeg, target, (back ? T.steerReturnRate : T.steerRate) * dt);
            float a = Mathf.Abs(SteerDeg) * Mathf.Deg2Rad;
            if (a < 1e-4f) { _steer[0] = _steer[1] = 0f; return; }
            float radius = T.wheelbase / Mathf.Tan(a);
            float inner = Mathf.Atan(T.wheelbase / (radius - 0.5f * T.track)) * Mathf.Rad2Deg;
            float outer = Mathf.Atan(T.wheelbase / (radius + 0.5f * T.track)) * Mathf.Rad2Deg;
            float s = Mathf.Sign(SteerDeg);
            // turning right (+): the right wheel (1) is inside
            _steer[0] = s * (s > 0f ? outer : inner);
            _steer[1] = s * (s > 0f ? inner : outer);
            _steer[2] = _steer[3] = 0f;
        }
    }
}
