extends SceneTree
## Перенос клипов Mixamo (res://assets/mx/<пак>.glb, скелет mixamorig, T-поза) на скелет Hunyuan в A-позе.
## Кости Hunyuan называются так же, как у Mixamo (Hips, Spine, LeftArm…), так что соответствие по именам.
## Все вращения считаются в общем (мировом) пространстве: у скелетов разная ориентация и масштаб родительских узлов.
## Запуск: godot --headless --path . --script scripts/retarget_mx.gd -- <пак> <персонаж> [клип ...]
## Результат: res://assets/hy/mx_<пак>_<персонаж>.res (AnimationLibrary; имена клипов как в паке).

const FPS := 30.0
const BONES := ["Hips", "Spine", "Spine1", "Spine2", "Neck", "Head", "LeftShoulder", "LeftArm", "LeftForeArm", "LeftHand",
	"RightShoulder", "RightArm", "RightForeArm", "RightHand", "LeftUpLeg", "LeftLeg", "LeftFoot", "LeftToeBase",
	"RightUpLeg", "RightLeg", "RightFoot", "RightToeBase"]


func _init() -> void:
	_main()


func _main() -> void:
	var args: PackedStringArray = []
	for a in OS.get_cmdline_user_args():
		if not a.begins_with("--"):
			args.append(a)
	await _do(args[0], args[1], args.slice(2))
	quit()


func _find(sk: Skeleton3D, n: String) -> int:
	for cand in ["mixamorig_" + n, "mixamorig:" + n, n]:
		var i := sk.find_bone(cand)
		if i >= 0:
			return i
	return -1


func _do(pack: String, ch: String, only: PackedStringArray) -> void:
	var src = (load("res://assets/mx/%s.glb" % pack) as PackedScene).instantiate()
	var dst = (load("res://assets/hy/%s.glb" % ch) as PackedScene).instantiate()
	get_root().add_child(src)
	get_root().add_child(dst)
	await process_frame
	await process_frame
	var ska := src.find_child("Skeleton3D", true, false) as Skeleton3D
	var skb := dst.find_child("Skeleton3D", true, false) as Skeleton3D
	var apa := src.find_child("AnimationPlayer", true, false) as AnimationPlayer
	var prefix := String(dst.get_path_to(skb))
	var nb := skb.get_bone_count()
	var Rs := ska.global_transform.basis.orthonormalized()
	var Rd := skb.global_transform.basis.orthonormalized()
	var Rd_inv := Rd.inverse()
	var a_idx := {}
	var a_rest_w := {}
	for n in BONES:
		var j := _find(ska, n)
		var i := skb.find_bone(n)
		if j < 0 or i < 0:
			print("нет кости ", n, " ", j, " ", i)
			continue
		a_idx[i] = j
		a_rest_w[i] = Rs * ska.get_bone_global_rest(j).basis.orthonormalized()
	var b_rest_w := {}
	for i in nb:
		b_rest_w[i] = Rd * skb.get_bone_global_rest(i).basis.orthonormalized()
	var b_rest_t := b_rest_w.duplicate()
	for side in ["Left", "Right"]:
		var tgt := Vector3(1.0 if side == "Left" else -1.0, 0, 0)
		var chain := [side + "Arm", side + "ForeArm", side + "Hand"]
		var q_fore := Basis.IDENTITY
		for k in 2:
			var i := skb.find_bone(chain[k])
			var c := skb.find_bone(chain[k + 1])
			var pi := skb.global_transform * skb.get_bone_global_rest(i).origin
			var pc := skb.global_transform * skb.get_bone_global_rest(c).origin
			var q := Basis(Quaternion((pc - pi).normalized(), tgt))
			b_rest_t[i] = q * (b_rest_w[i] as Basis)
			q_fore = q
		var hi := skb.find_bone(chain[2])
		b_rest_t[hi] = q_fore * (b_rest_w[hi] as Basis)
	var hips_a := _find(ska, "Hips")
	var hips_b := skb.find_bone("Hips")
	var a_hip_w := ska.global_transform * ska.get_bone_global_rest(hips_a).origin
	var b_hip_w := skb.global_transform * skb.get_bone_global_rest(hips_b).origin
	var kscale := b_hip_w.y / maxf(a_hip_w.y, 0.001)
	var lib := AnimationLibrary.new()
	var clips: Array = []
	for n in apa.get_animation_list():
		if only.is_empty() or n in only:
			clips.append(n)
	for c in clips:
		var length := apa.get_animation(c).length
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
		var hip0 := Vector3.ZERO
		for f in nf:
			var time := minf(f / FPS, length)
			apa.seek(time, true)
			apa.pause()
			await process_frame
			var gpose := {}   # глобальный поворот Hunyuan в мировом пространстве
			for i in nb:
				var tgt_w: Basis
				var par := skb.get_bone_parent(i)
				if a_idx.has(i):
					var a_pose := Rs * ska.get_bone_global_pose(a_idx[i]).basis.orthonormalized()
					tgt_w = a_pose * ((a_rest_w[i] as Basis).inverse() * (b_rest_t[i] as Basis))
				elif par >= 0:
					tgt_w = (gpose[par] as Basis) * ((b_rest_w[par] as Basis).inverse() * (b_rest_w[i] as Basis))
				else:
					tgt_w = b_rest_w[i]
				gpose[i] = tgt_w
				var local: Basis
				if par >= 0:
					local = ((gpose[par] as Basis).inverse() * tgt_w)
				else:
					local = Rd_inv * tgt_w
				out.rotation_track_insert_key(rot_tracks[i], time, local.get_rotation_quaternion())
			var hp := ska.global_transform * ska.get_bone_global_pose(hips_a).origin
			if f == 0:
				hip0 = hp
			# смещение бёдер в мире: по высоте как есть, по горизонтали ослаблено, чтобы клип не уезжал
			var dw := Vector3((hp.x - hip0.x) * 0.35, hp.y - a_hip_w.y, (hp.z - hip0.z) * 0.35) * kscale
			var dl := skb.global_transform.basis.inverse() * dw
			out.position_track_insert_key(pos_track, time, skb.get_bone_rest(hips_b).origin + dl)
		out.loop_mode = Animation.LOOP_NONE
		lib.add_animation(c, out)
	var path := "res://assets/hy/mx_%s_%s.res" % [pack, ch]
	ResourceSaver.save(lib, path)
	print("SAVED ", path, " clips ", lib.get_animation_list().size())
	src.queue_free()
	dst.queue_free()
