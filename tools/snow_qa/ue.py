"""Snow QA: run C# in the Snow editor (or an MPPM clone) through the pipeline CLI.

    import ue
    ue.run('return "hi";')                      # statements ending in return; result string
    ue.run(code, proj=ue.clone(2))              # in Player 2's clone (MPPM)
    ue.run_long(code, timeout=900)              # long work: the result comes back through a file

Pitfalls handled here: Python's stdout is cp1251 in this shell (Cyrillic came out garbled) -> utf-8; the CLI gives up waiting
after ~5 s on long main-thread work ("Main thread operation timed out after 5000ms") while the code keeps running -> run_long;
clone pipelines answer slowly -> retries.
"""
import json, os, subprocess, sys, tempfile, time, uuid

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8')
    except Exception:
        pass

PROJ = r"F:\ClaudeGames\snow\Snow"
TMP = os.path.join(tempfile.gettempdir(), 'snow_qa')
os.makedirs(TMP, exist_ok=True)


def clone(player):
    """Project path of an MPPM virtual player ('Player 2' -> Library/VP/mppm<id>)."""
    data = json.load(open(os.path.join(PROJ, 'Library', 'VP', 'SystemData.json'), encoding='utf-8'))['Data']
    for v in data.values():
        if v['Name'] == f'Player {player}':
            vp = v['TypeDependentPlayerInfo']['VirtualProjectIdentifier']
            return os.path.join(PROJ, 'Library', 'VP', vp['m_Prefix'] + vp['m_Id'])
    raise KeyError(f'no MPPM Player {player}')


def cli(*args, proj=PROJ, timeout=120):
    r = subprocess.run(['unity', 'command', '--project-path', proj, *args], capture_output=True, timeout=timeout + 30, shell=True)
    return (r.stdout + r.stderr).decode('utf-8', errors='replace')


def _result(out):
    try:
        return json.loads(out[out.index('{'):])['result']
    except Exception:
        return out.strip()


def run_file(path, timeout=120, proj=PROJ, retries=3):
    out = ''
    for _ in range(retries):
        out = cli('--timeout', str(timeout), 'eval_file', '--file', path, '--timeout', str(timeout), '--result-only', proj=proj, timeout=timeout)
        if 'No Pipeline' not in out and 'timed out' not in out:
            break
        time.sleep(2)
    return _result(out)


def run(code, timeout=120, proj=PROJ):
    path = os.path.join(TMP, f'eval_{uuid.uuid4().hex[:8]}.cs')
    with open(path, 'w', encoding='utf-8') as f:
        f.write(code)
    try:
        return run_file(path, timeout, proj, retries=1 if timeout > 120 else 3)
    finally:
        os.remove(path)


def run_long(code, timeout=900, proj=PROJ):
    """Code whose result arrives after the CLI stopped waiting: wraps it so the editor writes the result to a file."""
    res = os.path.join(TMP, f'res_{uuid.uuid4().hex[:8]}.txt').replace('\\', '/')
    wrapped = ('System.Func<string> __body = () => {\n' + code + '\n};\nstring __r = __body();\n'
               f'System.IO.File.WriteAllText(@"{res}", __r ?? "");\nreturn __r;')
    out = run(wrapped, timeout, proj)
    t0 = time.time()
    while not os.path.exists(res) and time.time() - t0 < timeout:
        time.sleep(2)
    time.sleep(0.5)
    if os.path.exists(res):
        text = open(res, encoding='utf-8').read()
        os.remove(res)
        return text
    return out


def status(proj=PROJ):
    try:
        out = cli('editor_status', '--result-only', proj=proj, timeout=30)
        return json.loads(out[out.index('{'):])
    except Exception:
        return {}


def wait_ready(proj=PROJ, timeout=300):
    """Editor idle: not compiling, no domain reload."""
    t0 = time.time()
    while time.time() - t0 < timeout:
        s = status(proj)
        if s.get('status') == 'ready' and not s.get('compiling') and not s.get('domainReloadInProgress'):
            return s
        time.sleep(2)
    return status(proj)


def recompile(proj=PROJ):
    """Refresh + recompile; returns new 'error CS' lines of Editor.log (empty = clean)."""
    log = os.path.join(os.environ['LOCALAPPDATA'], 'Unity', 'Editor', 'Editor.log')
    off = os.path.getsize(log)
    run('UnityEditor.AssetDatabase.Refresh(); return "ok";', 300, proj)
    cli('--timeout', '600', 'recompile', proj=proj, timeout=600)
    wait_ready(proj)
    with open(log, 'rb') as f:
        f.seek(off)
        return [l for l in f.read().decode('utf-8', errors='replace').splitlines() if 'error CS' in l]


if __name__ == '__main__':
    print(run(open(sys.argv[1], encoding='utf-8').read(), int(sys.argv[2]) if len(sys.argv) > 2 else 120))
