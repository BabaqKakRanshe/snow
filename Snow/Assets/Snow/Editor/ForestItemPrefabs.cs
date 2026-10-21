using System.Collections.Generic;
using System.Linq;
using Snow.Aircraft;
using Snow.Cargo;
using Snow.Levels.Forest;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;

namespace Snow.EditorTools
{
    /// <summary>
    /// Tools / Snow / Forest / Build Item Prefabs (FOREST_U3 F4.1): a network cargo prefab for each of the 83 models of
    /// Forest_Items.json, built like the game's loose items (PlaneBenchBuilder.NetItem — the survival kit and winter kit
    /// items: Rigidbody with interpolation and continuous dynamic collisions, server NetworkTransform without scale,
    /// NetworkRigidbody, CargoItem; layer Default). The model goes in as a child (materials as the location builder
    /// remapped them), without its import colliders; a BoxCollider from the catalogue, no side under 2 cm (TEMPORARY: thin
    /// items fall through at 1/120 s). Extras: the jerrycan carries fuel, the six note models can be read, the flashlight
    /// is the kit's flashlight with its own light point and lens, the rubbish bag takes rubbish. Saved at the same path
    /// every run (same GUID: replaced, not duplicated) and listed in Assets/DefaultNetworkPrefabs.asset.
    /// </summary>
    public static class ForestItemPrefabs
    {
        public const string Folder = "Assets/Snow/Levels/Forest/Items";
        const string ModelsFolder = "Assets/Locations/Forest/Models";
        const string ItemsJson = "Assets/Locations/Forest/Data/Forest_Items.json";
        const string MaterialsFolder = "Assets/Locations/Forest/Materials/URP";
        /// <summary>The list BenchBuilder.EnsurePrefabList makes and the network managers use.</summary>
        const string PrefabListPath = "Assets/DefaultNetworkPrefabs.asset";
        const float MinSide = 0.02f;
        const string Jerrycan = "Item_Jerrycan20", TrashBagModel = "Item_TrashBag";

        [MenuItem("Tools/Snow/Forest/Build Item Prefabs")]
        static void Menu() => Build();

        /// <summary>Batch mode: -executeMethod Snow.EditorTools.ForestItemPrefabs.BuildFromCommandLine</summary>
        public static void BuildFromCommandLine()
        {
            bool ok = Build();
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        public static bool Build()
        {
            var data = ForestData.Read<ItemsData>(AssetDatabase.LoadAssetAtPath<TextAsset>(ItemsJson));
            if (data?.items == null || data.items.Length == 0) { Debug.LogError($"[Snow] Forest items: {ItemsJson} not found or empty."); return false; }
            EnsureFolder(Folder);
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(BenchBuilder.PlayerPrefabPath);
            NetworkPrefabsList list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath) ?? BenchBuilder.EnsurePrefabList(player);
            int created = 0, updated = 0, failed = 0;
            var prefabs = new List<GameObject>();
            try
            {
                for (int i = 0; i < data.items.Length; i++)
                {
                    ItemEntry e = data.items[i];
                    EditorUtility.DisplayProgressBar("Forest items", e.model, (float)i / data.items.Length);
                    string path = $"{Folder}/{e.model}.prefab";
                    bool existed = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
                    GameObject go = BuildItem(e);
                    if (go == null) { failed++; continue; }
                    GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
                    Object.DestroyImmediate(go);
                    if (prefab == null) { failed++; continue; }
                    prefabs.Add(prefab);
                    if (existed) updated++; else created++;
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            foreach (GameObject p in prefabs)
                if (!list.PrefabList.Any(x => x.Prefab == p)) list.Add(new NetworkPrefab { Prefab = p });
            EditorUtility.SetDirty(list);
            AssetDatabase.SaveAssets();
            int listed = prefabs.Count(p => list.PrefabList.Any(x => x.Prefab == p));
            Debug.Log($"[Snow] Forest items: {prefabs.Count} prefabs ({created} new, {updated} updated), {listed} in DefaultNetworkPrefabs.");
            if (failed > 0) Debug.LogError($"[Snow] Forest items: {failed} model(s) failed (see above).");
            return failed == 0;
        }

        static GameObject BuildItem(ItemEntry e)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelsFolder}/{e.model}.fbx");
            if (model == null) { Debug.LogError($"[Snow] Forest items: {ModelsFolder}/{e.model}.fbx missing."); return null; }
            var root = new GameObject(e.model);
            var m = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(m, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            m.transform.SetParent(root.transform, false);
            m.transform.localPosition = Vector3.zero;
            m.transform.localRotation = Quaternion.identity;
            // no import colliders on a dynamic body (a concave MeshCollider is not supported there); 'Item_' has none anyway
            foreach (Collider c in m.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (Renderer r in m.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            var rb = root.AddComponent<Rigidbody>();
            rb.mass = e.massKg;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            root.AddComponent<NetworkObject>();
            var nt = root.AddComponent<NetworkTransform>();
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            root.AddComponent<NetworkRigidbody>();
            var item = root.AddComponent<CargoItem>();
            item.label = e.label;
            item.itemKey = e.model;
            item.massKg = e.massKg;

            var box = root.AddComponent<BoxCollider>();
            Vector3 center = ForestData.V3(e.collider?.center), size = ForestData.V3(e.collider?.size);
            if (size.y < MinSide) { center.y += (MinSide - size.y) * 0.5f; size.y = MinSide; }   // the bottom stays
            size.x = Mathf.Max(size.x, MinSide);
            size.z = Mathf.Max(size.z, MinSide);
            box.center = center;
            box.size = size;

            var tag = root.AddComponent<ForestItemTag>();
            tag.model = e.model;
            tag.clean = e.clean;
            tag.key = e.key;

            if (e.model == Jerrycan) root.AddComponent<FuelCan>();
            if (e.readable) root.AddComponent<ReadableNote>();
            if (e.model == TrashBagModel)
            {
                root.AddComponent<TrashBag>();
                // the upper half of the bag's box takes what falls in
                var trig = new GameObject("TrashTrigger");
                trig.transform.SetParent(root.transform, false);
                var t = trig.AddComponent<BoxCollider>();
                t.isTrigger = true;
                t.center = center + Vector3.up * (size.y * 0.25f);
                t.size = new Vector3(size.x, size.y * 0.5f, size.z);
            }
            if (e.light != null && e.light.p != null && e.light.p.Length >= 3) Flashlight(root, m, e.light);
            return root;
        }

        /// <summary>The kit's flashlight logic (SurvivalItem: Socket_Light, light on all rendering layers) with the
        /// catalogue's light point and direction and the lens M_TorchLens <-> M_TorchLensOn.</summary>
        static void Flashlight(GameObject root, GameObject model, ItemLight light)
        {
            var socket = new GameObject("Socket_Light").transform;
            socket.SetParent(root.transform, false);
            socket.localPosition = ForestData.V3(light.p);
            Vector3 d = ForestData.V3(light.d);
            socket.localRotation = Quaternion.LookRotation(d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward, Vector3.up);
            var s = root.AddComponent<SurvivalItem>();
            s.kind = SurvivalKind.Flashlight;
            s.lensOn = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/{light.lensOn}.mat");
            s.lensOff = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/{light.lensOff}.mat");
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && (mats[i].name == light.lensOff || mats[i].name == light.lensOn))
                    {
                        s.lensRenderer = r;
                        s.lensSlot = i;
                        return;
                    }
            }
            Debug.LogWarning($"[Snow] Forest items: no {light.lensOff} slot on the flashlight model; the lens does not light up.");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
