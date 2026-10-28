class_name MapGen
extends RefCounted
## Генератор карты по зерну и биому. Строит каркас: маршрут от старта к вожаку, реки с бродами на маршруте,
## хребты с перевалами, озёра, лагеря по возрастающей сложности, заставу на переправе, сундуки в стороне, дороги.
## Рельеф, расстановка и нав-сетка потом строятся из этого описания (spec), сама геометрия здесь только линии и круги.

## Параметры биомов. pal — палитра земли (sRGB), деревья/кусты берутся из набора res://assets/nature/prefabs.
const BIOMES := {
	"meadow": {
		"name": "Пограничный луг", "base": 2.4, "amp": 4.2, "freq": 0.011, "rivers": 1, "ridges": 0, "lakes": 1, "camps": 5, "chests": 2,
		"wood_thr": 0.60, "wood_n": 1500, "tree_free": 40, "bush": 130, "rock": 200, "flower": 520, "reed": 120,
		"trees": ["pine_tree_1", "pine_tree_2", "pine_tree_3", "normal_tree_1", "normal_tree_2", "normal_tree_3", "normal_tree_4", "birch_tree_1", "birch_tree_2", "birch_tree_3", "maple_tree_1", "maple_tree_2"],
		"dead": ["dead_tree_1", "dead_tree_2", "dead_tree_3"],
		"pal": {"a": Color(0.30, 0.45, 0.18), "b": Color(0.46, 0.55, 0.22), "dry": Color(0.62, 0.55, 0.28), "shore": Color(0.62, 0.54, 0.36), "bed": Color(0.34, 0.33, 0.28),
			"flat": Color(0.52, 0.41, 0.27), "road": Color(0.56, 0.44, 0.29), "rock": Color(0.46, 0.44, 0.42), "peak": Color(0.5, 0.48, 0.46)},
		"water": Color(0.16, 0.38, 0.5), "sky_top": Color(0.35, 0.55, 0.85), "sky_hor": Color(0.75, 0.82, 0.9), "fog": 0.0012, "sun": 1.35,
	},
	"forest": {
		"name": "Тёмный бор", "base": 2.6, "amp": 3.2, "freq": 0.013, "rivers": 1, "ridges": 0, "lakes": 2, "camps": 6, "chests": 3,
		"wood_thr": 0.40, "wood_n": 5200, "tree_free": 60, "bush": 260, "rock": 110, "flower": 160, "reed": 100,
		"trees": ["pine_tree_1", "pine_tree_2", "pine_tree_3", "pine_tree_4", "pine_tree_5", "pine_tree_1", "pine_tree_2", "normal_tree_2", "birch_tree_2"],
		"dead": ["dead_tree_1", "dead_tree_2", "dead_tree_3", "dead_tree_4"],
		"pal": {"a": Color(0.15, 0.30, 0.13), "b": Color(0.24, 0.38, 0.15), "dry": Color(0.36, 0.34, 0.18), "shore": Color(0.40, 0.36, 0.26), "bed": Color(0.26, 0.26, 0.22),
			"flat": Color(0.40, 0.31, 0.2), "road": Color(0.42, 0.34, 0.23), "rock": Color(0.36, 0.36, 0.35), "peak": Color(0.42, 0.42, 0.42)},
		"water": Color(0.10, 0.26, 0.30), "sky_top": Color(0.28, 0.42, 0.52), "sky_hor": Color(0.55, 0.65, 0.66), "fog": 0.0034, "sun": 1.05,
	},
	"steppe": {
		"name": "Выжженная степь", "base": 2.2, "amp": 2.4, "freq": 0.008, "rivers": 1, "ridges": 0, "lakes": 0, "camps": 6, "chests": 2,
		"wood_thr": 0.85, "wood_n": 200, "tree_free": 6, "bush": 60, "rock": 170, "flower": 140, "reed": 70,
		"trees": ["normal_tree_1", "normal_tree_3", "maple_tree_1"],
		"dead": ["dead_tree_1", "dead_tree_2", "dead_tree_3", "dead_tree_4"],
		"pal": {"a": Color(0.58, 0.52, 0.26), "b": Color(0.68, 0.58, 0.30), "dry": Color(0.74, 0.62, 0.34), "shore": Color(0.72, 0.62, 0.42), "bed": Color(0.45, 0.40, 0.30),
			"flat": Color(0.62, 0.48, 0.30), "road": Color(0.66, 0.52, 0.34), "rock": Color(0.56, 0.48, 0.40), "peak": Color(0.6, 0.52, 0.45)},
		"water": Color(0.2, 0.42, 0.5), "sky_top": Color(0.45, 0.6, 0.82), "sky_hor": Color(0.9, 0.84, 0.7), "fog": 0.0016, "sun": 1.55,
	},
	"highland": {
		"name": "Перевал", "base": 3.4, "amp": 6.5, "freq": 0.012, "rivers": 1, "ridges": 2, "lakes": 1, "camps": 6, "chests": 3,
		"wood_thr": 0.58, "wood_n": 1300, "tree_free": 30, "bush": 90, "rock": 380, "flower": 120, "reed": 60,
		"trees": ["pine_tree_1", "pine_tree_2", "pine_tree_3", "pine_tree_4", "pine_tree_5"],
		"dead": ["dead_tree_1", "dead_tree_2"],
		"pal": {"a": Color(0.30, 0.40, 0.24), "b": Color(0.40, 0.47, 0.28), "dry": Color(0.50, 0.48, 0.34), "shore": Color(0.55, 0.52, 0.42), "bed": Color(0.34, 0.34, 0.32),
			"flat": Color(0.46, 0.40, 0.30), "road": Color(0.5, 0.44, 0.34), "rock": Color(0.5, 0.5, 0.52), "peak": Color(0.86, 0.88, 0.92)},
		"water": Color(0.22, 0.42, 0.52), "sky_top": Color(0.38, 0.55, 0.82), "sky_hor": Color(0.82, 0.88, 0.94), "fog": 0.0018, "sun": 1.4,
	},
	"swamp": {
		"name": "Болото", "floor": -0.7, "base": 0.8, "amp": 1.5, "freq": 0.02, "rivers": 1, "ridges": 0, "lakes": 5, "camps": 6, "chests": 2,
		"wood_thr": 0.55, "wood_n": 900, "tree_free": 30, "bush": 220, "rock": 60, "flower": 300, "reed": 360,
		"trees": ["dead_tree_1", "dead_tree_2", "dead_tree_3", "dead_tree_4", "normal_tree_2", "birch_tree_1"],
		"dead": ["dead_tree_1", "dead_tree_2", "dead_tree_3", "dead_tree_4"],
		"pal": {"a": Color(0.22, 0.30, 0.14), "b": Color(0.30, 0.36, 0.16), "dry": Color(0.38, 0.36, 0.2), "shore": Color(0.30, 0.28, 0.18), "bed": Color(0.2, 0.2, 0.15),
			"flat": Color(0.36, 0.30, 0.2), "road": Color(0.4, 0.34, 0.22), "rock": Color(0.36, 0.36, 0.34), "peak": Color(0.4, 0.4, 0.4)},
		"water": Color(0.14, 0.28, 0.22), "sky_top": Color(0.34, 0.45, 0.48), "sky_hor": Color(0.62, 0.68, 0.6), "fog": 0.0038, "sun": 0.95,
	},
}

const NAMES := {
	"raider": ["Дозорные", "Сторожевой отряд", "Налётчики", "Караул"],
	"rarcher": ["Лучники у камней", "Стрелки в засаде", "Охотники"],
	"rider": ["Конный разъезд", "Всадники", "Гонцы"],
	"mixed": ["Лагерь погонщиков", "Становище", "Отряд сборщиков", "Стоянка"],
}

var rng := RandomNumberGenerator.new()
var spec := {}
var field := 110.0
var route := PackedVector2Array()
var route_cum := PackedFloat32Array()


func generate(seed_value: int, biome: String, f: float) -> Dictionary:
	rng.seed = seed_value
	field = f
	var b: Dictionary = BIOMES[biome]
	spec = {"seed": seed_value, "biome": biome, "b": b, "field": f, "rivers": [], "ridges": [], "lakes": [], "camps": [], "chests": [], "roads": []}
	_route()
	for i in int(b["rivers"]):
		_river(0.24 + 0.36 * i + rng.randf_range(-0.03, 0.05), 4.0 + rng.randf() * 2.0)
	for i in int(b["ridges"]):
		_ridge(0.5 + 0.17 * i + rng.randf_range(-0.04, 0.04))
	_camps(int(b["camps"]))
	_chests(int(b["chests"]))
	_lakes(int(b["lakes"]))
	_roads()
	return spec


# ---------------------------------------------------------------- геометрия

static func seg_dist(p: Vector2, a: Vector2, b: Vector2) -> float:
	var ab := b - a
	var t := clampf((p - a).dot(ab) / maxf(ab.length_squared(), 0.0001), 0.0, 1.0)
	return (p - (a + ab * t)).length()


static func poly_dist(pts: PackedVector2Array, p: Vector2) -> float:
	var best := 1e9
	for i in pts.size() - 1:
		best = minf(best, seg_dist(p, pts[i], pts[i + 1]))
	return best


func route_at(t: float) -> Array:
	var total := route_cum[route_cum.size() - 1]
	var s := clampf(t, 0.0, 1.0) * total
	for i in range(1, route.size()):
		if route_cum[i] >= s:
			var seg := route_cum[i] - route_cum[i - 1]
			var k := (s - route_cum[i - 1]) / maxf(seg, 0.001)
			var pos := route[i - 1].lerp(route[i], k)
			return [pos, (route[i] - route[i - 1]).normalized()]
	return [route[route.size() - 1], Vector2.RIGHT]


func _route() -> void:
	var a := (rng.randi() % 4) * PI / 2.0 + rng.randf_range(-0.45, 0.45)
	var d := Vector2(cos(a), sin(a))
	var perp := Vector2(-d.y, d.x)
	var start := d * field * 0.76
	var boss := -d * field * 0.72 + perp * rng.randf_range(-0.2, 0.2) * field
	var ctl: Array[Vector2] = [start]
	for k in range(1, 4):
		ctl.append(start.lerp(boss, k / 4.0) + perp * rng.randf_range(-0.24, 0.24) * field)
	ctl.append(boss)
	route = PackedVector2Array()
	for i in ctl.size() - 1:
		var p0 := ctl[maxi(i - 1, 0)]
		var p3 := ctl[mini(i + 2, ctl.size() - 1)]
		for s in 10:
			route.append(ctl[i].cubic_interpolate(ctl[i + 1], p0, p3, s / 10.0))
	route.append(boss)
	route_cum.resize(route.size())
	route_cum[0] = 0.0
	for i in range(1, route.size()):
		route_cum[i] = route_cum[i - 1] + route[i].distance_to(route[i - 1])
	spec["start"] = start
	spec["boss_pos"] = boss
	spec["route"] = route


## Река пересекает маршрут в точке t; в месте пересечения и ещё в одном есть брод.
func _river(t: float, width: float) -> void:
	var rp: Array = route_at(t)
	var P: Vector2 = rp[0]
	var tg: Vector2 = rp[1]
	var dir := Vector2(-tg.y, tg.x).rotated(rng.randf_range(-0.5, 0.5))
	var nrm := Vector2(-dir.y, dir.x)
	var a1 := rng.randf_range(6.0, 13.0)
	var a2 := rng.randf_range(2.0, 5.0)
	var p1 := rng.randf() * TAU
	var p2 := rng.randf() * TAU
	var f1 := rng.randf_range(2.2, 3.4)
	var f2 := rng.randf_range(6.0, 9.0)
	var off0 := a1 * sin(p1) + a2 * sin(p2)
	var pts := PackedVector2Array()
	var ws := PackedFloat32Array()
	var L := field * 2.4
	var N := 52
	for i in N + 1:
		var s := (float(i) / N) * 2.0 - 1.0
		var o := a1 * sin(s * f1 + p1) + a2 * sin(s * f2 + p2) - off0
		pts.append(P + dir * (s * L * 0.5) + nrm * o)
		ws.append(width * (1.0 + 0.22 * sin(s * 5.0 + p1)))
	var fords: Array[Vector2] = [P]
	var k := N / 2 + int(rng.randf_range(0.14, 0.2) * N * (1.0 if rng.randf() < 0.5 else -1.0))
	fords.append(pts[clampi(k, 2, N - 2)])
	(spec["rivers"] as Array).append({"pts": pts, "w": ws, "fords": fords})


## Хребет поперёк маршрута с перевалом на маршруте (и вторым перевалом в стороне).
func _ridge(t: float) -> void:
	var rp: Array = route_at(t)
	var P: Vector2 = rp[0]
	var tg: Vector2 = rp[1]
	var dir := Vector2(-tg.y, tg.x).rotated(rng.randf_range(-0.35, 0.35))
	var nrm := Vector2(-dir.y, dir.x)
	var ph := rng.randf() * TAU
	var pts := PackedVector2Array()
	var L := field * 2.6
	for i in 31:
		var s := (float(i) / 30.0) * 2.0 - 1.0
		pts.append(P + dir * (s * L * 0.5) + nrm * (9.0 * sin(s * 4.0 + ph) - 9.0 * sin(ph)))
	var passes: Array[Vector2] = [P]
	passes.append(pts[clampi(15 + int(rng.randf_range(0.18, 0.28) * 30.0) * (1 if rng.randf() < 0.5 else -1), 3, 27)])
	(spec["ridges"] as Array).append({"pts": pts, "w": rng.randf_range(7.0, 9.5), "h": rng.randf_range(11.0, 15.0), "passes": passes})


## Расстояние от точки до воды и хребтов (минус их ширина): меньше нуля — на препятствии.
func obstacle_gap(p: Vector2) -> float:
	var best := 1e9
	for r in spec["rivers"]:
		var pts: PackedVector2Array = r["pts"]
		var w: PackedFloat32Array = r["w"]
		for i in pts.size() - 1:
			best = minf(best, seg_dist(p, pts[i], pts[i + 1]) - w[i] * 2.4)
	for g in spec["ridges"]:
		best = minf(best, poly_dist(g["pts"], p) - g["w"] * 1.05)
	for l in spec["lakes"]:
		best = minf(best, p.distance_to(l[0]) - l[1] * 1.2)
	return best


func _near_any(p: Vector2, min_d: float) -> bool:
	if p.distance_to(spec["start"]) < min_d + 10.0:
		return true
	for c in spec["camps"]:
		if p.distance_to(c["pos"]) < min_d:
			return true
	for c in spec["chests"]:
		if p.distance_to(c["pos"]) < min_d * 0.6:
			return true
	return false


func _comp(tier: float) -> Array:
	# [состав, ключ имён]
	var roll := rng.randf()
	if tier < 0.3:
		return [{"raider": 3}, "raider"] if roll < 0.6 else [{"rarcher": 3}, "rarcher"]
	if tier < 0.55:
		if roll < 0.35:
			return [{"rider": 3}, "rider"]
		if roll < 0.7:
			return [{"rarcher": 3, "raider": 1}, "mixed"]
		return [{"raider": 4}, "raider"]
	if tier < 0.8:
		return [{"raider": 3, "rarcher": 2}, "mixed"] if roll < 0.6 else [{"rider": 3, "rarcher": 2}, "mixed"]
	return [{"raider": 3, "rider": 2, "rarcher": 2}, "mixed"]


func _camps(n: int) -> void:
	var camps: Array = spec["camps"]
	var used := {}
	for i in n:
		var t := 0.13 + 0.67 * (float(i) / maxf(n - 1, 1)) + rng.randf_range(-0.025, 0.025)
		var placed := false
		for attempt in 90:
			var rp: Array = route_at(t + rng.randf_range(-0.06, 0.06))
			var tg: Vector2 = rp[1]
			var side := -1.0 if (i + attempt) % 2 == 0 else 1.0
			var p: Vector2 = rp[0] + Vector2(-tg.y, tg.x) * side * rng.randf_range(9.0, 40.0)
			if absf(p.x) > field * 0.84 or absf(p.y) > field * 0.84:
				continue
			if obstacle_gap(p) < (16.0 if attempt < 40 else 8.0) or _near_any(p, 30.0 if attempt < 50 else 24.0):
				continue
			var cp := _comp(t)
			var key: String = cp[1]
			var pool: Array = NAMES[key]
			var nm: String = pool[rng.randi() % pool.size()]
			while used.has(nm) and used.size() < 3:
				nm = pool[rng.randi() % pool.size()]
			used[nm] = true
			var units := 0
			for k in cp[0]:
				units += int(cp[0][k])
			camps.append({"name": nm, "pos": p, "comp": cp[0], "boss": false, "xp": 8.0 * units + 4.0 * i, "tower": false, "r": 11.0, "t": t})
			placed = true
			break
		if not placed:
			continue
	# застава на переправе: на пассе хребта или на броде первой реки
	var guard := Vector2.INF
	var ridges: Array = spec["ridges"]
	var rivers: Array = spec["rivers"]
	if ridges.size() > 0:
		guard = ridges[0]["passes"][0]
	elif rivers.size() > 0:
		guard = rivers[0]["fords"][0]
	if guard != Vector2.INF:
		var best := Vector2.INF
		# точка за переправой по ходу движения, чуть в стороне от дороги
		var tt := 0.5
		var gd := 1e9
		for s in range(0, 100):
			var tq := s / 99.0
			var q2: Vector2 = route_at(tq)[0]
			if q2.distance_to(guard) < gd:
				gd = q2.distance_to(guard); tt = tq
		var total: float = route_cum[route_cum.size() - 1]
		var fw: Array = route_at(minf(tt + 36.0 / total, 1.0))
		var ftan: Vector2 = fw[1]
		for side in [1.0, -1.0]:
			var q3: Vector2 = fw[0] + Vector2(-ftan.y, ftan.x) * side * 11.0
			if obstacle_gap(q3) < 16.0 or _near_any(q3, 22.0) or absf(q3.x) > field * 0.84 or absf(q3.y) > field * 0.84:
				continue
			best = q3
			break
		if best != Vector2.INF:
			camps.append({"name": "Застава у переправы", "pos": best, "comp": {"raider": 2, "rarcher": 2}, "boss": false, "xp": 34.0, "tower": true, "r": 11.0, "t": tt + 36.0 / total})
	camps.sort_custom(func(x, y): return x["t"] < y["t"])
	# вожак на краю маршрута
	camps.append({"name": "Стан Кобека", "pos": spec["boss_pos"], "comp": {"raider": 4, "rarcher": 2, "rider": 2, "boss": 1}, "boss": true, "xp": 0.0, "tower": true, "r": 14.0, "t": 1.0})


func _chests(n: int) -> void:
	var chests: Array = spec["chests"]
	var camps: Array = spec["camps"]
	var names := ["Брошенный обоз", "Тайник у холма", "Схрон в чаще", "Забытая повозка", "Разорённая кладовая"]
	var xps := [30.0, 40.0, 50.0]
	for i in n:
		var best := Vector2.INF
		var bscore := -1e9
		for attempt in 140:
			var p: Vector2
			if i == 0 and camps.size() > 1:
				# обоз за спиной ближнего лагеря, подальше от дороги
				var c: Dictionary = camps[rng.randi() % (camps.size() - 1)]
				var rp: Array = route_at(float(c["t"]))
				var away: Vector2 = (c["pos"] - rp[0]).normalized()
				p = c["pos"] + away * rng.randf_range(14.0, 20.0)
			else:
				var rp2: Array = route_at(rng.randf_range(0.25, 0.8))
				var tg: Vector2 = rp2[1]
				p = rp2[0] + Vector2(-tg.y, tg.x) * (1.0 if rng.randf() < 0.5 else -1.0) * rng.randf_range(24.0, 62.0)
			if absf(p.x) > field * 0.82 or absf(p.y) > field * 0.82:
				continue
			if obstacle_gap(p) < 12.0 or _near_any(p, 12.0 if i == 0 else 22.0):
				continue
			var score := poly_dist(route, p)
			if i == 0:
				score = -absf(score - 30.0)
			if score > bscore:
				bscore = score; best = p
		if best != Vector2.INF:
			chests.append({"name": names[i % names.size()], "pos": best, "xp": xps[i % xps.size()]})


func _lakes(n: int) -> void:
	var lakes: Array = spec["lakes"]
	for i in n:
		for attempt in 60:
			var r := rng.randf_range(8.0, 15.0)
			var p := Vector2(rng.randf_range(-0.78, 0.78), rng.randf_range(-0.78, 0.78)) * field
			if poly_dist(route, p) < r + 12.0 or _near_any(p, r + 14.0):
				continue
			if obstacle_gap(p) < r + 8.0:
				continue
			lakes.append([p, r])
			break


func _roads() -> void:
	var roads: Array = spec["roads"]
	for i in range(0, route.size() - 1, 2):
		roads.append([route[i], route[mini(i + 2, route.size() - 1)]])
	for c in spec["camps"]:
		var best := Vector2.ZERO
		var bd := 1e9
		for q in route:
			var d: float = q.distance_to(c["pos"])
			if d < bd:
				bd = d; best = q
		if bd > 6.0:
			roads.append([best, c["pos"]])
