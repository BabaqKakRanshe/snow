using System.Collections.Generic;
using System.Linq;
using Snow.Bench;
using Snow.Character;
using Snow.Doors;
using Snow.Input;
using Snow.Interaction;
using Snow.Net;
using Snow.Player;
using Snow.Seats;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UtilityPlane;

namespace Snow.EditorTools
{
    /// <summary>
    /// Tools / Snow / Build P01 Net Bench: creates the player prefab and the P01 bench scene from code, so running it
    /// again replaces them instead of adding duplicates (PLANE_PROMPT §0 rule 7). Primitives only (P01).
    /// </summary>
    public static class BenchBuilder
    {
        public const string Folder = "Assets/Snow/Bench";
        public const string PlayerPrefabPath = Folder + "/Player.prefab";
        public const string ScenePath = Folder + "/P01_NetBench.unity";
        const string ActionsPath = "Assets/Snow/Input/SnowControls.inputactions";
        const string DefaultPrefabsListPath = "Assets/DefaultNetworkPrefabs.asset";

        [MenuItem("Tools/Snow/Build P01 Net Bench")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Snow", "Bench");
            GameObject player = BuildPlayerPrefab();
            NetworkPrefabsList list = EnsurePrefabList(player);
            BuildScene(player, list);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Snow] P01 bench built: {PlayerPrefabPath}, {ScenePath}");
        }

        // ------------------------------------------------------------------ player
        /// <summary>The existing Player.prefab gets the Player tag on its root without a rebuild (LOCATIONS L4.5: the
        /// core's fog event volumes react to it). True if the prefab had to change.</summary>
        internal static bool EnsurePlayerTag()
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (asset == null || asset.CompareTag("Player")) return false;
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                root.tag = "Player";
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            Debug.Log($"[Snow] {PlayerPrefabPath}: root tag Player (core fog event volumes).");
            return true;
        }

        internal static GameObject BuildPlayerPrefab()
        {
            var tuning = new MovementTuning();
            // built-in tag: the core's fog event volumes (Forest FogUnderHand, FogRibs) react to colliders tagged Player
            var root = new GameObject("Player") { tag = "Player" };

            var cc = root.AddComponent<CharacterController>();
            cc.skinWidth = HLCharacterMotor.SkinWidth;
            cc.height = tuning.standHeight;
            cc.radius = tuning.radius;
            cc.center = new Vector3(0f, HLCharacterMotor.SkinWidth + tuning.standHeight * 0.5f, 0f);
            cc.stepOffset = 0f;
            cc.slopeLimit = tuning.slopeLimit;
            cc.minMoveDistance = 0f;

            var motor = root.AddComponent<HLCharacterMotor>();
            motor.collisionMask = ~(1 << InteractLayer());
            var view = root.AddComponent<FirstPersonView>();
            view.motor = motor;
            var input = root.AddComponent<SnowInput>();
            var so = new SerializedObject(input);
            so.FindProperty("actions").objectReferenceValue = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ActionsPath);
            so.ApplyModifiedPropertiesWithoutUndo();
            var controller = root.AddComponent<PlayerController>();
            controller.motor = motor;
            controller.view = view;

            root.AddComponent<NetworkObject>();
            var nt = root.AddComponent<NetworkTransform>();
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Owner;     // §11.3: movement belongs to the owner
            nt.SwitchTransformSpaceWhenParented = true;                   // seats parent the player to the vehicle
            nt.SyncRotAngleX = false;
            nt.SyncRotAngleZ = false;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            nt.Interpolate = true;

            var net = root.AddComponent<PlayerNet>();
            net.controller = controller;
            net.motor = motor;
            net.view = view;
            net.humanInput = input;

            var sensor = root.AddComponent<InteractionSensor>();
            sensor.net = net;
            sensor.controller = controller;
            sensor.view = view;
            var outline = root.AddComponent<HoverOutline>();
            outline.sensor = sensor;
            var overlay = root.AddComponent<InteractionOverlay>();
            overlay.sensor = sensor;
            overlay.controller = controller;
            overlay.view = view;
            var seat = root.AddComponent<SeatController>();
            seat.net = net;
            seat.controller = controller;
            seat.motor = motor;
            seat.view = view;
            seat.sensor = sensor;
            net.sensor = sensor;
            net.seatController = seat;

            // TEMPORARY body (M4: no character mesh): a capsule and a marker showing others where the player looks
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = cc.center;
            body.transform.localScale = new Vector3(tuning.radius * 2f, tuning.standHeight * 0.5f, tuning.radius * 2f);
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "FacingMarker";
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            marker.transform.SetParent(body.transform, false);
            marker.transform.localPosition = new Vector3(0f, 0.8f, 0.45f);
            marker.transform.localScale = new Vector3(0.6f, 0.06f, 0.2f);
            net.body = body.GetComponent<Renderer>();
            net.hideForOwner = new[] { body.GetComponent<Renderer>(), marker.GetComponent<Renderer>() };

            var camGo = new GameObject("View");
            camGo.transform.SetParent(root.transform, false);
            camGo.transform.localPosition = Vector3.up * tuning.eyeHeight;
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.03f;
            cam.enabled = false;
            camGo.AddComponent<AudioListener>().enabled = false;
            view.cam = cam;
            net.cam = cam;

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        internal static Material EnsureMaterial(string name, string shader)
        {
            const string folder = "Assets/Snow/Materials";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Snow", "Materials");
            string path = $"{folder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            var s = Shader.Find(shader);
            if (s == null) { Debug.LogError($"[Snow] shader {shader} not found"); return null; }
            m = new Material(s);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        internal static NetworkPrefabsList EnsurePrefabList(GameObject player)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(DefaultPrefabsListPath);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(list, DefaultPrefabsListPath);
            }
            if (!list.PrefabList.Any(p => p.Prefab == player))
            {
                list.Add(new NetworkPrefab { Prefab = player });
                EditorUtility.SetDirty(list);
            }
            return list;
        }

        // ------------------------------------------------------------------ scene
        static void BuildScene(GameObject player, NetworkPrefabsList list)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            Box("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(2000f, 1f, 2000f), 0f);

            // HL course (AT-HL1): steps below, at and above stepOffset, ledges for jump and crouch-jump, a ramp, a low ceiling
            var course = new GameObject("HL_Course").transform;
            float x = -12f;
            foreach (float h in new[] { 0.18f, 0.30f, 0.45f, 0.70f, 1.00f })
            {
                Box($"Ledge_{h:0.00}", new Vector3(x, h * 0.5f, 10f), new Vector3(3f, h, 4f), 0f, course);
                x += 5f;
            }
            Box("Ramp_30", new Vector3(14f, 0f, 10f), new Vector3(3f, 0.2f, 6f), -30f, course);
            Box("LowCeiling_1_2", new Vector3(20f, 1.3f, 10f), new Vector3(3f, 0.2f, 4f), 0f, course);
            Box("LowCeiling_PostL", new Vector3(18.6f, 0.6f, 10f), new Vector3(0.2f, 1.2f, 4f), 0f, course);
            Box("LowCeiling_PostR", new Vector3(21.4f, 0.6f, 10f), new Vector3(0.2f, 1.2f, 4f), 0f, course);
            Box("Wall", new Vector3(-18f, 1.5f, 10f), new Vector3(0.3f, 3f, 10f), 0f, course);   // west of the course, off the bot paths

            BuildCabin(new Vector3(0f, 0f, -8f));

            // spawn points on a ring, facing the course
            var spawns = new List<Transform>();
            var spawnRoot = new GameObject("SpawnPoints").transform;
            for (int i = 0; i < SnowNetworkBootstrap.MaxPlayers; i++)
            {
                var sp = new GameObject($"Spawn_{i}").transform;
                sp.SetParent(spawnRoot, false);
                sp.position = new Vector3(-3f + 2f * i, 0f, 0f);
                sp.rotation = Quaternion.identity;
                spawns.Add(sp);
            }

            CreateNetworkManager(player, list, spawns.ToArray());

            // overview camera for the host window until a local player takes over
            var mainCam = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).FirstOrDefault();
            if (mainCam != null)
            {
                mainCam.transform.SetPositionAndRotation(new Vector3(0f, 12f, -18f), Quaternion.Euler(30f, 0f, 0f));
            }

            SaveNetworkScene(scene, ScenePath, true);
        }

        /// <summary>
        /// Save a scene with in-scene NetworkObjects so NGO recognises them. NetworkObject.OnValidate computes GlobalObjectIdHash
        /// only for a saved scene; with a zero hash NGO's scene processor does not mark the object in-scene placed and the
        /// server refuses to spawn it (error seen 29.09). So: save, add to Build Settings, reopen (OnValidate), save again.
        /// </summary>
        internal static void SaveNetworkScene(Scene scene, string path, bool first)
        {
            EditorSceneManager.SaveScene(scene, path);
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != path).ToList();
            if (first) scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            else scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Scene reopened = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            // reopening runs NetworkObject.OnValidate (scene load), which now sees a saved scene with a build index
            foreach (var no in Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                EditorUtility.SetDirty(no);
            EditorSceneManager.MarkSceneDirty(reopened);
            EditorSceneManager.SaveScene(reopened);
        }

        // ------------------------------------------------------------------ P01 cabin (primitives)
        // Local frame: +Z front, +X right side. Floor top at 0.1. Left wall has a door opening z -0.35..0.65,
        // height 1.9; the right side is open (no door). A crate on the right exit point tests "no room to get out".
        static void BuildCabin(Vector3 at)
        {
            var root = new GameObject("BenchCabin");
            root.transform.position = at;
            var t = root.transform;
            root.AddComponent<NetworkObject>();

            LocalBox("Floor", t, new Vector3(0f, 0.05f, 0f), new Vector3(2.4f, 0.1f, 2.6f));
            LocalBox("Roof", t, new Vector3(0f, 2.15f, 0f), new Vector3(2.4f, 0.1f, 2.6f));
            LocalBox("Wall_Front", t, new Vector3(0f, 1.1f, 1.3f), new Vector3(2.4f, 2.0f, 0.1f));
            LocalBox("Wall_Back", t, new Vector3(0f, 1.1f, -1.3f), new Vector3(2.4f, 2.0f, 0.1f));
            LocalBox("Wall_L_Front", t, new Vector3(-1.2f, 1.1f, 0.95f), new Vector3(0.1f, 2.0f, 0.6f));
            LocalBox("Wall_L_Back", t, new Vector3(-1.2f, 1.1f, -0.8f), new Vector3(0.1f, 2.0f, 0.9f));
            LocalBox("Wall_L_Lintel", t, new Vector3(-1.2f, 2.05f, 0.15f), new Vector3(0.1f, 0.1f, 1.0f));

            // door: hinge at the front edge of the opening, opens outward (+80 deg about Y)
            var hinge = new GameObject("Door_Hinge").transform;
            hinge.SetParent(t, false);
            hinge.localPosition = new Vector3(-1.2f, 0.1f, 0.65f);
            var panel = LocalBox("Door_Panel", hinge, new Vector3(0f, 0.95f, -0.5f), new Vector3(0.05f, 1.88f, 0.98f));
            LocalBox("Door_Knob_Out", hinge, new Vector3(-0.06f, 1.0f, -0.88f), new Vector3(0.05f, 0.05f, 0.12f));
            LocalBox("Door_Knob_In", hinge, new Vector3(0.06f, 1.0f, -0.88f), new Vector3(0.05f, 0.05f, 0.12f));
            Volume(hinge, BenchCabin.HandleOut, "door", "Дверь (ручка снаружи)", new Vector3(-0.1f, 1.0f, -0.88f), new Vector3(0.08f, 0.08f, 0.1f));
            Volume(hinge, BenchCabin.HandleIn, "door", "Дверь (ручка изнутри)", new Vector3(0.1f, 1.0f, -0.88f), new Vector3(0.08f, 0.08f, 0.1f));
            var door = hinge.gameObject.AddComponent<NetworkDoor>();
            door.doorId = "Door_L";
            door.openAngle = 80f;
            door.panelColliders = new[] { panel.GetComponent<Collider>() };
            door.vehicleRoot = t;
            door.blockMask = ~(1 << InteractLayer());

            // door buttons: a post outside by the door, one on the front panel by the pilot (NEW-04)
            LocalBox("Button_Post", t, new Vector3(-1.75f, 0.6f, 1.0f), new Vector3(0.1f, 1.2f, 0.1f));
            LocalBox("Button_Out", t, new Vector3(-1.75f, 1.25f, 1.0f), new Vector3(0.08f, 0.08f, 0.08f));
            Volume(t, BenchCabin.ButtonOut, "toggle", "Кнопка двери (снаружи)", new Vector3(-1.75f, 1.25f, 1.0f), Vector3.one * 0.07f);
            LocalBox("Button_In", t, new Vector3(-0.8f, 1.3f, 1.22f), new Vector3(0.06f, 0.06f, 0.06f));
            Volume(t, BenchCabin.ButtonIn, "toggle", "Кнопка двери (в кабине)", new Vector3(-0.8f, 1.3f, 1.22f), Vector3.one * 0.05f);

            // seats: left = pilot (SEAT-01)
            var seats = new List<SeatAnchor>();
            AddSeat(t, seats, "Seat_P", -0.45f, SeatRole.Pilot, "Кресло пилота", -1.9f, door);
            AddSeat(t, seats, "Seat_C", 0.45f, SeatRole.Passenger, "Кресло пассажира", 1.9f, null);  // open side on the bench

            // yoke in front of the pilot, driven by the server's control state
            var yoke = LocalBox("Yoke", t, new Vector3(-0.45f, 1.05f, 0.75f), new Vector3(0.35f, 0.06f, 0.05f));
            Object.DestroyImmediate(yoke.GetComponent<Collider>());

            var cabin = root.AddComponent<BenchCabin>();
            cabin.door = door;
            cabin.yoke = yoke.transform;
            cabin.seats = seats.ToArray();
            cabin.interiorCenter = new Vector3(0f, 1.1f, 0f);             // inside the walls
            cabin.interiorSize = new Vector3(2.3f, 2.0f, 2.5f);

            // physical item for "use" hints (grab is P02); it also blocks the right exit on purpose
            var crate = Box("Crate_ExitBlocker", at + new Vector3(1.9f, 0.35f, 0.15f), new Vector3(0.7f, 0.7f, 0.7f), 0f);
            crate.isStatic = false;
            crate.AddComponent<Grabbable>().label = "Ящик";
        }

        static void AddSeat(Transform t, List<SeatAnchor> seats, string id, float x, SeatRole role, string label, float exitX, NetworkDoor exitDoor)
        {
            LocalBox(id + "_Cushion", t, new Vector3(x, 0.35f, 0.1f), new Vector3(0.5f, 0.5f, 0.5f));
            LocalBox(id + "_Back", t, new Vector3(x, 0.85f, -0.13f), new Vector3(0.5f, 0.5f, 0.06f));
            Volume(t, id, "seat", label, new Vector3(x, 0.8f, 0.1f), new Vector3(0.25f, 0.2f, 0.25f));
            var anchor = new GameObject(id).AddComponent<SeatAnchor>();
            anchor.transform.SetParent(t, false);
            anchor.seatId = id;
            anchor.role = role;
            anchor.pelvis = Socket(t, "Socket_" + id + "_Pelvis", new Vector3(x, 0.6f, 0.1f), 0f);
            anchor.eye = Socket(t, "Socket_" + id + "_Eye", new Vector3(x, 1.35f, 0.1f), 0f);
            anchor.exit = Socket(t, "Socket_" + id + "_Exit", new Vector3(exitX, 0f, 0.15f), x < 0f ? -90f : 90f);
            anchor.exitDoor = exitDoor;
            seats.Add(anchor);
        }

        internal static GameObject LocalBox(string name, Transform parent, Vector3 localPos, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            return go;
        }

        internal static Transform Socket(Transform parent, string name, Vector3 localPos, float yaw)
        {
            var s = new GameObject(name).transform;
            s.SetParent(parent, false);
            s.localPosition = localPos;
            s.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return s;
        }

        internal static void Volume(Transform parent, string id, string kind, string label, Vector3 localCenter, Vector3 halfExtents)
        {
            var go = new GameObject("IV_" + id) { layer = InteractLayer() };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localCenter;
            var bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = halfExtents * 2f;
            var it = go.AddComponent<UtilityPlaneInteractable>();
            it.id = id;
            it.kind = kind;
            it.label = label;
        }

        internal static SnowNetworkBootstrap CreateNetworkManager(GameObject player, NetworkPrefabsList list, Transform[] spawns)
        {
            var netGo = new GameObject("NetworkManager");
            var nm = netGo.AddComponent<NetworkManager>();
            var ut = netGo.AddComponent<UnityTransport>();
            nm.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = ut,
                PlayerPrefab = player,
                ConnectionApproval = true,
                EnableSceneManagement = true,
            };
            nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(list);
            var boot = netGo.AddComponent<SnowNetworkBootstrap>();
            boot.spawnPoints = spawns;
            return boot;
        }

        internal static GameObject Box(string name, Vector3 center, Vector3 size, float pitch, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(center, Quaternion.Euler(pitch, 0f, 0f));
            go.transform.localScale = size;
            go.isStatic = true;
            return go;
        }

        internal static int InteractLayer()
        {
            int l = LayerMask.NameToLayer("Interact");
            return l >= 0 ? l : 8;
        }
    }
}
