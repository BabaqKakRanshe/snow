using NUnit.Framework;
using Snow.Doors;
using Snow.Interaction;
using Snow.Seats;

namespace Snow.Tests
{
    /// <summary>DOOR-01 §8.2 automaton, NET-02 command de-duplication, SEAT-01/02 seat arbitration.</summary>
    public class ServerModelTests
    {
        static int Run(DoorModel d, float seconds, bool powered = true, System.Func<float, bool> free = null, float dt = 0.02f)
        {
            int steps = (int)System.Math.Round(seconds / dt);      // integer step count: no float drift
            for (int i = 0; i < steps; i++) d.Tick(dt, powered, free ?? (_ => true));
            return steps;
        }

        [Test]
        public void Door_OpensInTravelTime_ThenCloses()
        {
            var d = new DoorModel(2.5f, 1.0f);
            d.Command(true, DoorDrive.Powered);
            Assert.That(d.State, Is.EqualTo(DoorState.Opening));
            Run(d, 2.48f);
            Assert.That(d.State, Is.EqualTo(DoorState.Opening), "not yet open just before the travel time");
            Run(d, 0.04f);
            Assert.That(d.State, Is.EqualTo(DoorState.Open));
            Assert.That(d.Position, Is.EqualTo(1f));
            d.Command(false, DoorDrive.Powered);
            Run(d, 2.6f);
            Assert.That(d.State, Is.EqualTo(DoorState.Closed));
            Assert.That(d.Position, Is.EqualTo(0f));
        }

        [Test]
        public void Door_ActuatorStopsWithoutPower_HandleStillWorks()
        {
            var d = new DoorModel(2.5f, 1.0f);
            d.Command(true, DoorDrive.Powered);
            Run(d, 1f, powered: false);
            Assert.That(d.Position, Is.EqualTo(0f), "no bus power: the actuator does not move");
            Assert.That(d.ActuatorRunning, Is.False);
            d.Command(true, DoorDrive.Manual);
            Run(d, 1.02f, powered: false);
            Assert.That(d.State, Is.EqualTo(DoorState.Open), "by hand in the manual travel time");
        }

        [Test]
        public void Door_BlocksOnObstacle_DoesNotPassIt_AndReverses()
        {
            var d = new DoorModel(2.5f, 1.0f);
            d.Restore(DoorState.Open, 1f);
            d.Command(false, DoorDrive.Powered);
            Run(d, 5f, free: p => p > 0.4f);                     // a crate in the opening from 0.4 down
            Assert.That(d.State, Is.EqualTo(DoorState.Blocked));
            Assert.That(d.Position, Is.GreaterThan(0.4f), "never enters the obstacle");
            float stuck = d.Position;
            Run(d, 1f, free: p => p > 0.4f);
            Assert.That(d.Position, Is.EqualTo(stuck), "stays blocked, no retry by itself");
            d.Toggle(DoorDrive.Powered);                          // a new command: the other way
            Assert.That(d.State, Is.EqualTo(DoorState.Opening));
            Run(d, 2.5f, free: p => p > 0.4f);
            Assert.That(d.State, Is.EqualTo(DoorState.Open));
        }

        [Test]
        public void Door_RepeatedCommandIsIdempotent()
        {
            var d = new DoorModel(2.5f, 1.0f);
            d.Command(true, DoorDrive.Powered);
            Run(d, 1f);
            float p = d.Position;
            d.Command(true, DoorDrive.Powered);
            Assert.That(d.State, Is.EqualTo(DoorState.Opening));
            Assert.That(d.Position, Is.EqualTo(p));
            d.Restore(DoorState.Open, 1f);
            d.Command(true, DoorDrive.Powered);
            Assert.That(d.State, Is.EqualTo(DoorState.Open));
        }

        [Test]
        public void Dedup_RejectsRepeatsAndOld_PerSender_WrapSafe()
        {
            var dd = new CommandDeduplicator();
            Assert.That(dd.Accept(1, 5), Is.True);
            Assert.That(dd.Accept(1, 5), Is.False, "resent");
            Assert.That(dd.Accept(1, 4), Is.False, "older");
            Assert.That(dd.Accept(2, 5), Is.True, "another sender");
            Assert.That(dd.Accept(1, 6), Is.True);
            Assert.That(dd.Accept(3, uint.MaxValue), Is.True);
            Assert.That(dd.Accept(3, 0), Is.True, "wrap-around");
            dd.Forget(1);
            Assert.That(dd.Accept(1, 1), Is.True, "after disconnect");
        }

        [Test]
        public void Seats_OneSeatPerPlayer_OnePlayerPerSeat_PilotOnlyLeft()
        {
            var s = new SeatRegistry(SeatRole.Pilot, SeatRole.Passenger);
            Assert.That(s.TryOccupy(0, 10, out _), Is.True);
            Assert.That(s.TryOccupy(0, 11, out string r1), Is.False, "AT-02: the second request for the left seat loses");
            Assert.That(r1, Is.Not.Null);
            Assert.That(s.TryOccupy(1, 10, out _), Is.False, "already seated");
            Assert.That(s.TryOccupy(1, 11, out _), Is.True);
            Assert.That(s.IsPilot(10), Is.True);
            Assert.That(s.IsPilot(11), Is.False, "right seat has no flight authority");
            Assert.That(s.Release(10), Is.EqualTo(0));
            Assert.That(s.IsPilot(10), Is.False);
            Assert.That(s.TryOccupy(0, 11, out _), Is.False, "must leave the right seat first (no taking over without moving)");
            s.Release(11);
            Assert.That(s.TryOccupy(0, 11, out _), Is.True);
        }
    }
}
