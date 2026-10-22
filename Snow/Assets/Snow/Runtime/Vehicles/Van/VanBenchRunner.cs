using System.Collections;
using System.Collections.Generic;
using Snow.Doors;
using Snow.Net;
using Unity.Netcode;
using UnityEngine;

namespace Snow.Vehicles.Van
{
    /// <summary>
    /// Bench scenarios on P09_VanBench (VAN3_INTEGRATION_UNITY.md AT-V06, AT-V07), server only: the van is put at a start
    /// pose and given the same driver intent the seat would send (VanVehicle.BenchIntent: W/S, A/D), the hand brake, the
    /// transfer levers and the TEMPORARY engine switch. Results go to BenchLog (van_bench …, van_bench_done). Started from
    /// the editor (eval) or a dev key: VanBenchRunner.Run("slopes" | "hold" | "ice" | "wall" | "door" | "all").
    /// The course numbers repeat VanBenchBuilder's (Editor assembly).
    /// </summary>
    public class VanBenchRunner : MonoBehaviour
    {
        static readonly (float grade, float x)[] Ramps = { (0.10f, 20f), (0.20f, 35f), (0.30f, 50f) };
        const float RampStartZ = 10f, RampLength = 40f;
        static readonly Vector3 IceCenter = new Vector3(-40f, 0f, 50f);
        const float IceHalf = 35f, WallFrontZ = 119.5f, VanFrontZ = 2.515f;

        VanVehicle _van;
        public static bool Running { get; private set; }

        public static string Run(string scenario)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return "not the server";
            if (Running) return "already running";
            var van = FindFirstObjectByType<VanVehicle>();
            if (van == null) return "no van";
            var go = new GameObject("VanBenchRunner");
            var r = go.AddComponent<VanBenchRunner>();
            r._van = van;
            r.StartCoroutine(r.Main(scenario));
            return "started " + scenario;
        }

        IEnumerator Main(string scenario)
        {
            Running = true;
            BenchLog.Event("van_bench_start", "scenario", scenario);
            bool all = scenario == "all";
            if (all || scenario == "slopes") yield return Slopes();
            if (all || scenario == "hold") yield return Hold();
            if (all || scenario == "ice") yield return Ice();
            if (all || scenario == "wall") yield return Wall();
            if (all || scenario == "door") yield return DoorBlock();
            if (all || scenario == "zones") yield return Zones();
            if (all || scenario == "electrics") yield return Electrics();
            _van.BenchIntent = null;
            _van.ServerSetHandbrake(true);
            _van.ServerTeleport(Vector3.zero, Quaternion.identity);
            BenchLog.Event("van_bench_done", "scenario", scenario);
            Running = false;
            Destroy(gameObject);
        }

        Rigidbody Body => _van.Body;
        float Kmh => Vector3.Dot(Body.linearVelocity, _van.transform.forward) * 3.6f;

        IEnumerator Place(Vector3 p, Quaternion r, bool frontAxle, bool low, bool engine)
        {
            _van.BenchIntent = Vector2.zero;
            _van.ServerSetHandbrake(true);
            _van.ServerTeleport(p + Vector3.up * 0.05f, r);
            _van.ServerSetFrontAxle(frontAxle);
            yield return new WaitForFixedUpdate();
            _van.ServerSetLowRange(low, out _);
            _van.ServerSetEngine(engine);
            yield return new WaitForSeconds(1.5f);                 // settle on the springs
        }

        // ---- AT-V06: climb each slope with H / L, rear / both axles
        IEnumerator Slopes()
        {
            foreach (var (grade, x) in Ramps)
                foreach (bool front in new[] { false, true })
                    foreach (bool low in new[] { false, true })
                    {
                        // a start on the slope itself (3 m up, all wheels on it): from a run-up the van carried a 30 % climb
                        // on momentum, the traction deficit there being under 1 % of its weight (bench 21.10)
                        float a = Mathf.Atan(grade), topZ = RampStartZ + RampLength * Mathf.Cos(a), topY = RampLength * Mathf.Sin(a);
                        yield return Place(new Vector3(x, 3f * Mathf.Sin(a), RampStartZ + 3f * Mathf.Cos(a)),
                                           Quaternion.Euler(-a * Mathf.Rad2Deg, 0f, 0f), front, low, true);
                        _van.ServerSetHandbrake(false);
                        _van.BenchIntent = new Vector2(1f, 0f);
                        float t0 = Time.time, slow = 0f, maxY = 0f;
                        string result = "timeout";
                        while (Time.time - t0 < 45f)
                        {
                            yield return new WaitForFixedUpdate();
                            Vector3 p = _van.transform.position;
                            maxY = Mathf.Max(maxY, p.y);
                            if (p.z > topZ + 3f) { result = "climbed"; break; }
                            slow = Mathf.Abs(Kmh) < 1f && Time.time - t0 > 4f ? slow + Time.fixedDeltaTime : 0f;
                            if (slow > 2.5f || (Time.time - t0 > 2f && Kmh < -3f)) { result = "stuck"; break; }
                        }
                        BenchLog.Event("van_bench", "test", "slope", "grade", grade, "front", front, "low", low, "result", result,
                            "time", Time.time - t0, "maxHeight", maxY, "top", topY, "rpm", _van.Motion.EngineRpm, "gear", _van.Motion.Gear);
                    }
        }

        // ---- AT-V06: hand brake on each slope (engine off), rear axle and with the front axle engaged
        IEnumerator Hold()
        {
            foreach (var (grade, x) in Ramps)
                foreach (bool front in new[] { false, true })
                {
                    float a = Mathf.Atan(grade), s = 20f;
                    var p = new Vector3(x, s * Mathf.Sin(a), RampStartZ + s * Mathf.Cos(a));
                    yield return Place(p, Quaternion.Euler(-a * Mathf.Rad2Deg, 0f, 0f), front, false, false);
                    Vector3 p0 = _van.transform.position;
                    yield return new WaitForSeconds(5f);
                    float slid = Vector3.Distance(_van.transform.position, p0);
                    BenchLog.Event("van_bench", "test", "handbrake", "grade", grade, "front", front, "slidM", slid,
                        "result", slid < 0.1f ? "holds" : "slides", "kmh", Kmh);
                }
        }

        // ---- AT-V06: brake on ice (locked wheels), then steer on ice (slide)
        IEnumerator Ice()
        {
            float iceStart = IceCenter.z - IceHalf;
            for (int run = 0; run < 2; run++)
            {
                yield return Place(new Vector3(IceCenter.x, 0f, -70f), Quaternion.identity, true, false, true);
                _van.ServerSetHandbrake(false);
                _van.BenchIntent = new Vector2(1f, 0f);
                float t0 = Time.time;
                while (_van.transform.position.z < iceStart - 25f && Time.time - t0 < 40f)
                {
                    _van.BenchIntent = new Vector2(Kmh < 40f ? 1f : 0f, 0f);
                    yield return new WaitForFixedUpdate();
                }
                while (_van.transform.position.z < iceStart + 2f && Time.time - t0 < 50f)
                {
                    _van.BenchIntent = new Vector2(0f, 0f);
                    yield return new WaitForFixedUpdate();
                }
                float v0 = Kmh, z0 = _van.transform.position.z, yaw0 = _van.transform.eulerAngles.y;
                bool locked = false;
                float maxYawRate = 0f;
                _van.BenchIntent = run == 0 ? new Vector2(-1f, 0f) : new Vector2(0f, 1f);
                float t1 = Time.time, sumN = 0f, sumLon = 0f, sumMu = 0f;
                int samples = 0;
                while (Time.time - t1 < 8f && (run == 1 || Kmh > 0.5f))
                {
                    yield return new WaitForFixedUpdate();
                    locked |= _van.Motion.Wheels[0].Locked && _van.Motion.Wheels[2].Locked;
                    maxYawRate = Mathf.Max(maxYawRate, Mathf.Abs(Body.angularVelocity.y) * Mathf.Rad2Deg);
                    if (Kmh < 3f) continue;
                    float n = 0f, lon = 0f;
                    foreach (VanWheel w in _van.Motion.Wheels) { n += w.NormalForce; lon += w.LongForce; }
                    sumN += n; sumLon += lon; samples++;
                    sumMu += _van.LastMu;
                }
                float weight = Body.mass * 9.81f;
                BenchLog.Event("van_bench", "test", run == 0 ? "ice_brake" : "ice_steer", "fromKmh", v0,
                    "distance", _van.transform.position.z - z0, "locked", locked, "yawDeg", Mathf.DeltaAngle(yaw0, _van.transform.eulerAngles.y),
                    "maxYawRateDeg", maxYawRate, "kmhEnd", Kmh, "avgN_per_weight", samples > 0 ? sumN / samples / weight : 0f,
                    "avgLon_per_weight", samples > 0 ? sumLon / samples / weight : 0f, "avgMu", samples > 0 ? sumMu / samples : 0f,
                    "decelFromDistance_g", (v0 / 3.6f) * (v0 / 3.6f) / (2f * Mathf.Max(0.1f, _van.transform.position.z - z0)) / 9.81f);
            }
        }

        // ---- AT-V07: into the wall at 20 km/h
        IEnumerator Wall()
        {
            yield return Place(new Vector3(0f, 0f, 70f), Quaternion.identity, true, false, true);
            _van.ServerSetHandbrake(false);
            float t0 = Time.time, impact = 0f, maxUp = 0f, maxSpin = 0f, deepest = 0f;
            bool hit = false;
            while (Time.time - t0 < 25f)
            {
                float front = _van.transform.position.z + VanFrontZ;
                _van.BenchIntent = new Vector2(!hit && Kmh < 20f && front < WallFrontZ - 4f ? 1f : 0f, 0f);
                yield return new WaitForFixedUpdate();
                if (!hit && front > WallFrontZ - 0.3f) { hit = true; impact = Kmh; t0 = Time.time - 20f; }
                if (hit)
                {
                    maxUp = Mathf.Max(maxUp, Body.linearVelocity.y);
                    maxSpin = Mathf.Max(maxSpin, Body.angularVelocity.magnitude * Mathf.Rad2Deg);
                    deepest = Mathf.Max(deepest, _van.transform.position.z + VanFrontZ - WallFrontZ);
                }
            }
            BenchLog.Event("van_bench", "test", "wall", "hit", hit, "impactKmh", impact, "maxUpMs", maxUp, "maxSpinDeg", maxSpin,
                "penetrationM", deepest, "p", _van.transform.position, "tiltDeg", Vector3.Angle(_van.transform.up, Vector3.up));
        }

        // ---- AT-V08: every zone the game has, used once (press, then release) by the host, seated in the driver's seat (the
        // cab controls need a seat, user decision 21.10); either the state changes or a clear refusal. Reach and the ray are
        // the sensor's and are not tested here: this is the server's table.
        IEnumerator Zones()
        {
            yield return Place(Vector3.zero, Quaternion.identity, false, false, false);
            ulong host = NetworkManager.ServerClientId;
            _van.ServerUse(host, "Sit_Driver", Interaction.UsePhase.Press, out _);
            yield return new WaitForSeconds(0.5f);
            int changed = 0, refused = 0, silent = 0;
            var ids = new List<string>();
            foreach (var v in _van.Volumes) ids.Add(v.id);
            ids.Sort(System.StringComparer.Ordinal);
            foreach (string id in ids)
            {
                string before = _van.Describe(id) ?? "";
                bool ok = _van.ServerUse(host, id, Interaction.UsePhase.Press, out string reason);
                yield return new WaitForSeconds(0.1f);
                string during = _van.Describe(id) ?? "";
                _van.ServerUse(host, id, Interaction.UsePhase.Release, out _);
                yield return new WaitForSeconds(0.1f);
                string after = _van.Describe(id) ?? "";
                bool change = before != during || before != after;
                if (!ok) refused++; else if (change) changed++; else silent++;
                BenchLog.Event("van_zone", "zone", id, "ok", ok, "reason", reason ?? "", "before", before, "during", during, "after", after,
                    "result", !ok ? "refused" : change ? "changed" : "no_change");
            }
            BenchLog.Event("van_bench", "test", "zones", "total", ids.Count, "changed", changed, "refused", refused, "noChange", silent);
            Snow.Player.PlayerNet.Local?.RequestExit();             // the normal exit path, so the host's seat state follows
            yield return new WaitForSeconds(1f);
        }

        // ---- AT-V10: headlights with the engine off drain the battery (time × scale); AT-V11: the headlight fuse out and a spare in
        IEnumerator Electrics()
        {
            yield return Place(Vector3.zero, Quaternion.identity, false, false, false);
            _van.ServerSetControl("MassSwitch_Key", 1);
            _van.ServerSetControl("Toggle_Head", 1);
            foreach (var (start, tempC, scale, minutes) in new[] { (0.22f, -10f, 120f, 50f), (1.0f, -10f, 1200f, 300f) })
            {
                _van.ServerSetBattery(start, tempC);
                _van.electricsTimeScale = scale;
                float t0 = Time.time, nextLog = 0f, clickAt = -1f;
                while ((Time.time - t0) * scale / 60f < minutes)
                {
                    yield return new WaitForFixedUpdate();
                    float gameMin = (Time.time - t0) * scale / 60f;
                    if (clickAt < 0f && _van.StarterBusVolts < 8f) clickAt = gameMin;
                    if (gameMin < nextLog) continue;
                    nextLog += minutes / 10f;
                    BenchLog.Event("van_bench", "test", "drain", "start", start, "gameMin", gameMin, "charge", _van.Electrics.Charge,
                        "busV", _van.Electrics.BusVolts, "amps", _van.Electrics.BatteryAmps, "starterBusV", _van.StarterBusVolts,
                        "headOutput", _van.rig.LampOutput("Head_L"));
                }
                BenchLog.Event("van_bench", "test", "drain_result", "start", start, "tempC", tempC, "minutes", minutes,
                    "chargeEnd", _van.Electrics.Charge, "starterClicksFromMin", clickAt);
            }
            _van.electricsTimeScale = 1f;

            // AT-V11: full battery, headlights on; out with Fuse_1 (hands empty), then a spare from the tool box into the slot
            _van.ServerSetBattery(1f, 20f);
            yield return new WaitForSeconds(1f);
            ulong host = NetworkManager.ServerClientId;
            float on = _van.rig.LampOutput("Head_L");
            bool outOk = _van.ServerUse(host, "Fuse_1", Interaction.UsePhase.Press, out string r1);
            yield return new WaitForSeconds(1f);
            float off = _van.rig.LampOutput("Head_L");
            // the forest start: the fuse in hand goes down on the ground, the slot gets a blown fuse; out with it, a spare in
            var inHand = Snow.Cargo.CargoItem.HeldBy(host);
            if (inHand != null) { inHand.ServerRelease(host); Snow.Player.PlayerNet.Local.ServerForgetHeld(inHand); }
            _van.ServerSetFuse(0, VanVehicle.FuseBlown);
            yield return new WaitForSeconds(0.5f);
            float blown = _van.rig.LampOutput("Head_L");
            bool blownOut = _van.ServerUse(host, "Fuse_1", Interaction.UsePhase.Press, out _);
            bool spareOk = _van.ServerUse(host, "Spare_Fuse_1", Interaction.UsePhase.Press, out string r2);
            yield return new WaitForSeconds(0.5f);
            string held = Snow.Cargo.CargoItem.HeldBy(host) != null ? Snow.Cargo.CargoItem.HeldBy(host).itemKey : "nothing";
            bool inOk = _van.ServerUse(host, "Fuse_1", Interaction.UsePhase.Press, out string r3);
            yield return new WaitForSeconds(1f);
            float again = _van.rig.LampOutput("Head_L");
            BenchLog.Event("van_bench", "test", "fuse", "headOn", on, "fuseOut", outOk, "r1", r1 ?? "", "headAfterOut", off,
                "headBlown", blown, "blownThrownOut", blownOut, "spareTaken", spareOk, "r2", r2 ?? "", "held", held, "fuseIn", inOk, "r3", r3 ?? "", "headAfterIn", again,
                "result", on > 0.5f && off < 0.05f && blown < 0.05f && blownOut && spareOk && again > 0.5f ? "pass" : "fail");
            _van.ServerSetControl("Toggle_Head", 0);
        }

        // ---- AT-V07 (spec AT-14): a box in the driver's doorway, then close the door
        IEnumerator DoorBlock()
        {
            yield return Place(new Vector3(0f, 0f, 0f), Quaternion.identity, false, false, false);
            NetworkDoor door = _van.Door("doorL");
            door.Command(true, DoorDrive.Manual);
            yield return new WaitForSeconds(2f);
            GameObject prefab = null;
            foreach (var p in NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs)
                if (p.Prefab != null && p.Prefab.name == "VanItem_ToolBox_Net") prefab = p.Prefab;
            if (prefab == null) { BenchLog.Event("van_bench", "test", "door_block", "result", "no box prefab"); yield break; }
            // in the arc of the door panel: 0.45 m out from the side, half a door length back from the hinge
            Vector3 at = _van.transform.TransformPoint(new Vector3(-1.25f, 0.9f, 1.15f));
            var box = Instantiate(prefab, at, _van.transform.rotation);
            box.GetComponent<NetworkObject>().Spawn();
            var rb = box.GetComponent<Rigidbody>();
            rb.isKinematic = true;                                  // held in the doorway (a player holding it)
            yield return new WaitForSeconds(0.5f);
            door.Command(false, DoorDrive.Manual);
            yield return new WaitForSeconds(3f);
            Vector3 moved = box.transform.position - at;
            BenchLog.Event("van_bench", "test", "door_block", "doorState", door.State.ToString(), "doorPos", door.Position,
                "boxMovedM", moved.magnitude, "result", door.State != DoorState.Closed && moved.magnitude < 0.01f ? "blocked" : "closed_through");
            box.GetComponent<NetworkObject>().Despawn(true);
            door.Command(false, DoorDrive.Manual);
        }
    }
}
