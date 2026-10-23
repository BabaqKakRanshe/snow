using System.Collections.Generic;

namespace Snow.Voice
{
    /// <summary>What was said this session: spent "once" lines, when each line last played, the last pick of a concept.</summary>
    public sealed class VoiceMemory
    {
        readonly HashSet<int> _spent = new HashSet<int>();
        readonly Dictionary<int, double> _playedAt = new Dictionary<int, double>();
        readonly Dictionary<string, int> _lastOf = new Dictionary<string, int>();

        public bool Spent(VoiceLine l) => l.Once && _spent.Contains(l.Index);
        public bool Cooling(VoiceLine l, double now) => l.Cooldown > 0f && _playedAt.TryGetValue(l.Index, out double at) && now - at < l.Cooldown;
        public bool WasLast(VoiceLine l) => _lastOf.TryGetValue(l.Speaker + "|" + l.Concept, out int i) && i == l.Index;

        public void Played(VoiceLine l, double now)
        {
            if (l.Once) _spent.Add(l.Index);
            _playedAt[l.Index] = now;
            _lastOf[l.Speaker + "|" + l.Concept] = l.Index;
        }

        public void Clear()
        {
            _spent.Clear();
            _playedAt.Clear();
            _lastOf.Clear();
        }
    }

    /// <summary>
    /// The rule match (Ruskin, GDC 2012; research §5): of the lines of a speaker for a concept, those whose every criterion
    /// holds are candidates, the one with the most criteria wins (the most specific), ties are random by weight. A spent
    /// "once" line or one on cooldown is no candidate; the line said last for the concept is skipped while others remain.
    /// </summary>
    public static class VoiceRules
    {
        static readonly List<VoiceLine> s_best = new List<VoiceLine>();

        /// <summary>The line to say, or null when nothing fits. Why it won (or why not) is in <paramref name="why"/>.</summary>
        public static VoiceLine Pick(VoiceTable table, string speaker, string concept, IReadOnlyDictionary<string, string> facts,
                                     VoiceMemory memory, double now, System.Random rng, out string why)
        {
            IReadOnlyList<VoiceLine> all = table.Of(speaker, concept);
            if (all.Count == 0) { why = $"{speaker}/{concept}: no lines"; return null; }
            s_best.Clear();
            int best = -1, spent = 0, cooling = 0, failed = 0;
            for (int i = 0; i < all.Count; i++)
            {
                VoiceLine l = all[i];
                if (memory != null && memory.Spent(l)) { spent++; continue; }
                if (memory != null && memory.Cooling(l, now)) { cooling++; continue; }
                bool ok = true;
                for (int c = 0; c < l.Criteria.Length && ok; c++) ok = l.Criteria[c].Match(facts);
                if (!ok) { failed++; continue; }
                int score = l.Criteria.Length;
                if (score > best) { best = score; s_best.Clear(); }
                if (score == best) s_best.Add(l);
            }
            if (s_best.Count == 0)
            {
                why = $"{speaker}/{concept}: none of {all.Count} (spent {spent}, cooling {cooling}, criteria {failed})";
                return null;
            }
            if (s_best.Count > 1 && memory != null)
                for (int i = s_best.Count - 1; i >= 0 && s_best.Count > 1; i--)
                    if (memory.WasLast(s_best[i])) s_best.RemoveAt(i);

            float total = 0f;
            foreach (VoiceLine l in s_best) total += l.Weight > 0f ? l.Weight : 0f;
            VoiceLine pick = s_best[0];
            if (total > 0f)
            {
                double roll = (rng ?? new System.Random()).NextDouble() * total;
                foreach (VoiceLine l in s_best)
                {
                    roll -= l.Weight > 0f ? l.Weight : 0f;
                    if (roll <= 0d) { pick = l; break; }
                }
            }
            why = $"{speaker}/{concept}: {pick.Id} (score {best}, {s_best.Count} tied, spent {spent}, cooling {cooling}, criteria {failed})";
            return pick;
        }
    }
}
