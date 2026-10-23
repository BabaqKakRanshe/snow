using UnityEngine;

namespace Snow.Character
{
    /// <summary>
    /// The player's camera options (user answers 23.10, docs/gdd/QUESTIONS_5.md 25–41): field of view, mouse, how much the
    /// camera moves by itself. Kept per player in PlayerPrefs. There is no settings menu yet: the dev pult page «Камера»
    /// shows them, the future menu binds to the same properties. Zero switches an effect off.
    /// </summary>
    public static class CameraSettings
    {
        const string Prefix = "snow.cam.";

        /// <summary>Horizontal field of view, degrees (70–110, 90 by default).</summary>
        public static float Fov { get => Get("fov", 90f); set => Set("fov", Mathf.Clamp(value, 70f, 110f)); }
        /// <summary>Degrees per mouse count.</summary>
        public static float Sensitivity { get => Get("sens", 0.07f); set => Set("sens", Mathf.Clamp(value, 0.01f, 0.3f)); }
        public static bool InvertY { get => Get("invy", 0f) > 0.5f; set => Set("invy", value ? 1f : 0f); }
        /// <summary>0–1: mouse smoothing (user 23.10: on by default). 1 = a 60 ms lag.</summary>
        public static float MouseSmoothing { get => Get("smooth", 0.35f); set => Set("smooth", Mathf.Clamp01(value)); }
        /// <summary>0–1: motion blur strength (user 23.10: on by default).</summary>
        public static float MotionBlur { get => Get("blur", 0.35f); set => Set("blur", Mathf.Clamp01(value)); }

        /// <summary>0–1: one knob over every camera movement below (bob, tilt, breathing, landing, car head).</summary>
        public static float Motion { get => Get("motion", 1f); set => Set("motion", Mathf.Clamp01(value)); }
        /// <summary>0–2: head bob while walking and breathing while standing (as VotV "Headbob strength").</summary>
        public static float Bob { get => Get("bob", 1f); set => Set("bob", Mathf.Clamp(value, 0f, 2f)); }
        /// <summary>0–2: camera roll when stepping sideways (as VotV "Headbob tilt").</summary>
        public static float Tilt { get => Get("tilt", 1f); set => Set("tilt", Mathf.Clamp(value, 0f, 2f)); }
        /// <summary>0–2: camera shake (monster, hits, landings, vehicle jolts).</summary>
        public static float Shake { get => Get("shake", 1f); set => Set("shake", Mathf.Clamp(value, 0f, 2f)); }
        /// <summary>Degrees added to the field of view at a sprint (user 23.10: yes). 0 = off.</summary>
        public static float SprintFov { get => Get("sprintfov", 6f); set => Set("sprintfov", Mathf.Clamp(value, 0f, 15f)); }
        /// <summary>Horizontal field of view with the cursor in a seat (Tab), degrees: as VotV "Panel FOV".</summary>
        public static float PanelFov { get => Get("panelfov", 65f); set => Set("panelfov", Mathf.Clamp(value, 40f, 110f)); }
        /// <summary>0–2: the head lags behind the body of a vehicle on bumps.</summary>
        public static float CarHead { get => Get("carhead", 1f); set => Set("carhead", Mathf.Clamp(value, 0f, 2f)); }

        public static void ResetAll()
        {
            foreach (string k in new[] { "fov", "sens", "invy", "smooth", "blur", "motion", "bob", "tilt", "shake", "sprintfov", "panelfov", "carhead" })
                PlayerPrefs.DeleteKey(Prefix + k);
            PlayerPrefs.Save();
        }

        static bool s_dirty;

        /// <summary>Writes changed values to disk (the setters only keep them: a slider calls them every frame of a drag).
        /// Called when the dev pult closes; Unity also saves PlayerPrefs on quit.</summary>
        public static void Save()
        {
            if (!s_dirty) return;
            s_dirty = false;
            PlayerPrefs.Save();
        }

        static float Get(string key, float def) => PlayerPrefs.GetFloat(Prefix + key, def);

        static void Set(string key, float v)
        {
            if (Mathf.Approximately(PlayerPrefs.GetFloat(Prefix + key, float.NaN), v)) return;
            PlayerPrefs.SetFloat(Prefix + key, v);
            s_dirty = true;
        }
    }
}
