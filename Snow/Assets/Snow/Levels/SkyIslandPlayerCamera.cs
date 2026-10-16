using SkyIsland;
using Snow.Player;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Snow.Levels
{
    /// <summary>
    /// Sky Island level: gives the local player's camera everything the location's look depends on, copied from the
    /// level camera the Sky Island builder made (HDR, post-processing, SMAA, dithering, far clip, fog by altitude), and
    /// switches that debug fly camera off so only the player's camera is MainCamera. Player.prefab stays unchanged, so
    /// the bench scenes keep their cameras as they are.
    /// </summary>
    public class SkyIslandPlayerCamera : MonoBehaviour
    {
        [Tooltip("Camera of the generated Sky Island scene (Main Camera with SkyIslandFlyCamera): the settings source")]
        public Camera sceneCamera;

        Camera _applied;

        void Update()
        {
            Camera cam = PlayerNet.Local != null ? PlayerNet.Local.cam : null;
            if (sceneCamera == null || cam == null || !cam.enabled || cam == _applied) return;
            Apply(cam);
            _applied = cam;
        }

        void Apply(Camera cam)
        {
            cam.allowHDR = sceneCamera.allowHDR;
            cam.farClipPlane = sceneCamera.farClipPlane;
            UniversalAdditionalCameraData from = sceneCamera.GetUniversalAdditionalCameraData();
            UniversalAdditionalCameraData to = cam.GetUniversalAdditionalCameraData();
            to.renderPostProcessing = from.renderPostProcessing;
            to.antialiasing = from.antialiasing;
            to.antialiasingQuality = from.antialiasingQuality;
            to.dithering = from.dithering;

            var fogFrom = sceneCamera.GetComponent<SkyIslandFogByAltitude>();
            if (fogFrom != null)
            {
                var fog = cam.GetComponent<SkyIslandFogByAltitude>();
                if (fog == null) fog = cam.gameObject.AddComponent<SkyIslandFogByAltitude>();
                fog.target = cam;
                fog.groundDensity = fogFrom.groundDensity;
                fog.airDensity = fogFrom.airDensity;
                fog.island = fogFrom.island;
                fog.islandTopY = fogFrom.islandTopY;
                fog.heightRange = fogFrom.heightRange;
                fog.distanceRange = fogFrom.distanceRange;
                fog.applyInEditMode = fogFrom.applyInEditMode;
            }

            // PlayerNet already deactivates other MainCamera cameras on spawn; repeated here so the level does not
            // depend on that order
            sceneCamera.gameObject.SetActive(false);
            cam.tag = "MainCamera";
        }
    }
}
