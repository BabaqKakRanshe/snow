using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Snow.World;
using UnityEditor;
using UnityEngine;

namespace Snow.EditorTools
{
    /// <summary>
    /// Tools / Snow / World Sync / Import Ground From Blender: a ground edit made in a map .blend
    /// (tools/blender_context/export_ground.py → ground_delta.f32 + .json) goes onto the open scene's Terrain tiles
    /// (docs/gdd/WORLD_SYNC_CONTRACT.md):
    ///  - first a check that the tiles still have the heights the edit was made on (base ground.r16 of the export), then a
    ///    copy of the touched tiles to snow_archive/backup/forest_ground_import_&lt;date&gt;;
    ///  - heights + delta; Terrain trees on changed ground follow it up or down;
    ///  - new ground (changed by <see cref="RefillFrom"/> m or more, faded over <see cref="RefillFade"/> m): its old trees,
    ///    paint and grass give way to forest like the forest around it — kinds, sizes and density of the Terrain trees within
    ///    <see cref="Around"/> m outside the new ground, planted on a jittered grid (not copied: a copy carries the donor's
    ///    straight strips along), its mean paint and grass; not under structures, not on slopes over 30°;
    ///  - placed objects of the scene's pack groups on changed ground move with it (the integration puts them on the
    ///    ground again the same way); structures move only by their hand moves (Import Moves From Blender);
    ///  - the tiles minus the bake become the hand sculpt layer (Forest_HandSculpt.bytes), so a re-bake keeps the edit.
    /// </summary>
    public static class WorldGroundImport
    {
        const string BackupRoot = @"F:\ClaudeGames\snow_archive\backup";
        const float RefillFade = 2f, Around = 150f, MaxSlope = 30f, StructureMargin = 4f;
        static float _refillFrom = 1f;                                  // this import's refillFrom (NearNew)
        static readonly string[] FollowGroups = { "ForestRocks", "ForestTrash", "ForestWorld", "ForestParts", "ForestTors" };

        [Serializable]
        class Info
        {
            public int nx, nz, changed;
            public float x0, z0, step, hmin, hmax;
            public string file, @base, blend;
            public float[] box;
        }

        [MenuItem("Tools/Snow/World Sync/Import Ground From Blender...")]
        static void Menu()
        {
            string path = EditorUtility.OpenFilePanel("ground_delta.json (export_ground.py)", Path.GetFullPath("../source/world_sync"), "json");
            if (string.IsNullOrEmpty(path)) return;
            if (!EditorUtility.DisplayDialog("Import ground", $"Put the ground edit {path} on the Forest Terrain tiles?\nThe touched tiles are copied to {BackupRoot} first.", "Import", "Cancel")) return;
            Debug.Log(Import(path, true));
        }

        public static string Import(string infoPath, bool captureHand) => Import(infoPath, captureHand, 1f, 0f);

        /// <summary><paramref name="refillFrom"/>: metres of change from which the ground gets new forest (a whole-map relief:
        /// float.MaxValue, the trees only follow); <paramref name="rockSlope"/>: degrees from which changed ground is painted rock
        /// (0 = no rock paint).</summary>
        public static string Import(string infoPath, bool captureHand, float refillFrom, float rockSlope)
        {
            float RefillFrom = refillFrom;
            _refillFrom = refillFrom;
            Info info = JsonUtility.FromJson<Info>(File.ReadAllText(infoPath));
            float[] delta = ReadFloats(Path.Combine(Path.GetDirectoryName(infoPath), info.file), info.nx * info.nz);
            Terrain[] ts = Terrain.activeTerrains.Where(t => t != null && t.terrainData != null).ToArray();
            if (ts.Length == 0) return "[Snow] Import ground: no Terrain in the open scene.";
            float gx0 = ts.Min(t => t.transform.position.x), gz0 = ts.Min(t => t.transform.position.z);
            float step = ts[0].terrainData.size.x / (ts[0].terrainData.heightmapResolution - 1);
            if (Mathf.Abs(gx0 - info.x0) > 0.01f || Mathf.Abs(gz0 - info.z0) > 0.01f || Mathf.Abs(step - info.step) > 1e-4f)
                return $"[Snow] Import ground: the edit's grid ({info.x0}, {info.z0}, {info.step}) is not the tiles' ({gx0}, {gz0}, {step}).";

            float D(int gx, int gz) => gx < 0 || gz < 0 || gx >= info.nx || gz >= info.nz ? 0f : delta[gz * info.nx + gx];
            float DAt(float x, float z) => D(Mathf.RoundToInt((x - info.x0) / info.step), Mathf.RoundToInt((z - info.z0) / info.step));
            float Refill(float x, float z) => RefillFrom >= 1e6f ? 0f : Smooth(RefillFrom, RefillFrom + RefillFade, Mathf.Abs(DAt(x, z)));
            var touched = ts.Where(t => Overlaps(t, info.box)).ToArray();

            // 1. the tiles must still be the ground the edit was made on
            string check = CheckBase(info, delta, touched);
            if (check != null) return check;

            // 2. backup
            string backup = Path.Combine(BackupRoot, "forest_ground_import_" + DateTime.Now.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(backup);
            foreach (Terrain t in touched)
            {
                string p = AssetDatabase.GetAssetPath(t.terrainData);
                if (!string.IsNullOrEmpty(p)) File.Copy(Path.GetFullPath(p), Path.Combine(backup, Path.GetFileName(p)), true);
            }

            // 3. heights
            int samples = 0, clamped = 0;
            foreach (Terrain t in touched)
            {
                TerrainData td = t.terrainData;
                int r = td.heightmapResolution;
                int ox = Mathf.RoundToInt((t.transform.position.x - info.x0) / info.step), oz = Mathf.RoundToInt((t.transform.position.z - info.z0) / info.step);
                float[,] hs = td.GetHeights(0, 0, r, r);
                for (int z = 0; z < r; z++)
                    for (int x = 0; x < r; x++)
                    {
                        float d = D(ox + x, oz + z);
                        if (Mathf.Abs(d) < 0.005f) continue;
                        float v = hs[z, x] + d / td.size.y;
                        if (v < 0f || v > 1f) clamped++;
                        hs[z, x] = Mathf.Clamp01(v);
                        samples++;
                    }
                td.SetHeights(0, 0, hs);
                EditorUtility.SetDirty(td);
            }

            // 4. what stands on it: structures keep their ground free, pack objects follow
            var keepOut = new List<Bounds>();
            Transform root = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == "Forest")?.transform;
            int followed = 0;
            if (root != null)
            {
                foreach (WorldId w in root.GetComponentsInChildren<WorldId>(true))
                {
                    Vector3 p = w.transform.position;
                    if (!Inside(info.box, p.x, p.z, Around)) continue;
                    if (FollowGroups.Contains(w.transform.parent != null ? w.transform.parent.name : ""))
                    {
                        float d = DAt(p.x, p.z);
                        if (Mathf.Abs(d) >= 0.005f)
                        {
                            // their built pose moves with the ground too: following it is not a hand move
                            w.transform.position = p + Vector3.up * d;
                            if (w.placed) { w.placedPosition += Vector3.up * d; EditorUtility.SetDirty(w); }
                            followed++;
                        }
                    }
                    Renderer[] rs = w.GetComponentsInChildren<Renderer>();
                    if (rs.Length == 0) continue;
                    Bounds b = rs[0].bounds;
                    foreach (Renderer x in rs) b.Encapsulate(x.bounds);
                    b.Expand(StructureMargin * 2f);
                    keepOut.Add(b);
                }
            }

            // 5. trees: follow the ground, or give way to new forest
            var protos = new List<(GameObject prefab, float w, float h, Color color)>();
            float nearArea = 0f;
            var rng = new System.Random(20261027);
            int moved = 0, removed = 0;
            foreach (Terrain t in ts)
            {
                TerrainData td = t.terrainData;
                Vector3 o = t.transform.position, s = td.size;
                var keep = new List<TreeInstance>(td.treeInstanceCount);
                bool tileTouched = touched.Contains(t);
                foreach (TreeInstance ti in td.treeInstances)
                {
                    Vector3 w = o + Vector3.Scale(ti.position, s);
                    if (Inside(info.box, w.x, w.z, Around) && Refill(w.x, w.z) < 0.01f && Mathf.Abs(DAt(w.x, w.z)) < 0.005f && NearNew(w.x, w.z, DAt))
                        protos.Add((td.treePrototypes[ti.prototypeIndex].prefab, ti.widthScale, ti.heightScale, ti.color));
                    if (!tileTouched) { keep.Add(ti); continue; }
                    float k = Refill(w.x, w.z);
                    if (k >= 0.5f) { removed++; continue; }
                    if (Mathf.Abs(DAt(w.x, w.z)) >= 0.005f)
                    {
                        TreeInstance n = ti;
                        n.position = new Vector3(ti.position.x, t.SampleHeight(w) / s.y, ti.position.z);
                        keep.Add(n);
                        moved++;
                    }
                    else keep.Add(ti);
                }
                if (tileTouched) td.SetTreeInstances(keep.ToArray(), false);
            }
            // the reference ring: samples within Around m of new ground that stayed as they were
            nearArea = RingArea(info, delta, DAt);
            int planted = Plant(ts, info, Refill, keepOut, protos, nearArea, rng);

            // 6. paint and grass of new ground: the mean of the ring
            int texels = RefillFrom < 1e6f ? Paint(ts, touched, info, Refill, DAt) : 0;
            int cells = RefillFrom < 1e6f ? Grass(ts, touched, info, Refill, DAt, rng) : 0;
            int rock = rockSlope > 0f ? Rock(touched, DAt, rockSlope) : 0;

            // 7. the mist cards of the package lie at their height over the ground: they follow it
            int mist = 0;
            Transform mistRoot = root != null ? root.Find("World/Forest_Mist") : null;
            if (mistRoot != null)
                foreach (Renderer r in mistRoot.GetComponentsInChildren<Renderer>(true))
                {
                    Vector3 c = r.bounds.center;
                    float d = DAt(c.x, c.z);
                    if (Mathf.Abs(d) < 0.05f) continue;
                    r.transform.position += Vector3.up * d;
                    mist++;
                }

            foreach (Terrain t in touched)
            {
                t.terrainData.SyncHeightmap();
                t.Flush();
                EditorUtility.SetDirty(t.terrainData);
            }
            AssetDatabase.SaveAssets();
            bool hand = captureHand && ForestTerrainBaker.CaptureHandSculpt();
            return $"[Snow] Import ground {infoPath}: {samples} height samples on {touched.Length} tiles ({clamped} clamped), {moved} trees followed the ground, " +
                   $"{removed} gave way, {planted} planted from {protos.Count} trees around ({protos.Select(p => p.prefab).Distinct().Count()} kinds), {texels} paint texels, " +
                   $"{cells} grass cells, {rock} rock texels on steep new slopes, {mist} mist cards followed, {followed} pack objects followed, {keepOut.Count} structures kept free; backup {backup}; hand sculpt {(hand ? "captured" : "not captured")}.";
        }

        // ------------------------------------------------------------------ checks

        static string CheckBase(Info info, float[] delta, Terrain[] touched)
        {
            if (string.IsNullOrEmpty(info.@base) || !File.Exists(info.@base)) return null;
            byte[] raw = File.ReadAllBytes(info.@base);
            if (raw.Length != info.nx * info.nz * 2) return null;
            int tested = 0, off = 0;
            for (int i = 0; i < delta.Length; i += 97)
            {
                if (Mathf.Abs(delta[i]) < 0.005f) continue;
                int gx = i % info.nx, gz = i / info.nx;
                float x = info.x0 + gx * info.step, z = info.z0 + gz * info.step;
                Terrain t = touched.FirstOrDefault(tt => Inside(tt, x, z));
                if (t == null) continue;
                float baseH = info.hmin + (raw[i * 2] | (raw[i * 2 + 1] << 8)) / 65535f * (info.hmax - info.hmin);
                float nowH = t.transform.position.y + t.terrainData.GetHeight(
                    Mathf.RoundToInt((x - t.transform.position.x) / info.step), Mathf.RoundToInt((z - t.transform.position.z) / info.step));
                tested++;
                if (Mathf.Abs(nowH - baseH) > 0.05f) off++;
            }
            return tested > 0 && off > tested / 100
                ? $"[Snow] Import ground: {off} of {tested} tested samples differ from the ground the edit was made on ({info.@base}) - the tiles changed since that export. Export the whole map again and redo the edit."
                : null;
        }

        // ------------------------------------------------------------------ new forest

        /// <summary>m² of ground within <see cref="Around"/> m of new ground that kept its height: the trees counted there
        /// set the density.</summary>
        static float RingArea(Info info, float[] delta, Func<float, float, float> dAt)
        {
            float cell = info.step * 4f, area = 0f;
            for (float z = info.box[1] - Around; z <= info.box[3] + Around; z += cell)
                for (float x = info.box[0] - Around; x <= info.box[2] + Around; x += cell)
                    if (Mathf.Abs(dAt(x, z)) < 0.005f && NearNew(x, z, dAt)) area += cell * cell;
            return area;
        }

        static bool NearNew(float x, float z, Func<float, float, float> dAt)
        {
            for (int a = 0; a < 8; a++)
            {
                float r = Around * 0.5f, ang = a * Mathf.PI / 4f;
                if (Mathf.Abs(dAt(x + Mathf.Cos(ang) * r, z + Mathf.Sin(ang) * r)) >= _refillFrom) return true;
                if (Mathf.Abs(dAt(x + Mathf.Cos(ang) * Around, z + Mathf.Sin(ang) * Around)) >= _refillFrom) return true;
            }
            return false;
        }

        static int Plant(Terrain[] ts, Info info, Func<float, float, float> refill, List<Bounds> keepOut,
                         List<(GameObject prefab, float w, float h, Color color)> protos, float area, System.Random rng)
        {
            if (protos.Count == 0 || area <= 0f) return 0;
            float density = protos.Count / area;                       // trees per m²
            float cell = 1f / Mathf.Sqrt(density);
            var add = new Dictionary<Terrain, List<TreeInstance>>();
            int planted = 0;
            for (float z = info.box[1]; z <= info.box[3]; z += cell)
                for (float x = info.box[0]; x <= info.box[2]; x += cell)
                {
                    float px = x + (float)rng.NextDouble() * cell, pz = z + (float)rng.NextDouble() * cell;
                    var pick = protos[rng.Next(protos.Count)];
                    if (rng.NextDouble() > refill(px, pz)) continue;       // the fade: fewer new trees at the edge
                    Terrain t = ts.FirstOrDefault(tt => Inside(tt, px, pz));
                    if (t == null) continue;
                    Vector3 w = new Vector3(px, 0f, pz);
                    if (keepOut.Any(b => b.Contains(new Vector3(px, b.center.y, pz)))) continue;
                    TerrainData td = t.terrainData;
                    Vector3 o = t.transform.position, s = td.size;
                    Vector2 uv = new Vector2((px - o.x) / s.x, (pz - o.z) / s.z);
                    if (td.GetSteepness(uv.x, uv.y) > MaxSlope) continue;
                    int proto = Array.FindIndex(td.treePrototypes, p => p.prefab == pick.prefab);
                    if (proto < 0) continue;
                    if (!add.TryGetValue(t, out List<TreeInstance> list)) add[t] = list = new List<TreeInstance>();
                    list.Add(new TreeInstance
                    {
                        prototypeIndex = proto, position = new Vector3(uv.x, t.SampleHeight(w) / s.y, uv.y),
                        widthScale = pick.w, heightScale = pick.h, rotation = (float)(rng.NextDouble() * Math.PI * 2.0),
                        color = pick.color, lightmapColor = Color.white,
                    });
                    planted++;
                }
            foreach (var kv in add)
                kv.Key.terrainData.SetTreeInstances(kv.Key.terrainData.treeInstances.Concat(kv.Value).ToArray(), false);
            return planted;
        }

        /// <summary>Alphamap texels of new ground blend to the mean layer weights of the ring around it.</summary>
        static int Paint(Terrain[] ts, Terrain[] touched, Info info, Func<float, float, float> refill, Func<float, float, float> dAt)
        {
            int changed = 0;
            foreach (Terrain t in touched)
            {
                TerrainData td = t.terrainData;
                int w = td.alphamapWidth, h = td.alphamapHeight, L = td.alphamapLayers;
                float[,,] map = td.GetAlphamaps(0, 0, w, h);
                Vector3 o = t.transform.position, s = td.size;
                var mean = new double[L];
                double n = 0;
                for (int z = 0; z < h; z += 2)
                    for (int x = 0; x < w; x += 2)
                    {
                        float wx = o.x + (x + 0.5f) / w * s.x, wz = o.z + (z + 0.5f) / h * s.z;
                        if (Mathf.Abs(dAt(wx, wz)) >= 0.005f || !NearNew(wx, wz, dAt)) continue;
                        for (int l = 0; l < L; l++) mean[l] += map[z, x, l];
                        n++;
                    }
                if (n == 0) continue;
                for (int z = 0; z < h; z++)
                    for (int x = 0; x < w; x++)
                    {
                        float wx = o.x + (x + 0.5f) / w * s.x, wz = o.z + (z + 0.5f) / h * s.z;
                        float k = refill(wx, wz);
                        if (k <= 0f) continue;
                        for (int l = 0; l < L; l++) map[z, x, l] = Mathf.Lerp(map[z, x, l], (float)(mean[l] / n), k);
                        changed++;
                    }
                td.SetAlphamaps(0, 0, map);
            }
            return changed;
        }

        /// <summary>Grass cells of new ground: the ring's mean density per layer, scattered.</summary>
        static int Grass(Terrain[] ts, Terrain[] touched, Info info, Func<float, float, float> refill, Func<float, float, float> dAt, System.Random rng)
        {
            int changed = 0;
            foreach (Terrain t in touched)
            {
                TerrainData td = t.terrainData;
                int res = td.detailResolution;
                Vector3 o = t.transform.position, s = td.size;
                for (int l = 0; l < td.detailPrototypes.Length; l++)
                {
                    int[,] d = td.GetDetailLayer(0, 0, res, res, l);
                    double sum = 0, n = 0;
                    for (int z = 0; z < res; z += 2)
                        for (int x = 0; x < res; x += 2)
                        {
                            float wx = o.x + (x + 0.5f) / res * s.x, wz = o.z + (z + 0.5f) / res * s.z;
                            if (Mathf.Abs(dAt(wx, wz)) >= 0.005f || !NearNew(wx, wz, dAt)) continue;
                            sum += d[z, x];
                            n++;
                        }
                    double mean = n > 0 ? sum / n : 0;
                    for (int z = 0; z < res; z++)
                        for (int x = 0; x < res; x++)
                        {
                            float wx = o.x + (x + 0.5f) / res * s.x, wz = o.z + (z + 0.5f) / res * s.z;
                            float k = refill(wx, wz);
                            if (k <= 0f) continue;
                            int v = (int)Math.Round(mean * 2.0 * rng.NextDouble());
                            d[z, x] = rng.NextDouble() < k ? v : d[z, x];
                            changed++;
                        }
                    td.SetDetailLayer(0, 0, l, d);
                }
            }
            return changed;
        }

        /// <summary>Changed ground steeper than <paramref name="from"/> deg (blended over 8 deg) is painted TL_Rock, as the bake
        /// paints its own steep ground.</summary>
        static int Rock(Terrain[] touched, Func<float, float, float> dAt, float from)
        {
            int n = 0;
            foreach (Terrain t in touched)
            {
                TerrainData td = t.terrainData;
                int rock = Array.FindIndex(td.terrainLayers, l => l != null && l.name == "TL_Rock");
                if (rock < 0) continue;
                int w = td.alphamapWidth, h = td.alphamapHeight, L = td.alphamapLayers;
                float[,,] map = td.GetAlphamaps(0, 0, w, h);
                Vector3 o = t.transform.position, s = td.size;
                for (int z = 0; z < h; z++)
                    for (int x = 0; x < w; x++)
                    {
                        float u = (x + 0.5f) / w, v = (z + 0.5f) / h;
                        if (Mathf.Abs(dAt(o.x + u * s.x, o.z + v * s.z)) < 0.05f) continue;
                        float k = Smooth(from - 4f, from + 4f, td.GetSteepness(u, v));
                        if (k <= 0f) continue;
                        for (int l = 0; l < L; l++) map[z, x, l] = Mathf.Lerp(map[z, x, l], l == rock ? 1f : 0f, k);
                        n++;
                    }
                td.SetAlphamaps(0, 0, map);
            }
            return n;
        }

        // ------------------------------------------------------------------ helpers

        static float[] ReadFloats(string path, int count)
        {
            byte[] b = File.ReadAllBytes(path);
            if (b.Length != count * 4) throw new InvalidDataException($"{path}: {b.Length} bytes, {count * 4} expected");
            var f = new float[count];
            Buffer.BlockCopy(b, 0, f, 0, b.Length);
            return f;
        }

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        static bool Inside(Terrain t, float x, float z)
        {
            Vector3 o = t.transform.position, s = t.terrainData.size;
            return x >= o.x && z >= o.z && x <= o.x + s.x && z <= o.z + s.z;
        }

        static bool Inside(float[] box, float x, float z, float margin) =>
            box != null && x >= box[0] - margin && x <= box[2] + margin && z >= box[1] - margin && z <= box[3] + margin;

        static bool Overlaps(Terrain t, float[] box)
        {
            if (box == null) return false;
            Vector3 o = t.transform.position, s = t.terrainData.size;
            return !(box[2] < o.x || box[0] > o.x + s.x || box[3] < o.z || box[1] > o.z + s.z);
        }
    }
}
