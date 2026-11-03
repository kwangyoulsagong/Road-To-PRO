"""glb·JSON 내보내기 (specs/044 FR-11·20·65). 옵션을 전부 명시해 기본값 변화에 흔들리지 않게 한다."""
import json
import math
import os

import bpy

REFERENCE_BLENDER = "5.1.1"


def blender_version():
    return ".".join(str(x) for x in bpy.app.version)


def check_version():
    v = blender_version()
    if v != REFERENCE_BLENDER:
        print(f"[stadium] 경고: Blender {v} — 기준 {REFERENCE_BLENDER}와 달라 결과 해시가 다를 수 있다")
    return v


def write_glb(path):
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=path,
        export_format="GLB",
        export_yup=True,
        export_apply=True,
        export_texcoords=True,
        export_normals=True,
        export_tangents=False,
        export_materials="EXPORT",
        export_image_format="NONE",
        export_cameras=False,
        export_lights=False,
        export_extras=False,
        export_animations=False,
        export_skins=False,
        export_morph=False,
        use_selection=False,
        use_visible=False,
        use_renderable=False,
        export_copyright="",
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
