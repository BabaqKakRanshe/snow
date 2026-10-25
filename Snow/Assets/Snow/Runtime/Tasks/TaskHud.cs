using System.Collections.Generic;
using Snow.Audio;
using Snow.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Snow.Tasks
{
    /// <summary>
    /// The task MVP on screen, local, nothing over the network (docs/gdd/QUESTS.md). J opens the shift journal: in
    /// "По инструкции" the shown tasks with done ones greyed, deadline and the hints heard so far; in "По памяти" only the
    /// letters themselves (the stand-in for PC mail until it exists). A short line at the top tells a new task (guided:
    /// its title; memory: only that the journal has a new page) and, guided only, a done one. Hints come as a subtitle at
    /// the bottom in both modes. A task marked 'letter' (K.'s message at the start, user 25.10: text, not voice) is typed
    /// out at the top left in both modes, letter by letter with a typewriter click (cue ui.letter.key), in the D3 «Протокол» look like the
    /// pockets list; it also prints for a late joiner. Added by <see cref="TaskBoard"/> on spawn; the toast, hint and
    /// journal styles are TEMPORARY until they move to <see cref="Hud"/>.
    /// </summary>
    public class TaskHud : MonoBehaviour
    {
        const float ToastSeconds = 4f, HintSeconds = 9f;
        // letters: characters per second, hold after typing (base + per character), fade, wait after the spawn
        const float TypeRate = 45f, HoldBase = 4f, HoldPerChar = 0.06f, FadeSeconds = 0.6f, StartDelay = 2f;

        TaskBoard _board;
        bool _open;
        Vector2 _scroll;
        string _toast, _hint;
        float _toastAt = -100f, _hintAt = -100f;
        GUIStyle _head, _title, _body, _small, _toastStyle, _hintStyle;

        readonly Queue<TaskDef> _letters = new Queue<TaskDef>();
        readonly HashSet<string> _printed = new HashSet<string>();
        TaskDef _letter;
        string _letterHead;
        List<string> _letterLines;
        float _letterAt, _linesScale = -1f, _spawnAt = -1f;
        int _letterChars, _typed;
        bool _startChecked;
        AudioSource _audio;

        void Awake() => _board = GetComponent<TaskBoard>();

        void Start()
        {
            if (Application.isBatchMode) return;
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
        }

        void OnEnable()
        {
            TaskBoard.Given += OnGiven;
            TaskBoard.Completed += OnCompleted;
            TaskBoard.Hinted += OnHinted;
        }

        void OnDisable()
        {
            TaskBoard.Given -= OnGiven;
            TaskBoard.Completed -= OnCompleted;
            TaskBoard.Hinted -= OnHinted;
        }

        void OnGiven(TaskDef t)
        {
            bool guided = _board != null && _board.Mode == TaskMode.Guided;
            Toast(guided ? Loc.T("Новое задание") + ": " + Loc.T(t.title) + "   [J]" : Loc.T("Новая запись в журнале") + "   [J]");
            Letter(t);
        }

        void Letter(TaskDef t)
        {
            if (t.letter && !string.IsNullOrEmpty(t.text) && _printed.Add(t.id)) _letters.Enqueue(t);
        }

        void OnCompleted(TaskDef t)
        {
            if (_board != null && _board.Mode == TaskMode.Guided) Toast(Loc.T("Выполнено") + ": " + Loc.T(t.title));
        }

        void OnHinted(TaskDef t, string text)
        {
            _hint = Loc.T(text);
            _hintAt = Time.time;
        }

        void Toast(string text)
        {
            _toast = text;
            _toastAt = Time.time;
        }

        void Update()
        {
            UpdateLetters();
            Keyboard kb = Keyboard.current;
            if (kb == null || PlayerNet.Local == null || Snow.Input.SnowInput.UiOpen) return;   // the dev pult holds the keyboard
            if (kb.jKey.wasPressedThisFrame) { _open = !_open; _scroll = Vector2.zero; }
            else if (_open && kb.escapeKey.wasPressedThisFrame) _open = false;
        }

        void OnGUI()
        {
            if (Application.isBatchMode || _board == null || !_board.IsSpawned) return;
            EnsureStyles();
            float now = Time.time;

            if (_toast != null && now - _toastAt < ToastSeconds)
            {
                GUI.color = new Color(1f, 0.85f, 0.5f, Mathf.Clamp01((ToastSeconds - (now - _toastAt)) / 0.6f));
                Plate(new Rect(0f, 48f, Screen.width, 30f), _toast, _toastStyle);
            }
            if (_hint != null && now - _hintAt < HintSeconds)
            {
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01((HintSeconds - (now - _hintAt)) / 0.8f));
                Plate(new Rect(Screen.width * 0.15f, Screen.height * 0.8f, Screen.width * 0.7f, 52f), _hint, _hintStyle);
            }
            GUI.color = Color.white;
            if (_letter != null && Event.current.type == EventType.Repaint) DrawLetter(now);
            if (_open) DrawJournal();
        }

        // ---------------------------------------------------------------- letters
        void UpdateLetters()
        {
            // the letters of the tasks already open at the spawn (no Given for them), a moment after the own player is in
            if (!_startChecked && _board != null && _board.IsSpawned && PlayerNet.Local != null)
            {
                if (_spawnAt < 0f) _spawnAt = Time.time;
                else if (Time.time - _spawnAt >= StartDelay)
                {
                    _startChecked = true;
                    foreach (TaskDef t in _board.Tasks)
                        if (_board.State(t) == TaskState.Active) Letter(t);
                }
            }
            if (!_startChecked) return;                                // a letter given meanwhile waits for the start ones
            float now = Time.time;
            if (_letter != null && now - _letterAt > _letterChars / TypeRate + HoldBase + HoldPerChar * _letterChars + FadeSeconds)
                _letter = null;
            if (_letter == null && _letters.Count > 0)
            {
                _letter = _letters.Dequeue();
                float h = TaskBoard.Hour != null ? TaskBoard.Hour() : -1f;
                _letterHead = (h >= 0f ? $"{(int)h:00}:{(int)(h % 1f * 60f):00}  " : "") + "Nachricht";
                _letterLines = null;                                   // wrapped on the next draw (needs the GUI style)
                _letterChars = _letterHead.Length + 1 + Loc.T(_letter.text).Length;
                _letterAt = now;
                _typed = 0;
            }
            if (_letter == null || _letterLines == null) return;
            // one key click per frame when a printed character came out (spaces and line ends are silent); the cue
            // ui.letter.key is silent until it has clips (AUDIO_LIST A-row «Письмо»)
            int shown = Mathf.Min(_letterChars, Mathf.FloorToInt((now - _letterAt) * TypeRate));
            if (shown > _typed && Printed(shown - 1) && _audio != null)
            {
                AudioClip key = SnowAudio.Clip("ui.letter.key");
                if (key != null)
                {
                    _audio.pitch = Random.Range(0.95f, 1.05f);
                    _audio.PlayOneShot(key, 0.35f);
                }
            }
            _typed = Mathf.Max(_typed, shown);
        }

        // the character at a typed position is a visible one (every line counts one more for its end)
        bool Printed(int index)
        {
            foreach (string line in _letterLines)
            {
                if (index < line.Length) return line[index] != ' ';
                index -= line.Length + 1;
                if (index < 0) return false;
            }
            return false;
        }

        // the message typed at the top left (D3 «Протокол»): "17:30  Nachricht" in the warm colour with "сообщение"
        // beside it once typed, then the text in ink, a cursor while typing, no plate; fades out after reading time
        void DrawLetter(float now)
        {
            GUIStyle st = Hud.Mono(21), small = Hud.Mono(18);
            if (_letterLines == null || !Mathf.Approximately(_linesScale, Hud.S))
            {
                _letterLines = Hud.Wrap(_letterHead + "\n" + Loc.T(_letter.text), st, Mathf.Min(Hud.Px(620f), Screen.width * 0.45f));
                _linesScale = Hud.S;
                _letterChars = 0;
                foreach (string line in _letterLines) _letterChars += line.Length + 1;
            }
            float t = now - _letterAt, typeTime = _letterChars / TypeRate;
            float end = typeTime + HoldBase + HoldPerChar * _letterChars;
            float alpha = Mathf.Clamp01(1f - (t - end) / FadeSeconds);
            int left = Mathf.FloorToInt(t * TypeRate);
            bool cursor = t < typeTime && Mathf.Repeat(t, 0.5f) < 0.3f;
            float x = Hud.SafeX, y = Hud.Px(150f), lh = Hud.Px(27f);
            Color warm = new Color(Hud.Warm.r, Hud.Warm.g, Hud.Warm.b, alpha), ink = new Color(Hud.Ink.r, Hud.Ink.g, Hud.Ink.b, alpha);
            for (int i = 0; i < _letterLines.Count && left > 0; i++)
            {
                string line = _letterLines[i];
                bool partial = left <= line.Length;
                Color c = i == 0 ? warm : ink;
                float w = Hud.Typed(x, y, partial ? line.Substring(0, left) : line, st, c);
                if (partial && cursor) Hud.Typed(x + w, y, "_", st, c);
                if (i == 0 && !partial)
                    Hud.Typed(x + w + Hud.Px(16f), y + Hud.Px(3f), Loc.T("сообщение"), small, new Color(0.663f, 0.651f, 0.612f, alpha));
                left -= line.Length + 1;
                y += lh;
            }
        }

        /// <summary>Text on a dark plate as wide as the text (readable over snow and fog).</summary>
        static void Plate(Rect area, string text, GUIStyle style)
        {
            Vector2 size = style.CalcSize(new GUIContent(text));
            float w = Mathf.Min(area.width, size.x + 28f);
            var r = new Rect(area.x + (area.width - w) * 0.5f, area.y, w, Mathf.Max(area.height, style.CalcHeight(new GUIContent(text), w) + 8f));
            Color c = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.7f * c.a);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = c;
            GUI.Label(r, text, style);
        }

        void DrawJournal()
        {
            bool guided = _board.Mode == TaskMode.Guided;
            float w = Mathf.Min(720f, Screen.width - 80f), h = Screen.height * 0.78f;
            var panel = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUILayout.BeginArea(new Rect(panel.x + 24f, panel.y + 18f, panel.width - 48f, panel.height - 36f));
            GUILayout.Label(Loc.T("Журнал смены") + (guided ? "" : " — " + Loc.T("письма")), _head);
            _scroll = GUILayout.BeginScrollView(_scroll);

            List<TaskDef> shown = new List<TaskDef>();
            foreach (TaskDef t in _board.Tasks)
                if (_board.State(t) == TaskState.Active) shown.Add(t);
            foreach (TaskDef t in _board.Tasks)
                if (_board.State(t) == TaskState.Done) shown.Add(t);
            if (shown.Count == 0) GUILayout.Label(Loc.T("Пока пусто."), _body);

            foreach (TaskDef t in shown)
            {
                bool done = _board.State(t) == TaskState.Done;
                GUILayout.Space(10f);
                if (guided)
                {
                    GUI.color = done ? new Color(0.6f, 0.6f, 0.6f) : new Color(1f, 0.85f, 0.5f);
                    GUILayout.Label((done ? "✓ " : "• ") + Loc.T(t.title) + (done ? "  (" + Loc.T("выполнено") + ")" : ""), _title);
                    GUI.color = done ? new Color(0.7f, 0.7f, 0.7f) : Color.white;
                }
                else
                {
                    GUI.color = new Color(0.75f, 0.75f, 0.75f);
                    GUILayout.Label(Loc.T(t.from), _small);
                    GUI.color = Color.white;
                }
                GUILayout.Label(Loc.T(t.text), _body);
                if (t.until >= 0f && !done)
                {
                    bool late = _board.Late(t);
                    GUI.color = late ? new Color(1f, 0.45f, 0.4f) : new Color(0.85f, 0.85f, 0.85f);
                    GUILayout.Label(late ? Loc.T("Срок прошёл") : Loc.T("Срок") + $": {(int)t.until:00}:{(int)(t.until % 1f * 60f):00}", _small);
                    GUI.color = Color.white;
                }
                if (guided && !done && t.hints != null)
                {
                    int heard = Mathf.Min(_board.HintLevel(t), t.hints.Length);
                    GUI.color = new Color(0.8f, 0.85f, 0.9f);
                    for (int i = 0; i < heard; i++) GUILayout.Label("— " + Loc.T(t.hints[i]), _small);
                    GUI.color = Color.white;
                }
            }
            GUILayout.EndScrollView();
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            GUILayout.Label(Loc.T("[J] закрыть"), _small);
            GUI.color = Color.white;
            GUILayout.EndArea();
        }

        void EnsureStyles()
        {
            if (_head != null) return;
            _head = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            _title = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, wordWrap = true };
            _body = new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            _toastStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
            _hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            foreach (GUIStyle st in new[] { _head, _title, _body, _small, _toastStyle, _hintStyle }) st.normal.textColor = Color.white;
        }
    }
}
