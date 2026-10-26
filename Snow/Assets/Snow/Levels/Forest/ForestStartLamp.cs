using Snow.Audio;
using UnityEngine;

namespace Snow.Levels.Forest
{
    /// <summary>
    /// The lamp at the start gate (user 25.10, docs/gdd/QUESTIONS_START.md): it runs on its own battery, not on the base's
    /// power. On at the start with short dips now and then; after <see cref="lifeMinutes"/> it flickers harder for
    /// <see cref="dyingSeconds"/> and goes out for good, so the hut's lamp becomes the only light to walk to. Purely visual:
    /// every peer runs the same clock from its own scene load (a few seconds apart in co-op). The hut's candle uses it too
    /// (a long life, gutters now and then): <see cref="Kill"/> at X01 with the lantern (ForestOpening.x01Lamps).
    /// </summary>
    public class ForestStartLamp : MonoBehaviour
    {
        public Light[] lights = new Light[0];
        [Tooltip("Lens renderer and its material slot that shows the lamp on / off")] public Renderer lens;
        public int lensSlot = 1;
        public Material lensOn, lensOff;
        [Tooltip("Glow quads shown while the lamp is lit")] public Renderer[] glows = new Renderer[0];
        public float intensity = 16f;
        public float lifeMinutes = 12f;
        public float dyingSeconds = 20f;
        [Tooltip("Mean seconds between short dips while healthy")] public float dipEvery = 7f;

        float _start, _nextDip, _dipUntil;
        // sounds by what the lamp is (SOUND_LIST_FULL «Старт-пост», «Хижина»; user 26.10.2026)
        string _loopCue, _dipCue, _dyingCue, _outCue;
        SoundLoop _loop;
        bool _dyingHeard, _dipHeard;
        bool _shown, _dead;   // the prefab's lens is the "off" one: the first Show(1) swaps it

        void Awake()
        {
            string n = name;
            if (n == "StartLamp") { _loopCue = "start.lamp.flood.loop"; _dipCue = "start.lamp.dip"; _dyingCue = "start.lamp.dying"; _outCue = "start.lamp.out"; }
            else if (n == "StartBooth") _outCue = "start.booth.lamp.out";
            else if (n.Contains("Candle")) _outCue = "hut.candle.out";
            else if (n == "HutSign") _outCue = "hut.trail.lantern.out";             // its Beacon_Trail twin stays silent
        }

        void Sound(string id) { if (!string.IsNullOrEmpty(id)) SnowAudio.Play(id, transform.position); }

        void OnEnable()
        {
            _start = Time.time;
            _nextDip = _start + Random.Range(0.5f, 2f) * dipEvery;
            Show(1f);
        }

        void Update()
        {
            if (_dead) return;
            float t = Time.time - _start, life = lifeMinutes * 60f;
            if (t >= life + dyingSeconds)
            {
                Show(0f);
                _dead = true;
                Sound(_outCue);
                if (_loop != null) _loop.Switch(false);
                return;
            }
            bool dying = t >= life;
            if (!string.IsNullOrEmpty(_loopCue))
            {
                if (_loop == null) _loop = SoundLoop.Create(_loopCue, transform, Vector3.zero, true);
                if (_loop != null) _loop.Switch(true);
            }
            if (dying && !_dyingHeard) { _dyingHeard = true; Sound(_dyingCue); }
            bool dip = Time.time < _dipUntil;
            if (dip && !_dipHeard && !dying) Sound(_dipCue);
            _dipHeard = dip;
            if (Time.time >= _nextDip)
            {
                _dipUntil = Time.time + (dying ? Random.Range(0.05f, 0.6f) : Random.Range(0.04f, 0.18f));
                _nextDip = _dipUntil + (dying ? Random.Range(0.05f, 1.2f) : Random.Range(0.5f, 1.5f) * dipEvery);
            }
            float k = Time.time < _dipUntil ? (dying ? 0f : 0.25f) : 1f;
            if (dying) k *= 1f - 0.6f * ((t - life) / dyingSeconds);
            Show(k);
        }

        /// <summary>Out for good now (the hut's candle at X01).</summary>
        public void Kill()
        {
            if (!_dead) Sound(_outCue);
            if (_loop != null) _loop.Switch(false);
            Show(0f);
            _dead = true;
        }

        void Show(float k)
        {
            foreach (Light l in lights)
                if (l != null)
                {
                    l.enabled = k > 0f;
                    l.intensity = intensity * k;
                }
            bool on = k > 0.5f;
            if (on == _shown) return;
            _shown = on;
            foreach (Renderer g in glows) if (g != null) g.enabled = on;
            if (lens != null && lensOn != null && lensOff != null && lensSlot < lens.sharedMaterials.Length)
            {
                Material[] mats = lens.sharedMaterials;
                mats[lensSlot] = on ? lensOn : lensOff;
                lens.sharedMaterials = mats;
            }
        }
    }
}
