using Snow.Doors;
using Snow.Interaction;
using Snow.Seats;
using UnityEngine;

namespace Snow.Bench
{
    /// <summary>
    /// P01 bench cabin made of primitives (§14 P01): one door with handles inside and outside, one door button outside and
    /// one on the panel (NEW-04), two seats (left = pilot), a yoke that shows the server's control state.
    /// Bus power is TEMPORARY always on: the bench has no electrics yet.
    /// </summary>
    public class BenchCabin : SeatedVehicle
    {
        public const string ButtonOut = "Button_Door_Out";
        public const string ButtonIn = "Button_Door_In";
        public const string HandleOut = "Door_Handle_Out";
        public const string HandleIn = "Door_Handle_In";

        public NetworkDoor door;
        public Transform yoke;

        Vector3 _yokeRest;
        Quaternion _yokeRestRot;

        protected override void Awake()
        {
            base.Awake();
            door.PowerSource = () => true;
            if (yoke != null)
            {
                _yokeRest = yoke.localPosition;
                _yokeRestRot = yoke.localRotation;
            }
        }

        public override bool ServerUse(ulong clientId, string id, UsePhase phase, out string reason)
        {
            reason = null;
            if (phase != UsePhase.Press) return true;              // nothing momentary on the bench
            switch (id)
            {
                case ButtonOut:
                case ButtonIn:
                    door.Toggle(DoorDrive.Powered);
                    return true;
                case HandleOut:
                case HandleIn:
                    door.Toggle(DoorDrive.Manual);
                    return true;
            }
            int seat = SeatIndex(id);
            if (seat >= 0) return ServerSit(clientId, seat, out reason);
            reason = "Нет действия";
            return false;
        }

        public override string Describe(string id)
        {
            switch (id)
            {
                case ButtonOut:
                case ButtonIn:
                case HandleOut:
                case HandleIn:
                    return DoorText(door.State);
            }
            int seat = SeatIndex(id);
            return seat >= 0 ? SeatText(seat) : null;
        }

        public override string ActionText(string id)
        {
            if (SeatIndex(id) >= 0) return "сесть";
            bool opening = door.State == DoorState.Open || door.State == DoorState.Opening;
            return opening ? "закрыть" : "открыть";
        }

        static string DoorText(DoorState s)
        {
            switch (s)
            {
                case DoorState.Closed: return "закрыта";
                case DoorState.Opening: return "открывается";
                case DoorState.Open: return "открыта";
                case DoorState.Closing: return "закрывается";
                default: return "упёрлась";
            }
        }

        void Update()
        {
            if (yoke == null) return;
            ControlState c = Controls;
            yoke.localPosition = _yokeRest + Vector3.forward * (0.08f * c.Pitch);
            yoke.localRotation = _yokeRestRot * Quaternion.Euler(0f, 0f, -50f * c.Roll);
        }
    }
}
