using System;
using System.Collections.Generic;
using System.Linq;
using SkyIsland;
using Snow.Levels;
using Snow.Levels.Forest;
using Snow.Seats;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UtilityPlane;
using Category = Snow.Levels.SkyIslandLocationHost.Category;
using Object = UnityEngine.Object;

namespace Snow.EditorTools
{
    /// <summary>
    /// The Forest's own step of Tools / Snow / Integrate Location Level / Forest (FOREST_U3 F5.1.2, FOREST_U4 G3.4), after
    /// the shared parts, zones and spawns, before the save; a rerun replaces everything it made:
    ///  - ForestOpening, ForestItems and NoteReader on the Forest root with their links; the data stay JSON TextAssets read
    ///    in play by JsonUtility (ENGINEERING: one source, nothing copied into the scene);
    ///  - zones without parts (Forest/ForestZones: padlock, phone); the four bunks as seats of their own root network
    ///    object ForestBunks (the seat system parents players to it);
    ///  - the static padlock on the barrier (Forest/ForestProps), the 33 grime decals and the 23 hidden X11 prints with
    ///    their cleaning zones (Forest/ForestDecals), each on its room's rendering layer;
    ///  - X01 and STÖRUNG links found by material (the slots moved in Update 4; in play SkyIslandFlicker swaps the lantern
    ///    material for a runtime copy, so it cannot be searched there); the barracks door's powered travel 6 s (X10);
    ///  - then the Unity Terrain sub-step if one is registered (<see cref="ForestTerrainHook"/>).
    /// </summary>
    public static class ForestLevelStep
    {
        const string DataFolder = "Assets/Locations/Forest/Data";
        const string ModelsFolder = "Assets/Locations/Forest/Models";
        const string MaterialsFolder = "Assets/Locations/Forest/Materials/URP";
        const string ZonesName = "ForestZones", DecalsName = "ForestDecals", PropsName = "ForestProps", BunksName = "ForestBunks";
        const string ZonePrefix = "IV_";
        /// <summary>m: the eye over the pillow from the mattress centre, and above the mattress top.</summary>
        const float EyeAlong = 0.7f, EyeUp = 0.18f, ExitAside = 0.9f;

        public static bool Run(Scene scene, Transform root)
        {
            if (!root.TryGetComponent(out SkyIslandLocationHost host)) { Debug.LogError("[Snow] Forest step: no SkyIslandLocationHost on the root."); return false; }
            var openingAsset = AssetDatabase.LoadAssetAtPath<TextAsset>($"{DataFolder}/Forest_Opening.json");
            var dressingAsset = AssetDatabase.LoadAssetAtPath<TextAsset>($"{DataFolder}/Forest_Dressing.json");
            var notesAsset = AssetDatabase.LoadAssetAtPath<TextAsset>($"{DataFolder}/Forest_Notes.json");
            OpeningData o = ForestData.Read<OpeningData>(openingAsset);
            DressingData d = ForestData.Read<DressingData>(dressingAsset);
            if (o == null || d == null || notesAsset == null) { Debug.LogError($"[Snow] Forest step: data missing in {DataFolder} (Forest_Update3)."); return false; }

            // the previous run
            foreach (string n in new[] { ZonesName, DecalsName, PropsName })
            {
                Transform old = root.Find(n);
                if (old != null) Object.DestroyImmediate(old.gameObject);
            }
            foreach (GameObject g in scene.GetRootGameObjects())
                if (g.name == BunksName) Object.DestroyImmediate(g);

            var opening = Ensure<ForestOpening>(root.gameObject);
            var items = Ensure<ForestItems>(root.gameObject);
            var reader = Ensure<NoteReader>(root.gameObject);
            items.opening = openingAsset;
            items.dressing = dressingAsset;
            items.prefabs = ItemPrefabs();
            reader.notes = notesAsset;
            opening.host = host;
            opening.items = items;
            opening.opening = openingAsset;
            if (items.prefabs.Length < 83)
                Debug.LogWarning($"[Snow] Forest step: {items.prefabs.Length} item prefabs in {ForestItemPrefabs.Folder} (83 expected): run Tools / Snow / Forest / Build Item Prefabs, then integrate again.");

            int interact = BenchBuilder.InteractLayer();
            Physics.SyncTransforms();

            // zones without parts; the bunks are seats of their own host
            var zonesRoot = Child(root, ZonesName);
            int extra = 0;
            var bunkZones = new List<OpeningZone>();
            foreach (OpeningZone z in o.zones ?? new OpeningZone[0])
            {
                if (z == null) continue;
                if (z.kind == "seat") { bunkZones.Add(z); continue; }
                Zone(zonesRoot, z.id, z.kind, z.label, ForestData.V3(z.p), ForestData.Q(z.q), ForestData.V3(z.size), interact);
                extra++;
            }
            opening.bunks = Bunks(scene, o, bunkZones, interact);

            // the padlock on the barrier: a static copy until the key takes it off (F5.5)
            OpeningItem pad = o.items?.FirstOrDefault(i => i != null && i.id == ForestItems.PadlockId);
            opening.padlockProxy = pad != null ? StaticModel(Child(root, PropsName), pad.model, pad.id + "_Proxy", ForestData.V3(pad.p), ForestData.Q(pad.q)) : null;

            // decals: 33 grime, then the 23 X11 prints (hidden until the morning)
            var decalRoot = Child(root, DecalsName);
            var decals = new List<GameObject>();
            var ids = new List<string>();
            var meshes = new Dictionary<string, Mesh>(StringComparer.Ordinal);
            int grime = 0, prints = 0;
            foreach (ForestDecal dc in d.decals ?? new ForestDecal[0])
                if (Decal(decalRoot, dc, host.lights, meshes, interact, false, decals, ids)) grime++;
            OpeningScare x11 = o.scares?.FirstOrDefault(s => s != null && s.id != null && s.id.StartsWith("X11", StringComparison.Ordinal));
            foreach (ForestDecal dc in x11?.decals ?? new ForestDecal[0])
                if (Decal(decalRoot, dc, host.lights, meshes, interact, true, decals, ids)) prints++;
            opening.decals = decals.ToArray();
            opening.decalIds = ids.ToArray();

            // X01 and STÖRUNG by material (G3.4)
            string lantern = LanternLinks(host, opening);
            string fault = FaultLinks(host, opening);

            // X10: the barracks door swings open whole and slowly on the powered drive
            string door = "IX_Door_10 not found";
            SkyIslandLocationHost.Part barracks = host.parts.FirstOrDefault(p => p != null && p.id == "IX_Door_10");
            if (barracks?.door != null)
            {
                barracks.door.poweredTravelTime = 6f;
                EditorUtility.SetDirty(barracks.door);
                door = "IX_Door_10 poweredTravelTime 6 s";
            }

            EditorUtility.SetDirty(opening);
            EditorUtility.SetDirty(items);
            EditorUtility.SetDirty(reader);
            int scripted = host.parts.Count(p => p != null && p.category == Category.Scripted);
            int locks = o.locks?.Length ?? 0;
            Debug.Log($"[Snow] Forest opening: {scripted} scripted parts, {extra} extra zones, {grime + prints} decals ({grime} grime, {prints} X11), " +
                      $"{locks} locks, padlock proxy on IX_BarrierArm_82.");
            Debug.Log($"[Snow] Forest bunks: {opening.bunks.seats.Length} seats ({BunksName}).");
            Debug.Log($"[Snow] Forest step: {lantern}; {fault}; {door}.");

            ForestTerrainHook.run?.Invoke(scene, root);
            return true;
        }

        // ------------------------------------------------------------------ pieces
        static T Ensure<T>(GameObject go) where T : Component => go.TryGetComponent(out T c) ? c : go.AddComponent<T>();

        static Transform Child(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        static ForestItems.ModelPrefab[] ItemPrefabs()
        {
            var list = new List<ForestItems.ModelPrefab>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { ForestItemPrefabs.Folder }))
            {
                var p = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (p != null && p.TryGetComponent(out ForestItemTag tag) && !string.IsNullOrEmpty(tag.model))
                    list.Add(new ForestItems.ModelPrefab { model = tag.model, prefab = p });
            }
            return list.OrderBy(m => m.model, StringComparer.Ordinal).ToArray();
        }

        static GameObject Zone(Transform parent, string id, string kind, string label, Vector3 p, Quaternion q, Vector3 size, int interact)
        {
            var go = new GameObject(ZonePrefix + id) { layer = interact };
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(p, q);
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            var zone = go.AddComponent<UtilityPlaneInteractable>();
            zone.id = id;
            zone.kind = kind;
            zone.label = label;
            return go;
        }

        /// <summary>
        /// ForestBunks: a root network object (scene network objects are roots, as in SkyIslandLevelBuilder) with a seat per
        /// mattress. The data give the mattress centre and the zone at the pillow; the head is towards the zone. The eye is
        /// over the pillow looking along the bunk to the feet; the exit is beside the bunk, on the side with free floor.
        /// </summary>
        static ForestBunks Bunks(Scene scene, OpeningData o, List<OpeningZone> zones, int interact)
        {
            var go = new GameObject(BunksName);
            SceneManager.MoveGameObjectToScene(go, scene);
            var no = go.AddComponent<NetworkObject>();
            no.SynchronizeTransform = false;
            var seats = new List<SeatAnchor>();
            ForestPoint[] spots = o.sleep?.spots ?? new ForestPoint[0];
            int solid = ~(1 << interact);
            for (int i = 0; i < zones.Count && i < spots.Length; i++)
            {
                OpeningZone z = zones[i];
                Vector3 spot = ForestData.V3(spots[i].p), zp = ForestData.V3(z.p);
                Vector3 head = Vector3.ProjectOnPlane(zp - spot, Vector3.up);
                head = head.sqrMagnitude > 1e-4f ? head.normalized : Vector3.right;
                Vector3 side = Vector3.Cross(Vector3.up, head);

                var bunk = new GameObject($"Bunk_{i + 1}");
                bunk.transform.SetParent(go.transform, false);
                bunk.transform.SetPositionAndRotation(spot, Quaternion.LookRotation(-head, Vector3.up));
                var a = bunk.AddComponent<SeatAnchor>();
                a.seatId = z.id;
                a.index = i;
                a.role = SeatRole.Passenger;
                a.inside = true;
                a.pelvis = Socket(bunk.transform, "Pelvis", spot, Quaternion.LookRotation(-head, Vector3.up));
                a.eye = Socket(bunk.transform, "Eye", spot + head * EyeAlong + Vector3.up * EyeUp,
                               Quaternion.LookRotation(Quaternion.AngleAxis(-20f, side) * -head, Vector3.up));
                Vector3 e1 = Floor(spot + side * ExitAside, solid, out bool free1), e2 = Floor(spot - side * ExitAside, solid, out bool free2);
                if (!free1 && free2) (e1, e2) = (e2, e1);
                if (!free1 && !free2) Debug.LogWarning($"[Snow] Forest bunks: {z.id}: no free floor beside the bunk; exit points may be blocked.");
                a.exit = Socket(bunk.transform, "Exit", e1, Quaternion.LookRotation(side, Vector3.up));
                a.altExit = Socket(bunk.transform, "AltExit", e2, Quaternion.LookRotation(-side, Vector3.up));
                Zone(bunk.transform, z.id, z.kind, z.label, zp, ForestData.Q(z.q), ForestData.V3(z.size), interact);
                seats.Add(a);
            }
            var host = go.AddComponent<ForestBunks>();
            host.seats = seats.ToArray();
            EditorUtility.SetDirty(host);
            return host;
        }

        static Transform Socket(Transform parent, string name, Vector3 p, Quaternion q)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.SetPositionAndRotation(p, q);
            return t;
        }

        /// <summary>The floor under a point (down from 0.3 m above it, solids only) and whether a standing capsule fits there.</summary>
        static Vector3 Floor(Vector3 p, int mask, out bool free)
        {
            free = false;
            if (!Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, out RaycastHit hit, 4f, mask, QueryTriggerInteraction.Ignore)) return p;
            Vector3 feet = hit.point + Vector3.up * 0.02f;
            const float r = 0.34f, h = 1.8f;                           // player capsule (Player.prefab, HLCharacterMotor)
            free = !Physics.CheckCapsule(feet + Vector3.up * (r + 0.03f), feet + Vector3.up * (h - r), r, mask, QueryTriggerInteraction.Ignore);
            return feet;
        }

        /// <summary>A static copy of an item model (no colliders, no physics, no network).</summary>
        static GameObject StaticModel(Transform parent, string model, string name, Vector3 p, Quaternion q)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelsFolder}/{model}.fbx");
            if (asset == null) { Debug.LogWarning($"[Snow] Forest step: {model}.fbx missing; no padlock copy on the barrier."); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(p, q);
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            return go;
        }

        /// <summary>A decal quad (side size, local XZ, normal +Y; u along -X, v along -Z, corner (+h, +h) = (u0, v0)) on its
        /// room's rendering layer (otherwise every room's lamps light it through the walls), with a 1 cm cleaning zone.</summary>
        static bool Decal(Transform parent, ForestDecal dc, SkyIslandLightManager lights, Dictionary<string, Mesh> meshes, int interact,
                          bool hidden, List<GameObject> list, List<string> ids)
        {
            if (dc == null || string.IsNullOrEmpty(dc.id) || dc.uv == null || dc.uv.Length < 4 || dc.size <= 0f) return false;
            var go = new GameObject(dc.id);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(ForestData.V3(dc.p), ForestData.Q(dc.q));
            go.AddComponent<MeshFilter>().sharedMesh = Quad(meshes, dc.size, dc.uv);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/{(string.IsNullOrEmpty(dc.material) ? "M_BV_Decal" : dc.material)}.mat");
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            int layer = RoomLayer(lights, dc.room);
            if (layer >= 0) r.renderingLayerMask = 1u << layer;
            else Debug.LogWarning($"[Snow] Forest step: decal {dc.id}: room '{dc.room}' not in the light manager; default rendering layer.");
            Zone(go.transform, dc.id, string.IsNullOrEmpty(dc.kind) ? "lever" : dc.kind, dc.label,
                 go.transform.position, go.transform.rotation, new Vector3(dc.size, 0.01f, dc.size), interact);
            if (hidden) go.SetActive(false);
            list.Add(go);
            ids.Add(dc.id);
            return true;
        }

        static int RoomLayer(SkyIslandLightManager lights, string room)
        {
            if (lights == null || string.IsNullOrEmpty(room)) return -1;
            foreach (var r in lights.rooms)
                if (r != null && r.name == room) return r.layer;
            return -1;
        }

        static Mesh Quad(Dictionary<string, Mesh> cache, float size, float[] uv)
        {
            string key = $"{size:0.###}_{uv[0]:0.####}_{uv[1]:0.####}_{uv[2]:0.####}_{uv[3]:0.####}";
            if (cache.TryGetValue(key, out Mesh m)) return m;
            float h = size * 0.5f;
            var v = new[] { new Vector3(-h, 0f, -h), new Vector3(-h, 0f, h), new Vector3(h, 0f, h), new Vector3(h, 0f, -h) };
            var t = new Vector2[4];
            for (int i = 0; i < 4; i++)
                t[i] = new Vector2(uv[0] + (h - v[i].x) / size * (uv[2] - uv[0]), uv[1] + (h - v[i].z) / size * (uv[3] - uv[1]));
            m = new Mesh { name = "ForestDecal_" + key, vertices = v, uv = t, triangles = new[] { 0, 1, 2, 0, 2, 3 } };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.RecalculateBounds();
            cache[key] = m;
            return m;
        }

        // ------------------------------------------------------------------ links by material (G3.4)
        static string LanternLinks(SkyIslandLocationHost host, ForestOpening opening)
        {
            Transform hut = ModelRoot(host, "IX_HutDoor_1");
            if (hut == null) return "hut lantern: TowerHut not found";
            opening.lanternGlass = null;
            opening.lanternSlot = -1;
            foreach (Renderer r in hut.GetComponentsInChildren<Renderer>(true))
            {
                int slot = Slot(r, "M_Lantern");
                if (slot < 0) continue;
                opening.lanternGlass = r;
                opening.lanternSlot = slot;
                break;
            }
            opening.lanternOff = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/M_LanternOff.mat");
            opening.lanternGlows = hut.GetComponentsInChildren<Renderer>(true).Where(r => Slot(r, "M_Glow_Lantern") >= 0).Select(r => r.gameObject).ToArray();
            opening.lanternLights = hut.GetComponentsInChildren<Light>(true).Where(l => l.name.StartsWith("Light_lantern", StringComparison.Ordinal)).ToArray();
            string glass = opening.lanternGlass != null ? $"{Path(hut, opening.lanternGlass.transform)} slot {opening.lanternSlot}" : "NOT FOUND";
            return $"hut lantern glass {glass} (M_Lantern), {opening.lanternGlows.Length} glow(s), {opening.lanternLights.Length} light(s)";
        }

        static string FaultLinks(SkyIslandLocationHost host, ForestOpening opening)
        {
            Transform boiler = ModelRoot(host, "IX_BurnerReset_308");
            var renderers = new List<Renderer>();
            var slots = new List<int>();
            var found = new List<string>();
            if (boiler != null)
                foreach (Renderer r in boiler.GetComponentsInChildren<Renderer>(true))
                {
                    int slot = Slot(r, "M_HeatFault");
                    if (slot < 0) slot = Slot(r, "M_HeatFaultOn");
                    if (slot < 0) continue;
                    renderers.Add(r);
                    slots.Add(slot);
                    found.Add($"{Path(boiler, r.transform)} slot {slot}");
                }
            opening.faultRenderers = renderers.ToArray();
            opening.faultSlots = slots.ToArray();
            opening.faultOff = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/M_HeatFault.mat");
            opening.faultOn = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/M_HeatFaultOn.mat");
            return found.Count > 0 ? $"STÖRUNG lamp {string.Join(", ", found)} (M_HeatFault)" : "STÖRUNG lamp NOT FOUND";
        }

        /// <summary>The model instance a part belongs to (its outermost prefab instance: the glows and lights the core adds
        /// sit at the prefab's root, beside the model).</summary>
        static Transform ModelRoot(SkyIslandLocationHost host, string partId)
        {
            SkyIslandLocationHost.Part p = host.parts.FirstOrDefault(x => x != null && x.id == partId);
            if (p?.part == null) return null;
            GameObject inst = PrefabUtility.GetOutermostPrefabInstanceRoot(p.part.gameObject);
            return inst != null ? inst.transform : p.part.transform.root;
        }

        static int Slot(Renderer r, string material)
        {
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] != null && mats[i].name == material) return i;
            return -1;
        }

        static string Path(Transform root, Transform t)
        {
            var names = new List<string>();
            for (Transform x = t; x != null && x != root; x = x.parent) names.Add(x.name);
            names.Add(root.name);
            names.Reverse();
            return string.Join("/", names);
        }
    }

    /// <summary>The Unity Terrain sub-step of the Forest integration (FOREST_ASSEMBLY K5): the terrain code registers
    /// itself here ([InitializeOnLoad]); the Forest step calls it last, before the scene is saved.</summary>
    public static class ForestTerrainHook
    {
        public static Action<Scene, Transform> run;
    }
}
