using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Snow.Voice
{
    /// <summary>
    /// A loudspeaker of the voice on this peer: the radio on the control desk now, later every radio and the base
    /// loudspeaker. Put on the anchor object the first time the server makes it speak. Plays an utterance as one piece:
    /// press-to-talk click, the lines back to back (the splices of numbers too: the small gap between pieces is the seam
    /// the impostor will be made of, VISION §5), the squelch tail; static runs under the whole of it and in the pauses
    /// (research §6). The voice is recorded dry; the radio's band and grit are made here (QUESTIONS_VOICE 7): high-pass
    /// 300 Hz, low-pass 3.4 kHz, a little distortion. Numbers TEMPORARY.
    /// </summary>
    public class VoiceEmitter : MonoBehaviour
    {
        public const float RadioRange = 15f, AirRange = 40f;
        static readonly Dictionary<ulong, VoiceEmitter> s_byAnchor = new Dictionary<ulong, VoiceEmitter>();
        static readonly Dictionary<Vector3Int, VoiceEmitter> s_byPoint = new Dictionary<Vector3Int, VoiceEmitter>();

        AudioSource _voice, _static;
        AudioHighPassFilter _hp;
        AudioLowPassFilter _lp;
        AudioDistortionFilter _dist;
        readonly List<VoiceLine> _lines = new List<VoiceLine>();
        int _next;
        double _nextAt;
        bool _busy;
        int _priority;
        VoiceNet.Channel _channel;
        float _staticTarget;

        /// <summary>The line playing now (null between utterances).</summary>
        public VoiceLine Current { get; private set; }
        public bool Busy => _busy;
        public float Range => _channel == VoiceNet.Channel.Radio ? RadioRange : AirRange;

        /// <summary>The emitter of an anchor object (a spawned NetworkObject) or, without one, of a fixed point.</summary>
        public static VoiceEmitter For(ulong anchor, Vector3 point)
        {
            if (anchor != 0)
            {
                if (s_byAnchor.TryGetValue(anchor, out VoiceEmitter e) && e != null) return e;
                NetworkManager nm = NetworkManager.Singleton;
                if (nm != null && nm.SpawnManager != null && nm.SpawnManager.SpawnedObjects.TryGetValue(anchor, out NetworkObject no) && no != null)
                {
                    if (!no.TryGetComponent(out e)) e = no.gameObject.AddComponent<VoiceEmitter>();
                    s_byAnchor[anchor] = e;
                    return e;
                }
            }
            var key = Vector3Int.RoundToInt(point * 4f);
            if (s_byPoint.TryGetValue(key, out VoiceEmitter p) && p != null) return p;
            var go = new GameObject("VoiceEmitter " + point);
            go.transform.position = point;
            p = go.AddComponent<VoiceEmitter>();
            s_byPoint[key] = p;
            return p;
        }

        void Awake()
        {
            _voice = gameObject.AddComponent<AudioSource>();
            _hp = gameObject.AddComponent<AudioHighPassFilter>();
            _lp = gameObject.AddComponent<AudioLowPassFilter>();
            _dist = gameObject.AddComponent<AudioDistortionFilter>();
            Setup(_voice);
            _static = new GameObject("Static").AddComponent<AudioSource>();
            _static.transform.SetParent(transform, false);
            Setup(_static);
            _static.clip = VoiceClips.Static;
            _static.loop = true;
            _static.volume = 0f;
            Channel(VoiceNet.Channel.Radio);
        }

        static void Setup(AudioSource s)
        {
            s.playOnAwake = false;
            s.spatialBlend = 1f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 1f;
            s.maxDistance = RadioRange;
            s.dopplerLevel = 0f;
            s.priority = 32;                                         // speech over effects (0 is the highest)
        }

        void Channel(VoiceNet.Channel c)
        {
            _channel = c;
            bool radio = c == VoiceNet.Channel.Radio;
            _hp.enabled = _lp.enabled = _dist.enabled = radio;
            _hp.cutoffFrequency = 300f;
            _lp.cutoffFrequency = 3400f;
            _dist.distortionLevel = 0.35f;
            _voice.maxDistance = _static.maxDistance = Range;
        }

        /// <summary>Play an utterance; one of at least the same priority cuts the current one, a lower one waits behind it.</summary>
        public void Play(List<VoiceLine> lines, VoiceNet.Channel channel, int priority)
        {
            if (lines == null || lines.Count == 0) return;
            if (_busy && priority < _priority)
            {
                _lines.AddRange(lines);                                // the server keeps channels apart; this only avoids a lost line
                Subtitles(lines);
                return;
            }
            if (_busy) Cut();
            Channel(channel);
            _lines.Clear();
            _lines.AddRange(lines);
            _next = 0;
            _priority = priority;
            _busy = true;
            double now = AudioSettings.dspTime;
            if (_channel == VoiceNet.Channel.Radio)
            {
                _voice.PlayOneShot(VoiceClips.Click, 0.8f);
                _staticTarget = 0.18f;
                if (!_static.isPlaying) _static.Play();
                _nextAt = now + 0.12;
            }
            else _nextAt = now;
            Subtitles(lines);
        }

        /// <summary>Stop at once: the radio clicks off with its squelch tail.</summary>
        public void Cut()
        {
            if (!_busy) return;
            _voice.Stop();
            _lines.Clear();
            Current = null;
            End();
            VoiceHud.ClearFrom(this);
        }

        void End()
        {
            _busy = false;
            Current = null;
            if (_channel == VoiceNet.Channel.Radio)
            {
                _voice.PlayOneShot(VoiceClips.Squelch, 0.7f);
                _staticTarget = 0f;
            }
        }

        void Update()
        {
            _static.volume = Mathf.MoveTowards(_static.volume, _staticTarget, Time.deltaTime * 0.8f);
            if (_static.isPlaying && _static.volume <= 0.001f && _staticTarget <= 0f) _static.Stop();
            if (!_busy) return;
            double now = AudioSettings.dspTime;
            if (now < _nextAt) return;
            if (_next >= _lines.Count)
            {
                End();
                return;
            }
            Current = _lines[_next++];
            AudioClip clip = VoiceClips.Of(Current);
            if (clip != null)
            {
                _voice.clip = clip;
                _voice.Play();
            }
            _nextAt = now + VoiceClips.Duration(Current);
        }

        void Subtitles(List<VoiceLine> lines)
        {
            // one subtitle per utterance: the splices (a number digit by digit) read as one sentence
            var text = new System.Text.StringBuilder();
            float seconds = 0.12f;
            string speaker = lines[0].Speaker;
            foreach (VoiceLine l in lines)
            {
                if (l.Speaker != speaker)
                {
                    VoiceHud.Subtitle(this, speaker, text.ToString(), seconds);
                    text.Clear();
                    speaker = l.Speaker;
                }
                if (text.Length > 0) text.Append(' ');
                text.Append(l.Text);
                seconds += VoiceClips.Duration(l);
            }
            if (text.Length > 0) VoiceHud.Subtitle(this, speaker, text.ToString(), seconds + 0.6f);
        }

        void OnDestroy()
        {
            VoiceHud.ClearFrom(this);
        }
    }
}
