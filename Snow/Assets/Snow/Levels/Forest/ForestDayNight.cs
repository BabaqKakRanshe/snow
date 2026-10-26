using System;
using Snow.Net;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace Snow.Levels.Forest
{
    /// <summary>
    /// Day and night on Forest (user decision 21.10: "нет смены времени суток; игра начинается глубокой ночью"; research 2,
    /// docs/notes/FOREST_RESEARCH_2_2026-10-21.md section 1). The server keeps only the clock - the hour at a server time,
    /// the day rate and the night speed; every peer computes the hour from NetworkManager.ServerTime, so nothing is sent per
    /// frame. The session starts at 02:00.
    ///  - One directional light is the sun by day and the moon by night: two arcs (sun <see cref="sunRise"/>..<see cref="sunSet"/>,
    ///    moon the rest), the light swaps between them when its intensity is ~0; the sun's colour follows its height
    ///    (colour temperature), shadows fade at the swap. Updated every frame.
    ///  - Ambient, fog colour and the sky are gradients over the hour: night keys are the location's own look (Forest_Look,
    ///    captured at start), dawn / day / dusk keys come from the fields below (TEMPORARY values); fog distances widen by day.
    ///  - The sky is a runtime copy on Snow/Forest/Sky (the SkyIsland core shader stays untouched): its own moon disc, stars
    ///    turning about the pole with the hour.
    ///  - Reflection probes and the default reflection swap between the night and the day set at mid-dawn / mid-dusk; the day
    ///    grade Volume's weight follows the day factor.
    ///  - Hooks: <see cref="DayChanged"/> at dawn and dusk (on every peer), <see cref="Threat"/> by hour for the server, a
    ///    night speed multiplier (night hours pass faster or slower, as DayZ does), day length presets and a local night
    ///    brightness setting (cheat menu; PlayerPrefs).
    /// The opening's morning (ForestOpening phase 2, 06:30) moves the clock there.
    /// Darker night (22.10, «ночь слишком яркая: не страшно, всё видно без фар»; three solutions in one pass):
    ///  1. Calibration over Forest_Look (the package data stays as delivered): moonlight, ambient, sky, fog colour and
    ///     reflections of the night, night exposure 0 instead of the look's +0.6 EV.
    ///  2. Night kinds (ForestDayNight.Nights.cs): the server picks dark / normal / bright for the next night at full day.
    ///  3. The player's light leads: canopy cover darkens the ground under the trees (<see cref="ForestCanopy"/>), the eyes
    ///     adapt to the frame (<see cref="ForestEyeAdaptation"/>), the darkest values are crushed to true black so a raised
    ///     monitor gamma shows nothing (ForestDayNight.Post.cs, a runtime Volume).
    /// </summary>
    public partial class ForestDayNight : NetworkBehaviour
    {
        [Tooltip("Hour at the start of a session (deep night)")] public float startHour = 2f;
        [Tooltip("Game hours per real minute (0.1667: one game hour in six minutes; TEMPORARY)")] public float hoursPerMinute = 1f / 6f;
        [Tooltip("Night hours pass this much faster (1 = as fast as day)")] public float nightSpeed = 1f;

        [Header("Sun and moon")]
        [Tooltip("The light becomes the sun at this hour (and the moon at sunSet); intensity is ~0 at both")] public float sunRise = 6f;
        public float sunSet = 20f;
        [Tooltip("Highest sun elevation, degrees (north, low winter sun)")] public float sunMaxElevation = 38f;
        public float sunKelvinLow = 1900f, sunKelvinHigh = 5600f;
        public float daySunIntensity = 1.2f;
        [Tooltip("Azimuth the moon travels over the night, degrees")] public float moonArc = 140f;

        [Header("Day (TEMPORARY)")]
        public Color dayAmbientSky = new Color(0.36f, 0.41f, 0.48f), dayAmbientEquator = new Color(0.27f, 0.29f, 0.29f), dayAmbientGround = new Color(0.12f, 0.12f, 0.1f);
        public Color dayFog = new Color(0.32f, 0.36f, 0.39f);
        public Color dayHorizon = new Color(0.5f, 0.56f, 0.6f), dayBand = new Color(0.55f, 0.6f, 0.63f), dayZenith = new Color(0.24f, 0.32f, 0.42f), dayBelow = new Color(0.3f, 0.33f, 0.35f);
        [Tooltip("Linear fog start / end by day, m (night: the location's own)")] public Vector2 dayFogRange = new Vector2(6f, 140f);
        public Color daySunSky = new Color(1f, 0.95f, 0.85f);

        [Header("Dawn and dusk (TEMPORARY)")]
        public Color dawnAmbientSky = new Color(0.3f, 0.27f, 0.3f), dawnAmbientEquator = new Color(0.26f, 0.21f, 0.19f), dawnAmbientGround = new Color(0.09f, 0.08f, 0.07f);
        public Color dawnFog = new Color(0.3f, 0.27f, 0.28f);
        public Color dawnHorizon = new Color(0.62f, 0.42f, 0.33f), dawnBand = new Color(0.7f, 0.5f, 0.38f), dawnZenith = new Color(0.22f, 0.25f, 0.36f), dawnBelow = new Color(0.25f, 0.2f, 0.2f);

        [Header("Night calibration (22.10, over Forest_Look)")]
        [Tooltip("Moonlight at night (the look has 0.6, half the day sun; 0 keeps the look's)")] public float nightMoonIntensity = 0.18f;
        [Tooltip("The look's night ambient and reflections, scaled (linear)")] public float nightAmbientScale = 0.5f;
        [Tooltip("The look's night sky colours, scaled (linear)")] public float nightSkyScale = 0.6f;
        [Tooltip("Night fog colour, linear: darker than the forest, so the fog hides instead of glowing (the look's 0.034, 0.05, 0.058)")]
        public Color nightFog = new Color(0.012f, 0.016f, 0.02f);
        [Tooltip("Post exposure at night, EV (the look's +0.6 lifted the whole night 1.5x); by day the day grade's")] public float nightExposure = 0f;
        [Tooltip("Black crush at night (lift, linear, after exposure): the darkest values become true black")] public float nightCrush = 0.0015f;

        [Header("Canopy (22.10)")]
        [Tooltip("Ambient on the ground under full canopy cover, night / day")] public Vector2 canopyAmbient = new Vector2(0.35f, 0.6f);
        [Tooltip("Share of the moon / sun the cover takes off the ground beyond the trees' shadow distance, night / day")]
        public Vector2 canopyDirect = new Vector2(0.85f, 0.6f);

        [Header("Set by the integration")]
        public Shader forestSky;
        public Volume dayVolume;
        public ReflectionProbe[] probes = new ReflectionProbe[0];
        public Texture[] nightProbeTextures = new Texture[0], dayProbeTextures = new Texture[0];
        public Texture nightReflection, dayReflection;

        [Header("Gameplay")]
        [Tooltip("Threat by hour 0..24 for the server's creatures (1 at night)")]
        public AnimationCurve threatByHour = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(5f, 1f), new Keyframe(7.5f, 0.25f),
                                                                new Keyframe(17.5f, 0.3f), new Keyframe(21f, 1f), new Keyframe(24f, 1f));

        /// <summary>True at dawn (the day starts), false at dusk; raised on every peer when the day factor crosses 0.5.</summary>
        public static event Action<bool> DayChanged;

        /// <summary>Day lengths for the cheat menu / a future lobby: real minutes per game day (at night speed 1).</summary>
        public static readonly (string name, float minutes)[] DayLengths = { ("48 мин", 48f), ("2 ч 24 мин", 144f), ("4 ч 48 мин", 288f), ("24 ч", 1440f) };

        const string BrightnessKey = "snow.forest.nightBrightness";
        /// <summary>Local setting 0..1 (streamers: compressed video eats the dark): lifts the night's ambient and moonlight.</summary>
        public static float NightBrightness
        {
            get => Mathf.Clamp01(PlayerPrefs.GetFloat(BrightnessKey, 0f));
            set { PlayerPrefs.SetFloat(BrightnessKey, Mathf.Clamp01(value)); PlayerPrefs.Save(); }
        }

        readonly NetworkVariable<double> _epoch = new NetworkVariable<double>();   // server time at which the hour was _epochHour
        readonly NetworkVariable<float> _epochHour = new NetworkVariable<float>();
        readonly NetworkVariable<float> _rate = new NetworkVariable<float>();      // game hours per real second (day)
        readonly NetworkVariable<float> _nightMul = new NetworkVariable<float>(1f);

        public static ForestDayNight Instance { get; private set; }

        // the night as the builder made it
        Light _sun;
        Quaternion _nightRot;
        Color _nightSun, _nightSky, _nightEq, _nightGround, _nightFog;
        float _nightIntensity, _nightShadow, _nightStars, _nightFogStart, _nightFogEnd;
        Material _sky, _skyAsset;
        SkyIsland.SkyIslandFogByAltitude _fogCtl;                        // the core's fog by altitude drives the distances each frame
        float _ctlStart, _ctlEnd;
        Gradient _gSky, _gEq, _gGround, _gFog, _gHorizon, _gBand, _gZenith, _gBelow;
        bool _captured, _morningDone, _wasDay, _dayProbes;
        float _shownHour = -100f;
        float _moonIntensity, _reflIntensity;                            // the calibrated moon; the look's reflection intensity
        float[] _probeIntensity = new float[0];
        ForestCanopy _canopy;
        static readonly int Horizon = Shader.PropertyToID("_HorizonColor"), Band = Shader.PropertyToID("_BandColor"), Zenith = Shader.PropertyToID("_ZenithColor"),
                            Below = Shader.PropertyToID("_BelowColor"), SunDir = Shader.PropertyToID("_SunDir"), SunColor = Shader.PropertyToID("_SunColor"),
                            SunDisc = Shader.PropertyToID("_SunDisc"), SunDiscPower = Shader.PropertyToID("_SunDiscPower"), SunGlow = Shader.PropertyToID("_SunGlow"),
                            SunGlowPower = Shader.PropertyToID("_SunGlowPower"), Stars = Shader.PropertyToID("_StarIntensity"), StarAngle = Shader.PropertyToID("_StarAngle"),
                            MoonDir = Shader.PropertyToID("_MoonDir"), MoonColor = Shader.PropertyToID("_MoonColor"), MoonDisc = Shader.PropertyToID("_MoonDisc"),
                            MoonDiscPower = Shader.PropertyToID("_MoonDiscPower"), MoonGlow = Shader.PropertyToID("_MoonGlow"), MoonGlowPower = Shader.PropertyToID("_MoonGlowPower"), StarAxis = Shader.PropertyToID("_StarAxis");

        public float Hour
        {
            get
            {
                if (!IsSpawned) return startHour;
                return Advance(_epochHour.Value, NetworkManager.ServerTime.Time - _epoch.Value, _rate.Value, _nightMul.Value, sunSet, sunRise);
            }
        }

        /// <summary>The server's threat level now (0..1, night 1; a bright night is calmer, see <see cref="NightKind.threat"/>).</summary>
        public float Threat
        {
            get
            {
                float h = Hour;
                return Mathf.Clamp01(threatByHour.Evaluate(h) * Mathf.Lerp(1f, CurrentNight.threat, 1f - DayFactor(h)));
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

        /// <summary>The hour after <paramref name="seconds"/> real seconds from <paramref name="hour"/>: day hours at
        /// <paramref name="rate"/> per second, night hours (nightFrom..24..nightTo) <paramref name="nightMul"/> times faster.
        /// Monotonic and the same on every peer.</summary>
        public static float Advance(float hour, double seconds, float rate, float nightMul, float nightFrom, float nightTo)
        {
            if (rate <= 0f || seconds <= 0d) return Mathf.Repeat(hour, 24f);
            nightMul = Mathf.Max(0.01f, nightMul);
            float nightLen = 24f - nightFrom + nightTo, dayLen = 24f - nightLen;
            double cycle = dayLen / rate + nightLen / (rate * nightMul);
            double t = seconds % cycle;
            double h = Mathf.Repeat(hour, 24f);
            for (int guard = 0; guard < 8 && t > 1e-9; guard++)
            {
                bool night = h >= nightFrom || h < nightTo;
                double speed = rate * (night ? nightMul : 1f);
                double next = night ? (h < nightTo ? nightTo : nightTo + 24d) : nightFrom;
                double dt = (next - h) / speed;
                if (dt >= t) { h += t * speed; t = 0d; }
                else { h = next; t -= dt; }
                h %= 24d;
            }
            return (float)h;
        }

        /// <summary>The sky material this clock drives (its own runtime copy, never the asset), or null.</summary>
        public Material SkyInstance => _sky;

        void Awake()
        {
            Instance = this;
            // the Forest wraps round its edges (ForestEdgeWrap); scenes integrated before 26.10 get it here, without a resave
            if (Application.isPlaying && !TryGetComponent(out ForestEdgeWrap _)) gameObject.AddComponent<ForestEdgeWrap>();
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Restore();
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                _epoch.Value = NetworkManager.ServerTime.Time;
                _epochHour.Value = startHour;
                _rate.Value = hoursPerMinute / 60f;
                _nightMul.Value = nightSpeed;
                _kind.Value = (byte)ClampKind(startNightKind);
                BenchLog.Event("daynight", "start", startHour, "rate_h_per_min", hoursPerMinute, "night_speed", nightSpeed, "night_kind", _kind.Value);
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

        /// <summary>Server: game hours per real minute (by day).</summary>
        public void ServerSetRate(float hoursPerRealMinute)
        {
            if (!IsServer) return;
            _epochHour.Value = Hour;
            _epoch.Value = NetworkManager.ServerTime.Time;
            _rate.Value = hoursPerRealMinute / 60f;
        }

        /// <summary>Server: a whole game day (at night speed 1) takes this many real minutes.</summary>
        public void ServerSetDayLength(float realMinutes) => ServerSetRate(24f / Mathf.Max(1f, realMinutes));

        /// <summary>Server: night hours pass this much faster than day hours.</summary>
        public void ServerSetNightSpeed(float mul)
        {
            if (!IsServer) return;
            _epochHour.Value = Hour;
            _epoch.Value = NetworkManager.ServerTime.Time;
            _nightMul.Value = Mathf.Max(0.01f, mul);
            BenchLog.Event("daynight", "night_speed", _nightMul.Value);
        }

        public float NightSpeed => IsSpawned ? _nightMul.Value : nightSpeed;

        void Capture()
        {
            _captured = true;
            _sun = RenderSettings.sun;
            if (_sun != null)
            {
                _nightRot = _sun.transform.rotation;
                _nightSun = _sun.color;
                _nightIntensity = _sun.intensity;
                _nightShadow = _sun.shadowStrength;
            }
            _moonIntensity = nightMoonIntensity > 0f ? nightMoonIntensity : _nightIntensity;
            _reflIntensity = RenderSettings.reflectionIntensity;
            _nightSky = RenderSettings.ambientSkyColor;
            _nightEq = RenderSettings.ambientEquatorColor;
            _nightGround = RenderSettings.ambientGroundColor;
            _nightFog = RenderSettings.fogColor;
            _nightFogStart = RenderSettings.fogStartDistance;
            _nightFogEnd = RenderSettings.fogEndDistance;
            _fogCtl = FindAnyObjectByType<SkyIsland.SkyIslandFogByAltitude>();
            if (_fogCtl != null && _fogCtl.linear)
            {
                _ctlStart = _fogCtl.groundStart;
                _ctlEnd = _fogCtl.groundEnd;
                _nightFogStart = _ctlStart;
                _nightFogEnd = _ctlEnd;
            }
            else _fogCtl = null;
            _skyAsset = RenderSettings.skybox;
            Color nH = Color.black, nB = Color.black, nZ = Color.black, nBl = Color.black;
            if (_skyAsset != null && _skyAsset.HasProperty(Horizon))
            {
                Shader sh = forestSky != null ? forestSky : Shader.Find("Snow/Forest/Sky");
                _sky = sh != null ? new Material(sh) : new Material(_skyAsset);
                // not CopyPropertiesFromMaterial: it replaces the property sheet with the source's, so the copy's own properties
                // (moon, pole) drop out (HasProperty false, vectors zero, the sky turns black from normalize(0))
                CopyMatching(_skyAsset, _sky);
                _sky.name = _skyAsset.name + " (day/night)";
                RenderSettings.skybox = _sky;
                nH = _skyAsset.GetColor(Horizon);
                nB = _skyAsset.GetColor(Band);
                nZ = _skyAsset.GetColor(Zenith);
                nBl = _skyAsset.HasProperty(Below) ? _skyAsset.GetColor(Below) : nH;
                _nightStars = _skyAsset.HasProperty(Stars) ? _skyAsset.GetFloat(Stars) : 0f;
                // CopyPropertiesFromMaterial zeroes what the SkyIsland sky lacks: the pole (Karelia, ~62° N) and the moon set here
                if (_sky.HasProperty(StarAxis)) _sky.SetVector(StarAxis, new Vector4(0f, 0.883f, 0.469f, 0f));
                if (_sky.HasProperty(MoonDir) && _sun != null) _sky.SetVector(MoonDir, -(_sun.transform.forward));
                if (_sky.HasProperty(MoonDisc))
                {
                    // the location's night sun disc is its moon: it moves to the moon's own disc, the sun's disc takes day values
                    _sky.SetColor(MoonColor, _skyAsset.GetColor(SunColor));
                    _sky.SetFloat(MoonDisc, _skyAsset.GetFloat(SunDisc));
                    _sky.SetFloat(MoonDiscPower, _skyAsset.GetFloat(SunDiscPower));
                    _sky.SetFloat(MoonGlow, _skyAsset.GetFloat(SunGlow));
                    _sky.SetFloat(MoonGlowPower, _skyAsset.GetFloat(SunGlowPower));
                    _sky.SetFloat(SunDisc, 0.6f);
                    _sky.SetFloat(SunDiscPower, 3000f);
                    _sky.SetFloat(SunGlow, 0.1f);
                    _sky.SetFloat(SunGlowPower, 6f);
                }
            }
            // the night keys calibrated (solution 1); the look's own values stay in _night* for Restore
            _gSky = Grad(Lin(_nightSky, nightAmbientScale), dawnAmbientSky.gamma, dayAmbientSky.gamma);
            _gEq = Grad(Lin(_nightEq, nightAmbientScale), dawnAmbientEquator.gamma, dayAmbientEquator.gamma);
            _gGround = Grad(Lin(_nightGround, nightAmbientScale), dawnAmbientGround.gamma, dayAmbientGround.gamma);
            _gFog = Grad(nightFog.gamma, dawnFog.gamma, dayFog.gamma);
            _gHorizon = Grad(Lin(nH, nightSkyScale), dawnHorizon.gamma, dayHorizon.gamma);
            _gBand = Grad(Lin(nB, nightSkyScale), dawnBand.gamma, dayBand.gamma);
            _gZenith = Grad(Lin(nZ, nightSkyScale), dawnZenith.gamma, dayZenith.gamma);
            _gBelow = Grad(Lin(nBl, nightSkyScale), dawnBelow.gamma, dayBelow.gamma);
            if (probes != null && nightProbeTextures != null && dayProbeTextures != null &&
                probes.Length == nightProbeTextures.Length && probes.Length == dayProbeTextures.Length)
                _dayProbes = true;
            _probeIntensity = new float[probes != null ? probes.Length : 0];
            for (int i = 0; i < _probeIntensity.Length; i++) _probeIntensity[i] = probes[i] != null ? probes[i].intensity : 1f;
            _wasDay = DayFactor(startHour) > 0.5f;
            if (Application.isPlaying && !Application.isBatchMode)          // a headless bench or server renders nothing
            {
                // solution 3: the cover map under the trees and the eyes (local, nothing over the network)
                if (!TryGetComponent(out _canopy)) _canopy = gameObject.AddComponent<ForestCanopy>();
                if (!TryGetComponent(out _eyes)) _eyes = gameObject.AddComponent<ForestEyeAdaptation>();
            }
        }

        /// <summary>A gamma-space colour (RenderSettings, materials) scaled in linear light.</summary>
        static Color Lin(Color c, float k)
        {
            Color l = c.linear;
            return new Color(l.r * k, l.g * k, l.b * k, c.a).gamma;
        }

        /// <summary>Every property both shaders declare, copied by type; the rest of <paramref name="dst"/> keeps its defaults.</summary>
        static void CopyMatching(Material src, Material dst)
        {
            Shader s = src.shader;
            for (int i = 0; i < s.GetPropertyCount(); i++)
            {
                int id = s.GetPropertyNameId(i);
                if (!dst.HasProperty(id)) continue;
                switch (s.GetPropertyType(i))
                {
                    case ShaderPropertyType.Color: dst.SetColor(id, src.GetColor(id)); break;
                    case ShaderPropertyType.Vector: dst.SetVector(id, src.GetVector(id)); break;
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range: dst.SetFloat(id, src.GetFloat(id)); break;
                    case ShaderPropertyType.Texture:
                        dst.SetTexture(id, src.GetTexture(id));
                        dst.SetTextureScale(id, src.GetTextureScale(id));
                        dst.SetTextureOffset(id, src.GetTextureOffset(id));
                        break;
                    case ShaderPropertyType.Int: dst.SetInteger(id, src.GetInteger(id)); break;
                }
            }
        }

        /// <summary>Night - dawn - day - dusk - night over the hour (8 keys, the Gradient maximum).</summary>
        Gradient Grad(Color night, Color dawn, Color day)
        {
            var g = new Gradient();
            float D(float h) => h / 24f;
            g.SetKeys(new[]
            {
                new GradientColorKey(night, 0f), new GradientColorKey(night, D(sunRise - 1.2f)), new GradientColorKey(dawn, D(sunRise + 0.4f)),
                new GradientColorKey(day, D(sunRise + 2f)), new GradientColorKey(day, D(sunSet - 2.5f)), new GradientColorKey(dawn, D(sunSet - 0.5f)),
                new GradientColorKey(night, D(sunSet + 1f)), new GradientColorKey(night, 1f),
            }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        void Restore()
        {
            if (_skyAsset != null) RenderSettings.skybox = _skyAsset;
            if (_sky != null) { if (Application.isPlaying) Destroy(_sky); else DestroyImmediate(_sky); }
            _sky = null;
            if (!_captured) return;
            if (_sun != null)
            {
                _sun.transform.rotation = _nightRot;
                _sun.color = _nightSun;
                _sun.intensity = _nightIntensity;
                _sun.shadowStrength = _nightShadow;
            }
            RenderSettings.ambientSkyColor = _nightSky;
            RenderSettings.ambientEquatorColor = _nightEq;
            RenderSettings.ambientGroundColor = _nightGround;
            RenderSettings.fogColor = _nightFog;
            RenderSettings.fogStartDistance = _nightFogStart;
            RenderSettings.fogEndDistance = _nightFogEnd;
            if (_fogCtl != null) { _fogCtl.groundStart = _ctlStart; _fogCtl.groundEnd = _ctlEnd; }
            SetProbes(false);
            RenderSettings.reflectionIntensity = _reflIntensity;
            for (int i = 0; i < _probeIntensity.Length && i < probes.Length; i++)
                if (probes[i] != null) probes[i].intensity = _probeIntensity[i];
            if (dayVolume != null) dayVolume.weight = 0f;
            DestroyPost();
            _captured = false;
        }

        /// <summary>Editor: the look at this hour without Play (the integration bakes the day reflection probes with it);
        /// <see cref="EditorRestore"/> puts the location's night back.</summary>
        public void EditorPreview(float hour)
        {
            if (!_captured) Capture();
            UpdateNightKind(DayFactor(hour), 0f);
            Apply(hour, true);
        }

        public void EditorRestore() => Restore();

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
            bool blending = UpdateNightKind(DayFactor(hour), Time.deltaTime);
            bool slow = blending || Mathf.Abs(Mathf.DeltaAngle(hour * 15f, _shownHour * 15f)) >= 0.25f;    // sky, probes, volume: every ~1 game minute
            Apply(hour, slow);
            if (slow) _shownHour = hour;
        }

        void Apply(float hour, bool slow)
        {
            float d = DayFactor(hour);
            float night = 1f - d;
            float bright = Application.isPlaying ? NightBrightness : 0f;

            // sun and moon: one light, two arcs, the swap at ~0 intensity
            float nightLen = 24f - sunSet + sunRise;
            bool sunTime = hour >= sunRise && hour <= sunSet;
            float sunT = Mathf.InverseLerp(sunRise, sunSet, hour);
            float sunUp = sunTime ? Mathf.Sin(Mathf.PI * sunT) : 0f;
            float moonT = Mathf.Repeat(hour - sunSet, 24f) / nightLen;
            float moonUp = sunTime ? 0f : Mathf.Sin(Mathf.PI * Mathf.Clamp01(moonT));
            Vector3 ne = _nightRot.eulerAngles;
            float peakT = Mathf.Repeat(startHour - sunSet, 24f) / nightLen;              // the location's moon at the session's start
            float peakSin = Mathf.Max(0.2f, Mathf.Sin(Mathf.PI * peakT));
            float moonElev = Mathf.Clamp(Mathf.DeltaAngle(0f, ne.x) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(moonT)) / peakSin, -10f, 85f);
            Quaternion moonRot = Quaternion.Euler(moonElev, ne.y + (Mathf.Clamp01(moonT) - peakT) * moonArc, 0f);
            Quaternion sunRot = Quaternion.Euler(sunMaxElevation * Mathf.Sin(Mathf.PI * sunT), Mathf.Lerp(95f, 265f, sunT), 0f);
            float sunK = Mathf.SmoothStep(0f, 1f, sunUp / 0.25f), moonK = Mathf.SmoothStep(0f, 1f, moonUp / 0.2f);
            if (_sun != null)
            {
                if (sunTime)
                {
                    _sun.transform.rotation = sunRot;
                    Color cct = Mathf.CorrelatedColorTemperatureToRGB(Mathf.Lerp(sunKelvinLow, sunKelvinHigh, Mathf.SmoothStep(0f, 1f, sunUp / 0.6f)));
                    _sun.color = cct;
                    _sun.intensity = daySunIntensity * sunK;
                    _sun.shadowStrength = _nightShadow * sunK;
                }
                else
                {
                    _sun.transform.rotation = moonRot;
                    _sun.color = _nightSun;
                    _sun.intensity = _moonIntensity * _kMoon * moonK * (1f + bright);
                    _sun.shadowStrength = _nightShadow * moonK;
                }
            }

            // ambient and fog every frame (cheap: Trilight, no GI to update)
            float t = hour / 24f;
            float lift = 1f + bright * 1.5f * night;
            float amb = Mathf.Lerp(1f, _kAmbient, night);                                       // the night kind
            RenderSettings.ambientSkyColor = Lin(_gSky.Evaluate(t), amb) * lift;
            RenderSettings.ambientEquatorColor = Lin(_gEq.Evaluate(t), amb) * lift;
            RenderSettings.ambientGroundColor = Lin(_gGround.Evaluate(t), amb) * lift;
            RenderSettings.fogColor = Lin(_gFog.Evaluate(t), Mathf.Lerp(1f, _kFog, night));
            float fs = Mathf.Lerp(_nightFogStart, dayFogRange.x, d), fe = Mathf.Lerp(_nightFogEnd, dayFogRange.y, d);
            if (_fogCtl != null) { _fogCtl.groundStart = fs; _fogCtl.groundEnd = fe; }       // it scales them by altitude and fog events
            else { RenderSettings.fogStartDistance = fs; RenderSettings.fogEndDistance = fe; }
            if (dayVolume != null) dayVolume.weight = d;

            // reflections follow the ambient (the night probes were baked with the look's brighter night)
            float refl = Mathf.Lerp(nightAmbientScale * _kAmbient, 1f, d) * lift;
            RenderSettings.reflectionIntensity = _reflIntensity * refl;
            for (int i = 0; i < _probeIntensity.Length && i < probes.Length; i++)
                if (probes[i] != null) probes[i].intensity = _probeIntensity[i] * refl;
            if (_canopy != null) _canopy.SetStrength(Mathf.Lerp(canopyAmbient.x, canopyAmbient.y, d), Mathf.Lerp(canopyDirect.x, canopyDirect.y, d));
            if (Application.isPlaying) UpdatePost(d, night, bright);

            bool isDay = d > 0.5f;
            if (isDay != _wasDay)
            {
                _wasDay = isDay;
                SetProbes(isDay);
                if (Application.isPlaying)
                {
                    BenchLog.Event("daynight", isDay ? "dawn" : "dusk", hour);
                    DayChanged?.Invoke(isDay);
                }
            }
            if (!slow || _sky == null) return;

            float skyK = Mathf.Lerp(1f, _kSky, night);
            _sky.SetColor(Horizon, Lin(_gHorizon.Evaluate(t), skyK));
            _sky.SetColor(Band, Lin(_gBand.Evaluate(t), skyK));
            _sky.SetColor(Zenith, Lin(_gZenith.Evaluate(t), skyK));
            if (_sky.HasProperty(Below)) _sky.SetColor(Below, Lin(_gBelow.Evaluate(t), skyK));
            if (_sky.HasProperty(Stars)) _sky.SetFloat(Stars, _nightStars * night * _kStars);
            if (_sky.HasProperty(StarAngle)) _sky.SetFloat(StarAngle, t * Mathf.PI * 2f);
            _sky.SetVector(SunDir, -(sunRot * Vector3.forward));
            _sky.SetColor(SunColor, daySunSky.gamma * sunK);
            if (_sky.HasProperty(MoonDir))
            {
                _sky.SetVector(MoonDir, -(moonRot * Vector3.forward));
                // the moon fades by day and below the horizon
                Color baseMoon = _skyAsset != null && _skyAsset.HasProperty(SunColor) ? _skyAsset.GetColor(SunColor) : Color.white;
                _sky.SetColor(MoonColor, baseMoon * moonK * night * _kDisc);
            }
        }

        void SetProbes(bool day)
        {
            if (_dayProbes)
                for (int i = 0; i < probes.Length; i++)
                    if (probes[i] != null)
                    {
                        Texture tex = day ? dayProbeTextures[i] : nightProbeTextures[i];
                        if (tex != null) probes[i].customBakedTexture = tex;
                    }
            Texture refl = day ? dayReflection : nightReflection;
            if (refl != null) RenderSettings.customReflectionTexture = refl;
        }
    }
}
