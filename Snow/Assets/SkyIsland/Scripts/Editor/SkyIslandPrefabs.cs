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

        /// <summary>Creates a prefab (variant of the imported model) per FBX with colliders, LODs, lights and helpers.
        /// Model roles come from the layout prefix lists (vegetation / grass / no collider; Sky Island names by default).</summary>
        public static Dictionary<string, GameObject> BuildPrefabs(Context ctx, Dictionary<string, Material> mats)
        {
            EnsureFolder(ctx.PrefabFolder);
            var rules = ctx.rules ?? SkyIslandModelRules.Default;
            var lights = new Dictionary<string, LightDef[]>();
            if (ctx.layout.prefabLights != null)
                foreach (var pl in ctx.layout.prefabLights) lights[pl.model] = pl.lights;
            var glows = new Dictionary<string, GlowDef[]>();
            if (ctx.layout.prefabGlows != null)
                foreach (var pg in ctx.layout.prefabGlows) glows[pg.model] = pg.glows;
            var radar = new Dictionary<string, RadarHead>();
            if (ctx.layout.radarHeads != null)
                foreach (var r in ctx.layout.radarHeads) radar[r.model] = r;
            var rotators = new Dictionary<string, List<RotatorDef>>();
            if (ctx.layout.rotators != null)
                foreach (var rd in ctx.layout.rotators)
                {
                    if (rd == null || string.IsNullOrEmpty(rd.model)) continue;
                    if (!rotators.TryGetValue(rd.model, out var list)) rotators[rd.model] = list = new List<RotatorDef>();
                    list.Add(rd);
                }
            var interact = new Dictionary<string, List<InteractableDef>>();
            if (ctx.layout.interactables != null)
                foreach (var it in ctx.layout.interactables)
                {
                    if (it == null || string.IsNullOrEmpty(it.model)) continue;
                    if (!interact.TryGetValue(it.model, out var list)) interact[it.model] = list = new List<InteractableDef>();
                    list.Add(it);
                }
            var noShadow = NoShadowMaterials(ctx, mats);
            string markerName = ctx.layout.marker != null ? ctx.layout.marker.name : null;
            UpdateStructureStatic();
            var lighting = LightingInfo.From(ctx.layout);   // v2.6 rendering layers per room (null: none)
            int fixtures = 0, layered = 0;

            var result = new Dictionary<string, GameObject>();
            if (!AssetDatabase.IsValidFolder(ctx.root + "/Models"))
            {
                Debug.LogWarning($"[SkyIsland] {ctx.root}/Models not found: no prefabs for {ctx.id}.");
                return result;
            }
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { ctx.root + "/Models" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null) continue;
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                try
                {
                    bool isTree = rules.IsVegetation(name);
                    bool isGrass = rules.IsGrass(name);
                    SetStatic(inst, isTree || isGrass ? StaticVegetation : s_StructureStatic);
                    foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                    {
                        r.shadowCastingMode = isGrass ? ShadowCastingMode.Off : ShadowCastingMode.On;
                        r.receiveShadows = true;
                    }
                    if (isTree) SetupTree(inst, name);
                    if (isGrass && (!ctx.defaults.grassLodIslandOnly || name == "SkyIsland_Grass")) SetupGrassChunks(inst);
                    RemoveMarkerCollider(inst, markerName);
                    ApplyNoColliderRules(inst, rules);
                    if (noShadow.Count > 0) DisableShadowsOfTransparent(inst, noShadow);
                    bool hasLights = lights.TryGetValue(name, out var defs);
                    bool hasGlows = glows.TryGetValue(name, out var gdefs);
                    // v2.6: lamps with an fx object get their lights and halos under it and a SkyIslandLightFixture
                    var fxIndex = HasFixtures(hasLights ? defs : null, hasGlows ? gdefs : null) ? NameIndex(inst.transform) : null;
                    if (hasLights) AddLights(ctx, inst.transform, defs, fxIndex, lighting);
                    if (hasGlows) AddGlows(ctx, inst.transform, gdefs, mats, 1f, null, fxIndex);
                    if (hasLights && fxIndex != null) fixtures += SetupFixtures(ctx, inst, name, defs, fxIndex, mats);
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
                    if (rotators.TryGetValue(name, out var rlist)) AddRotators(inst, name, rlist);
                    if (interact.TryGetValue(name, out var ilist)) AddInteractables(inst, name, ilist);
                    if (lighting != null) layered += ApplyRenderingLayers(inst, name, lighting);
                    string pfPath = $"{ctx.PrefabFolder}/{name}.prefab";
                    var pf = PrefabUtility.SaveAsPrefabAsset(inst, pfPath);
                    result[name] = pf;
                }
                finally
                {
                    Object.DestroyImmediate(inst);
                }
            }
            foreach (var model in rotators.Keys)
                if (!result.ContainsKey(model)) Debug.LogWarning($"[SkyIsland] {ctx.id}: rotator model '{model}' has no model in {ctx.root}/Models.");
            if (fixtures > 0 || lighting != null)
                Debug.Log($"[SkyIsland] {ctx.id}: {fixtures} light fixtures; rendering layer masks set on {layered} renderers.");
            return result;
        }

        /// <summary>The axis marker (layout.marker) is only a reference point: no collider.</summary>
        static void RemoveMarkerCollider(GameObject inst, string markerName)
        {
            if (string.IsNullOrEmpty(markerName)) return;
            var marker = FindDeep(inst.transform, markerName);
            if (marker == null) return;
            var mc = marker.GetComponent<Collider>();
            if (mc != null) Object.DestroyImmediate(mc);
        }

        /// <summary>Objects named with a noCollider prefix (the model itself or children, with their children):
        /// no collider, no shadows (lamp rows, wires...).</summary>
        static void ApplyNoColliderRules(GameObject inst, SkyIslandModelRules rules)
        {
            foreach (var t in inst.GetComponentsInChildren<Transform>(true))
            {
                if (!rules.IsNoCollider(t.name)) continue;
                foreach (var c in t.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                foreach (var r in t.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        /// <summary>Materials of type "transparent" (alpha-blended surfaces never cast shadows).</summary>
        static HashSet<Material> NoShadowMaterials(Context ctx, Dictionary<string, Material> mats)
        {
            var set = new HashSet<Material>();
            foreach (var s in ctx.materials.materials)
                if (s != null && s.type == "transparent" && mats.TryGetValue(s.name, out var m) && m != null) set.Add(m);
            return set;
        }

        /// <summary>Renderers whose materials are all transparent: shadow casting off (works in every pipeline).</summary>
        static void DisableShadowsOfTransparent(GameObject inst, HashSet<Material> noShadow)
        {
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                var ms = r.sharedMaterials;
                if (ms.Length == 0) continue;
                bool all = true;
                foreach (var m in ms)
                    if (m == null || !noShadow.Contains(m)) { all = false; break; }
                if (all) r.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        /// <summary>Generic rotators (layout rotators): SkyIslandRotator on the named child, so every placed instance spins.</summary>
        static void AddRotators(GameObject inst, string model, List<RotatorDef> defs)
        {
            foreach (var rd in defs)
            {
                var t = string.IsNullOrEmpty(rd.child) ? inst.transform : FindDeep(inst.transform, rd.child);
                if (t == null)
                {
                    Debug.LogWarning($"[SkyIsland] rotator: child '{rd.child}' not found in model {model}.");
                    continue;
                }
                var rot = t.gameObject.AddComponent<SkyIsland.SkyIslandRotator>();
                var axis = J.V3(rd.axis);
                rot.axis = axis.sqrMagnitude > 1e-8f ? axis.normalized : Vector3.up;
                rot.rpm = rd.rpm;
                // moving objects must not be batching static (the whole subtree turns)
                SetStatic(t.gameObject, StaticEditorFlags.OccludeeStatic);
            }
        }

        /// <summary>Openable parts (layout interactables): SkyIslandInteractable on the named child; the moving subtree is not static.
        /// v2.7: kind scale (shown open in the editor too), speed, needsPower, drives -> linked / driven.</summary>
        static void AddInteractables(GameObject inst, string model, List<InteractableDef> defs)
        {
            int missing = 0, duplicates = 0;
            var byChild = new Dictionary<string, SkyIsland.SkyIslandInteractable>();
            foreach (var d in defs)
            {
                var t = FindDeep(inst.transform, d.child);
                if (t == null) { missing++; continue; }
                // v2.7: TryGetComponent. In the Editor GetComponent returns a "fake null" object for a missing component,
                // so `GetComponent() ?? AddComponent()` (v2.3-v2.6) never added the component
                SkyIsland.SkyIslandInteractable it;
                if (!t.gameObject.TryGetComponent(out it)) it = t.gameObject.AddComponent<SkyIsland.SkyIslandInteractable>();
                else duplicates++;
                it.kind = d.kind == "slide" ? SkyIsland.SkyIslandInteractable.Kind.Slide
                        : d.kind == "scale" ? SkyIsland.SkyIslandInteractable.Kind.Scale : SkyIsland.SkyIslandInteractable.Kind.Hinge;
                var a = J.V3(d.axis);
                it.axis = a.sqrMagnitude > 1e-8f ? a.normalized : Vector3.up;
                it.min = d.min; it.max = d.max; it.locked = d.locked;
                if (d.speed > 0f) it.speed = d.speed;
                it.needsPower = d.needsPower;
                // v2.6: starts open (light switches that start on). Only the fields: Awake turns the part from its rest pose.
                // v2.7: a scale part (shutter curtain) is shown open in the editor too: rest scale stored once, open scale applied
                if (d.open)
                {
                    it.openAmount = 1f; it.target = 1f;
                    if (it.kind == SkyIsland.SkyIslandInteractable.Kind.Scale)
                    {
                        if (it.restScale.sqrMagnitude < 1e-8f) it.restScale = t.localScale;
                        it.Apply();
                    }
                }
                SetStatic(t.gameObject, StaticEditorFlags.OccludeeStatic);
                if (!string.IsNullOrEmpty(d.child)) byChild[d.child] = it;
            }
            if (missing > 0) Debug.LogWarning($"[SkyIsland] interactables: {missing} children not found in model {model}.");
            if (duplicates > 0) Debug.LogWarning($"[SkyIsland] interactables: {duplicates} entries name a child that already has one (model {model}).");
            // v2.7: drivers and their followers
            var drivers = new List<SkyIsland.SkyIslandInteractable>();
            var followersAll = new HashSet<SkyIsland.SkyIslandInteractable>();
            var unresolved = new List<string>();
            foreach (var d in defs)
            {
                if (d.drives == null || d.drives.Length == 0) continue;
                SkyIsland.SkyIslandInteractable drv;
                if (string.IsNullOrEmpty(d.child) || !byChild.TryGetValue(d.child, out drv)) continue;
                var followers = new List<SkyIsland.SkyIslandInteractable>();
                foreach (var followerName in d.drives)
                {
                    SkyIsland.SkyIslandInteractable fol;
                    if (string.IsNullOrEmpty(followerName) || followerName == d.child) continue;
                    if (byChild.TryGetValue(followerName, out fol)) { followers.Add(fol); fol.driven = true; followersAll.Add(fol); }
                    else unresolved.Add(followerName);
                }
                drv.linked = followers.ToArray();
                drivers.Add(drv);
            }
            // a powered driver makes its followers powered, down the whole chain
            for (bool changed = true; changed;)
            {
                changed = false;
                foreach (var drv in drivers)
                    if (drv.needsPower)
                        foreach (var fol in drv.linked)
                            if (fol != null && !fol.needsPower) { fol.needsPower = true; changed = true; }
            }
            if (drivers.Count > 0)
                Debug.Log($"[SkyIsland] interactables in {model}: {drivers.Count} drivers, {followersAll.Count} driven parts.");
            if (unresolved.Count > 0)
                Debug.LogWarning($"[SkyIsland] interactables in {model}: drives not found: {string.Join(", ", unresolved.Take(8))}" +
                                 (unresolved.Count > 8 ? $" (+{unresolved.Count - 8})" : "") + ".");
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
            // Sky Island trees by name; other vegetation: 3+ LOD levels (with impostor) like the spruces, else like the dead trees
            bool legacyName = name.StartsWith("Spruce_") || name.StartsWith("Dead_");
            bool spruce = name.StartsWith("Spruce_") || (!legacyName && levels.Count >= 3);
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
            float height = 8f;
            if (!legacyName && levels.Count > 0)
            {
                // small plants: the trunk capsule should not stick out of the model
                float modelHeight = RendererHeight(levels.Values.First());
                if (modelHeight > 0.5f) height = Mathf.Min(8f, modelHeight);
            }
            cap.height = height;
            cap.center = new Vector3(0f, height * 0.5f, 0f);
        }

        static float RendererHeight(List<Renderer> renderers)
        {
            if (renderers == null || renderers.Count == 0) return 0f;
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Count; i++) b.Encapsulate(renderers[i].bounds);
            return b.size.y;
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
            string path = ctx.root + "/Materials/" + ctx.AssetName("GlowQuad") + ".asset";
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (m != null) return m;
            EnsureFolder(ctx.root + "/Materials");
            m = new Mesh { name = ctx.AssetName("GlowQuad") };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.bounds = new Bounds(Vector3.zero, new Vector3(2.5f, 2.5f, 2.5f));
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        /// <summary>Camera-facing halo quads (SkyIsland/LightGlow) at lamp positions; scale = halo diameter.
        /// v2.6: a halo with fx goes under that fixture object of the model (fxIndex: names of the prefab's objects).</summary>
        public static int AddGlows(Context ctx, Transform parent, GlowDef[] defs, Dictionary<string, Material> mats, float unit, AxisFix? fix,
                                   Dictionary<string, Transform> fxIndex = null)
        {
            if (defs == null || mats == null) return 0;
            var quad = GlowQuad(ctx);
            int n = 0;
            foreach (var g in defs)
            {
                if (g == null || string.IsNullOrEmpty(g.m) || !mats.TryGetValue(g.m, out var mat) || mat == null) continue;
                var go = new GameObject(string.IsNullOrEmpty(g.tag) ? "Glow_" + n : $"Glow_{g.tag}_{n}");
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
                if (g.off) go.SetActive(false);
                if (fxIndex != null && !string.IsNullOrEmpty(g.fx) && fxIndex.TryGetValue(g.fx, out var fxT)) go.transform.SetParent(fxT, true);
                n++;
            }
            return n;
        }

        /// <summary>Prefab lights: named Beacon_/Lamp_/Light_&lt;tag&gt;_&lt;n&gt;, positions in the prefab's space. v2.6: a light
        /// with fx goes under that fixture object (world pose kept), a colour temperature k puts the light in temperature mode,
        /// and with layout.lighting the light gets the rendering layers of its room.</summary>
        static void AddLights(Context ctx, Transform parent, LightDef[] defs, Dictionary<string, Transform> fxIndex = null, LightingInfo lighting = null)
        {
            int i = 0;
            float scale = ctx.look != null && ctx.look.localLightScale > 0f ? ctx.look.localLightScale : 1f;
            foreach (var d in defs)
            {
                if (d == null) continue;
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
                // colour: linear -> gamma (Unity converts back); keep hue, move magnitude into intensity (v2.6: temperature mode with k)
                ApplyLightColor(ctx, l, d, scale);
                l.range = d.r;
                l.shadows = d.sh ? LightShadows.Soft : LightShadows.None;
                l.lightmapBakeType = LightmapBakeType.Realtime;
                l.renderMode = LightRenderMode.Auto;
                l.enabled = !d.off;
                if (fxIndex != null && !string.IsNullOrEmpty(d.fx) && fxIndex.TryGetValue(d.fx, out var fxT)) go.transform.SetParent(fxT, true);
                if (lighting != null) SetLightRenderingLayers(l, lighting.LightMask(d.room));
            }
        }
    }
}
