import bpy, sys, math
sys.path.insert(0, r"O:\BattleSim\tools\blender")
import charkit as ck
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=r"O:\BattleSim\godot\assets\kk\Knight.glb")
arm = [o for o in bpy.data.objects if o.type=='ARMATURE'][0]
keep = {"1H_Sword","Round_Shield","Knight_ArmLeft","Knight_ArmRight","Knight_Body","Knight_Head","Knight_Helmet","Knight_LegLeft","Knight_LegRight","Knight_Cape"}
for o in list(bpy.data.objects):
    if o.type=='MESH' and o.name not in keep: bpy.data.objects.remove(o)
ck.PREV = r"O:\BattleSim\art\previews"
ck.preview(arm, "kk_knight", "Idle", 4.2, 1.2)
