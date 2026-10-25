using System;
using System.Collections.Generic;
using System.Diagnostics;
using Snow.Aircraft;
using Snow.Cargo;
using Snow.Net;
using Unity.Netcode;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Snow.Levels.Forest
{
    /// <summary>
    /// The Forest's physical items (FOREST_U3 F4.2): on the server, once per session (OnNetworkSpawn of the location root),
    /// every opening item but the barrier padlock (it hangs on the barrier until unlocked, F5.5), every dressing prop and
    /// every note of the main building are instantiated from the item prefabs (Tools / Snow / Forest / Build Item Prefabs),
    /// set up (fuel, note id, mass) before Spawn so clients get it in the spawn message, spawned and put to sleep. A second
    /// Host in the same play session spawns them again (the first session's objects went with its shutdown). The id ->
    /// object table is for the opening script (X01, padlock, cleaning).
    /// </summary>
    public class ForestItems : NetworkBehaviour
    {
        [Serializable]
        public class ModelPrefab
        {
            public string model;
            public GameObject prefab;
        }

        [Tooltip("Assets/Locations/Forest/Data/Forest_Opening.json")] public TextAsset opening;
        [Tooltip("Assets/Locations/Forest/Data/Forest_Dressing.json")] public TextAsset dressing;
        [Tooltip("Model -> network prefab (Assets/Snow/Levels/Forest/Items), written by the Forest step")] public ModelPrefab[] prefabs = new ModelPrefab[0];

        /// <summary>The opening item that stays a static copy until its lock is taken off (F5.5).</summary>
        public const string PadlockId = "Padlock_Barrier";
        /// <summary>Every player starts with this in his hand, switched on (user 25.10: «появляться с фонариками сразу»).</summary>
        public const string StarterModel = "Item_Flashlight";

        readonly Dictionary<string, GameObject> _byModel = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        readonly Dictionary<string, NetworkObject> _byId = new Dictionary<string, NetworkObject>(StringComparer.Ordinal);
        readonly HashSet<ulong> _equipped = new HashSet<ulong>();

        void Awake()
        {
            foreach (ModelPrefab m in prefabs)
                if (m != null && m.prefab != null && !string.IsNullOrEmpty(m.model)) _byModel[m.model] = m.prefab;
        }

        /// <summary>Server: the spawned item of a placement id, or null.</summary>
        public NetworkObject Find(string id) => id != null && _byId.TryGetValue(id, out NetworkObject no) && no != null ? no : null;

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            _byId.Clear();
            var sw = Stopwatch.StartNew();
            var o = ForestData.Read<OpeningData>(opening);
            var d = ForestData.Read<DressingData>(dressing);
            int nOpening = 0, nProps = 0, nNotes = 0, missing = 0;
            if (o?.items != null)
                foreach (OpeningItem it in o.items)
                {
                    if (it == null || it.id == PadlockId) continue;
                    if (Spawn(it.id, it.model, ForestRelocation.Point(ForestData.V3(it.p)), ForestRelocation.Rotation(ForestData.V3(it.p), ForestData.Q(it.q)), it.massKg, it.fuelL, it.readable) != null) nOpening++;
                    else missing++;
                }
            if (d?.props != null)
                foreach (DressingProp it in d.props)
                    if (it != null && Spawn(it.id, it.model, ForestRelocation.Point(ForestData.V3(it.p)), ForestRelocation.Rotation(ForestData.V3(it.p), ForestData.Q(it.q)), it.massKg, -1f, it.readable) != null) nProps++;
                    else missing++;
            if (d?.notes != null)
                foreach (DressingProp it in d.notes)
                    if (it != null && Spawn(it.id, it.model, ForestRelocation.Point(ForestData.V3(it.p)), ForestRelocation.Rotation(ForestData.V3(it.p), ForestData.Q(it.q)), it.massKg, -1f, it.readable) != null) nNotes++;
                    else missing++;
            sw.Stop();
            int total = nOpening + nProps + nNotes;
            Debug.Log($"[Snow] Forest items spawned: {total} ({nOpening} opening, {nProps} props, {nNotes} notes).");
            if (missing > 0) Debug.LogWarning($"[Snow] Forest items: {missing} placement(s) without a prefab (run Tools / Snow / Forest / Build Item Prefabs).");
            BenchLog.Event("forest_items", "total", total, "opening", nOpening, "props", nProps, "notes", nNotes, "ms", sw.Elapsed.TotalMilliseconds);

            _equipped.Clear();
            NetworkManager.OnClientConnectedCallback += GiveStarter;
            foreach (ulong client in NetworkManager.ConnectedClientsIds) GiveStarter(client);
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkManager != null) NetworkManager.OnClientConnectedCallback -= GiveStarter;
        }

        // server: once per client of this session, a flashlight into his hand (its pocket slot), switched on; the hand
        // item is picked up by his InteractionSensor like one he took himself
        void GiveStarter(ulong client)
        {
            if (!_equipped.Add(client)) return;
            Vector3 p = NetworkManager.ConnectedClients.TryGetValue(client, out NetworkClient c) && c.PlayerObject != null
                ? c.PlayerObject.transform.position + Vector3.up : transform.position;
            NetworkObject no = Spawn($"Flashlight_Player{client}", StarterModel, p, Quaternion.identity);
            if (no == null) { Debug.LogWarning($"[Snow] Forest: no {StarterModel} prefab, player {client} starts without a flashlight."); return; }
            string reason = "no CargoItem";
            if (!no.TryGetComponent(out CargoItem item) || !item.ServerTakeInHand(client, out reason))
            {
                Debug.LogWarning($"[Snow] Forest: starter flashlight not given to player {client}: {reason}");
                return;
            }
            if (no.TryGetComponent(out SurvivalItem light)) light.ServerSetOn(true);
            BenchLog.Event("starter_item", "client", client, "item", no.name);
        }

        /// <summary>Server: one item at a pose of the model's origin, set up before Spawn, then asleep.</summary>
        public NetworkObject Spawn(string id, string model, Vector3 p, Quaternion q, float massKg = -1f, float fuelL = -1f, string readable = null)
        {
            if (!IsServer || model == null || !_byModel.TryGetValue(model, out GameObject prefab)) return null;
            GameObject go = Instantiate(prefab, p, q);
            if (!string.IsNullOrEmpty(id)) go.name = id;
            var item = go.GetComponent<CargoItem>();
            if (massKg >= 0f && item != null && !Mathf.Approximately(item.massKg, massKg))
            {
                item.massKg = massKg;                                  // CargoItem.Awake already gave the body the prefab's mass
                if (item.Body != null) item.Body.mass = massKg;
            }
            if (fuelL >= 0f && go.TryGetComponent(out FuelCan can)) can.SetInitial(fuelL);
            if (go.TryGetComponent(out ReadableNote note)) note.SetNote(readable);
            var no = go.GetComponent<NetworkObject>();
            no.Spawn();
            if (go.TryGetComponent(out Rigidbody rb)) rb.Sleep();      // they stand until touched
            if (!string.IsNullOrEmpty(id)) _byId[id] = no;
            return no;
        }
    }
}
