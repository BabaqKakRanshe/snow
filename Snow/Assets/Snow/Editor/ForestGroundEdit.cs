using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Snow.EditorTools
{
    /// <summary>
    /// Ground edits of the Forest dress steps that put a pack into the ground (the forester's cave, the lake bunker): the
    /// terrain is lowered only inside the given rectangles (each in its own frame: centre, yaw, u across, v along), fading
    /// back to the ground over <c>fade</c> metres outside, and Terrain holes can be cut inside them. The ground before the
    /// first edit is kept per tile in a .bytes file next to the step's data, so every rerun starts from the same ground and
    /// gives the same result; the touched tiles are copied to the archive once a day before the edit, as
    /// <see cref="ForestStartDressStep"/> does. Trees and grass can be cleared inside the same rectangles.
    /// </summary>
    public static class ForestGroundEdit
    {
        const string BackupRoot = @"F:\ClaudeGames\snow_archive\backup";

        /// <summary>A rectangle in its frame. mode "cut": the ground goes down to <c>y</c> at most (it never rises); "raise":
        /// up to <c>y</c> at least (it never sinks: earth over a roof); "set": the ground takes <c>y</c>. hole: Terrain holes inside the rectangle (no fade).</summary>
        [Serializable]
        public class Rect
        {
            public string name;
            public float x, z, yaw, y, fade;
            public float[] rect;                       // u0, v0, u1, v1 (m, in the frame)
            public string mode = "cut";
            /// <summary>false: the rectangle only clears trees / grass or cuts holes, the heights stay.</summary>
            public bool heights = true;
            public bool hole, clearTrees = true, clearGrass = true;
        }

        static float Cos(Rect r) => Mathf.Cos(r.yaw * Mathf.Deg2Rad);
        static float Sin(Rect r) => Mathf.Sin(r.yaw * Mathf.Deg2Rad);

        /// <summary>World x, z -> the rectangle's frame (u along its +X, v along its +Z, Unity yaw).</summary>
        public static Vector2 Frame(Rect r, float x, float z)
        {
            float dx = x - r.x, dz = z - r.z, c = Cos(r), s = Sin(r);
            return new Vector2(dx * c - dz * s, dx * s + dz * c);
        }

        public static Vector3 World(Rect r, float u, float v)
        {
            float c = Cos(r), s = Sin(r);
            return new Vector3(r.x + u * c + v * s, 0f, r.z - u * s + v * c);
        }

        static float Inside(Rect r, Vector2 p) => Mathf.Min(Mathf.Min(p.x - r.rect[0], r.rect[2] - p.x), Mathf.Min(p.y - r.rect[1], r.rect[3] - p.y));

        static float Share(Rect r, Vector2 p)
        {
            float d = Inside(r, p);
            if (d >= 0f) return 1f;
            if (r.fade <= 0f) return 0f;
            return 1f - Mathf.SmoothStep(0f, 1f, -d / r.fade);
        }

        static bool InReach(Rect r, Vector2 p) => Inside(r, p) > -Mathf.Max(r.fade, 0f) - 0.01f;

        static bool Overlaps(Terrain t, Rect r)
        {
            Vector3 o = t.transform.position, s = t.terrainData.size;
            float g = Mathf.Max(r.fade, 0f) + 1f;
            foreach (float u in new[] { r.rect[0] - g, r.rect[2] + g })
                foreach (float v in new[] { r.rect[1] - g, r.rect[3] + g })
                {
                    Vector3 w = World(r, u, v);
                    if (w.x >= o.x - g && w.x <= o.x + s.x + g && w.z >= o.z - g && w.z <= o.z + s.z + g) return true;
                }
            return false;
        }

        public static Terrain[] Tiles(Transform root, IEnumerable<Rect> rects) =>
            Terrain.activeTerrains.Where(t => t != null && t.terrainData != null && t.transform.IsChildOf(root) && rects.Any(r => Overlaps(t, r))).ToArray();

        public static float Height(float x, float z)
        {
            foreach (Terrain t in Terrain.activeTerrains)
            {
                Vector3 o = t.transform.position, s = t.terrainData.size;
                if (x >= o.x && z >= o.z && x <= o.x + s.x && z <= o.z + s.z) return o.y + t.SampleHeight(new Vector3(x, 0f, z));
            }
            return 0f;
        }

        /// <summary>A copy of the tiles in the archive, once a day per tag, before the first edit.</summary>
        public static void Backup(Terrain[] tiles, string tag)
        {
            string dir = Path.Combine(BackupRoot, tag + "_" + DateTime.Now.ToString("yyyyMMdd"));
            if (Directory.Exists(dir)) return;
            Directory.CreateDirectory(dir);
            foreach (Terrain t in tiles)
            {
                string path = AssetDatabase.GetAssetPath(t.terrainData);
                if (!string.IsNullOrEmpty(path)) File.Copy(path, Path.Combine(dir, Path.GetFileName(path)), true);
            }
            Debug.Log($"[Snow] Ground edit ({tag}): terrain tiles copied to {dir} before the edit.");
        }

        // ------------------------------------------------------------------ the ground before the first edit

        class Store
        {
            public readonly Dictionary<string, (int x0, int z0, float[,] h)> heights = new Dictionary<string, (int, int, float[,])>(StringComparer.Ordinal);
            public readonly Dictionary<string, (int x0, int z0, bool[,] h)> holes = new Dictionary<string, (int, int, bool[,])>(StringComparer.Ordinal);
        }

        static Store Load(string path)
        {
            var s = new Store();
            if (!File.Exists(path)) return s;
            using (var r = new BinaryReader(File.OpenRead(path)))
            {
                for (int n = r.ReadInt32(), i = 0; i < n; i++)
                {
                    string name = r.ReadString();
                    int ax = r.ReadInt32(), az = r.ReadInt32(), aw = r.ReadInt32(), ah = r.ReadInt32();
                    var a = new float[ah, aw];
                    for (int z = 0; z < ah; z++) for (int x = 0; x < aw; x++) a[z, x] = r.ReadSingle();
                    s.heights[name] = (ax, az, a);
                }
                if (r.BaseStream.Position >= r.BaseStream.Length) return s;
                for (int n = r.ReadInt32(), i = 0; i < n; i++)
                {
                    string name = r.ReadString();
                    int ax = r.ReadInt32(), az = r.ReadInt32(), aw = r.ReadInt32(), ah = r.ReadInt32();
                    var a = new bool[ah, aw];
                    for (int z = 0; z < ah; z++) for (int x = 0; x < aw; x++) a[z, x] = r.ReadBoolean();
                    s.holes[name] = (ax, az, a);
                }
            }
            return s;
        }

        static void Save(string path, Store s)
        {
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write(s.heights.Count);
                foreach (var kv in s.heights)
                {
                    w.Write(kv.Key);
                    w.Write(kv.Value.x0); w.Write(kv.Value.z0); w.Write(kv.Value.h.GetLength(1)); w.Write(kv.Value.h.GetLength(0));
                    foreach (float f in kv.Value.h) w.Write(f);
                }
                w.Write(s.holes.Count);
                foreach (var kv in s.holes)
                {
                    w.Write(kv.Key);
                    w.Write(kv.Value.x0); w.Write(kv.Value.z0); w.Write(kv.Value.h.GetLength(1)); w.Write(kv.Value.h.GetLength(0));
                    foreach (bool b in kv.Value.h) w.Write(b);
                }
            }
        }

        /// <summary>The index box (x0, z0, x1, z1) of a grid of <paramref name="res"/> samples spaced <paramref name="step"/>
        /// that any rectangle reaches, or null.</summary>
        static int[] Box(Terrain t, Rect[] rects, int res, float step, float half)
        {
            Vector3 o = t.transform.position;
            int x0 = res, z0 = res, x1 = -1, z1 = -1;
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    float wx = o.x + (x + half) * step, wz = o.z + (z + half) * step;
                    if (!rects.Any(r => InReach(r, Frame(r, wx, wz)))) continue;
                    x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x);
                    z0 = Mathf.Min(z0, z); z1 = Mathf.Max(z1, z);
                }
            return x1 < 0 ? null : new[] { x0, z0, x1, z1 };
        }

        /// <summary>Lowers / sets the ground and cuts the holes of <paramref name="rects"/>; the ground of the reach before the
        /// first run is kept in <paramref name="storePath"/>. Returns (height samples changed, hole cells).</summary>
        public static (int heights, int holes) Apply(Transform root, Rect[] rects, string storePath, string backupTag)
        {
            if (rects == null || rects.Length == 0) return (0, 0);
            Terrain[] tiles = Tiles(root, rects);
            if (tiles.Length == 0) return (0, 0);
            Backup(tiles, backupTag);
            Store store = Load(storePath);
            bool storeChanged = false;
            int nh = 0, nholes = 0;
            Rect[] heightRects = rects.Where(r => r.heights).ToArray();
            foreach (Terrain t in tiles)
            {
                TerrainData td = t.terrainData;
                Vector3 o = t.transform.position;
                int res = td.heightmapResolution;
                float step = td.size.x / (res - 1);
                int[] b = heightRects.Length > 0 ? Box(t, heightRects, res, step, 0f) : null;
                if (b != null)
                {
                    int w = b[2] - b[0] + 1, h = b[3] - b[1] + 1;
                    float[,] orig;
                    if (store.heights.TryGetValue(td.name, out var kept) && kept.x0 == b[0] && kept.z0 == b[1] && kept.h.GetLength(1) == w && kept.h.GetLength(0) == h)
                        orig = kept.h;
                    else
                    {
                        if (store.heights.ContainsKey(td.name))
                            Debug.LogWarning($"[Snow] Ground edit ({backupTag}): the stored ground of {td.name} has another extent (the rectangles changed); measuring again from the current ground.");
                        orig = td.GetHeights(b[0], b[1], w, h);
                        store.heights[td.name] = (b[0], b[1], orig);
                        storeChanged = true;
                    }
                    var hs = new float[h, w];
                    for (int z = 0; z < h; z++)
                        for (int x = 0; x < w; x++)
                        {
                            float wx = o.x + (b[0] + x) * step, wz = o.z + (b[1] + z) * step;
                            float g = orig[z, x] * td.size.y + o.y, v = g;
                            foreach (Rect r in heightRects)
                            {
                                Vector2 p = Frame(r, wx, wz);
                                if (!InReach(r, p)) continue;
                                float k = Share(r, p);
                                if (r.mode == "raise") { v = Mathf.Max(v, Mathf.Lerp(g, Mathf.Max(g, r.y), k)); continue; }
                                float target = r.mode == "set" ? r.y : Mathf.Min(g, r.y);
                                float nv = Mathf.Lerp(g, target, k);
                                v = r.mode == "set" ? (k > 0f ? nv : v) : Mathf.Min(v, nv);
                            }
                            hs[z, x] = (v - o.y) / td.size.y;
                            if (Mathf.Abs(v - g) > 1e-4f) nh++;
                        }
                    td.SetHeights(b[0], b[1], hs);
                    // the terrain trees standing on the changed ground follow it
                    TreeInstance[] trees = td.treeInstances;
                    bool moved = false;
                    for (int i = 0; i < trees.Length; i++)
                    {
                        float wx = o.x + trees[i].position.x * td.size.x, wz = o.z + trees[i].position.z * td.size.z;
                        if (!heightRects.Any(r => InReach(r, Frame(r, wx, wz)))) continue;
                        float ny = td.GetInterpolatedHeight(trees[i].position.x, trees[i].position.z) / td.size.y;
                        if (Mathf.Abs(ny - trees[i].position.y) < 1e-6f) continue;
                        trees[i].position = new Vector3(trees[i].position.x, ny, trees[i].position.z);
                        moved = true;
                    }
                    if (moved) td.SetTreeInstances(trees, false);
                }
                // holes: one cell per heightmap quad, inside the hole rectangles (no fade)
                Rect[] holeRects = rects.Where(r => r.hole).ToArray();
                if (holeRects.Length > 0)
                {
                    int hr = td.holesResolution;
                    float hstep = td.size.x / hr;
                    int[] hb = Box(t, holeRects.Select(r => new Rect { x = r.x, z = r.z, yaw = r.yaw, rect = r.rect, fade = 0f }).ToArray(), hr, hstep, 0.5f);
                    if (hb != null)
                    {
                        int w = hb[2] - hb[0] + 1, h = hb[3] - hb[1] + 1;
                        bool[,] orig;
                        if (store.holes.TryGetValue(td.name, out var kh) && kh.x0 == hb[0] && kh.z0 == hb[1] && kh.h.GetLength(1) == w && kh.h.GetLength(0) == h)
                            orig = kh.h;
                        else
                        {
                            orig = td.GetHoles(hb[0], hb[1], w, h);
                            store.holes[td.name] = (hb[0], hb[1], orig);
                            storeChanged = true;
                        }
                        var hm = (bool[,])orig.Clone();                 // true = solid ground
                        for (int z = 0; z < h; z++)
                            for (int x = 0; x < w; x++)
                            {
                                float wx = o.x + (hb[0] + x + 0.5f) * hstep, wz = o.z + (hb[1] + z + 0.5f) * hstep;
                                if (holeRects.Any(r => Inside(r, Frame(r, wx, wz)) >= 0f)) { hm[z, x] = false; nholes++; }
                            }
                        td.SetHoles(hb[0], hb[1], hm);
                    }
                }
                EditorUtility.SetDirty(td);
            }
            if (storeChanged) Save(storePath, store);
            return (nh, nholes);
        }

        /// <summary>Puts the ground of the archive's tile copies (<see cref="Backup"/>: the ground before the first edit of that
        /// day) into the store for the current rectangles, e.g. after the rectangles grew and the store was measured again
        /// from edited ground. The copies are imported for the read under Assets/_GroundRestore and deleted afterwards.</summary>
        public static int RestoreStore(Transform root, Rect[] rects, string storePath, string backupDir)
        {
            Rect[] heightRects = rects.Where(r => r.heights).ToArray();
            Store store = Load(storePath);
            const string tmp = "Assets/_GroundRestore";
            int restored = 0;
            try
            {
                foreach (Terrain t in Tiles(root, heightRects))
                {
                    TerrainData td = t.terrainData;
                    string src = Path.Combine(backupDir, Path.GetFileName(AssetDatabase.GetAssetPath(td)));
                    if (!File.Exists(src)) { Debug.LogWarning($"[Snow] Ground restore: no copy of {td.name} in {backupDir}."); continue; }
                    Directory.CreateDirectory(tmp);
                    string copy = $"{tmp}/{td.name}_copy.asset";
                    File.Copy(src, copy, true);
                    AssetDatabase.ImportAsset(copy);
                    var old = AssetDatabase.LoadAssetAtPath<TerrainData>(copy);
                    if (old == null) { Debug.LogWarning($"[Snow] Ground restore: {copy} did not load."); continue; }
                    int res = td.heightmapResolution;
                    int[] b = heightRects.Length > 0 ? Box(t, heightRects, res, td.size.x / (res - 1), 0f) : null;
                    if (b != null) store.heights[td.name] = (b[0], b[1], old.GetHeights(b[0], b[1], b[2] - b[0] + 1, b[3] - b[1] + 1));
                    // the holes as they were, for the current hole rectangles; the cells of the copy's box outside them
                    // are written back to the tile at once (rectangles that shrank)
                    Rect[] holeRects = rects.Where(r => r.hole).Select(r => new Rect { x = r.x, z = r.z, yaw = r.yaw, rect = r.rect, fade = 0f }).ToArray();
                    int hr = td.holesResolution;
                    int[] hb = holeRects.Length > 0 ? Box(t, holeRects, hr, td.size.x / hr, 0.5f) : null;
                    if (store.holes.TryGetValue(td.name, out var was))
                        td.SetHoles(was.x0, was.z0, old.GetHoles(was.x0, was.z0, was.h.GetLength(1), was.h.GetLength(0)));
                    if (hb != null) store.holes[td.name] = (hb[0], hb[1], old.GetHoles(hb[0], hb[1], hb[2] - hb[0] + 1, hb[3] - hb[1] + 1));
                    EditorUtility.SetDirty(td);
                    restored++;
                }
            }
            finally { AssetDatabase.DeleteAsset(tmp); }
            Save(storePath, store);
            Debug.Log($"[Snow] Ground restore: {restored} tile(s) of {storePath} taken from {backupDir}.");
            return restored;
        }

        /// <summary>Terrain trees inside the rectangles (grown by <paramref name="margin"/>) go; fence posts stay.</summary>
        public static int ClearTrees(Transform root, Rect[] rects, float margin)
        {
            rects = rects?.Where(r => r.clearTrees).ToArray();
            if (rects == null || rects.Length == 0) return 0;
            int removed = 0;
            foreach (Terrain t in Tiles(root, rects))
            {
                TerrainData td = t.terrainData;
                TreePrototype[] protos = td.treePrototypes;
                Vector3 o = t.transform.position, s = td.size;
                var keep = new List<TreeInstance>(td.treeInstanceCount);
                foreach (TreeInstance ti in td.treeInstances)
                {
                    string nm = protos[ti.prototypeIndex].prefab != null ? protos[ti.prototypeIndex].prefab.name : "";
                    bool fence = nm.StartsWith("OldFence", StringComparison.Ordinal) || nm.StartsWith("EuroPost", StringComparison.Ordinal) || nm.StartsWith("FenceChain", StringComparison.Ordinal);
                    float wx = o.x + ti.position.x * s.x, wz = o.z + ti.position.z * s.z;
                    if (!fence && rects.Any(r => Inside(r, Frame(r, wx, wz)) > -margin)) { removed++; continue; }
                    keep.Add(ti);
                }
                if (keep.Count != td.treeInstanceCount) td.SetTreeInstances(keep.ToArray(), false);
                EditorUtility.SetDirty(td);
            }
            return removed;
        }

        /// <summary>Grass and twigs inside the rectangles go.</summary>
        public static int ClearGrass(Transform root, Rect[] rects)
        {
            rects = rects?.Where(r => r.clearGrass).ToArray();
            if (rects == null || rects.Length == 0) return 0;
            int cells = 0;
            foreach (Terrain t in Tiles(root, rects))
            {
                TerrainData td = t.terrainData;
                int n = td.detailResolution, layers = td.detailPrototypes.Length;
                float cell = td.size.x / n;
                Vector3 o = t.transform.position;
                int[] b = Box(t, rects.Select(r => new Rect { x = r.x, z = r.z, yaw = r.yaw, rect = r.rect, fade = 0f }).ToArray(), n, cell, 0.5f);
                if (b == null) continue;
                int w = b[2] - b[0] + 1, h = b[3] - b[1] + 1;
                for (int l = 0; l < layers; l++)
                {
                    int[,] map = td.GetDetailLayer(b[0], b[1], w, h, l);
                    for (int z = 0; z < h; z++)
                        for (int x = 0; x < w; x++)
                        {
                            float wx = o.x + (b[0] + x + 0.5f) * cell, wz = o.z + (b[1] + z + 0.5f) * cell;
                            if (map[z, x] == 0 || !rects.Any(r => Inside(r, Frame(r, wx, wz)) >= 0f)) continue;
                            map[z, x] = 0;
                            cells++;
                        }
                    td.SetDetailLayer(b[0], b[1], l, map);
                }
                EditorUtility.SetDirty(td);
            }
            return cells;
        }
    }
}
