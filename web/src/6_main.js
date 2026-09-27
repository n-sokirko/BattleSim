// ------------------------------------------------------------------ сцена

const canvas = $('c');
const renderer = new THREE.WebGLRenderer({ canvas, antialias: !LOW_END, powerPreference: 'high-performance' });
renderer.setPixelRatio(Math.min(devicePixelRatio, LOW_END ? 1.25 : 2));
renderer.outputColorSpace = THREE.SRGBColorSpace;
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFSoftShadowMap;

const scene = new THREE.Scene();
const camera = new THREE.PerspectiveCamera(48, 1, 0.5, 3000);
const SKY = new Sky();
SKY.scale.setScalar(2500);
scene.add(SKY);
const PMREM = new THREE.PMREMGenerator(renderer);

const sunLight = new THREE.DirectionalLight(0xffffff, 2.5);
sunLight.castShadow = true;
sunLight.shadow.mapSize.set(LOW_END ? 1024 : 2048, LOW_END ? 1024 : 2048);
Object.assign(sunLight.shadow.camera, { left: -70, right: 70, top: 70, bottom: -70, near: 1, far: 500 });
sunLight.shadow.bias = -0.0004;
sunLight.shadow.normalBias = 0.05;
scene.add(sunLight, sunLight.target);
const hemiLight = new THREE.HemisphereLight(0xffffff, 0x445533, 0.35);
scene.add(hemiLight);

const unitLayer = new THREE.Group();
scene.add(unitLayer);
const world = new World();
const battle = new Battle();
const rig = new CameraRig(camera, canvas);

// Кольца цвета команды под солдатами
const rings = new THREE.InstancedMesh(new THREE.RingGeometry(0.78, 1, 28).rotateX(-Math.PI / 2),
  new THREE.MeshBasicMaterial({ transparent: true, opacity: 0.6, depthWrite: false, polygonOffset: true, polygonOffsetFactor: -2 }), 1000);
rings.count = 0; rings.frustumCulled = false; rings.renderOrder = 1;
scene.add(rings);
const ringColors = TEAM.map((t) => new THREE.Color(t.css));
const ringHidden = TEAM.map((t) => new THREE.Color(t.css).lerp(new THREE.Color(0x222222), 0.65));
const ringRout = new THREE.Color(0xf2e6b0);

function updateRings() {
  const m4 = new THREE.Matrix4();
  let n = 0;
  for (const u of battle.units) {
    if (!u.alive || n >= 1000) continue;
    const k = u.t.radius * 1.25;
    m4.makeScale(k, 1, k).setPosition(u.pos.x, u.pos.y + 0.07, u.pos.z);
    rings.setMatrixAt(n, m4);
    const sq = u.squad;
    const col = sq && sq.hidden ? ringHidden[u.team] : sq && sq.order.mode === 'rout' ? ringRout : ringColors[u.team];
    rings.setColorAt(n, col);
    n++;
  }
  rings.count = n;
  rings.instanceMatrix.needsUpdate = true;
  if (rings.instanceColor) rings.instanceColor.needsUpdate = true;
}

// Призрак отряда под курсором
const ghost = new THREE.InstancedMesh(new THREE.CircleGeometry(0.55, 18).rotateX(-Math.PI / 2),
  new THREE.MeshBasicMaterial({ transparent: true, opacity: 0.5, depthWrite: false }), 24);
ghost.count = 0; ghost.frustumCulled = false; ghost.renderOrder = 2;
scene.add(ghost);

// ------------------------------------------------------------------ состояние игры

const game = {
  phase: 'loading', type: 0, team: 0, eraser: false, speed: 1, paused: false,
  winner: -1, battleTime: 0, resultDelay: 0, seed: 1, style: null, lastBiome: -1,
};

function newMap(seed) {
  game.seed = seed ?? ((Math.random() * 99999) | 1);
  const r = mulberry32(game.seed);
  let biome = (r() * BIOMES.length) | 0;
  if (biome === game.lastBiome) biome = (biome + 1 + ((r() * (BIOMES.length - 1)) | 0)) % BIOMES.length;
  game.lastBiome = biome;
  game.style = makeStyle(biome, (r() * TIMES.length) | 0);
  world.generate(game.seed, game.style, ASSETS);
  battle.resetToPlan();
  game.phase = 'setup';
  rig.cinematic = false;
  rig.lookAt(0, -48, 0, 34 * DEG, 85, true);
  $('mapName').textContent = game.style.title + ' · карта №' + game.seed;
  updateWind();
  refreshUI();
}

function startBattle() {
  if (battle.planCount[0] === 0 || battle.planCount[1] === 0) { toast('Нужны обе армии: поставьте и синих, и красных'); return; }
  if (game.phase !== 'setup') battle.resetToPlan();
  $('chronicle').innerHTML = '';
  battle.startFight();
  Object.assign(game, { phase: 'fight', paused: false, speed: 1, winner: -1, battleTime: 0, resultDelay: 0, eraser: false });
  ghost.count = 0;
  refreshUI();
}

function stopBattle() {
  battle.resetToPlan();
  game.phase = 'setup';
  game.paused = false;
  rig.cinematic = false;
  refreshUI();
}

function placeAt(p) {
  if (game.phase !== 'setup' || !p) return;
  if (game.eraser) {
    if (battle.removeNear(p.x, p.z, 4) === 0) toast('Здесь никого нет');
    refreshUI();
    return;
  }
  if (!World.inField(p.x, p.z, 1)) { toast('Ставить отряды можно только на поле боя'); return; }
  const n = battle.placeSquad(game.type, game.team, p.x, p.z, game.team === 0 ? 0 : Math.PI);
  if (n === 0) toast(battle.units.length >= MAX_UNITS ? `Предел — ${MAX_UNITS} солдат` : 'Здесь тесно — выберите другое место');
  refreshUI();
}

rig.onTap = (x, y) => placeAt(rig.groundPoint(x, y));
rig.onHover = (x, y) => {
  if (game.phase !== 'setup' || game.eraser) { ghost.count = 0; return; }
  const p = rig.groundPoint(x, y);
  if (!p || !World.inField(p.x, p.z, 1)) { ghost.count = 0; return; }
  const t = TYPES[game.type], yaw = game.team === 0 ? 0 : Math.PI, cos = Math.cos(yaw), sin = Math.sin(yaw);
  const m4 = new THREE.Matrix4();
  let i = 0;
  for (let r = 0; r < t.rows; r++) for (let c = 0; c < t.cols; c++) {
    const ox = (c - (t.cols - 1) / 2) * t.spacing, oz = -(r - (t.rows - 1) / 2) * t.spacing;
    const gx = p.x + ox * cos + oz * sin, gz = p.z - ox * sin + oz * cos;
    const k = t.radius * 1.6;
    m4.makeScale(k, 1, k).setPosition(gx, world.heightAt(gx, gz) + 0.06, gz);
    ghost.setMatrixAt(i++, m4);
  }
  ghost.count = i;
  ghost.instanceMatrix.needsUpdate = true;
  ghost.material.color.set(TEAM[game.team].css);
};

// ------------------------------------------------------------------ интерфейс

function toast(text, ms = 2600) {
  const el = $('toast');
  el.textContent = text;
  el.hidden = false;
  clearTimeout(toast.t);
  toast.t = setTimeout(() => (el.hidden = true), ms);
}

function buildUI() {
  const cards = $('cards');
  TYPES.forEach((t, i) => {
    const b = document.createElement('button');
    b.className = 'card';
    b.id = 'type-' + t.key;
    b.innerHTML = `<span class="card-key">${i + 1}</span><b>${t.name}</b><small>${t.hp} ОЗ · урон ${t.dmg}${t.ranged ? ' · ' + t.range + ' м' : ''}</small><small class="note">${t.note}</small>`;
    b.onclick = () => { game.type = i; game.eraser = false; refreshUI(); };
    cards.appendChild(b);
  });
  TEAM.forEach((tm, i) => { $('team-' + i).onclick = () => { game.team = i; game.eraser = false; refreshUI(); }; });
  $('eraser').onclick = () => { game.eraser = !game.eraser; ghost.count = 0; refreshUI(); };
  $('fight').onclick = startBattle;
  $('newMap').onclick = () => { newMap(); toast('Карта: ' + game.style.title); };
  const makeArmies = () => { battle.randomArmies(+$('armySize').value); refreshUI(); toast(`Армии: ${battle.planCount[0]} синих против ${battle.planCount[1]} красных`); };
  $('random').onclick = makeArmies;
  $('armySize').onchange = makeArmies;
  $('clear').onclick = () => { battle.clearAll(); refreshUI(); };
  $('pause').onclick = () => { game.paused = !game.paused; refreshUI(); };
  [['slow', 0.25], ['normal', 1], ['fast', 2]].forEach(([id, s]) => ($(id).onclick = () => { game.speed = s; game.paused = false; refreshUI(); }));
  $('orbit').onclick = () => { rig.cinematic = !rig.cinematic; refreshUI(); };
  $('stop').onclick = stopBattle;
  $('rematch').onclick = () => { startBattle(); };
  $('edit').onclick = stopBattle;
  $('resultMap').onclick = () => { stopBattle(); newMap(); };
  $('cmdToggle').onclick = () => { battle.useCommanders = !battle.useCommanders; refreshUI(); };
  for (const id of ['helpBtn', 'helpBtn2']) $(id).onclick = () => { $('helpBox').hidden = false; game.pausedByHelp = game.phase === 'fight' && !game.paused; if (game.pausedByHelp) game.paused = true; refreshUI(); };
  $('helpClose').onclick = () => { $('helpBox').hidden = true; if (game.pausedByHelp) game.paused = false; game.pausedByHelp = false; refreshUI(); };
  battle.onLog = pushLog;

  addEventListener('keydown', (e) => {
    if (e.code === 'Space' && game.phase === 'fight') { game.paused = !game.paused; refreshUI(); e.preventDefault(); }
    if (e.code === 'Tab') { rig.cinematic = !rig.cinematic; refreshUI(); e.preventDefault(); }
    if (game.phase === 'setup' && /^Digit[1-4]$/.test(e.code)) { game.type = +e.code.slice(5) - 1; game.eraser = false; refreshUI(); }
    if (game.phase === 'setup' && e.code === 'KeyT') { game.team = 1 - game.team; refreshUI(); }
    if (game.phase === 'setup' && e.code === 'Enter') startBattle();
  });
}

const pressed = (id, on) => $(id).setAttribute('aria-pressed', on ? 'true' : 'false');

function refreshUI() {
  const setup = game.phase === 'setup', fight = game.phase === 'fight', result = game.phase === 'result';
  $('setupTop').hidden = !setup; $('setupBar').hidden = !setup;
  $('fightTop').hidden = setup; $('fightBar').hidden = !fight;
  $('result').hidden = !result;
  TYPES.forEach((t, i) => pressed('type-' + t.key, !game.eraser && game.type === i));
  TEAM.forEach((_, i) => pressed('team-' + i, game.team === i));
  pressed('eraser', game.eraser);
  document.body.dataset.team = game.team;
  $('fight').disabled = !(battle.planCount[0] > 0 && battle.planCount[1] > 0);
  pressed('pause', game.paused); $('pause').textContent = game.paused ? 'Дальше' : 'Пауза';
  pressed('slow', game.speed === 0.25); pressed('normal', game.speed === 1); pressed('fast', game.speed === 2);
  pressed('orbit', rig.cinematic);
  pressed('cmdToggle', battle.useCommanders);
  $('cmdToggle').textContent = battle.useCommanders ? 'Полководцы: есть' : 'Полководцы: нет';
  $('chronicle').hidden = setup;
  if (setup && battle.useCommanders && battle.planCount[0] && battle.planCount[1]) {
    const a = battle.commanderSpec(0), b = battle.commanderSpec(1);
    $('cmdInfo').textContent = `Синих ведёт ${a.name} (${TRAITS[a.trait].name}), красных — ${b.name} (${TRAITS[b.trait].name})`;
  } else $('cmdInfo').textContent = setup ? 'Без полководцев отряды бьются кто во что горазд' : '';
  $('hint').textContent = game.eraser ? 'Нажмите на солдат, чтобы убрать отряд'
    : IS_TOUCH ? 'Нажмите на землю — поставить отряд. Один палец двигает карту, два — зум и поворот'
    : 'Клик — поставить отряд · WASD — двигать · правая кнопка — вращать · колесо — зум · T — сменить армию';
  if (result) {
    const w = game.winner;
    $('resultTitle').textContent = w === 0 ? 'Победа синих' : w === 1 ? 'Победа красных' : 'Ничья';
    $('resultTitle').dataset.team = w;
    $('resultText').textContent = w >= 0
      ? `Выжило ${battle.alive[w]} из ${battle.planCount[w]} · бой длился ${Math.round(game.battleTime)} с`
      : 'Никто не выжил';
  }
  updateTally();
}

function updateTally() {
  const f = game.phase !== 'setup';
  const a = f ? battle.alive : battle.planCount;
  $('nBlue').textContent = f ? `${a[0]} / ${battle.planCount[0]}` : a[0];
  $('nRed').textContent = f ? `${a[1]} / ${battle.planCount[1]}` : a[1];
  const total = Math.max(1, a[0] + a[1]);
  $('barBlue').style.width = (a[0] / total * 100) + '%';
  $('barRed').style.width = (a[1] / total * 100) + '%';
}

// ------------------------------------------------------------------ летопись, подписи, ветер

const fmtTime = (t) => `${Math.floor(t / 60)}:${String(Math.floor(t % 60)).padStart(2, '0')}`;

function pushLog(e) {
  const list = $('chronicle');
  const li = document.createElement('li');
  li.dataset.team = e.team;
  li.innerHTML = `<time>${fmtTime(e.t)}</time><span></span>`;
  li.lastChild.textContent = e.text;
  list.prepend(li);
  while (list.children.length > (innerHeight < 500 ? 3 : 6)) list.lastChild.remove();
}

const labelPool = new Map();
const projV = new THREE.Vector3();

function labelFor(key, cls) {
  let el = labelPool.get(key);
  if (!el) { el = document.createElement('div'); el.className = 'olabel ' + cls; $('labels').appendChild(el); labelPool.set(key, el); }
  return el;
}

function placeLabel(el, x, y, z, text, opacity) {
  projV.set(x, y, z).project(camera);
  if (projV.z > 1 || Math.abs(projV.x) > 1.1 || Math.abs(projV.y) > 1.1) { el.hidden = true; return; }
  el.hidden = false;
  if (el.textContent !== text) el.textContent = text;
  el.style.opacity = opacity;
  el.style.transform = `translate(${(projV.x + 1) / 2 * innerWidth}px, ${(1 - projV.y) / 2 * innerHeight}px) translate(-50%, -100%)`;
}

/** Над отрядами — свежие приказы, над полководцами — имя. */
function updateLabels() {
  const seen = new Set();
  if (game.phase !== 'setup') {
    for (const sq of battle.squads) {
      if (sq.alive === 0) continue;
      if (sq.special) {
        const c = battle.commanders[sq.team];
        if (!c || !c.unit.alive) continue;
        const el = labelFor('c' + sq.team, 'cmd team' + sq.team);
        placeLabel(el, c.unit.pos.x, c.unit.pos.y + 4.9, c.unit.pos.z, '★ ' + c.name, 1);
        seen.add('c' + sq.team);
        continue;
      }
      let text = null, op = 1;
      if (sq.labelT > 0) { text = ORDER_TEXT[sq.order.kind] || ''; op = Math.min(1, sq.labelT); }
      else if (sq.hidden) { text = 'затаились'; op = 0.7; }
      if (!text) continue;
      const el = labelFor(sq.id, 'team' + sq.team);
      placeLabel(el, sq.center.x, sq.center.y + (sq.t.mount ? 4 : 3.2), sq.center.z, text, op);
      seen.add(sq.id);
    }
  }
  for (const [k, el] of labelPool) if (!seen.has(k)) el.hidden = true;
}

function updateWind() {
  const w = world.wind, s = w.length();
  $('windText').textContent = s < 0.5 ? 'Безветрие' : `Ветер ${s.toFixed(0)} м/с`;
  $('windArrow').hidden = s < 0.5;
  const low = game.style && game.style.elev < 26;
  $('sunText').hidden = !low;
}

/** Стрелка ветра повёрнута относительно камеры: вверх = от зрителя вглубь сцены. */
function updateWindArrow() {
  const w = world.wind, yaw = rig.yaw;
  const sx = -w.x * Math.cos(yaw) + w.y * Math.sin(yaw), sy = w.x * Math.sin(yaw) + w.y * Math.cos(yaw);
  $('windArrow').style.transform = `rotate(${Math.atan2(sx, sy) * 180 / Math.PI}deg)`;
}

// ------------------------------------------------------------------ отрисовка толпы

const _m4 = new THREE.Matrix4(), _rm = new THREE.Matrix4(), _off = new THREE.Matrix4();
const _q = new THREE.Quaternion(), _p = new THREE.Vector3(), _s = new THREE.Vector3(), _up = new THREE.Vector3(0, 1, 0);

/** Раскладывает всех видимых солдат по инстанс-мешам: вид × команда × LOD. */
function renderCrowd(animDt) {
  const C = ASSETS.crowd;
  for (const m of ASSETS.crowdList) m.begin();
  const cp = camera.position, lod2 = LOD_DIST * LOD_DIST;
  for (const u of battle.units) {
    stepAnim(u.anim, animDt);
    if (u.ride) stepAnim(u.ride, animDt);
    sphere.center.set(u.pos.x, u.pos.y + 1, u.pos.z);
    if (!frustum.intersectsSphere(sphere)) continue;
    const lod = cp.distanceToSquared(u.pos) < lod2 ? 0 : 1;
    const sink = u.alive ? 0 : Math.max(0, u.deadT - 18) * 0.25;
    _q.setFromAxisAngle(_up, u.yaw);
    _m4.compose(_p.set(u.pos.x, u.pos.y - sink, u.pos.z), _q, _s.setScalar(u.scale));
    if (u.t.mount) {
      const hm = C.horse[u.horse], hr = hm.row(u.anim);
      hm.add(0, lod, _m4, hr, u.anim.prevRow, u.anim.blend);
      u.anim.lastRow = hr;
      const rm = C.rider[u.type], rr = rm.row(u.ride);
      if (u.alive) {
        const bob = u.curSpeed > 3.5 ? Math.abs(Math.sin(u.phase)) * 0.12 : 0;
        _off.makeTranslation(u.riderBase.x, u.riderBase.y + bob, u.riderBase.z);
      } else _off.makeTranslation(1.2, 0, -0.3); // всадник падает рядом с конём
      _rm.multiplyMatrices(_m4, _off);
      rm.add(u.team, lod, _rm, rr, u.ride.prevRow, u.ride.blend);
      u.ride.lastRow = rr;
      if (u.t.special === 'commander') u.bannerMatrix = (u.bannerMatrix || new THREE.Matrix4()).copy(_rm);
    } else {
      const im = C.inf[u.type], r = im.row(u.anim);
      im.add(u.team, lod, _m4, r, u.anim.prevRow, u.anim.blend);
      u.anim.lastRow = r;
    }
  }
  for (const m of ASSETS.crowdList) m.end();
}

/** Знамёна полководцев едут за всадником и колышутся на ветру. */
const banners = [null, null];
function updateBanners(time) {
  for (let team = 0; team < 2; team++) {
    const c = battle.commanders[team];
    let b = banners[team];
    if (!c || !c.unit.alive || !c.unit.bannerMatrix) { if (b) b.visible = false; continue; }
    if (!b) {
      b = banners[team] = new THREE.Group();
      const flag = makeBanner(team);
      flag.position.set(-0.3, ASSETS.riderHipsY, -0.38);
      b.add(flag);
      b.userData.flag = flag;
      b.matrixAutoUpdate = false;
      scene.add(b);
    }
    b.visible = true;
    b.matrix.copy(c.unit.bannerMatrix);
    b.matrixWorldNeedsUpdate = true;
    waveBanner(b.userData.flag, time, world.wind.length());
  }
}

// ------------------------------------------------------------------ цикл

function resize() {
  const w = innerWidth, h = innerHeight;
  renderer.setSize(w, h, false);
  camera.aspect = w / h;
  camera.updateProjectionMatrix();
}
addEventListener('resize', resize);

let last = performance.now(), frameNo = 0, tallyT = 0;
const frustum = new THREE.Frustum(), projView = new THREE.Matrix4(), sphere = new THREE.Sphere(new THREE.Vector3(), 2.5);

function frame(now) {
  requestAnimationFrame(frame);
  const dt = Math.min((now - last) / 1000, 0.05);
  last = now;
  if (game.phase === 'loading') return;
  frameNo++;

  rig.update(dt, rig.cinematic ? battle.centroid() : null);

  const simDt = game.phase === 'setup' || game.paused ? 0 : dt * game.speed;
  const steps = Math.max(1, Math.ceil(simDt / 0.034));
  for (let i = 0; i < steps; i++) battle.tick(simDt / steps);
  if (game.phase === 'fight') {
    game.battleTime += simDt;
    if (battle.alive[0] === 0 || battle.alive[1] === 0) {
      game.resultDelay += simDt;
      if (game.resultDelay > 1.5) {
        game.winner = battle.alive[0] > 0 ? 0 : battle.alive[1] > 0 ? 1 : -1;
        game.phase = 'result';
        rig.cinematic = true;
        refreshUI();
      }
    }
  }

  // Анимации: дальние и невидимые солдаты обновляются реже
  const animDt = game.phase === 'setup' ? dt : game.paused ? 0 : dt * game.speed;
  projView.multiplyMatrices(camera.projectionMatrix, camera.matrixWorldInverse);
  frustum.setFromProjectionMatrix(projView);
  const state = game.phase === 'result' ? 'cheer' : null;
  for (const u of battle.units) if (u.alive) animate(u, state && u.team === game.winner ? 'cheer' : null);
  renderCrowd(animDt);

  // Тени следуют за камерой
  const tgt = rig.target;
  sunLight.target.position.set(tgt.x, rig.groundY, tgt.z);
  sunLight.position.copy(sunLight.target.position).addScaledVector(world.sunDir, 200);
  const sh = clamp(rig.dist * 0.9, 45, 120);
  const sc = sunLight.shadow.camera;
  if (sc.right !== sh) { sc.left = -sh; sc.right = sh; sc.top = sh; sc.bottom = -sh; sc.updateProjectionMatrix(); }

  updateRings();
  updateBanners(now / 1000);
  updateLabels();
  updateWindArrow();
  if ((tallyT += dt) > 0.12) { tallyT = 0; updateTally(); }
  renderer.render(scene, camera);
}

// ------------------------------------------------------------------ запуск

async function start(saved) {
  resize();
  buildUI();
  requestAnimationFrame(frame);
  try {
    await loadAssets((p) => { $('loadBar').style.width = Math.round(p * 100) + '%'; });
  } catch (err) {
    $('loadText').textContent = 'Не удалось загрузить модели: ' + (err?.message || err);
    return;
  }
  initSharedWorldResources();
  battle.bolts.init();
  const probe = ASSETS.bolt; // наконечник болта: выбираем сторону, где модель шире у кончика
  BOLT_TIP = probe.tip || 1;
  newMap(saved?.seed);
  if (saved?.plan?.length) battle.setPlan(saved.plan);
  else battle.randomArmies(LOW_END ? 1 : 2);
  $('armySize').value = LOW_END ? '1' : '2';
  refreshUI();
  $('loading').hidden = true;
  toast('Карта: ' + game.style.title + '. Расставьте армии и жмите «В бой!»', 4200);
}

window.claude?.hot?.snapshot?.(() => ({ seed: game.seed, plan: battle.plan }));
if (window.claude?.hot?.ready) window.claude.hot.ready(start);
else start(window.claude?.hot?.data ?? {});
window.sechaDebug = { rig, battle, game, world, ASSETS, camera };
// Отладка: прокрутить симуляцию вперёд без отрисовки (для проверки в среде без полноценного rAF)
window.sechaDebug.step = (sec, dt = 1 / 30) => {
  for (let t = 0; t < sec; t += dt) {
    battle.tick(game.phase === 'setup' ? 0 : dt);
    if (game.phase === 'fight') game.battleTime += dt;
    for (const u of battle.units) { if (u.alive) animate(u, null); stepAnim(u.anim, dt); if (u.ride) stepAnim(u.ride, dt); }
  }
  updateTally();
  return { alive: battle.alive.slice(), bolts: battle.bolts.list.length };
};
