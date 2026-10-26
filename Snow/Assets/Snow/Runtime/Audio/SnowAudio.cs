using System.Collections.Generic;
using Snow.Cargo;
using Snow.Player;
using Snow.Voice;
using Unity.Netcode;
using UnityEngine;

namespace Snow.Audio
{
    /// <summary>
    /// The sound effects of the game (RESEARCH_AUDIO §4; QUESTIONS_4: built-in Unity audio). Starts on its own like the
    /// voice: keeps a pool of sources for one-shots, gives every player footsteps, gives every cargo item its impact
    /// sound on the server, and carries server one-shots to the clients (<see cref="AudioNet"/>).
    /// Local: <see cref="Play(string, Vector3, float)"/>. Everyone + creatures: <see cref="ServerPlay"/>.
    /// </summary>
    public class SnowAudio : MonoBehaviour
    {
        const int PoolSize = 32;

        /// <summary>Volume per bus (SoundBus order) and the master; the settings menu writes these.</summary>
        public static readonly float[] BusVolume = { 1f, 1f, 1f, 1f };
        public static float Master = 1f;

        /// <summary>A sound that played (or was asked for and missing) on this peer, for the pult's «Звук» page.</summary>
        public struct Played
        {
            public float time, volume;
            public string id, clip, via;
            public Vector3 at;
        }

        const int HistorySize = 16;
        static readonly Played[] s_history = new Played[HistorySize];
        static int s_historyNext, s_historyCount;

        /// <summary>The last sounds, newest first.</summary>
        public static IEnumerable<Played> History()
        {
            for (int i = 1; i <= s_historyCount; i++) yield return s_history[(s_historyNext - i + HistorySize) % HistorySize];
        }

        /// <summary>Pool sources playing now / pool size.</summary>
        public static int Busy
        {
            get
            {
                int n = 0;
                if (s_instance != null) foreach (AudioSource s in s_instance._pool) if (s.isPlaying) n++;
                return n;
            }
        }

        public static int PoolCount => PoolSize;

        static void Remember(string id, AudioClip clip, string via, Vector3 at, float volume)
        {
            s_history[s_historyNext] = new Played { time = Time.time, id = id, clip = clip != null ? clip.name : null, via = via, at = at, volume = volume };
            s_historyNext = (s_historyNext + 1) % HistorySize;
            if (s_historyCount < HistorySize) s_historyCount++;
        }

        static SnowAudio s_instance;
        readonly List<AudioSource> _pool = new List<AudioSource>();
        int _next;
        NetworkManager _attached;
        float _scanAt;

        public static SoundLibrary Library => SoundLibrary.Instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (s_instance != null) return;
            var go = new GameObject("SnowAudio");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<SnowAudio>();
        }

        void Awake()
        {
            for (int i = 0; i < PoolSize; i++)
            {
                var child = new GameObject("Sfx" + i);
                child.transform.SetParent(transform, false);
                var s = child.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.dopplerLevel = 0f;
                _pool.Add(s);
            }
            if (Library == null) Debug.LogWarning("[SnowAudio] no Resources/" + SoundLibrary.ResourcePath + " — run Snow/Audio/Rebuild Sound Library");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            AudioDevPage.Register();
#endif
        }

        void Update()
        {
            NetworkManager nm = NetworkManager.Singleton;
            bool up = nm != null && nm.IsListening && nm.CustomMessagingManager != null;
            if (up && _attached != nm) { AudioNet.Attach(nm); _attached = nm; }
            else if (!up && _attached != null) { AudioNet.Detach(); _attached = null; }

            if (Time.unscaledTime < _scanAt) return;
            _scanAt = Time.unscaledTime + 1f;
            foreach (PlayerNet p in FindObjectsByType<PlayerNet>(FindObjectsSortMode.None))
                if (p.GetComponent<Footsteps>() == null) p.gameObject.AddComponent<Footsteps>();
            if (up && nm.IsServer)
                foreach (CargoItem c in CargoItem.All)
                    if (c != null && c.GetComponent<ItemImpactSound>() == null) c.gameObject.AddComponent<ItemImpactSound>();
        }

        public static float Bus(SoundBus b) => Master * BusVolume[(int)b];

        /// <summary>Stops every one-shot of this peer (the pult's test buttons can start a 2-minute bed).</summary>
        public static void StopAll()
        {
            if (s_instance != null) foreach (AudioSource s in s_instance._pool) s.Stop();
        }

        public static bool Has(string id)
        {
            SoundCue c = Library != null ? Library.Get(id) : null;
            return c != null && c.clips != null && c.clips.Length > 0;
        }

        /// <summary>A clip of the cue for a source the caller owns (a loop, a 2D UI sound); null when missing (silent).</summary>
        public static AudioClip Clip(string id)
        {
            SoundCue cue = Library != null ? Library.Get(id) : null;
            AudioClip clip = cue != null ? cue.Pick() : null;
            if (clip == null && !string.IsNullOrEmpty(id)) Remember(id, null, "свой источник", Vector3.zero, 0f);
            return clip;
        }

        /// <summary>This peer only: a one-shot of the cue at the point (null when the cue or its clips are missing).</summary>
        public static AudioSource Play(string id, Vector3 at, float volumeScale = 1f) => PlayId(id, at, volumeScale, "здесь");

        /// <summary>An interface sound at this peer's ears (the listener, else the main camera).</summary>
        public static AudioSource PlayUi(string id, float volumeScale = 1f)
        {
            AudioListener l = Object.FindAnyObjectByType<AudioListener>();
            Transform at = l != null ? l.transform : Camera.main != null ? Camera.main.transform : null;
            return Play(id, at != null ? at.position : Vector3.zero, volumeScale);
        }

        internal static AudioSource PlayId(string id, Vector3 at, float volumeScale, string via)
        {
            SoundCue cue = Library != null ? Library.Get(id) : null;
            if (cue == null)
            {
                if (!string.IsNullOrEmpty(id)) Remember(id, null, via, at, 0f);       // asked for, not in the library
                return null;
            }
            return Play(cue, at, volumeScale, via);
        }

        public static AudioSource Play(SoundCue cue, Vector3 at, float volumeScale = 1f) => Play(cue, at, volumeScale, "здесь");

        static AudioSource Play(SoundCue cue, Vector3 at, float volumeScale, string via)
        {
            if (cue == null || s_instance == null) return null;
            AudioClip clip = cue.Pick();
            Remember(cue.id, clip, via, at, cue.Volume * volumeScale);
            if (clip == null) return null;
            AudioSource s = s_instance.NextSource();
            s.transform.position = at;
            Setup(s, cue);
            s.clip = clip;
            s.volume = cue.Volume * volumeScale * Bus(cue.bus);
            s.pitch = cue.Pitch;
            s.loop = false;
            s.Play();
            return s;
        }

        /// <summary>Server: everyone plays it (the host here, the clients by message), creatures hear cue.noise.</summary>
        public static void ServerPlay(string id, Vector3 at, float volumeScale = 1f, ulong source = ulong.MaxValue)
        {
            SoundCue cue = Library != null ? Library.Get(id) : null;
            if (cue == null) return;
            AudioNet.SendToOthers(id, at, volumeScale);
            Play(cue, at, volumeScale, "сервер");
            if (cue.noise > 0f) NoiseEvents.Emit(at, cue.noise * Mathf.Clamp(volumeScale, 0.5f, 2f), "sfx:" + id, source);
        }

        internal static void Setup(AudioSource s, SoundCue cue)
        {
            s.spatialBlend = cue.spatial;
            s.minDistance = cue.minDistance;
            s.maxDistance = Mathf.Max(cue.minDistance + 0.1f, cue.maxDistance);
            s.priority = cue.priority;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.dopplerLevel = 0f;
        }

        AudioSource NextSource()
        {
            for (int i = 0; i < _pool.Count; i++)
            {
                AudioSource s = _pool[(_next + i) % _pool.Count];
                if (!s.isPlaying) { _next = (_next + i + 1) % _pool.Count; return s; }
            }
            AudioSource oldest = _pool[_next];                   // all busy: the round-robin oldest gives way
            _next = (_next + 1) % _pool.Count;
            return oldest;
        }
    }
}
