using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Snow.Voice
{
    /// <summary>
    /// One row of Resources/Snow_VoiceLines.csv (docs/gdd/VOICE_LIST.md §7): who says it, when the rules pick it (concept
    /// and criteria, after Valve's response rules, research §5), how it plays (priority, channel, flags, the concept that
    /// follows) and the subtitle in both languages. The voice is English for now (QUESTIONS_VOICE 4): <see cref="En"/> is
    /// what the clip says, <see cref="Ru"/> its Russian subtitle.
    /// </summary>
    public sealed class VoiceLine
    {
        public int Index;
        public string Id, Speaker, Concept, Channel, Then, En, Ru, File, Status, Note;
        public VoiceCriterion[] Criteria = Array.Empty<VoiceCriterion>();
        public int Priority;
        /// <summary>Said once per session, then it is gone (Ruskin's regret: no hand-kept "already said" flags).</summary>
        public bool Once;
        /// <summary>s before the same line may come again.</summary>
        public float Cooldown;
        public float Weight = 1f;

        /// <summary>Subtitle text in the interface language.</summary>
        public string Text => Loc.English ? En : Ru;
    }

    /// <summary>One test of a rule: <c>key=value</c>, <c>key!=value</c>, <c>key&gt;n</c>, <c>key&lt;n</c>, <c>key&gt;=n</c>, <c>key&lt;=n</c>.</summary>
    public readonly struct VoiceCriterion
    {
        public readonly string Key, Op, Value;
        readonly float _num;
        readonly bool _isNum;

        public VoiceCriterion(string key, string op, string value)
        {
            Key = key;
            Op = op;
            Value = value;
            _isNum = float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _num);
        }

        /// <summary>A missing fact fails the test, as in Valve's system: a rule that asks about something the query does
        /// not know about is not a candidate.</summary>
        public bool Match(IReadOnlyDictionary<string, string> facts)
        {
            if (facts == null || !facts.TryGetValue(Key, out string have)) return false;
            if (Op == "=") return string.Equals(have, Value, StringComparison.Ordinal);
            if (Op == "!=") return !string.Equals(have, Value, StringComparison.Ordinal);
            if (!_isNum || !float.TryParse(have, NumberStyles.Float, CultureInfo.InvariantCulture, out float n)) return false;
            switch (Op)
            {
                case ">": return n > _num;
                case "<": return n < _num;
                case ">=": return n >= _num;
                case "<=": return n <= _num;
                default: return false;
            }
        }

        static readonly string[] Ops = { "!=", ">=", "<=", "=", ">", "<" };

        /// <summary>"task=C03;stage=2" -> two criteria; bad parts are skipped with a warning.</summary>
        public static VoiceCriterion[] ParseAll(string text, string rowId)
        {
            if (string.IsNullOrWhiteSpace(text)) return Array.Empty<VoiceCriterion>();
            var list = new List<VoiceCriterion>();
            foreach (string raw in text.Split(';'))
            {
                string part = raw.Trim();
                if (part.Length == 0) continue;
                bool ok = false;
                foreach (string op in Ops)
                {
                    int i = part.IndexOf(op, StringComparison.Ordinal);
                    if (i <= 0) continue;
                    list.Add(new VoiceCriterion(part.Substring(0, i).Trim(), op, part.Substring(i + op.Length).Trim()));
                    ok = true;
                    break;
                }
                if (!ok) Debug.LogWarning($"[Snow] Voice lines: row {rowId}: criterion \"{part}\" has no operator.");
            }
            return list.ToArray();
        }
    }

    /// <summary>
    /// The voice lines, read once from Resources/Snow_VoiceLines.csv. Columns (header row, any order):
    /// id, speaker, concept, criteria, priority, channel, flags (once; cd=30; w=2), then, en, ru, file, status, note.
    /// </summary>
    public sealed class VoiceTable
    {
        public const string ResourceName = "Snow_VoiceLines";

        static VoiceTable s_main;
        readonly List<VoiceLine> _lines = new List<VoiceLine>();
        readonly Dictionary<string, VoiceLine> _byId = new Dictionary<string, VoiceLine>(StringComparer.Ordinal);
        readonly Dictionary<string, List<VoiceLine>> _byConcept = new Dictionary<string, List<VoiceLine>>(StringComparer.Ordinal);

        /// <summary>The game's table (loaded on first use).</summary>
        public static VoiceTable Main
        {
            get
            {
                if (s_main != null) return s_main;
                var asset = Resources.Load<TextAsset>(ResourceName);
                s_main = asset != null ? Parse(asset.text) : new VoiceTable();
                if (asset == null) Debug.LogWarning($"[Snow] Voice lines: Resources/{ResourceName}.csv not found.");
                return s_main;
            }
        }

        public int Count => _lines.Count;
        public VoiceLine this[int index] => index >= 0 && index < _lines.Count ? _lines[index] : null;
        public VoiceLine Get(string id) => id != null && _byId.TryGetValue(id, out VoiceLine l) ? l : null;

        /// <summary>Candidates of a speaker for a concept (empty when none).</summary>
        public IReadOnlyList<VoiceLine> Of(string speaker, string concept)
        {
            return _byConcept.TryGetValue(speaker + "|" + concept, out List<VoiceLine> list) ? list : (IReadOnlyList<VoiceLine>)Array.Empty<VoiceLine>();
        }

        public static VoiceTable Parse(string csv)
        {
            var t = new VoiceTable();
            List<string[]> rows = Rows(csv);
            if (rows.Count == 0) return t;
            var col = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < rows[0].Length; i++) col[rows[0][i].Trim()] = i;
            string F(string[] r, string name) => col.TryGetValue(name, out int i) && i < r.Length ? r[i].Trim() : "";

            for (int k = 1; k < rows.Count; k++)
            {
                string[] r = rows[k];
                string id = F(r, "id");
                if (id.Length == 0 || id.StartsWith("#", StringComparison.Ordinal)) continue;
                if (t._byId.ContainsKey(id)) { Debug.LogWarning($"[Snow] Voice lines: duplicate id {id}."); continue; }
                var line = new VoiceLine
                {
                    Index = t._lines.Count,
                    Id = id,
                    Speaker = F(r, "speaker"),
                    Concept = F(r, "concept"),
                    Channel = F(r, "channel"),
                    Then = F(r, "then"),
                    En = F(r, "en"),
                    Ru = F(r, "ru"),
                    File = F(r, "file"),
                    Status = F(r, "status"),
                    Note = F(r, "note"),
                    Criteria = VoiceCriterion.ParseAll(F(r, "criteria"), id),
                };
                int.TryParse(F(r, "priority"), NumberStyles.Integer, CultureInfo.InvariantCulture, out line.Priority);
                foreach (string raw in F(r, "flags").Split(';'))
                {
                    string flag = raw.Trim();
                    if (flag == "once") line.Once = true;
                    else if (flag.StartsWith("cd=", StringComparison.Ordinal)) float.TryParse(flag.Substring(3), NumberStyles.Float, CultureInfo.InvariantCulture, out line.Cooldown);
                    else if (flag.StartsWith("w=", StringComparison.Ordinal)) float.TryParse(flag.Substring(2), NumberStyles.Float, CultureInfo.InvariantCulture, out line.Weight);
                }
                t._lines.Add(line);
                t._byId[id] = line;
                if (line.Concept.Length == 0) continue;
                string key = line.Speaker + "|" + line.Concept;
                if (!t._byConcept.TryGetValue(key, out List<VoiceLine> list)) t._byConcept[key] = list = new List<VoiceLine>();
                list.Add(line);
            }
            return t;
        }

        /// <summary>RFC 4180 rows: quoted fields with commas, doubled quotes and line breaks.</summary>
        static List<string[]> Rows(string csv)
        {
            var rows = new List<string[]>();
            var row = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < csv.Length; i++)
            {
                char c = csv[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < csv.Length && csv[i + 1] == '"') { cell.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else cell.Append(c);
                    continue;
                }
                if (c == '"') quoted = true;
                else if (c == ',') { row.Add(cell.ToString()); cell.Clear(); }
                else if (c == '\n' || c == '\r')
                {
                    if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++;
                    row.Add(cell.ToString());
                    cell.Clear();
                    if (row.Count > 1 || row[0].Length > 0) rows.Add(row.ToArray());
                    row.Clear();
                }
                else if (c != '﻿') cell.Append(c);
            }
            if (cell.Length > 0 || row.Count > 0)
            {
                row.Add(cell.ToString());
                rows.Add(row.ToArray());
            }
            return rows;
        }
    }
}
