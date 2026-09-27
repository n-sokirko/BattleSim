"""
Упаковывает модели для веб-демо:
  * GLB/glTF -> компактный GLB без лишних анимаций и неиспользуемых данных;
  * встроенные текстуры выносятся в отдельные PNG (их можно отдавать как картинки);
  * GLB кодируется в base64 (.txt), потому что хостинг демо не отдаёт .glb/.bin.
"""
import base64, json, pathlib, struct

ROOT = pathlib.Path(__file__).parent
SRC = ROOT / "models"
OUT = ROOT / "pack"
OUT.mkdir(exist_ok=True)

KEEP_ANIMS = {
    "Knight": ["Idle", "Running_A", "Walking_A", "1H_Melee_Attack_Chop", "1H_Melee_Attack_Slice_Diagonal",
               "1H_Melee_Attack_Slice_Horizontal", "Death_A", "Death_B", "Cheer", "Sit_Chair_Idle"],
    "Barbarian": ["2H_Melee_Idle", "Running_A", "Walking_A", "2H_Melee_Attack_Chop", "2H_Melee_Attack_Slice",
                  "Death_A", "Death_B", "Cheer"],
    "Rogue_Hooded": ["2H_Ranged_Aiming", "2H_Ranged_Shoot", "2H_Ranged_Reload", "Running_A", "Walking_A",
                     "1H_Melee_Attack_Stab", "Death_A", "Death_B", "Cheer"],
    "Horse": ["Idle", "Walk", "Gallop", "Death"],
    "White_Horse": ["Idle", "Walk", "Gallop", "Death"],
}

MODELS = {
    "Knight": "chars/Knight.glb", "Barbarian": "chars/Barbarian.glb", "Rogue_Hooded": "chars/Rogue_Hooded.glb",
    "Horse": "horse/Horse.glb", "White_Horse": "horse/White_Horse.glb", "arrow": "weapons/arrow.gltf",
}
for n in ["tree_single_A", "tree_single_B", "trees_A_large", "trees_A_medium", "trees_B_large", "trees_B_medium",
          "trees_B_small", "rock_single_A", "rock_single_B", "rock_single_C", "rock_single_D", "rock_single_E"]:
    MODELS[n] = f"world/{n}.gltf"
MODELS.update({"castle_blue": "world/building_castle_blue.gltf", "castle_red": "world/building_castle_red.gltf",
               "windmill": "world/building_windmill_blue.gltf", "tower": "world/building_tower_A_red.gltf"})


def read_model(path: pathlib.Path):
    data = path.read_bytes()
    if path.suffix == ".glb":
        jl = struct.unpack("<I", data[12:16])[0]
        gj = json.loads(data[20:20 + jl])
        rest = data[20 + jl:]
        binchunk = rest[8:8 + struct.unpack("<I", rest[:4])[0]] if rest else b""
        return gj, [binchunk]
    gj = json.loads(data)
    bufs = [(path.parent / b["uri"]).read_bytes() for b in gj.get("buffers", [])]
    return gj, bufs


def pack(name, rel):
    gj, bufs = read_model(SRC / rel)
    keep = KEEP_ANIMS.get(name)
    anims = gj.get("animations", [])
    if keep is not None:
        joints = {j for s in gj.get("skins", []) for j in s["joints"]}
        new = []
        for a in anims:
            if a.get("name") in keep:
                # только каналы костей скелета (ИК-контроллеры в игре не нужны)
                chans = [c for c in a["channels"] if c["target"].get("node") in joints and c["target"]["path"] != "scale"]
                used = sorted({c["sampler"] for c in chans})
                remap = {o: i for i, o in enumerate(used)}
                for c in chans:
                    c["sampler"] = remap[c["sampler"]]
                new.append({"name": a["name"], "channels": chans, "samplers": [a["samplers"][i] for i in used]})
        gj["animations"] = new
    else:
        gj.pop("animations", None)

    # Встроенные картинки -> внешние PNG
    for i, im in enumerate(gj.get("images", [])):
        if "bufferView" in im:
            bv = gj["bufferViews"][im["bufferView"]]
            raw = bufs[bv.get("buffer", 0)][bv.get("byteOffset", 0): bv.get("byteOffset", 0) + bv["byteLength"]]
            fname = f"{name}_tex{i}.png"
            (OUT / fname).write_bytes(raw)
            gj["images"][i] = {"uri": fname, "mimeType": "image/png", "name": im.get("name", fname)}
        elif "uri" in im and not im["uri"].startswith("data:"):
            src = (SRC / rel).parent / im["uri"]
            (OUT / im["uri"]).write_bytes(src.read_bytes())

    # Какие accessors реально используются
    used_acc = set()
    for m in gj.get("meshes", []):
        for p in m["primitives"]:
            used_acc.update(p["attributes"].values())
            if "indices" in p: used_acc.add(p["indices"])
            for t in p.get("targets", []): used_acc.update(t.values())
    for s in gj.get("skins", []):
        if "inverseBindMatrices" in s: used_acc.add(s["inverseBindMatrices"])
    for a in gj.get("animations", []):
        for s in a["samplers"]: used_acc.update([s["input"], s["output"]])
    acc_order = sorted(used_acc)
    acc_map = {o: i for i, o in enumerate(acc_order)}
    accessors = [gj["accessors"][o] for o in acc_order]
    assert all("sparse" not in a for a in accessors)

    # Новый BIN только из нужных bufferViews
    used_bv = sorted({a["bufferView"] for a in accessors if "bufferView" in a})
    bv_map, views, blob = {}, [], bytearray()
    for o in used_bv:
        bv = dict(gj["bufferViews"][o])
        raw = bufs[bv.get("buffer", 0)][bv.get("byteOffset", 0): bv.get("byteOffset", 0) + bv["byteLength"]]
        while len(blob) % 4: blob.append(0)
        bv.update({"buffer": 0, "byteOffset": len(blob)})
        blob += raw
        bv_map[o] = len(views)
        views.append(bv)
    while len(blob) % 4: blob.append(0)
    for a in accessors:
        if "bufferView" in a: a["bufferView"] = bv_map[a["bufferView"]]

    for m in gj.get("meshes", []):
        for p in m["primitives"]:
            p["attributes"] = {k: acc_map[v] for k, v in p["attributes"].items()}
            if "indices" in p: p["indices"] = acc_map[p["indices"]]
            p["targets"] = [{k: acc_map[v] for k, v in t.items()} for t in p.get("targets", [])] or None
            if p["targets"] is None: del p["targets"]
    for s in gj.get("skins", []):
        if "inverseBindMatrices" in s: s["inverseBindMatrices"] = acc_map[s["inverseBindMatrices"]]
    for a in gj.get("animations", []):
        for s in a["samplers"]:
            s["input"], s["output"] = acc_map[s["input"]], acc_map[s["output"]]

    gj["accessors"], gj["bufferViews"] = accessors, views
    gj["buffers"] = [{"byteLength": len(blob)}]
    if "animations" in gj and not gj["animations"]:
        del gj["animations"]

    js = json.dumps(gj, separators=(",", ":")).encode()
    js += b" " * ((4 - len(js) % 4) % 4)
    glb = struct.pack("<III", 0x46546C67, 2, 12 + 8 + len(js) + 8 + len(blob))
    glb += struct.pack("<II", len(js), 0x4E4F534A) + js + struct.pack("<II", len(blob), 0x004E4942) + bytes(blob)
    (OUT / f"{name}.txt").write_text(base64.b64encode(glb).decode(), encoding="ascii")
    return len(glb)


total = 0
for name, rel in MODELS.items():
    n = pack(name, rel)
    total += n
    print(f"{name:16s} {n / 1024:8.0f} KB")
print("GLB total:", round(total / 1e6, 2), "MB;  folder:", round(sum(f.stat().st_size for f in OUT.iterdir()) / 1e6, 2), "MB")
