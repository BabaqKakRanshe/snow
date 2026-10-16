using System.IO;
using UnityEditor;
using UnityEngine;

namespace SkyIsland.EditorTools
{
    /// <summary>Import settings for the package's textures and models (applied automatically on import).</summary>
    public static class SkyIslandImport
    {
        public static bool IsPackageAsset(string path, string sub)
        {
            path = path.Replace('\\', '/');
            return path.Contains("SkyIsland") && path.Contains(sub);
        }

        public static void ConfigureTexture(TextureImporter ti, string path)
        {
            string file = Path.GetFileNameWithoutExtension(path);
            bool isNormal = file.EndsWith("_N");
            bool isLut = file.Contains("_LUT");
            bool isMask = file.EndsWith("_M");
            bool isDetail = file.Contains("Detail_");
            bool isNoise = file == "T_SkyClouds" || file == "T_FogNoise";
            bool isMacro = file.StartsWith("T_TerrainMacro") || file.StartsWith("T_Runway_") || file.StartsWith("T_Pavement_");
            bool isCutout = file == "T_SpruceBranch_A" || file == "T_SpruceImpostor_A" || file == "T_Grass_A" || file == "T_CamoNet_A" || file == "T_HangarNumbers_A" || file == "T_ChainLink_A";

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
            ti.wrapMode = isMacro ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            ti.anisoLevel = isMacro || isDetail || file.StartsWith("T_Rock") ? 8 : 4;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            if (file.StartsWith("T_Runway_") || file.StartsWith("T_Pavement_")) ti.maxTextureSize = 8192;
            else if (file.StartsWith("T_TerrainMacro")) ti.maxTextureSize = 4096;
            else ti.maxTextureSize = 2048;

            if (isCutout)
            {
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = true;
                ti.mipMapsPreserveCoverage = true;
                ti.alphaTestReferenceValue = 0.42f;
            }
            else if (!isMask)
            {
                ti.alphaSource = TextureImporterAlphaSource.None;
            }
        }

        public static void ConfigureModel(ModelImporter mi, string path)
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
            bool noCollider = file.StartsWith("SkyIsland_Grass") || file.StartsWith("Spruce_") || file.StartsWith("Dead_") || file.StartsWith("Grass_Clump");
            mi.addCollider = !noCollider;
        }

        /// <summary>Re-applies settings to every package texture/model; reimports only what changed.</summary>
        public static int FixAll(string root)
        {
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture", new[] { root + "/Textures" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(p) is TextureImporter ti)) continue;
                string before = EditorJsonUtility.ToJson(ti);
                ConfigureTexture(ti, p);
                if (EditorJsonUtility.ToJson(ti) != before) { ti.SaveAndReimport(); n++; }
            }
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
