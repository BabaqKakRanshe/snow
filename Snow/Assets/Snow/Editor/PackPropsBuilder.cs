using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Snow.Cargo;
using Snow.Levels.Forest;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Snow.EditorTools
{
    /// <summary>
    /// Tools / Snow / Packs / Build …: the Blender packs made after forest_start (rocks, trash, devices, parts, gear, world,
    /// sky_systems, bunker) as prefabs, the way <see cref="StartPropsBuilder"/> builds forest_start. Reads &lt;Pack&gt;_Unity.json
    /// (tools/forest_packs/make_unity_data.py; the FBX come from tools/forest_packs/export_fbx.py). Textures: point, no
    /// mipmaps, uncompressed (tiled ones repeat). Materials from the pack's .blend data: URP Lit like the kit's (PSX: no
    /// specular, smoothness 0), glass alpha blended, cut-outs clipped, lamps / LEDs / displays lit and _Off as separate
    /// materials - the prefab gets the _Off one. Prefabs: the parts the export lifted go back under their parents, box colliders
    /// of the contract (turned ones and the HIT_ triggers on their own child), MeshColliders (rocks, the cave), sockets as empty
    /// children, kit instances nested, lights switched off, parts standing at their contract rest pose; no interaction.
    /// Item_* are network cargo like forest_items (Rigidbody by mass_kg, NetworkObject, server NetworkTransform,
    /// NetworkRigidbody, CargoItem) and listed in DefaultNetworkPrefabs. Re-running rewrites the same assets (same GUIDs).
    /// </summary>
    public static class PackPropsBuilder
    {
        const string LightLayersFrom = "Assets/Locations/Forest/Prefabs/LightPole.prefab";
        const string PrefabListPath = "Assets/DefaultNetworkPrefabs.asset";
        const float MinSide = 0.02f;

        public static readonly string[] DataFiles =
        {
            "Assets/Locations/Forest/Rocks/Rocks_Unity.json", "Assets/Locations/Forest/Trash/Trash_Unity.json",
            "Assets/Locations/Forest/Devices/Devices_Unity.json", "Assets/Locations/Forest/Parts/Parts_Unity.json",
            "Assets/Locations/Forest/Gear/Gear_Unity.json", "Assets/Locations/Forest/World/World_Unity.json",
            "Assets/SkyIsland/Systems/Sky_Unity.json", "Assets/Locations/Bunker/Bunker_Unity.json",
        };

        [Serializable] public class Data { public string version, pack, root; public Mat[] materials; public Remap[] remap; public Tex[] textures; public Item[] items; public Attach[] attach; }
        [Serializable] public class Mat { public string name, @base, mode, emissionMap; public float[] color, emissionColor; public float alpha, emission; public bool twoSided; }
        [Serializable] public class Remap { public string from, to; }
        [Serializable] public class Tex { public string name; public bool repeat; }
        [Serializable]
        public class Item
        {
            public string name, fbx, kind, label;
            public float mass;
            public int tris;
            public Col[] colliders;
            public MeshCol[] meshColliders;
            public Socket[] sockets;
            public Parent[] parents;
            public LightDef[] lights;
            public string[] hidden;
            public Kit[] kit;
            public Pose[] poses;
        }
        [Serializable] public class Col { public string name, parent; public float[] center, size, euler; public bool trigger; }
        [Serializable] public class MeshCol { public string @object; public bool convex; }
        [Serializable] public class Socket { public string name, parent, kind; public float[] pos, dir, euler; public bool hasEuler; }
        [Serializable] public class Parent { public string child, parent; }
        [Serializable] public class LightDef { public string name, parent; public bool spot; public float angle, intensity, range; public float[] pos, dir, color; }
        [Serializable] public class Kit { public string prefab, parent; public float[] pos, rot; }
        [Serializable] public class Pose { public string @object; public float[] pivot, axis; public float angle; }
        [Serializable] public class Attach { public string prefab, host; public float[] pos, rot; public bool active, spawn; }

        [MenuItem("Tools/Snow/Packs/Build All Packs")] static void MenuAll() => BuildAll();
        [MenuItem("Tools/Snow/Packs/Build Rocks")] static void MenuRocks() => Build(DataFiles[0]);
        [MenuItem("Tools/Snow/Packs/Build Trash")] static void MenuTrash() => Build(DataFiles[1]);
        [MenuItem("Tools/Snow/Packs/Build Devices")] static void MenuDevices() => Build(DataFiles[2]);
        [MenuItem("Tools/Snow/Packs/Build Parts")] static void MenuParts() => Build(DataFiles[3]);
        [MenuItem("Tools/Snow/Packs/Build Gear")] static void MenuGear() => Build(DataFiles[4]);
        [MenuItem("Tools/Snow/Packs/Build World")] static void MenuWorld() => Build(DataFiles[5]);
        [MenuItem("Tools/Snow/Packs/Build Sky Systems")] static void MenuSky() => Build(DataFiles[6]);
        [MenuItem("Tools/Snow/Packs/Build Bunker")] static void MenuBunker() => Build(DataFiles[7]);

        public static bool BuildAll()
        {
            bool ok = true;
            foreach (string f in DataFiles) ok &= Build(f);
            return ok;
        }

        public static Data Read(string dataPath)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(dataPath);
            return asset != null ? JsonUtility.FromJson<Data>(asset.text) : null;
        }

        public static bool Build(string dataPath)
        {
            Data data = Read(dataPath);
            if (data?.items == null || data.items.Length == 0) { Debug.LogError($"[Snow] Packs: {dataPath} not found or empty."); return false; }
            string root = data.root;
            var problems = new List<string>();
            int tex = SetupTextures(data, root);
            Directory.CreateDirectory(root + "/Materials");
            Directory.CreateDirectory(root + "/Prefabs");
            var mats = new Dictionary<string, Material>(StringComparer.Ordinal);
            foreach (Mat m in data.materials) mats[m.name] = MakeMaterial(m, root);
            var remap = (data.remap ?? new Remap[0]).ToDictionary(r => r.from, r => r.to, StringComparer.Ordinal);
            int models = data.items.Count(it => SetupModel(it, mats, remap, problems));
            var built = new List<GameObject>();
            foreach (Item it in data.items)
            {
                GameObject p = BuildPrefab(it, root, mats, problems);
                if (p != null) built.Add(p);
            }
            int listed = ListNetworkItems(built.Where(p => p.TryGetComponent(out NetworkObject _)).ToList());
            AssetDatabase.SaveAssets();
            Debug.Log($"[Snow] Packs {data.pack} {data.version}: {tex} textures, {mats.Count} materials, {models} models, {built.Count} prefabs in {root}" +
                      (listed > 0 ? $", {listed} network items listed" : "") + ".");
            foreach (string p in problems) Debug.LogWarning($"[Snow] Packs {data.pack}: " + p);
            return problems.Count == 0 && built.Count == data.items.Length;
        }

        // ------------------------------------------------------------------ textures
        static int SetupTextures(Data data, string root)
        {
            int n = 0;
            foreach (Tex t in data.textures ?? new Tex[0])
            {
                string path = $"{root}/Textures/{t.name}";
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) { Debug.LogWarning($"[Snow] Packs: texture {path} missing."); continue; }
                TextureWrapMode wrap = t.repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                bool dirty = ti.textureType != TextureImporterType.Default || ti.textureShape != TextureImporterShape.Texture2D ||
                             ti.filterMode != FilterMode.Point || ti.mipmapEnabled || ti.textureCompression != TextureImporterCompression.Uncompressed ||
                             !ti.sRGBTexture || ti.wrapMode != wrap || ti.npotScale != TextureImporterNPOTScale.None || !ti.alphaIsTransparency;
                if (dirty)
                {
                    ti.textureType = TextureImporterType.Default;
                    ti.textureShape = TextureImporterShape.Texture2D;
                    ti.filterMode = FilterMode.Point;
                    ti.mipmapEnabled = false;
                    ti.textureCompression = TextureImporterCompression.Uncompressed;
                    ti.sRGBTexture = true;
                    ti.wrapMode = wrap;
                    ti.npotScale = TextureImporterNPOTScale.None;
                    ti.alphaIsTransparency = true;
                    ti.SaveAndReimport();
                }
                n++;
            }
            return n;
        }

        // ------------------------------------------------------------------ materials
        static Color Linear(float[] c) => c != null && c.Length >= 3 ? new Color(c[0], c[1], c[2]) : Color.white;

        static void Kw(Material m, string kw, bool on) { if (on) m.EnableKeyword(kw); else m.DisableKeyword(kw); }

        /// <summary>URP Lit as the kit's (PSX: no specular, no reflections, smoothness 0); colours of the .blend are linear.</summary>
        static Material MakeMaterial(Mat d, string root)
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            string path = $"{root}/Materials/{d.name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(lit) { name = d.name };
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != lit) m.shader = lit;
            Texture2D albedo = string.IsNullOrEmpty(d.@base) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>($"{root}/Textures/{d.@base}");
            Texture2D emap = string.IsNullOrEmpty(d.emissionMap) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>($"{root}/Textures/{d.emissionMap}");
            bool transparent = d.mode == "transparent", cutout = d.mode == "cutout";
            Color baseColor = Linear(d.color).gamma;
            baseColor.a = transparent ? d.alpha : 1f;
            m.SetFloat("_WorkflowMode", 1f);
            m.SetTexture("_BaseMap", albedo);
            m.SetTexture("_MainTex", albedo);
            m.SetColor("_BaseColor", baseColor);
            m.SetColor("_Color", baseColor);
            m.SetFloat("_Smoothness", 0f);
            m.SetFloat("_Metallic", 0f);
            Kw(m, "_METALLICSPECGLOSSMAP", false);
            Kw(m, "_NORMALMAP", false);
            bool emissive = d.emission > 0f;
            Color e = Linear(d.emissionColor) * d.emission;
            m.SetColor("_EmissionColor", emissive ? e.gamma : Color.black);
            m.SetTexture("_EmissionMap", emissive ? emap : null);
            Kw(m, "_EMISSION", emissive);
            m.globalIlluminationFlags = emissive ? MaterialGlobalIlluminationFlags.RealtimeEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            m.SetFloat("_AlphaClip", cutout ? 1f : 0f);
            m.SetFloat("_Cutoff", 0.5f);
            Kw(m, "_ALPHATEST_ON", cutout);
            m.SetFloat("_Surface", transparent ? 1f : 0f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", transparent ? (float)BlendMode.SrcAlpha : 1f);
            m.SetFloat("_DstBlend", transparent ? (float)BlendMode.OneMinusSrcAlpha : 0f);
            m.SetFloat("_ZWrite", transparent ? 0f : 1f);
            Kw(m, "_SURFACE_TYPE_TRANSPARENT", transparent);
            m.SetOverrideTag("RenderType", transparent ? "Transparent" : cutout ? "TransparentCutout" : "Opaque");
            m.renderQueue = transparent ? (int)RenderQueue.Transparent : cutout ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;
            m.SetFloat("_Cull", d.twoSided ? 0f : 2f);
            m.SetFloat("_ReceiveShadows", 1f);
            m.SetFloat("_EnvironmentReflections", 0f);
            m.SetFloat("_SpecularHighlights", 0f);
            Kw(m, "_ENVIRONMENTREFLECTIONS_OFF", true);
            Kw(m, "_SPECULARHIGHLIGHTS_OFF", true);
            m.SetShaderPassEnabled("ShadowCaster", !transparent && !emissive);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ models
        static bool SetupModel(Item it, Dictionary<string, Material> mats, Dictionary<string, string> remap, List<string> problems)
        {
            if (!(AssetImporter.GetAtPath(it.fbx) is ModelImporter mi)) { problems.Add($"{it.fbx} not found"); return false; }
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.None;
            mi.addCollider = false;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importCameras = false;
            mi.importLights = false;
            mi.isReadable = it.meshColliders != null && it.meshColliders.Length > 0;    // MeshColliders cook from it
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            foreach (var kv in mats)
            {
                Material target = remap.TryGetValue(kv.Key, out string to) && mats.TryGetValue(to, out Material off) ? off : kv.Value;
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), target);
            }
            mi.SaveAndReimport();
            return true;
        }

        // ------------------------------------------------------------------ prefabs
        static Vector3 V(float[] a) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;

        static Quaternion Q(float[] a) => a != null && a.Length >= 4 ? new Quaternion(a[0], a[1], a[2], a[3]) : Quaternion.identity;

        static Transform Deep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                Transform f = Deep(c, name);
                if (f != null) return f;
            }
            return null;
        }

        static Quaternion Look(Vector3 dir)
        {
            if (dir.sqrMagnitude < 1e-8f) return Quaternion.identity;
            Vector3 up = Mathf.Abs(dir.normalized.y) > 0.95f ? Vector3.forward : Vector3.up;
            return Quaternion.LookRotation(dir, up);
        }

        /// <summary>A child at a pose given in the root frame, then moved under its parent (world pose kept).</summary>
        static Transform Child(string name, Transform root, Transform parent, Vector3 pos, Quaternion rot)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            if (parent != root) go.transform.SetParent(parent, true);
            return go.transform;
        }

        static GameObject BuildPrefab(Item it, string folder, Dictionary<string, Material> mats, List<string> problems)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(it.fbx);
            if (model == null) { problems.Add($"{it.name}: no model {it.fbx}"); return null; }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                inst.name = it.name;
                Transform root = inst.transform;
                Transform Find(string n) => n == it.name ? root : Deep(root, n);
                bool item = it.kind == "item";

                // the parts the export lifted to the root go back under their parents (world pose kept)
                foreach (Parent p in it.parents ?? new Parent[0])
                {
                    Transform c = Find(p.child), par = Find(p.parent);
                    if (c == null || par == null) { problems.Add($"{it.name}: parent {p.parent} of {p.child} missing"); continue; }
                    c.SetParent(par, true);
                }
                foreach (Collider c in inst.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);

                var transparent = new HashSet<Material>(mats.Values.Where(m => m.renderQueue >= (int)RenderQueue.Transparent));
                foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    bool see = r.sharedMaterials.Any(x => x != null && transparent.Contains(x));
                    r.shadowCastingMode = see ? ShadowCastingMode.Off : ShadowCastingMode.On;
                    r.receiveShadows = !see;
                    if (!item) r.renderingLayerMask = 2u;                 // the map's lights (as the Forest prefabs)
                }

                foreach (MeshCol mc in it.meshColliders ?? new MeshCol[0])
                {
                    Transform t = Find(mc.@object);
                    if (t == null || !t.TryGetComponent(out MeshFilter mf)) { problems.Add($"{it.name}: mesh collider {mc.@object} missing"); continue; }
                    var col = t.gameObject.AddComponent<MeshCollider>();
                    col.sharedMesh = mf.sharedMesh;
                    col.convex = mc.convex;
                    if (mc.@object.StartsWith("COLM_", StringComparison.Ordinal))
                    {
                        // a collision shell only: no renderer
                        if (t.TryGetComponent(out MeshRenderer mr)) Object.DestroyImmediate(mr);
                    }
                }
                foreach (Col c in it.colliders ?? new Col[0])
                {
                    Transform parent = Find(c.parent);
                    if (parent == null) { problems.Add($"{it.name}: collider {c.name} parent {c.parent} missing"); parent = root; }
                    Vector3 size = V(c.size);
                    if (item) size = new Vector3(Mathf.Max(size.x, MinSide), Mathf.Max(size.y, MinSide), Mathf.Max(size.z, MinSide));
                    Transform t = Child(c.name, root, parent, V(c.center), Quaternion.Euler(V(c.euler)));
                    var box = t.gameObject.AddComponent<BoxCollider>();
                    box.size = size;
                    box.isTrigger = c.trigger;
                }
                foreach (Socket s in it.sockets ?? new Socket[0])
                {
                    Transform parent = Find(s.parent) ?? root;
                    Quaternion rot = s.hasEuler ? Quaternion.Euler(V(s.euler)) : Look(V(s.dir));
                    Child(s.name, root, parent, V(s.pos), rot);
                }
                foreach (Kit k in it.kit ?? new Kit[0])
                {
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(k.prefab);
                    if (asset == null) { problems.Add($"{it.name}: kit prefab {k.prefab} missing"); continue; }
                    Transform parent = Find(k.parent) ?? root;
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, root);
                    go.transform.localPosition = V(k.pos);
                    go.transform.localRotation = Q(k.rot);
                    if (parent != root) go.transform.SetParent(parent, true);
                }
                AddLights(it, root, Find);
                foreach (Pose p in it.poses ?? new Pose[0])
                {
                    Transform t = Find(p.@object);
                    if (t == null) { problems.Add($"{it.name}: posed part {p.@object} missing"); continue; }
                    Vector3 pivot = root.TransformPoint(V(p.pivot)), axis = root.TransformDirection(V(p.axis).normalized);
                    t.RotateAround(pivot, axis, p.angle);
                }
                foreach (string h in it.hidden ?? new string[0])
                {
                    Transform t = Find(h);
                    if (t != null) t.gameObject.SetActive(false);
                }
                if (item) MakeItem(it, inst);
                else
                {
                    var flags = StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
                    foreach (Transform t in inst.GetComponentsInChildren<Transform>(true))
                        if (PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject) == null) GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
                }
                CheckTris(it, inst, problems);
                return PrefabUtility.SaveAsPrefabAsset(inst, $"{folder}/Prefabs/{it.name}.prefab");
            }
            finally { Object.DestroyImmediate(inst); }
        }

        static void AddLights(Item it, Transform root, Func<string, Transform> find)
        {
            if (it.lights == null || it.lights.Length == 0) return;
            Light layersFrom = AssetDatabase.LoadAssetAtPath<GameObject>(LightLayersFrom)?.GetComponentInChildren<Light>(true);
            foreach (LightDef ld in it.lights)
            {
                Transform parent = find(ld.parent) ?? root;
                Transform t = Child(ld.name, root, parent, V(ld.pos), Look(V(ld.dir)));
                var l = t.gameObject.AddComponent<Light>();
                l.type = ld.spot ? LightType.Spot : LightType.Point;
                if (ld.spot)
                {
                    l.spotAngle = Mathf.Clamp(ld.angle, 1f, 179f);
                    l.innerSpotAngle = l.spotAngle * 0.55f;
                }
                Color c = Linear(ld.color);
                float mx = Mathf.Max(c.maxColorComponent, 1e-4f);
                l.color = new Color(c.r / mx, c.g / mx, c.b / mx).gamma;
                l.intensity = ld.intensity;
                l.range = ld.range;
                l.shadows = LightShadows.None;
                l.lightmapBakeType = LightmapBakeType.Realtime;
                if (layersFrom != null)
                {
                    l.renderingLayerMask = layersFrom.renderingLayerMask;
                    if (layersFrom.TryGetComponent(out UniversalAdditionalLightData src))
                    {
                        if (!t.TryGetComponent(out UniversalAdditionalLightData dst)) dst = t.gameObject.AddComponent<UniversalAdditionalLightData>();
                        dst.renderingLayers = src.renderingLayers;
                    }
                }
                l.enabled = false;                                       // switched off: the game turns it on
            }
        }

        /// <summary>A carried thing as forest_items' (ForestItemPrefabs.BuildItem): network cargo on layer Default.</summary>
        static void MakeItem(Item it, GameObject go)
        {
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = Mathf.Max(it.mass, 0.001f);
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            go.AddComponent<NetworkObject>();
            var nt = go.AddComponent<NetworkTransform>();
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            go.AddComponent<NetworkRigidbody>();
            var cargo = go.AddComponent<CargoItem>();
            cargo.label = it.label;
            cargo.itemKey = it.name;
            cargo.massKg = it.mass;
            var tag = go.AddComponent<ForestItemTag>();
            tag.model = it.name;
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
        }

        static void CheckTris(Item it, GameObject go, List<string> problems)
        {
            int tris = 0;
            foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.name.StartsWith("COLM_", StringComparison.Ordinal)) continue;
                if (PrefabUtility.GetNearestPrefabInstanceRoot(mf.gameObject) != null) continue;   // kit instances
                tris += mf.sharedMesh.triangles.Length / 3;
            }
            if (tris != it.tris) problems.Add($"{it.name}: {tris} triangles, the pack says {it.tris}");
        }

        static int ListNetworkItems(List<GameObject> prefabs)
        {
            if (prefabs.Count == 0) return 0;
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);
            if (list == null) { Debug.LogWarning($"[Snow] Packs: {PrefabListPath} missing; items not listed."); return 0; }
            foreach (GameObject p in prefabs)
                if (!list.PrefabList.Any(x => x.Prefab == p)) list.Add(new NetworkPrefab { Prefab = p });
            EditorUtility.SetDirty(list);
            return prefabs.Count(p => list.PrefabList.Any(x => x.Prefab == p));
        }
    }
}
