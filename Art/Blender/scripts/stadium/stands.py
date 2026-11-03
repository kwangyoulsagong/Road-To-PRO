"""관중석 (specs/044 FR-10·25, AC-09). 4면 × 층 × 줄 계단. 좌석은 메시가 아니라 좌석 줄 데이터(045 입력).

계획(`plan`)은 bpy 없이 계산한다 — 층별 줄 수·높이, 줄별 좌석 수. 좌석 색은 시드로 블록 단위 2색 패턴(글자·엠블럼 없음, FR-51).
"""
import math
import random

from . import layout as L

BLOCK_LEN = 10.0     # 좌석 색 블록 길이(줄 방향)
ROWS_PER_BAND = 5    # 같은 색 블록이 이어지는 줄 수


def seats_per_row(section):
    _, _, _, half = L.section_frame(section)
    return int(math.floor((2 * half - 2 * 0.5) / L.SEAT_PITCH))


def plan(p):
    ring = sum(seats_per_row(s) for s in L.SECTIONS)
    rows = int(math.ceil(p.capacity / (p.tiers * ring)))
    tiers = []
    u0, z0 = 0.0, L.FRONT_WALL
    for t in range(p.tiers):
        if t > 0:
            z0 = tiers[-1]["top"] + L.TIER_FASCIA
        tiers.append({"index": t + 1, "u0": u0, "z0": z0, "rows": rows,
                      "top": z0 + (rows - 1) * L.ROW_RISE, "depth": rows * L.ROW_DEPTH})
        u0 += rows * L.ROW_DEPTH
    remaining = p.capacity
    seat_rows = []
    for tier in tiers:
        for i in range(tier["rows"]):
            for sec in L.SECTIONS:
                c = min(seats_per_row(sec), remaining)
                if c <= 0:
                    continue
                remaining -= c
                seat_rows.append((sec, tier["index"], i, c))
    assert remaining == 0
    return {"tiers": tiers, "seatRows": seat_rows, "depth": u0, "top": tiers[-1]["top"]}


def _pt(section, u, h, s):
    n, t, front, _ = L.section_frame(section)
    d = front + u
    return (n[0] * d + t[0] * s, n[1] * d + t[1] * s, h)


def build(p, mats, pl):
    rng = random.Random(p.seed)
    rows_out = []
    seat_lookup = {}
    for sec, tier_idx, i, c in pl["seatRows"]:
        seat_lookup[(sec, tier_idx, i)] = c
    for sec in L.SECTIONS:
        n, t, front, half = L.section_frame(sec)
        outward = (n[0], n[1], 0.0)
        inward = (-n[0], -n[1], 0.0)
        cap = seats_per_row(sec)
        nblocks = max(1, int(round(2 * half / BLOCK_LEN)))
        for tier in pl["tiers"]:
            b = geometry_builder()
            u0, z0, rows = tier["u0"], tier["z0"], tier["rows"]
            # 좌석 색: 시드로 고른 2색 패턴 (글자·엠블럼 없음, FR-51). 띠 = 5줄, 블록 = 약 10 m
            colors = {}
            nb = (rows + ROWS_PER_BAND - 1) // ROWS_PER_BAND
            kind = rng.choice(("bands", "columns", "checker"))
            every = rng.choice((2, 3))
            phase = rng.randrange(every)
            for band in range(nb):
                for k in range(nblocks):
                    key = {"bands": band, "columns": k, "checker": band + k}[kind]
                    colors[(band, k)] = "Seat_B" if (key + phase) % every == 0 else "Seat_A"
            if sec == L.AWAY_SECTION:
                colors = {key: ("Seat_A" if v == "Seat_B" else "Seat_B") for key, v in colors.items()}
            profile = [(u0, 0.0), (u0, z0)]
            for i in range(rows):
                z = z0 + i * L.ROW_RISE
                ua, ub = u0 + i * L.ROW_DEPTH, u0 + (i + 1) * L.ROW_DEPTH
                profile.append((ub, z))
                if i + 1 < rows:
                    profile.append((ub, z + L.ROW_RISE))
                for k in range(nblocks):
                    sa = -half + 2 * half * k / nblocks
                    sb = -half + 2 * half * (k + 1) / nblocks
                    b.quad(_pt(sec, ua, z, sa), _pt(sec, ub, z, sa), _pt(sec, ub, z, sb), _pt(sec, ua, z, sb),
                           mats[colors[(i // ROWS_PER_BAND, k)]], (0, 0, 1))
                c = seat_lookup.get((sec, tier["index"], i), 0)
                if c > 0:
                    s_first = -(cap - 1) / 2 * L.SEAT_PITCH
                    start = _pt(sec, (ua + ub) / 2, z, s_first)
                    rows_out.append({"section": sec, "tier": tier["index"], "row": i, "home": sec != L.AWAY_SECTION,
                                     "start": L.unity(start), "dir": L.unity((t[0], t[1], 0.0)),
                                     "spacing": L.SEAT_PITCH, "count": c})
            profile.append((u0 + rows * L.ROW_DEPTH, 0.0))
            # 수직면(앞 벽·챌면·뒤 벽): 프로필 연속 점 중 수직 구간
            conc = mats["Concrete"]
            for j in range(len(profile) - 1):
                (ua, ha), (ub, hb) = profile[j], profile[j + 1]
                if abs(ua - ub) < 1e-9 and abs(ha - hb) > 1e-9:
                    want = outward if j == len(profile) - 2 else inward
                    b.quad(_pt(sec, ua, ha, -half), _pt(sec, ub, hb, -half), _pt(sec, ub, hb, half), _pt(sec, ua, ha, half),
                           conc, want)
            # 양 끝 마감(프로필 다각형)
            for s_end, sign in ((-half, -1.0), (half, 1.0)):
                idx = [b.v(_pt(sec, u, h, s_end)) for (u, h) in profile]
                b.face(idx, conc, (t[0] * sign, t[1] * sign, 0.0))
            b.build(f"Stand_{sec}_T{tier['index']}")
    # 코너 블록 (좌석 없음): 인접 두 면 사이를 1층 높이로 막는다
    top1 = pl["tiers"][0]["top"]
    d = pl["depth"]
    for sx, sy, nm in ((-1, -1, "SW"), (1, -1, "SE"), (-1, 1, "NW"), (1, 1, "NE")):
        cb = geometry_builder()
        x0, x1 = sorted((sx * L.STAND_X, sx * (L.STAND_X + d)))
        y0, y1 = sorted((sy * L.STAND_Y, sy * (L.STAND_Y + d)))
        cb.box((x0, y0, 0.0), (x1, y1, top1), mats["Concrete"])
        cb.build(f"Stand_Corner_{nm}")
    return rows_out


def geometry_builder():
    from . import geometry
    return geometry.MeshBuilder()
