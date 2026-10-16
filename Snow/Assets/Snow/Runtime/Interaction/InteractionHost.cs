using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UtilityPlane;

namespace Snow.Interaction
{
    public enum UsePhase : byte
    {
        Press,
        Release,
        /// <summary>A knob turned back one step (mouse wheel the other way, И2 F). Hosts without knobs ignore it.</summary>
        Back,
    }

    /// <summary>
    /// A networked object with interaction volumes (UtilityPlaneInteractable, layer Interact): the P01 bench cabin now,
    /// the plane later. Clients only ask; the server validates reach and rules and changes state (NET-01, NET-02).
    /// Describe() is read on clients from replicated state for the hover hint (INPUT-02).
    /// </summary>
    public abstract class InteractionHost : NetworkBehaviour
    {
        /// <summary>m, TEMPORARY: ray length on the client (§7.4) and the server's reach check with a latency margin.</summary>
        public const float UseRange = 2.0f;
        public const float ServerReachMargin = 0.6f;

        [Header("Interior (local space): zones named *_In are used from inside, *_Out from outside")]
        public Vector3 interiorCenter;
        public Vector3 interiorSize;

        readonly Dictionary<string, UtilityPlaneInteractable> _volumes = new Dictionary<string, UtilityPlaneInteractable>();
        Dictionary<Collider, List<UtilityPlaneInteractable>> _embedded;

        protected virtual void Awake()
        {
            foreach (var v in GetComponentsInChildren<UtilityPlaneInteractable>(true))
                if (!string.IsNullOrEmpty(v.id)) _volumes[v.id] = v;
        }

        public bool TryGetVolume(string id, out UtilityPlaneInteractable volume) => _volumes.TryGetValue(id, out volume);
        public IEnumerable<UtilityPlaneInteractable> Volumes => _volumes.Values;

        /// <summary>Server: can this player reach the volume from where the server sees him?</summary>
        public bool InReach(string id, Vector3 usePoint)
        {
            if (!TryGetVolume(id, out var v)) return false;
            var col = v.GetComponent<Collider>();
            Vector3 closest = col != null ? col.ClosestPoint(usePoint) : v.transform.position;
            return Vector3.Distance(usePoint, closest) <= UseRange + ServerReachMargin;
        }

        /// <summary>Is a world point (an eye) inside this vehicle's cabin or bay?</summary>
        public bool IsInside(Vector3 worldPoint)
        {
            if (interiorSize.x <= 0f || interiorSize.y <= 0f || interiorSize.z <= 0f) return false;
            return new Bounds(interiorCenter, interiorSize).Contains(transform.InverseTransformPoint(worldPoint));
        }

        /// <summary>
        /// Side rule of the model's zone names: a handle *_In is used from inside, *_Out from outside (§8.2, §8.3: later the
        /// lock refuses only the outside). Checked by the client's ray and again by the server (NET-01).
        /// </summary>
        public bool SideAllows(string id, Vector3 eye)
        {
            if (id.EndsWith("_In", System.StringComparison.Ordinal)) return IsInside(eye);
            if (id.EndsWith("_Out", System.StringComparison.Ordinal)) return !IsInside(eye);
            return true;
        }

        /// <summary>
        /// Is the zone there for a ray now? A closed zone is transparent: the ray goes on to what is behind it (v12
        /// integration И1.4: the stowage boxes of the bay while the player holds nothing that fits, the seats of folded
        /// troop seats). <paramref name="held"/> is the item the player carries, or null. Client ray and server both ask.
        /// </summary>
        public virtual bool ZoneOpen(UtilityPlaneInteractable zone, Snow.Cargo.CargoItem held) => true;

        /// <summary>
        /// Zones whose centre lies inside one of this host's solid colliders, or whose box cuts into it (in the model: seat
        /// zones inside the seat cushions, pulls and rheostats inside the panel, inner door handles inside the door, the
        /// flap lever behind the pedestal hull). A ray that meets such a
        /// collider and goes on through the zone's box within <paramref name="depth"/> reaches the zone. Built once:
        /// zone and collider move together (both are children of the same part).
        /// </summary>
        public bool EmbeddedOnRay(Collider solid, Ray ray, float solidDistance, float depth, System.Func<UtilityPlaneInteractable, bool> allowed,
                                  out UtilityPlaneInteractable zone)
        {
            zone = null;
            if (_embedded == null) BuildEmbedded();
            if (!_embedded.TryGetValue(solid, out var list)) return false;
            float best = float.MaxValue;
            foreach (var z in list)
            {
                if (z == null || (allowed != null && !allowed(z))) continue;
                if (!ZoneEntry(z, ray, out float d) || d > solidDistance + depth || d >= best) continue;
                best = d;
                zone = z;
            }
            return zone != null;
        }

        void BuildEmbedded()
        {
            _embedded = new Dictionary<Collider, List<UtilityPlaneInteractable>>();
            var solids = new List<Collider>();
            foreach (var c in GetComponentsInChildren<Collider>(true))
                if (!c.isTrigger && (c is BoxCollider || c is SphereCollider || c is CapsuleCollider || (c is MeshCollider m && m.convex)))
                    solids.Add(c);
            foreach (var z in _volumes.Values)
            {
                var zc = z.GetComponent<Collider>();
                if (zc == null) continue;
                Vector3 center = zc.bounds.center;
                foreach (var s in solids)
                {
                    // inside (ClosestPoint = itself), or the zone box cuts into the part: v5+ Lever_Flap sits 8 cm behind
                    // the face of COL_Pedestal, its centre outside the hull (И1 check I-07)
                    bool inside = (s.ClosestPoint(center) - center).sqrMagnitude <= 1e-8f;
                    if (!inside && !Physics.ComputePenetration(zc, zc.transform.position, zc.transform.rotation,
                            s, s.transform.position, s.transform.rotation, out _, out _)) continue;
                    if (!_embedded.TryGetValue(s, out var list)) _embedded[s] = list = new List<UtilityPlaneInteractable>();
                    list.Add(z);
                }
            }
        }

        /// <summary>Where a ray enters a zone's oriented box, world distance.</summary>
        public static bool ZoneEntry(UtilityPlaneInteractable zone, Ray ray, out float distance)
        {
            distance = 0f;
            var box = zone.GetComponent<BoxCollider>();
            if (box == null) return false;
            Transform t = box.transform;
            var local = new Ray(t.InverseTransformPoint(ray.origin), t.InverseTransformVector(ray.direction));
            if (!new Bounds(box.center, box.size).IntersectRay(local, out float d)) return false;
            distance = Vector3.Distance(ray.origin, t.TransformPoint(local.GetPoint(d)));
            return true;
        }

        /// <summary>Server: apply a use command after reach was checked. Reason is shown to the player on refusal.</summary>
        public abstract bool ServerUse(ulong clientId, string id, UsePhase phase, out string reason);

        /// <summary>Client and server: state part of the hover hint, e.g. "закрыта".</summary>
        public virtual string Describe(string id) => null;

        /// <summary>Client and server: the action a use would perform now, e.g. "открыть".</summary>
        public virtual string ActionText(string id) => "использовать";
    }
}
