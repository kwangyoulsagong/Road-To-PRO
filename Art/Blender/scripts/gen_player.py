"""선수 모델·리그·클립·텍스처 생성 (specs/043). 스크립트가 원본, 산출물은 손으로 고치지 않는다.

실행: /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python Art/Blender/scripts/gen_player.py -- \
        --out <폴더> [--lods 0,1,2] [--clips all|id,id,...] [--seed 0] [--no-preview]
산출물(<폴더>):
  player.glb            Player_LOD0..2(스킨·셰이프키) + PlayerRig + 클립(NLA 트랙 = 애니메이션)
  player.clips.json     클립 태그 (FR-32)
  player.layout.json    뼈대·UV 영역·번호/이름 영역·검사 요약
  Textures/*.png        키트 마스크 6종·원단 노말·몸(얼굴 8종·피부·머리카락)
  player.preview-lineup.png, player.preview-heads.png, player.preview-poses.png  (--no-preview면 없음)
종료 코드: 0 성공, 2 인자 오류, 3 검사 실패, 1 예외. 실패하면 --out에 아무것도 남기지 않는다(임시 폴더에서 만든 뒤 옮김).
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


class ParamError(Exception):
    pass


class _Parser(argparse.ArgumentParser):
    def error(self, message):
        raise ParamError(message)


def parse(argv):
    ap = _Parser(prog="gen_player", add_help=False)
    ap.add_argument("--out", required=True)
    ap.add_argument("--lods", default="0,1,2")
    ap.add_argument("--clips", default="all")
    ap.add_argument("--seed", default="0")
    ap.add_argument("--no-preview", action="store_true")
    a = ap.parse_args(argv)
    try:
        lods = sorted({int(x) for x in a.lods.split(",") if x.strip() != ""})
    except ValueError:
        raise ParamError(f"--lods 형식: {a.lods}")
    if not lods or any(l not in (0, 1, 2) for l in lods):
        raise ParamError(f"--lods는 0,1,2 중에서: {a.lods}")
    try:
        seed = int(a.seed)
    except ValueError:
        raise ParamError(f"--seed는 정수: {a.seed}")
    if seed < 0 or seed > 2 ** 31 - 1:
        raise ParamError(f"--seed 범위 0 ~ 2^31−1: {seed}")
    clips = None if a.clips == "all" else [c for c in a.clips.split(",") if c]
    if clips is not None and not clips:
        raise ParamError("--clips가 비었다")
    return a, lods, clips, seed


def generate(lods, clip_ids, seed, stem_dir, preview, command):
    from player import bake, checks, export, preview as PV, rig, skeleton, textures, uv
    from player import body as B
    from player.clips import all_clips

    blender = export.check_version()
    clips = all_clips()
    if clip_ids is not None:
        known = {c.id for c in clips}
        unknown = [c for c in clip_ids if c not in known]
        if unknown:
            raise ParamError(f"모르는 클립: {', '.join(unknown)}")
        clips = [c for c in clips if c.id in set(clip_ids)]
    rig_obj, meshes = rig.build(lods)
    tags = bake.bake(rig_obj, clips)
    tex = textures.write_all(os.path.join(stem_dir, "Textures"), seed)
    summary = {
        "budgets": checks.budgets({l: m for l, (o, m) in meshes.items()}, tex),
        "extremes": checks.extremes(min(lods)),
        "clips": checks.clip_list(tags) if clip_ids is None else {"ok": True, "count": len(tags), "subset": True},
        "gaitSlide": checks.gait_slide(clips),
        "ground": checks.ground(clips),
    }
    export.write_glb(os.path.join(stem_dir, "player.glb"))
    export.write_json(os.path.join(stem_dir, "player.clips.json"), {"schemaVersion": 1, "fps": 30, "clips": tags})
    layout = {
        "schemaVersion": 1,
        "generator": {"blender": blender, "script": "gen_player.py", "command": command, "seed": seed},
        "skeleton": {"refHeight": skeleton.REF_HEIGHT, "heightRangeCm": list(checks.HEIGHT_RANGE_CM),
                     "bones": [{"name": n, "parent": p, "head": list(h), "tail": list(t)} for n, p, h, t in skeleton.BONES]},
        "uv": uv.layout(),
        "shapes": list(B.SHAPES) + list(B.HAIR_STYLES),
        "materials": list(rig.MATERIALS),
        "textures": {k: list(v) for k, v in sorted(tex.items())},
        "summary": summary,
    }
    export.write_json(os.path.join(stem_dir, "player.layout.json"), layout)
    if preview:
        objs = {l: o for l, (o, m) in meshes.items()}
        PV.lineup(rig_obj, objs, os.path.join(stem_dir, "player.preview-lineup.png"), stem_dir)
        PV.heads(rig_obj, objs[min(objs)], os.path.join(stem_dir, "player.preview-heads.png"), stem_dir)
        ids = [i for i in PV.POSE_IDS if i in {t["id"] for t in tags}]
        if ids:
            PV.poses(rig_obj, objs[min(objs)], tags, ids, os.path.join(stem_dir, "player.preview-poses.png"), stem_dir)
    failed = [k for k, v in summary.items() if not v.get("ok")]
    tris = {l: m.triangles for l, (o, m) in meshes.items()}
    print(f"[player] triangles {tris}, clips {len(tags)}, textures {len(tex)}")
    return failed, summary


def main(argv):
    try:
        a, lods, clip_ids, seed = parse(argv)
    except ParamError as e:
        print(f"[player] 인자 오류: {e}", file=sys.stderr)
        return 2
    out = os.path.abspath(a.out)
    tmp = tempfile.mkdtemp(prefix="gen_player-")
    t0 = time.time()
    # 출력 경로는 기록하지 않는다 (같은 인자 → 같은 바이트)
    rest, skip = [], False
    for x in argv:
        if skip:
            skip = False
            continue
        if x == "--out":
            skip = True
            continue
        rest.append(x)
    command = " ".join(["gen_player.py", "--out", "<out>"] + rest)
    try:
        failed, summary = generate(lods, clip_ids, seed, tmp, not a.no_preview, command)
        if failed:
            import json
            print(f"[player] 검사 실패: {failed}", file=sys.stderr)
            for k in failed:
                print(json.dumps(summary[k], ensure_ascii=False, default=str)[:2000], file=sys.stderr)
            return 3
        os.makedirs(out, exist_ok=True)
        for root, _, files in os.walk(tmp):
            for f in sorted(files):
                src = os.path.join(root, f)
                dst = os.path.join(out, os.path.relpath(src, tmp))
                os.makedirs(os.path.dirname(dst), exist_ok=True)
                shutil.copyfile(src, dst)
        print(f"[player] 완료 {time.time() - t0:.1f} s → {out}")
        return 0
    except ParamError as e:
        print(f"[player] 인자 오류: {e}", file=sys.stderr)
        return 2
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


if __name__ == "__main__":
    code = main(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    sys.stdout.flush()
    sys.stderr.flush()
    os._exit(code)
