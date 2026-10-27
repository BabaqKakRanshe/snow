using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Snow.Levels.Forest;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Snow.EditorTools
{
    /// <summary>
    /// Forest integration, the forester's hut (user 25.10, docs/gdd/QUESTIONS_HUT.md 1, 2, 4, 5): a trodden trail from the
    /// road at Car_Start up to the hut door (the bake's trail layers, worn middle, no grass on it), the hut's name sign at the
    /// road as the pointer, the candle on the table lit (a <see cref="ForestStartLamp"/> that only gutters) and put out with
    /// the lantern at X01, and X01 itself moved to the moment the jerrycan leaves the hut (<see cref="ForestOpening"/>
    /// x01OutsideRadius). Read from <see cref="DataPath"/> in world coordinates. The only edit outside the dress root is the
    /// paint and the grass along the trail (from the stored original, so a rerun gives the same ground); no heights change and
    /// trees are removed only where a trunk stands on the trail (the data's route goes between them). Runs after
    /// <see cref="ForestStartDressStep"/>; Tools / Snow / Forest / Hut Dress runs it alone.
    /// </summary>
    public static class ForestHutDressStep
    {
        public const string DataPath = "Assets/Snow/Levels/Forest/ForestHutDress.json";
        public const string RootName = "ForestHutDress", EarlyRootName = "ForestHutYard";
        const string GlowMaterial = "Assets/Locations/Forest/Start/Materials/M_Hut_CandleGlow.mat";
        internal const string GlowQuad = "Assets/Kit/Materials/Kit_GlowQuad.asset";
        const string LightLayersFrom = "Assets/Locations/Forest/Prefabs/LightPole.prefab";

        [Serializable] public class Trail { public float w; public float[] p; }
        /// <summary>A prefab on the ground at world x, z; with <c>anchor</c> (a path under the Forest root) at the anchor's local
        /// point <c>local</c> instead, turned with it (the porch at the hut front). <c>early</c>: a prop with game doors (the
        /// outhouse), placed by <see cref="PlaceEarly"/> before the Forest integration gathers the parts, under its own root
        /// that this step keeps (only its heights follow the terrain); <c>y</c> is its height until the terrain is there.</summary>
        [Serializable] public class Prop { public string prefab, anchor; public float x, y, z, yaw; public float[] local; public bool early; }
        [Serializable] public class Candle { public string anchor; public float[] local, color; public float intensity, range, glow; }
        [Serializable] public class Prints { public string material; public float[] uv, p; public float size, step; }
        [Serializable] public class Turn { public float yaw, radius; public string[] objects; }
        [Serializable] public class Beacon { public string light; public float[] color; public float size, intensity, fogFloor, nearFade, sway, swayPeriod; }
        [Serializable] public class Pointer { public float x, z, height, intensity, range, glow; public float[] color; }
        [Serializable] class Placed { public string m; public float[] p, q; }
        [Serializable] class Layout { public Placed[] prefabs; }
        const string LayoutPath = "Assets/Locations/Forest/Data/Forest_Layout.json";
        [Serializable]
        public class Data
        {
            public Trail trail;
            public float clearRadius, x01OutsideRadius;
            public Prop[] props;
            public Candle candle;
            public Prints prints;
            public Turn turn;
            public Beacon beacon;
            public Pointer pointer;
            public string[] x01Items;
            /// <summary>Ground lowered in the hut's own frame (u = its local X, v = its local Z, y = metres above the hut's
            /// origin) after the turn: the porch deck sits at the hut floor, so the ground in front of the door goes down to
            /// it (user 27.10). ForestGroundEdit keeps the ground before the first run, a rerun gives the same result.</summary>
            public ForestGroundEdit.Rect[] ground;
        }

        static Data _d;

        [MenuItem("Tools/Snow/Forest/Hut Dress")]
        static void RunMenu()
        {
            Transform root = GameObject.Find("Forest")?.transform;
            if (root == null) { Debug.LogError("[Snow] Forest hut dress: no Forest root in the open scene."); return; }
            Run(root);
        }

        public static void Run(Transform root)
        {
            if (!File.Exists(DataPath)) { Debug.LogWarning($"[Snow] Forest hut dress: {DataPath} not found; the hut stays as it is."); return; }
            _d = JsonUtility.FromJson<Data>(File.ReadAllText(DataPath));
            Transform structures = root.Find("Structures");
            string turned = TurnHut(root, structures);
            string ground = Ground(root, structures);
            var tiles = Terrain.activeTerrains.Where(t => t != null && t.terrainData != null && t.transform.IsChildOf(root) && Overlaps(t)).ToArray();
            int painted = 0, cells = 0, removed = 0;
            foreach (Terrain t in tiles)
            {
                painted += Paint(t);
                cells += Details(t);
                removed += Trees(t);
                EditorUtility.SetDirty(t.terrainData);
            }
            AssetDatabase.SaveAssets();

            Transform old = root.Find(RootName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var dress = new GameObject(RootName).transform;
            dress.SetParent(root, false);
            Transform reference = structures != null ? structures.Find("Sign_Forsthuette") : null;
            int props = 0, missing = 0;
            string early = EarlyHeights(root);
            foreach (Prop p in _d.props ?? new Prop[0])
            {
                if (p.early) continue;
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(p.prefab);
                Transform anchor = string.IsNullOrEmpty(p.anchor) ? null : root.Find(p.anchor);
                if (!string.IsNullOrEmpty(p.anchor) && anchor == null) Debug.LogWarning($"[Snow] Forest hut dress: anchor {p.anchor} of {p.prefab} not found.");
                GameObject go = anchor != null && p.local != null && p.local.Length >= 3
                    ? PlaceAt(asset, dress, anchor.TransformPoint(V(p.local)), anchor.rotation * Quaternion.Euler(0f, p.yaw, 0f), reference)
                    : anchor == null && string.IsNullOrEmpty(p.anchor) ? Place(asset, dress, p.x, p.z, p.yaw, reference) : null;
                if (go == null) { missing++; continue; }
                props++;
            }
            // the yard pack's lamps ride in their prefabs (the pointer's lantern): out at X01 with the rest
            ForestStartLamp[] propLamps = dress.GetComponentsInChildren<ForestStartLamp>(true);
            int prints = PlacePrints(dress);
            Transform hut = _d.candle != null ? root.Find(_d.candle.anchor) : null;
            ForestStartLamp candle = hut != null ? MakeCandle(dress, hut) : null;
            ForestStartLamp beacon = hut != null ? MakeBeacon(hut) : null;
            ForestStartLamp pointer = MakePointer(dress);
            ForestOpening fo = root.GetComponentInChildren<ForestOpening>(true);
            if (fo != null)
            {
                fo.x01Lamps = new[] { candle, beacon, pointer }.Concat(propLamps).Where(l => l != null).ToArray();
                fo.x01Hut = hut;
                fo.x01OutsideRadius = _d.x01OutsideRadius;
                if (_d.x01Items != null && _d.x01Items.Length > 0) fo.x01Items = _d.x01Items;
                EditorUtility.SetDirty(fo);
            }

            Debug.Log($"[Snow] Forest hut dress ({DataPath}): {turned}, {ground}, trail of {Length():0} m, {painted} ground texels and {cells} grass cells rewritten, " +
                      $"{removed} Terrain trees on the trail removed, {props} props, {early}, {prints} boot prints, candle {(candle != null ? "lit" : "MISSING (no " + _d.candle?.anchor + ")")}, " +
                      $"far halo {(beacon != null ? "on the lantern (swaying)" : "none")}, trail-head light {(pointer != null ? "lit" : "none")}, " +
                      $"X01 {(fo != null ? $"at {_d.x01OutsideRadius:0.#} m from the hut" : "NOT linked (no ForestOpening)")}" + (missing > 0 ? $", {missing} prefabs MISSING" : "") + ".");
        }

        // ------------------------------------------------------------------ the turn

        /// <summary>The hut and its sign turned about the hut's pivot by the data's yaw (QUESTIONS_HUT 10: the open door to the
        /// trail), from their package places in Forest_Layout (a rerun gives the same pose); the items inside follow at run
        /// time through <see cref="ForestRelocation"/>'s hut group. The Hochsitz stays where it is.</summary>
        static string TurnHut(Transform root, Transform structures)
        {
            Turn tn = _d.turn;
            if (tn == null || tn.objects == null || tn.objects.Length == 0 || structures == null) return "hut not turned (no turn in the data)";
            if (!root.TryGetComponent(out ForestRelocation rel)) return "hut NOT turned: no ForestRelocation on the Forest root (the checkpoint step adds it)";
            var layout = JsonUtility.FromJson<Layout>(File.ReadAllText(LayoutPath));
            Placed hut = layout.prefabs.FirstOrDefault(p => p.m == tn.objects[0]);
            if (hut == null) return $"hut NOT turned: {tn.objects[0]} not in {LayoutPath}";
            Vector3 pivot = V(hut.p);
            Quaternion r = Quaternion.Euler(0f, tn.yaw, 0f);
            int n = 0;
            foreach (string name in tn.objects)
            {
                Placed pl = layout.prefabs.FirstOrDefault(p => p.m == name);
                Transform t = structures.Find(name);
                if (pl == null || t == null) { Debug.LogWarning($"[Snow] Forest hut dress: {name} not found to turn."); continue; }
                t.SetPositionAndRotation(pivot + r * (V(pl.p) - pivot), r * new Quaternion(pl.q[0], pl.q[1], pl.q[2], pl.q[3]));
                if (PrefabUtility.IsPartOfPrefabInstance(t)) PrefabUtility.RecordPrefabInstancePropertyModifications(t);
                EditorUtility.SetDirty(t);
                n++;
            }
            rel.hutPivot = pivot;
            rel.hutYaw = tn.yaw;
            rel.hutRadius = tn.radius;
            EditorUtility.SetDirty(rel);
            return $"hut and sign turned {tn.yaw:0.#}° about {pivot.x:0.#}, {pivot.z:0.#} ({n} objects, items within {tn.radius:0.#} m follow)";
        }

        static Vector3 V(float[] a) => new Vector3(a[0], a[1], a[2]);

        /// <summary>The data's ground rectangles moved from the hut's frame onto the turned hut and applied (heights only go
        /// down: ForestGroundEdit "cut"); before the paint and the props, so the trail, the prints and the props sit on the
        /// new ground.</summary>
        static string Ground(Transform root, Transform structures)
        {
            Transform hut = structures != null ? structures.Find("TowerHut") : null;
            if (_d.ground == null || _d.ground.Length == 0 || hut == null) return "ground as it is";
            var rects = _d.ground.Select(g => new ForestGroundEdit.Rect
            {
                name = g.name, x = hut.position.x, z = hut.position.z, yaw = hut.eulerAngles.y, y = hut.position.y + g.y, fade = g.fade,
                rect = g.rect, mode = g.mode, heights = g.heights, hole = g.hole, clearTrees = g.clearTrees, clearGrass = g.clearGrass,
            }).ToArray();
            (int heights, int _) = ForestGroundEdit.Apply(root, rects, Path.ChangeExtension(DataPath, null) + "_ground.bytes", "forest_hut");
            int grass = ForestGroundEdit.ClearGrass(root, rects.Where(r => r.clearGrass).ToArray());
            return $"ground in front of the hut: {heights} height samples lowered, {grass} grass cells cleared";
        }

        // ------------------------------------------------------------------ trail geometry

        static float Length()
        {
            float len = 0f;
            float[] p = _d.trail.p;
            for (int i = 0; i + 3 < p.Length; i += 2) len += new Vector2(p[i + 2] - p[i], p[i + 3] - p[i + 1]).magnitude;
            return len;
        }

        static float ToTrail(Vector2 q)
        {
            float best = float.MaxValue;
            float[] p = _d.trail.p;
            for (int i = 0; i + 3 < p.Length; i += 2)
            {
                Vector2 a = new Vector2(p[i], p[i + 1]), b = new Vector2(p[i + 2], p[i + 3]);
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                best = Mathf.Min(best, (a + ab * t - q).magnitude);
            }
            return best;
        }

        /// <summary>Everything this step may change on the ground: the trail and a margin for its ragged edge.</summary>
        static bool InReach(Vector2 q) => ToTrail(q) < _d.trail.w * 0.5f + 1.5f;

        static bool Overlaps(Terrain t)
        {
            Vector3 o = t.transform.position, s = t.terrainData.size;
            float[] p = _d.trail.p;
            for (int i = 0; i + 1 < p.Length; i += 2)
                if (p[i] >= o.x - 2f && p[i] <= o.x + s.x + 2f && p[i + 1] >= o.z - 2f && p[i + 1] <= o.z + s.z + 2f) return true;
            return false;
        }

        static float Edge(float depth, float soft) => Mathf.Clamp01(depth / soft + 0.5f);

        /// <summary>Share of the trail at a point and of its worn middle; the edge wanders by a few decimetres.</summary>
        static (float soil, float track) Sample(Vector2 q)
        {
            float n = Mathf.PerlinNoise(q.x / 1.3f + 4.1f, q.y / 1.3f + 7.7f) * 0.65f + Mathf.PerlinNoise(q.x / 0.5f + 2.3f, q.y / 0.5f + 5.9f) * 0.35f - 0.5f;
            float d = ToTrail(q), w = _d.trail.w;
            float share = Edge(w * 0.5f - d + n * 0.5f, 0.35f);
            float track = share * Mathf.Clamp01(1f - d / (w * 0.3f)) * 0.7f;
            return (share - track, track);
        }

        // ------------------------------------------------------------------ ground

        static string PaintPath => Path.ChangeExtension(DataPath, null) + "_paint.bytes";

        /// <summary>The bake's paint of the reach before the first run, per tile (a rerun paints from it again).</summary>
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
                Debug.LogWarning($"[Snow] Forest hut dress: the stored paint of {td.name} has another extent (the trail changed); measuring again from the current paint.");
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

        static int Layer(TerrainData td, string name) => Array.FindIndex(td.terrainLayers, l => l != null && l.name == name);

        static int Paint(Terrain t)
        {
            TerrainData td = t.terrainData;
            int soil = Layer(td, "TL_TrailSoil"), track = Layer(td, "TL_TrailTrack");
            if (soil < 0 || track < 0)
            {
                Debug.LogWarning($"[Snow] Forest hut dress: {td.name} lacks the bake's trail layers; no trail paint.");
                return 0;
            }
            int n = td.alphamapResolution, layers = td.alphamapLayers, count = 0;
            float tx = td.size.x / n;
            Vector3 o = t.transform.position;
            int x0 = n, z0 = n, x1 = -1, z1 = -1;
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                    if (InReach(new Vector2(o.x + (x + 0.5f) * tx, o.z + (z + 0.5f) * tx)))
                    {
                        x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x);
                        z0 = Mathf.Min(z0, z); z1 = Mathf.Max(z1, z);
                    }
            if (x1 < 0) return 0;
            var map = (float[,,])OriginalPaint(td, x0, z0, x1 - x0 + 1, z1 - z0 + 1).Clone();
            for (int z = 0; z < map.GetLength(0); z++)
                for (int x = 0; x < map.GetLength(1); x++)
                {
                    var q = new Vector2(o.x + (x0 + x + 0.5f) * tx, o.z + (z0 + z + 0.5f) * tx);
                    if (!InReach(q)) continue;
                    (float s, float k) = Sample(q);
                    if (s + k <= 0f) continue;
                    // the trail's layers only grow (max), the others keep their ratios in what is left (the road's gravel at
                    // the trail head stays under it)
                    float ns = Mathf.Max(map[z, x, soil], s), nk = Mathf.Min(Mathf.Max(map[z, x, track], k), 1f - ns);
                    float other = 0f;
                    for (int l = 0; l < layers; l++) if (l != soil && l != track) other += map[z, x, l];
                    float rest = 1f - ns - nk;
                    if (other <= 1e-5f) ns += rest;
                    for (int l = 0; l < layers; l++)
                        if (l != soil && l != track) map[z, x, l] = other > 1e-5f ? map[z, x, l] / other * rest : 0f;
                    map[z, x, soil] = ns;
                    map[z, x, track] = nk;
                    count++;
                }
            td.SetAlphamaps(x0, z0, map);
            return count;
        }

        /// <summary>No grass, ferns or twigs on the trodden middle; the edges keep theirs.</summary>
        static int Details(Terrain t)
        {
            TerrainData td = t.terrainData;
            int protos = td.detailPrototypes.Length, n = td.detailResolution, count = 0;
            float cell = td.size.x / n;
            Vector3 o = t.transform.position;
            var maps = new int[protos][,];
            for (int l = 0; l < protos; l++) maps[l] = td.GetDetailLayer(0, 0, n, n, l);
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    var q = new Vector2(o.x + (x + 0.5f) * cell, o.z + (z + 0.5f) * cell);
                    if (!InReach(q)) continue;
                    (float s, float k) = Sample(q);
                    if (s + k < 0.45f) continue;
                    for (int l = 0; l < protos; l++) maps[l][z, x] = 0;
                    count++;
                }
            for (int l = 0; l < protos; l++) td.SetDetailLayer(0, 0, l, maps[l]);
            return count;
        }

        /// <summary>A trunk standing on the trail goes (the route avoids them; this only catches a later change of it).</summary>
        static int Trees(Terrain t)
        {
            TerrainData td = t.terrainData;
            TreePrototype[] protos = td.treePrototypes;
            Vector3 o = t.transform.position, s = td.size;
            var keep = new List<TreeInstance>();
            int removed = 0;
            foreach (TreeInstance ti in td.treeInstances)
            {
                string nm = protos[ti.prototypeIndex].prefab != null ? protos[ti.prototypeIndex].prefab.name : "";
                bool fence = nm.StartsWith("OldFence", StringComparison.Ordinal) || nm.StartsWith("EuroPost", StringComparison.Ordinal) || nm.StartsWith("FenceChain", StringComparison.Ordinal);
                if (!fence && ToTrail(new Vector2(o.x + ti.position.x * s.x, o.z + ti.position.z * s.z)) < _d.clearRadius) { removed++; continue; }
                keep.Add(ti);
            }
            if (removed > 0) td.SetTreeInstances(keep.ToArray(), false);
            return removed;
        }

        // ------------------------------------------------------------------ objects

        static float Height(Vector3 w)
        {
            foreach (Terrain t in Terrain.activeTerrains)
            {
                Vector3 o = t.transform.position, s = t.terrainData.size;
                if (w.x >= o.x && w.z >= o.z && w.x <= o.x + s.x && w.z <= o.z + s.z) return o.y + t.SampleHeight(w);
            }
            return w.y;
        }

        static GameObject Place(GameObject asset, Transform parent, float x, float z, float yaw, Transform reference, float y = 0f) =>
            PlaceAt(asset, parent, new Vector3(x, Height(new Vector3(x, y, z)), z), Quaternion.Euler(0f, yaw, 0f), reference);

        // ------------------------------------------------------------------ props with game doors

        /// <summary>LocationProfile.preStep (after the KPP kit, before the Forest integration gathers the openable parts): the
        /// data's early props (the outhouse with its door) under <see cref="EarlyRootName"/>, rebuilt each time; at the data's
        /// height when no terrain stands there yet (<see cref="Run"/> puts them on the ground later). Their IX parts get their
        /// NetworkDoor from the integration like the map's own doors (rules in LocationProfiles.Forest).</summary>
        public static bool PlaceEarly(Scene scene, Transform root)
        {
            if (!File.Exists(DataPath)) return true;
            _d = JsonUtility.FromJson<Data>(File.ReadAllText(DataPath));
            Transform old = root.Find(EarlyRootName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            Prop[] early = (_d.props ?? new Prop[0]).Where(p => p.early).ToArray();
            if (early.Length == 0) return true;
            var parent = new GameObject(EarlyRootName).transform;
            parent.SetParent(root, false);
            Transform reference = root.Find("Structures/Sign_Forsthuette");
            int placed = 0;
            foreach (Prop p in early)
            {
                GameObject go = Place(AssetDatabase.LoadAssetAtPath<GameObject>(p.prefab), parent, p.x, p.z, p.yaw, reference, p.y);
                if (go == null)
                {
                    Debug.LogError($"[Snow] Forest hut yard: {p.prefab} not found (Tools / Snow / Forest / Build Hut Yard Props).");
                    return false;
                }
                // the doors move: no static flags from the reference on them (static batching would freeze the mesh)
                foreach (SkyIsland.SkyIslandInteractable ix in go.GetComponentsInChildren<SkyIsland.SkyIslandInteractable>(true))
                    foreach (Transform t in ix.GetComponentsInChildren<Transform>(true))
                        GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
                placed++;
            }
            Debug.Log($"[Snow] Forest hut yard: {placed} props with game doors placed before the parts are gathered.");
            return true;
        }

        /// <summary>The early props onto the terrain (the integration placed them before the tiles stood); in a scene built
        /// before they existed they are placed here, static until the next Forest integration gives their doors a NetworkDoor.</summary>
        static string EarlyHeights(Transform root)
        {
            Transform early = root.Find(EarlyRootName);
            if (early == null)
            {
                if (!(_d.props ?? new Prop[0]).Any(p => p.early)) return "no early props";
                PlaceEarly(root.gameObject.scene, root);
                _d = JsonUtility.FromJson<Data>(File.ReadAllText(DataPath));
                early = root.Find(EarlyRootName);
                if (early == null) return "early props MISSING";
                foreach (Transform t in early) t.position = new Vector3(t.position.x, Height(t.position), t.position.z);
                return $"{early.childCount} early props placed now (doors open after the next Forest integration)";
            }
            foreach (Transform t in early) t.position = new Vector3(t.position.x, Height(t.position), t.position.z);
            return $"{early.childCount} early props on the ground";
        }

        static GameObject PlaceAt(GameObject asset, Transform parent, Vector3 position, Quaternion rotation, Transform reference)
        {
            if (asset == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            go.transform.SetPositionAndRotation(position, rotation);
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

        /// <summary>One line of muddy boot prints along the data's polyline: the X11 prints' decal cell (a pair of soles per
        /// quad, walking towards local -Z), every <c>step</c> metres, lying on the ground's slope 2 cm above it.</summary>
        static int PlacePrints(Transform dress)
        {
            Prints pr = _d.prints;
            if (pr?.p == null || pr.p.Length < 4 || pr.uv == null || pr.uv.Length < 4 || pr.size <= 0f || pr.step <= 0f) return 0;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(pr.material);
            if (mat == null) { Debug.LogWarning($"[Snow] Forest hut dress: print material {pr.material} not found."); return 0; }
            float h = pr.size * 0.5f;
            var v = new[] { new Vector3(-h, 0f, -h), new Vector3(-h, 0f, h), new Vector3(h, 0f, h), new Vector3(h, 0f, -h) };
            var uv = new Vector2[4];
            for (int i = 0; i < 4; i++)
                uv[i] = new Vector2(pr.uv[0] + (h - v[i].x) / pr.size * (pr.uv[2] - pr.uv[0]), pr.uv[1] + (h - v[i].z) / pr.size * (pr.uv[3] - pr.uv[1]));
            var mesh = new Mesh { name = "HutPrints", vertices = v, uv = uv, triangles = new[] { 0, 1, 2, 0, 2, 3 } };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.RecalculateBounds();
            var parent = new GameObject("BootPrints").transform;
            parent.SetParent(dress, false);
            int count = 0;
            float carry = pr.step * 0.5f;
            for (int i = 0; i + 3 < pr.p.Length; i += 2)
            {
                Vector2 a = new Vector2(pr.p[i], pr.p[i + 1]), b = new Vector2(pr.p[i + 2], pr.p[i + 3]);
                float len = (b - a).magnitude;
                Vector2 dir = (b - a) / Mathf.Max(len, 1e-4f);
                float s = carry;
                for (; s < len; s += pr.step)
                {
                    Vector2 q = a + dir * s;
                    var w = new Vector3(q.x, 0f, q.y);
                    Vector3 n = Normal(w);
                    Vector3 fwd = Vector3.ProjectOnPlane(new Vector3(dir.x, 0f, dir.y), n).normalized;
                    var go = new GameObject($"Print_{count + 1}");
                    go.transform.SetParent(parent, false);
                    go.transform.SetPositionAndRotation(new Vector3(q.x, Height(w) + 0.02f, q.y), Quaternion.LookRotation(-fwd, n));
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterial = mat;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.renderingLayerMask = 2u;                  // the map's lights
                    count++;
                }
                carry = s - len;                                // the step goes on round the corner
            }
            return count;
        }

        static Vector3 Normal(Vector3 w)
        {
            foreach (Terrain t in Terrain.activeTerrains)
            {
                Vector3 o = t.transform.position, s = t.terrainData.size;
                if (w.x >= o.x && w.z >= o.z && w.x <= o.x + s.x && w.z <= o.z + s.z)
                    return t.terrainData.GetInterpolatedNormal((w.x - o.x) / s.x, (w.z - o.z) / s.z);
            }
            return Vector3.up;
        }

        /// <summary>The candle's flame: a warm point light and a small camera-facing halo (the kit's glow quad on the core's
        /// LightGlow) on the candle of Hut_Props, guttering now and then; X01 puts it out (ForestOpening.x01Lamps).</summary>
        static ForestStartLamp MakeCandle(Transform dress, Transform hut)
        {
            Candle c = _d.candle;
            var go = new GameObject("HutCandle");
            go.transform.SetParent(dress, false);
            go.transform.position = hut.TransformPoint(new Vector3(c.local[0], c.local[1], c.local[2]));
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            var col = new Color(c.color[0], c.color[1], c.color[2]);
            float mx = Mathf.Max(col.maxColorComponent, 1e-4f);
            l.color = new Color(col.r / mx, col.g / mx, col.b / mx).gamma;
            l.intensity = c.intensity;
            l.range = c.range;
            l.shadows = LightShadows.None;
            l.lightmapBakeType = LightmapBakeType.Realtime;
            Light from = AssetDatabase.LoadAssetAtPath<GameObject>(LightLayersFrom)?.GetComponentInChildren<Light>(true);
            if (from != null)
            {
                l.renderingLayerMask = from.renderingLayerMask;
                if (from.TryGetComponent(out UniversalAdditionalLightData src))
                {
                    if (!go.TryGetComponent(out UniversalAdditionalLightData dst)) dst = go.AddComponent<UniversalAdditionalLightData>();
                    dst.renderingLayers = src.renderingLayers;
                }
            }
            var glowGo = new GameObject("Glow_Candle");
            glowGo.transform.SetParent(go.transform, false);
            glowGo.transform.localScale = Vector3.one * c.glow;
            glowGo.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(GlowQuad);
            var r = glowGo.AddComponent<MeshRenderer>();
            r.sharedMaterial = GlowMat(col);
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var lamp = go.AddComponent<ForestStartLamp>();
            lamp.lights = new[] { l };
            lamp.glows = new Renderer[] { r };
            lamp.intensity = c.intensity;
            lamp.lifeMinutes = 600f;                            // a candle: lives until X01
            lamp.dipEvery = 2.5f;
            return lamp;
        }

        const string BeaconMaterial = "Assets/Locations/Forest/Start/Materials/M_Hut_BeaconGlow.mat";

        /// <summary>The hut's far halo (QUESTIONS_HUT_2 5, 10): a warm camera-facing glow on the lantern's light that the fog
        /// dims less than the light itself (LightGlow's fog floor), so the hut reads as a dot from the start post and the
        /// road and fades out up close; the light and the halo sway a little on the hook. Out at X01 with the lantern.
        /// Rebuilt each run (the old halo under the light is removed first).</summary>
        static ForestStartLamp MakeBeacon(Transform hut)
        {
            Beacon b = _d.beacon;
            Transform light = b != null ? hut.Find(b.light) : null;
            if (light == null) return null;
            foreach (Transform old in light.Cast<Transform>().Where(t => t.name == "Beacon_Hut").ToArray()) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var go = new GameObject("Beacon_Hut");
            go.transform.SetParent(light, false);
            go.transform.localScale = Vector3.one * b.size;
            go.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(GlowQuad);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = GlowMat(BeaconMaterial, new Color(b.color[0], b.color[1], b.color[2]), b.intensity, b.fogFloor, b.nearFade);
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var lamp = go.AddComponent<ForestStartLamp>();
            lamp.lights = new Light[0];
            lamp.glows = new Renderer[] { r };
            lamp.lifeMinutes = 600f;                            // lives until X01
            lamp.dipEvery = 1e4f;                               // the lantern's own flicker does the rest
            if (b.sway > 0f)
            {
                if (!light.TryGetComponent(out ForestLanternSway sw)) sw = light.gameObject.AddComponent<ForestLanternSway>();
                sw.amplitude = b.sway;
                sw.period = b.swayPeriod > 0f ? b.swayPeriod : sw.period;
                EditorUtility.SetDirty(sw);
            }
            return lamp;
        }

        /// <summary>A small warm light on the sign at the trail head (user 25.10: the hut cannot be seen from the road through
        /// the firs, so a light at the road draws the eye to the trail and the hut's lantern takes over from there). With the
        /// yard pack's pointer (HutSign) among the props its lantern is the light: only the far halo is added on its LIGHT_Lant;
        /// without it a point light and a halo at the data's place. Out at X01.</summary>
        static ForestStartLamp MakePointer(Transform dress)
        {
            Pointer p = _d.pointer;
            if (p == null || p.color == null) return null;
            Transform lant = dress.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "LIGHT_Lant");
            if (lant != null) return MakePointerHalo(lant, p);
            var go = new GameObject("TrailLight");
            go.transform.SetParent(dress, false);
            var w = new Vector3(p.x, 0f, p.z);
            go.transform.position = new Vector3(p.x, Height(w) + p.height, p.z);
            var col = new Color(p.color[0], p.color[1], p.color[2]);
            float mx = Mathf.Max(col.maxColorComponent, 1e-4f);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(col.r / mx, col.g / mx, col.b / mx).gamma;
            l.intensity = p.intensity;
            l.range = p.range;
            l.shadows = LightShadows.None;
            l.lightmapBakeType = LightmapBakeType.Realtime;
            Light from = AssetDatabase.LoadAssetAtPath<GameObject>(LightLayersFrom)?.GetComponentInChildren<Light>(true);
            if (from != null)
            {
                l.renderingLayerMask = from.renderingLayerMask;
                if (from.TryGetComponent(out UniversalAdditionalLightData src))
                {
                    if (!go.TryGetComponent(out UniversalAdditionalLightData dst)) dst = go.AddComponent<UniversalAdditionalLightData>();
                    dst.renderingLayers = src.renderingLayers;
                }
            }
            var glowGo = new GameObject("Glow_Trail");
            glowGo.transform.SetParent(go.transform, false);
            glowGo.transform.localScale = Vector3.one * p.glow;
            glowGo.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(GlowQuad);
            var r = glowGo.AddComponent<MeshRenderer>();
            Beacon b = _d.beacon;
            r.sharedMaterial = b != null ? GlowMat(BeaconMaterial, new Color(b.color[0], b.color[1], b.color[2]), b.intensity, b.fogFloor, b.nearFade) : GlowMat(col);
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var lamp = go.AddComponent<ForestStartLamp>();
            lamp.lights = new[] { l };
            lamp.glows = new Renderer[] { r };
            lamp.intensity = p.intensity;
            lamp.lifeMinutes = 600f;
            lamp.dipEvery = 6f;
            go.AddComponent<ForestLanternSway>().amplitude = 0.03f;
            return lamp;
        }

        /// <summary>The far halo on the pointer lantern's light (the beacon's glow: the fog dims it less, so the trail head
        /// reads from the road); its own glow-only lamp, out at X01 with the lantern.</summary>
        static ForestStartLamp MakePointerHalo(Transform light, Pointer p)
        {
            var go = new GameObject("Beacon_Trail");
            go.transform.SetParent(light, false);
            go.transform.localScale = Vector3.one * p.glow;
            go.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(GlowQuad);
            var r = go.AddComponent<MeshRenderer>();
            Beacon b = _d.beacon;
            r.sharedMaterial = b != null ? GlowMat(BeaconMaterial, new Color(b.color[0], b.color[1], b.color[2]), b.intensity, b.fogFloor, b.nearFade)
                                         : GlowMat(new Color(p.color[0], p.color[1], p.color[2]));
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var lamp = go.AddComponent<ForestStartLamp>();
            lamp.lights = new Light[0];
            lamp.glows = new Renderer[] { r };
            lamp.lifeMinutes = 600f;
            lamp.dipEvery = 1e4f;                               // the lantern's own dips do the rest
            return lamp;
        }

        static Material GlowMat(Color linear) => GlowMat(GlowMaterial, linear, 1.2f, 0f, 0.3f);

        internal static Material GlowMat(string path, Color linear, float intensity, float fogFloor, float nearFade)
        {
            Shader sh = Shader.Find("SkyIsland/LightGlow");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(sh) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(m, path);
            }
            float mx = Mathf.Max(linear.maxColorComponent, 1e-4f);
            m.SetColor("_Color", new Color(linear.r / mx, linear.g / mx, linear.b / mx).gamma);
            m.SetFloat("_Intensity", intensity);
            m.SetFloat("_Softness", 2.6f);
            if (m.HasProperty("_FogFloor")) m.SetFloat("_FogFloor", fogFloor);
            m.SetFloat("_NearFade", nearFade);
            m.renderQueue = (int)RenderQueue.Transparent + 10;
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
