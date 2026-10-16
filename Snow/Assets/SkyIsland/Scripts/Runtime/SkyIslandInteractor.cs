using UnityEngine;
#if ENABLE_INPUT_SYSTEM && SKYISLAND_INPUTSYSTEM
using UnityEngine.InputSystem;
#endif

namespace SkyIsland
{
    /// <summary>
    /// Test interaction for the fly camera: F or left click toggles the openable part under the screen centre
    /// (SkyIslandInteractable on the hit collider or its parents) within <see cref="reach"/> metres.
    /// </summary>
    [AddComponentMenu("Sky Island/Interactor (test)")]
    public class SkyIslandInteractor : MonoBehaviour
    {
        public float reach = 3.5f;
        public LayerMask mask = ~0;

        void Update()
        {
            bool press = false;
#if ENABLE_INPUT_SYSTEM && SKYISLAND_INPUTSYSTEM
            var kb = Keyboard.current; var mouse = Mouse.current;
            if (kb != null && kb.fKey.wasPressedThisFrame) press = true;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) press = true;
#elif ENABLE_LEGACY_INPUT_MANAGER
            press = Input.GetKeyDown(KeyCode.F) || Input.GetMouseButtonDown(0);
#endif
            if (!press) return;
            var cam = GetComponent<Camera>();
            Ray ray = cam != null ? cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)) : new Ray(transform.position, transform.forward);
            if (!Physics.Raycast(ray, out var hit, reach, mask, QueryTriggerInteraction.Ignore)) return;
            var it = hit.collider.GetComponentInParent<SkyIslandInteractable>();
            if (it != null) it.Toggle();
        }
    }
}
