#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using SkyIsland;
using Snow.Levels.Forest;
using Snow.Net;
using Snow.Player;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Snow.Levels
{
    /// <summary>
    /// Developer menu (F1; editor and Development builds only, user decision 21.10 "чит-меню, чтобы самому всё проверять"):
    /// every location of Build Settings (the network goes down, the scene loads, a host starts again if one ran), teleports to
    /// every viewpoint and spawn point of the open scene, time of day and its speed on Forest. Teleport moves only the own
    /// player (as the fall reset does: view yaw first, motor, NetworkTransform as owner). Created by itself on the first scene,
    /// survives scene loads; nothing of it goes into release builds.
    /// </summary>
    public class SnowCheatMenu : MonoBehaviour
    {
        static SnowCheatMenu s_instance;

        bool _open, _restoreLock, _rehost;
        Vector2 _scroll;
        readonly List<(string name, string path)> _scenes = new List<(string, string)>();
        readonly List<(string name, Transform t)> _points = new List<(string, Transform)>();
        float _nextScan;
        GUIStyle _head;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (s_instance != null) return;
            var go = new GameObject("SnowCheatMenu");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<SnowCheatMenu>();
        }

        void Awake()
        {
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(i);
                _scenes.Add((System.IO.Path.GetFileNameWithoutExtension(path), path));
            }
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;

        void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame)
            {
                _open = !_open;
                if (_open)
                {
                    _restoreLock = Cursor.lockState == CursorLockMode.Locked;
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                    Scan();
                }
                else if (_restoreLock)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }
            if (_open && Time.unscaledTime > _nextScan) Scan();
        }

        void Scan()
        {
            _nextScan = Time.unscaledTime + 2f;
            _points.Clear();
            foreach (SkyIslandViewpoint v in FindObjectsByType<SkyIslandViewpoint>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                _points.Add((v.name.Replace("View_", ""), v.transform));
            foreach (Transform t in FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (t.parent != null && t.parent.name == "SpawnPoints") _points.Add(("Спавн " + t.name.Replace("Spawn_", ""), t));
            _points.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        }

        void OnGUI()
        {
            if (!_open) return;
            _head ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 14 };
            GUILayout.BeginArea(new Rect(16f, 16f, 360f, Screen.height - 32f), GUI.skin.box);
            _scroll = GUILayout.BeginScrollView(_scroll);
            GUILayout.Label("Чит-меню (F1)", _head);

            GUILayout.Label("Локации", _head);
            foreach (var s in _scenes)
                if (GUILayout.Button(s.name)) LoadLocation(s.path);

            GUILayout.Label($"Точки — {SceneManager.GetActiveScene().name}", _head);
            if (PlayerNet.Local == null) GUILayout.Label("нет своего игрока (нажмите Host)");
            foreach (var p in _points)
                if (p.t != null && GUILayout.Button(p.name)) Teleport(p.t);

            ForestDayNight dn = ForestDayNight.Instance;
            if (dn != null && dn.IsSpawned)
            {
                float h = dn.Hour;
                GUILayout.Label($"Время {(int)h:00}:{(int)(h % 1f * 60f):00}" + (dn.IsServer ? "" : " (меняет хост)"), _head);
                if (dn.IsServer)
                {
                    GUILayout.BeginHorizontal();
                    foreach (float t in new[] { 2f, 5.5f, 6.5f, 12f, 19.5f })
                        if (GUILayout.Button($"{(int)t:00}:{(int)(t % 1f * 60f):00}")) dn.ServerSetHour(t);
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    foreach (float r in new[] { 1f / 6f, 1f, 6f, 60f })
                        if (GUILayout.Button(r < 1f ? "1 ч / 6 мин" : $"{r:0} ч / мин")) dn.ServerSetRate(r);
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void Teleport(Transform point)
        {
            PlayerNet p = PlayerNet.Local;
            if (p == null || p.Seated) return;
            Vector3 eye = point.position, feet = eye - Vector3.up * 1.6f;
            if (Physics.Raycast(eye + Vector3.up * 0.2f, Vector3.down, out RaycastHit hit, 300f, ~0, QueryTriggerInteraction.Ignore)) feet = hit.point;
            float yaw = point.eulerAngles.y;
            p.view.SetYaw(yaw);
            p.motor.Teleport(feet, yaw, Vector3.zero);
            if (p.TryGetComponent(out NetworkTransform nt) && nt.CanCommitToTransform)
                nt.Teleport(feet, Quaternion.Euler(0f, yaw, 0f), p.transform.localScale);
            p.view.LookAt(point.position + point.forward * 20f);
            BenchLog.Event("cheat_teleport", "to", point.name, "at", feet);
        }

        void LoadLocation(string path)
        {
            NetworkManager nm = NetworkManager.Singleton;
            _rehost = nm == null || nm.IsHost || !nm.IsListening;          // a client cannot take the others along
            if (nm != null)
            {
                if (nm.IsListening) nm.Shutdown();
                Destroy(nm.gameObject);                                   // every level scene carries its own NetworkManager
            }
            BenchLog.Event("cheat_location", "path", path);
            SceneManager.LoadScene(path, LoadSceneMode.Single);
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!_rehost) return;
            _rehost = false;
            SnowNetworkBootstrap boot = FindFirstObjectByType<SnowNetworkBootstrap>();
            if (boot != null && (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)) boot.StartHost();
            _nextScan = 0f;
        }
    }
}
#endif
