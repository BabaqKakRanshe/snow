using UnityEngine;

namespace SkyIsland
{
    /// <summary>
    /// Keeps the reference look on the ground (dense haze, ~300 m visibility) but thins the fog when the
    /// camera flies away from / above the island so the floating island stays readable from the air.
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
        public bool applyInEditMode = false;

        void LateUpdate()
        {
            if (!Application.isPlaying && !applyInEditMode) return;
            var cam = target != null ? target : Camera.main;
            if (cam == null) return;
            RenderSettings.fogDensity = DensityAt(cam.transform.position);
        }

        /// <summary>Fog density this controller would use for a camera at world position p.</summary>
        public float DensityAt(Vector3 p)
        {
            float dx = (p.x - island.x) / Mathf.Max(1f, island.z);
            float dz = (p.z - island.y) / Mathf.Max(1f, island.w);
            float rn = Mathf.Sqrt(dx * dx + dz * dz);
            float outside = Mathf.Max(0f, rn - 1f) * 0.5f * (island.z + island.w);
            float above = Mathf.Max(0f, p.y - islandTopY - 40f);
            float below = Mathf.Max(0f, -p.y - 60f);
            float t = Mathf.Max(Mathf.Max(Smooth(above / heightRange), Smooth(outside / distanceRange)), Smooth(below / heightRange));
            return Mathf.Lerp(groundDensity, airDensity, t);
        }

        static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }
    }
}
