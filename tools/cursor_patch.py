p = "godot/scripts/game.gd"
s = open(p, encoding="utf-8").read()
a = s.index("## Курсор из значка")
b = s.index("func _chests_tick")
new = '''## Курсоры нарисованного набора (res://assets/ui/gen/c_*.png), остриё берётся из cursor_hotspots.json.
var cursor_hot := {}


func _cursor_tex(name: String) -> Texture2D:
	if not cursor_tex.has(name):
		if cursor_hot.is_empty():
			cursor_hot = JSON.parse_string(FileAccess.get_file_as_string("res://assets/ui/gen/cursor_hotspots.json"))
		cursor_tex[name] = load("res://assets/ui/gen/%s.png" % name)
	return cursor_tex[name]


func _set_cursor(name: String) -> void:
	var t := _cursor_tex(name)
	var h: Array = cursor_hot.get(name, [20, 20])
	Input.set_custom_mouse_cursor(t, Input.CURSOR_ARROW, Vector2(h[0], h[1]))


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


'''
s = s[:a] + new + s[b:]
open(p, "w", encoding="utf-8").write(s)
print("ok")
