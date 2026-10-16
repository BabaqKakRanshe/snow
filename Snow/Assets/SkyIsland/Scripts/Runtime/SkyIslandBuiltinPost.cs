using UnityEngine;
using UnityEngine.Rendering;

namespace SkyIsland
{
    /// <summary>
    /// Post-processing for the Built-in Render Pipeline (URP/HDRP use the Volume profile instead):
    /// bloom -> vignette -> post exposure -> contrast (LogC, like URP) -> Neutral tonemapping -> reference LUT (sRGB) -> film grain.
    /// Same math and parameters as the URP profile, so the look matches.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    [ImageEffectAllowedInSceneView]
    [AddComponentMenu("Sky Island/Built-in Post (LUT + Bloom)")]
    public class SkyIslandBuiltinPost : MonoBehaviour
    {
        public Shader shader;

        [Header("Bloom")]
        public float bloomThreshold = 0.9f;
        public float bloomIntensity = 1.1f;
        [Range(0f, 1f)] public float bloomScatter = 0.72f;
        [Tooltip("Gamma-space tint, normalised by luminance like URP")] public Color bloomTint = new Color(1f, 0.96f, 0.87f);

        [Header("Vignette")]
        [Range(0f, 1f)] public float vignetteIntensity = 0.32f;
        [Range(0.01f, 1f)] public float vignetteSmoothness = 0.42f;
        [Tooltip("Linear colour (used as-is, like URP)")] public Color vignetteColor = new Color(0.05f, 0.035f, 0.008f);

        [Header("Grading")]
        public float postExposure = 0f;
        [Tooltip("Same as URP Color Adjustments > Contrast")][Range(-100f, 100f)] public float contrast = 0f;
        [Tooltip("2D strip LUT (height x height^2), imported linear")] public Texture2D lut;
        [Range(0f, 1f)] public float lutContribution = 1f;

        [Header("Film grain")]
        [Range(0f, 1f)] public float grainIntensity = 0.22f;
        [Range(0f, 1f)] public float grainResponse = 0.8f;

        const int MaxIterations = 7;
        readonly RenderTexture[] _down = new RenderTexture[MaxIterations];
        readonly RenderTexture[] _up = new RenderTexture[MaxIterations];
        Material _mat;

        static readonly int BloomParamsId = Shader.PropertyToID("_BloomParams");
        static readonly int BloomLowId = Shader.PropertyToID("_BloomLow");
        static readonly int BloomTexId = Shader.PropertyToID("_BloomTex");
        static readonly int BloomTintId = Shader.PropertyToID("_BloomTint");
        static readonly int VignetteId = Shader.PropertyToID("_Vignette");
        static readonly int VignetteColorId = Shader.PropertyToID("_VignetteColor");
        static readonly int ExposureId = Shader.PropertyToID("_PostExposure");
        static readonly int ContrastId = Shader.PropertyToID("_Contrast");
        static readonly int LutId = Shader.PropertyToID("_UserLut");
        static readonly int LutParamsId = Shader.PropertyToID("_UserLutParams");
        static readonly int GrainId = Shader.PropertyToID("_GrainParams");

        void OnEnable()
        {
            var cam = GetComponent<Camera>();
            if (cam != null) cam.allowHDR = true;
        }

        void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (GraphicsSettings.currentRenderPipeline != null || shader == null || !shader.isSupported)
            {
                Graphics.Blit(src, dst);
                return;
            }
            if (_mat == null) _mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };

            float thr = Mathf.GammaToLinearSpace(bloomThreshold);
            _mat.SetVector(BloomParamsId, new Vector4(thr, thr * 0.5f, bloomIntensity, Mathf.Lerp(0.05f, 0.95f, bloomScatter)));

            var fmt = RenderTextureFormat.DefaultHDR;
            int w = Mathf.Max(1, src.width / 2), h = Mathf.Max(1, src.height / 2);
            int iters = 0;
            Texture prev = src;
            for (int i = 0; i < MaxIterations; i++)
            {
                if (w < 4 || h < 4) break;
                _down[i] = RenderTexture.GetTemporary(w, h, 0, fmt);
                _down[i].filterMode = FilterMode.Bilinear;
                Graphics.Blit(prev, _down[i], _mat, i == 0 ? 0 : 1);
                prev = _down[i];
                iters++;
                w /= 2; h /= 2;
            }
            RenderTexture last = iters > 0 ? _down[iters - 1] : null;
            for (int i = iters - 2; i >= 0; i--)
            {
                _up[i] = RenderTexture.GetTemporary(_down[i].width, _down[i].height, 0, fmt);
                _up[i].filterMode = FilterMode.Bilinear;
                _mat.SetTexture(BloomLowId, last);
                Graphics.Blit(_down[i], _up[i], _mat, 2);
                last = _up[i];
            }

            Color tint = bloomTint.linear;
            float luma = tint.r * 0.2126f + tint.g * 0.7152f + tint.b * 0.0722f;
            tint = luma > 0f ? tint * (1f / luma) : Color.white;
            _mat.SetTexture(BloomTexId, last != null ? (Texture)last : Texture2D.blackTexture);
            _mat.SetVector(BloomTintId, new Vector4(tint.r, tint.g, tint.b, 1f));
            _mat.SetVector(VignetteId, new Vector4(vignetteIntensity * 3f, vignetteSmoothness * 5f, 0f, 0f));
            _mat.SetVector(VignetteColorId, new Vector4(vignetteColor.r, vignetteColor.g, vignetteColor.b, 1f));
            _mat.SetFloat(ExposureId, Mathf.Pow(2f, postExposure));
            _mat.SetFloat(ContrastId, contrast / 100f + 1f);
            if (lut != null)
            {
                _mat.SetTexture(LutId, lut);
                _mat.SetVector(LutParamsId, new Vector4(1f / lut.width, 1f / lut.height, lut.height - 1f, lutContribution));
            }
            else
            {
                _mat.SetVector(LutParamsId, Vector4.zero);
            }
            _mat.SetVector(GrainId, new Vector4(grainIntensity, grainResponse, Random.value, Random.value));
            Graphics.Blit(src, dst, _mat, 3);

            for (int i = 0; i < MaxIterations; i++)
            {
                if (_down[i] != null) { RenderTexture.ReleaseTemporary(_down[i]); _down[i] = null; }
                if (_up[i] != null) { RenderTexture.ReleaseTemporary(_up[i]); _up[i] = null; }
            }
        }

        void OnDisable()
        {
            if (_mat != null) DestroyImmediate(_mat);
            _mat = null;
        }
    }
}
