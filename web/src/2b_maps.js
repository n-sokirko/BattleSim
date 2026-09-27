// ------------------------------------------------------------------ типы местности

const MAP_TYPES = [
  { key: 'field', name: 'Поле', note: 'холмы, овраг и каменные ограды' },
  { key: 'forest', name: 'Лес', note: 'рощи прячут отряды и глушат болты, кони вязнут в чаще' },
  { key: 'mountains', name: 'Горы', note: 'террасы и обрывы, серпантины, ущелье с рекой и мосты' },
  { key: 'swamp', name: 'Болото', note: 'топи, островки и камыш — глубокую воду не перейти' },
  { key: 'city', name: 'Город', note: 'крепостные стены с башнями и воротами, детинец на холме, тесные кварталы' },
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


// ------------------------------------------------------------------ настилы: мосты, стены, лестницы, башни

/**
 * Всё, что выше земли: мост над ущельем, крепостная стена с боевым ходом поверху,
 * лестница-пандус от земли до боевого хода, площадка башни.
 * Сегмент от A до B шириной w; высота настила hA→hB — абсолютная или над рельефом (rel).
 * walk — по нему ходят; solid — сплошная кладка до земли (закрывает обзор и болты).
 */
class Decks {
  constructor(field) { this.grid = new Obstacles(field); this.list = []; }

  add(d) {
    d.len = Math.hypot(d.bx - d.ax, d.bz - d.az) || 0.01;
    d.ux = (d.bx - d.ax) / d.len; d.uz = (d.bz - d.az) / d.len;
    d.x = (d.ax + d.bx) / 2; d.z = (d.az + d.bz) / 2;
    this.list.push(d);
    this.grid.insert(d, d.len / 2 + d.w);
    return d;
  }

  /** Положение точки на настиле: t вдоль (0..1) и боковое смещение; null — мимо. */
  locate(d, x, z) {
    const px = x - d.ax, pz = z - d.az, along = px * d.ux + pz * d.uz;
    if (along < -0.05 || along > d.len + 0.05) return null;
    const lat = -px * d.uz + pz * d.ux;
    return Math.abs(lat) <= d.w / 2 ? { t: clamp(along / d.len, 0, 1), lat } : null;
  }

  heightOf(d, t, ground) { const h = d.hA + (d.hB - d.hA) * t; return d.rel ? ground + h : h; }

  /** Высота настила, по которому можно пройти в точке (или -Infinity). */
  surface(x, z, ground) {
    const L = this.grid.near(x, z);
    let best = -Infinity;
    if (L) for (const d of L) {
      if (!d.walk) continue;
      const p = this.locate(d, x, z);
      if (p) best = Math.max(best, this.heightOf(d, p.t, ground));
    }
    return best;
  }

  /** Верх сплошной кладки в точке (с зубцами на внешнем краю стены) — для обзора и болтов. */
  solidTop(x, z, ground) {
    const L = this.grid.near(x, z);
    let best = -Infinity;
    if (L) for (const d of L) {
      if (!d.solid) continue;
      const p = this.locate(d, x, z);
      if (!p) continue;
      let h = this.heightOf(d, p.t, ground);
      if (d.parapet && p.lat * (d.outSign || 0) > d.w * 0.25) h += d.parapet;
      best = Math.max(best, h);
    }
    return best;
  }

  /** Тонкий настил моста перекрывает точку на высоте y? */
  thinBlocks(x, z, y) {
    const L = this.grid.near(x, z);
    if (L) for (const d of L) {
      if (d.solid || !d.walk) continue;
      const p = this.locate(d, x, z);
      if (p) { const h = this.heightOf(d, p.t, 0); if (y < h && y > h - 0.7) return true; }
    }
    return false;
  }
}

// ------------------------------------------------------------------ навигация: многоуровневая сетка + A*

/**
 * Сетка 1,5×1,5 м: высота поверхности (земля или настил), скорость для пехоты (0)
 * и конницы (1), 0 — не пройти. Между соседними клетками можно шагнуть, только
 * если перепад высот не больше ~1,5 м: так обрывы, стены и берега ущелья
 * непроходимы, а лестницы, пандусы и тропы — проходимы.
 */
class NavGrid {
  constructor(world) {
    this.cell = 1.5; this.half = FIELD + 2;
    this.dim = Math.ceil((this.half * 2) / this.cell);
    const n = this.dim * this.dim;
    this.speed = [new Float32Array(n), new Float32Array(n)];
    this.surf = new Float32Array(n);
    this.conceal = new Uint8Array(n);
    this.canopy = new Uint8Array(n);
    this.barriers = 0;
    this.build(world);
    this.heap = new MinHeap();
    this.gs = new Float32Array(n); this.came = new Int32Array(n); this.seen = new Int32Array(n); this.done = new Int32Array(n); this.run = 0;
  }

  build(w) {
    const D = this.dim, c = this.cell;
    for (let iz = 0; iz < D; iz++) for (let ix = 0; ix < D; ix++) {
      const i = iz * D + ix, x = -this.half + (ix + 0.5) * c, z = -this.half + (iz + 0.5) * c;
      let si = 1, sc = 1, hide = 0, canopy = 0;
      const g = w.heightAt(x, z), deck = w.decks.surface(x, z, g), onDeck = deck > g + 0.05;
      this.surf[i] = onDeck ? deck : g;
      if (!onDeck) {
        const depth = WATER - g;
        if (depth > 1.1) si = sc = 0;                               // глубокая вода
        else if (depth > 0.4) { si *= 0.35; sc *= 0.25; }           // брод
        else if (depth > -0.35) { si *= 0.6; sc *= 0.42; }          // топь у берега
        const f = w.forestAt(x, z);
        if (f > 0.5) { si *= 0.8; sc *= 0.5; hide = 1; canopy = 1; } // чаща
        else if (f > 0.3) { si *= 0.92; sc *= 0.75; }
        if (w.reedsAt(x, z)) hide = 1;                              // камыш
        if (w.decks.solidTop(x, z, g) > g + 1) si = sc = 0;         // сплошная кладка без хода
      }
      const ob = w.obs.hit(x, z, 0.4);
      if (ob && (ob.rect || ob.r > 1)) si = sc = 0;                // дом, колодец, башня
      this.speed[0][i] = si; this.speed[1][i] = sc;
      this.conceal[i] = hide; this.canopy[i] = canopy;
    }
    let b = 0;
    for (let iz = 0; iz < D; iz++) for (let ix = 0; ix < D; ix++) {
      const i = iz * D + ix;
      if (this.speed[0][i] === 0) { b++; continue; }
      if (ix + 1 < D && !this.step(i, i + 1, false)) b++;
      if (iz + 1 < D && !this.step(i, i + D, false)) b++;
    }
    this.barriers = b;
  }

  /** Можно ли шагнуть между соседними клетками по высоте. */
  step(a, b, diag) { return Math.abs(this.surf[a] - this.surf[b]) <= (diag ? 2.1 : 1.5); }

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
    let prev = this.idx(ax, az);
    for (let k = 1; k <= n; k++) {
      const t = k / n, i = this.idx(ax + (bx - ax) * t, az + (bz - az) * t);
      if (i < 0 || i === prev) continue;
      if (sp[i] === 0 || (prev >= 0 && !this.step(prev, i, true))) return false;
      prev = i;
    }
    return true;
  }

  nearestOpen(i, sp) {
    const D = this.dim, x0 = i % D, z0 = (i / D) | 0;
    for (let r = 1; r < 10; r++)
      for (let dz = -r; dz <= r; dz++) for (let dx = -r; dx <= r; dx++) {
        if (Math.max(Math.abs(dx), Math.abs(dz)) !== r) continue;
        const x = x0 + dx, z = z0 + dz;
        if (x >= 0 && z >= 0 && x < D && z < D && sp[z * D + x] > 0) return z * D + x;
      }
    return -1;
  }

  /** A* по сетке с учётом перепадов высот; путь сглаживается до точек поворота. */
  findPath(ax, az, bx, bz, cls) {
    const D = this.dim, sp = this.speed[cls], S = this.surf;
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
    while (heap.size && expanded < 40000) {
      const c = heap.pop();
      if (done[c] === run) continue;
      done[c] = run; expanded++;
      if (c === g) break;
      const cx = c % D, cz = (c / D) | 0;
      for (let dz = -1; dz <= 1; dz++) for (let dx = -1; dx <= 1; dx++) {
        if (!dx && !dz) continue;
        const nx = cx + dx, nz = cz + dz;
        if (nx < 0 || nz < 0 || nx >= D || nz >= D) continue;
        const ni = nz * D + nx, diag = dx !== 0 && dz !== 0;
        if (sp[ni] === 0 || done[ni] === run || !this.step(c, ni, diag)) continue;
        if (diag) { // не срезаем углы домов и обрывов
          const a = cz * D + nx, b2 = nz * D + cx;
          if (sp[a] === 0 || sp[b2] === 0 || !this.step(c, a, false) || !this.step(c, b2, false)) continue;
        }
        const ng = gs[c] + (diag ? 1.4142 : 1) * (2 / (sp[c] + sp[ni])) + Math.abs(S[ni] - S[c]) * 0.4;
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
      let j = Math.min(pts.length - 1, i + 24);
      while (j > i + 1 && !this.lineClear(pts[i][0], pts[i][1], pts[j][0], pts[j][1], cls)) j--;
      out.push(pts[j]); i = j;
    }
    return out;
  }
}

// ------------------------------------------------------------------ сплайн для извилистых дорог

/** Кривая Катмулла-Рома через контрольные точки, шаг ~step метров. */
function catmull(ctrl, step = 1) {
  const out = [];
  const P = [ctrl[0], ...ctrl, ctrl[ctrl.length - 1]];
  for (let i = 1; i < P.length - 2; i++) {
    const p0 = P[i - 1], p1 = P[i], p2 = P[i + 1], p3 = P[i + 2];
    const n = Math.max(2, Math.ceil(Math.hypot(p2[0] - p1[0], p2[1] - p1[1]) / step));
    for (let k = 0; k < n; k++) {
      const t = k / n, t2 = t * t, t3 = t2 * t;
      const f = (a, b, c, d) => 0.5 * (2 * b + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t2 + (-a + 3 * b - 3 * c + d) * t3);
      out.push([f(p0[0], p1[0], p2[0], p3[0]), f(p0[1], p1[1], p2[1], p3[1])]);
    }
  }
  out.push(ctrl[ctrl.length - 1]);
  return out;
}

// ------------------------------------------------------------------ горы

/**
 * Горный перевал: армии стоят в долинах у краёв, между ними — массив террасами
 * с обрывами, посередине — ущелье с рекой. Через массив вьются серпантины к мостам.
 */
function makeMountainPlan(R) {
  const F = FIELD, gw = lerp(12, 16, R()), z0 = lerp(-5, 5, R()), amp = lerp(4, 8, R()), ph = R() * 6, fr = lerp(0.035, 0.06, R());
  const gz = (x) => z0 + Math.sin(x * fr + ph) * amp;
  const gorge = { w: gw, pts: [...Array(41)].map((_, i) => { const x = -F * 1.3 + (i * F * 2.6) / 40; return [x, gz(x)]; }) };
  const nb = F > 100 ? 3 : 2 + (R() < 0.5 ? 1 : 0);
  const bridges = [], paths = [];
  for (let k = 0; k < nb; k++) {
    const bx = (k - (nb - 1) / 2) * ((F * 1.3) / (nb - 1)) + lerp(-6, 6, R());
    const rim = gw / 2 + 1.5, zc = gz(bx), tilt = lerp(-3, 3, R());
    const a = [bx - tilt, zc - rim], b = [bx + tilt, zc + rim];
    const br = { a, b, paths: [] };
    bridges.push(br);
    for (const [side, end] of [[-1, a], [1, b]]) {
      const start = [clamp(end[0] + lerp(-18, 18, R()), -F + 8, F - 8), side * F * 0.66];
      const n = 4 + ((R() * 3) | 0), ctrl = [start];
      for (let j = 1; j < n; j++) {
        const t = j / n, zig = (j % 2 ? 1 : -1) * lerp(9, 17, R()) * Math.sin(Math.PI * t);
        ctrl.push([clamp(lerp(start[0], end[0], t) + zig, -F + 6, F - 6), lerp(start[1], end[1], t)]);
      }
      ctrl.push([end[0], end[1] - side * 3]);
      ctrl.push(end);
      const p = { ctrl, w: 4.6, side, bridge: br };
      paths.push(p);
      br.paths.push(p);
    }
  }
  return { gorge, gz, bridges, paths };
}

// ------------------------------------------------------------------ город

/** Высота модели в метрах для каждого вида постройки. */
const CITY_HEIGHTS = { home: 8.2, church: 17, tavern: 9.5, blacksmith: 8.5, market: 7, tower_B: 15, destroyed: 7, well: 3.2, prop: 1.1 };

/**
 * Крепость-город: стена с башнями, воротами и проломами, лестницы на боевой ход,
 * детинец на насыпном холме со своей стеной и въездом, кварталы и переулки,
 * площадь с рынком и колодцем, баррикады у ворот.
 */
function makeCity(R, defs) {
  const CX = FIELD * 0.8, CZ = FIELD * 0.55, T = 3.2, H = 7;
  const city = { CX, CZ, T, H, TX: CX, TZ: CZ, W: 5, streets: [], squares: [], buildings: [], props: [], walls: [], gardens: [],
    fort: { walls: [], towers: [], ramps: [], arches: [] }, citadel: null, road: null };
  const F = city.fort;

  // --- детинец: насыпной холм у одной из боковых стен
  const cs = R() < 0.5 ? -1 : 1, cw = lerp(24, 30, R()), cd = lerp(26, 34, R());
  const cz0 = lerp(-CZ * 0.25, CZ * 0.25, R());
  const xa = cs * (CX - 10), xb = cs * (CX - 10 - cw);
  city.citadel = { x0: Math.min(xa, xb), x1: Math.max(xa, xb), z0: cz0 - cd / 2, z1: cz0 + cd / 2, h: 6, H: 5.5, side: cs, cz: cz0 };
  const cit = city.citadel;
  const gateX = cs > 0 ? cit.x0 : cit.x1; // ворота детинца смотрят к центру города
  city.road = { ax: gateX - cs * 16, az: cz0, bx: gateX, bz: cz0, w: 5 }; // въезд-пандус на холм

  // --- внешняя стена: башни, ворота, проломы
  const gx = lerp(-CX * 0.25, CX * 0.25, R());
  const sides = [
    { a: [-CX, -CZ], b: [CX, -CZ], gates: [gx], out: [0, -1] },
    { a: [CX, -CZ], b: [CX, CZ], gates: cs < 0 && R() < 0.6 ? [lerp(-CZ * 0.3, CZ * 0.3, R())] : [], out: [1, 0] },
    { a: [CX, CZ], b: [-CX, CZ], gates: [gx], out: [0, 1] },
    { a: [-CX, CZ], b: [-CX, -CZ], gates: cs > 0 && R() < 0.6 ? [lerp(-CZ * 0.3, CZ * 0.3, R())] : [], out: [-1, 0] },
  ];
  const tower = (x, z, s = 6.2) => F.towers.push({ x, z, s, h: H + 1 });
  let breaches = 1 + (R() < 0.5 ? 1 : 0);
  for (const sd of sides) {
    const [ax, az] = sd.a, [bx, bz] = sd.b, len = Math.hypot(bx - ax, bz - az), ux = (bx - ax) / len, uz = (bz - az) / len;
    const gatesT = sd.gates.map((g) => (sd.out[0] ? (g - az) / uz : (g - ax) / ux));
    const cuts = []; // промежутки без стены
    for (const t of gatesT) {
      cuts.push([t - 3.2, t + 3.2]);
      tower(ax + ux * (t - 6.3), az + uz * (t - 6.3));
      tower(ax + ux * (t + 6.3), az + uz * (t + 6.3));
      F.arches.push({ x: ax + ux * t, z: az + uz * t, ux, uz, h: H, w: T });
    }
    tower(ax, az, 7);
    const nT = Math.max(1, Math.round(len / 26));
    for (let k = 1; k < nT; k++) {
      const t = (k * len) / nT;
      if (gatesT.some((g) => Math.abs(g - t) < 11)) continue;
      tower(ax + ux * t, az + uz * t);
    }
    if (breaches > 0 && R() < 0.45) {
      const t = lerp(len * 0.2, len * 0.8, R());
      if (!gatesT.some((g) => Math.abs(g - t) < 16)) {
        cuts.push([t - 3, t + 3]); breaches--;
        city.walls.push({ ax: ax + ux * (t - 2.5), az: az + uz * (t - 2.5), bx: ax + ux * (t + 2.5), bz: az + uz * (t + 2.5), h: 1.1, t: 1.4 }); // завал в проломе
      }
    }
    cuts.sort((p, q) => p[0] - q[0]);
    let t0 = 0;
    for (const [c0, c1] of [...cuts, [len, len]]) {
      if (c0 - t0 > 0.5) F.walls.push({ ax: ax + ux * t0, az: az + uz * t0, bx: ax + ux * c0, bz: az + uz * c0, w: T, h: H, out: sd.out });
      t0 = c1;
    }
    // Лестницы на боевой ход: у ворот и в середине стороны, вдоль внутренней стороны стены
    const inX = -sd.out[0], inZ = -sd.out[1], off = T / 2 + 1.2; // лестница заходит на стену, без щели
    const rampTs = gatesT.length ? gatesT.map((g) => g + 5.5) : [len * lerp(0.3, 0.7, R())];
    for (const t of rampTs) {
      const ta = clamp(t, 8, len - 20), tb = ta + 11;
      if (cuts.some(([c0, c1]) => tb > c0 - 1 && ta < c1 + 1)) continue;
      F.ramps.push({ ax: ax + ux * ta + inX * off, az: az + uz * ta + inZ * off, bx: ax + ux * tb + inX * off, bz: az + uz * tb + inZ * off, w: 2.8, h: H });
    }
  }

  // --- стена детинца (на холме, ниже внешней)
  // стены чуть внутри края холма, чтобы стояли на ровном
  const ci = { x0: cit.x0 + 1.4, x1: cit.x1 - 1.4, z0: cit.z0 + 1.4, z1: cit.z1 - 1.4 };
  const gateXi = cs > 0 ? ci.x0 : ci.x1;
  const cp = [[ci.x0, ci.z0], [ci.x1, ci.z0], [ci.x1, ci.z1], [ci.x0, ci.z1], [ci.x0, ci.z0]];
  const ccx = (cit.x0 + cit.x1) / 2;
  for (let k = 0; k < 4; k++) {
    const [ax, az] = cp[k], [bx, bz] = cp[k + 1], len = Math.hypot(bx - ax, bz - az), ux = (bx - ax) / len, uz = (bz - az) / len;
    const out = Math.abs(ux) > 0.5 ? [0, Math.sign((az + bz) / 2 - cz0)] : [Math.sign((ax + bx) / 2 - ccx), 0];
    F.towers.push({ x: ax, z: az, s: 5.2, h: cit.H + 1, citadel: true });
    const isGateSide = Math.abs(ax - gateXi) < 0.1 && Math.abs(bx - gateXi) < 0.1;
    if (isGateSide) {
      const tg = Math.abs(cz0 - az);
      F.walls.push({ ax, az, bx: ax + ux * (tg - 3), bz: az + uz * (tg - 3), w: 2.6, h: cit.H, out, citadel: true });
      F.walls.push({ ax: ax + ux * (tg + 3), az: az + uz * (tg + 3), bx, bz, w: 2.6, h: cit.H, out, citadel: true });
      F.arches.push({ x: gateXi, z: cz0, ux, uz, h: cit.H, w: 2.6, citadel: true });
    } else F.walls.push({ ax, az, bx, bz, w: 2.6, h: cit.H, out, citadel: true });
    // лестница на стену детинца изнутри
    if (k === 0 || k === 2) {
      const inZ = -out[1], off = 2.6 / 2 + 1.15, t0 = len * 0.25;
      F.ramps.push({ ax: ax + ux * t0, az: az + inZ * off, bx: ax + ux * (t0 + 9), bz: az + inZ * off, w: 2.6, h: cit.H, citadel: true });
    }
  }

  // --- улицы: главная (от ворот до ворот), поперечная, кольцевая у стен и кварталы делением
  const inner = { x0: -CX + 9.5, x1: CX - 9.5, z0: -CZ + 9.5, z1: CZ - 9.5 };
  const crossZ = lerp(-CZ * 0.2, CZ * 0.2, R());
  city.streets.push({ ax: gx, az: -CZ - 12, bx: gx, bz: CZ + 12, w: 8 });
  city.streets.push({ ax: -CX, az: crossZ, bx: CX, bz: crossZ, w: 6.5 });
  const ring = [[inner.x0 - 2.5, inner.z0 - 2.5], [inner.x1 + 2.5, inner.z0 - 2.5], [inner.x1 + 2.5, inner.z1 + 2.5], [inner.x0 - 2.5, inner.z1 + 2.5], [inner.x0 - 2.5, inner.z0 - 2.5]];
  for (let k = 0; k < 4; k++) city.streets.push({ ax: ring[k][0], az: ring[k][1], bx: ring[k + 1][0], bz: ring[k + 1][1], w: 4.5 });
  for (const sd of sides) for (const g of sd.gates) if (sd.out[0]) city.streets.push({ ax: sd.a[0], az: g, bx: 0, bz: g, w: 6 });
  city.streets.push({ ...city.road });
  const sq = { x: gx + lerp(-4, 4, R()), z: crossZ, hx: lerp(8.5, 11, R()), hz: lerp(6.5, 8.5, R()) };
  city.squares.push(sq);

  const blocks = [];
  const split = (r, depth) => {
    const w = r.x1 - r.x0, h = r.z1 - r.z0;
    if ((w < 24 && h < 24) || depth > 6) { blocks.push(r); return; }
    const sw = depth < 2 ? 5 : 3.6, f = lerp(0.38, 0.62, R());
    if (w >= h) {
      const x = r.x0 + w * f;
      city.streets.push({ ax: x, az: r.z0 - 1, bx: x, bz: r.z1 + 1, w: sw });
      split({ x0: r.x0, x1: x - sw / 2, z0: r.z0, z1: r.z1 }, depth + 1);
      split({ x0: x + sw / 2, x1: r.x1, z0: r.z0, z1: r.z1 }, depth + 1);
    } else {
      const z = r.z0 + h * f;
      city.streets.push({ ax: r.x0 - 1, az: z, bx: r.x1 + 1, bz: z, w: sw });
      split({ x0: r.x0, x1: r.x1, z0: r.z0, z1: z - sw / 2 }, depth + 1);
      split({ x0: r.x0, x1: r.x1, z0: z + sw / 2, z1: r.z1 }, depth + 1);
    }
  };
  for (const [x0, x1] of [[inner.x0, gx - 4], [gx + 4, inner.x1]])
    for (const [z0, z1] of [[inner.z0, crossZ - 3.25], [crossZ + 3.25, inner.z1]])
      if (x1 - x0 > 8 && z1 - z0 > 8) split({ x0, x1, z0, z1 }, 1);

  const overlaps = (r, o, m) => r.x0 < o.x1 + m && r.x1 > o.x0 - m && r.z0 < o.z1 + m && r.z1 > o.z0 - m;
  const road = { x0: Math.min(city.road.ax, city.road.bx), x1: Math.max(city.road.ax, city.road.bx), z0: cz0 - 3, z1: cz0 + 3 };
  const sqr = { x0: sq.x - sq.hx, x1: sq.x + sq.hx, z0: sq.z - sq.hz, z1: sq.z + sq.hz };

  const homes = defs.filter((d) => d.kind === 'home'), extra = defs.filter((d) => ['tavern', 'blacksmith', 'destroyed', 'tower_B'].includes(d.kind));
  const byKind = (k) => defs.find((d) => d.kind === k);
  const place = (def, x, z, rot) => {
    const s = CITY_HEIGHTS[def.kind] / def.h, turned = Math.abs(Math.sin(rot)) > 0.5;
    city.buildings.push({ def, x, z, rot, s, hx: (turned ? def.d : def.w) * s * 0.46, hz: (turned ? def.w : def.d) * s * 0.46, top: CITY_HEIGHTS[def.kind] * 0.9 });
  };
  /** Дома вдоль всех сторон квартала фасадом на улицу; дом, наезжающий на соседа, пропускаем. */
  const fillBlock = (b) => {
    const sb = [
      { ax: b.x0, az: b.z1, bx: b.x1, bz: b.z1, rot: 0, inX: 0, inZ: -1 },
      { ax: b.x0, az: b.z0, bx: b.x1, bz: b.z0, rot: Math.PI, inX: 0, inZ: 1 },
      { ax: b.x1, az: b.z0, bx: b.x1, bz: b.z1, rot: Math.PI / 2, inX: -1, inZ: 0 },
      { ax: b.x0, az: b.z0, bx: b.x0, bz: b.z1, rot: -Math.PI / 2, inX: 1, inZ: 0 },
    ];
    const mine = [];
    const clash = (x, z, hx, hz) => mine.some((o) => Math.abs(o.x - x) < o.hx + hx + 0.15 && Math.abs(o.z - z) < o.hz + hz + 0.15);
    for (const sd of sb) {
      const len = Math.hypot(sd.bx - sd.ax, sd.bz - sd.az), ux = (sd.bx - sd.ax) / len, uz = (sd.bz - sd.az) / len;
      const blockDepth = sd.inX ? b.x1 - b.x0 : b.z1 - b.z0;
      let t = R() * 0.6;
      while (t < len - 3) {
        const def = R() < 0.1 && extra.length ? extra[(R() * extra.length) | 0] : homes[(R() * homes.length) | 0];
        const s2 = CITY_HEIGHTS[def.kind] / def.h, fw = def.w * s2, fd = def.d * s2;
        if (t + fw > len + 0.5) break;
        if (fd > blockDepth - 0.4) { t += 1.5; continue; }
        const along = t + fw / 2, depth = fd / 2 + 0.3;
        const x = sd.ax + ux * along + sd.inX * depth, z = sd.az + uz * along + sd.inZ * depth;
        const turned = Math.abs(Math.sin(sd.rot)) > 0.5, hx = (turned ? fd : fw) / 2, hz = (turned ? fw : fd) / 2;
        if (clash(x, z, hx, hz)) { t += 1.5; continue; }
        mine.push({ x, z, hx, hz });
        place(def, x, z, sd.rot);
        t += fw + 0.2 + R() * 0.7; // дома стоят стена к стене — это город, а не деревня
      }
    }
  };
  for (const b of blocks) {
    if (R() < 0.06 && !overlaps(b, cit, 3)) { city.gardens.push(b); continue; }
    fillBlock(b);
  }
  city.debug = { blocks: blocks.length, placed: city.buildings.length };
  // Дома, попавшие на холм детинца, въезд или площадь, убираем по одному
  const rectOf = (bd) => ({ x0: bd.x - bd.hx, x1: bd.x + bd.hx, z0: bd.z - bd.hz, z1: bd.z + bd.hz });
  city.buildings = city.buildings.filter((bd) => { const r = rectOf(bd); return !overlaps(r, cit, 3) && !overlaps(r, road, 1.5) && !overlaps(r, sqr, 0.5); });
  city.debug.afterAreas = city.buildings.length;
  // Площадь: колодец и рынок; в детинце — собор, башня и терема
  const wl = byKind('well'); if (wl) place(wl, sq.x, sq.z, 0);
  const mk = byKind('market'); if (mk) place(mk, sq.x + sq.hx * 0.55, sq.z - sq.hz * 0.4, Math.PI);
  const ch = byKind('church'); if (ch) place(ch, (cit.x0 + cit.x1) / 2 + cs * 3, cz0, cs > 0 ? -Math.PI / 2 : Math.PI / 2);
  const tb = byKind('tower_B'); if (tb) place(tb, cs > 0 ? cit.x1 - 7 : cit.x0 + 7, cit.z1 - 7, 0);
  for (let k = 0; k < 2; k++) { const d = homes[(R() * homes.length) | 0]; place(d, lerp(cit.x0 + 8, cit.x1 - 8, R()), k ? cit.z0 + 6.5 : cit.z1 - 6.5, k ? Math.PI : 0); }
  // Дома не должны залезать на улицы, площадь и въезд
  city.buildings = city.buildings.filter((bd) =>
    !city.streets.some((st) => segDist(bd.x, bd.z, st.ax, st.az, st.bx, st.bz) < st.w / 2 + Math.min(bd.hx, bd.hz) * 0.6));

  city.debug.final = city.buildings.length;
  // Баррикады на главной улице у ворот и в переулках
  for (const dz of [-1, 1]) {
    const z = dz * (CZ - 16);
    city.walls.push({ ax: gx - 4, az: z, bx: gx - 1, bz: z, h: 1.3, t: 0.6 }, { ax: gx + 1, az: z, bx: gx + 4, bz: z, h: 1.3, t: 0.6 });
  }
  for (let k = 0; k < 6; k++) {
    const st = city.streets[4 + ((R() * (city.streets.length - 4)) | 0)];
    if (!st || st.w > 5.5) continue;
    const t = lerp(0.3, 0.7, R()), x = lerp(st.ax, st.bx, t), z = lerp(st.az, st.bz, t), vx = Math.abs(st.bx - st.ax) < 0.1 ? 1 : 0, vz = 1 - vx;
    city.walls.push({ ax: x - vx * st.w / 2, az: z - vz * st.w / 2, bx: x - vx * 0.9, bz: z - vz * 0.9, h: 1.2, t: 0.55 });
  }
  // Бочки и ящики вдоль улиц
  const props = defs.filter((d) => d.kind === 'prop');
  for (let k = 0; k < 60 && props.length; k++) {
    const st = city.streets[(R() * city.streets.length) | 0], t = R();
    const x = lerp(st.ax, st.bx, t), z = lerp(st.az, st.bz, t), side = R() < 0.5 ? -1 : 1, vertical = Math.abs(st.bx - st.ax) < 0.1;
    const px = vertical ? x + side * (st.w / 2 - 0.7) : x, pz = vertical ? z : z + side * (st.w / 2 - 0.7);
    if (Math.abs(px) > inner.x1 || Math.abs(pz) > inner.z1 || overlaps({ x0: px, x1: px, z0: pz, z1: pz }, cit, 1)) continue;
    city.props.push({ def: props[(R() * props.length) | 0], x: px, z: pz, rot: R() * Math.PI * 2 });
  }
  return city;
}
