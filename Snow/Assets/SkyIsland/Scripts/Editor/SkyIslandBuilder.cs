using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SkyIsland.EditorTools
{
    public enum Pipe { BuiltIn, URP, HDRP }

    /// <summary>
    /// Tools > Sky Island > Build Scene : creates materials for the active render pipeline, prefabs, the whole
    /// location (island, airfield, buildings, towers, vegetation, lights) and the fog / sky / post-processing look.
    /// The same builder makes the other locations (Assets/**/Locations/&lt;Id&gt;, data contract v2):
    /// BuildLocation(id), BuildLocationFromCommandLine() and Tools > Locations > Location Builder.
    /// </summary>
    public static partial class SkyIslandBuilder
    {
        const string Menu = "Tools/Sky Island/";

        /// <summary>Texture / model names with this prefix live in the SkyIsland package (e.g. "shared:T_Giant_A.png").</summary>
        public const string SharedPrefix = "shared:";

        /// <summary>Id (file prefix) of the original island: v1 data with its legacy defaults.</summary>
        public const string SkyIslandId = "SkyIsland";

        /// <summary>Environment textures a location may leave out: the SkyIsland copy is used instead.</summary>
        static readonly HashSet<string> CoreFallbackTextures = new HashSet<string> { "T_SkyClouds.png", "T_FogNoise.png", SmokeTexture };

        static string s_ProgressTitle = "Sky Island";

        // ------------------------------------------------------------------ menu
        [MenuItem(Menu + "Build Scene", priority = 1)]
        public static void BuildSceneMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildScene();
        }

        [MenuItem(Menu + "Rebuild Materials", priority = 20)]
        public static void RebuildMaterialsMenu()
        {
            var ctx = Context.Load();
            SkyIslandImport.FixAll(ctx.root);
            var mats = BuildMaterials(ctx);
            RemapModels(ctx, mats);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SkyIsland] {mats.Count} materials rebuilt for {ctx.pipe}.");
        }

        /// <summary>Re-applies fog / sky / sun / post of the open scene's location (scene name = location id; Sky Island otherwise).</summary>
        [MenuItem(Menu + "Apply Look To Open Scene", priority = 21)]
        public static void ApplyLookMenu()
        {
            string id = OpenLocationId();
            var ctx = id != null ? Context.Load(FindLocationRoot(id), id) : Context.Load();
            var mats = LoadMaterials(ctx);
            var root = GameObject.Find(ctx.id);
            ApplyLook(ctx, mats, root != null ? root.transform : null, Camera.main);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        [MenuItem(Menu + "Fix Import Settings", priority = 40)]
        public static void FixImportMenu()
        {
            var ctx = Context.Load();
            int n = SkyIslandImport.FixAll(ctx.root);
            Debug.Log($"[SkyIsland] import settings updated on {n} assets.");
        }

        // ------------------------------------------------------------------ context
        public class Context
        {
            /// <summary>Location id = data file prefix (Data/&lt;id&gt;_Layout.json), also the scene and root object name.</summary>
            public string id = SkyIslandId;
            public string root;
            /// <summary>SkyIsland package root: "shared:" textures / models and the environment texture fallbacks.</summary>
            public string coreRoot;
            public Pipe pipe;
            public LayoutData layout;
            public MaterialsData materials;
            public LookData look;
            public SkyIslandModelRules rules = SkyIslandModelRules.Default;
            /// <summary>Values used when the data leaves a v2 field out (Sky Island: its v1 island look).</summary>
            public LocationDefaults defaults = LocationDefaults.Island;

            public bool IsSkyIsland => id == SkyIslandId;

            /// <summary>Asset path of a texture: "shared:X" = SkyIsland/Textures/X; T_SkyClouds / T_FogNoise / T_SmokePuff
            /// fall back to the SkyIsland copy when the location does not ship them.</summary>
            public string Tex(string f)
            {
                if (string.IsNullOrEmpty(f)) return null;
                if (f.StartsWith(SharedPrefix, StringComparison.Ordinal))
                    return (coreRoot ?? root) + "/Textures/" + f.Substring(SharedPrefix.Length);
                string local = root + "/Textures/" + f;
                if (coreRoot != null && coreRoot != root && CoreFallbackTextures.Contains(f) && !File.Exists(local))
                {
                    string core = coreRoot + "/Textures/" + f;
                    if (File.Exists(core)) return core;
                }
                return local;
            }

            public string MatFolder => root + "/Materials/" + pipe;
            public string PrefabFolder => root + "/Prefabs";
            public string ScenePath => LocationScenePath(root, id);
            /// <summary>Name of a generated asset of this location, e.g. "Town_Skybox" (Sky Island keeps its v1 names).</summary>
            public string AssetName(string suffix) => id + "_" + suffix;

            /// <summary>Sky Island (v1 data, legacy defaults).</summary>
            public static Context Load() => Load(FindRoot(), SkyIslandId);

            /// <summary>Same as SkyIslandBuilder.FindLocations(): (id, folder) of every location in the project.</summary>
            public static List<(string id, string root)> FindLocations() => SkyIslandBuilder.FindLocations();

            /// <summary>Any location: &lt;root&gt;/Data/&lt;id&gt;_Layout.json, _Materials.json, _Look.json.</summary>
            public static Context Load(string root, string id)
            {
                if (string.IsNullOrEmpty(root)) throw new ArgumentException("[SkyIsland] location folder is empty.");
                if (string.IsNullOrEmpty(id)) throw new ArgumentException("[SkyIsland] location id is empty.");
                root = root.Replace('\\', '/').TrimEnd('/');
                var c = new Context { id = id, root = root, pipe = DetectPipeline(), defaults = LocationDefaults.For(id) };
                c.coreRoot = id == SkyIslandId ? root : FindCoreRoot();
                string layoutText = ReadData(root, id, "Layout");
                c.layout = JsonUtility.FromJson<LayoutData>(layoutText) ?? new LayoutData();
                c.layout.giants = LayoutJson.ReadGiants(layoutText);
                c.materials = JsonUtility.FromJson<MaterialsData>(ReadData(root, id, "Materials")) ?? new MaterialsData();
                if (c.materials.materials == null) c.materials.materials = new MatSpec[0];
                c.look = JsonUtility.FromJson<LookData>(ReadData(root, id, "Look")) ?? new LookData();
                c.look.FillMissing();
                c.rules = SkyIslandModelRules.From(c.layout);
                if (!string.IsNullOrEmpty(c.layout.id) && c.layout.id != id)
                    Debug.LogWarning($"[SkyIsland] {root}/Data/{id}_Layout.json declares id '{c.layout.id}'; the file name '{id}' is used.");
                if (c.coreRoot == null)
                    Debug.LogWarning("[SkyIsland] SkyIsland package folder not found: \"shared:\" textures and models cannot be resolved.");
                return c;
            }

            static string ReadData(string root, string id, string kind)
            {
                string path = $"{root}/Data/{id}_{kind}.json";
                if (!File.Exists(path)) throw new FileNotFoundException($"[SkyIsland] {path} not found.", path);
                return File.ReadAllText(path);
            }
        }

        /// <summary>
        /// What a location gets when its data leaves a v2 field out. Sky Island (v1 data) passes its island-specific
        /// values so it builds exactly as before; other locations get nothing extra.
        /// </summary>
        public sealed class LocationDefaults
        {
            /// <summary>Fog-by-altitude controller around this ellipse when look.fogByAltitude is absent.</summary>
            public bool islandFog;
            public Vector3 islandFogCenter;
            public Vector2 islandFogRadii;
            /// <summary>The three haze sheets under the island when look.haze is absent.</summary>
            public bool islandHaze;
            /// <summary>Per-chunk LOD culling only for the SkyIsland_Grass model (v1), not for every grass model.</summary>
            public bool grassLodIslandOnly;

            public static readonly LocationDefaults Island = new LocationDefaults
            {
                islandFog = true,
                islandFogCenter = new Vector3(0f, 0f, -40f),
                islandFogRadii = new Vector2(745f, 470f),
                islandHaze = true,
                grassLodIslandOnly = true,
            };

            public static readonly LocationDefaults None = new LocationDefaults();

            public static LocationDefaults For(string id) => id == SkyIslandId ? Island : None;
        }

        public static string FindRoot()
        {
            foreach (var guid in AssetDatabase.FindAssets("SkyIsland_Layout"))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                const string tail = "/Data/SkyIsland_Layout.json";
                if (p.EndsWith(tail)) return p.Substring(0, p.Length - tail.Length);
            }
            throw new Exception("[SkyIsland] Data/SkyIsland_Layout.json not found in the project.");
        }

        /// <summary>SkyIsland package root (shared textures / models): where Data/SkyIsland_Layout.json is, else where this script is.</summary>
        public static string FindCoreRoot()
        {
            try { return FindRoot(); }
            catch (Exception) { /* Sky Island data not imported: fall back to the scripts' folder */ }
            foreach (var guid in AssetDatabase.FindAssets("SkyIslandBuilder t:MonoScript"))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                const string tail = "/Scripts/Editor/SkyIslandBuilder.cs";
                if (p.EndsWith(tail, StringComparison.Ordinal)) return p.Substring(0, p.Length - tail.Length);
            }
            return null;
        }

        // ------------------------------------------------------------------ locations
        /// <summary>
        /// Every location of the project: folders &lt;root&gt;/Data/&lt;Id&gt;_Layout.json (Sky Island included),
        /// Sky Island first, then sorted by id and folder.
        /// </summary>
        public static List<(string id, string root)> FindLocations()
        {
            const string tail = "_Layout.json";
            var result = new List<(string id, string root)>();
            foreach (var raw in AssetDatabase.GetAllAssetPaths())
            {
                if (!raw.EndsWith(tail, StringComparison.Ordinal)) continue;
                var p = raw.Replace('\\', '/');
                int k = p.LastIndexOf("/Data/", StringComparison.Ordinal);
                if (k <= 0) continue;
                string file = p.Substring(k + "/Data/".Length);
                if (file.IndexOf('/') >= 0 || file.Length <= tail.Length) continue;
                result.Add((file.Substring(0, file.Length - tail.Length), p.Substring(0, k)));
            }
            result.Sort((a, b) =>
            {
                bool sa = a.id == SkyIslandId, sb = b.id == SkyIslandId;
                if (sa != sb) return sa ? -1 : 1;
                int c = string.CompareOrdinal(a.id, b.id);
                return c != 0 ? c : string.CompareOrdinal(a.root, b.root);
            });
            return result;
        }

        /// <summary>Folder of a location: the SkyIsland package for "SkyIsland", else Assets/**/Locations/&lt;id&gt;.</summary>
        public static string FindLocationRoot(string id)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("[SkyIsland] location id is empty.");
            if (id == SkyIslandId) return FindRoot();
            var roots = new List<string>();
            foreach (var (lid, root) in FindLocations())
                if (lid == id) roots.Add(root);
            if (roots.Count == 0)
                throw new Exception($"[SkyIsland] location '{id}' not found: expected Assets/Locations/{id}/Data/{id}_Layout.json (import the {id} package).");
            string best = roots.FirstOrDefault(r => r.EndsWith("/Locations/" + id, StringComparison.Ordinal)) ?? roots[0];
            if (roots.Count > 1)
                Debug.LogWarning($"[SkyIsland] location '{id}' exists in {roots.Count} folders ({string.Join(", ", roots)}); using {best}.");
            return best;
        }

        public static string LocationScenePath(string root, string id) => root + "/Scenes/" + id + ".unity";

        /// <summary>Id of the open scene's location when it is not Sky Island (scene name = id), else null.</summary>
        static string OpenLocationId()
        {
            string name = EditorSceneManager.GetActiveScene().name;
            if (string.IsNullOrEmpty(name) || name == SkyIslandId) return null;
            foreach (var (id, _) in FindLocations())
                if (id == name) return id;
            return null;
        }

        /// <summary>Value after "-location" on the command line.</summary>
        static string CommandLineLocation()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], "-location", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(args[i + 1]))
                    return args[i + 1];
            throw new ArgumentException("[SkyIsland] missing command line argument -location <Id> " +
                                        "(e.g. -executeMethod SkyIsland.EditorTools.SkyIslandBuilder.BuildLocationFromCommandLine -location Town).");
        }

        public static Pipe DetectPipeline()
        {
            var rp = GraphicsSettings.currentRenderPipeline;
            if (rp == null) return Pipe.BuiltIn;
            string n = rp.GetType().FullName ?? "";
            if (n.Contains("HighDefinition")) return Pipe.HDRP;
            return Pipe.URP;
        }

        static void EnsureFolder(string path)
        {
            path = path.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static bool Progress(string info, float t)
        {
            return EditorUtility.DisplayCancelableProgressBar(s_ProgressTitle, info, t);
        }

        // ------------------------------------------------------------------ build
        /// <summary>Texture files referenced by the material spec that are not in the project (package parts not imported).</summary>
        static List<string> MissingTextures(Context ctx)
        {
            var missing = new List<string>();
            void Check(string f)
            {
                if (string.IsNullOrEmpty(f) || missing.Contains(f)) return;
                if (!File.Exists(ctx.Tex(f))) missing.Add(f);
            }
            foreach (var m in ctx.materials.materials)
            {
                if (m == null) continue;
                Check(m.albedo); Check(m.normal); Check(m.mask); Check(m.emissionMap);
                if (m.detail != null) { Check(m.detail.albedo); Check(m.detail.normal); }
            }
            return missing;
        }

        /// <summary>Sky Island: Data/SkyIsland_*.json (v1) into Scenes/SkyIsland.unity.</summary>
        public static void BuildScene()
        {
            BuildLocationScene(Context.Load());
        }

        /// <summary>Builds Assets/**/Locations/&lt;id&gt; into &lt;root&gt;/Scenes/&lt;id&gt;.unity (root object named &lt;id&gt;).
        /// "SkyIsland" builds the island exactly like BuildScene().</summary>
        public static void BuildLocation(string id)
        {
            TryBuildLocation(id);
        }

        /// <summary>Builds the location of a given folder (the Location Builder window passes the listed folder).</summary>
        public static bool BuildLocationAt(string root, string id)
        {
            Context ctx;
            try { ctx = Context.Load(root, id); }
            catch (Exception ex) { Debug.LogError(ex.Message); return false; }
            return BuildLocationScene(ctx);
        }

        /// <summary>Batch mode: -executeMethod SkyIsland.EditorTools.SkyIslandBuilder.BuildLocationFromCommandLine -location &lt;Id&gt;</summary>
        public static void BuildLocationFromCommandLine()
        {
            string id = CommandLineLocation();
            if (!TryBuildLocation(id)) throw new Exception($"[SkyIsland] building location '{id}' failed (see the log above).");
        }

        static bool TryBuildLocation(string id)
        {
            string root;
            try { root = FindLocationRoot(id); }
            catch (Exception ex) { Debug.LogError(ex.Message); return false; }
            return BuildLocationAt(root, id);
        }

        /// <summary>Shared implementation of BuildScene / BuildLocation. Returns false when the build did not finish.</summary>
        static bool BuildLocationScene(Context ctx)
        {
            s_ProgressTitle = ctx.IsSkyIsland ? "Sky Island" : "Location " + ctx.id;
            if (PlayerSettings.colorSpace != ColorSpace.Linear)
                Debug.LogWarning("[SkyIsland] Project color space is Gamma: the colours, fog and LUT are calibrated for Linear (Project Settings > Player > Other Settings > Color Space = Linear).");
            var missingTex = MissingTextures(ctx);
            if (missingTex.Count > 0)
            {
                string msg = $"{missingTex.Count} textures are missing (e.g. {missingTex[0]}).\n\n" + (ctx.IsSkyIsland
                    ? "Import all parts of the Sky Island package (every SkyIsland_N_*.unitypackage), then run Build Scene again."
                    : $"Import all parts of the {ctx.id} package and the current SkyIsland Core (\"shared:\" textures), then build {ctx.id} again.");
                Debug.LogError("[SkyIsland] " + msg);
                if (!Application.isBatchMode) EditorUtility.DisplayDialog(s_ProgressTitle, msg, "OK");
                return false;
            }
            try
            {
                Progress("Import settings", 0.02f);
                SkyIslandImport.FixAll(ctx.root);
                Progress("Materials", 0.08f);
                var mats = BuildMaterials(ctx);
                Progress("Models", 0.14f);
                RemapModels(ctx, mats);
                Progress("Prefabs", 0.22f);
                SetupLightingProject(ctx, LightingInfo.From(ctx.layout));   // v2.6: URP rendering layers, light layer names
                var prefabs = BuildPrefabs(ctx, mats);

                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var rootGo = new GameObject(ctx.id);
                var world = new GameObject("World").transform; world.SetParent(rootGo.transform, false);

                foreach (var wm in ctx.layout.worldModels ?? new string[0])
                {
                    if (!prefabs.TryGetValue(wm, out var pf)) { Debug.LogWarning("[SkyIsland] missing world model " + wm); continue; }
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(pf, world);
                    go.transform.localPosition = Vector3.zero;
                    go.transform.localRotation = Quaternion.identity;
                }

                var fix = AxisFix.FromMarker(ctx.layout.marker, world);

                Transform Group(string name)
                {
                    var t = new GameObject(name).transform; t.SetParent(rootGo.transform, false); return t;
                }

                var props = Group("Structures");
                var trees = Group("Vegetation");
                var rocks = Group("Rocks");
                int total = (ctx.layout.prefabs?.Length ?? 0) + (ctx.layout.trees?.Length ?? 0) + (ctx.layout.rocks?.Length ?? 0);
                int done = 0;
                bool cancelled = false;
                // v2.2 proximity groups (building interiors): placements with e.g go under a SkyIslandProximityGroup
                var groupDefs = new Dictionary<string, GroupDef>();
                if (ctx.layout.groups != null)
                    foreach (var gd in ctx.layout.groups) if (gd != null && !string.IsNullOrEmpty(gd.name)) groupDefs[gd.name] = gd;
                var groupTs = new Dictionary<string, Transform>();
                Transform interiorsRoot = null;
                Transform GroupFor(string g, Transform fallback)
                {
                    if (string.IsNullOrEmpty(g)) return fallback;
                    if (groupTs.TryGetValue(g, out var gt)) return gt;
                    if (interiorsRoot == null) interiorsRoot = Group("Interiors");
                    gt = new GameObject(g).transform; gt.SetParent(interiorsRoot, false);
                    var pg = gt.gameObject.AddComponent<SkyIsland.SkyIslandProximityGroup>();
                    if (groupDefs.TryGetValue(g, out var gd))
                    {
                        pg.bounds = new Bounds(fix.P(J.V3(gd.center)), Vector3.Scale(J.V3(gd.size), new Vector3(Mathf.Abs(fix.sign.x), Mathf.Abs(fix.sign.y), Mathf.Abs(fix.sign.z))));
                        if (gd.margin > 0f) pg.margin = gd.margin;
                    }
                    groupTs[g] = gt;
                    return gt;
                }
                void PlaceAll(Placement[] list, Transform parent)
                {
                    if (list == null) return;
                    foreach (var e in list)
                    {
                        if (cancelled) return;
                        if (++done % 200 == 0 && Progress($"Placing objects {done}/{total}", 0.3f + 0.55f * done / Mathf.Max(1, total))) { cancelled = true; return; }
                        if (!prefabs.TryGetValue(e.m, out var pf)) continue;
                        var go = (GameObject)PrefabUtility.InstantiatePrefab(pf, GroupFor(e.g, parent));
                        go.transform.localPosition = fix.P(J.V3(e.p));
                        go.transform.localRotation = fix.Q(J.Q(e.q));
                        float s = (e.s > 0f ? e.s : 1f) * fix.unit;
                        go.transform.localScale = new Vector3(s, s, s);
                    }
                }
                PlaceAll(ctx.layout.prefabs, props);
                PlaceAll(ctx.layout.trees, trees);
                PlaceAll(ctx.layout.rocks, rocks);
                if (cancelled) { Debug.LogWarning("[SkyIsland] build cancelled."); return false; }
                if (ctx.layout.worldGlows != null && ctx.layout.worldGlows.Length > 0)
                    AddGlows(ctx, Group("Glows"), ctx.layout.worldGlows, mats, fix.unit, fix);
                if (ctx.layout.giants != null && ctx.layout.giants.Length > 0)
                {
                    Progress("Giants", 0.86f);
                    BuildGiants(ctx, rootGo.transform, mats, fix);
                }

                Progress("Lighting, fog, sky and post-processing", 0.88f);
                var cam = CreateCamera(ctx, rootGo.transform, fix);
                AddFogByAltitude(ctx, cam, fix);
                CreateViewpoints(ctx, rootGo.transform, fix);
                ApplyLook(ctx, mats, rootGo.transform, cam);
                CreateProbes(ctx, rootGo.transform, fix);
                AddBeacons(ctx, rootGo.transform, mats);
                if (HasLocationExtras(ctx))
                {
                    Progress("Location extras", 0.94f);
                    AddLocationExtras(ctx, rootGo.transform, mats, cam, fix);
                }

                Progress("Saving scene", 0.97f);
                string scenePath = ctx.ScenePath;
                EnsureFolder(ctx.root + "/Scenes");
                EditorSceneManager.SaveScene(scene, scenePath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[SkyIsland] Scene built for {ctx.pipe}: {scenePath}  ({done} objects). Play mode: hold RMB + WASD/QE, Shift = fast.");
                if (ctx.pipe == Pipe.HDRP)
                    Debug.LogWarning("[SkyIsland] HDRP detected: materials were converted to HDRP/Lit, but the fog/sky/post look is calibrated for URP (and Built-in). Set up HDRP fog/exposure in a Volume manually.");
                return true;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // ------------------------------------------------------------------ axis verification
        public struct AxisFix
        {
            public Vector3 sign;   // +-1 per axis
            public float scale;    // placement position scale (always 1: layout is in metres)
            public float unit;     // model scale correction if the FBX unit conversion differs

            public Vector3 P(Vector3 p) => new Vector3(p.x * sign.x, p.y * sign.y, p.z * sign.z) * scale;

            public Quaternion Q(Quaternion q)
            {
                if (sign.x > 0 && sign.z > 0) return q;
                // conjugate the rotation by the mirror/rotation diag(sx,1,sz)
                var m = Matrix4x4.Rotate(q);
                var s = Matrix4x4.Scale(new Vector3(sign.x, 1f, sign.z));
                var r = s * m * s;
                return r.rotation;
            }

            public static AxisFix FromMarker(MarkerData marker, Transform world)
            {
                var fix = new AxisFix { sign = Vector3.one, scale = 1f, unit = 1f };
                if (marker == null || marker.expected == null) return fix;
                var tr = FindDeep(world, marker.name);
                if (tr == null) { Debug.LogWarning("[SkyIsland] axis marker not found; assuming default FBX axes."); return fix; }
                var r = tr.GetComponent<Renderer>();
                Vector3 obs = r != null ? r.bounds.center : tr.position;
                Vector3 exp = J.V3(marker.expected);
                float so = new Vector2(obs.x, obs.z).magnitude, se = new Vector2(exp.x, exp.z).magnitude;
                float ratio = se > 1e-3f ? so / se : 1f;
                if (Mathf.Abs(ratio - 1f) > 0.05f)
                {
                    // FBX came in with a different unit scale: normalise the world root and scale placements
                    fix.unit = 1f / ratio;
                    world.localScale = Vector3.one * fix.unit;
                    Debug.LogWarning($"[SkyIsland] FBX unit scale mismatch ({ratio:0.###}); models rescaled by {fix.unit:0.###}.");
                    obs /= ratio;
                }
                fix.sign = new Vector3(Mathf.Sign(obs.x) == Mathf.Sign(exp.x) ? 1f : -1f, 1f, Mathf.Sign(obs.z) == Mathf.Sign(exp.z) ? 1f : -1f);
                if (fix.sign != Vector3.one)
                    Debug.LogWarning($"[SkyIsland] FBX axis convention differs ({fix.sign}); placements mirrored to match.");
                r = tr.GetComponent<Renderer>();
                if (r != null) r.enabled = false;
                return fix;
            }
        }

        public static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                var f = FindDeep(c, name);
                if (f != null) return f;
            }
            return null;
        }

        // ------------------------------------------------------------------ camera & helpers
        static Camera CreateCamera(Context ctx, Transform root, AxisFix fix)
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            go.transform.SetParent(root, false);
            var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 6000f;
            cam.fieldOfView = 58f;
            cam.allowHDR = true;
            cam.clearFlags = CameraClearFlags.Skybox;
            go.AddComponent<AudioListener>();
            go.AddComponent<SkyIsland.SkyIslandFlyCamera>();
            if (ctx.layout.interactables != null && ctx.layout.interactables.Length > 0) go.AddComponent<SkyIsland.SkyIslandInteractor>();
            var vp = ctx.layout.viewpoints != null && ctx.layout.viewpoints.Length > 0 ? ctx.layout.viewpoints[0] : null;
            if (vp != null)
            {
                var p = fix.P(J.V3(vp.p));
                var t = fix.P(J.V3(vp.target));
                go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(t - p, Vector3.up));
                if (vp.fov > 1f) cam.fieldOfView = vp.fov;
            }
            var cd = ctx.look.camera;   // v2: optional far clip / field of view
            if (cd != null)
            {
                if (cd.far > 0f) cam.farClipPlane = cd.far;
                if (cd.fov > 1f) cam.fieldOfView = cd.fov;
            }
            return cam;
        }

        /// <summary>Fog thinning away from the playable area: look.fogByAltitude if present, the island ellipse for
        /// Sky Island (v1), otherwise no controller.</summary>
        static SkyIsland.SkyIslandFogByAltitude AddFogByAltitude(Context ctx, Camera cam, AxisFix fix)
        {
            var f = ctx.look.fogByAltitude;
            bool fromData = f != null && f.IsSet;
            if (!fromData && !ctx.defaults.islandFog) return null;
            var fogCtl = cam.gameObject.AddComponent<SkyIsland.SkyIslandFogByAltitude>();
            fogCtl.target = cam;
            fogCtl.groundDensity = ctx.look.fog.density;
            fogCtl.airDensity = AirDensity(ctx);
            ConfigureLinearFog(ctx, fogCtl);
            if (fromData)
            {
                var c = J.V2(f.center, Vector2.zero);
                var ic = fix.P(new Vector3(c.x, 0f, c.y));
                fogCtl.island = new Vector4(ic.x, ic.z, Mathf.Abs(f.radii[0]), Mathf.Abs(f.radii[1]));
                fogCtl.islandTopY = f.topY;
                if (f.heightRange > 0f) fogCtl.heightRange = f.heightRange;
                if (f.distanceRange > 0f) fogCtl.distanceRange = f.distanceRange;
                fogCtl.thinBelow = false;   // the "camera under the island" rule is Sky Island only
            }
            else
            {
                var ic = fix.P(ctx.defaults.islandFogCenter);
                fogCtl.island = new Vector4(ic.x, ic.z, ctx.defaults.islandFogRadii.x, ctx.defaults.islandFogRadii.y);
            }
            return fogCtl;
        }

        /// <summary>v2.4: linear fog (PSX look) - start / end on the ground, the air end grows by 1 / airFactor.</summary>
        internal static void ConfigureLinearFog(Context ctx, SkyIsland.SkyIslandFogByAltitude fogCtl)
        {
            var fog = ctx.look.fog;
            fogCtl.linear = fog.IsLinear;
            if (!fog.IsLinear) return;
            fogCtl.groundStart = fog.start;
            fogCtl.groundEnd = fog.end;
            var f = ctx.look.fogByAltitude;
            fogCtl.airFactor = f != null && f.IsSet && f.airFactor > 0f ? f.airFactor : 1f / 3f;
        }

        /// <summary>Fog density far from the area: ground density x look.fogByAltitude.airFactor (1/3 as in v1 by default).</summary>
        static float AirDensity(Context ctx)
        {
            var f = ctx.look.fogByAltitude;
            if (f != null && f.IsSet && f.airFactor > 0f) return ctx.look.fog.density * f.airFactor;
            return ctx.look.fog.density / 3f;
        }

        static void CreateViewpoints(Context ctx, Transform root, AxisFix fix)
        {
            if (ctx.layout.viewpoints == null) return;
            var g = new GameObject("Viewpoints").transform;
            g.SetParent(root, false);
            foreach (var v in ctx.layout.viewpoints)
            {
                var t = new GameObject("View_" + v.name).transform;
                t.SetParent(g, false);
                var p = fix.P(J.V3(v.p));
                var tg = fix.P(J.V3(v.target));
                t.SetPositionAndRotation(p, Quaternion.LookRotation(tg - p, Vector3.up));
                var vp = t.gameObject.AddComponent<SkyIsland.SkyIslandViewpoint>();
                vp.fieldOfView = v.fov > 1f ? v.fov : 44f;
                vp.previewImage = v.preview;
                vp.previewPath = string.IsNullOrEmpty(v.preview) ? "" : ctx.root + "/Previews/" + v.preview;
            }
        }

        static void CreateProbes(Context ctx, Transform root, AxisFix fix)
        {
            if (ctx.layout.reflectionProbes == null) return;
            var g = new GameObject("ReflectionProbes").transform;
            g.SetParent(root, false);
            string folder = ctx.root + "/Lighting";
            EnsureFolder(folder);
            int i = 0;
            foreach (var pd in ctx.layout.reflectionProbes)
            {
                var go = new GameObject("ReflectionProbe_" + i);
                go.transform.SetParent(g, false);
                go.transform.position = fix.P(J.V3(pd.p));
                var probe = go.AddComponent<ReflectionProbe>();
                var sz = J.V3(pd.size);
                probe.size = new Vector3(Mathf.Abs(sz.x), Mathf.Abs(sz.y), Mathf.Abs(sz.z)) * fix.scale;
                probe.boxProjection = true;
                probe.resolution = 256;
                probe.hdr = true;
                probe.farClipPlane = 3000f;
                probe.importance = 1;
                probe.blendDistance = 20f;
                bool baked = false;
                if (ctx.pipe != Pipe.HDRP)
                {
                    try
                    {
                        probe.mode = ReflectionProbeMode.Custom;
                        string path = $"{folder}/{ctx.AssetName("Probe_" + i)}.exr";
                        if (Lightmapping.BakeReflectionProbe(probe, path))
                        {
                            AssetDatabase.ImportAsset(path);
                            probe.customBakedTexture = AssetDatabase.LoadAssetAtPath<Texture>(path);
                            baked = probe.customBakedTexture != null;
                        }
                    }
                    catch (Exception ex) { Debug.LogWarning("[SkyIsland] probe bake failed: " + ex.Message); }
                }
                if (!baked)
                {
                    probe.mode = ReflectionProbeMode.Realtime;
                    probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
                    probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
                }
                i++;
            }
        }

        /// <summary>Red obstruction lights pulse (SkyIslandBeacons) when the location has M_Light_Red or M_Glow_Red.</summary>
        static void AddBeacons(Context ctx, Transform root, Dictionary<string, Material> mats)
        {
            if (!HasMaterialSpec(ctx, "M_Light_Red") && !HasMaterialSpec(ctx, "M_Glow_Red")) return;
            var beacons = new GameObject("Beacons").AddComponent<SkyIsland.SkyIslandBeacons>();
            beacons.transform.SetParent(root, false);
            beacons.root = root;
            mats.TryGetValue("M_Light_Red", out beacons.beaconMaterial);
            mats.TryGetValue("M_Glow_Red", out beacons.glowMaterial);
        }

        static bool HasMaterialSpec(Context ctx, string name)
        {
            foreach (var m in ctx.materials.materials)
                if (m != null && m.name == name) return true;
            return false;
        }
    }
}
