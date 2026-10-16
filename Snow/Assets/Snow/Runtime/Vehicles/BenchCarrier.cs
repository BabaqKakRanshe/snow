using Snow.Doors;
using Snow.Interaction;
using Snow.Net;
using Snow.Seats;
using Unity.Netcode;
using UnityEngine;

namespace Snow.Vehicles
{
    /// <summary>
    /// P02 test vehicle (§14 P02, NET-03): a driven rigid body with a hollow cargo bay, a side door, two seats and tie-down
    /// sockets, used to measure network physics of a vehicle carrying loose, carried and secured cargo. It is a test rig,
    /// not the truck of P09. Server simulates it (variant A); clients see a kinematic copy. Forces only (PHYS-01): four
    /// ray suspensions with a friction circle; W/S drive, A/D steer (the pilot axes). No damping, explicit mass properties.
    /// TEMPORARY engine: the cab button cranks it for crankTime, then it runs; drive force only while it runs (29.09
    /// feedback "it does not start, it just drives"). The real start chain (§9.4) is the plane's, see Snow.Aircraft.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class BenchCarrier : SeatedVehicle
    {
        public const string ButtonBay = "Button_BayDoor";
        public const string HandleOut = "BayDoor_Handle_Out";
        public const string HandleIn = "BayDoor_Handle_In";
        public const string ButtonEngine = "Button_Engine";

        public enum EngineState : byte { Off, Cranking, Running }

        public NetworkDoor bayDoor;
        [Tooltip("Wheel centres at rest; 0,1 front (steered), 2,3 rear (driven)")] public Transform[] wheels;

        [Header("TEMPORARY (Docs/Tuning/Cargo.md)")]
        public float massKg = 1600f;
        public Vector3 centerOfMass = new Vector3(0f, 0.55f, 0.2f);
        [Tooltip("kg·m² about local X (pitch), Y (yaw), Z (roll)")] public Vector3 inertia = new Vector3(4200f, 4500f, 1300f);
        public float wheelRadius = 0.35f;
        public float suspensionTravel = 0.3f;
        [Tooltip("N/m per wheel")] public float springRate = 45000f;
        [Tooltip("N·s/m per wheel")] public float damperRate = 4500f;
        [Tooltip("N total at full W")] public float maxDrive = 6000f;
        public float maxSteerDeg = 25f;
        [Tooltip("tyre friction coefficient")] public float grip = 0.9f;
        public float rollingResistance = 0.015f;
        [Tooltip("s of cranking before the engine runs")] public float crankTime = 1.2f;

        readonly NetworkVariable<EngineState> _engine = new NetworkVariable<EngineState>(EngineState.Off,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        float _crankUntil;
        Rigidbody _rb;
        float[] _compression;
        readonly RaycastHit[] _hits = new RaycastHit[8];

        public Rigidbody Body => _rb;

        protected override void Awake()
        {
            base.Awake();
            _rb = GetComponent<Rigidbody>();
            _rb.mass = massKg;
            _rb.automaticCenterOfMass = false;
            _rb.centerOfMass = centerOfMass;
            _rb.automaticInertiaTensor = false;
            _rb.inertiaTensor = inertia;
            _rb.inertiaTensorRotation = Quaternion.identity;
            _rb.linearDamping = 0f;                               // PHYS-01: no hidden stabiliser
            _rb.angularDamping = 0f;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _compression = new float[wheels.Length];
            if (bayDoor != null) bayDoor.PowerSource = () => true;   // TEMPORARY: no electrics on the bench
        }

        protected override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!IsServer || !IsSpawned) return;
            ControlState c = Controls;
            if (_engine.Value == EngineState.Cranking && Time.time >= _crankUntil)
            {
                _engine.Value = EngineState.Running;
                BenchLog.Event("engine", "state", "Running");
            }
            float pitch = _engine.Value == EngineState.Running ? c.Pitch : 0f;   // no drive without a running engine
            float dt = Time.fixedDeltaTime;
            Vector3 up = transform.up;
            float steer = c.Roll * maxSteerDeg;
            for (int i = 0; i < wheels.Length; i++)
            {
                Vector3 origin = wheels[i].position;
                if (!CastWheel(origin, -up, wheelRadius + suspensionTravel, out RaycastHit hit))
                {
                    _compression[i] = 0f;
                    continue;
                }
                float compression = Mathf.Clamp(wheelRadius + suspensionTravel - hit.distance, 0f, suspensionTravel);
                float rate = (compression - _compression[i]) / dt;
                _compression[i] = compression;
                float load = Mathf.Max(0f, springRate * compression + damperRate * rate);

                Vector3 fwd = transform.forward;
                if (i < 2) fwd = Quaternion.AngleAxis(steer, up) * fwd;
                fwd = Vector3.ProjectOnPlane(fwd, hit.normal).normalized;
                Vector3 side = Vector3.Cross(hit.normal, fwd).normalized;
                Vector3 v = _rb.GetPointVelocity(hit.point);
                float vLong = Vector3.Dot(v, fwd), vLat = Vector3.Dot(v, side);

                // Tyre: stop this wheel's share of side slip in one step AND carry the steady side load (the tilted
                // suspension force and gravity), within the friction circle. A velocity-only model let the parked rig
                // creep sideways (seen 29.09 in the bench log). Rolling resistance fades in over 0.1 m/s (no sign flip).
                float share = _rb.mass / wheels.Length;
                Vector3 steady = up * load + Physics.gravity * share;
                float lat = -vLat * share / dt - Vector3.Dot(steady, side);
                float drive = i >= 2 ? pitch * maxDrive * 0.5f : 0f;
                float lon = drive - Mathf.Clamp(vLong / 0.1f, -1f, 1f) * rollingResistance * load;
                // TEMPORARY test-rig simplification: with no drive input and nearly stopped the wheels hold (in gear)
                if (Mathf.Abs(pitch) < 0.01f && Mathf.Abs(vLong) < 0.3f) lon = -vLong * share / dt - Vector3.Dot(steady, fwd);
                Vector2 tyre = Vector2.ClampMagnitude(new Vector2(lon, lat), grip * load);   // friction circle
                _rb.AddForceAtPosition(up * load + fwd * tyre.x + side * tyre.y, hit.point, ForceMode.Force);
            }
        }

        bool CastWheel(Vector3 origin, Vector3 dir, float length, out RaycastHit best)
        {
            best = default;
            int mask = ~(1 << LayerMask.NameToLayer("Interact"));
            int n = Physics.RaycastNonAlloc(origin, dir, _hits, length, mask, QueryTriggerInteraction.Ignore);
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                if (_hits[i].collider.attachedRigidbody == _rb) continue;   // own body
                if (_hits[i].collider.attachedRigidbody != null && !_hits[i].collider.attachedRigidbody.isKinematic
                    && _hits[i].collider.attachedRigidbody.mass < 200f) continue;   // do not drive on crates
                if (!found || _hits[i].distance < best.distance) { best = _hits[i]; found = true; }
            }
            return found;
        }

        // ---------------------------------------------------------------- interaction
        public override bool ServerUse(ulong clientId, string id, UsePhase phase, out string reason)
        {
            reason = null;
            if (phase != UsePhase.Press) return true;
            switch (id)
            {
                case ButtonBay: bayDoor.Toggle(DoorDrive.Powered); return true;
                case HandleOut:
                case HandleIn: bayDoor.Toggle(DoorDrive.Manual); return true;
                case ButtonEngine:
                    if (_engine.Value == EngineState.Off)
                    {
                        _engine.Value = EngineState.Cranking;
                        _crankUntil = Time.time + crankTime;
                    }
                    else _engine.Value = EngineState.Off;
                    BenchLog.Event("engine", "state", _engine.Value.ToString(), "client", clientId);
                    return true;
            }
            int seat = SeatIndex(id);
            if (seat >= 0) return ServerSit(clientId, seat, out reason);
            reason = "Нет действия";
            return false;
        }

        public override string Describe(string id)
        {
            int seat = SeatIndex(id);
            if (seat >= 0) return SeatText(seat);
            if (id == ButtonEngine)
                return _engine.Value == EngineState.Running ? "работает" : _engine.Value == EngineState.Cranking ? "стартер" : "заглушен";
            switch (bayDoor.State)
            {
                case DoorState.Closed: return "закрыта";
                case DoorState.Opening: return "открывается";
                case DoorState.Open: return "открыта";
                case DoorState.Closing: return "закрывается";
                default: return "упёрлась";
            }
        }

        public override string ActionText(string id)
        {
            if (SeatIndex(id) >= 0) return "сесть";
            if (id == ButtonEngine) return _engine.Value == EngineState.Off ? "запустить" : "заглушить";
            bool open = bayDoor.State == DoorState.Open || bayDoor.State == DoorState.Opening;
            return open ? "закрыть" : "открыть";
        }
    }
}
