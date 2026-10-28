p = "godot/scripts/hud.gd"
s = open(p, encoding="utf-8").read()
a = s.index("func _portrait(kind: String)")
b = s.index("# ---------------------------------------------------------------- кадр")
new = '''## Живой портрет: настоящий боец (с оружием) в своей маленькой сцене, дышит в стойке, свет — тёплый сверху и холодный контровой.
## close = лицо крупно (для значков группы), иначе по грудь в три четверти.
func _make_portrait(kind: String, close: bool) -> SubViewport:
	var vp := SubViewport.new()
	vp.size = Vector2i(128, 128) if close else Vector2i(256, 256)
	vp.own_world_3d = true
	vp.transparent_bg = false
	vp.msaa_3d = Viewport.MSAA_4X
	vp.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	var env := WorldEnvironment.new()
	var e := Environment.new()
	e.background_mode = Environment.BG_COLOR
	e.background_color = Color(0.07, 0.12, 0.13)
	e.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	e.ambient_light_color = Color(0.55, 0.62, 0.7)
	e.ambient_light_energy = 0.55
	e.tonemap_mode = Environment.TONE_MAPPER_FILMIC
	e.glow_enabled = true
	env.environment = e
	vp.add_child(env)
	var u := Unit.new()
	u.setup(kind, 0, game)
	u.ring.visible = false
	u.bar.visible = false
	u.rotation.y = deg_to_rad(-16.0)
	vp.add_child(u)
	u.play(u.a_idle, true)
	var key := DirectionalLight3D.new()
	key.rotation_degrees = Vector3(-32, -38, 0)
	key.light_color = Color(1.0, 0.92, 0.8)
	key.light_energy = 1.7
	vp.add_child(key)
	var rim := DirectionalLight3D.new()
	rim.rotation_degrees = Vector3(-18, 150, 0)
	rim.light_color = Color(0.55, 0.75, 1.0)
	rim.light_energy = 1.3
	vp.add_child(rim)
	var cam := Camera3D.new()
	cam.fov = 30.0
	vp.add_child(cam)
	add_child(vp)
	var box := _aabb(u.model, u)
	var sc: float = u.model.scale.y
	var h := box.size.y * sc
	if h < 0.1:
		h = 1.8
	var cy := h * (0.90 if close else 0.72)
	var ext := h * (0.30 if close else 0.64)
	var dist := ext * 0.5 / tan(deg_to_rad(cam.fov * 0.5))
	cam.position = Vector3(0.0, cy, dist)
	cam.rotation_degrees = Vector3(-3, 0, 0)
	if close:
		# значок группы рисуется один раз, когда боец уже встал в стойку
		get_tree().create_timer(0.5).timeout.connect(func(): vp.render_target_update_mode = SubViewport.UPDATE_DISABLED)
	return vp


func _portrait(kind: String) -> Texture2D:
	if not portrait_vp.has(kind):
		portrait_vp[kind] = _make_portrait(kind, false)
	return portrait_vp[kind].get_texture()


func _face(kind: String) -> Texture2D:
	if not face_vp.has(kind):
		face_vp[kind] = _make_portrait(kind, true)
	return face_vp[kind].get_texture()


## Радиальное затемнение по краям: портрет «в рамке», а не прямоугольник картинки.
func _vignette() -> TextureRect:
	var g := Gradient.new()
	g.colors = PackedColorArray([Color(0, 0, 0, 0), Color(0, 0, 0, 0), Color(0, 0, 0, 0.62)])
	g.offsets = PackedFloat32Array([0.0, 0.55, 1.0])
	var gt := GradientTexture2D.new()
	gt.gradient = g
	gt.fill = GradientTexture2D.FILL_RADIAL
	gt.fill_from = Vector2(0.5, 0.5)
	gt.fill_to = Vector2(1.0, 0.5)
	gt.width = 128; gt.height = 128
	var r := TextureRect.new()
	r.texture = gt
	r.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	r.stretch_mode = TextureRect.STRETCH_SCALE
	r.mouse_filter = Control.MOUSE_FILTER_IGNORE
	return r


'''
s = s[:a] + new + s[b:]
s = s.replace("var portrait_vp := {}\n", "var portrait_vp := {}\nvar face_vp := {}\n", 1)
# виньетка поверх портрета
s = s.replace("	pf.add_child(portrait_rect)\n", "	pf.add_child(portrait_rect)\n	var vg := _vignette()\n	vg.position = portrait_rect.position; vg.size = portrait_rect.size\n	pf.add_child(vg)\n", 1)
# рендерим только показанный портрет
s = s.replace("				portrait_rect.texture = _portrait(first.kind)\n", "				portrait_rect.texture = _portrait(first.kind)\n				for k in portrait_vp:\n					portrait_vp[k].render_target_update_mode = SubViewport.UPDATE_ALWAYS if k == first.kind else SubViewport.UPDATE_DISABLED\n", 1)
# значки группы — лица
old = s[s.index("		var cell := Button.new()"):s.index("		group_box.add_child(cell)")]
newc = '''		var cell := Button.new()
		cell.focus_mode = Control.FOCUS_NONE
		cell.custom_minimum_size = Vector2(56, 62)
		var edge := BRASS_HI if u.is_hero else BRASS
		cell.add_theme_stylebox_override("normal", _style(Color(0.05, 0.045, 0.04), edge, 2, 2))
		cell.add_theme_stylebox_override("hover", _style(Color(0.2, 0.16, 0.08), BRASS_HI, 2, 2))
		cell.add_theme_stylebox_override("pressed", _style(Color(0.3, 0.22, 0.1), BRASS_HI, 2, 2))
		var face := TextureRect.new()
		face.texture = _face(u.kind)
		face.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		face.stretch_mode = TextureRect.STRETCH_SCALE
		face.position = Vector2(3, 3); face.size = Vector2(50, 48)
		face.mouse_filter = Control.MOUSE_FILTER_IGNORE
		cell.add_child(face)
		cell.pressed.connect(game.select_only.bind(u))
		var back := ColorRect.new(); back.color = Color(0, 0, 0)
		back.position = Vector2(3, 53); back.size = Vector2(50, 6); back.mouse_filter = Control.MOUSE_FILTER_IGNORE
		cell.add_child(back)
		var bar := ColorRect.new(); bar.color = Color(0.3, 0.85, 0.35)
		bar.position = Vector2(4, 54); bar.size = Vector2(48, 4); bar.mouse_filter = Control.MOUSE_FILTER_IGNORE
		cell.add_child(bar)
'''
s = s.replace(old, newc, 1)
s = s.replace("c[\"bar\"].size.x = 36.0 * clampf(u.hp / u.max_hp, 0.0, 1.0)", "c[\"bar\"].size.x = 48.0 * clampf(u.hp / u.max_hp, 0.0, 1.0)", 1)
open(p, "w", encoding="utf-8").write(s)
print("ok")
