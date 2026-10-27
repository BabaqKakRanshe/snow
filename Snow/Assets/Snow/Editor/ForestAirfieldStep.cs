using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Snow.EditorTools
{
    /// <summary>
    /// Forest integration, the airfield on the east road (docs/gdd/locations/airfield, FOREST_AIRFIELD_CONTRACT.md; prefabs:
    /// <see cref="StartPropsBuilder"/> Build Airfield Props, Assets/Locations/Forest/Airfield): the road straightened for 700 m is
    /// the runway (user 27.10, like a German Autobahn emergency strip) - concrete modules on a smoothed profile of the ground,
    /// tilted with it; BunkerHangar dug into the bank east of it, gate to the runway, its back opening closed by AF_HangarPlug,
    /// the parked UtilityPlane on Slot_Plane with its winter kit on; lights, markers, the barriers, pointer and hut at the road
    /// ends, the windsock, PAPI, the flood mast and the power cabinet. Read from <see cref="DataPath"/> (world x, z, Unity yaw;
    /// tools/forest_airfield/make_layout.py). The ground is edited only in the data's rectangles (<see cref="ForestGroundEdit"/>:
    /// the strip set to its profile, the apron, the hall, earth over it, holes at the gate; trees cleared), from the ground stored
    /// before the first run, so a second run gives the same result. Runway lights stay off (the airfield's power lights them).
    /// Runs after <see cref="ForestBunkerStep"/>; Tools / Snow / Forest / Airfield Dress runs it alone.
    /// </summary>
    public static class ForestAirfieldStep
    {
        public const string DataPath = "Assets/Snow/Levels/Forest/ForestAirfield.json";
        public const string RootName = "ForestAirfield";

        [Serializable] public class Place { public float x, y, z, yaw; }
        [Serializable] public class Item { public string prefab, mode, note; public float x, z, yaw; }
        [Serializable]
        public class Data
        {
            public string note, plane, plug, hangarPrefab;
            public Place hangar;
            public bool kitInstalled;
            public Item[] items;
            public ForestGroundEdit.Rect[] ground;
        }

        [MenuItem("Tools/Snow/Forest/Airfield Dress")]
        static void RunMenu()
        {
            Transform root = GameObject.Find("Forest")?.transform;
            if (root == null) { Debug.LogError("[Snow] Forest airfield: no Forest root in the open scene."); return; }
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
            if (asset == null) { Debug.LogWarning($"[Snow] Forest airfield: {path} missing."); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            go.transform.SetPositionAndRotation(p, q);
            return go;
        }

        public static void Run(Transform root)
        {
            if (!File.Exists(DataPath)) { Debug.LogWarning($"[Snow] Forest airfield: {DataPath} not found; no airfield."); return; }
            var d = JsonUtility.FromJson<Data>(File.ReadAllText(DataPath));

            (int heights, int holes) = ForestGroundEdit.Apply(root, d.ground, Path.ChangeExtension(DataPath, null) + "_ground.bytes", "forest_airfield");
            int trees = ForestGroundEdit.ClearTrees(root, d.ground, 0.5f), grass = ForestGroundEdit.ClearGrass(root, d.ground);

            Transform old = root.Find(RootName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var group = new GameObject(RootName).transform;
            group.SetParent(root, false);

            string hangarInfo = "no hangar";
            GameObject hangar = d.hangar != null && !string.IsNullOrEmpty(d.hangarPrefab)
                ? Put(d.hangarPrefab, group, new Vector3(d.hangar.x, d.hangar.y, d.hangar.z), Quaternion.Euler(0f, d.hangar.yaw, 0f)) : null;
            if (hangar != null)
            {
                Transform link = Deep(hangar.transform, "Link_BlastDoor"), slot = Deep(hangar.transform, "Slot_Plane");
                GameObject plug = link != null && !string.IsNullOrEmpty(d.plug) ? Put(d.plug, group, link.position, hangar.transform.rotation) : null;
                string plane = "no plane";
                if (slot != null && !string.IsNullOrEmpty(d.plane))
                {
                    GameObject p = Put(d.plane, group, slot.position, slot.rotation);
                    if (p != null) plane = $"{p.name} on Slot_Plane, winter kit {(d.kitInstalled ? ForestBunkerStep.InstallKit(p) : 0)} items installed";
                }
                hangarInfo = $"hangar at ({d.hangar.x:0.0}; {d.hangar.y:0.00}; {d.hangar.z:0.0}) yaw {d.hangar.yaw:0}, plug {(plug != null ? "on Link_BlastDoor" : "MISSING")}, {plane}";
            }

            int placed = 0, missing = 0;
            foreach (Item it in d.items ?? new Item[0])
            {
                Quaternion yaw = Quaternion.Euler(0f, it.yaw, 0f);
                float y = ForestGroundEdit.Height(it.x, it.z);
                Quaternion q = yaw;
                if (it.mode == "runway" || it.mode == "apron")
                {
                    // a module lies on the strip: its top 5 cm over the set ground, tilted with the ground along its own +Z
                    Vector3 f = yaw * Vector3.forward;
                    float ya = ForestGroundEdit.Height(it.x + f.x * 5f, it.z + f.z * 5f), yb = ForestGroundEdit.Height(it.x - f.x * 5f, it.z - f.z * 5f);
                    q = yaw * Quaternion.Euler(-Mathf.Atan2(ya - yb, 10f) * Mathf.Rad2Deg, 0f, 0f);
                    y += 0.05f;
                }
                if (Put(it.prefab, group, new Vector3(it.x, y, it.z), q) != null) placed++;
                else missing++;
            }

            Debug.Log($"[Snow] Forest airfield ({DataPath}): {hangarInfo}; {placed} items placed{(missing > 0 ? $", {missing} MISSING" : "")}; " +
                      $"ground: {heights} height samples, {holes} hole cells, {trees} terrain trees and {grass} grass cells cleared.");
        }
    }
}
