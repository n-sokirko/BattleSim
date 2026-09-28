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

MODEL = os.path.join(here, '..', '..', 'unity', 'Assets', 'BattleSim', 'Resources', 'Models', 'Knight.bytes')
SRC = os.path.abspath(os.environ.get('GLB', MODEL))
OUT = os.path.abspath(os.environ.get('OUT', SRC))
RENDER = os.environ.get('RENDER', '0') == '1'
out = os.path.join(here, 'out'); os.makedirs(out, exist_ok=True)
if not SRC.endswith('.glb'):  # импортёр Blender узнаёт формат по расширению
    import shutil
    tmp = os.path.join(out, 'Knight.glb'); shutil.copyfile(SRC, tmp); SRC_LOAD = tmp
else:
    SRC_LOAD = SRC
rig = rigio.Rig(SRC_LOAD)
ik = pk.LegIK(rig)
X = Vector((1, 0, 0))
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


CLIPS = [('Shield_Wall_Idle', shield_wall_idle, 2 * 1.0667), ('Shield_Wall_Walk', shield_wall_walk, 1.0667), ('Rise_Undead', rise_undead, RISE)]
only = os.environ.get('ONLY')
for name, fn, dur in CLIPS:
    if only and name not in only.split(','): continue
    rig.record(name, fn, dur)
    print('recorded', name, dur)
rig.g.save(OUT)
print('saved', OUT)

if RENDER:
    tex = os.path.join(os.path.dirname(MODEL), 'Knight_tex0.png.bytes')
    cam = preview.setup(preview.KEEP_RUS, tex, res=(260, 300))
    for name, fn, dur in CLIPS:
        if only and name not in only.split(','): continue
        preview.show(preview.KEEP_DEAD if name == 'Rise_Undead' else preview.KEEP_RUS)
        rows = []
        for view, yaw in (('front', 25), ('side', 90)):
            paths, labels = [], []
            n = 8 if name == 'Rise_Undead' else 6
            for k in range(n):
                t = dur * k / (n - 1) if name == 'Rise_Undead' else dur * k / n
                fn(t)
                preview.aim(cam, (0, 0, 0.72), yaw_deg=yaw, dist=4.6, height=1.25)
                p = os.path.join(out, f'{name}_{view}{k}.png'); rig.scene.render.filepath = p
                bpy.ops.render.render(write_still=True)
                paths.append(p); labels.append(f'{name} {t:.2f}s')
            preview.strip(paths, labels, os.path.join(out, f'strip_{name}_{view}.png'))
            rows.append(os.path.join(out, f'strip_{name}_{view}.png'))
        preview.stack(rows, os.path.join(out, f'sheet_{name}.png'))
