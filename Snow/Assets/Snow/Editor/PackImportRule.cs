using System;
using UnityEditor;
using UnityEngine;

namespace Snow.EditorTools
{
    /// <summary>
    /// The sky_systems pack (Assets/SkyIsland/Systems, PackPropsBuilder): the core's import rule treats every texture whose
    /// path holds "SkyIsland" as a package texture and re-applies its own settings on each import; this runs after it (higher
    /// postprocess order) and only for the pack's textures, so they stay point-filtered, without mipmaps and uncompressed like
    /// the other packs (the tiled grating repeats).
    /// </summary>
    class PackImportRule : AssetPostprocessor
    {
        const string Folder = "Assets/SkyIsland/Systems/Textures/";

        public override int GetPostprocessOrder() => 110;               // after SkyIslandAssetPostprocessor (0)

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder, StringComparison.Ordinal) || !(assetImporter is TextureImporter ti)) return;
            ti.textureType = TextureImporterType.Default;
            ti.textureShape = TextureImporterShape.Texture2D;
            ti.filterMode = FilterMode.Point;
            ti.mipmapEnabled = false;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.sRGBTexture = true;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.alphaIsTransparency = true;
            ti.wrapMode = assetPath.Contains("Grating") ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        }
    }
}
