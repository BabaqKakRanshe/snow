using System;
using UnityEditor;
using UnityEngine;

namespace Snow.EditorTools
{
    /// <summary>
    /// Import of the game's sounds (Assets/Snow/Audio/Clips, built by tools/audio/build_audio.py; RESEARCH_AUDIO §4.4,
    /// §5): 22 050 Hz (the PSX sound), Vorbis; short one-shots decompressed on load, machine loops compressed in memory,
    /// ambience beds streamed. A rule so a reimport or the core's FixAll never resets it.
    /// </summary>
    class AudioImportRule : AssetPostprocessor
    {
        const string Folder = "Assets/Snow/Audio/Clips/";

        public override int GetPostprocessOrder() => 100;

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(Folder, StringComparison.Ordinal) || !(assetImporter is AudioImporter ai)) return;
            string file = System.IO.Path.GetFileName(assetPath);
            bool bed = assetPath.StartsWith(Folder + "amb/", StringComparison.Ordinal);
            bool loop = file.Contains(".loop_");
            AudioImporterSampleSettings s = ai.defaultSampleSettings;
            s.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
            s.sampleRateOverride = 22050;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = bed ? 0.5f : loop ? 0.6f : 0.7f;
            s.loadType = bed ? AudioClipLoadType.Streaming : loop ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            s.preloadAudioData = !bed;
            ai.defaultSampleSettings = s;
            ai.forceToMono = false;                                   // build_audio.py already made one-shots mono
            ai.loadInBackground = bed;
        }
    }
}
