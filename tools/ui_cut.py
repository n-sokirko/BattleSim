"""Режет лист (после ui_slice.py) на файлы в godot/assets/ui/gen."""
import numpy as np, os
from PIL import Image
from scipy import ndimage as ndi
im = Image.open("art/generated/ui/sheet_rgba.png")
a = np.array(im)[..., 3] > 128
lab, n = ndi.label(a)
boxes = []
for s in ndi.find_objects(lab):
    h = s[0].stop - s[0].start; w = s[1].stop - s[1].start
    if h * w >= 900:
        boxes.append((s[1].start, s[0].start, s[1].stop, s[0].stop))
out = "godot/assets/ui/gen"
os.makedirs(out, exist_ok=True)

def grid(x0, x1, y0, y1, names):
    cells = [b for b in boxes if x0 <= b[0] < x1 and y0 <= b[1] < y1]
    cells.sort(key=lambda b: (round(b[1] / 100), b[0]))
    assert len(cells) == len(names), (len(cells), len(names))
    for b, nm in zip(cells, names):
        im.crop(b).save(f"{out}/{nm}.png")

grid(0, 500, 430, 870, ["move", "stop", "hold", "attack", "ab_cry", "ab_slam", "ab_taunt", "ab_ward",
                         "poise", "stagger", "combo", "guard", "damage", "speed", "health", "level"])
grid(500, 1000, 430, 870, ["boss", "camp", "chest", "scroll", "potion_hp", "potion_mp", "sword", "shield",
                            "bow", "boots", "amulet", "key", "pause", "menu", "focus", "group"])
grid(1000, 1540, 430, 830, ["c_arrow", "c_friend", "c_enemy", "c_attackmove", "c_move", "c_pickup", "c_blocked", "c_hold",
                             "c_ability", "c_area", "c_box", "c_busy"])
def pick(name, x, y):
    for b in boxes:
        if b[0] <= x <= b[2] and b[1] <= y <= b[3]:
            im.crop(b).save(f"{out}/{name}.png"); return
    print("нет", name)
pick("panel_bottom", 400, 100)
pick("panel_small", 1000, 120)
pick("ring_portrait", 1150, 100)
pick("slot_big", 1150, 240)
pick("btn_sq", 1290, 80); pick("btn_sq_hover", 1380, 80); pick("btn_sq_down", 1470, 80)
pick("btn_rd", 1290, 170); pick("btn_rd_hover", 1380, 170); pick("btn_rd_down", 1470, 170)
pick("bar_frame", 200, 248); pick("bar_green", 500, 250); pick("bar_blue", 700, 250); pick("bar_gold", 900, 250)
pick("frame_minimap", 100, 350)
pick("sel_friend", 300, 360); pick("sel_enemy", 520, 360); pick("sel_neutral", 740, 360)
pick("header_strip", 1100, 340)
pick("fx_move", 100, 940); pick("fx_attack", 280, 940); pick("fx_sparkle", 440, 940)
pick("fx_hit", 620, 940); pick("fx_level", 810, 940); pick("fx_stagger", 980, 940)
pick("fx_fog_edge", 1240, 940); pick("fx_alert", 1460, 940)
print(sorted(os.listdir(out)))
