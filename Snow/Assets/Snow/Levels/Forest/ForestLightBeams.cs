using UnityEngine;
using UnityEngine.Rendering;

namespace Snow.Levels.Forest
{
    /// <summary>
    /// Light cones in the fog under the yard lamps and the antenna lamps (FOREST_ASSEMBLY_UNITY.md K6.4;
    /// LIGHTING_TECHNIQUE_FOR_ENVIRONMENT.md §6.2–6.3). One pyramid per lamp along the light's forward (down), drawn with the
    /// project's UtilityPlane/Beam shader (single scattering, clipped by the scene depth: no beam through walls). A beam shows
    /// only while its lamp burns (the Light component on and bright: the grid switches the lamps), only for the nearest
    /// <see cref="maxBeams"/> lit lamps within <see cref="maxDistance"/> of the camera. Local effect on every peer, nothing
    /// goes over the network. Geometry and parameters after UtilityPlaneRig.Lights (its BeamDef / PyramidMesh / FxParams).
    /// </summary>
    public class ForestLightBeams : MonoBehaviour
    {
        [Tooltip("Lamps with a beam (Light_power_* of the yard LightPoles and the antennas Dish_1…5)")] public Light[] lamps = new Light[0];
        [Tooltip("Template material on UtilityPlane/Beam (instanced per lamp at run time)")] public Material beamMaterial;
        [Tooltip("Air thickness for the beams, as UtilityPlaneRig.atmosphere: look.fog.density 0.0075 x 40 (TEMPORARY, by eye)")]
        public float atmosphere = 0.3f;
        [Tooltip("Beam brightness per light unit (TEMPORARY, by eye 21.10: 10 faint, 25 lights the ground; the lamps are 7 units)")]
        public float gain = 14f;
        public int maxBeams = 8;
        [Tooltip("m: beams farther from the camera switch off")] public float maxDistance = 80f;
        [Header("Pyramid (lamp frame, +Z = light forward)")]
        public float length = 18f;
        public float halfAngle = 40f;
        [Tooltip("Core sigma (deg)")] public float coreWidth = 20f;
        public float skirt = 0.05f;
        [Range(0f, 0.9f)] public float forwardScatter = 0.55f;

        /// <summary>s between choices of the nearest lit lamps.</summary>
        const float Pick = 0.2f;
        /// <summary>Light intensity below which a lamp counts as dark.</summary>
        const float Dark = 0.01f;

        Renderer[] _beams;
        Material[] _mats;
        float[] _lastI;
        Color[] _lastC;
        readonly int[] _chosen = new int[16];
        readonly float[] _chosenD = new float[16];
        int _count;
        float _next;
        Camera _cam;
        Mesh _mesh;
        static readonly int IntensityId = Shader.PropertyToID("_Intensity"), ColorId = Shader.PropertyToID("_Color"), DensityId = Shader.PropertyToID("_Density");

        void Awake()
        {
            int n = lamps.Length;
            _beams = new Renderer[n];
            _mats = new Material[n];
            _lastI = new float[n];
            _lastC = new Color[n];
            if (beamMaterial == null) { enabled = false; return; }
            _mesh = PyramidMesh();
            float density = 0.12f * Mathf.Clamp01(atmosphere);         // scattering per metre, as UtilityPlaneRig.AtmosphereDensity
            for (int i = 0; i < n; i++)
            {
                if (lamps[i] == null) continue;
                var go = new GameObject("Beam");
                go.transform.SetParent(lamps[i].transform, false);
                go.layer = lamps[i].gameObject.layer;
                var m = new Material(beamMaterial) { name = "FX_Beam_" + i };
                m.SetFloat("_Length", length);
                m.SetFloat("_XMin", -halfAngle);
                m.SetFloat("_XMax", halfAngle);
                m.SetFloat("_YMin", -halfAngle);
                m.SetFloat("_YMax", halfAngle);
                m.SetFloat("_CoreWidth", coreWidth);
                m.SetFloat("_Skirt", skirt);
                m.SetFloat("_Gain", gain);
                m.SetFloat("_G", forwardScatter);
                m.SetFloat(DensityId, density);
                m.SetFloat(IntensityId, 0f);
                go.AddComponent<MeshFilter>().sharedMesh = _mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = m;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                r.allowOcclusionWhenDynamic = false;
                r.enabled = false;
                _beams[i] = r;
                _mats[i] = m;
                _lastI[i] = -1f;
            }
        }

        void OnDestroy()
        {
            if (_mats != null) foreach (Material m in _mats) if (m != null) Destroy(m);
            if (_mesh != null) Destroy(_mesh);
        }

        void LateUpdate()
        {
            if (Time.unscaledTime >= _next)
            {
                _next = Time.unscaledTime + Pick;
                _cam = Camera.main;
                Choose();
            }
            // the chosen lamps follow their light every frame (the grid fades and flickers them)
            for (int k = 0; k < _count; k++)
            {
                int i = _chosen[k];
                Light l = lamps[i];
                bool lit = l != null && l.isActiveAndEnabled && l.intensity > Dark;
                if (_beams[i].enabled != lit) _beams[i].enabled = lit;
                if (!lit) continue;
                float intensity = l.intensity;
                if (Mathf.Abs(intensity - _lastI[i]) > 1e-4f * Mathf.Max(1f, intensity))
                {
                    _mats[i].SetFloat(IntensityId, intensity);
                    _lastI[i] = intensity;
                }
                Color c = l.color.linear;
                if (l.useColorTemperature) c *= Mathf.CorrelatedColorTemperatureToRGB(l.colorTemperature);
                if (c != _lastC[i])
                {
                    _mats[i].SetVector(ColorId, new Vector4(c.r, c.g, c.b, 1f));       // linear, as UtilityPlaneRig.FxParams
                    _lastC[i] = c;
                }
            }
        }

        /// <summary>The nearest lit lamps within reach of the camera; every other beam off. No allocations.</summary>
        void Choose()
        {
            int max = Mathf.Clamp(maxBeams, 0, _chosen.Length);
            _count = 0;
            Vector3 eye = _cam != null ? _cam.transform.position : transform.position;
            float reach = maxDistance * maxDistance;
            for (int i = 0; i < lamps.Length; i++)
            {
                Light l = lamps[i];
                if (max == 0 || _beams[i] == null || l == null || !l.isActiveAndEnabled || l.intensity <= Dark) continue;
                float d = (l.transform.position - eye).sqrMagnitude;
                if (d > reach) continue;
                // insertion into the sorted short list
                int k = _count < max ? _count++ : max;
                if (k == max && d >= _chosenD[max - 1]) continue;
                if (k == max) k = max - 1;
                while (k > 0 && _chosenD[k - 1] > d)
                {
                    _chosen[k] = _chosen[k - 1];
                    _chosenD[k] = _chosenD[k - 1];
                    k--;
                }
                _chosen[k] = i;
                _chosenD[k] = d;
            }
            for (int i = 0; i < _beams.Length; i++)
            {
                if (_beams[i] == null) continue;
                bool keep = false;
                for (int k = 0; k < _count; k++) if (_chosen[k] == i) { keep = true; break; }
                if (!keep && _beams[i].enabled) _beams[i].enabled = false;
            }
        }

        /// <summary>Closed pyramid, apex at the lamp, every face wound outwards (the shader draws the back faces).</summary>
        Mesh PyramidMesh()
        {
            float t = Mathf.Tan(halfAngle * Mathf.Deg2Rad), L = length;
            var v = new[]
            {
                Vector3.zero,
                new Vector3(-t * L, -t * L, L), new Vector3(t * L, -t * L, L),
                new Vector3(t * L, t * L, L), new Vector3(-t * L, t * L, L)
            };
            int[] tris = { 0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 1, 1, 3, 2, 1, 4, 3 };
            Vector3 inside = (v[1] + v[2] + v[3] + v[4]) * 0.15f;
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 a = v[tris[i]], b = v[tris[i + 1]], c = v[tris[i + 2]];
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3f - inside) < 0f)
                {
                    int s = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = s;
                }
            }
            var mesh = new Mesh { name = "FX_ForestBeamPyramid" };
            mesh.vertices = v;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
