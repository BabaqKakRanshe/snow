using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
#if SKYISLAND_URP
using UnityEngine.Rendering.Universal;
#endif

namespace SkyIsland.EditorTools
{
    /// <summary>
    /// v2.6 light fixtures: lamps as SkyIslandLightFixture objects (lights and halos under the fixture, colour by temperature),
    /// URP rendering layers per room (renderers by layout.lighting.assign, lights by room), the project settings they need,
    /// the SkyIslandLightManager on the location root, and no Batching Static when the GPU Resident Drawer is on.
    /// </summary>
    public static partial class SkyIslandBuilder
    {
        /// <summary>Static flags of structures in the current build (StaticAll without Batching Static under the GPU Resident Drawer).</summary>
        static StaticEditorFlags s_StructureStatic = StaticAll;

        // ------------------------------------------------------------------ layout.lighting
        /// <summary>layout.lighting resolved for one build (null when the location has none).</summary>
        internal sealed class LightingInfo
        {
            public int exteriorLayer = 1;
            public uint defaultMask = 2u, dynamicMask = 1u;
            public int maxShadowed = 3;
            public float shadowRange = 14f, lodFade = 6f;
            public int maxBit;
            public readonly Dictionary<string, int> roomLayer = new Dictionary<string, int>();
            public readonly Dictionary<string, Dictionary<string, uint>> nodes = new Dictionary<string, Dictionary<string, uint>>();
            public readonly HashSet<string> unknownRooms = new HashSet<string>();

            public static LightingInfo From(LayoutData layout)
            {
                var lg = layout != null ? layout.lighting : null;
                if (lg == null || !lg.IsSet) return null;
                var li = new LightingInfo();
                li.exteriorLayer = lg.exteriorLayer > 0 ? Mathf.Min(lg.exteriorLayer, 31) : 1;
                li.defaultMask = lg.defaultMask != 0 ? unchecked((uint)lg.defaultMask) : 1u << li.exteriorLayer;
                li.dynamicMask = lg.dynamicMask != 0 ? unchecked((uint)lg.dynamicMask) : 1u;
                if (lg.maxShadowed != 0) li.maxShadowed = Mathf.Max(0, lg.maxShadowed);
                if (lg.shadowRange > 0f) li.shadowRange = lg.shadowRange;
                if (lg.lodFade > 0f) li.lodFade = lg.lodFade;
                uint all = li.defaultMask | li.dynamicMask | (1u << li.exteriorLayer);
                foreach (var r in lg.rooms)
                {
                    if (r == null || string.IsNullOrEmpty(r.name) || r.layer < 0 || r.layer > 31) continue;
                    li.roomLayer[r.name] = r.layer;
                    all |= 1u << r.layer;
                }
                if (lg.assign != null)
                    foreach (var a in lg.assign)
                    {
                        if (a == null || string.IsNullOrEmpty(a.model) || string.IsNullOrEmpty(a.node)) continue;
                        Dictionary<string, uint> map;
                        if (!li.nodes.TryGetValue(a.model, out map)) li.nodes[a.model] = map = new Dictionary<string, uint>();
                        uint m = unchecked((uint)a.mask);
                        map[a.node] = m;
                        all |= m;
                    }
                for (int b = 31; b >= 0; b--)
                    if ((all & (1u << b)) != 0) { li.maxBit = b; break; }
                return li;
            }

            /// <summary>Layers a light of this room lights: its room bit plus the dynamic bits; no or unknown room = everything.</summary>
            public uint LightMask(string room)
            {
                if (string.IsNullOrEmpty(room)) return uint.MaxValue;
                int bit;
                if (roomLayer.TryGetValue(room, out bit)) return (1u << bit) | dynamicMask;
                unknownRooms.Add(room);
                return uint.MaxValue;
            }

            public string LayerName(int bit)
            {
                if (bit == exteriorLayer) return "Exterior";
                var rooms = new List<string>();
                foreach (var kv in roomLayer) if (kv.Value == bit) rooms.Add(kv.Key);
                return rooms.Count > 0 && rooms.Count <= 3 ? $"Zone {bit} ({string.Join(", ", rooms)})" : $"Zone {bit}";
            }
        }

        // ------------------------------------------------------------------ project settings
        /// <summary>GPU Resident Drawer active (Unity 6 + an SRP asset with GPU Resident Drawer = Instanced Drawing).</summary>
        static bool GpuResidentDrawerOn()
        {
#if UNITY_6000_0_OR_NEWER && SKYISLAND_SRP_CORE
            var g = GraphicsSettings.currentRenderPipeline as IGPUResidentRenderPipeline;
            return g != null && g.gpuResidentDrawerMode != GPUResidentDrawerMode.Disabled;
#else
            return false;
#endif
        }

        /// <summary>Static flags for this build; logs once when the GPU Resident Drawer takes over from static batching.</summary>
        static void UpdateStructureStatic()
        {
            bool grd = GpuResidentDrawerOn();
            s_StructureStatic = grd ? StaticAll & ~StaticEditorFlags.BatchingStatic : StaticAll;
            if (grd)
                Debug.Log("[SkyIsland] GPU Resident Drawer is on: structures are built without Batching Static (the drawer instances them). " +
                          "Also turn off Project Settings > Player > Other Settings > Static Batching.");
        }

        /// <summary>Settings rendering layers per room need: URP Use Rendering Layers, named layers up to the highest bit used
        /// (only named layers count in URP), additional light shadows for the shadow budget.</summary>
        static void SetupLightingProject(Context ctx, LightingInfo li)
        {
            if (li == null) return;
            switch (ctx.pipe)
            {
                case Pipe.URP:
#if SKYISLAND_URP && UNITY_2022_2_OR_NEWER
                    int named = EnsureRenderingLayerNames(li);
                    int changed = 0;
                    foreach (var urp in UrpAssets())
                    {
                        if (SetAssetBool(urp, "m_SupportsLightLayers", true)) changed++;
                        if (li.maxShadowed > 0 && SetAssetBool(urp, "m_AdditionalLightShadowsSupported", true)) changed++;
                    }
                    if (changed > 0)
                    {
                        AssetDatabase.SaveAssets();
                        Debug.Log($"[SkyIsland] {ctx.id}: URP assets updated ({changed} settings): Lighting > Use Rendering Layers on" +
                                  (li.maxShadowed > 0 ? ", Additional Lights > Cast Shadows on." : "."));
                    }
                    Debug.Log($"[SkyIsland] {ctx.id}: rendering layers 0..{li.maxBit} used by {li.roomLayer.Count} light zones ({named} layer name(s) added).");
#else
                    Debug.LogWarning($"[SkyIsland] {ctx.id}: rendering layers per room need URP 14+ (Unity 2022.2 or newer). Without them every room lamp also lights the rooms behind its walls.");
#endif
                    break;
                case Pipe.HDRP:
                    Debug.LogWarning($"[SkyIsland] {ctx.id}: rendering layers per room are set up for URP only (HDRP Light Layers are not configured). Room lamps will light the rooms behind their walls.");
                    break;
                default:
                    Debug.LogWarning($"[SkyIsland] {ctx.id}: the Built-in pipeline has no rendering layers for lights: room lamps will light the rooms behind their walls. Use URP.");
                    break;
            }
        }

#if SKYISLAND_URP && UNITY_2022_2_OR_NEWER
        /// <summary>URP assets of the project: the active one, the default one and every quality level's.</summary>
        static List<UniversalRenderPipelineAsset> UrpAssets()
        {
            var list = new List<UniversalRenderPipelineAsset>();
            AddUrp(list, GraphicsSettings.currentRenderPipeline);
            AddUrp(list, GraphicsSettings.defaultRenderPipeline);
            for (int i = 0; i < QualitySettings.names.Length; i++) AddUrp(list, QualitySettings.GetRenderPipelineAssetAt(i));
            return list;
        }

        static void AddUrp(List<UniversalRenderPipelineAsset> list, RenderPipelineAsset a)
        {
            var u = a as UniversalRenderPipelineAsset;
            if (u != null && !list.Contains(u)) list.Add(u);
        }

        /// <summary>Sets a serialized bool of an asset; true when it changed.</summary>
        static bool SetAssetBool(UnityEngine.Object asset, string property, bool value)
        {
            var so = new SerializedObject(asset);
            var p = so.FindProperty(property);
            if (p == null || p.propertyType != SerializedPropertyType.Boolean)
            {
                Debug.LogWarning($"[SkyIsland] {asset.name}: setting '{property}' not found (URP version?); set it by hand.");
                return false;
            }
            if (p.boolValue == value) return false;
            p.boolValue = value;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(asset);
            return true;
        }

        /// <summary>Names every rendering layer from 1 to li.maxBit that has no name: URP lights only use named layers
        /// (Unity 6: Project Settings > Tags and Layers > Rendering Layers; 2022: URP Global Settings). Returns how many were added.</summary>
        static int EnsureRenderingLayerNames(LightingInfo li)
        {
            int added = 0;
#if UNITY_6000_0_OR_NEWER
            for (int i = 1; i <= li.maxBit; i++)
            {
                int count = RenderingLayerMask.GetRenderingLayerCount();
                string have = i < count ? RenderingLayerMask.RenderingLayerToName(i) : null;
                if (!string.IsNullOrEmpty(have)) continue;
                string name = li.LayerName(i);
                bool ok = i < count
                    ? UnityEditor.Rendering.RenderPipelineEditorUtility.TrySetRenderingLayerName(i, name)
                    : UnityEditor.Rendering.RenderPipelineEditorUtility.TryAddRenderingLayerName(name);
                if (!ok)
                {
                    Debug.LogWarning($"[SkyIsland] could not name rendering layer {i} ('{name}'): add Rendering Layers up to {li.maxBit} in Project Settings > Tags and Layers.");
                    break;
                }
                added++;
            }
#else
            var gs = GraphicsSettings.GetSettingsForRenderPipeline<UniversalRenderPipeline>();
            if (gs == null)
            {
                Debug.LogWarning("[SkyIsland] URP Global Settings asset not found: open Project Settings > Graphics > URP Global Settings once, then build again (rendering layers need names there).");
                return 0;
            }
            var so = new SerializedObject(gs);
            var names = so.FindProperty("m_RenderingLayerNames");
            if (names == null || !names.isArray)
            {
                Debug.LogWarning($"[SkyIsland] URP Global Settings: rendering layer names not found; add layers up to {li.maxBit} by hand.");
                return 0;
            }
            while (names.arraySize <= li.maxBit)
            {
                names.arraySize++;
                names.GetArrayElementAtIndex(names.arraySize - 1).stringValue = "";
            }
            for (int i = 1; i <= li.maxBit; i++)
            {
                var e = names.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(e.stringValue)) continue;
                e.stringValue = li.LayerName(i);
                added++;
            }
            if (so.hasModifiedProperties)
            {
                so.ApplyModifiedProperties();
                // URP keeps the valid-layer mask in a cache that its own inspector refreshes with this (internal) method
                var update = gs.GetType().GetMethod("UpdateRenderingLayerNames", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (update != null) update.Invoke(gs, null);
                else Debug.LogWarning("[SkyIsland] URP Global Settings: rendering layer names were added; restart the editor so URP picks them up.");
                EditorUtility.SetDirty(gs);
                AssetDatabase.SaveAssets();
            }
#endif
            return added;
        }
#endif

        /// <summary>URP rendering layers of a light: what it lights and whose renderers cast its shadows (no-op outside URP).</summary>
        static void SetLightRenderingLayers(Light l, uint mask)
        {
#if SKYISLAND_URP && UNITY_2022_2_OR_NEWER
            if (l == null || DetectPipeline() != Pipe.URP) return;
            l.GetUniversalAdditionalLightData().renderingLayers = mask;
#endif
        }

        // ------------------------------------------------------------------ lights and fixtures
        static bool UsesColorTemperature(Context ctx) => ctx.pipe != Pipe.BuiltIn || GraphicsSettings.lightsUseColorTemperature;

        /// <summary>Light colour and intensity from the def. With a colour temperature (k) the light uses Unity's temperature
        /// mode and the colour becomes the filter that keeps the exact preview colour: Unity multiplies the linear colour by its
        /// temperature colour, so filter = wanted / temperature colour (magnitude moved into the intensity).</summary>
        static void ApplyLightColor(Context ctx, Light l, LightDef d, float scale)
        {
            var c = J.Lin(d.c);
            if (d.k > 0f && UsesColorTemperature(ctx))
            {
                var t = Mathf.CorrelatedColorTemperatureToRGB(d.k);
                c = new Color(c.r / Mathf.Max(t.r, 1e-3f), c.g / Mathf.Max(t.g, 1e-3f), c.b / Mathf.Max(t.b, 1e-3f));
                l.useColorTemperature = true;
                l.colorTemperature = d.k;
            }
            float mx = Mathf.Max(c.maxColorComponent, 1e-4f);
            l.color = new Color(c.r / mx, c.g / mx, c.b / mx).gamma;
            l.intensity = d.i * mx * scale;
        }

        /// <summary>Name -> first transform with that name (depth first, like FindDeep).</summary>
        static Dictionary<string, Transform> NameIndex(Transform root)
        {
            var map = new Dictionary<string, Transform>();
            IndexNames(root, map);
            return map;
        }

        static void IndexNames(Transform t, Dictionary<string, Transform> map)
        {
            if (!map.ContainsKey(t.name)) map[t.name] = t;
            foreach (Transform c in t) IndexNames(c, map);
        }

        static bool HasFixtures(LightDef[] defs, GlowDef[] glows)
        {
            if (defs != null) foreach (var d in defs) if (d != null && !string.IsNullOrEmpty(d.fx)) return true;
            if (glows != null) foreach (var g in glows) if (g != null && !string.IsNullOrEmpty(g.fx)) return true;
            return false;
        }

        static SkyIsland.SkyIslandLightFixture.Kind ParseKind(string kind)
        {
            switch ((kind ?? "").ToLowerInvariant())
            {
                case "fluorescent": return SkyIsland.SkyIslandLightFixture.Kind.Fluorescent;
                case "halogen": return SkyIsland.SkyIslandLightFixture.Kind.Halogen;
                case "hps": return SkyIsland.SkyIslandLightFixture.Kind.HPS;
                case "mh": return SkyIsland.SkyIslandLightFixture.Kind.MH;
                case "led": return SkyIsland.SkyIslandLightFixture.Kind.LED;
                case "flame": return SkyIsland.SkyIslandLightFixture.Kind.Flame;
                case "exit": return SkyIsland.SkyIslandLightFixture.Kind.Exit;
                case "emergency": return SkyIsland.SkyIslandLightFixture.Kind.Emergency;
                case "beacon": return SkyIsland.SkyIslandLightFixture.Kind.Beacon;
                default: return SkyIsland.SkyIslandLightFixture.Kind.Incandescent;
            }
        }

        static float DefaultLod(SkyIsland.SkyIslandLightFixture.Kind kind)
        {
            switch (kind)
            {
                case SkyIsland.SkyIslandLightFixture.Kind.HPS:
                case SkyIsland.SkyIslandLightFixture.Kind.MH: return 90f;
                case SkyIsland.SkyIslandLightFixture.Kind.Halogen: return 70f;
                case SkyIsland.SkyIslandLightFixture.Kind.Beacon: return 60f;
                case SkyIsland.SkyIslandLightFixture.Kind.Exit: return 20f;
                default: return 25f;
            }
        }

        /// <summary>SkyIslandLightFixture on every model child named by a light's fx: type and behaviour from the first light
        /// of the fixture, its Lights and Glow_ halos (already parented under it) and its renderers. The power grid's lamp
        /// materials (layout.power materialsOff / On, e.g. a charging LED) stay with the grid. Returns the count.</summary>
        static int SetupFixtures(Context ctx, GameObject inst, string model, LightDef[] defs, Dictionary<string, Transform> index,
                                 Dictionary<string, Material> mats)
        {
            if (defs == null || index == null) return 0;
            var gridMats = new HashSet<Material>();
            var pw = ctx.layout.power;
            if (pw != null && mats != null)
                foreach (var list in new[] { pw.materialsOff, pw.materialsOn })
                    if (list != null)
                        foreach (var matName in list)
                        {
                            Material gm;
                            if (!string.IsNullOrEmpty(matName) && mats.TryGetValue(matName, out gm) && gm != null) gridMats.Add(gm);
                        }
            var first = new Dictionary<string, LightDef>();
            var order = new List<string>();
            foreach (var d in defs)
                if (d != null && !string.IsNullOrEmpty(d.fx) && !first.ContainsKey(d.fx)) { first[d.fx] = d; order.Add(d.fx); }
            int n = 0;
            var missing = new List<string>();
            foreach (var fx in order)
            {
                Transform t;
                if (!index.TryGetValue(fx, out t)) { missing.Add(fx); continue; }
                var d = first[fx];
                var f = t.GetComponent<SkyIsland.SkyIslandLightFixture>();
                if (f == null) f = t.gameObject.AddComponent<SkyIsland.SkyIslandLightFixture>();
                f.kind = ParseKind(d.kind);
                bool battery = string.Equals(d.circuit, "battery", StringComparison.OrdinalIgnoreCase);
                f.circuit = battery ? SkyIsland.SkyIslandLightFixture.Circuit.Battery
                    : (f.kind == SkyIsland.SkyIslandLightFixture.Kind.Flame ? SkyIsland.SkyIslandLightFixture.Circuit.None : SkyIsland.SkyIslandLightFixture.Circuit.Grid);
                f.room = d.room ?? "";
                f.switchable = !d.nosw && f.circuit == SkyIsland.SkyIslandLightFixture.Circuit.Grid;
                f.faulty = d.faulty;
                f.hero = d.hero;
                f.lodDistance = d.lod > 0f ? d.lod : DefaultLod(f.kind);
                bool mh = f.kind == SkyIsland.SkyIslandLightFixture.Kind.MH;
                f.warmup = d.warmup > 0f ? d.warmup : (mh ? 120f : 90f);
                f.restrike = d.restrike > 0f ? d.restrike : (mh ? 240f : 60f);
                f.flash = J.Has(d.flash, 2) ? new Vector2(d.flash[0], d.flash[1]) : Vector2.zero;
                f.lights = t.GetComponentsInChildren<Light>(true);
                var halos = new List<Renderer>();
                var parts = new List<Renderer>();
                foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                    if (r.name.StartsWith("Glow_", StringComparison.Ordinal)) halos.Add(r); else parts.Add(r);
                f.halos = halos.ToArray();
                f.emissive = parts.ToArray();
                var ignore = new List<Material>();
                foreach (var r in parts)
                    foreach (var m in r.sharedMaterials)
                        if (m != null && gridMats.Contains(m) && !ignore.Contains(m)) ignore.Add(m);
                f.ignoreMaterials = ignore.ToArray();
                n++;
            }
            if (missing.Count > 0)
                Debug.LogWarning($"[SkyIsland] {ctx.id}: model {model}: {missing.Count} fixture object(s) not found (e.g. {missing[0]}); their lights stay on the model root without a fixture.");
            return n;
        }

        /// <summary>Rendering layer mask of every renderer of a prefab: the mask of its nearest ancestor listed in
        /// layout.lighting.assign for this model, otherwise the location's default mask. Returns how many were changed.</summary>
        static int ApplyRenderingLayers(GameObject inst, string model, LightingInfo li)
        {
            if (li == null) return 0;
            Dictionary<string, uint> map;
            li.nodes.TryGetValue(model, out map);
            int n = 0;
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                uint mask = li.defaultMask;
                if (map != null)
                    for (var t = r.transform; t != null; t = t.parent)
                    {
                        uint m;
                        if (map.TryGetValue(t.name, out m)) { mask = m; break; }
                        if (t == inst.transform) break;
                    }
                if (r.renderingLayerMask != mask) { r.renderingLayerMask = mask; n++; }
            }
            return n;
        }

        // ------------------------------------------------------------------ scene
        static bool HasLighting(Context ctx)
        {
            var l = ctx.layout;
            if (l.lighting != null && l.lighting.IsSet) return true;
            if (l.prefabLights != null)
                foreach (var pl in l.prefabLights)
                    if (pl != null && HasFixtures(pl.lights, null)) return true;
            return false;
        }

        /// <summary>SkyIslandLightManager on the location root: rooms with their switch prefixes, power grid, light LOD and
        /// shadow budget (layout.lighting). Runs after AddPower so the grid exists.</summary>
        static void AddLighting(Context ctx, Transform root)
        {
            var fixtures = root.GetComponentsInChildren<SkyIsland.SkyIslandLightFixture>(true);
            var li = LightingInfo.From(ctx.layout);
            if (li == null && fixtures.Length == 0) return;
            var m = root.gameObject.AddComponent<SkyIsland.SkyIslandLightManager>();
            m.root = root;
            m.grid = root.GetComponent<SkyIsland.SkyIslandPowerGrid>();
            int switches = 0;
            var noSwitch = new List<string>();
            if (li != null)
            {
                var interactables = root.GetComponentsInChildren<SkyIsland.SkyIslandInteractable>(true);
                foreach (var r in ctx.layout.lighting.rooms)
                {
                    if (r == null || string.IsNullOrEmpty(r.name)) continue;
                    m.rooms.Add(new SkyIsland.SkyIslandLightManager.Room { name = r.name, layer = r.layer, switchPrefix = r.sw ?? "", startOn = true });
                    int found = 0;
                    if (!string.IsNullOrEmpty(r.sw))
                        foreach (var ia in interactables)
                            if (ia.name.StartsWith(r.sw, StringComparison.Ordinal)) found++;
                    if (found == 0) noSwitch.Add(r.name);
                    switches += found;
                }
                m.maxShadowed = li.maxShadowed;
                m.shadowRange = li.shadowRange;
                m.lodFade = li.lodFade;
                if (ctx.layout.prefabLights != null)
                    foreach (var pl in ctx.layout.prefabLights)
                        if (pl != null && pl.lights != null)
                            foreach (var d in pl.lights) if (d != null) li.LightMask(d.room);   // collects unknown rooms
            }
            var kinds = new SortedDictionary<string, int>();
            int lights = 0, hero = 0;
            foreach (var f in fixtures)
            {
                string k = f.kind.ToString();
                int c;
                kinds[k] = kinds.TryGetValue(k, out c) ? c + 1 : 1;
                lights += f.lights != null ? f.lights.Length : 0;
                if (f.hero) hero++;
            }
            var parts = new List<string>();
            foreach (var kv in kinds) parts.Add($"{kv.Value} {kv.Key}");
            Debug.Log($"[SkyIsland] {ctx.id}: light manager: {fixtures.Length} fixtures ({string.Join(", ", parts)}), {lights} lights, {hero} hero, " +
                      $"{m.rooms.Count} rooms, {switches} switches, grid: {(m.grid != null ? "yes" : "none")}, shadows: {m.maxShadowed} within {m.shadowRange} m.");
            if (noSwitch.Count > 0) Debug.Log($"[SkyIsland] {ctx.id}: rooms without a light switch (always on with power): {string.Join(", ", noSwitch)}.");
            if (li != null && li.unknownRooms.Count > 0)
                Debug.LogWarning($"[SkyIsland] {ctx.id}: lights name rooms that layout.lighting does not list ({string.Join(", ", li.unknownRooms)}): they light every layer.");
        }
    }
}
