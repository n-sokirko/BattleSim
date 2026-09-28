"""
Свои меши, которых нет в KayKit, — дописываются узлами в исходные GLB (web/models/chars/*.glb):
  Bow — степной лук с загнутыми концами у Rogue_Hooded, в левой руке (handslot.l).
Геометрия — низкополигональная, с плоскими гранями, цвет — из атласа модели (UV в нужную область).
Крепление считается в Blender по позе Bow_Aim: лук стоит вертикально с лёгким наклоном, тетивой к стрелку.

После: python web/pack_models.py && python tools/blender/make_clips.py
"""
import math, os, runpy, sys
here = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, here)
import numpy as np
import glbkit

CHARS = os.path.join(here, '..', '..', 'web', 'models', 'chars')

# области атласа Rogue: светлое и тёмное дерево, бежевая нить
UV_WOOD, UV_GRIP, UV_STRING = (0.803, 0.390), (0.813, 0.455), (0.066, 0.629)


def box_strip(path, half_w, half_t, uv, tris):
    """Брусок вдоль ломаной path (точки (y, z)) в плоскости YZ: ширина по X, толщина по Z (в нормали к ломаной)."""
    rings = []
    for i, (y, z) in enumerate(path):
        a = path[max(0, i - 1)]; b = path[min(len(path) - 1, i + 1)]
        ty, tz = b[0] - a[0], b[1] - a[1]; l = math.hypot(ty, tz); ty, tz = ty / l, tz / l
        ny, nz = -tz, ty                                   # нормаль в плоскости YZ
        w, t = half_w[i], half_t[i]
        rings.append([(-w, y - ny * t, z - nz * t), (w, y - ny * t, z - nz * t), (w, y + ny * t, z + nz * t), (-w, y + ny * t, z + nz * t)])
    for i in range(len(rings) - 1):
        r0, r1 = rings[i], rings[i + 1]
        for k in range(4):
            a, b, c, d = r0[k], r0[(k + 1) % 4], r1[(k + 1) % 4], r1[k]
            tris.append((a, b, c, uv)); tris.append((a, c, d, uv))
    for r, flip in ((rings[0], True), (rings[-1], False)):   # торцы
        q = r[::-1] if flip else r
        tris.append((q[0], q[1], q[2], uv)); tris.append((q[0], q[2], q[3], uv))


def bow_geometry():
    """Лук в своих осях: Y — вдоль лука, рукоять в нуле, +Z — к цели, тетива сзади (-Z)."""
    K = 1.25  # кукольные пропорции KayKit: лук крупнее «настоящего», иначе издали его не видно
    half = [(y * K, z * K) for y, z in [(0.0, 0.0), (0.06, -0.005), (0.14, -0.03), (0.24, -0.075), (0.34, -0.125), (0.42, -0.155), (0.47, -0.15), (0.51, -0.115)]]
    path = [(-y, z) for y, z in half[::-1]] + half[1:]      # обе половины
    n = len(path)
    w = [0.03 if abs(y) < 0.55 else 0.022 for y, z in path]
    t = [0.02 + 0.016 * max(0, 1 - abs(y) / 0.35) for y, z in path]
    tris = []
    box_strip(path, w, t, UV_WOOD, tris)
    box_strip([(-0.09, 0.004), (0.09, 0.004)], [0.04, 0.04], [0.042, 0.042], UV_GRIP, tris)   # обмотка рукояти
    tip = path[-1]
    box_strip([(-tip[0], tip[1] - 0.01), (tip[0], tip[1] - 0.01)], [0.007, 0.007], [0.007, 0.007], UV_STRING, tris)  # тетива
    pos, nrm, uv, idx = [], [], [], []
    for a, b, c, u in tris:
        a, b, c = map(np.array, (a, b, c))
        nn = np.cross(b - a, c - a); l = np.linalg.norm(nn)
        if l < 1e-12: continue
        nn /= l
        for p in (a, b, c):
            idx.append(len(pos)); pos.append(p); nrm.append(nn); uv.append(u)
    return np.array(pos), np.array(nrm), np.array(uv), np.array(idx)


def aim_handslot_world(glb_path):
    """Мир handslot.l (glTF, пространство узла Rig) в позе Bow_Aim — через те же функции, что и клип."""
    os.environ['ONLY'] = 'none'
    g = runpy.run_path(os.path.join(here, 'make_clips.py'))
    G = g['bow_aim'].__globals__
    G['load']('Rogue_Hooded', glb_path)
    G['bow_aim'](0.0)
    rig = G['rig']
    return rig.pose_world()[rig.g.by_name['handslot.l']]


def add_mesh_node(g, name, parent, trs, geom, material):
    pos, nrm, uv, idx = geom
    acc = lambda a, typ: g._add_accessor(a, typ)
    pa = acc(pos, 'VEC3')
    g.j['accessors'][pa]['min'] = pos.min(0).tolist(); g.j['accessors'][pa]['max'] = pos.max(0).tolist()
    prim = {'attributes': {'POSITION': pa, 'NORMAL': acc(nrm, 'VEC3'), 'TEXCOORD_0': acc(uv, 'VEC2')}, 'material': material}
    ia = g._add_accessor(idx.astype(float), 'SCALAR')  # временно float — ниже переводим в uint16
    a = g.j['accessors'][ia]; bv = g.j['bufferViews'][a['bufferView']]
    raw = idx.astype('<u2').tobytes()
    g.bin[bv['byteOffset']:bv['byteOffset'] + bv['byteLength']] = raw + bytes(bv['byteLength'] - len(raw))
    bv['byteLength'] = len(raw); a['componentType'] = 5123
    prim['indices'] = ia
    t, r, s = trs
    node_fields = {'translation': [float(v) for v in t], 'rotation': [float(v) for v in r], 'scale': [1.0, 1.0, 1.0]}  # поворот и перенос; масштаб ровно 1
    if name in g.by_name:  # пересборка: меняем данные узла на месте (старые буферы уйдут при сжатии)
        node = g.nodes[g.by_name[name]]
        g.j['meshes'][node['mesh']]['primitives'] = [prim]
        node.update(node_fields)
        return
    g.j['meshes'].append({'name': name, 'primitives': [prim]})
    node = {'name': name, 'mesh': len(g.j['meshes']) - 1, **node_fields}
    g.nodes.append(node)
    g.nodes[g.by_name[parent]].setdefault('children', []).append(len(g.nodes) - 1)
    g.by_name[name] = len(g.nodes) - 1


def main():
    src = os.path.abspath(os.path.join(CHARS, 'Rogue_Hooded.glb'))
    hs = aim_handslot_world(src)
    # желаемая ориентация лука в мире: вертикально (Y), к цели (+Z), верх чуть к правой руке (наклон 12°)
    cant = math.radians(-12)
    want = np.eye(4)
    want[:3, :3] = glbkit.quat_to_mat((0, 0, math.sin(cant / 2), math.cos(cant / 2)))
    want[:3, 3] = hs[:3, 3]
    local = np.linalg.inv(hs) @ want
    g = glbkit.Glb(src)
    mat = g.j['meshes'][g.nodes[g.by_name['2H_Crossbow']]['mesh']]['primitives'][0]['material']
    add_mesh_node(g, 'Bow', 'handslot.l', glbkit.decompose(local), bow_geometry(), mat)
    g.save(src)
    print('saved', src, 'Bow:', len(bow_geometry()[3]) // 3, 'треугольников')


if __name__ == '__main__':
    main()
