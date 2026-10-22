using System;
using System.Linq;
using UnityEditor;

namespace Snow.EditorTools
{
    /// <summary>
    /// Forest update 7 (FOREST_U7_UNITY.md N4.2): the 13 netting / wire cut-outs keep their wires in the mips at the
    /// materials' own alpha threshold (0.5). The core's import rule sets 0.42 for every cut-out; this runs after it
    /// (higher postprocess order) and only for these paths, so the core and the other locations are untouched.
    /// </summary>
    class FenceCutoutImportRule : AssetPostprocessor
    {
        const string Folder = "Assets/Locations/Forest/Textures/";
        static readonly string[] Names =
        {
            "T_Base_ChainLink_A_px", "T_Base_Barbed_A_px", "T_Base_Rust_A_px", "T_Kpp_ChainLink_A_px", "T_Kpp_Barbed_A_px",
            "T_Yard_ChainLink_A_px", "T_Yard_Barbed_A_px", "T_Road_ChainLink_A_px", "T_Road_Barbed_A_px", "T_Fence_ChainLink_A_px",
            "T_Fence_Barbed_A_px", "T_Fence_ChainBarbed_A_px", "T_Fence_Razor_A_px",
        };
        const float Cutoff = 0.5f;                                      // = the materials' alpha clip threshold

        public override int GetPostprocessOrder() => 100;               // after SkyIslandAssetPostprocessor (0)

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder, StringComparison.Ordinal) || !(assetImporter is TextureImporter ti)) return;
            string name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            if (!Names.Contains(name)) return;
            ti.mipMapsPreserveCoverage = true;
            ti.alphaTestReferenceValue = Cutoff;
        }
    }
}
