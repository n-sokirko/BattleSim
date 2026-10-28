extends SceneTree
## Перенос клипов KayKit на скелет Hunyuan, который остаётся в A-позе (руки опущены): для рук и предплечий
## покой пересчитывается так, будто они подняты горизонтально (как у KayKit), и глобальная дельта переносится на него.
## Запуск: godot --headless --path . --script scripts/retarget_a.gd -- имя [имя ...]   (res://assets/hy/<имя>.glb)
## Результат: res://assets/hy/anims_<имя>.res (AnimationLibrary с именами клипов KayKit).

const CLIPS := ["1H_Melee_Attack_Slice_Diagonal", "1H_Melee_Attack_Slice_Horizontal", "1H_Melee_Attack_Chop", "1H_Melee_Attack_Stab",
	"Block", "Block_Hit", "Cheer", "Spellcast_Raise", "Spellcast_Shoot", "1H_Ranged_Shoot", "2H_Ranged_Shoot", "2H_Melee_Attack_Chop"]
const FPS := 30.0

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
	var prefix := String(dst.get_path_to(skb))
	var nb := skb.get_bone_count()
	var a_rest_g := {}
	for n in MAP.values():
		a_rest_g[n] = ska.get_bone_global_rest(ska.find_bone(n)).basis.orthonormalized()
	var b_rest_g := {}
	for i in nb:
		b_rest_g[i] = skb.get_bone_global_rest(i).basis.orthonormalized()
	# покой Hunyuan в «T-позе»: руки и предплечья довёрнуты горизонтально (Left → +X, Right → −X)
	var b_rest_t := b_rest_g.duplicate()
	for side in ["Left", "Right"]:
		var sg := 1.0 if side == "Left" else -1.0
		var tgt := Vector3(sg, 0, 0)
		var chain := [side + "Arm", side + "ForeArm", side + "Hand"]
		var q_fore := Basis.IDENTITY
		for k in 2:
			var i := skb.find_bone(chain[k])
			var c := skb.find_bone(chain[k + 1])
			var d := (skb.get_bone_global_rest(c).origin - skb.get_bone_global_rest(i).origin).normalized()
			var q := Basis(Quaternion(d, tgt))
			b_rest_t[i] = q * (b_rest_g[i] as Basis)
			q_fore = q
		var hi := skb.find_bone(chain[2])
		b_rest_t[hi] = q_fore * (b_rest_g[hi] as Basis)
	var a_hip_rest := ska.get_bone_global_rest(ska.find_bone("hips")).origin
	var b_hip_i := skb.find_bone("Hips")
	var kscale := skb.get_bone_global_rest(b_hip_i).origin.y / maxf(a_hip_rest.y, 0.001)
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
		var rot_tracks := {}
		for i in nb:
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
			var gpose := {}
			for i in nb:
				var tgt_b: Basis
				var a_name: String = MAP.get(skb.get_bone_name(i), "")
				var par := skb.get_bone_parent(i)
				if a_name != "":
					var a_pose := ska.get_bone_global_pose(ska.find_bone(a_name)).basis.orthonormalized()
					tgt_b = a_pose * ((a_rest_g[a_name] as Basis).inverse() * (b_rest_t[i] as Basis))
				elif par >= 0:
					tgt_b = (gpose[par] as Basis) * ((b_rest_g[par] as Basis).inverse() * (b_rest_g[i] as Basis))
				else:
					tgt_b = b_rest_g[i]
				gpose[i] = tgt_b
				var local: Basis = tgt_b
				if par >= 0:
					local = (gpose[par] as Basis).inverse() * tgt_b
				out.rotation_track_insert_key(rot_tracks[i], time, local.get_rotation_quaternion())
			var hp := ska.get_bone_global_pose(ska.find_bone("hips")).origin
			out.position_track_insert_key(pos_track, time, skb.get_bone_rest(b_hip_i).origin + (hp - a_hip_rest) * kscale)
		lib.add_animation(c, out)
	var path := "res://assets/hy/anims_%s.res" % ch
	ResourceSaver.save(lib, path)
	print("SAVED ", path, " clips ", lib.get_animation_list().size())
	src.queue_free()
	dst.queue_free()
