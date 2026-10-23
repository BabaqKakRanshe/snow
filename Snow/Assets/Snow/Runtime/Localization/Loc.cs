using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Snow
{
    /// <summary>
    /// Interface language, Russian or English (user 22.10: a switch in the F1 cheat menu). The game's text stays Russian
    /// wherever it is written (code, scene labels, data JSON, server replies); every place that draws text passes it
    /// through <see cref="T"/> right before showing it, so nothing on the network or in the scenes changes.
    /// Table: Resources/Snow_Loc_en.txt, one "russian TAB english" per line (\n for a line break, # comments).
    /// A key may hold {0}, {1}… (format specifiers like {0:0.#} are ignored): it matches any text where the holes are,
    /// and each hole's text is translated in turn. A key ending with ':' also matches "key anything".
    /// Lookup: exact → pattern → split on separators (line breaks, "   ", " — ", " · ", "; ", ": ", ", ") and translate
    /// the parts. What is still Russian stays Russian (editor and development builds list it in Logs/loc_missing.txt).
    /// The choice is per player, kept in PlayerPrefs.
    /// </summary>
    public static class Loc
    {
        const string PrefKey = "snow.language";
        const int MaxCache = 4096;
        static readonly string[] Separators = { "\n", "   ", " — ", " · ", "; ", ": ", ", " };
        static readonly Regex Hole = new Regex(@"\{(\d+)(?:[:,][^}]*)?\}", RegexOptions.Compiled);

        sealed class Pattern
        {
            public string Prefix, Suffix;
            public Regex Match;
            public int[] Holes;        // capture group i → hole number
            public string English;
        }

        static bool s_loaded, s_english;
        static readonly Dictionary<string, string> s_exact = new Dictionary<string, string>(System.StringComparer.Ordinal);
        static readonly List<Pattern> s_patterns = new List<Pattern>();
        static readonly Dictionary<string, string> s_cache = new Dictionary<string, string>(System.StringComparer.Ordinal);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        static readonly HashSet<string> s_missing = new HashSet<string>(System.StringComparer.Ordinal);
#endif

        /// <summary>English interface (false: Russian, the default). Setting it is remembered for the next start.</summary>
        public static bool English
        {
            get { Load(); return s_english; }
            set
            {
                Load();
                if (s_english == value) return;
                s_english = value;
                s_cache.Clear();
                PlayerPrefs.SetString(PrefKey, value ? "en" : "ru");
                PlayerPrefs.Save();
            }
        }

        /// <summary>The text in the current language: unchanged in Russian, translated where the table knows it in English.</summary>
        public static string T(string text)
        {
            if (string.IsNullOrEmpty(text) || !English || !Cyrillic(text)) return text;
            if (s_cache.TryGetValue(text, out string done)) return done;
            if (s_cache.Count >= MaxCache) s_cache.Clear();          // telemetry and timers make new strings every frame
            return s_cache[text] = Translate(text, 0);
        }

        static bool Cyrillic(string s)
        {
            foreach (char c in s) if (c >= 'Ѐ' && c <= 'ӿ') return true;
            return false;
        }

        static string Translate(string text, int depth)
        {
            if (!Cyrillic(text) || depth > 8) return text;
            string core = text.Trim();
            if (core.Length != text.Length)
            {
                int lead = text.IndexOf(core, System.StringComparison.Ordinal);
                return text.Substring(0, lead) + Translate(core, depth + 1) + text.Substring(lead + core.Length);
            }
            if (s_exact.TryGetValue(text, out string en)) return en;
            foreach (Pattern p in s_patterns)
            {
                if (!text.StartsWith(p.Prefix, System.StringComparison.Ordinal) || !text.EndsWith(p.Suffix, System.StringComparison.Ordinal)) continue;
                Match m = p.Match.Match(text);
                if (!m.Success) continue;
                string result = p.English;
                for (int g = 1; g < m.Groups.Count; g++)
                    result = result.Replace("{" + p.Holes[g - 1] + "}", Translate(m.Groups[g].Value, depth + 1));
                return result;
            }
            foreach (string sep in Separators)
            {
                if (text.IndexOf(sep, System.StringComparison.Ordinal) < 0) continue;
                string[] parts = text.Split(new[] { sep }, System.StringSplitOptions.None);
                for (int i = 0; i < parts.Length; i++) parts[i] = Translate(parts[i], depth + 1);
                return string.Join(sep, parts);
            }
            Missing(text);
            return text;
        }

        static void Load()
        {
            if (s_loaded) return;
            s_loaded = true;
            s_english = PlayerPrefs.GetString(PrefKey, "ru") == "en";
            var table = Resources.Load<TextAsset>("Snow_Loc_en");
            if (table == null) { Debug.LogWarning("Loc: Resources/Snow_Loc_en.txt not found, English shows Russian"); return; }
            foreach (string raw in table.text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                int tab = line.IndexOf('\t');
                if (line.Length == 0 || line[0] == '#' || tab <= 0) continue;
                string ru = Unescape(line.Substring(0, tab)).Trim(), en = Unescape(line.Substring(tab + 1)).Trim();
                if (ru.Length == 0 || en.Length == 0) continue;
                ru = Hole.Replace(ru, "{$1}");
                if (ru.EndsWith(":") && !ru.Contains("{")) AddPattern(ru + " {99}", en + " {99}");
                if (ru.Contains("{")) AddPattern(ru, en);
                else s_exact[ru] = en;
            }
            // the longest fixed text first: "Карманы полны ({0}): …" before "Карманы полны ({0})"
            s_patterns.Sort((a, b) => (b.Prefix.Length + b.Suffix.Length).CompareTo(a.Prefix.Length + a.Suffix.Length));
        }

        static void AddPattern(string ru, string en)
        {
            if (!Cyrillic(Hole.Replace(ru, ""))) return;               // "{0} ({1})" would swallow everything
            var holes = new List<int>();
            var rx = new StringBuilder("^");
            int at = 0;
            foreach (Match m in Hole.Matches(ru))
            {
                rx.Append(Regex.Escape(ru.Substring(at, m.Index - at))).Append("(.+?)");
                holes.Add(int.Parse(m.Groups[1].Value));
                at = m.Index + m.Length;
            }
            rx.Append(Regex.Escape(ru.Substring(at))).Append('$');
            int first = ru.IndexOf('{'), last = ru.LastIndexOf('}');
            s_patterns.Add(new Pattern
            {
                Prefix = ru.Substring(0, first),
                Suffix = ru.Substring(last + 1),
                Match = new Regex(rx.ToString(), RegexOptions.Singleline),
                Holes = holes.ToArray(),
                English = Hole.Replace(en, "{$1}"),
            });
        }

        static string Unescape(string s) => s.Replace("\\n", "\n").Replace("\\t", "\t");

        static void Missing(string text)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!s_missing.Add(text)) return;
            try
            {
                string path = System.IO.Path.Combine(Application.isEditor ? System.IO.Path.GetDirectoryName(Application.dataPath) : Application.persistentDataPath, "Logs", "loc_missing.txt");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.AppendAllText(path, text.Replace("\n", "\\n") + "\n");
            }
            catch (System.Exception) { }
#endif
        }
    }
}
