using System.Collections.Generic;
using System.Linq;
using Snow.Bench;
using Snow.Levels;
using Snow.Net;
using Snow.World;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Snow.EditorTools
{
    /// <summary>
    /// Tools / Snow / Integrate Sky Island Level: makes the scene built by the Sky Island package (Tools / Sky Island /
    /// Build Scene) a playable level: the game plane (UtilityPlane_Game) on the east threshold of the island runway
    /// facing down the strip (west; it brings its own cargo, PlaneLoadout), the weather provider, spawn points by
    /// the left pilot door, network manager with the game's player, the player camera look (SkyIslandPlayerCamera),
    /// and the scene first in Build Settings. The package builder recreates the scene from scratch, so run this again
    /// after every Sky Island rebuild; running it twice replaces, not duplicates.
    /// </summary>
    public static class SkyIslandLevelBuilder
    {
        public const string ScenePath = "Assets/SkyIsland/Scenes/SkyIsland.unity";
        const string LevelRootName = "SnowLevel";
        const string NetworkManagerName = "NetworkManager";
        const string SceneCameraPath = "SkyIsland/Main Camera";
        // runway from the package layout: edge lights x -400..400, z 83.4..116.6; threshold lights (green) at x +401.6,
        // approach lights east of it. The plane lines up 30 m inside the east threshold and takes off to the west.
        static readonly Vector3 PlaneSpot = new Vector3(370f, 0f, 100f);
        static readonly Quaternion PlaneFacing = Quaternion.Euler(0f, -90f, 0f);
        /// <summary>m: fall reset height (LOCATIONS L4.6).</summary>
        const float FallY = -60f;

        [MenuItem("Tools/Snow/Integrate Sky Island Level")]
        public static void IntegrateMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Integrate();
        }

        public static void Integrate()
        {
            // the plane variant comes from the P03 bench builder; build it once if missing
            var variant = AssetDatabase.LoadAssetAtPath<GameObject>(PlaneBenchBuilder.VariantPath);
            if (variant == null)
            {
                PlaneBenchBuilder.Build();
                variant = AssetDatabase.LoadAssetAtPath<GameObject>(PlaneBenchBuilder.VariantPath);
                if (variant == null) { Debug.LogError($"[Snow] Sky Island: {PlaneBenchBuilder.VariantPath} could not be built."); return; }
            }

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject sceneCamGo = GameObject.Find(SceneCameraPath);
            Camera sceneCam = sceneCamGo != null ? sceneCamGo.GetComponent<Camera>() : null;
            if (sceneCam == null)
            {
                Debug.LogError($"[Snow] {ScenePath}: {SceneCameraPath} not found. Run Tools / Sky Island / Build Scene first.");
                return;
            }
            foreach (string name in new[] { LevelRootName, NetworkManagerName, "Environment", "UtilityPlane_Game" })
            {
                GameObject old = GameObject.Find(name);
                if (old != null) Object.DestroyImmediate(old);
            }

            // the tag Player goes to the own player in play (SkyIslandPlayerCamera, LOCATIONS L4.5), not into Player.prefab
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(BenchBuilder.PlayerPrefabPath);
            NetworkPrefabsList list = BenchBuilder.EnsurePrefabList(player);
            if (!list.PrefabList.Any(x => x.Prefab == variant)) list.Add(new NetworkPrefab { Prefab = variant });
            EditorUtility.SetDirty(list);
            AssetDatabase.SaveAssets();

            Physics.SyncTransforms();
            LayerMask solid = ~(1 << BenchBuilder.InteractLayer());
            if (!Ground(PlaneSpot, solid, out Vector3 planePos, out string groundName))
            {
                Debug.LogError($"[Snow] Sky Island: no runway collider under {PlaneSpot}; the plane would fall through.");
                return;
            }

            var root = new GameObject(LevelRootName).transform;

            // in-scene NetworkObjects stay scene roots
            var envGo = new GameObject("Environment");
            envGo.AddComponent<NetworkObject>();
            envGo.AddComponent<EnvironmentProvider>();

            // parked tail-down on all three wheels (model v5+): root 0.372 m under the runway, nose up 9.98°
            var plane = (GameObject)PrefabUtility.InstantiatePrefab(variant);
            Pose parked = UtilityPlane.UtilityPlaneRig.ParkedPose(planePos, PlaneFacing.eulerAngles.y);
            plane.transform.SetPositionAndRotation(parked.position, parked.rotation);
            Debug.Log($"[Snow] Sky Island: plane at {planePos} on '{groundName}', heading 270.");

            // spawn points in a row off the left side, facing the pilot door (bench P03 layout in plane space)
            var spawnRoot = new GameObject("SpawnPoints").transform;
            spawnRoot.SetParent(root, false);
            var spawns = new List<Transform>();
            for (int i = 0; i < SnowNetworkBootstrap.MaxPlayers; i++)
            {
                Vector3 p = planePos + PlaneFacing * new Vector3(-3.5f - 1.2f * i, 0f, 1.5f);
                if (!Ground(p, solid, out p, out _))
                {
                    Debug.LogError($"[Snow] Sky Island: no collider under spawn {i}; skipped.");
                    continue;
                }
                var sp = new GameObject($"Spawn_{i}").transform;
                sp.SetParent(spawnRoot, false);
                sp.SetPositionAndRotation(p, PlaneFacing * Quaternion.Euler(0f, 90f, 0f));
                spawns.Add(sp);
            }

            BenchBuilder.CreateNetworkManager(player, list, spawns.ToArray()).name = NetworkManagerName;

            // the cargo, and later the key, ladder and kits, come with the plane (PlaneLoadout on the variant)

            root.gameObject.AddComponent<SkyIslandPlayerCamera>().sceneCamera = sceneCam;

            // walked off the island: back to a spawn point (runway and village at 0..20 m, relief from -2.7 m, nothing under it)
            var fall = root.gameObject.AddComponent<LocationFallReset>();
            fall.fallY = FallY;
            fall.spawnRoot = spawnRoot;

            SkySystemsStep.Run(scene);                                   // the sky_systems pack, placed roughly
            BenchBuilder.SaveNetworkScene(scene, ScenePath, true);
            Debug.Log($"[Snow] Sky Island level integrated: {ScenePath} (plane in one piece, {spawns.Count} spawn points), first in Build Settings.");
        }

        static bool Ground(Vector3 at, LayerMask mask, out Vector3 point, out string name)
        {
            point = at;
            name = null;
            if (!Physics.Raycast(new Vector3(at.x, 60f, at.z), Vector3.down, out RaycastHit hit, 200f, mask, QueryTriggerInteraction.Ignore))
                return false;
            point = hit.point;
            name = hit.collider.name;
            return true;
        }
    }
}
