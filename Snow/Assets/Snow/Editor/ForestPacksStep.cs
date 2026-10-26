using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Snow.Levels.Forest;
using UnityEditor;
using UnityEngine;
using Random = System.Random;

namespace Snow.EditorTools
{
    /// <summary>
    /// Forest integration of the packs made after forest_start (START_IMPORT_PACKS_1026.txt; prefabs: <see cref="PackPropsBuilder"/>):
    ///  - into the far points' prefabs (the core rebuilds them, so this runs in every integration): the tape recorder in each
    ///    antenna's cabinet, the louvred screen on the weather station, the ice in the relay mast, the dishes and the main
    ///    building's roof mast (switched off: the blizzard turns it on);
    ///  - the dosing pump in the pump house, spawned by <see cref="ForestItems"/> as a carried thing (its spots);
    ///  - the groups of <see cref="Groups"/>, each its own root under the Forest root, from Assets/Snow/Levels/Forest/&lt;group&gt;.json:
    ///    the rocks scattered over the forest (Tools / Snow / Forest / Generate Rock Scatter writes that list once), the base's
    ///    rubbish heaps, the world's cars, fire barrels and the forester's cave (with its ground cut), the lake sensor.
    /// Props stand on the ground at world x, z (or on a scene anchor); the only edits outside the group roots are the ground
    /// rectangles of a group (<see cref="ForestGroundEdit"/>) and the trees / grass under props that ask for it. A second run
    /// gives the same result. Runs after <see cref="ForestHutDressStep"/>; Tools / Snow / Forest / Packs Dress runs it alone.
    /// </summary>
    public static class ForestPacksStep
    {
        public static readonly string[] Groups = { "ForestRocks", "ForestTrash", "ForestWorld", "ForestParts" };
        const string DataFolder = "Assets/Snow/Levels/Forest/";
        const string HostFolder = "Assets/Locations/Forest/Prefabs/";

        /// <summary>A prefab at world x, z (or on <c>anchor</c>, a path under the Forest root, turned with it), turned by yaw.
        /// ground: "origin" (the prefab's origin on the ground), "lowest" (on the lowest ground under its footprint: the
        /// rocks' foot goes into the slope), "fixed" (at <c>y</c>: the cave, whose group cuts the ground under it). align: tilted to the ground under the footprint (cars, barrels). clear: m around
        /// it where terrain trees and grass go.</summary>
        [Serializable]
        public class Prop
        {
            public string prefab, anchor, ground = "origin";
            public float x, y, z, yaw, dy, clear;
            public float scale = 1f;
            public bool align;
        }

        [Serializable]
        public class Group
        {
            public string note;
            public bool staticProps;
            public Prop[] props;
            public ForestGroundEdit.Rect[] ground;
        }

        // ------------------------------------------------------------------ menus

        [MenuItem("Tools/Snow/Forest/Packs Dress")]
        static void RunMenu()
        {
            Transform root = GameObject.Find("Forest")?.transform;
            if (root == null) { Debug.LogError("[Snow] Forest packs: no Forest root in the open scene."); return; }
            Run(root);
        }

        [MenuItem("Tools/Snow/Forest/Packs Into Far Prefabs")]
        static void AttachMenu() => Attach();

        public static void Run(Transform root)
        {
            Attach();
            string spots = Spots(root);
            var parts = new List<string>();
            foreach (string g in Groups) parts.Add(PlaceGroup(root, g));
            Debug.Log($"[Snow] Forest packs: {spots}; " + string.Join("; ", parts.Where(p => p != null)) + ".");
        }

        // ------------------------------------------------------------------ into the far prefabs

        public static int Attach()
        {
            var byHost = new Dictionary<string, List<PackPropsBuilder.Attach>>(StringComparer.Ordinal);
            var source = new Dictionary<PackPropsBuilder.Attach, string>();
            foreach (string f in PackPropsBuilder.DataFiles)
            {
                PackPropsBuilder.Data d = PackPropsBuilder.Read(f);
                if (d?.attach == null) continue;
                foreach (PackPropsBuilder.Attach a in d.attach)
                {
                    if (a.spawn) continue;
                    if (!byHost.TryGetValue(a.host, out var list)) byHost[a.host] = list = new List<PackPropsBuilder.Attach>();
                    list.Add(a);
                    source[a] = d.root;
                }
            }
            int placed = 0;
            foreach (var kv in byHost)
            {
                string hostPath = HostFolder + kv.Key + ".prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(hostPath) == null) { Debug.LogWarning($"[Snow] Forest packs: host {hostPath} missing."); continue; }
                GameObject host = PrefabUtility.LoadPrefabContents(hostPath);
                try
                {
                    foreach (string name in kv.Value.Select(a => a.prefab).Distinct())
                        foreach (Transform old in host.transform.Cast<Transform>().Where(c => c.name == name).ToArray())
                            UnityEngine.Object.DestroyImmediate(old.gameObject);
                    foreach (PackPropsBuilder.Attach a in kv.Value)
                    {
                        string path = $"{source[a]}/Prefabs/{a.prefab}.prefab";
                        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        if (asset == null) { Debug.LogWarning($"[Snow] Forest packs: {path} missing (run Tools / Snow / Packs / Build All Packs)."); continue; }
                        var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, host.transform);
                        go.name = a.prefab;
                        go.transform.localPosition = V(a.pos);
                        go.transform.localRotation = Q(a.rot);
                        go.transform.localScale = Vector3.one;
                        go.SetActive(a.active);
                        placed++;
                    }
                    PrefabUtility.SaveAsPrefabAsset(host, hostPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(host); }
            }
            Debug.Log($"[Snow] Forest packs: {placed} pack prefabs nested into {byHost.Count} far prefabs ({string.Join(", ", byHost.Keys)}).");
            return placed;
        }

        // ------------------------------------------------------------------ carried things standing in their place

        static string Spots(Transform root)
        {
            ForestItems items = root.GetComponentInChildren<ForestItems>(true);
            if (items == null) return "no ForestItems in the scene: pack spots not set";
            var spots = new List<ForestItems.Spot>();
            Transform structures = root.Find("Structures");
            foreach (string f in PackPropsBuilder.DataFiles)
            {
                PackPropsBuilder.Data d = PackPropsBuilder.Read(f);
                if (d?.attach == null) continue;
                foreach (PackPropsBuilder.Attach a in d.attach.Where(a => a.spawn))
                {
                    Transform host = structures != null ? structures.Find(a.host) : null;
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{d.root}/Prefabs/{a.prefab}.prefab");
                    if (host == null || prefab == null) { Debug.LogWarning($"[Snow] Forest packs: spot {a.prefab} in {a.host} skipped ({(host == null ? "no host in the scene" : "no prefab")})."); continue; }
                    spots.Add(new ForestItems.Spot
                    {
                        id = $"{a.prefab}@{a.host}", prefab = prefab,
                        position = host.TransformPoint(V(a.pos)), rotation = host.rotation * Q(a.rot),
                    });
                }
            }
            items.spots = spots.ToArray();
            EditorUtility.SetDirty(items);
            return $"{spots.Count} carried pack thing(s) spawned in place ({string.Join(", ", spots.Select(s => s.id))})";
        }

        // ------------------------------------------------------------------ groups

        static string PlaceGroup(Transform root, string name)
        {
            string path = DataFolder + name + ".json";
            if (!File.Exists(path)) return $"{name}: no {path}";
            var g = JsonUtility.FromJson<Group>(File.ReadAllText(path));
            int heights = 0, holes = 0, trees = 0, grass = 0;
            if (g.ground != null && g.ground.Length > 0)
            {
                (heights, holes) = ForestGroundEdit.Apply(root, g.ground, DataFolder + name + "_ground.bytes", "forest_" + name.ToLowerInvariant());
                trees += ForestGroundEdit.ClearTrees(root, g.ground, 0.5f);
                grass += ForestGroundEdit.ClearGrass(root, g.ground);
            }
            Transform old = root.Find(name);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var group = new GameObject(name).transform;
            group.SetParent(root, false);
            Transform reference = root.Find("Structures")?.Cast<Transform>().FirstOrDefault();
            int placed = 0, missing = 0;
            var clears = new List<ForestGroundEdit.Rect>();
            foreach (Prop p in g.props ?? new Prop[0])
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(p.prefab);
                if (asset == null) { missing++; continue; }
                GameObject go = Place(root, group, asset, p, g.staticProps, reference);
                if (go == null) { missing++; continue; }
                placed++;
                if (p.clear > 0f)
                {
                    Vector3 w = go.transform.position;
                    clears.Add(new ForestGroundEdit.Rect { x = w.x, z = w.z, yaw = 0f, rect = new[] { -p.clear, -p.clear, p.clear, p.clear } });
                }
            }
            if (clears.Count > 0)
            {
                trees += ForestGroundEdit.ClearTrees(root, clears.ToArray(), 0f);
                grass += ForestGroundEdit.ClearGrass(root, clears.ToArray());
            }
            return $"{name}: {placed} props" + (missing > 0 ? $", {missing} MISSING" : "") +
                   (heights + holes > 0 ? $", ground {heights} height samples / {holes} hole cells" : "") +
                   (trees + grass > 0 ? $", {trees} terrain trees and {grass} grass cells cleared" : "");
        }

        static Vector3 V(float[] a) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;

        static Quaternion Q(float[] a) => a != null && a.Length >= 4 ? new Quaternion(a[0], a[1], a[2], a[3]) : Quaternion.identity;

        static bool Active(Transform t, Transform root)
        {
            for (; t != null; t = t.parent)
            {
                if (!t.gameObject.activeSelf) return false;
                if (t == root) break;
            }
            return true;
        }

        /// <summary>The prefab's footprint in its own frame (shown meshes only).</summary>
        static Bounds Footprint(GameObject asset)
        {
            var b = new Bounds();
            bool any = false;
            Matrix4x4 toRoot = asset.transform.worldToLocalMatrix;
            foreach (MeshFilter mf in asset.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || !mf.TryGetComponent(out MeshRenderer _) || !Active(mf.transform, asset.transform)) continue;
                Bounds mb = mf.sharedMesh.bounds;
                Matrix4x4 m = toRoot * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = m.MultiplyPoint3x4(c);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            return b;
        }

        static GameObject Place(Transform root, Transform group, GameObject asset, Prop p, bool makeStatic, Transform reference)
        {
            Vector3 pos;
            float yaw = p.yaw;
            if (!string.IsNullOrEmpty(p.anchor))
            {
                Transform a = root.Find(p.anchor);
                if (a == null) { Debug.LogWarning($"[Snow] Forest packs: anchor {p.anchor} not found; {asset.name} skipped."); return null; }
                pos = a.position;
                yaw += a.eulerAngles.y;
            }
            else pos = new Vector3(p.x, 0f, p.z);
            float scale = p.scale > 0f ? p.scale : 1f;
            Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
            Bounds fp = Footprint(asset);
            // footprint corners and middle on the ground
            Vector3 ext = new Vector3(fp.extents.x, 0f, fp.extents.z) * scale, ctr = new Vector3(fp.center.x, 0f, fp.center.z) * scale;
            Vector3 W(float sx, float sz) => pos + turn * (ctr + new Vector3(ext.x * sx, 0f, ext.z * sz));
            float H(Vector3 w) => ForestGroundEdit.Height(w.x, w.z);
            float y = H(pos);
            Quaternion rot = turn;
            if (p.ground == "fixed") y = p.y;                             // the ground under it is cut by the group itself
            else if (p.ground == "lowest")
            {
                y = Mathf.Min(y, H(W(-1, -1)), H(W(1, -1)), H(W(-1, 1)), H(W(1, 1)), H(W(0, -1)), H(W(0, 1)), H(W(-1, 0)), H(W(1, 0)));
            }
            if (p.align)
            {
                // the plane through the footprint's sides: pitch from front/back, roll from left/right
                float hf = H(W(0, 1)), hb = H(W(0, -1)), hl = H(W(-1, 0)), hr = H(W(1, 0));
                Vector3 fwd = turn * Vector3.forward, right = turn * Vector3.right;
                Vector3 f = fwd * (2f * Mathf.Max(ext.z, 0.1f)) + Vector3.up * (hf - hb);
                Vector3 r = right * (2f * Mathf.Max(ext.x, 0.1f)) + Vector3.up * (hr - hl);
                Vector3 up = Vector3.Cross(f, r).normalized;
                if (up.y < 0f) up = -up;
                rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(fwd, up).normalized, up);
                y = (hf + hb + hl + hr) * 0.25f;
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, group);
            go.transform.SetPositionAndRotation(new Vector3(pos.x, y + p.dy, pos.z), rot);
            go.transform.localScale = Vector3.one * scale;
            if (makeStatic && reference != null)
            {
                StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(reference.gameObject);
                foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
            }
            if (reference != null)
                foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = reference.gameObject.layer;
            return go;
        }

        // ------------------------------------------------------------------ the rock scatter (written once, then hand-moved)

        [Serializable]
        class ScatterRule
        {
            public string[] prefabs;
            public int count;
            public float minSlope, maxSlope, roadMin, roadMax, roadShare, spacing, treeClear, dy, clear;
            public string ground;
            public bool downhill;
        }

        [Serializable]
        class ScatterData
        {
            public int seed = 2610;
            public float margin = 90f;
            public float[] keepClear;                  // x, z, r triples
            public ScatterRule[] rules;
        }

        const string ScatterRulesPath = DataFolder + "ForestRocks_rules.json";

        [MenuItem("Tools/Snow/Forest/Generate Rock Scatter")]
        static void GenerateMenu()
        {
            string path = DataFolder + "ForestRocks.json";
            if (File.Exists(path) && !EditorUtility.DisplayDialog("Rock scatter", $"{path} exists (it may hold hand moves). Write a new scatter over it?", "Write", "Cancel")) return;
            GenerateRocks();
        }

        /// <summary>Stones and boulders scattered in the forest and along the verges, cliffs on the slopes: never on roads,
        /// trails, pads, water, near structures or the dress groups, never in a tree's trunk. Deterministic (seed in the
        /// rules); writes ForestRocks.json, which the group then places.</summary>
        public static int GenerateRocks()
        {
            Transform root = GameObject.Find("Forest")?.transform;
            if (root == null) { Debug.LogError("[Snow] Rock scatter: no Forest root in the open scene."); return 0; }
            var rules = JsonUtility.FromJson<ScatterData>(File.ReadAllText(ScatterRulesPath));
            Terrain[] tiles = Terrain.activeTerrains.Where(t => t.transform.IsChildOf(root)).ToArray();
            var all = new Bounds(tiles[0].transform.position, Vector3.zero);
            foreach (Terrain t in tiles) { all.Encapsulate(t.transform.position); all.Encapsulate(t.transform.position + t.terrainData.size); }
            float x0 = all.min.x + rules.margin, x1 = all.max.x - rules.margin, z0 = all.min.z + rules.margin, z1 = all.max.z - rules.margin;

            // road / trail / pad share on a 2 m grid, then the distance to it (two-pass chamfer)
            const float cell = 2f;
            int nx = Mathf.CeilToInt((all.size.x) / cell), nz = Mathf.CeilToInt((all.size.z) / cell);
            var dist = new float[nz, nx];
            string[] roadLayers = { "TL_RoadGravel", "TL_TrailSoil", "TL_RoadRuts", "TL_TrailTrack", "TL_Asphalt" };
            foreach (Terrain t in tiles)
            {
                TerrainData td = t.terrainData;
                int n = td.alphamapResolution;
                float[,,] am = td.GetAlphamaps(0, 0, n, n);
                int[] rl = Enumerable.Range(0, td.terrainLayers.Length).Where(l => roadLayers.Contains(td.terrainLayers[l].name)).ToArray();
                Vector3 o = t.transform.position;
                for (int az = 0; az < n; az++)
                    for (int ax = 0; ax < n; ax++)
                    {
                        float s = 0f;
                        foreach (int l in rl) s += am[az, ax, l];
                        if (s < 0.3f) continue;
                        int gx = (int)((o.x + (ax + 0.5f) * td.size.x / n - all.min.x) / cell), gz = (int)((o.z + (az + 0.5f) * td.size.z / n - all.min.z) / cell);
                        if (gx >= 0 && gz >= 0 && gx < nx && gz < nz) dist[gz, gx] = -1f;
                    }
            }
            for (int z = 0; z < nz; z++) for (int x = 0; x < nx; x++) dist[z, x] = dist[z, x] < 0f ? 0f : 1e6f;
            for (int z = 0; z < nz; z++)
                for (int x = 0; x < nx; x++)
                {
                    if (x > 0) dist[z, x] = Mathf.Min(dist[z, x], dist[z, x - 1] + cell);
                    if (z > 0) dist[z, x] = Mathf.Min(dist[z, x], dist[z - 1, x] + cell);
                    if (x > 0 && z > 0) dist[z, x] = Mathf.Min(dist[z, x], dist[z - 1, x - 1] + cell * 1.4142f);
                    if (x < nx - 1 && z > 0) dist[z, x] = Mathf.Min(dist[z, x], dist[z - 1, x + 1] + cell * 1.4142f);
                }
            for (int z = nz - 1; z >= 0; z--)
                for (int x = nx - 1; x >= 0; x--)
                {
                    if (x < nx - 1) dist[z, x] = Mathf.Min(dist[z, x], dist[z, x + 1] + cell);
                    if (z < nz - 1) dist[z, x] = Mathf.Min(dist[z, x], dist[z + 1, x] + cell);
                    if (x < nx - 1 && z < nz - 1) dist[z, x] = Mathf.Min(dist[z, x], dist[z + 1, x + 1] + cell * 1.4142f);
                    if (x > 0 && z < nz - 1) dist[z, x] = Mathf.Min(dist[z, x], dist[z + 1, x - 1] + cell * 1.4142f);
                }
            float RoadDist(float x, float z)
            {
                int gx = Mathf.Clamp((int)((x - all.min.x) / cell), 0, nx - 1), gz = Mathf.Clamp((int)((z - all.min.z) / cell), 0, nz - 1);
                return dist[gz, gx];
            }

            // water: the highest water surface per 4 m cell
            const float wcell = 4f;
            int wnx = Mathf.CeilToInt(all.size.x / wcell), wnz = Mathf.CeilToInt(all.size.z / wcell);
            var water = new float[wnz, wnx];
            for (int z = 0; z < wnz; z++) for (int x = 0; x < wnx; x++) water[z, x] = float.NegativeInfinity;
            Transform wt = root.Find("World/Forest_Water");
            if (wt != null)
                foreach (MeshFilter mf in wt.GetComponentsInChildren<MeshFilter>(true))
                {
                    Vector3[] v = mf.sharedMesh.vertices;
                    int[] tri = mf.sharedMesh.triangles;
                    Matrix4x4 m = mf.transform.localToWorldMatrix;
                    for (int k = 0; k < tri.Length; k += 3)
                    {
                        Vector3 a = m.MultiplyPoint3x4(v[tri[k]]), b = m.MultiplyPoint3x4(v[tri[k + 1]]), c = m.MultiplyPoint3x4(v[tri[k + 2]]);
                        float mnx = Mathf.Min(a.x, b.x, c.x), mxx = Mathf.Max(a.x, b.x, c.x), mnz = Mathf.Min(a.z, b.z, c.z), mxz = Mathf.Max(a.z, b.z, c.z), y = Mathf.Max(a.y, b.y, c.y);
                        for (int gz = Mathf.Max(0, (int)((mnz - all.min.z) / wcell)); gz <= Mathf.Min(wnz - 1, (int)((mxz - all.min.z) / wcell)); gz++)
                            for (int gx = Mathf.Max(0, (int)((mnx - all.min.x) / wcell)); gx <= Mathf.Min(wnx - 1, (int)((mxx - all.min.x) / wcell)); gx++)
                                water[gz, gx] = Mathf.Max(water[gz, gx], y);
                    }
                }
            bool Wet(float x, float z, float h, float r)
            {
                for (float dz = -r; dz <= r; dz += wcell)
                    for (float dx = -r; dx <= r; dx += wcell)
                    {
                        int gx = (int)((x + dx - all.min.x) / wcell), gz = (int)((z + dz - all.min.z) / wcell);
                        if (gx < 0 || gz < 0 || gx >= wnx || gz >= wnz) continue;
                        if (water[gz, gx] > h - 1.0f) return true;
                    }
                return false;
            }

            // tree trunks (terrain trees and the rock / stump prototypes) in a 4 m hash
            var trees = new Dictionary<long, List<Vector2>>();
            long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
            foreach (Terrain t in tiles)
            {
                Vector3 o = t.transform.position, s = t.terrainData.size;
                foreach (TreeInstance ti in t.terrainData.treeInstances)
                {
                    var p = new Vector2(o.x + ti.position.x * s.x, o.z + ti.position.z * s.z);
                    long k = Key(Mathf.FloorToInt(p.x / 4f), Mathf.FloorToInt(p.y / 4f));
                    if (!trees.TryGetValue(k, out var l)) trees[k] = l = new List<Vector2>();
                    l.Add(p);
                }
            }
            float TreeDist(float x, float z)
            {
                float best = 1e9f;
                int cx = Mathf.FloorToInt(x / 4f), cz = Mathf.FloorToInt(z / 4f);
                for (int dz = -2; dz <= 2; dz++)
                    for (int dx = -2; dx <= 2; dx++)
                        if (trees.TryGetValue(Key(cx + dx, cz + dz), out var l))
                            foreach (Vector2 p in l) best = Mathf.Min(best, (p - new Vector2(x, z)).sqrMagnitude);
                return Mathf.Sqrt(best);
            }

            // keep-clear: structures and the dress groups (bounds grown), plus the data's circles
            var clearBoxes = new List<Bounds>();
            foreach (Transform s in root.Find("Structures"))
            {
                Renderer[] rs = s.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) continue;
                Bounds b = rs[0].bounds;
                foreach (Renderer r in rs) b.Encapsulate(r.bounds);
                if (b.size.x > 150f || b.size.z > 150f) b = new Bounds(s.position, new Vector3(20f, 1f, 20f));   // wires and masts' guys
                b.Expand(new Vector3(16f, 0f, 16f));
                clearBoxes.Add(b);
            }
            foreach (string grp in WorldSync.DressGroups.Where(n => !Groups.Contains(n)).Concat(Groups.Where(n => n != "ForestRocks")))
            {
                Transform gr = root.Find(grp);
                if (gr == null) continue;
                foreach (Transform c in gr)
                    clearBoxes.Add(new Bounds(c.position, new Vector3(10f, 1f, 10f)));
            }
            bool Clear(float x, float z, float r)
            {
                foreach (Bounds b in clearBoxes)
                    if (x > b.min.x - r && x < b.max.x + r && z > b.min.z - r && z < b.max.z + r) return false;
                for (int i = 0; i + 2 < (rules.keepClear?.Length ?? 0); i += 3)
                    if (new Vector2(x - rules.keepClear[i], z - rules.keepClear[i + 1]).magnitude < rules.keepClear[i + 2] + r) return false;
                return true;
            }

            var rng = new Random(rules.seed);
            var placed = new List<(Vector2 p, float r)>();
            var props = new List<Prop>();
            Physics.SyncTransforms();
            foreach (ScatterRule rule in rules.rules)
            {
                var sizes = rule.prefabs.Select(p => Footprint(AssetDatabase.LoadAssetAtPath<GameObject>(p))).ToArray();
                int made = 0, tries = 0;
                while (made < rule.count && tries < rule.count * 400)
                {
                    tries++;
                    int pi = rng.Next(rule.prefabs.Length);
                    float scale = 0.85f + (float)rng.NextDouble() * 0.3f;
                    float r = Mathf.Max(sizes[pi].extents.x, sizes[pi].extents.z) * scale;
                    float x = x0 + (float)rng.NextDouble() * (x1 - x0), z = z0 + (float)rng.NextDouble() * (z1 - z0);
                    float rd = RoadDist(x, z);
                    bool verge = rng.NextDouble() < rule.roadShare;
                    if (rd < rule.roadMin + r || (verge ? rd > rule.roadMax : rd < rule.roadMax * 0.5f)) continue;
                    float h = ForestGroundEdit.Height(x, z);
                    float gx = (ForestGroundEdit.Height(x + 3f, z) - ForestGroundEdit.Height(x - 3f, z)) / 6f, gz = (ForestGroundEdit.Height(x, z + 3f) - ForestGroundEdit.Height(x, z - 3f)) / 6f;
                    float slope = Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
                    if (slope < rule.minSlope || slope > rule.maxSlope) continue;
                    if (Wet(x, z, h, r + 4f) || !Clear(x, z, r)) continue;
                    if (TreeDist(x, z) < r * 0.6f + rule.treeClear) continue;
                    if (placed.Any(q => (q.p - new Vector2(x, z)).magnitude < q.r + r + rule.spacing)) continue;
                    // no object colliders around (fences, logs, props)
                    if (Physics.OverlapBox(new Vector3(x, h + 1.5f, z), new Vector3(r + 1.5f, 1.4f, r + 1.5f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)
                        .Any(c => !(c is TerrainCollider))) continue;
                    float yaw = rule.downhill ? Mathf.Atan2(-gx, -gz) * Mathf.Rad2Deg : (float)rng.NextDouble() * 360f;
                    placed.Add((new Vector2(x, z), r));
                    props.Add(new Prop
                    {
                        prefab = rule.prefabs[pi], x = (float)Math.Round(x, 2), z = (float)Math.Round(z, 2), yaw = (float)Math.Round(yaw, 1),
                        scale = (float)Math.Round(scale, 3), ground = rule.ground, dy = rule.dy, clear = rule.clear,
                    });
                    made++;
                }
                Debug.Log($"[Snow] Rock scatter: {made}/{rule.count} of {string.Join(", ", rule.prefabs.Select(Path.GetFileNameWithoutExtension))} ({tries} tries).");
            }
            var data = new Group
            {
                note = "Rocks of forest_rocks scattered over the Forest by Tools / Snow / Forest / Generate Rock Scatter (rules: ForestRocks_rules.json); " +
                       "placed by ForestPacksStep as the group ForestRocks. Hand moves in the scene are kept by World Sync; edit or move, do not regenerate.",
                staticProps = true, props = props.ToArray(),
            };
            File.WriteAllText(DataFolder + "ForestRocks.json", JsonUtility.ToJson(data, true));
            AssetDatabase.ImportAsset(DataFolder + "ForestRocks.json");
            return props.Count;
        }
    }
}
