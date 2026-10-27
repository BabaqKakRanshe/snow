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
    /// P raises the pager to the lower left of the centre (the SnowPager model); its own small screen
    /// is only a picker: the count, the unread, the signal, and the list - time, sender, the first words. ↑ / ↓ or the wheel
    /// pick, E opens: the full text prints at the top left and the pager shows "» НА ЭКРАНЕ"; ↑ / ↓ go to the neighbour
    /// message, E or Esc back to the list; Esc or P on the list lowers it. Holding E on the list opens the mode: sound /
    /// vibrate / silent. Any key lights the screen for 5 s (it is hard to read in the dark otherwise); the LED blinks while
    /// something is unread. The pager is drawn by its own camera far below the world at <see cref="StageAt"/> with its own
    /// narrow lens at the screen's resolution (RESEARCH_UI_3 §6: through the PS1 frame's ~240 lines it smeared, at 480 lines
    /// the LCD text fell apart, with the world's wide lens it stretched in the corner), laid over the frame in OnGUI,
    /// the world dimmed behind it; its keys are one typed line under it. Where it is held up and down and the eye's lens come
    /// from the prefab Resources/PagerView (<see cref="PagerView"/>, set by hand). The LCD is a dot texture - grey-green glass, the
    /// 5 x 7 cells faintly visible, the characters in dark dots (PagerFont) - and the pager turns its screen to the eye.
    /// While the pager is up
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
        const float RaiseSeconds = 0.34f, LowerSeconds = 0.2f, Backlight = 5f, HoldMode = 0.6f, ReadRate = 70f, KeyPress = 0.12f;
        // game feel (user 27.10: "no animation, no game feel"): the raise comes up face-down and turns to the eye with a small
        // overshoot; the pager lags behind the head (sway), bobs with the steps and breathes when still; a key press dips the
        // whole pager under the thumb; an open message brings it closer; vibrate shakes it in the hand; the LCD redraws row by row
        const float Overshoot = 1.15f;
        const float SwayPerDegPerSec = 0.022f, SwayMax = 5f, SwayShift = 0.0016f, SwaySmooth = 0.09f;
        const float BobHz = 0.85f, BobUp = 0.0032f, BobSide = 0.0042f, BreathAmp = 0.0008f, BreathHz = 0.25f;
        const float KickSeconds = 0.16f, KickDown = 0.0028f, KickPitch = 3f;
        const float LeanCloser = 0.014f, LeanUp = 0.006f, LeanSmooth = 0.12f, BuzzSeconds = 0.7f, BuzzShift = 0.0016f, RowDraw = 0.03f;
        const int Cols = 20, Rows = 4;
        // 27.10 user: "make the pager a prefab so I can place it myself" - the held and the lowered pose and the eye's lens
        // (its own narrow lens, §6, keeps the case from stretching) are in Resources/PagerView, read once when the pager is made.
        // The eye renders at the screen's resolution: at 480 lines the LCD's 124 dots got ~1.3 px each and the text fell apart
        Vector3 _heldPos, _lowPos;
        Quaternion _heldRot, _lowRot;
        // the world behind the raised pager darkens (RESEARCH_UI_3.md §5, after Alien: Isolation's blur): a black quad behind it
        const float DimAlpha = 0.35f;
        // the pager's own eye: a camera far below the world (it sees only the pager and its lights) rendering at screen size
        static readonly Vector3 StageAt = new Vector3(0f, -3000f, 0f);
        // the pager's own lights. URP falls off as 1/d², so a light 3 cm from the case blows it white at any sane intensity
        // (27.10: 1.4 and then 0.22 both did): the backlight's spill stands 12 cm in front of the glass and is very weak
        const float BacklightIntensity = 0.01f, BacklightRange = 0.3f, BacklightAhead = 0.12f;

        enum Screen { List, Message, Mode }

        class Message
        {
            public TaskDef task;
            public string from, time, text;   // text: the letter itself, or one of its hints (Hinted)
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
        Camera _eye, _main;
        RenderTexture _rt;
        Transform _stage;
        float _raised;                     // how far the pager is up, 0..1 (the stepped raise)
        bool _up;
        float _moveAt = -10f, _lightUntil, _openAt, _eDownAt = -1f, _wheel;
        bool _eHeldUsed;
        Screen _screen = Screen.List;
        int _sel, _mode;                   // _sel: 0 = the newest; _mode: 0 sound, 1 vibrate, 2 silent
        readonly float[] _keyAt = { -10f, -10f, -10f };
        readonly Vector3[] _keyRest = new Vector3[3];
        // the LCD as dots (§6): a 124 x 40 grid of 5 x 7 cells (the texture's own layout) at 4 px per dot, 3 px lit
        const int LcdW = 124, LcdH = 40, LcdDot = 4;
        static readonly Color32 Glass = new Color32(122, 140, 98, 255), DotOff = new Color32(118, 136, 95, 255), DotOn = new Color32(26, 34, 20, 255);
        Texture2D _lcdTex;
        Color32[] _lcdPx;
        Material _lcdLit, _lcdDark;
        string _lcdShown;
        Quaternion _lastCamRot = Quaternion.identity;
        Vector3 _lastCamPos;
        Vector2 _sway, _swayVel;
        float _bobPhase, _bobWeight, _lean, _leanVel, _kickAt = -10f, _buzzUntil = -10f, _lcdAt = -10f;
        bool _camKnown;

        void Awake() => _board = GetComponent<TaskBoard>();

        void OnEnable()
        {
            TaskBoard.Given += OnGiven;
            TaskBoard.Hinted += OnHinted;
        }

        void OnDisable()
        {
            TaskBoard.Given -= OnGiven;
            TaskBoard.Hinted -= OnHinted;
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
            if (_stage != null) Destroy(_stage.gameObject);
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
        }

        void OnGiven(TaskDef t)
        {
            if (!t.letter || string.IsNullOrEmpty(t.text) || !_got.Add(t.id)) return;
            Enqueue(t, t.text);
        }

        // a hint for a letter task comes as another message from its sender (user 27.10, QUESTIONS_6 §7а 12: on day 1 only K.'s
        // pager hints - no subtitle, no radio; TaskHud leaves letter tasks' hints to the pager)
        void OnHinted(TaskDef t, string hint)
        {
            if (!t.letter || string.IsNullOrEmpty(hint) || !_got.Add(t.id + "|" + hint)) return;
            Enqueue(t, hint);
        }

        void Enqueue(TaskDef t, string text)
        {
            float h = TaskBoard.Hour != null ? TaskBoard.Hour() : -1f;
            _queue.Enqueue(new Message
            {
                task = t, text = text, from = string.IsNullOrEmpty(t.from) ? "K." : t.from,
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
                _buzzUntil = Time.time + BuzzSeconds;
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
            _kickAt = Time.time;
            _lcdAt = Time.time;
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
            if (up) { _lightUntil = Time.time + Backlight; _lcdAt = Time.time + RaiseSeconds * 0.6f; _camKnown = false; }
            Play(up ? "ui.pager.raise" : "ui.pager.lower", 0.7f, 1f, "ui.letter.key", 0.45f, up ? 0.8f : 0.7f);
            InteractionSensor.DeviceRaised = up;
        }

        // ---------------------------------------------------------------- the model on the camera
        void Device()
        {
            float u = Mathf.Clamp01((Time.time - _moveAt) / (_up ? RaiseSeconds : LowerSeconds));
            float shown = _up ? EaseOutBack(u) : 1f - u * u * u;          // up: a small overshoot and back; down: falls away
            _raised = Mathf.Clamp01(shown);
            if (!_up && u >= 1f)
            {
                if (_model != null && _model.gameObject.activeSelf) _model.gameObject.SetActive(false);
                if (_eye != null) _eye.enabled = false;
                return;
            }
            if (!EnsureModel()) return;
            if (!_model.gameObject.activeSelf) _model.gameObject.SetActive(true);
            Eye();
            Transform t = _model.transform;
            float now = Time.time;
            Feel(t, shown, now);
            bool lit = now < _lightUntil;
            if (_model.lcd != null) _model.lcd.sharedMaterial = _lcdLit != null ? (lit ? _lcdLit : _lcdDark) : lit ? _model.lcdGlow : _model.lcdOff;
            if (_model.backlight != null) _model.backlight.enabled = false;        // 27.10 user: no light of its own - the glass glows, nothing else
            UpdateLcd();
            bool unread = _inbox.Exists(m => !m.read);
            if (_model.led != null) _model.led.sharedMaterial = unread && Mathf.Repeat(now, 1f) < 0.15f ? _model.ledOn : _model.ledOff;
            for (int i = 0; i < 3; i++)
            {
                if (_model.keys[i] == null) continue;
                bool down = now - _keyAt[i] < KeyPress;
                _model.keys[i].localPosition = _keyRest[i] + (down ? new Vector3(0f, 0f, -0.0012f) : Vector3.zero);   // into the body
            }
        }

        static float EaseOutBack(float x)
        {
            float c1 = Overshoot, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }

        // where the pager is this frame: the raise path (from below, face down, turning to the eye) plus the hand's life
        void Feel(Transform t, float shown, float now)
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Transform cam = _main != null ? _main.transform : null;
            Vector2 target = Vector2.zero;
            float speed = 0f;
            if (cam != null)
            {
                if (_camKnown)
                {
                    Vector3 d = (Quaternion.Inverse(_lastCamRot) * cam.rotation).eulerAngles;
                    float yaw = Mathf.DeltaAngle(0f, d.y) / dt, pitch = Mathf.DeltaAngle(0f, d.x) / dt;     // deg / s
                    target = new Vector2(Mathf.Clamp(-yaw * SwayPerDegPerSec, -SwayMax, SwayMax), Mathf.Clamp(-pitch * SwayPerDegPerSec, -SwayMax, SwayMax));
                    Vector3 v = (cam.position - _lastCamPos) / dt;
                    speed = new Vector2(v.x, v.z).magnitude;
                }
                _lastCamRot = cam.rotation;
                _lastCamPos = cam.position;
                _camKnown = true;
            }
            _sway = Vector2.SmoothDamp(_sway, target, ref _swayVel, SwaySmooth, Mathf.Infinity, dt);
            _bobWeight = Mathf.MoveTowards(_bobWeight, Mathf.Clamp01(speed / 2.5f), dt * 4f);
            _bobPhase += dt * BobHz * Mathf.Max(0.6f, speed) * Mathf.PI * 2f;
            _lean = Mathf.SmoothDamp(_lean, _up && _screen == Screen.Message ? 1f : 0f, ref _leanVel, LeanSmooth, Mathf.Infinity, dt);
            float kt = (now - _kickAt) / KickSeconds, kick = kt >= 0f && kt < 1f ? Mathf.Sin(Mathf.PI * kt) : 0f;
            Vector3 pos = Vector3.LerpUnclamped(_lowPos, _heldPos, shown);
            pos += new Vector3(_sway.x * SwayShift, _sway.y * SwayShift, 0f);
            pos += new Vector3(Mathf.Sin(_bobPhase) * BobSide * _bobWeight, -Mathf.Abs(Mathf.Sin(_bobPhase)) * BobUp * _bobWeight, 0f);
            pos.y += Mathf.Sin(now * BreathHz * Mathf.PI * 2f) * BreathAmp * (1f - _bobWeight);
            pos += new Vector3(0.012f * _lean, LeanUp * _lean, -LeanCloser * _lean);
            pos.y -= KickDown * kick;
            float roll = 0f;
            if (now < _buzzUntil && _raised > 0.5f)
            {
                pos += new Vector3(Random.Range(-BuzzShift, BuzzShift), Random.Range(-BuzzShift, BuzzShift), 0f);
                roll = Random.Range(-1.5f, 1.5f);
            }
            t.localPosition = pos;
            Quaternion pose = Quaternion.SlerpUnclamped(_lowRot, _heldRot, shown);   // the prefab's turns: down at the hip, up
            t.localRotation = Quaternion.Euler(_sway.y + KickPitch * kick, _sway.x, roll - 3f * _lean) * pose;
        }

        bool EnsureModel()
        {
            if (_model != null) return true;
            PlayerNet me = PlayerNet.Local;
            Camera cam = me == null ? null : me.view != null && me.view.cam != null ? me.view.cam : me.cam;
            if (cam == null) return false;
            var prefab = Resources.Load<PagerView>("PagerView");
            if (prefab == null || prefab.pager == null || prefab.lowered == null)
            {
                Debug.LogWarning("[Snow] Pager: Resources/PagerView not found or incomplete (Tools / Snow / Items / Build Pager).");
                enabled = false;
                return false;
            }
            _main = cam;
            _stage = new GameObject("Pager (stage)").transform;
            _stage.position = StageAt;
            PagerView view = Instantiate(prefab, _stage, false);
            view.name = "Pager (eye)";
            view.transform.localPosition = Vector3.zero;                       // the eye is the origin the poses are given in
            view.transform.localRotation = Quaternion.identity;
            GameObject go = view.pager.gameObject;
            go.name = "Pager (held)";
            _heldPos = view.pager.localPosition;
            _heldRot = view.pager.localRotation;
            _lowPos = view.lowered.localPosition;
            _lowRot = view.lowered.localRotation;
            _eye = view.GetComponent<Camera>();
            _eye.clearFlags = CameraClearFlags.SolidColor;
            _eye.backgroundColor = new Color(0f, 0f, 0f, 0f);         // transparent round the pager
            _eye.nearClipPlane = 0.01f;
            _eye.farClipPlane = 2f;
            _eye.allowHDR = false;
            _eye.allowMSAA = false;
            _eye.enabled = false;
            _model = go.GetComponent<PagerModel>();
            if (_model == null) { Debug.LogWarning("[Snow] Pager: SnowPager has no PagerModel."); Destroy(go); enabled = false; return false; }
            for (int i = 0; i < 3; i++) if (_model.keys[i] != null) _keyRest[i] = _model.keys[i].localPosition;
            BuildLcd();
            if (_model.backlight != null)
            {
                _model.backlight.intensity = BacklightIntensity;
                _model.backlight.range = BacklightRange;
                if (_model.lcd != null)                                        // in front of the glass (the prefab's front is +Z)
                    _model.backlight.transform.localPosition = _model.lcd.transform.localPosition + new Vector3(0f, 0f, BacklightAhead);
            }
            if (_model.fill != null) _model.fill.enabled = false;              // 27.10 user: no light of its own, the world's light only
            return true;
        }

        // the eye has its own lens and renders the pager into a screen-sized texture (laid over the screen in OnGUI)
        void Eye()
        {
            if (_eye == null || _main == null) return;
            float aspect = (float)Mathf.Max(16, UnityEngine.Screen.width) / Mathf.Max(16, UnityEngine.Screen.height);
            int h = Mathf.Max(16, UnityEngine.Screen.height), w = Mathf.Max(16, UnityEngine.Screen.width);
            if (_rt == null || _rt.width != w || _rt.height != h)
            {
                if (_rt != null) { _rt.Release(); Destroy(_rt); }
                _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "Pager eye", filterMode = FilterMode.Point };
                _eye.targetTexture = _rt;
            }
            _eye.aspect = aspect;
            _eye.enabled = true;
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
            if (_model != null && _model.gameObject.activeSelf && _rt != null)
            {
                var full = new Rect(0f, 0f, UnityEngine.Screen.width, UnityEngine.Screen.height);
                Hud.Fill(full, new Color(0f, 0f, 0f, DimAlpha * _raised));   // the world behind steps back
                GUI.DrawTexture(full, _rt, ScaleMode.StretchToFill, true);
                if (_up) DrawKeys();
            }
            if (_up && _screen == Screen.Message && Picked != null) DrawMessage(Picked, Time.time);
            else if (_typing != null) DrawTyping(Time.time);
        }

        static readonly Color Dim = new Color(0.663f, 0.651f, 0.612f);
        static float X => Hud.SafeX;
        static float Y => Hud.Px(150f);
        static float Lh => Hud.Px(27f);
        static float WrapWidth => Mathf.Min(Hud.Px(620f), UnityEngine.Screen.width * 0.45f);

        static Color A(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);

        // the pager's keys as a typed line (the keys warm, the words dim, as under the thing in the hand)
        static readonly (string, string)[] KeysClosed = { ("P", "открыть пейджер") };

        // the keys (§6): one typed line centred under the pager, for this screen
        static readonly (string, string)[] KeysList = { ("↑↓", "выбрать"), ("E", "открыть"), ("держать E", "режим"), ("P", "убрать") };
        static readonly (string, string)[] KeysMessage = { ("↑↓", "другое"), ("E", "к списку"), ("P", "убрать") };
        static readonly (string, string)[] KeysMode = { ("↑↓", "режим"), ("E", "готово") };

        void DrawKeys()
        {
            if (_eye == null || _model == null) return;
            var mr = _model.GetComponent<MeshRenderer>();
            if (mr == null) return;
            Bounds b = mr.bounds;
            float minX = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                Vector3 v = _eye.WorldToViewportPoint(corner);
                if (v.z <= 0f) return;
                float sx = v.x * UnityEngine.Screen.width, sy = (1f - v.y) * UnityEngine.Screen.height;
                minX = Mathf.Min(minX, sx); maxX = Mathf.Max(maxX, sx); maxY = Mathf.Max(maxY, sy);
            }
            var keys = _screen == Screen.Mode ? KeysMode : _screen == Screen.Message ? KeysMessage : KeysList;
            GUIStyle small = Hud.Mono(18);
            float w = 0f;
            for (int i = 0; i < keys.Length; i++)
                w += (i > 0 ? Hud.Px(20f) : 0f) + Hud.Size(Loc.T(keys[i].Item1), small).x + Hud.Px(8f) + Hud.Size(Loc.T(keys[i].Item2), small).x;
            float x = Mathf.Clamp((minX + maxX) * 0.5f - w * 0.5f, Hud.SafeX, UnityEngine.Screen.width - Hud.SafeX - w);
            float y = Mathf.Min(maxY + Hud.Px(10f), UnityEngine.Screen.height - Hud.SafeY - Lh);
            PocketsHud.KeysLine(x, y, keys, small, 1f);
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
                _lines = Hud.Wrap(Loc.T(_typing.text), st, WrapWidth);   // again when the screen changes size
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
            foreach (string line in Hud.Wrap(Loc.T(m.text), st, WrapWidth))
            {
                if (left > 0) Hud.Typed(X, y, line.Substring(0, Mathf.Min(line.Length, left)), st, Hud.Ink);
                left -= line.Length + 1;
                y += Lh;
            }
        }

        // the pager's own screen (§6): a dot texture on the LCD glass - grey-green glass, the 5 x 7 cells faintly visible, the
        // characters in dark dots (PagerFont, capitals); lit = the same texture glowing, dark = it unlit and dimmer
        void BuildLcd()
        {
            if (_model.lcd == null || _model.lcdGlow == null || _model.lcdOff == null) return;
            _lcdTex = new Texture2D(LcdW * LcdDot, LcdH * LcdDot, TextureFormat.RGBA32, false)
            {
                name = "Pager LCD", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
            };
            _lcdPx = new Color32[_lcdTex.width * _lcdTex.height];
            _lcdLit = new Material(_model.lcdGlow) { name = "Pager LCD lit" };
            _lcdLit.SetTexture("_BaseMap", _lcdTex);
            _lcdLit.SetTexture("_EmissionMap", _lcdTex);
            _lcdLit.SetColor("_EmissionColor", new Color(0.85f, 0.85f, 0.85f));
            _lcdLit.EnableKeyword("_EMISSION");
            _lcdDark = new Material(_model.lcdOff) { name = "Pager LCD dark" };
            _lcdDark.SetTexture("_BaseMap", _lcdTex);
            _lcdDark.SetColor("_BaseColor", new Color(0.55f, 0.55f, 0.55f));
            _lcdShown = null;
        }

        // redraws the dots when the rows (or how many of them are drawn yet) change
        void UpdateLcd()
        {
            if (_lcdTex == null) return;
            string[] rows = LcdRows();
            int drawn = Mathf.Clamp(Mathf.FloorToInt((Time.time - _lcdAt) / RowDraw) + 1, 0, Rows);   // a redraw goes top to bottom
            var key = new System.Text.StringBuilder();
            for (int r = 0; r < Rows; r++) key.Append(r < drawn ? rows[r] : "").Append('\n');
            string k = key.ToString();
            if (k == _lcdShown) return;
            _lcdShown = k;
            for (int i = 0; i < _lcdPx.Length; i++) _lcdPx[i] = Glass;
            for (int r = 0; r < Rows; r++)
            {
                string line = r < drawn && rows[r] != null ? rows[r].ToUpperInvariant() : "";
                for (int c = 0; c < Cols; c++)
                {
                    byte[] g = null;
                    if (c < line.Length) PagerFont.Glyphs.TryGetValue(line[c], out g);
                    for (int y = 0; y < PagerFont.H; y++)
                        for (int x = 0; x < PagerFont.W; x++)
                        {
                            bool on = g != null && (g[y] & (1 << (PagerFont.W - 1 - x))) != 0;
                            Dot(2 + c * 6 + x, 2 + r * 9 + y, on ? DotOn : DotOff);
                        }
                }
            }
            _lcdTex.SetPixels32(_lcdPx);
            _lcdTex.Apply(false);
        }

        // one LCD dot: a whole 4 x 4 cell (27.10: at 480 lines a gap between dots is under a pixel and rippled); grid y runs down,
        // texture y up
        void Dot(int gx, int gy, Color32 col)
        {
            int tw = LcdW * LcdDot, x0 = gx * LcdDot, y0 = (LcdH - 1 - gy) * LcdDot;
            for (int y = 0; y < LcdDot; y++)
                for (int x = 0; x < LcdDot; x++)
                    _lcdPx[(y0 + y) * tw + x0 + x] = col;
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
                List<string> words = Wrap20(Loc.T(m.text));
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
                r[i + 1] = Fit((k == _sel ? ">" : " ") + (m.read ? " " : "•") + m.time + " " + m.from + " " + Loc.T(m.text).Replace('\n', ' '));
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
