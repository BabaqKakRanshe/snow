#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Reflection;
using Snow.Character;
using Snow.Player;
using Snow.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Snow.Dev
{
    /// <summary>
    /// The pult's «Эффекты» page (user 23.10: every setting of the realism effects on ~): camera shake and its sources,
    /// the flashlight beam, the frost on the screen edges, SSAO. Values change live on this peer and last until the game
    /// stops (statics; SSAO is put back into PC_Renderer.asset on quit, so the editor never saves a pult value). Good numbers
    /// go into the code defaults (or PC_Renderer) by hand.
    /// </summary>
    static class CameraFxDevPage
    {
        const string Page = "Эффекты";
        const int Order = 6;

        static bool s_registered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Register()
        {
            if (s_registered) return;
            s_registered = true;
            DevPult.Add(Page, Order, DrawShake);
            DevPult.Add(Page, Order, DrawBeam);
            DevPult.Add(Page, Order, DrawFrost);
            DevPult.Add(Page, Order, DrawSsao);
        }

        // ------------------------------------------------------------------ shake
        static void DrawShake()
        {
            DevUi.Header("Тряска камеры");
            DevUi.Checkbox("Включена##shake", ref CameraShake.Enabled);
            DevUi.Row();
            if (DevUi.Button("Слабо")) CameraShake.Add(0.3f);
            DevUi.Row();
            if (DevUi.Button("Средне")) CameraShake.Add(0.6f);
            DevUi.Row();
            if (DevUi.Button("Сильно")) CameraShake.Add(1f);
            DevUi.Raw($"{Loc.T("травма")} {CameraShake.Trauma:0.00}, {Loc.T("сила из настроек камеры")} ×{CameraSettings.Shake:0.00}");
            DevUi.Slider("Кивок, °", ref CameraShake.MaxAngles.x, 0f, 15f, "%.1f");
            DevUi.Slider("Поворот, °", ref CameraShake.MaxAngles.y, 0f, 15f, "%.1f");
            DevUi.Slider("Крен, °", ref CameraShake.MaxAngles.z, 0f, 20f, "%.1f");
            DevUi.Slider("Затухание, 1/с", ref CameraShake.Decay, 0.1f, 5f);
            DevUi.Slider("Частота", ref CameraShake.Frequency, 1f, 40f, "%.1f");

            FirstPersonView view = PlayerNet.Local != null ? PlayerNet.Local.view : null;
            if (view != null)
            {
                DevUi.Text("Источники (свой игрок)");
                DevUi.Slider("Приземление: с, м/с", ref view.landingShakeFrom, 1f, 15f, "%.1f");
                DevUi.Slider("Приземление: максимум, м/с", ref view.landingShakeFull, 2f, 25f, "%.1f");
                DevUi.Slider("Толчок кресла: порог, м/с²", ref view.joltFrom, 1f, 50f, "%.0f");
                DevUi.Slider("Толчок кресла: сила", ref view.joltShake, 0f, 0.3f, "%.3f");
            }
            else DevUi.Note("Нет своего игрока: источники тряски не показаны");
            if (DevUi.Button("Сбросить тряску")) CameraShake.ResetTuning();
        }

        // ------------------------------------------------------------------ flashlight beam
        static void DrawBeam()
        {
            DevUi.Header("Луч фонарика в тумане");
            DevUi.Checkbox("Включён##beam", ref LightBeam.Enabled);
            bool c = false;
            c |= DevUi.Slider("Яркость", ref LightBeam.Gain, 0f, 30f, "%.1f");
            c |= DevUi.Slider("Длина, м", ref LightBeam.Length, 2f, 40f, "%.0f");
            c |= DevUi.Slider("Ширина ядра, °", ref LightBeam.CoreWidth, 0.5f, 30f, "%.1f");
            c |= DevUi.Slider("Края", ref LightBeam.Skirt, 0f, 0.3f, "%.3f");
            c |= DevUi.Slider("Рассеяние вперёд", ref LightBeam.ForwardScatter, 0f, 0.9f);
            DevUi.Slider("Туман → плотность", ref LightBeam.FogScale, 0f, 150f, "%.0f");
            DevUi.Slider("Дальность показа, м", ref LightBeam.MaxDistance, 5f, 200f, "%.0f");
            if (c) LightBeam.TuningChanged();
            if (DevUi.Button("Сбросить луч")) LightBeam.ResetTuning();
        }

        // ------------------------------------------------------------------ frost
        static void DrawFrost()
        {
            DevUi.Header("Иней по краям");
            DevUi.Checkbox("Включён##frost", ref ScreenFrost.Enabled);
            DevUi.Raw($"{Loc.T("иней")} {ScreenFrost.Amount:0.00} → {ScreenFrost.Target:0.00}" + (ScreenFrost.Hold >= 0f ? " " + Loc.T("(держится)") : ""));
            float hold = ScreenFrost.Hold >= 0f ? ScreenFrost.Hold : ScreenFrost.Amount;
            if (DevUi.Slider("Держать на", ref hold, 0f, 1f)) ScreenFrost.Hold = hold;
            DevUi.Row();
            if (DevUi.Button("Как идёт")) ScreenFrost.Hold = -1f;
            DevUi.Slider("Полный иней при, °C", ref ScreenFrost.FullColdC, -60f, -1f, "%.0f");
            DevUi.Slider("Ветер до максимума, м/с", ref ScreenFrost.FullWindMs, 1f, 40f, "%.0f");
            DevUi.Slider("Доля ветра", ref ScreenFrost.WindShare, 0f, 1f);
            DevUi.Slider("Нарастает за, с", ref ScreenFrost.GrowTime, 1f, 600f, "%.0f");
            DevUi.Slider("Тает за, с", ref ScreenFrost.MeltTime, 1f, 120f, "%.0f");
            DevUi.Slider("Мягкость кромки", ref ScreenFrost.Edge, 0.01f, 0.3f, "%.3f");
            DevUi.Slider("Непрозрачность", ref ScreenFrost.Opacity, 0f, 1f);
            DevUi.Slider("Цвет: красный", ref ScreenFrost.Tint.r, 0f, 1f);
            DevUi.Slider("Цвет: зелёный", ref ScreenFrost.Tint.g, 0f, 1f);
            DevUi.Slider("Цвет: синий", ref ScreenFrost.Tint.b, 0f, 1f);
            if (DevUi.Button("Сбросить иней")) ScreenFrost.ResetTuning();
        }

        // ------------------------------------------------------------------ SSAO (URP renderer feature, settings internal)
        static ScreenSpaceAmbientOcclusion s_ssao;
        static object s_ssaoSettings;
        static FieldInfo s_fIntensity, s_fRadius, s_fDirect, s_fFalloff;
        static float[] s_ssaoSaved;
        static bool s_ssaoActiveSaved, s_ssaoTried;

        static void DrawSsao()
        {
            DevUi.Header("Затенение в щелях (SSAO)");
            if (!FindSsao()) { DevUi.Note("SSAO не найдено в рендерере"); return; }
            bool on = s_ssao.isActive;
            if (DevUi.Checkbox("Включено##ssao", ref on)) s_ssao.SetActive(on);
            SsaoSlider("Сила", s_fIntensity, 0f, 4f);
            SsaoSlider("Радиус, м", s_fRadius, 0.05f, 2f);
            SsaoSlider("Тень и под прямым светом", s_fDirect, 0f, 1f);
            SsaoSlider("Дальность, м", s_fFalloff, 5f, 500f, "%.0f");
            if (DevUi.Button("Как в PC_Renderer")) RestoreSsao();
            DevUi.Note("Действует на этом пире до выхода из игры; хорошие числа перенести в PC_Renderer.asset руками.");
        }

        static void SsaoSlider(string label, FieldInfo f, float min, float max, string format = "%.2f")
        {
            float v = (float)f.GetValue(s_ssaoSettings);
            if (DevUi.Slider(label, ref v, min, max, format)) f.SetValue(s_ssaoSettings, v);
        }

        static bool FindSsao()
        {
            if (s_ssaoTried) return s_ssao != null;
            s_ssaoTried = true;
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset asset)) return false;
            foreach (ScriptableRendererData data in asset.rendererDataList)
            {
                if (data == null) continue;
                foreach (ScriptableRendererFeature f in data.rendererFeatures)
                    if (f is ScreenSpaceAmbientOcclusion ao) { s_ssao = ao; break; }
                if (s_ssao != null) break;
            }
            if (s_ssao == null) return false;
            const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            s_ssaoSettings = typeof(ScreenSpaceAmbientOcclusion).GetField("m_Settings", Any)?.GetValue(s_ssao);
            if (s_ssaoSettings == null) { s_ssao = null; return false; }
            System.Type t = s_ssaoSettings.GetType();
            s_fIntensity = t.GetField("Intensity", Any);
            s_fRadius = t.GetField("Radius", Any);
            s_fDirect = t.GetField("DirectLightingStrength", Any);
            s_fFalloff = t.GetField("Falloff", Any);
            if (s_fIntensity == null || s_fRadius == null || s_fDirect == null || s_fFalloff == null) { s_ssao = null; return false; }
            s_ssaoSaved = new[]
            {
                (float)s_fIntensity.GetValue(s_ssaoSettings), (float)s_fRadius.GetValue(s_ssaoSettings),
                (float)s_fDirect.GetValue(s_ssaoSettings), (float)s_fFalloff.GetValue(s_ssaoSettings)
            };
            s_ssaoActiveSaved = s_ssao.isActive;
            Application.quitting += RestoreSsao;                    // in the editor: leaving Play mode
            return true;
        }

        static void RestoreSsao()
        {
            if (s_ssao == null || s_ssaoSaved == null) return;
            s_fIntensity.SetValue(s_ssaoSettings, s_ssaoSaved[0]);
            s_fRadius.SetValue(s_ssaoSettings, s_ssaoSaved[1]);
            s_fDirect.SetValue(s_ssaoSettings, s_ssaoSaved[2]);
            s_fFalloff.SetValue(s_ssaoSettings, s_ssaoSaved[3]);
            s_ssao.SetActive(s_ssaoActiveSaved);
        }
    }
}
#endif
