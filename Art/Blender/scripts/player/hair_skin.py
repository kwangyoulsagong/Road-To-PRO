"""헤어 메시 공통 도우미 (specs/088 T10 — 062 hair.py에서 그루밍 굽기가 쓰는 부분만 옮김: 피부 밀어내기·두피 묶음·컬러 포인트).
피부 밀어내기·묶음 규칙은 057 FR-13·062 FR-45와 같다(두피 간격, 머리 뼈 가중치 1, 두피 묶음점의 얼굴·머리·HideHead 델타)."""
import numpy as np

from . import mpfb_src as MS


def clear(co, faces, skin, nrm, gap=0.0012, iters=3):
    """감량된 셸 면이 휜 곳(관자·귀 위)에서 현만큼 안쪽으로 파고들어 피부 정점이 뚫고 나오지 않게, 피부 정점이 셸 아래 gap 이상
    들어가도록 가까운 셸 면의 정점을 바깥으로 민다."""
    from mathutils.bvhtree import BVHTree
    co = co.copy()
    tris = [tuple(f) for f in faces]
    for _ in range(iters):
        tree = BVHTree.FromPolygons([tuple(map(float, p)) for p in co], tris)
        push = np.zeros(len(co))
        for p, n in zip(skin, nrm):
            hit = tree.ray_cast(tuple(map(float, p - n * 0.05)), tuple(map(float, n)), 0.2)
            if hit[0] is None:
                continue
            depth = float(np.dot(np.array(hit[0]) - p, n))   # 피부 → 셸 (법선 방향, 양수 = 셸이 바깥)
            if depth < gap:
                for v in tris[hit[2]]:
                    push[v] = max(push[v], gap - depth)
        if not push.any():
            break
        vn = np.zeros_like(co)
        for f in tris:
            fn = np.cross(co[f[1]] - co[f[0]], co[f[2]] - co[f[0]]) if len(f) == 3 else np.cross(co[f[2]] - co[f[0]], co[f[3]] - co[f[1]])
            for v in f:
                vn[v] += fn
        vn /= np.maximum(np.linalg.norm(vn, axis=1, keepdims=True), 1e-12)
        out = co - skin.mean(axis=0)
        vn *= np.where(np.einsum("ij,ij->i", vn, out) < 0, -1.0, 1.0)[:, None]  # 감기와 상관없이 바깥
        co += vn * push[:, None] * 1.2
    return co


def append(m, P, faces_uv, part, R, tree, hd, alld, vpart=None):
    """새 정점 P (k,3)를 두피 최근접 + 오프셋으로 묶어 m에 붙인다(머리 모양 델타를 따라간다).
    faces_uv: [(정점 지역 인덱스 튜플, 코너 UV 튜플)], hd: (k,2) 밀도·컬러 포인트."""
    from . import body as B
    hi = MS.OUR_NAMES.index("Head")
    base = len(m.verts)
    tri = np.zeros((len(P), 3), dtype=np.int64)
    bc = np.zeros((len(P), 3))
    off = np.zeros((len(P), 3))
    for i, p in enumerate(P):
        (t3, w3), _ = B._bind_point(R, tree, p)
        tri[i], bc[i] = t3, w3
        off[i] = p - (R.verts[t3] * w3[:, None]).sum(axis=0)
    bind = B.Binding.__new__(B.Binding)
    bind.tri, bind.bc = tri, bc
    Pn = bind.apply(R.verts) + off
    W = np.zeros((len(P), len(MS.OUR_NAMES)))
    W[:, hi] = 1.0
    m.verts = np.vstack([m.verts, Pn])
    m.W = np.vstack([m.W, W])
    m.hairdata = np.vstack([m.hairdata, np.asarray(hd, dtype=float).reshape(-1, 2)])
    for k in m.deltas:
        m.deltas[k] = np.vstack([m.deltas[k], bind.apply(alld[k])])
    m.vpart += [vpart or part] * len(P)
    for f, uv in faces_uv:
        m.faces.append((tuple(base + i for i in f), B.BODY, tuple(uv), part, None))


def color_point(kind, P, R):
    """062 FR-54 두 색 마스크 (0 = 뿌리·옆 자연색, 1 = 위·끝 탈색·염색). kind: None · "top" · "strip" · "tips"."""
    from . import face as F
    if not kind:
        return np.zeros(len(P))
    c, d = F.eye_frame(R)
    up = (P[:, 1] - c[1]) / d
    side = np.abs(P[:, 0] - c[0]) / d

    def ss(x, a, b):
        t = np.clip((x - a) / (b - a), 0, 1)
        return t * t * (3 - 2 * t)
    if kind == "top":
        return ss(up, 1.0, 1.4)
    if kind == "strip":
        return ss(0.36 - side, 0.0, 0.04) * ss(up, 0.8, 1.1)
    if kind == "tips":
        return 1.0 - ss(up, -0.55, -0.45)
    return np.zeros(len(P))
