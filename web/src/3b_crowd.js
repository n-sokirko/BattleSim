// ------------------------------------------------------------------ толпа: запечённая анимация + инстансинг + LOD
//
// Чтобы на поле помещались тысячи солдат, у каждого нет своего скелета и своего
// AnimationMixer. Позы костей всех анимаций заранее «запекаются» в текстуру
// (строка = кадр, 4 текселя = матрица кости), а скиннинг считает видеокарта.
// Все солдаты одного вида рисуются одним InstancedMesh; дальние — упрощённой моделью.

const BAKE_FPS = 30;
const LOD_DIST = LOW_END ? 38 : 55;

/** Упрощение меша кластеризацией вершин (для дальнего LOD). Цвета из разных клеток палитры не смешиваются. */
function decimateSkinned(src, cell) {
  const pos = src.getAttribute('position'), nor = src.getAttribute('normal'), uv = src.getAttribute('uv');
  const si = src.getAttribute('skinIndex'), sw = src.getAttribute('skinWeight'), col = src.getAttribute('color');
  const n = pos.count, remap = new Int32Array(n), keys = new Map();
  const P = [], N = [], U = [], SI = [], SW = [], CO = [];
  for (let i = 0; i < n; i++) {
    const x = pos.getX(i), y = pos.getY(i), z = pos.getZ(i);
    let swatch = '';
    if (uv) swatch = Math.floor(uv.getX(i) * 8) + ':' + Math.floor(uv.getY(i) * 4);
    else if (col) swatch = Math.round(col.getX(i) * 20) + ':' + Math.round(col.getY(i) * 20) + ':' + Math.round(col.getZ(i) * 20);
    const key = Math.floor(x / cell) + ',' + Math.floor(y / cell) + ',' + Math.floor(z / cell) + '|' + swatch;
    let k = keys.get(key);
    if (k === undefined) {
      k = P.length / 3;
      keys.set(key, k);
      P.push(x, y, z); N.push(0, 0, 0);
      if (uv) U.push(uv.getX(i), uv.getY(i));
      if (col) CO.push(col.getX(i), col.getY(i), col.getZ(i));
      SI.push(si.getX(i), si.getY(i), si.getZ(i), si.getW(i));
      SW.push(sw.getX(i), sw.getY(i), sw.getZ(i), sw.getW(i));
    }
    N[k * 3] += nor.getX(i); N[k * 3 + 1] += nor.getY(i); N[k * 3 + 2] += nor.getZ(i);
    remap[i] = k;
  }
  const idx = src.index ? src.index.array : [...Array(n).keys()], out = [];
  for (let t = 0; t < idx.length; t += 3) {
    const a = remap[idx[t]], b = remap[idx[t + 1]], c = remap[idx[t + 2]];
    if (a !== b && b !== c && a !== c) out.push(a, b, c);
  }
  for (let k = 0; k < N.length; k += 3) { const l = Math.hypot(N[k], N[k + 1], N[k + 2]) || 1; N[k] /= l; N[k + 1] /= l; N[k + 2] /= l; }
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(P, 3));
  g.setAttribute('normal', new THREE.Float32BufferAttribute(N, 3));
  if (uv) g.setAttribute('uv', new THREE.Float32BufferAttribute(U, 2));
  if (col) g.setAttribute('color', new THREE.Float32BufferAttribute(CO, 3));
  g.setAttribute('skinIndex', new THREE.Uint16BufferAttribute(SI, 4));
  g.setAttribute('skinWeight', new THREE.Float32BufferAttribute(SW, 4));
  g.setIndex(out);
  return g;
}

/** Подменяет скиннинг в шейдере материала: матрицы костей берутся из запечённой текстуры. */
function injectBake(material, tex, depth) {
  material.onBeforeCompile = (sh) => {
    sh.uniforms.bakeTex = { value: tex };
    sh.vertexShader = sh.vertexShader.replace('#include <common>', `#include <common>
      uniform highp sampler2D bakeTex;
      attribute vec4 skinIndex;
      attribute vec4 skinWeight;
      attribute vec2 aFrame;
      attribute float aBlend;
      mat4 bakeBone(float row, float b) {
        ivec2 p = ivec2(int(b) * 4, int(row));
        return mat4(texelFetch(bakeTex, p, 0), texelFetch(bakeTex, p + ivec2(1, 0), 0),
                    texelFetch(bakeTex, p + ivec2(2, 0), 0), texelFetch(bakeTex, p + ivec2(3, 0), 0));
      }
      mat4 bakeSkin(float row) {
        return skinWeight.x * bakeBone(row, skinIndex.x) + skinWeight.y * bakeBone(row, skinIndex.y)
             + skinWeight.z * bakeBone(row, skinIndex.z) + skinWeight.w * bakeBone(row, skinIndex.w);
      }
      mat4 bakeMatrix() {
        mat4 m = bakeSkin(aFrame.x);
        if (aBlend > 0.001) m = m * (1.0 - aBlend) + bakeSkin(aFrame.y) * aBlend;
        return m;
      }`);
    if (depth) {
      sh.vertexShader = sh.vertexShader.replace('#include <begin_vertex>', 'mat4 bakeM = bakeMatrix();\nvec3 transformed = (bakeM * vec4(position, 1.0)).xyz;');
    } else {
      sh.vertexShader = sh.vertexShader
        .replace('#include <beginnormal_vertex>', 'mat4 bakeM = bakeMatrix();\nvec3 objectNormal = normalize(mat3(bakeM) * normal);')
        .replace('#include <begin_vertex>', 'vec3 transformed = (bakeM * vec4(position, 1.0)).xyz;');
    }
  };
  material.customProgramCacheKey = () => (depth ? 'bake-depth' : 'bake-color');
}

/**
 * Модель толпы: запечённые анимации одного вида солдат/коней и инстанс-меши по командам и LOD.
 * templates — шаблоны по командам (для коней — один), clipDefs — [{ name, clips: [clip, ...слои], loop }].
 */
class CrowdModel {
  constructor(templates, clipDefs, cap) {
    const tpl = templates[0];
    const obj = SkeletonUtils.clone(tpl);
    obj.updateMatrixWorld(true);
    let sm = null;
    obj.traverse((o) => { if (o.isSkinnedMesh && !sm) sm = o; });
    this.bake(obj, sm, clipDefs);

    const base0 = this.cleanBase(sm.geometry, sm.material);
    // Шаг упрощения — от размера самой геометрии (у коня она в 100 раз меньше, чем у солдата)
    base0.computeBoundingBox();
    const diag = base0.boundingBox.getSize(new THREE.Vector3()).length();
    const base1 = decimateSkinned(base0, diag * 0.034);
    this.lodGeo = [base0, base1];
    this.verts = [base0.getAttribute('position').count, base1.getAttribute('position').count];

    this.depthMat = new THREE.MeshDepthMaterial({ depthPacking: THREE.RGBADepthPacking });
    injectBake(this.depthMat, this.tex, true);
    this.meshes = templates.map((t) => {
      let m = null;
      t.traverse((o) => { if (o.isSkinnedMesh && !m) m = o; });
      const mat = this.materialFrom(m.material);
      return [0, 1].map((lod) => this.makeMesh(this.lodGeo[lod], mat, cap));
    });
    this.count = this.meshes.map(() => [0, 0]);
  }

  /** Запекаем позы: для каждого кадра — матрицы, переводящие вершину меша в пространство юнита. */
  bake(obj, sm, clipDefs) {
    const bones = sm.skeleton.bones, inv = sm.skeleton.boneInverses, nb = bones.length;
    const mixer = new THREE.AnimationMixer(obj), m = new THREE.Matrix4();
    const rows = [];
    this.clips = {};
    for (const def of clipDefs) {
      const clips = def.clips.filter(Boolean);
      if (!clips.length) continue;
      mixer.stopAllAction();
      const actions = clips.map((c) => { const a = mixer.clipAction(c); a.reset(); a.play(); return a; });
      const dur = clips[0].duration, frames = Math.max(1, Math.round(dur * BAKE_FPS));
      this.clips[def.name] = { start: rows.length, frames, loop: def.loop !== false, dur };
      for (let f = 0; f < frames; f++) {
        mixer.setTime(Math.min(f / BAKE_FPS, dur - 1e-4));
        obj.updateMatrixWorld(true);
        const row = new Float32Array(nb * 16);
        for (let b = 0; b < nb; b++) { m.multiplyMatrices(bones[b].matrixWorld, inv[b]).multiply(sm.bindMatrix); m.toArray(row, b * 16); }
        rows.push(row);
      }
      actions.forEach((a) => a.stop());
      mixer.uncacheRoot(obj);
    }
    const w = nb * 4, h = rows.length, data = new Float32Array(w * h * 4);
    rows.forEach((r, i) => data.set(r, i * nb * 16));
    this.tex = new THREE.DataTexture(data, w, h, THREE.RGBAFormat, THREE.FloatType);
    this.tex.minFilter = this.tex.magFilter = THREE.NearestFilter;
    this.tex.needsUpdate = true;
    // высота в позе покоя — для размера упрощения
    const bb = new THREE.Box3().setFromObject(obj);
    this.height = bb.max.y - bb.min.y;
  }

  /** Базовая геометрия; многоматериальные меши (кони) переводим в цвета вершин. */
  cleanBase(src, material) {
    const g = new THREE.BufferGeometry();
    for (const name of ['position', 'normal', 'uv', 'skinIndex', 'skinWeight']) {
      const a = src.getAttribute(name);
      if (a) g.setAttribute(name, a);
    }
    if (src.index) g.setIndex(src.index);
    if (Array.isArray(material)) {
      const n = src.getAttribute('position').count, col = new Float32Array(n * 3), idx = src.index ? src.index.array : null;
      for (const grp of src.groups) {
        const c = material[grp.materialIndex].color;
        for (let i = grp.start; i < grp.start + grp.count; i++) {
          const v = idx ? idx[i] : i;
          col[v * 3] = c.r; col[v * 3 + 1] = c.g; col[v * 3 + 2] = c.b;
        }
      }
      g.setAttribute('color', new THREE.BufferAttribute(col, 3));
      g.deleteAttribute('uv');
    }
    return g;
  }

  materialFrom(src) {
    let mat;
    if (Array.isArray(src)) mat = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.85, metalness: 0 });
    else { mat = src.clone(); mat.roughness = 0.72; mat.metalness = 0; }
    mat.envMapIntensity = 0.7;
    injectBake(mat, this.tex, false);
    return mat;
  }

  makeMesh(base, mat, cap) {
    const g = new THREE.BufferGeometry();
    for (const [k, a] of Object.entries(base.attributes)) g.setAttribute(k, a);
    g.setIndex(base.index);
    const frame = new THREE.InstancedBufferAttribute(new Float32Array(cap * 2), 2);
    const blend = new THREE.InstancedBufferAttribute(new Float32Array(cap), 1);
    frame.setUsage(THREE.DynamicDrawUsage); blend.setUsage(THREE.DynamicDrawUsage);
    g.setAttribute('aFrame', frame);
    g.setAttribute('aBlend', blend);
    const mesh = new THREE.InstancedMesh(g, mat, cap);
    mesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
    mesh.customDepthMaterial = this.depthMat;
    mesh.castShadow = true; mesh.receiveShadow = true;
    mesh.frustumCulled = false;
    mesh.count = 0;
    unitLayer.add(mesh);
    return mesh;
  }

  begin() { for (const c of this.count) { c[0] = 0; c[1] = 0; } }

  add(team, lod, matrix, rowA, rowB, blend) {
    const mesh = this.meshes[team][lod], i = this.count[team][lod]++;
    if (i >= mesh.instanceMatrix.count) return;
    mesh.setMatrixAt(i, matrix);
    const g = mesh.geometry, f = g.attributes.aFrame.array;
    f[i * 2] = rowA; f[i * 2 + 1] = rowB;
    g.attributes.aBlend.array[i] = blend;
  }

  end() {
    this.meshes.forEach((pair, team) => pair.forEach((mesh, lod) => {
      mesh.count = Math.min(this.count[team][lod], mesh.instanceMatrix.count);
      mesh.instanceMatrix.needsUpdate = true;
      mesh.geometry.attributes.aFrame.needsUpdate = true;
      mesh.geometry.attributes.aBlend.needsUpdate = true;
    }));
  }

  row(st) {
    const c = this.clips[st.name];
    if (!c) return 0;
    let f = Math.floor(st.t * BAKE_FPS);
    f = c.loop && !st.once ? f % c.frames : Math.min(f, c.frames - 1);
    return c.start + f;
  }
}

// ------------------------------------------------------------------ состояние анимации солдата (без Three.js-объектов)

function animState(name) { return { name, t: Math.random() * 10, speed: 1, once: false, prevRow: 0, blend: 0, lastRow: 0 }; }

function playAnim(st, name, { once = false, speed = 1, restart = false } = {}) {
  if (st.name === name && !restart) { st.speed = speed; return; }
  st.prevRow = st.lastRow; st.blend = 1;
  st.name = name; st.t = once ? 0 : Math.random() * 10; st.speed = speed; st.once = once;
}

function stepAnim(st, dt) {
  st.t += dt * st.speed;
  if (st.blend > 0) st.blend = Math.max(0, st.blend - dt / 0.14);
}
