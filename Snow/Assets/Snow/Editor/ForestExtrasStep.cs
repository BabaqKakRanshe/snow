using System.Linq;
using Snow.Levels.Forest;
using UnityEditor;
using UnityEngine;
using UtilityPlane;

namespace Snow.EditorTools
{
    /// <summary>
    /// Forest integration, the user's 21.10 additions: the day / night clock (<see cref="ForestDayNight"/>) and the pockets
    /// (<see cref="ForestInventory"/>) on the Forest root next to the opening, and the checkpoint's power panel — a zone
    /// "KPP_Power" (owned by ForestOpening: without it the motorised barrier stays down) on the booth's door wall, with a copy
    /// of the generator container's main switch as its visible box. Running it again replaces, no duplicates.
    /// </summary>
    public static class ForestExtrasStep
    {
        const string PanelName = "KPP_PowerPanel";
        // booth frame (Checkpoint prefab): door wall at z ≈ -1.66, the door spans x -1.39..-0.44; the panel to its right, outside
        static readonly Vector3 PanelLocal = new Vector3(0.35f, 1.35f, -1.78f);

        public static void Run(Transform root)
        {
            if (!root.TryGetComponent(out ForestDayNight dn)) dn = root.gameObject.AddComponent<ForestDayNight>();
            dn.startHour = 2f;
            EditorUtility.SetDirty(dn);
            if (!root.TryGetComponent(out ForestInventory inv)) inv = root.gameObject.AddComponent<ForestInventory>();
            inv.items = root.GetComponent<ForestItems>();
            EditorUtility.SetDirty(inv);

            Transform booth = root.Find("Structures/Checkpoint");
            Transform zones = root.Find("ForestZones");
            if (booth == null || zones == null) { Debug.LogWarning("[Snow] Forest extras: Structures/Checkpoint or ForestZones not found; no checkpoint power panel."); return; }
            foreach (Transform old in root.GetComponentsInChildren<Transform>(true).Where(t => t.name == PanelName || t.name == "IV_" + ForestOpening.KppPower).ToArray())
                if (old != null) Object.DestroyImmediate(old.gameObject);

            Vector3 at = booth.TransformPoint(PanelLocal);
            Quaternion face = booth.rotation * Quaternion.Euler(0f, 180f, 0f);  // facing out of the door wall
            // the visible box: the generator container's main switch, without its behaviour and colliders
            var container = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Locations/Forest/Prefabs/GenContainer.prefab");
            Transform sw = container != null ? container.transform.Find("IX_MainSwitch_90") : null;
            if (sw != null)
            {
                GameObject panel = Object.Instantiate(sw.gameObject, root);
                panel.name = PanelName;
                foreach (Component c in panel.GetComponentsInChildren<Component>(true))
                    if (c is MonoBehaviour || c is Collider) Object.DestroyImmediate(c);
                panel.transform.SetPositionAndRotation(at, face);
                foreach (Renderer r in panel.GetComponentsInChildren<Renderer>(true)) r.renderingLayerMask = 2u;   // the map's lights
            }
            var zone = new GameObject("IV_" + ForestOpening.KppPower) { layer = BenchBuilder.InteractLayer() };
            zone.transform.SetParent(zones, false);
            zone.transform.SetPositionAndRotation(at, face);
            var box = zone.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(0.6f, 0.7f, 0.35f);
            var ia = zone.AddComponent<UtilityPlaneInteractable>();
            ia.id = ForestOpening.KppPower;
            ia.kind = "toggle";
            ia.label = "Щиток КПП";
            Debug.Log($"[Snow] Forest extras: day/night (start {dn.startHour:00}:00), pockets ({ForestInventory.Slots} slots), checkpoint power panel at {at} " +
                      $"({(sw != null ? "main switch copy" : "zone only")}).");
        }
    }
}
