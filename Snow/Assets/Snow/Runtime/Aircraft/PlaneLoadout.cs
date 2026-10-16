using System;
using System.Collections.Generic;
using Snow.Net;
using Unity.Netcode;
using UnityEngine;

namespace Snow.Aircraft
{
    /// <summary>
    /// The aircraft "in one piece" (user, 16.10): everything that comes with the plane is listed on the plane prefab and
    /// is spawned by the server once, when the plane spawns, as separate network bodies (§12: cargo and gear are never
    /// children of the vehicle). A level needs only the plane prefab and the network manager.
    ///  - Bay: an item on the cargo floor at a plane-space offset (cargo_layout.json); stackOn puts it on the real top of
    ///    an item spawned earlier in the list (audit N5: the layout's stacks overlap by 4.7 cm).
    ///  - Ground: on the ground at a socket of the plane plus a plane-space offset, upright, heading = plane heading + yaw;
    ///    the height from a ray down that ignores the plane itself.
    ///  - HostPocket: the key goes straight into the host player's pocket (TEMPORARY start of a level, §8.3).
    /// Late joiners get the items as ordinary network objects; nothing is spawned twice.
    /// </summary>
    public class PlaneLoadout : NetworkBehaviour
    {
        public enum Place : byte
        {
            Bay,
            Ground,
            /// <summary>The key (PlaneKey) in the host's pocket at the start (И2 TEMPORARY, §8.3).</summary>
            HostPocket,
        }

        [Serializable]
        public struct Entry
        {
            public GameObject prefab;
            public Place place;
            [Tooltip("Bay: plane space. Ground: plane space, from the socket")] public Vector3 offset;
            [Tooltip("deg about up, added to the plane's heading")] public float yaw;
            [Tooltip("Ground: socket of the plane the offset starts from")] public string socket;
            [Tooltip("Bay: name of an item spawned earlier in this list to stand on")] public string stackOn;
        }

        public Entry[] items = Array.Empty<Entry>();
        [Tooltip("What the Ground rays may land on")] public LayerMask groundMask = ~0;

        bool _pending, _done;
        readonly Dictionary<string, float> _tops = new Dictionary<string, float>();

        /// <summary>Server: the items this loadout spawned, by prefab name (later iterations look items up here).</summary>
        public readonly Dictionary<string, NetworkObject> Spawned = new Dictionary<string, NetworkObject>();

        public override void OnNetworkSpawn()
        {
            if (IsServer && !_done) _pending = true;               // next frame: in-scene objects finish spawning first
        }

        void Update()
        {
            if (!_pending || !IsServer || !IsSpawned) return;
            _pending = false;
            _done = true;
            foreach (var e in items)
                if (e.prefab != null) SpawnOne(e);
        }

        void SpawnOne(Entry e)
        {
            Vector3 pos;
            Quaternion rot;
            Quaternion heading = Quaternion.Euler(0f, transform.eulerAngles.y + e.yaw, 0f);
            if (e.place == Place.Bay)
            {
                pos = transform.TransformPoint(e.offset);
                rot = transform.rotation * Quaternion.Euler(0f, e.yaw, 0f);
                if (!string.IsNullOrEmpty(e.stackOn) && _tops.TryGetValue(e.stackOn, out float top)) pos.y = top + 0.005f;
            }
            else if (e.place == Place.HostPocket)
            {
                pos = transform.position + Vector3.up * 1.5f;
                rot = heading;
            }
            else
            {
                Transform socket = string.IsNullOrEmpty(e.socket) ? transform : Find(e.socket);
                pos = socket.position + Quaternion.Euler(0f, transform.eulerAngles.y, 0f) * e.offset;
                pos = OnGround(pos);
                rot = heading;
            }
            GameObject go = Instantiate(e.prefab, pos, rot);
            go.name = e.prefab.name;
            var no = go.GetComponent<NetworkObject>();
            no.Spawn();
            Spawned[go.name] = no;
            if (e.place == Place.HostPocket && go.TryGetComponent(out PlaneKey key)) key.ServerPocket(NetworkManager.ServerClientId, out _);
            _tops[go.name] = pos.y + Height(e.prefab);
            BenchLog.Event("loadout_spawn", "item", go.name, "place", e.place.ToString(), "p", pos);
        }

        Transform Find(string name)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            Debug.LogWarning($"[Snow] PlaneLoadout: socket {name} not found on {this.name}");
            return transform;
        }

        // the ground under a point (the plane's own colliders and triggers ignored); the point itself if none
        Vector3 OnGround(Vector3 p)
        {
            var hits = Physics.RaycastAll(p + Vector3.up * 1f, Vector3.down, 6f, groundMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Vector3 ground = p;
            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(transform) || h.distance >= best) continue;
                best = h.distance;
                ground = h.point;
            }
            return ground;
        }

        // pivot at the bottom centre: the height is the top of the collider bounds
        static float Height(GameObject prefab)
        {
            var mc = prefab.GetComponentInChildren<MeshCollider>();
            if (mc != null && mc.sharedMesh != null) return mc.sharedMesh.bounds.max.y * mc.transform.lossyScale.y;
            var bc = prefab.GetComponentInChildren<BoxCollider>();
            return bc != null ? (bc.center.y + bc.size.y * 0.5f) * bc.transform.lossyScale.y : 0.5f;
        }
    }
}
