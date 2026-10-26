using System.Collections.Generic;
using Snow.Audio;
using Snow.Character;
using Snow.Interaction;
using Snow.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Snow.Tasks
{
    /// <summary>
    /// Every worker's pager (user 25-26.10.2026, docs/gdd/QUESTIONS_6.md, docs/gdd/research/RESEARCH_UI_3.md, model
    /// docs/gdd/PAGER_CONTRACT.md). K. writes, he does not speak, so his messages come here. A message beeps (cue
    /// ui.pager.beep; vibrate shakes the view, silent does neither), then types out at the top left under "PAGER · K.  17:30"
    /// in the D3 «Протокол» look of the pockets list, letter by letter with a key click (ui.letter.key), and fades. The device's own
    /// sounds: ui.pager.raise / lower / button / vibrate / open / mode (the key click stands in for the first four while they
    /// have no clips); ui.letter.return at each line end of the typing.
    /// P raises the pager into the left of the view (the SnowPager model on the camera, in four steps); its own small screen
    /// is only a picker: the count, the unread, the signal, and the list - time, sender, the first words. ↑ / ↓ or the wheel
    /// pick, E opens: the full text prints at the top left and the pager shows "» НА ЭКРАНЕ"; ↑ / ↓ go to the neighbour
    /// message, E or Esc back to the list; Esc or P on the list lowers it. Holding E on the list opens the mode: sound /
    /// vibrate / silent. Any key lights the screen for 5 s (it is hard to read in the dark otherwise); the LED blinks while
    /// something is unread. The list is drawn over the model's LCD glass, along its projected corners. While the pager is up
    /// E, the arrows and the wheel are its own (InteractionSensor.DeviceRaised). Messages are the tasks marked 'letter'
    /// (Forest_Tasks.json) as they show up, and the ones already open when the own player arrives, once the frame rate has
    /// settled after the load (a late joiner gets them too). Local, nothing over the network; added by
    /// <see cref="TaskBoard"/> next to <see cref="TaskHud"/>.
    /// </summary>
    public class Pager : MonoBehaviour
    {
        // typing: characters per second, extra stops (s), most characters a single frame may add (a hitch shows as
        // typing, not as a jump), reading time (base + per character), fade
        const float TypeRate = 24f, StopSentence = 0.22f, StopComma = 0.1f, StopLine = 0.18f, BeepLead = 0.55f;
        const int MaxPerFrame = 2;
        const float HoldBase = 4f, HoldPerChar = 0.05f, FadeSeconds = 0.6f;
        // the start: at least this long after the own player is in, then this long without a slow frame (or the cap)
        const float StartMin = 2f, CalmNeeded = 0.75f, SlowFrame = 0.04f, StartCap = 10f;
        // the device: raise / lower time (4 steps), the backlight after a key, the hold that opens the mode, the re-read speed
        const float RaiseSeconds = 0.24f, Backlight = 5f, HoldMode = 0.6f, ReadRate = 70f, KeyPress = 0.12f;
        const int Cols = 20, Rows = 4;
        // where the pager sits in the camera's frame when raised (m, deg): low left, the screen to the eye
        static readonly Vector3 HeldPos = new Vector3(-0.07f, -0.056f, 0.165f), LoweredPos = new Vector3(-0.07f, -0.21f, 0.15f);
        static readonly Quaternion HeldRot = Quaternion.Euler(6f, 0f, -4f) * Quaternion.Euler(0f, 180f, 0f);
        // the world behind the raised pager darkens (RESEARCH_UI_3.md §5, after Alien: Isolation's blur): a black quad behind it
        const float DimAlpha = 0.35f, DimDistance = 0.30f;

        enum Screen { List, Message, Mode }

        class Message
        {
            public TaskDef task;
            public string from, time;
            public bool read;
        }

        TaskBoard _board;
        readonly List<Message> _inbox = new List<Message>();        // oldest first
        readonly Queue<Message> _queue = new Queue<Message>();
        readonly HashSet<string> _got = new HashSet<string>();
        Message _typing;
        List<string> _lines;
        float[] _at;                       // when each typed position comes out (lines joined, one more per line end)
        float _typingAt, _doneAt, _spawnAt = -1f, _calm, _linesScale = -1f;
        int _shown;
        bool _started;
        AudioSource _audio;

        // the device
        PagerModel _model;
        Renderer _dim;
        MaterialPropertyBlock _dimBlock;
        bool _up;
        float _moveAt = -10f, _lightUntil, _openAt, _eDownAt = -1f, _wheel;
        bool _eHeldUsed;
        Screen _screen = Screen.List;
        int _sel, _mode;                   // _sel: 0 = the newest; _mode: 0 sound, 1 vibrate, 2 silent
        readonly float[] _keyAt = { -10f, -10f, -10f };
        readonly Vector3[] _keyRest = new Vector3[3];
        readonly Vector3[] _lcdCorners = new Vector3[4];

        void Awake() => _board = GetComponent<TaskBoard>();

        void OnEnable() => TaskBoard.Given += OnGiven;

        void OnDisable()
        {
            TaskBoard.Given -= OnGiven;
            InteractionSensor.DeviceRaised = false;
        }

        void Start()
        {
            if (Application.isBatchMode) return;
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
        }

        void OnDestroy()
        {
            if (_model != null) Destroy(_model.gameObject);
            if (_dim != null) Destroy(_dim.gameObject);
        }

        void OnGiven(TaskDef t)
        {
            if (!t.letter || string.IsNullOrEmpty(t.text) || !_got.Add(t.id)) return;
            float h = TaskBoard.Hour != null ? TaskBoard.Hour() : -1f;
            _queue.Enqueue(new Message
            {
                task = t, from = string.IsNullOrEmpty(t.from) ? "K." : t.from,
                time = h >= 0f ? $"{(int)h:00}:{(int)(h % 1f * 60f):00}" : "",
            });
        }

        void Update()
        {
            if (!Begin()) return;
            Keys();
            Device();
            float now = Time.time;
            if (_typing != null && _shown >= Total && now - _doneAt > HoldBase + HoldPerChar * Total + FadeSeconds) _typing = null;
            if (_typing == null && _queue.Count > 0) Arrive(_queue.Dequeue());
            if (_typing == null || _at == null || _shown >= _at.Length) return;
            float t = now - _typingAt;
            int target = _shown;
            while (target < _at.Length && _at[target] <= t) target++;
            int next = Mathf.Min(target, _shown + MaxPerFrame);
            bool printed = false, lineEnd = false;
            for (int i = _shown; i < next; i++)
            {
                printed |= Printed(i);
                lineEnd |= i < _at.Length - 1 && LineEnd(i);
            }
            if (printed) Play("ui.letter.key", Random.Range(0.35f, 0.55f), Random.Range(0.95f, 1.05f));
            if (lineEnd) Play("ui.letter.return", 0.4f, 1f);                  // the carriage goes back between lines
            _shown = next;
            if (_shown >= _at.Length)
            {
                _doneAt = now;
                _typing.read = true;                                 // it has been on the screen in full
            }
        }

        void Arrive(Message m)
        {
            _typing = m;
            _inbox.Add(m);
            _lines = null;                                           // wrapped on the next draw (needs the GUI style)
            _at = null;
            _shown = 0;
            _typingAt = Time.time;
            if (_screen == Screen.List) _sel = 0;
            else if (_screen == Screen.Message) _sel++;              // the open one keeps its place in the list
            if (_mode == 0) Play("ui.pager.beep", 0.8f, 1f);
            else if (_mode == 1)
            {
                CameraShake.Add(0.18f);
                Play("ui.pager.vibrate", 0.8f, 1f);
            }
        }

        int Total => _at != null ? _at.Length : int.MaxValue;

        // the start ones wait for the own player and a settled frame rate: the first seconds after the load hitch, and
        // typed text would come out in lumps
        bool Begin()
        {
            if (_started) return true;
            if (_board == null || !_board.IsSpawned || PlayerNet.Local == null) return false;
            float now = Time.time;
            if (_spawnAt < 0f) _spawnAt = now;
            _calm = Time.unscaledDeltaTime < SlowFrame ? _calm + Time.unscaledDeltaTime : 0f;
            if (now - _spawnAt < StartMin || (_calm < CalmNeeded && now - _spawnAt < StartCap)) return false;
            _started = true;
            var given = new List<Message>(_queue);                   // given meanwhile: after the ones open at the start
            _queue.Clear();
            foreach (TaskDef t in _board.Tasks)
                if (_board.State(t) == TaskState.Active) OnGiven(t);
            foreach (Message m in given) _queue.Enqueue(m);
            return true;
        }

        // ---------------------------------------------------------------- keys
        Message Picked => _inbox.Count == 0 ? null : _inbox[_inbox.Count - 1 - Mathf.Clamp(_sel, 0, _inbox.Count - 1)];

        bool OnFoot()
        {
            PlayerNet me = PlayerNet.Local;
            return me != null && me.controller != null && me.controller.Source != null
                   && me.controller.Source.Context == Snow.Input.InputContext.OnFoot;
        }

        void Keys()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null || Snow.Input.SnowInput.UiOpen) return;   // the dev pult holds the keyboard
            if (!OnFoot())
            {
                if (_up) Raise(false);
                return;
            }
            if (kb.pKey.wasPressedThisFrame) { Raise(!_up); return; }
            if (!_up) return;
            float now = Time.time;
            int step = 0;
            if (kb.upArrowKey.wasPressedThisFrame) step = -1;
            if (kb.downArrowKey.wasPressedThisFrame) step = 1;
            if (Mouse.current != null)
            {
                _wheel += Mouse.current.scroll.ReadValue().y;
                if (Mathf.Abs(_wheel) >= 1f) { step = _wheel > 0f ? -1 : 1; _wheel = 0f; }
            }
            if (step != 0) Step(step);
            // E: a press opens / goes back; held on the list it opens the mode
            if (kb.eKey.wasPressedThisFrame) { _eDownAt = now; _eHeldUsed = false; }
            if (kb.eKey.isPressed && _eDownAt >= 0f && !_eHeldUsed && _screen == Screen.List && now - _eDownAt >= HoldMode)
            {
                _eHeldUsed = true;
                _screen = Screen.Mode;
                Press(1);
                Play("ui.pager.open", 0.6f, 1f);
            }
            if (kb.eKey.wasReleasedThisFrame && _eDownAt >= 0f)
            {
                if (!_eHeldUsed) Read();
                _eDownAt = -1f;
            }
            if (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame)
            {
                if (_screen == Screen.List) Raise(false);
                else { _screen = Screen.List; Press(1); }
            }
        }

        void Step(int d)
        {
            Press(d < 0 ? 0 : 2);
            if (_screen == Screen.Mode)
            {
                _mode = (_mode + d + 3) % 3;
                Play("ui.pager.mode", 0.6f, 1f);
                return;
            }
            int was = _sel;
            _sel = Mathf.Clamp(_sel + d, 0, Mathf.Max(0, _inbox.Count - 1));
            if (_screen == Screen.Message && _sel != was) Open();
        }

        void Read()
        {
            Press(1);
            if (_screen == Screen.List) { if (_inbox.Count > 0) Open(); }
            else _screen = Screen.List;                                // the message or the mode: back to the list
        }

        void Open()
        {
            _screen = Screen.Message;
            _openAt = Time.time;
            Play("ui.pager.open", 0.6f, 1f);
            Message m = Picked;
            if (m != null) m.read = true;
            if (_typing == m) _typing = null;                         // the same text would print twice
        }

        void Press(int key)
        {
            _keyAt[key] = Time.time;
            _lightUntil = Time.time + Backlight;
            Play("ui.pager.button", 0.6f, 1f, "ui.letter.key", 0.4f, 1.15f);
        }

        void Raise(bool up)
        {
            if (up == _up) return;
            _up = up;
            _moveAt = Time.time;
            _screen = Screen.List;
            _sel = 0;
            _eDownAt = -1f;
            if (up) _lightUntil = Time.time + Backlight;
            Play(up ? "ui.pager.raise" : "ui.pager.lower", 0.7f, 1f, "ui.letter.key", 0.45f, up ? 0.8f : 0.7f);
            InteractionSensor.DeviceRaised = up;
        }

        // ---------------------------------------------------------------- the model on the camera
        void Device()
        {
            float k = Mathf.Clamp01((Time.time - _moveAt) / RaiseSeconds);
            k = Mathf.Ceil(k * 4f) / 4f;                               // PS1: in steps, no easing
            float shown = _up ? k : 1f - k;
            if (shown <= 0f)
            {
                if (_model != null && _model.gameObject.activeSelf) _model.gameObject.SetActive(false);
                if (_dim != null && _dim.gameObject.activeSelf) _dim.gameObject.SetActive(false);
                return;
            }
            if (!EnsureModel()) return;
            if (!_model.gameObject.activeSelf) _model.gameObject.SetActive(true);
            Transform t = _model.transform;
            t.localPosition = Vector3.Lerp(LoweredPos, HeldPos, shown);
            t.localRotation = HeldRot;
            float now = Time.time;
            bool lit = now < _lightUntil;
            if (_model.lcd != null) _model.lcd.sharedMaterial = lit ? _model.lcdGlow : _model.lcdOff;
            if (_model.backlight != null) _model.backlight.enabled = lit;          // the screen lights the keys and the case
            if (_dim != null)
            {
                if (!_dim.gameObject.activeSelf) _dim.gameObject.SetActive(true);
                _dimBlock.SetColor("_BaseColor", new Color(0f, 0f, 0f, DimAlpha * shown));
                _dim.SetPropertyBlock(_dimBlock);
            }
            bool unread = _inbox.Exists(m => !m.read);
            if (_model.led != null) _model.led.sharedMaterial = unread && Mathf.Repeat(now, 1f) < 0.15f ? _model.ledOn : _model.ledOff;
            for (int i = 0; i < 3; i++)
            {
                if (_model.keys[i] == null) continue;
                bool down = now - _keyAt[i] < KeyPress;
                _model.keys[i].localPosition = _keyRest[i] + (down ? new Vector3(0f, 0f, -0.0012f) : Vector3.zero);   // into the body
            }
        }

        bool EnsureModel()
        {
            if (_model != null) return true;
            PlayerNet me = PlayerNet.Local;
            Camera cam = me == null ? null : me.view != null && me.view.cam != null ? me.view.cam : me.cam;
            if (cam == null) return false;
            var prefab = Resources.Load<GameObject>("SnowPager");
            if (prefab == null) { Debug.LogWarning("[Snow] Pager: Resources/SnowPager not found (Tools / Snow / Items / Build Pager)."); enabled = false; return false; }
            GameObject go = Instantiate(prefab, cam.transform, false);
            go.name = "Pager (held)";
            _model = go.GetComponent<PagerModel>();
            if (_model == null) { Debug.LogWarning("[Snow] Pager: SnowPager has no PagerModel."); Destroy(go); enabled = false; return false; }
            for (int i = 0; i < 3; i++) if (_model.keys[i] != null) _keyRest[i] = _model.keys[i].localPosition;
            if (_model.dim != null)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(quad.GetComponent<Collider>());
                quad.name = "Pager (dim)";
                quad.transform.SetParent(cam.transform, false);
                quad.transform.localPosition = new Vector3(0f, 0f, DimDistance);
                quad.transform.localScale = new Vector3(2f, 2f, 1f);
                _dim = quad.GetComponent<Renderer>();
                _dim.sharedMaterial = _model.dim;
                _dim.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _dim.receiveShadows = false;
                _dimBlock = new MaterialPropertyBlock();
            }
            return true;
        }

        // a cue; while it has no clips yet, the stand-in cue (snow-07's ui.pager.* come after the GPU queue, 26.10)
        void Play(string cue, float volume, float pitch, string standIn = null, float standInVolume = 0f, float standInPitch = 1f)
        {
            if (_audio == null) return;
            AudioClip clip = SnowAudio.Clip(cue);
            if (clip == null && standIn != null)
            {
                clip = SnowAudio.Clip(standIn);
                volume = standInVolume;
                pitch = standInPitch;
            }
            if (clip == null) return;
            _audio.pitch = pitch;
            _audio.PlayOneShot(clip, volume * SnowAudio.Bus(SoundBus.Ui));
        }

        // ---------------------------------------------------------------- the arrival typing
        // the typed position shows a visible character (spaces and line ends are silent)
        bool Printed(int index)
        {
            foreach (string line in _lines)
            {
                if (index < line.Length) return line[index] != ' ';
                index -= line.Length + 1;
                if (index < 0) return false;
            }
            return false;
        }

        // the typed position is a line end (the carriage returns there)
        bool LineEnd(int index)
        {
            foreach (string line in _lines)
            {
                if (index == line.Length) return true;
                if (index < line.Length) return false;
                index -= line.Length + 1;
            }
            return false;
        }

        // when each position comes out: an even beat, longer on a space-less stop, after punctuation and at a line end
        void Schedule()
        {
            int n = 0;
            foreach (string line in _lines) n += line.Length + 1;
            _at = new float[n];
            float t = BeepLead;
            int k = 0;
            foreach (string line in _lines)
            {
                foreach (char ch in line)
                {
                    _at[k++] = t;
                    t += (ch == ' ' ? 0.7f : 1f) / TypeRate;
                    if (ch == '.' || ch == '!' || ch == '?') t += StopSentence;
                    else if (ch == ',' || ch == '—' || ch == ':') t += StopComma;
                }
                _at[k++] = t;
                t += StopLine;
            }
        }

        // ---------------------------------------------------------------- drawing
        void OnGUI()
        {
            if (Application.isBatchMode || Event.current.type != EventType.Repaint) return;
            if (_up && _screen == Screen.Message && Picked != null) DrawMessage(Picked, Time.time);
            else if (_typing != null) DrawTyping(Time.time);
            else if (_up) DrawKeys();
            if (_model != null && _model.gameObject.activeSelf) DrawLcd();
        }

        static readonly Color Dim = new Color(0.663f, 0.651f, 0.612f);
        static float X => Hud.SafeX;
        static float Y => Hud.Px(150f);
        static float Lh => Hud.Px(27f);
        static float WrapWidth => Mathf.Min(Hud.Px(620f), UnityEngine.Screen.width * 0.45f);

        static Color A(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);

        // the pager's keys as a typed line (the keys warm, the words dim, as under the thing in the hand)
        static readonly (string, string)[] KeysClosed = { ("P", "открыть пейджер") };
        static readonly (string, string)[] KeysList = { ("↑↓", "выбрать"), ("E", "открыть"), ("держать E", "режим"), ("P", "убрать") };
        static readonly (string, string)[] KeysMode = { ("↑↓", "режим"), ("E", "готово") };
        static readonly (string, string)[] KeysMessage = { ("↑↓", "другое"), ("E", "к списку"), ("P", "убрать") };
        static readonly (string, string)[] KeysMessageOne = { ("E", "к списку"), ("P", "убрать") };

        // the raised pager on its list or its mode, no message on screen: "PAGER" and its keys at the top left
        void DrawKeys()
        {
            GUIStyle st = Hud.Mono(21), small = Hud.Mono(18);
            float w = Hud.Typed(X, Y, "PAGER", st, Hud.Warm);
            Hud.Typed(X + w + Hud.Px(16f), Y + Hud.Px(3f), Loc.T("пейджер"), small, Dim);
            PocketsHud.KeysLine(X, Y + Lh + Hud.Px(2f), _screen == Screen.Mode ? KeysMode : KeysList, small, 1f);
        }

        // "PAGER · K.  17:30" + "пейджер": the pager's own line, there at once with the beep
        float Header(Message m, float y, float alpha, string extra = null)
        {
            GUIStyle st = Hud.Mono(21), small = Hud.Mono(18);
            string head = "PAGER · " + m.from + (m.time.Length > 0 ? "   " + m.time : "");
            float w = Hud.Typed(X, y, head, st, A(Hud.Warm, alpha));
            Hud.Typed(X + w + Hud.Px(16f), y + Hud.Px(3f), extra ?? Loc.T("пейджер"), small, A(Dim, alpha));
            return y + Lh;
        }

        void DrawTyping(float now)
        {
            GUIStyle st = Hud.Mono(21);
            if (_lines == null || !Mathf.Approximately(_linesScale, Hud.S))
            {
                _lines = Hud.Wrap(Loc.T(_typing.task.text), st, WrapWidth);   // again when the screen changes size
                _linesScale = Hud.S;
                Schedule();
                _shown = Mathf.Min(_shown, Total);
            }
            float alpha = 1f;
            if (_at != null && _shown >= _at.Length) alpha = Mathf.Clamp01(1f - (now - _doneAt - HoldBase - HoldPerChar * Total) / FadeSeconds);
            float y = Header(_typing, Y, alpha);
            int left = _shown;
            bool cursor = _at != null && _shown < _at.Length && Mathf.Repeat(now, 0.5f) < 0.3f;
            for (int i = 0; i < _lines.Count && (left > 0 || cursor); i++)
            {
                string line = _lines[i];
                bool partial = left <= line.Length;
                float w = Hud.Typed(X, y, partial ? line.Substring(0, Mathf.Max(0, left)) : line, st, A(Hud.Ink, alpha));
                if (partial)
                {
                    if (cursor) Hud.Typed(X + w, y, "_", st, A(Hud.Ink, alpha));
                    break;
                }
                left -= line.Length + 1;
                y += Lh;
            }
            if (!_up && _at != null && _shown >= _at.Length) PocketsHud.KeysLine(X, y + Hud.Px(8f), KeysClosed, Hud.Mono(18), alpha);
        }

        // the message opened on the pager: its full text at the top left, printed quickly (it is being re-read), the keys
        // as a dim line under it
        void DrawMessage(Message m, float now)
        {
            GUIStyle st = Hud.Mono(21), small = Hud.Mono(18);
            int index = _inbox.Count - 1 - _inbox.IndexOf(m);
            float y = Header(m, Y, 1f, $"{index + 1}/{_inbox.Count}");
            int left = Mathf.FloorToInt((now - _openAt) * ReadRate);
            foreach (string line in Hud.Wrap(Loc.T(m.task.text), st, WrapWidth))
            {
                if (left > 0) Hud.Typed(X, y, line.Substring(0, Mathf.Min(line.Length, left)), st, Hud.Ink);
                left -= line.Length + 1;
                y += Lh;
            }
            PocketsHud.KeysLine(X, y + Hud.Px(8f), _inbox.Count > 1 ? KeysMessage : KeysMessageOne, small, 1f);
        }

        // the pager's own screen: 20 x 4 characters drawn over the LCD glass along its projected corners
        void DrawLcd()
        {
            if (_model.lcd == null) return;
            Camera cam = _model.GetComponentInParent<Camera>();
            MeshFilter mf = _model.lcd.GetComponent<MeshFilter>();
            if (cam == null || mf == null || mf.sharedMesh == null || mf.sharedMesh.vertexCount < 4) return;
            Vector3[] v = mf.sharedMesh.vertices;
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = cam.WorldToViewportPoint(_model.lcd.transform.TransformPoint(v[i]));
                if (p.z <= 0f) return;
                _lcdCorners[i] = new Vector3(p.x * UnityEngine.Screen.width, (1f - p.y) * UnityEngine.Screen.height, 0f);
            }
            // top two by height, then left / right
            System.Array.Sort(_lcdCorners, (a, b) => a.y.CompareTo(b.y));
            Vector3 tl = _lcdCorners[0].x <= _lcdCorners[1].x ? _lcdCorners[0] : _lcdCorners[1];
            Vector3 tr = _lcdCorners[0].x <= _lcdCorners[1].x ? _lcdCorners[1] : _lcdCorners[0];
            Vector3 bl = _lcdCorners[2].x <= _lcdCorners[3].x ? _lcdCorners[2] : _lcdCorners[3];
            float w = Vector3.Distance(tl, tr), h = Vector3.Distance(tl, bl);
            if (w < 20f || h < 8f) return;
            float angle = Mathf.Atan2(tr.y - tl.y, tr.x - tl.x) * Mathf.Rad2Deg;
            // the LCD grid of the texture: 124 x 40 px, cells 6 x 9 from (2, 2)
            float cellW = w * 6f / 124f, rowH = h * 9f / 40f, mx = w * 2f / 124f, my = h * 2f / 40f;
            float px = Mathf.Min(cellW / 0.55f, rowH * 1.05f);
            GUIStyle st = Hud.Mono(Mathf.Max(1, Mathf.RoundToInt(px / Hud.S)));
            bool lit = Time.time < _lightUntil;
            Color ink = lit ? new Color(0.05f, 0.10f, 0.04f, 0.95f) : new Color(0.16f, 0.20f, 0.15f, 0.6f);
            Matrix4x4 saved = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, new Vector2(tl.x, tl.y));
            GUI.color = ink;
            string[] rows = LcdRows();
            for (int r = 0; r < Rows; r++)
                GUI.Label(new Rect(tl.x + mx, tl.y + my + r * rowH - (px - rowH) * 0.5f, w, px * 1.3f), rows[r], st);
            GUI.color = Color.white;
            GUI.matrix = saved;
        }

        string[] LcdRows()
        {
            var r = new string[Rows];
            int n = _inbox.Count, unread = 0;
            foreach (Message m in _inbox) if (!m.read) unread++;
            if (_screen == Screen.Mode)
            {
                r[0] = Loc.T("РЕЖИМ");
                string[] opt = { Loc.T("ЗВУК"), Loc.T("ВИБРО"), Loc.T("ТИХО") };
                for (int i = 0; i < 3; i++) r[i + 1] = (i == _mode ? ">" : " ") + opt[i];
                return r;
            }
            string head = n + " " + Loc.T("СООБЩ.") + (unread > 0 ? " •" + unread : "");
            r[0] = Fit(head.PadRight(Cols - 3) + "|||");
            if (n == 0)
            {
                r[1] = "";
                r[2] = Fit(Loc.T("НЕТ СООБЩЕНИЙ"));
                r[3] = "";
                return r;
            }
            if (_screen == Screen.Message && Picked != null)
            {
                Message m = Picked;
                string pos = $"{_sel + 1}/{n}";
                r[0] = Fit((m.from + " " + m.time).PadRight(Cols - pos.Length) + pos);
                r[1] = Fit("» " + Loc.T("НА ЭКРАНЕ"));
                List<string> words = Wrap20(Loc.T(m.task.text));
                r[2] = words.Count > 0 ? words[0] : "";
                r[3] = Fit("↑↓ " + Loc.T("ДРУГОЕ"));
                return r;
            }
            int first = Mathf.Clamp(_sel - 1, 0, Mathf.Max(0, n - 3));
            for (int i = 0; i < 3; i++)
            {
                int k = first + i;
                if (k >= n) { r[i + 1] = ""; continue; }
                Message m = _inbox[n - 1 - k];
                r[i + 1] = Fit((k == _sel ? ">" : " ") + (m.read ? " " : "•") + m.time + " " + m.from + " " + Loc.T(m.task.text).Replace('\n', ' '));
            }
            return r;
        }

        static string Fit(string s) => s.Length > Cols ? s.Substring(0, Cols) : s;

        static List<string> Wrap20(string text)
        {
            var lines = new List<string>();
            string line = "";
            foreach (string word in text.Replace('\n', ' ').Split(' '))
            {
                string next = line.Length == 0 ? word : line + " " + word;
                if (next.Length > Cols && line.Length > 0) { lines.Add(line); line = word; }
                else line = next;
            }
            if (line.Length > 0) lines.Add(Fit(line));
            return lines;
        }
    }
}
