using System.Collections.Generic;
using Snow.Aircraft;
using Snow.Audio;
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
    /// (CargoItem carry) and InteractionSensor; sends nothing. Sounds are sound-library cues (ui.pocket.*), silent until
    /// they have clips. All numbers (docs/dev/Tuning/Cargo.md). Look (UI pick 23.10, D3 «Протокол»): the occupied pockets as typed lines
    /// at the bottom left, shown for a moment after a pocket change and while Tab is held, gone otherwise. UI pick 26.10
    /// (variant C «Один столбец», RESEARCH_UI_3.md): no block at the bottom right - the thing in the hand opens its keys
    /// on the line under it, a heavy thing carried in both hands stands first; the Tab list (InteractionOverlay) stacks above.
    /// </summary>
    public class PocketsHud : MonoBehaviour
    {
        public InteractionSensor sensor;
        [Tooltip("s the pockets list stays after something happened")] public float awakeSeconds = 2.5f;
        [Tooltip("Typewriter font with Cyrillic for the list (PT Mono / TT2020); empty = an OS mono, TEMPORARY")] public Font mono;
        [Range(0f, 1f)] public float volume = 0.45f;

        const int N = CargoItem.PocketSlots;
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
                if (held != null && !Had(held)) { Play("ui.pocket.take"); Toast(true); }          // from the world: at once
                else if (held == null && _prevHeld != null && !_prevHeld.Pocketable) Play("ui.pocket.throw", 0.6f);   // heavy thing put down
            }
            if (_hand != _prevHand)
            {
                if (_hand != null)
                {
                    Pop(_hand.Slot);
                    if (Had(_hand)) { Play("ui.pocket.switch"); Toast(false); }
                }
                if (_prevHand != null && _prevHand.CarriedBy(me)) Play("ui.pocket.in");
                else if (_prevHand != null) Play("ui.pocket.throw");                           // thrown (or used up)
            }
            if (sensor.MessageAt > _lastMessage) { _lastMessage = sensor.MessageAt; Play("ui.deny", 0.5f); }
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

        void Play(string cue, float scale = 1f)
        {
            Wake();
            if (_audio == null) return;
            AudioClip clip = SnowAudio.Clip(cue);
            if (clip == null) return;
            _audio.pitch = Random.Range(0.94f, 1.06f);
            _audio.PlayOneShot(clip, volume * scale);
        }

        static float EaseOut(float t) => 1f - (1f - t) * (1f - t) * (1f - t);

        // ---------------------------------------------------------------- drawing
        /// <summary>The top of the left column this frame (the pockets list's header, or its bottom anchor when hidden):
        /// InteractionOverlay stacks the Tab list ("Nahe · рядом") above it (UI pick 26.10, variant C).</summary>
        public static float ColumnTop { get; private set; }

        void OnGUI()
        {
            if (Application.isBatchMode || Event.current.type != EventType.Repaint || sensor == null || sensor.controller == null
                || sensor.controller.Source == null || sensor.controller.Source.Context != InputContext.OnFoot) return;
            float now = Time.time;
            float awake = Mathf.Clamp01(1f - (now - _awakeAt - awakeSeconds) / 0.6f);
            bool tab = Keyboard.current != null && Keyboard.current.tabKey.isPressed;      // on foot Tab is free (Controls.md)
            float alpha = tab ? 1f : awake;
            int lit = now - sensor.RequestedAt < 0.4f ? sensor.RequestedSlot : _hand != null ? _hand.Slot : -1;   // asked for: lit at once
            ColumnTop = Screen.height - Hud.Px(60f);
            if (alpha > 0.01f) InventoryList(lit, alpha, now, tab);
        }

        // UI pick 23.10 (D3 «Протокол», RESEARCH_UI_2.md) and 26.10 (variant C): the occupied pockets as typed lines at the
        // bottom left, no plates — "Inventar  карманы", then "▸3  Ключ «Schranke»  neu"; the pocket in the hand marked, the
        // others dimmer, its keys on the line under it ("ЛКМ выключить   Q в карман   G бросить"); a heavy thing carried in
        // both hands first, unnumbered, with its keys
        void InventoryList(int lit, float alpha, float now, bool tab)
        {
            GUIStyle st = Mono(21), small = Mono(18);
            float lh = Hud.Px(27f), x = Hud.SafeX;
            int count = 0;
            for (int s = 0; s < N; s++) if (_slots[s] != null) count++;
            CargoItem heavy = sensor.Held != null && System.Array.IndexOf(_slots, sensor.Held) < 0 ? sensor.Held : null;   // in the hands, not in a pocket
            if (count == 0 && heavy == null && !tab) return;
            bool keysUnderHand = _hand != null && _hand.Slot >= 0 && _hand.Slot < N && _slots[_hand.Slot] == _hand;
            int lines = 1 + Mathf.Max(1, count + (heavy != null ? 2 : 0)) + (keysUnderHand ? 1 : 0);
            float y = Screen.height - Hud.Px(60f) - lh * lines;
            ColumnTop = y;
            Color warm = new Color(Hud.Warm.r, Hud.Warm.g, Hud.Warm.b, alpha), ink = new Color(Hud.Ink.r, Hud.Ink.g, Hud.Ink.b, alpha);
            float hw = Typed(x, y, "Inventar", st, warm);
            Typed(x + hw + Hud.Px(16f), y + Hud.Px(3f), Loc.T("карманы"), small, new Color(0.663f, 0.651f, 0.612f, alpha));
            y += lh;
            float indent = Hud.Size("▸1", st).x + Hud.Px(14f);
            if (heavy != null)
            {
                Typed(x, y, "▸", st, warm);
                Typed(x + indent, y, Loc.T(heavy.label), st, ink);
                y += lh;
                KeysLine(x + indent, y + Hud.Px(2f), HandKeysFor(heavy), small, alpha);
                y += lh;
            }
            else if (count == 0)
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
                Typed(cx, y, (on ? "▸" : " ") + (s + 1), st, new Color(warm.r, warm.g, warm.b, a));
                cx += indent;
                cx += Typed(cx, y, Loc.T(item.label), st, Color.Lerp(new Color(ink.r, ink.g, ink.b, a), warm, flash));
                if (on && fresh) Typed(cx + Hud.Px(14f), y, "neu", st, warm);
                y += lh;
                if (item == _hand && keysUnderHand)
                {
                    KeysLine(x + indent, y + Hud.Px(2f), HandKeysFor(item), small, alpha);
                    y += lh;
                }
            }
        }

        static float Typed(float x, float y, string text, GUIStyle st, Color color) => Hud.Typed(x, y, text, st, color);

        // the typewriter face: the font set on the component, else the shared OS mono (Hud.Mono)
        GUIStyle Mono(int designPx) => Hud.Mono(designPx, mono);

        // the keys for the thing in the hand: LMB its own action (or used on the zone under the ray; a heavy item only with a
        // zone in sight), this frame's extras (NoteReader: T читать), into the pocket / put down, throw
        List<(string key, string text)> HandKeysFor(CargoItem item)
        {
            HandKeys.Clear();
            string act = item.TryGetComponent(out SurvivalItem tool) ? tool.ActionText() : null;
            if (string.IsNullOrEmpty(act) && (item.Pocketable || sensor.TargetVolume != null)) act = "применить";
            if (!string.IsNullOrEmpty(act)) HandKeys.Add(("ЛКМ", act));
            if (_extraFrame == Time.frameCount) HandKeys.AddRange(Extra);
            if (item.Pocketable) HandKeys.Add(("Q", "в карман"));
            else HandKeys.Add(("E", "поставить"));                    // InteractionSensor: Use puts a heavy item down
            HandKeys.Add(("G", "бросить"));
            return HandKeys;
        }

        // "ЛКМ выключить   Q в карман   G бросить": the keys warm, the words dim, no caps around the keys
        public static void KeysLine(float x, float y, IList<(string key, string text)> keys, GUIStyle st, float alpha)
        {
            Color warm = new Color(Hud.Warm.r, Hud.Warm.g, Hud.Warm.b, alpha), dim = new Color(0.663f, 0.651f, 0.612f, alpha);
            float cx = x;
            for (int i = 0; i < keys.Count; i++)
            {
                if (i > 0) cx += Hud.Px(20f);
                cx += Typed(cx, y, Loc.T(keys[i].key), st, warm) + Hud.Px(8f);
                cx += Typed(cx, y, Loc.T(keys[i].text), st, dim);
            }
        }
    }
}
