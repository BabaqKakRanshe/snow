using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
#if SKYISLAND_HDRP
using UnityEngine.Rendering.HighDefinition;
#endif

namespace SkyIsland.EditorTools
{
    public static partial class SkyIslandBuilder
    {
        // ------------------------------------------------------------------ materials
        public static Dictionary<string, Material> BuildMaterials(Context ctx)
        {
            EnsureFolder(ctx.MatFolder);
            var result = new Dictionary<string, Material>();
            bool glowWarned = false;
            foreach (var s in ctx.materials.materials)
            {
                string path = $"{ctx.MatFolder}/{s.name}.mat";
                bool glow = s.type == "glow";
                if (glow && ctx.pipe == Pipe.HDRP)
                {
                    if (!glowWarned) Debug.LogWarning("[SkyIsland] HDRP: fog halos (SkyIsland/LightGlow) are Built-in/URP only and are skipped.");
                    glowWarned = true;
                    continue;
                }
                Shader sh = PickShader(ctx.pipe, s);
                if (sh == null)
                {
                    Debug.LogError($"[SkyIsland] shader not found for {s.name} ({ctx.pipe})");
                    continue;
                }
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(sh) { name = s.name };
                    AssetDatabase.CreateAsset(mat, path);
                }
                else if (mat.shader != sh)
                {
                    mat.shader = sh;
                }
                if (glow) SetupGlow(ctx, mat, s);
                else
                {
                    switch (ctx.pipe)
                    {
                        case Pipe.URP: SetupURP(ctx, mat, s); break;
                        case Pipe.HDRP: SetupHDRP(ctx, mat, s); break;
                        default: SetupBuiltIn(ctx, mat, s); break;
                    }
                }
                mat.enableInstancing = s.instancing || s.type == "cutout" || glow;
                EditorUtility.SetDirty(mat);
                result[s.name] = mat;
            }
            AssetDatabase.SaveAssets();
            return result;
        }

        public static Dictionary<string, Material> LoadMaterials(Context ctx)
        {
            var result = new Dictionary<string, Material>();
            foreach (var s in ctx.materials.materials)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>($"{ctx.MatFolder}/{s.name}.mat");
                if (m != null) result[s.name] = m;
            }
            if (result.Count == 0) result = BuildMaterials(ctx);
            return result;
        }

        static Shader PickShader(Pipe pipe, MatSpec s)
        {
            if (s.type == "glow") return Shader.Find("SkyIsland/LightGlow");
            switch (pipe)
            {
                case Pipe.URP: return Shader.Find("Universal Render Pipeline/Lit");
                case Pipe.HDRP: return Shader.Find("HDRP/Lit");
                default:
                    if (s.type == "cutout" && s.doubleSided) return Shader.Find("SkyIsland/Builtin/FoliageCutout");
                    return Shader.Find("Standard");
            }
        }

        static Texture2D Tex(Context ctx, string file) => string.IsNullOrEmpty(file) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(ctx.Tex(file));

        static void Kw(Material m, string kw, bool on)
        {
            if (on) m.EnableKeyword(kw); else m.DisableKeyword(kw);
        }

        static Color Emission(Context ctx, MatSpec s)
        {
            if (s.emission == null || s.emission.Length < 3 || s.emissionIntensity <= 0f) return Color.black;
            float k = s.emissionIntensity * (ctx.look != null && ctx.look.emissionScale > 0f ? ctx.look.emissionScale : 1f);
            var lin = new Color(s.emission[0] * k, s.emission[1] * k, s.emission[2] * k, 1f);
            return lin.gamma; // Material.SetColor converts gamma -> linear in Linear colour space
        }

        /// <summary>Additive fog halo: colour normalised (hue), brightness in _Intensity (linear HDR multiplier).</summary>
        static void SetupGlow(Context ctx, Material m, MatSpec s)
        {
            var c = J.Lin(s.color);
            float mx = Mathf.Max(c.maxColorComponent, 1e-4f);
            m.SetColor("_Color", new Color(c.r / mx, c.g / mx, c.b / mx).gamma);
            float k = ctx.look != null && ctx.look.emissionScale > 0f ? ctx.look.emissionScale : 1f;
            m.SetFloat("_Intensity", s.emissionIntensity * mx * k);
            m.SetFloat("_Softness", s.softness > 0f ? s.softness : 2.6f);
            m.SetFloat("_NearFade", 3f);
            m.renderQueue = (int)RenderQueue.Transparent + 10;
        }

        static void SetupURP(Context ctx, Material m, MatSpec s)
        {
            var albedo = Tex(ctx, s.albedo);
            var normal = Tex(ctx, s.normal);
            var mask = Tex(ctx, s.mask);
            var emap = Tex(ctx, s.emissionMap);
            var tiling = J.V2(s.tiling, Vector2.one);
            m.SetFloat("_WorkflowMode", 1f);
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_Blend", 0f);
            m.SetTexture("_BaseMap", albedo);
            m.SetTextureScale("_BaseMap", tiling);
            m.SetTexture("_MainTex", albedo);
            m.SetTextureScale("_MainTex", tiling);
            m.SetColor("_BaseColor", J.Gam(s.color));
            m.SetColor("_Color", J.Gam(s.color));
            if (mask != null)
            {
                m.SetTexture("_MetallicGlossMap", mask);
                m.SetTexture("_OcclusionMap", mask);
                m.SetFloat("_OcclusionStrength", 1f);
                m.SetFloat("_Smoothness", 1f);
                m.SetFloat("_Metallic", 0f);
            }
            else
            {
                m.SetTexture("_MetallicGlossMap", null);
                m.SetTexture("_OcclusionMap", null);
                m.SetFloat("_Smoothness", s.smoothness);
                m.SetFloat("_Metallic", s.metallic);
            }
            Kw(m, "_METALLICSPECGLOSSMAP", mask != null);
            Kw(m, "_OCCLUSIONMAP", mask != null);
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            Kw(m, "_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A", false);
            Kw(m, "_SPECULAR_SETUP", false);
            m.SetTexture("_BumpMap", normal);
            m.SetFloat("_BumpScale", s.normalScale > 0f ? s.normalScale : 1f);

            bool detail = s.detail != null && !string.IsNullOrEmpty(s.detail.albedo);
            var dA = detail ? Tex(ctx, s.detail.albedo) : null;
            var dN = detail ? Tex(ctx, s.detail.normal) : null;
            m.SetTexture("_DetailAlbedoMap", dA);
            m.SetTexture("_DetailNormalMap", dN);
            if (detail)
            {
                m.SetTextureScale("_DetailAlbedoMap", J.V2(s.detail.tiling, Vector2.one));
                m.SetFloat("_DetailAlbedoMapScale", 1f);
                m.SetFloat("_DetailNormalMapScale", s.detail.normalScale > 0f ? s.detail.normalScale : 1f);
            }
            Kw(m, "_DETAIL_MULX2", dA != null || dN != null);
            Kw(m, "_DETAIL_SCALED", false);
            Kw(m, "_NORMALMAP", normal != null || dN != null);

            var e = Emission(ctx, s);
            bool emissive = e.maxColorComponent > 0f;
            m.SetColor("_EmissionColor", e);
            m.SetTexture("_EmissionMap", emap);
            Kw(m, "_EMISSION", emissive);
            m.globalIlluminationFlags = emissive ? MaterialGlobalIlluminationFlags.RealtimeEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;

            bool cutout = s.type == "cutout";
            m.SetFloat("_AlphaClip", cutout ? 1f : 0f);
            m.SetFloat("_Cutoff", cutout ? s.cutoff : 0.5f);
            Kw(m, "_ALPHATEST_ON", cutout);
            Kw(m, "_SURFACE_TYPE_TRANSPARENT", false);
            Kw(m, "_ALPHAPREMULTIPLY_ON", false);
            m.SetOverrideTag("RenderType", cutout ? "TransparentCutout" : "Opaque");
            m.renderQueue = cutout ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;
            m.SetFloat("_SrcBlend", 1f);
            m.SetFloat("_DstBlend", 0f);
            m.SetFloat("_ZWrite", 1f);
            m.SetFloat("_Cull", s.doubleSided ? 0f : 2f);
            m.SetFloat("_ReceiveShadows", 1f);
            Kw(m, "_RECEIVE_SHADOWS_OFF", false);
            m.SetFloat("_EnvironmentReflections", 1f);
            m.SetFloat("_SpecularHighlights", 1f);
            m.SetShaderPassEnabled("ShadowCaster", s.type != "emissive");
        }

        static void SetupBuiltIn(Context ctx, Material m, MatSpec s)
        {
            var albedo = Tex(ctx, s.albedo);
            var normal = Tex(ctx, s.normal);
            var mask = Tex(ctx, s.mask);
            var emap = Tex(ctx, s.emissionMap);
            var tiling = J.V2(s.tiling, Vector2.one);
            m.SetTexture("_MainTex", albedo);
            m.SetTextureScale("_MainTex", tiling);
            m.SetColor("_Color", J.Gam(s.color));
            if (m.shader.name == "SkyIsland/Builtin/FoliageCutout")
            {
                m.SetFloat("_Cutoff", s.cutoff);
                m.SetFloat("_Glossiness", s.smoothness);
                return;
            }
            m.SetTexture("_MetallicGlossMap", mask);
            m.SetTexture("_OcclusionMap", mask);
            Kw(m, "_METALLICGLOSSMAP", mask != null);
            m.SetFloat("_GlossMapScale", 1f);
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            m.SetFloat("_Glossiness", s.smoothness);
            m.SetFloat("_Metallic", s.metallic);
            m.SetFloat("_OcclusionStrength", 1f);
            m.SetTexture("_BumpMap", normal);
            m.SetFloat("_BumpScale", s.normalScale > 0f ? s.normalScale : 1f);
            Kw(m, "_NORMALMAP", normal != null);
            var e = Emission(ctx, s);
            bool emissive = e.maxColorComponent > 0f;
            m.SetColor("_EmissionColor", e);
            m.SetTexture("_EmissionMap", emap);
            Kw(m, "_EMISSION", emissive);
            m.globalIlluminationFlags = emissive ? MaterialGlobalIlluminationFlags.RealtimeEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            bool cutout = s.type == "cutout";
            m.SetFloat("_Mode", cutout ? 1f : 0f);
            m.SetFloat("_Cutoff", s.cutoff);
            m.SetOverrideTag("RenderType", cutout ? "TransparentCutout" : "");
            m.SetInt("_SrcBlend", (int)BlendMode.One);
            m.SetInt("_DstBlend", (int)BlendMode.Zero);
            m.SetInt("_ZWrite", 1);
            Kw(m, "_ALPHATEST_ON", cutout);
            Kw(m, "_ALPHABLEND_ON", false);
            Kw(m, "_ALPHAPREMULTIPLY_ON", false);
            m.renderQueue = cutout ? (int)RenderQueue.AlphaTest : -1;
        }

        static void SetupHDRP(Context ctx, Material m, MatSpec s)
        {
            var albedo = Tex(ctx, s.albedo);
            var normal = Tex(ctx, s.normal);
            var mask = Tex(ctx, s.mask);
            var emap = Tex(ctx, s.emissionMap);
            var tiling = J.V2(s.tiling, Vector2.one);
            m.SetTexture("_BaseColorMap", albedo);
            m.SetTextureScale("_BaseColorMap", tiling);
            m.SetColor("_BaseColor", J.Gam(s.color));
            m.SetTexture("_NormalMap", normal);
            m.SetFloat("_NormalScale", s.normalScale > 0f ? s.normalScale : 1f);
            m.SetTexture("_MaskMap", mask);
            if (mask != null)
            {
                m.SetFloat("_Metallic", 1f);
                m.SetFloat("_SmoothnessRemapMin", 0f);
                m.SetFloat("_SmoothnessRemapMax", 1f);
                m.SetFloat("_AORemapMin", 0f);
                m.SetFloat("_AORemapMax", 1f);
            }
            else
            {
                m.SetFloat("_Metallic", s.metallic);
                m.SetFloat("_Smoothness", s.smoothness);
            }
            var e = Emission(ctx, s);
            // HDRP uses physical units (nits); scale the calibrated URP emission up so bloom still reads.
            var eLin = e.linear * 600f;
            m.SetColor("_EmissiveColor", eLin);
            m.SetTexture("_EmissiveColorMap", emap);
            bool cutout = s.type == "cutout";
            m.SetFloat("_AlphaCutoffEnable", cutout ? 1f : 0f);
            m.SetFloat("_AlphaCutoff", s.cutoff);
            m.SetFloat("_DoubleSidedEnable", s.doubleSided ? 1f : 0f);
#if SKYISLAND_HDRP
            HDMaterial.ValidateMaterial(m);
#endif
        }

        // ------------------------------------------------------------------ model remaps
        public static void RemapModels(Context ctx, Dictionary<string, Material> mats)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { ctx.root + "/Models" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is ModelImporter mi)) continue;
                bool changed = false;
                var existing = mi.GetExternalObjectMap();
                // collect material names used by the model
                var names = new HashSet<string>();
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (o is Material mat) names.Add(mat.name);
                foreach (var kv in existing)
                    if (kv.Key.type == typeof(Material)) names.Add(kv.Key.name);
                foreach (var n in names)
                {
                    if (!mats.TryGetValue(n, out var target)) continue;
                    var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), n);
                    if (existing.TryGetValue(id, out var cur) && cur == target) continue;
                    mi.AddRemap(id, target);
                    changed = true;
                }
                if (changed) mi.SaveAndReimport();
            }
        }
    }
}
