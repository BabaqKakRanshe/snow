using UnityEngine;
#if ENABLE_INPUT_SYSTEM && SKYISLAND_INPUTSYSTEM
using UnityEngine.InputSystem;
#endif

namespace SkyIsland
{
    /// <summary>
    /// Simple free-fly camera for looking around the location.
    /// Hold the right mouse button to look, WASD to move, Q/E down/up, Shift = fast, mouse wheel = speed,
    /// keys 1-9 = jump to the preview viewpoints (SkyIslandViewpoint), 0 = back to the default field of view.
    /// Works with the new Input System and with the legacy Input Manager.
    /// </summary>
    [AddComponentMenu("Sky Island/Fly Camera")]
    public class SkyIslandFlyCamera : MonoBehaviour
    {
        public float moveSpeed = 12f;
        public float fastMultiplier = 6f;
        public float lookSensitivity = 0.12f;
        public bool requireRightMouse = true;

        float _yaw, _pitch, _defaultFov = -1f;
        SkyIslandViewpoint[] _views;

        void JumpTo(int index)
        {
            if (_views == null || _views.Length == 0)
            {
#if UNITY_2023_1_OR_NEWER
                _views = FindObjectsByType<SkyIslandViewpoint>(FindObjectsSortMode.None);
#else
                _views = FindObjectsOfType<SkyIslandViewpoint>();
#endif
                System.Array.Sort(_views, (a, b) => a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));
            }
            var cam = GetComponent<Camera>();
            if (index < 0)
            {
                if (cam != null && _defaultFov > 0f) cam.fieldOfView = _defaultFov;
                return;
            }
            if (index >= _views.Length || _views[index] == null) return;
            var v = _views[index];
            transform.SetPositionAndRotation(v.transform.position, v.transform.rotation);
            if (cam != null)
            {
                if (_defaultFov < 0f) _defaultFov = cam.fieldOfView;
                cam.fieldOfView = v.fieldOfView;
            }
            OnEnable();
        }

        void OnEnable()
        {
            var e = transform.eulerAngles;
            _yaw = e.y;
            _pitch = e.x > 180f ? e.x - 360f : e.x;
        }

        void Update()
        {
            Vector2 look = Vector2.zero;
            Vector3 move = Vector3.zero;
            bool fast = false, lookHeld = !requireRightMouse;
            float scroll = 0f;

#if ENABLE_INPUT_SYSTEM && SKYISLAND_INPUTSYSTEM
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (mouse != null)
            {
                if (requireRightMouse) lookHeld = mouse.rightButton.isPressed;
                look = mouse.delta.ReadValue();
                float sy = mouse.scroll.ReadValue().y;
                scroll = Mathf.Abs(sy) > 0.01f ? Mathf.Sign(sy) : 0f;
            }
            if (kb != null)
            {
                if (kb.wKey.isPressed) move.z += 1f;
                if (kb.sKey.isPressed) move.z -= 1f;
                if (kb.dKey.isPressed) move.x += 1f;
                if (kb.aKey.isPressed) move.x -= 1f;
                if (kb.eKey.isPressed) move.y += 1f;
                if (kb.qKey.isPressed) move.y -= 1f;
                fast = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                var digits = new[] { kb.digit1Key, kb.digit2Key, kb.digit3Key, kb.digit4Key, kb.digit5Key, kb.digit6Key, kb.digit7Key, kb.digit8Key, kb.digit9Key };
                for (int i = 0; i < digits.Length; i++)
                    if (digits[i].wasPressedThisFrame) JumpTo(i);
                if (kb.digit0Key.wasPressedThisFrame) JumpTo(-1);
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (requireRightMouse) lookHeld = Input.GetMouseButton(1);
            look = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 10f;
            scroll = Input.mouseScrollDelta.y;
            if (Input.GetKey(KeyCode.W)) move.z += 1f;
            if (Input.GetKey(KeyCode.S)) move.z -= 1f;
            if (Input.GetKey(KeyCode.D)) move.x += 1f;
            if (Input.GetKey(KeyCode.A)) move.x -= 1f;
            if (Input.GetKey(KeyCode.E)) move.y += 1f;
            if (Input.GetKey(KeyCode.Q)) move.y -= 1f;
            fast = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            for (int i = 0; i < 9; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) JumpTo(i);
            if (Input.GetKeyDown(KeyCode.Alpha0)) JumpTo(-1);
#endif
            if (Mathf.Abs(scroll) > 0.01f)
                moveSpeed = Mathf.Clamp(moveSpeed * (1f + 0.15f * scroll), 0.5f, 400f);

            if (lookHeld)
            {
                _yaw += look.x * lookSensitivity;
                _pitch = Mathf.Clamp(_pitch - look.y * lookSensitivity, -89f, 89f);
                transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }

            float speed = moveSpeed * (fast ? fastMultiplier : 1f);
            transform.position += transform.TransformDirection(move) * speed * Time.unscaledDeltaTime;
        }
    }
}
