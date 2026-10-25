using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Snow.Levels.Forest;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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
        public const string RootName = "ForestHutDress";
        const string GlowMaterial = "Assets/Locations/Forest/Start/Materials/M_Hut_CandleGlow.mat";
        const string GlowQuad = "Assets/Kit/Materials/Kit_GlowQuad.asset";
        const string LightLayersFrom = "Assets/Locations/Forest/Prefabs/LightPole.prefab";

        [Serializable] public class Trail { public float w; public float[] p; }
        [Serializable] public class Prop { public string prefab; public float x, z, yaw; }
        [Serializable] public class Candle { public string anchor; public float[] local, color; public float intensity, range, glow; }
        [Serializable]
        public class Data
        {
            public Trail trail;
            public float clearRadius, x01OutsideRadius;
            public Prop[] props;
            public Candle candle;
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
            foreach (Prop p in _d.props ?? new Prop[0])
            {
                if (Place(AssetDatabase.LoadAssetAtPath<GameObject>(p.prefab), dress, p.x, p.z, p.yaw, reference) == null) { missing++; continue; }
                props++;
            }
            Transform hut = _d.candle != null ? root.Find(_d.candle.anchor) : null;
            ForestStartLamp candle = hut != null ? MakeCandle(dress, hut) : null;
            ForestOpening fo = root.GetComponentInChildren<ForestOpening>(true);
            if (fo != null)
            {
                fo.x01Lamps = candle != null ? new[] { candle } : new ForestStartLamp[0];
                fo.x01Hut = hut;
                fo.x01OutsideRadius = _d.x01OutsideRadius;
                EditorUtility.SetDirty(fo);
            }

            Debug.Log($"[Snow] Forest hut dress ({DataPath}): trail of {Length():0} m, {painted} ground texels and {cells} grass cells rewritten, " +
                      $"{removed} Terrain trees on the trail removed, {props} props, candle {(candle != null ? "lit" : "MISSING (no " + _d.candle?.anchor + ")")}, " +
                      $"X01 {(fo != null ? $"at {_d.x01OutsideRadius:0.#} m from the hut" : "NOT linked (no ForestOpening)")}" + (missing > 0 ? $", {missing} prefabs MISSING" : "") + ".");
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

        static GameObject Place(GameObject asset, Transform parent, float x, float z, float yaw, Transform reference)
        {
            if (asset == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            var w = new Vector3(x, 0f, z);
            go.transform.SetPositionAndRotation(new Vector3(x, Height(w), z), Quaternion.Euler(0f, yaw, 0f));
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

        static Material GlowMat(Color linear)
        {
            Shader sh = Shader.Find("SkyIsland/LightGlow");
            var m = AssetDatabase.LoadAssetAtPath<Material>(GlowMaterial);
            if (m == null)
            {
                m = new Material(sh) { name = Path.GetFileNameWithoutExtension(GlowMaterial) };
                AssetDatabase.CreateAsset(m, GlowMaterial);
            }
            float mx = Mathf.Max(linear.maxColorComponent, 1e-4f);
            m.SetColor("_Color", new Color(linear.r / mx, linear.g / mx, linear.b / mx).gamma);
            m.SetFloat("_Intensity", 1.2f);
            m.SetFloat("_Softness", 2.6f);
            if (m.HasProperty("_FogFloor")) m.SetFloat("_FogFloor", 0f);
            m.SetFloat("_NearFade", 0.3f);
            m.renderQueue = (int)RenderQueue.Transparent + 10;
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
