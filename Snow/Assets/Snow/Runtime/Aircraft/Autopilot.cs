using UnityEngine;

namespace Snow.Aircraft
{
    /// <summary>
    /// A simple two-axis autopilot as a cockpit device (user decision 30.09: "автопилот-прибор"; §10.3 had it DEFERRED).
    /// It holds the heading and the altitude it was engaged at, by moving the same ailerons and elevator the pilot does,
    /// through servos with limited authority — it is not a stabiliser inside the physics (PHYS-01 stays): out of trim,
    /// overloaded or in a storm it can run out of authority, and the aircraft does what the aerodynamics say.
    /// No autothrottle and no yaw damper: speed and balance stay with the pilot. Pure C#.
    ///
    /// Signs: axes as ControlInput (pitch + = push, roll + = right). Angles in degrees, bank + = right wing down,
    /// pitch + = nose up, rates in deg/s with the same signs, vertical speed in fpm, altitude in ft.
    /// </summary>
    public class Autopilot
    {
        public struct Gains
        {
            public float BankPerHeadingDeg, MaxBank, RollP, RollD, RollAuthority;
            public float VsPerFt, MaxVs, PitchPerFpm, PitchIntegral, MaxPitch, PitchP, PitchD, PitchAuthority;
        }

        public bool Engaged { get; private set; }
        public float TargetHeading { get; private set; }
        public float TargetAltitudeFt { get; private set; }
        /// <summary>Why it last disengaged (0 = not disengaged); text: ReasonText.</summary>
        public byte DisengageCode { get; private set; }

        public const byte ByPilot = 1, ByYoke = 2, NoPower = 3, Limits = 4, PilotLeft = 5;

        public static string ReasonText(byte code)
        {
            switch (code)
            {
                case ByPilot: return "Автопилот выключен";
                case ByYoke: return "Автопилот отключён штурвалом";
                case NoPower: return "Автопилот отключился: нет питания авионики";
                case Limits: return "Автопилот отключился: вышли за пределы";
                case PilotLeft: return "Автопилот отключён: пилот покинул кресло";
                default: return "Автопилот выключен";
            }
        }
        public float RollCommand { get; private set; }
        public float PitchCommand { get; private set; }

        Gains _g;
        float _pitchTrim;

        public Autopilot(Gains gains) { _g = gains; }

        /// <summary>Limits of engagement: within ±45° bank and ±25° pitch.</summary>
        public static bool CanEngage(float bank, float pitch, out string reason)
        {
            reason = Mathf.Abs(bank) > 45f || Mathf.Abs(pitch) > 25f ? "Автопилот: слишком большой крен или тангаж" : null;
            return reason == null;
        }

        public void Engage(float heading, float altitudeFt, float pitch)
        {
            Engaged = true;
            DisengageCode = 0;
            TargetHeading = Mathf.Repeat(heading, 360f);
            TargetAltitudeFt = altitudeFt;
            _pitchTrim = Mathf.Clamp(pitch, -_g.MaxPitch, _g.MaxPitch);   // hold the attitude it was given, then adjust
            RollCommand = PitchCommand = 0f;
        }

        public void Disengage(byte code)
        {
            if (!Engaged) return;
            Engaged = false;
            DisengageCode = code;
            RollCommand = PitchCommand = 0f;
        }

        public void Step(float heading, float bank, float rollRate, float pitch, float pitchRate, float altitudeFt, float vsFpm, float dt)
        {
            if (!Engaged || dt <= 0f) return;
            if (Mathf.Abs(bank) > 60f || Mathf.Abs(pitch) > 35f) { Disengage(Limits); return; }

            // lateral: heading error → bank target → aileron (proportional on bank, damped on roll rate)
            float headingError = Mathf.DeltaAngle(heading, TargetHeading);
            float bankTarget = Mathf.Clamp(_g.BankPerHeadingDeg * headingError, -_g.MaxBank, _g.MaxBank);
            RollCommand = Mathf.Clamp(_g.RollP * (bankTarget - bank) - _g.RollD * rollRate, -_g.RollAuthority, _g.RollAuthority);

            // vertical: altitude error → vertical speed target → pitch target (with a slow trim integrator) → elevator
            float vsTarget = Mathf.Clamp(_g.VsPerFt * (TargetAltitudeFt - altitudeFt), -_g.MaxVs, _g.MaxVs);
            float vsError = vsTarget - vsFpm;
            _pitchTrim = Mathf.Clamp(_pitchTrim + _g.PitchIntegral * vsError * dt, -_g.MaxPitch, _g.MaxPitch);
            float pitchTarget = Mathf.Clamp(_pitchTrim + _g.PitchPerFpm * vsError, -_g.MaxPitch, _g.MaxPitch);
            float noseUp = _g.PitchP * (pitchTarget - pitch) - _g.PitchD * pitchRate;
            PitchCommand = Mathf.Clamp(-noseUp, -_g.PitchAuthority, _g.PitchAuthority);   // pitch axis: + = push
        }
    }
}
