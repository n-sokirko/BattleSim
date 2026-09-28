"""Blender <-> glTF: загрузка модели и выгрузка позы скелета в локальные TRS узлов исходного GLB."""
import numpy as np
import bpy
from mathutils import Matrix
import glbkit

# glTF (Y вверх) -> Blender (Z вверх): (x, y, z) -> (x, -z, y)
CV = np.array([[1, 0, 0, 0], [0, 0, -1, 0], [0, 1, 0, 0], [0, 0, 0, 1]], float)
CVI = np.linalg.inv(CV)


class Rig:
    def __init__(self, path):
        self.g = glbkit.Glb(path)
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.gltf(filepath=path, guess_original_bind_pose=False)
        self.arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
        self.scene = bpy.context.scene
        self.fps = self.scene.render.fps / self.scene.render.fps_base
        g = self.g
        self.joints = g.j['skins'][0]['joints']
        self.rig_node = g.parent[g.by_name['root']]
        # покой: мир узла в пространстве узла Rig
        self.rest_world = {}
        def walk(i, parent_m):
            t, r, s = g.rest_trs(i)
            m = parent_m @ glbkit.compose(t, r, s)
            self.rest_world[i] = m
            for c in g.nodes[i].get('children', []): walk(c, m)
        for c in g.nodes[self.rig_node].get('children', []): walk(c, np.eye(4))
        self.bone_rest = {b.name: np.array(b.matrix_local) for b in self.arm.data.bones}

    def use_action(self, name):
        act = bpy.data.actions[name]
        ad = self.arm.animation_data or self.arm.animation_data_create()
        ad.action = act
        if hasattr(ad, 'action_slot') and ad.action_slot is None and len(act.slots): ad.action_slot = act.slots[0]
        return act

    def pose_world(self):
        """Текущая поза (с учётом ограничений) -> мир каждого сустава в пространстве узла Rig (glTF)."""
        bpy.context.view_layer.update()
        out = {}
        for j in self.joints:
            name = self.g.nodes[j]['name']
            pb = self.arm.pose.bones.get(name)
            d = np.array(pb.matrix) @ np.linalg.inv(self.bone_rest[name])  # деформация в пространстве арматуры
            out[j] = (CVI @ d @ CV) @ self.rest_world[j]
        return out

    def local_trs(self, world):
        res = {}
        for j, m in world.items():
            p = self.g.parent.get(j)
            pm = world[p] if p in world else (np.eye(4) if p == self.rig_node else self.rest_world[p])
            res[j] = glbkit.decompose(np.linalg.inv(pm) @ m)
        return res

    def record(self, name, frames_fn, dur, fps=30):
        """frames_fn(t) выставляет позу на момент t; снимаем клип с шагом 1/fps и дописываем в GLB."""
        n = max(2, int(round(dur * fps)) + 1)
        times = [min(k / fps, dur) for k in range(n)]
        tr = {j: {'translation': [], 'rotation': [], 'scale': []} for j in self.joints}
        prev = {}
        for t in times:
            frames_fn(t)
            for j, (tt, rr, ss) in self.local_trs(self.pose_world()).items():
                if j in prev and np.dot(prev[j], rr) < 0: rr = -rr  # без перескоков кватерниона
                prev[j] = rr
                tr[j]['translation'].append(tt); tr[j]['rotation'].append(rr); tr[j]['scale'].append(ss)
        tracks = {}
        for j, d in tr.items():
            rt, rr, rs = self.g.rest_trs(j)
            rest = {'translation': rt, 'rotation': rr, 'scale': rs}
            keep = {}
            for k, v in d.items():
                v = np.array(v)
                if k == 'rotation':
                    same = np.all(1 - np.abs(v @ rest[k]) < 1e-9)
                else:
                    same = np.abs(v - rest[k]).max() < 1e-5
                if not same: keep[k] = v  # неизменные каналы (масштаб, перенос у большинства костей) не пишем
            if keep: tracks[j] = keep
        self.g.add_animation(name, times, tracks)
        return times, tracks
