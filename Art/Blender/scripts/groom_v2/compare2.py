import sys
from PIL import Image, ImageDraw
out = sys.argv[1]; pairs = [(sys.argv[i], sys.argv[i + 1]) for i in range(2, len(sys.argv), 2)]
h = 620
cols = []
for ref, ren in pairs:
    r = Image.open(ref).convert('RGB'); r = r.resize((int(r.width * h / r.height), h))
    m = Image.open(ren).convert('RGB').crop((40, 20, 560, 680)); m = m.resize((int(m.width * h / m.height), h))
    cols += [("참고", r), ("우리", m)]
W = Image.new('RGB', (sum(t.width for _, t in cols) + 12 * len(cols), h + 24), (30, 30, 30))
x = 0
for name, t in cols:
    W.paste(t, (x, 24)); ImageDraw.Draw(W).text((x + 4, 5), name, fill=(255, 255, 255)); x += t.width + 12
W.save(out)
