using NUnit.Framework;
using Snow.Vehicles.Van;
using UnityEngine;

namespace Snow.Tests
{
    /// <summary>
    /// VAN3_INTEGRATION_UNITY.md §4 on a one-dimensional bench: the van as a point mass on a straight slope, its four
    /// wheels at the static pose of the model. Not the bench (that is P09_VanBench, AT-V05/V06): this pins the wheel and
    /// drivetrain rules — suspension from the static pose, no shift hunting, locked wheels, the hand brake — and prints the
    /// numbers for docs/dev/Tuning/Van.md.
    /// </summary>
    public class VanMotionTests
    {
        const float Dt = 1f / 120f;

        sealed class Line
        {
            public readonly VanTuning T;
            public readonly VanMotion M;
            public float V, X, Grade, Mu, Mass;
            readonly VanWheelInput[] _g = new VanWheelInput[4];
            readonly Vector3[] _f = new Vector3[4];

            public Line(float mu, float grade = 0f, float mass = 1950f)
            {
                T = ScriptableObject.CreateInstance<VanTuning>();
                Mass = mass;
                M = new VanMotion(T, mass);
                Mu = mu;
                Grade = grade;
            }

            public void Run(float seconds, float pedal, float hand = 0f, bool engine = true, System.Func<bool> until = null)
            {
                for (float t = 0f; t < seconds; t += Dt)
                {
                    Step(pedal, hand, engine);
                    if (until != null && until()) return;
                }
            }

            public void Step(float pedal, float hand, bool engine)
            {
                for (int i = 0; i < 4; i++)
                    _g[i] = new VanWheelInput
                    {
                        Hit = true,
                        Distance = T.bumpTravel + T.wheelRadius,          // the model pose on flat ground
                        Point = new Vector3(T.wheelCentres[i].x, 0f, X + T.wheelCentres[i].z),
                        Normal = Vector3.up, Up = Vector3.up, Velocity = Vector3.forward * V,
                        Mu = Mu, Rolling = T.rollingResistanceSnow, Radius = T.wheelRadius,
                    };
                var u = new VanMotion.Inputs { Pedal = pedal, Handbrake = hand, Combustion = engine, IgnitionOn = engine, TorqueScale = 1f, ForwardSpeed = V };
                M.Step(u, _g, Vector3.forward, Dt, _f);
                float fx = 0f;
                foreach (var f in _f) fx += f.z;
                float slope = Mathf.Atan(Grade);
                fx -= Mass * 9.81f * Mathf.Sin(slope);
                fx -= 0.5f * T.airDensity * T.dragArea * V * Mathf.Abs(V);
                V += fx / Mass * Dt;
                X += V * Dt;
            }

            public void Start() => M.EngineOmega = T.idleRpm * 2f * Mathf.PI / 60f;
        }

        [Test]
        public void StaticPose_CarriesStaticLoad_WithoutBumpStop()
        {
            var l = new Line(0.32f);
            l.Run(1f, 0f, 1f, false);
            float total = 0f;
            for (int i = 0; i < 4; i++)
            {
                VanWheel w = l.M.Wheels[i];
                Assert.That(Mathf.Abs(w.Travel), Is.LessThan(0.002f), $"wheel {i} travel");
                Assert.That(w.Compression, Is.LessThan(w.Droop + w.BumpTravel), $"wheel {i} on the bump stop");
                total += w.NormalForce;
            }
            Assert.That(total, Is.EqualTo(1950f * 9.81f).Within(1950f * 9.81f * 0.01f));
            Debug.Log($"[VanTest] droop front {l.M.Wheels[0].Droop:0.000} m, rear {l.M.Wheels[2].Droop:0.000} m");
        }

        [TestCase(false, 0.32f)]
        [TestCase(true, 0.32f)]
        [TestCase(false, 0.45f)]
        public void Launch_0To40(bool frontAxle, float mu)
        {
            var l = new Line(mu, 0f, 2290f);
            l.Start();
            l.M.FrontAxle = frontAxle;
            float t = 0f;
            l.Run(40f, 1f, 0f, true, () => { t += Dt; return l.V * 3.6f >= 40f; });
            Debug.Log($"[VanTest] 0-40 km/h front={frontAxle} mu={mu}: {t:0.0} s, gear {l.M.Gear}, rpm {l.M.EngineRpm:0}, shifts {l.M.Shifts}");
            Assert.That(l.V * 3.6f, Is.GreaterThanOrEqualTo(40f), "never reached 40 km/h");
        }

        [Test]
        public void FullThrottle_NoShiftHunting()
        {
            var l = new Line(0.45f);
            l.Start();
            int last = l.M.Gear, downs = 0;
            for (float t = 0f; t < 60f; t += Dt)
            {
                l.Step(1f, 0f, true);
                if (l.M.Gear < last) downs++;
                last = l.M.Gear;
            }
            Debug.Log($"[VanTest] 60 s full W, mu 0.45: {l.V * 3.6f:0} km/h, gear {l.M.Gear}, rpm {l.M.EngineRpm:0}, shifts {l.M.Shifts}, downshifts {downs}");
            Assert.That(downs, Is.EqualTo(0), "downshift under full throttle on the flat (hunting)");
            Assert.That(l.M.Shifts, Is.LessThanOrEqualTo(3));
        }

        [Test]
        public void Brake_From40_LocksWheels()
        {
            var l = new Line(0.32f);
            l.Start();
            l.M.FrontAxle = true;                                  // the run-up only; the brakes do not care
            l.Run(40f, 1f, 0f, true, () => l.V * 3.6f >= 40f);
            Assert.That(l.V * 3.6f, Is.GreaterThanOrEqualTo(40f), "run-up did not reach 40 km/h");
            float x0 = l.X;
            bool locked = false;
            l.Run(20f, -1f, 0f, true, () => { locked |= l.M.Wheels[0].Locked && l.M.Wheels[2].Locked; return l.V <= 0.05f; });
            Debug.Log($"[VanTest] brake 40-0 mu 0.32: {l.X - x0:0.0} m, locked {locked}, rpm {l.M.EngineRpm:0}");
            Assert.That(locked, "no wheel lock on snow at full brake");
            Assert.That(l.M.EngineRpm, Is.GreaterThan(l.T.stallRpm), "engine stalled while braking");
        }

        [TestCase(0.10f, true)]
        [TestCase(0.30f, false)]
        public void Handbrake_OnSlope(float grade, bool holds)
        {
            var l = new Line(0.32f, grade);
            l.Run(5f, 0f, 1f, false);
            Debug.Log($"[VanTest] hand brake {grade * 100f:0} %: v {l.V:0.00} m/s, x {l.X:0.00} m after 5 s");
            if (holds) Assert.That(Mathf.Abs(l.X), Is.LessThan(0.05f));
            else Assert.That(l.X, Is.LessThan(-0.5f));
        }

        [Test]
        public void Idle_InGear_WithBrake_DoesNotStall()
        {
            var l = new Line(0.32f);
            l.Start();
            l.Run(5f, 0f, 1f, true);
            Assert.That(l.M.EngineRpm, Is.GreaterThan(450f));
            l.Run(5f, -1f, 0f, true);                               // S at a standstill: becomes R after 0.4 s, S is then gas
            Debug.Log($"[VanTest] S held 5 s from standstill: gear {l.M.Gear}, v {l.V:0.00} m/s");
            Assert.That(l.M.Gear, Is.EqualTo(-1));
            Assert.That(l.V, Is.LessThan(-0.5f), "reverse does not drive");
        }
    }
}
