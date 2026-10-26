using System.Collections.Generic;
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
    /// the bottom in both modes. Added by <see cref="TaskBoard"/> on spawn; styles like NoteReader / PocketsHud (TEMPORARY
    /// until the UI pick, RESEARCH_UI.md).
    /// </summary>
    public class TaskHud : MonoBehaviour
    {
        const float ToastSeconds = 4f, HintSeconds = 9f;

        TaskBoard _board;
        bool _open;
        Vector2 _scroll;
        string _toast, _hint;
        float _toastAt = -100f, _hintAt = -100f;
        GUIStyle _head, _title, _body, _small, _toastStyle, _hintStyle;

        void Awake() => _board = GetComponent<TaskBoard>();

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
            Snow.Audio.SnowAudio.PlayUi("ui.task.new");
            bool guided = _board != null && _board.Mode == TaskMode.Guided;
            Toast(guided ? Loc.T("Новое задание") + ": " + Loc.T(t.title) + "   [J]" : Loc.T("Новая запись в журнале") + "   [J]");
        }

        void OnCompleted(TaskDef t)
        {
            Snow.Audio.SnowAudio.PlayUi("ui.task.done");
            if (_board != null && _board.Mode == TaskMode.Guided) Toast(Loc.T("Выполнено") + ": " + Loc.T(t.title));
        }

        void OnHinted(TaskDef t, string text)
        {
            Snow.Audio.SnowAudio.PlayUi("ui.hint");
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
            Keyboard kb = Keyboard.current;
            if (kb == null || PlayerNet.Local == null || Snow.Input.SnowInput.UiOpen) return;   // the dev pult holds the keyboard
            if (kb.jKey.wasPressedThisFrame) { _open = !_open; _scroll = Vector2.zero; Snow.Audio.SnowAudio.PlayUi(_open ? "ui.journal.open" : "ui.journal.close"); }
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
            if (_open) DrawJournal();
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
            Vector2 before = _scroll;
            _scroll = GUILayout.BeginScrollView(_scroll);
            if (Mathf.Abs(_scroll.y - before.y) > 30f) Snow.Audio.SnowAudio.PlayUi("ui.journal.scroll", 0.5f);

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
