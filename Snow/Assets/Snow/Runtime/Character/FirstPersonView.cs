using System;
using UnityEngine;

namespace Snow.Character
{
    /// <summary>
    /// First-person camera (§7.3, §7.5; user answers 23.10, docs/gdd/QUESTIONS_5.md 25–41, numbers from
    /// docs/gdd/research/RESEARCH_CAMERA.md §4). Mouse 1:1 with optional smoothing; horizontal FOV; render interpolation
    /// between fixed simulation steps. On top of the look, small layers that add up: head bob (up-down and a half-speed
    /// sideways sway), roll when stepping sideways, landing dip and nod, breathing while standing (harder after a sprint),
    /// crouch spring, wider view at a sprint, deeper bob under a heavy load, shake (<see cref="CameraShake"/>). Bob is a
    /// move, not a turn: at 360 lines a turn over ~0.3° steps the whole frame. In a seat the eye sits on the seat's eye
    /// socket, the head turns within limits and lags on bumps; with the cursor on (Tab) the view narrows to read the panel.
    /// In a ragdoll the eye rides the mannequin's head, then eases back to the standing eye on getting up.
    /// Options in <see cref="CameraSettings"/>. Local owner only.
    /// </summary>
    public class FirstPersonView : MonoBehaviour
    {
        public HLCharacterMotor motor;
        public Camera cam;

        [Header("Look (options: CameraSettings)")]
        [Tooltip("Horizontal field of view while zooming (held right mouse button), degrees")] public float zoomFov = 35f;
        [Tooltip("s to zoom in or out")] public float zoomTime = 0.15f;

        /// <summary>Set every frame by the player controller: the zoom key is held.</summary>
        public bool ZoomHeld { get; set; }
        /// <summary>Set every frame by the player controller: the sprint key is held.</summary>
        public bool SprintHeld { get; set; }
        /// <summary>Set every frame by the player controller: the cursor is on in a seat (Tab).</summary>
        public bool PanelMode { get; set; }
        /// <summary>0..1, set by the hand every frame: how heavy the thing carried in front is.</summary>
        public float CarryLoad { get; set; }
        /// <summary>0..1: out of breath after a sprint (for the breathing sound).</summary>
        public float Exertion => _exertion;
        /// <summary>Any landing faster than 2 m/s, with the fall speed (the ragdoll falls on a hard one).</summary>
        public event Action<float> Landed;

        [Header("Feel (TEMPORARY, RESEARCH_CAMERA.md §4)")]
        [Tooltip("m of travel per bob cycle")] public float bobStride = 1.6f;
        [Tooltip("m: vertical bob at walk / run / sprint speed")] public float bobWalk = 0.012f, bobRun = 0.02f, bobSprint = 0.03f;
        [Tooltip("Sideways bob as a share of the vertical one, half as fast (a figure eight)")] public float bobSide = 0.5f;
        [Tooltip("deg of roll stepping sideways at run speed (Quake cl_rollangle 2)")] public float strafeRoll = 1.5f;
        [Tooltip("s, halflife of the roll")] public float rollHalflife = 0.1f;
        [Tooltip("m, largest landing dip")] public float landingDipMax = 0.08f;
        [Tooltip("m per m/s of fall speed above 2 m/s")] public float landingDipPerSpeed = 0.012f;
        [Tooltip("deg of nod at the largest dip")] public float landingNod = 1.5f;
        [Tooltip("m and s: calm breathing while standing")] public float breathCalm = 0.003f, breathCalmPeriod = 4f;
        [Tooltip("m and s: breathing out of breath")] public float breathHard = 0.008f, breathHardPeriod = 2f;
        [Tooltip("s of sprint to be fully out of breath / s to calm down")] public float exertionRise = 6f, exertionFall = 5f;
        [Tooltip("s, halflife of the eye height when crouching")] public float crouchHalflife = 0.06f;
        [Tooltip("s, halflife of the field of view (sprint, panel)")] public float fovHalflife = 0.15f;
        [Tooltip("Full heavy load: bob x, stride x, eye lower by m, nod down by deg")] public float loadBob = 1.3f, loadStride = 1.25f, loadDrop = 0.02f, loadNod = 1f;

        [Header("Seat head limits, degrees (§7.5, TEMPORARY)")]
        public float seatYawLimit = 135f;
        public float seatPitchDown = 70f, seatPitchUp = 80f;
        [Tooltip("m of head lag per m/s² of seat acceleration / largest lag, m")] public float carHeadPerAcc = 0.0012f, carHeadMax = 0.02f;
        [Tooltip("s, halflife of the head lag")] public float carHeadHalflife = 0.08f;

        [Header("Shake (CameraShake, TEMPORARY)")]
        [Tooltip("m/s landing speed where the shake starts / is strongest")] public float landingShakeFrom = 5f, landingShakeFull = 12f;
        [Tooltip("m/s² of seat acceleration above which a vehicle shakes the camera")] public float joltFrom = 12f;
        [Tooltip("Trauma per (m/s² over joltFrom) per second")] public float joltShake = 0.06f;

        [Header("Ragdoll (TEMPORARY)")]
        [Tooltip("Head look limits in a ragdoll, degrees")] public float ragdollYaw = 70f, ragdollPitch = 50f;
        [Tooltip("Eye in the mannequin's head, m (head space)")] public Vector3 ragdollEye = new Vector3(0f, 0.02f, 0.06f);
        [Tooltip("s: the eye eases from the fallen head to the standing eye")] public float getUpTime = 0.45f;

        // look: targets take the mouse at once, the shown values follow them (mouse smoothing)
        float _yaw, _pitch, _yawT, _pitchT;
        float _seatYaw, _seatPitch, _seatYawT, _seatPitchT;
        float _ragYaw, _ragPitch;
        float _fov = -1f, _fovBase = -1f, _fovSprint;
        float _bobPhase, _breathPhase, _exertion, _roll, _dip, _dipVel, _eyeOffset;
        Vector3 _seatPrevPos, _seatVel, _seatAcc, _carHead;
        float _seatWarm;
        bool _seatJoltReset = true;
        Transform _seatEye, _ragHead;
        float _getUp;
        Vector3 _getUpPos;
        Quaternion _getUpRot;

        /// <summary>Body yaw on foot, degrees.</summary>
        public float Yaw => Mathf.Repeat(_yaw, 360f);
        /// <summary>Head pitch, degrees, + = down: on foot, or within the seat.</summary>
        public float Pitch => Seated ? _seatPitch : _pitch;
        /// <summary>Head yaw within the seat, degrees (0 on foot).</summary>
        public float SeatYaw => Seated ? _seatYaw : 0f;
        public bool Seated => _seatEye != null;
        public bool Ragdolled => _ragHead != null;

        /// <summary>Eye pose of the simulation (no smoothing, no bob): the use ray starts here. Works without a camera (bots).</summary>
        public Vector3 EyePosition => Ragdolled ? _ragHead.position : Seated ? _seatEye.position : motor != null ? motor.EyeWorld : transform.position;
        public Quaternion EyeRotation => Ragdolled ? _ragHead.rotation
            : Seated ? _seatEye.rotation * Quaternion.Euler(_seatPitch, _seatYaw, 0f)
            : Quaternion.Euler(_pitch, _yaw, 0f);

        /// <summary>Turn the head (seat) or the body and head (on foot) towards a world point. Used by bench bots.</summary>
        public void LookAt(Vector3 point)
        {
            Vector3 d = point - EyePosition;
            if (d.sqrMagnitude < 1e-6f) return;
            if (Seated)
            {
                Vector3 local = Quaternion.Inverse(_seatEye.rotation) * d;
                _seatYaw = _seatYawT = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -seatYawLimit, seatYawLimit);
                _seatPitch = _seatPitchT = Mathf.Clamp(-Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg, -seatPitchUp, seatPitchDown);
            }
            else
            {
                _yaw = _yawT = Mathf.Repeat(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 360f);
                _pitch = _pitchT = Mathf.Clamp(-Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg, -89f, 89f);
            }
        }

        public void SetYaw(float yawDeg) { _yaw = _yawT = yawDeg; _pitch = _pitchT = 0f; }

        /// <summary>Sit: the eye follows the socket; head look starts straight ahead.</summary>
        public void EnterSeat(Transform eyeSocket)
        {
            _seatEye = eyeSocket;
            _seatYaw = _seatYawT = 0f;
            _seatPitch = _seatPitchT = 0f;
            _carHead = Vector3.zero;
        }

        public void LeaveSeat(float bodyYaw)
        {
            _seatEye = null;
            SetYaw(bodyYaw);
            _eyeOffset = 0f;
        }

        /// <summary>The eye rides this head (the ragdoll's) until <see cref="EndRagdoll"/>.</summary>
        public void BeginRagdoll(Transform head)
        {
            _ragHead = head;
            _ragYaw = _ragPitch = 0f;
            _getUp = 0f;
        }

        /// <summary>Back on foot facing bodyYaw: the eye eases from where the head lay to the standing eye.</summary>
        public void EndRagdoll(float bodyYaw)
        {
            if (cam != null)
            {
                _getUpPos = cam.transform.position;
                _getUpRot = cam.transform.rotation;
                _getUp = 1f;
            }
            _ragHead = null;
            SetYaw(bodyYaw);
            _eyeOffset = 0f;
            _dip = _dipVel = 0f;
            _roll = 0f;
        }

        public void AddLook(Vector2 delta)
        {
            // zoomed in, the same mouse move turns the head less, so aiming at a small switch stays steady
            float baseFov = CameraSettings.Fov;
            float k = CameraSettings.Sensitivity * (_fov > 0f ? _fov / baseFov : 1f);
            float dy = (CameraSettings.InvertY ? 1f : -1f) * delta.y * k;
            float dx = delta.x * k;
            if (Ragdolled)
            {
                _ragYaw = Mathf.Clamp(_ragYaw + dx, -ragdollYaw, ragdollYaw);
                _ragPitch = Mathf.Clamp(_ragPitch + dy, -ragdollPitch, ragdollPitch);
            }
            else if (Seated)
            {
                _seatYawT = Mathf.Clamp(_seatYawT + dx, -seatYawLimit, seatYawLimit);
                _seatPitchT = Mathf.Clamp(_seatPitchT + dy, -seatPitchUp, seatPitchDown);
            }
            else
            {
                _yawT += dx;
                _pitchT = Mathf.Clamp(_pitchT + dy, -89f, 89f);
            }
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;

            // mouse smoothing: the shown look follows the mouse with a halflife (0 = raw). Before the camera check: Yaw,
            // EyeRotation and the motor's yaw come from here, also for a view without a camera (a bot: raw)
            float smooth = cam != null ? CameraSettings.MouseSmoothing * 0.06f : 0f;
            float k = smooth > 1e-4f ? 1f - Mathf.Pow(2f, -dt / smooth) : 1f;
            if (_yawT > 720f || _yawT < -720f) { float wrap = Mathf.Floor(_yawT / 360f) * 360f; _yawT -= wrap; _yaw -= wrap; }
            _yaw += (_yawT - _yaw) * k;
            _pitch += (_pitchT - _pitch) * k;
            _seatYaw += (_seatYawT - _seatYaw) * k;
            _seatPitch += (_seatPitchT - _seatPitch) * k;

            if (cam == null) return;
            float motion = CameraSettings.Motion;

            float hs = 0f;
            bool onFoot = !Seated && !Ragdolled && motor != null;
            if (onFoot) hs = new Vector2(motor.Velocity.x, motor.Velocity.z).magnitude;
            bool sprinting = onFoot && SprintHeld && motor.Grounded && hs > motor.tuning.runSpeed + 0.3f;
            _exertion = Mathf.Clamp01(_exertion + (sprinting ? dt / exertionRise : -dt / exertionFall));

            // field of view: the option, narrower at a panel, wider at a sprint, zoom over everything
            float wantBase = PanelMode ? CameraSettings.PanelFov : CameraSettings.Fov;
            if (_fovBase < 0f) _fovBase = wantBase;
            _fovBase = Damp(_fovBase, wantBase, fovHalflife, dt);
            _fovSprint = Damp(_fovSprint, sprinting ? CameraSettings.SprintFov : 0f, fovHalflife, dt);
            float wantFov = ZoomHeld ? zoomFov : _fovBase + _fovSprint;
            if (_fov < 0f) _fov = wantFov;
            _fov = Damp(_fov, wantFov, zoomTime / 3f, dt);
            if (cam.aspect > 0f)
                cam.fieldOfView = Camera.HorizontalToVerticalFieldOfView(_fov, cam.aspect);

            Quaternion shake = CameraShake.Step();
            Vector3 pos;
            Quaternion rot;
            if (Ragdolled)
            {
                _seatJoltReset = true;
                Quaternion head = _ragHead.rotation;
                cam.transform.SetPositionAndRotation(_ragHead.position + head * ragdollEye,
                    head * Quaternion.Euler(_ragPitch, _ragYaw, 0f) * shake);
                return;
            }
            if (Seated)
            {
                SeatJolt();
                // the head lags behind the body on bumps and braking: opposite to the seat's acceleration
                Vector3 lag = -Vector3.ClampMagnitude(_seatAcc * carHeadPerAcc, carHeadMax) * (CameraSettings.CarHead * motion);
                _carHead = Vector3.Lerp(_carHead, lag, 1f - Mathf.Pow(2f, -dt / carHeadHalflife));
                pos = _seatEye.position + _carHead;
                rot = _seatEye.rotation * Quaternion.Euler(_seatPitch, _seatYaw, 0f) * shake;
            }
            else
            {
                _seatJoltReset = true;
                if (motor == null) return;
                OnFoot(dt, hs, motion, shake, out pos, out rot);
            }

            // getting up from a ragdoll: from where the head lay to the standing eye
            if (_getUp > 0f)
            {
                _getUp = Mathf.Max(0f, _getUp - dt / Mathf.Max(0.01f, getUpTime));
                float e = Mathf.SmoothStep(0f, 1f, 1f - _getUp);
                pos = Vector3.Lerp(_getUpPos, pos, e);
                rot = Quaternion.Slerp(_getUpRot, rot, e);
            }
            cam.transform.SetPositionAndRotation(pos, rot);
        }

        void OnFoot(float dt, float hs, float motion, Quaternion shake, out Vector3 pos, out Quaternion rot)
        {
            float fixedDt = Time.fixedDeltaTime;
            float alpha = fixedDt > 0f ? Mathf.Clamp01((Time.time - Time.fixedTime) / fixedDt) : 1f;
            Vector3 eye = Vector3.Lerp(motor.PrevEyeWorld, motor.EyeWorld, alpha);
            MovementTuning t = motor.tuning;

            // crouch: absorb the stance jump and spring it out
            _eyeOffset -= motor.ConsumeStanceEyeJump();
            _eyeOffset = Damp(_eyeOffset, 0f, crouchHalflife, dt);

            // landing dip: a damped spring kicked by the fall speed
            if (motor.LastLandingSpeed > 2f)
            {
                float speed = motor.LastLandingSpeed;
                _dipVel -= Mathf.Min(landingDipMax, landingDipPerSpeed * (speed - 2f)) * 12f;
                CameraShake.Add(Mathf.InverseLerp(landingShakeFrom, landingShakeFull, speed) * 0.6f);
                motor.LastLandingSpeed = 0f;
                Landed?.Invoke(speed);
                if (Ragdolled) { pos = cam.transform.position; rot = cam.transform.rotation; return; }
            }
            motor.LastLandingSpeed = 0f;
            _dipVel += (-_dip * 150f - _dipVel * 20f) * dt;
            _dip = Mathf.Clamp(_dip + _dipVel * dt, -landingDipMax, 0.02f);

            float load = Mathf.Clamp01(CarryLoad);
            float bobK = CameraSettings.Bob * motion;

            // head bob from the distance walked (not time), a sideways sway at half the rate
            float bobY = 0f, bobX = 0f;
            if (motor.Grounded && hs > 0.3f)
            {
                _bobPhase += hs * dt / (bobStride * Mathf.Lerp(1f, loadStride, load)) * 2f * Mathf.PI;
                float amp = BobAmplitude(hs, t) * Mathf.Lerp(1f, loadBob, load) * bobK;
                bobY = amp * Mathf.Sin(_bobPhase);
                bobX = amp * bobSide * Mathf.Sin(_bobPhase * 0.5f);
            }

            // breathing: barely there standing calm, deeper and faster after a sprint, mostly hidden by walking
            _breathPhase += dt / Mathf.Lerp(breathCalmPeriod, breathHardPeriod, _exertion) * 2f * Mathf.PI;
            if (_breathPhase > 2f * Mathf.PI) _breathPhase -= 2f * Mathf.PI;
            float breath = Mathf.Lerp(breathCalm, breathHard, _exertion) * (1f - 0.7f * Mathf.Clamp01(hs / t.runSpeed)) * bobK;
            float breathY = breath * Mathf.Sin(_breathPhase);

            // roll into a sideways step
            Quaternion body = Quaternion.Euler(0f, _yaw, 0f);
            float side = Vector3.Dot(motor.Velocity, body * Vector3.right);
            float rollTarget = -Mathf.Clamp(side / t.runSpeed, -1.5f, 1.5f) * strafeRoll * CameraSettings.Tilt * motion;
            _roll = Damp(_roll, rollTarget, rollHalflife, dt);

            float nod = (-_dip / landingDipMax * landingNod + load * loadNod) * motion;
            pos = eye + Vector3.up * (_eyeOffset + _dip * motion + bobY + breathY - load * loadDrop * motion) + body * Vector3.right * bobX;
            rot = Quaternion.Euler(_pitch + nod, _yaw, _roll) * shake;
        }

        // walk / run / sprint amplitudes joined by straight lines
        float BobAmplitude(float hs, MovementTuning t)
        {
            if (hs <= t.walkSpeed) return bobWalk * hs / t.walkSpeed;
            if (hs <= t.runSpeed) return Mathf.Lerp(bobWalk, bobRun, Mathf.InverseLerp(t.walkSpeed, t.runSpeed, hs));
            return Mathf.Lerp(bobRun, bobSprint, Mathf.InverseLerp(t.runSpeed, t.sprintSpeed, hs));
        }

        // exponential approach with a halflife (Holden, "Spring-It-On"): frame-rate independent
        static float Damp(float x, float target, float halflife, float dt) =>
            halflife <= 1e-4f ? target : Mathf.Lerp(x, target, 1f - Mathf.Pow(2f, -dt / halflife));

        // jolts of a vehicle seat (bumps, a crash, turbulence): the eye socket's acceleration over joltFrom shakes the camera.
        // Velocity and acceleration are smoothed first: a client sees the vehicle interpolated, frame steps are uneven
        void SeatJolt()
        {
            float dt = Time.deltaTime;
            Vector3 p = _seatEye.position;
            if (_seatJoltReset || dt <= 0f || (p - _seatPrevPos).sqrMagnitude > 25f)   // just seated or teleported
            {
                _seatPrevPos = p;
                _seatVel = Vector3.zero;
                _seatAcc = Vector3.zero;
                _seatJoltReset = false;
                _seatWarm = 0.3f;
                return;
            }
            Vector3 v = (p - _seatPrevPos) / dt;
            _seatPrevPos = p;
            float k = 1f - Mathf.Exp(-dt / 0.05f);
            Vector3 prev = _seatVel;
            _seatVel += (v - _seatVel) * k;
            _seatAcc += ((_seatVel - prev) / dt - _seatAcc) * k;
            if (_seatWarm > 0f) { _seatWarm -= dt; _seatAcc = Vector3.zero; return; }   // the filters settle after a reset
            float over = _seatAcc.magnitude - joltFrom;
            if (over > 0f) CameraShake.Add(over * joltShake * dt);
        }
    }
}
