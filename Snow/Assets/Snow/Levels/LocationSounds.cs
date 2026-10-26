using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using SkyIsland;
using Snow.Audio;
using Snow.Doors;
using Snow.Voice;
using Unity.Netcode;
using UnityEngine;

namespace Snow.Levels
{
    /// <summary>
    /// Sounds of a location's parts, lights, grid and machines from one rule table (Resources/Audio/Snow_LocationSounds,
    /// user 26.10.2026: cover the whole game with sounds). Every peer watches the state it already has — a part's target
    /// and travel, a NetworkDoor's state, a fixture's events, the grid — and plays the cue locally, so nothing new goes
    /// over the network; the server also tells the creatures (NoiseEvents) when the cue has a noise radius. A change that
    /// arrives without travel (late join, a snap) makes no sound.
    /// </summary>
    [DisallowMultipleComponent]
    public class LocationSounds : MonoBehaviour
    {
        [Serializable] class PartRule
        {
            public string match = "", open = "", close = "", loop = "", stop = "", blocked = "", locked = "";
            [Tooltip("While travelling towards open / closed (else loop); a second loop under it (a gate's motor and rollers)")]
            public string loopOpen = "", loopClose = "", loop2 = "";
            [Tooltip("At the end of the travel, fully open / closed (on top of stop)")] public string stopOpen = "", stopClosed = "";
            [Tooltip("Repeated every min..max s while the part stays open (a door banging in the wind)")] public string whileOpen = "";
            public Vector2 every = new Vector2(10f, 30f);
        }
        [Serializable] class FixtureRule { public string kind = "", starting = "", on = "", off = "", blink = "", fault = "", broken = "", loop = ""; }
        [Serializable] class EmitterRule
        {
            public string match = "", loop = "", when = "always";
            public int max = 8;
            [Tooltip("A one-shot repeated every min..max s (a bell, a creak, a drip) instead of / beside the loop")] public string oneshot = "";
            public Vector2 every = new Vector2(20f, 60f);
        }
        [Serializable] class Rules
        {
            public PartRule[] parts = new PartRule[0];
            public FixtureRule[] fixtures = new FixtureRule[0];
            public EmitterRule[] emitters = new EmitterRule[0];
            public string gridOn = "", gridOff = "";
        }

        sealed class Watched
        {
            public SkyIslandLocationHost.Part part;
            public PartRule rule;
            public float target, amount;
            public DoorState door;
            public SoundLoop loop, loopOpen, loopClose, loop2;
            public bool moving;
            public float nextOpen;
        }

        sealed class Emitter { public SoundLoop loop; public string when, oneshot; public Transform at; public Vector3 offset; public Vector2 every; public float next; }

        /// <summary>The local point in the middle of an object's renderers (zero when it has none).</summary>
        static Vector3 Center(Transform t)
        {
            Renderer[] rs = t.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return Vector3.zero;
            Bounds b = rs[0].bounds;
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            return t.InverseTransformPoint(b.center);
        }

        /// <summary>An emitter's condition: always, powered / unpowered (the grid), day, night, windy, storm.</summary>
        static bool When(string when, bool powered)
        {
            switch (when)
            {
                case "powered": return powered;
                case "unpowered": return !powered;
                case "day": return LocationWeather.Day > 0.5f;
                case "night": return LocationWeather.Day < 0.3f;
                case "windy": return LocationWeather.Gust > 0.3f;
                case "storm": return LocationWeather.Storm > 0.2f;
                default: return true;
            }
        }

        /// <summary>s after start without sounds: the first states (late join) are not events.</summary>
        const float QuietStart = 2f;

        static Rules s_rules;
        SkyIslandLocationHost _host;
        readonly List<Watched> _parts = new List<Watched>();
        readonly Dictionary<SkyIslandLightFixture, SoundLoop> _fixtureLoops = new Dictionary<SkyIslandLightFixture, SoundLoop>();
        readonly List<Emitter> _emitters = new List<Emitter>();
        float _since;
        bool _ready, _powered;

        /// <summary>Server: a use refused because the part is locked or unpowered (the host calls it) — everyone hears the
        /// rattle; a locked door or lid tugged also strains the one who pulls.</summary>
        public static void Refused(SkyIslandLocationHost host, SkyIslandLocationHost.Part p, bool tugged = false)
        {
            if (host == null || p?.part == null) return;
            PartRule r = Find(p.id);
            if (r != null && !string.IsNullOrEmpty(r.locked)) SnowAudio.ServerPlay(r.locked, p.part.transform.position);
            if (tugged && p.category is SkyIslandLocationHost.Category.Door or SkyIslandLocationHost.Category.HostDoor or SkyIslandLocationHost.Category.Furniture)
                SnowAudio.ServerPlay("body.effort.push", p.part.transform.position, 0.6f);
        }

        static Rules Load()
        {
            if (s_rules != null) return s_rules;
            var text = Resources.Load<TextAsset>("Audio/Snow_LocationSounds");
            s_rules = text != null ? JsonUtility.FromJson<Rules>(text.text) : new Rules();
            return s_rules;
        }

        static PartRule Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (PartRule r in Load().parts)
                if (!string.IsNullOrEmpty(r.match) && Regex.IsMatch(id, r.match)) return r;
            return null;
        }

        void Start()
        {
            _host = GetComponent<SkyIslandLocationHost>();
            if (_host == null) { enabled = false; return; }
            Rules rules = Load();
            foreach (SkyIslandLocationHost.Part p in _host.parts)
            {
                if (p?.part == null) continue;
                PartRule r = Find(p.id);
                if (r == null) continue;
                _parts.Add(new Watched
                {
                    part = p, rule = r, target = p.part.target, amount = p.part.openAmount,
                    door = p.door != null ? p.door.State : DoorState.Closed,
                });
            }
            if (_host.lights != null) _host.lights.onFixtureEvent += OnFixture;
            if (_host.grid != null) _powered = _host.grid.Powered;
            FindEmitters(rules);
            _since = Time.time;
            _ready = true;
        }

        void OnDestroy()
        {
            if (_host != null && _host.lights != null) _host.lights.onFixtureEvent -= OnFixture;
        }

        void FindEmitters(Rules rules)
        {
            if (rules.emitters.Length == 0) return;
            var counts = new int[rules.emitters.Length];
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                for (int i = 0; i < rules.emitters.Length; i++)
                {
                    EmitterRule e = rules.emitters[i];
                    if (counts[i] >= e.max || string.IsNullOrEmpty(e.match) || !Regex.IsMatch(t.name, e.match)) continue;
                    Vector3 local = Center(t);                                   // a room or a building: its middle, not its pivot
                    SoundLoop loop = string.IsNullOrEmpty(e.loop) ? null : SoundLoop.Create(e.loop, t, local, true);
                    if (loop == null && string.IsNullOrEmpty(e.oneshot)) continue;
                    counts[i]++;
                    _emitters.Add(new Emitter
                    {
                        loop = loop, when = e.when, oneshot = e.oneshot, at = t, offset = local, every = e.every,
                        next = Time.time + UnityEngine.Random.Range(e.every.x * 0.3f, e.every.y),
                    });
                }
            }
        }

        bool Quiet => Time.time - _since < QuietStart;

        /// <summary>What this location sounds through (the pult, tests).</summary>
        public string Summary => $"{_parts.Count} parts, {_emitters.Count} emitters, {_fixtureLoops.Count} lamp loops";

        static Transform s_ear;
        static float s_earAt = -10f;

        /// <summary>This peer's ears, looked up every 2 s (the listener moves with the player's camera).</summary>
        static Transform Ear()
        {
            if (s_ear != null && Time.time - s_earAt < 2f) return s_ear;
            s_earAt = Time.time;
            AudioListener l = FindAnyObjectByType<AudioListener>();
            s_ear = l != null ? l.transform : Camera.main != null ? Camera.main.transform : null;
            return s_ear;
        }

        void Update()
        {
            if (!_ready) return;
            bool quiet = Quiet;
            foreach (Watched w in _parts) Tick(w, quiet);

            if (_host.grid != null && _host.grid.Powered != _powered)
            {
                _powered = _host.grid.Powered;
                if (!quiet) Play(_powered ? Load().gridOn : Load().gridOff, _host.grid.transform.position);
            }
            bool powered = _host.grid == null || _host.grid.Powered;
            Transform ear = Ear();
            foreach (Emitter e in _emitters)
            {
                bool on = When(e.when, powered);
                if (e.loop != null) e.loop.Switch(on);
                if (string.IsNullOrEmpty(e.oneshot) || Time.time < e.next) continue;
                e.next = Time.time + UnityEngine.Random.Range(e.every.x, e.every.y);
                if (!on || quiet || ear == null || e.at == null) continue;
                SoundCue cue = SnowAudio.Library != null ? SnowAudio.Library.Get(e.oneshot) : null;
                Vector3 point = e.at.TransformPoint(e.offset);
                if (cue != null && (ear.position - point).sqrMagnitude < cue.maxDistance * cue.maxDistance * 1.2f)
                    SnowAudio.Play(e.oneshot, point);
            }
        }

        void Tick(Watched w, bool quiet)
        {
            SkyIslandInteractable it = w.part.part;
            Vector3 at = it.transform.position;
            if (w.part.door != null)
            {
                DoorState s = w.part.door.State;
                if (s != w.door)
                {
                    if (!quiet)
                    {
                        if (s == DoorState.Opening && w.door == DoorState.Closed) Play(w.rule.open, at);
                        else if (s == DoorState.Closed) Play(w.rule.close, at);
                        else if (s == DoorState.Blocked) Play(w.rule.blocked, at);
                    }
                    w.door = s;
                }
                WhileOpen(w, s == DoorState.Open, at, quiet);
                return;
            }

            float t = it.target, a = it.openAmount;
            if (!Mathf.Approximately(t, w.target))
            {
                // a snap (late join, a client catching up) sets the travel too: no event
                bool snap = Mathf.Approximately(a, t) && !Mathf.Approximately(w.amount, t);
                if (!quiet && !snap) Play(t > w.target ? w.rule.open : w.rule.close, at);
                w.target = t;
            }
            bool moving = it.enabled && Mathf.Abs(a - t) > 0.002f && Mathf.Abs(a - w.amount) > 1e-5f;
            bool opening = t > a;
            MoveLoop(ref w.loop, w.rule.loop, it.transform, moving && !quiet);
            MoveLoop(ref w.loopOpen, w.rule.loopOpen, it.transform, moving && opening && !quiet);
            MoveLoop(ref w.loopClose, w.rule.loopClose, it.transform, moving && !opening && !quiet);
            MoveLoop(ref w.loop2, w.rule.loop2, it.transform, moving && !quiet);
            if (w.moving && !moving && Mathf.Abs(a - t) <= 0.002f && !quiet)
            {
                Play(w.rule.stop, at);
                Play(t > 0.5f ? w.rule.stopOpen : w.rule.stopClosed, at);
            }
            w.moving = moving;
            w.amount = a;
            WhileOpen(w, a > 0.98f, at, quiet);
        }

        void WhileOpen(Watched w, bool open, Vector3 at, bool quiet)
        {
            if (string.IsNullOrEmpty(w.rule.whileOpen)) return;
            if (!open) { w.nextOpen = 0f; return; }
            if (w.nextOpen == 0f) { w.nextOpen = Time.time + UnityEngine.Random.Range(w.rule.every.x, w.rule.every.y); return; }
            if (Time.time < w.nextOpen) return;
            w.nextOpen = Time.time + UnityEngine.Random.Range(w.rule.every.x, w.rule.every.y);
            if (!quiet) Play(w.rule.whileOpen, at);
        }

        static void MoveLoop(ref SoundLoop loop, string id, Transform at, bool on)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (loop == null && on) loop = SoundLoop.Create(id, at, Vector3.zero, true);
            if (loop != null) loop.Switch(on);
        }

        void OnFixture(SkyIslandLightFixture f, SkyIslandLightFixture.Event e)
        {
            if (f == null) return;
            FixtureRule r = null;
            string kind = f.kind.ToString();
            foreach (FixtureRule x in Load().fixtures)
                if (x.kind == kind) { r = x; break; }
            if (r == null) return;
            Vector3 at = f.transform.position;
            bool quiet = Quiet;
            switch (e)
            {
                case SkyIslandLightFixture.Event.Starting: if (!quiet) Play(r.starting, at); break;
                case SkyIslandLightFixture.Event.On: if (!quiet) Play(r.on, at); break;
                case SkyIslandLightFixture.Event.Off: if (!quiet) Play(r.off, at); break;
                case SkyIslandLightFixture.Event.Blink: if (!quiet) Play(r.blink, at); break;
                case SkyIslandLightFixture.Event.Fault: if (!quiet) Play(r.fault, at); break;
                case SkyIslandLightFixture.Event.Broken: if (!quiet) Play(r.broken, at); break;
            }
            if (string.IsNullOrEmpty(r.loop)) return;
            bool lit = e == SkyIslandLightFixture.Event.On || e == SkyIslandLightFixture.Event.Starting ||
                       e == SkyIslandLightFixture.Event.Blink || e == SkyIslandLightFixture.Event.Repaired;
            if (!_fixtureLoops.TryGetValue(f, out SoundLoop loop) && lit)
            {
                loop = SoundLoop.Create(r.loop, f.transform, Vector3.zero, true);
                _fixtureLoops[f] = loop;
            }
            if (loop != null) loop.Switch(lit);
        }

        /// <summary>This peer hears it; the server also tells the creatures.</summary>
        static void Play(string id, Vector3 at)
        {
            if (string.IsNullOrEmpty(id)) return;
            SnowAudio.Play(id, at);
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer || SnowAudio.Library == null) return;
            SoundCue cue = SnowAudio.Library.Get(id);
            if (cue != null && cue.noise > 0f) NoiseEvents.Emit(at, cue.noise, "sfx:" + id);
        }
    }
}
