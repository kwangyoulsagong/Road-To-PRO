"""헤드리스 Chrome: HTML → PNG 스크린샷, 좁은 폭 레이아웃 검사 (specs/000 FR-01·71, AC-14)."""
import html
import os
import re
import subprocess
import tempfile

import numpy as np
from PIL import Image

from . import paths
from .errors import RunnerError


def _run(args, timeout=60):
    if not os.access(paths.CHROME_APP, os.X_OK):
        raise RunnerError(f"Chrome이 없다: {paths.CHROME_APP}")
    with tempfile.TemporaryDirectory() as prof:
        cmd = [paths.CHROME_APP, "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check",
               "--hide-scrollbars", "--allow-file-access-from-files", f"--user-data-dir={prof}"] + args
        try:
            r = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout)
        except subprocess.TimeoutExpired as e:
            raise RunnerError("Chrome 헤드리스가 응답하지 않는다") from e
    return r


def _crop_bottom(png):
    """긴 창으로 찍은 뒤 아래쪽 바탕색만 남은 줄을 잘라낸다."""
    with Image.open(png) as im:
        a = np.asarray(im.convert("RGB"))
    bg = a[-1, -1]
    rows = np.nonzero((np.abs(a.astype(int) - bg).sum(axis=2) > 6).any(axis=1))[0]
    if len(rows) == 0:
        return
    bottom = min(a.shape[0], int(rows.max()) + 24)
    Image.fromarray(a[:bottom]).save(png)


MIN_VIEWPORT = 500  # 헤드리스 Chrome의 최소 창 폭 (실측). 더 좁은 폭은 iframe으로 흉내 낸다.


def _narrow_wrapper(html_path, width, height):
    """같은 폴더에 width px iframe으로 감싼 페이지를 만든다. 검사 결과를 감싼 페이지의 data-layout에 옮긴다."""
    wrap = os.path.join(os.path.dirname(os.path.abspath(html_path)), f".narrow-{width}.html")
    src = os.path.basename(html_path)
    with open(wrap, "w", encoding="utf-8") as f:
        f.write(
            "<!doctype html><html><head><meta charset=utf-8><style>html,body{margin:0;background:#fff}"
            f"iframe{{border:0;width:{width}px;height:{height}px;display:block}}</style></head><body>"
            f'<iframe id=f src="{src}"></iframe><script>'
            "document.getElementById('f').addEventListener('load',function(){var d;"
            "try{d=this.contentDocument.documentElement}catch(e){document.documentElement.setAttribute('data-layout','error:'+e);return}"
            "var cw=this.clientWidth,sw=d.scrollWidth;"
            "document.documentElement.setAttribute('data-layout',sw<=cw+1?'ok':'overflow:'+sw+'>'+cw);});"
            "</script></body></html>"
        )
    return wrap


def screenshot(html_path, png_path, width=1100, height=4000):
    os.makedirs(os.path.dirname(os.path.abspath(png_path)), exist_ok=True)
    target = html_path
    win = width
    if width < MIN_VIEWPORT:
        target = _narrow_wrapper(html_path, width, height)
        win = MIN_VIEWPORT
    try:
        r = _run([f"--screenshot={os.path.abspath(png_path)}", f"--window-size={win},{height}",
                  "--force-device-scale-factor=1", "--virtual-time-budget=3000", "file://" + os.path.abspath(target)])
    finally:
        if target != html_path and os.path.exists(target):
            os.remove(target)
    if not os.path.exists(png_path):
        raise RunnerError(f"Chrome 스크린샷 실패: {r.stderr[-400:]}")
    if win != width:
        with Image.open(png_path) as im:
            im.crop((0, 0, width, im.height)).save(png_path)
    _crop_bottom(png_path)
    return png_path


def layout_check(html_path, width=400, height=900):
    """width px 폭에서 페이지 가로 넘침 검사: 'ok' | 'overflow:<scrollWidth>><폭>'."""
    target = html_path
    win = width
    if width < MIN_VIEWPORT:
        target = _narrow_wrapper(html_path, width, height)
        win = MIN_VIEWPORT
    try:
        r = _run([f"--window-size={win},{height}", "--virtual-time-budget=3000", "--dump-dom",
                  "file://" + os.path.abspath(target)])
    finally:
        if target != html_path and os.path.exists(target):
            os.remove(target)
    m = re.search(r'<html[^>]*data-layout="([^"]+)"', r.stdout)
    if not m:
        raise RunnerError("레이아웃 검사 결과를 읽지 못했다 (data-layout 없음)")
    return html.unescape(m.group(1))
