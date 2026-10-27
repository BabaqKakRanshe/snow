using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Snow.Levels.Forest;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Snow.EditorTools
{
    /// <summary>
    /// Forest integration, the start post at the SPERRGEBIET gate (user 25.10, docs/gdd/QUESTIONS_START.md): a lay-by for
    /// 2–3 cars on the uphill side of the road, old asphalt patched with gravel, jersey blocks at its end, signs at the gate,
    /// the forest_start pack (booth, abandoned car, tripod work light on its battery, tyres, rubbish: StartPropsBuilder; the
    /// lamps' <see cref="ForestStartLamp"/> rides in their prefabs) and props of the project's kits. Read from
    /// <see cref="DataPath"/> in the gate's own frame. The only edits outside the dress root are the terrain inside the data's
    /// level rect and its fades (heights to the stored padY, ground paint, grass, the trees standing there); a copy of the
    /// touched tiles goes to the archive before the first edit of a day. Nothing else of the map is touched, and a second run
    /// gives the same result. Runs after <see cref="ForestYardDressStep"/>; Tools / Snow / Forest / Start Dress runs it alone.
    /// </summary>
    public static class ForestStartDressStep
    {
        public const string DataPath = "Assets/Snow/Levels/Forest/ForestStartDress.json";
        public const string RootName = "ForestStartDress";
        const string BackupRoot = @"F:\ClaudeGames\snow_archive\backup";

        /// <summary>A prop at frame u, v, turned by yaw; <c>part</c> (optional) stands turned by <c>open</c> degrees about its
        /// hinge (the axis of its SkyIslandInteractable): the barrier's arm raised, static until the post gets its interaction.</summary>
        [Serializable] public class Prop { public string prefab, part; public float u, v, yaw, open; public PropLamp lamp; }
        /// <summary>A prop's lights set by the data (start post rework 27.10): colour, intensity, range, battery minutes
        /// (0 = the map's lampLifeMinutes). A prop whose lights belong to the base's power (Light_power_* / Glow_power_*, the
        /// yard's LightPole) gets them renamed Light_post_* / Glow_post_* and its own <see cref="ForestStartLamp"/>: battery, not grid.</summary>
        [Serializable] public class PropLamp { public float[] color; public float intensity, range, life; }
        [Serializable] public class Spot { public float u, v, r; public int n; }
        [Serializable] public class Fuse { public string prefab, on, slot; }
        /// <summary>The far halo on the post's lamps (user 27.10, QUESTIONS_6 §7а 16): the hut's beacon glow (the fog dims it
        /// less than the light), so the post reads from the start ~80 m off; out with its lamp (the lamp's glows).</summary>
        [Serializable] public class Beacon { public float[] color; public float size, intensity, fogFloor, nearFade; }
        // lampLifeMinutes: the post lamps' battery on this map (0 = the prefab's); with the 17:30 start they must outlast the dusk
        [Serializable]
        public class Data
        {
            public float[] centre, level, asphalt, gravel;
            public float yaw, roadFade, outerFade, padY, shoulder, clearMargin;
            public Prop[] props;
            public string[] clumpPrefabs;
            public Spot[] clumps;
            public Fuse fuse;
            public Beacon beacon;
            public float lampLifeMinutes;
            // false (start post rework 27.10): the ground is Blender's (world sync, tools/blender_context/edits/forest_start_pad.py);
            // this step keeps paint, grass, Terrain trees and props only
            public bool levelGround = true;
        }

        static Data _d;
        static float _cos, _sin;

        [MenuItem("Tools/Snow/Forest/Start Dress")]
        static void RunMenu()
        {
            Transform root = GameObject.Find("Forest")?.transform;
            if (root == null) { Debug.LogError("[Snow] Forest start dress: no Forest root in the open scene."); return; }
            Run(root);
        }

        public static void Run(Transform root)
        {
            if (!File.Exists(DataPath)) { Debug.LogWarning($"[Snow] Forest start dress: {DataPath} not found; the gate stays as it is."); return; }
            _d = JsonUtility.FromJson<Data>(File.ReadAllText(DataPath));
            _cos = Mathf.Cos(_d.yaw * Mathf.Deg2Rad);
            _sin = Mathf.Sin(_d.yaw * Mathf.Deg2Rad);
            Transform structures = root.Find("Structures");
            var tiles = Terrain.activeTerrains.Where(t => t != null && t.terrainData != null && t.transform.IsChildOf(root) && Overlaps(t)).ToArray();
            if (tiles.Length == 0) { Debug.LogWarning("[Snow] Forest start dress: no baked terrain under the gate; nothing done."); return; }

            // the pad's height: measured once on its road-side edge, then kept in the data (a rerun must not measure its own pad)
            if (_d.levelGround && _d.padY == 0f)
            {
                float sum = 0f;
                int n = 0;
                for (float v = _d.level[1]; v <= _d.level[3]; v += 1f) { sum += Height(World(_d.level[2], v)); n++; }
                _d.padY = (float)Math.Round(sum / n, 2);
                SavePadY(_d.padY);
            }

            Backup(tiles);
            int levelled = 0, painted = 0, cells = 0, removed = 0, snapped = 0;
            foreach (Terrain t in tiles)
            {
                if (_d.levelGround) levelled += Level(t);
                painted += Paint(t);
                cells += Details(t);
                (int r, int s) = Trees(t);
                removed += r;
                snapped += s;
                EditorUtility.SetDirty(t.terrainData);
            }
            AssetDatabase.SaveAssets();

            Transform old = root.Find(RootName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var dress = new GameObject(RootName).transform;
            dress.SetParent(root, false);
            Transform reference = structures != null ? structures.Find("Sign_Observatory") ?? structures.Find("ZoneGate") : null;
            int props = 0, clumps = 0, missing = 0;
            _ownLife.Clear();
            foreach (Prop p in _d.props)
            {
                GameObject placed = Place(AssetDatabase.LoadAssetAtPath<GameObject>(p.prefab), dress, p.u, p.v, p.yaw, reference);
                if (placed == null) { missing++; continue; }
                if (!string.IsNullOrEmpty(p.part)) Pose(placed.transform, p.part, p.open);
                if (p.lamp != null && ((p.lamp.color?.Length ?? 0) >= 3 || p.lamp.intensity > 0f || p.lamp.range > 0f || p.lamp.life > 0f))
                    Lamp(placed, p.lamp);                               // JsonUtility makes an empty one for every prop
                props++;
            }
            clumps = PlaceClumps(dress, reference, ref missing);
            string fuse = LinkFuse(root, dress);
            int halos = PostHalos(dress);

            Debug.Log($"[Snow] Forest start dress ({DataPath}): pad at {_d.padY:0.00} m, {levelled} height samples levelled, {painted} ground texels and " +
                      $"{cells} grass cells rewritten, {removed} Terrain trees removed from the pad, {snapped} put back on the new ground, " +
                      $"{props} props, {clumps} grass clumps, {halos} far halos on the post lamps, {fuse}" + (missing > 0 ? $", {missing} prefabs MISSING" : "") + ".");
        }

        const string BeaconMaterial = "Assets/Locations/Forest/Start/Materials/M_Start_BeaconGlow.mat";

        /// <summary>A camera-facing far halo on every light of the post's lamps (<see cref="ForestStartLamp"/> in the placed
        /// prefabs), added to that lamp's glows so it dips and dies with it.</summary>
        static int PostHalos(Transform dress)
        {
            Beacon b = _d.beacon;
            if (b == null || b.color == null || b.size <= 0f) return 0;
            var beaconColor = new Color(b.color[0], b.color[1], b.color[2]);
            var mats = new Dictionary<string, Material>();
            // the halo takes its lamp's colour (the warm pole lamp gets a warm one); the beacon colour is the cold lamps'
            Material MatFor(Color c)
            {
                bool same = Mathf.Abs(c.r - beaconColor.r) + Mathf.Abs(c.g - beaconColor.g) + Mathf.Abs(c.b - beaconColor.b) < 0.15f;
                string path = same ? BeaconMaterial : BeaconMaterial.Replace(".mat", "_" + ColorUtility.ToHtmlStringRGB(c) + ".mat");
                if (!mats.TryGetValue(path, out Material m)) mats[path] = m = ForestHutDressStep.GlowMat(path, same ? beaconColor : c, b.intensity, b.fogFloor, b.nearFade);
                return m;
            }
            var quad = AssetDatabase.LoadAssetAtPath<Mesh>(ForestHutDressStep.GlowQuad);
            int n = 0;
            foreach (ForestStartLamp lamp in dress.GetComponentsInChildren<ForestStartLamp>(true))
            {
                var glows = new List<Renderer>(lamp.glows ?? new Renderer[0]);
                foreach (Light l in lamp.lights ?? new Light[0])
                {
                    if (l == null) continue;
                    var go = new GameObject("Beacon_Post");
                    go.transform.SetParent(l.transform, false);
                    go.transform.localScale = Vector3.one * b.size;
                    go.AddComponent<MeshFilter>().sharedMesh = quad;
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterial = MatFor(l.color);
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    r.lightProbeUsage = LightProbeUsage.Off;
                    r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    r.enabled = false;                                   // the lamp lights it (Show)
                    glows.Add(r);
                    n++;
                }
                lamp.glows = glows.ToArray();
                if (_d.lampLifeMinutes > 0f && !_ownLife.Contains(lamp)) lamp.lifeMinutes = _d.lampLifeMinutes;
                EditorUtility.SetDirty(lamp);
            }
            return n;
        }

        /// <summary>The van's spare headlight fuse on the booth's desk (user 25.10): ForestOpening spawns it there at the start.</summary>
        static string LinkFuse(Transform root, Transform dress)
        {
            ForestOpening fo = root.GetComponentInChildren<ForestOpening>(true);
            if (_d.fuse == null || string.IsNullOrEmpty(_d.fuse.prefab))
            {
                // no spare on the desk (user 27.10: the van needs only fuel): an earlier link is cleared
                if (fo != null && (fo.startFusePrefab != null || fo.startFuseSpawn != null))
                {
                    fo.startFusePrefab = null;
                    fo.startFuseSpawn = null;
                    EditorUtility.SetDirty(fo);
                }
                return "no spare fuse";
            }
            Transform on = dress.Find(_d.fuse.on);
            Transform slot = on != null ? on.Find(_d.fuse.slot) : null;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(_d.fuse.prefab);
            if (fo == null || slot == null || prefab == null || !prefab.TryGetComponent(out Unity.Netcode.NetworkObject no))
                return $"spare fuse NOT linked ({(fo == null ? "no ForestOpening" : slot == null ? $"no {_d.fuse.on}/{_d.fuse.slot}" : $"no NetworkObject prefab {_d.fuse.prefab}")})";
            fo.startFusePrefab = no;
            fo.startFuseSpawn = slot;
            EditorUtility.SetDirty(fo);
            return $"spare fuse on {_d.fuse.on}/{_d.fuse.slot}";
        }

        // ------------------------------------------------------------------ frame

        static Vector3 World(float u, float v) => new Vector3(_d.centre[0] + u * _cos + v * _sin, 0f, _d.centre[1] - u * _sin + v * _cos);

        static Vector2 Frame(float x, float z)
        {
            float dx = x - _d.centre[0], dz = z - _d.centre[1];
            return new Vector2(dx * _cos - dz * _sin, dx * _sin + dz * _cos);
        }

        /// <summary>Signed depth of a point inside a rect (negative outside), metres.</summary>
        static float Inside(float[] r, Vector2 p) => Mathf.Min(Mathf.Min(p.x - r[0], r[2] - p.x), Mathf.Min(p.y - r[1], r[3] - p.y));

        /// <summary>How much of the pad's height a point takes: 1 inside the level rect, fading out over roadFade on the road
        /// side (+u) and outerFade on the others.</summary>
        static float LevelShare(Vector2 p)
        {
            float[] r = _d.level;
            float out1 = Mathf.Max(0f, r[0] - p.x) / _d.outerFade, out2 = Mathf.Max(0f, p.x - r[2]) / _d.roadFade;
            float out3 = Mathf.Max(0f, r[1] - p.y) / _d.outerFade, out4 = Mathf.Max(0f, p.y - r[3]) / _d.outerFade;
            float k = new Vector2(Mathf.Max(out1, out2), Mathf.Max(out3, out4)).magnitude;
            return 1f - Mathf.SmoothStep(0f, 1f, k);
        }

        /// <summary>The level rect grown by the largest fade: everything this step may change on the ground.</summary>
        static bool InReach(Vector2 p)
        {
            float g = Mathf.Max(_d.roadFade, _d.outerFade);
            float[] r = _d.level;
            return p.x > r[0] - g && p.x < r[2] + g && p.y > r[1] - g && p.y < r[3] + g;
        }

        static bool Overlaps(Terrain t)
        {
            Vector3 o = t.transform.position, s = t.terrainData.size;
            float g = Mathf.Max(_d.roadFade, _d.outerFade);
            foreach (float u in new[] { _d.level[0] - g, _d.level[2] + g })
                foreach (float v in new[] { _d.level[1] - g, _d.level[3] + g })
                {
                    Vector3 w = World(u, v);
                    if (w.x >= o.x && w.x <= o.x + s.x && w.z >= o.z && w.z <= o.z + s.z) return true;
                }
            return false;
        }

        static float Height(Vector3 w)
        {
            foreach (Terrain t in Terrain.activeTerrains)
            {
                Vector3 o = t.transform.position, s = t.terrainData.size;
                if (w.x >= o.x && w.z >= o.z && w.x <= o.x + s.x && w.z <= o.z + s.z) return o.y + t.SampleHeight(w);
            }
            return w.y;
        }

        static void SavePadY(float y)
        {
            string text = File.ReadAllText(DataPath);
            text = System.Text.RegularExpressions.Regex.Replace(text, "\"padY\":\\s*[-0-9.eE]+", $"\"padY\": {y.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            File.WriteAllText(DataPath, text);
        }

        /// <summary>A copy of the tiles this step edits, once a day, before the edit.</summary>
        static void Backup(Terrain[] tiles)
        {
            string dir = Path.Combine(BackupRoot, "forest_start_" + DateTime.Now.ToString("yyyyMMdd"));
            if (Directory.Exists(dir)) return;
            Directory.CreateDirectory(dir);
            foreach (Terrain t in tiles)
            {
                string path = AssetDatabase.GetAssetPath(t.terrainData);
                if (!string.IsNullOrEmpty(path)) File.Copy(path, Path.Combine(dir, Path.GetFileName(path)), true);
            }
            Debug.Log($"[Snow] Forest start dress: terrain tiles copied to {dir} before the edit.");
        }

        // ------------------------------------------------------------------ ground

        /// <summary>Heights of the reach before the first run, per tile (the fades lerp from them, so a rerun or a run after
        /// the integration gives the same ground). Written once; a re-bake gives the same heights back, so it stays valid.</summary>
        static string GroundPath => Path.ChangeExtension(DataPath, null) + "_ground.bytes";

        static float[,] Original(TerrainData td, int x0, int z0, int w, int h)
        {
            string path = GroundPath;
            var all = new Dictionary<string, (int x0, int z0, float[,] h)>();
            if (File.Exists(path))
                using (var r = new BinaryReader(File.OpenRead(path)))
                    for (int n = r.ReadInt32(), i = 0; i < n; i++)
                    {
                        string name = r.ReadString();
                        int ax = r.ReadInt32(), az = r.ReadInt32(), aw = r.ReadInt32(), ah = r.ReadInt32();
                        var a = new float[ah, aw];
                        for (int z = 0; z < ah; z++) for (int x = 0; x < aw; x++) a[z, x] = r.ReadSingle();
                        all[name] = (ax, az, a);
                    }
            if (all.TryGetValue(td.name, out var kept) && kept.x0 == x0 && kept.z0 == z0 && kept.h.GetLength(1) == w && kept.h.GetLength(0) == h)
                return kept.h;
            if (all.ContainsKey(td.name))
                Debug.LogWarning($"[Snow] Forest start dress: the stored ground of {td.name} has another extent (the data's level rect changed); " +
                                 "measuring again from the current ground, which already carries the old pad.");
            float[,] now = td.GetHeights(x0, z0, w, h);
            all[td.name] = (x0, z0, now);
            using (var wr = new BinaryWriter(File.Create(path)))
            {
                wr.Write(all.Count);
                foreach (var kv in all)
                {
                    wr.Write(kv.Key);
                    wr.Write(kv.Value.x0); wr.Write(kv.Value.z0); wr.Write(kv.Value.h.GetLength(1)); wr.Write(kv.Value.h.GetLength(0));
                    foreach (float f in kv.Value.h) wr.Write(f);   // row-major: z outer, x inner
                }
            }
            return now;
        }

        static int Level(Terrain t)
        {
            TerrainData td = t.terrainData;
            int res = td.heightmapResolution, count = 0;
            float step = td.size.x / (res - 1);
            Vector3 o = t.transform.position;
            float target = (_d.padY - o.y) / td.size.y;
            // the reach's index box on this tile
            int x0 = res, z0 = res, x1 = -1, z1 = -1;
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    if (InReach(Frame(o.x + x * step, o.z + z * step)))
                    {
                        x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x);
                        z0 = Mathf.Min(z0, z); z1 = Mathf.Max(z1, z);
                    }
            if (x1 < 0) return 0;
            int w = x1 - x0 + 1, hgt = z1 - z0 + 1;
            float[,] orig = Original(td, x0, z0, w, hgt);
            var h = new float[hgt, w];
            for (int z = 0; z < hgt; z++)
                for (int x = 0; x < w; x++)
                {
                    h[z, x] = orig[z, x];
                    Vector2 p = Frame(o.x + (x0 + x) * step, o.z + (z0 + z) * step);
                    float k = InReach(p) ? LevelShare(p) : 0f;
                    if (k <= 0f) continue;
                    h[z, x] = Mathf.Lerp(orig[z, x], target, k);
                    count++;
                }
            td.SetHeights(x0, z0, h);
            return count;
        }

        static int Layer(TerrainData td, string name) => Array.FindIndex(td.terrainLayers, l => l != null && l.name == name);

        static float Edge(float depth, float soft) => Mathf.Clamp01(depth / soft + 0.5f);

        /// <summary>Old asphalt (broken into patches) on the pad, gravel around it and in the cracks; edges wander.</summary>
        static (float asphalt, float gravel) Sample(Vector2 p, Vector3 w)
        {
            float n = Mathf.PerlinNoise(w.x / 1.7f + 3.1f, w.z / 1.7f + 8.3f) * 0.65f + Mathf.PerlinNoise(w.x / 0.6f + 1.9f, w.z / 0.6f + 4.4f) * 0.35f - 0.5f;
            float big = Mathf.PerlinNoise(w.x / 6f + 9.2f, w.z / 6f + 1.4f) - 0.5f;   // breaks the rect's straight edges by ~1 m
            float worn = Mathf.Clamp01((Mathf.PerlinNoise(w.x / 3.1f + 6.7f, w.z / 3.1f + 2.9f) - 0.3f) * 3f);
            float asphalt = Edge(Inside(_d.asphalt, p) + n * 0.5f + big * 1.2f, 0.3f) * worn;
            float gravel = Mathf.Max(Edge(Inside(_d.gravel, p) + n * 0.9f + big * 2.2f, 0.6f), Edge(Inside(_d.asphalt, p) + _d.shoulder + n * 0.9f, 0.4f));
            return (asphalt, gravel * (1f - asphalt));
        }

        /// <summary>The bake's paint of the reach before the first run, per tile, like <see cref="Original"/> for heights.</summary>
        static string PaintPath => Path.ChangeExtension(DataPath, null) + "_paint.bytes";

        static float[,,] OriginalPaint(TerrainData td, int x0, int z0, int w, int h)
        {
            int layers = td.alphamapLayers;
            var all = new Dictionary<string, (int x0, int z0, float[,,] a)>();
            if (File.Exists(PaintPath))
                using (var r = new BinaryReader(File.OpenRead(PaintPath)))
                    for (int n = r.ReadInt32(), i = 0; i < n; i++)
                    {
                        string name = r.ReadString();
                        int ax = r.ReadInt32(), az = r.ReadInt32(), aw = r.ReadInt32(), ah = r.ReadInt32(), al = r.ReadInt32();
                        var a = new float[ah, aw, al];
                        for (int z = 0; z < ah; z++) for (int x = 0; x < aw; x++) for (int l = 0; l < al; l++) a[z, x, l] = r.ReadSingle();
                        all[name] = (ax, az, a);
                    }
            if (all.TryGetValue(td.name, out var kept) && kept.x0 == x0 && kept.z0 == z0 && kept.a.GetLength(1) == w && kept.a.GetLength(0) == h && kept.a.GetLength(2) == layers)
                return kept.a;
            if (all.ContainsKey(td.name))
                Debug.LogWarning($"[Snow] Forest start dress: the stored paint of {td.name} has another extent; measuring again from the current paint.");
            float[,,] now = td.GetAlphamaps(x0, z0, w, h);
            all[td.name] = (x0, z0, now);
            using (var wr = new BinaryWriter(File.Create(PaintPath)))
            {
                wr.Write(all.Count);
                foreach (var kv in all)
                {
                    wr.Write(kv.Key);
                    wr.Write(kv.Value.x0); wr.Write(kv.Value.z0); wr.Write(kv.Value.a.GetLength(1)); wr.Write(kv.Value.a.GetLength(0)); wr.Write(kv.Value.a.GetLength(2));
                    foreach (float f in kv.Value.a) wr.Write(f);
                }
            }
            return now;
        }

        static int Paint(Terrain t)
        {
            TerrainData td = t.terrainData;
            int gravel = Layer(td, "TL_RoadGravel"), asphalt = Layer(td, "TL_Asphalt");
            if (gravel < 0 || asphalt < 0)
            {
                Debug.LogWarning($"[Snow] Forest start dress: {td.name} lacks the bake's terrain layers; no ground paint.");
                return 0;
            }
            int n = td.alphamapResolution, layers = td.alphamapLayers, count = 0;
            float tx = td.size.x / n;
            Vector3 o = t.transform.position;
            int x0 = n, z0 = n, x1 = -1, z1 = -1;
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                    if (InReach(Frame(o.x + (x + 0.5f) * tx, o.z + (z + 0.5f) * tx)))
                    {
                        x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x);
                        z0 = Mathf.Min(z0, z); z1 = Mathf.Max(z1, z);
                    }
            if (x1 < 0) return 0;
            float[,,] orig = OriginalPaint(td, x0, z0, x1 - x0 + 1, z1 - z0 + 1);
            var map = (float[,,])orig.Clone();
            for (int z = 0; z < map.GetLength(0); z++)
                for (int x = 0; x < map.GetLength(1); x++)
                {
                    var w = new Vector3(o.x + (x0 + x + 0.5f) * tx, 0f, o.z + (z0 + z + 0.5f) * tx);
                    Vector2 p = Frame(w.x, w.z);
                    if (!InReach(p)) continue;
                    (float a, float g) = Sample(p, w);
                    if (a + g <= 0f) continue;
                    // asphalt and gravel only grow (max), the bake's other layers keep their ratios in what is left: a rerun
                    // changes nothing, the shoulders blend into the road and the floor
                    float na = Mathf.Max(map[z, x, asphalt], a), ng = Mathf.Min(Mathf.Max(map[z, x, gravel], g), 1f - na);
                    float other = 0f;
                    for (int l = 0; l < layers; l++) if (l != asphalt && l != gravel) other += map[z, x, l];
                    float rest = 1f - na - ng;
                    if (other <= 1e-5f) ng += rest;
                    for (int l = 0; l < layers; l++)
                        if (l != asphalt && l != gravel) map[z, x, l] = other > 1e-5f ? map[z, x, l] / other * rest : 0f;
                    map[z, x, asphalt] = na;
                    map[z, x, gravel] = ng;
                    count++;
                }
            td.SetAlphamaps(x0, z0, map);
            return count;
        }

        static float Hash(int x, int z, int salt)
        {
            uint h = (uint)(x * 73856093) ^ (uint)(z * 19349663) ^ (uint)(salt * 83492791);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return (h & 0xffffff) / (float)0x1000000;
        }

        /// <summary>No grass, ferns or twigs on the hard ground; a few pebbles on the gravel.</summary>
        static int Details(Terrain t)
        {
            TerrainData td = t.terrainData;
            DetailPrototype[] protos = td.detailPrototypes;
            int[] stones = { Array.FindIndex(protos, d => d.prototype != null && d.prototype.name == "Pebbles_1"), Array.FindIndex(protos, d => d.prototype != null && d.prototype.name == "Pebbles_2") };
            int n = td.detailResolution, count = 0;
            float cell = td.size.x / n;
            Vector3 o = t.transform.position;
            var maps = new int[protos.Length][,];
            for (int l = 0; l < protos.Length; l++) maps[l] = td.GetDetailLayer(0, 0, n, n, l);
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    var w = new Vector3(o.x + (x + 0.5f) * cell, 0f, o.z + (z + 0.5f) * cell);
                    Vector2 p = Frame(w.x, w.z);
                    if (!InReach(p)) continue;
                    (float a, float g) = Sample(p, w);
                    if (a + g < 0.35f) continue;
                    for (int l = 0; l < protos.Length; l++) maps[l][z, x] = 0;
                    count++;
                    int s = stones[(int)(Hash(x, z, 2) * 2f) % 2];
                    if (g > 0.5f && s >= 0 && Hash(x, z, 1) < 0.1f) maps[s][z, x] = 1;
                }
            for (int l = 0; l < protos.Length; l++) td.SetDetailLayer(0, 0, l, maps[l]);
            return count;
        }

        /// <summary>Trees, rocks and stumps on the pad (and a margin round it) go; the ones on the fades are put back on the
        /// new ground. Fence posts stay where they are.</summary>
        static (int removed, int snapped) Trees(Terrain t)
        {
            TerrainData td = t.terrainData;
            TreePrototype[] protos = td.treePrototypes;
            Vector3 o = t.transform.position, s = td.size;
            var keep = new List<TreeInstance>();
            int removed = 0, snapped = 0;
            float[] pad = { _d.level[0] - _d.clearMargin, _d.level[1] - _d.clearMargin, _d.level[2] + _d.clearMargin, _d.level[3] + _d.clearMargin };
            foreach (TreeInstance ti in td.treeInstances)
            {
                TreeInstance tree = ti;
                string nm = protos[ti.prototypeIndex].prefab != null ? protos[ti.prototypeIndex].prefab.name : "";
                Vector2 p = Frame(o.x + ti.position.x * s.x, o.z + ti.position.z * s.z);
                bool fence = nm.StartsWith("OldFence", StringComparison.Ordinal) || nm.StartsWith("EuroPost", StringComparison.Ordinal) || nm.StartsWith("FenceChain", StringComparison.Ordinal);
                if (!fence && Inside(pad, p) > 0f) { removed++; continue; }
                if (InReach(p) && LevelShare(p) > 0f)
                {
                    tree.position = new Vector3(ti.position.x, td.GetInterpolatedHeight(ti.position.x, ti.position.z) / s.y, ti.position.z);
                    snapped++;
                }
                keep.Add(tree);
            }
            td.SetTreeInstances(keep.ToArray(), false);
            return (removed, snapped);
        }

        // ------------------------------------------------------------------ objects

        /// <summary>A prefab instance on the ground at a frame point, turned by the frame's yaw plus its own; layer and static
        /// flags taken from a structure of the gate.</summary>
        static readonly HashSet<ForestStartLamp> _ownLife = new HashSet<ForestStartLamp>();

        /// <summary>The data's colour, intensity, range and battery on a prop's lights (<see cref="PropLamp"/>).</summary>
        static void Lamp(GameObject go, PropLamp d)
        {
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Light_power", StringComparison.Ordinal) || t.name.StartsWith("Glow_power", StringComparison.Ordinal))
                    t.name = t.name.Replace("_power", "_post");
            Light[] lights = go.GetComponentsInChildren<Light>(true);
            if (!go.TryGetComponent(out ForestStartLamp lamp))
            {
                lamp = go.AddComponent<ForestStartLamp>();
                lamp.lights = lights;
                lamp.glows = go.GetComponentsInChildren<Renderer>(true).Where(r => r.name.StartsWith("Glow_", StringComparison.Ordinal)).ToArray();
                lamp.lens = null;
            }
            foreach (Light l in lights)
            {
                if (d.color != null && d.color.Length >= 3) l.color = new Color(d.color[0], d.color[1], d.color[2]);
                if (d.range > 0f) l.range = d.range;
                if (d.intensity > 0f) l.intensity = d.intensity;
            }
            if (d.intensity > 0f) lamp.intensity = d.intensity;
            if (d.life > 0f) { lamp.lifeMinutes = d.life; _ownLife.Add(lamp); }
            EditorUtility.SetDirty(lamp);
        }

        static GameObject Place(GameObject asset, Transform parent, float u, float v, float yaw, Transform reference)
        {
            if (asset == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            Vector3 w = World(u, v);
            go.transform.SetPositionAndRotation(new Vector3(w.x, Height(w), w.z), Quaternion.Euler(0f, _d.yaw + yaw, 0f));
            int layer = reference != null ? reference.gameObject.layer : go.layer;
            StaticEditorFlags flags = reference != null ? GameObjectUtility.GetStaticEditorFlags(reference.gameObject) : 0;
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = layer;
                if (reference != null) GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
            }
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true)) r.renderingLayerMask = 2u;   // the map's lights
            return go;
        }

        /// <summary>A part turned about its hinge at the rest of its interactable (openAmount stays 0, so the game keeps the pose).</summary>
        static void Pose(Transform root, string part, float degrees)
        {
            Transform t = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == part);
            if (t == null) { Debug.LogWarning($"[Snow] Forest start dress: part {part} not found in {root.name}."); return; }
            Vector3 axis = t.TryGetComponent(out SkyIsland.SkyIslandInteractable ix) && ix.axis.sqrMagnitude > 1e-8f ? ix.axis.normalized : Vector3.up;
            t.localRotation = Quaternion.AngleAxis(degrees, axis) * t.localRotation;
        }

        static int PlaceClumps(Transform parent, Transform reference, ref int missing)
        {
            if (_d.clumpPrefabs == null || _d.clumps == null) return 0;
            var assets = _d.clumpPrefabs.Select(AssetDatabase.LoadAssetAtPath<GameObject>).Where(a => a != null).ToArray();
            missing += _d.clumpPrefabs.Length - assets.Length;
            if (assets.Length == 0) return 0;
            var rng = new System.Random(2510);
            int count = 0;
            foreach (Spot c in _d.clumps)
                for (int i = 0; i < c.n; i++)
                {
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f, d = c.r * Mathf.Sqrt((float)rng.NextDouble());
                    GameObject go = Place(assets[rng.Next(assets.Length)], parent, c.u + Mathf.Cos(a) * d, c.v + Mathf.Sin(a) * d, (float)rng.NextDouble() * 360f, reference);
                    float sc = 0.8f + (float)rng.NextDouble() * 0.5f;
                    go.transform.localScale = new Vector3(sc, sc, sc);
                    count++;
                }
            return count;
        }
    }
}
