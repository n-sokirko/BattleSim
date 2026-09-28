"""Мини-инструменты GLB: чтение узлов и анимаций, сэмплирование, дописывание новых клипов."""
import json, struct
import numpy as np

COMP = {5126: ('f', 4), 5123: ('H', 2), 5121: ('B', 1), 5125: ('I', 4)}
NCOMP = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}


def quat_to_mat(q):
    x, y, z, w = q
    return np.array([
        [1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
        [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
        [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


def mat_to_quat(m):
    t = m[0, 0] + m[1, 1] + m[2, 2]
    if t > 0:
        s = np.sqrt(t + 1.0) * 2
        w = 0.25 * s; x = (m[2, 1] - m[1, 2]) / s; y = (m[0, 2] - m[2, 0]) / s; z = (m[1, 0] - m[0, 1]) / s
    elif m[0, 0] > m[1, 1] and m[0, 0] > m[2, 2]:
        s = np.sqrt(1.0 + m[0, 0] - m[1, 1] - m[2, 2]) * 2
        w = (m[2, 1] - m[1, 2]) / s; x = 0.25 * s; y = (m[0, 1] + m[1, 0]) / s; z = (m[0, 2] + m[2, 0]) / s
    elif m[1, 1] > m[2, 2]:
        s = np.sqrt(1.0 + m[1, 1] - m[0, 0] - m[2, 2]) * 2
        w = (m[0, 2] - m[2, 0]) / s; x = (m[0, 1] + m[1, 0]) / s; y = 0.25 * s; z = (m[1, 2] + m[2, 1]) / s
    else:
        s = np.sqrt(1.0 + m[2, 2] - m[0, 0] - m[1, 1]) * 2
        w = (m[1, 0] - m[0, 1]) / s; x = (m[0, 2] + m[2, 0]) / s; y = (m[1, 2] + m[2, 1]) / s; z = 0.25 * s
    q = np.array([x, y, z, w])
    return q / np.linalg.norm(q)


def compose(t, r, s):
    m = np.eye(4)
    m[:3, :3] = quat_to_mat(r) * np.array(s)[None, :]
    m[:3, 3] = t
    return m


def decompose(m):
    t = m[:3, 3].copy()
    a = m[:3, :3]
    s = np.linalg.norm(a, axis=0)
    r = mat_to_quat(a / s[None, :])
    return t, r, s


def slerp(a, b, u):
    d = float(np.dot(a, b))
    if d < 0: b = -b; d = -d
    if d > 0.9995:
        q = a + (b - a) * u
    else:
        th = np.arccos(d)
        q = (np.sin((1 - u) * th) * a + np.sin(u * th) * b) / np.sin(th)
    return q / np.linalg.norm(q)


class Glb:
    def __init__(self, path):
        b = open(path, 'rb').read()
        assert b[:4] == b'glTF'
        jl = struct.unpack('<I', b[12:16])[0]
        self.j = json.loads(b[20:20 + jl])
        o = 20 + jl
        bl = struct.unpack('<I', b[o:o + 4])[0]
        self.bin = bytearray(b[o + 8:o + 8 + bl])
        self.nodes = self.j['nodes']
        self.parent = {}
        for i, n in enumerate(self.nodes):
            for c in n.get('children', []): self.parent[c] = i
        self.by_name = {n.get('name'): i for i, n in enumerate(self.nodes)}

    def acc(self, i):
        a = self.j['accessors'][i]
        bv = self.j['bufferViews'][a['bufferView']]
        fmt, size = COMP[a['componentType']]
        n = NCOMP[a['type']]
        off = bv.get('byteOffset', 0) + a.get('byteOffset', 0)
        stride = bv.get('byteStride', size * n)
        out = np.zeros((a['count'], n))
        for k in range(a['count']):
            out[k] = struct.unpack_from('<' + fmt * n, self.bin, off + k * stride)
        return out if n > 1 else out[:, 0]

    def rest_trs(self, i):
        n = self.nodes[i]
        if 'matrix' in n:
            return decompose(np.array(n['matrix']).reshape(4, 4).T)
        return (np.array(n.get('translation', [0, 0, 0]), float), np.array(n.get('rotation', [0, 0, 0, 1]), float),
                np.array(n.get('scale', [1, 1, 1]), float))

    def anim(self, name):
        for a in self.j.get('animations', []):
            if a.get('name') == name: return a
        return None

    def sample(self, anim, t):
        """Локальные TRS узлов в момент t (только анимированные каналы; остальные — покой)."""
        res = {}
        for ch in anim['channels']:
            node, path = ch['target']['node'], ch['target']['path']
            sm = anim['samplers'][ch['sampler']]
            times = self.acc(sm['input']); vals = self.acc(sm['output'])
            if t <= times[0]: v = vals[0]
            elif t >= times[-1]: v = vals[-1]
            else:
                k = np.searchsorted(times, t) - 1
                u = (t - times[k]) / (times[k + 1] - times[k])
                if sm.get('interpolation') == 'STEP': v = vals[k]
                elif path == 'rotation': v = slerp(vals[k], vals[k + 1], u)
                else: v = vals[k] + (vals[k + 1] - vals[k]) * u
            res.setdefault(node, {})[path] = np.array(v, float)
        return res

    def duration(self, anim):
        return max(self.acc(anim['samplers'][c['sampler']]['input'])[-1] for c in anim['channels'])

    def _add_accessor(self, arr, typ, minmax=False, comp=5126):
        arr = np.asarray(arr, dtype={5126: '<f4', 5123: '<u2', 5121: 'u1', 5125: '<u4'}[comp])
        while len(self.bin) % 4: self.bin.append(0)
        off = len(self.bin)
        self.bin += arr.tobytes()
        self.j['bufferViews'].append({'buffer': 0, 'byteOffset': off, 'byteLength': arr.nbytes})
        a = {'bufferView': len(self.j['bufferViews']) - 1, 'componentType': comp, 'count': int(arr.shape[0]), 'type': typ}
        if minmax:
            a['min'] = [float(arr.min())]; a['max'] = [float(arr.max())]
        self.j['accessors'].append(a)
        return len(self.j['accessors']) - 1

    def add_animation(self, name, times, tracks):
        """tracks: {node: {'translation': (F,3), 'rotation': (F,4), 'scale': (F,3)}}"""
        self.j['animations'] = [a for a in self.j.get('animations', []) if a.get('name') != name]
        ti = self._add_accessor(np.asarray(times, float), 'SCALAR', True)
        an = {'name': name, 'channels': [], 'samplers': []}
        for node, paths in sorted(tracks.items()):
            for path, vals in paths.items():
                oi = self._add_accessor(vals, {'translation': 'VEC3', 'rotation': 'VEC4', 'scale': 'VEC3'}[path])
                an['samplers'].append({'input': ti, 'output': oi, 'interpolation': 'LINEAR'})
                an['channels'].append({'sampler': len(an['samplers']) - 1, 'target': {'node': node, 'path': path}})
        self.j['animations'].append(an)

    def compact(self):
        """Выбросить данные, на которые больше никто не ссылается (старые версии заменённых клипов)."""
        j = self.j
        used = set()
        for m in j.get('meshes', []):
            for p in m['primitives']:
                used.update(p['attributes'].values())
                if 'indices' in p: used.add(p['indices'])
                for t in p.get('targets', []): used.update(t.values())
        for s in j.get('skins', []):
            if 'inverseBindMatrices' in s: used.add(s['inverseBindMatrices'])
        for a in j.get('animations', []):
            for sm in a['samplers']: used.update((sm['input'], sm['output']))
        if len(used) == len(j['accessors']): return
        acc_map = {old: new for new, old in enumerate(sorted(used))}
        accessors = [j['accessors'][i] for i in sorted(used)]
        views = sorted({a['bufferView'] for a in accessors if 'bufferView' in a} |
                       {im['bufferView'] for im in j.get('images', []) if 'bufferView' in im})
        view_map, new_views, new_bin = {}, [], bytearray()
        for old in views:
            bv = dict(j['bufferViews'][old])
            while len(new_bin) % 4: new_bin.append(0)
            o = bv.get('byteOffset', 0)
            chunk = self.bin[o:o + bv['byteLength']]
            bv['byteOffset'] = len(new_bin)
            new_bin += chunk
            view_map[old] = len(new_views); new_views.append(bv)
        for a in accessors:
            if 'bufferView' in a: a['bufferView'] = view_map[a['bufferView']]
        for im in j.get('images', []):
            if 'bufferView' in im: im['bufferView'] = view_map[im['bufferView']]
        for m in j.get('meshes', []):
            for p in m['primitives']:
                p['attributes'] = {k: acc_map[v] for k, v in p['attributes'].items()}
                if 'indices' in p: p['indices'] = acc_map[p['indices']]
                if 'targets' in p: p['targets'] = [{k: acc_map[v] for k, v in t.items()} for t in p['targets']]
        for s in j.get('skins', []):
            if 'inverseBindMatrices' in s: s['inverseBindMatrices'] = acc_map[s['inverseBindMatrices']]
        for a in j.get('animations', []):
            for sm in a['samplers']:
                sm['input'] = acc_map[sm['input']]; sm['output'] = acc_map[sm['output']]
        j['accessors'], j['bufferViews'], self.bin = accessors, new_views, new_bin

    def save(self, path):
        self.compact()
        while len(self.bin) % 4: self.bin.append(0)
        self.j['buffers'][0]['byteLength'] = len(self.bin)
        js = json.dumps(self.j, separators=(',', ':')).encode()
        while len(js) % 4: js += b' '
        total = 12 + 8 + len(js) + 8 + len(self.bin)
        with open(path, 'wb') as f:
            f.write(struct.pack('<III', 0x46546C67, 2, total))
            f.write(struct.pack('<II', len(js), 0x4E4F534A)); f.write(js)
            f.write(struct.pack('<II', len(self.bin), 0x004E4942)); f.write(bytes(self.bin))
