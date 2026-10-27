#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Snow.Creatures;
using Snow.Player;
using Unity.Netcode.Components;
using UnityEngine;

namespace Snow.Dev
{
    /// <summary>
    /// The pult's «Лесник» page (user 26.10: "a debug panel for him so I don't walk around looking for him"): put the
    /// blockout in front of me or at a known spot, bring me to it, its neck (Stand ↔ NeckReach), a marker on screen with the
    /// distance. On this peer only - the others don't see it; there is no behaviour yet (docs/gdd/monsters/02_lesnik).
    /// </summary>
    static class LesnikDevPage
    {
        const string Page = "Лесник";
        const int Order = 42;

        static readonly (string name, Vector3 feet, float yaw)[] Spots =
        {
            ("Кухня (кадр концепта)", new Vector3(337.2f, 20.15f, -354.7f), 180f),
        };

        static LesnikBlockout s_it;
        static bool s_registered;
        static float s_dist = 4f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Register()
        {
            if (s_registered) return;
            s_registered = true;
            DevPult.Add(Page, Order, Draw);
        }

        static void Draw()
        {
            DevUi.Header("Лесник — блокинг");
            DevUi.Note("Только на этом компьютере: другие игроки его не видят. Поведения пока нет.");
            PlayerNet me = PlayerNet.Local;
            if (s_it == null) DevUi.Text("Не поставлен");
            else
            {
                Vector3 p = s_it.transform.position;
                string d = me != null ? $", {Loc.T("до меня")} {Vector3.Distance(me.transform.position, p):0.0} {Loc.T("м")}" : "";
                DevUi.Raw($"{Loc.T("Стоит")} ({p.x:0.0}; {p.y:0.0}; {p.z:0.0}){d}");
            }

            DevUi.Slider("Расстояние, м", ref s_dist, 2f, 30f, "%.0f");
            if (DevUi.Button("Поставить передо мной", me != null)) Place(Front(me, s_dist), me);
            DevUi.Row();
            if (DevUi.Button("На 15 м (граница страха)", me != null)) Place(Front(me, 15f), me);
            foreach (var s in Spots)
                if (DevUi.Button(s.name)) Place(s.feet, null, s.yaw);
            if (DevUi.Button("Перенести меня к нему", s_it != null && me != null)) Bring(me);
            DevUi.Row();
            if (DevUi.Button("Повернуть ко мне", s_it != null && me != null)) Face(me.transform.position);
            DevUi.Row();
            if (DevUi.Button("Убрать", s_it != null)) { Object.Destroy(s_it.gameObject); s_it = null; }
            DevUi.Checkbox("Метка на экране", ref LesnikBlockout.ShowMarker);

            DevUi.Header("Шея");
            if (s_it == null) { DevUi.Note("Сначала поставьте его"); return; }
            float r = s_it.reach;
            if (DevUi.Slider("Вытянута", ref r, 0f, 1f)) s_it.SetReach(r);
            if (DevUi.Button("Тянуть голову")) s_it.ReachTo(1f);
            DevUi.Row();
            if (DevUi.Button("Обратно")) s_it.ReachTo(0f);
            DevUi.Note("0 — стоит (голова висит вверх ногами), 1 — шея 3,5 м, голова у лица игрока в 3,6 м от тела.");

            DevUi.Header("Клипы");
            DevUi.Raw($"{Loc.T("Сейчас")}: {(s_it.Playing != null ? s_it.Playing.name : Loc.T("поза шеи"))}");
            if (s_it.clips == null || s_it.clips.Length == 0) { DevUi.Note("Клипов нет в префабе"); return; }
            for (int i = 0; i < s_it.clips.Length; i++)
            {
                var c = s_it.clips[i];
                if (c == null) continue;
                if (i > 0) DevUi.Row();
                if (DevUi.Button(ClipLabel(c.name) + "##" + c.name)) s_it.Play(c.name);
            }
            if (DevUi.Button("Стоп (поза шеи)")) s_it.Play(null);
            DevUi.Note("Клипы на месте: тело не идёт по полу, ноги могут скользить — это блокинг.");
        }

        static string ClipLabel(string clip)
        {
            switch (clip)
            {
                case "Shuffle": return "Шаркает";
                case "Grab": return "Берёт";
                case "Carry": return "Несёт";
                case "Cower": return "Закрывается";
                case "Flee": return "Убегает";
                default: return clip;
            }
        }

        static Vector3 Front(PlayerNet me, float dist)
        {
            Transform look = me.view != null && me.view.cam != null ? me.view.cam.transform : me.transform;
            Vector3 f = look.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) f = me.transform.forward;
            return me.transform.position + f.normalized * dist;
        }

        static Vector3 Ground(Vector3 p)
        {
            int mask = ~(1 << LayerMask.NameToLayer("Interact"));
            return Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 30f, mask, QueryTriggerInteraction.Ignore)
                ? hit.point : p;
        }

        static void Place(Vector3 feet, PlayerNet faceMe, float yaw = 0f)
        {
            if (s_it == null)
            {
                var prefab = Resources.Load<GameObject>("LesnikBlockout");
                if (prefab == null) { Debug.LogWarning("[Snow] Лесник: Resources/LesnikBlockout не найден."); return; }
                s_it = Object.Instantiate(prefab).GetComponent<LesnikBlockout>();
                s_it.name = "Lesnik (blockout, pult)";
            }
            s_it.transform.position = Ground(feet);
            s_it.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (faceMe != null) Face(faceMe.transform.position);
        }

        static void Face(Vector3 target)
        {
            Vector3 d = target - s_it.transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > 1e-4f) s_it.transform.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
        }

        /// <summary>Me 5 m in front of it, looking at its head.</summary>
        static void Bring(PlayerNet me)
        {
            Transform t = s_it.transform;
            Vector3 feet = Ground(t.position + t.forward * 5f);
            Vector3 at = s_it.head != null ? s_it.head.position : t.position + Vector3.up * 2f;
            Vector3 d = at - feet;
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            me.motor.Teleport(feet, yaw, Vector3.zero);
            var nt = me.GetComponent<NetworkTransform>();
            if (nt != null && nt.CanCommitToTransform) nt.Teleport(feet, Quaternion.Euler(0f, yaw, 0f), me.transform.localScale);
            if (me.view != null) { me.view.SetYaw(yaw); me.view.LookAt(at); }
        }
    }
}
#endif
