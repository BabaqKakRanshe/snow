using System.Collections.Generic;
using System.Linq;
using Snow.Aircraft;
using Snow.Bench;
using Snow.Cargo;
using Snow.Doors;
using Snow.Net;
using Snow.Seats;
using Snow.World;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UtilityPlane;

namespace Snow.EditorTools
{
    /// <summary>
    /// Tools / Snow / Build P03 Plane Bench. The game prefab is a Prefab Variant of the model's UtilityPlane.prefab
    /// (§8.1): Rigidbody (no damping, explicit mass properties at runtime), NetworkObject + server NetworkTransform +
    /// NetworkRigidbody (variant A), four NetworkDoors on the model's door hinges, two seats on the model's sockets,
    /// the 15 tie-downs, AircraftPhysics on the aero/wheel sockets, PlaneVehicle, the presentation and the loadout: the
    /// plane in one piece (user, 16.10) spawns what comes with it (the six cases of cargo_layout.json loose in the bay).
    /// The model itself is not changed. The scene: a long flat field with a runway strip, a windsock, the weather
    /// provider, the plane parked tail-down, and four spawn points by the door.
    /// Idempotent: running it again rebuilds the variant and the scene (the tuning asset is kept).
    /// </summary>
    public static class PlaneBenchBuilder
    {
        public const string PlaneFolder = "Assets/Snow/Plane";
        public const string VariantPath = PlaneFolder + "/UtilityPlane_Game.prefab";
        public const string TuningPath = PlaneFolder + "/AircraftTuning.asset";
        /// <summary>Game variants of the model's loose items (key, ladder, survival kit, winter kit): rebuilt on every run.</summary>
        public const string ItemsFolder = PlaneFolder + "/Items";
        public const string ScenePath = BenchBuilder.Folder + "/P03_PlaneBench.unity";
        const string ModelPrefabPath = "Assets/UtilityPlane/Prefabs/UtilityPlane.prefab";
        static readonly Vector3 PlanePosition = new Vector3(0f, 0f, -600f);

        [MenuItem("Tools/Snow/Build P03 Plane Bench")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(BenchBuilder.Folder)) AssetDatabase.CreateFolder("Assets/Snow", "Bench");
            if (!AssetDatabase.IsValidFolder(PlaneFolder)) AssetDatabase.CreateFolder("Assets/Snow", "Plane");
            AircraftTuning tuning = EnsureTuning();
            GameObject player = BenchBuilder.BuildPlayerPrefab();
            NetworkPrefabsList list = BenchBuilder.EnsurePrefabList(player);
            // what comes with the plane: network prefabs first, the variant's loadout refers to them
            var cargo = new List<GameObject>();
            if (!AssetDatabase.IsValidFolder(PhysicsBenchBuilder.CargoFolder)) AssetDatabase.CreateFolder(BenchBuilder.Folder, "Cargo");
            for (int i = 1; i <= 6; i++)
            {
                GameObject c = AssetDatabase.LoadAssetAtPath<GameObject>($"{PhysicsBenchBuilder.CargoFolder}/Cargo_Case{i}_Net.prefab")
                               ?? PhysicsBenchBuilder.BuildCargoPrefab(i, PhysicsBenchBuilder.Masses[i - 1]);
                if (c == null) continue;
                cargo.Add(c);
                if (!list.PrefabList.Any(x => x.Prefab == c)) list.Add(new NetworkPrefab { Prefab = c });
            }
            if (!AssetDatabase.IsValidFolder(ItemsFolder)) AssetDatabase.CreateFolder(PlaneFolder, "Items");
            var items = new Dictionary<string, GameObject>();
            void Item(GameObject prefab)
            {
                if (prefab == null) return;
                items[prefab.name] = prefab;
                if (!list.PrefabList.Any(x => x.Prefab == prefab)) list.Add(new NetworkPrefab { Prefab = prefab });
            }
            // И2 E.4: the key, TEMPORARY 0.05 kg
            Item(NetItem("PlaneKey", "PlaneKey_Net", 0.05f, "Ключ от самолёта", go => go.AddComponent<PlaneKey>()));
            // И3 H.1: the step ladder, TEMPORARY 8 kg; L.2: the survival kit, masses from Data/survival_kit.json (TEMPORARY)
            Item(NetItem("StepLadder", "StepLadder_Net", 8f, "Стремянка", go => go.AddComponent<StepLadderItem>()));
            Item(NetItem("Surv_Bag", "Surv_Bag_Net", 6f, "Аварийная сумка", go =>
            {
                Transform strap = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "Surv_Bag_Strap");
                if (strap != null) { go.GetComponent<CargoItem>().stowedMarker = strap.gameObject; strap.gameObject.SetActive(false); }
            }));
            // user decision 16.10: the kit works at a basic level (SurvivalItem)
            System.Action<GameObject> Kind(SurvivalKind k) => go => go.AddComponent<SurvivalItem>().kind = k;
            Item(NetItem("Surv_Flashlight", "Surv_Flashlight_Net", 0.45f, "Фонарь", Kind(SurvivalKind.Flashlight)));
            Item(NetItem("Surv_FlareGun", "Surv_FlareGun_Net", 0.6f, "Ракетница", Kind(SurvivalKind.FlareGun)));
            Item(NetItem("Surv_FlareShell", "Surv_FlareShell_Net", 0.05f, "Патрон к ракетнице, 26.5 мм", Kind(SurvivalKind.FlareShell)));
            Item(NetItem("Surv_Flare", "Surv_Flare_Net", 0.3f, "Фальшфейер", Kind(SurvivalKind.Flare)));
            Item(NetItem("Surv_Shovel", "Surv_Shovel_Net", 1.5f, "Лопата", Kind(SurvivalKind.Shovel)));
            // И4 K4.2: game variants of the winter-kit carry forms (Prefabs/Kit/KitCarry_*, made by Install Winter Kit)
            foreach (var (key, label) in KitLabels)
            {
                var src = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/UtilityPlane/Prefabs/Kit/KitCarry_{key}.prefab");
                if (src == null) continue;
                float mass = src.GetComponent<UtilityPlaneKitItem>().TotalMassKg;
                Item(NetItem($"Kit/KitCarry_{key}", $"KitCarry_{key}_Net", mass, label, go => go.AddComponent<KitCarry>()));
            }
            Item(BuildBarrel());
            GameObject variant = BuildVariant(tuning, cargo, items);
            if (variant == null) return;
            if (!list.PrefabList.Any(x => x.Prefab == variant)) list.Add(new NetworkPrefab { Prefab = variant });
            EditorUtility.SetDirty(list);
            AssetDatabase.SaveAssets();
            BuildScene(variant, player, list);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Snow] P03 plane bench built: {VariantPath} (plane in one piece: {variant.GetComponent<PlaneLoadout>().items.Length} loadout items), {ScenePath}, tuning {TuningPath}");
        }

        static AircraftTuning EnsureTuning()
        {
            var t = AssetDatabase.LoadAssetAtPath<AircraftTuning>(TuningPath);
            if (t != null) return t;
            t = ScriptableObject.CreateInstance<AircraftTuning>();
            AssetDatabase.CreateAsset(t, TuningPath);
            return t;
        }

        // ------------------------------------------------------------------ prefab variant
        static GameObject BuildVariant(AircraftTuning tuning, List<GameObject> cargo, Dictionary<string, GameObject> items)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPrefabPath);
            if (model == null)
            {
                Debug.LogError($"[Snow] {ModelPrefabPath} missing: run Tools/Utility Plane/Build Materials + Prefabs first");
                return null;
            }
            var root = (GameObject)PrefabUtility.InstantiatePrefab(model);
            root.name = "UtilityPlane_Game";
            Transform t = root.transform;
            Transform F(string n) => t.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == n);
            int interact = BenchBuilder.InteractLayer();
            LayerMask solid = ~(1 << interact);

            var rb = root.AddComponent<Rigidbody>();
            rb.mass = tuning.emptyMassKg;
            rb.linearDamping = 0f;                                    // §8.1: default damping is a hidden stabiliser
            rb.angularDamping = 0f;
            rb.automaticCenterOfMass = false;
            rb.automaticInertiaTensor = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            root.AddComponent<NetworkObject>();
            var nt = root.AddComponent<NetworkTransform>();
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            nt.Interpolate = true;
            root.AddComponent<NetworkRigidbody>();

            var physics = root.AddComponent<AircraftPhysics>();
            physics.tuning = tuning;
            physics.body = rb;
            physics.wingLIn = F("Socket_Aero_Wing_L_In"); physics.wingLOut = F("Socket_Aero_Wing_L_Out");
            physics.wingRIn = F("Socket_Aero_Wing_R_In"); physics.wingROut = F("Socket_Aero_Wing_R_Out");
            physics.hStabL = F("Socket_Aero_HStab_L"); physics.hStabR = F("Socket_Aero_HStab_R");
            physics.fin = F("Socket_Aero_Fin"); physics.fuselage = F("Socket_Aero_Fuselage"); physics.thrustPoint = F("Socket_Thrust");
            physics.wheelL = F("Socket_WheelContact_L"); physics.wheelR = F("Socket_WheelContact_R"); physics.wheelTail = F("Socket_WheelContact_Tail");
            physics.groundMask = solid;

            // doors on the model's hinges (UNITY_CONTRACT §3 signs), panels = COL_<Door>
            NetworkDoor Door(string name, float angle, float powered)
            {
                Transform hinge = F(name);
                var d = hinge.gameObject.AddComponent<NetworkDoor>();
                d.doorId = name;
                d.openAngle = angle;
                d.axis = Vector3.up;
                d.poweredTravelTime = powered;
                d.manualTravelTime = tuning.manualDoorTime;
                d.panelColliders = hinge.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger).ToArray();
                d.vehicleRoot = t;
                d.blockMask = solid;
                return d;
            }
            NetworkDoor pilotL = Door("PilotDoor_L", 70f, tuning.cabinDoorTime);
            NetworkDoor pilotR = Door("PilotDoor_R", -70f, tuning.cabinDoorTime);
            NetworkDoor cargoL = Door("CargoDoor_L", -100f, tuning.cargoDoorTime);
            NetworkDoor cargoR = Door("CargoDoor_R", 100f, tuning.cargoDoorTime);

            SeatAnchor Seat(string id, SeatRole role, string pelvis, string eye, string exit, NetworkDoor door)
            {
                var a = new GameObject("SeatAnchor_" + id).AddComponent<SeatAnchor>();
                a.transform.SetParent(t, false);
                a.seatId = id;
                a.role = role;
                a.pelvis = F(pelvis);
                a.eye = F(eye);
                a.exit = F(exit);
                a.exitDoor = door;
                return a;
            }
            SeatAnchor Pax(string key, string altKey)
            {
                // И3 K.1: troop seats of the bay (Data/pax_seats.json), getting up inside onto the bay floor
                SeatAnchor a = Seat("PaxSeat_" + key, SeatRole.Passenger, "Socket_Seat_Pax" + key, "Socket_PaxEye_" + key, "Socket_PaxExit_" + key, null);
                a.inside = true;
                a.altExit = F("Socket_PaxExit_" + altKey);
                return a;
            }
            var seats = new[]
            {
                Seat("Seat_P", SeatRole.Pilot, "Socket_Seat_P", "Socket_PilotEye", "Socket_Exit_L", pilotL),
                Seat("Seat_C", SeatRole.Passenger, "Socket_Seat_C", "Socket_CopilotEye", "Socket_Exit_R", pilotR),
                Pax("L1", "L2"), Pax("R1", "R2"), Pax("L2", "L1"), Pax("R2", "R1"),
            };

            for (int i = 1; i <= 15; i++)
            {
                Transform s = F($"Socket_TieDown_{i:00}");
                if (s == null) continue;
                var tie = s.gameObject.AddComponent<TieDownSocket>();
                tie.socketId = s.name;
                tie.captureRadius = 0.45f;                            // v5 rails: X −0.42 / 0 / +0.42, Z +1.45 … −1.10: every floor point is within 0.41 m of a socket
            }

            // И3 L.1: stow points on the model's Socket_Stow_* (Data/stowage.json): full pose, one item each
            var stows = new List<StowPoint>();
            foreach (var (id, accepts) in new[] { ("Flashlight", "Surv_Flashlight"), ("FlareGun", "Surv_FlareGun"), ("Shovel", "Surv_Shovel"),
                                                 ("SurvivalBag", "Surv_Bag"), ("JerryCan", "KitCarry_JerryCan"), ("Ladder", "StepLadder") })
            {
                Transform sock = F("Socket_Stow_" + id);
                if (sock == null) { Debug.LogWarning($"[Snow] Socket_Stow_{id} not found"); continue; }
                var sp = sock.gameObject.AddComponent<StowPoint>();
                sp.stowId = id;
                sp.accepts = accepts;
                stows.Add(sp);
            }

            var vehicle = root.AddComponent<PlaneVehicle>();
            vehicle.stowPoints = stows.ToArray();
            vehicle.exitCargoL = F("Socket_Exit_Cargo_L");
            vehicle.paxDownBoxes = PaxBoxes(t, F).ToArray();
            // model_facts.json ladderStowed: floor box and rings under / for the straps
            Vector3 lmin = new Vector3(-0.21f, 1.133f, -1.0922f), lmax = new Vector3(0.6f, 1.7616f, 1.42f);
            vehicle.ladderStowBox = new PlaneVehicle.BoxPose { center = (lmin + lmax) * 0.5f, half = (lmax - lmin) * 0.5f, rotation = Quaternion.identity, seat = -1 };
            vehicle.ladderRings = new[] { "05", "06", "08", "09", "11", "12", "04", "10" }
                .Select(n => F("Socket_TieDown_" + n)).Where(x => x != null).Select(x => x.GetComponent<TieDownSocket>()).ToArray();
            Transform ring13 = F("Socket_TieDown_13");
            vehicle.jerryCanRing = ring13 != null ? ring13.GetComponent<TieDownSocket>() : null;
            vehicle.tuning = tuning;
            vehicle.physics = physics;
            vehicle.pilotDoorL = pilotL; vehicle.pilotDoorR = pilotR; vehicle.cargoDoorL = cargoL; vehicle.cargoDoorR = cargoR;
            vehicle.seats = seats;
            vehicle.centerOfMass = F("Socket_CenterOfMass");
            // cabin + bay inside the fuselage walls (floor 1.07, roof ~2.6, rear bulkhead -1.32, panel ~3.1): *_In handles
            vehicle.interiorCenter = new Vector3(0f, 1.85f, 0.9f);
            vehicle.interiorSize = new Vector3(1.4f, 1.6f, 4.5f);

            // И4: winter kit 1.2 state, zones and consequences (the WinterKit child comes with the model prefab)
            if (root.GetComponentInChildren<UtilityPlaneWinterKit>(true) != null)
            {
                var kitNet = root.AddComponent<PlaneWinterKitNet>();
                kitNet.carryPrefabs = items.Values.Where(x => x.GetComponent<KitCarry>() != null).ToArray();
            }
            else Debug.LogWarning("[Snow] WinterKit missing in UtilityPlane.prefab: run Tools/Utility Plane/Install Winter Kit, then rebuild");

            root.AddComponent<RadioDisplays>().vehicle = vehicle;      // user decision 16.10: standard data on the radio screens
            var presentation = root.AddComponent<PlanePresentation>();
            presentation.vehicle = vehicle;
            presentation.cabinAudio = F("Socket_Audio_Cabin");

            // the plane in one piece (user, 16.10): what comes with it is spawned by the plane itself
            var loadout = root.AddComponent<PlaneLoadout>();
            loadout.groundMask = solid;
            loadout.items = Loadout(cargo, items).ToArray();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, VariantPath);   // an instance root saves as a Variant
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>
        /// И3 K.3: the pan and leg colliders of each troop seat as boxes in prefab space, in the down pose (the model rests
        /// with the seats down; the Rig folds them only at runtime). The server tests them for cargo before a seat goes down.
        /// </summary>
        static IEnumerable<PlaneVehicle.BoxPose> PaxBoxes(Transform root, System.Func<string, Transform> find)
        {
            for (int i = 0; i < UtilityPlaneRig.PaxSeatNames.Length; i++)
            {
                string key = UtilityPlaneRig.PaxSeatNames[i].Substring("PaxSeat_".Length);
                foreach (string name in new[] { "COL_PaxSeat_" + key, "COL_PaxSeatLeg_" + key })
                {
                    Transform ct = find(name);
                    Collider c = ct != null ? ct.GetComponent<Collider>() : null;
                    if (c == null) { Debug.LogWarning($"[Snow] {name} not found"); continue; }
                    Bounds b = c is BoxCollider bc ? new Bounds(bc.center, bc.size)
                             : c is MeshCollider mc && mc.sharedMesh != null ? mc.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one * 0.1f);
                    Vector3 scale = ct.lossyScale;
                    yield return new PlaneVehicle.BoxPose
                    {
                        center = root.InverseTransformPoint(ct.TransformPoint(b.center)),
                        half = Vector3.Scale(b.extents, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z))) * 0.95f,
                        rotation = Quaternion.Inverse(root.rotation) * ct.rotation,
                        seat = i,
                    };
                }
            }
        }

        // cargo_layout.json (plane space), loose in the bay; stacks are put on the real top (audit N5)
        static List<PlaneLoadout.Entry> Loadout(List<GameObject> cargo, Dictionary<string, GameObject> items)
        {
            GameObject I(string n) => items.TryGetValue(n, out var g) ? g : null;
            PlaneLoadout.Entry Ground(string item, Vector3 offset, float yaw) =>
                new PlaneLoadout.Entry { prefab = I(item), place = PlaneLoadout.Place.Ground, socket = "Socket_Ladder_Fuel_L", offset = offset, yaw = yaw };
            PlaneLoadout.Entry Kit(PlaneLoadout.Entry e, int sets, float litres) { e.setKit = true; e.kitSets = sets; e.kitFuelLitres = litres; return e; }
            PlaneLoadout.Entry Stow(string item, string stowId) => new PlaneLoadout.Entry { prefab = I(item), place = PlaneLoadout.Place.Stow, socket = stowId };
            GameObject C(int n) => cargo.FirstOrDefault(c => c.name == $"Cargo_Case{n}_Net");
            PlaneLoadout.Entry OnGround(int n, Vector3 offset) =>
                new PlaneLoadout.Entry { prefab = C(n), place = PlaneLoadout.Place.Ground, socket = "Socket_Exit_Cargo_L", offset = offset };
            return new List<PlaneLoadout.Entry>
            {
                // user decision 16.10: the ladder lives stowed in the bay, so the bench cases (cargo_layout.json would overlap
                // it) wait on the ground by the left cargo door, ready to be loaded (TEMPORARY bench content)
                OnGround(1, new Vector3(-2.2f, 0f, 0.9f)), OnGround(2, new Vector3(-2.2f, 0f, 0.0f)), OnGround(5, new Vector3(-2.2f, 0f, -0.9f)),
                OnGround(3, new Vector3(-3.1f, 0f, 0.9f)), OnGround(4, new Vector3(-3.1f, 0f, 0.0f)), OnGround(6, new Vector3(-3.1f, 0f, -0.9f)),
                // И2: the key starts in the host's pocket, the plane unlocked (TEMPORARY, §8.3)
                new PlaneLoadout.Entry { prefab = I("PlaneKey_Net"), place = PlaneLoadout.Place.HostPocket },
                // user decision 16.10: the ladder starts stowed in the bay (troop seats 2 and 4 folded)
                Stow("StepLadder_Net", "Ladder"),
                // И3 L TEMPORARY: the survival kit in its places; flare and shell (no place of their own) loose on the bay floor
                Stow("Surv_Flashlight_Net", "Flashlight"), Stow("Surv_FlareGun_Net", "FlareGun"), Stow("Surv_Shovel_Net", "Shovel"),
                Stow("Surv_Bag_Net", "SurvivalBag"),
                new PlaneLoadout.Entry { prefab = I("Surv_Flare_Net"), place = PlaneLoadout.Place.Bay, offset = new Vector3(0.25f, 1.10f, 1.30f), yaw = 90f },
                new PlaneLoadout.Entry { prefab = I("Surv_FlareShell_Net"), place = PlaneLoadout.Place.Bay, offset = new Vector3(0.25f, 1.10f, 1.10f), yaw = 90f },
                // И4 K4.6 TEMPORARY (where the kit lives is OPEN): at the left wing on the ground the heater, the empty
                // tie-down bag (all three sets are on the plane) and two full jerry cans; a fuel drum to fill them (K4.5)
                Ground("KitCarry_HeaterStowed_Net", new Vector3(-1.6f, 0f, -0.6f), 90f),
                Kit(Ground("KitCarry_TieDownBag_Net", new Vector3(-1.6f, 0f, 0.4f), 90f), 0, 0f),
                Kit(Ground("KitCarry_JerryCan_Net", new Vector3(-0.9f, 0f, 1.2f), 90f), 1, 20f),
                Kit(Ground("KitCarry_JerryCan_Net", new Vector3(-0.5f, 0f, 1.2f), 90f), 1, 20f),
                Ground("FuelBarrel_Net", new Vector3(-3.5f, 0f, 1.5f), 0f),
            }.Where(e => e.prefab != null).ToList();
        }

        /// <summary>
        /// A game variant of one of the model's loose items, built like the cargo cases (§12, И2 E.4 / И3 L.2): the
        /// model's prefab unpacked (the model's menu rebuilds its prefabs, the game copy is rebuilt here every run, same
        /// path = same GUID), with a Rigidbody, a server NetworkTransform + NetworkRigidbody and a CargoItem (carried by
        /// grab). The model prefab itself is not changed.
        /// </summary>
        static readonly (string key, string label)[] KitLabels =
        {
            ("IntakePlug", "Заглушка воздухозаборника"), ("ExhaustCover", "Заглушка выхлопа"), ("PitotCover", "Чехол ПВД"),
            ("WingCoverBag", "Чехол крыла"), ("WindshieldCoverBag", "Чехол лобового стекла"), ("EngineCoverBag", "Утеплённый чехол двигателя"),
            ("Chocks", "Колодки"), ("ChocksTail", "Колодки хвостового колеса"), ("TieDownBag", "Сумка швартовки"),
            ("HeaterStowed", "Подогреватель двигателя"), ("JerryCan", "Канистра 20 л"), ("Funnel", "Воронка с фильтром"),
        };

        /// <summary>
        /// TEMPORARY (K4.5, where to fill the jerry can is OPEN): a fuel drum as a network object — a stand-in cylinder of
        /// drum size, not a model asset — with one zone Barrel_Fill on the Interact layer.
        /// </summary>
        static GameObject BuildBarrel()
        {
            var go = new GameObject("FuelBarrel_Net");
            var body = go.transform;
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            visual.name = "Drum";
            visual.transform.SetParent(body, false);
            visual.transform.localPosition = new Vector3(0f, 0.44f, 0f);
            visual.transform.localScale = new Vector3(0.58f, 0.44f, 0.58f);
            visual.GetComponent<Renderer>().sharedMaterial = Material("M_FuelDrum", new Color(0.55f, 0.12f, 0.08f));
            go.AddComponent<NetworkObject>();
            go.AddComponent<FuelBarrel>();
            var zone = new GameObject("IV_Barrel_Fill") { layer = BenchBuilder.InteractLayer() };
            zone.transform.SetParent(body, false);
            zone.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            var box = zone.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(0.5f, 0.12f, 0.5f);
            var iv = zone.AddComponent<UtilityPlaneInteractable>();
            iv.id = "Barrel_Fill";
            iv.kind = "barrel";
            iv.label = "Бочка с топливом (TEMPORARY)";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{ItemsFolder}/FuelBarrel_Net.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        internal static GameObject NetItem(string source, string name, float massKg, string label, System.Action<GameObject> extra = null, string folder = ItemsFolder)
        {
            string src = $"Assets/UtilityPlane/Prefabs/{source}.prefab";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(src);
            if (model == null) { Debug.LogWarning($"[Snow] {src} missing: run Tools/Utility Plane/Build Materials + Prefabs"); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = name;
            if (!go.TryGetComponent(out Rigidbody rb)) rb = go.AddComponent<Rigidbody>();   // not ?? : Unity's fake null
            rb.mass = massKg;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            go.AddComponent<NetworkObject>();
            var nt = go.AddComponent<NetworkTransform>();
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            go.AddComponent<NetworkRigidbody>();
            var item = go.AddComponent<CargoItem>();
            item.label = label;
            item.massKg = massKg;
            item.itemKey = System.IO.Path.GetFileName(source);         // what stow points accept (Data/stowage.json prefab)
            extra?.Invoke(go);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{folder}/{name}.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        // ------------------------------------------------------------------ scene
        static void BuildScene(GameObject variant, GameObject player, NetworkPrefabsList list)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            BenchBuilder.Box("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(6000f, 1f, 6000f), 0f);
            var runway = BenchBuilder.Box("Runway", new Vector3(0f, 0.005f, 0f), new Vector3(30f, 0.01f, 1500f), 0f);
            runway.GetComponent<Renderer>().sharedMaterial = Material("M_Runway", new Color(0.18f, 0.18f, 0.2f));
            Object.DestroyImmediate(runway.GetComponent<Collider>());   // visual only: the ground is the surface
            for (int i = 0; i < 25; i++)
            {
                var mark = BenchBuilder.Box($"Centreline_{i}", new Vector3(0f, 0.012f, -700f + 60f * i), new Vector3(0.6f, 0.01f, 25f), 0f, runway.transform.parent);
                mark.GetComponent<Renderer>().sharedMaterial = Material("M_Marking", new Color(0.9f, 0.9f, 0.85f));
                Object.DestroyImmediate(mark.GetComponent<Collider>());
            }

            var envGo = new GameObject("Environment");
            envGo.AddComponent<NetworkObject>();
            envGo.AddComponent<EnvironmentProvider>();
            BuildWindsock(new Vector3(-25f, 0f, -560f));

            // parked tail-down on all three wheels (model v5+): root 0.372 m under the ground, nose up 9.98° (UNITY_CONTRACT §1)
            var plane = (GameObject)PrefabUtility.InstantiatePrefab(variant);
            Pose parked = UtilityPlaneRig.ParkedPose(PlanePosition, 0f);
            plane.transform.SetPositionAndRotation(parked.position, parked.rotation);

            var spawns = new List<Transform>();
            var spawnRoot = new GameObject("SpawnPoints").transform;
            for (int i = 0; i < SnowNetworkBootstrap.MaxPlayers; i++)
            {
                var sp = new GameObject($"Spawn_{i}").transform;
                sp.SetParent(spawnRoot, false);
                sp.position = PlanePosition + new Vector3(-3.5f - 1.2f * i, 0f, 1.5f);
                sp.rotation = Quaternion.Euler(0f, 90f, 0f);           // facing the pilot door
                spawns.Add(sp);
            }
            BenchBuilder.CreateNetworkManager(player, list, spawns.ToArray());


            var mainCam = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).FirstOrDefault();
            if (mainCam != null)
                mainCam.transform.SetPositionAndRotation(PlanePosition + new Vector3(-14f, 6f, -10f), Quaternion.Euler(18f, 50f, 0f));

            BenchBuilder.SaveNetworkScene(scene, ScenePath, false);
        }

        static void BuildWindsock(Vector3 at)
        {
            var root = new GameObject("Windsock");
            root.transform.position = at;
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "Pole";
            pole.transform.SetParent(root.transform, false);
            pole.transform.localPosition = new Vector3(0f, 2.5f, 0f);
            pole.transform.localScale = new Vector3(0.12f, 2.5f, 0.12f);
            var pivot = new GameObject("Sock").transform;
            pivot.SetParent(root.transform, false);
            pivot.localPosition = new Vector3(0f, 5f, 0f);
            var sock = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            sock.name = "SockCloth";
            Object.DestroyImmediate(sock.GetComponent<Collider>());
            sock.transform.SetParent(pivot, false);
            sock.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            sock.transform.localPosition = new Vector3(0f, 0f, 1.1f);
            sock.transform.localScale = new Vector3(0.45f, 1.1f, 0.45f);
            sock.GetComponent<Renderer>().sharedMaterial = Material("M_Windsock", new Color(1f, 0.45f, 0.05f));
            root.AddComponent<Windsock>().sock = pivot;
        }

        static Material Material(string name, Color color)
        {
            string path = $"{PlaneFolder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }
    }
}
