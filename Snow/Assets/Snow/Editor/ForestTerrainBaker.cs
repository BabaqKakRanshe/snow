using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Snow.Levels.Forest;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Snow.EditorTools
{
    /// <summary>
    /// Tools / Snow / Forest / Bake Unity Terrain: the Forest ground becomes Unity Terrain tiles, 3 x 3 of 600 m (1800 x 1800 m,
    /// the base in the middle; user decisions 21.10: "расширяем", the map's scattered props as Terrain trees and details like
    /// Unreal foliage). The 1200 m square of the package is baked from the ground mesh of Models/Forest_Terrain.fbx (tiles,
    /// road and trail, yard asphalt, marker; read from the model asset, so a scene that the integration has cleaned still
    /// bakes): heights 1025² per tile, eight layers painted from the road and asphalt meshes. The five tiles around it are
    /// generated: heights continue the package's edge with noise, the floor is painted by noise and slope. Trees, bushes,
    /// rocks, stumps and snags of the layout become Terrain trees (rock-likes with box / capsule colliders on prototypes in
    /// Terrain/Prototypes); ferns, weeds, branches and pebbles become Terrain details; logs (tilted along slopes), fences and
    /// posts stay objects. Denser forest: saplings in the package square, generated forest around it, ground cover everywhere
    /// (best practice: 400–800 stems/ha near the player, layered undergrowth). Writes only into Assets/Locations/Forest/Terrain;
    /// the scene is placed by the Forest integration (ForestTerrainStep). Once the user brushes the tiles the assets are the
    /// source of truth: existing tiles are overwritten only after a question and with a copy in the archive.
    /// </summary>
    public class ForestTerrainBaker : EditorWindow
    {
        public const string Folder = "Assets/Locations/Forest/Terrain";
        public const string MetaPath = Folder + "/Forest_TerrainBake.json";
        const string ReportPath = Folder + "/Forest_TerrainBake.txt";
        const string MaterialPath = Folder + "/M_Forest_TerrainLit.mat";
        const string ModelPath = "Assets/Locations/Forest/Models/Forest_Terrain.fbx";
        const string LayoutPath = "Assets/Locations/Forest/Data/Forest_Layout.json";
        const string Tex = "Assets/Locations/Forest/Textures/";
        const string Prefabs = "Assets/Locations/Forest/Prefabs/";
        const string BackupRoot = @"F:\ClaudeGames\snow_archive\backup";
        public const string PrototypeFolder = Folder + "/Prototypes";

        // the package's square (data, the ground mesh) and the tiled world around it
        const float Half = 600f, Size = 1200f;
        public const float Tile = 600f;
        public const int TilesX = 3, TilesZ = 3;
        public const float WorldX0 = -600f, WorldZ0 = -1200f;          // the base (340; -360) sits near the middle
        const int TileRes = 1025, AlphaRes = 1024, DetailRes = 512, DetailPatch = 32;
        const int GN = TilesX * (TileRes - 1) + 1;                      // global height samples per row (3073)
        const float BelowMin = 1f, AboveMax = 1f;
        const int Super = 4;                                            // road / asphalt coverage: 4 x 4 samples per texel
        const float FlatSlope = 15f, TolFlat = 0.05f, TolOther = 0.15f; // support tolerance (m)
        const float RockFrom = 35f, RockTo = 41f;                       // rock on slopes steeper than ~38°
        const int TileTex = 64, Block = 8;                              // T_TL_* tiles: 64 px made of 8 px blocks (4 px for the thin stripes)
        const int RandomPoints = 100000;
        // anti-tiling (user 21.10 "текстура везде повторяется"): the two floor layers repeat at incommensurate sizes, so the
        // light and dark patterns never line up; with the noise-driven light / dark split the repeat no longer reads
        const float DarkTileRatio = 1.3f;
        // generated surroundings: fractal noise continuing the package's edge
        const float BlendRange = 120f;
        static readonly float[] NoiseScale = { 260f, 110f, 45f }, NoiseAmp = { 16f, 6f, 2f };
        // density (TEMPORARY, best practice 21.10): generated forest 320 stems/ha, saplings 2–8 m in the package square,
        // ground cover ferns + weeds per m² of free floor
        const float GenTreeSpacing = 5.6f, SaplingSpacing = 8f, SaplingChance = 0.75f, FernsPerM2 = 0.06f, WeedsPerM2 = 0.05f;
        const float PrefabClear = 22f, BaseClear = 48f;                 // m: no saplings / extra cover around buildings

        static readonly string[] TreeSpecies = { "Spruce_", "SpruceHC_", "Fir_", "Birch_", "Dead_", "Bush_" };
        // layout rocks that become Terrain trees: rocks, stumps, snags, logs (tilt binned into prototypes), fences and posts
        static readonly string[] RockTrees = { "Rock_", "Stump", "Snag_", "Log_", "FenceChain", "OldFence", "EuroPost" };
        static readonly string[] PlacedOnly = { "FenceChain", "OldFence", "EuroPost" };        // never scattered on new land
        const float TiltBin = 5f;                                                               // degrees
        static readonly string[] DetailSpecies = { "Fern_", "Weeds_", "Branches_", "Pebbles_" };
        public static readonly string[] ConvertedRocks = { "Rock_", "Stump", "Snag_", "Log_", "FenceChain", "OldFence", "EuroPost", "Branches_", "Pebbles_" };
        static readonly string[] DetailOrder = { "Fern_1", "Fern_2", "Weeds_1", "Weeds_2", "Weeds_3", "Branches_1", "Branches_2", "Pebbles_1", "Pebbles_2" };
        static readonly string[] Saplings = { "Spruce_13", "Fir_4", "SpruceHC_17" };

        // stripes of the road textures (u across the strip): ruts and the trail's dark middle (K3.5)
        static readonly Vector2[] RutU = { new Vector2(0.25f, 0.31f), new Vector2(0.69f, 0.75f) };
        static readonly Vector2 TrackU = new Vector2(0.375f, 0.625f);
        static readonly Vector2[] GravelCols = { new Vector2(0.04f, 0.23f), new Vector2(0.37f, 0.47f), new Vector2(0.53f, 0.63f), new Vector2(0.78f, 0.96f) };
        static readonly Vector2[] RutCols = { new Vector2(0.27f, 0.32f), new Vector2(0.675f, 0.725f) };
        static readonly Vector2[] SoilCols = { new Vector2(0.12f, 0.35f), new Vector2(0.66f, 0.87f) };
        static readonly Vector2[] TrackCols = { new Vector2(0.42f, 0.56f) };

        enum Kind { Ground, Road, Trail, Asphalt, Marker }
        const int LFloor = 0, LFloorDark = 1, LRock = 2, LGravel = 3, LRuts = 4, LSoil = 5, LTrack = 6, LAsphalt = 7, Layers = 8;
        static readonly string[] LayerNames = { "TL_ForestFloor", "TL_ForestFloorDark", "TL_Rock", "TL_RoadGravel", "TL_RoadRuts", "TL_TrailSoil", "TL_TrailTrack", "TL_Asphalt" };

        bool _heights = true, _paint = true, _vegetation = true;

        [MenuItem("Tools/Snow/Forest/Bake Unity Terrain")]
        static void Open() => GetWindow<ForestTerrainBaker>(true, "Bake Unity Terrain").minSize = new Vector2(380, 180);

        void OnGUI()
        {
            EditorGUILayout.HelpBox("Forest ground -> 3 x 3 Unity Terrain tiles of 600 m (Assets/Locations/Forest/Terrain). Source: " +
                                    "Models/Forest_Terrain.fbx and Data/Forest_Layout.json. Existing tiles are overwritten only after a question, with a copy.", MessageType.Info);
            _heights = EditorGUILayout.ToggleLeft("Heights (1025² per tile)", _heights);
            _paint = EditorGUILayout.ToggleLeft("Paint (8 layers, 1024² per tile)", _paint);
            _vegetation = EditorGUILayout.ToggleLeft("Vegetation (trees, rocks, details)", _vegetation);
            using (new EditorGUI.DisabledScope(!_heights && !_paint && !_vegetation))
                if (GUILayout.Button("Bake")) Bake(_heights, _paint, _vegetation, null);
        }

        /// <summary>Batch / script: -executeMethod Snow.EditorTools.ForestTerrainBaker.BakeFromCommandLine [-parts heights,paint,vegetation]
        /// [-overwrite] (without -overwrite existing tiles are left alone).</summary>
        public static void BakeFromCommandLine()
        {
            string[] a = Environment.GetCommandLineArgs();
            int k = Array.IndexOf(a, "-parts");
            string parts = k >= 0 && k + 1 < a.Length ? a[k + 1] : "heights,paint,vegetation";
            bool ok = Bake(parts.Contains("heights"), parts.Contains("paint"), parts.Contains("vegetation"), a.Contains("-overwrite"));
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <param name="overwrite">null: ask (interactive); true: overwrite existing tiles (copy first); false: never.</param>
        public static bool Bake(bool heights, bool paint, bool vegetation, bool? overwrite)
        {
            try { return Run(heights, paint, vegetation, overwrite); }
            finally { EditorUtility.ClearProgressBar(); }
        }

        // ------------------------------------------------------------------ data
        [Serializable] class Placed { public string m; public float[] p, q; public float s; }
        [Serializable] class Layout { public Placed[] trees, rocks, prefabs; }
        [Serializable] public class BakeMeta
        {
            public float worldX0 = WorldX0, worldZ0 = WorldZ0, tile = Tile, originY, sizeY;
            public int tilesX = TilesX, tilesZ = TilesZ;
            public string[] tiles;                                     // asset paths, row-major from (0, 0)
            public string[] treePrefabs, detailPrefabs;                 // asset paths
            public string date;
        }

        public static string TilePath(int ix, int iz) => $"{Folder}/Forest_TerrainData_{ix}_{iz}.asset";
        public static Vector3 TileOrigin(int ix, int iz, float y) => new Vector3(WorldX0 + ix * Tile, y, WorldZ0 + iz * Tile);

        sealed class Src
        {
            public Kind kind;
            public string name;
            public Vector3[] v;
            public Vector2[] uv;
            public int[] t;
            public float gradU, gradV;                                 // median |grad u|, |grad v| in plan (1/m), strips only
        }

        sealed class Ctx
        {
            public readonly List<Src> src = new List<Src>();
            public float minY = float.MaxValue, maxY = float.MinValue, bottom, sizeY, step = Tile / (TileRes - 1), texel = Tile / AlphaRes;
            public float[,] heights;                                   // [gz, gx] world metres over the whole world
            public bool[,] solid;                                      // [gz, gx] per quad: a surface (holes under the basement)
            public TriGrid grid;
            public Layout layout;
            public readonly TerrainData[,] tiles = new TerrainData[TilesX, TilesZ];
            public readonly StringBuilder report = new StringBuilder();
            public readonly CultureInfo ci = CultureInfo.InvariantCulture;
            public void R(string s) => report.AppendLine(s);
        }

        // ------------------------------------------------------------------ main
        static bool Run(bool doHeights, bool doPaint, bool doVeg, bool? overwrite)
        {
            if (!doHeights && !doPaint && !doVeg) return false;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null) { Debug.LogError($"[Snow] Forest terrain bake: {ModelPath} not found."); return false; }

            // the user's brush work is not overwritten without a question and a copy
            bool any = false;
            for (int ix = 0; ix < TilesX; ix++) for (int iz = 0; iz < TilesZ; iz++) any |= File.Exists(TilePath(ix, iz));
            if (any)
            {
                bool yes = overwrite ?? EditorUtility.DisplayDialog("Bake Unity Terrain",
                    "Forest terrain tiles already exist.\nПерезаписать ручные правки рельефа и растительности?\n\n" +
                    "A copy goes to " + BackupRoot + "\\forest_terrain_<date> first.", "Перезаписать", "Отмена");
                if (!yes) { Debug.Log("[Snow] Forest terrain bake: cancelled, the existing terrain is kept."); return false; }
                Debug.Log($"[Snow] Forest terrain bake: previous terrain copied to {Backup()}.");
            }

            var ctx = new Ctx();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            ctx.R($"Forest terrain bake {DateTime.Now:yyyy-MM-dd HH:mm}; {TilesX} x {TilesZ} tiles of {Tile} m from ({WorldX0}, {WorldZ0}); parts: " +
                  $"{(doHeights ? "heights " : "")}{(doPaint ? "paint " : "")}{(doVeg ? "vegetation" : "")}");
            Progress("Reading meshes", 0.02f);
            if (!ReadSources(model.transform, ctx)) return false;
            ctx.layout = JsonUtility.FromJson<Layout>(File.ReadAllText(LayoutPath));

            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Locations/Forest", "Terrain");
            BakeMeta meta = LoadMeta() ?? new BakeMeta();
            bool fresh = false;
            for (int ix = 0; ix < TilesX; ix++)
                for (int iz = 0; iz < TilesZ; iz++)
                {
                    var td = AssetDatabase.LoadAssetAtPath<TerrainData>(TilePath(ix, iz));
                    if (td == null)
                    {
                        td = new TerrainData { name = $"Forest_TerrainData_{ix}_{iz}", heightmapResolution = TileRes, size = new Vector3(Tile, 100f, Tile) };
                        AssetDatabase.CreateAsset(td, TilePath(ix, iz));
                        fresh = true;
                    }
                    ctx.tiles[ix, iz] = td;
                }
            if (fresh) doHeights = doPaint = doVeg = true;                 // a new tile needs everything
            meta.tiles = new string[TilesX * TilesZ];
            for (int iz = 0; iz < TilesZ; iz++) for (int ix = 0; ix < TilesX; ix++) meta.tiles[iz * TilesX + ix] = TilePath(ix, iz);

            Progress("Heights", 0.1f);
            if (doHeights)
            {
                Rasterise(ctx);
                Surroundings(ctx);
                meta.originY = ctx.bottom;
                meta.sizeY = ctx.sizeY;
                WriteHeights(ctx);
                FitSupports(ctx);
            }
            else
            {
                ctx.bottom = meta.originY;
                ctx.sizeY = meta.sizeY;
                ctx.R($"heights: kept (assets), origin Y {ctx.bottom.ToString("0.###", ctx.ci)}, size Y {ctx.sizeY.ToString("0.###", ctx.ci)}");
            }

            if (doPaint) Paint(ctx);
            else ctx.R("paint: kept (assets)");
            if (doVeg) Vegetation(ctx, meta);
            else ctx.R("vegetation: kept (assets)");

            meta.date = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            File.WriteAllText(MetaPath, JsonUtility.ToJson(meta, true));
            foreach (TerrainData td in ctx.tiles) EditorUtility.SetDirty(td);
            AssetDatabase.SaveAssets();
            int trees = 0, details = 0;
            foreach (TerrainData td in ctx.tiles) { trees += td.treeInstanceCount; details += CountDetails(td); }
            ctx.R($"done in {sw.Elapsed.TotalSeconds:0} s: {trees} trees, {details} details over {TilesX * TilesZ} tiles");
            File.WriteAllText(ReportPath, ctx.report.ToString());
            AssetDatabase.ImportAsset(MetaPath);
            AssetDatabase.ImportAsset(ReportPath);
            Debug.Log($"[Snow] Forest terrain bake: {TilesX}x{TilesZ} tiles of {Tile} m ({TileRes}² heights each), {Layers} layers, " +
                      $"{trees} trees, {details} details; report {ReportPath}.");
            return true;
        }

        static void Progress(string what, float f) => EditorUtility.DisplayProgressBar("Bake Unity Terrain", what, f);

        static string Backup()
        {
            string dir = Path.Combine(BackupRoot, "forest_terrain_" + DateTime.Now.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(dir);
            foreach (string f in Directory.GetFiles(Path.GetFullPath(Folder), "Forest_Terrain*"))
                File.Copy(f, Path.Combine(dir, Path.GetFileName(f)), true);
            return dir;
        }

        public static BakeMeta LoadMeta() => File.Exists(MetaPath) ? JsonUtility.FromJson<BakeMeta>(File.ReadAllText(MetaPath)) : null;

        // ------------------------------------------------------------------ sources
        static bool ReadSources(Transform model, Ctx ctx)
        {
            int tiles = 0;
            foreach (MeshFilter mf in model.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = mf.sharedMesh;
                if (mesh == null) continue;
                var mr = mf.GetComponent<MeshRenderer>();
                string n = mf.name;
                Matrix4x4 m = model.worldToLocalMatrix * mf.transform.localToWorldMatrix;   // the model stands at the origin
                Vector3[] v = mesh.vertices;
                for (int i = 0; i < v.Length; i++) v[i] = m.MultiplyPoint3x4(v[i]);
                Vector2[] uv = mesh.uv;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    string mat = mr != null && s < mr.sharedMaterials.Length && mr.sharedMaterials[s] != null ? mr.sharedMaterials[s].name : "";
                    Kind kind;
                    if (n.StartsWith("Terrain_", StringComparison.Ordinal)) kind = Kind.Ground;
                    else if (n == "Forest_Road") kind = mat == "M_TrailSoil" ? Kind.Trail : Kind.Road;
                    else if (n == "Forest_BasePaved") kind = Kind.Asphalt;
                    else if (n == "Forest_Marker") kind = Kind.Marker;
                    else continue;
                    Vector3[] sv = v;
                    if (kind == Kind.Asphalt)
                    {
                        sv = (Vector3[])v.Clone();                          // the yard's asphalt follows the yard (ForestYardPlan)
                        for (int i = 0; i < sv.Length; i++) sv[i] = ForestYardPlan.Map(sv[i]);
                    }
                    var src = new Src { kind = kind, name = n + (mesh.subMeshCount > 1 ? "/" + mat : ""), v = sv, uv = uv, t = mesh.GetTriangles(s) };
                    ctx.src.Add(src);
                    foreach (int i in src.t) { ctx.minY = Mathf.Min(ctx.minY, sv[i].y); ctx.maxY = Mathf.Max(ctx.maxY, sv[i].y); }
                    if (kind == Kind.Ground) tiles++;
                }
            }
            if (tiles != 64 || !ctx.src.Any(s => s.kind == Kind.Road) || !ctx.src.Any(s => s.kind == Kind.Trail) || !ctx.src.Any(s => s.kind == Kind.Asphalt))
            {
                Debug.LogError($"[Snow] Forest terrain bake: expected 64 tiles, road (M_RoadDirt), trail (M_TrailSoil) and Forest_BasePaved in {ModelPath}; " +
                               $"found {tiles} tiles, {string.Join(", ", ctx.src.Where(s => s.kind != Kind.Ground).Select(s => s.name))}.");
                return false;
            }
            foreach (Src s in ctx.src) if (s.kind == Kind.Road || s.kind == Kind.Trail) Gradients(s);
            ctx.grid = new TriGrid(ctx.src);
            ctx.R($"source: {ModelPath}: {tiles} tiles, {ctx.src.Sum(s => s.t.Length / 3)} triangles; Y {ctx.minY.ToString("0.00", ctx.ci)} .. {ctx.maxY.ToString("0.00", ctx.ci)} m");
            return true;
        }

        // ------------------------------------------------------------------ heights
        static int GX(float x) => Mathf.RoundToInt((x - WorldX0) / (Tile / (TileRes - 1)));
        static int GZ(float z) => Mathf.RoundToInt((z - WorldZ0) / (Tile / (TileRes - 1)));
        static bool InData(double x, double z) => x >= -Half - 1e-3 && x <= Half + 1e-3 && z >= -Half - 1e-3 && z <= Half + 1e-3;

        /// <summary>Top surface of the package's meshes on the global grid (inside the 1200 m square) and the quads with no
        /// mesh under their centre (the ground is cut under the main building's basement: Terrain holes).</summary>
        static void Rasterise(Ctx ctx)
        {
            float st = ctx.step;
            var h = new float[GN, GN];
            for (int z = 0; z < GN; z++) for (int x = 0; x < GN; x++) h[z, x] = float.MinValue;
            var solid = new bool[GN - 1, GN - 1];
            for (int z = 0; z < GN - 1; z++) for (int x = 0; x < GN - 1; x++) solid[z, x] = !InData(WorldX0 + (x + 0.5) * st, WorldZ0 + (z + 0.5) * st);
            foreach (Src s in ctx.src)
                for (int k = 0; k < s.t.Length; k += 3)
                {
                    Vector3 a = s.v[s.t[k]], b = s.v[s.t[k + 1]], c = s.v[s.t[k + 2]];
                    float lx = Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - WorldX0, hx = Mathf.Max(a.x, Mathf.Max(b.x, c.x)) - WorldX0;
                    float lz = Mathf.Min(a.z, Mathf.Min(b.z, c.z)) - WorldZ0, hz = Mathf.Max(a.z, Mathf.Max(b.z, c.z)) - WorldZ0;
                    int x0 = Mathf.Max(0, Mathf.CeilToInt(lx / st - 1e-4f)), x1 = Mathf.Min(GN - 1, Mathf.FloorToInt(hx / st + 1e-4f));
                    int z0 = Mathf.Max(0, Mathf.CeilToInt(lz / st - 1e-4f)), z1 = Mathf.Min(GN - 1, Mathf.FloorToInt(hz / st + 1e-4f));
                    for (int zi = z0; zi <= z1; zi++)
                        for (int xi = x0; xi <= x1; xi++)
                        {
                            if (!Bary(s, k, WorldX0 + xi * (double)st, WorldZ0 + zi * (double)st, out double w0, out double w1, out double w2)) continue;
                            float y = (float)(w0 * a.y + w1 * b.y + w2 * c.y);
                            if (y > h[zi, xi]) h[zi, xi] = y;
                        }
                    if (s.kind == Kind.Marker) continue;
                    int q0 = Mathf.Max(0, Mathf.CeilToInt(lx / st - 0.5f)), q1 = Mathf.Min(GN - 2, Mathf.FloorToInt(hx / st - 0.5f));
                    int r0 = Mathf.Max(0, Mathf.CeilToInt(lz / st - 0.5f)), r1 = Mathf.Min(GN - 2, Mathf.FloorToInt(hz / st - 0.5f));
                    for (int zi = r0; zi <= r1; zi++)
                        for (int xi = q0; xi <= q1; xi++)
                            if (!solid[zi, xi] && Bary(s, k, WorldX0 + (xi + 0.5) * st, WorldZ0 + (zi + 0.5) * st, out _, out _, out _)) solid[zi, xi] = true;
                }
            // inside the square: uncovered samples (the basement cut) take a covered neighbour; holes keep them out of sight
            int holes = 0, holeQuads = 0, gx0 = GX(-Half), gx1 = GX(Half), gz0 = GZ(-Half), gz1 = GZ(Half);
            for (int z = gz0; z <= gz1; z++)
                for (int x = gx0; x <= gx1; x++)
                {
                    if (h[z, x] > float.MinValue) continue;
                    holes++;
                    for (int r = 1; r < 12 && h[z, x] == float.MinValue; r++)
                    {
                        if (x - r >= gx0 && h[z, x - r] > float.MinValue) h[z, x] = h[z, x - r];
                        else if (x + r <= gx1 && h[z, x + r] > float.MinValue) h[z, x] = h[z, x + r];
                        else if (z - r >= gz0 && h[z - r, x] > float.MinValue) h[z, x] = h[z - r, x];
                        else if (z + r <= gz1 && h[z + r, x] > float.MinValue) h[z, x] = h[z + r, x];
                    }
                    if (h[z, x] == float.MinValue) h[z, x] = ctx.minY;
                }
            foreach (bool b in solid) if (!b) holeQuads++;
            ctx.heights = h;
            ctx.solid = solid;
            ctx.R($"heights: {GN} x {GN} samples over {TilesX * Tile} x {TilesZ * Tile} m, step {st.ToString("0.0000", ctx.ci)} m; package square: " +
                  $"{holes} uncovered samples filled, {holeQuads} hole quads ({(holeQuads * st * st).ToString("0", ctx.ci)} m², under the main building's basement)");
        }

        /// <summary>Generated land around the package's square: the edge height of the square carried outwards plus fractal
        /// noise that fades in over <see cref="BlendRange"/> m, so the tiles meet the square without a step.</summary>
        static void Surroundings(Ctx ctx)
        {
            float st = ctx.step;
            int gx0 = GX(-Half), gx1 = GX(Half), gz0 = GZ(-Half), gz1 = GZ(Half);
            float lo = float.MaxValue, hi = float.MinValue;
            for (int z = 0; z < GN; z++)
                for (int x = 0; x < GN; x++)
                {
                    float wx = WorldX0 + x * st, wz = WorldZ0 + z * st;
                    if (x >= gx0 && x <= gx1 && z >= gz0 && z <= gz1) { lo = Mathf.Min(lo, ctx.heights[z, x]); hi = Mathf.Max(hi, ctx.heights[z, x]); continue; }
                    int bx = Mathf.Clamp(x, gx0, gx1), bz = Mathf.Clamp(z, gz0, gz1);
                    float bwx = WorldX0 + bx * st, bwz = WorldZ0 + bz * st;
                    float d = Mathf.Sqrt((wx - bwx) * (wx - bwx) + (wz - bwz) * (wz - bwz));
                    float w = Mathf.SmoothStep(0f, 1f, d / BlendRange);
                    float y = ctx.heights[bz, bx] + w * (Noise(wx, wz) - Noise(bwx, bwz));
                    ctx.heights[z, x] = y;
                    lo = Mathf.Min(lo, y);
                    hi = Mathf.Max(hi, y);
                }
            ctx.bottom = Mathf.Floor(lo - BelowMin);
            ctx.sizeY = Mathf.Ceil(hi + AboveMax - ctx.bottom);
            ctx.R($"surroundings: 5 generated tiles, fractal noise (scales {string.Join("/", NoiseScale)} m, amplitudes {string.Join("/", NoiseAmp)} m) " +
                  $"faded in over {BlendRange} m from the square's edge; world Y {lo.ToString("0.0", ctx.ci)} .. {hi.ToString("0.0", ctx.ci)}; " +
                  $"terrain origin Y {ctx.bottom.ToString(ctx.ci)}, size Y {ctx.sizeY.ToString(ctx.ci)}, height step {(ctx.sizeY / 65535f * 1000f).ToString("0.0", ctx.ci)} mm");
        }

        static float Noise(float x, float z)
        {
            float n = 0f;
            for (int o = 0; o < NoiseScale.Length; o++)
                n += NoiseAmp[o] * (Mathf.PerlinNoise(x / NoiseScale[o] + 37.1f * (o + 1), z / NoiseScale[o] + 91.7f * (o + 1)) - 0.5f) * 2f;
            return n;
        }

        static void WriteHeights(Ctx ctx)
        {
            for (int ix = 0; ix < TilesX; ix++)
                for (int iz = 0; iz < TilesZ; iz++)
                {
                    TerrainData td = ctx.tiles[ix, iz];
                    td.heightmapResolution = TileRes;
                    td.size = new Vector3(Tile, ctx.sizeY, Tile);
                    var norm = new float[TileRes, TileRes];
                    var holes = new bool[TileRes - 1, TileRes - 1];
                    int ox = ix * (TileRes - 1), oz = iz * (TileRes - 1);
                    for (int z = 0; z < TileRes; z++)
                        for (int x = 0; x < TileRes; x++)
                            norm[z, x] = Mathf.Clamp01((ctx.heights[oz + z, ox + x] - ctx.bottom) / ctx.sizeY);
                    for (int z = 0; z < TileRes - 1; z++)
                        for (int x = 0; x < TileRes - 1; x++)
                            holes[z, x] = ctx.solid[oz + z, ox + x];
                    td.SetHeights(0, 0, norm);
                    td.SetHoles(0, 0, holes);                          // true = surface, false = hole
                }
        }

        /// <summary>Accuracy at random points of the package's square and at the supports of every placed model; supports
        /// over tolerance are fixed by levelling the heightmap cell under them to the mesh (all four corners move by the
        /// error), never by moving the model. Measured with Terrain.SampleHeight on temporary terrains.</summary>
        static void FitSupports(Ctx ctx)
        {
            var temp = new List<GameObject>();
            try
            {
                var terrains = new Terrain[TilesX, TilesZ];
                for (int ix = 0; ix < TilesX; ix++)
                    for (int iz = 0; iz < TilesZ; iz++)
                    {
                        var go = new GameObject("ForestTerrainBake_tmp") { hideFlags = HideFlags.HideAndDontSave };
                        temp.Add(go);
                        terrains[ix, iz] = go.AddComponent<Terrain>();
                        terrains[ix, iz].terrainData = ctx.tiles[ix, iz];
                        go.transform.position = TileOrigin(ix, iz, ctx.bottom);
                    }
                float Sample(Vector3 p)
                {
                    int ix = Mathf.Clamp(Mathf.FloorToInt((p.x - WorldX0) / Tile), 0, TilesX - 1), iz = Mathf.Clamp(Mathf.FloorToInt((p.z - WorldZ0) / Tile), 0, TilesZ - 1);
                    return terrains[ix, iz].SampleHeight(p) + ctx.bottom;
                }
                var sup = new List<(string what, Vector3 p, float mesh, float tol)>();
                void Add(Placed[] list, string group, bool flatTight)
                {
                    if (list == null) return;
                    foreach (Placed e in list)
                    {
                        if (e?.p == null || e.p.Length < 3) continue;
                        if (!ctx.grid.Top(e.p[0], e.p[2], out float mh, out float slope)) continue;
                        float tol = flatTight && slope < FlatSlope ? TolFlat : TolOther;
                        sup.Add(($"{group} {e.m}", new Vector3(e.p[0], e.p[1], e.p[2]), mh, tol));
                    }
                }
                Add(ctx.layout.prefabs, "prefab", true);
                Add(ctx.layout.rocks, "rock", true);
                Add(ctx.layout.trees, "tree", false);

                int before = sup.Count(s => Mathf.Abs(Sample(s.p) - s.mesh) > s.tol), fixedN = 0;
                for (int pass = 0; pass < 6; pass++)
                {
                    int changed = 0;
                    foreach (var s in sup)
                    {
                        float e = s.mesh - Sample(s.p);
                        if (Mathf.Abs(e) <= s.tol * 0.5f) continue;
                        int x = Mathf.Clamp(Mathf.FloorToInt((s.p.x - WorldX0) / ctx.step), 0, GN - 2), z = Mathf.Clamp(Mathf.FloorToInt((s.p.z - WorldZ0) / ctx.step), 0, GN - 2);
                        ctx.heights[z, x] += e; ctx.heights[z, x + 1] += e; ctx.heights[z + 1, x] += e; ctx.heights[z + 1, x + 1] += e;
                        changed++;
                    }
                    if (changed == 0) break;
                    fixedN += changed;
                    WriteHeights(ctx);
                }
                var after = sup.Select(s => (s, err: Sample(s.p) - s.mesh)).ToList();
                ctx.R($"supports: {sup.Count} (prefabs {ctx.layout.prefabs?.Length ?? 0}, rocks {ctx.layout.rocks?.Length ?? 0}, tree roots {ctx.layout.trees?.Length ?? 0}); " +
                      $"tolerance {TolFlat * 100:0} cm on flat (<{FlatSlope:0}°) prefabs/rocks/fences, {TolOther * 100:0} cm elsewhere; over before {before}, " +
                      $"{fixedN} cell corrections, over after {after.Count(a => Mathf.Abs(a.err) > a.s.tol)}, max {(after.Count == 0 ? 0f : after.Max(a => Mathf.Abs(a.err))).ToString("0.000", ctx.ci)} m");
                var rnd = new System.Random(2110);
                var errs = new List<float>(RandomPoints);
                for (int i = 0; i < RandomPoints; i++)
                {
                    float x = (float)(rnd.NextDouble() * Size - Half), z = (float)(rnd.NextDouble() * Size - Half);
                    if (!ctx.grid.Top(x, z, out float mh, out _)) continue;
                    errs.Add(Mathf.Abs(Sample(new Vector3(x, 0f, z)) - mh));
                }
                errs.Sort();
                ctx.R($"accuracy, {errs.Count} random points of the package square |SampleHeight + Y - mesh|: p50 {P(errs, 0.5f).ToString("0.000", ctx.ci)} m, " +
                      $"p95 {P(errs, 0.95f).ToString("0.000", ctx.ci)} m, max {errs[errs.Count - 1].ToString("0.000", ctx.ci)} m");
            }
            finally { foreach (GameObject go in temp) Object.DestroyImmediate(go); }
        }

        // ------------------------------------------------------------------ paint
        struct Tone { public Color lin; public float smooth, ao; }

        static void Paint(Ctx ctx)
        {
            Progress("Layer textures", 0.45f);
            float groundUvPerM = Median(ctx.src.Where(s => s.kind == Kind.Ground).SelectMany(UvDensity).ToList());
            float asphaltUvPerM = Median(ctx.src.Where(s => s.kind == Kind.Asphalt).SelectMany(UvDensity).ToList());
            Src road = ctx.src.First(s => s.kind == Kind.Road), trail = ctx.src.First(s => s.kind == Kind.Trail);
            float rockUvPerM = RockDensity();
            Texture2D gravelSrc = Load(Tex + "T_RoadGravel_A_px.png"), soilSrc = Load(Tex + "T_TrailSoil_A_px.png");
            Texture2D soilMaskSrc = Load(Tex + "T_TrailSoil_M_px.png");
            float roadPxPerM = Mathf.Sqrt(gravelSrc.width * road.gradU * gravelSrc.height * road.gradV);
            float trailPxPerM = Mathf.Sqrt(soilSrc.width * trail.gradU * soilSrc.height * trail.gradV);
            float floorTile = 1f / (groundUvPerM * 600f);              // M_ForestTerrain detail tiling 600
            float asphaltTile = 1f / (asphaltUvPerM * 0.25f);          // M_Asphalt tiling 0.25
            float rockTile = rockUvPerM > 0f ? 1f / rockUvPerM : 2f;   // M_Rock tiling 1
            float roadTile = TileTex / roadPxPerM, trailTile = TileTex / trailPxPerM;
            ctx.R($"pixel density: ground detail {(128f / floorTile).ToString("0.0", ctx.ci)} px/m (tile {floorTile.ToString("0.000", ctx.ci)} m); " +
                  $"road {roadPxPerM.ToString("0.0", ctx.ci)} px/m (width {(1f / road.gradU).ToString("0.00", ctx.ci)} m); " +
                  $"trail {trailPxPerM.ToString("0.0", ctx.ci)} px/m (width {(1f / trail.gradU).ToString("0.00", ctx.ci)} m); asphalt tile {asphaltTile.ToString("0.000", ctx.ci)} m; rock tile {rockTile.ToString("0.000", ctx.ci)} m");

            Texture2D tGravel = Tiles("T_TL_RoadGravel_px", gravelSrc, null, GravelCols, 101, false, out _);
            Texture2D tRuts = Tiles("T_TL_RoadRuts_px", gravelSrc, null, RutCols, 102, false, out _, 4);
            Texture2D tSoil = Tiles("T_TL_TrailSoil_px", soilSrc, soilMaskSrc, SoilCols, 103, true, out Texture2D mSoil);
            Texture2D tTrack = Tiles("T_TL_TrailTrack_px", soilSrc, soilMaskSrc, TrackCols, 104, true, out Texture2D mTrack, 4);
            Texture2D mRock = UrpMask("T_TL_Rock_M_px", Load(Tex + "T_Rock_M_px.png"));
            Texture2D mAsphalt = UrpMask("T_TL_Asphalt_M_px", Load(Tex + "T_Asphalt_M_px.png"));

            Texture2D macroA = Load(Tex + "T_ForestMacro_A.jpg"), macroM = Load(Tex + "T_ForestMacro_M.png");
            Texture2D detail = Load(Tex + "T_ForestDetail_A_px.png"), floor = Load(Tex + "T_ForestFloor_A_px.png");
            Tones(macroA, macroM, out Tone light, out Tone dark, out float gLight, out float gDark);
            Color detailRaw = Mean(detail, false);
            Color floorLin = Mean(floor, true);
            Color lightTarget = light.lin * 2f * detailRaw;
            Vector4 floorRemap = new Vector4(lightTarget.r / floorLin.r, lightTarget.g / floorLin.g, lightTarget.b / floorLin.b, 1f);
            Vector4 darkRemap = new Vector4(dark.lin.r * 2f, dark.lin.g * 2f, dark.lin.b * 2f, 1f);
            ctx.R($"floor tones (linear): light {Fmt(ctx, lightTarget)}, AO {light.ao.ToString("0.00", ctx.ci)}, smoothness {light.smooth.ToString("0.000", ctx.ci)}; " +
                  $"dark macro x2 {Fmt(ctx, dark.lin * 2f)}, AO {dark.ao.ToString("0.00", ctx.ci)}, smoothness {dark.smooth.ToString("0.000", ctx.ci)}");

            var asphaltColor = new Vector4(0.6571f, 0.6568f, 0.6207f, 1f);
            TerrainLayer[] layers =
            {
                Layer(LFloor, AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "T_ForestFloor_A_px.png"), null, floorTile, floorRemap, light.smooth, light.ao),
                Layer(LFloorDark, AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "T_ForestDetail_A_px.png"), null, floorTile * DarkTileRatio, darkRemap, dark.smooth, dark.ao),
                Layer(LRock, AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "T_Rock_A_px.png"), mRock, rockTile, Vector4.one, 0f, 1f),
                Layer(LGravel, tGravel, null, roadTile, Vector4.one, 0f, 1f),
                Layer(LRuts, tRuts, null, roadTile, Vector4.one, 0f, 1f),
                Layer(LSoil, tSoil, mSoil, trailTile, Vector4.one, 0f, 1f),
                Layer(LTrack, tTrack, mTrack, trailTile, Vector4.one, 0f, 1f),
                Layer(LAsphalt, AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "T_Asphalt_A_px.png"), mAsphalt, asphaltTile, asphaltColor, 0f, 1f),
            };
            EnsureMaterial();
            var sum = new double[Layers];
            for (int ix = 0; ix < TilesX; ix++)
                for (int iz = 0; iz < TilesZ; iz++)
                {
                    Progress($"Paint tile {ix}_{iz}", 0.5f + 0.3f * (ix * TilesZ + iz) / (TilesX * TilesZ));
                    TerrainData td = ctx.tiles[ix, iz];
                    td.terrainLayers = layers;
                    PaintTile(ctx, td, TileOrigin(ix, iz, 0f), macroM, gLight, gDark, sum);
                }
            foreach (Texture2D t in new[] { gravelSrc, soilSrc, soilMaskSrc, macroA, macroM, detail, floor }) Object.DestroyImmediate(t);
            double total = sum.Sum();
            ctx.R($"paint: {AlphaRes}² per tile, texel {ctx.texel.ToString("0.0000", ctx.ci)} m; share by layer: " +
                  string.Join(", ", Enumerable.Range(0, Layers).Select(l => $"{LayerNames[l]} {(sum[l] / total * 100).ToString("0.00", ctx.ci)} %")) +
                  "; outside the package square the floor tone follows noise and rock the slope");
        }

        static void PaintTile(Ctx ctx, TerrainData td, Vector3 origin, Texture2D macroM, float gLight, float gDark, double[] sum)
        {
            int n = AlphaRes;
            float tx = ctx.texel, sub = tx / Super, subW = 1f / (Super * Super), edge = tx;
            float ox = origin.x, oz = origin.z;
            var cov = new float[n * n * 3];
            var rut = new float[n * n];
            var trk = new float[n * n];
            var macroUv = new Vector2[n * n];
            var hasMacro = new bool[n * n];
            foreach (Src s in ctx.src)
            {
                if (s.kind == Kind.Marker) continue;
                int slot = s.kind == Kind.Road ? 0 : s.kind == Kind.Trail ? 1 : 2;
                for (int k = 0; k < s.t.Length; k += 3)
                {
                    Vector3 a = s.v[s.t[k]], b = s.v[s.t[k + 1]], c = s.v[s.t[k + 2]];
                    float lx = Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - ox, hx = Mathf.Max(a.x, Mathf.Max(b.x, c.x)) - ox;
                    float lz = Mathf.Min(a.z, Mathf.Min(b.z, c.z)) - oz, hz = Mathf.Max(a.z, Mathf.Max(b.z, c.z)) - oz;
                    if (hx < 0f || hz < 0f || lx > Tile || lz > Tile) continue;
                    if (s.kind == Kind.Ground)
                    {
                        int x0 = Mathf.Max(0, Mathf.CeilToInt(lx / tx - 0.5f)), x1 = Mathf.Min(n - 1, Mathf.FloorToInt(hx / tx - 0.5f));
                        int z0 = Mathf.Max(0, Mathf.CeilToInt(lz / tx - 0.5f)), z1 = Mathf.Min(n - 1, Mathf.FloorToInt(hz / tx - 0.5f));
                        for (int zi = z0; zi <= z1; zi++)
                            for (int xi = x0; xi <= x1; xi++)
                            {
                                if (!Bary(s, k, ox + (xi + 0.5) * tx, oz + (zi + 0.5) * tx, out double w0, out double w1, out double w2)) continue;
                                macroUv[zi * n + xi] = (float)w0 * s.uv[s.t[k]] + (float)w1 * s.uv[s.t[k + 1]] + (float)w2 * s.uv[s.t[k + 2]];
                                hasMacro[zi * n + xi] = true;
                            }
                        continue;
                    }
                    float width = 0f;
                    if (s.kind != Kind.Asphalt)
                    {
                        if (!PlanGradient(s, k, out Vector2 du, out _) || du.magnitude < 1e-5f) continue;
                        width = 1f / du.magnitude;
                    }
                    int sx0 = Mathf.Max(0, Mathf.FloorToInt(lx / sub)), sx1 = Mathf.Min(n * Super - 1, Mathf.FloorToInt(hx / sub));
                    int sz0 = Mathf.Max(0, Mathf.FloorToInt(lz / sub)), sz1 = Mathf.Min(n * Super - 1, Mathf.FloorToInt(hz / sub));
                    for (int zs = sz0; zs <= sz1; zs++)
                        for (int xs = sx0; xs <= sx1; xs++)
                        {
                            if (!Bary(s, k, ox + (xs + 0.5) * sub, oz + (zs + 0.5) * sub, out double w0, out double w1, out double w2)) continue;
                            int texel = (zs / Super) * n + xs / Super;
                            if (cov[texel * 3 + slot] >= 1f - 1e-4f) continue;
                            cov[texel * 3 + slot] += subW;
                            if (s.kind == Kind.Asphalt) continue;
                            float u = (float)(w0 * s.uv[s.t[k]].x + w1 * s.uv[s.t[k + 1]].x + w2 * s.uv[s.t[k + 2]].x);
                            if (s.kind == Kind.Road) rut[texel] += subW * Stripe(u, RutU, width, edge);
                            else trk[texel] += subW * Stripe(u, new[] { TrackU }, width, edge);
                        }
                }
            }
            var map = new float[n, n, Layers];
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    int i = z * n + x;
                    float r = Mathf.Min(cov[i * 3], 1f), t = Mathf.Min(cov[i * 3 + 1], 1f), a = Mathf.Min(cov[i * 3 + 2], 1f);
                    float total = r + t + a;
                    if (total > 1f) { r /= total; t /= total; a /= total; total = 1f; }
                    float ruts = cov[i * 3] > 0f ? r * Mathf.Clamp01(rut[i] / cov[i * 3]) : 0f;
                    float track = cov[i * 3 + 1] > 0f ? t * Mathf.Clamp01(trk[i] / cov[i * 3 + 1]) : 0f;
                    float rest = 1f - total;
                    float slope = td.GetSteepness((x + 0.5f) / n, (z + 0.5f) / n);
                    float rock = rest * Mathf.Clamp01((slope - RockFrom) / (RockTo - RockFrom));
                    float floorW = rest - rock;
                    float darkK;
                    if (hasMacro[i]) darkK = Mathf.Clamp01((gLight - Sample(macroM, macroUv[i]).g) / Mathf.Max(0.05f, gLight - gDark));
                    else
                    {
                        // generated land: crowns' shade as patchy noise, ~60 % dark like the package's forest
                        float wx = ox + (x + 0.5f) * tx, wz = oz + (z + 0.5f) * tx;
                        float pn = Mathf.PerlinNoise(wx / 9f + 11.3f, wz / 9f + 7.1f) * 0.7f + Mathf.PerlinNoise(wx / 31f + 3.7f, wz / 31f + 5.9f) * 0.3f;
                        darkK = Mathf.SmoothStep(0f, 1f, (pn - 0.38f) / 0.16f);
                    }
                    float darkW = floorW * darkK;
                    map[z, x, LFloor] = floorW - darkW;
                    map[z, x, LFloorDark] = darkW;
                    map[z, x, LRock] = rock;
                    map[z, x, LGravel] = r - ruts;
                    map[z, x, LRuts] = ruts;
                    map[z, x, LSoil] = t - track;
                    map[z, x, LTrack] = track;
                    map[z, x, LAsphalt] = a;
                    for (int l = 0; l < Layers; l++) sum[l] += map[z, x, l];
                }
            td.alphamapResolution = n;
            td.SetAlphamaps(0, 0, map);
        }

        /// <summary>Median |grad u| and |grad v| over the plan (1/m) of a strip mesh: u across (road width = 1 / |grad u|).</summary>
        static void Gradients(Src s)
        {
            var gu = new List<float>(s.t.Length / 3);
            var gv = new List<float>(s.t.Length / 3);
            for (int k = 0; k < s.t.Length; k += 3)
                if (PlanGradient(s, k, out Vector2 du, out Vector2 dv)) { gu.Add(du.magnitude); gv.Add(dv.magnitude); }
            s.gradU = Median(gu);
            s.gradV = Median(gv);
        }

        /// <summary>Gradient of u and v over the plan (x, z) on triangle k.</summary>
        static bool PlanGradient(Src s, int k, out Vector2 du, out Vector2 dv)
        {
            Vector3 a = s.v[s.t[k]], b = s.v[s.t[k + 1]], c = s.v[s.t[k + 2]];
            Vector2 ua = s.uv[s.t[k]], ub = s.uv[s.t[k + 1]], uc = s.uv[s.t[k + 2]];
            double x1 = b.x - a.x, z1 = b.z - a.z, x2 = c.x - a.x, z2 = c.z - a.z;
            double det = x1 * z2 - x2 * z1;
            du = dv = default;
            if (Math.Abs(det) < 1e-9) return false;
            double u1 = ub.x - ua.x, u2 = uc.x - ua.x, v1 = ub.y - ua.y, v2 = uc.y - ua.y;
            du = new Vector2((float)((u1 * z2 - u2 * z1) / det), (float)((x1 * u2 - x2 * u1) / det));
            dv = new Vector2((float)((v1 * z2 - v2 * z1) / det), (float)((x1 * v2 - x2 * v1) / det));
            return true;
        }

        /// <summary>Plan barycentrics of (x, z) in triangle k (inclusive with a small tolerance on shared edges).</summary>
        static bool Bary(Src s, int k, double x, double z, out double w0, out double w1, out double w2)
        {
            Vector3 a = s.v[s.t[k]], b = s.v[s.t[k + 1]], c = s.v[s.t[k + 2]];
            double d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            w0 = w1 = w2 = 0;
            if (Math.Abs(d) < 1e-12) return false;
            w0 = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / d;
            w1 = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / d;
            w2 = 1 - w0 - w1;
            const double e = -1e-7;
            return w0 >= e && w1 >= e && w2 >= e;
        }

        /// <summary>Triangles bucketed by plan cell: the exact mesh height at any point (the top one) for the accuracy check.</summary>
        sealed class TriGrid
        {
            const float Cell = 8f;
            readonly int _n = Mathf.CeilToInt(Size / Cell);
            readonly List<long>[] _cells;
            readonly List<Src> _src;

            public TriGrid(List<Src> src)
            {
                _src = src;
                _cells = new List<long>[_n * _n];
                for (int si = 0; si < src.Count; si++)
                {
                    Src s = src[si];
                    for (int k = 0; k < s.t.Length; k += 3)
                    {
                        Vector3 a = s.v[s.t[k]], b = s.v[s.t[k + 1]], c = s.v[s.t[k + 2]];
                        int x0 = C(Mathf.Min(a.x, Mathf.Min(b.x, c.x))), x1 = C(Mathf.Max(a.x, Mathf.Max(b.x, c.x)));
                        int z0 = C(Mathf.Min(a.z, Mathf.Min(b.z, c.z))), z1 = C(Mathf.Max(a.z, Mathf.Max(b.z, c.z)));
                        for (int z = z0; z <= z1; z++)
                            for (int x = x0; x <= x1; x++)
                                (_cells[z * _n + x] ??= new List<long>()).Add(((long)si << 32) | (uint)k);
                    }
                }
            }

            int C(float w) => Mathf.Clamp(Mathf.FloorToInt((w + Half) / Cell), 0, _n - 1);

            /// <summary>Top surface at (x, z): height and its slope (deg); false where no triangle covers the point.</summary>
            public bool Top(float x, float z, out float h, out float slope, bool groundOnly = false)
            {
                h = float.MinValue;
                slope = 0f;
                List<long> list = _cells[C(z) * _n + C(x)];
                if (list == null) return false;
                bool any = false;
                foreach (long e in list)
                {
                    Src s = _src[(int)(e >> 32)];
                    if (groundOnly && s.kind != Kind.Ground) continue;
                    int k = (int)(e & 0xffffffff);
                    if (!Bary(s, k, x, z, out double w0, out double w1, out double w2)) continue;
                    float y = (float)(w0 * s.v[s.t[k]].y + w1 * s.v[s.t[k + 1]].y + w2 * s.v[s.t[k + 2]].y);
                    if (y <= h) continue;
                    h = y;
                    any = true;
                    Vector3 nrm = Vector3.Cross(s.v[s.t[k + 1]] - s.v[s.t[k]], s.v[s.t[k + 2]] - s.v[s.t[k]]);
                    slope = Vector3.Angle(nrm.y < 0 ? -nrm : nrm, Vector3.up);
                }
                return any;
            }
        }

        /// <summary>Weight of the stripes at u across a strip <paramref name="width"/> m wide: full weight over the stripe but
        /// at least one texel wide, then a soft edge of one texel outside it (a texel-wide core survives the alphamap's
        /// bilinear filter; a centred ramp alone would never reach full weight).</summary>
        static float Stripe(float u, Vector2[] stripes, float width, float edge)
        {
            float w = 0f;
            foreach (Vector2 st in stripes)
            {
                float centre = (st.x + st.y) * 0.5f;
                float half = Mathf.Max((st.y - st.x) * 0.5f * width, edge * 0.5f);
                float d = Mathf.Abs(u - centre) * width;
                w = Mathf.Max(w, Mathf.Clamp01((half + edge - d) / edge));
            }
            return w;
        }

        static TerrainLayer Layer(int index, Texture2D albedo, Texture2D mask, float tile, Vector4 remap, float smoothness, float ao)
        {
            string path = $"{Folder}/{LayerNames[index]}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, path);
            }
            layer.diffuseTexture = albedo;
            layer.maskMapTexture = mask;
            layer.normalMapTexture = null;
            layer.tileSize = new Vector2(tile, tile);
            layer.tileOffset = Vector2.zero;
            layer.diffuseRemapMin = Vector4.zero;
            layer.diffuseRemapMax = new Vector4(remap.x, remap.y, remap.z, 1f);   // w = 1: no opacity-as-density
            layer.maskMapRemapMin = Vector4.zero;
            layer.maskMapRemapMax = new Vector4(1f, ao, 1f, 1f);                  // without a mask: G is the layer's AO
            layer.smoothness = smoothness;
            layer.metallic = 0f;
            layer.specular = Color.black;
            EditorUtility.SetDirty(layer);
            return layer;
        }

        static void EnsureMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            // the project's copy of URP Terrain Lit with a world-space macro tint (anti-tiling, user 21.10); URP's own as a fallback
            Shader sh = Shader.Find("Snow/Terrain/Lit Macro") ?? Shader.Find("Universal Render Pipeline/Terrain/Lit");
            if (mat == null)
            {
                mat = new Material(sh) { name = "M_Forest_TerrainLit" };
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }
            mat.shader = sh;
            if (mat.HasProperty("_EnableHeightBlend")) mat.SetFloat("_EnableHeightBlend", 0f);
            mat.DisableKeyword("_TERRAIN_BLEND_HEIGHT");
            mat.EnableKeyword("_MASKMAP");                              // layers with masks: rock, trail, asphalt
            mat.enableInstancing = true;
            string macroPath = Folder + "/T_TL_Macro.png";
            if (File.Exists(macroPath) && mat.HasProperty("_MacroTex"))
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(macroPath);
                if (ti != null && (ti.sRGBTexture || ti.filterMode != FilterMode.Bilinear || ti.textureCompression != TextureImporterCompression.Uncompressed))
                {
                    ti.sRGBTexture = false;
                    ti.filterMode = FilterMode.Bilinear;
                    ti.wrapMode = TextureWrapMode.Repeat;
                    ti.mipmapEnabled = true;
                    ti.textureCompression = TextureImporterCompression.Uncompressed;
                    ti.SaveAndReimport();
                }
                mat.SetTexture("_MacroTex", AssetDatabase.LoadAssetAtPath<Texture2D>(macroPath));
                mat.SetVector("_MacroScale", new Vector4(53f, 17f, 0f, 0f));
                mat.SetFloat("_MacroStrength", 0.4f);
                // no-tile: 40 m noise, A/B 21.10 (top-down repetition 0.28 -> 0.06, 0.54 -> 0.13; 23 m left swirl ghosts)
                mat.SetFloat("_NoTile", 1f);
                mat.SetFloat("_NoTileScale", 40f);
            }
            EditorUtility.SetDirty(mat);
        }

        // ------------------------------------------------------------------ layer textures
        /// <summary>Texture file as stored (sRGB bytes, readable): the importer's settings do not matter.</summary>
        static Texture2D Load(string path)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            t.LoadImage(File.ReadAllBytes(path));
            return t;
        }

        /// <summary>A seamless 64 x 64 tile made of 8 x 8 blocks copied from the given u columns of a strip texture: only
        /// source pixels, no blur, no new colours; the block grid wraps like the tile does. The mask tile (if any) takes
        /// the same blocks, converted to the URP layout.</summary>
        static Texture2D Tiles(string name, Texture2D src, Texture2D maskSrc, Vector2[] cols, int seed, bool withMask, out Texture2D mask, int block = Block)
        {
            var rnd = new System.Random(seed);
            var px = new Color32[TileTex * TileTex];
            var mpx = withMask ? new Color32[TileTex * TileTex] : null;
            Color32[] s = src.GetPixels32();
            Color32[] ms = withMask ? maskSrc.GetPixels32() : null;
            int w = src.width, h = src.height;
            var spans = cols.Select(c => (x0: Mathf.CeilToInt(c.x * w), x1: Mathf.FloorToInt(c.y * w) - block)).Where(c => c.x1 >= c.x0).ToArray();
            for (int by = 0; by < TileTex; by += block)
                for (int bx = 0; bx < TileTex; bx += block)
                {
                    var span = spans[rnd.Next(spans.Length)];
                    int sx = rnd.Next(span.x0, span.x1 + 1), sy = rnd.Next(0, h - block + 1);
                    for (int y = 0; y < block; y++)
                        for (int x = 0; x < block; x++)
                        {
                            int si = (sy + y) * w + sx + x, di = (by + y) * TileTex + bx + x;
                            Color32 c = s[si];
                            px[di] = new Color32(c.r, c.g, c.b, 255);
                            if (withMask)
                            {
                                Color32 m = ms[si];
                                mpx[di] = new Color32(m.r, m.g, 128, m.a);       // R metal, G AO (as the core), B height 0.5, A smooth
                            }
                        }
                }
            mask = withMask ? Save(name.Replace("_px", "_M_px"), mpx, TileTex, TileTex, true) : null;
            return Save(name, px, TileTex, TileTex, false);
        }

        /// <summary>The core's mask (R metallic, G AO — it is the URP Lit occlusion map too, A smoothness) with B = 0.5 for
        /// the terrain's height channel.</summary>
        static Texture2D UrpMask(string name, Texture2D src)
        {
            Color32[] p = src.GetPixels32();
            for (int i = 0; i < p.Length; i++) p[i].b = 128;
            return Save(name, p, src.width, src.height, true);
        }

        static Texture2D Save(string name, Color32[] px, int w, int h, bool mask)
        {
            var t = new Texture2D(w, h, mask ? TextureFormat.RGBA32 : TextureFormat.RGB24, false, true);
            t.SetPixels32(px);
            t.Apply(false);
            string path = $"{Folder}/{name}.png";
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Default;
            ti.textureShape = TextureImporterShape.Texture2D;
            ti.sRGBTexture = !mask;
            ti.alphaSource = mask ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            ti.alphaIsTransparency = false;
            ti.filterMode = FilterMode.Point;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.mipmapEnabled = true;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.isReadable = false;
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Light / dark floor classes of the macro (G >= 0.9 / G <= 0.6 of the mask, roads and water out by their
        /// smoothness): mean linear colour, smoothness (A) and AO (G).</summary>
        static void Tones(Texture2D macroA, Texture2D macroM, out Tone light, out Tone dark, out float gLight, out float gDark)
        {
            Color32[] a = macroA.GetPixels32(), m = macroM.GetPixels32();
            int aw = macroA.width, ah = macroA.height, mw = macroM.width, mh = macroM.height;
            double[] l = new double[5], d = new double[5];
            int nl = 0, nd = 0;
            for (int y = 0; y < mh; y++)
                for (int x = 0; x < mw; x++)
                {
                    Color32 mm = m[y * mw + x];
                    if (mm.a > 38) continue;                           // smoothness > 0.15: road, trail, water
                    Color32 c = a[(y * ah / mh) * aw + x * aw / mw];
                    float g = mm.g / 255f;
                    double[] acc = g >= 0.9f ? l : g <= 0.6f ? d : null;
                    if (acc == null) continue;
                    acc[0] += Lin(c.r); acc[1] += Lin(c.g); acc[2] += Lin(c.b); acc[3] += mm.a / 255.0; acc[4] += g;
                    if (acc == l) nl++; else nd++;
                }
            light = new Tone { lin = new Color((float)(l[0] / nl), (float)(l[1] / nl), (float)(l[2] / nl)), smooth = (float)(l[3] / nl), ao = (float)(l[4] / nl) };
            dark = new Tone { lin = new Color((float)(d[0] / nd), (float)(d[1] / nd), (float)(d[2] / nd)), smooth = (float)(d[3] / nd), ao = (float)(d[4] / nd) };
            gLight = light.ao;
            gDark = dark.ao;
        }

        static float Lin(byte c)
        {
            float v = c / 255f;
            return v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
        }

        static Color Mean(Texture2D t, bool srgb)
        {
            Color32[] p = t.GetPixels32();
            double r = 0, g = 0, b = 0;
            foreach (Color32 c in p)
            {
                r += srgb ? Lin(c.r) : c.r / 255.0;
                g += srgb ? Lin(c.g) : c.g / 255.0;
                b += srgb ? Lin(c.b) : c.b / 255.0;
            }
            return new Color((float)(r / p.Length), (float)(g / p.Length), (float)(b / p.Length));
        }

        /// <summary>Bilinear sample (as stored, 0..1) at uv; v = 0 is the bottom row (Unity convention).</summary>
        static Color Sample(Texture2D t, Vector2 uv)
        {
            float fx = Mathf.Clamp(uv.x * t.width - 0.5f, 0f, t.width - 1.001f), fy = Mathf.Clamp(uv.y * t.height - 0.5f, 0f, t.height - 1.001f);
            int x = (int)fx, y = (int)fy;
            float ax = fx - x, ay = fy - y;
            Color c00 = t.GetPixel(x, y), c10 = t.GetPixel(x + 1, y), c01 = t.GetPixel(x, y + 1), c11 = t.GetPixel(x + 1, y + 1);
            return Color.Lerp(Color.Lerp(c00, c10, ax), Color.Lerp(c01, c11, ax), ay);
        }

        /// <summary>sqrt(uv area / plan area) per triangle: UV units per metre.</summary>
        static IEnumerable<float> UvDensity(Src s)
        {
            for (int k = 0; k < s.t.Length; k += 3)
            {
                Vector3 a = s.v[s.t[k]], b = s.v[s.t[k + 1]], c = s.v[s.t[k + 2]];
                Vector2 ua = s.uv[s.t[k]], ub = s.uv[s.t[k + 1]], uc = s.uv[s.t[k + 2]];
                float area = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                float uvArea = Mathf.Abs((ub.x - ua.x) * (uc.y - ua.y) - (uc.x - ua.x) * (ub.y - ua.y)) * 0.5f;
                if (area > 1e-4f && uvArea > 0f) yield return Mathf.Sqrt(uvArea / area);
            }
        }

        /// <summary>UV units per metre of the rock models of the layout (M_Rock, tiling 1).</summary>
        static float RockDensity()
        {
            var d = new List<float>();
            foreach (string n in new[] { "Rock_1", "Rock_2", "Rock_3", "Rock_4" })
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + n + ".prefab");
                if (go == null || !go.TryGetComponent(out MeshFilter mf) || mf.sharedMesh == null) continue;
                Mesh m = mf.sharedMesh;
                var s = new Src { v = m.vertices, uv = m.uv, t = m.triangles };
                if (s.uv.Length == s.v.Length) d.AddRange(UvDensity(s));
            }
            return Median(d);
        }


        // ------------------------------------------------------------------ vegetation (trees, rocks, details)
        sealed class TileVeg
        {
            public readonly List<TreeInstance> trees = new List<TreeInstance>();
            public readonly int[][,] details = new int[DetailOrder.Length][,];
        }

        static void Vegetation(Ctx ctx, BakeMeta meta)
        {
            Progress("Vegetation", 0.85f);
            Layout L = ctx.layout;
            // tree prototypes: the layout's species (builder prefabs, LOD + capsule) and the rock-likes (Terrain/Prototypes)
            var protoNames = new List<string>();
            var protoPaths = new List<string>();
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            int Proto(string m)
            {
                int i = protoNames.IndexOf(m);
                if (i >= 0) return i;
                string path = RockTrees.Any(p => m.StartsWith(p, StringComparison.Ordinal))
                    ? AssetDatabase.GetAssetPath(TreePrototypePrefab(m)) : Prefabs + m + ".prefab";
                protoNames.Add(m);
                protoPaths.Add(path);
                return protoNames.Count - 1;
            }
            var tv = new TileVeg[TilesX, TilesZ];
            for (int ix = 0; ix < TilesX; ix++) for (int iz = 0; iz < TilesZ; iz++)
                {
                    tv[ix, iz] = new TileVeg();
                    for (int d = 0; d < DetailOrder.Length; d++) tv[ix, iz].details[d] = new int[DetailRes, DetailRes];
                }
            var scale = new Dictionary<string, Vector2>(StringComparer.Ordinal);
            int clamped = 0;
            void Count(string m) => counts[m] = counts.TryGetValue(m, out int c) ? c + 1 : 1;
            bool AddTree(string m, float x, float y, float z, float yawRad, float s)
            {
                int ix = Mathf.FloorToInt((x - WorldX0) / Tile), iz = Mathf.FloorToInt((z - WorldZ0) / Tile);
                if (ix < 0 || iz < 0 || ix >= TilesX || iz >= TilesZ) { clamped++; return false; }
                Vector3 o = TileOrigin(ix, iz, meta.originY);
                tv[ix, iz].trees.Add(new TreeInstance
                {
                    prototypeIndex = Proto(m),
                    position = new Vector3(Mathf.Clamp01((x - o.x) / Tile), (y - meta.originY) / meta.sizeY, Mathf.Clamp01((z - o.z) / Tile)),
                    rotation = yawRad, widthScale = s, heightScale = s,
                    color = new Color32(255, 255, 255, 255), lightmapColor = new Color32(255, 255, 255, 255),
                });
                Count(m);
                return true;
            }
            bool AddDetail(string m, float x, float z, float s)
            {
                int d = Array.IndexOf(DetailOrder, m);
                if (d < 0) return false;
                int ix = Mathf.FloorToInt((x - WorldX0) / Tile), iz = Mathf.FloorToInt((z - WorldZ0) / Tile);
                if (ix < 0 || iz < 0 || ix >= TilesX || iz >= TilesZ) return false;
                int cx = Mathf.Clamp(Mathf.FloorToInt((x - (WorldX0 + ix * Tile)) / Tile * DetailRes), 0, DetailRes - 1);
                int cz = Mathf.Clamp(Mathf.FloorToInt((z - (WorldZ0 + iz * Tile)) / Tile * DetailRes), 0, DetailRes - 1);
                int[,] layer = tv[ix, iz].details[d];
                if (layer[cz, cx] >= 16) return false;
                layer[cz, cx]++;
                Vector2 r = scale.TryGetValue(m, out Vector2 v) ? v : new Vector2(s, s);
                scale[m] = new Vector2(Mathf.Min(r.x, s), Mathf.Max(r.y, s));
                Count(m);
                return true;
            }
            float Yaw(float[] q) => new Quaternion(q[0], q[1], q[2], q[3]).eulerAngles.y * Mathf.Deg2Rad;

            // 1. the layout: trees and bushes, ferns and weeds; rocks, stumps, snags as trees, branches and pebbles as details
            var occupied = new HashSet<long>();                        // 2 m cells with a trunk or rock: saplings keep out
            long Cell(float x, float z) => ((long)Mathf.FloorToInt(x / 2f) << 32) ^ (uint)Mathf.FloorToInt(z / 2f);
            int layoutTrees = 0, layoutRocks = 0, layoutDetails = 0;
            foreach (Placed e in L.trees)
            {
                if (e?.m == null || e.p == null) continue;
                float s = e.s > 0f ? e.s : 1f;
                if (DetailSpecies.Any(p => e.m.StartsWith(p, StringComparison.Ordinal))) { if (AddDetail(e.m, e.p[0], e.p[2], s)) layoutDetails++; continue; }
                if (!TreeSpecies.Any(p => e.m.StartsWith(p, StringComparison.Ordinal))) continue;
                if (AddTree(e.m, e.p[0], e.p[1], e.p[2], Yaw(e.q), s)) { layoutTrees++; occupied.Add(Cell(e.p[0], e.p[2])); }
            }
            HashSet<Placed> yardFence = YardFence(L);
            int fencesDropped = L.rocks.Count(e => e?.m == "FenceChain") - yardFence.Count;
            foreach (Placed e in L.rocks)
            {
                if (e?.m == null || e.p == null) continue;
                float s = e.s > 0f ? e.s : 1f;
                if (e.m == "FenceChain")
                {
                    // the yard fence on the smaller yard: its plan position drawn in, the ground under it
                    if (!yardFence.Contains(e)) continue;
                    Vector3 fp = ForestYardPlan.InYard(V3(e.p)) ? ForestYardPlan.Map(V3(e.p)) : V3(e.p);
                    if (AddTree(e.m, fp.x, HeightAt(fp.x, fp.z), fp.z, new Quaternion(e.q[0], e.q[1], e.q[2], e.q[3]).eulerAngles.y * Mathf.Deg2Rad, s)) { layoutRocks++; occupied.Add(Cell(fp.x, fp.z)); }
                    continue;
                }
                if (RockTrees.Any(p => e.m.StartsWith(p, StringComparison.Ordinal)))
                {
                    // the tilt of a log along its slope lives in the prototype (5° bins), the yaw in the instance
                    Vector3 eu = new Quaternion(e.q[0], e.q[1], e.q[2], e.q[3]).eulerAngles;
                    string m = e.m.StartsWith("Log_", StringComparison.Ordinal) ? LogVariant(e.m, eu.x, eu.z) : e.m;
                    if (AddTree(m, e.p[0], e.p[1], e.p[2], eu.y * Mathf.Deg2Rad, s)) { layoutRocks++; occupied.Add(Cell(e.p[0], e.p[2])); }
                }
                else if (e.m.StartsWith("Branches_", StringComparison.Ordinal) || e.m.StartsWith("Pebbles_", StringComparison.Ordinal)) { if (AddDetail(e.m, e.p[0], e.p[2], s)) layoutDetails++; }
            }
            ctx.R($"layout: {layoutTrees} trees and bushes, {layoutRocks} rocks / stumps / snags / logs / fences / posts as Terrain trees " +
                  $"(log tilt in {TiltBin}° prototype bins), {layoutDetails} details (ferns, weeds, branches, pebbles); out of the world: {clamped}; " +
                  $"yard drawn in x{ForestYardPlan.Scale} (area / 2): {yardFence.Count} chain-link sections kept, {fencesDropped} overlapping or in the gate opening dropped");

            // species mix and per-hectare densities of the package's forest, for the generated land
            float area = Size * Size / 10000f;
            var mix = L.trees.Where(e => e?.m != null && TreeSpecies.Any(p => e.m.StartsWith(p, StringComparison.Ordinal)))
                             .GroupBy(e => e.m).Select(g => (m: g.Key, n: g.Count())).OrderBy(g => g.m, StringComparer.Ordinal).ToList();
            int mixTotal = mix.Sum(g => g.n);
            var rockMix = L.rocks.Where(e => e?.m != null && !PlacedOnly.Any(p => e.m.StartsWith(p, StringComparison.Ordinal)) && (RockTrees.Any(p => e.m.StartsWith(p, StringComparison.Ordinal)) ||
                                                                 e.m.StartsWith("Branches_", StringComparison.Ordinal) || e.m.StartsWith("Pebbles_", StringComparison.Ordinal)))
                                 .GroupBy(e => e.m).Select(g => (m: g.Key, perHa: g.Count() / area)).OrderBy(g => g.m, StringComparer.Ordinal).ToList();

            // buildings keep their surroundings clear of saplings and extra cover
            var clear = L.prefabs.Where(p => p?.p != null).Select(p =>
            {
                Vector3 at = ForestYardPlan.Structures.Contains(p.m) && ForestYardPlan.InYard(V3(p.p)) ? ForestYardPlan.Map(V3(p.p)) : V3(p.p);
                return (c: new Vector2(at.x, at.z), r: p.m == "BaseMain" ? BaseClear : PrefabClear);
            }).ToList();
            bool NearBuilding(float x, float z) => clear.Any(c => (c.c - new Vector2(x, z)).sqrMagnitude < c.r * c.r);

            float HeightAt(float x, float z)
            {
                int ix = Mathf.Clamp(Mathf.FloorToInt((x - WorldX0) / Tile), 0, TilesX - 1), iz = Mathf.Clamp(Mathf.FloorToInt((z - WorldZ0) / Tile), 0, TilesZ - 1);
                TerrainData td = ctx.tiles[ix, iz];
                Vector3 o = TileOrigin(ix, iz, 0f);
                return meta.originY + td.GetInterpolatedHeight((x - o.x) / Tile, (z - o.z) / Tile);
            }
            // free forest floor: no road, trail, asphalt or rock paint, gentle slope
            var alphas = new float[TilesX, TilesZ][,,];
            for (int ix = 0; ix < TilesX; ix++) for (int iz = 0; iz < TilesZ; iz++) alphas[ix, iz] = ctx.tiles[ix, iz].GetAlphamaps(0, 0, AlphaRes, AlphaRes);
            bool FreeFloor(float x, float z, float maxSlope)
            {
                int ix = Mathf.FloorToInt((x - WorldX0) / Tile), iz = Mathf.FloorToInt((z - WorldZ0) / Tile);
                if (ix < 0 || iz < 0 || ix >= TilesX || iz >= TilesZ) return false;
                Vector3 o = TileOrigin(ix, iz, 0f);
                float u = (x - o.x) / Tile, v = (z - o.z) / Tile;
                int ax = Mathf.Clamp((int)(u * AlphaRes), 0, AlphaRes - 1), az = Mathf.Clamp((int)(v * AlphaRes), 0, AlphaRes - 1);
                float[,,] a = alphas[ix, iz];
                if (a[az, ax, LFloor] + a[az, ax, LFloorDark] < 0.95f) return false;
                return ctx.tiles[ix, iz].GetSteepness(u, v) <= maxSlope;
            }

            var rnd = new System.Random(2210);
            float R01() => (float)rnd.NextDouble();
            // 2. generated forest on the five new tiles (jittered grid, the package's species mix, ~320 stems/ha)
            int genTrees = 0, genRocks = 0, genDetails = 0;
            for (float z = WorldZ0 + GenTreeSpacing * 0.5f; z < WorldZ0 + TilesZ * Tile; z += GenTreeSpacing)
                for (float x = WorldX0 + GenTreeSpacing * 0.5f; x < WorldX0 + TilesX * Tile; x += GenTreeSpacing)
                {
                    float px = x + (R01() - 0.5f) * GenTreeSpacing * 0.9f, pz = z + (R01() - 0.5f) * GenTreeSpacing * 0.9f;
                    if (InData(px, pz) || !FreeFloor(px, pz, 40f)) continue;
                    int pick = rnd.Next(mixTotal), acc = 0;
                    string m = mix[mix.Count - 1].m;
                    foreach (var g in mix) { acc += g.n; if (pick < acc) { m = g.m; break; } }
                    if (AddTree(m, px, HeightAt(px, pz), pz, R01() * Mathf.PI * 2f, 0.75f + R01() * 0.5f)) genTrees++;
                }
            // rocks, stumps, snags, branches, pebbles at the package's densities
            float newArea = (TilesX * Tile * TilesZ * Tile - Size * Size) / 10000f;
            foreach (var g in rockMix)
            {
                int n = Mathf.RoundToInt(g.perHa * newArea), tries = 0;
                bool tree = RockTrees.Any(p => g.m.StartsWith(p, StringComparison.Ordinal));
                for (int k = 0; k < n && tries < n * 6; tries++)
                {
                    float px = WorldX0 + R01() * TilesX * Tile, pz = WorldZ0 + R01() * TilesZ * Tile;
                    if (InData(px, pz) || !FreeFloor(px, pz, 35f)) continue;
                    float s = g.m.StartsWith("Rock_", StringComparison.Ordinal) ? 0.3f + R01() * 1.4f : 0.8f + R01() * 0.5f;
                    if (tree ? AddTree(g.m, px, HeightAt(px, pz), pz, R01() * Mathf.PI * 2f, s) : AddDetail(g.m, px, pz, s)) { k++; if (tree) genRocks++; else genDetails++; }
                }
            }
            // 2b. the old yard's clearing outside the smaller yard grows over (ForestYardPlan): forest of the same mix and density
            int infill = 0;
            {
                Vector2 lo = new Vector2(232f, -443f), hi = new Vector2(448f, -281f);         // the old chain-link fence's extent
                Vector3 nlo = ForestYardPlan.Map(new Vector3(lo.x, 0f, lo.y)), nhi = ForestYardPlan.Map(new Vector3(hi.x, 0f, hi.y));
                for (float z = lo.y; z < hi.y; z += GenTreeSpacing)
                    for (float x = lo.x; x < hi.x; x += GenTreeSpacing)
                    {
                        float px = x + R01() * GenTreeSpacing * 0.9f, pz = z + R01() * GenTreeSpacing * 0.9f;
                        if (px > nlo.x - 4f && px < nhi.x + 4f && pz > nlo.z - 4f && pz < nhi.z + 4f) continue;   // the yard itself
                        if (occupied.Contains(Cell(px, pz)) || NearBuilding(px, pz) || !FreeFloor(px, pz, 40f)) continue;
                        int pick = rnd.Next(mixTotal), acc = 0;
                        string m = mix[mix.Count - 1].m;
                        foreach (var g in mix) { acc += g.n; if (pick < acc) { m = g.m; break; } }
                        if (AddTree(m, px, HeightAt(px, pz), pz, R01() * Mathf.PI * 2f, 0.75f + R01() * 0.5f)) infill++;
                    }
            }
            ctx.R($"old yard clearing outside the smaller yard: {infill} trees");

            // 3. saplings 2–8 m in the package square, off roads and away from buildings and trunks
            int saplings = 0;
            for (float z = -Half + SaplingSpacing * 0.5f; z < Half; z += SaplingSpacing)
                for (float x = -Half + SaplingSpacing * 0.5f; x < Half; x += SaplingSpacing)
                {
                    if (R01() > SaplingChance) continue;
                    float px = x + (R01() - 0.5f) * SaplingSpacing * 0.9f, pz = z + (R01() - 0.5f) * SaplingSpacing * 0.9f;
                    if (occupied.Contains(Cell(px, pz)) || NearBuilding(px, pz) || !FreeFloor(px, pz, 32f)) continue;
                    if (AddTree(Saplings[rnd.Next(Saplings.Length)], px, HeightAt(px, pz), pz, R01() * Mathf.PI * 2f, 0.3f + R01() * 0.35f)) saplings++;
                }
            // 4. ground cover: ferns and weeds on the free floor of every tile (~0.11 per m²)
            int cover = 0;
            float cell = Tile / DetailRes, cellArea = cell * cell;
            for (int ix = 0; ix < TilesX; ix++)
                for (int iz = 0; iz < TilesZ; iz++)
                {
                    Vector3 o = TileOrigin(ix, iz, 0f);
                    for (int cz = 0; cz < DetailRes; cz++)
                        for (int cx = 0; cx < DetailRes; cx++)
                        {
                            float px = o.x + (cx + 0.5f) * cell, pz = o.z + (cz + 0.5f) * cell;
                            float pf = FernsPerM2 * cellArea, pw = WeedsPerM2 * cellArea;
                            float roll = R01();
                            if (roll >= pf + pw) continue;
                            if (!FreeFloor(px, pz, 38f) || (InData(px, pz) && NearBuilding(px, pz))) continue;
                            string m = roll < pf ? (R01() < 0.5f ? "Fern_1" : "Fern_2") : DetailOrder[2 + rnd.Next(3)];
                            if (AddDetail(m, px, pz, 0.8f + R01() * 0.45f)) cover++;
                        }
                }
            ctx.R($"generated: {genTrees} trees on the new tiles (~{GenTreeSpacing} m grid), {genRocks} rocks / stumps / snags and {genDetails} branches / pebbles " +
                  $"at the package's densities, {saplings} saplings in the package square (~{SaplingChance / (SaplingSpacing * SaplingSpacing) * 10000f:0}/ha), " +
                  $"{cover} ferns and weeds of ground cover (~{FernsPerM2 + WeedsPerM2:0.00}/m² of free floor)");

            // prototypes and instances per tile
            var tprotos = protoPaths.Select(p => new TreePrototype { prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p), bendFactor = 0f }).ToArray();
            if (tprotos.Any(t => t.prefab == null)) { Debug.LogError("[Snow] Forest terrain bake: a tree prototype prefab is missing: " + string.Join(", ", protoPaths.Where(p => AssetDatabase.LoadAssetAtPath<GameObject>(p) == null))); return; }
            var dprotos = new DetailPrototype[DetailOrder.Length];
            var dpaths = new string[DetailOrder.Length];
            for (int d = 0; d < DetailOrder.Length; d++)
            {
                GameObject prefab = DetailPrototypePrefab(DetailOrder[d]);
                if (prefab == null) { Debug.LogError($"[Snow] Forest terrain bake: {DetailOrder[d]}.prefab has no mesh on its root."); return; }
                dpaths[d] = AssetDatabase.GetAssetPath(prefab);
                Vector2 r = scale.TryGetValue(DetailOrder[d], out Vector2 v) ? v : new Vector2(0.8f, 1.25f);
                dprotos[d] = new DetailPrototype
                {
                    prototype = prefab, usePrototypeMesh = true, useInstancing = true, renderMode = DetailRenderMode.VertexLit,
                    minWidth = r.x, maxWidth = r.y, minHeight = r.x, maxHeight = r.y,
                    healthyColor = Color.white, dryColor = Color.white, noiseSpread = 0.1f, density = 1f,
                };
            }
            for (int ix = 0; ix < TilesX; ix++)
                for (int iz = 0; iz < TilesZ; iz++)
                {
                    TerrainData td = ctx.tiles[ix, iz];
                    td.treeInstances = new TreeInstance[0];
                    td.treePrototypes = tprotos;
                    td.SetTreeInstances(tv[ix, iz].trees.ToArray(), false);
                    td.SetDetailResolution(DetailRes, DetailPatch);
                    td.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
                    td.detailPrototypes = dprotos;
                    for (int d = 0; d < DetailOrder.Length; d++) td.SetDetailLayer(0, 0, d, tv[ix, iz].details[d]);
                }
            meta.treePrefabs = protoPaths.ToArray();
            meta.detailPrefabs = dpaths;
            ctx.R($"prototypes: {tprotos.Length} trees ({string.Join(", ", protoNames.Select(nm => $"{nm} {counts[nm]}"))}); " +
                  $"{dprotos.Length} details ({string.Join(", ", DetailOrder.Select(nm => $"{nm} {(counts.TryGetValue(nm, out int c) ? c : 0)}"))}); " +
                  $"details {DetailRes}² per tile ({cell.ToString("0.00", ctx.ci)} m cells), instance count mode, max 16 per cell");
        }

        static Vector3 V3(float[] a) => new Vector3(a[0], a[1], a[2]);

        /// <summary>Chain-link sections of the smaller yard: drawn in towards the building they would overlap by a third, so
        /// going round the yard only sections at least 2.7 m from the last kept one stay; none in the moved gate's opening.</summary>
        static HashSet<Placed> YardFence(Layout L)
        {
            Placed gate = L.prefabs.FirstOrDefault(p => p?.m == "Gate");
            Vector3 gateAt = gate != null ? ForestYardPlan.Map(V3(gate.p)) : new Vector3(1e6f, 0f, 1e6f);
            var all = L.rocks.Where(e => e?.m == "FenceChain" && e.p != null).ToList();
            var kept = new HashSet<Placed>();
            var yard = all.Where(e => ForestYardPlan.InYard(V3(e.p)))
                          .OrderBy(e => Mathf.Atan2(e.p[2] - ForestYardPlan.Centre.z, e.p[0] - ForestYardPlan.Centre.x)).ToList();
            var keptAt = new List<Vector3>();
            foreach (Placed e in yard)
            {
                Vector3 m = ForestYardPlan.Map(V3(e.p));
                if (new Vector2(m.x - gateAt.x, m.z - gateAt.z).magnitude < 4.5f) continue;
                if (keptAt.Any(k => new Vector2(m.x - k.x, m.z - k.z).magnitude < 2.7f)) continue;
                kept.Add(e);
                keptAt.Add(m);
            }
            foreach (Placed e in all) if (!ForestYardPlan.InYard(V3(e.p))) kept.Add(e);
            return kept;
        }

        /// <summary>Prototype name of a log with its tilt (euler x, z of the layout rotation) in <see cref="TiltBin"/>° bins:
        /// Log_12_t10_-5; untilted logs keep the plain name.</summary>
        static string LogVariant(string model, float ex, float ez)
        {
            int bx = Mathf.RoundToInt(Mathf.DeltaAngle(0f, ex) / TiltBin) * (int)TiltBin, bz = Mathf.RoundToInt(Mathf.DeltaAngle(0f, ez) / TiltBin) * (int)TiltBin;
            return bx == 0 && bz == 0 ? model : $"{model}_t{bx}_{bz}";
        }

        /// <summary>A rock, stump, snag, log, fence section or post of the layout as a Terrain tree prototype
        /// (Terrain/Prototypes/&lt;name&gt;.prefab): the same mesh, materials and layer, and a collider the Terrain collider can
        /// use instead of the mesh collider — a box on the mesh bounds (rocks, stumps, fences, posts), an upright capsule for
        /// snags, a capsule along the log; OldFence has none (no-collider model). A tilted log (Log_12_t10_-5) carries its
        /// tilt on a child under a one-level LODGroup (Terrain trees turn only about Y). Refreshed in place (same GUID).</summary>
        public static GameObject TreePrototypePrefab(string name)
        {
            string model = name;
            Quaternion tilt = Quaternion.identity;
            var mt = System.Text.RegularExpressions.Regex.Match(name, @"^(Log_\d+)_t(-?\d+)_(-?\d+)$");
            if (mt.Success)
            {
                model = mt.Groups[1].Value;
                tilt = Quaternion.Euler(int.Parse(mt.Groups[2].Value, CultureInfo.InvariantCulture), 0f, int.Parse(mt.Groups[3].Value, CultureInfo.InvariantCulture));
            }
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + model + ".prefab");
            if (src == null || !src.TryGetComponent(out MeshFilter smf) || smf.sharedMesh == null || !src.TryGetComponent(out MeshRenderer smr)) return null;
            if (!AssetDatabase.IsValidFolder(PrototypeFolder)) AssetDatabase.CreateFolder(Folder, "Prototypes");
            string path = $"{PrototypeFolder}/{name}.prefab";
            var go = new GameObject(name) { layer = src.layer };
            try
            {
                GameObject meshGo = go;
                if (mt.Success)
                {
                    meshGo = new GameObject("Mesh") { layer = src.layer };
                    meshGo.transform.SetParent(go.transform, false);
                    meshGo.transform.localRotation = tilt;
                }
                meshGo.AddComponent<MeshFilter>().sharedMesh = smf.sharedMesh;
                var mr = meshGo.AddComponent<MeshRenderer>();
                mr.sharedMaterials = smr.sharedMaterials;
                mr.shadowCastingMode = smr.shadowCastingMode;
                if (mt.Success)
                {
                    var lg = go.AddComponent<LODGroup>();
                    lg.SetLODs(new[] { new LOD(0.002f, new Renderer[] { mr }) });
                    lg.RecalculateBounds();
                }
                // collider on the root, on the (tilted) mesh bounds: Terrain tree colliders do not rotate with the tilt
                Bounds mb = smf.sharedMesh.bounds, b = new Bounds(tilt * mb.center, Vector3.zero);
                for (int k = 0; k < 8; k++)
                    b.Encapsulate(tilt * (mb.center + Vector3.Scale(mb.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1))));
                if (model.StartsWith("OldFence", StringComparison.Ordinal)) { }
                else if (model.StartsWith("Snag_", StringComparison.Ordinal))
                {
                    var cap = go.AddComponent<CapsuleCollider>();
                    cap.direction = 1;
                    cap.center = b.center;
                    cap.radius = Mathf.Max(0.1f, 0.35f * Mathf.Min(b.size.x, b.size.z));
                    cap.height = Mathf.Max(b.size.y, cap.radius * 2f);
                }
                else if (model.StartsWith("Log_", StringComparison.Ordinal))
                {
                    var cap = go.AddComponent<CapsuleCollider>();
                    int axis = mb.size.x >= mb.size.z ? 0 : 2;          // along the log
                    cap.direction = axis;
                    cap.center = b.center;
                    cap.radius = Mathf.Max(0.1f, 0.5f * Mathf.Min(mb.size.y, axis == 0 ? mb.size.z : mb.size.x));
                    cap.height = Mathf.Max(axis == 0 ? b.size.x : b.size.z, cap.radius * 2f);
                }
                else
                {
                    var box = go.AddComponent<BoxCollider>();
                    box.center = b.center;
                    bool rock = model.StartsWith("Rock_", StringComparison.Ordinal) || model == "Stump";
                    box.size = rock ? new Vector3(b.size.x * 0.9f, b.size.y, b.size.z * 0.9f) : b.size;
                }
                return PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>K4.2: a detail prototype may not carry a LODGroup (Unity refuses it), and the Fern / Weeds prefabs of
        /// the builder have one on the root. The prototype is a prefab in Terrain/Prototypes with the same mesh, materials,
        /// shadow mode and layer, no LODGroup and no collider. Refreshed in place (same file, same GUID) from the builder's
        /// prefab, so a rebuild that rewrites meshes or materials is picked up by the next integration.</summary>
        public static GameObject DetailPrototypePrefab(string name)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + name + ".prefab");
            if (src == null || !src.TryGetComponent(out MeshFilter smf) || smf.sharedMesh == null || !src.TryGetComponent(out MeshRenderer smr)) return null;
            if (!AssetDatabase.IsValidFolder(PrototypeFolder)) AssetDatabase.CreateFolder(Folder, "Prototypes");
            string path = $"{PrototypeFolder}/{name}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null && existing.TryGetComponent(out MeshFilter emf) && emf.sharedMesh == smf.sharedMesh &&
                existing.TryGetComponent(out MeshRenderer emr) && emr.sharedMaterials.SequenceEqual(smr.sharedMaterials) &&
                emr.shadowCastingMode == smr.shadowCastingMode && existing.layer == src.layer)
                return existing;
            var go = new GameObject(name) { layer = src.layer };
            try
            {
                go.AddComponent<MeshFilter>().sharedMesh = smf.sharedMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = smr.sharedMaterials;
                mr.shadowCastingMode = smr.shadowCastingMode;
                mr.receiveShadows = smr.receiveShadows;
                return PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally { Object.DestroyImmediate(go); }
        }

        static int CountDetails(TerrainData td)
        {
            if (td.detailPrototypes == null || td.detailPrototypes.Length == 0) return 0;
            int n = 0;
            for (int i = 0; i < td.detailPrototypes.Length; i++)
                foreach (int v in td.GetDetailLayer(0, 0, td.detailWidth, td.detailHeight, i)) n += v;
            return n;
        }

        // ------------------------------------------------------------------ helpers
        static float Median(List<float> l)
        {
            if (l.Count == 0) return 0f;
            l.Sort();
            return l[l.Count / 2];
        }

        static float P(List<float> sorted, float q) => sorted.Count == 0 ? 0f : sorted[Mathf.Clamp(Mathf.RoundToInt(q * (sorted.Count - 1)), 0, sorted.Count - 1)];

        static string Fmt(Ctx ctx, Color c) => $"({c.r.ToString("0.0000", ctx.ci)}, {c.g.ToString("0.0000", ctx.ci)}, {c.b.ToString("0.0000", ctx.ci)})";
    }
}
