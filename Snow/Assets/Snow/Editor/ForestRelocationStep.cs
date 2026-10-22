using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Snow.Levels.Forest;
using UnityEditor;
using UnityEngine;

namespace Snow.EditorTools
{
    /// <summary>
    /// Forest integration: the checkpoint moves out along the road (user 21.10: "КПП намного дальше от базы", the map was
    /// extended for it). The booth (Checkpoint) and the barrier go <see cref="Distance"/> m back along the gravel road, away from
    /// the base, turned with the road; the base's sliding gate stays in the yard fence. Everything is computed from the
    /// package's data (layout, opening zones, the road mesh of Models/Forest_Terrain.fbx), so running it again puts things in
    /// the same place. The terrain under the new booth and barrier is levelled with a soft edge and the Terrain trees standing
    /// there are removed (the only edit of the tiles outside the bake). At run time <see cref="ForestRelocation"/> moves the
    /// opening's items, zones and sound points of the group the same way.
    /// </summary>
    public static class ForestRelocationStep
    {
        const float Distance = 260f, GroupRadius = 16f, FlatInner = 6.5f, FlatOuter = 11f, ClearBooth = 10f, ClearBarrier = 6f;
        static readonly Vector3 Base = new Vector3(340f, 0f, -360f);

        [Serializable] class Placed { public string m; public float[] p, q; }
        [Serializable] class Layout { public Placed[] prefabs; }
        [Serializable] class Zone { public string id; public float[] p, q; }
        [Serializable] class Opening { public Zone[] zones, items; }

        static Vector3 V(float[] a) => new Vector3(a[0], a[1], a[2]);
        static Quaternion Q(float[] a) => new Quaternion(a[0], a[1], a[2], a[3]);

        public static void Run(Transform root)
        {
            var layout = JsonUtility.FromJson<Layout>(File.ReadAllText("Assets/Locations/Forest/Data/Forest_Layout.json"));
            Placed booth = layout.prefabs.FirstOrDefault(p => p.m == "Checkpoint"), barrier = layout.prefabs.FirstOrDefault(p => p.m == "Barrier");
            Transform tBooth = root.Find("Structures/Checkpoint"), tBarrier = root.Find("Structures/Barrier");
            ForestTerrainBaker.BakeMeta meta = ForestTerrainBaker.LoadMeta();
            if (booth == null || barrier == null || tBooth == null || tBarrier == null || meta == null)
            {
                Debug.LogWarning("[Snow] Forest checkpoint: booth, barrier or terrain not found; it stays where the package put it.");
                return;
            }
            var tiles = new TerrainData[ForestTerrainBaker.TilesX, ForestTerrainBaker.TilesZ];
            for (int ix = 0; ix < ForestTerrainBaker.TilesX; ix++)
                for (int iz = 0; iz < ForestTerrainBaker.TilesZ; iz++)
                    tiles[ix, iz] = AssetDatabase.LoadAssetAtPath<TerrainData>(ForestTerrainBaker.TilePath(ix, iz));

            // road centreline samples: centres of the gravel road's triangles
            var road = RoadCentres();
            Vector3 oldPivot = V(barrier.p);
            var cand = road.Where(c => Flat(c - oldPivot) > Distance - 10f && Flat(c - oldPivot) < Distance + 15f).ToList();
            if (cand.Count == 0) { Debug.LogWarning("[Snow] Forest checkpoint: no road found 260 m out; it stays."); return; }
            Vector3 site = cand.OrderByDescending(c => Flat(c - Base)).First();
            Vector3 oldTan = Tangent(road, oldPivot), newTan = Tangent(road, site);
            float yaw = Vector3.SignedAngle(oldTan, newTan, Vector3.up);
            float dy = Ground(tiles, meta, site) - Ground(tiles, meta, oldPivot);
            Vector3 newPivot = new Vector3(site.x, oldPivot.y + dy, site.z);
            // a second run measures ground it levelled itself: keep the height of the first run at the same site
            if (root.TryGetComponent(out ForestRelocation prev) && Flat(prev.newPivot - newPivot) < 0.1f && prev.newPivot.y != 0f) newPivot.y = prev.newPivot.y;
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            Vector3 T(Vector3 p) => newPivot + r * (p - oldPivot);

            // the models (scene overrides of the builder's instances)
            Place(tBooth, T(V(booth.p)), r * Q(booth.q));
            Place(tBarrier, T(V(barrier.p)), r * Q(barrier.q));
            // the opening's zones of the group
            var opening = JsonUtility.FromJson<Opening>(File.ReadAllText("Assets/Locations/Forest/Data/Forest_Opening.json"));
            Transform zones = root.Find("ForestZones");
            int zonesMoved = 0;
            if (opening?.zones != null && zones != null)
                foreach (Zone z in opening.zones)
                {
                    if (z?.p == null || Flat(V(z.p) - oldPivot) > GroupRadius) continue;
                    Transform zt = zones.Find("IV_" + z.id);
                    if (zt == null) continue;
                    zt.SetPositionAndRotation(T(V(z.p)), r * Q(z.q));
                    zonesMoved++;
                }
            // the static padlock copy on the barrier arm (ForestLevelStep puts it at the data position): with the barrier
            // (client check 21.10 found it hanging at the old checkpoint)
            Zone pad = opening?.items?.FirstOrDefault(i => i?.id == ForestItems.PadlockId && i.p != null && i.q != null);
            ForestOpening fo = root.GetComponentInChildren<ForestOpening>(true);
            bool padMoved = pad != null && fo != null && fo.padlockProxy != null;
            if (padMoved)
            {
                fo.padlockProxy.transform.SetPositionAndRotation(T(V(pad.p)), r * Q(pad.q));
                EditorUtility.SetDirty(fo.padlockProxy.transform);
            }

            // level the ground under the booth and the barrier, clear the trees there
            Vector3 boothAt = T(V(booth.p)), barrierAt = T(V(barrier.p));
            Level(tiles, meta, boothAt, boothAt.y);
            Level(tiles, meta, barrierAt, barrierAt.y);
            int cleared = Clear(tiles, meta, boothAt, ClearBooth) + Clear(tiles, meta, barrierAt, ClearBarrier);
            foreach (TerrainData td in tiles) EditorUtility.SetDirty(td);
            AssetDatabase.SaveAssets();

            if (!root.TryGetComponent(out ForestRelocation rel)) rel = root.gameObject.AddComponent<ForestRelocation>();
            rel.oldPivot = oldPivot;
            rel.newPivot = newPivot;
            rel.yaw = yaw;
            rel.radius = GroupRadius;
            EditorUtility.SetDirty(rel);
            Debug.Log($"[Snow] Forest checkpoint: booth and barrier moved {Flat(newPivot - oldPivot):0} m out along the road " +
                      $"(to {newPivot}, turned {yaw:0.#}°; {Flat(newPivot - Base):0} m from the base, was {Flat(oldPivot - Base):0}); " +
                      $"{zonesMoved} opening zones{(padMoved ? " and the padlock copy" : "")} moved, ground levelled, {cleared} trees cleared; the yard's sliding gate stays.");
        }

        static float Flat(Vector3 d) => new Vector2(d.x, d.z).magnitude;

        static void Place(Transform t, Vector3 p, Quaternion q)
        {
            t.SetPositionAndRotation(p, q);
            if (PrefabUtility.IsPartOfPrefabInstance(t)) PrefabUtility.RecordPrefabInstancePropertyModifications(t);
            EditorUtility.SetDirty(t);
        }

        static List<Vector3> RoadCentres()
        {
            var list = new List<Vector3>();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Locations/Forest/Models/Forest_Terrain.fbx");
            Transform rt = model != null ? model.transform.Find("Forest_Road") : null;
            if (rt == null || !rt.TryGetComponent(out MeshFilter mf) || !rt.TryGetComponent(out MeshRenderer mr)) return list;
            Mesh m = mf.sharedMesh;
            Vector3[] v = m.vertices;
            for (int s = 0; s < m.subMeshCount && s < mr.sharedMaterials.Length; s++)
            {
                if (mr.sharedMaterials[s] == null || mr.sharedMaterials[s].name != "M_RoadDirt") continue;
                int[] t = m.GetTriangles(s);
                for (int k = 0; k < t.Length; k += 3) list.Add((v[t[k]] + v[t[k + 1]] + v[t[k + 2]]) / 3f);
            }
            return list;
        }

        /// <summary>Road direction at a point (principal axis of the nearby centres), pointing towards the base.</summary>
        static Vector3 Tangent(List<Vector3> road, Vector3 at)
        {
            var near = road.Where(c => Flat(c - at) < 9f).ToList();
            if (near.Count < 3) return (Base - at).normalized;
            Vector3 mean = near.Aggregate(Vector3.zero, (a, c) => a + c) / near.Count;
            float xx = 0f, xz = 0f, zz = 0f;
            foreach (Vector3 c in near) { float dx = c.x - mean.x, dz = c.z - mean.z; xx += dx * dx; xz += dx * dz; zz += dz * dz; }
            float angle = 0.5f * Mathf.Atan2(2f * xz, xx - zz);
            var dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            if (Vector3.Dot(dir, Base - at) < 0f) dir = -dir;
            return dir;
        }

        static bool Tile(ForestTerrainBaker.BakeMeta meta, Vector3 p, out int ix, out int iz, out Vector3 o)
        {
            ix = Mathf.FloorToInt((p.x - ForestTerrainBaker.WorldX0) / ForestTerrainBaker.Tile);
            iz = Mathf.FloorToInt((p.z - ForestTerrainBaker.WorldZ0) / ForestTerrainBaker.Tile);
            o = ForestTerrainBaker.TileOrigin(ix, iz, meta.originY);
            return ix >= 0 && iz >= 0 && ix < ForestTerrainBaker.TilesX && iz < ForestTerrainBaker.TilesZ;
        }

        static float Ground(TerrainData[,] tiles, ForestTerrainBaker.BakeMeta meta, Vector3 p)
        {
            if (!Tile(meta, p, out int ix, out int iz, out Vector3 o) || tiles[ix, iz] == null) return p.y;
            return meta.originY + tiles[ix, iz].GetInterpolatedHeight((p.x - o.x) / ForestTerrainBaker.Tile, (p.z - o.z) / ForestTerrainBaker.Tile);
        }

        /// <summary>Heights around a point to its height, full inside <see cref="FlatInner"/>, fading out to <see cref="FlatOuter"/>.</summary>
        static void Level(TerrainData[,] tiles, ForestTerrainBaker.BakeMeta meta, Vector3 at, float y)
        {
            if (!Tile(meta, at, out int ix, out int iz, out Vector3 o) || tiles[ix, iz] == null) return;
            TerrainData td = tiles[ix, iz];
            int res = td.heightmapResolution;
            float step = ForestTerrainBaker.Tile / (res - 1);
            int cx = Mathf.RoundToInt((at.x - o.x) / step), cz = Mathf.RoundToInt((at.z - o.z) / step), rr = Mathf.CeilToInt(FlatOuter / step) + 1;
            int x0 = Mathf.Clamp(cx - rr, 0, res - 1), x1 = Mathf.Clamp(cx + rr, 0, res - 1), z0 = Mathf.Clamp(cz - rr, 0, res - 1), z1 = Mathf.Clamp(cz + rr, 0, res - 1);
            float[,] h = td.GetHeights(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
            float target = Mathf.Clamp01((y - meta.originY) / meta.sizeY);
            for (int z = 0; z <= z1 - z0; z++)
                for (int x = 0; x <= x1 - x0; x++)
                {
                    float d = new Vector2((x0 + x - cx) * step, (z0 + z - cz) * step).magnitude;
                    float k = 1f - Mathf.SmoothStep(0f, 1f, (d - FlatInner) / (FlatOuter - FlatInner));
                    h[z, x] = Mathf.Lerp(h[z, x], target, k);
                }
            td.SetHeights(x0, z0, h);
        }

        static int Clear(TerrainData[,] tiles, ForestTerrainBaker.BakeMeta meta, Vector3 at, float radius)
        {
            if (!Tile(meta, at, out int ix, out int iz, out Vector3 o) || tiles[ix, iz] == null) return 0;
            TerrainData td = tiles[ix, iz];
            TreeInstance[] all = td.treeInstances;
            var keep = new List<TreeInstance>(all.Length);
            foreach (TreeInstance t in all)
            {
                Vector3 p = o + Vector3.Scale(t.position, new Vector3(ForestTerrainBaker.Tile, 0f, ForestTerrainBaker.Tile));
                if (Flat(p - at) > radius) keep.Add(t);
            }
            if (keep.Count != all.Length) td.SetTreeInstances(keep.ToArray(), false);
            return all.Length - keep.Count;
        }
    }
}
