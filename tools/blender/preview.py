"""Превью: рендер кадров клипа (Cycles) и склейка в полосу."""
import math, os
import bpy
from mathutils import Vector
from PIL import Image, ImageDraw

KEEP_RUS = {'1H_Sword', 'Badge_Shield', 'Knight_Helmet', 'Knight_Cape'}
KEEP = {'Knight': KEEP_RUS, 'Rogue_Hooded': {'Rogue_Cape'}, 'Skeleton': {'1H_Sword', 'Round_Shield'}}
# снаряжение, которое прячется, если не выбрано (как Defs.AllAttachments)
ATTACH = {'1H_Sword_Offhand', 'Badge_Shield', 'Rectangle_Shield', 'Round_Shield', 'Spike_Shield', '1H_Sword', '2H_Sword',
          'Knight_Helmet', 'Knight_Cape', '1H_Axe_Offhand', 'Barbarian_Round_Shield', '1H_Axe', '2H_Axe', 'Mug', 'Barbarian_Hat',
          'Barbarian_Cape', 'Knife_Offhand', '1H_Crossbow', '2H_Crossbow', 'Knife', 'Throwable', 'Rogue_Cape', 'Bow',
          'Orc_Tusks', 'Orc_Ears', 'Rus_Helmet', 'Steppe_Hat', 'Necro_Staff'}
KEEP_DEAD = {'1H_Sword', 'Round_Shield'}
BODY = {'Knight_ArmLeft', 'Knight_ArmRight', 'Knight_Body', 'Knight_Head', 'Knight_LegLeft', 'Knight_LegRight'}


def show(keep):
    """Оставить тело и снаряжение вида войск (как Keep в Defs)."""
    for o in bpy.data.objects:
        if o.type == 'MESH' and o.name != 'Plane':
            on = o.name not in ATTACH and o.name != 'Icosphere' or o.name in keep
            o.hide_render = not on; o.hide_viewport = not on


def setup(keep, tex_path, res=(300, 380)):
    sc = bpy.context.scene
    show(keep)
    # текстура
    img = bpy.data.images.load(tex_path)
    for m in bpy.data.materials:
        if not m.use_nodes: continue
        nt = m.node_tree
        for n in nt.nodes:
            if n.type == 'TEX_IMAGE': n.image = img
    # земля
    bpy.ops.mesh.primitive_plane_add(size=12)
    ground = bpy.context.object
    gm = bpy.data.materials.new('ground'); gm.use_nodes = True
    gm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.35, 0.42, 0.28, 1)
    ground.data.materials.append(gm)
    # свет и камера
    bpy.ops.object.light_add(type='SUN', rotation=(math.radians(50), math.radians(10), math.radians(-35)))
    bpy.context.object.data.energy = 3.5
    sc.world = bpy.data.worlds.new('w'); sc.world.use_nodes = True
    sc.world.node_tree.nodes['Background'].inputs['Color'].default_value = (0.55, 0.62, 0.72, 1)
    sc.world.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.8
    bpy.ops.object.camera_add()
    cam = bpy.context.object; sc.camera = cam
    cam.data.lens = 50
    sc.render.engine = 'CYCLES'; sc.cycles.samples = 12; sc.cycles.use_denoising = False
    sc.cycles.device = 'CPU'
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.film_transparent = False
    return cam


def aim(cam, target, yaw_deg=35, dist=4.2, height=1.4):
    a = math.radians(yaw_deg)
    # Blender: персонаж смотрит вдоль -Y? (glTF +Z вперёд -> Blender -Y)
    cam.location = Vector((target[0] + dist * math.sin(a), target[1] - dist * math.cos(a), height))
    d = Vector(target) - cam.location
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()


def strip(paths, labels, out):
    ims = [Image.open(p) for p in paths]
    w, h = ims[0].size
    sheet = Image.new('RGB', (w * len(ims), h + 18), 'white')
    dr = ImageDraw.Draw(sheet)
    for i, (im, lb) in enumerate(zip(ims, labels)):
        sheet.paste(im, (i * w, 18)); dr.text((i * w + 4, 3), lb, fill='black')
    sheet.save(out)


def stack(paths, out):
    ims = [Image.open(p) for p in paths]
    w = max(i.size[0] for i in ims); h = sum(i.size[1] for i in ims)
    sheet = Image.new('RGB', (w, h), 'white'); y = 0
    for im in ims: sheet.paste(im, (0, y)); y += im.size[1]
    sheet.save(out)


# ---------------------------------------------------------------- цвета как в игре (ModelKit.RecolorRace / RecolorCells / TeamTint)
import numpy as np

RACES = {  # SkinHue, SkinSat, SkinLit, MetalHue, MetalSat, HoodHue, HoodSat, HoodLit (Defs.cs; -1 — не менять)
    'rus': (-1, 0, 1, -1, 0, 215, 0.08, 0.95), 'orcs': (100, 0.55, 0.62, 30, 0.08, 28, 0.4, 0.6),
    'nav': (80, 0.07, 0.82, 25, 0.3, 272, 0.28, 0.42), 'steppe': (24, 0.45, 0.85, 38, 0.45, 36, 0.55, 0.9)}
TEAMS = [(214, 0.62), (2, 0.66)]


def team_cells(model):
    return {'Knight': [(0, 1), (2, 2)], 'Skeleton': [(0, 1), (2, 2)], 'Barbarian': [(0, 1), (1, 1), (2, 2)]}.get(model, [(0, 1), (1, 2)])


def _hsl(rgb):
    mx, mn = rgb.max(-1), rgb.min(-1); l = (mx + mn) / 2; d = mx - mn
    s = np.where(d == 0, 0, d / np.maximum(1e-9, 1 - np.abs(2 * l - 1)))
    return s, l


def _from_hsl(h, s, l):
    c = (1 - np.abs(2 * l - 1)) * s; hp = (h * 6) % 6; x = c * (1 - np.abs(hp % 2 - 1)); m = l - c / 2
    z = np.zeros_like(l); i = np.floor(hp).astype(int)
    r = np.select([i == 0, i == 1, i == 2, i == 3, i == 4, i == 5], [c, x, z, z, x, c])
    g = np.select([i == 0, i == 1, i == 2, i == 3, i == 4, i == 5], [x, c, c, x, z, z])
    b = np.select([i == 0, i == 1, i == 2, i == 3, i == 4, i == 5], [z, z, x, c, c, x])
    return np.stack([r + m, g + m, b + m], -1)


def game_texture(png_bytes_path, model, race, team, out_png):
    """Атлас модели в цветах расы и команды — как его перекрашивает игра."""
    im = np.asarray(Image.open(png_bytes_path).convert('RGB'), float) / 255
    H, W, _ = im.shape; cw, ch = W // 8, H // 4
    def cell(cx, cy, fn):
        blk = im[cy * ch:(cy + 1) * ch, cx * cw:(cx + 1) * cw]
        s, l = _hsl(blk); im[cy * ch:(cy + 1) * ch, cx * cw:(cx + 1) * cw] = fn(s, l)
    rp = RACES.get(race)
    if rp:
        hue, sat, lit, mh, ms, hh, hs, hl = rp
        if hue >= 0:
            for cx in (0, 1): cell(cx, 0, lambda s, l: _from_hsl(np.full_like(l, hue / 360), np.full_like(l, sat), l * lit))
        if mh >= 0 and model in ('Knight', 'Skeleton'): cell(3, 0, lambda s, l: _from_hsl(np.full_like(l, mh / 360), np.full_like(l, ms), l))
        if hh >= 0 and model == 'Rogue_Hooded': cell(1, 1, lambda s, l: _from_hsl(np.full_like(l, hh / 360), np.full_like(l, hs), l * hl))
    th, ts = TEAMS[team]
    for cx, cy in team_cells(model):
        cell(cx, cy, lambda s, l: _from_hsl(np.full_like(l, th / 360), np.maximum(s, ts), np.clip(l * 0.95 + 0.03, 0.08, 0.8)))
    tint = np.array([1, 1, 1]) * 0.86 + _from_hsl(np.array(th / 360), np.array(0.7), np.array(0.6)) * 0.14
    Image.fromarray((np.clip(im * tint, 0, 1) * 255).astype(np.uint8)).save(out_png)
    return out_png
