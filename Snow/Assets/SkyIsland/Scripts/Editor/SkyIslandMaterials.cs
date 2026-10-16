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
                if (s == null || string.IsNullOrEmpty(s.name)) continue;
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
                else if (s.type == "interior" && ctx.pipe != Pipe.HDRP) SetupInterior(ctx, mat, s);
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
                if (s == null || string.IsNullOrEmpty(s.name)) continue;
                var m = AssetDatabase.LoadAssetAtPath<Material>($"{ctx.MatFolder}/{s.name}.mat");
                if (m != null) result[s.name] = m;
            }
            if (result.Count == 0) result = BuildMaterials(ctx);
            return result;
        }

        static Shader PickShader(Pipe pipe, MatSpec s)
        {
            if (s.type == "glow") return Shader.Find("SkyIsland/LightGlow");
            if (s.type == "interior" && pipe != Pipe.HDRP) return Shader.Find("SkyIsland/InteriorWindow");
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
            if (m.HasProperty("_FogFloor"))                                                          // v2.4
                m.SetFloat("_FogFloor", ctx.look != null && ctx.look.fog != null ? Mathf.Clamp01(ctx.look.fog.glowFloor) : 0f);
            m.SetFloat("_NearFade", 3f);
            m.renderQueue = (int)RenderQueue.Transparent + 10;
        }

        /// <summary>Fake room behind the glass: room atlas (albedo), curtain layer, lit / unlit levels, sky-gradient reflection
        /// (defaults to the look's sky horizon / below colours so the glass matches the fog).</summary>
        static void SetupInterior(Context ctx, Material m, MatSpec s)
        {
            m.SetTexture("_Atlas", Tex(ctx, s.albedo));
            m.SetTexture("_Curtains", Tex(ctx, s.curtains));
            m.SetFloat("_Cols", s.cols > 0f ? s.cols : 4f);
            m.SetFloat("_Rows", s.rows > 0f ? s.rows : 4f);
            m.SetFloat("_Depth", s.depth > 0f ? s.depth : 1f);
            m.SetFloat("_CamDist", s.camDist > 0f ? s.camDist : 1f);
            float k = ctx.look != null && ctx.look.emissionScale > 0f ? ctx.look.emissionScale : 1f;
            m.SetFloat("_LitIntensity", (s.litIntensity > 0f ? s.litIntensity : 1.6f) * k);
            m.SetFloat("_DarkIntensity", s.darkIntensity > 0f ? s.darkIntensity : 0.05f);
            m.SetFloat("_Fresnel", s.fresnel > 0f ? s.fresnel : 0.04f);
            m.SetColor("_LitTint", (s.litTint != null && s.litTint.Length >= 3 ? J.Lin(s.litTint) : new Color(1f, 0.9f, 0.78f)).gamma);
            m.SetColor("_DarkTint", (s.darkTint != null && s.darkTint.Length >= 3 ? J.Lin(s.darkTint) : new Color(0.55f, 0.62f, 0.85f)).gamma);
            Color top = new Color(0.05f, 0.06f, 0.12f), bottom = new Color(0.015f, 0.015f, 0.02f);
            if (s.reflTop != null && s.reflTop.Length >= 3) top = J.Lin(s.reflTop);
            else if (ctx.look != null && ctx.look.sky != null && ctx.look.sky.horizon != null && ctx.look.sky.horizon.Length >= 3) top = J.Lin(ctx.look.sky.horizon);
            if (s.reflBottom != null && s.reflBottom.Length >= 3) bottom = J.Lin(s.reflBottom);
            else if (ctx.look != null && ctx.look.sky != null && ctx.look.sky.below != null && ctx.look.sky.below.Length >= 3) bottom = J.Lin(ctx.look.sky.below) * 0.5f;
            m.SetColor("_ReflTop", top.gamma);
            m.SetColor("_ReflBottom", bottom.gamma);
            m.SetFloat("_ReflStrength", 1f);
            m.renderQueue = (int)RenderQueue.Geometry;
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
            m.SetFloat("_SrcBlendAlpha", 1f);   // URP 14+ separate alpha factors (opaque values; restores a former transparent)
            m.SetFloat("_DstBlendAlpha", 0f);
            m.SetFloat("_ZWrite", 1f);
            m.SetFloat("_Cull", s.doubleSided ? 0f : 2f);
            m.SetFloat("_ReceiveShadows", 1f);
            Kw(m, "_RECEIVE_SHADOWS_OFF", false);
            bool noSpec = s.noSpecular;                                                              // v2.4 PSX: no specular
            m.SetFloat("_EnvironmentReflections", noSpec ? 0f : 1f);
            m.SetFloat("_SpecularHighlights", noSpec ? 0f : 1f);
            Kw(m, "_ENVIRONMENTREFLECTIONS_OFF", noSpec);
            Kw(m, "_SPECULARHIGHLIGHTS_OFF", noSpec);
            m.SetShaderPassEnabled("DepthOnly", true);
            bool transparent = IsTransparent(s);
            if (transparent) MakeURPTransparent(m, s);
            m.SetShaderPassEnabled("ShadowCaster", s.type != "emissive" && !transparent);
        }

        static bool IsTransparent(MatSpec s) => s.type == "transparent";

        /// <summary>Base alpha of a transparent material (preview defaults: 1 with alphaFromTexture, else 0.6).</summary>
        static float TransparentAlpha(MatSpec s) => s.alpha > 0f ? Mathf.Clamp01(s.alpha) : (s.alphaFromTexture ? 1f : 0.6f);

        static Color TransparentColor(MatSpec s)
        {
            var c = J.Gam(s.color);
            c.a = TransparentAlpha(s);
            return c;
        }

        /// <summary>URP Lit, Surface Transparent / Blend Alpha (same state as the URP material inspector, without
        /// "preserve specular"): SrcAlpha / OneMinusSrcAlpha, ZWrite off, queue Transparent, no depth / shadow passes.</summary>
        static void MakeURPTransparent(Material m, MatSpec s)
        {
            var c = TransparentColor(s);
            m.SetColor("_BaseColor", c);
            m.SetColor("_Color", c);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_BlendModePreserveSpecular", 0f);
            m.SetFloat("_AlphaClip", 0f);
            m.SetFloat("_AlphaToMask", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            Kw(m, "_SURFACE_TYPE_TRANSPARENT", true);
            Kw(m, "_ALPHATEST_ON", false);
            Kw(m, "_ALPHAPREMULTIPLY_ON", false);
            Kw(m, "_ALPHAMODULATE_ON", false);
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetShaderPassEnabled("DepthOnly", false);
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
            bool transparent = IsTransparent(s);
            if (transparent)
            {
                // Standard shader "Fade" mode (StandardShaderGUI): alpha blend, no depth write, queue Transparent
                m.SetColor("_Color", TransparentColor(s));
                m.SetFloat("_Mode", 2f);
                m.SetFloat("_Cutoff", s.cutoff);
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                Kw(m, "_ALPHATEST_ON", false);
                Kw(m, "_ALPHABLEND_ON", true);
                Kw(m, "_ALPHAPREMULTIPLY_ON", false);
                m.renderQueue = (int)RenderQueue.Transparent;
                m.SetShaderPassEnabled("ShadowCaster", false);
                return;
            }
            m.SetFloat("_Mode", cutout ? 1f : 0f);
            m.SetFloat("_Cutoff", s.cutoff);
            m.SetOverrideTag("RenderType", cutout ? "TransparentCutout" : "");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            m.SetInt("_ZWrite", 1);
            Kw(m, "_ALPHATEST_ON", cutout);
            Kw(m, "_ALPHABLEND_ON", false);
            Kw(m, "_ALPHAPREMULTIPLY_ON", false);
            m.renderQueue = cutout ? (int)RenderQueue.AlphaTest : -1;
            m.SetShaderPassEnabled("ShadowCaster", true);   // restores a former transparent material
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
            bool transparent = IsTransparent(s);
            m.SetFloat("_AlphaCutoffEnable", cutout ? 1f : 0f);
            m.SetFloat("_AlphaCutoff", s.cutoff);
            m.SetFloat("_DoubleSidedEnable", s.doubleSided ? 1f : 0f);
            m.SetFloat("_SurfaceType", transparent ? 1f : 0f);
            if (transparent)
            {
                m.SetColor("_BaseColor", TransparentColor(s));
                m.SetFloat("_BlendMode", 0f);            // Alpha
                m.SetFloat("_TransparentZWrite", 0f);
                m.SetFloat("_ZWrite", 0f);
            }
#if SKYISLAND_HDRP
            HDMaterial.ValidateMaterial(m);
#endif
            m.SetShaderPassEnabled("ShadowCaster", !transparent);
        }

        // ------------------------------------------------------------------ model remaps
        public static void RemapModels(Context ctx, Dictionary<string, Material> mats)
        {
            if (!AssetDatabase.IsValidFolder(ctx.root + "/Models")) return;
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
