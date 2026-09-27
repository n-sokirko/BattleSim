// ------------------------------------------------------------------ мир

const SIZE = 440, RES = 256, HALF = SIZE / 2, CELL = SIZE / RES, FIELD = 80, WATER = 0, TEX = 512;

class World {
  constructor() {
    this.h = new Float32Array((RES + 1) * (RES + 1));
    this.group = null;
    this.owned = [];
    this.sunDir = new THREE.Vector3(0, 1, 0);
  }

  generate(seed, style, assets) {
    this.dispose();
    this.style = style;
    this.group = new THREE.Group();
    scene.add(this.group);
    const rand = mulberry32(seed);
    this.rand = rand;
    this.pn = new Perlin(rand);
    const o = () => rand() * 200;
    this.o = { hx: o(), hz: o(), lx: o(), lz: o(), mx: o(), mz: o(), cx: o(), cz: o() };
    this.features = this.makeFeatures(rand);
    const wa = rand() * Math.PI * 2, ws = rand() < 0.2 ? 0 : 1 + rand() * 6;
    this.wind = new THREE.Vector2(Math.cos(wa) * ws, Math.sin(wa) * ws);

    const h = this.h;
    for (let z = 0; z <= RES; z++)
      for (let x = 0; x <= RES; x++)
        h[z * (RES + 1) + x] = this.rawHeight(x * CELL - HALF, z * CELL - HALF);
    this.analyze();

    this.buildTerrain(style);
    this.buildWater(style);
    this.applyAtmosphere(style);
    this.buildWalls();
    if (assets) this.buildDecor(style, assets);
  }

  // ---------------------------------------------------------------- тактический рельеф

  /** Холмы, овраг, гряда и развалины каменных оград — всё, за что можно воевать. */
  makeFeatures(R) {
    const f = { hills: [], ridges: [], ravine: null, walls: [] };
    for (const side of [-1, 1])
      f.hills.push({ x: lerp(-45, 45, R()), z: side * lerp(20, 42, R()), r: lerp(11, 16, R()), h: lerp(4.5, 7.5, R()) });
    if (R() < 0.65)
      f.hills.push({ x: (R() < 0.5 ? -1 : 1) * lerp(38, 60, R()), z: lerp(-10, 10, R()), r: lerp(9, 13, R()), h: lerp(3.5, 6, R()) });

    // Овраг поперёк поля между армиями
    const z0 = lerp(-10, 10, R()), tilt = lerp(-0.22, 0.22, R()), x0 = lerp(-58, -22, R()), x1 = lerp(22, 60, R()), ph = R() * 6;
    const pts = [];
    for (let i = 0; i <= 16; i++) { const x = lerp(x0, x1, i / 16); pts.push([x, z0 + x * tilt + Math.sin(x * 0.06 + ph) * 6]); }
    f.ravine = { pts, w: lerp(6.5, 9, R()), d: lerp(3.4, 4.4, R()) };

    if (R() < 0.7) {
      const s = R() < 0.5 ? -1 : 1, x = s * lerp(42, 64, R());
      f.ridges.push({ ax: x, az: lerp(-38, -8, R()), bx: x + lerp(-10, 10, R()), bz: lerp(8, 38, R()), r: 4.5, h: lerp(2.6, 3.6, R()) });
    }

    // Каменные ограды с проёмами
    const nW = 3 + ((R() * 3) | 0);
    for (let i = 0; i < nW; i++) {
      const cx = lerp(-62, 62, R()), cz = lerp(-34, 34, R());
      const ang = R() < 0.6 ? lerp(-0.35, 0.35, R()) : Math.PI / 2 + lerp(-0.35, 0.35, R());
      const len = lerp(10, 22, R()), dx = Math.cos(ang), dz = Math.sin(ang);
      let s = -len / 2;
      while (s < len / 2) {
        const e = Math.min(len / 2, s + lerp(3.5, 7, R()));
        f.walls.push({ ax: cx + dx * s, az: cz + dz * s, bx: cx + dx * e, bz: cz + dz * e, h: 1.35, t: 0.5 });
        s = e + lerp(1.7, 2.6, R());
      }
    }
    return f;
  }

  /** Карта «выпуклости» поля: где высоты, где низины. По ней думают полководцы. */
  analyze() {
    const step = 4, n = Math.floor((FIELD * 2) / step) + 1, prom = new Float32Array(n * n);
    for (let iz = 0; iz < n; iz++) for (let ix = 0; ix < n; ix++) {
      const x = -FIELD + ix * step, z = -FIELD + iz * step;
      let s = 0;
      for (let a = 0; a < 8; a++) s += this.heightAt(x + Math.cos(a * Math.PI / 4) * 14, z + Math.sin(a * Math.PI / 4) * 14);
      prom[iz * n + ix] = this.heightAt(x, z) - s / 8;
    }
    const high = [], low = [];
    for (let iz = 1; iz < n - 1; iz++) for (let ix = 1; ix < n - 1; ix++) {
      const p = prom[iz * n + ix], x = -FIELD + ix * step, z = -FIELD + iz * step, h = this.heightAt(x, z);
      if (p > 1.4) {
        let top = true;
        for (let dz = -1; dz <= 1 && top; dz++) for (let dx = -1; dx <= 1; dx++)
          if ((dx || dz) && this.heightAt(x + dx * step, z + dz * step) > h) { top = false; break; }
        if (top) high.push({ x, z, h, prom: p });
      }
      if (p < -1.0) low.push({ x, z, h, depth: -p });
    }
    high.sort((a, b) => b.prom - a.prom);
    this.an = { step, n, prom, high: high.slice(0, 12), low };
  }

  prominenceAt(x, z) {
    const a = this.an, ix = clamp(Math.round((x + FIELD) / a.step), 0, a.n - 1), iz = clamp(Math.round((z + FIELD) / a.step), 0, a.n - 1);
    return a.prom[iz * a.n + ix];
  }

  /** Верх ограды в точке (или -Infinity, если ограды нет). */
  wallTop(x, z) {
    for (const w of this.features.walls) {
      if (segDist(x, z, w.ax, w.az, w.bx, w.bz) < w.t * 0.5 + 0.1) return this.heightAt(x, z) + w.h;
    }
    return -Infinity;
  }

  inWall(x, z) {
    for (const w of this.features.walls) if (segDist(x, z, w.ax, w.az, w.bx, w.bz) < w.t * 0.5 + 0.35) return true;
    return false;
  }

  /** Прямая видимость между двумя точками: мешают склоны и ограды. */
  los(ax, ay, az, bx, by, bz) {
    const dx = bx - ax, dz = bz - az, d = Math.hypot(dx, dz), n = Math.max(2, Math.ceil(d / 1.4));
    for (let i = 1; i < n; i++) {
      const t = i / n, x = ax + dx * t, z = az + dz * t, y = ay + (by - ay) * t;
      if (this.heightAt(x, z) > y - 0.1) return false;
      if (this.wallTop(x, z) > y) return false;
    }
    return true;
  }

  buildWalls() {
    const walls = this.features.walls;
    const stones = [];
    const R = mulberry32(991);
    for (const w of walls) {
      const len = Math.hypot(w.bx - w.ax, w.bz - w.az), dx = (w.bx - w.ax) / len, dz = (w.bz - w.az) / len, yaw = Math.atan2(dx, dz);
      for (let course = 0; course < 3; course++) {
        const sy = course === 2 ? 0.3 : 0.52, y0 = course === 0 ? 0.26 : course === 1 ? 0.78 : 1.2;
        for (let s = (course % 2) * 0.35; s < len; s += lerp(0.62, 0.8, R())) {
          if (course === 2 && R() < 0.35) continue; // верх ограды местами осыпался
          const x = w.ax + dx * s, z = w.az + dz * s;
          stones.push({ x, z, y: this.heightAt(x, z) + y0, yaw: yaw + lerp(-0.12, 0.12, R()), sx: lerp(0.62, 0.82, R()), sy, sz: w.t * lerp(0.9, 1.1, R()), c: lerp(0.78, 1.05, R()) });
        }
      }
    }
    if (!stones.length) return;
    const mesh = new THREE.InstancedMesh(this.own(new THREE.BoxGeometry(1, 1, 1)), this.own(new THREE.MeshStandardMaterial({ color: 0x8c8479, roughness: 0.95 })), stones.length);
    const m4 = new THREE.Matrix4(), q = new THREE.Quaternion(), e = new THREE.Euler(), p = new THREE.Vector3(), sc = new THREE.Vector3(), c = new THREE.Color();
    stones.forEach((st, i) => {
      q.setFromEuler(e.set(lerp(-0.05, 0.05, R()), st.yaw + Math.PI / 2, lerp(-0.05, 0.05, R())));
      m4.compose(p.set(st.x, st.y, st.z), q, sc.set(st.sx, st.sy, st.sz));
      mesh.setMatrixAt(i, m4);
      mesh.setColorAt(i, c.setScalar(st.c));
    });
    mesh.castShadow = true; mesh.receiveShadow = true;
    this.group.add(mesh);
  }

  dispose() {
    if (this.group) scene.remove(this.group);
    for (const o of this.owned) o.dispose?.();
    this.owned = [];
    this.group = null;
  }

  own(o) { this.owned.push(o); return o; }

  // ---------------------------------------------------------------- высоты

  rawHeight(x, z) {
    const n = this.pn, o = this.o;
    const dist = Math.max(Math.abs(x), Math.abs(z)) * 0.6 + Math.hypot(x, z) * 0.4;
    const hills = n.fbm(x * 0.006 + o.hx, z * 0.006 + o.hz, 4);
    const detail = n.fbm(x * 0.05 + o.hx + 31.7, z * 0.05 + o.hz - 11.3, 2);

    // Поле боя: мягкие холмы, всегда выше воды
    let field = 2.6 + hills * 3.2 + detail * 0.35;
    const F = this.features;
    if (F && dist < FIELD + 30) {
      for (const hl of F.hills) {
        const d2 = ((x - hl.x) ** 2 + (z - hl.z) ** 2) / (hl.r * hl.r);
        if (d2 < 9) field += hl.h * Math.exp(-d2);
      }
      for (const r of F.ridges) {
        const d = segDist(x, z, r.ax, r.az, r.bx, r.bz);
        if (d < r.r * 3) field += r.h * Math.exp(-((d / r.r) ** 2));
      }
      const rv = F.ravine;
      const d = polyDist(x, z, rv.pts);
      if (d < rv.w) field -= rv.d * smooth(rv.w, rv.w * 0.3, d);
    }
    if (field < 1.2) field = 1.2 - (1.2 - field) * 0.3;

    // Окрестности: холмы крупнее, озёра, горы по краям
    let outer = 3 + hills * 16 + detail * 0.9;
    const lake = n.n01(x * 0.011 + o.lx, z * 0.011 + o.lz);
    outer -= Math.max(0, lake - 0.58) * 70 * smooth(105, 135, dist);
    const floor = lerp(1.0, -30, smooth(100, 125, dist));
    if (outer < floor) outer = floor;
    const mt = smooth(115, 215, dist);
    outer += mt * mt * (30 + n.ridged(x * 0.009 + o.mx, z * 0.009 + o.mz, 4) * 60);

    return lerp(field, outer, smooth(FIELD - 5, FIELD + 40, dist));
  }

  H(ix, iz) { return this.h[iz * (RES + 1) + ix]; }

  heightAt(x, z) {
    const gx = clamp((x + HALF) / CELL, 0, RES - 0.0001), gz = clamp((z + HALF) / CELL, 0, RES - 0.0001);
    const ix = gx | 0, iz = gz | 0, fx = gx - ix, fz = gz - iz;
    const h00 = this.H(ix, iz), h10 = this.H(ix + 1, iz), h01 = this.H(ix, iz + 1), h11 = this.H(ix + 1, iz + 1);
    if (fz >= fx) return h00 + (h11 - h01) * fx + (h01 - h00) * fz;
    return h00 + (h10 - h00) * fx + (h11 - h10) * fz;
  }

  slopeAt(x, z) {
    const e = 1;
    const dx = this.heightAt(x - e, z) - this.heightAt(x + e, z), dz = this.heightAt(x, z - e) - this.heightAt(x, z + e);
    return 1 - 2 * e / Math.hypot(dx, 2 * e, dz); // 0 = ровно, 1 = отвесно
  }

  static inField(x, z, margin = 0) { return Math.abs(x) <= FIELD - margin && Math.abs(z) <= FIELD - margin; }

  /** Пересечение луча с рельефом: шагаем по лучу, затем уточняем делением пополам. */
  raycast(ray) {
    const p = new THREE.Vector3();
    let prev = 0;
    for (let t = 0; t < 1400; t += 1.5) {
      ray.at(t, p);
      if (Math.abs(p.x) > HALF || Math.abs(p.z) > HALF) { prev = t; continue; }
      if (p.y <= this.heightAt(p.x, p.z)) {
        let a = prev, b = t;
        for (let i = 0; i < 14; i++) { const m = (a + b) / 2; ray.at(m, p); if (p.y <= this.heightAt(p.x, p.z)) b = m; else a = m; }
        ray.at(b, p);
        p.y = this.heightAt(p.x, p.z);
        return p;
      }
      prev = t;
    }
    return null;
  }

  // ---------------------------------------------------------------- рельеф

  buildTerrain(s) {
    const n = this.pn, o = this.o;
    const data = new Uint8Array(TEX * TEX * 4);
    const c = new THREE.Color(), t = new THREE.Color();
    for (let ty = 0; ty < TEX; ty++) {
      for (let tx = 0; tx < TEX; tx++) {
        const x = (tx + 0.5) / TEX * SIZE - HALF, z = (ty + 0.5) / TEX * SIZE - HALF;
        const y = this.heightAt(x, z), slope = this.slopeAt(x, z);
        const n1 = n.n01(x * 0.035 + o.cx, z * 0.035 + o.cz);
        const n2 = n.n01(x * 0.16 + o.cz, z * 0.16 + o.cx);
        const n3 = n.n01(x * 0.012 + o.cx * 0.5, z * 0.012 + o.cz * 0.5);

        c.copy(s.grassA).lerp(s.grassB, n1);
        c.lerp(s.dry, smooth(0.55, 0.8, n3) * 0.75);
        c.lerp(s.dirt, smooth(0.72, 0.85, n2 * 0.6 + n1 * 0.4) * 0.6);
        const shore = y - WATER;
        t.copy(s.sand).lerp(c, smooth(0.2, 1.4, shore + (n2 - 0.5) * 0.6)); c.copy(t);
        if (shore < 0) c.copy(s.sand).lerp(s.underwater, smooth(0, -5, shore));
        c.lerp(s.rock, smooth(0.07, 0.16, slope + (n2 - 0.5) * 0.04));
        c.lerp(s.rock, smooth(32, 48, y + (n1 - 0.5) * 12));
        c.lerp(s.snow, smooth(s.snowLine, s.snowLine + 6, y + (n2 - 0.5) * 10) * (1 - smooth(0.2, 0.3, slope)));
        const k = 0.9 + n2 * 0.18;
        const i = (ty * TEX + tx) * 4;
        data[i] = clamp(c.r * k * 255, 0, 255); data[i + 1] = clamp(c.g * k * 255, 0, 255); data[i + 2] = clamp(c.b * k * 255, 0, 255); data[i + 3] = 255;
      }
    }
    // Цвета заданы в sRGB: кодируем их в текстуру как есть
    const colorTex = this.own(new THREE.DataTexture(data, TEX, TEX));
    colorTex.colorSpace = THREE.SRGBColorSpace;
    colorTex.generateMipmaps = true;
    colorTex.minFilter = THREE.LinearMipmapLinearFilter;
    colorTex.magFilter = THREE.LinearFilter;
    colorTex.anisotropy = renderer.capabilities.getMaxAnisotropy();
    colorTex.needsUpdate = true;

    const V = RES + 1;
    const pos = new Float32Array(V * V * 3), nor = new Float32Array(V * V * 3), uv = new Float32Array(V * V * 2);
    for (let z = 0; z < V; z++) {
      for (let x = 0; x < V; x++) {
        const i = z * V + x;
        pos[i * 3] = x * CELL - HALF; pos[i * 3 + 1] = this.H(x, z); pos[i * 3 + 2] = z * CELL - HALF;
        const hl = this.H(Math.max(x - 1, 0), z), hr = this.H(Math.min(x + 1, RES), z);
        const hd = this.H(x, Math.max(z - 1, 0)), hu = this.H(x, Math.min(z + 1, RES));
        const nx = hl - hr, ny = 2 * CELL, nz = hd - hu, l = Math.hypot(nx, ny, nz);
        nor[i * 3] = nx / l; nor[i * 3 + 1] = ny / l; nor[i * 3 + 2] = nz / l;
        uv[i * 2] = x / RES; uv[i * 2 + 1] = z / RES;
      }
    }
    const idx = new Uint32Array(RES * RES * 6);
    let k = 0;
    for (let z = 0; z < RES; z++) for (let x = 0; x < RES; x++) {
      const i00 = z * V + x, i10 = i00 + 1, i01 = i00 + V, i11 = i01 + 1;
      idx[k++] = i00; idx[k++] = i01; idx[k++] = i11;
      idx[k++] = i00; idx[k++] = i11; idx[k++] = i10;
    }
    const g = this.own(new THREE.BufferGeometry());
    g.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    g.setAttribute('normal', new THREE.BufferAttribute(nor, 3));
    g.setAttribute('uv', new THREE.BufferAttribute(uv, 2));
    g.setIndex(new THREE.BufferAttribute(idx, 1));
    g.computeBoundingSphere();

    const mat = this.own(new THREE.MeshStandardMaterial({ map: colorTex, roughness: 0.96, metalness: 0 }));
    mat.onBeforeCompile = (sh) => {
      sh.uniforms.detailMap = { value: DETAIL_TEX };
      sh.fragmentShader = sh.fragmentShader
        .replace('#include <common>', '#include <common>\nuniform sampler2D detailMap;')
        .replace('#include <map_fragment>', `#ifdef USE_MAP
          vec4 sampledDiffuseColor = texture2D( map, vMapUv );
          float dA = texture2D( detailMap, vMapUv * 97.0 ).r;
          float dB = texture2D( detailMap, vMapUv * 23.0 ).g;
          sampledDiffuseColor.rgb *= 0.74 + dA * 0.38 + dB * 0.16;
          diffuseColor *= sampledDiffuseColor;
        #endif`);
    };
    const mesh = new THREE.Mesh(g, mat);
    mesh.receiveShadow = true;
    mesh.castShadow = !LOW_END;
    this.group.add(mesh);
  }

  buildWater(s) {
    const g = this.own(new THREE.PlaneGeometry(SIZE + 80, SIZE + 80));
    g.rotateX(-Math.PI / 2);
    const m = this.own(new THREE.MeshStandardMaterial({ color: s.water, transparent: true, opacity: s.waterAlpha, roughness: 0.05, metalness: 0.1 }));
    const w = new THREE.Mesh(g, m);
    w.position.y = WATER;
    this.group.add(w);
  }

  // ---------------------------------------------------------------- небо и свет

  applyAtmosphere(s) {
    const phi = (90 - s.elev) * DEG, theta = s.azim * DEG;
    this.sunDir.setFromSphericalCoords(1, phi, theta);
    SKY.material.uniforms.sunPosition.value.copy(this.sunDir);
    SKY.material.uniforms.turbidity.value = s.turb;
    SKY.material.uniforms.rayleigh.value = s.rayleigh;
    SKY.material.uniforms.mieCoefficient.value = 0.005;
    SKY.material.uniforms.mieDirectionalG.value = 0.8;
    sunLight.color.copy(s.sunColor);
    sunLight.intensity = s.sunI;
    hemiLight.color.copy(s.fog).lerp(new THREE.Color(0xffffff), 0.4);
    hemiLight.groundColor.copy(s.grassA).multiplyScalar(0.5);
    scene.fog = new THREE.FogExp2(s.fog, s.fogD);
    renderer.toneMappingExposure = s.expo;

    // Отражения и мягкий свет от неба
    if (this.envRT) this.envRT.dispose();
    const skyScene = new THREE.Scene();
    const skyCopy = new Sky(); skyCopy.scale.setScalar(1000); skyCopy.material = SKY.material;
    skyScene.add(skyCopy);
    this.envRT = PMREM.fromScene(skyScene, 0, 0.1, 2000);
    scene.environment = this.envRT.texture;
  }

  // ---------------------------------------------------------------- деревья, камни, трава, замки

  flatSpot(xMin, xMax, zMin, zMax, radius, tries) {
    let best = null, bestScore = Infinity;
    for (let i = 0; i < tries; i++) {
      const x = lerp(xMin, xMax, this.rand()), z = lerp(zMin, zMax, this.rand());
      let lo = Infinity, hi = -Infinity;
      for (let a = 0; a < 8; a++) {
        const hx = this.heightAt(x + Math.cos(a * Math.PI / 4) * radius, z + Math.sin(a * Math.PI / 4) * radius);
        lo = Math.min(lo, hx); hi = Math.max(hi, hx);
      }
      if (lo < WATER + 0.8) continue;
      const score = hi - lo;
      if (score < bestScore) { bestScore = score; best = { x, z, y: lo }; }
    }
    return best;
  }

  buildDecor(s, assets) {
    const r = this.rand, m4 = new THREE.Matrix4(), q = new THREE.Quaternion(), sc = new THREE.Vector3(), p = new THREE.Vector3(), up = new THREE.Vector3(0, 1, 0);
    const treeKinds = assets.trees;
    const lists = treeKinds.map(() => []);

    // Деревья рощами, вне поля боя
    const tries = Math.floor(2400 * s.treeDensity);
    for (let i = 0; i < tries; i++) {
      const x = lerp(-HALF + 6, HALF - 6, r()), z = lerp(-HALF + 6, HALF - 6, r());
      if (Math.abs(x) < FIELD + 7 && Math.abs(z) < FIELD + 7) continue;
      const y = this.heightAt(x, z);
      if (y < WATER + 0.8 || y > s.snowLine + 4 || this.slopeAt(x, z) > 0.12) continue;
      const forest = this.pn.n01(x * 0.013 + this.o.mx, z * 0.013 + this.o.mz);
      if (forest < 0.45 && r() > 0.06) continue;
      const kind = Math.floor(r() * treeKinds.length);
      lists[kind].push([x, y - 0.2, z, r() * Math.PI * 2, lerp(0.8, 1.3, r())]);
    }
    const treeMat = assets.treeMaterial(s);
    treeKinds.forEach((kind, i) => this.instanced(kind, treeMat, lists[i], true));

    // Камни: крупные снаружи, мелкие на поле
    const rockLists = assets.rocks.map(() => []);
    for (let i = 0; i < 260; i++) {
      const x = lerp(-HALF + 5, HALF - 5, r()), z = lerp(-HALF + 5, HALF - 5, r());
      const inField = Math.abs(x) < FIELD + 4 && Math.abs(z) < FIELD + 4;
      if (inField && r() > 0.3) continue;
      const y = this.heightAt(x, z);
      if (y < WATER - 1.5 || y > 40) continue;
      const size = inField ? lerp(0.5, 1.1, r()) : lerp(1.2, 4, r());
      rockLists[Math.floor(r() * rockLists.length)].push([x, y - size * 0.3, z, r() * Math.PI * 2, size]);
    }
    assets.rocks.forEach((kind, i) => this.instanced(kind, assets.rockMat, rockLists[i], true));

    // Трава и цветы
    const tuftCount = Math.floor((LOW_END ? 3500 : 7000) * s.grassDensity);
    const grass = new THREE.InstancedMesh(GRASS_GEO, GRASS_MAT, tuftCount);
    const flowerCount = Math.floor(tuftCount * s.flowers);
    const flowers = new THREE.InstancedMesh(FLOWER_GEO, FLOWER_MAT, Math.max(1, flowerCount));
    const col = new THREE.Color();
    let gi = 0, fi = 0;
    for (let tries2 = 0; gi < tuftCount && tries2 < tuftCount * 3; tries2++) {
      const x = lerp(-FIELD - 30, FIELD + 30, r()), z = lerp(-FIELD - 30, FIELD + 30, r());
      const y = this.heightAt(x, z);
      if (y < WATER + 0.4 || this.pn.n01(x * 0.05 + this.o.lx, z * 0.05 + this.o.lz) < 0.33) continue;
      q.setFromAxisAngle(up, r() * Math.PI * 2);
      const k = lerp(0.7, 1.4, r());
      m4.compose(p.set(x, y - 0.03, z), q, sc.set(k, k * lerp(0.8, 1.3, r()), k));
      grass.setMatrixAt(gi, m4);
      grass.setColorAt(gi, col.copy(s.grass1).lerp(s.grass2, r()));
      gi++;
      if (fi < flowerCount && r() < s.flowers * 1.2) {
        m4.compose(p.set(x + 0.1, y + 0.33 * k, z), q, sc.set(1, 1, 1));
        flowers.setMatrixAt(fi, m4);
        flowers.setColorAt(fi, s.flower[Math.floor(r() * 3)]);
        fi++;
      }
    }
    grass.count = gi; flowers.count = fi;
    grass.receiveShadow = true;
    this.group.add(grass);
    if (fi > 0) this.group.add(flowers);

    // Замки армий за их спинами, мельница и башни на холмах
    const blue = this.flatSpot(-35, 35, -150, -104, 9, 60), red = this.flatSpot(-35, 35, 104, 150, 9, 60);
    if (blue) this.placeBuilding(assets.buildings.castle_blue, blue, 0, 24);
    if (red) this.placeBuilding(assets.buildings.castle_red, red, Math.PI, 24);
    for (let i = 0; i < 3; i++) {
      const side = r() < 0.5 ? -1 : 1;
      const spot = this.flatSpot(side * 150, side * 100, -120, 120, 5, 40);
      if (spot) this.placeBuilding(i === 0 ? assets.buildings.windmill : assets.buildings.tower, spot, r() * Math.PI * 2, i === 0 ? 15 : 13);
    }
  }

  instanced(kind, mat, list, shadow) {
    if (!list.length) return;
    const mesh = new THREE.InstancedMesh(kind.geometry, mat, list.length);
    const m4 = new THREE.Matrix4(), q = new THREE.Quaternion(), s = new THREE.Vector3(), p = new THREE.Vector3(), up = new THREE.Vector3(0, 1, 0);
    list.forEach(([x, y, z, rot, k], i) => {
      q.setFromAxisAngle(up, rot);
      const f = kind.norm * k;
      m4.compose(p.set(x, y, z), q, s.set(f, f, f));
      mesh.setMatrixAt(i, m4);
    });
    mesh.castShadow = shadow;
    mesh.receiveShadow = true;
    mesh.computeBoundingSphere();
    this.group.add(mesh);
  }

  placeBuilding(model, spot, rot, height) {
    const o = model.scene.clone(true);
    const f = height / model.height;
    o.scale.setScalar(f);
    o.position.set(spot.x, spot.y - 0.6, spot.z);
    o.rotation.y = rot;
    o.traverse((c) => { if (c.isMesh) { c.castShadow = true; c.receiveShadow = true; } });
    this.group.add(o);
  }
}

// Общие ресурсы травы и детальной текстуры земли (создаются один раз)
let DETAIL_TEX, GRASS_GEO, GRASS_MAT, FLOWER_GEO, FLOWER_MAT;

function initSharedWorldResources() {
  const n = 128, d = new Uint8Array(n * n * 4), pn = new Perlin(mulberry32(7));
  for (let y = 0; y < n; y++) for (let x = 0; x < n; x++) {
    // Бесшовный шум: смешиваем четыре сдвинутые копии
    const u = x / n, v = y / n;
    const f = (a, b, s) => pn.n01(a * s, b * s);
    const tile = (s) => f(u, v, s) * (1 - u) * (1 - v) + f(u - 1, v, s) * u * (1 - v) + f(u, v - 1, s) * (1 - u) * v + f(u - 1, v - 1, s) * u * v;
    const i = (y * n + x) * 4;
    d[i] = tile(9) * 255; d[i + 1] = tile(3) * 255; d[i + 2] = 128; d[i + 3] = 255;
  }
  DETAIL_TEX = new THREE.DataTexture(d, n, n);
  DETAIL_TEX.wrapS = DETAIL_TEX.wrapT = THREE.RepeatWrapping;
  DETAIL_TEX.generateMipmaps = true;
  DETAIL_TEX.minFilter = THREE.LinearMipmapLinearFilter;
  DETAIL_TEX.magFilter = THREE.LinearFilter;
  DETAIL_TEX.needsUpdate = true;

  // Пучок травы: 5 изогнутых травинок
  const pos = [];
  for (let b = 0; b < 5; b++) {
    const a = b * Math.PI * 2 / 5 + (b % 2) * 0.4;
    const sx = Math.cos(a), sz = Math.sin(a), ax = -sz * 0.05, az = sx * 0.05;
    const bx = sx * 0.06, bz = sz * 0.06, tx = sx * 0.22, tz = sz * 0.22, th = 0.42 + (b % 3) * 0.08;
    // обе стороны травинки с нормалью вверх — иначе обратная сторона чернеет
    pos.push(bx - ax, 0, bz - az, bx + ax, 0, bz + az, tx, th, tz);
    pos.push(bx + ax, 0, bz + az, bx - ax, 0, bz - az, tx, th, tz);
  }
  GRASS_GEO = new THREE.BufferGeometry();
  GRASS_GEO.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  const nrm = [];
  for (let i = 0; i < pos.length / 3; i++) nrm.push(0, 1, 0);
  GRASS_GEO.setAttribute('normal', new THREE.Float32BufferAttribute(nrm, 3));
  GRASS_MAT = new THREE.MeshLambertMaterial();
  FLOWER_GEO = new THREE.IcosahedronGeometry(0.07, 0);
  FLOWER_MAT = new THREE.MeshLambertMaterial();
}

/** Расстояние от точки до отрезка (в плоскости XZ). */
function segDist(x, z, ax, az, bx, bz) {
  const vx = bx - ax, vz = bz - az, l2 = vx * vx + vz * vz;
  const t = l2 > 0 ? clamp(((x - ax) * vx + (z - az) * vz) / l2, 0, 1) : 0;
  return Math.hypot(x - ax - vx * t, z - az - vz * t);
}

/** Расстояние от точки до ломаной. */
function polyDist(x, z, pts) {
  let best = Infinity;
  for (let i = 1; i < pts.length; i++) best = Math.min(best, segDist(x, z, pts[i - 1][0], pts[i - 1][1], pts[i][0], pts[i][1]));
  return best;
}
