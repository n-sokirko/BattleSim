extends Node3D
## Стенд подбора хвата: персонаж в позе из клипа, предмет в кости руки повёрнут 24 способами; снимается крупно у кисти.
## Запуск: godot --path . res://scenes/grip_test.tscn -- --char=erland_militia --clip=mx_pro_sns/sword_and_shield_idle --t=0.3 --hand=RightHand --prop=sword_short [--other=shield_rect --otherrot=0,0,0]
var combos: Array = []


func _ready() -> void:
	var ch := "erland_militia"
	var clip := "mx_pro_sns/sword_and_shield_idle"
	var tt := 0.3
	var hand := "RightHand"
	var prop_name := "sword_short"
	var other := ""
	var other_rot := Vector3.ZERO
	var side := 1.0
	var cam_off := Vector3(0.9, 0.7, 2.1)
	var tag := ""
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--char="): ch = a.substr(7)
		if a.begins_with("--clip="): clip = a.substr(7)
		if a.begins_with("--t="): tt = float(a.substr(4))
		if a.begins_with("--hand="): hand = a.substr(7)
		if a.begins_with("--prop="): prop_name = a.substr(7)
		if a.begins_with("--other="): other = a.substr(8)
		if a.begins_with("--otherrot="):
			var p := a.substr(11).split(",")
			other_rot = Vector3(float(p[0]), float(p[1]), float(p[2]))
		if a.begins_with("--side="): side = float(a.substr(7))
		if a.begins_with("--cam="):
			var q := a.substr(6).split(",")
			cam_off = Vector3(float(q[0]), float(q[1]), float(q[2]))
		if a.begins_with("--tag="): tag = a.substr(6)
	var other_hand := "LeftHand" if hand == "RightHand" else "RightHand"
	var env := WorldEnvironment.new()
	var e := Environment.new()
	e.background_mode = Environment.BG_COLOR
	e.background_color = Color(0.4, 0.45, 0.5)
	e.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	e.ambient_light_color = Color(0.85, 0.85, 0.9)
	add_child(env)
	env.environment = e
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-50, 20, 0)
	add_child(sun)
	var seen := {}
	for x in 4:
		for y in 4:
			for z in 4:
				var b := Basis.from_euler(Vector3(deg_to_rad(x * 90), deg_to_rad(y * 90), deg_to_rad(z * 90)))
				var key := ""
				for r in 3:
					for c in 3:
						key += str(roundi(b[c][r])) + ","
				if not seen.has(key):
					seen[key] = true
					combos.append(Vector3(x * 90, y * 90, z * 90))
	var m = (load("res://assets/hy/%s.glb" % ch) as PackedScene).instantiate()
	add_child(m)
	var ap := AnimationPlayer.new()
	m.add_child(ap)
	for pk in ["pro_sns", "great_sword", "longbow", "rifle"]:
		if ResourceLoader.exists("res://assets/hy/mx_%s_%s.res" % [pk, ch]):
			ap.add_animation_library("mx_" + pk, load("res://assets/hy/mx_%s_%s.res" % [pk, ch]))
	var skel := m.find_child("Skeleton3D", true, false) as Skeleton3D
	if other != "":
		_attach(skel, m, other_hand, other, other_rot)
	var cam := Camera3D.new()
	cam.fov = 32.0
	add_child(cam)
	var tile := 250
	var sheet := Image.create(tile * 6, tile * 4, false, Image.FORMAT_RGBA8)
	await get_tree().process_frame
	var bi := skel.find_bone(hand)
	var att: BoneAttachment3D = null
	for i in combos.size():
		if att != null:
			att.queue_free()
		att = _attach(skel, m, hand, prop_name, combos[i])
		ap.play(clip)
		ap.seek(tt, true)
		ap.pause()
		await get_tree().process_frame
		await get_tree().process_frame
		var hp := skel.global_transform * skel.get_bone_global_pose(bi).origin
		cam.position = hp + cam_off * Vector3(side, 1, 1)
		cam.look_at(hp, Vector3.UP)
		await get_tree().process_frame
		await get_tree().process_frame
		var img := get_viewport().get_texture().get_image()
		img.convert(Image.FORMAT_RGBA8)
		var sx := img.get_width()
		var sy := img.get_height()
		var s := mini(sx, sy)
		var crop := img.get_region(Rect2i((sx - s) / 2, (sy - s) / 2, s, s))
		crop.resize(tile, tile)
		# номер варианта в углу
		sheet.blit_rect(crop, Rect2i(0, 0, tile, tile), Vector2i((i % 6) * tile, (i / 6) * tile))
		print("TILE ", i, " ", combos[i])
	sheet.save_png("O:/BattleSim/art/previews/grip_%s_%s%s.png" % [ch, hand, tag])
	get_tree().quit()


func _attach(skel: Skeleton3D, root: Node, bone: String, prop_name: String, rot: Vector3) -> BoneAttachment3D:
	var att := BoneAttachment3D.new()
	att.bone_name = bone
	skel.add_child(att)
	var prop := (load("res://assets/gen/props/%s.glb" % prop_name) as PackedScene).instantiate() as Node3D
	var sk := 1.0
	var n: Node = skel
	while n != null and n != root:
		if n is Node3D:
			sk *= (n as Node3D).scale.x
		n = n.get_parent()
	var s := 1.0 / maxf(sk, 0.0001)
	prop.transform = Transform3D(Basis.from_euler(Vector3(deg_to_rad(rot.x), deg_to_rad(rot.y), deg_to_rad(rot.z))) * Basis.from_scale(Vector3(s, s, s)), Vector3.ZERO)
	att.add_child(prop)
	return att
