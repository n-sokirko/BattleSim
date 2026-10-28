extends Node3D
## Поход в духе Warcraft 3: герой и небольшой отряд, спящие лагеря врага, сундуки, вожак.
## ПК: ЛКМ — выбрать/рамка, ПКМ — идти/бить, A+ЛКМ — атака с движением, Q W E R — способности героя,
## WASD/края экрана — камера, колесо — масштаб, Пробел — к герою, Ctrl+A — выбрать всех.

const FIELD := 110.0
const CAMP_POS := [Vector2(-0.35, -0.30), Vector2(0.45, -0.05), Vector2(-0.45, 0.25), Vector2(0.35, 0.45), Vector2(-0.05, 0.80)]
const CHEST_POS := [Vector2(0.62, -0.45), Vector2(-0.62, 0.55)]
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
var cam_zoom := 14.0
var ab_ready := [0.0, 0.0, 0.0, 0.0]
var t_game := 0.0
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
var no_edge := false
var auto := false
var shake := 0.0
var fx_test := false
var fx_i := 0
var fx_clock := 0.0
var sel_all_test := false
var shots_done := 0
var next_shot := 3.0
var log_lines: Array[String] = []
var auto_t := 0.0


func _ready() -> void:
	Unit.load_shared()
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--shot="):
			shot_dir = a.substr(7)
		elif a.begins_with("--handl="):
			Unit.hand_test_l = a.substr(8)
		elif a.begins_with("--hand="):
			Unit.hand_test = a.substr(7)
		elif a == "--noedge":
			no_edge = true
		elif a.begins_with("--zoom="):
			cam_zoom = float(a.substr(7))
		elif a == "--auto":
			auto = true
		elif a == "--fxtest":
			fx_test = true
		elif a == "--selall":
			sel_all_test = true
		elif a.begins_with("--speed="):
			Engine.time_scale = float(a.substr(8))
	seed(7)
	_make_terrain()
	_build_camps()
	_build_world()
	_build_party()
	_build_hud()
	fog.update(0.0, true)
	if sel_all_test:
		_select_all()
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
	e.ambient_light_energy = 0.95
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
	sun.light_energy = 1.35
	sun.shadow_enabled = true
	sun.directional_shadow_max_distance = 90.0
	add_child(sun)
	# земля, река, дороги
	terrain.build_nodes(self)
	_scatter()
	nav.build(terrain, FIELD)
	# камера
	pivot = Node3D.new()
	add_child(pivot)
	cam = Camera3D.new()
	cam.fov = 45.0
	cam.far = 400.0
	pivot.add_child(cam)
	_apply_cam()


## Расстановка растительности и камней по правилам: не на воде, не на дорогах, не на лагерях, склоны — камни.
func _scatter() -> void:
	var rng := RandomNumberGenerator.new(); rng.seed = 11
	var trees := _scenes(["pine_tree_1", "pine_tree_2", "pine_tree_3", "pine_tree_4", "pine_tree_5", "normal_tree_1", "normal_tree_2", "normal_tree_3", "normal_tree_4", "birch_tree_1", "birch_tree_2", "birch_tree_3", "maple_tree_1", "maple_tree_2"])
	var dead := _scenes(["dead_tree_1", "dead_tree_2", "dead_tree_3", "dead_tree_4"])
	var bushes := _scenes(["bush", "bush_large", "bush_small", "bush_flowers", "bush_large_flowers"])
	var rocks := _scenes(["rock_1", "rock_2", "rock_3", "rock_4", "rock_5"])
	var flowers := _scenes(["flower_1_clump", "flower_2_clump", "flower_3_clump", "flower_4_clump", "flower_5_clump", "plant_1", "plant_2"])
	var grass := _scenes(["grass_large", "grass_small"])
	# рощи
	var woods: Array[Vector3] = []
	var tries := 0
	while woods.size() < 20 and tries < 400:
		tries += 1
		var c := Vector3(rng.randf_range(-FIELD * 0.92, FIELD * 0.92), 0, rng.randf_range(-FIELD * 0.92, FIELD * 0.92))
		if _near_poi(c, 26.0) or terrain.river_dist(c.x, c.z) < 14.0 or terrain.road_dist(c.x, c.z) < 9.0:
			continue
		woods.append(c)
	for w in woods:
		for j in 28:
			var a := rng.randf() * TAU
			var r := sqrt(rng.randf()) * 16.0
			var p := w + Vector3(cos(a) * r, 0, sin(a) * r)
			if _prop_ok(p, 13.0, 0.18):
				var ts := rng.randf_range(1.0, 1.7)
				_put(trees[rng.randi() % trees.size()], p, ts, rng, 0.3 * ts)
	for i in 50:
		var p1 := Vector3(rng.randf_range(-FIELD, FIELD), 0, rng.randf_range(-FIELD, FIELD))
		if _prop_ok(p1, 14.0, 0.2):
			var ts1 := rng.randf_range(1.0, 1.6)
			_put(trees[rng.randi() % trees.size()] if i % 6 != 0 else dead[rng.randi() % dead.size()], p1, ts1, rng, 0.3 * ts1)
	for i in 140:
		var p2 := Vector3(rng.randf_range(-FIELD, FIELD), 0, rng.randf_range(-FIELD, FIELD))
		if _prop_ok(p2, 8.0, 0.25):
			_put(bushes[rng.randi() % bushes.size()], p2, rng.randf_range(1.0, 1.6), rng)
	# камни: больше на склонах
	for i in 220:
		var p3 := Vector3(rng.randf_range(-FIELD, FIELD), 0, rng.randf_range(-FIELD, FIELD))
		var sl := terrain.slope(p3.x, p3.z)
		if _prop_ok(p3, 9.0, 1.0) and (sl > 0.09 or rng.randf() < 0.18):
			_put(rocks[rng.randi() % rocks.size()], p3, rng.randf_range(1.0, 2.6) * (1.0 + sl * 3.0), rng, -1.0)
	# скалы по краям карты
	for i in 90:
		var e := rng.randf_range(0.9, 1.15) * FIELD
		var t := rng.randf_range(-1.15, 1.15) * FIELD
		var pr := Vector3(e, 0, t) if i % 4 == 0 else (Vector3(-e, 0, t) if i % 4 == 1 else (Vector3(t, 0, e) if i % 4 == 2 else Vector3(t, 0, -e)))
		_put(rocks[rng.randi() % rocks.size()], pr, rng.randf_range(3.0, 7.0), rng, -1.0)
	# берег реки: кусты, камыш и трава
	for i in 150:
		var x := rng.randf_range(-FIELD, FIELD)
		var side := 1.0 if rng.randf() < 0.5 else -1.0
		var pz := Vector3(x, 0, terrain.river_z(x) + side * (terrain.river_w(x) + rng.randf_range(0.8, 5.5)))
		if _prop_ok(pz, 8.0, 0.4):
			_put(grass[rng.randi() % grass.size()] if i % 3 != 0 else bushes[rng.randi() % bushes.size()], pz, rng.randf_range(1.2, 2.2), rng)
	for i in 520:
		var p4 := Vector3(rng.randf_range(-FIELD, FIELD), 0, rng.randf_range(-FIELD, FIELD))
		if _prop_ok(p4, 5.0, 0.3):
			_put(flowers[rng.randi() % flowers.size()] if i % 3 == 0 else grass[rng.randi() % grass.size()], p4, rng.randf_range(1.2, 2.2), rng)


func _prop_ok(p: Vector3, poi_r: float, max_slope: float) -> bool:
	if absf(p.x) > FIELD * 1.02 or absf(p.z) > FIELD * 1.02:
		return false
	if _near_poi(p, poi_r):
		return false
	if terrain.height(p.x, p.z) < Terrain.WATER_Y + 0.5:
		return false
	if terrain.road_dist(p.x, p.z) < 3.4:
		return false
	return terrain.slope(p.x, p.z) < max_slope


## Точка на земле: подставляет высоту ландшафта.
func ground(p: Vector3) -> Vector3:
	return Vector3(p.x, terrain.height(p.x, p.z), p.z)


var terrain: Terrain
var nav: Nav
var fog: Fog
var pois: Array = []


## Радиус непроходимой части предмета по его габаритам (деревья — только ствол).
func _footprint(n: Node3D) -> float:
	var box := AABB()
	var first := true
	for mi in n.find_children("*", "MeshInstance3D", true, false):
		var b: AABB = (mi as MeshInstance3D).global_transform * (mi as MeshInstance3D).get_aabb()
		box = b if first else box.merge(b)
		first = false
	if first:
		return 0.5
	return clampf(0.38 * maxf(box.size.x, box.size.z), 0.4, 4.0)


## Ландшафт строится до лагерей: ему нужны поляны под лагеря, сундуки и дороги между ними.
func _make_terrain() -> void:
	terrain = Terrain.new()
	var pl: Array = []
	var start := Vector3(0, 0, -FIELD * 0.78)
	pl.append([start, 10.0])
	var cp: Array = []
	for i in CAMP_POS.size():
		var q := Vector3(CAMP_POS[i].x * FIELD, 0, CAMP_POS[i].y * FIELD)
		cp.append(q)
		pl.append([q, 14.0 if i == CAMP_POS.size() - 1 else 11.0])
	for c in CHEST_POS:
		pl.append([Vector3(c.x * FIELD, 0, c.y * FIELD), 5.0])
	var ford := Vector3(0, 0, -58)
	var mid := Vector3(0, 0, -22)
	var roads: Array = [[start, ford], [ford, mid], [mid, cp[0]], [mid, cp[1]], [mid, cp[2]], [cp[1], cp[3]], [cp[3], cp[4]], [cp[2], cp[4]]]
	terrain.setup(FIELD, pl, roads)
	nav = Nav.new()
	fog = Fog.new()
	fog.setup(self, terrain)
	terrain.fog = fog


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
		["Дозорные", CAMP_POS[0], {"raider": 3}, false, 15.0],
		["Лучники у камней", CAMP_POS[1], {"rarcher": 3}, false, 20.0],
		["Конный разъезд", CAMP_POS[2], {"rider": 3}, false, 25.0],
		["Лагерь погонщиков", CAMP_POS[3], {"raider": 3, "rarcher": 2}, false, 40.0],
		["Стан Кобека", CAMP_POS[4], {"raider": 4, "rarcher": 2, "rider": 2, "boss": 1}, true, 0.0],
	]
	pois.append(Vector3(0, 0, -FIELD * 0.78))
	for d in defs:
		var pos := ground(Vector3(d[1].x * FIELD, 0, d[1].y * FIELD))
		pois.append(pos)
	for d in defs:
		var pos := ground(Vector3(d[1].x * FIELD, 0, d[1].y * FIELD))
		var camp := {"name": d[0], "pos": pos, "units": [], "awake": false, "cleared": false, "boss": d[3], "xp": d[4]}
		var k := 0
		for kind in d[2]:
			for i in d[2][kind]:
				var a := k * 2.4 + 0.5
				var r := 2.0 + (k % 3) * 1.4
				var u := Unit.new()
				u.setup(kind, 1, self)
				u.position = ground(pos + Vector3(cos(a) * r, 0, sin(a) * r))
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
	var cdefs := [["Брошенный обоз", CHEST_POS[0], 30.0], ["Тайник у холма", CHEST_POS[1], 40.0]]
	for c in cdefs:
		var pos := ground(Vector3(c[1].x * FIELD, 0, c[1].y * FIELD))
		var node := Node3D.new()
		node.position = pos
		var crate := (load("res://assets/hex/crate_open.gltf") as PackedScene).instantiate()
		crate.scale = Vector3.ONE * 2.6
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
	fog.register(fire, pos)
	var ember := MeshInstance3D.new(); var sm := SphereMesh.new(); sm.radius = 0.35; sm.height = 0.6
	ember.mesh = sm; ember.position = pos + Vector3(0, 0.3, 0)
	var em := StandardMaterial3D.new(); em.albedo_color = Color(1, 0.5, 0.15); em.emission_enabled = true; em.emission = Color(1, 0.45, 0.1); em.emission_energy_multiplier = 3.0
	ember.material_override = em
	add_child(ember)
	fog.register(ember, pos)
	var S := 4.2
	var ring := ["tent", "crate_A_big", "weaponrack", "barrel", "tent", "sack", "crate_B_big", "wheelbarrow"]
	var n := 8 if is_boss else 5
	for i in n:
		var a := i * (TAU / n) + 0.4
		var rr := 8.5 if ring[i % ring.size()] == "tent" else 6.5
		_hex(ring[i % ring.size()], pos + Vector3(cos(a) * rr, 0, sin(a) * rr), S, -a + PI / 2)
	_hex("flag_red", pos + Vector3(2.8, 0, -2.8), S * 1.4, 0.0, false)
	if is_boss:
		_hex("building_tower_A_red", pos + Vector3(-11, 0, 4), 3.2, 0.6)
		_hex("fence_wood_straight", pos + Vector3(0, 0, 13), 3.0)
		_hex("fence_wood_straight", pos + Vector3(7, 0, 12), 3.0)


func _build_party() -> void:
	var start := ground(Vector3(0, 0, -FIELD * 0.78))
	hero = Unit.new()
	hero.setup("hero", 0, self)
	hero.position = ground(start + Vector3(0, 0, 3))
	add_child(hero); units.append(hero)
	var kinds := ["footman", "footman", "footman", "footman", "footman", "footman", "archer", "archer", "archer"]
	for i in kinds.size():
		var u := Unit.new()
		u.setup(kinds[i], 0, self)
		var row := i / 5
		var col := i % 5
		u.position = ground(start + Vector3((col - 2) * 2.0, 0, -row * 2.2 - (2.5 if kinds[i] == "archer" else 0.0)))
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
		if u.team == 0 and not fog.seen(o.global_position):
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


## Сбитая цель: короткая светлая вспышка у ног.
func fx_stagger(p: Vector3) -> void:
	fx_ring(p, 1.1, Color(1.0, 0.95, 0.6))


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
	ui.big.text = ("Победа!\nКобек повержен, пленные свободны." if win else "Каэл пал.") + "\nНажмите Enter — начать заново"
	ui.big.visible = true
	_log("RESULT %s t=%.0f level=%d" % ["WIN" if win else "LOSE", t_game, hero.level])


func _toast(s: String, sec := 3.0) -> void:
	if ui != null:
		ui.toast(s, sec)


func _log(s: String) -> void:
	log_lines.append("%.1f %s" % [t_game, s])
	print(s)


# ---------------------------------------------------------------- способности

func shake_cam(a: float) -> void:
	shake = maxf(shake, a)


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
					Fx.aura(self, u, Color(1.0, 0.82, 0.38), 10.0)
			Fx.cry(self, hp, 20.0)
		1:
			for u in units:
				if u.team == 1 and u.state != Unit.S.DEAD and u.global_position.distance_to(hp) < 7.0:
					var away := (u.global_position - hp).normalized() * 1.8
					u.global_position += Vector3(away.x, 0, away.z)
					u.hurt(30.0, hero, 0.0)
					u.stagger(1.6)
					fx_hit(u.global_position + Vector3(0, 1.0, 0))
			Fx.slam(self, hp, 7.0)
		2:
			for u in units:
				if u.team == 1 and u.state != Unit.S.DEAD and not u.is_boss and u.global_position.distance_to(hp) < 15.0:
					u.taunt_by = hero; u.taunt_t = 5.0; u.target = hero; u.forced = true
					Fx.alert(self, u)
			hero.guard_t = 5.0
			Fx.taunt(self, hp, 15.0)
		3:
			for u in units:
				if u.team == 0 and u.state != Unit.S.DEAD and u.global_position.distance_to(hp) < 22.0:
					u.guard_t = 10.0
					Fx.aura(self, u, Color(0.45, 0.75, 1.0), 10.0)
			Fx.ward(self, hp, 22.0)
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
	var p := o + d * t
	# уточняем по рельефу: пересечение луча с плоскостью на высоте земли в найденной точке
	for i in 4:
		var h := terrain.height(p.x, p.z)
		t = (h - o.y) / d.y
		p = o + d * t
	return p


func _unit_at(pos: Vector2, team: int) -> Unit:
	var g = _screen_ground(pos)
	if g == null:
		return null
	var best: Unit = null
	var bd := 1.6
	for u in units:
		if u.team != team or u.state == Unit.S.DEAD or not u.visible:
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
		if mb.pressed and ui.over_ui(mb.position) and mb.button_index != MOUSE_BUTTON_WHEEL_UP and mb.button_index != MOUSE_BUTTON_WHEEL_DOWN:
			return
		if mb.button_index == MOUSE_BUTTON_WHEEL_UP and mb.pressed:
			cam_zoom = clampf(cam_zoom - 2.5, 10.0, 55.0)
		elif mb.button_index == MOUSE_BUTTON_WHEEL_DOWN and mb.pressed:
			cam_zoom = clampf(cam_zoom + 2.5, 10.0, 55.0)
		elif mb.button_index == MOUSE_BUTTON_LEFT:
			if mb.pressed:
				drag_from = mb.position; dragging = true
			elif dragging:
				dragging = false
				ui.selbox.visible = false
				if drag_from.distance_to(mb.position) < 6.0:
					_click_select(mb.position)
				else:
					_box_select(drag_from, mb.position)
		elif mb.button_index == MOUSE_BUTTON_RIGHT and mb.pressed:
			_right_click(mb.position)
	elif ev is InputEventMouseMotion and dragging:
		var mm := ev as InputEventMouseMotion
		if drag_from.distance_to(mm.position) > 6.0:
			ui.selbox.visible = true
			ui.selbox.set("a", drag_from); ui.selbox.set("b", mm.position)
			ui.selbox.queue_redraw()
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
					cmd_mode = "attack"
			KEY_M: cmd_mode = "move"
			KEY_H: order_hold()
			KEY_ESCAPE: cmd_mode = ""


func _click_select(pos: Vector2) -> void:
	if cmd_mode != "":
		var mode := cmd_mode
		cmd_mode = ""
		var g = _screen_ground(pos)
		if g != null:
			if mode == "move":
				order_selection_move(g)
			else:
				for u in selection:
					u.order_move(g, true)
			fx_ring(g, 1.0, Color(0.4, 1, 0.4))
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
	cmd_mode = ""
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

var ui: Hud
var cmd_mode := ""


func _build_hud() -> void:
	hud = CanvasLayer.new()
	add_child(hud)
	ui = Hud.new()
	ui.game = self
	hud.add_child(ui)


func _update_hud(dt: float) -> void:
	ui.refresh(dt)


func select_only(u: Unit) -> void:
	_clear_sel()
	_select(u)


func order_selection_move(g: Vector3) -> void:
	var n := selection.size()
	for i in n:
		var a := i * 2.4
		var r := 0.0 if n == 1 else 1.6 + 0.5 * (i / 6)
		var off := Vector3(cos(a) * r, 0, sin(a) * r) if i > 0 else Vector3.ZERO
		selection[i].order_move(g + off)


func order_hold() -> void:
	for u in selection:
		u.order_hold()


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
	if Input.is_key_pressed(KEY_UP) or (not no_edge and mp.y < edge and mp.y >= 0 and DisplayServer.window_is_focused()): mv.z -= 1
	if Input.is_key_pressed(KEY_DOWN) or (not no_edge and mp.y > vp.y - edge and DisplayServer.window_is_focused()): mv.z += 1
	if Input.is_key_pressed(KEY_LEFT) or (not no_edge and mp.x < edge and mp.x >= 0 and DisplayServer.window_is_focused()): mv.x -= 1
	if Input.is_key_pressed(KEY_RIGHT) or (not no_edge and mp.x > vp.x - edge and DisplayServer.window_is_focused()): mv.x += 1
	if mv != Vector3.ZERO:
		var m := Basis(Vector3.UP, cam_yaw) * mv.normalized()
		pivot.position += m * cam_zoom * 1.1 * dt
	if hero.state != Unit.S.DEAD and Input.is_key_pressed(KEY_F):
		pivot.position = pivot.position.lerp(hero.global_position, 0.2)
	pivot.position.x = clampf(pivot.position.x, -FIELD, FIELD)
	pivot.position.z = clampf(pivot.position.z, -FIELD, FIELD)
	pivot.position.y = lerpf(pivot.position.y, terrain.height(pivot.position.x, pivot.position.z), 0.12)
	cam.position = cam.position.lerp(Vector3(0, sin(deg_to_rad(55.0)) * cam_zoom, cos(deg_to_rad(55.0)) * cam_zoom), 0.2)
	pivot.rotation.y = cam_yaw
	shake = move_toward(shake, 0.0, dt * 1.6)
	cam.h_offset = randf_range(-1.0, 1.0) * shake * 0.5
	cam.v_offset = randf_range(-1.0, 1.0) * shake * 0.5
	# бойцы
	if not finished:
		for u in units.duplicate():
			if is_instance_valid(u): u.tick(dt)
		_camps_tick(dt)
		_projectiles_tick(dt)
		_chests_tick()
	fog.update(dt)
	_update_hud(dt)
	if not auto:
		_update_cursor(mp)
	if auto and hero.state != Unit.S.DEAD:
		pivot.position = pivot.position.lerp(hero.global_position, 0.08)
	if auto:
		_auto_tick(dt)
	if fx_test:
		_fx_test(dt)
	if shot_dir != "":
		_shots(dt)


## Проверка эффектов способностей: каждые 3 с срабатывает следующая, снимок через 0.45 с.
func _fx_test(dt: float) -> void:
	fx_clock += dt
	hero.level = 4
	var step := fx_clock - 1.0
	if step < 0.0 or fx_i >= 4:
		if fx_i >= 4 and fx_clock > 14.5:
			get_tree().quit()
		return
	var phase := fmod(step, 3.0)
	var idx := int(step / 3.0)
	if idx != fx_i:
		return
	if phase < 0.1 and ab_ready[idx] <= t_game:
		cast(idx)
		fx_shot_at = fx_clock + 0.45
	if fx_shot_at > 0.0 and fx_clock >= fx_shot_at:
		fx_shot_at = -1.0
		DirAccess.make_dir_recursive_absolute(shot_dir)
		get_viewport().get_texture().get_image().save_png("%s/fx_%d.png" % [shot_dir, idx])
		fx_i += 1


var fx_shot_at := -1.0


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
			tg.hurt(p["dmg"] * randf_range(0.85, 1.15), p["src"], 6.0)
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


# ---------------------------------------------------------------- курсор

var cur_cursor := "?"
var cursor_tex := {}


## Курсоры нарисованного набора (res://assets/ui/gen/c_*.png), остриё берётся из cursor_hotspots.json.
var cursor_hot := {}


func _cursor_tex(name: String) -> Texture2D:
	if not cursor_tex.has(name):
		if cursor_hot.is_empty():
			cursor_hot = JSON.parse_string(FileAccess.get_file_as_string("res://assets/ui/gen/cursor_hotspots.json"))
		var img: Image = (load("res://assets/ui/gen/%s.png" % name) as Texture2D).get_image()
		img.convert(Image.FORMAT_RGBA8)
		cursor_tex[name] = ImageTexture.create_from_image(img)
	return cursor_tex[name]


func _set_cursor(name: String) -> void:
	var t := _cursor_tex(name)
	var h: Array = cursor_hot.get(name, [20, 20])
	Input.set_custom_mouse_cursor(t, Input.CURSOR_ARROW, Vector2(h[0], h[1]))
	Input.set_custom_mouse_cursor(t, Input.CURSOR_POINTING_HAND, Vector2(h[0], h[1]))


func _update_cursor(mp: Vector2) -> void:
	var want := "c_arrow"
	if not ui.over_ui(mp) and not finished:
		var g = _screen_ground(mp)
		if cmd_mode == "attack":
			want = "c_attackmove"
		elif cmd_mode == "move":
			want = "c_move"
		elif not selection.is_empty() and _unit_at(mp, 1) != null:
			want = "c_enemy"
		elif _unit_at(mp, 0) != null:
			want = "c_friend"
		elif g != null and not selection.is_empty():
			for c in chests:
				if not c["opened"] and fog.explored(c["pos"]) and Vector2(g.x - c["pos"].x, g.z - c["pos"].z).length() < 2.2:
					want = "c_pickup"
					break
			if want == "c_arrow" and nav.is_blocked(g.x, g.z) and fog.explored(g):
				want = "c_blocked"
	if want == cur_cursor:
		return
	cur_cursor = want
	_set_cursor(want)


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


# ---------------------------------------------------------------- украшения

func _scenes(names: Array) -> Array:
	var out := []
	for n in names:
		out.append(load("res://assets/nature/prefabs/%s.tscn" % n))
	return out


## block: радиус непроходимого круга (0 — проходимо, -1 — по габаритам предмета).
func _put(ps: PackedScene, p: Vector3, s: float, rng: RandomNumberGenerator, block := 0.0) -> Node3D:
	var n := ps.instantiate() as Node3D
	n.position = ground(p)
	n.rotation.y = rng.randf() * TAU
	n.scale = Vector3.ONE * s
	add_child(n)
	fog.register(n, n.position)
	if block != 0.0:
		nav.add_circle(n.position, block if block > 0.0 else _footprint(n))
	return n


func _hex(name: String, p: Vector3, s: float, yaw := 0.0, block := true) -> Node3D:
	var n := (load("res://assets/hex/%s.gltf" % name) as PackedScene).instantiate() as Node3D
	n.position = ground(p)
	n.rotation.y = yaw
	n.scale = Vector3.ONE * s
	add_child(n)
	fog.register(n, n.position)
	if block:
		nav.add_circle(n.position, _footprint(n))
	return n
