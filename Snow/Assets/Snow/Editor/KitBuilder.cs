using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkyIsland;
using Snow.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Snow.EditorTools
{
    /// <summary>
    /// Tools / Snow / Kit / Build Kit: the snow_kit set (SNOW_KIT_CONTRACT.md 1.1) in Assets/Kit, shared by every map.
    /// Reads Assets/Kit/Data/Kit_Unity.json (tools/kit/make_unity_data.py from the contract). Textures T_Kit_*_px: point,
    /// no mipmaps, uncompressed. Materials M_Kit_*: URP Lit set up like the location core's PSX materials (no specular,
    /// no reflections, smoothness 0), lamps lit / _Off, screens, wires, additive halos. Models: normals imported, no
    /// import colliders, materials remapped by name. A prefab per model: box colliders (colliders), sockets as empty
    /// children, the IX_ parts as SkyIslandInteractable (hinge axes already turned for the +angle convention), the power
    /// parts' off materials and swaps, the lights of snow_kit_lights.json and halos, all on a <see cref="KitItem"/>.
    /// Re-running rewrites the same assets (same GUIDs).
    /// </summary>
    public static class KitBuilder
    {
        const string Root = "Assets/Kit", DataPath = Root + "/Data/Kit_Unity.json";
        const string Models = Root + "/Models", Textures = Root + "/Textures", Materials = Root + "/Materials", Prefabs = Root + "/Prefabs";

        [Serializable] class KitData { public string version; public Item[] items; }
        [Serializable] public class Item { public string name, fbx; public string[] materials; public Part[] parts; public Socket[] sockets; public Col[] colliders; public SwapDef[] swaps; public LightDef[] lights; }
        [Serializable] public class Part { public string name, kind, parent; public float[] axis, pivot; public float min, max, rest, glowSize; }
        [Serializable] public class Socket { public string name, kind, parent; public float[] pos, dir, up; }
        [Serializable] public class Col { public string name, parent; public float[] center, size; }
        [Serializable] public class SwapDef { public string part, on, off; }
        [Serializable] public class LightDef { public string name, parent, glow; public bool spot; public float angle, intensity, range, rpm; public float[] pos, dir, up, color; }

        // lit lamps: the lens glows by the panel's emission map at the contract's night_emit (Blender material props)
        static readonly Dictionary<string, float> LampEmit = new Dictionary<string, float>(StringComparer.Ordinal)
        { { "Amber", 3f }, { "Beacon", 4f }, { "Flood", 3f }, { "Green", 3f }, { "Red", 3f }, { "Street", 3f } };

        [MenuItem("Tools/Snow/Kit/Build Kit")]
        static void Menu() => Build();

        public static bool Build()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(DataPath);
            KitData data = asset != null ? JsonUtility.FromJson<KitData>(asset.text) : null;
            if (data?.items == null || data.items.Length == 0) { Debug.LogError($"[Snow] Kit: {DataPath} not found or empty."); return false; }
            foreach (string f in new[] { Materials, Prefabs }) Ensure(f);

            int tex = SetupTextures();
            Dictionary<string, Material> mats = BuildMaterials(data);
            int models = 0, prefabs = 0;
            var problems = new List<string>();
            try
            {
                for (int i = 0; i < data.items.Length; i++)
                {
                    Item it = data.items[i];
                    EditorUtility.DisplayProgressBar("Kit", it.name, (float)i / data.items.Length);
                    if (SetupModel(it, mats, problems)) models++;
                }
                for (int i = 0; i < data.items.Length; i++)
                    if (BuildPrefab(data.items[i], mats, problems)) prefabs++;
            }
            finally { EditorUtility.ClearProgressBar(); }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Snow] Kit {data.version}: {tex} textures, {mats.Count} materials, {models} models, {prefabs} prefabs in {Root}.");
            foreach (string p in problems) Debug.LogWarning("[Snow] Kit: " + p);
            return problems.Count == 0 && prefabs == data.items.Length;
        }

        // ------------------------------------------------------------------ textures
        static int SetupTextures()
        {
            int n = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Textures }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                bool dirty = ti.textureType != TextureImporterType.Default || ti.textureShape != TextureImporterShape.Texture2D ||
                             ti.filterMode != FilterMode.Point || ti.mipmapEnabled || ti.textureCompression != TextureImporterCompression.Uncompressed ||
                             !ti.sRGBTexture || ti.wrapMode != TextureWrapMode.Clamp || ti.npotScale != TextureImporterNPOTScale.None;
                if (dirty)
                {
                    ti.textureType = TextureImporterType.Default;
                    ti.textureShape = TextureImporterShape.Texture2D;
                    ti.filterMode = FilterMode.Point;
                    ti.mipmapEnabled = false;
                    ti.textureCompression = TextureImporterCompression.Uncompressed;
                    ti.sRGBTexture = true;
                    ti.wrapMode = TextureWrapMode.Clamp;
                    ti.npotScale = TextureImporterNPOTScale.None;
                    ti.SaveAndReimport();
                }
                n++;
            }
            return n;
        }

        static Texture2D Tex(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{Textures}/{name}.png");

        // ------------------------------------------------------------------ materials
        static Dictionary<string, Material> BuildMaterials(KitData data)
        {
            var mats = new Dictionary<string, Material>(StringComparer.Ordinal);
            Texture2D kit = Tex("T_Kit_Kit_A_px"), panel = Tex("T_Kit_Panel_A_px"), panelE = Tex("T_Kit_Panel_E_px");
            mats["M_Kit_Kit"] = Lit("M_Kit_Kit", kit, Color.white, null, 0f);
            mats["M_Kit_Panel"] = Lit("M_Kit_Panel", panel, Color.white, null, 0f);
            foreach (var kv in LampEmit)
            {
                mats[$"M_Kit_Lamp_{kv.Key}"] = Lit($"M_Kit_Lamp_{kv.Key}", panel, Color.white, panelE, kv.Value);
                mats[$"M_Kit_Lamp_{kv.Key}_Off"] = Lit($"M_Kit_Lamp_{kv.Key}_Off", panel, Color.white, null, 0f);
            }
            mats["M_Kit_Screen_Off"] = Lit("M_Kit_Screen_Off", Tex("T_Kit_Screen_Off_A_px"), Color.white, null, 0f);
            mats["M_Kit_Screen_On"] = Lit("M_Kit_Screen_On", Tex("T_Kit_Screen_On_A_px"), Color.white, Tex("T_Kit_Screen_On_A_px"), 1.8f);
            mats["M_Kit_Screen_On_Gate"] = Lit("M_Kit_Screen_On_Gate", Tex("T_Kit_Screen_On_Gate_A_px"), Color.white, Tex("T_Kit_Screen_On_Gate_A_px"), 1.8f);
            // snow_kit 1.1: the CRT glass's faces point into the case (normal +y, the model's front is -y) - drawn from both
            // sides until the model is fixed (OPEN for the Blender chat)
            foreach (string s in new[] { "M_Kit_Screen_Off", "M_Kit_Screen_On", "M_Kit_Screen_On_Gate" })
            {
                mats[s].SetFloat("_Cull", 0f);
                EditorUtility.SetDirty(mats[s]);
            }
            mats["M_Kit_Wire"] = Lit("M_Kit_Wire", null, new Color(0.012f, 0.012f, 0.012f), null, 0f);
            mats["M_Kit_WireBare"] = Lit("M_Kit_WireBare", null, new Color(0.32f, 0.33f, 0.34f), null, 0f);
            foreach (Item it in data.items)
                foreach (LightDef l in it.lights ?? new LightDef[0])
                    if (!string.IsNullOrEmpty(l.glow)) mats["M_Kit_" + l.glow] = Glow("M_Kit_" + l.glow, Lin(l.color), 2f);
            return mats;
        }

        static Color Lin(float[] c) => c != null && c.Length >= 3 ? new Color(c[0], c[1], c[2]) : Color.white;

        static Material Load(string name, Shader shader)
        {
            string path = $"{Materials}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader) m.shader = shader;
            return m;
        }

        static void Kw(Material m, string kw, bool on) { if (on) m.EnableKeyword(kw); else m.DisableKeyword(kw); }

        /// <summary>URP Lit like SkyIslandMaterials.SetupURP for the PSX "lit" / "emissive" types with noSpecular (colour
        /// linear in, set as gamma; emission linear white x <paramref name="emit"/>).</summary>
        static Material Lit(string name, Texture2D albedo, Color linear, Texture2D emissionMap, float emit)
        {
            Material m = Load(name, Shader.Find("Universal Render Pipeline/Lit"));
            m.SetFloat("_WorkflowMode", 1f);
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_Blend", 0f);
            m.SetTexture("_BaseMap", albedo);
            m.SetTexture("_MainTex", albedo);
            m.SetColor("_BaseColor", linear.gamma);
            m.SetColor("_Color", linear.gamma);
            m.SetFloat("_Smoothness", 0f);
            m.SetFloat("_Metallic", 0f);
            m.SetTexture("_MetallicGlossMap", null);
            m.SetTexture("_OcclusionMap", null);
            m.SetTexture("_BumpMap", null);
            Kw(m, "_METALLICSPECGLOSSMAP", false);
            Kw(m, "_OCCLUSIONMAP", false);
            Kw(m, "_NORMALMAP", false);
            Kw(m, "_SPECULAR_SETUP", false);
            bool emissive = emit > 0f;
            m.SetColor("_EmissionColor", emissive ? new Color(emit, emit, emit, 1f).gamma : Color.black);
            m.SetTexture("_EmissionMap", emissive ? emissionMap : null);
            Kw(m, "_EMISSION", emissive);
            m.globalIlluminationFlags = emissive ? MaterialGlobalIlluminationFlags.RealtimeEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            m.SetFloat("_AlphaClip", 0f);
            Kw(m, "_ALPHATEST_ON", false);
            Kw(m, "_SURFACE_TYPE_TRANSPARENT", false);
            m.SetOverrideTag("RenderType", "Opaque");
            m.renderQueue = (int)RenderQueue.Geometry;
            m.SetFloat("_SrcBlend", 1f);
            m.SetFloat("_DstBlend", 0f);
            m.SetFloat("_ZWrite", 1f);
            m.SetFloat("_Cull", 2f);
            m.SetFloat("_ReceiveShadows", 1f);
            m.SetFloat("_EnvironmentReflections", 0f);
            m.SetFloat("_SpecularHighlights", 0f);
            Kw(m, "_ENVIRONMENTREFLECTIONS_OFF", true);
            Kw(m, "_SPECULARHIGHLIGHTS_OFF", true);
            m.SetShaderPassEnabled("ShadowCaster", !emissive);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Additive halo on the core's SkyIsland/LightGlow (as SkyIslandMaterials.SetupGlow).</summary>
        static Material Glow(string name, Color linear, float intensity)
        {
            Shader sh = Shader.Find("SkyIsland/LightGlow");
            if (sh == null) return null;
            Material m = Load(name, sh);
            float mx = Mathf.Max(linear.maxColorComponent, 1e-4f);
            m.SetColor("_Color", new Color(linear.r / mx, linear.g / mx, linear.b / mx).gamma);
            m.SetFloat("_Intensity", intensity * mx);
            m.SetFloat("_Softness", 2.6f);
            if (m.HasProperty("_FogFloor")) m.SetFloat("_FogFloor", 0f);
            m.SetFloat("_NearFade", 3f);
            m.renderQueue = (int)RenderQueue.Transparent + 10;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ models
        static bool SetupModel(Item it, Dictionary<string, Material> mats, List<string> problems)
        {
            if (!(AssetImporter.GetAtPath(it.fbx) is ModelImporter mi)) { problems.Add($"{it.fbx} not found"); return false; }
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.None;
            mi.addCollider = false;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importCameras = false;
            mi.importLights = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            foreach (string mn in it.materials ?? new string[0])
                if (mats.TryGetValue(mn, out Material m) && m != null)
                    mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), mn), m);
            mi.SaveAndReimport();
            return true;
        }

        // ------------------------------------------------------------------ prefabs
        static bool BuildPrefab(Item it, Dictionary<string, Material> mats, List<string> problems)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(it.fbx);
            if (model == null) { problems.Add($"{it.name}: no model"); return false; }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                Transform root = inst.transform;
                Transform Find(string n) => n == it.name ? root : Deep(root, n);
                var kit = inst.AddComponent<KitItem>();
                kit.item = it.name;

                foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    r.shadowCastingMode = ShadowCastingMode.On;
                    r.receiveShadows = true;
                }
                foreach (Col c in it.colliders ?? new Col[0])
                {
                    Transform t = Find(c.parent);
                    if (t == null) { problems.Add($"{it.name}: collider {c.name} parent {c.parent} missing"); t = root; }
                    var bc = t.gameObject.AddComponent<BoxCollider>();
                    bc.center = V(c.center);
                    bc.size = V(c.size);
                }
                foreach (Socket s in it.sockets ?? new Socket[0])
                {
                    Transform parent = Find(s.parent) ?? root;
                    var go = new GameObject(s.name);
                    go.transform.SetParent(parent, false);
                    go.transform.position = root.TransformPoint(V(s.pos));
                    Vector3 dir = V(s.dir), up = V(s.up);
                    if (dir.sqrMagnitude > 1e-6f)
                        go.transform.rotation = root.rotation * Look(dir, up);
                }
                foreach (Part p in it.parts ?? new Part[0])
                {
                    if (p.name.StartsWith("IX_", StringComparison.Ordinal))
                    {
                        Transform t = Find(p.name);
                        if (t == null) { problems.Add($"{it.name}: part {p.name} missing"); continue; }
                        if (!t.TryGetComponent(out SkyIslandInteractable ia)) ia = t.gameObject.AddComponent<SkyIslandInteractable>();
                        ia.kind = p.kind == "slide" ? SkyIslandInteractable.Kind.Slide : SkyIslandInteractable.Kind.Hinge;
                        Vector3 a = V(p.axis);
                        ia.axis = a.sqrMagnitude > 1e-8f ? a.normalized : Vector3.up;
                        ia.min = p.min;
                        ia.max = p.max;
                        ia.locked = false;
                        ia.needsPower = false;
                        // rest (clamps lifted 0.06): only the fields, Awake turns the part from its modelled pose
                        float rest = Mathf.Approximately(p.max, p.min) ? 0f : Mathf.Clamp01((p.rest - p.min) / (p.max - p.min));
                        ia.openAmount = rest;
                        ia.target = rest;
                    }
                    else if (p.kind == "spin")
                    {
                        kit.spin = Find(p.name);
                        kit.spinAxis = V(p.axis).normalized;
                    }
                    else if (p.name.StartsWith("Glow_", StringComparison.Ordinal) && p.glowSize > 0f)
                    {
                        mats.TryGetValue("M_Kit_" + p.name, out Material gm);
                        GameObject g = Halo(p.name, root, V(p.pivot), p.glowSize, gm);
                        kit.glows = kit.glows.Append(g).ToArray();
                    }
                }
                var swaps = new List<KitItem.Swap>();
                foreach (SwapDef sd in it.swaps ?? new SwapDef[0])
                {
                    if (!mats.TryGetValue(sd.on, out Material on) || !mats.TryGetValue(sd.off, out Material off)) { problems.Add($"{it.name}: swap {sd.part} materials {sd.on}/{sd.off}"); continue; }
                    int found = 0;
                    foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
                    {
                        Material[] ms = r.sharedMaterials;
                        bool changed = false;
                        for (int k = 0; k < ms.Length; k++)
                        {
                            if (ms[k] != on && ms[k] != off) continue;
                            if (swaps.Any(s => s.renderer == r && s.slot == k)) continue;
                            swaps.Add(new KitItem.Swap { renderer = r, slot = k, on = on, off = off });
                            ms[k] = off;
                            changed = true;
                            found++;
                        }
                        if (changed) r.sharedMaterials = ms;
                    }
                    if (found == 0) problems.Add($"{it.name}: power part {sd.part} ({sd.on}) not found on any renderer");
                }
                kit.swaps = swaps.ToArray();
                var lights = new List<Light>();
                foreach (LightDef ld in it.lights ?? new LightDef[0])
                {
                    Transform parent = Find(ld.parent) ?? root;
                    var go = new GameObject(ld.name);
                    go.transform.SetParent(parent, false);
                    go.transform.position = root.TransformPoint(V(ld.pos));
                    go.transform.rotation = root.rotation * Look(V(ld.dir), V(ld.up));
                    var l = go.AddComponent<Light>();
                    l.type = ld.spot ? LightType.Spot : LightType.Point;
                    if (ld.spot)
                    {
                        l.spotAngle = Mathf.Clamp(ld.angle, 1f, 179f);
                        l.innerSpotAngle = l.spotAngle * 0.55f;
                    }
                    Color c = Lin(ld.color);
                    float mx = Mathf.Max(c.maxColorComponent, 1e-4f);
                    l.color = new Color(c.r / mx, c.g / mx, c.b / mx).gamma;
                    l.intensity = ld.intensity * mx;
                    l.range = ld.range;
                    l.shadows = LightShadows.None;
                    l.lightmapBakeType = LightmapBakeType.Realtime;
                    l.enabled = false;
                    lights.Add(l);
                    if (ld.rpm > 0f) kit.rpm = ld.rpm;
                }
                kit.lights = lights.ToArray();
                PrefabUtility.SaveAsPrefabAsset(inst, $"{Prefabs}/{it.name}.prefab");
                return true;
            }
            finally { Object.DestroyImmediate(inst); }
        }

        static Quaternion Look(Vector3 dir, Vector3 up)
        {
            if (up.sqrMagnitude < 1e-6f || Mathf.Abs(Vector3.Dot(dir.normalized, up.normalized)) > 0.99f)
                up = Mathf.Abs(dir.normalized.y) > 0.95f ? Vector3.forward : Vector3.up;
            return Quaternion.LookRotation(dir, up);
        }

        static GameObject Halo(string name, Transform root, Vector3 local, float size, Material m)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = local;
            go.transform.localScale = Vector3.one * size;
            go.AddComponent<MeshFilter>().sharedMesh = GlowQuad();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.allowOcclusionWhenDynamic = false;
            go.SetActive(false);
            return go;
        }

        /// <summary>The core's camera-facing quad (SkyIslandBuilder.GlowQuad), the kit's own copy.</summary>
        static Mesh GlowQuad()
        {
            string path = $"{Materials}/Kit_GlowQuad.asset";
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (m != null) return m;
            m = new Mesh { name = "Kit_GlowQuad" };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.bounds = new Bounds(Vector3.zero, new Vector3(2.5f, 2.5f, 2.5f));
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Vector3 V(float[] a) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;

        static Transform Deep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform r = Deep(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        static void Ensure(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            Ensure(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
