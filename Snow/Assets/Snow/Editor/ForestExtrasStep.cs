using System.Linq;
using Snow.Levels.Forest;
using UnityEditor;
using UnityEngine;

namespace Snow.EditorTools
{
    /// <summary>
    /// Forest integration, the user's 21.10 additions: the day / night clock (<see cref="ForestDayNight"/>) on the Forest
    /// root next to the opening (the pockets moved into the core: CargoItem, PlayerNet). The checkpoint's power panel of
    /// 21.10 (a copy of the container's main switch by the booth door) is gone with KPP v2: the booth has its own power
    /// cabinet (ForestKppStep, ForestKpp); a rerun removes the old panel and its zone from scenes integrated before.
    /// </summary>
    public static class ForestExtrasStep
    {
        const string PanelName = "KPP_PowerPanel", PanelZone = "IV_KPP_Power";

        public static void Run(Transform root)
        {
            if (!root.TryGetComponent(out ForestDayNight dn)) dn = root.gameObject.AddComponent<ForestDayNight>();
            dn.startHour = 2f;
            EditorUtility.SetDirty(dn);
            if (!root.TryGetComponent(out ForestEdgeWrap _)) root.gameObject.AddComponent<ForestEdgeWrap>();   // «Лес не отпускает», 26.10
            int removed = 0;
            foreach (Transform old in root.GetComponentsInChildren<Transform>(true).Where(t => t.name == PanelName || t.name == PanelZone).ToArray())
                if (old != null) { Object.DestroyImmediate(old.gameObject); removed++; }
            Debug.Log($"[Snow] Forest extras: day/night (start {dn.startHour:00}:00){(removed > 0 ? $", the old KPP power panel removed ({removed})" : "")}.");
        }
    }
}
