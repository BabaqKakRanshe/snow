using System.Collections.Generic;
using Snow.Input;
using Snow.Interaction;
using Snow.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Snow.Voice
{
    /// <summary>
    /// The voice on the screen of this peer (QUESTIONS_VOICE 6, 12, 13, 15; research §8 subtitles): subtitles of what is
    /// audible here, with the speaker and the device («Кесслер (рация): …», two lines at most, on a dark plate, on by
    /// default); the reply to a radio by choice — near a radio with an open question T opens the list, 1–9 chooses, T or
    /// Esc closes (the digits are taken from the pockets while it is open); the callout marks of everyone; the small "I can
    /// be heard" mark. Also plays the body sounds the server sends. Sizes TEMPORARY.
    /// </summary>
    public class VoiceHud : MonoBehaviour
    {
        const float PromptReach = 3f, PingSeconds = 6f, BodyRange = 40f;

        sealed class Sub { public VoiceEmitter From; public string Speaker, Text; public float Until; }
        sealed class Mark { public Vector3 Point; public float Until; public bool Mine; }

        static VoiceHud s_instance;
        static readonly List<Sub> s_subs = new List<Sub>();
        static readonly List<Mark> s_marks = new List<Mark>();

        bool _menu;
        uint _menuPrompt;

        /// <summary>A radio reply is on offer right now (T opens its menu): the note reader does not take T then.</summary>
        public static bool ReplyOffered { get; private set; }
        GUIStyle _sub, _opt, _hint, _mark;
        Texture2D _plate;

        internal static VoiceHud Ensure(GameObject host)
        {
            if (s_instance == null) s_instance = host.AddComponent<VoiceHud>();
            return s_instance;
        }

        // ---------------------------------------------------------------- feeds
        public static void Subtitle(VoiceEmitter from, string speaker, string text, float seconds)
        {
            if (string.IsNullOrEmpty(text)) return;
            s_subs.Add(new Sub { From = from, Speaker = speaker, Text = text, Until = Time.time + seconds });
            if (s_subs.Count > 6) s_subs.RemoveAt(0);
        }

        public static void ClearFrom(VoiceEmitter from)
        {
            for (int i = s_subs.Count - 1; i >= 0; i--)
                if (s_subs[i].From == from) s_subs.RemoveAt(i);
        }

        /// <summary>A body sound of a player arrived from the server: play it where it was made, or put up the mark.</summary>
        public static void BodyHeard(ulong client, BodySound sound, int variant, Vector3 point, bool mine)
        {
            if (sound == BodySound.Ping)
            {
                Snow.Audio.SnowAudio.Play("ui.ping", point);
                s_marks.Add(new Mark { Point = point, Until = Time.time + PingSeconds, Mine = mine });
                return;
            }
            if (mine) NoiseEvents.LocalMade(BodySounds.Radius(sound));
            if (Application.isBatchMode) return;
            AudioClip[] set = VoiceClips.Set(BodySounds.ClipName(sound));
            AudioClip clip = set.Length > 0 ? set[variant % set.Length] : null;
            if (clip == null)                                        // no recording: the generated library cue (user 26.10.2026)
            {
                string cue = BodyCue(sound, variant);
                if (cue != null) Snow.Audio.SnowAudio.Play(cue, point);
                return;
            }
            var go = new GameObject("Body " + sound);
            go.transform.position = point;
            var src = go.AddComponent<AudioSource>();
            src.spatialBlend = mine ? 0.3f : 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 1f;
            src.maxDistance = Mathf.Max(8f, Mathf.Min(BodyRange, BodySounds.Radius(sound) * 1.3f));
            src.dopplerLevel = 0f;
            src.pitch = 0.95f + 0.1f * ((variant * 37) % 10) / 10f;
            src.clip = clip;
            src.Play();
            Destroy(go, clip.length / src.pitch + 0.1f);
        }

        // ---------------------------------------------------------------- reply menu
        void Update()
        {
            Keyboard kb = Keyboard.current;
            VoiceNet.Prompt near = NearPrompt();
            bool onFoot = PlayerNet.Local != null && PlayerNet.Local.controller != null && PlayerNet.Local.controller.Source != null
                          && PlayerNet.Local.controller.Source.Context == InputContext.OnFoot;
            bool reader = InteractionSensor.ReaderOpen;
            bool offered = onFoot && near != null && !reader;
            if (offered != ReplyOffered) Snow.Audio.SnowAudio.PlayUi(offered ? "ui.reply.open" : "ui.reply.close", 0.7f);
            ReplyOffered = offered;
            if (_menu && (near == null || near.Id != _menuPrompt || !onFoot || reader)) _menu = false;
            if (kb != null && ReplyOffered)
            {
                if (kb.tKey.wasPressedThisFrame) { _menu = !_menu; _menuPrompt = near.Id; }
                else if (_menu && kb.escapeKey.wasPressedThisFrame) _menu = false;
                else if (_menu)
                    for (int i = 0; i < near.Options.Length && i < 9; i++)
                        if (kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame)
                        {
                            Snow.Audio.SnowAudio.PlayUi("radio.ptt.press");
                            VoiceNet.Choose(near.Id, i);
                            Snow.Audio.SnowAudio.PlayUi("ui.reply.select");
                            _pttReleaseAt = Time.time + 1.2f;
                            _menu = false;
                            break;
                        }
            }
            InteractionSensor.DigitsTaken = _menu;

            float now = Time.time;
            s_subs.RemoveAll(s => s.Until < now || s.From == null);
            s_marks.RemoveAll(m => m.Until < now);
        }

        static VoiceNet.Prompt NearPrompt()
        {
            PlayerNet me = PlayerNet.Local;
            if (me == null || VoiceNet.ClientPrompts.Count == 0) return null;
            Vector3 eye = me.view != null ? me.view.EyePosition : me.transform.position + Vector3.up * 1.6f;
            VoiceNet.Prompt best = null;
            float bestD = float.MaxValue;
            foreach (VoiceNet.Prompt p in VoiceNet.ClientPrompts.Values)
            {
                Vector3 at = AnchorNow(p);
                float d = Vector3.Distance(eye, at);
                if (d <= Mathf.Min(p.Radius, PromptReach) && d < bestD) { best = p; bestD = d; }
            }
            return best;
        }

        static Vector3 AnchorNow(VoiceNet.Prompt p)
        {
            Unity.Netcode.NetworkManager nm = Unity.Netcode.NetworkManager.Singleton;
            if (p.Anchor != 0 && nm != null && nm.SpawnManager != null && nm.SpawnManager.SpawnedObjects.TryGetValue(p.Anchor, out Unity.Netcode.NetworkObject no) && no != null)
                return no.transform.position;
            return p.Point;
        }

        // ---------------------------------------------------------------- drawing
        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint && Event.current.type != EventType.Layout) return;
            PlayerNet me = PlayerNet.Local;
            if (me == null) return;
            EnsureStyles();
            float k = Mathf.Max(0.6f, Screen.height / 1080f);
            Vector3 ear = me.view != null ? me.view.EyePosition : me.transform.position;

            DrawMarks(me, k);
            DrawSubtitles(ear, k);
            DrawPrompt(k);
            DrawHeard(k);
        }

        void DrawSubtitles(Vector3 ear, float k)
        {
            float w = Mathf.Min(Screen.width - 32f, 900f * k), y = Screen.height * 0.74f;
            int shown = 0;
            for (int i = s_subs.Count - 1; i >= 0 && shown < 2; i--)
            {
                Sub s = s_subs[i];
                if (s.From == null || Vector3.Distance(ear, s.From.transform.position) > s.From.Range) continue;
                string who = Loc.T(SpeakerLabel(s.Speaker)) + ": ";
                string text = who + s.Text;
                var content = new GUIContent(text);
                float h = _sub.CalcHeight(content, w - 24f * k) + 12f * k;
                var r = new Rect((Screen.width - w) * 0.5f, y - h, w, h);
                GUI.color = new Color(0f, 0f, 0f, 0.6f);
                GUI.DrawTexture(r, _plate);
                GUI.color = Color.white;
                GUI.Label(new Rect(r.x + 12f * k, r.y + 6f * k, r.width - 24f * k, r.height - 12f * k), text, _sub);
                y -= h + 4f * k;
                shown++;
            }
        }

        /// <summary>Speaker and device of a subtitle (QUESTIONS_VOICE 6). Game text is Russian; Loc.T translates.</summary>
        /// <summary>The generated library cue of a body sound (SOUND_LIST_FULL «Персонаж»); null for silent ones.</summary>
        static string BodyCue(BodySound sound, int variant)
        {
            switch (sound)
            {
                case BodySound.Callout: return variant % 2 == 0 ? "body.callout.whistle" : "body.callout.hey";
                case BodySound.Land: return "body.land";
                case BodySound.PainSmall: return "body.pain.small";
                case BodySound.PainBig: return "body.pain.big";
                case BodySound.EffortLight: return "body.effort.light";
                case BodySound.EffortHeavy: return "body.effort.heavy";
                case BodySound.Cough: return "body.cough";
                case BodySound.Shiver: return "body.shiver";
                case BodySound.Breath: return "body.breath.calm";
                case BodySound.Wake: return "body.wake";
                default: return null;
            }
        }

        public static string SpeakerLabel(string speaker)
        {
            switch (speaker)
            {
                case "kessler": return "Кесслер (рация)";
                case "radio_voice": return "??? (рация)";
                default: return speaker;
            }
        }

        void DrawPrompt(float k)
        {
            VoiceNet.Prompt p = NearPrompt();
            if (p == null) return;
            if (!_menu)
            {
                GUI.Label(new Rect(0f, Screen.height * 0.80f, Screen.width, 26f * k), Loc.T("[T] ответить по рации"), _hint);
                return;
            }
            float w = 420f * k, line = 28f * k, h = line * (p.Options.Length + 1) + 16f * k;
            var r = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.80f - h, w, h);
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(r, _plate);
            GUI.color = Color.white;
            GUI.Label(new Rect(r.x + 12f * k, r.y + 8f * k, w - 24f * k, line), Loc.T("Ответ по рации: [1–9] выбрать, [T] закрыть"), _hint);
            for (int i = 0; i < p.Options.Length; i++)
                GUI.Label(new Rect(r.x + 12f * k, r.y + 8f * k + line * (i + 1), w - 24f * k, line), $"[{i + 1}]  {Loc.T(p.Options[i])}", _opt);
        }

        void DrawMarks(PlayerNet me, float k)
        {
            Camera cam = me.cam != null ? me.cam : Camera.main;
            if (cam == null) return;
            foreach (Mark m in s_marks)
            {
                Vector3 v = cam.WorldToViewportPoint(m.Point);
                if (v.z <= 0f) continue;
                float a = Mathf.Clamp01((m.Until - Time.time) / 1.5f);
                var c = new Vector2(v.x * Screen.width, (1f - v.y) * Screen.height);
                GUI.color = m.Mine ? new Color(1f, 0.85f, 0.4f, a) : new Color(0.6f, 0.95f, 1f, a);
                GUI.Label(new Rect(c.x - 20f * k, c.y - 20f * k, 40f * k, 40f * k), "◆", _mark);
                GUI.Label(new Rect(c.x - 60f * k, c.y + 12f * k, 120f * k, 20f * k), $"{Vector3.Distance(me.transform.position, m.Point):0} {Loc.T("м")}", _hint);
            }
            GUI.color = Color.white;
        }

        float _heardSound = -10f, _pttReleaseAt = -1f;

        void LateUpdate()
        {
            if (_pttReleaseAt > 0f && Time.time >= _pttReleaseAt) { _pttReleaseAt = -1f; Snow.Audio.SnowAudio.PlayUi("radio.ptt.release"); }
        }

        void DrawHeard(float k)
        {
            float left = NoiseEvents.LocalHeardUntil - Time.time;
            if (NoiseEvents.LocalHeardUntil > _heardSound + 0.5f) { _heardSound = NoiseEvents.LocalHeardUntil; Snow.Audio.SnowAudio.PlayUi("ui.heard", 0.5f); }
            if (left <= 0f) return;
            GUI.color = new Color(0.85f, 0.95f, 1f, Mathf.Clamp01(left / 0.5f) * 0.85f);
            GUI.Label(new Rect(Screen.width - 160f * k, Screen.height - 64f * k, 140f * k, 24f * k), "≋ " + Loc.T("слышно"), _hint);
            GUI.color = Color.white;
        }

        void EnsureStyles()
        {
            if (_sub != null) return;
            float k = Mathf.Max(0.6f, Screen.height / 1080f);
            _sub = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(26f * k), wordWrap = true, alignment = TextAnchor.UpperCenter };
            _opt = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(22f * k), alignment = TextAnchor.MiddleLeft };
            _hint = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(16f * k), alignment = TextAnchor.MiddleCenter };
            _mark = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(26f * k), alignment = TextAnchor.MiddleCenter };
            foreach (GUIStyle st in new[] { _sub, _opt, _hint, _mark }) st.normal.textColor = Color.white;
            _plate = Texture2D.whiteTexture;
        }

        void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
            InteractionSensor.DigitsTaken = false;
        }
    }
}
