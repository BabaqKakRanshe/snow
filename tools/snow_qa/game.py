"""Snow QA: act in Play through the same server rules a player's E / grab reach (locks, fuel, power are not bypassed).

    import game
    game.go((x, y, z), (tx, ty, tz))             # teleport the local player (eye point, look target; world y)
    game.use('IX_GenStart_86'); game.describe('IX_GenStart_86'); game.angle('IX_Gate_81')
    game.free_hands(); game.grab('Key_Generator'); game.pocket('Key_Generator')
    game.carry_to('Jerrycan_Yard', point, 24, press='IX_GenFillCap_300')   # hold an item at a point, optionally hold Use

Pitfalls handled: CargoItem.HeldBy(client) returns the FIRST item with a hand entry -> grab() frees the hands first; a hand
target set from the CLI goes stale after targetTimeout (0.5 s) -> carry_to() moves the body with the target every call and
presses Use in the same call; teleporting leaves held items behind -> bring(); ForestItems.Find works on the server only.
"""
import os, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ue

PRE = ('var host = UnityEngine.Object.FindFirstObjectByType<Snow.Levels.SkyIslandLocationHost>(); '
       'var items = UnityEngine.Object.FindFirstObjectByType<Snow.Levels.Forest.ForestItems>(); var CI = System.Globalization.CultureInfo.InvariantCulture; ')
TH = ('System.Func<float, float, float> th = (x, z) => { foreach (var te in UnityEngine.Terrain.activeTerrains) { var o = te.GetPosition(); var s = te.terrainData.size; '
      'if (x >= o.x && x < o.x + s.x && z >= o.z && z < o.z + s.z) return te.SampleHeight(new UnityEngine.Vector3(x, 0, z)) + o.y; } return 0f; }; ')


def f(v):
    return f'{float(v):.3f}f'


def V(p):
    return f'new UnityEngine.Vector3({f(p[0])}, {f(p[1])}, {f(p[2])})'


def _xyz(r):
    try:
        return tuple(float(v) for v in r.split())
    except Exception:
        return None


def go(eye, target, proj=ue.PROJ, terrain_rel=False):
    """Teleport the local player so its eye is near `eye` and it looks at `target`. terrain_rel: y are metres above Terrain."""
    rel = 'true' if terrain_rel else 'false'
    return ue.run(TH + f'''var p = {V(eye)}; var t = {V(target)}; if ({rel}) {{ p.y += th(p.x, p.z); t.y += th(t.x, t.z); }}
var pl = Snow.Player.PlayerNet.Local; if (pl == null) return "no player";
var feet = p - UnityEngine.Vector3.up * 1.6f; UnityEngine.RaycastHit h; int mask = ~(1 << UnityEngine.LayerMask.NameToLayer("Interact"));
if (UnityEngine.Physics.Raycast(p + UnityEngine.Vector3.up * 0.3f, UnityEngine.Vector3.down, out h, 60f, mask, UnityEngine.QueryTriggerInteraction.Ignore)) feet = h.point;
var d = t - p; float yaw = UnityEngine.Mathf.Atan2(d.x, d.z) * UnityEngine.Mathf.Rad2Deg;
pl.view.SetYaw(yaw); pl.motor.Teleport(feet, yaw, UnityEngine.Vector3.zero); pl.view.LookAt(t);
return "feet " + feet.ToString("F2");''', proj=proj)


def look(target, proj=ue.PROJ, terrain_rel=False):
    rel = 'true' if terrain_rel else 'false'
    return ue.run(TH + f'var t = {V(target)}; if ({rel}) t.y += th(t.x, t.z); Snow.Player.PlayerNet.Local.view.LookAt(t); return "look";', proj=proj)


def near(p, dx=1.2, dz=0.0, dy=0.3):
    """Stand dx/dz metres off a world point and look at it."""
    return go((p[0] + dx, p[1] + dy, p[2] + dz), p)


def use(part, phase='Press'):
    return ue.run(PRE + f'bool ok = host.ServerUse(0, "{part}", Snow.Interaction.UsePhase.{phase}, out string why); '
                        f'return "{part} {phase}: " + ok + " " + why + " -> " + host.Describe("{part}");')


def describe(part):
    return ue.run(PRE + f'return "{part}: " + host.Describe("{part}");')


def part_pos(part):
    return _xyz(ue.run(PRE + f'foreach (var t in host.transform.root.GetComponentsInChildren<UnityEngine.Transform>(true)) if (t.name == "{part}") '
                             'return t.position.x.ToString(CI) + " " + t.position.y.ToString(CI) + " " + t.position.z.ToString(CI); return "";'))


def angle(part):
    return ue.run(PRE + f'foreach (var t in host.transform.root.GetComponentsInChildren<UnityEngine.Transform>(true)) if (t.name == "{part}") '
                        f'return "{part} local rot " + t.localEulerAngles.ToString("F1"); return "{part} ?";')


def item_pos(item):
    return _xyz(ue.run(PRE + f'var no = items.Find("{item}"); if (no == null) return ""; var p = no.transform.position; '
                             'return p.x.ToString(CI) + " " + p.y.ToString(CI) + " " + p.z.ToString(CI);'))


def free_hands():
    """Drop whatever the host holds in the hand (pockets stay)."""
    return ue.run(PRE + 'string r = ""; foreach (var c in Snow.Cargo.CargoItem.All) { if (!c.IsSpawned) continue; '
                        'if (c.GrabbedBy(0)) { c.ServerRelease(0); r += c.name + " released; "; } '
                        'if (c.CarriedBy(0) && c.Carry == Snow.Cargo.CarryMode.Hand) { c.ServerDrop(0, UnityEngine.Vector3.zero); r += c.name + " dropped; "; } } return "hands free: " + r;')


def grab(item):
    free_hands()
    return ue.run(PRE + f'var no = items.Find("{item}"); if (no == null) return "no {item}"; var c = no.GetComponent<Snow.Cargo.CargoItem>(); '
                        f'bool ok = c.ServerGrab(0, UnityEngine.Vector3.zero, out string why); return "grab {item}: " + ok + " " + why + " held " + (Snow.Cargo.CargoItem.HeldBy(0) == c);')


def pocket(item):
    return ue.run(PRE + f'var c = items.Find("{item}").GetComponent<Snow.Cargo.CargoItem>(); bool ok = c.ServerToPocket(0, out string why); return "pocket {item}: " + ok + " " + why;')


def release(item):
    return ue.run(PRE + f'var c = items.Find("{item}").GetComponent<Snow.Cargo.CargoItem>(); c.ServerRelease(0); return "release {item}";')


def _hold(item, p):
    return (f'var c = items.Find("{item}").GetComponent<Snow.Cargo.CargoItem>(); var p = {V(p)}; c.ServerTarget(0, p, c.transform.rotation); '
            'var rb = c.GetComponent<UnityEngine.Rigidbody>(); rb.position = p; rb.linearVelocity = UnityEngine.Vector3.zero; c.transform.position = p; ')


def carry_to(item, p, seconds, press=None):
    """Hold a grabbed item at p for `seconds`; with press: Use Press on that part in the first call, Release at the end."""
    first = PRE + _hold(item, p)
    if press:
        first += f'bool ok = host.ServerUse(0, "{press}", Snow.Interaction.UsePhase.Press, out string why); return "press " + ok + " " + why;'
    else:
        first += 'return "held at " + c.transform.position.ToString("F2");'
    out = [ue.run(first)]
    t0 = time.time()
    while time.time() - t0 < seconds:
        ue.run(PRE + _hold(item, p) + 'return "";')
        time.sleep(0.15)
    if press:
        out.append(use(press, 'Release'))
    return out


def bring(item):
    """A held item to the hand after a teleport, its light / tip along the view."""
    return ue.run(PRE + f'var no = items.Find("{item}"); var cam = UnityEngine.Camera.main.transform; var rb = no.GetComponent<UnityEngine.Rigidbody>(); '
                        'var lt = no.GetComponentInChildren<UnityEngine.Light>(true); var q = cam.rotation; '
                        'if (lt != null) q = cam.rotation * UnityEngine.Quaternion.Inverse(UnityEngine.Quaternion.Inverse(no.transform.rotation) * lt.transform.rotation); '
                        'var p = cam.position + cam.forward * 0.6f - cam.up * 0.25f + cam.right * 0.2f; if (rb != null) { rb.position = p; rb.rotation = q; } '
                        'no.transform.SetPositionAndRotation(p, q); return "brought " + p.ToString("F2");')


def screenshot(path, proj=ue.PROJ):
    """The real 1920x1080 frame with the PS1 present pass (Game View size set by play.start)."""
    path = path.replace('\\', '/')
    if os.path.exists(path):
        os.remove(path)
    ue.run(f'UnityEngine.ScreenCapture.CaptureScreenshot(@"{path}"); return "shot";', proj=proj)
    for _ in range(20):
        if os.path.exists(path):
            return True
        time.sleep(0.5)
    return False


if __name__ == '__main__':
    fn, args = sys.argv[1], sys.argv[2:]
    print(globals()[fn](*args))
