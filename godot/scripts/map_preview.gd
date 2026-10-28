extends SceneTree
## Предпросмотр генерации: карты разных биомов и зёрен картинками сверху, с маршрутом, лагерями и сундуками.
## Запуск: godot --headless --path . --script scripts/map_preview.gd -- <биом> <зерно> [<зерно> ...]

func _init() -> void:
	var args := OS.get_cmdline_user_args()
	var biome := String(args[0])
	for k in range(1, args.size()):
		var sd := int(args[k])
		var spec := MapGen.new().generate(sd, biome, 110.0)
		var pl: Array = [[Vector3(spec["start"].x, 0, spec["start"].y), 10.0]]
		for c in spec["camps"]:
			pl.append([Vector3(c["pos"].x, 0, c["pos"].y), c["r"]])
		for c in spec["chests"]:
			pl.append([Vector3(c["pos"].x, 0, c["pos"].y), 5.0])
		var t := Terrain.new()
		t.setup(110.0, spec, pl)
		var nav := Nav.new()
		nav.build(t, 110.0)
		var s0 := nav.nearest_free(nav.cell_of(spec["start"]))
		var bad := 0
		var tg: Array = []
		for c in spec["camps"]: tg.append(c["pos"])
		for c in spec["chests"]: tg.append(c["pos"])
		for q in tg:
			var e := nav.nearest_free(nav.cell_of(q))
			var path := nav.astar.get_id_path(s0, e)
			if path.is_empty() or Vector2i(path[path.size() - 1]) != e:
				bad += 1
		if bad > 0:
			var line := ""
			for q in spec["route"]:
				line += "X" if nav.is_blocked(q.x, q.y) else "."
			print("ROUTE ", line)
			for g in spec["ridges"]:
				var pp: Vector2 = g["passes"][0]
				print("PASS ", pp, " blocked ", nav.is_blocked(pp.x, pp.y), " h ", t.height(pp.x, pp.y))
			var dbg := Image.create(nav.n, nav.n, false, Image.FORMAT_RGB8)
			var seen := PackedByteArray()
			seen.resize(nav.n * nav.n)
			var qu: Array = [s0]
			seen[s0.x * nav.n + s0.y] = 1
			var qi := 0
			while qi < qu.size():
				var cq: Vector2i = qu[qi]
				qi += 1
				for dd in [Vector2i(1, 0), Vector2i(-1, 0), Vector2i(0, 1), Vector2i(0, -1)]:
					var nq: Vector2i = cq + dd
					if nq.x < 0 or nq.y < 0 or nq.x >= nav.n or nq.y >= nav.n: continue
					var ix: int = nq.x * nav.n + nq.y
					if seen[ix] == 1 or nav.blocked[ix] == 1: continue
					seen[ix] = 1
					qu.append(nq)
			for ii in nav.n:
				for jj in nav.n:
					var col := Color(0.2, 0.2, 0.2)
					if nav.blocked[ii * nav.n + jj] == 0:
						col = Color(0.5, 0.9, 0.5) if seen[ii * nav.n + jj] == 1 else Color(0.9, 0.7, 0.3)
					dbg.set_pixel(ii, jj, col)
			dbg.save_png("O:/BattleSim/art/previews/nav_%s_%d.png" % [biome, sd])
		print("REACH ", biome, " ", sd, " unreachable ", bad, " of ", tg.size())
		var px := 384
		var img := t.render_map(px)
		var f := 110.0
		var mark := func(p: Vector2, r: int, col: Color):
			var x := int((p.x / f * 0.5 + 0.5) * px)
			var y := int((p.y / f * 0.5 + 0.5) * px)
			for dx in range(-r, r + 1):
				for dy in range(-r, r + 1):
					if dx * dx + dy * dy <= r * r and x + dx >= 0 and y + dy >= 0 and x + dx < px and y + dy < px:
						img.set_pixel(x + dx, y + dy, col)
		for c in spec["camps"]:
			mark.call(c["pos"], 6 if c["boss"] else 4, Color(0.95, 0.15, 0.1) if not c["tower"] else Color(0.95, 0.5, 0.1))
		for c in spec["chests"]:
			mark.call(c["pos"], 3, Color(1, 0.9, 0.2))
		mark.call(spec["start"], 5, Color(0.3, 0.8, 1.0))
		img.save_png("O:/BattleSim/art/previews/map_%s_%d.png" % [biome, sd])
		print("OK ", biome, " ", sd, " camps ", spec["camps"].size(), " chests ", spec["chests"].size())
	quit()
