class_name Hud
extends Control
## Нижняя панель в духе классических RTS, но со своим оформлением: миникарта, портрет и характеристики,
## предметы героя и карта команд (идти, стоять, держать, атака, способности).

const BRASS := Color(0.62, 0.47, 0.22)
const BRASS_HI := Color(0.95, 0.78, 0.38)
const DARK := Color(0.085, 0.07, 0.058)
const DARKER := Color(0.045, 0.04, 0.035)
const INK := Color(0.93, 0.89, 0.8)
const PANEL_H := 210.0

var game
var selbox: Control
var big: Label
var lbl_msg: Label
var lbl_goal: Label
var panel: Panel
var minimap: Control
var portrait_rect: TextureRect
var portrait_vp := {}
var portrait_kind := ""
var lbl_name: Label
var lbl_stats: Label
var hp_bar: ColorRect
var hp_back: ColorRect
var hp_text: Label
var xp_bar: ColorRect
var group_box: GridContainer
var group_cells: Array = []
var inv_box: Control
var cmd_buttons := {}
var toasts: Array = []
var shown_kind := ""

class SelBox extends Control:
	var a := Vector2.ZERO
	var b := Vector2.ZERO
	func _draw() -> void:
		var r := Rect2(a, b - a).abs()
		draw_rect(r, Color(0.3, 1, 0.4, 0.12), true)
		draw_rect(r, Color(0.3, 1, 0.4, 0.9), false, 2.0)

class Mini extends Control:
	var game
	func _gui_input(ev: InputEvent) -> void:
		if ev is InputEventMouseButton and ev.pressed:
			var w = _to_world(ev.position)
			if ev.button_index == MOUSE_BUTTON_LEFT:
				game.pivot.position = Vector3(w.x, 0, w.z)
			elif ev.button_index == MOUSE_BUTTON_RIGHT:
				game.order_selection_move(w)
		elif ev is InputEventMouseMotion and Input.is_mouse_button_pressed(MOUSE_BUTTON_LEFT):
			var w2 = _to_world(ev.position)
			game.pivot.position = Vector3(w2.x, 0, w2.z)
	func _to_world(p: Vector2) -> Vector3:
		var f: float = game.FIELD
		return Vector3((p.x / size.x * 2.0 - 1.0) * f, 0, (p.y / size.y * 2.0 - 1.0) * f)
	func _pt(x: float, z: float) -> Vector2:
		var f: float = game.FIELD
		return Vector2((x / f * 0.5 + 0.5) * size.x, (z / f * 0.5 + 0.5) * size.y)
	func _draw() -> void:
		draw_rect(Rect2(Vector2.ZERO, size), Color(0.16, 0.22, 0.1))
		for c in game.chests:
			if not c["opened"]:
				draw_rect(Rect2(_pt(c["pos"].x, c["pos"].z) - Vector2(3, 3), Vector2(6, 6)), Color(1, 0.85, 0.3))
		for c in game.camps:
			if not c["cleared"]:
				var col := Color(1, 0.25, 0.2) if c["awake"] else Color(0.75, 0.2, 0.18)
				draw_circle(_pt(c["pos"].x, c["pos"].z), 6.0 if c["boss"] else 4.5, col)
		for u in game.units:
			if u.state == Unit.S.DEAD:
				continue
			var p := _pt(u.global_position.x, u.global_position.z)
			if u.team == 0:
				draw_rect(Rect2(p - Vector2(1.5, 1.5), Vector2(3, 3)), Color(0.45, 0.75, 1.0) if not u.is_hero else Color(1, 1, 1))
			elif not u.asleep:
				draw_rect(Rect2(p - Vector2(1.5, 1.5), Vector2(3, 3)), Color(1, 0.4, 0.3))
		# куда смотрит камера
		var cp: Vector2 = _pt(game.pivot.position.x, game.pivot.position.z)
		var half: float = game.cam_zoom * 0.9 / game.FIELD * 0.5 * size.x
		draw_rect(Rect2(cp - Vector2(half * 1.4, half), Vector2(half * 2.8, half * 2.0)), Color(1, 1, 1, 0.8), false, 1.5)


func _style(bg: Color, border: Color, bw := 2, radius := 3) -> StyleBoxFlat:
	var s := StyleBoxFlat.new()
	s.bg_color = bg
	s.border_color = border
	s.set_border_width_all(bw)
	s.set_corner_radius_all(radius)
	return s


func _label(parent: Node, text: String, size := 16, col := INK) -> Label:
	var l := Label.new()
	l.text = text
	l.add_theme_font_size_override("font_size", size)
	l.add_theme_color_override("font_color", col)
	l.add_theme_color_override("font_outline_color", Color(0, 0, 0))
	l.add_theme_constant_override("outline_size", 4)
	l.mouse_filter = Control.MOUSE_FILTER_IGNORE
	parent.add_child(l)
	return l


func _ready() -> void:
	set_anchors_preset(Control.PRESET_FULL_RECT)
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	selbox = SelBox.new()
	selbox.set_anchors_preset(Control.PRESET_FULL_RECT)
	selbox.mouse_filter = Control.MOUSE_FILTER_IGNORE
	selbox.visible = false
	add_child(selbox)
	_build_top()
	_build_panel()


func over_ui(p: Vector2) -> bool:
	return p.y > get_viewport_rect().size.y - PANEL_H


# ---------------------------------------------------------------- верх

func _build_top() -> void:
	var box := Panel.new()
	box.add_theme_stylebox_override("panel", _style(Color(DARK, 0.88), BRASS))
	box.position = Vector2(12, 12)
	box.size = Vector2(330, 62)
	box.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(box)
	lbl_goal = _label(box, "", 16)
	lbl_goal.position = Vector2(12, 6)
	lbl_msg = _label(self, "", 20)
	lbl_msg.set_anchors_preset(Control.PRESET_CENTER_TOP)
	lbl_msg.position = Vector2(-500, 90)
	lbl_msg.size = Vector2(1000, 60)
	lbl_msg.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	big = _label(self, "", 44)
	big.set_anchors_preset(Control.PRESET_CENTER)
	big.position = Vector2(-500, -140)
	big.size = Vector2(1000, 200)
	big.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	big.visible = false


# ---------------------------------------------------------------- нижняя панель

func _build_panel() -> void:
	panel = Panel.new()
	panel.set_anchors_preset(Control.PRESET_BOTTOM_WIDE)
	panel.offset_top = -PANEL_H
	panel.offset_bottom = 0
	var ps := _style(DARK, BRASS, 3, 0)
	ps.border_width_bottom = 0
	panel.add_theme_stylebox_override("panel", ps)
	panel.mouse_filter = Control.MOUSE_FILTER_STOP
	add_child(panel)
	# миникарта
	var mf := Panel.new()
	mf.add_theme_stylebox_override("panel", _style(DARKER, BRASS, 3, 2))
	mf.position = Vector2(12, 14)
	mf.size = Vector2(186, 186)
	panel.add_child(mf)
	minimap = Mini.new()
	minimap.game = game
	minimap.position = Vector2(6, 6)
	minimap.size = Vector2(174, 174)
	mf.add_child(minimap)
	# портрет
	var pf := Panel.new()
	pf.add_theme_stylebox_override("panel", _style(DARKER, BRASS, 3, 2))
	pf.position = Vector2(214, 14)
	pf.size = Vector2(150, 186)
	panel.add_child(pf)
	portrait_rect = TextureRect.new()
	portrait_rect.position = Vector2(6, 6)
	portrait_rect.size = Vector2(138, 138)
	portrait_rect.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	portrait_rect.stretch_mode = TextureRect.STRETCH_SCALE
	pf.add_child(portrait_rect)
	lbl_name = _label(pf, "", 15, BRASS_HI)
	lbl_name.position = Vector2(6, 146)
	lbl_name.size = Vector2(138, 20)
	lbl_name.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	hp_back = ColorRect.new(); hp_back.color = Color(0.02, 0.02, 0.02)
	hp_back.position = Vector2(8, 168); hp_back.size = Vector2(134, 14)
	pf.add_child(hp_back)
	hp_bar = ColorRect.new(); hp_bar.color = Color(0.25, 0.8, 0.3)
	hp_bar.position = Vector2(9, 169); hp_bar.size = Vector2(132, 12)
	pf.add_child(hp_bar)
	hp_text = _label(pf, "", 11)
	hp_text.position = Vector2(8, 167); hp_text.size = Vector2(134, 16)
	hp_text.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	# характеристики и группа
	var cf := Panel.new()
	cf.add_theme_stylebox_override("panel", _style(DARKER, BRASS, 3, 2))
	cf.position = Vector2(378, 14)
	cf.size = Vector2(430, 186)
	panel.add_child(cf)
	lbl_stats = _label(cf, "", 15)
	lbl_stats.position = Vector2(12, 8)
	lbl_stats.size = Vector2(250, 170)
	group_box = GridContainer.new()
	group_box.columns = 6
	group_box.position = Vector2(12, 10)
	group_box.add_theme_constant_override("h_separation", 6)
	group_box.add_theme_constant_override("v_separation", 6)
	cf.add_child(group_box)
	inv_box = Control.new()
	inv_box.position = Vector2(270, 12)
	cf.add_child(inv_box)
	var il := _label(inv_box, "Предметы", 12, Color(0.7, 0.65, 0.55))
	il.position = Vector2(0, -2)
	for i in 6:
		var slot := Panel.new()
		slot.add_theme_stylebox_override("panel", _style(Color(0.03, 0.028, 0.025), Color(0.38, 0.3, 0.16), 2, 2))
		slot.position = Vector2((i % 2) * 74, 20 + (i / 2) * 50)
		slot.size = Vector2(68, 46)
		inv_box.add_child(slot)
	# карта команд
	var kf := Panel.new()
	kf.add_theme_stylebox_override("panel", _style(DARKER, BRASS, 3, 2))
	kf.set_anchors_preset(Control.PRESET_TOP_RIGHT)
	kf.offset_left = -332
	kf.offset_right = -12
	kf.offset_top = 14
	kf.offset_bottom = 200
	panel.add_child(kf)
	var cmds := [
		["move", "Идти", "M"], ["stop", "Стоять", "S"], ["hold", "Держать", "H"], ["attack", "Атака", "A"],
		["a0", "Боевой\nклич", "Q"], ["a1", "Удар\nо землю", "W"], ["a2", "Вызов", "E"], ["a3", "Непоко-\nлебимые", "R"],
	]
	for i in cmds.size():
		var b := Button.new()
		b.focus_mode = Control.FOCUS_NONE
		b.size = Vector2(72, 54)
		b.position = Vector2(8 + (i % 4) * 78, 8 + (i / 4) * 62)
		b.add_theme_font_size_override("font_size", 12)
		b.add_theme_stylebox_override("normal", _style(Color(0.2, 0.15, 0.09), BRASS, 2, 3))
		b.add_theme_stylebox_override("hover", _style(Color(0.3, 0.22, 0.12), BRASS_HI, 2, 3))
		b.add_theme_stylebox_override("pressed", _style(Color(0.4, 0.3, 0.14), BRASS_HI, 2, 3))
		b.add_theme_stylebox_override("disabled", _style(Color(0.1, 0.09, 0.08), Color(0.3, 0.25, 0.15), 2, 3))
		b.add_theme_color_override("font_color", INK)
		b.add_theme_color_override("font_disabled_color", Color(0.5, 0.46, 0.4))
		b.pressed.connect(_on_cmd.bind(cmds[i][0]))
		b.tooltip_text = "%s (%s)" % [cmds[i][1].replace("\n", " "), cmds[i][2]]
		b.text = "%s\n[%s]" % [cmds[i][1], cmds[i][2]]
		kf.add_child(b)
		cmd_buttons[cmds[i][0]] = b
	var hint := _label(kf, "Бойцы: ПКМ — идти/бить", 11, Color(0.6, 0.55, 0.45))
	hint.position = Vector2(8, 138)
	var hint2 := _label(kf, "Рамка — выбрать, Ctrl+A — все", 11, Color(0.6, 0.55, 0.45))
	hint2.position = Vector2(8, 156)


func _on_cmd(id: String) -> void:
	match id:
		"move": game.cmd_mode = "move"
		"attack": game.cmd_mode = "attack"
		"stop": game._order_stop()
		"hold": game.order_hold()
		"a0": game.cast(0)
		"a1": game.cast(1)
		"a2": game.cast(2)
		"a3": game.cast(3)


# ---------------------------------------------------------------- портрет

func _portrait(kind: String) -> Texture2D:
	if not portrait_vp.has(kind):
		var vp := SubViewport.new()
		vp.size = Vector2i(256, 256)
		vp.own_world_3d = true
		vp.transparent_bg = false
		vp.render_target_update_mode = SubViewport.UPDATE_ALWAYS
		var env := WorldEnvironment.new()
		var e := Environment.new()
		e.background_mode = Environment.BG_COLOR
		e.background_color = Color(0.16, 0.14, 0.12)
		e.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
		e.ambient_light_color = Color(0.7, 0.7, 0.75)
		e.ambient_light_energy = 0.8
		env.environment = e
		vp.add_child(env)
		var m := Unit.make_model(kind)
		vp.add_child(m)
		var light := DirectionalLight3D.new()
		light.rotation_degrees = Vector3(-30, 30, 0)
		vp.add_child(light)
		var cam := Camera3D.new()
		cam.position = Vector3(0, 1.55, 1.7)
		cam.rotation_degrees = Vector3(-6, 0, 0)
		cam.fov = 38.0
		vp.add_child(cam)
		add_child(vp)
		portrait_vp[kind] = vp
	return portrait_vp[kind].get_texture()


# ---------------------------------------------------------------- кадр

func toast(s: String, sec: float) -> void:
	toasts.append([s, sec])


func refresh(dt: float) -> void:
	_layout()
	var left := 0
	for c in game.camps:
		if not c["cleared"]: left += 1
	lbl_goal.text = "Задача: победить хана Кобека\nЛагерей осталось: %d из %d" % [left, game.camps.size()]
	minimap.queue_redraw()
	var sel: Array = game.selection
	var first: Unit = sel[0] if sel.size() > 0 else null
	var single := sel.size() == 1
	# портрет и имя
	if first != null:
		if shown_kind != first.kind:
			shown_kind = first.kind
			portrait_rect.texture = _portrait(first.kind)
		lbl_name.text = first.title if single else "Выбрано: %d" % sel.size()
		var ratio := clampf(first.hp / first.max_hp, 0.0, 1.0)
		hp_bar.size.x = 132.0 * ratio
		hp_bar.color = Color(0.25, 0.8, 0.3) if ratio > 0.5 else (Color(0.9, 0.7, 0.2) if ratio > 0.25 else Color(0.85, 0.25, 0.2))
		hp_text.text = "%d / %d" % [first.hp, first.max_hp]
	else:
		portrait_rect.texture = null
		shown_kind = ""
		lbl_name.text = "Никто не выбран"
		hp_bar.size.x = 0
		hp_text.text = ""
	# характеристики или состав группы
	group_box.visible = sel.size() > 1
	lbl_stats.visible = sel.size() <= 1
	inv_box.visible = first != null and first.is_hero and sel.size() == 1
	if sel.size() <= 1 and first != null:
		var s := "%s\n" % first.title
		if first.is_hero:
			var nxt: float = game.XP_AT[first.level] if first.level < game.XP_AT.size() else first.xp
			s += "Уровень %d   опыт %d / %d\n" % [first.level, first.xp, nxt]
		s += "Урон: %d%s\n" % [first.dmg * (1.0 + 0.1 * (first.level - 1)), "   (дальний бой)" if first.ranged else ""]
		s += "Скорость: %.1f\n" % first.speed
		s += "Здоровье: %d / %d" % [first.hp, first.max_hp]
		if first.buff_t > 0.0: s += "\nКлич: +25% урона"
		if first.guard_t > 0.0: s += "\nСтойкость: −30% урона"
		lbl_stats.text = s
	elif sel.size() > 1:
		_rebuild_group(sel)
	# команды
	var has_sel := sel.size() > 0
	for k in ["move", "stop", "hold", "attack"]:
		cmd_buttons[k].disabled = not has_sel
	for i in 4:
		var b: Button = cmd_buttons["a%d" % i]
		var open: bool = game.hero.level >= game.ABILITY_LEVEL[i]
		var cdl: float = maxf(0.0, game.ab_ready[i] - game.t_game)
		var nm: String = ["Боевой\nклич", "Удар\nо землю", "Вызов", "Непоко-\nлебимые"][i]
		if not open:
			b.text = "%s\nур. %d" % [nm.replace("\n", " "), game.ABILITY_LEVEL[i]]
			b.disabled = true
		elif cdl > 0.0:
			b.text = "%s\n%d с" % [nm.replace("\n", " "), ceili(cdl)]
			b.disabled = true
		else:
			b.text = "%s\n[%s]" % [nm, game.ABILITY_KEYS[i]]
			b.disabled = false
	# сообщения
	if toasts.size() > 0:
		toasts[0][1] -= dt
		lbl_msg.text = toasts[0][0]
		if toasts[0][1] <= 0.0:
			toasts.pop_front()
	else:
		lbl_msg.text = ""
	if game.cmd_mode != "":
		lbl_msg.text = "Выберите точку: %s (Esc — отмена)" % ("идти" if game.cmd_mode == "move" else "атака с движением")


func _rebuild_group(sel: Array) -> void:
	var key := ""
	for u in sel:
		key += str(u.get_instance_id()) + ","
	if group_box.has_meta("key") and group_box.get_meta("key") == key:
		# обновляем только полоски здоровья
		for i in group_cells.size():
			var c: Dictionary = group_cells[i]
			var u: Unit = c["unit"]
			if is_instance_valid(u):
				c["bar"].size.x = 36.0 * clampf(u.hp / u.max_hp, 0.0, 1.0)
		return
	group_box.set_meta("key", key)
	for ch in group_box.get_children():
		ch.queue_free()
	group_cells.clear()
	for u in sel:
		var cell := Button.new()
		cell.focus_mode = Control.FOCUS_NONE
		cell.custom_minimum_size = Vector2(44, 44)
		cell.add_theme_stylebox_override("normal", _style(Color(0.12, 0.1, 0.08), BRASS, 2, 2))
		cell.add_theme_stylebox_override("hover", _style(Color(0.25, 0.2, 0.1), BRASS_HI, 2, 2))
		cell.add_theme_font_size_override("font_size", 13)
		cell.text = {"hero": "Г", "footman": "М", "archer": "С"}.get(u.kind, "?")
		cell.pressed.connect(game.select_only.bind(u))
		var back := ColorRect.new(); back.color = Color(0, 0, 0)
		back.position = Vector2(3, 36); back.size = Vector2(38, 5); back.mouse_filter = Control.MOUSE_FILTER_IGNORE
		cell.add_child(back)
		var bar := ColorRect.new(); bar.color = Color(0.3, 0.85, 0.35)
		bar.position = Vector2(4, 37); bar.size = Vector2(36, 3); bar.mouse_filter = Control.MOUSE_FILTER_IGNORE
		cell.add_child(bar)
		group_box.add_child(cell)
		group_cells.append({"unit": u, "bar": bar})


func _layout() -> void:
	var vp := get_viewport_rect().size
	panel.position = Vector2(0, vp.y - PANEL_H)
	panel.size = Vector2(vp.x, PANEL_H)
	var kf: Control = cmd_buttons["move"].get_parent()
	kf.position = Vector2(vp.x - 332, 14)
	kf.size = Vector2(320, 186)
	lbl_msg.position = Vector2(vp.x / 2 - 500, 90)
	lbl_msg.size = Vector2(1000, 60)
	big.position = Vector2(vp.x / 2 - 500, vp.y / 2 - 180)
	big.size = Vector2(1000, 200)
