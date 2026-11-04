"""glb·JSON 내보내기 (specs/043). 옵션을 전부 명시한다. JSON은 키 정렬·소수 6자리 (044 규칙)."""
import json
import math
import os

import bpy

REFERENCE_BLENDER = "5.1.1"


def check_version():
    v = ".".join(str(x) for x in bpy.app.version)
    if v != REFERENCE_BLENDER:
        print(f"[player] 경고: Blender {v} — 기준 {REFERENCE_BLENDER}와 달라 결과 해시가 다를 수 있다")
    return v


def write_glb(path):
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=path, export_format="GLB", export_yup=True, export_apply=False,
        export_texcoords=True, export_normals=True, export_tangents=False,
        export_materials="EXPORT", export_image_format="NONE",
        export_cameras=False, export_lights=False, export_extras=False,
        export_animations=True, export_animation_mode="NLA_TRACKS", export_frame_range=False,
        export_force_sampling=True, export_frame_step=1, export_optimize_animation_size=False,
        export_skins=True, export_all_influences=False, export_def_bones=False,
        export_morph=True, export_morph_normal=False,
        use_selection=False, use_visible=False, use_renderable=False, export_copyright="",
    )


def _norm(v):
    if isinstance(v, float):
        if math.isnan(v) or math.isinf(v):
            raise ValueError("JSON에 NaN/Inf")
        r = round(v, 6)
        return 0.0 if r == 0 else r
    if isinstance(v, dict):
        return {k: _norm(x) for k, x in v.items()}
    if isinstance(v, (list, tuple)):
        return [_norm(x) for x in v]
    return v


def write_json(path, data):
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(_norm(data), f, ensure_ascii=False, sort_keys=True, indent=1)
        f.write("\n")
