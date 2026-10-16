using UnityEngine;

namespace Snow.Input
{
    /// <summary>Which action maps are live. Cursor is only available in a seat (INPUT-02).</summary>
    public enum InputContext
    {
        None,
        OnFoot,
        Seat,
        SeatCursor,
    }

    /// <summary>
    /// Held state of every player control for one frame. Press edges are derived by the consumer from the previous
    /// frame, so the same frame can come from the keyboard or from a scripted bot.
    /// </summary>
    public struct PlayerInputFrame
    {
        // on foot
        public Vector2 Move;
        public bool Jump, Crouch, Sprint, Walk, Use;
        /// <summary>Secure / release the looked-at or held cargo item on a tie-down (SECURE-01). TEMPORARY key R.</summary>
        public bool Secure;
        // look (mouse delta, pixels this frame), on foot and in a seat without cursor
        public Vector2 LookDelta;
        /// <summary>Held: narrow the field of view to look closer (TEMPORARY right mouse button).</summary>
        public bool Zoom;
        /// <summary>Held: show every interactive thing around (TEMPORARY B: Alt is taken by the Unity editor; rebind in
        /// Assets/Snow/Input/SnowControls.inputactions, see Docs/Controls.md).</summary>
        public bool Reveal;
        // seat: flight axes -1..1, + = push / right roll / right pedal / more throttle
        public float Pitch, Roll, Yaw, Throttle;
        /// <summary>Toe brakes 0..1 (TEMPORARY Space) and trim wheel rate -1..1, + = nose up (TEMPORARY [ and ]).</summary>
        public float Brake, Trim;
        public bool UseExit, CursorToggle;
        /// <summary>Autopilot on/off (TEMPORARY Z; the model has no autopilot switch of its own).</summary>
        public bool Autopilot;
        // cursor
        public Vector2 Pointer;
        public bool Click;
    }

    public interface IPlayerInputSource
    {
        InputContext Context { get; set; }
        PlayerInputFrame Read();
    }
}
