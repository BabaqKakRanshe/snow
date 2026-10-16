"""
Network bench runner for P01+ acceptance tests (PLANE_PROMPT §15). Launches separate game processes (a built
player, headless) with -snow-* arguments, waits for them and analyses their JSON-lines logs.

    python Tools/Bench/bench.py list
    python Tools/Bench/bench.py run at01            # host + 3 clients, players disperse
    python Tools/Bench/bench.py analyze <run-dir>

Every run writes Logs/bench/<scenario>-<timestamp>/ with one <name>.jsonl (BenchLog) and <name>-unity.log per process,
plus report.json. Bench geometry (P01_NetBench): spawns at x -3,-1,1,3 z 0; cabin at (0,0,-8), door on its left
(-X) side, door button post at (-1.75,-7.0), right side open, crate on the right exit point; HL course at z 8..12.
"""
import json
import math
import os
import subprocess
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
EXE = os.path.join(ROOT, "Builds", "P01", "Snow.exe")
# P02 scenarios need a build whose first scene is Assets/Snow/Bench/P02_PhysicsBench.unity
EXE_P02 = os.path.join(ROOT, "Builds", "P02", "Snow.exe")

# walk from the left door around the back of the cabin to its open right side (the crate sits on the right exit point)
AROUND_TO_RIGHT = "goto:-2.4:-9.8:0.3;goto:1.8:-9.8:0.3;goto:1.7:-7.0:0.2"
# The door panel sweeps a quarter disc of radius 0.98 m around the hinge (-1.2, -7.35): anyone inside it blocks the door
# (DOOR-01, correct), so bots operate the door from (-2.4, -7.95) and step in to (-1.9, ...) only when it is open.
DOOR_SPOT = "goto:-2.4:-7.95:0.2"

SCENARIOS = {
    "at01": {
        "desc": "AT-01: host + 3 clients, everyone sprints 15 s in a different direction, then stops",
        "procs": [
            ("host", 0, ["-snow-host", "-snow-bot", "wander:90:15:sprint", "-snow-quit-after", "40"]),
            ("c1", 3, ["-snow-client", "127.0.0.1", "-snow-bot", "wander:270:15:sprint", "-snow-quit-after", "36"]),
            ("c2", 3.5, ["-snow-client", "127.0.0.1", "-snow-bot", "wander:225:15:sprint", "-snow-quit-after", "36"]),
            ("c3", 4, ["-snow-client", "127.0.0.1", "-snow-bot", "wander:135:15:sprint", "-snow-quit-after", "36"]),
        ],
    },
    "at02": {
        "desc": "AT-02: two clients press 'use' on the left seat at the same server time; the loser takes the right "
                "seat and tries flight axes through the client (not sent) and forced through the RPC (server drops)",
        "procs": [
            ("host", 0, ["-snow-host", "-snow-bot", "wait:80", "-snow-quit-after", "70"]),
            ("c1", 3, ["-snow-client", "127.0.0.1", "-snow-quit-after", "66", "-snow-bot",
                       DOOR_SPOT + ";look:Button_Door_Out;use;wait:3;goto:-1.9:-7.85:0.15;atserver:25;look:Seat_P;use;wait:1;log:race;"
                       "atserver:35;axes:0.5:0.5:0:1:2;log:pilot_axes_done"]),
            ("c2", 3.5, ["-snow-client", "127.0.0.1", "-snow-quit-after", "66", "-snow-bot",
                         "goto:-2.6:-9.2:0.2;atserver:20;goto:-1.9:-8.6:0.15;atserver:25;look:Seat_P;use;wait:1;log:race;"
                         + AROUND_TO_RIGHT + ";look:Seat_C;use;wait:1;log:right_seat;"
                         "atserver:40;forceaxes:1:1:1:1:2;log:forced_done;atserver:44;axes:1:1:1:1:2;log:client_gate_done"]),
        ],
    },
    "at03": {
        "desc": "AT-03 static: a client opens the door by hand, sits, closes it from inside, is refused the exit, "
                "opens with the cabin button, gets out; then the blocked right exit is refused",
        "procs": [
            ("host", 0, ["-snow-host", "-snow-bot", "wait:80", "-snow-quit-after", "60"]),
            ("c1", 3, ["-snow-client", "127.0.0.1", "-snow-quit-after", "56", "-snow-bot",
                       DOOR_SPOT + ";look:Door_Handle_Out;use;wait:1.6;goto:-1.9:-7.95:0.15;look:Seat_P;use;wait:1;log:seated;"
                       "look:Door_Handle_In;use;wait:1.6;log:closed_inside;exit;wait:1;log:exit_door_closed;"
                       "look:Button_Door_In;use;wait:3;exit;wait:1;log:exited_left;"
                       + AROUND_TO_RIGHT + ";look:Seat_C;use;wait:1;exit;wait:1;log:exit_blocked"]),
        ],
    },
    "athl1": {
        "desc": "AT-HL1: client at ~100 ms RTT (simulator on the client, 42 ms each way): run, stop, strafe, sprint, jump, "
                "running crouch-jump onto the 0.70 m ledge, bunny hops; host traces the remote player every frame",
        "procs": [
            ("host", 0, ["-snow-host", "-snow-trace", "-snow-bot", "wait:120", "-snow-quit-after", "75"]),
            ("c1", 3, ["-snow-client", "127.0.0.1", "-snow-sim-delay", "42", "-snow-trace", "-snow-quit-after", "71", "-snow-bot",
                       "goto:-30:-20:0.3;face:0;wait:1;log:accel;run:0:1:2;log:stop;wait:1.5;log:strafe;run:1:0:1.5;wait:1;"
                       "log:sprint;run:0:1:2:sprint;wait:1.5;log:jump;jump;wait:1.5;"
                       "goto:3:4.5:0.2;face:0;wait:0.5;log:crouchjump;leap:0.9:crouch;wait:1;log:ledge;"
                       "goto:-30:-30:0.3;face:0;wait:0.5;log:bhop;bhop:6;wait:1;log:end"]),
        ],
    },
    # ------------------------------------------------------------------ P02 (network physics, variant A)
    # P02 bench: carrier at (0, ~0.56, -25) facing +Z, bay door on its left (x -1.05, z -25.0..-26.5) with boarding steps,
    # seats in the open cab (z ~ -22.7), tie-downs 01..04 in the bay; Case5 (60 kg) outside the bay door at (-3.2, -25.8),
    # Case4 (10 kg) at (-6, -27), Case6 (7 kg) at (-7.5, -27); in the bay Case1, Case2, Case3 (stacked on Case1).
    "at07": {
        "exe": EXE_P02,
        "desc": "AT-07: grab Case4 at the centre of its top face and lift, release; then at a corner and lift: hanging differs",
        "procs": [
            ("host", 0, ["-snow-host", "-snow-trace", "-snow-bot", "wait:80", "-snow-quit-after", "45"]),
            ("c1", 3, ["-snow-client", "127.0.0.1", "-snow-trace", "-snow-quit-after", "41", "-snow-bot",
                       "goto:-6:-26.1:0.15;grab:Cargo_Case4_Net:0:0.347:0;lookat:-6:1.7:-26.9;wait:3;log:held_centre;release;wait:2;"
                       "grab:Cargo_Case4_Net:0.25:0.347:0.22;lookat:-6:1.7:-26.9;wait:3;log:held_corner;release;wait:2;log:end"]),
        ],
    },
    "at08": {
        "exe": EXE_P02,
        "desc": "AT-08/AT-09: the 60 kg Case5 - one client tries to lift it, then a second client takes the other end and "
                "both lift; then the second client disconnects (quits) and only his grab disappears",
        "procs": [
            ("host", 0, ["-snow-host", "-snow-trace", "-snow-bot", "wait:120", "-snow-quit-after", "60"]),
            ("c1", 3, ["-snow-client", "127.0.0.1", "-snow-trace", "-snow-quit-after", "56", "-snow-bot",
                       "goto:-3.2:-24.9:0.15;atserver:15;grab:Cargo_Case5_Net:0:0.647:0.3;lookat:-3.2:2.0:-25.6;wait:4;log:alone;"
                       "atserver:25;lookat:-3.2:2.0:-25.6;wait:6;log:together;atserver:33;wait:6;log:after_c2_left;release;log:end"]),
            ("c2", 3.5, ["-snow-client", "127.0.0.1", "-snow-trace", "-snow-quit-after", "32", "-snow-bot",
                         "goto:-4.2:-27.2:0.2;goto:-3.2:-26.75:0.15;atserver:23;grab:Cargo_Case5_Net:0:0.647:-0.3;"
                         "atserver:25;lookat:-3.2:2.0:-26.0;wait:6;log:together"]),
        ],
    },
    "at10open": {
        "exe": EXE_P02,
        "desc": "AT-10: loose cargo in the bay, bay door OPEN, the driver goes over bumps and turns right (cargo slides left)",
        "procs": [
            ("host", 0, ["-snow-host", "-snow-bot", "wait:120", "-snow-quit-after", "50"]),
            ("c1", 3, ["-snow-client", "127.0.0.1", "-snow-quit-after", "46", "-snow-bot",
                       "goto:-2.0:-22.7:0.15;look:Seat_P;use;wait:1;look:Button_BayDoor;use;wait:3.5;log:door_open;"
                       "axes:1:0:0:0:5;axes:1:1:0:0:3;axes:-1:0:0:0:3;log:end"]),
        ],
    },
    "at10closed": {
        "exe": EXE_P02,
        "desc": "AT-10: the same drive with the bay door CLOSED: the door holds the cargo by collision",
        "procs": [
            ("host", 0, ["-snow-host", "-snow-bot", "wait:120", "-snow-quit-after", "46"]),
            ("c1", 3, ["-snow-client", "127.0.0.1", "-snow-quit-after", "42", "-snow-bot",
                       "goto:-2.0:-22.7:0.15;look:Seat_P;use;wait:1;log:door_closed;"
                       "axes:1:0:0:0:5;axes:1:1:0:0:3;axes:-1:0:0:0:3;log:end"]),
        ],
    },
    "at11": {
        "exe": EXE_P02,
        "desc": "AT-11 + AT-HL3: a client climbs into the bay and secures Case1; the driver sets off; the rider (standing "
                "in the bay, parented to the vehicle) releases Case1 while moving: it keeps the vehicle's velocity",
        "procs": [
            ("host", 0, ["-snow-host", "-snow-trace", "-snow-bot", "wait:120", "-snow-quit-after", "60"]),
            ("c1", 3, ["-snow-client", "127.0.0.1", "-snow-quit-after", "56", "-snow-bot",
                       "goto:-2.0:-22.7:0.15;look:Seat_P;use;wait:1;look:Button_BayDoor;use;wait:3.5;"
                       "atserver:30;look:Button_BayDoor;use;wait:3;axes:0.6:0:0:0:8;axes:0:0:0:0:3;log:end"]),
            ("c2", 3.5, ["-snow-client", "127.0.0.1", "-snow-trace", "-snow-quit-after", "56", "-snow-bot",
                         "atserver:16;goto:-2.2:-25.8:0.15;goto:-1.5:-25.8:0.1;goto:-0.8:-25.9:0.15;log:in_bay;"
                         "grab:Cargo_Case1_Net:0:0.467:0;release;secure;wait:1;log:secured;atserver:36;log:riding;"
                         "atserver:39;grab:Cargo_Case1_Net:0:0.467:0;secure;release;wait:3;log:released;wait:4;log:end"]),
        ],
    },
    "at18": {
        "exe": EXE_P02,
        "desc": "AT-18: two clients press R on the same item at the same server time; then both grab it at once",
        "procs": [
            ("host", 0, ["-snow-host", "-snow-bot", "wait:120", "-snow-quit-after", "50"]),
            ("c1", 3, ["-snow-client", "127.0.0.1", "-snow-quit-after", "46", "-snow-bot",
                       "atserver:14;goto:-2.2:-25.6:0.15;goto:-1.5:-25.6:0.1;goto:-0.9:-25.6:0.15;"
                       "atserver:28;grab:Cargo_Case1_Net:0:0.467:0;release;atserver:30;secure;wait:1;log:secure_race;"
                       "atserver:33;secure;wait:1;atserver:35;grab:Cargo_Case1_Net:0:0.467:0.2;wait:3;log:co_grab;release"]),
            ("c2", 3.5, ["-snow-client", "127.0.0.1", "-snow-quit-after", "46", "-snow-bot",
                         "atserver:18;goto:-2.2:-26.3:0.15;goto:-1.5:-26.3:0.1;goto:-0.9:-26.3:0.15;"
                         "atserver:28;grab:Cargo_Case1_Net:0:0.467:0;release;atserver:30;secure;wait:1;log:secure_race;"
                         "atserver:35;grab:Cargo_Case1_Net:0:0.467:-0.2;wait:3;log:co_grab;release"]),
        ],
    },
    "athl2": {
        "desc": "AT-HL2: 'use' targets from the ground (door handle, button, crate, seat) and from the seat (panel button, "
                "inner handle); hints logged",
        "procs": [
            ("host", 0, ["-snow-host", "-snow-bot", "wait:80", "-snow-quit-after", "45"]),
            ("c1", 3, ["-snow-client", "127.0.0.1", "-snow-quit-after", "41", "-snow-bot",
                       DOOR_SPOT + ";look:Door_Handle_Out;look:Button_Door_Out;use;wait:3;goto:-1.9:-7.95:0.15;"
                       "look:Seat_P;use;wait:1;look:Button_Door_In;look:Door_Handle_In;look:Seat_C;exit;wait:1;"
                       + AROUND_TO_RIGHT + ";lookat:1.9:0.35:-7.85;log:done"]),
        ],
    },
}


def _at19_variants():
    """AT-19: AT-08 and AT-11 under the Unity Transport simulator on the clients: RTT ~100 / ~200 ms, loss 0 / 1 / 3 %."""
    for delay in (50, 100):
        for loss in (0, 1, 3):
            for base in ("at08", "at11"):
                sc = SCENARIOS[base]
                procs = []
                for pname, start, args in sc["procs"]:
                    extra = [] if pname == "host" else ["-snow-sim-delay", str(delay), "-snow-sim-loss", str(loss)]
                    procs.append((pname, start, args + extra))
                SCENARIOS[f"at19_{base}_d{delay}_l{loss}"] = {
                    "exe": sc["exe"], "desc": f"AT-19: {base} with {delay} ms each way and {loss}% loss on the clients",
                    "procs": procs, "base": base}


_at19_variants()


def run(name):
    sc = SCENARIOS[name]
    exe = sc.get("exe", EXE)
    out = os.path.join(ROOT, "Logs", "bench", f"{name}-{time.strftime('%Y%m%d-%H%M%S')}")
    os.makedirs(out, exist_ok=True)
    print(sc["desc"], "->", out)
    procs, t0 = [], time.time()
    for pname, delay, args in sc["procs"]:
        while time.time() - t0 < delay:
            time.sleep(0.1)
        cmd = [exe, "-batchmode", "-nographics", "-logFile", os.path.join(out, pname + "-unity.log"),
               "-snow-log", os.path.join(out, pname + ".jsonl")] + args
        procs.append((pname, subprocess.Popen(cmd, cwd=os.path.dirname(exe))))
        print(f"  started {pname}: {' '.join(args)}")
    for pname, p in procs:
        try:
            p.wait(timeout=240)
        except subprocess.TimeoutExpired:
            p.kill()
            print(f"  {pname} killed on timeout")
        print(f"  {pname} exit {p.returncode}")
    analyze(out, name)
    return out


def load(path):
    rows = []
    with open(path, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line:
                rows.append(json.loads(line))
    return rows


def ev(rows, name, **match):
    return [r for r in rows if r["ev"] == name and all(r.get(k) == v for k, v in match.items())]


# ---------------------------------------------------------------------------------------------- common analysis
def common(logs):
    report, own_final, seen_final = {}, {}, {}
    for name, rows in logs.items():
        counts = {}
        for r in rows:
            counts[r["ev"]] = counts.get(r["ev"], 0) + 1
        rtts = [r["ms"] for r in rows if r["ev"] == "rtt"]
        # the last snapshot in which this process saw the most players (the host's quit snapshot comes after the
        # clients have left, so "last" alone would hide its view of them)
        by_time = {}
        for r in rows:
            if r["ev"] == "pos":
                by_time.setdefault(round(r["t"], 1), []).append(r)   # one snapshot's lines are microseconds apart
        best = max(len(v) for v in by_time.values()) if by_time else 0
        final_t = max((t for t, v in by_time.items() if len(v) == best), default=None)
        final = by_time.get(final_t, [])
        spawns = {r["owner"]: r["p"] for r in rows if r["ev"] == "player_spawn"}
        info = {
            "events": counts,
            "rtt_ms": {"min": min(rtts), "max": max(rtts), "mean": round(sum(rtts) / len(rtts), 1)} if rtts else None,
            "players_seen_at_end": sorted({r["owner"] for r in final}),
            "implausible_moves": counts.get("implausible_move", 0),
            "bot_timeouts": [r["step"] for r in ev(rows, "bot_timeout")],
            "unknown_steps": [r["step"] for r in ev(rows, "bot_unknown_step")],
        }
        for r in final:
            if r["local"]:
                own_final[r["owner"]] = (name, r["p"])
                if r["owner"] in spawns:
                    info["own_travel_m"] = round(math.dist(spawns[r["owner"]], r["p"]), 2)
            else:
                seen_final.setdefault(r["owner"], []).append((name, r["p"]))
        report[name] = info
    worst, pairs = 0.0, []
    for owner, (oname, op) in own_final.items():
        for vname, vp in seen_final.get(owner, []):
            d = math.dist(op, vp)
            worst = max(worst, d)
            pairs.append({"owner": owner, "owner_proc": oname, "viewer": vname, "err_m": round(d, 4)})
    return report, {"worst_err_m": round(worst, 4), "pairs": pairs}


# ---------------------------------------------------------------------------------------------- per-test checks
def check_at02(logs):
    host = logs["host"]
    enters = ev(host, "seat_enter")
    pilots = [r for r in enters if r["seat"] == "Seat_P"]
    uses_p = [r for r in ev(host, "use") if r["id"] == "Seat_P"]
    snaps = ev(host, "vehicle")
    rejected = max((r["rejectedAxes"] for r in snaps), default=0)
    pilot = pilots[0]["client"] if pilots else None
    return {
        "seat_P_requests": [(r["client"], r["ok"], r["reason"]) for r in uses_p],
        "seat_P_winners": [r["client"] for r in pilots],
        "PASS_one_pilot": len(pilots) == 1 and len(uses_p) >= 2,
        "right_seat": [r["client"] for r in enters if r["seat"] == "Seat_C"],
        "axes_rejected_on_server": rejected,
        "max_pitch_seen": max((r["pitch"] for r in snaps), default=0),
        "PASS_passenger_blocked": rejected > 0 and all(abs(r["pitch"]) < 0.6 for r in snaps),
        "pilot": pilot,
    }


def check_at03(logs):
    host = logs["host"]
    c1 = logs["c1"]
    doors = [(r["state"], round(r["pos"], 2)) for r in ev(host, "door_state")]
    refusals = [r["reason"] for r in ev(host, "exit_refused")]
    exits = ev(host, "seat_exit")
    left = [r for r in ev(c1, "left_seat")]
    return {
        "door_states_on_host": doors,
        "exit_refusals": refusals,
        "PASS_refused_door_closed": "Дверь закрыта" in refusals,
        "PASS_refused_no_room": "Нет места для выхода" in refusals,
        "exits": [(r["seat"], r["p"]) for r in exits],
        "PASS_exit_left_outside": any(r["seat"] == "Seat_P" and r["p"][0] < -1.6 for r in exits),
        "owner_left_seat_at": [r["p"] for r in left],
        "PASS_client_opened_door": any(r["client"] != 0 for r in ev(host, "use") if r["id"] in ("Door_Handle_Out", "Button_Door_In") and r["ok"]),
    }


def check_athl2(logs):
    c1 = logs["c1"]
    targets = [(r["want"], r["got"], r["hint"], r["ctx"]) for r in ev(c1, "target")]
    return {
        "targets": targets,
        "PASS_all_hit": all(w == g or (w.startswith("lookat") and g.startswith("grab:")) for w, g, _, _ in targets),
        "PASS_seat_context": any(ctx != "OnFoot" for _, _, _, ctx in targets),
    }


def check_athl1(logs):
    """Owner-side movement numbers from 'opos' (every physics step) and remote smoothness from host 'rpos'."""
    c1, host = logs["c1"], logs["host"]
    o = ev(c1, "opos")
    tags = {r["tag"]: r["t"] for r in ev(c1, "bot_log")}
    rows = sorted(o, key=lambda r: r["t"])

    def window(a, b):
        ta, tb = tags.get(a), tags.get(b)
        return [r for r in rows if ta is not None and tb is not None and ta <= r["t"] <= tb]

    def hspeed(r):
        return math.hypot(r["v"][0], r["v"][2])

    res = {}
    acc = window("accel", "stop")
    if acc:
        t0 = acc[0]["t"]
        reach = next((r["t"] - t0 for r in acc if hspeed(r) >= 0.99 * 3.6), None)
        res["run_speed_max"] = round(max(hspeed(r) for r in acc), 3)
        res["time_to_99pct_run_s"] = round(reach, 3) if reach is not None else None
    stp = window("stop", "strafe")
    if stp:
        moving = [r for r in stp if hspeed(r) > 0.01]
        res["stop_time_s"] = round(moving[-1]["t"] - stp[0]["t"], 3) if moving else 0.0
    sf = window("strafe", "sprint")
    if sf:
        res["strafe_speed_max"] = round(max(hspeed(r) for r in sf), 3)
    sp = window("sprint", "jump")
    if sp:
        res["sprint_speed_max"] = round(max(hspeed(r) for r in sp), 3)
    jp = window("jump", "crouchjump")
    if jp:
        base = jp[0]["p"][1]
        res["jump_apex_m"] = round(max(r["p"][1] for r in jp) - base, 3)
    led = window("ledge", "bhop")
    if led:
        res["after_crouch_jump_y"] = round(led[0]["p"][1], 3)
    bh = window("bhop", "end")
    if bh:
        takeoffs = [hspeed(b) for a, b in zip(bh, bh[1:]) if a["g"] and not b["g"]]
        res["bhop_takeoffs"] = len(takeoffs)
        res["bhop_takeoff_speed_max"] = round(max(takeoffs), 3) if takeoffs else None
        res["PASS_bhop_cap"] = bool(takeoffs) and max(takeoffs) <= 1.1 * 3.6 + 0.05
    # remote view on the host: per-frame speed spikes and lag error against the owner's own trace (by server time)
    r = sorted([x for x in ev(host, "rpos")], key=lambda x: x["st"])
    if r and rows:
        spikes, worst_step = 0, 0.0
        for a, b in zip(r, r[1:]):
            dt = b["st"] - a["st"]
            if dt <= 1e-4:
                continue
            v = math.dist(a["p"], b["p"]) / dt
            worst_step = max(worst_step, v)
            if v > 1.5 * 6.1:
                spikes += 1
        own = sorted(rows, key=lambda x: x["st"])
        errs = []
        j = 0
        for s in r:
            while j + 1 < len(own) and own[j + 1]["st"] <= s["st"]:
                j += 1
            errs.append(math.dist(own[j]["p"], s["p"]))
        errs.sort()
        res["remote_frames"] = len(r)
        res["remote_speed_spikes_over_9mps"] = spikes
        res["remote_max_frame_speed"] = round(worst_step, 2)
        res["remote_vs_owner_err_m"] = {"p50": round(errs[len(errs) // 2], 3), "p95": round(errs[int(len(errs) * 0.95)], 3),
                                        "max": round(errs[-1], 3)}
    return res


def cargo_track(rows, item):
    return [r for r in rows if r["ev"] == "cargo" and r["item"] == item]


def check_at07(logs):
    host, c1 = logs["host"], logs["c1"]
    tags = {r["tag"]: r["t"] for r in ev(c1, "bot_log")}
    track = cargo_track(host, "Cargo_Case4_Net")
    return {"grabs_on_server": [(r["client"], r["local"]) for r in ev(host, "cargo_grab")],
            "pose_samples": [(round(r["t"], 1), r["state"], [round(x, 2) for x in r["p"]], [round(x) for x in r["r"]]) for r in track],
            "client_tags": tags}


def check_at08(logs):
    host = logs["host"]
    track = cargo_track(host, "Cargo_Case5_Net")
    grabs = [(r["client"], r["carriers"]) for r in ev(host, "cargo_grab")]
    rel = [(r["client"], r["carriers"]) for r in ev(host, "cargo_release")]
    # teleport check on the server trace: largest position step between consecutive traced samples
    cp = sorted([r for r in host if r["ev"] == "cpos" and r["item"] == "Cargo_Case5_Net"], key=lambda r: r["st"])
    worst = max((math.dist(a["p"], b["p"]) for a, b in zip(cp, cp[1:])), default=0.0)
    return {"grabs": grabs, "releases": rel, "max_height_y": max((r["p"][1] for r in track), default=None),
            "height_samples": [(round(r["t"], 1), r["carriers"], round(r["p"][1], 3)) for r in track],
            "largest_trace_step_m": round(worst, 3), "PASS_no_teleport": worst < 0.5}


def check_at10(logs):
    host = logs["host"]
    first, last = {}, {}
    for r in host:
        if r["ev"] == "cargo" and r["item"] in ("Cargo_Case1_Net", "Cargo_Case2_Net", "Cargo_Case3_Net"):
            first.setdefault(r["item"], r)
            last[r["item"]] = r
    veh = ev(host, "vehicle")
    return {"items": {k: {"start": first[k]["p"], "end": last[k]["p"], "end_state": last[k]["state"]} for k in last},
            "door_states": [(r["state"], round(r["pos"], 2)) for r in ev(host, "door_state")],
            "vehicle_last": veh[-1] if veh else None}


def check_at11(logs):
    host = logs["host"]
    sec = ev(host, "cargo_secure")
    uns = ev(host, "cargo_unsecure")
    plat = ev(host, "platform")
    return {"secured": [(r["item"], r["socket"]) for r in sec], "unsecured_velocity": [r["v"] for r in uns],
            "PASS_kept_velocity": any(math.hypot(r["v"][0], r["v"][2]) > 0.5 for r in uns),
            "platform_events": [(r["client"], r["on"]) for r in plat]}


def check_at18(logs):
    host = logs["host"]
    sec = ev(host, "cargo_secure")
    uses = [(r["client"], r["ok"], r["reason"]) for r in ev(host, "use")]
    grabs = [(r["client"], r["carriers"]) for r in ev(host, "cargo_grab")]
    return {"secures": len(sec), "PASS_one_secure": len(sec) == 1 or len({r["t"] // 5 for r in sec}) == len(sec),
            "grabs": grabs, "PASS_co_grab": any(c == 2 for _, c in grabs)}


def check_at19(logs, base):
    res = CHECKS[base](logs)
    res["rtt_ms"] = {n: [r["ms"] for r in rows if r["ev"] == "rtt"][-5:] for n, rows in logs.items()}
    # server vs client pose of traced cargo by server time
    srv = {}
    for r in logs["host"]:
        if r["ev"] == "cpos":
            srv.setdefault(r["item"], []).append(r)
    errs = []
    for name, rows in logs.items():
        if name == "host":
            continue
        for r in rows:
            if r["ev"] != "cpos" or r.get("srv"):
                continue
            s = srv.get(r["item"])
            if not s:
                continue
            nearest = min(s, key=lambda x: abs(x["st"] - r["st"]))
            errs.append(math.dist(nearest["p"], r["p"]))
    errs.sort()
    if errs:
        res["client_vs_server_pos_err_m"] = {"p50": round(errs[len(errs) // 2], 3), "p95": round(errs[int(len(errs) * 0.95)], 3), "max": round(errs[-1], 3)}
    return res


CHECKS = {"at02": check_at02, "at03": check_at03, "athl1": check_athl1, "athl2": check_athl2,
          "at07": check_at07, "at08": check_at08, "at10open": check_at10, "at10closed": check_at10, "at11": check_at11,
          "at18": check_at18}


def analyze(out, scenario=None):
    logs = {os.path.splitext(n)[0]: load(os.path.join(out, n)) for n in sorted(os.listdir(out)) if n.endswith(".jsonl")}
    if scenario is None:
        scenario = os.path.basename(out).rsplit("-", 2)[0]
    processes, agreement = common(logs)
    report = {"dir": out, "scenario": scenario, "processes": processes, "agreement": agreement}
    if scenario.startswith("at19_"):
        base = scenario.split("_")[1]
        try:
            report["checks"] = check_at19(logs, base)
        except Exception as e:
            report["checks_error"] = repr(e)
    elif scenario in CHECKS:
        try:
            report["checks"] = CHECKS[scenario](logs)
        except Exception as e:  # a failed analysis must not hide the logs
            report["checks_error"] = repr(e)
    with open(os.path.join(out, "report.json"), "w", encoding="utf-8") as f:
        json.dump(report, f, indent=1, ensure_ascii=False)
    sys.stdout.reconfigure(encoding="utf-8")
    print(json.dumps({k: report[k] for k in report if k != "processes"}, indent=1, ensure_ascii=False)[:8000])
    return report


if __name__ == "__main__":
    if sys.argv[1] == "list":
        for k, v in SCENARIOS.items():
            print(f"{k}: {v['desc']}")
    elif sys.argv[1] == "run":
        run(sys.argv[2])
    elif sys.argv[1] == "analyze":
        analyze(sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else None)
