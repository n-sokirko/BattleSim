// ------------------------------------------------------------------ анимация

/**
 * Выбирает анимацию по состоянию солдата. Здесь только имена клипов и время —
 * сами позы берутся из запечённой текстуры (см. 3b_crowd.js).
 */
function animate(u, state) {
  const a = u.t.anim, speed = u.curSpeed;
  if (u.t.mount) {
    if (!u.alive) {
      if (!u.deathShown) {
        playAnim(u.anim, 'Death', { once: true });
        playAnim(u.ride, Math.random() < 0.5 ? 'Death_A' : 'Death_B', { once: true });
        u.deathShown = true;
      }
      return;
    }
    if (speed > 3.5) playAnim(u.anim, 'Gallop', { speed: clamp(speed / 7, 0.7, 1.4) });
    else if (speed > 0.4) playAnim(u.anim, 'Walk', { speed: clamp(speed / 1.6, 0.6, 1.6) });
    else playAnim(u.anim, 'Idle');
    if (u.atkNew && a.attack.length) {
      u.atkNew = false;
      playAnim(u.ride, 'atk' + ((Math.random() * Math.min(2, a.attack.length)) | 0), { once: true, restart: true, speed: 1.1 });
    } else if (u.atkT < 0 && u.ride.name !== 'ride') playAnim(u.ride, 'ride');
    return;
  }
  if (!u.alive) {
    if (!u.deathShown) { playAnim(u.anim, Math.random() < 0.5 ? 'Death_A' : 'Death_B', { once: true }); u.deathShown = true; }
    return;
  }
  if (state === 'cheer') { playAnim(u.anim, a.cheer); return; }
  if (u.atkNew) {
    u.atkNew = false;
    let name = a.attack[(Math.random() * a.attack.length) | 0];
    if (u.t.ranged && !u.shot) name = a.melee;
    const clip = ASSETS.crowd.inf[u.type].clips[name];
    playAnim(u.anim, name, { once: true, restart: true, speed: clip ? clamp(clip.dur / u.atkDur, 0.8, 2.2) : 1 });
    return;
  }
  if (u.atkT >= 0) return; // удар ещё идёт
  if (u.t.ranged && u.aiming) { playAnim(u.anim, u.cooldown > 0.6 ? a.reload : a.aim); return; }
  if (speed > 1.6) playAnim(u.anim, a.run, { speed: clamp(speed / 3.4, 0.6, 1.5) });
  else if (speed > 0.3) playAnim(u.anim, 'Walking_A', { speed: clamp(speed / 1.4, 0.6, 1.4) });
  else playAnim(u.anim, a.idle);
}

// ------------------------------------------------------------------ отряды

const cap = (s) => s.charAt(0).toUpperCase() + s.slice(1);
/** Римские цифры для номеров отрядов: I, II, … XLIX. */
function roman(n) {
  let out = '';
  for (const [v, r] of [[40, 'XL'], [10, 'X'], [9, 'IX'], [5, 'V'], [4, 'IV'], [1, 'I']]) while (n >= v) { out += r; n -= v; }
  return out;
}
const d2d = (a, b) => Math.hypot(a.x - b.x, a.z - b.z);

/** Отряд: у него общий приказ, боевой дух и точки строя. */
class Squad {
  constructor(id, type, team, yaw) {
    this.id = id; this.type = type; this.t = TYPES_ALL[type]; this.team = team; this.yaw = yaw;
    this.units = []; this.size = 0; this.alive = 0;
    this.center = new THREE.Vector3();
    this.order = { kind: 'advance', mode: 'advance' }; this.orderT = 0; this.pending = null;
    this.morale = 100; this.lastHitT = -99; this.lastShotT = -99; this.firstStrikeT = -99; this.lastActiveT = 0;
    this.engaged = false; this.engagedFor = 0; this.hidden = false;
    this.nextDecision = 0; this.labelT = 0;
    this.special = !!this.t.special;
  }
  get name() {
    const base = SQUAD_NAME[this.type] || this.t.name.toLowerCase();
    return this.num ? base + ' ' + roman(this.num) : base;
  }
  refresh(dt) {
    let n = 0, eng = false;
    this.center.set(0, 0, 0);
    for (const u of this.units) if (u.alive) { this.center.add(u.pos); n++; if (u.engaged) eng = true; }
    this.alive = n;
    if (n) this.center.divideScalar(n);
    this.engaged = eng;
    this.engagedFor = eng ? this.engagedFor + dt : 0;
    this.labelT -= dt;
  }
  /** Место солдата в строю вокруг точки приказа. */
  slot(u) {
    const o = this.order, fy = o.face ?? this.yaw, c = Math.cos(fy), s = Math.sin(fy);
    const gx = o.x ?? this.center.x, gz = o.z ?? this.center.z;
    return [gx + u.ox * c + u.oz * s, gz - u.ox * s + u.oz * c];
  }
}

// ------------------------------------------------------------------ солдаты

const GRID = 2.5;
let GRID_HALF = 90, GRID_DIM = Math.ceil((GRID_HALF * 2) / GRID);

class Unit {
  constructor(plan) {
    this.plan = plan;
    this.type = plan.type; this.t = TYPES_ALL[plan.type]; this.team = plan.team; this.variant = plan.variant;
    this.ox = plan.ox || 0; this.oz = plan.oz || 0;
    this.pos = new THREE.Vector3(plan.x, world.groundAt(plan.x, plan.z), plan.z);
    this.vel = new THREE.Vector3(); this.knock = new THREE.Vector3();
    this.yaw = plan.yaw; this.hp = this.t.hp; this.alive = true;
    this.target = null; this.retargetT = Math.random() * 0.5; this.cooldown = Math.random() * 0.8;
    this.atkT = -1; this.atkDur = 0.6; this.shot = false; this.hitDone = false; this.aiming = false; this.atkNew = false; this.engaged = false;
    this.chargeT = 0; this.deadT = 0; this.phase = Math.random() * 6; this.deathShown = false; this.curSpeed = 0;
    this.losT = -9; this.losTarget = null; this.losOk = false; this.carry = null; this.gone = false; this.squad = null;
    this.scale = this.t.special === 'commander' ? 1.12 : this.t.special === 'captain' ? 1.06 : 0.95 + Math.random() * 0.1;
    if (this.t.mount) {
      this.horse = this.t.special === 'commander' ? 1 : this.t.special ? 0 : this.variant % 2;
      const hd = ASSETS.horses[this.horse];
      this.riderBase = new THREE.Vector3(hd.saddle.x, hd.saddle.y - ASSETS.riderHipsY, hd.saddle.z);
      this.anim = animState('Idle');
      this.ride = animState('ride');
    } else this.anim = animState(this.t.anim.idle);
  }
  get forward() { return new THREE.Vector3(Math.sin(this.yaw), 0, Math.cos(this.yaw)); }
  face(dx, dz, dt, rate = 9.5) {
    if (dx * dx + dz * dz < 1e-6) return;
    let d = Math.atan2(dx, dz) - this.yaw;
    d = Math.atan2(Math.sin(d), Math.cos(d));
    const step = rate * dt;
    this.yaw += Math.abs(d) <= step ? d : Math.sign(d) * step;
  }

}

// ------------------------------------------------------------------ бой

class Battle {
  constructor() {
    this.units = []; this.plan = []; this.squads = [];
    this.alive = [0, 0]; this.planCount = [0, 0];
    this.fighting = false; this.useCommanders = true;
    this.teams = [[], []];
    this.head = new Int32Array(GRID_DIM * GRID_DIM);
    this.next = new Int32Array(1024);
    this.push = new Float32Array(2048);
    this.time = 0; this.squadSeq = 0;
    this.commanders = [null, null];
    this.captains = [[], []];
    this.wings = [[], []];
    this.couriers = [null, null];
    this.log = []; this.onLog = null; this.glareLogged = [false, false];
    this.bolts = new Bolts(this);
    this.armyC = [null, null];
  }

  addLog(team, text) {
    const e = { t: this.time, team, text };
    this.log.push(e);
    if (this.onLog) this.onLog(e);
  }

  // ---------------------------------------------------------------- расстановка

  placeSquad(type, team, cx, cz, yaw) {
    const t = TYPES[type], cos = Math.cos(yaw), sin = Math.sin(yaw), id = ++this.squadSeq;
    const sq = new Squad(id, type, team, yaw);
    for (let r = 0; r < t.rows; r++) {
      for (let c = 0; c < t.cols; c++) {
        if (this.units.length >= MAX_UNITS) break;
        const ox = (c - (t.cols - 1) / 2) * t.spacing, oz = -(r - (t.rows - 1) / 2) * t.spacing;
        const jx = ox + (Math.random() - 0.5) * 0.2, jz = oz + (Math.random() - 0.5) * 0.2;
        const x = cx + jx * cos + jz * sin, z = cz - jx * sin + jz * cos;
        if (!World.inField(x, z, 1) || !world.walkable(x, z, t.radius) || this.occupied(x, z, t.radius)) continue;
        const plan = { type, team, x, z, yaw, variant: (Math.random() * 2) | 0, squad: id, ox, oz };
        this.plan.push(plan);
        const u = new Unit(plan);
        u.squad = sq; sq.units.push(u);
        this.units.push(u);
      }
    }
    if (sq.units.length) { sq.size = sq.units.length; sq.refresh(0); this.squads.push(sq); }
    this.numberSquads();
    this.recount();
    return sq.units.length;
  }

  occupied(x, z, radius) {
    for (const u of this.units) {
      const min = (radius + u.t.radius) * 0.9, dx = u.pos.x - x, dz = u.pos.z - z;
      if (dx * dx + dz * dz < min * min) return true;
    }
    return false;
  }

  removeNear(x, z, radius) {
    let n = 0;
    this.units = this.units.filter((u) => {
      const dx = u.pos.x - x, dz = u.pos.z - z;
      if (dx * dx + dz * dz > radius * radius) return true;
      n++;
      return false;
    });
    for (const sq of this.squads) sq.units = sq.units.filter((u) => this.units.includes(u));
    this.squads = this.squads.filter((sq) => sq.units.length);
    this.numberSquads();
    this.plan = this.units.map((u) => u.plan);
    this.recount();
    return n;
  }

  clearUnits() {
    this.units = []; this.squads = [];
    this.commanders = [null, null]; this.couriers = [null, null];
    this.captains = [[], []]; this.wings = [[], []];
    this.bolts.clear();
    this.fighting = false;
    this.time = 0; this.log = []; this.glareLogged = [false, false];
  }

  clearAll() { this.clearUnits(); this.plan = []; this.cmdSpec = null; this.recount(); }

  resetToPlan() {
    this.clearUnits();
    const map = new Map();
    this.plan = this.plan.filter((p) => World.inField(p.x, p.z, 1) && world.walkable(p.x, p.z, 0.4));
    for (const p of this.plan) {
      const u = new Unit(p);
      this.units.push(u);
      const key = p.squad ?? 't' + p.team + '_' + p.type;
      let sq = map.get(key);
      if (!sq) { sq = new Squad(key, p.type, p.team, p.yaw); map.set(key, sq); this.squads.push(sq); }
      sq.units.push(u); u.squad = sq;
      if (typeof p.squad === 'number') this.squadSeq = Math.max(this.squadSeq, p.squad);
    }
    for (const sq of this.squads) { sq.size = sq.units.length; sq.refresh(0); }
    this.numberSquads();
    this.recount();
  }

  /** «Мечники I, II, III» — чтобы в летописи было понятно, кому какой приказ. */
  numberSquads() {
    for (let team = 0; team < 2; team++) for (let type = 0; type < TYPES.length; type++) {
      const list = this.squads.filter((q) => q.team === team && q.type === type).sort((a, b) => a.center.x - b.center.x);
      list.forEach((q, i) => (q.num = list.length > 1 ? i + 1 : 0));
    }
  }

  setPlan(plan) { this.plan = plan.slice(); this.resetToPlan(); }

  recount() {
    this.planCount = [0, 0];
    for (const p of this.plan) this.planCount[p.team]++;
    this.alive = this.planCount.slice();
  }

  /** Случайные армии: size 1 — стычка, 2 — сражение, 3 — великая сеча. */
  randomArmies(size = 1) {
    this.clearAll();
    const cfg = [null,
      { front: [3, 5], xbow: [2, 3], cav: [1, 2], lines: 1 },
      { front: [6, 8], xbow: [4, 5], cav: [2, 4], lines: 2 },
      { front: [11, 13], xbow: [7, 9], cav: [4, 6], lines: 4 }][size] || null;
    const R = Math.random, pick = ([a, b]) => a + ((R() * (b - a + 1)) | 0), z0 = world.spawnZ;
    for (let team = 0; team < 2; team++) {
      const dir = team === 0 ? -1 : 1, yaw = team === 0 ? 0 : Math.PI;
      for (let line = 0; line < cfg.lines; line++) {
        const front = pick(cfg.front);
        for (let i = 0; i < front; i++)
          this.placeSquad(R() < 0.55 ? 0 : 1, team, (i - (front - 1) / 2) * 10 + (R() - 0.5) * 2, dir * (z0 + line * 8 + R() * 2), yaw);
      }
      const xbRows = Math.max(1, Math.ceil(cfg.lines / 2));
      for (let row = 0; row < xbRows; row++) {
        const n = pick(cfg.xbow);
        for (let i = 0; i < n; i++)
          this.placeSquad(2, team, (i - (n - 1) / 2) * 11 + (R() - 0.5) * 2, dir * (z0 + 2 + cfg.lines * 8 + row * 6 + R() * 2), yaw);
      }
      const cav = pick(cfg.cav);
      for (let i = 0; i < cav; i++) {
        const side = i % 2 ? -1 : 1, k = Math.floor(i / 2);
        this.placeSquad(3, team, side * (FIELD * 0.72 + k * 3 - R() * 4), dir * (z0 + 4 + k * 10 + R() * 4), yaw);
      }
    }
  }

  /** Имя и характер полководца армии (сохраняются между реваншами). */
  commanderSpec(team) {
    if (!this.cmdSpec) this.cmdSpec = [null, null];
    if (!this.cmdSpec[team]) {
      const keys = Object.keys(TRAITS);
      this.cmdSpec[team] = { name: CMD_NAMES[team][(Math.random() * CMD_NAMES[team].length) | 0], trait: keys[(Math.random() * keys.length) | 0] };
    }
    return this.cmdSpec[team];
  }

  // ---------------------------------------------------------------- начало боя, полководцы, гонцы

  startFight() {
    this.fighting = true;
    this.time = 0; this.log = []; this.glareLogged = [false, false];
    for (const sq of this.squads) { sq.order = { kind: 'advance', mode: 'advance' }; sq.morale = 100; }
    if (!this.useCommanders) { this.addLog(-1, 'Полководцев нет — каждый отряд бьётся сам по себе'); return; }
    for (let team = 0; team < 2; team++) this.spawnCommander(team);
  }

  /**
   * Главнокомандующий за центром армии. У большой армии (от 7 отрядов) — три крыла
   * с воеводами и резерв из самых дальних отрядов при главнокомандующем.
   */
  spawnCommander(team) {
    const mine = this.squads.filter((s) => s.team === team && !s.special && s.alive > 0);
    if (!mine.length) return;
    const spec = this.commanderSpec(team), back = team === 0 ? -1 : 1, yaw = team === 0 ? 0 : Math.PI;
    const c = Commander.center(mine);
    const leader = (type, x, z, key) => {
      const p = clampField({ x, z }, 4);
      const u = new Unit({ type, team, x: p.x, z: p.z, yaw, variant: type === T_CMD ? 1 : 0 });
      this.units.push(u);
      const sq = new Squad(key, type, team, yaw);
      sq.units.push(u); u.squad = sq; sq.size = 1;
      sq.order = { kind: 'hold', mode: 'hold', x: p.x, z: p.z };
      this.squads.push(sq);
      return u;
    };
    const role = mine.length >= 7 ? 'general' : 'solo';
    const gen = new Commander(this, team, leader(T_CMD, c.x, c.z + back * 18, 'cmd' + team), spec.trait, spec.name, role);
    this.commanders[team] = gen;
    this.couriers[team] = new Squad('msg' + team, T_MSG, team, yaw);
    const army = team === 0 ? 'синих' : 'красных';
    if (role === 'solo') {
      this.addLog(team, `${spec.name}, ${TRAITS[spec.trait].name} полководец, ведёт ${army}: ${TRAITS[spec.trait].note}`);
      return;
    }
    // Резерв: самые дальние от врага отряды
    let rest = mine.slice().sort((a, b) => b.center.z * back - a.center.z * back);
    let reserve = [];
    if (mine.length >= 12) {
      reserve = rest.splice(0, Math.max(1, Math.round(mine.length * 0.15)));
      for (const sq of reserve) { sq.reserve = true; sq.order = { kind: 'reserve', mode: 'hold', x: sq.center.x, z: sq.center.z, leash: 18 }; }
    }
    // Три крыла по ширине фронта. Синие смотрят на +z, их правое крыло — со стороны -x.
    rest.sort((a, b) => a.center.x - b.center.x);
    const n = rest.length, cuts = [0, Math.round(n / 3), Math.round((2 * n) / 3), n];
    const keys = team === 0 ? ['right', 'center', 'left'] : ['left', 'center', 'right'];
    const names = CMD_NAMES[team].filter((x) => x !== spec.name).sort(() => Math.random() - 0.5);
    const traitKeys = Object.keys(TRAITS), desc = [];
    for (let k = 0; k < 3; k++) {
      const list = rest.slice(cuts[k], cuts[k + 1]);
      if (!list.length) continue;
      const w = { key: keys[k], squads: list, captain: null, mission: { kind: 'attack' }, pendingMission: null };
      for (const sq of list) sq.wing = w;
      const wc = Commander.center(list), trait = traitKeys[(Math.random() * traitKeys.length) | 0];
      const cap = new Commander(this, team, leader(T_CAP, wc.x, wc.z + back * 11, 'cap' + team + keys[k]), trait, names[k], 'captain', w);
      w.captain = cap;
      this.captains[team].push(cap);
      this.wings[team].push(w);
      desc.push(`${WING_NAMES[w.key]} — ${cap.name} (${TRAITS[trait].name})`);
    }
    this.addLog(team, `${spec.name}, ${TRAITS[spec.trait].name} главнокомандующий, ведёт ${army}. Воеводы: ${desc.join('; ')}${reserve.length ? `. Резерв — ${reserve.length} отр.` : ''}`);
  }

  spawnMessenger(cmd, sq, order, extra = null) {
    const c = cmd.unit.pos, side = Math.random() < 0.5 ? -1.5 : 1.5;
    const plan = { type: T_MSG, team: cmd.team, x: clamp(c.x + side, -FIELD + 1, FIELD - 1), z: c.z, yaw: cmd.unit.yaw, variant: 0 };
    const u = new Unit(plan);
    u.carry = { squad: sq, order, cmd, delivered: false, ...(extra || {}) };
    u.squad = this.couriers[cmd.team];
    this.couriers[cmd.team].units.push(u);
    this.units.push(u);
  }

  applyOrder(sq, order) {
    if (order.mode !== 'advance' && order.mode !== 'rout' && order.mode !== 'charge' && order.x == null) { order.x = sq.center.x; order.z = sq.center.z; }
    if (order.x != null && order.face == null) {
      const e = this.armyCenter(1 - sq.team);
      if (e) order.face = Math.atan2(e.x - order.x, e.z - order.z);
    }
    sq.order = order;
    sq.orderT = this.time;
    sq.labelT = 4;
  }

  armyCenter(team) { return this.armyC ? this.armyC[team] : null; }

  computeArmyCenters() {
    this.armyC = [0, 1].map((team) => {
      const c = new THREE.Vector3();
      let n = 0;
      for (const u of this.teams[team]) if (!u.t.special) { c.add(u.pos); n++; }
      return n ? c.divideScalar(n) : null;
    });
  }

  // ---------------------------------------------------------------- главный цикл

  tick(dt) {
    this.time += dt;
    this.pathBudget = 4;
    this.teams[0].length = 0; this.teams[1].length = 0;
    const alive = [0, 0];
    for (const u of this.units) if (u.alive) { this.teams[u.team].push(u); if (!u.t.special) alive[u.team]++; }
    this.alive = alive;
    this.computeArmyCenters();
    this.buildGrid();
    for (const sq of this.squads) {
      sq.refresh(dt);
      if (sq.engaged || this.time - sq.lastShotT < 1.5) sq.lastActiveT = this.time;
    }
    for (const cs of this.couriers) if (cs) cs.refresh(dt);

    if (this.fighting && dt > 0) {
      if ((this.foesT = (this.foesT || 0) - dt) <= 0) { this.foesT = 0.8; this.updateFoes(); }
      this.updateSquads(dt);
      for (const c of this.commanders) if (c) c.tick(dt);
      for (const list of this.captains) for (const c of list) c.tick(dt);
      for (const u of this.units) if (u.alive) this.think(u, dt);
      for (const u of this.units) if (u.alive) this.integrate(u, dt);
      this.separate();
    }
    this.bolts.tick(dt);

    for (let i = this.units.length - 1; i >= 0; i--) {
      const u = this.units[i];
      if (u.gone) { u.alive = false; this.units.splice(i, 1); continue; }
      if (!u.alive) {
        u.deadT += dt;
        if (u.deadT > 24) { this.units.splice(i, 1); continue; }
      }
    }
  }

  /** Боевой дух, скрытность в низинах и смена фаз приказа. */
  updateSquads(dt) {
    for (const sq of this.squads) {
      if (sq.special || sq.alive === 0) continue;
      const o = sq.order, cmd = this.commanders[sq.team];
      const wcap = sq.wing && sq.wing.captain;
      const cmdNear = (cmd && cmd.unit.alive && d2d(cmd.unit.pos, sq.center) < 26) || (wcap && wcap.unit.alive && d2d(wcap.unit.pos, sq.center) < 22);
      if (!sq.engaged && this.time - sq.lastHitT > 3) sq.morale += dt * (cmdNear ? 4 : 1.2);
      if (sq.alive < sq.size * 0.3) sq.morale = Math.min(sq.morale, 55);
      sq.morale = clamp(sq.morale, 0, 100);

      const enemyNear = sq.foes && sq.foes.length > 0 && sq.foes[0].alive > 0 && d2d(sq.foes[0].center, sq.center) < 20;

      if (o.mode !== 'rout' && sq.morale < 18) {
        this.applyOrder(sq, { kind: 'rout', mode: 'rout' });
        sq.pending = null;
        this.addLog(sq.team, `${cap(sq.name)} дрогнули и бегут!`);
        continue;
      }
      if (o.mode === 'rout') {
        if (sq.morale > 45) {
          const p = cmd && cmd.unit.alive ? cmd.unit.pos : { x: sq.center.x, z: (sq.team === 0 ? -1 : 1) * (FIELD - 12) };
          this.applyOrder(sq, { kind: 'rally', mode: 'move', x: p.x, z: p.z, then: { kind: 'hold', mode: 'hold' } });
          this.addLog(sq.team, `${cap(sq.name)} опомнились и собираются у знамени`);
        }
        continue;
      }

      sq.hidden = world.concealedAt(sq.center.x, sq.center.z) && !sq.engaged && this.time - sq.lastShotT > 3 && !enemyNear;

      if (o.mode === 'move' && (d2d(o, sq.center) < 4 || this.time - sq.orderT > 35)) {
        const next = o.then ? { ...o.then } : { kind: 'advance', mode: 'advance' };
        if (next.mode !== 'advance' && next.mode !== 'charge' && next.x == null) { next.x = o.x; next.z = o.z; next.face = o.face; }
        this.applyOrder(sq, next);
      } else if (o.mode === 'charge' && (!o.target || o.target.alive === 0)) {
        this.applyOrder(sq, { kind: 'advance', mode: 'advance' });
      } else if (o.mode === 'ambush' && enemyNear) {
        sq.firstStrikeT = this.time;
        this.applyOrder(sq, { kind: 'charge', mode: 'advance' });
        this.addLog(sq.team, `Засада! ${cap(sq.name)} бьют из укрытия`);
      }
    }
  }

  think(u, dt) {
    const t = u.t, sq = u.squad, o = sq.order;
    u.cooldown -= dt; u.retargetT -= dt;
    u.engaged = false; u.aiming = false;
    if (t.special === 'messenger') { this.thinkMessenger(u, dt); return; }
    if (o.mode === 'rout') { this.flee(u, dt); return; }

    if (!u.target || !u.target.alive || u.retargetT <= 0) { u.retargetT = 0.45 + Math.random() * 0.45; u.target = this.pickTarget(u); }
    const tg = u.target;
    let dx = 0, dz = 0;
    let wantSlot = o.mode === 'move' || o.mode === 'hold' || o.mode === 'ambush' || isLeader(u);
    if (tg) {
      const tx = tg.pos.x - u.pos.x, tz = tg.pos.z - u.pos.z, d = Math.hypot(tx, tz);
      const nx = d > 1e-4 ? tx / d : Math.sin(u.yaw), nz = d > 1e-4 ? tz / d : Math.cos(u.yaw);
      const contact = t.radius + tg.t.radius + t.reach;
      if (t.ranged && d > contact + 1.5) {
        if (d <= this.rangeOf(u, tg) && this.canSee(u, tg)) {
          u.aiming = true; wantSlot = false;
          u.face(nx, nz, dt);
          if (u.cooldown <= 0 && u.atkT < 0) this.startAttack(u, true);
        } else if (o.mode === 'advance' || o.mode === 'charge') {
        const [wx, wz] = this.navTarget(u, tg.pos.x, tg.pos.z), ll = Math.hypot(wx - u.pos.x, wz - u.pos.z) || 1;
        dx = (wx - u.pos.x) / ll * t.speed; dz = (wz - u.pos.z) / ll * t.speed; wantSlot = false;
      }
      } else if (d <= contact && Math.abs(tg.pos.y - u.pos.y) < 1.8) {
        u.engaged = true; wantSlot = false;
        u.face(nx, nz, dt);
        if (u.cooldown <= 0 && u.atkT < 0 && t.dmg > 0) this.startAttack(u, false);
      } else if (this.mayChase(u, tg)) {
        wantSlot = false;
        if (d > 6) {
          const [wx, wz] = this.navTarget(u, tg.pos.x, tg.pos.z), ll = Math.hypot(wx - u.pos.x, wz - u.pos.z) || 1;
          dx = (wx - u.pos.x) / ll * t.speed; dz = (wz - u.pos.z) / ll * t.speed;
        } else { dx = nx * t.speed; dz = nz * t.speed; }
      }
    }
    if (wantSlot) {
      let gx, gz;
      if (isLeader(u)) { const c = u.cmd; gx = c ? c.post.x : u.pos.x; gz = c ? c.post.z : u.pos.z; }
      else [gx, gz] = sq.slot(u);
      const sd0 = Math.hypot(gx - u.pos.x, gz - u.pos.z);
      if (sd0 > 4) [gx, gz] = this.navTarget(u, gx, gz);
      const sx = gx - u.pos.x, sz = gz - u.pos.z, sd = Math.hypot(sx, sz);
      if (sd0 > 0.7) {
        const k = (sd0 > 5 ? 1 : 0.55) * (isLeader(u) ? 0.6 : 1);
        dx = sx / (sd || 1) * t.speed * k; dz = sz / (sd || 1) * t.speed * k;
      } else if (o.face != null && !u.aiming) u.face(Math.sin(o.face), Math.cos(o.face), dt, 3);
    }
    if (u.atkT >= 0 && !t.ranged && t.charge <= 1) { dx *= 0.3; dz *= 0.3; }
    this.steer(u, dx, dz, dt, !u.engaged && !u.aiming);

    if (t.charge > 1) {
      if (u.curSpeed > t.speed * 0.7) u.chargeT += dt;
      else if (!u.engaged) u.chargeT = Math.max(0, u.chargeT - dt * 2);
    }
    if (u.atkT >= 0) {
      u.atkT += dt / u.atkDur;
      if (!u.hitDone && u.atkT >= (u.shot ? 0.55 : 0.5)) { u.hitDone = true; this.resolveHit(u); }
      if (u.atkT >= 1) u.atkT = -1;
    }
  }

  /**
   * Куда идти сейчас, чтобы добраться до (gx, gz): прямо, если путь свободен,
   * иначе к следующей точке пути A*. Путь считается один раз на отряд.
   */
  navTarget(u, gx, gz) {
    const nav = world.nav;
    if (!nav.barriers) return [gx, gz];
    const cls = u.t.mount ? 1 : 0;
    if (this.time > (u.navT || 0) || Math.hypot(gx - u.navGX, gz - u.navGZ) > 5) {
      u.navT = this.time + 0.7 + Math.random() * 0.4;
      u.navGX = gx; u.navGZ = gz;
      if (nav.lineClear(u.pos.x, u.pos.z, gx, gz, cls)) u.path = null;
      else {
        const sq = u.squad, pc = sq.pathCache;
        let path;
        if (pc && pc.cls === cls && Math.hypot(pc.gx - gx, pc.gz - gz) < 6 && this.time - pc.t < 3 && d2d(sq.center, u.pos) < 12) path = pc.path;
        else if (this.pathBudget <= 0) { u.navT = this.time + 0.2; return u.path ? u.path[Math.min(u.pathI, u.path.length - 1)] : [gx, gz]; }
        else {
          // не больше нескольких поисков пути за шаг — иначе рывки при сотнях отрядов
          this.pathBudget--;
          const from = d2d(sq.center, u.pos) < 12 && nav.speedAt(sq.center.x, sq.center.z, cls) > 0 ? sq.center : u.pos;
          path = nav.findPath(from.x, from.z, gx, gz, cls);
          if (from === sq.center) sq.pathCache = { cls, gx, gz, t: this.time, path };
        }
        u.path = path;
        u.pathI = 1;
        if (path) { // начинаем с ближайшей точки пути
          let best = Infinity;
          for (let i = 1; i < path.length; i++) { const d = Math.hypot(path[i][0] - u.pos.x, path[i][1] - u.pos.z); if (d < best) { best = d; u.pathI = i; } }
        }
      }
    }
    const P = u.path;
    if (!P) return [gx, gz];
    while (u.pathI < P.length - 1 && Math.hypot(P[u.pathI][0] - u.pos.x, P[u.pathI][1] - u.pos.z) < 2.5) u.pathI++;
    if (u.pathI >= P.length - 1 && Math.hypot(P[P.length - 1][0] - u.pos.x, P[P.length - 1][1] - u.pos.z) < 3) return [gx, gz];
    return P[Math.min(u.pathI, P.length - 1)];
  }

  steer(u, dx, dz, dt, faceMove) {
    const ax = dx - u.vel.x, az = dz - u.vel.z, al = Math.hypot(ax, az), step = u.t.accel * dt;
    if (al <= step) { u.vel.x = dx; u.vel.z = dz; } else { u.vel.x += ax / al * step; u.vel.z += az / al * step; }
    if (faceMove && Math.hypot(u.vel.x, u.vel.z) > 0.2) u.face(u.vel.x, u.vel.z, dt, u.t.mount ? 3 : 9.5);
  }

  mayChase(u, tg) {
    const o = u.squad.order;
    if (isLeader(u)) return d2d(u.pos, tg.pos) < 7;
    if (o.mode === 'advance' || o.mode === 'charge') return true;
    if (o.mode === 'hold' || o.mode === 'ambush') return d2d(o, tg.pos) < (o.leash ?? (u.t.mount ? 16 : 10));
    return false;
  }

  /**
   * Цель с учётом приказа, скрытности и прямой видимости. Ищем не по всей армии,
   * а в ближайших клетках сетки и среди вражеских отрядов, известных своему отряду, —
   * так поиск не тормозит и при тысячах солдат.
   */
  pickTarget(u) {
    const o = u.squad.order, ranged = u.t.ranged;
    const gx = o.x ?? u.pos.x, gz = o.z ?? u.pos.z, leash = o.leash ?? (u.t.mount ? 16 : 10);
    const allowed = (e, d2) => {
      if (e.squad && e.squad.hidden && d2 > 196) return false; // в низине не видно
      if (e.t.special === 'messenger' && !ranged && d2 > 64) return false;
      if (isLeader(u) && d2 > 64) return false;
      if (o.mode === 'move' && d2 > 10) return false;
      if ((o.mode === 'hold' || o.mode === 'ambush') && !ranged) {
        const lx = e.pos.x - gx, lz = e.pos.z - gz;
        if (lx * lx + lz * lz > leash * leash && d2 > 9) return false;
      }
      return true;
    };
    const near = this.gridNearest(u, ranged ? 4 : 10, allowed);
    if (near) return near;
    if (u.t.special) return null;

    const foes = u.squad.foes || [];
    const cands = o.mode === 'charge' && o.target && o.target.alive ? [o.target, ...foes] : foes;
    const best = [];
    for (let qi = 0; qi < Math.min(cands.length, 4); qi++) {
      const q = cands[qi];
      for (const e of q.units) {
        if (!e.alive) continue;
        const dx = e.pos.x - u.pos.x, dz = e.pos.z - u.pos.z, d2 = dx * dx + dz * dz;
        if (!allowed(e, d2)) continue;
        const sc = o.mode === 'charge' && o.target === q ? d2 * 0.2 : d2;
        if (best.length < 5 || sc < best[best.length - 1].s) {
          best.push({ e, s: sc });
          best.sort((a, b) => a.s - b.s);
          if (best.length > 5) best.pop();
        }
      }
    }
    if (!best.length) return null;
    if (!ranged) return best[0].e;
    for (const c of best) if (d2d(c.e.pos, u.pos) <= this.rangeOf(u, c.e) && this.canSee(u, c.e)) return c.e;
    return o.mode === 'advance' || o.mode === 'charge' ? best[0].e : null;
  }

  /** Ближайший враг в радиусе R по сетке соседей. */
  gridNearest(u, R, allowed) {
    const [cx, cz] = this.cellOf(u.pos.x, u.pos.z), r = Math.ceil(R / GRID);
    let best = null, bs = R * R;
    for (let z = Math.max(cz - r, 0); z <= Math.min(cz + r, GRID_DIM - 1); z++) {
      for (let x = Math.max(cx - r, 0); x <= Math.min(cx + r, GRID_DIM - 1); x++) {
        for (let j = this.head[z * GRID_DIM + x]; j >= 0; j = this.next[j]) {
          const e = this.units[j];
          if (!e || !e.alive || e.team === u.team) continue;
          const dx = e.pos.x - u.pos.x, dz = e.pos.z - u.pos.z, d2 = dx * dx + dz * dz;
          if (d2 >= bs || (allowed && !allowed(e, d2))) continue;
          best = e; bs = d2;
        }
      }
    }
    return best;
  }

  /** Раз в 0,8 с каждому отряду — список ближайших видимых вражеских отрядов. */
  updateFoes() {
    const S = this.squads.filter((q) => !q.special && q.alive > 0);
    for (const sq of S) {
      const list = [];
      for (const e of S) {
        if (e.team === sq.team) continue;
        const d = d2d(e.center, sq.center);
        if (!e.hidden || d < 16) list.push({ e, d });
      }
      list.sort((a, b) => a.d - b.d);
      sq.foes = list.slice(0, 4).map((x) => x.e);
      sq.foeDist = list.length ? list[0].d : Infinity;
    }
  }

  canSee(u, tg) {
    if (u.losTarget === tg && this.time - u.losT < 0.5) return u.losOk;
    u.losTarget = tg; u.losT = this.time;
    return (u.losOk = world.los(u.pos.x, u.pos.y + 1.3, u.pos.z, tg.pos.x, tg.pos.y + 1.0, tg.pos.z));
  }

  /** Дальность выстрела: высота добавляет, ветер помогает или мешает, солнце слепит. */
  rangeOf(u, tg) {
    let r = u.t.range;
    const dx = tg.pos.x - u.pos.x, dz = tg.pos.z - u.pos.z, d = Math.hypot(dx, dz) || 1;
    r += clamp((u.pos.y - tg.pos.y) * 1.2, -6, 12);
    r += (world.wind.x * dx + world.wind.y * dz) / d * 1.2;
    if (this.glare(dx / d, dz / d)) r *= 0.85;
    return r;
  }

  glare(nx, nz) {
    if (!world.style || world.style.elev > 26) return false;
    const sx = world.sunDir.x, sz = world.sunDir.z, l = Math.hypot(sx, sz) || 1;
    return (nx * sx + nz * sz) / l > 0.77;
  }

  flee(u, dt) {
    if (u.retargetT <= 0 || !u.fleeFrom || !u.fleeFrom.alive) {
      u.retargetT = 0.5;
      u.fleeFrom = this.gridNearest(u, 16, null);
    }
    const [ex, ez] = this.navTarget(u, u.pos.x, (u.team === 0 ? -1 : 1) * (FIELD - 4));
    let fx = ex - u.pos.x, fz = ez - u.pos.z;
    const fl = Math.hypot(fx, fz) || 1;
    fx /= fl; fz /= fl;
    if (u.fleeFrom) {
      const ax = u.pos.x - u.fleeFrom.pos.x, az = u.pos.z - u.fleeFrom.pos.z, l = Math.hypot(ax, az) || 1;
      fx += ax / l * 1.5; fz += az / l * 1.5;
    }
    const l = Math.hypot(fx, fz) || 1;
    this.steer(u, fx / l * u.t.speed * 1.05, fz / l * u.t.speed * 1.05, dt, true);
    if (u.atkT >= 0) { u.atkT += dt / u.atkDur; if (u.atkT >= 1) u.atkT = -1; }
  }

  thinkMessenger(u, dt) {
    const c = u.carry;
    let gx = u.pos.x, gz = u.pos.z;
    if (!c.delivered && c.captain) {
      const cap = c.captain;
      if (!cap.unit.alive) { c.delivered = true; if (c.wing.pendingMission === c.mission) c.wing.pendingMission = null; }
      else {
        gx = cap.unit.pos.x; gz = cap.unit.pos.z;
        if (d2d(cap.unit.pos, u.pos) < 6) { c.delivered = true; if (c.wing.pendingMission === c.mission) cap.receive(c.mission); }
      }
    } else if (!c.delivered) {
      const sq = c.squad;
      if (!sq || sq.alive === 0 || sq.order.mode === 'rout') { c.delivered = true; if (sq && sq.pending === c.order) sq.pending = null; }
      else {
        gx = sq.center.x; gz = sq.center.z;
        if (d2d(sq.center, u.pos) < 6) {
          c.delivered = true;
          if (sq.pending === c.order) { sq.pending = null; this.applyOrder(sq, c.order); }
        }
      }
    }
    if (c.delivered) {
      const cu = c.cmd.unit;
      if (cu.alive) { gx = cu.pos.x; gz = cu.pos.z; if (d2d(cu.pos, u.pos) < 5) u.gone = true; }
      else { gz = (u.team === 0 ? -1 : 1) * FIELD; if (Math.abs(u.pos.z) > FIELD - 3) u.gone = true; }
    }
    // Гонец объезжает врагов стороной
    if (u.retargetT <= 0) {
      u.retargetT = 0.3;
      u.fleeFrom = this.gridNearest(u, 12, null);
    }
    [gx, gz] = this.navTarget(u, gx, gz);
    let vx = gx - u.pos.x, vz = gz - u.pos.z;
    const l = Math.hypot(vx, vz) || 1;
    vx /= l; vz /= l;
    if (u.fleeFrom && u.fleeFrom.alive) {
      const ax = u.pos.x - u.fleeFrom.pos.x, az = u.pos.z - u.fleeFrom.pos.z, al = Math.hypot(ax, az) || 1;
      const k = clamp((12 - al) / 12, 0, 1) * 1.6;
      vx += ax / al * k; vz += az / al * k;
    }
    const vl = Math.hypot(vx, vz) || 1;
    this.steer(u, vx / vl * u.t.speed, vz / vl * u.t.speed, dt, true);
  }

  startAttack(u, shot) {
    u.atkT = 0; u.hitDone = false; u.shot = shot; u.atkNew = true;
    u.atkDur = shot || !u.t.ranged ? u.t.atkTime : 0.55;
    const cd = shot || !u.t.ranged ? u.t.cd : 1.0;
    u.cooldown = cd * (0.85 + Math.random() * 0.3);
    if (shot) u.squad.lastShotT = this.time;
  }

  resolveHit(u) {
    const tg = u.target;
    if (!tg || !tg.alive) return;
    if (u.shot) { this.bolts.fire(u, tg); return; }
    const tx = tg.pos.x - u.pos.x, tz = tg.pos.z - u.pos.z, d = Math.hypot(tx, tz);
    if (d > u.t.radius + tg.t.radius + u.t.reach + 0.6 || Math.abs(tg.pos.y - u.pos.y) > 2) return; // промах: цель отошла
    const nx = d > 1e-4 ? tx / d : 0, nz = d > 1e-4 ? tz / d : 1;
    let dmg = u.t.ranged ? 8 : u.t.dmg;
    if (tg.t.mount) dmg *= u.t.vsCav;
    dmg *= 1 + clamp((u.pos.y - tg.pos.y) * 0.1, -0.3, 0.35);   // сверху бить легче
    const rear = Math.sin(tg.yaw) * nx + Math.cos(tg.yaw) * nz > 0.35; // цель смотрит от нас
    if (rear) dmg *= 1.35;
    if (this.time - u.squad.firstStrikeT < 2.5) dmg *= 1.6;          // удар из засады
    const charge = u.t.charge > 1 && u.chargeT > 0.8;
    if (charge) { dmg *= u.t.charge; u.chargeT = 0; this.trample(u, tg); }
    const kb = (charge ? 5 : 1.3) / tg.t.mass;
    this.damage(tg, dmg, nx * kb, nz * kb, false, u, rear);
  }

  /** Натиск рыцаря задевает и сбивает соседей цели. */
  trample(k, main) {
    const fx = Math.sin(k.yaw), fz = Math.cos(k.yaw);
    for (const e of this.teams[1 - k.team]) {
      if (e === main || !e.alive) continue;
      const tx = e.pos.x - k.pos.x, tz = e.pos.z - k.pos.z, d = Math.hypot(tx, tz);
      if (d > 2.6 || tx * fx + tz * fz < 0) continue;
      const s = 2.5 / e.t.mass;
      this.damage(e, k.t.dmg * 0.6, (tx / Math.max(d, 0.1) + fx) * s, (tz / Math.max(d, 0.1) + fz) * s, false, k);
      if (e.squad) e.squad.morale -= 2;
    }
  }

  damage(t, amount, kx, kz, arrow, src, rear) {
    if (!t.alive) return;
    amount *= 0.8 + Math.random() * 0.4;
    if (arrow && !rear) amount *= 1 - t.t.arrowBlock;
    if (arrow && t.pos.y > world.heightAt(t.pos.x, t.pos.z) + 3) amount *= 0.55; // за зубцами стены
    amount *= 1 - t.t.armor;
    t.hp -= amount;
    t.knock.x += kx; t.knock.z += kz;
    const sq = t.squad;
    if (sq && !sq.special) {
      if (arrow) { sq.lastHitT = this.time; sq.morale -= 0.6; }
      if (rear) sq.morale -= 1.5;
    }
    if (t.hp <= 0) { this.kill(t); return; }
    if (src && !arrow && t.target !== src && Math.random() < 0.5) t.target = src;
  }

  kill(u) {
    u.alive = false; u.deadT = 0; u.target = null; u.atkT = -1;
    u.vel.set(0, 0, 0); u.curSpeed = 0;
    const sq = u.squad;
    if (u.t.special === 'messenger') {
      const c = u.carry;
      if (c && !c.delivered && c.captain) {
        if (c.wing.pendingMission === c.mission) c.wing.pendingMission = null;
        this.addLog(u.team, `Гонец к воеводе ${c.captain.name} перехвачен — приказ «${MISSION_TEXT[c.mission.kind]}» не дошёл`);
      } else if (c && !c.delivered) {
        if (c.squad.pending === c.order) c.squad.pending = null;
        this.addLog(u.team, `Гонец к отряду «${c.squad.name}» перехвачен — приказ «${ORDER_TEXT[c.order.kind].toLowerCase()}» не дошёл`);
      }
    } else if (u.t.special === 'captain') {
      const cp = u.cmd, gen = this.commanders[u.team], wing = cap(WING_NAMES[cp.wing.key]);
      for (const s of cp.wing.squads) s.morale -= 15;
      this.addLog(u.team, gen && gen.unit.alive
        ? `Воевода ${cp.name} пал! ${wing}: теперь приказы отдаёт ${gen.name}`
        : `Воевода ${cp.name} пал! ${wing} осталось без начальства`);
    } else if (u.t.special === 'commander') {
      const cmd = this.commanders[u.team];
      for (const s of this.squads) if (s.team === u.team && !s.special) s.morale -= 25;
      const caps = this.captains[u.team].filter((c) => c.unit.alive).length;
      this.addLog(u.team, `${cmd ? cmd.name : 'Полководец'} пал! ${caps ? 'Воеводы бьются дальше своим умом' : 'Армия осталась без приказов'}`);
    } else if (sq) sq.morale -= 6;
    animate(u);
  }

  integrate(u, dt) {
    const vx = u.vel.x, vz = u.vel.z, sp = Math.hypot(vx, vz), cls = u.t.mount ? 1 : 0, nav = world.nav;
    let f = 1;
    if (sp > 0.05) {
      // в гору тяжело, под гору легче
      const g = world.groundAt(u.pos.x + vx / sp, u.pos.z + vz / sp) - u.pos.y;
      f = g > 0 ? Math.max(0.35, 1 - g * 1.4) : Math.min(1.2, 1 - g * 0.5);
    }
    f *= Math.max(0.2, nav.speedAt(u.pos.x, u.pos.z, cls)); // топь, брод, чаща
    if (world.inWall(u.pos.x, u.pos.z)) f *= 0.35; // перелезаем ограду
    u.curSpeed = sp * f;
    const ox = u.pos.x, oz = u.pos.z, oy = u.pos.y, lim = FIELD - 0.5;
    let nx = clamp(ox + (vx * f + u.knock.x) * dt, -lim, lim), nz = clamp(oz + (vz * f + u.knock.z) * dt, -lim, lim);
    // Не заходим в непроходимое и не прыгаем с обрыва или со стены — скользим вдоль
    if (!this.canMove(ox, oz, oy, nx, nz, cls)) {
      if (this.canMove(ox, oz, oy, nx, oz, cls)) nz = oz;
      else if (this.canMove(ox, oz, oy, ox, nz, cls)) nx = ox;
      else { nx = ox; nz = oz; }
    }
    u.pos.x = nx; u.pos.z = nz;
    world.obs.pushOut(u.pos, u.t.radius * 0.8);
    const ny = world.groundAt(u.pos.x, u.pos.z);
    if (Math.abs(ny - oy) > 1.2) { u.pos.x = ox; u.pos.z = oz; u.pos.y = oy; } else u.pos.y = ny;
    const k = Math.exp(-7 * dt);
    u.knock.x *= k; u.knock.z *= k;
    u.phase += dt * u.curSpeed * (u.t.mount ? 1.4 : 3.3);
  }

  /** Можно ли шагнуть из (ox, oz) на высоте oy в (nx, nz): не в дом, не в воду, не с обрыва. */
  canMove(ox, oz, oy, nx, nz, cls) {
    const nav = world.nav;
    if (nav.speedAt(nx, nz, cls) === 0 && nav.speedAt(ox, oz, cls) > 0) return false;
    return Math.abs(world.groundAt(nx, nz) - oy) <= Math.hypot(nx - ox, nz - oz) * 1.2 + 0.25;
  }

  // ---------------------------------------------------------------- соседи

  cellOf(x, z) {
    return [clamp(((x + GRID_HALF) / GRID) | 0, 0, GRID_DIM - 1), clamp(((z + GRID_HALF) / GRID) | 0, 0, GRID_DIM - 1)];
  }

  buildGrid() {
    if (GRID_HALF !== FIELD + 10) {
      GRID_HALF = FIELD + 10;
      GRID_DIM = Math.ceil((GRID_HALF * 2) / GRID);
      this.head = new Int32Array(GRID_DIM * GRID_DIM);
    }
    this.head.fill(-1);
    if (this.next.length < this.units.length) { this.next = new Int32Array(this.units.length * 2); this.push = new Float32Array(this.units.length * 4); }
    for (let i = 0; i < this.units.length; i++) {
      const u = this.units[i];
      if (!u.alive) continue;
      const [cx, cz] = this.cellOf(u.pos.x, u.pos.z), c = cz * GRID_DIM + cx;
      this.next[i] = this.head[c]; this.head[c] = i;
    }
  }

  separate() {
    const U = this.units, P = this.push;
    for (let i = 0; i < U.length; i++) {
      P[i * 2] = 0; P[i * 2 + 1] = 0;
      const u = U[i];
      if (!u.alive) continue;
      const [cx, cz] = this.cellOf(u.pos.x, u.pos.z);
      for (let z = Math.max(cz - 1, 0); z <= Math.min(cz + 1, GRID_DIM - 1); z++) {
        for (let x = Math.max(cx - 1, 0); x <= Math.min(cx + 1, GRID_DIM - 1); x++) {
          for (let j = this.head[z * GRID_DIM + x]; j >= 0; j = this.next[j]) {
            if (j === i) continue;
            const o = U[j], dx = u.pos.x - o.pos.x, dz = u.pos.z - o.pos.z, min = u.t.radius + o.t.radius, d2 = dx * dx + dz * dz;
            if (d2 >= min * min || Math.abs(u.pos.y - o.pos.y) > 1.5) continue; // на стене и под стеной не толкаются
            const d = Math.sqrt(d2);
            const nx = d > 1e-4 ? dx / d : Math.cos(i), nz = d > 1e-4 ? dz / d : Math.sin(i);
            const s = (min - d) * (o.t.mass / (u.t.mass + o.t.mass)) * 0.5;
            P[i * 2] += nx * s; P[i * 2 + 1] += nz * s;
          }
        }
      }
    }
    const lim = FIELD - 0.5;
    for (let i = 0; i < U.length; i++) {
      const u = U[i];
      let px = P[i * 2], pz = P[i * 2 + 1];
      if (!u.alive || (px === 0 && pz === 0)) continue;
      const l = Math.hypot(px, pz);
      if (l > 0.4) { px *= 0.4 / l; pz *= 0.4 / l; }
      const ox = u.pos.x, oz = u.pos.z, oy = u.pos.y, cls = u.t.mount ? 1 : 0;
      const nx = clamp(u.pos.x + px, -lim, lim), nz = clamp(u.pos.z + pz, -lim, lim);
      if (!this.canMove(ox, oz, oy, nx, nz, cls)) continue; // толкотня не сбрасывает со стены и в воду
      u.pos.x = nx; u.pos.z = nz;
      world.obs.pushOut(u.pos, u.t.radius * 0.8);
      const ny = world.groundAt(u.pos.x, u.pos.z);
      if (Math.abs(ny - oy) > 1.2) { u.pos.x = ox; u.pos.z = oz; } else u.pos.y = ny;
    }
  }

  /** Ближайший живой враг команды team у точки (для попадания болтов). */
  enemyNear(x, z, team, extra) {
    const [cx, cz] = this.cellOf(x, z);
    let best = null, bestD = Infinity;
    for (let gz = Math.max(cz - 1, 0); gz <= Math.min(cz + 1, GRID_DIM - 1); gz++) {
      for (let gx = Math.max(cx - 1, 0); gx <= Math.min(cx + 1, GRID_DIM - 1); gx++) {
        for (let j = this.head[gz * GRID_DIM + gx]; j >= 0; j = this.next[j]) {
          const o = this.units[j];
          if (!o || !o.alive || o.team === team) continue;
          const dx = o.pos.x - x, dz = o.pos.z - z, r = o.t.radius + extra, d2 = dx * dx + dz * dz;
          if (d2 < r * r && d2 < bestD) { bestD = d2; best = o; }
        }
      }
    }
    return best;
  }

  centroid() {
    const c = new THREE.Vector3();
    let n = 0;
    for (const u of this.units) if (u.alive && !u.t.special) { c.add(u.pos); n++; }
    return n ? c.divideScalar(n) : null;
  }
}

// ------------------------------------------------------------------ арбалетные болты

class Bolts {
  constructor(battle) { this.battle = battle; this.cap = LOW_END ? 700 : 1600; this.list = []; this.mesh = null; }
  init() {
    const b = ASSETS.bolt;
    this.mesh = new THREE.InstancedMesh(b.geometry, b.material, this.cap);
    this.mesh.count = 0;
    this.mesh.frustumCulled = false;
    unitLayer.add(this.mesh);
    this.axis = new THREE.Vector3(b.axis === 'x' ? 1 : 0, b.axis === 'y' ? 1 : 0, b.axis === 'z' ? 1 : 0).multiplyScalar(BOLT_TIP);
  }
  fire(src, dst) {
    if (this.list.length >= this.cap) {
      const i = this.list.findIndex((a) => !a.flying);
      if (i < 0) return;
      this.list.splice(i, 1);
    }
    const B = this.battle, f = src.forward;
    const from = new THREE.Vector3(src.pos.x + f.x * 0.6, src.pos.y + 1.3, src.pos.z + f.z * 0.6);
    const dx = dst.pos.x - src.pos.x, dz = dst.pos.z - src.pos.z, d = Math.hypot(dx, dz) || 1;
    const dur = d / 42 + 0.22;
    const glare = B.glare(dx / d, dz / d);
    if (glare && !B.glareLogged[src.team]) {
      B.glareLogged[src.team] = true;
      B.addLog(src.team, 'Низкое солнце бьёт арбалетчикам в глаза — болты уходят мимо');
    }
    const spread = (0.3 + d * 0.03) * (glare ? 2.4 : 1), ang = Math.random() * Math.PI * 2, rad = Math.sqrt(Math.random()) * spread;
    const to = new THREE.Vector3(
      dst.pos.x + dst.vel.x * dur + Math.cos(ang) * rad + world.wind.x * dur * 0.8, 0,
      dst.pos.z + dst.vel.z * dur + Math.sin(ang) * rad + world.wind.y * dur * 0.8);
    to.y = world.groundAt(to.x, to.z) + 0.9;
    this.list.push({ from, to, age: 0, dur, arc: 0.4 + d * 0.07, team: src.team, dmg: src.t.dmg, flying: true, stuck: 0, pos: from.clone(), dir: new THREE.Vector3(dx, 0, dz) });
  }
  tick(dt) {
    const L = this.list;
    for (let i = L.length - 1; i >= 0; i--) {
      const a = L[i];
      if (a.flying) {
        a.age += dt;
        const s = Math.min(1, a.age / a.dur);
        a.pos.lerpVectors(a.from, a.to, s); a.pos.y += a.arc * 4 * s * (1 - s);
        a.dir.subVectors(a.to, a.from).divideScalar(a.dur); a.dir.y += a.arc * 4 * (1 - 2 * s) / a.dur;
        // Болт врезается в склон или ограду — так и работает укрытие
        const gy = world.groundAt(a.pos.x, a.pos.z);
        const inTrees = world.type === 'forest' && a.pos.y < gy + 7 && world.nav.canopyAt(a.pos.x, a.pos.z) && Math.random() < dt * 2.2;
        if (s > 0.08 && s < 1 && (a.pos.y < gy + 0.05 || world.wallTop(a.pos.x, a.pos.z) > a.pos.y || world.obs.blocks(a.pos.x, a.pos.z, a.pos.y) || world.decks.solidTop(a.pos.x, a.pos.z, world.heightAt(a.pos.x, a.pos.z)) > a.pos.y || inTrees)) {
          a.flying = false; a.stuck = 8; a.dir.normalize();
          continue;
        }
        if (s < 1) continue;
        const hit = this.battle.enemyNear(a.to.x, a.to.z, a.team, 0.35);
        if (hit) {
          const l = Math.hypot(a.dir.x, a.dir.z) || 1;
          const back = Math.sin(hit.yaw) * a.dir.x / l + Math.cos(hit.yaw) * a.dir.z / l > 0.35;
          this.battle.damage(hit, a.dmg, a.dir.x / l * 0.6 / hit.t.mass, a.dir.z / l * 0.6 / hit.t.mass, true, null, back);
          L.splice(i, 1);
          continue;
        }
        a.flying = false; a.stuck = 8;
        const g = world.groundAt(a.to.x, a.to.z);
        a.dir.normalize();
        const down = Math.max(0.3, -a.dir.y);
        a.pos.copy(a.to).addScaledVector(a.dir, Math.max(0, a.to.y - g) / down - 0.25);
      } else if ((a.stuck -= dt) <= 0) L.splice(i, 1);
    }
    if (!this.mesh) return;
    const m4 = new THREE.Matrix4(), q = new THREE.Quaternion(), sc = new THREE.Vector3().setScalar(ASSETS.bolt.norm), dn = new THREE.Vector3();
    const n = Math.min(L.length, this.cap);
    for (let i = 0; i < n; i++) {
      const a = L[i];
      q.setFromUnitVectors(this.axis, dn.copy(a.dir).normalize());
      m4.compose(a.pos, q, sc);
      this.mesh.setMatrixAt(i, m4);
    }
    this.mesh.count = n;
    this.mesh.instanceMatrix.needsUpdate = true;
  }
  clear() { this.list.length = 0; if (this.mesh) this.mesh.count = 0; }
}
let BOLT_TIP = 1; // направление наконечника вдоль оси модели (+1 или -1)
