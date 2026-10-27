"""
Собирает персонажей Hunyuan (скелет Mixamo-типа, 28 костей) с набором анимаций.
Анимации сделаны на Каэле и переносятся на всех по именам костей (корень Hips масштабируется по росту).
Результат: godot/assets/hy/<имя>.glb (меш + скелет + все клипы).
Запуск: blender -b -P anim_pipeline.py -- имя [имя ...] [--render]
"""
import bpy, os, sys, math
from mathutils import Vector

HY = r"O:\BattleSim\art\generated\hy"
OUT = r"O:\BattleSim\godot\assets\hy"
CLIPS = {   # имя клипа в игре: файл
    "idle": "idle1.fbx", "walk": "walk1.fbx", "run": "run1.fbx", "jog": "jog2.fbx",
    "atk_double": "atk_double.fbx", "atk_continuous": "atk_continuous.fbx", "atk_charge": "atk_charge.fbx",
    "hit": "hit2.fbx", "death": "death1.fbx",
}
CHARS = ["hero_kael", "erland_militia", "erland_archer", "nomad_raider", "nomad_archer", "nomad_khan"]


def rig_file(name):
    return os.path.join(HY, "kael_rigged.fbx" if name == "hero_kael" else f"rig_{name}.fbx")


def hips_height(arm):
    return (arm.matrix_world @ arm.data.bones["Hips"].head_local).z


def load_clip(path, label):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    arm = [o for o in new if o.type == 'ARMATURE'][0]
    act = arm.animation_data.action
    act.name = label
    act.use_fake_user = True
    h = hips_height(arm)
    arm.animation_data.action = None
    for o in new:
        bpy.data.objects.remove(o, do_unlink=True)
    return act, h


def run(name, render):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.fps = 60
    bpy.ops.import_scene.fbx(filepath=rig_file(name))
    arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
    mesh = [o for o in bpy.data.objects if o.type == 'MESH'][0]
    my_h = hips_height(arm)
    arm.animation_data_create()
    stats = []
    for label, fn in CLIPS.items():
        p = os.path.join(HY, fn)
        if not os.path.exists(p):
            print("SKIP", label)
            continue
        act, src_h = load_clip(p, label)
        # масштаб ходьбы по росту: смещение Hips умножаем на отношение высот бёдер
        k = my_h / src_h if src_h else 1.0
        if abs(k - 1.0) > 0.01:
            for fc in act.fcurves:
                if 'pose.bones["Hips"].location' in fc.data_path or fc.data_path.endswith('Hips"].location'):
                    for kp in fc.keyframe_points:
                        kp.co.y *= k
                        kp.handle_left.y *= k
                        kp.handle_right.y *= k
        stats.append((label, [round(x) for x in act.frame_range], round(k, 3)))
    print("CLIPS", name, stats)
    # экспорт: арматура+меш выбраны, клипы — все действия
    for o in bpy.data.objects:
        o.select_set(False)
    arm.select_set(True)
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = arm
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, name + ".glb"), export_format='GLB', use_selection=True,
                              export_animations=True, export_animation_mode='ACTIONS', export_skins=True,
                              export_yup=True, export_apply=False)
    print("EXPORTED", name)


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else CHARS
    for n in [a for a in argv if not a.startswith("--")]:
        run(n, "--render" in argv)
