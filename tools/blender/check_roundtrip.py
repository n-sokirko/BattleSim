"""Проверка выгрузки позы: клипы KayKit, снятые через Blender, должны совпасть с исходными каналами GLB.

    GLB=путь/к/модели.glb python tools/blender/check_roundtrip.py
"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np
import rigio

rig = rigio.Rig(os.environ['GLB'])
for clip in ['Blocking', 'Walking_A', 'Lie_StandUp']:
    rig.use_action(clip)
    a = rig.g.anim(clip)
    worst_t = worst_r = 0
    for t in np.linspace(0, rig.g.duration(a), 9):
        f = t * rig.fps
        rig.scene.frame_set(int(np.floor(f)), subframe=float(f - np.floor(f)))
        mine = rig.local_trs(rig.pose_world())
        ref = rig.g.sample(a, t)
        for j in rig.joints:
            rt, rr, rs = rig.g.rest_trs(j)
            r = ref.get(j, {})
            worst_t = max(worst_t, np.abs(mine[j][0] - r.get('translation', rt)).max())
            worst_r = max(worst_r, 1 - abs(np.dot(mine[j][1], r.get('rotation', rr))))
    print(f'{clip}: перенос до {worst_t:.1e} м, поворот 1-|q·q| до {worst_r:.1e}')
