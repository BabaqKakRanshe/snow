using UnityEngine;

namespace Snow.Cargo
{
    /// <summary>
    /// GRAB-01: one player's hand is a spring-damper between the grab point on the item and the hand target, with a
    /// force budget per player. Several players add their own clamped forces at their own points; physics computes the
    /// result and the rotation. No torque is applied directly, so orientation cannot create an unbounded moment.
    /// All values TEMPORARY (docs/dev/Tuning/Cargo.md).
    /// </summary>
    public static class GrabMath
    {
        /// <summary>N/m, N·s/m, N.</summary>
        public const float Stiffness = 2000f, Damping = 150f, MaxForcePerPlayer = 400f;
        /// <summary>m: how far from his eye a player can hold the grab point.</summary>
        public const float MinHold = 0.4f, MaxHold = 2.0f;

        /// <summary>Force on the item at the grab point. Velocities are world-space; damping acts on the relative velocity.</summary>
        public static Vector3 HandForce(Vector3 point, Vector3 pointVelocity, Vector3 target, Vector3 targetVelocity,
            float stiffness = Stiffness, float damping = Damping, float maxForce = MaxForcePerPlayer)
        {
            Vector3 f = stiffness * (target - point) - damping * (pointVelocity - targetVelocity);
            return Vector3.ClampMagnitude(f, maxForce);
        }

        /// <summary>Keep a hand target within the player's reach (server-side sanity check of client targets).</summary>
        public static Vector3 ClampTarget(Vector3 eye, Vector3 target)
        {
            Vector3 d = target - eye;
            float m = d.magnitude;
            if (m < 1e-4f) return eye;
            return eye + d / m * Mathf.Clamp(m, MinHold, MaxHold + 0.5f);
        }
    }
}
