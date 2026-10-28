"""
Декорации из Hunyuan3D: FBX -> один меш, центр по XY, низ на нулевой высоте, масштаб под заданный размер,
упрощение до нужного числа треугольников (UV и текстура сохраняются) -> godot/assets/props_gen/<имя>.glb.
Запуск: blender -b -P prop_import.py -- имя размер_в_метрах [треугольников]
Исходник: art/generated/props/<имя>.fbx
"""
import bpy, sys, os
from mathutils import Vector

SRC = r"O:\BattleSim\art\generated\props"
OUT = r"O:\BattleSim\godot\assets\props_gen"

args = sys.argv[sys.argv.index("--") + 1:]
name = args[0]
size = float(args[1])
tris = int(args[2]) if len(args) > 2 else 20000

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=os.path.join(SRC, name + ".fbx"))
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
bpy.ops.object.select_all(action='DESELECT')
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1:
    bpy.ops.object.join()
ob = bpy.context.view_layer.objects.active
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
n0 = len(ob.data.polygons)
# нормализация: центр по XY, низ в нуле, больший горизонтальный размер = size
pts = [v.co.copy() for v in ob.data.vertices]
mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
# FBX из Hunyuan приходит с осью вверх Y в сантиметрах: если высота по Y заметно больше, чем по Z, повернём
dx, dy, dz = mx.x - mn.x, mx.y - mn.y, mx.z - mn.z
print("BBOX", dx, dy, dz)
k = size / max(dx, dy)
cx, cy = (mn.x + mx.x) / 2, (mn.y + mx.y) / 2
for v in ob.data.vertices:
    v.co = Vector(((v.co.x - cx) * k, (v.co.y - cy) * k, (v.co.z - mn.z) * k))
md = ob.modifiers.new("dec", 'DECIMATE')
md.ratio = min(1.0, tris / max(n0 * 1.0, 1))
bpy.ops.object.modifier_apply(modifier="dec")
bpy.ops.object.shade_smooth()
print("POLYS", n0, "->", len(ob.data.polygons))
os.makedirs(OUT, exist_ok=True)
bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, name + ".glb"), export_format='GLB', export_yup=True, export_image_format='AUTO')
print("SAVED", name)
