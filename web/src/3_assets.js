// ------------------------------------------------------------------ модели (KayKit, Quaternius — CC0)

// Модели упакованы скриптом pack_models.py: компактный GLB в base64 + текстуры PNG рядом
const MODEL_NAMES = ['Knight', 'Barbarian', 'Rogue_Hooded', 'Horse', 'White_Horse', 'arrow',
  'tree_single_A', 'tree_single_B', 'trees_A_large', 'trees_A_medium', 'trees_B_large', 'trees_B_medium', 'trees_B_small',
  'rock_single_A', 'rock_single_B', 'rock_single_C', 'rock_single_D', 'rock_single_E',
  'castle_blue', 'castle_red', 'windmill', 'tower'];

async function loadPacked(loader, name) {
  const res = await fetch('pack/' + name + '.txt');
  if (!res.ok) throw new Error(name + ': HTTP ' + res.status);
  const bin = Uint8Array.from(atob((await res.text()).trim()), (c) => c.charCodeAt(0));
  return loader.parseAsync(bin.buffer, 'pack/');
}

/** Клетки текстуры-палитры (8x4), которые перекрашиваются в цвет команды. */
const TEAM_CELLS = {
  Knight: [[0, 1], [2, 2]],
  Barbarian: [[0, 1], [1, 1], [2, 2]],
  Rogue_Hooded: [[0, 1], [1, 1], [1, 2]],
};

const UPPER_BONES = /^(upperarm|lowerarm|wrist|hand|handslot|chest|head)/;

function imageToCanvas(img) {
  const cv = document.createElement('canvas');
  cv.width = img.width; cv.height = img.height;
  const ctx = cv.getContext('2d', { willReadFrequently: true });
  ctx.drawImage(img, 0, 0);
  return { cv, ctx };
}

function canvasTexture(cv, like) {
  const t = new THREE.CanvasTexture(cv);
  t.flipY = like.flipY;
  t.colorSpace = THREE.SRGBColorSpace;
  t.wrapS = like.wrapS; t.wrapT = like.wrapT;
  t.magFilter = like.magFilter; t.minFilter = like.minFilter;
  t.anisotropy = 4;
  return t;
}

/** Перекрашивает клетки палитры в оттенок команды, сохраняя градиент светлоты. */
function recolorCells(tex, cells, team) {
  const { cv, ctx } = imageToCanvas(tex.image);
  const cw = cv.width / 8, ch = cv.height / 4, c = new THREE.Color(), hsl = {};
  for (const [cx, cy] of cells) {
    const img = ctx.getImageData(cx * cw, cy * ch, cw, ch), d = img.data;
    for (let i = 0; i < d.length; i += 4) {
      c.setRGB(d[i] / 255, d[i + 1] / 255, d[i + 2] / 255);
      c.getHSL(hsl);
      c.setHSL(team.hue / 360, Math.max(hsl.s, team.sat), clamp(hsl.l * 0.95 + 0.03, 0.08, 0.8));
      d[i] = c.r * 255; d[i + 1] = c.g * 255; d[i + 2] = c.b * 255;
    }
    ctx.putImageData(img, cx * cw, cy * ch);
  }
  return canvasTexture(cv, tex);
}

/** Меняет цвет листвы под биом: осень, зима, степь. */
function recolorFoliage(tex, mode) {
  const { cv, ctx } = imageToCanvas(tex.image);
  const img = ctx.getImageData(0, 0, cv.width, cv.height), d = img.data, c = new THREE.Color(), hsl = {};
  for (let i = 0; i < d.length; i += 4) {
    c.setRGB(d[i] / 255, d[i + 1] / 255, d[i + 2] / 255);
    c.getHSL(hsl);
    const hue = hsl.h * 360;
    if (hue < 60 || hue > 170 || hsl.s < 0.15) continue;
    const k = (hue - 60) / 110;
    if (mode === 'autumn') c.setHSL((10 + k * 36) / 360, Math.max(hsl.s, 0.62), hsl.l * 1.05);
    else if (mode === 'winter') c.setHSL(0.58, hsl.s * 0.2, clamp(hsl.l * 0.45 + 0.52, 0, 0.95));
    else if (mode === 'steppe') c.setHSL((42 + k * 20) / 360, Math.min(0.6, hsl.s * 0.8), hsl.l * 1.08);
    d[i] = c.r * 255; d[i + 1] = c.g * 255; d[i + 2] = c.b * 255;
  }
  ctx.putImageData(img, 0, 0);
  return canvasTexture(cv, tex);
}

/** Геометрия только с нужными атрибутами (иначе слияние невозможно). */
function cleanGeometry(src, skin) {
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', src.getAttribute('position').clone());
  g.setAttribute('normal', src.getAttribute('normal').clone());
  const uv = src.getAttribute('uv');
  g.setAttribute('uv', uv ? uv.clone() : new THREE.BufferAttribute(new Float32Array(src.getAttribute('position').count * 2), 2));
  if (skin) {
    g.setAttribute('skinIndex', skin.index);
    g.setAttribute('skinWeight', skin.weight);
  }
  if (src.index) g.setIndex(src.index.clone());
  else g.setIndex([...Array(src.getAttribute('position').count).keys()]);
  return g;
}

/**
 * Готовит шаблон персонажа: оставляет нужное оружие и превращает все части
 * (тело, шлем, щит, меч) в ОДИН скиннованный меш — один draw call на солдата.
 */
function prepareCharacter(gltf, modelName, keep, team, targetHeight) {
  const root = SkeletonUtils.clone(gltf.scene);
  const drop = [];
  root.traverse((o) => { if (ALL_ATTACHMENTS.includes(o.name) && !keep.includes(o.name)) drop.push(o); });
  drop.forEach((o) => o.parent.remove(o));
  root.updateMatrixWorld(true);

  const skinned = [], rigid = [];
  root.traverse((o) => { if (o.isSkinnedMesh) skinned.push(o); else if (o.isMesh) rigid.push(o); });
  const ref = skinned[0], skel = ref.skeleton;
  const invRefBind = ref.bindMatrix.clone().invert();
  const boneIndex = new Map(skel.bones.map((b, i) => [b, i]));
  const geos = [];

  for (const part of skinned) {
    const si = part.geometry.getAttribute('skinIndex'), sw = part.geometry.getAttribute('skinWeight');
    const idx = new Uint16Array(si.count * 4), w = new Float32Array(sw.count * 4);
    for (let v = 0; v < si.count; v++) {
      for (let k = 0; k < 4; k++) {
        const b = part.skeleton.bones[si.getComponent(v, k)];
        idx[v * 4 + k] = boneIndex.has(b) ? boneIndex.get(b) : 0;
        w[v * 4 + k] = sw.getComponent(v, k);
      }
    }
    const g = cleanGeometry(part.geometry, { index: new THREE.BufferAttribute(idx, 4), weight: new THREE.BufferAttribute(w, 4) });
    g.applyMatrix4(invRefBind.clone().multiply(part.bindMatrix));
    geos.push(g);
  }

  for (const mesh of rigid) {
    let bone = mesh.parent;
    while (bone && !(bone.isBone && boneIndex.has(bone))) bone = bone.parent;
    if (!bone) continue;
    const bi = boneIndex.get(bone), n = mesh.geometry.getAttribute('position').count;
    const idx = new Uint16Array(n * 4), w = new Float32Array(n * 4);
    for (let v = 0; v < n; v++) { idx[v * 4] = bi; w[v * 4] = 1; }
    const g = cleanGeometry(mesh.geometry, { index: new THREE.BufferAttribute(idx, 4), weight: new THREE.BufferAttribute(w, 4) });
    const L = bone.matrixWorld.clone().invert().multiply(mesh.matrixWorld);
    const boneBind = skel.boneInverses[bi].clone().invert();
    g.applyMatrix4(invRefBind.clone().multiply(boneBind).multiply(L));
    geos.push(g);
  }

  const merged = mergeGeometries(geos, false);
  const srcMat = ref.material;
  const mat = srcMat.clone();
  mat.map = recolorCells(srcMat.map, TEAM_CELLS[modelName], team);
  mat.roughness = 0.72; mat.metalness = 0; mat.envMapIntensity = 0.7;
  // лёгкий оттенок команды на всём солдате — так армии различимы издалека
  mat.color.set(0xffffff).lerp(new THREE.Color().setHSL(team.hue / 360, 0.7, 0.6), 0.14);

  const body = new THREE.SkinnedMesh(merged, mat);
  body.name = 'UnitBody';
  body.position.copy(ref.position); body.quaternion.copy(ref.quaternion); body.scale.copy(ref.scale);
  ref.parent.add(body);
  body.bind(skel, ref.bindMatrix);
  body.castShadow = true;
  body.receiveShadow = true;
  for (const m of [...skinned, ...rigid]) m.parent && m.parent.remove(m);

  // Масштаб: заданный рост, ступни на нуле
  merged.computeBoundingBox();
  const bb = merged.boundingBox.clone().applyMatrix4(ref.bindMatrix);
  const k = targetHeight / (bb.max.y - bb.min.y);
  const wrap = new THREE.Group();
  root.scale.setScalar(k);
  root.position.y = -bb.min.y * k;
  wrap.add(root);
  return wrap;
}

/** Оставляет в клипе только треки костей скелета (ИК-контроллеры не нужны — экономим CPU). */
function trimClip(clip, allow, filter) {
  const tracks = clip.tracks.filter((t) => {
    const node = t.name.split('.')[0];
    if (!allow.has(node) || t.name.endsWith('.scale')) return false;
    return filter ? filter(node) : true;
  });
  return new THREE.AnimationClip(clip.name, clip.duration, tracks);
}

function clipsFor(gltf) {
  let bones = null;
  gltf.scene.traverse((o) => { if (o.isSkinnedMesh && !bones) bones = new Set(o.skeleton.bones.map((b) => b.name)); });
  const map = {};
  for (const c of gltf.animations) map[c.name] = trimClip(c, bones);
  map.__bones = bones;
  return map;
}

function mergeStatic(gltf) {
  gltf.scene.updateMatrixWorld(true);
  const geos = [];
  let mat = null;
  gltf.scene.traverse((o) => {
    if (!o.isMesh) return;
    const g = cleanGeometry(o.geometry);
    g.applyMatrix4(o.matrixWorld);
    geos.push(g);
    mat = mat || o.material;
  });
  const g = mergeGeometries(geos, false);
  g.computeBoundingBox();
  const bb = g.boundingBox;
  g.translate(-(bb.min.x + bb.max.x) / 2, -bb.min.y, -(bb.min.z + bb.max.z) / 2);
  g.computeBoundingSphere();
  return { geometry: g, material: mat, height: bb.max.y - bb.min.y };
}

const ASSETS = {};

async function loadAssets(onProgress) {
  const loader = new GLTFLoader();
  let done = 0;
  const entries = await Promise.all(MODEL_NAMES.map(async (k) => {
    const g = await loadPacked(loader, k);
    onProgress(++done / MODEL_NAMES.length);
    return [k, g];
  }));
  const G = Object.fromEntries(entries);

  // Солдаты: для каждого типа и каждой команды свой шаблон
  ASSETS.units = TYPES.map((t) => {
    const keep = t.keep;
    return TEAM.map((team) => prepareCharacter(G[t.model], t.model, keep, team, t.mount ? 1.75 : 1.9));
  });
  // Полководец (рыцарь с двуручным мечом) и гонец (разбойник в плаще) — тоже всадники
  ASSETS.units[T_CMD] = TEAM.map((team) => prepareCharacter(G.Knight, 'Knight', COMMANDER.keep, team, 1.85));
  ASSETS.units[T_MSG] = TEAM.map((team) => prepareCharacter(G.Rogue_Hooded, 'Rogue_Hooded', MESSENGER.keep, team, 1.7));
  ASSETS.clips = { Knight: clipsFor(G.Knight), Barbarian: clipsFor(G.Barbarian), Rogue_Hooded: clipsFor(G.Rogue_Hooded) };

  // Анимации всадника: ноги сидят, руки рубят
  const kc = G.Knight.animations, kb = ASSETS.clips.Knight.__bones;
  const find = (name) => kc.find((c) => c.name === name);
  ASSETS.rider = {
    sit: trimClip(find('Sit_Chair_Idle'), kb, (n) => !UPPER_BONES.test(n)),
    arms: trimClip(find('Idle'), kb, (n) => UPPER_BONES.test(n)),
    attack: TYPES[3].anim.attack.map((a) => trimClip(find(a), kb, (n) => UPPER_BONES.test(n))),
  };

  // Кони: нормализуем размер, ищем седло
  ASSETS.horses = ['Horse', 'White_Horse'].map((name) => {
    const g = G[name];
    const root = SkeletonUtils.clone(g.scene);
    root.updateMatrixWorld(true);
    const bb = new THREE.Box3().setFromObject(root);
    const size = bb.getSize(new THREE.Vector3());
    const len = Math.max(size.x, size.z);
    const k = 2.9 / len;
    root.scale.multiplyScalar(k);
    root.position.y = -bb.min.y * k;
    root.traverse((o) => { if (o.isMesh) { o.castShadow = true; o.receiveShadow = true; if (o.material) o.material.envMapIntensity = 0.6; } });
    const wrap = new THREE.Group();
    wrap.add(root);
    wrap.updateMatrixWorld(true);
    const bb2 = new THREE.Box3().setFromObject(wrap);
    const clips = {};
    for (const c of g.animations) if (!c.name.includes('|')) clips[c.name] = c;
    return { template: wrap, clips, saddle: new THREE.Vector3(0, bb2.max.y * 0.8, (bb2.min.z + bb2.max.z) / 2 - 0.1), forwardX: size.x > size.z };
  });

  // Высота бёдер всадника в позе сидя (чтобы посадить его в седло)
  {
    const probe = SkeletonUtils.clone(ASSETS.units[3][0]);
    const mixer = new THREE.AnimationMixer(probe);
    mixer.clipAction(ASSETS.rider.sit).play();
    mixer.update(0);
    probe.updateMatrixWorld(true);
    let hipsY = 0.8;
    probe.traverse((o) => { if (o.name === 'hips') hipsY = o.getWorldPosition(new THREE.Vector3()).y; });
    ASSETS.riderHipsY = hipsY;
  }

  // Толпа: запечённые анимации + инстансинг (тысячи солдат без отдельных скелетов)
  const cap = MAX_UNITS + 64, K = ASSETS.clips;
  const infantryDefs = (t) => {
    const a = t.anim, clips = K[t.model], defs = [], seen = new Set();
    const add = (name, loop) => { if (name && !seen.has(name) && clips[name]) { seen.add(name); defs.push({ name, clips: [clips[name]], loop }); } };
    [a.idle, a.run, 'Walking_A', a.cheer, a.aim, a.reload].forEach((n) => add(n, true));
    [...a.attack, a.melee, 'Death_A', 'Death_B'].forEach((n) => add(n, false));
    return defs;
  };
  const R = ASSETS.rider;
  const riderDefs = [
    { name: 'ride', clips: [R.arms, R.sit] },
    ...R.attack.map((c, i) => ({ name: 'atk' + i, clips: [c, R.sit], loop: false })),
    { name: 'Death_A', clips: [K.Knight.Death_A], loop: false },
    { name: 'Death_B', clips: [K.Knight.Death_B], loop: false },
  ];
  ASSETS.crowd = {
    inf: [0, 1, 2].map((i) => new CrowdModel(ASSETS.units[i], infantryDefs(TYPES[i]), cap)),
    rider: { 3: new CrowdModel(ASSETS.units[3], riderDefs, cap), [T_CMD]: new CrowdModel(ASSETS.units[T_CMD], riderDefs, 24), [T_MSG]: new CrowdModel(ASSETS.units[T_MSG], riderDefs, 96) },
    horse: ASSETS.horses.map((h) => new CrowdModel([h.template], ['Idle', 'Walk', 'Gallop', 'Death'].map((n) => ({ name: n, clips: [h.clips[n]], loop: n !== 'Death' })), cap)),
  };
  ASSETS.crowdList = [...ASSETS.crowd.inf, ...Object.values(ASSETS.crowd.rider), ...ASSETS.crowd.horse];

  // Болт для арбалета
  const arrow = mergeStatic(G.arrow);
  ASSETS.bolt = { geometry: arrow.geometry, material: arrow.material, norm: 0.95 / Math.max(arrow.height, 0.01) };
  arrow.geometry.computeBoundingBox();
  const ab = arrow.geometry.boundingBox.getSize(new THREE.Vector3());
  ASSETS.bolt.axis = ab.x > ab.y && ab.x > ab.z ? 'x' : ab.z > ab.y ? 'z' : 'y';
  ASSETS.bolt.norm = 0.95 / Math.max(ab.x, ab.y, ab.z);

  // Природа и постройки
  const treeTargets = { tree_single_A: 6.5, tree_single_B: 7, trees_A_large: 9, trees_A_medium: 7.5, trees_B_large: 9, trees_B_medium: 7.5, trees_B_small: 6 };
  ASSETS.trees = Object.entries(treeTargets).map(([k, h]) => { const m = mergeStatic(G[k]); return { geometry: m.geometry, norm: h / m.height, material: m.material }; });
  ASSETS.rocks = ['rock_single_A', 'rock_single_B', 'rock_single_C', 'rock_single_D', 'rock_single_E'].map((k) => { const m = mergeStatic(G[k]); return { geometry: m.geometry, norm: 1 / m.height }; });
  ASSETS.worldMat = ASSETS.trees[0].material;
  ASSETS.worldMat.roughness = 0.9;
  ASSETS.rockMat = ASSETS.worldMat.clone();
  ASSETS.rockMat.color.set(0x8f877c);
  const foliage = {};
  ASSETS.treeMaterial = (style) => {
    const key = style.leafShift || 'summer';
    if (!foliage[key]) {
      const m = ASSETS.worldMat.clone();
      if (style.leafShift) m.map = recolorFoliage(ASSETS.worldMat.map, style.leafShift);
      foliage[key] = m;
    }
    return foliage[key];
  };
  ASSETS.buildings = {};
  for (const k of ['castle_blue', 'castle_red', 'windmill', 'tower']) {
    const sc = G[k].scene;
    const bb = new THREE.Box3().setFromObject(sc);
    sc.position.y -= bb.min.y;
    const wrap = new THREE.Group(); wrap.add(sc);
    ASSETS.buildings[k] = { scene: wrap, height: bb.max.y - bb.min.y };
  }
}

/** Знамя полководца: древко, полотнище цвета армии и золотое навершие. */
function makeBanner(team) {
  const g = new THREE.Group();
  const pole = new THREE.Mesh(new THREE.CylinderGeometry(0.035, 0.045, 3.4, 6), new THREE.MeshStandardMaterial({ color: 0x5b3a22, roughness: 0.8 }));
  pole.position.y = 1.7;
  const flagGeo = new THREE.PlaneGeometry(1.25, 0.85, 8, 1);
  flagGeo.translate(0.625, 0, 0);
  const flag = new THREE.Mesh(flagGeo, new THREE.MeshStandardMaterial({ color: TEAM[team].css, roughness: 0.7, side: THREE.DoubleSide }));
  flag.position.y = 2.95;
  const tip = new THREE.Mesh(new THREE.OctahedronGeometry(0.11), new THREE.MeshStandardMaterial({ color: 0xe8c15a, metalness: 0.6, roughness: 0.35 }));
  tip.position.y = 3.45;
  for (const m of [pole, flag, tip]) { m.castShadow = true; g.add(m); }
  g.userData.flag = flag;
  g.userData.base = flagGeo.attributes.position.array.slice();
  return g;
}

/** Колышем полотнище волной (по оси X полотнища), сила — от ветра. */
function waveBanner(banner, time, wind) {
  const flag = banner.userData.flag, pos = flag.geometry.attributes.position, base = banner.userData.base;
  const amp = 0.06 + Math.min(wind, 7) * 0.02;
  for (let i = 0; i < pos.count; i++) {
    const x = base[i * 3];
    pos.array[i * 3 + 2] = Math.sin(x * 4 - time * 5) * amp * x;
  }
  pos.needsUpdate = true;
  flag.geometry.computeVertexNormals();
}
