class_name Nav
extends RefCounted
## Проходимость: сетка 1×1 м. Непроходимы глубокая вода, крутые склоны, горный край и препятствия (деревья, камни, постройки).
## Путь ищет A* по сетке (склоны дороже), затем выпрямляется по линии видимости.

const UNIT_R := 0.5          # запас на ширину бойца
const MAX_GRADE := 0.6       # круче — стена
const DEEP_Y := -0.5         # глубже этой отметки воды не пройти (брод мельче)

var n := 0
var origin := 0.0
var blocked := PackedByteArray()      # индекс i * n + j, как у Terrain.heights
var circles: Array = []               # [Vector2 центр, радиус] — препятствия, добавленные до build()
var astar := AStarGrid2D.new()
var built := false


func add_circle(p: Vector3, r: float) -> void:
	circles.append([Vector2(p.x, p.z), r])
	if built:
		_mark_circle(Vector2(p.x, p.z), r)


func build(t: Terrain, field: float) -> void:
	n = t.grid_n
	origin = -t.extent
	blocked.resize(n * n)
	blocked.fill(0)
	astar.region = Rect2i(0, 0, n, n)
	astar.cell_size = Vector2.ONE
	astar.diagonal_mode = AStarGrid2D.DIAGONAL_MODE_ONLY_IF_NO_OBSTACLES
	astar.default_compute_heuristic = AStarGrid2D.HEURISTIC_OCTILE
	astar.default_estimate_heuristic = AStarGrid2D.HEURISTIC_OCTILE
	astar.update()
	var lim := field * 0.97
	for i in n:
		var x := origin + i
		for j in n:
			var z := origin + j
			var h := t.heights[i * n + j]
			var hl := t.heights[maxi(i - 1, 0) * n + j]
			var hr := t.heights[mini(i + 1, n - 1) * n + j]
			var hd := t.heights[i * n + maxi(j - 1, 0)]
			var hu := t.heights[i * n + mini(j + 1, n - 1)]
			var grade := Vector2((hr - hl) * 0.5, (hu - hd) * 0.5).length()
			if absf(x) > lim or absf(z) > lim or h < Terrain.WATER_Y + DEEP_Y or grade > MAX_GRADE:
				blocked[i * n + j] = 1
			elif grade > 0.12:
				astar.set_point_weight_scale(Vector2i(i, j), 1.0 + grade * 5.0)
	for c in circles:
		_mark_circle(c[0], c[1])
	built = true
	for i in n:
		for j in n:
			if blocked[i * n + j] == 1:
				astar.set_point_solid(Vector2i(i, j), true)


func _mark_circle(c: Vector2, r: float) -> void:
	var rr := r + UNIT_R
	var i0 := maxi(int(floor(c.x - rr - origin)), 0)
	var i1 := mini(int(ceil(c.x + rr - origin)), n - 1)
	var j0 := maxi(int(floor(c.y - rr - origin)), 0)
	var j1 := mini(int(ceil(c.y + rr - origin)), n - 1)
	for i in range(i0, i1 + 1):
		for j in range(j0, j1 + 1):
			if Vector2(origin + i, origin + j).distance_to(c) <= rr:
				blocked[i * n + j] = 1
				if built:
					astar.set_point_solid(Vector2i(i, j), true)


func cell_of(p: Vector2) -> Vector2i:
	return Vector2i(clampi(roundi(p.x - origin), 0, n - 1), clampi(roundi(p.y - origin), 0, n - 1))


func is_blocked(x: float, z: float) -> bool:
	var i := roundi(x - origin)
	var j := roundi(z - origin)
	if i < 0 or j < 0 or i >= n or j >= n:
		return true
	return blocked[i * n + j] == 1


func nearest_free(c: Vector2i) -> Vector2i:
	if blocked[c.x * n + c.y] == 0:
		return c
	for r in range(1, 14):
		var best := Vector2i(-1, -1)
		var bd := 1e9
		for dx in range(-r, r + 1):
			for dy in range(-r, r + 1):
				if maxi(absi(dx), absi(dy)) != r:
					continue
				var q := Vector2i(c.x + dx, c.y + dy)
				if q.x < 0 or q.y < 0 or q.x >= n or q.y >= n or blocked[q.x * n + q.y] == 1:
					continue
				var d := float(dx * dx + dy * dy)
				if d < bd:
					bd = d; best = q
		if best.x >= 0:
			return best
	return c


func los(a: Vector2, b: Vector2) -> bool:
	var d := b - a
	var len := d.length()
	if len < 0.01:
		return true
	var steps := int(len / 0.5) + 1
	for k in range(0, steps + 1):
		var p := a + d * (float(k) / steps)
		if is_blocked(p.x, p.y):
			return false
	return true


## Путь от a до b точками (без стартовой). Если прямой путь свободен — одна точка.
func find(from: Vector3, to: Vector3) -> PackedVector3Array:
	var a := Vector2(from.x, from.z)
	var b := Vector2(to.x, to.z)
	if not built:
		return PackedVector3Array([to])
	if not is_blocked(b.x, b.y) and los(a, b):
		return PackedVector3Array([to])
	var ca := nearest_free(cell_of(a))
	var cb := nearest_free(cell_of(b))
	var ids := astar.get_id_path(ca, cb)
	if ids.size() < 2:
		return PackedVector3Array([Vector3(origin + cb.x, 0.0, origin + cb.y)])
	var pts: Array[Vector2] = []
	for id in ids:
		pts.append(Vector2(origin + id.x, origin + id.y))
	var out := PackedVector3Array()
	var cur := a
	var i := 0
	while i < pts.size():
		var j := mini(i + 24, pts.size() - 1)
		while j > i and not los(cur, pts[j]):
			j -= 1
		cur = pts[j]
		out.append(Vector3(cur.x, 0.0, cur.y))
		i = j + 1
	if not is_blocked(b.x, b.y):
		out[out.size() - 1] = Vector3(b.x, 0.0, b.y)
	return out
