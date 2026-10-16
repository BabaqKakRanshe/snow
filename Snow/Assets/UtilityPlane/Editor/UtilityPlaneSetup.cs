#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UtilityPlane.EditorTools
{
    /// <summary>First-import settings for the Utility Plane v12 model and textures (Assets/UtilityPlane/...).</summary>
    public class UtilityPlaneImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains("/UtilityPlane/Textures/")) return;
            if (!assetImporter.importSettingsMissing) return;
            UtilityPlaneSetup.ApplyTextureSettings((TextureImporter)assetImporter, assetPath);
        }

        void OnPreprocessModel()
        {
            if (!assetPath.Replace('\\', '/').Contains("/UtilityPlane/Models/")) return;
            if (!assetImporter.importSettingsMissing) return;
            UtilityPlaneSetup.ApplyModelSettings((ModelImporter)assetImporter);
        }
    }

    /// <summary>
    /// Tools / Utility Plane / Build Materials + Prefabs:
    ///  - materials for Built-in, URP or HDRP (auto-detected), remapped onto the FBX by name;
    ///  - Prefabs/UtilityPlane.prefab: COL_* meshes become convex MeshColliders (renderers removed),
    ///    interaction volumes from Data/interaction_volumes.json become trigger BoxColliders on the "Interact" layer
    ///    with UtilityPlaneInteractable, UtilityPlaneRig is added; v7: small parts cast no shadows;
    ///    v8: two LODGroups from Data/lod.json (Exterior on the root, Cockpit on the Cockpit object);
    ///  - Prefabs/Cargo_CaseN.prefab for the six cargo cases; v7: StepLadder.prefab and PlaneKey.prefab.
    /// v8 materials: M_Airframe (fuselage, doors, fin, dorsal, struts) and M_Wing (wing, flaps, ailerons, horizontal
    /// tail).
    /// v9: the exterior atlases (Airframe, Wing, Details) and the survival kit are PS1 / PSX lo-fi textures: 512 px
    /// (survival 256), 64 colours with ordered dither, shading baked in; imported Point-filtered and uncompressed, used
    /// without normal and detail maps. New materials M_Fittings (cargo-bay seats and stowage hardware, interior style)
    /// and M_Survival (SM_Surv_* models -> Prefabs/Surv_*.prefab). The rig gets the fog print brushes.
    /// v10 (optimisation): Models/UtilityPlane.fbx imported without Read/Write (no CPU copy of every mesh); its
    /// MeshColliders and the fog print code use readable copies made here (Generated/UtilityPlane_RuntimeMeshes.asset,
    /// sub-assets kept by name between builds so references stay valid); shadow proxies
    /// <part>_Shadow from Data/lod.json (ShadowsOnly in LOD0, the part itself casts no shadow there); interior atlases
    /// imported at 1024, gauges / placards / displays at 2048.
    /// v11 (lighting): every Light_* is a spot shaped by a cookie (Textures/Cookies/T_Cookie_&lt;key&gt;.png: sRGB grey,
    /// clamped, uncompressed) with colour temperature and per-light shadow settings (UtilityPlaneRig.LightDefs);
    /// URP materials M_FX_Airlight / M_FX_Beam (shaders UtilityPlane/Airlight and UtilityPlane/Beam) go to the rig, which
    /// makes the glow quads and beam meshes at run time.
    /// v12 (PSX interior, edition 2): the interior atlases (Cockpit, Interior, Floor, Seats 512 px, Fittings 256) and the
    /// webbing are PSX textures like the exterior (32-colour palette with ordered dither, shading and painted light baked
    /// in, Point, colour uncompressed, no normal or detail maps); the instrument / placard atlas (T_Gauges_*,
    /// T_Displays_Emission, 1024 px) is pixel art with
    /// pixel-font legends, imported the same way; T_PixelFont = the same fonts for screen text (UtilityPlanePixelFont).
    /// No Rigidbody is added: the vehicle and cargo systems own physics.
    /// </summary>
    public static class UtilityPlaneSetup
    {
        const string Root = "Assets/UtilityPlane";
        const string Fbx = Root + "/Models/UtilityPlane.fbx";
        const string GeneratedDir = Root + "/Generated";
        const string RuntimeMeshesPath = GeneratedDir + "/UtilityPlane_RuntimeMeshes.asset";

        /// <summary>v10: false = Models/UtilityPlane.fbx is imported without Read/Write (saves the CPU copy of every mesh).
        /// Set true only if your own code reads plane meshes at run time (vertices / normals / triangles, runtime NavMesh
        /// baking from render meshes, outline smooth-normal baking ...), then run Build Materials + Prefabs again.</summary>
        public static readonly bool KeepMeshesReadable = false;

        /// <summary>v10: interior atlases (1024 px v10-v11; v12: PSX 512 px, Fittings 256).</summary>
        public static bool IsInteriorAtlas(string n) =>
            n.StartsWith("T_Cockpit_") || n.StartsWith("T_Interior_") || n.StartsWith("T_Floor_") ||
            n.StartsWith("T_Seats_") || n.StartsWith("T_Fittings_");

        /// <summary>v9: PS1 / PSX lo-fi atlases (exterior, survival kit, winter kit v1.2): palette textures with the
        /// shading baked in. Point filter keeps the texels crisp; no compression keeps the 64-colour palette and the
        /// dither intact (DXT would smear both).</summary>
        public static bool IsPsxTexture(string n) =>
            n.StartsWith("T_Airframe_") || n.StartsWith("T_Wing_") || n.StartsWith("T_Details_") ||
            n.StartsWith("T_Survival_") || n.StartsWith("T_KitCovers_") || n.StartsWith("T_KitProps_") ||
            IsInteriorAtlas(n) || IsTextAtlas(n) || n.StartsWith("T_Webbing_");                    // v12

        /// <summary>v12: instrument, placard, lightplate and display atlas: pixel art with pixel-font legends, 1024 px.
        /// Point keeps every letter sharp; no compression keeps the colours exact.</summary>
        public static bool IsTextAtlas(string n) => n.StartsWith("T_Gauges_") || n.StartsWith("T_Displays_");

        public static void ApplyTextureSettings(TextureImporter ti, string path)
        {
            string n = Path.GetFileNameWithoutExtension(path);
            bool keepSprite = n.StartsWith("T_PixelFont") && ti.textureType == TextureImporterType.Sprite;   // v12
            ti.maxTextureSize = n.StartsWith("T_FogPrint_") ? 128 :
                                n.StartsWith("T_GlassFog") || IsTextAtlas(n) ? 1024 :                       // v12: text atlas
                                n.Contains("Webbing") || n.Contains("_Detail_") || n.StartsWith("T_Prop") ||
                                n.StartsWith("T_PixelFont") || IsPsxTexture(n) ? 512 :                      // PSX exterior + interior, props
                                2048;                                                                        // anything else
            ti.mipmapEnabled = true;
            ti.anisoLevel = 4;
            ti.filterMode = FilterMode.Bilinear;
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.isReadable = false;
            ti.textureType = TextureImporterType.Default;
            if (n.StartsWith("T_Cookie_"))
            {
                // v11 light cookies: grey emission patterns, sRGB-encoded (the URP cookie atlas decodes them), alpha = grey
                // (Built-in uses alpha), clamped (spot cookie UVs end at the cone), no mips, no compression (smooth gradients)
                ti.sRGBTexture = true;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.mipmapEnabled = false;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.maxTextureSize = 256;
                ti.anisoLevel = 0;
                return;
            }
            if (IsPsxTexture(n))
            {
                ti.filterMode = FilterMode.Point;                 // crisp PSX texels (mips stay on: no shimmer far away)
                ti.anisoLevel = 0;
                // colour maps uncompressed: DXT would smear the palette, the dither and the pixel letters. v12: masks
                // (metal and smoothness in at most 4 even steps, G = B = 1) and the emission maps of the text atlas (black
                // + one glyph colour per 4 x 4 block almost everywhere) block-compress practically without loss, at 1/4
                // and 1/8 the memory
                ti.textureCompression = n.EndsWith("_MaskMap") || n.EndsWith("_Emission")
                    ? TextureImporterCompression.Compressed : TextureImporterCompression.Uncompressed;
            }
            if (n.StartsWith("T_PixelFont"))
            {
                // v12 pixel font sheet (white glyphs on transparent): exact texels, no mips; a sheet the user sliced
                // as Sprite (Multiple) stays a sprite sheet when the menu runs again
                if (keepSprite) ti.textureType = TextureImporterType.Sprite;
                ti.sRGBTexture = true;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = true;
                ti.filterMode = FilterMode.Point;
                ti.mipmapEnabled = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.anisoLevel = 0;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                return;
            }
            if (n.StartsWith("T_FogPrint_"))
            {
                // v9 fog print brushes: read on the CPU by UtilityPlaneRig.StampFogPrint
                ti.sRGBTexture = false;
                ti.isReadable = true;
                ti.mipmapEnabled = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                return;
            }
            if (n.EndsWith("_Normal"))
            {
                ti.textureType = TextureImporterType.NormalMap;
            }
            else if (n.EndsWith("_MaskMap") || n.EndsWith("_MetallicSmoothness") || n.EndsWith("_AO") || n.StartsWith("T_GlassFog") ||
                     n.EndsWith("_DetailMask"))
            {
                ti.sRGBTexture = false;                  // data: metallic / AO / detail / smoothness, fog order + density
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
            }
            else if (n.Contains("_Detail_") && n.EndsWith("_Albedo"))
            {
                // detail albedo x2 (128 = no change). URP Lit samples it raw and needs it linear (as sRGB it would
                // darken the surface to ~0.43x); Built-in Standard expects sRGB (compensated by unity_ColorSpaceDouble).
                ti.sRGBTexture = DetectPipeline() != "URP";
            }
            else
            {
                ti.sRGBTexture = true;
                ti.alphaIsTransparency = n.StartsWith("T_Prop");
            }
        }

        public static void ApplyModelSettings(ModelImporter mi)
        {
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importAnimation = false;
            mi.importBlendShapes = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.addCollider = false;
            // v10: the plane FBX without Read/Write (its colliders and fog meshes get readable copies, RuntimeCopy);
            // the small separate models (cargo, ladder, key, survival kit, winter kit) stay readable as before
            mi.isReadable = KeepMeshesReadable || !mi.assetPath.Replace('\\', '/').EndsWith("/Models/UtilityPlane.fbx");
        }

        [MenuItem("Tools/Utility Plane/Build Materials + Prefabs")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                // v10: the build reads meshes of the non-readable FBX: the editor allows that only outside Play Mode
                Debug.LogError("[UtilityPlane] Leave Play Mode first, then run Build Materials + Prefabs again.");
                return;
            }
            if (!(AssetImporter.GetAtPath(Fbx) is ModelImporter mi))
            {
                Debug.LogError($"[UtilityPlane] {Fbx} not found. Keep the folder at {Root}.");
                return;
            }
            foreach (var guid in AssetDatabase.FindAssets("t:Texture", new[] { Root + "/Textures" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(p) is TextureImporter ti)) continue;
                ApplyTextureSettings(ti, p);
                ti.SaveAndReimport();
            }

            string pipe = DetectPipeline();
            if (pipe == "URP") CheckUrpDepth();                                                     // v11
            EnsureFolder(Root, "Materials");
            EnsureFolder(Root, "Prefabs");

            var texel = LoadTexel();
            var mats = new (string name, Material mat)[]
            {
                ("M_Airframe", MakeLit("M_Airframe", "T_Airframe", pipe, FindTexel(texel, "Airframe"))),
                ("M_Wing", MakeLit("M_Wing", "T_Wing", pipe, FindTexel(texel, "Wing"))),
                ("M_Details", MakeLit("M_Details", "T_Details", pipe, FindTexel(texel, "Details"))),
                ("M_Fittings", MakeLit("M_Fittings", "T_Fittings", pipe, FindTexel(texel, "Fittings"))),      // v9
                ("M_Cockpit", MakeLit("M_Cockpit", "T_Cockpit", pipe, FindTexel(texel, "Cockpit"))),
                ("M_Interior", MakeLit("M_Interior", "T_Interior", pipe, FindTexel(texel, "Interior"))),
                ("M_Floor", MakeLit("M_Floor", "T_Floor", pipe, FindTexel(texel, "Floor"))),
                ("M_Seats", MakeLit("M_Seats", "T_Seats", pipe, FindTexel(texel, "Seats"))),
                ("M_Gauges", MakeDecal("M_Gauges", pipe, 0.3f, "T_Gauges_Emission", Color.white)),   // matte print
                // v6: the atlas holds the unlit lens look, T_Displays_Emission the lit lamp (per-lamp instance from the rig)
                ("M_Annunciator", MakeDecal("M_Annunciator", pipe, 0.8f, "T_Displays_Emission", Color.white)),
                ("M_RadioDisplay", MakeDecal("M_RadioDisplay", pipe, 0.7f, "T_Displays_Emission", Color.white)),
                ("M_Screen", MakeLens("M_Screen", pipe, new Color(0.012f, 0.016f, 0.016f), 0.85f)),
                ("M_Webbing", MakeTiled("M_Webbing", "T_Webbing", pipe, new Vector2(10f, 1f), 0.2f)),
                ("M_Glass", MakeGlass("M_Glass", pipe, new Color(0.85f, 0.92f, 0.92f, 0.12f))),
                ("M_GaugeGlass", MakeGlass("M_GaugeGlass", pipe, new Color(1f, 1f, 1f, 0.04f), 0.78f, false)),
                ("M_GlassFog", MakeFog("M_GlassFog", pipe)),
                ("M_PropBlur", MakeAlphaTex("M_PropBlur", "T_PropBlur", pipe)),
                ("M_PropDisc", MakeAlphaTex("M_PropDisc", "T_PropDisc", pipe)),
                ("M_Light_Red", MakeLens("M_Light_Red", pipe, new Color(0.35f, 0.02f, 0.01f))),
                ("M_Light_Green", MakeLens("M_Light_Green", pipe, new Color(0.02f, 0.3f, 0.05f))),
                ("M_Light_White", MakeLens("M_Light_White", pipe, new Color(0.6f, 0.6f, 0.6f))),
                ("M_Light_Beacon", MakeLens("M_Light_Beacon", pipe, new Color(0.35f, 0.03f, 0.01f))),
                ("M_Light_Landing", MakeLens("M_Light_Landing", pipe, new Color(0.7f, 0.7f, 0.65f))),
                ("M_Light_Cabin", MakeLens("M_Light_Cabin", pipe, new Color(0.7f, 0.65f, 0.55f))),
            };

            ApplyModelSettings(mi);
            foreach (var (name, mat) in mats)
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), mat);
            mi.SaveAndReimport();
            var survival = MakeLit("M_Survival", "T_Survival", pipe, FindTexel(texel, "Survival"));            // v9
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Root + "/Models" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (!Path.GetFileNameWithoutExtension(p).StartsWith("SM_Surv_")) continue;
                var smi = (ModelImporter)AssetImporter.GetAtPath(p);
                ApplyModelSettings(smi);
                smi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_Survival"), survival);
                smi.SaveAndReimport();
            }
            var details = mats.First(m => m.name == "M_Details").mat;
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Root + "/Models" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                string fn = Path.GetFileNameWithoutExtension(p);
                if (!(fn.StartsWith("SM_Cargo_") || fn == "SM_StepLadder" || fn == "SM_PlaneKey")) continue;
                var cmi = (ModelImporter)AssetImporter.GetAtPath(p);
                ApplyModelSettings(cmi);
                cmi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_Details"), details);
                cmi.SaveAndReimport();
            }

            // v11 lighting assets
            _cookies = AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Textures/Cookies" })
                .Select(g => AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(t => t != null && t.name.StartsWith("T_Cookie_")).ToArray();
            _fxGlow = pipe == "URP" ? MakeFx("M_FX_Airlight", "UtilityPlane/Airlight") : null;
            _fxBeam = pipe == "URP" ? MakeFx("M_FX_Beam", "UtilityPlane/Beam") : null;

            BeginRuntimeMeshes();                                                                   // v10
            BuildPlanePrefab(pipe);
            BuildCargoPrefabs();
            BuildPropPrefabs();
            BuildSurvivalPrefabs();
            EndRuntimeMeshes();
            AssetDatabase.SaveAssets();
            Debug.Log($"[UtilityPlane] Done ({pipe}), v12. Prefabs in {Root}/Prefabs; " +
                      (KeepMeshesReadable ? "FBX kept readable (KeepMeshesReadable), no mesh copies"
                                          : $"readable collider / fog meshes: {_rtCopies.Count} in {RuntimeMeshesPath}"));

            // files an older install leaves behind: nothing references them after this build
            // (v9: the PSX exterior atlases have no normal maps, and the Paint detail map is no longer used;
            //  v12: neither have the PSX interior atlases and the webbing, and the Grain / Leather detail maps are gone)
            var stale = new[] { Root + "/Materials/M_CargoNet.mat", Root + "/Textures/T_CargoNet_BCA.png",
                                Root + "/Textures/T_Airframe_Normal.png", Root + "/Textures/T_Wing_Normal.png",
                                Root + "/Textures/T_Details_Normal.png", Root + "/Textures/T_Detail_Paint_Albedo.png",
                                Root + "/Textures/T_Detail_Paint_Normal.png", Root + "/Textures/T_Webbing_Normal.png",
                                Root + "/Textures/T_Detail_Grain_Albedo.png", Root + "/Textures/T_Detail_Grain_Normal.png",
                                Root + "/Textures/T_Detail_Leather_Albedo.png", Root + "/Textures/T_Detail_Leather_Normal.png" }
                .Concat(new[] { "Cockpit", "Interior", "Floor", "Seats", "Fittings" }
                    .Select(a => $"{Root}/Textures/T_{a}_Normal.png"))
                .Concat(Atlases.SelectMany(a => new[] { $"{Root}/Textures/T_{a}_MetallicSmoothness.png",
                                                         $"{Root}/Textures/T_{a}_AO.png" }))
                .Where(File.Exists).ToArray();
            if (stale.Length > 0)
                Debug.Log("[UtilityPlane] Unused files from an older version (safe to delete): " + string.Join(", ", stale));
        }

        // ------------------------------------------------------------------ prefabs
#pragma warning disable 0649 // filled by JsonUtility
        [Serializable] class Vol { public string id, parent, kind, label; public float[] center, halfExtents; }
        [Serializable] class VolList { public Vol[] items; }
        [Serializable] class Texel
        {
            public string atlas, material, detail;
            public float metresPerUV, pxPerMetre, areaM2, detailTiling, detailNormalScale;
            public bool psx;                                    // v9: PSX atlas (no normal map, no detail map)
        }
        [Serializable] class TexelList { public Texel[] items; }
        [Serializable] class LodLevel { public float screenRelativeHeight; public int triangles; public string[] renderers; }
        [Serializable] class LodGroupData { public string name, root; public LodLevel[] levels; }
        [Serializable] class ShadowProxy { public string renderer, source, group; public float errorMm; public int triangles, sourceTriangles; }
        [Serializable] class LodFile { public string note; public LodGroupData[] groups; public ShadowProxy[] shadowProxies; }
#pragma warning restore 0649

        static readonly string[] Atlases = { "Airframe", "Wing", "Details", "Cockpit", "Interior", "Floor", "Seats", "Fittings" };

        // v7: renderers too small to cast a visible shadow (needles, knobs, switches, lamps, lenses, discs, glass, fog)
        static readonly string[] NoShadowPrefixes =
        {
            "Needle_", "Knob_", "SW_", "Guard_", "Rheo_", "Pull_", "Btn_", "Lamp_", "Lens_", "Display_", "ATT_", "HDG_",
            "TC_", "Clock_", "ALT_", "Compass_", "Belt_", "Lock_", "PropBlur", "PropDisc", "Glass"
        };

        static Texel[] LoadTexel()
        {
            string p = Root + "/Data/atlas_texel.json";
            if (!File.Exists(p)) return new Texel[0];
            var list = JsonUtility.FromJson<TexelList>("{\"items\":" + File.ReadAllText(p) + "}");
            return list != null && list.items != null ? list.items : new Texel[0];
        }

        static Texel FindTexel(Texel[] all, string atlas) => all.FirstOrDefault(t => t.atlas == atlas);

        static void BuildPlanePrefab(string pipe)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            var all = inst.GetComponentsInChildren<Transform>(true);

            // collider proxies
            int cols = 0;
            foreach (var t in all.Where(t => t.name.StartsWith("COL_")))
            {
                var mf = t.GetComponent<MeshFilter>();
                if (mf == null) continue;
                var mc = t.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = RuntimeCopy(mf.sharedMesh, t.name);                                   // v10
                mc.convex = true;
                // the propeller disk proxy must not spin with the blades inside the vehicle body
                if (t.name == "COL_Propeller") t.SetParent(inst.transform, true);
                UnityEngine.Object.DestroyImmediate(t.GetComponent<MeshRenderer>());
                UnityEngine.Object.DestroyImmediate(mf);
                cols++;
            }

            // interaction volumes
            int layer = EnsureLayer("Interact");
            string json = File.ReadAllText(Root + "/Data/interaction_volumes.json");
            var list = JsonUtility.FromJson<VolList>("{\"items\":" + json + "}");
            int vols = 0;
            foreach (var v in list.items)
            {
                var parent = all.FirstOrDefault(t => t.name == v.parent);
                if (parent == null) { Debug.LogWarning($"[UtilityPlane] parent {v.parent} not found for {v.id}"); continue; }
                var go = new GameObject("IV_" + v.id);
                go.transform.SetParent(parent, false);
                if (layer >= 0) go.layer = layer;
                var bc = go.AddComponent<BoxCollider>();
                bc.isTrigger = true;
                bc.center = new Vector3(v.center[0], v.center[1], v.center[2]);
                bc.size = 2f * new Vector3(v.halfExtents[0], v.halfExtents[1], v.halfExtents[2]);
                var it = go.AddComponent<UtilityPlaneInteractable>();
                it.id = v.id; it.kind = v.kind; it.label = v.label;
                vols++;
            }

            // v7: shadows only from parts big enough to matter
            int noShadow = 0;
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.name;
                if (NoShadowPrefixes.Any(p => n.StartsWith(p, StringComparison.Ordinal)) || n.EndsWith("_Glass") || n.EndsWith("_Fog"))
                {
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    noShadow++;
                }
            }

            // v10: the fog print code reads the fog meshes at run time -> readable copies (the FBX is not readable)
            int fogCopies = 0;
            foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.name != "Glass_Fog" && !r.name.EndsWith("_Fog", StringComparison.Ordinal)) continue;
                var fmf = r.GetComponent<MeshFilter>();
                if (fmf == null || fmf.sharedMesh == null) continue;
                fmf.sharedMesh = RuntimeCopy(fmf.sharedMesh, r.name);
                fogCopies++;
            }

            int lights = UtilityPlaneRig.BuildLights(inst.transform, pipe == "HDRP", _cookies, true);
            int shadowed = PresetLightShadows(inst, pipe);                                   // v11
            var rig = inst.GetComponent<UtilityPlaneRig>();
            if (rig == null) rig = inst.AddComponent<UtilityPlaneRig>();
            rig.fogPrintHand = Tex("T_FogPrint_Hand");                                   // v9
            rig.fogPrintFinger = Tex("T_FogPrint_Finger");
            rig.lightCookies = _cookies;                                                 // v11
            rig.glowMaterial = _fxGlow;
            rig.beamMaterial = _fxBeam;
            rig.lightsVersion = 11;
            if (pipe == "URP" && (_fxGlow == null || _fxBeam == null))
                Debug.LogWarning("[UtilityPlane] v11 glow / beam shaders unavailable: the plane lights work, without glows and beams.");
            string lodInfo = BuildLods(inst);
            string path = Root + "/Prefabs/UtilityPlane.prefab";
            FlushRuntimeMeshes();                                   // v10: the copies are on disk before the prefab refers to them
            PrefabUtility.SaveAsPrefabAsset(inst, path);
            UnityEngine.Object.DestroyImmediate(inst);
            Debug.Log($"[UtilityPlane] {path}: {cols} convex colliders, {vols} interaction volumes, {lights} lights " +
                      $"({_cookies.Length} cookies, {shadowed} with shadows at the default quality, glow / beam FX: " +
                      $"{(_fxGlow != null && _fxBeam != null ? "yes" : "no")}), {noShadow} renderers without shadows, " +
                      $"LOD groups: {lodInfo}, fog meshes copied: {fogCopies}");
        }

        // v8: LODGroups from Data/lod.json. Each level lists renderer names (unique in the prefab); <part>_LOD1 / _LOD2
        // are children of <part>, so moving parts carry their LODs. Returns e.g. "Exterior 3, Cockpit 3".
        static string BuildLods(GameObject inst)
        {
            string p = Root + "/Data/lod.json";
            if (!File.Exists(p)) { Debug.LogWarning("[UtilityPlane] Data/lod.json not found: no LODGroup."); return "none"; }
            var file = JsonUtility.FromJson<LodFile>(File.ReadAllText(p));
            if (file == null || file.groups == null) return "none";
            var byName = new System.Collections.Generic.Dictionary<string, Renderer>();
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                if (!byName.ContainsKey(r.name)) byName[r.name] = r;
            var made = new System.Collections.Generic.List<string>();
            foreach (var g in file.groups)
            {
                Transform root = g.root == "UtilityPlane" ? inst.transform :
                    inst.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == g.root);
                if (root == null || g.levels == null) { Debug.LogWarning($"[UtilityPlane] LOD group {g.name}: root {g.root} not found"); continue; }
                var lods = new LOD[g.levels.Length];
                for (int i = 0; i < g.levels.Length; i++)
                {
                    var names = g.levels[i].renderers ?? new string[0];
                    var rs = names.Where(byName.ContainsKey).Select(n => byName[n]).ToArray();
                    if (rs.Length != names.Length)
                        Debug.LogWarning($"[UtilityPlane] LOD group {g.name} level {i}: {names.Length - rs.Length} renderers not found: " +
                                         string.Join(", ", names.Where(n => !byName.ContainsKey(n)).Take(8)));
                    lods[i] = new LOD(g.levels[i].screenRelativeHeight, rs);
                }
                var lg = root.GetComponent<LODGroup>();
                if (lg == null) lg = root.gameObject.AddComponent<LODGroup>();
                lg.fadeMode = LODFadeMode.None;
                lg.SetLODs(lods);
                lg.RecalculateBounds();
                made.Add($"{g.name} {lods.Length}");
            }
            // v10: shadow proxies - the simplified copy casts the LOD0 shadow, the part itself only draws
            int proxies = 0;
            foreach (var sp in file.shadowProxies ?? new ShadowProxy[0])
            {
                if (!byName.TryGetValue(sp.renderer, out var pr) || !byName.TryGetValue(sp.source, out var src))
                {
                    Debug.LogWarning($"[UtilityPlane] shadow proxy {sp.renderer} / {sp.source} not found");
                    continue;
                }
                pr.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                pr.receiveShadows = false;
                src.shadowCastingMode = ShadowCastingMode.Off;
                proxies++;
            }
            if (proxies > 0) made.Add($"{proxies} shadow proxies");
            return made.Count > 0 ? string.Join(", ", made) : "none";
        }

        // ------------------------------------------------------------------ v10: readable runtime meshes
        static Mesh _rtHolder;
        static readonly Dictionary<Mesh, Mesh> _rtCopies = new Dictionary<Mesh, Mesh>();
        static readonly Dictionary<string, Mesh> _rtOld = new Dictionary<string, Mesh>();
        static readonly HashSet<string> _rtUsed = new HashSet<string>();

        /// <summary>v10: opens (or creates) the container of readable mesh copies. Sub-assets of the previous build are
        /// refilled in place by name, so their file IDs and every reference to them stay valid.</summary>
        static void BeginRuntimeMeshes()
        {
            _rtCopies.Clear();
            _rtOld.Clear();
            _rtUsed.Clear();
            _rtHolder = null;
            if (KeepMeshesReadable)
            {
                if (File.Exists(RuntimeMeshesPath)) AssetDatabase.DeleteAsset(RuntimeMeshesPath);   // unused with a readable FBX
                return;
            }
            EnsureFolder(Root, "Generated");
            _rtHolder = AssetDatabase.LoadAssetAtPath<Mesh>(RuntimeMeshesPath);
            if (_rtHolder == null)
            {
                if (File.Exists(RuntimeMeshesPath)) AssetDatabase.DeleteAsset(RuntimeMeshesPath);
                _rtHolder = new Mesh { name = "UtilityPlane_RuntimeMeshes" };
                AssetDatabase.CreateAsset(_rtHolder, RuntimeMeshesPath);
            }
            foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(RuntimeMeshesPath))
                if (o is Mesh m && !_rtOld.ContainsKey(m.name)) _rtOld[m.name] = m;
        }

        /// <summary>v10: readable copy of a mesh of the non-readable plane FBX, a sub-asset of
        /// Generated/UtilityPlane_RuntimeMeshes.asset named "&lt;object&gt;_RT". Used for MeshColliders (convex cooking in
        /// the player then needs no Read/Write, whatever the scale or the Prebake Collision Meshes setting) and for the
        /// fog meshes the print code reads. The editor may read any mesh outside Play Mode, so the copy is made here.
        /// Readable meshes (the separate models) are returned as they are.</summary>
        static Mesh RuntimeCopy(Mesh src, string objectName)
        {
            if (src == null || src.isReadable || _rtHolder == null) return src;
            if (_rtCopies.TryGetValue(src, out var c)) return c;
            string n = objectName + "_RT";
            for (int k = 2; _rtUsed.Contains(n); k++) n = objectName + "_RT" + k;
            _rtUsed.Add(n);
            if (_rtOld.TryGetValue(n, out c)) c.Clear();
            else
            {
                c = new Mesh();
                AssetDatabase.AddObjectToAsset(c, _rtHolder);
            }
            c.name = n;
            c.indexFormat = src.indexFormat;
            c.vertices = src.vertices;
            c.normals = src.normals;
            c.tangents = src.tangents;
            var uv = new List<Vector2>();
            src.GetUVs(0, uv);
            if (uv.Count == src.vertexCount) c.SetUVs(0, uv);
            c.subMeshCount = src.subMeshCount;
            for (int i = 0; i < src.subMeshCount; i++)
                c.SetTriangles(src.GetTriangles(i), i, false);
            c.RecalculateBounds();
            EditorUtility.SetDirty(c);
            _rtCopies[src] = c;
            return c;
        }

        static void FlushRuntimeMeshes()
        {
            if (_rtHolder == null) return;
            EditorUtility.SetDirty(_rtHolder);
            AssetDatabase.SaveAssetIfDirty(_rtHolder);
        }

        /// <summary>v10: drops copies the new build no longer uses and writes the container.</summary>
        static void EndRuntimeMeshes()
        {
            if (_rtHolder == null) return;
            foreach (var kv in _rtOld)
                if (!_rtUsed.Contains(kv.Key))
                {
                    AssetDatabase.RemoveObjectFromAsset(kv.Value);
                    UnityEngine.Object.DestroyImmediate(kv.Value, true);
                }
            FlushRuntimeMeshes();
        }

        // Adds a user layer if it is missing (first free slot from 8). Returns -1 if all slots are taken.
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
                Debug.Log($"[UtilityPlane] Added layer \"{name}\" at index {i}. Turn its collisions off in Physics settings.");
                return i;
            }
            Debug.LogWarning($"[UtilityPlane] No free layer for \"{name}\"; volumes stay on Default.");
            return -1;
        }

        static void BuildCargoPrefabs()
        {
            for (int i = 1; i <= 6; i++)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Models/SM_Cargo_Case{i}.fbx");
                if (model == null) continue;
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                var mf = inst.GetComponentInChildren<MeshFilter>();
                if (mf != null)
                {
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = RuntimeCopy(mf.sharedMesh, mf.name);                              // v10
                    mc.convex = true;
                }
                PrefabUtility.SaveAsPrefabAsset(inst, $"{Root}/Prefabs/Cargo_Case{i}.prefab");
                UnityEngine.Object.DestroyImmediate(inst);
            }
        }

        // v7: step ladder (COL_* boxes -> BoxColliders, IV_Ladder_Climb -> trigger + UtilityPlaneInteractable "ladder",
        // Ladder_RearLegs = fold hinge about its local X) and the key (convex MeshCollider)
        static void BuildPropPrefabs()
        {
            int layer = LayerMask.NameToLayer("Interact");
            var lad = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Models/SM_StepLadder.fbx");
            if (lad != null)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(lad);
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                foreach (var t in inst.GetComponentsInChildren<Transform>(true)
                             .Where(t => t.name.StartsWith("COL_") || t.name.StartsWith("IV_")).ToArray())
                {
                    var mf = t.GetComponent<MeshFilter>();
                    if (mf == null) continue;
                    var b = mf.sharedMesh.bounds;
                    var bc = t.gameObject.AddComponent<BoxCollider>();
                    bc.center = b.center;
                    bc.size = b.size;
                    if (t.name.StartsWith("IV_"))
                    {
                        bc.isTrigger = true;
                        if (layer >= 0) t.gameObject.layer = layer;
                        var it = t.gameObject.AddComponent<UtilityPlaneInteractable>();
                        it.id = "Ladder_Climb"; it.kind = "ladder"; it.label = "Стремянка";
                    }
                    UnityEngine.Object.DestroyImmediate(t.GetComponent<MeshRenderer>());
                    UnityEngine.Object.DestroyImmediate(mf);
                }
                PrefabUtility.SaveAsPrefabAsset(inst, $"{Root}/Prefabs/StepLadder.prefab");
                UnityEngine.Object.DestroyImmediate(inst);
            }
            var key = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Models/SM_PlaneKey.fbx");
            if (key != null)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(key);
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                var mf = inst.GetComponentInChildren<MeshFilter>();
                if (mf != null)
                {
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = RuntimeCopy(mf.sharedMesh, mf.name);                              // v10
                    mc.convex = true;
                }
                PrefabUtility.SaveAsPrefabAsset(inst, $"{Root}/Prefabs/PlaneKey.prefab");
                UnityEngine.Object.DestroyImmediate(inst);
            }
        }

        // v9: survival kit. COL_* children -> convex MeshColliders (renderers removed); Socket_* empties stay for the game
        // (grip = the prefab root, light / muzzle / flame points); Surv_Bag_Strap (the strap shown while the bag is
        // tied down, CargoItem.securedMarker) is saved inactive.
        static void BuildSurvivalPrefabs()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Root + "/Models" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                string fn = Path.GetFileNameWithoutExtension(p);
                if (!fn.StartsWith("SM_Surv_")) continue;
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                foreach (var t in inst.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("COL_")).ToArray())
                {
                    var mf = t.GetComponent<MeshFilter>();
                    if (mf == null) continue;
                    var mc = t.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = RuntimeCopy(mf.sharedMesh, mf.name);                              // v10
                    mc.convex = true;
                    UnityEngine.Object.DestroyImmediate(t.GetComponent<MeshRenderer>());
                    UnityEngine.Object.DestroyImmediate(mf);
                }
                var strap = inst.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Surv_Bag_Strap");
                if (strap != null) strap.gameObject.SetActive(false);
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                    if (fn != "SM_Surv_Bag" && fn != "SM_Surv_Shovel") r.shadowCastingMode = ShadowCastingMode.Off;
                string name = "Surv_" + fn.Substring("SM_Surv_".Length);
                PrefabUtility.SaveAsPrefabAsset(inst, $"{Root}/Prefabs/{name}.prefab");
                UnityEngine.Object.DestroyImmediate(inst);
            }
        }

        // ------------------------------------------------------------------ v11 lighting
        static Texture2D[] _cookies = new Texture2D[0];
        static Material _fxGlow, _fxBeam;

        // v11: the glow and beam shaders read the camera depth texture in the transparent pass
        static void CheckUrpDepth()
        {
            var rp = GraphicsSettings.currentRenderPipeline;
            if (rp == null) return;
            var so = new SerializedObject(rp);
            var req = so.FindProperty("m_RequireDepthTexture");
            if (req != null && !req.boolValue)
                Debug.LogWarning("[UtilityPlane] URP Asset: Depth Texture is off. The v11 glows and beams need it (without it they " +
                                 "show through walls); a camera can also request it in its own settings.");
            var list = so.FindProperty("m_RendererDataList");
            if (list == null) return;
            for (int i = 0; i < list.arraySize; i++)
            {
                var data = list.GetArrayElementAtIndex(i).objectReferenceValue;
                if (data == null) continue;
                var mode = new SerializedObject(data).FindProperty("m_CopyDepthMode");
                if (mode != null && mode.intValue == 1)                  // CopyDepthMode.AfterTransparents
                    Debug.LogWarning($"[UtilityPlane] {data.name}: Depth Texture Mode is After Transparents. The v11 glows and beams " +
                                     "read the depth in the transparent pass: set After Opaques or Force Prepass.");
            }
        }

        // URP-only effect materials (templates: the rig makes one instance per lamp)
        static Material MakeFx(string name, string shaderName)
        {
            var sh = Shader.Find(shaderName);
            if (sh == null || ShaderUtil.ShaderHasError(sh) || !sh.isSupported)
            {
                Debug.LogWarning($"[UtilityPlane] Shader {shaderName} is missing, has errors or is not supported here: no {name}.");
                return null;
            }
            var mat = GetOrCreate(name, "URP", sh);
            mat.renderQueue = -1;                                        // the shader's queue (Transparent)
            mat.shaderKeywords = new string[0];
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // Shadows the prefab lights get at the rig's default quality (Medium); the rig re-applies its shadowQuality at
        // run time. URP: per-light shadow resolution tier in UniversalAdditionalLightData (serialized field).
        static int PresetLightShadows(GameObject inst, string pipe)
        {
            Type urpData = pipe == "URP" ? FindTypeByName("UnityEngine.Rendering.Universal.UniversalAdditionalLightData") : null;
            int n = 0;
            foreach (var d in UtilityPlaneRig.LightDefs)
            {
                var lt = inst.GetComponentsInChildren<Light>(true).FirstOrDefault(l => l.name == "Light_" + d.key);
                if (lt == null) continue;
                bool cast = d.shadowTier >= 0 && d.shadowFrom <= UtilityPlaneRig.LightShadowQuality.Medium;
                lt.shadows = cast ? LightShadows.Soft : LightShadows.None;
                if (!cast) continue;
                n++;
                if (urpData == null) continue;
                Component c = lt.GetComponent(urpData);
                if (c == null) c = lt.gameObject.AddComponent(urpData);
                var so = new SerializedObject(c);
                var tier = so.FindProperty("m_AdditionalLightsShadowResolutionTier");
                if (tier != null) { tier.intValue = d.shadowTier; so.ApplyModifiedPropertiesWithoutUndo(); }
            }
            return n;
        }

        static Type FindTypeByName(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }

        // ------------------------------------------------------------------ materials
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

        static Texture2D Tex(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Textures/{name}.png");

        static Shader LitShader(string pipe) =>
            pipe == "HDRP" ? Shader.Find("HDRP/Lit") :
            pipe == "URP" ? Shader.Find("Universal Render Pipeline/Lit") : Shader.Find("Standard");

        static Material GetOrCreate(string name, string pipe, Shader shader = null)
        {
            string path = $"{Root}/Materials/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = shader != null ? shader : LitShader(pipe);
            if (mat == null)
            {
                mat = new Material(sh);
                AssetDatabase.CreateAsset(mat, path);
            }
            else mat.shader = sh;
            return mat;
        }

        static void SetBase(Material m, string pipe, Texture2D tex, Color col)
        {
            string map = pipe == "HDRP" ? "_BaseColorMap" : pipe == "URP" ? "_BaseMap" : "_MainTex";
            string c = pipe == "BuiltIn" ? "_Color" : "_BaseColor";
            if (tex != null) m.SetTexture(map, tex);
            m.SetColor(c, col);
        }

        static void SetEmission(Material m, string pipe, Texture2D map)
        {
            if (pipe == "HDRP")
            {
                if (map != null) m.SetTexture("_EmissiveColorMap", map);
                m.SetColor("_EmissiveColor", Color.black);
            }
            else
            {
                if (map != null) m.SetTexture("_EmissionMap", map);
                m.SetColor("_EmissionColor", Color.black);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
        }

        static void SetSmoothness(Material m, string pipe, float s)
        {
            m.SetFloat(pipe == "BuiltIn" ? "_Glossiness" : "_Smoothness", s);
        }

        // v7: one packed map per atlas, T_<Atlas>_MaskMap = R metallic, G occlusion, B detail mask (1), A smoothness.
        // URP Lit and Built-in Standard read it as the Metallic map (R, A) and the Occlusion map (G); HDRP as its Mask Map.
        static Material MakeLit(string name, string prefix, string pipe, Texel detail)
        {
            Material mat = GetOrCreate(name, pipe);
            bool psx = (detail != null && detail.psx) || IsPsxTexture(prefix + "_");
            // v9: PSX atlases have no normal map (an old T_*_Normal left in the folder is ignored)
            Texture2D bc = Tex(prefix + "_BaseColor"), nrm = psx ? null : Tex(prefix + "_Normal"), mask = Tex(prefix + "_MaskMap");
            SetBase(mat, pipe, bc, Color.white);
            if (psx)
            {
                foreach (var t in new[] { "_DetailAlbedoMap", "_DetailNormalMap", "_DetailMask" })
                    if (mat.HasProperty(t)) mat.SetTexture(t, null);
                mat.DisableKeyword("_DETAIL_MULX2");
                mat.DisableKeyword("_NORMALMAP");
            }
            switch (pipe)
            {
                case "URP":
                    mat.SetTexture("_BumpMap", nrm); mat.SetFloat("_BumpScale", 1f);
                    if (nrm != null) mat.EnableKeyword("_NORMALMAP");
                    mat.SetTexture("_MetallicGlossMap", mask); mat.SetFloat("_Smoothness", 1f);
                    mat.SetFloat("_SmoothnessTextureChannel", 0f); mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                    mat.SetTexture("_OcclusionMap", mask); mat.SetFloat("_OcclusionStrength", 1f);
                    mat.EnableKeyword("_OCCLUSIONMAP");
                    break;
                case "HDRP":
                    mat.SetTexture("_NormalMap", nrm);
                    mat.SetTexture("_MaskMap", mask);
                    mat.SetFloat("_MetallicRemapMin", 0f); mat.SetFloat("_MetallicRemapMax", 1f);
                    mat.SetFloat("_SmoothnessRemapMin", 0f); mat.SetFloat("_SmoothnessRemapMax", 1f);
                    mat.SetFloat("_AORemapMin", 0f); mat.SetFloat("_AORemapMax", 1f);
                    break;
                default:
                    mat.SetTexture("_BumpMap", nrm);
                    if (nrm != null) mat.EnableKeyword("_NORMALMAP");
                    mat.SetTexture("_MetallicGlossMap", mask); mat.SetFloat("_GlossMapScale", 1f);
                    mat.SetFloat("_SmoothnessTextureChannel", 0f); mat.EnableKeyword("_METALLICGLOSSMAP");
                    mat.SetTexture("_OcclusionMap", mask); mat.SetFloat("_OcclusionStrength", 1f);
                    break;
            }
            if (!psx && detail != null && !string.IsNullOrEmpty(detail.detail) && pipe != "HDRP") SetDetail(mat, pipe, detail);
            Validate(mat, pipe);
            return mat;
        }

        // micro grain on top of the baked atlas (URP / Built-in detail maps, x2 albedo + normal, tiled on UV0;
        // the atlases have uniform texel density, Data/atlas_texel.json gives the tiling). HDRP: not set.
        static void SetDetail(Material m, string pipe, Texel d)
        {
            var alb = Tex("T_Detail_" + d.detail + "_Albedo");
            var nrm = Tex("T_Detail_" + d.detail + "_Normal");
            if (alb == null || nrm == null || d.detailTiling <= 0f) return;
            m.SetTexture("_DetailAlbedoMap", alb);
            m.SetTexture("_DetailNormalMap", nrm);
            m.SetFloat("_DetailNormalMapScale", d.detailNormalScale);
            if (pipe == "URP") m.SetFloat("_DetailAlbedoMapScale", 1f);
            m.SetTextureScale("_DetailAlbedoMap", new Vector2(d.detailTiling, d.detailTiling));
            m.EnableKeyword("_DETAIL_MULX2");
        }

        // gauge faces / placards / lightplates: printed atlas; emission map = backlit markings only (driven by the rig's
        // material instances). Annunciator lamps and the radio faces (Display_Radio) use T_Displays_Emission.
        static Material MakeDecal(string name, string pipe, float smooth, string emissionMap, Color baseTint)
        {
            Material mat = GetOrCreate(name, pipe);
            SetBase(mat, pipe, Tex("T_Gauges_BC"), baseTint);
            SetSmoothness(mat, pipe, smooth);
            SetEmission(mat, pipe, Tex(emissionMap));
            Validate(mat, pipe);
            return mat;
        }

        // tiling material (cargo barrier webbing: UV u runs in metres along the strap). v12: PSX, no normal map
        static Material MakeTiled(string name, string prefix, string pipe, Vector2 tiling, float smooth)
        {
            Material mat = GetOrCreate(name, pipe);
            SetBase(mat, pipe, Tex(prefix + "_BaseColor"), Color.white);
            var nrm = IsPsxTexture(prefix + "_") ? null : Tex(prefix + "_Normal");
            switch (pipe)
            {
                case "HDRP":
                    mat.SetTexture("_NormalMap", nrm);
                    mat.SetTextureScale("_BaseColorMap", tiling);
                    break;
                case "URP":
                    mat.SetTexture("_BumpMap", nrm);
                    if (nrm != null) mat.EnableKeyword("_NORMALMAP"); else mat.DisableKeyword("_NORMALMAP");
                    mat.SetTextureScale("_BaseMap", tiling);
                    break;
                default:
                    mat.SetTexture("_BumpMap", nrm);
                    if (nrm != null) mat.EnableKeyword("_NORMALMAP"); else mat.DisableKeyword("_NORMALMAP");
                    mat.SetTextureScale("_MainTex", tiling);
                    break;
            }
            mat.SetFloat("_Metallic", 0f);
            SetSmoothness(mat, pipe, smooth);
            Validate(mat, pipe);
            return mat;
        }

        static Material MakeLens(string name, string pipe, Color tint, float smooth = 0.9f)
        {
            Material mat = GetOrCreate(name, pipe);
            SetBase(mat, pipe, null, tint);
            SetSmoothness(mat, pipe, smooth);
            SetEmission(mat, pipe, null);
            Validate(mat, pipe);
            return mat;
        }

        // reflections = false (gauge glass, v6): no sun glints and no probe reflections, so the dials stay readable
        // against a bright windshield; switch them back on in the material if you want glare.
        static Material MakeGlass(string name, string pipe, Color col, float smooth = 0.95f, bool reflections = true)
        {
            Material mat = GetOrCreate(name, pipe);
            SetBase(mat, pipe, null, col);
            SetSmoothness(mat, pipe, smooth);
            if (!reflections && pipe != "HDRP")
            {
                mat.SetFloat("_SpecularHighlights", 0f);
                mat.SetFloat(pipe == "URP" ? "_EnvironmentReflections" : "_GlossyReflections", 0f);
                mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                mat.EnableKeyword(pipe == "URP" ? "_ENVIRONMENTREFLECTIONS_OFF" : "_GLOSSYREFLECTIONS_OFF");
            }
            else if (!reflections) SetSmoothness(mat, pipe, 0.3f);      // HDRP: no per-material toggle, blur them
            MakeTransparent(mat, pipe, true);
            Validate(mat, pipe);
            return mat;
        }

        // v7: prop blur / disc: alpha from the texture, two-sided, no shadows (set on the renderers); the rig fades
        // _BaseColor.a on its instances
        static Material MakeAlphaTex(string name, string texName, string pipe)
        {
            Material mat = GetOrCreate(name, pipe);
            SetBase(mat, pipe, Tex(texName), new Color(1f, 1f, 1f, 0f));
            SetSmoothness(mat, pipe, 0.25f);
            if (pipe != "HDRP") mat.SetFloat("_Metallic", 0f);
            MakeTransparent(mat, pipe, true, fade: true);
            Validate(mat, pipe);
            return mat;
        }

        // v7 (SYS-01): fog veil on the inner glass layers. URP: UtilityPlane/GlassFog (clears from the bottom by the
        // mask); HDRP / Built-in: transparent Lit, the rig fades an even veil.
        static Material MakeFog(string name, string pipe)
        {
            var sh = pipe == "URP" ? Shader.Find("UtilityPlane/GlassFog") : null;
            if (sh != null && (ShaderUtil.ShaderHasError(sh) || !sh.isSupported))
            {
                Debug.LogWarning("[UtilityPlane] Shader UtilityPlane/GlassFog has errors or is not supported here: " +
                                 "M_GlassFog falls back to transparent Lit (even veil, no clearing by the mask).");
                sh = null;
            }
            if (sh != null)
            {
                var mat = GetOrCreate(name, pipe, sh);
                mat.renderQueue = -1;                           // the shader's own queue (Transparent+1), also after a fallback
                mat.shaderKeywords = new string[0];
                mat.SetTexture("_FogMask", Tex("T_GlassFog_Mask"));
                mat.SetColor("_FogColor", new Color(0.86f, 0.88f, 0.90f, 1f));
                mat.SetFloat("_Fog", 0f); mat.SetFloat("_FogSide", 0f);
                mat.SetFloat("_Edge", 0.12f); mat.SetFloat("_MaxAlpha", 0.85f);
                EditorUtility.SetDirty(mat);
                return mat;
            }
            var m = GetOrCreate(name, pipe);
            SetBase(m, pipe, null, new Color(0.86f, 0.88f, 0.90f, 0f));
            SetSmoothness(m, pipe, 0.2f);
            MakeTransparent(m, pipe, true, fade: true);
            Validate(m, pipe);
            return m;
        }

        // fade = true (v7: prop discs, fog fallback): straight alpha that also fades the specular, so a fully transparent
        // texel shows no sheen; fade = false (glass): reflections stay on a clear pane.
        static void MakeTransparent(Material mat, string pipe, bool twoSided, bool fade = false)
        {
            switch (pipe)
            {
                case "URP":
                    mat.SetFloat("_Surface", 1f); mat.SetFloat("_Blend", 0f);
                    mat.SetFloat("_BlendModePreserveSpecular", fade ? 0f : 1f);
                    mat.SetOverrideTag("RenderType", "Transparent");
                    mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                    mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                    mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                    mat.SetFloat("_ZWrite", 0f); mat.SetFloat("_Cull", twoSided ? 0f : 2f);
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.renderQueue = (int)RenderQueue.Transparent;
                    break;
                case "HDRP":
                    mat.SetFloat("_SurfaceType", 1f); mat.SetFloat("_BlendMode", 0f);
                    mat.SetFloat("_EnableBlendModePreserveSpecularLighting", fade ? 0f : 1f);
                    mat.SetFloat("_DoubleSidedEnable", twoSided ? 1f : 0f);
                    break;
                default:
                    mat.SetFloat("_Mode", fade ? 2f : 3f);              // Standard: Fade / Transparent
                    mat.SetOverrideTag("RenderType", "Transparent");
                    mat.SetInt("_SrcBlend", (int)(fade ? BlendMode.SrcAlpha : BlendMode.One));
                    mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                    mat.SetInt("_ZWrite", 0);
                    mat.DisableKeyword("_ALPHATEST_ON");
                    if (fade) { mat.EnableKeyword("_ALPHABLEND_ON"); mat.DisableKeyword("_ALPHAPREMULTIPLY_ON"); }
                    else { mat.DisableKeyword("_ALPHABLEND_ON"); mat.EnableKeyword("_ALPHAPREMULTIPLY_ON"); }
                    mat.renderQueue = (int)RenderQueue.Transparent;
                    break;
            }
        }

        // URP and HDRP set their shader keywords in their own editor code. Call it through reflection so this file
        // compiles in any pipeline (no package references). Built-in keywords are set above.
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
                    catch (Exception e) { Debug.LogWarning($"[UtilityPlane] {typeName}.{method}: {e.InnerException?.Message ?? e.Message}"); }
                    return;
                }
            }
        }
    }
}
#endif
