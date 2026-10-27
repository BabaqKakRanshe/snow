using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Snow.EditorTools
{
    /// <summary>
    /// Sub-step "Unity Terrain" of Tools / Snow / Integrate Location Level / Forest, run last in the Forest step through
    /// <see cref="ForestTerrainHook"/>: the 3 x 3 terrain tiles baked by ForestTerrainBaker under the Forest root
    /// (Forest_UnityTerrain_&lt;x&gt;_&lt;z&gt;, neighbours connected, one grouping id so brushes cross tiles), and the scene cleaned
    /// of what the terrain now carries — the ground mesh (unpacked, only Forest_Marker stays), the Vegetation group, and the
    /// rocks, stumps, snags, branches and pebbles of the Rocks group (logs, fences and posts stay objects). GPU instancing on
    /// the prototypes' materials (the core rewrites materials on every rebuild); missing prototypes re-bound by path; the
    /// map's reflection probes re-baked so they see the terrain (probes only, no GI). Never bakes: once the user brushes the
    /// tiles, the assets are the source of truth. Without them the scene stays on the mesh ground. Running it again changes
    /// nothing that is already in place.
    /// </summary>
    [InitializeOnLoad]
    public static class ForestTerrainStep
    {
        const string TerrainPrefix = "Forest_UnityTerrain";
        // the look's fog ends at 70 m (21.10): nothing beyond ~110 m reaches the screen
        const float PixelError = 3f, BasemapDistance = 110f, TreeDistance = 120f, DetailDistance = 35f, DetailDensity = 1f;
        const int GroupingId = 21;
        static readonly string[] MeshGround = { "Forest_Road", "Forest_BasePaved" };

        // the light cones (K6.4) and the grade volume ride the same hook: installed whether or not a terrain is baked
        static ForestTerrainStep() => ForestTerrainHook.run = (scene, root) => { WorldSync.Begin(root); Run(scene, root); ForestRelocationStep.Run(root); ForestYardStep.Run(root); WorldSync.LayoutPlaced(root); ForestYardDressStep.Run(root); ForestStartDressStep.Run(root); ForestHutDressStep.Run(root); ForestPacksStep.Run(root); ForestBunkerStep.Run(root); ForestStreamsStep.Run(root); ForestAirfieldStep.Run(root); ForestGiantsStep.Run(root); ForestViewpointsStep.Run(root); ForestBeamsStep.Run(root); ForestGradeStep.Run(root); ForestExtrasStep.Run(root); ForestDayNightStep.Run(root); ForestKppStep.Link(root); WorldSync.Finish(root); };

        public static void Run(Scene scene, Transform root)
        {
            ForestTerrainBaker.BakeMeta meta = ForestTerrainBaker.LoadMeta();
            var tiles = new TerrainData[ForestTerrainBaker.TilesX, ForestTerrainBaker.TilesZ];
            bool all = meta != null && meta.tiles != null;
            for (int ix = 0; all && ix < ForestTerrainBaker.TilesX; ix++)
                for (int iz = 0; all && iz < ForestTerrainBaker.TilesZ; iz++)
                    all = (tiles[ix, iz] = AssetDatabase.LoadAssetAtPath<TerrainData>(ForestTerrainBaker.TilePath(ix, iz))) != null;
            if (!all)
            {
                Debug.LogWarning("[Snow] Forest terrain: the terrain tiles are not baked (Tools / Snow / Forest / Bake Unity Terrain); the scene stays on the mesh ground.");
                return;
            }
            int rebound = Rebind(tiles, meta);

            // the map's layer, tag, static flags and rendering layer mask, from a ground tile (or the marker once the tiles are gone)
            Transform ground = root.Find("World/Forest_Terrain");
            MeshRenderer reference = ground != null ? ground.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault() : null;
            int layer = reference != null ? reference.gameObject.layer : 0;
            string tag = reference != null ? reference.gameObject.tag : "Untagged";
            StaticEditorFlags flags = reference != null ? GameObjectUtility.GetStaticEditorFlags(reference.gameObject)
                : StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
            uint renderingLayers = reference != null ? reference.renderingLayerMask : 2u;

            // 1. the terrain tiles (one object per tile; the single terrain of the first bake goes)
            foreach (Terrain t in root.GetComponentsInChildren<Terrain>(true).ToArray())
                if (t.name == TerrainPrefix) Object.DestroyImmediate(t.gameObject);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(ForestTerrainBaker.Folder + "/M_Forest_TerrainLit.mat");
            var terrains = new Terrain[ForestTerrainBaker.TilesX, ForestTerrainBaker.TilesZ];
            for (int ix = 0; ix < ForestTerrainBaker.TilesX; ix++)
                for (int iz = 0; iz < ForestTerrainBaker.TilesZ; iz++)
                {
                    string name = $"{TerrainPrefix}_{ix}_{iz}";
                    Transform existing = root.Find(name);
                    GameObject go = existing != null ? existing.gameObject : new GameObject(name);
                    go.transform.SetParent(root, false);
                    go.transform.position = ForestTerrainBaker.TileOrigin(ix, iz, meta.originY);
                    go.transform.rotation = Quaternion.identity;
                    go.transform.localScale = Vector3.one;
                    go.layer = layer;
                    go.tag = tag;
                    GameObjectUtility.SetStaticEditorFlags(go, flags);
                    if (!go.TryGetComponent(out Terrain terrain)) terrain = go.AddComponent<Terrain>();
                    if (!go.TryGetComponent(out TerrainCollider col)) col = go.AddComponent<TerrainCollider>();
                    terrain.terrainData = tiles[ix, iz];
                    col.terrainData = tiles[ix, iz];
                    var colSo = new SerializedObject(col);
                    SerializedProperty treesProp = colSo.FindProperty("m_EnableTreeColliders");
                    if (treesProp != null) { treesProp.boolValue = true; colSo.ApplyModifiedPropertiesWithoutUndo(); }
                    terrain.materialTemplate = mat;
                    terrain.drawInstanced = true;
                    terrain.heightmapPixelError = PixelError;
                    terrain.basemapDistance = BasemapDistance;
                    terrain.shadowCastingMode = ShadowCastingMode.On;
                    terrain.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                    terrain.treeDistance = TreeDistance;
                    terrain.treeBillboardDistance = TreeDistance;      // LOD-group trees: their own LODs, no billboards
                    terrain.detailObjectDistance = DetailDistance;
                    terrain.detailObjectDensity = DetailDensity;
                    terrain.renderingLayerMask = renderingLayers;      // the map's defaultMask (2): the yard lamps light it
                    terrain.groupingID = GroupingId;
                    terrain.allowAutoConnect = true;
                    EditorUtility.SetDirty(terrain);
                    EditorUtility.SetDirty(col);
                    terrains[ix, iz] = terrain;
                }
            for (int ix = 0; ix < ForestTerrainBaker.TilesX; ix++)
                for (int iz = 0; iz < ForestTerrainBaker.TilesZ; iz++)
                    terrains[ix, iz].SetNeighbors(ix > 0 ? terrains[ix - 1, iz] : null, iz < ForestTerrainBaker.TilesZ - 1 ? terrains[ix, iz + 1] : null,
                                                  ix < ForestTerrainBaker.TilesX - 1 ? terrains[ix + 1, iz] : null, iz > 0 ? terrains[ix, iz - 1] : null);

            // instanced tree drawing in Play (Unity's terrain trees cost one draw per tree, material and cascade)
            if (!root.TryGetComponent(out Snow.Levels.Forest.ForestTreeRenderer treeRenderer)) treeRenderer = root.gameObject.AddComponent<Snow.Levels.Forest.ForestTreeRenderer>();
            treeRenderer.terrains = terrains.Cast<Terrain>().ToArray();
            treeRenderer.renderingLayers = renderingLayers;
            treeRenderer.drawDistance = 85f;
            treeRenderer.shadowDistance = 35f;
            treeRenderer.lodBias = 0.6f;
            EditorUtility.SetDirty(treeRenderer);

            // 2. the ground mesh goes from the scene (the model instance unpacked; the marker stays for the core's check)
            int meshGone = 0;
            if (ground != null)
            {
                if (PrefabUtility.IsPartOfPrefabInstance(ground.gameObject))
                    PrefabUtility.UnpackPrefabInstance(PrefabUtility.GetOutermostPrefabInstanceRoot(ground.gameObject), PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                for (int i = ground.childCount - 1; i >= 0; i--)
                {
                    Transform c = ground.GetChild(i);
                    if (c.name == "Forest_Marker") continue;
                    if (!c.name.StartsWith("Terrain_", System.StringComparison.Ordinal) && !MeshGround.Contains(c.name)) continue;
                    Object.DestroyImmediate(c.gameObject);
                    meshGone++;
                }
            }

            // 3. what the terrain carries now: the Vegetation group, rocks / stumps / snags / branches / pebbles of the Rocks group
            int vegGone = 0, rocksGone = 0;
            Transform veg = root.Find("Vegetation");
            if (veg != null) { vegGone = veg.childCount; Object.DestroyImmediate(veg.gameObject); }
            Transform rocks = root.Find("Rocks");
            if (rocks != null)
                for (int i = rocks.childCount - 1; i >= 0; i--)
                {
                    Transform c = rocks.GetChild(i);
                    if (!ForestTerrainBaker.ConvertedRocks.Any(p => c.name.StartsWith(p, System.StringComparison.Ordinal))) continue;
                    if (i % 2000 == 0) EditorUtility.DisplayProgressBar("Integrate Forest", $"Rocks {i}", 0.97f);
                    Object.DestroyImmediate(c.gameObject);
                    rocksGone++;
                }
            if (rocks != null && rocks.childCount == 0) Object.DestroyImmediate(rocks.gameObject);   // everything is on the terrain now

            // 4. GPU instancing on every material of the prototypes (K4.3)
            var mats = new HashSet<Material>();
            foreach (TerrainData td in tiles)
            {
                foreach (TreePrototype p in td.treePrototypes)
                    if (p.prefab != null) foreach (Renderer r in p.prefab.GetComponentsInChildren<Renderer>(true)) mats.UnionWith(r.sharedMaterials);
                foreach (DetailPrototype p in td.detailPrototypes)
                    if (p.prototype != null) foreach (Renderer r in p.prototype.GetComponentsInChildren<Renderer>(true)) mats.UnionWith(r.sharedMaterials);
            }
            int instanced = 0;
            foreach (Material m in mats)
            {
                if (m == null) continue;
                if (!m.enableInstancing) { m.enableInstancing = true; EditorUtility.SetDirty(m); }
                instanced++;
            }
            AssetDatabase.SaveAssets();

            // 5. the map's reflection probes see the terrain now (probe files in Lighting/ belong to the builder)
            int probes = 0;
            Transform probeRoot = root.Find("ReflectionProbes");
            if (probeRoot != null)
                foreach (ReflectionProbe probe in probeRoot.GetComponentsInChildren<ReflectionProbe>(true))
                {
                    if (probe.mode != ReflectionProbeMode.Custom || probe.customBakedTexture == null) continue;
                    string path = AssetDatabase.GetAssetPath(probe.customBakedTexture);
                    EditorUtility.DisplayProgressBar("Integrate Forest", "Reflection probe " + probe.name, 0.98f);
                    if (!Lightmapping.BakeReflectionProbe(probe, path)) { Debug.LogWarning($"[Snow] Forest terrain: probe {probe.name} not baked."); continue; }
                    AssetDatabase.ImportAsset(path);
                    probe.customBakedTexture = AssetDatabase.LoadAssetAtPath<Texture>(path);
                    probes++;
                }

            int trees = 0, details = 0;
            foreach (TerrainData td in tiles)
            {
                trees += td.treeInstanceCount;
                for (int i = 0; i < td.detailPrototypes.Length; i++)
                    foreach (int v in td.GetDetailLayer(0, 0, td.detailWidth, td.detailHeight, i)) details += v;
            }
            Debug.Log($"[Snow] Forest terrain: {ForestTerrainBaker.TilesX}x{ForestTerrainBaker.TilesZ} tiles of {ForestTerrainBaker.Tile:0} m, " +
                      $"{tiles[0, 0].terrainLayers.Length} layers, {trees} trees, {details} details; removed from the scene: {meshGone} ground mesh objects, " +
                      $"vegetation group ({vegGone}), {rocksGone} rocks / stumps / snags / logs / fences / posts / branches / pebbles.");
            Debug.Log($"[Snow] Forest terrain: {instanced} prototype materials instanced, {probes} reflection probes re-baked, " +
                      $"{rebound} prototypes re-bound, rendering layer mask {renderingLayers}, tree / detail distance {TreeDistance:0} / {DetailDistance:0} m.");
        }

        /// <summary>Tree and detail prototypes whose prefab went missing (a rebuild that wrote prefabs with new GUIDs) are bound
        /// again by path; detail prototypes (Terrain/Prototypes, no LODGroup) are refreshed from the builder's prefabs.</summary>
        static int Rebind(TerrainData[,] tiles, ForestTerrainBaker.BakeMeta meta)
        {
            int n = 0;
            foreach (TerrainData td in tiles)
            {
                TreePrototype[] tp = td.treePrototypes;
                bool changed = false;
                for (int i = 0; i < tp.Length && meta.treePrefabs != null && i < meta.treePrefabs.Length; i++)
                {
                    if (tp[i].prefab != null) continue;
                    tp[i].prefab = AssetDatabase.LoadAssetAtPath<GameObject>(meta.treePrefabs[i]);
                    changed = true;
                    n++;
                }
                if (changed) td.treePrototypes = tp;
                DetailPrototype[] dp = td.detailPrototypes;
                changed = false;
                for (int i = 0; i < dp.Length && meta.detailPrefabs != null && i < meta.detailPrefabs.Length; i++)
                {
                    GameObject proto = ForestTerrainBaker.DetailPrototypePrefab(System.IO.Path.GetFileNameWithoutExtension(meta.detailPrefabs[i]));
                    if (dp[i].prototype == proto) continue;
                    dp[i].prototype = proto;
                    changed = true;
                    n++;
                }
                if (changed) td.detailPrototypes = dp;
                if (n > 0) EditorUtility.SetDirty(td);
            }
            if (n > 0) { Debug.LogWarning($"[Snow] Forest terrain: {n} prototype references re-bound."); AssetDatabase.SaveAssets(); }
            return n;
        }
    }
}
