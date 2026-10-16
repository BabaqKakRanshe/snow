using UnityEngine;

namespace Snow.Aircraft
{
    /// <summary>
    /// One lifting section (a wing panel, a stabiliser half, the fin) at its quarter-chord point (PHYS-02.2-4). Pure C#:
    /// input is the section's velocity relative to the local air in body axes, output the force in body axes.
    ///
    /// Angle of attack comes from the flow, never from speed thresholds. Lift is linear up to the stall angle of the
    /// current flap/aileron deflection; past it the flow separates over stallWidth towards a flat plate that is valid
    /// all round (±90° and reverse flow). Separation and reattachment are first-order lags, and reattachment needs the
    /// angle to come back below the stall angle by a hysteresis margin: the stall is smooth, has hysteresis, and a
    /// wing that dips in a slip stalls first because its own angle is higher. Units: SI, radians.
    /// </summary>
    public class AeroSection
    {
        public readonly string Name;
        public readonly AeroProfile P;
        /// <summary>Quarter-chord point, body (prefab) space, m.</summary>
        public readonly Vector3 Position;
        /// <summary>Unit axes in body space: normal (lift at zero angle), chord (leading edge direction), span.</summary>
        public readonly Vector3 Normal, Chord, Span;

        float _attached = 1f;

        /// <summary>Effective angle of attack of the last step, rad (-pi..pi).</summary>
        public float Alpha { get; private set; }
        /// <summary>Positive stall angle at the last deflection, rad.</summary>
        public float StallAlpha { get; private set; }
        /// <summary>1 = attached flow, 0 = fully separated.</summary>
        public float Attached => _attached;
        public float LastCl { get; private set; }
        public float LastCd { get; private set; }

        public AeroSection(string name, AeroProfile profile, Vector3 position, Vector3 normal, Vector3 chord)
        {
            Name = name;
            P = profile;
            Position = position;
            Normal = normal.normalized;
            Chord = chord.normalized;
            Span = Vector3.Cross(Normal, Chord).normalized;
        }

        /// <summary>
        /// Force on the section, body axes, N. vRel: velocity of the section relative to the local air (includes wind,
        /// rotation and propeller wash), body axes, m/s. deflectionDown: control surface, rad, trailing edge towards
        /// -Normal = more lift. alphaOffset: downwash, rad. groundPhi: ground-effect factor on induced drag, 1 = none.
        /// </summary>
        public Vector3 Force(Vector3 vRel, float rho, float deflectionDown, float alphaOffset, float groundPhi, float dt)
        {
            Vector3 v = vRel - Span * Vector3.Dot(vRel, Span);      // spanwise flow makes no lift
            float v2 = v.sqrMagnitude;
            if (v2 < 0.04f)
            {
                Evaluate(0f, deflectionDown, groundPhi, dt);
                Alpha = 0f;
                return Vector3.zero;
            }
            float alpha = WrapPi(Mathf.Atan2(-Vector3.Dot(v, Normal), Vector3.Dot(v, Chord)) - alphaOffset);
            Alpha = alpha;
            Evaluate(alpha, deflectionDown, groundPhi, dt);
            float q = 0.5f * rho * v2 * P.area;
            // lift is perpendicular to the (downwash-turned) flow in the chord-normal plane, drag along it
            Vector3 liftDir = Normal * Mathf.Cos(alpha) + Chord * Mathf.Sin(alpha);
            Vector3 flowDir = Chord * Mathf.Cos(alpha) - Normal * Mathf.Sin(alpha);
            return q * (LastCl * liftDir - LastCd * flowDir);
        }

        /// <summary>Coefficients at an angle of attack; advances the separation state by dt. Public for EditMode tests.</summary>
        public void Evaluate(float alpha, float deflectionDown, float groundPhi, float dt)
        {
            float geLift = 1f + 0.2f * (1f - groundPhi);             // ground effect: a little more lift slope
            float slope = P.liftSlope * geLift;
            float dCl = P.liftSlope * P.controlTau * deflectionDown;
            float clMax = P.clMax + P.clMaxShift * dCl;
            float clMin = P.clMin + P.clMaxShift * dCl;
            float aPos = (clMax - P.cl0 - dCl) / slope;
            float aNeg = (clMin - P.cl0 - dCl) / slope;
            StallAlpha = aPos;

            bool reversed = Mathf.Abs(alpha) > Mathf.PI * 0.5f;
            float over = alpha >= 0.5f * (aPos + aNeg) ? alpha - aPos : aNeg - alpha;   // > 0 past the stall
            float width = P.stallWidthDeg * Mathf.Deg2Rad;
            float target = reversed ? 0f : Mathf.Clamp01(1f - over / width);
            if (reversed) _attached = 0f;                            // flow from the trailing edge: no attached lift at all
            else if (target > _attached)
            {
                // reattach only once back below the stall angle by the hysteresis margin
                float back = reversed ? 0f : Mathf.Clamp01(1f - (over + P.reattachHysteresisDeg * Mathf.Deg2Rad) / width);
                if (back > _attached) _attached += (back - _attached) * (1f - Mathf.Exp(-dt / Mathf.Max(1e-4f, P.reattachTime)));
            }
            else _attached += (target - _attached) * (1f - Mathf.Exp(-dt / Mathf.Max(1e-4f, P.separationTime)));

            float f = _attached;
            float clLin = P.cl0 + slope * alpha + dCl;
            float s = Mathf.Sin(alpha);
            float clPlate = P.flatPlateCl * Mathf.Sin(2f * alpha);
            LastCl = f * clLin + (1f - f) * clPlate;
            float induced = f * clLin * clLin / (Mathf.PI * P.aspectRatio * P.oswald) * groundPhi;
            LastCd = P.cd0 + P.controlCd2 * deflectionDown * deflectionDown + induced + (1f - f) * P.flatPlateCd * s * s;
        }

        /// <summary>Reset the separation state (spawn, checkpoint load).</summary>
        public void Reset() => _attached = 1f;

        /// <summary>Ground effect factor on induced drag for a height above ground h and span b (McCormick).</summary>
        public static float GroundPhi(float h, float span)
        {
            if (h <= 0f) return 0.05f;
            float x = 16f * h / span;
            return Mathf.Max(0.05f, x * x / (1f + x * x));
        }

        static float WrapPi(float a)
        {
            while (a > Mathf.PI) a -= 2f * Mathf.PI;
            while (a < -Mathf.PI) a += 2f * Mathf.PI;
            return a;
        }
    }
}
