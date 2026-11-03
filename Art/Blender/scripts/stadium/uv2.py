"""라이트맵 UV (specs/044 FR-23, AC-08). 면마다 자기 평면에 투영한 사각형을 선반(shelf) 방식으로 겹침 없이 배치한다.

UV0도 같은 값으로 채운다(텍스처를 쓰지 않으므로 UV0는 자리만). 결정론: 면 순서·정렬 키 고정.
"""
import math

PAD_FRAC = 0.004   # 아틀라스 한 변 대비 여백


def _face_rect(mesh, poly):
    pts = [mesh.vertices[i].co for i in poly.vertices]
    n = poly.normal
    e = (pts[1] - pts[0])
    if e.length < 1e-9:
        e = pts[2] - pts[0]
    u = e.normalized()
    v = n.cross(u).normalized()
    proj = [((p - pts[0]).dot(u), (p - pts[0]).dot(v)) for p in pts]
    minx = min(q[0] for q in proj)
    miny = min(q[1] for q in proj)
    w = max(q[0] for q in proj) - minx
    h = max(q[1] for q in proj) - miny
    return [(q[0] - minx, q[1] - miny) for q in proj], max(w, 1e-4), max(h, 1e-4)


def assign(obj):
    me = obj.data
    rects = []
    for poly in me.polygons:
        local, w, h = _face_rect(me, poly)
        rects.append((poly.index, local, w, h))
    area = sum(w * h for _, _, w, h in rects)
    side = math.sqrt(area) * 1.15 + max(max(w for _, _, w, _ in rects), max(h for _, _, _, h in rects)) * 0.0
    side = max(side, max(max(w, h) for _, _, w, h in rects))
    order = sorted(rects, key=lambda r: (-round(r[3], 6), r[0]))
    for attempt in range(40):
        pad = side * PAD_FRAC
        placed = {}
        x = y = row_h = 0.0
        ok = True
        for idx, local, w, h in order:
            if x + w + pad > side:
                x = 0.0
                y += row_h + pad
                row_h = 0.0
            if w + pad > side or y + h + pad > side:
                ok = False
                break
            placed[idx] = (x + pad, y + pad, local)
            x += w + pad
            row_h = max(row_h, h)
        if ok:
            break
        side *= 1.1
    else:
        raise RuntimeError(f"{obj.name}: UV2 배치 실패")
    while len(me.uv_layers) < 2:
        me.uv_layers.new(name="UVMap" if len(me.uv_layers) == 0 else "Lightmap")
    uv0, uv1 = me.uv_layers[0], me.uv_layers[1]
    for poly in me.polygons:
        ox, oy, local = placed[poly.index]
        for k, li in enumerate(poly.loop_indices):
            u = (ox + local[k][0]) / side
            v = (oy + local[k][1]) / side
            uv1.data[li].uv = (u, v)
            uv0.data[li].uv = (u, v)
    return side


def overlap_ratio(obj, res=256):
    """UV2 삼각형을 픽셀 중심에서 래스터화해 두 면 이상이 덮는 픽셀 비율."""
    me = obj.data
    uv = me.uv_layers[1].data
    cover = bytearray(res * res)
    over = 0
    covered = 0
    me.calc_loop_triangles()
    owner = [-1] * (res * res)
    for tri in me.loop_triangles:
        a, b, c = (uv[li].uv for li in tri.loops)
        minx = max(0, int(math.floor(min(a.x, b.x, c.x) * res)))
        maxx = min(res - 1, int(math.ceil(max(a.x, b.x, c.x) * res)))
        miny = max(0, int(math.floor(min(a.y, b.y, c.y) * res)))
        maxy = min(res - 1, int(math.ceil(max(a.y, b.y, c.y) * res)))
        den = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y)
        if abs(den) < 1e-14:
            continue
        for py in range(miny, maxy + 1):
            fy = (py + 0.5) / res
            for px in range(minx, maxx + 1):
                fx = (px + 0.5) / res
                l1 = ((b.y - c.y) * (fx - c.x) + (c.x - b.x) * (fy - c.y)) / den
                l2 = ((c.y - a.y) * (fx - c.x) + (a.x - c.x) * (fy - c.y)) / den
                l3 = 1 - l1 - l2
                eps = 1e-6
                if l1 > eps and l2 > eps and l3 > eps:
                    k = py * res + px
                    if cover[k] and owner[k] != tri.polygon_index:
                        over += 1
                    elif not cover[k]:
                        covered += 1
                    cover[k] = 1
                    owner[k] = tri.polygon_index
    return 0.0 if covered == 0 else over / covered
