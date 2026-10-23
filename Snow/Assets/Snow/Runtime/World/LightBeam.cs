using UnityEngine;
using UnityEngine.Rendering;

namespace Snow.World
{
    /// <summary>
    /// Visible beam of a spot light in the fog (user 23.10, realism list item 5): the hand flashlight; the yard lamps have
    /// ForestLightBeams, the van's lamps VanRig. One pyramid along the light's forward with the light's spot angle, drawn
    /// with UtilityPlane/Beam (single scattering, clipped by the scene depth: no beam through walls). Air thickness from the
    /// scene fog as VanRig.AtmosphereDensity, so the beam thickens with the night's fog. Shows only while the light is on and
    /// within <see cref="maxDistance"/> of the camera. Local on every peer. No shader in the build (a scene without a beam
    /// material) = no beam. Numbers TEMPORARY (by eye).
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class LightBeam : MonoBehaviour
    {
        /// <summary>Off in the F1 menu.</summary>
        public static bool Enabled = true;

        [Tooltip("m")] public float length = 18f;
        [Tooltip("Core sigma (deg)")] public float coreWidth = 8f;
        public float skirt = 0.03f;
        [Tooltip("Beam brightness per light unit (TEMPORARY: the yard lamps use 14 at 7 units seen from aside; a flashlight is seen along its beam)")]
        public float gain = 4f;
        [Range(0f, 0.9f)] public float forwardScatter = 0.6f;
        [Tooltip("Scene fog to atmosphere, as VanRig.atmosphereFogScale")] public float fogScale = 40f;
        [Tooltip("m: farther from the camera the beam is off")] public float maxDistance = 60f;

        Light _light;
        Renderer _beam;
        Material _mat;
        Mesh _mesh;
        float _lastI = -1f, _lastD = -1f;
        Color _lastC;
        static readonly int IntensityId = Shader.PropertyToID("_Intensity"), ColorId = Shader.PropertyToID("_Color"), DensityId = Shader.PropertyToID("_Density");

        void Start()
        {
            _light = GetComponent<Light>();
            Shader sh = Shader.Find("UtilityPlane/Beam");
            if (sh == null || _light.type != LightType.Spot) { enabled = false; return; }
            float half = _light.spotAngle * 0.5f;
            _mesh = PyramidMesh(half, length);
            _mat = new Material(sh) { name = "FX_LightBeam", hideFlags = HideFlags.DontSave };
            _mat.SetFloat("_Length", length);
            _mat.SetFloat("_XMin", -half);
            _mat.SetFloat("_XMax", half);
            _mat.SetFloat("_YMin", -half);
            _mat.SetFloat("_YMax", half);
            _mat.SetFloat("_CoreWidth", coreWidth);
            _mat.SetFloat("_Skirt", skirt);
            _mat.SetFloat("_Gain", gain);
            _mat.SetFloat("_G", forwardScatter);
            var go = new GameObject("Beam") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            go.layer = gameObject.layer;
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.allowOcclusionWhenDynamic = false;
            r.enabled = false;
            _beam = r;
        }

        void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
            if (_mesh != null) Destroy(_mesh);
        }

        void LateUpdate()
        {
            if (_beam == null) return;
            Camera cam = Camera.main;
            float density = Density();
            bool show = Enabled && _light.isActiveAndEnabled && _light.intensity > 0.01f && density > 0f && cam != null
                        && (cam.transform.position - transform.position).sqrMagnitude < maxDistance * maxDistance;
            if (_beam.enabled != show) _beam.enabled = show;
            if (!show) return;
            if (Mathf.Abs(_light.intensity - _lastI) > 1e-4f) { _mat.SetFloat(IntensityId, _light.intensity); _lastI = _light.intensity; }
            if (Mathf.Abs(density - _lastD) > 1e-5f) { _mat.SetFloat(DensityId, density); _lastD = density; }
            Color c = _light.color.linear;
            if (_light.useColorTemperature) c *= Mathf.CorrelatedColorTemperatureToRGB(_light.colorTemperature);
            if (c != _lastC) { _mat.SetVector(ColorId, new Vector4(c.r, c.g, c.b, 1f)); _lastC = c; }
        }

        /// <summary>Scattering per metre from the scene fog (VanRig.AtmosphereDensity).</summary>
        float Density()
        {
            if (!RenderSettings.fog) return 0f;
            float a = RenderSettings.fogMode == FogMode.Linear
                ? 3f / Mathf.Max(RenderSettings.fogEndDistance - RenderSettings.fogStartDistance, 1f) * fogScale
                : RenderSettings.fogDensity * fogScale;
            return 0.12f * Mathf.Clamp01(a);
        }

        /// <summary>Closed pyramid, apex at the light, every face wound outwards (the shader draws the back faces).</summary>
        static Mesh PyramidMesh(float halfAngle, float L)
        {
            float t = Mathf.Tan(halfAngle * Mathf.Deg2Rad);
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
            var mesh = new Mesh { name = "FX_LightBeamPyramid", hideFlags = HideFlags.DontSave };
            mesh.vertices = v;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
