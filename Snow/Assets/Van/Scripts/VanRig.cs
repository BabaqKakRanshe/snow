using System;
using System.Collections.Generic;
using UnityEngine;

namespace UazVan
{
    /// <summary>
    /// Presentation of the UAZ van (VAN3_CONTRACT.md, Data/*.json): your vehicle, electrics and interaction code writes
    /// values, the rig moves every part, needle, wheel, lamp and pane of the model. It never computes physics, never
    /// owns game state and never talks to the network: write the replicated state into it on every peer.
    ///
    /// Values are the contract's: degrees for hinges / twists / spins, metres for slides, the scale for stretches.
    /// The model sits at value = rest; a hinge turns by (value - rest) about rotAxis, a slide moves by (value - rest)
    /// along dir, a stretch scales by value along dir from its pivot. Hidden parts (removed items, the broken belt,
    /// chains not fitted, no snow) are inactive GameObjects.
    ///
    /// Partial classes: VanRig.Lights.cs (41 lamps, circuits, IES cookies, glows / beams, flicker),
    /// VanRig.Glass.cs (frost, snow, fog, hand prints, wipers, mirror, radio display).
    /// Tools/UAZ Van/Build Materials + Prefab fills the tables below; do not edit them by hand.
    /// </summary>
    [DisallowMultipleComponent]
    public partial class VanRig : MonoBehaviour
    {
        public enum PartKind { Static, Hinge, Twist, Slide, Item, Spin, Stretch }
        public enum Gauge { Speedo, Tacho, Fuel, Temp, Oil, Volt }

        [Serializable]
        public class Part
        {
            public string name;
            public PartKind kind;
            public Transform t;
            [Tooltip("Stretch: the node that scales (the part, or Stretch_<part> when the axis is not a local axis)")]
            public Transform scaleNode;
            [Tooltip("Stretch: the local axis of scaleNode that is scaled (0 x, 1 y, 2 z)")]
            public int scaleAxis = 2;
            public Vector3 rotAxis, dir, rotAxis2, liftDir;
            public bool hasRange;
            public float min, max, rest;
            public Vector3 restPos;
            public Quaternion restRot = Quaternion.identity;
            public Vector3 restScale = Vector3.one;
            public bool defaultHidden;
            public bool hasScale;
            public float scaleMin, scaleMax, angleMin, angleMax;
            public string blendshape;
            public SkinnedMeshRenderer skin;
            public int blendIndex = -1;
        }

        [Serializable]
        public class GearPose { public string name; public float sideDeg, foreAftDeg, lift; }

        [Serializable]
        public class SocketRef { public string name, kind, pane; public Transform t; }

        [Header("Model tables (filled by Tools/UAZ Van/Build Materials + Prefab)")]
        public List<Part> parts = new List<Part>();
        public List<GearPose> gears = new List<GearPose>();
        public List<SocketRef> sockets = new List<SocketRef>();
        [Tooltip("Steering ratio: steering wheel degrees per road-wheel degree")] public float steeringRatio = 16.875f;
        [Tooltip("Road wheel lock, degrees")] public float maxSteerDeg = 32f;

        [Header("Needle dynamics (second order, like a real movement)")]
        public float needleOmega = 9f;
        [Range(0.2f, 1.5f)] public float needleDamping = 0.7f;
        [Tooltip("Gear lever and pedals follow their targets in about this time (s); 0 = at once")]
        public float controlLag = 0.08f;

        [Header("Engine and wheels (visual)")]
        [Tooltip("Engine speed for the fan (x 1.2) and the idle shake")] public float engineRpm;
        [Tooltip("Engine shake at idle, degrees (0 = none)")] public float engineShakeDeg = 0.25f;

        // ------------------------------------------------------------------ state
        float[] _value, _target, _vel;
        bool[] _dirty;
        readonly Dictionary<string, int> _index = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly Dictionary<string, Transform> _socket = new Dictionary<string, Transform>(StringComparer.Ordinal);
        readonly List<int> _smoothed = new List<int>();             // needles / lever / pedals moving to a target
        int _steerFL = -1, _steerFR = -1, _wheelSteer = -1, _fan = -1, _engine = -1, _lever = -1;
        readonly int[] _wheel = { -1, -1, -1, -1 };
        readonly int[] _wheelNode = { -1, -1, -1, -1 };               // the node that rides the suspension
        readonly float[] _wheelSpin = new float[4], _wheelComp = new float[4];
        readonly bool[] _wheelDirty = { true, true, true, true };
        float _leverSide, _leverFa, _leverLift, _leverSideT, _leverFaT, _leverLiftT;
        float _fanAngle;
        bool _ready;

        public static readonly string[] WheelNames = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };
        public static readonly string[] SeatNames = { "L1", "L2", "L3", "L4", "R1", "R2" };
        public static readonly string[] CurtainNames = { "L1", "L2", "L3", "R1", "R2", "R3" };
        public static readonly string[] SnowNames = { "Roof", "Visor", "Bumper", "Head_L", "Head_R", "Spot_L", "Spot_R", "Fog_L", "Fog_R" };

        void Awake() => Init();

        void Init()
        {
            if (_ready) return;
            _ready = true;
            int n = parts.Count;
            _value = new float[n];
            _target = new float[n];
            _vel = new float[n];
            _dirty = new bool[n];
            _index.Clear();
            for (int i = 0; i < n; i++)
            {
                var p = parts[i];
                if (p == null || string.IsNullOrEmpty(p.name)) continue;
                _index[p.name] = i;
                _value[i] = _target[i] = p.rest;
                if (p.t != null && p.defaultHidden) p.t.gameObject.SetActive(false);
            }
            if (n == 0)
                Debug.LogError("[UazVan] VanRig has no parts: run Tools/UAZ Van/Build Materials + Prefab and use Prefabs/Van.prefab.", this);
            _socket.Clear();
            foreach (var s in sockets)
                if (s != null && s.t != null && !_socket.ContainsKey(s.name)) _socket[s.name] = s.t;
            _steerFL = Index("Steer_FL");
            _steerFR = Index("Steer_FR");
            _wheelSteer = Index("SteeringWheel");
            _fan = Index("Engine_Fan");
            _engine = Index("Engine");
            _lever = Index("GearLever");
            for (int w = 0; w < 4; w++)
            {
                _wheel[w] = Index(WheelNames[w]);
                _wheelNode[w] = w < 2 ? Index(w == 0 ? "Steer_FL" : "Steer_FR") : _wheel[w];
            }
            InitLighting();
            InitGlass();
        }

        void OnEnable()
        {
            Init();
            ResyncPose();
        }

        void OnDisable() => SuspendLighting();

        void OnDestroy()
        {
            DestroyLighting();
            DestroyGlass();
        }

        /// <summary>Writes every part again (after a network state sync, a teleport or re-enabling the van).</summary>
        public void ResyncPose()
        {
            if (_dirty == null) return;
            for (int i = 0; i < _dirty.Length; i++) _dirty[i] = true;
            for (int w = 0; w < 4; w++) _wheelDirty[w] = true;
            ResyncLighting();
            ResyncGlass();
        }

        // ------------------------------------------------------------------ lookup
        /// <summary>Index of a part in <see cref="parts"/> (-1 if there is none).</summary>
        public int Index(string part) => part != null && _index.TryGetValue(part, out int i) ? i : -1;

        /// <summary>The part's transform (null if there is none).</summary>
        public Transform PartTransform(string part)
        {
            Init();
            int i = Index(part);
            return i >= 0 ? parts[i].t : null;
        }

        /// <summary>A socket of sockets.json (hand / foot / pour / item_slot / sit / audio / vfx / knock / decal / hide /
        /// heat / jack / camera). Its +Z = the socket's dir (or facing), +Y = up.</summary>
        public Transform Socket(string name)
        {
            Init();
            return name != null && _socket.TryGetValue(name, out var t) ? t : null;
        }

        public IEnumerable<SocketRef> SocketsOfKind(string kind)
        {
            foreach (var s in sockets)
                if (s != null && s.kind == kind) yield return s;
        }

        // ------------------------------------------------------------------ parts
        /// <summary>Current value of a part (contract units).</summary>
        public float GetPart(string part)
        {
            Init();
            int i = Index(part);
            return i >= 0 ? _value[i] : 0f;
        }

        /// <summary>Sets a part at once (contract units; clamped to its range). Spins wrap at 360.</summary>
        public void SetPart(string part, float value) => SetPart(Index(part), value);

        public void SetPart(int i, float value)
        {
            Init();
            if (i < 0 || i >= parts.Count) return;
            var p = parts[i];
            if (p.kind == PartKind.Spin) value = Mathf.Repeat(value, 360f);
            else if (p.hasRange) value = Mathf.Clamp(value, Mathf.Min(p.min, p.max), Mathf.Max(p.min, p.max));
            _target[i] = value;
            _smoothed.Remove(i);
            if (_value[i] == value && !_dirty[i]) return;
            _value[i] = value;
            _vel[i] = 0f;
            _dirty[i] = true;
        }

        /// <summary>0..1 of the part's range (doors, lids, toggles, slides, curtains): 0 = min, 1 = max.</summary>
        public void SetPart01(string part, float t)
        {
            Init();
            int i = Index(part);
            if (i < 0) return;
            var p = parts[i];
            SetPart(i, p.hasRange ? Mathf.Lerp(p.min, p.max, Mathf.Clamp01(t)) : p.rest);
        }

        public float GetPart01(string part)
        {
            Init();
            int i = Index(part);
            if (i < 0) return 0f;
            var p = parts[i];
            return p.hasRange && p.max != p.min ? Mathf.InverseLerp(p.min, p.max, _value[i]) : 0f;
        }

        /// <summary>Shows or hides a part with its children (a removed item, the broken belt, fitted chains, snow).</summary>
        public void SetVisible(string part, bool visible)
        {
            Init();
            int i = Index(part);
            if (i < 0 || parts[i].t == null) return;
            var go = parts[i].t.gameObject;
            if (go.activeSelf != visible) go.SetActive(visible);
        }

        public bool IsVisible(string part)
        {
            Init();
            int i = Index(part);
            return i >= 0 && parts[i].t != null && parts[i].t.gameObject.activeSelf;
        }

        /// <summary>A tool of the van set up somewhere else (the jack under a Jack_Point_*, the blowtorch under the oil pan):
        /// the part, with its moving children (ram, screw, pump, valve), zones and sockets, moves to this world pose and stays
        /// there relative to the van until UnplacePart. Call it again (every frame if you like) to follow the ground.</summary>
        public void PlacePart(string part, Vector3 worldPosition, Quaternion worldRotation)
        {
            Init();
            int i = Index(part);
            if (i < 0 || parts[i].t == null) return;
            parts[i].t.SetPositionAndRotation(worldPosition, worldRotation);
            if (!parts[i].t.gameObject.activeSelf) parts[i].t.gameObject.SetActive(true);
        }

        /// <summary>Back to its place in the van (the pose of the model).</summary>
        public void UnplacePart(string part)
        {
            Init();
            int i = Index(part);
            if (i < 0 || parts[i].t == null) return;
            parts[i].t.localPosition = parts[i].restPos;
            parts[i].t.localRotation = parts[i].restRot;
            _dirty[i] = true;
        }

        /// <summary>Moves a part towards a value with the needle dynamics (gauges) or the control lag (levers).</summary>
        void SetSmoothed(int i, float value)
        {
            if (i < 0) return;
            var p = parts[i];
            if (p.hasRange) value = Mathf.Clamp(value, Mathf.Min(p.min, p.max), Mathf.Max(p.min, p.max));
            _target[i] = value;
            if (!_smoothed.Contains(i)) _smoothed.Add(i);
        }

        // ------------------------------------------------------------------ driving controls
        /// <summary>Road-wheel steering angle, degrees (+ = left, as the contract's Steer_FL / FR). Turns the front
        /// wheels and the steering wheel (x steeringRatio, +-1.5 turns).</summary>
        public void SetSteer(float roadWheelDeg)
        {
            Init();
            float a = Mathf.Clamp(roadWheelDeg, -maxSteerDeg, maxSteerDeg);
            SetPart(_steerFL, a);
            SetPart(_steerFR, a);
            SetPart(_wheelSteer, a * steeringRatio);
        }

        /// <summary>Wheel i (0 FL, 1 FR, 2 RL, 3 RR): spin angle (degrees, + rolls forward; send the running angle,
        /// it wraps) and suspension travel (metres, + = wheel up into the arch, 0 = as modelled).</summary>
        public void SetWheel(int i, float spinDeg, float compression)
        {
            Init();
            if (i < 0 || i > 3) return;
            spinDeg = Mathf.Repeat(spinDeg, 360f);
            if (_wheelSpin[i] == spinDeg && _wheelComp[i] == compression) return;
            _wheelSpin[i] = spinDeg;
            _wheelComp[i] = compression;
            _wheelDirty[i] = true;
        }

        /// <summary>Flat tyre 0..1 (blend shape Flat).</summary>
        public void SetFlat(int i, float amount)
        {
            Init();
            if (i < 0 || i > 3) return;
            var p = _wheel[i] >= 0 ? parts[_wheel[i]] : null;
            if (p == null || p.skin == null || p.blendIndex < 0) return;
            p.skin.SetBlendShapeWeight(p.blendIndex, 100f * Mathf.Clamp01(amount));
        }

        /// <summary>Pedals 0..1 (pressed = the contract's max angle).</summary>
        public void SetPedals(float clutch, float brake, float gas)
        {
            Init();
            Pedal("Pedal_Clutch", clutch);
            Pedal("Pedal_Brake", brake);
            Pedal("Pedal_Gas", gas);
        }

        void Pedal(string n, float t)
        {
            int i = Index(n);
            if (i < 0) return;
            var p = parts[i];
            float v = Mathf.Lerp(p.min, p.max, Mathf.Clamp01(t));
            if (controlLag <= 0f) SetPart(i, v); else SetSmoothed(i, v);
        }

        /// <summary>Gear lever to a position of the H pattern: "N", "1", "2", "3", "4", "R".</summary>
        public void SetGearLever(string gear)
        {
            Init();
            foreach (var g in gears)
                if (g.name == gear)
                {
                    SetGearLeverAngles(g.sideDeg, g.foreAftDeg, g.lift);
                    return;
                }
        }

        /// <summary>Gear lever by angles: side (about the body's forward axis), fore-aft (about the lateral axis), lift (m).</summary>
        public void SetGearLeverAngles(float sideDeg, float foreAftDeg, float lift)
        {
            Init();
            _leverSideT = sideDeg;
            _leverFaT = foreAftDeg;
            _leverLiftT = lift;
            if (controlLag <= 0f) { _leverSide = sideDeg; _leverFa = foreAftDeg; _leverLift = lift; }
            if (_lever >= 0) _dirty[_lever] = true;
        }

        /// <summary>Gauge needle to a reading (units of the dial: km/h, rpm, tank fraction 0..1, deg C, kgf/cm2, volts).
        /// The needle follows with its own dynamics (needleOmega, needleDamping).</summary>
        public void SetGauge(Gauge g, float reading)
        {
            Init();
            int i = Index("Needle_" + GaugeName(g));
            if (i < 0) return;
            var p = parts[i];
            if (!p.hasScale || p.scaleMax == p.scaleMin) return;
            float u = Mathf.Clamp01((reading - p.scaleMin) / (p.scaleMax - p.scaleMin));
            SetSmoothed(i, Mathf.Lerp(p.angleMin, p.angleMax, u));
        }

        public static string GaugeName(Gauge g)
        {
            switch (g)
            {
                case Gauge.Speedo: return "speedo";
                case Gauge.Tacho: return "tacho";
                case Gauge.Fuel: return "fuel";
                case Gauge.Temp: return "temp";
                case Gauge.Oil: return "oil";
                default: return "volt";
            }
        }

        // ------------------------------------------------------------------ cabin furniture and snow
        /// <summary>Troop seat L1..L4, R1, R2: 0 = down (sit on it), 1 = folded to the wall. Pan and leg move together.</summary>
        public void SetSeatFold(string seat, float fold01)
        {
            Init();
            float t = Mathf.Clamp01(fold01);
            SetPart01("Seat_" + seat, t);
            SetPart01("Seat_" + seat + "_Leg", t);
        }

        /// <summary>Curtains of window L1..L3 / R1..R3: 0 = open, 1 = closed (both halves meet).</summary>
        public void SetCurtain(string window, float close01)
        {
            Init();
            SetPart01("Curtain_" + window + "A", close01);
            SetPart01("Curtain_" + window + "B", close01);
        }

        /// <summary>Snow 0..1 on Roof (7 cm), Visor, Bumper (blend shape Clear) or on a lamp Head_L / Head_R / Spot_L /
        /// Spot_R / Fog_L / Fog_R (the cap scales along the lamp; the lamp loses up to 85 % of its light).
        /// 0 hides the snow object.</summary>
        public void SetSnow(string where, float amount)
        {
            Init();
            int i = Index("Snow_" + where);
            if (i < 0) return;
            var p = parts[i];
            float a = Mathf.Clamp01(amount);
            bool show = a > 0.001f;
            if (p.t != null && p.t.gameObject.activeSelf != show) p.t.gameObject.SetActive(show);
            if (p.kind == PartKind.Stretch)
                SetPart(i, Mathf.Lerp(p.min, p.max, a));
            else if (p.skin != null && p.blendIndex >= 0)
                p.skin.SetBlendShapeWeight(p.blendIndex, 100f * (1f - a));
            _value[i] = p.kind == PartKind.Stretch ? _value[i] : a;
            SnowChangedLighting();
        }

        /// <summary>Snow amount 0..1 last set on a snow part (lamps: from its scale).</summary>
        public float GetSnow(string where)
        {
            Init();
            int i = Index("Snow_" + where);
            if (i < 0 || parts[i].t == null || !parts[i].t.gameObject.activeSelf) return 0f;
            var p = parts[i];
            return p.kind == PartKind.Stretch ? Mathf.InverseLerp(p.min, p.max, _value[i]) : _value[i];
        }

        // ------------------------------------------------------------------ per frame
        void Update()
        {
            float dt = Time.deltaTime;
            UpdateLighting(dt);
            UpdateGlass(dt);
        }

        void LateUpdate()
        {
            if (!_ready) return;
            float dt = Time.deltaTime;
            // needles, lever and pedals towards their targets
            for (int k = _smoothed.Count - 1; k >= 0; k--)
            {
                int i = _smoothed[k];
                bool needle = parts[i].name.StartsWith("Needle_", StringComparison.Ordinal);
                float x = _value[i], tgt = _target[i];
                if (needle)
                {
                    // x'' = w^2 (tgt - x) - 2 z w x'   (semi-implicit Euler, sub-stepped)
                    int steps = Mathf.Max(1, Mathf.CeilToInt(dt / 0.01f));
                    float h = dt / steps;
                    for (int s = 0; s < steps; s++)
                    {
                        _vel[i] += (needleOmega * needleOmega * (tgt - x) - 2f * needleDamping * needleOmega * _vel[i]) * h;
                        x += _vel[i] * h;
                    }
                }
                else x = controlLag > 0f ? Mathf.Lerp(x, tgt, 1f - Mathf.Exp(-dt / controlLag)) : tgt;
                if (Mathf.Abs(x - tgt) < 1e-3f && Mathf.Abs(_vel[i]) < 1e-2f) { x = tgt; _vel[i] = 0f; _smoothed.RemoveAt(k); }
                _value[i] = x;
                _dirty[i] = true;
            }
            if (_lever >= 0 && controlLag > 0f)
            {
                float a = 1f - Mathf.Exp(-dt / controlLag);
                float s0 = _leverSide, f0 = _leverFa, l0 = _leverLift;
                _leverSide = Mathf.Lerp(_leverSide, _leverSideT, a);
                _leverFa = Mathf.Lerp(_leverFa, _leverFaT, a);
                _leverLift = Mathf.Lerp(_leverLift, _leverLiftT, a);
                if (s0 != _leverSide || f0 != _leverFa || l0 != _leverLift) _dirty[_lever] = true;
            }
            // engine fan and idle shake
            if (_fan >= 0 && engineRpm > 1f)
            {
                _fanAngle = Mathf.Repeat(_fanAngle + engineRpm * 1.2f * 6f * dt, 360f);
                _value[_fan] = _fanAngle;
                _dirty[_fan] = true;
            }
            if (_engine >= 0 && parts[_engine].t != null)
            {
                var e = parts[_engine];
                float shake = engineRpm > 200f && engineShakeDeg > 0f
                    ? engineShakeDeg * Mathf.Clamp01(1.4f - engineRpm / 2500f) * Mathf.Sin(Time.time * engineRpm / 60f * 2f * Mathf.PI * 2f)
                    : 0f;
                e.t.localRotation = e.restRot * Quaternion.AngleAxis(shake, Vector3.forward);
            }
            // parts
            for (int i = 0; i < _dirty.Length; i++)
            {
                if (!_dirty[i]) continue;
                _dirty[i] = false;
                Apply(i);
            }
            for (int w = 0; w < 4; w++)
                if (_wheelDirty[w]) { _wheelDirty[w] = false; ApplyWheel(w); }
            LateUpdateGlass(dt);
        }

        void Apply(int i)
        {
            var p = parts[i];
            if (p.t == null) return;
            float v = _value[i];
            switch (p.kind)
            {
                case PartKind.Hinge:
                case PartKind.Twist:
                    if (i == _lever)
                    {
                        p.t.localRotation = p.restRot * Quaternion.AngleAxis(_leverSide, p.rotAxis2) *
                                            Quaternion.AngleAxis(_leverFa, p.rotAxis);
                        p.t.localPosition = p.restPos + p.liftDir * _leverLift;
                    }
                    else if (i == _steerFL || i == _steerFR)
                        p.t.localRotation = p.restRot * Quaternion.AngleAxis(v - p.rest, p.rotAxis);   // position: ApplyWheel
                    else
                        p.t.localRotation = p.restRot * Quaternion.AngleAxis(v - p.rest, p.rotAxis);
                    break;
                case PartKind.Spin:
                    if (Array.IndexOf(_wheel, i) >= 0) break;                                       // wheels: ApplyWheel
                    p.t.localRotation = p.restRot * Quaternion.AngleAxis(v, p.rotAxis);
                    break;
                case PartKind.Slide:
                case PartKind.Item:
                    if (p.hasRange) p.t.localPosition = p.restPos + p.dir * (v - p.rest);
                    break;
                case PartKind.Stretch:
                {
                    var node = p.scaleNode != null ? p.scaleNode : p.t;
                    float s = p.rest != 0f ? v / p.rest : v;
                    Vector3 sc = node == p.t ? p.restScale : Vector3.one;
                    if (p.scaleAxis == 0) sc.x *= s; else if (p.scaleAxis == 1) sc.y *= s; else sc.z *= s;
                    node.localScale = sc;
                    break;
                }
            }
        }

        void ApplyWheel(int w)
        {
            int wi = _wheel[w], ni = _wheelNode[w];
            if (wi < 0) return;
            var wp = parts[wi];
            if (wp.t == null) return;
            if (ni >= 0 && parts[ni].t != null)
            {
                var np = parts[ni];
                np.t.localPosition = np.restPos + Vector3.up * _wheelComp[w];
            }
            wp.t.localRotation = wp.restRot * Quaternion.AngleAxis(_wheelSpin[w], wp.rotAxis);
        }
    }
}
