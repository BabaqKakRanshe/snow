using System;
using System.Collections.Generic;
using UnityEngine;

namespace SkyIsland
{
    /// <summary>
    /// Lights of a location: supplies every <see cref="SkyIslandLightFixture"/> under the root from the power grid, the room
    /// switches and the batteries, and keeps the realtime light count down.
    /// <list type="bullet">
    /// <item>Rooms: each room is a light zone (its static geometry has its own rendering layer, so its lamps light nothing
    /// behind its walls). Interactables whose name starts with the room's switch prefix toggle its switchable lamps (each
    /// toggle flips the room, so two switches work as a two-way pair).</item>
    /// <item>Power: Grid lamps need <see cref="grid"/> (none = always powered) and their room on; exit signs run on battery;
    /// emergency lights come on only while the grid is down; <see cref="gridVoltage"/> under 1 browns the lamps out; when the
    /// grid comes back the lamps start spread over <see cref="restoreSpread"/> seconds.</item>
    /// <item>Light LOD: a fixture's Lights are on only within its lodDistance (x <see cref="lodScale"/>) of the viewer and fade
    /// out over <see cref="lodFade"/> metres past it; emission and halo stay, so distant lamps still glow.</item>
    /// <item>Shadow budget: of the hero fixtures within <see cref="shadowRange"/> of the viewer, the <see cref="maxShadowed"/>
    /// nearest lit ones cast realtime shadows (with hysteresis, re-evaluated every <see cref="shadowInterval"/> s).</item>
    /// </list>
    /// The first frame applies the start state instantly (no starter blinks at scene load); later changes animate.
    /// </summary>
    [AddComponentMenu("Sky Island/Light Manager")]
    [DefaultExecutionOrder(50)]
    public class SkyIslandLightManager : MonoBehaviour
    {
        [Serializable]
        public class Room
        {
            public string name = "";
            [Tooltip("Rendering layer bit of the room's static geometry (informative; set by the builder)")] public int layer;
            [Tooltip("Interactables whose name starts with this toggle the room's switchable lamps")] public string switchPrefix = "";
            [Tooltip("Lamps on at start when the room has no switch")] public bool startOn = true;
        }

        [Tooltip("Root to search for fixtures and switches (defaults to this object)")] public Transform root;
        [Tooltip("Powers the Grid lamps (none = always powered)")] public SkyIslandPowerGrid grid;
        public List<Room> rooms = new List<Room>();
        [Tooltip("Light LOD and the shadow budget measure from here (default: the main camera)")] public Transform viewer;

        [Header("Light LOD")]
        [Tooltip("Lights fade out over this many metres past their fixture's LOD distance")] public float lodFade = 6f;
        [Tooltip("Multiplies every fixture's LOD distance (quality setting)")] public float lodScale = 1f;

        [Header("Shadow budget")]
        [Tooltip("Number of hero fixtures (the nearest lit ones) that cast realtime shadows")] public int maxShadowed = 3;
        [Tooltip("Only hero fixtures within this distance of the viewer get shadows (m)")] public float shadowRange = 14f;
        [Tooltip("A shadowed fixture keeps its shadow until another one is this much closer (m)")] public float shadowHysteresis = 2f;
        public LightShadows shadowType = LightShadows.Soft;
        [Tooltip("Point lights need 6 shadow maps each: off = only spot lights get shadows")] public bool pointShadows;
        [Tooltip("Seconds between shadow budget updates")] public float shadowInterval = 0.2f;

        [Header("Power")]
        [Tooltip("Grid voltage, 1 = nominal: lower values dim and warm filament lamps, make tubes flicker (out under 0.65) and put gas lamps out (under 0.85)")]
        [Range(0f, 1.2f)] public float gridVoltage = 1f;
        [Tooltip("Battery time of exit signs and emergency lights while the grid is down, seconds (0 = endless)")] public float batteryLife;
        [Tooltip("When the grid comes back, lamps start spread over this many seconds")] public float restoreSpread = 0.8f;

        /// <summary>Accessibility option ("no flicker"): steady lamps, no starter blinks, faults or beacon-like flicker of
        /// flames (WCAG 2.3.1). Read when a lamp starts or animates.</summary>
        public static bool noFlicker;

        /// <summary>Grid lamps have power.</summary>
        public bool GridPowered { get { return _powered; } }
        /// <summary>Charge of the exit sign / emergency light batteries, 0..1.</summary>
        public float Battery { get { return _battery; } }
        public IList<SkyIslandLightFixture> Fixtures { get { return _fixtures; } }
        /// <summary>Room name, on.</summary>
        public event Action<string, bool> onRoomChanged;
        /// <summary>Every fixture event (sound hooks: Blink = starter click, Fault = buzz, Starting = ballast strike...).</summary>
        public event Action<SkyIslandLightFixture, SkyIslandLightFixture.Event> onFixtureEvent;

        readonly List<SkyIslandLightFixture> _fixtures = new List<SkyIslandLightFixture>();
        readonly List<int> _fixtureRoom = new List<int>();
        readonly Dictionary<string, int> _roomIndex = new Dictionary<string, int>();
        bool[] _roomOn = new bool[0];
        readonly List<KeyValuePair<SkyIslandInteractable, Action<SkyIslandInteractable, bool>>> _subs =
            new List<KeyValuePair<SkyIslandInteractable, Action<SkyIslandInteractable, bool>>>();
        readonly List<SkyIslandLightFixture> _candidates = new List<SkyIslandLightFixture>();
        readonly List<float> _scores = new List<float>();
        readonly List<bool> _chosen = new List<bool>();
        bool _collected, _started, _dirty, _powered = true, _restoring;
        float _battery = 1f, _batteryStart, _lastVoltage = -1f, _nextShadow;
        uint _rng = 0x9E3779B9u;

        // ------------------------------------------------------------------ setup
        void Awake() { Collect(); }

        /// <summary>Finds fixtures and switches under the root (also called by Rescan after spawning lamps).</summary>
        void Collect()
        {
            if (_collected) return;
            _collected = true;
            var r0 = root != null ? root : transform;
            if (grid == null) grid = r0.GetComponentInChildren<SkyIslandPowerGrid>(true);
            _roomIndex.Clear();
            for (int i = 0; i < rooms.Count; i++)
                if (rooms[i] != null && !string.IsNullOrEmpty(rooms[i].name) && !_roomIndex.ContainsKey(rooms[i].name)) _roomIndex[rooms[i].name] = i;
            _roomOn = new bool[rooms.Count];
            var interactables = r0.GetComponentsInChildren<SkyIslandInteractable>(true);
            for (int i = 0; i < rooms.Count; i++)
            {
                var room = rooms[i];
                if (room == null) continue;
                bool any = false, anyOpen = false;
                if (!string.IsNullOrEmpty(room.switchPrefix))
                    foreach (var ia in interactables)
                    {
                        if (!ia.name.StartsWith(room.switchPrefix, StringComparison.Ordinal)) continue;
                        any = true;
                        anyOpen |= ia.IsOpen;
                        int index = i;
                        Action<SkyIslandInteractable, bool> h = (s, open) => FlipRoom(index);
                        ia.onToggled += h;
                        _subs.Add(new KeyValuePair<SkyIslandInteractable, Action<SkyIslandInteractable, bool>>(ia, h));
                    }
                _roomOn[i] = any ? anyOpen : room.startOn;
            }
            _fixtures.Clear();
            _fixtureRoom.Clear();
            foreach (var f in r0.GetComponentsInChildren<SkyIslandLightFixture>(true))
            {
                f.Manager = this;
                _fixtures.Add(f);
                int ri;
                _fixtureRoom.Add(!string.IsNullOrEmpty(f.room) && _roomIndex.TryGetValue(f.room, out ri) ? ri : -1);
            }
            _dirty = true;
        }

        /// <summary>Collects fixtures and switches again (after adding lamps at runtime); their current state is kept.</summary>
        public void Rescan()
        {
            Unsubscribe();
            var keep = (bool[])_roomOn.Clone();
            _collected = false;
            Collect();
            for (int i = 0; i < keep.Length && i < _roomOn.Length; i++) _roomOn[i] = keep[i];
            _dirty = true;
        }

        void OnDestroy() { Unsubscribe(); }

        void Unsubscribe()
        {
            foreach (var kv in _subs)
                if (kv.Key != null) kv.Key.onToggled -= kv.Value;
            _subs.Clear();
        }

        // ------------------------------------------------------------------ API
        public bool IsRoomOn(string room)
        {
            int i;
            return room != null && _roomIndex.TryGetValue(room, out i) && _roomOn[i];
        }

        /// <summary>Switches a room's lamps (the wall switch is not moved).</summary>
        public void SetRoom(string room, bool on)
        {
            int i;
            if (room == null || !_roomIndex.TryGetValue(room, out i) || _roomOn[i] == on) return;
            _roomOn[i] = on;
            _dirty = true;
            if (onRoomChanged != null) onRoomChanged(room, on);
        }

        public void ToggleRoom(string room) { SetRoom(room, !IsRoomOn(room)); }

        /// <summary>Grid on / off: through the power grid component if there is one.</summary>
        public void SetGrid(bool on)
        {
            if (grid != null) grid.SetPower(on);
            else if (_powered != on) { _powered = on; OnPowerChanged(); }
        }

        /// <summary>Breaks the lamps within radius of a point (a creature passing, an explosion); returns how many.</summary>
        public int BreakFixtures(Vector3 center, float radius)
        {
            int n = 0;
            float r2 = radius * radius;
            foreach (var f in _fixtures)
                if (f != null && !f.Broken && (f.transform.position - center).sqrMagnitude <= r2) { f.Break(); n++; }
            return n;
        }

        internal void RaiseFixtureEvent(SkyIslandLightFixture f, SkyIslandLightFixture.Event e)
        {
            if (onFixtureEvent != null) onFixtureEvent(f, e);
        }

        void FlipRoom(int index)
        {
            if (index < 0 || index >= _roomOn.Length) return;
            _roomOn[index] = !_roomOn[index];
            _dirty = true;
            if (onRoomChanged != null) onRoomChanged(rooms[index].name, _roomOn[index]);
        }

        void OnPowerChanged()
        {
            if (!_powered) _batteryStart = Time.time;
            _restoring = _powered;
            _dirty = true;
        }

        // ------------------------------------------------------------------ update
        void Update()
        {
            Collect();
            float now = Time.time;
            if (grid != null && grid.Powered != _powered)
            {
                _powered = grid.Powered;
                if (_started) OnPowerChanged();
            }
            float battery = 1f;
            if (!_powered && batteryLife > 0f) battery = Mathf.Clamp01(1f - (now - _batteryStart) / batteryLife);
            if (Mathf.Abs(battery - _battery) > 0.005f || (battery <= 0f && _battery > 0f)) { _battery = battery; _dirty = true; }
            if (Mathf.Abs(gridVoltage - _lastVoltage) > 0.001f) { _lastVoltage = gridVoltage; _dirty = true; }
            if (_dirty || !_started)
            {
                Supply(!_started);
                _started = true;
                _dirty = false;
                _restoring = false;
            }
            Vector3 eye = ViewerPosition();
            UpdateLod(eye);
            if (now >= _nextShadow)
            {
                _nextShadow = now + Mathf.Max(0.02f, shadowInterval);
                UpdateShadows(eye);
            }
        }

        Vector3 ViewerPosition()
        {
            if (viewer != null) return viewer.position;
            var cam = Camera.main;
            return cam != null ? cam.transform.position : transform.position;
        }

        void Supply(bool instant)
        {
            for (int i = 0; i < _fixtures.Count; i++)
            {
                var f = _fixtures[i];
                if (f == null) continue;
                bool on;
                float voltage = 1f, battery = 1f, delay = 0f;
                switch (f.circuit)
                {
                    case SkyIslandLightFixture.Circuit.Battery:
                        battery = _battery;
                        on = _battery > 0f && (f.kind != SkyIslandLightFixture.Kind.Emergency || !_powered);
                        break;
                    case SkyIslandLightFixture.Circuit.None:
                        on = true;
                        break;
                    default:
                        int ri = _fixtureRoom[i];
                        on = _powered && (!f.switchable || ri < 0 || _roomOn[ri]);
                        voltage = gridVoltage;
                        if (on && !f.Supplied) delay = (_restoring ? restoreSpread : 0.12f) * Rand();
                        break;
                }
                f.SetSupply(on, voltage, battery, instant, delay);
            }
        }

        void UpdateLod(Vector3 eye)
        {
            float band = Mathf.Max(0.01f, lodFade);
            for (int i = 0; i < _fixtures.Count; i++)
            {
                var f = _fixtures[i];
                if (f == null || !f.gameObject.activeInHierarchy) continue;
                float lod = f.lodDistance * lodScale;
                if (lod <= 0f) { f.SetLodFade(1f); continue; }
                float d2 = (f.transform.position - eye).sqrMagnitude;
                float far = lod + band;
                if (d2 >= far * far) { f.SetLodFade(0f); continue; }
                if (d2 <= lod * lod) { f.SetLodFade(1f); continue; }
                f.SetLodFade(1f - (Mathf.Sqrt(d2) - lod) / band);
            }
        }

        void UpdateShadows(Vector3 eye)
        {
            _candidates.Clear();
            _scores.Clear();
            float r2 = shadowRange * shadowRange;
            for (int i = 0; i < _fixtures.Count; i++)
            {
                var f = _fixtures[i];
                if (f == null || !f.hero) continue;
                float d2 = (f.transform.position - eye).sqrMagnitude;
                bool ok = maxShadowed > 0 && f.gameObject.activeInHierarchy && f.IsLit && f.LodFade > 0.5f && d2 <= r2;
                if (!ok) { if (f.Shadowed) f.SetShadows(false, shadowType, pointShadows); continue; }
                _candidates.Add(f);
                _scores.Add(Mathf.Sqrt(d2) - (f.Shadowed ? shadowHysteresis : 0f));
            }
            // pick the maxShadowed lowest scores (small lists: selection by repeated minimum)
            int picks = Mathf.Min(maxShadowed, _candidates.Count);
            _chosen.Clear();
            for (int i = 0; i < _candidates.Count; i++) _chosen.Add(false);
            for (int p = 0; p < picks; p++)
            {
                int best = -1;
                for (int i = 0; i < _candidates.Count; i++)
                    if (!_chosen[i] && (best < 0 || _scores[i] < _scores[best])) best = i;
                _chosen[best] = true;
            }
            for (int i = 0; i < _candidates.Count; i++)
                if (_candidates[i].Shadowed != _chosen[i]) _candidates[i].SetShadows(_chosen[i], shadowType, pointShadows);
        }

        float Rand()
        {
            _rng ^= _rng << 13;
            _rng ^= _rng >> 17;
            _rng ^= _rng << 5;
            return (_rng & 0xFFFFFF) / 16777216f;
        }
    }
}
