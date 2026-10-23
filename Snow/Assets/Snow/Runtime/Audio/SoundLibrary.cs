using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Snow.Audio
{
    public enum SoundBus : byte { Sfx, Ambience, Ui, Music }

    /// <summary>
    /// One sound of the game with its variations (RESEARCH_AUDIO §4.5): a clip is picked from a shuffled bag (no repeat
    /// until all played, never the same twice in a row), volume and pitch are random in their ranges.
    /// </summary>
    [Serializable]
    public class SoundCue
    {
        public string id;
        public AudioClip[] clips = new AudioClip[0];
        [Tooltip("Random volume between x and y")] public Vector2 volume = new Vector2(0.8f, 1f);
        [Tooltip("Random pitch between x and y")] public Vector2 pitch = new Vector2(0.95f, 1.05f);
        [Range(0f, 1f)] public float spatial = 1f;
        public float minDistance = 1.5f, maxDistance = 30f;
        public SoundBus bus;
        [Range(0, 255)] public int priority = 128;
        [Tooltip("m: creatures hear it (server, NoiseEvents); 0 = silent for them")] public float noise;

        [NonSerialized] int[] _bag;
        [NonSerialized] int _bagPos;
        [NonSerialized] int _last = -1;

        public float Volume => Random.Range(volume.x, volume.y);
        public float Pitch => Random.Range(pitch.x, pitch.y);

        public AudioClip Pick()
        {
            if (clips == null || clips.Length == 0) return null;
            if (clips.Length == 1) return clips[0];
            if (_bag == null || _bag.Length != clips.Length || _bagPos >= _bag.Length)
            {
                if (_bag == null || _bag.Length != clips.Length)
                {
                    _bag = new int[clips.Length];
                    for (int i = 0; i < _bag.Length; i++) _bag[i] = i;
                }
                for (int i = _bag.Length - 1; i > 0; i--)
                {
                    int j = Random.Range(0, i + 1);
                    (_bag[i], _bag[j]) = (_bag[j], _bag[i]);
                }
                if (_bag[0] == _last) (_bag[0], _bag[_bag.Length - 1]) = (_bag[_bag.Length - 1], _bag[0]);
                _bagPos = 0;
            }
            _last = _bag[_bagPos++];
            return clips[_last];
        }
    }

    [Serializable]
    public class SurfaceSteps
    {
        public string surface;
        [Tooltip("Cue ids; an empty run uses walk, an empty land uses the default land")] public string walk, run, land;
    }

    [Serializable]
    public class LayerSurface
    {
        [Tooltip("Terrain layer asset name (Forest: ForestFloor, RoadGravel …)")] public string layer;
        public string surface;
    }

    [Serializable]
    public class KeywordSurface
    {
        [Tooltip("Lower-case part of a material, collider or object name")] public string keyword;
        public string surface;
    }

    /// <summary>
    /// The game's sounds (Resources/Audio/SnowSoundLibrary): the cues, and what the feet sound like on which ground.
    /// Built and updated by Snow/Audio/Rebuild Sound Library from tools/audio (sound_cues.json); values tweaked here in
    /// the inspector stay when it rebuilds.
    /// </summary>
    [CreateAssetMenu(menuName = "Snow/Sound Library")]
    public class SoundLibrary : ScriptableObject
    {
        public const string ResourcePath = "Audio/SnowSoundLibrary";

        public List<SoundCue> cues = new List<SoundCue>();
        [Header("Footsteps")]
        public List<SurfaceSteps> steps = new List<SurfaceSteps>();
        public List<LayerSurface> terrainLayers = new List<LayerSurface>();
        [Tooltip("Checked in order against the ground's SurfaceTag, material, collider and object names")]
        public List<KeywordSurface> keywords = new List<KeywordSurface>();
        [Tooltip("Ground with no match: under the open sky / under a roof")] public string outdoorDefault = "Dirt", indoorDefault = "Tile";
        public string defaultLand = "step.dirt.land";

        Dictionary<string, SoundCue> _byId;
        Dictionary<string, SurfaceSteps> _steps;
        Dictionary<string, string> _layers;

        static SoundLibrary s_instance;
        static bool s_loaded;

        public static SoundLibrary Instance
        {
            get
            {
                if (!s_loaded) { s_instance = Resources.Load<SoundLibrary>(ResourcePath); s_loaded = true; }
                return s_instance;
            }
        }

        void OnValidate() { _byId = null; _steps = null; _layers = null; }

        public SoundCue Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_byId == null)
            {
                _byId = new Dictionary<string, SoundCue>();
                foreach (SoundCue c in cues) if (c != null && !string.IsNullOrEmpty(c.id)) _byId[c.id] = c;
            }
            return _byId.TryGetValue(id, out SoundCue cue) ? cue : null;
        }

        public SurfaceSteps Steps(string surface)
        {
            if (_steps == null)
            {
                _steps = new Dictionary<string, SurfaceSteps>();
                foreach (SurfaceSteps s in steps) if (s != null && !string.IsNullOrEmpty(s.surface)) _steps[s.surface] = s;
            }
            return surface != null && _steps.TryGetValue(surface, out SurfaceSteps st) ? st : null;
        }

        public string ForLayer(string layerName)
        {
            if (_layers == null)
            {
                _layers = new Dictionary<string, string>();
                foreach (LayerSurface l in terrainLayers) if (l != null && !string.IsNullOrEmpty(l.layer)) _layers[l.layer] = l.surface;
            }
            return layerName != null && _layers.TryGetValue(layerName, out string s) ? s : null;
        }

        public string ForName(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            text = text.ToLowerInvariant();
            foreach (KeywordSurface k in keywords)
                if (k != null && !string.IsNullOrEmpty(k.keyword) && text.Contains(k.keyword)) return k.surface;
            return null;
        }
    }
}
