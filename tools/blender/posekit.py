"""Поза в Blender из нескольких клипов + правки в пространстве арматуры + IK ног со ступнями на земле."""
import math
import bpy
from mathutils import Matrix, Vector

LEG = {'l': ('upperleg.l', 'lowerleg.l', 'foot.l'), 'r': ('upperleg.r', 'lowerleg.r', 'foot.r')}
ORDER = None


def order(arm):
    global ORDER
    if ORDER is None:
        out = []
        def walk(b):
            out.append(b.name)
            for c in b.children: walk(c)
        for b in arm.data.bones:
            if b.parent is None: walk(b)
        ORDER = out
    return ORDER


def capture(rig, action, t):
    """Локальные позы (matrix_basis) всех костей из клипа в момент t (сек)."""
    rig.use_action(action)
    f = t * rig.fps
    rig.scene.frame_set(int(math.floor(f)), subframe=f - math.floor(f))
    return {pb.name: pb.matrix_basis.copy() for pb in rig.arm.pose.bones}


def detach(rig):
    ad = rig.arm.animation_data
    if ad: ad.action = None


def apply_basis(rig, basis):
    for pb in rig.arm.pose.bones:
        if pb.name in basis: pb.matrix_basis = basis[pb.name]
    bpy.context.view_layer.update()


def rotate(rig, bone, axis, deg, pivot=None):
    """Поворот кости вокруг её головы (или pivot) в пространстве арматуры; потомки следуют."""
    pb = rig.arm.pose.bones[bone]
    head = pivot if pivot is not None else pb.matrix.to_translation()
    r = Matrix.Rotation(math.radians(deg), 4, axis)
    pb.matrix = Matrix.Translation(head) @ r @ Matrix.Translation(-head) @ pb.matrix
    bpy.context.view_layer.update()


def move(rig, bone, offset):
    pb = rig.arm.pose.bones[bone]
    pb.matrix = Matrix.Translation(Vector(offset)) @ pb.matrix
    bpy.context.view_layer.update()


class LegIK:
    """IK на голень (цепь 2) с полюсом перед коленом; ступня копирует сохранённый поворот."""
    def __init__(self, rig):
        self.rig = rig
        self.t, self.p, self.f = {}, {}, {}
        for s, (up, lo, ft) in LEG.items():
            for kind, store in (('tgt', self.t), ('pole', self.p), ('foot', self.f)):
                e = bpy.data.objects.new(f'ik_{kind}_{s}', None)
                bpy.context.scene.collection.objects.link(e)
                store[s] = e
            pb = rig.arm.pose.bones[lo]
            c = pb.constraints.new('IK'); c.target = self.t[s]; c.pole_target = self.p[s]
            c.chain_count = 2; c.pole_angle = math.radians(-90); c.enabled = False
            fc = rig.arm.pose.bones[ft].constraints.new('COPY_ROTATION'); fc.target = self.f[s]; fc.enabled = False
        self.on = False

    def pin(self, spread=0.0, lift=None):
        """Запомнить текущие ступни (голова стопы, её поворот, колено) и включить IK."""
        aw = self.rig.arm.matrix_world
        for s, (up, lo, ft) in LEG.items():
            pbs = self.rig.arm.pose.bones
            foot = aw @ pbs[ft].matrix
            knee = aw @ pbs[lo].matrix.to_translation()
            side = 1 if s == 'l' else -1
            pos = foot.to_translation() + Vector((side * spread, 0, 0))
            if lift is not None: pos.z = max(pos.z, lift)
            self.t[s].matrix_world = Matrix.Translation(pos)
            self.p[s].matrix_world = Matrix.Translation(knee + Vector((side * spread, -0.6, 0)))
            self.f[s].matrix_world = Matrix.Translation(pos) @ foot.to_quaternion().to_matrix().to_4x4()
        self.enable(True)

    def enable(self, on):
        for s, (up, lo, ft) in LEG.items():
            self.rig.arm.pose.bones[lo].constraints['IK'].enabled = on
            self.rig.arm.pose.bones[ft].constraints['Copy Rotation'].enabled = on
        bpy.context.view_layer.update()


def mix(a, b, w):
    """Смесь двух наборов matrix_basis: перенос и масштаб линейно, поворот — slerp."""
    out = {}
    for k in a:
        if k not in b: out[k] = a[k]; continue
        la, ra, sa = a[k].decompose(); lb, rb, sb = b[k].decompose()
        from mathutils import Matrix
        out[k] = Matrix.LocRotScale(la.lerp(lb, w), ra.slerp(rb, w), sa.lerp(sb, w))
    return out


def smooth(x):
    x = min(1.0, max(0.0, x))
    return x * x * (3 - 2 * x)


class ArmIK:
    """IK рук: цель — запястье (конец предплечья), полюс — куда смотрит локоть. Координаты — пространство арматуры."""
    def __init__(self, rig):
        self.rig = rig
        self.t, self.p = {}, {}
        for s in 'lr':
            for kind, store in (('tgt', self.t), ('pole', self.p)):
                e = bpy.data.objects.new(f'arm_{kind}_{s}', None)
                bpy.context.scene.collection.objects.link(e)
                store[s] = e
            c = rig.arm.pose.bones[f'lowerarm.{s}'].constraints.new('IK')
            c.name = 'ArmIK'; c.target = self.t[s]; c.pole_target = self.p[s]
            c.chain_count = 2; c.pole_angle = math.radians(-90); c.enabled = False

        self.angle = {}

    def set(self, side, pos, pole):
        aw = self.rig.arm.matrix_world
        self.t[side].matrix_world = aw @ Matrix.Translation(Vector(pos))
        self.p[side].matrix_world = aw @ Matrix.Translation(Vector(pole))
        c = self.rig.arm.pose.bones[f'lowerarm.{side}'].constraints['ArmIK']
        c.enabled = True
        if side not in self.angle:
            # угол полюса зависит от крена костей: берём тот, при котором локоть ближе всего к полюсу
            best = None
            for deg in range(-180, 180, 15):
                c.pole_angle = math.radians(deg)
                bpy.context.view_layer.update()
                d = (self.rig.arm.pose.bones[f'lowerarm.{side}'].head - Vector(pole)).length
                if best is None or d < best[0]: best = (d, deg)
            self.angle[side] = math.radians(best[1])
        c.pole_angle = self.angle[side]
        bpy.context.view_layer.update()

    def enable(self, on):
        for s in 'lr': self.rig.arm.pose.bones[f'lowerarm.{s}'].constraints['ArmIK'].enabled = on
        bpy.context.view_layer.update()


def lerp3(a, b, w):
    return tuple(x + (y - x) * w for x, y in zip(a, b))



# ---------------------------------------------------------------- пропорции как в игре (Pose.Stretch / Pose.Size в ModelKit.cs)
import os, re

def game_proportions():
    """Читает растяжения костей и размер головы прямо из ModelKit.cs — один источник правды."""
    src = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'unity', 'Assets', 'BattleSim',
                            'Scripts', 'Core', 'ModelKit.cs'), encoding='utf-8').read()
    def block(name):
        m = re.search(r'Dictionary<string, float> ' + name + r' = new Dictionary<string, float>\s*\{(.*?)\};', src, re.S)
        return {k: float(v) for k, v in re.findall(r'\["([\w.]+)"\] = ([\d.]+)f', m.group(1))} if m else {}
    return block('Stretch'), block('Size')


def apply_proportions(rig):
    """Кость тянется вдоль себя (ось Y кости), потомки не наследуют растяжение; голова — целиком. Как Pose в игре."""
    if 'upperleg.l' not in rig.arm.pose.bones: return False
    stretch, size = game_proportions()
    for n, f in stretch.items():
        pb = rig.arm.pose.bones.get(n)
        if pb is None: continue
        pb.scale = (1, f, 1)
        for c in rig.arm.data.bones[n].children: c.inherit_scale = 'NONE'
    for n, f in size.items():
        pb = rig.arm.pose.bones.get(n)
        if pb is not None: pb.scale = (f, f, f)
    bpy.context.view_layer.update()
    rig.props = (stretch, size)
    return True


def freeze(rig):
    """Итог позы (с IK) -> чистые повороты и переносы без растяжений: такой клип игра растянет сама и получит ту же позу."""
    arm = rig.arm
    local = {}
    for n in order(arm):
        pb = arm.pose.bones[n]
        local[n] = arm.convert_space(pose_bone=pb, matrix=pb.matrix, from_space='POSE', to_space='LOCAL')
    for pb in arm.pose.bones:
        for c in pb.constraints: c.enabled = False
    for n in order(arm):
        l, r, _ = local[n].decompose()
        arm.pose.bones[n].matrix_basis = Matrix.LocRotScale(l, r, Vector((1, 1, 1)))
    for b in arm.data.bones: b.inherit_scale = 'FULL'
    bpy.context.view_layer.update()
