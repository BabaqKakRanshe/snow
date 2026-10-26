using System.Collections.Generic;
using Snow.Audio;
using Snow.Player;
using Snow.World;
using Unity.Netcode;
using UnityEngine;

namespace Snow.Levels.Forest
{
    /// <summary>
    /// The Forest's background sound (RESEARCH_AUDIO §4.7), on every peer while a Forest day / night runs:
    /// beds — wind always, gusts and storm by the wind at the listener (EnvironmentProvider; without one a slow made-up
    /// breath), birds by day, owls and far howling by night, a room tone under a roof; outdoor beds go quiet and muffled
    /// indoors. One-shots — tree creaks, branches, rarely a falling tree — come from the server for everyone at the same
    /// point (QUESTIONS_4 13: no per-player differences on such small things). Music: later, by the director.
    /// Numbers TEMPORARY.
    /// </summary>
    public class ForestAmbience : MonoBehaviour
    {
        static ForestAmbience s_instance;

        SoundLoop _wind, _gusts, _storm, _day, _night, _howl, _indoor;
        AudioListener _listener;
        float _listenerAt, _indoor01, _roofAt, _nextCreak, _nextBranch, _nextFall;
        float _windShown, _dayShown;                                        // last values, for the pult
        bool _roof, _built;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (s_instance != null) return;
            var go = new GameObject("ForestAmbience");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<ForestAmbience>();
        }

        void Build()
        {
            _built = true;
            _wind = SoundLoop.Create("amb.forest.wind", transform, Vector3.zero, false);
            _gusts = SoundLoop.Create("amb.forest.gusts", transform, Vector3.zero, false);
            _storm = SoundLoop.Create("amb.wind.storm", transform, Vector3.zero, false);
            _day = SoundLoop.Create("amb.forest.day", transform, Vector3.zero, false);
            _night = SoundLoop.Create("amb.forest.night", transform, Vector3.zero, false);
            _howl = SoundLoop.Create("amb.night.howl", transform, Vector3.zero, false);
            _indoor = SoundLoop.Create("amb.indoor.tone", transform, Vector3.zero, false);
            if (_gusts != null) _gusts.fadeSeconds = 3f;
            if (_storm != null) _storm.fadeSeconds = 4f;
            if (_day != null) _day.fadeSeconds = 8f;
            if (_night != null) _night.fadeSeconds = 8f;
            if (_howl != null) _howl.fadeSeconds = 8f;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Snow.Dev.DevPult.Add("Звук", 45, DrawDev, () => ForestDayNight.Instance != null);
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Pult page «Звук»: the Forest's beds and their levels, the wind, test one-shots (host).</summary>
        void DrawDev()
        {
            Snow.Dev.DevUi.Header("Фон леса");
            Snow.Dev.DevUi.Raw($"ветер {_windShown:0.0} м/с{(EnvironmentProvider.Current != null ? "" : " (погоды нет, условный)")}   день {_dayShown:0.00}   под крышей {_indoor01:0.00}");
            (string name, SoundLoop loop)[] beds =
                { ("ветер", _wind), ("порывы", _gusts), ("буря", _storm), ("день", _day), ("ночь", _night), ("вой", _howl), ("помещение", _indoor) };
            foreach ((string name, SoundLoop loop) in beds)
                Snow.Dev.DevUi.Raw(loop == null ? $"{name,-10} нет звука" : $"{name,-10} уровень {loop.Level:0.00}   громкость {loop.Volume:0.00}{(loop.On ? "" : "   выкл")}");
            if (!Snow.Dev.DevUi.HostOnly()) return;
            float now = Time.time;
            Snow.Dev.DevUi.Raw($"следующие: скрип через {Mathf.Max(0f, _nextCreak - now):0} с, ветка {Mathf.Max(0f, _nextBranch - now):0} с, дерево {Mathf.Max(0f, _nextFall - now):0} с");
            if (Snow.Dev.DevUi.Button("Скрип дерева рядом")) Scatter("os.tree.creak", 8f, 15f);
            Snow.Dev.DevUi.Row();
            if (Snow.Dev.DevUi.Button("Ветка рядом")) Scatter("os.branch", 8f, 15f);
            Snow.Dev.DevUi.Row();
            if (Snow.Dev.DevUi.Button("Падает дерево")) Scatter("os.treefall", 40f, 60f);
        }
#endif

        void Update()
        {
            ForestDayNight dn = ForestDayNight.Instance;
            Transform ear = Ear();
            if (dn == null || ear == null)
            {
                if (_built) foreach (SoundLoop l in Loops()) if (l != null) l.Switch(false);
                foreach (var kv in _roomLoops) if (kv.Value != null) kv.Value.Switch(false);
                return;
            }
            if (!_built) Build();
            Vector3 p = ear.position;

            if (Time.time >= _roofAt) { _roofAt = Time.time + 0.5f; _roof = SurfaceResolver.Indoors(p); }
            _indoor01 = Mathf.MoveTowards(_indoor01, _roof ? 1f : 0f, Time.deltaTime / 1.2f);
            float outdoor = 1f - 0.85f * _indoor01;

            float h = dn.Hour;
            float day = Mathf.InverseLerp(dn.sunRise - 0.5f, dn.sunRise + 1f, h) * (1f - Mathf.InverseLerp(dn.sunSet - 1f, dn.sunSet + 0.5f, h));
            float wind = EnvironmentProvider.Current != null
                ? EnvironmentProvider.WindAt(p).magnitude
                : 4f + 4f * Mathf.PerlinNoise(Time.time * 0.05f, 0.3f);           // no weather yet: a slow breath
            float gust = Mathf.InverseLerp(3f, 9f, wind), storm = Mathf.InverseLerp(10f, 16f, wind);
            _windShown = wind;
            _dayShown = day;
            LocationWeather.Day = day; LocationWeather.Gust = gust; LocationWeather.Storm = storm;   // for the locations' emitters

            Set(_wind, outdoor * (1f - 0.5f * storm));
            Set(_gusts, outdoor * gust);
            Set(_storm, outdoor * storm);
            Set(_day, outdoor * day * (1f - storm));
            Set(_night, outdoor * (1f - day) * (1f - 0.6f * storm));
            Set(_howl, outdoor * (1f - day) * 0.8f * (1f - storm));
            if (Time.time >= _roomAt) { _roomAt = Time.time + 0.5f; _room = RoomAt(p); }
            SoundLoop tone = _room != null ? RoomLoop(_room) : null;
            Set(_indoor, tone == null ? _indoor01 : 0f);
            foreach (var kv in _roomLoops) Set(kv.Value, kv.Value == tone ? _indoor01 : 0f);
            Music(day);
            Weather(p, outdoor, day, gust, storm);
            float cutoff = Mathf.Lerp(22000f, 900f, _indoor01);
            foreach (SoundLoop l in new[] { _wind, _gusts, _storm, _day, _night, _howl }) if (l != null) l.Muffle(cutoff);

            NetworkManager nm = NetworkManager.Singleton;
            if (nm != null && nm.IsServer && nm.IsListening) ServerOneShots(day);
        }

        // ---- weather layers from the wind this ambience already hears (SOUND_LIST_FULL «Погода», user 26.10.2026)
        SoundLoop _blizHiss, _blizGlass, _frost, _prestorm, _ridge;
        float _nextSlam, _nextRime;

        void Weather(Vector3 p, float outdoor, float day, float gust, float storm)
        {
            bool inside = _indoor01 > 0.5f;
            Bed(ref _blizHiss, "amb.weather.blizzard.hiss", outdoor * Mathf.Clamp01((storm - 0.2f) / 0.6f));
            Bed(ref _blizGlass, "amb.weather.blizzard.glass", _indoor01 * Mathf.Clamp01((storm - 0.2f) / 0.6f));
            Bed(ref _frost, "amb.weather.frost", outdoor * (1f - day) * (1f - gust) * 0.6f);              // a clear calm frost night
            Bed(ref _prestorm, "amb.weather.prestorm", outdoor * Mathf.Clamp01(gust - storm * 2f) * 0.5f);  // the wind rising
            float h = Terrain.activeTerrain != null ? p.y - Terrain.activeTerrain.SampleHeight(p) - Terrain.activeTerrain.transform.position.y : 0f;
            Bed(ref _ridge, "amb.wind.ridge", outdoor * Mathf.Clamp01((h - 12f) / 20f));                 // up a tower or a mast
            if (storm > 0.5f && Time.time >= _nextSlam)
            {
                _nextSlam = Time.time + Random.Range(6f, 18f);
                SnowAudio.Play("amb.weather.blizzard.slam", p + Random.onUnitSphere * 6f);
            }
            if (day < 0.3f && gust < 0.3f && !inside && Time.time >= _nextRime)
            {
                _nextRime = Time.time + Random.Range(20f, 60f);
                SnowAudio.Play("amb.weather.rime", p + Random.onUnitSphere * 4f);
            }
        }

        void Bed(ref SoundLoop loop, string id, float level)
        {
            if (loop == null && level > 0.01f) loop = SoundLoop.Create(id, transform, Vector3.zero, false);
            Set(loop, level);
        }

        // ---- music (QUESTIONS_4: an unobtrusive base bed + Dead Space–style danger stems by one "danger" number).
        // No director yet: danger is a little at night outdoors and jumps with the prologue's scares (Spike), fading
        // over half a minute; a director only has to call Spike / set the number later.
        static float s_spike;
        SoundLoop _calm;
        readonly SoundLoop[] _stems = new SoundLoop[4];
        static readonly string[] StemIds = { "music.danger.stem1", "music.danger.stem2", "music.danger.stem3", "music.danger.stem4" };

        /// <summary>A scare happened (every peer): the danger music rises at once and fades on its own.</summary>
        public static void Spike(float danger)
        {
            if (danger >= 0.6f && s_spike < 0.5f) SnowAudio.PlayUi("body.breath.fear", 0.7f);   // the own breath catches
            s_spike = Mathf.Max(s_spike, Mathf.Clamp01(danger));
        }

        void Music(float day)
        {
            s_spike = Mathf.MoveTowards(s_spike, 0f, Time.deltaTime / 30f);
            bool atBase = _room != null && _room.StartsWith("base.", System.StringComparison.Ordinal);
            float danger = Mathf.Max(s_spike, (1f - day) * (1f - _indoor01) * 0.25f);
            if (_calm == null) _calm = SoundLoop.Create("music.base.calm", transform, Vector3.zero, false);
            if (_calm != null) { _calm.fadeSeconds = 6f; Set(_calm, atBase ? 0.35f * _indoor01 * (1f - danger) : 0f); }
            for (int i = 0; i < 4; i++)
            {
                if (_stems[i] == null) _stems[i] = SoundLoop.Create(StemIds[i], transform, Vector3.zero, false);
                if (_stems[i] == null) continue;
                _stems[i].fadeSeconds = 3f;
                float from = 0.1f + i * 0.25f;                              // stem 1 at 0.1, 2 at 0.35, 3 at 0.6, 4 at 0.85
                Set(_stems[i], Mathf.Clamp01((danger - from) / 0.15f) * 0.6f);
            }
        }

        // ---- room tones (SOUND_LIST_FULL «Помещения», user 26.10.2026): the room the ear is in, by its renderers' bounds
        static readonly (string obj, string cue)[] Rooms =
        {
            ("R_Windfang", "base.room.windfang"), ("R_Leitstand", "base.room.leitstand"), ("R_Flur_EG", "base.room.corridor"),
            ("R_Flur_OG", "base.room.corridor"), ("R_Flur_UG", "base.room.corridor"), ("R_Lager", "base.room.lager"),
            ("R_Serverraum", "base.room.serverraum"), ("R_Garage", "base.room.garage"), ("R_Kaserne", "base.room.kaserne"),
            ("R_Kueche", "base.room.kueche"), ("R_Waschraum", "base.room.waschraum"), ("R_Stationsleiter", "base.room.stationsleiter"),
            ("R_Heizung", "base.room.heizung"), ("R_Schaltraum", "base.room.schaltraum"), ("R_Treppenhaus", "base.room.treppenhaus"),
            ("Garage", "base.room.garageshed"), ("GenContainer", "base.room.gencont"), ("PumpHouse", "base.room.pumphouse"),
            ("TowerHut", "amb.hut.indoor"), ("ForesterCave", "world.cave.tone"),
            ("BunkerHangar", "base.room.bunker.hangar"), ("BlastDoor", "base.room.bunker.vestibule"), ("BunkerEntrance", "base.room.bunker.shaft"),
        };
        readonly List<(Bounds box, string cue)> _roomBoxes = new List<(Bounds, string)>();
        readonly Dictionary<string, SoundLoop> _roomLoops = new Dictionary<string, SoundLoop>();
        bool _roomsFound;
        float _roomAt;
        string _room;

        string RoomAt(Vector3 p)
        {
            if (!_roomsFound)
            {
                _roomsFound = true;
                var wanted = new Dictionary<string, string>();
                foreach ((string obj, string cue) in Rooms) wanted[obj] = cue;
                foreach (Transform t in FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (!wanted.TryGetValue(t.name, out string cue)) continue;
                    Renderer[] rs = t.GetComponentsInChildren<Renderer>();
                    if (rs.Length == 0) continue;
                    Bounds b = rs[0].bounds;
                    foreach (Renderer r in rs) b.Encapsulate(r.bounds);
                    _roomBoxes.Add((b, cue));
                }
                _roomBoxes.Sort((a, b) => (a.box.size.x * a.box.size.y * a.box.size.z).CompareTo(b.box.size.x * b.box.size.y * b.box.size.z));
            }
            foreach ((Bounds box, string cue) in _roomBoxes)                   // the smallest box that holds the ear
                if (box.Contains(p)) return cue;
            return null;
        }

        SoundLoop RoomLoop(string cue)
        {
            if (_roomLoops.TryGetValue(cue, out SoundLoop l)) return l;
            l = SoundLoop.Create(cue, transform, Vector3.zero, false);
            _roomLoops[cue] = l;
            return l;
        }

        static void Set(SoundLoop l, float level)
        {
            if (l == null) return;
            l.Level = level;
            l.Switch(level > 0.01f);
        }

        SoundLoop[] Loops() => new[] { _wind, _gusts, _storm, _day, _night, _howl, _indoor };

        void OnEnable() => ForestDayNight.DayChanged += OnDayChanged;
        void OnDisable() => ForestDayNight.DayChanged -= OnDayChanged;

        /// <summary>Dawn: the crows take off (server, heard by everyone).</summary>
        static void OnDayChanged(bool isDay)
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (isDay && nm != null && nm.IsServer && nm.IsListening) Scatter("forest.dawn.crows", 40f, 120f);
        }

        /// <summary>Server: small forest sounds near a random player, heard by everyone at that point.</summary>
        void ServerOneShots(float day)
        {
            float now = Time.time;
            if (_nextCreak == 0f) { _nextCreak = now + 10f; _nextBranch = now + 20f; _nextFall = now + 400f; }
            if (now >= _nextCreak) { _nextCreak = now + Random.Range(12f, 35f); Scatter("os.tree.creak", 12f, 45f); }
            if (now >= _nextBranch) { _nextBranch = now + Random.Range(day > 0.5f ? 25f : 15f, day > 0.5f ? 60f : 40f); Scatter("os.branch", 15f, 50f); }
            if (now >= _nextFall) { _nextFall = now + Random.Range(400f, 900f); Scatter("os.treefall", 80f, 160f); }
            for (int i = 0; i < Extras.Length; i++)
            {
                Extra e = Extras[i];
                if (_nextExtra[i] == 0f) { _nextExtra[i] = now + Random.Range(e.min * 0.3f, e.max); continue; }
                if (now < _nextExtra[i]) continue;
                _nextExtra[i] = now + Random.Range(e.min, e.max);
                bool fits = e.when == 0 || (e.when == 1 ? day > 0.5f : day < 0.3f);
                if (fits) Scatter(e.cue, e.near, e.far);
            }
        }

        /// <summary>More small sounds of the forest (SOUND_LIST_FULL «Лес», user 26.10.2026). when: 0 any, 1 day, 2 night.</summary>
        struct Extra { public string cue; public byte when; public float min, max, near, far; }
        static readonly Extra[] Extras =
        {
            new Extra { cue = "forest.bird.crow", when = 1, min = 40f, max = 120f, near = 30f, far = 90f },
            new Extra { cue = "forest.bird.woodpecker", when = 1, min = 90f, max = 240f, near = 50f, far = 120f },
            new Extra { cue = "forest.owl", when = 2, min = 60f, max = 180f, near = 40f, far = 110f },
            new Extra { cue = "forest.deer.far", when = 2, min = 150f, max = 420f, near = 70f, far = 150f },
            new Extra { cue = "forest.snow.slide", when = 0, min = 25f, max = 70f, near = 8f, far = 30f },
            new Extra { cue = "forest.frost.crack", when = 2, min = 50f, max = 150f, near = 15f, far = 60f },
            new Extra { cue = "forest.lake.ice.groan", when = 0, min = 200f, max = 500f, near = 120f, far = 220f },
            new Extra { cue = "forest.far.helicopter", when = 2, min = 900f, max = 2400f, near = 250f, far = 400f },
        };
        readonly float[] _nextExtra = new float[Extras.Length];

        static void Scatter(string cue, float minDist, float maxDist)
        {
            PlayerNet[] players = FindObjectsByType<PlayerNet>(FindObjectsSortMode.None);
            if (players.Length == 0) return;
            Vector3 c = players[Random.Range(0, players.Length)].transform.position;
            if (SurfaceResolver.Indoors(c)) return;                          // not into the house
            float a = Random.Range(0f, Mathf.PI * 2f), d = Random.Range(minDist, maxDist);
            Vector3 at = c + new Vector3(Mathf.Cos(a) * d, Random.Range(1f, 6f), Mathf.Sin(a) * d);
            SnowAudio.ServerPlay(cue, at);
        }

        Transform Ear()
        {
            if (_listener != null && _listener.isActiveAndEnabled) return _listener.transform;
            if (Time.unscaledTime < _listenerAt) return null;
            _listenerAt = Time.unscaledTime + 1f;
            foreach (AudioListener l in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                if (l.isActiveAndEnabled) { _listener = l; return l.transform; }
            return null;
        }
    }
}
