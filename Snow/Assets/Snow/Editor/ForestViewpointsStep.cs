using System;
using System.IO;
using System.Linq;
using Snow.Levels.Forest;
using UnityEditor;
using UnityEngine;

namespace Snow.EditorTools
{
    /// <summary>
    /// Forest integration: the viewpoints (View_*, the F1 teleports and the fly camera's keys) follow what the earlier steps
    /// moved (user 23.10: "объекты сместили, обнови координаты"). The builder puts them at the package's places; here a point
    /// that looks at the checkpoint goes with the booth (<see cref="ForestRelocationStep"/>), a point at a yard structure goes
    /// with that structure, any other point in the yard is drawn in with it (<see cref="ForestYardPlan"/>); points inside the
    /// main building and outside the yard stay. Height keeps its distance above the ground. Computed from the layout every
    /// time, so running it again puts them in the same place.
    /// </summary>
    public static class ForestViewpointsStep
    {
        [Serializable] class View { public string name; public float[] p, target; }
        [Serializable] class Layout { public View[] viewpoints; }

        public static void Run(Transform root)
        {
            var layout = JsonUtility.FromJson<Layout>(File.ReadAllText("Assets/Locations/Forest/Data/Forest_Layout.json"));
            Transform views = root.Find("Viewpoints");
            ForestTerrainBaker.BakeMeta meta = ForestTerrainBaker.LoadMeta();
            if (layout?.viewpoints == null || views == null || meta == null || !root.TryGetComponent(out ForestRelocation rel)) return;
            var tiles = new TerrainData[ForestTerrainBaker.TilesX, ForestTerrainBaker.TilesZ];
            for (int ix = 0; ix < ForestTerrainBaker.TilesX; ix++)
                for (int iz = 0; iz < ForestTerrainBaker.TilesZ; iz++)
                    tiles[ix, iz] = AssetDatabase.LoadAssetAtPath<TerrainData>(ForestTerrainBaker.TilePath(ix, iz));
            Rect building = Building(root);
            Quaternion turn = Quaternion.Euler(0f, rel.yaw, 0f);
            Vector3 OnGround(Vector3 from, Vector3 to) => new Vector3(to.x, from.y + Ground(tiles, meta, to) - Ground(tiles, meta, from), to.z);

            int kpp = 0, yard = 0, drawn = 0;
            foreach (View v in layout.viewpoints)
            {
                Transform t = v?.p != null && v.target != null ? views.Find("View_" + v.name) : null;
                if (t == null) continue;
                Vector3 p = V(v.p), tg = V(v.target), np = p, ntg = tg;
                int s = Structure(rel, p);
                if (building.Contains(new Vector2(p.x, p.z))) { }
                else if (rel.Moves(p) || rel.Moves(tg))
                {
                    np = OnGround(p, rel.newPivot + turn * (p - rel.oldPivot));
                    ntg = OnGround(tg, rel.newPivot + turn * (tg - rel.oldPivot));
                    kpp++;
                }
                else if (s >= 0)
                {
                    Vector3 d = rel.yardTo[s] - rel.yardFrom[s];
                    np = p + d;
                    ntg = tg + d;
                    yard++;
                }
                else if (ForestYardPlan.InYard(p))
                {
                    np = OnGround(p, ForestYardPlan.Map(p));
                    ntg = OnGround(tg, ForestYardPlan.Map(tg));
                    drawn++;
                }
                t.SetPositionAndRotation(np, Quaternion.LookRotation(ntg - np, Vector3.up));
                EditorUtility.SetDirty(t);
            }
            Debug.Log($"[Snow] Forest viewpoints: {kpp} moved with the checkpoint, {yard} with their yard structure, {drawn} drawn in with the yard; the rest stay.");
        }

        static Vector3 V(float[] a) => new Vector3(a[0], a[1], a[2]);

        /// <summary>The moved yard structure a point stands at (the nearest within its radius), or -1 — as <see cref="ForestRelocation"/> does at run time.</summary>
        static int Structure(ForestRelocation rel, Vector3 p)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; rel.yardFrom != null && i < rel.yardFrom.Length && i < rel.yardTo.Length && i < rel.yardRadius.Length; i++)
            {
                float d = new Vector2(p.x - rel.yardFrom[i].x, p.z - rel.yardFrom[i].z).sqrMagnitude;
                if (d <= rel.yardRadius[i] * rel.yardRadius[i] && d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>Plan rectangle of the main building (its renderers), which the yard plan does not move.</summary>
        static Rect Building(Transform root)
        {
            Transform main = root.Find("Structures/BaseMain");
            Renderer[] rs = main != null ? main.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            if (rs.Length == 0) return Rect.zero;
            Bounds b = rs[0].bounds;
            foreach (Renderer r in rs.Skip(1)) b.Encapsulate(r.bounds);
            return new Rect(b.min.x, b.min.z, b.size.x, b.size.z);
        }

        static float Ground(TerrainData[,] tiles, ForestTerrainBaker.BakeMeta meta, Vector3 p)
        {
            int ix = Mathf.FloorToInt((p.x - ForestTerrainBaker.WorldX0) / ForestTerrainBaker.Tile);
            int iz = Mathf.FloorToInt((p.z - ForestTerrainBaker.WorldZ0) / ForestTerrainBaker.Tile);
            if (ix < 0 || iz < 0 || ix >= ForestTerrainBaker.TilesX || iz >= ForestTerrainBaker.TilesZ || tiles[ix, iz] == null) return p.y;
            Vector3 o = ForestTerrainBaker.TileOrigin(ix, iz, meta.originY);
            return meta.originY + tiles[ix, iz].GetInterpolatedHeight((p.x - o.x) / ForestTerrainBaker.Tile, (p.z - o.z) / ForestTerrainBaker.Tile);
        }
    }
}
