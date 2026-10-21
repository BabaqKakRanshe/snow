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
    /// Forest integration: the base yard at half its area (<see cref="ForestYardPlan"/>, user decision 21.10). The yard's
    /// structures (helipad, mast, tanks, transformer, garage, generator container, crates, log pile, hatch, lamp and power
    /// poles, the sliding gate) go to their drawn-in places with the same turn; the ground under each is levelled to its new
    /// height and the Terrain trees there cleared; the power line (Forest_Wires, a world model inside the yard) is scaled with
    /// the yard about the building, so its wires meet the moved poles. The asphalt and the chain-link fence come from the bake.
    /// <see cref="ForestRelocation"/> learns each move, so the opening's items and points (yard jerrycan, pump nozzle, filler
    /// neck, the generator note) follow their structure. Computed from the package's layout every time: running it again puts
    /// things in the same place.
    /// </summary>
    public static class ForestYardStep
    {
        [Serializable] class Placed { public string m; public float[] p, q; }
        [Serializable] class Layout { public Placed[] prefabs; }

        static readonly Dictionary<string, float> Footprint = new Dictionary<string, float>
        {
            { "Helipad", 10f }, { "Garage", 11f }, { "FuelTanks", 8f }, { "GenContainer", 7f }, { "Trafo", 7f }, { "RadioMast", 5f },
            { "Crates", 3f }, { "LogPile", 4f }, { "Hatch", 3f }, { "Gate", 5f }, { "LightPole", 1.5f }, { "PowerPole", 1.5f },
        };

        public static void Run(Transform root)
        {
            var layout = JsonUtility.FromJson<Layout>(File.ReadAllText("Assets/Locations/Forest/Data/Forest_Layout.json"));
            ForestTerrainBaker.BakeMeta meta = ForestTerrainBaker.LoadMeta();
            Transform structures = root.Find("Structures");
            if (structures == null || meta == null) return;
            var tiles = new TerrainData[ForestTerrainBaker.TilesX, ForestTerrainBaker.TilesZ];
            for (int ix = 0; ix < ForestTerrainBaker.TilesX; ix++)
                for (int iz = 0; iz < ForestTerrainBaker.TilesZ; iz++)
                    tiles[ix, iz] = AssetDatabase.LoadAssetAtPath<TerrainData>(ForestTerrainBaker.TilePath(ix, iz));

            var entries = layout.prefabs.Where(p => p?.p != null && ForestYardPlan.Structures.Contains(p.m) && ForestYardPlan.InYard(V(p.p))).ToList();
            var used = new HashSet<Placed>();
            var from = new List<Vector3>();
            var to = new List<Vector3>();
            var radius = new List<float>();
            var placed = new List<Transform>();
            int moved = 0, cleared = 0;
            foreach (Transform t in structures.Cast<Transform>().ToArray())
            {
                if (!ForestYardPlan.Structures.Contains(t.name)) continue;
                // the layout entry of this instance: nearest by its original or its drawn-in place (a second run)
                Placed e = entries.Where(p => p.m == t.name && !used.Contains(p))
                                  .OrderBy(p => Mathf.Min(Flat(V(p.p) - t.position), Flat(ForestYardPlan.Map(V(p.p)) - t.position))).FirstOrDefault();
                if (e == null) continue;
                used.Add(e);
                Vector3 orig = V(e.p), plan = ForestYardPlan.Map(orig);
                float dy = ForestRelocationStepGround(tiles, meta, plan) - ForestRelocationStepGround(tiles, meta, orig);
                Vector3 at = new Vector3(plan.x, orig.y + dy, plan.z);
                // a second run measures ground it levelled itself: keep the first run's height for the same structure
                if (root.TryGetComponent(out ForestRelocation prev) && prev.yardFrom != null)
                    for (int k = 0; k < prev.yardFrom.Length && k < prev.yardTo.Length; k++)
                        if (Flat(prev.yardFrom[k] - orig) < 0.05f) { at.y = prev.yardTo[k].y; break; }
                t.SetPositionAndRotation(at, new Quaternion(e.q[0], e.q[1], e.q[2], e.q[3]));
                from.Add(orig);
                to.Add(at);
                placed.Add(t);
                moved++;
            }
            int pushed = Relax(structures, placed, to, tiles, meta);
            for (int i = 0; i < placed.Count; i++)
            {
                Transform t = placed[i];
                if (PrefabUtility.IsPartOfPrefabInstance(t)) PrefabUtility.RecordPrefabInstancePropertyModifications(t);
                float r = Footprint.TryGetValue(t.name, out float f) ? f : 3f;
                if (r >= 3f) Level(tiles, meta, to[i], to[i].y, r * 0.75f, r + 3f);
                cleared += Clear(tiles, meta, to[i], r + 1.5f);
                radius.Add(r + 2f);
            }
            // the power line: a world model in the yard, scaled with it about the building (wires meet the moved poles)
            Transform wires = root.Find("World/Forest_Wires");
            if (wires != null)
            {
                float s = ForestYardPlan.Scale;
                wires.localScale = new Vector3(s, 1f, s);
                wires.localPosition = new Vector3(ForestYardPlan.Centre.x * (1f - s), 0f, ForestYardPlan.Centre.z * (1f - s));
                if (PrefabUtility.IsPartOfPrefabInstance(wires)) PrefabUtility.RecordPrefabInstancePropertyModifications(wires);
            }
            foreach (TerrainData td in tiles) EditorUtility.SetDirty(td);
            AssetDatabase.SaveAssets();

            if (!root.TryGetComponent(out ForestRelocation rel)) rel = root.gameObject.AddComponent<ForestRelocation>();
            rel.yardFrom = from.ToArray();
            rel.yardTo = to.ToArray();
            rel.yardRadius = radius.ToArray();
            EditorUtility.SetDirty(rel);
            Debug.Log($"[Snow] Forest yard: {moved} structures drawn in x{ForestYardPlan.Scale} towards the main building (yard area / 2), " +
                      $"{pushed} pushed apart where the drawn-in footprints overlapped, power line scaled with them, ground levelled, {cleared} trees cleared; asphalt and chain-link from the bake.");
        }

        static Vector3 V(float[] a) => new Vector3(a[0], a[1], a[2]);

        /// <summary>The drawn-in structures keep their size, so some footprints overlap: overlapping pairs are pushed apart
        /// along the line between them (the main building does not move; the mast counts by its base, poles not at all) until
        /// they clear by half a metre. Plan rectangles from the solid colliders; deterministic, so a second run agrees.</summary>
        static int Relax(Transform structures, List<Transform> placed, List<Vector3> to, TerrainData[,] tiles, ForestTerrainBaker.BakeMeta meta)
        {
            Physics.SyncTransforms();
            Rect Footprint2(Transform t)
            {
                if (t.name == "RadioMast") return new Rect(t.position.x - 3f, t.position.z - 3f, 6f, 6f);
                bool any = false;
                var b = new Bounds();
                foreach (Collider c in t.GetComponentsInChildren<Collider>())
                {
                    if (c.isTrigger) continue;
                    if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
                }
                if (!any) return new Rect(t.position.x - 1f, t.position.z - 1f, 2f, 2f);
                return new Rect(b.min.x, b.min.z, b.size.x, b.size.z);
            }
            var fixedRects = new List<Rect>();
            foreach (Transform t in structures)
                if (t.name == "BaseMain") fixedRects.Add(Footprint2(t));
            var idx = Enumerable.Range(0, placed.Count).Where(i => placed[i].name != "LightPole" && placed[i].name != "PowerPole").ToList();
            var rect = idx.ToDictionary(i => i, i => Footprint2(placed[i]));
            var off = idx.ToDictionary(i => i, i => Vector2.zero);
            const float gap = 0.5f;
            int touched = 0;
            for (int pass = 0; pass < 40; pass++)
            {
                bool any = false;
                foreach (int i in idx)
                {
                    Rect a = Shift(rect[i], off[i]);
                    foreach (Rect f in fixedRects) any |= Push(a, f, v => off[i] += v, null);
                    foreach (int j in idx)
                    {
                        if (j <= i) continue;
                        Rect b = Shift(rect[j], off[j]);
                        int ii = i, jj = j;
                        any |= Push(Shift(rect[i], off[i]), b, v => off[ii] += v * 0.5f, v => off[jj] -= v * 0.5f);
                    }
                }
                if (!any) break;
            }
            foreach (int i in idx)
            {
                if (off[i].sqrMagnitude < 1e-4f) continue;
                Vector3 p = placed[i].position + new Vector3(off[i].x, 0f, off[i].y);
                p.y = to[i].y;                                         // a push of a few metres on the flat yard: same height
                placed[i].position = p;
                to[i] = p;
                touched++;
            }
            return touched;

            static Rect Shift(Rect r, Vector2 o) => new Rect(r.x + o.x, r.y + o.y, r.width, r.height);
            bool Push(Rect a, Rect b, Action<Vector2> moveA, Action<Vector2> moveB)
            {
                float ox = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin) + gap, oz = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin) + gap;
                if (ox <= 0f || oz <= 0f) return false;
                Vector2 d = a.center - b.center;
                // out along the axis with the smaller overlap, away from the other one
                Vector2 v = ox < oz ? new Vector2(Mathf.Sign(d.x == 0f ? 1f : d.x) * ox, 0f) : new Vector2(0f, Mathf.Sign(d.y == 0f ? 1f : d.y) * oz);
                if (moveB == null) moveA(v);
                else { moveA(v); moveB(v); }
                return true;
            }
        }

        static float Ground(TerrainData[,] tiles, ForestTerrainBaker.BakeMeta meta, Vector3 p) => ForestRelocationStepGround(tiles, meta, p);
        static float Flat(Vector3 d) => new Vector2(d.x, d.z).magnitude;

        static bool Tile(ForestTerrainBaker.BakeMeta meta, Vector3 p, out int ix, out int iz, out Vector3 o)
        {
            ix = Mathf.FloorToInt((p.x - ForestTerrainBaker.WorldX0) / ForestTerrainBaker.Tile);
            iz = Mathf.FloorToInt((p.z - ForestTerrainBaker.WorldZ0) / ForestTerrainBaker.Tile);
            o = ForestTerrainBaker.TileOrigin(ix, iz, meta.originY);
            return ix >= 0 && iz >= 0 && ix < ForestTerrainBaker.TilesX && iz < ForestTerrainBaker.TilesZ;
        }

        static float ForestRelocationStepGround(TerrainData[,] tiles, ForestTerrainBaker.BakeMeta meta, Vector3 p)
        {
            if (!Tile(meta, p, out int ix, out int iz, out Vector3 o) || tiles[ix, iz] == null) return p.y;
            return meta.originY + tiles[ix, iz].GetInterpolatedHeight((p.x - o.x) / ForestTerrainBaker.Tile, (p.z - o.z) / ForestTerrainBaker.Tile);
        }

        static void Level(TerrainData[,] tiles, ForestTerrainBaker.BakeMeta meta, Vector3 at, float y, float inner, float outer)
        {
            if (!Tile(meta, at, out int ix, out int iz, out Vector3 o) || tiles[ix, iz] == null) return;
            TerrainData td = tiles[ix, iz];
            int res = td.heightmapResolution;
            float step = ForestTerrainBaker.Tile / (res - 1);
            int cx = Mathf.RoundToInt((at.x - o.x) / step), cz = Mathf.RoundToInt((at.z - o.z) / step), rr = Mathf.CeilToInt(outer / step) + 1;
            int x0 = Mathf.Clamp(cx - rr, 0, res - 1), x1 = Mathf.Clamp(cx + rr, 0, res - 1), z0 = Mathf.Clamp(cz - rr, 0, res - 1), z1 = Mathf.Clamp(cz + rr, 0, res - 1);
            float[,] h = td.GetHeights(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
            float target = Mathf.Clamp01((y - meta.originY) / meta.sizeY);
            for (int z = 0; z <= z1 - z0; z++)
                for (int x = 0; x <= x1 - x0; x++)
                {
                    float d = new Vector2((x0 + x - cx) * step, (z0 + z - cz) * step).magnitude;
                    float k = 1f - Mathf.SmoothStep(0f, 1f, (d - inner) / Mathf.Max(0.1f, outer - inner));
                    h[z, x] = Mathf.Lerp(h[z, x], target, k);
                }
            td.SetHeights(x0, z0, h);
        }

        static int Clear(TerrainData[,] tiles, ForestTerrainBaker.BakeMeta meta, Vector3 at, float r)
        {
            if (!Tile(meta, at, out int ix, out int iz, out Vector3 o) || tiles[ix, iz] == null) return 0;
            TerrainData td = tiles[ix, iz];
            TreeInstance[] all = td.treeInstances;
            TreePrototype[] protos = td.treePrototypes;
            // the yard fence is on the terrain too: its sections stay
            var keep = all.Where(t => (protos[t.prototypeIndex].prefab != null && protos[t.prototypeIndex].prefab.name.StartsWith("FenceChain", StringComparison.Ordinal)) ||
                                      Flat(o + Vector3.Scale(t.position, new Vector3(ForestTerrainBaker.Tile, 0f, ForestTerrainBaker.Tile)) - at) > r).ToArray();
            if (keep.Length != all.Length) td.SetTreeInstances(keep, false);
            return all.Length - keep.Length;
        }
    }
}
