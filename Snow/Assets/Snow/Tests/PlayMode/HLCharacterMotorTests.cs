using System.Collections.Generic;
using NUnit.Framework;
using Snow.Character;
using UnityEngine;

namespace Snow.Tests
{
    /// <summary>
    /// NEW-01, §7.1–7.2 with real colliders: the motor is stepped by hand with a fixed dt, so results do not depend
    /// on frame timing. Ledge heights check the TEMPORARY jump numbers: 0.5 m jump, +0.35 m crouch-jump.
    /// </summary>
    public class HLCharacterMotorTests
    {
        readonly List<GameObject> _made = new List<GameObject>();
        HLCharacterMotor _motor;

        [SetUp]
        public void SetUp()
        {
            Box("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(200f, 1f, 200f));
            var p = new GameObject("TestPlayer");
            _made.Add(p);
            p.AddComponent<CharacterController>();
            _motor = p.AddComponent<HLCharacterMotor>();
            _motor.enabled = false;                      // stepped manually, not by FixedUpdate
            _motor.Teleport(Vector3.zero, 0f, Vector3.zero);
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _made) Object.DestroyImmediate(go);
            _made.Clear();
        }

        GameObject Box(string name, Vector3 center, Vector3 size, float pitchDeg = 0f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetPositionAndRotation(center, Quaternion.Euler(pitchDeg, 0f, 0f));
            go.transform.localScale = size;
            _made.Add(go);
            Physics.SyncTransforms();
            return go;
        }

        /// <summary>Ledge whose near face is at z, top at height h, 20 m deep.</summary>
        void Ledge(float z, float h) => Box("Ledge", new Vector3(0f, h * 0.5f, z + 10f), new Vector3(4f, h, 20f));

        void Run(float seconds, MoveInput input, float dt = 0.02f, System.Action<int> perStep = null)
        {
            int n = Mathf.RoundToInt(seconds / dt);
            for (int i = 0; i < n; i++)
            {
                perStep?.Invoke(i);
                if (perStep == null) _motor.Input = input;
                _motor.Simulate(dt);
            }
        }

        static MoveInput Fwd(bool jump = false, bool crouch = false) =>
            new MoveInput { Move = new Vector2(0f, 1f), Jump = jump, Crouch = crouch };

        float Y => _motor.transform.position.y;
        float Z => _motor.transform.position.z;

        [Test]
        public void FlatGround_RunSpeedAndGrounded()
        {
            Run(0.2f, default);                           // settle
            Assert.That(_motor.Grounded, Is.True);
            Run(1.0f, Fwd());
            Vector3 v = _motor.Velocity;
            Assert.That(new Vector2(v.x, v.z).magnitude, Is.EqualTo(3.6f).Within(0.02f));
            Assert.That(Y, Is.EqualTo(0f).Within(0.03f));
        }

        [Test]
        public void Step_0_30_IsClimbed_0_45_Blocks()
        {
            Ledge(1.5f, 0.30f);
            Run(0.2f, default);
            Run(1.5f, Fwd());
            Assert.That(Y, Is.EqualTo(0.30f).Within(0.03f), "0.30 m is below stepOffset 0.35");

            TearDown(); SetUp();
            Ledge(1.5f, 0.45f);
            Run(0.2f, default);
            Run(1.5f, Fwd());
            Assert.That(Y, Is.LessThan(0.05f), "0.45 m needs a jump");
            Assert.That(Z, Is.LessThan(1.5f));
        }

        [Test]
        public void JumpApex_WithCapsule([Values(0.02f, 1f / 120f)] float dt)
        {
            Run(0.2f, default, dt);
            float apex = 0f;
            _motor.QueueJump();
            Run(1.0f, default, dt, i => { _motor.Input = default; apex = Mathf.Max(apex, Y); });
            apex = Mathf.Max(apex, Y);
            Assert.That(apex, Is.EqualTo(0.5f).Within(0.02f));
            Assert.That(_motor.Grounded, Is.True, "landed");
        }

        // running jump: take off 0.9 m before the face, optionally tuck the legs 3 steps after take-off, keep running
        float TryLedge(float h, bool crouchJump)
        {
            Ledge(0f, h);
            _motor.Teleport(new Vector3(0f, 0f, -4f), 0f, Vector3.zero);
            Physics.SyncTransforms();
            Run(0.2f, default);
            int sinceJump = -1;
            Run(3.0f, default, 0.02f, i =>
            {
                if (sinceJump < 0 && Z + 0.34f >= -0.9f) { _motor.QueueJump(); sinceJump = 0; }
                else if (sinceJump >= 0) sinceJump++;
                bool crouch = crouchJump && sinceJump >= 3 && sinceJump < 40;
                _motor.Input = Fwd(crouch: crouch);
            });
            return Y;
        }

        [Test] public void Ledge_0_45_Jump() => Assert.That(TryLedge(0.45f, false), Is.EqualTo(0.45f).Within(0.03f));
        [Test] public void Ledge_0_70_JumpFails() => Assert.That(TryLedge(0.70f, false), Is.LessThan(0.05f));
        [Test] public void Ledge_0_70_CrouchJump() => Assert.That(TryLedge(0.70f, true), Is.EqualTo(0.70f).Within(0.03f));
        [Test] public void Ledge_1_00_CrouchJumpFails() => Assert.That(TryLedge(1.00f, true), Is.LessThan(0.05f));

        [Test]
        public void Stairs_0_18_ClimbedWithoutJumping()
        {
            for (int k = 0; k < 6; k++)
                Box("Stair" + k, new Vector3(0f, 0.09f * (k + 1), 1.5f + 0.28f * k + 5f), new Vector3(3f, 0.18f * (k + 1), 10f));
            Run(0.2f, default);
            Run(2.5f, Fwd());
            Assert.That(Y, Is.EqualTo(6 * 0.18f).Within(0.03f), "top of six 0.18 m steps");
        }

        [Test]
        public void StepDown_0_25_StaysGrounded()
        {
            Box("Platform", new Vector3(0f, 0.125f, 0f), new Vector3(4f, 0.25f, 4f));   // top 0.25, front edge at z = 2
            _motor.Teleport(new Vector3(0f, 0.25f, 0f), 0f, Vector3.zero);
            Physics.SyncTransforms();
            Run(0.2f, default);
            int airborne = 0;
            Run(1.5f, default, 0.02f, i => { _motor.Input = Fwd(); });
            for (int i = 0; i < 75; i++) { _motor.Input = Fwd(); _motor.Simulate(0.02f); if (!_motor.Grounded) airborne++; }
            Assert.That(Y, Is.EqualTo(0f).Within(0.02f));
            Assert.That(airborne, Is.EqualTo(0), "walking down a step below stepOffset never leaves the ground");
        }

        [Test]
        public void Ledge_0_70_CrouchJump_At120Hz()
        {
            Ledge(0f, 0.70f);
            _motor.Teleport(new Vector3(0f, 0f, -4f), 0f, Vector3.zero);
            Physics.SyncTransforms();
            float dt = 1f / 120f;
            Run(0.2f, default, dt);
            int sinceJump = -1;
            Run(3.0f, default, dt, i =>
            {
                if (sinceJump < 0 && Z + 0.34f >= -0.9f) { _motor.QueueJump(); sinceJump = 0; }
                else if (sinceJump >= 0) sinceJump++;
                bool crouch = sinceJump >= 7 && sinceJump < 96;   // same times as at 50 Hz
                _motor.Input = Fwd(crouch: crouch);
            });
            Assert.That(Y, Is.EqualTo(0.70f).Within(0.03f));
        }

        [Test]
        public void Wall_DoesNotStoreSpeed()
        {
            Box("Wall", new Vector3(0f, 1.5f, 1.5f), new Vector3(4f, 3f, 0.2f));
            Run(0.2f, default);
            Run(1.0f, Fwd());
            Assert.That(_motor.Velocity.z, Is.LessThan(0.05f), "velocity into the wall is clipped");
            Run(0.1f, new MoveInput { Move = new Vector2(0f, -1f) });
            Assert.That(_motor.Velocity.z, Is.LessThan(0f), "backing off works at once, nothing stored");
        }

        [Test]
        public void Crouch_StaysDownUnderLowCeiling()
        {
            Box("Ceiling", new Vector3(0f, 1.3f, 3f), new Vector3(4f, 0.2f, 3f)); // underside at 1.2 m, from z 1.5 to 4.5
            Run(0.2f, default);
            Run(2.0f, Fwd(crouch: true));
            Assert.That(_motor.Crouched, Is.True);
            Assert.That(Z, Is.GreaterThan(2.0f), "moved under the ceiling crouched");
            Run(0.2f, default);                            // crouch released: no headroom
            Assert.That(_motor.Crouched, Is.True, "must not stand up into the ceiling");
            Assert.That(_motor.Controller.height, Is.EqualTo(0.9f).Within(1e-4f));
        }

        [Test]
        public void Slope_30_Walkable()
        {
            Box("Ramp", new Vector3(0f, 0f, 4f), new Vector3(4f, 0.2f, 6f), -30f);
            Run(0.2f, default);
            Run(1.6f, Fwd());
            Assert.That(Y, Is.GreaterThan(0.8f), "walked up the 30 deg ramp");
        }
    }
}
