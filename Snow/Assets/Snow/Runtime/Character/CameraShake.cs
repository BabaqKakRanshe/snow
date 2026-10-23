using UnityEngine;

namespace Snow.Character
{
    /// <summary>
    /// Camera shake (user 23.10, realism list item 3). Callers add "trauma" 0..1 that fades by itself; the shake is trauma²,
    /// so small kicks stay small and a big hit is violent (Eiserloh, GDC 2016 "Math for Game Programmers: Juicing Your
    /// Cameras With Math"). Rotation only: moving the eye could push it into a wall. Smooth Perlin noise, not random jumps.
    /// Local to this peer: whatever happens near the own camera calls <see cref="AddAt"/>. FirstPersonView puts
    /// <see cref="Step"/> after the look every frame. Numbers TEMPORARY.
    /// </summary>
    public static class CameraShake
    {
        /// <summary>Off on the pult page «Эффекты».</summary>
        public static bool Enabled = true;

        static readonly Vector3 DefaultAngles = new Vector3(4f, 4f, 6f);   // first: static fields initialise in text order
        const float DefaultDecay = 1.4f, DefaultFrequency = 18f;

        /// <summary>Degrees at full trauma: pitch, yaw, roll (tuned on the pult page «Эффекты»).</summary>
        public static Vector3 MaxAngles = DefaultAngles;
        /// <summary>Trauma lost per second; noise speed.</summary>
        public static float Decay = DefaultDecay, Frequency = DefaultFrequency;

        public static void ResetTuning() { MaxAngles = DefaultAngles; Decay = DefaultDecay; Frequency = DefaultFrequency; }

        static float s_trauma, s_time;
        static int s_frame = -1;
        static Quaternion s_rot = Quaternion.identity;

        /// <summary>0..1 now.</summary>
        public static float Trauma => s_trauma;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { s_trauma = 0f; s_time = 0f; s_frame = -1; s_rot = Quaternion.identity; }

        public static void Add(float trauma) => s_trauma = Mathf.Clamp01(s_trauma + Mathf.Max(0f, trauma));

        /// <summary>A kick at a world point: full within a quarter of the radius from the camera, nothing beyond the radius.</summary>
        public static void AddAt(Vector3 at, float trauma, float radius)
        {
            Camera cam = Camera.main;
            if (cam == null || radius <= 0f) return;
            float k = 1f - Mathf.InverseLerp(radius * 0.25f, radius, Vector3.Distance(cam.transform.position, at));
            if (k > 0f) Add(trauma * k);
        }

        /// <summary>The rotation to put after the look this frame (computed once per frame, trauma decays here).</summary>
        public static Quaternion Step()
        {
            if (s_frame == Time.frameCount) return s_rot;
            s_frame = Time.frameCount;
            float dt = Time.deltaTime;
            s_trauma = Mathf.Max(0f, s_trauma - Decay * dt);
            s_time += dt;
            if (!Enabled || s_trauma <= 0f) return s_rot = Quaternion.identity;
            float a = s_trauma * s_trauma * CameraSettings.Shake, t = s_time * Frequency;
            return s_rot = Quaternion.Euler(MaxAngles.x * a * Noise(t, 0f), MaxAngles.y * a * Noise(t, 17.3f), MaxAngles.z * a * Noise(t, 41.9f));
        }

        static float Noise(float t, float seed) => Mathf.PerlinNoise(t, seed) * 2f - 1f;
    }
}
