using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SkyIsland;
using Snow.Cargo;
using Snow.Levels;
using Snow.Levels.Forest;
using Snow.World;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UtilityPlane;
using KitItem = Snow.World.KitItem;
using Object = UnityEngine.Object;

namespace Snow.EditorTools
{
    /// <summary>
    /// KPP 2.1 and the Leitstand console 1.2 in the Forest level (KPP_CONTRACT.md, MAIN_CONTRACT.md «Пункт управления КПП»):
    ///  - <see cref="Place"/> (LocationProfile.preStep, before the parts are gathered): the snow_kit prefab on every instance of
    ///    Data/Forest_KitInstances.json (tools/kit/make_unity_data.py) — pos / rot / scale in the parent's frame, the IX_
    ///    parts numbered from ix_ids, the rendering layers of the room (lightMask, else the root's own); the battery shelf
    ///    (a StowPoint with its zone), the monitor cameras at the heads' CAM_View, the battery's spawn by the road;
    ///  - <see cref="Link"/> (after the terrain hook's steps: the booth and barrier have moved): ForestKpp on the Forest
    ///    root with its links, the battery item in the network prefab list;
    ///  - Tools / Snow / Forest / KPP v2: Level Walls Ground — walls/levelling.json once, with a copy of the tiles.
    /// Running again replaces what it made.
    /// </summary>
    public static class ForestKppStep
    {
        const string DataPath = "Assets/Locations/Forest/Data/Forest_KitInstances.json";
        const string LevellingPath = "Assets/Locations/Forest/Data/Forest_KppLevelling.json";
        const string KitPrefabs = "Assets/Kit/Prefabs";
        const string BatteryPrefab = "Assets/Snow/Van/Items/VanItem_Battery_Net.prefab";
        const string PrefabListPath = "Assets/DefaultNetworkPrefabs.asset";
        const string MaterialsFolder = "Assets/Locations/Forest/Materials/URP";
        const string SlotName = "KPP_BatterySlot", SpawnName = "KPP_BatterySpawn", CameraPrefix = "KPP_Camera_";
        static readonly string[] Roots = { "Checkpoint", "Barrier", "Gate", "BaseMain" };
        /// <summary>TEMPORARY (user 23.10: no van on the map): the battery lies on the road 6 m before the barrier (barrier frame).</summary>
        static readonly Vector3 BatteryLocal = new Vector3(-1.0f, 0f, 6f);
        /// <summary>The contract's ~55° taken as the horizontal field of a 4:3 picture.</summary>
        const float CameraFov = 55f, CameraFar = 120f;

        [Serializable] class Data { public Inst[] instances; public Cam[] cameras; }
        [Serializable] class Inst { public string name, item, parent, root; public Ix[] ix; public float[] pos, rot, scale, game; public uint lightMask; }
        [Serializable] class Ix { public string part; public int id; }
        [Serializable] class Cam { public string name, parent, monitor; public float fov; public float[] game; }

        static Data Load() => JsonUtility.FromJson<Data>(File.ReadAllText(DataPath));

        static Transform RootOf(Transform root, string name) => root.Find("Structures/" + name);

        // ------------------------------------------------------------------ place (before the parts)
        public static bool Place(Scene scene, Transform root)
        {
            if (!File.Exists(DataPath)) { Debug.LogError($"[Snow] Forest KPP: {DataPath} not found."); return false; }
            Data d = Load();
            var roots = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (string r in Roots)
            {
                Transform t = RootOf(root, r);
                if (t == null) { Debug.LogError($"[Snow] Forest KPP: Structures/{r} not found."); return false; }
                roots[r] = t;
                foreach (KitItem k in t.GetComponentsInChildren<KitItem>(true).Where(k => k.transform.parent == null || k.transform.parent.GetComponentInParent<KitItem>(true) == null).ToArray())
                    if (k != null) Object.DestroyImmediate(k.gameObject);
                foreach (Transform old in t.GetComponentsInChildren<Transform>(true).Where(x => x.name == SpawnName).ToArray())
                    if (old != null) Object.DestroyImmediate(old.gameObject);
            }

            int interact = BenchBuilder.InteractLayer();
            uint exteriorLights = ExteriorLightLayers(roots["Checkpoint"]);
            var placed = new Dictionary<string, Transform>(StringComparer.Ordinal);
            var pending = new List<Inst>(d.instances);
            int missingPrefab = 0, ix = 0;
            for (int pass = 0; pass < 8 && pending.Count > 0; pass++)
                foreach (Inst i in pending.ToArray())
                {
                    Transform rt = roots[i.root];
                    Transform parent = i.parent == i.root ? rt : placed.TryGetValue(i.parent, out Transform pp) ? pp : Deep(rt, i.parent);
                    if (parent == null) continue;
                    pending.Remove(i);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{KitPrefabs}/{i.item}.prefab");
                    if (prefab == null) { missingPrefab++; continue; }
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                    go.name = i.name;
                    go.transform.localPosition = V(i.pos);
                    go.transform.localRotation = new Quaternion(i.rot[0], i.rot[1], i.rot[2], i.rot[3]);
                    go.transform.localScale = V(i.scale);
                    foreach (Ix x in i.ix ?? new Ix[0])
                    {
                        Transform part = Deep(go.transform, x.part);
                        if (part == null) { Debug.LogError($"[Snow] Forest KPP: {i.name} has no {x.part}."); return false; }
                        part.name = $"{x.part}_{x.id}";
                        ix++;
                    }
                    uint mask = i.lightMask != 0 ? i.lightMask : RootMask(rt);
                    foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true)) r.renderingLayerMask = mask;
                    foreach (Light l in go.GetComponentsInChildren<Light>(true))
                        if (exteriorLights != 0) l.GetUniversalAdditionalLightData().renderingLayers = exteriorLights;
                    placed[i.name] = go.transform;
                }
            if (pending.Count > 0 || missingPrefab > 0)
            {
                Debug.LogError($"[Snow] Forest KPP: {pending.Count} instances without a parent ({string.Join(", ", pending.Take(6).Select(p => p.name + "<" + p.parent))}), " +
                               $"{missingPrefab} without a kit prefab: run Tools / Snow / Kit / Build Kit.");
                return false;
            }

            // the battery shelf: a StowPoint on a kinematic body of its own (CargoItem.ServerStow ignores only that body's
            // colliders), its zone in front of the shelf
            Transform cab = placed["PowerCabinet"], slot = Deep(cab, "Slot_Battery");
            var shelf = new GameObject(SlotName);
            shelf.transform.SetParent(cab, false);
            shelf.transform.position = slot != null ? slot.position : cab.position;
            // battery local +x (the posts) -> the cabinet's +Z (its front), +y -> +Y (SNOW_KIT_CONTRACT K5)
            shelf.transform.rotation = cab.rotation * Quaternion.LookRotation(Vector3.left, Vector3.up);
            var rb = shelf.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            var sp = shelf.AddComponent<StowPoint>();
            sp.stowId = "KPP_Battery";
            sp.accepts = "Battery";
            var zone = new GameObject("IV_" + SlotName) { layer = interact };
            zone.transform.SetParent(cab, false);
            zone.transform.localPosition = (slot != null ? slot.localPosition : Vector3.zero) + new Vector3(0f, 0.14f, 0.02f);
            var box = zone.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(0.3f, 0.3f, 0.3f);
            var ia = zone.AddComponent<UtilityPlaneInteractable>();
            ia.id = ForestKpp.BatterySlot;
            ia.kind = "momentary";
            ia.label = "Полка аккумулятора";

            // monitor cameras at the heads' optical centre (CAM_View)
            int cams = 0;
            foreach (Cam c in d.cameras ?? new Cam[0])
            {
                string head = "Cam_" + c.name.Substring(4);                // CAM_Barrier -> Cam_Barrier
                Transform view = placed.TryGetValue(head, out Transform h) ? Deep(h, "CAM_View") : null;
                if (view == null) { Debug.LogError($"[Snow] Forest KPP: {head}/CAM_View not found."); return false; }
                var go = new GameObject(CameraPrefix + c.name.Substring(4));
                go.transform.SetParent(view, false);
                var cam = go.AddComponent<Camera>();
                cam.enabled = false;
                cam.fieldOfView = Camera.HorizontalToVerticalFieldOfView(c.fov > 0f ? c.fov : CameraFov, 4f / 3f);
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = CameraFar;
                cam.depth = -10f;
                cam.allowHDR = false;
                cam.allowMSAA = false;
                cam.cullingMask = ~(1 << interact);
                var data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = false;
                data.renderShadows = false;
                cams++;
            }

            var spawn = new GameObject(SpawnName).transform;
            spawn.SetParent(roots["Barrier"], false);
            spawn.localPosition = BatteryLocal;
            spawn.localRotation = Quaternion.Euler(0f, 90f, 0f);
            Debug.Log($"[Snow] Forest KPP: {placed.Count} kit instances placed ({ix} IX parts numbered), battery shelf, {cams} monitor cameras, battery spawn by the road.");
            return true;
        }

        static uint RootMask(Transform t)
        {
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
                if (r.GetComponentInParent<KitItem>(true) == null) return r.renderingLayerMask;
            return 1u;
        }

        /// <summary>The rendering layers of the booth's own exterior lamps (the core gave them the Exterior room's).</summary>
        static uint ExteriorLightLayers(Transform booth)
        {
            foreach (Light l in booth.GetComponentsInChildren<Light>(true))
                if (l.GetComponentInParent<KitItem>(true) == null && l.TryGetComponent(out UniversalAdditionalLightData a)) return a.renderingLayers;
            return 0u;
        }

        // ------------------------------------------------------------------ links (after the relocation)
        public static void Link(Transform root)
        {
            if (!root.TryGetComponent(out SkyIslandLocationHost host)) { Debug.LogWarning("[Snow] Forest KPP: no location host; ForestKpp not linked."); return; }
            if (!root.TryGetComponent(out ForestKpp kpp)) kpp = root.gameObject.AddComponent<ForestKpp>();
            Transform booth = RootOf(root, "Checkpoint"), barrier = RootOf(root, "Barrier"), gate = RootOf(root, "Gate"), main = RootOf(root, "BaseMain");
            KitItem K(Transform r, string n) { Transform t = r != null ? Deep(r, n) : null; return t != null ? t.GetComponent<KitItem>() : null; }
            kpp.host = host;
            kpp.booth = booth;
            Transform slot = booth != null ? Deep(booth, SlotName) : null;
            kpp.batterySlot = slot != null ? slot.GetComponent<StowPoint>() : null;
            kpp.batterySpawn = barrier != null ? Deep(barrier, SpawnName) : null;
            var battery = AssetDatabase.LoadAssetAtPath<GameObject>(BatteryPrefab);
            kpp.batteryPrefab = battery != null ? battery.GetComponent<NetworkObject>() : null;
            if (battery != null)
            {
                var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);
                if (list != null && !list.PrefabList.Any(x => x.Prefab == battery))
                {
                    list.Add(new NetworkPrefab { Prefab = battery });
                    EditorUtility.SetDirty(list);
                    AssetDatabase.SaveAssets();
                }
            }
            kpp.cabinet = K(booth, "PowerCabinet");
            kpp.doorStrike = K(booth, "DoorLockStrike");
            kpp.beacon = K(barrier, "Beacon_Barrier");
            kpp.btnBarrier = K(booth, "Btn_Schranke");
            kpp.btnGate = K(booth, "Btn_Tor");
            kpp.leitBtnBarrier = K(main, "Leit_Btn_Schranke");
            kpp.leitBtnGate = K(main, "Leit_Btn_Tor");
            kpp.boothMonitors = new[] { K(booth, "Monitor_Barrier"), K(booth, "Monitor_Gate") };
            kpp.leitMonitors = new[] { K(main, "Leit_Monitor_Barrier"), K(main, "Leit_Monitor_Gate") };
            kpp.cameraHeads = new[] { K(booth, "Cam_Barrier"), K(gate, "Cam_Gate") };
            kpp.floods = new[] { K(booth, "Flood_Barrier"), K(gate, "Flood_Gate") };
            Transform cb = booth != null ? Deep(booth, CameraPrefix + "Barrier") : null, cg = gate != null ? Deep(gate, CameraPrefix + "Gate") : null;
            kpp.cameras = new[] { cb != null ? cb.GetComponent<Camera>() : null, cg != null ? cg.GetComponent<Camera>() : null };
            // the booth's lamps: the core's Light_kpp_* and Glow_kpp_* (Forest_Layout prefabLights / prefabGlows, tag kpp)
            kpp.boothLights = booth != null ? booth.GetComponentsInChildren<Light>(true).Where(l => l.name.StartsWith("Light_kpp_", StringComparison.Ordinal)).OrderBy(l => l.name, StringComparer.Ordinal).ToArray() : new Light[0];
            kpp.boothGlows = booth != null ? booth.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Glow_kpp_", StringComparison.Ordinal)).OrderBy(t => t.name, StringComparer.Ordinal).Select(t => t.gameObject).ToArray() : new GameObject[0];
            kpp.fluorOn = Mat("M_Kpp_Lamp_Fluor"); kpp.fluorOff = Mat("M_Kpp_Lamp_Fluor_Off");
            kpp.bulkOn = Mat("M_Kpp_Lamp_Bulk"); kpp.bulkOff = Mat("M_Kpp_Lamp_Bulk_Off");
            kpp.lampRenderers = booth != null ? booth.GetComponentsInChildren<Renderer>(true)
                .Where(r => r.GetComponentInParent<KitItem>(true) == null && r.sharedMaterials.Any(m => m != null && (m == kpp.fluorOff || m == kpp.bulkOff || m == kpp.fluorOn || m == kpp.bulkOn))).ToArray() : new Renderer[0];
            EditorUtility.SetDirty(kpp);

            var missing = new List<string>();
            if (kpp.batterySlot == null) missing.Add("battery shelf");
            if (kpp.batteryPrefab == null) missing.Add(BatteryPrefab);
            foreach (var (k, n) in new[] { (kpp.cabinet, "PowerCabinet"), (kpp.doorStrike, "DoorLockStrike"), (kpp.beacon, "Beacon_Barrier"), (kpp.btnBarrier, "Btn_Schranke"),
                                           (kpp.btnGate, "Btn_Tor"), (kpp.leitBtnBarrier, "Leit_Btn_Schranke"), (kpp.leitBtnGate, "Leit_Btn_Tor") })
                if (k == null) missing.Add(n);
            if (kpp.boothMonitors.Any(m => m == null) || kpp.leitMonitors.Any(m => m == null)) missing.Add("monitors");
            if (kpp.cameras.Any(c => c == null)) missing.Add("cameras");
            if (kpp.boothLights.Length != 2 || kpp.boothGlows.Length != 2) missing.Add($"booth lamps ({kpp.boothLights.Length} lights, {kpp.boothGlows.Length} halos)");
            if (kpp.lampRenderers.Length == 0 || kpp.fluorOn == null || kpp.bulkOn == null) missing.Add("lamp lenses / materials");
            // where the cameras stand now against the contract's game positions (KPP_CONTRACT: CAM_Barrier, CAM_Gate)
            Data d = Load();
            string camText = string.Join("; ", (d.cameras ?? new Cam[0]).Select((c, i) =>
            {
                Camera cam = i < kpp.cameras.Length ? kpp.cameras[i] : null;
                return cam == null ? c.name + " -" : string.Format(CultureInfo.InvariantCulture, "{0} {1} (contract {2}, {3:0.000} m)", c.name, cam.transform.position, V(c.game), Vector3.Distance(cam.transform.position, V(c.game)));
            }));
            if (missing.Count > 0) Debug.LogError("[Snow] Forest KPP: not linked: " + string.Join(", ", missing));
            else Debug.Log($"[Snow] Forest KPP linked: {kpp.lampRenderers.Length} lamp renderers, cameras: {camText}.");
        }

        static Material Mat(string n) => AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/{n}.mat");

        // ------------------------------------------------------------------ ground (once, before the vegetation bake)
        class StripFlat { public string name; public float ax, az, bx, bz, height, half_width, falloff, ramp_start, ramp_end; }

        [MenuItem("Tools/Snow/Forest/KPP v2: Level Walls Ground")]
        static void LevelMenu()
        {
            if (!EditorUtility.DisplayDialog("KPP v2: Level Walls Ground", "Level the ground under the gate and barrier walls (walls/levelling.json)?\n" +
                                             "A copy of the terrain tiles goes to snow_archive/backup first. Run once, then bake the vegetation.", "Level", "Cancel")) return;
            Level();
        }

        /// <summary>walls/levelling.json: for every height sample h += w (H − h), w = across × along (KPP_CONTRACT «Стены…»).
        /// Copies the tiles to F:\ClaudeGames\snow_archive\backup\forest_terrain_kpp_&lt;date&gt; first.</summary>
        public static bool Level()
        {
            List<StripFlat> strips = ReadStrips();
            ForestTerrainBaker.BakeMeta meta = ForestTerrainBaker.LoadMeta();
            if (strips == null || strips.Count == 0 || meta == null) { Debug.LogError($"[Snow] Forest KPP levelling: {LevellingPath} or the terrain meta missing."); return false; }
            string dir = Path.Combine(@"F:\ClaudeGames\snow_archive\backup", "forest_terrain_kpp_" + DateTime.Now.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(dir);
            foreach (string f in Directory.GetFiles(Path.GetFullPath("Assets/Locations/Forest/Terrain"), "Forest_TerrainData_*.asset"))
                File.Copy(f, Path.Combine(dir, Path.GetFileName(f)), true);

            var report = new List<string>();
            for (int ix = 0; ix < ForestTerrainBaker.TilesX; ix++)
                for (int iz = 0; iz < ForestTerrainBaker.TilesZ; iz++)
                {
                    var td = AssetDatabase.LoadAssetAtPath<TerrainData>(ForestTerrainBaker.TilePath(ix, iz));
                    if (td == null) continue;
                    Vector3 o = ForestTerrainBaker.TileOrigin(ix, iz, meta.originY);
                    int res = td.heightmapResolution;
                    float step = ForestTerrainBaker.Tile / (res - 1);
                    float[,] h = td.GetHeights(0, 0, res, res);
                    int changed = 0;
                    float maxD = 0f;
                    foreach (StripFlat s in strips)
                    {
                        float reach = s.half_width + s.falloff;
                        float minX = Mathf.Min(s.ax, s.bx) - reach - Mathf.Max(s.ramp_start, s.ramp_end), maxX = Mathf.Max(s.ax, s.bx) + reach + Mathf.Max(s.ramp_start, s.ramp_end);
                        float minZ = Mathf.Min(s.az, s.bz) - reach - Mathf.Max(s.ramp_start, s.ramp_end), maxZ = Mathf.Max(s.az, s.bz) + reach + Mathf.Max(s.ramp_start, s.ramp_end);
                        int x0 = Mathf.Max(0, Mathf.FloorToInt((minX - o.x) / step)), x1 = Mathf.Min(res - 1, Mathf.CeilToInt((maxX - o.x) / step));
                        int z0 = Mathf.Max(0, Mathf.FloorToInt((minZ - o.z) / step)), z1 = Mathf.Min(res - 1, Mathf.CeilToInt((maxZ - o.z) / step));
                        if (x0 > x1 || z0 > z1) continue;
                        var a = new Vector2(s.ax, s.az);
                        Vector2 ab = new Vector2(s.bx, s.bz) - a;
                        float len = ab.magnitude;
                        Vector2 dir1 = ab / len;
                        float target = (s.height - meta.originY) / meta.sizeY;
                        for (int z = z0; z <= z1; z++)
                            for (int x = x0; x <= x1; x++)
                            {
                                Vector2 p = new Vector2(o.x + x * step, o.z + z * step) - a;
                                float along = Vector2.Dot(p, dir1);
                                float across = Mathf.Abs(dir1.x * p.y - dir1.y * p.x);
                                float wA = across <= s.half_width ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((s.half_width + s.falloff - across) / s.falloff));
                                float wL = along < 0f ? Mathf.Clamp01(1f + along / s.ramp_start) : along > len ? Mathf.Clamp01(1f - (along - len) / s.ramp_end) : 1f;
                                float w = wA * wL;
                                if (w <= 0f) continue;
                                float before = h[z, x];
                                h[z, x] = before + w * (target - before);
                                maxD = Mathf.Max(maxD, Mathf.Abs(h[z, x] - before) * meta.sizeY);
                                changed++;
                            }
                    }
                    if (changed == 0) continue;
                    td.SetHeights(0, 0, h);
                    EditorUtility.SetDirty(td);
                    report.Add(string.Format(CultureInfo.InvariantCulture, "tile {0}_{1}: {2} samples, largest change {3:0.000} m", ix, iz, changed, maxD));
                }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Snow] Forest KPP levelling ({string.Join(", ", strips.Select(s => s.name))}): {string.Join("; ", report)}; copy in {dir}.");
            return report.Count > 0;
        }

        /// <summary>levelling.json's nested arrays (line [[x, z], [x, z]]) are read by hand: JsonUtility skips jagged arrays.</summary>
        static List<StripFlat> ReadStrips()
        {
            if (!File.Exists(LevellingPath)) return null;
            string json = File.ReadAllText(LevellingPath);
            var list = new List<StripFlat>();
            int at = 0;
            while ((at = json.IndexOf("\"name\"", at, StringComparison.Ordinal)) >= 0)
            {
                int end = json.IndexOf('}', at);
                string block = json.Substring(at, end - at);
                at = end;
                float[] line = Numbers(block, "\"line\"", 4);
                if (line == null) continue;
                var s = new StripFlat
                {
                    name = Str(block, "\"name\""), ax = line[0], az = line[1], bx = line[2], bz = line[3],
                    height = Numbers(block, "\"height\"", 1)[0], half_width = Numbers(block, "\"half_width\"", 1)[0],
                    falloff = Numbers(block, "\"falloff\"", 1)[0], ramp_start = Numbers(block, "\"ramp_start\"", 1)[0],
                    ramp_end = Numbers(block, "\"ramp_end\"", 1)[0],
                };
                list.Add(s);
            }
            return list;
        }

        static string Str(string block, string key)
        {
            int k = block.IndexOf(key, StringComparison.Ordinal) + key.Length;
            int q0 = block.IndexOf('"', block.IndexOf(':', k)) + 1;
            return block.Substring(q0, block.IndexOf('"', q0) - q0);
        }

        static float[] Numbers(string block, string key, int count)
        {
            int k = block.IndexOf(key, StringComparison.Ordinal);
            if (k < 0) return null;
            k = block.IndexOf(':', k) + 1;
            var res = new float[count];
            int n = 0;
            while (n < count && k < block.Length)
            {
                while (k < block.Length && "-0123456789.".IndexOf(block[k]) < 0) k++;
                int s = k;
                while (k < block.Length && "-0123456789.eE+".IndexOf(block[k]) >= 0) k++;
                if (k > s) res[n++] = float.Parse(block.Substring(s, k - s), CultureInfo.InvariantCulture);
            }
            return n == count ? res : null;
        }

        // ------------------------------------------------------------------ helpers
        static Vector3 V(float[] a) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;

        static Transform Deep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform r = Deep(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
