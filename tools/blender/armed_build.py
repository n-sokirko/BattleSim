"""
Персонаж с оружием в руке (Hunyuan: меш + скелет + клипы из шаблонов) → один GLB со всеми клипами.
Каждый клип скачан отдельным FBX (меш + скелет + одно действие); берём меш/скелет из первого, действия из остальных.
Запуск: blender -b -P armed_build.py -- имя [имя ...]
Вход: art/generated/hy_armed/<имя>_<клип>.fbx; выход: godot/assets/hy/<имя>.glb
"""
import bpy, os, sys

IN = r"O:\BattleSim\art\generated\hy_armed"
OUT = r"O:\BattleSim\godot\assets\hy"
CLIPS = ["idle", "walk", "run", "atk_double", "atk_charge", "atk_cont", "hit", "death"]


def import_fbx(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    return [o for o in bpy.data.objects if o not in before]


def pin_blade(arm, mesh, bone="RightHand", ahead=0.12, side=0.1, cut=True):
    """Клинок и всё, что выступает вперёд от кисти, целиком привязываем к кисти: авто-веса гнут тонкий клинок зигзагом."""
    hand = arm.matrix_world @ arm.data.bones[bone].head_local
    mw = mesh.matrix_world
    grp = mesh.vertex_groups[bone]
    world = {v.index: mw @ v.co for v in mesh.data.vertices}
    # кончик клинка: самая «передняя» вершина среди далеко выступающих от кисти
    far = [p for p in world.values() if p.y < hand.y - 0.25 and abs(p.x - hand.x) < 0.15]
    tip = min(far, key=lambda p: p.y)
    axis = tip - hand
    length = axis.length
    axis.normalize()
    ids = []
    for i, p in world.items():
        t = (p - hand).dot(axis)
        if t < -0.03 or t > length + 0.03:
            continue
        d = ((p - hand) - axis * t).length
        if d < (0.10 if t < 0.2 else 0.05) and p.y < hand.y + 0.02:
            ids.append(i)
    if cut:
        # клинок и гарда удаляются совсем: в игре меч ставится отдельной моделью, в кисти остаётся кулак и рукоять
        import bmesh
        kill = [i for i, p in world.items()
                if p.y < hand.y - 0.10 and hand.x - 0.08 < p.x < hand.x + 0.045 and 0.25 < p.z < hand.z + 0.02]
        bm = bmesh.new()
        bm.from_mesh(mesh.data)
        bm.verts.ensure_lookup_table()
        bmesh.ops.delete(bm, geom=[bm.verts[i] for i in kill], context='VERTS')
        bm.to_mesh(mesh.data)
        bm.free()
        print("CUT", len(kill), "verts")
        return
    for g in mesh.vertex_groups:
        if g.name != bone:
            g.remove(ids)
    grp.add(ids, 1.0, 'REPLACE')
    print("PINNED", len(ids), "verts to", bone)


def run(name):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    first = None
    for clip in CLIPS:
        p = os.path.join(IN, f"{name}_{clip}.fbx")
        if not os.path.exists(p):
            print("SKIP", clip)
            continue
        new = import_fbx(p)
        arm = [o for o in new if o.type == 'ARMATURE'][0]
        act = arm.animation_data.action
        act.name = clip
        act.use_fake_user = True
        if first is None:
            first = arm
            first.animation_data.action = None
            pin_blade(first, [o for o in new if o.type == 'MESH'][0])
            continue
        arm.animation_data.action = None
        for o in new:
            bpy.data.objects.remove(o, do_unlink=True)
    for o in bpy.data.objects:
        o.select_set(o.type in ('ARMATURE', 'MESH'))
    bpy.context.view_layer.objects.active = first
    os.makedirs(OUT, exist_ok=True)
    out = os.path.join(OUT, f"armed_{name}.glb")
    bpy.ops.export_scene.gltf(filepath=out, export_format='GLB', use_selection=True,
                              export_animations=True, export_animation_mode='ACTIONS', export_skins=True,
                              export_yup=True, export_apply=False)
    print("EXPORTED", out, [a.name for a in bpy.data.actions])


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:]
    for n in argv:
        run(n)
