"""Snow QA: Play sessions in the Snow editor with MPPM clients that join by themselves.

    python play.py start [--clients 2 3] [--log DIR] [--port N]   host in the editor (Game View 1920x1080), clients auto-join
    python play.py join 3 [--log DIR]                             a late client (Play already running)
    python play.py stop                                           exit Play, clones off, args files removed
    python play.py hour 12 [--rate 0.001]                         Forest clock (server); rate in game hours per real minute

Clients are joined by the agent (user decision 21.10: never ask the user to press Join). Before compiling or writing code
into Assets: nobody else may be in Play (ue.status()['playMode']).
"""
import argparse, os, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ue

ARGS = 'Temp/SnowBenchArgs.txt'

MPPM = '''System.Type mp = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { foreach (var t in a.GetTypes()) if (t.Name == "MultiplayerPlaymode") { mp = t; break; } if (mp != null) break; }
var players = mp.GetProperty("Players", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).GetValue(null) as System.Collections.IEnumerable;
string r = "";
foreach (var p in players) { var pt = p.GetType(); string name = (string)pt.GetProperty("Name").GetValue(p);
  r += name + "=" + pt.GetProperty("PlayerState")?.GetValue(p) + " ";
  if (name != "PLAYER") continue;
  foreach (var m in pt.GetMethods()) if (m.Name == "METHOD" && (m.Name == "Deactivate" || m.GetParameters().Length == 2)) { var args = new object[m.GetParameters().Length]; m.Invoke(p, args); r += "[" + m.Name + "] "; break; } }
return r;'''

GAMEVIEW_1080 = '''var asm = typeof(UnityEditor.EditorWindow).Assembly; var gvType = asm.GetType("UnityEditor.GameView"); var sizesType = asm.GetType("UnityEditor.GameViewSizes");
var inst = typeof(UnityEditor.ScriptableSingleton<>).MakeGenericType(sizesType).GetProperty("instance").GetValue(null);
var group = sizesType.GetMethod("GetGroup").Invoke(inst, new object[] { (int)UnityEditor.GameViewSizeGroupType.Standalone }); var gtype = group.GetType();
int count = (int)gtype.GetMethod("GetTotalCount").Invoke(group, null), idx = -1;
for (int i = 0; i < count; i++) { var s = gtype.GetMethod("GetGameViewSize").Invoke(group, new object[] { i }); var st = s.GetType();
  if ((int)st.GetProperty("width").GetValue(s) == 1920 && (int)st.GetProperty("height").GetValue(s) == 1080 && st.GetProperty("sizeType").GetValue(s).ToString() == "FixedResolution") { idx = i; break; } }
if (idx < 0) { var ns = System.Activator.CreateInstance(asm.GetType("UnityEditor.GameViewSize"), System.Enum.Parse(asm.GetType("UnityEditor.GameViewSizeType"), "FixedResolution"), 1920, 1080, "Snow 1080p");
  gtype.GetMethod("AddCustomSize").Invoke(group, new object[] { ns }); idx = count; }
var gv = UnityEditor.EditorWindow.GetWindow(gvType);
gvType.GetMethod("SizeSelectionCallback", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).Invoke(gv, new object[] { idx, null });
return "game view 1920x1080";'''

READY = 'var nm = Unity.Netcode.NetworkManager.Singleton; return (nm != null && nm.IsServer) + " " + (Snow.Player.PlayerNet.Local != null) + " clients " + (nm != null && nm.IsServer ? nm.ConnectedClientsIds.Count : 0);'


def mppm(player, method):
    return ue.run(MPPM.replace('PLAYER', f'Player {player}').replace('METHOD', method), 300)


def write_args(proj, line):
    with open(os.path.join(proj, ARGS), 'w', encoding='utf-8') as f:
        f.write(line)


def _client_line(port, log):
    return '-snow-client 127.0.0.1' + (f' -snow-port {port}' if port else '') + (f' -snow-log {log}' if log else '')


def launch(player, port=None, log_dir=None):
    write_args(ue.clone(player), _client_line(port, f'{log_dir}/benchlog_client{player}.jsonl' if log_dir else None))
    mppm(player, 'Activate')
    for _ in range(60):
        if f'Player {player}=Launched' in mppm(player, '-'):
            return True
        time.sleep(5)
    return False


def start(clients=(2,), port=None, log_dir=None):
    if ue.status().get('playMode') not in (None, 'stopped'):
        return 'Play is already running (someone else?) - not touching it'
    write_args(ue.PROJ, '-snow-host' + (f' -snow-port {port}' if port else '') + (f' -snow-log {log_dir}/benchlog_host.jsonl' if log_dir else ''))
    for c in clients:
        print(f'Player {c} launched:', launch(c, port, log_dir))
    time.sleep(15)                                   # a clone still finishing its own start drops its connection
    print(ue.cli('editor_play'))
    for _ in range(60):
        r = ue.run(READY, 30)
        if r.startswith('True True'):
            break
        time.sleep(3)
    want = 1 + len(clients)
    for _ in range(40):
        r = ue.run(READY, 30)
        if r.endswith(f'clients {want}'):
            break
        time.sleep(4)
    print(ue.run(GAMEVIEW_1080))
    return r


def join(player, port=None, log_dir=None):
    launch(player, port, log_dir)
    for _ in range(40):
        r = ue.run(READY, 30)
        n = int(r.rsplit(' ', 1)[-1]) if r.rsplit(' ', 1)[-1].isdigit() else 0
        if n >= 2 and f'Player {player}=Launched' in mppm(player, '-'):
            time.sleep(10)
            return ue.run(READY, 30)
        time.sleep(4)
    return r


def stop():
    print(ue.cli('editor_stop'))
    for c in (2, 3, 4):
        try:
            mppm(c, 'Deactivate')
            p = os.path.join(ue.clone(c), ARGS)
            if os.path.exists(p):
                os.remove(p)
        except KeyError:
            pass
    p = os.path.join(ue.PROJ, ARGS)
    if os.path.exists(p):
        os.remove(p)
    return mppm(0, '-')


def hour(h, rate=0.001):
    return ue.run(f'var dn = Snow.Levels.Forest.ForestDayNight.Instance; dn.ServerSetRate({rate}f); dn.ServerSetHour({h}f); return "hour " + dn.Hour.ToString("0.00");')


if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('cmd', choices=['start', 'join', 'stop', 'hour'])
    ap.add_argument('value', nargs='?')
    ap.add_argument('--clients', nargs='*', type=int, default=[2])
    ap.add_argument('--port', type=int)
    ap.add_argument('--log')
    ap.add_argument('--rate', type=float, default=0.001)
    a = ap.parse_args()
    if a.cmd == 'start':
        print(start(a.clients, a.port, a.log))
    elif a.cmd == 'join':
        print(join(int(a.value), a.port, a.log))
    elif a.cmd == 'stop':
        print(stop())
    else:
        print(hour(float(a.value), a.rate))
