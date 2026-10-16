// P00: door openings, main/tail gear travel, cargo_layout.json bounds, instrument positions.
// Not compiled by the project (lives outside Assets). Run in Play Mode through the Unity MCP RunCommand tool.
// Second command of the P00 check; P00_SignCheck.cs is the first, P00_OpeningMap.cs the third.
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UtilityPlane;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult R)
    {
        if (!Application.isPlaying) { R.LogError("Enter Play Mode first"); return; }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/UtilityPlane/Prefabs/UtilityPlane.prefab");
        var inst = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
        inst.name = "P00_SignCheck2";
        try
        {
            var rig = inst.GetComponent<UtilityPlaneRig>();
            var T = inst.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name);
            System.Action step = () => rig.SendMessage("Update", SendMessageOptions.RequireReceiver);
            step(); Physics.SyncTransforms();
            var cols = inst.GetComponentsInChildren<MeshCollider>(true).ToDictionary(c => c.name);
            R.Log($"INFO floor top Y = {cols["COL_Floor"].bounds.max.y:F3} (contract 1.07)");
            R.Log($"INFO pilot sill top Y L/R = {cols["COL_Wall_L_PilotSill"].bounds.max.y:F3}/{cols["COL_Wall_R_PilotSill"].bounds.max.y:F3} (contract 1.14)");
            R.Log($"INFO cargo sill top Y L/R = {cols["COL_Wall_L_CargoSill"].bounds.max.y:F3}/{cols["COL_Wall_R_CargoSill"].bounds.max.y:F3} (contract 1.10)");
            R.Log($"INFO pilot head (lintel) bottom Y L = {cols["COL_Wall_L_PilotHead"].bounds.min.y:F3}; cargo head bottom Y L = {cols["COL_Wall_L_CargoHead"].bounds.min.y:F3}");
            R.Log($"INFO roof bottom Y = {cols["COL_Roof"].bounds.min.y:F3}; floor X extent = {cols["COL_Floor"].bounds.min.x:F3}..{cols["COL_Floor"].bounds.max.x:F3}; floor Z extent = {cols["COL_Floor"].bounds.min.z:F3}..{cols["COL_Floor"].bounds.max.z:F3}");

            int mask = ~(1 << LayerMask.NameToLayer("Interact"));
            foreach (var side in new[] { "L", "R" })
            {
                float zf = cols[$"COL_Wall_{side}_Front"].bounds.min.z, za = cols[$"COL_Wall_{side}_Mid"].bounds.max.z;
                float y0 = cols[$"COL_Wall_{side}_PilotSill"].bounds.max.y, y1 = cols[$"COL_Wall_{side}_PilotHead"].bounds.min.y;
                R.Log($"INFO PilotDoor_{side} opening from wall slabs: Z {za:F3}..{zf:F3} ({zf - za:F3} m), Y {y0:F3}..{y1:F3} ({y1 - y0:F3} m)");
                float czf = cols[$"COL_Wall_{side}_Mid"].bounds.min.z, cza = cols[$"COL_Wall_{side}_Aft"].bounds.max.z;
                float cy0 = cols[$"COL_Wall_{side}_CargoSill"].bounds.max.y, cy1 = cols[$"COL_Wall_{side}_CargoHead"].bounds.min.y;
                R.Log($"INFO CargoDoor_{side} opening from wall slabs: Z {cza:F3}..{czf:F3} ({czf - cza:F3} m), Y {cy0:F3}..{cy1:F3} ({cy1 - cy0:F3} m)");
            }

            // all doors open; thin slab in the wall plane without the door's own collider; box-cast of the widest case
            rig.pilotDoorL = rig.pilotDoorR = rig.cargoDoorL = rig.cargoDoorR = 1; step(); Physics.SyncTransforms();
            foreach (var d in new[] { "CargoDoor_L", "CargoDoor_R" })
            {
                float sx = d.EndsWith("L") ? -1f : 1f;
                var c = new Vector3(sx * 0.70f, (1.10f + 2.44f) / 2f, (0.85f - 0.45f) / 2f);
                var half = new Vector3(0.10f, (2.44f - 1.10f) / 2f - 0.03f, (0.85f + 0.45f) / 2f - 0.03f);
                var hits = Physics.OverlapBox(c, half, Quaternion.identity, mask, QueryTriggerInteraction.Ignore)
                    .Where(h => h.name != "COL_" + d).Select(h => h.name).ToList();
                R.Log($"{(hits.Count == 0 ? "PASS" : "FAIL")} {d} opening slab (contract Z -0.45..+0.85, Y 1.10..2.44, shrunk 3 cm): other colliders [{string.Join(", ", hits)}]");
                var ext = new Vector3(0.684f, 0.407f, 0.632f) * 0.5f; // Cargo_Case2
                var start = new Vector3(sx * 2.0f, 1.10f + ext.y + 0.02f, 0.20f);
                bool hit = Physics.BoxCast(start, ext, new Vector3(-sx, 0, 0), out var info, Quaternion.identity, 2.0f, mask, QueryTriggerInteraction.Ignore);
                R.Log($"{(!hit ? "PASS" : "FAIL")} {d}: Cargo_Case2 box-cast from outside to the centreline at floor height: {(hit ? "blocked by " + info.collider.name + " at X " + info.point.x.ToString("F3") : "clear")}");
            }
            foreach (var d in new[] { "PilotDoor_L", "PilotDoor_R" })
            {
                string side = d.EndsWith("L") ? "L" : "R";
                float sx = side == "L" ? -1f : 1f;
                float zf = cols[$"COL_Wall_{side}_Front"].bounds.min.z, za = cols[$"COL_Wall_{side}_Mid"].bounds.max.z;
                float y0 = cols[$"COL_Wall_{side}_PilotSill"].bounds.max.y, y1 = cols[$"COL_Wall_{side}_PilotHead"].bounds.min.y;
                var c = new Vector3(sx * 0.70f, (y0 + y1) / 2f, (za + zf) / 2f);
                var half = new Vector3(0.05f, (y1 - y0) / 2f - 0.03f, (zf - za) / 2f - 0.03f);
                var hits = Physics.OverlapBox(c, half, Quaternion.identity, mask, QueryTriggerInteraction.Ignore)
                    .Where(h => h.name != "COL_" + d).Select(h => h.name).ToList();
                R.Log($"{(hits.Count == 0 ? "PASS" : "FAIL")} {d} opening slab X {sx * 0.70f:F2}+-0.05, Y {y0:F3}..{y1:F3}, Z {za:F3}..{zf:F3} (shrunk 3 cm): other colliders [{string.Join(", ", hits)}]");
            }
            rig.pilotDoorL = rig.pilotDoorR = rig.cargoDoorL = rig.cargoDoorR = 0; step();

            // gear travel vs Rig compression (contract: 12 deg ~ 12 cm)
            System.Func<string, Vector3> centroid = part =>
            {
                var mf = T[part].GetComponentsInChildren<MeshFilter>(true).First(m => m.sharedMesh != null && m.sharedMesh.vertexCount > 0);
                return mf.transform.TransformPoint(mf.sharedMesh.bounds.center);
            };
            Vector3 w0 = centroid("Wheel_L");
            var travel = new List<string>();
            foreach (float k in new[] { 0.25f, 0.5f, 0.69f, 1f })
            {
                rig.gearCompressionL = k; step();
                Vector3 w = centroid("Wheel_L") - w0;
                travel.Add($"{k:F2}: dy {w.y:F3} dx {w.x:F3}");
            }
            rig.gearCompressionL = 0; step();
            R.Log($"INFO Wheel_L centre at rest {w0.ToString("F3")} (Socket_WheelContact_L {T["Socket_WheelContact_L"].position.ToString("F3")}); travel: {string.Join("; ", travel)}");
            Vector3 t0 = centroid("TailWheel");
            rig.tailGearCompression = 1; step();
            R.Log($"INFO TailWheel centre at rest {t0.ToString("F3")} (Socket_WheelContact_Tail {T["Socket_WheelContact_Tail"].position.ToString("F3")}); travel at 1.0: {(centroid("TailWheel") - t0).ToString("F3")}");
            rig.tailGearCompression = 0; step();

            var layout = new (string n, Vector3 pos)[]
            {
                ("Cargo_Case1", new Vector3(-0.126f, 1.09f, 0.5f)), ("Cargo_Case2", new Vector3(-0.146f, 1.09f, -0.12f)),
                ("Cargo_Case3", new Vector3(-0.126f, 1.51f, 0.48f)), ("Cargo_Case4", new Vector3(-0.106f, 1.45f, -0.1f)),
                ("Cargo_Case5", new Vector3(0.454f, 1.09f, 0.2f)), ("Cargo_Case6", new Vector3(-0.086f, 1.75f, 0.05f)),
            };
            foreach (var (n, pos) in layout)
            {
                var mesh = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/UtilityPlane/Prefabs/{n}.prefab").GetComponentInChildren<MeshFilter>().sharedMesh;
                var b = mesh.bounds;
                R.Log($"INFO {n}: local bounds min {b.min.ToString("F3")} max {b.max.ToString("F3")}; at layout Y {pos.y + b.min.y:F3}..{pos.y + b.max.y:F3}, X {pos.x + b.min.x:F3}..{pos.x + b.max.x:F3}");
            }
            var inst2 = new[] { "Needle_ASI", "Needle_ALT100", "Needle_VSI", "ATT_Roll", "HDG_Card", "TC_Plane", "Needle_TRQ", "Needle_PROP", "Needle_ITT", "Needle_NG", "Needle_OILP", "Needle_OILT", "Needle_FF", "Needle_FUELL", "Needle_FUELR", "Needle_AMP", "Needle_OAT", "Clock_Hour", "Lamp_00", "Lamp_11", "Display_GPS", "Btn_AnnunTest" };
            R.Log("INFO positions: " + string.Join("; ", inst2.Select(n => n + " " + T[n].position.ToString("F3"))));
        }
        finally { Object.Destroy(inst); }
    }
}
