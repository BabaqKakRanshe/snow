using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Snow.EditorTools
{
    /// <summary>
    /// Forest integration, the base yard dressed like the user's target picture (23.10, docs/gdd/research/RESEARCH_BASE_YARD.md):
    /// one road from the gate past the front door to the garage, parking, gravel under the service yard and the mast, worn
    /// paths, tall weeds along the fence and in the empty corners, clusters of young firs, lamps along the fence (off until
    /// the base has power: Light_power_* of the LightPole prefab) and props from the project's location kits. Everything is
    /// read from <see cref="DataPath"/> in the yard's own frame (u east, v north, metres from the main building, turned with
    /// it). Inside the fence the ground paint, the details and the Terrain trees are written from the data alone, so a
    /// second run (or a run after a bake) gives the same yard; brush strokes inside the fence do not survive it.
    /// Runs after <see cref="ForestYardStep"/> (which puts the yard's structures back where the package plan has them) and
    /// before <see cref="ForestBeamsStep"/> (which gives the new lamps their light cones).
    /// </summary>
    public static class ForestYardDressStep
    {
        public const string DataPath = "Assets/Snow/Levels/Forest/ForestYardDress.json";
        public const string RootName = "ForestYardDress";
        const string LampPrefab = "Assets/Locations/Forest/Prefabs/LightPole.prefab";

        [Serializable] public class Area { public string n; public float[] r; }
        [Serializable] public class Path { public string n; public float w; public float[] p; }
        [Serializable] public class Move { public string m; public float[] from, to; }
        [Serializable] public class Lamp { public float u, v, fu, fv; }
        [Serializable] public class Prop { public string prefab; public float u, v, yaw; }
        [Serializable] public class Spot { public float u, v, r; public int n; }
        [Serializable] public class Trees { public int seed; public Spot[] clusters; public int fenceBushes; }
        [Serializable]
        public class Data
        {
            public float[] centre, fence;
            public float yaw, shoulder, fenceBand, lampIntensity, lampRange;
            public Area[] asphalt, gravel, wild, keepClear;
            public Path[] paths;
            public Move[] moves;
            public Lamp[] lamps;
            public Prop[] props;
            public string[] clumpPrefabs;
            public int fenceClumps;
            public Spot[] clumps;
            public Trees trees;
        }

        /// <summary>Ground of one point: shares of asphalt, gravel, path (and its worn middle), and how wild the rest is.</summary>
        struct Ground { public float asphalt, gravel, path, track, wild; }

        static Data _d;
        static float _cos, _sin;

        public static void Run(Transform root)
        {
            if (!File.Exists(DataPath)) { Debug.LogWarning($"[Snow] Forest yard dress: {DataPath} not found; yard left as it is."); return; }
            _d = JsonUtility.FromJson<Data>(File.ReadAllText(DataPath));
            _cos = Mathf.Cos(_d.yaw * Mathf.Deg2Rad);
            _sin = Mathf.Sin(_d.yaw * Mathf.Deg2Rad);
            Transform structures = root.Find("Structures");
            if (structures == null) { Debug.LogWarning("[Snow] Forest yard dress: Structures not found."); return; }

            int moved = MoveStructures(structures);
            var tiles = Terrain.activeTerrains.Where(t => t != null && t.terrainData != null && t.transform.IsChildOf(root) && Overlaps(t)).ToArray();
            int painted = 0, cells = 0, removed = 0, planted = 0;
            var lampSpots = _d.lamps.Select(l => new Vector2(l.u, l.v)).Concat(_d.moves.Select(m => new Vector2(m.to[0], m.to[1]))).ToList();
            foreach (Terrain t in tiles)
            {
                painted += Paint(t);
                cells += Details(t);
            }
            (removed, planted) = PlantTrees(tiles, lampSpots);
            foreach (Terrain t in tiles) EditorUtility.SetDirty(t.terrainData);
            AssetDatabase.SaveAssets();

            // the placed objects: one root, rebuilt each run
            Transform old = root.Find(RootName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var dress = new GameObject(RootName).transform;
            dress.SetParent(root, false);
            Transform pole = structures.Find("LightPole"), crates = structures.Find("Crates");
            int lamps = 0, props = 0, clumps = 0, missing = 0;
            var lampAsset = AssetDatabase.LoadAssetAtPath<GameObject>(LampPrefab);
            foreach (Lamp l in _d.lamps)
            {
                GameObject go = Place(lampAsset, dress, l.u, l.v, 0f, pole);
                if (go == null) { missing++; continue; }
                FaceArm(go.transform, new Vector2(l.fu, l.fv));
                go.name = "LightPole";
                lamps++;
            }
            foreach (Prop p in _d.props)
            {
                if (Place(AssetDatabase.LoadAssetAtPath<GameObject>(p.prefab), dress, p.u, p.v, p.yaw, crates) == null) { missing++; continue; }
                props++;
                if (BlockedBy(new Vector2(p.u, p.v), 0f, out string what) && !p.prefab.Contains("ChainFence") && !p.prefab.Contains("Cabinet"))
                    Debug.LogWarning($"[Snow] Forest yard dress: {System.IO.Path.GetFileNameWithoutExtension(p.prefab)} at ({p.u}, {p.v}) stands on {what}.");
            }
            clumps = PlaceClumps(dress, crates, lampSpots, ref missing);
            int bright = Brighten(structures, dress);

            Debug.Log($"[Snow] Forest yard dress ({DataPath}): {moved} yard lamps moved off the new roads, {painted} ground texels and " +
                      $"{cells} detail cells inside the fence rewritten ({_d.asphalt.Length} asphalt, {_d.gravel.Length} gravel, {_d.paths.Length} paths, " +
                      $"weeds along {_d.fenceBand:0} m of the fence), Terrain trees inside the fence {removed} removed / {planted} planted, " +
                      $"{lamps} lamps (power), {bright} yard lamps at {_d.lampIntensity:0.#} / {_d.lampRange:0} m, {props} props, {clumps} grass clumps" + (missing > 0 ? $", {missing} prefabs MISSING" : "") + ".");
        }

        // ------------------------------------------------------------------ frame

        static Vector3 World(float u, float v) => new Vector3(_d.centre[0] + u * _cos + v * _sin, 0f, _d.centre[1] - u * _sin + v * _cos);
        static Vector2 Yard(float x, float z)
        {
            float dx = x - _d.centre[0], dz = z - _d.centre[1];
            return new Vector2(dx * _cos - dz * _sin, dx * _sin + dz * _cos);
        }

        static bool InFence(Vector2 p, float inset) =>
            p.x > _d.fence[0] + inset && p.x < _d.fence[2] - inset && p.y > _d.fence[1] + inset && p.y < _d.fence[3] - inset;

        static float ToFence(Vector2 p) => Mathf.Min(Mathf.Min(p.x - _d.fence[0], _d.fence[2] - p.x), Mathf.Min(p.y - _d.fence[1], _d.fence[3] - p.y));

        static bool Overlaps(Terrain t)
        {
            Vector3 o = t.transform.position, s = t.terrainData.size;
            foreach (float u in new[] { _d.fence[0], _d.fence[2] })
                foreach (float v in new[] { _d.fence[1], _d.fence[3] })
                {
                    Vector3 w = World(u, v);
                    if (w.x >= o.x - 5f && w.x <= o.x + s.x + 5f && w.z >= o.z - 5f && w.z <= o.z + s.z + 5f) return true;
                }
            return false;
        }

        /// <summary>Signed depth of a point inside a rect (negative outside), metres.</summary>
        static float Inside(float[] r, Vector2 p) => Mathf.Min(Mathf.Min(p.x - r[0], r[2] - p.x), Mathf.Min(p.y - r[1], r[3] - p.y));

        static float ToPath(Path path, Vector2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i + 3 < path.p.Length; i += 2)
            {
                Vector2 a = new Vector2(path.p[i], path.p[i + 1]), b = new Vector2(path.p[i + 2], path.p[i + 3]);
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                best = Mathf.Min(best, (a + ab * t - p).magnitude);
            }
            return best;
        }

        static float Edge(float depth, float soft) => Mathf.Clamp01(depth / soft + 0.5f);

        /// <summary>What the ground at a yard point is; edges wander by a few decimetres (like the bake's ragged road edges).</summary>
        static Ground Sample(Vector2 p, Vector3 w)
        {
            float n = Mathf.PerlinNoise(w.x / 1.7f + 3.1f, w.z / 1.7f + 8.3f) * 0.65f + Mathf.PerlinNoise(w.x / 0.6f + 1.9f, w.z / 0.6f + 4.4f) * 0.35f - 0.5f;
            var g = new Ground();
            float nearRoad = float.MinValue;
            foreach (Area a in _d.asphalt)
            {
                float d = Inside(a.r, p);
                g.asphalt = Mathf.Max(g.asphalt, Edge(d + n * 0.5f, 0.3f));
                g.gravel = Mathf.Max(g.gravel, Edge(d + _d.shoulder + n * 0.9f, 0.4f));
                nearRoad = Mathf.Max(nearRoad, d);
            }
            foreach (Area a in _d.gravel) g.gravel = Mathf.Max(g.gravel, Edge(Inside(a.r, p) + n * 0.8f, 0.5f));
            g.gravel *= 1f - g.asphalt;
            foreach (Path path in _d.paths)
            {
                float d = ToPath(path, p);
                float share = Edge(path.w * 0.5f - d + n * 0.5f, 0.35f);
                if (share <= g.path) continue;
                g.path = share;
                g.track = share * Mathf.Clamp01(1f - d / (path.w * 0.3f)) * 0.7f;
            }
            float hard = g.asphalt + g.gravel;
            g.path *= 1f - hard;
            g.track *= 1f - hard;
            // wild: a band along the fence and the empty corners, never right next to a road; the borders wander by several
            // metres (large noise), so no rectangle shows from above
            float big = Mathf.PerlinNoise(w.x / 14f + 5.7f, w.z / 14f + 2.3f) - 0.5f;
            float wild = Mathf.Clamp01((_d.fenceBand + big * 8f - ToFence(p)) / 3f + n * 0.5f);
            foreach (Area a in _d.wild) wild = Mathf.Max(wild, Mathf.Clamp01((Inside(a.r, p) + big * 12f) / 4f + 0.2f + n * 0.5f));
            wild *= Mathf.Clamp01((-nearRoad - 1.5f) / 3f);
            g.wild = wild;
            return g;
        }

        // ------------------------------------------------------------------ ground paint

        static int Layer(TerrainData td, string name) => Array.FindIndex(td.terrainLayers, l => l != null && l.name == name);

        static int Paint(Terrain t)
        {
            TerrainData td = t.terrainData;
            int floor = Layer(td, "TL_ForestFloor"), dark = Layer(td, "TL_ForestFloorDark"), gravel = Layer(td, "TL_RoadGravel"),
                soil = Layer(td, "TL_TrailSoil"), track = Layer(td, "TL_TrailTrack"), asphalt = Layer(td, "TL_Asphalt");
            if (floor < 0 || dark < 0 || gravel < 0 || soil < 0 || track < 0 || asphalt < 0)
            {
                Debug.LogWarning($"[Snow] Forest yard dress: {td.name} lacks the bake's terrain layers; no ground paint.");
                return 0;
            }
            int n = td.alphamapResolution, layers = td.alphamapLayers, count = 0;
            float tx = td.size.x / n;
            Vector3 o = t.transform.position;
            float[,,] map = td.GetAlphamaps(0, 0, n, n);
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    var w = new Vector3(o.x + (x + 0.5f) * tx, 0f, o.z + (z + 0.5f) * tx);
                    Vector2 p = Yard(w.x, w.z);
                    if (!InFence(p, 0.3f)) continue;
                    Ground g = Sample(p, w);
                    float rest = Mathf.Max(0f, 1f - g.asphalt - g.gravel - g.path);
                    float patch = Mathf.PerlinNoise(w.x / 9f + 11.3f, w.z / 9f + 7.1f) - 0.5f;
                    float darkK = Mathf.Clamp01(0.2f + 0.45f * g.wild + patch * 0.6f);
                    for (int l = 0; l < layers; l++) map[z, x, l] = 0f;
                    map[z, x, asphalt] = g.asphalt;
                    map[z, x, gravel] = g.gravel;
                    map[z, x, soil] = g.path - g.track;
                    map[z, x, track] = g.track;
                    map[z, x, dark] = rest * darkK;
                    map[z, x, floor] = rest * (1f - darkK);
                    count++;
                }
            td.SetAlphamaps(0, 0, map);
            return count;
        }

        // ------------------------------------------------------------------ details (weeds, ferns, pebbles)

        static int Proto(DetailPrototype[] protos, string name) =>
            Array.FindIndex(protos, d => d.prototype != null && d.prototype.name == name);

        static float Hash(int x, int z, int salt)
        {
            uint h = (uint)(x * 73856093) ^ (uint)(z * 19349663) ^ (uint)(salt * 83492791);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return (h & 0xffffff) / (float)0x1000000;
        }

        static int Details(Terrain t)
        {
            TerrainData td = t.terrainData;
            DetailPrototype[] protos = td.detailPrototypes;
            int[] weeds = { Proto(protos, "Weeds_1"), Proto(protos, "Weeds_2"), Proto(protos, "Weeds_3") };
            int[] ferns = { Proto(protos, "Fern_1"), Proto(protos, "Fern_2") };
            int[] twigs = { Proto(protos, "Branches_1"), Proto(protos, "Branches_2") };
            int[] stones = { Proto(protos, "Pebbles_1"), Proto(protos, "Pebbles_2") };
            int n = td.detailResolution, count = 0;
            float cell = td.size.x / n;
            Vector3 o = t.transform.position;
            var maps = new int[protos.Length][,];
            for (int l = 0; l < protos.Length; l++) maps[l] = td.GetDetailLayer(0, 0, n, n, l);
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    var w = new Vector3(o.x + (x + 0.5f) * cell, 0f, o.z + (z + 0.5f) * cell);
                    Vector2 p = Yard(w.x, w.z);
                    if (!InFence(p, 0.5f)) continue;
                    for (int l = 0; l < protos.Length; l++) maps[l][z, x] = 0;
                    count++;
                    Ground g = Sample(p, w);
                    if (g.asphalt + g.gravel + g.path > 0.35f)
                    {
                        if (g.gravel > 0.5f && Hash(x, z, 1) < 0.1f) Set(maps, stones[(int)(Hash(x, z, 2) * 2f) % 2], z, x, 1);
                        continue;
                    }
                    if (Hash(x, z, 3) < 0.06f + 0.75f * g.wild) Set(maps, weeds[(int)(Hash(x, z, 4) * 3f) % 3], z, x, 1);
                    if (g.wild > 0.6f && Hash(x, z, 5) < 0.3f * g.wild) Set(maps, weeds[(int)(Hash(x, z, 6) * 3f) % 3], z, x, 1);
                    if (g.wild > 0.5f && Hash(x, z, 7) < 0.15f) Set(maps, ferns[(int)(Hash(x, z, 8) * 2f) % 2], z, x, 1);
                    if (Hash(x, z, 9) < 0.02f * g.wild) Set(maps, twigs[(int)(Hash(x, z, 10) * 2f) % 2], z, x, 1);
                }
            for (int l = 0; l < protos.Length; l++) td.SetDetailLayer(0, 0, l, maps[l]);
            return count;

            static void Set(int[][,] m, int layer, int z, int x, int add) { if (layer >= 0) m[layer][z, x] += add; }
        }

        // ------------------------------------------------------------------ trees

        static bool BlockedBy(Vector2 p, float margin, out string what)
        {
            foreach (Area a in _d.keepClear)
                if (Inside(a.r, p) > -margin) { what = a.n; return true; }
            what = null;
            return false;
        }

        static (int removed, int planted) PlantTrees(Terrain[] tiles, List<Vector2> lamps)
        {
            var rng = new System.Random(_d.trees.seed);
            var spots = new List<(Vector2 p, string proto, float scale, float r)>();
            bool Free(Vector2 p, float r)
            {
                if (!InFence(p, 3f) || BlockedBy(p, 2f, out _)) return false;
                if (lamps.Any(l => (l - p).magnitude < 3f)) return false;
                Vector3 w = World(p.x, p.y);
                Ground g = Sample(p, w);
                if (g.asphalt + g.gravel + g.path > 0.01f) return false;
                // keep 1.5 m off any hard ground
                foreach (Vector2 d in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down })
                {
                    Ground e = Sample(p + d * 1.5f, World(p.x + d.x * 1.5f, p.y + d.y * 1.5f));
                    if (e.asphalt + e.gravel + e.path > 0.01f) return false;
                }
                return spots.All(s => (s.p - p).magnitude >= Mathf.Max(r, s.r));
            }
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            foreach (Spot c in _d.trees.clusters)
            {
                int placed = 0;
                for (int tries = 0; tries < 200 && placed < c.n; tries++)
                {
                    float ang = R(0f, Mathf.PI * 2f), dist = c.r * Mathf.Sqrt(R(0f, 1f));
                    var p = new Vector2(c.u + Mathf.Cos(ang) * dist, c.v + Mathf.Sin(ang) * dist);
                    bool rim = dist > c.r * 0.65f && placed > 0 && rng.NextDouble() < 0.5;
                    string proto = rim ? (rng.NextDouble() < 0.5 ? "Bush_1" : "Bush_2")
                                 : placed == 0 && c.r >= 6f ? (rng.NextDouble() < 0.5 ? "Spruce_13" : "Fir_7")
                                 : "Fir_4";
                    float r = rim ? 1.8f : 2.4f;
                    if (!Free(p, r)) continue;
                    float scale = proto.StartsWith("Bush") ? R(0.7f, 1.0f) : proto == "Fir_4" ? R(0.55f, 1.1f) : R(0.75f, 1.0f);
                    spots.Add((p, proto, scale, r));
                    placed++;
                }
            }
            for (int i = 0, tries = 0; i < _d.trees.fenceBushes && tries < 400; tries++)
            {
                Vector2 p = OnFenceBand(rng, 3.2f, 4.5f);
                if (!Free(p, 2f)) continue;
                spots.Add((p, rng.NextDouble() < 0.5 ? "Bush_1" : "Bush_2", R(0.7f, 1.0f), 2f));
                i++;
            }

            int removed = 0, planted = 0;
            foreach (Terrain t in tiles)
            {
                TerrainData td = t.terrainData;
                TreePrototype[] protos = td.treePrototypes;
                Vector3 o = t.transform.position, s = td.size;
                var keep = new List<TreeInstance>();
                foreach (TreeInstance ti in td.treeInstances)
                {
                    string nm = protos[ti.prototypeIndex].prefab != null ? protos[ti.prototypeIndex].prefab.name : "";
                    Vector2 p = Yard(o.x + ti.position.x * s.x, o.z + ti.position.z * s.z);
                    bool fence = nm.StartsWith("OldFence", StringComparison.Ordinal) || nm.StartsWith("EuroPost", StringComparison.Ordinal) || nm.StartsWith("FenceChain", StringComparison.Ordinal);
                    if (!fence && InFence(p, 0f)) { removed++; continue; }
                    keep.Add(ti);
                }
                foreach (var sp in spots)
                {
                    Vector3 w = World(sp.p.x, sp.p.y);
                    if (w.x < o.x || w.z < o.z || w.x >= o.x + s.x || w.z >= o.z + s.z) continue;
                    int proto = Array.FindIndex(protos, pr => pr.prefab != null && pr.prefab.name == sp.proto);
                    if (proto < 0) { Debug.LogWarning($"[Snow] Forest yard dress: no Terrain tree prototype {sp.proto} on {td.name}."); continue; }
                    float nx = (w.x - o.x) / s.x, nz = (w.z - o.z) / s.z;
                    keep.Add(new TreeInstance
                    {
                        prototypeIndex = proto,
                        position = new Vector3(nx, td.GetInterpolatedHeight(nx, nz) / s.y, nz),
                        heightScale = sp.scale,
                        widthScale = sp.scale * R(0.9f, 1.1f),
                        rotation = R(0f, Mathf.PI * 2f),
                        color = Color.white,
                        lightmapColor = Color.white,
                    });
                    planted++;
                }
                td.SetTreeInstances(keep.ToArray(), false);   // no snap: the bake's rocks and logs keep their own heights
            }
            return (removed, planted);
        }

        /// <summary>A random point between <paramref name="near"/> and <paramref name="far"/> metres inside the fence, along its length.</summary>
        static Vector2 OnFenceBand(System.Random rng, float near, float far)
        {
            float w = _d.fence[2] - _d.fence[0], h = _d.fence[3] - _d.fence[1];
            float along = (float)rng.NextDouble() * 2f * (w + h), off = near + (float)rng.NextDouble() * (far - near);
            if (along < w) return new Vector2(_d.fence[0] + along, _d.fence[1] + off);
            along -= w;
            if (along < w) return new Vector2(_d.fence[0] + along, _d.fence[3] - off);
            along -= w;
            if (along < h) return new Vector2(_d.fence[0] + off, _d.fence[1] + along);
            return new Vector2(_d.fence[2] - off, _d.fence[1] + along - h);
        }

        // ------------------------------------------------------------------ objects

        /// <summary>Yard lamps the package plan puts onto the new roads go to their data place (the yard step puts them back
        /// each run, so this always starts from the same place).</summary>
        static int MoveStructures(Transform structures)
        {
            int moved = 0;
            foreach (Move m in _d.moves)
            {
                var from = new Vector2(m.from[0], m.from[1]);
                Transform best = null;
                float bestD = 2f;
                foreach (Transform t in structures)
                {
                    if (t.name != m.m) continue;
                    float d = (Yard(t.position.x, t.position.z) - from).magnitude;
                    if (d < bestD) { bestD = d; best = t; }
                }
                var to = new Vector2(m.to[0], m.to[1]);
                if (best == null && structures.Cast<Transform>().Any(t => t.name == m.m && (Yard(t.position.x, t.position.z) - to).magnitude < 0.5f))
                    continue;                                   // already there (a run without the yard step before it)
                if (best == null) { Debug.LogWarning($"[Snow] Forest yard dress: no {m.m} near ({m.from[0]}, {m.from[1]}) to move."); continue; }
                Vector3 w = World(m.to[0], m.to[1]);
                best.position = new Vector3(w.x, Height(w), w.z);
                if (PrefabUtility.IsPartOfPrefabInstance(best)) PrefabUtility.RecordPrefabInstancePropertyModifications(best);
                moved++;
            }
            return moved;
        }

        static float Height(Vector3 w)
        {
            foreach (Terrain t in Terrain.activeTerrains)
            {
                Vector3 o = t.transform.position, s = t.terrainData.size;
                if (w.x >= o.x && w.z >= o.z && w.x <= o.x + s.x && w.z <= o.z + s.z) return o.y + t.SampleHeight(w);
            }
            return w.y;
        }

        /// <summary>A prefab instance on the ground at a yard point, turned by the yard's yaw plus its own; layer, static flags
        /// and the map's rendering layer taken from a reference structure of the same kind.</summary>
        static GameObject Place(GameObject asset, Transform parent, float u, float v, float yaw, Transform reference)
        {
            if (asset == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            Vector3 w = World(u, v);
            go.transform.SetPositionAndRotation(new Vector3(w.x, Height(w), w.z), Quaternion.Euler(0f, _d.yaw + yaw, 0f));
            int layer = reference != null ? reference.gameObject.layer : go.layer;
            StaticEditorFlags flags = reference != null ? GameObjectUtility.GetStaticEditorFlags(reference.gameObject) : 0;
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = layer;
                if (reference != null) GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
            }
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true)) r.renderingLayerMask = 2u;   // the map's lights
            return go;
        }

        /// <summary>The package's yard lamp (intensity 7, range 22 m) leaves no visible pool on the ground at the dark night's
        /// exposure: every yard LightPole (the package's and the fence lamps) gets the data's intensity and range.</summary>
        static int Brighten(Transform structures, Transform dress)
        {
            if (_d.lampIntensity <= 0f) return 0;
            int count = 0;
            var poles = structures.Cast<Transform>().Where(t => t.name == "LightPole").Concat(dress.Cast<Transform>().Where(t => t.name == "LightPole"));
            foreach (Transform pole in poles)
                foreach (Light l in pole.GetComponentsInChildren<Light>(true))
                {
                    if (!l.name.StartsWith("Light_power", StringComparison.Ordinal)) continue;
                    l.intensity = _d.lampIntensity;
                    if (_d.lampRange > 0f) l.range = _d.lampRange;
                    if (PrefabUtility.IsPartOfPrefabInstance(l)) PrefabUtility.RecordPrefabInstancePropertyModifications(l);
                    count++;
                }
            return count;
        }

        /// <summary>Turns a lamp so its arm (pole foot to the light) points along the yard direction <paramref name="face"/>.</summary>
        static void FaceArm(Transform pole, Vector2 face)
        {
            Light light = pole.GetComponentInChildren<Light>(true);
            if (light == null || face.sqrMagnitude < 1e-4f) return;
            Vector3 arm = light.transform.position - pole.position;
            arm.y = 0f;
            if (arm.sqrMagnitude < 1e-4f) return;
            Vector3 want = World(face.x, face.y) - World(0f, 0f);
            pole.rotation = Quaternion.Euler(0f, Vector3.SignedAngle(arm, want, Vector3.up), 0f) * pole.rotation;
        }

        static int PlaceClumps(Transform parent, Transform reference, List<Vector2> lamps, ref int missing)
        {
            var assets = _d.clumpPrefabs.Select(AssetDatabase.LoadAssetAtPath<GameObject>).Where(a => a != null).ToArray();
            missing += _d.clumpPrefabs.Length - assets.Length;
            if (assets.Length == 0) return 0;
            var rng = new System.Random(_d.trees.seed + 1);
            int count = 0;
            void One(Vector2 p)
            {
                GameObject go = Place(assets[rng.Next(assets.Length)], parent, p.x, p.y, (float)rng.NextDouble() * 360f, reference);
                float s = 0.8f + (float)rng.NextDouble() * 0.5f;
                go.transform.localScale = new Vector3(s, s, s);
                count++;
            }
            for (int i = 0, tries = 0; i < _d.fenceClumps && tries < 400; tries++)
            {
                Vector2 p = OnFenceBand(rng, 0.5f, 2.0f);
                if (BlockedBy(p, 1f, out _) || lamps.Any(l => (l - p).magnitude < 1f)) continue;
                Ground g = Sample(p, World(p.x, p.y));
                if (g.asphalt + g.gravel + g.path > 0.2f) continue;
                One(p);
                i++;
            }
            foreach (Spot c in _d.clumps)
                for (int i = 0; i < c.n; i++)
                {
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f, d = c.r * Mathf.Sqrt((float)rng.NextDouble());
                    One(new Vector2(c.u + Mathf.Cos(a) * d, c.v + Mathf.Sin(a) * d));
                }
            return count;
        }
    }
}
