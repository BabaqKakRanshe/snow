using Snow.Character;
using Snow.Doors;
using Snow.Interaction;
using Snow.Net;
using Snow.Player;
using Unity.Netcode;
using UnityEngine;

namespace Snow.Seats
{
    /// <summary>
    /// Seats of a networked vehicle (SEAT-01, SEAT-02, §7.5, §11.3). The server decides who sits where, parents the
    /// player object to the vehicle, checks the exit (own-side door open, room for the capsule), and accepts flight
    /// axes only from the pilot seat. The pilot leaving or dropping out zeroes the axes; the throttle stays (O-05).
    /// </summary>
    public abstract class SeatedVehicle : InteractionHost
    {
        public SeatAnchor[] seats;
        [Tooltip("Throttle lever travel per second at full rate, 0..1 per s (TEMPORARY)")] public float throttleSpeed = 0.5f;
        [Tooltip("s without pilot input before the axes return to zero (lost packets, focus loss)")] public float inputTimeout = 0.3f;
        [Tooltip("Trim wheel travel per second at full rate, of the -1..1 range (TEMPORARY)")] public float trimSpeed = 0.25f;

        protected SeatRegistry Registry;
        readonly NetworkList<ulong> _occupants = new NetworkList<ulong>();
        readonly NetworkVariable<ControlState> _controls = new NetworkVariable<ControlState>(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        ControlInput _input;
        float _inputTime = -10f;

        public int RejectedAxes { get; private set; }
        public ControlState Controls => _controls.Value;

        protected override void Awake()
        {
            base.Awake();
            var roles = new SeatRole[seats.Length];
            for (int i = 0; i < seats.Length; i++)
            {
                seats[i].index = i;
                roles[i] = seats[i].role;
            }
            Registry = new SeatRegistry(roles);
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                if (_occupants.Count == 0)
                    for (int i = 0; i < seats.Length; i++) _occupants.Add(SeatRegistry.Empty);
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnect;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= OnClientDisconnect;
        }

        public ulong OccupantOf(int seat) => seat < _occupants.Count ? _occupants[seat] : SeatRegistry.Empty;
        public SeatAnchor Seat(int index) => seats[index];

        public int SeatIndex(string id)
        {
            for (int i = 0; i < seats.Length; i++)
                if (seats[i].seatId == id) return i;
            return -1;
        }

        // ---------------------------------------------------------------- server
        protected bool ServerSit(ulong client, int seat, out string reason)
        {
            if (!Registry.TryOccupy(seat, client, out reason)) return false;
            NetworkObject player = NetworkManager.ConnectedClients.TryGetValue(client, out var cc) ? cc.PlayerObject : null;
            if (player == null || !player.TrySetParent(NetworkObject, true))
            {
                Registry.Release(client);
                reason = "Не удалось сесть";
                return false;
            }
            _occupants[seat] = client;
            player.GetComponent<PlayerNet>().ServerSeated(this, seat);
            BenchLog.Event("seat_enter", "client", client, "seat", seats[seat].seatId, "role", seats[seat].role.ToString());
            return true;
        }

        /// <summary>Server: validate and perform an exit. On success the player is unparented; the caller moves him.</summary>
        public bool ServerExit(ulong client, out Vector3 position, out float yaw, out Vector3 velocity, out string reason)
        {
            position = default; yaw = 0f; velocity = default; reason = null;
            int s = Registry.SeatOf(client);
            if (s < 0) { reason = "Вы не в кресле"; return false; }
            SeatAnchor a = seats[s];
            if (a.exitDoor != null && a.exitDoor.State != DoorState.Open) { reason = "Дверь закрыта"; return false; }
            yaw = a.exit.eulerAngles.y;
            if (!FindExit(a, out position))
            {
                reason = "Нет места для выхода: снаружи у двери препятствие";
                return false;
            }

            Registry.Release(client);
            _occupants[s] = SeatRegistry.Empty;
            if (a.role == SeatRole.Pilot) _input = default;
            OnSeatReleased(client, s);
            var rb = GetComponentInParent<Rigidbody>();
            velocity = rb != null ? rb.GetPointVelocity(position) : Vector3.zero;   // SEAT-02: keep the vehicle's speed
            NetworkObject player = NetworkManager.ConnectedClients[client].PlayerObject;
            player.TryRemoveParent(true);
            BenchLog.Event("seat_exit", "client", client, "seat", a.seatId, "p", position);
            return true;
        }

        /// <summary>
        /// Where the player stands after getting out. The model's exit socket is "on the ground" only for the empty
        /// aircraft at rest: loaded, it sits ~4 cm lower on its suspension and the socket is under the ground, which made
        /// every exit fail the capsule test by 2 mm (30.09, reproduced in the scene). So: the real ground under the
        /// socket (the vehicle's own colliders ignored), and if that spot is taken, spots further out from the side.
        /// In flight there is no ground within reach: the socket itself (SEAT-02: out at once, keeping the speed).
        /// </summary>
        bool FindExit(SeatAnchor a, out Vector3 feet)
        {
            Vector3 socket = a.exit.position;
            Vector3 outward = socket - transform.position;
            outward.y = 0f;
            outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : -a.exit.right;
            foreach (float step in ExitSteps)
            {
                feet = OnGround(socket + outward * step);
                if (ExitClear(feet)) return true;
            }
            feet = socket;
            BenchLog.Event("exit_blocked", "seat", a.seatId, "socket", socket);
            return false;
        }

        static readonly float[] ExitSteps = { 0f, 0.35f, 0.7f, 1.1f };

        Vector3 OnGround(Vector3 p)
        {
            int mask = ~(1 << LayerMask.NameToLayer("Interact"));
            var hits = Physics.RaycastAll(p + Vector3.up * 1.2f, Vector3.down, 2.4f, mask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Vector3 ground = p;
            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(transform) || h.collider is CharacterController) continue;
                if (h.normal.y < 0.6f || h.distance >= best) continue;   // not something to stand on
                best = h.distance;
                ground = h.point + Vector3.up * 0.03f;
            }
            return ground;
        }

        static bool ExitClear(Vector3 feet)
        {
            var t = new MovementTuning();
            float r = t.radius;
            int mask = ~(1 << LayerMask.NameToLayer("Interact"));
            Vector3 a = feet + Vector3.up * (HLCharacterMotor.SkinWidth + r + 0.02f);
            Vector3 b = feet + Vector3.up * (HLCharacterMotor.SkinWidth + t.standHeight - r);
            return !Physics.CheckCapsule(a, b, r, mask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Server: pilot axes. From anyone else they are counted and dropped (AT-02).</summary>
        public void ServerAxes(ulong client, ControlInput input)
        {
            if (!Registry.IsPilot(client))
            {
                RejectedAxes++;
                if (RejectedAxes % 30 == 1) BenchLog.Event("axes_rejected", "client", client, "count", RejectedAxes);
                return;
            }
            _input = input.Clamped();
            _inputTime = Time.time;
        }

        protected virtual void FixedUpdate()
        {
            if (!IsServer || !IsSpawned) return;
            if (Time.time - _inputTime > inputTimeout) _input = default;
            ControlState c = _controls.Value;
            var next = new ControlState
            {
                Pitch = _input.Pitch,
                Roll = _input.Roll,
                Yaw = _input.Yaw,
                Throttle = Mathf.Clamp01(c.Throttle + _input.ThrottleRate * throttleSpeed * Time.fixedDeltaTime),
                Brake = _input.Brake,
                Trim = Mathf.Clamp(c.Trim + _input.TrimRate * trimSpeed * Time.fixedDeltaTime, -1f, 1f),
            };
            if (!next.Equals(c)) _controls.Value = next;
        }

        /// <summary>Server: set the trim wheel directly (cursor on the wheel, checkpoint restore).</summary>
        protected void ServerSetTrim(float trim)
        {
            ControlState c = _controls.Value;
            c.Trim = Mathf.Clamp(trim, -1f, 1f);
            _controls.Value = c;
        }

        /// <summary>Server: a discrete command from a seat (e.g. the autopilot key). Vehicles without it refuse.</summary>
        public virtual bool ServerPilotCommand(ulong client, PilotCommand command, out string reason)
        {
            reason = "Здесь этого нет";
            return false;
        }

        /// <summary>Server: a seat was freed by an exit or a disconnect (release what that player was holding).</summary>
        protected virtual void OnSeatReleased(ulong client, int seat) { }

        void OnClientDisconnect(ulong client)
        {
            int s = Registry.Release(client);
            if (s < 0) return;
            _occupants[s] = SeatRegistry.Empty;
            if (seats[s].role == SeatRole.Pilot) _input = default;
            OnSeatReleased(client, s);
            BenchLog.Event("seat_cleared", "client", client, "seat", seats[s].seatId, "reason", "disconnect");
        }

        public string SeatText(int seat)
        {
            ulong o = OccupantOf(seat);
            return o == SeatRegistry.Empty ? "свободно" : $"занято (игрок {o})";
        }
    }
}
