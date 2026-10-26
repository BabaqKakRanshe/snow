using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Snow.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Snow.EditorTools
{
    /// <summary>
    /// Unity ↔ Blender exchange of object places for the Forest, step 1 of docs/gdd/WORLD_SYNC_CONTRACT.md (user 26.10: the
    /// ground is Blender's, object places are edited on both sides; in Unity only by moving objects).
    ///  - ids: every direct child of Structures / Rocks (the layout's prefabs / rocks) and of the dress roots gets a
    ///    <see cref="WorldId"/> "&lt;group&gt;/&lt;model&gt;#&lt;n&gt;" (n counts the model in the list's order; an "id" in a layout
    ///    entry wins). <see cref="Begin"/> runs first in the Forest chain, <see cref="LayoutPlaced"/> after the checkpoint and yard
    ///    steps, <see cref="Finish"/> last: each remembers the pose the steps gave its objects, then puts the hand moves back on;
    ///  - hand moves: saving the Forest scene (or Tools / Snow / World Sync / Save My Moves) writes every object that stands
    ///    elsewhere than its built pose to <see cref="MovesPath"/>; back at the built pose = the move is dropped;
    ///  - Export For Blender: source/world_sync/forest/ — every object with an id in its final pose + the moves;
    ///  - Check Incoming Layout: a new Forest_Layout.json from the Blender chat against the moves (taken over, both moved,
    ///    removed); Import Moves From Blender: moves_from_blender.json of tools/blender_context/export_moves.py.
    /// Only whole objects travel (no child parts, no Terrain trees, no items of the opening: they spawn in play).
    /// </summary>
    [InitializeOnLoad]
    public static class WorldSync
    {
        public const string MovesPath = "Assets/Snow/Levels/Forest/ForestMoves.json";
        const string LayoutPath = "Assets/Locations/Forest/Data/Forest_Layout.json";
        const string ExportDir = "../source/world_sync/forest";
        const string RootName = "Forest";
        public static readonly string[] DressGroups = new[] { ForestYardDressStep.RootName, ForestStartDressStep.RootName, ForestHutDressStep.RootName }.Concat(ForestPacksStep.Groups).Concat(new[] { ForestBunkerStep.RootName }).ToArray();
        /// <summary>m and degrees: smaller differences are not a move; incoming poses this close are "the same place".</summary>
        const float MoveEps = 0.01f, MoveAngle = 0.1f, SameEps = 0.02f, SameAngle = 0.5f;

        [Serializable] public class Move { public string id, m, by, at; public float[] p, q, placedP, placedQ; }
        [Serializable] public class Moves { public string note = "Hand moves of the Forest's objects (Editor/WorldSync.cs, WORLD_SYNC_CONTRACT.md)"; public Move[] moves = new Move[0]; }
        [Serializable] class Entry { public string id, m; public float[] p, q; public float s; }
        [Serializable] class Lists { public Entry[] prefabs, rocks; }
        [Serializable] class OutObject { public string id, m; public float[] p, q; public float s; public bool moved; }
        [Serializable] class OutFile { public string location, exported, axes, note; public OutObject[] objects; }
        [Serializable] class BlenderMove { public string id; public float[] p, q, fromP, fromQ; }
        [Serializable] class BlenderMoves { public BlenderMove[] moves; }

        static WorldSync() => EditorSceneManager.sceneSaving += OnSaving;

        // ------------------------------------------------------------------ the Forest chain

        /// <summary>First in the chain: ids for the layout's objects (a fresh build), and objects that already have a built
        /// pose (a rerun on an integrated scene) go back to it, so the steps work on the same state as after a rebuild.</summary>
        public static void Begin(Transform root)
        {
            int tagged = TagLayout(root);
            int reset = 0;
            foreach (WorldId w in root.GetComponentsInChildren<WorldId>(true))
                if (w.placed) { w.transform.SetPositionAndRotation(w.placedPosition, w.placedRotation); reset++; }
            Debug.Log($"[Snow] World sync: {tagged} layout objects got ids, {reset} put back on their built pose before the steps.");
        }

        /// <summary>After the steps that move layout objects (checkpoint, yard), before the dress steps: the layout objects'
        /// built pose is remembered and their hand moves applied, so dress placed around them (the hut's things) follows.</summary>
        public static void LayoutPlaced(Transform root) => PlaceAndApply(root, w => !IsDress(w.transform, root), "layout");

        /// <summary>Last in the chain: ids for the dress objects, their built pose remembered, their hand moves applied.</summary>
        public static void Finish(Transform root)
        {
            int tagged = 0;
            foreach (string g in DressGroups) tagged += TagByName(root.Find(g), g, null);
            PlaceAndApply(root, w => IsDress(w.transform, root), $"dress ({tagged} new ids)");
        }

        static void PlaceAndApply(Transform root, Func<WorldId, bool> which, string what)
        {
            var byId = new Dictionary<string, WorldId>(StringComparer.Ordinal);
            var dup = new List<string>();
            foreach (WorldId w in root.GetComponentsInChildren<WorldId>(true))
            {
                if (string.IsNullOrEmpty(w.id) || !which(w)) continue;
                w.placed = true;
                w.placedPosition = w.transform.position;
                w.placedRotation = w.transform.rotation;
                EditorUtility.SetDirty(w);
                if (!byId.ContainsKey(w.id)) byId[w.id] = w; else dup.Add(w.id);
            }
            int applied = 0;
            foreach (Move mv in Load().moves)
                if (byId.TryGetValue(mv.id, out WorldId w)) { w.transform.SetPositionAndRotation(V(mv.p), Q(mv.q)); applied++; }
            Debug.Log($"[Snow] World sync, {what}: {byId.Count} objects with ids, {applied} hand moves applied from {MovesPath}.");
            if (dup.Count > 0) Debug.LogWarning($"[Snow] World sync: repeated ids (the first one gets the move): {string.Join(", ", dup.Distinct().Take(12))}");
            if (what.StartsWith("dress", StringComparison.Ordinal))
            {
                var all = new HashSet<string>(root.GetComponentsInChildren<WorldId>(true).Select(x => x.id), StringComparer.Ordinal);
                string[] missing = Load().moves.Where(m => !all.Contains(m.id)).Select(m => m.id).ToArray();
                if (missing.Length > 0) Debug.LogWarning($"[Snow] World sync: {missing.Length} moves have no object any more (kept in the file): {string.Join(", ", missing.Take(12))}");
            }
        }

        /// <summary>Structures and Rocks: the k-th scene child named m is the k-th entry of m in the layout's list (the core
        /// builder places the lists in order); the entry's own "id" wins over the rule.</summary>
        static int TagLayout(Transform root)
        {
            Lists l = File.Exists(LayoutPath) ? JsonUtility.FromJson<Lists>(File.ReadAllText(LayoutPath)) : null;
            return TagByName(root.Find("Structures"), "Structures", l?.prefabs) + TagByName(root.Find("Rocks"), "Rocks", l?.rocks);
        }

        static int TagByName(Transform group, string name, Entry[] entries)
        {
            if (group == null) return 0;
            // the ids of each model in list order
            var listed = new Dictionary<string, Queue<string>>(StringComparer.Ordinal);
            var count = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Entry e in entries ?? new Entry[0])
            {
                if (e?.m == null) continue;
                count.TryGetValue(e.m, out int n);
                count[e.m] = ++n;
                if (!listed.TryGetValue(e.m, out Queue<string> q)) listed[e.m] = q = new Queue<string>();
                q.Enqueue(string.IsNullOrEmpty(e.id) ? $"{name}/{e.m}#{n}" : e.id);
            }
            count.Clear();
            int added = 0;
            foreach (Transform c in group)
            {
                count.TryGetValue(c.name, out int n);
                count[c.name] = ++n;
                string id = listed.TryGetValue(c.name, out Queue<string> q) && q.Count > 0 ? q.Dequeue() : $"{name}/{c.name}#{n}";
                if (c.TryGetComponent(out WorldId w) && !string.IsNullOrEmpty(w.id)) continue;
                if (w == null) w = c.gameObject.AddComponent<WorldId>();
                w.id = id;
                w.placed = false;
                added++;
            }
            return added;
        }

        static bool IsDress(Transform t, Transform root) => t.parent != null && t.parent.parent == root && DressGroups.Contains(t.parent.name);

        // ------------------------------------------------------------------ hand moves

        static void OnSaving(Scene scene, string path)
        {
            Transform root = ForestRoot(scene);
            if (root != null) Capture(root, "unity");
        }

        [MenuItem("Tools/Snow/World Sync/Save My Moves")]
        static void CaptureMenu()
        {
            Transform root = ForestRoot(SceneManager.GetActiveScene());
            if (root == null) { Debug.LogWarning("[Snow] World sync: open the Forest scene (after the integration) first."); return; }
            if (Capture(root, "unity") == 0) Debug.Log($"[Snow] World sync: no new moves ({Load().moves.Length} in {MovesPath}).");
        }

        /// <summary>Objects away from their built pose become moves, objects back on it lose theirs. Returns the changes.</summary>
        public static int Capture(Transform root, string by)
        {
            Moves data = Load();
            var list = data.moves.ToList();
            int added = 0, changed = 0, removed = 0;
            var names = new List<string>();
            foreach (WorldId w in root.GetComponentsInChildren<WorldId>(true))
            {
                if (!w.placed || string.IsNullOrEmpty(w.id)) continue;
                Transform t = w.transform;
                int k = list.FindIndex(m => m.id == w.id);
                if (Same(t.position, t.rotation, w.placedPosition, w.placedRotation, MoveEps, MoveAngle))
                {
                    if (k >= 0) { list.RemoveAt(k); removed++; names.Add("-" + w.id); }
                    continue;
                }
                if (k >= 0 && Same(t.position, t.rotation, V(list[k].p), Q(list[k].q), MoveEps, MoveAngle)) continue;
                var mv = new Move
                {
                    id = w.id, m = ModelOf(t), by = by, at = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    p = A(t.position), q = A(t.rotation), placedP = A(w.placedPosition), placedQ = A(w.placedRotation),
                };
                if (k >= 0) { list[k] = mv; changed++; } else { list.Add(mv); added++; }
                names.Add(w.id);
            }
            int total = added + changed + removed;
            if (total == 0) return 0;
            data.moves = list.OrderBy(m => m.id, StringComparer.Ordinal).ToArray();
            Save(data);
            Debug.Log($"[Snow] World sync: moves saved to {MovesPath} — {added} new, {changed} changed, {removed} back in place: {string.Join(", ", names.Take(12))}");
            return total;
        }

        // ------------------------------------------------------------------ Unity -> Blender

        [MenuItem("Tools/Snow/World Sync/Export For Blender")]
        static void ExportMenu()
        {
            Transform root = ForestRoot(SceneManager.GetActiveScene());
            if (root == null) { Debug.LogWarning("[Snow] World sync: open the Forest scene (after the integration) first."); return; }
            string dir = Export(root);
            EditorUtility.RevealInFinder(Path.Combine(dir, "unity_objects.json"));
        }

        /// <summary>Writes unity_objects.json (every object with an id in its final pose, layout format, Unity axes) and
        /// moves.json; returns the folder.</summary>
        public static string Export(Transform root)
        {
            Capture(root, "unity");
            var moved = new HashSet<string>(Load().moves.Select(m => m.id), StringComparer.Ordinal);
            var objects = root.GetComponentsInChildren<WorldId>(true).Where(w => !string.IsNullOrEmpty(w.id)).Select(w => new OutObject
            {
                id = w.id, m = ModelOf(w.transform), p = A(w.transform.position), q = A(w.transform.rotation),
                s = (float)Math.Round(w.transform.lossyScale.x, 4), moved = moved.Contains(w.id),
            }).OrderBy(o => o.id, StringComparer.Ordinal).ToArray();
            string dir = Path.GetFullPath(ExportDir);
            Directory.CreateDirectory(dir);
            var file = new OutFile
            {
                location = RootName, exported = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                axes = "Unity (as Forest_Layout.json): x east, y up, z north; Blender (x, y, z) = Unity (-x, -z, y)",
                note = "Final poses in the Unity scene (after the integration's steps and the hand moves). moved = in moves.json.",
                objects = objects,
            };
            File.WriteAllText(Path.Combine(dir, "unity_objects.json"), JsonUtility.ToJson(file, true));
            File.Copy(Path.GetFullPath(MovesPath), Path.Combine(dir, "moves.json"), true);
            Debug.Log($"[Snow] World sync: {objects.Length} objects ({moved.Count} moved by hand) exported to {dir}.");
            return dir;
        }

        // ------------------------------------------------------------------ Blender -> Unity

        [MenuItem("Tools/Snow/World Sync/Check Incoming Layout...")]
        static void IncomingMenu()
        {
            string path = EditorUtility.OpenFilePanel("A new Forest_Layout.json from Blender", Path.GetFullPath("../source"), "json");
            if (string.IsNullOrEmpty(path)) return;
            string report = CheckIncoming(path, false, out int dropped, out int conflicts);
            if (dropped > 0 && conflicts == 0 && EditorUtility.DisplayDialog("World sync", report + $"\n\nDrop the {dropped} moves Blender took over?", "Drop", "Keep all"))
                CheckIncoming(path, true, out _, out _);
        }

        /// <summary>Each hand move against the incoming layout: Blender put the object at the moved pose = taken over (dropped
        /// when <paramref name="apply"/>); Blender moved it elsewhere = both moved; no such id = removed in Blender. Objects of
        /// the dress roots are Unity's own and stay. The report also goes to &lt;incoming&gt;.sync.txt.</summary>
        public static string CheckIncoming(string incomingPath, bool apply, out int dropped, out int conflicts)
        {
            Dictionary<string, Entry> inc = Ids(JsonUtility.FromJson<Lists>(File.ReadAllText(incomingPath)));
            Dictionary<string, Entry> cur = Ids(JsonUtility.FromJson<Lists>(File.ReadAllText(LayoutPath)));
            Moves data = Load();
            var keep = new List<Move>();
            var sb = new StringBuilder($"World sync: {incomingPath} against {data.moves.Length} hand moves\n");
            dropped = conflicts = 0;
            foreach (Move mv in data.moves)
            {
                if (!mv.id.StartsWith("Structures/", StringComparison.Ordinal) && !mv.id.StartsWith("Rocks/", StringComparison.Ordinal))
                {
                    keep.Add(mv);
                    sb.Append($"  stays (Unity's own object): {mv.id}\n");
                    continue;
                }
                if (!inc.TryGetValue(mv.id, out Entry e)) { keep.Add(mv); conflicts++; sb.Append($"  CONFLICT removed in Blender, moved in Unity: {mv.id}\n"); continue; }
                if (Same(V(e.p), Q(e.q), V(mv.p), Q(mv.q), SameEps, SameAngle)) { dropped++; sb.Append($"  taken over by Blender: {mv.id}\n"); continue; }
                keep.Add(mv);
                if (cur.TryGetValue(mv.id, out Entry c) && !Same(V(e.p), Q(e.q), V(c.p), Q(c.q), SameEps, SameAngle))
                {
                    conflicts++;
                    sb.Append($"  CONFLICT moved in both: {mv.id} (Unity {Fmt(mv.p)}, Blender {Fmt(e.p)})\n");
                }
                else sb.Append($"  stays (Blender did not touch it): {mv.id}\n");
            }
            sb.Append($"{dropped} taken over, {conflicts} conflicts, {keep.Count} moves stay.");
            if (apply && dropped > 0)
            {
                data.moves = keep.ToArray();
                Save(data);
                sb.Append(" Applied.");
            }
            string report = sb.ToString();
            File.WriteAllText(incomingPath + ".sync.txt", report);
            if (conflicts > 0) Debug.LogWarning("[Snow] " + report); else Debug.Log("[Snow] " + report);
            return report;
        }

        [MenuItem("Tools/Snow/World Sync/Import Moves From Blender...")]
        static void BlenderMovesMenu()
        {
            Transform root = ForestRoot(SceneManager.GetActiveScene());
            if (root == null) { Debug.LogWarning("[Snow] World sync: open the Forest scene (after the integration) first."); return; }
            string path = EditorUtility.OpenFilePanel("moves_from_blender.json", Path.GetFullPath("../source/context"), "json");
            if (!string.IsNullOrEmpty(path)) ImportBlenderMoves(root, path);
        }

        /// <summary>Moves made in a context .blend (tools/blender_context/export_moves.py): each object goes to its Blender pose
        /// in the open scene and into the moves, unless Unity moved it after the export (its pose is not the one Blender
        /// started from) — then it is a conflict and stays. Returns the moves taken.</summary>
        public static int ImportBlenderMoves(Transform root, string path)
        {
            BlenderMoves bm = JsonUtility.FromJson<BlenderMoves>(File.ReadAllText(path));
            var byId = new Dictionary<string, WorldId>(StringComparer.Ordinal);
            foreach (WorldId w in root.GetComponentsInChildren<WorldId>(true)) if (!string.IsNullOrEmpty(w.id) && !byId.ContainsKey(w.id)) byId[w.id] = w;
            int taken = 0;
            var problems = new List<string>();
            foreach (BlenderMove b in bm?.moves ?? new BlenderMove[0])
            {
                if (!byId.TryGetValue(b.id, out WorldId w) || !w.placed) { problems.Add($"no object {b.id}"); continue; }
                if (!Same(w.transform.position, w.transform.rotation, V(b.fromP), Q(b.fromQ), SameEps, SameAngle))
                {
                    problems.Add($"CONFLICT {b.id}: moved in Unity after the export (Unity {Fmt(A(w.transform.position))}, Blender started from {Fmt(b.fromP)})");
                    continue;
                }
                Undo.RecordObject(w.transform, "Blender moves");
                w.transform.SetPositionAndRotation(V(b.p), Q(b.q));
                taken++;
            }
            Capture(root, "blender");
            Debug.Log($"[Snow] World sync: {taken} moves from {path} put on the open scene and into {MovesPath}; save the scene to keep them.");
            if (problems.Count > 0) Debug.LogWarning("[Snow] World sync: " + string.Join("\n", problems));
            return taken;
        }

        // ------------------------------------------------------------------ helpers

        static Dictionary<string, Entry> Ids(Lists l)
        {
            var d = new Dictionary<string, Entry>(StringComparer.Ordinal);
            void Add(Entry[] list, string group)
            {
                var count = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (Entry e in list ?? new Entry[0])
                {
                    if (e?.m == null || e.p == null || e.q == null) continue;
                    count.TryGetValue(e.m, out int n);
                    count[e.m] = ++n;
                    d[string.IsNullOrEmpty(e.id) ? $"{group}/{e.m}#{n}" : e.id] = e;
                }
            }
            Add(l?.prefabs, "Structures");
            Add(l?.rocks, "Rocks");
            return d;
        }

        static Transform ForestRoot(Scene scene)
        {
            if (!scene.IsValid()) return null;
            GameObject g = scene.GetRootGameObjects().FirstOrDefault(x => x.name == RootName && x.transform.Find("Structures") != null);
            return g != null ? g.transform : null;
        }

        public static Moves Load() => File.Exists(MovesPath) ? JsonUtility.FromJson<Moves>(File.ReadAllText(MovesPath)) ?? new Moves() : new Moves();

        static void Save(Moves d)
        {
            File.WriteAllText(MovesPath, JsonUtility.ToJson(d, true));
            // inside sceneSaving the asset database must not import
            EditorApplication.delayCall += () => AssetDatabase.ImportAsset(MovesPath);
        }

        static string ModelOf(Transform t)
        {
            GameObject src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
            return src != null ? src.name : t.name;
        }

        static bool Same(Vector3 p1, Quaternion q1, Vector3 p2, Quaternion q2, float eps, float deg) =>
            (p1 - p2).magnitude <= eps && Quaternion.Angle(q1, q2) <= deg;

        static float[] A(Vector3 v) => new[] { R(v.x), R(v.y), R(v.z) };
        static float[] A(Quaternion q) => new[] { R(q.x, 6), R(q.y, 6), R(q.z, 6), R(q.w, 6) };
        static float R(float f, int d = 4) => (float)Math.Round(f, d);
        static Vector3 V(float[] a) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;
        static Quaternion Q(float[] a) => a != null && a.Length >= 4 ? new Quaternion(a[0], a[1], a[2], a[3]).normalized : Quaternion.identity;
        static string Fmt(float[] p) => p == null ? "?" : string.Join(", ", p.Take(3).Select(f => f.ToString("0.00", CultureInfo.InvariantCulture)));
    }
}
