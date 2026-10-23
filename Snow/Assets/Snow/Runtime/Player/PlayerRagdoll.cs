using Snow.Character;
using Snow.Input;
using Snow.Interaction;
using Snow.Seats;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace Snow.Player
{
    /// <summary>Why a player went down as a ragdoll. Death stays down until <see cref="PlayerRagdoll.EndDeath"/>.</summary>
    public enum RagdollCause : byte { Key, HardLanding, Hit, Death }

    /// <summary>
    /// "Тряпичная кукла" (user 23.10, QUESTIONS_5.md 41: "обязательно и в лучшем виде"; triggers: own key, hard landing,
    /// hit by a vehicle or a creature, death). There is no body model yet (players are capsules), so the body is a
    /// mannequin of 11 primitives on CharacterJoints (user choice 23.10), built when the player falls and destroyed when
    /// he gets up; a real worker model later takes its bones. The owner simulates it (as he does his own movement) and
    /// streams the pose (pelvis position + 11 rotations, ~100 bytes) every network tick; every other peer shows a
    /// kinematic copy that follows it. The camera rides the head (FirstPersonView), the owner sees only the shadow of his
    /// own body (user: no own body). Things in the hands are dropped. Numbers TEMPORARY.
    /// Plain component added by PlayerNet on every peer; PlayerNet carries the network state and messages.
    /// </summary>
    public class PlayerRagdoll : MonoBehaviour
    {
        public PlayerNet net;

        [Tooltip("m/s landing speed that knocks the player down (10 m/s ≈ 5 m fall)")] public float hardLanding = 10f;
        [Tooltip("m/s a vehicle must close in on the player to knock him down")] public float vehicleHit = 4f;
        [Tooltip("s down at least: own key / landing / hit")] public float minDownKey = 0.8f, minDownLanding = 1.5f, minDownHit = 2f;
        [Tooltip("m/s of the pelvis under which the body counts as still; s still before getting up by itself")] public float restSpeed = 0.4f, restTime = 1f;
        [Tooltip("m/s of the pelvis under which the own key or jump gets up")] public float getUpSpeed = 1.5f;
        [Tooltip("s: a ragdoll that never settles gets up anyway (not death)")] public float maxDown = 20f;

        // ------------------------------------------------------------------ the mannequin, standing, feet at 0, facing +Z
        const int Bones = 11;
        enum Shape : byte { Box, Sphere, Capsule }
        struct Part
        {
            public string Name; public int Parent; public Shape Shape;
            public Vector3 Center, Size, Joint;        // Size: box size / (radius, length) for a capsule / (radius) for a sphere
            public float Mass;
            public Vector3 Axis; public float Low, High, Swing1, Swing2;
        }

        // joint limits after Unity's ragdoll wizard; twist axis = the hinge (right), + bends forward for the spine and head
        static readonly Part[] Parts =
        {
            new Part { Name = "Pelvis",    Parent = -1, Shape = Shape.Box,     Center = new Vector3(0f, 0.95f, 0f),       Size = new Vector3(0.32f, 0.2f, 0.2f),  Mass = 12f },
            new Part { Name = "Chest",     Parent = 0,  Shape = Shape.Box,     Center = new Vector3(0f, 1.27f, 0f),       Size = new Vector3(0.38f, 0.44f, 0.22f), Mass = 22f, Joint = new Vector3(0f, 1.05f, 0f), Axis = Vector3.right, Low = -20f, High = 40f, Swing1 = 15f, Swing2 = 15f },
            new Part { Name = "Head",      Parent = 1,  Shape = Shape.Sphere,  Center = new Vector3(0f, 1.62f, 0f),       Size = new Vector3(0.11f, 0f, 0f),       Mass = 5f,  Joint = new Vector3(0f, 1.5f, 0f),  Axis = Vector3.right, Low = -40f, High = 50f, Swing1 = 30f, Swing2 = 50f },
            new Part { Name = "UpperArmL", Parent = 1,  Shape = Shape.Capsule, Center = new Vector3(-0.25f, 1.29f, 0f),   Size = new Vector3(0.05f, 0.32f, 0f),    Mass = 2.5f, Joint = new Vector3(-0.24f, 1.44f, 0f), Axis = Vector3.right, Low = -150f, High = 50f, Swing1 = 60f, Swing2 = 40f },
            new Part { Name = "ForearmL",  Parent = 3,  Shape = Shape.Capsule, Center = new Vector3(-0.265f, 0.99f, 0f),  Size = new Vector3(0.045f, 0.32f, 0f),   Mass = 2f,  Joint = new Vector3(-0.26f, 1.14f, 0f), Axis = Vector3.right, Low = -130f, High = 0f, Swing1 = 3f, Swing2 = 20f },
            new Part { Name = "UpperArmR", Parent = 1,  Shape = Shape.Capsule, Center = new Vector3(0.25f, 1.29f, 0f),    Size = new Vector3(0.05f, 0.32f, 0f),    Mass = 2.5f, Joint = new Vector3(0.24f, 1.44f, 0f),  Axis = Vector3.right, Low = -150f, High = 50f, Swing1 = 60f, Swing2 = 40f },
            new Part { Name = "ForearmR",  Parent = 5,  Shape = Shape.Capsule, Center = new Vector3(0.265f, 0.99f, 0f),   Size = new Vector3(0.045f, 0.32f, 0f),   Mass = 2f,  Joint = new Vector3(0.26f, 1.14f, 0f),  Axis = Vector3.right, Low = -130f, High = 0f, Swing1 = 3f, Swing2 = 20f },
            new Part { Name = "ThighL",    Parent = 0,  Shape = Shape.Capsule, Center = new Vector3(-0.1f, 0.67f, 0f),    Size = new Vector3(0.075f, 0.44f, 0f),   Mass = 8f,  Joint = new Vector3(-0.1f, 0.88f, 0f),  Axis = Vector3.right, Low = -100f, High = 20f, Swing1 = 30f, Swing2 = 20f },
            new Part { Name = "ShinL",     Parent = 7,  Shape = Shape.Capsule, Center = new Vector3(-0.1f, 0.25f, 0f),    Size = new Vector3(0.06f, 0.48f, 0f),    Mass = 4.5f, Joint = new Vector3(-0.1f, 0.47f, 0f),  Axis = Vector3.right, Low = 0f, High = 120f, Swing1 = 3f, Swing2 = 5f },
            new Part { Name = "ThighR",    Parent = 0,  Shape = Shape.Capsule, Center = new Vector3(0.1f, 0.67f, 0f),     Size = new Vector3(0.075f, 0.44f, 0f),   Mass = 8f,  Joint = new Vector3(0.1f, 0.88f, 0f),   Axis = Vector3.right, Low = -100f, High = 20f, Swing1 = 30f, Swing2 = 20f },
            new Part { Name = "ShinR",     Parent = 9,  Shape = Shape.Capsule, Center = new Vector3(0.1f, 0.25f, 0f),     Size = new Vector3(0.06f, 0.48f, 0f),    Mass = 4.5f, Joint = new Vector3(0.1f, 0.47f, 0f),   Axis = Vector3.right, Low = 0f, High = 120f, Swing1 = 3f, Swing2 = 5f },
        };
        const int Head = 2, Pelvis = 0;

        HLCharacterMotor _motor;
        FirstPersonView _view;
        InteractionSensor _sensor;
        PlayerController _controller;
        NetworkTransform _nt;

        Transform _root;
        readonly Transform[] _bones = new Transform[Bones];
        readonly Rigidbody[] _bodies = new Rigidbody[Bones];
        bool _owner, _down;
        RagdollCause _cause;
        float _since, _still;
        readonly Collider[] _near = new Collider[16];

        // remote copy: the last pose received, followed smoothly
        Vector3 _netPelvis;
        readonly Quaternion[] _netRot = new Quaternion[Bones];
        bool _netHave;
        RagdollPose _send;

        public bool Down => _down;
        public RagdollCause Cause => _cause;

        public void Init(PlayerNet owner)
        {
            net = owner;
            _motor = owner.motor;
            _view = owner.view;
            _sensor = owner.sensor;
            _controller = owner.controller;
            _nt = owner.GetComponent<NetworkTransform>();
            _owner = owner.IsOwner;
            if (_owner) _view.Landed += OnLanded;
        }

        void OnDestroy()
        {
            if (_owner && _view != null) _view.Landed -= OnLanded;
            DestroyMannequin();
        }

        void OnEnable()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.NetworkTickSystem != null)
                NetworkManager.Singleton.NetworkTickSystem.Tick += OnTick;
        }

        void OnDisable()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.NetworkTickSystem != null)
                NetworkManager.Singleton.NetworkTickSystem.Tick -= OnTick;
        }

        // ================================================================== owner
        void OnLanded(float speed)
        {
            if (speed < hardLanding || _down) return;
            Vector3 v = _motor.Velocity;
            Begin(new Vector3(v.x, -speed * 0.3f, v.z), Vector3.zero, RagdollCause.HardLanding);
        }

        /// <summary>Owner: fall as a ragdoll with the body's velocity plus an impulse (m/s, to the chest). A death while
        /// already down only makes it permanent.</summary>
        public void Begin(Vector3 velocity, Vector3 impulse, RagdollCause cause)
        {
            if (!_owner) return;
            if (_down)
            {
                if (cause == RagdollCause.Death) _cause = cause;
                if (impulse != Vector3.zero) Push(impulse);
                return;
            }
            if (_view.Seated) return;
            if (_sensor.Held != null) net.RequestDrop();                // what is in the hands falls with him
            _sensor.enabled = false;
            _motor.enabled = false;
            _motor.Controller.enabled = false;

            Build(Quaternion.Euler(0f, _view.Yaw, 0f), kinematic: false);
            foreach (Rigidbody rb in _bodies) rb.linearVelocity = velocity;
            Push(impulse);
            _down = true;
            _cause = cause;
            _since = Time.time;
            _still = 0f;
            _view.BeginRagdoll(_bones[Head]);
            net.SetRagdollNet(true);
            SendPose();
        }

        /// <summary>Owner: a push while down (a second hit).</summary>
        void Push(Vector3 impulse)
        {
            if (impulse == Vector3.zero || _bodies[1] == null) return;
            _bodies[1].AddForce(impulse * 0.7f, ForceMode.VelocityChange);
            _bodies[Pelvis].AddForce(impulse * 0.4f, ForceMode.VelocityChange);
        }

        /// <summary>Owner: the own key (TEMPORARY V; C is the callout). Standing: fall over forward; down: get up once the
        /// body is slow enough (jump gets up too).</summary>
        public void KeyPressed()
        {
            if (!_owner) return;
            if (!_down)
            {
                Vector3 fwd = Quaternion.Euler(0f, _view.Yaw, 0f) * Vector3.forward;
                Begin(_motor.Velocity, fwd * 1.2f, RagdollCause.Key);
                return;
            }
            TryGetUp(byKey: true);
        }

        /// <summary>Owner: the death is over (the future death system: after the black-out, before the bed).</summary>
        public void EndDeath()
        {
            if (_owner && _down && _cause == RagdollCause.Death) GetUp();
        }

        void Update()
        {
            if (!_owner) { if (_down) FollowNet(); return; }

            PlayerInputFrame f = _controller.Frame;
            PlayerInputFrame last = _controller.PreviousFrame;
            bool key = f.Ragdoll && !last.Ragdoll;
            if (!_down)
            {
                if (key && _motor.enabled) KeyPressed();
                return;
            }
            if (key || (f.Jump && !last.Jump)) TryGetUp(byKey: true);
            if (!_down) return;

            Rigidbody pelvis = _bodies[Pelvis];
            float speed = pelvis.linearVelocity.magnitude;
            _still = speed < restSpeed ? _still + Time.deltaTime : 0f;
            if (_cause != RagdollCause.Key && _cause != RagdollCause.Death && _still >= restTime) TryGetUp(byKey: false);
            else if (_cause != RagdollCause.Death && (Time.time - _since > maxDown || pelvis.position.y < -500f)) GetUp();

            // the player object follows the body: the server's use point, positional voice, the network position
            Vector3 p = pelvis.position;
            transform.position = new Vector3(p.x, p.y - 0.9f, p.z);
        }

        void TryGetUp(bool byKey)
        {
            if (_cause == RagdollCause.Death) return;
            float min = _cause == RagdollCause.Key ? minDownKey : _cause == RagdollCause.HardLanding ? minDownLanding : minDownHit;
            if (Time.time - _since < min) return;
            if (byKey && _bodies[Pelvis].linearVelocity.magnitude > getUpSpeed) return;
            GetUp();
        }

        void GetUp()
        {
            Vector3 pelvis = _bodies[Pelvis].position;
            Vector3 look = _view.cam != null ? _view.cam.transform.forward : _bones[1].forward;
            look.y = 0f;
            if (look.sqrMagnitude < 0.01f) { look = _bones[1].up; look.y = 0f; }      // lying on the back, looking at the sky
            float yaw = look.sqrMagnitude > 0.01f ? Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg : _view.Yaw;
            DestroyMannequin();

            Vector3 feet = StandPoint(pelvis);
            _motor.Controller.enabled = true;
            _motor.enabled = true;
            _motor.Teleport(feet, yaw, Vector3.zero);
            _motor.LastLandingSpeed = 0f;
            if (_nt != null && _nt.CanCommitToTransform) _nt.Teleport(feet, Quaternion.Euler(0f, yaw, 0f), transform.localScale);
            _view.EndRagdoll(yaw);
            _sensor.enabled = true;
            _down = false;
            net.SetRagdollNet(false);
        }

        // the ground under the pelvis, raised until a standing capsule fits (a body on a slope or against a wall)
        Vector3 StandPoint(Vector3 pelvis)
        {
            MovementTuning t = _motor.tuning;
            Vector3 feet = pelvis + Vector3.down * 0.9f;
            if (Physics.Raycast(pelvis + Vector3.up * 0.3f, Vector3.down, out RaycastHit hit, 3f, _motor.collisionMask, QueryTriggerInteraction.Ignore))
                feet = hit.point;
            for (int i = 0; i < 7; i++)
            {
                Vector3 a = feet + Vector3.up * (t.radius + 0.03f), b = feet + Vector3.up * (t.standHeight - t.radius);
                if (!Physics.CheckCapsule(a, b, t.radius, _motor.collisionMask, QueryTriggerInteraction.Ignore)) break;
                feet += Vector3.up * 0.1f;
            }
            return feet;
        }

        void FixedUpdate()
        {
            if (!_owner || _down || !_motor.enabled || _view.Seated) return;
            CheckVehicleHit();
        }

        // a moving vehicle closing in fast knocks the player down (not one he stands on)
        void CheckVehicleHit()
        {
            MovementTuning t = _motor.tuning;
            Vector3 feet = transform.position;
            Vector3 a = feet + Vector3.up * (t.radius + 0.05f), b = feet + Vector3.up * (t.standHeight - t.radius);
            int n = Physics.OverlapCapsuleNonAlloc(a, b, t.radius + 0.12f, _near, ~0, QueryTriggerInteraction.Ignore);
            Rigidbody ground = _motor.GroundCollider != null ? _motor.GroundCollider.attachedRigidbody : null;
            Vector3 center = feet + Vector3.up * (t.standHeight * 0.5f);
            for (int i = 0; i < n; i++)
            {
                Rigidbody rb = _near[i].attachedRigidbody;
                if (rb == null || rb.isKinematic || rb == ground || rb.GetComponentInParent<SeatedVehicle>() == null) continue;
                Vector3 rel = rb.GetPointVelocity(center) - _motor.Velocity;
                Vector3 dir = center - _near[i].ClosestPoint(center);
                dir.y = 0f;
                if (dir.sqrMagnitude < 1e-4f) dir = -rel;
                dir.y = 0f;
                if (dir.sqrMagnitude < 1e-4f) continue;
                float closing = Vector3.Dot(rel, dir.normalized);
                if (closing < vehicleHit) continue;
                CameraShake.Add(0.7f);
                Begin(_motor.Velocity, rel * 0.9f + Vector3.up * Mathf.Min(3f, closing * 0.3f), RagdollCause.Hit);
                return;
            }
        }

        void OnTick()
        {
            if (_owner && _down) SendPose();
        }

        void SendPose()
        {
            if (_bones[Pelvis] == null) return;
            _send.Pelvis = _bones[Pelvis].position;
            _send.Q ??= new short[Bones * 4];
            for (int i = 0; i < Bones; i++) RagdollPose.Pack(_bones[i].rotation, _send.Q, i);
            net.SendRagdollPose(_send);
        }

        // ================================================================== other peers
        /// <summary>Every peer but the owner: the replicated state changed (also on late join).</summary>
        public void ShowRemote(bool down)
        {
            if (_owner || down == _down) return;
            _down = down;
            if (down)
            {
                Build(transform.rotation, kinematic: true);
                _netHave = false;
                net.ShowCapsule(false);
            }
            else
            {
                DestroyMannequin();
                net.ShowCapsule(true);
            }
        }

        public void ReceivePose(RagdollPose pose)
        {
            if (_owner || pose.Q == null || pose.Q.Length < Bones * 4) return;
            _netPelvis = pose.Pelvis;
            for (int i = 0; i < Bones; i++) _netRot[i] = RagdollPose.Unpack(pose.Q, i);
            if (!_netHave && _bones[Pelvis] != null)                       // the first pose: jump there
            {
                _netHave = true;
                Place(_netPelvis, _netRot, 1f);
            }
            _netHave = true;
        }

        void FollowNet()
        {
            if (!_netHave || _bones[Pelvis] == null) return;
            Place(_netPelvis, _netRot, 1f - Mathf.Pow(2f, -Time.deltaTime / 0.05f));
        }

        // rotations as given, positions rebuilt down the chain from the pelvis through the joint points
        void Place(Vector3 pelvis, Quaternion[] rots, float k)
        {
            for (int i = 0; i < Bones; i++)
            {
                Quaternion r = Quaternion.Slerp(_bones[i].rotation, rots[i], k);
                Vector3 p;
                if (Parts[i].Parent < 0) p = Vector3.Lerp(_bones[i].position, pelvis, k);
                else
                {
                    Part part = Parts[i];
                    Transform parent = _bones[part.Parent];
                    p = parent.position + parent.rotation * (part.Joint - Parts[part.Parent].Center) + r * (part.Center - part.Joint);
                }
                _bones[i].SetPositionAndRotation(p, r);
            }
        }

        // ================================================================== building
        void Build(Quaternion facing, bool kinematic)
        {
            DestroyMannequin();
            _root = new GameObject($"Ragdoll_{net.OwnerClientId}").transform;
            Vector3 origin = transform.position;
            Material mat = net.body != null ? net.body.sharedMaterial : null;
            var block = new MaterialPropertyBlock();
            if (net.body != null) net.body.GetPropertyBlock(block);

            for (int i = 0; i < Bones; i++)
            {
                Part part = Parts[i];
                var go = new GameObject(part.Name);
                Transform tr = go.transform;
                tr.SetParent(_root, false);
                tr.SetPositionAndRotation(origin + facing * part.Center, facing);
                _bones[i] = tr;

                Collider col;
                PrimitiveType prim;
                Vector3 scale;
                switch (part.Shape)
                {
                    case Shape.Box:
                        col = go.AddComponent<BoxCollider>();
                        ((BoxCollider)col).size = part.Size;
                        prim = PrimitiveType.Cube;
                        scale = part.Size;
                        break;
                    case Shape.Sphere:
                        col = go.AddComponent<SphereCollider>();
                        ((SphereCollider)col).radius = part.Size.x;
                        prim = PrimitiveType.Sphere;
                        scale = Vector3.one * part.Size.x * 2f;
                        break;
                    default:
                        var cap = go.AddComponent<CapsuleCollider>();
                        cap.radius = part.Size.x;
                        cap.height = part.Size.y;
                        cap.direction = 1;
                        col = cap;
                        prim = PrimitiveType.Capsule;
                        scale = new Vector3(part.Size.x * 2f, part.Size.y * 0.5f, part.Size.x * 2f);
                        break;
                }
                col.enabled = !kinematic;                                    // a remote copy is a picture only

                GameObject vis = GameObject.CreatePrimitive(prim);
                Destroy(vis.GetComponent<Collider>());
                vis.name = "Mesh";
                vis.transform.SetParent(tr, false);
                vis.transform.localScale = scale;
                var mr = vis.GetComponent<MeshRenderer>();
                if (mat != null) mr.sharedMaterial = mat;
                mr.SetPropertyBlock(block);
                if (_owner) mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;   // no own body

                var rb = go.AddComponent<Rigidbody>();
                rb.mass = part.Mass;
                rb.isKinematic = kinematic;
                rb.interpolation = kinematic ? RigidbodyInterpolation.None : RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = kinematic ? CollisionDetectionMode.Discrete : CollisionDetectionMode.ContinuousSpeculative;
                rb.solverIterations = 12;
                rb.solverVelocityIterations = 4;
                rb.linearDamping = 0.05f;
                rb.angularDamping = 0.3f;
                rb.maxAngularVelocity = 20f;
                _bodies[i] = rb;

                if (part.Parent < 0 || kinematic) continue;
                var j = go.AddComponent<CharacterJoint>();
                j.connectedBody = _bodies[part.Parent];
                j.anchor = part.Joint - part.Center;
                j.axis = part.Axis;
                j.swingAxis = Vector3.forward;
                j.lowTwistLimit = new SoftJointLimit { limit = part.Low };
                j.highTwistLimit = new SoftJointLimit { limit = part.High };
                j.swing1Limit = new SoftJointLimit { limit = part.Swing1 };
                j.swing2Limit = new SoftJointLimit { limit = part.Swing2 };
                j.enableProjection = true;
                j.enablePreprocessing = false;
                Physics.IgnoreCollision(col, _bones[part.Parent].GetComponent<Collider>());
            }
            if (kinematic) return;
            // arms against the chest and the thighs against each other jitter at the shoulders and hips
            Ignore(3, 1); Ignore(5, 1); Ignore(7, 9); Ignore(4, 1); Ignore(6, 1);
        }

        void Ignore(int a, int b) => Physics.IgnoreCollision(_bones[a].GetComponent<Collider>(), _bones[b].GetComponent<Collider>());

        void DestroyMannequin()
        {
            if (_root != null) Destroy(_root.gameObject);
            _root = null;
            for (int i = 0; i < Bones; i++) { _bones[i] = null; _bodies[i] = null; }
        }
    }

    /// <summary>The ragdoll pose on the wire: pelvis position and every bone's world rotation as four shorts.</summary>
    public struct RagdollPose : INetworkSerializable
    {
        public Vector3 Pelvis;
        public short[] Q;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Pelvis);
            int n = Q != null ? Q.Length : 0;
            serializer.SerializeValue(ref n);
            if (serializer.IsReader)
            {
                if (n < 0 || n > 64) n = 0;
                Q = new short[n];
            }
            for (int i = 0; i < n; i++) serializer.SerializeValue(ref Q[i]);
        }

        public static void Pack(Quaternion q, short[] to, int i)
        {
            q = q.normalized;
            if (q.w < 0f) { q.x = -q.x; q.y = -q.y; q.z = -q.z; q.w = -q.w; }
            to[i * 4] = (short)(q.x * 32767f);
            to[i * 4 + 1] = (short)(q.y * 32767f);
            to[i * 4 + 2] = (short)(q.z * 32767f);
            to[i * 4 + 3] = (short)(q.w * 32767f);
        }

        public static Quaternion Unpack(short[] from, int i)
        {
            var q = new Quaternion(from[i * 4] / 32767f, from[i * 4 + 1] / 32767f, from[i * 4 + 2] / 32767f, from[i * 4 + 3] / 32767f);
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return m > 1e-4f ? new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m) : Quaternion.identity;
        }
    }
}
