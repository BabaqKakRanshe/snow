using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Snow.EditorTools
{
    /// <summary>
    /// Tools / Snow / Items / Build Pager: the forest_pager 1.0 pack (source/assets/forest_pager, docs/gdd/PAGER_CONTRACT.md) as
    /// the worker's pager in Assets/Snow/Items/Pager, the way <see cref="StartPropsBuilder"/> builds the start props. The FBX and
    /// the textures come from tools/snow_style/work/forest_pager/export_fbx.py. Textures: point, no mipmaps, uncompressed.
    /// Materials M_Pager_*: URP Lit (PSX: no specular, no reflections) - the atlas, the panel, the LCD lit with the pack's sample
    /// list, the LCD glowing blank (the game prints its list over it, <see cref="Snow.Tasks.Pager"/>) and off, the LED red and
    /// off. Prefab Resources/SnowPager: the model only (it is a held device, no collider, no shadows). Re-running rewrites the
    /// same assets (same GUIDs).
    /// </summary>
    public static class PagerBuilder
    {
        const string Root = "Assets/Snow/Items/Pager";
        const string Fbx = Root + "/Models/Item_Pager.fbx";
        const string Textures = Root + "/Textures";
        const string Materials = Root + "/Materials";
        const string Prefab = Root + "/Resources/SnowPager.prefab";

        [MenuItem("Tools/Snow/Items/Build Pager")]
        static void Menu() => Build();

        public static bool Build()
        {
            int tex = SetupTextures();
            System.IO.Directory.CreateDirectory(Materials);
            System.IO.Directory.CreateDirectory(Root + "/Resources");
            Texture2D small = Tex("T_Pager_Small_BaseColor"), panel = Tex("T_Pager_Panel_BaseColor");
            Texture2D lcd = Tex("T_Pager_LCD_BaseColor"), lcdE = Tex("T_Pager_LCD_Emission");
            var mats = new Dictionary<string, Material>
            {
                ["M_Pager_Small"] = Lit("M_Pager_Small", small, null, Color.black),
                ["M_Pager_Panel"] = Lit("M_Pager_Panel", panel, null, Color.black),
                ["M_Pager_LCD"] = Lit("M_Pager_LCD", lcd, lcdE, Color.white),
                ["M_Pager_LCD_Glow"] = Lit("M_Pager_LCD_Glow", lcd, null, new Color(0.55f, 1.10f, 0.40f)),   // the backlight, no text
                ["M_Pager_LCD_Off"] = Lit("M_Pager_LCD_Off", lcd, null, Color.black),
                ["M_Pager_LEDR"] = Lit("M_Pager_LEDR", null, null, new Color(2.0f, 0.25f, 0.15f), new Color(0.47f, 0.12f, 0.09f)),
                ["M_Pager_LED_Off"] = Lit("M_Pager_LED_Off", null, null, Color.black, new Color(0.16f, 0.12f, 0.11f)),
            };
            if (!(AssetImporter.GetAtPath(Fbx) is ModelImporter mi)) { Debug.LogError($"[Snow] Pager: {Fbx} not found (run export_fbx.py)."); return false; }
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

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.name = "SnowPager";
            foreach (var r in inst.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = ShadowCastingMode.Off;                   // a held device: no shadow, none received
                r.receiveShadows = false;
            }
            // readable at night (RESEARCH_UI_3.md §5): the pager's renderers on the world's lights and on its own layer
            uint mask = 1u | (1u << Snow.Tasks.PagerModel.ViewmodelLayer);
            foreach (var r in inst.GetComponentsInChildren<Renderer>()) r.renderingLayerMask = mask;
            var pm = inst.GetComponent<Snow.Tasks.PagerModel>() ?? inst.AddComponent<Snow.Tasks.PagerModel>();
            Transform Part(string n) { var t = inst.transform.Find(n); if (t == null) Debug.LogWarning($"[Snow] Pager: no {n} in the model."); return t; }
            pm.lcd = Part("Pager_LCD")?.GetComponent<MeshRenderer>();
            pm.led = Part("Pager_LED")?.GetComponent<MeshRenderer>();
            pm.keys = new[] { Part("Pager_Key_Up"), Part("Pager_Key_Read"), Part("Pager_Key_Down") };
            pm.lcdGlow = mats["M_Pager_LCD_Glow"];
            pm.lcdOff = mats["M_Pager_LCD_Off"];
            pm.ledOn = mats["M_Pager_LEDR"];
            pm.ledOff = mats["M_Pager_LED_Off"];
            if (pm.lcd != null) pm.lcd.sharedMaterial = pm.lcdOff;
            // the backlight: a green point light just in front of the glass (lights the keys, the case, a little of the world);
            // the fill: a dim warm point light up right of the pager (as from the torch in the right hand), the pager only
            Vector3 glass = pm.lcd != null ? pm.lcd.transform.localPosition : new Vector3(0f, 0.04f, 0.011f);
            pm.backlight = PointLight(inst.transform, "Pager_Backlight", glass + new Vector3(0f, -0.004f, 0.03f),
                                      new Color(0.45f, 1f, 0.35f), 1.4f, 0.35f, ~0u);
            pm.fill = PointLight(inst.transform, "Pager_Fill", new Vector3(-0.10f, 0.14f, 0.12f),
                                 new Color(1f, 0.92f, 0.80f), 0.55f, 0.6f, 1u << Snow.Tasks.PagerModel.ViewmodelLayer);
            pm.backlight.enabled = false;
            pm.dim = Dim("M_Pager_Dim");
            PrefabUtility.SaveAsPrefabAsset(inst, Prefab);
            Object.DestroyImmediate(inst);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Snow] Pager: {tex} textures, {mats.Count} materials, prefab {Prefab}.");
            return true;
        }

        static Light PointLight(Transform parent, string name, Vector3 local, Color color, float intensity, float range, uint layers)
        {
            var t = parent.Find(name);
            var go = t != null ? t.gameObject : new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            var l = go.GetComponent<Light>() ?? go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
            l.lightmapBakeType = LightmapBakeType.Realtime;
            l.renderingLayerMask = (int)layers;
            return l;
        }

        /// <summary>URP Unlit, alpha blended, black: the quad that darkens the world behind the raised pager (alpha by script).</summary>
        static Material Dim(string name)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            string path = $"{Materials}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader) { name = name }; AssetDatabase.CreateAsset(m, path); }
            else if (m.shader != shader) m.shader = shader;
            m.SetColor("_BaseColor", new Color(0f, 0f, 0f, 0.35f));
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

        static void Kw(Material m, string kw, bool on) { if (on) m.EnableKeyword(kw); else m.DisableKeyword(kw); }

        /// <summary>URP Lit as StartPropsBuilder's (smoothness 0, no specular, no reflections); emission = the map (or white)
        /// x <paramref name="emission"/> (linear), black = none; <paramref name="baseColor"/> when there is no albedo.</summary>
        static Material Lit(string name, Texture2D albedo, Texture2D emissionMap, Color emission, Color? baseColor = null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            string path = $"{Materials}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader) { name = name }; AssetDatabase.CreateAsset(m, path); }
            else if (m.shader != shader) m.shader = shader;
            Color bc = baseColor.HasValue ? baseColor.Value.gamma : Color.white;
            m.SetFloat("_WorkflowMode", 1f);
            m.SetFloat("_Surface", 0f);
            m.SetTexture("_BaseMap", albedo);
            m.SetTexture("_MainTex", albedo);
            m.SetColor("_BaseColor", bc);
            m.SetColor("_Color", bc);
            m.SetFloat("_Smoothness", 0f);
            m.SetFloat("_Metallic", 0f);
            Kw(m, "_METALLICSPECGLOSSMAP", false);
            Kw(m, "_NORMALMAP", false);
            bool emissive = emission.maxColorComponent > 0f;
            m.SetColor("_EmissionColor", emissive ? emission.gamma : Color.black);
            m.SetTexture("_EmissionMap", emissive ? emissionMap : null);
            Kw(m, "_EMISSION", emissive);
            m.globalIlluminationFlags = emissive ? MaterialGlobalIlluminationFlags.RealtimeEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            m.SetFloat("_AlphaClip", 0f);
            Kw(m, "_ALPHATEST_ON", false);
            Kw(m, "_SURFACE_TYPE_TRANSPARENT", false);
            m.SetOverrideTag("RenderType", "Opaque");
            m.renderQueue = (int)RenderQueue.Geometry;
            m.SetFloat("_ZWrite", 1f);
            m.SetFloat("_Cull", 2f);
            m.SetFloat("_EnvironmentReflections", 0f);
            m.SetFloat("_SpecularHighlights", 0f);
            Kw(m, "_ENVIRONMENTREFLECTIONS_OFF", true);
            Kw(m, "_SPECULARHIGHLIGHTS_OFF", true);
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
