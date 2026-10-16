using NUnit.Framework;
using Snow.Cargo;
using UnityEngine;

namespace Snow.Tests
{
    /// <summary>GRAB-01: a clamped spring-damper per player at his own grab point.</summary>
    public class GrabMathTests
    {
        [Test]
        public void Force_PullsTowardsTarget_AndIsClampedPerPlayer()
        {
            Vector3 f = GrabMath.HandForce(Vector3.zero, Vector3.zero, new Vector3(0f, 0.05f, 0f), Vector3.zero);
            Assert.That(f.y, Is.EqualTo(GrabMath.Stiffness * 0.05f).Within(1e-3f), "linear inside the budget");
            Vector3 big = GrabMath.HandForce(Vector3.zero, Vector3.zero, new Vector3(0f, 5f, 0f), Vector3.zero);
            Assert.That(big.magnitude, Is.EqualTo(GrabMath.MaxForcePerPlayer).Within(1e-3f), "never more than one player's budget");
        }

        [Test]
        public void Damping_ActsOnRelativeVelocity()
        {
            // the item moves with the hand (riding a vehicle): no damping force
            Vector3 v = new Vector3(10f, 0f, 0f);
            Vector3 f = GrabMath.HandForce(Vector3.zero, v, Vector3.zero, v);
            Assert.That(f.magnitude, Is.LessThan(1e-4f));
            // the item swings past a still hand: the force opposes the swing
            f = GrabMath.HandForce(Vector3.zero, new Vector3(1f, 0f, 0f), Vector3.zero, Vector3.zero);
            Assert.That(f.x, Is.LessThan(0f));
        }

        [Test]
        public void HeavyItem_OneHandCannotLift_TwoCan()
        {
            // AT-08 numbers: 60 kg weighs 588.6 N; budget 400 N per player
            float weight = 60f * 9.81f;
            Assert.That(GrabMath.MaxForcePerPlayer, Is.LessThan(weight), "one player alone cannot hold it up");
            Assert.That(2f * GrabMath.MaxForcePerPlayer, Is.GreaterThan(weight), "two players together can");
        }

        [Test]
        public void Target_ClampedToReach()
        {
            Vector3 eye = Vector3.zero;
            Vector3 far = GrabMath.ClampTarget(eye, new Vector3(0f, 0f, 50f));
            Assert.That(far.magnitude, Is.LessThanOrEqualTo(GrabMath.MaxHold + 0.5f + 1e-4f));
            Vector3 near = GrabMath.ClampTarget(eye, new Vector3(0f, 0f, 0.1f));
            Assert.That(near.magnitude, Is.EqualTo(GrabMath.MinHold).Within(1e-4f));
        }
    }
}
