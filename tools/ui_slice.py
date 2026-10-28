"""Режет лист интерфейса из ChatGPT: убирает нарисованную «шахматку» фона, находит элементы и сохраняет их."""
import sys, numpy as np
from PIL import Image
from scipy import ndimage as ndi

src = sys.argv[1]; out = sys.argv[2]
im = np.array(Image.open(src).convert("RGB")).astype(int)
mn = im.min(2); mx = im.max(2)
cand = (mn >= 205) & (mx - mn <= 9)
lab, n = ndi.label(cand)
border = set(np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]]))) - {0}
bg = np.isin(lab, list(border))
# мягкая кромка: светлые пиксели рядом с фоном тоже фон
soft = (mn >= 175) & (mx - mn <= 14)
edge = ndi.binary_dilation(bg, iterations=2) & soft
bg = bg | edge
alpha = np.where(bg, 0, 255).astype(np.uint8)
alpha = (ndi.gaussian_filter(alpha.astype(float), 0.6)).astype(np.uint8)
rgba = np.dstack([im.astype(np.uint8), alpha])
Image.fromarray(rgba).save(out)
# компоненты: слегка раздуваем, чтобы свечения и тени слились с предметом
fg = ndi.binary_dilation(~bg, iterations=5)
lab2, n2 = ndi.label(fg)
objs = ndi.find_objects(lab2)
boxes = []
for k, s in enumerate(objs):
    h = s[0].stop - s[0].start; w = s[1].stop - s[1].start
    if h * w < 400: continue
    boxes.append((s[1].start, s[0].start, s[1].stop, s[0].stop))
boxes.sort(key=lambda b: (b[1] // 60, b[0]))
for i, b in enumerate(boxes):
    print(i, b, b[2] - b[0], b[3] - b[1])
