using Snow.Cargo;
using Snow.Character;
using Snow.Dev;
using Snow.Input;
using Snow.Interaction;
using Snow.Net;
using Snow.Seats;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Snow.Player
{
    /// <summary>
    /// Network side of a player (§11.3). The owner simulates his own character (NetworkTransform with owner authority);
    /// other copies keep only the CharacterController as a collider and are moved by the network. Every command to the
    /// world goes owner → this object → server check → state change (NET-02): discrete commands are reliable RPCs with a
    /// sequence number (a repeat is ignored), pilot axes are unreliable RPCs every tick.
    /// </summary>
    public class PlayerNet : NetworkBehaviour
    {
        public PlayerController controller;
        public HLCharacterMotor motor;
        public FirstPersonView view;
        public SnowInput humanInput;
        public SeatController seatController;
        public InteractionSensor sensor;
        public Renderer body;
        [Tooltip("Own body parts: the owner sees only their shadow (the camera sits inside the body)")] public Renderer[] hideForOwner;
        public Camera cam;

        [Header("Server plausibility check (TEMPORARY)")]
        [Tooltip("m/s; faster owner movement is logged as a violation")] public float maxPlausibleSpeed = 12f;

        static readonly CommandDeduplicator Dedup = new CommandDeduplicator();
        readonly NetworkVariable<bool> _seatedNet = new NetworkVariable<bool>(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        uint _seq;
        CargoItem _serverHeld;
        NetworkObject _platform;             // owner: the vehicle we asked to stand on
        Vector3 _lastServerPos;
        float _lastServerTime;
        SeatedVehicle _serverVehicle;
        int _serverSeat = -1;

        public int PlausibilityViolations { get; private set; }
        public static PlayerNet Local { get; private set; }
        public bool ServerSeatedNow => _serverVehicle != null;
        public bool Seated => _seatedNet.Value;

        static readonly Color[] Colors =
        {
            new Color(0.85f, 0.3f, 0.25f), new Color(0.25f, 0.55f, 0.9f), new Color(0.3f, 0.75f, 0.35f), new Color(0.9f, 0.75f, 0.2f),
        };

        public override void OnNetworkSpawn()
        {
            name = $"Player_{OwnerClientId}";
            if (body != null)
            {
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", Colors[(int)(OwnerClientId % (ulong)Colors.Length)]);
                body.SetPropertyBlock(block);
            }

            bool local = IsOwner;
            motor.enabled = local;
            controller.enabled = local;
            view.enabled = local;
            humanInput.enabled = local;
            seatController.enabled = local;
            sensor.enabled = local;
            if (cam != null)
            {
                cam.enabled = local && !Application.isBatchMode;
                var listener = cam.GetComponent<AudioListener>();
                if (listener != null) listener.enabled = cam.enabled;
            }

            if (local && hideForOwner != null)
                foreach (var r in hideForOwner)
                    if (r != null) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;

            if (local)
            {
                Local = this;
                BenchArgs args = SnowNetworkBootstrap.Args;
                IPlayerInputSource source = humanInput;
                if (args != null && !string.IsNullOrEmpty(args.Bot))
                {
                    var bot = gameObject.AddComponent<BenchBot>();
                    bot.Init(args.Bot, this);
                    source = bot;
                }
                controller.SetSource(source, InputContext.OnFoot);
                float yaw = transform.eulerAngles.y;
                view.SetYaw(yaw);
                motor.Teleport(transform.position, yaw, Vector3.zero);
                if (cam != null && cam.enabled)
                    foreach (var other in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                        if (other != cam && other.CompareTag("MainCamera")) other.gameObject.SetActive(false);
            }
            _lastServerPos = transform.position;
            _lastServerTime = Time.time;
            _seatedNet.OnValueChanged += (_, seated) => ShowSeated(seated);
            ShowSeated(_seatedNet.Value);
            BenchLog.Event("player_spawn", "owner", OwnerClientId, "local", local, "p", transform.position);
        }

        // every peer: a seated player shows a short capsule and has no collider (M4: no body mesh). Standing on a moving
        // vehicle also makes the player its child (§7.6), so "seated" is a replicated flag, not the parent.
        void ShowSeated(bool seated)
        {
            if (body != null)
            {
                float h = motor.tuning.standHeight;
                body.transform.localScale = new Vector3(motor.tuning.radius * 2f, seated ? 0.45f : h * 0.5f, motor.tuning.radius * 2f);
                body.transform.localPosition = Vector3.up * (HLCharacterMotor.SkinWidth + (seated ? 0.45f : h * 0.5f));
            }
            if (!IsOwner) motor.Controller.enabled = !seated;
        }

        public override void OnNetworkDespawn()
        {
            if (Local == this) Local = null;
            if (IsServer) Dedup.Forget(OwnerClientId);
            BenchLog.Event("player_despawn", "owner", OwnerClientId);
        }

        void Update()
        {
            // AT-HL1 evidence: how the server sees a remote player, every rendered frame
            if (IsSpawned && IsServer && !IsOwner && SnowNetworkBootstrap.Args != null && SnowNetworkBootstrap.Args.Trace)
                BenchLog.Event("rpos", "owner", OwnerClientId, "st", NetworkManager.ServerTime.Time, "p", transform.position);
        }

        void FixedUpdate()
        {
            if (IsSpawned && IsOwner && seatController.Seat == null) UpdatePlatform();
            if (IsSpawned && IsOwner && SnowNetworkBootstrap.Args != null && SnowNetworkBootstrap.Args.Trace)
                BenchLog.Event("opos", "st", NetworkManager.ServerTime.Time, "p", transform.position, "v", motor.Velocity,
                    "g", motor.Grounded, "c", motor.Crouched);
            if (!IsServer || !IsSpawned || IsOwner || _serverVehicle != null) return;
            // owner-authoritative movement: the server does not correct it yet, it counts implausible jumps (§11.3)
            float dt = Time.time - _lastServerTime;
            if (dt < 0.25f) return;
            float speed = Vector3.Distance(transform.position, _lastServerPos) / dt;
            if (speed > maxPlausibleSpeed)
            {
                PlausibilityViolations++;
                BenchLog.Event("implausible_move", "owner", OwnerClientId, "speed", speed);
            }
            _lastServerPos = transform.position;
            _lastServerTime = Time.time;
        }

        // ================================================================ owner → server
        /// <summary>Owner: ask the server to use a volume of a host.</summary>
        public void RequestUse(InteractionHost host, string id, UsePhase phase)
        {
            if (!IsOwner || host == null) return;
            uint seq = ++_seq;
            BenchLog.Event("use_request", "id", id, "phase", phase.ToString(), "seq", seq);
            UseRpc(host.NetworkObject, new FixedString64Bytes(id), phase, seq);
        }

        public void RequestExit()
        {
            if (!IsOwner) return;
            uint seq = ++_seq;
            BenchLog.Event("exit_request", "seq", seq);
            ExitSeatRpc(seq);
        }

        /// <summary>Owner: a discrete seat command (autopilot key); the server checks the seat and the conditions.</summary>
        public void RequestPilotCommand(PilotCommand command)
        {
            if (!IsOwner) return;
            uint seq = ++_seq;
            BenchLog.Event("pilot_command_request", "cmd", command.ToString(), "seq", seq);
            PilotCommandRpc(command, seq);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void PilotCommandRpc(PilotCommand command, uint seq, RpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            if (!Dedup.Accept(sender, seq)) return;
            string reason = "Вы не в кресле";
            bool ok = _serverVehicle != null && _serverVehicle.ServerPilotCommand(sender, command, out reason);
            BenchLog.Event("pilot_command", "client", sender, "cmd", command.ToString(), "ok", ok, "reason", reason);
            UseResultRpc(seq, ok, new FixedString128Bytes(reason ?? ""));
        }

        /// <summary>Owner: flight axes for this tick. Also used by bench bots to test the server gate (AT-02).</summary>
        public void SendAxes(ControlInput input)
        {
            if (IsOwner) AxesRpc(input);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void UseRpc(NetworkObjectReference target, FixedString64Bytes id, UsePhase phase, uint seq, RpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            string sid = id.ToString();
            if (!Dedup.Accept(sender, seq))
            {
                BenchLog.Event("use_duplicate", "client", sender, "id", sid, "seq", seq);
                return;
            }
            string reason;
            bool ok = false;
            if (!target.TryGet(out NetworkObject no) || !no.TryGetComponent(out InteractionHost host))
                reason = "Нет цели";
            else if (!host.InReach(sid, ServerUsePoint()))
                reason = "Слишком далеко";
            else if (!host.SideAllows(sid, ServerUsePoint()))
                reason = "С этой стороны не достать";
            else if (host.TryGetVolume(sid, out var zone) && !host.ZoneOpen(zone, _serverHeld))
                reason = "Нет действия";
            else
                ok = host.ServerUse(sender, sid, phase, out reason);
            BenchLog.Event("use", "client", sender, "id", sid, "phase", phase.ToString(), "seq", seq, "ok", ok, "reason", reason);
            UseResultRpc(seq, ok, new FixedString128Bytes(reason ?? ""));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void ExitSeatRpc(uint seq, RpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            if (!Dedup.Accept(sender, seq)) return;
            if (_serverVehicle == null)
            {
                UseResultRpc(seq, false, new FixedString128Bytes("Вы не в кресле"));
                return;
            }
            if (!_serverVehicle.ServerExit(sender, out Vector3 pos, out float yaw, out Vector3 vel, out string reason))
            {
                BenchLog.Event("exit_refused", "client", sender, "reason", reason);
                UseResultRpc(seq, false, new FixedString128Bytes(reason));
                return;
            }
            _serverVehicle = null;
            _serverSeat = -1;
            _seatedNet.Value = false;
            _lastServerPos = pos;
            _lastServerTime = Time.time;
            ExitApprovedRpc(pos, yaw, vel);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner, Delivery = RpcDelivery.Unreliable)]
        void AxesRpc(ControlInput input, RpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            if (_serverVehicle != null) _serverVehicle.ServerAxes(sender, input);
            else BenchLog.Event("axes_not_seated", "client", sender);
        }

        // ---------------------------------------------------------------- cargo (GRAB-01, SECURE-01)
        public void RequestGrab(CargoItem item, Vector3 localPoint)
        {
            if (!IsOwner || item == null) return;
            uint seq = ++_seq;
            GrabRpc(item.NetworkObject, localPoint, seq);
        }

        public void RequestRelease(CargoItem item)
        {
            if (!IsOwner || item == null) return;
            ReleaseRpc(item.NetworkObject, ++_seq);
        }

        /// <summary>Owner, every network tick while holding: where the hand is.</summary>
        public void SendGrabTarget(CargoItem item, Vector3 target)
        {
            if (IsOwner && item != null) GrabTargetRpc(item.NetworkObject, target);
        }

        public void RequestSecure(CargoItem item, bool secure)
        {
            if (!IsOwner || item == null) return;
            uint seq = ++_seq;
            BenchLog.Event("secure_request", "item", item.name, "secure", secure, "seq", seq);
            SecureRpc(item.NetworkObject, secure, seq);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void GrabRpc(NetworkObjectReference itemRef, Vector3 localPoint, uint seq, RpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            if (!Dedup.Accept(sender, seq)) return;
            string reason = null;
            bool ok = false;
            if (!itemRef.TryGet(out NetworkObject no) || !no.TryGetComponent(out CargoItem item)) reason = "Нет цели";
            else if (Vector3.Distance(ServerUsePoint(), item.transform.TransformPoint(localPoint)) > GrabMath.MaxHold + 0.6f) reason = "Слишком далеко";
            else
            {
                if (_serverHeld != null && _serverHeld != item) _serverHeld.ServerRelease(sender);   // one item per player
                ok = item.ServerGrab(sender, localPoint, out reason);
                if (ok) _serverHeld = item;
            }
            UseResultRpc(seq, ok, new FixedString128Bytes(reason ?? ""));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void ReleaseRpc(NetworkObjectReference itemRef, uint seq, RpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            if (!Dedup.Accept(sender, seq)) return;
            if (itemRef.TryGet(out NetworkObject no) && no.TryGetComponent(out CargoItem item)) item.ServerRelease(sender);
            if (_serverHeld != null && _serverHeld.NetworkObject == no) _serverHeld = null;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner, Delivery = RpcDelivery.Unreliable)]
        void GrabTargetRpc(NetworkObjectReference itemRef, Vector3 target, RpcParams rpc = default)
        {
            if (!itemRef.TryGet(out NetworkObject no) || !no.TryGetComponent(out CargoItem item)) return;
            item.ServerTarget(rpc.Receive.SenderClientId, GrabMath.ClampTarget(ServerUsePoint(), target));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void SecureRpc(NetworkObjectReference itemRef, bool secure, uint seq, RpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            if (!Dedup.Accept(sender, seq)) return;
            string reason = null;
            bool ok = false;
            CargoItem item = null;
            if (!itemRef.TryGet(out NetworkObject no) || !no.TryGetComponent(out item)) reason = "Нет цели";
            else if (Vector3.Distance(ServerUsePoint(), item.transform.position) > GrabMath.MaxHold + 1.0f) reason = "Слишком далеко";
            else if (item.TryGetComponent(out Snow.Aircraft.PlaneKey key)) ok = key.ServerPocket(sender, out reason);   // R on the key: into the pocket
            else ok = item.ServerSecure(sender, secure, out reason);
            if (ok && secure && _serverHeld == item) _serverHeld = null;
            UseResultRpc(seq, ok, new FixedString128Bytes(reason ?? ""));
        }

        /// <summary>Owner: the plane key out of the pocket onto the ground (G).</summary>
        public void RequestDropKey()
        {
            if (!IsOwner) return;
            DropKeyRpc(++_seq);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void DropKeyRpc(uint seq, RpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            if (!Dedup.Accept(sender, seq)) return;
            string reason = "Ключа в кармане нет";
            bool ok = false;
            foreach (var key in Snow.Aircraft.PlaneKey.All)
                if (key.Holder == sender) { ok = key.ServerDrop(sender, out reason); break; }
            UseResultRpc(seq, ok, new FixedString128Bytes(reason ?? ""));
        }

        // ---------------------------------------------------------------- standing on a moving vehicle (§7.6, TEMPORARY)
        // The owner reports the vehicle under his feet; the server makes the player its child (NGO parenting is server
        // only), so he rides with it and his own movement stays relative to it. Leaving: back to world space.
        void UpdatePlatform()
        {
            if (!motor.Grounded) return;
            Collider ground = motor.GroundCollider;
            SeatedVehicle v = ground != null ? ground.GetComponentInParent<SeatedVehicle>() : null;
            NetworkObject want = v != null && v.GetComponent<Rigidbody>() != null ? v.NetworkObject : null;
            if (want == _platform) return;
            _platform = want;
            PlatformRpc(want != null ? new NetworkObjectReference(want) : default, want != null, ++_seq);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void PlatformRpc(NetworkObjectReference vehicle, bool on, uint seq, RpcParams rpc = default)
        {
            if (!Dedup.Accept(rpc.Receive.SenderClientId, seq) || _serverVehicle != null) return;
            if (on && vehicle.TryGet(out NetworkObject v))
            {
                if (Vector3.Distance(transform.position, v.transform.position) > 15f) return;   // sanity
                NetworkObject.TrySetParent(v, true);
            }
            else if (!on) NetworkObject.TryRemoveParent(true);
            BenchLog.Event("platform", "client", rpc.Receive.SenderClientId, "on", on);
        }

        /// <summary>Server: where the use ray of this player starts, as the server sees it.</summary>
        Vector3 ServerUsePoint()
        {
            if (_serverVehicle != null && _serverSeat >= 0) return _serverVehicle.Seat(_serverSeat).eye.position;
            return transform.position + Vector3.up * 1.4f;
        }

        // ================================================================ server → owner
        /// <summary>Server: the vehicle has seated this player.</summary>
        public void ServerSeated(SeatedVehicle vehicle, int seat)
        {
            if (_serverHeld != null) { _serverHeld.ServerRelease(OwnerClientId); _serverHeld = null; }
            _serverVehicle = vehicle;
            _serverSeat = seat;
            _seatedNet.Value = true;
            EnterSeatRpc(vehicle.NetworkObject, seat);
        }

        [Rpc(SendTo.Owner)]
        void EnterSeatRpc(NetworkObjectReference vehicle, int seat)
        {
            if (!vehicle.TryGet(out NetworkObject no) || !no.TryGetComponent(out SeatedVehicle v)) return;
            seatController.Enter(v.Seat(seat));
        }

        [Rpc(SendTo.Owner)]
        void ExitApprovedRpc(Vector3 position, float yaw, Vector3 velocity)
        {
            seatController.Leave(position, yaw, velocity);
        }

        [Rpc(SendTo.Owner)]
        void UseResultRpc(uint seq, bool ok, FixedString128Bytes reason)
        {
            string r = reason.ToString();
            BenchLog.Event("use_result", "seq", seq, "ok", ok, "reason", r);
            if (!ok && sensor != null) sensor.ShowMessage(string.IsNullOrEmpty(r) ? "Нельзя" : r);
        }
    }
}
