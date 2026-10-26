using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Snow.EditorTools
{
    /// <summary>
    /// Sky Island, the sky_systems pack (SKY_CONTRACT.md; prefabs: <see cref="PackPropsBuilder"/>) placed roughly in the
    /// Sky Island scene from <see cref="DataPath"/>: the altitude board by the control tower facing the field, the ballast
    /// release on the island's edge with its drop outward, the refrigeration unit and the dust sprayer on level ground by the
    /// buildings. The walkway modules stay prefabs. A prop with <c>edge</c> walks along its front (+Z) to the island's edge;
    /// one with <c>search</c> looks for a free level spot within that many metres. Only the root <see cref="RootName"/>
    /// changes; <see cref="SkyIslandLevelBuilder"/> runs this before it saves the scene, and Tools / Snow / Sky Island Systems
    /// runs it alone (the scene opened next to the current one, saved and closed).
    /// </summary>
    public static class SkySystemsStep
    {
        public const string DataPath = "Assets/SkyIsland/Systems/SkySystems_Place.json";
        public const string RootName = "SkySystems";

        [Serializable] public class Prop { public string prefab; public float x, z, yaw, search; public bool edge; }
        [Serializable] public class Data { public string note; public Prop[] props; }

        [MenuItem("Tools/Snow/Sky Island Systems")]
        static void Menu()
        {
            Scene current = SceneManager.GetActiveScene();
            bool open = current.path == SkyIslandLevelBuilder.ScenePath;
            Scene scene = open ? current : EditorSceneManager.OpenScene(SkyIslandLevelBuilder.ScenePath, OpenSceneMode.Additive);
            Run(scene);
            EditorSceneManager.SaveScene(scene);
            if (!open) EditorSceneManager.CloseScene(scene, true);
        }

        public static void Run(Scene scene)
        {
            if (!File.Exists(DataPath)) { Debug.LogWarning($"[Snow] Sky systems: {DataPath} not found."); return; }
            var d = JsonUtility.FromJson<Data>(File.ReadAllText(DataPath));
            foreach (GameObject g in scene.GetRootGameObjects().Where(g => g.name == RootName)) UnityEngine.Object.DestroyImmediate(g);
            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            Physics.SyncTransforms();
            var lines = new System.Collections.Generic.List<string>();
            foreach (Prop p in d.props)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(p.prefab);
                if (asset == null) { lines.Add($"{p.prefab} MISSING"); continue; }
                Bounds fp = Footprint(asset);
                Quaternion turn = Quaternion.Euler(0f, p.yaw, 0f);
                Vector3 at = new Vector3(p.x, 0f, p.z);
                string how = "as given";
                if (p.edge)
                {
                    Vector3 fwd = turn * Vector3.forward, last = at;
                    for (float s = 0f; s < 400f; s += 0.5f)
                    {
                        Vector3 q = at + fwd * s;
                        if (!Ground(scene, q, true, out _)) break;
                        last = q;
                    }
                    at = last - fwd * 0.3f;
                    how = "on the edge";
                }
                else if (p.search > 0f)
                {
                    bool found = false;
                    for (float r = 0f; r <= p.search && !found; r += 2f)
                        for (int k = 0; k < Mathf.Max(1, (int)(r * 1.5f)) && !found; k++)
                        {
                            float a = k * Mathf.PI * 2f / Mathf.Max(1, (int)(r * 1.5f));
                            Vector3 q = at + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                            if (Free(scene, q, turn, fp)) { at = q; found = true; how = r > 0f ? $"moved {r:0} m to a free level spot" : "free"; }
                        }
                    if (!found) how = $"NO free level spot within {p.search} m: left as given";
                }
                Ground(scene, at, false, out float y);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, root.transform);
                go.transform.SetPositionAndRotation(new Vector3(at.x, y, at.z), turn);
                lines.Add($"{asset.name} at ({at.x:0.0}; {y:0.00}; {at.z:0.0}) yaw {p.yaw:0}, {how}");
            }
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[Snow] Sky systems: " + string.Join("; ", lines) + ".");
        }

        static bool InScene(Collider c, Scene scene) => c.gameObject.scene == scene && !c.isTrigger && c.transform.root.name != RootName;

        static bool IsGround(Collider c) => c.transform.root.name.StartsWith("SkyIsland", StringComparison.Ordinal) &&
                                            (c.name.StartsWith("Terrain_", StringComparison.Ordinal) || c.name.StartsWith("Runway_", StringComparison.Ordinal) || c.name.StartsWith("Pavement_", StringComparison.Ordinal));

        /// <summary>The ground (terrain, runway, pavement of the island) under a point; only the island's own colliders.</summary>
        static bool Ground(Scene scene, Vector3 at, bool groundOnly, out float y)
        {
            y = 0f;
            RaycastHit[] hits = Physics.RaycastAll(new Vector3(at.x, 400f, at.z), Vector3.down, 800f, ~0, QueryTriggerInteraction.Ignore)
                .Where(h => InScene(h.collider, scene) && (!groundOnly || IsGround(h.collider))).OrderBy(h => h.distance).ToArray();
            RaycastHit[] ground = hits.Where(h => IsGround(h.collider)).ToArray();
            if (ground.Length == 0) return false;
            y = ground[0].point.y;
            return true;
        }

        static bool Free(Scene scene, Vector3 at, Quaternion turn, Bounds fp)
        {
            var h = new float[5];
            Vector3 c = new Vector3(fp.center.x, 0f, fp.center.z), e = new Vector3(fp.extents.x, 0f, fp.extents.z);
            Vector3[] pts = { c, c + new Vector3(-e.x, 0, -e.z), c + new Vector3(e.x, 0, -e.z), c + new Vector3(-e.x, 0, e.z), c + new Vector3(e.x, 0, e.z) };
            for (int i = 0; i < 5; i++)
                if (!Ground(scene, at + turn * pts[i], true, out h[i])) return false;
            if (h.Max() - h.Min() > 0.6f) return false;
            Vector3 centre = at + turn * c + Vector3.up * (h[0] + fp.size.y * 0.5f + 0.3f);
            return !Physics.OverlapBox(centre, new Vector3(e.x + 1.5f, fp.size.y * 0.5f, e.z + 1.5f), turn, ~0, QueryTriggerInteraction.Ignore)
                .Any(col => InScene(col, scene) && !IsGround(col));
        }

        static Bounds Footprint(GameObject asset)
        {
            var b = new Bounds();
            bool any = false;
            foreach (Renderer r in asset.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer) || !r.TryGetComponent(out MeshFilter mf) || mf.sharedMesh == null) continue;
                Bounds mb = mf.sharedMesh.bounds;
                Matrix4x4 m = asset.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 p = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            return b;
        }
    }
}
