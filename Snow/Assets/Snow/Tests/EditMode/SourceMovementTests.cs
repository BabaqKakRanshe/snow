using NUnit.Framework;
using Snow.Character;
using UnityEngine;

namespace Snow.Tests
{
    /// <summary>NEW-01, PLANE_PROMPT §7.2: GoldSrc movement formulas and TEMPORARY numbers.</summary>
    public class SourceMovementTests
    {
        static readonly MovementTuning T = new MovementTuning();
        static readonly float[] Rates = { 30f, 50f, 60f, 120f, 144f };

        static MoveInput Forward(bool jump = false, bool sprint = false) =>
            new MoveInput { Move = new Vector2(0f, 1f), Jump = jump, Sprint = sprint };

        static float HSpeed(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        [Test]
        public void WishDirection_FollowsYaw()
        {
            var input = new MoveInput { Move = new Vector2(0f, 1f), YawDeg = 90f };
            Vector3 d = SourceMovement.WishDirection(input);
            Assert.That(d.x, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(d.z, Is.EqualTo(0f).Within(1e-5f));
            input.Move = new Vector2(1f, 0f);
            input.YawDeg = 0f;
            Assert.That(SourceMovement.WishDirection(input).x, Is.EqualTo(1f).Within(1e-5f), "strafe right is +X at yaw 0");
        }

        [Test]
        public void WishSpeed_Gaits()
        {
            Assert.That(SourceMovement.WishSpeed(Forward(), false, T), Is.EqualTo(3.6f).Within(1e-5f));
            Assert.That(SourceMovement.WishSpeed(Forward(sprint: true), false, T), Is.EqualTo(6.1f).Within(1e-5f));
            var walk = Forward(); walk.Walk = true;
            Assert.That(SourceMovement.WishSpeed(walk, false, T), Is.EqualTo(2.9f).Within(1e-5f));
            Assert.That(SourceMovement.WishSpeed(Forward(), true, T), Is.EqualTo(3.6f * 0.333f).Within(1e-4f));
        }

        [Test]
        public void Friction_UsesStopSpeedBelowIt_AndNeverReverses()
        {
            var v = new Vector3(1.0f, 0f, 0f);
            SourceMovement.ApplyFriction(ref v, 0.02f, T);
            Assert.That(v.x, Is.EqualTo(1.0f - 1.9f * 4f * 0.02f).Within(1e-5f), "drop = stopspeed * friction * dt below stopspeed");
            v = new Vector3(0.05f, 3f, 0f);
            SourceMovement.ApplyFriction(ref v, 0.02f, T);
            Assert.That(v.x, Is.EqualTo(0f), "clamped at zero, no reversal");
            Assert.That(v.y, Is.EqualTo(3f), "friction is horizontal only");
        }

        [Test]
        public void Ground_ReachesRunSpeed_WithoutOvershoot([ValueSource(nameof(Rates))] float hz)
        {
            float dt = 1f / hz;
            var s = new MoveState { Grounded = true };
            float t = 0f, reached = -1f, max = 0f;
            while (t < 1f)
            {
                SourceMovement.Tick(ref s, Forward(), T, dt, out _);
                t += dt;
                float sp = HSpeed(s.Velocity);
                max = Mathf.Max(max, sp);
                if (reached < 0f && sp >= 0.99f * T.runSpeed) reached = t;
            }
            Assert.That(max, Is.LessThanOrEqualTo(T.runSpeed + 1e-4f));
            Assert.That(reached, Is.InRange(0.05f, 0.25f), $"time to 99 % of run at {hz} Hz");
        }

        [Test]
        public void Ground_StopsFromRun_WithinHalfSecond([ValueSource(nameof(Rates))] float hz)
        {
            float dt = 1f / hz;
            var s = new MoveState { Grounded = true, Velocity = new Vector3(0f, 0f, T.runSpeed) };
            float t = 0f;
            while (HSpeed(s.Velocity) > 0f && t < 2f)
            {
                SourceMovement.Tick(ref s, default, T, dt, out _);
                t += dt;
                Assert.That(s.Velocity.z, Is.GreaterThanOrEqualTo(0f));
            }
            Assert.That(t, Is.InRange(0.3f, 0.5f), $"stop time at {hz} Hz");
        }

        [Test]
        public void JumpApex_IsHalfMetre_AtAnyRate([ValueSource(nameof(Rates))] float hz)
        {
            float dt = 1f / hz;
            var s = new MoveState { Grounded = true };
            float y = 0f, apex = 0f;
            Vector3 d = SourceMovement.Tick(ref s, new MoveInput { Jump = true }, T, dt, out bool jumped);
            Assert.That(jumped, Is.True);
            y += d.y;
            for (int i = 0; i < 1000 && y > 0f; i++)
            {
                apex = Mathf.Max(apex, y);
                y += SourceMovement.Tick(ref s, new MoveInput { Jump = true }, T, dt, out _).y;
            }
            Assert.That(apex, Is.EqualTo(T.jumpHeight).Within(0.005f), $"apex at {hz} Hz");
        }

        [Test]
        public void Jump_NeedsANewPress()
        {
            var s = new MoveState { Grounded = true };
            SourceMovement.Tick(ref s, new MoveInput { Jump = true }, T, 0.02f, out bool first);
            s.Grounded = true; s.Velocity = Vector3.zero;        // landed, jump still held
            SourceMovement.Tick(ref s, new MoveInput { Jump = true }, T, 0.02f, out bool held);
            SourceMovement.Tick(ref s, new MoveInput(), T, 0.02f, out _);
            SourceMovement.Tick(ref s, new MoveInput { Jump = true }, T, 0.02f, out bool again);
            Assert.That(first, Is.True);
            Assert.That(held, Is.False, "holding jump must not bounce");
            Assert.That(again, Is.True);
        }

        [Test]
        public void Jump_SkipsFriction_AndClampsToBhopCap()
        {
            var fast = new MoveState { Grounded = true, Velocity = new Vector3(0f, 0f, 6.1f) };
            SourceMovement.Tick(ref fast, new MoveInput { Jump = true }, T, 0.02f, out _);
            Assert.That(HSpeed(fast.Velocity), Is.EqualTo(1.1f * 3.6f).Within(1e-3f), "clamped to 1.1 x run");

            var slow = new MoveState { Grounded = true, Velocity = new Vector3(0f, 0f, 3.0f) };
            SourceMovement.Tick(ref slow, new MoveInput { Jump = true }, T, 0.02f, out _);
            Assert.That(HSpeed(slow.Velocity), Is.EqualTo(3.0f).Within(1e-4f), "below the cap: no friction on the jump tick");
        }

        [Test]
        public void Bhop_RepeatedJumps_NeverExceedCap()
        {
            // air-strafe between hops, jump again on each landing: speed at take-off stays at or below the cap
            var s = new MoveState { Grounded = true, Velocity = new Vector3(0f, 0f, 3.6f) };
            float dt = 1f / 60f, yaw = 0f, y = 0f;
            for (int hop = 0; hop < 20; hop++)
            {
                s.JumpHeld = false;
                y += SourceMovement.Tick(ref s, new MoveInput { Jump = true, YawDeg = yaw }, T, dt, out bool jumped).y;
                Assert.That(jumped, Is.True);
                Assert.That(HSpeed(s.Velocity), Is.LessThanOrEqualTo(T.BhopCap + 1e-3f), $"take-off speed, hop {hop}");
                for (int i = 0; i < 200 && y > 0f; i++)
                {
                    yaw += 3f; // turn with the strafe: classic air strafe
                    y += SourceMovement.Tick(ref s, new MoveInput { Move = new Vector2(1f, 0f), YawDeg = yaw }, T, dt, out _).y;
                }
                y = 0f;
                s.Grounded = true;
                s.Velocity.y = 0f;
            }
        }

        [Test]
        public void Air_WishSpeedAlongWishDir_IsCapped()
        {
            var v = Vector3.zero;
            for (int i = 0; i < 100; i++) SourceMovement.AirAccelerate(ref v, Vector3.forward, 3.6f, T, 0.02f);
            Assert.That(v.z, Is.EqualTo(T.airWishSpeedCap).Within(1e-4f));

            // strafing across a fast velocity adds up to the cap sideways: the classic air-strafe gain
            v = new Vector3(0f, 0f, 3.6f);
            SourceMovement.AirAccelerate(ref v, Vector3.right, 3.6f, T, 0.02f);
            Assert.That(v.x, Is.EqualTo(0.57f).Within(1e-4f));
            Assert.That(v.magnitude, Is.GreaterThan(3.6f));
        }

        [Test]
        public void Air_GravityAndNoFriction()
        {
            var s = new MoveState { Grounded = false, Velocity = new Vector3(0f, 0f, 3f) };
            SourceMovement.Tick(ref s, default, T, 0.1f, out _);
            Assert.That(s.Velocity.z, Is.EqualTo(3f).Within(1e-5f), "no friction in the air");
            Assert.That(s.Velocity.y, Is.EqualTo(-0.981f).Within(1e-4f));
        }
    }
}
