using UnityEngine;

namespace SkyIsland
{
    /// <summary>Keeps the SkyIsland skybox sun position in sync with the directional light.</summary>
    [ExecuteAlways]
    [AddComponentMenu("Sky Island/Sky Controller")]
    public class SkyIslandSky : MonoBehaviour
    {
        public Light sun;
        [Tooltip("Leave empty to use RenderSettings.skybox")] public Material skybox;

        static readonly int SunDirId = Shader.PropertyToID("_SunDir");
        Vector3 _last;

        void Update()
        {
            var m = skybox != null ? skybox : RenderSettings.skybox;
            if (m == null || sun == null || !m.HasProperty(SunDirId)) return;
            Vector3 d = -sun.transform.forward;
            if ((d - _last).sqrMagnitude < 1e-8f) return;
            _last = d;
            m.SetVector(SunDirId, new Vector4(d.x, d.y, d.z, 0f));
        }
    }
}
