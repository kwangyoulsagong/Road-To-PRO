"""외형 팩 기준 (specs/062 FR-67~69, AC-16). 049 외형 팩 검사기가 참조하는 '기준 머리 뼈대 · 기준 메시 · UV 규격 버전'을 내보낸다.

head_reference.json:
  bones          — 기준 뼈대 전체(이름·부모·바인드 head/tail, 선수 좌표 Y-up m). 팩 머리·헤어 메시의 뼈는 이 안에 있어야 하고
                   이름·부모·바인드 포즈가 같아야 한다(FR-67).
  referenceMesh  — 기준 머리 메시 = player.glb의 Head_Closeup (목 이음매 정점은 이 메시의 열린 아래 고리, Unity가 용접 위치로 찾는다).
  neckSeam       — 기준 메시 목 고리 측정값(정점 수·높이) + 몸 LOD0 표면까지 거리(최대·p95, mm) → 허용 오차 [제안]의 근거(FR-68 [미정]).
  seamToleranceM — 목 이음매 허용 오차 [제안] (승인 전 잠정값).
  maxInfluences  — 정점당 뼈 수 상한 4.
  uv             — 규격 버전·해시(얼굴 UV 배치 + 기준 메시 UV + 클로즈업 텍스처 해상도)·버전 이력(FR-69). 해시가 이력에 없으면
                   생성이 멈춘다 — 배치를 바꿨다면 PLAYER_UV_BUMP=1로 다시 돌려 uv_versions.json에 새 버전(최대 + 1)을 넣는다.
head_uv_template.png — 제작자용 UV 템플릿: 기준 메시 UV 와이어프레임 + 영역 테두리만(가상, 실존 인물 사진·얼굴 없음, FR-61·69).
"""
import hashlib
import json
import os

import numpy as np

from . import skeleton as S
from . import skin as SK
from . import uv as U

VERSIONS = os.path.join(os.path.dirname(__file__), "uv_versions.json")
MAX_INFLUENCES = 4
TEMPLATE = 1024
SEAM_TOL = 0.002   # [제안] 목 이음매 허용 오차 2 mm (2026-11-21 사용자 "베스트 케이스" — 기준 메시에서 만든 팩의 목 고리 이동 한도.
                   # neckSeam 측정값(클로즈업 머리 ↔ 감량 LOD0 간격 p95 ≈ 7 mm)은 근거 기록용, 허용 오차로 쓰면 느슨하다)


def _seam(head):
    """기준 메시(클로즈업 머리) 열린 경계 중 가장 낮은 고리 정점 (위치 용접 1e-5 m)."""
    key = {}
    vid = []
    for p in head.verts:
        k = tuple(np.round(p / 1e-5).astype(np.int64))
        vid.append(key.setdefault(k, len(key)))
    count = {}
    for f, mat, uvs, part, _ in head.faces:
        if part != "skin":
            continue
        for i in range(len(f)):
            a, b = vid[f[i]], vid[f[(i + 1) % len(f)]]
            e = (min(a, b), max(a, b))
            count[e] = count.get(e, 0) + 1
    rep = {}
    for i, g in enumerate(vid):
        rep.setdefault(g, i)
    bverts = sorted({v for e, c in count.items() if c == 1 for v in e})
    P = np.array([head.verts[rep[g]] for g in bverts])
    if not len(P):
        return P
    y0 = P[:, 1].min()
    return P[P[:, 1] < y0 + 0.06]   # 목 고리 (눈·입 안 경계는 위쪽)


def _uv_hash(head):
    h = hashlib.sha256()
    h.update(json.dumps({"body": U.BODY_REGIONS, "face": [U.FACE_X, U.FACE_Y0, U.FACE_Y1], "closeup": SK.CLOSEUP},
                        sort_keys=True).encode())
    for f, mat, uvs, part, _ in head.faces:
        for u in uvs:
            h.update(f"{u[0]:.5f},{u[1]:.5f};".encode())
    return h.hexdigest()[:16]


def _history():
    if not os.path.exists(VERSIONS):
        return []
    with open(VERSIONS, encoding="utf-8") as f:
        return json.load(f)["versions"]


def uv_version(head):
    """(버전, 해시, 이력). 해시가 이력에 없으면 PLAYER_UV_BUMP=1일 때만 새 버전을 이력에 쓴다."""
    hsh = _uv_hash(head)
    hist = _history()
    for e in hist:
        if e["hash"] == hsh:
            return e["version"], hsh, hist
    if os.environ.get("PLAYER_UV_BUMP") != "1":
        raise RuntimeError(f"UV 규격 해시 {hsh}가 uv_versions.json 이력에 없다 — 얼굴 UV·기준 메시·해상도를 바꿨다면 "
                           "PLAYER_UV_BUMP=1로 다시 생성해 버전을 올린다 (062 FR-69)")
    v = max([e["version"] for e in hist], default=0) + 1
    hist = hist + [{"version": v, "hash": hsh}]
    with open(VERSIONS, "w", encoding="utf-8") as f:
        json.dump({"schemaVersion": 1, "spec": "specs/062 FR-69", "versions": hist}, f, ensure_ascii=False, indent=2)
        f.write("\n")
    return v, hsh, hist


def seam_gap(head, body0):
    """목 고리 정점 → 몸 LOD0 표면 최근접 거리(mm): 최대·p95. 클로즈업 머리 ↔ 몸 LOD0 이음매의 현재 간격 = 허용 오차 근거."""
    from mathutils.bvhtree import BVHTree
    P = _seam(head)
    faces = [list(f) for f, mat, uvs, part, _ in body0.faces if part == "skin"]
    tree = BVHTree.FromPolygons([tuple(p) for p in body0.verts], faces, all_triangles=False)
    d = []
    for p in P:
        hit = tree.find_nearest(tuple(p))
        if hit[0] is not None:
            d.append(hit[3])
    d = np.sort(np.array(d)) if d else np.zeros(1)
    return {"verts": int(len(P)), "yMin": round(float(P[:, 1].min()), 5) if len(P) else 0.0,
            "yMax": round(float(P[:, 1].max()), 5) if len(P) else 0.0,
            "gapMaxMm": round(float(d.max()) * 1000, 3), "gapP95Mm": round(float(d[int(0.95 * (len(d) - 1))]) * 1000, 3)}


def template(head):
    """UV 템플릿 PNG (RGBA): 기준 메시 UV 삼각형 선(흰색) + 얼굴 칸·피부·헤어 영역 테두리(회색). 가상 — 사진·얼굴 그림 없음."""
    n = TEMPLATE
    img = np.zeros((n, n, 4), dtype=np.uint8)
    img[..., 3] = 255

    def line(a, b, col):
        k = int(max(abs(b[0] - a[0]), abs(b[1] - a[1])) * n) + 2
        t = np.linspace(0.0, 1.0, k)
        x = np.clip(((a[0] + (b[0] - a[0]) * t) % 1.0 * n).astype(int), 0, n - 1)
        y = np.clip(((1.0 - (a[1] + (b[1] - a[1]) * t)) * n).astype(int), 0, n - 1)
        img[y, x, :3] = col

    for name, (u0, v0, u1, v1) in U.BODY_REGIONS.items():
        for a, b in (((u0, v0), (u1, v0)), ((u1, v0), (u1, v1)), ((u1, v1), (u0, v1)), ((u0, v1), (u0, v0))):
            line(a, b, (110, 110, 110))
    for f, mat, uvs, part, _ in head.faces:
        if part != "skin":
            continue
        for i in range(len(uvs)):
            a, b = uvs[i], uvs[(i + 1) % len(uvs)]
            if a[0] < 1.0 and b[0] < 1.0:
                line(a, b, (235, 235, 235))
    return img


def write(out_dir, head, body0):
    from . import textures as T
    ver, hsh, hist = uv_version(head)
    gap = seam_gap(head, body0)
    tol = SEAM_TOL
    data = {
        "schemaVersion": 1,
        "spec": "specs/062 FR-67~69",
        "bones": [{"name": nm, "parent": par, "head": [round(x, 6) for x in h], "tail": [round(x, 6) for x in t]}
                  for nm, par, h, t in S.BONES],
        "referenceMesh": {"glb": "player.glb", "mesh": "Head_Closeup"},
        "neckSeam": gap,
        "seamToleranceM": tol,
        "maxInfluences": MAX_INFLUENCES,
        "uv": {"version": ver, "hash": hsh, "history": hist, "closeupTexture": SK.CLOSEUP, "template": "head_uv_template.png"},
    }
    with open(os.path.join(out_dir, "head_reference.json"), "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
        f.write("\n")
    T.write_png(os.path.join(out_dir, "head_uv_template.png"), template(head))
    return {"ok": gap["verts"] > 0, "uvVersion": ver, "uvHash": hsh, "seamToleranceM": tol, "neckSeam": gap}
