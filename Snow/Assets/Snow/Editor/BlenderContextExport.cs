using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Snow.EditorTools
{
    /// <summary>
    /// Tools / Snow / Blender Context: a place of the open scene as the Blender agent needs it to rework models in place.
    /// Writes source/context/&lt;area&gt;/ — every visible mesh where Unity has it (OBJ per unique mesh in Blender axes, an
    /// instance list with Blender world matrices), the Terrain patch with its paint as one texture, the Terrain trees, the
    /// lamps, for every object its prefab, FBX and source hints in source/, five shots, a README for the agent — and runs
    /// Blender headless (tools/blender_context/build_context.py) to make context.blend from it. Areas are presets in
    /// tools/blender_context/areas.json or a box around the selection. Read-only for the project: the scene and assets are
    /// not changed. Axes as in every package contract: Blender (x, y, z) = Unity (-x, -z, y).
    /// </summary>
    public class BlenderContextExport : EditorWindow
    {
        const string AreasFile = "../tools/blender_context/areas.json";
        const string BuildScript = "../tools/blender_context/build_context.py";
        const string OutDir = "../source/context";
        const float MaxObjectSize = 300f;       // world models spanning the map (wires, mist) stay out
        const float PaintPxPerM = 4f;

        [Serializable] public class AreaDef { public string name, title, scene, around; public float[] centre, rect, size; public float yaw, margin; }
        [Serializable] public class AreasDef { public string blender; public AreaDef[] areas; }

        [Serializable] public class OutMesh { public string id, obj, fbx, mesh; public int vertices, triangles; public string[] materials; }
        [Serializable] public class OutInstance { public string name, path, group, root, prefab, mesh; public string[] materials; public float[] matrix; }
        [Serializable] public class OutRoot { public string id, worldId, name, path, group, prefab, fbx; public float[] unityPosition, unityEuler, unityScale, areaUV, matrix; public string[] sources; }
        [Serializable] public class OutLight { public string name, path, type; public bool powered, enabled; public float[] color, matrix; public float intensity, range, spotAngle; }
        [Serializable] public class OutTerrain { public string obj, texture; public float pxPerM; public float[] worldMin, worldMax; }
        [Serializable]
        public class OutContext
        {
            public string name, title, scene, exported, axes, note;
            public float[] centre, rect;
            public float yaw, margin;
            public OutMesh[] meshes;
            public OutRoot[] roots;
            public OutInstance[] instances, trees;
            public OutLight[] lights;
            public OutTerrain terrain;
            public string[] shots, skipped;
        }

        // ------------------------------------------------------------------ window

        Vector2 _scroll;
        float _selectionSize = 40f;
        bool _build = true;

        [MenuItem("Tools/Snow/Blender Context")]
        static void Open() => GetWindow<BlenderContextExport>("Blender Context");

        void OnGUI()
        {
            EditorGUILayout.HelpBox("Выгружает место карты для агента Blender: модели на своих местах, землю, ёлки, фонари, снимки и context.blend " +
                                    "в source/context/<место>/. Сцена и ассеты не меняются.", MessageType.Info);
            _build = EditorGUILayout.ToggleLeft("Собрать context.blend (Blender без окна)", _build);
            AreasDef def = LoadAreas();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (def?.areas != null)
                foreach (AreaDef a in def.areas)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(a.name, a.title ?? "");
                    if (GUILayout.Button("Выгрузить", GUILayout.Width(90))) Debug.Log(Export(a.name, _build));
                    EditorGUILayout.EndHorizontal();
                }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            _selectionSize = EditorGUILayout.FloatField("Вокруг выделенного, м", _selectionSize);
            using (new EditorGUI.DisabledScope(Selection.activeTransform == null))
                if (GUILayout.Button("Выгрузить", GUILayout.Width(90))) Debug.Log(ExportAroundSelection(_selectionSize, _build));
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Открыть areas.json")) EditorUtility.RevealInFinder(Full(AreasFile));
        }

        // ------------------------------------------------------------------ entry points (also for the pipeline CLI)

        public static string Export(string areaName, bool buildBlend = true)
        {
            AreasDef def = LoadAreas();
            AreaDef a = def?.areas?.FirstOrDefault(x => x.name == areaName);
            if (a == null) return $"[Snow] Blender context: no area '{areaName}' in {Full(AreasFile)}.";
            string active = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
            if (!string.IsNullOrEmpty(a.scene) && a.scene != active) return $"[Snow] Blender context: area '{areaName}' is in {a.scene}, the open scene is {active}. Open it first.";
            if (!string.IsNullOrEmpty(a.around))
            {
                GameObject go = GameObject.Find(a.around);
                if (go == null) return $"[Snow] Blender context: '{a.around}' not found in the open scene.";
                float[] s = a.size ?? new[] { 40f, 40f };
                a.centre = new[] { go.transform.position.x, go.transform.position.z };
                a.yaw = go.transform.eulerAngles.y;
                a.rect = new[] { -s[0] * 0.5f, -s[1] * 0.5f, s[0] * 0.5f, s[1] * 0.5f };
            }
            return Run(a, def.blender, buildBlend);
        }

        public static string ExportAroundSelection(float size, bool buildBlend = true)
        {
            Transform t = Selection.activeTransform;
            if (t == null) return "[Snow] Blender context: nothing selected.";
            var a = new AreaDef
            {
                name = "sel_" + Safe(t.name).ToLowerInvariant(),
                title = "Вокруг " + t.name,
                scene = t.gameObject.scene.path,
                centre = new[] { t.position.x, t.position.z },
                yaw = t.eulerAngles.y,
                rect = new[] { -size * 0.5f, -size * 0.5f, size * 0.5f, size * 0.5f },
                margin = 0f,
            };
            return Run(a, LoadAreas()?.blender, buildBlend);
        }

        // ------------------------------------------------------------------ export

        static AreaDef _a;
        static float _cos, _sin;
        static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        static string Run(AreaDef a, string blender, bool buildBlend)
        {
            _a = a;
            _cos = Mathf.Cos(a.yaw * Mathf.Deg2Rad);
            _sin = Mathf.Sin(a.yaw * Mathf.Deg2Rad);
            string dir = Full(Path.Combine(OutDir, a.name));
            string root = Full(OutDir);
            if (!dir.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return "[Snow] Blender context: bad area name " + a.name;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(Path.Combine(dir, "meshes"));
            Directory.CreateDirectory(Path.Combine(dir, "shots"));
            if (!File.Exists(Path.Combine(root, ".gitignore"))) File.WriteAllText(Path.Combine(root, ".gitignore"), "# generated by Tools / Snow / Blender Context\n*\n!.gitignore\n");

            var sw = Stopwatch.StartNew();
            var ctx = new OutContext
            {
                name = a.name, title = a.title, scene = a.scene, exported = DateTime.Now.ToString("yyyy-MM-dd HH:mm", Ci),
                centre = a.centre, yaw = a.yaw, rect = a.rect, margin = a.margin,
                axes = "Blender (x, y, z) = Unity (-x, -z, y); matrices are Blender world matrices, row-major 4x4; metres",
                note = "Generated by Snow Unity Tools / Snow / Blender Context. Do not edit: export again instead.",
            };
            var meshes = new Dictionary<Mesh, OutMesh>();
            var instances = new List<OutInstance>();
            var roots = new Dictionary<Transform, OutRoot>();
            var skipped = new List<string>();
            var sources = new SourceIndex(Full("../source"));
            try
            {
                EditorUtility.DisplayProgressBar("Blender Context", "Objects", 0.1f);
                foreach (GameObject top in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                    foreach (Renderer r in top.GetComponentsInChildren<Renderer>(false))
                    {
                        if (!(r is MeshRenderer || r is SkinnedMeshRenderer) || !r.enabled || r.name.StartsWith("__", StringComparison.Ordinal)) continue;
                        if (!InArea(r.bounds)) continue;
                        if (r.bounds.size.x > MaxObjectSize || r.bounds.size.z > MaxObjectSize) { skipped.Add(PathOf(r.transform) + " (spans the map)"); continue; }
                        if (!Lod0(r)) continue;
                        Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                        if (mesh == null) continue;
                        OutMesh m = MeshOf(mesh, r.sharedMaterials, dir, meshes);
                        Transform rootT = RootOf(r.transform);
                        if (!roots.ContainsKey(rootT)) { roots[rootT] = RootRecord(rootT, mesh, sources); roots[rootT].id = $"r{roots.Count:0000}"; }
                        instances.Add(new OutInstance
                        {
                            name = r.name, path = PathOf(r.transform), group = GroupOf(r.transform), root = roots[rootT].id, prefab = roots[rootT].prefab,
                            mesh = m.id, materials = r.sharedMaterials.Select(x => x != null ? x.name : "").ToArray(), matrix = Blender(r.localToWorldMatrix),
                        });
                    }
                EditorUtility.DisplayProgressBar("Blender Context", "Terrain trees", 0.4f);
                ctx.trees = Trees(dir, meshes).ToArray();
                EditorUtility.DisplayProgressBar("Blender Context", "Terrain", 0.55f);
                ctx.terrain = TerrainPatch(dir);
                ctx.lights = Lights().ToArray();
                EditorUtility.DisplayProgressBar("Blender Context", "Shots", 0.7f);
                ctx.shots = Shots(dir).ToArray();
            }
            finally { EditorUtility.ClearProgressBar(); }
            ctx.meshes = meshes.Values.ToArray();
            ctx.instances = instances.ToArray();
            ctx.roots = roots.Values.OrderBy(x => x.group).ThenBy(x => x.path).ToArray();
            ctx.skipped = skipped.ToArray();
            string json = Path.Combine(dir, "context.json");
            File.WriteAllText(json, JsonUtility.ToJson(ctx, true));
            File.WriteAllText(Path.Combine(dir, "README.md"), Readme(ctx), new UTF8Encoding(false));
            File.Copy(Full(BuildScript), Path.Combine(dir, "build_context.py"), true);

            string blend = "not built";
            if (buildBlend)
            {
                EditorUtility.DisplayProgressBar("Blender Context", "Blender: context.blend", 0.85f);
                try { blend = BuildBlend(blender, dir); }
                finally { EditorUtility.ClearProgressBar(); }
            }
            return $"[Snow] Blender context '{a.name}' -> {dir}: {ctx.roots.Length} objects, {ctx.instances.Length} mesh instances of {ctx.meshes.Length} meshes, " +
                   $"{ctx.trees.Length} Terrain trees, {ctx.lights.Length} lights, terrain {(ctx.terrain != null ? "yes" : "no")}, {ctx.shots.Length} shots, " +
                   $"{skipped.Count} skipped; context.blend: {blend}; {sw.Elapsed.TotalSeconds:0} s.";
        }

        // ------------------------------------------------------------------ frame and helpers

        static string Full(string rel) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", rel)).Replace('\\', '/');
        static string Safe(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        static string F(float v) => v.ToString("0.#####", Ci);

        static AreasDef LoadAreas()
        {
            string p = Full(AreasFile);
            return File.Exists(p) ? JsonUtility.FromJson<AreasDef>(File.ReadAllText(p)) : null;
        }

        static Vector2 Local(Vector3 w)
        {
            float dx = w.x - _a.centre[0], dz = w.z - _a.centre[1];
            return new Vector2(dx * _cos - dz * _sin, dx * _sin + dz * _cos);
        }

        static Vector3 World(float u, float v) => new Vector3(_a.centre[0] + u * _cos + v * _sin, 0f, _a.centre[1] - u * _sin + v * _cos);

        static bool InArea(Bounds b)
        {
            float u0 = float.MaxValue, u1 = float.MinValue, v0 = float.MaxValue, v1 = float.MinValue;
            foreach (float x in new[] { b.min.x, b.max.x })
                foreach (float z in new[] { b.min.z, b.max.z })
                {
                    Vector2 p = Local(new Vector3(x, 0f, z));
                    u0 = Mathf.Min(u0, p.x); u1 = Mathf.Max(u1, p.x); v0 = Mathf.Min(v0, p.y); v1 = Mathf.Max(v1, p.y);
                }
            float m = _a.margin;
            return u1 >= _a.rect[0] - m && u0 <= _a.rect[2] + m && v1 >= _a.rect[1] - m && v0 <= _a.rect[3] + m;
        }

        static bool InArea(Vector3 p)
        {
            Vector2 l = Local(p);
            float m = _a.margin;
            return l.x >= _a.rect[0] - m && l.x <= _a.rect[2] + m && l.y >= _a.rect[1] - m && l.y <= _a.rect[3] + m;
        }

        /// <summary>World-axis box of the area with its margin (x0, z0, x1, z1).</summary>
        static Vector4 WorldBox()
        {
            float m = _a.margin, x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
            foreach (float u in new[] { _a.rect[0] - m, _a.rect[2] + m })
                foreach (float v in new[] { _a.rect[1] - m, _a.rect[3] + m })
                {
                    Vector3 w = World(u, v);
                    x0 = Mathf.Min(x0, w.x); x1 = Mathf.Max(x1, w.x); z0 = Mathf.Min(z0, w.z); z1 = Mathf.Max(z1, w.z);
                }
            return new Vector4(x0, z0, x1, z1);
        }

        static bool Lod0(Renderer r)
        {
            LODGroup g = r.GetComponentInParent<LODGroup>();
            if (g == null) return true;
            LOD[] lods = g.GetLODs();
            for (int i = 1; i < lods.Length; i++)
                if (lods[i].renderers.Contains(r) && !lods[0].renderers.Contains(r)) return false;
            return true;
        }

        static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (; t != null; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>The group an object belongs to: the second level of the scene (Forest/Structures → "Structures").</summary>
        static string GroupOf(Transform t)
        {
            string[] p = PathOf(t).Split('/');
            return p.Length > 2 ? p[1] : p[0];
        }

        /// <summary>The placed object a renderer belongs to: the outermost prefab instance below the group, else the
        /// object right below the group.</summary>
        static Transform RootOf(Transform t)
        {
            Transform best = null;
            for (Transform c = t; c != null && c.parent != null; c = c.parent)
            {
                if (c.parent.parent == null) break;                                  // c is the group (second level)
                if (PrefabUtility.IsAnyPrefabInstanceRoot(c.gameObject)) best = c;
                if (c.parent.parent.parent == null && best == null) best = c;        // right below the group
            }
            return best ?? t;
        }

        // Blender = C * Unity, C = (x, y, z) -> (-x, -z, y); C is a reflection, so a world matrix becomes C M C^T
        static readonly Matrix4x4 C = new Matrix4x4(new Vector4(-1, 0, 0, 0), new Vector4(0, 0, 1, 0), new Vector4(0, -1, 0, 0), new Vector4(0, 0, 0, 1));
        static Vector3 B(Vector3 u) => new Vector3(-u.x, -u.z, u.y);

        static float[] Blender(Matrix4x4 m)
        {
            Matrix4x4 b = C * m * C.transpose;
            var r = new float[16];
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++) r[i * 4 + j] = b[i, j];
            return r;
        }

        // ------------------------------------------------------------------ meshes

        static OutMesh MeshOf(Mesh mesh, Material[] mats, string dir, Dictionary<Mesh, OutMesh> table)
        {
            if (table.TryGetValue(mesh, out OutMesh known)) return known;
            string id = $"{table.Count:0000}_{Safe(mesh.name)}";
            if (id.Length > 60) id = id.Substring(0, 60);
            var m = new OutMesh
            {
                id = id, obj = $"meshes/{id}.obj", fbx = AssetDatabase.GetAssetPath(mesh), mesh = mesh.name,
                vertices = mesh.vertexCount, triangles = (int)(mesh.GetIndexCount(0) / 3), materials = mats.Select(x => x != null ? x.name : "").ToArray(),
            };
            WriteObj(Path.Combine(dir, m.obj), id, mesh.vertices, mesh.normals, mesh.uv, Enumerable.Range(0, mesh.subMeshCount).Select(mesh.GetTriangles).ToArray(), mats);
            m.triangles = Enumerable.Range(0, mesh.subMeshCount).Sum(s => (int)mesh.GetIndexCount(s) / 3);
            table[mesh] = m;
            return m;
        }

        /// <summary>OBJ in Blender axes: positions and normals through C, triangle winding reversed (C mirrors), one
        /// usemtl group per submesh, an MTL with the base colour and the base texture (absolute path in the project).</summary>
        static void WriteObj(string path, string id, Vector3[] v, Vector3[] n, Vector2[] uv, int[][] subs, Material[] mats)
        {
            var sb = new StringBuilder(v.Length * 60);
            sb.Append("# Snow Blender Context, Blender axes\nmtllib ").Append(id).Append(".mtl\no ").Append(id).Append('\n');
            foreach (Vector3 p in v) { Vector3 b = B(p); sb.Append("v ").Append(F(b.x)).Append(' ').Append(F(b.y)).Append(' ').Append(F(b.z)).Append('\n'); }
            bool hasN = n != null && n.Length == v.Length, hasUv = uv != null && uv.Length == v.Length;
            if (hasUv) foreach (Vector2 t in uv) sb.Append("vt ").Append(F(t.x)).Append(' ').Append(F(t.y)).Append('\n');
            if (hasN) foreach (Vector3 p in n) { Vector3 b = B(p); sb.Append("vn ").Append(F(b.x)).Append(' ').Append(F(b.y)).Append(' ').Append(F(b.z)).Append('\n'); }
            var mtl = new StringBuilder();
            for (int s = 0; s < subs.Length; s++)
            {
                Material mat = mats != null && s < mats.Length ? mats[s] : null;
                string mname = mat != null ? Safe(mat.name) : "none";
                sb.Append("usemtl ").Append(mname).Append('\n');
                if (!mtl.ToString().Contains("newmtl " + mname + "\n")) mtl.Append(Mtl(mat, mname));
                int[] t = subs[s];
                for (int k = 0; k + 2 < t.Length; k += 3)
                {
                    sb.Append('f');
                    foreach (int i in new[] { t[k + 2], t[k + 1], t[k] })
                    {
                        int j = i + 1;
                        sb.Append(' ').Append(j);
                        if (hasUv || hasN) sb.Append('/').Append(hasUv ? j.ToString(Ci) : "").Append(hasN ? "/" + j.ToString(Ci) : "");
                    }
                    sb.Append('\n');
                }
            }
            File.WriteAllText(path, sb.ToString());
            File.WriteAllText(Path.ChangeExtension(path, ".mtl"), mtl.ToString());
        }

        static string Mtl(Material mat, string name)
        {
            var sb = new StringBuilder();
            sb.Append("newmtl ").Append(name).Append('\n');
            Color c = Color.white;
            Texture tex = null;
            if (mat != null)
            {
                if (mat.HasProperty("_BaseColor")) c = mat.GetColor("_BaseColor"); else if (mat.HasProperty("_Color")) c = mat.GetColor("_Color");
                if (mat.HasProperty("_BaseMap")) tex = mat.GetTexture("_BaseMap");
                if (tex == null && mat.HasProperty("_MainTex")) tex = mat.GetTexture("_MainTex");
            }
            sb.Append("Kd ").Append(F(c.r)).Append(' ').Append(F(c.g)).Append(' ').Append(F(c.b)).Append('\n');
            if (c.a < 0.999f) sb.Append("d ").Append(F(c.a)).Append('\n');
            string tp = tex != null ? AssetDatabase.GetAssetPath(tex) : null;
            if (!string.IsNullOrEmpty(tp) && !tp.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) sb.Append("map_Kd ").Append(Full(tp)).Append('\n');
            return sb.ToString();
        }

        // ------------------------------------------------------------------ objects, sources

        static OutRoot RootRecord(Transform t, Mesh firstMesh, SourceIndex sources)
        {
            string prefab = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject) ?? "";
            string fbx = AssetDatabase.GetAssetPath(firstMesh);
            string stem = prefab.Length > 0 ? Path.GetFileNameWithoutExtension(prefab) : t.name;
            return new OutRoot
            {
                name = t.name, path = PathOf(t), group = GroupOf(t), prefab = prefab, fbx = fbx,
                worldId = t.TryGetComponent(out Snow.World.WorldId wid) ? wid.id : "",     // only the object itself: its moves travel back (WorldSync)
                unityPosition = new[] { t.position.x, t.position.y, t.position.z },
                unityEuler = new[] { t.eulerAngles.x, t.eulerAngles.y, t.eulerAngles.z },
                unityScale = new[] { t.lossyScale.x, t.lossyScale.y, t.lossyScale.z },
                areaUV = new[] { Local(t.position).x, Local(t.position).y },
                matrix = Blender(t.localToWorldMatrix),
                sources = sources.Hints(stem, Path.GetFileNameWithoutExtension(fbx)),
            };
        }

        /// <summary>Where a model's source may be: files in source/ named like the prefab or its FBX, and the packages whose
        /// contract json names the prefab (their .blend).</summary>
        class SourceIndex
        {
            readonly Dictionary<string, List<string>> _byStem = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            readonly List<(string dir, string text)> _contracts = new List<(string, string)>();

            public SourceIndex(string root)
            {
                if (!Directory.Exists(root)) return;
                foreach (string f in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
                {
                    string p = f.Replace('\\', '/');
                    if (p.Contains("/context/") || p.Contains("/_to_delete/") || p.Contains("/.git/")) continue;
                    string ext = Path.GetExtension(p).ToLowerInvariant();
                    if (ext != ".blend" && ext != ".fbx" && ext != ".json") continue;
                    string stem = Path.GetFileNameWithoutExtension(p);
                    if (!_byStem.TryGetValue(stem, out var list)) _byStem[stem] = list = new List<string>();
                    list.Add(p);
                    if (ext == ".json" && new FileInfo(p).Length < 4_000_000 && p.Contains("/assets/"))
                        _contracts.Add((Path.GetDirectoryName(p).Replace('\\', '/'), File.ReadAllText(p)));
                }
            }

            public string[] Hints(string prefab, string fbx)
            {
                var r = new List<string>();
                foreach (string s in new[] { prefab, fbx })
                    if (!string.IsNullOrEmpty(s) && _byStem.TryGetValue(s, out var list)) r.AddRange(list.Where(p => !p.EndsWith(".json")));
                foreach (var c in _contracts)
                    if (c.text.Contains("\"" + prefab + "\""))
                        r.AddRange(Directory.GetFiles(c.dir, "*.blend").Select(p => p.Replace('\\', '/')));
                return r.Distinct().Take(6).ToArray();
            }
        }

        // ------------------------------------------------------------------ terrain

        static IEnumerable<Terrain> Terrains()
        {
            Vector4 box = WorldBox();
            foreach (Terrain t in Terrain.activeTerrains)
            {
                if (t == null || t.terrainData == null) continue;
                Vector3 o = t.transform.position, s = t.terrainData.size;
                if (!(box.z < o.x || box.x > o.x + s.x || box.w < o.z || box.y > o.z + s.z)) yield return t;
            }
        }

        static Terrain TerrainAt(Terrain[] ts, float x, float z)
        {
            foreach (Terrain t in ts)
            {
                Vector3 o = t.transform.position, s = t.terrainData.size;
                if (x >= o.x && z >= o.z && x <= o.x + s.x && z <= o.z + s.z) return t;
            }
            return null;
        }

        static OutTerrain TerrainPatch(string dir)
        {
            Terrain[] ts = Terrains().ToArray();
            if (ts.Length == 0) return null;
            Vector4 box = WorldBox();
            TerrainData td0 = ts[0].terrainData;
            float step = td0.size.x / (td0.heightmapResolution - 1);
            int nx = Mathf.CeilToInt((box.z - box.x) / step) + 1, nz = Mathf.CeilToInt((box.w - box.y) / step) + 1;
            var v = new Vector3[nx * nz];
            var uv = new Vector2[nx * nz];
            for (int z = 0; z < nz; z++)
                for (int x = 0; x < nx; x++)
                {
                    float wx = Mathf.Min(box.x + x * step, box.z), wz = Mathf.Min(box.y + z * step, box.w);
                    Terrain t = TerrainAt(ts, wx, wz);
                    float wy = t != null ? t.transform.position.y + t.SampleHeight(new Vector3(wx, 0f, wz)) : 0f;
                    v[z * nx + x] = new Vector3(wx, wy, wz);
                    uv[z * nx + x] = new Vector2((wx - box.x) / (box.z - box.x), (wz - box.y) / (box.w - box.y));
                }
            var tris = new List<int>((nx - 1) * (nz - 1) * 6);
            for (int z = 0; z < nz - 1; z++)
                for (int x = 0; x < nx - 1; x++)
                {
                    int a = z * nx + x, b = a + nx, c = a + 1, d = b + 1;
                    tris.Add(a); tris.Add(b); tris.Add(c);                           // clockwise from above (Unity front face)
                    tris.Add(c); tris.Add(b); tris.Add(d);
                }
            string tex = PaintTexture(ts, box, dir);
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { name = "Terrain_Paint" };
            WriteObj(Path.Combine(dir, "terrain.obj"), "terrain", v, null, uv, new[] { tris.ToArray() }, new[] { mat });
            // the MTL points at the paint next to it
            File.WriteAllText(Path.Combine(dir, "terrain.mtl"), "newmtl Terrain_Paint\nKd 1 1 1\nmap_Kd terrain_paint.png\n");
            UnityEngine.Object.DestroyImmediate(mat);
            return new OutTerrain { obj = "terrain.obj", texture = tex, pxPerM = PaintPxPerM, worldMin = new[] { box.x, box.y }, worldMax = new[] { box.z, box.w } };
        }

        /// <summary>The ground paint as one picture: per pixel the Terrain layers' textures (tiled as on the Terrain) mixed
        /// by the alphamap weights.</summary>
        static string PaintTexture(Terrain[] ts, Vector4 box, string dir)
        {
            int w = Mathf.CeilToInt((box.z - box.x) * PaintPxPerM), h = Mathf.CeilToInt((box.w - box.y) * PaintPxPerM);
            var img = new Texture2D(w, h, TextureFormat.RGB24, false);
            var px = new Color[w * h];
            var layerTex = new Dictionary<TerrainLayer, Texture2D>();
            var maps = new Dictionary<Terrain, float[,,]>();
            Texture2D Readable(TerrainLayer l)
            {
                if (layerTex.TryGetValue(l, out Texture2D t)) return t;
                string p = l.diffuseTexture != null ? AssetDatabase.GetAssetPath(l.diffuseTexture) : null;
                t = null;
                if (!string.IsNullOrEmpty(p) && File.Exists(p))
                {
                    t = new Texture2D(2, 2) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
                    if (!t.LoadImage(File.ReadAllBytes(p))) t = null;
                }
                return layerTex[l] = t;
            }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float wx = box.x + (x + 0.5f) / PaintPxPerM, wz = box.y + (y + 0.5f) / PaintPxPerM;
                    Terrain t = TerrainAt(ts, wx, wz);
                    if (t == null) { px[y * w + x] = Color.black; continue; }
                    TerrainData td = t.terrainData;
                    if (!maps.TryGetValue(t, out float[,,] map)) maps[t] = map = td.GetAlphamaps(0, 0, td.alphamapWidth, td.alphamapHeight);
                    Vector3 o = t.transform.position;
                    int ax = Mathf.Clamp((int)((wx - o.x) / td.size.x * td.alphamapWidth), 0, td.alphamapWidth - 1);
                    int az = Mathf.Clamp((int)((wz - o.z) / td.size.z * td.alphamapHeight), 0, td.alphamapHeight - 1);
                    Color c = Color.black;
                    TerrainLayer[] layers = td.terrainLayers;
                    for (int l = 0; l < layers.Length; l++)
                    {
                        float a = map[az, ax, l];
                        if (a <= 0.001f || layers[l] == null) continue;
                        Texture2D lt = Readable(layers[l]);
                        Vector2 tile = layers[l].tileSize;
                        Color s = lt != null ? lt.GetPixelBilinear((wx - o.x + layers[l].tileOffset.x) / tile.x, (wz - o.z + layers[l].tileOffset.y) / tile.y) : Color.gray;
                        c += s * a;
                    }
                    px[y * w + x] = c;
                }
            img.SetPixels(px);
            img.Apply();
            File.WriteAllBytes(Path.Combine(dir, "terrain_paint.png"), img.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(img);
            foreach (Texture2D t in layerTex.Values) if (t != null) UnityEngine.Object.DestroyImmediate(t);
            return "terrain_paint.png";
        }

        static IEnumerable<OutInstance> Trees(string dir, Dictionary<Mesh, OutMesh> meshes)
        {
            foreach (Terrain t in Terrains())
            {
                TerrainData td = t.terrainData;
                Vector3 o = t.transform.position, s = td.size;
                TreePrototype[] protos = td.treePrototypes;
                var parts = new Dictionary<int, List<(Mesh mesh, Material[] mats, Matrix4x4 local, string name)>>();
                foreach (TreeInstance ti in td.treeInstances)
                {
                    Vector3 w = o + Vector3.Scale(ti.position, s);
                    if (!InArea(w)) continue;
                    if (!parts.TryGetValue(ti.prototypeIndex, out var list))
                    {
                        list = new List<(Mesh, Material[], Matrix4x4, string)>();
                        GameObject pf = protos[ti.prototypeIndex].prefab;
                        if (pf != null)
                            foreach (MeshRenderer r in pf.GetComponentsInChildren<MeshRenderer>(true))
                            {
                                if (!Lod0(r)) continue;
                                Mesh m = r.GetComponent<MeshFilter>()?.sharedMesh;
                                if (m != null) list.Add((m, r.sharedMaterials, pf.transform.worldToLocalMatrix * r.transform.localToWorldMatrix, pf.name));
                            }
                        parts[ti.prototypeIndex] = list;
                    }
                    Matrix4x4 at = Matrix4x4.TRS(w, Quaternion.Euler(0f, ti.rotation * Mathf.Rad2Deg, 0f), new Vector3(ti.widthScale, ti.heightScale, ti.widthScale));
                    foreach (var p in list)
                    {
                        OutMesh m = MeshOf(p.mesh, p.mats, dir, meshes);
                        yield return new OutInstance
                        {
                            name = p.name, path = "Terrain trees/" + p.name, group = "TerrainTrees", root = "", prefab = AssetDatabase.GetAssetPath(protos[ti.prototypeIndex].prefab),
                            mesh = m.id, materials = p.mats.Select(x => x != null ? x.name : "").ToArray(), matrix = Blender(at * p.local),
                        };
                    }
                }
            }
        }

        static IEnumerable<OutLight> Lights()
        {
            foreach (GameObject top in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (Light l in top.GetComponentsInChildren<Light>(true))
                {
                    if (l.type == LightType.Directional || !InArea(l.transform.position)) continue;
                    yield return new OutLight
                    {
                        name = l.name, path = PathOf(l.transform), type = l.type.ToString(), powered = l.name.StartsWith("Light_power", StringComparison.Ordinal),
                        enabled = l.enabled && l.gameObject.activeInHierarchy, color = new[] { l.color.r, l.color.g, l.color.b },
                        intensity = l.intensity, range = l.range, spotAngle = l.spotAngle, matrix = Blender(l.transform.localToWorldMatrix),
                    };
                }
        }

        // ------------------------------------------------------------------ shots

        /// <summary>Top view and four views from the corners, flat bright light and no fog so the layout reads.</summary>
        static IEnumerable<string> Shots(string dir)
        {
            float m = _a.margin, u0 = _a.rect[0] - m, u1 = _a.rect[2] + m, v0 = _a.rect[1] - m, v1 = _a.rect[3] + m;
            float uc = (u0 + u1) * 0.5f, vc = (v0 + v1) * 0.5f, size = Mathf.Max(u1 - u0, v1 - v0);
            Vector3 centre = World(uc, vc);
            centre.y = GroundY(centre);
            var shots = new List<(string name, Vector3 pos, Vector3 look, bool top)>
            {
                ("top", centre + Vector3.up * (size + 60f), centre, true),
                ("view_nw", World(u0, v1) + Vector3.up * (centre.y + size * 0.35f), centre, false),
                ("view_ne", World(u1, v1) + Vector3.up * (centre.y + size * 0.35f), centre, false),
                ("view_se", World(u1, v0) + Vector3.up * (centre.y + size * 0.35f), centre, false),
                ("view_sw", World(u0, v0) + Vector3.up * (centre.y + size * 0.35f), centre, false),
            };
            var go = new GameObject("__ContextCam");
            var cam = go.AddComponent<Camera>();
            Color amb = RenderSettings.ambientLight;
            UnityEngine.Rendering.AmbientMode mode = RenderSettings.ambientMode;
            bool fog = RenderSettings.fog;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(1.1f, 1.1f, 1.1f);
            RenderSettings.fog = false;
            var result = new List<string>();
            try
            {
                foreach (var s in shots)
                {
                    int w = s.top ? 1600 : 1600, h = s.top ? 1600 : 900;
                    var rt = new RenderTexture(w, h, 24);
                    cam.targetTexture = rt;
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(0.5f, 0.55f, 0.6f);
                    cam.orthographic = s.top;
                    cam.orthographicSize = size * 0.5f;
                    cam.fieldOfView = 50f;
                    cam.nearClipPlane = 0.3f;
                    cam.farClipPlane = size * 4f + 200f;
                    go.transform.position = s.pos;
                    if (s.top) go.transform.rotation = Quaternion.Euler(90f, _a.yaw, 0f); else go.transform.LookAt(s.look);
                    cam.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;
                    File.WriteAllBytes(Path.Combine(dir, "shots", s.name + ".png"), tex.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(tex);
                    cam.targetTexture = null;
                    rt.Release();
                    result.Add("shots/" + s.name + ".png");
                }
            }
            finally
            {
                RenderSettings.ambientMode = mode;
                RenderSettings.ambientLight = amb;
                RenderSettings.fog = fog;
                UnityEngine.Object.DestroyImmediate(go);
            }
            return result;
        }

        static float GroundY(Vector3 w)
        {
            Terrain t = TerrainAt(Terrain.activeTerrains, w.x, w.z);
            return t != null ? t.transform.position.y + t.SampleHeight(w) : 0f;
        }

        // ------------------------------------------------------------------ Blender

        static string BuildBlend(string blender, string dir)
        {
            string exe = Environment.GetEnvironmentVariable("SNOW_BLENDER");
            if (string.IsNullOrEmpty(exe)) exe = blender;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return $"Blender not found ({exe}); set 'blender' in areas.json or SNOW_BLENDER";
            string log = Path.Combine(dir, "blender_build.log");
            var psi = new ProcessStartInfo(exe, $"-b --factory-startup --python \"{Path.Combine(dir, "build_context.py")}\" -- \"{Path.Combine(dir, "context.json")}\" \"{Path.Combine(dir, "context.blend")}\"")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            };
            var output = new StringBuilder();
            using (var p = new Process { StartInfo = psi })
            {
                // both streams read as they come: a full stderr pipe would otherwise stall Blender
                p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                bool done = p.WaitForExit(900_000);
                if (done) p.WaitForExit();
                else p.Kill();
                lock (output) File.WriteAllText(log, output.ToString());
                if (!done) return "Blender timed out (see blender_build.log)";
            }
            return File.Exists(Path.Combine(dir, "context.blend")) ? "context.blend" : "FAILED (see blender_build.log)";
        }

        // ------------------------------------------------------------------ README

        static string Readme(OutContext c)
        {
            var sb = new StringBuilder();
            sb.Append($"# Контекст места «{c.title ?? c.name}» для Blender\n\n");
            sb.Append($"Выгружено из Unity {c.exported}, сцена `{c.scene}`. Это копия того, что стоит в игре; править здесь нечего — ");
            sb.Append("файл пересоздаётся каждой выгрузкой (Unity: Tools / Snow / Blender Context).\n\n");
            sb.Append("## Что внутри\n");
            sb.Append("- `context.blend` — готовая сцена (если собралась; иначе запусти в Blender `build_context.py`, см. ниже). Коллекции по группам сцены Unity: ");
            sb.Append(string.Join(", ", c.roots.Select(r => r.group).Concat(c.trees.Length > 0 ? new[] { "TerrainTrees" } : new string[0]).Distinct())).Append(", Terrain, Lights, Cameras.\n");
            sb.Append($"- `context.json` — все объекты: {c.roots.Length} размещённых объектов, {c.instances.Length} мешей на сцене из {c.meshes.Length} разных, {c.trees.Length} деревьев Terrain, {c.lights.Length} ламп.\n");
            sb.Append("- `meshes/*.obj` — меши, как их видит Unity (LOD0), уже в осях Blender; текстуры — ссылками на файлы проекта.\n");
            sb.Append("- `terrain.obj` + `terrain_paint.png` — земля участка с покраской (дороги, гравий, тропы).\n");
            sb.Append("- `shots/` — вид сверху и четыре вида с углов (ровный свет, без тумана).\n\n");
            sb.Append("## Оси и место\n");
            sb.Append("Blender (x, y, z) = Unity (−x, −z, y), метры, как во всех контрактах пакетов: север карты (Unity +z) в Blender — это −Y, ");
            sb.Append("восток (Unity +x) — −X. Мировые координаты Unity не сдвинуты: ");
            sb.Append($"центр участка — Unity ({F(c.centre[0])}, {F(c.centre[1])}) по x, z, поворот рамки участка {F(c.yaw)}°.\n");
            sb.Append("У каждого объекта в Blender есть свойства (Object Properties → Custom Properties): `unity_path`, `prefab`, `fbx`, `sources` ");
            sb.Append("(где искать исходник в source/), а у корней ещё `unity_position`, `unity_euler`.\n\n");
            sb.Append("## Как дорабатывать модель\n");
            sb.Append("1. Найди объект в context.blend, посмотри `sources` — там .blend пакета, из которого он сделан.\n");
            sb.Append("2. Правь модель в её исходнике (не здесь). Чтобы видеть её на месте: в своём .blend сделай File → Link этой сцены, ");
            sb.Append("или наоборот — Link своей коллекции сюда и поставь корень в матрицу корня объекта (`roots[].matrix` в context.json).\n");
            sb.Append("3. Не меняй имена корней, точки поворота, детали `IX_` (оси и ход), `COL_` — на них держится механика в Unity.\n");
            sb.Append("4. Сдача — как обычно: пакет в source/assets/<pack>/ с контрактом. Если объект надо передвинуть — напиши новые места ");
            sb.Append("(Unity-координаты или координаты рамки участка) в контракт отдельной таблицей; в игру их переносит Claude Code.\n\n");
            sb.Append("## Пересобрать context.blend вручную\n");
            sb.Append("`blender -b --factory-startup --python build_context.py -- context.json context.blend` (из этой папки) или в открытом Blender: ");
            sb.Append("Scripting → открыть build_context.py → Run Script (соберёт в текущую сцену).\n");
            if (c.skipped.Length > 0) sb.Append("\n## Не выгружено\n").Append(string.Join("\n", c.skipped.Select(s => "- " + s))).Append('\n');
            sb.Append("\n## Объекты\n| Объект | Группа | Префаб | Исходник (подсказка) |\n|---|---|---|---|\n");
            string src = Full("../source") + "/";
            foreach (var g in c.roots.GroupBy(r => (r.name, r.group, prefab: Path.GetFileName(r.prefab), source: r.sources.Length > 0 ? r.sources[0].Replace(src, "source/") : "—")))
                sb.Append($"| {g.Key.name}{(g.Count() > 1 ? $" ×{g.Count()}" : "")} | {g.Key.group} | {g.Key.prefab} | {g.Key.source} |\n");
            return sb.ToString();
        }
    }
}
