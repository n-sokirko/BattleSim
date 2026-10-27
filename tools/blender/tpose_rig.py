"""
Скелет Hunyuan поставлен по нашей A-позе модели; анимации KayKit рассчитаны на T-позу.
Здесь руки (и предплечья) разворачиваются горизонтально в позе, поза запекается в сетку и в покой скелета —
получается тот же персонаж в T-позе, на который анимации переносятся без пересчёта позы покоя.
Запуск: blender -b -P tpose_rig.py -- имя [имя ...]
Результат: godot/assets/hy/<имя>.glb (меш + скелет, без анимаций).
"""
import bpy, os, sys
from mathutils import Vector, Quaternion, Matrix

HY = r"O:\BattleSim\art\generated\hy"
OUT = r"O:\BattleSim\godot\assets\hy"
CHARS = ["hero_kael", "erland_militia", "erland_archer", "nomad_raider", "nomad_archer", "nomad_khan"]


def rig_file(name):
    return os.path.join(HY, "kael_rigged.fbx" if name == "hero_kael" else f"rig_{name}.fbx")


def head_w(arm, n):
    return arm.matrix_world @ arm.pose.bones[n].head


def tail_w(arm, n):
    return arm.matrix_world @ arm.pose.bones[n].tail


def aim(arm, bone, target_dir_w):
    """Повернуть кость в позе так, чтобы её направление (голова→хвост) совпало с target_dir_w (мировые оси)."""
    pb = arm.pose.bones[bone]
    cur = (tail_w(arm, bone) - head_w(arm, bone)).normalized()
    q = cur.rotation_difference(target_dir_w.normalized())
    h = head_w(arm, bone)
    m = Matrix.Translation(h) @ q.to_matrix().to_4x4() @ Matrix.Translation(-h)
    pb.matrix = (arm.matrix_world.inverted() @ m @ arm.matrix_world) @ pb.matrix
    bpy.context.view_layer.update()


def run(name):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=rig_file(name))
    arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
    mesh = [o for o in bpy.data.objects if o.type == 'MESH'][0]
    for a in list(bpy.data.actions):
        bpy.data.actions.remove(a)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='POSE')
    for pb in arm.pose.bones:
        pb.rotation_mode = 'QUATERNION'
    # направление «в сторону» в мировых осях: +X для левой, −X для правой
    for side, sg in (("Left", 1), ("Right", -1)):
        d = Vector((sg, 0, 0))
        aim(arm, f"{side}Arm", d)
        aim(arm, f"{side}ForeArm", d)
        aim(arm, f"{side}Hand", d)
    bpy.ops.object.mode_set(mode='OBJECT')
    # запечь позу в сетку
    bpy.ops.object.select_all(action='DESELECT')
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = mesh
    for md in mesh.modifiers:
        if md.type == 'ARMATURE':
            bpy.ops.object.modifier_apply(modifier=md.name)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True)
    bpy.ops.object.mode_set(mode='POSE')
    bpy.ops.pose.armature_apply(selected=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    md = mesh.modifiers.new("Armature", 'ARMATURE')
    md.object = arm
    mesh.parent = arm
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True)
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = arm
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, name + ".glb"), export_format='GLB', use_selection=True,
                              export_animations=False, export_skins=True, export_yup=True)
    print("TPOSE", name, [round(x, 2) for x in head_w(arm, "LeftHand")], [round(x, 2) for x in head_w(arm, "RightHand")])


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else CHARS
    for n in argv:
        run(n)
