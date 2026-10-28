class_name Terrain
extends RefCounted
## Процедурный ландшафт карты: холмы, горный край, река с бродом, дороги, поляны под лагеря.
## height(x, z) — единственный источник правды: меш, расстановка, высота юнитов и клик по земле берут её.

const WATER_Y := 0.0
const FORD_X0 := -14.0
const FORD_X1 := 8.0
const STEP := 1.0

var field := 110.0
var extent := 143.0
var n_hills := FastNoiseLite.new()
var n_detail := FastNoiseLite.new()
var n_color := FastNoiseLite.new()
var flats: Array = []        # [Vector2 центр, радиус, высота]
var roads: Array = []        # [Vector2 a, Vector2 b]
var grid_n := 0
var heights := PackedFloat32Array()
var fog: Fog


func setup(f: float, poi_list: Array, road_list: Array) -> void:
	field = f
	extent = f * 1.3
	n_hills.seed = 5
	n_hills.frequency = 0.011
	n_hills.fractal_octaves = 3
	n_detail.seed = 9
	n_detail.frequency = 0.05
	n_color.seed = 21
	n_color.frequency = 0.03
	n_color.fractal_octaves = 3
	for p in poi_list:           # [Vector3 позиция, радиус]
		var q: Vector3 = p[0]
		flats.append([Vector2(q.x, q.z), p[1], _raw(q.x, q.z)])
	for r in road_list:
		roads.append([Vector2(r[0].x, r[0].z), Vector2(r[1].x, r[1].z)])


func _raw(x: float, z: float) -> float:
	var h := 2.4 + n_hills.get_noise_2d(x, z) * 4.2 + n_detail.get_noise_2d(x, z) * 0.45
	h = maxf(h, 0.8)
	var e := maxf(absf(x), absf(z))
	var rim := field * 0.86
	if e > rim:
		var k := (e - rim) / (field * 0.25)
		h += k * k * 26.0
	return h


func river_z(x: float) -> float:
	return -60.0 + 7.0 * sin(x * 0.045) + 3.5 * sin(x * 0.12 + 1.0)


func river_w(x: float) -> float:
	return 4.5 + 1.5 * sin(x * 0.08 + 2.0)


func _ford(x: float) -> float:
	return smoothstep(FORD_X0 - 6.0, FORD_X0, x) * (1.0 - smoothstep(FORD_X1, FORD_X1 + 6.0, x))


func height(x: float, z: float) -> float:
	var h := _raw(x, z)
	for f in flats:
		var d := Vector2(x - f[0].x, z - f[0].y).length()
		if d < f[1] * 1.8:
			h = lerpf(h, f[2], 1.0 - smoothstep(f[1], f[1] * 1.8, d))
	var dz := absf(z - river_z(x))
	var hw := river_w(x)
	var bank := hw * 2.4
	if dz < bank:
		var t := 1.0 - smoothstep(hw * 0.55, bank, dz)
		var bed := lerpf(WATER_Y - 0.95, WATER_Y - 0.28, _ford(x))
		h = lerpf(h, bed, t)
	return h


func normal_at(x: float, z: float) -> Vector3:
	var e := 0.8
	var dx := height(x + e, z) - height(x - e, z)
	var dz := height(x, z + e) - height(x, z - e)
	return Vector3(-dx, 2.0 * e, -dz).normalized()


func slope(x: float, z: float) -> float:
	return 1.0 - normal_at(x, z).y


func road_dist(x: float, z: float) -> float:
	var best := 1e9
	var p := Vector2(x, z)
	for r in roads:
		var a: Vector2 = r[0]
		var ab: Vector2 = r[1] - a
		var t := clampf((p - a).dot(ab) / maxf(ab.length_squared(), 0.001), 0.0, 1.0)
		best = minf(best, (p - (a + ab * t)).length())
	return best


func river_dist(x: float, z: float) -> float:
	return absf(z - river_z(x)) - river_w(x)


func is_deep(x: float, z: float) -> bool:
	return height(x, z) < WATER_Y - 0.25


func is_water(x: float, z: float) -> bool:
	return height(x, z) < WATER_Y + 0.05


## Цвет вершины: трава с пятнами, берег, дорога, вытоптанные поляны, скалы по склонам.
func _color(x: float, z: float, h: float, nrm: Vector3) -> Color:
	var nv := n_color.get_noise_2d(x, z) * 0.5 + 0.5
	var c := Color(0.30, 0.45, 0.18).lerp(Color(0.46, 0.55, 0.22), nv)
	var dry := smoothstep(0.62, 0.9, nv)
	c = c.lerp(Color(0.62, 0.55, 0.28), dry * 0.7)
	# берег и дно
	if h < WATER_Y + 0.7:
		c = c.lerp(Color(0.62, 0.54, 0.36), smoothstep(WATER_Y + 0.7, WATER_Y + 0.05, h))
	if h < WATER_Y - 0.05:
		c = c.lerp(Color(0.34, 0.33, 0.28), 0.6)
	# вытоптанные поляны
	for f in flats:
		var d := Vector2(x - f[0].x, z - f[0].y).length()
		if d < f[1] * 0.95:
			c = c.lerp(Color(0.52, 0.41, 0.27), (1.0 - smoothstep(f[1] * 0.35, f[1] * 0.95, d)) * 0.8)
	# дороги
	var rd := road_dist(x, z)
	if rd < 2.6:
		c = c.lerp(Color(0.56, 0.44, 0.29), 1.0 - smoothstep(1.2, 2.6, rd))
	# склоны и горы
	var sl := 1.0 - nrm.y
	var rock := smoothstep(0.16, 0.34, sl)
	c = c.lerp(Color(0.46, 0.44, 0.42), rock)
	c = c.lerp(Color(0.5, 0.48, 0.46), smoothstep(9.0, 16.0, h))
	return c


## Меш земли (вершинные цвета) и водная плоскость.
func build_nodes(parent: Node3D) -> void:
	var n := int(extent * 2.0 / STEP) + 1
	grid_n = n
	heights.resize(n * n)
	for i in n:
		var x := -extent + i * STEP
		for j in n:
			heights[i * n + j] = height(x, -extent + j * STEP)
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
		water.material_override = fog.water_material()
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
