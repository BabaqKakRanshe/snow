using UnityEngine;

namespace Snow.Vehicles.Van
{
    /// <summary>
    /// Numbers of the UAZ van (VAN3_INTEGRATION_UNITY.md §4, §8; Data/vehicle.json of the package). All TEMPORARY: tuned on
    /// the P09 bench, every change goes to docs/dev/Tuning/Van.md with units, source and the measurement.
    /// Units: kg, m, N, N·m, kg·m², rpm, degrees, seconds unless the name says otherwise.
    /// </summary>
    [CreateAssetMenu(menuName = "Snow/Van Tuning")]
    public class VanTuning : ScriptableObject
    {
        [Header("Body (§4.1)")]
        public float massKg = 1950f;
        [Tooltip("van space (x right, y up, z forward)")] public Vector3 centerOfMass = new Vector3(0f, 0.88f, 0.15f);
        [Tooltip("kg·m² about x (pitch), y (yaw), z (roll); explicit like BenchCarrier (package note P-2)")]
        public Vector3 inertia = new Vector3(4300f, 4100f, 1600f);
        [Tooltip("kg per occupied seat, at the seat's hip")] public float personMassKg = 85f;
        [Tooltip("kg per litre of petrol in the tanks")] public float fuelKgPerLitre = 0.74f;
        [Tooltip("m², air drag ½ρ·CdA·v²")] public float dragArea = 2.2f;
        [Tooltip("kg/m³")] public float airDensity = 1.3f;
        [Tooltip("friction of the underside boxes (underbody, tanks, bumpers): the belly slides off an edge, not hooks on it")]
        public float underbodyFriction = 0.1f;

        [Header("Wheels and suspension (§4.2)")]
        [Tooltip("van space, the model pose (static, loaded): FL, FR, RL, RR (vehicle.json wheels[].centre)")]
        public Vector3[] wheelCentres =
        {
            new Vector3(-0.8272f, 0.3618f, 1.2067f), new Vector3(0.8243f, 0.3618f, 1.2067f),
            new Vector3(-0.8272f, 0.3618f, -1.2031f), new Vector3(0.8243f, 0.3618f, -1.2031f),
        };
        public float wheelRadius = 0.364f;
        [Tooltip("N/m per wheel")] public float springRate = 42000f;
        [Tooltip("N·s/m, compression")] public float damperBump = 3500f;
        [Tooltip("N·s/m, rebound")] public float damperRebound = 5000f;
        [Tooltip("m of compression travel above the static pose of the model")] public float bumpTravel = 0.10f;
        [Tooltip("bump stop stiffness, × springRate")] public float bumpStop = 10f;
        [Tooltip("rebound damper × this while on the bump stop: it soaks the hit up instead of throwing the body back")]
        public float bumpStopDamping = 4f;
        [Tooltip("m/s: the damper sees at most this compression speed (a step under the ray is not a 12 m/s hit)")]
        public float damperMaxRate = 1.5f;
        [Tooltip("rays along the tyre's footprint (odd): the wheel rolls up onto an edge instead of a point dropping into it")]
        public int profileRays = 5;
        [Tooltip("the outer rays at ± this × radius ahead / behind the axle")] public float profileSpan = 0.8f;
        [Tooltip("degrees: ground steeper than this to the van's up (a rock's side, a step) is taken as flat under the wheel")]
        public float steepNormalDeg = 40f;
        [Tooltip("kg·m² per wheel (with its share of the axle)")] public float wheelInertia = 1.2f;
        [Tooltip("N/m tangential tyre anchor stiffness (WheelContact)")] public float tyreStiffness = 120000f;
        [Tooltip("damping ratio of the tyre anchor")] public float tyreDamping = 0.7f;
        [Tooltip("m/s of slip at which the tyre force is fully built (below: proportional)")] public float slipFull = 0.25f;
        public float rollingResistance = 0.015f;
        public float rollingResistanceSnow = 0.03f;

        [Header("Grip μ (§4.2, V-02)")]
        public float muRoad = 0.8f;
        public float muPackedSnow = 0.7f;
        public float muLooseSnow = 0.55f;
        public float muIce = 0.10f;
        public float muSandTrack = 0.9f;
        public float chainsBonusSnow = 0.12f;
        public float chainsBonusIce = 0.18f;
        [Tooltip("μ × this on a flat tyre")] public float flatMuFactor = 0.6f;
        [Tooltip("m less radius on a flat tyre")] public float flatRadiusLoss = 0.04f;
        [Tooltip("rolling resistance × this on a flat tyre")] public float flatRollingFactor = 3f;

        [Header("Steering (§4.2, §4.4)")]
        public float wheelbase = 2.4098f;
        public float track = 1.6515f;
        public float maxSteerDeg = 32f;
        [Tooltip("deg/s of the road wheels while A/D is held")] public float steerRate = 60f;
        [Tooltip("deg/s back to centre")] public float steerReturnRate = 90f;
        [Tooltip("km/h above which the keyboard lock fades")] public float steerFadeFromKmh = 40f;
        [Tooltip("km/h where the keyboard lock reaches steerFadeMin")] public float steerFadeToKmh = 80f;
        [Range(0f, 1f)] public float steerFadeMin = 0.5f;

        [Header("Engine UMZ-451M (§4.3)")]
        public float[] torqueRpm = { 600, 1000, 1500, 2000, 2500, 3000, 3500, 4000, 4300 };
        public float[] torqueNm = { 118, 140, 158, 165, 167, 162, 152, 135, 110 };
        public float idleRpm = 600f;
        [Tooltip("ignition cut (package note P-1: redlineRpm)")] public float cutoffRpm = 4200f;
        public float stallRpm = 350f;
        [Tooltip("N·m friction at any speed")] public float engineFrictionNm = 18f;
        [Tooltip("N·m of engine braking at closed throttle per 1000 rpm")] public float engineBrakeNmPer1000 = 6f;
        public float engineInertia = 0.18f;
        [Tooltip("idle governor: throttle that holds idle against friction")] public float idleThrottle = 0.18f;
        [Tooltip("idle governor: extra throttle per rpm below idle")] public float idleGain = 0.002f;

        [Header("Automatic clutch (§4.3)")]
        [Tooltip("N·m per rad/s of slip while closing")] public float clutchStiffness = 40f;
        [Tooltip("× the engine's peak torque")] public float clutchCapacity = 1.3f;
        public float clutchEngageFromRpm = 900f;
        public float clutchEngageToRpm = 1500f;
        [Tooltip("push / tow start: input shaft rpm window while the engine is stopped")] public float pushEngageFromRpm = 100f;
        public float pushEngageToRpm = 400f;

        [Header("Gearbox, transfer, axles (§4.3)")]
        public float[] gearRatios = { 4.124f, 2.641f, 1.58f, 1.0f };
        public float reverseRatio = 5.224f;
        public float shiftUpRpm = 3000f;
        public float shiftUpFloor = 0.8f;
        public float shiftDownRpm = 1300f;
        [Tooltip("s without torque while shifting")] public float shiftTime = 0.6f;
        [Tooltip("s after a shift before the next one")] public float shiftLockout = 1.2f;
        [Tooltip("kick-down only if the rpm after the downshift stays at or below this")] public float kickdownMaxRpm = 2700f;
        [Tooltip("gas pedal crossing this is a kick-down request")] public float kickdownPedal = 0.9f;
        [Tooltip("s of S held below reverseEngageKmh to engage reverse")] public float reverseHoldTime = 0.4f;
        public float reverseEngageKmh = 1f;
        public float transferHigh = 1.0f;
        public float transferLow = 1.94f;
        [Tooltip("km/h: above this the range lever refuses (no synchroniser)")] public float rangeShiftMaxKmh = 3f;
        public float finalDrive = 5.125f;
        [Tooltip("0 = open differentials; 1 = a wheel can pass all its torque to the other wheel of the axle (self-locking)")]
        [Range(0f, 1f)] public float diffLock = 0.7f;
        [Tooltip("the front axle lever is in when the van spawns")] public bool frontAxleAtStart = true;

        [Header("Brakes (§4.2, §4.3)")]
        [Tooltip("N·m per front wheel at full pedal")] public float brakeFrontNm = 2600f;
        [Tooltip("N·m per rear wheel at full pedal")] public float brakeRearNm = 1700f;
        [Tooltip("N·m on the transmission drum")] public float handbrakeNm = 1400f;

        [Header("Electrics (В4, vehicle.json electrics)")]
        public float batteryAh = 60f;
        [Tooltip("V open-circuit at the knee charge and below it (see kneeCharge)")] public float batteryOcvEmpty = 11.6f;
        [Tooltip("V open-circuit full")] public float batteryOcvFull = 12.7f;
        public float batteryRintOhm = 0.006f;
        [Tooltip("× internal resistance at −20 °C (1.0 at +20 °C, linear)")] public float coldRintFactor = 1.5f;
        public float kneeCharge = 0.2f;
        [Tooltip("V of open-circuit voltage lost per unit of charge below the knee")] public float ocvDropBelowKnee = 9f;
        [Tooltip("internal resistance × (1 + this × (knee − charge)) below the knee")] public float rintRiseBelowKnee = 30f;
        public float alternatorAmps = 40f;
        public float alternatorCutInRpm = 900f;
        public float regulatorVolts = 14.2f;
        public float starterOhm = 0.045f;
        [Tooltip("V: the lamps' watts are rated at this (loads = W / this)")] public float lampRatedVolts = 12.6f;
        public float ignitionAmps = 4f;
        public float[] heaterFanWatts = { 0f, 25f, 45f, 70f };
        public float wiperWatts = 35f;
        public float radioWatts = 15f;
        [Tooltip("wipes per minute with the switch on")] public float wiperRate = 40f;
        [Tooltip("°C of the battery on the bench (the forest start sets its own)")] public float benchBatteryTempC = 20f;

        [Header("Network (§3.2)")]
        [Tooltip("Hz of the telemetry struct")] public float telemetryHz = 15f;
        [Tooltip("Hz of glass and snow")] public float glassHz = 1f;
    }
}
