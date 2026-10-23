using System;
using System.Collections.Generic;
using Snow.Net;
using UnityEngine;

namespace Snow.Voice
{
    /// <summary>
    /// The server's conductor of speech (research §5, §9): a script per loudspeaker — say a concept (the rules pick the
    /// line, its <c>then</c> follows), say fixed lines (a splice), wait, ask (a prompt; the answer branches the script),
    /// call back. Each line is picked at the moment it is due, so a change of the world in between changes what comes
    /// (Valve's re-query instead of an interruption system). Budget: one script per loudspeaker, 3 s of quiet after a
    /// script, a short breath between lines. A script of higher priority cuts the running one with a click; the cut one
    /// waits and goes on afterwards with its "resume" line (Firewatch: interrupted talks resume). Everything said reaches
    /// the creatures' hearing as radio noise (QUESTIONS_VOICE 14). Numbers TEMPORARY.
    /// </summary>
    public sealed class VoiceDirector
    {
        public const float Gap = 3f, Breath = 0.4f, RadioNoise = NoiseEvents.Medium;

        internal enum Kind { Concept, Lines, Wait, Prompt, Call }

        internal sealed class Step
        {
            public Kind Kind;
            public string Speaker, Concept;
            public Dictionary<string, string> Facts;
            public List<VoiceLine> Lines;
            public float Seconds;
            public string[] Options;
            public Action<int> OnAnswer;
            public Action Call;
        }

        /// <summary>A queue of speech steps for one loudspeaker; built with the fluent methods.</summary>
        public sealed class Script
        {
            public readonly string Name;
            public readonly int Priority;
            /// <summary>Concept said first when the script goes on after being cut (null: just go on).</summary>
            public string ResumeSpeaker, ResumeConcept;
            public Action Done;
            internal readonly List<Step> Steps = new List<Step>();
            internal int Next;

            public Script(string name, int priority) { Name = name; Priority = priority; }

            public Script Say(string speaker, string concept, Dictionary<string, string> facts = null)
            {
                Steps.Add(new Step { Kind = Kind.Concept, Speaker = speaker, Concept = concept, Facts = facts });
                return this;
            }

            public Script SayLines(List<VoiceLine> lines)
            {
                if (lines != null && lines.Count > 0) Steps.Add(new Step { Kind = Kind.Lines, Lines = lines });
                return this;
            }

            public Script Wait(float seconds)
            {
                Steps.Add(new Step { Kind = Kind.Wait, Seconds = seconds });
                return this;
            }

            /// <summary>Open a question; <paramref name="onAnswer"/> gets the option or -1 after the timeout.</summary>
            public Script Ask(string[] options, float timeout, Action<int> onAnswer)
            {
                Steps.Add(new Step { Kind = Kind.Prompt, Options = options, Seconds = timeout, OnAnswer = onAnswer });
                return this;
            }

            public Script Then(Action call)
            {
                Steps.Add(new Step { Kind = Kind.Call, Call = call });
                return this;
            }

            internal void InsertNext(Step s) => Steps.Insert(Next, s);

            /// <summary>Steps added from an answer go right after the current one.</summary>
            public Script Insert(Script more)
            {
                Steps.InsertRange(Next, more.Steps);
                return this;
            }
        }

        sealed class Speaker
        {
            public string Key;
            public ulong Anchor;
            public Vector3 Point;
            public float PromptRadius;
            public Script Running;
            public readonly Stack<Script> Suspended = new Stack<Script>();
            public double BusyUntil, FreeAt;
            public uint Prompt;
            public double PromptUntil;
            public int Answer = int.MinValue;
            public Action<int> OnAnswer;
        }

        public readonly VoiceTable Table;
        public readonly VoiceMemory Memory = new VoiceMemory();
        readonly System.Random _rng;
        readonly Dictionary<string, Speaker> _speakers = new Dictionary<string, Speaker>(StringComparer.Ordinal);
        readonly Dictionary<string, string> _empty = new Dictionary<string, string>();

        /// <summary>The last rule decision, for the cheat menu.</summary>
        public string LastWhy { get; private set; } = "";
        /// <summary>Called before every pick: add the world's facts to the query (day, phase, …).</summary>
        public Action<Dictionary<string, string>> WorldFacts;

        public VoiceDirector(VoiceTable table, int seed)
        {
            Table = table;
            _rng = new System.Random(seed);
            VoiceNet.Chosen += OnChosen;
        }

        public void Dispose() => VoiceNet.Chosen -= OnChosen;

        /// <summary>A loudspeaker: a spawned object (anchor) or a fixed point; who stands within the radius may answer.</summary>
        public void Place(string key, ulong anchor, Vector3 point, float promptRadius = 3f)
        {
            if (!_speakers.TryGetValue(key, out Speaker s)) _speakers[key] = s = new Speaker { Key = key };
            s.Anchor = anchor;
            s.Point = point;
            s.PromptRadius = promptRadius;
        }

        public bool Busy(string key) => _speakers.TryGetValue(key, out Speaker s) && (s.Running != null || s.Suspended.Count > 0);
        public string RunningName(string key) => _speakers.TryGetValue(key, out Speaker s) && s.Running != null ? s.Running.Name : null;

        /// <summary>Start a script; false when a script of the same or higher priority is running or the quiet gap is not over.</summary>
        public bool Start(string key, Script script, double now)
        {
            if (!_speakers.TryGetValue(key, out Speaker s) || script == null) return false;
            if (s.Running != null)
            {
                if (script.Priority <= s.Running.Priority) return false;
                Cut(s);
                s.Suspended.Push(s.Running);
                BenchLog.Event("voice_cut", "speaker", key, "script", s.Running.Name, "by", script.Name);
            }
            else if (now < s.FreeAt && script.Priority < 4) return false;
            s.Running = script;
            s.BusyUntil = now;
            BenchLog.Event("voice_script", "speaker", key, "script", script.Name);
            return true;
        }

        /// <summary>Stop everything of a loudspeaker (the radio was carried away, the power died).</summary>
        public void Silence(string key, double now)
        {
            if (!_speakers.TryGetValue(key, out Speaker s)) return;
            if (s.Running != null) Cut(s);
            s.Running = null;
            s.Suspended.Clear();
            s.FreeAt = now + Gap;
        }

        void Cut(Speaker s)
        {
            VoiceNet.Stop(s.Anchor, s.Point);
            // an open question is asked again when the script goes on; a cut line is not repeated (its "then" is
            // already queued), the "resume" line bridges the gap
            Script r = s.Running;
            if (s.Prompt != 0)
            {
                VoiceNet.ClosePrompt(s.Prompt);
                if (r != null && r.Next > 0 && r.Steps[r.Next - 1].Kind == Kind.Prompt) r.Next--;
            }
            s.Prompt = 0;
            s.OnAnswer = null;
            s.Answer = int.MinValue;
        }

        void OnChosen(ulong client, uint prompt, int option)
        {
            foreach (Speaker s in _speakers.Values)
                if (s.Prompt == prompt) s.Answer = option;
        }

        public void Tick(double now)
        {
            foreach (Speaker s in _speakers.Values) Tick(s, now);
        }

        void Tick(Speaker s, double now)
        {
            if (s.Running == null)
            {
                if (s.Suspended.Count == 0 || now < s.FreeAt) return;
                s.Running = s.Suspended.Pop();
                if (s.Running.ResumeConcept != null)
                    s.Running.InsertNext(new Step { Kind = Kind.Concept, Speaker = s.Running.ResumeSpeaker, Concept = s.Running.ResumeConcept });
                s.BusyUntil = now;
            }
            if (now < s.BusyUntil) return;

            if (s.Prompt != 0)
            {
                bool answered = s.Answer != int.MinValue, timedOut = now >= s.PromptUntil;
                if (!answered && !timedOut) return;
                if (!answered) VoiceNet.ClosePrompt(s.Prompt);
                int a = answered ? s.Answer : -1;
                Action<int> cb = s.OnAnswer;
                s.Prompt = 0;
                s.OnAnswer = null;
                s.Answer = int.MinValue;
                cb?.Invoke(a);
            }

            Script r = s.Running;
            if (r.Next >= r.Steps.Count)
            {
                s.Running = null;
                s.FreeAt = now + Gap;
                BenchLog.Event("voice_script_done", "speaker", s.Key, "script", r.Name);
                r.Done?.Invoke();
                return;
            }
            Step step = r.Steps[r.Next++];
            switch (step.Kind)
            {
                case Kind.Concept:
                {
                    var facts = new Dictionary<string, string>(step.Facts ?? _empty, StringComparer.Ordinal);
                    WorldFacts?.Invoke(facts);
                    VoiceLine line = VoiceRules.Pick(Table, step.Speaker, step.Concept, facts, Memory, now, _rng, out string why);
                    LastWhy = why;
                    BenchLog.Event("voice_pick", "speaker", s.Key, "why", why);
                    if (line == null) break;
                    Say(s, new List<VoiceLine> { line }, r.Priority, now);
                    if (!string.IsNullOrEmpty(line.Then))
                        r.InsertNext(new Step { Kind = Kind.Concept, Speaker = line.Speaker, Concept = line.Then, Facts = step.Facts });
                    break;
                }
                case Kind.Lines:
                    Say(s, step.Lines, r.Priority, now);
                    break;
                case Kind.Wait:
                    s.BusyUntil = now + step.Seconds;
                    break;
                case Kind.Prompt:
                    s.Prompt = VoiceNet.OpenPrompt(s.Anchor, s.Point, s.PromptRadius, step.Options);
                    s.PromptUntil = now + step.Seconds;
                    s.OnAnswer = step.OnAnswer;
                    s.Answer = int.MinValue;
                    if (s.Prompt == 0) { s.OnAnswer = null; step.OnAnswer?.Invoke(-1); }
                    break;
                case Kind.Call:
                    step.Call?.Invoke();
                    break;
            }
        }

        void Say(Speaker s, List<VoiceLine> lines, int priority, double now)
        {
            float seconds = 0.12f;
            foreach (VoiceLine l in lines)
            {
                seconds += VoiceClips.Duration(l);
                Memory.Played(l, now);
            }
            VoiceNet.Say(s.Anchor, s.Point, VoiceNet.Channel.Radio, priority, lines);
            s.BusyUntil = now + seconds + Breath;
            NoiseEvents.Emit(VoiceNet.AnchorPoint(s.Anchor, s.Point), RadioNoise, "radio_" + lines[0].Speaker);
            BenchLog.Event("voice_say", "speaker", s.Key, "lines", string.Join(",", lines.ConvertAll(l => l.Id)), "s", seconds);
        }
    }
}
