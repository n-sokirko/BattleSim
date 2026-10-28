class_name Fx
extends RefCounted
## Эффекты способностей героя: волны на земле, искры, пыль, обломки, трещины, купол, ауры на бойцах.
## Всё строится из простых примитивов и нарисованных вспышек (res://assets/ui/gen/fx_*.png), без внешних частиц.

const T_LEVEL := "res://assets/ui/gen/fx_level.png"
const T_RING := "res://assets/ui/gen/fx_stagger.png"
const T_HIT := "res://assets/ui/gen/fx_hit.png"
const T_SPARK := "res://assets/ui/gen/fx_sparkle.png"
const T_ALERT := "res://assets/ui/gen/fx_alert.png"
const T_MOVE := "res://assets/ui/gen/fx_move.png"


static var _soft_tex: Texture2D


## Мягкое круглое пятно (для пыли и дыма).
static func _soft() -> Texture2D:
	if _soft_tex == null:
		var g := Gradient.new()
		g.colors = PackedColorArray([Color(1, 1, 1, 0.9), Color(1, 1, 1, 0.35), Color(1, 1, 1, 0.0)])
		g.offsets = PackedFloat32Array([0.0, 0.5, 1.0])
		var gt := GradientTexture2D.new()
		gt.gradient = g
		gt.fill = GradientTexture2D.FILL_RADIAL
		gt.fill_from = Vector2(0.5, 0.5)
		gt.fill_to = Vector2(1.0, 0.5)
		gt.width = 64; gt.height = 64
		_soft_tex = gt
	return _soft_tex


static func _mat(tex: Texture2D, col: Color, additive := true, billboard := false) -> StandardMaterial3D:
	var m := StandardMaterial3D.new()
	m.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	m.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	if additive:
		m.blend_mode = BaseMaterial3D.BLEND_MODE_ADD
	m.cull_mode = BaseMaterial3D.CULL_DISABLED
	m.vertex_color_use_as_albedo = true
	m.albedo_color = col
	if tex != null:
		m.albedo_texture = tex
	if billboard:
		m.billboard_mode = BaseMaterial3D.BILLBOARD_ENABLED
	return m


static func _free_later(game: Node, n: Node, sec: float) -> void:
	game.get_tree().create_timer(sec).timeout.connect(func(): if is_instance_valid(n): n.queue_free())


## Плоская вспышка на земле: растёт от r0 до r1 и гаснет.
static func decal(game: Node3D, tex_path: String, p: Vector3, r0: float, r1: float, sec: float, col: Color, delay := 0.0, spin := 0.0) -> void:
	var m := MeshInstance3D.new()
	var q := QuadMesh.new()
	q.size = Vector2(2, 2)
	m.mesh = q
	m.rotation_degrees.x = -90.0
	m.position = p + Vector3(0, 0.12, 0)
	m.scale = Vector3.ONE * r0
	var mat := _mat(load(tex_path), col)
	m.material_override = mat
	m.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	game.add_child(m)
	m.visible = delay <= 0.0
	var tw := game.create_tween()
	if delay > 0.0:
		tw.tween_interval(delay)
		tw.tween_callback(func(): m.visible = true)
	tw.tween_property(m, "scale", Vector3.ONE * r1, sec).set_ease(Tween.EASE_OUT).set_trans(Tween.TRANS_CUBIC)
	tw.parallel().tween_property(mat, "albedo_color:a", 0.0, sec).set_ease(Tween.EASE_IN)
	if spin != 0.0:
		tw.parallel().tween_property(m, "rotation:z", spin, sec)
	tw.tween_callback(m.queue_free)


const RING_SHADER := """
shader_type spatial;
render_mode blend_add, unshaded, cull_disabled, depth_draw_never;
uniform vec4 col : source_color = vec4(1.0);
uniform float thick = 0.1;
uniform float fade = 1.0;
void fragment() {
	vec2 q = UV * 2.0 - 1.0;
	float d = length(q);
	float edge = 1.0 - smoothstep(0.97, 1.0, d);
	float ring = smoothstep(1.0 - thick - 0.06, 1.0 - thick, d) * edge;
	float glow = smoothstep(0.3, 1.0, d) * 0.14 * edge;
	ALBEDO = col.rgb;
	ALPHA = (ring + glow) * col.a * fade;
}
"""


## Чистое кольцо на земле: расходится от r0 до r1 и гаснет (без картинок, края резкие).
static func ring(game: Node3D, p: Vector3, r0: float, r1: float, sec: float, col: Color, delay := 0.0, thick := 0.1) -> void:
	var m := MeshInstance3D.new()
	var q := QuadMesh.new()
	q.size = Vector2(2, 2)
	m.mesh = q
	m.rotation_degrees.x = -90.0
	m.position = p + Vector3(0, 0.14, 0)
	m.scale = Vector3.ONE * r0
	var sh := Shader.new()
	sh.code = RING_SHADER
	var mat := ShaderMaterial.new()
	mat.shader = sh
	mat.set_shader_parameter("col", col)
	mat.set_shader_parameter("thick", thick)
	m.material_override = mat
	m.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	m.visible = delay <= 0.0
	game.add_child(m)
	var tw := game.create_tween()
	if delay > 0.0:
		tw.tween_interval(delay)
		tw.tween_callback(func(): m.visible = true)
	tw.tween_property(m, "scale", Vector3.ONE * r1, sec).set_ease(Tween.EASE_OUT).set_trans(Tween.TRANS_CUBIC)
	tw.parallel().tween_method(func(v: float): mat.set_shader_parameter("fade", v), 1.0, 0.0, sec).set_ease(Tween.EASE_IN)
	tw.tween_callback(m.queue_free)


## Столб света, растущий вверх и тающий.
static func column(game: Node3D, p: Vector3, r: float, h: float, sec: float, col: Color) -> void:
	var m := MeshInstance3D.new()
	var c := CylinderMesh.new()
	c.top_radius = r; c.bottom_radius = r * 1.1; c.height = h
	c.radial_segments = 20
	m.mesh = c
	m.position = p + Vector3(0, h * 0.5, 0)
	var mat := _mat(null, col)
	m.material_override = mat
	m.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	m.scale = Vector3(0.3, 0.1, 0.3)
	game.add_child(m)
	var tw := game.create_tween()
	tw.tween_property(m, "scale", Vector3(1, 1, 1), sec * 0.35).set_ease(Tween.EASE_OUT)
	tw.parallel().tween_property(mat, "albedo_color:a", 0.0, sec).set_ease(Tween.EASE_IN)
	tw.tween_callback(m.queue_free)


static func light(game: Node3D, p: Vector3, col: Color, energy: float, rng: float, sec: float) -> void:
	var l := OmniLight3D.new()
	l.light_color = col
	l.light_energy = energy
	l.omni_range = rng
	l.position = p + Vector3(0, 1.0, 0)
	game.add_child(l)
	var tw := game.create_tween()
	tw.tween_property(l, "light_energy", 0.0, sec)
	tw.tween_callback(l.queue_free)


## Разовый выброс частиц. kind: "spark" (искры), "dust" (пыль), "chunk" (обломки).
static func burst(game: Node3D, p: Vector3, kind: String, amount: int, col_a: Color, col_b: Color, vel: float, life: float, size: float, ring_r := 0.5, up := 1.0) -> void:
	var ps := CPUParticles3D.new()
	var q := QuadMesh.new()
	q.size = Vector2(1, 1)
	if kind == "chunk":
		var bm := BoxMesh.new()
		bm.size = Vector3(0.28, 0.2, 0.22)
		var cm := StandardMaterial3D.new()
		cm.albedo_color = Color(0.36, 0.28, 0.2)
		cm.vertex_color_use_as_albedo = true
		bm.material = cm
		ps.mesh = bm
	else:
		q.material = _mat(load(T_SPARK) if kind == "spark" else _soft(), Color.WHITE, kind == "spark", true)
		ps.mesh = q
	ps.amount = amount
	ps.lifetime = life
	ps.one_shot = true
	ps.explosiveness = 0.95
	ps.emission_shape = CPUParticles3D.EMISSION_SHAPE_RING
	ps.emission_ring_axis = Vector3.UP
	ps.emission_ring_radius = ring_r
	ps.emission_ring_inner_radius = ring_r * 0.6
	ps.emission_ring_height = 0.1
	ps.direction = Vector3(0, up, 0)
	ps.spread = 70.0 if kind != "dust" else 90.0
	ps.initial_velocity_min = vel * 0.5
	ps.initial_velocity_max = vel
	ps.gravity = Vector3(0, -9.0 if kind == "chunk" else (-1.0 if kind == "spark" else 0.6), 0)
	if kind == "dust":
		ps.radial_accel_min = 2.0
		ps.radial_accel_max = 5.0
		ps.damping_min = 2.0
		ps.damping_max = 3.0
	ps.scale_amount_min = size * 0.6
	ps.scale_amount_max = size
	var g := Gradient.new()
	g.colors = PackedColorArray([col_a, col_b, Color(col_b, 0.0)])
	g.offsets = PackedFloat32Array([0.0, 0.5, 1.0])
	ps.color_ramp = g
	if kind == "dust":
		var sc := Curve.new()
		sc.add_point(Vector2(0, 0.5)); sc.add_point(Vector2(1, 1.8))
		ps.scale_amount_curve = sc
	ps.position = p
	ps.emitting = true
	game.add_child(ps)
	_free_later(game, ps, life + 0.6)


## Аура на бойце: искры идут вверх, под ногами мягкое кольцо. Пропадает через sec.
static func aura(game: Node3D, u: Node3D, col: Color, sec: float) -> void:
	var old := u.get_node_or_null("AuraFx")
	if old != null:
		old.queue_free()
	var root := Node3D.new()
	root.name = "AuraFx"
	u.add_child(root)
	var ps := CPUParticles3D.new()
	var q := QuadMesh.new()
	q.size = Vector2(1, 1)
	q.material = _mat(load(T_SPARK), Color.WHITE, true, true)
	ps.mesh = q
	ps.amount = 10
	ps.lifetime = 1.1
	ps.emission_shape = CPUParticles3D.EMISSION_SHAPE_RING
	ps.emission_ring_axis = Vector3.UP
	ps.emission_ring_radius = 0.55
	ps.emission_ring_inner_radius = 0.2
	ps.emission_ring_height = 0.1
	ps.direction = Vector3.UP
	ps.spread = 12.0
	ps.initial_velocity_min = 0.6
	ps.initial_velocity_max = 1.2
	ps.gravity = Vector3.ZERO
	ps.scale_amount_min = 0.12
	ps.scale_amount_max = 0.26
	var g := Gradient.new()
	g.colors = PackedColorArray([Color(col, 0.0), Color(col, 0.9), Color(col, 0.0)])
	g.offsets = PackedFloat32Array([0.0, 0.3, 1.0])
	ps.color_ramp = g
	ps.position.y = 0.1
	root.add_child(ps)
	var ring := MeshInstance3D.new()
	var rq := QuadMesh.new()
	rq.size = Vector2(1.9, 1.9)
	ring.mesh = rq
	ring.rotation_degrees.x = -90.0
	ring.position.y = 0.1
	var rm := _mat(load(T_RING), Color(col, 0.55))
	ring.material_override = rm
	ring.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	root.add_child(ring)
	var tw := game.create_tween().set_loops()
	tw.tween_property(rm, "albedo_color:a", 0.25, 0.7)
	tw.tween_property(rm, "albedo_color:a", 0.6, 0.7)
	game.get_tree().create_timer(sec).timeout.connect(func():
		if is_instance_valid(root):
			ps.emitting = false
			root.queue_free()
		tw.kill())


## Восклицательный знак над головой: враг клюнул на вызов.
static func alert(game: Node3D, u: Node3D, sec := 1.4) -> void:
	var s := Sprite3D.new()
	s.texture = load(T_ALERT)
	s.pixel_size = 0.0075
	s.billboard = BaseMaterial3D.BILLBOARD_ENABLED
	s.shaded = false
	s.no_depth_test = true
	s.position = Vector3(0, 2.7, 0)
	s.scale = Vector3.ONE * 0.2
	u.add_child(s)
	var tw := game.create_tween()
	tw.tween_property(s, "scale", Vector3.ONE * 1.0, 0.18).set_trans(Tween.TRANS_BACK).set_ease(Tween.EASE_OUT)
	tw.tween_property(s, "position:y", 3.1, sec - 0.5)
	tw.tween_property(s, "modulate:a", 0.0, 0.3)
	tw.tween_callback(s.queue_free)


# ---------------------------------------------------------------- способности

## Боевой клич: золотая волна, столб света и россыпь искр.
static func cry(game: Node3D, p: Vector3, radius: float) -> void:
	var gold := Color(1.0, 0.82, 0.38)
	ring(game, p, 1.0, radius * 0.55, 0.9, Color(1, 0.85, 0.4, 0.95), 0.0, 0.07)
	ring(game, p, 1.0, radius * 0.4, 0.9, Color(1, 0.95, 0.7, 0.8), 0.15, 0.05)
	ring(game, p, 1.0, radius * 0.7, 1.2, Color(1, 0.75, 0.3, 0.6), 0.3, 0.04)
	column(game, p, 0.8, 5.0, 0.9, Color(1, 0.8, 0.35, 0.14))
	burst(game, p, "spark", 36, Color(1, 0.95, 0.7, 1), gold, 6.0, 1.4, 0.1, 1.8)
	light(game, p, Color(1, 0.85, 0.45), 2.0, radius * 0.4, 0.7)


## Удар о землю: пыль кольцом, трещины, обломки, ударная волна и тряска камеры.
static func slam(game: Node3D, p: Vector3, radius: float) -> void:
	decal(game, T_HIT, p, 0.8, radius * 0.5, 0.35, Color(1, 0.6, 0.25, 0.8))
	ring(game, p, 0.8, radius * 1.2, 0.6, Color(1, 0.62, 0.25, 0.95), 0.03, 0.09)
	ring(game, p, 0.5, radius * 0.9, 0.7, Color(1, 0.85, 0.5, 0.7), 0.12, 0.05)
	_cracks(game, p, radius)
	burst(game, p, "dust", 26, Color(0.62, 0.52, 0.38, 0.55), Color(0.5, 0.43, 0.33, 0.35), 4.0, 1.5, 2.2, 1.2, 0.4)
	burst(game, p, "chunk", 18, Color(1, 1, 1, 1), Color(1, 1, 1, 1), 7.0, 1.1, 1.0, 0.8, 1.6)
	burst(game, p, "spark", 18, Color(1, 0.8, 0.4, 1), Color(1, 0.45, 0.15, 1), 6.0, 0.7, 0.09, 0.6, 1.2)
	light(game, p, Color(1, 0.6, 0.25), 4.0, radius * 1.0, 0.3)
	if game.has_method("shake_cam"):
		game.shake_cam(0.5)


## Трещины в земле: тёмные полосы по лучам, медленно зарастают.
static func _cracks(game: Node3D, p: Vector3, radius: float) -> void:
	var rng := RandomNumberGenerator.new()
	rng.randomize()
	for i in 9:
		var a := TAU * i / 9.0 + rng.randf_range(-0.2, 0.2)
		var len := radius * rng.randf_range(0.3, 0.6)
		var m := MeshInstance3D.new()
		var q := QuadMesh.new()
		q.size = Vector2(0.12, len)
		m.mesh = q
		# квад лежит на земле (поворот по x), длинная сторона смотрит по лучу a
		var dir := Vector3(sin(a), 0, cos(a))
		var b := Basis(Vector3.UP, a) * Basis(Vector3.RIGHT, -PI / 2.0)
		m.transform = Transform3D(b, p + dir * (len * 0.5 + 0.4) + Vector3(0, 0.1, 0))
		var mat := _mat(null, Color(0.1, 0.07, 0.04, 0.7), false)
		m.material_override = mat
		m.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		game.add_child(m)
		var tw := game.create_tween()
		tw.tween_interval(1.8)
		tw.tween_property(mat, "albedo_color:a", 0.0, 1.4)
		tw.tween_callback(m.queue_free)


## Вызов: красные волны сходятся к герою, красный свет, ярость вихрем.
static func taunt(game: Node3D, p: Vector3, radius: float) -> void:
	for i in 3:
		ring(game, p, radius * 0.6, 1.2, 0.8, Color(1, 0.22, 0.18, 0.9), i * 0.2, 0.08)
	ring(game, p, 1.0, radius * 0.6, 1.1, Color(1, 0.35, 0.25, 0.5), 0.0, 0.04)
	column(game, p, 0.7, 4.0, 1.0, Color(1, 0.2, 0.15, 0.14))
	burst(game, p, "spark", 24, Color(1, 0.6, 0.45, 1), Color(0.9, 0.12, 0.08, 1), 4.0, 1.0, 0.1, radius * 0.4, 0.4)
	light(game, p, Color(1, 0.25, 0.2), 2.5, radius * 0.5, 0.8)


## Непоколебимые: голубой купол над отрядом и руны по земле.
static func ward(game: Node3D, p: Vector3, radius: float) -> void:
	var blue := Color(0.45, 0.75, 1.0)
	ring(game, p, 1.0, radius * 0.6, 1.1, Color(0.5, 0.8, 1.0, 0.95), 0.0, 0.07)
	ring(game, p, 1.0, 10.0, 1.4, Color(0.7, 0.9, 1.0, 0.7), 0.15, 0.04)
	var dome := MeshInstance3D.new()
	var sm := SphereMesh.new()
	sm.radius = 1.0; sm.height = 2.0
	sm.radial_segments = 32; sm.rings = 16
	dome.mesh = sm
	var sh := Shader.new()
	sh.code = """
shader_type spatial;
render_mode blend_add, unshaded, cull_disabled;
uniform vec4 col : source_color = vec4(0.4, 0.7, 1.0, 1.0);
uniform float fade = 1.0;
void fragment() {
	float f = pow(1.0 - abs(dot(normalize(NORMAL), normalize(VIEW))), 2.5);
	float hex = 0.5 + 0.5 * sin(UV.x * 90.0) * sin(UV.y * 60.0);
	ALBEDO = col.rgb * (0.3 + f * 1.2 + hex * 0.15);
	ALPHA = (0.03 + f * 0.45) * fade;
}
"""
	var dm := ShaderMaterial.new()
	dm.shader = sh
	dm.set_shader_parameter("col", blue)
	dome.material_override = dm
	dome.position = p
	dome.scale = Vector3(0.4, 0.2, 0.4)
	dome.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	game.add_child(dome)
	var tw := game.create_tween()
	tw.tween_property(dome, "scale", Vector3(9.0, 6.0, 9.0), 0.5).set_trans(Tween.TRANS_BACK).set_ease(Tween.EASE_OUT)
	tw.tween_method(func(v: float): dm.set_shader_parameter("fade", v), 1.0, 0.0, 1.1)
	tw.tween_callback(dome.queue_free)
	burst(game, p, "spark", 34, Color(0.85, 0.95, 1, 1), blue, 4.0, 1.5, 0.1, radius * 0.35, 0.8)
	light(game, p, Color(0.5, 0.75, 1.0), 2.5, radius * 0.5, 0.9)
