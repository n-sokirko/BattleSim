class_name Unit
extends Node3D
## Один боец: модель с анимациями, здоровье, приказы и простой ближний/дальний бой.

enum S { IDLE, MOVE, ATTACK, DEAD }

const KINDS := {
	"hero":    {"model": "x",  "hp": 650.0, "dmg": 34.0, "reach": 1.9,  "ranged": false, "speed": 5.0, "cd": 1.0, "scale": 1.4,  "name": "Каэл Вейн"},
	"footman": {"model": "Male_Peasant", "hp": 150.0, "dmg": 14.0, "reach": 1.7,  "ranged": false, "speed": 4.3, "cd": 1.1, "scale": 1.3,  "name": "Ополченец"},
	"archer":  {"model": "Male_Ranger",  "hp": 85.0,  "dmg": 13.0, "reach": 15.0, "ranged": true,  "speed": 4.2, "cd": 1.7, "scale": 1.25, "name": "Стрелок"},
	"raider":  {"model": "Male_Peasant", "hp": 110.0, "dmg": 11.0, "reach": 1.7,  "ranged": false, "speed": 4.4, "cd": 1.1, "scale": 1.3,  "name": "Налётчик"},
	"rarcher": {"model": "Male_Ranger",  "hp": 70.0,  "dmg": 11.0, "reach": 15.0, "ranged": true,  "speed": 4.2, "cd": 1.8, "scale": 1.25, "name": "Лучник"},
	"rider":   {"model": "Male_Ranger",  "hp": 120.0, "dmg": 12.0, "reach": 1.8,  "ranged": false, "speed": 6.5, "cd": 0.9, "scale": 1.25, "name": "Разведчик"},
	"boss":    {"model": "Male_Ranger",  "hp": 800.0, "dmg": 36.0, "reach": 2.0,  "ranged": false, "speed": 4.6, "cd": 1.0, "scale": 1.7,  "name": "Хан Кобек"},
}


static var models := {}
static var lib: AnimationLibrary
static var bar_shader: Shader

var game
var team := 0
var kind := "footman"
var title := ""
var is_hero := false
var is_boss := false
var max_hp := 100.0
var hp := 100.0
var dmg := 10.0
var reach := 1.7
var ranged := false
var speed := 4.0
var cooldown := 1.0
var radius := 0.55
var cd := 0.0
var state := S.IDLE
var target: Unit = null
var forced := false         # цель назначена приказом, а не найдена сама
var goal := Vector3.ZERO
var has_goal := false
var attack_move := false
var asleep := false
var camp = null
var home := Vector3.ZERO
var stun := 0.0
var poise := 100.0
var poise_max := 100.0
var poise_t := 0.0
var combo := 0
var combo_t := 0.0
var taunt_by: Unit = null
var taunt_t := 0.0
var buff_t := 0.0
var guard_t := 0.0
var level := 1
var xp := 0.0
var selected := false
var anim: AnimationPlayer
var model: Node3D
var ring: MeshInstance3D
var bar: MeshInstance3D
var cur := ""
var swing := -1.0
var dead_t := 0.0
var kill_by: Unit = null


static func load_shared() -> void:
	if lib != null:
		return
	var ual = load("res://assets/chars/UAL1_Standard.glb").instantiate()
	lib = ual.get_node("AnimationPlayer").get_animation_library("")
	for n in ["Idle", "Walk", "Jog_Fwd", "Sword_Idle", "Spell_Simple_Idle"]:
		if lib.has_animation(n):
			lib.get_animation(n).loop_mode = Animation.LOOP_LINEAR
	ual.queue_free()
	bar_shader = Shader.new()
	bar_shader.code = """shader_type spatial;
render_mode unshaded, depth_draw_never, cull_disabled;
uniform float ratio = 1.0;
uniform vec4 col : source_color = vec4(0.3, 0.8, 0.3, 1.0);
void vertex() {
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
}
void fragment() {
	float f = step(UV.x, ratio);
	vec3 c = mix(vec3(0.07, 0.07, 0.08), col.rgb, f);
	if (UV.y < 0.15 || UV.y > 0.85 || UV.x < 0.015 || UV.x > 0.985) c = vec3(0.0);
	ALBEDO = c;
}
"""


func setup(k: String, t: int, g) -> void:
	game = g
	kind = k
	team = t
	var d: Dictionary = KINDS[k]
	title = d["name"]
	max_hp = d["hp"]; hp = max_hp
	dmg = d["dmg"]; reach = d["reach"]; ranged = d["ranged"]
	speed = d["speed"]; cooldown = d["cd"]
	is_hero = k == "hero"
	is_boss = k == "boss"
	poise_max = 160.0 if is_hero else (320.0 if is_boss else 100.0)
	poise = poise_max
	load_shared()
	var cfg: Dictionary = LOOKS[k]
	model = (load("res://assets/hy/%s.glb" % cfg["file"]) as PackedScene).instantiate()
	model.scale = Vector3.ONE * d["scale"]
	radius = 0.55 * d["scale"]
	add_child(model)
	if cfg.get("baked", false):
		# оружие слито с моделью, клипы Hunyuan лежат в самом GLB
		anim = model.find_child("AnimationPlayer", true, false) as AnimationPlayer
		for n in ["idle", "walk", "run"]:
			if anim.has_animation(n):
				anim.get_animation(n).loop_mode = Animation.LOOP_LINEAR
		# удары мечом: клипы KayKit, перенесённые на этот скелет (scripts/retarget_a.gd); движение, урон и смерть — Hunyuan
		anim.add_animation_library("kk", load("res://assets/hy/anims_%s.res" % cfg["file"]) as AnimationLibrary)
		anim.add_animation_library("mx", load("res://assets/hy/mx_pro_sns_%s.res" % cfg["file"]) as AnimationLibrary)
		equip(cfg["main"], "")
		a_idle = "idle"; a_run = "run"; a_hit = "hit"; a_die = "death"
		a_attacks = ["mx/sword_and_shield_slash", "mx/sword_and_shield_slash_3", "mx/sword_and_shield_attack_4", "mx/sword_and_shield_slash_2"]
		atk_speed = 1.5
		cur = ""
		play(a_idle)
	else:
		# проигрыватель анимаций: клипы KayKit, перенесённые на этот скелет (res://assets/hy/anims_<имя>.res)
		anim = AnimationPlayer.new()
		model.add_child(anim)
		var al := load("res://assets/hy/anims_%s.res" % cfg["file"]) as AnimationLibrary
		for n in al.get_animation_list():
			if n in ["Idle", "2H_Melee_Idle", "Walking_A", "Running_A", "Running_B"]:
				al.get_animation(n).loop_mode = Animation.LOOP_LINEAR
		anim.add_animation_library("kk", al)
		use_mx_grip = cfg.has("mx")
		_equip_default()
		if cfg.has("mx"):
			_apply_mx(cfg["mx"], cfg["file"])
	# кольцо под ногами: цвет армии
	ring = MeshInstance3D.new()
	var tm := TorusMesh.new()
	tm.inner_radius = radius * 1.05; tm.outer_radius = radius * 1.3
	ring.mesh = tm
	ring.scale = Vector3(1, 0.06, 1)
	ring.position.y = 0.05
	var rm := StandardMaterial3D.new()
	rm.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	rm.albedo_color = Color(0.25, 0.55, 1.0) if team == 0 else Color(0.95, 0.25, 0.2)
	ring.material_override = rm
	add_child(ring)
	# полоска здоровья
	bar = MeshInstance3D.new()
	var q := QuadMesh.new()
	q.size = Vector2(1.5 if (is_hero or is_boss) else 1.1, 0.16)
	bar.mesh = q
	bar.position.y = 1.95 * d["scale"] + 0.2
	var sm := ShaderMaterial.new()
	sm.shader = bar_shader
	sm.set_shader_parameter("col", Color(0.3, 0.85, 0.35) if team == 0 else Color(0.9, 0.3, 0.25))
	bar.material_override = sm
	add_child(bar)
	home = position
	play(a_idle)


static func _tint(n: Node, c: Color) -> void:
	if n is MeshInstance3D:
		var mi := n as MeshInstance3D
		for i in mi.mesh.get_surface_count():
			var m := mi.get_active_material(i)
			if m is StandardMaterial3D:
				var m2 := (m as StandardMaterial3D).duplicate() as StandardMaterial3D
				m2.albedo_color = m2.albedo_color * c
				mi.set_surface_override_material(i, m2)
	for ch in n.get_children():
		_tint(ch, c)


func set_selected(v: bool) -> void:
	selected = v
	var rm := ring.material_override as StandardMaterial3D
	if v:
		rm.albedo_color = Color(0.3, 1.0, 0.35)
		ring.scale = Vector3(1.35, 0.1, 1.35)
	else:
		rm.albedo_color = Color(0.25, 0.55, 1.0) if team == 0 else Color(0.95, 0.25, 0.2)
		ring.scale = Vector3(1, 0.06, 1)


func play(n: String, restart := false, spd := 1.0) -> void:
	if anim == null:
		cur = n
		return
	if not anim.has_animation(n):
		return
	if cur == n and not restart:
		return
	cur = n
	anim.speed_scale = spd
	anim.play(n, 0.15)


# ---------------------------------------------------------------- приказы

func order_move(p: Vector3, amove := false) -> void:
	goal = p; has_goal = true; attack_move = amove; hold = false
	target = null; forced = false


func order_attack(t: Unit) -> void:
	target = t; forced = true; has_goal = false; attack_move = false; hold = false


func order_stop() -> void:
	target = null; forced = false; has_goal = false; attack_move = false


# ---------------------------------------------------------------- урон

## poise_dmg: сколько стойкости снимает удар. Когда стойкость кончается, цель сбита: атака прервана, играет анимация урона.
func hurt(amount: float, src: Unit, poise_dmg := 14.0) -> void:
	if state == S.DEAD:
		return
	if guard_t > 0.0:
		amount *= 0.65 if not is_hero else 0.6
	if is_hero:
		amount *= pow(0.94, level - 1)
	hp -= amount
	game.fx_hit(global_position + Vector3(0, 1.2, 0))
	if hp <= 0.0:
		die(src)
		return
	if src != null and not forced and not has_goal and target == null and not asleep:
		target = src
	poise_t = 0.0
	if guard_t <= 0.0 and poise_dmg > 0.0:
		poise -= poise_dmg
		if poise <= 0.0:
			stagger(0.8 if poise_dmg >= 50.0 else 0.55)


## Сбить цель: прервать замах, сыграть анимацию урона, отложить следующий удар.
func stagger(dur: float) -> void:
	if state == S.DEAD:
		return
	poise = poise_max
	stun = maxf(stun, dur)
	swing = -1.0
	cd = maxf(cd, dur + 0.25)
	cur = "hit"
	play(a_hit, true)
	game.fx_stagger(global_position)


func die(src: Unit) -> void:
	state = S.DEAD
	kill_by = src
	hp = 0.0
	target = null
	play(a_die, true)
	ring.visible = false
	bar.visible = false
	if selected:
		set_selected(false)
	game.on_death(self)


# ---------------------------------------------------------------- шаг

func tick(dt: float) -> void:
	_tick_logic(dt)


func _tick_logic(dt: float) -> void:
	if state == S.DEAD:
		dead_t += dt
		if dead_t > 8.0:
			position.y -= dt * 0.4
			if dead_t > 12.0:
				game.units.erase(self)
				queue_free()
		return
	global_position.y = game.terrain.height(global_position.x, global_position.z)
	cd -= dt
	if taunt_t > 0.0: taunt_t -= dt
	if buff_t > 0.0: buff_t -= dt
	if guard_t > 0.0: guard_t -= dt
	(bar.material_override as ShaderMaterial).set_shader_parameter("ratio", clampf(hp / max_hp, 0.0, 1.0))
	poise_t += dt
	combo_t += dt
	if combo_t > 3.5:
		combo = 0
	if poise_t > 2.0:
		poise = minf(poise_max, poise + 30.0 * dt)
	if stun > 0.0:
		stun -= dt
		if cur == "hit" and anim != null and not anim.is_playing():
			cur = ""
		if cur != "hit":
			play(a_idle)
		return
	if (cur == "hit" or cur == "atk") and anim != null and not anim.is_playing():
		cur = ""
	# атака в разгаре: ждём момента удара
	if swing >= 0.0:
		swing += dt
		if swing >= 0.38:
			swing = -1.0
			_strike()
		else:
			return
	if asleep and not has_goal:
		state = S.IDLE
		play(a_idle)
		return
	if taunt_t > 0.0 and taunt_by != null and taunt_by.state != S.DEAD:
		target = taunt_by; forced = true
	if target != null and target.state == S.DEAD:
		target = null; forced = false
	# сам выбирает цель, если нет приказа идти
	if target == null and not asleep and (not has_goal or attack_move):
		var r := 9.0
		if team == 1: r = 14.0
		if attack_move: r = 16.0
		if hold: r = reach + 1.5
		target = game.nearest_enemy(self, r)
	if target != null:
		var d := _dist(target)
		var need := reach + target.radius if not ranged else reach
		if d <= need:
			state = S.ATTACK
			_face(target.global_position, dt)
			if cd <= 0.0:
				cd = cooldown * randf_range(0.9, 1.1)
				swing = 0.0
				play(a_attack(), true, atk_speed)
				cur = "atk"
			elif cur != "atk":
				play(a_idle)
		else:
			if hold and not forced:
				target = null
				play(a_idle)
				return
			state = S.MOVE
			_walk_to(target.global_position, dt, need * 0.85)
		# не гнаться бесконечно за недосягаемым (лагерь на привязи)
		if team == 1 and camp != null and not asleep and global_position.distance_to(home) > 60.0 and not forced:
			target = null
		return
	if has_goal:
		var gd := Vector2(goal.x - global_position.x, goal.z - global_position.z).length()
		if gd < 0.8:
			has_goal = false; attack_move = false
			if asleep: position = position
		else:
			state = S.MOVE
			_walk_to(goal, dt, 0.5)
			if walk_end:
				has_goal = false; attack_move = false
			return
	state = S.IDLE
	if cur != "hit" and cur != "atk":
		play(a_idle)
	elif anim != null and not anim.is_playing():
		cur = ""


func _strike() -> void:
	if target == null or target.state == S.DEAD:
		return
	var m := dmg * (1.0 + 0.1 * (level - 1))
	if buff_t > 0.0:
		m *= 1.25
	if ranged:
		game.shoot(self, target, m)
	elif _dist(target) <= reach + target.radius + 1.0:
		# каждый третий удар подряд — комбо-финиш: сильнее и надёжно сбивает цель
		combo += 1
		combo_t = 0.0
		var heavy := combo % 3 == 0
		if heavy:
			m *= 1.4
		target.hurt(m * randf_range(0.85, 1.15), self, 70.0 if heavy else 14.0)
	# cur остаётся "atk": клип удара доигрывается до конца, а не обрывается стойкой


func _dist(o: Unit) -> float:
	return Vector2(o.global_position.x - global_position.x, o.global_position.z - global_position.z).length()


func _face(p: Vector3, dt: float) -> void:
	var dir := Vector2(p.x - global_position.x, p.z - global_position.z)
	if dir.length() < 0.01:
		return
	var a := atan2(dir.x, dir.y)
	rotation.y = lerp_angle(rotation.y, a, clampf(dt * 12.0, 0.0, 1.0))


var path_pts := PackedVector3Array()
var path_i := 0
var path_goal := Vector3(1e9, 0.0, 1e9)
var path_t := 0.0
var walk_end := false     # дошёл до последней доступной точки (цель за препятствием)


## Ближайшая точка пути к p: путь пересчитывается при смене цели и раз в полсекунды.
func _steer_point(p: Vector3, dt: float) -> Vector3:
	path_t -= dt
	if path_t <= 0.0 or Vector2(p.x - path_goal.x, p.z - path_goal.z).length() > 1.5:
		path_t = randf_range(0.4, 0.7)
		path_goal = p
		path_pts = game.nav.find(global_position, p)
		path_i = 0
	while path_i < path_pts.size() - 1 and Vector2(path_pts[path_i].x - global_position.x, path_pts[path_i].z - global_position.z).length() < 0.8:
		path_i += 1
	return path_pts[path_i] if path_i < path_pts.size() else p


func _walk_to(p: Vector3, dt: float, stop: float) -> void:
	walk_end = false
	var dir := Vector3(p.x - global_position.x, 0.0, p.z - global_position.z)
	var dist := dir.length()
	if dist <= stop:
		play(a_idle)
		return
	dir /= dist
	var sp := _steer_point(p, dt)
	var to_sp := Vector3(sp.x - global_position.x, 0.0, sp.z - global_position.z)
	if to_sp.length() > 0.001:
		dir = to_sp.normalized()
		if path_i >= path_pts.size() - 1 and to_sp.length() < 0.6:
			walk_end = true
			play(a_idle)
			return
	# расталкиваемся с соседями
	var push := Vector3.ZERO
	for o in game.units:
		if o == self or o.state == S.DEAD:
			continue
		var dd: Vector3 = global_position - o.global_position
		dd.y = 0.0
		var l := dd.length()
		var min_d: float = radius + o.radius
		if l < min_d and l > 0.001:
			push += dd / l * (min_d - l) * 2.2
	var v := (dir + push).limit_length(1.0) * speed
	if game.terrain.is_deep(global_position.x, global_position.z):
		v *= 0.6            # глубокая вода замедляет, брод — нет
	var old := global_position
	var np := old + v * dt
	if game.nav.is_blocked(np.x, np.z) and not game.nav.is_blocked(old.x, old.z):
		# упёрся в препятствие: скользим вдоль него или стоим
		var sx := Vector3(np.x, old.y, old.z)
		var sz := Vector3(old.x, old.y, np.z)
		if not game.nav.is_blocked(sx.x, sx.z):
			np = sx
		elif not game.nav.is_blocked(sz.x, sz.z):
			np = sz
		else:
			np = old
	# в гору медленнее, под гору чуть быстрее
	var step := Vector2(np.x - old.x, np.z - old.z).length()
	if step > 0.0001:
		var grade: float = (game.terrain.height(np.x, np.z) - game.terrain.height(old.x, old.z)) / step
		var k := clampf(1.0 - grade * 0.9, 0.55, 1.2)
		np = old + (np - old) * k
	global_position = np
	global_position.y = game.terrain.height(global_position.x, global_position.z)
	_face(global_position + v, dt)
	play(a_run, false, clampf(speed / 4.6, 0.8, 1.4))


var atk_speed := 1.0
var a_idle := "kk/Idle"
var a_run := "kk/Running_A"
var a_hit := "kk/Hit_A"
var a_die := "kk/Death_A"
var a_attacks: Array = ["kk/1H_Melee_Attack_Slice_Diagonal"]


func a_attack() -> String:
	return a_attacks[randi() % a_attacks.size()]


var hold := false


func order_hold() -> void:
	target = null; forced = false; has_goal = false; attack_move = false
	hold = true


# ---------------------------------------------------------------- оружие и наборы анимаций

## Оружие: модель, в какой руке, какой набор анимаций оно задаёт (атаки разным оружием работают по-разному).
const WEAPONS := {
	"sword":        {"prop": "sword_knight",  "hand": "RightHand", "set": "melee1h"},
	"sword_short":  {"prop": "sword_short",   "hand": "RightHand", "set": "melee1h"},
	"saber":        {"prop": "saber",         "hand": "RightHand", "set": "melee1h"},
	"axe2":         {"prop": "axe2",          "hand": "RightHand", "set": "melee2h"},
	"crossbow":     {"prop": "crossbow",      "hand": "RightHand", "set": "crossbow"},
	"bow":          {"prop": "bow",           "hand": "LeftHand",  "set": "bow"},
	"shield_tower": {"prop": "shield_tower",  "hand": "LeftHand",  "set": ""},
	"shield_rect":  {"prop": "shield_rect",   "hand": "LeftHand",  "set": ""},
	"shield_sun":   {"prop": "shield_sun",    "hand": "LeftHand",  "set": ""},
}

## Наборы анимаций (клипы KayKit, перенесённые на скелет Hunyuan: scripts/retarget.gd).
const ANIM_SETS := {
	"unarmed":  {"idle": "Idle", "walk": "Walking_A", "run": "Running_A", "hit": "Hit_A", "die": "Death_A",
				 "attacks": ["1H_Melee_Attack_Slice_Diagonal"]},
	"melee1h":  {"idle": "Idle", "walk": "Walking_A", "run": "Running_A", "hit": "Hit_A", "die": "Death_A",
				 "attacks": ["1H_Melee_Attack_Slice_Diagonal", "1H_Melee_Attack_Chop", "1H_Melee_Attack_Stab"]},
	"melee2h":  {"idle": "2H_Melee_Idle", "walk": "Walking_A", "run": "Running_A", "hit": "Hit_B", "die": "Death_B",
				 "attacks": ["2H_Melee_Attack_Chop", "2H_Melee_Attack_Slice", "2H_Melee_Attack_Spin"]},
	"crossbow": {"idle": "Idle", "walk": "Walking_A", "run": "Running_A", "hit": "Hit_A", "die": "Death_A",
				 "attacks": ["1H_Ranged_Shoot"]},
	"bow":      {"idle": "Idle", "walk": "Walking_A", "run": "Running_A", "hit": "Hit_A", "die": "Death_A",
				 "attacks": ["2H_Ranged_Shoot"]},
}

## Внешний вид и снаряжение по умолчанию: файл модели, оружие в правой и левой руке.
## Клипы Mixamo по типам оружия (переносятся на скелет Hunyuan: scripts/retarget_mx.gd). Имена: <библиотека>/<клип>.
const MX_SNS := {"packs": ["pro_sns"], "idle": "mx_pro_sns/sword_and_shield_idle", "run": "mx_pro_sns/sword_and_shield_run",
	"hit": "mx_pro_sns/sword_and_shield_impact", "die": "mx_pro_sns/sword_and_shield_death", "speed": 1.5,
	"attacks": ["mx_pro_sns/sword_and_shield_slash", "mx_pro_sns/sword_and_shield_slash_2", "mx_pro_sns/sword_and_shield_slash_3", "mx_pro_sns/sword_and_shield_attack_4"]}
const MX_GREAT := {"packs": ["great_sword"], "idle": "mx_great_sword/great_sword_idle", "run": "mx_great_sword/great_sword_run",
	"hit": "mx_great_sword/great_sword_impact", "die": "mx_great_sword/two_handed_sword_death", "speed": 1.3,
	"attacks": ["mx_great_sword/great_sword_slash", "mx_great_sword/great_sword_slash_2", "mx_great_sword/great_sword_attack"]}
const MX_BOW := {"packs": ["longbow"], "idle": "mx_longbow/standing_idle_01", "run": "mx_longbow/standing_run_forward",
	"hit": "mx_longbow/standing_react_small_from_front", "die": "mx_longbow/standing_death_forward_01", "speed": 1.3,
	"attacks": ["mx_longbow/standing_aim_recoil"]}
const MX_RIFLE := {"packs": ["rifle", "longbow"], "idle": "mx_rifle/idle", "run": "mx_rifle/run_forward",
	"hit": "mx_longbow/standing_react_small_from_front", "die": "mx_rifle/death_from_the_front", "speed": 1.3,
	"attacks": ["mx_rifle/firing_rifle"]}

const LOOKS := {
	"hero":    {"file": "armed_kael",     "main": "sword",       "off": "shield_tower", "baked": true},
	"footman": {"file": "erland_militia", "main": "sword", "off": "shield_rect", "mx": MX_SNS},
	"archer":  {"file": "erland_archer",  "main": "crossbow",    "off": "", "mx": MX_RIFLE},
	"raider":  {"file": "nomad_raider",   "main": "saber",       "off": "shield_sun", "mx": MX_SNS},
	"rarcher": {"file": "nomad_archer",   "main": "bow",         "off": "", "mx": MX_BOW},
	"rider":   {"file": "nomad_archer",   "main": "bow",         "off": "", "mx": MX_BOW},
	"boss":    {"file": "nomad_khan",     "main": "axe2",        "off": "", "mx": MX_GREAT},
}

var gear_nodes: Array = []
var anim_set := "unarmed"
var main_weapon := ""
var off_weapon := ""
static var hand_test := ""
static var hand_test_l := ""
const GRIP_R := Vector3(0, 0, -35)
const GRIP_L := Vector3(0, -90, 0)


## Сменить оружие: меняются и модель в руке, и набор анимаций (атаки, простой, раны, смерть).
func equip(main: String, off := "") -> void:
	for n in gear_nodes:
		if is_instance_valid(n):
			n.queue_free()
	gear_nodes.clear()
	main_weapon = main
	off_weapon = off
	var skel := model.find_child("Skeleton3D", true, false) as Skeleton3D
	for w in [main, off]:
		if w == "" or not WEAPONS.has(w):
			continue
		var wd: Dictionary = WEAPONS[w]
		var att := BoneAttachment3D.new()
		att.bone_name = wd["hand"]
		skel.add_child(att)
		var prop := (load("res://assets/gen/props/%s.glb" % wd["prop"]) as PackedScene).instantiate() as Node3D
		# скелеты Hunyuan: сантиметры с корнем ×0.01; модели с запечённым оружием — уже метры. Масштаб предмета считаем по скелету.
		var sk := 1.0
		var n: Node = skel
		while n != null and n != model:
			if n is Node3D:
				sk *= (n as Node3D).scale.x
			n = n.get_parent()
		prop.scale = Vector3.ONE / maxf(sk, 0.0001)
		var goff := Vector3.ZERO
		if use_mx_grip:
			var o: Vector3 = test_off.get(w, GRIP_OFF.get(w, Vector3.ZERO))
			goff = o / maxf(sk, 0.0001)
		prop.transform = Transform3D(_grip_for(w, wd["hand"] == "RightHand"), goff) * Transform3D(Basis.from_scale(prop.scale), Vector3.ZERO)
		att.add_child(prop)
		gear_nodes.append(att)
	var set_name := "unarmed"
	if main != "" and WEAPONS.has(main):
		set_name = WEAPONS[main]["set"]
	if main == "bow" or (off == "bow"):
		set_name = "bow"
	anim_set = set_name
	var s: Dictionary = ANIM_SETS[set_name]
	a_idle = "kk/" + s["idle"]
	a_run = "kk/" + s["run"]
	a_hit = "kk/" + s["hit"]
	a_die = "kk/" + s["die"]
	a_attacks = []
	for a in s["attacks"]:
		a_attacks.append("kk/" + a)
	cur = ""
	play(a_idle)


## Хват под клипы Mixamo (подобран стендом scripts/grip_test.gd): поворот предмета в кости руки, градусы.
const GRIPS_MX := {
	"sword": Vector3(0, 90, 270), "sword_short": Vector3(0, 0, 270), "saber": Vector3(0, 0, 270),
	"shield_rect": Vector3(0, 0, 90), "shield_tower": Vector3(0, 0, 90), "shield_sun": Vector3(0, 0, 90),
	"bow": Vector3(0, 0, 0), "crossbow": Vector3(0, 0, 0), "axe2": Vector3(0, 0, 0),
}
var use_mx_grip := false
## Смещение предмета в кисти (метры, оси кости руки): щит выносится вперёд от предплечья, чтобы рука не торчала сквозь него.
const GRIP_OFF := {
	"shield_rect": Vector3(0, 0, -0.11), "shield_tower": Vector3(0, 0, -0.11), "shield_sun": Vector3(0, 0, -0.11),
}
static var test_off := {}


func _grip_for(w: String, right: bool) -> Basis:
	if use_mx_grip and GRIPS_MX.has(w):
		var e: Vector3 = GRIPS_MX[w]
		return Basis.from_euler(Vector3(deg_to_rad(e.x), deg_to_rad(e.y), deg_to_rad(e.z)))
	return _grip(right)


## Поворот предмета в кисти (клинок/щит относительно кости руки). Подбирается один раз под скелет Hunyuan.
func _grip(right: bool) -> Basis:
	var src := hand_test if right else hand_test_l
	var e := Vector3(0, 0, 0)
	if src != "":
		var p := src.split(",")
		e = Vector3(float(p[0]), float(p[1]), float(p[2]))
	else:
		e = GRIP_R if right else GRIP_L
	return Basis.from_euler(Vector3(deg_to_rad(e.x), deg_to_rad(e.y), deg_to_rad(e.z)))


## Подключить клипы Mixamo для этого персонажа: простой, бег, урон, смерть и набор ударов.
func _apply_mx(mx: Dictionary, file: String) -> void:
	for pk in mx["packs"]:
		var path := "res://assets/hy/mx_%s_%s.res" % [pk, file]
		if ResourceLoader.exists(path):
			anim.add_animation_library("mx_" + pk, load(path) as AnimationLibrary)
	for n in [mx["idle"], mx["run"]]:
		if anim.has_animation(n):
			anim.get_animation(n).loop_mode = Animation.LOOP_LINEAR
	a_idle = mx["idle"]; a_run = mx["run"]; a_hit = mx["hit"]; a_die = mx["die"]
	a_attacks = []
	for a in mx["attacks"]:
		a_attacks.append(a)
	atk_speed = mx["speed"]
	cur = ""
	play(a_idle)


func _equip_default() -> void:
	var cfg: Dictionary = LOOKS[kind]
	equip(cfg["main"], cfg["off"])


## Портрет: одетая модель без анимаций.
static func make_model(k: String) -> Node3D:
	var cfg: Dictionary = LOOKS[k]
	var m := (load("res://assets/hy/%s.glb" % cfg["file"]) as PackedScene).instantiate() as Node3D
	return m
