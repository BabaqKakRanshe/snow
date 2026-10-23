#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Snow.Dev;
using Snow.Player;
using UnityEngine;

namespace Snow.Audio
{
    /// <summary>
    /// The pult's «Звук» page: volumes per bus, what is under the own feet and why, the last sounds of this peer (missing
    /// cues too), a play button per cue. The Forest adds its ambience block to the same page (ForestAmbience).
    /// </summary>
    static class AudioDevPage
    {
        public const string Page = "Звук";
        public const int Order = 45;

        static readonly (string prefix, string label)[] Groups =
        {
            ("step.", "Шаги"), ("amb.", "Фоны"), ("os.", "Лес: мелочи"), ("forest.", "Пролог"), ("item.", "Предметы"),
        };
        static readonly string[] Buses = { "Эффекты", "Фон", "Интерфейс", "Музыка" };
        static readonly bool[] s_open = new bool[Groups.Length];
        static bool s_registered, s_mute;

        internal static void Register()
        {
            if (s_registered) return;
            s_registered = true;
            DevPult.Add(Page, Order, DrawVolumes);
            DevPult.Add(Page, Order, DrawFeet, () => PlayerNet.Local != null);
            DevPult.Add(Page, Order, DrawHistory);
            DevPult.Add(Page, Order, DrawTest, () => SnowAudio.Library != null);
        }

        static Vector3 Ear()
        {
            PlayerNet me = PlayerNet.Local;
            if (me != null && me.cam != null) return me.cam.transform.position;
            Camera c = Camera.main;
            return c != null ? c.transform.position : Vector3.zero;
        }

        static void DrawVolumes()
        {
            DevUi.Header("Громкость");
            DevUi.Slider("Общая", ref SnowAudio.Master, 0f, 1f);
            for (int i = 0; i < Buses.Length; i++) DevUi.Slider(Buses[i], ref SnowAudio.BusVolume[i], 0f, 1f);
            if (DevUi.Checkbox("Без звука", ref s_mute)) AudioListener.volume = s_mute ? 0f : 1f;
            DevUi.Text("Источников играет");
            DevUi.Row();
            DevUi.Raw(SnowAudio.Busy + " / " + SnowAudio.PoolCount);
            if (SnowAudio.Library == null) DevUi.Note("нет библиотеки звуков: Snow/Audio/Rebuild Sound Library");
        }

        static void DrawFeet()
        {
            PlayerNet me = PlayerNet.Local;
            SoundLibrary lib = SnowAudio.Library;
            DevUi.Header("Под ногами");
            Vector3 feet = me.transform.position;
            string surface = SurfaceResolver.Explain(feet, me.transform, out string why);
            DevUi.Text("Поверхность");
            DevUi.Row();
            DevUi.Raw(surface ?? "—");
            DevUi.Text("Почему");
            DevUi.Row();
            DevUi.Raw(why ?? "—");
            SurfaceSteps st = lib != null ? lib.Steps(surface) ?? lib.Steps(lib.outdoorDefault) : null;
            DevUi.Text("Шаг / бег / прыжок");
            DevUi.Row();
            DevUi.Raw(st != null ? st.walk + " / " + (string.IsNullOrEmpty(st.run) ? st.walk : st.run) + " / " + (string.IsNullOrEmpty(st.land) ? lib.defaultLand : st.land) : "—");
            DevUi.Text("Под крышей");
            DevUi.Row();
            DevUi.Text(SurfaceResolver.Indoors(feet) ? "да" : "нет");
        }

        static void DrawHistory()
        {
            DevUi.Header("Последние звуки");
            Vector3 ear = Ear();
            bool any = false;
            foreach (SnowAudio.Played p in SnowAudio.History())
            {
                any = true;
                float ago = Time.time - p.time;
                if (p.clip == null)
                    DevUi.Raw($"{ago,5:0.0} с   НЕТ ЗВУКА   {p.id}   ({p.via})");
                else
                    DevUi.Raw($"{ago,5:0.0} с   {p.id}   {Vector3.Distance(ear, p.at),3:0} м   гр. {p.volume:0.00}   {p.via}   [{p.clip}]");
            }
            if (!any) DevUi.Note("пока тихо");
        }

        static void DrawTest()
        {
            SoundLibrary lib = SnowAudio.Library;
            DevUi.Header("Проверить звук");
            DevUi.Note("играет в 2 м перед камерой, слышно только здесь");
            if (DevUi.Button("Стоп")) SnowAudio.StopAll();
            for (int g = 0; g < Groups.Length; g++)
            {
                DevUi.Checkbox(Groups[g].label + "##grp" + g, ref s_open[g]);
                if (!s_open[g]) continue;
                int k = 0;
                foreach (SoundCue cue in lib.cues)
                {
                    if (cue == null || cue.id == null || !cue.id.StartsWith(Groups[g].prefix, System.StringComparison.Ordinal)) continue;
                    if (k++ % 3 != 0) DevUi.Row();
                    string label = cue.id + " (" + (cue.clips != null ? cue.clips.Length : 0) + ")##" + cue.id;
                    if (DevUi.Button(label, cue.clips != null && cue.clips.Length > 0))
                    {
                        Transform cam = PlayerNet.Local != null && PlayerNet.Local.cam != null ? PlayerNet.Local.cam.transform : null;
                        SnowAudio.Play(cue, cam != null ? cam.position + cam.forward * 2f : Ear());
                    }
                }
            }
        }
    }
}
#endif
