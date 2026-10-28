extends Node3D
## Контактный лист клипов персонажа: --char=<имя> --clips=a,b,c ; кадры в долях длины клипа.
var frames := [0.1, 0.3, 0.5, 0.7, 0.9]
func _ready() -> void:
	var ch := "erland_militia"
	var clips: PackedStringArray = ["Idle", "kk/Idle", "kk/Walking_A", "kk/Running_A", "kk/1H_Melee_Attack_Slice_Diagonal", "kk/1H_Melee_Attack_Chop", "kk/Hit_A", "kk/Death_A"]
	var out_tag := "kk"
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--char="): ch = a.substr(7)
		if a.begins_with("--clips="): clips = a.substr(8).split(",")
	var env := WorldEnvironment.new()
	var e := Environment.new()
	e.background_mode = Environment.BG_COLOR
	e.background_color = Color(0.45, 0.5, 0.55)
	e.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	e.ambient_light_color = Color(0.8, 0.8, 0.85)
	e.ambient_light_energy = 0.9
	env.environment = e
	add_child(env)
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-40, 30, 0)
	add_child(sun)
	var cam := Camera3D.new()
	cam.position = Vector3(0, 1.2, 4.2)
	cam.fov = 38
	add_child(cam)
	var m = (load(("res://assets/kk/%s.glb" % ch) if ch == "Knight" else ("res://assets/hy/%s.glb" % ch)) as PackedScene).instantiate()
	add_child(m)
	var ap := m.find_child("AnimationPlayer", true, false) as AnimationPlayer
	if ap == null:
		ap = AnimationPlayer.new()
		m.add_child(ap)
	if ResourceLoader.exists("res://assets/hy/anims_%s.res" % ch):
		ap.add_animation_library("kk", load("res://assets/hy/anims_%s.res" % ch))
	var vp := get_viewport()
	var tw := 300
	var th := 360
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--tile="):
			tw = int(a.substr(7))
			th = int(tw * 1.2)
	var sheet := Image.create(tw * frames.size(), th * clips.size(), false, Image.FORMAT_RGBA8)
	await get_tree().process_frame
	await get_tree().process_frame
	for ci in clips.size():
		var c := clips[ci]
		if not ap.has_animation(c): continue
		var len := ap.get_animation(c).length
		for fi in frames.size():
			ap.play(c)
			ap.seek(len * frames[fi], true)
			ap.pause()
			await get_tree().process_frame
			await get_tree().process_frame
			var img := vp.get_texture().get_image()
			var sx := img.get_width() / 3
			img.convert(Image.FORMAT_RGBA8)
			var crop := img.get_region(Rect2i(sx, 0, sx, img.get_height()))
			crop.resize(tw, th)
			sheet.blit_rect(crop, Rect2i(0, 0, tw, th), Vector2i(fi * tw, ci * th))
	sheet.save_png("O:/BattleSim/art/previews/clips_%s.png" % (ch + "_" + out_tag))
	get_tree().quit()
