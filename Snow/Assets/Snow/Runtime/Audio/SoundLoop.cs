using UnityEngine;

namespace Snow.Audio
{
    /// <summary>
    /// A looping cue on its own source (a machine, an ambience bed): switched on and off with a fade, scaled by
    /// <see cref="Level"/> (0..1, e.g. wind strength), optionally muffled (low-pass) — this peer only. A bed starts at a
    /// random point of its clip, so two loops of one cue never sound in step.
    /// </summary>
    public class SoundLoop : MonoBehaviour
    {
        [Tooltip("s for a full fade in / out")] public float fadeSeconds = 1.5f;
        /// <summary>0..1 on top of the cue's volume; changes are smoothed by the fade.</summary>
        public float Level = 1f;
        /// <summary>Times the cue's pitch (an engine's rpm, a fan's speed); applied every frame.</summary>
        public float PitchScale = 1f;

        SoundCue _cue;
        AudioSource _src;
        AudioLowPassFilter _lp;
        float _base, _pitch = 1f, _cutoff = 22000f;
        bool _on;

        public bool On => _on;
        /// <summary>The source's volume now (after bus, level and fade), for the pult.</summary>
        public float Volume => _src != null ? _src.volume : 0f;

        /// <summary>A loop of the cue at the parent (spatial: 3D at its point; else 2D). Null when the cue is missing.</summary>
        public static SoundLoop Create(string id, Transform parent, Vector3 localPosition, bool spatial)
        {
            SoundCue cue = SnowAudio.Library != null ? SnowAudio.Library.Get(id) : null;
            if (cue == null || cue.clips == null || cue.clips.Length == 0) return null;
            var go = new GameObject("Loop " + id);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            var loop = go.AddComponent<SoundLoop>();
            loop._cue = cue;
            loop._src = go.AddComponent<AudioSource>();
            SnowAudio.Setup(loop._src, cue);
            if (!spatial) loop._src.spatialBlend = 0f;
            loop._src.playOnAwake = false;
            loop._src.loop = true;
            loop._src.volume = 0f;
            loop._base = cue.Volume;
            return loop;
        }

        public void Switch(bool on)
        {
            _on = on;
            if (!on || _src.isPlaying) return;
            _src.clip = _cue.Pick();
            if (_src.clip == null) return;
            _pitch = _cue.Pitch;
            _src.pitch = _pitch * PitchScale;
            _src.time = Random.Range(0f, _src.clip.length * 0.9f);
            _src.Play();
        }

        /// <summary>Low-pass cutoff in Hz (22000 = open).</summary>
        public void Muffle(float cutoffHz)
        {
            _cutoff = cutoffHz;
            if (_lp == null && cutoffHz < 21000f) _lp = gameObject.AddComponent<AudioLowPassFilter>();
            if (_lp != null) _lp.cutoffFrequency = cutoffHz;
        }

        void Update()
        {
            if (_src == null) return;
            float want = _on ? _base * Mathf.Clamp01(Level) * SnowAudio.Bus(_cue.bus) : 0f;
            _src.volume = Mathf.MoveTowards(_src.volume, want, Time.deltaTime / Mathf.Max(0.05f, fadeSeconds));
            _src.pitch = _pitch * Mathf.Clamp(PitchScale, 0.1f, 3f);
            if (!_on && _src.isPlaying && _src.volume <= 0.0005f) _src.Stop();
        }
    }
}
