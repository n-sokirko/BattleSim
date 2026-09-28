"""Превью: рендер кадров клипа (Cycles) и склейка в полосу."""
import math, os
import bpy
from mathutils import Vector
from PIL import Image, ImageDraw

KEEP_RUS = {'1H_Sword', 'Badge_Shield', 'Knight_Helmet', 'Knight_Cape'}
KEEP_DEAD = {'1H_Sword', 'Round_Shield'}
BODY = {'Knight_ArmLeft', 'Knight_ArmRight', 'Knight_Body', 'Knight_Head', 'Knight_LegLeft', 'Knight_LegRight'}


def show(keep):
    """Оставить тело и снаряжение вида войск (как Keep в Defs)."""
    for o in bpy.data.objects:
        if o.type == 'MESH' and o.name != 'Plane':
            on = o.name in BODY or o.name in keep
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
