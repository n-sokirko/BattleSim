"""Одноразовая правка hud.gd: рамки и значки из нарисованного набора (godot/assets/ui/gen)."""
p = "godot/scripts/hud.gd"
s = open(p, encoding="utf-8").read()


def rep(a, b):
    global s
    assert a in s, a
    s = s.replace(a, b, 1)


rep('''static var _icons := {}
''', '''static var _icons := {}
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
''')
rep('''	mouse_filter = Control.MOUSE_FILTER_IGNORE
	selbox = SelBox.new()''', '''	mouse_filter = Control.MOUSE_FILTER_IGNORE
	texture_filter = CanvasItem.TEXTURE_FILTER_LINEAR_WITH_MIPMAPS
	selbox = SelBox.new()''')
rep('box.add_theme_stylebox_override("panel", _style(Color(DARK, 0.9), BRASS))', 'box.add_theme_stylebox_override("panel", _box("panel_small", 22))')
rep('''		var ic := _icon(box, rows[i][0], 24, rows[i][1])
		ic.position = Vector2(10, 7 + i * 28)''', '''		var ic := _gicon(box, ["boss", "camp", "chest"][i], 26)
		ic.position = Vector2(14, 8 + i * 27)''')
rep('lbl_goal.position = Vector2(42, 6)', 'lbl_goal.position = Vector2(48, 8)')
rep('''	var ps := _style(DARK, BRASS, 3, 0)
	ps.border_width_bottom = 0
	panel.add_theme_stylebox_override("panel", ps)''', '''	panel.add_theme_stylebox_override("panel", _box("panel_small", 24))''')
rep('mf.add_theme_stylebox_override("panel", _style(DARKER, BRASS, 3, 2))', 'mf.add_theme_stylebox_override("panel", _box("frame_minimap", 22))')
rep('''	minimap.position = Vector2(6, 6)
	minimap.size = Vector2(174, 174)''', '''	minimap.position = Vector2(15, 15)
	minimap.size = Vector2(156, 156)''')
rep('pf.add_theme_stylebox_override("panel", _style(DARKER, BRASS, 3, 2))', 'pf.add_theme_stylebox_override("panel", _box("panel_small", 22))')
rep('''	portrait_rect.position = Vector2(6, 6)
	portrait_rect.size = Vector2(138, 138)''', '''	portrait_rect.position = Vector2(14, 13)
	portrait_rect.size = Vector2(122, 122)''')
rep('''	lbl_name.position = Vector2(6, 146)
	lbl_name.size = Vector2(138, 20)''', '''	lbl_name.position = Vector2(6, 137)
	lbl_name.size = Vector2(138, 20)''')
rep('hp_back.position = Vector2(8, 168); hp_back.size = Vector2(134, 14)', 'hp_back.position = Vector2(16, 160); hp_back.size = Vector2(118, 14)')
rep('hp_bar.position = Vector2(9, 169); hp_bar.size = Vector2(132, 12)', 'hp_bar.position = Vector2(17, 161); hp_bar.size = Vector2(116, 12)')
rep('hp_text.position = Vector2(8, 167); hp_text.size = Vector2(134, 16)', 'hp_text.position = Vector2(16, 159); hp_text.size = Vector2(118, 16)')
rep('hp_bar.size.x = 132.0 * ratio', 'hp_bar.size.x = 116.0 * ratio')
rep('cf.add_theme_stylebox_override("panel", _style(DARKER, BRASS, 3, 2))', 'cf.add_theme_stylebox_override("panel", _box("panel_small", 22))')
rep('lbl_stats.position = Vector2(14, 8)', 'lbl_stats.position = Vector2(24, 14)')
rep('''		var ic := _icon(cf, key, 24, Color(0.85, 0.7, 0.35) if key != "hp" else Color(0.9, 0.35, 0.3))
		ic.position = Vector2(14, 42 + i * 28)''', '''		var ic := _gicon(cf, {"dmg": "damage", "speed": "speed", "hp": "health"}[key], 26)
		ic.position = Vector2(20, 42 + i * 28)''')
rep('lb.position = Vector2(46, 40 + i * 28)', 'lb.position = Vector2(54, 41 + i * 28)')
rep('''	var lv_ic := _icon(cf, "level", 24, Color(0.55, 0.85, 0.7))
	lv_ic.position = Vector2(14, 126)''', '''	var lv_ic := _gicon(cf, "level", 26)
	lv_ic.position = Vector2(20, 126)''')
rep('lbl_level.position = Vector2(46, 124)', 'lbl_level.position = Vector2(54, 125)')
rep('lbl_buff.position = Vector2(14, 168)', 'lbl_buff.position = Vector2(24, 166)')
rep('group_box.position = Vector2(12, 10)', 'group_box.position = Vector2(18, 16)')
rep('slot.add_theme_stylebox_override("panel", _style(Color(0.03, 0.028, 0.025), Color(0.38, 0.3, 0.16), 2, 2))', 'slot.add_theme_stylebox_override("panel", _box("slot_big", 16))')
rep('inv_box.position = Vector2(270, 12)', 'inv_box.position = Vector2(268, 16)')
rep('kf.add_theme_stylebox_override("panel", _style(DARKER, BRASS, 3, 2))', 'kf.add_theme_stylebox_override("panel", _box("panel_small", 22))')
rep('''		b.size = Vector2(72, 54)
		b.position = Vector2(8 + (i % 4) * 78, 8 + (i / 4) * 62)''', '''		b.size = Vector2(68, 66)
		b.position = Vector2(16 + (i % 4) * 73, 12 + (i / 4) * 70)''')
rep('''		b.add_theme_stylebox_override("normal", _style(Color(0.2, 0.15, 0.09), BRASS, 2, 3))
		b.add_theme_stylebox_override("hover", _style(Color(0.3, 0.22, 0.12), BRASS_HI, 2, 3))
		b.add_theme_stylebox_override("pressed", _style(Color(0.4, 0.3, 0.14), BRASS_HI, 2, 3))
		b.add_theme_stylebox_override("disabled", _style(Color(0.1, 0.09, 0.08), Color(0.3, 0.25, 0.15), 2, 3))''', '''		b.add_theme_stylebox_override("normal", StyleBoxEmpty.new())
		b.add_theme_stylebox_override("hover", _style(Color(1, 0.9, 0.5, 0.10), BRASS_HI, 2, 4))
		b.add_theme_stylebox_override("pressed", _style(Color(1, 0.9, 0.5, 0.25), BRASS_HI, 2, 4))
		b.add_theme_stylebox_override("disabled", StyleBoxEmpty.new())''')
rep('''		var ic := _icon(b, cmds[i][0], 30, INK)
		ic.position = Vector2(21, 3)''', '''		var ic := _gicon(b, {"move": "move", "stop": "stop", "hold": "hold", "attack": "attack", "a0": "ab_cry", "a1": "ab_slam", "a2": "ab_taunt", "a3": "ab_ward"}[cmds[i][0]], 50)
		ic.position = Vector2(9, 1)''')
rep('hk.position = Vector2(5, 1)', 'hk.position = Vector2(12, 2)')
rep('''		nm.position = Vector2(0, 36)
		nm.size = Vector2(72, 16)''', '''		nm.position = Vector2(0, 51)
		nm.size = Vector2(68, 15)''')
rep('cd.position = Vector2(2, 2); cd.size = Vector2(68, 50)', 'cd.position = Vector2(9, 1); cd.size = Vector2(50, 50)')
rep('cl.position = Vector2(0, 12); cl.size = Vector2(72, 28)', 'cl.position = Vector2(0, 12); cl.size = Vector2(68, 28)')
rep('hint.position = Vector2(8, 138)', 'hint.position = Vector2(16, 150)')
rep('hint2.position = Vector2(8, 156)', 'hint2.position = Vector2(16, 165)')
rep('(cmd_buttons[k].get_meta("ic") as Control).modulate = INK if has_sel else Color(INK, 0.3)', '(cmd_buttons[k].get_meta("ic") as Control).modulate = Color.WHITE if has_sel else Color(1, 1, 1, 0.35)')
rep('aic.modulate = Color(INK, 0.3)', 'aic.modulate = Color(1, 1, 1, 0.35)')
rep('aic.modulate = Color(INK, 0.55)', 'aic.modulate = Color(1, 1, 1, 0.6)')
rep('aic.modulate = Color(1.0, 0.9, 0.55)', 'aic.modulate = Color.WHITE')
open(p, "w", encoding="utf-8").write(s)
print("ok")
