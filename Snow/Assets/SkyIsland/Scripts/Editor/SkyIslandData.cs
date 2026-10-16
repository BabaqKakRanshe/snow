using System;
using UnityEngine;

namespace SkyIsland.EditorTools
{
    // JSON contracts produced by the generator (Data/*.json). All positions/rotations are in Unity space.

    [Serializable] public class MarkerData { public string name; public float[] expected; }

    [Serializable] public class Placement
    {
        public string m;      // model / prefab name
        public float[] p;     // position
        public float[] q;     // rotation quaternion (x, y, z, w)
        public float s;       // uniform scale (0 = 1)
    }

    [Serializable] public class LightDef
    {
        public string t;      // "point" | "spot"
        public float[] p;     // local position
        public float[] d;     // local direction (spot)
        public float[] c;     // linear colour
        public float i;       // intensity (URP units)
        public float r;       // range
        public float a;       // spot angle
        public bool sh;       // shadows
        public string tag;
    }

    [Serializable] public class PrefabLights { public string model; public LightDef[] lights; }

    [Serializable] public class GlowDef
    {
        public float[] p;     // position (prefab-local, or world for worldGlows)
        public string m;      // glow material (M_Glow_*)
        public float s;       // halo diameter in metres
    }

    [Serializable] public class PrefabGlows { public string model; public GlowDef[] glows; }
    [Serializable] public class RadarHead { public string model; public string child; public float rpm; }
    [Serializable] public class Viewpoint { public string name; public float[] p; public float[] target; public float fov; public string preview; }
    [Serializable] public class ProbeDef { public float[] p; public float[] size; }

    [Serializable] public class LayoutData
    {
        public int version;
        public MarkerData marker;
        public string[] worldModels;
        public Placement[] prefabs;
        public Placement[] trees;
        public Placement[] rocks;
        public PrefabLights[] prefabLights;
        public PrefabGlows[] prefabGlows;
        public GlowDef[] worldGlows;
        public RadarHead[] radarHeads;
        public Viewpoint[] viewpoints;
        public ProbeDef[] reflectionProbes;
    }

    [Serializable] public class DetailSpec { public string albedo; public string normal; public float[] tiling; public float normalScale; }

    [Serializable] public class MatSpec
    {
        public string name, type, albedo, normal, mask, emissionMap;
        public float[] tiling, color, emission;
        public float emissionIntensity, smoothness, metallic, cutoff, normalScale, softness;
        public bool doubleSided, instancing;
        public DetailSpec detail;
    }

    [Serializable] public class MaterialsData { public MatSpec[] materials; }

    [Serializable] public class FogData { public string mode; public float density; public float[] color; }

    [Serializable] public class SkyData
    {
        public float[] horizon, band, zenith, below, sunColor;
        public float bandHeight, bandWidth, cloudScale, cloudContrast, cloudSpeed, sunGlow, sunGlowPower, sunDisc, sunDiscPower;
    }

    [Serializable] public class SunData { public float[] dir_unity; public float[] color; public float intensity; }
    [Serializable] public class AmbientData { public float[] sky, equator, ground; }
    [Serializable] public class BloomData { public float threshold, intensity, scatter, clamp; public float[] tint; public bool highQuality; }
    [Serializable] public class VignetteData { public float intensity, smoothness; public float[] color; public bool rounded; }
    [Serializable] public class GrainData { public string type; public float intensity, response; }

    [Serializable] public class PostData
    {
        public float exposure;
        public float contrast;
        public string tonemap;
        public BloomData bloom;
        public VignetteData vignette;
        public GrainData grain;
        public float chromaticAberration;
        public string lut;
        public float lutContribution;
    }

    [Serializable] public class LookData
    {
        public FogData fog;
        public SkyData sky;
        public SunData sun;
        public AmbientData ambient;
        public PostData post;
        public float localLightScale = 1f;
        public float emissionScale = 1f;
    }

    public static class J
    {
        public static Vector3 V3(float[] a) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;
        public static Vector2 V2(float[] a, Vector2 def) => a != null && a.Length >= 2 ? new Vector2(a[0], a[1]) : def;
        public static Quaternion Q(float[] a) => a != null && a.Length >= 4 ? new Quaternion(a[0], a[1], a[2], a[3]) : Quaternion.identity;
        /// <summary>Linear colour from json.</summary>
        public static Color Lin(float[] a, float alpha = 1f) => a != null && a.Length >= 3 ? new Color(a[0], a[1], a[2], alpha) : Color.white;
        /// <summary>Linear colour converted to the gamma-space value Unity expects for Color properties
        /// (Unity converts it back to linear when the project uses Linear colour space; in Gamma space the
        /// display value is what the shaders should get anyway).</summary>
        public static Color Gam(float[] a, float mul = 1f)
        {
            var c = Lin(a);
            c = new Color(c.r * mul, c.g * mul, c.b * mul, 1f);
            return c.gamma;
        }
        public static Color Gam(Color linear)
        {
            return linear.gamma;
        }
    }
}
