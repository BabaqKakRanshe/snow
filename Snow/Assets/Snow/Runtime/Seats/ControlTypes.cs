using System;
using Unity.Netcode;
using UnityEngine;

namespace Snow.Seats
{
    /// <summary>Discrete pilot-seat commands that are not a cockpit zone (reliable RPC, only from the pilot seat).</summary>
    public enum PilotCommand : byte
    {
        AutopilotToggle = 1,
    }

    /// <summary>
    /// Pilot intent sent every network tick, unreliable (§11.1). Axes -1..1: + = push forward, right roll, right pedal.
    /// ThrottleRate -1..1 moves the throttle lever; it is not a speed command (INPUT-01). Brake 0..1 (toe brakes, split
    /// by the pedals); TrimRate -1..1 turns the elevator trim wheel, + = nose up.
    /// </summary>
    public struct ControlInput : INetworkSerializable
    {
        public float Pitch, Roll, Yaw, ThrottleRate, Brake, TrimRate;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Pitch);
            s.SerializeValue(ref Roll);
            s.SerializeValue(ref Yaw);
            s.SerializeValue(ref ThrottleRate);
            s.SerializeValue(ref Brake);
            s.SerializeValue(ref TrimRate);
        }

        public ControlInput Clamped() => new ControlInput
        {
            Pitch = Mathf.Clamp(Pitch, -1f, 1f),
            Roll = Mathf.Clamp(Roll, -1f, 1f),
            Yaw = Mathf.Clamp(Yaw, -1f, 1f),
            ThrottleRate = Mathf.Clamp(ThrottleRate, -1f, 1f),
            Brake = Mathf.Clamp01(Brake),
            TrimRate = Mathf.Clamp(TrimRate, -1f, 1f),
        };
    }

    /// <summary>
    /// Server-owned control positions, replicated for the yoke, pedals and levers on every peer (VIEW-01).
    /// Throttle 0..1 lever position; Brake 0..1 toe brakes; Trim -1..1 trim wheel position, + = nose up.
    /// </summary>
    public struct ControlState : INetworkSerializable, IEquatable<ControlState>
    {
        public float Pitch, Roll, Yaw, Throttle, Brake, Trim;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Pitch);
            s.SerializeValue(ref Roll);
            s.SerializeValue(ref Yaw);
            s.SerializeValue(ref Throttle);
            s.SerializeValue(ref Brake);
            s.SerializeValue(ref Trim);
        }

        public bool Equals(ControlState o) => Pitch == o.Pitch && Roll == o.Roll && Yaw == o.Yaw && Throttle == o.Throttle
                                              && Brake == o.Brake && Trim == o.Trim;
    }
}
