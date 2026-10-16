// P00: numeric sign check of UtilityPlaneRig channels against UNITY_CONTRACT.md §3, §4, §6, §7, §10, §11.
// Not compiled by the project (lives outside Assets). Run in Play Mode through the Unity MCP RunCommand tool
// (class CommandScript : IRunCommand). It instantiates Prefabs/UtilityPlane.prefab at the origin for the duration
// of the check, drives the Rig fields, calls Rig.Update() through SendMessage (deterministic, no dependence on editor focus),
// measures world-space displacement of the real geometry and destroys the instance.
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UtilityPlane;

internal class CommandScript : IRunCommand
{
    ExecutionResult R;
    GameObject inst;
    UtilityPlaneRig rig;
    Dictionary<string, Transform> T;
    int pass, fail;

    public void Execute(ExecutionResult result)
    {
        R = result;
        if (!Application.isPlaying) { R.LogError("Enter Play Mode first"); return; }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/UtilityPlane/Prefabs/UtilityPlane.prefab");
        inst = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
        inst.name = "P00_SignCheck";
        try { Run(); }
        finally { Object.Destroy(inst); }
        R.Log("TOTAL: PASS {0}, FAIL {1}", pass, fail);
    }

    void Run()
    {
        rig = inst.GetComponent<UtilityPlaneRig>();
        T = inst.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name);
        R.Log("dt = {0:F4} s", Time.deltaTime);
        Step();

        // ---------------- exterior (§3)
        Centroid("PilotDoor_L", () => rig.pilotDoorL = 1, () => rig.pilotDoorL = 0, d => d.x < -0.2f, "opens outward (-X)");
        Centroid("PilotDoor_R", () => rig.pilotDoorR = 1, () => rig.pilotDoorR = 0, d => d.x > 0.2f, "opens outward (+X)");
        Centroid("CargoDoor_L", () => rig.cargoDoorL = 1, () => rig.cargoDoorL = 0, d => d.x < -0.2f, "opens outward (-X)");
        Centroid("CargoDoor_R", () => rig.cargoDoorR = 1, () => rig.cargoDoorR = 0, d => d.x > 0.2f, "opens outward (+X)");
        Centroid("FuelCap_L", () => rig.fuelCapL = 1, () => rig.fuelCapL = 0, d => d.y > 0.01f, "opens upward");
        Centroid("FuelCap_R", () => rig.fuelCapR = 1, () => rig.fuelCapR = 0, d => d.y > 0.01f, "opens upward");
        Edge("Rudder", MinZ, () => rig.rudderDeg = 25, () => rig.rudderDeg = 0, d => d.x > 0.05f, "rudderDeg +25: trailing edge right (+X)");
        Edge("Elevator_L", MinZ, () => rig.elevatorDeg = 15, () => rig.elevatorDeg = 0, d => d.y > 0.02f, "elevatorDeg +15: trailing edge up");
        Edge("Elevator_R", MinZ, () => rig.elevatorDeg = 15, () => rig.elevatorDeg = 0, d => d.y > 0.02f, "elevatorDeg +15: trailing edge up");
        Edge("Aileron_R", MinZ, () => rig.aileronDeg = 20, () => rig.aileronDeg = 0, d => d.y > 0.02f, "aileronDeg +20 (right roll): right TE up");
        Edge("Aileron_L", MinZ, () => rig.aileronDeg = 20, () => rig.aileronDeg = 0, d => d.y < -0.02f, "aileronDeg +20 (right roll): left TE down");
        Edge("Flap_L", MinZ, () => rig.flapDeg = 35, () => rig.flapDeg = 0, d => d.y < -0.05f, "flapDeg 35: trailing edge down");
        Edge("Flap_R", MinZ, () => rig.flapDeg = 35, () => rig.flapDeg = 0, d => d.y < -0.05f, "flapDeg 35: trailing edge down");
        Centroid("Wheel_L", () => rig.gearCompressionL = 1, () => rig.gearCompressionL = 0, d => d.y > 0.05f, "gearCompressionL 1: wheel moves up");
        Centroid("Wheel_R", () => rig.gearCompressionR = 1, () => rig.gearCompressionR = 0, d => d.y > 0.05f, "gearCompressionR 1: wheel moves up");
        Centroid("TailWheel", () => rig.tailGearCompression = 1, () => rig.tailGearCompression = 0, d => d.y > 0.01f, "tailGearCompression 1: tail wheel moves up");
        {
            rig.tailWheelSteerDeg = 30; Step();
            float fx = T["TailWheel"].forward.x;
            Check("TailWheel_Fork", fx > 0.3f, $"tailWheelSteerDeg +30: wheel front to the right, forward.x = {fx:F3}");
            rig.tailWheelSteerDeg = 0; Step();
        }
        {
            // one frame of rotation, ~10 degrees
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Edge("Propeller", MaxY, () => rig.propRpm = 10f / (6f * dt), () => rig.propRpm = 0, d => d.x > 0.01f,
                "propRpm +: blade at the top moves right = clockwise seen from the cockpit", resetAngles: true);
            Edge("Wheel_L", MaxY, () => rig.mainWheelSpeed = 10f * 2f * Mathf.PI * 0.484f / (360f * dt), () => rig.mainWheelSpeed = 0,
                d => d.z > 0.01f, "mainWheelSpeed +: top of the wheel moves forward = rolling forward", resetAngles: true);
        }

        // ---------------- cockpit controls (§4)
        Point("Yoke_P", () => rig.yokePitch = 1, () => rig.yokePitch = 0, d => d.z > 0.07f, "yokePitch +1: yoke forward (+Z, from pilot)");
        Point("Yoke_C", () => rig.yokePitch = 1, () => rig.yokePitch = 0, d => d.z > 0.07f, "Yoke_C follows yokePitch");
        Point("Socket_Hand_Yoke_R", () => rig.yokeRoll = 1, () => rig.yokeRoll = 0, d => d.y < -0.05f, "yokeRoll +1 (right): right grip goes down");
        Point("Socket_Hand_Yoke_L", () => rig.yokeRoll = 1, () => rig.yokeRoll = 0, d => d.y > 0.05f, "yokeRoll +1 (right): left grip goes up");
        Point("Socket_Foot_R", () => rig.pedals = 1, () => rig.pedals = 0, d => d.z > 0.01f, "pedals +1: right pedal forward");
        Point("Socket_Foot_L", () => rig.pedals = 1, () => rig.pedals = 0, d => d.z < -0.01f, "pedals +1: left pedal aft");
        Centroid("PedalC_R", () => rig.pedals = 1, () => rig.pedals = 0, d => d.z > 0.005f, "pedals +1: copilot right pedal forward");
        Centroid("PedalC_L", () => rig.pedals = 1, () => rig.pedals = 0, d => d.z < -0.005f, "pedals +1: copilot left pedal aft");
        Point("Socket_Hand_Throttle", () => rig.powerLever = 1, () => rig.powerLever = 0, d => d.z > 0.02f, "powerLever +1: throttle forward");
        Edge("Lever_Prop", MaxY, () => rig.propLever = 1, () => rig.propLever = 0, d => d.z > 0.02f, "propLever +1: knob forward");
        Edge("Lever_Condition", MaxY, () => rig.conditionLever = 1, () => rig.conditionLever = 0, d => d.z > 0.02f, "conditionLever +1: knob forward");
        Edge("Lever_Flap", MaxY, () => rig.flapLever = 3, () => rig.flapLever = 0, d => d.y < -0.01f, "flapLever 3 (35 deg): knob goes down (info: direction not fixed by the contract)");
        Edge("TrimWheel", MaxY, () => rig.trimWheelDeg = 20, () => rig.trimWheelDeg = 0, d => d.z < -0.005f, "trimWheelDeg +20 (nose-up): top of the wheel rolls aft");
        foreach (var sw in T.Keys.Where(n => n.StartsWith("SW_")).OrderBy(n => n).ToList())
            Edge(sw, MinZ, () => rig.switchesOn.Add(sw), () => rig.switchesOn.Remove(sw), d => d.y > 0.003f, "switch ON: bat goes up");
        Point("Btn_AnnunTest", () => rig.annunciatorTestPressed = true, () => rig.annunciatorTestPressed = false, d => d.z > 0.002f, "pressed: +Z into the panel");
        Point("Pull_CabinHeat", () => rig.pullCabinHeat = 1, () => rig.pullCabinHeat = 0, d => d.z < -0.05f, "pulled: toward the pilot (-Z)");
        Point("Pull_Defrost", () => rig.pullDefrost = 1, () => rig.pullDefrost = 0, d => d.z < -0.05f, "pulled: toward the pilot (-Z)");
        Point("Pull_ParkBrake", () => rig.pullParkBrake = 1, () => rig.pullParkBrake = 0, d => d.z < -0.05f, "pulled: toward the pilot (-Z)");
        Point("Pull_FuelShutoff", () => rig.pullFuelShutoff = 1, () => rig.pullFuelShutoff = 0, d => d.z < -0.05f, "pulled: toward the pilot (-Z)");
        foreach (var fs in new[] { "FuelSel_L", "FuelSel_R" })
        {
            Vector3 c = CentroidOf(fs), p = T[fs].position;
            Check(fs, c.y < p.y - 0.005f, $"rest (ON): handle hangs down from the pivot, centroid-pivot dy = {c.y - p.y:F4}");
        }
        Centroid("FuelSel_L", () => rig.fuelSelL = false, () => rig.fuelSelL = true, d => d.magnitude > 0.01f, "OFF: handle turns (info)");
        Centroid("Visor_P", () => rig.visorP = 1, () => rig.visorP = 0, d => d.y < -0.03f, "visorP 1: visor lowered");
        Centroid("Visor_C", () => rig.visorC = 1, () => rig.visorC = 0, d => d.y < -0.03f, "visorC 1: visor lowered");
        foreach (var s in new[] { "P", "C" })
        {
            bool isP = s == "P";
            if (isP) rig.beltP = false; else rig.beltC = false;
            Step();
            bool offOk = T[$"Belt_{s}_On"].localScale == Vector3.zero && T[$"Belt_{s}_Off"].localScale != Vector3.zero;
            if (isP) rig.beltP = true; else rig.beltC = true;
            Step();
            bool onOk = T[$"Belt_{s}_On"].localScale != Vector3.zero && T[$"Belt_{s}_Off"].localScale == Vector3.zero;
            Check($"Belt_{s}", offOk && onOk, "exactly one of On/Off visible for false/true");
            if (isP) rig.beltP = false; else rig.beltC = false;
            Step();
        }

        // ---------------- annunciator (§6) and lights (§7)
        var mpb = new MaterialPropertyBlock();
        System.Func<string, float> glow = n =>
        {
            var r = T[n].GetComponent<Renderer>();
            r.GetPropertyBlock(mpb);
            Vector4 v = mpb.GetVector("_EmissionColor");
            return v.x + v.y + v.z;
        };
        rig.busPowered = true; rig.annunciatorTestPressed = true; Step();
        int lit = Enumerable.Range(0, 12).Count(i => glow($"Lamp_{i:00}") > 0.01f);
        Check("Lamps", lit == 12, $"bus + test: {lit}/12 lit");
        rig.busPowered = false; Step();
        lit = Enumerable.Range(0, 12).Count(i => glow($"Lamp_{i:00}") > 0.01f);
        Check("Lamps", lit == 0, $"no bus + test: {lit}/12 lit");
        rig.busPowered = true; rig.annunciatorTestPressed = false; rig.lamps = new bool[12]; rig.lamps[3] = true; Step();
        var litIdx = Enumerable.Range(0, 12).Where(i => glow($"Lamp_{i:00}") > 0.01f).ToList();
        Check("Lamps", litIdx.Count == 1 && litIdx[0] == 3, $"bus, lamps[3] only: lit = [{string.Join(",", litIdx)}] (legend {UtilityPlaneRig.LampLegends[3]})");
        rig.lamps = new bool[12]; rig.busPowered = false; Step();

        var lights = inst.GetComponentsInChildren<Light>(true).ToDictionary(l => l.name);
        rig.navLights = 1; rig.landingLight = 0.5f; Step();
        Check("Light_Nav_L/R/Tail", lights["Light_Nav_L"].enabled && lights["Light_Nav_R"].enabled && lights["Light_Nav_Tail"].enabled,
            $"navLights 1: enabled, intensity {lights["Light_Nav_L"].intensity:F2}");
        Check("Light_Landing", lights["Light_Landing"].enabled && Mathf.Abs(lights["Light_Landing"].intensity - 6f) < 0.01f,
            $"landingLight 0.5: intensity {lights["Light_Landing"].intensity:F2} (100 % = 12)");
        Check("Lens_Nav_L", glow("Lens_Nav_L") > 0.01f, "navLights 1: lens emits");
        rig.navLights = 0; rig.landingLight = 0; Step();
        Check("Lens_Nav_L", glow("Lens_Nav_L") < 0.001f && !lights["Light_Nav_L"].enabled, "navLights 0: lens dark, light off");

        // ---------------- colliders and scale (§10, §11.4, §11.5)
        Physics.SyncTransforms();
        var cols = inst.GetComponentsInChildren<MeshCollider>(true).ToDictionary(c => c.name);
        R.Log("INFO floor top Y = {0:F3} (expected 1.07)", cols["COL_Floor"].bounds.max.y);
        R.Log("INFO pilot sill top Y L/R = {0:F3}/{1:F3} (expected 1.14)", cols["COL_Wall_L_PilotSill"].bounds.max.y, cols["COL_Wall_R_PilotSill"].bounds.max.y);
        R.Log("INFO cargo sill top Y L/R = {0:F3}/{1:F3} (expected 1.10)", cols["COL_Wall_L_CargoSill"].bounds.max.y, cols["COL_Wall_R_CargoSill"].bounds.max.y);
        R.Log("INFO roof bottom Y = {0:F3}", cols["COL_Roof"].bounds.min.y);
        int mask = ~(1 << LayerMask.NameToLayer("Interact"));

        // door openings: open all doors, probe a box that fills the closed door outline shrunk by 5 cm
        var doorBounds = new Dictionary<string, Bounds>();
        foreach (var d in new[] { "PilotDoor_L", "PilotDoor_R", "CargoDoor_L", "CargoDoor_R" }) doorBounds[d] = RendBounds(d);
        rig.pilotDoorL = rig.pilotDoorR = rig.cargoDoorL = rig.cargoDoorR = 1; Step(); Physics.SyncTransforms();
        foreach (var kv in doorBounds)
        {
            Bounds b = kv.Value;
            var half = new Vector3(0.12f, b.extents.y - 0.05f, b.extents.z - 0.05f);
            var hits = Physics.OverlapBox(b.center, half, Quaternion.identity, mask, QueryTriggerInteraction.Ignore);
            Check(kv.Key + " opening", hits.Length == 0,
                $"closed door bounds c={b.center.ToString("F3")} size={b.size.ToString("F3")}; probe overlaps: [{string.Join(", ", hits.Select(h => h.name))}]");
        }
        rig.pilotDoorL = rig.pilotDoorR = rig.cargoDoorL = rig.cargoDoorR = 0; Step(); Physics.SyncTransforms();

        // cargo bay interior: X +-0.60, floor+0.03 .. 2.40, Z -1.25 .. +1.90
        {
            var c = new Vector3(0f, (1.10f + 2.40f) / 2f, (-1.25f + 1.90f) / 2f);
            var half = new Vector3(0.60f, (2.40f - 1.10f) / 2f, (1.90f + 1.25f) / 2f);
            var hits = Physics.OverlapBox(c, half, Quaternion.identity, mask, QueryTriggerInteraction.Ignore);
            R.Log("INFO cargo bay box X+-0.60 Y1.10..2.40 Z-1.25..1.90 overlaps: [{0}]", string.Join(", ", hits.Select(h => h.name)));
        }
        // player capsule (h 1.80, r 0.34) at the exit sockets, standing on the ground
        foreach (var s in new[] { "Socket_Exit_L", "Socket_Exit_R", "Socket_Exit_Cargo_L", "Socket_Exit_Cargo_R" })
        {
            Vector3 p = T[s].position;
            bool blocked = Physics.CheckCapsule(p + Vector3.up * 0.35f, p + Vector3.up * 1.45f, 0.34f, mask, QueryTriggerInteraction.Ignore);
            Check(s, !blocked, $"capsule 1.80 x r0.34 at {p.ToString("F2")} is free");
        }
        // standing capsule in the cargo bay vs. crouched (0.9 m)
        {
            Vector3 p = new Vector3(0f, 1.075f, 0.2f);
            bool stand = Physics.CheckCapsule(p + Vector3.up * 0.35f, p + Vector3.up * 1.45f, 0.34f, mask, QueryTriggerInteraction.Ignore);
            bool crouch = Physics.CheckCapsule(p + Vector3.up * 0.35f, p + Vector3.up * 0.55f, 0.34f, mask, QueryTriggerInteraction.Ignore);
            R.Log("INFO player in cargo bay at {0}: standing 1.80 m blocked = {1}; crouched 0.90 m blocked = {2}", p.ToString("F2"), stand, crouch);
        }
        // cargo_layout.json: case bounds vs airframe colliders and vs each other
        var layout = new (string n, Vector3 pos)[]
        {
            ("Cargo_Case1", new Vector3(-0.126f, 1.09f, 0.5f)), ("Cargo_Case2", new Vector3(-0.146f, 1.09f, -0.12f)),
            ("Cargo_Case3", new Vector3(-0.126f, 1.51f, 0.48f)), ("Cargo_Case4", new Vector3(-0.106f, 1.45f, -0.1f)),
            ("Cargo_Case5", new Vector3(0.454f, 1.09f, 0.2f)), ("Cargo_Case6", new Vector3(-0.086f, 1.75f, 0.05f)),
        };
        var boxes = new List<Bounds>();
        foreach (var (n, pos) in layout)
        {
            var cp = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/UtilityPlane/Prefabs/{n}.prefab");
            var mesh = cp.GetComponentInChildren<MeshFilter>().sharedMesh;
            Bounds lb = mesh.bounds; // pivot at the bottom centre, identity rotation
            var wb = new Bounds(pos + lb.center, lb.size);
            boxes.Add(wb);
            var hits = Physics.OverlapBox(wb.center, wb.extents - Vector3.one * 0.005f, Quaternion.identity, mask, QueryTriggerInteraction.Ignore);
            R.Log("INFO {0} at {1}: mesh local bounds min.y {2:F3}; world Y {3:F3}..{4:F3}; airframe overlaps: [{5}]",
                n, pos.ToString("F3"), lb.min.y, wb.min.y, wb.max.y, string.Join(", ", hits.Select(h => h.name)));
        }
        for (int i = 0; i < boxes.Count; i++)
            for (int j = i + 1; j < boxes.Count; j++)
                if (boxes[i].Intersects(boxes[j]))
                {
                    Vector3 o = Vector3.Min(boxes[i].max, boxes[j].max) - Vector3.Max(boxes[i].min, boxes[j].min);
                    R.Log("INFO layout overlap {0} x {1}: {2}", layout[i].n, layout[j].n, o.ToString("F3"));
                }
    }

    // ------------------------------------------------------------ helpers
    void Step(int n = 1) { for (int i = 0; i < n; i++) rig.SendMessage("Update", SendMessageOptions.RequireReceiver); }

    void Check(string what, bool ok, string msg)
    {
        if (ok) pass++; else fail++;
        R.Log("{0} {1}: {2}", ok ? "PASS" : "FAIL", what, msg);
    }

    List<Vector3> Verts(string part)
    {
        var list = new List<Vector3>();
        foreach (var mf in T[part].GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var m = mf.transform.localToWorldMatrix;
            foreach (var v in mf.sharedMesh.vertices) list.Add(m.MultiplyPoint3x4(v));
        }
        return list;
    }

    Vector3 CentroidOf(string part)
    {
        var v = Verts(part);
        return v.Count == 0 ? T[part].position : v.Aggregate(Vector3.zero, (a, b) => a + b) / v.Count;
    }

    Bounds RendBounds(string part)
    {
        var rs = T[part].GetComponentsInChildren<Renderer>(true);
        Bounds b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static int MinZ(List<Vector3> v) { int k = 0; for (int i = 1; i < v.Count; i++) if (v[i].z < v[k].z) k = i; return k; }
    static int MaxY(List<Vector3> v) { int k = 0; for (int i = 1; i < v.Count; i++) if (v[i].y > v[k].y) k = i; return k; }

    void Centroid(string part, System.Action set, System.Action reset, System.Func<Vector3, bool> ok, string msg)
    {
        Step(); Vector3 a = CentroidOf(part);
        set(); Step(); Vector3 b = CentroidOf(part);
        reset(); Step();
        Vector3 d = b - a;
        Check(part, ok(d), $"{msg}; centroid delta {d.ToString("F4")}");
    }

    void Point(string node, System.Action set, System.Action reset, System.Func<Vector3, bool> ok, string msg)
    {
        Step(); Vector3 a = T[node].position;
        set(); Step(); Vector3 b = T[node].position;
        reset(); Step();
        Vector3 d = b - a;
        Check(node, ok(d), $"{msg}; delta {d.ToString("F4")}");
    }

    // Tracks one vertex picked at rest (e.g. the trailing edge = min Z) through the change.
    void Edge(string part, System.Func<List<Vector3>, int> pick, System.Action set, System.Action reset,
              System.Func<Vector3, bool> ok, string msg, bool resetAngles = false)
    {
        Step();
        var v0 = Verts(part);
        if (v0.Count == 0) { Check(part, false, "no mesh vertices"); return; }
        int k = pick(v0);
        set(); Step();
        var v1 = Verts(part);
        reset(); Step();
        Vector3 d = v1[k] - v0[k];
        Check(part, ok(d), $"{msg}; tracked vertex {v0[k].ToString("F3")} delta {d.ToString("F4")}");
    }
}
