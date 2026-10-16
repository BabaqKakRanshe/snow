using System;
using UnityEngine;

namespace Snow.Character
{
    /// <summary>
    /// Character movement numbers. All values are TEMPORARY (PLANE_PROMPT_UNITY.md §7.2), SI units.
    /// The table with sources and tuning results is docs/dev/Tuning/Character.md.
    /// </summary>
    [Serializable]
    public class MovementTuning
    {
        [Header("Speeds, m/s")]
        public float walkSpeed = 2.9f;
        public float runSpeed = 3.6f;
        public float sprintSpeed = 6.1f;
        [Tooltip("Crouched speed = factor x current speed. Not in §7.2: HL1 PLAYER_DUCKING_MULTIPLIER 0.333")]
        public float crouchSpeedFactor = 0.333f;

        [Header("Ground, GoldSrc PM_Friction / PM_Accelerate")]
        [Tooltip("1/s")] public float friction = 4.0f;
        [Tooltip("m/s")] public float stopSpeed = 1.9f;
        [Tooltip("1/s")] public float accelerate = 10f;

        [Header("Air, GoldSrc PM_AirAccelerate")]
        [Tooltip("1/s")] public float airAccelerate = 10f;
        [Tooltip("m/s: wish speed along the wish direction is capped in the air")] public float airWishSpeedCap = 0.57f;

        [Header("Vertical")]
        [Tooltip("m/s^2, real gravity: cargo is physical")] public float gravity = 9.81f;
        [Tooltip("m, apex of a standing jump")] public float jumpHeight = 0.5f;
        [Tooltip("Horizontal speed at the moment of a jump is clamped to factor x runSpeed")] public float bhopCapFactor = 1.1f;

        [Header("Capsule, m")]
        public float standHeight = 1.80f;
        public float crouchHeight = 0.90f;
        public float radius = 0.34f;
        public float eyeHeight = 1.65f;
        [Tooltip("Not in §7.2: stand eye is 0.15 m under the top, the same offset crouched")] public float crouchEyeHeight = 0.75f;
        [Tooltip("How far the feet rise when crouching in the air. §7.2: crouch-jump gives +0.35 m")] public float airCrouchFeetRaise = 0.35f;
        [Tooltip("s, eye height transition, visual only")] public float eyeTransitionTime = 0.2f;

        [Header("Ground contact")]
        public float stepOffset = 0.35f;
        [Tooltip("deg")] public float slopeLimit = 45f;
        [Tooltip("m, own sphere-cast below the capsule (CharacterController.isGrounded is not used)")] public float groundProbeDistance = 0.08f;

        public float BhopCap => bhopCapFactor * runSpeed;
        public float JumpSpeed => Mathf.Sqrt(2f * gravity * jumpHeight);
        public float MinGroundNormalY => Mathf.Cos(slopeLimit * Mathf.Deg2Rad);
    }
}
