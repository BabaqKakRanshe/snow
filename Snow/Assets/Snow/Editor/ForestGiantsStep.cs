using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Snow.EditorTools
{
    /// <summary>
    /// Forest integration, the giants' skeletons (docs/gdd/locations/giant_bones; data <see cref="DataPath"/>):
    ///  - the core's giant (Forest_Layout giants[], 558 m) gets the fixed skull (Assets/Snow/Giants/Giant_SkullFix.fbx: one closed
    ///    surface instead of 37 loose pieces with holes, source/assets/giant_skull_fix) and the data's bone material;
    ///  - more skeletons of the same model at other sizes and poses: a pose turns joints (the bones of the model are flat
    ///    siblings, each pivot at its joint; <see cref="Parent"/> gives the chain), built as one child per bone like the core's giant (bones wholly under the ground are left out), set by the pelvis at
    ///    world x, z and dy over the ground;
    ///  - loose bones of a giant's size, props of the camps and digs, ground edits (<see cref="ForestGroundEdit"/>: craters, pits);
    ///  - earth heaped against every bone that lies on the ground (<see cref="Mounds"/>): the bones sink into it as if they had lain
    ///    for decades; the ground under the mounds before them is kept in <see cref="MoundPath"/> and put back first on every run;
    ///  - terrain trees that a bone would cut are taken away, overgrown giants get young spruces through them. Both are kept in
    ///    <see cref="TreesPath"/> and undone before the next run, so a second run gives the same result.
    /// Runs after <see cref="ForestAirfieldStep"/>; Tools / Snow / Forest / Giants Dress runs it alone.
    /// </summary>
    public static class ForestGiantsStep
    {
        public const string DataPath = "Assets/Snow/Levels/Forest/ForestGiants.json";
        const string TreesPath = "Assets/Snow/Levels/Forest/ForestGiants_trees.json";
        const string MoundPath = "Assets/Snow/Levels/Forest/ForestGiants_mounds.bytes";   // the ground under the mounds before them
        const string MeshFolder = "Assets/Snow/Levels/Forest/Giants";   // the giants' materials
        const string GiantFbx = "Assets/SkyIsland/Models/Giant_Skeleton.fbx";
        const string RootName = "ForestGiants";
        const float FullHeight = 558f;                    // the model's height at scale 1

        [Serializable] public class Joint { public string name; public float[] e; }
        [Serializable] public class Pose { public string name; public Joint[] joints; }
        [Serializable] public class Mat { public string name, from; public float[] color; }
        [Serializable]
        public class Giant
        {
            public string name, pose, material = "dark", note;
            public float size = 60f, x, z, dy, yaw, pitch, roll;
            public Joint[] extra;                         // joints on top of the pose
            public string[] hide;
            public float overgrown;                       // young spruces per 1000 m2 of the footprint (0: none)
            public float clear;                           // m round the bones where the forest's trees go (a clearing to see it)
            public float mound = 1f;                      // earth heaped against the bones that touch the ground (0: none)
        }
        [Serializable]
        public class Bone
        {
            public string bone, material = "dark", note;
            public float size = 558f, x, z, dy, yaw, pitch, roll, mound = 1f;
        }
        [Serializable]
        public class Prop
        {
            public string prefab, note;
            public float x, z, dy, yaw, pitch, roll, scale = 1f;
        }
        [Serializable]
        public class Data
        {
            public string note, skullFbx, skullTexture, coreMaterial;
            public Mat[] materials;
            public Pose[] poses;
            public Giant[] giants;
            public Bone[] bones;
            public Prop[] props;
            public ForestGroundEdit.Rect[] ground;
        }

        [Serializable] class TreeRec { public string tile; public int proto; public float[] p; public float w, h, rot; public int[] c; }
        [Serializable] class TreeStore { public TreeRec[] removed = new TreeRec[0]; public TreeRec[] added = new TreeRec[0]; }

        [MenuItem("Tools/Snow/Forest/Giants Dress")]
        static void RunMenu()
        {
            Transform root = GameObject.Find("Forest")?.transform;
            if (root == null) { Debug.LogError("[Snow] Forest giants: no Forest root in the open scene."); return; }
            Run(root);
        }

        // ------------------------------------------------------------------ joints
        /// <summary>The joint a bone hangs from (null: the pelvis, the root of the chain).</summary>
        public static string Parent(string n, ICollection<string> all)
        {
            if (n == "B_Pelvis") return null;
            if (n == "B_Skull") return "B_C1";
            if (n == "B_Mandible") return "B_Skull";
            if (n == "B_Sternum") return "B_T5";
            string side = n.Length > 2 && (n.EndsWith("_L") || n.EndsWith("_R")) ? n.Substring(n.Length - 1) : null;
            int Num(string s) => int.Parse(new string(s.Where(char.IsDigit).ToArray()));
            if (n.StartsWith("B_L") && n.Length <= 4) { int k = Num(n); return k == 5 ? "B_Pelvis" : "B_L" + (k + 1); }
            if (n.StartsWith("B_T") && n.Length <= 5 && !n.StartsWith("B_Ti") && !n.StartsWith("B_To") && !n.StartsWith("B_Ta")) { int k = Num(n); return k == 12 ? "B_L1" : "B_T" + (k + 1); }
            if (n.StartsWith("B_C") && n.Length <= 4) { int k = Num(n); return k == 7 ? "B_T1" : "B_C" + (k + 1); }
            if (n.StartsWith("B_Rib_")) return "B_T" + Num(n.Substring(7));
            if (side != null)
            {
                string b = n.Substring(0, n.Length - 2);
                switch (b)
                {
                    case "B_Clavicle": return "B_T1";
                    case "B_Scapula": return "B_Clavicle_" + side;
                    case "B_Humerus": return "B_Scapula_" + side;
                    case "B_Radius": case "B_Ulna": return "B_Humerus_" + side;
                    case "B_Carpals": return "B_Radius_" + side;
                    case "B_Femur": return "B_Pelvis";
                    case "B_Patella": case "B_Tibia": case "B_Fibula": return "B_Femur_" + side;
                    case "B_Tarsals": return "B_Tibia_" + side;
                }
            }
            // B_Metacarpal_L3, B_Phalanx_L3_2, B_Metatarsal_R1, B_Toe_R1_3
            string[] parts = n.Split('_');
            if (parts.Length == 3 && parts[1] == "Metacarpal") return "B_Carpals_" + parts[2][0];
            if (parts.Length == 3 && parts[1] == "Metatarsal") return "B_Tarsals_" + parts[2][0];
            if (parts.Length == 4 && (parts[1] == "Phalanx" || parts[1] == "Toe"))
            {
                int k = int.Parse(parts[3]);
                string chainRoot = parts[1] == "Phalanx" ? "B_Metacarpal_" + parts[2] : "B_Metatarsal_" + parts[2];
                for (int j = k - 1; j >= 1; j--)
                {
                    string p = $"B_{parts[1]}_{parts[2]}_{j}";
                    if (all.Contains(p)) return p;
                }
                return chainRoot;
            }
            return "B_Pelvis";
        }

        /// <summary>Joint turns of a pose; group names spread a turn over a chain: spine (L5..L1), chest (T12..T1), neck
        /// (C7..C1), fingers_L / fingers_R (every phalanx), toes_L / toes_R.</summary>
        static Dictionary<string, Quaternion> Turns(IEnumerable<Joint> joints, ICollection<string> all)
        {
            var d = new Dictionary<string, Quaternion>();
            void Add(string bone, Vector3 e)
            {
                if (!all.Contains(bone)) return;
                d[bone] = (d.TryGetValue(bone, out Quaternion q) ? q : Quaternion.identity) * Quaternion.Euler(e);
            }
            foreach (Joint j in joints ?? new Joint[0])
            {
                if (j?.e == null || j.e.Length < 3) continue;
                var e = new Vector3(j.e[0], j.e[1], j.e[2]);
                string[] chain = null;
                switch (j.name)
                {
                    case "spine": chain = Enumerable.Range(1, 5).Select(k => "B_L" + k).ToArray(); break;
                    case "chest": chain = Enumerable.Range(1, 12).Select(k => "B_T" + k).ToArray(); break;
                    case "neck": chain = Enumerable.Range(1, 7).Select(k => "B_C" + k).ToArray(); break;
                    case "fingers_L": case "fingers_R":
                        foreach (string b in all.Where(b => b.StartsWith("B_Phalanx_" + j.name[8]))) Add(b, e);
                        continue;
                    case "toes_L": case "toes_R":
                        foreach (string b in all.Where(b => b.StartsWith("B_Toe_" + j.name[5]))) Add(b, e);
                        continue;
                }
                if (chain == null) { Add(j.name, e); continue; }
                foreach (string b in chain) Add(b, e / chain.Length);
            }
            return d;
        }

        /// <summary>Forward kinematics over the flat bones: rest pivot + the parent's world turn.</summary>
        static Dictionary<string, (Vector3 p, Quaternion q)> Posed(Dictionary<string, Vector3> rest, Dictionary<string, Quaternion> turns)
        {
            var res = new Dictionary<string, (Vector3 p, Quaternion q)>();
            var keys = rest.Keys.ToList();
            (Vector3 p, Quaternion q) Get(string n)
            {
                if (res.TryGetValue(n, out var v)) return v;
                Quaternion t = turns.TryGetValue(n, out Quaternion tq) ? tq : Quaternion.identity;
                string par = Parent(n, keys);
                if (par == null || !rest.ContainsKey(par)) v = (rest[n], t);
                else
                {
                    var pp = Get(par);
                    v = (pp.p + pp.q * (rest[n] - rest[par]), pp.q * t);
                }
                res[n] = v;
                return v;
            }
            foreach (string n in keys) Get(n);
            return res;
        }

        // ------------------------------------------------------------------ materials
        static Material EnsureMat(string path, Material src, Texture baseMap, float[] color)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(src); AssetDatabase.CreateAsset(m, path); }
            else m.CopyPropertiesFromMaterial(src);
            if (baseMap != null) { m.SetTexture("_BaseMap", baseMap); m.SetTexture("_MainTex", baseMap); }
            if (color != null && color.Length >= 3)
            {
                var c = new Color(color[0], color[1], color[2], 1f);
                m.SetColor("_BaseColor", c);
                m.SetColor("_Color", c);
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ trees
        static string TileKey(Terrain t) => t.name + "@" + t.transform.position.x.ToString("0") + "," + t.transform.position.z.ToString("0");

        static TreeRec Rec(Terrain t, TreeInstance ti) => new TreeRec
        {
            tile = TileKey(t), proto = ti.prototypeIndex, p = new[] { ti.position.x, ti.position.y, ti.position.z },
            w = ti.widthScale, h = ti.heightScale, rot = ti.rotation, c = new int[] { ti.color.r, ti.color.g, ti.color.b, ti.color.a },
        };

        static TreeInstance Inst(TreeRec r) => new TreeInstance
        {
            prototypeIndex = r.proto, position = new Vector3(r.p[0], r.p[1], r.p[2]), widthScale = r.w, heightScale = r.h,
            rotation = r.rot, color = new Color32((byte)r.c[0], (byte)r.c[1], (byte)r.c[2], (byte)r.c[3]), lightmapColor = Color.white,
        };

        static bool Same(TreeInstance a, TreeRec r) =>
            a.prototypeIndex == r.proto && Mathf.Abs(a.position.x - r.p[0]) < 1e-6f && Mathf.Abs(a.position.z - r.p[2]) < 1e-6f &&
            Mathf.Abs(a.heightScale - r.h) < 1e-5f;

        /// <summary>The last run's tree edits undone: its young spruces out, the trees it took away back.</summary>
        static (int back, int gone) UndoTrees(Transform root)
        {
            if (!File.Exists(TreesPath)) return (0, 0);
            var st = JsonUtility.FromJson<TreeStore>(File.ReadAllText(TreesPath));
            int back = 0, gone = 0;
            foreach (Terrain t in Terrain.activeTerrains.Where(t => t != null && t.transform.IsChildOf(root)))
            {
                string key = TileKey(t);
                var add = st.added.Where(r => r.tile == key).ToList();
                var rem = st.removed.Where(r => r.tile == key).ToList();
                if (add.Count == 0 && rem.Count == 0) continue;
                TerrainData td = t.terrainData;
                var list = new List<TreeInstance>(td.treeInstances);
                int before = list.Count;
                list.RemoveAll(ti => add.Any(r => Same(ti, r)));
                gone += before - list.Count;
                foreach (TreeRec r in rem)
                    if (r.proto < td.treePrototypes.Length) { list.Add(Inst(r)); back++; }
                td.SetTreeInstances(list.ToArray(), false);
                EditorUtility.SetDirty(td);
            }
            return (back, gone);
        }

        static Terrain TileAt(Transform root, float x, float z) =>
            Terrain.activeTerrains.FirstOrDefault(t => t != null && t.transform.IsChildOf(root) &&
                x >= t.transform.position.x && z >= t.transform.position.z &&
                x < t.transform.position.x + t.terrainData.size.x && z < t.transform.position.z + t.terrainData.size.z);

        /// <summary>Terrain trees whose trunk (0.5..12 m) comes within <paramref name="reach"/> m of a giant's collider are taken away.</summary>
        static int CutTrees(Transform root, IList<Collider> cols, Bounds area, TreeStore st, float reach = 0.5f)
        {
            int cut = 0;
            reach = Mathf.Max(0.5f, reach);
            area.Expand(new Vector3(reach * 2f, 0f, reach * 2f));
            var set = new HashSet<Collider>(cols);
            var hits = new Collider[16];
            foreach (Terrain t in Terrain.activeTerrains.Where(t => t != null && t.transform.IsChildOf(root)))
            {
                TerrainData td = t.terrainData;
                Vector3 o = t.transform.position, s = td.size;
                var keep = new List<TreeInstance>(td.treeInstanceCount);
                bool changed = false;
                foreach (TreeInstance ti in td.treeInstances)
                {
                    Vector3 w = new Vector3(o.x + ti.position.x * s.x, o.y + ti.position.y * s.y, o.z + ti.position.z * s.z);
                    if (w.x < area.min.x || w.x > area.max.x || w.z < area.min.z || w.z > area.max.z) { keep.Add(ti); continue; }
                    int n = Physics.OverlapCapsuleNonAlloc(w + Vector3.up * 0.5f, w + Vector3.up * 12f, reach, hits, ~0, QueryTriggerInteraction.Ignore);
                    bool hit = false;
                    for (int i = 0; i < n && !hit; i++) hit = set.Contains(hits[i]);
                    if (!hit) { keep.Add(ti); continue; }
                    st.removed = st.removed.Append(Rec(t, ti)).ToArray();
                    changed = true;
                    cut++;
                }
                if (changed) { td.SetTreeInstances(keep.ToArray(), false); EditorUtility.SetDirty(td); }
            }
            return cut;
        }

        /// <summary>Young spruces in a giant's footprint (through the ribs, along the bones), where the trunk's foot is free.</summary>
        static int Plant(Transform root, IList<Collider> cols, Bounds area, float perK, int seed, TreeStore st)
        {
            if (perK <= 0f) return 0;
            var rnd = new System.Random(seed);
            area.Expand(new Vector3(6f, 0f, 6f));
            float areaM2 = area.size.x * area.size.z;
            int want = Mathf.Clamp(Mathf.RoundToInt(areaM2 / 1000f * perK), 3, 80), planted = 0;
            var set = new HashSet<Collider>(cols);
            var hits = new Collider[16];
            var adds = new Dictionary<Terrain, List<TreeInstance>>();
            for (int tries = 0; tries < want * 12 && planted < want; tries++)
            {
                float x = area.min.x + (float)rnd.NextDouble() * area.size.x, z = area.min.z + (float)rnd.NextDouble() * area.size.z;
                Terrain t = TileAt(root, x, z);
                if (t == null) continue;
                float gy = ForestGroundEdit.Height(x, z);
                // only near the bones (a bone within 4 m of the trunk), never inside one
                int n = Physics.OverlapCapsuleNonAlloc(new Vector3(x, gy + 0.5f, z), new Vector3(x, gy + 6f, z), 4f, hits, ~0, QueryTriggerInteraction.Ignore);
                bool near = false;
                for (int i = 0; i < n && !near; i++) near = set.Contains(hits[i]);
                if (!near) continue;
                n = Physics.OverlapCapsuleNonAlloc(new Vector3(x, gy + 0.3f, z), new Vector3(x, gy + 3f, z), 0.4f, hits, ~0, QueryTriggerInteraction.Ignore);
                bool blocked = false;
                for (int i = 0; i < n && !blocked; i++) blocked = set.Contains(hits[i]);
                if (blocked) continue;
                TerrainData td = t.terrainData;
                // the kind most common round here (the forest's spruce)
                int proto = Spruce(t);
                if (proto < 0) continue;
                Vector3 o = t.transform.position, s = td.size;
                float hs = 0.35f + (float)rnd.NextDouble() * 0.45f;
                var ti = new TreeInstance
                {
                    prototypeIndex = proto, position = new Vector3((x - o.x) / s.x, (gy - o.y) / s.y, (z - o.z) / s.z),
                    widthScale = hs * (0.85f + (float)rnd.NextDouble() * 0.3f), heightScale = hs, rotation = (float)rnd.NextDouble() * Mathf.PI * 2f,
                    color = Color.white, lightmapColor = Color.white,
                };
                if (!adds.TryGetValue(t, out var l)) adds[t] = l = new List<TreeInstance>();
                l.Add(ti);
                st.added = st.added.Append(Rec(t, ti)).ToArray();
                planted++;
            }
            foreach (var kv in adds)
            {
                kv.Key.terrainData.SetTreeInstances(kv.Key.terrainData.treeInstances.Concat(kv.Value).ToArray(), false);
                EditorUtility.SetDirty(kv.Key.terrainData);
            }
            return planted;
        }

        static readonly Dictionary<TerrainData, int> s_Spruce = new Dictionary<TerrainData, int>();
        static int Spruce(Terrain t)
        {
            TerrainData td = t.terrainData;
            if (s_Spruce.TryGetValue(td, out int p)) return p;
            var protos = td.treePrototypes;
            var counts = new int[protos.Length];
            foreach (TreeInstance ti in td.treeInstances) counts[ti.prototypeIndex]++;
            int best = -1;
            for (int i = 0; i < protos.Length; i++)
            {
                string nm = protos[i].prefab != null ? protos[i].prefab.name : "";
                bool spruce = nm.IndexOf("spruce", StringComparison.OrdinalIgnoreCase) >= 0 || nm.IndexOf("fir", StringComparison.OrdinalIgnoreCase) >= 0 || nm.IndexOf("pine", StringComparison.OrdinalIgnoreCase) >= 0;
                if (spruce && (best < 0 || counts[i] > counts[best])) best = i;
            }
            if (best < 0) best = Array.IndexOf(counts, counts.DefaultIfEmpty(0).Max());
            s_Spruce[td] = best;
            return best;
        }

        // ------------------------------------------------------------------ mounds
        class Piece
        {
            public string name;
            public float size, mound;
            public HashSet<Collider> cols;
            public Bounds area;
            public float Reach => 2f + 0.1f * size;                    // m the heap spreads from a bone (a slope of ~1 : 2.5)
            public float Cover => 0.04f * size * mound;                // m of earth at most against a bone
            public float Gap => 0.25f + 0.02f * size;                  // a bone this far over the ground still gets earth under it
        }

        static IEnumerable<Terrain> TilesOf(Transform root) => Terrain.activeTerrains.Where(t => t != null && t.terrainData != null && t.transform.IsChildOf(root));

        /// <summary>The terrain trees in an index box of a tile stand on the ground again.</summary>
        static void FollowTrees(Terrain t, int x0, int z0, int w, int h)
        {
            TerrainData td = t.terrainData;
            int res = td.heightmapResolution;
            float u0 = (float)x0 / (res - 1), v0 = (float)z0 / (res - 1), u1 = (float)(x0 + w - 1) / (res - 1), v1 = (float)(z0 + h - 1) / (res - 1);
            TreeInstance[] trees = td.treeInstances;
            bool moved = false;
            for (int i = 0; i < trees.Length; i++)
            {
                Vector3 p = trees[i].position;
                if (p.x < u0 || p.x > u1 || p.z < v0 || p.z > v1) continue;
                float ny = td.GetInterpolatedHeight(p.x, p.z) / td.size.y;
                if (Mathf.Abs(ny - p.y) < 1e-6f) continue;
                trees[i].position = new Vector3(p.x, ny, p.z);
                moved = true;
            }
            if (moved) td.SetTreeInstances(trees, false);
        }

        /// <summary>The ground under the last run's mounds back as it was before them.</summary>
        static int RestoreMounds(Transform root)
        {
            if (!File.Exists(MoundPath)) return 0;
            int n = 0;
            var tiles = TilesOf(root).ToList();
            using (var r = new BinaryReader(File.OpenRead(MoundPath)))
                for (int c = r.ReadInt32(), i = 0; i < c; i++)
                {
                    string name = r.ReadString();
                    int x0 = r.ReadInt32(), z0 = r.ReadInt32(), w = r.ReadInt32(), h = r.ReadInt32();
                    var a = new float[h, w];
                    for (int z = 0; z < h; z++) for (int x = 0; x < w; x++) a[z, x] = r.ReadSingle();
                    Terrain t = tiles.FirstOrDefault(tt => tt.terrainData.name == name);
                    if (t == null) continue;
                    t.terrainData.SetHeights(x0, z0, a);
                    FollowTrees(t, x0, z0, w, h);
                    EditorUtility.SetDirty(t.terrainData);
                    n += w * h;
                }
            return n;
        }

        /// <summary>How much of the ground at a point is painted road, track or asphalt (0..1).</summary>
        static float Roadness(Transform root, float x, float z)
        {
            Terrain t = TileAt(root, x, z);
            if (t == null) return 0f;
            TerrainData td = t.terrainData;
            Vector3 o = t.transform.position;
            int ax = Mathf.Clamp((int)((x - o.x) / td.size.x * td.alphamapWidth), 0, td.alphamapWidth - 1);
            int az = Mathf.Clamp((int)((z - o.z) / td.size.z * td.alphamapHeight), 0, td.alphamapHeight - 1);
            float[,,] a = td.GetAlphamaps(ax, az, 1, 1);
            TerrainLayer[] layers = td.terrainLayers;
            float w = 0f;
            for (int i = 0; i < layers.Length; i++)
            {
                string n = layers[i] != null ? layers[i].name : "";
                if (n.Contains("Road") || n.Contains("Trail") || n.Contains("Asphalt")) w += a[0, 0, i];
            }
            return Mathf.Clamp01(w);
        }

        /// <summary>Ground that keeps its height: under something lying on it that is not the terrain or a giant (the road and
        /// paved meshes, buildings, props) within 1.5 m.</summary>
        static bool Kept(float x, float z)
        {
            float g = ForestGroundEdit.Height(x, z);
            foreach (RaycastHit h in Physics.RaycastAll(new Vector3(x, g + 6f, z), Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider is TerrainCollider || h.point.y < g - 1.5f) continue;
                Transform giants = h.collider.transform.root.Find(RootName);
                if (giants != null && h.collider.transform.IsChildOf(giants)) continue;
                return true;
            }
            return false;
        }

        /// <summary>Earth heaped against the bones: where a bone lies on the ground (its underside at most <see cref="Piece.Gap"/>
        /// over it), the ground rises to half the bone's thickness (at most <see cref="Piece.Cover"/>) and spreads out over
        /// <see cref="Piece.Reach"/> m with a soft edge and a little unevenness. The ground before is stored first.</summary>
        static int Mounds(Transform root, List<Piece> pieces)
        {
            pieces = pieces.Where(p => p.mound > 0f).ToList();
            // one index box per piece and tile it reaches (boxes may overlap: they all hold the ground before any mound)
            var boxes = new List<(Terrain t, Piece p, int x0, int z0, int w, int h)>();
            foreach (Terrain t in TilesOf(root))
            {
                TerrainData td = t.terrainData;
                Vector3 o = t.transform.position;
                int res = td.heightmapResolution;
                float step = td.size.x / (res - 1);
                foreach (Piece p in pieces)
                {
                    float r = p.Reach + step;
                    int ax = Mathf.Max(0, Mathf.FloorToInt((p.area.min.x - r - o.x) / step)), bx = Mathf.Min(res - 1, Mathf.CeilToInt((p.area.max.x + r - o.x) / step));
                    int az = Mathf.Max(0, Mathf.FloorToInt((p.area.min.z - r - o.z) / step)), bz = Mathf.Min(res - 1, Mathf.CeilToInt((p.area.max.z + r - o.z) / step));
                    if (bx < ax || bz < az) continue;
                    boxes.Add((t, p, ax, az, bx - ax + 1, bz - az + 1));
                }
            }
            // the ground before the mounds (the last run's mounds are already gone: RestoreMounds)
            using (var w = new BinaryWriter(File.Create(MoundPath)))
            {
                w.Write(boxes.Count);
                foreach (var b in boxes)
                {
                    float[,] a = b.t.terrainData.GetHeights(b.x0, b.z0, b.w, b.h);
                    w.Write(b.t.terrainData.name); w.Write(b.x0); w.Write(b.z0); w.Write(b.w); w.Write(b.h);
                    foreach (float f in a) w.Write(f);
                }
            }

            // seeds: earth wanted right at the bones (world x, z, metres, reach), on the tiles' own samples under each piece
            var seeds = new List<(float x, float z, float v, float r)>();
            bool backfaces = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            try
            {
                foreach (var b in boxes)
                {
                    TerrainData td = b.t.terrainData;
                    Vector3 o = b.t.transform.position;
                    float step = td.size.x / (td.heightmapResolution - 1);
                    Piece p = b.p;
                    for (int iz = 0; iz < b.h; iz++)
                        for (int ix = 0; ix < b.w; ix++)
                        {
                            float x = o.x + (b.x0 + ix) * step, z = o.z + (b.z0 + iz) * step;
                            if (x < p.area.min.x || x > p.area.max.x || z < p.area.min.z || z > p.area.max.z) continue;
                            float top = p.area.max.y + 1f;
                            RaycastHit[] hits = Physics.RaycastAll(new Vector3(x, top, z), Vector3.down, top - p.area.min.y + 2f, ~0, QueryTriggerInteraction.Ignore);
                            float yTop = float.MinValue, yBot = float.MaxValue;
                            foreach (RaycastHit h in hits)
                                if (p.cols.Contains(h.collider)) { yTop = Mathf.Max(yTop, h.point.y); yBot = Mathf.Min(yBot, h.point.y); }
                            if (yTop == float.MinValue) continue;
                            float g = ForestGroundEdit.Height(x, z);
                            if (yTop <= g || yBot > g + p.Gap) continue;               // buried here, or arching over the ground
                            float bottom = Mathf.Max(yBot, g);
                            float v = Mathf.Min(p.Cover, (bottom - g) + 0.5f * (yTop - bottom));
                            if (v > 0.02f) seeds.Add((x, z, v, p.Reach));
                        }
                }
            }
            finally { Physics.queriesHitBackfaces = backfaces; }
            if (seeds.Count == 0) return 0;

            // spread: each sample takes the highest seed's earth, faded by its distance over the seed's reach
            const float Cell = 4f;
            var grid = new Dictionary<(int, int), List<int>>();
            for (int i = 0; i < seeds.Count; i++)
            {
                var key = (Mathf.FloorToInt(seeds[i].x / Cell), Mathf.FloorToInt(seeds[i].z / Cell));
                if (!grid.TryGetValue(key, out var l)) grid[key] = l = new List<int>();
                l.Add(i);
            }
            int span = Mathf.CeilToInt(seeds.Max(s => s.r) / Cell);
            var done = new HashSet<(Terrain, int, int)>();                 // a sample in two boxes is raised once
            int changed = 0;
            foreach (var b in boxes)
            {
                TerrainData td = b.t.terrainData;
                Vector3 o = b.t.transform.position;
                float step = td.size.x / (td.heightmapResolution - 1);
                float[,] hs = td.GetHeights(b.x0, b.z0, b.w, b.h);
                bool any = false;
                for (int iz = 0; iz < b.h; iz++)
                    for (int ix = 0; ix < b.w; ix++)
                    {
                        if (!done.Add((b.t, b.x0 + ix, b.z0 + iz))) continue;
                        float x = o.x + (b.x0 + ix) * step, z = o.z + (b.z0 + iz) * step;
                        int cx = Mathf.FloorToInt(x / Cell), cz = Mathf.FloorToInt(z / Cell);
                        float add = 0f;
                        for (int gz = cz - span; gz <= cz + span; gz++)
                            for (int gx = cx - span; gx <= cx + span; gx++)
                            {
                                if (!grid.TryGetValue((gx, gz), out var l)) continue;
                                foreach (int i in l)
                                {
                                    var s = seeds[i];
                                    float dd = (s.x - x) * (s.x - x) + (s.z - z) * (s.z - z);
                                    if (dd >= s.r * s.r) continue;
                                    add = Mathf.Max(add, s.v * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Sqrt(dd) / s.r)));
                                }
                            }
                        if (add <= 0.01f || Kept(x, z)) continue;
                        // roads and tracks keep their height; a mound fades out at their edge
                        float road = Mathf.Max(Roadness(root, x, z), 0.6f * Mathf.Max(Roadness(root, x + 2f, z), Roadness(root, x - 2f, z), Roadness(root, x, z + 2f), Roadness(root, x, z - 2f)));
                        add *= 1f - Mathf.Clamp01(road * 2f);
                        if (add <= 0.01f) continue;
                        add *= 0.8f + 0.4f * Mathf.PerlinNoise(x * 0.13f + 31.7f, z * 0.13f + 7.3f);
                        hs[iz, ix] += add / td.size.y;
                        changed++;
                        any = true;
                    }
                if (!any) continue;
                td.SetHeights(b.x0, b.z0, hs);
                FollowTrees(b.t, b.x0, b.z0, b.w, b.h);
                EditorUtility.SetDirty(td);
            }
            return changed;
        }

        // ------------------------------------------------------------------ build
        /// <summary>A posed giant as the core builds its own: one child per bone (the model's mesh, or the fixed skull), each with
        /// its collider; the root at the pelvis joint, scaled to the giant's size. Nothing is written as an asset.</summary>
        static GameObject Build(Transform parent, string name, GameObject model, GameObject skullFix, Dictionary<string, (Vector3 p, Quaternion q)> posed,
                                float k, ICollection<string> hide, Material[] mats)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * k;
            Vector3 pelvis = posed["B_Pelvis"].p;
            foreach (Transform b in model.transform)
            {
                if (hide != null && hide.Contains(b.name)) continue;
                var mf = b.GetComponent<MeshFilter>();
                if (mf == null || !posed.ContainsKey(b.name)) continue;
                bool isSkull = (b.name == "B_Skull" || b.name == "B_Mandible") && skullFix != null;
                Mesh mesh = isSkull ? skullFix.transform.Find(b.name).GetComponent<MeshFilter>().sharedMesh : mf.sharedMesh;
                var bone = Put(go.transform, b.name, mesh, new[] { mats[isSkull ? 1 : 0] }, true);
                bone.transform.localPosition = posed[b.name].p - pelvis;
                bone.transform.localRotation = posed[b.name].q;
            }
            return go;
        }

        /// <summary>Bones wholly under the ground (their top under the ground at their centre) are not kept.</summary>
        static int Prune(GameObject giant)
        {
            int n = 0;
            foreach (Transform b in giant.transform.Cast<Transform>().ToList())
            {
                Bounds bb = b.GetComponent<Renderer>().bounds;
                if (bb.max.y < ForestGroundEdit.Height(bb.center.x, bb.center.z) - 0.3f) { UnityEngine.Object.DestroyImmediate(b.gameObject); n++; }
            }
            return n;
        }

        static GameObject Put(Transform parent, string name, Mesh mesh, Material[] mats, bool collider)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats;
            r.shadowCastingMode = ShadowCastingMode.On;
            r.receiveShadows = true;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic);
            return go;
        }

        public static void Run(Transform root)
        {
            if (!File.Exists(DataPath)) { Debug.LogWarning($"[Snow] Forest giants: {DataPath} not found; no giants."); return; }
            var d = JsonUtility.FromJson<Data>(File.ReadAllText(DataPath));
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(GiantFbx);
            if (model == null) { Debug.LogError($"[Snow] Forest giants: {GiantFbx} missing."); return; }
            var skullFix = string.IsNullOrEmpty(d.skullFbx) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(d.skullFbx);
            var skullTex = string.IsNullOrEmpty(d.skullTexture) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(d.skullTexture);
            if (!AssetDatabase.IsValidFolder(MeshFolder)) AssetDatabase.CreateFolder(Path.GetDirectoryName(MeshFolder).Replace('\\', '/'), Path.GetFileName(MeshFolder));

            // materials: <name> for the bones (the core's atlas), <name>_Skull for the fixed skull's own texture
            var mats = new Dictionary<string, Material[]>();
            foreach (Mat m in d.materials ?? new Mat[0])
            {
                var src = AssetDatabase.LoadAssetAtPath<Material>(m.from);
                if (src == null) { Debug.LogWarning($"[Snow] Forest giants: material source {m.from} missing."); continue; }
                mats[m.name] = new[]
                {
                    EnsureMat($"{MeshFolder}/M_Giant_{m.name}.mat", src, null, m.color),
                    EnsureMat($"{MeshFolder}/M_Giant_{m.name}_Skull.mat", src, skullTex, m.color),
                };
            }
            Material[] MatsOf(string key) => key != null && mats.TryGetValue(key, out var v) ? v : mats.Values.FirstOrDefault();

            // the core's giant: fixed skull, the data's material
            int coreBones = 0;
            Transform core = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Giant" && t.parent != null && t.parent.name == "Giants");
            Material[] coreMats = MatsOf(d.coreMaterial);
            if (core != null)
                foreach (Transform b in core)
                {
                    var r = b.GetComponent<MeshRenderer>();
                    if (r == null) continue;
                    bool isSkull = (b.name == "B_Skull" || b.name == "B_Mandible") && skullFix != null;
                    if (isSkull)
                    {
                        Mesh fm = skullFix.transform.Find(b.name).GetComponent<MeshFilter>().sharedMesh;
                        b.GetComponent<MeshFilter>().sharedMesh = fm;
                        var mc = b.GetComponent<MeshCollider>();
                        if (mc != null) mc.sharedMesh = fm;
                    }
                    if (coreMats != null) r.sharedMaterial = coreMats[isSkull ? 1 : 0];
                    coreBones++;
                }

            // the ground first (the giants stand on it: the last run's mounds out, the pits and craters in), the last run's trees back
            int unmounded = RestoreMounds(root);
            (int heights, int holes) = ForestGroundEdit.Apply(root, d.ground, Path.ChangeExtension(DataPath, null) + "_ground.bytes", "forest_giants");
            int groundTrees = ForestGroundEdit.ClearTrees(root, d.ground, 0.5f), grass = ForestGroundEdit.ClearGrass(root, d.ground);
            (int back, int gone) = UndoTrees(root);
            var st = new TreeStore();

            Transform old = root.Find(RootName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var group = new GameObject(RootName).transform;
            group.SetParent(root, false);

            var rest = new Dictionary<string, Vector3>();
            foreach (Transform b in model.transform) rest[b.name] = b.localPosition;
            var names = rest.Keys.ToList();
            var poses = (d.poses ?? new Pose[0]).Where(p => p != null && p.name != null).ToDictionary(p => p.name);

            var report = new List<string>();
            var pieces = new List<Piece>();
            var after = new List<Action>();                            // trees, once the mounds are in
            int seed = 7;
            foreach (Giant g in d.giants ?? new Giant[0])
            {
                var joints = new List<Joint>();
                if (!string.IsNullOrEmpty(g.pose))
                {
                    if (poses.TryGetValue(g.pose, out Pose p)) joints.AddRange(p.joints ?? new Joint[0]);
                    else Debug.LogWarning($"[Snow] Forest giants: {g.name}: pose '{g.pose}' not in the data.");
                }
                if (g.extra != null) joints.AddRange(g.extra);
                var posed = Posed(rest, Turns(joints, names));
                float k = g.size / FullHeight;
                var go = Build(group, g.name, model, skullFix, posed, k, g.hide, MatsOf(g.material));
                float gy = ForestGroundEdit.Height(g.x, g.z);
                go.transform.SetPositionAndRotation(new Vector3(g.x, gy + g.dy, g.z), Quaternion.Euler(g.pitch, g.yaw, g.roll));
                int buried = Prune(go);
                Physics.SyncTransforms();
                var cols = go.GetComponentsInChildren<Collider>().ToList();
                var rs = go.GetComponentsInChildren<Renderer>();
                Bounds area = rs[0].bounds;
                foreach (Renderer r in rs) area.Encapsulate(r.bounds);
                pieces.Add(new Piece { name = g.name, size = g.size, mound = g.mound, cols = new HashSet<Collider>(cols), area = area });
                int giantSeed = seed++;
                string line = $"{g.name} {g.size:0} m {g.pose} ({rs.Length} bones, {buried} under the ground, top {area.max.y - gy:0} m over the pelvis' ground";
                Giant gg = g;
                after.Add(() =>
                {
                    int cut = CutTrees(root, cols, area, st, gg.clear);
                    int young = Plant(root, cols, area, gg.overgrown, giantSeed, st);
                    report.Add($"{line}, {cut} trees cut, {young} young)");
                });
            }

            foreach (Bone b in d.bones ?? new Bone[0])
            {
                Transform src = model.transform.Find(b.bone ?? "");
                if (src == null) { Debug.LogWarning($"[Snow] Forest giants: bone {b.bone} not in the model."); continue; }
                bool isSkull = (b.bone == "B_Skull" || b.bone == "B_Mandible") && skullFix != null;
                Mesh bm = isSkull ? skullFix.transform.Find(b.bone).GetComponent<MeshFilter>().sharedMesh : src.GetComponent<MeshFilter>().sharedMesh;
                var mat = MatsOf(b.material);
                var go = Put(group, "Bone_" + b.bone.Substring(2) + "_" + b.x.ToString("0") + "_" + b.z.ToString("0"), bm, new[] { mat[isSkull ? 1 : 0] }, true);
                float k = b.size / FullHeight;
                go.transform.localScale = Vector3.one * k;
                Quaternion q = Quaternion.Euler(b.pitch, b.yaw, b.roll);
                Vector3 centre = q * (bm.bounds.center * k);
                go.transform.SetPositionAndRotation(new Vector3(b.x, ForestGroundEdit.Height(b.x, b.z) + b.dy, b.z) - centre, q);
                Physics.SyncTransforms();
                Collider bc = go.GetComponent<Collider>();
                Bounds bb = go.GetComponent<Renderer>().bounds;
                pieces.Add(new Piece { name = go.name, size = b.size * 0.15f, mound = b.mound, cols = new HashSet<Collider> { bc }, area = bb });
                after.Add(() => CutTrees(root, new[] { bc }, bb, st));
            }

            int mounded = Mounds(root, pieces);
            foreach (Action a in after) a();

            int props = 0, missing = 0;
            foreach (Prop p in d.props ?? new Prop[0])
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(p.prefab);
                if (asset == null) { Debug.LogWarning($"[Snow] Forest giants: {p.prefab} missing."); missing++; continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, group);
                go.transform.SetPositionAndRotation(new Vector3(p.x, ForestGroundEdit.Height(p.x, p.z) + p.dy, p.z), Quaternion.Euler(p.pitch, p.yaw, p.roll));
                go.transform.localScale *= p.scale;
                props++;
            }

            File.WriteAllText(TreesPath, JsonUtility.ToJson(st, true));
            AssetDatabase.ImportAsset(TreesPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Snow] Forest giants ({DataPath}): core giant {(core != null ? $"{coreBones} bones re-skinned" : "NOT FOUND")}; " +
                      $"{report.Count} giants: {string.Join("; ", report)}; {d.bones?.Length ?? 0} loose bones; {props} props{(missing > 0 ? $", {missing} MISSING" : "")}; " +
                      $"mounds: {unmounded} samples put back, {mounded} raised; " +
                      $"ground: {heights} height samples, {holes} holes, {groundTrees} trees and {grass} grass cells cleared; " +
                      $"trees: {back} back and {gone} young removed from the last run, {st.removed.Length} cut and {st.added.Length} planted now.");
        }
    }
}
