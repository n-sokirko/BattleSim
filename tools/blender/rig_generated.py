"""
Привязывает подготовленные (import_generated.py) модели к скелету KayKit и экспортирует с анимациями.
Модель стоит в A-позе, скелет в T-позе: скелет сначала приводится к A-позе, считаются веса,
затем модель и скелет возвращаются в T-позу (веса остаются), чтобы родные анимации KayKit подошли.
Запуск: blender -b -P rig_generated.py -- имя [имя ...] [--render]
"""
import bpy, os, sys, math, glob
from mathutils import Vector, Matrix

sys.path.insert(0, r"O:\BattleSim\tools\blender")
import charkit as ck

GEN = r"O:\BattleSim\godot\assets\gen"
SRC = r"O:\BattleSim\art\generated\static"
SCALE = 1.1            # во сколько раз увеличить скелет под фигуру
ARM_DOWN = math.radians(70)   # на сколько опущены руки в A-позе (от горизонтали)
DEFORM = ["hips", "spine", "chest", "head", "upperarm.l", "lowerarm.l", "wrist.l", "hand.l", "upperarm.r", "lowerarm.r", "wrist.r", "hand.r",
          "upperleg.l", "lowerleg.l", "foot.l", "toes.l", "upperleg.r", "lowerleg.r", "foot.r", "toes.r"]


def setup(name):
    arm, bones = ck.reset_scene()
    for o in list(bpy.data.objects):
        if o.type == 'MESH':
            bpy.data.objects.remove(o)
    bpy.ops.import_scene.gltf(filepath=os.path.join(SRC, name + ".glb"))
    mesh = [o for o in bpy.data.objects if o.type == "MESH"][0]
    bpy.ops.object.select_all(action="DESELECT")
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = mesh
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    zs = [v.co.z for v in mesh.data.vertices]
    mz = min(zs)
    for v in mesh.data.vertices:
        v.co.z -= mz
    print("ZRANGE", mz, max(zs), len(zs))
    return arm, mesh


def torso_center(mesh):
    zs = [v.co.z for v in mesh.data.vertices]
    band = [v.co for v in mesh.data.vertices if 0.85 <= v.co.z <= 1.15]
    xs = sorted(c.x for c in band)
    ys = sorted(c.y for c in band)
    return xs[len(xs) // 2], ys[len(ys) // 2]


def pose_matrix(arm, bone, m):
    pb = arm.pose.bones[bone]
    pb.matrix = m @ pb.matrix


def rot_about(point, axis, ang):
    return Matrix.Translation(point) @ Matrix.Rotation(ang, 4, axis) @ Matrix.Translation(-point)



def fix_weights(mesh, arm):
    """Веса по расстоянию до костей: ближайшие 4 кости, вес ~ 1/расстояние^4 (сетка рваная, связность не используем)."""
    for g in list(mesh.vertex_groups):
        mesh.vertex_groups.remove(g)
    vg = {}
    segs = {}
    for b in arm.data.bones:
        if b.use_deform:
            vg[b.name] = mesh.vertex_groups.new(name=b.name)
            segs[b.name] = (arm.matrix_world @ b.head_local, arm.matrix_world @ b.tail_local)

    def dist(p, a, b):
        ab = b - a
        t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9)))
        return (p - (a + ab * t)).length

    for v in mesh.data.vertices:
        ds = sorted(((dist(v.co, *segs[n]), n) for n in segs))[:4]
        ws = [1.0 / (d + 0.03) ** 4 for d, n in ds]
        tot = sum(ws)
        for (d, n), w in zip(ds, ws):
            vg[n].add([v.index], w / tot, 'REPLACE')
    print("WEIGHTED", len(mesh.data.vertices))


def rig(name, render):
    arm, mesh = setup(name)
    cx, cy = torso_center(mesh)
    arm.scale = (SCALE, SCALE, SCALE)
    arm.location = (cx, cy, 0)
    bpy.context.view_layer.update()
    for b in arm.data.bones:
        b.use_deform = b.name in DEFORM
    bpy.ops.object.select_all(action='DESELECT')
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=True)   # масштаб скелета запечь в кости
    bpy.ops.object.mode_set(mode='POSE')
    # руки в A-позу: левая (x>0) вниз — вращение вокруг +Y по часовой от +X к -Z
    heads = {n: arm.pose.bones[n].head.copy() for n in ("upperarm.l", "upperarm.r")}
    pose_matrix(arm, "upperarm.l", rot_about(arm.matrix_world @ heads["upperarm.l"], 'Y', ARM_DOWN))
    pose_matrix(arm, "upperarm.r", rot_about(arm.matrix_world @ heads["upperarm.r"], 'Y', -ARM_DOWN))
    bpy.context.view_layer.update()
    bpy.ops.pose.armature_apply(selected=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    # веса
    bpy.ops.object.select_all(action='DESELECT')
    mesh.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.parent_set(type='ARMATURE_NAME')
    fix_weights(mesh, arm)
    nvg = len(mesh.vertex_groups)
    # сколько вершин осталось без весов
    nz = sum(1 for v in mesh.data.vertices if not any(g.weight > 0.001 for g in v.groups))
    print("WEIGHTS", name, "groups", nvg, "no-weight verts", nz, "of", len(mesh.data.vertices))
    # возвращаем T-позу: поворачиваем руки назад в позе и запекаем в сетку
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='POSE')
    for n, ang in (("upperarm.l", -ARM_DOWN), ("upperarm.r", ARM_DOWN)):
        pb = arm.pose.bones[n]
        h = arm.matrix_world @ pb.head
        pose_matrix(arm, n, rot_about(h, 'Y', ang))
    bpy.context.view_layer.update()
    bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.select_all(action='DESELECT')
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = mesh
    bpy.ops.object.modifier_apply(modifier="Armature")
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True)
    bpy.ops.object.mode_set(mode='POSE')
    bpy.ops.pose.armature_apply(selected=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    md = mesh.modifiers.new("Armature", 'ARMATURE')
    md.object = arm
    mesh.parent = arm
    # экспорт
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True)
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = arm
    out = os.path.join(GEN, name + ".glb")
    bpy.ops.export_scene.gltf(filepath=out, export_format='GLB', use_selection=True, export_animations=True,
                              export_animation_mode='ACTIONS', export_skins=True, export_yup=True)
    print("RIGGED", name)
    if render:
        ck.PREV = r"O:\BattleSim\art\previews"
        for pose in ("Running_A", "1H_Melee_Attack_Chop"):
            act = bpy.data.actions.get(pose)
            if act:
                arm.animation_data_create()
                arm.animation_data.action = act
                bpy.context.scene.frame_set(int(act.frame_range[0] + (act.frame_range[1] - act.frame_range[0]) * 0.4))
                ck.preview(arm, f"gen_{name}_{pose}", None, 2.9, 0.9)


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    render = "--render" in argv
    for n in [a for a in argv if not a.startswith("--")]:
        rig(n, render)
