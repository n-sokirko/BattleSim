"""Постобработка нарезки: курсоры без серой плашки и красной точки (точка = остриё), кольца и эффекты без остатков шахматки."""
import json, numpy as np, os
from PIL import Image
from scipy import ndimage as ndi
D = "godot/assets/ui/gen"
hot = {}
ARROWS = {"c_arrow", "c_friend", "c_enemy"}
for f in sorted(os.listdir(D)):
    if not f.endswith(".png"): continue
    n = f[:-4]
    p = f"{D}/{f}"
    im = Image.open(p).convert("RGBA")
    a = np.array(im).astype(int)
    if n.startswith("sel_") or n.startswith("fx_"):
        mn = a[..., :3].min(2); mx = a[..., :3].max(2)
        junk = (mn >= 200) & (mx - mn <= 12)
        a[junk, 3] = 0
        Image.fromarray(a.astype(np.uint8)).save(p)
    elif n.startswith("c_"):
        rgb = a[..., :3]
        mx = rgb.max(2); mn = rgb.min(2)
        sat = (mx - mn) / np.maximum(mx, 1)
        plate = (sat < 0.45) & (mx < 100) | (a[..., 3] < 40)
        lab, k = ndi.label(plate)
        edge = set(np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]]))) - {0}
        bg = np.isin(lab, list(edge))
        bg = ndi.binary_dilation(bg, iterations=1)
        # красная точка-остриё в левом верхнем углу плашки
        red = (a[..., 0] > 200) & (a[..., 1] < 90) & (a[..., 2] < 90)
        sub = np.zeros_like(red); sub[:34, :34] = red[:34, :34]
        ys, xs = np.nonzero(sub)
        dot = None
        if len(xs) > 15:
            dot = (xs.mean(), ys.mean())
            yy, xx = np.mgrid[:a.shape[0], :a.shape[1]]
            bg |= (xx - dot[0]) ** 2 + (yy - dot[1]) ** 2 < 8.5 ** 2
        a[bg, 3] = 0
        ys, xs = np.nonzero(a[..., 3] > 30)
        x0, x1, y0, y1 = xs.min(), xs.max() + 1, ys.min(), ys.max() + 1
        if n in ARROWS and dot is not None:
            hx, hy = dot[0] - x0, dot[1] - y0
            # остриё чуть ниже и правее точки: берём ближайший непрозрачный пиксель к точке
            d2 = (xs - dot[0]) ** 2 + (ys - dot[1]) ** 2
            i = d2.argmin(); hx, hy = xs[i] - x0, ys[i] - y0
        else:
            hx, hy = (x1 - x0) / 2, (y1 - y0) / 2
        crop = Image.fromarray(a.astype(np.uint8)).crop((x0, y0, x1, y1))
        sc = 44 / max(crop.size)
        crop = crop.resize((max(1, round(crop.width * sc)), max(1, round(crop.height * sc))), Image.LANCZOS)
        crop.save(p)
        hot[n] = [round(hx * sc), round(hy * sc)]
json.dump(hot, open(f"{D}/cursor_hotspots.json", "w"))
print(hot)
