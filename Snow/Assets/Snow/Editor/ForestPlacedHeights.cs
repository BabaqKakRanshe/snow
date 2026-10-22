using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Snow.EditorTools
{
    /// <summary>
    /// Heights the checkpoint move (<see cref="ForestRelocationStep"/>) and the yard plan (<see cref="ForestYardStep"/>) found
    /// on their first run. Both level the ground under what they place, so a later run would measure ground they levelled
    /// themselves; their scene component remembers the first heights, but a Location Builder rebuild makes a new scene, and the
    /// gate sank ~10 cm, the barrier ~4 cm on every rebuild (found 22.10 with Forest update 7, user: fix). This file keeps them
    /// across rebuilds; seeded with the heights of 22.10, which update 7's placements are computed for.
    /// </summary>
    public static class ForestPlacedHeights
    {
        public const string Path = "Assets/Snow/Levels/Forest/ForestPlacedHeights.json";

        [Serializable]
        public class Data
        {
            public Vector3 newPivot;                                    // the barrier after the move (y = its height)
            public Vector3[] yardFrom = new Vector3[0], yardTo = new Vector3[0];
        }

        public static Data Load() => File.Exists(Path) ? JsonUtility.FromJson<Data>(File.ReadAllText(Path)) ?? new Data() : new Data();

        public static void Save(Data d)
        {
            File.WriteAllText(Path, JsonUtility.ToJson(d, true));
            AssetDatabase.ImportAsset(Path);
        }

        /// <summary>The first run's height of the yard structure whose data place is <paramref name="orig"/>, if known.</summary>
        public static bool YardHeight(Data d, Vector3 orig, out float y)
        {
            for (int k = 0; d.yardFrom != null && k < d.yardFrom.Length && k < d.yardTo.Length; k++)
                if (new Vector2(d.yardFrom[k].x - orig.x, d.yardFrom[k].z - orig.z).magnitude < 0.05f) { y = d.yardTo[k].y; return true; }
            y = 0f;
            return false;
        }
    }
}
