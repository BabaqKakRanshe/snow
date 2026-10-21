using SkyIsland;
using Snow.Player;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Snow.Levels
{
    /// <summary>
    /// Levels built by the Sky Island core (Sky Island, BaseV2): gives the local player's camera everything the location's
    /// look depends on, copied from the level camera the core builder made (HDR, post-processing, SMAA, dithering, far
    /// clip, fog controller, the PS1 frame), and switches that debug fly camera off so only the player's camera is
    /// MainCamera. Player.prefab stays unchanged, so the bench scenes keep their cameras as they are.
    /// </summary>
    public class SkyIslandPlayerCamera : MonoBehaviour
    {
        [Tooltip("Camera of the generated location scene (Main Camera with SkyIslandFlyCamera): the settings source")]
        public Camera sceneCamera;
        [Tooltip("Copy the scene camera's SkyIslandPSX (240 lines, 15-bit colour, dither) to the player's camera. " +
                 "User decision 16.10: the PS1 frame everywhere, the cockpit included")]
        public bool copyPsxFrame = true;

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

            if (sceneCamera.TryGetComponent(out SkyIslandFogByAltitude fogFrom))
            {
                if (!cam.TryGetComponent(out SkyIslandFogByAltitude fog)) fog = cam.gameObject.AddComponent<SkyIslandFogByAltitude>();
                // every serialized field, so later core fields (linear fog, thin below...) come along without a code change
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(fogFrom), fog);
                fog.target = cam;
                // the core's fog events (Forest FogUnderHand, FogRibs) scale the controller they found first: the scene
                // camera's, switched off below. Pointed at the player's, they thicken the fog he sees; otherwise his
                // controller rewrites the fog every frame and the event has no effect (LOCATIONS QL11)
                foreach (SkyIslandFogEvent e in FindObjectsByType<SkyIslandFogEvent>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (e.fogController == null || e.fogController == fogFrom) e.fogController = fog;
            }

            if (copyPsxFrame && sceneCamera.TryGetComponent(out SkyIslandPSX psxFrom) && !cam.TryGetComponent(out SkyIslandPSX _))
            {
                // OnEnable reads canvasOrder and presentShader and builds the screen: added to an active camera it runs at
                // once, so switch it off, copy the settings, then on again
                var psx = cam.gameObject.AddComponent<SkyIslandPSX>();
                psx.enabled = false;
                psx.height = psxFrom.height;
                psx.colorBits = psxFrom.colorBits;
                psx.dither = psxFrom.dither;
                psx.presentShader = psxFrom.presentShader;
                psx.canvasOrder = psxFrom.canvasOrder;
                psx.enabled = true;
            }

            // PlayerNet already deactivates other MainCamera cameras on spawn; repeated here so the level does not
            // depend on that order (the scene camera's SkyIslandPSX removes its screen in OnDisable)
            sceneCamera.gameObject.SetActive(false);
            cam.tag = "MainCamera";
        }
    }
}
