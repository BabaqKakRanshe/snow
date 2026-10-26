using System;
using System.Collections.Generic;
using Snow.Net;
using Unity.Netcode;
using UnityEngine;

namespace Snow.Tasks
{
    public enum TaskState : byte { Hidden, Active, Done }

    /// <summary>"По инструкции": task list on J and hints every 5 min; "По памяти": letters only, hints every 15 min (names TEMPORARY, QUESTIONS_3 q34).</summary>
    public enum TaskMode : byte { Guided, Memory }

    [Serializable]
    public class TaskFile
    {
        public float guidedHintMinutes = 5f, memoryHintMinutes = 15f;
        public TaskDef[] tasks;
    }

    [Serializable]
    public class TaskDef
    {
        public string id, from, title, text;
        /// <summary>"radio": the location speaks the hints itself (ForestRadio); TaskBoard gives none.</summary>
        public string hintsBy;
        /// <summary>The text is a message from 'from' to the workers' pagers: it beeps and is typed out when the task shows up (<see cref="Pager"/>), both modes.</summary>
        public bool letter;
        public int day;
        public float until = -1f;
        public string[] after, done, hints;
    }

    /// <summary>
    /// The task MVP (docs/gdd/QUESTS.md, research §C): facts are named numbers the location writes (ForestOpening steps:
    /// "forest.S7" = 1); a task shows up when its 'after' tasks are done and is done when any of its 'done' facts is set —
    /// so a step made before its task showed up counts at once. The server owns the facts and pushes them as one
    /// NetworkList of ints in the order of the task file (late joiners get it with the spawn); task states, the journal
    /// and the toasts are worked out from it on every peer. Hints: one by one after N minutes without progress, stored
    /// as the fact "hint.&lt;id&gt;" so they survive a late join. Lives on the location root next to the location script.
    /// </summary>
    public class TaskBoard : NetworkBehaviour
    {
        [Tooltip("Assets/Locations/Forest/Data/Forest_Tasks.json")] public TextAsset tasks;

        public static TaskBoard Instance { get; private set; }
        /// <summary>Game hour from the location (ForestDayNight), -1 when unknown; for deadlines.</summary>
        public static Func<float> Hour;
        /// <summary>Every peer: a task showed up / got done / a new hint came. Not raised for what was already so on spawn.</summary>
        public static event Action<TaskDef> Given, Completed;
        public static event Action<TaskDef, string> Hinted;

        readonly NetworkList<int> _facts = new NetworkList<int>();
        readonly NetworkVariable<byte> _mode = new NetworkVariable<byte>((byte)TaskMode.Guided);
        readonly List<string> _names = new List<string>();
        readonly Dictionary<string, int> _index = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly Dictionary<string, TaskDef> _byId = new Dictionary<string, TaskDef>(StringComparer.Ordinal);
        readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);
        TaskFile _file;
        TaskDef[] _defs = new TaskDef[0];
        TaskState[] _prev;
        int[] _prevHints;
        float[] _waitFrom;                 // server: when the hint clock of each task started (shown, progress, last hint)
        float _nextTick;

        public IReadOnlyList<TaskDef> Tasks => _defs;
        public TaskMode Mode => (TaskMode)_mode.Value;
        public float HintMinutes => _file == null ? 5f : Mode == TaskMode.Guided ? _file.guidedHintMinutes : _file.memoryHintMinutes;

        void Awake()
        {
            Instance = this;
            Load();
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        void Load()
        {
            _file = tasks != null ? JsonUtility.FromJson<TaskFile>(tasks.text) : null;
            _defs = _file?.tasks ?? new TaskDef[0];
            foreach (TaskDef t in _defs)
            {
                if (string.IsNullOrEmpty(t.id) || _byId.ContainsKey(t.id)) { Debug.LogError($"[Snow] Tasks: empty or repeated id '{t.id}'."); continue; }
                if (MixedScript(t.id)) Debug.LogError($"[Snow] Tasks: id '{t.id}' mixes Latin and Cyrillic letters.");
                foreach (string a in t.after ?? new string[0])
                    if (!_byId.ContainsKey(a)) Debug.LogError($"[Snow] Tasks: '{t.id}' waits for '{a}', which is not an earlier task.");
                _byId[t.id] = t;
                foreach (string f in t.done ?? new string[0]) Name(f);
                Name(HintFact(t));
            }
            _prev = new TaskState[_defs.Length];
            _prevHints = new int[_defs.Length];
            _waitFrom = new float[_defs.Length];
        }

        void Name(string fact)
        {
            if (string.IsNullOrEmpty(fact) || _index.ContainsKey(fact)) return;
            _index[fact] = _names.Count;
            _names.Add(fact);
        }

        static string HintFact(TaskDef t) => "hint." + t.id;

        static bool MixedScript(string s)
        {
            bool latin = false, cyr = false;
            foreach (char c in s)
            {
                if (c < 128 && char.IsLetter(c)) latin = true;
                else if (c >= 'Ѐ' && c <= 'ӿ') cyr = true;
            }
            return latin && cyr;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                if (_facts.Count != _names.Count)
                {
                    _facts.Clear();
                    for (int i = 0; i < _names.Count; i++) _facts.Add(0);
                }
                for (int i = 0; i < _waitFrom.Length; i++) _waitFrom[i] = Time.time;
            }
            _facts.OnListChanged += OnFactsChanged;
            Refresh(true);
            if (!Application.isBatchMode && !TryGetComponent(out TaskHud _)) gameObject.AddComponent<TaskHud>();
            if (!Application.isBatchMode && !TryGetComponent(out Pager _)) gameObject.AddComponent<Pager>();
        }

        public override void OnNetworkDespawn() => _facts.OnListChanged -= OnFactsChanged;

        void OnFactsChanged(NetworkListEvent<int> e) => Refresh(false);

        // ------------------------------------------------------------------ reading (every peer)
        public int Fact(string name) => name != null && _index.TryGetValue(name, out int i) && i < _facts.Count ? _facts[i] : 0;

        public int HintLevel(TaskDef t) => Fact(HintFact(t));

        public TaskState State(TaskDef t) => State(t, 0);

        TaskState State(TaskDef t, int depth)
        {
            if (t == null || depth > 32) return TaskState.Hidden;
            foreach (string a in t.after ?? new string[0])
                if (!_byId.TryGetValue(a, out TaskDef before) || State(before, depth + 1) != TaskState.Done) return TaskState.Hidden;
            foreach (string f in t.done ?? new string[0])
                if (Fact(f) > 0) return TaskState.Done;
            return TaskState.Active;
        }

        /// <summary>True when the task has a deadline and the game hour is past it (within 12 h after, so the next morning is not "late").</summary>
        public bool Late(TaskDef t)
        {
            if (t.until < 0f || Hour == null || State(t) != TaskState.Active) return false;
            float h = Hour();
            if (h < 0f) return false;
            float past = Mathf.Repeat(h - t.until, 24f);
            return past < 12f;
        }

        void Refresh(bool baseline)
        {
            for (int i = 0; i < _defs.Length; i++)
            {
                TaskDef t = _defs[i];
                TaskState s = State(t);
                int hint = HintLevel(t);
                if (!baseline)
                {
                    if (s != _prev[i] && s == TaskState.Active) Given?.Invoke(t);
                    if (s != _prev[i] && s == TaskState.Done) Completed?.Invoke(t);
                    if (hint > _prevHints[i] && t.hints != null && hint <= t.hints.Length && s == TaskState.Active) Hinted?.Invoke(t, t.hints[hint - 1]);
                }
                if (IsServer && s == TaskState.Active && _prev[i] != TaskState.Active) _waitFrom[i] = Time.time;
                _prev[i] = s;
                _prevHints[i] = hint;
            }
        }

        // ------------------------------------------------------------------ writing (server)
        /// <summary>Server: set a fact. Facts no task reads are ignored (warned once in the editor).</summary>
        public static void ServerSet(string fact, int value)
        {
            TaskBoard b = Instance;
            if (b == null || !b.IsSpawned || !b.IsServer) return;
            if (!b._index.TryGetValue(fact, out int i))
            {
                if (Application.isEditor && b._warned.Add(fact)) Debug.Log($"[Snow] Tasks: fact '{fact}' is not read by any task.");
                return;
            }
            if (b._facts[i] == value) return;
            b._facts[i] = value;
            if (!fact.StartsWith("hint.", StringComparison.Ordinal))
                for (int k = 0; k < b._waitFrom.Length; k++) b._waitFrom[k] = Time.time;     // any progress restarts the hint clocks
            BenchLog.Event("task_fact", fact, value);
        }

        public void ServerSetMode(TaskMode mode)
        {
            if (!IsServer) return;
            _mode.Value = (byte)mode;
            BenchLog.Event("task_mode", mode.ToString());
        }

        /// <summary>Server, cheat: mark the task done by its first fact (the world does not move).</summary>
        public void ServerComplete(TaskDef t)
        {
            if (t?.done != null && t.done.Length > 0) ServerSet(t.done[0], 1);
        }

        /// <summary>Server: give the next hint of the task now.</summary>
        public void ServerHint(TaskDef t)
        {
            if (t?.hints == null || !string.IsNullOrEmpty(t.hintsBy) || State(t) != TaskState.Active) return;
            int level = HintLevel(t);
            if (level >= t.hints.Length) return;
            ServerSet(HintFact(t), level + 1);
            int i = Array.IndexOf(_defs, t);
            if (i >= 0) _waitFrom[i] = Time.time;
        }

        void Update()
        {
            if (!IsSpawned || !IsServer || Time.time < _nextTick) return;
            _nextTick = Time.time + 1f;
            float wait = HintMinutes * 60f;
            for (int i = 0; i < _defs.Length; i++)
                if (State(_defs[i]) == TaskState.Active && Time.time - _waitFrom[i] >= wait) ServerHint(_defs[i]);
        }
    }
}
