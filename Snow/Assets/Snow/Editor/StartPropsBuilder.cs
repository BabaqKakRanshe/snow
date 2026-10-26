using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkyIsland;
using Snow.Levels.Forest;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Snow.EditorTools
{
    /// <summary>
    /// Tools / Snow / Forest / Build Start Props: the forest_start 1.0 pack (source/assets/forest_start, FOREST_START_CONTRACT.md)
    /// as prefabs in Assets/Locations/Forest/Start, the way <see cref="KitBuilder"/> builds the kit. Reads Start_Unity.json
    /// (tools/forest_start/make_unity_data.py); the FBX come from tools/forest_start/export_fbx.py. Textures: point, no mipmaps,
    /// uncompressed. Materials M_Start_*: URP Lit like the kit's (atlases, panel, lamp lenses lit / _Off), transparent glass,
    /// additive halos. Prefabs: box colliders of the contract, the sockets as empty children, the doors standing at their
    /// data pose (static: no interaction until the pack joins the Forest integration), the lamps' lights driven by a
    /// <see cref="ForestStartLamp"/> (the tripod: battery, dies; the booth lamp: lives a little longer). Re-running rewrites
    /// the same assets (same GUIDs). Tools / Snow / Forest / Build Hut Yard Props builds the forest_hut_yard 1.0 pack
    /// (FOREST_HUT_YARD_CONTRACT.md: outhouse, feeder, timber stack, trail-head pointer with its lantern, porch, map) the same way
    /// into Assets/Locations/Forest/HutYard (HutYard_Unity.json from tools/forest_hut_yard).
    /// </summary>
    public static class StartPropsBuilder
    {
        const string LightLayersFrom = "Assets/Locations/Forest/Prefabs/LightPole.prefab";

        /// <summary>One pack: its folder, data file, material prefix and materials.</summary>
        class Pack
        {
            public string label, root, data, prefix;
            public Func<Dictionary<string, Material>> materials;
        }

        static readonly Pack StartPack = new Pack
        {
            label = "Start props", root = "Assets/Locations/Forest/Start", data = "Start_Unity.json", prefix = "M_Start_", materials = StartMaterials,
        };
        static readonly Pack YardPack = new Pack
        {
            label = "Hut yard props", root = "Assets/Locations/Forest/HutYard", data = "HutYard_Unity.json", prefix = "M_Yard_", materials = YardMaterials,
        };

        static Pack _p = StartPack;
        static string Root => _p.root;
        static string DataPath => Root + "/" + _p.data;
        static string Textures => Root + "/Textures";
        static string Materials => Root + "/Materials";
        static string Prefabs => Root + "/Prefabs";

        [Serializable] class Data { public string version; public Item[] items; }
        [Serializable] class Item { public string name, fbx; public Col[] colliders; public LightDef[] lights; public Socket[] sockets; public Door[] doors; }
        [Serializable] class Col { public string name, parent; public float[] center, size; }
        [Serializable] class Socket { public string name, kind; public float[] pos; }
        [Serializable] class Door { public string name, ix; public float[] pivot, axis; public float min, max, pose; }
        [Serializable] class LightDef { public string name, lens, glow, group; public bool spot; public float angle, intensity, range; public float[] pos, dir, up, color; }

        // lamp life (minutes) by contract group: the tripod's battery goes first, the booth lamp a little later; the trail-head
        // lantern lives until X01 (ForestHutDressStep hands it to ForestOpening.x01Lamps)
        static readonly Dictionary<string, float> Life = new Dictionary<string, float> { { "battery", 12f }, { "post", 14f }, { "trail", 600f } };

        [MenuItem("Tools/Snow/Forest/Build Start Props")]
        static void Menu() => Build();

        [MenuItem("Tools/Snow/Forest/Build Hut Yard Props")]
        static void MenuYard() => BuildHutYard();

        public static bool Build() => Build(StartPack);

        public static bool BuildHutYard() => Build(YardPack);

        static bool Build(Pack pack)
        {
            _p = pack;
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(DataPath);
            Data data = asset != null ? JsonUtility.FromJson<Data>(asset.text) : null;
            if (data?.items == null || data.items.Length == 0) { Debug.LogError($"[Snow] {_p.label}: {DataPath} not found or empty."); return false; }
            int tex = SetupTextures();
            Directory.CreateDirectory(Materials);
            Dictionary<string, Material> mats = _p.materials();
            var problems = new List<string>();
            int models = data.items.Count(it => SetupModel(it, mats, problems));
            int prefabs = data.items.Count(it => BuildPrefab(it, mats, problems));
            AssetDatabase.SaveAssets();
            Debug.Log($"[Snow] {_p.label} {data.version}: {tex} textures, {mats.Count} materials, {models} models, {prefabs} prefabs in {Root}.");
            foreach (string p in problems) Debug.LogWarning($"[Snow] {_p.label}: " + p);
            return problems.Count == 0 && prefabs == data.items.Length;
        }

        // ------------------------------------------------------------------ textures
        static int SetupTextures()
        {
            int n = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Textures }))
            {
                if (!(AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) is TextureImporter ti)) continue;
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
        static Dictionary<string, Material> StartMaterials()
        {
            Texture2D start = Tex("T_Start_Start_BaseColor"), props = Tex("T_Start_StartProps_BaseColor");
            Texture2D panel = Tex("T_Start_Panel_BaseColor"), panelE = Tex("T_Start_Panel_Emission");
            var m = new Dictionary<string, Material>(StringComparer.Ordinal)
            {
                ["M_Start_Start"] = Lit("M_Start_Start", start, null, 0f),
                ["M_Start_StartProps"] = Lit("M_Start_StartProps", props, null, 0f),
                ["M_Start_Panel"] = Lit("M_Start_Panel", panel, null, 0f),
                ["M_Start_Lamp_Booth"] = Lit("M_Start_Lamp_Booth", panel, panelE, 3f),
                ["M_Start_Lamp_Booth_Off"] = Lit("M_Start_Lamp_Booth_Off", panel, null, 0f),
                ["M_Start_Lamp_Flood"] = Lit("M_Start_Lamp_Flood", panel, panelE, 4f),
                ["M_Start_Lamp_Flood_Off"] = Lit("M_Start_Lamp_Flood_Off", panel, null, 0f),
                ["M_Start_Glass"] = Glass("M_Start_Glass", new Color(0.12f, 0.13f, 0.12f), 0.45f),
                ["M_Start_GlassDirty"] = Glass("M_Start_GlassDirty", new Color(0.17f, 0.16f, 0.13f), 0.62f),
                ["M_Start_Glow"] = Glow("M_Start_Glow", panelE, 1.4f),
            };
            return m;
        }

        static Dictionary<string, Material> YardMaterials()
        {
            Texture2D wood = Tex("T_Yard_YardWood_BaseColor"), props = Tex("T_Yard_YardProps_BaseColor");
            Texture2D panel = Tex("T_Yard_Panel_BaseColor"), panelE = Tex("T_Yard_Panel_Emission");
            return new Dictionary<string, Material>(StringComparer.Ordinal)
            {
                ["M_Yard_YardWood"] = Lit("M_Yard_YardWood", wood, null, 0f),
                ["M_Yard_YardProps"] = Lit("M_Yard_YardProps", props, null, 0f),
                ["M_Yard_Panel"] = Lit("M_Yard_Panel", panel, null, 0f),
                ["M_Yard_Lamp_Lant"] = Lit("M_Yard_Lamp_Lant", panel, panelE, 3f),
                ["M_Yard_Lamp_Lant_Off"] = Lit("M_Yard_Lamp_Lant_Off", panel, null, 0f),
                ["M_Yard_Glow"] = Glow("M_Yard_Glow", panelE, 1.4f),
            };
        }

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

        /// <summary>URP Lit as the kit's (PSX: no specular, no reflections, smoothness 0); emission white x emit by the map.</summary>
        static Material Lit(string name, Texture2D albedo, Texture2D emissionMap, float emit)
        {
            Material m = Load(name, Shader.Find("Universal Render Pipeline/Lit"));
            m.SetFloat("_WorkflowMode", 1f);
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_Blend", 0f);
            m.SetTexture("_BaseMap", albedo);
            m.SetTexture("_MainTex", albedo);
            m.SetColor("_BaseColor", Color.white);
            m.SetColor("_Color", Color.white);
            m.SetFloat("_Smoothness", 0f);
            m.SetFloat("_Metallic", 0f);
            Kw(m, "_METALLICSPECGLOSSMAP", false);
            Kw(m, "_NORMALMAP", false);
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

        /// <summary>Window glass: URP Lit, alpha blended, both sides, no shadows.</summary>
        static Material Glass(string name, Color linear, float alpha)
        {
            Material m = Lit(name, null, null, 0f);
            Color c = linear.gamma;
            c.a = alpha;
            m.SetColor("_BaseColor", c);
            m.SetColor("_Color", c);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            Kw(m, "_SURFACE_TYPE_TRANSPARENT", true);
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetShaderPassEnabled("ShadowCaster", false);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>The halo cards: URP Unlit, additive, both sides, the panel's emission tiles x <paramref name="gain"/>.</summary>
        static Material Glow(string name, Texture2D map, float gain)
        {
            Material m = Load(name, Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetTexture("_BaseMap", map);
            m.SetTexture("_MainTex", map);
            m.SetColor("_BaseColor", new Color(gain, gain, gain, 1f));
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 2f);                                   // additive
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            Kw(m, "_SURFACE_TYPE_TRANSPARENT", true);
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent + 10;
            m.SetShaderPassEnabled("ShadowCaster", false);
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
            foreach (var kv in mats)
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
            mi.SaveAndReimport();
            return true;
        }

        // ------------------------------------------------------------------ prefabs
        static Vector3 V(float[] a) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;

        static Transform Deep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                Transform f = Deep(c, name);
                if (f != null) return f;
            }
            return null;
        }

        static Quaternion Look(Vector3 dir, Vector3 up)
        {
            if (up.sqrMagnitude < 1e-6f || Mathf.Abs(Vector3.Dot(dir.normalized, up.normalized)) > 0.99f)
                up = Mathf.Abs(dir.normalized.y) > 0.95f ? Vector3.forward : Vector3.up;
            return Quaternion.LookRotation(dir, up);
        }

        static bool BuildPrefab(Item it, Dictionary<string, Material> mats, List<string> problems)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(it.fbx);
            if (model == null) { problems.Add($"{it.name}: no model"); return false; }
            Directory.CreateDirectory(Prefabs);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                inst.name = it.name;
                Transform root = inst.transform;
                Transform Find(string n) => n == it.name ? root : Deep(root, n);
                foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    bool see = r.name.StartsWith("Glow_", StringComparison.Ordinal) || r.sharedMaterials.Any(x => x != null && x.name.StartsWith(_p.prefix + "Glass", StringComparison.Ordinal));
                    r.shadowCastingMode = see ? ShadowCastingMode.Off : ShadowCastingMode.On;
                    r.receiveShadows = !see;
                }
                // colliders: the contract's centres are in the root frame; the parts stand unturned at this point
                foreach (Col c in it.colliders ?? new Col[0])
                {
                    Transform t = Find(c.parent);
                    if (t == null) { problems.Add($"{it.name}: collider {c.name} parent {c.parent} missing"); t = root; }
                    var bc = t.gameObject.AddComponent<BoxCollider>();
                    bc.center = t.InverseTransformPoint(root.TransformPoint(V(c.center)));
                    bc.size = V(c.size);
                }
                foreach (Socket s in it.sockets ?? new Socket[0])
                {
                    var go = new GameObject(s.name);
                    go.transform.SetParent(root, false);
                    go.transform.localPosition = V(s.pos);
                }
                foreach (Door d in it.doors ?? new Door[0])
                {
                    Transform t = Find(d.name);
                    if (t == null) { problems.Add($"{it.name}: door {d.name} missing"); continue; }
                    // turn about the hinge line (the part's origin is on it): the static pose of the data
                    Vector3 axis = V(d.axis).sqrMagnitude > 1e-8f ? V(d.axis).normalized : Vector3.up;
                    t.localRotation = Quaternion.AngleAxis(d.pose, axis) * t.localRotation;
                    if (string.IsNullOrEmpty(d.ix)) continue;
                    // a game door: the core's openable part under its scene name; the Forest integration gathers it (when the
                    // prefab stands before the parts are gathered: ForestHutDressStep.PlaceEarly) and puts a NetworkDoor on it.
                    // The contract opens outward by +angle, Unity by -angle about the same axis: the part turns about -axis
                    t.name = d.ix;
                    var part = t.gameObject.AddComponent<SkyIslandInteractable>();
                    part.kind = SkyIslandInteractable.Kind.Hinge;
                    part.axis = -axis;
                    part.min = d.min;
                    part.max = d.max;
                }
                BuildLamp(it, inst, root, Find, mats, problems);
                PrefabUtility.SaveAsPrefabAsset(inst, $"{Prefabs}/{it.name}.prefab");
                return true;
            }
            finally { Object.DestroyImmediate(inst); }
        }

        static void BuildLamp(Item it, GameObject inst, Transform root, Func<string, Transform> find, Dictionary<string, Material> mats, List<string> problems)
        {
            if (it.lights == null || it.lights.Length == 0) return;
            // the map's lights reach the renderers on rendering layer 2 (the dress step's Place): same layers as the yard lamps
            Light layersFrom = AssetDatabase.LoadAssetAtPath<GameObject>(LightLayersFrom)?.GetComponentInChildren<Light>(true);
            var lights = new List<Light>();
            var glows = new List<Renderer>();
            Renderer lens = null;
            Material on = null, off = null;
            float life = 12f;
            foreach (LightDef ld in it.lights)
            {
                var go = new GameObject(ld.name);
                go.transform.SetParent(root, false);
                go.transform.localPosition = V(ld.pos);
                go.transform.localRotation = Look(V(ld.dir), V(ld.up));
                var l = go.AddComponent<Light>();
                l.type = ld.spot ? LightType.Spot : LightType.Point;
                if (ld.spot)
                {
                    l.spotAngle = Mathf.Clamp(ld.angle, 1f, 179f);
                    l.innerSpotAngle = l.spotAngle * 0.55f;
                }
                Color c = ld.color != null && ld.color.Length >= 3 ? new Color(ld.color[0], ld.color[1], ld.color[2]) : Color.white;
                float mx = Mathf.Max(c.maxColorComponent, 1e-4f);
                l.color = new Color(c.r / mx, c.g / mx, c.b / mx).gamma;
                l.intensity = ld.intensity;
                l.range = ld.range;
                l.shadows = LightShadows.None;
                l.lightmapBakeType = LightmapBakeType.Realtime;
                if (layersFrom != null)
                {
                    l.renderingLayerMask = layersFrom.renderingLayerMask;
                    if (layersFrom.TryGetComponent(out UniversalAdditionalLightData src))
                    {
                        if (!go.TryGetComponent(out UniversalAdditionalLightData dst)) dst = go.AddComponent<UniversalAdditionalLightData>();
                        dst.renderingLayers = src.renderingLayers;
                    }
                }
                lights.Add(l);
                Transform lt = find(ld.lens), gt = find(ld.glow);
                if (lt == null || !lt.TryGetComponent(out lens)) problems.Add($"{it.name}: lens {ld.lens} missing");
                if (gt != null && gt.TryGetComponent(out Renderer g)) { glows.Add(g); g.enabled = false; }
                else problems.Add($"{it.name}: glow {ld.glow} missing");
                string kind = ld.lens.Replace("Lens_", "");
                mats.TryGetValue($"{_p.prefix}Lamp_{kind}", out on);
                mats.TryGetValue($"{_p.prefix}Lamp_{kind}_Off", out off);
                if (lens != null && off != null) lens.sharedMaterial = off;   // off in the asset; the lamp script lights it
                if (Life.TryGetValue(ld.group ?? "", out float lf)) life = lf;
            }
            var lamp = inst.AddComponent<ForestStartLamp>();
            lamp.lights = lights.ToArray();
            lamp.glows = glows.ToArray();
            lamp.lens = lens;
            lamp.lensSlot = 0;
            lamp.lensOn = on;
            lamp.lensOff = off;
            lamp.intensity = lights.Count > 0 ? lights[0].intensity : lamp.intensity;
            lamp.lifeMinutes = life;
        }
    }
}
