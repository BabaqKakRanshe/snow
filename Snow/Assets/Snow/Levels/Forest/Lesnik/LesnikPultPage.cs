#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using Snow.Dev;
using Snow.Player;
using Unity.Netcode.Components;
using UnityEngine;

namespace Snow.Levels.Forest
{
    /// <summary>
    /// The pult's «Лесник» page, the networked one (the blockout block above it is Runtime/Dev/LesnikDevPage): what he is
    /// doing and where, the clock, and host buttons to make him come, steal, peek, go home; a peek spot from where I stand.
    /// </summary>
    static class LesnikPultPage
    {
        const string Page = "Лесник";
        const string SpotsFile = "Assets/Snow/Creatures/Lesnik/Resources/LesnikSpots.json";
        static bool s_registered, s_spotUp = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Register()
        {
            if (s_registered) return;
            s_registered = true;
            DevPult.Add(Page, 42, Draw, () => ForestOpening.Instance != null && ForestOpening.Instance.IsSpawned);
        }

        static void Draw()
        {
            DevUi.Header("Лесник в игре (видят все)");
            Lesnik it = Lesnik.Instance;
            PlayerNet me = PlayerNet.Local;
            bool host = ForestOpening.Instance.IsServer;
            bool on = LesnikSpawner.Enabled;
            if (host && DevUi.Checkbox("Лесник в игре", ref on)) LesnikSpawner.Enabled = on;
            if (it == null) { DevUi.Text("Не появился"); return; }

            var clock = ForestDayNight.Instance;
            string hour = clock != null ? $"{(int)clock.Hour:00}:{(int)(clock.Hour % 1f * 60f):00}" : "—";
            DevUi.Raw($"{Loc.T("Состояние")}: {Name(it.State)}{(host && it.Doing.Length > 0 ? " — " + it.Doing : "")}");
            string where = it.Shown ? $"({it.transform.position.x:0}; {it.transform.position.y:0}; {it.transform.position.z:0})" : Loc.T("нигде (в пути)");
            string dist = it.Shown && me != null ? $", {Loc.T("до меня")} {Vector3.Distance(me.transform.position, it.transform.position):0} {Loc.T("м")}" : "";
            DevUi.Raw($"{Loc.T("Где")}: {where}{dist}");
            DevUi.Raw($"{Loc.T("Время")} {hour}, {Loc.T("ночь")} {it.Night}, {Loc.T("кража этой ночью")}: {(it.TheftDone ? Loc.T("была") : Loc.T("нет"))}, {Loc.T("несёт")}: {it.Carrying}");
            DevUi.Checkbox("Метка на экране", ref Snow.Creatures.LesnikBlockout.ShowMarker);
            if (!host) { DevUi.Note("Кнопки — только у хоста."); return; }

            bool paused = Lesnik.Paused;
            if (DevUi.Checkbox("Замереть (не думает)", ref paused)) Lesnik.Paused = paused;
            if (DevUi.Button("Ночь (23:00)", clock != null)) clock.ServerSetHour(23f);
            DevUi.Row();
            if (DevUi.Button("Утро (8:00)", clock != null)) clock.ServerSetHour(8f);
            if (DevUi.Button("Выйти к базе сейчас", LesnikNav.Ready)) it.ServerComeNow();
            DevUi.Row();
            if (DevUi.Button("Ещё одна кража")) it.ServerAllowTheft();
            DevUi.Row();
            if (DevUi.Button("Домой в пещеру")) it.ServerHome();
            if (DevUi.Button("К окну, где люди", LesnikNav.Ready)) it.ServerPeekNow();
            DevUi.Row();
            if (DevUi.Button("Перенести меня к нему", it.Shown && me != null)) Bring(me, it.transform);
            if (!LesnikNav.Ready) DevUi.Note("NavMesh ещё строится (несколько секунд после старта).");

            DevUi.Header("Точки подглядывания");
            DevUi.Raw($"{Loc.T("Точек")}: {it.Spots.Count}");
            DevUi.Checkbox("Голова вверх (окно второго этажа)", ref s_spotUp);
            if (DevUi.Button("Точка здесь: встать у окна лицом к нему", me != null)) AddSpot(it, me);
            DevUi.Note("Встаньте снаружи в 3 м от окна, лицом к нему. Окно смотрит в комнату, где стоит голова; в ней должен быть игрок.");
        }

        static string Name(LesnikState s)
        {
            switch (s)
            {
                case LesnikState.Away: return Loc.T("в пути");
                case LesnikState.Sleep: return Loc.T("спит");
                case LesnikState.Walk: return Loc.T("бродит");
                case LesnikState.Watch: return Loc.T("подглядывает");
                case LesnikState.Peek: return Loc.T("у окна");
                case LesnikState.Sneak: return Loc.T("крадётся");
                case LesnikState.Grab: return Loc.T("берёт");
                case LesnikState.Leave: return Loc.T("уносит");
                case LesnikState.Flee: return Loc.T("убегает");
                default: return s.ToString();
            }
        }

        static void Bring(PlayerNet me, Transform t)
        {
            Vector3 feet = t.position + t.forward * 16f;                   // outside R6, or he runs
            if (Physics.Raycast(feet + Vector3.up * 30f, Vector3.down, out RaycastHit hit, 60f, ~(1 << LayerMask.NameToLayer("Interact")), QueryTriggerInteraction.Ignore))
                feet = hit.point;
            Vector3 at = t.position + Vector3.up * 2f;
            Vector3 d = at - feet;
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            me.motor.Teleport(feet, yaw, Vector3.zero);
            var nt = me.GetComponent<NetworkTransform>();
            if (nt != null && nt.CanCommitToTransform) nt.Teleport(feet, Quaternion.Euler(0f, yaw, 0f), me.transform.localScale);
            if (me.view != null) { me.view.SetYaw(yaw); me.view.LookAt(at); }
        }

        // a peek spot from where I stand: the rooms are those round the point his head will be at
        static void AddSpot(Lesnik it, PlayerNet me)
        {
            Transform look = me.view != null && me.view.cam != null ? me.view.cam.transform : me.transform;
            Vector3 f = look.forward;
            f.y = 0f;
            f = f.sqrMagnitude > 1e-4f ? f.normalized : me.transform.forward;
            Vector3 feet = me.transform.position;
            Vector3 head = feet + f * (s_spotUp ? 2.95f : 3.64f) + Vector3.up * (s_spotUp ? 4.63f : 1.4f) + f * 2f;
            var rooms = new List<string>();
            var opening = ForestOpening.Instance;
            var data = opening != null ? ForestData.Read<OpeningData>(opening.opening) : null;
            if (data?.rooms != null)
                foreach (ForestOrientedRoom r in data.rooms)
                {
                    Vector3 l = Quaternion.Inverse(ForestData.Q(r.q)) * (head - ForestData.V3(r.p)), half = ForestData.V3(r.size) * 0.5f + Vector3.one;
                    if (Mathf.Abs(l.x) <= half.x && Mathf.Abs(l.y) <= half.y && Mathf.Abs(l.z) <= half.z) rooms.Add(r.name);
                }
            var spots = new List<LesnikSpot>(it.Spots);
            spots.Add(new LesnikSpot
            {
                name = (rooms.Count > 0 ? rooms[0] : "spot") + " " + (spots.Count + 1),
                p = new[] { Mathf.Round(feet.x * 100f) / 100f, Mathf.Round(feet.y * 100f) / 100f, Mathf.Round(feet.z * 100f) / 100f },
                yaw = Mathf.Round(Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg),
                up = s_spotUp,
                rooms = rooms.ToArray(),
            });
            it.ServerSetSpots(spots.ToArray());
#if UNITY_EDITOR
            System.IO.File.WriteAllText(SpotsFile, JsonUtility.ToJson(new LesnikSpotList { spots = spots.ToArray() }, true));
            Debug.Log($"[Snow] Лесник: точка подглядывания {spots[spots.Count - 1].name} ({string.Join(", ", rooms)}) → {SpotsFile}");
#endif
        }
    }
}
#endif
