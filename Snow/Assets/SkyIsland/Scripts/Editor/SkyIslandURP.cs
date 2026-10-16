#if SKYISLAND_URP
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SkyIsland.EditorTools
{
    public static partial class SkyIslandBuilder
    {
        /// <summary>
        /// URP global Volume: Neutral tonemapping + reference LUT (exact palette of the concept art),
        /// bloom for the lights, vignette, film grain, a touch of chromatic aberration.
        /// Order inside URP: bloom -> vignette -> post exposure -> contrast (LogC) -> tonemap -> user LUT (sRGB) -> grain.
        /// </summary>
#if UNITY_2022_2_OR_NEWER
        /// <summary>m_RenderingMode of the first renderer of the URP asset (0 Forward, 1 Deferred, 2 Forward+), -1 if unknown.</summary>
        static int FirstRendererMode(UniversalRenderPipelineAsset urp)
        {
            var list = new SerializedObject(urp).FindProperty("m_RendererDataList");
            if (list == null || list.arraySize == 0) return -1;
            var rd = list.GetArrayElementAtIndex(0).objectReferenceValue;
            if (rd == null) return -1;
            var mode = new SerializedObject(rd).FindProperty("m_RenderingMode");
            return mode != null ? mode.intValue : -1;
        }

        [MenuItem(Menu + "Switch URP Renderer To Forward+", priority = 41)]
        public static void SwitchToForwardPlus()
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp == null) { Debug.LogWarning("[SkyIsland] URP is not the active render pipeline."); return; }
            var list = new SerializedObject(urp).FindProperty("m_RendererDataList");
            int changed = 0;
            for (int i = 0; list != null && i < list.arraySize; i++)
            {
                var rd = list.GetArrayElementAtIndex(i).objectReferenceValue;
                if (rd == null) continue;
                var so = new SerializedObject(rd);
                var mode = so.FindProperty("m_RenderingMode");
                if (mode == null || mode.intValue == 2) continue;
                Undo.RecordObject(rd, "Sky Island: Forward+");
                mode.intValue = 2;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(rd);
                changed++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log(changed > 0 ? $"[SkyIsland] {changed} URP renderer(s) switched to Forward+." : "[SkyIsland] URP renderers already use Forward+.");
        }
#endif

        static void SetupURPPost(Context ctx, Transform root, Camera cam)
        {
            int lutSize = 32;
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null)
            {
                if (!urp.supportsHDR)
                {
                    Undo.RecordObject(urp, "Sky Island: enable HDR");
                    urp.supportsHDR = true;
                    EditorUtility.SetDirty(urp);
                    Debug.Log("[SkyIsland] HDR enabled on the active URP asset (needed for bloom on the lights).");
                }
                lutSize = urp.colorGradingLutSize;
                if (lutSize != 32 && lutSize != 64)
                {
                    Undo.RecordObject(urp, "Sky Island: LUT size");
                    urp.colorGradingLutSize = 32;
                    lutSize = 32;
                    EditorUtility.SetDirty(urp);
                    Debug.Log("[SkyIsland] URP grading LUT size set to 32 to match the reference LUT.");
                }
                if (urp.colorGradingMode != ColorGradingMode.HighDynamicRange)
                {
                    // the reference look is calibrated as: post exposure -> contrast -> Neutral tonemap -> LUT (HDR grading order)
                    Undo.RecordObject(urp, "Sky Island: HDR grading");
                    urp.colorGradingMode = ColorGradingMode.HighDynamicRange;
                    EditorUtility.SetDirty(urp);
                    Debug.Log("[SkyIsland] URP Post-processing > Grading Mode set to High Dynamic Range (required for the calibrated look).");
                }
                {
                    var list = new SerializedObject(urp).FindProperty("m_RendererDataList");
                    var rd = list != null && list.arraySize > 0 ? list.GetArrayElementAtIndex(0).objectReferenceValue : null;
                    var ppd = rd != null ? new SerializedObject(rd).FindProperty("postProcessData") : null;
                    if (ppd != null && ppd.objectReferenceValue == null)
                        Debug.LogWarning("[SkyIsland] Post-processing is disabled on the URP renderer (Renderer > Post-processing). Enable it, otherwise bloom, tonemapping and the LUT are not applied.");
                }
                if (urp.maxAdditionalLightsCount < 8)
                {
                    // Forward path only (Forward+ has no per-object limit): hangar, lamp and beacon lights overlap on the apron
                    Undo.RecordObject(urp, "Sky Island: per-object lights");
                    urp.maxAdditionalLightsCount = 8;
                    EditorUtility.SetDirty(urp);
                    Debug.Log("[SkyIsland] URP Additional Lights > Per Object Limit raised to 8.");
                }
                if (urp.additionalLightsRenderingMode == LightRenderingMode.Disabled)
                    Debug.LogWarning("[SkyIsland] Additional Lights are disabled in the active URP asset: lamps, hangar lights and beacons will not light the scene. Set Lighting > Additional Lights to Per Pixel.");
#if UNITY_2022_2_OR_NEWER
                if (FirstRendererMode(urp) == 0)
                    Debug.LogWarning("[SkyIsland] The URP renderer uses Forward: with ~80 lamps some lights will pop on large meshes. Recommended: Tools > Sky Island > Switch URP Renderer To Forward+.");
#endif
            }
            var lut = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ctx.root}/Textures/LUT/SkyIsland_RefLook_LUT{lutSize}.png");

            EnsureFolder(ctx.root + "/Settings");
            string path = ctx.root + "/Settings/SkyIsland_URP_Volume.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "SkyIsland_URP_Volume";
            AssetDatabase.CreateAsset(profile, path);

            T Add<T>() where T : VolumeComponent
            {
                var c = profile.Add<T>(false);
                c.name = typeof(T).Name;
                c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(c, profile);
                return c;
            }

            var p = ctx.look.post;
            var tm = Add<Tonemapping>();
            tm.mode.Override(TonemappingMode.Neutral);

            var bloom = Add<Bloom>();
            bloom.threshold.Override(p.bloom.threshold);
            bloom.intensity.Override(p.bloom.intensity);
            bloom.scatter.Override(p.bloom.scatter);
            bloom.clamp.Override(p.bloom.clamp > 0f ? p.bloom.clamp : 65472f);
            bloom.tint.Override(J.Gam(p.bloom.tint));
            bloom.highQualityFiltering.Override(p.bloom.highQuality);

            var ca = Add<ColorAdjustments>();
            ca.postExposure.Override(p.exposure);
            ca.contrast.Override(p.contrast);

            var cl = Add<ColorLookup>();
            cl.texture.Override(lut);
            cl.contribution.Override(p.lutContribution);

            var vg = Add<Vignette>();
            vg.color.Override(J.Lin(p.vignette.color));
            vg.center.Override(new Vector2(0.5f, 0.5f));
            vg.intensity.Override(p.vignette.intensity);
            vg.smoothness.Override(p.vignette.smoothness);
            vg.rounded.Override(p.vignette.rounded);

            var fg = Add<FilmGrain>();
            fg.type.Override(FilmGrainLookup.Medium1);
            fg.intensity.Override(p.grain.intensity);
            fg.response.Override(p.grain.response);

            if (p.chromaticAberration > 0f)
            {
                var chr = Add<ChromaticAberration>();
                chr.intensity.Override(p.chromaticAberration);
            }

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var old = GameObject.Find("PostProcess Volume");
            if (old != null) Object.DestroyImmediate(old);
            var volGo = new GameObject("PostProcess Volume");
            if (root != null) volGo.transform.SetParent(root, false);
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 10f;
            vol.weight = 1f;
            vol.sharedProfile = profile;

            if (cam == null) cam = Camera.main;
            if (cam != null)
            {
                var data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.antialiasingQuality = AntialiasingQuality.High;
                data.dithering = true;
            }
        }
    }
}
#endif
