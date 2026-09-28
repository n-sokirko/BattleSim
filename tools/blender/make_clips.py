"""
Свои клипы для модели Knight (KayKit), сделанные в Blender:
  Shield_Wall_Idle, Shield_Wall_Walk — стена щитов мечников Руси (низкая стойка, щит закрывает лицо);
  Rise_Undead — мертвец Нави встаёт: рука с мечом из земли, рывками, как кукла на нитях, голова набок.
Поза собирается из клипов KayKit (Blocking, Walking_A, Lie_StandUp, Idle) с правками поверх и IK ног,
снимается в локальные TRS узлов и дописывается в исходный GLB — меши, скелет и прочие клипы не меняются.

    pip install bpy pillow            # Blender как модуль Python (нужен Python 3.11)
    python tools/blender/make_clips.py            # пересобрать Resources/Models/Knight.bytes
    RENDER=1 python tools/blender/make_clips.py   # и нарисовать превью в tools/blender/out/
"""
import os, sys, math
here = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, here)
import bpy
from mathutils import Vector
import rigio, preview, posekit as pk

MODELS_DIR = os.path.join(here, '..', '..', 'unity', 'Assets', 'BattleSim', 'Resources', 'Models')
RENDER = os.environ.get('RENDER', '0') == '1'
out = os.path.join(here, 'out'); os.makedirs(out, exist_ok=True)
rig = ik = arms = None


def load(model, src=None):
    """Модель игры -> Blender (импортёр узнаёт формат по расширению, поэтому копия в out/*.glb)."""
    global rig, ik, arms
    import shutil
    src = src or os.path.join(MODELS_DIR, model + '.bytes')
    tmp = os.path.join(out, model + '.glb'); shutil.copyfile(src, tmp)
    rig = rigio.Rig(tmp)
    ik = pk.LegIK(rig)
    arms = pk.ArmIK(rig)
    return src


X = Vector((1, 0, 0))
Z = Vector((0, 0, 1))
UPPER = ('upperarm', 'lowerarm', 'wrist', 'hand', 'handslot', 'chest', 'head')


def shield_body(t, period, depth, lean):
    """Верх — как в стойке с щитом: корпус подан вперёд, голова пригнута за щит, дыхание."""
    breath = math.sin(2 * math.pi * t / period)
    pk.move(rig, 'hips', (0, 0, -depth + 0.012 * breath))
    ik.enable(True)
    pk.rotate(rig, 'spine', X, lean + 1.2 * breath)
    pk.rotate(rig, 'head', X, 7 - 1.5 * breath)


def shield_wall_idle(t):
    dur = 2 * 1.0667
    pk.detach(rig); ik.enable(False)
    base = pk.capture(rig, 'Blocking', t % 1.0667)
    pk.detach(rig); pk.apply_basis(rig, base)
    ik.pin(spread=0.05)
    shield_body(t, dur, 0.11, 9)


def shield_wall_walk(t):
    dur = 1.0667
    ik.enable(False)
    legs = pk.capture(rig, 'Walking_A', t)
    top = pk.capture(rig, 'Blocking', 0.0)
    pk.detach(rig)
    # корпус (и поясница) — из стойки со щитом, чтобы щит не гулял вбок вместе с поворотом таза
    pk.apply_basis(rig, {k: (top[k] if k.startswith(UPPER + ('spine',)) else v) for k, v in legs.items()})
    ik.pin(spread=0.03)
    shield_body(t * 2, dur, 0.08, 8)


RISE = 2.9
FWD = Vector((0, -1, 0))


def jerky(u, steps=4, move=0.55):
    """Рывками: каждый отрезок — быстрый рывок (ease-out) и замирание, как кукла на нитях."""
    u = min(1.0, max(0.0, u))
    k = min(steps - 1, int(u * steps))
    x = min(1.0, (u * steps - k) / move)
    return (k + 1 - (1 - x) ** 3) / steps


def rise_undead(t):
    ik.enable(False)
    stand = 2.3333
    if t < 0.6:
        # лежит и дёргается: рука тянется из земли к небу, голова мотается
        base = pk.capture(rig, 'Lie_StandUp', 0.0)
        pk.detach(rig); pk.apply_basis(rig, base)
        reach = pk.smooth(t / 0.25) * (1 - pk.smooth((t - 0.42) / 0.18))
        pk.rotate(rig, 'upperarm.r', X, 75 * reach)
        pk.rotate(rig, 'lowerarm.r', X, 20 * reach)
        pk.rotate(rig, 'head', FWD, 18 * math.sin(t * 31) * pk.smooth(t / 0.15))
        return
    u = (t - 0.6) / (2.45 - 0.6)
    ts = jerky(u) * stand
    base = pk.capture(rig, 'Lie_StandUp', ts)
    if t > 2.45:
        # последний рывок — в стойку мертвеца; голова резко встаёт на место
        idle = pk.capture(rig, 'Idle', 0.0)
        base = pk.mix(base, idle, pk.smooth((t - 2.45) / 0.4))
    pk.detach(rig); pk.apply_basis(rig, base)
    loll = 1 - pk.smooth((t - 2.5) / 0.12)          # голова набок до самого конца, потом щелчок
    pk.rotate(rig, 'head', FWD, 28 * loll)
    pk.rotate(rig, 'head', X, 12 * loll)
    hold = 1 - pk.smooth(u * 1.2)
    pk.rotate(rig, 'spine', FWD, 6 * math.sin(t * 23) * hold)  # дрожь в замираниях


BOW = 1.0
# стойка лучника (пространство арматуры: +X — левая рука, -Y — вперёд, Z — вверх)
BOW_HAND = (0.13, -0.58, 1.12)        # левая рука с луком вытянута к цели
NOCK = (0.06, -0.44, 1.10)            # правая у лука — стрела на тетиве
ANCHOR = (-0.19, 0.04, 1.13)          # тетива натянута к плечу (у кукольной головы щеки нет — к скуле не дотянуться)
RELEASE = (-0.27, 0.12, 1.15)         # отпустил — рука по инерции уходит назад
QUIVER = (-0.16, 0.24, 1.26)          # за стрелой в колчан за правым плечом
POLE_L = (0.75, -0.25, 0.75)          # локоть лучной руки — вниз-наружу
POLE_R = (-0.7, 0.45, 1.45)           # локоть тетивы — высоко и назад
TWIST = -35                           # левое плечо к цели


CHEST_Z = 0.959  # высота груди Rogue в покое, под которую подобраны точки лука


def bow_base(t):
    """Ноги и таз — из покоя, грудь развёрнута левым плечом к цели, голова смотрит на цель.
    Возвращает сдвиг точек лука по высоте: при длинном корпусе плечи выше."""
    ik.enable(False); arms.enable(False)
    base = pk.capture(rig, 'Idle', t % 1.0667)
    pk.detach(rig); pk.apply_basis(rig, base)
    pk.rotate(rig, 'chest', Z, TWIST)
    return rig.arm.pose.bones['chest'].head.z - CHEST_Z


def up(p, dz): return (p[0], p[1], p[2] + dz)


def bow_head(tilt=0.0):
    pk.rotate(rig, 'head', Z, -TWIST * 0.85)
    if tilt: pk.rotate(rig, 'head', FWD, tilt)


def bow_shoot(t):
    """Выстрел из лука (~1 с): натянуть, прицелиться, отпустить, достать новую стрелу."""
    dz = bow_base(t)
    raise_ = pk.smooth(t / 0.3)
    bow = pk.lerp3((0.16, -0.5, 1.0), BOW_HAND, raise_)
    if 0.55 <= t < 0.7:  # отдача лука
        k = math.sin(math.pi * (t - 0.55) / 0.15)
        bow = pk.lerp3(bow, (bow[0], bow[1] - 0.03, bow[2] - 0.03), k)
    if t < 0.08: hand = NOCK
    elif t < 0.42: hand = pk.lerp3(NOCK, ANCHOR, pk.smooth((t - 0.08) / 0.34))
    elif t < 0.55: hand = pk.lerp3(ANCHOR, (ANCHOR[0], ANCHOR[1] + 0.01, ANCHOR[2]), math.sin(t * 60) * 0.5 + 0.5)
    elif t < 0.6: hand = pk.lerp3(ANCHOR, RELEASE, 1 - (1 - (t - 0.55) / 0.05) ** 2)
    elif t < 0.78: hand = pk.lerp3(RELEASE, QUIVER, pk.smooth((t - 0.6) / 0.18))
    else: hand = pk.lerp3(QUIVER, NOCK, pk.smooth((t - 0.78) / 0.22))
    arms.set('l', up(bow, dz), up(POLE_L, dz))
    arms.set('r', up(hand, dz), up(POLE_R, dz))
    bow_head(6 * pk.smooth((t - 0.1) / 0.3) * (1 - pk.smooth((t - 0.6) / 0.2)))


def bow_aim(t):
    """Тетива натянута, ждёт цель: дыхание и лёгкая дрожь."""
    dz = bow_base(t)
    b = math.sin(2 * math.pi * t / 1.0667)
    arms.set('l', (BOW_HAND[0], BOW_HAND[1], BOW_HAND[2] + 0.01 * b + dz), up(POLE_L, dz))
    arms.set('r', (ANCHOR[0], ANCHOR[1] + 0.006 * math.sin(t * 40), ANCHOR[2] + 0.01 * b + dz), up(POLE_R, dz))
    bow_head(6)


KNIGHT = [('Shield_Wall_Idle', shield_wall_idle, 2 * 1.0667), ('Shield_Wall_Walk', shield_wall_walk, 1.0667), ('Rise_Undead', rise_undead, RISE)]
BOW_CLIPS = [('Bow_Shoot', bow_shoot, BOW), ('Bow_Aim', bow_aim, 1.0667)]
# всадники берут верх тела для атак из Knight (ModelLibrary.MakeRider) — поэтому лук и там
MODELS = {'Knight': KNIGHT + BOW_CLIPS, 'Rogue_Hooded': BOW_CLIPS, 'Skeleton': [c for c in KNIGHT if c[0] == 'Rise_Undead']}
KEEP = {'Rise_Undead': preview.KEEP_DEAD, 'Bow_Shoot': {'Rogue_Cape', 'Bow'}, 'Bow_Aim': {'Rogue_Cape', 'Bow'}}
only = os.environ.get('ONLY')
pick = lambda name: not only or name in only.split(',')

for model, clips in MODELS.items():
    if os.environ.get('MODEL') and model != os.environ['MODEL']: continue
    todo = [c for c in clips if pick(c[0])]
    if not todo: continue
    src = load(model)
    for name, fn, dur in todo:
        # поза — на скелете с игровыми пропорциями (IK решает на длинных ногах), в клип — чистые повороты
        rig.record(name, lambda t, fn=fn: (pk.apply_proportions(rig), fn(t), pk.freeze(rig)), dur)
        print('recorded', model, name, dur)
    rig.g.save(src)
    print('saved', src)
    if not RENDER: continue
    cam = preview.setup(preview.KEEP.get(model, set()), os.path.join(MODELS_DIR, model + '_tex0.png.bytes'), res=(260, 300))
    for name, fn, dur in todo:
        preview.show(KEEP.get(name, preview.KEEP.get(model, set())))
        rows = []
        views = (('front', 25, 1.25), ('side', 90, 1.25))
        if name.startswith('Bow'): views = (('right', -90, 1.3), ('back', -140, 1.6), ('top', -60, 4.2))
        for view, yaw, hgt in views:
            paths, labels = [], []
            n = 8 if dur > 2.5 else 6
            for k in range(n):
                t = dur * k / (n - 1) if dur > 2.5 else dur * k / n
                pk.apply_proportions(rig); fn(t)
                preview.aim(cam, (0, 0, 0.72), yaw_deg=yaw, dist=4.6, height=hgt)
                p = os.path.join(out, f'{model}_{name}_{view}{k}.png'); rig.scene.render.filepath = p
                bpy.ops.render.render(write_still=True)
                paths.append(p); labels.append(f'{name} {t:.2f}s')
            preview.strip(paths, labels, os.path.join(out, f'strip_{model}_{name}_{view}.png'))
            rows.append(os.path.join(out, f'strip_{model}_{name}_{view}.png'))
        preview.stack(rows, os.path.join(out, f'sheet_{model}_{name}.png'))
