using UnityEngine;

namespace SkyIsland
{
    /// <summary>
    /// Keeps the reference look on the ground (dense haze, ~300 m visibility) but thins the fog when the
    /// camera flies away from / above the island so the floating island stays readable from the air.
    /// Fog events (SkyIslandFogEvent) scale the result through densityScale.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Sky Island/Fog By Altitude")]
    public class SkyIslandFogByAltitude : MonoBehaviour
    {
        [Tooltip("Camera to track (defaults to Camera.main)")] public Camera target;
        [Tooltip("Exponential density on the airfield (reference look)")] public float groundDensity = 0.0036f;
        [Tooltip("Exponential density when flying away from the island")] public float airDensity = 0.0012f;
        [Tooltip("Island footprint centre (x, z) and half-axes (x, z) in world metres")]
        public Vector4 island = new Vector4(0f, -40f, 745f, 470f);
        public float islandTopY = 20f;
        [Tooltip("Height above the island where the fog reaches air density")] public float heightRange = 350f;
        [Tooltip("Horizontal distance outside the island where the fog reaches air density")] public float distanceRange = 350f;
        [Tooltip("Thin the fog when the camera is below bottomY (looking at the floating island from underneath)")] public bool thinBelow = true;
        [Tooltip("Height under which the fog thins (with Thin Below)")] public float bottomY = -60f;
        public bool applyInEditMode = false;
        [Header("Linear fog (PSX look)")]
        [Tooltip("Drive RenderSettings fog start/end instead of the exponential density")] public bool linear = false;
        [Tooltip("Linear fog start / end on the ground (m)")] public float groundStart = 15f, groundEnd = 220f;
        [Tooltip("Air end = ground end / airFactor (fog thins away from the area)")] public float airFactor = 0.33f;

        /// <summary>Runtime multiplier of the computed density (set by fog events; 1 = unchanged).</summary>
        [System.NonSerialized] public float densityScale = 1f;

        void LateUpdate()
        {
            if (!Application.isPlaying && !applyInEditMode) return;
            var cam = target != null ? target : Camera.main;
            if (cam == null) return;
            if (linear)
            {
                float t = BlendAt(cam.transform.position);
                float k = Mathf.Lerp(1f, 1f / Mathf.Max(0.05f, airFactor), t) / Mathf.Max(0.05f, densityScale);
                RenderSettings.fogStartDistance = groundStart * k;
                RenderSettings.fogEndDistance = groundEnd * k;
                return;
            }
            RenderSettings.fogDensity = DensityAt(cam.transform.position) * densityScale;
        }

        /// <summary>Fog density this controller would use for a camera at world position p.</summary>
        public float DensityAt(Vector3 p) => Mathf.Lerp(groundDensity, airDensity, BlendAt(p));

        /// <summary>0 on the ground inside the area, 1 high above / far away from it.</summary>
        public float BlendAt(Vector3 p)
        {
            float dx = (p.x - island.x) / Mathf.Max(1f, island.z);
            float dz = (p.z - island.y) / Mathf.Max(1f, island.w);
            float rn = Mathf.Sqrt(dx * dx + dz * dz);
            float outside = Mathf.Max(0f, rn - 1f) * 0.5f * (island.z + island.w);
            float above = Mathf.Max(0f, p.y - islandTopY - 40f);
            float below = thinBelow ? Mathf.Max(0f, bottomY - p.y) : 0f;
            return Mathf.Max(Mathf.Max(Smooth(above / heightRange), Smooth(outside / distanceRange)), Smooth(below / heightRange));
        }

        static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }
    }
}
