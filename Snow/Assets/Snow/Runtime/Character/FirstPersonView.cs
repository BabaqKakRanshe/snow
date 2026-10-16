using UnityEngine;

namespace Snow.Character
{
    /// <summary>
    /// First-person camera (§7.3, §7.5): raw mouse 1:1, horizontal FOV, render interpolation between fixed simulation
    /// steps, optional head bob, landing dip. In a seat the eye sits on the seat's eye socket and the head turns within
    /// limits. Local owner only.
    /// </summary>
    public class FirstPersonView : MonoBehaviour
    {
        public HLCharacterMotor motor;
        public Camera cam;

        [Header("Look (TEMPORARY, O-01)")]
        [Tooltip("Degrees per mouse count; no acceleration")] public float sensitivity = 0.07f;
        public bool invertY;
        [Tooltip("Horizontal field of view, degrees (HL convention)")] public float horizontalFov = 90f;
        [Tooltip("Horizontal field of view while zooming (held right mouse button), degrees")] public float zoomFov = 35f;
        [Tooltip("s to zoom in or out")] public float zoomTime = 0.15f;

        /// <summary>Set every frame by the player controller: the zoom key is held.</summary>
        public bool ZoomHeld { get; set; }
        float _fov = -1f;

        [Header("Feel (TEMPORARY)")]
        public bool headBob = true;
        [Tooltip("m")] public float bobAmplitude = 0.012f;
        [Tooltip("m of travel per bob cycle")] public float bobStride = 1.6f;
        [Tooltip("m, largest landing dip")] public float landingDipMax = 0.08f;
        [Tooltip("m per m/s of fall speed above 2 m/s")] public float landingDipPerSpeed = 0.012f;

        [Header("Seat head limits, degrees (§7.5, TEMPORARY)")]
        public float seatYawLimit = 135f;
        public float seatPitchDown = 70f, seatPitchUp = 80f;

        float _yaw, _pitch, _bobPhase, _dip, _dipVel, _eyeOffset;
        Transform _seatEye;
        float _seatYaw, _seatPitch;

        /// <summary>Body yaw on foot, degrees.</summary>
        public float Yaw => _yaw;
        public bool Seated => _seatEye != null;

        /// <summary>Eye pose of the simulation (no smoothing, no bob): the use ray starts here. Works without a camera (bots).</summary>
        public Vector3 EyePosition => Seated ? _seatEye.position : motor != null ? motor.EyeWorld : transform.position;
        public Quaternion EyeRotation => Seated
            ? _seatEye.rotation * Quaternion.Euler(_seatPitch, _seatYaw, 0f)
            : Quaternion.Euler(_pitch, _yaw, 0f);

        /// <summary>Turn the head (seat) or the body and head (on foot) towards a world point. Used by bench bots.</summary>
        public void LookAt(Vector3 point)
        {
            Vector3 d = point - EyePosition;
            if (d.sqrMagnitude < 1e-6f) return;
            if (Seated)
            {
                Vector3 local = Quaternion.Inverse(_seatEye.rotation) * d;
                _seatYaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -seatYawLimit, seatYawLimit);
                _seatPitch = Mathf.Clamp(-Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg, -seatPitchUp, seatPitchDown);
            }
            else
            {
                _yaw = Mathf.Repeat(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 360f);
                _pitch = Mathf.Clamp(-Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg, -89f, 89f);
            }
        }

        public void SetYaw(float yawDeg) { _yaw = yawDeg; _pitch = 0f; }

        /// <summary>Sit: the eye follows the socket; head look starts straight ahead.</summary>
        public void EnterSeat(Transform eyeSocket)
        {
            _seatEye = eyeSocket;
            _seatYaw = 0f;
            _seatPitch = 0f;
        }

        public void LeaveSeat(float bodyYaw)
        {
            _seatEye = null;
            _yaw = bodyYaw;
            _pitch = 0f;
            _eyeOffset = 0f;
        }

        public void AddLook(Vector2 delta)
        {
            // zoomed in, the same mouse move turns the head less, so aiming at a small switch stays steady
            float k = sensitivity * (_fov > 0f ? _fov / horizontalFov : 1f);
            float dy = (invertY ? 1f : -1f) * delta.y * k;
            float dx = delta.x * k;
            if (Seated)
            {
                _seatYaw = Mathf.Clamp(_seatYaw + dx, -seatYawLimit, seatYawLimit);
                _seatPitch = Mathf.Clamp(_seatPitch + dy, -seatPitchUp, seatPitchDown);
            }
            else
            {
                _yaw = Mathf.Repeat(_yaw + dx, 360f);
                _pitch = Mathf.Clamp(_pitch + dy, -89f, 89f);
            }
        }

        void LateUpdate()
        {
            if (cam == null) return;
            if (_fov < 0f) _fov = horizontalFov;
            float target = ZoomHeld ? zoomFov : horizontalFov;
            _fov = Mathf.MoveTowards(_fov, target, Mathf.Abs(horizontalFov - zoomFov) / Mathf.Max(0.01f, zoomTime) * Time.deltaTime);
            if (cam.aspect > 0f)
                cam.fieldOfView = Camera.HorizontalToVerticalFieldOfView(_fov, cam.aspect);

            if (Seated)
            {
                cam.transform.SetPositionAndRotation(_seatEye.position,
                    _seatEye.rotation * Quaternion.Euler(_seatPitch, _seatYaw, 0f));
                return;
            }
            if (motor == null) return;

            float dt = Time.deltaTime;
            float fixedDt = Time.fixedDeltaTime;
            float alpha = fixedDt > 0f ? Mathf.Clamp01((Time.time - Time.fixedTime) / fixedDt) : 1f;
            Vector3 eye = Vector3.Lerp(motor.PrevEyeWorld, motor.EyeWorld, alpha);

            // crouch: absorb the stance jump and ease it out over eyeTransitionTime
            _eyeOffset -= motor.ConsumeStanceEyeJump();
            float rate = (motor.tuning.eyeHeight - motor.tuning.crouchEyeHeight) / Mathf.Max(0.01f, motor.tuning.eyeTransitionTime);
            _eyeOffset = Mathf.MoveTowards(_eyeOffset, 0f, rate * dt);

            // landing dip: a damped spring kicked by the fall speed
            if (motor.LastLandingSpeed > 2f)
            {
                _dipVel -= Mathf.Min(landingDipMax, landingDipPerSpeed * (motor.LastLandingSpeed - 2f)) * 12f;
            }
            motor.LastLandingSpeed = 0f;
            _dipVel += (-_dip * 150f - _dipVel * 20f) * dt;
            _dip = Mathf.Clamp(_dip + _dipVel * dt, -landingDipMax, 0.02f);

            float bob = 0f;
            Vector3 v = motor.Velocity;
            float hs = new Vector2(v.x, v.z).magnitude;
            if (headBob && motor.Grounded && hs > 0.3f)
            {
                _bobPhase += hs * dt / bobStride * 2f * Mathf.PI;
                bob = bobAmplitude * Mathf.Sin(_bobPhase) * Mathf.Clamp01(hs / motor.tuning.runSpeed);
            }

            cam.transform.SetPositionAndRotation(eye + Vector3.up * (_eyeOffset + _dip + bob),
                Quaternion.Euler(_pitch, _yaw, 0f));
        }
    }
}
