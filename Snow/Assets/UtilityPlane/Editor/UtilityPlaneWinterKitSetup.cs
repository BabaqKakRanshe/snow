#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UtilityPlane.EditorTools
{
    /// <summary>
    /// Tools / Utility Plane / Install Winter Kit (add-on to the plane package, run after Build Materials + Prefabs):
    ///  - M_KitCovers, M_KitProps: Lit with the baked atlases (MaskMap as in v7). Kit v1.2 (plane v9): PS1 / PSX lo-fi
    ///    atlases (512 px, 64 colours, ordered dither, shading in the colour; Point filter, uncompressed) without normal
    ///    and detail maps; v1 atlases (psx false in winter_kit.json) keep the fabric weave detail map;
    ///  - Prefabs/Kit/KitCarry_&lt;Key&gt;.prefab: 12 carry forms - root with the mesh and a convex MeshCollider on Default,
    ///    UtilityPlaneKitItem; child IV_KitItem = trigger on the Interact layer with UtilityPlaneInteractable
    ///    (kind "kit_item"); no Rigidbody (add it in your own prefab variants);
    ///  - Prefabs/WinterKit.prefab: SM_WinterKit (15 installed forms, hidden), trigger zones IV_Kit_&lt;Item&gt; on the
    ///    Interact layer with UtilityPlaneInteractable (kind "kit"), Attach_* points, UtilityPlaneWinterKit with slots
    ///    (label, install time, carry prefab, attach point) and zones;
    ///  - nests WinterKit.prefab into Prefabs/UtilityPlane.prefab at the root (local identity) unless an instance of it
    ///    is already there (that one is kept with its overrides).
    /// Re-run it after "Build Materials + Prefabs" (that one rebuilds UtilityPlane.prefab without the kit).
    /// </summary>
    public static class UtilityPlaneWinterKitSetup
    {
        const string Root = "Assets/UtilityPlane";
        const string KitFbx = Root + "/Models/SM_WinterKit.fbx";
        const string KitPrefab = Root + "/Prefabs/WinterKit.prefab";
        const string PlanePrefab = Root + "/Prefabs/UtilityPlane.prefab";

#pragma warning disable 0649 // filled by JsonUtility
        [Serializable] class Zone { public float[] center, halfExtents, rotationEuler; }
        [Serializable] class Item
        {
            public string id, @object, label, carry, carryFbx;
            public float[] socket, attach;
            public Zone zone;
            public string[] effects, excludes;
            public float installSeconds;
            public bool groundItem;
        }
        [Serializable] class Carry
        {
            public string prefab, fbx, note, label;
            public float massKg, capacityL, fullMassKg;
            public float[] sizeM, grip, pour;
            public int sets, count;
        }
        [Serializable] class Atlas
        {
            public string atlas, material, detail, detailMask;
            public float metresPerUV, pxPerMetre, areaM2, detailTiling, detailNormalScale;
            public bool psx;                                    // v1.2: PSX atlas, no normal / detail map
        }
        [Serializable] class KitDoc { public int version; public Item[] items; public Carry[] carry; public Atlas[] atlases; }
#pragma warning restore 0649

        [MenuItem("Tools/Utility Plane/Install Winter Kit")]
        public static void Install()
        {
            if (!(AssetImporter.GetAtPath(KitFbx) is ModelImporter))
            {
                Debug.LogError($"[WinterKit] {KitFbx} not found. Copy the kit files into {Root} first.");
                return;
            }
            string jp = Root + "/Data/winter_kit.json";
            if (!File.Exists(jp)) { Debug.LogError($"[WinterKit] {jp} not found."); return; }
            var doc = JsonUtility.FromJson<KitDoc>(File.ReadAllText(jp));

            foreach (var guid in AssetDatabase.FindAssets("t:Texture", new[] { Root + "/Textures" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                string n = Path.GetFileNameWithoutExtension(p);
                if (!(n.StartsWith("T_Kit") || n.StartsWith("T_Detail_Fabric"))) continue;
                if (!(AssetImporter.GetAtPath(p) is TextureImporter ti)) continue;
                UtilityPlaneSetup.ApplyTextureSettings(ti, p);           // v7 rules (MaskMap / DetailMask linear ...)
                if (n.EndsWith("_DetailMask")) ti.maxTextureSize = 1024;
                ti.SaveAndReimport();
            }

            string pipe = DetectPipeline();
            EnsureFolder(Root, "Materials");
            EnsureFolder(Root, "Prefabs");
            EnsureFolder(Root + "/Prefabs", "Kit");
            var mats = doc.atlases.Select(a => MakeKitLit(a, pipe)).ToArray();

            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Root + "/Models" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                string fn = Path.GetFileNameWithoutExtension(p);
                if (!(fn == "SM_WinterKit" || fn.StartsWith("SM_Kit_"))) continue;
                var mi = (ModelImporter)AssetImporter.GetAtPath(p);
                UtilityPlaneSetup.ApplyModelSettings(mi);
                foreach (var m in mats)
                    mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name), m);
                mi.SaveAndReimport();
            }

            int layer = EnsureLayer("Interact");
            int carried = BuildCarryPrefabs(doc, layer);
            var kit = BuildKitPrefab(doc, layer, out int slotCount, out int zoneCount);
            string nested = NestIntoPlane(kit);
            AssetDatabase.SaveAssets();
            Debug.Log($"[WinterKit] Done ({pipe}): {KitPrefab} ({slotCount} items, {zoneCount} zones), {carried} carry " +
                      $"prefabs in {Root}/Prefabs/Kit, {nested}");
            if (doc.atlases.Any(a => a.psx))
            {
                // v1.2: files of the v1 kit that nothing uses any more
                var stale = new[] { "T_KitCovers_Normal", "T_KitProps_Normal", "T_KitCovers_DetailMask", "T_KitProps_DetailMask",
                                    "T_Detail_Fabric_Albedo", "T_Detail_Fabric_Normal" }
                    .Select(n => $"{Root}/Textures/{n}.png").Where(File.Exists).ToArray();
                if (stale.Length > 0)
                    Debug.Log("[WinterKit] Unused files from kit v1 (safe to delete): " + string.Join(", ", stale));
            }
        }

        // ------------------------------------------------------------------ prefabs
        static string CarryPath(string prefab) => $"{Root}/Prefabs/Kit/{prefab}.prefab";

        static GameObject BuildKitPrefab(KitDoc doc, int layer, out int slotCount, out int zoneCount)
        {
            slotCount = zoneCount = 0;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(KitFbx);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                inst.name = "WinterKit";
                var all = inst.GetComponentsInChildren<Transform>(true);
                var comp = inst.GetComponent<UtilityPlaneWinterKit>();      // (no ?? on Unity objects: fake null)
                if (comp == null) comp = inst.AddComponent<UtilityPlaneWinterKit>();
                var slots = new System.Collections.Generic.List<UtilityPlaneWinterKit.Slot>();
                var zones = new System.Collections.Generic.List<Collider>();
                foreach (var it in doc.items)
                {
                    if (!Enum.TryParse(it.id, out KitItem item)) { Debug.LogWarning($"[WinterKit] unknown item {it.id}"); continue; }
                    var vis = all.FirstOrDefault(t => t.name == it.@object);
                    if (vis == null) { Debug.LogWarning($"[WinterKit] {it.@object} not in the FBX"); continue; }
                    vis.gameObject.SetActive(false);
                    foreach (var r in vis.GetComponentsInChildren<Renderer>(true))
                        r.shadowCastingMode = it.id.StartsWith("Exhaust") || it.id == "PitotCover"
                            ? ShadowCastingMode.Off : ShadowCastingMode.On;
                    Transform attach = null;
                    if (it.attach != null && it.attach.Length == 3)
                    {
                        attach = new GameObject("Attach_" + it.id).transform;
                        attach.SetParent(inst.transform, false);
                        attach.localPosition = V(it.attach);
                    }
                    slots.Add(new UtilityPlaneWinterKit.Slot
                    {
                        item = item, visual = vis.gameObject, installed = false, label = it.label,
                        installSeconds = it.installSeconds, groundItem = it.groundItem, attach = attach,
                        carryPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CarryPath(it.carry))
                    });

                    // zone box: centre in WinterKit space, half extents along its own axes (the windshield one is
                    // tilted along the glass so it stays outside the cabin)
                    var go = new GameObject("IV_Kit_" + it.id);
                    go.transform.SetParent(inst.transform, false);
                    go.transform.localPosition = V(it.zone.center);
                    var e = it.zone.rotationEuler;
                    go.transform.localRotation = e != null && e.Length == 3 ? Quaternion.Euler(e[0], e[1], e[2]) : Quaternion.identity;
                    if (layer >= 0) go.layer = layer;
                    var bc = go.AddComponent<BoxCollider>();
                    bc.isTrigger = true;
                    bc.center = Vector3.zero;
                    bc.size = 2f * V(it.zone.halfExtents);
                    var ia = go.AddComponent<UtilityPlaneInteractable>();
                    ia.id = it.id; ia.kind = "kit"; ia.label = it.label;
                    zones.Add(bc);
                }
                comp.slots = slots.ToArray();
                comp.zones = zones.ToArray();
                comp.groundMask = Physics.DefaultRaycastLayers & ~(layer >= 0 ? 1 << layer : 0);
                slotCount = slots.Count;
                zoneCount = zones.Count;
                return PrefabUtility.SaveAsPrefabAsset(inst, KitPrefab);
            }
            finally { UnityEngine.Object.DestroyImmediate(inst); }
        }

        static int BuildCarryPrefabs(KitDoc doc, int layer)
        {
            int n = 0;
            foreach (var c in doc.carry)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Models/{c.fbx}.fbx");
                if (model == null) { Debug.LogWarning($"[WinterKit] {c.fbx}.fbx not found"); continue; }
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                try
                {
                    PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    inst.name = c.prefab;                     // root stays on Default: it collides like a cargo case
                    var mf = inst.GetComponentInChildren<MeshFilter>();
                    if (mf != null)
                    {
                        var mc = mf.gameObject.AddComponent<MeshCollider>();
                        mc.sharedMesh = mf.sharedMesh;
                        mc.convex = true;
                    }
                    var all = inst.GetComponentsInChildren<Transform>(true);
                    var iv = new GameObject("IV_KitItem");       // use-ray target (Interact layer, no collisions)
                    iv.transform.SetParent(mf != null ? mf.transform : inst.transform, false);
                    if (layer >= 0) iv.layer = layer;
                    var bc = iv.AddComponent<BoxCollider>();
                    bc.isTrigger = true;
                    if (mf != null)
                    {
                        var b = mf.sharedMesh.bounds;
                        bc.center = b.center;
                        bc.size = b.size + new Vector3(0.02f, 0.02f, 0.02f);
                    }
                    var ia = iv.AddComponent<UtilityPlaneInteractable>();
                    ia.id = c.prefab; ia.kind = "kit_item"; ia.label = c.label;
                    var ki = inst.AddComponent<UtilityPlaneKitItem>();
                    ki.key = c.prefab.StartsWith("KitCarry_") ? c.prefab.Substring(9) : c.prefab;
                    ki.massKg = c.massKg;
                    ki.count = Mathf.Max(1, c.count);
                    ki.sets = Mathf.Max(1, c.sets);
                    ki.setsLeft = ki.sets;
                    ki.capacityLitres = c.capacityL;
                    ki.grip = all.FirstOrDefault(t => t.name == "Socket_Grip");
                    ki.pour = all.FirstOrDefault(t => t.name == "Socket_Pour");
                    ki.installs = doc.items.Where(i => i.carry == c.prefab)
                                           .Select(i => Enum.TryParse(i.id, out KitItem k) ? (KitItem?)k : null)
                                           .Where(k => k.HasValue).Select(k => k.Value).ToArray();
                    PrefabUtility.SaveAsPrefabAsset(inst, CarryPath(c.prefab));
                    n++;
                }
                finally { UnityEngine.Object.DestroyImmediate(inst); }
            }
            return n;
        }

        // keeps an existing instance of WinterKit.prefab (and its overrides); adds one only when there is none
        static string NestIntoPlane(GameObject kitPrefab)
        {
            if (kitPrefab == null || AssetDatabase.LoadAssetAtPath<GameObject>(PlanePrefab) == null)
                return "UtilityPlane.prefab not found - put WinterKit under your plane root at local identity.";
            var root = PrefabUtility.LoadPrefabContents(PlanePrefab);
            try
            {
                bool kept = false, removed = false;
                foreach (var w in root.GetComponentsInChildren<UtilityPlaneWinterKit>(true).ToArray())
                {
                    if (w == null) continue;                          // destroyed with an outer duplicate
                    var go = w.gameObject;
                    bool ours = PrefabUtility.IsAnyPrefabInstanceRoot(go) &&
                                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go) == KitPrefab;
                    if (ours && !kept) { kept = true; continue; }
                    UnityEngine.Object.DestroyImmediate(go);
                    removed = true;
                }
                if (kept)
                {
                    if (removed) PrefabUtility.SaveAsPrefabAsset(root, PlanePrefab);
                    return "WinterKit already in UtilityPlane.prefab (kept).";
                }
                var k = (GameObject)PrefabUtility.InstantiatePrefab(kitPrefab, root.transform);
                k.name = "WinterKit";
                k.transform.localPosition = Vector3.zero;
                k.transform.localRotation = Quaternion.identity;
                k.transform.localScale = Vector3.one;
                PrefabUtility.SaveAsPrefabAsset(root, PlanePrefab);
                return "nested into UtilityPlane.prefab.";
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static Vector3 V(float[] a) => new Vector3(a[0], a[1], a[2]);

        // ------------------------------------------------------------------ materials (same packing as v7 MakeLit)
        static Material MakeKitLit(Atlas a, string pipe)
        {
            string name = a.material, prefix = "T_" + a.atlas;
            string path = $"{Root}/Materials/{name}.mat";
            var sh = pipe == "HDRP" ? Shader.Find("HDRP/Lit") :
                     pipe == "URP" ? Shader.Find("Universal Render Pipeline/Lit") : Shader.Find("Standard");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(sh); AssetDatabase.CreateAsset(mat, path); }
            else mat.shader = sh;
            // v1.2: a PSX atlas has no normal map (an old T_Kit*_Normal left in the folder is ignored)
            Texture2D bc = Tex(prefix + "_BaseColor"), nrm = a.psx ? null : Tex(prefix + "_Normal"), mask = Tex(prefix + "_MaskMap");
            if (a.psx)
            {
                foreach (var t in new[] { "_DetailAlbedoMap", "_DetailNormalMap", "_DetailMask" })
                    if (mat.HasProperty(t)) mat.SetTexture(t, null);
                mat.DisableKeyword("_DETAIL_MULX2");
                mat.DisableKeyword("_NORMALMAP");
            }
            switch (pipe)
            {
                case "URP":
                    mat.SetTexture("_BaseMap", bc); mat.SetColor("_BaseColor", Color.white);
                    mat.SetTexture("_BumpMap", nrm); mat.SetFloat("_BumpScale", 1f);
                    if (nrm != null) mat.EnableKeyword("_NORMALMAP");
                    mat.SetTexture("_MetallicGlossMap", mask); mat.SetFloat("_Smoothness", 1f);
                    mat.SetFloat("_SmoothnessTextureChannel", 0f); mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                    mat.SetTexture("_OcclusionMap", mask); mat.SetFloat("_OcclusionStrength", 1f);
                    mat.EnableKeyword("_OCCLUSIONMAP");
                    break;
                case "HDRP":
                    mat.SetTexture("_BaseColorMap", bc); mat.SetColor("_BaseColor", Color.white);
                    mat.SetTexture("_NormalMap", nrm);
                    mat.SetTexture("_MaskMap", mask);
                    mat.SetFloat("_MetallicRemapMin", 0f); mat.SetFloat("_MetallicRemapMax", 1f);
                    mat.SetFloat("_SmoothnessRemapMin", 0f); mat.SetFloat("_SmoothnessRemapMax", 1f);
                    mat.SetFloat("_AORemapMin", 0f); mat.SetFloat("_AORemapMax", 1f);
                    break;
                default:
                    mat.SetTexture("_MainTex", bc); mat.SetColor("_Color", Color.white);
                    mat.SetTexture("_BumpMap", nrm);
                    if (nrm != null) mat.EnableKeyword("_NORMALMAP");
                    mat.SetTexture("_MetallicGlossMap", mask); mat.SetFloat("_GlossMapScale", 1f);
                    mat.SetFloat("_SmoothnessTextureChannel", 0f); mat.EnableKeyword("_METALLICGLOSSMAP");
                    mat.SetTexture("_OcclusionMap", mask); mat.SetFloat("_OcclusionStrength", 1f);
                    break;
            }
            // fabric weave only on the textile parts (T_Kit*_DetailMask .a); tiling from the atlas texel density
            var dAlb = Tex("T_Detail_" + a.detail + "_Albedo");
            var dNrm = Tex("T_Detail_" + a.detail + "_Normal");
            var dMask = Tex(a.detailMask);
            if (!a.psx && pipe != "HDRP" && dAlb != null && dNrm != null && a.detailTiling > 0f)
            {
                mat.SetTexture("_DetailMask", dMask);
                mat.SetTexture("_DetailAlbedoMap", dAlb);
                mat.SetTexture("_DetailNormalMap", dNrm);
                mat.SetFloat("_DetailNormalMapScale", a.detailNormalScale);
                if (pipe == "URP") mat.SetFloat("_DetailAlbedoMapScale", 1f);
                else mat.SetFloat("_UVSec", 0f);
                mat.SetTextureScale("_DetailAlbedoMap", new Vector2(a.detailTiling, a.detailTiling));
                mat.EnableKeyword("_DETAIL_MULX2");
            }
            Validate(mat, pipe);
            return mat;
        }

        static Texture2D Tex(string name) => string.IsNullOrEmpty(name) ? null :
            AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Textures/{name}.png");

        static string DetectPipeline()
        {
            RenderPipelineAsset rp = GraphicsSettings.currentRenderPipeline;
            if (rp == null) return "BuiltIn";
            return rp.GetType().Name.Contains("HDRenderPipeline") ? "HDRP" : "URP";
        }

        static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }

        static int EnsureLayer(string name)
        {
            int existing = LayerMask.NameToLayer(name);
            if (existing >= 0) return existing;
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return -1;
            var so = new SerializedObject(assets[0]);
            var layers = so.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++)
            {
                var sp = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(sp.stringValue)) continue;
                sp.stringValue = name;
                so.ApplyModifiedProperties();
                Debug.Log($"[WinterKit] Added layer \"{name}\" at index {i}.");
                return i;
            }
            Debug.LogWarning($"[WinterKit] No free layer for \"{name}\"; zones stay on Default.");
            return -1;
        }

        // URP / HDRP set their keywords in their own editor code; called through reflection (no package references).
        static void Validate(Material m, string pipe)
        {
            if (pipe == "HDRP")
                InvokeStatic("UnityEditor.Rendering.HighDefinition.HDShaderUtils", "ResetMaterialKeywords", m);
            else if (pipe == "URP")
            {
                if (m.shader != null && m.shader.name == "Universal Render Pipeline/Lit")
                    InvokeStatic("UnityEditor.Rendering.Universal.ShaderGUI.LitGUI", "SetMaterialKeywords", m);
                InvokeStatic("UnityEditor.BaseShaderGUI", "SetMaterialKeywords", m);
            }
            EditorUtility.SetDirty(m);
        }

        static void InvokeStatic(string typeName, string method, Material m)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType(typeName);
                if (t == null) continue;
                foreach (var mi in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    var ps = mi.GetParameters();
                    if (mi.Name != method || ps.Length == 0 || ps[0].ParameterType != typeof(Material)) continue;
                    if (ps.Skip(1).Any(p => !p.IsOptional && p.ParameterType.IsValueType)) continue;
                    var args = new object[ps.Length];
                    args[0] = m;
                    for (int i = 1; i < ps.Length; i++) args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : null;
                    try { mi.Invoke(null, args); }
                    catch (Exception e) { Debug.LogWarning($"[WinterKit] {typeName}.{method}: {e.InnerException?.Message ?? e.Message}"); }
                    return;
                }
            }
        }
    }
}
#endif
