"""Snow QA: shots and frame time at named points in Play.

    python shots.py <points.json> <out_dir> <tag> [name|group ...] [--client N] [--no-shot]
    python shots.py frames <before.jsonl> <after.jsonl>       CPU/GPU/draw calls before -> after per point

points.json: {"name": {"eye": [x, y, z], "look": [x, y, z], "rel": true, "groups": ["perf", "u7"]}, ...}
  rel: y are metres above Terrain (outdoors); else world y (indoors). Writes <out>/<tag>_<name>.png and <out>/<tag>_frames.jsonl
  (3 frame-time samples per point). --client N: the point is taken by Player N (frame time of that clone; no shot).
"""
import json, os, statistics, subprocess, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ue, game


def perf(proj=ue.PROJ):
    out = ue.cli('get_performance_stats', '--result-only', proj=proj, timeout=60)
    try:
        d = json.loads(out[out.index('{'):])
        return round(d['frameTiming']['cpuFrameTimeMs'], 2), round(d['frameTiming']['gpuFrameTimeMs'], 2), d['render']['drawCalls']
    except Exception:
        return None


def take(points, out, tag, names, client=None, shot=True):
    os.makedirs(out, exist_ok=True)
    proj = ue.clone(client) if client else ue.PROJ
    log = open(os.path.join(out, f'{tag}_frames.jsonl'), 'a', encoding='utf-8')
    for n in names:
        p = points[n]
        r = game.go(p['eye'], p['look'], proj, p.get('rel', False))
        time.sleep(p.get('wait', 3.0))
        game.look(p['look'], proj, p.get('rel', False))
        time.sleep(1.0)
        rec = {'tag': tag, 'name': n, 'client': client, 'teleport': r, 'perf': [perf(proj) for _ in range(3)]}
        if shot and not client:
            rec['shot'] = game.screenshot(os.path.join(out, f'{tag}_{n}.png'))
        log.write(json.dumps(rec, ensure_ascii=False) + '\n')
        log.flush()
        print(n, r, rec['perf'], rec.get('shot', ''))


def frames(before, after):
    def load(path):
        d = {}
        for l in open(path, encoding='utf-8'):
            r = json.loads(l)
            s = [x for x in r['perf'] if x]
            if s:
                d[(r['name'], bool(r['client']))] = (statistics.median(x[0] for x in s), statistics.median(x[1] for x in s), s[-1][2])
        return d
    b, a = load(before), load(after)
    print('point              who     CPU ms        GPU ms       draw calls')
    for k in sorted(set(b) & set(a)):
        x, y = b[k], a[k]
        print(f"{k[0]:18s} {'client' if k[1] else 'host':6s} {x[0]:5.1f} -> {y[0]:5.1f}  {x[1]:4.1f} -> {y[1]:4.1f}  {x[2]} -> {y[2]}")


if __name__ == '__main__':
    if sys.argv[1] == 'frames':
        frames(sys.argv[2], sys.argv[3])
        sys.exit()
    args = sys.argv[1:]
    client = None
    if '--client' in args:
        i = args.index('--client'); client = int(args[i + 1]); del args[i:i + 2]
    shot = '--no-shot' not in args
    args = [a for a in args if a != '--no-shot']
    points = json.load(open(args[0], encoding='utf-8'))
    out, tag, sel = args[1], args[2], args[3:] or ['all']
    names = [n for n in points if 'all' in sel or n in sel or set(sel) & set(points[n].get('groups', []))]
    take(points, out, tag, names, client, shot)
