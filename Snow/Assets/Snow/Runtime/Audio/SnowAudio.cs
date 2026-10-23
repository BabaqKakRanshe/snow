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

        public static bool Has(string id)
        {
            SoundCue c = Library != null ? Library.Get(id) : null;
            return c != null && c.clips != null && c.clips.Length > 0;
        }

        /// <summary>This peer only: a one-shot of the cue at the point (null when the cue or its clips are missing).</summary>
        public static AudioSource Play(string id, Vector3 at, float volumeScale = 1f) =>
            Play(Library != null ? Library.Get(id) : null, at, volumeScale);

        public static AudioSource Play(SoundCue cue, Vector3 at, float volumeScale = 1f)
        {
            if (cue == null || s_instance == null) return null;
            AudioClip clip = cue.Pick();
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
            Play(cue, at, volumeScale);
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
