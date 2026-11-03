"""경기장 생성 (specs/044). 스크립트가 원본, glb·layout.json·미리보기 PNG는 산출물.

실행: .claude/scripts/blender-gen.sh Art/Blender/scripts/gen_stadium.py <out.glb> [인자]
인자: --out <glb>  --capacity {8000,20000,40000,60000}  --tiers 1~3  --roof none|partial|full
      --seat-colors #RRGGBB,#RRGGBB  --lights towers|roofline  --boards on|off  --seed <int>  [--no-preview]
산출물(같은 폴더): <이름>.glb, <이름>.layout.json, <이름>.preview-{main,goal,top}.png
종료 코드: 0 성공, 2 인자 오류, 3 규격·예산 검사 실패, 1 예외. 실패하면 출력 파일을 남기지 않는다.
예산: 삼각형 ≤ 100k (관중 제외), 머티리얼 ≤ 12, 텍스처 없음 (피치 텍스처는 Unity 쪽).
"""
import argparse
import os
import shutil
import sys
import tempfile
import time

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

from stadium import params as P  # noqa: E402

TRIANGLE_BUDGET = 100_000
MATERIAL_BUDGET = 12


class _Parser(argparse.ArgumentParser):
    def error(self, message):
        raise P.ParamError(message)


def parse(argv):
    ap = _Parser(prog="gen_stadium", add_help=False)
    ap.add_argument("--out", required=True)
    ap.add_argument("--capacity", required=True)
    ap.add_argument("--tiers", required=True)
    ap.add_argument("--roof", required=True)
    ap.add_argument("--seat-colors", required=True)
    ap.add_argument("--lights", required=True)
    ap.add_argument("--boards", required=True)
    ap.add_argument("--seed", required=True)
    ap.add_argument("--no-preview", action="store_true")
    a = ap.parse_args(argv)
    p = P.StadiumParams.from_args(a.capacity, a.tiers, a.roof, a.seat_colors, a.lights, a.boards, a.seed)
    return a, p


def generate(p, stem, preview):
    import bpy  # noqa: F401

    from stadium import boards, camera_data, checks, export, geometry, goals, layout as L, lights, markings
    from stadium import materials, pitch, roof, stands, uv2

    blender = export.check_version()
    geometry.reset_scene()
    mats = materials.make_all(p.seat_colors)
    tuning = L.ball_tuning()
    pitch.build(mats)
    goal_data = goals.build(mats, tuning)
    pl = stands.plan(p)
    seat_rows = stands.build(p, mats, pl)
    roofs = roof.build(p, mats, pl)
    emitters = lights.build(p, mats, pl, roofs)
    boards.build(p, mats)
    for o in checks.mesh_objects():
        uv2.assign(o)

    meas = checks.measure()
    spec = checks.spec_check(meas)
    mount = camera_data.mount_center(pl)
    structure = []
    for o in checks.mesh_objects():
        if o.name.startswith(("Stand_", "Roof_", "LightTower_", "LightStrip_", "Tunnel", "Bench_")):
            lo, hi = checks.bbox(checks.stadium_verts(o))
            structure.append((o.name, lo, hi))
    counts = checks.summary_counts()
    notes = []
    if p.lights_switched:
        notes.append("전체 지붕 + 4기 타워 → 지붕 라인 조명으로 자동 전환 (FR-61)")
    summary = dict(counts)
    summary["checks"] = {
        "spec": spec,
        "runoff": checks.runoff_intrusions(),
        "visibility": checks.visibility(mount),
        "uv2": checks.uv2_check(),
        "seats": checks.seats_on_mesh(seat_rows),
        "budget": {"triangles": counts["triangles"]["total"] <= TRIANGLE_BUDGET, "materials": counts["materials"] <= MATERIAL_BUDGET},
    }
    summary["measured"] = meas
    summary["notes"] = notes
    summary["seatCount"] = sum(r["count"] for r in seat_rows)
    mk = markings.pitch_markings()
    lay = {
        "schemaVersion": 1,
        "generator": {"blender": blender, "script": "Art/Blender/scripts/gen_stadium.py", "args": p.as_args(),
                      "command": "python: .claude/scripts/blender-gen.sh Art/Blender/scripts/gen_stadium.py <out> "
                                 + " ".join(f"--{k.replace('_', '-')} {v}" for k, v in p.as_args().items())},
        "pitch": {"length": mk["length"], "width": mk["width"], "lineWidth": mk["lineWidth"], "runoff": L.RUNOFF,
                  "lines": mk["lines"], "spots": mk["spots"], "stripes": mk["stripes"]},
        "goals": goal_data,
        "seats": {"count": summary["seatCount"], "rows": seat_rows},
        "cameras": camera_data.build(pl, structure),
        "lights": {"mode": p.lights, "requested": p.requested_lights, "switchedFromTowers": p.lights_switched, "emitters": emitters},
        "stands": {"tiers": [{k: t[k] for k in ("index", "rows", "z0", "top", "depth")} for t in pl["tiers"]],
                   "roof": p.roof, "roofHeight": roof.roof_height(pl) if roofs else None},
        "summary": summary,
    }
    export.write_glb(stem + ".glb")
    export.write_json(stem + ".layout.json", lay)
    if preview:
        from stadium import preview as pv
        pv.render(stem, mount)
    ok = spec["ok"] and all(summary["checks"]["budget"].values()) and not summary["checks"]["runoff"]["intrusions"] \
        and not summary["checks"]["visibility"]["blockedCorners"] and summary["checks"]["uv2"]["overlapRatio"] == 0.0 \
        and not summary["checks"]["uv2"]["missing"] and summary["checks"]["seats"]["maxDistance"] <= 0.05
    return ok, lay


def main(argv):
    t0 = time.time()
    try:
        a, p = parse(argv)
    except P.ParamError as e:
        print(f"[stadium] 인자 오류: {e}", file=sys.stderr)
        sys.stderr.flush()
        os._exit(2)
    out = os.path.abspath(a.out)
    name = os.path.splitext(os.path.basename(out))[0]
    tmp = tempfile.mkdtemp(prefix="stadium-gen-")
    try:
        ok, lay = generate(p, os.path.join(tmp, name), not a.no_preview)
        s = lay["summary"]
        print(f"[stadium] Blender {lay['generator']['blender']} · triangles {s['triangles']['total']} · materials {s['materials']} · "
              f"objects {s['objects']} · seats {s['seatCount']} · bbox {s['bbox']}")
        for k, v in s["triangles"].items():
            print(f"[stadium]   triangles {k}: {v}")
        print(f"[stadium] 검사: 규격 {s['checks']['spec']} · 런오프 {s['checks']['runoff']} · 가시성 {s['checks']['visibility']['blockedCorners']} · "
              f"UV2 {s['checks']['uv2']} · 좌석 {s['checks']['seats']} · 예산 {s['checks']['budget']}")
        for n in s["notes"]:
            print("[stadium] 참고: " + n)
        if not ok:
            print("[stadium] 검사 실패 — 출력하지 않는다", file=sys.stderr)
            sys.stdout.flush()
            sys.stderr.flush()
            shutil.rmtree(tmp, ignore_errors=True)
            os._exit(3)
        os.makedirs(os.path.dirname(out), exist_ok=True)
        for f in sorted(os.listdir(tmp)):
            shutil.move(os.path.join(tmp, f), os.path.join(os.path.dirname(out), f))
        print(f"[stadium] 완료 {time.time() - t0:.1f}s → {out}")
    except Exception:
        shutil.rmtree(tmp, ignore_errors=True)
        raise
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


if __name__ == "__main__":
    main(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
