"""
Набор для сборки персонажей «Сечи»: плотные героические пропорции, грани, жёсткие части на общем скелете KayKit.
Запуск: blender -b -P charkit.py -- <имя> [<имя> ...] [--render] [--pose Idle]
Результат: godot/assets/own/<имя>.glb (с анимациями скелета) и превью в art/previews.
"""
import bpy, bmesh, math, sys, os
from mathutils import Vector, Matrix, Euler

ROOT = r"O:\BattleSim"
RIG = ROOT + r"\godot\assets\kk\Knight.glb"
OUT = ROOT + r"\godot\assets\own"
PREV = ROOT + r"\art\previews"
KEEP_ACTIONS = ["Idle", "Running_A", "Running_B", "Walking_A", "1H_Melee_Attack_Chop", "1H_Melee_Attack_Slice_Diagonal",
                "1H_Melee_Attack_Slice_Horizontal", "1H_Melee_Attack_Stab", "2H_Melee_Attack_Chop", "2H_Melee_Attack_Slice",
                "2H_Melee_Attack_Spin", "2H_Melee_Idle", "1H_Ranged_Shoot", "1H_Ranged_Aiming", "Dualwield_Melee_Attack_Slice",
                "Dualwield_Melee_Attack_Stab", "Hit_A", "Hit_B", "Death_A", "Death_B", "Block", "Cheer", "Spellcast_Shoot", "Spellcast_Raise"]

# ---------------------------------------------------------------- палитра (общая на всю игру)
PAL = {
    "skin": (0.80, 0.58, 0.45), "skin_dark": (0.55, 0.38, 0.30),
    "steel": (0.50, 0.56, 0.64), "steel_dark": (0.27, 0.31, 0.38), "iron": (0.18, 0.19, 0.22),
    "bronze": (0.72, 0.46, 0.20), "bone": (0.86, 0.80, 0.66),
    "teal": (0.10, 0.30, 0.46), "teal_dark": (0.06, 0.17, 0.28),
    "leather": (0.40, 0.25, 0.14), "leather_dark": (0.24, 0.15, 0.09), "wood": (0.45, 0.30, 0.16),
    "olive": (0.30, 0.38, 0.20), "olive_dark": (0.18, 0.24, 0.13),
    "rust": (0.60, 0.20, 0.12), "rust_dark": (0.38, 0.11, 0.08), "ochre": (0.78, 0.58, 0.22), "fur": (0.55, 0.45, 0.32),
    "dark": (0.04, 0.04, 0.05), "white": (0.9, 0.9, 0.88),
}
_mats = {}


def mat(key):
    if key not in _mats:
        m = bpy.data.materials.new(key)
        m.use_nodes = True
        bsdf = m.node_tree.nodes["Principled BSDF"]
        c = PAL[key]
        bsdf.inputs["Base Color"].default_value = (c[0], c[1], c[2], 1)
        bsdf.inputs["Roughness"].default_value = 0.85
        bsdf.inputs["Metallic"].default_value = 0.0
        _mats[key] = m
    return _mats[key]


# ---------------------------------------------------------------- сцена и скелет

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    _mats.clear()
    bpy.ops.import_scene.gltf(filepath=RIG)
    for o in list(bpy.data.objects):
        if o.type != 'ARMATURE':
            bpy.data.objects.remove(o, do_unlink=True)
    arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
    for a in list(bpy.data.actions):
        if a.name not in KEEP_ACTIONS:
            bpy.data.actions.remove(a)
    bones = {}
    for b in arm.data.bones:
        bones[b.name] = (arm.matrix_world @ b.head_local, arm.matrix_world @ b.tail_local)
    return arm, bones


# ---------------------------------------------------------------- примитивы

class Kit:
    def __init__(self, arm, bones):
        self.arm = arm
        self.B = bones
        self.parts = []

    def _emit(self, bm, bone, color, name, flat=True):
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        bm.free()
        me.materials.append(mat(color))
        for p in me.polygons:
            p.use_smooth = not flat
        ob = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(ob)
        vg = ob.vertex_groups.new(name=bone)
        vg.add(list(range(len(me.vertices))), 1.0, 'REPLACE')
        self.parts.append(ob)
        return ob

    @staticmethod
    def _xf(bm, loc, rot, scale, fr=None):
        m = Matrix.Translation(loc) @ Euler(rot).to_matrix().to_4x4() @ Matrix.Diagonal((scale[0], scale[1], scale[2], 1))
        if fr is not None:
            m = fr @ m
        bmesh.ops.transform(bm, matrix=m, verts=bm.verts)

    def box(self, bone, color, loc, size, rot=(0, 0, 0), top=(1, 1), bevel=0.0, name="box", fr=None):
        """Параллелепипед размером size; top — во сколько раз сужена верхняя грань по x и y."""
        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=1.0)
        for v in bm.verts:
            if v.co.z > 0:
                v.co.x *= top[0]
                v.co.y *= top[1]
        if bevel > 0:
            bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=1, affect='EDGES')
        self._xf(bm, loc, rot, size, fr)
        return self._emit(bm, bone, color, name)

    def cyl(self, bone, color, loc, r1, r2, h, rot=(0, 0, 0), seg=6, scale=(1, 1, 1), name="cyl", fr=None):
        bm = bmesh.new()
        bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=seg, radius1=r1, radius2=r2, depth=h)
        self._xf(bm, loc, rot, scale, fr)
        return self._emit(bm, bone, color, name)

    def ball(self, bone, color, loc, r, scale=(1, 1, 1), sub=1, rot=(0, 0, 0), name="ball", fr=None):
        bm = bmesh.new()
        bmesh.ops.create_icosphere(bm, subdivisions=sub, radius=r)
        self._xf(bm, loc, rot, scale, fr)
        return self._emit(bm, bone, color, name)

    def limb(self, bone, color, a, b, r1, r2, seg=6, name="limb"):
        """Конус от точки a до точки b."""
        a = Vector(a); b = Vector(b)
        d = b - a
        mid = (a + b) / 2
        q = d.normalized().to_track_quat('Z', 'Y')
        bm = bmesh.new()
        bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=seg, radius1=r1, radius2=r2, depth=d.length)
        m = Matrix.Translation(mid) @ q.to_matrix().to_4x4()
        bmesh.ops.transform(bm, matrix=m, verts=bm.verts)
        return self._emit(bm, bone, color, name)

    def finish(self, name):
        for o in bpy.context.selected_objects:
            o.select_set(False)
        for o in self.parts:
            o.select_set(True)
        bpy.context.view_layer.objects.active = self.parts[0]
        bpy.ops.object.join()
        ob = bpy.context.view_layer.objects.active
        ob.name = name
        ob.data.name = name
        ob.parent = self.arm
        md = ob.modifiers.new("Armature", 'ARMATURE')
        md.object = self.arm
        return ob


# ---------------------------------------------------------------- человечек

def humanoid(k, spec):
    B = k.B
    V = Vector
    s = spec
    W = s.get("shoulder", 1.0)      # ширина плеч
    skin = s.get("skin", "skin")
    # --- ноги
    for side, sg in (("l", 1), ("r", -1)):
        ul, ll, ft, tz = B["upperleg." + side], B["lowerleg." + side], B["foot." + side], B["toes." + side]
        k.limb("upperleg." + side, s["pants"], ul[0], ul[1], 0.115, 0.10, name="thigh")
        k.limb("lowerleg." + side, s["greave"], ll[0] + V((0, 0, 0.02)), ll[1], 0.105, 0.115, name="shin")
        k.box("foot." + side, s["boot"], V((sg * 0.17, -0.10, 0.075)), (0.17, 0.34, 0.15), top=(0.9, 0.7), name="boot")
        if s.get("kneepads"):
            k.ball("upperleg." + side, s["trim"], V((sg * 0.17, -0.09, 0.29)), 0.085, (1, 0.7, 1), name="knee")
    # --- таз и торс
    k.box("hips", s["pants"], V((0, 0, 0.52)), (0.40, 0.26, 0.20), top=(1.05, 1.0), name="pelvis")
    k.box("hips", s["belt"], V((0, 0, 0.60)), (0.43, 0.285, 0.06), name="belt")
    k.box("hips", s["buckle"], V((0, -0.145, 0.60)), (0.09, 0.03, 0.07), name="buckle")
    k.box("spine", s["torso"], V((0, 0, 0.78)), (0.44 * W ** 0.3, 0.28, 0.36), top=(1.12, 1.0), name="waist")
    k.box("chest", s["torso"], V((0, 0, 1.10)), (0.44 * W, 0.30, 0.34), top=(1.18, 1.0), name="chest")
    if s.get("plate"):
        k.box("chest", s["plate"], V((0, -0.065, 1.09)), (0.42 * W, 0.17, 0.28), top=(1.15, 1.0), bevel=0.02, name="breastplate")
        k.box("spine", s["plate"], V((0, -0.06, 0.78)), (0.42, 0.20, 0.30), top=(1.1, 1.0), bevel=0.015, name="abdomen")
    # --- голова
    hz = 1.40
    k.limb("head", skin, V((0, 0, 1.26)), V((0, 0, 1.34)), 0.075, 0.075, name="neck")
    k.ball("head", skin, V((0, -0.01, hz)), 0.185, (1.0, 0.95, 1.05), sub=2, name="head")
    for sg in (-1, 1):
        k.box("head", "dark", V((sg * 0.062, -0.155, hz + 0.02)), (0.05, 0.03, 0.03), name="eye")
    k.box("head", s.get("hair", "skin_dark"), V((0, -0.17, hz - 0.06)), (0.10, 0.03, 0.05), name="mouth_beard") if s.get("beard") else None
    # --- руки
    for side, sg in (("l", 1), ("r", -1)):
        ua, la, wr, hd = B["upperarm." + side], B["lowerarm." + side], B["wrist." + side], B["hand." + side]
        k.limb("upperarm." + side, s["sleeve"], ua[0], ua[1], 0.105, 0.095, name="arm")
        k.limb("lowerarm." + side, s["glove"], la[0], la[1], 0.10, 0.115, name="forearm")
        k.box("hand." + side, s["glove"], (wr[0] + hd[1]) / 2, (0.16, 0.14, 0.14), name="fist")
    # --- наплечники (у Эрланда асимметричные: левый большой, правый малый)
    pa = s.get("pauldrons")
    if pa:
        for side, sg in (("l", 1), ("r", -1)):
            big = pa[0] if side == "l" else pa[1]
            if big <= 0:
                continue
            ua = B["upperarm." + side]
            c = V((sg * 0.27 * W, 0, 1.22))
            k.ball("upperarm." + side, s["armor_dark"], c + V((sg * 0.02, 0, 0)), 0.15 * big, (1.25, 1.0, 0.8), name="pauldron")
            k.ball("upperarm." + side, s["trim"], c + V((sg * 0.02, 0, 0.06 * big)), 0.10 * big, (1.2, 1.0, 0.45), name="pauldron_trim")
            if s.get("spikes") and big >= 1:
                k.cyl("upperarm." + side, s["trim"], c + V((sg * 0.06, 0, 0.14 * big)), 0.03, 0.0, 0.14, seg=4, name="spike")
    # --- одежда поверх: длинный разрез спереди и сзади, шарф
    if s.get("coat"):
        for yy, nm in ((-0.14, "coat_f"), (0.14, "coat_b")):
            k.box("hips", s["coat"], V((0, yy, 0.38)), (0.30, 0.03, 0.34), top=(1.15, 1.0), name=nm)
        if s.get("coat_trim"):
            k.box("hips", s["coat_trim"], V((0, -0.16, 0.22)), (0.30, 0.02, 0.04), name="coat_hem")
    if s.get("scarf"):
        k.cyl("chest", s["scarf"], V((0, 0, 1.27)), 0.17, 0.13, 0.09, seg=8, name="scarf")
        k.box("chest", s["scarf"], V((0.10, 0.12, 1.12)), (0.10, 0.03, 0.28), rot=(0, 0.2, 0), name="scarf_tail")
    if s.get("cape"):
        k.box("chest", s["cape"], V((0, 0.19, 0.80)), (0.56 * W, 0.035, 0.80), top=(1.05, 1), name="cape")
        k.box("chest", s.get("cape_trim", s["cape"]), V((0, 0.21, 0.40)), (0.52 * W, 0.03, 0.04), name="cape_hem")
    if s.get("fur"):
        k.ball("chest", "fur", V((0, 0.0, 1.27)), 0.24, (1.35 * W, 1.0, 0.45), sub=1, name="fur_mantle")
    # --- шлем
    helmet(k, s.get("helmet"), hz, s)
    # --- вещи
    weapon_r(k, s.get("weapon_r"), s)
    weapon_l(k, s.get("weapon_l"), s)
    if s.get("quiver"):
        k.box("chest", "leather_dark", V((-0.10, 0.18, 1.05)), (0.12, 0.12, 0.40), rot=(0.2, 0, 0.3), name="quiver")
        for i in range(3):
            k.cyl("chest", "bone", V((-0.12 + i * 0.03, 0.2, 1.30)), 0.012, 0.012, 0.12, rot=(0.2, 0, 0.3), seg=4, name="arrow")


def helmet(k, kind, hz, s):
    V = Vector
    if kind == "bascinet":   # Эрланд-герой: закрытый шлем, плавник-гребень, щель
        k.ball("head", s["armor"], V((0, -0.005, hz + 0.02)), 0.235, (1.0, 1.0, 1.12), sub=2, name="helm")
        k.box("head", s["armor_dark"], V((0, -0.215, hz + 0.02)), (0.26, 0.04, 0.04), name="visor")
        k.box("head", s["armor"], V((0.17, -0.02, hz - 0.05)), (0.05, 0.18, 0.16), name="cheek")
        k.box("head", s["armor"], V((-0.17, -0.02, hz - 0.05)), (0.05, 0.18, 0.16), name="cheek")
        k.box("head", s["trim"], V((0, 0.0, hz + 0.25)), (0.06, 0.36, 0.22), top=(1, 0.45), name="fin")
        k.box("head", s["trim"], V((0, -0.19, hz - 0.10)), (0.03, 0.04, 0.16), name="nasal")
    elif kind == "cap":       # ополченец: открытый шлем-шапка с полями
        k.ball("head", s["armor"], V((0, 0, hz + 0.07)), 0.19, (1.0, 1.0, 0.75), sub=2, name="cap")
        k.cyl("head", s["armor_dark"], V((0, 0, hz + 0.05)), 0.26, 0.24, 0.025, seg=10, name="brim")
    elif kind == "hood":      # стрелок: острый капюшон
        k.cyl("head", s["hood"], V((0, 0.02, hz + 0.10)), 0.24, 0.0, 0.40, seg=7, rot=(-0.12, 0, 0), name="hood")
        k.cyl("head", s["hood"], V((0, 0.06, hz - 0.08)), 0.23, 0.2, 0.2, seg=7, name="hood_low")
    elif kind == "horned":    # кочевник: рогатый кожаный шлем
        k.ball("head", s["armor"], V((0, 0, hz + 0.06)), 0.20, (1.0, 1.0, 0.78), sub=2, name="helm")
        k.box("head", s["armor_dark"], V((0, -0.195, hz + 0.0)), (0.22, 0.03, 0.04), name="brow")
        for sg in (-1, 1):
            k.limb("head", "bone", V((sg * 0.17, 0, hz + 0.10)), V((sg * 0.30, 0, hz + 0.20)), 0.045, 0.03, seg=5, name="horn_a")
            k.limb("head", "bone", V((sg * 0.30, 0, hz + 0.20)), V((sg * 0.34, 0, hz + 0.36)), 0.03, 0.0, seg=5, name="horn_b")
    elif kind == "fur_cap":   # лучник кочевников: остроконечная меховая шапка
        k.cyl("head", s["armor"], V((0, 0, hz + 0.14)), 0.20, 0.0, 0.32, seg=7, name="cap")
        k.cyl("head", "fur", V((0, 0, hz + 0.0)), 0.22, 0.22, 0.10, seg=8, name="fur_rim")


R90 = (-math.pi / 2, 0, 0)   # поворот: длина детали (z) -> вдоль оси кости Y


def frame(k, bone):
    return k.arm.data.bones[bone].matrix_local.copy()


def weapon_r(k, kind, s):
    """Правая рука: оружие строится в системе кости handslot.r — рукоять в начале, клинок вдоль +Y (как у KayKit)."""
    if not kind:
        return
    V = Vector
    bn = "hand.r"
    fr = frame(k, "handslot.r")
    if kind == "sword":
        k.box(bn, "leather_dark", V((0, 0.02, 0)), (0.06, 0.06, 0.26), rot=R90, name="grip", fr=fr)
        k.ball(bn, s["trim"], V((0, -0.12, 0)), 0.055, name="pommel", fr=fr)
        k.box(bn, s["trim"], V((0, 0.17, 0)), (0.34, 0.05, 0.07), name="guard", fr=fr)
        k.box(bn, "steel", V((0, 0.73, 0)), (0.13, 0.03, 1.08), rot=R90, top=(0.35, 1.0), name="blade", fr=fr)
        k.box(bn, "bone", V((0, 0.62, 0.0)), (0.035, 0.035, 0.80), rot=R90, name="fuller", fr=fr)
    elif kind == "axe2":
        k.limb(bn, "wood", fr @ V((0, -0.25, 0)), fr @ V((0, 1.25, 0)), 0.035, 0.035, seg=5, name="haft")
        k.box(bn, "iron", V((0, 1.12, 0.0)), (0.07, 0.34, 0.34), name="axehead", fr=fr)
        k.box(bn, "steel", V((0, 1.12, 0.26)), (0.05, 0.40, 0.18), top=(1, 0.5), name="edge_a", fr=fr)
        k.box(bn, "steel", V((0, 1.12, -0.26)), (0.05, 0.40, 0.18), rot=(math.pi, 0, 0), top=(1, 0.5), name="edge_b", fr=fr)
        k.cyl(bn, "iron", V((0, 1.30, 0)), 0.03, 0.0, 0.14, rot=R90, seg=5, name="spike", fr=fr)
    elif kind == "saber":
        k.box(bn, "leather_dark", V((0, 0.02, 0)), (0.06, 0.06, 0.24), rot=R90, name="grip", fr=fr)
        k.box(bn, "bronze", V((0, 0.16, 0)), (0.22, 0.05, 0.06), name="guard", fr=fr)
        k.box(bn, "steel", V((0, 0.55, 0)), (0.11, 0.03, 0.70), rot=R90, top=(0.9, 1.0), name="blade1", fr=fr)
        k.box(bn, "steel", V((0, 1.00, 0.10)), (0.10, 0.03, 0.42), rot=(-math.pi / 2 + 0.35, 0, 0), top=(0.3, 1.0), name="blade2", fr=fr)
    elif kind == "crossbow":
        k.box(bn, "wood", V((0, 0.35, 0)), (0.08, 0.10, 0.80), rot=R90, name="stock", fr=fr)
        k.box(bn, "iron", V((0, 0.62, 0)), (0.80, 0.07, 0.07), name="prod", fr=fr)
        k.box(bn, "bone", V((0, 0.55, 0)), (0.62, 0.015, 0.015), name="string", fr=fr)
        k.box(bn, "iron", V((0, 0.15, -0.06)), (0.05, 0.05, 0.10), name="trigger", fr=fr)
    elif kind == "bow":
        k.limb(bn, "wood", fr @ V((0.0, 0.0, 0.0)) + V((0, 0, 0)), fr @ V((0.0, 0.4, 0.5)), 0.03, 0.025, seg=5, name="bow_a")
        k.limb(bn, "wood", fr @ V((0.0, 0.0, 0.0)), fr @ V((0.0, 0.4, -0.5)), 0.03, 0.025, seg=5, name="bow_b")


def weapon_l(k, kind, s):
    """Левая рука: щит в системе кости handslot.l — диск лежит в плоскости XY кости, лицо смотрит вдоль +Z."""
    if not kind:
        return
    V = Vector
    bn = "hand.l"
    fr = frame(k, "handslot.l")
    FZ = 0.16   # смещение щита от кулака
    if kind == "round_shield":
        k.cyl(bn, s["shield"], V((0, 0.05, FZ)), 0.46, 0.46, 0.07, seg=10, name="shield", fr=fr)
        k.cyl(bn, s["trim"], V((0, 0.05, FZ + 0.04)), 0.47, 0.47, 0.03, seg=10, name="rim", fr=fr)
        k.ball(bn, s["trim"], V((0, 0.05, FZ + 0.09)), 0.09, (1, 1, 0.5), name="boss", fr=fr)
    elif kind == "tower_shield":   # высокий щит с гербом-башней
        k.box(bn, s["shield"], V((0, 0.10, FZ)), (0.62, 0.86, 0.07), top=(1, 1), name="shield", fr=fr)
        k.ball(bn, s["shield"], V((0, 0.53, FZ)), 0.31, (1, 0.7, 0.22), sub=1, name="shield_top", fr=fr)
        k.box(bn, s["trim"], V((0, 0.10, FZ + 0.045)), (0.66, 0.90, 0.025), top=(1, 1), name="frame_dummy", fr=fr)
        k.box(bn, "bone", V((0, 0.15, FZ + 0.06)), (0.17, 0.34, 0.03), name="tower", fr=fr)
        for sx in (-0.065, 0.0, 0.065):
            k.box(bn, "bone", V((sx, 0.36, FZ + 0.06)), (0.045, 0.08, 0.03), name="crenel", fr=fr)
        k.box(bn, s["shield"], V((0, 0.2, FZ + 0.075)), (0.05, 0.12, 0.02), name="door", fr=fr)
    elif kind == "heater":
        k.box(bn, s["shield"], V((0, 0.08, FZ)), (0.50, 0.70, 0.07), top=(1, 1), name="shield", fr=fr)
        k.box(bn, s["trim"], V((0, 0.08, FZ + 0.045)), (0.10, 0.70, 0.02), name="stripe", fr=fr)
    elif kind == "sun_shield":
        k.cyl(bn, "wood", V((0, 0.05, FZ)), 0.42, 0.42, 0.07, seg=10, name="shield", fr=fr)
        k.cyl(bn, "rust", V((0, 0.05, FZ + 0.045)), 0.24, 0.24, 0.02, seg=8, name="sun", fr=fr)
        k.ball(bn, "ochre", V((0, 0.05, FZ + 0.08)), 0.07, (1, 1, 0.5), name="boss", fr=fr)


# ---------------------------------------------------------------- виды

SPECS = {
    # Эрланд: глубокий сине-зелёный, тёмная сталь, бронза (не золото); асимметричные плечи, гребень-плавник, башня на щите
    "hero_kael": dict(shoulder=1.28, pants="teal_dark", greave="steel", boot="leather_dark", belt="leather_dark", buckle="bronze",
                      torso="teal_dark", plate="steel", sleeve="teal_dark", glove="steel_dark", armor="steel", armor_dark="steel_dark", trim="bronze",
                      pauldrons=(1.55, 0.8), spikes=True, kneepads=True, coat="teal", coat_trim="bronze", scarf="bone", cape="teal", cape_trim="bronze",
                      helmet="bascinet", weapon_r="sword", weapon_l="tower_shield", shield="teal"),
    "erland_militia": dict(shoulder=1.1, pants="leather_dark", greave="leather", boot="leather_dark", belt="leather_dark", buckle="steel",
                           torso="leather", plate=None, sleeve="leather_dark", glove="leather", armor="steel", armor_dark="steel_dark", trim="steel",
                           pauldrons=(1.0, 0.0), coat="teal", scarf="bone", helmet="cap", weapon_r="sword", weapon_l="heater", shield="teal", beard=True),
    "erland_archer": dict(shoulder=0.95, pants="olive_dark", greave="leather", boot="leather_dark", belt="leather_dark", buckle="bronze",
                          torso="olive", plate=None, sleeve="olive_dark", glove="leather", armor="olive", armor_dark="olive_dark", hood="olive",
                          trim="bronze", coat="olive", scarf="teal", helmet="hood", weapon_r="crossbow", quiver=True),
    "nomad_raider": dict(shoulder=1.15, pants="rust_dark", greave="leather_dark", boot="leather_dark", belt="leather_dark", buckle="bone",
                         torso="leather", plate="leather_dark", sleeve="leather", glove="leather_dark", armor="iron", armor_dark="iron", trim="bone",
                         fur=True, coat="rust", coat_trim="ochre", helmet="horned", weapon_r="saber", weapon_l="sun_shield", shield="wood"),
    "nomad_archer": dict(shoulder=0.98, pants="rust_dark", greave="leather_dark", boot="leather_dark", belt="leather_dark", buckle="bone",
                         torso="ochre", plate=None, sleeve="rust", glove="leather", armor="leather", armor_dark="leather_dark", trim="bone",
                         coat="rust", coat_trim="ochre", scarf="rust", helmet="fur_cap", weapon_r="crossbow", quiver=True),
    "nomad_khan": dict(shoulder=1.55, pants="rust_dark", greave="iron", boot="iron", belt="leather_dark", buckle="bone",
                       torso="rust_dark", plate="iron", sleeve="leather_dark", glove="iron", armor="iron", armor_dark="iron", trim="bone",
                       pauldrons=(1.5, 1.5), spikes=True, fur=True, coat="rust", coat_trim="ochre", cape="rust", cape_trim="ochre",
                       helmet="horned", weapon_r="axe2", kneepads=True),
}


def build(name, render):
    arm, bones = reset_scene()
    k = Kit(arm, bones)
    humanoid(k, SPECS[name])
    mesh = k.finish("Body")
    os.makedirs(OUT, exist_ok=True)
    for o in bpy.context.selected_objects:
        o.select_set(False)
    arm.select_set(True)
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, name + ".glb"), export_format='GLB', use_selection=True,
                              export_animations=True, export_animation_mode='ACTIONS', export_skins=True, export_yup=True)
    print("EXPORTED", name, "tris", sum(len(p.vertices) - 2 for p in mesh.data.polygons))
    if render:
        preview(arm, name)


def preview(arm, name, pose="Idle", scale=2.7, zc=0.85):
    sc = bpy.context.scene
    act = bpy.data.actions.get(pose)
    if act:
        arm.animation_data_create()
        arm.animation_data.action = act
        sc.frame_set(1)
    for o in list(bpy.data.objects):
        if o.type in ('CAMERA', 'LIGHT'):
            bpy.data.objects.remove(o)
    cam = bpy.data.cameras.new("c")
    cam.type = 'ORTHO'
    cam.ortho_scale = scale
    co = bpy.data.objects.new("c", cam)
    sc.collection.objects.link(co)
    sc.camera = co
    sun = bpy.data.lights.new("s", 'SUN')
    sun.energy = 2.4
    so = bpy.data.objects.new("s", sun)
    so.rotation_euler = (math.radians(50), 0, math.radians(-30))
    sc.collection.objects.link(so)
    w = bpy.data.worlds.new("w")
    w.use_nodes = True
    w.node_tree.nodes["Background"].inputs["Color"].default_value = (0.62, 0.64, 0.66, 1)
    w.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.6
    sc.world = w
    sc.render.engine = 'BLENDER_EEVEE_NEXT'
    sc.render.resolution_x = 520
    sc.render.resolution_y = 700
    sc.view_settings.view_transform = 'Standard'
    os.makedirs(PREV, exist_ok=True)
    # Blender: персонаж смотрит на −Y. Вид спереди: камера на −Y; сбоку: −X; сзади: +Y
    for tag, loc, rot in (("front", (0, -6, zc), (math.radians(90), 0, 0)),
                          ("side", (-6, 0, zc), (math.radians(90), 0, math.radians(-90))),
                          ("back", (0, 6, zc), (math.radians(90), 0, math.radians(180)))):
        co.location = loc
        co.rotation_euler = rot
        sc.render.filepath = os.path.join(PREV, f"{name}_{tag}.png")
        bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    render = "--render" in argv
    for n in [a for a in argv if not a.startswith("--")]:
        build(n, render)
