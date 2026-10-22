using UnityEditor;

namespace Snow.EditorTools
{
    /// <summary>
    /// Forest update 6 (FOREST_U6_UNITY.md J4.1): the Apparition figure keeps its rig and clips. Every Location Builder
    /// rebuild runs the core's FixAll, which imports location models without animation; this runs after the core's
    /// postprocessor (higher order) and only for Models/Apparition.fbx: Generic rig, avatar from the model, no root motion
    /// node, the four takes as Stand / Walk / Sit / SitLook (frames as in the file, 24 fps; SitLook holds its last frame).
    /// </summary>
    class ApparitionImportRule : AssetPostprocessor
    {
        const string Path = "Assets/Locations/Forest/Models/Apparition.fbx";
        static readonly (string name, float first, float last, bool loop)[] Clips =
        {
            ("Stand", 0f, 1f, true), ("Walk", 0f, 30f, true), ("Sit", 0f, 1f, true), ("SitLook", 0f, 14f, false),
        };

        public override int GetPostprocessOrder() => 100;               // after SkyIslandAssetPostprocessor (0)

        void OnPreprocessModel()
        {
            if (assetPath != Path || !(assetImporter is ModelImporter mi)) return;
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.motionNodeName = "";
            mi.importAnimation = true;
            mi.bakeAxisConversion = false;
            var clips = new ModelImporterClipAnimation[Clips.Length];
            for (int i = 0; i < Clips.Length; i++)
                clips[i] = new ModelImporterClipAnimation
                {
                    name = Clips[i].name, takeName = "Apparition_Rig|" + Clips[i].name,
                    firstFrame = Clips[i].first, lastFrame = Clips[i].last, loopTime = Clips[i].loop, loopPose = false,
                };
            mi.clipAnimations = clips;
        }
    }
}
