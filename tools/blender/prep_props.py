"""
Готовит оружие и щиты, сгенерированные по референсам: поворот в нужную ось, масштаб, начало координат в хвате,
упрощение сетки, уменьшение текстур. Система координат как у KayKit: длина вдоль +Z (в игре +Y), ширина X.
Результат: godot/assets/gen/props/<имя>.glb
"""
import bpy, bmesh, os, sys, math, glob
from mathutils import Vector, Matrix

SRC = r"O:\BattleSim\art\generated\raw2"
OUT = r"O:\BattleSim\godot\assets\gen\props"
TRIS = 4500
TEX = 512

# имя: (префикс файла, поворот, какой конец вверх, длина по Z, доля длины до хвата, центрировать по Z)
PROPS = {
    "sword_knight": ("1ded4b9e", ("X", 90), "narrow", 1.05, 0.15, False),
    "shield_tower": ("6992fee0", None, None, 1.0, 0.5, True),
    "sword_short":  ("a8aeeea7", None, "narrow", 0.80, 0.15, False),
    "shield_rect":  ("2cbe2cbd", None, None, 0.85, 0.5, True),
    "crossbow":     ("7bbf7f10", None, "wide", 0.80, 0.28, False),
    "saber":        ("1e7ee006", None, "narrow", 0.95, 0.14, False),
    "shield_sun":   ("2b58471f", None, None, 0.80, 0.5, True),
    "bow":          ("d763ca13", ("Y", 90), None, 1.10, 0.5, True),
    "axe2":         ("60486ab6", ("Y", 90), "wide", 1.35, 0.30, False),
}


def run(name):
    pre, rot, up, length, grip, center = PROPS[name]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=glob.glob(os.path.join(SRC, pre + "*.glb"))[0])
    ob = [o for o in bpy.data.objects if o.type == 'MESH'][0]
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    # упрощение
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    md = ob.modifiers.new("dec", 'DECIMATE')
    md.ratio = min(1.0, TRIS / max(tris, 1))
    md.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier="dec")
    me = ob.data
    if rot:
        axis, deg = rot
        m = Matrix.Rotation(math.radians(deg), 4, axis)
        for v in me.vertices:
            v.co = m @ v.co
    # длинная ось должна стать Z: если нет — повернуть
    def ext():
        xs = [v.co.x for v in me.vertices]; ys = [v.co.y for v in me.vertices]; zs = [v.co.z for v in me.vertices]
        return (min(xs), max(xs)), (min(ys), max(ys)), (min(zs), max(zs))
    ex, ey, ez = ext()
    # щиты: высота по Z уже; оружие: проверка конца
    if up:
        zmin, zmax = ez
        span = zmax - zmin
        if up == "narrow":   # меч: гарда (самое широкое место) внизу, остриё вверх
            nb = 40
            lo = [1e9] * nb
            hi = [-1e9] * nb
            for v in me.vertices:
                b = min(nb - 1, int((v.co.z - zmin) / span * nb))
                lo[b] = min(lo[b], v.co.x); hi[b] = max(hi[b], v.co.x)
            wmax = [hi[b] - lo[b] if hi[b] > lo[b] else 0 for b in range(nb)]
            gi = max(range(2, nb - 2), key=lambda b: wmax[b])
            flip = gi > nb / 2
            print("ORIENT", name, "guard bin", gi, "flip", flip)
        else:                # широкий конец (набалдашник топора, дуга арбалета) вверх
            def width(lo_z, hi_z):
                vs = [v.co for v in me.vertices if lo_z <= v.co.z <= hi_z]
                return (max(v.x for v in vs) - min(v.x for v in vs)) if vs else 0
            top = width(zmax - span * 0.15, zmax)
            bot = width(zmin, zmin + span * 0.15)
            flip = top < bot
            print("ORIENT", name, "top", round(top, 3), "bottom", round(bot, 3), "flip", flip)
        if flip:
            m = Matrix.Rotation(math.pi, 4, 'X')
            for v in me.vertices:
                v.co = m @ v.co
    ex, ey, ez = ext()
    k = length / (ez[1] - ez[0])
    oz = (ez[0] + ez[1]) / 2 if center else ez[0] + (ez[1] - ez[0]) * grip
    ox = (ex[0] + ex[1]) / 2
    oy = (ey[0] + ey[1]) / 2
    for v in me.vertices:
        v.co = Vector(((v.co.x - ox) * k, (v.co.y - oy) * k, (v.co.z - oz) * k))
    for im in bpy.data.images:
        if im.size[0] > TEX:
            im.scale(TEX, TEX)
    for p in me.polygons:
        p.use_smooth = True
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, name + ".glb"), export_format='GLB', use_selection=True, export_apply=True, export_yup=True)
    print("PROP", name, "tris", sum(len(p.vertices) - 2 for p in me.polygons))


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else list(PROPS)
    for n in argv:
        run(n)
