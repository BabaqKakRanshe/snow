using System.Collections.Generic;
using UnityEngine;

namespace Snow.Voice
{
    /// <summary>
    /// Voice clips by file name from Resources/Voice (VOICE_LIST.md §7: <c>vo_kessler_hello_01_en.wav</c> is loaded as
    /// <c>Voice/vo_kessler_hello_01_en</c>). A line without its clip still plays: silence of an estimated length under
    /// the radio noise, with its subtitle, so the whole system runs before the voices are generated. The radio's own
    /// sounds (press-to-talk click, squelch tail, static) and the whistle are made here (TEMPORARY until real SFX).
    /// </summary>
    public static class VoiceClips
    {
        const string Folder = "Voice/";
        static readonly Dictionary<string, AudioClip> s_cache = new Dictionary<string, AudioClip>();
        static readonly Dictionary<string, AudioClip[]> s_sets = new Dictionary<string, AudioClip[]>();
        static AudioClip s_click, s_squelch, s_static, s_whistle;

        public static AudioClip Of(VoiceLine line) => line == null ? null : Load(line.File);

        public static AudioClip Load(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            if (s_cache.TryGetValue(file, out AudioClip c)) return c;
            string name = System.IO.Path.GetFileNameWithoutExtension(file);
            c = Resources.Load<AudioClip>(Folder + name);
            s_cache[file] = c;
            return c;
        }

        /// <summary>Variants <c>name_1</c> … <c>name_8</c> that exist (body sounds); empty when none.</summary>
        public static AudioClip[] Set(string name)
        {
            if (s_sets.TryGetValue(name, out AudioClip[] set)) return set;
            var list = new List<AudioClip>();
            for (int i = 1; i <= 8; i++)
            {
                AudioClip c = Resources.Load<AudioClip>(Folder + name + "_" + i);
                if (c != null) list.Add(c);
            }
            set = list.ToArray();
            s_sets[name] = set;
            return set;
        }

        /// <summary>
        /// Seconds the line takes: the clip, or ~15 characters a second plus a breath for a missing one. The server and
        /// every peer compute the same number, so a conversation's timing does not depend on who has the clips.
        /// </summary>
        public static float Duration(VoiceLine line)
        {
            if (line == null) return 0f;
            AudioClip c = Of(line);
            if (c != null) return c.length;
            int chars = string.IsNullOrEmpty(line.En) ? 8 : line.En.Length;
            return Mathf.Clamp(0.35f + chars * 0.065f, 0.4f, 12f);
        }

        // ---------------------------------------------------------------- made sounds (TEMPORARY)
        public static AudioClip Click { get { Make(); return s_click; } }
        public static AudioClip Squelch { get { Make(); return s_squelch; } }
        public static AudioClip Static { get { Make(); return s_static; } }
        public static AudioClip Whistle { get { Make(); return s_whistle; } }

        static void Make()
        {
            if (s_click != null) return;
            const float pi2 = Mathf.PI * 2f;
            var rnd = new System.Random(8);
            float Noise() => (float)rnd.NextDouble() * 2f - 1f;
            float lp = 0f, lp2 = 0f;
            // press-to-talk: a hard plastic click with a short burst of carrier
            s_click = Synth("radio_click", 0.06f, t => Mathf.Sin(pi2 * 1800f * t) * Mathf.Exp(-t / 0.004f) * 0.6f + Noise() * Mathf.Exp(-t / 0.012f) * 0.5f);
            // squelch tail: a hiss that dies when the other side lets go
            s_squelch = Synth("radio_squelch", 0.22f, t =>
            {
                lp += 0.6f * (Noise() - lp);
                return lp * Mathf.Min(1f, t / 0.01f) * Mathf.Exp(-t / 0.07f) * 0.9f;
            });
            // static: a loop of band-limited noise with a slow flutter
            s_static = Synth("radio_static", 2f, t =>
            {
                lp2 += 0.35f * (Noise() - lp2);
                return lp2 * (0.75f + 0.25f * Mathf.Sin(pi2 * 3f * t)) * 0.5f;
            });
            // whistle: two notes up, the "hey, over here" of a worker
            s_whistle = Synth("body_whistle", 0.55f, t =>
            {
                float f = t < 0.22f ? Mathf.Lerp(1500f, 1900f, t / 0.22f) : Mathf.Lerp(1700f, 2300f, (t - 0.25f) / 0.3f);
                float env = t < 0.22f ? Mathf.Sin(Mathf.PI * t / 0.22f) : t < 0.25f ? 0f : Mathf.Sin(Mathf.PI * (t - 0.25f) / 0.3f);
                return Mathf.Sin(pi2 * f * t) * env * 0.5f + Noise() * env * 0.04f;
            });
        }

        static AudioClip Synth(string name, float seconds, System.Func<float, float> sample)
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
