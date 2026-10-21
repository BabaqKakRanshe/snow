using UnityEngine;

namespace Snow.Levels.Forest
{
    /// <summary>
    /// The base yard half its former area (user decision 21.10: "территория базы должна занимать раза 2 меньше места"): the
    /// yard's layout is drawn in towards the main building by 1/√2 — the building itself, its room boxes and everything in it
    /// stay where the package put them. Shared by the terrain bake (asphalt, chain-link fence), the Forest integration (the
    /// yard's structures, the power line) and, through <see cref="ForestRelocation"/>, the opening's items at run time.
    /// </summary>
    public static class ForestYardPlan
    {
        public static readonly Vector3 Centre = new Vector3(340f, 0f, -360f);  // the main building
        public const float Scale = 0.7071f;
        /// <summary>m: the yard (chain-link fence 216 x 162 m around the building); farther things keep their place.</summary>
        public const float Radius = 135f;
        /// <summary>The yard's structures that move (the main building, the meteo station and the moved checkpoint do not).</summary>
        public static readonly string[] Structures = { "Helipad", "RadioMast", "LogPile", "Hatch", "Garage", "FuelTanks", "GenContainer", "Trafo", "Crates", "LightPole", "PowerPole", "Gate" };

        public static bool InYard(Vector3 p) => new Vector2(p.x - Centre.x, p.z - Centre.z).sqrMagnitude <= Radius * Radius;

        /// <summary>A plan point drawn in towards the building (height unchanged).</summary>
        public static Vector3 Map(Vector3 p) => new Vector3(Centre.x + (p.x - Centre.x) * Scale, p.y, Centre.z + (p.z - Centre.z) * Scale);
    }
}
