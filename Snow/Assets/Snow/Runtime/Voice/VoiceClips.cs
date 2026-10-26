using System.Collections.Generic;
using UnityEngine;

namespace Snow.Voice
{
    /// <summary>
    /// Voice clips by file name from Resources/Voice (VOICE_LIST.md §7: <c>vo_kessler_hello_01_en.wav</c> is loaded as
    /// <c>Voice/vo_kessler_hello_01_en</c>). A line without its clip still plays: silence of an estimated length under
    /// the radio noise, with its subtitle, so the whole system runs before the voices are generated. The radio's own
    /// sounds (click, squelch, static) are sound-library cues (<see cref="VoiceEmitter"/>).
    /// A Russian interface plays the <c>_ru</c> twin of an <c>_en</c> file when it exists (user 26.10: voice in English
    /// and Russian), and falls back to the English clip.
    /// </summary>
    public static class VoiceClips
    {
        const string Folder = "Voice/";
        static readonly Dictionary<string, AudioClip> s_cache = new Dictionary<string, AudioClip>();
        static readonly Dictionary<string, AudioClip[]> s_sets = new Dictionary<string, AudioClip[]>();

        /// <summary>The clip to play in the player's language.</summary>
        public static AudioClip Of(VoiceLine line)
        {
            if (line == null) return null;
            AudioClip ru = Loc.English ? null : Load(RussianFile(line.File));
            return ru != null ? ru : Load(line.File);
        }

        /// <summary><c>vo_kessler_hello_01_en.wav</c> → <c>vo_kessler_hello_01_ru.wav</c>; null for a file without <c>_en</c>.</summary>
        static string RussianFile(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            int at = file.LastIndexOf("_en.", System.StringComparison.Ordinal);
            return at < 0 ? null : file.Substring(0, at) + "_ru." + file.Substring(at + 4);
        }

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
        /// every peer compute the same number, so a conversation's timing does not depend on who has the clips. With both
        /// languages it is the longer of the two, so it is also the same for peers playing different languages.
        /// </summary>
        public static float Duration(VoiceLine line)
        {
            if (line == null) return 0f;
            AudioClip en = Load(line.File), ru = Load(RussianFile(line.File));
            if (en != null || ru != null) return Mathf.Max(en != null ? en.length : 0f, ru != null ? ru.length : 0f);
            int chars = string.IsNullOrEmpty(line.En) ? 8 : line.En.Length;
            return Mathf.Clamp(0.35f + chars * 0.065f, 0.4f, 12f);
        }
    }
}
