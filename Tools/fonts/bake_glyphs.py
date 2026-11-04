"""키트 번호·이름 글리프 아틀라스 굽기 (specs/043 FR-21·23, T02).

Unity FontEngine에는 CPU 래스터화 공개 API가 없어서(-nographics 테스트·결정론 합성 불가) OFL 폰트를 미리 굽는다.
- names: 공백·라틴(ASCII 출력 가능 문자 + Latin-1 문자) + KS X 1001 한글 2,350자, 셀 높이 SIZE_NAME px
- numbers: 0~9, 셀 높이 SIZE_NUM px (등번호는 크게 보이므로 따로)
출력: <out>/kit-glyphs-names.png, kit-glyphs-numbers.png (8비트 그레이, 흰 글자), kit-glyphs.json
JSON: {"schemaVersion":1,"font":..., "sets":{"names":{"size":..,"ascent":..,"descent":..,"atlas":[w,h],
       "glyphs":{"<코드포인트>":[x,y,w,h,bearingX,bearingY,advance]}}, "numbers":{...}}}
좌표는 아틀라스 왼쪽 위 원점, y 아래로. bearingY = 기준선에서 글리프 위쪽까지.
같은 폰트 → 같은 바이트 (정렬·고정 패킹).

사용: python3 Tools/fonts/bake_glyphs.py --font <otf> --out <dir>
"""
import argparse
import json
import os

from PIL import Image, ImageDraw, ImageFont

SIZE_NAME = 36
SIZE_NUM = 128
PAD = 2


def ksx1001_hangul():
    out = []
    for hi in range(0xB0, 0xC9):
        for lo in range(0xA1, 0xFF):
            try:
                out.append(bytes([hi, lo]).decode("euc-kr"))
            except UnicodeDecodeError:
                pass
    return out


def name_chars():
    chars = [chr(c) for c in range(0x20, 0x7F)]
    chars += [chr(c) for c in range(0xC0, 0x100) if c not in (0xD7, 0xF7)]
    chars += ksx1001_hangul()
    return chars


def bake(font_path, size, chars, width):
    font = ImageFont.truetype(font_path, size)
    ascent, descent = font.getmetrics()
    rects, x, y, row_h = {}, PAD, PAD, 0
    items = []
    for ch in chars:
        l, t, r, b = font.getbbox(ch, anchor="ls")  # 기준선 원점, y 아래로 (+)
        adv = font.getlength(ch)
        w, h = max(0, r - l), max(0, b - t)
        if x + w + PAD > width:
            x, y, row_h = PAD, y + row_h + PAD, 0
        items.append((ch, x, y, w, h, l, -t, adv))
        x += w + PAD
        row_h = max(row_h, h)
    height = y + row_h + PAD
    height = 1 << (height - 1).bit_length()
    img = Image.new("L", (width, height), 0)
    for ch, gx, gy, w, h, l, top, adv in items:
        if w and h:
            g = Image.new("L", (w, h), 0)
            ImageDraw.Draw(g).text((-l, top), ch, font=font, fill=255, anchor="ls")
            img.paste(g, (gx, gy))
        rects[str(ord(ch))] = [gx, gy, w, h, l, top, round(adv, 3)]
    meta = {"size": size, "ascent": ascent, "descent": descent, "atlas": [width, height], "glyphs": rects}
    return img, meta


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--font", required=True)
    ap.add_argument("--out", required=True)
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    names_img, names = bake(a.font, SIZE_NAME, name_chars(), 2048)
    num_img, nums = bake(a.font, SIZE_NUM, [str(d) for d in range(10)], 1024)
    for img in (names_img, num_img):
        assert img.size[0] <= 2048 and img.size[1] <= 2048, img.size
    names_img.save(os.path.join(a.out, "kit-glyphs-names.png"), optimize=False)
    num_img.save(os.path.join(a.out, "kit-glyphs-numbers.png"), optimize=False)
    meta = {"schemaVersion": 1, "font": os.path.basename(a.font), "sets": {"names": names, "numbers": nums}}
    with open(os.path.join(a.out, "kit-glyphs.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(meta, f, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
        f.write("\n")
    print(f"names {names_img.size} {len(names['glyphs'])}자, numbers {num_img.size}")


if __name__ == "__main__":
    main()
