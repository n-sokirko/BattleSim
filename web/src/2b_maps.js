// ------------------------------------------------------------------ типы местности

const MAP_TYPES = [
  { key: 'field', name: 'Поле', note: 'холмы, овраг и каменные ограды' },
  { key: 'forest', name: 'Лес', note: 'рощи прячут отряды и глушат болты, кони вязнут в чаще' },
  { key: 'mountains', name: 'Горы', note: 'гряды с перевалами, крутые склоны не пройти' },
  { key: 'swamp', name: 'Болото', note: 'топи, островки и камыш — глубокую воду не перейти' },
  { key: 'city', name: 'Город', note: 'улицы, кварталы, площадь и баррикады' },
];

// ------------------------------------------------------------------ препятствия: дома (прямоугольники) и стволы (круги)

class Obstacles {
  constructor(field) {
    this.cell = 8; this.half = field + 12;
    this.dim = Math.ceil((this.half * 2) / this.cell);
    this.grid = Array.from({ length: this.dim * this.dim }, () => []);
    this.list = [];
  }
  cellRange(x, z, r) {
    const c = this.cell, h = this.half, d = this.dim;
    return [clamp(Math.floor((x - r + h) / c), 0, d - 1), clamp(Math.floor((x + r + h) / c), 0, d - 1),
            clamp(Math.floor((z - r + h) / c), 0, d - 1), clamp(Math.floor((z + r + h) / c), 0, d - 1)];
  }
  insert(o, r) {
    this.list.push(o);
    const [x0, x1, z0, z1] = this.cellRange(o.x, o.z, r);
    for (let z = z0; z <= z1; z++) for (let x = x0; x <= x1; x++) this.grid[z * this.dim + x].push(o);
  }
  /** Прямоугольник (дом): центр, полуразмеры, поворот, высота крыши над землёй. */
  addRect(x, z, hx, hz, ang, top, ground) { this.insert({ rect: true, x, z, hx, hz, c: Math.cos(ang), s: Math.sin(ang), top, ground }, Math.hypot(hx, hz)); }
  addCircle(x, z, r, top, ground) { this.insert({ rect: false, x, z, r, top, ground }, r); }
  near(x, z) {
    const c = this.cell, h = this.half, d = this.dim;
    const ix = Math.floor((x + h) / c), iz = Math.floor((z + h) / c);
    if (ix < 0 || iz < 0 || ix >= d || iz >= d) return null;
    return this.grid[iz * d + ix];
  }
  /** Точка внутри препятствия (с запасом pad)? Возвращает препятствие или null. */
  hit(x, z, pad = 0) {
    const L = this.near(x, z);
    if (!L) return null;
    for (const o of L) {
      const dx = x - o.x, dz = z - o.z;
      if (o.rect) {
        const lx = dx * o.c + dz * o.s, lz = -dx * o.s + dz * o.c;
        if (Math.abs(lx) < o.hx + pad && Math.abs(lz) < o.hz + pad) return o;
      } else if (dx * dx + dz * dz < (o.r + pad) ** 2) return o;
    }
    return null;
  }
  /** Выталкивает круг радиуса r из препятствий (солдат скользит вдоль стены). */
  pushOut(p, r) {
    const L = this.near(p.x, p.z);
    if (!L || !L.length) return;
    for (const o of L) {
      const dx = p.x - o.x, dz = p.z - o.z;
      if (o.rect) {
        let lx = dx * o.c + dz * o.s, lz = -dx * o.s + dz * o.c;
        const ex = o.hx + r, ez = o.hz + r;
        if (Math.abs(lx) >= ex || Math.abs(lz) >= ez) continue;
        if (ex - Math.abs(lx) < ez - Math.abs(lz)) lx = Math.sign(lx || 1) * ex; else lz = Math.sign(lz || 1) * ez;
        p.x = o.x + lx * o.c - lz * o.s;
        p.z = o.z + lx * o.s + lz * o.c;
      } else {
        const d = Math.hypot(dx, dz), m = o.r + r;
        if (d >= m) continue;
        const k = d > 1e-4 ? m / d : 0;
        p.x = o.x + (d > 1e-4 ? dx * k : m);
        p.z = o.z + dz * k;
      }
    }
  }
  /** Перекрывает ли препятствие точку на высоте y (для прямой видимости и болтов). */
  blocks(x, z, y) {
    const o = this.hit(x, z);
    return !!o && y < o.ground + o.top;
  }
}

// ------------------------------------------------------------------ навигация: сетка проходимости + A*

class MinHeap {
  constructor() { this.n = []; this.f = []; }
  get size() { return this.n.length; }
  clear() { this.n.length = 0; this.f.length = 0; }
  push(node, f) {
    const N = this.n, F = this.f;
    let i = N.length;
    N.push(node); F.push(f);
    while (i > 0) {
      const p = (i - 1) >> 1;
      if (F[p] <= f) break;
      N[i] = N[p]; F[i] = F[p]; i = p;
    }
    N[i] = node; F[i] = f;
  }
  pop() {
    const N = this.n, F = this.f, top = N[0], last = N.pop(), lf = F.pop();
    if (N.length) {
      let i = 0;
      for (;;) {
        const l = i * 2 + 1, r = l + 1;
        let m = i, mf = lf;
        if (l < N.length && F[l] < mf) { m = l; mf = F[l]; }
        if (r < N.length && F[r] < mf) { m = r; mf = F[r]; }
        if (m === i) break;
        N[i] = N[m]; F[i] = F[m]; i = m;
      }
      N[i] = last; F[i] = lf;
    }
    return top;
  }
}

/**
 * Сетка 2×2 м по полю боя: скорость движения в клетке для пехоты (0) и конницы (1),
 * 0 — не пройти (дом, обрыв, глубокая вода). По ней ищутся пути и замедляются войска.
 */
class NavGrid {
  constructor(world) {
    this.cell = 2; this.half = FIELD + 2;
    this.dim = Math.ceil((this.half * 2) / this.cell);
    const n = this.dim * this.dim;
    this.speed = [new Float32Array(n), new Float32Array(n)];
    this.conceal = new Uint8Array(n);
    this.canopy = new Uint8Array(n);
    this.blockedCount = 0;
    this.build(world);
    this.heap = new MinHeap();
    this.gs = new Float32Array(n); this.came = new Int32Array(n); this.seen = new Int32Array(n); this.done = new Int32Array(n); this.run = 0;
  }

  build(w) {
    const D = this.dim, c = this.cell;
    for (let iz = 0; iz < D; iz++) for (let ix = 0; ix < D; ix++) {
      const i = iz * D + ix, x = -this.half + (ix + 0.5) * c, z = -this.half + (iz + 0.5) * c;
      let si = 1, sc = 1, hide = 0, canopy = 0;
      const h = w.heightAt(x, z);
      const g = Math.max(Math.abs(w.heightAt(x + 1, z) - w.heightAt(x - 1, z)), Math.abs(w.heightAt(x, z + 1) - w.heightAt(x, z - 1))) / 2;
      if (g > 1.35) si = sc = 0;                                  // обрыв
      const depth = WATER - h;
      if (depth > 1.1) si = sc = 0;                               // глубокая вода
      else if (depth > 0.4) { si *= 0.35; sc *= 0.25; }           // брод
      else if (depth > -0.35) { si *= 0.6; sc *= 0.42; }          // топь у берега
      const f = w.forestAt(x, z);
      if (f > 0.5) { si *= 0.8; sc *= 0.5; hide = 1; canopy = 1; } // чаща
      else if (f > 0.3) { si *= 0.92; sc *= 0.75; }
      if (w.reedsAt(x, z)) hide = 1;                              // камыш
      const ob = w.obs.hit(x, z, 0.4);
      if (ob && (ob.rect || ob.r > 1)) si = sc = 0;                // дом, колодец (стволы обходят сами)
      this.speed[0][i] = si; this.speed[1][i] = sc;
      this.conceal[i] = hide; this.canopy[i] = canopy;
      if (si === 0) this.blockedCount++;
    }
  }

  idx(x, z) {
    const D = this.dim, ix = Math.floor((x + this.half) / this.cell), iz = Math.floor((z + this.half) / this.cell);
    return ix < 0 || iz < 0 || ix >= D || iz >= D ? -1 : iz * D + ix;
  }
  center(i) { const D = this.dim; return [-this.half + ((i % D) + 0.5) * this.cell, -this.half + (((i / D) | 0) + 0.5) * this.cell]; }
  speedAt(x, z, cls) { const i = this.idx(x, z); return i < 0 ? 1 : this.speed[cls][i]; }
  concealAt(x, z) { const i = this.idx(x, z); return i >= 0 && this.conceal[i] === 1; }
  canopyAt(x, z) { const i = this.idx(x, z); return i >= 0 && this.canopy[i] === 1; }

  lineClear(ax, az, bx, bz, cls) {
    const d = Math.hypot(bx - ax, bz - az), n = Math.max(1, Math.ceil(d / (this.cell * 0.5))), sp = this.speed[cls];
    for (let k = 1; k <= n; k++) {
      const t = k / n, i = this.idx(ax + (bx - ax) * t, az + (bz - az) * t);
      if (i >= 0 && sp[i] === 0) return false;
    }
    return true;
  }

  nearestOpen(i, sp) {
    const D = this.dim, x0 = i % D, z0 = (i / D) | 0;
    for (let r = 1; r < 8; r++)
      for (let dz = -r; dz <= r; dz++) for (let dx = -r; dx <= r; dx++) {
        if (Math.max(Math.abs(dx), Math.abs(dz)) !== r) continue;
        const x = x0 + dx, z = z0 + dz;
        if (x >= 0 && z >= 0 && x < D && z < D && sp[z * D + x] > 0) return z * D + x;
      }
    return -1;
  }

  /** A* по сетке; путь сглаживается до немногих точек поворота. */
  findPath(ax, az, bx, bz, cls) {
    const D = this.dim, sp = this.speed[cls];
    let s = this.idx(ax, az), g = this.idx(bx, bz);
    if (s < 0 || g < 0) return null;
    if (sp[s] === 0) s = this.nearestOpen(s, sp);
    const goalOpen = sp[g] > 0;
    if (!goalOpen) g = this.nearestOpen(g, sp);
    if (s < 0 || g < 0) return null;
    const run = ++this.run, gs = this.gs, came = this.came, seen = this.seen, done = this.done, heap = this.heap;
    const gx = g % D, gz = (g / D) | 0;
    const h = (i) => { const dx = Math.abs((i % D) - gx), dz = Math.abs(((i / D) | 0) - gz); return dx + dz - 0.5858 * Math.min(dx, dz); };
    heap.clear();
    gs[s] = 0; came[s] = -1; seen[s] = run; heap.push(s, h(s));
    let expanded = 0;
    while (heap.size && expanded < 25000) {
      const c = heap.pop();
      if (done[c] === run) continue;
      done[c] = run; expanded++;
      if (c === g) break;
      const cx = c % D, cz = (c / D) | 0;
      for (let dz = -1; dz <= 1; dz++) for (let dx = -1; dx <= 1; dx++) {
        if (!dx && !dz) continue;
        const nx = cx + dx, nz = cz + dz;
        if (nx < 0 || nz < 0 || nx >= D || nz >= D) continue;
        const ni = nz * D + nx;
        if (sp[ni] === 0 || done[ni] === run) continue;
        if (dx && dz && (sp[cz * D + nx] === 0 || sp[nz * D + cx] === 0)) continue; // не срезаем углы домов
        const ng = gs[c] + (dx && dz ? 1.4142 : 1) * (2 / (sp[c] + sp[ni]));
        if (seen[ni] !== run || ng < gs[ni]) { seen[ni] = run; gs[ni] = ng; came[ni] = c; heap.push(ni, ng + h(ni)); }
      }
    }
    if (done[g] !== run) return null;
    const cells = [];
    for (let c = g; c !== -1; c = came[c]) cells.push(c);
    cells.reverse();
    const pts = cells.map((i) => this.center(i));
    if (goalOpen) pts[pts.length - 1] = [bx, bz];
    const out = [pts[0]];
    for (let i = 0; i < pts.length - 1;) {
      let j = pts.length - 1;
      while (j > i + 1 && !this.lineClear(pts[i][0], pts[i][1], pts[j][0], pts[j][1], cls)) j--;
      out.push(pts[j]); i = j;
    }
    return out;
  }
}

// ------------------------------------------------------------------ город

/** Высота модели в метрах для каждого вида постройки. */
const CITY_HEIGHTS = { home: 7.5, church: 15, tavern: 9.5, blacksmith: 8, market: 7, tower_B: 13, destroyed: 6, well: 3.2 };

/**
 * План города: сетка улиц, кварталы с домами по краям, площадь с церковью,
 * рынком и колодцем, сады, баррикады поперёк улиц (с проёмами) и мелочи.
 */
function makeTown(R, defs) {
  const TX = FIELD * 0.78, TZ = FIELD * 0.42, W = 6.5;
  const nx = Math.max(3, Math.round((TX * 2) / 27)), nz = Math.max(2, Math.round((TZ * 2) / 25));
  const xs = [...Array(nx + 1)].map((_, i) => -TX + (i * TX * 2) / nx);
  const zs = [...Array(nz + 1)].map((_, i) => -TZ + (i * TZ * 2) / nz);
  const town = { TX, TZ, W, streets: [], buildings: [], props: [], walls: [], squares: [], gardens: [] };
  for (const x of xs) town.streets.push({ ax: x, az: -TZ - 10, bx: x, bz: TZ + 10, w: W });
  for (const z of zs) town.streets.push({ ax: -TX - 10, az: z, bx: TX + 10, bz: z, w: W });

  const homes = defs.filter((d) => d.kind === 'home'), extra = defs.filter((d) => ['tavern', 'blacksmith', 'destroyed', 'tower_B'].includes(d.kind));
  const byKind = (k) => defs.find((d) => d.kind === k);
  const place = (def, x, z, rot) => {
    const s = CITY_HEIGHTS[def.kind] / def.h, turned = Math.abs(Math.sin(rot)) > 0.5;
    town.buildings.push({ def, x, z, rot, s, hx: (turned ? def.d : def.w) * s * 0.46, hz: (turned ? def.w : def.d) * s * 0.46, top: CITY_HEIGHTS[def.kind] * 0.9 });
  };
  const cix = Math.floor(nx / 2), ciz = Math.floor(nz / 2);

  for (let i = 0; i < nx; i++) for (let j = 0; j < nz; j++) {
    const x0 = xs[i] + W / 2, x1 = xs[i + 1] - W / 2, z0 = zs[j] + W / 2, z1 = zs[j + 1] - W / 2;
    const cx = (x0 + x1) / 2, cz = (z0 + z1) / 2;
    if (i === cix && j === ciz) {
      // Площадь: церковь у края, колодец в центре, рынок сбоку
      town.squares.push({ x: cx, z: cz, hx: (x1 - x0) / 2, hz: (z1 - z0) / 2 });
      const ch = byKind('church');
      if (ch) place(ch, cx, z1 - ch.d * (CITY_HEIGHTS.church / ch.h) * 0.5 - 0.5, 0);
      const wl = byKind('well');
      if (wl) place(wl, cx, cz - 2, 0);
      const mk = byKind('market');
      if (mk) place(mk, x0 + mk.w * (CITY_HEIGHTS.market / mk.h) * 0.5 + 1, z0 + mk.d * (CITY_HEIGHTS.market / mk.h) * 0.5 + 1, Math.PI);
      continue;
    }
    if (R() < 0.14) { town.gardens.push({ x0, x1, z0, z1 }); continue; } // сад с оградой
    // Дома вдоль четырёх сторон квартала, фасадом на улицу
    const sides = [
      { ax: x0, az: z1, bx: x1, bz: z1, rot: 0, inX: 0, inZ: -1 },
      { ax: x0, az: z0, bx: x1, bz: z0, rot: Math.PI, inX: 0, inZ: 1 },
      { ax: x1, az: z0, bx: x1, bz: z1, rot: Math.PI / 2, inX: -1, inZ: 0 },
      { ax: x0, az: z0, bx: x0, bz: z1, rot: -Math.PI / 2, inX: 1, inZ: 0 },
    ];
    for (let k = 0; k < 4; k++) {
      const sd = sides[k], len = Math.hypot(sd.bx - sd.ax, sd.bz - sd.az), ux = (sd.bx - sd.ax) / len, uz = (sd.bz - sd.az) / len;
      const margin = k >= 2 ? 7.5 : 0; // боковые стороны короче: углы заняты
      let t = margin + R() * 1.5;
      while (t < len - margin - 4) {
        const def = R() < 0.12 && extra.length ? extra[(R() * extra.length) | 0] : homes[(R() * homes.length) | 0];
        const s = CITY_HEIGHTS[def.kind] / def.h, fw = def.w * s, fd = def.d * s;
        if (t + fw > len - margin) break;
        const along = t + fw / 2, depth = fd / 2 + 0.4;
        place(def, sd.ax + ux * along + sd.inX * depth, sd.az + uz * along + sd.inZ * depth, sd.rot);
        t += fw + 0.6 + R() * 2.2;
      }
    }
  }

  // Баррикады поперёк улиц: две секции с проёмом посередине
  const nBar = Math.round(nx * nz * 0.45);
  for (let b = 0; b < nBar; b++) {
    const vertical = R() < 0.5;
    if (vertical) {
      const x = xs[(R() * xs.length) | 0], j = (R() * nz) | 0, z = (zs[j] + zs[j + 1]) / 2 + (R() - 0.5) * 6;
      town.walls.push({ ax: x - W / 2, az: z, bx: x - 0.9, bz: z, h: 1.3, t: 0.55 }, { ax: x + 0.9, az: z, bx: x + W / 2, bz: z, h: 1.3, t: 0.55 });
    } else {
      const z = zs[(R() * zs.length) | 0], i = (R() * nx) | 0, x = (xs[i] + xs[i + 1]) / 2 + (R() - 0.5) * 8;
      town.walls.push({ ax: x, az: z - W / 2, bx: x, bz: z - 0.9, h: 1.3, t: 0.55 }, { ax: x, az: z + 0.9, bx: x, bz: z + W / 2, h: 1.3, t: 0.55 });
    }
  }
  // Сады: низкая ограда по периметру с проходами
  for (const g of town.gardens) {
    const pts = [[g.x0, g.z0], [g.x1, g.z0], [g.x1, g.z1], [g.x0, g.z1], [g.x0, g.z0]];
    for (let k = 0; k < 4; k++) {
      const [ax, az] = pts[k], [bx, bz] = pts[k + 1], mx = (ax + bx) / 2, mz = (az + bz) / 2;
      const gx = (bx - ax) / Math.hypot(bx - ax, bz - az) * 1.2, gz = (bz - az) / Math.hypot(bx - ax, bz - az) * 1.2;
      town.walls.push({ ax, az, bx: mx - gx, bz: mz - gz, h: 1.0, t: 0.45 }, { ax: mx + gx, az: mz + gz, bx, bz, h: 1.0, t: 0.45 });
    }
  }
  // Бочки, ящики, тачки вдоль улиц
  const props = defs.filter((d) => d.kind === 'prop');
  for (let k = 0; k < nx * nz * 5 && props.length; k++) {
    const st = town.streets[(R() * town.streets.length) | 0], t = R();
    const x = lerp(st.ax, st.bx, t), z = lerp(st.az, st.bz, t), side = R() < 0.5 ? -1 : 1;
    const px = st.ax === st.bx ? x + side * (W / 2 - 0.7) : x, pz = st.ax === st.bx ? z : z + side * (W / 2 - 0.7);
    if (Math.abs(px) > TX || Math.abs(pz) > TZ) continue;
    town.props.push({ def: props[(R() * props.length) | 0], x: px, z: pz, rot: R() * Math.PI * 2 });
  }
  return town;
}
