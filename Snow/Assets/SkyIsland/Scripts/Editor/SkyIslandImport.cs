using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SkyIsland.EditorTools
{
    /// <summary>
    /// Model-name rules of a location (layout vegetationPrefixes / grassPrefixes / noColliderPrefixes).
    /// Defaults are the Sky Island names, so v1 data behaves exactly as before.
    /// </summary>
    public sealed class SkyIslandModelRules
    {
        public static readonly string[] DefaultVegetation = { "Spruce_", "Dead_" };
        public static readonly string[] DefaultGrass = { "SkyIsland_Grass", "Grass_Clump" };
        public static readonly string[] DefaultNoCollider = { "Airfield_Lights", "Approach_Lights", "Power_Lines" };
        public static readonly SkyIslandModelRules Default = new SkyIslandModelRules(null, null, null);

        public readonly string[] vegetation, grass, noCollider;

        public SkyIslandModelRules(string[] vegetationPrefixes, string[] grassPrefixes, string[] noColliderPrefixes)
        {
            vegetation = Pick(vegetationPrefixes, DefaultVegetation);
            grass = Pick(grassPrefixes, DefaultGrass);
            noCollider = Pick(noColliderPrefixes, DefaultNoCollider);
        }

        public static SkyIslandModelRules From(LayoutData layout) =>
            layout == null ? Default : new SkyIslandModelRules(layout.vegetationPrefixes, layout.grassPrefixes, layout.noColliderPrefixes);

        /// <summary>Tree setup: LODGroup from *_LODn children, capsule collider, vegetation static flags.</summary>
        public bool IsVegetation(string name) => Match(name, vegetation);
        /// <summary>No collider, no shadows, LOD cull 2%.</summary>
        public bool IsGrass(string name) => Match(name, grass);
        /// <summary>Object (model or child) without collider and shadows.</summary>
        public bool IsNoCollider(string name) => Match(name, noCollider);
        /// <summary>Import-time MeshColliders for a model file.</summary>
        public bool ModelGetsCollider(string modelName) => !(IsVegetation(modelName) || IsGrass(modelName) || IsNoCollider(modelName));

        // null/empty list = defaults (JsonUtility cannot tell an absent list from an empty one)
        static string[] Pick(string[] list, string[] def) => list != null && list.Length > 0 ? list : def;

        static bool Match(string name, string[] prefixes)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (var p in prefixes)
                if (!string.IsNullOrEmpty(p) && name.StartsWith(p, StringComparison.Ordinal)) return true;
            return false;
        }
    }

    /// <summary>Import settings for the package's textures and models (applied automatically on import).</summary>
    public static class SkyIslandImport
    {
        // ------------------------------------------------------------------ which assets are ours
        /// <summary>
        /// Sky Island assets (path contains "SkyIsland", as in v1) or assets of a location folder ".../Locations/&lt;Id&gt;/"
        /// that has a Data/*_Layout.json (so unrelated "Locations" folders of the project are left alone).
        /// </summary>
        public static bool IsPackageAsset(string path, string sub)
        {
            path = path.Replace('\\', '/');
            int k = path.LastIndexOf(sub, StringComparison.Ordinal);
            if (k < 0) return false;
            if (path.Contains("SkyIsland")) return true;
            return path.Contains("/Locations/") && IsLocationRoot(path.Substring(0, k));
        }

        /// <summary>Folder that contains a sub folder (e.g. "/Models/"), or null.</summary>
        public static string RootOf(string path, string sub)
        {
            path = path.Replace('\\', '/');
            int k = path.LastIndexOf(sub, StringComparison.Ordinal);
            return k < 0 ? null : path.Substring(0, k);
        }

        static readonly Dictionary<string, string> s_LayoutFile = new Dictionary<string, string>();

        /// <summary>Data/&lt;Id&gt;_Layout.json of a location root on disk (null if none). Found files are cached per root.</summary>
        static string LayoutFile(string root)
        {
            if (string.IsNullOrEmpty(root)) return null;
            if (s_LayoutFile.TryGetValue(root, out var cached) && File.Exists(cached)) return cached;
            string found = null;
            string data = root + "/Data";
            if (Directory.Exists(data))
            {
                var files = Directory.GetFiles(data, "*_Layout.json");
                Array.Sort(files, StringComparer.Ordinal);
                string preferred = data + "/" + Path.GetFileName(root) + "_Layout.json";
                foreach (var f in files)
                {
                    var p = f.Replace('\\', '/');
                    if (found == null || p == preferred) found = p;
                }
            }
            if (found != null) s_LayoutFile[root] = found;   // not-found is not cached: the Data folder may arrive later
            return found;
        }

        static bool IsLocationRoot(string root) => LayoutFile(root) != null;

        // ------------------------------------------------------------------ data-driven hints (cached per file version)
        [Serializable] internal class PrefixLists { public string[] vegetationPrefixes, grassPrefixes, noColliderPrefixes; }
        [Serializable] internal class AlphaSpec { public string type, albedo, curtains; public bool alphaFromTexture; }
        [Serializable] internal class AlphaSpecList { public AlphaSpec[] materials; }

        sealed class AlphaHints
        {
            public static readonly AlphaHints None = new AlphaHints();
            public readonly HashSet<string> cutout = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> blend = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        static readonly Dictionary<string, (DateTime stamp, SkyIslandModelRules rules)> s_Rules = new Dictionary<string, (DateTime, SkyIslandModelRules)>();
        static readonly Dictionary<string, (DateTime stamp, AlphaHints hints)> s_Alpha = new Dictionary<string, (DateTime, AlphaHints)>();

        /// <summary>Model rules of the location that owns a model asset (layout prefix lists, Sky Island defaults otherwise).</summary>
        public static SkyIslandModelRules RulesForAsset(string assetPath)
        {
            string layout = LayoutFile(RootOf(assetPath, "/Models/"));
            if (layout == null) return SkyIslandModelRules.Default;
            var stamp = File.GetLastWriteTimeUtc(layout);
            if (s_Rules.TryGetValue(layout, out var c) && c.stamp == stamp) return c.rules;
            var rules = SkyIslandModelRules.Default;
            try
            {
                var l = JsonUtility.FromJson<PrefixLists>(File.ReadAllText(layout));
                if (l != null) rules = new SkyIslandModelRules(l.vegetationPrefixes, l.grassPrefixes, l.noColliderPrefixes);
            }
            catch (Exception ex) { Debug.LogWarning($"[SkyIsland] could not read model rules from {layout}: {ex.Message}"); }
            s_Rules[layout] = (stamp, rules);
            return rules;
        }

        /// <summary>Albedo files of cutout materials and of transparent materials with alphaFromTexture
        /// (local textures of the location's Materials.json; "shared:" textures keep the Sky Island settings).</summary>
        static AlphaHints AlphaHintsForAsset(string assetPath)
        {
            string layout = LayoutFile(RootOf(assetPath, "/Textures/"));
            if (layout == null) return AlphaHints.None;
            string mats = layout.Substring(0, layout.Length - "_Layout.json".Length) + "_Materials.json";
            if (!File.Exists(mats)) return AlphaHints.None;
            var stamp = File.GetLastWriteTimeUtc(mats);
            if (s_Alpha.TryGetValue(mats, out var c) && c.stamp == stamp) return c.hints;
            var hints = new AlphaHints();
            try
            {
                var list = JsonUtility.FromJson<AlphaSpecList>(File.ReadAllText(mats));
                if (list != null && list.materials != null)
                    foreach (var m in list.materials)
                    {
                        if (m != null && m.type == "interior" && !string.IsNullOrEmpty(m.curtains) && !m.curtains.StartsWith(SkyIslandBuilder.SharedPrefix, StringComparison.Ordinal))
                            hints.blend.Add(m.curtains);                // curtain layer keeps its alpha
                        if (m == null || string.IsNullOrEmpty(m.albedo) || m.albedo.StartsWith(SkyIslandBuilder.SharedPrefix, StringComparison.Ordinal)) continue;
                        if (m.type == "cutout") hints.cutout.Add(m.albedo);
                        else if (m.type == "transparent" && m.alphaFromTexture) hints.blend.Add(m.albedo);
                    }
            }
            catch (Exception ex) { Debug.LogWarning($"[SkyIsland] could not read texture alpha hints from {mats}: {ex.Message}"); }
            s_Alpha[mats] = (stamp, hints);
            return hints;
        }

        // ------------------------------------------------------------------ textures
        static bool LegacyCutout(string file) =>
            file == "T_SpruceBranch_A" || file == "T_SpruceImpostor_A" || file == "T_Grass_A" || file == "T_CamoNet_A" || file == "T_HangarNumbers_A" || file == "T_ChainLink_A";

        public static void ConfigureTexture(TextureImporter ti, string path)
        {
            string file = Path.GetFileNameWithoutExtension(path);
            string fileWithExt = Path.GetFileName(path);
            // PSX textures are '<stem>_px' (v2.5, Utility Plane recipe: colour '<..._A>_px', metal/smoothness mask '<..._M>_px')
            bool isPsx = file.EndsWith("_px");
            string core = isPsx ? file.Substring(0, file.Length - 3) : file;
            bool isNormal = core.EndsWith("_N");
            bool isLut = file.Contains("_LUT");
            bool isMask = core.EndsWith("_M");
            bool isDetail = file.Contains("Detail_");
            bool isNoise = file == "T_SkyClouds" || file == "T_FogNoise";
            bool bigMacro = file.StartsWith("T_Runway_") || file.StartsWith("T_Pavement_") || file.Contains("_Macro");
            bool isMacro = bigMacro || file.StartsWith("T_TerrainMacro");
            bool isSmoke = file.StartsWith("T_Smoke");
            var hints = AlphaHintsForAsset(path);
            bool isCutout = LegacyCutout(file) || file.EndsWith("_Cut") || hints.cutout.Contains(fileWithExt);
            bool isBlend = !isCutout && (isSmoke || hints.blend.Contains(fileWithExt));

            ti.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = !(isNormal || isLut || isMask || isDetail || isNoise);
            ti.isReadable = false;

            if (isLut)
            {
                ti.mipmapEnabled = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Bilinear;
                ti.anisoLevel = 0;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.maxTextureSize = 4096;
                ti.alphaSource = TextureImporterAlphaSource.None;
                return;
            }

            ti.mipmapEnabled = true;
            ti.filterMode = FilterMode.Trilinear;
            ti.wrapMode = isMacro || isSmoke ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            ti.anisoLevel = isMacro || isDetail || file.StartsWith("T_Rock") ? 8 : 4;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            if (isPsx)
            {
                // PSX textures (small, palettised, Bayer-dithered): Point filtering, mipmaps on (distant surfaces do not
                // shimmer), no anisotropy, no compression (block compression smears the palette and the dither), no
                // power-of-two rescale (sign atlases keep their exact texels). Masks stay linear with their alpha (smoothness).
                ti.filterMode = FilterMode.Point;
                ti.mipmapEnabled = true;
                ti.anisoLevel = 0;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.npotScale = TextureImporterNPOTScale.None;
            }
            if (bigMacro) ti.maxTextureSize = 8192;
            else if (file.StartsWith("T_TerrainMacro")) ti.maxTextureSize = 4096;
            else ti.maxTextureSize = 2048;

            if (isCutout)
            {
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = true;
                ti.mipMapsPreserveCoverage = true;
                ti.alphaTestReferenceValue = 0.42f;
            }
            else if (isBlend)
            {
                // smoke puffs, alpha-blended surfaces: keep the alpha, no coverage rescaling
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = true;
                ti.mipMapsPreserveCoverage = false;
            }
            else if (!isMask)
            {
                ti.alphaSource = TextureImporterAlphaSource.None;
            }
        }

        // ------------------------------------------------------------------ models
        public static void ConfigureModel(ModelImporter mi, string path) => ConfigureModel(mi, path, RulesForAsset(path));

        public static void ConfigureModel(ModelImporter mi, string path, SkyIslandModelRules rules)
        {
            string file = Path.GetFileNameWithoutExtension(path);
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importVisibility = false;
            mi.importBlendShapes = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importAnimation = false;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.isReadable = false;
            mi.optimizeMeshPolygons = true;
            mi.optimizeMeshVertices = true;
            mi.indexFormat = ModelImporterIndexFormat.Auto;
            mi.generateSecondaryUV = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            // vegetation, grass and no-collider models: no MeshColliders (trees get a capsule in their prefab)
            mi.addCollider = (rules ?? SkyIslandModelRules.Default).ModelGetsCollider(file);
        }

        /// <summary>Re-applies settings to every package texture/model; reimports only what changed.</summary>
        public static int FixAll(string root)
        {
            int n = 0;
            if (AssetDatabase.IsValidFolder(root + "/Textures"))
                foreach (var guid in AssetDatabase.FindAssets("t:Texture", new[] { root + "/Textures" }))
                {
                    var p = AssetDatabase.GUIDToAssetPath(guid);
                    if (!(AssetImporter.GetAtPath(p) is TextureImporter ti)) continue;
                    string before = EditorJsonUtility.ToJson(ti);
                    ConfigureTexture(ti, p);
                    if (EditorJsonUtility.ToJson(ti) != before) { ti.SaveAndReimport(); n++; }
                }
            if (AssetDatabase.IsValidFolder(root + "/Models"))
                foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { root + "/Models" }))
                {
                    var p = AssetDatabase.GUIDToAssetPath(guid);
                    if (!(AssetImporter.GetAtPath(p) is ModelImporter mi)) continue;
                    string before = EditorJsonUtility.ToJson(mi);
                    ConfigureModel(mi, p);
                    if (EditorJsonUtility.ToJson(mi) != before) { mi.SaveAndReimport(); n++; }
                }
            return n;
        }
    }

    class SkyIslandAssetPostprocessor : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (SkyIslandImport.IsPackageAsset(assetPath, "/Textures/") && assetImporter is TextureImporter ti)
                SkyIslandImport.ConfigureTexture(ti, assetPath);
        }

        void OnPreprocessModel()
        {
            if (SkyIslandImport.IsPackageAsset(assetPath, "/Models/") && assetImporter is ModelImporter mi)
            {
                // only on first import: keep any manual changes (remaps are set by the builder)
                if (mi.importSettingsMissing) SkyIslandImport.ConfigureModel(mi, assetPath);
            }
        }
    }
}
