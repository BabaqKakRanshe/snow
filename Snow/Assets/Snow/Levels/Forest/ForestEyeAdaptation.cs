using UnityEngine;
using UnityEngine.Rendering;

namespace Snow.Levels.Forest
{
    /// <summary>
    /// The eyes (22.10, solution 3; Project Zomboid players' ask for adaptation instead of a flat void): five times a second
    /// the player camera's frame (the PSX target, post already applied) is shrunk on the GPU and read back asynchronously;
    /// its log-average brightness with the exposure divided out is compared with <see cref="reference"/> (a dark night without
    /// lights). In the dark the eyes open slowly, up to <see cref="maxOpen"/> EV; looking into headlights or a lamp they close
    /// fast, up to <see cref="maxClose"/> EV, so everything outside the beam sinks. The clock adds <see cref="Offset"/> to its
    /// night exposure and reports the exposure it applied (<see cref="appliedExposure"/>). Local only; no allocations per
    /// measurement beyond the readback.
    /// </summary>
    public class ForestEyeAdaptation : MonoBehaviour
    {
        [Tooltip("Frame brightness (linear, exposure divided out) at which the eyes neither open nor close")] public float reference = 0.001f;
        [Tooltip("Share of the brightness difference the eyes make up (1 = full auto exposure)")] [Range(0f, 1f)] public float strength = 0.6f;
        [Tooltip("EV: the eyes open at most this much in the dark")] public float maxOpen = 0.6f;
        [Tooltip("EV: and close at most this much in bright light")] public float maxClose = 1.5f;
        [Tooltip("s: opening in the dark is slow")] public float openTime = 12f;
        [Tooltip("s: closing in light is fast")] public float closeTime = 0.7f;
        [Tooltip("s between frame measurements")] public float interval = 0.2f;

        /// <summary>Set by the clock every frame: the total post exposure, EV, of the frames being measured.</summary>
        [System.NonSerialized] public float appliedExposure;

        /// <summary>EV the eyes add now (positive in the dark).</summary>
        public float Offset { get; private set; }
        /// <summary>Last measured frame brightness with the exposure divided out (-1 before the first).</summary>
        public float SceneLuminance { get; private set; } = -1f;

        RenderTexture _small;
        float _next, _target, _exposureAtRequest;
        bool _pending;

        void OnDestroy()
        {
            if (_small != null) { _small.Release(); Destroy(_small); }
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float tau = _target < Offset ? closeTime : openTime;
            Offset += (_target - Offset) * (1f - Mathf.Exp(-dt / Mathf.Max(tau, 0.01f)));

            if (_pending || Time.unscaledTime < _next || !SystemInfo.supportsAsyncGPUReadback) return;
            Camera cam = Camera.main;
            RenderTexture src = cam != null ? cam.targetTexture : null;
            if (src == null) return;                                   // no PSX target: no measurement, the eyes stay as they are
            _next = Time.unscaledTime + interval;
            if (_small == null)
            {
                _small = new RenderTexture(64, 32, 0, RenderTextureFormat.ARGB32)
                {
                    name = "EyeAdaptation", useMipMap = true, autoGenerateMips = true, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave
                };
                _small.Create();
            }
            Graphics.Blit(src, _small);
            _exposureAtRequest = appliedExposure;
            _pending = true;
            AsyncGPUReadback.Request(_small, 3, TextureFormat.RGBA32, OnRead);    // mip 3: 8 x 4
        }

        void OnRead(AsyncGPUReadbackRequest r)
        {
            _pending = false;
            if (this == null || r.hasError) return;
            var data = r.GetData<byte>();
            int n = data.Length / 4;
            if (n == 0) return;
            double logSum = 0d;
            for (int i = 0; i < n; i++)
            {
                float lr = Mathf.GammaToLinearSpace(data[i * 4] / 255f), lg = Mathf.GammaToLinearSpace(data[i * 4 + 1] / 255f),
                      lb = Mathf.GammaToLinearSpace(data[i * 4 + 2] / 255f);
                logSum += Mathf.Log(Mathf.Max(0.2126f * lr + 0.7152f * lg + 0.0722f * lb, 1e-4f));
            }
            float measured = Mathf.Exp((float)(logSum / n));
            SceneLuminance = measured / Mathf.Pow(2f, _exposureAtRequest);
            float ev = Mathf.Log(SceneLuminance / Mathf.Max(reference, 1e-5f), 2f);
            _target = Mathf.Clamp(-ev * strength, -maxClose, maxOpen);
        }
    }
}
