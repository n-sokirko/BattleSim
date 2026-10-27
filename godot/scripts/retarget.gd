extends SceneTree
## Перенос анимаций KayKit (мечи, топоры, арбалеты, смерть…) на скелеты Hunyuan: по кадрам берём глобальный поворот кости KayKit
## и ставим его на соответствующую кость Hunyuan (с поправкой на то, что у Hunyuan поза покоя — A, а у KayKit — T).
## Запуск: godot --headless --path . --script scripts/retarget.gd -- имя [имя ...]
## Результат: res://assets/hy/anims_<имя>.res (AnimationLibrary: имена клипов KayKit как есть).

const CLIPS := ["Idle", "2H_Melee_Idle", "Walking_A", "Running_A", "Running_B",
	"1H_Melee_Attack_Chop", "1H_Melee_Attack_Slice_Diagonal", "1H_Melee_Attack_Slice_Horizontal", "1H_Melee_Attack_Stab",
	"2H_Melee_Attack_Chop", "2H_Melee_Attack_Slice", "2H_Melee_Attack_Spin", "2H_Melee_Attack_Stab",
	"1H_Ranged_Shoot", "1H_Ranged_Aiming", "2H_Ranged_Shoot", "2H_Ranged_Aiming",
	"Dualwield_Melee_Attack_Slice", "Dualwield_Melee_Attack_Stab", "Dualwield_Melee_Attack_Chop",
	"Hit_A", "Hit_B", "Death_A", "Death_B", "Block", "Block_Hit", "Cheer", "Spellcast_Raise", "Spellcast_Shoot"]
const FPS := 30.0

# кость Hunyuan -> кость KayKit, с которой берём поворот
const MAP := {
	"Hips": "hips", "Spine": "spine", "Spine1": "chest", "Spine2": "chest", "Neck": "head", "Head": "head",
	"LeftShoulder": "chest", "LeftArm": "upperarm.l", "LeftForeArm": "lowerarm.l", "LeftHand": "hand.l",
	"RightShoulder": "chest", "RightArm": "upperarm.r", "RightForeArm": "lowerarm.r", "RightHand": "hand.r",
	"LeftUpLeg": "upperleg.l", "LeftLeg": "lowerleg.l", "LeftFoot": "foot.l", "LeftToeBase": "toes.l",
	"RightUpLeg": "upperleg.r", "RightLeg": "lowerleg.r", "RightFoot": "foot.r", "RightToeBase": "toes.r",
}


func _init() -> void:
	_main()


func _main() -> void:
	var names: PackedStringArray = []
	for a in OS.get_cmdline_user_args():
		if not a.begins_with("--"):
			names.append(a)
	if names.is_empty():
		names = PackedStringArray(["hero_kael", "erland_militia", "erland_archer", "nomad_raider", "nomad_archer", "nomad_khan"])
	for n in names:
		await _do(n)
	quit()


func _do(ch: String) -> void:
	var src = (load("res://assets/kk/Knight.glb") as PackedScene).instantiate()
	var dst = (load("res://assets/hy/%s.glb" % ch) as PackedScene).instantiate()
	get_root().add_child(src)
	get_root().add_child(dst)
	var ska := src.find_child("Skeleton3D", true, false) as Skeleton3D
	var skb := dst.find_child("Skeleton3D", true, false) as Skeleton3D
	var apa := src.find_child("AnimationPlayer", true, false) as AnimationPlayer
	var prefix := String(dst.get_path_to(skb))   # путь к скелету от корня модели; AnimationPlayer в игре — прямой потомок корня
	var order: Array[int] = []
	var parent_of := {}
	for i in skb.get_bone_count():
		parent_of[i] = skb.get_bone_parent(i)
		order.append(i)
	var a_rest_g := {}
	for n in MAP.values():
		var j := ska.find_bone(n)
		a_rest_g[n] = ska.get_bone_global_rest(j).basis.orthonormalized()
	var b_rest_g := {}
	for i in skb.get_bone_count():
		b_rest_g[i] = skb.get_bone_global_rest(i).basis.orthonormalized()
	var a_hip_rest := ska.get_bone_global_rest(ska.find_bone("hips")).origin
	var b_hip_i := skb.find_bone("Hips")
	var b_hip_rest := skb.get_bone_global_rest(b_hip_i).origin
	var kscale := b_hip_rest.y / maxf(a_hip_rest.y, 0.001)
	var lib := AnimationLibrary.new()
	for c in CLIPS:
		if not apa.has_animation(c):
			print("нет клипа ", c)
			continue
		var anim_a := apa.get_animation(c)
		var length := anim_a.length
		var nf := int(ceil(length * FPS)) + 1
		var out := Animation.new()
		out.length = length
		out.loop_mode = Animation.LOOP_NONE
		var rot_tracks := {}
		for i in skb.get_bone_count():
			var t := out.add_track(Animation.TYPE_ROTATION_3D)
			out.track_set_path(t, NodePath("%s:%s" % [prefix, skb.get_bone_name(i)]))
			rot_tracks[i] = t
		var pos_track := out.add_track(Animation.TYPE_POSITION_3D)
		out.track_set_path(pos_track, NodePath("%s:Hips" % prefix))
		apa.play(c)
		for f in nf:
			var time := minf(f / FPS, length)
			apa.seek(time, true)
			apa.pause()
			await process_frame
			var gpose := {}   # глобальный поворот Hunyuan в этом кадре
			for i in order:
				var tgt: Basis
				var a_name: String = MAP.get(skb.get_bone_name(i), "")
				var par: int = parent_of[i]
				if a_name != "":
					var a_pose := ska.get_bone_global_pose(ska.find_bone(a_name)).basis.orthonormalized()
					var ra: Basis = a_rest_g[a_name]
					var rb: Basis = b_rest_g[i]
					# оба скелета в T-позе: перенос глобальной дельты

					tgt = a_pose * (ra.inverse() * rb)
				elif par >= 0:
					# вне соответствия (кончики): остаются как в покое относительно родителя
					var rp: Basis = b_rest_g[par]
					tgt = (gpose[par] as Basis) * (rp.inverse() * (b_rest_g[i] as Basis))
				else:
					tgt = b_rest_g[i]
				gpose[i] = tgt
				var local: Basis = tgt
				if par >= 0:
					local = (gpose[par] as Basis).inverse() * tgt
				out.rotation_track_insert_key(rot_tracks[i], time, local.get_rotation_quaternion())
			# смещение бёдер относительно покоя, с поправкой на рост
			var hp := ska.get_bone_global_pose(ska.find_bone("hips")).origin
			var d := (hp - a_hip_rest) * kscale
			out.position_track_insert_key(pos_track, time, skb.get_bone_rest(b_hip_i).origin + d)
		lib.add_animation(c, out)
	var path := "res://assets/hy/anims_%s.res" % ch
	ResourceSaver.save(lib, path)
	print("SAVED ", path, " clips ", lib.get_animation_list().size())
	src.queue_free()
	dst.queue_free()
