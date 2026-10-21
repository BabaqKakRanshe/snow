using Snow.Net;
using Unity.Netcode;
using UnityEngine;

namespace Snow.Levels.Forest
{
    /// <summary>
    /// Day and night on Forest (user decision 21.10: "нет смены времени суток; игра начинается глубокой ночью"). The server
    /// keeps only the clock — the hour at a server time and the rate; every peer computes the hour from NetworkManager.ServerTime,
    /// so nothing is sent per frame. The session starts at 02:00. The night is the location's own look (moon, sky, fog, ambient,
    /// as the builder set it from Forest_Look.json), captured at start; the day preset is TEMPORARY: low sun, grey overcast
    /// sky, light fog. Dawn 05:00–07:00, dusk 18:30–20:30. The opening's morning (ForestOpening phase 2, 06:30) moves the clock
    /// there. The skybox is a runtime copy, the asset is not touched.
    /// </summary>
    public class ForestDayNight : NetworkBehaviour
    {
        [Tooltip("Hour at the start of a session (deep night)")] public float startHour = 2f;
        [Tooltip("Game hours per real minute (0.1667: one game hour in six minutes; TEMPORARY)")] public float hoursPerMinute = 1f / 6f;

        [Header("Day (TEMPORARY)")]
        public Color daySun = new Color(1f, 0.93f, 0.82f);
        public float daySunIntensity = 1.2f;
        public Color dayAmbientSky = new Color(0.36f, 0.41f, 0.48f), dayAmbientEquator = new Color(0.27f, 0.29f, 0.29f), dayAmbientGround = new Color(0.12f, 0.12f, 0.1f);
        public Color dayFog = new Color(0.32f, 0.36f, 0.39f);
        public Color dayHorizon = new Color(0.5f, 0.56f, 0.6f), dayBand = new Color(0.55f, 0.6f, 0.63f), dayZenith = new Color(0.24f, 0.32f, 0.42f);

        readonly NetworkVariable<double> _epoch = new NetworkVariable<double>();   // server time at which the hour was _epochHour
        readonly NetworkVariable<float> _epochHour = new NetworkVariable<float>();
        readonly NetworkVariable<float> _rate = new NetworkVariable<float>();      // game hours per real second

        public static ForestDayNight Instance { get; private set; }

        // the night as the builder made it
        Light _sun;
        Quaternion _nightRot;
        Color _nightSun, _nightSky, _nightEq, _nightGround, _nightFog, _nightHorizon, _nightBand, _nightZenith;
        float _nightIntensity, _nightStars;
        Material _sky, _skyAsset;
        bool _captured, _morningDone;
        float _shownDay = -1f, _shownHour = -1f;
        static readonly int Horizon = Shader.PropertyToID("_HorizonColor"), Band = Shader.PropertyToID("_BandColor"), Zenith = Shader.PropertyToID("_ZenithColor"),
                            SunDir = Shader.PropertyToID("_SunDir"), SunColor = Shader.PropertyToID("_SunColor"), Stars = Shader.PropertyToID("_StarIntensity");

        public float Hour
        {
            get
            {
                if (!IsSpawned) return startHour;
                double h = _epochHour.Value + (NetworkManager.ServerTime.Time - _epoch.Value) * _rate.Value;
                return (float)(((h % 24d) + 24d) % 24d);
            }
        }

        /// <summary>0 at night, 1 by day.</summary>
        public static float DayFactor(float hour)
        {
            if (hour >= 7f && hour <= 18.5f) return 1f;
            if (hour > 5f && hour < 7f) return Mathf.SmoothStep(0f, 1f, (hour - 5f) / 2f);
            if (hour > 18.5f && hour < 20.5f) return Mathf.SmoothStep(1f, 0f, (hour - 18.5f) / 2f);
            return 0f;
        }

        void Awake() => Instance = this;

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_skyAsset != null) RenderSettings.skybox = _skyAsset;
            if (_sky != null) Destroy(_sky);
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                _epoch.Value = NetworkManager.ServerTime.Time;
                _epochHour.Value = startHour;
                _rate.Value = hoursPerMinute / 60f;
                BenchLog.Event("daynight", "start", startHour, "rate_h_per_min", hoursPerMinute);
            }
        }

        /// <summary>Server: the clock to this hour (cheat menu, the opening's morning).</summary>
        public void ServerSetHour(float hour)
        {
            if (!IsServer) return;
            _epoch.Value = NetworkManager.ServerTime.Time;
            _epochHour.Value = Mathf.Repeat(hour, 24f);
            BenchLog.Event("daynight", "set", _epochHour.Value);
        }

        /// <summary>Server: game hours per real minute.</summary>
        public void ServerSetRate(float hoursPerRealMinute)
        {
            if (!IsServer) return;
            _epochHour.Value = Hour;
            _epoch.Value = NetworkManager.ServerTime.Time;
            _rate.Value = hoursPerRealMinute / 60f;
        }

        void Capture()
        {
            _captured = true;
            _sun = RenderSettings.sun;
            if (_sun != null)
            {
                _nightRot = _sun.transform.rotation;
                _nightSun = _sun.color;
                _nightIntensity = _sun.intensity;
            }
            _nightSky = RenderSettings.ambientSkyColor;
            _nightEq = RenderSettings.ambientEquatorColor;
            _nightGround = RenderSettings.ambientGroundColor;
            _nightFog = RenderSettings.fogColor;
            _skyAsset = RenderSettings.skybox;
            if (_skyAsset != null && _skyAsset.HasProperty(Horizon))
            {
                _sky = new Material(_skyAsset) { name = _skyAsset.name + " (day/night)" };
                RenderSettings.skybox = _sky;
                _nightHorizon = _sky.GetColor(Horizon);
                _nightBand = _sky.GetColor(Band);
                _nightZenith = _sky.GetColor(Zenith);
                _nightStars = _sky.HasProperty(Stars) ? _sky.GetFloat(Stars) : 0f;
            }
        }

        void LateUpdate()
        {
            if (!IsSpawned) return;
            if (!_captured) Capture();
            float hour = Hour;
            // the opening's morning: 06:30 (X11), once
            if (IsServer && !_morningDone && ForestOpening.Instance != null && ForestOpening.Instance.Phase == 2)
            {
                _morningDone = true;
                if (hour > 18f || hour < 6.5f) { ServerSetHour(6.5f); hour = 6.5f; }
            }
            float d = DayFactor(hour);
            if (Mathf.Abs(d - _shownDay) < 0.002f && Mathf.Abs(Mathf.DeltaAngle(hour * 15f, _shownHour * 15f)) < 0.5f) return;
            _shownDay = d;
            _shownHour = hour;

            if (_sun != null)
            {
                // by day a low sun crossing the south (elevation up to ~40°); at night the location's moon
                float t = Mathf.Clamp01((hour - 6f) / 12f);
                Quaternion day = Quaternion.Euler(10f + Mathf.Sin(t * Mathf.PI) * 32f, Mathf.Lerp(100f, 260f, t), 0f);
                _sun.transform.rotation = Quaternion.Slerp(_nightRot, day, d);
                _sun.color = Color.Lerp(_nightSun, daySun, d);
                _sun.intensity = Mathf.Lerp(_nightIntensity, daySunIntensity, d);
            }
            RenderSettings.ambientSkyColor = Color.Lerp(_nightSky, dayAmbientSky.gamma, d);
            RenderSettings.ambientEquatorColor = Color.Lerp(_nightEq, dayAmbientEquator.gamma, d);
            RenderSettings.ambientGroundColor = Color.Lerp(_nightGround, dayAmbientGround.gamma, d);
            RenderSettings.fogColor = Color.Lerp(_nightFog, dayFog.gamma, d);
            if (_sky != null)
            {
                _sky.SetColor(Horizon, Color.Lerp(_nightHorizon, dayHorizon.gamma, d));
                _sky.SetColor(Band, Color.Lerp(_nightBand, dayBand.gamma, d));
                _sky.SetColor(Zenith, Color.Lerp(_nightZenith, dayZenith.gamma, d));
                if (_sky.HasProperty(Stars)) _sky.SetFloat(Stars, Mathf.Lerp(_nightStars, 0f, d));
                if (_sun != null && _sky.HasProperty(SunDir)) _sky.SetVector(SunDir, -_sun.transform.forward);
                if (_sun != null && _sky.HasProperty(SunColor)) _sky.SetColor(SunColor, _sun.color);
            }
        }
    }
}
