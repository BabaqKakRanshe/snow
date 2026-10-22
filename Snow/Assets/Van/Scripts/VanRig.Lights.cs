using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UazVan
{
    /// <summary>
    /// The van's 41 lamps (Data/lights.json). Your electrics decide what is switched on (switch, fuse, relay, the mass
    /// switch) and how many volts the bus has; the rig turns that into light:
    ///  - every Light_&lt;name&gt; is a spot shaped by the cookie made from its IES emission diagram (head, spot, fog, work,
    ///    reverse, plate, dome, flood, torch) or a point (markers, tails, turn signals, night red, hand lamp, flares, stove,
    ///    pendant), intensity in URP units at 100 %, colour temperature from the lamp;
    ///  - tungsten physics: light ~ V^3.4 and colour temperature ~ V^0.42 of the bus voltage (13.8 V = 100 %), the filament
    ///    warms up and cools down (tau 50-150 ms), so a cranking starter dims and reddens every lamp;
    ///  - the turn signals blink from the rig's flasher relay (turnSignal, hazard), tails double as brake lamps (brake);
    ///  - flares burn with a fast red flicker, the stove with a slow fire flicker (x 0.3 with its door shut);
    ///  - snow on a lamp (SetSnow) takes up to 85 % of its light;
    ///  - each lens glows with its lamp (MaterialPropertyBlock on the lens renderer), the gauges are backlit (panelLight);
    ///  - glows in fog / falling snow and visible beams of the head, spot, fog, work lamps and the torch (Van/Airlight,
    ///    Van/Beam, need the URP Depth Texture), scaled by atmosphere (or the scene fog, linear fog included);
    ///  - a shadow budget: only the most important lit lamps cast shadows (shadowQuality), so 21 shadow-capable lamps do
    ///    not flood the 2048 shadow atlas;
    ///  - horror hooks: lightFlicker (unstable electrics) and FlickerBurst(); weak battery: dome and panel lamps flutter.
    /// Rendering layers: every van light lights every rendering layer (the Forest ground and statics are on layer 2).
    /// </summary>
    public partial class VanRig
    {
        public enum LightShadowQuality { Off, Low, Medium, High }
        public enum FlickerKind { Filament, Flasher, Flare, Fire }

        [Serializable]
        public class Lamp
        {
            public string name, type, group, toggle, fuse;
            public Light light;
            public Renderer lens;
            public float intensity = 1f, kelvin = 3000f, range = 5f, watts;
            public Color filter = Color.white;                 // linear
            public bool spot, shadows, ownPower, glow, beam;
            public int shadowPriority = 9;
            public FlickerKind flicker;
            public string dimmedBy;                            // Snow_<...> part over the lamp
            [NonSerialized] public float level, output, filament, lastIntensity = -1f, lastKelvin = -1f;
            [NonSerialized] public bool lastEnabled, castShadow;
            [NonSerialized] public int dimIndex = -1;
            [NonSerialized] public MaterialPropertyBlock lensBlock;
            [NonSerialized] public Renderer glowR, beamR;
            [NonSerialized] public Material glowMat, beamMat;
            [NonSerialized] public float lastGlowI = -1f, lastBeamI = -1f, lastFxKey = -1f;
            [NonSerialized] public float seed;
        }

        [Header("Lamps (filled by the setup from Data/lights.json)")]
        public List<Lamp> lamps = new List<Lamp>();
        public Texture2D[] lightCookies = new Texture2D[0];
        public Material glowMaterial, beamMaterial;
        [Tooltip("Dash_Gauges: the backlit dials (M_Van_Gauges emission)")] public Renderer gaugesRenderer;
        [Tooltip("StoveGlow: the fire seen through the stove door")] public Renderer stoveGlowRenderer;
        [Tooltip("LampBulb: the pendant lamp's bulb over the table")] public Renderer pendantBulbRenderer;
        [Tooltip("Needle_*: the needles glow with the dial backlight")] public Renderer[] needleRenderers = new Renderer[0];

        [Header("Electrics (write from your electrical model)")]
        [Tooltip("Bus voltage, V: 0 with the mass switch off; ~12.6 battery only, ~14 with the alternator, ~9.5 cranking")]
        public float busVoltage = 12.6f;
        [Tooltip("Voltage at which a lamp gives its 100 % (intensity in lights.json)")] public float nominalVolts = 13.8f;
        [Tooltip("-1 left, 0 off, +1 right (the flasher needs the bus powered)")] public int turnSignal;
        public bool hazard;
        [Tooltip("Brake pedal pressed: the tail lamps x brakeBoost")] public bool brake;
        public float brakeBoost = 4f;
        [Tooltip("Flasher relay, cycles per minute at nominal voltage")] public float flasherRate = 90f;
        [Range(0, 1), Tooltip("Instrument backlight level (your headlight switch): follows the bus voltage")] public float panelLight;
        [Range(0, 1), Tooltip("Stove fire 0..1 (your stove model); its light, the glow and the lamp LIGHT_Stove")] public float stoveFire;
        [Range(0, 1), Tooltip("Stove door 0 shut .. 1 open: the fire's light x (0.3 + 0.7 x door)")] public float stoveDoor;

        [Header("Look")]
        [Range(0, 1), Tooltip("Fog / falling snow density for the glows and beams: 0 clear night, 0.25 mist, 1 blizzard")]
        public float atmosphere = 0.25f;
        [Tooltip("Take atmosphere from the scene fog (linear: 3 / (end - start); exponential: density) x atmosphereFogScale")]
        public bool atmosphereFromSceneFog = true;
        public float atmosphereFogScale = 40f;
        [Tooltip("Glow of the lamps in the air (needs the URP Depth Texture)")] public bool glows = true;
        [Tooltip("Visible beams of the head, spot, fog and work lamps and the torch (needs the URP Depth Texture)")] public bool beams = true;
        [Range(0, 4)] public float glowIntensity = 1f;
        [Range(0, 4)] public float beamIntensity = 1f;
        [Tooltip("Lens brightness per unit of lamp output")] public float lensGain = 4f;
        [Tooltip("Off: no lamp casts shadows. Low: 2 lamps (by priority), Medium: up to 6 spot-equivalents, High: 12. " +
                 "A point light costs 6 (six shadow faces).")]
        public LightShadowQuality shadowQuality = LightShadowQuality.Medium;
        [Tooltip("Tungsten warm-up / cool-down. Off = lamps follow at once.")] public bool lampInertia = true;
        [Range(0, 1), Tooltip("Horror: unstable electrics, every lamp sags and drops out at random")] public float lightFlicker;
        public int flickerSeed;
        [Tooltip(">= 0: use this clock (s) for flicker and flasher instead of the local one (a shared network time keeps " +
                 "the pattern the same on every peer)")]
        public float flickerClock = -1f;

        // ------------------------------------------------------------------ API
        /// <summary>Commanded level 0..1 of one lamp (its switch, fuse and relay are closed); the rig adds the bus voltage,
        /// the filament, the flicker and the snow. Names as lights.json (Head_L, Marker_Cab0, Dome_Cab, Torch, Flare_1 ...).</summary>
        public void SetLamp(string name, float level)
        {
            Init();
            if (name != null && _lampByName.TryGetValue(name, out var l)) l.level = Mathf.Clamp01(level);
        }

        /// <summary>Same level for every lamp of a group (drive, fog, spot, work, int, reverse, turn, hand, torch, flare, fire)
        /// or of a toggle (Toggle_Head ...).</summary>
        public void SetLampGroup(string groupOrToggle, float level)
        {
            Init();
            float v = Mathf.Clamp01(level);
            foreach (var l in lamps)
                if (l.group == groupOrToggle || l.toggle == groupOrToggle) l.level = v;
        }

        /// <summary>Current light output 0..1 of a lamp (after voltage, filament, flasher, flicker, snow).</summary>
        public float LampOutput(string name) => name != null && _lampByName.TryGetValue(name, out var l) ? l.output : 0f;

        /// <summary>Electrical load of the lit lamps, watts at 12 V (for your battery model). Own-power lamps (torch,
        /// flares, stove) are not counted.</summary>
        public float LampLoadWatts()
        {
            float w = 0f;
            foreach (var l in lamps)
            {
                if (l == null || l.ownPower) continue;
                if (l.level > 0f) w += l.watts * l.level;
                if (l.type == "tail" && brake) w += l.watts * brakeBoost;      // the brake filament, lit with or without the tails
            }
            return w;
        }

        /// <summary>True on the frames the flasher relay clicks on (for the click sound).</summary>
        public bool FlasherClicked { get; private set; }

        /// <summary>Flicker every lamp for a while (strength 0..1, fades out over the last third). Deterministic for a
        /// flickerSeed and flickerClock: send it as an event to every peer.</summary>
        public void FlickerBurst(float seconds, float strength = 1f)
        {
            _burstStart = LightClock;
            _burstEnd = _burstStart + Mathf.Max(0f, seconds);
            _burstStrength = Mathf.Clamp01(strength);
        }

        public static bool IsEffectRenderer(Renderer r) =>
            r != null && (r.name.StartsWith("Glow_", StringComparison.Ordinal) || r.name.StartsWith("Beam_", StringComparison.Ordinal));

        // ------------------------------------------------------------------ state
        readonly Dictionary<string, Lamp> _lampByName = new Dictionary<string, Lamp>(StringComparer.Ordinal);
        readonly List<Mesh> _fxMeshes = new List<Mesh>();
        readonly List<Material> _created = new List<Material>();
        float _lightClockLocal, _burstStart, _burstEnd = -1f, _burstStrength, _flasherPhase, _shadowTimer;
        bool _flasherOn, _fxOk;
        float _panelFilament, _lastPanel = -1f, _lastStove = -1f, _lastPendant = -1f;
        MaterialPropertyBlock _gaugesBlock, _stoveBlock, _bulbBlock, _needleBlock;
        LightShadowQuality _appliedShadows = (LightShadowQuality)(-1);
        int _densityId, _intensityId, _colorId, _emissionId;
        Color _gaugesEmission = new Color(1f, 0.82f, 0.6f), _stoveEmission = new Color(1f, 0.42f, 0.1f),
              _bulbEmission = new Color(1f, 0.8f, 0.52f);
        float LightClock => flickerClock >= 0f ? flickerClock : _lightClockLocal;

        void InitLighting()
        {
            _densityId = Shader.PropertyToID("_Density");
            _intensityId = Shader.PropertyToID("_Intensity");
            _colorId = Shader.PropertyToID("_Color");
            _emissionId = Shader.PropertyToID("_EmissionColor");
            _lampByName.Clear();
            // glows and beams are made in play mode only (an editor script calling the rig must not add objects to a prefab)
            bool srp = GraphicsSettings.currentRenderPipeline != null && Application.isPlaying;
            if (srp)
            {
                if (!UsableFx(glowMaterial)) glowMaterial = FxTemplate("Van/Airlight");
                if (!UsableFx(beamMaterial)) beamMaterial = FxTemplate("Van/Beam");
            }
            _fxOk = srp && glowMaterial != null && beamMaterial != null;
            int k = 0;
            foreach (var l in lamps)
            {
                if (l == null || string.IsNullOrEmpty(l.name)) continue;
                _lampByName[l.name] = l;
                l.dimIndex = string.IsNullOrEmpty(l.dimmedBy) ? -1 : Index(l.dimmedBy);
                l.seed = Hash01(k++ * 7919 + 13) * 100f;
                // the stove glow and the pendant bulb have their own emission (UpdatePanelAndProps): one writer per renderer
                bool ownEmission = l.lens != null && (l.lens == stoveGlowRenderer || l.lens == pendantBulbRenderer);
                l.lensBlock = l.lens != null && !ownEmission ? new MaterialPropertyBlock() : null;
                if (l.light != null)
                {
                    l.lastEnabled = l.light.enabled;
                    if (Application.isPlaying) RemoveOldFx(l.light.transform);
                    if (_fxOk) BuildFx(l);
                }
            }
            _gaugesBlock = new MaterialPropertyBlock();
            _stoveBlock = new MaterialPropertyBlock();
            _bulbBlock = new MaterialPropertyBlock();
            _needleBlock = new MaterialPropertyBlock();
        }

        static bool UsableFx(Material m) => m != null && m.shader != null && m.shader.isSupported;

        Material FxTemplate(string shaderName)
        {
            var sh = Shader.Find(shaderName);
            if (sh == null || !sh.isSupported) return null;
            var m = new Material(sh) { name = shaderName };
            _created.Add(m);
            return m;
        }

        // effect objects copied with a live van (Instantiate during play) belong to the source
        static void RemoveOldFx(Transform light)
        {
            for (int i = light.childCount - 1; i >= 0; i--)
            {
                var c = light.GetChild(i);
                if (c.name.StartsWith("Glow_", StringComparison.Ordinal) || c.name.StartsWith("Beam_", StringComparison.Ordinal))
                {
                    c.gameObject.SetActive(false);
                    Destroy(c.gameObject);
                }
            }
        }

        void ResyncLighting()
        {
            foreach (var l in lamps)
            {
                if (l == null) continue;
                l.lastIntensity = l.lastKelvin = l.lastGlowI = l.lastBeamI = l.lastFxKey = -1f;
                if (l.light != null) l.lastEnabled = !l.light.enabled;
            }
            _lastPanel = _lastStove = _lastPendant = -1f;
            _appliedShadows = (LightShadowQuality)(-1);
        }

        void SuspendLighting()
        {
            foreach (var l in lamps)
            {
                if (l == null) continue;
                if (l.glowR != null) l.glowR.enabled = false;
                if (l.beamR != null) l.beamR.enabled = false;
            }
            ResyncLighting();
        }

        void DestroyLighting()
        {
            foreach (var m in _fxMeshes) if (m != null) Destroy(m);
            _fxMeshes.Clear();
            foreach (var m in _created) if (m != null) Destroy(m);
            _created.Clear();
        }

        // snow on a lamp changed: nothing to cache, the next frame reads it
        void SnowChangedLighting() { }

        // ------------------------------------------------------------------ glows and beams
        struct GlowPreset { public float core, coreGain, floor, sector, beamWidth, halo, iso, radiusMax; public bool lightFrame; }
        struct BeamPreset { public float length, xMin, xMax, yMin, yMax, core, skirt, gain; }

        static GlowPreset GlowFor(string type)
        {
            switch (type)
            {
                case "head": return new GlowPreset { core = 0.07f, coreGain = 0.35f, floor = 0.015f, beamWidth = 6f, halo = 1f, iso = 0f, radiusMax = 60f, lightFrame = true };
                case "spot": return new GlowPreset { core = 0.06f, coreGain = 0.3f, floor = 0.01f, beamWidth = 4f, halo = 1f, iso = 0f, radiusMax = 60f, lightFrame = true };
                case "fog": return new GlowPreset { core = 0.06f, coreGain = 0.5f, floor = 0.02f, beamWidth = 16f, halo = 1f, iso = 0.3f, radiusMax = 40f, lightFrame = true };
                case "work": return new GlowPreset { core = 0.05f, coreGain = 0.5f, floor = 0.02f, beamWidth = 20f, halo = 1f, iso = 0.3f, radiusMax = 40f, lightFrame = true };
                case "torch": return new GlowPreset { core = 0.025f, coreGain = 0.6f, floor = 0.01f, beamWidth = 5f, halo = 1f, iso = 0f, radiusMax = 25f, lightFrame = true };
                case "flare": return new GlowPreset { core = 0.03f, coreGain = 3f, floor = 1f, sector = 180f, halo = 1.2f, iso = 1f, radiusMax = 30f };
                case "hand": return new GlowPreset { core = 0.03f, coreGain = 2f, floor = 1f, sector = 180f, halo = 0.8f, iso = 1f, radiusMax = 15f };
                default: return new GlowPreset { core = 0.025f, coreGain = 2.2f, floor = 0.06f, sector = 70f, halo = 0.7f, iso = 1f, radiusMax = 25f };
            }
        }

        static bool BeamFor(string type, out BeamPreset b)
        {
            switch (type)
            {
                case "head": b = new BeamPreset { length = 45f, xMin = -28, xMax = 32, yMin = -14, yMax = 5, core = 5f, skirt = 0.012f, gain = 1f }; return true;
                case "spot": b = new BeamPreset { length = 70f, xMin = -14, xMax = 14, yMin = -12, yMax = 12, core = 3.5f, skirt = 0.01f, gain = 1f }; return true;
                case "fog": b = new BeamPreset { length = 22f, xMin = -45, xMax = 45, yMin = -20, yMax = 6, core = 0f, skirt = 0f, gain = 0.8f }; return true;
                case "work": b = new BeamPreset { length = 20f, xMin = -52, xMax = 52, yMin = -48, yMax = 20, core = 0f, skirt = 0f, gain = 0.7f }; return true;
                case "torch": b = new BeamPreset { length = 16f, xMin = -12, xMax = 12, yMin = -12, yMax = 12, core = 3f, skirt = 0.02f, gain = 1f }; return true;
                default: b = default; return false;
            }
        }

        void BuildFx(Lamp l)
        {
            var lt = l.light.transform;
            if (l.glow)
            {
                var g = GlowFor(l.type);
                var go = new GameObject("Glow_" + l.name);
                go.transform.SetParent(lt, false);
                var m = new Material(glowMaterial) { name = "FX_Glow_" + l.name };
                _created.Add(m);
                m.SetFloat("_CoreSize", g.core);
                m.SetFloat("_CoreGain", g.coreGain);
                m.SetFloat("_Halo", g.halo);
                m.SetFloat("_Iso", g.iso);
                m.SetFloat("_Sector", g.lightFrame ? 180f : g.sector);
                m.SetFloat("_SectorSoft", 6f);
                m.SetFloat("_BeamWidth", g.beamWidth);
                m.SetFloat("_FanWidth", 0f);
                m.SetFloat("_Floor", g.floor);
                m.SetFloat("_RadiusMax", g.radiusMax);
                m.SetFloat(_intensityId, 0f);
                l.glowMat = m;
                l.glowR = AddFxRenderer(go, QuadMesh(g.radiusMax), m);
            }
            if (l.beam && BeamFor(l.type, out var b))
            {
                var go = new GameObject("Beam_" + l.name);
                go.transform.SetParent(lt, false);
                var m = new Material(beamMaterial) { name = "FX_Beam_" + l.name };
                _created.Add(m);
                m.SetFloat("_Length", b.length);
                m.SetFloat("_XMin", b.xMin);
                m.SetFloat("_XMax", b.xMax);
                m.SetFloat("_YMin", b.yMin);
                m.SetFloat("_YMax", b.yMax);
                m.SetFloat("_CoreWidth", b.core);
                m.SetFloat("_Skirt", b.skirt);
                m.SetFloat("_FanWidth", 0f);
                m.SetFloat("_Gain", b.gain);
                m.SetFloat(_intensityId, 0f);
                l.beamMat = m;
                l.beamR = AddFxRenderer(go, PyramidMesh(b), m);
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
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * (2.5f * Mathf.Max(radiusMax, 1f)));
            _fxMeshes.Add(mesh);
            return mesh;
        }

        Mesh PyramidMesh(BeamPreset b)
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

        // ------------------------------------------------------------------ per frame
        void UpdateLighting(float dt)
        {
            if (!_ready) return;
            _lightClockLocal += dt;
            float clock = LightClock;
            float supply = Mathf.Max(0f, busVoltage) / Mathf.Max(nominalVolts, 1f);
            float flick = FlickerMultiplier(clock, 0);
            UpdateFlasher(dt, supply);
            float density = AtmosphereDensity();
            for (int i = 0; i < lamps.Count; i++)
            {
                var l = lamps[i];
                if (l == null || l.light == null) continue;
                float level = l.level;
                float sup = l.ownPower ? 1f : supply;
                switch (l.flicker)
                {
                    case FlickerKind.Flasher:
                    {
                        bool left = l.name.EndsWith("L", StringComparison.Ordinal);
                        bool active = hazard || (turnSignal < 0 && left) || (turnSignal > 0 && !left);
                        level = active && _flasherOn ? 1f : 0f;
                        break;
                    }
                    case FlickerKind.Flare:
                        level *= 0.78f + 0.22f * Noise(clock * 17f + l.seed) * Noise(clock * 5.3f + l.seed * 3f);
                        break;
                    case FlickerKind.Fire:
                        level = Mathf.Clamp01(stoveFire) * (0.3f + 0.7f * Mathf.Clamp01(stoveDoor)) *
                                (0.72f + 0.28f * Noise(clock * 2.3f + l.seed) * Noise(clock * 7.1f + l.seed * 2f));
                        break;
                }
                if (l.type == "tail" && brake && sup > 0.3f) level = Mathf.Max(level, 1f) * brakeBoost;
                // tungsten: steady output ~ (V/Vnom)^3.4, colour temperature ~ (V/Vnom)^0.42; weak bus: interior lamps flutter
                float target = level > 0f && sup > 0.05f ? level * Mathf.Pow(sup, 3.4f) : 0f;
                if (!l.ownPower && sup < 0.85f && sup > 0.05f && (l.group == "int"))
                    target *= 1f - (0.85f - sup) * 1.6f * Noise(clock * 9f + l.seed);
                if (!l.ownPower) target *= flick;
                if (l.dimIndex >= 0) target *= 1f - 0.85f * GetSnowIndex(l.dimIndex);
                if (lampInertia && dt > 0f && l.flicker != FlickerKind.Fire && l.flicker != FlickerKind.Flare)
                {
                    float tau = target > l.filament ? 0.05f : 0.14f;
                    l.filament += (target - l.filament) * (1f - Mathf.Exp(-dt / tau));
                }
                else l.filament = target;
                if (l.filament < 0.002f && target <= 0f) l.filament = 0f;
                l.output = l.filament;
                float kelvin = l.ownPower || l.flicker != FlickerKind.Filament && l.flicker != FlickerKind.Flasher
                    ? l.kelvin
                    : Mathf.Max(1200f, l.kelvin * Mathf.Pow(Mathf.Clamp(sup, 0.05f, 1.2f), 0.42f));
                ApplyLamp(l, kelvin, density);
            }
            UpdateShadows(dt);
            UpdatePanelAndProps(dt, supply, flick, clock);
        }

        float GetSnowIndex(int i)
        {
            var p = parts[i];
            if (p.t == null || !p.t.gameObject.activeSelf) return 0f;
            return p.kind == PartKind.Stretch ? Mathf.InverseLerp(p.min, p.max, _value[i]) : _value[i];
        }

        void UpdateFlasher(float dt, float supply)
        {
            FlasherClicked = false;
            bool want = (hazard || turnSignal != 0) && supply > 0.6f;
            if (!want) { _flasherOn = false; _flasherPhase = 0f; return; }
            float rate = flasherRate / 60f * Mathf.Clamp(supply, 0.6f, 1.1f);
            float before = _flasherPhase;
            _flasherPhase = Mathf.Repeat(_flasherPhase + rate * dt, 1f);
            bool on = _flasherPhase < 0.5f;
            if (on && !_flasherOn) FlasherClicked = true;
            if (before > _flasherPhase && !on) FlasherClicked = true;
            _flasherOn = on;
        }

        void ApplyLamp(Lamp l, float kelvin, float density)
        {
            bool on = l.output > 0.002f;
            float intensity = on ? l.intensity * l.output : 0f;
            var lt = l.light;
            if (on != l.lastEnabled) { lt.enabled = on; l.lastEnabled = on; }
            if (Mathf.Abs(intensity - l.lastIntensity) > 1e-4f * Mathf.Max(1f, l.intensity))
            {
                lt.intensity = intensity;
                l.lastIntensity = intensity;
            }
            if (on && Mathf.Abs(kelvin - l.lastKelvin) > 15f)
            {
                SetLightColour(lt, l.filter, kelvin);
                l.lastKelvin = kelvin;
                l.lastFxKey = -1f;
            }
            Color lin = Mul(l.filter, Mathf.CorrelatedColorTemperatureToRGB(kelvin));
            if (l.lens != null && l.lensBlock != null)
            {
                l.lens.GetPropertyBlock(l.lensBlock);
                float g = lensGain * Mathf.Min(l.output, 4f);
                l.lensBlock.SetColor(_emissionId, new Color(lin.r * g, lin.g * g, lin.b * g, 1f));
                l.lens.SetPropertyBlock(l.lensBlock);
            }
            if (!_fxOk) return;
            if (l.glowR != null)
            {
                bool show = on && glows && glowIntensity > 0f;
                if (l.glowR.enabled != show) l.glowR.enabled = show;
                if (show) FxParams(l.glowMat, intensity * glowIntensity, lin, kelvin, density, ref l.lastGlowI, l);
            }
            if (l.beamR != null)
            {
                bool show = on && beams && beamIntensity > 0f && density > 0f;
                if (l.beamR.enabled != show) l.beamR.enabled = show;
                if (show) FxParams(l.beamMat, intensity * beamIntensity, lin, kelvin, density, ref l.lastBeamI, l);
            }
        }

        void FxParams(Material m, float intensity, Color lin, float kelvin, float density, ref float lastI, Lamp l)
        {
            if (Mathf.Abs(intensity - lastI) > 1e-4f * Mathf.Max(1f, intensity))
            {
                m.SetFloat(_intensityId, intensity);
                lastI = intensity;
            }
            float key = kelvin * 0.001f + density * 1000f;
            if (Mathf.Abs(key - l.lastFxKey) > 1e-3f)
            {
                var cv = new Vector4(lin.r, lin.g, lin.b, 1f);
                if (l.glowMat != null) { l.glowMat.SetVector(_colorId, cv); l.glowMat.SetFloat(_densityId, density); }
                if (l.beamMat != null) { l.beamMat.SetVector(_colorId, cv); l.beamMat.SetFloat(_densityId, density); }
                l.lastFxKey = key;
            }
        }

        // Light.color is gamma; the filter is linear. Colour temperature on (the project's setting) or folded into the colour.
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

        float AtmosphereDensity()
        {
            float a = atmosphere;
            if (atmosphereFromSceneFog && RenderSettings.fog)
            {
                if (RenderSettings.fogMode == FogMode.Linear)
                    a = 3f / Mathf.Max(RenderSettings.fogEndDistance - RenderSettings.fogStartDistance, 1f) * atmosphereFogScale;
                else
                    a = RenderSettings.fogDensity * atmosphereFogScale;
            }
            return 0.12f * Mathf.Clamp01(a);
        }

        // ------------------------------------------------------------------ shadows: a budget by priority
        void UpdateShadows(float dt)
        {
            _shadowTimer -= dt;
            if (_shadowTimer > 0f && _appliedShadows == shadowQuality) return;
            _shadowTimer = 0.25f;
            _appliedShadows = shadowQuality;
            int budget = shadowQuality == LightShadowQuality.Low ? 2 : shadowQuality == LightShadowQuality.Medium ? 6 :
                         shadowQuality == LightShadowQuality.High ? 12 : 0;
            int lowCount = 0;
            // lamps that may cast, lit, best priority first (then the brighter one)
            _cand.Clear();
            foreach (var l in lamps)
                if (l != null && l.light != null && l.shadows && l.output > 0.05f) _cand.Add(l);
            _cand.Sort((a, b) => a.shadowPriority != b.shadowPriority ? a.shadowPriority.CompareTo(b.shadowPriority)
                                                                       : (b.intensity * b.output).CompareTo(a.intensity * a.output));
            foreach (var l in lamps) if (l != null) l.castShadow = false;
            foreach (var l in _cand)
            {
                int cost = l.spot ? 1 : 6;
                if (shadowQuality == LightShadowQuality.Low) { if (lowCount >= 2) break; cost = 1; }
                if (cost > budget) continue;
                budget -= cost;
                lowCount++;
                l.castShadow = true;
            }
            foreach (var l in lamps)
            {
                if (l == null || l.light == null) continue;
                var want = l.castShadow ? LightShadows.Soft : LightShadows.None;
                if (l.light.shadows != want) l.light.shadows = want;
            }
        }

        readonly List<Lamp> _cand = new List<Lamp>();

        // ------------------------------------------------------------------ dials, stove glow, pendant bulb
        void UpdatePanelAndProps(float dt, float supply, float flick, float clock)
        {
            // instrument backlight: tungsten behind the dials, dims and reddens with the bus
            float target = panelLight > 0f && supply > 0.05f ? Mathf.Clamp01(panelLight) * Mathf.Pow(supply, 3.4f) * flick : 0f;
            if (supply < 0.85f && supply > 0.05f) target *= 1f - (0.85f - supply) * 1.6f * Noise(clock * 9f + 3.3f);
            _panelFilament = lampInertia && dt > 0f
                ? _panelFilament + (target - _panelFilament) * (1f - Mathf.Exp(-dt / (target > _panelFilament ? 0.05f : 0.14f)))
                : target;
            if (gaugesRenderer != null && Mathf.Abs(_panelFilament - _lastPanel) > 1e-3f)
            {
                _lastPanel = _panelFilament;
                float k = Mathf.Max(1200f, 3000f * Mathf.Pow(Mathf.Clamp(supply, 0.05f, 1.2f), 0.42f));
                Color c = Mul(_gaugesEmission, Mathf.CorrelatedColorTemperatureToRGB(k));
                gaugesRenderer.GetPropertyBlock(_gaugesBlock);
                _gaugesBlock.SetColor(_emissionId, c * (0.8f * _panelFilament));
                gaugesRenderer.SetPropertyBlock(_gaugesBlock);
                foreach (var r in needleRenderers)
                {
                    if (r == null) continue;
                    r.GetPropertyBlock(_needleBlock);
                    _needleBlock.SetColor(_emissionId, new Color(1f, 0.35f, 0.08f) * (0.9f * _panelFilament));
                    r.SetPropertyBlock(_needleBlock);
                }
            }
            float stove = Mathf.Clamp01(stoveFire) * (0.72f + 0.28f * Noise(clock * 2.3f + 11f) * Noise(clock * 7.1f + 5f));
            if (stoveGlowRenderer != null && Mathf.Abs(stove - _lastStove) > 1e-3f)
            {
                _lastStove = stove;
                stoveGlowRenderer.GetPropertyBlock(_stoveBlock);
                _stoveBlock.SetColor(_emissionId, _stoveEmission * (2.2f * stove));
                stoveGlowRenderer.SetPropertyBlock(_stoveBlock);
            }
            float bulb = LampOutput("Pendant");
            if (pendantBulbRenderer != null && Mathf.Abs(bulb - _lastPendant) > 1e-3f)
            {
                _lastPendant = bulb;
                pendantBulbRenderer.GetPropertyBlock(_bulbBlock);
                _bulbBlock.SetColor(_emissionId, _bulbEmission * (3f * bulb));
                pendantBulbRenderer.SetPropertyBlock(_bulbBlock);
            }
        }

        // ------------------------------------------------------------------ flicker
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
            float r = Hash01(slot * 3 + flickerSeed * 7919 + salt * 1013);
            float drop = r < 0.04f + 0.42f * f ? 0.35f + 0.65f * Hash01(slot * 11 + flickerSeed + 1 + salt * 389) : 0f;
            float sag = 0.5f + 0.5f * Mathf.Sin(t * 7.3f) * Mathf.Sin(t * 2.1f + 1f);
            return Mathf.Clamp01(1f - f * (drop + 0.18f * sag));
        }

        // smooth value noise 0..1
        static float Noise(float x)
        {
            int i = Mathf.FloorToInt(x);
            float f = x - i;
            float a = Hash01(i), b = Hash01(i + 1);
            float u = f * f * (3f - 2f * f);
            return a + (b - a) * u;
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
    }
}
