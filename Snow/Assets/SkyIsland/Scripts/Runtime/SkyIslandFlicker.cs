using System.Collections.Generic;
using UnityEngine;

namespace SkyIsland
{
    /// <summary>
    /// Flickering lamps: each entry swaps a shared material for a runtime copy on the renderers under the root and
    /// modulates its emission (_EmissionColor / _EmissiveColor, glow halos: _Intensity) with smooth noise between
    /// min and max; Light components whose name starts with lightPrefix flicker along. Assets are never modified.
    /// </summary>
    [AddComponentMenu("Sky Island/Flicker")]
    public class SkyIslandFlicker : MonoBehaviour
    {
        [System.Serializable]
        public class Entry
        {
            [Tooltip("Shared material asset (renderers get a runtime copy in play mode)")] public Material material;
            [Tooltip("Lowest emission multiplier")] public float min = 0.6f;
            [Tooltip("Highest emission multiplier")] public float max = 1f;
            [Tooltip("Noise speed (about how many changes per second)")] public float speed = 1f;
            [Tooltip("Lights whose name starts with this prefix flicker along (empty = none)")] public string lightPrefix = "";
        }

        [Tooltip("Root to search for renderers and lights (defaults to this object)")] public Transform root;
        public List<Entry> entries = new List<Entry>();

        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int EmissiveId = Shader.PropertyToID("_EmissiveColor");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        sealed class Live
        {
            public Entry entry;
            public SkyIslandMaterialSwap swap;
            public int colorProp = -1;
            public Color baseColor;
            public bool hasIntensity;
            public float baseIntensity;
            public float seed;
            public readonly List<Light> lights = new List<Light>();
            public readonly List<float> lightBase = new List<float>();
        }

        readonly List<Live> _live = new List<Live>();

        /// <summary>Entries that share a lightPrefix (a lamp material and its glow halo) get the same noise curve.</summary>
        static float SeedFor(string prefix, int index)
        {
            if (string.IsNullOrEmpty(prefix)) return 17.3f * (index + 1);
            int h = 0;
            foreach (char c in prefix) h = unchecked(h * 31 + c);
            return 17.3f + (h & 0x3FF) * 0.731f;
        }

        void Start()
        {
            var r0 = root != null ? root : transform;
            var drivenPrefixes = new HashSet<string>();
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e == null || e.material == null) continue;
                var l = new Live { entry = e, swap = SkyIslandMaterialSwap.Create(r0, e.material), seed = SeedFor(e.lightPrefix, i) };
                var m = l.swap.Runtime;
                if (m.HasProperty(EmissionId)) l.colorProp = EmissionId;
                else if (m.HasProperty(EmissiveId)) l.colorProp = EmissiveId;
                if (l.colorProp != -1) l.baseColor = m.GetColor(l.colorProp);
                l.hasIntensity = m.HasProperty(IntensityId);
                if (l.hasIntensity) l.baseIntensity = m.GetFloat(IntensityId);
                if (!string.IsNullOrEmpty(e.lightPrefix) && drivenPrefixes.Add(e.lightPrefix))   // each light is driven by one entry only
                    foreach (var light in r0.GetComponentsInChildren<Light>(true))
                        if (light.name.StartsWith(e.lightPrefix, System.StringComparison.Ordinal)) { l.lights.Add(light); l.lightBase.Add(light.intensity); }
                _live.Add(l);
            }
        }

        void Update()
        {
            float t = Time.time;
            foreach (var l in _live)
            {
                var e = l.entry;
                float s = Mathf.Max(0.01f, e.speed);
                // two octaves of smooth noise: slow drift + quicker flutter
                float n = Mathf.PerlinNoise(l.seed, t * s) * 0.7f + Mathf.PerlinNoise(l.seed + 5.1f, t * s * 3.3f) * 0.3f;
                float level = Mathf.Lerp(e.min, e.max, Mathf.Clamp01(n));
                var m = l.swap.Runtime;
                if (m != null)
                {
                    if (l.colorProp != -1) m.SetColor(l.colorProp, l.baseColor * level);
                    if (l.hasIntensity) m.SetFloat(IntensityId, l.baseIntensity * level);
                }
                for (int i = 0; i < l.lights.Count; i++)
                    if (l.lights[i] != null) l.lights[i].intensity = l.lightBase[i] * level;
            }
        }

        void OnDestroy()
        {
            foreach (var l in _live) l.swap.Restore();
            _live.Clear();
        }
    }
}
