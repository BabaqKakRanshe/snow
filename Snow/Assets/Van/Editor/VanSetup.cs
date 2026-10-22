#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UazVan.EditorTools
{
    /// <summary>First-import settings for the van's model and textures (Assets/Van/...).</summary>
    public class VanImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Van/Textures/")) return;
            if (!assetImporter.importSettingsMissing) return;
            VanSetup.ApplyTextureSettings((TextureImporter)assetImporter, assetPath);
        }

        void OnPreprocessModel()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Van/Models/")) return;
            if (!assetImporter.importSettingsMissing) return;
            VanSetup.ApplyModelSettings((ModelImporter)assetImporter);
        }
    }

    /// <summary>
    /// Tools / UAZ Van / Build Materials + Prefab (URP):
    ///  - texture import (PSX atlases Point and uncompressed, masks, the wipe map as data, cookies), materials from
    ///    Data/materials.json remapped onto Models/Van.fbx by name (URP Lit; the glass on Van/Glass);
    ///  - Prefabs/Van.prefab: the model with VanRig (240 parts, 41 lamps, 13 panes, gauges, mirror, radio display),
    ///    49 BoxColliders (Data/colliders.json, COL_* children of their parents), 231 interaction zones IV_* (Data/zones.json:
    ///    trigger boxes on the Interact layer, VanInteractable), 200 sockets (Data/sockets.json), 41 lights Light_*
    ///    (Data/lights.json: spot with the IES cookie or point, colour temperature, every rendering layer, off);
    ///  - Prefabs/Items/VanItem_&lt;type&gt;.prefab: the 43 item types of the kit (Data/items.json): model, BoxCollider,
    ///    VanItemInfo, the light of the torch, flares and hand lamp.
    /// No Rigidbody, no network, no game logic: the game's prefab variant adds them (VAN3_INTEGRATION_UNITY.md).
    /// Running it again rebuilds the same files (references to the prefabs stay valid).
    /// </summary>
    public static class VanSetup
    {
        public const string Root = "Assets/Van";
        const string Fbx = Root + "/Models/Van.fbx";

        // ------------------------------------------------------------------ import settings
        static bool IsMask(string n) => n == "T_Van_Frost" || n == "T_Van_Snow" || n == "T_Van_Fog" || n == "T_Van_Handprints";

        public static void ApplyTextureSettings(TextureImporter ti, string path)
        {
            string n = Path.GetFileNameWithoutExtension(path);
            ti.textureType = TextureImporterType.Default;
            ti.isReadable = false;
            ti.anisoLevel = 0;
            ti.filterMode = FilterMode.Point;                          // PSX texels
            ti.textureCompression = TextureImporterCompression.Uncompressed;   // palette + dither intact
            ti.mipmapEnabled = true;
            ti.sRGBTexture = true;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.maxTextureSize = 1024;
            if (n.StartsWith("T_Cookie_"))
            {
                // light cookies from the IES diagrams: grey, sRGB (the URP cookie atlas decodes them), clamped, no mips
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.mipmapEnabled = false;
                ti.filterMode = FilterMode.Bilinear;
                ti.maxTextureSize = 256;
                return;
            }
            if (n == "T_Van_WipeMap")
            {
                ti.sRGBTexture = false;                                 // R, G = wiper angles: data
                ti.mipmapEnabled = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                return;
            }
            if (IsMask(n))
            {
                ti.wrapMode = TextureWrapMode.Clamp;                    // RGB = the layer's colour, A = the order (data)
                return;
            }
        }

        public static void ApplyModelSettings(ModelImporter mi)
        {
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importAnimation = false;
            mi.importBlendShapes = true;                                // Wheel_* Flat, Snow_Roof / Visor / Bumper Clear
            mi.animationType = ModelImporterAnimationType.None;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.None;            // no normal maps
            mi.addCollider = false;
            mi.isReadable = false;
            mi.meshCompression = ModelImporterMeshCompression.Off;
        }

        // ------------------------------------------------------------------ data
#pragma warning disable 0649
        [Serializable] class GearJ { public string name; public float sideDeg, foreAftDeg, lift; }
        [Serializable] class PartJ
        {
            public string name, kind, parent, label, unit, removable, zoneKind, zoneSide, itemType, blendshape, flatBlendshape, side;
            public float[] pivot, pivotLocal, dir, rotAxis, dir2, rotAxis2, liftDir, boxCenter, boxSize;
            public float rest, min, max, scaleMin, scaleMax, angleMin, angleMax;
            public bool hasRange, hasAxis, hasBox, hasScale, hasWheel, defaultHidden;
            public GearJ[] gears;
        }
        [Serializable] class PartsFile { public PartJ[] parts; }
        [Serializable] class SocketJ
        {
            public string name, kind, parent, pane;
            public float[] positionLocal, dir, up, facing;
            public bool hasDir, hasUp, hasFacing;
        }
        [Serializable] class SocketsFile { public SocketJ[] sockets; }
        [Serializable] class ColliderJ { public string name, parent; public float[] center, size; }
        [Serializable] class CollidersFile { public ColliderJ[] colliders; }
        [Serializable] class LightJ
        {
            public string name, type, group, toggle, fuse, parent, lens, unityLight, cookie, flicker, dimmedBy;
            public float[] positionLocal, forward, up, filter;
            public float intensity, kelvin, range, spotOuter, spotInner, watts;
            public bool shadows, ownPower, glow, beam;
            public int shadowPriority;
        }
        [Serializable] class LightsFile { public LightJ[] lights; }
        [Serializable] class PaneJ { public string name; public bool cab, wipers; public float[] uvU, uvV; }
        [Serializable] class GlassFile { public PaneJ[] panes; }
        [Serializable] class ZoneJ { public string id, kind, side, label, parent, part, socket, itemType, pane, insideOf, coveredBy; public float[] center, size; }
        [Serializable] class ZonesFile { public ZoneJ[] zones; }
        [Serializable] class MatJ { public string name, kind, baseMap, emissionMap; public float[] color, emissionColor; public float alpha, smoothness; }
        [Serializable] class MatsFile { public MatJ[] materials; }
        [Serializable] class ItemTypeJ
        {
            public string type, label, prefab, sourcePart, tether, tetherFrom, lamp, readable, contents;
            public string[] parts;
            public float massKg, tetherM, capacityL, densityKgL;
            public int hands;
        }
        [Serializable] class ItemsFile { public ItemTypeJ[] types; }
#pragma warning restore 0649

        static T Load<T>(string file)
        {
            string p = Root + "/Data/" + file;
            if (!File.Exists(p)) throw new FileNotFoundException($"[UazVan] {p} not found: keep the package folder at {Root}.");
            return JsonUtility.FromJson<T>(File.ReadAllText(p));
        }

        static Vector3 V(float[] a) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;
        static Vector4 V4(float[] a) => a != null && a.Length >= 4 ? new Vector4(a[0], a[1], a[2], a[3]) : Vector4.zero;
        static Color C(float[] a, float alpha = 1f) => a != null && a.Length >= 3 ? new Color(a[0], a[1], a[2], alpha) : Color.white;

        // ------------------------------------------------------------------ build
        [MenuItem("Tools/UAZ Van/Build Materials + Prefab")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[UazVan] Leave Play Mode first, then run Build Materials + Prefab again.");
                return;
            }
            if (!(AssetImporter.GetAtPath(Fbx) is ModelImporter mi))
            {
                Debug.LogError($"[UazVan] {Fbx} not found. Copy the package folder to {Root}.");
                return;
            }
            if (GraphicsSettings.currentRenderPipeline == null)
                Debug.LogWarning("[UazVan] No render pipeline asset: the materials are made for URP (Universal Render Pipeline/Lit).");
            foreach (var guid in AssetDatabase.FindAssets("t:Texture", new[] { Root + "/Textures" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(p) is TextureImporter ti)) continue;
                ApplyTextureSettings(ti, p);
                ti.SaveAndReimport();
            }
            CheckUrpDepth();
            EnsureFolder(Root, "Materials");
            EnsureFolder(Root, "Prefabs");
            EnsureFolder(Root + "/Prefabs", "Items");

            var mats = Load<MatsFile>("materials.json").materials;
            var made = new Dictionary<string, Material>();
            foreach (var m in mats) made[m.name] = MakeMaterial(m);
            ApplyModelSettings(mi);
            foreach (var kv in made)
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
            mi.SaveAndReimport();
            var glow = MakeFx("M_FX_VanAirlight", "Van/Airlight");
            var beam = MakeFx("M_FX_VanBeam", "Van/Beam");

            string report = BuildVanPrefab(glow, beam);
            string items = BuildItemPrefabs();
            AssetDatabase.SaveAssets();
            Debug.Log($"[UazVan] Done: {made.Count} materials, {report}; {items}.");
        }

        // ------------------------------------------------------------------ the van
        static readonly string[] NoShadowPrefixes =
        {
            "Needle_", "Toggle_", "Knob_", "Lens_", "Glass_", "lob", "Fuse_", "Spare_Fuse", "Radio_", "Heater_", "Lock_",
            "Wheel_FL_Nut", "Wheel_FR_Nut", "Wheel_RL_Nut", "Wheel_RR_Nut", "Engine_Plug", "Engine_Lead", "Ignition", "Dash_Gauges",
            "Socket_12V", "Horn_Button", "LampBulb", "StoveGlow", "RearMirror_Glass", "Map", "Logbook", "Snow_Head", "Snow_Spot",
            "Snow_Fog", "Battery_Clamp", "MassSwitch", "Pedal_", "Wiper_"
        };

        static string BuildVanPrefab(Material glow, Material beam)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.name = "Van";
            try
            {
                var byName = new Dictionary<string, Transform>(StringComparer.Ordinal);
                foreach (var t in inst.GetComponentsInChildren<Transform>(true))
                {
                    if (t == inst.transform) continue;
                    if (byName.ContainsKey(t.name)) Debug.LogWarning($"[UazVan] two nodes named {t.name}");
                    else byName[t.name] = t;
                }
                Transform Node(string n) => string.IsNullOrEmpty(n) ? inst.transform : byName.TryGetValue(n, out var t) ? t : null;
                var rig = inst.GetComponent<VanRig>();
                if (rig == null) rig = inst.AddComponent<VanRig>();

                // parts
                var parts = Load<PartsFile>("parts.json").parts;
                rig.parts.Clear();
                rig.gears.Clear();
                int stretchPivots = 0, blend = 0;
                foreach (var p in parts)
                {
                    var t = Node(p.name);
                    if (t == null) { Debug.LogWarning($"[UazVan] part {p.name} not in the model"); continue; }
                    var e = new VanRig.Part
                    {
                        name = p.name, kind = KindOf(p.kind), t = t, rotAxis = V(p.rotAxis), dir = V(p.dir), rotAxis2 = V(p.rotAxis2),
                        liftDir = V(p.liftDir), hasRange = p.hasRange, min = p.min, max = p.max, rest = p.rest,
                        defaultHidden = p.defaultHidden, hasScale = p.hasScale, scaleMin = p.scaleMin, scaleMax = p.scaleMax,
                        angleMin = p.angleMin, angleMax = p.angleMax, blendshape = !string.IsNullOrEmpty(p.blendshape) ? p.blendshape : p.flatBlendshape
                    };
                    if (e.kind == VanRig.PartKind.Stretch)
                    {
                        Vector3 d = e.dir.normalized;
                        int ax = Mathf.Abs(d.x) > 0.9999f ? 0 : Mathf.Abs(d.y) > 0.9999f ? 1 : Mathf.Abs(d.z) > 0.9999f ? 2 : -1;
                        if (ax >= 0) { e.scaleNode = t; e.scaleAxis = ax; }
                        else
                        {
                            // scale along a slanted axis: a pivot turned so its +Z is the axis, the part under it
                            var piv = new GameObject("Stretch_" + p.name).transform;
                            piv.SetParent(t.parent, false);
                            piv.localPosition = t.localPosition;
                            piv.localRotation = Quaternion.LookRotation(d, Mathf.Abs(Vector3.Dot(d, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up);
                            t.SetParent(piv, true);
                            e.scaleNode = piv;
                            e.scaleAxis = 2;
                            stretchPivots++;
                        }
                    }
                    e.restPos = t.localPosition;
                    e.restRot = t.localRotation;
                    e.restScale = t.localScale;
                    if (!string.IsNullOrEmpty(e.blendshape))
                    {
                        var smr = t.GetComponent<SkinnedMeshRenderer>();
                        if (smr != null && smr.sharedMesh != null)
                        {
                            e.skin = smr;
                            e.blendIndex = smr.sharedMesh.GetBlendShapeIndex(e.blendshape);
                            if (e.blendIndex >= 0) blend++;
                            smr.updateWhenOffscreen = false;
                        }
                        if (e.blendIndex < 0) Debug.LogWarning($"[UazVan] {p.name}: blend shape {e.blendshape} not found (Import BlendShapes?)");
                    }
                    if (p.gears != null && p.gears.Length > 0)
                        foreach (var g in p.gears)
                            rig.gears.Add(new VanRig.GearPose { name = g.name, sideDeg = g.sideDeg, foreAftDeg = g.foreAftDeg, lift = g.lift });
                    if (p.defaultHidden) t.gameObject.SetActive(false);
                    rig.parts.Add(e);
                }

                // colliders: COL_* children with a BoxCollider each
                int cols = 0;
                foreach (var c in Load<CollidersFile>("colliders.json").colliders)
                {
                    var par = Node(c.parent);
                    if (par == null) { Debug.LogWarning($"[UazVan] collider {c.name}: parent {c.parent} missing"); continue; }
                    var go = new GameObject(c.name);
                    go.transform.SetParent(par, false);
                    var bc = go.AddComponent<BoxCollider>();
                    bc.center = V(c.center);
                    bc.size = V(c.size);
                    cols++;
                }

                // sockets
                var sockRoot = new GameObject("Sockets").transform;
                sockRoot.SetParent(inst.transform, false);
                rig.sockets.Clear();
                foreach (var s in Load<SocketsFile>("sockets.json").sockets)
                {
                    var par = string.IsNullOrEmpty(s.parent) ? sockRoot : Node(s.parent);
                    if (par == null) { Debug.LogWarning($"[UazVan] socket {s.name}: parent {s.parent} missing"); continue; }
                    var go = new GameObject(s.name);
                    go.transform.SetParent(par, false);
                    go.transform.localPosition = V(s.positionLocal);
                    Vector3 fwd = s.hasDir ? V(s.dir) : s.hasFacing ? V(s.facing) : Vector3.forward;
                    Vector3 up = s.hasUp ? V(s.up) : Vector3.up;
                    if (Mathf.Abs(Vector3.Dot(fwd.normalized, up.normalized)) > 0.98f) up = Vector3.forward;
                    if (fwd.sqrMagnitude > 1e-6f) go.transform.localRotation = Quaternion.LookRotation(fwd, up);
                    rig.sockets.Add(new VanRig.SocketRef { name = s.name, kind = s.kind, pane = s.pane, t = go.transform });
                }

                // interaction zones
                int interact = EnsureLayer("Interact");
                int zones = 0;
                foreach (var z in Load<ZonesFile>("zones.json").zones)
                {
                    var par = Node(z.parent);
                    if (par == null) { Debug.LogWarning($"[UazVan] zone {z.id}: parent {z.parent} missing"); continue; }
                    var go = new GameObject("IV_" + z.id);
                    go.transform.SetParent(par, false);
                    if (interact >= 0) go.layer = interact;
                    var bc = go.AddComponent<BoxCollider>();
                    bc.isTrigger = true;
                    bc.center = V(z.center);
                    bc.size = V(z.size);
                    var vi = go.AddComponent<VanInteractable>();
                    vi.id = z.id; vi.kind = z.kind; vi.side = z.side; vi.label = z.label; vi.part = z.part; vi.socket = z.socket;
                    vi.itemType = z.itemType; vi.pane = z.pane; vi.insideOf = z.insideOf; vi.coveredBy = z.coveredBy;
                    zones++;
                }

                // lights
                var cookies = AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Textures/Cookies" })
                    .Select(g => AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(g)))
                    .Where(t => t != null).ToArray();
                rig.lightCookies = cookies;
                rig.lamps.Clear();
                int lights = 0;
                foreach (var l in Load<LightsFile>("lights.json").lights)
                {
                    var par = Node(l.parent);
                    if (par == null) { Debug.LogWarning($"[UazVan] light {l.name}: parent {l.parent} missing"); continue; }
                    var lt = MakeLight(par, l, cookies);
                    var lens = string.IsNullOrEmpty(l.lens) ? null : Node(l.lens);
                    rig.lamps.Add(new VanRig.Lamp
                    {
                        name = l.name, type = l.type, group = l.group, toggle = l.toggle, fuse = l.fuse, light = lt,
                        lens = lens != null ? lens.GetComponent<Renderer>() : null, intensity = l.intensity, kelvin = l.kelvin,
                        range = l.range, watts = l.watts, filter = C(l.filter), spot = l.unityLight == "Spot", shadows = l.shadows,
                        ownPower = l.ownPower, glow = l.glow, beam = l.beam, shadowPriority = l.shadowPriority,
                        flicker = l.flicker == "flasher" ? VanRig.FlickerKind.Flasher : l.flicker == "flare" ? VanRig.FlickerKind.Flare :
                                  l.flicker == "fire" ? VanRig.FlickerKind.Fire : VanRig.FlickerKind.Filament,
                        dimmedBy = l.dimmedBy
                    });
                    lights++;
                }
                rig.glowMaterial = glow;
                rig.beamMaterial = beam;

                // glass panes, mirror, radio, dials, stove, pendant
                rig.panes.Clear();
                foreach (var g in Load<GlassFile>("glass.json").panes)
                {
                    var t = Node(g.name);
                    var r = t != null ? t.GetComponent<Renderer>() : null;
                    if (r == null) { Debug.LogWarning($"[UazVan] pane {g.name} has no renderer"); continue; }
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    rig.panes.Add(new VanRig.Pane
                    {
                        name = g.name, group = g.cab ? "cab" : "saloon", renderer = r, wipers = g.wipers, uvU = V4(g.uvU), uvV = V4(g.uvV)
                    });
                }
                rig.wipeMap = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/T_Van_WipeMap.png");
                rig.mirrorRenderer = Node("RearMirror_Glass")?.GetComponent<Renderer>();
                rig.mirrorCameraPoint = rig.sockets.FirstOrDefault(s => s.name == "MirrorCam")?.t;
                rig.radioDisplay = Node("Radio_Display")?.GetComponent<Renderer>();
                rig.gaugesRenderer = Node("Dash_Gauges")?.GetComponent<Renderer>();
                rig.stoveGlowRenderer = Node("StoveGlow")?.GetComponent<Renderer>();
                rig.pendantBulbRenderer = Node("LampBulb")?.GetComponent<Renderer>();
                rig.needleRenderers = byName.Values.Where(t => t.name.StartsWith("Needle_")).Select(t => t.GetComponent<Renderer>())
                    .Where(r => r != null).ToArray();

                // small parts cast no shadow
                int noShadow = 0;
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                    if (NoShadowPrefixes.Any(px => r.name.StartsWith(px, StringComparison.Ordinal)))
                    {
                        r.shadowCastingMode = ShadowCastingMode.Off;
                        noShadow++;
                    }

                string path = Root + "/Prefabs/Van.prefab";
                PrefabUtility.SaveAsPrefabAsset(inst, path);
                return $"{path}: {rig.parts.Count} parts ({stretchPivots} stretch pivots, {blend} blend shapes), {cols} colliders, " +
                       $"{zones} zones, {rig.sockets.Count} sockets, {lights} lights ({cookies.Length} cookies), {rig.panes.Count} panes, " +
                       $"{noShadow} renderers without shadows";
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(inst);
            }
        }

        static VanRig.PartKind KindOf(string k)
        {
            switch (k)
            {
                case "hinge": return VanRig.PartKind.Hinge;
                case "twist": return VanRig.PartKind.Twist;
                case "slide": return VanRig.PartKind.Slide;
                case "item": return VanRig.PartKind.Item;
                case "spin": return VanRig.PartKind.Spin;
                case "stretch": return VanRig.PartKind.Stretch;
                default: return VanRig.PartKind.Static;
            }
        }

        static Light MakeLight(Transform parent, LightJ l, Texture2D[] cookies)
        {
            var go = new GameObject("Light_" + l.name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = V(l.positionLocal);
            Vector3 f = V(l.forward), up = V(l.up);
            if (up.sqrMagnitude < 1e-6f || Mathf.Abs(Vector3.Dot(f.normalized, up.normalized)) > 0.98f) up = Vector3.forward;
            go.transform.localRotation = Quaternion.LookRotation(f, up);
            var lt = go.AddComponent<Light>();
            bool spot = l.unityLight == "Spot";
            lt.type = spot ? LightType.Spot : LightType.Point;
            if (spot)
            {
                lt.spotAngle = l.spotOuter;
                lt.innerSpotAngle = Mathf.Min(l.spotInner, l.spotOuter);
                lt.cookie = cookies.FirstOrDefault(c => c.name == l.cookie);
            }
            lt.range = l.range;
            lt.intensity = l.intensity;
            lt.shadows = LightShadows.None;                             // the rig's shadow budget decides
            lt.shadowNearPlane = 0.05f;
            lt.renderMode = LightRenderMode.ForcePixel;
            lt.lightmapBakeType = LightmapBakeType.Realtime;            // a moving vehicle: nothing baked
            if (GraphicsSettings.lightsUseColorTemperature)
            {
                lt.useColorTemperature = true;
                lt.colorTemperature = l.kelvin;
                lt.color = C(l.filter).gamma;
            }
            else lt.color = (C(l.filter) * Mathf.CorrelatedColorTemperatureToRGB(l.kelvin)).gamma;
            // shadow map size when the rig lets the lamp cast: head lamps and the torch High, the other spots Medium,
            // point lights (six faces) and the small interior spots Low
            int tier = l.type == "head" || l.type == "torch" ? 2 : spot && (l.type == "spot" || l.type == "fog" || l.type == "work") ? 1 : 0;
            SetUrpLightData(lt, tier);
            lt.renderingLayerMask = -1;                                  // every rendering layer (Forest statics are on 2)
            lt.enabled = false;
            return lt;
        }

        // URP keeps the light's rendering layers and shadow tier in UniversalAdditionalLightData (reflection: no package
        // reference). Rendering layers: all, the Forest ground and statics are on layer 2.
        static void SetUrpLightData(Light lt, int shadowTier)
        {
            var type = FindType("UnityEngine.Rendering.Universal.UniversalAdditionalLightData");
            if (type == null) return;
            var c = lt.GetComponent(type);
            if (c == null) c = lt.gameObject.AddComponent(type);
            if (c == null) return;
            try
            {
                var prop = type.GetProperty("renderingLayers", BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && prop.CanWrite)
                {
                    var pt = prop.PropertyType;
                    if (pt == typeof(uint)) prop.SetValue(c, uint.MaxValue);
                    else if (pt == typeof(int)) prop.SetValue(c, -1);
                    else
                    {
                        // a mask struct (RenderingLayerMask) built from a uint
                        var op = pt.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(m =>
                            m.Name == "op_Implicit" && m.ReturnType == pt && m.GetParameters().Length == 1 &&
                            m.GetParameters()[0].ParameterType == typeof(uint));
                        if (op != null) prop.SetValue(c, op.Invoke(null, new object[] { uint.MaxValue }));
                        else Debug.LogWarning($"[UazVan] {lt.name}: set Rendering Layers to Everything by hand ({pt.Name}).");
                    }
                }
                var so = new SerializedObject(c);
                var tier = so.FindProperty("m_AdditionalLightsShadowResolutionTier");
                if (tier != null) { tier.intValue = shadowTier; so.ApplyModifiedPropertiesWithoutUndo(); }
            }
            catch (Exception e) { Debug.LogWarning("[UazVan] URP light data of " + lt.name + ": " + (e.InnerException?.Message ?? e.Message)); }
        }

        // ------------------------------------------------------------------ item prefabs
        static string BuildItemPrefabs()
        {
            var types = Load<ItemsFile>("items.json").types;
            var parts = Load<PartsFile>("parts.json").parts.ToDictionary(p => p.name);
            var lights = Load<LightsFile>("lights.json").lights;
            var socketData = Load<SocketsFile>("sockets.json").sockets;
            var cookies = AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Textures/Cookies" })
                .Select(g => AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(g))).Where(t => t != null).ToArray();
            var itemPart = new HashSet<string>(types.SelectMany(t => t.parts ?? new string[0]));
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            int made = 0;
            try
            {
                var byName = inst.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                foreach (var it in types)
                {
                    if (!byName.TryGetValue(it.sourcePart, out var src)) { Debug.LogWarning($"[UazVan] item {it.type}: {it.sourcePart} missing"); continue; }
                    var go = UnityEngine.Object.Instantiate(src.gameObject);
                    go.name = it.prefab;
                    go.SetActive(true);
                    go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    go.transform.localScale = Vector3.one;
                    // children that are items of their own (the tools in the tool box, the fuses in the tin) stay out
                    foreach (var c in go.GetComponentsInChildren<Transform>(true).Where(c => c != go.transform && itemPart.Contains(c.name)).ToArray())
                        if (c != null) UnityEngine.Object.DestroyImmediate(c.gameObject);
                    // the item's own sockets (spout thread, jack pump socket, blowtorch flame, cord ends, hooks): same local pose
                    var nodes = go.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                    nodes[it.sourcePart] = go.transform;
                    foreach (var s in socketData)
                    {
                        if (string.IsNullOrEmpty(s.parent) || !nodes.TryGetValue(s.parent, out var sp)) continue;
                        var sg = new GameObject(s.name);
                        sg.transform.SetParent(sp, false);
                        sg.transform.localPosition = V(s.positionLocal);
                        Vector3 fwd = s.hasDir ? V(s.dir) : s.hasFacing ? V(s.facing) : Vector3.forward;
                        Vector3 up = s.hasUp ? V(s.up) : Vector3.up;
                        if (Mathf.Abs(Vector3.Dot(fwd.normalized, up.normalized)) > 0.98f) up = Vector3.forward;
                        if (fwd.sqrMagnitude > 1e-6f) sg.transform.localRotation = Quaternion.LookRotation(fwd, up);
                    }
                    var p = parts[it.sourcePart];
                    if (p.hasBox)
                    {
                        var bc = go.AddComponent<BoxCollider>();
                        bc.center = V(p.boxCenter);
                        bc.size = Vector3.Max(V(p.boxSize), Vector3.one * 0.01f);
                    }
                    var info = go.AddComponent<VanItemInfo>();
                    info.type = it.type; info.label = it.label; info.massKg = it.massKg; info.hands = it.hands;
                    info.capacityL = it.capacityL; info.contents = it.contents; info.densityKgL = it.densityKgL;
                    info.tether = it.tether; info.tetherFrom = it.tetherFrom; info.tetherM = it.tetherM; info.readable = it.readable;
                    if (!string.IsNullOrEmpty(it.lamp))
                    {
                        string lampName = it.lamp == "Flare_N" ? it.sourcePart : it.lamp;
                        var lj = lights.FirstOrDefault(l => l.name == lampName);
                        if (lj != null)
                        {
                            info.lamp = MakeLight(go.transform, lj, cookies);
                            var lens = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == lj.lens);
                            if (lens != null) info.lens = lens.GetComponent<Renderer>();
                        }
                    }
                    PrefabUtility.SaveAsPrefabAsset(go, $"{Root}/Prefabs/Items/{it.prefab}.prefab");
                    UnityEngine.Object.DestroyImmediate(go);
                    made++;
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(inst);
            }
            return $"{made} item prefabs in {Root}/Prefabs/Items";
        }

        // ------------------------------------------------------------------ materials (URP)
        static Material MakeMaterial(MatJ j)
        {
            switch (j.kind)
            {
                case "glass":
                {
                    var sh = Shader.Find("Van/Glass");
                    if (sh != null && !ShaderUtil.ShaderHasError(sh) && sh.isSupported)
                    {
                        var m = GetOrCreate(j.name, sh);
                        m.renderQueue = -1;
                        m.shaderKeywords = new string[0];
                        m.SetColor("_BaseColor", C(j.color, j.alpha));
                        m.SetTexture("_FrostMask", Tex("T_Van_Frost"));
                        m.SetTexture("_SnowMask", Tex("T_Van_Snow"));
                        m.SetTexture("_FogMask", Tex("T_Van_Fog"));
                        m.SetTexture("_WipeMap", Tex("T_Van_WipeMap"));
                        m.SetTexture("_Handprints", Tex("T_Van_Handprints"));
                        EditorUtility.SetDirty(m);
                        return m;
                    }
                    Debug.LogWarning("[UazVan] Shader Van/Glass is missing or has errors: " + j.name + " falls back to transparent Lit " +
                                     "(no frost, fog, prints or wipers).");
                    var f = GetOrCreate(j.name, LitShader());
                    f.SetColor("_BaseColor", C(j.color, j.alpha));
                    f.SetFloat("_Smoothness", j.smoothness);
                    MakeTransparent(f);
                    Validate(f);
                    return f;
                }
                default:
                {
                    var m = GetOrCreate(j.name, LitShader());
                    m.SetTexture("_BaseMap", string.IsNullOrEmpty(j.baseMap) ? null : Tex(j.baseMap));
                    m.SetColor("_BaseColor", j.kind == "mirror" ? new Color(0.06f, 0.065f, 0.07f, 1f) :
                                             j.kind == "display" ? C(j.color) : string.IsNullOrEmpty(j.baseMap) ? C(j.color) : Color.white);
                    m.SetFloat("_Smoothness", j.smoothness);
                    m.SetFloat("_Metallic", 0f);
                    m.SetTexture("_MetallicGlossMap", null);
                    m.DisableKeyword("_METALLICSPECGLOSSMAP");
                    m.SetTexture("_BumpMap", null);
                    m.DisableKeyword("_NORMALMAP");
                    if (j.kind == "emissive" || j.kind == "lens" || j.kind == "mirror" || j.kind == "display")
                    {
                        // the rig lights them per renderer (MaterialPropertyBlock _EmissionColor); off by default
                        m.SetTexture("_EmissionMap", string.IsNullOrEmpty(j.emissionMap) ? null : Tex(j.emissionMap));
                        m.SetColor("_EmissionColor", Color.black);
                        m.EnableKeyword("_EMISSION");
                        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    }
                    else
                    {
                        m.SetColor("_EmissionColor", Color.black);
                        m.DisableKeyword("_EMISSION");
                    }
                    Validate(m);
                    if (j.kind == "emissive" || j.kind == "lens" || j.kind == "mirror" || j.kind == "display")
                    {
                        m.EnableKeyword("_EMISSION");                    // URP's keyword pass drops it while the colour is black
                        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                        EditorUtility.SetDirty(m);
                    }
                    return m;
                }
            }
        }

        static Material MakeFx(string name, string shaderName)
        {
            var sh = Shader.Find(shaderName);
            if (sh == null || ShaderUtil.ShaderHasError(sh) || !sh.isSupported)
            {
                Debug.LogWarning($"[UazVan] Shader {shaderName} is missing, has errors or is not supported: no {name} (lamps work, " +
                                 "without glows / beams).");
                return null;
            }
            var mat = GetOrCreate(name, sh);
            mat.renderQueue = -1;
            mat.shaderKeywords = new string[0];
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Shader LitShader() => Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        static Material GetOrCreate(string name, Shader shader)
        {
            string path = $"{Root}/Materials/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader) mat.shader = shader;
            return mat;
        }

        static Texture2D Tex(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Textures/{name}.png");

        static void MakeTransparent(Material mat)
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
        }

        // URP sets its keywords in its own editor code: called by reflection (no package reference)
        static void Validate(Material m)
        {
            if (m.shader != null && m.shader.name == "Universal Render Pipeline/Lit")
                InvokeStatic("UnityEditor.Rendering.Universal.ShaderGUI.LitGUI", "SetMaterialKeywords", m);
            InvokeStatic("UnityEditor.BaseShaderGUI", "SetMaterialKeywords", m);
            EditorUtility.SetDirty(m);
        }

        static void InvokeStatic(string typeName, string method, Material m)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType(typeName);
                if (t == null) continue;
                foreach (var mi in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    var ps = mi.GetParameters();
                    if (mi.Name != method || ps.Length == 0 || ps[0].ParameterType != typeof(Material)) continue;
                    if (ps.Skip(1).Any(p => !p.IsOptional && p.ParameterType.IsValueType)) continue;
                    var args = new object[ps.Length];
                    args[0] = m;
                    for (int i = 1; i < ps.Length; i++) args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : null;
                    try { mi.Invoke(null, args); }
                    catch (Exception e) { Debug.LogWarning($"[UazVan] {typeName}.{method}: {e.InnerException?.Message ?? e.Message}"); }
                    return;
                }
            }
        }

        static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }

        // glows and beams read the camera depth texture in the transparent pass
        static void CheckUrpDepth()
        {
            var rp = GraphicsSettings.currentRenderPipeline;
            if (rp == null) return;
            var so = new SerializedObject(rp);
            var req = so.FindProperty("m_RequireDepthTexture");
            if (req != null && !req.boolValue)
                Debug.LogWarning("[UazVan] URP Asset: Depth Texture is off. The lamp glows and beams need it.");
        }

        static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }

        static int EnsureLayer(string name)
        {
            int existing = LayerMask.NameToLayer(name);
            if (existing >= 0) return existing;
            Debug.LogWarning($"[UazVan] Layer \"{name}\" not found: zones stay on Default (the game uses layer 8 Interact).");
            return -1;
        }
    }
}
#endif
