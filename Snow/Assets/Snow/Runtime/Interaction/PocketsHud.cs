using System.Collections.Generic;
using Snow.Aircraft;
using Snow.Cargo;
using Snow.Input;
using UnityEngine;

namespace Snow.Interaction
{
    /// <summary>
    /// The owner's pockets bar (user 21.10; Lethal Company / Phasmophobia hotbar, game-feel practice): eight slots at the
    /// bottom of the screen, the one in the hand lifted and lit. A slot pops the moment it is asked for (before the
    /// server answers), the name of what came into the hand shows above the bar for a moment, and the bar fades to a
    /// faint strip when nothing happens (small UI, Q11). Every change has a short sound: take, into the pocket, switch,
    /// throw, refusal — slightly different pitch each time so repeats do not grate. Reads the replicated pocket state
    /// (CargoItem carry) and InteractionSensor; sends nothing. Sounds are made here (TEMPORARY until real SFX), as are
    /// all numbers (docs/dev/Tuning/Cargo.md). Look (UI pick 23.10, version 3, <see cref="Hud"/>): numbered slots with a
    /// warm mark when full, the name of the slot in the hand on a plate above (with "новое" for a thing just taken from
    /// the world), and "в руке" at the bottom right with the keys for the thing in the hand.
    /// </summary>
    public class PocketsHud : MonoBehaviour
    {
        public InteractionSensor sensor;
        [Tooltip("s the bar stays bright after something happened")] public float awakeSeconds = 2.5f;
        [Range(0f, 1f)] public float restAlpha = 0.35f;
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
            float alpha = Mathf.Lerp(restAlpha, 1f, awake);
            float w = Hud.Px(52f), h = Hud.Px(46f), gap = Hud.Px(4f);
            float x0 = Mathf.Round((Screen.width - (N * w + (N - 1) * gap)) * 0.5f), y = Screen.height - Hud.Px(48f) - h;
            int lit = now - sensor.RequestedAt < 0.4f ? sensor.RequestedSlot : _hand != null ? _hand.Slot : -1;   // asked for: lit at once
            Color warm = Hud.Warm;

            for (int s = 0; s < N; s++)
            {
                var r = new Rect(x0 + s * (w + gap), y, w, h);
                bool on = s == lit;
                CargoItem item = _slots[s];
                float pop = s == _popSlot ? 1f + 0.16f * (1f - EaseOut(Mathf.Clamp01((now - _popAt) / 0.2f))) : 1f;
                Matrix4x4 saved = GUI.matrix;
                if (pop > 1.001f) GUIUtility.ScaleAroundPivot(new Vector2(pop, pop), r.center);
                if (item != null) Hud.Panel(r, alpha);
                else
                {
                    Hud.Fill(r, new Color(0.063f, 0.067f, 0.075f, 0.35f * alpha));
                    Hud.Outline(r, new Color(1f, 1f, 1f, 0.22f * alpha), Mathf.Max(1f, Hud.Px(1f)));
                }
                if (on) Hud.Outline(r, new Color(warm.r, warm.g, warm.b, alpha), Hud.Px(2f));
                Color num = on ? Color.white : item != null ? new Color(0.93f, 0.92f, 0.9f) : new Color(1f, 1f, 1f, 0.5f);
                Hud.Label(r, (s + 1).ToString(), Hud.Text(18, true, TextAnchor.MiddleCenter), new Color(num.r, num.g, num.b, num.a * alpha));
                if (item != null)
                    Hud.Fill(new Rect(r.x + Hud.Px(14f), r.yMax - Hud.Px(9f), r.width - Hud.Px(28f), Hud.Px(3f)), new Color(warm.r, warm.g, warm.b, 0.75f * alpha));
                GUI.matrix = saved;
            }

            // the name of the slot in the hand above the bar; "новое" while a thing just taken from the world is announced
            CargoItem shown = lit >= 0 && lit < N ? _slots[lit] : null;
            if (shown != null)
            {
                bool fresh = _toastNew && now - _toastAt < 1.4f;
                string num = (lit + 1).ToString(), dot = "·", label = Loc.T(shown.label), tag = Loc.T("новое");
                GUIStyle numSt = Hud.Text(22, true), st = Hud.Text(22), tagSt = Hud.Text(15, false, TextAnchor.MiddleCenter);
                float pad = Hud.Px(14f), sp = Hud.Px(8f), ph = Hud.Px(36f);
                float nw = Hud.Size(num, numSt).x, dw = Hud.Size(dot, st).x, lw = Hud.Size(label, st).x, tw = Hud.Size(tag, tagSt).x + Hud.Px(12f);
                float pw = pad * 2f + nw + sp + dw + sp + lw + (fresh ? sp + tw : 0f);
                var plate = new Rect(Mathf.Round((Screen.width - pw) * 0.5f), y - Hud.Px(8f) - ph, pw, ph);
                Hud.Panel(plate, alpha);
                float cx = plate.x + pad;
                Hud.Label(new Rect(cx, plate.y, nw + 2f, ph), num, numSt, new Color(warm.r, warm.g, warm.b, alpha));
                cx += nw + sp;
                Hud.Label(new Rect(cx, plate.y, dw + 2f, ph), dot, st, new Color(1f, 1f, 1f, 0.4f * alpha));
                cx += dw + sp;
                Hud.Label(new Rect(cx, plate.y, lw + 2f, ph), label, st, new Color(1f, 1f, 1f, alpha));
                cx += lw + sp;
                if (fresh)
                {
                    var tr = new Rect(cx, plate.y + (ph - Hud.Px(22f)) * 0.5f, tw, Hud.Px(22f));
                    Hud.Fill(tr, new Color(warm.r, warm.g, warm.b, 0.2f * alpha));
                    Hud.Label(tr, tag, tagSt, new Color(warm.r, warm.g, warm.b, alpha));
                }
            }

            // what is in the hand and its keys, bottom right
            CargoItem inHand = _hand != null ? _hand : sensor.Held;
            if (inHand != null) HandBlock(inHand, alpha);
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
