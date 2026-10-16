using Snow.World;
using UnityEngine;

namespace Snow.Aircraft
{
    /// <summary>
    /// Applies the flight model to the aircraft's Rigidbody (§6 AircraftPhysics, §9.1, PHYS-01/02). Server only, called
    /// from the vehicle's FixedUpdate. It only gathers inputs and applies forces (AddForceAtPosition / AddTorque); the
    /// aerodynamics, gear and engine are pure C# (AeroSection, WheelContact, TurbopropEngine). It never sets the pose.
    ///
    /// Points come from the model's sockets (UNITY_CONTRACT App. A): 4 wing sections, 2 stabiliser halves, the fin, the
    /// fuselage, the thrust line and 3 wheel rays. At each point: v_air = GetPointVelocity(p) − wind(p), plus the
    /// propeller wash for the tail. Downwash at the tail lags the wing by the travel time. Propeller torque reaction,
    /// gyroscopic moment and P-factor are applied explicitly; the slipstream swirl reaches the fin.
    /// Axes: +Z nose, +Y up, +X right wing. SI units, radians inside.
    /// </summary>
    public class AircraftPhysics : MonoBehaviour
    {
        public struct Inputs
        {
            /// <summary>Actuator positions, deg (Rig signs): elevator + = TE up, aileron + = right roll, rudder + = TE right.</summary>
            public float ElevatorDeg, AileronDeg, RudderDeg, FlapDeg, TailSteerDeg;
            /// <summary>Brake friction coefficient × pedal, per main wheel.</summary>
            public float BrakeLeft, BrakeRight;
            /// <summary>Propeller thrust N, shaft torque N·m, propeller speed rad/s.</summary>
            public float ThrustN, ShaftTorqueNm, PropOmega;
        }

        public struct Outputs
        {
            public float GearL, GearR, GearT;            // compression 0..1 of travel
            public float WheelMain, WheelTail;           // rolling speed m/s
            public float WingAlphaDeg, StallAlphaDeg;    // mean of the inner wing sections
            public float WingAttached;                   // 0..1
            public float Airspeed;                       // air-relative speed along the nose at the CG, m/s
            public Vector3 AirVelocityBody;              // air-relative velocity of the CG, body axes
            public bool OnGround;
            public float HeightAgl;
        }

        public AircraftTuning tuning;
        public Rigidbody body;
        [Header("Model sockets")]
        public Transform wingLIn, wingLOut, wingRIn, wingROut, hStabL, hStabR, fin, fuselage, thrustPoint;
        public Transform wheelL, wheelR, wheelTail;
        [Tooltip("What the wheels and the ground-effect rays can touch")] public LayerMask groundMask = ~0;

        AeroSection _wLIn, _wLOut, _wRIn, _wROut, _hL, _hR, _fin;
        WheelContact _gL, _gR, _gT;
        float _wingAlphaLag;
        readonly RaycastHit[] _hits = new RaycastHit[12];
        public Outputs Last;

        public AeroSection[] Sections => new[] { _wLIn, _wLOut, _wRIn, _wROut, _hL, _hR, _fin };

        void Awake() => Build();

        public void Build()
        {
            AircraftTuning t = tuning;
            float d = t.dihedralDeg;
            // dihedral tilts each wing's normal inboard; chord +Z; points from the sockets (prefab space)
            // incidence pitches the wing's chord and normal nose-up about the span (rotation about +X is nose-down)
            Quaternion inc = Quaternion.AngleAxis(-t.wingIncidenceDeg, Vector3.right);
            Vector3 chord = inc * Vector3.forward;
            Vector3 nL = Quaternion.AngleAxis(-d, Vector3.forward) * (inc * Vector3.up);
            Vector3 nR = Quaternion.AngleAxis(d, Vector3.forward) * (inc * Vector3.up);
            _wLIn = new AeroSection("Wing_L_In", t.wingInner.Clone(), Local(wingLIn), nL, chord);
            _wLOut = new AeroSection("Wing_L_Out", t.wingOuter.Clone(), Local(wingLOut), nL, chord);
            _wRIn = new AeroSection("Wing_R_In", t.wingInner.Clone(), Local(wingRIn), nR, chord);
            _wROut = new AeroSection("Wing_R_Out", t.wingOuter.Clone(), Local(wingROut), nR, chord);
            _hL = new AeroSection("HStab_L", t.hStab.Clone(), Local(hStabL), Vector3.up, Vector3.forward);
            _hR = new AeroSection("HStab_R", t.hStab.Clone(), Local(hStabR), Vector3.up, Vector3.forward);
            _fin = new AeroSection("Fin", t.fin.Clone(), Local(fin), Vector3.right, Vector3.forward);
            _gL = new WheelContact(t.mainWheelRadius, t.mainTravel, t.mainSpring, t.mainDamper, t.bumpStop, t.mainGrip);
            _gR = new WheelContact(t.mainWheelRadius, t.mainTravel, t.mainSpring, t.mainDamper, t.bumpStop, t.mainGrip);
            _gT = new WheelContact(t.tailWheelRadius, t.tailTravel, t.tailSpring, t.tailDamper, t.bumpStop, t.tailGrip);
        }

        Vector3 Local(Transform socket) => socket != null ? transform.InverseTransformPoint(socket.position) : Vector3.zero;

        // ENGINEERING: moments about the centre of mass along the fuselage (Socket_WheelContact_*); the main wheels split
        // the rest evenly. Empty aircraft with the CG at Socket_CenterOfMass: 0.44 / 0.44 / 0.12. Fuel, people and cargo on
        // joints move the CG and the shares with it.
        void WheelShares(out float main, out float tail)
        {
            main = 0.44f;
            tail = 0.12f;
            if (wheelL == null || wheelR == null || wheelTail == null) return;
            float zMain = 0.5f * (Local(wheelL).z + Local(wheelR).z);
            float span = zMain - Local(wheelTail).z;
            if (span < 0.5f) return;
            tail = Mathf.Clamp((zMain - body.centerOfMass.z) / span, 0.02f, 0.6f);
            main = 0.5f * (1f - tail);
        }

        /// <summary>Set mass, centre of mass (body space) and the diagonal inertia (kg·m²) explicitly (§8.1).</summary>
        public void ApplyMass(float mass, Vector3 com, Vector3 inertia)
        {
            body.mass = mass;
            body.centerOfMass = com;
            body.inertiaTensor = inertia;
            body.inertiaTensorRotation = Quaternion.identity;
        }

        public void Step(in Inputs u, float dt)
        {
            AircraftTuning t = tuning;
            Vector3 cg = body.worldCenterOfMass;
            float rho = EnvironmentProvider.DensityAt(cg.y);
            Vector3 airCg = body.linearVelocity - EnvironmentProvider.WindAt(cg);
            Vector3 airBody = transform.InverseTransformDirection(airCg);
            float forward = Mathf.Max(0f, airBody.z);

            // propeller slipstream (momentum theory): far-wake velocity increment, and its swirl at the fin
            float diskArea = Mathf.PI * 0.25f * t.propDiameter * t.propDiameter;
            float wash = u.ThrustN > 0f ? Mathf.Sqrt(forward * forward + 2f * u.ThrustN / (rho * diskArea)) - forward : 0f;

            // --- wing: flaps on the inner sections, ailerons on the outer (right roll: right TE up, left TE down)
            float flap = u.FlapDeg * Mathf.Deg2Rad;
            // factory rigging (not a control input, not a stabiliser): a fixed tab and aileron rig cancel the left-turning
            // tendencies of a clockwise propeller near cruise; at climb power the pilot still needs right rudder
            float ail = (u.AileronDeg + t.aileronRigDeg) * Mathf.Deg2Rad;
            Section(_wLIn, flap, 0f, Vector3.zero, rho, dt, true);
            Section(_wRIn, flap, 0f, Vector3.zero, rho, dt, true);
            Section(_wLOut, ail, 0f, Vector3.zero, rho, dt, true);
            Section(_wROut, -ail, 0f, Vector3.zero, rho, dt, true);

            // --- downwash at the tail, delayed by the travel time from the wing
            float wingAlpha = 0.5f * (_wLIn.Alpha + _wRIn.Alpha);
            float attached = 0.5f * (_wLIn.Attached + _wRIn.Attached);
            float lag = t.tailArm / Mathf.Max(5f, forward);
            _wingAlphaLag += (wingAlpha - _wingAlphaLag) * (1f - Mathf.Exp(-dt / lag));
            float eps = (t.downwash0 + t.downwashSlope * _wingAlphaLag) * attached;

            // --- tail in the slipstream: elevator TE up = less tail lift (deflection down = −elevator)
            Vector3 tailWash = new Vector3(0f, 0f, 0.6f * wash);
            float elev = -u.ElevatorDeg * Mathf.Deg2Rad;
            Section(_hL, elev, eps, tailWash, rho, dt, false);
            Section(_hR, elev, eps, tailWash, rho, dt, false);
            // fin: normal +X; rudder TE right pushes the tail left (deflection "down" = −rudder). The clockwise
            // (seen from the cockpit) slipstream moves the air at the fin to the right: the aircraft yaws left with power.
            Vector3 finWash = new Vector3(-0.15f * wash, 0f, 0.5f * wash);
            Section(_fin, -(u.RudderDeg + t.rudderRigDeg) * Mathf.Deg2Rad, 0f, finWash, rho, dt, false);

            // --- fuselage, gear and strut drag at their reference point
            if (fuselage != null)
            {
                Vector3 p = fuselage.position;
                Vector3 v = transform.InverseTransformDirection(body.GetPointVelocity(p) - EnvironmentProvider.WindAt(p));
                Vector3 f = -0.5f * rho * new Vector3(t.fuselageDragAreaSide * v.x * Mathf.Abs(v.x),
                    t.fuselageDragAreaVertical * v.y * Mathf.Abs(v.y), t.fuselageDragAreaAxial * v.z * Mathf.Abs(v.z));
                body.AddForceAtPosition(transform.TransformDirection(f), p, ForceMode.Force);
            }

            // --- thrust with P-factor (descending right blade pulls harder at a positive angle of attack)
            if (thrustPoint != null)
            {
                float bodyAlpha = forward > 3f ? Mathf.Atan2(-airBody.y, airBody.z) : 0f;
                float offset = u.ThrustN > 0f ? t.pFactor * Mathf.Clamp(bodyAlpha, -0.35f, 0.35f) : 0f;
                Vector3 at = thrustPoint.position + transform.right * offset;
                body.AddForceAtPosition(transform.forward * u.ThrustN, at, ForceMode.Force);
            }
            // torque reaction of a propeller turning clockwise from behind: the airframe rolls left (+Z)
            body.AddRelativeTorque(new Vector3(0f, 0f, u.ShaftTorqueNm), ForceMode.Force);
            // gyroscopic moment: H along −Z (body); the airframe feels H × ω
            Vector3 h = transform.TransformDirection(new Vector3(0f, 0f, -t.propInertia * u.PropOmega));
            body.AddTorque(Vector3.Cross(h, body.angularVelocity), ForceMode.Force);

            // --- gear: static weight shares from the contact points and the current centre of mass (v5 U4.3)
            float g = -Physics.gravity.y;
            float share = body.mass * g;
            WheelShares(out float mainShare, out float tailShare);
            Last.GearL = Wheel(_gL, wheelL, transform.forward, u.BrakeLeft, share * mainShare, t.mainTravel, dt, out Last.WheelMain);
            Last.GearR = Wheel(_gR, wheelR, transform.forward, u.BrakeRight, share * mainShare, t.mainTravel, dt, out _);
            Vector3 tailHeading = Quaternion.AngleAxis(u.TailSteerDeg, transform.up) * transform.forward;
            Last.GearT = Wheel(_gT, wheelTail, tailHeading, 0f, share * tailShare, t.tailTravel, dt, out Last.WheelTail);

            Last.WingAlphaDeg = wingAlpha * Mathf.Rad2Deg;
            Last.StallAlphaDeg = 0.5f * (_wLIn.StallAlpha + _wRIn.StallAlpha) * Mathf.Rad2Deg;
            Last.WingAttached = attached;
            Last.Airspeed = forward;
            Last.AirVelocityBody = airBody;
            Last.OnGround = _gL.Grounded || _gR.Grounded || _gT.Grounded;
            Last.HeightAgl = Height(cg);
        }

        void Section(AeroSection s, float deflection, float alphaOffset, Vector3 washBody, float rho, float dt, bool groundEffect)
        {
            Vector3 p = transform.TransformPoint(s.Position);
            Vector3 air = body.GetPointVelocity(p) - EnvironmentProvider.WindAt(p);
            Vector3 v = transform.InverseTransformDirection(air) + washBody;
            float phi = groundEffect ? AeroSection.GroundPhi(Height(p), tuning.span) : 1f;
            Vector3 f = s.Force(v, rho, deflection, alphaOffset, phi, dt);
            body.AddForceAtPosition(transform.TransformDirection(f), p, ForceMode.Force);
        }

        // wheel ray from the centre of the fully extended wheel (the socket), started one travel higher so a compressed
        // strut still finds the ground. Returns compression as a fraction of the travel for the Rig.
        float Wheel(WheelContact w, Transform socket, Vector3 heading, float brake, float load, float travel, float dt, out float roll)
        {
            roll = 0f;
            if (socket == null) return 0f;
            Vector3 up = transform.up;
            Vector3 origin = socket.position + up * travel;
            if (!Cast(origin, -up, w.Radius + travel, out RaycastHit hit))
            {
                w.Lift();
                return 0f;
            }
            float distance = hit.distance - travel;
            Vector3 v = body.GetPointVelocity(hit.point);
            if (hit.rigidbody != null) v -= hit.rigidbody.GetPointVelocity(hit.point);
            float g = -Physics.gravity.y;
            Vector3 f = w.Step(distance, hit.point, hit.normal, up, heading, v, brake * tuning.brakeGrip, tuning.rollingResistance,
                tuning.tyreStiffness, tuning.tyreDamping, load / g, dt);
            if (w.Grounded) body.AddForceAtPosition(f, hit.point, ForceMode.Force);
            roll = w.RollSpeed;
            return Mathf.Clamp01(w.Compression / travel);
        }

        bool Cast(Vector3 origin, Vector3 dir, float length, out RaycastHit best)
        {
            best = default;
            int n = Physics.RaycastNonAlloc(origin, dir, _hits, length, groundMask, QueryTriggerInteraction.Ignore);
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                Collider c = _hits[i].collider;
                if (c.attachedRigidbody == body) continue;                        // own airframe
                if (c is CharacterController) continue;                           // players are not runway
                Rigidbody rb = c.attachedRigidbody;
                if (rb != null && !rb.isKinematic && rb.mass < 300f) continue;    // do not roll on crates
                if (!found || _hits[i].distance < best.distance) { best = _hits[i]; found = true; }
            }
            return found;
        }

        float Height(Vector3 p) => Cast(p, Vector3.down, 200f, out RaycastHit hit) ? hit.distance : 200f;
    }
}
