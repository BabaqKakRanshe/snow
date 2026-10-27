using UnityEngine;

namespace Snow.Vehicles.Van
{
    /// <summary>What the van's body gives one wheel each physics step (world space).</summary>
    public struct VanWheelInput
    {
        /// <summary>The ray from the wheel's top of travel found ground within <see cref="VanWheel.RayLength"/>.</summary>
        public bool Hit;
        /// <summary>m from the ray origin (wheel centre of the model pose + bumpTravel along up) to the ground.</summary>
        public float Distance;
        public Vector3 Point, Normal, Up, Heading, Velocity;
        /// <summary>N·m on the wheel from the drivetrain, + = forward.</summary>
        public float DriveTorque;
        /// <summary>N·m the brakes can hold (service + hand brake), ≥ 0.</summary>
        public float BrakeTorque;
        public float Mu, Rolling;
        /// <summary>m, rolling radius now (a flat tyre is smaller).</summary>
        public float Radius;
    }

    /// <summary>
    /// One ray wheel of the van (VAN3_INTEGRATION_UNITY.md §4.2): a copy of Snow.Aircraft.WheelContact (tyre anchor and
    /// friction circle), which stays untouched, with what a driven car wheel needs. Pure C#.
    ///  - Suspension from the static pose (the spring force acts along the van's up, the suspension axis): the model shows the wheel loaded (F₀). Compression c is measured from full droop
    ///    F₀/k below that pose, N = k·c (no preload), so at the model pose N = F₀ and at first touch N = 0. A bump stop
    ///    (× bumpStop) past the compression travel. Damper bump / rebound.
    ///  - The wheel spins on its own (Omega, inertia): drive torque, brake torque and the tyre's longitudinal force. The
    ///    spin step is implicit (the tyre's slip stiffness at 1/120 s would blow up an explicit step): the slip force is
    ///    linear up to μN at slipFull, past that the tyre slides with μN — wheelspin and locked wheels come from this.
    ///  - Sideways the WheelContact anchor: holds until the friction circle (what longitudinal force leaves of μN) is
    ///    exceeded. A locked wheel standing (|v| below 0.3 m/s) also holds along its heading through the anchor, so a van
    ///    with the hand brake set stays on a slope it can hold, and slides down one it cannot.
    /// </summary>
    public class VanWheel
    {
        public readonly float Radius, Droop, BumpTravel, Spring, DamperBump, DamperRebound, BumpStop, Inertia, SlipFull;
        readonly float _tyreK, _tyreDamping, _bumpStopDamping, _maxRate, _cosSteep;

        Vector3 _anchor;
        bool _anchored;
        float _lastC;

        /// <summary>m from full droop (0 = hanging).</summary>
        public float Compression { get; private set; }
        /// <summary>m of the wheel from the model's pose, + = up into the arch (VanRig.SetWheel).</summary>
        public float Travel => Compression - Droop;
        public bool Grounded { get; private set; }
        public float NormalForce { get; private set; }
        /// <summary>rad/s, + = rolling forward.</summary>
        public float Omega { get; set; }
        /// <summary>degrees, running (for the rig).</summary>
        public float SpinDeg { get; private set; }
        public bool Sliding { get; private set; }
        public bool Locked { get; private set; }
        public float LongForce { get; private set; }
        public float LatForce { get; private set; }
        /// <summary>m/s of slip, ω·r − v along the heading.</summary>
        public float Slip { get; private set; }
        /// <summary>N/(m/s): the slip stiffness of the last step (the clutch uses it to see what the wheel is tied to).</summary>
        public float SlipStiffness { get; private set; }
        public float RayLength => BumpTravel + Droop + Radius;

        /// <param name="staticLoad">F₀, N: this wheel's share of the van's weight in the model pose.</param>
        public VanWheel(float radius, float staticLoad, VanTuning t)
        {
            Radius = radius;
            Spring = t.springRate;
            Droop = staticLoad / t.springRate;
            BumpTravel = t.bumpTravel;
            DamperBump = t.damperBump;
            DamperRebound = t.damperRebound;
            BumpStop = t.bumpStop;
            Inertia = t.wheelInertia;
            SlipFull = Mathf.Max(0.02f, t.slipFull);
            _tyreK = t.tyreStiffness;
            _tyreDamping = t.tyreDamping;
            _bumpStopDamping = Mathf.Max(1f, t.bumpStopDamping);
            _maxRate = Mathf.Max(0.1f, t.damperMaxRate);
            _cosSteep = Mathf.Cos(t.steepNormalDeg * Mathf.Deg2Rad);
        }

        public void Reset()
        {
            Lift();
            Omega = 0f;
        }

        void Lift()
        {
            Grounded = false;
            _anchored = false;
            Compression = 0f;
            _lastC = 0f;
            NormalForce = 0f;
            Sliding = false;
            LongForce = LatForce = Slip = 0f;
            SlipStiffness = 0f;
        }

        /// <summary>One step: returns the world force on the body at the contact point.</summary>
        public Vector3 Step(in VanWheelInput u, float dt)
        {
            float r = u.Radius > 0.05f ? u.Radius : Radius;
            float c = BumpTravel + Droop + r - u.Distance;
            if (!u.Hit || c <= 0f)
            {
                Lift();
                // in the air: drive and brakes act on the wheel alone
                float b0 = Inertia * Omega / dt + u.DriveTorque;
                Omega = Mathf.Abs(b0) <= u.BrakeTorque ? 0f : (b0 - Mathf.Sign(b0) * u.BrakeTorque) * dt / Inertia;
                Locked = Omega == 0f && u.BrakeTorque > 0f;
                SpinDeg = Mathf.Repeat(SpinDeg + Omega * Mathf.Rad2Deg * dt, 360f);
                return Vector3.zero;
            }

            // damper speed: the change of compression; on the first contact after a lift the body's own speed along up
            // (_lastC was 0 there, and c/dt kicked one corner with tens of kN — the van flipped off small bumps). Capped:
            // ground jumping under the ray at an edge is not a real 12 m/s hit
            float rate = Grounded ? (c - _lastC) / dt : -Vector3.Dot(u.Velocity, u.Up);
            rate = Mathf.Clamp(rate, -_maxRate, _maxRate);
            Grounded = true;
            Compression = c;
            _lastC = c;
            float maxC = Droop + BumpTravel;
            float spring = c <= maxC ? Spring * c : Spring * maxC + Spring * BumpStop * (c - maxC);
            // on the bump stop the rebound is damped harder: the stop soaks the hit up instead of throwing the body back
            float damper = rate > 0f ? DamperBump : DamperRebound * (c > maxC ? _bumpStopDamping : 1f);
            float n = Mathf.Max(0f, spring + damper * rate);
            NormalForce = n;

            // a hit on a steep face (a rock's side, the edge of a step) is taken as flat ground under the wheel: its normal
            // turned the whole spring force sideways at ground level and rolled the van over
            Vector3 normal = Vector3.Dot(u.Normal, u.Up) >= _cosSteep ? u.Normal : u.Up;
            Vector3 fwd = Vector3.ProjectOnPlane(u.Heading, normal).normalized;
            Vector3 side = Vector3.Cross(normal, fwd).normalized;
            float vLon = Vector3.Dot(u.Velocity, fwd), vLat = Vector3.Dot(u.Velocity, side);
            float fMax = u.Mu * n;
            float k = fMax / SlipFull;                              // N per m/s of slip
            // rolling resistance is a force at the contact patch of a rolling tyre (added below), not a brake on the spin:
            // as a spin brake it vanished whenever the wheel spun, and the van climbed 30 % on snow in 4×4 (bench 21.10)
            float cap = u.BrakeTorque;

            // implicit spin: I(ω'−ω)/dt = T_drive − r·F(ω') − T_brake, F = k(ω'r − v)
            float a = Inertia / dt + k * r * r;
            float b = Inertia * Omega / dt + u.DriveTorque + k * r * vLon;
            float omega = Mathf.Abs(b) <= cap ? 0f : (b - Mathf.Sign(b) * cap) / a;
            float lon = k * (omega * r - vLon);
            bool locked = omega == 0f && u.BrakeTorque > 0f;       // rolling resistance alone does not park a wheel

            if (!_anchored) { _anchor = u.Point; _anchored = true; }
            Vector3 d = u.Point - _anchor;
            float eLon = Vector3.Dot(d, fwd), eLat = Vector3.Dot(d, side);
            float massShare = Mathf.Max(20f, n / 9.81f);
            float cDamp = 2f * _tyreDamping * Mathf.Sqrt(_tyreK * massShare);

            bool parked = locked && Mathf.Abs(vLon) < 0.3f;
            if (parked)
            {
                // parked: the locked tyre holds its place along the heading, up to what the brake holds
                float hold = u.BrakeTorque / r;
                float want = -_tyreK * eLon - cDamp * vLon;
                lon = Mathf.Clamp(want, -hold, hold);
                if (Mathf.Abs(want) > hold) eLon = -(lon + cDamp * vLon) / _tyreK;   // the brake gives: the wheel turns
            }
            else eLon = 0f;                                        // rolling or sliding: the anchor travels with the wheel
            float lat = -_tyreK * eLat - cDamp * vLat;

            // friction circle: past μN the tyre slides, and the sliding force opposes the slip vector (ω·r − v along,
            // v across). A spinning or locked wheel keeps a side force in proportion to its side slip — not zero (bench
            // 21.10: with "longitudinal first" a spinning rear axle had no side grip at all and the van spun by itself)
            Sliding = false;
            if (lon * lon + lat * lat > fMax * fMax)
            {
                Sliding = true;
                float sLon = parked ? -vLon : omega * r - vLon, sLat = vLat;
                float s = Mathf.Sqrt(sLon * sLon + sLat * sLat);
                if (s > 0.05f) { lon = fMax * sLon / s; lat = -fMax * sLat / s; }
                else
                {
                    float scale = fMax / Mathf.Sqrt(lon * lon + lat * lat);
                    lon *= scale;
                    lat *= scale;
                }
                if (!parked)
                {
                    // the wheel's spin with the force the tyre can really give
                    float b2 = Inertia * Omega / dt + u.DriveTorque - r * lon;
                    omega = Mathf.Abs(b2) <= cap ? 0f : (b2 - Mathf.Sign(b2) * cap) * dt / Inertia;
                    locked = omega == 0f && u.BrakeTorque > 0f;
                }
                eLon = parked ? -(lon + cDamp * vLon) / _tyreK : 0f;
                eLat = -(lat + cDamp * vLat) / _tyreK;
            }
            _anchor = u.Point - fwd * eLon - side * eLat;

            // a sliding tyre does not tie the wheel to the ground: the clutch must see only the wheel's own inertia, or its
            // sync torque spins the wheel 16 rad/s per step and the drive chatters to nothing (VanMotionTests 21.10)
            SlipStiffness = Sliding ? 0f : k;
            Locked = locked;
            Omega = omega;
            Slip = omega * r - vLon;
            SpinDeg = Mathf.Repeat(SpinDeg + omega * Mathf.Rad2Deg * dt, 360f);
            if (!locked) lon -= Mathf.Clamp(vLon / 0.1f, -1f, 1f) * u.Rolling * n;   // fades in over 0.1 m/s: no sign flip at rest
            LongForce = lon;
            LatForce = lat;
            return u.Up * n + fwd * lon + side * lat;          // the spring pushes along the suspension, the tyre in the ground plane
        }
    }
}
