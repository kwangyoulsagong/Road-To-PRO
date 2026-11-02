#!/usr/bin/env python3
"""스펙 린터. 사용: python3 -I .claude/scripts/spec-lint.py [NNN ...]
specs/NNN-*/spec.md 가 SDD 규칙(.claude/rules/05-sdd.md)과 템플릿을 지키는지 검사한다.
오류가 하나라도 있으면 종료 코드 1.
"""
import re, sys, pathlib

ROOT = pathlib.Path(__file__).resolve().parents[2]
SPECS = ROOT / "specs"
REQUIRED = ["## 배경", "## 범위", "## 요구사항", "## 성능 예산", "## 수용 기준", "## 검증 · 증거", "## 미정 / 질문"]
TAGS = {"로직", "E2E", "성능", "결정론"}
STATES = {"초안", "승인됨", "완료"}
# 구현 방법이 스펙에 새면 안 된다 (플랜에서 정함). 기능 이름으로 쓰라는 규칙.
IMPL_WORDS = [r"\bMonoBehaviour\b", r"\bScriptableObject\b", r"\bCinemachine\b", r"\bFMOD\b", r"\bYarn Spinner\b",
              r"\bBurst\b", r"\bJob System\b", r"\bclass [A-Z]\w+", r"\bpublic (?:void|int|float)\b"]
# 0번 문서 정책: 실존 인물 이름 금지. 대표적인 것만 막는다 (완전하지 않다).
REAL_NAMES = [r"\bMessi\b|리오넬|호날두|Ronaldo|손흥민|Son Heung|음바페|Mbapp|홀란|Haaland|과르디올라|Guardiola|클롭|Klopp|무리뉴|Mourinho"]
PROFILES = {"low-igpu-1080p", "mac-m1-1600p", "compact-1366", "handheld-800p", "ultrawide-1080",
            "desktop-1440p", "uhd-4k-200", "tablet-4x3", "phone-portrait", "phone-landscape"}

def lint(path: pathlib.Path):
    errs, warns = [], []
    t = path.read_text(encoding="utf-8")
    nnn = path.parent.name[:3]
    first = t.splitlines()[0] if t else ""
    if not first.startswith(f"# {nnn}"):
        errs.append(f"제목이 '# {nnn} — ...' 형식이 아님: {first[:60]}")
    m = re.search(r"^상태:\s*(\S+)", t, re.M)
    if not m or m.group(1) not in STATES:
        errs.append("상태: 줄 없음 또는 값이 초안/승인됨/완료가 아님")
    if not re.search(r"^기획 출처:", t, re.M):
        errs.append("기획 출처: 줄 없음")
    for h in REQUIRED:
        if h not in t:
            errs.append(f"필수 섹션 없음: {h}")
    if "경계" not in t:
        errs.append("'경계 · 예외 상황' 소절 없음")
    sec = re.search(r"^## 수용 기준\s*$(.*?)(?=^## |\Z)", t, re.M | re.S)
    acs = re.findall(r"^- (AC-\d+)\s*((?:\[[^\]]+\]\s*)*):?", sec.group(1) if sec else "", re.M)
    if not acs:
        errs.append("수용 기준 AC 없음")
    ids = [a for a, _ in acs]
    dup = {a for a in ids if ids.count(a) > 1}
    if dup:
        errs.append(f"AC 번호 중복: {sorted(dup)}")
    kinds = set()
    for a, tags in acs:
        got = set(re.findall(r"\[([^\]]+)\]", tags))
        if not got:
            errs.append(f"{a}: 검증 태그 없음")
        bad = got - TAGS
        if bad:
            errs.append(f"{a}: 알 수 없는 태그 {sorted(bad)}")
        kinds |= got & TAGS
    if "성능" not in kinds:
        warns.append("[성능] AC 없음")
    e2e = re.findall(r"`?e2e/([^\s`,)]+)", t)
    for e in e2e:
        if not re.match(r"^\d{3}-", e):
            errs.append(f"E2E id가 e2e/NNN-... 형식이 아님: e2e/{e}")
    own = [e for e in e2e if e.startswith(nnn + "-")]
    if "E2E" in kinds and not own:
        errs.append(f"[E2E] AC가 있는데 자기 E2E 시나리오 id(e2e/{nnn}-...)가 없음")
    for p in set(re.findall(r"`([a-z0-9]+(?:-[a-z0-9]+)+)`", t)):
        if re.match(r"^(low|mac|compact|handheld|ultrawide|desktop|uhd|tablet|phone)-", p) and p not in PROFILES:
            errs.append(f"000에 없는 프로필 id: {p}")
    for w in IMPL_WORDS:
        for mm in re.finditer(w, t):
            line = t[:mm.start()].count("\n") + 1
            warns.append(f"구현 세부가 스펙에 있음(플랜으로): '{mm.group(0)}' (줄 {line})")
    for w in REAL_NAMES:
        for mm in re.finditer(w, t):
            line = t[:mm.start()].count("\n") + 1
            errs.append(f"실존 인물 이름 의심: '{mm.group(0)}' (줄 {line})")
    for ref in set(re.findall(r"(?<![\d.])(0\d\d)(?=\s*(?:이|가|의|에서|이 제공|참조|스펙|\)|,|·))", t)):
        if ref != nnn and not list(SPECS.glob(f"{ref}-*")):
            warns.append(f"참조한 스펙 {ref} 폴더가 아직 없음")
    return errs, warns, len(ids), sorted(kinds)

def main():
    want = set(sys.argv[1:])
    files = sorted(SPECS.glob("[0-9][0-9][0-9]-*/spec.md"))
    if want:
        files = [f for f in files if f.parent.name[:3] in want]
    total_err = 0
    for f in files:
        errs, warns, n, kinds = lint(f)
        mark = "FAIL" if errs else "ok  "
        print(f"{mark} {f.parent.name}  AC {n}  [{', '.join(kinds)}]")
        for e in errs: print(f"     ✗ {e}")
        for w in warns[:6]: print(f"     · {w}")
        if len(warns) > 6: print(f"     · …경고 {len(warns)-6}개 더")
        total_err += len(errs)
    readme = (SPECS / "README.md").read_text(encoding="utf-8")
    listed = set(re.findall(r"\]\((\d{3}-[^/]+)/spec\.md\)", readme))
    present = {f.parent.name for f in SPECS.glob("[0-9][0-9][0-9]-*/spec.md")}
    if not want:
        for x in sorted(listed - present): print(f"FAIL README에 있으나 파일 없음: {x}"); total_err += 1
        for x in sorted(present - listed): print(f"FAIL 파일은 있으나 README에 없음(또는 폴더명 불일치): {x}"); total_err += 1
    print(f"\n스펙 {len(files)}개, 오류 {total_err}")
    sys.exit(1 if total_err else 0)

if __name__ == "__main__":
    main()
