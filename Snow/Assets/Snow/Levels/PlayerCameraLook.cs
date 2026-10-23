using Snow.Character;
using Snow.Player;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Snow.Dev;
#endif

namespace Snow.Levels
{
    /// <summary>
    /// Camera options that need the render pipeline, and their dev pult block. Motion blur (user 23.10, QUESTIONS_5.md 31:
    /// on by default) is one global volume of its own, camera-only, strength from <see cref="CameraSettings.MotionBlur"/>.
    /// The block on the pult page «Камера» shows every <see cref="CameraSettings"/> option (there is no settings menu yet)
    /// and ragdoll test buttons. Created by itself on the first scene, survives scene loads.
    /// </summary>
    public class PlayerCameraLook : MonoBehaviour
    {
        static PlayerCameraLook s_instance;

        VolumeProfile _profile;
        MotionBlur _blur;
        Volume _volume;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        System.Action _page;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (s_instance != null) return;
            var go = new GameObject("PlayerCameraLook");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<PlayerCameraLook>();
        }

        void Awake()
        {
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _blur = _profile.Add<MotionBlur>(true);
            _blur.mode.Override(MotionBlurMode.CameraOnly);
            _blur.quality.Override(MotionBlurQuality.Low);
            _blur.clamp.Override(0.05f);
            _volume = gameObject.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 50f;
            _volume.sharedProfile = _profile;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            DevPult.Add("Камера", 5, _page = DrawPage);
#endif
        }

        void OnDestroy()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            DevPult.Remove("Камера", _page);
#endif
            if (_profile != null) Destroy(_profile);
            if (s_instance == this) s_instance = null;
        }

        void Update()
        {
            float blur = CameraSettings.MotionBlur;
            _blur.intensity.Override(blur);
            _volume.weight = blur > 0.001f ? 1f : 0f;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // ------------------------------------------------------------------ pult: options and ragdoll tests (this peer)
        void DrawPage()
        {
            DevUi.Header("Настройки камеры");
            float v;
            v = CameraSettings.Fov; if (DevUi.Slider("Обзор, °", ref v, 70f, 110f, "%.0f")) CameraSettings.Fov = v;
            v = CameraSettings.Sensitivity; if (DevUi.Slider("Мышь, °/отсчёт", ref v, 0.01f, 0.3f, "%.3f")) CameraSettings.Sensitivity = v;
            bool inv = CameraSettings.InvertY; if (DevUi.Checkbox("Инверсия Y", ref inv)) CameraSettings.InvertY = inv;
            v = CameraSettings.MouseSmoothing; if (DevUi.Slider("Сглаживание мыши", ref v, 0f, 1f)) CameraSettings.MouseSmoothing = v;
            v = CameraSettings.MotionBlur; if (DevUi.Slider("Размытие в движении", ref v, 0f, 1f)) CameraSettings.MotionBlur = v;
            v = CameraSettings.Motion; if (DevUi.Slider("Движение камеры (всё)", ref v, 0f, 1f)) CameraSettings.Motion = v;
            v = CameraSettings.Bob; if (DevUi.Slider("Покачивание и дыхание", ref v, 0f, 2f)) CameraSettings.Bob = v;
            v = CameraSettings.Tilt; if (DevUi.Slider("Наклон при шаге вбок", ref v, 0f, 2f)) CameraSettings.Tilt = v;
            v = CameraSettings.Shake; if (DevUi.Slider("Сила тряски", ref v, 0f, 2f)) CameraSettings.Shake = v;
            v = CameraSettings.SprintFov; if (DevUi.Slider("Шире на спринте, °", ref v, 0f, 15f, "%.0f")) CameraSettings.SprintFov = v;
            v = CameraSettings.PanelFov; if (DevUi.Slider("Обзор у приборов (Tab), °", ref v, 40f, 110f, "%.0f")) CameraSettings.PanelFov = v;
            v = CameraSettings.CarHead; if (DevUi.Slider("Голова в машине", ref v, 0f, 2f)) CameraSettings.CarHead = v;
            if (DevUi.Button("Сбросить настройки камеры")) CameraSettings.ResetAll();

            DevUi.Header("Тряпичная кукла");
            PlayerNet me = PlayerNet.Local;
            PlayerRagdoll r = me != null ? me.Ragdoll : null;
            if (r == null) { DevUi.Note("Нет своего игрока"); return; }
            DevUi.Raw(r.Down ? $"{Loc.T("лежит")}: {r.Cause}" : Loc.T("стоит"));
            if (DevUi.Button("Упасть", !r.Down)) r.KeyPressed();
            DevUi.Row();
            if (DevUi.Button("Удар в спину")) r.Begin(me.motor.Velocity, Quaternion.Euler(0f, me.view.Yaw, 0f) * new Vector3(0f, 2f, 6f), RagdollCause.Hit);
            DevUi.Row();
            if (DevUi.Button("Смерть")) r.Begin(me.motor.Velocity, Vector3.zero, RagdollCause.Death);
            DevUi.Row();
            if (DevUi.Button("Конец смерти", r.Down && r.Cause == RagdollCause.Death)) r.EndDeath();
            DevUi.Note("V — упасть / встать (пробел тоже встаёт). Встать можно, когда тело почти не движется.");
        }
#endif
    }
}
