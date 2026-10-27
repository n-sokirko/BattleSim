class_name PoseMod
extends SkeletonModifier3D
## Добавка к позе поверх анимации: рыцарский простой — локти согнуты, щит у груди, меч вперёд-вниз.
## Углы в градусах, ось — локальная кость (подбирается один раз под наш скелет).

var offsets := {}      # имя кости -> Basis (локальная добавка)
var strength := 0.0    # 0..1 — насколько применять сейчас (плавно)


func set_offset(bone: String, axis: Vector3, deg: float) -> void:
	offsets[bone] = Basis(axis.normalized(), deg_to_rad(deg))


func _process_modification() -> void:
	if strength <= 0.001:
		return
	var sk := get_skeleton()
	if sk == null:
		return
	for b in offsets:
		var i := sk.find_bone(b)
		if i < 0:
			continue
		var cur := sk.get_bone_pose_rotation(i)
		var add := Quaternion(offsets[b]).slerp(Quaternion.IDENTITY, 1.0 - strength)
		sk.set_bone_pose_rotation(i, cur * add)
