using Snow.Net;
using UnityEngine;

namespace Snow.Levels.Forest
{
    /// <summary>
    /// Canopy cover under the Forest's trees (22.10, solution 3: «под ёлками так же светло, как в поле»). Ambient is one
    /// trilight for the whole map and the trees' shadows are drawn only within ~35 m, so snow under the forest was as bright as
    /// a clearing. Once at start the Terrain trees tall enough to be trees (rocks, stumps and logs are Terrain trees too) are
    /// splatted into a cover map (2 m texels over every tile, crown = a share of the tree's height, blurred a little for the
    /// light leaking under the edge); Snow/Terrain/Lit Macro reads it as globals: ambient on the ground goes down with the
    /// cover, and beyond the shadow distance the cover also takes the moon / sun off the ground (it stands in for the shadows
    /// that are not drawn there). The clock sets the strength by the hour (<see cref="SetStrength"/>). Local, nothing goes over
    /// the network; only the terrain uses it (the trees are dark themselves, props are few).
    /// </summary>
    public class ForestCanopy : MonoBehaviour
    {
        [Tooltip("m per texel of the cover map")] public float texel = 2f;
        [Tooltip("m: Terrain tree prototypes shorter than this (at their instance scale) are not canopy")] public float minTreeHeight = 4f;
        [Tooltip("Crown radius as a share of the tree's height (spruce, pine)")] public float crownRadius = 0.2f;
        [Tooltip("Cover one crown gives at its centre")] [Range(0f, 1f)] public float crownCover = 0.75f;
        [Tooltip("Blur radius, texels")] public int blur = 2;
        [Tooltip("m from the camera: the cover starts standing in for the trees' shadows here, fully after the second value " +
                 "(ForestTreeRenderer draws tree shadows within 35 m)")]
        public Vector2 shadowFade = new Vector2(25f, 15f);

        static readonly int TexId = Shader.PropertyToID("_SnowCanopyTex"), RectId = Shader.PropertyToID("_SnowCanopyRect"),
                            ParamsId = Shader.PropertyToID("_SnowCanopyParams"), FadeId = Shader.PropertyToID("_SnowCanopyFade");

        Texture2D _tex;
        byte[] _cover;
        int _w, _h;
        Vector2 _origin;

        /// <summary>Trees counted as canopy and the mean cover over the map (bench, tests).</summary>
        public int TreeCount { get; private set; }
        public float MeanCover { get; private set; }

        void Start() => Build();

        void OnDestroy()
        {
            Shader.SetGlobalVector(ParamsId, Vector4.zero);
            if (_tex != null) Destroy(_tex);
        }

        /// <summary>ambient: multiplier on the ground's ambient under full cover; direct: share of the main light the cover takes
        /// off the ground beyond the shadow distance.</summary>
        public void SetStrength(float ambient, float direct) =>
            Shader.SetGlobalVector(ParamsId, _tex != null ? new Vector4(1f, ambient, direct, 0f) : Vector4.zero);

        /// <summary>Cover 0..1 at a world position (0 outside the map).</summary>
        public float CoverAt(Vector3 p)
        {
            if (_cover == null) return 0f;
            int x = Mathf.FloorToInt((p.x - _origin.x) / texel), z = Mathf.FloorToInt((p.z - _origin.y) / texel);
            return x < 0 || z < 0 || x >= _w || z >= _h ? 0f : _cover[z * _w + x] / 255f;
        }

        void Build()
        {
            Terrain[] terrains = Terrain.activeTerrains;
            if (terrains.Length == 0) return;
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (Terrain t in terrains)
            {
                if (t.terrainData == null) continue;
                Vector3 o = t.GetPosition(), s = t.terrainData.size;
                minX = Mathf.Min(minX, o.x); minZ = Mathf.Min(minZ, o.z);
                maxX = Mathf.Max(maxX, o.x + s.x); maxZ = Mathf.Max(maxZ, o.z + s.z);
            }
            if (minX >= maxX) return;
            texel = Mathf.Max(0.5f, texel);
            _origin = new Vector2(minX, minZ);
            _w = Mathf.CeilToInt((maxX - minX) / texel);
            _h = Mathf.CeilToInt((maxZ - minZ) / texel);
            var open = new float[_w * _h];                             // share of the sky still open, multiplied crown by crown
            for (int i = 0; i < open.Length; i++) open[i] = 1f;

            int count = 0;
            foreach (Terrain t in terrains)
            {
                TerrainData td = t.terrainData;
                if (td == null) continue;
                TreePrototype[] tp = td.treePrototypes;
                var height = new float[tp.Length];
                for (int i = 0; i < tp.Length; i++) height[i] = PrefabHeight(tp[i].prefab);
                Vector3 o = t.GetPosition(), size = td.size;
                foreach (TreeInstance ti in td.treeInstances)
                {
                    if (ti.prototypeIndex < 0 || ti.prototypeIndex >= height.Length) continue;
                    float h = height[ti.prototypeIndex] * ti.heightScale;
                    if (h < minTreeHeight) continue;
                    float r = height[ti.prototypeIndex] * crownRadius * ti.widthScale;
                    Vector3 p = o + Vector3.Scale(ti.position, size);
                    Splat((p.x - _origin.x) / texel, (p.z - _origin.y) / texel, r / texel, open);
                    count++;
                }
            }

            var cover = new float[open.Length];
            for (int i = 0; i < open.Length; i++) cover[i] = 1f - open[i];
            if (blur > 0) { BoxBlur(cover, _w, _h, blur, true); BoxBlur(cover, _w, _h, blur, false); }
            _cover = new byte[cover.Length];
            double sum = 0d;
            for (int i = 0; i < cover.Length; i++) { _cover[i] = (byte)Mathf.RoundToInt(Mathf.Clamp01(cover[i]) * 255f); sum += cover[i]; }
            TreeCount = count;
            MeanCover = (float)(sum / cover.Length);

            _tex = new Texture2D(_w, _h, TextureFormat.R8, false, true)
            {
                name = "ForestCanopy (runtime)", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave
            };
            _tex.SetPixelData(_cover, 0);
            _tex.Apply(false, true);
            Shader.SetGlobalTexture(TexId, _tex);
            Shader.SetGlobalVector(RectId, new Vector4(_origin.x, _origin.y, 1f / (_w * texel), 1f / (_h * texel)));
            Shader.SetGlobalVector(FadeId, new Vector4(shadowFade.x, 1f / Mathf.Max(shadowFade.y, 0.01f), 0f, 0f));
            BenchLog.Event("canopy", "built", count, "size", _w + "x" + _h, "mean_cover", MeanCover);
        }

        /// <summary>One crown: cover falls off from the centre to the rim; overlapping crowns close the sky multiplicatively.</summary>
        void Splat(float cx, float cz, float r, float[] open)
        {
            r = Mathf.Max(r, 0.5f);
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - r)), x1 = Mathf.Min(_w - 1, Mathf.CeilToInt(cx + r));
            int z0 = Mathf.Max(0, Mathf.FloorToInt(cz - r)), z1 = Mathf.Min(_h - 1, Mathf.CeilToInt(cz + r));
            float r2 = r * r;
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x + 0.5f - cx, dz = z + 0.5f - cz, d2 = dx * dx + dz * dz;
                    if (d2 >= r2) continue;
                    float k = 1f - d2 / r2;
                    open[z * _w + x] *= 1f - crownCover * k;
                }
        }

        static void BoxBlur(float[] a, int w, int h, int r, bool horizontal)
        {
            var src = (float[])a.Clone();
            int n = horizontal ? w : h, lines = horizontal ? h : w;
            for (int l = 0; l < lines; l++)
            {
                float sum = 0f;
                int count = 0;
                for (int i = -r; i <= r; i++)
                    if (i >= 0 && i < n) { sum += src[horizontal ? l * w + i : i * w + l]; count++; }
                for (int i = 0; i < n; i++)
                {
                    a[horizontal ? l * w + i : i * w + l] = sum / count;
                    int add = i + r + 1, rem = i - r;
                    if (add < n) { sum += src[horizontal ? l * w + add : add * w + l]; count++; }
                    if (rem >= 0) { sum -= src[horizontal ? l * w + rem : rem * w + l]; count--; }
                }
            }
        }

        /// <summary>m: top of the prefab's meshes above its pivot (every LOD; 0 without meshes).</summary>
        static float PrefabHeight(GameObject prefab)
        {
            if (prefab == null) return 0f;
            float top = 0f;
            Vector3 pivot = prefab.transform.position;
            foreach (MeshFilter mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Bounds b = mf.sharedMesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1f : 1f, (c & 2) == 0 ? -1f : 1f, (c & 4) == 0 ? -1f : 1f));
                    top = Mathf.Max(top, mf.transform.TransformPoint(corner).y - pivot.y);
                }
            }
            return top;
        }
    }
}
