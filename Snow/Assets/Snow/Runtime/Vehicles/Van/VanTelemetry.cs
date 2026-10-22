using System;
using Unity.Netcode;
using UnityEngine;

namespace Snow.Vehicles.Van
{
    [Flags]
    public enum VanFlags : ushort
    {
        None = 0,
        Engine = 1 << 0,
        Starter = 1 << 1,
        Handbrake = 1 << 2,
        Brake = 1 << 3,
        Reverse = 1 << 4,
        TurnLeft = 1 << 5,
        TurnRight = 1 << 6,
        Hazard = 1 << 7,
        Horn = 1 << 8,
        FrontAxle = 1 << 9,
        LowRange = 1 << 10,
        Ignition = 1 << 11,
    }

    /// <summary>
    /// The van's fast state, one struct at telemetryHz (VAN3_INTEGRATION_UNITY.md §3.2): what every peer's presentation
    /// needs for the steering wheel, wheels, suspension, needles, pedals, lever and lamps. Written only by the server.
    /// </summary>
    public struct VanTelemetry : INetworkSerializable, IEquatable<VanTelemetry>
    {
        public float Rpm, SpeedKmh, SteerDeg;
        /// <summary>rad/s per wheel FL, FR, RL, RR (clients spin the wheels themselves).</summary>
        public float W0, W1, W2, W3;
        /// <summary>m of suspension travel from the model pose per wheel, + = up.</summary>
        public float S0, S1, S2, S3;
        public sbyte Gear;
        public VanFlags Flags;
        public byte Gas, Brake;
        /// <summary>V on the bus (VanRig.busVoltage, the voltmeter); 0 with the mass off.</summary>
        public float BusVolts;
        /// <summary>Battery charge 0..255.</summary>
        public byte Charge;

        public float Omega(int i) => i == 0 ? W0 : i == 1 ? W1 : i == 2 ? W2 : W3;
        public float Travel(int i) => i == 0 ? S0 : i == 1 ? S1 : i == 2 ? S2 : S3;
        public bool Has(VanFlags f) => (Flags & f) != 0;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Rpm);
            s.SerializeValue(ref SpeedKmh);
            s.SerializeValue(ref SteerDeg);
            s.SerializeValue(ref W0); s.SerializeValue(ref W1); s.SerializeValue(ref W2); s.SerializeValue(ref W3);
            s.SerializeValue(ref S0); s.SerializeValue(ref S1); s.SerializeValue(ref S2); s.SerializeValue(ref S3);
            s.SerializeValue(ref Gear);
            s.SerializeValue(ref Flags);
            s.SerializeValue(ref Gas);
            s.SerializeValue(ref Brake);
            s.SerializeValue(ref BusVolts);
            s.SerializeValue(ref Charge);
        }

        public bool Equals(VanTelemetry o) =>
            Rpm == o.Rpm && SpeedKmh == o.SpeedKmh && SteerDeg == o.SteerDeg && W0 == o.W0 && W1 == o.W1 && W2 == o.W2 && W3 == o.W3
            && S0 == o.S0 && S1 == o.S1 && S2 == o.S2 && S3 == o.S3 && Gear == o.Gear && Flags == o.Flags && Gas == o.Gas && Brake == o.Brake
            && BusVolts == o.BusVolts && Charge == o.Charge;

        public static byte Byte01(float v) => (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f);
    }
}
