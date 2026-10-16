using System.Collections.Generic;
using Snow.Cargo;
using Snow.Net;
using Unity.Netcode;
using UnityEngine;

namespace Snow.Aircraft
{
    /// <summary>
    /// The key of the plane's central lock (§8.3, NEW-05; model v7 PlaneKey). A physical network item (CargoItem: it is
    /// carried by grab like cargo) that can also go into a player's pocket — TEMPORARY one pocket for the key, no UI
    /// (O-03). The server decides who holds it: Holder is the client id, or None while it lies in the world. In a pocket
    /// it is hidden, has no collisions and travels with the player; dropped, it lies at his feet. Nothing duplicates it:
    /// there is exactly one key object and only the server moves it between pocket and world.
    /// </summary>
    [RequireComponent(typeof(CargoItem))]
    public class PlaneKey : NetworkBehaviour
    {
        public const ulong None = ulong.MaxValue;
        static readonly List<PlaneKey> AllKeys = new List<PlaneKey>();

        readonly NetworkVariable<ulong> _holder = new NetworkVariable<ulong>(None,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        CargoItem _item;
        Rigidbody _rb;
        Collider[] _colliders;
        Renderer[] _renderers;

        public ulong Holder => _holder.Value;
        public CargoItem Item => _item;
        public static IReadOnlyList<PlaneKey> All => AllKeys;

        /// <summary>Does this client have a key: in his pocket or in his hands (grabbed)?</summary>
        public static bool ClientHasKey(ulong client)
        {
            foreach (var k in AllKeys)
            {
                if (k.Holder == client) return true;
                var item = k._item;
                for (int i = 0; item != null && item.IsSpawned && i < item.Carriers; i++)
                    if (item.Grab(i).Client == client) return true;
            }
            return false;
        }

        void Awake()
        {
            _item = GetComponent<CargoItem>();
            _rb = GetComponent<Rigidbody>();
            _colliders = GetComponentsInChildren<Collider>(true);
            _renderers = GetComponentsInChildren<Renderer>(true);
        }

        void OnEnable() => AllKeys.Add(this);
        void OnDisable() => AllKeys.Remove(this);

        public override void OnNetworkSpawn()
        {
            _holder.OnValueChanged += (_, h) => Show(h);
            Show(_holder.Value);
            if (IsServer) NetworkManager.OnClientDisconnectCallback += OnDisconnect;
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= OnDisconnect;
        }

        void Show(ulong holder)
        {
            bool inPocket = holder != None;
            foreach (var r in _renderers) if (r != null) r.enabled = !inPocket;
            foreach (var c in _colliders) if (c != null) c.enabled = !inPocket;
            if (IsServer && _rb != null) _rb.isKinematic = inPocket;
        }

        // ================================================================ server
        /// <summary>Server: into this client's pocket (from the world or from his hands).</summary>
        public bool ServerPocket(ulong client, out string reason)
        {
            reason = null;
            if (Holder == client) { reason = "Ключ уже в кармане"; return false; }
            if (Holder != None) { reason = "Ключ у другого игрока"; return false; }
            if (_item.State == CargoState.Secured) _item.ServerSecure(client, false, out _);
            for (int i = _item.Carriers - 1; i >= 0; i--) _item.ServerRelease(_item.Grab(i).Client);
            _holder.Value = client;
            Show(client);
            BenchLog.Event("key_pocket", "client", client);
            return true;
        }

        /// <summary>Server: out of the pocket, onto the ground in front of the player.</summary>
        public bool ServerDrop(ulong client, out string reason)
        {
            reason = null;
            if (Holder != client) { reason = "Ключа в кармане нет"; return false; }
            Transform p = PlayerTransform(client);
            Vector3 at = p != null ? p.position + p.forward * 0.45f + Vector3.up * 0.4f : transform.position;
            _holder.Value = None;
            Show(None);
            _rb.position = at;
            transform.position = at;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            BenchLog.Event("key_drop", "client", client, "p", at);
            return true;
        }

        void OnDisconnect(ulong client)
        {
            if (Holder == client) ServerDrop(client, out _);   // a player who left does not take the key out of the game
        }

        void FixedUpdate()
        {
            if (!IsServer || !IsSpawned || Holder == None) return;
            Transform p = PlayerTransform(Holder);
            if (p == null) return;                               // the host's player not spawned yet: wait
            Vector3 at = p.position + Vector3.up * 1.0f;         // in the pocket: rides with the player (late joiners see it hidden)
            _rb.position = at;
            transform.position = at;
        }

        Transform PlayerTransform(ulong client) =>
            NetworkManager != null && NetworkManager.ConnectedClients.TryGetValue(client, out var c) && c.PlayerObject != null
                ? c.PlayerObject.transform : null;
    }
}
