"""기기 프로필 로드·검증과 매트릭스 전개 (specs/000 FR-10~13)."""
import json
from dataclasses import dataclass

from . import paths
from .errors import RunnerError

QUALITIES = ("Low", "Medium", "High")
TIERS = ("smoke", "full")
THEMES = ("light", "dark")
LANGS = ("ko", "en")

_FIELDS = {
    "id": str,
    "description": str,
    "width": int,
    "height": int,
    "uiScale": (int, float),
    "aspect": str,
    "quality": str,
    "renderScale": (int, float),
    "targetFps": int,
    "batterySaver": bool,
    "isReferenceDevice": bool,
    "tier": str,
}


@dataclass(frozen=True)
class Profile:
    id: str
    description: str
    width: int
    height: int
    ui_scale: float
    aspect: str
    quality: str
    render_scale: float
    target_fps: int
    battery_saver: bool
    is_reference_device: bool
    tier: str


@dataclass(frozen=True)
class Combo:
    """프로필 × 테마 × 언어 한 칸."""

    profile: Profile
    theme: str
    lang: str

    @property
    def key(self):
        return f"{self.profile.id}-{self.theme}-{self.lang}"


class ProfileSet:
    def __init__(self, profiles, quality_map):
        self.profiles = profiles
        self.quality_map = quality_map
        self._by_id = {p.id: p for p in profiles}

    def get(self, pid):
        if pid not in self._by_id:
            raise RunnerError(f"알 수 없는 프로필 id: {pid}")
        return self._by_id[pid]

    def unity_quality_level(self, quality):
        return self.quality_map[quality]

    def expand(self, run_tier, selector="all", only=None, langs=None, themes=None):
        """실행 등급(smoke/full) + 시나리오 대상(all/smoke/[ids]) + CLI 필터 → 조합 목록.

        smoke 실행: smoke 등급 프로필 × (light, ko). full 실행: 모든 프로필 × 테마 2 × 언어 2.
        """
        if run_tier not in TIERS:
            raise RunnerError(f"알 수 없는 실행 등급: {run_tier}")
        chosen = list(self.profiles)
        if run_tier == "smoke":
            chosen = [p for p in chosen if p.tier == "smoke"]

        if selector == "smoke":
            chosen = [p for p in chosen if p.tier == "smoke"]
        elif isinstance(selector, list):
            for pid in selector:
                self.get(pid)
            chosen = [p for p in chosen if p.id in selector]
        elif selector != "all":
            raise RunnerError(f"시나리오 대상 프로필 지정이 잘못됨: {selector!r}")

        if only:
            for pid in only:
                self.get(pid)
            # CLI로 직접 지정한 프로필은 등급과 무관하게 돈다.
            chosen = [p for p in self.profiles if p.id in only]

        if run_tier == "smoke":
            axis = [("light", "ko")]
        else:
            axis = [(t, l) for l in LANGS for t in THEMES]
        if langs:
            axis = [(t, l) for (t, _) in axis for l in langs]
            axis = list(dict.fromkeys(axis))
        if themes:
            axis = [(t, l) for (_, l) in axis for t in themes]
            axis = list(dict.fromkeys(axis))
        return [Combo(p, t, l) for p in chosen for (t, l) in axis]


def _check(raw, where):
    for name, typ in _FIELDS.items():
        if name not in raw:
            raise RunnerError(f"프로필 {where}: 필드 '{name}' 없음")
        v = raw[name]
        if isinstance(v, bool) and typ is not bool:
            raise RunnerError(f"프로필 {where}: '{name}' 형식이 잘못됨")
        if not isinstance(v, typ):
            raise RunnerError(f"프로필 {where}: '{name}' 형식이 잘못됨 ({v!r})")
    if raw["quality"] not in QUALITIES:
        raise RunnerError(f"프로필 {where}: quality는 {QUALITIES} 중 하나 ({raw['quality']!r})")
    if raw["tier"] not in TIERS:
        raise RunnerError(f"프로필 {where}: tier는 {TIERS} 중 하나 ({raw['tier']!r})")
    if not 0 < raw["renderScale"] <= 2:
        raise RunnerError(f"프로필 {where}: renderScale 범위 (0, 2] 밖 ({raw['renderScale']})")
    if raw["width"] <= 0 or raw["height"] <= 0 or raw["uiScale"] <= 0 or raw["targetFps"] <= 0:
        raise RunnerError(f"프로필 {where}: 해상도·배율·목표 fps는 양수")


def load(path=None):
    path = path or paths.PROFILES_JSON
    try:
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
    except (OSError, json.JSONDecodeError) as e:
        raise RunnerError(f"프로필 파일을 읽을 수 없다: {path}: {e}") from e

    if data.get("schemaVersion") != 1:
        raise RunnerError(f"프로필 schemaVersion 1만 지원 ({data.get('schemaVersion')!r})")
    qmap = data.get("qualityMap") or {}
    for q in QUALITIES:
        if not isinstance(qmap.get(q), str):
            raise RunnerError(f"qualityMap에 '{q}' 매핑 없음")

    seen = set()
    out = []
    for i, raw in enumerate(data.get("profiles") or []):
        where = raw.get("id", f"#{i}")
        _check(raw, where)
        if raw["id"] in seen:
            raise RunnerError(f"프로필 id 중복: {raw['id']}")
        seen.add(raw["id"])
        out.append(
            Profile(
                id=raw["id"],
                description=raw["description"],
                width=raw["width"],
                height=raw["height"],
                ui_scale=float(raw["uiScale"]),
                aspect=raw["aspect"],
                quality=raw["quality"],
                render_scale=float(raw["renderScale"]),
                target_fps=raw["targetFps"],
                battery_saver=raw["batterySaver"],
                is_reference_device=raw["isReferenceDevice"],
                tier=raw["tier"],
            )
        )
    if not out:
        raise RunnerError("프로필이 하나도 없다")
    return ProfileSet(out, qmap)
