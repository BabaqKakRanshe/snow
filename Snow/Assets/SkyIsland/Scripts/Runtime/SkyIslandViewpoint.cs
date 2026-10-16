using UnityEngine;

namespace SkyIsland
{
    /// <summary>
    /// A named camera position matching one of the preview renders (Assets/SkyIsland/Previews).
    /// The fly camera jumps here with keys 1-9; Tools > Sky Island > Capture Viewpoints renders all of them.
    /// </summary>
    [AddComponentMenu("Sky Island/Viewpoint")]
    public class SkyIslandViewpoint : MonoBehaviour
    {
        [Tooltip("Vertical field of view (deg) that frames the view like the preview at 16:9")] public float fieldOfView = 44f;
        [Tooltip("Preview image in Assets/SkyIsland/Previews rendered from this viewpoint")] public string previewImage;

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.75f, 0.3f, 0.9f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawFrustum(Vector3.zero, fieldOfView, 25f, 0.3f, 16f / 9f);
        }
    }
}
