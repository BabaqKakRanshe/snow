using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UtilityPlane
{
    /// <summary>
    /// v11 lighting of the Utility Plane (exterior and cabin). The game API is unchanged: your code writes the actual
    /// outputs 0..1 into navLights, strobeLights, beaconLights, landingLight, cabinLight, cargoLight, floodLight, panelLight
    /// and busPowered, as before. What v11 does with them:
    ///  - every lamp is a spot light shaped by a cookie made from its real emission pattern (aviation sectors: red left /
    ///    green right 110 deg, white tail 140 deg; strobes outboard; rotating beacon fans; PAR landing lamp with a hot core
    ///    and a ground skirt; dome, fluorescent cargo strip and glareshield flood aimed where they light in a real cabin);
    ///  - lamp physics: tungsten filaments warm up / cool down (tau 60-250 ms) and redden as they dim (colour temperature
    ///    follows the filament), xenon strobes fire a double flash that slows down and stops at low voltage, the beacon
    ///    turns at 45 rpm and slows with the bus, the cargo strip is a fluorescent tube that flickers when it strikes and
    ///    fails to start at low voltage;
    ///  - glow of each exterior lamp in fog and snow (UtilityPlane/Airlight) and visible beams of the landing light and
    ///    the beacons (UtilityPlane/Beam), both scaled by <see cref="atmosphere"/>;
    ///  - shadows from the landing light and the cabin lights (shadowQuality), so cabin light leaves only through the
    ///    windows and the landing light throws the shadows of whoever walks in front of the plane;
    ///  - horror hooks: lightFlicker (unstable electrics on every lamp) and FlickerBurst().
    /// </summary>
    public partial class UtilityPlaneRig
    {
        public enum LightShadowQuality { Off, Low, Medium, High }

        [Header("v11 lighting: atmosphere, glow, beams")]
        [Range(0, 1), Tooltip("Fog / falling snow density for the lamp glows and beams: 0 clear night, 0.25 mist, 1 blizzard. " +
                              "Drive it from your weather.")]
        public float atmosphere = 0.25f;
        [Tooltip("Take atmosphere from the scene fog instead (RenderSettings.fogDensity x atmosphereFogScale, Exponential / " +
                 "Exponential Squared fog only; Sky Island 0.0036 x 40 = 0.14)")]
        public bool atmosphereFromSceneFog;
        public float atmosphereFogScale = 40f;
        [Tooltip("Glow of the exterior lamps in the air (needs the URP Depth Texture)")] public bool glows = true;
        [Tooltip("Visible beams of the landing light and the beacons (needs the URP Depth Texture)")] public bool beams = true;
        [Range(0, 4)] public float glowIntensity = 1f;
        [Range(0, 4)] public float beamIntensity = 1f;
        [Tooltip("Off: no lamp casts shadows. Low: landing light. Medium: + dome, cargo and flood lights (default). " +
                 "High: + both beacons.")]
        public LightShadowQuality shadowQuality = LightShadowQuality.Medium;

        [Header("v11 lamps")]
        [Tooltip("Rotating beacons, revolutions per minute at full voltage")] public float beaconRpm = 45f;
        [Tooltip("Glareshield flood: aviation red (night) or warm white")] public bool floodRed = true;
        [Tooltip("Cargo strip is a fluorescent tube: flickers when it strikes, fails to start at low voltage. Off = incandescent.")]
        public bool cargoFluorescent = true;
        [Tooltip("Tungsten warm-up / cool-down and colour shift. Off = lamps follow the levels instantly.")]
        public bool lampInertia = true;
        [Range(0, 1), Tooltip("Horror: unstable electrics. Every lamp sags and drops out at random (0 = steady).")]
        public float lightFlicker;
        [Tooltip("Seed of the flicker pattern. Same seed + same flickerClock on every peer = the same pattern.")]
        public int flickerSeed;
        [Tooltip(">= 0: use this clock (s) for the flicker pattern instead of the local one, e.g. your network time since the " +
                 "session started (a float: keep it small, not an epoch time)")]
        public float flickerClock = -1f;

        [Header("v11 assets (Tools/Utility Plane/Build Materials + Prefabs fills these)")]
        public Material glowMaterial;
        public Material beamMaterial;
        public Texture2D[] lightCookies = new Texture2D[0];
        [Tooltip("Lights in this prefab were built by this package version. Below 11 the rig rebuilds the Light_* objects " +
                 "on Awake (old prefab in a scene).")]
        public int lightsVersion;

        /// <summary>Flicker every lamp for <paramref name="seconds"/> (strength 0..1, fades out over the last third).
        /// Deterministic for a given flickerSeed: send it as an event to every peer.</summary>
        public void FlickerBurst(float seconds, float strength = 1f)
        {
            _burstStart = LightClock;
            _burstEnd = _burstStart + Mathf.Max(0f, seconds);
            _burstStrength = Mathf.Clamp01(strength);
        }

        /// <summary>True for the glow / beam renderers the rig makes at run time (Glow_&lt;key&gt;, Beam_&lt;key&gt;): code that walks
        /// the plane's renderers (outline, hide the model, snow) should skip them. To hide the plane with its lights, set
        /// glows = beams = false as well (the rig switches its own renderers).</summary>
        public static bool IsEffectRenderer(Renderer r) =>
            r != null && (r.name.StartsWith("Glow_", System.StringComparison.Ordinal) ||
                          r.name.StartsWith("Beam_", System.StringComparison.Ordinal));

        /// <summary>Current relative output 0..1 of a lamp (after inertia, flicker, strobe / beacon timing), e.g. to sync
        /// audio (strobe tick) or to light your own effects. Keys: see LightDefs.</summary>
        public float LampOutput(string key) => _lampByKey.TryGetValue(key, out var lp) ? lp.output : 0f;

        // ------------------------------------------------------------------ definitions
        public enum LampGroup { Nav, Strobe, Beacon, Landing, Cabin, Cargo, Flood }
        public enum LampKind { Incandescent, Xenon, Fluorescent }

        public struct GlowDef
        {
            public bool on, lightFrame;
            public Vector3 patternForward;                     // plane frame; ignored when lightFrame
            public float sector, soft, elevPower, beamWidth, fanWidth, elLo, elHi, floor, coreSize, coreGain, halo, iso,
                         radiusScale, radiusMax;
        }

        public struct BeamDef
        {
            public bool on;
            public float length, xMin, xMax, yMin, yMax, coreWidth, skirt, fanWidth, elLo, elHi, gain, g;
        }

        /// <summary>One lamp. Directions in the plane frame (= socket frame: +X right, +Y up, +Z forward).
        /// Intensity: URP units (100 % level; the prefab's Light.intensity overrides it), hdrpCandela for HDRP.</summary>
        public struct LightDef
        {
            public string key, socket, lens;
            public LampGroup group;
            public LampKind kind;
            public LightType type;                             // Spot (shaped by its cookie) or Point (cabin bounce fill)
            public Vector3 offset, forward, up;
            public float outer, inner, range, intensity, hdrpCandela, kelvin, nearPlane, tauOn, tauOff, lensGain;
            public Color filter;                               // linear
            public int shadowTier;                             // -1 none, 0 low, 1 medium, 2 high (URP tiers)
            public LightShadowQuality shadowFrom;
            public bool rotates;
            public bool cutForward;                            // rotating lamp whose beam would cross the cabin when it points forward
            public GlowDef glow;
            public BeamDef beam;
            public Quaternion Rotation => Quaternion.LookRotation(forward, up);
        }

        static Vector3 Dir(float az, float el)
        {
            float a = az * Mathf.Deg2Rad, e = el * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Cos(a) * Mathf.Cos(e));
        }

        static LightDef Lamp(string key, string socket, string lens, LampGroup group, LampKind kind, Vector3 fwd, Vector3 up,
                             float outer, float inner, float range, float intensity, float hdrpCandela, float kelvin, Color filter,
                             float lensGain, float tauOn = 0.06f, float tauOff = 0.12f, int shadowTier = -1,
                             LightShadowQuality shadowFrom = LightShadowQuality.High, float nearPlane = 0.1f, bool rotates = false,
                             Vector3 offset = default(Vector3), LightType type = LightType.Spot)
        {
            return new LightDef
            {
                key = key, socket = socket, lens = lens, group = group, kind = kind, type = type, forward = fwd.normalized, up = up,
                outer = outer, inner = inner, range = range, intensity = intensity, hdrpCandela = hdrpCandela, kelvin = kelvin,
                filter = filter, lensGain = lensGain, tauOn = tauOn, tauOff = tauOff, shadowTier = shadowTier,
                shadowFrom = shadowFrom, nearPlane = nearPlane, rotates = rotates, offset = offset
            };
        }

        static GlowDef Glow(Vector3 patternForward, float coreSize, float coreGain, float floor, float sector = 180f, float soft = 3f,
                            float elevPower = 0f, float beamWidth = 0f, float fanWidth = 0f, float elLo = -90f, float elHi = 90f,
                            float halo = 1f, float iso = 1f, float radiusScale = 0.5f, float radiusMax = 60f, bool lightFrame = false)
        {
            return new GlowDef
            {
                on = true, lightFrame = lightFrame, patternForward = patternForward, sector = sector, soft = soft,
                elevPower = elevPower, beamWidth = beamWidth, fanWidth = fanWidth, elLo = elLo, elHi = elHi, floor = floor,
                coreSize = coreSize, coreGain = coreGain, halo = halo, iso = iso, radiusScale = radiusScale, radiusMax = radiusMax
            };
        }

        static LightDef WithGlow(LightDef d, GlowDef g) { d.glow = g; return d; }
        static LightDef CutForward(LightDef d) { d.cutForward = true; return d; }
        static LightDef WithBeam(LightDef d, BeamDef b) { d.beam = b; return d; }

        static BeamDef Beam(float length, float xMin, float xMax, float yMin, float yMax, float coreWidth = 0f, float skirt = 0f,
                            float fanWidth = 7f, float elLo = -90f, float elHi = 90f, float gain = 1f, float g = 0.6f)
        {
            return new BeamDef
            {
                on = true, length = length, xMin = xMin, xMax = xMax, yMin = yMin, yMax = yMax, coreWidth = coreWidth,
                skirt = skirt, fanWidth = fanWidth, elLo = elLo, elHi = elHi, gain = gain, g = g
            };
        }

        // lens filters, linear. Coloured lamps use 6500 K at full power (the filter is the emitted colour) and redden only as
        // they dim; white tungsten lamps use their real colour temperature.
        static readonly Color NavRed = new Color(1f, 0.02f, 0.01f), NavGreen = new Color(0.02f, 1f, 0.45f);
        static readonly Color BeaconRed = new Color(1f, 0.06f, 0.012f), XenonWhite = new Color(0.92f, 0.96f, 1f);
        static readonly Color FloodRed = new Color(1f, 0.05f, 0.02f), FluorescentTint = new Color(0.88f, 1f, 0.86f);   // cool white tube, a touch of green

        /// <summary>v11 lamps (same keys as v9 / v10, so Light_&lt;key&gt; names are unchanged).</summary>
        public static readonly LightDef[] LightDefs =
        {
            WithGlow(Lamp("Nav_L", "Socket_Nav_L", "Lens_Nav_L", LampGroup.Nav, LampKind.Incandescent, Dir(-55, -35), Vector3.up,
                          160, 152, 8f, 4f, 40f, 6500, NavRed, 3f),
                     Glow(Dir(-55, 0), 0.035f, 2.2f, 0.06f, sector: 55, elevPower: 2.5f, halo: 0.7f)),
            WithGlow(Lamp("Nav_R", "Socket_Nav_R", "Lens_Nav_R", LampGroup.Nav, LampKind.Incandescent, Dir(55, -35), Vector3.up,
                          160, 152, 8f, 4f, 40f, 6500, NavGreen, 3f),
                     Glow(Dir(55, 0), 0.035f, 2.2f, 0.06f, sector: 55, elevPower: 2.5f, halo: 0.7f)),
            WithGlow(Lamp("Nav_Tail", "Socket_Nav_Tail", "Lens_Nav_Tail", LampGroup.Nav, LampKind.Incandescent, Dir(180, -25),
                          Vector3.up, 160, 152, 7f, 1.5f, 20f, 3000, Color.white, 2.5f),
                     Glow(Dir(180, 0), 0.03f, 2.2f, 0.06f, sector: 70, elevPower: 2.5f, halo: 0.7f)),
            WithGlow(Lamp("Strobe_L", "Socket_Strobe_L", "Lens_Strobe_L", LampGroup.Strobe, LampKind.Xenon, Dir(-90, -15), Vector3.up,
                          170, 164, 26f, 30f, 2000f, 6500, XenonWhite, 25f),
                     Glow(Dir(-90, 0), 0.03f, 2.5f, 0.04f, sector: 100, soft: 20, elevPower: 1.2f, halo: 0.6f, radiusScale: 0.9f)),
            WithGlow(Lamp("Strobe_R", "Socket_Strobe_R", "Lens_Strobe_R", LampGroup.Strobe, LampKind.Xenon, Dir(90, -15), Vector3.up,
                          170, 164, 26f, 30f, 2000f, 6500, XenonWhite, 25f),
                     Glow(Dir(90, 0), 0.03f, 2.5f, 0.04f, sector: 100, soft: 20, elevPower: 1.2f, halo: 0.6f, radiusScale: 0.9f)),
            WithBeam(WithGlow(CutForward(Lamp("Beacon_Top", "Socket_Beacon_Top", "Lens_Beacon_Top", LampGroup.Beacon, LampKind.Incandescent,
                                   Dir(180, -16), Vector3.up, 72, 68, 30f, 25f, 400f, 6500, BeaconRed, 6f, shadowTier: 0,
                                   shadowFrom: LightShadowQuality.High, rotates: true)),
                              Glow(Dir(180, 0), 0.03f, 1.6f, 0.05f, fanWidth: 7f, elLo: -40, elHi: 8, iso: 0f)),
                     Beam(26f, -24, 24, -30, 30, fanWidth: 7f, elLo: -40, elHi: 8)),
            WithBeam(WithGlow(Lamp("Beacon_Bottom", "Socket_Beacon_Bottom", "Lens_Beacon_Bottom", LampGroup.Beacon, LampKind.Incandescent,
                                   Dir(0, -36), Vector3.up, 80, 76, 16f, 8f, 200f, 6500, BeaconRed, 6f, shadowTier: 0,
                                   shadowFrom: LightShadowQuality.High, rotates: true),
                              Glow(Dir(0, 0), 0.04f, 1.6f, 0.05f, fanWidth: 7f, elLo: -62, elHi: -3, iso: 0f)),
                     Beam(12f, -24, 24, -30, 36, fanWidth: 7f, elLo: -62, elHi: -3)),
            WithBeam(WithGlow(Lamp("Landing", "Socket_Landing", "Lens_Landing", LampGroup.Landing, LampKind.Incandescent,
                                   Dir(1.5f, -12), Vector3.up, 78, 74, 160f, 900f, 150000f, 3400, Color.white, 8f, 0.12f, 0.25f,
                                   shadowTier: 1, shadowFrom: LightShadowQuality.Low, nearPlane: 0.2f),
                              Glow(Vector3.forward, 0.05f, 0.35f, 0.015f, beamWidth: 4.5f, iso: 0f, lightFrame: true)),
                     Beam(70f, -32, 32, -38, 26, coreWidth: 4.5f, skirt: 0.012f, g: 0.7f)),
            Lamp("Dome_L", "Socket_Light_Dome_L", "Lens_Dome", LampGroup.Cabin, LampKind.Incandescent, new Vector3(-0.245f, -0.97f, 0.11f),
                 Vector3.forward, 118, 104, 2.3f, 0.5f, 15f, 3000, Color.white, 2.5f, shadowTier: 0,
                 shadowFrom: LightShadowQuality.Medium, nearPlane: 0.08f),
            Lamp("Dome_R", "Socket_Light_Dome_R", null, LampGroup.Cabin, LampKind.Incandescent, new Vector3(0.245f, -0.97f, 0.11f),
                 Vector3.forward, 118, 104, 2.3f, 0.5f, 15f, 3000, Color.white, 0f, shadowTier: 0,
                 shadowFrom: LightShadowQuality.Medium, nearPlane: 0.08f),
            Lamp("Cargo", "Socket_Light_Cargo", "Lens_CargoLight", LampGroup.Cargo, LampKind.Fluorescent, Vector3.down, Vector3.forward,
                 150, 142, 2.7f, 0.7f, 25f, 6500, FluorescentTint, 2.5f, shadowTier: 1, shadowFrom: LightShadowQuality.Medium,
                 nearPlane: 0.08f),
            Lamp("Cargo_2", "Socket_Light_Cargo_2", null, LampGroup.Cargo, LampKind.Fluorescent, Vector3.down, Vector3.forward,
                 150, 142, 2.7f, 0.7f, 25f, 6500, FluorescentTint, 0f, shadowTier: 1, shadowFrom: LightShadowQuality.Medium,
                 nearPlane: 0.08f),
            // panel floods, left and right: lamps on arms from the glareshield lip (PanelFlood_L / _R, lenses Lens_Flood_L / _R),
            // down the panel face from 10 cm behind it
            Lamp("Flood", "Socket_Light_Flood", "Lens_Flood_L", LampGroup.Flood, LampKind.Incandescent, new Vector3(0f, -0.966f, 0.259f),
                 Vector3.forward, 140, 132, 1.5f, 0.3f, 4f, 6500, FloodRed, 2f, shadowTier: 0,
                 shadowFrom: LightShadowQuality.Medium, nearPlane: 0.05f, offset: new Vector3(-0.3f, -0.005f, -0.10f)),
            Lamp("Flood_2", "Socket_Light_Flood", "Lens_Flood_R", LampGroup.Flood, LampKind.Incandescent, new Vector3(0f, -0.966f, 0.259f),
                 Vector3.forward, 140, 132, 1.5f, 0.3f, 4f, 6500, FloodRed, 2f, shadowTier: 0,
                 shadowFrom: LightShadowQuality.Medium, nearPlane: 0.05f, offset: new Vector3(0.3f, -0.005f, -0.10f)),
            // bounce fill: real-time lights get no bounce in URP; a weak shadowless point per cabin area stands in for the light
            // thrown back by seats, floor and walls. Ranges end inside the cabin (no glow on the wing roots or the snow).
            Lamp("Fill_Cockpit", "Socket_Light_Dome_L", null, LampGroup.Cabin, LampKind.Incandescent, Vector3.forward, Vector3.up,
                 179, 179, 1.25f, 0.075f, 2f, 3000, Color.white, 0f, offset: new Vector3(0.085f, -0.42f, 0f), type: LightType.Point),
            Lamp("Fill_Bay", "Socket_Light_Cargo", null, LampGroup.Cargo, LampKind.Fluorescent, Vector3.forward, Vector3.up,
                 179, 179, 2f, 0.1f, 3f, 6500, FluorescentTint, 0f, offset: new Vector3(0f, -0.54f, -0.65f), type: LightType.Point),
        };

        /// <summary>
        /// Creates the Light_&lt;key&gt; objects under their sockets, or rebuilds them when <paramref name="rebuild"/> is set
        /// (v11 shape: spot, angles, cookie, colour temperature, shadows). Used by the editor build step and at run time
        /// for a prefab without v11 lights. The prefab's Light.intensity is the 100 % level (tune it there).
        /// HDRP: HDAdditionalLightData through reflection, intensity in candela (TEMPORARY values).
        /// </summary>
        public static int BuildLights(Transform root, bool hdrp, IList<Texture2D> cookies, bool rebuild = false)
        {
            var byName = new Dictionary<string, Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (!byName.ContainsKey(t.name)) byName[t.name] = t;
            System.Type hdType = hdrp ? FindType("UnityEngine.Rendering.HighDefinition.HDAdditionalLightData") : null;
            int made = 0;
            foreach (var d in LightDefs)
            {
                byName.TryGetValue(d.socket, out Transform socket);
                byName.TryGetValue("Light_" + d.key, out Transform found);
                if (socket == null) continue;
                if (found != null && !rebuild) continue;
                GameObject go = found != null ? found.gameObject : new GameObject("Light_" + d.key);
                go.transform.SetParent(socket, false);
                go.transform.localPosition = d.offset;
                go.transform.localRotation = d.Rotation;
                go.transform.localScale = Vector3.one;
                var l = go.GetComponent<Light>();
                if (l == null) l = go.AddComponent<Light>();
                if (hdType != null)
                {
                    Component hd = go.GetComponent(hdType);                  // no ?? on Unity objects (fake null in the editor)
                    if (hd == null)
                    {
                        hd = go.AddComponent(hdType);
                        try
                        {
                            hdType.GetMethod("InitDefaultHDAdditionalLightData",
                                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                                ?.Invoke(null, new object[] { hd });
                        }
                        catch (System.Exception e) { Debug.LogWarning("[UtilityPlane] HDRP light defaults: " + e.Message); }
                    }
                }
                ApplyDef(l, d, hdrp, FindCookie(cookies, d.key));
                l.enabled = false;
                made++;
            }
            return made;
        }

        static Texture2D FindCookie(IList<Texture2D> cookies, string key)
        {
            if (cookies == null) return null;
            string n = "T_Cookie_" + key;
            foreach (var c in cookies)
                if (c != null && c.name == n) return c;
            return null;
        }

        static void ApplyDef(Light l, LightDef d, bool hdrp, Texture2D cookie)
        {
            l.type = d.type;
            if (d.type == LightType.Spot)
            {
                l.spotAngle = d.outer;
                l.innerSpotAngle = Mathf.Min(d.inner, d.outer);
            }
            l.range = d.range;
            l.cookie = d.type == LightType.Spot ? cookie : null;
            l.shadows = LightShadows.None;
            l.shadowNearPlane = d.nearPlane;
            l.intensity = hdrp ? d.hdrpCandela : d.intensity;
            l.renderMode = LightRenderMode.ForcePixel;
            SetLightColour(l, d.filter, d.kelvin);
        }

        // Light.color is gamma space; the filter is linear. With "lights use colour temperature" off, the temperature is
        // folded into the colour.
        static void SetLightColour(Light l, Color filterLinear, float kelvin)
        {
            if (GraphicsSettings.lightsUseColorTemperature)
            {
                if (!l.useColorTemperature) l.useColorTemperature = true;
                l.colorTemperature = kelvin;
                l.color = filterLinear.gamma;
            }
            else
            {
                if (l.useColorTemperature) l.useColorTemperature = false;
                l.color = Mul(filterLinear, Mathf.CorrelatedColorTemperatureToRGB(kelvin)).gamma;
            }
        }

        static Color Mul(Color a, Color b) => new Color(a.r * b.r, a.g * b.g, a.b * b.b, 1f);

        static System.Type FindType(string fullName)
        {
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }

        // ------------------------------------------------------------------ runtime state
        class LampState
        {
            public LightDef def;
            public Light light;
            public float maxIntensity, filament, output, lastIntensity = -1f, lastKelvin = -1f, lastFilterKey = -1f;
            public bool lastEnabled;
            public Quaternion baseRotation;
            public float angle;                                    // beacon heading, degrees
            public Material lensMat;
            public Renderer glowR, beamR;
            public Material glowMat, beamMat;
            public float lastGlowI = -1f, lastBeamI = -1f, lastDensity = -1f, lastKelvinFx = -1f;
        }

        readonly List<LampState> _lamps = new List<LampState>();
        readonly Dictionary<string, LampState> _lampByKey = new Dictionary<string, LampState>();
        readonly List<Mesh> _fxMeshes = new List<Mesh>();
        float _lightClockLocal, _burstStart, _burstEnd = -1f, _burstStrength;
        float _strobeTimer, _strobeSecond = -1f, _strobeAge = 10f;
        bool _strobeFired;
        float _panelFilament, _lastPanelKey = -1f, _panelOutput;
        readonly float[] _flick = new float[8];                  // per LampGroup (+ 7 = panel backlight)
        bool _lastFluorescent = true;
        int _fluoState, _fluoStrikes;                              // 0 off, 1 striking, 2 running
        float _fluoT, _fluoStrikeLen;
        LightShadowQuality _appliedShadows = (LightShadowQuality)(-1);
        bool _fxOk, _fxWarned;
        int _densityId, _intensityId, _colorId, _upOsId, _hzOsId;

        float LightClock => flickerClock >= 0f ? flickerClock : _lightClockLocal;

        void InitLighting()
        {
            _densityId = Shader.PropertyToID("_Density");
            _intensityId = Shader.PropertyToID("_Intensity");
            _colorId = Shader.PropertyToID("_Color");
            _upOsId = Shader.PropertyToID("_UpOS");
            _hzOsId = Shader.PropertyToID("_HzOS");
            bool outdated = lightsVersion < 11;
            if (outdated && (lightCookies == null || lightCookies.Length == 0))
                Debug.LogWarning("[UtilityPlane] Lights of this plane are from an older package: rebuilt without cookies (unshaped). " +
                                 "Run Tools/Utility Plane/Build Materials + Prefabs and use the rebuilt prefab.");
            if (createLights || outdated) BuildLights(transform, _hdrp, lightCookies, outdated);
            var lightByKey = new Dictionary<string, Light>();
            foreach (var l in GetComponentsInChildren<Light>(true))
                if (l.name.StartsWith("Light_", System.StringComparison.Ordinal)) lightByKey[l.name.Substring(6)] = l;
            if (!_hdrp && GraphicsSettings.currentRenderPipeline != null)
            {
                glowMaterial = UsableFx(glowMaterial) ? glowMaterial : FxTemplate("UtilityPlane/Airlight");
                beamMaterial = UsableFx(beamMaterial) ? beamMaterial : FxTemplate("UtilityPlane/Beam");
            }
            _fxOk = !_hdrp && GraphicsSettings.currentRenderPipeline != null && glowMaterial != null && beamMaterial != null;
            if (!_fxOk && !_fxWarned && GraphicsSettings.currentRenderPipeline != null && !_hdrp)
            {
                _fxWarned = true;
                Debug.LogWarning("[UtilityPlane] v11 glow / beam materials missing: run Tools/Utility Plane/Build Materials + Prefabs.");
            }
            foreach (var d in LightDefs)
            {
                if (!lightByKey.TryGetValue(d.key, out var l) || l == null) continue;
                // a rotating lamp may have been copied mid-turn (Instantiate of a live plane): it restarts from its rest pose
                if (d.rotates) l.transform.localRotation = d.Rotation;
                var s = new LampState
                {
                    def = d, light = l, maxIntensity = l.intensity, baseRotation = l.transform.localRotation, lastEnabled = l.enabled
                };
                if (d.lens != null) _emitMats.TryGetValue(d.lens, out s.lensMat);
                RemoveOldFx(l.transform);
                if (_fxOk) BuildFx(s);
                _lamps.Add(s);
                _lampByKey[d.key] = s;
            }
        }

        static bool UsableFx(Material m) => m != null && m.shader != null && m.shader.isSupported;

        // effect objects copied along with a live plane (Instantiate during play) belong to the source: drop them
        static void RemoveOldFx(Transform light)
        {
            for (int i = light.childCount - 1; i >= 0; i--)
            {
                var c = light.GetChild(i);
                if (c.name.StartsWith("Glow_", System.StringComparison.Ordinal) || c.name.StartsWith("Beam_", System.StringComparison.Ordinal))
                {
                    c.gameObject.SetActive(false);
                    Destroy(c.gameObject);
                }
            }
        }

        Material FxTemplate(string shaderName)
        {
            var sh = Shader.Find(shaderName);
            if (sh == null || !sh.isSupported) return null;
            var m = new Material(sh) { name = shaderName };
            _created.Add(m);
            return m;
        }

        /// <summary>Forget what was last written to the lights and effect materials (ResyncPose calls it).</summary>
        void ResyncLighting()
        {
            foreach (var s in _lamps)
            {
                s.lastIntensity = s.lastKelvin = s.lastFilterKey = -1f;
                s.lastGlowI = s.lastBeamI = s.lastDensity = s.lastKelvinFx = -1f;
                if (s.light != null) s.lastEnabled = !s.light.enabled;
            }
            _lastPanelKey = -1f;
            _appliedShadows = (LightShadowQuality)(-1);
        }

        // rig disabled: nothing would update, so a strobe caught mid-flash and the effects would freeze. Switch them off.
        void SuspendLighting()
        {
            foreach (var s in _lamps)
            {
                if (s.def.kind == LampKind.Xenon && s.light != null) s.light.enabled = false;
                if (s.glowR != null) s.glowR.enabled = false;
                if (s.beamR != null) s.beamR.enabled = false;
            }
            ResyncLighting();
        }

        // ------------------------------------------------------------------ glow quads and beam pyramids
        void BuildFx(LampState s)
        {
            var d = s.def;
            var lt = s.light.transform;
            if (d.glow.on)
            {
                var go = new GameObject("Glow_" + d.key);
                go.transform.SetParent(lt, false);
                // pattern frame: plane-aligned sector frame (or the light frame itself), as a rotation under the light
                Quaternion pattern = d.glow.lightFrame ? s.baseRotation : Quaternion.LookRotation(d.glow.patternForward, Vector3.up);
                go.transform.localRotation = Quaternion.Inverse(s.baseRotation) * pattern;
                var m = new Material(glowMaterial) { name = "FX_Glow_" + d.key };
                _created.Add(m);
                var g = d.glow;
                m.SetFloat("_CoreSize", g.coreSize);
                m.SetFloat("_CoreGain", g.coreGain);
                m.SetFloat("_Halo", g.halo);
                m.SetFloat("_Iso", g.iso);
                m.SetFloat("_Sector", g.sector);
                m.SetFloat("_SectorSoft", g.soft);
                m.SetFloat("_ElevPower", g.elevPower);
                m.SetFloat("_BeamWidth", g.beamWidth);
                m.SetFloat("_FanWidth", g.fanWidth);
                m.SetFloat("_ElLo", g.elLo);
                m.SetFloat("_ElHi", g.elHi);
                m.SetFloat("_Floor", g.floor);
                m.SetFloat("_RadiusScale", g.radiusScale);
                m.SetFloat("_RadiusMax", g.radiusMax);
                m.SetFloat(_intensityId, 0f);
                s.glowMat = m;
                s.glowR = AddFxRenderer(go, QuadMesh(g.radiusMax), m);
            }
            if (d.beam.on)
            {
                var go = new GameObject("Beam_" + d.key);
                go.transform.SetParent(lt, false);
                var b = d.beam;
                var m = new Material(beamMaterial) { name = "FX_Beam_" + d.key };
                _created.Add(m);
                m.SetFloat("_Length", b.length);
                m.SetFloat("_XMin", b.xMin);
                m.SetFloat("_XMax", b.xMax);
                m.SetFloat("_YMin", b.yMin);
                m.SetFloat("_YMax", b.yMax);
                m.SetFloat("_CoreWidth", b.coreWidth);
                m.SetFloat("_Skirt", b.skirt);
                m.SetFloat("_FanWidth", b.fanWidth);
                m.SetFloat("_ElLo", b.elLo);
                m.SetFloat("_ElHi", b.elHi);
                m.SetFloat("_Gain", b.gain);
                m.SetFloat("_G", b.g);
                // the plane's up and the beam's horizontal heading in the light frame (constant while it turns about up)
                Quaternion inv = Quaternion.Inverse(s.baseRotation);
                Vector3 hz = Vector3.ProjectOnPlane(s.baseRotation * Vector3.forward, Vector3.up).normalized;
                Vector3 upL = inv * Vector3.up, hzL = inv * hz;
                m.SetVector(_upOsId, new Vector4(upL.x, upL.y, upL.z, 0f));
                m.SetVector(_hzOsId, new Vector4(hzL.x, hzL.y, hzL.z, 0f));
                m.SetFloat(_intensityId, 0f);
                s.beamMat = m;
                s.beamR = AddFxRenderer(go, PyramidMesh(b), m);
            }
        }

        Renderer AddFxRenderer(GameObject go, Mesh mesh, Material m)
        {
            go.layer = gameObject.layer;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.allowOcclusionWhenDynamic = false;
            r.enabled = false;
            return r;
        }

        Mesh QuadMesh(float radiusMax)
        {
            var mesh = new Mesh { name = "FX_GlowQuad" };
            mesh.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            // the vertex shader moves the corners up to the halo window (or over the whole screen): never cull early
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * (2.5f * Mathf.Max(radiusMax, 1f)));
            _fxMeshes.Add(mesh);
            return mesh;
        }

        // closed pyramid, apex at the lamp, every face wound outwards (the shader draws the back faces)
        Mesh PyramidMesh(BeamDef b)
        {
            float tx0 = Mathf.Tan(b.xMin * Mathf.Deg2Rad), tx1 = Mathf.Tan(b.xMax * Mathf.Deg2Rad);
            float ty0 = Mathf.Tan(b.yMin * Mathf.Deg2Rad), ty1 = Mathf.Tan(b.yMax * Mathf.Deg2Rad);
            float L = b.length;
            var v = new[]
            {
                Vector3.zero,
                new Vector3(tx0 * L, ty0 * L, L), new Vector3(tx1 * L, ty0 * L, L),
                new Vector3(tx1 * L, ty1 * L, L), new Vector3(tx0 * L, ty1 * L, L)
            };
            int[] tris = { 0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 1, 1, 3, 2, 1, 4, 3 };
            Vector3 inside = (v[1] + v[2] + v[3] + v[4]) * 0.15f;
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 a = v[tris[i]], bb = v[tris[i + 1]], c = v[tris[i + 2]];
                if (Vector3.Dot(Vector3.Cross(bb - a, c - a), (a + bb + c) / 3f - inside) < 0f)
                {
                    int t = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = t;
                }
            }
            var mesh = new Mesh { name = "FX_BeamPyramid" };
            mesh.vertices = v;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            _fxMeshes.Add(mesh);
            return mesh;
        }

        void DestroyFx()
        {
            foreach (var m in _fxMeshes)
                if (m != null) Destroy(m);
            _fxMeshes.Clear();
        }

        // ------------------------------------------------------------------ per frame
        void UpdateLighting(float dt)
        {
            _lightClockLocal += dt;
            float clock = LightClock;
            for (int g = 0; g < _flick.Length; g++) _flick[g] = FlickerMultiplier(clock, g);
            if (shadowQuality != _appliedShadows) ApplyShadowQuality();
            float density = AtmosphereDensity();

            // strobes: xenon double flash, slower and finally none at low voltage (same supply for both wing tips)
            float strobeLevel = Mathf.Clamp01(strobeLights) * _flick[(int)LampGroup.Strobe];
            UpdateStrobeTimer(strobeLevel, dt);

            // fluorescent cargo strip (one tube, two light sources and the bay fill)
            if (cargoFluorescent != _lastFluorescent) { _lastFluorescent = cargoFluorescent; _fluoState = 0; }
            float cargoLevel = Mathf.Clamp01(cargoLight) * _flick[(int)LampGroup.Cargo];
            float fluo = cargoFluorescent ? FluorescentOutput(cargoLevel, dt) : -1f;

            for (int i = 0; i < _lamps.Count; i++)
            {
                var s = _lamps[i];
                var d = s.def;
                float level;
                switch (d.group)
                {
                    case LampGroup.Nav: level = navLights; break;
                    case LampGroup.Strobe: level = strobeLevel; break;
                    case LampGroup.Beacon: level = beaconLights; break;
                    case LampGroup.Landing: level = landingLight; break;
                    case LampGroup.Cabin: level = cabinLight; break;
                    case LampGroup.Cargo: level = cargoLevel; break;
                    default: level = busPowered ? floodLight : 0f; break;
                }
                level = Mathf.Clamp01(level);
                if (d.group != LampGroup.Strobe && d.group != LampGroup.Cargo) level *= _flick[(int)d.group];

                bool whiteFlood = d.group == LampGroup.Flood && !floodRed;
                bool tungstenTube = d.kind == LampKind.Fluorescent && !cargoFluorescent;      // cargo strip as a plain lamp
                float baseKelvin = tungstenTube || whiteFlood ? 3000f : d.kelvin;
                float kelvin = baseKelvin;
                Color filter = whiteFlood || tungstenTube ? Color.white : d.filter;
                if (d.kind == LampKind.Xenon)
                    s.output = StrobeEnvelope();
                else if (d.kind == LampKind.Fluorescent && fluo >= 0f)
                    s.output = fluo;
                else
                {
                    // tungsten: light ~ T^5, colour temperature ~ T (T relative to full power)
                    float target = level > 0f ? Mathf.Pow(level, 0.2f) : 0f;
                    if (lampInertia && dt > 0f)
                    {
                        float tau = target > s.filament ? d.tauOn : d.tauOff;
                        s.filament += (target - s.filament) * (1f - Mathf.Exp(-dt / Mathf.Max(tau, 1e-3f)));
                    }
                    else s.filament = target;
                    if (s.filament < 0.02f && target <= 0f) s.filament = 0f;
                    s.output = s.filament * s.filament * s.filament * s.filament * s.filament;
                    kelvin = Mathf.Max(1000f, baseKelvin * Mathf.Max(s.filament, 0.05f));
                }
                float lightCut = 1f, beamCut = 1f;
                if (d.rotates)
                {
                    float rpm = beaconRpm * Mathf.Sqrt(Mathf.Clamp01(beaconLights));          // the motor slows with the bus
                    if (rpm > 0f)
                    {
                        s.angle = Mathf.Repeat(s.angle + rpm * 6f * dt, 360f);
                        s.light.transform.localRotation = Quaternion.AngleAxis(s.angle, Vector3.up) * s.baseRotation;
                    }
                    if (d.cutForward)
                    {
                        // pointing forward, the top beacon's beam runs into the roof and wing: the airframe shadows it.
                        // Without a shadow map the light would reach the cabin floor and its in-air beam would show inside the
                        // cabin, so both fade out within +-33 deg of the nose (cabin walls 13-16 deg + the 7 deg fan; the lens
                        // glow stays).
                        Vector3 f = s.light.transform.localRotation * Vector3.forward;
                        float heading = Mathf.Abs(Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg);
                        beamCut = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(26f, 40f, heading));
                        if (!CastsShadows(d)) lightCut = beamCut;
                    }
                }
                ApplyLamp(s, filter, kelvin, density, lightCut, beamCut);
            }
            UpdatePanelBacklight(dt, _flick[7]);
        }

        bool CastsShadows(LightDef d) =>
            d.shadowTier >= 0 && shadowQuality != LightShadowQuality.Off && shadowQuality >= d.shadowFrom;

        void ApplyLamp(LampState s, Color filter, float kelvin, float density, float lightCut, float beamCut)
        {
            var d = s.def;
            bool on = s.output > 0.002f;
            float lampIntensity = on ? s.maxIntensity * s.output : 0f;          // what the lens and its glow show
            float intensity = lampIntensity * lightCut;                          // what the Light component casts
            var l = s.light;
            bool lit = on && intensity > 0f;
            if (lit != s.lastEnabled) { l.enabled = lit; s.lastEnabled = lit; }
            if (Mathf.Abs(intensity - s.lastIntensity) > 1e-4f * Mathf.Max(1f, s.maxIntensity))
            {
                l.intensity = intensity;
                s.lastIntensity = intensity;
            }
            float filterKey = filter.r + filter.g * 3f + filter.b * 7f;
            if (on && (Mathf.Abs(kelvin - s.lastKelvin) > 15f || filterKey != s.lastFilterKey))
            {
                SetLightColour(l, filter, kelvin);
                s.lastKelvin = kelvin;
                s.lastFilterKey = filterKey;
                s.lastKelvinFx = -1f;                                   // glow / beam colour follows
            }
            Color lin = Mul(filter, Mathf.CorrelatedColorTemperatureToRGB(kelvin));
            if (s.lensMat != null) SetEmission(s.lensMat, EmissionLinear(lin, d.lensGain * s.output));
            if (!_fxOk) return;
            // the glow also draws the lens as a point that never shrinks below 1.5 px: a lamp stays visible far away even
            // in clear air (density 0: no halo, core only); beams need air
            bool fxOn = on && density > 0f;
            if (s.glowR != null)
            {
                bool show = on && glows && glowIntensity > 0f;
                if (s.glowR.enabled != show) s.glowR.enabled = show;
                if (show) FxParams(s.glowMat, lampIntensity * glowIntensity, lin, kelvin, density, ref s.lastGlowI, s);
            }
            if (s.beamR != null)
            {
                bool show = fxOn && beams && beamIntensity > 0f && beamCut > 0.001f;
                if (s.beamR.enabled != show) s.beamR.enabled = show;
                if (show) FxParams(s.beamMat, lampIntensity * beamCut * beamIntensity, lin, kelvin, density, ref s.lastBeamI, s);
            }
        }

        void FxParams(Material m, float intensity, Color lin, float kelvin, float density, ref float lastI, LampState s)
        {
            if (Mathf.Abs(intensity - lastI) > 1e-4f * Mathf.Max(1f, intensity))
            {
                m.SetFloat(_intensityId, intensity);
                lastI = intensity;
            }
            if (Mathf.Abs(kelvin - s.lastKelvinFx) > 15f || Mathf.Abs(density - s.lastDensity) > 1e-5f || s.lastKelvinFx < 0f)
            {
                // both materials of the lamp are refreshed together
                var cv = new Vector4(lin.r, lin.g, lin.b, 1f);                     // linear: no gamma conversion
                if (s.glowMat != null) { s.glowMat.SetVector(_colorId, cv); s.glowMat.SetFloat(_densityId, density); }
                if (s.beamMat != null) { s.beamMat.SetVector(_colorId, cv); s.beamMat.SetFloat(_densityId, density); }
                s.lastKelvinFx = kelvin;
                s.lastDensity = density;
            }
        }

        // scattering coefficient per metre for the glow / beam shaders
        float AtmosphereDensity()
        {
            float a = atmosphere;
            if (atmosphereFromSceneFog && RenderSettings.fog && RenderSettings.fogMode != FogMode.Linear)
                a = RenderSettings.fogDensity * atmosphereFogScale;
            return 0.12f * Mathf.Clamp01(a);
        }

        void ApplyShadowQuality()
        {
            _appliedShadows = shadowQuality;
            System.Type urpType = _hdrp ? null : FindType("UnityEngine.Rendering.Universal.UniversalAdditionalLightData");
            var tierField = urpType?.GetField("m_AdditionalLightsShadowResolutionTier",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            foreach (var s in _lamps)
            {
                bool cast = CastsShadows(s.def);
                s.light.shadows = cast ? LightShadows.Soft : LightShadows.None;
                if (!cast || tierField == null) continue;
                Component data = s.light.GetComponent(urpType);
                if (data == null) data = s.light.gameObject.AddComponent(urpType);
                int tier = s.def.shadowTier + (shadowQuality == LightShadowQuality.High && s.def.group == LampGroup.Landing ? 1 : 0);
                try { tierField.SetValue(data, Mathf.Clamp(tier, 0, 2)); }
                catch (System.Exception) { }
            }
        }

        // ------------------------------------------------------------------ lamp physics
        void UpdateStrobeTimer(float level, float dt)
        {
            _strobeAge += dt;
            _strobeFired = false;
            if (level < 0.25f) { _strobeTimer = 0f; _strobeSecond = -1f; return; }
            float interval = 1.05f / Mathf.Pow(level, 0.8f);          // the capacitor charges slower at low voltage
            _strobeTimer += dt;
            if (_strobeSecond >= 0f && _strobeTimer >= _strobeSecond)
            {
                _strobeSecond = -1f;
                Fire();
            }
            if (_strobeTimer >= interval)
            {
                _strobeTimer -= interval;
                Fire();
                if (level > 0.5f) _strobeSecond = _strobeTimer + 0.12f;  // double flash
            }
        }

        void Fire()
        {
            _strobeAge = 0f;
            _strobeFired = true;                                     // this frame shows the full flash whatever the frame rate
        }

        float StrobeEnvelope()
        {
            if (_strobeFired || _strobeAge <= 0f) return 1f;
            if (_strobeAge > 0.09f) return 0f;
            return Mathf.Exp(-_strobeAge / 0.025f);
        }

        float FluorescentOutput(float level, float dt)
        {
            if (level <= 0.001f) { _fluoState = 0; return 0f; }
            if (_fluoState == 0)
            {
                _fluoState = 1;
                _fluoT = 0f;
                _fluoStrikeLen = 0.35f + 0.9f * Hash01(_fluoStrikes * 7 + flickerSeed * 131 + 1);
                _fluoStrikes++;
            }
            _fluoT += dt;
            int seed = flickerSeed * 977 + _fluoStrikes * 61;
            if (_fluoState == 1)
            {
                if (_fluoT < _fluoStrikeLen)
                {
                    int slot = Mathf.FloorToInt(_fluoT / 0.06f);
                    return Hash01(slot * 13 + seed) > 0.55f ? 0.3f + 0.6f * Hash01(slot * 29 + seed + 5) : 0.02f;
                }
                _fluoState = 2;
            }
            if (level >= 0.75f) return 0.9f + 0.1f * level;
            if (level >= 0.45f)
            {
                float u = (0.75f - level) / 0.3f;                    // unstable: random dropouts
                int slot = Mathf.FloorToInt(_fluoT / 0.09f);
                return Hash01(slot * 17 + seed + 3) < 0.05f + 0.3f * u ? 0.15f * Hash01(slot + seed) : 0.65f + 0.3f * (1f - u);
            }
            // fails to run: dark, a burst of strike attempts every 1.5 s
            float ph = Mathf.Repeat(_fluoT, 1.5f);
            if (ph > 0.25f) return 0f;
            int c = Mathf.FloorToInt(_fluoT / 1.5f), s2 = Mathf.FloorToInt(ph / 0.05f);
            return Hash01(c * 31 + s2 * 7 + seed) > 0.5f ? 0.4f : 0f;
        }

        float FlickerMultiplier(float t, int salt)
        {
            float burst = 0f;
            if (t < _burstEnd && t >= _burstStart)
            {
                float k = (_burstEnd - t) / Mathf.Max((_burstEnd - _burstStart) / 3f, 1e-3f);
                burst = _burstStrength * Mathf.Clamp01(k);
            }
            float f = Mathf.Max(Mathf.Clamp01(lightFlicker), burst);
            if (f <= 0f) return 1f;
            int slot = Mathf.FloorToInt(t / 0.06f);
            float r = Hash01(slot * 3 + flickerSeed * 7919 + salt * 1013);                 // each circuit on its own
            float drop = r < 0.04f + 0.42f * f ? 0.35f + 0.65f * Hash01(slot * 11 + flickerSeed + 1 + salt * 389) : 0f;
            float sag = 0.5f + 0.5f * Mathf.Sin(t * 7.3f) * Mathf.Sin(t * 2.1f + 1f);
            return Mathf.Clamp01(1f - f * (drop + 0.18f * sag));
        }

        static float Hash01(int x)
        {
            unchecked
            {
                uint h = (uint)x;
                h = (h ^ 61u) ^ (h >> 16);
                h *= 9u;
                h ^= h >> 4;
                h *= 0x27d4eb2du;
                h ^= h >> 15;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        // instrument backlight: tungsten behind the dials, dimmed by the panel rheostat (reddens as it dims)
        void UpdatePanelBacklight(float dt, float flick)
        {
            float level = busPowered ? Mathf.Clamp01(panelLight) * flick : 0f;
            float target = level > 0f ? Mathf.Pow(level, 0.2f) : 0f;
            if (lampInertia && dt > 0f)
                _panelFilament += (target - _panelFilament) * (1f - Mathf.Exp(-dt / (target > _panelFilament ? 0.06f : 0.12f)));
            else _panelFilament = target;
            if (_panelFilament < 0.02f && target <= 0f) _panelFilament = 0f;
            float output = Mathf.Pow(_panelFilament, 5f);
            _panelOutput = output;
            if (Mathf.Abs(output - _lastPanelKey) < 1e-4f) return;
            _lastPanelKey = output;
            Color c = Mul(new Color(1f, 0.9f, 0.78f), Mathf.CorrelatedColorTemperatureToRGB(Mathf.Max(1000f, 3000f * Mathf.Max(_panelFilament, 0.05f))));
            SetEmission(_gaugesMat, EmissionLinear(c, 0.75f * output));
        }

        // HDR emission from a linear colour (no gamma conversion)
        Vector4 EmissionLinear(Color lin, float k)
        {
            float scale = k * (_hdrp ? hdrpEmissionScale : 1f);
            return new Vector4(lin.r * scale, lin.g * scale, lin.b * scale, 1f);
        }
    }
}
