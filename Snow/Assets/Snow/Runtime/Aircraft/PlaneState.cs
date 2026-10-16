using System;
using Unity.Netcode;

namespace Snow.Aircraft
{
    /// <summary>Two-position cockpit controls (§8.4). Names follow the model's objects (UNITY_CONTRACT §4).</summary>
    [Flags]
    public enum PlaneSwitch : uint
    {
        None = 0,
        Battery = 1u << 0,
        Avionics = 1u << 1,
        FuelPump = 1u << 2,
        Ignition = 1u << 3,
        Starter = 1u << 4,          // momentary: held
        Beacon = 1u << 5,
        Strobe = 1u << 6,
        NavLights = 1u << 7,
        Landing = 1u << 8,
        CargoDoorL = 1u << 9,       // SW_CargoDoor_L up = open
        CargoDoorR = 1u << 10,
        CabinLight = 1u << 11,
        CargoLight = 1u << 12,
        AnnunTest = 1u << 13,       // momentary: held
        CabinHeat = 1u << 14,       // Pull_*: set = pulled
        Defrost = 1u << 15,
        ParkBrake = 1u << 16,
        FuelShutoff = 1u << 17,     // pulled = fuel shut off
        FuelSelL = 1u << 18,        // set = ON (handle down)
        FuelSelR = 1u << 19,
        BeltP = 1u << 20,
        BeltC = 1u << 21,
        VisorP = 1u << 22,
        VisorC = 1u << 23,
        FuelCapL = 1u << 24,        // set = open
        FuelCapR = 1u << 25,
        ConditionCutOff = 1u << 26, // Lever_Condition in cut-off
        CabinDoorL = 1u << 27,      // v7 SW_CabinDoor_L up = open (И2, M1 closed)
        CabinDoorR = 1u << 28,
        Locked = 1u << 29,          // central lock (§8.3): outer handles do not open
        Glovebox = 1u << 30,        // Glovebox_Lid open
    }

    /// <summary>
    /// Replicated positions of every cockpit control that is not a flight axis (NEW-02, §11.1): the requested
    /// position. What it actually does (power, fuel, current) is in the systems and in PlaneTelemetry (SYS-01).
    /// </summary>
    public struct PlaneSwitchState : INetworkSerializable, IEquatable<PlaneSwitchState>
    {
        public uint Bits;
        /// <summary>Rheostats 0..1, Lever_Prop 0..1 (1 = max rpm).</summary>
        public float RheoPanel, RheoFlood, PropLever;
        /// <summary>Lever_Flap detent 0..3 (0/10/20/35°).</summary>
        public byte FlapLever;
        /// <summary>v7 instrument knobs (И2): altimeter setting, hundredths of inHg (2860..3100); heading bug, deg 0..359;
        /// Knob_DG_Set turns, steps of 1° (the knob's own angle; the DG shift is in the server instruments).</summary>
        public ushort AltSetting, HdgBug;
        public short DgKnob;
        /// <summary>13 radio knobs (UtilityPlaneRig.RadioKnobNames), one byte each: detent 0..23 of 15° clockwise.
        /// Knobs 0..7 in RadioA, 8..12 in RadioB. Xpdr: Knob_XPDR_Mode 0..4 = OFF, SBY, ON, ALT, TST.</summary>
        public ulong RadioA, RadioB;
        public byte Xpdr;

        public const int RadioKnobCount = 13, RadioDetents = 24;
        public int RadioKnob(int i) => (int)(((i < 8 ? RadioA : RadioB) >> (8 * (i & 7))) & 0xFF);
        public void SetRadioKnob(int i, int detent)
        {
            int shift = 8 * (i & 7);
            ulong v = (ulong)(((detent % RadioDetents) + RadioDetents) % RadioDetents) << shift, mask = 0xFFUL << shift;
            if (i < 8) RadioA = (RadioA & ~mask) | v; else RadioB = (RadioB & ~mask) | v;
        }
        public float AltSettingInHg => AltSetting / 100f;

        public bool On(PlaneSwitch s) => (Bits & (uint)s) != 0;
        public void Set(PlaneSwitch s, bool on) => Bits = on ? Bits | (uint)s : Bits & ~(uint)s;

        /// <summary>A parked aircraft at session start: fuel selectors on, parking brake set, condition at idle, prop max.</summary>
        public static PlaneSwitchState ColdAndDark => new PlaneSwitchState
        {
            Bits = (uint)(PlaneSwitch.FuelSelL | PlaneSwitch.FuelSelR | PlaneSwitch.ParkBrake),
            RheoPanel = 0.5f,
            RheoFlood = 0f,
            PropLever = 1f,
            FlapLever = 0,
            AltSetting = 2992,
        };

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Bits);
            s.SerializeValue(ref RheoPanel);
            s.SerializeValue(ref RheoFlood);
            s.SerializeValue(ref PropLever);
            s.SerializeValue(ref FlapLever);
            s.SerializeValue(ref AltSetting); s.SerializeValue(ref HdgBug); s.SerializeValue(ref DgKnob);
            s.SerializeValue(ref RadioA); s.SerializeValue(ref RadioB); s.SerializeValue(ref Xpdr);
        }

        public bool Equals(PlaneSwitchState o) => Bits == o.Bits && RheoPanel == o.RheoPanel && RheoFlood == o.RheoFlood
                                                  && PropLever == o.PropLever && FlapLever == o.FlapLever
                                                  && AltSetting == o.AltSetting && HdgBug == o.HdgBug && DgKnob == o.DgKnob
                                                  && RadioA == o.RadioA && RadioB == o.RadioB && Xpdr == o.Xpdr;
    }

    /// <summary>
    /// Server state that every peer shows (§11.1, VIEW-01): actuator positions, gear, sensor readings (not truth),
    /// lamps and lights. Written by the server each physics step, sent at the network tick; the presentation rebuilds
    /// everything from it, never from one-off events (NET-04).
    /// </summary>
    public struct PlaneTelemetry : INetworkSerializable
    {
        // actuators, degrees: elevator + = trailing edge up, aileron + = right roll, rudder + = trailing edge right
        public float Elevator, Aileron, Rudder, Flap, TailSteer;
        // gear: compression 0..1 of travel, rolling speeds m/s
        public float GearL, GearR, GearT, WheelMain, WheelTail;
        // engine truth for the moving propeller
        public float PropRpm;
        // flight instruments (sensor models)
        public float IasKt, AltFt, VsiFpm, PitchDeg, BankDeg, HeadingDeg, TurnRate, Slip;
        // engine, fuel and electric gauges (zero without bus power)
        public float TorqueFtLb, PropGauge, IttC, NgPct, OilPsi, OilC, FuelFlowPph, FuelLGal, FuelRGal, Amps, OatC;
        public float BusVolts;
        /// <summary>И2: suction gauge inHg; magnetic compass card deg (lagging); glass fog 0..1 (windshield, other panes).</summary>
        public float SuctionInHg, CompassDeg, FogWindshield, FogSide;
        /// <summary>Lamp_00..11 conditions (the Rig adds bus power and the test button).</summary>
        public ushort Lamps;
        public bool BusPowered, StallWarning, EngineRunning;
        // autopilot: engaged, targets, last disengage reason (Autopilot.ReasonText)
        public bool ApEngaged;
        public byte ApReason;
        public float ApHeading, ApAltFt;
        // light outputs 0..255 (already include switch and bus voltage); Avionics = avionics bus output (SW_Avionics × bus)
        public byte Nav, Strobe, Beacon, LandingLight, Cabin, Cargo, Panel, Flood, Avionics;
        // developer telemetry only (not shown without the dev overlay)
        public float AlphaDeg, TrueAirspeed, GroundSpeed, HeightAgl, MassKg, FuelTotalL, BatteryCharge;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Elevator); s.SerializeValue(ref Aileron); s.SerializeValue(ref Rudder);
            s.SerializeValue(ref Flap); s.SerializeValue(ref TailSteer);
            s.SerializeValue(ref GearL); s.SerializeValue(ref GearR); s.SerializeValue(ref GearT);
            s.SerializeValue(ref WheelMain); s.SerializeValue(ref WheelTail); s.SerializeValue(ref PropRpm);
            s.SerializeValue(ref IasKt); s.SerializeValue(ref AltFt); s.SerializeValue(ref VsiFpm);
            s.SerializeValue(ref PitchDeg); s.SerializeValue(ref BankDeg); s.SerializeValue(ref HeadingDeg);
            s.SerializeValue(ref TurnRate); s.SerializeValue(ref Slip);
            s.SerializeValue(ref TorqueFtLb); s.SerializeValue(ref PropGauge); s.SerializeValue(ref IttC);
            s.SerializeValue(ref NgPct); s.SerializeValue(ref OilPsi); s.SerializeValue(ref OilC);
            s.SerializeValue(ref FuelFlowPph); s.SerializeValue(ref FuelLGal); s.SerializeValue(ref FuelRGal);
            s.SerializeValue(ref Amps); s.SerializeValue(ref OatC); s.SerializeValue(ref BusVolts);
            s.SerializeValue(ref SuctionInHg); s.SerializeValue(ref CompassDeg); s.SerializeValue(ref FogWindshield); s.SerializeValue(ref FogSide);
            s.SerializeValue(ref Lamps);
            s.SerializeValue(ref BusPowered); s.SerializeValue(ref StallWarning); s.SerializeValue(ref EngineRunning);
            s.SerializeValue(ref ApEngaged); s.SerializeValue(ref ApReason); s.SerializeValue(ref ApHeading); s.SerializeValue(ref ApAltFt);
            s.SerializeValue(ref Nav); s.SerializeValue(ref Strobe); s.SerializeValue(ref Beacon);
            s.SerializeValue(ref LandingLight); s.SerializeValue(ref Cabin); s.SerializeValue(ref Cargo);
            s.SerializeValue(ref Panel); s.SerializeValue(ref Flood); s.SerializeValue(ref Avionics);
            s.SerializeValue(ref AlphaDeg); s.SerializeValue(ref TrueAirspeed); s.SerializeValue(ref GroundSpeed);
            s.SerializeValue(ref HeightAgl); s.SerializeValue(ref MassKg); s.SerializeValue(ref FuelTotalL);
            s.SerializeValue(ref BatteryCharge);
        }
    }
}
