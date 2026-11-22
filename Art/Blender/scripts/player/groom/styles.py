"""그루밍 스타일 표 (specs/088 FR-10·20). 게임 헤어 이름(062 순서 0~34) → 그루밍 정의.

승인 4종(2026-11-22 사용자 사진 비교 승인)은 시안 main()의 흐름을 그대로 옮겼다:
  Hair_Quiff ← pomade · Hair_PartedTwoBlock ← gail_g · Hair_TexturedCrop ← see_g · Hair_Curtains ← garma.
나머지 스타일은 T12~T17에서 같은 방식(바탕장 F·H·L + 가이드 + 높이 상한)으로 더하고, Claude가 3시점 렌더로 판정해 approved=True로 둔다
(스펙 Q6 개정, 사용자 최종 일괄 확인).
"""
import numpy as np

from . import grow as G

SEED = 7          # 시안과 같은 시드 (모든 스타일 공통 — 스타일 사이 독립은 스타일별 가닥 수·흐름이 만든다)
STEPS = 16


def _keep(P, C):
    x0, f0, z0 = C.of(P)
    return ~((np.abs(x0) > 0.05) & (z0 < -0.012) & (f0 > -0.03) & (f0 < 0.035))


def _post_parted_two_block(S, P, C, tree, rng):
    S, _ = G.apply_guides(S, P[_keep(P, C)], C, tree, rng, G.GAIL_GUIDES, G.GAIL_SIDE, G.GAIL_G_PART)
    return S, P[_keep(P, C)]


def _post_textured_crop(S, P, C, tree, rng):
    k = _keep(P, C)
    S, _ = G.apply_guides(S, P[k], C, tree, rng, G.SEE_GUIDES, G.SEE_SIDE, -1.0, noise=0.003, clump=0.55, cdiv=600,
                          ridge_x=0.078, fmin=-0.10, cap_fn=G.see_cap)
    S, bidx = G.see_bang_locks(S, P[k], C, tree, rng, S.shape[1] - 1)
    bang = np.zeros(len(S), bool)
    bang[bidx] = True
    drop = bang & (rng.random(len(S)) < G.SEE_THIN)
    return S[~drop], P[k][~drop]


def _post_curtains(S, P, C, tree, rng):
    k = _keep(P, C)
    S, _ = G.apply_guides(S, P[k], C, tree, rng, G.GARMA_GUIDES, G.GARMA_SIDE, G.GARMA_PART, noise=0.0025, clump=0.55,
                          cdiv=500, ridge_x=0.078, fmin=-0.10, cap_fn=G.garma_cap)
    S = G.garma_trim(S, C, rng)
    S = G.garma_wave(S, C, tree, rng)
    return S, P[k]


def _post_none(S, P, C, tree, rng):
    return S, P[_keep(P, C)]


# 이름 → 정의. fn = 바탕장(F, H, L), n = 가닥 수, post = 가이드·후처리, strand = 결 계열(공유 아틀라스 타일),
# part = 가르마 x(머리 좌표, m, 없으면 None), fringe = 앞머리 끝 기준(눈썹 위 높이 m, 없으면 None), approved = 3시점 판정 통과
STYLES = {
    "Hair_Quiff": dict(fn=G.pomade, n=110000, post=_post_none, strand="straight", part=-0.030, fringe=None, approved=True,
                       note="포마드 (옆가르마·앞 퀴프) — 사용자 승인 2026-11-22"),
    "Hair_PartedTwoBlock": dict(fn=G.pomade, n=150000, post=_post_parted_two_block, strand="straight", part=G.GAIL_G_PART,
                                fringe=-0.005, cp="top", approved=True, note="가일컷 3:7 — 사용자 승인 2026-11-22"),
    "Hair_TexturedCrop": dict(fn=G.seethrough_base, n=150000, post=_post_textured_crop, strand="straight", part=None,
                              fringe=-0.015, approved=True, note="시스루 — 사용자 승인 2026-11-22"),
    "Hair_Curtains": dict(fn=G.garma_base, n=160000, post=_post_curtains, strand="wave", part=G.GARMA_PART, fringe=-0.004,
                          approved=True, note="가르마 6:4 펌 — 사용자 승인 2026-11-22"),
}


def groom(name, V, T, eye, density=1.0):
    """V·T = 머리 표면(Blender 틀), eye = 두 눈 중심. → (S (k, STEPS+1, 3), C). density < 1이면 가닥 수를 줄인다(빠른 지표용)."""
    st = STYLES[name]
    rng = np.random.default_rng(SEED)
    tree = BVHTree_from(V, T)
    C = G.Coords(eye)
    P, N = G.roots(V, T, eye, max(2000, int(st["n"] * density)), "full", rng)
    S, _ = G.grow2(P, N, C, st["fn"], tree, rng, steps=STEPS)
    S, P2 = st["post"](S, P, C, tree, rng)
    tipr = np.linalg.norm(S[:, -1] - C.c, axis=1)
    midr = np.linalg.norm(S[:, S.shape[1] // 2] - C.c, axis=1)
    good = (tipr < np.percentile(tipr, 99.7)) & (midr < np.percentile(midr, 99.7))
    return S[good], C


def BVHTree_from(V, T):
    return G.BVHTree.FromPolygons([tuple(p) for p in V], [tuple(int(i) for i in t) for t in T])


# ---------------------------------------------------------------- 088 T12 짧은 계열 8종 (매개변수형, Claude 3시점 판정)
from . import param as PM  # noqa: E402


def _line_drop(x, f, z):
    """버즈 + 라인: 선수 오른쪽 옆머리에 비스듬한 면도 선 (폭 3 mm)."""
    return (x < -0.045) & (np.abs(z - (0.018 + 0.25 * f)) < 0.0028) & (f > -0.06) & (f < 0.045)


def _S(fn, n, post, strand="straight", part=None, fringe=None, cp=None, approved=False, note="", dense=False):
    return dict(fn=fn, n=n, post=post, strand=strand, part=part, fringe=fringe, cp=cp, approved=approved, note=note, dense=dense)


STYLES.update({
    "Hair_Buzz": _S(PM.make(top_len=0.006, side_len=0.005, back_len=0.005, top_flow="natural", side_flow="down", vol_top=0.002,
                            vol_side=0.0015, over_ear=False), 140000, PM.post(capf=PM.cap(0.004, 0.003)), note="버즈컷", approved=True),
    "Hair_BuzzLine": _S(PM.make(top_len=0.006, side_len=0.005, back_len=0.005, top_flow="natural", side_flow="down", vol_top=0.002,
                                vol_side=0.0015, over_ear=False), 140000, PM.post(capf=PM.cap(0.004, 0.003), drop=_line_drop),
                        note="버즈 + 라인", approved=True),
    "Hair_Short": _S(PM.make(top_len=0.032, side_len=0.013, back_len=0.012, top_flow="back", vol_top=0.008, vol_side=0.004,
                             fringe_len=0.022, fringe_flow="up", fringe_f=0.03), 120000,
                     PM.post(capf=PM.cap(0.011, 0.006), clump_n=500, clump_s=0.35), note="숏컷 (앞머리 살짝 세움)", approved=True),
    "Hair_Crew": _S(PM.make(top_len=0.028, side_len=0.010, back_len=0.010, top_flow="up", vol_top=0.008, vol_side=0.003,
                            fade=(0.004, -0.045, 0.02), fringe_len=0.018, fringe_flow="up", fringe_f=0.03), 130000,
                    PM.post(capf=PM.cap(0.011, 0.005), clump_n=600, clump_s=0.3),
                    note="크루컷 (짧은 스포츠 컷)", approved=True),
    "Hair_FrenchCrop": _S(PM.make(top_len=0.030, side_len=0.008, back_len=0.008, top_flow="forward", vol_top=0.006, vol_side=0.003,
                                  fade=(0.003, -0.05, 0.01), fringe_len=0.026, fringe_flow="forward"), 130000,
                          PM.post(capf=PM.cap(0.010, 0.005), clump_n=500, clump_s=0.45), fringe=0.02, note="프렌치 크롭", approved=True),
    "Hair_Spiky": _S(PM.make(top_len=0.036, side_len=0.010, back_len=0.010, top_flow="up", vol_top=0.020, vol_side=0.004,
                             fringe_len=0.03, fringe_flow="up", fringe_f=0.03), 120000,
                     PM.post(capf=PM.cap(0.020, 0.006), clump_n=220, clump_s=0.9, clump_min=0.025), note="스파이크", approved=True),
    "Hair_SidePart": _S(PM.make(top_len=0.042, side_len=0.012, back_len=0.011, top_flow="side", part=-0.030, vol_top=0.007,
                                vol_side=0.004), 130000, PM.post(capf=PM.cap(0.010, 0.006)), part=-0.030, note="사이드 파트", approved=True),
    "Hair_IvyLeague": _S(PM.make(top_len=0.045, side_len=0.007, back_len=0.007, top_flow="backup", part=-0.025, vol_top=0.013,
                                 vol_side=0.002, fade=(0.0015, -0.025, 0.035), fringe_len=0.035, fringe_flow="up", fringe_f=0.03),
                         130000, PM.post(capf=PM.cap(0.014, 0.004)),
                         part=-0.025, note="아이비리그", approved=True),
})


# ---------------------------------------------------------------- 088 T13 페이드 3종 · T14 중간 3종
def _strip_cap(width=0.022, top=0.035, side=0.002):
    def c(x, f, z):
        t = PM.smooth((width - np.abs(x)) / 0.008) * PM.smooth((z - 0.0) / 0.04)
        return side + (top - side) * t
    return c


def _band_cap(width, top, side):
    """윗머리 띠(|x| < width) 안만 볼륨, 밖은 짧게 민 높이 (언더컷)."""
    def c(x, f, z):
        return side + (top - side) * PM.smooth((width - np.abs(x)) / 0.006)
    return c


def _low_long(x, f, z):
    return np.zeros(len(x), bool)


def _strip_drop(x, f, z):
    """모히칸: 띠 밖 옆머리 뿌리를 대부분 뺀다 (짧게 민 그늘은 셸 밀도가 맡는다)."""
    return (np.abs(x) > 0.03) & (z > -0.01)


STYLES.update({
    "Hair_Fade": _S(PM.make(top_len=0.026, side_len=0.012, back_len=0.012, top_flow="natural", vol_top=0.007, vol_side=0.003,
                            fade=(0.0008, -0.045, 0.03), fringe_len=0.018, fringe_flow="up", fringe_f=0.03), 130000,
                    PM.post(capf=PM.cap(0.010, 0.004), clump_n=500, clump_s=0.3), cp="top", note="페이드", approved=True),
    "Hair_Undercut": _S(PM.make(top_len=0.06, side_len=0.010, back_len=0.010, top_flow="backup", vol_top=0.016, vol_side=0.002,
                                fade=(0.0015, 0.012, 0.02), ridge_w=0.046), 140000,
                        PM.post(capf=_band_cap(0.046, 0.02, 0.003), drop=_low_long), cp="top",
                        note="언더컷 (윗머리 뒤로, 옆 밀기)", approved=True),
    "Hair_Mohawk": _S(PM.make(top_len=0.05, side_len=0.0015, back_len=0.0015, top_flow="up", vol_top=0.032, vol_side=0.0015,
                              ridge_w=0.025), 120000, PM.post(capf=_strip_cap(), clump_n=200, clump_s=0.7, clump_min=0.02),
                      cp="strip", note="모히칸", approved=True),
    "Hair_SlickBack": _S(PM.make(top_len=0.07, side_len=0.022, back_len=0.03, top_flow="back", side_flow="back", vol_top=0.008,
                                 vol_side=0.005), 140000, PM.post(capf=PM.cap(0.010, 0.006)), note="슬릭백", approved=True),
    "Hair_MediumWave": _S(PM.make(top_len=0.08, side_len=0.04, back_len=0.045, top_flow="side", part=0.02, vol_top=0.012,
                                  vol_side=0.008, over_ear=False), 150000,
                          PM.post(capf=PM.cap(0.016, 0.010), wave_amp=0.003, wave_lam=0.035), strand="wave", part=0.02,
                          note="미디엄 웨이브", approved=True),
    "Hair_CommaPerm": _S(PM.make(top_len=0.065, side_len=0.03, back_len=0.03, top_flow="side", part=-0.032, vol_top=0.013,
                                 vol_side=0.007, fringe_len=0.055, fringe_flow="side", fringe_f=0.03, over_ear=False), 150000,
                         PM.post(capf=PM.cap(0.016, 0.009), wave_amp=0.004, wave_lam=0.022), strand="wave", fringe=-0.005,
                         part=-0.032,
                         note="쉼표머리 펌", approved=True),
})


# ---------------------------------------------------------------- 088 T15 곱슬 5종 (FR-43 결: 곱슬 = 나선, 코일 = 촘촘한 나선)
STYLES.update({
    "Hair_Curly": _S(PM.make(top_len=0.05, side_len=0.032, back_len=0.032, top_flow="natural", vol_top=0.022, vol_side=0.014,
                             over_ear=False), 140000, PM.post(capf=PM.cap(0.026, 0.016), helix_r=0.0035, helix_turns=5.0,
                                                              clump_n=600, clump_s=0.4, clump_min=0.02), strand="curl",
                     note="곱슬", approved=True),
    "Hair_Afro": _S(PM.make(top_len=0.08, side_len=0.07, back_len=0.07, top_flow="up", side_flow="natural", vol_top=0.055,
                            vol_side=0.045, over_ear=False), 160000,
                    PM.post(extra=PM.shape_to(PM.afro_target(), 0.0035, 8.0)), strand="coil", note="아프로", approved=True),
    "Hair_HighTop": _S(PM.make(top_len=0.07, side_len=0.006, back_len=0.006, top_flow="up", vol_top=0.055, vol_side=0.003,
                               fade=(0.0015, -0.035, 0.035), ridge_w=0.058), 150000,
                       PM.post(extra=PM.shape_to(PM.hightop_target(), 0.0025, 8.0)), strand="coil",
                       note="하이톱", approved=True),
    "Hair_Twists": _S(PM.make(top_len=0.055, side_len=0.04, back_len=0.045, top_flow="natural", vol_top=0.02, vol_side=0.012,
                              over_ear=False), 140000,
                      PM.post(capf=PM.cap(0.024, 0.014), clump_n=320, clump_s=0.97, clump_min=0.02, helix_r=0.0016,
                              helix_turns=6.0), strand="coil", note="트위스트", approved=True),
    "Hair_CurlyTop": _S(PM.make(top_len=0.042, side_len=0.008, back_len=0.008, top_flow="up", vol_top=0.02, vol_side=0.003,
                                fade=(0.001, -0.04, 0.025)), 140000,
                        PM.post(capf=_band_cap(0.06, 0.024, 0.004), helix_r=0.003, helix_turns=7.0), strand="curl", cp="top",
                        note="짧은 곱슬 톱 + 페이드", approved=True),
})


# ---------------------------------------------------------------- 088 T16 브레이드 5종
STYLES.update({
    "Hair_Cornrows": _S(PM.make(top_len=0.12, side_len=0.06, back_len=0.03, top_flow="back", side_flow="back", vol_top=0.002,
                                vol_side=0.0018, over_ear=False), 110000,
                        PM.post(capf=PM.cap(0.004, 0.003), extra=PM.rows(0.016, 0.25)), strand="braid", note="콘로", approved=True),
    "Hair_StraightCornrows": _S(PM.make(top_len=0.12, side_len=0.06, back_len=0.03, top_flow="back", side_flow="back",
                                        vol_top=0.002, vol_side=0.0018, over_ear=False), 110000,
                                PM.post(capf=PM.cap(0.004, 0.003), extra=PM.rows(0.014, 0.0)), strand="braid",
                                note="직선 콘로우", approved=True),
    "Hair_ZigzagCornrows": _S(PM.make(top_len=0.12, side_len=0.06, back_len=0.03, top_flow="back", side_flow="back",
                                      vol_top=0.002, vol_side=0.0018, over_ear=False), 110000,
                              PM.post(capf=PM.cap(0.004, 0.003), extra=PM.rows(0.018, 0.1, zig=0.012)), strand="braid",
                              note="지그재그 콘로우", approved=True),
    "Hair_BoxBraids": _S(PM.make(top_len=0.20, side_len=0.17, back_len=0.19, top_flow="back", side_flow="down", vol_top=0.006,
                                 vol_side=0.005, over_ear=False), 120000,
                         PM.post(clump_n=140, clump_s=0.97, clump_min=0.05, clump_root=True, helix_r=0.0022, helix_turns=10.0),
                         strand="braid", cp="tips", note="박스 브레이드 (어깨 길이)", approved=True),
    "Hair_Dreads": _S(PM.make(top_len=0.15, side_len=0.13, back_len=0.15, top_flow="back", side_flow="down", vol_top=0.008,
                              vol_side=0.006, over_ear=False), 120000,
                      PM.post(clump_n=65, clump_s=0.96, clump_min=0.05, clump_root=True, helix_r=0.0010, helix_turns=3.0),
                      strand="dread", note="드레드", approved=True),
})


# ---------------------------------------------------------------- 088 T17 장발·묶음 6종
STYLES.update({
    "Hair_Long": _S(PM.make(top_len=0.17, side_len=0.15, back_len=0.17, top_flow="side", side_flow="down", part=0.012,
                            vol_top=0.010, vol_side=0.008, over_ear=False), 160000,
                    PM.post(capf=PM.cap(0.014, 0.011), clump_n=500, clump_s=0.35, clump_min=0.05), part=0.012,
                    note="장발 (어깨 위)", approved=True),
    "Hair_VolumeCurlyLong": _S(PM.make(top_len=0.13, side_len=0.12, back_len=0.13, top_flow="side", side_flow="down", part=0.0,
                                       vol_top=0.03, vol_side=0.028, over_ear=False), 160000,
                               PM.post(capf=PM.cap(0.04, 0.035), helix_r=0.0045, helix_turns=4.0, clump_n=500, clump_s=0.4,
                                       clump_min=0.05), strand="curl", note="볼륨 큰 곱슬 장발", approved=True),
    "Hair_Bun": _S(PM.make(top_len=0.14, side_len=0.12, back_len=0.12, top_flow="back", side_flow="back", vol_top=0.004,
                           vol_side=0.003, over_ear=False), 130000,
                   PM.post(extra=PM.tied((0.0, -0.085, 0.045), "bun", 0.032)), dense=True, note="번 (뒤통수 묶음)", approved=True),
    "Hair_TopKnot": _S(PM.make(top_len=0.12, side_len=0.006, back_len=0.006, top_flow="back", vol_top=0.004, vol_side=0.002,
                               fade=(0.0015, -0.03, 0.03), ridge_w=0.055), 130000,
                       PM.post(extra=PM.tied((0.0, -0.03, 0.10), "bun", 0.026, split_z=0.035)), dense=True, note="톱 노트 (옆 짧게)", approved=True),
    "Hair_Ponytail": _S(PM.make(top_len=0.14, side_len=0.12, back_len=0.12, top_flow="back", side_flow="back", vol_top=0.004,
                                vol_side=0.003, over_ear=False), 130000,
                        PM.post(extra=PM.tied((0.0, -0.095, 0.0), "tail", tail_len=0.15, tail_clump=0)), dense=True, note="포니테일", approved=True),
    "Hair_HalfUp": _S(PM.make(top_len=0.15, side_len=0.14, back_len=0.15, top_flow="back", side_flow="down", vol_top=0.006,
                              vol_side=0.008, over_ear=False), 180000,
                      PM.post(extra=PM.tied((0.0, -0.08, 0.06), "bun", 0.022, split_z=0.045, standoff=0.004), clump_n=400, clump_s=0.3,
                              clump_min=0.06), dense=True, note="하프 업", approved=True),
})
