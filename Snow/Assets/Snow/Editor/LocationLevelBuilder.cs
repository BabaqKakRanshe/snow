using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
    /// Makes a location scene built by the Sky Island core (Tools / Locations / Location Builder -> &lt;Id&gt;) a networked
    /// level, driven by a <see cref="LocationProfile"/> (LocationProfiles.cs): NetworkObject + SkyIslandLocationHost on the
    /// location root when it has openable parts, a NetworkDoor on the doors the profile names, the other doors the shared
    /// way (a bit on the host, on their own layer), furniture on the Interact layer, a zone on every part but the shutter
    /// curtains, interior groups under LocationProximity, a fall reset, four spawn points, network manager with the game's
    /// player, the player camera look (PS1 frame included). The core builder recreates the scene from scratch, so run this
    /// again after every build of the location; running it twice replaces, not duplicates. Everything changed inside the
    /// location's prefab instances stays a scene override (no Apply to the prefabs). The scene goes last in Build Settings.
    /// </summary>
    public static class LocationLevelBuilder
    {
        const string LevelRootName = "SnowLevel";
        const string NetworkManagerName = "NetworkManager";
        const string ZonePrefix = "IV_";

        // zone boxes: renderer bounds of the part (closed) + pad per side, at least MinZone per axis; doors get a thicker
        // pad across the leaf so the ray meets the zone before the leaf from both sides. 2 cm on every axis would make
        // the stacked drawers' zones overlap.
        const float ZonePad = 0.01f, DoorThicknessPad = 0.02f, MinZone = 0.06f;
        // spawn: player capsule (Player.prefab, HLCharacterMotor) and a row across the spawn's right vector
        const float CapsuleRadius = 0.34f, CapsuleHeight = 1.8f, SkinLift = 0.02f;
        static readonly float[] SpawnOffsets = { -1.8f, -0.6f, 0.6f, 1.8f };
        /// <summary>m under the lowest solid collider of the location: the default fall reset height.</summary>
        const float FallMargin = 30f;

        [MenuItem("Tools/Snow/Integrate Location Level/Forest")] static void Forest() => Menu(LocationProfiles.Forest);
        [MenuItem("Tools/Snow/Integrate Location Level/Town")] static void Town() => Menu(LocationProfiles.Town);
        [MenuItem("Tools/Snow/Integrate Location Level/CoalPit")] static void CoalPit() => Menu(LocationProfiles.CoalPit);
        [MenuItem("Tools/Snow/Integrate Location Level/Dam")] static void Dam() => Menu(LocationProfiles.Dam);
        [MenuItem("Tools/Snow/Integrate Location Level/AboveClouds")] static void AboveClouds() => Menu(LocationProfiles.AboveClouds);

        public static void Menu(LocationProfile profile)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Integrate(profile);
        }

        /// <summary>Batch mode: -executeMethod Snow.EditorTools.LocationLevelBuilder.IntegrateFromCommandLine -location Town</summary>
        public static void IntegrateFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            int k = Array.IndexOf(args, "-location");
            string id = k >= 0 && k + 1 < args.Length ? args[k + 1] : null;
            LocationProfile profile = LocationProfiles.Find(id);
            if (profile == null) { Debug.LogError($"[Snow] Integrate Location Level: unknown -location '{id}'."); EditorApplication.Exit(1); return; }
            bool ok = Integrate(profile);
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        public static bool Integrate(LocationProfile profile)
        {
            try { return Run(profile); }
            finally { EditorUtility.ClearProgressBar(); }
        }

        static bool Run(LocationProfile pr)
        {
            string id = pr.id;
            string hint = $"Run Tools / Locations / Location Builder -> {id} -> Build first.";

            // 1. the scene the core builder made
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(pr.scenePath) == null) { Debug.LogError($"[Snow] {id}: {pr.scenePath} not found. {hint}"); return false; }
            Scene scene = EditorSceneManager.OpenScene(pr.scenePath, OpenSceneMode.Single);
            GameObject rootGo = scene.GetRootGameObjects().FirstOrDefault(g => g.name == pr.rootName);
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
            // hierarchy order from the root: the order of generated ids, the same on every peer (one scene)
            SkyIslandInteractable[] all = root != null ? root.GetComponentsInChildren<SkyIslandInteractable>(true) : new SkyIslandInteractable[0];
            var missing = new List<string>();
            if (root == null) missing.Add($"root '{pr.rootName}'");
            if (sceneCam == null) missing.Add($"{pr.rootName}/Main Camera");
            if (spawn == null) missing.Add("PlayerSpawn (tag Respawn)");
            if (pr.requireLights && lights == null) missing.Add("SkyIslandLightManager on the root");
            if (pr.requireGrid && grid == null) missing.Add("SkyIslandPowerGrid on the root");
            if (pr.rules.Length > 0 && all.Length == 0) missing.Add("SkyIslandInteractable parts (core below v2.7?)");
            if (missing.Count > 0) { Debug.LogError($"[Snow] {id}: {string.Join(", ", missing)} not found in {pr.scenePath}. {hint}"); return false; }
            if (pr.rules.Length == 0 && all.Length > 0) { Debug.LogError($"[Snow] {id}: {all.Length} openable parts but the profile has no rules."); return false; }

            SkyIslandInteractable[] parts;
            if (pr.uniqueNames)
            {
                parts = all.OrderBy(p => p.name, StringComparer.Ordinal).ToArray();
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in parts)
                    if (!names.Add(p.name)) { Debug.LogError($"[Snow] {id}: part name '{p.name}' is not unique; the profile needs generated ids (uniqueNames = false)."); return false; }
            }
            else parts = all;
            var rules = new LocationProfile.Rule[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                rules[i] = pr.RuleOf(parts[i].name);
                if (rules[i] == null) { Debug.LogError($"[Snow] {id}: no rule for part '{parts[i].name}' (profile)."); return false; }
                if (rules[i].category == Category.Door && (parts[i].kind != SkyIslandInteractable.Kind.Hinge || !Mathf.Approximately(parts[i].min, 0f)))
                {
                    Debug.LogError($"[Snow] {id}: door '{parts[i].name}' is {parts[i].kind} with min {parts[i].min}; NetworkDoor turns from 0 only.");
                    return false;
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

            // 4. the fly camera's local test clicks (no network) go; fly camera and PSX stay until a player joins
            if (sceneCam.TryGetComponent(out SkyIslandInteractor interactor)) Object.DestroyImmediate(interactor);

            int interact = BenchBuilder.InteractLayer();
            int doors = 0, zones = 0, synced = 0;
            if (parts.Length > 0)
            {
                // 3. network identity and the host on the (static) location root
                if (!rootGo.TryGetComponent(out NetworkObject no)) no = rootGo.AddComponent<NetworkObject>();
                no.SynchronizeTransform = false;
                if (!rootGo.TryGetComponent(out SkyIslandLocationHost host)) host = rootGo.AddComponent<SkyIslandLocationHost>();

                var hostParts = new SkyIslandLocationHost.Part[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                {
                    if (i % 250 == 0) EditorUtility.DisplayProgressBar($"Integrate {id}", $"Parts {i}/{parts.Length}", (float)i / parts.Length);
                    SkyIslandInteractable part = parts[i];
                    LocationProfile.Rule rule = rules[i];
                    // 4.4: the name when names are unique, else p<n> (one house stands many times; hierarchy paths repeat too)
                    string zoneId = pr.uniqueNames ? part.name : "p" + i;
                    var hp = new SkyIslandLocationHost.Part
                    {
                        id = zoneId, part = part, category = rule.category, lockedState = rule.lockedState,
                        doorStates = rule.doorStates,
                    };
                    hostParts[i] = hp;

                    // doors: NetworkDoor on the hinge object (the part itself); the core part stays but nothing calls it
                    if (rule.category == Category.Door)
                    {
                        hp.door = Door(part, zoneId, root, rule.travel, interact);
                        doors++;
                    }
                    else
                    {
                        // a part that was a NetworkDoor in an older profile goes the shared way now
                        if (part.TryGetComponent(out NetworkDoor stale)) Object.DestroyImmediate(stale);
                        synced++;
                        // furniture solids on Interact: the player's motor ignores them (an opened door cannot lock him in),
                        // the use ray still stops at them. Doors without a NetworkDoor stay on their layer: a closed door
                        // holds the player
                        if (rule.category != Category.HostDoor)
                            foreach (Transform t in part.GetComponentsInChildren<Transform>(true))
                            {
                                if (t.name.StartsWith(ZonePrefix, StringComparison.Ordinal) || t.gameObject.layer == interact) continue;
                                t.gameObject.layer = interact;
                                if (PrefabUtility.IsPartOfPrefabInstance(t.gameObject)) PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);
                            }
                    }

                    // host data
                    string label = rule.label;
                    switch (rule.category)
                    {
                        case Category.LightSwitch:
                            hp.room = LightRoom(lights, part.name);
                            if (hp.room == null) { Debug.LogError($"[Snow] {id}: light switch '{part.name}' matches no light manager room."); return false; }
                            label = hp.room == "Exterior" ? "Наружный свет" : $"{rule.label} ({pr.RoomLabel(hp.room)})";
                            break;
                        case Category.ShutterSwitch:
                        case Category.ShutterWinder:
                            hp.room = ShutterRoom(part.name, rule.prefix);
                            if (rule.category == Category.ShutterSwitch) label = $"{rule.label} ({pr.RoomLabel(hp.room)})";
                            break;
                        default:
                            hp.state0 = rule.s0; hp.state1 = rule.s1; hp.action0 = rule.a0; hp.action1 = rule.a1;
                            hp.state1NoPower = rule.s1NoPower;
                            break;
                    }

                    // zones: every part but the curtains (scaled down to 3 % when rolled up; driven by switches and winders)
                    if (rule.category != Category.Shutter)
                    {
                        Zone(part, zoneId, rule.kind, label, rule.category == Category.Door || rule.category == Category.HostDoor, interact);
                        zones++;
                    }
                }
                host.parts = hostParts;
                host.lights = lights;
                host.grid = grid;
                host.incremental = pr.incremental;
                EditorUtility.SetDirty(host);
            }
            else
            {
                // a location without openable parts has no host; a stale one from an older run goes
                if (rootGo.TryGetComponent(out SkyIslandLocationHost oldHost)) Object.DestroyImmediate(oldHost);
                if (rootGo.TryGetComponent(out NetworkObject oldNo)) Object.DestroyImmediate(oldNo);
            }

            var levelRoot = new GameObject(LevelRootName).transform;

            // 6. interior groups: the server keeps a building on while any player is near it (LocationProximity)
            SkyIslandProximityGroup[] groups = root.GetComponentsInChildren<SkyIslandProximityGroup>(true);
            foreach (var g in groups)
            {
                g.enabled = false;                                     // its Start would hide the children by its own rule
                EditorUtility.SetDirty(g);
                if (PrefabUtility.IsPartOfPrefabInstance(g)) PrefabUtility.RecordPrefabInstancePropertyModifications(g);
            }
            if (groups.Length > 0) levelRoot.gameObject.AddComponent<LocationProximity>().groups = groups;

            // 9. spawn points by PlayerSpawn, facing like it
            EditorUtility.DisplayProgressBar($"Integrate {id}", "Spawn points", 0.95f);
            Physics.SyncTransforms();
            LayerMask solid = ~(1 << interact);
            var spawnRoot = new GameObject("SpawnPoints").transform;
            spawnRoot.SetParent(levelRoot, false);
            var spawns = new List<Transform>();
            for (int i = 0; i < SnowNetworkBootstrap.MaxPlayers && i < SpawnOffsets.Length; i++)
            {
                if (!FreeSpot(spawn, SpawnOffsets[i], solid, out Vector3 feet))
                {
                    Debug.LogError($"[Snow] {id}: no free ground for spawn {i} next to PlayerSpawn {spawn.position}.");
                    continue;
                }
                var sp = new GameObject($"Spawn_{i}").transform;
                sp.SetParent(spawnRoot, false);
                sp.SetPositionAndRotation(feet, Quaternion.Euler(0f, spawn.eulerAngles.y, 0f));
                spawns.Add(sp);
            }

            // 7. back to a spawn point after a fall off the edge (on foot only)
            float fallY = pr.fallY ?? LowestSolid(root) - FallMargin;
            var fall = levelRoot.gameObject.AddComponent<LocationFallReset>();
            fall.fallY = fallY;
            fall.spawnRoot = spawnRoot;

            // network manager with the game's player (no new network prefabs)
            BenchBuilder.EnsurePlayerTag();                            // the fog event volumes react to the tag
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(BenchBuilder.PlayerPrefabPath);
            NetworkPrefabsList list = BenchBuilder.EnsurePrefabList(player);
            BenchBuilder.CreateNetworkManager(player, list, spawns.ToArray()).name = NetworkManagerName;

            // the player's camera takes the location look, PS1 frame included (user decision 16.10)
            var look = levelRoot.gameObject.AddComponent<SkyIslandPlayerCamera>();
            look.sceneCamera = sceneCam;
            look.copyPsxFrame = true;

            // saved so NGO sees the in-scene NetworkObject; last in Build Settings
            EditorUtility.DisplayProgressBar($"Integrate {id}", "Saving the scene", 0.98f);
            BenchBuilder.SaveNetworkScene(scene, pr.scenePath, false);
            int rooms = lights != null ? lights.rooms.Count : 0;
            Debug.Log($"[Snow] {id} level integrated: {pr.scenePath} ({doors} doors, {zones} zones, {synced} synced parts, " +
                      $"{rooms} rooms, {groups.Length} proximity groups, fall reset Y {fallY:0.#}, {spawns.Count} spawn points), appended to Build Settings.");
            return true;
        }

        // ------------------------------------------------------------------ parts
        static NetworkDoor Door(SkyIslandInteractable part, string id, Transform root, float travel, int interact)
        {
            GameObject go = part.gameObject;
            if (!go.TryGetComponent(out NetworkDoor door)) door = go.AddComponent<NetworkDoor>();
            door.doorId = id;
            // the core's axis is in the parent's space, NetworkDoor's in its own (_rest * AngleAxis)
            Vector3 axis = Quaternion.Inverse(go.transform.localRotation) * part.axis;
            door.axis = axis.sqrMagnitude > 1e-8f ? axis.normalized : Vector3.up;
            door.openAngle = part.max;
            door.panelColliders = go.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger).ToArray();
            door.vehicleRoot = root;                                   // the location's walls and furniture never block it
            door.blockMask = ~(1 << interact);                          // players and cargo do
            door.manualTravelTime = travel;
            EditorUtility.SetDirty(door);
            return door;
        }

        static void Zone(SkyIslandInteractable part, string id, string kind, string label, bool door, int interact)
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
            zone.id = id;
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
            if (lights == null) return null;
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

        // ------------------------------------------------------------------ spawn and fall
        /// <summary>Ground under PlayerSpawn + offset along its right vector with room for the player's capsule; moved back
        /// from the facing direction 0.5 m at a time, up to 3 times.</summary>
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

        /// <summary>Lowest point of the location's solid colliders (no triggers: zones, fog events; a disabled collider
        /// reports an empty box at the origin).</summary>
        static float LowestSolid(Transform root)
        {
            float y = float.MaxValue;
            foreach (Collider col in root.GetComponentsInChildren<Collider>(false))
                if (!col.isTrigger && col.enabled) y = Mathf.Min(y, col.bounds.min.y);
            return y == float.MaxValue ? root.position.y : y;
        }
    }

    /// <summary>What LocationLevelBuilder needs to know about one location (LOCATIONS_UPDATE_UNITY.md, appendix A).</summary>
    public sealed class LocationProfile
    {
        public string id, scenePath, rootName;
        public Rule[] rules = new Rule[0];
        public Dictionary<string, string> roomLabels = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>Fall reset height; null = the lowest solid collider minus 30 m.</summary>
        public float? fallY;
        public bool requireLights, requireGrid;
        /// <summary>Part names unique in the location: zone id = name, parts in ordinal name order. Else p&lt;n&gt; in
        /// hierarchy order (Town: one house stands many times).</summary>
        public bool uniqueNames = true;
        /// <summary>SkyIslandLocationHost.incremental (Town).</summary>
        public bool incremental;

        public sealed class Rule
        {
            /// <summary>Name = prefix + "_" + ... (StartsWith with the "_"), unless <see cref="regex"/> is set.</summary>
            public string prefix;
            public Regex regex;
            public Category category;
            public string label, kind, s0, s1, a0, a1, s1NoPower, lockedState;
            /// <summary>Doors: closed, opening, open, closing, blocked (null: the feminine defaults).</summary>
            public string[] doorStates;
            /// <summary>s, NetworkDoor travel time (TEMPORARY, as the game's other doors).</summary>
            public float travel = 1.2f;

            public bool Matches(string name) =>
                regex != null ? regex.IsMatch(name) : name.StartsWith(prefix + "_", StringComparison.Ordinal);
        }

        public Rule RuleOf(string name)
        {
            foreach (Rule r in rules)
                if (r.Matches(name)) return r;
            return null;
        }

        public string RoomLabel(string room) => room != null && roomLabels.TryGetValue(room, out string l) ? l : room;
    }
}
