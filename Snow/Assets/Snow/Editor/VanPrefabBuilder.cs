using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Snow.Doors;
using Snow.Seats;
using Snow.Vehicles.Van;
using UazVan;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;
using UtilityPlane;
using Object = UnityEngine.Object;

namespace Snow.EditorTools
{
    /// <summary>
    /// Tools / Snow / Van / Build Van Prefab (VAN3_INTEGRATION_UNITY.md В1.2, В1.4). The game prefab is a Prefab Variant of
    /// the package's Assets/Van/Prefabs/Van.prefab: Rigidbody (§4.1), NetworkObject + server NetworkTransform +
    /// NetworkRigidbody (variant A, as the plane), VanVehicle, VanPresentation, 8 SeatAnchors from Data/seats.json, NetworkDoors
    /// on the five door hinges (axis and angle from Data/parts.json, panels = the COL_* under the hinge) and a
    /// UtilityPlaneInteractable next to every VanInteractable (same id / kind / label: the game's InteractionHost and sensor
    /// see it). The 43 kit items become NetItems (the plane's recipe, CargoItem.itemKey = items.json type). The package's
    /// files are not changed. Idempotent: every run rebuilds the variant and the items at the same paths (same GUIDs);
    /// the tuning asset is kept.
    /// </summary>
    public static class VanPrefabBuilder
    {
        public const string Folder = "Assets/Snow/Van";
        public const string VariantPath = Folder + "/VanVehicle.prefab";
        public const string TuningPath = Folder + "/VanTuning.asset";
        public const string ItemsFolder = Folder + "/Items";
        const string ModelPrefabPath = "Assets/Van/Prefabs/Van.prefab";
        const string DataFolder = "Assets/Van/Data/";
        public static readonly string[] DoorIds = { "doorL", "doorP", "doorSalon", "doorZL", "dooZP" };

        /// <summary>
        /// The 68 zones in the game (user decisions 21.10, docs/dev/Van/ZonesReview.md: the list, then the belt out and the tool box lid in for the spare fuses). A merged group is kept as its first
        /// zone (MergedLabels); the rest of the group and every other zone of the package is switched off.
        /// </summary>
        public static readonly HashSet<string> Keep = new HashSet<string>(StringComparer.Ordinal)
        {
            "doorL", "doorP", "doorSalon", "doorZL", "dooZP",
            "Ignition_Key", "Handbrake", "TransferLever_Axle", "TransferLever_Range", "Horn_Button",
            "Toggle_Head", "Toggle_Fog", "Toggle_Spot", "Toggle_Roof", "Toggle_Work", "Toggle_Cabin", "Toggle_Stove", "Toggle_Wiper",
            "Glovebox_Lid", "FuseBox_Lid", "Heater_Fan", "Radio_Volume",
            "BatteryBox_Lid", "MassSwitch_Key", "Battery_ClampPos", "Battery",
            "Fuse_1", "Fuse_2", "Fuse_3", "Fuse_4", "Fuse_5", "Fuse_6", "Fuse_7", "Fuse_8", "Spare_Fuse_1", "Spare_Fuse_2", "Spare_Fuse_3",
            "EngineCover", "Engine_Lead_1", "Engine_DistributorCap", "ToolBox_Lid",
            "FuelFlap_L", "FuelFlap_R",
            "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR", "Jack_Point_FL", "Jack_Point_FR", "Jack_Point_RL", "Jack_Point_RR",
            "Jack_Screw", "Jack_Take", "SpareWheel", "Winch_Clutch", "Winch_Hook",
            "Sit_Driver", "Sit_Passenger", "Sit_L1", "Sit_L2", "Sit_L3", "Sit_L4", "Sit_R1", "Sit_R2", "Seat_L4", "Curtain_L1A",
            "Jerrycan_1", "Jerrycan_2",
        };

        /// <summary>Merged groups: the kept zone and the label of the whole group (its members are off).</summary>
        public static readonly Dictionary<string, string> MergedLabels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Battery_ClampPos", "Клеммы АКБ" },                  // + Battery_ClampNeg
            { "Engine_Lead_1", "Провода свечей" },                  // + Engine_Lead_2…4
            { "FuelFlap_L", "Левый бак" },                          // + FuelCap_L, Pour_Fuel_L
            { "FuelFlap_R", "Правый бак" },                         // + FuelCap_R, Pour_Fuel_R
            { "Jack_Screw", "Домкрат: поднять / опустить" },        // + Jack_PumpSocket
            { "SpareWheel", "Запаска" },                            // + SpareWheel_Nut
            { "Curtain_L1A", "Шторки" },                            // + the other 11 curtains
            { "Wheel_FL", "Колесо" }, { "Wheel_FR", "Колесо" }, { "Wheel_RL", "Колесо" }, { "Wheel_RR", "Колесо" },   // + 6 nuts each
        };

        [MenuItem("Tools/Snow/Van/Build Van Prefab")]
        public static void BuildMenu() => Build();

        internal static GameObject Build()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Snow", "Van");
            if (!AssetDatabase.IsValidFolder(ItemsFolder)) AssetDatabase.CreateFolder(Folder, "Items");
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(BenchBuilder.PlayerPrefabPath);
            if (player == null)
            {
                Debug.LogError($"[Snow] {BenchBuilder.PlayerPrefabPath} missing: build the P01 bench first");
                return null;
            }
            VanTuning tuning = EnsureTuning();
            NetworkPrefabsList list = BenchBuilder.EnsurePrefabList(player);
            int items = BuildItems(list);
            GameObject variant = BuildVariant(tuning);
            if (variant == null) return null;
            if (!list.PrefabList.Any(x => x.Prefab == variant)) list.Add(new NetworkPrefab { Prefab = variant });
            EditorUtility.SetDirty(list);
            AssetDatabase.SaveAssets();
            var v = variant.GetComponent<VanVehicle>();
            Debug.Log($"[Snow] Van prefab built: {VariantPath} ({v.seats.Length} seats, {v.doors.Length} doors, " +
                      $"{variant.GetComponentsInChildren<UtilityPlaneInteractable>(true).Length} zones), {items} items in {ItemsFolder}, tuning {TuningPath}");
            return variant;
        }

        static VanTuning EnsureTuning()
        {
            var t = AssetDatabase.LoadAssetAtPath<VanTuning>(TuningPath);
            if (t != null) return t;
            t = ScriptableObject.CreateInstance<VanTuning>();
            AssetDatabase.CreateAsset(t, TuningPath);
            return t;
        }

        // ------------------------------------------------------------------ data (Assets/Van/Data, JsonUtility)
        [Serializable] class PartsFile { public PartJ[] parts; }
        [Serializable] class PartJ { public string name, kind, blendshape, flatBlendshape; public float[] rotAxis; public float rest, min, max; }
        [Serializable] class SeatsFile { public SeatJ[] seats; }
        [Serializable] class SeatJ { public string name, role, parent, door; public float[] hipLocal, eye, facing, exit; }
        [Serializable] class ItemsFile { public TypeJ[] types; }
        [Serializable] class TypeJ { public string type, label, prefab; public float massKg; }
        [Serializable] class FactsFile { public float[] interiorCenter, interiorSize; }

        static T Data<T>(string file) => JsonUtility.FromJson<T>(File.ReadAllText(DataFolder + file));
        static Vector3 V(float[] a) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;

        // ------------------------------------------------------------------ items (В1.4)
        static int BuildItems(NetworkPrefabsList list)
        {
            int made = 0;
            foreach (TypeJ t in Data<ItemsFile>("items.json").types)
            {
                GameObject p = PlaneBenchBuilder.NetItemFrom($"Assets/Van/Prefabs/Items/{t.prefab}.prefab", t.type,
                                                            t.prefab + "_Net", t.massKg, t.label, null, ItemsFolder);
                if (p == null) continue;
                made++;
                if (!list.PrefabList.Any(x => x.Prefab == p)) list.Add(new NetworkPrefab { Prefab = p });
            }
            return made;
        }

        /// <summary>The NetItem prefab of an items.json type (null if not built).</summary>
        public static GameObject ItemPrefab(string type) => AssetDatabase.LoadAssetAtPath<GameObject>($"{ItemsFolder}/VanItem_{type}_Net.prefab");

        // ------------------------------------------------------------------ prefab variant (В1.2)
        static GameObject BuildVariant(VanTuning tuning)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPrefabPath);
            if (model == null)
            {
                Debug.LogError($"[Snow] {ModelPrefabPath} missing: run Tools/UAZ Van/Build Materials + Prefab first");
                return null;
            }
            var root = (GameObject)PrefabUtility.InstantiatePrefab(model);
            root.name = "VanVehicle";
            Transform t = root.transform;
            var byName = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (var x in t.GetComponentsInChildren<Transform>(true))
                if (!byName.ContainsKey(x.name)) byName[x.name] = x;
            Transform F(string n) => !string.IsNullOrEmpty(n) && byName.TryGetValue(n, out var x) ? x : null;
            int interact = BenchBuilder.InteractLayer();
            LayerMask solid = ~(1 << interact);

            var rb = root.AddComponent<Rigidbody>();
            rb.mass = tuning.massKg;
            rb.linearDamping = 0f;                                    // §4.1: no hidden stabiliser
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

            int shapes = FixBlendShapes(root.GetComponent<VanRig>(), Data<PartsFile>("parts.json").parts);
            if (shapes != 7) Debug.LogWarning($"[Snow] van blend shapes: {shapes} of 7 found");

            // zones: the game's marker next to the package's (§3.3); kind and side stay readable on VanInteractable.
            // User decision 21.10 (docs/dev/Van/ZonesReview.md): only the Keep zones are in the game for now; the others are
            // switched off here (the package stays as it is), a merged group is its first zone with a common label.
            int kept = 0;
            foreach (var vi in t.GetComponentsInChildren<VanInteractable>(true))
            {
                bool keep = Keep.Contains(vi.id);
                vi.gameObject.SetActive(keep);
                if (!keep) continue;
                if (!vi.TryGetComponent(out UtilityPlaneInteractable u)) u = vi.gameObject.AddComponent<UtilityPlaneInteractable>();
                u.id = vi.id;
                u.kind = vi.kind;
                u.label = MergedLabels.TryGetValue(vi.id, out string merged) ? merged : vi.label;
                kept++;
            }
            if (kept != Keep.Count) Debug.LogWarning($"[Snow] van zones: {kept} of {Keep.Count} kept zones found");

            // doors on the hinges (§5 В1.2): axis = rotAxis, angle = max of parts.json; one writer: NetworkDoor (audit R1)
            var parts = Data<PartsFile>("parts.json").parts.ToDictionary(p => p.name, StringComparer.Ordinal);
            var doors = new List<NetworkDoor>();
            foreach (string id in DoorIds)
            {
                Transform hinge = F(id);
                if (hinge == null || !parts.TryGetValue(id, out PartJ p)) { Debug.LogWarning($"[Snow] van door {id} not found"); continue; }
                var d = hinge.gameObject.AddComponent<NetworkDoor>();
                d.doorId = id;
                d.openAngle = p.max - p.rest;
                d.axis = V(p.rotAxis);
                d.panelColliders = hinge.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger).ToArray();
                d.vehicleRoot = t;
                d.blockMask = solid;
                doors.Add(d);
            }
            NetworkDoor DoorOf(string id) => doors.FirstOrDefault(d => d.doorId == id);

            // seats (seats.json): pelvis at the hip on the seat part (troop seats fold with it), eye and exit in van space
            var seats = new List<SeatAnchor>();
            SeatJ[] seatData = Data<SeatsFile>("seats.json").seats;
            foreach (SeatJ s in seatData)
            {
                var a = new GameObject("SeatAnchor_" + s.name).AddComponent<SeatAnchor>();
                a.transform.SetParent(t, false);
                a.seatId = s.name;                                     // = the zone id Sit_*
                a.role = s.role == "Driver" ? SeatRole.Pilot : SeatRole.Passenger;
                Quaternion facing = Quaternion.LookRotation(V(s.facing), Vector3.up);
                Transform seatPart = F(s.parent);
                a.pelvis = Child(seatPart != null ? seatPart : a.transform, "Pelvis", V(s.hipLocal), facing);
                a.eye = Child(a.transform, "Eye", V(s.eye), facing);
                a.exit = Child(a.transform, "Exit", V(s.exit), facing);
                a.inside = s.role == "Saloon";
                a.exitDoor = a.inside ? null : DoorOf(s.door);
                seats.Add(a);
            }
            // inside seats: the neighbour's exit when the own one is taken (SeatedVehicle.ServerInsideExit)
            foreach (var (seat, alt) in new[] { ("L1", "L2"), ("L2", "L1"), ("L3", "L4"), ("L4", "L3"), ("R1", "R2"), ("R2", "R1") })
            {
                SeatAnchor a = seats.FirstOrDefault(x => x.seatId == "Sit_" + seat), b = seats.FirstOrDefault(x => x.seatId == "Sit_" + alt);
                if (a != null && b != null) a.altExit = b.exit;
            }

            var facts = Data<FactsFile>("model_facts.json");
            var vehicle = root.AddComponent<VanVehicle>();
            vehicle.tuning = tuning;
            vehicle.rig = root.GetComponent<VanRig>();
            vehicle.doors = doors.ToArray();
            vehicle.seats = seats.ToArray();
            vehicle.interiorCenter = V(facts.interiorCenter);
            vehicle.interiorSize = V(facts.interiorSize);
            vehicle.itemPrefabs = AssetDatabase.FindAssets("t:Prefab", new[] { ItemsFolder })
                .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(p => p != null && p.GetComponent<Snow.Cargo.CargoItem>() != null).ToArray();
            root.AddComponent<VanPresentation>().vehicle = vehicle;

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, VariantPath);   // an instance root saves as a Variant
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>
        /// PackageIssues P-3: Unity 6000.0 imports the seven meshes with a blend shape (Wheel_* Flat, Snow_Roof / Visor /
        /// Bumper Clear) as MeshFilter + MeshRenderer, so the package's menu finds no SkinnedMeshRenderer and the rig gets
        /// no blend index (flat tyres and melting snow do nothing). In the game variant (the package stays as it is):
        /// the renderer becomes a SkinnedMeshRenderer with the same mesh, materials and shadow settings, and the rig's part
        /// gets it and the shape's index. Returns how many parts have their shape.
        /// </summary>
        static int FixBlendShapes(VanRig rig, PartJ[] data)
        {
            int found = 0;
            var shapeOf = data.Where(p => !string.IsNullOrEmpty(p.blendshape) || !string.IsNullOrEmpty(p.flatBlendshape))
                              .ToDictionary(p => p.name, p => !string.IsNullOrEmpty(p.blendshape) ? p.blendshape : p.flatBlendshape, StringComparer.Ordinal);
            foreach (VanRig.Part part in rig.parts)
            {
                if (part?.t == null || !shapeOf.TryGetValue(part.name, out string shape)) continue;
                if (part.skin == null && part.t.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null && part.t.TryGetComponent(out MeshRenderer mr))
                {
                    Mesh mesh = mf.sharedMesh;
                    Material[] mats = mr.sharedMaterials;
                    var cast = mr.shadowCastingMode;
                    bool receive = mr.receiveShadows;
                    uint layers = mr.renderingLayerMask;
                    Object.DestroyImmediate(mr, true);
                    Object.DestroyImmediate(mf, true);
                    var smr = part.t.gameObject.AddComponent<SkinnedMeshRenderer>();
                    smr.sharedMesh = mesh;
                    smr.sharedMaterials = mats;
                    smr.shadowCastingMode = cast;
                    smr.receiveShadows = receive;
                    smr.renderingLayerMask = layers;
                    smr.localBounds = mesh.bounds;
                    smr.updateWhenOffscreen = false;
                    part.skin = smr;
                }
                if (part.skin != null && part.skin.sharedMesh != null) part.blendIndex = part.skin.sharedMesh.GetBlendShapeIndex(shape);
                if (part.blendIndex >= 0) found++;
                else Debug.LogWarning($"[Snow] van part {part.name}: blend shape {shape} not found");
            }
            return found;
        }

        static Transform Child(Transform parent, string name, Vector3 localPos, Quaternion localRot)
        {
            var c = new GameObject(name).transform;
            c.SetParent(parent, false);
            c.localPosition = localPos;
            c.localRotation = localRot;
            return c;
        }
    }
}
