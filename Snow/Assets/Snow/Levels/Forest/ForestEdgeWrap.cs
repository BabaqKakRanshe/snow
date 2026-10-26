using System.Collections.Generic;
using Snow.Cargo;
using Snow.Net;
using Snow.Player;
using Snow.Seats;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace Snow.Levels.Forest
{
    /// <summary>
    /// «Лес не отпускает» (user 26.10, RESEARCH_MAP_EDGE.md, idea 3; TEMPORARY until the edge model): the Forest is closed
    /// on itself. Whoever crosses the wrap line (the terrain's edge minus <see cref="margin"/>) comes out on the opposite
    /// side, moving the same way, at the same height above the ground. The fog thickens towards the line so that it always
    /// ends before the end of the terrain: the void past the map is never seen, and the jump happens inside a white
    /// (by night black) wall that looks the same on both sides.
    ///  - On foot: the owner moves his own character (motor + NetworkTransform, as <see cref="LocationFallReset"/>).
    ///    Items in the hand or a pocket follow him by themselves; carried crates catch up (CargoItem's jump snap).
    ///  - Vehicles (van, plane): the server moves the body with its velocity, the cargo inside it, and the seated and
    ///    standing crew (they are the vehicle's children).
    /// The map is the union of the active terrains, read once the terrains are up: nothing is configured per tile.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public class ForestEdgeWrap : MonoBehaviour
    {
        [Tooltip("The wrap line lies this far inside the terrain's edge (m)")] public float margin = 10f;
        [Tooltip("Arrival this far inside the opposite wrap line (m)")] public float arrive = 3f;
        [Tooltip("Fog end at the wrap line itself (m)")] public float minFog = 6f;
        [Tooltip("Off: no wrap, no edge fog")] public bool wrapOn = true;

        public static ForestEdgeWrap Instance { get; private set; }

        Rect _map;                          // x/z of the union of the terrains
        bool _hasMap;
        float _nextScan, _playerCool;
        readonly List<SeatedVehicle> _vehicles = new List<SeatedVehicle>();
        readonly Dictionary<SeatedVehicle, float> _vehicleCool = new Dictionary<SeatedVehicle, float>();
        readonly Collider[] _hits = new Collider[16];

        // edge fog and sky: written in LateUpdate (last: after the day/night, the fog controller and the beams), undone in
        // the next frame's Update, so the LateUpdates that read or write fog and sky see the scene's own values
        bool _fogSet, _skySet;
        float _baseStart, _baseEnd, _baseDensity, _wroteStart, _wroteEnd, _wroteDensity;
        Material _skyMat;
        Color _baseHorizon, _baseBand, _baseBelow, _wroteHorizon;
        static readonly int Horizon = Shader.PropertyToID("_HorizonColor"), Band = Shader.PropertyToID("_BandColor"),
                            Below = Shader.PropertyToID("_BelowColor");

        public bool HasMap => _hasMap;
        public Rect Map => _map;

        void OnEnable() => Instance = this;

        void OnDisable()
        {
            UndoLook();
            if (Instance == this) Instance = null;
        }

        bool ReadMap()
        {
            if (_hasMap) return true;
            Terrain[] all = Terrain.activeTerrains;
            if (all == null || all.Length == 0) return false;
            float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
            foreach (Terrain t in all)
            {
                if (t == null || t.terrainData == null) continue;
                Vector3 o = t.GetPosition(), s = t.terrainData.size;
                x0 = Mathf.Min(x0, o.x); z0 = Mathf.Min(z0, o.z);
                x1 = Mathf.Max(x1, o.x + s.x); z1 = Mathf.Max(z1, o.z + s.z);
            }
            if (x1 - x0 < 4f * (margin + arrive) || z1 - z0 < 4f * (margin + arrive)) return false;
            _map = Rect.MinMaxRect(x0, z0, x1, z1);
            _hasMap = true;
            BenchLog.Event("edge_wrap_map", "min", new Vector2(x0, z0), "max", new Vector2(x1, z1), "margin", margin);
            return true;
        }

        /// <summary>Horizontal distance from p to the nearest wrap line, 0 on or past it.</summary>
        public float LineDistance(Vector3 p)
        {
            float dx = Mathf.Min(p.x - (_map.xMin + margin), (_map.xMax - margin) - p.x);
            float dz = Mathf.Min(p.z - (_map.yMin + margin), (_map.yMax - margin) - p.z);
            return Mathf.Max(0f, Mathf.Min(dx, dz));
        }

        /// <summary>The horizontal jump for something at p, zero while it is inside the wrap lines.</summary>
        Vector3 Jump(Vector3 p)
        {
            float wx = _map.width - 2f * margin - arrive, wz = _map.height - 2f * margin - arrive;
            float dx = p.x > _map.xMax - margin ? -wx : p.x < _map.xMin + margin ? wx : 0f;
            float dz = p.z > _map.yMax - margin ? -wz : p.z < _map.yMin + margin ? wz : 0f;
            return new Vector3(dx, 0f, dz);
        }

        /// <summary>Terrain height under (x, z); NaN off the terrains.</summary>
        public static float GroundY(float x, float z)
        {
            foreach (Terrain t in Terrain.activeTerrains)
            {
                if (t == null || t.terrainData == null) continue;
                Vector3 o = t.GetPosition(), s = t.terrainData.size;
                if (x < o.x || z < o.z || x > o.x + s.x || z > o.z + s.z) continue;
                return o.y + t.SampleHeight(new Vector3(x, 0f, z));
            }
            return float.NaN;
        }

        // the jump plus the ground's rise between the two places (the same height above the ground on arrival)
        bool FullJump(Vector3 p, out Vector3 delta)
        {
            delta = Jump(p);
            if (delta == Vector3.zero) return false;
            Vector3 to = p + delta;
            float g0 = GroundY(p.x, p.z), g1 = GroundY(to.x, to.z);
            if (!float.IsNaN(g0) && !float.IsNaN(g1)) delta.y = g1 - g0;
            return true;
        }

        // ---------------------------------------------------------------- on foot (the owner)
        void Update()
        {
            UndoLook();
            if (!wrapOn || !ReadMap()) return;
            PlayerNet p = PlayerNet.Local;
            if (p == null || !p.IsSpawned || p.Seated || (p.seatController != null && p.seatController.Seat != null)) return;
            if (p.transform.parent != null || Time.time < _playerCool) return;   // riding a vehicle: the vehicle wraps
            Vector3 at = p.transform.position;
            if (!FullJump(at, out Vector3 delta)) return;

            CharacterController cc = p.motor != null ? p.motor.Controller : null;
            Vector3 to = Free(at + delta, delta, cc, p.transform);
            float yaw = p.view != null ? p.view.Yaw : p.transform.eulerAngles.y;
            p.motor.Teleport(to, yaw, p.motor.Velocity);
            if (p.TryGetComponent(out NetworkTransform nt) && nt.CanCommitToTransform)
                nt.Teleport(to, Quaternion.Euler(0f, yaw, 0f), p.transform.localScale);
            _playerCool = Time.time + 0.5f;
            BenchLog.Event("edge_wrap", "who", "player", "from", at, "to", to);
        }

        // a place for the capsule near the wanted one: the wanted spot, else sideways along the wrap line (a trunk, a rock)
        Vector3 Free(Vector3 want, Vector3 delta, CharacterController cc, Transform self)
        {
            if (cc == null) return want;
            Vector3 side = Mathf.Abs(delta.x) > 0f && Mathf.Abs(delta.z) > 0f ? Vector3.zero
                : Mathf.Abs(delta.x) > 0f ? Vector3.forward : Vector3.right;
            float r = cc.radius, h = Mathf.Max(cc.height, 2f * r);
            for (int i = 0; i < 9; i++)
            {
                float s = (i + 1) / 2 * 1.5f * (i % 2 == 0 ? 1f : -1f);
                Vector3 feet = want + side * s;
                float g = GroundY(feet.x, feet.z);
                if (!float.IsNaN(g)) feet.y = Mathf.Max(feet.y, g + 0.05f);
                Vector3 a = feet + Vector3.up * (r + 0.3f), b = feet + Vector3.up * (h - r);
                int n = Physics.OverlapCapsuleNonAlloc(a, b, r, _hits, ~0, QueryTriggerInteraction.Ignore);
                bool blocked = false;
                for (int k = 0; k < n && !blocked; k++) blocked = !_hits[k].transform.IsChildOf(self);
                if (!blocked) return feet;
                if (side == Vector3.zero) break;
            }
            return want;
        }

        // ---------------------------------------------------------------- vehicles (the server)
        void FixedUpdate()
        {
            if (!wrapOn || !ReadMap()) return;
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;
            if (Time.time >= _nextScan)
            {
                _nextScan = Time.time + 1f;                             // vans and planes spawn and despawn rarely
                _vehicles.Clear();
                _vehicles.AddRange(FindObjectsByType<SeatedVehicle>(FindObjectsSortMode.None));
            }
            foreach (SeatedVehicle v in _vehicles)
            {
                if (v == null || !v.IsSpawned || !v.TryGetComponent(out Rigidbody rb)) continue;
                if (_vehicleCool.TryGetValue(v, out float cool) && Time.time < cool) continue;
                if (!FullJump(rb.position, out Vector3 delta)) continue;
                ShiftVehicle(v, rb, delta);
                _vehicleCool[v] = Time.time + 0.5f;
            }
        }

        void ShiftVehicle(SeatedVehicle v, Rigidbody rb, Vector3 delta)
        {
            Bounds box = VehicleBounds(v);
            Vector3 from = rb.position, lv = rb.linearVelocity, av = rb.angularVelocity;
            delta += Clear(v, box, delta);
            Vector3 at = from + delta;
            rb.position = at;
            v.transform.position = at;
            if (v.TryGetComponent(out NetworkTransform nt) && nt.CanCommitToTransform)
                nt.Teleport(at, v.transform.rotation, v.transform.localScale);
            rb.linearVelocity = lv;
            rb.angularVelocity = av;
            int moved = 0;
            box.Expand(0.5f);
            foreach (CargoItem item in CargoItem.All)
            {
                // on board: lying, tied down, stowed or carried there; in a hand or a pocket it follows its player
                if (item == null || !item.IsSpawned || item.Carry != CarryMode.None || !box.Contains(item.transform.position)) continue;
                item.ServerShift(delta);
                moved++;
            }
            BenchLog.Event("edge_wrap", "who", v.name, "from", from, "to", at, "kmh", lv.magnitude * 3.6f, "cargo", moved);
        }

        static Bounds VehicleBounds(SeatedVehicle v)
        {
            Bounds b = new Bounds(v.transform.position, Vector3.one);
            bool any = false;
            foreach (Collider c in v.GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger || !c.enabled) continue;
                if (!any) { b = c.bounds; any = true; }
                else b.Encapsulate(c.bounds);
            }
            return b;
        }

        // a sideways nudge (along the wrap line) so the body does not come out inside a trunk; zero if the spot is free
        Vector3 Clear(SeatedVehicle v, Bounds box, Vector3 delta)
        {
            Vector3 side = Mathf.Abs(delta.x) > 0f && Mathf.Abs(delta.z) > 0f ? Vector3.zero
                : Mathf.Abs(delta.x) > 0f ? Vector3.forward : Vector3.right;
            Vector3 half = box.extents;
            half.y = Mathf.Max(0.1f, half.y - 0.25f);                   // lifted off the ground: the wheels may touch it
            for (int i = 0; i < 11; i++)
            {
                float s = (i + 1) / 2 * 2f * (i % 2 == 0 ? 1f : -1f);
                Vector3 c = box.center + delta + side * s + Vector3.up * 0.3f;
                int n = Physics.OverlapBoxNonAlloc(c, half, _hits, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
                bool blocked = false;
                for (int k = 0; k < n && !blocked; k++)
                    blocked = !_hits[k].transform.IsChildOf(v.transform) && _hits[k].attachedRigidbody == null;
                if (!blocked) return side * s;
                if (side == Vector3.zero) break;
            }
            return Vector3.zero;
        }

        // ---------------------------------------------------------------- edge fog (every peer, its own camera)
        void LateUpdate()
        {
            if (!wrapOn || !ReadMap()) return;
            Camera cam = Camera.main;
            if (cam == null || !RenderSettings.fog) return;
            float d = LineDistance(cam.transform.position);
            bool linear = RenderSettings.fogMode == FogMode.Linear;
            float density = RenderSettings.fogDensity;
            // how far one sees now (linear: the fog's end; exponential: ~95 % fog)
            float seen = linear ? RenderSettings.fogEndDistance
                : RenderSettings.fogMode == FogMode.Exponential ? 3f / Mathf.Max(1e-5f, density) : 1.7f / Mathf.Max(1e-5f, density);
            // the void begins at the terrain's edge, margin past the line: the fog must end before it
            float limit = Mathf.Max(minFog, d + margin * 0.5f);
            if (seen <= limit) return;
            float k = limit / seen;

            _baseStart = RenderSettings.fogStartDistance;
            _baseEnd = RenderSettings.fogEndDistance;
            _baseDensity = density;
            if (linear)
            {
                RenderSettings.fogStartDistance = _wroteStart = _baseStart * k;
                RenderSettings.fogEndDistance = _wroteEnd = _baseEnd * k;
            }
            else
            {
                _wroteStart = _baseStart;
                _wroteEnd = _baseEnd;
                RenderSettings.fogDensity = _wroteDensity = density / k;
            }
            _fogSet = true;

            // looking straight at the void one sees the sky's horizon and below-horizon colours: they go to the fog's
            ForestDayNight dn = ForestDayNight.Instance;
            _skyMat = dn != null ? dn.SkyInstance : null;                // only the day/night's own copy, never an asset
            if (_skyMat == null || !_skyMat.HasProperty(Horizon)) return;
            float e = 1f - k;
            Color fog = RenderSettings.fogColor;
            _baseHorizon = _skyMat.GetColor(Horizon);
            _baseBand = _skyMat.HasProperty(Band) ? _skyMat.GetColor(Band) : _baseHorizon;
            _baseBelow = _skyMat.HasProperty(Below) ? _skyMat.GetColor(Below) : _baseHorizon;
            _skyMat.SetColor(Horizon, _wroteHorizon = Color.Lerp(_baseHorizon, fog, e));
            if (_skyMat.HasProperty(Band)) _skyMat.SetColor(Band, Color.Lerp(_baseBand, fog, e));
            if (_skyMat.HasProperty(Below)) _skyMat.SetColor(Below, Color.Lerp(_baseBelow, fog, e));
            _skySet = true;
        }

        // back to the scene's own values, unless someone has already written new ones this frame
        void UndoLook()
        {
            if (_fogSet)
            {
                if (RenderSettings.fogEndDistance == _wroteEnd && RenderSettings.fogStartDistance == _wroteStart)
                {
                    RenderSettings.fogStartDistance = _baseStart;
                    RenderSettings.fogEndDistance = _baseEnd;
                }
                if (RenderSettings.fogMode != FogMode.Linear && RenderSettings.fogDensity == _wroteDensity) RenderSettings.fogDensity = _baseDensity;
                _fogSet = false;
            }
            if (_skySet)
            {
                if (_skyMat != null && _skyMat.GetColor(Horizon) == _wroteHorizon)
                {
                    _skyMat.SetColor(Horizon, _baseHorizon);
                    if (_skyMat.HasProperty(Band)) _skyMat.SetColor(Band, _baseBand);
                    if (_skyMat.HasProperty(Below)) _skyMat.SetColor(Below, _baseBelow);
                }
                _skySet = false;
            }
        }
    }
}
