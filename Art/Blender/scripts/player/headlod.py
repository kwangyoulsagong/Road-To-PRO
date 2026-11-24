"""클로즈업 머리 (specs/056 FR-61). 세리머니·컷신 클로즈업에서만 몸 LOD0 머리(HideHead로 접음) 대신 그린다. ≤ 6k tris, 동시 최대 2명.

기준 표면 머리·목 윗부분(머리 뼈 가중치 ≥ 0.15)을 감량(목표 HEAD_TRIS) + LOD0 안구. 스킨 UV(MakeHuman)는 몸과 같아서
클로즈업 전용 2048² 피부 텍스처(textures.skin_closeup)를 같은 UV로 쓴다. 머리카락은 선수 스타일의 Hair_*_LOD0을 Unity가 합친다.
"""
import numpy as np

from . import body as B
from . import mpfb_src as MS

HEAD_TRIS = None   # 088 FR-52 (T27): 원본 MPFB 머리 (이전 5,200 감량)
NAME = "Head_Closeup"
CLOSEUP_SKIN_HD = (1.0, 0.65)   # UV1 (밀도, 눈썹 마스크 감쇠)
CLOSEUP_NECK_HD_Y = 0.4         # 088 T30h: 클로즈업 머리의 목 부분 UV1.y — 셰이더가 얼굴(> 0.5)만 그림자 맵을 건너뛴다. 목은 LOD0 목과 같은 그림자
                                # (겹친 두 목이 다르게 칠해져 얼룩이 났다). 눈썹 마스크는 목에 없어 영향 없음


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
    if HEAD_TRIS is None:
        co, fl, fu = R.verts[ids].copy(), [tuple(remap[v] for v in f) for f in faces], uvs
    else:
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
    B._append_eyes(R, m, 0, closeup=True)   # 088 FR-53 (T28): 클로즈업 눈 = 원본 고해상도 눈
    B._append_mouth(R, m, "closeup")  # 062 FR-72 입 안
    # 088 T29: 피부 UV1.y = 0.65 → 셰이더 눈썹·속눈썹 마스크 0.35배 (원본 가닥이 주 — 시안 눈썹). 재질 값은 LOD0 고정 등급과 같게 두고(062 FR-88) 메시로 구분
    wh = m.W[:, MS.OUR_NAMES.index("Head")]
    t = np.clip((wh - 0.3) / 0.4, 0.0, 1.0)
    t = t * t * (3 - 2 * t)                                   # 머리 뼈 가중치 0.3 → 0.7에서 목 → 얼굴로 부드럽게
    m.hairdata = np.column_stack([np.full(len(m.verts), CLOSEUP_SKIN_HD[0]),
                                  CLOSEUP_NECK_HD_Y + (CLOSEUP_SKIN_HD[1] - CLOSEUP_NECK_HD_Y) * t])
    return m
