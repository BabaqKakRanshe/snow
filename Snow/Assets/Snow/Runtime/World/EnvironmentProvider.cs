using System;
using System.Globalization;
using Snow.Net;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Snow.World
{
    /// <summary>
    /// Air at a point of the world (§6 EnvironmentProvider, WEATHER-01): wind, temperature, pressure, density. The
    /// server's physics asks it at each aerodynamic point, so conditions follow the aircraft's place, not a camera, and
    /// the same wind is used once per point (PHYS-02.2). Wind = steady wind + gusts along it + turbulence that varies
    /// across space (so a span of 14 m feels it differently: the storm rolls and yaws the aircraft through physics).
    /// No reduction near the ground (PHYS-02.7). Atmosphere: ISA lapse from a sea-level temperature and pressure.
    ///
    /// The settings are server state, replicated so every peer's windsock agrees. TEMPORARY test control (developer
    /// tool): F9 on the host opens a panel with presets; -snow-wind KT,FROMDEG,GUSTKT,TURBMS on the command line.
    /// </summary>
    public class EnvironmentProvider : NetworkBehaviour
    {
        public struct Settings : INetworkSerializable, IEquatable<Settings>
        {
            /// <summary>m/s steady wind; direction it blows FROM, degrees clockwise from +Z (north).</summary>
            public float SpeedMs, FromDeg;
            /// <summary>m/s extra speed in gusts; m/s turbulence intensity per axis.</summary>
            public float GustMs, TurbulenceMs;
            /// <summary>°C at sea level, hPa at sea level.</summary>
            public float SeaLevelC, SeaLevelHpa;

            public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
            {
                s.SerializeValue(ref SpeedMs); s.SerializeValue(ref FromDeg);
                s.SerializeValue(ref GustMs); s.SerializeValue(ref TurbulenceMs);
                s.SerializeValue(ref SeaLevelC); s.SerializeValue(ref SeaLevelHpa);
            }

            public bool Equals(Settings o) => SpeedMs == o.SpeedMs && FromDeg == o.FromDeg && GustMs == o.GustMs &&
                                              TurbulenceMs == o.TurbulenceMs && SeaLevelC == o.SeaLevelC && SeaLevelHpa == o.SeaLevelHpa;
        }

        public const float Kt = 0.514444f;

        [Tooltip("TEMPORARY start conditions")] public Settings initial = new Settings { SeaLevelC = -10f, SeaLevelHpa = 1013.25f };
        [Tooltip("m, turbulence length scale")] public float turbulenceScale = 40f;
        [Tooltip("s, gust period scale")] public float gustPeriod = 7f;

        readonly NetworkVariable<Settings> _settings = new NetworkVariable<Settings>(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        bool _panel;
        public static EnvironmentProvider Current { get; private set; }
        public Settings Now => IsSpawned ? _settings.Value : initial;

        void Awake() => Current = this;

        public override void OnDestroy()
        {
            if (Current == this) Current = null;
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            Settings s = initial;
            string arg = SnowNetworkBootstrap.Args != null ? SnowNetworkBootstrap.Args.Wind : null;
            if (!string.IsNullOrEmpty(arg))
            {
                string[] p = arg.Split(',');
                float F(int i) => i < p.Length ? float.Parse(p[i], CultureInfo.InvariantCulture) : 0f;
                s.SpeedMs = F(0) * Kt; s.FromDeg = F(1); s.GustMs = F(2) * Kt; s.TurbulenceMs = F(3);
            }
            Apply(s);
        }

        /// <summary>Server: new conditions.</summary>
        public void Apply(Settings s)
        {
            if (!IsServer) return;
            _settings.Value = s;
            BenchLog.Event("weather", "windKt", s.SpeedMs / Kt, "from", s.FromDeg, "gustKt", s.GustMs / Kt, "turb", s.TurbulenceMs);
        }

        /// <summary>Session time both server and clients agree on (for gusts and turbulence).</summary>
        double Time => NetworkManager != null && NetworkManager.IsListening ? NetworkManager.ServerTime.Time : UnityEngine.Time.timeAsDouble;

        // ---------------------------------------------------------------- queries (any peer)
        /// <summary>Velocity of the air at a world point, m/s.</summary>
        public Vector3 Wind(Vector3 p)
        {
            Settings s = Now;
            if (s.SpeedMs <= 0f && s.GustMs <= 0f && s.TurbulenceMs <= 0f) return Vector3.zero;
            float t = (float)(Time % 10000.0);
            float to = (s.FromDeg + 180f) * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Sin(to), 0f, Mathf.Cos(to));
            float gust = s.GustMs * Mathf.Clamp01(Mathf.PerlinNoise(t / gustPeriod, 3.7f) * 1.6f - 0.3f);
            Vector3 w = dir * (s.SpeedMs + gust);
            if (s.TurbulenceMs > 0f)
            {
                // frozen-ish field carried with the wind, evolving slowly
                Vector3 q = (p - dir * (s.SpeedMs * t)) / turbulenceScale;
                float e = t * 0.15f;
                w.x += s.TurbulenceMs * (Mathf.PerlinNoise(q.x + e, q.z + 11.3f + q.y * 0.5f) * 2f - 1f) * 1.4f;
                w.z += s.TurbulenceMs * (Mathf.PerlinNoise(q.z + 23.1f, q.x - e + q.y * 0.5f) * 2f - 1f) * 1.4f;
                w.y += 0.6f * s.TurbulenceMs * (Mathf.PerlinNoise(q.x + 47.9f + q.y * 0.5f, q.z + e) * 2f - 1f) * 1.4f;
            }
            return w;
        }

        public float TemperatureC(float heightM) => Now.SeaLevelC - 0.0065f * heightM;

        public float PressurePa(float heightM)
        {
            float t0 = Now.SeaLevelC + 273.15f;
            return Now.SeaLevelHpa * 100f * Mathf.Pow(Mathf.Max(0.1f, 1f - 0.0065f * heightM / t0), 5.2559f);
        }

        public float Density(float heightM) => PressurePa(heightM) / (287.05f * (TemperatureC(heightM) + 273.15f));

        /// <summary>Fallbacks when no provider is in the scene: still air, ISA -10 °C.</summary>
        public static Vector3 WindAt(Vector3 p) => Current != null ? Current.Wind(p) : Vector3.zero;
        public static float DensityAt(float h) => Current != null ? Current.Density(h) : 1.27f * Mathf.Pow(Mathf.Max(0.1f, 1f - 2.26e-5f * h), 4.256f);
        public static float PressureAt(float h) => Current != null ? Current.PressurePa(h) : 101325f * Mathf.Pow(Mathf.Max(0.1f, 1f - 2.26e-5f * h), 5.2559f);
        public static float TemperatureAt(float h) => Current != null ? Current.TemperatureC(h) : -10f - 0.0065f * h;

        // ---------------------------------------------------------------- developer panel (host)
        void Update()
        {
            if (IsServer && Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame) _panel = !_panel;
        }

        void OnGUI()
        {
            if (!_panel || !IsServer) return;
            Settings s = Now;
            GUILayout.BeginArea(new Rect(Screen.width - 330, 20, 310, 330), "Погода (dev, F9)", GUI.skin.window);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Штиль")) s = new Settings { SeaLevelC = s.SeaLevelC, SeaLevelHpa = s.SeaLevelHpa };
            if (GUILayout.Button("Бок. 15 уз")) { s.SpeedMs = 15f * Kt; s.FromDeg = 90f; s.GustMs = 0f; s.TurbulenceMs = 0.5f; }
            if (GUILayout.Button("Буря")) { s.SpeedMs = 35f * Kt; s.FromDeg = 60f; s.GustMs = 15f * Kt; s.TurbulenceMs = 4f; }
            GUILayout.EndHorizontal();
            GUILayout.Label($"Ветер {s.SpeedMs / Kt:0} уз");
            s.SpeedMs = GUILayout.HorizontalSlider(s.SpeedMs / Kt, 0f, 60f) * Kt;
            GUILayout.Label($"Откуда {s.FromDeg:0}° (0 = с +Z)");
            s.FromDeg = Mathf.Round(GUILayout.HorizontalSlider(s.FromDeg, 0f, 359f));
            GUILayout.Label($"Порывы +{s.GustMs / Kt:0} уз");
            s.GustMs = GUILayout.HorizontalSlider(s.GustMs / Kt, 0f, 30f) * Kt;
            GUILayout.Label($"Турбулентность {s.TurbulenceMs:0.0} м/с");
            s.TurbulenceMs = GUILayout.HorizontalSlider(s.TurbulenceMs, 0f, 8f);
            GUILayout.Label($"Температура у земли {s.SeaLevelC:0} °C");
            s.SeaLevelC = Mathf.Round(GUILayout.HorizontalSlider(s.SeaLevelC, -40f, 30f));
            GUILayout.EndArea();
            if (!s.Equals(Now)) Apply(s);
        }
    }
}
