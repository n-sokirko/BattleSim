// ------------------------------------------------------------------ мир

let SIZE = 440, RES = 256, HALF = SIZE / 2, CELL = SIZE / RES, FIELD = 80;
const WATER = 0, TEX = 512;

/** Размер поля боя: обычное 160×160 м или большое (для великой сечи). */
function setWorldScale(big) {
  FIELD = big ? 118 : 80;
  SIZE = FIELD * 2 + 280;
  RES = big ? 300 : 256;
  HALF = SIZE / 2;
  CELL = SIZE / RES;
}

class World {
  constructor() {
    this.h = new Float32Array((RES + 1) * (RES + 1));
    this.group = null;
    this.owned = [];
    this.sunDir = new THREE.Vector3(0, 1, 0);
    this.type = 'field';
    this.features = { hills: [], ridges: [], ravine: null, walls: [] };
  }

  generate(seed, style, assets, type = 'field', big = false) {
    this.dispose();
    setWorldScale(big);
    this.type = type; this.big = big; this.style = style;
    this.h = new Float32Array((RES + 1) * (RES + 1));
    this.group = new THREE.Group();
    scene.add(this.group);
    const rand = mulberry32(seed);
    this.rand = rand;
    this.pn = new Perlin(rand);
    const o = () => rand() * 200;
    this.o = { hx: o(), hz: o(), lx: o(), lz: o(), mx: o(), mz: o(), cx: o(), cz: o() };
    this.town = type === 'city' && assets ? makeTown(rand, assets.cityDefs) : null;
    this.features = this.makeFeatures(rand, type);
    const wa = rand() * Math.PI * 2, ws = rand() < 0.2 ? 0 : 1 + rand() * 6;
    this.wind = new THREE.Vector2(Math.cos(wa) * ws, Math.sin(wa) * ws);

    const h = this.h;
    for (let z = 0; z <= RES; z++)
      for (let x = 0; x <= RES; x++)
        h[z * (RES + 1) + x] = this.rawHeight(x * CELL - HALF, z * CELL - HALF);

    // Препятствия: стволы леса, дома, мелочи; ограды — отдельной сеткой (их перелезают)
    this.obs = new Obstacles(FIELD);
    this.wallGrid = new Obstacles(FIELD);
    for (const w of this.features.walls) {
      const len = Math.hypot(w.bx - w.ax, w.bz - w.az);
      this.wallGrid.insert({ wall: w, x: (w.ax + w.bx) / 2, z: (w.az + w.bz) / 2 }, len / 2 + w.t);
    }
    this.trees = type === 'forest' ? this.plantForest(rand) : [];
    for (const t of this.trees) this.obs.addCircle(t.x, t.z, t.trunk, 7, this.heightAt(t.x, t.z));
    if (this.town) {
      for (const b of this.town.buildings) {
        b.y = this.footprintY(b);
        if (b.def.kind === 'well') this.obs.addCircle(b.x, b.z, Math.max(b.hx, b.hz), b.top, b.y);
        else this.obs.addRect(b.x, b.z, b.hx, b.hz, b.rot, b.top, b.y);
      }
      for (const p of this.town.props) {
        const s = 1.1 / p.def.h;
        p.s = s; p.y = this.heightAt(p.x, p.z);
        this.obs.addCircle(p.x, p.z, Math.max(p.def.w, p.def.d) * s * 0.5, 1.1, p.y);
      }
    }
    this.nav = new NavGrid(this);
    this.analyze();

    this.buildTerrain(style);
    this.buildWater(style);
    this.applyAtmosphere(style);
    this.buildWalls();
    if (assets) this.buildDecor(style, assets);
  }

  dispose() {
    if (this.group) scene.remove(this.group);
    for (const o of this.owned) o.dispose?.();
    this.owned = [];
    this.group = null;
  }

  own(o) { this.owned.push(o); return o; }

  /** Где армии выстраиваются перед боем (расстояние от центра до первой линии). */
  get spawnZ() { return this.town ? this.town.TZ + 8 : FIELD * (this.big ? 0.3 : 0.28); }

  // ---------------------------------------------------------------- тактический рельеф

  /** Холмы, овраги, гряды, островки и развалины — всё, за что можно воевать. */
  makeFeatures(R, type) {
    const f = { hills: [], ridges: [], ravine: null, walls: [], islands: [] };
    const F = FIELD, S = F / 80;
    const ruins = (count) => {
      for (let i = 0; i < count; i++) {
        const cx = lerp(-F * 0.78, F * 0.78, R()), cz = lerp(-F * 0.42, F * 0.42, R());
        const ang = R() < 0.6 ? lerp(-0.35, 0.35, R()) : Math.PI / 2 + lerp(-0.35, 0.35, R());
        const len = lerp(10, 22, R()), dx = Math.cos(ang), dz = Math.sin(ang);
        let s = -len / 2;
        while (s < len / 2) {
          const e = Math.min(len / 2, s + lerp(3.5, 7, R()));
          f.walls.push({ ax: cx + dx * s, az: cz + dz * s, bx: cx + dx * e, bz: cz + dz * e, h: 1.35, t: 0.5 });
          s = e + lerp(1.7, 2.6, R());
        }
      }
    };

    if (type === 'city') { f.walls = this.town ? this.town.walls.slice() : []; return f; }

    if (type === 'mountains') {
      // Две гряды поперёк поля, в каждой — перевалы
      for (const zc of [lerp(-F * 0.22, -F * 0.05, R()), lerp(F * 0.08, F * 0.25, R())]) {
        const gaps = 1 + ((R() * 2) | 0), cuts = [...Array(gaps)].map(() => lerp(-F * 0.6, F * 0.6, R())).sort((a, b) => a - b);
        let x = -F * 1.05;
        for (const c of [...cuts, F * 1.05]) {
          const e = c - 7;
          if (e - x > 8) f.ridges.push({ ax: x, az: zc + lerp(-6, 6, R()), bx: e, bz: zc + lerp(-6, 6, R()), r: lerp(6.5, 8.5, R()), h: lerp(11, 16, R()) });
          x = c + 7;
        }
      }
      for (let i = 0; i < 3; i++) f.hills.push({ x: lerp(-F * 0.7, F * 0.7, R()), z: lerp(-F * 0.45, F * 0.45, R()), r: lerp(8, 12, R()) * S, h: lerp(6, 10, R()) });
      return f;
    }

    if (type === 'swamp') {
      const n = Math.round(lerp(6, 9, R()) * S);
      for (let i = 0; i < n; i++) f.hills.push({ x: lerp(-F * 0.75, F * 0.75, R()), z: lerp(-F * 0.4, F * 0.4, R()), r: lerp(6, 10, R()), h: lerp(1.8, 3.2, R()) });
      ruins(1 + ((R() * 2) | 0));
      return f;
    }

    // Поле и лес: холмы по сторонам, овраг между армиями, гряда на фланге
    for (const side of [-1, 1])
      f.hills.push({ x: lerp(-F * 0.56, F * 0.56, R()), z: side * lerp(F * 0.25, F * 0.52, R()), r: lerp(11, 16, R()), h: lerp(4.5, 7.5, R()) });
    if (R() < 0.65) f.hills.push({ x: (R() < 0.5 ? -1 : 1) * lerp(F * 0.48, F * 0.75, R()), z: lerp(-10, 10, R()), r: lerp(9, 13, R()), h: lerp(3.5, 6, R()) });
    if (type === 'field' || R() < 0.5) {
      const z0 = lerp(-10, 10, R()), tilt = lerp(-0.22, 0.22, R()), x0 = lerp(-F * 0.72, -F * 0.28, R()), x1 = lerp(F * 0.28, F * 0.75, R()), ph = R() * 6;
      const pts = [];
      for (let i = 0; i <= 16; i++) { const x = lerp(x0, x1, i / 16); pts.push([x, z0 + x * tilt + Math.sin(x * 0.06 + ph) * 6]); }
      f.ravine = { pts, w: lerp(6.5, 9, R()), d: type === 'forest' ? lerp(2.2, 3, R()) : lerp(3.4, 4.4, R()) };
    }
    if (R() < 0.7) {
      const s = R() < 0.5 ? -1 : 1, x = s * lerp(F * 0.52, F * 0.8, R());
      f.ridges.push({ ax: x, az: lerp(-F * 0.48, -F * 0.1, R()), bx: x + lerp(-10, 10, R()), bz: lerp(F * 0.1, F * 0.48, R()), r: 4.5, h: lerp(2.6, 3.6, R()) });
    }
    ruins(type === 'forest' ? 1 + ((R() * 2) | 0) : Math.round((3 + ((R() * 3) | 0)) * S));
    return f;
  }

  /** Лес: плотность чащи 0..1 (у мест построения армий — поляны). */
  forestAt(x, z) {
    if (this.type !== 'forest' || !World.inField(x, z)) return 0;
    const d = this.pn.n01(x * 0.024 + this.o.cx, z * 0.024 + this.o.cz);
    const mask = 1 - 0.8 * smooth(FIELD * 0.46, FIELD * 0.72, Math.abs(z));
    return smooth(0.4, 0.6, d) * mask;
  }

  /** Болото: камыш в мелкой воде и на топких берегах. */
  reedsAt(x, z) {
    if (this.type !== 'swamp') return false;
    const h = this.heightAt(x, z);
    return h < 0.45 && h > -0.9 && this.pn.n01(x * 0.09 + this.o.lx, z * 0.09 + this.o.lz) > 0.42;
  }

  plantForest(R) {
    const out = [], tries = Math.round(FIELD * FIELD * 0.26);
    for (let i = 0; i < tries; i++) {
      const x = lerp(-FIELD + 2, FIELD - 2, R()), z = lerp(-FIELD + 2, FIELD - 2, R());
      const f = this.forestAt(x, z);
      if (f < 0.3 || R() > f * 0.85) continue;
      const cluster = R() < 0.22;
      out.push({ x, z, kind: cluster ? 2 + ((R() * 5) | 0) : (R() * 2) | 0, rot: R() * Math.PI * 2, k: lerp(0.95, 1.4, R()), trunk: cluster ? 1.5 : 0.45 });
    }
    return out;
  }

  /** Высота основания дома: по самому низкому углу, чтобы он не висел над землёй. */
  footprintY(b) {
    let lo = Infinity;
    for (const [sx, sz] of [[-1, -1], [1, -1], [1, 1], [-1, 1], [0, 0]]) lo = Math.min(lo, this.heightAt(b.x + sx * b.hx, b.z + sz * b.hz));
    return lo - 0.15;
  }

  /** Карта «выпуклости» поля и укромные места: по ним думают полководцы. */
  analyze() {
    const step = 4, n = Math.floor((FIELD * 2) / step) + 1, prom = new Float32Array(n * n);
    for (let iz = 0; iz < n; iz++) for (let ix = 0; ix < n; ix++) {
      const x = -FIELD + ix * step, z = -FIELD + iz * step;
      let s = 0;
      for (let a = 0; a < 8; a++) s += this.heightAt(x + Math.cos(a * Math.PI / 4) * 14, z + Math.sin(a * Math.PI / 4) * 14);
      prom[iz * n + ix] = this.heightAt(x, z) - s / 8;
    }
    const high = [], low = [], hide = [];
    for (let iz = 1; iz < n - 1; iz++) for (let ix = 1; ix < n - 1; ix++) {
      const p = prom[iz * n + ix], x = -FIELD + ix * step, z = -FIELD + iz * step, h = this.heightAt(x, z);
      if (this.nav.speedAt(x, z, 0) === 0) continue;
      if (p > 1.4) {
        let top = true;
        for (let dz = -1; dz <= 1 && top; dz++) for (let dx = -1; dx <= 1; dx++)
          if ((dx || dz) && this.heightAt(x + dx * step, z + dz * step) > h) { top = false; break; }
        if (top) high.push({ x, z, h, prom: p });
      }
      if (p < -1.0 && h > WATER - 0.3) { low.push({ x, z, h, depth: -p }); hide.push({ x, z, depth: -p }); }
      else if ((ix + iz) % 2 === 0 && this.nav.concealAt(x, z)) hide.push({ x, z, depth: 1.5 });
    }
    high.sort((a, b) => b.prom - a.prom);
    this.an = { step, n, prom, high: high.slice(0, 16), low, hide };
  }

  prominenceAt(x, z) {
    const a = this.an, ix = clamp(Math.round((x + FIELD) / a.step), 0, a.n - 1), iz = clamp(Math.round((z + FIELD) / a.step), 0, a.n - 1);
    return a.prom[iz * a.n + ix];
  }

  /** Отряд укрыт от глаз: в низине, в чаще или в камыше. */
  concealedAt(x, z) { return this.prominenceAt(x, z) < -0.9 || this.nav.concealAt(x, z); }

  walkable(x, z, pad = 0.3) { return this.nav.speedAt(x, z, 0) > 0 && !this.obs.hit(x, z, pad); }

  /** Верх ограды в точке (или -Infinity, если ограды нет). */
  wallTop(x, z) {
    const L = this.wallGrid.near(x, z);
    if (L) for (const o of L) {
      const w = o.wall;
      if (segDist(x, z, w.ax, w.az, w.bx, w.bz) < w.t * 0.5 + 0.1) return this.heightAt(x, z) + w.h;
    }
    return -Infinity;
  }

  inWall(x, z) {
    const L = this.wallGrid.near(x, z);
    if (L) for (const o of L) { const w = o.wall; if (segDist(x, z, w.ax, w.az, w.bx, w.bz) < w.t * 0.5 + 0.35) return true; }
    return false;
  }

  /** Прямая видимость: мешают склоны, ограды, дома и густая чаща. */
  los(ax, ay, az, bx, by, bz) {
    const dx = bx - ax, dz = bz - az, d = Math.hypot(dx, dz), n = Math.max(2, Math.ceil(d / 1.4)), step = d / n;
    let canopy = 0;
    const forest = this.type === 'forest', houses = this.obs.list.length > 0;
    for (let i = 1; i < n; i++) {
      const t = i / n, x = ax + dx * t, z = az + dz * t, y = ay + (by - ay) * t, g = this.heightAt(x, z);
      if (g > y - 0.1) return false;
      if (this.wallTop(x, z) > y) return false;
      if (houses && this.obs.blocks(x, z, y)) return false;
      if (forest && y < g + 7 && this.nav.canopyAt(x, z) && (canopy += step) > 7) return false;
    }
    return true;
  }

  /** Места, где можно укрыться от стрелков врага (ec — центр вражеской армии). */
  coverCandidates(ec) {
    const out = [];
    for (const l of this.an.hide) out.push({ x: l.x, z: l.z, kind: 'low' });
    for (const w of this.features.walls) {
      const mx = (w.ax + w.bx) / 2, mz = (w.az + w.bz) / 2, l = Math.hypot(w.bx - w.ax, w.bz - w.az) || 1;
      const nx = -(w.bz - w.az) / l, nz = (w.bx - w.ax) / l, side = nx * (mx - ec.x) + nz * (mz - ec.z) > 0 ? 1 : -1;
      out.push({ x: mx + nx * 1.6 * side, z: mz + nz * 1.6 * side, kind: 'wall' });
    }
    if (this.town) for (const b of this.town.buildings) {
      const v = norm2(b.x - ec.x, b.z - ec.z), r = Math.max(b.hx, b.hz) + 2;
      out.push({ x: b.x + v.x * r, z: b.z + v.z * r, kind: 'house' });
    }
    return out;
  }

  // ---------------------------------------------------------------- высоты

  rawHeight(x, z) {
    const n = this.pn, o = this.o, T = this.type, F = this.features;
    const dist = Math.max(Math.abs(x), Math.abs(z)) * 0.6 + Math.hypot(x, z) * 0.4;
    const hills = n.fbm(x * 0.006 + o.hx, z * 0.006 + o.hz, 4);
    const detail = n.fbm(x * 0.05 + o.hx + 31.7, z * 0.05 + o.hz - 11.3, 2);

    let field;
    if (T === 'swamp') {
      field = 0.35 + hills * 1.4 + detail * 0.35;
      const pool = n.n01(x * 0.028 + o.lx, z * 0.028 + o.lz);
      field -= Math.max(0, pool - 0.42) * 7;
      field += smooth(FIELD * 0.42, FIELD * 0.72, Math.abs(z)) * 1.9; // у армий суше
    } else if (T === 'city') field = 2.2 + hills * 1.0 + detail * 0.12;
    else if (T === 'mountains') field = 3 + hills * 5 + detail * 0.5 + n.ridged(x * 0.02 + o.mx, z * 0.02 + o.mz, 3) * 4 * (1 - smooth(FIELD * 0.5, FIELD * 0.75, Math.abs(z)));
    else field = 2.6 + hills * 3.2 + detail * 0.35;

    if (dist < FIELD + 30) {
      for (const hl of F.hills) {
        const d2 = ((x - hl.x) ** 2 + (z - hl.z) ** 2) / (hl.r * hl.r);
        if (d2 < 9) field += hl.h * Math.exp(-d2);
      }
      for (const r of F.ridges) {
        const d = segDist(x, z, r.ax, r.az, r.bx, r.bz);
        if (d < r.r * 3) field += r.h * Math.exp(-((d / r.r) ** 2));
      }
      if (F.ravine) {
        const rv = F.ravine, d = polyDist(x, z, rv.pts);
        if (d < rv.w) field -= rv.d * smooth(rv.w, rv.w * 0.3, d);
      }
    }
    if (T === 'swamp') field = Math.max(field, -2.2);
    else if (field < 1.2) field = 1.2 - (1.2 - field) * 0.3;

    // Окрестности: холмы крупнее, озёра, горы по краям
    let outer = 3 + hills * 16 + detail * 0.9;
    const lake = n.n01(x * 0.011 + o.lx, z * 0.011 + o.lz);
    outer -= Math.max(0, lake - 0.58) * 70 * smooth(FIELD + 25, FIELD + 55, dist);
    const floor = lerp(1.0, -30, smooth(FIELD + 20, FIELD + 45, dist));
    if (outer < floor) outer = floor;
    const mt = smooth(FIELD + 35, FIELD + 135, dist);
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

  /** Мощёные улицы и площадь города: 0 — трава, 1 — камень. */
  paving(x, z) {
    const T = this.town;
    if (!T || Math.abs(x) > T.TX + 12 || Math.abs(z) > T.TZ + 12) return 0;
    for (const sq of T.squares) if (Math.abs(x - sq.x) < sq.hx && Math.abs(z - sq.z) < sq.hz) return 1;
    let best = Infinity;
    for (const s of T.streets) best = Math.min(best, segDist(x, z, s.ax, s.az, s.bx, s.bz) - s.w / 2);
    return smooth(0.8, -0.4, best);
  }

  // ---------------------------------------------------------------- рельеф

  buildTerrain(s) {
    const n = this.pn, o = this.o;
    const data = new Uint8Array(TEX * TEX * 4);
    const c = new THREE.Color(), t = new THREE.Color();
    const mud = new THREE.Color(0x4f4a2c), cobble = new THREE.Color(0x8b8274), floorC = new THREE.Color(0x3f5328);
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
        if (this.type === 'forest') c.lerp(floorC, this.forestAt(x, z) * 0.7);
        const shore = y - WATER;
        if (this.type === 'swamp') {
          c.lerp(mud, smooth(0.9, 0.1, shore) * 0.8);
          if (shore < 0) c.copy(mud).lerp(s.underwater, smooth(0, -1.5, shore));
        } else {
          t.copy(s.sand).lerp(c, smooth(0.2, 1.4, shore + (n2 - 0.5) * 0.6)); c.copy(t);
          if (shore < 0) c.copy(s.sand).lerp(s.underwater, smooth(0, -5, shore));
        }
        c.lerp(s.rock, smooth(0.07, 0.16, slope + (n2 - 0.5) * 0.04));
        c.lerp(s.rock, smooth(32, 48, y + (n1 - 0.5) * 12));
        c.lerp(s.snow, smooth(s.snowLine, s.snowLine + 6, y + (n2 - 0.5) * 10) * (1 - smooth(0.2, 0.3, slope)));
        if (this.town) c.lerp(cobble, this.paving(x, z) * (0.75 + n2 * 0.2));
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

  buildWalls() {
    const walls = this.features.walls;
    const stones = [];
    const R = mulberry32(991);
    for (const w of walls) {
      const len = Math.hypot(w.bx - w.ax, w.bz - w.az);
      if (len < 0.3) continue;
      const dx = (w.bx - w.ax) / len, dz = (w.bz - w.az) / len, yaw = Math.atan2(dx, dz);
      const courses = w.h > 1.2 ? 3 : 2;
      for (let course = 0; course < courses; course++) {
        const sy = course === 2 ? 0.3 : 0.52, y0 = course === 0 ? 0.26 : course === 1 ? 0.78 : 1.2;
        for (let s = (course % 2) * 0.35; s < len; s += lerp(0.62, 0.8, R())) {
          if (course === courses - 1 && R() < 0.35) continue; // верх ограды местами осыпался
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

  // ---------------------------------------------------------------- деревья, камни, трава, постройки

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
    const T = this.type;
    const treeKinds = assets.trees;
    const lists = treeKinds.map(() => []);

    // Деревья рощами вокруг поля боя
    const tries = Math.floor(2400 * s.treeDensity * (T === 'forest' ? 1.4 : T === 'mountains' ? 0.7 : 1) * (this.big ? 1.4 : 1));
    for (let i = 0; i < tries; i++) {
      const x = lerp(-HALF + 6, HALF - 6, r()), z = lerp(-HALF + 6, HALF - 6, r());
      if (Math.abs(x) < FIELD + 7 && Math.abs(z) < FIELD + 7) continue;
      const y = this.heightAt(x, z);
      if (y < WATER + 0.8 || y > s.snowLine + 4 || this.slopeAt(x, z) > 0.12) continue;
      const forest = this.pn.n01(x * 0.013 + this.o.mx, z * 0.013 + this.o.mz);
      if (forest < 0.45 && r() > 0.06) continue;
      lists[Math.floor(r() * treeKinds.length)].push([x, y - 0.2, z, r() * Math.PI * 2, lerp(0.8, 1.3, r())]);
    }
    // Лес на самом поле боя
    for (const t of this.trees) lists[t.kind].push([t.x, this.heightAt(t.x, t.z) - 0.2, t.z, t.rot, t.k]);
    // Горы: редкие сосны на пологих склонах
    if (T === 'mountains') for (let i = 0; i < FIELD * 3; i++) {
      const x = lerp(-FIELD, FIELD, r()), z = lerp(-FIELD, FIELD, r());
      if (this.slopeAt(x, z) > 0.1 || this.nav.speedAt(x, z, 0) === 0) continue;
      lists[(r() * 2) | 0].push([x, this.heightAt(x, z) - 0.2, z, r() * Math.PI * 2, lerp(0.8, 1.2, r())]);
      this.obs.addCircle(x, z, 0.45, 7, this.heightAt(x, z));
    }
    const treeMat = assets.treeMaterial(s);
    treeKinds.forEach((kind, i) => this.instanced(kind, treeMat, lists[i], true));

    // Камни: крупные снаружи, мелкие на поле (в горах — побольше)
    const rockLists = assets.rocks.map(() => []);
    for (let i = 0; i < (T === 'mountains' ? 420 : 260) * (this.big ? 1.5 : 1); i++) {
      const x = lerp(-HALF + 5, HALF - 5, r()), z = lerp(-HALF + 5, HALF - 5, r());
      const inField = Math.abs(x) < FIELD + 4 && Math.abs(z) < FIELD + 4;
      if (inField && (r() > (T === 'mountains' ? 0.6 : 0.3) || this.paving(x, z) > 0 || this.obs.hit(x, z, 1))) continue;
      const y = this.heightAt(x, z);
      if (y < WATER - 1.5 || y > 40) continue;
      const size = inField ? lerp(0.5, T === 'mountains' ? 2.5 : 1.1, r()) : lerp(1.2, 4, r());
      rockLists[Math.floor(r() * rockLists.length)].push([x, y - size * 0.3, z, r() * Math.PI * 2, size]);
    }
    assets.rocks.forEach((kind, i) => this.instanced(kind, assets.rockMat, rockLists[i], true));

    // Трава и цветы (на болоте — камыш)
    const tuftCount = Math.floor((LOW_END ? 3500 : 7000) * s.grassDensity * (this.big ? 1.6 : 1));
    const grass = new THREE.InstancedMesh(GRASS_GEO, GRASS_MAT, tuftCount);
    const flowerCount = Math.floor(tuftCount * s.flowers);
    const flowers = new THREE.InstancedMesh(FLOWER_GEO, FLOWER_MAT, Math.max(1, flowerCount));
    const col = new THREE.Color(), reedA = new THREE.Color(0x8a8f45), reedB = new THREE.Color(0xa99a55);
    let gi = 0, fi = 0;
    for (let tries2 = 0; gi < tuftCount && tries2 < tuftCount * 3; tries2++) {
      const x = lerp(-FIELD - 30, FIELD + 30, r()), z = lerp(-FIELD - 30, FIELD + 30, r());
      const y = this.heightAt(x, z);
      const reed = this.reedsAt(x, z);
      if (!reed && (y < WATER + 0.4 || this.pn.n01(x * 0.05 + this.o.lx, z * 0.05 + this.o.lz) < 0.33)) continue;
      if (this.town && (this.paving(x, z) > 0.2 || this.obs.hit(x, z, 0.3))) continue;
      q.setFromAxisAngle(up, r() * Math.PI * 2);
      const k = lerp(0.7, 1.4, r());
      if (reed) m4.compose(p.set(x, Math.max(y, WATER - 0.2) - 0.05, z), q, sc.set(k * 1.1, k * lerp(3, 4.5, r()), k * 1.1));
      else m4.compose(p.set(x, y - 0.03, z), q, sc.set(k, k * lerp(0.8, 1.3, r()), k));
      grass.setMatrixAt(gi, m4);
      grass.setColorAt(gi, reed ? col.copy(reedA).lerp(reedB, r()) : col.copy(s.grass1).lerp(s.grass2, r()));
      gi++;
      if (!reed && fi < flowerCount && r() < s.flowers * 1.2) {
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

    if (this.town) this.buildTown(assets);

    // Замки армий за их спинами, мельница и башни на холмах
    const F = FIELD;
    const blue = this.flatSpot(-35, 35, -(F + 70), -(F + 24), 9, 60), red = this.flatSpot(-35, 35, F + 24, F + 70, 9, 60);
    if (blue) this.placeBuilding(assets.buildings.castle_blue, blue, 0, 24);
    if (red) this.placeBuilding(assets.buildings.castle_red, red, Math.PI, 24);
    for (let i = 0; i < 3; i++) {
      const side = r() < 0.5 ? -1 : 1;
      const spot = this.flatSpot(side * (F + 70), side * (F + 20), -(F + 40), F + 40, 5, 40);
      if (spot) this.placeBuilding(i === 0 ? assets.buildings.windmill : assets.buildings.tower, spot, r() * Math.PI * 2, i === 0 ? 15 : 13);
    }
  }

  /** Все дома города сливаются в один меш: одна текстура, один вызов отрисовки. */
  buildTown(assets) {
    const geos = [], m4 = new THREE.Matrix4(), q = new THREE.Quaternion(), p = new THREE.Vector3(), s = new THREE.Vector3(), up = new THREE.Vector3(0, 1, 0);
    const put = (def, x, y, z, rot, k) => {
      q.setFromAxisAngle(up, rot);
      m4.compose(p.set(x, y, z), q, s.set(k, k, k));
      geos.push(def.geometry.clone().applyMatrix4(m4));
    };
    for (const b of this.town.buildings) put(b.def, b.x, b.y, b.z, b.rot, b.s);
    for (const pr of this.town.props) put(pr.def, pr.x, pr.y, pr.z, pr.rot, pr.s);
    if (!geos.length) return;
    const merged = this.own(mergeGeometries(geos, false));
    geos.forEach((g) => g.dispose());
    merged.computeBoundingSphere();
    const mesh = new THREE.Mesh(merged, assets.cityMat);
    mesh.castShadow = true; mesh.receiveShadow = true;
    this.group.add(mesh);
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
