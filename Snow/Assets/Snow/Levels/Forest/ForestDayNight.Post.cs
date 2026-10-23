using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Snow.Levels.Forest
{
    /// <summary>
    /// The clock's own post Volume (22.10, created in Play, never saved): exposure over the hour (night <see cref="nightExposure"/>,
    /// day the day grade's value) plus the eyes' adaptation at night, and the black crush at night (LiftGammaGain lift below
    /// zero after exposure: values under it become true black, so raising the monitor's or the driver's gamma reveals nothing;
    /// the anti-gamma idea from Bohemia's DayZ tracker). Priority 13, above the day grade (12), ForestGrade (11) and the
    /// core's look (10); only exposure and lift are overridden. The local night brightness setting (streamers) eases the crush.
    /// </summary>
    public partial class ForestDayNight
    {
        Volume _post;
        VolumeProfile _postProfile;
        ColorAdjustments _postAdj;
        LiftGammaGain _postLift;
        float _dayExposure;
        ForestEyeAdaptation _eyes;

        /// <summary>Post exposure now, EV (for the cheat menu and the bench).</summary>
        public float Exposure => _postAdj != null ? _postAdj.postExposure.value : 0f;

        void UpdatePost(float day, float night, float bright)
        {
            if (_post == null) CreatePost();
            float adapt = _eyes != null ? _eyes.Offset * night : 0f;
            float exposure = Mathf.Lerp(nightExposure, _dayExposure, day) + adapt;
            _postAdj.postExposure.value = exposure;
            if (_eyes != null) _eyes.appliedExposure = exposure;
            _postLift.lift.value = new Vector4(1f, 1f, 1f, -nightCrush * night * (1f - bright));
        }

        void CreatePost()
        {
            // by day the day grade's exposure (ForestDayNightStep writes 0.35 there)
            _dayExposure = dayVolume != null && dayVolume.sharedProfile != null && dayVolume.sharedProfile.TryGet(out ColorAdjustments a) && a.postExposure.overrideState
                ? a.postExposure.value : 0.35f;
            var go = new GameObject("ForestNightPost (runtime)") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            go.layer = 0;                                              // the player camera's volume mask is Default
            _postProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            _postProfile.name = "ForestNightPost";
            _postProfile.hideFlags = HideFlags.DontSave;
            _postAdj = _postProfile.Add<ColorAdjustments>(false);
            _postAdj.postExposure.overrideState = true;
            _postLift = _postProfile.Add<LiftGammaGain>(false);
            _postLift.lift.overrideState = true;
            _post = go.AddComponent<Volume>();
            _post.isGlobal = true;
            _post.priority = 13f;
            _post.weight = 1f;
            _post.sharedProfile = _postProfile;
        }

        void DestroyPost()
        {
            if (_post != null) Destroy(_post.gameObject);
            if (_postProfile != null) Destroy(_postProfile);
            _post = null;
            _postProfile = null;
            _postAdj = null;
            _postLift = null;
        }
    }
}
