"""MakeHuman CC0 에셋 (specs/088 FR-01·02·05): 피부 diffuse 3세트, 고해상도 눈 + 눈 재질, 눈썹·속눈썹 프록시.

원본: `Art/ThirdParty/MakeHuman/` (assets.json = 파일·sha256·라이선스). MPFB 서비스를 쓰지 않고 mhclo를 직접 맞춘다 —
프록시 정점 = Σ wᵢ · 베이스[refᵢ] + 오프셋 × 축 배율이라 **베이스 정점에 선형**이고, 그래서 체형·얼굴·표정 델타가 같은 식으로
그대로 옮겨진다(델타는 오프셋 항 없이 가중 합).

좌표: MakeHuman obj (x, y 위, z 정면) → Blender (x, −z, y) → 선수 (−x, y, z). 축 배율은 선수 좌표 기준 정점 쌍 거리 / 분모.
"""
import hashlib
import json
import os

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", "..", "..", ".."))
ROOT = os.path.join(REPO, "Art", "ThirdParty", "MakeHuman")
ASSETS_JSON = os.path.join(ROOT, "assets.json")

# 088 Q4: 피부 세트 (키 → 원본). 지역·톤은 PlayerLook 톤 표가 세트 + 틴트로 정한다.
SKINS = {
    "light": "skins/young_caucasian_male/young_caucasian_male.mhmat",
    "asian": "skins/young_asian_male/young_asian_male.mhmat",
    "dark": "skins/young_african_male/young_african_male.mhmat",
}
EYES = "eyes/high-poly/high-poly.mhclo"
EYE_MATERIAL = "eyes/materials/brown.mhmat"     # 홍채 색은 셰이더가 G(홍채 마스크)로 칠한다 → 밝기만 쓴다
EYEBROWS = ("eyebrows/mindfront_eyebrows_02/mindfront_eyebrows_02.mhclo",
            "eyebrows/mindfront_eyebrows_04/mindfront_eyebrows_04.mhclo",
            "eyebrows/mindfront_eyebrows_09/mindfront_eyebrows_09.mhclo")
EYELASHES = "eyelashes/mindfront_eyelashes_02/mindfront_eyelashes_02.mhclo"

_USED = set()


def path(rel):
    p = os.path.join(ROOT, rel)
    if not os.path.exists(p):
        raise FileNotFoundError(f"MakeHuman 에셋이 없다: {p} — specs/088 T02 (Art/ThirdParty/MakeHuman)")
    _USED.add(os.path.relpath(p, ROOT))
    return p


def used_files():
    out = []
    for rel in sorted(_USED):
        with open(os.path.join(ROOT, rel), "rb") as f:
            out.append({"path": rel, "sha256": hashlib.sha256(f.read()).hexdigest()})
    return out


def licenses():
    """쓴 파일이 assets.json에 같은 sha256·CC0로 있는지 (088 AC-02)."""
    with open(ASSETS_JSON, encoding="utf-8") as f:
        pinned = {a["path"]: a for a in json.load(f)["files"]}
    bad = []
    for a in used_files():
        p = pinned.get(a["path"])
        if p is None or p["sha256"] != a["sha256"] or p.get("license") != "CC0":
            bad.append(a["path"])
    csv = open(os.path.join(REPO, "Art", "LICENSES.csv"), encoding="utf-8").read().splitlines()
    rows = [r for r in csv if r.startswith("MakeHuman")]
    return {"ok": not bad and bool(_USED) and bool(rows) and all("CC0" in r for r in rows), "bad": bad,
            "files": len(_USED), "csvRows": len(rows)}


# ---------------------------------------------------------------- 파서

def read_mhmat(rel):
    out = {}
    base = os.path.dirname(rel)
    for line in open(path(rel), encoding="utf-8", errors="ignore"):
        line = line.strip()
        if not line or line.startswith(("#", "//")):
            continue
        k, _, v = line.partition(" ")
        out[k] = v.strip()
    if "diffuseTexture" in out:
        out["diffusePath"] = path(os.path.join(base, out["diffuseTexture"]))
    return out


def read_obj(p):
    V, UV, F, FT = [], [], [], []
    for line in open(p, encoding="utf-8", errors="ignore"):
        if line.startswith("v "):
            V.append([float(x) for x in line.split()[1:4]])
        elif line.startswith("vt "):
            UV.append([float(x) for x in line.split()[1:3]])
        elif line.startswith("f "):
            f, ft = [], []
            for tok in line.split()[1:]:
                parts = tok.split("/")
                f.append(int(parts[0]) - 1)
                ft.append(int(parts[1]) - 1 if len(parts) > 1 and parts[1] else -1)
            F.append(f)
            FT.append(ft)
    return np.array(V), np.array(UV) if UV else np.zeros((0, 2)), F, FT


class Proxy:
    """맞춘 프록시: refs (N,3) 정수, w (N,3), off (N,3, MH 좌표), 축 배율 정점 쌍·분모, obj 면·UV."""

    def __init__(self, rel):
        self.rel = rel
        base = os.path.dirname(rel)
        refs, w, off, scales = [], [], [], {}
        obj = None
        in_verts = False
        for line in open(path(rel), encoding="utf-8", errors="ignore"):
            s = line.split()
            if not s or s[0].startswith("#"):
                continue
            if in_verts:
                if len(s) == 9:
                    refs.append([int(x) for x in s[:3]])
                    w.append([float(x) for x in s[3:6]])
                    off.append([float(x) for x in s[6:9]])
                    continue
                if len(s) == 1 and s[0].lstrip("-").isdigit():
                    refs.append([int(s[0])] * 3)
                    w.append([1.0, 0.0, 0.0])
                    off.append([0.0, 0.0, 0.0])
                    continue
                in_verts = False
            if s[0] == "obj_file":
                obj = s[1]
            elif s[0] in ("x_scale", "y_scale", "z_scale"):
                scales[s[0][0]] = (int(s[1]), int(s[2]), float(s[3]))
            elif s[0] == "verts":
                in_verts = True
        self.refs = np.array(refs, dtype=np.int64)
        self.w = np.array(w)
        self.off = np.array(off)
        self.scales = scales
        V, UV, F, FT = read_obj(path(os.path.join(base, obj)))
        assert len(V) == len(self.refs), f"{rel}: obj 정점 {len(V)} ≠ mhclo {len(self.refs)}"
        self.faces, self.face_uv_idx, self.uv = F, FT, UV

    def fit(self, verts):
        """베이스 정점 (선수 좌표) → 프록시 정점 (선수 좌표)."""
        P = (verts[self.refs] * self.w[..., None]).sum(axis=1)
        sc = np.ones(3)
        for ax, i in (("x", 0), ("y", 1), ("z", 2)):
            if ax in self.scales:
                a, b, den = self.scales[ax]
                sc[i] = abs(verts[a, i] - verts[b, i]) / den
        off = self.off * np.array([-1.0, 1.0, 1.0])       # MH x → 선수 −X
        return P + off * sc

    def delta(self, d):
        """베이스 델타 (V,3) → 프록시 델타 (오프셋 배율 변화는 무시 — 머리 크기가 거의 같은 셰이프 키)."""
        return (d[self.refs] * self.w[..., None]).sum(axis=1)

    def triangles(self):
        out, uvs = [], []
        for f, ft in zip(self.faces, self.face_uv_idx):
            for k in range(1, len(f) - 1):
                out.append((f[0], f[k], f[k + 1]))
                uvs.append((ft[0], ft[k], ft[k + 1]))
        return np.array(out, dtype=np.int64), np.array(uvs, dtype=np.int64)


def load_png(p):
    """PNG → float (H,W,C) 0~1. Blender 이미지 API로 읽는다(외부 의존 없음)."""
    import bpy
    img = bpy.data.images.load(p, check_existing=False)
    img.colorspace_settings.name = "Non-Color"
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    bpy.data.images.remove(img)
    return px.reshape(h, w, 4)[::-1].astype(np.float64)   # 행 0 = 위


def resize(img, size):
    """상자 평균 축소 (정수 배) 후 최근접 — 원본은 2의 거듭제곱(4096·2048)."""
    h, w = img.shape[:2]
    if h == size and w == size:
        return img
    if h % size == 0 and w % size == 0:
        fy, fx = h // size, w // size
        return img.reshape(size, fy, size, fx, -1).mean(axis=(1, 3))
    ys = (np.arange(size) * h / size).astype(int)
    xs = (np.arange(size) * w / size).astype(int)
    return img[ys][:, xs]
