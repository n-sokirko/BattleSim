class_name Fog
extends RefCounted
## Туман войны: не разведано — чёрное, разведано, но не в обзоре — затемнено, в обзоре — как есть.
## Обзор считается лучами от каждого бойца: холм перекрывает вид, с высоты видно дальше за гребни.
## Враги видны только в обзоре; декор и сундуки — только на разведанной земле.

const UPDATE_EVERY := 0.2
const EYE := 1.7
const GROUND_SHADER := """
shader_type spatial;
render_mode diffuse_burley, specular_disabled;
uniform sampler2D detail : source_color, repeat_enable, filter_linear_mipmap;
uniform sampler2D vis_tex : filter_linear, repeat_disable;
uniform sampler2D exp_tex : filter_linear, repeat_disable;
uniform vec2 org;
uniform float span;
varying vec3 wpos;
float soft(sampler2D t, vec2 uv) {
	vec2 px = vec2(1.0) / vec2(textureSize(t, 0));
	float a = texture(t, uv).r * 2.0;
	a += texture(t, uv + vec2(px.x, 0.0) * 1.2).r + texture(t, uv - vec2(px.x, 0.0) * 1.2).r;
	a += texture(t, uv + vec2(0.0, px.y) * 1.2).r + texture(t, uv - vec2(0.0, px.y) * 1.2).r;
	return a / 6.0;
}
void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}
void fragment() {
	vec3 c = COLOR.rgb;
	vec3 lin = mix(pow((c + 0.055) / 1.055, vec3(2.4)), c / 12.92, step(c, vec3(0.04045)));
	vec2 fuv = (wpos.xz - org + 0.5) / span;
	float v = soft(vis_tex, fuv);
	float e = soft(exp_tex, fuv);
	ALBEDO = lin * texture(detail, UV).rgb * e * mix(0.42, 1.0, v);
	ROUGHNESS = 1.0;
}
"""
const WATER_SHADER := """
shader_type spatial;
render_mode blend_mix, cull_disabled, specular_schlick_ggx;
uniform sampler2D vis_tex : filter_linear, repeat_disable;
uniform sampler2D exp_tex : filter_linear, repeat_disable;
uniform vec2 org;
uniform float span;
uniform vec3 wcol : source_color = vec3(0.16, 0.38, 0.5);
varying vec3 wpos;
float soft(sampler2D t, vec2 uv) {
	vec2 px = vec2(1.0) / vec2(textureSize(t, 0));
	float a = texture(t, uv).r * 2.0;
	a += texture(t, uv + vec2(px.x, 0.0) * 1.2).r + texture(t, uv - vec2(px.x, 0.0) * 1.2).r;
	a += texture(t, uv + vec2(0.0, px.y) * 1.2).r + texture(t, uv - vec2(0.0, px.y) * 1.2).r;
	return a / 6.0;
}
void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}
void fragment() {
	vec2 fuv = (wpos.xz - org + 0.5) / span;
	float v = soft(vis_tex, fuv);
	float e = soft(exp_tex, fuv);
	ALBEDO = wcol * mix(0.42, 1.0, v);
	ALPHA = 0.68 * e;
	ROUGHNESS = 0.06;
	METALLIC = 0.25;
}
"""

var game: Node3D
var terrain: Terrain
var n := 0
var origin := 0.0
var vis := PackedByteArray()
var expl := PackedByteArray()
var tex_v: ImageTexture
var tex_e: ImageTexture
var props: Array = []        # [Node3D, индекс ячейки]
var t := 0.0
var all_visible := false     # проверка карты: всё открыто


func setup(g: Node3D, tr: Terrain) -> void:
	game = g
	terrain = tr
	n = int(tr.extent * 2.0 / Terrain.STEP) + 1
	origin = -tr.extent
	vis.resize(n * n); vis.fill(0)
	expl.resize(n * n); expl.fill(0)
	tex_v = ImageTexture.create_from_image(Image.create_from_data(n, n, false, Image.FORMAT_R8, vis))
	tex_e = ImageTexture.create_from_image(Image.create_from_data(n, n, false, Image.FORMAT_R8, expl))


func _material(code: String) -> ShaderMaterial:
	var sh := Shader.new()
	sh.code = code
	var m := ShaderMaterial.new()
	m.shader = sh
	m.set_shader_parameter("vis_tex", tex_v)
	m.set_shader_parameter("exp_tex", tex_e)
	m.set_shader_parameter("org", Vector2(origin, origin))
	m.set_shader_parameter("span", float(n))
	return m


func ground_material(detail: Texture2D) -> ShaderMaterial:
	var m := _material(GROUND_SHADER)
	m.set_shader_parameter("detail", detail)
	return m


func water_material(col: Color) -> ShaderMaterial:
	var m := _material(WATER_SHADER)
	m.set_shader_parameter("wcol", col)
	return m


func _idx(p: Vector3) -> int:
	var i := clampi(roundi(p.x - origin), 0, n - 1)
	var j := clampi(roundi(p.z - origin), 0, n - 1)
	return j * n + i


func register(node: Node3D, p: Vector3) -> void:
	node.visible = false
	props.append([node, _idx(p)])


func seen(p: Vector3) -> bool:
	return vis[_idx(p)] > 0


func explored(p: Vector3) -> bool:
	return expl[_idx(p)] > 100


func vision_of(u: Unit) -> float:
	if u == game.hero:
		return 17.0
	return 13.0 if u.ranged else 12.0


func _see(u: Unit) -> void:
	var rad := vision_of(u)
	var cx := clampi(roundi(u.global_position.x - origin), 0, n - 1)
	var cz := clampi(roundi(u.global_position.z - origin), 0, n - 1)
	var heights := terrain.heights
	var eye := u.global_position.y + EYE
	for i in range(maxi(cx - 2, 0), mini(cx + 3, n)):
		for j in range(maxi(cz - 2, 0), mini(cz + 3, n)):
			vis[j * n + i] = 255
			expl[j * n + i] = 255
	var rays := int(TAU * rad * 1.25)
	var steps := int(rad)
	for k in rays:
		var a := TAU * k / rays
		var dx := cos(a)
		var dz := sin(a)
		var max_s := -1e9
		for s in range(1, steps + 1):
			var i := roundi(cx + dx * s)
			var j := roundi(cz + dz * s)
			if i < 0 or j < 0 or i >= n or j >= n:
				break
			var h := heights[i * n + j]
			if (h + 0.6 - eye) / s >= max_s:
				vis[j * n + i] = 255
				expl[j * n + i] = 255
			var g := (h - eye) / s
			if g > max_s:
				max_s = g


## Затемнение для миникарты: не разведано — почти чёрное, разведано — полупрозрачное, в обзоре — прозрачное.
func minimap_overlay(px: int) -> Image:
	var img := Image.create(px, px, false, Image.FORMAT_RGBA8)
	var span := float(n)
	for y in px:
		var j := clampi(int((y + 0.5) / px * span), 0, n - 1)
		for x in px:
			var i := clampi(int((x + 0.5) / px * span), 0, n - 1)
			var k := j * n + i
			var a := 0.0
			if expl[k] < 100:
				a = 0.92
			elif vis[k] == 0:
				a = 0.38
			img.set_pixel(x, y, Color(0.02, 0.03, 0.05, a))
	return img


func update(dt: float, force := false) -> void:
	t -= dt
	if t > 0.0 and not force:
		return
	t = UPDATE_EVERY
	vis.fill(0)
	if all_visible:
		vis.fill(255)
		expl.fill(255)
	else:
		for u in game.units:
			if u.team == 0 and u.state != Unit.S.DEAD:
				_see(u)
	tex_v.update(Image.create_from_data(n, n, false, Image.FORMAT_R8, vis))
	tex_e.update(Image.create_from_data(n, n, false, Image.FORMAT_R8, expl))
	for pr in props:
		var on: bool = expl[pr[1]] > 100
		if pr[0].visible != on:
			pr[0].visible = on
	for u in game.units:
		if u.team == 1:
			u.visible = vis[_idx(u.global_position)] > 0
	for c in game.chests:
		if not c["opened"]:
			c["node"].visible = expl[_idx(c["pos"])] > 100
