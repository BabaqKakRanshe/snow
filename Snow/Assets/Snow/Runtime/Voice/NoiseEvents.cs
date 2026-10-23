using System;
using Snow.Net;
using UnityEngine;

namespace Snow.Voice
{
    /// <summary>
    /// What creatures can hear (R2; QUESTIONS_VOICE 3, 14): the server announces every loud thing with a point and a
    /// radius — body sounds, callouts, a radio talking. Creatures do not exist yet; they will subscribe to <see cref="Heard"/>.
    /// Radii are TEMPORARY (VOICE_LIST.md §4: quiet 5 m, medium 15 m, loud 30 m).
    /// </summary>
    public static class NoiseEvents
    {
        public const float Quiet = 5f, Medium = 15f, Loud = 30f;

        /// <summary>Server: point, radius in metres, kind, the client who made it (ulong.MaxValue: the world).</summary>
        public static event Action<Vector3, float, string, ulong> Heard;

        /// <summary>Client: until when the own player was loud enough to be heard (the "I can be heard" mark, QUESTIONS_VOICE 15).</summary>
        public static float LocalHeardUntil { get; private set; } = -10f;

        public static void Emit(Vector3 point, float radius, string kind, ulong source = ulong.MaxValue)
        {
            Heard?.Invoke(point, radius, kind, source);
            BenchLog.Event("noise", "kind", kind, "r", radius, "at", point, "from", source == ulong.MaxValue ? -1L : (long)source);
        }

        /// <summary>Client: the own player made a sound of this radius.</summary>
        public static void LocalMade(float radius)
        {
            if (radius >= Medium) LocalHeardUntil = Time.time + 1.5f;
        }
    }

    /// <summary>Sounds of the player's body (VOICE_LIST.md §4). The order is the wire format: add at the end.</summary>
    public enum BodySound : byte
    {
        /// <summary>C pressed: a whistle or "Hey!", heard far (QUESTIONS_VOICE 13).</summary>
        Callout,
        /// <summary>C held: a mark where the flashlight points, seen by everyone, silent.</summary>
        Ping,
        Land,
        PainSmall,
        PainBig,
        EffortLight,
        EffortHeavy,
        Cough,
        Shiver,
        Breath,
        Wake,
    }

    public static class BodySounds
    {
        /// <summary>Radius of a body sound for creatures (0: silent).</summary>
        public static float Radius(BodySound s)
        {
            switch (s)
            {
                case BodySound.Callout: case BodySound.PainBig: return NoiseEvents.Loud;
                case BodySound.Land: case BodySound.PainSmall: case BodySound.Cough: return NoiseEvents.Medium;
                case BodySound.Ping: return 0f;
                default: return NoiseEvents.Quiet;
            }
        }

        /// <summary>s between two of the same sound of one player (server and client both keep it).</summary>
        public static float Cooldown(BodySound s)
        {
            switch (s)
            {
                case BodySound.Callout: case BodySound.Ping: return 3f;
                case BodySound.Cough: return 20f;
                case BodySound.PainSmall: return 3f;
                case BodySound.PainBig: return 5f;
                case BodySound.EffortLight: case BodySound.EffortHeavy: return 2f;
                default: return 1f;
            }
        }

        /// <summary>Clip set name in Resources/Voice: <c>vo_body_callout_1</c> …</summary>
        public static string ClipName(BodySound s) => "vo_body_" + s.ToString().ToLowerInvariant();
    }
}
