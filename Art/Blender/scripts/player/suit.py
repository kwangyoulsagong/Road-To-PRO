"""정장 의상 (specs/062 FR-27, AC-15). 076 3D 시상 장면용 정장 1벌 — 재킷·셔츠·바지·구두.

몸 LOD 생성 경로(body.build)를 정장 모드로 한 번 더 돈다: 같은 감량·바인딩·셸·턱·체형/얼굴/표정 키, 절단 평면·부위만 다르다
(재킷 = 엉덩이 아래까지 + 손목 위까지 소매, 바지 = 발목까지, 구두 = 축구화 셸 자리). 얼굴·헤어는 같은 선수 그대로(머리 피부는
유니폼 몸과 같은 정점·키). 셔츠(깃·V존·소맷부리)·넥타이는 정장 마스크(`kit_mask_suit.png`, 키트 UV) 색 영역 — 메시가 늘지 않는다.
천 시뮬·보조 뼈 없음(FR-21 원칙). 디자인은 가상: 로고·문자·실존 브랜드·특정 시상식 복장 없음(01-legal).
색: 중립 3종(차콜·네이비·미드 그레이) [제안] 중 Unity가 시드로 고른다(PlayerLook.SuitColors).
"""
import numpy as np

from . import body as B
from . import textures as T
from . import uv as U

NAME = "Suit_LOD{}"
# 076 시상 장면 동작 (서기·걷기·돌기·입퇴장 걷기) + emotion 그룹 전부
CEREMONY_CLIPS = {"idle", "walk_f", "turn_l90", "turn_r90", "turn_180", "exit_walk_off", "decel_stop"}
MASK = "kit_mask_suit.png"


class _Mode:
    def __enter__(self):
        B.OUTFIT[0] = "suit"
        return self

    def __exit__(self, *a):
        B.OUTFIT[0] = "kit"


def mode():
    """정장 절단 평면·부위로 바꾸는 문맥 (with suit.mode(): ...) — 검사(outline·kit_boundary)도 이 안에서."""
    return _Mode()


def build(lod):
    with mode():
        return B.build(lod)


def suit_mask():
    """kit_mask_suit.png (키트 UV, RGBA = 주색 · 보조색 · 포인트색 가중치 · A 팀 색 / 0 구두 — PlayerKit 셰이더 규칙 그대로).
    주색 = 정장 천(재킷·바지), 포인트색 = 흰 셔츠(앞 V존·뒤 깃·소맷부리), 보조색 = 넥타이(V존 가운데 띠), 구두 = A 0."""
    u, v = T._grid(T.KIT_SIZE)
    R = np.ones_like(u)
    G = np.zeros_like(u)
    Bc = np.zeros_like(u)
    A = np.ones_like(u)
    KR = U.KIT_REGIONS
    front = T._in(u, v, KR["shirtFront"])
    back = T._in(u, v, KR["shirtBack"])
    fl, hl = T._local(u, v, KR["shirtFront"])     # fl 0.5 = 가슴 정중앙, hl 1 = 깃
    _, bh = T._local(u, v, KR["shirtBack"])
    # V존: 가슴 가운데에서 위로 갈수록 넓어지는 쐐기 (hl 0.50 꼭짓점 → 깃에서 반폭 0.13)
    t = np.clip((hl - 0.50) / 0.50, 0.0, 1.0)
    vzone = front & (hl > 0.50) & (np.abs(fl - 0.5) < 0.13 * t)
    tie = vzone & (np.abs(fl - 0.5) < 0.014 + 0.012 * (1.0 - t)) & (hl < 0.965)
    collar = (front & (hl > 0.965) & (np.abs(fl - 0.5) < 0.16)) | (back & (bh > 0.965))
    shirt = (vzone & ~tie) | collar
    for key in ("sleeveL", "sleeveR"):
        m = T._in(u, v, KR[key])
        _, lv = T._local(u, v, KR[key])
        shirt |= m & (lv > 0.965)                  # 소맷부리 밖으로 보이는 셔츠 1 cm
    R = np.where(shirt | tie, 0.0, R)
    Bc = np.where(shirt, 1.0, Bc)
    G = np.where(tie, 1.0, G)
    boots = T._in(u, v, KR["boots"])
    A = np.where(boots, 0.0, A)
    R = np.where(boots, 0.0, R)
    return np.stack([T._to8(R), T._to8(G), T._to8(Bc), T._to8(A)], axis=-1)


def write_mask(out_dir):
    import os
    T.write_png(os.path.join(out_dir, MASK), suit_mask())
    return {MASK: (T.KIT_SIZE, T.KIT_SIZE)}


def summary(clips, suits, parts):
    """test_suit 약속: {ok, lods: {lod: {body, styleMax, ok}}, penetration, outline, boundary}.
    LOD 삼각형 = 정장 몸 + 가장 무거운 헤어 조각(유니폼과 같은 규칙, FR-81). 관통·윤곽은 LOD0 레스트·클립 × 극단 4."""
    from . import checks
    from . import hair as H
    from . import visual_metrics as VM
    out = {"ok": True, "lods": {}}
    for lod, m in sorted(suits.items()):
        hair = max(checks._tris(parts[f"{st}_LOD{lod}"]) for st in H.MESH_STYLES)
        lo, hi = checks.TRI_BUDGET[lod]
        mats = len({f[1] for f in m.faces})
        tot = m.triangles + hair
        ok = lo <= tot <= hi and mats <= checks.MATERIAL_BUDGET
        out["lods"][str(lod)] = {"body": m.triangles, "triangles": tot, "materials": mats, "ok": ok}
        out["ok"] &= ok
    if 0 in suits:
        with mode():
            out["outline"] = VM.outline_metrics(suits[0])
            out["boundary"] = checks.kit_boundary(suits[0], 0)
        # 관통 판정은 시상 장면에서 쓰는 클립(서기·걷기·방향 전환·감정·입퇴장 걷기) — 질주·킥 클립의 손가락 × 허벅지 겹침은
        # 몸 애니메이션 자체(유니폼은 맨살끼리라 검사 밖)라 정장 바지에서만 드러난다 → 기록만 (T32, 결과 줄 참고)
        cer = [c for c in clips if c.id in CEREMONY_CLIPS or c.group == "emotion"]
        out["penetration"] = checks.penetration(cer, {0: suits[0]}, {}, 0)
        out["penetration"]["clips"] = sorted(c.id for c in cer)
        rest = [c for c in clips if c not in cer]
        if rest:
            r = checks.penetration(rest, {0: suits[0]}, {}, 0)
            out["penetrationOther"] = {k: r[k] for k in ("skinKit", "flipped", "poses", "clipHits")}
        out["ok"] &= out["outline"]["ok"] and out["penetration"]["ok"] and out["boundary"]["ok"]
    return out
