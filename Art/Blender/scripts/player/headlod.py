"""클로즈업 머리 (specs/056 FR-61). 세리머니·컷신 클로즈업에서만 몸 LOD0 머리(HideHead로 접음) 대신 그린다. ≤ 6k tris, 동시 최대 2명.

기준 표면 머리·목 윗부분(머리 뼈 가중치 ≥ 0.15)을 감량(목표 HEAD_TRIS) + LOD0 안구. 스킨 UV(MakeHuman)는 몸과 같아서
클로즈업 전용 2048² 피부 텍스처(textures.skin_closeup)를 같은 UV로 쓴다. 머리카락은 선수 스타일의 Hair_*_LOD0을 Unity가 합친다.
"""
import numpy as np

from . import body as B
from . import mpfb_src as MS

HEAD_TRIS = 5200
NAME = "Head_Closeup"


def build(R):
    hi = MS.OUR_NAMES.index("Head")
    ni = MS.OUR_NAMES.index("Neck")
    w = R.weights[:, hi] + 0.5 * R.weights[:, ni]
    bodyset = set(R.groups["body"])
    keep = np.zeros(R.n, dtype=bool)
    for v in R.groups["body"]:
        keep[v] = w[v] >= 0.15
    sel = [(f, u) for f, u in zip(R.faces, R.face_uvs) if all(keep[v] and v in bodyset for v in f)]
    faces = [f for f, _ in sel]
    uvs = [u for _, u in sel]
    ids = sorted({v for f in faces for v in f})
    remap = {v: i for i, v in enumerate(ids)}
    co, fl, fu = B._decimate_faces(R.verts[ids], [tuple(remap[v] for v in f) for f in faces], HEAD_TRIS, uvs)
    tree = B._subset_tree(R, faces)
    bind = B.Binding.__new__(B.Binding)
    bind.tri = np.zeros((len(co), 3), dtype=np.int64)
    bind.bc = np.zeros((len(co), 3))
    for i, p in enumerate(co):
        (t, bc), _ = B._bind_point(R, tree, p)
        bind.tri[i], bind.bc[i] = t, bc
    m = B.Mesh()
    m.verts = bind.apply(R.verts)
    W = bind.apply(R.weights)
    m.W = W / W.sum(axis=1, keepdims=True)
    alld = B._all_deltas(R)
    m.deltas = {k: bind.apply(v) for k, v in sorted(alld.items())}
    m.deltas["HideHead"] = np.zeros_like(m.verts)  # 클로즈업 머리 자신은 접지 않는다
    m.vpart = ["head"] * len(m.verts)
    for f, u in zip(fl, fu):
        m.faces.append((tuple(f), B.BODY, tuple(u), "skin", None))
    B._append_eyes(R, m, 0)
    return m
