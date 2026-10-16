using System;
using System.Collections.Generic;
using UnityEngine;

namespace SkyIsland.EditorTools
{
    // JSON contracts produced by the generator (Data/<Id>_Layout.json, <Id>_Materials.json, <Id>_Look.json).
    // All positions/rotations are in Unity space (metres), colours are LINEAR.
    // v2 (CONTRACT_v2.md): every new field is optional. Absent fields come back from JsonUtility as 0 / false /
    // null or empty, so the builder treats 0 and empty values as "not set" and applies its defaults
    // (it never relies on field initialisers or on null to detect a missing object).

    [Serializable] public class MarkerData { public string name; public float[] expected; }

    [Serializable] public class Placement
    {
        public string m;      // model / prefab name
        public float[] p;     // position
        public float[] q;     // rotation quaternion (x, y, z, w)
        public float s;       // uniform scale (0 = 1)
        public string g;      // v2.2: proximity group name (layout.groups) or empty
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
        public bool off;      // v2.3: created disabled (power grid lamps: switched on by SkyIslandPowerGrid)
        // v2.6 light fixtures (all optional)
        public string fx;     // fixture object (model child): the light goes under it, driven by its SkyIslandLightFixture
        public string room;   // light zone = layout.lighting.rooms[].name (rendering layers, room switch)
        public string kind;   // fluorescent | incandescent | halogen | hps | mh | led | flame | exit | emergency | beacon
        public string circuit;// "power" (grid, default) | "battery" (exit signs, emergency lights)
        public float k;       // colour temperature in kelvin (0 = plain colour c)
        public bool hero;     // may get a realtime shadow from the shadow budget
        public bool faulty;   // failing lamp
        public bool nosw;     // not on the room switch (grid only: street and obstruction lights)
        public float lod;     // light LOD distance in metres (0 = default of the kind)
        public float warmup;  // gas discharge lamps: warm-up seconds (0 = 90, MH 120)
        public float restrike;// gas discharge lamps: restart delay after losing power (0 = 60, MH 240)
        public float[] flash; // beacons: [period s, on fraction] (empty = steady)
    }

    [Serializable] public class PrefabLights { public string model; public LightDef[] lights; }

    [Serializable] public class GlowDef
    {
        public float[] p;     // position (prefab-local, or world for worldGlows)
        public string m;      // glow material (M_Glow_*)
        public float s;       // halo diameter in metres
        public string tag;    // v2.3: object name Glow_<tag>_<n> (empty: Glow_<n>)
        public bool off;      // v2.3: created inactive (power grid halos)
        public string fx;     // v2.6: fixture object (model child) the halo belongs to
    }

    [Serializable] public class PrefabGlows { public string model; public GlowDef[] glows; }
    [Serializable] public class RadarHead { public string model; public string child; public float rpm; }
    [Serializable] public class Viewpoint { public string name; public float[] p; public float[] target; public float fov; public string preview; }
    [Serializable] public class ProbeDef { public float[] p; public float[] size; }

    // ------------------------------------------------------------------ layout v2 extras
    /// <summary>Spins the named child of every placed instance of a model (axis in the child's local space).</summary>
    [Serializable] public class RotatorDef { public string model; public string child; public float[] axis; public float rpm; }

    /// <summary>v2.2: openable child of a model (door leaf, drawer, lid): hinge = AngleAxis(angle, axis) in the parent's space through
    /// the child's pivot, slide = move along axis; [min, max] degrees / metres, min = closed, max = open. v2.6: open = starts
    /// open (light switches that start on). v2.7: kind scale (factor along the child's own axis, roller-shutter curtains),
    /// speed (0 = default), needsPower, drives (children that follow this part: shutter switches and strap winders).</summary>
    [Serializable] public class InteractableDef
    {
        public string model, child, kind; public float[] axis; public float min, max; public bool locked, open;
        public float speed; public bool needsPower; public string[] drives;
    }

    /// <summary>v2.2: placements with g == name are parented under a SkyIslandProximityGroup (enabled only near the camera).
    /// center / size: world bounds (Unity), margin: activation distance around them.</summary>
    [Serializable] public class GroupDef { public string name; public float[] center, size; public float margin; }

    /// <summary>Runtime copy of a material whose emission flickers between min and max (+ lights named lightPrefix*).</summary>
    [Serializable] public class FlickerDef { public string material; public float min, max, speed; public string lightPrefix; }

    /// <summary>Runtime copy of a material with scrolling texture offsets (UV per second).</summary>
    [Serializable] public class UVScrollDef { public string material; public float[] speed; public float[] normalSpeed; }

    /// <summary>Particle emitter: type smoke | steam | spray | dust | embers; colour linear (null = preset).</summary>
    [Serializable] public class EmitterDef { public string type; public float[] p; public float scale, rate; public float[] color; }

    /// <summary>Reservoir: surfaces (object names) move between minY and maxY, start at startY.</summary>
    [Serializable] public class WaterDef
    {
        public string[] surfaces;
        public float minY, maxY, startY;
        public string[] flowObjects;
        public bool IsSet => surfaces != null && surfaces.Length > 0;
    }

    /// <summary>Trigger box that thickens the fog (density x densityMul, optional colour) over duration seconds.</summary>
    [Serializable] public class FogEventDef
    {
        public string name;
        public float[] center, size;
        public float densityMul;
        public float[] color;
        public float duration;
        public bool oneShot;
        public string tag;
    }

    /// <summary>v2.3: location power grid. Lights named lightPrefix* and halos named glowPrefix* start off; power comes on when
    /// every interactable whose name starts with one of the `switches` prefixes is open (generator start lever, main switch).
    /// materialsOff[i] is swapped for materialsOn[i] on the renderers while powered.</summary>
    [Serializable] public class PowerDef
    {
        public string lightPrefix, glowPrefix;
        public string[] switches;
        public string[] materialsOff, materialsOn;
        public bool startPowered;
        public bool IsSet => !string.IsNullOrEmpty(lightPrefix) || !string.IsNullOrEmpty(glowPrefix);
    }

    /// <summary>v2.4: named empty transform "Anchor_&lt;name&gt;" under the location root.</summary>
    [Serializable] public class AnchorDef { public string name; public float[] p, q; }

    /// <summary>v2.6: light zone. layer = rendering layer bit of its static geometry, sw = name prefix of its light switches.</summary>
    [Serializable] public class LightRoomDef { public string name; public int layer; public string sw; }

    /// <summary>v2.6: rendering layer mask of a model child (node) and the renderers under it.</summary>
    [Serializable] public class LayerAssignDef { public string model, node; public int mask; }

    /// <summary>v2.6: rendering layers per room and the light budget (SkyIslandLightManager). Lights of a room light only
    /// renderers with the room's bit (plus dynamicMask: players, props), so they do not leak through walls without shadows.</summary>
    [Serializable] public class LightingDef
    {
        public int exteriorLayer;         // rendering layer bit of the exterior statics (0 = 1)
        public int defaultMask;           // renderers of this location not listed in assign (0 = 1 << exteriorLayer)
        public int dynamicMask;           // bits every room light lights in addition to its room (0 = 1, the Default layer)
        public LightRoomDef[] rooms;
        public LayerAssignDef[] assign;
        public int maxShadowed;           // shadow budget: hero lamps with realtime shadows (0 = 3, negative = none)
        public float shadowRange;         // metres (0 = 14)
        public float lodFade;             // light LOD fade band in metres (0 = 6)
        public bool IsSet => rooms != null && rooms.Length > 0;
    }

    [Serializable] public class SpawnDef
    {
        public float[] p;
        public float yaw;
        public bool IsSet => p != null && p.Length >= 3;
    }

    /// <summary>Bone pose LOCAL to the giant root (Unity space). p or q may be missing (keeps the rest value).</summary>
    [Serializable] public class BonePose { public string name; public float[] p; public float[] q; }

    /// <summary>A posed instance of a skeleton model ("shared:Giant_Skeleton" = SkyIsland/Models/Giant_Skeleton.fbx).</summary>
    [Serializable] public class GiantDef
    {
        public string model, name, material;
        public float[] p, q;
        public float s;
        public BonePose[] bones;
        public string[] hide;
    }

    [Serializable] public class LayoutData
    {
        public int version;
        public string id;
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

        // v2 (all optional)
        public string[] vegetationPrefixes;
        public string[] grassPrefixes;
        public string[] noColliderPrefixes;
        public RotatorDef[] rotators;
        public FlickerDef[] flickers;
        public UVScrollDef[] uvScroll;
        public EmitterDef[] emitters;
        public WaterDef water;
        public FogEventDef[] fogEvents;
        public SpawnDef spawn;
        public InteractableDef[] interactables;
        public GroupDef[] groups;
        public PowerDef power;            // v2.3
        public AnchorDef[] anchors;       // v2.4: named empty transforms (places for separate assets, e.g. the players' car)
        public LightingDef lighting;      // v2.6: rendering layers per room, light LOD, shadow budget
        /// <summary>Read separately (LayoutJson.ReadGiants): "bones" is a JSON object keyed by bone name,
        /// which JsonUtility cannot deserialize.</summary>
        [NonSerialized] public GiantDef[] giants;
    }

    [Serializable] public class DetailSpec { public string albedo; public string normal; public float[] tiling; public float normalScale; }

    [Serializable] public class MatSpec
    {
        public string name, type, albedo, normal, mask, emissionMap;
        public float[] tiling, color, emission;
        public float emissionIntensity, smoothness, metallic, cutoff, normalScale, softness;
        public bool doubleSided, instancing;
        public DetailSpec detail;
        // v2: type "transparent"
        public float alpha;               // base alpha (0 = default: 1 with alphaFromTexture, else 0.6 like the preview)
        public bool alphaFromTexture;     // multiply by the albedo alpha (the texture is imported with its alpha)
        // v2.2: type "interior" (SkyIsland/InteriorWindow): albedo = room atlas, curtains = 2x2 RGBA curtain layer
        public string curtains;
        public float cols, rows, depth, camDist, litIntensity, darkIntensity, fresnel;
        public float[] litTint, darkTint, reflTop, reflBottom;
        // v2.4: true turns off specular highlights and environment reflections (PSX look: no specular)
        public bool noSpecular;
    }

    [Serializable] public class MaterialsData { public MatSpec[] materials; }

    [Serializable] public class FogData
    {
        public string mode; public float density; public float[] color;
        // v2.4 (PSX look): mode "Linear" uses start / end (metres); glowFloor = least fraction of a halo's brightness kept in fog
        public float start, end, glowFloor;
        public bool IsLinear => mode == "Linear" && end > start;
    }

    [Serializable] public class SkyData
    {
        public float[] horizon, band, zenith, below, sunColor;
        public float bandHeight, bandWidth, cloudScale, cloudContrast, cloudSpeed, sunGlow, sunGlowPower, sunDisc, sunDiscPower;
        // v2.4: procedural stars (0 intensity = none)
        public float[] starColor;
        public float starIntensity, starDensity, starScale, starSize;
    }

    /// <summary>v2.4: PSX output (SkyIslandPSX on the camera): the frame is rendered at `height` lines and shown with
    /// nearest-neighbour upscaling, colour reduced to `colorBits` per channel with a 4x4 ordered dither.</summary>
    [Serializable] public class PsxData
    {
        public int height;                // internal lines (240 = PS1); 0 = off
        public int colorBits;             // 5 = 15-bit colour
        public float dither;              // 0..1 strength of the Bayer dither
        public bool pointTextures;        // informative: '*_px' textures are imported with Point filtering
        public bool IsSet => height > 0;
    }

    [Serializable] public class SunData
    {
        public float[] dir_unity; public float[] color; public float intensity;
        public float shadowStrength;      // v2: 0 = default 0.8
    }

    [Serializable] public class AmbientData
    {
        public float[] sky, equator, ground;
        public float intensity;           // v2: multiplies the trilight colours, 0 = default 1
    }

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
        public string lut;                // file in <root>/Textures/LUT ("shared:" = SkyIsland's LUT folder)
        public float lutContribution;     // <= 0: no LUT
    }

    /// <summary>v2: thins the fog away from / above an elliptical footprint (SkyIslandFogByAltitude).</summary>
    [Serializable] public class FogByAltitudeData
    {
        public float[] center;            // x, z
        public float[] radii;             // half-axes along x, z
        public float topY, heightRange, distanceRange, airFactor;
        public bool IsSet => radii != null && radii.Length >= 2 && (radii[0] > 0f || radii[1] > 0f);
    }

    /// <summary>v2: one transparent haze sheet (SkyIsland/FogSheet) at height y.</summary>
    [Serializable] public class HazeDef { public float y, density; public float[] speed; }

    /// <summary>v2: main camera far clip / vertical field of view (0 = default).</summary>
    [Serializable] public class CameraData { public float far, fov; }

    [Serializable] public class LookData
    {
        public FogData fog;
        public SkyData sky;
        public SunData sun;
        public AmbientData ambient;
        public PostData post;
        public float localLightScale = 1f;
        public float emissionScale = 1f;
        // v2 (all optional)
        public FogByAltitudeData fogByAltitude;
        public HazeDef[] haze;
        public CameraData camera;
        public PsxData psx;               // v2.4

        /// <summary>Replaces missing sections by empty ones so a sparse file cannot cause null references.</summary>
        public void FillMissing()
        {
            if (fog == null) fog = new FogData();
            if (sky == null) sky = new SkyData();
            if (sun == null) sun = new SunData();
            if (ambient == null) ambient = new AmbientData();
            if (post == null) post = new PostData();
            if (post.bloom == null) post.bloom = new BloomData();
            if (post.vignette == null) post.vignette = new VignetteData();
            if (post.grain == null) post.grain = new GrainData();
        }
    }

    public static class J
    {
        public static Vector3 V3(float[] a) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;
        public static Vector2 V2(float[] a, Vector2 def) => a != null && a.Length >= 2 ? new Vector2(a[0], a[1]) : def;
        public static Quaternion Q(float[] a) => a != null && a.Length >= 4 ? new Quaternion(a[0], a[1], a[2], a[3]) : Quaternion.identity;
        /// <summary>True if the array holds at least n values.</summary>
        public static bool Has(float[] a, int n) => a != null && a.Length >= n;
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

    /// <summary>Contract parts that JsonUtility cannot read (objects keyed by name).</summary>
    public static class LayoutJson
    {
        /// <summary>giants[] of a layout file (null when absent). bones: {"B_Femur_R": {p, q}, ...} (an array of
        /// {name, p, q} is accepted too).</summary>
        public static GiantDef[] ReadGiants(string layoutJson)
        {
            if (string.IsNullOrEmpty(layoutJson) || layoutJson.IndexOf("\"giants\"", StringComparison.Ordinal) < 0) return null;
            if (!SkyIslandJson.TryParseTopLevel(layoutJson, "giants", out var value) || !(value is List<object> list)) return null;
            var result = new List<GiantDef>();
            foreach (var item in list)
            {
                if (!(item is SkyIslandJson.JsonObject o)) continue;
                var g = new GiantDef
                {
                    model = SkyIslandJson.ToStr(o.Get("model")),
                    name = SkyIslandJson.ToStr(o.Get("name")),
                    material = SkyIslandJson.ToStr(o.Get("material")),
                    p = SkyIslandJson.ToFloats(o.Get("p")),
                    q = SkyIslandJson.ToFloats(o.Get("q")),
                    s = SkyIslandJson.ToFloat(o.Get("s")),
                    hide = SkyIslandJson.ToStrings(o.Get("hide")),
                    bones = ReadBones(o.Get("bones")),
                };
                result.Add(g);
            }
            return result.ToArray();
        }

        static BonePose[] ReadBones(object v)
        {
            var bones = new List<BonePose>();
            if (v is SkyIslandJson.JsonObject byName)
            {
                foreach (var kv in byName)
                    if (kv.Value is SkyIslandJson.JsonObject b)
                        bones.Add(new BonePose { name = kv.Key, p = SkyIslandJson.ToFloats(b.Get("p")), q = SkyIslandJson.ToFloats(b.Get("q")) });
            }
            else if (v is List<object> arr)
            {
                foreach (var item in arr)
                    if (item is SkyIslandJson.JsonObject b)
                        bones.Add(new BonePose { name = SkyIslandJson.ToStr(b.Get("name")), p = SkyIslandJson.ToFloats(b.Get("p")), q = SkyIslandJson.ToFloats(b.Get("q")) });
            }
            return bones.ToArray();
        }
    }
}
