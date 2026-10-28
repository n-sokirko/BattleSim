extends Node3D
## Витрина декораций Hunyuan: все res://assets/props_gen/*.glb в ряд, снимок в art/previews/props_view.png.

func _ready() -> void:
	var env := WorldEnvironment.new()
	var e := Environment.new()
	e.background_mode = Environment.BG_COLOR
	e.background_color = Color(0.5, 0.58, 0.5)
	e.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	e.ambient_light_color = Color(0.75, 0.75, 0.8)
	e.tonemap_mode = Environment.TONE_MAPPER_FILMIC
	env.environment = e
	add_child(env)
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-50, 30, 0)
	sun.shadow_enabled = true
	add_child(sun)
	var ground := MeshInstance3D.new()
	var pm := PlaneMesh.new(); pm.size = Vector2(200, 40)
	ground.mesh = pm
	var gm := StandardMaterial3D.new(); gm.albedo_color = Color(0.35, 0.45, 0.25)
	ground.material_override = gm
	add_child(ground)
	var names: Array = []
	for f in DirAccess.get_files_at("res://assets/props_gen"):
		if f.ends_with(".glb"):
			names.append(f.get_basename())
	names.sort()
	var x := 0.0
	for n in names:
		var m := (load("res://assets/props_gen/%s.glb" % n) as PackedScene).instantiate() as Node3D
		m.position = Vector3(x, 0, 0)
		add_child(m)
		x += 9.0
	var cam := Camera3D.new()
	cam.fov = 35.0
	add_child(cam)
	var mid := (x - 9.0) * 0.5
	cam.position = Vector3(mid, 9.0, 24.0 + names.size() * 2.0)
	cam.look_at(Vector3(mid, 3.5, 0), Vector3.UP)
	await get_tree().create_timer(1.0).timeout
	get_viewport().get_texture().get_image().save_png("O:/BattleSim/art/previews/props_view.png")
	print("PROPS ", names)
	get_tree().quit()
