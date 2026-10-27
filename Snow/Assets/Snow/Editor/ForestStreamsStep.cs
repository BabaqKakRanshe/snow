using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Snow.EditorTools
{
    /// <summary>
    /// Forest integration: the creeks and ponds (user 27.10: "3 rivers, none has water, all planted with trees" -> creeks
    /// along the valleys). Data <see cref="DataPath"/> from tools/blender_context/edits/forest_streams.py, which also carved
    /// the beds: per creek a line of (x, z, water y) and a width, per pond its level and the 2.3 m cells it covers, and
    /// lines whose bed is only cleared (the north-west river keeps the package's water mesh). Each run rebuilds the water
    /// (root <see cref="RootName"/>; the Forest_Water material, so its flow scroll runs too), takes the Terrain trees and
    /// grass out of the beds and ponds and paints the beds stony (rock + gravel). Idempotent.
    /// </summary>
    public static class ForestStreamsStep
    {
        public const string DataPath = "Assets/Snow/Levels/Forest/ForestStreams.json";
        public const string RootName = "ForestStreams";
        const float TreeMargin = 2.5f, GrassMargin = 1f, BedPaint = 1f;

        [Serializable] class Line { public string name; public float width; public float[] points; }
        [Serializable] class Pond { public string name; public float level, cell; public float[] cells; }
        [Serializable] class Data { public Line[] creeks, clear; public Pond[] ponds; }

        [MenuItem("Tools/Snow/Forest/Streams")]
        static void Menu()
        {
            Transform root = GameObject.Find("Forest")?.transform;
            if (root == null) { Debug.LogError("[Snow] Forest streams: no Forest root in the open scene."); return; }
            Run(root);
        }

        public static void Run(Transform root)
        {
            if (!File.Exists(DataPath)) return;
            Data d = JsonUtility.FromJson<Data>(File.ReadAllText(DataPath));
            Transform old = root.Find(RootName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var group = new GameObject(RootName).transform;
            group.SetParent(root, false);
            Renderer lake = root.Find("World/Forest_Water")?.GetComponentInChildren<Renderer>();
            Material water = lake != null ? lake.sharedMaterial : null;

            int creeks = 0, ponds = 0;
            foreach (Line c in d.creeks ?? new Line[0])
                if (c?.points != null && c.points.Length >= 6) { Water(group, c.name, Ribbon(c), water, lake); creeks++; }
            foreach (Pond p in d.ponds ?? new Pond[0])
                if (p?.cells != null && p.cells.Length >= 2) { Water(group, "Pond_" + p.name, Cells(p), water, lake); ponds++; }

            // beds: what is near a line (2D, its points x, z every 3rd or 2nd value)
            var lines = new List<(Vector2[] pts, float half)>();
            foreach (Line c in d.creeks ?? new Line[0]) if (c?.points != null) lines.Add((Pts(c.points, 3), c.width * 0.5f));
            foreach (Line c in d.clear ?? new Line[0]) if (c?.points != null) lines.Add((Pts(c.points, 2), c.width * 0.5f));
            var pondCells = new List<(Vector2 p, float level, float half)>();
            foreach (Pond p in d.ponds ?? new Pond[0])
                if (p?.cells != null)
                    for (int i = 0; i + 1 < p.cells.Length; i += 2) pondCells.Add((new Vector2(p.cells[i], p.cells[i + 1]), p.level, p.cell * 0.75f));
            var pondGrid = pondCells.GroupBy(c => Key(c.p)).ToDictionary(gr => gr.Key, gr => gr.First());

            bool InPond(float x, float z)
            {
                var k = Key(new Vector2(x, z));
                for (int a = -1; a <= 1; a++)
                    for (int b = -1; b <= 1; b++)
                        if (pondGrid.TryGetValue((k.Item1 + a, k.Item2 + b), out var c) && (c.p - new Vector2(x, z)).magnitude <= c.half * 1.5f) return true;
                return false;
            }
            Rect box = Bounds(lines, pondCells, 6f);
            // metres outside the nearest bed (< 0 inside), on a 1 m grid: each segment stamps its neighbourhood
            int gw = Mathf.CeilToInt(box.width) + 1, gh = Mathf.CeilToInt(box.height) + 1;
            var near = new float[Math.Max(1, gw * gh)];
            for (int i = 0; i < near.Length; i++) near[i] = float.MaxValue;
            foreach (var l in lines)
                for (int i = 0; i + 1 < l.pts.Length; i++)
                {
                    Vector2 a = l.pts[i], ab = l.pts[i + 1] - a;
                    float reach = l.half + TreeMargin + BedPaint + 1f;
                    int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, a.x + ab.x) - reach - box.xMin)), x1 = Mathf.Min(gw - 1, Mathf.CeilToInt(Mathf.Max(a.x, a.x + ab.x) + reach - box.xMin));
                    int z0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, a.y + ab.y) - reach - box.yMin)), z1 = Mathf.Min(gh - 1, Mathf.CeilToInt(Mathf.Max(a.y, a.y + ab.y) + reach - box.yMin));
                    for (int gz = z0; gz <= z1; gz++)
                        for (int gx = x0; gx <= x1; gx++)
                        {
                            var p = new Vector2(box.xMin + gx, box.yMin + gz);
                            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                            float dd = Vector2.Distance(p, a + ab * t) - l.half;
                            int k = gz * gw + gx;
                            if (dd < near[k]) near[k] = dd;
                        }
                }
            float Bed(float x, float z)
            {
                int gx = Mathf.RoundToInt(x - box.xMin), gz = Mathf.RoundToInt(z - box.yMin);
                return gx < 0 || gz < 0 || gx >= gw || gz >= gh ? float.MaxValue : near[gz * gw + gx];
            }
            int trees = 0, cells = 0, texels = 0;
            foreach (Terrain t in Terrain.activeTerrains.Where(t => t != null && t.terrainData != null && t.transform.IsChildOf(root)))
            {
                TerrainData td = t.terrainData;
                Vector3 o = t.transform.position, s = td.size;
                if (o.x > box.xMax || o.z > box.yMax || o.x + s.x < box.xMin || o.z + s.z < box.yMin) continue;
                var keep = new List<TreeInstance>(td.treeInstanceCount);
                foreach (TreeInstance ti in td.treeInstances)
                {
                    Vector3 w = o + Vector3.Scale(ti.position, s);
                    if (box.Contains(new Vector2(w.x, w.z)) && (Bed(w.x, w.z) < TreeMargin || InPond(w.x, w.z))) { trees++; continue; }
                    keep.Add(ti);
                }
                td.SetTreeInstances(keep.ToArray(), false);

                int dn = td.detailResolution;
                for (int l = 0; l < td.detailPrototypes.Length; l++)
                {
                    int[,] m = td.GetDetailLayer(0, 0, dn, dn, l);
                    for (int z = 0; z < dn; z++)
                        for (int x = 0; x < dn; x++)
                        {
                            if (m[z, x] == 0) continue;
                            float wx = o.x + (x + 0.5f) * s.x / dn, wz = o.z + (z + 0.5f) * s.z / dn;
                            if (!box.Contains(new Vector2(wx, wz)) || !(Bed(wx, wz) < GrassMargin || InPond(wx, wz))) continue;
                            m[z, x] = 0;
                            cells++;
                        }
                    td.SetDetailLayer(0, 0, l, m);
                }

                int rock = Array.FindIndex(td.terrainLayers, x => x != null && x.name == "TL_Rock");
                int gravel = Array.FindIndex(td.terrainLayers, x => x != null && x.name == "TL_RoadGravel");
                if (rock >= 0 && gravel >= 0)
                {
                    int n = td.alphamapResolution, L = td.alphamapLayers;
                    float[,,] map = td.GetAlphamaps(0, 0, n, n);
                    for (int z = 0; z < n; z++)
                        for (int x = 0; x < n; x++)
                        {
                            float wx = o.x + (x + 0.5f) * s.x / n, wz = o.z + (z + 0.5f) * s.z / n;
                            if (!box.Contains(new Vector2(wx, wz))) continue;
                            float bed = Bed(wx, wz);
                            if (bed > BedPaint) continue;
                            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((BedPaint - bed) / (BedPaint + 1f)));
                            float r = Mathf.PerlinNoise(wx * 0.35f, wz * 0.35f);       // rock and pebbles in patches
                            for (int l = 0; l < L; l++)
                                map[z, x, l] = Mathf.Lerp(map[z, x, l], l == rock ? r : l == gravel ? 1f - r : 0f, k);
                            texels++;
                        }
                    td.SetAlphamaps(0, 0, map);
                }
                EditorUtility.SetDirty(td);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Snow] Forest streams ({DataPath}): {creeks} creeks, {ponds} ponds of water, {trees} Terrain trees and {cells} grass cells " +
                      $"taken out of the beds, {texels} bed texels painted.");
        }

        // ------------------------------------------------------------------ water meshes

        static Mesh Ribbon(Line c)
        {
            int n = c.points.Length / 3;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            float along = 0f, half = c.width * 0.5f;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = P3(c.points, i);
                Vector3 a = P3(c.points, Mathf.Max(0, i - 1)), b = P3(c.points, Mathf.Min(n - 1, i + 1));
                Vector3 t = new Vector3(b.x - a.x, 0f, b.z - a.z).normalized;
                Vector3 side = new Vector3(-t.z, 0f, t.x) * half;
                if (i > 0) along += new Vector2(p.x - P3(c.points, i - 1).x, p.z - P3(c.points, i - 1).z).magnitude;
                v.Add(p - side); v.Add(p + side);
                uv.Add(new Vector2(0f, along / c.width)); uv.Add(new Vector2(1f, along / c.width));
                if (i > 0) { int k = v.Count - 4; tri.AddRange(new[] { k, k + 1, k + 2, k + 1, k + 3, k + 2 }); }   // clockwise from above: faces up
            }
            var m = new Mesh { name = "Creek_" + c.name };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        static Mesh Cells(Pond p)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            var index = new Dictionary<(int, int), int>();
            float c = p.cell;
            int V(float x, float z)
            {
                var k = (Mathf.RoundToInt(x / c * 2f), Mathf.RoundToInt(z / c * 2f));
                if (index.TryGetValue(k, out int i)) return i;
                v.Add(new Vector3(x, p.level, z)); uv.Add(new Vector2(x / 4f, z / 4f));
                return index[k] = v.Count - 1;
            }
            for (int i = 0; i + 1 < p.cells.Length; i += 2)
            {
                float x = p.cells[i], z = p.cells[i + 1], h = c * 0.5f + 0.4f;                 // a little over the cell: no seams
                int a = V(x - h, z - h), b = V(x + h, z - h), d = V(x - h, z + h), e = V(x + h, z + h);
                tri.AddRange(new[] { a, d, b, b, d, e });
            }
            var m = new Mesh { name = "Pond_" + p.name, indexFormat = IndexFormat.UInt32 };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        static void Water(Transform parent, string name, Mesh mesh, Material mat, Renderer like)
        {
            string folder = "Assets/Snow/Levels/Forest/Streams";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Snow/Levels/Forest", "Streams");
            string path = $"{folder}/{mesh.name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            if (like != null)
            {
                go.layer = like.gameObject.layer;
                r.renderingLayerMask = like.renderingLayerMask;
                GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(like.gameObject));
            }
        }

        // ------------------------------------------------------------------ helpers

        static Vector3 P3(float[] a, int i) => new Vector3(a[i * 3], a[i * 3 + 2], a[i * 3 + 1]);   // data: x, z, y

        static Vector2[] Pts(float[] a, int stride)
        {
            var r = new Vector2[a.Length / stride];
            for (int i = 0; i < r.Length; i++) r[i] = new Vector2(a[i * stride], a[i * stride + 1]);
            return r;
        }

        static float ToLine(Vector2 p, Vector2[] line)
        {
            float best = float.MaxValue;
            for (int i = 0; i + 1 < line.Length; i++)
            {
                Vector2 a = line[i], ab = line[i + 1] - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }
            return best;
        }

        static (int, int) Key(Vector2 p) => (Mathf.FloorToInt(p.x / 2.5f), Mathf.FloorToInt(p.y / 2.5f));

        static Rect Bounds(List<(Vector2[] pts, float half)> lines, List<(Vector2 p, float level, float half)> cells, float margin)
        {
            float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
            foreach (var l in lines) foreach (var p in l.pts) { x0 = Mathf.Min(x0, p.x - l.half); x1 = Mathf.Max(x1, p.x + l.half); z0 = Mathf.Min(z0, p.y - l.half); z1 = Mathf.Max(z1, p.y + l.half); }
            foreach (var c in cells) { x0 = Mathf.Min(x0, c.p.x); x1 = Mathf.Max(x1, c.p.x); z0 = Mathf.Min(z0, c.p.y); z1 = Mathf.Max(z1, c.p.y); }
            return x0 > x1 ? new Rect() : Rect.MinMaxRect(x0 - margin, z0 - margin, x1 + margin, z1 + margin);
        }
    }
}
