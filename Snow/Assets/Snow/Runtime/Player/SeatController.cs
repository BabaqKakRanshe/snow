using Snow.Character;
using Snow.Input;
using Snow.Interaction;
using Snow.Net;
using Snow.Seats;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace Snow.Player
{
    /// <summary>
    /// Owner-side seat behaviour (§7.5, INPUT-01, INPUT-02): the character sits on the pelvis socket with the capsule off,
    /// the head turns within limits, Tab toggles the cursor (keyboard axes stay live), a short F uses what is looked at,
    /// holding F for exitHoldTime asks the server to get out. Only the pilot seat sends axes, once per network tick.
    /// </summary>
    public class SeatController : MonoBehaviour
    {
        public PlayerNet net;
        public PlayerController controller;
        public HLCharacterMotor motor;
        public FirstPersonView view;
        public InteractionSensor sensor;
        [Tooltip("s, TEMPORARY (§7.5)")] public float exitHoldTime = 0.4f;
        [Header("Keyboard axes (PHYS-02.7: input smoothing is allowed), TEMPORARY")]
        [Tooltip("per second a held key moves its axis towards full: a tap gives a small deflection")] public float axisRise = 2.5f;
        [Tooltip("per second a released axis returns to centre")] public float axisReturn = 5f;

        float _pitch, _roll, _yaw;

        SeatAnchor _seat;
        float _hold;
        bool _exitSent, _cursorWas, _heldF;
        NetworkTransform _nt;

        public SeatAnchor Seat => _seat;
        public bool IsPilot => _seat != null && _seat.role == SeatRole.Pilot;

        void Awake() => _nt = GetComponent<NetworkTransform>();

        void OnEnable()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.NetworkTickSystem != null)
                NetworkManager.Singleton.NetworkTickSystem.Tick += OnTick;
        }

        void OnDisable()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.NetworkTickSystem != null)
                NetworkManager.Singleton.NetworkTickSystem.Tick -= OnTick;
        }

        public void Enter(SeatAnchor seat)
        {
            _seat = seat;
            _hold = 0f;
            _pitch = _roll = _yaw = 0f;
            _exitSent = false;
            _heldF = true;                                         // ignore the F/E still held from getting in
            motor.enabled = false;
            motor.Controller.enabled = false;                      // no collisions while seated
            transform.SetPositionAndRotation(seat.pelvis.position, seat.pelvis.rotation);
            view.EnterSeat(seat.eye);
            controller.Source.Context = InputContext.Seat;
            sensor.ShowMessage("Удерживайте F — выйти.   Tab — курсор (тумблеры: ЛКМ).   H — подсказка", 6f);
            BenchLog.Event("seated", "seat", seat.seatId, "role", seat.role.ToString());
        }

        public void Leave(Vector3 position, float yaw, Vector3 velocity)
        {
            _seat = null;
            motor.Controller.enabled = true;
            motor.enabled = true;
            motor.Teleport(position, yaw, velocity);
            if (_nt != null && _nt.CanCommitToTransform) _nt.Teleport(position, Quaternion.Euler(0f, yaw, 0f), transform.localScale);
            view.LeaveSeat(yaw);
            controller.Source.Context = InputContext.OnFoot;
            BenchLog.Event("left_seat", "p", position, "v", velocity);
        }

        void Update()
        {
            if (_seat == null) return;
            // stay on the pelvis socket; as a child of the vehicle this follows it
            transform.SetPositionAndRotation(_seat.pelvis.position, _seat.pelvis.rotation);

            PlayerInputFrame f = controller.Frame;
            PlayerInputFrame last = controller.PreviousFrame;
            _pitch = Ramp(_pitch, f.Pitch);
            _roll = Ramp(_roll, f.Roll);
            _yaw = Ramp(_yaw, f.Yaw);
            if (f.Autopilot && !last.Autopilot)
            {
                // the plane: autopilot; the van: hazard lights (VAN3 §4.4)
                if (IsPilot) net.RequestPilotCommand(PilotCommand.AutopilotToggle);
                else sensor.ShowMessage("Только с места пилота / водителя");
            }
            if (f.Horn != last.Horn && IsPilot) net.RequestPilotCommand(f.Horn ? PilotCommand.HornOn : PilotCommand.HornOff);
            if (f.CursorToggle && !last.CursorToggle)
            {
                var src = controller.Source;
                src.Context = src.Context == InputContext.SeatCursor ? InputContext.Seat : InputContext.SeatCursor;
            }

            if (f.UseExit)
            {
                if (_heldF) return;                                 // still the press that got us in
                _hold += Time.deltaTime;
                if (_hold >= exitHoldTime && !_exitSent)
                {
                    _exitSent = true;
                    net.RequestExit();
                }
            }
            else
            {
                if (!_heldF && _hold > 0f && _hold < exitHoldTime) sensor.Tap();   // a short press is "use"
                _heldF = false;
                _hold = 0f;
                _exitSent = false;
            }
        }

        // keyboard keys are 0 or ±1: the axis moves towards the key at a rate, so taps give small corrections
        float Ramp(float current, float key)
        {
            bool towards = Mathf.Abs(key) > 0.01f && (Mathf.Abs(current) < 0.001f || Mathf.Sign(key) == Mathf.Sign(current));
            float rate = towards ? axisRise : axisReturn;
            return Mathf.MoveTowards(current, key, rate * Time.deltaTime);
        }

        // pilot axes once per network tick, unreliable; the passenger seat never sends (SEAT-01)
        void OnTick()
        {
            if (!IsPilot || controller.Source == null) return;
            PlayerInputFrame f = controller.Frame;
            net.SendAxes(new ControlInput { Pitch = _pitch, Roll = _roll, Yaw = _yaw, ThrottleRate = f.Throttle, Brake = f.Brake, TrimRate = f.Trim });
        }
    }
}
