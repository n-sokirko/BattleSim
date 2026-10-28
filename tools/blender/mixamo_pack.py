"""
Пак Mixamo (папка с FBX: X Bot.fbx + по файлу на анимацию) → один GLB: скелет Mixamo с мешем X Bot и всеми клипами.
Запуск: blender -b -P mixamo_pack.py -- <папка> <выход.glb>
"""
import bpy, os, sys, re

argv = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT = argv[0], argv[1]


def clean(n):
    return re.sub(r"[^a-z0-9]+", "_", n.lower()).strip("_")


def import_fbx(p):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=p)
    return [o for o in bpy.data.objects if o not in before]


bpy.ops.wm.read_factory_settings(use_empty=True)
base = import_fbx(os.path.join(SRC, "X Bot.fbx"))
arm = [o for o in base if o.type == 'ARMATURE'][0]
arm.animation_data_create()
arm.animation_data.action = None
count = 0
for f in sorted(os.listdir(SRC)):
    if not f.lower().endswith(".fbx") or f == "X Bot.fbx":
        continue
    new = import_fbx(os.path.join(SRC, f))
    a = [o for o in new if o.type == 'ARMATURE'][0]
    act = a.animation_data.action
    act.name = clean(os.path.splitext(f)[0])
    act.use_fake_user = True
    a.animation_data.action = None
    for o in new:
        bpy.data.objects.remove(o, do_unlink=True)
    count += 1
for o in bpy.data.objects:
    o.select_set(o.type in ('ARMATURE', 'MESH'))
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB', use_selection=True, export_animations=True,
                          export_animation_mode='ACTIONS', export_skins=True, export_yup=True, export_apply=False)
print("EXPORTED", OUT, count, "clips")
