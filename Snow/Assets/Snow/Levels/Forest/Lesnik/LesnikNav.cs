using System;
using System.Collections;
using System.Collections.Generic;
using Snow.Cargo;
using Snow.Net;
using UnityEngine;
using UnityEngine.AI;

namespace Snow.Levels.Forest
{
    /// <summary>
    /// The Lesnik's walkable ground (server only): the Forest has no baked NavMesh, so the host builds one at start for a
    /// few regions (the base and its yard, the cave) from the physics colliders. Door leaves are left out of the build
    /// and get a carving NavMeshObstacle instead, so an open door is a way in and a closed one is a wall (he never opens a
    /// door: docs/gdd/monsters/02_lesnik). Items, players and vehicles are left out (they move).
    /// </summary>
    public static class LesnikNav
    {
        /// <summary>Agent type of the Lesnik's NavMesh (-1 until built). He stoops: 1.9 m fits under a door.</summary>
        public static int AgentType { get; private set; } = -1;
        public static bool Ready { get; private set; }

        static readonly List<NavMeshDataInstance> Instances = new List<NavMeshDataInstance>();
        static readonly List<NavMeshObstacle> Obstacles = new List<NavMeshObstacle>();

        public static NavMeshQueryFilter Filter => new NavMeshQueryFilter { agentTypeID = AgentType, areaMask = NavMesh.AllAreas };

        public static IEnumerator Build(Bounds[] regions, SkyIslandLocationHost host, Action done)
        {
            Clear();
            NavMeshBuildSettings settings = NavMesh.CreateSettings();
            settings.agentRadius = 0.3f;
            settings.agentHeight = 1.9f;
            settings.agentClimb = 0.45f;
            settings.agentSlope = 40f;
            settings.minRegionArea = 2f;
            settings.overrideVoxelSize = true;
            settings.voxelSize = 0.12f;
            AgentType = settings.agentTypeID;

            var markups = new List<NavMeshBuildMarkup>();
            void Skip(Transform t)
            {
                if (t != null) markups.Add(new NavMeshBuildMarkup { root = t, overrideIgnore = true, ignoreFromBuild = true, applyToChildren = true });
            }
            var doors = new List<Transform>();
            if (host != null && host.parts != null)
                foreach (var p in host.parts)
                    if (p != null && p.part != null && (p.category == SkyIslandLocationHost.Category.Door || p.category == SkyIslandLocationHost.Category.HostDoor))
                    {
                        Skip(p.part.transform);
                        doors.Add(p.part.transform);
                    }
            foreach (var item in CargoItem.All) if (item != null) Skip(item.transform);
            foreach (var rb in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
                if (rb != null && !rb.isKinematic) Skip(rb.transform);      // the van, crates: they move
            foreach (var player in UnityEngine.Object.FindObjectsByType<Snow.Player.PlayerNet>(FindObjectsSortMode.None))
                Skip(player.transform);

            int mask = ~0;
            foreach (string layer in new[] { "Interact", "Ignore Raycast", "Water", "UI", "TransparentFX" })
            {
                int l = LayerMask.NameToLayer(layer);
                if (l >= 0) mask &= ~(1 << l);
            }

            float t0 = Time.realtimeSinceStartup;
            var sources = new List<NavMeshBuildSource>();
            foreach (Bounds b in regions)
            {
                sources.Clear();
                NavMeshBuilder.CollectSources(b, mask, NavMeshCollectGeometry.PhysicsColliders, 0, markups, sources);
                var data = new NavMeshData(settings.agentTypeID);
                Instances.Add(NavMesh.AddNavMeshData(data));
                AsyncOperation op = NavMeshBuilder.UpdateNavMeshDataAsync(data, settings, sources, b);
                yield return op;
                BenchLog.Event("lesnik_nav", "center", b.center, "size", b.size, "sources", sources.Count);
            }
            foreach (Transform d in doors) AddObstacle(d);
            Ready = true;
            Debug.Log($"[Snow] Лесник: NavMesh {regions.Length} обл., {doors.Count} дверей, {Time.realtimeSinceStartup - t0:0.0} с.");
            done?.Invoke();
        }

        // a box over the leaf's own colliders, in its space, carving: it moves with the leaf when the door opens
        static void AddObstacle(Transform leaf)
        {
            var cols = leaf.GetComponentsInChildren<Collider>(true);
            bool any = false;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            foreach (var c in cols)
            {
                if (c == null || c.isTrigger) continue;
                Bounds wb = c.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = new Vector3((i & 1) == 0 ? wb.min.x : wb.max.x, (i & 2) == 0 ? wb.min.y : wb.max.y, (i & 4) == 0 ? wb.min.z : wb.max.z);
                    Vector3 l = leaf.InverseTransformPoint(corner);
                    min = Vector3.Min(min, l);
                    max = Vector3.Max(max, l);
                    any = true;
                }
            }
            if (!any) return;
            var o = leaf.gameObject.GetComponent<NavMeshObstacle>();
            if (o == null) o = leaf.gameObject.AddComponent<NavMeshObstacle>();
            o.shape = NavMeshObstacleShape.Box;
            o.center = (min + max) * 0.5f;
            o.size = Vector3.Max(max - min, Vector3.one * 0.05f);
            o.carving = true;
            o.carveOnlyStationary = true;
            Obstacles.Add(o);
        }

        public static void Clear()
        {
            foreach (var i in Instances) NavMesh.RemoveNavMeshData(i);
            Instances.Clear();
            foreach (var o in Obstacles) if (o != null) UnityEngine.Object.Destroy(o);
            Obstacles.Clear();
            if (AgentType >= 0) NavMesh.RemoveSettings(AgentType);
            AgentType = -1;
            Ready = false;
        }
    }
}
