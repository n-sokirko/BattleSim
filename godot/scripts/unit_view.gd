extends Node3D
## Стенд осмотра бойца: стойка/клип, камера спереди-сверху и сбоку, снимок в art/previews.
## Запуск: godot --path . res://scenes/unit_view.tscn -- --kind=footman --t=0.6 --tag=x [--clip=имя] [--soff=x,y,z] [--roff=x,y,z]

func _ready() -> void:
	var kind := "footman"
	var tt := 0.6
	var tag := "view"
	var clip := ""
	var focus := ""
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--kind="): kind = a.substr(7)
		if a.begins_with("--t="): tt = float(a.substr(4))
		if a.begins_with("--tag="): tag = a.substr(6)
		if a.begins_with("--clip="): clip = a.substr(7)
		if a.begins_with("--focus="): focus = a.substr(8)
		if a.begins_with("--soff="):
			var p := a.substr(7).split(",")
			Unit.test_off["shield_rect"] = Vector3(float(p[0]), float(p[1]), float(p[2]))
			Unit.test_off["shield_tower"] = Unit.test_off["shield_rect"]
		if a.begins_with("--roff="):
			var q := a.substr(7).split(",")
			Unit.test_off["sword_short"] = Vector3(float(q[0]), float(q[1]), float(q[2]))
			Unit.test_off["sword"] = Unit.test_off["sword_short"]
	Unit.load_shared()
	var env := WorldEnvironment.new()
	var e := Environment.new()
	e.background_mode = Environment.BG_COLOR
	e.background_color = Color(0.45, 0.5, 0.45)
	e.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	e.ambient_light_color = Color(0.8, 0.8, 0.85)
	env.environment = e
	add_child(env)
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-50, 30, 0)
	add_child(sun)
	var u := Unit.new()
	u.setup(kind, 0, null)
	u.ring.visible = false
	u.bar.visible = false
	add_child(u)
	if clip != "" and u.anim.has_animation(clip):
		u.anim.play(clip)
	else:
		u.play(u.a_idle, true)
	await get_tree().create_timer(tt).timeout
	u.anim.pause()
	var cam := Camera3D.new()
	cam.fov = 30.0
	add_child(cam)
	var views := {"front": Vector3(0, 2.2, 4.2), "top": Vector3(0, 5.2, 2.6), "side": Vector3(4.2, 1.8, 0.5), "back": Vector3(0, 2.2, -4.2)}
	var sheet := Image.create(1200, 300, false, Image.FORMAT_RGBA8)
	var i := 0
	await get_tree().process_frame
	var skel := u.model.find_child("Skeleton3D", true, false) as Skeleton3D
	for v in views:
		var tgt := Vector3(0, 1.1, 0)
		var offv: Vector3 = views[v]
		if focus != "":
			var bi := skel.find_bone(focus)
			tgt = skel.global_transform * skel.get_bone_global_pose(bi).origin
			offv = offv.normalized() * 1.6
		cam.position = tgt + (offv if focus != "" else offv - Vector3(0, 0, 0))
		if focus == "":
			cam.position = offv
		cam.look_at(tgt, Vector3.UP)
		await get_tree().process_frame
		await get_tree().process_frame
		var img := get_viewport().get_texture().get_image()
		img.convert(Image.FORMAT_RGBA8)
		var sz := img.get_size()
		var side := mini(sz.x, sz.y)
		img = img.get_region(Rect2i((sz.x - side) / 2, 0, side, side))
		img.resize(300, 300)
		sheet.blit_rect(img, Rect2i(0, 0, 300, 300), Vector2i(i * 300, 0))
		i += 1
	sheet.save_png("O:/BattleSim/art/previews/uv_%s_%s.png" % [kind, tag])
	get_tree().quit()
