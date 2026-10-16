using System.Collections.Generic;
using UnityEngine;

namespace SkyIsland
{
    /// <summary>
    /// Drives the red obstruction lights: swaps the shared beacon material for a runtime copy and
    /// modulates its emission together with the beacon Light components (names starting with "Beacon").
    /// </summary>
    [AddComponentMenu("Sky Island/Beacon Controller")]
    public class SkyIslandBeacons : MonoBehaviour
    {
        public enum Mode { Steady, Pulse, Flash }

        [Tooltip("Shared beacon material asset (M_Light_Red)")] public Material beaconMaterial;
        [Tooltip("Shared red fog-halo material (M_Glow_Red), pulses with the beacons")] public Material glowMaterial;
        [Tooltip("Root to search for beacon renderers/lights (defaults to this object)")] public Transform root;
        public Mode mode = Mode.Pulse;
        public float flashesPerMinute = 30f;
        [Range(0f, 1f)] public float minLevel = 0.35f;

        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int EmissiveId = Shader.PropertyToID("_EmissiveColor");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        Material _runtime, _glowRuntime;
        float _glowBase;
        Color _baseEmission;
        int _prop;
        readonly List<Light> _lights = new List<Light>();
        readonly List<float> _base = new List<float>();

        void Start()
        {
            var r0 = root != null ? root : transform;
            if (glowMaterial != null && glowMaterial.HasProperty(IntensityId))
            {
                _glowRuntime = new Material(glowMaterial) { name = glowMaterial.name + " (Runtime)" };
                _glowBase = _glowRuntime.GetFloat(IntensityId);
                foreach (var r in r0.GetComponentsInChildren<Renderer>(true))
                    if (r.sharedMaterial == glowMaterial) r.sharedMaterial = _glowRuntime;
            }
            if (beaconMaterial == null) return;
            _runtime = new Material(beaconMaterial) { name = beaconMaterial.name + " (Runtime)" };
            _prop = _runtime.HasProperty(EmissionId) ? EmissionId : (_runtime.HasProperty(EmissiveId) ? EmissiveId : -1);
            if (_prop != -1) _baseEmission = _runtime.GetColor(_prop);
            foreach (var r in r0.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] == beaconMaterial) { mats[i] = _runtime; changed = true; }
                if (changed) r.sharedMaterials = mats;
            }
            foreach (var l in r0.GetComponentsInChildren<Light>(true))
                if (l.name.StartsWith("Beacon")) { _lights.Add(l); _base.Add(l.intensity); }
        }

        void Update()
        {
            if (_runtime == null && _glowRuntime == null) return;
            float level = 1f;
            float f = flashesPerMinute / 60f;
            switch (mode)
            {
                case Mode.Pulse:
                    level = Mathf.Lerp(minLevel, 1f, 0.5f + 0.5f * Mathf.Sin(Time.time * f * 2f * Mathf.PI));
                    break;
                case Mode.Flash:
                    level = Mathf.Repeat(Time.time * f, 1f) < 0.35f ? 1f : minLevel;
                    break;
            }
            if (_runtime != null && _prop != -1) _runtime.SetColor(_prop, _baseEmission * level);
            if (_glowRuntime != null) _glowRuntime.SetFloat(IntensityId, _glowBase * level);
            for (int i = 0; i < _lights.Count; i++)
                if (_lights[i] != null) _lights[i].intensity = _base[i] * level;
        }

        void OnDestroy()
        {
            if (_runtime != null) Destroy(_runtime);
            if (_glowRuntime != null) Destroy(_glowRuntime);
        }
    }
}
