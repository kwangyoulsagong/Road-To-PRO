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


def strip_animation_channels(path, node_names):
    """062: glb에서 지정 노드(손가락 뼈)를 가리키는 애니메이션 채널·샘플러를 지우고, 안 쓰게 된 accessor·bufferView를
    빼서 버퍼를 다시 묶는다. export_force_sampling은 키 없는 뼈까지 매 프레임 샘플링해 클립 메모리가 2배가 된다(T11 측정).
    결정론: 입력 순서 그대로 복사."""
    import struct
    with open(path, "rb") as f:
        data = f.read()
    ln, _ = struct.unpack_from("<II", data, 12)
    js = json.loads(data[20:20 + ln])
    off = 20 + ln
    bln, _ = struct.unpack_from("<II", data, off)
    binbuf = data[off + 8:off + 8 + bln]
    drop_nodes = {i for i, n in enumerate(js["nodes"]) if n.get("name") in set(node_names)}
    for anim in js.get("animations", []):
        keep_ch = [c for c in anim["channels"] if c["target"].get("node") not in drop_nodes]
        used = sorted({c["sampler"] for c in keep_ch})
        remap = {o: i for i, o in enumerate(used)}
        anim["samplers"] = [anim["samplers"][o] for o in used]
        for c in keep_ch:
            c["sampler"] = remap[c["sampler"]]
        anim["channels"] = keep_ch
    # 쓰이는 accessor 모으기 (메시·스킨·애니메이션)
    used_acc = set()
    for m in js.get("meshes", []):
        for pr in m["primitives"]:
            used_acc.update(pr["attributes"].values())
            if "indices" in pr:
                used_acc.add(pr["indices"])
            for t in pr.get("targets", []):
                used_acc.update(t.values())
    for sk in js.get("skins", []):
        if "inverseBindMatrices" in sk:
            used_acc.add(sk["inverseBindMatrices"])
    for anim in js.get("animations", []):
        for smp in anim["samplers"]:
            used_acc.update((smp["input"], smp["output"]))
    acc_order = sorted(used_acc)
    acc_map = {o: i for i, o in enumerate(acc_order)}
    bvs = set()
    for a in acc_order:
        ac = js["accessors"][a]
        if "bufferView" in ac:
            bvs.add(ac["bufferView"])
        if "sparse" in ac:  # 모프 타깃은 sparse — indices·values가 bufferView를 따로 가리킨다
            bvs.add(ac["sparse"]["indices"]["bufferView"])
            bvs.add(ac["sparse"]["values"]["bufferView"])
    used_bv = sorted(bvs)
    bv_map = {o: i for i, o in enumerate(used_bv)}
    new_bin = bytearray()
    new_bv = []
    for o in used_bv:
        bv = dict(js["bufferViews"][o])
        start = bv.get("byteOffset", 0)
        chunk = binbuf[start:start + bv["byteLength"]]
        while len(new_bin) % 4:
            new_bin += b"\x00"
        bv["byteOffset"] = len(new_bin)
        new_bin += chunk
        new_bv.append(bv)
    while len(new_bin) % 4:
        new_bin += b"\x00"
    accs = []
    for o in acc_order:
        a = dict(js["accessors"][o])
        if "bufferView" in a:
            a["bufferView"] = bv_map[a["bufferView"]]
        if "sparse" in a:
            sp = json.loads(json.dumps(a["sparse"]))
            sp["indices"]["bufferView"] = bv_map[sp["indices"]["bufferView"]]
            sp["values"]["bufferView"] = bv_map[sp["values"]["bufferView"]]
            a["sparse"] = sp
        accs.append(a)
    js["accessors"] = accs
    js["bufferViews"] = new_bv
    js["buffers"][0]["byteLength"] = len(new_bin)
    for m in js.get("meshes", []):
        for pr in m["primitives"]:
            pr["attributes"] = {k: acc_map[v] for k, v in pr["attributes"].items()}
            if "indices" in pr:
                pr["indices"] = acc_map[pr["indices"]]
            if "targets" in pr:
                pr["targets"] = [{k: acc_map[v] for k, v in t.items()} for t in pr["targets"]]
    for sk in js.get("skins", []):
        if "inverseBindMatrices" in sk:
            sk["inverseBindMatrices"] = acc_map[sk["inverseBindMatrices"]]
    for anim in js.get("animations", []):
        for smp in anim["samplers"]:
            smp["input"], smp["output"] = acc_map[smp["input"]], acc_map[smp["output"]]
    jb = json.dumps(js, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
    while len(jb) % 4:
        jb += b" "
    out = struct.pack("<III", 0x46546C67, 2, 12 + 8 + len(jb) + 8 + len(new_bin))
    out += struct.pack("<II", len(jb), 0x4E4F534A) + jb + struct.pack("<II", len(new_bin), 0x004E4942) + bytes(new_bin)
    with open(path, "wb") as f:
        f.write(out)


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
