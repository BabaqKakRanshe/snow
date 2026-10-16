using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SkyIsland
{
    /// <summary>
    /// One lamp: its Lights, its emissive parts and its fog halo follow one brightness level, animated by the lamp type, so
    /// light, glow and halo always agree. The fixture is lit while it is supplied (SetSupply: grid power plus the room switch,
    /// or its battery) and not broken.
    /// <list type="bullet">
    /// <item>Fluorescent: 1-3 starter blinks over 0.3-1.5 s when switched on; a faulty tube drops out in bursts and sometimes
    /// goes out and restarts; low voltage makes tubes flicker, below 65 % they go out.</item>
    /// <item>Incandescent, Halogen: the filament ramps up in about 0.1 s (red first, then its colour temperature) and glows
    /// on for a moment when switched off; low voltage dims (V^3.4) and warms (V^0.42) the light.</item>
    /// <item>HPS, MH (gas discharge): warm up from a dim glow over <see cref="warmup"/> seconds; after losing power they cannot
    /// restart until cooled (<see cref="restrike"/> seconds, less if they were not fully warm); below 85 % voltage they go out.</item>
    /// <item>LED: instant. Flame: flickers by itself and needs no power.</item>
    /// <item>Exit: battery, always lit. Emergency: battery, lit only while the grid is down.</item>
    /// <item>Beacon: steady, or flashing with <see cref="flash"/> = (period s, on fraction), e.g. obstruction lights.</item>
    /// </list>
    /// Patterns stay at or under 3 flashes per second (WCAG 2.3.1); <see cref="SkyIslandLightManager.noFlicker"/> makes every
    /// lamp steady. Light intensity also follows the manager's light LOD fade (emission and halo do not, so distant lamps still
    /// glow), and the manager's shadow budget switches realtime shadows of hero fixtures. Without a manager the fixture switches
    /// itself on at start (emergency lights stay dark). Play mode only: emissive and halo materials get runtime copies per
    /// fixture (assets are never changed); edit mode shows the lamp as authored.
    /// </summary>
    [AddComponentMenu("Sky Island/Light Fixture")]
    [DisallowMultipleComponent]
    public class SkyIslandLightFixture : MonoBehaviour
    {
        public enum Kind { Fluorescent, Incandescent, Halogen, HPS, MH, LED, Flame, Exit, Emergency, Beacon }
        public enum Circuit { Grid, Battery, None }
        /// <summary>For sound and gameplay: Blink = starter click / tube ping, Fault = buzz and dropout, Starting = ballast
        /// strike of a gas lamp or start of a sequence.</summary>
        public enum Event { Off, Starting, On, Blink, Fault, Broken, Repaired }

        [Header("Type")]
        public Kind kind = Kind.Fluorescent;
        [Tooltip("Grid: needs power (and the room switch). Battery: exit signs, emergency lights. None: flames")]
        public Circuit circuit = Circuit.Grid;
        [Tooltip("Light zone (SkyIslandLightManager room) of this lamp")] public string room = "";
        [Tooltip("Follows the room's light switch (off: grid only, e.g. street and obstruction lights)")] public bool switchable = true;
        [Tooltip("Failing lamp: random dropouts and restarts")] public bool faulty;
        [Tooltip("May get a realtime shadow from the manager's shadow budget")] public bool hero;
        [Tooltip("The Light is on only within this distance of the viewer, in metres (0 = always); the lamp keeps glowing farther away")]
        public float lodDistance = 25f;
        [Tooltip("Gas discharge lamps: warm-up time in seconds")] public float warmup = 90f;
        [Tooltip("Gas discharge lamps: after losing power the lamp cannot restart for this many seconds")] public float restrike = 60f;
        [Tooltip("Beacons: x = flash period in seconds (0 = steady), y = fraction of the period the lamp is lit")]
        public Vector2 flash = Vector2.zero;

        [Header("Parts")]
        [Tooltip("Lights driven by this lamp")] public Light[] lights = new Light[0];
        [Tooltip("Fog halo quads (SkyIsland/LightGlow)")] public Renderer[] halos = new Renderer[0];
        [Tooltip("Renderers whose emissive materials follow the lamp (empty: every renderer under this object except the halos)")]
        public Renderer[] emissive = new Renderer[0];
        [Tooltip("Materials the lamp leaves alone, e.g. the power grid's LED / screen materials (a charging LED follows the grid)")]
        public Material[] ignoreMaterials = new Material[0];

        /// <summary>Raised on state changes (see <see cref="Event"/>).</summary>
        public event Action<SkyIslandLightFixture, Event> onEvent;

        /// <summary>Manager that supplies this fixture (set by SkyIslandLightManager), or null.</summary>
        public SkyIslandLightManager Manager { get; internal set; }
        /// <summary>Current light output 0..1 (before the LOD fade).</summary>
        public float Level { get { return Broken ? 0f : _level; } }
        public bool IsLit { get { return !Broken && _level > 0.01f; } }
        public bool Supplied { get { return _supplied; } }
        public bool Broken { get; private set; }
        public float LodFade { get { return _lodFade; } }
        public bool Shadowed { get { return _shadowed; } }

        enum Phase { Off, Starting, On, Stopping, Cooling }

        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int EmissiveId = Shader.PropertyToID("_EmissiveColor");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        sealed class EmissiveSlot { public Renderer r; public int index; public Material original, copy; public int prop; public Color baseColor; }
        sealed class HaloSlot { public Renderer r; public Material original, copy; public float baseIntensity; public bool hasColor; public Color baseColor; }

        // captured authored values
        float[] _baseI = new float[0];
        float[] _baseK = new float[0];
        bool[] _useK = new bool[0];
        bool[] _spot = new bool[0];
        LightShadows[] _baseShadows = new LightShadows[0];
        readonly List<EmissiveSlot> _emissive = new List<EmissiveSlot>();
        readonly List<HaloSlot> _halos = new List<HaloSlot>();
        float _kelvin;                       // colour temperature of the lamp (0 = not temperature based)
        bool _init;

        // state
        Phase _phase = Phase.Off;
        bool _supplied;
        float _voltage = 1f, _battery = 1f;
        float _level, _kFactor = 1f;
        float _t0;                           // phase start (Time.time); Starting may begin in the future (start delay)
        float _fromLevel, _fromK;            // values when Stopping began
        float _lodFade = 1f;
        bool _shadowed;
        float _appliedLit = -1f, _appliedK = -1f;
        uint _rng = 2463534242u;
        float _seed;
        // fluorescent start sequence / fault burst: segment end times (relative to _t0) and levels
        readonly List<float> _segEnd = new List<float>();
        readonly List<float> _segLevel = new List<float>();
        float _nextFault = float.MaxValue;
        bool _burst, _burstThenOut;
        float _burstT0;
        // gas discharge lamps
        float _coolUntil;

        // ------------------------------------------------------------------ setup
        void Awake() { EnsureInit(); }

        void EnsureInit()
        {
            if (_init) return;
            _init = true;
            var p = transform.position;
            _rng ^= (uint)(Mathf.Abs(p.x * 73.13f + p.y * 191.7f + p.z * 37.9f) * 1000f) | 1u;
            _seed = Rand() * 100f;
            if (lights == null) lights = new Light[0];
            int n = lights.Length;
            _baseI = new float[n]; _baseK = new float[n]; _useK = new bool[n]; _spot = new bool[n]; _baseShadows = new LightShadows[n];
            bool cct = GraphicsSettings.lightsUseColorTemperature;
            for (int i = 0; i < n; i++)
            {
                var l = lights[i];
                if (l == null) continue;
                _baseI[i] = l.intensity;
                _baseK[i] = l.colorTemperature;
                _useK[i] = cct && l.useColorTemperature;
                _spot[i] = l.type == LightType.Spot;
                _baseShadows[i] = l.shadows;
                if (_useK[i] && _kelvin <= 0f) _kelvin = l.colorTemperature;
            }
            if (Application.isPlaying) CopyMaterials();
            _level = 0f;
        }

        void CopyMaterials()
        {
            var haloSet = new HashSet<Renderer>();
            if (halos != null)
                foreach (var h in halos)
                {
                    if (h == null || !haloSet.Add(h)) continue;
                    var m = h.sharedMaterial;
                    if (m == null) continue;
                    var slot = new HaloSlot { r = h, original = m, copy = new Material(m) { name = m.name + " (Lamp)" } };
                    slot.baseIntensity = m.HasProperty(IntensityId) ? m.GetFloat(IntensityId) : 1f;
                    slot.hasColor = m.HasProperty(ColorId);
                    if (slot.hasColor) slot.baseColor = m.GetColor(ColorId);
                    h.sharedMaterial = slot.copy;
                    _halos.Add(slot);
                }
            var ignore = new HashSet<Material>();
            if (ignoreMaterials != null) foreach (var m in ignoreMaterials) if (m != null) ignore.Add(m);
            var rs = emissive != null && emissive.Length > 0 ? emissive : GetComponentsInChildren<Renderer>(true);
            foreach (var r in rs)
            {
                if (r == null || haloSet.Contains(r)) continue;
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || ignore.Contains(m)) continue;
                    int prop = m.HasProperty(EmissionId) ? EmissionId : (m.HasProperty(EmissiveId) ? EmissiveId : -1);
                    if (prop == -1) continue;
                    var c = m.GetColor(prop);
                    if (c.maxColorComponent <= 1e-4f) continue;
                    if (prop == EmissionId && !m.IsKeywordEnabled("_EMISSION")) continue;   // URP Lit / Standard: emission off
                    var slot = new EmissiveSlot { r = r, index = i, original = m, copy = new Material(m) { name = m.name + " (Lamp)" }, prop = prop, baseColor = c };
                    mats[i] = slot.copy;
                    _emissive.Add(slot);
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        void Start()
        {
            EnsureInit();
            if (Manager == null) SetSupply(kind != Kind.Emergency, 1f, 1f, true, 0f);
        }

        void OnEnable()
        {
            if (!_init || !Application.isPlaying) return;
            // back from an inactive proximity group: skip missed faults, catch up on running sequences
            if (_nextFault < Time.time) ScheduleFault(Time.time);
            ApplyAll(true);
        }

        void OnDestroy()
        {
            foreach (var s in _emissive)
            {
                if (s.r != null)
                {
                    var mats = s.r.sharedMaterials;
                    if (s.index < mats.Length && mats[s.index] == s.copy) { mats[s.index] = s.original; s.r.sharedMaterials = mats; }
                }
                if (s.copy != null) Destroy(s.copy);
            }
            foreach (var h in _halos)
            {
                if (h.r != null && h.r.sharedMaterial == h.copy) h.r.sharedMaterial = h.original;
                if (h.copy != null) Destroy(h.copy);
            }
            _emissive.Clear();
            _halos.Clear();
        }

        // ------------------------------------------------------------------ control
        /// <summary>Power state from the manager (or gameplay code). on: the lamp should be lit; voltage: grid voltage
        /// (1 = nominal); battery: charge 0..1 of battery lamps; instant: jump to the end state without the start / stop
        /// sequence; delay: seconds before a start sequence begins.</summary>
        public void SetSupply(bool on, float voltage, float battery, bool instant, float delay)
        {
            EnsureInit();
            float now = Time.time;
            bool wasSupplied = _supplied;
            _voltage = Mathf.Max(0f, voltage);
            _battery = Mathf.Clamp01(battery);
            _supplied = on;
            if (instant)
            {
                _segEnd.Clear(); _segLevel.Clear(); _burst = false;
                if (on && SteadyTarget() > 0f)
                {
                    _phase = Phase.On;
                    _level = SteadyTarget();
                    _kFactor = SteadyK();
                    ScheduleFault(now);
                }
                else
                {
                    _phase = Phase.Off;
                    _level = 0f;
                    _kFactor = 1f;
                }
                _t0 = now;
                ApplyAll(true);
                enabled = NeedsTick();
                return;
            }
            if (on != wasSupplied)
            {
                if (on) BeginStart(now, Mathf.Max(0f, delay));
                else BeginStop(now);
            }
            enabled = true;   // voltage / battery changes are followed in Tick
        }

        /// <summary>Destroyed lamp (e.g. by a creature): dark until <see cref="Repair"/>.</summary>
        public void Break()
        {
            EnsureInit();
            if (Broken) return;
            Broken = true;
            if (kind == Kind.HPS || kind == Kind.MH) _coolUntil = Time.time + restrike;
            _phase = Phase.Off;
            _level = 0f;
            ApplyAll(true);
            Raise(Event.Broken);
            enabled = false;
        }

        public void Repair()
        {
            EnsureInit();
            if (!Broken) return;
            Broken = false;
            Raise(Event.Repaired);
            if (_supplied) BeginStart(Time.time, 0f);
            enabled = true;
        }

        /// <summary>Light LOD from the manager: 0 = Light off, 1 = full (emission and halo are not affected).</summary>
        public void SetLodFade(float fade)
        {
            fade = Mathf.Clamp01(fade);
            if (Mathf.Abs(fade - _lodFade) < 0.01f && !(fade == 0f && _lodFade != 0f) && !(fade == 1f && _lodFade != 1f)) return;
            _lodFade = fade;
            if (_init) ApplyLights();
        }

        /// <summary>Realtime shadows from the manager's shadow budget (spot lights; point lights only if allowPoint).</summary>
        public void SetShadows(bool on, LightShadows type, bool allowPoint)
        {
            EnsureInit();
            _shadowed = on;
            int n = Mathf.Min(lights.Length, _spot.Length);
            for (int i = 0; i < n; i++)
            {
                var l = lights[i];
                if (l == null) continue;
                var want = on && (_spot[i] || allowPoint) ? type : _baseShadows[i];
                if (l.shadows != want) l.shadows = want;
            }
        }

        // ------------------------------------------------------------------ behaviour
        static bool NoFlicker { get { return SkyIslandLightManager.noFlicker; } }

        bool IsGas { get { return kind == Kind.HPS || kind == Kind.MH; } }

        /// <summary>Steady output for the current voltage / battery.</summary>
        float SteadyTarget()
        {
            if (!_supplied || Broken) return 0f;
            float v = Mathf.Min(_voltage, 1.25f);
            switch (kind)
            {
                case Kind.Fluorescent: return v < 0.65f ? 0f : Mathf.Pow(v, 1.5f);
                case Kind.Incandescent: return Mathf.Pow(v, 3.4f);
                case Kind.Halogen: return Mathf.Pow(v, 3.0f);
                case Kind.HPS:
                case Kind.MH: return v < 0.85f ? 0f : v * v;
                case Kind.LED:
                case Kind.Beacon: return v < 0.7f ? 0f : 1f;
                case Kind.Exit:
                case Kind.Emergency: return _battery <= 0f ? 0f : (_battery > 0.15f ? 1f : _battery / 0.15f);
                default: return 1f;   // flame
            }
        }

        /// <summary>Steady colour temperature factor (filament lamps warm up under low voltage).</summary>
        float SteadyK()
        {
            float v = Mathf.Clamp(_voltage, 0.05f, 1.25f);
            if (kind == Kind.Incandescent) return Mathf.Pow(v, 0.42f);
            if (kind == Kind.Halogen) return Mathf.Pow(v, 0.35f);
            return 1f;
        }

        void BeginStart(float now, float delay)
        {
            if (Broken) return;
            _burst = false;
            _phase = Phase.Starting;
            _t0 = now + delay;
            _segEnd.Clear(); _segLevel.Clear();
            if (kind == Kind.Fluorescent && !NoFlicker) BuildStarterSequence();
            if (IsGas && now + delay < _coolUntil) { _phase = Phase.Cooling; _t0 = _coolUntil; }
            if (delay <= 0f && _phase == Phase.Starting) Raise(Event.Starting);
        }

        void BeginStop(float now)
        {
            if (_phase == Phase.Off) return;
            _fromLevel = _level;
            _fromK = _kFactor;
            if (IsGas)
            {
                float target = Mathf.Max(0.05f, SteadyTargetFor(true));
                float heat = Mathf.Clamp01(_level / target);
                _coolUntil = now + restrike * heat;
            }
            _burst = false;
            _phase = Phase.Stopping;
            _t0 = now;
        }

        /// <summary>Steady target as if supplied (used when the supply just went away).</summary>
        float SteadyTargetFor(bool supplied)
        {
            bool s = _supplied;
            _supplied = supplied;
            float t = SteadyTarget();
            _supplied = s;
            return t;
        }

        /// <summary>Starter: 0.1-0.2 s dark, then 1-3 short blinks, then on (steady after 0.5-1.55 s). Blink onsets and the final
        /// strike are at least 0.36 s apart, so no one-second window holds more than 3 flashes.</summary>
        void BuildStarterSequence()
        {
            float t = 0.1f + Rand() * 0.1f;
            _segEnd.Add(t); _segLevel.Add(0f);
            int blinks = 1 + (int)(Rand() * 2.999f);
            for (int b = 0; b < blinks; b++)
            {
                t += 0.05f + Rand() * 0.03f; _segEnd.Add(t); _segLevel.Add(0.55f + Rand() * 0.4f);
                t += 0.31f + Rand() * 0.06f; _segEnd.Add(t); _segLevel.Add(0f);
            }
        }

        /// <summary>Fault burst of a failing tube: 2-5 dips at least 0.34 s apart; sometimes the tube then goes out for
        /// 1.5-6 s and restarts.</summary>
        void BuildFaultBurst(float now)
        {
            _segEnd.Clear(); _segLevel.Clear();
            float t = 0f;
            int dips = 2 + (int)(Rand() * 3.999f);
            for (int d = 0; d < dips; d++)
            {
                t += 0.06f + Rand() * 0.08f; _segEnd.Add(t); _segLevel.Add(0.04f + Rand() * 0.25f);
                t += 0.28f + Rand() * 0.35f; _segEnd.Add(t); _segLevel.Add(1f);
            }
            _burstThenOut = Rand() < 0.25f;
            if (_burstThenOut) { t += 1.5f + Rand() * 4.5f; _segEnd.Add(t); _segLevel.Add(0f); }
            _burst = true;
            _burstT0 = now;
        }

        void ScheduleFault(float now)
        {
            bool lowVolt = kind == Kind.Fluorescent && _voltage < 0.85f;
            if (kind != Kind.Fluorescent || NoFlicker || (!faulty && !lowVolt)) { _nextFault = float.MaxValue; return; }
            _nextFault = now + (lowVolt && !faulty ? 1f + Rand() * 4f : 3f + Rand() * 11f);
        }

        /// <summary>Level of a segment list at time t (relative), or -1 when past its end.</summary>
        float SegmentLevel(float t)
        {
            for (int i = 0; i < _segEnd.Count; i++)
                if (t < _segEnd[i]) return _segLevel[i];
            return -1f;
        }

        bool NeedsTick()
        {
            if (Broken) return false;
            switch (_phase)
            {
                case Phase.Off: return false;
                case Phase.On:
                    if (kind == Kind.Flame) return !NoFlicker;
                    if (kind == Kind.Beacon && flash.x > 0.05f) return true;
                    if ((kind == Kind.Exit || kind == Kind.Emergency) && _battery < 0.15f) return true;
                    if (_nextFault < float.MaxValue || _burst) return true;
                    return Mathf.Abs(_level - SteadyTarget()) > 0.002f || Mathf.Abs(_kFactor - SteadyK()) > 0.002f;
                default: return true;
            }
        }

        void Update()
        {
            float now = Time.time;
            Tick(now, Time.deltaTime);
            ApplyAll(false);
            if (!NeedsTick()) enabled = false;
        }

        void Tick(float now, float dt)
        {
            if (Broken) { _level = 0f; return; }
            float target = SteadyTarget();
            switch (_phase)
            {
                case Phase.Off:
                    _level = 0f;
                    if (_supplied && target > 0f) BeginStart(now, 0f);   // voltage came back
                    break;
                case Phase.Cooling:
                    _level = 0f;
                    if (now >= _t0) { _phase = Phase.Starting; _t0 = now; Raise(Event.Starting); }
                    break;
                case Phase.Starting:
                    TickStart(now, target);
                    break;
                case Phase.Stopping:
                    TickStop(now);
                    break;
                case Phase.On:
                    TickOn(now, dt, target);
                    break;
            }
        }

        void TickStart(float now, float target)
        {
            float t = now - _t0;
            if (t < 0f) { _level = 0f; return; }   // start delay
            if (target <= 0f) { _level = 0f; if (!_supplied) { _phase = Phase.Off; } return; }
            switch (kind)
            {
                case Kind.Fluorescent:
                    if (NoFlicker) { _level = target * Mathf.Clamp01(t / 0.25f); if (t >= 0.25f) FinishStart(now, target); break; }
                    float s = SegmentLevel(t);
                    if (s < 0f) { FinishStart(now, target); break; }
                    if (s > 0f && _level <= 0f) Raise(Event.Blink);
                    _level = s * target;
                    break;
                case Kind.Incandescent:
                case Kind.Halogen:
                {
                    float tau = kind == Kind.Halogen ? 0.05f : 0.07f;
                    float e = 1f - Mathf.Exp(-t / tau);
                    _level = target * e;
                    _kFactor = Mathf.Lerp(0.55f, 1f, e) * SteadyK();
                    if (t > tau * 6f) FinishStart(now, target);
                    break;
                }
                case Kind.HPS:
                case Kind.MH:
                {
                    float w = Mathf.Max(0.5f, warmup);
                    float p = Mathf.Clamp01(t / w);
                    float sm = p * p * (3f - 2f * p);
                    _level = target * (0.06f + 0.94f * sm);
                    _kFactor = Mathf.Lerp(kind == Kind.HPS ? 1.6f : 1.5f, 1f, sm);
                    if (p >= 1f) FinishStart(now, target);
                    break;
                }
                case Kind.Emergency:
                    _level = target * Mathf.Clamp01(t / 0.15f);
                    if (t >= 0.15f) FinishStart(now, target);
                    break;
                case Kind.Flame:
                    _level = target * Mathf.Clamp01(t / 1.2f) * FlameNoise(now);
                    if (t >= 1.2f) FinishStart(now, target);
                    break;
                default:   // LED, Exit, Beacon
                    _level = target * Mathf.Clamp01(t / 0.03f);
                    if (t >= 0.03f) FinishStart(now, target);
                    break;
            }
        }

        void FinishStart(float now, float target)
        {
            _phase = Phase.On;
            _level = target;
            _kFactor = SteadyK();
            _segEnd.Clear(); _segLevel.Clear();
            ScheduleFault(now);
            Raise(Event.On);
        }

        void TickStop(float now)
        {
            float t = now - _t0;
            float tau;
            switch (kind)
            {
                case Kind.Incandescent: tau = 0.08f; break;   // light output; the dull red glow (kFactor) is gone in ~0.4 s
                case Kind.Halogen: tau = 0.06f; break;
                case Kind.Flame: tau = 0.25f; break;
                case Kind.HPS:
                case Kind.MH: tau = 0.03f; break;
                default: tau = 0.012f; break;
            }
            float e = Mathf.Exp(-t / tau);
            _level = _fromLevel * e;
            if (kind == Kind.Incandescent || kind == Kind.Halogen) _kFactor = _fromK * Mathf.Lerp(0.5f, 1f, e);
            if (_level < 0.004f)
            {
                _level = 0f;
                _kFactor = 1f;
                _phase = Phase.Off;
                Raise(Event.Off);
            }
        }

        void TickOn(float now, float dt, float target)
        {
            if (target <= 0f)
            {
                // voltage collapsed (gas lamps go out under 85 %, tubes under 65 %): stop now, restart when it recovers
                BeginStop(now);
                return;
            }
            float baseLevel = target;
            if (kind == Kind.Fluorescent)
            {
                if (_burst)
                {
                    float s = SegmentLevel(now - _burstT0);
                    if (s < 0f)
                    {
                        _burst = false;
                        if (_burstThenOut) { BeginStart(now, 0f); return; }   // the tube restarts with its starter
                        ScheduleFault(now);
                    }
                    else baseLevel = target * s;
                }
                else if (now >= _nextFault)
                {
                    BuildFaultBurst(now);
                    Raise(Event.Fault);
                    float s = SegmentLevel(0f);
                    baseLevel = target * (s < 0f ? 1f : s);
                }
                else if (_nextFault == float.MaxValue && (faulty || _voltage < 0.85f)) ScheduleFault(now);
                _level = baseLevel;
                return;
            }
            if (kind == Kind.Flame)
            {
                _level = NoFlicker ? target * 0.9f : target * FlameNoise(now);
                _kFactor = NoFlicker ? 1f : 0.97f + 0.06f * Mathf.PerlinNoise(_seed + 7.7f, now * 1.1f);
                return;
            }
            if (kind == Kind.Beacon && flash.x > 0.05f)
            {
                float period = flash.x;
                float duty = Mathf.Clamp(flash.y > 0f ? flash.y : 0.5f, 0.05f, 0.95f);
                float ph = Mathf.Repeat(now + _seed, period);
                float on = duty * period;
                float edge = 0.05f;
                float l = ph < on ? Mathf.Clamp01(Mathf.Min(ph / edge, (on - ph) / edge)) : 0f;
                _level = target * l;
                return;
            }
            if ((kind == Kind.Exit || kind == Kind.Emergency) && _battery < 0.15f && !NoFlicker)
            {
                // nearly flat battery: the lamp sags and wavers (smooth, no flashes)
                _level = target * (0.55f + 0.45f * Mathf.PerlinNoise(_seed, now * 1.7f));
                return;
            }
            // steady: follow voltage changes smoothly
            float k = 1f - Mathf.Exp(-dt / 0.1f);
            _level = Mathf.Lerp(_level, target, k);
            _kFactor = Mathf.Lerp(_kFactor, SteadyK(), k);
            if (Mathf.Abs(_level - target) < 0.002f) _level = target;
        }

        float FlameNoise(float now)
        {
            if (NoFlicker) return 0.9f;
            float n = Mathf.PerlinNoise(_seed, now * 1.3f) * 0.7f + Mathf.PerlinNoise(_seed + 3.3f, now * 2.9f) * 0.3f;
            return 0.78f + 0.22f * n;
        }

        // ------------------------------------------------------------------ output
        void ApplyAll(bool force)
        {
            ApplyLights();
            ApplyMaterials(force);
        }

        void ApplyLights()
        {
            float lit = Broken ? 0f : _level;
            int n = Mathf.Min(lights.Length, _baseI.Length);
            for (int i = 0; i < n; i++)
            {
                var l = lights[i];
                if (l == null) continue;
                float inten = _baseI[i] * lit * _lodFade;
                bool en = inten > 1e-4f;
                if (l.enabled != en) l.enabled = en;
                if (!en) continue;
                if (!Mathf.Approximately(l.intensity, inten)) l.intensity = inten;
                if (_useK[i])
                {
                    float k = Mathf.Clamp(_baseK[i] * _kFactor, 1000f, 20000f);
                    if (!Mathf.Approximately(l.colorTemperature, k)) l.colorTemperature = k;
                }
            }
        }

        void ApplyMaterials(bool force)
        {
            float lit = Broken ? 0f : _level;
            if (!force && Mathf.Abs(lit - _appliedLit) < 0.002f && Mathf.Abs(_kFactor - _appliedK) < 0.002f) return;
            _appliedLit = lit;
            _appliedK = _kFactor;
            Color tint = Tint();
            for (int i = 0; i < _emissive.Count; i++)
            {
                var s = _emissive[i];
                if (s.copy == null) continue;
                var c = s.baseColor;
                s.copy.SetColor(s.prop, new Color(c.r * lit * tint.r, c.g * lit * tint.g, c.b * lit * tint.b, c.a));
            }
            for (int i = 0; i < _halos.Count; i++)
            {
                var h = _halos[i];
                if (h.r == null || h.copy == null) continue;
                bool vis = lit > 0.005f;
                if (h.r.enabled != vis) h.r.enabled = vis;
                if (!vis) continue;
                h.copy.SetFloat(IntensityId, h.baseIntensity * lit);
                if (h.hasColor)
                {
                    var c = h.baseColor;
                    h.copy.SetColor(ColorId, new Color(c.r * tint.r, c.g * tint.g, c.b * tint.b, c.a));
                }
            }
        }

        /// <summary>Colour shift of emission and halo when the colour temperature moves (filament warm-up, brownout, gas
        /// lamp warm-up): Unity's temperature colour at the current temperature over the one at the lamp's own.</summary>
        Color Tint()
        {
            if (_kelvin <= 0f || Mathf.Abs(_kFactor - 1f) < 0.002f) return Color.white;
            var a = Mathf.CorrelatedColorTemperatureToRGB(Mathf.Clamp(_kelvin * _kFactor, 1000f, 20000f));
            var b = Mathf.CorrelatedColorTemperatureToRGB(_kelvin);
            return new Color(a.r / Mathf.Max(b.r, 1e-3f), a.g / Mathf.Max(b.g, 1e-3f), a.b / Mathf.Max(b.b, 1e-3f), 1f);
        }

        void Raise(Event e)
        {
            if (onEvent != null) onEvent(this, e);
            if (Manager != null) Manager.RaiseFixtureEvent(this, e);
        }

        /// <summary>xorshift32, seeded from the fixture position: the same lamp behaves the same in every run.</summary>
        float Rand()
        {
            _rng ^= _rng << 13;
            _rng ^= _rng >> 17;
            _rng ^= _rng << 5;
            return (_rng & 0xFFFFFF) / 16777216f;
        }
    }
}
