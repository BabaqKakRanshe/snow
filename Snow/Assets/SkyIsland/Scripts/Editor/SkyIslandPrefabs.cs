using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SkyIsland.EditorTools
{
    public static partial class SkyIslandBuilder
    {
        const StaticEditorFlags StaticAll = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
                                            StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
        const StaticEditorFlags StaticVegetation = StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;

        /// <summary>Creates a prefab (variant of the imported model) per FBX with colliders, LODs, lights and helpers.</summary>
        public static Dictionary<string, GameObject> BuildPrefabs(Context ctx, Dictionary<string, Material> mats)
        {
            EnsureFolder(ctx.PrefabFolder);
            var lights = new Dictionary<string, LightDef[]>();
            if (ctx.layout.prefabLights != null)
                foreach (var pl in ctx.layout.prefabLights) lights[pl.model] = pl.lights;
            var glows = new Dictionary<string, GlowDef[]>();
            if (ctx.layout.prefabGlows != null)
                foreach (var pg in ctx.layout.prefabGlows) glows[pg.model] = pg.glows;
            var radar = new Dictionary<string, RadarHead>();
            if (ctx.layout.radarHeads != null)
                foreach (var r in ctx.layout.radarHeads) radar[r.model] = r;

            var result = new Dictionary<string, GameObject>();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { ctx.root + "/Models" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null) continue;
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                try
                {
                    bool isTree = name.StartsWith("Spruce_") || name.StartsWith("Dead_");
                    bool isGrass = name.StartsWith("SkyIsland_Grass") || name.StartsWith("Grass_Clump");
                    SetStatic(inst, isTree || isGrass ? StaticVegetation : StaticAll);
                    foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                    {
                        r.shadowCastingMode = isGrass ? ShadowCastingMode.Off : ShadowCastingMode.On;
                        r.receiveShadows = true;
                    }
                    if (isTree) SetupTree(inst, name);
                    if (name == "SkyIsland_Grass") SetupGrassChunks(inst);
                    if (name == "SkyIsland_Terrain")
                    {
                        var marker = FindDeep(inst.transform, "SkyIsland_Marker");
                        if (marker != null)
                        {
                            var mc = marker.GetComponent<Collider>();
                            if (mc != null) Object.DestroyImmediate(mc);
                        }
                        foreach (var t in inst.GetComponentsInChildren<Transform>(true))
                            if (t.name.StartsWith("Airfield_Lights") || t.name.StartsWith("Approach_Lights") || t.name.StartsWith("Power_Lines"))
                            {
                                var c = t.GetComponent<Collider>();
                                if (c != null) Object.DestroyImmediate(c);
                                var r = t.GetComponent<Renderer>();
                                if (r != null) r.shadowCastingMode = ShadowCastingMode.Off;
                            }
                    }
                    if (lights.TryGetValue(name, out var defs)) AddLights(ctx, inst.transform, defs);
                    if (glows.TryGetValue(name, out var gdefs)) AddGlows(ctx, inst.transform, gdefs, mats, 1f, null);
                    if (radar.TryGetValue(name, out var rh))
                    {
                        var head = FindDeep(inst.transform, rh.child);
                        if (head != null)
                        {
                            var rot = head.gameObject.AddComponent<SkyIsland.SkyIslandRadarRotator>();
                            rot.rpm = rh.rpm;
                            SetStatic(head.gameObject, StaticEditorFlags.OccludeeStatic, true);
                        }
                    }
                    string pfPath = $"{ctx.PrefabFolder}/{name}.prefab";
                    var pf = PrefabUtility.SaveAsPrefabAsset(inst, pfPath);
                    result[name] = pf;
                }
                finally
                {
                    Object.DestroyImmediate(inst);
                }
            }
            return result;
        }

        static void SetStatic(GameObject go, StaticEditorFlags flags, bool onlyThis = false)
        {
            if (onlyThis) { GameObjectUtility.SetStaticEditorFlags(go, flags); return; }
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
        }

        static void SetupTree(GameObject inst, string name)
        {
            var levels = new SortedDictionary<int, List<Renderer>>();
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                int k = r.name.LastIndexOf("_LOD");
                int lod = 0;
                if (k >= 0) int.TryParse(r.name.Substring(k + 4), out lod);
                if (!levels.TryGetValue(lod, out var list)) levels[lod] = list = new List<Renderer>();
                list.Add(r);
                if (lod >= 2) r.shadowCastingMode = ShadowCastingMode.Off;
            }
            foreach (var other in inst.GetComponentsInChildren<LODGroup>(true))
                if (other.gameObject != inst) Object.DestroyImmediate(other);
            var lg = inst.GetComponent<LODGroup>();
            if (lg == null) lg = inst.AddComponent<LODGroup>();
            bool spruce = name.StartsWith("Spruce_");
            float[] h = spruce ? new[] { 0.16f, 0.045f, 0.004f } : new[] { 0.12f, 0.018f };
            var lods = new List<LOD>();
            int i = 0;
            foreach (var kv in levels)
            {
                float t = i < h.Length ? h[i] : 0.002f;
                lods.Add(new LOD(t, kv.Value.ToArray()));
                i++;
            }
            lg.SetLODs(lods.ToArray());
            lg.fadeMode = LODFadeMode.None;
            lg.RecalculateBounds();
            var cap = inst.GetComponent<CapsuleCollider>();
            if (cap == null) cap = inst.AddComponent<CapsuleCollider>();
            cap.direction = 1;
            cap.radius = spruce ? 0.45f : 0.35f;
            cap.height = 8f;
            cap.center = new Vector3(0f, 4f, 0f);
        }

        static void SetupGrassChunks(GameObject inst)
        {
            foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true))
            {
                var go = r.gameObject;
                var lg = go.GetComponent<LODGroup>();
                if (lg == null) lg = go.AddComponent<LODGroup>();
                lg.SetLODs(new[] { new LOD(0.02f, new Renderer[] { r }) });
                lg.fadeMode = LODFadeMode.None;
                lg.RecalculateBounds();
            }
        }

        /// <summary>Quad for fog halos with generous bounds (the shader turns it towards the camera).</summary>
        static Mesh GlowQuad(Context ctx)
        {
            string path = ctx.root + "/Materials/SkyIsland_GlowQuad.asset";
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (m != null) return m;
            EnsureFolder(ctx.root + "/Materials");
            m = new Mesh { name = "SkyIsland_GlowQuad" };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.bounds = new Bounds(Vector3.zero, new Vector3(2.5f, 2.5f, 2.5f));
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        /// <summary>Camera-facing halo quads (SkyIsland/LightGlow) at lamp positions; scale = halo diameter.</summary>
        public static int AddGlows(Context ctx, Transform parent, GlowDef[] defs, Dictionary<string, Material> mats, float unit, AxisFix? fix)
        {
            if (defs == null || mats == null) return 0;
            var quad = GlowQuad(ctx);
            int n = 0;
            foreach (var g in defs)
            {
                if (g == null || string.IsNullOrEmpty(g.m) || !mats.TryGetValue(g.m, out var mat) || mat == null) continue;
                var go = new GameObject("Glow_" + n);
                go.transform.SetParent(parent, false);
                var p = J.V3(g.p);
                go.transform.localPosition = fix.HasValue ? fix.Value.P(p) : p;
                float s = Mathf.Max(0.1f, g.s) * unit;
                go.transform.localScale = new Vector3(s, s, s);
                go.AddComponent<MeshFilter>().sharedMesh = quad;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                r.allowOcclusionWhenDynamic = false;
                GameObjectUtility.SetStaticEditorFlags(go, 0);
                n++;
            }
            return n;
        }

        static void AddLights(Context ctx, Transform parent, LightDef[] defs)
        {
            int i = 0;
            float scale = ctx.look != null && ctx.look.localLightScale > 0f ? ctx.look.localLightScale : 1f;
            foreach (var d in defs)
            {
                string prefix = d.tag == "beacon" ? "Beacon" : (d.tag == "lamp" ? "Lamp" : "Light");
                var go = new GameObject($"{prefix}_{d.tag}_{i++}");
                go.transform.SetParent(parent, false);
                go.transform.localPosition = J.V3(d.p);
                var l = go.AddComponent<Light>();
                bool spot = d.t == "spot";
                l.type = spot ? LightType.Spot : LightType.Point;
                if (spot)
                {
                    var dir = J.V3(d.d);
                    if (dir.sqrMagnitude < 1e-6f) dir = Vector3.down;
                    go.transform.localRotation = Quaternion.LookRotation(dir.normalized, Mathf.Abs(dir.normalized.y) > 0.95f ? Vector3.forward : Vector3.up);
                    l.spotAngle = Mathf.Clamp(d.a, 1f, 179f);
                    l.innerSpotAngle = l.spotAngle * 0.55f;
                }
                // colour: linear -> gamma (Unity converts back); keep hue, move magnitude into intensity
                var c = J.Lin(d.c);
                float mx = Mathf.Max(c.maxColorComponent, 1e-4f);
                l.color = new Color(c.r / mx, c.g / mx, c.b / mx).gamma;
                l.intensity = d.i * mx * scale;
                l.range = d.r;
                l.shadows = d.sh ? LightShadows.Soft : LightShadows.None;
                l.lightmapBakeType = LightmapBakeType.Realtime;
                l.renderMode = LightRenderMode.Auto;
            }
        }
    }
}
