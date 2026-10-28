import bpy, sys, os
from mathutils import Vector
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=r"O:\BattleSim\art\generated\hy_armed\kael_idle.fbx")
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
mesh = [o for o in bpy.data.objects if o.type == 'MESH'][0]
arm.animation_data.action = None
bpy.context.view_layer.update()
print("ARM scale", arm.scale, "mesh scale", mesh.scale, "parent", mesh.parent)
for n in ("Hips", "RightArm", "RightForeArm", "RightHand", "LeftHand", "RightUpLeg", "RightFoot"):
    b = arm.data.bones[n]
    print("BONE", n, [round(x, 3) for x in (arm.matrix_world @ b.head_local)], [round(x, 3) for x in (arm.matrix_world @ b.tail_local)])
mw = mesh.matrix_world
pts = [mw @ v.co for v in mesh.data.vertices]
print("VERTS", len(pts), "min", [round(min(p[i] for p in pts), 3) for i in range(3)], "max", [round(max(p[i] for p in pts), 3) for i in range(3)])
print("GROUPS", [g.name for g in mesh.vertex_groups][:40])
hand = arm.matrix_world @ arm.data.bones["RightHand"].head_local
cand = [p for p in pts if p.z < hand.z - 0.03 and abs(p.x - hand.x) < 0.5]
# распределение по высоте вокруг руки
import collections
hist = collections.Counter()
for p in pts:
    if p.x < hand.x + 0.08:
        hist[(round(p.x, 1), round(p.z, 1))] += 1
for k in sorted(hist):
    print("CELL", k, hist[k])
sw = [p for p in pts if p.y < -0.25]
print("SWORD n", len(sw), "min", [round(min(p[i] for p in sw), 3) for i in range(3)], "max", [round(max(p[i] for p in sw), 3) for i in range(3)])
sw2 = [p for p in pts if p.y < -0.25 or (p.x < -0.42)]
import collections
c2 = collections.Counter()
for p in pts:
    if p.x < -0.25 and p.y < 0.0 and p.z < 1.0:
        c2[(round(p.y, 1), round(p.z, 1))] += 1
for k in sorted(c2):
    print("SW", k, c2[k])
# веса вершин вблизи руки до фикса
vg = {g.index: g.name for g in mesh.vertex_groups}
import statistics
rows = collections.Counter()
for v in mesh.data.vertices:
    p = mw @ v.co
    if p.y < -0.25:
        top = max(v.groups, key=lambda g: g.weight)
        rows[vg[top.group]] += 1
print("BLADE top-group", dict(rows))
