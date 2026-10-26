"""
Готовит сгенерированные по референсу модели (ИИ-3D) для игры:
вырезает одну фигуру (вид спереди) из файла с тремя видами, упрощает до ~TRIS треугольников,
уменьшает текстуры, ставит ноги на ноль и экспортирует в godot/assets/gen/<имя>.glb.
Запуск: blender -b -P import_generated.py [-- имя ...]
"""
import bpy, bmesh, os, sys, math
from mathutils import Vector

SRC = r"O:\BattleSim\art\generated"
OUT = r"O:\BattleSim\godot\assets\gen"
TRIS = 22000
HEIGHT = 1.7
TEX = 1024

FILES = {
    "hero_kael":      ("4e789a85188878595dcf5a5c17e2aa43.glb", False),   # один персонаж, видов нет
    "erland_militia": ("0880add0f4075b5f7655fff777c01bb4.glb", True),
    "erland_archer":  ("9881be9995abc4a86978e07c04b31ab8.glb", True),
    "nomad_raider":   ("816fe42c5d8dfc30ff76a2398d019299.glb", True),
    "nomad_archer":   ("899ee7ed1598fc91b89aa554c655a0dd.glb", True),
    "nomad_khan":     ("06ba3cc96bc6b0afa8d3d7e797f3c005.glb", True),
}


def islands(me):
    """Связные куски сетки: список множеств индексов вершин."""
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    seen = set()
    out = []
    for v in bm.verts:
        if v.index in seen:
            continue
        stack = [v]
        comp = []
        seen.add(v.index)
        while stack:
            a = stack.pop()
            comp.append(a.index)
            for e in a.link_edges:
                b = e.other_vert(a)
                if b.index not in seen:
                    seen.add(b.index)
                    stack.append(b)
        out.append(comp)
    bm.free()
    return out


def process(name):
    fn, three = FILES[name]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=os.path.join(SRC, fn))
    ob = [o for o in bpy.data.objects if o.type == 'MESH'][0]
    me = ob.data
    print("LOADED", name, len(me.polygons), "polys")
    if three:
        comps = islands(me)
        co = me.vertices
        # интервалы по X для каждого куска; сливаем пересекающиеся в группы — это отдельные фигуры
        items = []
        for c in comps:
            xs = [co[i].co.x for i in c]
            items.append((min(xs), max(xs), c))
        items.sort(key=lambda t: t[0])
        groups = []
        for lo, hi, c in items:
            if groups and lo <= groups[-1][1] + 0.02:
                groups[-1][1] = max(groups[-1][1], hi)
                groups[-1][2].extend(c)
            else:
                groups.append([lo, hi, list(c)])
        print("GROUPS", [(round(g[0], 2), round(g[1], 2), len(g[2])) for g in groups])
        groups = [g for g in groups if len(g[2]) > 5000]
        front = groups[0]
        keep = set(front[2])
        bm = bmesh.new()
        bm.from_mesh(me)
        bm.verts.ensure_lookup_table()
        kill = [v for v in bm.verts if v.index not in keep]
        bmesh.ops.delete(bm, geom=kill, context='VERTS')
        bm.to_mesh(me)
        bm.free()
    # упрощение с сохранением разверстки
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    md = ob.modifiers.new("dec", 'DECIMATE')
    md.decimate_type = 'COLLAPSE'
    md.ratio = min(1.0, TRIS / max(tris, 1))
    md.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier="dec")
    print("DECIMATED", name, tris, "->", sum(len(p.vertices) - 2 for p in ob.data.polygons))
    # положение: ноги на ноль, центр по XY, высота HEIGHT (оружие может быть выше — берём по телу: 98-й процентиль не нужен)
    pts = [ob.matrix_world @ v.co for v in ob.data.vertices]
    mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    h = mx.z - mn.z
    k = HEIGHT / h
    cx = (mn.x + mx.x) / 2
    cy = (mn.y + mx.y) / 2
    for v in ob.data.vertices:
        v.co = Vector(((v.co.x - cx) * k, (v.co.y - cy) * k, (v.co.z - mn.z) * k))
    print("FINAL", name, "size", [round((mx[i] - mn[i]) * k, 2) for i in range(3)])
    # текстуры поменьше
    for im in bpy.data.images:
        if im.size[0] > TEX:
            im.scale(TEX, TEX)
    for p in ob.data.polygons:
        p.use_smooth = True
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, name + ".glb"), export_format='GLB', use_selection=True,
                              export_apply=True, export_yup=True, export_image_format='AUTO')
    print("EXPORTED", name)


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else list(FILES)
    for n in argv:
        process(n)
