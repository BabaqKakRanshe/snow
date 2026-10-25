#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using SkyIsland;
using Snow.Dev;
using Snow.Levels.Forest;
using Snow.Net;
using Snow.Player;
using Snow.Tasks;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Snow.Levels
{
    /// <summary>
    /// The level pages of the dev pult (~; editor and Development builds only; was the F1 cheat menu, user decisions 21.10
    /// "чит-меню, чтобы самому всё проверять" and 23.10 "все менюшки в нормальную тулзу"): Игра (language, every location
    /// of Build Settings, teleports to the viewpoints and spawn points of the open scene), Время (Forest day and night),
    /// Задания, Рация, База (power and room lights of the Forest base). Location load: the network goes down, the scene
    /// loads, a host starts again if one ran. Teleport moves only the own player (as the fall reset does: view yaw first,
    /// motor, NetworkTransform as owner). Created by itself on the first scene, survives scene loads.
    /// </summary>
    public class SnowDevPages : MonoBehaviour
    {
        static SnowDevPages s_instance;

        bool _rehost;
        readonly List<(string name, string path)> _scenes = new List<(string, string)>();
        readonly List<(string name, Transform t)> _points = new List<(string, Transform)>();
        float _nextScan;
        Action _game, _time, _tasks, _radio, _base;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (s_instance != null) return;
            var go = new GameObject("SnowDevPages");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<SnowDevPages>();
        }

        void Awake()
        {
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(i);
                _scenes.Add((System.IO.Path.GetFileNameWithoutExtension(path), path));
            }
            SceneManager.sceneLoaded += OnSceneLoaded;
            DevPult.Add("Игра", 0, _game = DrawGame);
            DevPult.Add("Время", 10, _time = DrawTime, () => ForestDayNight.Instance != null && ForestDayNight.Instance.IsSpawned);
            DevPult.Add("Задания", 20, _tasks = DrawTasks, () => TaskBoard.Instance != null && TaskBoard.Instance.IsSpawned);
            DevPult.Add("Рация", 30, _radio = DrawRadio, () => ForestRadio.Instance != null);
            DevPult.Add("База", 40, _base = DrawBase, () => ForestOpening.Instance != null && ForestOpening.Instance.IsSpawned);
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            DevPult.Remove("Игра", _game);
            DevPult.Remove("Время", _time);
            DevPult.Remove("Задания", _tasks);
            DevPult.Remove("Рация", _radio);
            DevPult.Remove("База", _base);
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

        // ------------------------------------------------------------------ Игра
        void DrawGame()
        {
            // interface language for this player (user 22.10); remembered in PlayerPrefs
            if (DevUi.Button(Loc.English ? "Language: English → Русский" : "Язык: русский → English")) Loc.English = !Loc.English;

            DevUi.Header("Локации");
            foreach (var s in _scenes)
                if (DevUi.Button(s.name)) LoadLocation(s.path);

            if (Time.unscaledTime > _nextScan) Scan();
            DevUi.Header($"Точки — {SceneManager.GetActiveScene().name}");
            if (PlayerNet.Local == null) DevUi.Note("нет своего игрока (нажмите Host)");
            for (int i = 0; i < _points.Count; i++)
                if (_points[i].t != null && DevUi.Button($"{_points[i].name}##pt{i}")) Teleport(_points[i].t);
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
            _nextScan = 0f;
            if (!_rehost) return;
            _rehost = false;
            SnowNetworkBootstrap boot = FindFirstObjectByType<SnowNetworkBootstrap>();
            if (boot != null && (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening))
            {
                boot.StartHost();
                DevPult.Hide();
            }
        }

        // ------------------------------------------------------------------ Время
        void DrawTime()
        {
            ForestDayNight dn = ForestDayNight.Instance;
            float h = dn.Hour;
            DevUi.Header($"Время {(int)h:00}:{(int)(h % 1f * 60f):00}");
            bool host = DevUi.HostOnly();
            for (int i = 0; i < Hours.Length; i++)
            {
                float t = Hours[i];
                if (i > 0) DevUi.Row();
                if (DevUi.Button($"{(int)t:00}:{(int)(t % 1f * 60f):00}", host)) dn.ServerSetHour(t);
            }
            for (int i = 0; i < Rates.Length; i++)
            {
                float r = Rates[i];
                if (i > 0) DevUi.Row();
                if (DevUi.Button(r < 1f ? "1 ч / 6 мин" : $"{r:0} ч / мин", host)) dn.ServerSetRate(r);
            }
            DevUi.Header("Длина суток");
            for (int i = 0; i < ForestDayNight.DayLengths.Length; i++)
            {
                if (i > 0) DevUi.Row();
                if (DevUi.Button(ForestDayNight.DayLengths[i].name, host)) dn.ServerSetDayLength(ForestDayNight.DayLengths[i].minutes);
            }
            DevUi.Header($"Скорость ночи ×{dn.NightSpeed:0.##}");
            for (int i = 0; i < NightSpeeds.Length; i++)
            {
                if (i > 0) DevUi.Row();
                if (DevUi.Button($"×{NightSpeeds[i]:0.#}", host)) dn.ServerSetNightSpeed(NightSpeeds[i]);
            }
            DevUi.Header($"Ночь: {dn.CurrentNight.name}");
            for (int k = 0; k < dn.nightKinds.Length; k++)
            {
                if (k > 0) DevUi.Row();
                if (DevUi.Button(Loc.T(dn.nightKinds[k].name).Split(' ')[0] + $"##nk{k}", host)) dn.ServerSetNightKind(k);
            }
            ForestEyeAdaptation eyes = dn.GetComponent<ForestEyeAdaptation>();
            DevUi.Text($"Экспозиция {dn.Exposure:+0.00;-0.00} EV" + (eyes != null ? $", глаза {eyes.Offset:+0.00;-0.00}, кадр {eyes.SceneLuminance:0.0000}" : ""));
            float nb = ForestDayNight.NightBrightness;
            if (DevUi.Slider("Яркость ночи (у себя)", ref nb, 0f, 1f) && Mathf.Abs(nb - ForestDayNight.NightBrightness) > 0.01f)
                ForestDayNight.NightBrightness = nb;
        }

        static readonly float[] Hours = { 2f, 5.5f, 6.5f, 12f, 19.5f };
        static readonly float[] Rates = { 1f / 6f, 1f, 6f, 60f };
        static readonly float[] NightSpeeds = { 0.5f, 1f, 2f, 4f };

        // ------------------------------------------------------------------ Задания
        /// <summary>The task MVP (docs/gdd/QUESTS.md): mode, every task with its state; the host can close a task or give its next hint.</summary>
        void DrawTasks()
        {
            TaskBoard board = TaskBoard.Instance;
            bool host = DevUi.HostOnly();
            bool guided = board.Mode == TaskMode.Guided;
            if (DevUi.Button(guided ? "Режим: По инструкции → По памяти" : "Режим: По памяти → По инструкции", host))
                board.ServerSetMode(guided ? TaskMode.Memory : TaskMode.Guided);
            int n = 0;
            foreach (TaskDef t in board.Tasks)
            {
                n++;
                TaskState st = board.State(t);
                string mark = st == TaskState.Done ? "✓" : st == TaskState.Active ? "•" : "·";
                DevUi.Text($"{mark} {Loc.T(t.title)}");
                if (!host || st != TaskState.Active) continue;
                DevUi.Row();
                if (DevUi.Button($"готово##t{n}")) board.ServerComplete(t);
                if (t.hints != null && t.hints.Length > 0)
                {
                    DevUi.Row();
                    if (DevUi.Button($"подсказка {board.HintLevel(t)}/{t.hints.Length}##h{n}")) board.ServerHint(t);
                }
            }
        }

        // ------------------------------------------------------------------ Рация
        /// <summary>The control-desk radio (voice, docs/gdd/VOICE_LIST.md): run a part now, see the last rule decision.</summary>
        void DrawRadio()
        {
            ForestRadio radio = ForestRadio.Instance;
            bool host = DevUi.HostOnly();
            for (int i = 0; i < RadioParts.Length; i++)
            {
                if (i % 4 != 0) DevUi.Row();
                if (DevUi.Button(RadioParts[i].label, host)) radio.DebugRun(RadioParts[i].id);
            }
            if (radio.LastDebug.Length > 0) DevUi.Text(radio.LastDebug);
            if (radio.LastWhy.Length > 0) DevUi.Raw(radio.LastWhy);
        }

        static readonly (string id, string label)[] RadioParts =
        {
            ("day1", "День 1"), ("hint", "Подсказка"), ("x08", "X08"), ("e12", "E12"),
            ("morning", "Утро"), ("idle", "Шутка"), ("stop", "Стоп"),
        };

        // ------------------------------------------------------------------ База
        /// <summary>Forest base power (ForestOpening.DevPower): the whole chain at once, or the generator off; room lights one
        /// by one. Values as this peer sees them; the buttons only on the host.</summary>
        void DrawBase()
        {
            ForestOpening op = ForestOpening.Instance;
            SkyIslandLocationHost host = op.host;
            DevUi.Text($"Генератор: {(op.GeneratorRunning ? "работает" : "стоит")}, в баке {op.DayTankL:0.#} л");
            ForestKpp kpp = ForestKpp.Instance;
            DevUi.Text($"Сеть: {(host != null && host.Powered ? "есть" : "нет")}; ток в будке КПП: {(op.KppPowered ? "есть" : "нет")}" +
                       (kpp != null ? $"; аккумулятор {(kpp.BatteryIn ? "в шкафу" : "не в шкафу")}" : ""));
            bool server = DevUi.HostOnly();
            if (DevUi.Button("Свет везде", server)) op.DevPower(true);
            DevUi.Row();
            if (DevUi.Button("Обесточить (генератор стоп)", server)) op.DevPower(false);
            if (kpp != null)
            {
                if (DevUi.Button("КПП: аккумулятор в шкаф", server)) kpp.DevBoothPower(true);
                DevUi.Row();
                if (DevUi.Button("КПП: на NETZ, клеммы снять", server)) kpp.DevBoothPower(false);
            }

            SkyIslandLightManager lights = host != null ? host.lights : null;
            if (lights == null) return;
            int on = 0;
            foreach (var r in lights.rooms) if (r != null && lights.IsRoomOn(r.name)) on++;
            DevUi.Header($"Свет в комнатах: {on} / {lights.rooms.Count}");
            for (int i = 0; i < lights.rooms.Count; i++)
            {
                var r = lights.rooms[i];
                if (r == null) continue;
                bool lit = lights.IsRoomOn(r.name);
                if (i % 3 != 0) DevUi.Row();
                if (DevUi.Checkbox($"{r.name}##room{i}", ref lit, server)) op.DevRoom(r.name, lit);
            }
        }
    }
}
#endif
