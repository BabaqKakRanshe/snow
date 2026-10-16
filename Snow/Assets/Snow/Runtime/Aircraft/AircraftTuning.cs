using System;
using UnityEngine;

namespace Snow.Aircraft
{
    /// <summary>
    /// Aerodynamic data of one lifting section (PHYS-02.4, §9.3). SI units: m², 1/rad. All values TEMPORARY
    /// ("single-engine turboprop STOL" class, not a real aircraft), see docs/dev/Tuning/UtilityPlane.md.
    /// </summary>
    [Serializable]
    public class AeroProfile
    {
        [Tooltip("m², planform area of this section")] public float area = 7.4f;
        [Tooltip("1/rad, finite-wing lift slope")] public float liftSlope = 4.9f;
        public float cl0 = 0.25f;
        [Tooltip("positive stall lift coefficient, clean")] public float clMax = 1.6f;
        [Tooltip("negative stall lift coefficient, clean")] public float clMin = -1.0f;
        public float cd0 = 0.012f;
        public float aspectRatio = 6.8f;
        [Tooltip("Oswald efficiency")] public float oswald = 0.75f;
        [Tooltip("control surface effectiveness tau: dCL = liftSlope * tau * deflection(rad)")] public float controlTau = 0.45f;
        [Tooltip("share of the control dCL that also raises CLmax (flaps lower the stall angle a little)")] public float clMaxShift = 0.85f;
        [Tooltip("extra CD per rad² of control deflection")] public float controlCd2 = 0.27f;
        [Tooltip("deg past the stall angle over which the flow separates fully (smooth stall)")] public float stallWidthDeg = 8f;
        [Tooltip("deg below the stall angle needed to reattach (hysteresis)")] public float reattachHysteresisDeg = 3f;
        [Tooltip("s, separation / reattachment time constants")] public float separationTime = 0.08f, reattachTime = 0.4f;
        [Tooltip("post-stall flat plate: CL = k sin 2a, CD = k2 sin² a")] public float flatPlateCl = 0.95f, flatPlateCd = 1.3f;

        public AeroProfile Clone() => (AeroProfile)MemberwiseClone();
    }

    /// <summary>
    /// Every TEMPORARY number of the Utility Plane in one ScriptableObject (§6, PHYS-02.8). Units in the field names or
    /// tooltips; SI inside the models, gauge units only at the instruments. Origin and tuning results of each value:
    /// docs/dev/Tuning/UtilityPlane.md.
    /// </summary>
    [CreateAssetMenu(menuName = "Snow/Aircraft Tuning", fileName = "AircraftTuning")]
    public class AircraftTuning : ScriptableObject
    {
        [Header("Mass (§9.3)")]
        public float emptyMassKg = 1750f;
        public float maxMassKg = 3290f;
        [Tooltip("kg·m² of the empty aircraft about its centre of mass: X pitch, Y yaw, Z roll")] public Vector3 emptyInertia = new Vector3(9000f, 14000f, 6500f);
        [Tooltip("kg per occupied seat")] public float personMassKg = 85f;

        [Header("Fuel (§9.3, §9.6)")]
        public float tankCapacityL = 580f;
        [Tooltip("kg/L, Jet-A")] public float fuelDensity = 0.80f;
        [Tooltip("L per tank at session start")] public float startFuelL = 290f;
        [Tooltip("prefab space, m")] public Vector3 tankCentreL = new Vector3(-2.0f, 2.75f, 2.0f), tankCentreR = new Vector3(2.0f, 2.75f, 2.0f);
        [Tooltip("L, FUEL LOW lamp")] public float fuelLowL = 40f;
        [Tooltip("pph the high wing feeds by gravity with the pump off")] public float gravityFeedPph = 250f;
        [Tooltip("L between the shutoff and the engine: how long it runs after the supply is cut")] public float lineFuelL = 0.25f;

        [Header("Wing (§9.2, §9.3)")]
        public AeroProfile wingInner = new AeroProfile { area = 7.4f, controlTau = 0.43f };           // flap
        public AeroProfile wingOuter = new AeroProfile { area = 7.4f, controlTau = 0.45f, controlCd2 = 0.15f };   // aileron
        [Tooltip("deg, dihedral: tilts the section normals")] public float dihedralDeg = 2f;
        [Tooltip("deg, wing incidence to the fuselage (nose-up), TEMPORARY. Since model v5 the aircraft parks tail-down at " +
                 "9.98°: on the ground the wing meets the air at about 12°, the take-off run is with the tail raised")]
        public float wingIncidenceDeg = 2f;
        [Tooltip("m, span for ground effect")] public float span = 14.21f;

        [Header("Tail (§9.2)")]
        public AeroProfile hStab = new AeroProfile
        {
            area = 2.335f, liftSlope = 4.0f, cl0 = 0f, clMax = 1.1f, clMin = -1.1f, cd0 = 0.010f, aspectRatio = 5.6f,
            oswald = 0.8f, controlTau = 0.5f, clMaxShift = 0.6f, controlCd2 = 0.2f, stallWidthDeg = 6f,
        };
        public AeroProfile fin = new AeroProfile
        {
            area = 1.92f, liftSlope = 3.0f, cl0 = 0f, clMax = 1.0f, clMin = -1.0f, cd0 = 0.010f, aspectRatio = 1.9f,
            oswald = 0.8f, controlTau = 0.5f, clMaxShift = 0.6f, controlCd2 = 0.2f, stallWidthDeg = 8f,
        };
        [Tooltip("rad, downwash at zero wing angle of attack")] public float downwash0 = 0.023f;
        [Tooltip("d(epsilon)/d(alpha) at the tail")] public float downwashSlope = 0.4f;
        [Tooltip("m, 1/4 chord of the wing to the tail (downwash delay = this / airspeed)")] public float tailArm = 6.27f;

        [Header("Fuselage and gear drag")]
        [Tooltip("m², CD·A along the axis (fuselage, gear, struts)")] public float fuselageDragAreaAxial = 0.95f;
        [Tooltip("m², CD·A sideways")] public float fuselageDragAreaSide = 7.0f;
        [Tooltip("m², CD·A vertical")] public float fuselageDragAreaVertical = 12.0f;

        [Header("Controls (INPUT-01, §9.1)")]
        [Tooltip("deg, elevator trailing edge up (nose-up) limit")] public float elevatorUpDeg = 15f;
        [Tooltip("deg, elevator trailing edge down limit")] public float elevatorDownDeg = 25f;
        public float aileronDeg = 20f, rudderDeg = 25f;
        [Tooltip("deg/s, flight control actuators")] public float actuatorRate = 60f;
        [Tooltip("deg of elevator balance shift at full trim")] public float trimElevatorDeg = 8f;
        [Tooltip("deg/s, electric flaps: 35 deg in 10 s")] public float flapRate = 3.5f;
        public float[] flapDetents = { 0f, 10f, 20f, 35f };
        [Tooltip("deg, tail wheel steering from the pedals")] public float tailSteerDeg = 30f;
        [Tooltip("deg, fixed (ground-adjustable) rudder tab, + = like right rudder: offsets the slipstream swirl near cruise power")]
        public float rudderRigDeg = 1.2f;
        [Tooltip("deg, aileron rigging, + = like right roll: offsets the propeller torque near cruise power")]
        public float aileronRigDeg = 0.3f;

        [Header("Autopilot (user decision 30.09), servo authority as a fraction of the pilot's axis")]
        public float apBankPerHeadingDeg = 1f, apMaxBankDeg = 20f, apRollP = 0.04f, apRollD = 0.02f, apRollAuthority = 0.35f;
        [Tooltip("fpm of climb per ft of altitude error; max fpm")] public float apVsPerFt = 3f, apMaxVs = 700f;
        [Tooltip("deg of pitch per fpm of error; integral deg per fpm·s; max pitch deg")] public float apPitchPerFpm = 0.004f, apPitchIntegral = 0.0015f, apMaxPitchDeg = 10f;
        public float apPitchP = 0.08f, apPitchD = 0.05f, apPitchAuthority = 0.4f;
        [Tooltip("A while engaged")] public float autopilotAmps = 3f;

        public Autopilot.Gains AutopilotGains => new Autopilot.Gains
        {
            BankPerHeadingDeg = apBankPerHeadingDeg, MaxBank = apMaxBankDeg, RollP = apRollP, RollD = apRollD, RollAuthority = apRollAuthority,
            VsPerFt = apVsPerFt, MaxVs = apMaxVs, PitchPerFpm = apPitchPerFpm, PitchIntegral = apPitchIntegral, MaxPitch = apMaxPitchDeg,
            PitchP = apPitchP, PitchD = apPitchD, PitchAuthority = apPitchAuthority,
        };

        [Header("Engine and propeller (§9.3, §9.4)")]
        [Tooltip("W, 750 shp")] public float maxPowerW = 559000f;
        public float idleNg = 52f, maxNg = 101.6f;
        [Tooltip("% Ng the starter alone reaches at 24 V")] public float starterNg = 20f;
        [Tooltip("% Ng of light-off")] public float lightOffNg = 13f;
        [Tooltip("%/s the starter gives below its limit")] public float starterRate = 4f;
        [Tooltip("%/s combustion adds during the start")] public float startAcceleration = 5f;
        [Tooltip("s, Ng time constant above idle (idle to max about 2.5 s)")] public float spoolTime = 0.8f;
        [Tooltip("s, Ng run-down time constant without fuel")] public float rundownTime = 6f;
        [Tooltip("%/s the gas generator loses below idle without the starter (hung start)")] public float subIdleDecay = 2f;
        [Tooltip("rpm, governor range by Lever_Prop")] public float propMinRpm = 1600f, propMaxRpm = 1900f;
        [Tooltip("W at which the fine-pitch propeller reaches the governed rpm")] public float propFinePowerW = 150000f;
        [Tooltip("N, static thrust at full power")] public float staticThrustN = 10000f;
        [Tooltip("propulsive efficiency in cruise")] public float propEfficiency = 0.8f;
        public float propDiameter = 2.64f;
        [Tooltip("kg·m², propeller and reduction gear")] public float propInertia = 12f;
        [Tooltip("m of lateral thrust offset per rad of angle of attack (P-factor)")] public float pFactor = 0.35f;
        [Tooltip("CD·A of a windmilling / stopped propeller, m²")] public float propDragArea = 0.4f;
        [Tooltip("°C")] public float ittLimit = 805f, ittStartLimit = 1000f;
        [Tooltip("ITT loss of power: fraction per 100 °C over the limit (reversible)")] public float overheatDerate = 0.3f;
        public float idleFuelPph = 70f, maxFuelPph = 400f, startFuelPph = 90f;

        [Header("Electrics (§9.5)")]
        public float batteryAh = 42f;
        [Tooltip("V open circuit, full and empty")] public float batteryVoltsFull = 25.8f, batteryVoltsEmpty = 22.0f;
        [Tooltip("Ω internal resistance")] public float batteryResistance = 0.012f;
        [Tooltip("start state of charge 0..1")] public float batteryStartCharge = 0.9f;
        public float generatorVolts = 28.2f, generatorAmps = 200f;
        [Tooltip("% Ng the generator comes online")] public float generatorNg = 55f;
        [Tooltip("V, BATT LOW lamp")] public float battLowVolts = 23.5f;
        [Tooltip("V below which loads drop out")] public float brownoutVolts = 16f;
        public float starterAmps = 300f, landingAmps = 8f, strobeAmps = 4f, navAmps = 3f, beaconAmps = 2f, avionicsAmps = 10f,
            pumpAmps = 5f, domeAmps = 1f, cargoLightAmps = 1f, panelLightAmps = 1f, floodAmps = 1f, doorAmps = 15f, flapAmps = 10f,
            gyroAmps = 2f, blowerAmps = 3f;

        [Header("Gear (§9.1, §9.3)")]
        [Tooltip("m, tyre radii of the model (v5: tail wheel 0.2856)")] public float mainWheelRadius = 0.484f, tailWheelRadius = 0.2856f;
        [Tooltip("m of wheel travel")] public float mainTravel = 0.12f, tailTravel = 0.06f;
        [Tooltip("N/m per main wheel: about 0.05 m sag at empty mass")] public float mainSpring = 190000f;
        [Tooltip("N·s/m per main wheel")] public float mainDamper = 14000f;
        public float tailSpring = 140000f, tailDamper = 5000f;
        [Tooltip("stiffness multiplier beyond the travel (bump stop)")] public float bumpStop = 12f;
        [Tooltip("tyre friction coefficient (dry hard surface, TEMPORARY)")] public float mainGrip = 0.8f, tailGrip = 0.6f;
        public float brakeGrip = 0.6f, rollingResistance = 0.02f;
        [Tooltip("N/m, sideways tyre stiffness before sliding")] public float tyreStiffness = 80000f;
        [Tooltip("damping ratio of that stiffness")] public float tyreDamping = 0.7f;

        [Header("Doors (§8.2)")]
        public float cargoDoorTime = 4.5f, cabinDoorTime = 2.5f, manualDoorTime = 1.5f;

        [Header("Instruments (§10.3)")]
        [Tooltip("s")] public float vsiLag = 1.5f;
        [Tooltip("s to spin the attitude gyro up / down")] public float gyroSpinUp = 60f, gyroSpinDown = 180f;
        [Tooltip("deg per hour")] public float headingDrift = 4f;
        [Tooltip("deg of wing angle of attack before the stall at which the horn sounds (about 5-10 kt, §10.1)")] public float stallWarningMarginDeg = 3f;
        [Tooltip("inHg the engine-driven vacuum pump gives above idle (TEMPORARY, no vacuum model, И2 C.4)")] public float suctionInHg = 5f;
        [Tooltip("s, suction rise / fall time constants")] public float suctionRise = 1f, suctionFall = 3f;
        [Tooltip("s, magnetic compass card lag: first order, no deviation or turning errors (TEMPORARY, И2 C.9)")] public float compassLag = 1f;

        [Header("Winter kit (TEMPORARY, И4 K4.3, K4.5)")]
        [Tooltip("°C per minute the heater warms oil and engine (×2 under the insulated cover)")] public float preheatCPerMin = 1f;
        [Tooltip("°C above the outside air the heater stops at")] public float preheatAboveOatC = 30f;
        [Tooltip("L/min from the jerry can into a tank")] public float jerryPourLpm = 10f;
        [Tooltip("N/m and N·s/m: chocks / tie-downs / heater duct hold the parked aircraft (horizontal spring at the CG)")] public float holdSpring = 200000f, holdDamper = 43000f;
        [Tooltip("N·m/rad and N·m·s/rad: the same about the vertical")] public float holdYawSpring = 2000000f, holdYawDamper = 330000f;

        [Header("Glass fog (TEMPORARY, И2 G.1)")]
        [Tooltip("°C: below this outside temperature the glass fogs from breath")] public float fogBelowOatC = 5f;
        [Tooltip("fog per second per person in the cabin and bay")] public float fogPerPerson = 0.004f;
        [Tooltip("per second × pull: Pull_Defrost with the engine running clears the windshield")] public float defrostRate = 0.05f;
        [Tooltip("per second × pull: Pull_CabinHeat with the engine running clears all panes and stops the fogging")] public float cabinHeatRate = 0.02f;
    }
}
