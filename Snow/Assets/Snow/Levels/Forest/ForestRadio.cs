using System.Collections.Generic;
using Snow.Net;
using Snow.Voice;
using Unity.Netcode;
using UnityEngine;

namespace Snow.Levels.Forest
{
    /// <summary>
    /// The control-desk radio of the Forest base (docs/gdd/VOICE_LIST.md, QUESTIONS_VOICE answers 23.10): Kessler's
    /// sessions and the voice in the radio, on the server, through <see cref="VoiceDirector"/>. Added by
    /// <see cref="ForestOpening"/> to its own object; reads the opening's steps and phase.
    /// - Day 1, power on (S7): call sign, the player's personnel number spliced digit by digit, the greeting, the code
    ///   words and one check: Kessler says the first half, the player picks the second from three (answer by choice).
    /// - Hints when there is no progress: the boiler (S8) and closing the base (S9), two stages each - OFF since 27.10
    ///   (user, QUESTIONS_6 §7а 12: on day 1 only K.'s pager hints; the same lines are the tasks' pager hints).
    /// - X08 at 23:40 with power: the German voice; the player may answer by choice. An answer comes back 1–3 min later
    ///   in that voice (E12) and something moves at the gate (E13: loud noise there for the creatures).
    /// - The morning (phase 2): a short session. Rare jokes while someone is near the radio.
    /// The radio is the spawned prop Leitstand_RadioSet_1 (it can be carried; it talks wherever it is). Without power it
    /// is silent. Personnel numbers are 4 random digits per player (QUESTIONS_VOICE 11), kept here on the server; the
    /// number on the chest is DEFERRED. Timings TEMPORARY.
    /// </summary>
    public class ForestRadio : MonoBehaviour
    {
        const string RadioProp = "Leitstand_RadioSet_1", Key = "leitstand", Kessler = "kessler", RadioVoice = "radio_voice";
        /// <summary>The boiler / closing hints by voice (off: the pager gives them, user 27.10).</summary>
        static readonly bool RadioHints = false;
        const float X08Hour = 23f + 40f / 60f, StartDelay = 4f, HintFirst = 240f, HintNext = 180f, ListenRange = 12f;
        const float CodeTimeout = 40f, NightTimeout = 45f, PromptRadius = 3f;
        const int SessionPriority = 3, AnomalyPriority = 4, IdlePriority = 1;

        /// <summary>Code words (docs/gdd/VOICE_LINES: kes_code_w01…w10): Kessler says the first half, the answer is the second.</summary>
        static readonly string[,] CodeWords =
        {
            { "Eis", "berg" }, { "Schnee", "mann" }, { "Nord", "licht" }, { "Wind", "mühle" }, { "Fern", "rohr" },
            { "Hoch", "moos" }, { "Kalt", "front" }, { "Stern", "warte" }, { "Funk", "turm" }, { "Holz", "fäller" },
        };
        /// <summary>Night replies to the voice (game text) and the e12 answer each one is repeated as.</summary>
        static readonly string[] NightReplies = { "Кто это?", "Не та частота.", "Отзыв?" };
        static readonly int[] NightAnswer = { 1, 2, 4 };

        public static ForestRadio Instance { get; private set; }

        ForestOpening _opening;
        VoiceDirector _director;
        NetworkObject _radio;
        readonly Dictionary<ulong, int> _numbers = new Dictionary<ulong, int>();
        readonly System.Random _rng = new System.Random();
        bool _day1, _day1Done, _x08, _e12, _morning;
        double _s7At = -1d, _s8At = -1d, _e12At = -1d, _nextIdle = -1d;
        int _e12Answer = -1, _hintC03, _hintC04;
        float _lastHour = -1f;
        string _lastDebug = "";

        public string LastWhy => _director != null ? _director.LastWhy : "";
        public string LastDebug => _lastDebug;

        void Awake()
        {
            _opening = GetComponent<ForestOpening>();
            Instance = this;
        }

        void OnDestroy()
        {
            _director?.Dispose();
            if (Instance == this) Instance = null;
        }

        /// <summary>Server: the personnel number of a player (4 digits, unique, made on first use).</summary>
        public int NumberOf(ulong client)
        {
            if (_numbers.TryGetValue(client, out int n)) return n;
            do n = _rng.Next(1000, 10000); while (_numbers.ContainsValue(n));
            _numbers[client] = n;
            BenchLog.Event("voice_number", "client", client, "number", n);
            return n;
        }

        // ---------------------------------------------------------------- server tick
        void Update()
        {
            if (_opening == null || !_opening.IsSpawned || !_opening.IsServer) return;
            if (_director == null)
            {
                _director = new VoiceDirector(VoiceTable.Main, System.Environment.TickCount);
                _director.WorldFacts = f =>
                {
                    f["phase"] = _opening.Phase.ToString();
                    f["day"] = _opening.Phase == 2 ? "2" : "1";
                };
            }
            if (_radio == null && _opening.items != null) _radio = _opening.items.Find(RadioProp);
            if (_radio == null) return;
            _director.Place(Key, _radio.NetworkObjectId, _radio.transform.position, PromptRadius);
            double now = Time.timeAsDouble;

            bool powered = _opening.host != null && _opening.host.Powered;
            if (!powered)
            {
                if (_director.Busy(Key)) _director.Silence(Key, now);
                _lastHour = Hour();
                return;
            }

            Day1(now);
            Hints(now);
            X08(now);
            E12(now);
            Morning(now);
            Idle(now);
            _director.Tick(now);
        }

        float Hour() => ForestDayNight.Instance != null ? ForestDayNight.Instance.Hour : -1f;

        void Day1(double now)
        {
            if (_day1 || !_opening.StepDone(7)) return;
            if (_s7At < 0d) _s7At = now;
            if (now - _s7At < StartDelay) return;
            VoiceDirector.Script s = Session("day1");
            s.Say(Kessler, "session.start").SayLines(NumberLines(Nearest())).Say(Kessler, "hello");
            CodeCheck(s, s, 1);
            s.Say(Kessler, "session.end").Then(() => { _day1Done = true; _nextIdle = Time.timeAsDouble + 360d; });
            _day1 = _director.Start(Key, s, now);
        }

        /// <summary>One check of a code word: the first half, "Countersign?", three second halves to choose from. Steps go
        /// into <paramref name="build"/>; what the answer brings goes into the running <paramref name="root"/>.</summary>
        void CodeCheck(VoiceDirector.Script build, VoiceDirector.Script root, int attempt)
        {
            int word = _rng.Next(CodeWords.GetLength(0));
            string[] options = CodeOptions(word, out int right);
            build.Say(Kessler, "code.word", Facts("word", (word + 1).ToString())).Say(Kessler, "code.ask");
            build.Ask(options, CodeTimeout, a =>
            {
                var more = new VoiceDirector.Script("code", SessionPriority);
                if (a == right) more.Say(Kessler, "code.ok");
                else if (a >= 0)
                {
                    more.Say(Kessler, "code.bad");
                    if (attempt < 2) CodeCheck(more, root, attempt + 1);
                }
                else
                {
                    more.Say(Kessler, "reply.none");
                    if (attempt < 2) CodeCheck(more, root, attempt + 1);
                    else more.Say(Kessler, "reply.giveup");
                }
                BenchLog.Event("voice_code", "word", CodeWords[word, 0] + CodeWords[word, 1], "answer", a, "right", right, "attempt", attempt);
                root.Insert(more);
            });
        }

        string[] CodeOptions(int word, out int right)
        {
            var picks = new List<int> { word };
            while (picks.Count < 3)
            {
                int o = _rng.Next(CodeWords.GetLength(0));
                if (!picks.Contains(o)) picks.Add(o);
            }
            for (int i = picks.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (picks[i], picks[j]) = (picks[j], picks[i]);
            }
            right = picks.IndexOf(word);
            return picks.ConvertAll(i => CodeWords[i, 1]).ToArray();
        }

        void Hints(double now)
        {
            if (!RadioHints || !_day1Done || _opening.Phase != 0) return;
            if (_opening.StepDone(8) && _s8At < 0d) _s8At = now;
            if (!_opening.StepDone(8)) Hint(ref _hintC03, "C03", now - _s7At, now);
            else if (!_opening.StepDone(9)) Hint(ref _hintC04, "C04", now - _s8At, now);
        }

        void Hint(ref int stage, string task, double since, double now)
        {
            if (stage >= 2 || since < HintFirst + stage * HintNext) return;
            VoiceDirector.Script s = Session("hint " + task).Say(Kessler, "session.start")
                .Say(Kessler, "hint", Facts("task", task, "stage", (stage + 1).ToString())).Say(Kessler, "session.end");
            if (_director.Start(Key, s, now)) stage++;
        }

        void X08(double now)
        {
            float hour = Hour();
            bool crossed = _lastHour >= 0f && hour >= 0f && _lastHour < X08Hour && hour >= X08Hour;
            _lastHour = hour;
            if (_x08 || !crossed || _opening.Phase == 2) return;
            RunX08(now);
        }

        bool RunX08(double now)
        {
            var s = new VoiceDirector.Script("x08", AnomalyPriority);
            s.Say(RadioVoice, "x08").Ask(NightReplies, NightTimeout, a =>
            {
                if (a < 0) return;                                     // silence is the safe answer (ENTITIES #6)
                _e12Answer = NightAnswer[a];
                _e12At = Time.timeAsDouble + 60d + _rng.NextDouble() * 120d;
                BenchLog.Event("voice_x08_answer", "answer", a, "e12_in", _e12At - Time.timeAsDouble);
            });
            _x08 = _director.Start(Key, s, now);
            return _x08;
        }

        void E12(double now)
        {
            if (_e12 || _e12At < 0d || now < _e12At) return;
            RunE12(now);
        }

        bool RunE12(double now)
        {
            var s = new VoiceDirector.Script("e12", AnomalyPriority);
            s.Say(RadioVoice, "e12", Facts("answer", Mathf.Max(0, _e12Answer).ToString())).Then(E13);
            _e12 = _director.Start(Key, s, now);
            return _e12;
        }

        /// <summary>E13: something moves at the fence (the creature goes to the voice, R2) — for now a loud noise at the gate.</summary>
        void E13()
        {
            Vector3 at = _radio != null ? _radio.transform.position : Vector3.zero;
            if (_opening.host != null && _opening.host.TryGetPart("IX_Gate_81", out SkyIslandLocationHost.Part gate) && gate?.part != null)
                at = gate.part.transform.position;
            NoiseEvents.Emit(at, NoiseEvents.Loud, "e13_fence");
            Snow.Audio.SnowAudio.ServerPlay("fence.e13.rattle", at);           // players at the gate hear it too
            BenchLog.Event("forest_e13", "at", at);
        }

        void Morning(double now)
        {
            if (_morning || _opening.Phase != 2) return;
            VoiceDirector.Script s = Session("morning");
            s.Say(Kessler, "session.start").SayLines(NumberLines(Nearest())).Say(Kessler, "morning").Say(Kessler, "session.end");
            _morning = _director.Start(Key, s, now);
        }

        void Idle(double now)
        {
            if (!_day1Done || _opening.Phase == 1 || _nextIdle < 0d || now < _nextIdle || _director.Busy(Key)) return;
            if (!AnyoneNear(ListenRange)) return;
            var s = new VoiceDirector.Script("idle", IdlePriority).Say(Kessler, "idle", Facts("calm", "1"));
            if (_director.Start(Key, s, now)) _nextIdle = now + 360d + _rng.NextDouble() * 180d;
        }

        // ---------------------------------------------------------------- helpers
        VoiceDirector.Script Session(string name) =>
            new VoiceDirector.Script(name, SessionPriority) { ResumeSpeaker = Kessler, ResumeConcept = "resume" };

        static Dictionary<string, string> Facts(params string[] kv)
        {
            var d = new Dictionary<string, string>(System.StringComparer.Ordinal);
            for (int i = 0; i + 1 < kv.Length; i += 2) d[kv[i]] = kv[i + 1];
            return d;
        }

        /// <summary>"Number 4-0-7-2. Do you copy?" spliced from single digits (VOICE_LIST.md §2).</summary>
        List<VoiceLine> NumberLines(ulong client)
        {
            VoiceTable t = VoiceTable.Main;
            var lines = new List<VoiceLine>();
            string digits = NumberOf(client).ToString("0000");
            void Add(string id) { VoiceLine l = t.Get(id); if (l != null) lines.Add(l); }
            Add("kes_num_word");
            for (int i = 0; i < digits.Length; i++) Add($"kes_num_{digits[i]}_{(i == digits.Length - 1 ? "end" : "mid")}");
            Add("kes_num_q");
            return lines;
        }

        /// <summary>The player nearest to the radio (the host when nobody is there).</summary>
        ulong Nearest()
        {
            NetworkManager nm = NetworkManager.Singleton;
            ulong best = NetworkManager.ServerClientId;
            float bestD = float.MaxValue;
            if (nm == null || _radio == null) return best;
            foreach (NetworkClient c in nm.ConnectedClientsList)
            {
                if (c.PlayerObject == null) continue;
                float d = (c.PlayerObject.transform.position - _radio.transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = c.ClientId; }
            }
            return best;
        }

        bool AnyoneNear(float radius)
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || _radio == null) return false;
            foreach (NetworkClient c in nm.ConnectedClientsList)
                if (c.PlayerObject != null && (c.PlayerObject.transform.position - _radio.transform.position).sqrMagnitude <= radius * radius) return true;
            return false;
        }

        // ---------------------------------------------------------------- cheat menu (server)
        /// <summary>Server: run a part now (F1). Returns what happened.</summary>
        public string DebugRun(string what)
        {
            if (_director == null || _radio == null) return _lastDebug = "рация не найдена или нет сервера";
            double now = Time.timeAsDouble;
            bool ok;
            switch (what)
            {
                case "day1":
                    _day1 = false;
                    _s7At = now - StartDelay;
                    if (!_opening.StepDone(7)) return _lastDebug = "нет питания (S7)";
                    Day1(now);
                    ok = _day1;
                    break;
                case "hint":
                    VoiceDirector.Script h = Session("hint debug").Say(Kessler, "hint", Facts("task", "C03", "stage", (_rng.Next(2) + 1).ToString()));
                    ok = _director.Start(Key, h, now);
                    break;
                case "x08": ok = RunX08(now); break;
                case "e12":
                    if (_e12Answer < 0) _e12Answer = NightAnswer[_rng.Next(NightAnswer.Length)];
                    _e12 = false;
                    ok = RunE12(now);
                    break;
                case "morning":
                    _morning = false;
                    VoiceDirector.Script m = Session("morning debug");
                    m.Say(Kessler, "session.start").SayLines(NumberLines(Nearest())).Say(Kessler, "morning").Say(Kessler, "session.end");
                    ok = _morning = _director.Start(Key, m, now);
                    break;
                case "idle":
                    ok = _director.Start(Key, new VoiceDirector.Script("idle debug", IdlePriority).Say(Kessler, "idle", Facts("calm", "1")), now);
                    break;
                case "stop":
                    _director.Silence(Key, now);
                    ok = true;
                    break;
                default: return _lastDebug = "?";
            }
            return _lastDebug = $"{what}: {(ok ? "запущено" : "занято")}";
        }
    }
}
