class_name Unit
extends Node3D
## Один боец: модель с анимациями, здоровье, приказы и простой ближний/дальний бой.

enum S { IDLE, MOVE, ATTACK, DEAD }

const KINDS := {
	"hero":    {"model": "x",  "hp": 650.0, "dmg": 34.0, "reach": 1.9,  "ranged": false, "speed": 5.0, "cd": 1.0, "scale": 1.3,  "name": "Каэл Вейн"},
	"footman": {"model": "Male_Peasant", "hp": 150.0, "dmg": 14.0, "reach": 1.7,  "ranged": false, "speed": 4.3, "cd": 1.1, "scale": 1.0,  "name": "Ополченец"},
	"archer":  {"model": "Male_Ranger",  "hp": 85.0,  "dmg": 13.0, "reach": 15.0, "ranged": true,  "speed": 4.2, "cd": 1.7, "scale": 0.95, "name": "Стрелок"},
	"raider":  {"model": "Male_Peasant", "hp": 110.0, "dmg": 11.0, "reach": 1.7,  "ranged": false, "speed": 4.4, "cd": 1.1, "scale": 1.0,  "name": "Налётчик"},
	"rarcher": {"model": "Male_Ranger",  "hp": 70.0,  "dmg": 11.0, "reach": 15.0, "ranged": true,  "speed": 4.2, "cd": 1.8, "scale": 0.95, "name": "Лучник"},
	"rider":   {"model": "Male_Ranger",  "hp": 120.0, "dmg": 12.0, "reach": 1.8,  "ranged": false, "speed": 6.5, "cd": 0.9, "scale": 1.05, "name": "Разведчик"},
	"boss":    {"model": "Male_Ranger",  "hp": 800.0, "dmg": 36.0, "reach": 2.0,  "ranged": false, "speed": 4.6, "cd": 1.0, "scale": 1.5,  "name": "Хан Кобек"},
}


const LOOKS := {
	"hero": {"file": "Knight", "show": {"handslot_r": ["1H_Sword"], "handslot_l": ["Round_Shield"], "head": ["Knight_Helmet"], "chest": ["Knight_Cape"]}, "attacks": ["1H_Melee_Attack_Slice_Diagonal", "1H_Melee_Attack_Chop", "1H_Melee_Attack_Slice_Horizontal"]},
	"footman": {"file": "Knight", "show": {"handslot_r": ["1H_Sword"], "handslot_l": ["Rectangle_Shield"], "head": ["Knight_Helmet"]}, "attacks": ["1H_Melee_Attack_Chop", "1H_Melee_Attack_Stab"]},
	"archer": {"file": "Rogue_Hooded", "show": {"handslot_r": ["1H_Crossbow"], "chest": ["Rogue_Cape"]}, "attacks": ["1H_Ranged_Shoot"]},
	"raider": {"file": "Barbarian", "show": {"handslot_r": ["1H_Axe"], "handslot_l": ["Barbarian_Round_Shield"], "head": ["Barbarian_Hat"]}, "tint": Color(1.0, 0.5, 0.45), "attacks": ["1H_Melee_Attack_Chop", "1H_Melee_Attack_Slice_Horizontal"]},
	"rarcher": {"file": "Rogue", "show": {"handslot_r": ["1H_Crossbow"], "chest": ["Rogue_Cape"]}, "tint": Color(1.0, 0.5, 0.45), "attacks": ["1H_Ranged_Shoot"]},
	"rider": {"file": "Rogue", "show": {"handslot_r": ["Knife"], "handslot_l": ["Knife_Offhand"], "chest": ["Rogue_Cape"]}, "tint": Color(1.0, 0.55, 0.3), "attacks": ["Dualwield_Melee_Attack_Slice", "Dualwield_Melee_Attack_Stab"]},
	"boss": {"file": "Barbarian", "show": {"handslot_r": ["2H_Axe"], "head": ["Barbarian_Hat"], "chest": ["Barbarian_Cape"]}, "tint": Color(0.85, 0.35, 0.3), "idle": "2H_Melee_Idle", "attacks": ["2H_Melee_Attack_Chop", "2H_Melee_Attack_Slice", "2H_Melee_Attack_Spin"]},
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
	load_shared()
	var cfg: Dictionary = LOOKS[k]
	model = (load("res://assets/kk/%s.glb" % cfg["file"]) as PackedScene).instantiate()
	model.scale = Vector3.ONE * d["scale"]
	radius = 0.55 * d["scale"]
	add_child(model)
	_dress(model, cfg)
	_tint(model, cfg.get("tint", Color(1, 1, 1)))
	anim = model.find_child("AnimationPlayer", true, false) as AnimationPlayer
	for n in ["Idle", "Idle_Combat", "Running_A", "Running_B", "Walking_A", "2H_Melee_Idle"]:
		if anim.has_animation(n):
			anim.get_animation(n).loop_mode = Animation.LOOP_LINEAR
	a_idle = cfg.get("idle", "Idle")
	a_run = cfg.get("run", "Running_A")
	a_attacks = cfg.get("attacks", ["1H_Melee_Attack_Slice_Diagonal"])
	a_hit = "Hit_A"
	a_die = "Death_A"
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
	bar.position.y = 2.6 * d["scale"] + 0.2
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
	if not anim.has_animation(n):
		return
	if cur == n and not restart:
		return
	cur = n
	anim.speed_scale = spd
	anim.play(n, 0.12)


# ---------------------------------------------------------------- приказы

func order_move(p: Vector3, amove := false) -> void:
	goal = p; has_goal = true; attack_move = amove; hold = false
	target = null; forced = false


func order_attack(t: Unit) -> void:
	target = t; forced = true; has_goal = false; attack_move = false; hold = false


func order_stop() -> void:
	target = null; forced = false; has_goal = false; attack_move = false


# ---------------------------------------------------------------- урон

func hurt(amount: float, src: Unit) -> void:
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
	if state == S.IDLE and swing < 0.0 and stun <= 0.0 and randf() < 0.35:
		play(a_hit, true)
		cur = "hit"


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
	if state == S.DEAD:
		dead_t += dt
		if dead_t > 8.0:
			position.y -= dt * 0.4
			if dead_t > 12.0:
				game.units.erase(self)
				queue_free()
		return
	cd -= dt
	if taunt_t > 0.0: taunt_t -= dt
	if buff_t > 0.0: buff_t -= dt
	if guard_t > 0.0: guard_t -= dt
	(bar.material_override as ShaderMaterial).set_shader_parameter("ratio", clampf(hp / max_hp, 0.0, 1.0))
	if stun > 0.0:
		stun -= dt
		play(a_idle)
		return
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
				play(a_attack(), true)
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
			return
	state = S.IDLE
	if cur != "hit" and cur != "atk":
		play(a_idle)
	elif not anim.is_playing():
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
		target.hurt(m * randf_range(0.85, 1.15), self)
	cur = ""


func _dist(o: Unit) -> float:
	return Vector2(o.global_position.x - global_position.x, o.global_position.z - global_position.z).length()


func _face(p: Vector3, dt: float) -> void:
	var dir := Vector2(p.x - global_position.x, p.z - global_position.z)
	if dir.length() < 0.01:
		return
	var a := atan2(dir.x, dir.y)
	rotation.y = lerp_angle(rotation.y, a, clampf(dt * 12.0, 0.0, 1.0))


func _walk_to(p: Vector3, dt: float, stop: float) -> void:
	var dir := Vector3(p.x - global_position.x, 0.0, p.z - global_position.z)
	var dist := dir.length()
	if dist <= stop:
		play(a_idle)
		return
	dir /= dist
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
	global_position += v * dt
	global_position.y = 0.0
	_face(global_position + v, dt)
	play(a_run, false, clampf(speed / 4.6, 0.8, 1.4))


var a_idle := "Idle"
var a_run := "Running_A"
var a_hit := "Hit_A"
var a_die := "Death_A"
var a_attacks: Array = ["1H_Melee_Attack_Slice_Diagonal"]


func a_attack() -> String:
	return a_attacks[randi() % a_attacks.size()]


## Показать только нужное оружие и одежду: в моделях KayKit все предметы лежат на костях сразу.
static func _dress(model: Node, cfg: Dictionary) -> void:
	var skel := model.find_child("Skeleton3D", true, false) as Skeleton3D
	var show: Dictionary = cfg.get("show", {})
	for att in skel.get_children():
		if att is BoneAttachment3D:
			var keep: Array = show.get(att.name, [])
			for m in att.get_children():
				m.visible = m.name in keep


## Готовая одетая модель бойца вида k (для портрета в интерфейсе).
static func make_model(k: String) -> Node3D:
	var cfg: Dictionary = LOOKS[k]
	var m := (load("res://assets/kk/%s.glb" % cfg["file"]) as PackedScene).instantiate() as Node3D
	_dress(m, cfg)
	_tint(m, cfg.get("tint", Color(1, 1, 1)))
	var ap := m.find_child("AnimationPlayer", true, false) as AnimationPlayer
	if ap and ap.has_animation("Idle"):
		ap.get_animation("Idle").loop_mode = Animation.LOOP_LINEAR
		ap.play(cfg.get("idle", "Idle"))
	return m


var hold := false

func order_hold() -> void:
	target = null; forced = false; has_goal = false; attack_move = false
	hold = true
