using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Snow.Levels.Forest
{
    /// <summary>
    /// Draws the Forest's Terrain trees with GPU instancing (the free replacement for GPU Instancer, user decision 21.10). Unity's
    /// own tree rendering issues one draw per tree, material and shadow cascade — 100 000 trees made 140 000 draws. Here the
    /// tree instances of every tile are bucketed into 32 m cells once; each frame the cells near the camera and inside its
    /// frustum are gathered, every tree picks its LOD by distance (the prototype's LODGroup heights) and the instances go out
    /// with Graphics.RenderMeshInstanced, one call per mesh, submesh and LOD (up to 1023 instances). Trees beyond
    /// <see cref="drawDistance"/> are skipped (the fog hides them), shadows only within <see cref="shadowDistance"/>. The terrain
    /// keeps the trees as data — Paint Trees, the tree colliders and the edit-mode view are unchanged; in Play the terrain's own
    /// tree drawing is switched off. Local, nothing goes over the network; no allocations per frame.
    /// </summary>
    public class ForestTreeRenderer : MonoBehaviour
    {
        [Tooltip("Terrain tiles whose trees are drawn here")] public Terrain[] terrains = new Terrain[0];
        [Tooltip("m: trees farther from the camera are not drawn (the fog ends at 70 m)")] public float drawDistance = 85f;
        [Tooltip("m: trees cast shadows only this close (the moon's shadow distance is 50 m)")] public float shadowDistance = 35f;
        [Tooltip("Rendering layer mask of the trees: the map's defaultMask (2), lit by the yard lamps")] public uint renderingLayers = 2;
        [Tooltip("Scales the LOD distances (1 = as the prefabs' LODGroups)")] public float lodBias = 1f;

        const float CellSize = 32f;
        const int Batch = 1023;

        sealed class Lod
        {
            public Mesh mesh;
            public Material[] materials;
            public bool shadows;
            public float maxDist;                                      // m at scale 1
            public Matrix4x4 local = Matrix4x4.identity;               // the mesh's place in the prefab (tilted logs)
            public bool hasLocal;
            public readonly Matrix4x4[] near = new Matrix4x4[Batch], far = new Matrix4x4[Batch];
            public int nearCount, farCount;
        }

        sealed class Proto { public Lod[] lods; }

        struct Cell { public Vector3 centre; public Bounds bounds; public int start, count; }

        Proto[] _protos;
        Matrix4x4[] _matrix;                                           // all instances, grouped by cell
        int[] _protoOf;
        float[] _scale;
        Vector3[] _pos;
        Cell[] _cells;
        readonly Plane[] _planes = new Plane[6];
        readonly List<float> _savedTreeDistance = new List<float>();
        RenderParams[] _params;

        void Awake() => Build();

        void OnEnable()
        {
            _savedTreeDistance.Clear();
            foreach (Terrain t in terrains)
            {
                _savedTreeDistance.Add(t != null ? t.treeDistance : 0f);
                if (t != null) t.treeDistance = 0f;                    // the terrain keeps colliders and data, stops drawing trees
            }
        }

        void OnDisable()
        {
            for (int i = 0; i < terrains.Length && i < _savedTreeDistance.Count; i++)
                if (terrains[i] != null) terrains[i].treeDistance = _savedTreeDistance[i];
        }

        void Build()
        {
            var protoIndex = new Dictionary<GameObject, int>();
            var protos = new List<Proto>();
            var items = new List<(int proto, Matrix4x4 m, Vector3 p, float s)>();
            foreach (Terrain t in terrains)
            {
                if (t == null || t.terrainData == null) continue;
                TerrainData td = t.terrainData;
                TreePrototype[] tp = td.treePrototypes;
                var local = new int[tp.Length];
                for (int i = 0; i < tp.Length; i++)
                {
                    GameObject prefab = tp[i].prefab;
                    if (prefab == null) { local[i] = -1; continue; }
                    if (!protoIndex.TryGetValue(prefab, out int pi)) { pi = protos.Count; protoIndex[prefab] = pi; protos.Add(MakeProto(prefab)); }
                    local[i] = pi;
                }
                Vector3 o = t.transform.position, size = td.size;
                foreach (TreeInstance ti in td.treeInstances)
                {
                    int pi = ti.prototypeIndex >= 0 && ti.prototypeIndex < local.Length ? local[ti.prototypeIndex] : -1;
                    if (pi < 0 || protos[pi].lods.Length == 0) continue;
                    Vector3 p = o + Vector3.Scale(ti.position, size);
                    var m = Matrix4x4.TRS(p, Quaternion.Euler(0f, ti.rotation * Mathf.Rad2Deg, 0f), new Vector3(ti.widthScale, ti.heightScale, ti.widthScale));
                    items.Add((pi, m, p, Mathf.Max(ti.widthScale, ti.heightScale)));
                }
            }
            _protos = protos.ToArray();

            // bucket by 32 m cell
            var byCell = new Dictionary<long, List<int>>();
            for (int i = 0; i < items.Count; i++)
            {
                long key = ((long)Mathf.FloorToInt(items[i].p.x / CellSize) << 32) ^ (uint)Mathf.FloorToInt(items[i].p.z / CellSize);
                if (!byCell.TryGetValue(key, out List<int> l)) byCell[key] = l = new List<int>();
                l.Add(i);
            }
            _matrix = new Matrix4x4[items.Count];
            _protoOf = new int[items.Count];
            _scale = new float[items.Count];
            _pos = new Vector3[items.Count];
            _cells = new Cell[byCell.Count];
            int n = 0, c = 0;
            foreach (List<int> l in byCell.Values)
            {
                var b = new Bounds(items[l[0]].p, Vector3.zero);
                int start = n;
                foreach (int i in l)
                {
                    _matrix[n] = items[i].m;
                    _protoOf[n] = items[i].proto;
                    _scale[n] = items[i].s;
                    _pos[n] = items[i].p;
                    b.Encapsulate(items[i].p);
                    n++;
                }
                b.Expand(new Vector3(16f, 70f, 16f));                  // crowns: up to ~35 m tall trees at scale 1.3
                b.center += Vector3.up * 30f;
                _cells[c++] = new Cell { centre = b.center, bounds = b, start = start, count = l.Count };
            }
            _params = new RenderParams[2];
        }

        /// <summary>LODs of a tree prefab: the LODGroup's renderers (one mesh each) with distances from its screen heights at
        /// 60° vertical FOV; a prefab without LODGroup is one LOD to the draw distance.</summary>
        Proto MakeProto(GameObject prefab)
        {
            var lods = new List<Lod>();
            if (prefab.TryGetComponent(out LODGroup group))
            {
                LOD[] gl = group.GetLODs();
                float size = group.size, k = 2f * Mathf.Tan(30f * Mathf.Deg2Rad);
                for (int i = 0; i < gl.Length; i++)
                {
                    Renderer r = gl[i].renderers.Length > 0 ? gl[i].renderers[0] : null;
                    if (r == null || !r.TryGetComponent(out MeshFilter mf) || mf.sharedMesh == null) continue;
                    float h = Mathf.Max(gl[i].screenRelativeTransitionHeight, 1e-4f);
                    Matrix4x4 local = prefab.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
                    lods.Add(new Lod { mesh = mf.sharedMesh, materials = r.sharedMaterials, shadows = r.shadowCastingMode != ShadowCastingMode.Off, maxDist = size / (k * h),
                                       local = local, hasLocal = local != Matrix4x4.identity });
                }
            }
            else if (prefab.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null && prefab.TryGetComponent(out MeshRenderer mr))
                lods.Add(new Lod { mesh = mf.sharedMesh, materials = mr.sharedMaterials, shadows = mr.shadowCastingMode != ShadowCastingMode.Off, maxDist = float.MaxValue });
            foreach (Lod l in lods)
                foreach (Material m in l.materials)
                    if (m != null && !m.enableInstancing) m.enableInstancing = true;
            return new Proto { lods = lods.ToArray() };
        }

        void LateUpdate()
        {
            Camera cam = Camera.main;
            if (cam == null || _cells == null) return;
            Vector3 eye = cam.transform.position;
            GeometryUtility.CalculateFrustumPlanes(cam, _planes);
            float reach = drawDistance + CellSize, reach2 = reach * reach, draw2 = drawDistance * drawDistance, shadow2 = shadowDistance * shadowDistance;
            float bias2 = lodBias * lodBias;

            for (int c = 0; c < _cells.Length; c++)
            {
                Cell cell = _cells[c];
                float dx = cell.centre.x - eye.x, dz = cell.centre.z - eye.z;
                if (dx * dx + dz * dz > reach2) continue;
                bool inView = GeometryUtility.TestPlanesAABB(_planes, cell.bounds);
                bool shadowNear = dx * dx + dz * dz < (shadowDistance + CellSize) * (shadowDistance + CellSize);
                if (!inView && !shadowNear) continue;                  // out of view cells still cast shadows into it
                for (int i = cell.start, end = cell.start + cell.count; i < end; i++)
                {
                    Vector3 d = _pos[i] - eye;
                    float d2 = d.sqrMagnitude;
                    if (d2 > draw2) continue;
                    Proto p = _protos[_protoOf[i]];
                    float s2 = _scale[i] * _scale[i] * bias2;
                    Lod lod = null;
                    foreach (Lod l in p.lods) { if (d2 <= l.maxDist * l.maxDist * s2) { lod = l; break; } }
                    if (lod == null) continue;                         // culled by its LODGroup
                    bool castShadow = lod.shadows && d2 < shadow2;
                    if (!inView && !castShadow) continue;
                    Matrix4x4 m = lod.hasLocal ? _matrix[i] * lod.local : _matrix[i];
                    if (castShadow) { lod.near[lod.nearCount++] = m; if (lod.nearCount == Batch) Flush(lod, true); }
                    else { lod.far[lod.farCount++] = m; if (lod.farCount == Batch) Flush(lod, false); }
                }
            }
            foreach (Proto p in _protos)
                foreach (Lod l in p.lods)
                {
                    if (l.nearCount > 0) Flush(l, true);
                    if (l.farCount > 0) Flush(l, false);
                }
        }

        void Flush(Lod lod, bool shadows)
        {
            int count = shadows ? lod.nearCount : lod.farCount;
            Matrix4x4[] m = shadows ? lod.near : lod.far;
            for (int sub = 0; sub < lod.mesh.subMeshCount && sub < lod.materials.Length; sub++)
            {
                Material mat = lod.materials[sub];
                if (mat == null) continue;
                var rp = new RenderParams(mat)
                {
                    shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                    receiveShadows = true,
                    layer = gameObject.layer,
                    renderingLayerMask = renderingLayers,
                    lightProbeUsage = LightProbeUsage.Off,
                    reflectionProbeUsage = ReflectionProbeUsage.BlendProbes,
                    worldBounds = new Bounds(transform.position, new Vector3(4000f, 1000f, 4000f)),
                };
                Graphics.RenderMeshInstanced(rp, lod.mesh, sub, m, count);
            }
            if (shadows) lod.nearCount = 0; else lod.farCount = 0;
        }
    }
}
