using Unity.Netcode;
using UnityEngine;

namespace Snow.Levels.Forest
{
    /// <summary>
    /// Puts the networked Lesnik (Resources/LesnikNet, in DefaultNetworkPrefabs) into a Forest game on the host: no scene
    /// change, it watches for the Forest opening to spawn. One Lesnik; the pult can take him out (<see cref="Enabled"/>).
    /// </summary>
    public class LesnikSpawner : MonoBehaviour
    {
        /// <summary>Pult: off despawns him and keeps him out.</summary>
        public static bool Enabled = true;

        static LesnikSpawner s_instance;
        float _next;
        bool _warned;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (s_instance != null) return;
            var go = new GameObject("LesnikSpawner");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<LesnikSpawner>();
        }

        void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f;
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer || !nm.IsListening) return;
            Lesnik it = Lesnik.Instance;
            if (!Enabled)
            {
                if (it != null && it.IsSpawned) it.NetworkObject.Despawn(true);
                return;
            }
            if (it != null || ForestOpening.Instance == null || !ForestOpening.Instance.IsSpawned) return;
            var prefab = Resources.Load<GameObject>("LesnikNet");
            if (prefab == null)
            {
                if (!_warned) Debug.LogWarning("[Snow] Лесник: Resources/LesnikNet не найден (Tools/Snow/Creatures/Build Lesnik Net Prefab).");
                _warned = true;
                return;
            }
            GameObject go = Instantiate(prefab, ForestOpening.Instance.transform.position, Quaternion.identity);
            go.GetComponent<NetworkObject>().Spawn(true);
        }
    }
}
