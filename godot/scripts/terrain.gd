class_name Terrain
extends RefCounted
## Процедурный ландшафт по описанию карты (MapGen): базовый шум биома, хребты с перевалами, озёра, реки с бродами,
## поляны под лагеря. Высоты считаются один раз в сетку 1 м; height(x, z) берёт её по тем же треугольникам, что и меш.

const WATER_Y := 0.0
const STEP := 1.0

var field := 110.0
var extent := 143.0
var spec := {}
var pal := {}
var base_h := 2.4
var amp_h := 4.2
var floor_h := 0.8
var n_hills := FastNoiseLite.new()
var n_detail := FastNoiseLite.new()
var n_color := FastNoiseLite.new()
var flats: Array = []        # [Vector2 центр, радиус, высота]
var grid_n := 0
var heights := PackedFloat32Array()      # индекс i * n + j (i — по x, j — по z)
var road_grid := PackedFloat32Array()    # расстояние до дороги, м (не больше 12)
var wet_grid := PackedFloat32Array()     # расстояние до берега, м (на воде меньше нуля, не больше 40)
var fog: Fog


func setup(f: float, sp: Dictionary, poi_list: Array) -> void:
	field = f
	extent = f * 1.3
	spec = sp
	var b: Dictionary = sp["b"]
	pal = b["pal"]
	base_h = b["base"]; amp_h = b["amp"]; floor_h = b.get("floor", 0.8)
	var sd: int = sp["seed"]
	n_hills.seed = sd * 3 + 5
	n_hills.frequency = b["freq"]
	n_hills.fractal_octaves = 3
	n_detail.seed = sd * 3 + 9
	n_detail.frequency = 0.05
	n_color.seed = sd * 3 + 21
	n_color.frequency = 0.03
	n_color.fractal_octaves = 3
	for p in poi_list:           # [Vector3 позиция, радиус]
		var q: Vector3 = p[0]
		flats.append([Vector2(q.x, q.z), p[1], maxf(_raw(q.x, q.z), 0.8)])
	_build_grid()


func _raw(x: float, z: float) -> float:
	var h := base_h + n_hills.get_noise_2d(x, z) * amp_h + n_detail.get_noise_2d(x, z) * 0.45
	h = maxf(h, floor_h)
	var e := maxf(absf(x), absf(z))
	var rim := field * 0.86
	if e > rim:
		var k := (e - rim) / (field * 0.25)
		h += k * k * 26.0
	return h


## Расстояние (в долях местной ширины) до ломаной на каждую ячейку: меньше — ближе к оси. Дальше reach ширин — 9.
func _raster_u(pts: PackedVector2Array, widths: PackedFloat32Array, reach: float) -> PackedFloat32Array:
	var n := grid_n
	var g := PackedFloat32Array()
	g.resize(n * n)
	g.fill(9.0)
	for s in pts.size() - 1:
		var a := pts[s]
		var b := pts[s + 1]
		var w0 := widths[s]
		var w1 := widths[s + 1]
		var rr := maxf(w0, w1) * reach
		var i0 := maxi(int(floor(minf(a.x, b.x) - rr + extent)), 0)
		var i1 := mini(int(ceil(maxf(a.x, b.x) + rr + extent)), n - 1)
		var j0 := maxi(int(floor(minf(a.y, b.y) - rr + extent)), 0)
		var j1 := mini(int(ceil(maxf(a.y, b.y) + rr + extent)), n - 1)
		var ab := b - a
		var l2 := maxf(ab.length_squared(), 0.0001)
		for i in range(i0, i1 + 1):
			for j in range(j0, j1 + 1):
				var p := Vector2(-extent + i, -extent + j)
				var t := clampf((p - a).dot(ab) / l2, 0.0, 1.0)
				var u := (p - (a + ab * t)).length() / lerpf(w0, w1, t)
				if u < g[i * n + j]:
					g[i * n + j] = u
	return g


## Расстояние до ломаной (минус ширина) на каждую ячейку, до cap метров.
func _raster_gap(g: PackedFloat32Array, pts: PackedVector2Array, widths: PackedFloat32Array, cap: float) -> void:
	var n := grid_n
	for s in pts.size() - 1:
		var a := pts[s]
		var b := pts[s + 1]
		var rr := cap + maxf(widths[s], widths[s + 1])
		var i0 := maxi(int(floor(minf(a.x, b.x) - rr + extent)), 0)
		var i1 := mini(int(ceil(maxf(a.x, b.x) + rr + extent)), n - 1)
		var j0 := maxi(int(floor(minf(a.y, b.y) - rr + extent)), 0)
		var j1 := mini(int(ceil(maxf(a.y, b.y) + rr + extent)), n - 1)
		var ab := b - a
		var l2 := maxf(ab.length_squared(), 0.0001)
		for i in range(i0, i1 + 1):
			for j in range(j0, j1 + 1):
				var p := Vector2(-extent + i, -extent + j)
				var t := clampf((p - a).dot(ab) / l2, 0.0, 1.0)
				var d := (p - (a + ab * t)).length() - lerpf(widths[s], widths[s + 1], t)
				if d < g[i * n + j]:
					g[i * n + j] = d


func _build_grid() -> void:
	var n := int(extent * 2.0 / STEP) + 1
	grid_n = n
	heights.resize(n * n)
	for i in n:
		var x := -extent + i * STEP
		for j in n:
			heights[i * n + j] = _raw(x, -extent + j * STEP)
	# хребты: стена вдоль оси, на перевалах опускается
	for g in spec["ridges"]:
		var pts: PackedVector2Array = g["pts"]
		var ws := PackedFloat32Array()
		ws.resize(pts.size())
		ws.fill(g["w"])
		var u := _raster_u(pts, ws, 1.05)
		var passes: Array = g["passes"]
		for i in n:
			for j in n:
				var uu := u[i * n + j]
				if uu >= 1.0:
					continue
				var k := 1.0 - smoothstep(0.25, 1.0, uu)
				var p := Vector2(-extent + i, -extent + j)
				k *= 0.75 + 0.45 * (n_detail.get_noise_2d(p.x * 0.5, p.y * 0.5) * 0.5 + 0.5)
				for q in passes:
					k *= smoothstep(0.3, 1.0, p.distance_to(q) / 15.0)
				heights[i * n + j] += k * float(g["h"])
	for l in spec["lakes"]:
		_lake(l[0], l[1])
	# реки с бродами
	wet_grid.resize(n * n)
	wet_grid.fill(40.0)
	for r in spec["rivers"]:
		var pts: PackedVector2Array = r["pts"]
		var ws: PackedFloat32Array = r["w"]
		var u := _raster_u(pts, ws, 2.4)
		var ford := PackedFloat32Array()
		ford.resize(n * n)
		ford.fill(0.0)
		for fc in r["fords"]:
			var i0 := maxi(int(floor(fc.x - 9.0 + extent)), 0)
			var i1 := mini(int(ceil(fc.x + 9.0 + extent)), n - 1)
			var j0 := maxi(int(floor(fc.y - 9.0 + extent)), 0)
			var j1 := mini(int(ceil(fc.y + 9.0 + extent)), n - 1)
			for i in range(i0, i1 + 1):
				for j in range(j0, j1 + 1):
					var d := Vector2(-extent + i, -extent + j).distance_to(fc) / 9.0
					ford[i * n + j] = maxf(ford[i * n + j], 1.0 - smoothstep(0.5, 1.0, d))
		for i in n:
			for j in n:
				var uu := u[i * n + j]
				if uu >= 2.4:
					continue
				var t := 1.0 - smoothstep(0.55, 2.4, uu)
				var bed := lerpf(WATER_Y - 0.95, WATER_Y - 0.28, ford[i * n + j])
				heights[i * n + j] = lerpf(heights[i * n + j], bed, t)
		for fc in r["fords"]:
			_carve_ford(fc, pts, ws)
		_raster_gap(wet_grid, pts, ws, 40.0)
	for l in spec["lakes"]:
		_lake_gap(l[0], l[1])
	# поляны под лагеря и сундуки
	for f in flats:
		var c: Vector2 = f[0]
		var r: float = f[1]
		var rr := r * 1.8
		var i0 := maxi(int(floor(c.x - rr + extent)), 0)
		var i1 := mini(int(ceil(c.x + rr + extent)), n - 1)
		var j0 := maxi(int(floor(c.y - rr + extent)), 0)
		var j1 := mini(int(ceil(c.y + rr + extent)), n - 1)
		for i in range(i0, i1 + 1):
			for j in range(j0, j1 + 1):
				var d := Vector2(-extent + i, -extent + j).distance_to(c)
				if d < rr:
					heights[i * n + j] = lerpf(heights[i * n + j], f[2], 1.0 - smoothstep(r, rr, d))
	# дороги
	road_grid.resize(n * n)
	road_grid.fill(12.0)
	for rd in spec["roads"]:
		_raster_gap(road_grid, PackedVector2Array([rd[0], rd[1]]), PackedFloat32Array([0.0, 0.0]), 12.0)


## Брод: поперёк реки прокладывается пологий спуск с мелким дном посередине, чтобы переправа всегда была проходима.
func _carve_ford(fc: Vector2, pts: PackedVector2Array, ws: PackedFloat32Array) -> void:
	var n := grid_n
	var bi := 0
	var bd := 1e9
	for i in pts.size() - 1:
		var d := MapGen.seg_dist(fc, pts[i], pts[i + 1])
		if d < bd:
			bd = d; bi = i
	var dir := (pts[bi + 1] - pts[bi]).normalized()
	var w := ws[bi]
	var nrm := Vector2(-dir.y, dir.x)
	var L := w * 3.8
	var hA := height(fc.x - nrm.x * L, fc.y - nrm.y * L)
	var hB := height(fc.x + nrm.x * L, fc.y + nrm.y * L)
	var i0 := maxi(int(floor(fc.x - L - 8.0 + extent)), 0)
	var i1 := mini(int(ceil(fc.x + L + 8.0 + extent)), n - 1)
	var j0 := maxi(int(floor(fc.y - L - 8.0 + extent)), 0)
	var j1 := mini(int(ceil(fc.y + L + 8.0 + extent)), n - 1)
	for i in range(i0, i1 + 1):
		for j in range(j0, j1 + 1):
			var rel := Vector2(-extent + i, -extent + j) - fc
			var x := rel.dot(nrm)
			var lat := absf(rel.dot(dir))
			if absf(x) > L or lat > 7.0:
				continue
			var he := hA if x < 0.0 else hB
			var ax := absf(x)
			var target := WATER_Y - 0.28
			if ax > w * 0.8:
				target = lerpf(WATER_Y - 0.28, he, (ax - w * 0.8) / (L - w * 0.8))
			var k := 1.0 - smoothstep(3.0, 7.0, lat)
			heights[i * n + j] = lerpf(heights[i * n + j], target, k)


func _lake(c: Vector2, r: float) -> void:
	var n := grid_n
	var rr := r * 1.3
	var i0 := maxi(int(floor(c.x - rr + extent)), 0)
	var i1 := mini(int(ceil(c.x + rr + extent)), n - 1)
	var j0 := maxi(int(floor(c.y - rr + extent)), 0)
	var j1 := mini(int(ceil(c.y + rr + extent)), n - 1)
	for i in range(i0, i1 + 1):
		for j in range(j0, j1 + 1):
			var d := Vector2(-extent + i, -extent + j).distance_to(c) / r
			if d < 1.3:
				heights[i * n + j] = lerpf(heights[i * n + j], WATER_Y - 1.4, 1.0 - smoothstep(0.55, 1.25, d))


func _lake_gap(c: Vector2, r: float) -> void:
	var n := grid_n
	var rr := r + 40.0
	var i0 := maxi(int(floor(c.x - rr + extent)), 0)
	var i1 := mini(int(ceil(c.x + rr + extent)), n - 1)
	var j0 := maxi(int(floor(c.y - rr + extent)), 0)
	var j1 := mini(int(ceil(c.y + rr + extent)), n - 1)
	for i in range(i0, i1 + 1):
		for j in range(j0, j1 + 1):
			var d := Vector2(-extent + i, -extent + j).distance_to(c) - r * 0.9
			if d < wet_grid[i * n + j]:
				wet_grid[i * n + j] = d


## Высота по сетке: те же треугольники, что и в меше (диагональ b–c).
func height(x: float, z: float) -> float:
	var n := grid_n
	var fx := (x + extent) / STEP
	var fz := (z + extent) / STEP
	var i := clampi(int(floor(fx)), 0, n - 2)
	var j := clampi(int(floor(fz)), 0, n - 2)
	var tx := clampf(fx - i, 0.0, 1.0)
	var tz := clampf(fz - j, 0.0, 1.0)
	var h00 := heights[i * n + j]
	var h10 := heights[(i + 1) * n + j]
	var h01 := heights[i * n + j + 1]
	if tx + tz <= 1.0:
		return h00 + (h10 - h00) * tx + (h01 - h00) * tz
	var h11 := heights[(i + 1) * n + j + 1]
	return h11 + (h01 - h11) * (1.0 - tx) + (h10 - h11) * (1.0 - tz)


func normal_at(x: float, z: float) -> Vector3:
	var e := 0.8
	var dx := height(x + e, z) - height(x - e, z)
	var dz := height(x, z + e) - height(x, z - e)
	return Vector3(-dx, 2.0 * e, -dz).normalized()


func slope(x: float, z: float) -> float:
	return 1.0 - normal_at(x, z).y


func _cell(x: float, z: float) -> int:
	var i := clampi(roundi(x + extent), 0, grid_n - 1)
	var j := clampi(roundi(z + extent), 0, grid_n - 1)
	return i * grid_n + j


func road_dist(x: float, z: float) -> float:
	return road_grid[_cell(x, z)]


## Расстояние до ближайшей воды (реки и озёра), м; на воде меньше нуля.
func river_dist(x: float, z: float) -> float:
	return wet_grid[_cell(x, z)]


func is_deep(x: float, z: float) -> bool:
	return height(x, z) < WATER_Y - 0.25


func is_water(x: float, z: float) -> bool:
	return height(x, z) < WATER_Y + 0.05


## Цвет вершины: трава с пятнами, берег, дорога, вытоптанные поляны, скалы и снег по склонам.
func _color(x: float, z: float, h: float, nrm: Vector3) -> Color:
	var nv := n_color.get_noise_2d(x, z) * 0.5 + 0.5
	var c: Color = (pal["a"] as Color).lerp(pal["b"], nv)
	var dry := smoothstep(0.62, 0.9, nv)
	c = c.lerp(pal["dry"], dry * 0.7)
	if h < WATER_Y + 0.7:
		c = c.lerp(pal["shore"], smoothstep(WATER_Y + 0.7, WATER_Y + 0.05, h))
	if h < WATER_Y - 0.05:
		c = c.lerp(pal["bed"], 0.6)
	for f in flats:
		var d := Vector2(x - f[0].x, z - f[0].y).length()
		if d < f[1] * 0.95:
			c = c.lerp(pal["flat"], (1.0 - smoothstep(f[1] * 0.35, f[1] * 0.95, d)) * 0.8)
	var rd := road_dist(x, z)
	if rd < 2.6:
		c = c.lerp(pal["road"], 1.0 - smoothstep(1.2, 2.6, rd))
	var sl := 1.0 - nrm.y
	var rock := smoothstep(0.16, 0.34, sl)
	c = c.lerp(pal["rock"], rock)
	c = c.lerp(pal["peak"], smoothstep(9.0, 16.0, h))
	return c


## Карта сверху одной картинкой (миникарта и предпросмотр генерации): цвета земли, вода, мягкая светотень.
func render_map(px: int) -> Image:
	var img := Image.create(px, px, false, Image.FORMAT_RGB8)
	var water: Color = spec["b"]["water"]
	var light := Vector3(-0.5, 0.8, -0.3).normalized()
	for y in px:
		for x in px:
			var wx := -field + (x + 0.5) / px * 2.0 * field
			var wz := -field + (y + 0.5) / px * 2.0 * field
			var h := height(wx, wz)
			var nrm := normal_at(wx, wz)
			var c := _color(wx, wz, h, nrm)
			if h < WATER_Y + 0.05:
				c = water.lerp(Color(0.05, 0.12, 0.18), clampf(-h / 1.4, 0.0, 0.7)).lightened(0.12)
			var sh := clampf(0.8 + (nrm.dot(light) - 0.8) * 1.4, 0.55, 1.15)
			img.set_pixel(x, y, Color(c.r * sh, c.g * sh, c.b * sh))
	return img


## Меш земли (вершинные цвета) и водная плоскость.
func build_nodes(parent: Node3D) -> void:
	var n := grid_n
	var verts := PackedVector3Array()
	var norms := PackedVector3Array()
	var cols := PackedColorArray()
	var uvs := PackedVector2Array()
	verts.resize(n * n); norms.resize(n * n); cols.resize(n * n); uvs.resize(n * n)
	for i in n:
		for j in n:
			var x := -extent + i * STEP
			var z := -extent + j * STEP
			var h := heights[i * n + j]
			var hl := heights[maxi(i - 1, 0) * n + j]
			var hr := heights[mini(i + 1, n - 1) * n + j]
			var hd := heights[i * n + maxi(j - 1, 0)]
			var hu := heights[i * n + mini(j + 1, n - 1)]
			var nm := Vector3(hl - hr, 2.0 * STEP, hd - hu).normalized()
			var k := i * n + j
			verts[k] = Vector3(x, h, z)
			norms[k] = nm
			cols[k] = _color(x, z, h, nm)
			uvs[k] = Vector2(x, z) / 7.0
	var idx := PackedInt32Array()
	for i in n - 1:
		for j in n - 1:
			var a := i * n + j
			var b := (i + 1) * n + j
			var c := a + 1
			var d := b + 1
			idx.append_array([a, b, c, b, d, c])
	var arr := []
	arr.resize(Mesh.ARRAY_MAX)
	arr[Mesh.ARRAY_VERTEX] = verts
	arr[Mesh.ARRAY_NORMAL] = norms
	arr[Mesh.ARRAY_COLOR] = cols
	arr[Mesh.ARRAY_TEX_UV] = uvs
	arr[Mesh.ARRAY_INDEX] = idx
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arr)
	var ground := MeshInstance3D.new()
	ground.mesh = mesh
	var noise := FastNoiseLite.new()
	noise.frequency = 0.08
	noise.fractal_octaves = 3
	var grad := Gradient.new()
	grad.colors = PackedColorArray([Color(0.72, 0.72, 0.72), Color(1.0, 1.0, 1.0)])
	var nt := NoiseTexture2D.new()
	nt.noise = noise
	nt.color_ramp = grad
	nt.seamless = true
	nt.width = 512; nt.height = 512
	if fog != null:
		ground.material_override = fog.ground_material(nt)
	else:
		var gm := StandardMaterial3D.new()
		gm.vertex_color_use_as_albedo = true
		gm.vertex_color_is_srgb = true
		gm.albedo_texture = nt
		gm.roughness = 1.0
		ground.material_override = gm
	parent.add_child(ground)
	# вода
	var water := MeshInstance3D.new()
	var pm := PlaneMesh.new()
	pm.size = Vector2(extent * 2.0, extent * 2.0)
	water.mesh = pm
	water.position.y = WATER_Y
	if fog != null:
		water.material_override = fog.water_material(spec["b"]["water"])
	else:
		var wm := StandardMaterial3D.new()
		wm.albedo_color = Color(0.16, 0.38, 0.5, 0.68)
		wm.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
		wm.roughness = 0.06
		wm.metallic = 0.25
		wm.cull_mode = BaseMaterial3D.CULL_DISABLED
		water.material_override = wm
	water.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	parent.add_child(water)
