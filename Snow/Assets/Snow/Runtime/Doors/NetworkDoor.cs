using System;
using Snow.Net;
using Unity.Netcode;
using UnityEngine;

namespace Snow.Doors
{
    /// <summary>
    /// One door on a networked vehicle (DOOR-01, §8.2). Sits on the hinge object (a child of the vehicle's NetworkObject);
    /// the door panel and its colliders are children of the hinge. The server runs DoorModel and, before every step,
    /// tests the panel at the next position against everything that is not part of the vehicle (OverlapBox, then
    /// ComputePenetration): a crate or a player in the opening blocks the door, which neither passes through it nor
    /// pushes it. Every peer poses the hinge from the replicated state, so the panel collider is where it is seen.
    /// </summary>
    public class NetworkDoor : NetworkBehaviour
    {
        public string doorId = "Door";
        [Tooltip("Hinge rotation at fully open, degrees about the local axis")] public float openAngle = 80f;
        public Vector3 axis = Vector3.up;
        [Tooltip("s, TEMPORARY")] public float poweredTravelTime = 2.5f;
        [Tooltip("s, TEMPORARY")] public float manualTravelTime = 1.2f;
        [Tooltip("Colliders of the panel that move with the hinge")] public Collider[] panelColliders;
        [Tooltip("Root of the vehicle; its own colliders never block its doors")] public Transform vehicleRoot;
        [Tooltip("Layers that can block the door")] public LayerMask blockMask = ~0;

        public struct DoorNet : INetworkSerializable, IEquatable<DoorNet>
        {
            public DoorState State;
            public float Position;

            public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
            {
                s.SerializeValue(ref State);
                s.SerializeValue(ref Position);
            }

            public bool Equals(DoorNet o) => State == o.State && Position == o.Position;
        }

        readonly NetworkVariable<DoorNet> _net = new NetworkVariable<DoorNet>(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        DoorModel _model;
        Quaternion _rest;
        float _shown;
        readonly Collider[] _overlap = new Collider[16];

        public DoorState State => IsServer && _model != null ? _model.State : _net.Value.State;
        public float Position => IsServer && _model != null ? _model.Position : _net.Value.Position;
        /// <summary>The position the hinge is drawn at on this peer (smoothed on clients); feeds UtilityPlaneRig.</summary>
        public float ShownPosition => _shown;
        /// <summary>Server: the actuator draws current this step (electrics, P06).</summary>
        public bool ActuatorRunning => _model != null && _model.ActuatorRunning;
        /// <summary>Server-side bus power for the actuator; set by the vehicle's systems (P01 bench: always on).</summary>
        public Func<bool> PowerSource;

        void Awake()
        {
            _rest = transform.localRotation;
            _model = new DoorModel(poweredTravelTime, manualTravelTime);
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer) _model.Restore(_net.Value.State, _net.Value.Position);
            _shown = _net.Value.Position;
            Pose(_shown);
        }

        /// <summary>Server: open or close.</summary>
        public void Command(bool open, DoorDrive drive)
        {
            if (!IsServer) return;
            _model.Command(open, drive);
            Publish();
            BenchLog.Event("door_cmd", "door", doorId, "open", open, "drive", drive.ToString(), "state", _model.State.ToString());
        }

        /// <summary>Server: reverse the current request.</summary>
        public void Toggle(DoorDrive drive)
        {
            if (!IsServer) return;
            _model.Toggle(drive);
            Publish();
            BenchLog.Event("door_cmd", "door", doorId, "toggle", true, "drive", drive.ToString(), "state", _model.State.ToString());
        }

        void FixedUpdate()
        {
            if (!IsServer || !IsSpawned) return;
            DoorState before = _model.State;
            bool powered = PowerSource == null || PowerSource();
            bool moved = _model.Tick(Time.fixedDeltaTime, powered, IsFree);
            if (moved || _model.State != before)
            {
                Publish();
                if (_model.State != before)
                    BenchLog.Event("door_state", "door", doorId, "state", _model.State.ToString(), "pos", _model.Position);
            }
        }

        void Publish() => _net.Value = new DoorNet { State = _model.State, Position = _model.Position };

        void Update()
        {
            float target = Position;
            if (IsServer) _shown = target;
            else
            {
                // replicated at tick rate: move towards it no faster than the door can, so it never jumps
                float rate = 1.5f / Mathf.Max(0.1f, Mathf.Min(poweredTravelTime, manualTravelTime));
                _shown = Mathf.MoveTowards(_shown, target, rate * Time.deltaTime);
            }
            Pose(_shown);
        }

        // the hinge is written only when the shown position changes (a parked aircraft writes no transforms, contract 45)
        float _posed = float.NaN;
        void Pose(float position)
        {
            if (position == _posed) return;
            _posed = position;
            transform.localRotation = _rest * Quaternion.AngleAxis(openAngle * position, axis);
        }

        // ---------------------------------------------------------------- obstacle test (server)
        bool IsFree(float position)
        {
            Quaternion parentRot = transform.parent != null ? transform.parent.rotation : Quaternion.identity;
            Quaternion hingeRot = parentRot * _rest * Quaternion.AngleAxis(openAngle * position, axis);
            Quaternion currentHingeRot = transform.rotation;
            Vector3 hingePos = transform.position;
            foreach (var panel in panelColliders)
            {
                if (panel == null || !panel.enabled) continue;
                // pose of the panel collider if the hinge were at 'position'
                Quaternion toNew = hingeRot * Quaternion.Inverse(currentHingeRot);
                Vector3 pos = hingePos + toNew * (panel.transform.position - hingePos);
                Quaternion rot = toNew * panel.transform.rotation;
                Bounds local = LocalBounds(panel);
                Vector3 scale = panel.transform.lossyScale;
                Vector3 half = Vector3.Scale(local.extents, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                Vector3 center = pos + rot * Vector3.Scale(local.center, scale);
                int n = Physics.OverlapBoxNonAlloc(center, half, _overlap, rot, blockMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    Collider other = _overlap[i];
                    if (IsOwn(other)) continue;
                    if (other is CharacterController) return false;   // a player in the doorway (not supported by ComputePenetration)
                    if (Physics.ComputePenetration(panel, pos, rot, other, other.transform.position, other.transform.rotation,
                            out _, out float depth) && depth > 0.001f)
                        return false;
                }
            }
            return true;
        }

        bool IsOwn(Collider c)
        {
            if (vehicleRoot == null) return false;
            if (c.transform.IsChildOf(vehicleRoot)) return true;
            if (c.attachedRigidbody != null && c.attachedRigidbody.transform == vehicleRoot) return true;
            // an item on one of this vehicle's stow points (the flashlight on the pilot door) is part of it (И3 L.4)
            var item = c.GetComponentInParent<Snow.Cargo.CargoItem>();
            return item != null && item.State == Snow.Cargo.CargoState.Stowed && item.Stow != null && item.Stow.transform.IsChildOf(vehicleRoot);
        }

        static Bounds LocalBounds(Collider c)
        {
            switch (c)
            {
                case BoxCollider b: return new Bounds(b.center, b.size);
                case MeshCollider m when m.sharedMesh != null: return m.sharedMesh.bounds;
                case SphereCollider s: return new Bounds(s.center, Vector3.one * s.radius * 2f);
                case CapsuleCollider cap: return new Bounds(cap.center, new Vector3(cap.radius * 2f, cap.height, cap.radius * 2f));
                default: return new Bounds(Vector3.zero, Vector3.one * 0.1f);
            }
        }
    }
}
