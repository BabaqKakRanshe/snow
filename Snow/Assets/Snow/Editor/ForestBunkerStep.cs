using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UtilityPlane;

namespace Snow.EditorTools
{
    /// <summary>
    /// Forest integration, the bunker under the lake (START_BUNKER_LOCATION.txt, BUNKER_CONTRACT.md; prefabs:
    /// <see cref="PackPropsBuilder"/>, Assets/Locations/Bunker), at the place the user chose (26.10, site A: the south-west
    /// shore of the lake with the hand, the gate to the north-east): the hangar in the bank with its apron on the ice, the
    /// vestibule with the round door on the hangar's Link_BlastDoor (no turn), the parked UtilityPlane on Slot_Plane with its
    /// winter kit installed and the tarp under its root, the plane package's cases and stepladder on the dressing slots, the
    /// entrance by the hand. Read from <see cref="DataPath"/> (world coordinates). The ground is edited only in the data's
    /// rectangles (<see cref="ForestGroundEdit"/>: the apron cut, earth over the hall's roof, Terrain holes at the facade
    /// and in the entrance's stairwell; trees cleared there), from the ground stored before the first run, so a second run
    /// gives the same result. Links Link_Down, Link_Shaft, Link_SideDoor stay empty points. Nothing interactive.
    /// Runs after <see cref="ForestPacksStep"/>; Tools / Snow / Forest / Bunker Dress runs it alone.
    /// </summary>
    public static class ForestBunkerStep
    {
        public const string DataPath = "Assets/Snow/Levels/Forest/ForestBunker.json";
        public const string RootName = "ForestBunker";
        const string Prefabs = "Assets/Locations/Bunker/Prefabs/";

        [Serializable] public class Place { public float x, y, z, yaw; }
        [Serializable] public class Dressing { public string slot, prefab; public float yaw; }
        [Serializable]
        public class Data
        {
            public string note, plane;
            public Place hangar, entrance;
            public bool kitInstalled;
            public Dressing[] dressing;
            public ForestGroundEdit.Rect[] ground;
        }

        [MenuItem("Tools/Snow/Forest/Bunker Dress")]
        static void RunMenu()
        {
            Transform root = GameObject.Find("Forest")?.transform;
            if (root == null) { Debug.LogError("[Snow] Forest bunker: no Forest root in the open scene."); return; }
            Run(root);
        }

        static Transform Deep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                Transform f = Deep(c, name);
                if (f != null) return f;
            }
            return null;
        }

        static GameObject Put(string path, Transform parent, Vector3 p, Quaternion q)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) { Debug.LogWarning($"[Snow] Forest bunker: {path} missing."); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            go.transform.SetPositionAndRotation(p, q);
            return go;
        }

        public static void Run(Transform root)
        {
            if (!File.Exists(DataPath)) { Debug.LogWarning($"[Snow] Forest bunker: {DataPath} not found; no bunker."); return; }
            var d = JsonUtility.FromJson<Data>(File.ReadAllText(DataPath));

            (int heights, int holes) = ForestGroundEdit.Apply(root, d.ground, Path.ChangeExtension(DataPath, null) + "_ground.bytes", "forest_bunker");
            int trees = ForestGroundEdit.ClearTrees(root, d.ground, 0.5f), grass = ForestGroundEdit.ClearGrass(root, d.ground);

            Transform old = root.Find(RootName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var group = new GameObject(RootName).transform;
            group.SetParent(root, false);

            GameObject hangar = Put(Prefabs + "BunkerHangar.prefab", group, new Vector3(d.hangar.x, d.hangar.y, d.hangar.z), Quaternion.Euler(0f, d.hangar.yaw, 0f));
            if (hangar == null) return;
            Transform link = Deep(hangar.transform, "Link_BlastDoor"), slot = Deep(hangar.transform, "Slot_Plane");
            GameObject door = link != null ? Put(Prefabs + "BlastDoor.prefab", group, link.position, hangar.transform.rotation) : null;

            string plane = "no plane";
            if (slot != null && !string.IsNullOrEmpty(d.plane))
            {
                GameObject p = Put(d.plane, group, slot.position, slot.rotation);
                if (p != null)
                {
                    int kit = d.kitInstalled ? InstallKit(p) : 0;
                    GameObject tarp = Put(Prefabs + "PlaneTarp.prefab", p.transform, p.transform.position, p.transform.rotation);
                    if (tarp != null) { tarp.transform.localPosition = Vector3.zero; tarp.transform.localRotation = Quaternion.identity; }
                    plane = $"{p.name} on Slot_Plane, winter kit {kit} items installed, tarp {(tarp != null ? "on" : "MISSING")}";
                }
            }
            int dressed = 0;
            foreach (Dressing dr in d.dressing ?? new Dressing[0])
            {
                Transform s = Deep(hangar.transform, dr.slot);
                if (s == null) { Debug.LogWarning($"[Snow] Forest bunker: {dr.slot} missing."); continue; }
                if (Put(dr.prefab, group, s.position, s.rotation * Quaternion.Euler(0f, dr.yaw, 0f)) != null) dressed++;   // the slot's own yaw (to the wall)
            }
            GameObject entrance = Put(Prefabs + "BunkerEntrance.prefab", group,
                new Vector3(d.entrance.x, d.entrance.y != 0f ? d.entrance.y : ForestGroundEdit.Height(d.entrance.x, d.entrance.z), d.entrance.z),
                Quaternion.Euler(0f, d.entrance.yaw, 0f));

            Debug.Log($"[Snow] Forest bunker ({DataPath}): hangar at ({d.hangar.x:0.0}; {d.hangar.y:0.00}; {d.hangar.z:0.0}) yaw {d.hangar.yaw:0}, " +
                      $"blast door {(door != null ? "on Link_BlastDoor" : "MISSING")}, {plane}, {dressed} dressing props, entrance {(entrance != null ? $"at ({entrance.transform.position.x:0.0}; {entrance.transform.position.y:0.00}; {entrance.transform.position.z:0.0})" : "MISSING")}; " +
                      $"ground: {heights} height samples, {holes} hole cells, {trees} terrain trees and {grass} grass cells cleared.");
        }

        /// <summary>The winter kit stands on the plane (mothballed): every slot installed but the heater, which shares the
        /// intake with the intake plug. A scene override of the instance; the kit's component shows them at Awake.</summary>
        internal static int InstallKit(GameObject plane)
        {
            var kit = plane.GetComponentInChildren<UtilityPlaneWinterKit>(true);
            if (kit == null) return 0;
            int n = 0;
            foreach (UtilityPlaneWinterKit.Slot s in kit.slots.Where(s => s != null))
            {
                s.installed = s.item != KitItem.Heater;
                if (s.visual != null) s.visual.SetActive(s.installed);
                if (s.installed) n++;
            }
            EditorUtility.SetDirty(kit);
            PrefabUtility.RecordPrefabInstancePropertyModifications(kit);
            return n;
        }
    }
}
