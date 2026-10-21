using System.Linq;
using Snow.Levels.Forest;
using UnityEditor;
using UnityEngine;

namespace Snow.EditorTools
{
    /// <summary>
    /// Forest integration, light cones in the fog (FOREST_ASSEMBLY_UNITY.md K6.4): <see cref="ForestLightBeams"/> on the Forest
    /// root with the 6 yard lamps (LightPole) and the 5 antenna lamps (Dish_1…5), Light_power_* of each, and a beam material
    /// on the project's UtilityPlane/Beam shader (the shader stays in the plane package, only a material refers to it; the
    /// glows of the lamps are the core's prefabGlows and stay as they are). Running it again replaces the list, no duplicates.
    /// </summary>
    public static class ForestBeamsStep
    {
        const string MaterialPath = "Assets/Snow/Materials/M_ForestLampBeam.mat";
        /// <summary>Beam brightness per light unit, chosen by eye on 21.10 (TEMPORARY): the yard lamps are only 7 units.</summary>
        const float Gain = 14f;
        static readonly string[] Owners = { "LightPole", "Dish_1", "Dish_2", "Dish_3", "Dish_4", "Dish_5" };

        public static void Run(Transform root)
        {
            Transform structures = root.Find("Structures");
            Light[] lamps = structures == null ? new Light[0] : structures.GetComponentsInChildren<Light>(true)
                .Where(l => l.name.StartsWith("Light_power", System.StringComparison.Ordinal) && Owners.Contains(OwnerOf(l.transform, structures)))
                .OrderBy(l => OwnerOf(l.transform, structures), System.StringComparer.Ordinal).ThenBy(l => l.transform.position.x).ToArray();
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                Shader sh = Shader.Find("UtilityPlane/Beam");
                if (sh == null) { Debug.LogWarning("[Snow] Forest beams: shader UtilityPlane/Beam not found (DEFERRED)."); return; }
                mat = new Material(sh) { name = "M_ForestLampBeam" };
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }
            if (!root.TryGetComponent(out ForestLightBeams beams)) beams = root.gameObject.AddComponent<ForestLightBeams>();
            beams.lamps = lamps;
            beams.beamMaterial = mat;
            beams.gain = Gain;
            EditorUtility.SetDirty(beams);
            int poles = lamps.Count(l => OwnerOf(l.transform, structures) == "LightPole");
            Debug.Log($"[Snow] Forest beams: {lamps.Length} lamps ({poles} yard, {lamps.Length - poles} antennas), up to {beams.maxBeams} " +
                      $"in view within {beams.maxDistance:0} m, atmosphere {beams.atmosphere:0.##}, gain {beams.gain:0.#}.");
        }

        /// <summary>Name of the structure (child of Structures) the light belongs to.</summary>
        static string OwnerOf(Transform t, Transform structures)
        {
            while (t != null && t.parent != structures) t = t.parent;
            return t != null ? t.name : null;
        }
    }
}
