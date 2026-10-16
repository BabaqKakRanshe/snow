using Snow.Cargo;
using Snow.Doors;
using Snow.Player;
using Snow.Seats;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Snow.Net
{
    /// <summary>
    /// Starts the session (CORE-05: one host plus up to three clients) from BenchArgs or from a small developer menu,
    /// approves connections with a server-chosen spawn point, and writes the bench evidence log (connections, RTT,
    /// player positions once per second).
    /// </summary>
    [RequireComponent(typeof(NetworkManager), typeof(UnityTransport))]
    public class SnowNetworkBootstrap : MonoBehaviour
    {
        public const int MaxPlayers = 4;

        public Transform[] spawnPoints;
        [Tooltip("Seconds between snapshot and RTT lines in the bench log")] public float logInterval = 1f;

        public static BenchArgs Args { get; private set; }

        NetworkManager _nm;
        UnityTransport _ut;
        float _nextLog;
        string _menuAddress = "127.0.0.1";
        string _menuDelay = "0";
        string _menuPort;
        string _menuError;
        int _spawned;

        void Awake()
        {
            Args = BenchArgs.Read();
            Application.runInBackground = true;                   // several game processes on one machine
            if (Application.isBatchMode) Application.targetFrameRate = 60;
            _nm = GetComponent<NetworkManager>();
            _ut = GetComponent<UnityTransport>();
            _nm.NetworkConfig.ConnectionApproval = true;
            _nm.ConnectionApprovalCallback = Approve;
            _nm.OnClientConnectedCallback += id => BenchLog.Event("connected", "client", id);
            _nm.OnClientDisconnectCallback += id => BenchLog.Event("disconnected", "client", id, "reason", _nm.DisconnectReason);
            BenchLog.Open(Args.LogPath);
            BenchLog.Event("start", "args", Args.ToString(), "batch", Application.isBatchMode, "fixedDt", Time.fixedDeltaTime,
                "tickRate", _nm.NetworkConfig.TickRate);
        }

        void Start()
        {
            if (Args.Host) StartHost();
            else if (Args.Client) StartClient(Args.Address);
        }

        void OnDestroy() => BenchLog.Close();

        public void StartHost()
        {
            Configure();
            _ut.SetConnectionData("127.0.0.1", Args.Port, "0.0.0.0");
            BenchLog.Role = "host";
            bool ok = _nm.StartHost();
            // a port still held (another game process, or a socket the editor leaked when a session was torn down by a
            // script reload in Play Mode) makes the bind fail: say so in the menu instead of only in the console
            _menuError = ok ? null : $"Порт {Args.Port} занят. Закройте другой экземпляр игры, перезапустите редактор или укажите другой порт.";
            BenchLog.ClientId = _nm.LocalClientId;
            BenchLog.Event("start_host", "ok", ok, "port", Args.Port);
        }

        public void StartClient(string address)
        {
            Configure();
            _ut.SetConnectionData(address, Args.Port);
            BenchLog.Role = "client";
            bool ok = _nm.StartClient();
            BenchLog.Event("start_client", "ok", ok, "address", address, "port", Args.Port);
        }

        void Configure()
        {
            UnityTransport.s_DriverConstructor = Args.Simulate
                ? new SimulatedDriverConstructor(Args.SimDelayMs, Args.SimJitterMs, Args.SimLossPercent)
                : null;
        }

        void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            int players = _nm.ConnectedClientsIds.Count;
            response.Approved = players < MaxPlayers;
            response.Reason = response.Approved ? null : $"Session is full ({MaxPlayers} players)";
            response.CreatePlayerObject = response.Approved;
            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                Transform sp = spawnPoints[_spawned++ % spawnPoints.Length];
                response.Position = sp.position;
                response.Rotation = sp.rotation;
            }
            BenchLog.Event("approve", "client", request.ClientNetworkId, "approved", response.Approved, "players", players);
        }

        void Update()
        {
            if (Args.QuitAfter > 0f && Time.realtimeSinceStartup > Args.QuitAfter)
            {
                BenchLog.Event("quit", "after", Args.QuitAfter);
                Snapshot();
                if (_nm.IsListening) _nm.Shutdown();
                Application.Quit();
                enabled = false;
                return;
            }
            if (!_nm.IsListening || Time.realtimeSinceStartup < _nextLog) return;
            _nextLog = Time.realtimeSinceStartup + logInterval;
            if (_nm.IsServer)
            {
                foreach (ulong id in _nm.ConnectedClientsIds)
                    if (id != NetworkManager.ServerClientId) BenchLog.Event("rtt", "client", id, "ms", _ut.GetCurrentRtt(id));
            }
            else if (_nm.IsConnectedClient)
            {
                BenchLog.Event("rtt", "client", _nm.LocalClientId, "ms", _ut.GetCurrentRtt(NetworkManager.ServerClientId));
            }
            Snapshot();
        }

        /// <summary>Every player object as this process sees it (own one simulated, others interpolated).</summary>
        void Snapshot()
        {
            foreach (var p in FindObjectsByType<PlayerNet>(FindObjectsSortMode.None))
            {
                if (!p.IsSpawned) continue;
                BenchLog.Event("pos", "owner", p.OwnerClientId, "local", p.IsOwner, "p", p.transform.position,
                    "tick", _nm.NetworkTickSystem != null ? _nm.NetworkTickSystem.ServerTime.Tick : -1);
            }
            foreach (var c in FindObjectsByType<CargoItem>(FindObjectsSortMode.None))
                if (c.IsSpawned)
                    BenchLog.Event("cargo", "item", c.name, "state", c.State.ToString(), "carriers", c.Carriers, "socket", c.SocketId,
                        "p", c.transform.position, "r", c.transform.eulerAngles, "v", c.Body.linearVelocity);
            foreach (var d in FindObjectsByType<NetworkDoor>(FindObjectsSortMode.None))
                if (d.IsSpawned) BenchLog.Event("door", "door", d.doorId, "state", d.State.ToString(), "pos", d.Position);
            foreach (var v in FindObjectsByType<SeatedVehicle>(FindObjectsSortMode.None))
            {
                if (!v.IsSpawned) continue;
                ControlState c = v.Controls;
                BenchLog.Event("vehicle", "name", v.name, "seat0", (long)(v.OccupantOf(0) == SeatRegistry.Empty ? -1 : (long)v.OccupantOf(0)),
                    "seat1", (long)(v.OccupantOf(1) == SeatRegistry.Empty ? -1 : (long)v.OccupantOf(1)),
                    "pitch", c.Pitch, "roll", c.Roll, "yaw", c.Yaw, "throttle", c.Throttle, "rejectedAxes", v.RejectedAxes,
                    "p", v.transform.position, "r", v.transform.eulerAngles);
            }
        }

        void OnGUI()
        {
            if (Args == null || Args.Automatic || _nm == null || _nm.IsListening) return;
            GUILayout.BeginArea(new Rect(20, 20, 300, 300), "Snow network bench (dev)", GUI.skin.window);
            if (_menuPort == null) _menuPort = Args.Port.ToString();
            GUILayout.Label("Port (host and join)");
            _menuPort = GUILayout.TextField(_menuPort);
            if (ushort.TryParse(_menuPort, out ushort port)) Args.Port = port;
            if (GUILayout.Button("Host")) StartHost();
            GUILayout.Label("Server address");
            _menuAddress = GUILayout.TextField(_menuAddress);
            GUILayout.Label("Simulated delay each way, ms (this process)");
            _menuDelay = GUILayout.TextField(_menuDelay);
            if (GUILayout.Button("Join"))
            {
                int.TryParse(_menuDelay, out Args.SimDelayMs);
                StartClient(_menuAddress);
            }
            if (!string.IsNullOrEmpty(_menuError))
            {
                GUI.color = new Color(1f, 0.6f, 0.4f);
                GUILayout.Label(_menuError);
                GUI.color = Color.white;
            }
            GUILayout.EndArea();
        }
    }
}
