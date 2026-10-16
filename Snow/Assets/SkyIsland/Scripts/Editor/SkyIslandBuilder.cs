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
    /// </summary>
    public static partial class SkyIslandBuilder
    {
        const string Menu = "Tools/Sky Island/";

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

        [MenuItem(Menu + "Apply Look To Open Scene", priority = 21)]
        public static void ApplyLookMenu()
        {
            var ctx = Context.Load();
            var mats = LoadMaterials(ctx);
            var root = GameObject.Find("SkyIsland");
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
            public string root;
            public Pipe pipe;
            public LayoutData layout;
            public MaterialsData materials;
            public LookData look;
            public string Tex(string f) => string.IsNullOrEmpty(f) ? null : root + "/Textures/" + f;
            public string MatFolder => root + "/Materials/" + pipe;
            public string PrefabFolder => root + "/Prefabs";

            public static Context Load()
            {
                var c = new Context { root = FindRoot(), pipe = DetectPipeline() };
                c.layout = JsonUtility.FromJson<LayoutData>(File.ReadAllText(c.root + "/Data/SkyIsland_Layout.json"));
                c.materials = JsonUtility.FromJson<MaterialsData>(File.ReadAllText(c.root + "/Data/SkyIsland_Materials.json"));
                c.look = JsonUtility.FromJson<LookData>(File.ReadAllText(c.root + "/Data/SkyIsland_Look.json"));
                return c;
            }
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
            return EditorUtility.DisplayCancelableProgressBar("Sky Island", info, t);
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
                Check(m.albedo); Check(m.normal); Check(m.mask); Check(m.emissionMap);
                if (m.detail != null) { Check(m.detail.albedo); Check(m.detail.normal); }
            }
            return missing;
        }

        public static void BuildScene()
        {
            var ctx = Context.Load();
            if (PlayerSettings.colorSpace != ColorSpace.Linear)
                Debug.LogWarning("[SkyIsland] Project color space is Gamma: the colours, fog and LUT are calibrated for Linear (Project Settings > Player > Other Settings > Color Space = Linear).");
            var missingTex = MissingTextures(ctx);
            if (missingTex.Count > 0)
            {
                string msg = $"{missingTex.Count} textures are missing (e.g. {missingTex[0]}).\n\n" +
                             "Import all parts of the Sky Island package (every SkyIsland_N_*.unitypackage), then run Build Scene again.";
                Debug.LogError("[SkyIsland] " + msg);
                EditorUtility.DisplayDialog("Sky Island", msg, "OK");
                return;
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
                var prefabs = BuildPrefabs(ctx, mats);

                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var rootGo = new GameObject("SkyIsland");
                var world = new GameObject("World").transform; world.SetParent(rootGo.transform, false);

                foreach (var wm in ctx.layout.worldModels)
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
                void PlaceAll(Placement[] list, Transform parent)
                {
                    if (list == null) return;
                    foreach (var e in list)
                    {
                        if (cancelled) return;
                        if (++done % 200 == 0 && Progress($"Placing objects {done}/{total}", 0.3f + 0.55f * done / Mathf.Max(1, total))) { cancelled = true; return; }
                        if (!prefabs.TryGetValue(e.m, out var pf)) continue;
                        var go = (GameObject)PrefabUtility.InstantiatePrefab(pf, parent);
                        go.transform.localPosition = fix.P(J.V3(e.p));
                        go.transform.localRotation = fix.Q(J.Q(e.q));
                        float s = (e.s > 0f ? e.s : 1f) * fix.unit;
                        go.transform.localScale = new Vector3(s, s, s);
                    }
                }
                PlaceAll(ctx.layout.prefabs, props);
                PlaceAll(ctx.layout.trees, trees);
                PlaceAll(ctx.layout.rocks, rocks);
                if (cancelled) { Debug.LogWarning("[SkyIsland] build cancelled."); return; }
                if (ctx.layout.worldGlows != null && ctx.layout.worldGlows.Length > 0)
                    AddGlows(ctx, Group("Glows"), ctx.layout.worldGlows, mats, fix.unit, fix);

                Progress("Lighting, fog, sky and post-processing", 0.88f);
                var cam = CreateCamera(ctx, rootGo.transform, fix);
                var fogCtl = cam.gameObject.AddComponent<SkyIsland.SkyIslandFogByAltitude>();
                fogCtl.target = cam;
                fogCtl.groundDensity = ctx.look.fog.density;
                fogCtl.airDensity = ctx.look.fog.density / 3f;
                var ic = fix.P(new Vector3(0f, 0f, -40f));
                fogCtl.island = new Vector4(ic.x, ic.z, 745f, 470f);
                CreateViewpoints(ctx, rootGo.transform, fix);
                ApplyLook(ctx, mats, rootGo.transform, cam);
                CreateProbes(ctx, rootGo.transform, fix);

                var beacons = new GameObject("Beacons").AddComponent<SkyIsland.SkyIslandBeacons>();
                beacons.transform.SetParent(rootGo.transform, false);
                beacons.root = rootGo.transform;
                mats.TryGetValue("M_Light_Red", out beacons.beaconMaterial);
                mats.TryGetValue("M_Glow_Red", out beacons.glowMaterial);

                Progress("Saving scene", 0.97f);
                string scenePath = ctx.root + "/Scenes/SkyIsland.unity";
                EnsureFolder(ctx.root + "/Scenes");
                EditorSceneManager.SaveScene(scene, scenePath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[SkyIsland] Scene built for {ctx.pipe}: {scenePath}  ({done} objects). Play mode: hold RMB + WASD/QE, Shift = fast.");
                if (ctx.pipe == Pipe.HDRP)
                    Debug.LogWarning("[SkyIsland] HDRP detected: materials were converted to HDRP/Lit, but the fog/sky/post look is calibrated for URP (and Built-in). Set up HDRP fog/exposure in a Volume manually.");
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
            var vp = ctx.layout.viewpoints != null && ctx.layout.viewpoints.Length > 0 ? ctx.layout.viewpoints[0] : null;
            if (vp != null)
            {
                var p = fix.P(J.V3(vp.p));
                var t = fix.P(J.V3(vp.target));
                go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(t - p, Vector3.up));
                if (vp.fov > 1f) cam.fieldOfView = vp.fov;
            }
            return cam;
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
                        string path = $"{folder}/SkyIsland_Probe_{i}.exr";
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
    }
}
