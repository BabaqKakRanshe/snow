using System;
using System.Collections.Generic;
using Snow.Net;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Snow.Cargo
{
    public enum CargoState : byte
    {
        Loose,
        Carried,
        Secured,
    }

    /// <summary>
    /// A physical cargo item (CARGO-01, GRAB-01, SECURE-01, MASS-01). Always a separate dynamic body simulated by the server
    /// (network physics variant A, §11.2); clients see a kinematic copy (NetworkRigidbody). Its owner never changes.
    ///  - Carried: every grabbing player pulls his own grab point (item-local, where he took it) towards his hand with a
    ///    clamped spring-damper; one entry per player, so repeated presses never add force.
    ///  - Secured: snapped to a free tie-down socket and held by a FixedJoint to the vehicle body, so its mass keeps acting
    ///    on the vehicle through the joint (no mass is added to the vehicle anywhere: no double counting).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class CargoItem : NetworkBehaviour
    {
        public string label = "Ящик";
        [Tooltip("kg, TEMPORARY (contents provider comes with CONTAINER-01)")] public float massKg = 15f;
        [Tooltip("Shown while secured: the visible strap (SECURE-01)")] public GameObject securedMarker;
        [Tooltip("N and N·m at which the tie-down lets go, TEMPORARY")] public float jointBreakForce = 30000f;
        [Tooltip("s: a hand target older than this stops pulling (lost packets, frozen client)")] public float targetTimeout = 0.5f;

        public struct GrabNet : INetworkSerializable, IEquatable<GrabNet>
        {
            public ulong Client;
            public Vector3 LocalPoint;

            public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
            {
                s.SerializeValue(ref Client);
                s.SerializeValue(ref LocalPoint);
            }

            public bool Equals(GrabNet o) => Client == o.Client && LocalPoint == o.LocalPoint;
        }

        class Hand
        {
            public Vector3 Local, Target, TargetVelocity;
            public float Time;
        }

        readonly NetworkVariable<CargoState> _state = new NetworkVariable<CargoState>(CargoState.Loose,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        readonly NetworkList<GrabNet> _grabs = new NetworkList<GrabNet>();
        readonly NetworkVariable<FixedString64Bytes> _socket = new NetworkVariable<FixedString64Bytes>(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        readonly Dictionary<ulong, Hand> _hands = new Dictionary<ulong, Hand>();
        readonly Collider[] _overlap = new Collider[16];
        Rigidbody _rb;
        FixedJoint _joint;
        TieDownSocket _tieDown;
        Collider[] _colliders;

        public CargoState State => _state.Value;
        public int Carriers => _grabs.Count;
        public string SocketId => _socket.Value.ToString();
        public Rigidbody Body => _rb;
        public GrabNet Grab(int index) => _grabs[index];

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.mass = massKg;
            _colliders = GetComponentsInChildren<Collider>();
        }

        public override void OnNetworkSpawn()
        {
            _state.OnValueChanged += (_, s) => ShowState(s);
            ShowState(_state.Value);
            if (IsServer) NetworkManager.OnClientDisconnectCallback += ServerRelease;
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= ServerRelease;
        }

        void ShowState(CargoState s)
        {
            if (securedMarker != null) securedMarker.SetActive(s == CargoState.Secured);
        }

        public string Describe()
        {
            switch (State)
            {
                case CargoState.Secured: return $"закреплён ({SocketId})";
                case CargoState.Carried: return $"несут: {Carriers}";
                default: return "свободен";
            }
        }

        // ================================================================ server: grab
        public bool ServerGrab(ulong client, Vector3 localPoint, out string reason)
        {
            reason = null;
            if (State == CargoState.Secured) { reason = "Закреплён — сначала открепите [R]"; return false; }
            Vector3 world = transform.TransformPoint(localPoint);
            if (_hands.TryGetValue(client, out Hand h))
                h.Local = localPoint;                               // re-grab moves the point, the budget stays one hand
            else
                _hands[client] = new Hand { Local = localPoint, Target = world, Time = Time.time };
            SyncGrabs();
            _state.Value = CargoState.Carried;
            BenchLog.Event("cargo_grab", "item", name, "client", client, "local", localPoint, "carriers", _hands.Count);
            return true;
        }

        public void ServerRelease(ulong client)
        {
            if (!_hands.Remove(client)) return;
            SyncGrabs();
            if (_hands.Count == 0 && State == CargoState.Carried) _state.Value = CargoState.Loose;
            BenchLog.Event("cargo_release", "item", name, "client", client, "carriers", _hands.Count);
        }

        public void ServerTarget(ulong client, Vector3 target)
        {
            if (!_hands.TryGetValue(client, out Hand h)) return;
            float dt = Time.time - h.Time;
            h.TargetVelocity = dt > 1e-3f && dt < targetTimeout ? (target - h.Target) / dt : Vector3.zero;
            h.Target = target;
            h.Time = Time.time;
        }

        void SyncGrabs()
        {
            _grabs.Clear();
            foreach (var kv in _hands) _grabs.Add(new GrabNet { Client = kv.Key, LocalPoint = kv.Value.Local });
        }

        int _traceStep;

        void FixedUpdate()
        {
            // AT-19 evidence: the server's pose and the client's (kinematic, interpolated) pose, by server time, ~24 Hz
            if (IsSpawned && SnowNetworkBootstrap.Args != null && SnowNetworkBootstrap.Args.Trace && (_traceStep++ % 5) == 0)
                BenchLog.Event("cpos", "item", name, "st", NetworkManager.ServerTime.Time, "p", transform.position,
                    "r", transform.eulerAngles, "s", State.ToString(), "srv", IsServer);
            if (!IsServer || !IsSpawned || State != CargoState.Carried) return;
            // light items (key 50 g, survival kit): the hand's spring, damper and budget scale with the mass below 5 kg,
            // otherwise the explicit step is unstable (c·dt/m = 60 for the key at 50 Hz)
            float g = Mathf.Min(1f, _rb.mass / 5f);
            foreach (var h in _hands.Values)
            {
                if (Time.time - h.Time > targetTimeout) continue;
                Vector3 p = transform.TransformPoint(h.Local);
                Vector3 f = GrabMath.HandForce(p, _rb.GetPointVelocity(p), h.Target, h.TargetVelocity,
                    GrabMath.Stiffness * g, GrabMath.Damping * g, GrabMath.MaxForcePerPlayer * g);
                _rb.AddForceAtPosition(f, p, ForceMode.Force);
            }
        }

        // ================================================================ server: secure
        /// <summary>Atomic on the server: compatible free socket in reach, tilt, no intersection at the snapped pose.</summary>
        public bool ServerSecure(ulong client, bool secure, out string reason)
        {
            reason = null;
            if (!secure) return ServerUnsecure(client, out reason);
            if (State == CargoState.Secured) { reason = "Уже закреплён"; return false; }

            TieDownSocket socket = FindSocket(out string why);
            CargoItem below = null;
            if (socket == null)
            {
                // a stacked item is tied to the secured item it stands on (SECURE-01: each item of a stack separately)
                below = SupportItem();
                if (below == null) { reason = why ?? "Нет свободного крепления рядом: поставьте ящик на пол у точки крепления"; return false; }
                if (below.State != CargoState.Secured) { reason = "Сначала закрепите нижний ящик"; return false; }
            }

            // "brought in a little" (SECURE-01): onto the socket with the yaw snapped to 90°; if that pose hits a wall or a
            // neighbour, only the yaw is snapped; if that hits too, it is tied where it stands (it is already there)
            Vector3 pos = transform.position;
            Quaternion rot = transform.rotation;
            if (socket != null)
            {
                Quaternion snapped = SnappedRotation(socket);
                if (Obstruction(socket.transform.position, snapped, socket) == null) { pos = socket.transform.position; rot = snapped; }
                else if (Obstruction(transform.position, snapped, socket) == null) rot = snapped;
            }

            // co-carriers let go explicitly before the item becomes rigidly attached (SECURE-01)
            _hands.Clear();
            SyncGrabs();
            Rigidbody vehicle = socket != null ? socket.GetComponentInParent<Rigidbody>() : below.Body;
            _rb.position = pos;
            _rb.rotation = rot;
            transform.SetPositionAndRotation(pos, rot);
            _rb.linearVelocity = vehicle != null ? vehicle.GetPointVelocity(pos) : Vector3.zero;
            _rb.angularVelocity = vehicle != null ? vehicle.angularVelocity : Vector3.zero;
            _joint = gameObject.AddComponent<FixedJoint>();
            _joint.connectedBody = vehicle;
            _joint.breakForce = jointBreakForce;
            _joint.breakTorque = jointBreakForce;
            string place = socket != null ? socket.socketId : "на " + below.label;
            if (socket != null) socket.Occupant = this;
            _tieDown = socket;
            _socket.Value = new FixedString64Bytes(place);
            _state.Value = CargoState.Secured;
            BenchLog.Event("cargo_secure", "item", name, "client", client, "socket", place, "p", pos);
            return true;
        }

        bool ServerUnsecure(ulong client, out string reason)
        {
            reason = null;
            if (State != CargoState.Secured) { reason = "Не закреплён"; return false; }
            // the joint kept the item at the vehicle's point velocity: removing it keeps that velocity (no zeroing)
            if (_joint != null) Destroy(_joint);
            _joint = null;
            FreeSocket();
            _state.Value = CargoState.Loose;
            BenchLog.Event("cargo_unsecure", "item", name, "client", client, "v", _rb.linearVelocity);
            return true;
        }

        void OnJointBreak(float force)
        {
            if (!IsServer) return;
            _joint = null;
            FreeSocket();
            _state.Value = CargoState.Loose;
            BenchLog.Event("cargo_joint_broke", "item", name, "force", force);
        }

        void FreeSocket()
        {
            if (_tieDown != null && _tieDown.Occupant == this) _tieDown.Occupant = null;
            _tieDown = null;
            _socket.Value = default;
        }

        // A free socket the item stands next to: its pivot (bottom centre) on the socket's floor plane within the capture
        // radius, measured along the floor (a stacked item is above the floor, not "near" a socket), and upright.
        TieDownSocket FindSocket(out string why)
        {
            why = null;
            TieDownSocket best = null;
            float bestDist = float.MaxValue;
            foreach (var s in FindObjectsByType<TieDownSocket>(FindObjectsSortMode.None))
            {
                Vector3 up = s.transform.up;
                Vector3 d = transform.position - s.transform.position;
                float height = Vector3.Dot(d, up);
                float along = (d - up * height).magnitude;
                if (height < -0.08f || height > 0.15f || along > s.captureRadius || along >= bestDist) continue;
                if (s.Occupant != null) { why ??= "Эта точка крепления занята"; continue; }
                if (Vector3.Angle(transform.up, up) > s.maxTilt) { why = "Ящик стоит неровно: поставьте его на дно"; continue; }
                best = s;
                bestDist = along;
            }
            return best;
        }

        // the cargo item this one stands on (ray down from the middle of its bottom), or null
        CargoItem SupportItem()
        {
            Bounds b = _colliders.Length > 0 ? _colliders[0].bounds : new Bounds(transform.position, Vector3.one * 0.2f);
            foreach (var c in _colliders) if (c != null && !c.isTrigger) b.Encapsulate(c.bounds);
            var hits = Physics.RaycastAll(b.center, Vector3.down, b.extents.y + 0.1f, ~0, QueryTriggerInteraction.Ignore);
            CargoItem best = null;
            float bestDist = float.MaxValue;
            foreach (var h in hits)
            {
                var item = h.collider.GetComponentInParent<CargoItem>();
                if (item == null || item == this || h.distance >= bestDist) continue;
                best = item;
                bestDist = h.distance;
            }
            return best;
        }

        Quaternion SnappedRotation(TieDownSocket s)
        {
            Vector3 up = s.transform.up;
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, up);
            float yaw = Vector3.SignedAngle(s.transform.forward, fwd, up);
            float snapped = Mathf.Round(yaw / 90f) * 90f;
            return s.transform.rotation * Quaternion.Euler(0f, snapped, 0f);
        }

        /// <summary>Name of what the item would intersect at the pose (the floor it rests on excepted), or null.</summary>
        string Obstruction(Vector3 pos, Quaternion rot, TieDownSocket socket)
        {
            Quaternion delta = rot * Quaternion.Inverse(transform.rotation);
            foreach (var c in _colliders)
            {
                if (c == null || !c.enabled || c.isTrigger) continue;
                Vector3 cPos = pos + delta * (c.transform.position - transform.position);
                Quaternion cRot = delta * c.transform.rotation;
                Bounds b = c.bounds;
                Vector3 center = pos + delta * (b.center - transform.position);
                int n = Physics.OverlapBoxNonAlloc(center, b.extents, _overlap, cRot, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    Collider o = _overlap[i];
                    if (o.transform.IsChildOf(transform) || o is CharacterController) continue;
                    if (Physics.ComputePenetration(c, cPos, cRot, o, o.transform.position, o.transform.rotation, out _, out float depth)
                        && depth > 0.01f)
                        return o.name;
                }
            }
            return null;
        }
    }
}
