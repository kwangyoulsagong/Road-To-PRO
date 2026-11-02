"""리포트 HTML 공통: 이스케이프와 스타일 토큰 (게임 UI 톤 — 회색 바탕, 흰 카드, 블루 포인트 하나)."""
from html import escape

CSS = """
:root{--bg:#f2f3f5;--card:#fff;--text:#191f28;--muted:#6b7684;--line:#e5e8eb;
--accent:#3182f6;--pass:#12a150;--fail:#e5383b;--warn:#f59f00;--cand:#8b5cf6;--code:#f7f8fa;color-scheme:light}
@media (prefers-color-scheme:dark){:root:not([data-theme="light"]){--bg:#101215;--card:#1b1e23;
--text:#e8eaed;--muted:#9aa0a6;--line:#2c3036;--code:#15181c;color-scheme:dark}}
:root[data-theme="dark"]{--bg:#101215;--card:#1b1e23;--text:#e8eaed;--muted:#9aa0a6;--line:#2c3036;--code:#15181c;color-scheme:dark}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--text);
font:15px/1.5 -apple-system,BlinkMacSystemFont,"Apple SD Gothic Neo","Noto Sans KR",sans-serif}
main{max-width:1100px;margin:0 auto;padding-block:20px 48px;padding-left:max(16px,env(safe-area-inset-left,0px));padding-right:max(16px,env(safe-area-inset-right,0px))}
h1{font-size:22px;margin:4px 0 12px}h2{font-size:17px;margin:0 0 10px}
.card{background:var(--card);border-radius:16px;padding:16px;margin:0 0 12px;overflow-wrap:anywhere}
.meta{color:var(--muted);font-size:13px;display:flex;flex-wrap:wrap;gap:4px 14px}
.meta b{color:var(--text);font-weight:600}
.verdict{display:inline-block;font-weight:700;font-size:15px;padding:4px 12px;border-radius:999px;color:#fff}
.verdict.pass{background:var(--pass)}.verdict.fail{background:var(--fail)}
.big{font-size:34px;font-weight:700;line-height:1.1}
.stats{display:flex;flex-wrap:wrap;gap:8px 24px;margin-top:10px}
.stats div{min-width:72px}.stats span{display:block;color:var(--muted);font-size:12px}
.tag{display:inline-block;font-size:12px;font-weight:600;padding:1px 8px;border-radius:6px;color:#fff}
.tag.pass{background:var(--pass)}.tag.fail{background:var(--fail)}.tag.skip{background:var(--muted)}
.tag.cand{background:var(--cand)}.tag.warn{background:var(--warn)}.tag.info{background:var(--accent)}
.warnbar{background:var(--warn);color:#000;border-radius:12px;padding:10px 14px;margin:0 0 12px;font-weight:600}
pre{background:var(--code);border-radius:10px;padding:10px;font:12px/1.45 ui-monospace,Menlo,monospace;
overflow-x:auto;white-space:pre;margin:6px 0 0}
.scroll{overflow-x:auto}
table{border-collapse:collapse;width:100%;font-size:13px}
th,td{text-align:left;padding:6px 8px;border-bottom:1px solid var(--line);vertical-align:top}
th{color:var(--muted);font-weight:600}
.case{padding:10px 0;border-bottom:1px solid var(--line)}.case:last-child{border-bottom:0}
.case .name{font-weight:600}.muted{color:var(--muted)}
a{color:var(--accent)}
"""


def e(v):
    return escape("" if v is None else str(v), quote=True)


def page(title, body, extra_css="", extra_head=""):
    return (
        "<!doctype html><html lang=\"ko\"><head><meta charset=\"utf-8\">"
        "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">"
        f"<title>{e(title)}</title><meta name=\"claude:viewport-fit-sides\" content=\"cover\"><style>{CSS}{extra_css}</style>{extra_head}</head>"
        f"<body><main>{body}</main></body></html>"
    )
