using UnityEngine;
using UnityEngine.InputSystem;

namespace Snow.Input
{
    /// <summary>
    /// Keyboard and mouse through the Input System. OnFoot, Seat and Cursor are separate action maps (INPUT-02):
    /// switching the context disables the other maps, which resets their held state, so nothing sticks after a seat
    /// change or focus loss. Bindings are TEMPORARY (O-01) and live in Assets/Snow/Input/SnowControls.inputactions.
    /// </summary>
    public class SnowInput : MonoBehaviour, IPlayerInputSource
    {
        [SerializeField] InputActionAsset actions;

        InputActionAsset _asset;
        InputActionMap _onFoot, _seat, _cursor;
        InputAction _move, _footLook, _jump, _crouch, _sprint, _walk, _use, _secure, _footZoom, _seatZoom, _footReveal, _seatReveal, _ragdoll;
        InputAction _pitch, _roll, _yaw, _throttle, _seatLook, _useExit, _cursorToggle, _brake, _trim, _autopilot, _horn;
        InputAction _point, _click;
        InputContext _context = InputContext.None;
        bool _focused = true;
        static bool s_uiOpen;

        /// <summary>The context as the game sees it: None while a developer window holds the input (<see cref="UiOpen"/>),
        /// so the interaction sensor, pockets and voice keys stay quiet too.</summary>
        public InputContext Context
        {
            get => s_uiOpen ? InputContext.None : _context;
            set { _context = value; Apply(); }
        }

        /// <summary>A developer window (the pult, ~) holds mouse and keyboard: every action map off, the cursor free.
        /// Closing it gives the player back the context he had.</summary>
        public static bool UiOpen
        {
            get => s_uiOpen;
            set
            {
                if (s_uiOpen == value) return;
                s_uiOpen = value;
                foreach (SnowInput i in FindObjectsByType<SnowInput>(FindObjectsSortMode.None)) i.Apply();
                if (value)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
            }
        }

        void Awake()
        {
            // a private copy: the asset in the project keeps no runtime state between play sessions
            _asset = Instantiate(actions);
            _onFoot = _asset.FindActionMap("OnFoot", true);
            _seat = _asset.FindActionMap("Seat", true);
            _cursor = _asset.FindActionMap("Cursor", true);
            _move = _onFoot.FindAction("Move", true);
            _footLook = _onFoot.FindAction("Look", true);
            _jump = _onFoot.FindAction("Jump", true);
            _crouch = _onFoot.FindAction("Crouch", true);
            _sprint = _onFoot.FindAction("Sprint", true);
            _walk = _onFoot.FindAction("Walk", true);
            _use = _onFoot.FindAction("Use", true);
            _secure = _onFoot.FindAction("Secure", true);
            _footZoom = _onFoot.FindAction("Zoom", true);
            _seatZoom = _seat.FindAction("Zoom", true);
            _footReveal = _onFoot.FindAction("Reveal", true);
            _seatReveal = _seat.FindAction("Reveal", true);
            _ragdoll = _onFoot.FindAction("Ragdoll", true);
            _pitch = _seat.FindAction("Pitch", true);
            _roll = _seat.FindAction("Roll", true);
            _yaw = _seat.FindAction("Yaw", true);
            _throttle = _seat.FindAction("Throttle", true);
            _seatLook = _seat.FindAction("Look", true);
            _useExit = _seat.FindAction("UseExit", true);
            _cursorToggle = _seat.FindAction("CursorToggle", true);
            _brake = _seat.FindAction("Brake", true);
            _trim = _seat.FindAction("Trim", true);
            _autopilot = _seat.FindAction("Autopilot", true);
            _horn = _seat.FindAction("Horn", true);
            _point = _cursor.FindAction("Point", true);
            _click = _cursor.FindAction("Click", true);
        }

        void OnDestroy()
        {
            if (_asset != null) Destroy(_asset);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void OnApplicationFocus(bool focus)
        {
            _focused = focus;
            Apply();
        }

        void Apply()
        {
            if (_asset == null) return;
            bool live = _focused && !s_uiOpen;
            bool foot = live && _context == InputContext.OnFoot;
            bool seat = live && (_context == InputContext.Seat || _context == InputContext.SeatCursor);
            bool cursor = live && _context == InputContext.SeatCursor;
            Set(_onFoot, foot);
            Set(_seat, seat);
            Set(_cursor, cursor);
            if (_context == InputContext.None) return;
            Cursor.lockState = cursor || !live ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = cursor || !live;
        }

        static void Set(InputActionMap map, bool on)
        {
            if (on && !map.enabled) map.Enable();
            else if (!on && map.enabled) map.Disable();
        }

        /// <summary>Display name of the key bound to an action, e.g. "Seat/Reveal" → "B". Hints use it, so a rebind in the
        /// actions asset shows up in the game without code changes.</summary>
        public string KeyName(string actionPath)
        {
            InputAction a = (_asset != null ? _asset : actions)?.FindAction(actionPath);
            return a != null ? a.GetBindingDisplayString() : "?";
        }

        public PlayerInputFrame Read()
        {
            var f = new PlayerInputFrame();
            if (_asset == null) return f;
            if (_onFoot.enabled)
            {
                f.Move = _move.ReadValue<Vector2>();
                f.LookDelta = _footLook.ReadValue<Vector2>();
                f.Jump = _jump.IsPressed();
                f.Crouch = _crouch.IsPressed();
                f.Sprint = _sprint.IsPressed();
                f.Walk = _walk.IsPressed();
                f.Use = _use.IsPressed();
                f.Secure = _secure.IsPressed();
                f.Zoom = _footZoom.IsPressed();
                f.Reveal = _footReveal.IsPressed();
                f.Ragdoll = _ragdoll.IsPressed();
            }
            if (_seat.enabled)
            {
                f.Pitch = _pitch.ReadValue<float>();
                f.Roll = _roll.ReadValue<float>();
                f.Yaw = _yaw.ReadValue<float>();
                f.Throttle = _throttle.ReadValue<float>();
                f.Brake = _brake.IsPressed() ? 1f : 0f;
                f.Trim = _trim.ReadValue<float>();
                f.UseExit = _useExit.IsPressed();
                f.Zoom = _seatZoom.IsPressed();
                f.Reveal = _seatReveal.IsPressed();
                f.CursorToggle = _cursorToggle.IsPressed();
                f.Autopilot = _autopilot.IsPressed();
                f.Horn = _horn.IsPressed();
                // the cursor takes the mouse: no head look while it is on (INPUT-02)
                if (!_cursor.enabled) f.LookDelta = _seatLook.ReadValue<Vector2>();
            }
            if (_cursor.enabled)
            {
                f.Pointer = _point.ReadValue<Vector2>();
                f.Click = _click.IsPressed();
            }
            return f;
        }
    }
}
