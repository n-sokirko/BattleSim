import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import * as SkeletonUtils from 'three/addons/utils/SkeletonUtils.js';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';
import { Sky } from 'three/addons/objects/Sky.js';

// ------------------------------------------------------------------ утилиты

const $ = (id) => document.getElementById(id);
const clamp = (v, a, b) => (v < a ? a : v > b ? b : v);
const clamp01 = (v) => clamp(v, 0, 1);
const lerp = (a, b, t) => a + (b - a) * t;
const smooth = (a, b, x) => { const t = clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); };
const DEG = Math.PI / 180;
const IS_TOUCH = matchMedia('(pointer: coarse)').matches;
const LOW_END = IS_TOUCH || (navigator.hardwareConcurrency || 8) <= 4;

function mulberry32(a) {
  return function () {
    a |= 0; a = (a + 0x6d2b79f5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

/** Классический 2D-шум Перлина. noise() ≈ -1..1, n01() ≈ 0..1 (как Mathf.PerlinNoise). */
class Perlin {
  constructor(rand) {
    const p = [...Array(256).keys()];
    for (let i = 255; i > 0; i--) { const j = Math.floor(rand() * (i + 1)); [p[i], p[j]] = [p[j], p[i]]; }
    this.p = new Uint8Array(512);
    for (let i = 0; i < 512; i++) this.p[i] = p[i & 255];
  }
  static grad(h, x, y) {
    switch (h & 7) {
      case 0: return x + y; case 1: return -x + y; case 2: return x - y; case 3: return -x - y;
      case 4: return x; case 5: return -x; case 6: return y; default: return -y;
    }
  }
  noise(x, y) {
    let X = Math.floor(x), Y = Math.floor(y);
    x -= X; y -= Y; X &= 255; Y &= 255;
    const u = x * x * x * (x * (x * 6 - 15) + 10), v = y * y * y * (y * (y * 6 - 15) + 10);
    const p = this.p, a = p[X] + Y, b = p[X + 1] + Y, g = Perlin.grad;
    return lerp(lerp(g(p[a], x, y), g(p[b], x - 1, y), u), lerp(g(p[a + 1], x, y - 1), g(p[b + 1], x - 1, y - 1), u), v) * 0.72;
  }
  n01(x, y) { return this.noise(x, y) * 0.5 + 0.5; }
  fbm(x, y, oct) {
    let s = 0, amp = 1, f = 1, norm = 0;
    for (let i = 0; i < oct; i++) { s += this.noise(x * f, y * f) * amp; norm += amp; amp *= 0.5; f *= 2.03; }
    return s / norm;
  }
  ridged(x, y, oct) {
    let s = 0, amp = 1, f = 1, norm = 0;
    for (let i = 0; i < oct; i++) { const n = 1 - Math.abs(this.noise(x * f, y * f)); s += n * n * amp; norm += amp; amp *= 0.5; f *= 2.1; }
    return s / norm;
  }
}

// ------------------------------------------------------------------ типы войск

/** Баланс армий. Цифры те же, что в Unity-версии (UnitTypes.cs). */
const TYPES = [
  { key: 'sword', name: 'Мечники', model: 'Knight', hp: 110, armor: 0.3, speed: 3.3, accel: 12, radius: 0.58, mass: 1,
    reach: 0.7, dmg: 18, cd: 1.05, atkTime: 0.75, arrowBlock: 0.55, vsCav: 1, charge: 1, cols: 5, rows: 3, spacing: 1.45,
    note: 'Щит гасит болты', keep: ['1H_Sword', 'Badge_Shield', 'Knight_Helmet', 'Knight_Cape'],
    anim: { idle: 'Idle', run: 'Running_A', attack: ['1H_Melee_Attack_Chop', '1H_Melee_Attack_Slice_Diagonal'], cheer: 'Cheer' } },
  { key: 'barb', name: 'Варвары', model: 'Barbarian', hp: 130, armor: 0.12, speed: 3.6, accel: 12, radius: 0.6, mass: 1.1,
    reach: 1.0, dmg: 30, cd: 1.5, atkTime: 0.85, arrowBlock: 0, vsCav: 2.2, charge: 1, cols: 4, rows: 3, spacing: 1.5,
    note: 'Топор рубит конницу', keep: ['2H_Axe', 'Barbarian_Hat', 'Barbarian_Cape'],
    anim: { idle: '2H_Melee_Idle', run: 'Running_A', attack: ['2H_Melee_Attack_Chop', '2H_Melee_Attack_Slice'], cheer: 'Cheer' } },
  { key: 'xbow', name: 'Арбалетчики', model: 'Rogue_Hooded', hp: 65, armor: 0.05, speed: 3.4, accel: 12, radius: 0.52, mass: 0.9,
    reach: 0.5, dmg: 26, cd: 2.3, atkTime: 0.7, ranged: true, range: 36, arrowBlock: 0, vsCav: 1, charge: 1, cols: 5, rows: 2, spacing: 1.45,
    note: 'Стреляют на 36 м', keep: ['2H_Crossbow', 'Rogue_Cape'],
    anim: { idle: '2H_Ranged_Aiming', run: 'Running_A', attack: ['2H_Ranged_Shoot'], aim: '2H_Ranged_Aiming', reload: '2H_Ranged_Reload', melee: '1H_Melee_Attack_Stab', cheer: 'Cheer' } },
  { key: 'knight', name: 'Рыцари', model: 'Knight', mount: true, hp: 220, armor: 0.38, speed: 7.5, accel: 5, radius: 1.1, mass: 3.5,
    reach: 0.9, dmg: 24, cd: 1.25, atkTime: 0.7, arrowBlock: 0.25, vsCav: 1, charge: 2.3, cols: 3, rows: 2, spacing: 2.7,
    note: 'Натиск ×2,3', keep: ['1H_Sword', 'Round_Shield', 'Knight_Helmet', 'Knight_Cape'],
    anim: { attack: ['1H_Melee_Attack_Slice_Horizontal', '1H_Melee_Attack_Slice_Diagonal'] } },
];
const ALL_ATTACHMENTS = ['1H_Sword_Offhand', 'Badge_Shield', 'Rectangle_Shield', 'Round_Shield', 'Spike_Shield', '1H_Sword', '2H_Sword',
  'Knight_Helmet', 'Knight_Cape', '1H_Axe_Offhand', 'Barbarian_Round_Shield', '1H_Axe', '2H_Axe', 'Mug', 'Barbarian_Hat', 'Barbarian_Cape',
  'Knife_Offhand', '1H_Crossbow', '2H_Crossbow', 'Knife', 'Throwable', 'Rogue_Cape'];

const TEAM = [
  { name: 'Синие', hue: 214, sat: 0.62, css: '#3f7fe6' },
  { name: 'Красные', hue: 2, sat: 0.66, css: '#d9483b' },
];
const MAX_UNITS = LOW_END ? 420 : 700;

// ------------------------------------------------------------------ стили мира

const BIOMES = ['Лето', 'Осень', 'Зима', 'Степь'];
const TIMES = ['день', 'закат', 'туманное утро'];
const C = (hex) => new THREE.Color(hex);

function makeStyle(biome, time) {
  const s = { biome, time, title: BIOMES[biome] + ', ' + TIMES[time] };
  Object.assign(s, {
    rock: C(0x7b7670), snow: C(0xf2f5fa), sand: C(0xd8c894), dirt: C(0x7a5d3f), underwater: C(0x4f6f5c),
    water: C(0x2a6f86), waterAlpha: 0.8, snowLine: 42, snowGround: false, treeDensity: 1, grassDensity: 1, flowers: 0.15,
    grass1: C(0x5e9a2f), grass2: C(0x86b440), flower: [C(0xf2e14a), C(0xf0f0f0), C(0xd9534f)], leafShift: null,
  });
  if (biome === 0) Object.assign(s, { grassA: C(0x5f9a35), grassB: C(0x78ad3c), dry: C(0xa9a852) });
  if (biome === 1) Object.assign(s, { grassA: C(0x8a8a3a), grassB: C(0x9c8c3c), dry: C(0xb8904a), grass1: C(0x8f8a3a), grass2: C(0xb09a45),
    flower: [C(0xe8b030), C(0xc8602a), C(0xa03a28)], flowers: 0.05, leafShift: 'autumn', water: C(0x255f70) });
  if (biome === 2) Object.assign(s, { grassA: C(0xe6ecf4), grassB: C(0xd7e0ec), dry: C(0x9aa08a), snowGround: true, snowLine: 18,
    grass1: C(0x9aa37a), grass2: C(0xb8bfa0), grassDensity: 0.25, flowers: 0, leafShift: 'winter', underwater: C(0x6a8a94), sand: C(0xc8ccc8), water: C(0x4b7c92) });
  if (biome === 3) Object.assign(s, { grassA: C(0xb4a55a), grassB: C(0xc2b066), dry: C(0xcfae6a), dirt: C(0x9a6a45), rock: C(0xa47e62),
    grass1: C(0xb8a458), grass2: C(0xd0bc6c), flower: [C(0xe86a3a), C(0xf0d060), C(0xb06ac0)], flowers: 0.08, treeDensity: 0.3, grassDensity: 1.2, snowLine: 60, leafShift: 'steppe', water: C(0x2b6e74) });

  // Время суток: положение солнца, свет, туман, параметры неба
  if (time === 0) Object.assign(s, { elev: 52, azim: -35, sunColor: C(0xfff1dc), sunI: 2.6, fog: C(0xb7c8d8), fogD: 0.0031, turb: 5, rayleigh: 1.4, expo: 0.62 });
  if (time === 1) Object.assign(s, { elev: 9, azim: -65, sunColor: C(0xffb070), sunI: 2.9, fog: C(0xd9a78a), fogD: 0.0036, turb: 9, rayleigh: 3.2, expo: 0.58 });
  if (time === 2) Object.assign(s, { elev: 20, azim: 115, sunColor: C(0xffe2bf), sunI: 2.1, fog: C(0xc3cad3), fogD: 0.0074, turb: 12, rayleigh: 2.2, expo: 0.62 });
  if (s.snowGround) { s.fog.lerp(C(0xd6dfe9), 0.5); s.sunI *= 0.9; }
  return s;
}

// ------------------------------------------------------------------ полководцы и гонцы

/** Полководец: сидит на белом коне со знаменем, думает и рассылает приказы. */
const COMMANDER = { key: 'cmd', name: 'Полководец', model: 'Knight', mount: true, special: 'commander', hp: 320, armor: 0.45, speed: 6.5, accel: 6,
  radius: 1.1, mass: 3.5, reach: 0.9, dmg: 22, cd: 1.2, atkTime: 0.7, arrowBlock: 0.4, vsCav: 1, charge: 1, keep: ['2H_Sword', 'Knight_Helmet', 'Knight_Cape'],
  anim: { attack: ['1H_Melee_Attack_Slice_Horizontal'] } };
/** Гонец: везёт приказ от полководца к отряду. Его можно перехватить. */
const MESSENGER = { key: 'msg', name: 'Гонец', model: 'Rogue_Hooded', mount: true, special: 'messenger', hp: 45, armor: 0, speed: 11, accel: 10,
  radius: 0.9, mass: 2.5, reach: 0.5, dmg: 0, cd: 99, atkTime: 0.5, arrowBlock: 0, vsCav: 1, charge: 1, keep: ['Rogue_Cape'], anim: { attack: [] } };
const TYPES_ALL = [...TYPES, COMMANDER, MESSENGER];
const T_CMD = 4, T_MSG = 5;

const CMD_NAMES = [['Ратибор', 'Ярополк', 'Мстислав', 'Добрыня', 'Святослав'], ['Всеслав', 'Изяслав', 'Горислав', 'Судислав', 'Братислав']];
const TRAITS = {
  cautious: { name: 'осторожный', think: 3.6, note: 'бережёт людей, держит высоты и укрытия' },
  fierce: { name: 'яростный', think: 2.6, note: 'рвётся в бой и давит числом' },
  cunning: { name: 'хитрый', think: 3.0, note: 'устраивает засады, обходы и налёты' },
};
const ORDER_TEXT = {
  advance: 'Вперёд', hold: 'Держать строй', high: 'Занять высоту', cover: 'В укрытие', ambush: 'Засада', flank: 'Обход с фланга',
  charge: 'Натиск', screen: 'Прикрыть стрелков', withdraw: 'Отход', rally: 'Сбор у знамени', rout: 'Бегут!', reserve: 'В резерв',
};
const SQUAD_NAME = ['мечники', 'варвары', 'арбалетчики', 'рыцари'];
