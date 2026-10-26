"""Shelf packing of UV islands by their bounding boxes (the main building's walls, strips and boxes are rectangles:
Blender's pack_islands leaves ~60 % of such an atlas empty, the shelves ~25 %). Islands = connected UV regions of a
group of objects; each is turned to lie long side horizontal, the boxes are sorted by height and laid in rows; the
largest scale that fits is found by bisection; the margin is in atlas pixels.
repack(objs, size, margin_px=2.2, uv_name='PSX') -> fill (share of the atlas covered)"""
import numpy as np
from scipy.sparse import coo_matrix
from scipy.sparse.csgraph import connected_components


def _islands(me, uv):
    nl = len(me.loops)
    lv = np.empty(nl, np.int32)
    me.loops.foreach_get('vertex_index', lv)
    ps = np.empty(len(me.polygons), np.int32)
    pt = np.empty(len(me.polygons), np.int32)
    me.polygons.foreach_get('loop_start', ps)
    me.polygons.foreach_get('loop_total', pt)
    rows, cols = [], []
    for s, t in zip(ps, pt):                                    # the loops of a face belong together
        idx = np.arange(s, s + t)
        rows.append(idx[:-1])
        cols.append(idx[1:])
    key = np.round(uv * 2 ** 16).astype(np.int64)                 # the same vertex at the same UV: one island
    k = lv.astype(np.int64) * (2 ** 40) + key[:, 0] * (2 ** 20) + key[:, 1]
    order = np.argsort(k, kind='stable')
    ks = k[order]
    same = np.nonzero(ks[1:] == ks[:-1])[0]
    rows.append(order[same])
    cols.append(order[same + 1])
    r = np.concatenate(rows)
    c = np.concatenate(cols)
    g = coo_matrix((np.ones(len(r), np.int8), (r, c)), shape=(nl, nl))
    n, lab = connected_components(g, directed=False)
    return n, lab


def _shelf(boxes, s, m):
    """place boxes (w, h) scaled by s with margin m into [0, 1]^2 in rows; -> positions or None"""
    pos = []
    x = y = 0.0
    row_h = 0.0
    for w, h in boxes:
        W, H = w * s + m, h * s + m
        if W > 1.0 or H > 1.0:
            return None
        if x + W > 1.0:
            y += row_h
            x, row_h = 0.0, 0.0
        if y + H > 1.0:
            return None
        pos.append((x + m / 2, y + m / 2))
        x += W
        row_h = max(row_h, H)
    return pos


def repack(objs, size, margin_px=2.2, uv_name='PSX'):
    m = margin_px / size
    data = []
    for o in objs:
        me = o.data
        nl = len(me.loops)
        uv = np.empty(2 * nl, np.float32)
        me.uv_layers[uv_name].data.foreach_get('uv', uv)
        uv = uv.reshape(-1, 2).astype(np.float64)
        n, lab = _islands(me, uv)
        data.append((o, uv, n, lab))
    isl = []                                                    # (object index, label, lo, hi, rotate)
    for oi, (o, uv, n, lab) in enumerate(data):
        lo = np.full((n, 2), np.inf)
        hi = np.full((n, 2), -np.inf)
        np.minimum.at(lo, lab, uv)
        np.maximum.at(hi, lab, uv)
        for i in range(n):
            w, h = hi[i] - lo[i]
            isl.append((oi, i, lo[i], hi[i], h > w))
    boxes = []
    for oi, i, lo, hi, rot in isl:
        w, h = hi - lo
        boxes.append((h, w) if rot else (w, h))
    order = sorted(range(len(boxes)), key=lambda j: (-boxes[j][1], -boxes[j][0]))
    sb = [boxes[j] for j in order]
    a, b = 0.0, 1.0
    while _shelf(sb, b, m) is not None:                          # bracket the scale
        a, b = b, b * 2
    for _ in range(40):
        c = (a + b) / 2
        if _shelf(sb, c, m) is not None:
            a = c
        else:
            b = c
    s = a
    pos = _shelf(sb, s, m)
    place = {order[j]: pos[j] for j in range(len(order))}
    new = [uv.copy() for (_, uv, _, _) in data]
    groups = []                                                 # the loops of each island, found once (not a scan per island)
    for (o, uv, n, lab) in data:
        srt = np.argsort(lab, kind='stable')
        groups.append((srt, np.searchsorted(lab[srt], np.arange(n + 1))))
    for j, (oi, i, lo, hi, rot) in enumerate(isl):
        o, uv, n, lab = data[oi]
        srt, bnd = groups[oi]
        msk = srt[bnd[i]:bnd[i + 1]]
        q = uv[msk] - lo
        if rot:                                                 # a quarter turn: (u, v) -> (v, w - u)
            q = np.stack([q[:, 1], (hi - lo)[0] - q[:, 0]], 1)
        new[oi][msk] = q * s + np.array(place[j])
    area = 0.0
    for (o, uv, n, lab), nu in zip(data, new):
        o.data.uv_layers[uv_name].data.foreach_set('uv', nu.astype(np.float32).reshape(-1))
    for o in objs:
        me = o.data
        uvl = me.uv_layers[uv_name].data
        for p in me.polygons:
            pts = [uvl[i].uv for i in p.loop_indices]
            a2 = 0.0
            for i in range(len(pts)):
                a2 += pts[i].x * pts[(i + 1) % len(pts)].y - pts[(i + 1) % len(pts)].x * pts[i].y
            area += abs(a2) / 2
    return area
