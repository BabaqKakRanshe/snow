using System;
using System.Collections.Generic;
using System.Linq;
using SkyIsland;
using Snow.Doors;
using Snow.Levels;
using Snow.Net;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UtilityPlane;
using Category = Snow.Levels.SkyIslandLocationHost.Category;
using Object = UnityEngine.Object;

namespace Snow.EditorTools
{
    /// <summary>
    /// Tools / Snow / Integrate BaseV2 Level: makes the main building scene built by the Sky Island core (Tools / Locations /
    /// Location Builder -> BaseV2) a networked level: NetworkObject + SkyIslandLocationHost on the location root, a
    /// NetworkDoor on every door, an interaction zone on every part but the shutter curtains, furniture parts on the
    /// Interact layer, four spawn points in the yard, network manager with the game's player, the player camera look (PS1
    /// frame included). The core builder recreates the scene from scratch, so run this again after every BaseV2 build;
    /// running it twice replaces, not duplicates. Everything changed inside the BaseMain prefab instance stays a scene
    /// override (no Apply to the prefab). The scene goes last in Build Settings (the boot scene is the user's call).
    /// </summary>
    public static class BaseV2LevelBuilder
    {
        public const string ScenePath = "Assets/Locations/BaseV2/Scenes/BaseV2.unity";
        const string RootName = "BaseV2";
        const string LevelRootName = "SnowLevel";
        const string NetworkManagerName = "NetworkManager";
        const string ZonePrefix = "IV_";
        const string BuildHint = "Run Tools / Locations / Location Builder -> BaseV2 -> Build first.";

        // zone boxes: renderer bounds of the part (closed) + pad per side, at least MinZone per axis; doors get a thicker
        // pad across the leaf so the ray meets the zone before the leaf from both sides. 2 cm on every axis would make
        // the stacked drawers' zones overlap.
        const float ZonePad = 0.01f, DoorThicknessPad = 0.02f, MinZone = 0.06f;
        const float DoorTravel = 1.2f, GateTravel = 3f;                // s, TEMPORARY (as the game's other doors)
        // spawn: player capsule (Player.prefab, HLCharacterMotor) and a row across the spawn's right vector
        const float CapsuleRadius = 0.34f, CapsuleHeight = 1.8f, SkinLift = 0.02f;
        static readonly float[] SpawnOffsets = { -1.8f, -0.6f, 0.6f, 1.8f };

        /// <summary>BASEV2_INTEGRATION_UNITY.md, appendix A. Names are prefix + "_" + ...; IX_Shutter_ does not match
        /// IX_ShutterSwitch_ / IX_ShutterWinder_.</summary>
        sealed class Def
        {
            public string prefix, label, kind, s0, s1, a0, a1;
            public Category category;
            public bool gate;
        }

        static Def D(string prefix, Category c, string label, string kind, string s0 = null, string s1 = null, string a0 = null, string a1 = null, bool gate = false) =>
            new Def { prefix = prefix, category = c, label = label, kind = kind, s0 = s0, s1 = s1, a0 = a0, a1 = a1, gate = gate };

        static readonly Def[] Defs =
        {
            D("IX_Door", Category.Door, "Дверь", "door"),
            D("IX_MainDoor", Category.Door, "Входная дверь", "door"),
            D("IX_ExitDoor", Category.Door, "Запасной выход", "door"),
            D("IX_RoofDoor", Category.Door, "Дверь на крышу", "door"),
            D("IX_GarageDoor", Category.Door, "Дверь гаража", "door"),
            D("IX_GarageGate", Category.Door, "Ворота гаража", "door", gate: true),
            D("IX_Drawer", Category.Furniture, "Выдвижной ящик", "pull", "задвинут", "выдвинут", "выдвинуть", "задвинуть"),
            D("IX_RackDoor", Category.Furniture, "Дверца стойки", "hinge", "закрыта", "открыта", "открыть", "закрыть"),
            D("IX_LockerDoor", Category.Furniture, "Дверца шкафчика", "hinge", "закрыта", "открыта", "открыть", "закрыть"),
            D("IX_TumbaDoor", Category.Furniture, "Дверца тумбы", "hinge", "закрыта", "открыта", "открыть", "закрыть"),
            D("IX_FridgeDoor", Category.Furniture, "Холодильник", "hinge", "закрыт", "открыт", "открыть", "закрыть"),
            D("IX_PanelDoor", Category.Furniture, "Дверца щита", "hinge", "закрыта", "открыта", "открыть", "закрыть"),
            D("IX_Breaker", Category.Positional, "Автомат", "toggle", "включён", "выключен", "выключить", "включить"),
            D("IX_Isolator", Category.Positional, "Поворотный выключатель", "toggle", "включён", "выключен", "выключить", "включить"),
            D("IX_MainSwitch", Category.PowerSwitch, "Главный рубильник", "toggle", "выключен", "включён", "включить", "выключить"),
            D("IX_LightSwitch", Category.LightSwitch, "Выключатель света", "toggle"),
            D("IX_ShutterSwitch", Category.ShutterSwitch, "Рольставни", "toggle"),
            D("IX_ShutterWinder", Category.ShutterWinder, "Ремень рольставни", "pull"),
            D("IX_Shutter", Category.Shutter, null, null),
            D("IX_HeatingStop", Category.Positional, "Аварийный выключатель отопления", "toggle", "выключен", "включён", "включить", "выключить"),
        };

        static readonly Dictionary<string, string> RoomLabels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Windfang", "тамбур" }, { "Leitstand", "пульт" }, { "Flur_EG", "коридор 1-го этажа" }, { "Lager", "склад" },
            { "Treppenhaus", "лестница" }, { "Serverraum", "серверная" }, { "Garage", "гараж" }, { "Kaserne", "казарма" },
            { "Kueche", "кухня" }, { "Flur_OG", "коридор 2-го этажа" }, { "Waschraum", "санузел" },
            { "Stationsleiter", "кабинет начальника" }, { "Heizung", "котельная" }, { "Flur_UG", "коридор подвала" },
            { "Schaltraum", "щитовая" }, { "Exterior", "наружный свет" },
        };

        [MenuItem("Tools/Snow/Integrate BaseV2 Level")]
        public static void IntegrateMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Integrate();
        }

        public static void Integrate()
        {
            // 1. the scene the core builder made
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) { Debug.LogError($"[Snow] BaseV2: {ScenePath} not found. {BuildHint}"); return; }
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject rootGo = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
            Transform root = rootGo != null ? rootGo.transform : null;
            Transform camT = root != null ? root.Find("Main Camera") : null;
            Camera sceneCam = camT != null && camT.TryGetComponent(out Camera c) ? c : null;
            Transform spawn = root != null ? root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "PlayerSpawn" && t.CompareTag("Respawn")) : null;
            SkyIslandLightManager lights = null;
            SkyIslandPowerGrid grid = null;
            if (root != null)
            {
                root.TryGetComponent(out lights);
                root.TryGetComponent(out grid);
            }
            SkyIslandInteractable[] all = root != null ? root.GetComponentsInChildren<SkyIslandInteractable>(true) : new SkyIslandInteractable[0];
            var missing = new List<string>();
            if (root == null) missing.Add($"root '{RootName}'");
            if (sceneCam == null) missing.Add($"{RootName}/Main Camera");
            if (spawn == null) missing.Add("PlayerSpawn (tag Respawn)");
            if (lights == null) missing.Add("SkyIslandLightManager on the root");
            if (grid == null) missing.Add("SkyIslandPowerGrid on the root");
            if (all.Length == 0) missing.Add("SkyIslandInteractable parts (core below v2.7?)");
            if (missing.Count > 0) { Debug.LogError($"[Snow] BaseV2: {string.Join(", ", missing)} not found in {ScenePath}. {BuildHint}"); return; }

            var parts = all.OrderBy(p => p.name, StringComparer.Ordinal).ToArray();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in parts)
                if (!names.Add(p.name)) { Debug.LogError($"[Snow] BaseV2: part name '{p.name}' is not unique; zone ids need generated ids here."); return; }
            var defs = new Def[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                defs[i] = DefOf(parts[i].name);
                if (defs[i] == null) { Debug.LogError($"[Snow] BaseV2: no rule for part '{parts[i].name}' (appendix A)."); return; }
                if (defs[i].category == Category.Door && !Mathf.Approximately(parts[i].min, 0f))
                {
                    Debug.LogError($"[Snow] BaseV2: door '{parts[i].name}' has min {parts[i].min}; NetworkDoor opens from 0.");
                    return;
                }
            }

            // 2. the previous result
            foreach (GameObject g in scene.GetRootGameObjects())
                if (g.name == LevelRootName || g.name == NetworkManagerName) Object.DestroyImmediate(g);
            foreach (var p in parts)
                for (int k = p.transform.childCount - 1; k >= 0; k--)
                {
                    Transform ch = p.transform.GetChild(k);
                    if (ch.name.StartsWith(ZonePrefix, StringComparison.Ordinal)) Object.DestroyImmediate(ch.gameObject);
                }

            // 3. network identity and the host on the (static) location root
            if (!rootGo.TryGetComponent(out NetworkObject no)) no = rootGo.AddComponent<NetworkObject>();
            no.SynchronizeTransform = false;
            if (!rootGo.TryGetComponent(out SkyIslandLocationHost host)) host = rootGo.AddComponent<SkyIslandLocationHost>();

            // 4. the fly camera's local test clicks (no network) go; fly camera and PSX stay until a player joins
            if (sceneCam.TryGetComponent(out SkyIslandInteractor interactor)) Object.DestroyImmediate(interactor);

            int interact = BenchBuilder.InteractLayer();
            int doors = 0, zones = 0, synced = 0;
            var hostParts = new SkyIslandLocationHost.Part[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                SkyIslandInteractable part = parts[i];
                Def def = defs[i];
                var hp = new SkyIslandLocationHost.Part { id = part.name, part = part, category = def.category };
                hostParts[i] = hp;

                // 5. doors: NetworkDoor on the hinge object (the part itself); the core part stays but nothing calls it
                if (def.category == Category.Door)
                {
                    hp.door = Door(part, root, def.gate, interact);
                    doors++;
                }
                else
                {
                    synced++;
                    // 7. furniture solids on Interact: the player's motor ignores them (an opened door cannot lock him in),
                    // the use ray still stops at them
                    foreach (Transform t in part.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name.StartsWith(ZonePrefix, StringComparison.Ordinal) || t.gameObject.layer == interact) continue;
                        t.gameObject.layer = interact;
                        if (PrefabUtility.IsPartOfPrefabInstance(t.gameObject)) PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);
                    }
                }

                // 8. host data
                string label = def.label;
                switch (def.category)
                {
                    case Category.LightSwitch:
                        hp.room = LightRoom(lights, part.name);
                        if (hp.room == null) { Debug.LogError($"[Snow] BaseV2: light switch '{part.name}' matches no light manager room."); return; }
                        label = hp.room == "Exterior" ? "Наружный свет" : $"{def.label} ({RoomLabel(hp.room)})";
                        break;
                    case Category.ShutterSwitch:
                    case Category.ShutterWinder:
                        hp.room = ShutterRoom(part.name, def.prefix);
                        if (def.category == Category.ShutterSwitch) label = $"{def.label} ({RoomLabel(hp.room)})";
                        break;
                    default:
                        hp.state0 = def.s0; hp.state1 = def.s1; hp.action0 = def.a0; hp.action1 = def.a1;
                        break;
                }

                // 6. zones: every part but the curtains (scaled down to 3 % when rolled up; driven by switches and winders)
                if (def.category != Category.Shutter)
                {
                    Zone(part, def.kind, label, def.category == Category.Door, interact);
                    zones++;
                }
            }
            host.parts = hostParts;
            host.lights = lights;
            host.grid = grid;
            EditorUtility.SetDirty(host);

            // 9. spawn points in the yard, facing the entrance like PlayerSpawn
            Physics.SyncTransforms();
            LayerMask solid = ~(1 << interact);
            var levelRoot = new GameObject(LevelRootName).transform;
            var spawnRoot = new GameObject("SpawnPoints").transform;
            spawnRoot.SetParent(levelRoot, false);
            var spawns = new List<Transform>();
            for (int i = 0; i < SnowNetworkBootstrap.MaxPlayers && i < SpawnOffsets.Length; i++)
            {
                if (!FreeSpot(spawn, SpawnOffsets[i], solid, out Vector3 feet))
                {
                    Debug.LogError($"[Snow] BaseV2: no free ground for spawn {i} next to PlayerSpawn {spawn.position}.");
                    continue;
                }
                var sp = new GameObject($"Spawn_{i}").transform;
                sp.SetParent(spawnRoot, false);
                sp.SetPositionAndRotation(feet, Quaternion.Euler(0f, spawn.eulerAngles.y, 0f));
                spawns.Add(sp);
            }

            // 10. network manager with the game's player (no new network prefabs)
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(BenchBuilder.PlayerPrefabPath);
            NetworkPrefabsList list = BenchBuilder.EnsurePrefabList(player);
            BenchBuilder.CreateNetworkManager(player, list, spawns.ToArray()).name = NetworkManagerName;

            // 11. the player's camera takes the location look, PS1 frame included (user decision 16.10)
            var look = levelRoot.gameObject.AddComponent<SkyIslandPlayerCamera>();
            look.sceneCamera = sceneCam;
            look.copyPsxFrame = true;

            // 12. saved so NGO sees the in-scene NetworkObject; last in Build Settings
            BenchBuilder.SaveNetworkScene(scene, ScenePath, false);
            Debug.Log($"[Snow] BaseV2 level integrated: {ScenePath} ({doors} doors, {zones} zones, {synced} synced parts, " +
                      $"{lights.rooms.Count} rooms, {spawns.Count} spawn points), appended to Build Settings.");
        }

        // ------------------------------------------------------------------ parts
        static Def DefOf(string name)
        {
            foreach (Def d in Defs)
                if (name.StartsWith(d.prefix + "_", StringComparison.Ordinal)) return d;
            return null;
        }

        static NetworkDoor Door(SkyIslandInteractable part, Transform root, bool gate, int interact)
        {
            GameObject go = part.gameObject;
            if (!go.TryGetComponent(out NetworkDoor door)) door = go.AddComponent<NetworkDoor>();
            door.doorId = part.name;
            // the core's axis is in the parent's space, NetworkDoor's in its own (_rest * AngleAxis)
            Vector3 axis = Quaternion.Inverse(go.transform.localRotation) * part.axis;
            door.axis = axis.sqrMagnitude > 1e-8f ? axis.normalized : Vector3.up;
            door.openAngle = part.max;
            door.panelColliders = go.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger).ToArray();
            door.vehicleRoot = root;                                   // the building's walls and furniture never block it
            door.blockMask = ~(1 << interact);                          // players and cargo do
            door.manualTravelTime = gate ? GateTravel : DoorTravel;
            EditorUtility.SetDirty(door);
            return door;
        }

        static void Zone(SkyIslandInteractable part, string kind, string label, bool door, int interact)
        {
            Transform t = part.transform;
            if (!LocalBounds(t, out Bounds b)) b = new Bounds(Vector3.zero, Vector3.one * MinZone);
            Vector3 scale = t.lossyScale;
            Vector3 s = new Vector3(Mathf.Max(1e-4f, Mathf.Abs(scale.x)), Mathf.Max(1e-4f, Mathf.Abs(scale.y)), Mathf.Max(1e-4f, Mathf.Abs(scale.z)));
            Vector3 world = Vector3.Scale(b.size, s);                  // size in metres
            int thin = world.x <= world.y && world.x <= world.z ? 0 : world.y <= world.z ? 1 : 2;
            Vector3 size = default;
            for (int a = 0; a < 3; a++)
            {
                float pad = door && a == thin ? DoorThicknessPad : ZonePad;
                float m = Mathf.Max(MinZone, world[a] + 2f * pad);
                size[a] = m / s[a];                                    // back to the part's local units
            }
            var go = new GameObject(ZonePrefix + part.name) { layer = interact };
            go.transform.SetParent(t, false);
            go.transform.localPosition = b.center;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            var zone = go.AddComponent<UtilityPlaneInteractable>();
            zone.id = part.name;
            zone.kind = kind;
            zone.label = label;
        }

        /// <summary>Bounds of the part's meshes in its own space (the part as it stands in the scene: closed).</summary>
        static bool LocalBounds(Transform part, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (MeshFilter mf in part.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.name.StartsWith(ZonePrefix, StringComparison.Ordinal)) continue;
                Bounds m = mf.sharedMesh.bounds;
                Matrix4x4 toPart = part.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 corner = m.center + Vector3.Scale(m.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                    Vector3 p = toPart.MultiplyPoint3x4(corner);
                    if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                    else bounds.Encapsulate(p);
                }
            }
            return any;
        }

        /// <summary>The light manager room whose switch prefix starts the name (the longest one): Flur_EG / Flur_OG /
        /// Flur_UG have "_" inside, so the name is never split.</summary>
        static string LightRoom(SkyIslandLightManager lights, string name)
        {
            string best = null;
            int len = -1;
            foreach (var r in lights.rooms)
                if (r != null && !string.IsNullOrEmpty(r.switchPrefix) && name.StartsWith(r.switchPrefix, StringComparison.Ordinal) && r.switchPrefix.Length > len)
                {
                    best = r.name;
                    len = r.switchPrefix.Length;
                }
            return best;
        }

        /// <summary>IX_ShutterSwitch_Kueche_129 -> Kueche, IX_ShutterWinder_Flur_OG_124 -> Flur_OG.</summary>
        static string ShutterRoom(string name, string prefix)
        {
            string rest = name.Substring(prefix.Length + 1);
            int cut = rest.LastIndexOf('_');
            return cut > 0 && int.TryParse(rest.Substring(cut + 1), out _) ? rest.Substring(0, cut) : rest;
        }

        static string RoomLabel(string room) => room != null && RoomLabels.TryGetValue(room, out string l) ? l : room;

        // ------------------------------------------------------------------ spawn
        /// <summary>Ground under PlayerSpawn + offset along its right vector with room for the player's capsule; moved back
        /// from the entrance 0.5 m at a time, up to 3 times.</summary>
        static bool FreeSpot(Transform spawn, float offset, LayerMask mask, out Vector3 feet)
        {
            Vector3 right = Vector3.ProjectOnPlane(spawn.right, Vector3.up).normalized;
            Vector3 back = -Vector3.ProjectOnPlane(spawn.forward, Vector3.up).normalized;
            for (int attempt = 0; attempt <= 3; attempt++)
            {
                Vector3 p = spawn.position + right * offset + back * (0.5f * attempt);
                if (!Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 10f, mask, QueryTriggerInteraction.Ignore)) continue;
                feet = hit.point;
                // as HLCharacterMotor.CapsuleFree: lifted off the ground, else touching the ground counts as occupied
                Vector3 bottom = feet + Vector3.up * (SkinLift + CapsuleRadius + 0.01f);
                Vector3 top = feet + Vector3.up * (SkinLift + CapsuleHeight - CapsuleRadius);
                if (!Physics.CheckCapsule(bottom, top, CapsuleRadius, mask, QueryTriggerInteraction.Ignore)) return true;
            }
            feet = spawn.position;
            return false;
        }
    }
}
