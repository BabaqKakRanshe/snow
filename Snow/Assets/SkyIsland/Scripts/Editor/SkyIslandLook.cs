using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SkyIsland.EditorTools
{
    public static partial class SkyIslandBuilder
    {
        /// <summary>Fog, sky, ambient, sun, haze layers and post-processing for the active pipeline.</summary>
        public static void ApplyLook(Context ctx, Dictionary<string, Material> mats, Transform root, Camera cam)
        {
            var look = ctx.look;
            EnsureFolder(ctx.root + "/Lighting");
            EnsureFolder(ctx.root + "/Materials");

            // --- no baked GI: everything is realtime
            string lsPath = ctx.root + "/Lighting/SkyIsland_LightingSettings.lighting";
            var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(lsPath);
            if (ls == null)
            {
                ls = new LightingSettings { name = "SkyIsland_LightingSettings" };
                AssetDatabase.CreateAsset(ls, lsPath);
            }
            ls.bakedGI = false;
            ls.realtimeGI = false;
            ls.autoGenerate = false;
            Lightmapping.lightingSettings = ls;

            // --- skybox
            var skyShader = Shader.Find("SkyIsland/Skybox");
            string skyPath = ctx.root + "/Materials/SkyIsland_Skybox.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if (sky == null)
            {
                sky = new Material(skyShader) { name = "SkyIsland_Skybox" };
                AssetDatabase.CreateAsset(sky, skyPath);
            }
            if (skyShader != null) sky.shader = skyShader;
            var s = look.sky;
            sky.SetColor("_HorizonColor", J.Gam(s.horizon));
            sky.SetColor("_BandColor", J.Gam(s.band));
            sky.SetColor("_ZenithColor", J.Gam(s.zenith));
            sky.SetColor("_BelowColor", J.Gam(s.below));
            sky.SetColor("_SunColor", J.Gam(s.sunColor));
            sky.SetFloat("_BandHeight", s.bandHeight);
            sky.SetFloat("_BandWidth", s.bandWidth);
            sky.SetFloat("_CloudScale", s.cloudScale);
            sky.SetFloat("_CloudContrast", s.cloudContrast);
            sky.SetFloat("_CloudSpeed", s.cloudSpeed);
            sky.SetFloat("_SunGlow", s.sunGlow);
            sky.SetFloat("_SunGlowPower", s.sunGlowPower);
            sky.SetFloat("_SunDisc", s.sunDisc);
            sky.SetFloat("_SunDiscPower", s.sunDiscPower);
            sky.SetFloat("_Exposure", 1f);
            sky.SetTexture("_CloudTex", AssetDatabase.LoadAssetAtPath<Texture2D>(ctx.Tex("T_SkyClouds.png")));
            Vector3 travel = J.V3(look.sun.dir_unity).normalized;
            if (travel.sqrMagnitude < 1e-6f) travel = new Vector3(0.3f, -0.35f, -0.9f).normalized;
            sky.SetVector("_SunDir", -travel);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;

            // --- distance fog (visibility ~300-600 m like the references)
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = look.fog.density;
            RenderSettings.fogColor = J.Gam(look.fog.color);
            if (cam != null)
            {
                var fogCtl = cam.GetComponent<SkyIsland.SkyIslandFogByAltitude>();
                if (fogCtl != null) { fogCtl.groundDensity = look.fog.density; fogCtl.airDensity = look.fog.density / 3f; }
            }

            // --- ambient (trilight) and reflections
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = J.Gam(look.ambient.sky);
            RenderSettings.ambientEquatorColor = J.Gam(look.ambient.equator);
            RenderSettings.ambientGroundColor = J.Gam(look.ambient.ground);
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.reflectionIntensity = 1f;
            var cube = RenderSkyCubemap(ctx, sky);
            if (cube != null)
            {
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
#if UNITY_2022_1_OR_NEWER
                RenderSettings.customReflectionTexture = cube;
#else
                RenderSettings.customReflection = cube;
#endif
            }
            else
            {
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            }

            // --- sun
            var sunGo = GameObject.Find("Sun");
            if (sunGo == null)
            {
                sunGo = new GameObject("Sun");
                if (root != null) sunGo.transform.SetParent(root, false);
            }
            var sun = sunGo.GetComponent<Light>();
            if (sun == null) sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sunGo.transform.rotation = Quaternion.LookRotation(travel, Vector3.up);
            var sc = J.Lin(look.sun.color);
            float mx = Mathf.Max(sc.maxColorComponent, 1e-4f);
            sun.color = new Color(sc.r / mx, sc.g / mx, sc.b / mx).gamma;
            sun.intensity = look.sun.intensity * mx;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.8f;
            sun.lightmapBakeType = LightmapBakeType.Realtime;
            RenderSettings.sun = sun;
            var skyCtl = sunGo.GetComponent<SkyIsland.SkyIslandSky>();
            if (skyCtl == null) skyCtl = sunGo.AddComponent<SkyIsland.SkyIslandSky>();
            skyCtl.sun = sun;
            skyCtl.skybox = sky;

            // --- haze layers under the island
            CreateFogSheets(ctx, root);

            DynamicGI.UpdateEnvironment();

            // --- post-processing
            switch (ctx.pipe)
            {
                case Pipe.URP:
#if SKYISLAND_URP
                    SetupURPPost(ctx, root, cam);
#else
                    Debug.LogWarning("[SkyIsland] URP is active but the URP package define is missing; post-processing skipped.");
#endif
                    break;
                case Pipe.BuiltIn:
                    SetupBuiltInPost(ctx, cam);
                    break;
                default:
                    Debug.LogWarning("[SkyIsland] HDRP: fog/sky/post are not converted automatically (the look is calibrated for URP).");
                    break;
            }
        }

        static Cubemap RenderSkyCubemap(Context ctx, Material sky)
        {
            string path = ctx.root + "/Lighting/SkyIsland_SkyReflection.cubemap";
            GameObject tmp = null;
            try
            {
                var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
                if (cube == null)
                {
                    cube = new Cubemap(128, TextureFormat.RGBAHalf, true) { name = "SkyIsland_SkyReflection" };
                    AssetDatabase.CreateAsset(cube, path);
                }
                tmp = new GameObject("SkyIsland_CubemapCamera") { hideFlags = HideFlags.HideAndDontSave };
                var c = tmp.AddComponent<Camera>();
                c.cullingMask = 0;
                c.clearFlags = CameraClearFlags.Skybox;
                c.allowHDR = true;
                c.enabled = false;
                var prevSky = RenderSettings.skybox;
                RenderSettings.skybox = sky;
                bool ok = c.RenderToCubemap(cube);
                RenderSettings.skybox = prevSky;
                if (!ok) return null;
                // some pipelines accept the call but leave the cubemap black: verify before using it
                float sum = 0f;
                foreach (CubemapFace face in new[] { CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveZ, CubemapFace.NegativeZ })
                {
                    var px = cube.GetPixel(face, cube.width / 2, cube.width / 2);
                    sum += px.r + px.g + px.b;
                }
                if (sum < 1e-3f) return null;
                // URP (17) renders RenderToCubemap faces like a back-buffer target: in Linear colour space the values
                // come back sRGB-encoded. Detect it on the zenith (known sky colour) and decode to linear.
                if (QualitySettings.activeColorSpace == ColorSpace.Linear && sky.HasProperty("_ZenithColor"))
                {
                    Color zLin = sky.GetColor("_ZenithColor").linear, zEnc = zLin.gamma;
                    var up = cube.GetPixel(CubemapFace.PositiveY, cube.width / 2, cube.width / 2);
                    float dLin = Mathf.Abs(up.r - zLin.r) + Mathf.Abs(up.g - zLin.g) + Mathf.Abs(up.b - zLin.b);
                    float dEnc = Mathf.Abs(up.r - zEnc.r) + Mathf.Abs(up.g - zEnc.g) + Mathf.Abs(up.b - zEnc.b);
                    if (dEnc < dLin)
                    {
                        foreach (CubemapFace face in new[] { CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY, CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ })
                        {
                            var pixels = cube.GetPixels(face);
                            for (int i = 0; i < pixels.Length; i++) pixels[i] = pixels[i].linear;
                            cube.SetPixels(pixels, face);
                        }
                        cube.Apply(true);
                        Debug.Log("[SkyIsland] sky reflection cubemap came back sRGB-encoded from RenderToCubemap; decoded to linear.");
                    }
                }
                EditorUtility.SetDirty(cube);
                return cube;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SkyIsland] sky reflection cubemap not rendered: " + ex.Message);
                return null;
            }
            finally
            {
                if (tmp != null) UnityEngine.Object.DestroyImmediate(tmp);
            }
        }

        static void CreateFogSheets(Context ctx, Transform root)
        {
            var shader = Shader.Find("SkyIsland/FogSheet");
            if (shader == null || ctx.pipe == Pipe.HDRP) return;
            string path = ctx.root + "/Materials/SkyIsland_FogSheet.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = "SkyIsland_FogSheet" };
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetColor("_Color", J.Gam(ctx.look.fog.color));
            mat.SetTexture("_NoiseTex", AssetDatabase.LoadAssetAtPath<Texture2D>(ctx.Tex("T_FogNoise.png")));
            EditorUtility.SetDirty(mat);

            var old = GameObject.Find("HazeLayers");
            if (old != null) UnityEngine.Object.DestroyImmediate(old);
            var g = new GameObject("HazeLayers").transform;
            if (root != null) g.SetParent(root, false);
            float[] heights = { -150f, -320f, -520f };
            float[] density = { 0.52f, 0.62f, 0.75f };
            for (int i = 0; i < heights.Length; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "Haze_" + i;
                var col = q.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.DestroyImmediate(col);
                q.transform.SetParent(g, false);
                q.transform.localPosition = new Vector3(0f, heights[i], -40f);
                q.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                q.transform.localScale = new Vector3(9000f, 9000f, 1f);
                var r = q.GetComponent<MeshRenderer>();
                var inst = new Material(mat) { name = "SkyIsland_FogSheet_" + i };
                inst.SetFloat("_Density", density[i]);
                inst.SetVector("_Speed", new Vector4(1.2f + i * 0.4f, 0.5f - i * 0.3f, 0f, 0f));
                string ip = ctx.root + $"/Materials/SkyIsland_FogSheet_{i}.mat";
                AssetDatabase.DeleteAsset(ip);
                AssetDatabase.CreateAsset(inst, ip);
                r.sharedMaterial = inst;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }

        static void SetupBuiltInPost(Context ctx, Camera cam)
        {
            if (cam == null) cam = Camera.main;
            if (cam == null) return;
            var post = cam.GetComponent<SkyIsland.SkyIslandBuiltinPost>();
            if (post == null) post = cam.gameObject.AddComponent<SkyIsland.SkyIslandBuiltinPost>();
            var p = ctx.look.post;
            post.shader = Shader.Find("Hidden/SkyIsland/BuiltinPost");
            post.lut = AssetDatabase.LoadAssetAtPath<Texture2D>(ctx.root + "/Textures/LUT/SkyIsland_RefLook_LUT32.png");
            post.lutContribution = p.lutContribution;
            post.postExposure = p.exposure;
            post.contrast = p.contrast;
            post.bloomThreshold = p.bloom.threshold;
            post.bloomIntensity = p.bloom.intensity;
            post.bloomScatter = p.bloom.scatter;
            post.bloomTint = J.Gam(p.bloom.tint);
            post.vignetteIntensity = p.vignette.intensity;
            post.vignetteSmoothness = p.vignette.smoothness;
            post.vignetteColor = J.Lin(p.vignette.color);
            post.grainIntensity = p.grain.intensity;
            post.grainResponse = p.grain.response;
            cam.allowHDR = true;
        }
    }
}
