using System.Collections.Generic;
using UnityEngine;

namespace Snow.Character
{
    /// <summary>
    /// Half-Life style first-person motor (NEW-01, §7.1): kinematic CharacterController moved by SourceMovement.
    /// Simulates in FixedUpdate; ground contact comes from an own sphere cast, not CharacterController.isGrounded.
    /// Stepping is GoldSrc PM_StepMove (up, across, down), not the PhysX auto-step: with a 0.34 m radius every step
    /// below 0.36 m touches the round bottom, where PhysX auto-step never triggers.
    /// Only the local owner runs it; remote copies keep the CharacterController as a collider and are moved by the network.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class HLCharacterMotor : MonoBehaviour
    {
        public MovementTuning tuning = new MovementTuning();
        [Tooltip("Layers the capsule stands on and collides with in its own queries")]
        public LayerMask collisionMask = ~0;
        [Tooltip("m/s along a ladder (И3 H.3, Half-Life ladder, TEMPORARY)")] public float ladderSpeed = 1.6f;

        /// <summary>PhysX keeps the capsule this far off surfaces; the capsule centre is raised by it so the feet (transform) touch the ground.</summary>
        public const float SkinWidth = 0.02f;
        /// <summary>
        /// ENGINEERING: a capsule standing on the edge of a step sees a tilted contact normal although it stands on a
        /// flat top face (a GoldSrc box would). Edge contacts up to 55 deg count as ground when the face under the
        /// contact is walkable. Steeper edge contacts (e.g. hanging below a ledge top after a short jump) do not.
        /// </summary>
        public const float EdgeMinNormalY = 0.57f;

        CharacterController _cc;
        MoveState _state;
        bool _jumpQueued;
        readonly List<Vector3> _hitNormals = new List<Vector3>(8);
        readonly List<Vector3> _savedNormals = new List<Vector3>(8);
        readonly RaycastHit[] _castHits = new RaycastHit[16];
        readonly Collider[] _ladderHits = new Collider[8];
        readonly List<Collider> _ladderIgnored = new List<Collider>(16);
        Transform _ladder;
        Vector3 _prevEye, _currEye;
        float _stanceEyeJump;

        /// <summary>Latest intent; set by the controller every frame. Yaw is the body yaw in degrees.</summary>
        public MoveInput Input;
        public Vector3 Velocity => _state.Velocity;
        public bool Grounded => _state.Grounded;
        public bool Crouched => _state.Crouched;
        /// <summary>Downward speed at the last landing, m/s (for the camera dip). Reset by the reader.</summary>
        public float LastLandingSpeed { get; set; }
        /// <summary>Eye position at the previous and the latest simulation step, for render interpolation.</summary>
        public Vector3 PrevEyeWorld => _prevEye;
        public Vector3 EyeWorld => _currEye;
        public float EyeHeight => _state.Crouched ? tuning.crouchEyeHeight : tuning.eyeHeight;
        /// <summary>Sum of eye-height jumps caused by stance changes since the last read; the view smooths them out.</summary>
        public float ConsumeStanceEyeJump() { float j = _stanceEyeJump; _stanceEyeJump = 0f; return j; }
        public CharacterController Controller => _cc;
        /// <summary>What the capsule stands on (null in the air). Standing on a vehicle floor makes the player ride it (§7.6).</summary>
        public Collider GroundCollider { get; private set; }
        /// <summary>On a ladder (И3 H.3): the climb volume the capsule is in, null otherwise.</summary>
        public Transform Ladder => _ladder;
        /// <summary>Contact normals of the last Move (diagnostics and tests).</summary>
        public IReadOnlyList<Vector3> LastHitNormals => _hitNormals;

        /// <summary>What the last ground move did (diagnostics and tests).</summary>
        public struct WalkInfo { public float Plain, Stepped, Rise; public bool StepTried, StepKept, Landed; public string Why; }
        public WalkInfo LastWalk;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _cc.skinWidth = SkinWidth;
            _cc.stepOffset = 0f;                                  // own PM_StepMove instead
            _cc.slopeLimit = tuning.slopeLimit;
            _cc.minMoveDistance = 0f;
            ApplyCapsule(false);
            _currEye = _prevEye = transform.position + Vector3.up * EyeHeight;
        }

        /// <summary>A jump press between two simulation steps is not lost.</summary>
        public void QueueJump() => _jumpQueued = true;

        /// <summary>Place the capsule and set its velocity (seat exit, spawn). Keeps the stance standing.</summary>
        public void Teleport(Vector3 feet, float yawDeg, Vector3 velocity)
        {
            ApplyCapsule(false);
            _state = new MoveState { Velocity = velocity, Grounded = false };
            Place(feet);
            transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);
            _currEye = _prevEye = feet + Vector3.up * EyeHeight;
        }

        void FixedUpdate() => Simulate(Time.fixedDeltaTime);

        /// <summary>One simulation step. Public for tests, which drive it with a fixed dt.</summary>
        public void Simulate(float dt)
        {
            if (!_cc.enabled || dt <= 0f) return;
            _prevEye = _currEye;
            transform.rotation = Quaternion.Euler(0f, Input.YawDeg, 0f);

            UpdateStance(Input.Crouch);
            if (LadderMove(dt)) { _currEye = transform.position + Vector3.up * EyeHeight; return; }

            MoveInput input = Input;
            input.Jump |= _jumpQueued;
            _jumpQueued = false;
            bool wasGrounded = _state.Grounded;
            float fallSpeed = -_state.Velocity.y;

            Vector3 displacement = SourceMovement.Tick(ref _state, input, tuning, dt, out bool jumped);
            bool airborne = !_state.Grounded;

            if (airborne) AirMove(displacement);
            else WalkMove(displacement);
            ClipVelocity();

            // ground: stay on ground over steps and slopes going down (PM StayOnGround), else a short probe
            bool grounded = false;
            if (!jumped && _state.Velocity.y <= 0.01f)
            {
                float reach = wasGrounded ? tuning.stepOffset : tuning.groundProbeDistance;
                if (ProbeGround(reach, out float distance))
                {
                    grounded = true;
                    float snap = distance - SkinWidth;
                    if (wasGrounded && snap > 0.001f) _cc.Move(Vector3.down * snap);
                }
            }
            if (grounded && !wasGrounded) LastLandingSpeed = Mathf.Max(LastLandingSpeed, fallSpeed);
            if (grounded) _state.Velocity.y = 0f;
            else EscapeEdge();
            _state.Grounded = grounded;

            _currEye = transform.position + Vector3.up * EyeHeight;
        }

        void OnControllerColliderHit(ControllerColliderHit hit) => _hitNormals.Add(hit.normal);

        // ---------------------------------------------------------------- moves
        // PM_WalkMove + PM_StepMove: plain move; if it was blocked, also try up-across-down and keep the one that went further.
        void WalkMove(Vector3 displacement)
        {
            Vector3 start = transform.position;
            _hitNormals.Clear();
            _cc.Move(displacement);
            Vector3 plainEnd = transform.position;

            var wish = new Vector3(displacement.x, 0f, displacement.z);
            float want = wish.magnitude;
            float plain = Horizontal(plainEnd - start);
            LastWalk = new WalkInfo { Plain = plain };
            if (want < 1e-5f || plain >= want - 1e-4f) return;   // not blocked

            _savedNormals.Clear();
            _savedNormals.AddRange(_hitNormals);
            Place(start);
            _hitNormals.Clear();
            _cc.Move(Vector3.up * tuning.stepOffset);
            float lifted = transform.position.y - start.y;
            _cc.Move(wish);
            Vector3 across = transform.position;
            float stepped = Horizontal(across - start);
            float rise = 0f;
            bool kept = stepped > plain + 1e-4f && TryStandOnStep(start, lifted, out rise);
            LastWalk = new WalkInfo { Plain = plain, Stepped = stepped, Rise = rise, StepTried = true, StepKept = kept, Landed = kept, Why = _why };
            if (kept)
            {
                _hitNormals.Clear();                              // the wall of the step is behind us now: keep the speed
                return;
            }
            Place(plainEnd);
            _hitNormals.Clear();
            _hitNormals.AddRange(_savedNormals);
        }

        // After the up and across moves: find the walkable top face under the capsule and put the feet on it, as a GoldSrc
        // box would stand on the step while overhanging its edge. Dropping the round bottom onto the edge would leave the
        // capsule 5–15 cm lower on a steep contact instead.
        string _why;

        bool TryStandOnStep(Vector3 start, float lifted, out float rise)
        {
            rise = 0f;
            _why = "cast";
            float r = _cc.radius - 0.01f;
            Vector3 bottomSphere = transform.position + _cc.center + Vector3.down * (_cc.height * 0.5f - _cc.radius);
            if (!CastDown(bottomSphere, r, lifted + SkinWidth + 0.02f, out RaycastHit hit))
                return false;                                     // nothing under us within the step: no step
            float top = hit.point.y;
            if (hit.normal.y < tuning.MinGroundNormalY)
            {
                // an edge of the step ahead: its top face lies beyond the edge, away from the capsule axis
                Vector3 fromAxis = hit.point - bottomSphere;
                fromAxis.y = 0f;
                Vector3 origin = hit.point + (fromAxis.sqrMagnitude > 1e-6f ? fromAxis.normalized * 0.01f : Vector3.zero) + Vector3.up * 0.02f;
                _why = "face";
                if (!Physics.Raycast(origin, Vector3.down, out RaycastHit face, 0.05f, collisionMask, QueryTriggerInteraction.Ignore)
                    || face.normal.y < tuning.MinGroundNormalY)
                    return false;
                top = face.point.y;
            }
            rise = top - start.y;
            _why = "rise";
            if (rise < -0.01f || rise > tuning.stepOffset + 0.005f) return false;
            Vector3 feet = new Vector3(transform.position.x, Mathf.Max(top, start.y), transform.position.z);
            _why = "blocked";
            if (!CapsuleFree(feet, _cc.height)) return false;
            Place(feet);
            _why = "ok";
            return true;
        }

        // PM_FlyMove. Two capsule-specific fixes so a round bottom behaves like the GoldSrc box hull:
        //  - lift gained by sliding up a steep edge is taken back (no free height over ledges);
        //  - resting on a steep edge (PhysX does not slide off it) pushes the capsule out just far enough to fall past.
        void AirMove(Vector3 displacement)
        {
            float y0 = transform.position.y;
            _hitNormals.Clear();
            _cc.Move(displacement);
            float minGround = tuning.MinGroundNormalY;
            bool walkable = false, steep = false;
            for (int i = 0; i < _hitNormals.Count; i++)
            {
                Vector3 n = _hitNormals[i];
                if (n.y >= EdgeMinNormalY) walkable = true;       // floor, or a rim the ground probe will judge
                else if (n.y > 0.01f) steep = true;
            }
            if (walkable || !steep) return;

            float extraRise = transform.position.y - y0 - displacement.y;
            if (extraRise > 0.001f) _cc.Move(Vector3.down * extraRise);
        }

        // Resting on a steep edge while not grounded (PhysX does not slide off it): push the capsule out just far enough
        // to drop past the edge. The contact normal points from the edge to the bottom sphere centre, so the centre is
        // radius * |n_h| from the edge horizontally; it must be a full radius away.
        void EscapeEdge()
        {
            if ((_cc.collisionFlags & CollisionFlags.Below) == 0 || _state.Velocity.y > 0f) return;
            Vector3 steepest = Vector3.zero;
            for (int i = 0; i < _hitNormals.Count; i++)
            {
                Vector3 n = _hitNormals[i];
                if (n.y > 0.01f && n.y < tuning.MinGroundNormalY && n.y > steepest.y) steepest = n;
            }
            var h = new Vector3(steepest.x, 0f, steepest.z);
            float hl = h.magnitude;
            if (steepest.y <= 0f || hl < 1e-4f) return;
            _cc.Move(h / hl * (_cc.radius * (1f - hl) + 0.005f));
        }

        // PM_ClipVelocity with overbounce 1: never keep pushing into walls, stop on ceilings. A tilted normal from the
        // round capsule may slow a fall or turn it into a slide, but must never create upward speed: then it is
        // treated as the vertical wall a box hull would have hit.
        void ClipVelocity()
        {
            float minGround = tuning.MinGroundNormalY;
            for (int i = 0; i < _hitNormals.Count; i++)
            {
                Vector3 n = _hitNormals[i];
                if (n.y >= minGround) continue;                  // walkable floor, handled by the ground logic
                if (n.y < -0.7f)
                {
                    if (_state.Velocity.y > 0f) _state.Velocity.y = 0f;
                    continue;
                }
                Vector3 v = _state.Velocity;
                float into = Vector3.Dot(v, n);
                if (into >= 0f) continue;
                Vector3 clipped = v - n * into;
                if (clipped.y > Mathf.Max(v.y, 0f) + 1e-4f)
                {
                    var h = new Vector3(n.x, 0f, n.z);
                    if (h.sqrMagnitude < 1e-6f) continue;
                    h.Normalize();
                    float intoH = Vector3.Dot(v, h);
                    clipped = intoH < 0f ? v - h * intoH : v;
                }
                _state.Velocity = clipped;
            }
        }

        bool ProbeGround(float reach, out float distance)
        {
            distance = 0f;
            float r = _cc.radius - 0.01f;
            Vector3 bottomSphere = transform.position + _cc.center + Vector3.down * (_cc.height * 0.5f - _cc.radius);
            GroundCollider = null;
            if (!CastDown(bottomSphere, r, reach + SkinWidth + 0.01f, out RaycastHit hit))
                return false;
            if (hit.normal.y < tuning.MinGroundNormalY)
            {
                if (hit.normal.y < EdgeMinNormalY) return false;
                // edge contact: look at the face right under the contact point, a little towards the capsule axis
                Vector3 toAxis = bottomSphere - hit.point;
                toAxis.y = 0f;
                Vector3 origin = hit.point + (toAxis.sqrMagnitude > 1e-6f ? toAxis.normalized * 0.01f : Vector3.zero) + Vector3.up * 0.02f;
                if (!Physics.Raycast(origin, Vector3.down, out RaycastHit face, 0.05f, collisionMask, QueryTriggerInteraction.Ignore)
                    || face.normal.y < tuning.MinGroundNormalY)
                    return false;
            }
            distance = hit.distance;
            GroundCollider = hit.collider;
            return true;
        }

        // nearest sphere-cast hit that is not this capsule
        bool CastDown(Vector3 origin, float radius, float distance, out RaycastHit best)
        {
            best = default;
            int n = Physics.SphereCastNonAlloc(origin, radius, Vector3.down, _castHits, distance, collisionMask, QueryTriggerInteraction.Ignore);
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                if (_castHits[i].collider == _cc) continue;
                if (!found || _castHits[i].distance < best.distance) { best = _castHits[i]; found = true; }
            }
            return found;
        }

        void Place(Vector3 feet)
        {
            _cc.enabled = false;
            transform.position = feet;
            _cc.enabled = true;
        }

        static float Horizontal(Vector3 d) => new Vector2(d.x, d.z).magnitude;

        // ---------------------------------------------------------------- ladder (И3 H.3, NEW-01: Half-Life ladder)
        // The climb volume (kind "ladder", layer Interact, no collisions) is found by presence: OverlapCapsule with the
        // Interact mask and triggers on, because trigger callbacks never come for a layer without collisions. While the
        // capsule is in it and the player faces the treads, forward climbs and back descends along the stiles; the
        // treads and stiles let the capsule through (the platform and rails do not), so it steps off onto the platform at
        // the top and onto the ground at the bottom. Jump lets go.
        bool LadderMove(float dt)
        {
            Transform ladder = FindLadder();
            if (ladder == null || Input.Jump)
            {
                LeaveLadder();
                return false;
            }
            if (ladder != _ladder) EnterLadder(ladder);
            Vector3 look = Quaternion.Euler(0f, Input.YawDeg, 0f) * Vector3.forward;
            Vector3 toTreads = -ladder.forward;                              // treads on the ladder's +Z side, climber faces -Z
            bool facing = Vector3.Dot(look, toTreads) > 0.2f;
            // along the stiles: bottom tread (z 0.61, y 0.25) to the top tread (z 0.23, y 1.29), ladder space
            Vector3 along = ladder.TransformDirection(new Vector3(0f, 1.04f, -0.38f).normalized);
            float climb = facing ? Input.Move.y : 0f;
            Vector3 v = along * (climb * ladderSpeed) + ladder.right * (Input.Move.x * ladderSpeed * 0.5f);
            if (!facing) v += look * (Input.Move.y * tuning.walkSpeed * 0.5f);   // turned away: step off
            // no climb input: settle onto the tread or the platform under the feet (queries see the treads)
            bool onSomething = ProbeGround(tuning.groundProbeDistance, out _);
            if (Mathf.Abs(climb) < 0.01f && !onSomething) v += Vector3.down * ladderSpeed;
            _hitNormals.Clear();
            _cc.Move(v * dt);
            _state.Velocity = v;
            _state.Grounded = onSomething && climb <= 0f;
            return true;
        }

        Transform FindLadder()
        {
            int interact = LayerMask.NameToLayer("Interact");
            if (interact < 0) return null;
            float r = _cc.radius;
            Vector3 a = transform.position + Vector3.up * (SkinWidth + r), b = transform.position + Vector3.up * (SkinWidth + _cc.height - r);
            int n = Physics.OverlapCapsuleNonAlloc(a, b, r, _ladderHits, 1 << interact, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var zone = _ladderHits[i].GetComponent<UtilityPlane.UtilityPlaneInteractable>();
                if (zone != null && zone.kind == "ladder") return zone.transform;
            }
            return null;
        }

        void EnterLadder(Transform ladder)
        {
            LeaveLadder();
            _ladder = ladder;
            Transform root = ladder.parent != null ? ladder.parent : ladder;
            foreach (var c in root.GetComponentsInChildren<Collider>())
                if (!c.isTrigger && (c.name.Contains("Tread") || c.name.Contains("Stile")))
                {
                    Physics.IgnoreCollision(_cc, c, true);
                    _ladderIgnored.Add(c);
                }
        }

        void LeaveLadder()
        {
            if (_ladder == null) return;
            foreach (var c in _ladderIgnored) if (c != null) Physics.IgnoreCollision(_cc, c, false);
            _ladderIgnored.Clear();
            _ladder = null;
        }

        // ---------------------------------------------------------------- stance
        void UpdateStance(bool wantCrouch)
        {
            float eyeBefore = transform.position.y + EyeHeight;
            ChangeStance(wantCrouch);
            _stanceEyeJump += transform.position.y + EyeHeight - eyeBefore;
        }

        void ChangeStance(bool wantCrouch)
        {
            if (wantCrouch && !_state.Crouched)
            {
                ApplyCapsule(true);                              // on the ground the feet stay and the top comes down
                if (!_state.Grounded)
                    _cc.Move(Vector3.up * tuning.airCrouchFeetRaise);   // in the air the legs tuck (§7.2: +0.35 m)
                _state.Crouched = true;
            }
            else if (!wantCrouch && _state.Crouched)
            {
                float lower = _state.Grounded ? 0f : tuning.airCrouchFeetRaise;
                Vector3 feet = transform.position + Vector3.down * lower;
                if (!CapsuleFree(feet, tuning.standHeight)) return;  // no headroom (or no room for the legs): stay crouched
                if (lower > 0f) Place(feet);
                ApplyCapsule(false);
                _state.Crouched = false;
            }
        }

        bool CapsuleFree(Vector3 feet, float height)
        {
            float r = _cc.radius;
            Vector3 a = feet + Vector3.up * (SkinWidth + r + 0.01f);
            Vector3 b = feet + Vector3.up * (SkinWidth + height - r);
            bool was = _cc.enabled;
            _cc.enabled = false;                                 // do not collide with ourselves
            bool blocked = Physics.CheckCapsule(a, b, r, collisionMask, QueryTriggerInteraction.Ignore);
            _cc.enabled = was;
            return !blocked;
        }

        void ApplyCapsule(bool crouched)
        {
            if (_cc == null) _cc = GetComponent<CharacterController>();
            float h = crouched ? tuning.crouchHeight : tuning.standHeight;
            _cc.height = h;
            _cc.radius = tuning.radius;
            _cc.center = new Vector3(0f, SkinWidth + h * 0.5f, 0f);
        }
    }
}
