using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Snow.Audio;
using UnityEditor;
using UnityEngine;

namespace Snow.EditorTools
{
    /// <summary>
    /// Snow/Audio/Rebuild Sound Library: reads Assets/Snow/Audio/sound_cues.json (tools/audio/build_audio.py --install)
    /// into Resources/Audio/SnowSoundLibrary. A new cue takes the manifest's settings; an existing one only gets its
    /// clips replaced, so volumes and distances tweaked in the inspector stay. Footstep tables are filled when empty.
    /// From the CLI: <c>return Snow.EditorTools.SoundLibraryBuildStep.Run();</c>
    /// </summary>
    public static class SoundLibraryBuildStep
    {
        const string Json = "Assets/Snow/Audio/sound_cues.json";
        const string AssetPath = "Assets/Snow/Audio/Resources/Audio/SnowSoundLibrary.asset";

        [Serializable] class CueSet { public float[] volume, pitch; public float minDistance = -1f, maxDistance = -1f, noise = -1f; public string bus = ""; }
        [Serializable] class CueJson { public string id, type; public string[] clips; public CueSet set; }
        [Serializable] class CuesJson { public CueJson[] cues; }

        /// <summary>Cues made in the editor from the clips of others (the forest floor is leaves, earth and some grass).</summary>
        static readonly (string id, string[] from, int perSource)[] Derived =
        {
            ("step.forest.walk", new[] { "step.leaves.walk", "step.dirt.walk", "step.grass.walk" }, 10),
            ("step.forest.run", new[] { "step.dirt.run" }, 99),
            ("step.forest.land", new[] { "step.grass.land", "step.dirt.land" }, 99),
        };

        [MenuItem("Snow/Audio/Rebuild Sound Library")]
        public static void Menu() => Debug.Log(Run());

        public static string Run()
        {
            if (!File.Exists(Json)) return "no " + Json + " — run python tools/audio/build_audio.py --install";
            AssetDatabase.Refresh();
            CuesJson data = JsonUtility.FromJson<CuesJson>(File.ReadAllText(Json));
            SoundLibrary lib = AssetDatabase.LoadAssetAtPath<SoundLibrary>(AssetPath);
            if (lib == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
                lib = ScriptableObject.CreateInstance<SoundLibrary>();
                AssetDatabase.CreateAsset(lib, AssetPath);
            }
            int created = 0, updated = 0, missing = 0;
            foreach (CueJson c in data.cues ?? new CueJson[0])
            {
                AudioClip[] clips = (c.clips ?? new string[0]).Select(AssetDatabase.LoadAssetAtPath<AudioClip>).Where(a => a != null).ToArray();
                missing += (c.clips?.Length ?? 0) - clips.Length;
                SoundCue cue = lib.cues.FirstOrDefault(x => x.id == c.id);
                if (cue == null) { cue = New(c); lib.cues.Add(cue); created++; }
                else updated++;
                cue.clips = clips;
            }
            foreach ((string id, string[] from, int per) in Derived)
            {
                var clips = new List<AudioClip>();
                foreach (string f in from)
                {
                    SoundCue src = lib.cues.FirstOrDefault(x => x.id == f);
                    if (src != null) clips.AddRange(src.clips.Take(per));
                }
                SoundCue cue = lib.cues.FirstOrDefault(x => x.id == id);
                if (cue == null)
                {
                    SoundCue like = lib.cues.FirstOrDefault(x => x.id == from[0]);
                    cue = new SoundCue { id = id };
                    if (like != null) { cue.volume = like.volume; cue.pitch = like.pitch; cue.maxDistance = like.maxDistance; cue.minDistance = like.minDistance; }
                    lib.cues.Add(cue);
                    created++;
                }
                cue.clips = clips.ToArray();
            }
            lib.cues.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            if (lib.steps.Count == 0) lib.steps = DefaultSteps();
            if (lib.terrainLayers.Count == 0) lib.terrainLayers = DefaultLayers();
            if (lib.keywords.Count == 0) lib.keywords = DefaultKeywords();
            if (lib.outdoorDefault == "Dirt") lib.outdoorDefault = "Forest";
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            return $"SoundLibrary: {lib.cues.Count} cues ({created} new, {updated} updated), {missing} clips not found, " +
                   $"{lib.steps.Count} surfaces, {lib.terrainLayers.Count} terrain layers, {lib.keywords.Count} keywords";
        }

        static SoundCue New(CueJson c)
        {
            bool bed = c.type == "bed", loop = c.type == "loop";
            var cue = new SoundCue
            {
                id = c.id,
                spatial = bed ? 0f : 1f,
                bus = bed || c.id.StartsWith("os.", StringComparison.Ordinal) ? SoundBus.Ambience : SoundBus.Sfx,
                priority = bed ? 40 : loop ? 90 : c.id.StartsWith("step.", StringComparison.Ordinal) ? 60 : 128,
                volume = bed || loop ? new Vector2(0.8f, 0.8f) : new Vector2(0.8f, 1f),
                pitch = bed || loop ? Vector2.one : new Vector2(0.95f, 1.05f),
            };
            CueSet s = c.set;
            if (s == null) return cue;
            if (s.volume != null && s.volume.Length == 2) cue.volume = new Vector2(s.volume[0], s.volume[1]);
            if (s.pitch != null && s.pitch.Length == 2) cue.pitch = new Vector2(s.pitch[0], s.pitch[1]);
            if (s.minDistance >= 0f) cue.minDistance = s.minDistance;
            if (s.maxDistance >= 0f) cue.maxDistance = s.maxDistance;
            if (s.noise >= 0f) cue.noise = s.noise;
            if (!string.IsNullOrEmpty(s.bus) && Enum.TryParse(s.bus, out SoundBus b)) cue.bus = b;
            return cue;
        }

        static SurfaceSteps S(string surface, string walk, string run, string land) =>
            new SurfaceSteps { surface = surface, walk = walk, run = run, land = land };

        static List<SurfaceSteps> DefaultSteps() => new List<SurfaceSteps>
        {
            S("Forest", "step.forest.walk", "step.forest.run", "step.forest.land"),
            S("Snow", "step.snow.walk", "step.snow.run", "step.snow.land"),
            S("Frozen", "step.frozen.walk", "step.frozen.run", "step.frozen.land"),
            S("Gravel", "step.gravel.walk", "step.gravel.run", "step.gravel.land"),
            S("Wood", "step.wood.walk", "step.wood.run", "step.wood.land"),
            S("Tile", "step.tile.walk", "step.tile.run", "step.tile.land"),
            S("Metal", "step.metal.walk", "step.metal.run", "step.metal.land"),
            S("Dirt", "step.dirt.walk", "step.dirt.run", "step.dirt.land"),
            S("Grass", "step.grass.walk", "", "step.grass.land"),
            S("Leaves", "step.leaves.walk", "step.dirt.run", "step.grass.land"),
            S("Mud", "step.mud.walk", "step.dirt.run", "step.dirt.land"),
        };

        // Forest terrain layers (TL_*); no snow layer yet — Snow / Frozen wait for one
        static List<LayerSurface> DefaultLayers() => new List<LayerSurface>
        {
            new LayerSurface { layer = "TL_ForestFloor", surface = "Forest" },
            new LayerSurface { layer = "TL_ForestFloorDark", surface = "Forest" },
            new LayerSurface { layer = "TL_TrailSoil", surface = "Dirt" },
            new LayerSurface { layer = "TL_TrailTrack", surface = "Dirt" },
            new LayerSurface { layer = "TL_RoadRuts", surface = "Mud" },
            new LayerSurface { layer = "TL_RoadGravel", surface = "Gravel" },
            new LayerSurface { layer = "TL_Rock", surface = "Gravel" },
            new LayerSurface { layer = "TL_Asphalt", surface = "Tile" },
        };

        // order matters: the first match wins. Main building rooms (MAIN_CONTRACT: R_<Room> floors) first.
        static List<KeywordSurface> DefaultKeywords()
        {
            var k = new List<KeywordSurface>();
            void Add(string surface, params string[] words) { foreach (string w in words) k.Add(new KeywordSurface { keyword = w, surface = surface }); }
            Add("Wood", "rm_stationsleiter");
            Add("Gravel", "main_roof", "roof", "gravel", "kies", "rock");
            Add("Metal", "grating", "grate", "metal", "steel", "stahl", "blech", "container");
            Add("Wood", "board", "plank", "wood", "holz", "parkett", "dielen", "crate");
            Add("Tile", "rm_", "tile", "fliese", "linol", "concrete", "beton", "asphalt", "floor");
            Add("Snow", "snow", "schnee");
            Add("Frozen", "frozen", "glatteis");
            Add("Grass", "grass");
            Add("Mud", "mud", "schlamm");
            Add("Dirt", "dirt", "soil", "erde");
            Add("Leaves", "leaves", "laub");
            return k;
        }
    }
}
