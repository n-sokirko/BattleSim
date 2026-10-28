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
var face_vp := {}
var portrait_kind := ""
var lbl_name: Label
var lbl_stats: Label
var hp_gbar: GBar
var xp_gbar: GBar
var stat_rows := {}
var lbl_level: Label
var lbl_buff: Label
var hero_only: Array = []
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

## Иконка: рисует текстуру ровно в свой квадрат (цвет задаётся через modulate).
class Ico extends Control:
	var tex: Texture2D
	func _draw() -> void:
		if tex != null:
			draw_texture_rect(tex, Rect2(Vector2.ZERO, size), false)

## Кнопка команды с подробной подсказкой: название, клавиша, перезарядка, описание.
class CmdButton extends Button:
	var tip := {}
	func _make_custom_tooltip(_for_text: String) -> Object:
		var pc := PanelContainer.new()
		var sb := StyleBoxFlat.new()
		sb.bg_color = Color(0.07, 0.055, 0.04, 0.96)
		sb.border_color = Color(0.85, 0.65, 0.3)
		sb.set_border_width_all(2)
		sb.set_corner_radius_all(4)
		sb.set_content_margin_all(10)
		pc.add_theme_stylebox_override("panel", sb)
		var vb := VBoxContainer.new()
		vb.add_theme_constant_override("separation", 4)
		pc.add_child(vb)
		var head := Label.new()
		head.text = "%s  [%s]" % [tip.get("name", ""), tip.get("key", "")]
		head.add_theme_font_size_override("font_size", 17)
		head.add_theme_color_override("font_color", Color(0.98, 0.82, 0.42))
		vb.add_child(head)
		if tip.has("meta"):
			var meta := Label.new()
			meta.text = tip["meta"]
			meta.add_theme_font_size_override("font_size", 13)
			meta.add_theme_color_override("font_color", Color(0.6, 0.8, 0.95))
			vb.add_child(meta)
		var body := Label.new()
		body.text = tip.get("text", "")
		body.custom_minimum_size = Vector2(270, 0)
		body.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		body.add_theme_font_size_override("font_size", 14)
		body.add_theme_color_override("font_color", Color(0.93, 0.89, 0.8))
		vb.add_child(body)
		return pc

## Полоска из нарисованного набора: рамка, цветная заливка, «хвост» недавней потери и число по центру.
class GBar extends Control:
	var frame: Texture2D
	var fill: Texture2D
	var ratio := 1.0
	var trail := 1.0
	var text := ""
	var tint := Color.WHITE
	var font_size := 13
	func _process(dt: float) -> void:
		if trail > ratio:
			trail = maxf(ratio, trail - dt * 0.35)
		else:
			trail = ratio
		queue_redraw()
	func _draw() -> void:
		var pad := Vector2(size.y * 0.2, size.y * 0.24)
		var inner := Rect2(pad, size - pad * 2.0)
		if frame != null:
			draw_texture_rect(frame, Rect2(Vector2.ZERO, size), false)
		draw_rect(inner, Color(0.03, 0.02, 0.02, 0.95))
		if trail > ratio + 0.001:
			draw_rect(Rect2(inner.position, Vector2(inner.size.x * trail, inner.size.y)), Color(0.95, 0.85, 0.55, 0.75))
		if ratio > 0.001 and fill != null:
			var fw := inner.size.x * ratio
			var src := Rect2(0, 0, fill.get_width() * ratio, fill.get_height())
			draw_texture_rect_region(fill, Rect2(inner.position, Vector2(fw, inner.size.y)), src, tint)
			draw_rect(Rect2(inner.position, Vector2(fw, inner.size.y * 0.35)), Color(1, 1, 1, 0.12))
		if text != "":
			var f := ThemeDB.fallback_font
			var ts := f.get_string_size(text, HORIZONTAL_ALIGNMENT_CENTER, -1, font_size)
			var pos := Vector2((size.x - ts.x) * 0.5, (size.y + ts.y * 0.62) * 0.5)
			draw_string_outline(f, pos, text, HORIZONTAL_ALIGNMENT_LEFT, -1, font_size, 4, Color(0, 0, 0, 0.9))
			draw_string(f, pos, text, HORIZONTAL_ALIGNMENT_LEFT, -1, font_size, Color(1, 0.97, 0.88))

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
			if not c["opened"] and game.fog.explored(c["pos"]):
				draw_rect(Rect2(_pt(c["pos"].x, c["pos"].z) - Vector2(3, 3), Vector2(6, 6)), Color(1, 0.85, 0.3))
		for c in game.camps:
			if not c["cleared"] and game.fog.explored(c["pos"]):
				var col := Color(1, 0.25, 0.2) if c["awake"] else Color(0.75, 0.2, 0.18)
				draw_circle(_pt(c["pos"].x, c["pos"].z), 6.0 if c["boss"] else 4.5, col)
		for u in game.units:
			if u.state == Unit.S.DEAD:
				continue
			var p := _pt(u.global_position.x, u.global_position.z)
			if u.team == 0:
				draw_rect(Rect2(p - Vector2(1.5, 1.5), Vector2(3, 3)), Color(0.45, 0.75, 1.0) if not u.is_hero else Color(1, 1, 1))
			elif not u.asleep and u.visible:
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


static var _icons := {}
static var _gen := {}


## Нарисованный значок или рамка из res://assets/ui/gen (с мипмапами, чтобы при уменьшении не рябило).
static func _g(name: String) -> Texture2D:
	if not _gen.has(name):
		var img: Image = (load("res://assets/ui/gen/%s.png" % name) as Texture2D).get_image()
		img.generate_mipmaps()
		_gen[name] = ImageTexture.create_from_image(img)
	return _gen[name]


func _box(name: String, m: int) -> StyleBoxTexture:
	var s := StyleBoxTexture.new()
	s.texture = _g(name)
	s.texture_margin_left = m; s.texture_margin_right = m
	s.texture_margin_top = m; s.texture_margin_bottom = m
	return s


func _gicon(parent: Node, name: String, px: float) -> Control:
	var t := Ico.new()
	t.tex = _g(name)
	t.custom_minimum_size = Vector2(px, px)
	t.size = Vector2(px, px)
	t.mouse_filter = Control.MOUSE_FILTER_IGNORE
	parent.add_child(t)
	return t


## Иконка из набора res://assets/ui/icons (белый силуэт; цвет задаётся через modulate).
func _icon(parent: Node, name: String, px: float, col := INK) -> Control:
	if not _icons.has(name):
		_icons[name] = load("res://assets/ui/icons/%s.svg" % name)
	var t := Ico.new()
	t.tex = _icons[name]
	t.custom_minimum_size = Vector2(px, px)
	t.size = Vector2(px, px)
	t.modulate = col
	t.mouse_filter = Control.MOUSE_FILTER_IGNORE
	parent.add_child(t)
	return t


func _ready() -> void:
	set_anchors_preset(Control.PRESET_FULL_RECT)
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	texture_filter = CanvasItem.TEXTURE_FILTER_LINEAR_WITH_MIPMAPS
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
	box.add_theme_stylebox_override("panel", _box("panel_small", 22))
	box.position = Vector2(12, 12)
	box.size = Vector2(300, 100)
	box.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(box)
	# три строки: главная цель, лагеря, сундуки
	var rows := [["goal", Color(0.95, 0.45, 0.35)], ["camp", BRASS_HI], ["chest", Color(1.0, 0.85, 0.35)]]
	for i in rows.size():
		var ic := _gicon(box, ["boss", "camp", "chest"][i], 26)
		ic.position = Vector2(14, 8 + i * 27)
	lbl_goal = _label(box, "", 16)
	lbl_goal.position = Vector2(48, 8)
	lbl_goal.add_theme_constant_override("line_spacing", 6)
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
	panel.add_theme_stylebox_override("panel", _box("panel_small", 24))
	panel.mouse_filter = Control.MOUSE_FILTER_STOP
	add_child(panel)
	# миникарта
	var mf := Panel.new()
	mf.add_theme_stylebox_override("panel", _box("frame_minimap", 22))
	mf.position = Vector2(12, 14)
	mf.size = Vector2(186, 186)
	panel.add_child(mf)
	minimap = Mini.new()
	minimap.game = game
	minimap.position = Vector2(15, 15)
	minimap.size = Vector2(156, 156)
	mf.add_child(minimap)
	# портрет
	var pf := Panel.new()
	pf.add_theme_stylebox_override("panel", _box("panel_small", 22))
	pf.position = Vector2(214, 14)
	pf.size = Vector2(150, 186)
	panel.add_child(pf)
	portrait_rect = TextureRect.new()
	portrait_rect.position = Vector2(14, 13)
	portrait_rect.size = Vector2(122, 122)
	portrait_rect.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	portrait_rect.stretch_mode = TextureRect.STRETCH_SCALE
	pf.add_child(portrait_rect)
	var vg := _vignette()
	vg.position = portrait_rect.position; vg.size = portrait_rect.size
	pf.add_child(vg)
	lbl_name = _label(pf, "", 15, BRASS_HI)
	lbl_name.position = Vector2(6, 137)
	lbl_name.size = Vector2(138, 20)
	lbl_name.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	hp_gbar = GBar.new()
	hp_gbar.frame = _g("bar_frame")
	hp_gbar.fill = _g("bar_green")
	hp_gbar.position = Vector2(10, 158)
	hp_gbar.size = Vector2(130, 24)
	hp_gbar.font_size = 13
	hp_gbar.mouse_filter = Control.MOUSE_FILTER_IGNORE
	pf.add_child(hp_gbar)
	# характеристики и группа
	var cf := Panel.new()
	cf.add_theme_stylebox_override("panel", _box("panel_small", 22))
	cf.position = Vector2(378, 14)
	cf.size = Vector2(430, 186)
	panel.add_child(cf)
	lbl_stats = _label(cf, "", 18, BRASS_HI)
	lbl_stats.position = Vector2(24, 14)
	lbl_stats.size = Vector2(250, 26)
	# характеристики: значок и число
	for i in 3:
		var key: String = ["dmg", "speed", "hp"][i]
		var ic := _gicon(cf, {"dmg": "damage", "speed": "speed", "hp": "health"}[key], 26)
		ic.position = Vector2(20, 42 + i * 28)
		var lb := _label(cf, "", 17)
		lb.position = Vector2(54, 41 + i * 28)
		stat_rows[key] = [ic, lb]
	var lv_ic := _gicon(cf, "level", 26)
	lv_ic.position = Vector2(20, 118)
	lbl_level = _label(cf, "", 17)
	lbl_level.position = Vector2(54, 117)
	xp_gbar = GBar.new()
	xp_gbar.frame = _g("bar_frame")
	xp_gbar.fill = _g("bar_blue")
	xp_gbar.position = Vector2(54, 142)
	xp_gbar.size = Vector2(200, 18)
	xp_gbar.font_size = 11
	xp_gbar.mouse_filter = Control.MOUSE_FILTER_IGNORE
	cf.add_child(xp_gbar)
	var xp_ic := _icon(cf, "xp", 18, Color(0.5, 0.8, 1.0))
	xp_ic.position = Vector2(30, 141)
	lbl_buff = _label(cf, "", 13, Color(0.6, 0.85, 0.7))
	lbl_buff.position = Vector2(24, 154)
	lbl_buff.size = Vector2(250, 18)
	hero_only = [lv_ic, lbl_level, xp_gbar, xp_ic]
	group_box = GridContainer.new()
	group_box.columns = 6
	group_box.position = Vector2(18, 16)
	group_box.add_theme_constant_override("h_separation", 6)
	group_box.add_theme_constant_override("v_separation", 6)
	cf.add_child(group_box)
	inv_box = Control.new()
	inv_box.position = Vector2(268, 16)
	cf.add_child(inv_box)
	var il := _label(inv_box, "Предметы", 12, Color(0.7, 0.65, 0.55))
	il.position = Vector2(0, -2)
	for i in 6:
		var slot := Panel.new()
		slot.add_theme_stylebox_override("panel", _box("slot_big", 16))
		slot.position = Vector2((i % 2) * 74, 20 + (i / 2) * 50)
		slot.size = Vector2(68, 46)
		inv_box.add_child(slot)
		var si := _icon(slot, "slot", 30, Color(0.5, 0.42, 0.25, 0.28))
		si.position = Vector2(19, 8)
	# карта команд
	var kf := Panel.new()
	kf.add_theme_stylebox_override("panel", _box("panel_small", 22))
	kf.set_anchors_preset(Control.PRESET_TOP_RIGHT)
	kf.offset_left = -332
	kf.offset_right = -12
	kf.offset_top = 14
	kf.offset_bottom = 200
	panel.add_child(kf)
	var cmds := [
		["move", "Идти", "M"], ["stop", "Стоять", "S"], ["hold", "Держать", "H"], ["attack", "Атака", "A"],
		["a0", "Клич", "Q"], ["a1", "Удар", "W"], ["a2", "Вызов", "E"], ["a3", "Стена", "R"],
	]
	for i in cmds.size():
		var b := CmdButton.new()
		b.focus_mode = Control.FOCUS_NONE
		b.size = Vector2(68, 66)
		b.position = Vector2(16 + (i % 4) * 73, 12 + (i / 4) * 70)
		b.add_theme_font_size_override("font_size", 12)
		b.add_theme_stylebox_override("normal", StyleBoxEmpty.new())
		b.add_theme_stylebox_override("hover", _style(Color(1, 0.9, 0.5, 0.10), BRASS_HI, 2, 4))
		b.add_theme_stylebox_override("pressed", _style(Color(1, 0.9, 0.5, 0.25), BRASS_HI, 2, 4))
		b.add_theme_stylebox_override("disabled", StyleBoxEmpty.new())
		b.add_theme_color_override("font_color", INK)
		b.add_theme_color_override("font_disabled_color", Color(0.5, 0.46, 0.4))
		b.pressed.connect(_on_cmd.bind(cmds[i][0]))
		b.tip = _tip_for(cmds[i][0], cmds[i][2])
		b.tooltip_text = b.tip["name"]
		kf.add_child(b)
		cmd_buttons[cmds[i][0]] = b
		# значок, горячая клавиша, подпись, шторка перезарядки
		var ic := _gicon(b, {"move": "move", "stop": "stop", "hold": "hold", "attack": "attack", "a0": "ab_cry", "a1": "ab_slam", "a2": "ab_taunt", "a3": "ab_ward"}[cmds[i][0]], 50)
		ic.position = Vector2(9, 1)
		var hk := _label(b, cmds[i][2], 12, BRASS_HI)
		hk.position = Vector2(12, 2)
		hk.add_theme_constant_override("outline_size", 3)
		var nm := _label(b, cmds[i][1], 11, Color(0.8, 0.75, 0.65))
		nm.position = Vector2(0, 51)
		nm.size = Vector2(68, 15)
		nm.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		nm.add_theme_constant_override("outline_size", 3)
		var cd := ColorRect.new()
		cd.color = Color(0, 0, 0, 0.66)
		cd.position = Vector2(9, 1); cd.size = Vector2(50, 50)
		cd.mouse_filter = Control.MOUSE_FILTER_IGNORE
		cd.visible = false
		b.add_child(cd)
		var cl := _label(b, "", 20, Color(1, 0.92, 0.7))
		cl.position = Vector2(0, 12); cl.size = Vector2(68, 28)
		cl.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		b.set_meta("ic", ic); b.set_meta("cd", cd); b.set_meta("cdl", cl)
	var hint := _label(kf, "ПКМ — идти/бить · Ctrl+A — все", 11, Color(0.6, 0.55, 0.45))
	hint.position = Vector2(16, 154)


func _tip_for(id: String, key: String) -> Dictionary:
	var d := {
		"move": ["Идти", "Отряд идёт в точку и не ввязывается в бой, пока не отдан другой приказ."],
		"stop": ["Стоять", "Прекратить текущий приказ. Бойцы сами отвечают тем, кто подошёл вплотную."],
		"hold": ["Держать позицию", "Не покидать место: бить только тех, кто в досягаемости. Хорошо для лучников за спиной щитов."],
		"attack": ["Атака с движением", "Идти в точку и атаковать каждого врага по пути."],
	}
	if d.has(id):
		return {"name": d[id][0], "key": key, "text": d[id][1]}
	var i := int(id.substr(1))
	var info: Array = [
		["радиус 20 м, длится 10 с", "Герой трубит в рог: все союзники рядом бьют на 25% сильнее."],
		["радиус 7 м, урон 30", "Удар о землю отбрасывает врагов, наносит урон и сбивает их на 1,6 с: они не могут атаковать."],
		["радиус 15 м, длится 5 с", "Враги вокруг (кроме вожаков) бросаются на героя, а он получает на 30% меньше урона. Отряд бьёт их с боков."],
		["радиус 22 м, длится 10 с", "Союзники рядом становятся стеной: получают на 30% меньше урона."],
	][i]
	var names: Array = game.ABILITY_NAMES
	return {"name": names[i], "key": key, "meta": "Уровень %d · перезарядка %d с · %s" % [game.ABILITY_LEVEL[i], game.ABILITY_CD[i], info[0]], "text": info[1]}


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

## Живой портрет: настоящий боец (с оружием) в своей маленькой сцене, дышит в стойке, свет — тёплый сверху и холодный контровой.
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
	if close and u.off_weapon.begins_with("shield") and u.gear_nodes.size() > 1:
		(u.gear_nodes[1] as Node3D).visible = false      # лицо без щита перед ним
	u.rotation.y = deg_to_rad(-16.0)
	vp.add_child(u)
	# стойка туда-обратно: у клипа края не совпадают, и цикл дёргался на стыке
	if u.anim != null and u.anim.has_animation(u.a_idle):
		u.anim.get_animation(u.a_idle).loop_mode = Animation.LOOP_PINGPONG
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
	var cy := h * 0.72
	var ext := h * 0.64
	var dist := ext * 0.5 / tan(deg_to_rad(cam.fov * 0.5))
	cam.position = Vector3(0.0, cy, dist)
	cam.rotation_degrees = Vector3(-3, 0, 0)
	# кадр по кости головы в живой позе (оружие и гребни искажают габариты), поэтому чуть позже
	for t in [0.15, 0.3, 0.42]:
		get_tree().create_timer(t).timeout.connect(_frame_face.bind(u, cam, close))
	if close:
		# значок группы рисуется один раз, когда боец уже встал в стойку
		get_tree().create_timer(0.5).timeout.connect(func(): vp.render_target_update_mode = SubViewport.UPDATE_DISABLED)
	return vp


func _frame_face(u: Unit, cam: Camera3D, close: bool) -> void:
	if not is_instance_valid(u) or not is_instance_valid(cam):
		return
	var sk := u.model.find_child("Skeleton3D", true, false) as Skeleton3D
	if sk == null:
		return
	for i in sk.get_bone_count():
		var bn := sk.get_bone_name(i).to_lower()
		if bn.ends_with("head") and not bn.contains("top"):
			var hp: Vector3 = sk.global_transform * sk.get_bone_global_pose(i).origin
			var hh := (hp.y - u.global_position.y) / 0.93
			var d2 := hh * (0.36 if close else 0.56) * 0.5 / tan(deg_to_rad(cam.fov * 0.5))
			cam.position = Vector3(hp.x, hp.y + hh * (0.03 if close else 0.03), hp.z + d2)
			return


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


# ---------------------------------------------------------------- кадр

func toast(s: String, sec: float) -> void:
	toasts.append([s, sec])


func refresh(dt: float) -> void:
	_layout()
	var left := 0
	for c in game.camps:
		if not c["cleared"]: left += 1
	var chests_open := 0
	for c in game.chests:
		if c["opened"]: chests_open += 1
	lbl_goal.text = "Победить хана Кобека\nЛагерей: %d из %d" % [game.camps.size() - left, game.camps.size()] + "\nСундуков: %d из %d" % [chests_open, game.chests.size()]
	minimap.queue_redraw()
	var sel: Array = game.selection
	var first: Unit = sel[0] if sel.size() > 0 else null
	var single := sel.size() == 1
	# портрет и имя
	if first != null:
		if shown_kind != first.kind:
			shown_kind = first.kind
			portrait_rect.texture = _portrait(first.kind)
		if hp_gbar.has_meta("uid") and hp_gbar.get_meta("uid") != first.get_instance_id():
			hp_gbar.trail = clampf(first.hp / first.max_hp, 0.0, 1.0)
		hp_gbar.set_meta("uid", first.get_instance_id())
		lbl_name.text = first.title if single else "Выбрано: %d" % sel.size()
		var ratio := clampf(first.hp / first.max_hp, 0.0, 1.0)
		hp_gbar.ratio = ratio
		hp_gbar.tint = Color(1, 1, 1) if ratio > 0.5 else (Color(1.5, 1.2, 0.35) if ratio > 0.25 else Color(1.7, 0.4, 0.35))
		hp_gbar.text = "%d / %d" % [first.hp, first.max_hp]
	else:
		portrait_rect.texture = null
		shown_kind = ""
		lbl_name.text = "Никто не выбран"
		hp_gbar.ratio = 0.0
		hp_gbar.trail = 0.0
		hp_gbar.text = ""
	# характеристики или состав группы
	group_box.visible = sel.size() > 1
	lbl_stats.visible = sel.size() <= 1
	inv_box.visible = first != null and first.is_hero and sel.size() == 1
	var show_stats := sel.size() <= 1 and first != null
	for k in stat_rows:
		(stat_rows[k][0] as Control).visible = show_stats
		(stat_rows[k][1] as Control).visible = show_stats
	lbl_buff.visible = show_stats
	for n in hero_only:
		(n as Control).visible = show_stats and first.is_hero
	if show_stats:
		lbl_stats.text = first.title
		stat_rows["dmg"][1].text = "%d%s" % [first.dmg * (1.0 + 0.1 * (first.level - 1)), "  дальний бой" if first.ranged else ""]
		stat_rows["speed"][1].text = "%.1f" % first.speed
		stat_rows["hp"][1].text = "%d / %d" % [first.hp, first.max_hp]
		if first.is_hero:
			var nxt: float = game.XP_AT[first.level] if first.level < game.XP_AT.size() else first.xp
			var prev: float = game.XP_AT[first.level - 1]
			lbl_level.text = "Уровень %d      %d / %d" % [first.level, first.xp, nxt]
			xp_gbar.ratio = clampf((first.xp - prev) / maxf(nxt - prev, 1.0), 0.0, 1.0)
			xp_gbar.trail = xp_gbar.ratio
			xp_gbar.text = "%d / %d" % [first.xp, nxt]
		var buffs := ""
		if first.buff_t > 0.0: buffs += "Клич: +25% урона   "
		if first.guard_t > 0.0: buffs += "Стена: −30% урона"
		lbl_buff.text = buffs
	elif sel.size() > 1:
		lbl_stats.text = ""
		_rebuild_group(sel)
	else:
		lbl_stats.text = ""
	# команды
	var has_sel := sel.size() > 0
	for k in ["move", "stop", "hold", "attack"]:
		cmd_buttons[k].disabled = not has_sel
		(cmd_buttons[k].get_meta("ic") as Control).modulate = Color.WHITE if has_sel else Color(1, 1, 1, 0.35)
	for i in 4:
		var b: Button = cmd_buttons["a%d" % i]
		var open: bool = game.hero.level >= game.ABILITY_LEVEL[i]
		var cdl: float = maxf(0.0, game.ab_ready[i] - game.t_game)
		var cdr: ColorRect = b.get_meta("cd")
		var cdt: Label = b.get_meta("cdl")
		var aic: Control = b.get_meta("ic")
		if not open:
			b.disabled = true
			cdr.visible = true; cdr.size.y = 50.0
			cdt.text = "ур. %d" % game.ABILITY_LEVEL[i]
			cdt.add_theme_font_size_override("font_size", 14)
			aic.modulate = Color(1, 1, 1, 0.35)
		elif cdl > 0.0:
			b.disabled = true
			cdr.visible = true; cdr.size.y = 50.0 * cdl / maxf(game.ABILITY_CD[i], 0.1)
			cdt.text = str(ceili(cdl))
			cdt.add_theme_font_size_override("font_size", 20)
			aic.modulate = Color(1, 1, 1, 0.6)
		else:
			b.disabled = false
			cdr.visible = false
			cdt.text = ""
			aic.modulate = Color.WHITE
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
				c["bar"].size.x = 48.0 * clampf(u.hp / u.max_hp, 0.0, 1.0)
		return
	group_box.set_meta("key", key)
	for ch in group_box.get_children():
		ch.queue_free()
	group_cells.clear()
	for u in sel:
		var cell := Button.new()
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


func _aabb(n: Node, root: Node) -> AABB:
	var out := AABB()
	var first := true
	for mi in n.find_children("*", "MeshInstance3D", true, false):
		var m := mi as MeshInstance3D
		var t := Transform3D.IDENTITY
		var p: Node = m
		while p != null and p != root:
			if p is Node3D:
				t = (p as Node3D).transform * t
			p = p.get_parent()
		var a := t * m.get_aabb()
		out = a if first else out.merge(a)
		first = false
	return out
