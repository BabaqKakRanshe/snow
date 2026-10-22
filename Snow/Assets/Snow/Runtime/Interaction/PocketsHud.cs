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
    /// all numbers (docs/dev/Tuning/Cargo.md).
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
        string _toast;
        AudioSource _audio;
        GUIStyle _num, _name, _toastStyle, _hintStyle;

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
                if (held != null && !Had(held)) { Play(_takeClip); Toast(held.label); }          // from the world: at once
                else if (held == null && _prevHeld != null && !_prevHeld.Pocketable) Play(_dropClip, 0.6f);   // heavy thing put down
            }
            if (_hand != _prevHand)
            {
                if (_hand != null)
                {
                    Pop(_hand.Slot);
                    if (Had(_hand)) { Play(_switchClip); Toast(_hand.label); }
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

        void Toast(string text)
        {
            _toast = text;
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
            EnsureStyles();
            float now = Time.time;
            float awake = Mathf.Clamp01(1f - (now - _awakeAt - awakeSeconds) / 0.6f);
            float alpha = Mathf.Lerp(restAlpha, 1f, awake);
            const float w = 92f, h = 34f, gap = 4f;
            float x0 = (Screen.width - (N * w + (N - 1) * gap)) * 0.5f, y = Screen.height - h - 16f;
            int lit = now - sensor.RequestedAt < 0.4f ? sensor.RequestedSlot : _hand != null ? _hand.Slot : -1;   // asked for: lit at once
            var warm = new Color(1f, 0.8f, 0.4f);

            for (int s = 0; s < N; s++)
            {
                var r = new Rect(x0 + s * (w + gap), y, w, h);
                bool on = s == lit;
                if (on) r.y -= 5f;
                float pop = s == _popSlot ? 1f + 0.16f * (1f - EaseOut(Mathf.Clamp01((now - _popAt) / 0.2f))) : 1f;
                Matrix4x4 saved = GUI.matrix;
                if (pop > 1.001f) GUIUtility.ScaleAroundPivot(new Vector2(pop, pop), r.center);
                GUI.color = new Color(0.08f, 0.08f, 0.09f, (on ? 0.85f : 0.7f) * alpha);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                if (on)
                {
                    GUI.color = new Color(warm.r, warm.g, warm.b, alpha);
                    GUI.DrawTexture(new Rect(r.x, r.y, r.width, 2f), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(r.x, r.yMax - 2f, r.width, 2f), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(r.x, r.y, 2f, r.height), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(r.xMax - 2f, r.y, 2f, r.height), Texture2D.whiteTexture);
                }
                CargoItem item = _slots[s];
                GUI.color = new Color(1f, 1f, 1f, (item != null ? 0.75f : 0.35f) * alpha);
                GUI.Label(new Rect(r.x + 4f, r.y + 1f, 16f, 14f), (s + 1).ToString(), _num);
                if (item != null)
                {
                    GUI.color = on ? new Color(warm.r, warm.g, warm.b, alpha) : new Color(1f, 1f, 1f, 0.9f * alpha);
                    GUI.Label(new Rect(r.x + 4f, r.y + 8f, r.width - 8f, r.height - 8f), Short(item.label), _name);
                }
                GUI.matrix = saved;
            }

            // the name of what just came into the hand, above the bar; how to use it while the bar is awake
            float since = now - _toastAt;
            if (_toast != null && since < 1.4f)
            {
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01((1.4f - since) / 0.4f));
                GUI.Label(new Rect(0f, y - 34f, Screen.width, 24f), _toast, _toastStyle);
            }
            if (_hand != null && awake > 0.01f)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.7f * awake);
                GUI.Label(new Rect(0f, y - 56f, Screen.width, 20f), "[ЛКМ] применить   [Q] в карман   [G] бросить   [1–8] / колесо — карманы", _hintStyle);
            }
            GUI.color = Color.white;
        }

        static string Short(string label)
        {
            if (string.IsNullOrEmpty(label)) return "?";
            return label.Length <= 13 ? label : label.Substring(0, 12) + "…";
        }

        void EnsureStyles()
        {
            if (_num != null) return;
            _num = new GUIStyle(GUI.skin.label) { fontSize = 10, alignment = TextAnchor.UpperLeft, padding = new RectOffset(0, 0, 0, 0) };
            _name = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Clip, padding = new RectOffset(0, 0, 0, 0) };
            _toastStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, alignment = TextAnchor.MiddleCenter };
            _hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
            foreach (GUIStyle st in new[] { _num, _name, _toastStyle, _hintStyle }) st.normal.textColor = Color.white;
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
