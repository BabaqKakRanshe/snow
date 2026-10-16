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
            string lsPath = ctx.root + "/Lighting/" + ctx.AssetName("LightingSettings") + ".lighting";
            var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(lsPath);
            if (ls == null)
            {
                ls = new LightingSettings { name = ctx.AssetName("LightingSettings") };
                AssetDatabase.CreateAsset(ls, lsPath);
            }
            ls.bakedGI = false;
            ls.realtimeGI = false;
            ls.autoGenerate = false;
            Lightmapping.lightingSettings = ls;

            // --- skybox
            var skyShader = Shader.Find("SkyIsland/Skybox");
            string skyPath = ctx.root + "/Materials/" + ctx.AssetName("Skybox") + ".mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if (sky == null)
            {
                sky = new Material(skyShader) { name = ctx.AssetName("Skybox") };
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
            sky.SetFloat("_StarIntensity", Mathf.Max(0f, s.starIntensity));                       // v2.4 stars
            sky.SetFloat("_StarDensity", s.starDensity > 0f ? s.starDensity : 0.06f);
            sky.SetFloat("_StarScale", s.starScale > 0f ? s.starScale : 220f);
            sky.SetFloat("_StarSize", s.starSize > 0f ? s.starSize : 0.32f);
            sky.SetColor("_StarColor", J.Has(s.starColor, 3) ? J.Gam(s.starColor) : new Color(0.8f, 0.85f, 1f));
            sky.SetFloat("_Exposure", 1f);
            sky.SetTexture("_CloudTex", AssetDatabase.LoadAssetAtPath<Texture2D>(ctx.Tex("T_SkyClouds.png")));
            Vector3 travel = J.V3(look.sun.dir_unity).normalized;
            if (travel.sqrMagnitude < 1e-6f) travel = new Vector3(0.3f, -0.35f, -0.9f).normalized;
            sky.SetVector("_SunDir", -travel);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;

            // --- distance fog (visibility ~300-600 m like the references; density 0 = no fog)
            if (look.fog.IsLinear)                                                                   // v2.4 PSX: linear fog
            {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogStartDistance = look.fog.start;
                RenderSettings.fogEndDistance = look.fog.end;
            }
            else
            {
                RenderSettings.fog = look.fog.density > 0f;
                RenderSettings.fogMode = FogMode.Exponential;
                RenderSettings.fogDensity = look.fog.density;
            }
            RenderSettings.fogColor = J.Gam(look.fog.color);
            if (cam != null)
            {
                var fogCtl = cam.GetComponent<SkyIsland.SkyIslandFogByAltitude>();
                if (fogCtl != null) { fogCtl.groundDensity = look.fog.density; fogCtl.airDensity = AirDensity(ctx); ConfigureLinearFog(ctx, fogCtl); }
                // v2.4 PSX output on the location camera (look.psx); removed again when the look has none
                var psx = cam.GetComponent<SkyIsland.SkyIslandPSX>();
                if (look.psx != null && look.psx.IsSet)
                {
                    if (psx == null) psx = cam.gameObject.AddComponent<SkyIsland.SkyIslandPSX>();
                    psx.height = look.psx.height;
                    psx.colorBits = look.psx.colorBits > 0 ? look.psx.colorBits : 5;
                    psx.dither = Mathf.Clamp01(look.psx.dither);
                    psx.presentShader = Shader.Find("SkyIsland/PSXPresent");
                    if (psx.presentShader == null) Debug.LogWarning("[SkyIsland] Shader SkyIsland/PSXPresent not found (Assets/SkyIsland/Shaders).");
                    EditorUtility.SetDirty(psx);
                }
                else if (psx != null) UnityEngine.Object.DestroyImmediate(psx);
            }

            // --- ambient (trilight; v2 ambient.intensity scales the colours, as in the preview) and reflections
            float ambK = look.ambient.intensity > 0f ? look.ambient.intensity : 1f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = J.Gam(look.ambient.sky, ambK);
            RenderSettings.ambientEquatorColor = J.Gam(look.ambient.equator, ambK);
            RenderSettings.ambientGroundColor = J.Gam(look.ambient.ground, ambK);
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
            sun.shadowStrength = look.sun.shadowStrength > 0f ? Mathf.Clamp01(look.sun.shadowStrength) : 0.8f;
            sun.lightmapBakeType = LightmapBakeType.Realtime;
            SetLightRenderingLayers(sun, uint.MaxValue);   // v2.6: the sun / moon lights every rendering layer (rooms included)
            RenderSettings.sun = sun;
            var skyCtl = sunGo.GetComponent<SkyIsland.SkyIslandSky>();
            if (skyCtl == null) skyCtl = sunGo.AddComponent<SkyIsland.SkyIslandSky>();
            skyCtl.sun = sun;
            skyCtl.skybox = sky;

            // --- haze layers (look.haze; Sky Island: the three sheets under the island)
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
            string path = ctx.root + "/Lighting/" + ctx.AssetName("SkyReflection") + ".cubemap";
            GameObject tmp = null;
            try
            {
                var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
                if (cube == null)
                {
                    cube = new Cubemap(128, TextureFormat.RGBAHalf, true) { name = ctx.AssetName("SkyReflection") };
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
                // v2.7.1 (fix made in the game project on 01.10.2026): URP 17 renders RenderToCubemap faces like a back-buffer
                // target, so in Linear colour space the values come back sRGB-encoded. Detect it on the zenith (known sky
                // colour) and decode the faces to linear.
                if (QualitySettings.activeColorSpace == ColorSpace.Linear && sky.HasProperty("_ZenithColor"))
                {
                    Color zLin = sky.GetColor("_ZenithColor").linear, zEnc = zLin.gamma;
                    var up = cube.GetPixel(CubemapFace.PositiveY, cube.width / 2, cube.width / 2);
                    float dLin = Mathf.Abs(up.r - zLin.r) + Mathf.Abs(up.g - zLin.g) + Mathf.Abs(up.b - zLin.b);
                    float dEnc = Mathf.Abs(up.r - zEnc.r) + Mathf.Abs(up.g - zEnc.g) + Mathf.Abs(up.b - zEnc.b);
                    if (dEnc < dLin)
                    {
                        foreach (CubemapFace face in new[] { CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY,
                                                             CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ })
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

        /// <summary>One transparent haze sheet: height, coverage, drift (m/s) and centre z.</summary>
        struct HazeSheet { public float y, density, z; public Vector2 speed; }

        /// <summary>look.haze if present; Sky Island (v1): its three sheets under the island; otherwise none.</summary>
        static List<HazeSheet> HazeSheets(Context ctx)
        {
            var list = new List<HazeSheet>();
            var haze = ctx.look.haze;
            if (haze != null && haze.Length > 0)
            {
                foreach (var h in haze)
                {
                    if (h == null) continue;
                    list.Add(new HazeSheet
                    {
                        y = h.y,
                        density = h.density > 0f ? Mathf.Clamp01(h.density) : 0.6f,
                        speed = J.V2(h.speed, new Vector2(1.2f, 0.5f)),
                        z = 0f,
                    });
                }
            }
            else if (ctx.defaults.islandHaze)
            {
                float[] heights = { -150f, -320f, -520f };
                float[] density = { 0.52f, 0.62f, 0.75f };
                for (int i = 0; i < heights.Length; i++)
                    list.Add(new HazeSheet { y = heights[i], density = density[i], speed = new Vector2(1.2f + i * 0.4f, 0.5f - i * 0.3f), z = -40f });
            }
            return list;
        }

        static void CreateFogSheets(Context ctx, Transform root)
        {
            var shader = Shader.Find("SkyIsland/FogSheet");
            if (shader == null || ctx.pipe == Pipe.HDRP) return;
            var sheets = HazeSheets(ctx);
            var old = GameObject.Find("HazeLayers");
            if (old != null) UnityEngine.Object.DestroyImmediate(old);
            if (sheets.Count == 0) return;

            string path = ctx.root + "/Materials/" + ctx.AssetName("FogSheet") + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = ctx.AssetName("FogSheet") };
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetColor("_Color", J.Gam(ctx.look.fog.color));
            mat.SetTexture("_NoiseTex", AssetDatabase.LoadAssetAtPath<Texture2D>(ctx.Tex("T_FogNoise.png")));
            EditorUtility.SetDirty(mat);

            var g = new GameObject("HazeLayers").transform;
            if (root != null) g.SetParent(root, false);
            for (int i = 0; i < sheets.Count; i++)
            {
                var sh = sheets[i];
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "Haze_" + i;
                var col = q.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.DestroyImmediate(col);
                q.transform.SetParent(g, false);
                q.transform.localPosition = new Vector3(0f, sh.y, sh.z);
                q.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                q.transform.localScale = new Vector3(9000f, 9000f, 1f);
                var r = q.GetComponent<MeshRenderer>();
                var inst = new Material(mat) { name = ctx.AssetName("FogSheet_" + i) };
                inst.SetFloat("_Density", sh.density);
                inst.SetVector("_Speed", new Vector4(sh.speed.x, sh.speed.y, 0f, 0f));
                string ip = ctx.root + "/Materials/" + ctx.AssetName("FogSheet_" + i) + ".mat";
                AssetDatabase.DeleteAsset(ip);
                AssetDatabase.CreateAsset(inst, ip);
                r.sharedMaterial = inst;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }

        /// <summary>
        /// Grading LUT (look.post.lut, in &lt;root&gt;/Textures/LUT; "shared:" = SkyIsland's LUT folder). With size 64 the
        /// "64" variant of the file name is used when it exists. Null when there is no LUT or lutContribution &lt;= 0.
        /// </summary>
        static Texture2D LoadLut(Context ctx, int lutSize)
        {
            var p = ctx.look.post;
            if (string.IsNullOrEmpty(p.lut)) return null;
            if (p.lutContribution <= 0f)
            {
                Debug.Log($"[SkyIsland] {ctx.id}: lutContribution is 0, LUT {p.lut} not applied.");
                return null;
            }
            string file = p.lut, folder = ctx.root + "/Textures/LUT/";
            if (file.StartsWith(SharedPrefix, System.StringComparison.Ordinal))
            {
                file = file.Substring(SharedPrefix.Length);
                folder = (ctx.coreRoot ?? ctx.root) + "/Textures/LUT/";
            }
            if (lutSize == 64)
            {
                int k = file.LastIndexOf("32", System.StringComparison.Ordinal);
                if (k >= 0)
                {
                    var big = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + file.Substring(0, k) + "64" + file.Substring(k + 2));
                    if (big != null) return big;
                }
            }
            var lut = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + file);
            if (lut == null) Debug.LogWarning($"[SkyIsland] {ctx.id}: LUT {folder}{file} not found; colour grading LUT skipped.");
            return lut;
        }

        static void SetupBuiltInPost(Context ctx, Camera cam)
        {
            if (cam == null) cam = Camera.main;
            if (cam == null) return;
            var post = cam.GetComponent<SkyIsland.SkyIslandBuiltinPost>();
            if (post == null) post = cam.gameObject.AddComponent<SkyIsland.SkyIslandBuiltinPost>();
            var p = ctx.look.post;
            post.shader = Shader.Find("Hidden/SkyIsland/BuiltinPost");
            post.lut = LoadLut(ctx, 32);
            post.lutContribution = post.lut != null ? p.lutContribution : 0f;
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
