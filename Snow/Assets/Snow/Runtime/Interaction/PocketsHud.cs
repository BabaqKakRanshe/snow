using System.Collections.Generic;
using Snow.Aircraft;
using Snow.Cargo;
using Snow.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Snow.Interaction
{
    /// <summary>
    /// The owner's pockets bar (user 21.10; Lethal Company / Phasmophobia hotbar, game-feel practice): eight slots at the
    /// bottom of the screen, the one in the hand lifted and lit. A slot pops the moment it is asked for (before the
    /// server answers), the name of what came into the hand shows above the bar for a moment, and the bar fades to a
    /// faint strip when nothing happens (small UI, Q11). Every change has a short sound: take, into the pocket, switch,
    /// throw, refusal — slightly different pitch each time so repeats do not grate. Reads the replicated pocket state
    /// (CargoItem carry) and InteractionSensor; sends nothing. Sounds are made here (TEMPORARY until real SFX), as are
    /// all numbers (docs/dev/Tuning/Cargo.md). Look (UI pick 23.10, D3 «Протокол»): the occupied pockets as typed lines
    /// at the bottom left, shown for a moment after a pocket change and while Tab is held, gone otherwise; "в руке" at
    /// the bottom right with the keys for the thing in the hand (<see cref="Hud"/>, version 3 — not decided yet).
    /// </summary>
    public class PocketsHud : MonoBehaviour
    {
        public InteractionSensor sensor;
        [Tooltip("s the pockets list stays after something happened")] public float awakeSeconds = 2.5f;
        [Tooltip("Typewriter font with Cyrillic for the list (PT Mono / TT2020); empty = an OS mono, TEMPORARY")] public Font mono;
        [Range(0f, 1f)] public float volume = 0.45f;

        const int N = CargoItem.PocketSlots;
        static AudioClip _takeClip, _pocketClip, _switchClip, _dropClip, _denyClip;
        readonly CargoItem[] _slots = new CargoItem[N];
        readonly CargoItem[] _had = new CargoItem[N];
        CargoItem _hand, _prevHand, _prevHeld;
        float _awakeAt = -10f, _popAt = -10f, _toastAt = -10f, _lastMessage = -10f, _lastRequest = -10f;
        int _popSlot = -1;
        bool _toastNew;
        AudioSource _audio;
        static readonly List<(string key, string text)> Extra = new List<(string, string)>();
        static readonly List<(string key, string text)> HandKeys = new List<(string, string)>();
        static int _extraFrame = -1;

        /// <summary>One more key for the thing in the hand, this frame only (NoteReader: [T] читать).</summary>
        public static void HandHint(string key, string text)
        {
            if (_extraFrame != Time.frameCount) { Extra.Clear(); _extraFrame = Time.frameCount; }
            Extra.Add((key, text));
        }

        void Start()
        {
            if (Application.isBatchMode) return;
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
            MakeClips();
        }

        void Update()
        {
            if (sensor == null || sensor.net == null) return;
            ulong me = sensor.net.OwnerClientId;
            System.Array.Clear(_slots, 0, N);
            _hand = null;
            foreach (CargoItem c in CargoItem.All)
            {
                if (!c.IsSpawned || !c.CarriedBy(me) || c.Slot >= N) continue;
                _slots[c.Slot] = c;
                if (c.Carry == CarryMode.Hand) _hand = c;
            }

            CargoItem held = sensor.Held;
            if (held != _prevHeld)
            {
                if (held != null && !Had(held)) { Play(_takeClip); Toast(true); }          // from the world: at once
                else if (held == null && _prevHeld != null && !_prevHeld.Pocketable) Play(_dropClip, 0.6f);   // heavy thing put down
            }
            if (_hand != _prevHand)
            {
                if (_hand != null)
                {
                    Pop(_hand.Slot);
                    if (Had(_hand)) { Play(_switchClip); Toast(false); }
                }
                if (_prevHand != null && _prevHand.CarriedBy(me)) Play(_pocketClip);
                else if (_prevHand != null) Play(_dropClip);                           // thrown (or used up)
            }
            if (sensor.MessageAt > _lastMessage) { _lastMessage = sensor.MessageAt; Play(_denyClip, 0.5f); }
            if (sensor.RequestedAt > _lastRequest)
            {
                _lastRequest = sensor.RequestedAt;
                if (sensor.RequestedSlot >= 0) Pop(sensor.RequestedSlot);
                Wake();
            }
            System.Array.Copy(_slots, _had, N);
            _prevHand = _hand;
            _prevHeld = held;
        }

        bool Had(CargoItem item) => System.Array.IndexOf(_had, item) >= 0;

        void Wake() => _awakeAt = Time.time;

        void Pop(int slot)
        {
            _popSlot = slot;
            _popAt = Time.time;
            Wake();
        }

        void Toast(bool fromWorld)
        {
            _toastNew = fromWorld;
            _toastAt = Time.time;
            Wake();
        }

        void Play(AudioClip clip, float scale = 1f)
        {
            Wake();
            if (_audio == null || clip == null) return;
            _audio.pitch = Random.Range(0.94f, 1.06f);
            _audio.PlayOneShot(clip, volume * scale);
        }

        static float EaseOut(float t) => 1f - (1f - t) * (1f - t) * (1f - t);

        // ---------------------------------------------------------------- drawing
        void OnGUI()
        {
            if (Application.isBatchMode || Event.current.type != EventType.Repaint || sensor == null || sensor.controller == null
                || sensor.controller.Source == null || sensor.controller.Source.Context != InputContext.OnFoot) return;
            float now = Time.time;
            float awake = Mathf.Clamp01(1f - (now - _awakeAt - awakeSeconds) / 0.6f);
            bool tab = Keyboard.current != null && Keyboard.current.tabKey.isPressed;      // on foot Tab is free (Controls.md)
            float alpha = tab ? 1f : awake;
            int lit = now - sensor.RequestedAt < 0.4f ? sensor.RequestedSlot : _hand != null ? _hand.Slot : -1;   // asked for: lit at once
            if (alpha > 0.01f) InventoryList(lit, alpha, now, tab);

            // what is in the hand and its keys, bottom right
            CargoItem inHand = _hand != null ? _hand : sensor.Held;
            if (inHand != null) HandBlock(inHand, Mathf.Lerp(0.35f, 1f, awake));
        }

        // UI pick 23.10 (D3 «Протокол», RESEARCH_UI_2.md): the occupied pockets as typed lines at the bottom left, no
        // plates — "Inventar  карманы", then "▸3  Ключ «Schranke»  neu"; the pocket in the hand marked, the others dimmer
        void InventoryList(int lit, float alpha, float now, bool tab)
        {
            GUIStyle st = Mono(21), small = Mono(18);
            float lh = Hud.Px(27f), x = Hud.SafeX;
            int count = 0;
            for (int s = 0; s < N; s++) if (_slots[s] != null) count++;
            if (count == 0 && !tab) return;
            float y = Screen.height - Hud.Px(60f) - lh * (1 + Mathf.Max(1, count));
            Color warm = new Color(Hud.Warm.r, Hud.Warm.g, Hud.Warm.b, alpha), ink = new Color(0.9f, 0.874f, 0.784f, alpha);
            float hw = Typed(x, y, "Inventar", st, warm);
            Typed(x + hw + Hud.Px(16f), y + Hud.Px(3f), Loc.T("карманы"), small, new Color(0.663f, 0.651f, 0.612f, alpha));
            y += lh;
            if (count == 0)
            {
                Typed(x, y, Loc.T("пусто"), st, new Color(ink.r, ink.g, ink.b, 0.7f * alpha));
                return;
            }
            bool fresh = _toastNew && now - _toastAt < 1.4f;
            for (int s = 0; s < N; s++)
            {
                CargoItem item = _slots[s];
                if (item == null) continue;
                bool on = s == lit;
                float flash = s == _popSlot ? 1f - EaseOut(Mathf.Clamp01((now - _popAt) / 0.3f)) : 0f;   // the line that just changed
                float a = on ? alpha : 0.7f * alpha;
                float cx = x;
                cx += Typed(cx, y, (on ? "▸" : " ") + (s + 1), st, new Color(warm.r, warm.g, warm.b, a));
                cx += Hud.Px(14f);
                cx += Typed(cx, y, Loc.T(item.label), st, Color.Lerp(new Color(ink.r, ink.g, ink.b, a), warm, flash));
                if (on && fresh) Typed(cx + Hud.Px(14f), y, "neu", st, warm);
                y += lh;
            }
        }

        // typewriter text with a 2 px dark outline (no plate); returns its width
        static float Typed(float x, float y, string text, GUIStyle st, Color color)
        {
            Vector2 size = Hud.Size(text, st);
            var r = new Rect(x, y, size.x + 2f, size.y);
            float o = Mathf.Max(1f, Hud.Px(2f));
            GUI.color = new Color(0.04f, 0.04f, 0.05f, 0.9f * color.a);
            for (int i = 0; i < 8; i++)
            {
                float ox = i == 0 || i == 3 || i == 5 ? -o : i == 2 || i == 4 || i == 7 ? o : 0f;
                float oy = i < 3 ? -o : i > 4 ? o : 0f;
                GUI.Label(new Rect(r.x + ox, r.y + oy, r.width, r.height), text, st);
            }
            GUI.color = color;
            GUI.Label(r, text, st);
            GUI.color = Color.white;
            return size.x;
        }

        static Font _osMono;
        static readonly Dictionary<int, GUIStyle> MonoStyles = new Dictionary<int, GUIStyle>();
        static float _monoScale = -1f;

        // the typewriter face: the font set on the component, else an OS mono (TEMPORARY until PT Mono / TT2020 is in the
        // project, RESEARCH_UI_2.md §4)
        GUIStyle Mono(int designPx)
        {
            if (!Mathf.Approximately(Hud.S, _monoScale)) { MonoStyles.Clear(); _monoScale = Hud.S; }
            if (MonoStyles.TryGetValue(designPx, out GUIStyle st)) return st;
            Font f = mono;
            if (f == null)
            {
                if (_osMono == null) _osMono = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Courier New", "Liberation Mono" }, 21);
                f = _osMono;
            }
            st = new GUIStyle(GUI.skin.label)
            {
                font = f, fontSize = Mathf.Max(9, Mathf.RoundToInt(designPx * Hud.S)), alignment = TextAnchor.UpperLeft,
                wordWrap = false, clipping = TextClipping.Overflow, padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0),
            };
            st.normal.textColor = Color.white;
            MonoStyles[designPx] = st;
            return st;
        }

        void HandBlock(CargoItem item, float alpha)
        {
            HandKeys.Clear();
            // LMB: the tool's own action, else the item used on the zone under the ray (a heavy item only with a zone in
            // sight); no key cap without text
            string act = item.TryGetComponent(out SurvivalItem tool) ? tool.ActionText() : null;
            if (string.IsNullOrEmpty(act) && (item.Pocketable || sensor.TargetVolume != null)) act = "применить";
            if (!string.IsNullOrEmpty(act)) HandKeys.Add(("ЛКМ", act));
            if (_extraFrame == Time.frameCount) HandKeys.AddRange(Extra);
            if (item.Pocketable) HandKeys.Add(("Q", "в карман"));
            else HandKeys.Add(("E", "поставить"));                    // InteractionSensor: Use puts a heavy item down
            HandKeys.Add(("G", "бросить"));
            string caption = Loc.T("в руке").ToUpperInvariant(), name = Loc.T(item.label);
            GUIStyle capSt = Hud.Text(13, false, TextAnchor.MiddleRight), nameSt = Hud.Text(22, false, TextAnchor.MiddleRight);
            float pad = Hud.Px(14f), capH = Hud.Px(18f), nameH = Hud.Px(30f), keysH = Hud.Px(28f), sp = Hud.Px(4f);
            float keysW = Hud.Hints(0f, 0f, keysH, HandKeys, 17, alpha, false);
            float bw = Mathf.Max(Hud.Size(name, nameSt).x, Mathf.Max(Hud.Size(caption, capSt).x, keysW)) + 2f * pad;
            float bh = Hud.Px(10f) + capH + sp + nameH + sp + keysH + Hud.Px(12f);
            var r = new Rect(Screen.width - Hud.SafeX - bw, Screen.height - Hud.Px(48f) - bh, bw, bh);
            Hud.Panel(r, alpha);
            float cy = r.y + Hud.Px(10f), iw = bw - 2f * pad;
            Hud.Label(new Rect(r.x + pad, cy, iw, capH), caption, capSt, new Color(1f, 1f, 1f, 0.6f * alpha));
            cy += capH + sp;
            Hud.Label(new Rect(r.x + pad, cy, iw, nameH), name, nameSt, new Color(1f, 1f, 1f, alpha));
            cy += nameH + sp;
            Hud.Hints(r.xMax - pad - keysW, cy, keysH, HandKeys, 17, alpha);
        }

        // ---------------------------------------------------------------- sounds (made once, TEMPORARY)
        static void MakeClips()
        {
            if (_takeClip != null) return;
            const float pi2 = Mathf.PI * 2f;
            var rnd = new System.Random(21);
            float Noise() => (float)rnd.NextDouble() * 2f - 1f;
            float lp = 0f;
            // take: a soft knock of the thing in the palm
            _takeClip = Make("pocket_take", 0.08f, t => Mathf.Sin(pi2 * 420f * t) * Mathf.Exp(-t / 0.018f) * 0.8f + Noise() * Mathf.Exp(-t / 0.003f) * 0.4f);
            // into the pocket: a cloth rustle (low-passed noise, quick attack)
            _pocketClip = Make("pocket_in", 0.18f, t =>
            {
                lp += 0.25f * (Noise() - lp);
                return lp * Mathf.Min(1f, t / 0.015f) * Mathf.Exp(-t / 0.06f) * 1.6f;
            });
            // switch: a small click
            _switchClip = Make("pocket_switch", 0.04f, t => Mathf.Sin(pi2 * 1100f * t) * Mathf.Exp(-t / 0.006f) * 0.5f + Noise() * Mathf.Exp(-t / 0.002f) * 0.2f);
            // throw: a short swish
            _dropClip = Make("pocket_throw", 0.2f, t =>
            {
                lp += 0.08f * (Noise() - lp);
                return lp * Mathf.Sin(Mathf.PI * t / 0.2f) * 2.2f;
            });
            // refusal: a dull low buzz
            _denyClip = Make("pocket_deny", 0.12f, t => (Mathf.Sin(pi2 * 150f * t) + 0.4f * Mathf.Sin(pi2 * 300f * t)) * Mathf.Exp(-t / 0.05f) * 0.45f);
        }

        static AudioClip Make(string name, float seconds, System.Func<float, float> sample)
        {
            const int rate = 22050;
            int n = Mathf.CeilToInt(seconds * rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(sample(i / (float)rate), -1f, 1f);
            var clip = AudioClip.Create(name, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
