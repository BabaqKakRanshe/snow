using System.Collections.Generic;
using System.Linq;
using Snow.Bench;
using Snow.Cargo;
using Snow.Doors;
using Snow.Seats;
using Snow.Vehicles;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Snow.EditorTools
{
    /// <summary>
    /// Tools / Snow / Build P02 Physics Bench: networked cargo prefabs from the Utility Plane cases and the P02 scene with
    /// the test carrier (hollow bay, side door, two seats, four tie-downs), bumps and the cargo spawner. Idempotent.
    /// </summary>
    public static class PhysicsBenchBuilder
    {
        internal const string CargoFolder = BenchBuilder.Folder + "/Cargo";
        public const string ScenePath = BenchBuilder.Folder + "/P02_PhysicsBench.unity";

        // TEMPORARY masses, kg (docs/dev/Tuning/Cargo.md): Case5 is the heavy one of AT-08 (one hand 400 N < 588 N weight < two)
        internal static readonly float[] Masses = { 18f, 22f, 12f, 10f, 60f, 7f };

        [MenuItem("Tools/Snow/Build P02 Physics Bench")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(BenchBuilder.Folder)) AssetDatabase.CreateFolder("Assets/Snow", "Bench");
            if (!AssetDatabase.IsValidFolder(CargoFolder)) AssetDatabase.CreateFolder(BenchBuilder.Folder, "Cargo");
            GameObject player = BenchBuilder.BuildPlayerPrefab();
            NetworkPrefabsList list = BenchBuilder.EnsurePrefabList(player);
            var cargo = new List<GameObject>();
            for (int i = 1; i <= 6; i++)
            {
                GameObject p = BuildCargoPrefab(i, Masses[i - 1]);
                if (p == null) continue;
                cargo.Add(p);
                if (!list.PrefabList.Any(x => x.Prefab == p)) list.Add(new NetworkPrefab { Prefab = p });
            }
            EditorUtility.SetDirty(list);
            BuildScene(player, list, cargo);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Snow] P02 physics bench built: {ScenePath}, {cargo.Count} cargo prefabs in {CargoFolder}");
        }

        internal static GameObject BuildCargoPrefab(int index, float mass)
        {
            string src = $"Assets/UtilityPlane/Prefabs/Cargo_Case{index}.prefab";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(src);
            if (source == null) { Debug.LogWarning($"[Snow] {src} missing: run Tools/Utility Plane/Build Materials + Prefabs"); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = $"Cargo_Case{index}_Net";

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;   // §12: no tunnelling through the floor
            go.AddComponent<NetworkObject>();
            var nt = go.AddComponent<NetworkTransform>();
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Server;              // variant A: the server simulates cargo
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            go.AddComponent<NetworkRigidbody>();
            var item = go.AddComponent<CargoItem>();
            item.label = $"Кейс {index}";
            item.massKg = mass;

            // visible strap while secured (SECURE-01), no collider
            var mesh = go.GetComponentInChildren<MeshFilter>().sharedMesh;
            Bounds b = mesh.bounds;
            var strap = GameObject.CreatePrimitive(PrimitiveType.Cube);
            strap.name = "SecuredStrap";
            Object.DestroyImmediate(strap.GetComponent<Collider>());
            strap.transform.SetParent(go.transform, false);
            strap.transform.localPosition = new Vector3(b.center.x, b.max.y * 0.5f, b.center.z);
            strap.transform.localScale = new Vector3(b.size.x + 0.02f, b.max.y + 0.02f, 0.05f);
            strap.GetComponent<Renderer>().sharedMaterial = StrapMaterial();
            strap.SetActive(false);
            item.securedMarker = strap;

            string path = $"{CargoFolder}/{go.name}.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        static Material StrapMaterial()
        {
            string path = CargoFolder + "/M_Strap.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", new Color(1f, 0.55f, 0.05f));
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ------------------------------------------------------------------ scene
        static void BuildScene(GameObject player, NetworkPrefabsList list, List<GameObject> cargo)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            BenchBuilder.Box("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(2000f, 1f, 2000f), 0f);
            var road = new GameObject("Road").transform;
            for (int i = 0; i < 6; i++)
                BenchBuilder.Box($"Bump_{i}", new Vector3(0f, 0.04f, -5f + 12f * i), new Vector3(8f, 0.08f, 0.4f), 0f, road);
            BenchBuilder.Box("Ramp", new Vector3(12f, 0f, 20f), new Vector3(4f, 0.3f, 10f), -8f, road);

            BenchCarrier carrier = BuildCarrier(new Vector3(0f, 0.6f, -25f));

            var spawns = new List<Transform>();
            var spawnRoot = new GameObject("SpawnPoints").transform;
            for (int i = 0; i < 4; i++)
            {
                var sp = new GameObject($"Spawn_{i}").transform;
                sp.SetParent(spawnRoot, false);
                sp.position = new Vector3(-4.5f - 1.2f * i, 0f, -25f);
                sp.rotation = Quaternion.Euler(0f, 90f, 0f);
                spawns.Add(sp);
            }
            BenchBuilder.CreateNetworkManager(player, list, spawns.ToArray());

            var bay = carrier.transform.Find("BayAnchor");
            var spawner = new GameObject("CargoSpawner").AddComponent<BenchCargoSpawner>();
            GameObject C(int n) => cargo.FirstOrDefault(c => c.name == $"Cargo_Case{n}_Net");
            spawner.items = new[]
            {
                // in the bay, loose (AT-10, AT-12): two on the floor, one stacked
                new BenchCargoSpawner.Entry { prefab = C(1), anchor = bay, offset = new Vector3(-0.4f, 0f, -0.2f) },
                new BenchCargoSpawner.Entry { prefab = C(2), anchor = bay, offset = new Vector3(0.45f, 0f, -1.6f) },
                new BenchCargoSpawner.Entry { prefab = C(3), anchor = bay, offset = new Vector3(-0.4f, 0f, -0.2f), stackOn = "Cargo_Case1_Net" },
                // outside: the heavy case by the bay door (AT-08) and two light ones by the spawn (AT-07, AT-09)
                new BenchCargoSpawner.Entry { prefab = C(5), anchor = null, offset = new Vector3(-3.2f, 0.02f, -25.8f) },
                new BenchCargoSpawner.Entry { prefab = C(4), anchor = null, offset = new Vector3(-6f, 0.02f, -27f) },
                new BenchCargoSpawner.Entry { prefab = C(6), anchor = null, offset = new Vector3(-7.5f, 0.02f, -27f) },
            }.Where(e => e.prefab != null).ToArray();

            var mainCam = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).FirstOrDefault();
            if (mainCam != null) mainCam.transform.SetPositionAndRotation(new Vector3(-12f, 8f, -34f), Quaternion.Euler(25f, 45f, 0f));

            BenchBuilder.SaveNetworkScene(scene, ScenePath, false);
        }

        // Local frame: +Z forward. Chassis floor top at local y 0.4. Cab z +1.5..+3.0 (open sides), bay z -3.0..+1.5 with
        // 1.2 m walls, a 1.5 m side door on the left of the bay (hinge at its front edge, opens outward), open top.
        // Wheel centres at y 0 at (+-1.0, +-2.2); resting ride height about 0.56 m above ground.
        static BenchCarrier BuildCarrier(Vector3 at)
        {
            var root = new GameObject("Carrier");
            root.transform.position = at;
            var t = root.transform;
            var rb = root.AddComponent<Rigidbody>();
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            root.AddComponent<NetworkObject>();
            var nt = root.AddComponent<NetworkTransform>();
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            root.AddComponent<NetworkRigidbody>();

            BenchBuilder.LocalBox("Chassis", t, new Vector3(0f, 0.3f, 0f), new Vector3(2.2f, 0.2f, 6.0f));
            BenchBuilder.LocalBox("Cab_Front", t, new Vector3(0f, 1.3f, 3.0f), new Vector3(2.2f, 1.8f, 0.1f));
            BenchBuilder.LocalBox("Cab_Roof", t, new Vector3(0f, 2.25f, 2.25f), new Vector3(2.2f, 0.1f, 1.5f));
            BenchBuilder.LocalBox("Bulkhead", t, new Vector3(0f, 1.3f, 1.5f), new Vector3(2.2f, 1.8f, 0.1f));
            BenchBuilder.LocalBox("Bay_Right", t, new Vector3(1.05f, 1.0f, -0.75f), new Vector3(0.1f, 1.2f, 4.5f));
            BenchBuilder.LocalBox("Bay_Rear", t, new Vector3(0f, 1.0f, -3.0f), new Vector3(2.2f, 1.2f, 0.1f));
            BenchBuilder.LocalBox("Bay_Left_Front", t, new Vector3(-1.05f, 1.0f, 0.75f), new Vector3(0.1f, 1.2f, 1.5f));
            BenchBuilder.LocalBox("Bay_Left_Back", t, new Vector3(-1.05f, 1.0f, -2.25f), new Vector3(0.1f, 1.2f, 1.5f));

            var hinge = new GameObject("BayDoor_Hinge").transform;
            hinge.SetParent(t, false);
            hinge.localPosition = new Vector3(-1.05f, 0.4f, 0f);
            var panel = BenchBuilder.LocalBox("BayDoor_Panel", hinge, new Vector3(0f, 0.6f, -0.75f), new Vector3(0.05f, 1.2f, 1.48f));
            BenchBuilder.Volume(hinge, BenchCarrier.HandleOut, "door", "Дверь отсека (снаружи)", new Vector3(-0.1f, 0.7f, -1.35f), new Vector3(0.08f, 0.08f, 0.1f));
            BenchBuilder.Volume(hinge, BenchCarrier.HandleIn, "door", "Дверь отсека (изнутри)", new Vector3(0.1f, 0.7f, -1.35f), new Vector3(0.08f, 0.08f, 0.1f));
            var door = hinge.gameObject.AddComponent<NetworkDoor>();
            door.doorId = "BayDoor_L";
            door.openAngle = 80f;
            door.panelColliders = new[] { panel.GetComponent<Collider>() };
            door.vehicleRoot = t;
            door.blockMask = ~(1 << BenchBuilder.InteractLayer());

            // two boarding steps under the bay door: floor ~0.96 m above ground, steps of ~0.32 m (stepOffset 0.35)
            // tops at local -0.24 and 0.08 (ride height ~0.56): ground -> 0.32 -> 0.64 -> floor 0.96, three rises of 0.32 m
            BenchBuilder.LocalBox("Step_Low", t, new Vector3(-1.7f, -0.29f, -0.8f), new Vector3(0.3f, 0.1f, 1.1f));
            BenchBuilder.LocalBox("Step_High", t, new Vector3(-1.3f, 0.03f, -0.8f), new Vector3(0.5f, 0.1f, 1.1f));

            BenchBuilder.LocalBox("Button_BayDoor_Visual", t, new Vector3(-0.3f, 1.4f, 2.93f), new Vector3(0.06f, 0.06f, 0.04f));
            BenchBuilder.Volume(t, BenchCarrier.ButtonBay, "toggle", "Кнопка двери отсека", new Vector3(-0.3f, 1.4f, 2.92f), Vector3.one * 0.05f);
            var engineButton = BenchBuilder.LocalBox("Button_Engine_Visual", t, new Vector3(-0.6f, 1.4f, 2.93f), new Vector3(0.07f, 0.07f, 0.04f));
            engineButton.GetComponent<Renderer>().sharedMaterial = StrapMaterial();
            BenchBuilder.Volume(t, BenchCarrier.ButtonEngine, "toggle", "Двигатель", new Vector3(-0.6f, 1.4f, 2.92f), Vector3.one * 0.05f);

            var seats = new List<SeatAnchor>();
            foreach (var (id, x, role, label) in new[] { ("Seat_P", -0.5f, SeatRole.Pilot, "Место водителя"), ("Seat_C", 0.5f, SeatRole.Passenger, "Место пассажира") })
            {
                BenchBuilder.LocalBox(id + "_Cushion", t, new Vector3(x, 0.6f, 2.3f), new Vector3(0.5f, 0.4f, 0.5f));
                BenchBuilder.Volume(t, id, "seat", label, new Vector3(x, 0.95f, 2.3f), new Vector3(0.25f, 0.2f, 0.25f));
                var a = new GameObject(id).AddComponent<SeatAnchor>();
                a.transform.SetParent(t, false);
                a.seatId = id;
                a.role = role;
                a.pelvis = BenchBuilder.Socket(t, "Socket_" + id + "_Pelvis", new Vector3(x, 0.8f, 2.3f), 0f);
                a.eye = BenchBuilder.Socket(t, "Socket_" + id + "_Eye", new Vector3(x, 1.55f, 2.3f), 0f);
                a.exit = BenchBuilder.Socket(t, "Socket_" + id + "_Exit", new Vector3(x < 0f ? -1.8f : 1.8f, -0.4f, 2.3f), x < 0f ? -90f : 90f);
                a.exitDoor = null;                                    // the cab sides are open
                seats.Add(a);
            }

            var sockets = new List<TieDownSocket>();
            int n = 1;
            foreach (var p in new[] { new Vector3(-0.45f, 0.4f, -0.4f), new Vector3(0.45f, 0.4f, -0.4f), new Vector3(-0.45f, 0.4f, -2.0f), new Vector3(0.45f, 0.4f, -2.0f) })
            {
                var s = BenchBuilder.Socket(t, $"Socket_TieDown_{n:00}", p, 0f).gameObject.AddComponent<TieDownSocket>();
                s.socketId = $"Socket_TieDown_{n:00}";
                var mark = BenchBuilder.LocalBox("Mark", s.transform, new Vector3(0f, 0.005f, 0f), new Vector3(0.12f, 0.01f, 0.12f));
                Object.DestroyImmediate(mark.GetComponent<Collider>());
                sockets.Add(s);
                n++;
            }
            BenchBuilder.Socket(t, "BayAnchor", new Vector3(0f, 0.405f, 0f), 0f);

            var wheels = new List<Transform>();
            foreach (var p in new[] { new Vector3(-1.0f, 0f, 2.2f), new Vector3(1.0f, 0f, 2.2f), new Vector3(-1.0f, 0f, -2.2f), new Vector3(1.0f, 0f, -2.2f) })
            {
                var w = BenchBuilder.Socket(t, $"Wheel_{wheels.Count}", p, 0f);
                var vis = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                vis.name = "WheelVisual";
                Object.DestroyImmediate(vis.GetComponent<Collider>());
                vis.transform.SetParent(w, false);
                vis.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                vis.transform.localScale = new Vector3(0.7f, 0.12f, 0.7f);
                wheels.Add(w);
            }

            var carrier = root.AddComponent<BenchCarrier>();
            carrier.bayDoor = door;
            carrier.seats = seats.ToArray();
            carrier.wheels = wheels.ToArray();
            carrier.interiorCenter = new Vector3(0f, 1.3f, 0f);          // cab and bay above the chassis, inside the walls
            carrier.interiorSize = new Vector3(2.0f, 1.8f, 6.0f);
            return carrier;
        }
    }
}
