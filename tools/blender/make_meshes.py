"""
Свои меши, которых нет в KayKit, — дописываются узлами в исходные GLB (web/models/chars/*.glb):
  Bow — степной лук с загнутыми концами у Rogue_Hooded, в левой руке (handslot.l);
  Orc_Tusks, Orc_Ears — клыки из нижней челюсти и острые уши у Barbarian и Rogue_Hooded (на кости head);
    уши берут цвет из клетки кожи атласа — у Орды она зелёная, как и лицо.
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


# клетки атласа 8×4: кожа (0,0) — её перекрашивает раса; светлая кость — не командная и не кожа
UV_SKIN = (0.0625, 0.125)
UV_BONE = {'Barbarian': (0.1875, 0.875), 'Rogue_Hooded': (0.0625, 0.625)}


def to_arrays(tris):
    pos, nrm, uv, idx = [], [], [], []
    for a, b, c, u in tris:
        a, b, c = map(np.array, (a, b, c))
        nn = np.cross(b - a, c - a); l = np.linalg.norm(nn)
        if l < 1e-12: continue
        nn /= l
        for p in (a, b, c):
            idx.append(len(pos)); pos.append(p); nrm.append(nn); uv.append(u)
    return np.array(pos), np.array(nrm), np.array(uv), np.array(idx)


def frame(d):
    d = np.array(d, float); d /= np.linalg.norm(d)
    a = np.array([0, 1, 0]) if abs(d[1]) < 0.9 else np.array([1, 0, 0])
    u = np.cross(d, a); u /= np.linalg.norm(u); v = np.cross(d, u)
    return d, u, v


def cone(tris, base, d, r, length, uv, sides=5, flat=1.0):
    """Конус от base вдоль d; flat < 1 — сплюснут (ухо)."""
    d, u, v = frame(d); base = np.array(base, float); tip = base + d * length
    ring = [base + (u * np.cos(2 * np.pi * k / sides) + v * np.sin(2 * np.pi * k / sides) * flat) * r for k in range(sides)]
    for k in range(sides):
        a, b = ring[k], ring[(k + 1) % sides]
        tris.append((a, b, tip, uv)); tris.append((b, a, base, uv))


def rest_world(g):
    out = {}
    def walk(i, pm):
        t, r, s = g.rest_trs(i); m = pm @ glbkit.compose(t, r, s); out[i] = m
        for c in g.nodes[i].get('children', []): walk(c, m)
    roots = set(range(len(g.nodes))) - set(g.parent)
    for i in roots: walk(i, np.eye(4))
    return out


def head_parts(g, model):
    """Клыки и уши по геометрии головы в позе покоя (glTF: Y вверх, +Z вперёд, +X — левая сторона)."""
    head = [n for n in g.nodes if n.get('mesh') is not None and n.get('name', '').endswith(('_Head', '_Head_Hooded'))][0]
    P = np.concatenate([g.acc(p['attributes']['POSITION']) for p in g.j['meshes'][head['mesh']]['primitives']])
    y0, y1 = P[:, 1].min(), P[:, 1].max(); h = y1 - y0
    # рот: передний край лица в нижней трети головы
    band = P[(P[:, 1] > y0 + 0.12 * h) & (P[:, 1] < y0 + 0.3 * h) & (np.abs(P[:, 0]) < 0.25 * h)]
    zf, ym = band[:, 2].max(), y0 + 0.2 * h
    tusks = []
    for sx in (1, -1):
        cone(tusks, (sx * 0.1 * h, ym, zf - 0.03 * h), (sx * 0.25, 1, 0.35), 0.045 * h, 0.17 * h, UV_BONE[model], sides=5)
    # уши: по бокам на высоте глаз
    ye = y0 + 0.45 * h
    side = P[np.abs(P[:, 1] - ye) < 0.08 * h]
    xs = np.abs(side[:, 0]).max()
    ears = []
    for sx in (1, -1):
        cone(ears, (sx * (xs - 0.04 * h), ye, -0.02 * h), (sx * 1, 0.55, -0.35), 0.1 * h, 0.42 * h, UV_SKIN, sides=4, flat=0.35)
    return to_arrays(tusks), to_arrays(ears)


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
    node_fields = {'translation': [float(v) for v in t], 'rotation': [float(v) for v in r], 'scale': [float(round(v, 4)) for v in s]}  # у лука масштаб 1; у частей головы — обратный масштабу кости
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


def add_head_parts(model):
    src = os.path.abspath(os.path.join(CHARS, model + '.glb'))
    g = glbkit.Glb(src)
    rw = rest_world(g)
    local = np.linalg.inv(rw[g.by_name['head']])  # геометрия задана в пространстве модели (поза покоя)
    tusks, ears = head_parts(g, model)
    mat = g.j['meshes'][[n for n in g.nodes if n.get('name', '').endswith(('_Head', '_Head_Hooded'))][0]['mesh']]['primitives'][0].get('material', 0)
    add_mesh_node(g, 'Orc_Tusks', 'head', glbkit.decompose(local), tusks, mat)
    add_mesh_node(g, 'Orc_Ears', 'head', glbkit.decompose(local), ears, mat)
    g.save(src)
    print('saved', src, 'Orc_Tusks/Orc_Ears')


def main():
    for model in ('Barbarian', 'Rogue_Hooded'): add_head_parts(model)
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
