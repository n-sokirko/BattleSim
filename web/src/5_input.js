// ------------------------------------------------------------------ камера и управление

class CameraRig {
  constructor(cam, dom) {
    this.cam = cam; this.dom = dom;
    this.yaw = 0; this.pitch = 34 * DEG; this.dist = 85; this.target = new THREE.Vector3(0, 0, -48);
    this.goal = { yaw: this.yaw, pitch: this.pitch, dist: this.dist, target: this.target.clone() };
    this.groundY = 0;
    this.cinematic = false;
    this.keys = new Set();
    this.pointers = new Map();
    this.multi = false;
    this.onTap = null; this.onHover = null;
    this.ray = new THREE.Raycaster();
    this.plane = new THREE.Plane(new THREE.Vector3(0, 1, 0), 0);
    this.bind();
  }

  lookAt(x, z, yaw, pitch, dist, instant) {
    Object.assign(this.goal, { yaw, pitch, dist });
    this.goal.target.set(x, 0, z);
    if (instant) { this.yaw = yaw; this.pitch = pitch; this.dist = dist; this.target.copy(this.goal.target); }
  }

  ndc(x, y) {
    const r = this.dom.getBoundingClientRect();
    return new THREE.Vector2(((x - r.left) / r.width) * 2 - 1, -((y - r.top) / r.height) * 2 + 1);
  }

  groundPoint(x, y) {
    this.ray.setFromCamera(this.ndc(x, y), this.cam);
    return world.raycast(this.ray.ray);
  }

  /** «Схватить землю» и протащить её за пальцем или мышью. */
  dragPan(x0, y0, x1, y1) {
    this.plane.constant = -this.groundY;
    const a = new THREE.Vector3(), b = new THREE.Vector3();
    this.ray.setFromCamera(this.ndc(x0, y0), this.cam);
    if (!this.ray.ray.intersectPlane(this.plane, a)) return;
    this.ray.setFromCamera(this.ndc(x1, y1), this.cam);
    if (!this.ray.ray.intersectPlane(this.plane, b)) return;
    const d = a.sub(b); d.y = 0;
    d.clampLength(0, this.dist * 0.5 + 5);
    this.goal.target.add(d); this.target.add(d);
    this.cinematic = false;
  }

  bind() {
    const el = this.dom;
    el.addEventListener('contextmenu', (e) => e.preventDefault());
    el.addEventListener('pointerdown', (e) => {
      el.setPointerCapture(e.pointerId);
      this.pointers.set(e.pointerId, { x: e.clientX, y: e.clientY, sx: e.clientX, sy: e.clientY, t: performance.now(), button: e.button, type: e.pointerType, moved: false });
      if (this.pointers.size > 1) this.multi = true;
    });
    el.addEventListener('pointermove', (e) => {
      const p = this.pointers.get(e.pointerId);
      if (!p) { if (e.pointerType === 'mouse' && this.onHover) this.onHover(e.clientX, e.clientY); return; }
      const slop = p.type === 'touch' ? 14 : 6;
      if (!p.moved && Math.hypot(e.clientX - p.sx, e.clientY - p.sy) > slop) p.moved = true;
      if (this.pointers.size >= 2) this.gesture(e.pointerId, e.clientX, e.clientY);
      else if (p.moved) {
        if (p.type === 'mouse' && p.button === 2) {
          this.goal.yaw -= (e.clientX - p.x) * 0.005;
          this.goal.pitch = clamp(this.goal.pitch + (e.clientY - p.y) * 0.004, 10 * DEG, 86 * DEG);
          this.cinematic = false;
        } else this.dragPan(p.x, p.y, e.clientX, e.clientY);
      }
      p.x = e.clientX; p.y = e.clientY;
      if (e.pointerType === 'mouse' && this.onHover) this.onHover(e.clientX, e.clientY);
    });
    const end = (e) => {
      const p = this.pointers.get(e.pointerId);
      if (!p) return;
      this.pointers.delete(e.pointerId);
      const tap = !p.moved && !this.multi && performance.now() - p.t < 650 && (p.type !== 'mouse' || p.button === 0);
      if (this.pointers.size === 0) this.multi = false;
      if (tap && e.type === 'pointerup' && this.onTap) this.onTap(e.clientX, e.clientY);
    };
    el.addEventListener('pointerup', end);
    el.addEventListener('pointercancel', end);
    el.addEventListener('wheel', (e) => {
      e.preventDefault();
      const s = e.deltaMode === 1 ? e.deltaY * 16 : e.deltaY;
      this.goal.dist = clamp(this.goal.dist * Math.pow(1.0015, s), 6, 170);
    }, { passive: false });
    addEventListener('keydown', (e) => { if (!e.target.closest?.('input,textarea')) this.keys.add(e.code); });
    addEventListener('keyup', (e) => this.keys.delete(e.code));
    addEventListener('blur', () => this.keys.clear());
  }

  gesture(id, x, y) {
    const ids = [...this.pointers.keys()].slice(0, 2);
    const [a, b] = ids.map((k) => this.pointers.get(k));
    const na = id === ids[0] ? { x, y } : a, nb = id === ids[1] ? { x, y } : b;
    const d0 = Math.hypot(b.x - a.x, b.y - a.y), d1 = Math.hypot(nb.x - na.x, nb.y - na.y);
    if (d0 > 20 && d1 > 20) this.goal.dist = clamp(this.goal.dist * d0 / d1, 6, 170);
    const a0 = Math.atan2(b.y - a.y, b.x - a.x), a1 = Math.atan2(nb.y - na.y, nb.x - na.x);
    let da = a1 - a0; da = Math.atan2(Math.sin(da), Math.cos(da));
    this.goal.yaw += da;
    const dyA = na.y - a.y, dyB = nb.y - b.y;
    if (dyA * dyB > 0) this.goal.pitch = clamp(this.goal.pitch + (dyA + dyB) * 0.5 * 0.004, 10 * DEG, 86 * DEG);
    a.moved = b.moved = true;
    this.cinematic = false;
  }

  update(dt, focus) {
    const g = this.goal, k = this.keys;
    const fx = Math.sin(g.yaw), fz = Math.cos(g.yaw);
    let mx = 0, mz = 0;
    if (k.has('KeyW') || k.has('ArrowUp')) mz += 1;
    if (k.has('KeyS') || k.has('ArrowDown')) mz -= 1;
    if (k.has('KeyD') || k.has('ArrowRight')) mx += 1;
    if (k.has('KeyA') || k.has('ArrowLeft')) mx -= 1;
    if (mx || mz) {
      const sp = (g.dist * 0.9 + 10) * dt;
      // вправо = (-cos, 0, sin) для камеры, смотрящей вдоль (sin, 0, cos)
      g.target.x += (fx * mz - fz * mx) * sp;
      g.target.z += (fz * mz + fx * mx) * sp;
      this.cinematic = false;
    }
    if (k.has('KeyQ')) g.yaw += 1.6 * dt;
    if (k.has('KeyE')) g.yaw -= 1.6 * dt;
    if (k.has('KeyR')) g.pitch = clamp(g.pitch + 0.8 * dt, 10 * DEG, 86 * DEG);
    if (k.has('KeyF')) g.pitch = clamp(g.pitch - 0.8 * dt, 10 * DEG, 86 * DEG);

    if (this.cinematic) {
      g.yaw += 0.12 * dt;
      if (focus) g.target.lerp(focus, 1 - Math.exp(-0.8 * dt));
    }
    const lim = FIELD + 25;
    g.target.x = clamp(g.target.x, -lim, lim); g.target.z = clamp(g.target.z, -lim, lim);

    const s = 1 - Math.exp(-12 * dt);
    let dy = g.yaw - this.yaw; dy = Math.atan2(Math.sin(dy), Math.cos(dy));
    this.yaw += dy * s;
    this.pitch += (g.pitch - this.pitch) * s;
    this.dist += (g.dist - this.dist) * s;
    this.target.x += (g.target.x - this.target.x) * s;
    this.target.z += (g.target.z - this.target.z) * s;
    this.groundY += (world.heightAt(this.target.x, this.target.z) - this.groundY) * (1 - Math.exp(-6 * dt));

    const cp = Math.cos(this.pitch);
    const look = new THREE.Vector3(this.target.x, this.groundY + 1, this.target.z);
    const pos = look.clone().sub(new THREE.Vector3(Math.sin(this.yaw) * cp, -Math.sin(this.pitch), Math.cos(this.yaw) * cp).multiplyScalar(this.dist));
    const minY = world.heightAt(pos.x, pos.z) + 1.5;
    if (pos.y < minY) pos.y = minY;
    this.cam.position.copy(pos);
    this.cam.lookAt(look);
  }
}
