using System;

namespace Snow.Doors
{
    public enum DoorState : byte
    {
        Closed,
        Opening,
        Open,
        Closing,
        Blocked,
    }

    /// <summary>Who drives the door: the electric actuator (needs bus power) or a hand on the handle.</summary>
    public enum DoorDrive : byte
    {
        Powered,
        Manual,
    }

    /// <summary>
    /// Server-side door automaton (DOOR-01, §8.2): Closed → Opening → Open → Closing → Closed; Opening/Closing →
    /// Blocked when the next step would hit an obstacle; a new command leaves Blocked. Position is 0 (closed) … 1 (open).
    /// Pure C#: the obstacle test is a callback, so the door never moves through a crate (§8.2) and the model is testable.
    /// </summary>
    public class DoorModel
    {
        /// <summary>Seconds for a full stroke on the actuator and by hand. TEMPORARY (AircraftTuning later).</summary>
        public float PoweredTravelTime, ManualTravelTime;

        public DoorState State { get; private set; } = DoorState.Closed;
        public float Position { get; private set; }
        public bool TargetOpen { get; private set; }
        public DoorDrive Drive { get; private set; }
        /// <summary>The actuator is running (it draws current, §9.5).</summary>
        public bool ActuatorRunning { get; private set; }

        public DoorModel(float poweredTravelTime, float manualTravelTime)
        {
            PoweredTravelTime = poweredTravelTime;
            ManualTravelTime = manualTravelTime;
        }

        /// <summary>Restores a state (checkpoint load, late join). Moving states restart from the given position.</summary>
        public void Restore(DoorState state, float position)
        {
            Position = Clamp01(position);
            State = state;
            TargetOpen = state == DoorState.Open || state == DoorState.Opening;
        }

        /// <summary>Request open (true) or close (false). Repeating the current request changes nothing.</summary>
        public void Command(bool open, DoorDrive drive)
        {
            Drive = drive;
            TargetOpen = open;
            if (open && Position >= 1f) { State = DoorState.Open; return; }
            if (!open && Position <= 0f) { State = DoorState.Closed; return; }
            State = open ? DoorState.Opening : DoorState.Closing;
        }

        public void Toggle(DoorDrive drive) =>
            Command(!(State == DoorState.Open || State == DoorState.Opening || (State == DoorState.Blocked && TargetOpen)), drive);

        /// <summary>
        /// Advance by dt. powered: bus power for the actuator. isFree(position): the door panel at that position does
        /// not intersect anything. Returns true when the position changed.
        /// </summary>
        public bool Tick(float dt, bool powered, Func<float, bool> isFree)
        {
            ActuatorRunning = false;
            if (State != DoorState.Opening && State != DoorState.Closing) return false;
            if (Drive == DoorDrive.Powered && !powered) return false;   // the actuator stops without power
            float time = Drive == DoorDrive.Powered ? PoweredTravelTime : ManualTravelTime;
            float step = time > 0f ? dt / time : 1f;
            float next = Clamp01(Position + (State == DoorState.Opening ? step : -step));
            if (isFree != null && !isFree(next))
            {
                State = DoorState.Blocked;
                return false;
            }
            ActuatorRunning = Drive == DoorDrive.Powered;
            Position = next;
            if (State == DoorState.Opening && Position >= 1f) State = DoorState.Open;
            else if (State == DoorState.Closing && Position <= 0f) State = DoorState.Closed;
            return true;
        }

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
