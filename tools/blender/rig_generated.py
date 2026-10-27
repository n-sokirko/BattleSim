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



def fill_missing(mesh, arm):
    """Вершинам без весов (после автоматических) — ближайшая кость; затем нормализация."""
    vg = {g.name: g for g in mesh.vertex_groups}
    segs = {b.name: (arm.matrix_world @ b.head_local, arm.matrix_world @ b.tail_local) for b in arm.data.bones if b.name in vg}

    def dist(p, a, b):
        ab = b - a
        t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9)))
        return (p - (a + ab * t)).length

    n = 0
    for v in mesh.data.vertices:
        if not any(g.weight > 0.001 for g in v.groups):
            best = min(segs, key=lambda k: dist(v.co, *segs[k]))
            vg[best].add([v.index], 1.0, 'REPLACE')
            n += 1
    print("FILLED", n)


def fix_weights(mesh, arm):
    """
    Веса по расстоянию до костей. Тело (голова, торс, ноги) считается по костям тела, рука — по костям своей руки;
    вершина у плеча смешивает те и другие по расстоянию от оси тела (a=0 у середины груди, a=1 у наружного края руки).
    """
    for g in list(mesh.vertex_groups):
        mesh.vertex_groups.remove(g)
    vg = {}
    segs = {}
    for b in arm.data.bones:
        if b.use_deform:
            vg[b.name] = mesh.vertex_groups.new(name=b.name)
            segs[b.name] = (arm.matrix_world @ b.head_local, arm.matrix_world @ b.tail_local)
    arm_bones = {s: [f"upperarm.{s}", f"lowerarm.{s}", f"wrist.{s}", f"hand.{s}"] for s in ("l", "r")}
    all_arm = set(arm_bones["l"] + arm_bones["r"])
    body = [n for n in segs if n not in all_arm]
    cx = segs["hips"][0].x

    def dist(p, a, b):
        ab = b - a
        t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9)))
        return (p - (a + ab * t)).length

    def weights(p, cand, k=3, power=3):
        ds = sorted(((dist(p, *segs[n]), n) for n in cand))[:k]
        ws = [1.0 / (d + 0.03) ** power for d, n in ds]
        t = sum(ws)
        return {n: w / t for (d, n), w in zip(ds, ws)}

    def smooth(x0, x1, x):
        t = max(0.0, min(1.0, (x - x0) / (x1 - x0)))
        return t * t * (3 - 2 * t)

    for v in mesh.data.vertices:
        p = v.co
        side = "l" if p.x > cx else "r"
        wb = weights(p, body)
        da = min(dist(p, *segs[n]) for n in arm_bones[side])
        lateral = abs(p.x - cx)
        a = smooth(0.20, 0.34, lateral) if da < 0.17 else 0.0
        if a > 0:
            wa = weights(p, arm_bones[side])
            mix = {}
            for n, w in wb.items():
                mix[n] = mix.get(n, 0) + w * (1 - a)
            for n, w in wa.items():
                mix[n] = mix.get(n, 0) + w * a
        else:
            mix = wb
        for n, w in mix.items():
            if w > 0.002:
                vg[n].add([v.index], w, 'REPLACE')
    print("WEIGHTED", len(mesh.data.vertices))


def fit_handslots(mesh, arm):
    """Точка хвата (handslot) ставится в центр настоящей кисти модели: среднее вершин, привязанных к кисти."""
    vgs = {g.index: g.name for g in mesh.vertex_groups}
    print("VG", len(vgs), list(vgs.values())[:5], "verts", len(mesh.data.vertices), "withgroups", sum(1 for v in mesh.data.vertices if v.groups))
    centers = {}
    from collections import Counter
    cnt = Counter(vgs.get(max(v.groups, key=lambda g: g.weight).group) for v in mesh.data.vertices if v.groups)
    print("TOP", dict(cnt))
    for side in ("l", "r"):
        cand = []
        for v in mesh.data.vertices:
            if not v.groups:
                continue
            top = max(v.groups, key=lambda g: g.weight)
            nm = vgs.get(top.group, "")
            if nm in (f"lowerarm.{side}", f"wrist.{side}", f"hand.{side}"):
                cand.append(v.co.copy())
        if cand:
            far = max(abs(p.x) for p in cand)
            pts = [p for p in cand if abs(p.x) > far - 0.11]       # кисть — крайние 11 см руки
            centers[side] = sum(pts, Vector()) / len(pts)
            print("HANDFIT", side, "arm verts", len(cand), "fist verts", len(pts), "far x", round(far, 3))
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    for side, c in centers.items():
        eb = arm.data.edit_bones.get(f"handslot.{side}")
        if eb is None:
            continue
        d = eb.tail - eb.head
        eb.head = c
        eb.tail = c + d
        print("HANDSLOT", side, [round(x, 3) for x in c], "hand verts", len(pts))
    bpy.ops.object.mode_set(mode='OBJECT')


def arm_angles(mesh, arm):
    """Угол опущенных рук у модели (от горизонтали, градусы → радианы): от плеча скелета до самой низкой наружной точки кисти."""
    out = {}
    cx = arm.matrix_world.translation.x
    for side, sg in (("l", 1), ("r", -1)):
        sh = arm.matrix_world @ arm.pose.bones[f"upperarm.{side}"].head
        cand = [v.co for v in mesh.data.vertices if sg * (v.co.x - sh.x) > 0.10 and 0.30 < v.co.z < sh.z - 0.05]
        if not cand:
            out[side] = math.radians(70)
            continue
        zmin = min(c.z for c in cand)
        low = [c for c in cand if c.z < zmin + 0.12]
        h = sum(low, Vector()) / len(low)
        ang = math.atan2(sh.z - h.z, abs(h.x - sh.x))
        out[side] = max(math.radians(35), min(math.radians(85), ang))
        print("ARMANGLE", side, round(math.degrees(out[side]), 1), "hand at", [round(x, 2) for x in h], "shoulder", [round(x, 2) for x in sh])
    return out


# высоты суставов для наших персонажей (рост 1.7): подгоняем скелет под них
JOINT = dict(ankle=0.13, knee=0.50, hip=0.88, spine=0.97, spine_top=1.14, chest_top=1.34, shoulder=1.27, neck=1.36, top=1.64, sx=0.25, lx=0.19)


def measure_arm(mesh, side):
    """Длина опущенной руки (плечо → кисть) и угол от горизонтали по самой модели."""
    sg = 1 if side == "l" else -1
    S = Vector((sg * JOINT["sx"], 0, JOINT["shoulder"]))
    cand = [v.co for v in mesh.data.vertices if sg * (v.co.x - S.x) > 0.08 and 0.25 < v.co.z < S.z - 0.08]
    if not cand:
        return 0.6, math.radians(70)
    zmin = min(c.z for c in cand)
    low = [c for c in cand if c.z < zmin + 0.10]
    h = sum(low, Vector()) / len(low)
    d = h - S
    L = d.length
    ang = max(math.radians(35), min(math.radians(88), math.atan2(S.z - h.z, abs(h.x - S.x))))
    print("MEASURE", side, "arm length", round(L, 2), "angle", round(math.degrees(ang), 1), "hand", [round(x, 2) for x in h])
    return L, ang


def fit_skeleton(arm, arms):
    """Кости по высотам нашего персонажа; руки — по длине руки модели. Направления костей те же, что у KayKit (сохраняются крены)."""
    J = JOINT
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    E = arm.data.edit_bones

    def put(name, head, tail):
        b = E[name]
        b.use_connect = False
        b.head = Vector(head)
        b.tail = Vector(tail)

    put("hips", (0, 0, J["hip"] - 0.08), (0, 0, J["spine"]))
    put("spine", (0, 0, J["spine"]), (0, 0, J["spine_top"]))
    put("chest", (0, 0, J["spine_top"]), (0, 0, J["chest_top"]))
    put("head", (0, 0, J["neck"]), (0, 0, J["top"]))
    for side, sg in (("l", 1), ("r", -1)):
        L, _ = arms[side]
        x0 = J["sx"]
        z = J["shoulder"]
        a, b, c, d = 0.46 * L, 0.88 * L, 0.94 * L, 1.0 * L
        put(f"upperarm.{side}", (sg * x0, 0, z), (sg * (x0 + a), 0, z))
        put(f"lowerarm.{side}", (sg * (x0 + a), 0, z), (sg * (x0 + b), 0, z))
        put(f"wrist.{side}", (sg * (x0 + b), 0, z), (sg * (x0 + c), 0, z))
        put(f"hand.{side}", (sg * (x0 + c), 0, z), (sg * (x0 + d), 0, z))
        hs = E[f"handslot.{side}"]
        hs.use_connect = False
        mid = Vector((sg * (x0 + (c + d) / 2), 0.0, z - 0.02))
        hs.head = mid
        hs.tail = mid + Vector((0, -0.11, 0))
        lx = J["lx"]
        put(f"upperleg.{side}", (sg * lx, 0, J["hip"]), (sg * lx, 0, J["knee"]))
        put(f"lowerleg.{side}", (sg * lx, 0, J["knee"]), (sg * lx, 0, J["ankle"]))
        put(f"foot.{side}", (sg * lx, 0.02, J["ankle"]), (sg * lx, -0.11, 0.04))
        put(f"toes.{side}", (sg * lx, -0.11, 0.04), (sg * lx, -0.29, 0.04))
    bpy.ops.object.mode_set(mode='OBJECT')


def rig(name, render):
    arm, mesh = setup(name)
    for a_ in bpy.data.actions:
        a_.use_fake_user = True
    if arm.animation_data:
        arm.animation_data.action = None
    for pb_ in arm.pose.bones:
        pb_.matrix_basis = Matrix()
    cx, cy = torso_center(mesh)
    arm.scale = (SCALE, SCALE, SCALE)
    for v in mesh.data.vertices:
        v.co.x -= cx
        v.co.y -= cy
    arm.location = (0, 0, 0)
    bpy.context.view_layer.update()
    for b in arm.data.bones:
        b.use_deform = b.name in DEFORM
    bpy.ops.object.select_all(action='DESELECT')
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=True)   # масштаб скелета запечь в кости
    arms = {s: measure_arm(mesh, s) for s in ('l', 'r')}
    fit_skeleton(arm, arms)
    bpy.ops.object.mode_set(mode='POSE')
    # руки в A-позу: левая (x>0) вниз — вращение вокруг +Y по часовой от +X к -Z
    heads = {n: arm.pose.bones[n].head.copy() for n in ("upperarm.l", "upperarm.r")}
    ang = {s: arms[s][1] for s in arms}
    pose_matrix(arm, "upperarm.l", rot_about(arm.matrix_world @ heads["upperarm.l"], "Y", ang["l"]))
    pose_matrix(arm, "upperarm.r", rot_about(arm.matrix_world @ heads["upperarm.r"], "Y", -ang["r"]))
    bpy.context.view_layer.update()
    bpy.ops.pose.armature_apply(selected=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    for nme in ('upperarm.l','lowerarm.l','wrist.l','hand.l'):
        bb = arm.data.bones[nme]
        print('ABONE', nme, [round(x,2) for x in bb.head_local], [round(x,2) for x in bb.tail_local])
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
    for n, ang_n in (("upperarm.l", -ang["l"]), ("upperarm.r", ang["r"])):
        pb = arm.pose.bones[n]
        h = arm.matrix_world @ pb.head
        pose_matrix(arm, n, rot_about(h, 'Y', ang_n))
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
    fit_handslots(mesh, arm)
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
