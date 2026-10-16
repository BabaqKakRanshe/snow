// P00: occupancy map of the pilot door openings (doors open), 5 cm x 10 cm cells in the wall plane.
// Not compiled by the project (lives outside Assets). Run in Play Mode through the Unity MCP RunCommand tool.
// Third command of the P00 check. Legend: W = COL_Windshield, P = COL_Panel, D = own door, # = other, . = free.
using System.Linq;
using System.Text;
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
        inst.name = "P00_OpeningMap";
        try
        {
            var rig = inst.GetComponent<UtilityPlaneRig>();
            rig.pilotDoorL = rig.pilotDoorR = 1;
            rig.SendMessage("Update", SendMessageOptions.RequireReceiver);
            Physics.SyncTransforms();
            int mask = ~(1 << LayerMask.NameToLayer("Interact"));
            foreach (var side in new[] { "L", "R" })
            {
                float sx = side == "L" ? -1f : 1f;
                var sb = new StringBuilder();
                sb.AppendLine($"PilotDoor_{side} opening, wall plane X {sx * 0.70f:F2} +-0.05; rows Y top->bottom 2.47..1.17 step 0.10; cols Z aft->fwd 2.005..3.095 step 0.05");
                for (float y = 2.47f; y > 1.14f; y -= 0.10f)
                {
                    sb.Append($"{y:F2} ");
                    for (float z = 2.005f; z < 3.12f; z += 0.05f)
                    {
                        var hits = Physics.OverlapBox(new Vector3(sx * 0.70f, y, z), new Vector3(0.05f, 0.05f, 0.025f), Quaternion.identity, mask, QueryTriggerInteraction.Ignore);
                        char c = '.';
                        if (hits.Any(h => h.name == "COL_Windshield")) c = 'W';
                        else if (hits.Any(h => h.name == "COL_Panel")) c = 'P';
                        else if (hits.Any(h => h.name.StartsWith("COL_PilotDoor"))) c = 'D';
                        else if (hits.Length > 0) c = '#';
                        sb.Append(c);
                    }
                    sb.AppendLine();
                }
                R.Log(sb.ToString());
            }
            var cols = inst.GetComponentsInChildren<MeshCollider>(true).ToDictionary(c => c.name);
            R.Log($"COL_Panel bounds {cols["COL_Panel"].bounds.min.ToString("F3")}..{cols["COL_Panel"].bounds.max.ToString("F3")}");
            R.Log($"COL_Windshield bounds {cols["COL_Windshield"].bounds.min.ToString("F3")}..{cols["COL_Windshield"].bounds.max.ToString("F3")}");
            R.Log($"COL_Seat_P bounds {cols["COL_Seat_P"].bounds.min.ToString("F3")}..{cols["COL_Seat_P"].bounds.max.ToString("F3")}");
        }
        finally { Object.Destroy(inst); }
    }
}
