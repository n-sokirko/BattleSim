// ------------------------------------------------------------------ полководец

const STANCE_TEXT = {
  defend: 'стоим на высотах — пусть враг лезет вверх под болтами',
  attack: 'все вперёд, давим числом!',
  maneuver: 'связать их фронт, а самим обойти фланги и ударить из засады',
};

const norm2 = (x, z) => { const l = Math.hypot(x, z) || 1; return { x: x / l, z: z / l }; };
const clampField = (p, m = 4) => ({ x: clamp(p.x, -FIELD + m, FIELD - m), z: clamp(p.z, -FIELD + m, FIELD - m) });

/**
 * Полководец видит поле (кроме затаившихся в низинах), раз в несколько секунд
 * оценивает силы и рельеф и рассылает отрядам приказы через гонцов.
 */
class Commander {
  /**
   * role: 'solo' — единственный полководец небольшой армии;
   * 'general' — главнокомандующий: ставит задачи крыльям, держит резерв;
   * 'captain' — воевода крыла: раздаёт приказы отрядам своего крыла.
   */
  constructor(battle, team, unit, trait, name, role = 'solo', wing = null) {
    Object.assign(this, { battle, team, unit, trait, name, role, wing });
    unit.cmd = this;
    this.post = unit.pos.clone();
    this.nextThink = 1.2 + Math.random();
    this.stance = null; this.stanceT = -99;
    this.ambushSet = false;
    this.nextWings = 2;
    this.mission = role === 'captain' ? { kind: 'attack' } : null;
    this.flankDone = false;
    this.reserveCommitted = false;
  }

  get title() { return this.role === 'captain' ? 'Воевода ' + this.name : this.name; }

  allMine() { return this.battle.squads.filter((s) => s.team === this.team && !s.special && s.alive > 0); }

  /** Отряды, которыми полководец командует сам. */
  mySquads() {
    const all = this.allMine();
    if (this.role === 'captain') return all.filter((s) => s.wing === this.wing);
    if (this.role === 'general') return all.filter((s) => !s.wing || !s.wing.captain || !s.wing.captain.unit.alive);
    return all;
  }
  foeSquads() { return this.battle.squads.filter((s) => s.team !== this.team && !s.special && s.alive > 0 && !s.hidden); }

  static center(list) {
    let x = 0, z = 0, n = 0;
    for (const s of list) { x += s.center.x * s.alive; z += s.center.z * s.alive; n += s.alive; }
    return n ? { x: x / n, z: z / n } : null;
  }
  static nearest(list, p) {
    let best = null, bd = Infinity;
    for (const s of list) { const d = d2d(s.center, p); if (d < bd) { bd = d; best = s; } }
    return best;
  }
  static count(list, pred) { return list.reduce((s, q) => s + (pred(q) ? q.alive : 0), 0); }

  tick(dt) {
    if (!this.unit.alive) return;
    const all = this.allMine();
    if (!all.length) return;
    const wingSq = this.role === 'captain' ? all.filter((s) => s.wing === this.wing) : all;
    this.updatePost(wingSq.length ? wingSq : all);
    if (this.role === 'general' && (this.nextWings -= dt) <= 0) {
      this.nextWings = 6.5 + Math.random() * 1.5;
      this.thinkWings();
    }
    if ((this.nextThink -= dt) > 0) return;
    this.nextThink = TRAITS[this.trait].think + Math.random() * 0.8;

    const my = this.mySquads();
    if (!my.length) return;
    const en = this.foeSquads();
    if (this.role === 'captain') this.stance = { attack: 'attack', hold: 'defend', flank: 'maneuver', support: 'attack', reserve: 'defend' }[this.mission.kind] || 'attack';
    else if (!this.stance || this.battle.time - this.stanceT > 20) this.chooseStance(my, en);
    // Роли, уже розданные отрядам: одну задачу не поручаем двоим
    this.claims = new Set();
    for (const q of my) { const o = q.pending || q.order; if (o.claim) this.claims.add(o.claim); }
    this.outbox = [];
    for (const sq of my) {
      if (sq.order.mode === 'rout' || sq.pending || this.battle.time < sq.nextDecision) continue;
      if (sq.reserve && !this.reserveCommitted) continue; // резерв ждёт своего часа
      const order = this.decide(sq, my, en);
      if (order && this.differs(sq.order, order)) {
        if (order.claim) this.claims.add(order.claim);
        if (this.send(sq, order)) sq.nextDecision = this.battle.time + 5 + Math.random() * 2;
      }
    }
    this.flushLog();
  }

  /** Одинаковые приказы разным отрядам — одной строкой летописи. */
  flushLog() {
    const groups = new Map();
    for (const e of this.outbox) {
      const key = e.via + '|' + e.order.kind + '|' + (e.order.why || '');
      if (!groups.has(key)) groups.set(key, { ...e, names: [] });
      groups.get(key).names.push(e.sq.name);
    }
    for (const g of groups.values()) {
      const who = g.names.join(', ');
      const what = ORDER_TEXT[g.order.kind].toLowerCase() + (g.order.why ? ' (' + g.order.why + ')' : '');
      this.battle.addLog(this.team, g.via === 'voice' ? `${this.title} → ${who}: ${what}` : `${this.title} шлёт ${g.names.length > 1 ? 'гонцов' : 'гонца'} → ${who}: ${what}`);
    }
    this.outbox = [];
  }

  /** Полководец держится позади войска, по возможности на высоте, и уходит от опасности. */
  updatePost(my) {
    const c = Commander.center(my), all = this.battle.squads.filter((s) => s.team !== this.team && !s.special && s.alive > 0);
    const e = Commander.center(all) || { x: c.x, z: -(this.team === 0 ? -1 : 1) * FIELD };
    const away = norm2(c.x - e.x, c.z - e.z);
    const back = this.role === 'captain' ? 11 : 18;
    let p = { x: c.x + away.x * back, z: c.z + away.z * back };
    const hill = world.an.high.find((h) => d2d(h, p) < 22 && !all.some((s) => d2d(s.center, h) < 20));
    if (hill) p = { x: hill.x, z: hill.z };
    const u = this.unit;
    for (const foe of this.battle.teams[1 - this.team]) {
      if (!foe.t.special && d2d(foe.pos, u.pos) < 14) {
        const a = norm2(u.pos.x - foe.pos.x, u.pos.z - foe.pos.z);
        p = { x: u.pos.x + a.x * 20, z: u.pos.z + a.z * 20 };
        break;
      }
    }
    p = clampField(p);
    this.post.set(p.x, 0, p.z);
  }

  chooseStance(my, en) {
    const myR = Commander.count(my, (q) => q.t.ranged), enR = Commander.count(en, (q) => q.t.ranged);
    const myAll = Commander.count(my, () => true), enAll = Commander.count(en, () => true);
    let st = this.trait === 'fierce' ? 'attack' : this.trait === 'cunning' ? 'maneuver' : myAll > enAll * 1.4 ? 'attack' : 'defend';
    // Стоять под превосходящим обстрелом глупо — сближаемся
    if (st === 'defend' && enR > myR * 1.5 && this.battle.time > 15) st = 'maneuver';
    if (st !== this.stance) this.battle.addLog(this.team, `${this.title}: ${STANCE_TEXT[st]}`);
    this.stance = st;
    this.stanceT = this.battle.time;
  }

  decide(sq, my, en) {
    if (this.role === 'captain' && en.length) {
      const o = this.missionOrder(sq, my, en);
      if (o !== undefined) return o;
    }
    if (!en.length) return sq.order.mode === 'advance' ? null : { kind: 'advance', mode: 'advance', why: 'враг скрылся — искать его' };
    const ec = Commander.center(en);
    if (sq.t.ranged) return this.decideRanged(sq, my, en, ec);
    if (sq.t.mount) return this.decideCavalry(sq, my, en, ec);
    return this.decideInfantry(sq, my, en, ec);
  }

  // ---------------------------------------------------------------- задачи крыльев

  /** Приказ, прямо вытекающий из задачи крыла (undefined — решать как обычно). */
  missionOrder(sq, my, en) {
    const m = this.mission, B = this.battle, wc = Commander.center(my);
    if (m.kind === 'flank' && !this.flankDone && wc) {
      if (d2d(wc, m) < 14 || B.time - (m.t || 0) > 40) {
        this.flankDone = true;
        B.addLog(this.team, `${this.title}: ${WING_NAMES[this.wing.key]} вышло во фланг — бьём!`);
        return undefined;
      }
      const p = clampField({ x: m.x + (sq.center.x - wc.x) * 0.6, z: m.z + (sq.center.z - wc.z) * 0.6 });
      return { kind: 'flank', mode: 'move', x: p.x, z: p.z, then: { kind: 'advance', mode: 'advance' }, why: `${WING_NAMES[this.wing.key]} идёт в обход` };
    }
    if (m.kind === 'support' && m.wing) {
      const tc = Commander.center(m.wing.squads.filter((s) => s.alive > 0));
      if (tc && d2d(sq.center, tc) > 22 && !sq.engaged)
        return { kind: 'support', mode: 'move', ...clampField({ x: lerp(sq.center.x, tc.x, 0.8), z: lerp(sq.center.z, tc.z, 0.8) }), then: { kind: 'advance', mode: 'advance' }, why: `на помощь: ${WING_NAMES[m.wing.key]}` };
    }
    return undefined;
  }

  /** Главнокомандующий: сравнивает силы на каждом крыле и ставит воеводам задачи. */
  thinkWings() {
    const B = this.battle, wings = B.wings[this.team].filter((w) => w.squads.some((s) => s.alive > 0));
    const en = this.foeSquads();
    if (!wings.length || !en.length) return;
    const val = (q) => q.alive * (q.t.mount ? 2.2 : q.t.ranged ? 0.8 : q.type === 1 ? 1.1 : 1);
    const info = wings.map((w) => {
      const sq = w.squads.filter((s) => s.alive > 0), c = Commander.center(sq);
      const x0 = Math.min(...sq.map((q) => q.center.x)) - 12, x1 = Math.max(...sq.map((q) => q.center.x)) + 12;
      const str = sq.reduce((a, q) => a + val(q), 0);
      const opp = en.filter((e) => e.center.x >= x0 && e.center.x <= x1).reduce((a, q) => a + val(q), 0);
      return { w, c, str, ratio: str / Math.max(1, opp), engaged: sq.some((q) => q.engaged), cav: sq.filter((q) => q.t.mount).reduce((a, q) => a + q.alive, 0) };
    });
    const ec = Commander.center(en);
    const myTot = info.reduce((a, i) => a + i.str, 0), enTot = en.reduce((a, q) => a + val(q), 0);
    const plan = new Map();
    for (const i of info) {
      let kind;
      if (this.trait === 'fierce') kind = i.ratio < 0.45 ? 'hold' : 'attack';
      else if (this.trait === 'cautious') kind = i.ratio > 1.35 ? 'attack' : 'hold';
      else kind = i.w.key === 'center' ? (i.ratio > 1.6 ? 'attack' : 'hold') : i.ratio > 1.1 ? 'attack' : 'hold';
      plan.set(i.w, { kind });
    }
    // Хитрый посылает сильнейшее по коннице крыло в обход
    if (this.trait === 'cunning') {
      const outer = info.filter((i) => i.w.key !== 'center' && i.ratio > 0.9 && !i.engaged).sort((a, b) => b.cav - a.cav || b.ratio - a.ratio)[0];
      if (outer) {
        const side = Math.sign(outer.c.x - ec.x) || 1;
        plan.set(outer.w, { kind: 'flank', ...clampField({ x: ec.x + side * 32, z: ec.z + (this.team === 0 ? 1 : -1) * 6 }, 6), t: B.time });
      }
    }
    // Проигрывающему крылу — помощь соседа и резерв
    const losing = info.filter((i) => i.engaged && i.ratio < 0.6).sort((a, b) => a.ratio - b.ratio)[0];
    if (losing) {
      const helper = info.filter((i) => i !== losing && !i.engaged && i.ratio > 1).sort((a, b) => d2d(a.c, losing.c) - d2d(b.c, losing.c))[0];
      if (helper && this.trait !== 'fierce') plan.set(helper.w, { kind: 'support', wing: losing.w });
      this.commitReserve(losing);
    } else if (myTot > enTot * 1.3 || B.time > 60) this.commitReserve(null);
    for (const [w, m] of plan) this.sendMission(w, m);
  }

  commitReserve(target) {
    if (this.reserveCommitted) return;
    const res = this.allMine().filter((s) => s.reserve);
    if (!res.length) return;
    this.reserveCommitted = true;
    for (const sq of res) {
      const o = target
        ? { kind: 'support', mode: 'move', ...clampField({ x: target.c.x, z: target.c.z }), then: { kind: 'advance', mode: 'advance' }, why: `резерв на помощь: ${WING_NAMES[target.w.key]}` }
        : { kind: 'advance', mode: 'advance', why: 'резерв — в бой!' };
      this.send(sq, o);
    }
    this.battle.addLog(this.team, `${this.title}: ввожу резерв${target ? ' — на помощь, ' + WING_NAMES[target.w.key] : ', пора добивать'}`);
  }

  sendMission(w, m) {
    const cur = w.pendingMission || w.mission || {};
    if (cur.kind === m.kind && cur.wing === m.wing && (m.x == null || Math.hypot((cur.x ?? 1e9) - m.x, (cur.z ?? 1e9) - m.z) < 12)) return;
    const cap = w.captain, B = this.battle;
    if (!cap || !cap.unit.alive) { w.mission = m; return; }
    const text = `воевода ${cap.name}, ${WING_NAMES[w.key]}: ${MISSION_TEXT[m.kind]}${m.kind === 'support' ? ' (' + WING_NAMES[m.wing.key] + ')' : ''}`;
    if (d2d(cap.unit.pos, this.unit.pos) < 16) { cap.receive(m); B.addLog(this.team, `${this.title} → ${text}`); return; }
    w.pendingMission = m;
    B.spawnMessenger(this, null, null, { captain: cap, mission: m, wing: w });
    B.addLog(this.team, `${this.title} шлёт гонца → ${text}`);
  }

  /** Воевода получил новую задачу крыла: пересматриваем приказы отрядам сразу. */
  receive(m) {
    this.mission = m;
    this.wing.mission = m;
    this.wing.pendingMission = null;
    this.flankDone = false;
    for (const q of this.mySquads()) q.nextDecision = 0;
    this.nextThink = 0.2;
  }

  // ---------------------------------------------------------------- стрелки

  decideRanged(sq, my, en, ec) {
    const B = this.battle;
    const threat = en.find((e) => !e.t.ranged && d2d(e.center, sq.center) < 22);
    if (threat && !my.some((q) => !q.t.ranged && !q.t.mount && q.order.mode !== 'rout' && d2d(q.center, sq.center) < 12)) {
      const guards = my.filter((q) => !q.t.ranged && !q.t.mount && q.order.mode !== 'rout');
      const guard = Commander.nearest(guards, sq.center);
      const away = norm2(sq.center.x - threat.center.x, sq.center.z - threat.center.z);
      const p = guard ? { x: guard.center.x + away.x * 7, z: guard.center.z + away.z * 7 } : { x: sq.center.x + away.x * 20, z: sq.center.z + away.z * 20 };
      return { kind: 'withdraw', mode: 'move', ...clampField(p), then: { kind: 'hold', mode: 'hold' }, why: `к ним рвутся ${threat.name}` };
    }
    const enR = Commander.count(en, (q) => q.t.ranged), myR = Commander.count(my, (q) => q.t.ranged);
    const underFire = B.time - sq.lastHitT < 3;
    if ((underFire && enR > myR * 1.1) || (this.trait === 'cautious' && enR > myR * 1.3)) {
      const c = this.coverFor(sq, ec);
      if (c && d2d(c, sq.center) > 5) return { kind: 'cover', mode: 'move', x: c.x, z: c.z, then: { kind: 'hold', mode: 'hold' }, why: { wall: 'за ограду', house: 'за дома', low: world.type === 'forest' ? 'в чащу' : world.type === 'swamp' ? 'в камыши' : 'в низину' }[c.kind] + ', от чужих болтов' };
    }
    if (world.prominenceAt(sq.center.x, sq.center.z) < 1.0 && sq.order.kind !== 'cover') {
      const h = this.highNear(sq.center, 36, en, ec);
      if (h) return { kind: 'high', mode: 'move', x: h.x, z: h.z, then: { kind: 'hold', mode: 'hold' }, why: 'с высоты бьют дальше' };
    }
    if (sq.order.mode === 'hold' && B.time - sq.lastShotT > 7 && B.time - sq.orderT > 7) {
      const v = norm2(ec.x - sq.center.x, ec.z - sq.center.z);
      return { kind: 'advance', mode: 'move', ...clampField({ x: sq.center.x + v.x * 14, z: sq.center.z + v.z * 14 }), then: { kind: 'hold', mode: 'hold' }, why: 'враг вне выстрела' };
    }
    if (sq.order.mode === 'advance') return { kind: 'hold', mode: 'hold', why: 'стоять и стрелять' };
    return null;
  }

  // ---------------------------------------------------------------- конница

  decideCavalry(sq, my, en, ec) {
    const o = sq.order;
    if (o.kind === 'charge' && sq.engagedFor > 3.5 && this.trait !== 'fierce') {
      const back = norm2(sq.center.x - ec.x, sq.center.z - ec.z);
      return { kind: 'withdraw', mode: 'move', ...clampField({ x: sq.center.x + back.x * 24, z: sq.center.z + back.z * 24 }), then: { kind: 'hold', mode: 'hold', leash: 14 }, why: 'выйти из свалки и разогнаться снова' };
    }
    const exposed = en.filter((e) => e.t.ranged && !en.some((q) => !q.t.ranged && q !== e && d2d(q.center, e.center) < 14));
    if (exposed.length) {
      const target = Commander.nearest(exposed, sq.center);
      if (this.trait === 'cunning' && d2d(target.center, sq.center) > 30) {
        const fp = this.flankPoint(target, sq.center, my);
        return { kind: 'flank', mode: 'move', x: fp.x, z: fp.z, then: { kind: 'charge', mode: 'charge', target }, why: `в обход, цель — ${target.name}` };
      }
      return { kind: 'charge', mode: 'charge', target, why: `${target.name} остались без прикрытия` };
    }
    const pinned = en.filter((e) => e.engaged && !e.t.ranged);
    if (pinned.length) {
      const target = Commander.nearest(pinned, sq.center);
      return { kind: 'charge', mode: 'charge', target, why: `${target.name} связаны боем — удар во фланг` };
    }
    if (this.stance === 'defend') {
      const inf = my.filter((q) => !q.t.ranged && !q.t.mount), c = Commander.center(inf) || sq.center;
      const back = norm2(c.x - ec.x, c.z - ec.z), side = sq.center.x >= c.x ? 1 : -1;
      const p = clampField({ x: c.x + back.x * 10 - back.z * 14 * side, z: c.z + back.z * 10 + back.x * 14 * side });
      return { kind: 'reserve', mode: 'hold', x: p.x, z: p.z, leash: 18, why: 'ждать, пока враг ввяжется' };
    }
    return { kind: 'advance', mode: 'advance' };
  }

  // ---------------------------------------------------------------- пехота

  decideInfantry(sq, my, en, ec) {
    const B = this.battle;
    // 1. Закрыть стрелков от конницы (варвары — лучше всех)
    for (const xb of my.filter((q) => q.t.ranged)) {
      const cav = en.find((e) => e.t.mount && d2d(e.center, xb.center) < 34);
      if (!cav || d2d(sq.center, xb.center) > 40) continue;
      const barbNear = my.some((q) => q.type === 1 && q !== sq && d2d(q.center, xb.center) < 40 && !this.claims.has('screen:' + xb.id));
      if (this.claims.has('screen:' + xb.id)) continue;
      if (sq.type === 1 || !barbNear) {
        const v = norm2(cav.center.x - xb.center.x, cav.center.z - xb.center.z);
        return { kind: 'screen', mode: 'move', claim: 'screen:' + xb.id, ...clampField({ x: xb.center.x + v.x * 6, z: xb.center.z + v.z * 6 }), then: { kind: 'hold', mode: 'hold', leash: 12, claim: 'screen:' + xb.id }, why: `${cav.name} угрожают стрелкам (${xb.name})` };
      }
    }
    // 2. Засада в низине (хитрый, в начале боя)
    if (this.trait === 'cunning' && !this.ambushSet && B.time < 30 && !sq.engaged) {
      const hollow = world.an.hide
        .filter((l) => d2d(l, sq.center) < 45 && d2d(l, ec) < d2d(sq.center, ec) + 5 && d2d(l, ec) > 25)
        .sort((a, b) => b.depth - a.depth)[0];
      if (hollow) {
        this.ambushSet = true;
        return { kind: 'ambush', mode: 'move', x: hollow.x, z: hollow.z, then: { kind: 'ambush', mode: 'ambush', leash: 14 }, why: world.type === 'forest' ? 'затаиться в чаще и ждать' : world.type === 'swamp' ? 'затаиться в камышах' : 'затаиться в низине и ждать' };
      }
    }
    // 3. Оборона: занять холм и ждать
    if (this.stance === 'defend' && !sq.engaged) {
      if (world.prominenceAt(sq.center.x, sq.center.z) < 1.0) {
        const h = this.highNear(sq.center, 32, en, ec);
        if (h) return { kind: 'high', mode: 'move', x: h.x, z: h.z, then: { kind: 'hold', mode: 'hold', leash: 11 }, why: 'пусть враг лезет вверх' };
      }
      if (sq.order.mode === 'advance') return { kind: 'hold', mode: 'hold', leash: 11, why: 'держим строй' };
      return null;
    }
    // 4. Свои связали врага боем — обойти и ударить во фланг
    if (!sq.engaged && (this.trait !== 'fierce' || Math.random() < 0.3)) {
      const pinned = en.filter((e) => e.engaged && !e.t.ranged && d2d(e.center, sq.center) > 14 && d2d(e.center, sq.center) < 70 && !this.claims.has('flank:' + e.id));
      if (pinned.length) {
        const target = Commander.nearest(pinned, sq.center);
        const fp = this.flankPoint(target, sq.center, my);
        return { kind: 'flank', mode: 'move', claim: 'flank:' + target.id, x: fp.x, z: fp.z, then: { kind: 'charge', mode: 'charge', target, claim: 'flank:' + target.id }, why: `${target.name} связаны боем` };
      }
    }
    // 5. Враг засел на холме: не лезть в лоб, если есть чем стрелять
    const target = Commander.nearest(en, sq.center);
    if (this.trait !== 'fierce' && world.prominenceAt(target.center.x, target.center.z) > 1.2 && !sq.engaged) {
      const myR = Commander.count(my, (q) => q.t.ranged), enR = Commander.count(en, (q) => q.t.ranged);
      if (myR > enR && sq.order.kind !== 'hold') return { kind: 'hold', mode: 'hold', leash: 10, why: `${target.name} на холме — пусть их выбьют арбалеты` };
      if (myR <= enR && sq.order.kind !== 'flank') {
        const fp = this.flankPoint(target, sq.center, my);
        return { kind: 'flank', mode: 'move', x: fp.x, z: fp.z, then: { kind: 'charge', mode: 'charge', target }, why: `${target.name} на холме — обойти склон` };
      }
    }
    return sq.order.mode === 'advance' || sq.order.mode === 'charge' ? null : { kind: 'advance', mode: 'advance', why: 'в атаку' };
  }

  // ---------------------------------------------------------------- рельеф глазами полководца

  highNear(p, maxD, en, ec) {
    const dp = d2d(p, ec);
    let best = null, bs = Infinity;
    for (const h of world.an.high) {
      const d = d2d(h, p);
      if (d > maxD || en.some((e) => d2d(e.center, h) < 14)) continue;
      if (d2d(h, ec) < dp - 25) continue; // не лезть к врагу в пасть
      const s = d - h.prom * 3;
      if (s < bs) { bs = s; best = h; }
    }
    return best;
  }

  /** Укрытие: низина, чаща, камыш, обратная сторона ограды или дома — не ближе к врагу. */
  coverFor(sq, ec) {
    const dNow = d2d(sq.center, ec);
    let best = null, bd = Infinity;
    for (const c of world.coverCandidates(ec)) {
      const d = d2d(c, sq.center);
      if (d > 34 || d2d(c, ec) < dNow - 8 || !world.walkable(c.x, c.z, 0.5)) continue;
      if (d < bd) { bd = d; best = c; }
    }
    if (best) best.wall = best.kind !== 'low';
    return best;
  }

  flankPoint(target, from, my) {
    const mc = Commander.center(my) || from;
    const v = norm2(target.center.x - mc.x, target.center.z - mc.z), perp = { x: -v.z, z: v.x };
    const side = Math.sign(perp.x * (from.x - target.center.x) + perp.z * (from.z - target.center.z)) || 1;
    return clampField({ x: target.center.x + perp.x * 16 * side + v.x * 6, z: target.center.z + perp.z * 16 * side + v.z * 6 });
  }

  differs(cur, next) {
    if (cur.kind !== next.kind) return true;
    if (next.target !== cur.target) return true;
    if (next.x != null && cur.x != null && Math.hypot(next.x - cur.x, next.z - cur.z) > 8) return true;
    return false;
  }

  /** Рядом — приказ голосом, далеко — с гонцом (его могут перехватить). */
  send(sq, order) {
    const B = this.battle;
    if (d2d(this.unit.pos, sq.center) < 16) {
      B.applyOrder(sq, order);
      this.outbox.push({ sq, order, via: 'voice' });
      return true;
    }
    const busy = B.units.filter((m) => m.alive && m.carry && m.carry.cmd === this && !m.carry.delivered).length;
    if (busy >= 5) return false;
    sq.pending = order;
    B.spawnMessenger(this, sq, order);
    this.outbox.push({ sq, order, via: 'messenger' });
    return true;
  }
}
