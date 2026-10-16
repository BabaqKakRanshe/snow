using NUnit.Framework;
using Snow.Aircraft;
using UnityEngine;

namespace Snow.Tests
{
    /// <summary>
    /// Pure models of the Utility Plane (CORE-02, §15 "чистые модели — EditMode"): aerodynamic section (PHYS-02.4 stall
    /// from the angle of attack, hysteresis, flaps), engine start chain (§9.4, AT-F1), electrics (§9.5, AT-15), fuel
    /// (§9.6), tyre contact (parked in wind holds, pushed harder it slides).
    /// </summary>
    public class AircraftModelTests
    {
        static AircraftTuning Tuning() => ScriptableObject.CreateInstance<AircraftTuning>();

        static float SteadyCl(AeroSection s, float alphaDeg, float flapDeg = 0f)
        {
            for (int i = 0; i < 200; i++) s.Evaluate(alphaDeg * Mathf.Deg2Rad, flapDeg * Mathf.Deg2Rad, 1f, 0.05f);
            return s.LastCl;
        }

        static AeroSection Wing(AircraftTuning t) =>
            new AeroSection("W", t.wingInner.Clone(), Vector3.zero, Vector3.up, Vector3.forward);

        [Test]
        public void Aero_LinearRange_FollowsLiftSlope()
        {
            var t = Tuning();
            var s = Wing(t);
            Assert.That(SteadyCl(s, 0f), Is.EqualTo(t.wingInner.cl0).Within(1e-3f));
            float expected = t.wingInner.cl0 + t.wingInner.liftSlope * 5f * Mathf.Deg2Rad;
            Assert.That(SteadyCl(s, 5f), Is.EqualTo(expected).Within(1e-3f));
        }

        [Test]
        public void Aero_Stall_FromAngleOfAttack_LiftFallsPastIt()
        {
            var t = Tuning();
            var s = Wing(t);
            float stallDeg = (t.wingInner.clMax - t.wingInner.cl0) / t.wingInner.liftSlope * Mathf.Rad2Deg;
            float atStall = SteadyCl(s, stallDeg);
            Assert.That(atStall, Is.EqualTo(t.wingInner.clMax).Within(0.02f), "CLmax at the stall angle");
            float deep = SteadyCl(s, stallDeg + 15f);
            Assert.That(deep, Is.LessThan(atStall * 0.8f), "separated flow carries less lift");
            Assert.That(s.Attached, Is.LessThan(0.05f));
            Assert.That(s.LastCd, Is.GreaterThan(0.3f), "and a lot more drag");
        }

        [Test]
        public void Aero_Hysteresis_StaysSeparatedJustBelowStall()
        {
            var t = Tuning();
            var s = Wing(t);
            float stallDeg = (t.wingInner.clMax - t.wingInner.cl0) / t.wingInner.liftSlope * Mathf.Rad2Deg;
            SteadyCl(s, stallDeg + 15f);
            float justBelow = SteadyCl(s, stallDeg + t.wingInner.stallWidthDeg * 0.5f - 1f);
            var fresh = Wing(t);
            float freshCl = SteadyCl(fresh, stallDeg + t.wingInner.stallWidthDeg * 0.5f - 1f);
            Assert.That(justBelow, Is.LessThan(freshCl), "coming down from a stall the flow reattaches later");
            SteadyCl(s, 0f);
            Assert.That(s.Attached, Is.EqualTo(1f).Within(1e-3f), "well below the stall it reattaches");
        }

        [Test]
        public void Aero_Flaps_RaiseMaxLift()
        {
            var t = Tuning();
            var clean = Wing(t);
            var flapped = Wing(t);
            float bestClean = 0f, bestFlap = 0f;
            for (float a = 0f; a < 25f; a += 0.5f)
            {
                bestClean = Mathf.Max(bestClean, SteadyCl(clean, a));
                bestFlap = Mathf.Max(bestFlap, SteadyCl(flapped, a, 35f));
            }
            Assert.That(bestFlap - bestClean, Is.GreaterThan(0.8f), "35° flaps on the inner section");
        }

        [Test]
        public void Aero_ReverseFlow_NoControlEffect_NoNaN()
        {
            var t = Tuning();
            var s = Wing(t);
            Vector3 f = s.Force(new Vector3(0f, 0f, -30f), 1.2f, 0.3f, 0f, 1f, 0.01f);
            Assert.That(float.IsNaN(f.x) || float.IsNaN(f.y) || float.IsNaN(f.z), Is.False);
            Assert.That(f.z, Is.GreaterThan(0f), "moving backwards: drag pushes forward");
        }

        // ---------------------------------------------------------------- engine start (§9.4, AT-F1)
        static void Run(AircraftSystems sys, PlaneSwitchState s, float seconds, float throttle = 0f)
        {
            const float dt = 1f / 120f;
            int n = (int)System.Math.Round(seconds / dt);
            for (int i = 0; i < n; i++)
                sys.Step(new AircraftSystems.Inputs { Switches = s, Throttle = throttle, Rho = 1.25f, OatC = -10f }, dt);
        }

        static PlaneSwitchState ReadyToStart()
        {
            var s = PlaneSwitchState.ColdAndDark;
            s.Set(PlaneSwitch.Battery, true);
            s.Set(PlaneSwitch.FuelPump, true);
            s.Set(PlaneSwitch.Ignition, true);
            return s;
        }

        [Test]
        public void Engine_StartSequence_RunsOnItsOwnAfterIdle()
        {
            var sys = new AircraftSystems(Tuning(), -10f);
            var s = ReadyToStart();
            Run(sys, s, 0.5f);
            s.Set(PlaneSwitch.Starter, true);
            Run(sys, s, 15f);
            Assert.That(sys.Engine.Combusting, Is.True, "lit");
            Assert.That(sys.Engine.Ng, Is.GreaterThanOrEqualTo(51f), "reached idle with the starter");
            s.Set(PlaneSwitch.Starter, false);
            Run(sys, s, 10f);
            Assert.That(sys.Engine.Ng, Is.GreaterThan(50f), "runs by itself");
            Assert.That(sys.Electrics.GeneratorOnline, Is.False, "§9.4: GEN comes online above 55 % Ng, idle is 52 %");
            Run(sys, s, 5f, 0.15f);
            Assert.That(sys.Electrics.GeneratorOnline, Is.True);
            Assert.That(sys.Electrics.BatteryAmps, Is.GreaterThan(0f), "and recharges the battery after the start");
        }

        [Test]
        public void Engine_NoStart_WithoutBattery_OrFuel_OrWithCutOff()
        {
            var t = Tuning();
            var noBatt = ReadyToStart(); noBatt.Set(PlaneSwitch.Battery, false); noBatt.Set(PlaneSwitch.Starter, true);
            var noFuel = ReadyToStart(); noFuel.Set(PlaneSwitch.FuelSelL, false); noFuel.Set(PlaneSwitch.FuelSelR, false); noFuel.Set(PlaneSwitch.Starter, true);
            var shut = ReadyToStart(); shut.Set(PlaneSwitch.FuelShutoff, true); shut.Set(PlaneSwitch.Starter, true);
            var cut = ReadyToStart(); cut.Set(PlaneSwitch.ConditionCutOff, true); cut.Set(PlaneSwitch.Starter, true);
            var noIgn = ReadyToStart(); noIgn.Set(PlaneSwitch.Ignition, false); noIgn.Set(PlaneSwitch.Starter, true);
            foreach (var s in new[] { noBatt, noFuel, shut, cut, noIgn })
            {
                var sys = new AircraftSystems(t, -10f);
                Run(sys, s, 15f);
                Assert.That(sys.Engine.Combusting, Is.False, $"bits {s.Bits:X}");
            }
        }

        [Test]
        public void Engine_ReleasingStarterEarly_Hangs_ThenFlamesOut()
        {
            var sys = new AircraftSystems(Tuning(), -10f);
            var s = ReadyToStart();
            s.Set(PlaneSwitch.Starter, true);
            Run(sys, s, 3.5f);
            Assert.That(sys.Engine.Combusting, Is.True);
            Assert.That(sys.Engine.Ng, Is.LessThan(40f));
            s.Set(PlaneSwitch.Starter, false);
            Run(sys, s, 20f);
            Assert.That(sys.Engine.Combusting, Is.False, "below idle it cannot sustain itself");
        }

        [Test]
        public void Engine_CutOff_Stops_AndPowerFollowsLever()
        {
            var t = Tuning();
            var sys = new AircraftSystems(t, -10f);
            sys.Engine.SetRunning(true, -10f);
            var s = ReadyToStart();
            Run(sys, s, 5f, 1f);
            Assert.That(sys.Engine.Ng, Is.GreaterThan(95f));
            Assert.That(sys.Engine.ThrustN, Is.GreaterThan(t.staticThrustN * 0.8f), "static thrust at full power");
            s.Set(PlaneSwitch.ConditionCutOff, true);
            Run(sys, s, 30f);
            Assert.That(sys.Engine.Combusting, Is.False);
            Assert.That(sys.Engine.Ng, Is.LessThan(10f));
        }

        // ---------------------------------------------------------------- electrics (AT-15) and fuel
        [Test]
        public void Electrics_StarterPullsBusDown_LightsDrainBattery_GeneratorCharges()
        {
            var t = Tuning();
            var el = new ElectricalSystem(t, 0.9f);
            el.Step(true, false, t.starterAmps, 0.01f);
            Assert.That(el.BusVolts, Is.LessThan(t.battLowVolts), "BATT LOW while cranking");
            float before = el.Charge;
            for (int i = 0; i < 3600; i++) el.Step(true, false, t.landingAmps + t.navAmps + t.strobeAmps + t.beaconAmps, 1f);
            Assert.That(el.Charge, Is.LessThan(before - 0.3f), "an hour of lights: 17 A of 42 Ah");
            float low = el.Charge;
            for (int i = 0; i < 600; i++) el.Step(true, true, 20f, 1f);
            Assert.That(el.Charge, Is.GreaterThan(low), "the generator recharges");
            Assert.That(el.BatteryAmps, Is.GreaterThan(0f));
        }

        [Test]
        public void Electrics_FlatBattery_LightsGoOut()
        {
            var t = Tuning();
            var el = new ElectricalSystem(t, 0.02f);
            el.Step(true, false, t.landingAmps, 1f);
            Assert.That(el.Brightness, Is.LessThan(0.2f));
        }

        [Test]
        public void Fuel_OpenTanksFeedEqually_ClosedSelectorMakesImbalance()
        {
            var t = Tuning();
            var f = new FuelSystem(t, 100f, 100f);
            f.Burn(20f, true, true);
            Assert.That(f.LeftL, Is.EqualTo(90f).Within(1e-3f));
            Assert.That(f.RightL, Is.EqualTo(90f).Within(1e-3f));
            f.Burn(20f, true, false);
            Assert.That(f.LeftL, Is.EqualTo(70f).Within(1e-3f));
            Assert.That(f.RightL, Is.EqualTo(90f).Within(1e-3f));
            f.Evaluate(true, true, true, true);
            Assert.That(f.Supply, Is.False, "shutoff pulled");
            f.Evaluate(true, true, false, false);
            Assert.That(f.FeedLimitPph, Is.EqualTo(t.gravityFeedPph), "pump off: gravity feed only");
            Assert.That(f.Add(true, 1000f), Is.EqualTo(t.tankCapacityL - 70f).Within(1e-3f), "capacity respected");
        }

        // ---------------------------------------------------------------- autopilot signs and limits
        static Autopilot Ap()
        {
            var ap = new Autopilot(Tuning().AutopilotGains);
            ap.Engage(90f, 3000f, 2f);
            return ap;
        }

        [Test]
        public void Autopilot_LevelsTheWings_AndTurnsTowardsTheHeading()
        {
            var ap = Ap();
            ap.Step(90f, 15f, 0f, 2f, 0f, 3000f, 0f, 0.01f);
            Assert.That(ap.RollCommand, Is.LessThan(0f), "right bank, on heading: roll left");
            ap.Step(60f, 0f, 0f, 2f, 0f, 3000f, 0f, 0.01f);
            Assert.That(ap.RollCommand, Is.GreaterThan(0f), "target 30° to the right: roll right");
            Assert.That(Mathf.Abs(ap.RollCommand), Is.LessThanOrEqualTo(Tuning().apRollAuthority + 1e-4f), "limited authority");
        }

        [Test]
        public void Autopilot_BelowTarget_Pulls_AboveTarget_Pushes()
        {
            var ap = Ap();
            ap.Step(90f, 0f, 0f, 0f, 0f, 2800f, 0f, 0.01f);
            Assert.That(ap.PitchCommand, Is.LessThan(0f), "200 ft low: pull (pitch axis + = push)");
            var ap2 = Ap();
            ap2.Step(90f, 0f, 0f, 2f, 0f, 3200f, 0f, 0.01f);
            Assert.That(ap2.PitchCommand, Is.GreaterThan(0f), "200 ft high: push");
        }

        [Test]
        public void Autopilot_DisengagesOutsideLimits()
        {
            var ap = Ap();
            ap.Step(90f, 70f, 0f, 0f, 0f, 3000f, 0f, 0.01f);
            Assert.That(ap.Engaged, Is.False);
            Assert.That(ap.DisengageCode, Is.EqualTo(Autopilot.Limits));
            Assert.That(ap.RollCommand, Is.EqualTo(0f));
        }

        // ---------------------------------------------------------------- tyre
        [Test]
        public void Tyre_HoldsBelowFriction_SlidesAbove()
        {
            var w = new WheelContact(0.5f, 0.12f, 190000f, 14000f, 12f, 0.8f);
            Vector3 up = Vector3.up, fwd = Vector3.forward;
            Vector3 p = Vector3.zero;
            // settle with weight ~9.5 kN at 0.05 m compression, then push sideways with 3 kN (< 0.8 N)
            float n = 0f;
            for (int i = 0; i < 240; i++)
            {
                Vector3 f = w.Step(0.45f, p, up, up, fwd, Vector3.zero, 1f, 0.02f, 80000f, 0.7f, 970f, 1f / 120f);
                n = f.y;
            }
            Assert.That(n, Is.EqualTo(9500f).Within(50f));
            Vector3 hold = w.Step(0.45f, p + Vector3.right * 0.03f, up, up, fwd, Vector3.zero, 1f, 0.02f, 80000f, 0.7f, 970f, 1f / 120f);
            Assert.That(hold.x, Is.LessThan(-2000f), "the tyre pulls back to its anchor");
            Assert.That(w.Sliding, Is.False);
            Vector3 slide = w.Step(0.45f, p + Vector3.right * 0.5f, up, up, fwd, Vector3.right * 3f, 1f, 0.02f, 80000f, 0.7f, 970f, 1f / 120f);
            Assert.That(w.Sliding, Is.True);
            Assert.That(Mathf.Abs(slide.x), Is.LessThanOrEqualTo(0.8f * 9500f + 1f), "never more than the friction circle");
        }
    }
}
