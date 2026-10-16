using System;
using System.Collections.Generic;
using Snow.Net;
using Unity.Netcode;
using UnityEngine;

namespace Snow.Bench
{
    /// <summary>
    /// Server: spawns the bench cargo as separate network bodies (§12: never children of the vehicle). An item stacked on
    /// another is placed on the lower item's real top from its mesh bounds, so stacks never start interpenetrating
    /// (audit finding N5: cargo_layout.json stacks overlap by 4.7 cm).
    /// </summary>
    public class BenchCargoSpawner : MonoBehaviour
    {
        [Serializable]
        public struct Entry
        {
            public GameObject prefab;
            [Tooltip("Pose reference; offset is in its local space. Null = world")] public Transform anchor;
            public Vector3 offset;
            public float yaw;
            [Tooltip("Name of an item spawned earlier in this list to stand on")] public string stackOn;
        }

        public Entry[] items;
        readonly Dictionary<string, (Vector3 pos, float top)> _spawned = new Dictionary<string, (Vector3, float)>();

        void Start()
        {
            if (NetworkManager.Singleton == null) return;
            NetworkManager.Singleton.OnServerStarted += Spawn;
        }

        void OnDestroy()
        {
            if (NetworkManager.Singleton != null) NetworkManager.Singleton.OnServerStarted -= Spawn;
        }

        void Spawn()
        {
            foreach (var e in items)
            {
                if (e.prefab == null) continue;
                Vector3 pos = e.anchor != null ? e.anchor.TransformPoint(e.offset) : e.offset;
                Quaternion rot = (e.anchor != null ? e.anchor.rotation : Quaternion.identity) * Quaternion.Euler(0f, e.yaw, 0f);
                if (!string.IsNullOrEmpty(e.stackOn) && _spawned.TryGetValue(e.stackOn, out var below))
                    pos.y = below.top + 0.005f;
                var go = Instantiate(e.prefab, pos, rot);
                go.name = e.prefab.name;
                go.GetComponent<NetworkObject>().Spawn();
                _spawned[go.name] = (pos, pos.y + Height(e.prefab));
                BenchLog.Event("cargo_spawn", "item", go.name, "p", pos);
            }
        }

        // pivot at the bottom centre: the height is the top of the collider mesh bounds
        static float Height(GameObject prefab)
        {
            var mc = prefab.GetComponentInChildren<MeshCollider>();
            if (mc != null && mc.sharedMesh != null) return mc.sharedMesh.bounds.max.y * mc.transform.lossyScale.y;
            var bc = prefab.GetComponentInChildren<BoxCollider>();
            return bc != null ? (bc.center.y + bc.size.y * 0.5f) * bc.transform.lossyScale.y : 0.5f;
        }
    }
}
