using Snow.Character;
using Snow.Input;
using UnityEngine;

namespace Snow.Player
{
    /// <summary>
    /// Local player glue: reads one input frame per rendered frame and feeds the view and the motor.
    /// Works offline (tests, sandbox) and under the network layer, which enables it only for the owner.
    /// </summary>
    public class PlayerController : MonoBehaviour
    {
        public HLCharacterMotor motor;
        public FirstPersonView view;

        IPlayerInputSource _source;
        PlayerInputFrame _last;

        public IPlayerInputSource Source => _source;
        public PlayerInputFrame Frame { get; private set; }
        public PlayerInputFrame PreviousFrame => _last;

        public void SetSource(IPlayerInputSource source, InputContext context)
        {
            _source = source;
            if (_source != null) _source.Context = context;
        }

        void Awake()
        {
            if (_source == null)
            {
                var human = GetComponent<SnowInput>();
                if (human != null) SetSource(human, InputContext.OnFoot);
            }
        }

        void Update()
        {
            if (_source == null) return;
            _last = Frame;
            Frame = _source.Read();
            PlayerInputFrame f = Frame;

            if (view != null)
            {
                view.ZoomHeld = f.Zoom;
                view.SprintHeld = f.Sprint;
                view.PanelMode = _source.Context == InputContext.SeatCursor;
                view.AddLook(f.LookDelta);
            }
            if (motor != null && motor.enabled)
            {
                if (f.Jump && !_last.Jump) motor.QueueJump();
                motor.Input = new MoveInput
                {
                    Move = f.Move,
                    Jump = f.Jump,
                    Crouch = f.Crouch,
                    Sprint = f.Sprint,
                    Walk = f.Walk,
                    YawDeg = view != null ? view.Yaw : motor.Input.YawDeg,
                };
            }
        }
    }
}
