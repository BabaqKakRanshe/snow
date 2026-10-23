using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Snow.EditorTools
{
    /// <summary>
    /// Forest integration, the horror grade (user decision 21.10: "слабый, не атмосферный пост"): a second global Volume on
    /// the Forest root with what the core's look schema cannot carry — desaturation, split toning (cold shadows, amber
    /// highlights) and a cold lift of the shadows. Exposure, contrast, bloom, vignette, grain and chromatic aberration stay
    /// in Forest_Look.json (the core builds the main Volume from it). Priority above the core's Volume; only the parameters
    /// below are overridden. Values TEMPORARY, from the 21.10 research (best practice for URP horror grading).
    /// </summary>
    public static class ForestGradeStep
    {
        const string ProfilePath = "Assets/Snow/Levels/Forest/Forest_HorrorGrade.asset";
        const string VolumeName = "ForestGrade";

        public static void Run(Transform root)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            var adj = Get<ColorAdjustments>(profile);
            adj.saturation.Override(-32f);
            var split = Get<SplitToning>(profile);
            split.shadows.Override(new Color(0.42f, 0.58f, 0.62f));
            split.highlights.Override(new Color(0.66f, 0.55f, 0.42f));
            split.balance.Override(-30f);
            // 22.10 («ночь слишком яркая»): the cold tint stays, the shadow lift (w 0.02) is gone - it kept the night off true black
            var smh = Get<ShadowsMidtonesHighlights>(profile);
            smh.shadows.Override(new Vector4(0.96f, 1f, 1.06f, 0f));
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            Transform t = root.Find(VolumeName);
            GameObject go = t != null ? t.gameObject : new GameObject(VolumeName);
            go.transform.SetParent(root, false);
            go.layer = 0;                                              // the player camera's volume mask is Default
            if (!go.TryGetComponent(out Volume v)) v = go.AddComponent<Volume>();
            v.isGlobal = true;
            v.priority = 11f;                                          // the core's look Volume is 10
            v.weight = 1f;
            v.sharedProfile = profile;
            EditorUtility.SetDirty(v);
            Debug.Log("[Snow] Forest grade: Volume ForestGrade (priority 11): saturation -32, split toning (cold shadows, amber highlights, balance -30), cold shadow tint (no lift).");
        }

        static T Get<T>(VolumeProfile p) where T : VolumeComponent
        {
            if (!p.TryGet(out T c))
            {
                c = p.Add<T>(false);
                c.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(c, p);
            }
            c.active = true;
            return c;
        }
    }
}
