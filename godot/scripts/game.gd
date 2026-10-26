extends Node3D
## Поход в духе Warcraft 3: герой и небольшой отряд, спящие лагеря врага, сундуки, вожак.
## ПК: ЛКМ — выбрать/рамка, ПКМ — идти/бить, A+ЛКМ — атака с движением, Q W E R — способности героя,
## WASD/края экрана — камера, колесо — масштаб, Пробел — к герою, Ctrl+A — выбрать всех.

const FIELD := 110.0
const ABILITY_NAMES := ["Боевой клич", "Удар о землю", "Вызов", "Непоколебимые"]
const ABILITY_KEYS := ["Q", "W", "E", "R"]
const ABILITY_LEVEL := [1, 2, 3, 4]
const ABILITY_CD := [25.0, 18.0, 28.0, 45.0]
const XP_AT := [0.0, 25.0, 70.0, 140.0, 230.0, 350.0]

var units: Array[Unit] = []
var hero: Unit
var camps: Array = []
var chests: Array = []
var selection: Array[Unit] = []
var cam: Camera3D
var pivot: Node3D
var cam_yaw := 0.0
var cam_zoom := 17.0
var ab_ready := [0.0, 0.0, 0.0, 0.0]
var t_game := 0.0
var attack_cmd := false
var finished := false
var boss: Unit
var projectiles: Array = []

# интерфейс
var hud: CanvasLayer
var lbl_goal: Label
var lbl_hero: Label
var lbl_sel: Label
var lbl_msg: Label
var lbl_big: Label
var ab_buttons: Array[Button] = []
var selbox: Control
var drag_from := Vector2.ZERO
var dragging := false
var toasts: Array = []

# автопроверка из командной строки
var shot_dir := ""
var auto := false
var shots_done := 0
var next_shot := 3.0
var log_lines: Array[String] = []
var auto_t := 0.0


func _ready() -> void:
	Unit.load_shared()
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--shot="):
			shot_dir = a.substr(7)
		elif a.begins_with("--zoom="):
			cam_zoom = float(a.substr(7))
		elif a == "--auto":
			auto = true
		elif a.begins_with("--speed="):
			Engine.time_scale = float(a.substr(8))
	seed(7)
	_build_world()
	_build_camps()
	_build_party()
	_build_hud()
	_toast("Каэл со своим отрядом. ПКМ — идти и бить. Q W E R — способности. Лагеря спят, пока не подойдёшь.", 8.0)


# ---------------------------------------------------------------- мир

func _build_world() -> void:
	var env := WorldEnvironment.new()
	var e := Environment.new()
	var sky := Sky.new()
	var psm := ProceduralSkyMaterial.new()
	psm.sky_top_color = Color(0.35, 0.55, 0.85)
	psm.sky_horizon_color = Color(0.75, 0.82, 0.9)
	psm.ground_horizon_color = Color(0.62, 0.66, 0.6)
	sky.sky_material = psm
	e.background_mode = Environment.BG_SKY
	e.sky = sky
	e.ambient_light_source = Environment.AMBIENT_SOURCE_SKY
	e.ambient_light_energy = 0.9
	e.tonemap_mode = Environment.TONE_MAPPER_FILMIC
	e.fog_enabled = true
	e.fog_light_color = Color(0.75, 0.82, 0.9)
	e.fog_density = 0.0012
	e.ssao_enabled = true
	e.glow_enabled = true
	env.environment = e
	add_child(env)
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-52, 35, 0)
	sun.light_energy = 1.25
	sun.shadow_enabled = true
	sun.directional_shadow_max_distance = 90.0
	add_child(sun)
	# земля
	var ground := MeshInstance3D.new()
	var pm := PlaneMesh.new()
	pm.size = Vector2(FIELD * 2.6, FIELD * 2.6)
	ground.mesh = pm
	var noise := FastNoiseLite.new()
	noise.frequency = 0.012
	noise.fractal_octaves = 4
	var grad := Gradient.new()
	grad.colors = PackedColorArray([Color(0.30, 0.42, 0.18), Color(0.42, 0.5, 0.2), Color(0.55, 0.5, 0.26), Color(0.36, 0.45, 0.2)])
	grad.offsets = PackedFloat32Array([0.0, 0.4, 0.7, 1.0])
	var nt := NoiseTexture2D.new()
	nt.noise = noise
	nt.color_ramp = grad
	nt.width = 1024; nt.height = 1024
	var gm := StandardMaterial3D.new()
	gm.albedo_texture = nt
	gm.uv1_scale = Vector3(1, 1, 1)
	gm.roughness = 1.0
	ground.material_override = gm
	add_child(ground)
	# деревья и камни
	var trunk_m := StandardMaterial3D.new(); trunk_m.albedo_color = Color(0.32, 0.22, 0.13)
	var leaf_m := StandardMaterial3D.new(); leaf_m.albedo_color = Color(0.13, 0.34, 0.16)
	var leaf_m2 := StandardMaterial3D.new(); leaf_m2.albedo_color = Color(0.2, 0.42, 0.18)
	var rock_m := StandardMaterial3D.new(); rock_m.albedo_color = Color(0.45, 0.45, 0.47)
	var rng := RandomNumberGenerator.new(); rng.seed = 11
	for i in 330:
		var p := Vector3(rng.randf_range(-FIELD, FIELD), 0, rng.randf_range(-FIELD, FIELD))
		if _near_poi(p, 16.0):
			continue
		var s := rng.randf_range(0.8, 1.7)
		var tree := Node3D.new()
		tree.position = p
		tree.rotation.y = rng.randf() * TAU
		var tr := MeshInstance3D.new(); var cm := CylinderMesh.new()
		cm.top_radius = 0.18 * s; cm.bottom_radius = 0.26 * s; cm.height = 1.6 * s
		tr.mesh = cm; tr.position.y = 0.8 * s; tr.material_override = trunk_m
		tree.add_child(tr)
		for j in 3:
			var lf := MeshInstance3D.new(); var co := CylinderMesh.new()
			co.top_radius = 0.0; co.bottom_radius = (1.7 - j * 0.45) * s; co.height = 2.0 * s
			lf.mesh = co; lf.position.y = (1.7 + j * 1.0) * s
			lf.material_override = leaf_m if (i % 2 == 0) else leaf_m2
			tree.add_child(lf)
		add_child(tree)
	for i in 70:
		var p2 := Vector3(rng.randf_range(-FIELD, FIELD), 0, rng.randf_range(-FIELD, FIELD))
		if _near_poi(p2, 10.0):
			continue
		var r := MeshInstance3D.new(); var sm := SphereMesh.new()
		var rs := rng.randf_range(0.5, 1.6)
		sm.radius = rs; sm.height = rs * 1.3
		r.mesh = sm; r.position = p2 + Vector3(0, rs * 0.3, 0)
		r.rotation.y = rng.randf() * TAU
		r.scale = Vector3(1, 0.7, rng.randf_range(0.8, 1.3))
		r.material_override = rock_m
		add_child(r)
	# камера
	pivot = Node3D.new()
	add_child(pivot)
	cam = Camera3D.new()
	cam.fov = 45.0
	cam.far = 400.0
	pivot.add_child(cam)
	_apply_cam()


var pois: Array = []

func _near_poi(p: Vector3, r: float) -> bool:
	for q in pois:
		if Vector2(p.x - q.x, p.z - q.z).length() < r:
			return true
	return false


func _apply_cam() -> void:
	pivot.rotation = Vector3(0, cam_yaw, 0)
	cam.position = Vector3(0, sin(deg_to_rad(55.0)) * cam_zoom, cos(deg_to_rad(55.0)) * cam_zoom)
	cam.rotation = Vector3(deg_to_rad(-55.0), 0, 0)


# ---------------------------------------------------------------- лагеря и отряд

func _build_camps() -> void:
	# имя, позиция (доли поля), состав, вожак, опыт за лагерь
	var defs := [
		["Дозорные", Vector2(-0.35, -0.30), {"raider": 3}, false, 15.0],
		["Лучники у камней", Vector2(0.45, -0.05), {"rarcher": 3}, false, 20.0],
		["Конный разъезд", Vector2(-0.45, 0.25), {"rider": 3}, false, 25.0],
		["Лагерь погонщиков", Vector2(0.35, 0.45), {"raider": 3, "rarcher": 2}, false, 40.0],
		["Стан Кобека", Vector2(-0.05, 0.80), {"raider": 4, "rarcher": 2, "rider": 2, "boss": 1}, true, 0.0],
	]
	pois.append(Vector3(0, 0, -FIELD * 0.78))
	for d in defs:
		var pos := Vector3(d[1].x * FIELD, 0, d[1].y * FIELD)
		pois.append(pos)
	for d in defs:
		var pos := Vector3(d[1].x * FIELD, 0, d[1].y * FIELD)
		var camp := {"name": d[0], "pos": pos, "units": [], "awake": false, "cleared": false, "boss": d[3], "xp": d[4]}
		var k := 0
		for kind in d[2]:
			for i in d[2][kind]:
				var a := k * 2.4 + 0.5
				var r := 2.0 + (k % 3) * 1.4
				var u := Unit.new()
				u.setup(kind, 1, self)
				u.position = pos + Vector3(cos(a) * r, 0, sin(a) * r)
				u.rotation.y = a + PI
				u.home = u.position
				u.asleep = true
				u.camp = camp
				add_child(u)
				units.append(u)
				camp["units"].append(u)
				if kind == "boss":
					boss = u
				k += 1
		_camp_props(pos, d[3])
		camps.append(camp)
	var cdefs := [["Брошенный обоз", Vector2(0.62, -0.45), 30.0], ["Тайник у холма", Vector2(-0.62, 0.55), 40.0]]
	for c in cdefs:
		var pos := Vector3(c[1].x * FIELD, 0, c[1].y * FIELD)
		var node := Node3D.new()
		node.position = pos
		var crate := (load("res://assets/props/Prop_Crate.gltf") as PackedScene).instantiate()
		crate.scale = Vector3.ONE * 1.6
		node.add_child(crate)
		var gl := OmniLight3D.new(); gl.light_color = Color(1, 0.85, 0.4); gl.light_energy = 1.6; gl.omni_range = 6.0
		gl.position.y = 1.5
		node.add_child(gl)
		add_child(node)
		chests.append({"name": c[0], "pos": pos, "xp": c[2], "opened": false, "node": node})
		pois.append(pos)


func _camp_props(pos: Vector3, is_boss: bool) -> void:
	var fire := OmniLight3D.new()
	fire.light_color = Color(1.0, 0.6, 0.25); fire.light_energy = 2.2; fire.omni_range = 9.0
	fire.position = pos + Vector3(0, 1.2, 0)
	add_child(fire)
	var ember := MeshInstance3D.new(); var sm := SphereMesh.new(); sm.radius = 0.35; sm.height = 0.6
	ember.mesh = sm; ember.position = pos + Vector3(0, 0.3, 0)
	var em := StandardMaterial3D.new(); em.albedo_color = Color(1, 0.5, 0.15); em.emission_enabled = true; em.emission = Color(1, 0.45, 0.1); em.emission_energy_multiplier = 3.0
	ember.material_override = em
	add_child(ember)
	for i in (7 if is_boss else 4):
		var a := i * 1.7 + 0.3
		var node := Node3D.new()
		var src := "res://assets/props/Prop_Crate.gltf" if i % 3 != 0 else "res://assets/props/Prop_WoodenFence_Single.gltf"
		if is_boss and i == 0:
			src = "res://assets/props/Prop_Wagon.gltf"
		node.add_child((load(src) as PackedScene).instantiate())
		node.position = pos + Vector3(cos(a) * 7.0, 0, sin(a) * 7.0)
		node.rotation.y = a
		node.scale = Vector3.ONE * 1.5
		add_child(node)
	# красный стяг
	var pole := MeshInstance3D.new(); var pcm := CylinderMesh.new(); pcm.top_radius = 0.06; pcm.bottom_radius = 0.08; pcm.height = 5.0
	pole.mesh = pcm; pole.position = pos + Vector3(2.5, 2.5, -2.5)
	var pm := StandardMaterial3D.new(); pm.albedo_color = Color(0.3, 0.2, 0.12)
	pole.material_override = pm
	add_child(pole)
	var flag := MeshInstance3D.new(); var bm := BoxMesh.new(); bm.size = Vector3(1.4, 0.9, 0.05)
	flag.mesh = bm; flag.position = pos + Vector3(3.2, 4.4, -2.5)
	var fm := StandardMaterial3D.new(); fm.albedo_color = Color(0.7, 0.1, 0.08)
	flag.material_override = fm
	add_child(flag)


func _build_party() -> void:
	var start := Vector3(0, 0, -FIELD * 0.78)
	hero = Unit.new()
	hero.setup("hero", 0, self)
	hero.position = start + Vector3(0, 0, 3)
	add_child(hero); units.append(hero)
	var kinds := ["footman", "footman", "footman", "footman", "footman", "footman", "archer", "archer", "archer"]
	for i in kinds.size():
		var u := Unit.new()
		u.setup(kinds[i], 0, self)
		var row := i / 5
		var col := i % 5
		u.position = start + Vector3((col - 2) * 2.0, 0, -row * 2.2 - (2.5 if kinds[i] == "archer" else 0.0))
		add_child(u); units.append(u)
	pivot.position = hero.position
	selection.append(hero)
	hero.set_selected(true)


# ---------------------------------------------------------------- бой: помощники

func nearest_enemy(u: Unit, r: float) -> Unit:
	var best: Unit = null
	var bd := r
	for o in units:
		if o.team == u.team or o.state == Unit.S.DEAD:
			continue
		var d := u._dist(o)
		if d < bd:
			bd = d; best = o
	return best


func shoot(src: Unit, tg: Unit, amount: float) -> void:
	var m := MeshInstance3D.new()
	var bm := BoxMesh.new(); bm.size = Vector3(0.05, 0.05, 0.7)
	m.mesh = bm
	var mat := StandardMaterial3D.new(); mat.albedo_color = Color(0.9, 0.8, 0.5); mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	m.material_override = mat
	m.position = src.global_position + Vector3(0, 1.4, 0)
	add_child(m)
	projectiles.append({"node": m, "target": tg, "src": src, "dmg": amount})


func fx_hit(p: Vector3) -> void:
	var m := MeshInstance3D.new()
	var sm := SphereMesh.new(); sm.radius = 0.18; sm.height = 0.36
	m.mesh = sm; m.position = p
	var mat := StandardMaterial3D.new(); mat.albedo_color = Color(1, 0.85, 0.5); mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	m.material_override = mat
	add_child(m)
	var tw := create_tween()
	tw.tween_property(m, "scale", Vector3(2.2, 2.2, 2.2), 0.15)
	tw.parallel().tween_property(mat, "albedo_color:a", 0.0, 0.15)
	mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	tw.tween_callback(m.queue_free)


func fx_ring(p: Vector3, r: float, c: Color) -> void:
	var m := MeshInstance3D.new()
	var t := TorusMesh.new(); t.inner_radius = r * 0.92; t.outer_radius = r
	m.mesh = t; m.scale = Vector3(1, 0.05, 1); m.position = p + Vector3(0, 0.1, 0)
	var mat := StandardMaterial3D.new(); mat.albedo_color = c; mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	m.material_override = mat
	add_child(m)
	var tw := create_tween()
	tw.tween_property(m, "scale", Vector3(1.4, 0.05, 1.4), 0.5)
	tw.parallel().tween_property(mat, "albedo_color:a", 0.0, 0.5)
	tw.tween_callback(m.queue_free)


func on_death(u: Unit) -> void:
	if u == hero:
		_end(false)
		return
	if u.team == 1 and not finished:
		_gain_xp(80.0 if u.is_boss else 1.0)
		if u.is_boss:
			_end(true)


func _gain_xp(a: float) -> void:
	hero.xp += a
	while hero.level < XP_AT.size() and hero.xp >= XP_AT[hero.level]:
		hero.level += 1
		hero.hp = minf(hero.max_hp, hero.hp + hero.max_hp * 0.4)
		var msg := "Новый уровень: %d" % hero.level
		for i in 4:
			if ABILITY_LEVEL[i] == hero.level:
				msg += " · открыта способность «%s» (%s)" % [ABILITY_NAMES[i], ABILITY_KEYS[i]]
		_toast(msg, 5.0)
		fx_ring(hero.global_position, 3.0, Color(1, 0.85, 0.3))


func _end(win: bool) -> void:
	if finished:
		return
	finished = true
	lbl_big.text = ("Победа!\nКобек повержен, пленные свободны." if win else "Каэл пал.") + "\nНажмите Enter — начать заново"
	lbl_big.visible = true
	_log("RESULT %s t=%.0f level=%d" % ["WIN" if win else "LOSE", t_game, hero.level])


func _toast(s: String, sec := 3.0) -> void:
	toasts.append([s, sec])


func _log(s: String) -> void:
	log_lines.append("%.1f %s" % [t_game, s])
	print(s)


# ---------------------------------------------------------------- способности

func cast(i: int) -> void:
	if finished or hero.state == Unit.S.DEAD:
		return
	if hero.level < ABILITY_LEVEL[i]:
		_toast("«%s» откроется на %d-м уровне" % [ABILITY_NAMES[i], ABILITY_LEVEL[i]])
		return
	if t_game < ab_ready[i]:
		_toast("«%s» ещё не готова" % ABILITY_NAMES[i])
		return
	ab_ready[i] = t_game + ABILITY_CD[i]
	var hp := hero.global_position
	match i:
		0:
			for u in units:
				if u.team == 0 and u.state != Unit.S.DEAD and u.global_position.distance_to(hp) < 20.0:
					u.buff_t = 10.0
			fx_ring(hp, 20.0, Color(1, 0.8, 0.3))
		1:
			for u in units:
				if u.team == 1 and u.state != Unit.S.DEAD and u.global_position.distance_to(hp) < 7.0:
					u.stun = 2.0
					var away := (u.global_position - hp).normalized() * 1.8
					u.global_position += Vector3(away.x, 0, away.z)
					u.hurt(30.0, hero)
			fx_ring(hp, 7.0, Color(1, 0.4, 0.2))
		2:
			for u in units:
				if u.team == 1 and u.state != Unit.S.DEAD and not u.is_boss and u.global_position.distance_to(hp) < 15.0:
					u.taunt_by = hero; u.taunt_t = 5.0; u.target = hero; u.forced = true
			hero.guard_t = 5.0
			fx_ring(hp, 15.0, Color(1, 0.2, 0.2))
		3:
			for u in units:
				if u.team == 0 and u.state != Unit.S.DEAD and u.global_position.distance_to(hp) < 22.0:
					u.guard_t = 10.0
			fx_ring(hp, 22.0, Color(0.4, 0.7, 1.0))
	_log("cast %d" % i)


# ---------------------------------------------------------------- ввод

func _screen_ground(pos: Vector2) -> Variant:
	var o := cam.project_ray_origin(pos)
	var d := cam.project_ray_normal(pos)
	if absf(d.y) < 0.0001:
		return null
	var t := -o.y / d.y
	if t < 0.0:
		return null
	return o + d * t


func _unit_at(pos: Vector2, team: int) -> Unit:
	var g = _screen_ground(pos)
	if g == null:
		return null
	var best: Unit = null
	var bd := 1.6
	for u in units:
		if u.team != team or u.state == Unit.S.DEAD:
			continue
		var d := Vector2(u.global_position.x - g.x, u.global_position.z - g.z).length()
		if d < bd + u.radius * 0.5:
			bd = d; best = u
	return best


func _input(ev: InputEvent) -> void:
	if finished and ev is InputEventKey and ev.pressed and ev.keycode == KEY_ENTER:
		get_tree().reload_current_scene()
		return
	if ev is InputEventMouseButton:
		var mb := ev as InputEventMouseButton
		if mb.button_index == MOUSE_BUTTON_WHEEL_UP and mb.pressed:
			cam_zoom = clampf(cam_zoom - 2.5, 10.0, 55.0)
		elif mb.button_index == MOUSE_BUTTON_WHEEL_DOWN and mb.pressed:
			cam_zoom = clampf(cam_zoom + 2.5, 10.0, 55.0)
		elif mb.button_index == MOUSE_BUTTON_LEFT:
			if mb.pressed:
				drag_from = mb.position; dragging = true
			elif dragging:
				dragging = false
				selbox.visible = false
				if drag_from.distance_to(mb.position) < 6.0:
					_click_select(mb.position)
				else:
					_box_select(drag_from, mb.position)
		elif mb.button_index == MOUSE_BUTTON_RIGHT and mb.pressed:
			_right_click(mb.position)
	elif ev is InputEventMouseMotion and dragging:
		var mm := ev as InputEventMouseMotion
		if drag_from.distance_to(mm.position) > 6.0:
			selbox.visible = true
			selbox.set("a", drag_from); selbox.set("b", mm.position)
			selbox.queue_redraw()
		if Input.is_mouse_button_pressed(MOUSE_BUTTON_MIDDLE):
			cam_yaw -= mm.relative.x * 0.005
	elif ev is InputEventMouseMotion and Input.is_mouse_button_pressed(MOUSE_BUTTON_MIDDLE):
		cam_yaw -= (ev as InputEventMouseMotion).relative.x * 0.005
	elif ev is InputEventKey and ev.pressed and not ev.echo:
		var k := (ev as InputEventKey).keycode
		match k:
			KEY_Q: cast(0)
			KEY_W: cast(1)
			KEY_E: cast(2)
			KEY_R: cast(3)
			KEY_SPACE: pivot.position = hero.global_position
			KEY_S: _order_stop()
			KEY_A:
				if ev.ctrl_pressed:
					_select_all()
				else:
					attack_cmd = true
			KEY_ESCAPE: attack_cmd = false


func _click_select(pos: Vector2) -> void:
	if attack_cmd:
		attack_cmd = false
		var g = _screen_ground(pos)
		if g != null:
			for u in selection:
				u.order_move(g, true)
		return
	var u := _unit_at(pos, 0)
	_clear_sel()
	if u != null:
		_select(u)


func _box_select(a: Vector2, b: Vector2) -> void:
	var r := Rect2(a, b - a).abs()
	var add := Input.is_key_pressed(KEY_SHIFT)
	if not add:
		_clear_sel()
	for u in units:
		if u.team != 0 or u.state == Unit.S.DEAD:
			continue
		if cam.is_position_behind(u.global_position):
			continue
		if r.has_point(cam.unproject_position(u.global_position + Vector3(0, 1, 0))):
			_select(u)


func _select_all() -> void:
	_clear_sel()
	for u in units:
		if u.team == 0 and u.state != Unit.S.DEAD:
			_select(u)


func _clear_sel() -> void:
	for u in selection:
		if is_instance_valid(u):
			u.set_selected(false)
	selection.clear()


func _select(u: Unit) -> void:
	if not selection.has(u):
		selection.append(u)
		u.set_selected(true)


func _order_stop() -> void:
	for u in selection:
		u.order_stop()


func _right_click(pos: Vector2) -> void:
	attack_cmd = false
	var foe := _unit_at(pos, 1)
	if foe != null:
		for u in selection:
			u.order_attack(foe)
		fx_ring(foe.global_position, 1.4, Color(1, 0.3, 0.2))
		return
	var g = _screen_ground(pos)
	if g == null:
		return
	var n := selection.size()
	for i in n:
		var a := i * 2.4
		var r := 0.0 if n == 1 else 1.6 + 0.5 * (i / 6)
		var off := Vector3(cos(a) * r, 0, sin(a) * r) if i > 0 else Vector3.ZERO
		selection[i].order_move(g + off)
	fx_ring(g, 1.0, Color(0.4, 1, 0.4))


# ---------------------------------------------------------------- интерфейс

class SelBox extends Control:
	var a := Vector2.ZERO
	var b := Vector2.ZERO
	func _draw() -> void:
		var r := Rect2(a, b - a).abs()
		draw_rect(r, Color(0.3, 1, 0.4, 0.12), true)
		draw_rect(r, Color(0.3, 1, 0.4, 0.9), false, 2.0)


func _label(parent: Control, text: String, size := 18) -> Label:
	var l := Label.new()
	l.text = text
	l.add_theme_font_size_override("font_size", size)
	l.add_theme_color_override("font_outline_color", Color(0, 0, 0))
	l.add_theme_constant_override("outline_size", 5)
	l.mouse_filter = Control.MOUSE_FILTER_IGNORE
	parent.add_child(l)
	return l


func _build_hud() -> void:
	hud = CanvasLayer.new()
	add_child(hud)
	var root := Control.new()
	root.set_anchors_preset(Control.PRESET_FULL_RECT)
	root.mouse_filter = Control.MOUSE_FILTER_IGNORE
	hud.add_child(root)
	selbox = SelBox.new()
	selbox.set_anchors_preset(Control.PRESET_FULL_RECT)
	selbox.mouse_filter = Control.MOUSE_FILTER_IGNORE
	selbox.visible = false
	root.add_child(selbox)
	lbl_goal = _label(root, "", 20)
	lbl_goal.position = Vector2(16, 12)
	lbl_hero = _label(root, "", 18)
	lbl_hero.position = Vector2(16, 66)
	lbl_sel = _label(root, "", 16)
	lbl_sel.set_anchors_preset(Control.PRESET_BOTTOM_LEFT)
	lbl_sel.position = Vector2(16, -80)
	lbl_msg = _label(root, "", 20)
	lbl_msg.set_anchors_preset(Control.PRESET_CENTER_TOP)
	lbl_msg.position = Vector2(-500, 130)
	lbl_msg.size = Vector2(1000, 60)
	lbl_msg.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	lbl_big = _label(root, "", 44)
	lbl_big.set_anchors_preset(Control.PRESET_CENTER)
	lbl_big.position = Vector2(-500, -100)
	lbl_big.size = Vector2(1000, 200)
	lbl_big.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	lbl_big.visible = false
	var row := HBoxContainer.new()
	row.set_anchors_preset(Control.PRESET_CENTER_BOTTOM)
	row.position = Vector2(-300, -110)
	row.add_theme_constant_override("separation", 10)
	root.add_child(row)
	for i in 4:
		var b := Button.new()
		b.custom_minimum_size = Vector2(140, 90)
		b.focus_mode = Control.FOCUS_NONE
		b.pressed.connect(cast.bind(i))
		row.add_child(b)
		ab_buttons.append(b)


func _update_hud(dt: float) -> void:
	var left := 0
	for c in camps:
		if not c["cleared"]: left += 1
	lbl_goal.text = "Задача: победить хана Кобека\nЛагерей осталось: %d из %d" % [left, camps.size()]
	var nxt: float = XP_AT[hero.level] if hero.level < XP_AT.size() else hero.xp
	lbl_hero.text = "%s · ур. %d · здоровье %d/%d · опыт %d/%d" % [hero.title, hero.level, hero.hp, hero.max_hp, hero.xp, nxt]
	var names := {}
	for u in selection:
		names[u.title] = names.get(u.title, 0) + 1
	var parts: PackedStringArray = []
	for k in names:
		parts.append("%s ×%d" % [k, names[k]])
	lbl_sel.text = "Выбрано: " + (", ".join(parts) if parts.size() > 0 else "никого") + ("\n[атака с движением: кликните точку]" if attack_cmd else "")
	for i in 4:
		var b := ab_buttons[i]
		var open: bool = hero.level >= ABILITY_LEVEL[i]
		var cdl: float = maxf(0.0, ab_ready[i] - t_game)
		if not open:
			b.text = "%s\n%s\nур. %d" % [ABILITY_KEYS[i], ABILITY_NAMES[i], ABILITY_LEVEL[i]]
			b.disabled = true
		elif cdl > 0.0:
			b.text = "%s\n%s\n%d с" % [ABILITY_KEYS[i], ABILITY_NAMES[i], ceili(cdl)]
			b.disabled = true
		else:
			b.text = "%s\n%s" % [ABILITY_KEYS[i], ABILITY_NAMES[i]]
			b.disabled = false
	# сообщения
	if toasts.size() > 0:
		toasts[0][1] -= dt
		lbl_msg.text = toasts[0][0]
		if toasts[0][1] <= 0.0:
			toasts.pop_front()
	else:
		lbl_msg.text = ""


# ---------------------------------------------------------------- кадр

func _process(dt: float) -> void:
	dt = minf(dt, 0.05)
	if not finished:
		t_game += dt
	# камера
	var mv := Vector3.ZERO
	var vp := get_viewport().get_visible_rect().size
	var mp := get_viewport().get_mouse_position()
	var edge := 8.0
	if Input.is_key_pressed(KEY_UP) or (mp.y < edge and mp.y >= 0 and DisplayServer.window_is_focused()): mv.z -= 1
	if Input.is_key_pressed(KEY_DOWN) or (mp.y > vp.y - edge and DisplayServer.window_is_focused()): mv.z += 1
	if Input.is_key_pressed(KEY_LEFT) or (mp.x < edge and mp.x >= 0 and DisplayServer.window_is_focused()): mv.x -= 1
	if Input.is_key_pressed(KEY_RIGHT) or (mp.x > vp.x - edge and DisplayServer.window_is_focused()): mv.x += 1
	if mv != Vector3.ZERO:
		var m := Basis(Vector3.UP, cam_yaw) * mv.normalized()
		pivot.position += m * cam_zoom * 1.1 * dt
	if hero.state != Unit.S.DEAD and Input.is_key_pressed(KEY_F):
		pivot.position = pivot.position.lerp(hero.global_position, 0.2)
	pivot.position.x = clampf(pivot.position.x, -FIELD, FIELD)
	pivot.position.z = clampf(pivot.position.z, -FIELD, FIELD)
	cam.position = cam.position.lerp(Vector3(0, sin(deg_to_rad(55.0)) * cam_zoom, cos(deg_to_rad(55.0)) * cam_zoom), 0.2)
	pivot.rotation.y = cam_yaw
	# бойцы
	if not finished:
		for u in units.duplicate():
			if is_instance_valid(u): u.tick(dt)
		_camps_tick(dt)
		_projectiles_tick(dt)
		_chests_tick()
	_update_hud(dt)
	if auto and hero.state != Unit.S.DEAD:
		pivot.position = pivot.position.lerp(hero.global_position, 0.08)
	if auto:
		_auto_tick(dt)
	if shot_dir != "":
		_shots(dt)


func _projectiles_tick(dt: float) -> void:
	for p in projectiles.duplicate():
		var m: MeshInstance3D = p["node"]
		var tg: Unit = p["target"]
		if tg == null or not is_instance_valid(tg) or tg.state == Unit.S.DEAD:
			m.queue_free(); projectiles.erase(p); continue
		var to := tg.global_position + Vector3(0, 1.2, 0) - m.position
		var step := 30.0 * dt
		if to.length() > 0.01:
			m.look_at(m.position + to, Vector3.UP)
		if to.length() <= step:
			tg.hurt(p["dmg"] * randf_range(0.85, 1.15), p["src"])
			m.queue_free(); projectiles.erase(p)
		else:
			m.position += to.normalized() * step


func _camps_tick(_dt: float) -> void:
	for c in camps:
		if c["cleared"]:
			continue
		var alive := 0
		var hurt := false
		for u in c["units"]:
			if is_instance_valid(u) and u.state != Unit.S.DEAD:
				alive += 1
				if u.hp < u.max_hp - 0.5: hurt = true
		if alive == 0:
			c["cleared"] = true
			_toast("Лагерь «%s» разгромлен" % c["name"], 4.0)
			_log("camp cleared %s" % c["name"])
			if c["xp"] > 0.0: _gain_xp(c["xp"])
			continue
		if alive < c["units"].size() and not c["awake"]:
			hurt = true
		var near := false
		var far := true
		for u in units:
			if u.team != 0 or u.state == Unit.S.DEAD: continue
			var d: float = u.global_position.distance_to(c["pos"])
			if d < 20.0: near = true
			if d < 55.0: far = false
		if not c["awake"] and (near or hurt):
			c["awake"] = true
			for u in c["units"]:
				if is_instance_valid(u):
					u.asleep = false; u.has_goal = false
			_toast("Лагерь «%s» проснулся!" % c["name"], 3.0)
			_log("camp woke %s" % c["name"])
		elif c["awake"] and far:
			c["awake"] = false
			for u in c["units"]:
				if is_instance_valid(u) and u.state != Unit.S.DEAD:
					u.asleep = true; u.target = null; u.forced = false
					u.order_move(u.home); u.hp = u.max_hp


func _chests_tick() -> void:
	if hero.state == Unit.S.DEAD:
		return
	for c in chests:
		if c["opened"]: continue
		if hero.global_position.distance_to(c["pos"]) < 3.5:
			c["opened"] = true
			c["node"].visible = false
			hero.hp = hero.max_hp
			_gain_xp(c["xp"])
			_toast("Сундук «%s»: здоровье восстановлено, опыт +%d" % [c["name"], c["xp"]], 4.0)
			_log("chest %s" % c["name"])


# ---------------------------------------------------------------- автопроверка

func _shots(dt: float) -> void:
	if t_game >= next_shot or finished and shots_done < 99:
		var img := get_viewport().get_texture().get_image()
		DirAccess.make_dir_recursive_absolute(shot_dir)
		img.save_png("%s/shot_%02d.png" % [shot_dir, shots_done])
		shots_done += 1
		next_shot = t_game + 20.0
		if finished and shots_done >= 99:
			pass
	if finished or t_game > 600.0:
		var f := FileAccess.open(shot_dir + "/log.txt", FileAccess.WRITE)
		f.store_string("\n".join(log_lines))
		f.close()
		get_tree().quit()


func _auto_tick(dt: float) -> void:
	auto_t -= dt
	if auto_t > 0.0 or finished:
		return
	auto_t = 1.0
	# идём на ближайший нетронутый лагерь целым отрядом
	var best = null
	var bd := 1e9
	for c in camps:
		if c["cleared"]: continue
		var d: float = hero.global_position.distance_to(c["pos"])
		if d < bd: bd = d; best = c
	if best == null:
		return
	for u in units:
		if u.team == 0 and u.state != Unit.S.DEAD and not u.forced and not u.has_goal and u.target == null:
			u.order_move(best["pos"] + Vector3(randf_range(-3, 3), 0, randf_range(-8, -5)), true)
	var foes := nearest_enemy(hero, 12.0)
	if foes != null:
		for i in 4:
			if hero.level >= ABILITY_LEVEL[i] and t_game >= ab_ready[i]:
				cast(i)
				break
	if int(t_game) % 15 == 0:
		_log("hero hp %d lvl %d xp %d friends %d foes %d" % [hero.hp, hero.level, hero.xp, _count(0), _count(1)])


func _count(team: int) -> int:
	var n := 0
	for u in units:
		if u.team == team and u.state != Unit.S.DEAD: n += 1
	return n
