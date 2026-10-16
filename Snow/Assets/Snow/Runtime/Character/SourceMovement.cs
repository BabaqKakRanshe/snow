using UnityEngine;

namespace Snow.Character
{
    /// <summary>One simulation tick of player intent. Move: x = strafe right, y = forward, each -1..1.</summary>
    public struct MoveInput
    {
        public Vector2 Move;
        public bool Jump;
        public bool Crouch;
        public bool Sprint;
        public bool Walk;
        /// <summary>Body yaw, degrees, Unity convention (0 = +Z, 90 = +X).</summary>
        public float YawDeg;
    }

    /// <summary>Simulation state that survives between ticks. Velocity in m/s, world space.</summary>
    public struct MoveState
    {
        public Vector3 Velocity;
        public bool Grounded;
        public bool Crouched;
        /// <summary>Jump was held on the previous tick. A new jump needs a new press (GoldSrc oldbuttons).</summary>
        public bool JumpHeld;
    }

    /// <summary>
    /// GoldSrc-style player movement without collision: friction, ground and air acceleration, jump, gravity.
    /// Pure C#: the caller moves the capsule by the returned displacement and reports ground contact back.
    /// </summary>
    public static class SourceMovement
    {
        /// <summary>Horizontal unit wish direction from the input axes and yaw; zero when there is no input.</summary>
        public static Vector3 WishDirection(in MoveInput input)
        {
            Vector2 m = Vector2.ClampMagnitude(input.Move, 1f);
            if (m.sqrMagnitude < 1e-6f) return Vector3.zero;
            float yaw = input.YawDeg * Mathf.Deg2Rad;
            var forward = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            var right = new Vector3(forward.z, 0f, -forward.x);
            return (forward * m.y + right * m.x).normalized;
        }

        /// <summary>Wish speed: stick deflection times the speed of the current gait.</summary>
        public static float WishSpeed(in MoveInput input, bool crouched, MovementTuning t)
        {
            float gait = input.Sprint ? t.sprintSpeed : input.Walk ? t.walkSpeed : t.runSpeed;
            if (crouched) gait *= t.crouchSpeedFactor;
            return gait * Mathf.Min(1f, input.Move.magnitude);
        }

        /// <summary>PM_Friction: drop = max(speed, stopspeed) * friction * dt, horizontal only.</summary>
        public static void ApplyFriction(ref Vector3 v, float dt, MovementTuning t)
        {
            var h = new Vector3(v.x, 0f, v.z);
            float speed = h.magnitude;
            if (speed < 1e-4f) { v.x = 0f; v.z = 0f; return; }
            float drop = Mathf.Max(speed, t.stopSpeed) * t.friction * dt;
            float scale = Mathf.Max(speed - drop, 0f) / speed;
            v.x *= scale;
            v.z *= scale;
        }

        /// <summary>PM_Accelerate: add = wishspeed - dot(v, wishdir); v += wishdir * min(accel * wishspeed * dt, add).</summary>
        public static void Accelerate(ref Vector3 v, Vector3 wishDir, float wishSpeed, float accel, float dt)
        {
            float add = wishSpeed - Vector3.Dot(v, wishDir);
            if (add <= 0f) return;
            v += wishDir * Mathf.Min(accel * wishSpeed * dt, add);
        }

        /// <summary>
        /// PM_AirAccelerate: the speed gained along the wish direction is capped at airWishSpeedCap, while the per-tick
        /// step still scales with the full wish speed. This is what makes air strafing controllable.
        /// </summary>
        public static void AirAccelerate(ref Vector3 v, Vector3 wishDir, float wishSpeed, MovementTuning t, float dt)
        {
            float capped = Mathf.Min(wishSpeed, t.airWishSpeedCap);
            float add = capped - Vector3.Dot(v, wishDir);
            if (add <= 0f) return;
            v += wishDir * Mathf.Min(t.airAccelerate * wishSpeed * dt, add);
        }

        /// <summary>Clamp horizontal speed to the bunny-hop cap at the moment of a jump.</summary>
        public static void ClampBhop(ref Vector3 v, MovementTuning t)
        {
            var h = new Vector3(v.x, 0f, v.z);
            float speed = h.magnitude;
            float cap = t.BhopCap;
            if (speed <= cap) return;
            float k = cap / speed;
            v.x *= k;
            v.z *= k;
        }

        /// <summary>
        /// Advances the state by dt and returns the displacement for the capsule. The caller must set Grounded
        /// from its own ground probe after moving. Gravity uses a half step on each side of the move, so jump
        /// apex height does not depend on dt.
        /// </summary>
        public static Vector3 Tick(ref MoveState s, in MoveInput input, MovementTuning t, float dt, out bool jumped)
        {
            jumped = false;
            Vector3 wishDir = WishDirection(input);
            float wishSpeed = WishSpeed(input, s.Crouched, t);
            Vector3 v = s.Velocity;

            if (s.Grounded && input.Jump && !s.JumpHeld)
            {
                ClampBhop(ref v, t);
                v.y = t.JumpSpeed;
                s.Grounded = false;
                jumped = true;
            }
            s.JumpHeld = input.Jump;

            Vector3 displacement;
            if (s.Grounded)
            {
                v.y = 0f;
                ApplyFriction(ref v, dt, t);
                Accelerate(ref v, wishDir, wishSpeed, t.accelerate, dt);
                displacement = v * dt;
            }
            else
            {
                AirAccelerate(ref v, wishDir, wishSpeed, t, dt);
                float vy0 = v.y;
                v.y -= t.gravity * dt;
                displacement = new Vector3(v.x * dt, 0.5f * (vy0 + v.y) * dt, v.z * dt);
            }
            s.Velocity = v;
            return displacement;
        }
    }
}
