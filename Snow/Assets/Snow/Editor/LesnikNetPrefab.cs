using System.Linq;
using Snow.Levels.Forest;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;

namespace Snow.EditorTools
{
    /// <summary>
    /// Resources/LesnikNet.prefab: the networked Lesnik (NetworkObject, server NetworkTransform, Lesnik) with the blockout
    /// prefab inside as "Model", listed in DefaultNetworkPrefabs. LesnikSpawner puts it into a Forest game on the host.
    /// </summary>
    public static class LesnikNetPrefab
    {
        const string Model = "Assets/Snow/Creatures/Lesnik/Resources/LesnikBlockout.prefab";
        const string PrefabPath = "Assets/Snow/Creatures/Lesnik/Resources/LesnikNet.prefab";
        const string PrefabListPath = "Assets/DefaultNetworkPrefabs.asset";

        [MenuItem("Tools/Snow/Creatures/Build Lesnik Net Prefab")]
        public static string Build()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            if (model == null) return "no " + Model;
            var root = new GameObject("LesnikNet");
            root.AddComponent<NetworkObject>();
            var nt = root.AddComponent<NetworkTransform>();
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            nt.SyncRotAngleX = nt.SyncRotAngleZ = false;                   // he only turns about Y
            root.AddComponent<Lesnik>();
            var m = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            m.name = "Model";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);
            if (list == null) return "no " + PrefabListPath;
            if (!list.PrefabList.Any(x => x.Prefab == prefab))
            {
                list.Add(new NetworkPrefab { Prefab = prefab });
                EditorUtility.SetDirty(list);
                AssetDatabase.SaveAssetIfDirty(list);                     // only this asset: not other sessions' edits
            }
            string msg = $"[Snow] Лесник: {PrefabPath}, в DefaultNetworkPrefabs: {list.PrefabList.Any(x => x.Prefab == prefab)}";
            Debug.Log(msg);
            return msg;
        }
    }
}
