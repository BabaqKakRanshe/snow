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
        }

        void Update()
        {
            ForestDayNight dn = ForestDayNight.Instance;
            Transform ear = Ear();
            if (dn == null || ear == null)
            {
                if (_built) foreach (SoundLoop l in Loops()) if (l != null) l.Switch(false);
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

            Set(_wind, outdoor * (1f - 0.5f * storm));
            Set(_gusts, outdoor * gust);
            Set(_storm, outdoor * storm);
            Set(_day, outdoor * day * (1f - storm));
            Set(_night, outdoor * (1f - day) * (1f - 0.6f * storm));
            Set(_howl, outdoor * (1f - day) * 0.8f * (1f - storm));
            Set(_indoor, _indoor01);
            float cutoff = Mathf.Lerp(22000f, 900f, _indoor01);
            foreach (SoundLoop l in new[] { _wind, _gusts, _storm, _day, _night, _howl }) if (l != null) l.Muffle(cutoff);

            NetworkManager nm = NetworkManager.Singleton;
            if (nm != null && nm.IsServer && nm.IsListening) ServerOneShots(day);
        }

        static void Set(SoundLoop l, float level)
        {
            if (l == null) return;
            l.Level = level;
            l.Switch(level > 0.01f);
        }

        SoundLoop[] Loops() => new[] { _wind, _gusts, _storm, _day, _night, _howl, _indoor };

        /// <summary>Server: small forest sounds near a random player, heard by everyone at that point.</summary>
        void ServerOneShots(float day)
        {
            float now = Time.time;
            if (_nextCreak == 0f) { _nextCreak = now + 10f; _nextBranch = now + 20f; _nextFall = now + 400f; }
            if (now >= _nextCreak) { _nextCreak = now + Random.Range(12f, 35f); Scatter("os.tree.creak", 12f, 45f); }
            if (now >= _nextBranch) { _nextBranch = now + Random.Range(day > 0.5f ? 25f : 15f, day > 0.5f ? 60f : 40f); Scatter("os.branch", 15f, 50f); }
            if (now >= _nextFall) { _nextFall = now + Random.Range(400f, 900f); Scatter("os.treefall", 80f, 160f); }
        }

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
