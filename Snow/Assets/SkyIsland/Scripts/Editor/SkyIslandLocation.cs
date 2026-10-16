using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SkyIsland.EditorTools
{
    /// <summary>Data contract v2 scene parts: giants and the layout extras (flickers, UV scroll, emitters, water, fog events, spawn).
    /// Rotators are added to the prefabs (SkyIslandPrefabs.cs) so every placed instance spins.</summary>
    public static partial class SkyIslandBuilder
    {
        // ------------------------------------------------------------------ giants
        /// <summary>Posed skeleton instances (layout giants) under a "Giants" group.</summary>
        static void BuildGiants(Context ctx, Transform root, Dictionary<string, Material> mats, AxisFix fix)
        {
            var group = new GameObject("Giants").transform;
            group.SetParent(root, false);
            int built = 0;
            for (int i = 0; i < ctx.layout.giants.Length; i++)
            {
                var g = ctx.layout.giants[i];
                if (g != null && BuildGiant(ctx, group, g, i, mats, fix) != null) built++;
            }
            Debug.Log($"[SkyIsland] {ctx.id}: {built}/{ctx.layout.giants.Length} giants placed.");
        }

        static GameObject BuildGiant(Context ctx, Transform group, GiantDef g, int index, Dictionary<string, Material> mats, AxisFix fix)
        {
            var model = ResolveModel(ctx, g.model);
            if (model == null)
            {
                Debug.LogWarning($"[SkyIsland] {ctx.id}: giant model '{g.model}' not found ({ModelPathHint(ctx, g.model)}).");
                return null;
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, group);
            go.name = string.IsNullOrEmpty(g.name) ? "Giant_" + index : g.name;
            go.transform.localPosition = fix.P(J.V3(g.p));
            go.transform.localRotation = fix.Q(J.Q(g.q));
            float s = (g.s > 0f ? g.s : 1f) * fix.unit;
            go.transform.localScale = new Vector3(s, s, s);

            int posed = PoseBones(ctx, go.transform, g.bones);
            int hidden = HideChildren(ctx, go.transform, g.hide);
            AssignGiantMaterial(ctx, go, g.material, mats);
            SetStatic(go, s_StructureStatic);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;
            }
            int added = EnsureMeshColliders(go);
            Debug.Log($"[SkyIsland] {ctx.id}: giant {go.name}: {posed} bones posed, {hidden} hidden, {added} mesh colliders added.");
            return go;
        }

        /// <summary>Model asset: "shared:Name" = &lt;SkyIsland&gt;/Models/Name.fbx, plain names = &lt;location&gt;/Models.</summary>
        static GameObject ResolveModel(Context ctx, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string folder = ctx.root + "/Models", file = name;
            if (name.StartsWith(SharedPrefix, System.StringComparison.Ordinal))
            {
                folder = (ctx.coreRoot ?? ctx.root) + "/Models";
                file = name.Substring(SharedPrefix.Length);
            }
            var go = AssetDatabase.LoadAssetAtPath<GameObject>($"{folder}/{file}.fbx");
            if (go != null) return go;
            if (!AssetDatabase.IsValidFolder(folder)) return null;
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(p) == file) return AssetDatabase.LoadAssetAtPath<GameObject>(p);
            }
            return null;
        }

        static string ModelPathHint(Context ctx, string name)
        {
            if (string.IsNullOrEmpty(name)) return "no model name";
            return name.StartsWith(SharedPrefix, System.StringComparison.Ordinal)
                ? $"{ctx.coreRoot}/Models/{name.Substring(SharedPrefix.Length)}.fbx - update the SkyIsland Core package"
                : $"{ctx.root}/Models/{name}.fbx";
        }

        /// <summary>
        /// Bone poses are in the giant root's local space. Bones that are direct children of the root get them as
        /// localPosition / localRotation; nested bones are converted through their (already posed) parents.
        /// Bones are matched like FindDeep (first match, depth first); unlisted bones keep their rest transform.
        /// </summary>
        static int PoseBones(Context ctx, Transform giant, BonePose[] bones)
        {
            if (bones == null || bones.Length == 0) return 0;
            var poses = new Dictionary<string, BonePose>();
            foreach (var b in bones)
                if (b != null && !string.IsNullOrEmpty(b.name)) poses[b.name] = b;
            var done = new HashSet<string>();
            foreach (var t in giant.GetComponentsInChildren<Transform>(true))   // depth first: parents before children
            {
                if (t == giant || done.Contains(t.name) || !poses.TryGetValue(t.name, out var bp)) continue;
                done.Add(t.name);
                bool hasP = J.Has(bp.p, 3), hasQ = J.Has(bp.q, 4);
                var q = hasQ ? NormalizedQ(bp.q) : Quaternion.identity;
                if (t.parent == giant)
                {
                    if (hasP) t.localPosition = J.V3(bp.p);
                    if (hasQ) t.localRotation = q;
                }
                else
                {
                    if (hasP) t.position = giant.TransformPoint(J.V3(bp.p));
                    if (hasQ) t.rotation = giant.rotation * q;
                }
            }
            if (done.Count < poses.Count)
            {
                var missing = new StringBuilder();
                foreach (var n in poses.Keys)
                    if (!done.Contains(n)) missing.Append(missing.Length > 0 ? ", " : "").Append(n);
                Debug.LogWarning($"[SkyIsland] {ctx.id}: giant {giant.name}: bones not found in the model: {missing}");
            }
            return done.Count;
        }

        static Quaternion NormalizedQ(float[] a)
        {
            var q = J.Q(a);
            float n = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return n > 1e-6f ? new Quaternion(q.x / n, q.y / n, q.z / n, q.w / n) : Quaternion.identity;
        }

        static int HideChildren(Context ctx, Transform giant, string[] names)
        {
            if (names == null) return 0;
            int n = 0;
            foreach (var name in names)
            {
                if (string.IsNullOrEmpty(name)) continue;
                var t = FindDeep(giant, name);
                if (t == null || t == giant) { Debug.LogWarning($"[SkyIsland] {ctx.id}: giant {giant.name}: hidden bone '{name}' not found."); continue; }
                t.gameObject.SetActive(false);
                n++;
            }
            return n;
        }

        /// <summary>The location material (Materials.json name) on every renderer slot of the giant.</summary>
        static void AssignGiantMaterial(Context ctx, GameObject giant, string material, Dictionary<string, Material> mats)
        {
            if (string.IsNullOrEmpty(material)) { Debug.LogWarning($"[SkyIsland] {ctx.id}: giant {giant.name} has no material; the model materials are kept."); return; }
            if (!mats.TryGetValue(material, out var m) || m == null)
            {
                Debug.LogWarning($"[SkyIsland] {ctx.id}: giant material '{material}' is not in {ctx.id}_Materials.json; the model materials are kept.");
                return;
            }
            foreach (var r in giant.GetComponentsInChildren<Renderer>(true))
            {
                var slots = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                for (int i = 0; i < slots.Length; i++) slots[i] = m;
                r.sharedMaterials = slots;
            }
        }

        /// <summary>Keeps the import MeshColliders; adds one to every mesh that has no collider.</summary>
        static int EnsureMeshColliders(GameObject go)
        {
            int n = 0;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.GetComponent<Collider>() != null) continue;
                mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                n++;
            }
            return n;
        }

        // ------------------------------------------------------------------ extras
        static bool HasLocationExtras(Context ctx)
        {
            var l = ctx.layout;
            return (l.flickers != null && l.flickers.Length > 0) || (l.uvScroll != null && l.uvScroll.Length > 0) ||
                   (l.emitters != null && l.emitters.Length > 0) || (l.water != null && l.water.IsSet) ||
                   (l.fogEvents != null && l.fogEvents.Length > 0) || (l.spawn != null && l.spawn.IsSet) ||
                   (l.power != null && l.power.IsSet) || (l.anchors != null && l.anchors.Length > 0) || HasLighting(ctx);
        }

        /// <summary>Runtime controllers on the location root (flicker, UV scroll, water level), emitters, fog event volumes, spawn,
        /// anchors, power grid and (v2.6) the light manager.
        /// Emitters come before the water so water.flowObjects can name them ("Emitter_&lt;type&gt;_&lt;index&gt;").</summary>
        static void AddLocationExtras(Context ctx, Transform root, Dictionary<string, Material> mats, Camera cam, AxisFix fix)
        {
            AddFlickers(ctx, root, mats);
            AddUVScroll(ctx, root, mats);
            AddEmitters(ctx, root, fix);
            AddWater(ctx, root);
            AddFogEvents(ctx, root, cam, fix);
            AddSpawn(ctx, root, fix);
            AddAnchors(ctx, root, fix);
            AddPower(ctx, root, mats);
            AddLighting(ctx, root);   // v2.6, after the power grid it listens to
        }

        /// <summary>v2.3 power -> SkyIslandPowerGrid on the location root (lamps off until the generator and the main switch are on).</summary>
        static void AddPower(Context ctx, Transform root, Dictionary<string, Material> mats)
        {
            var p = ctx.layout.power;
            if (p == null || !p.IsSet) return;
            var c = root.gameObject.AddComponent<SkyIsland.SkyIslandPowerGrid>();
            c.root = root;
            c.lightPrefix = p.lightPrefix ?? "";
            c.glowPrefix = p.glowPrefix ?? "";
            c.switches = p.switches ?? new string[0];
            c.startPowered = p.startPowered;
            int n = Mathf.Min(p.materialsOff?.Length ?? 0, p.materialsOn?.Length ?? 0);
            for (int i = 0; i < n; i++)
            {
                if (!mats.TryGetValue(p.materialsOff[i], out var off) || off == null || !mats.TryGetValue(p.materialsOn[i], out var on) || on == null)
                {
                    Debug.LogWarning($"[SkyIsland] {ctx.id}: power material pair '{p.materialsOff[i]}' -> '{p.materialsOn[i]}' not found.");
                    continue;
                }
                c.materials.Add(new SkyIsland.SkyIslandPowerGrid.MaterialPair { off = off, on = on });
            }
            int lights = 0, glows = 0;
            foreach (var l in root.GetComponentsInChildren<Light>(true))
                if (!string.IsNullOrEmpty(c.lightPrefix) && l.name.StartsWith(c.lightPrefix)) lights++;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (!string.IsNullOrEmpty(c.glowPrefix) && t.name.StartsWith(c.glowPrefix)) glows++;
            Debug.Log($"[SkyIsland] {ctx.id}: power grid: {lights} lights, {glows} halos, {c.materials.Count} lamp material pairs, switches: {string.Join(", ", c.switches)}.");
            // v2.7: powered interactables (shutter motors and their switches) get the grid directly (no search at run time)
            int powered = 0;
            foreach (var ia in root.GetComponentsInChildren<SkyIsland.SkyIslandInteractable>(true))
                if (ia.needsPower)
                {
                    ia.grid = c; powered++;
                    if (PrefabUtility.IsPartOfPrefabInstance(ia)) PrefabUtility.RecordPrefabInstancePropertyModifications(ia);   // keep the override
                }
            if (powered > 0) Debug.Log($"[SkyIsland] {ctx.id}: {powered} powered interactables on the grid.");
        }

        static void AddFlickers(Context ctx, Transform root, Dictionary<string, Material> mats)
        {
            var defs = ctx.layout.flickers;
            if (defs == null || defs.Length == 0) return;
            var c = root.gameObject.AddComponent<SkyIsland.SkyIslandFlicker>();
            c.root = root;
            foreach (var d in defs)
            {
                if (d == null) continue;
                if (string.IsNullOrEmpty(d.material) || !mats.TryGetValue(d.material, out var m) || m == null)
                {
                    Debug.LogWarning($"[SkyIsland] {ctx.id}: flicker material '{d.material}' not found.");
                    continue;
                }
                float min = d.min, max = d.max;
                if (min <= 0f && max <= 0f) { min = 0.6f; max = 1f; }   // absent: gentle flicker
                if (max < min) { float t = min; min = max; max = t; }
                c.entries.Add(new SkyIsland.SkyIslandFlicker.Entry
                {
                    material = m, min = min, max = max, speed = d.speed > 0f ? d.speed : 1f, lightPrefix = d.lightPrefix ?? "",
                });
            }
            FinishController(ctx, c, c.entries.Count, "flicker");
        }

        static void AddUVScroll(Context ctx, Transform root, Dictionary<string, Material> mats)
        {
            var defs = ctx.layout.uvScroll;
            if (defs == null || defs.Length == 0) return;
            var c = root.gameObject.AddComponent<SkyIsland.SkyIslandUVScroll>();
            c.root = root;
            foreach (var d in defs)
            {
                if (d == null) continue;
                if (string.IsNullOrEmpty(d.material) || !mats.TryGetValue(d.material, out var m) || m == null)
                {
                    Debug.LogWarning($"[SkyIsland] {ctx.id}: UV scroll material '{d.material}' not found.");
                    continue;
                }
                c.entries.Add(new SkyIsland.SkyIslandUVScroll.Entry
                {
                    material = m, speed = J.V2(d.speed, Vector2.zero), normalSpeed = J.V2(d.normalSpeed, Vector2.zero),
                });
            }
            FinishController(ctx, c, c.entries.Count, "UV scroll");
        }

        static void FinishController(Context ctx, Component c, int entries, string what)
        {
            if (entries == 0) { Object.DestroyImmediate(c); return; }
            Debug.Log($"[SkyIsland] {ctx.id}: {entries} {what} material(s).");
        }

        /// <summary>water -> SkyIslandWaterLevel on the location root. Surfaces stop being static (they move) and lose
        /// their colliders (water is not walkable).</summary>
        static void AddWater(Context ctx, Transform root)
        {
            var w = ctx.layout.water;
            if (w == null || !w.IsSet) return;
            var surfaces = FindAllByName(ctx, root, w.surfaces, "water surface");
            if (surfaces.Count == 0) { Debug.LogWarning($"[SkyIsland] {ctx.id}: no water surface found; water level skipped."); return; }
            float minY = Mathf.Min(w.minY, w.maxY), maxY = Mathf.Max(w.minY, w.maxY);
            int removed = 0;
            float authored = 0f;
            foreach (var s in surfaces)
            {
                SetStatic(s.gameObject, (StaticEditorFlags)0);
                foreach (var col in s.GetComponentsInChildren<Collider>(true)) { Object.DestroyImmediate(col); removed++; }
                authored += SkyIsland.SkyIslandWaterLevel.PlaneY(s);
            }
            authored /= surfaces.Count;
            float start = w.startY;
            if (start < minY || start > maxY)
            {
                Debug.Log($"[SkyIsland] {ctx.id}: water startY {w.startY} is outside {minY}..{maxY}; starting at the modelled level {authored:0.##} (clamped).");
                start = Mathf.Clamp(authored, minY, maxY);
            }
            int moved = 0;
            foreach (var srf in surfaces)
            {
                // the scene shows the start state (as in play mode): put every water plane at the start level
                float dy = start - SkyIsland.SkyIslandWaterLevel.PlaneY(srf);
                if (Mathf.Abs(dy) > 1e-3f) { srf.position += new Vector3(0f, dy, 0f); moved++; }
            }
            if (moved > 0) Debug.Log($"[SkyIsland] {ctx.id}: {moved} water surface(s) moved to the start level {start}.");
            var c = root.gameObject.AddComponent<SkyIsland.SkyIslandWaterLevel>();
            c.surfaces = surfaces.ToArray();
            c.minY = minY;
            c.maxY = maxY;
            c.startLevel = start;
            var flows = w.flowObjects != null && w.flowObjects.Length > 0 ? FindAllByName(ctx, root, w.flowObjects, "water flow object") : new List<Transform>();
            c.flowObjects = new GameObject[flows.Count];
            for (int i = 0; i < flows.Count; i++) c.flowObjects[i] = flows[i].gameObject;
            Debug.Log($"[SkyIsland] {ctx.id}: water level {minY}..{maxY} m (start {start}) on {surfaces.Count} surface(s), {flows.Count} flow object(s), {removed} collider(s) removed.");
        }

        /// <summary>fogEvents -> trigger boxes with SkyIslandFogEvent under "FogEvents". The trigger needs the player to
        /// have a CharacterController or a Rigidbody (standard Unity trigger rules).</summary>
        static void AddFogEvents(Context ctx, Transform root, Camera cam, AxisFix fix)
        {
            var defs = ctx.layout.fogEvents;
            if (defs == null || defs.Length == 0) return;
            var fogCtl = cam != null ? cam.GetComponent<SkyIsland.SkyIslandFogByAltitude>() : null;
            var group = new GameObject("FogEvents").transform;
            group.SetParent(root, false);
            for (int i = 0; i < defs.Length; i++)
            {
                var d = defs[i];
                if (d == null) continue;
                var go = new GameObject(string.IsNullOrEmpty(d.name) ? "FogEvent_" + i : d.name);
                go.transform.SetParent(group, false);
                go.transform.localPosition = fix.P(J.V3(d.center));
                var box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;
                var size = J.V3(d.size);
                size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)) * fix.scale;
                box.size = size.sqrMagnitude > 1e-6f ? size : new Vector3(10f, 10f, 10f);
                var fe = go.AddComponent<SkyIsland.SkyIslandFogEvent>();
                fe.densityMul = d.densityMul > 0f ? d.densityMul : 3f;
                fe.changeColor = J.Has(d.color, 3);
                if (fe.changeColor) fe.fogColor = J.Gam(d.color);
                fe.duration = d.duration > 0f ? d.duration : 4f;
                fe.oneShot = d.oneShot;
                fe.triggerTag = string.IsNullOrEmpty(d.tag) ? "Player" : d.tag;
                fe.fogController = fogCtl;
            }
            Debug.Log($"[SkyIsland] {ctx.id}: {defs.Length} fog event volume(s).");
        }

        /// <summary>spawn -> empty "PlayerSpawn" (tag Respawn) at p, turned by yaw degrees.</summary>
        static void AddSpawn(Context ctx, Transform root, AxisFix fix)
        {
            var sp = ctx.layout.spawn;
            if (sp == null || !sp.IsSet) return;
            var go = new GameObject("PlayerSpawn");
            go.transform.SetParent(root, false);
            go.transform.localPosition = fix.P(J.V3(sp.p));
            go.transform.localRotation = fix.Q(Quaternion.Euler(0f, sp.yaw, 0f));
            try { go.tag = "Respawn"; }   // built-in tag, lets game code find it with GameObject.FindWithTag
            catch (UnityException) { /* tag manager without the built-in tags: keep it untagged */ }
            Debug.Log($"[SkyIsland] {ctx.id}: PlayerSpawn at {go.transform.localPosition}, yaw {sp.yaw:0.#}.");
        }

        /// <summary>v2.4 anchors -> empty "Anchor_&lt;name&gt;" transforms under "Anchors" (places for separate assets,
        /// e.g. the players' car: Anchor_Car_Start).</summary>
        static void AddAnchors(Context ctx, Transform root, AxisFix fix)
        {
            var defs = ctx.layout.anchors;
            if (defs == null || defs.Length == 0) return;
            var group = new GameObject("Anchors").transform;
            group.SetParent(root, false);
            foreach (var a in defs)
            {
                if (a == null || string.IsNullOrEmpty(a.name)) continue;
                var go = new GameObject("Anchor_" + a.name);
                go.transform.SetParent(group, false);
                go.transform.localPosition = fix.P(J.V3(a.p));
                go.transform.localRotation = fix.Q(J.Q(a.q));
            }
            Debug.Log($"[SkyIsland] {ctx.id}: {defs.Length} anchor(s).");
        }

        /// <summary>Objects under root with one of the names (hierarchy order; a match inside another match is skipped).</summary>
        static List<Transform> FindAllByName(Context ctx, Transform root, string[] names, string what)
        {
            var wanted = new HashSet<string>();
            foreach (var n in names)
                if (!string.IsNullOrEmpty(n)) wanted.Add(n);
            var found = new List<Transform>();
            var foundNames = new HashSet<string>();
            var set = new HashSet<Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root || !wanted.Contains(t.name) || HasAncestorIn(t, set, root)) continue;
                found.Add(t);
                set.Add(t);
                foundNames.Add(t.name);
            }
            foreach (var n in wanted)
                if (!foundNames.Contains(n)) Debug.LogWarning($"[SkyIsland] {ctx.id}: {what} '{n}' not found in the scene.");
            return found;
        }

        static bool HasAncestorIn(Transform t, HashSet<Transform> set, Transform stop)
        {
            for (var p = t.parent; p != null && p != stop; p = p.parent)
                if (set.Contains(p)) return true;
            return false;
        }
    }
}
