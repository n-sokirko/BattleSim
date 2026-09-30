using System;
using System.Collections.Generic;

namespace BattleSim.Core
{
    /// <summary>Собирает меш из коробок с цветами вершин (кладка стен, доски мостов, камни оград).</summary>
    public sealed class BoxBuilder
    {
        readonly List<float> pos = new List<float>(), nrm = new List<float>(), col = new List<float>();
        readonly List<int> idx = new List<int>();

        // Грани единичного куба: нормаль и 4 угла (против часовой стрелки снаружи)
        static readonly float[][] Faces =
        {
            new float[] { 1, 0, 0,   1, -1, 1,  1, -1, -1,  1, 1, -1,  1, 1, 1 },
            new float[] { -1, 0, 0,  -1, -1, -1, -1, -1, 1, -1, 1, 1, -1, 1, -1 },
            new float[] { 0, 1, 0,   -1, 1, 1,  1, 1, 1,  1, 1, -1,  -1, 1, -1 },
            new float[] { 0, -1, 0,  -1, -1, -1, 1, -1, -1, 1, -1, 1, -1, -1, 1 },
            new float[] { 0, 0, 1,   -1, -1, 1, 1, -1, 1,  1, 1, 1,  -1, 1, 1 },
            new float[] { 0, 0, -1,  1, -1, -1, -1, -1, -1, -1, 1, -1, 1, 1, -1 },
        };

        public int Count => idx.Count / 36;

        /// <summary>Коробка размером (sx, sy, sz) с преобразованием m (центр куба в нуле).</summary>
        public void Box(Mat4 m, Rgb c)
        {
            foreach (var f in Faces)
            {
                int b = pos.Count / 3;
                var n = m.Dir(f[0], f[1], f[2]).Normalized;
                for (int k = 0; k < 4; k++)
                {
                    var p = m.Point(f[3 + k * 3] * 0.5f, f[4 + k * 3] * 0.5f, f[5 + k * 3] * 0.5f);
                    pos.Add(p.x); pos.Add(p.y); pos.Add(p.z);
                    nrm.Add(n.x); nrm.Add(n.y); nrm.Add(n.z);
                    col.Add(c.r); col.Add(c.g); col.Add(c.b);
                }
                idx.Add(b); idx.Add(b + 1); idx.Add(b + 2);
                idx.Add(b); idx.Add(b + 2); idx.Add(b + 3);
            }
        }

        /// <summary>Как BoxGeometry(sx,sy,sz).rotateY(yaw).translate(x,y,z) в веб-версии.</summary>
        public void Box(float x, float y, float z, float sx, float sy, float sz, float yaw, Rgb c) =>
            Box(Mat4.Translation(x, y, z) * Mat4.RotationY(yaw) * Mat4.Compose(0, 0, 0, 0, 0, 0, 1, sx, sy, sz), c);

        /// <summary>Призма (цилиндр из sides граней) от y0 до y0+h: низ радиуса r0, верх — r1 (0 — конус).</summary>
        public void Prism(float cx, float y0, float cz, float r0, float r1, float h, int sides, Rgb c)
        {
            float y1 = y0 + h, slope = (r0 - r1) / MathF.Max(0.01f, h);
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * M.PI * 2 / sides, a1 = (i + 1) * M.PI * 2 / sides, am = (a0 + a1) / 2;
                float nx = MathF.Cos(am), nz = MathF.Sin(am), nl = M.Hypot(1, slope);
                int b = pos.Count / 3;
                void V(float x, float y, float z) { pos.Add(x); pos.Add(y); pos.Add(z); nrm.Add(nx / nl); nrm.Add(slope / nl); nrm.Add(nz / nl); col.Add(c.r); col.Add(c.g); col.Add(c.b); }
                V(cx + MathF.Cos(a0) * r0, y0, cz + MathF.Sin(a0) * r0);
                V(cx + MathF.Cos(a1) * r0, y0, cz + MathF.Sin(a1) * r0);
                V(cx + MathF.Cos(a1) * r1, y1, cz + MathF.Sin(a1) * r1);
                V(cx + MathF.Cos(a0) * r1, y1, cz + MathF.Sin(a0) * r1);
                idx.Add(b); idx.Add(b + 2); idx.Add(b + 1);
                idx.Add(b); idx.Add(b + 3); idx.Add(b + 2);
            }
            if (r1 > 0.01f)
            { // крышка сверху
                int b = pos.Count / 3;
                pos.Add(cx); pos.Add(y1); pos.Add(cz); nrm.Add(0); nrm.Add(1); nrm.Add(0); col.Add(c.r); col.Add(c.g); col.Add(c.b);
                for (int i = 0; i <= sides; i++)
                {
                    float a = i * M.PI * 2 / sides;
                    pos.Add(cx + MathF.Cos(a) * r1); pos.Add(y1); pos.Add(cz + MathF.Sin(a) * r1);
                    nrm.Add(0); nrm.Add(1); nrm.Add(0); col.Add(c.r); col.Add(c.g); col.Add(c.b);
                }
                for (int i = 0; i < sides; i++) { idx.Add(b); idx.Add(b + 2 + i); idx.Add(b + 1 + i); }
            }
        }

        public MeshData Build() => new MeshData { Pos = pos.ToArray(), Nrm = nrm.ToArray(), Col = col.ToArray(), Idx = idx.ToArray() };
    }

    /// <summary>Геометрия мира: рельеф кусками, крепость, мосты, ограды, трава. Всё в координатах ядра.</summary>
    public static class WorldMeshes
    {
        /// <summary>Кусок рельефа: вершины сетки высот [x0..x0+n] × [z0..z0+n], UV — доля карты.</summary>
        public static MeshData TerrainChunk(World w, int x0, int z0, int n)
        {
            int R = w.Res, nx = Math.Min(n, R - x0), nz = Math.Min(n, R - z0), V = nx + 1;
            var m = new MeshData { Pos = new float[V * (nz + 1) * 3], Nrm = new float[V * (nz + 1) * 3], Uv = new float[V * (nz + 1) * 2], Idx = new int[nx * nz * 6] };
            for (int z = 0; z <= nz; z++)
                for (int x = 0; x <= nx; x++)
                {
                    int gx = x0 + x, gz = z0 + z, i = z * V + x;
                    m.Pos[i * 3] = gx * w.Cell - w.Half; m.Pos[i * 3 + 1] = w.Hv(gx, gz); m.Pos[i * 3 + 2] = gz * w.Cell - w.Half;
                    float hl = w.Hv(Math.Max(gx - 1, 0), gz), hr = w.Hv(Math.Min(gx + 1, R), gz);
                    float hd = w.Hv(gx, Math.Max(gz - 1, 0)), hu = w.Hv(gx, Math.Min(gz + 1, R));
                    float ax = hl - hr, ay = 2 * w.Cell, az = hd - hu, l = M.Hypot(ax, ay, az);
                    m.Nrm[i * 3] = ax / l; m.Nrm[i * 3 + 1] = ay / l; m.Nrm[i * 3 + 2] = az / l;
                    m.Uv[i * 2] = (float)gx / R; m.Uv[i * 2 + 1] = (float)gz / R;
                }
            int k = 0;
            for (int z = 0; z < nz; z++)
                for (int x = 0; x < nx; x++)
                {
                    int i00 = z * V + x, i10 = i00 + 1, i01 = i00 + V, i11 = i01 + 1;
                    m.Idx[k++] = i00; m.Idx[k++] = i01; m.Idx[k++] = i11;
                    m.Idx[k++] = i00; m.Idx[k++] = i11; m.Idx[k++] = i10;
                }
            return m;
        }

        /// <summary>Пятна тона кладки: плавно по месту (по 3–4 м), а не случайный оттенок каждого блока — без «полосатости».</summary>
        static float Tone(float x, float z)
        {
            int ix = (int)MathF.Floor(x / 3.5f), iz = (int)MathF.Floor(z / 3.5f);
            uint h = (uint)(ix * 73856093) ^ (uint)(iz * 19349663);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return 0.955f + 0.09f * (h & 1023) / 1023f;
        }

        /// <summary>
        /// Крепость: светлый камень на тёмном цоколе, по верху стен — плита боевого хода и парапет с зубцами,
        /// у башен — угловые башенки с остроконечными крышами, лестницы — ровные ступени с каменными бортами.
        /// Все высоты — из тех же профилей, по которым ходят бойцы (FortWall.Top, FortRamp.TopAt).
        /// </summary>
        public static MeshData Fortress(World w)
        {
            if (w.Town == null) return null;
            var F = w.Town.Fort;
            var bb = new BoxBuilder();
            var R = new Rng(4242);
            Rgb stone = Rgb.Hex(0x9fa3a6), plinth = Rgb.Hex(0x74787b), cap = Rgb.Hex(0xb9bdbf), tower = Rgb.Hex(0x959a9d); // холодный серый: на закате не «персиковый»
            Rgb roof = Rgb.Hex(0xb04a3a), roofDark = Rgb.Hex(0x8e3a2e), wood = Rgb.Hex(0x6b4a2e), gateDark = Rgb.Hex(0x57524b);
            void Box(float x, float y, float z, float sx, float sy, float sz, float yaw, Rgb c) => bb.Box(x, y, z, sx, sy, sz, yaw, c * Tone(x, z));
            foreach (var wl in F.Walls)
            {
                float len = M.Hypot(wl.Bx - wl.Ax, wl.Bz - wl.Az), ux = (wl.Bx - wl.Ax) / len, uz = (wl.Bz - wl.Az) / len, yaw = MathF.Atan2(ux, uz) - M.PI / 2;
                // верх — профиль боевого хода (тот же, по которому ходят), кусками по 1 м, чтобы всходы к башням были ровными
                float Top(float t) => wl.Top != null ? wl.TopAt(t) : w.HeightAt(wl.Ax + ux * t, wl.Az + uz * t) + wl.H;
                float ox = wl.Out.x, oz = wl.Out.z;
                for (float t = 0; t < len; t += 1)
                {
                    float l = MathF.Min(1.03f, len - t + 0.03f), x = wl.Ax + ux * (t + l / 2), z = wl.Az + uz * (t + l / 2), g = w.HeightAt(x, z);
                    float bas = MathF.Min(g, MathF.Min(w.HeightAt(x + ox * wl.W / 2, z + oz * wl.W / 2), w.HeightAt(x - ox * wl.W / 2, z - oz * wl.W / 2))) - 1.5f, top = Top(t + l / 2);
                    Box(x, (bas + top - 0.14f) / 2, z, l, top - 0.14f - bas, wl.W, yaw, stone);                  // тело стены
                    float pl = MathF.Min(top - 1.2f, g + 1.3f);
                    if (pl > bas + 0.3f) Box(x, (bas + pl) / 2, z, l, pl - bas, wl.W + 0.5f, yaw, plinth);       // цоколь шире и темнее
                    Box(x, top - 0.07f, z, l, 0.14f, wl.W, yaw, cap);                                            // плита боевого хода
                    // парапет по внешнему краю — сплошной, зубцы на нём
                    Box(x + ox * (wl.W / 2 - 0.2f), top + 0.25f, z + oz * (wl.W / 2 - 0.2f), l, 0.5f, 0.4f, yaw, stone);
                    // пояс-карниз снаружи под боевым ходом
                    Box(x + ox * (wl.W / 2 + 0.08f), top - 0.55f, z + oz * (wl.W / 2 + 0.08f), l, 0.22f, 0.3f, yaw, cap);
                }
                for (float t = 0.7f; t < len - 0.3f; t += 1.45f)
                {
                    float x = wl.Ax + ux * t + ox * (wl.W / 2 - 0.2f), z = wl.Az + uz * t + oz * (wl.W / 2 - 0.2f);
                    Box(x, Top(t) + 0.8f, z, 0.72f, 0.6f, 0.4f, yaw, stone);
                }
            }
            foreach (var t in F.Towers)
            {
                float g = w.HeightAt(t.X, t.Z), bas = MathF.Min(g, MathF.Min(w.HeightAt(t.X + t.S / 2, t.Z + t.S / 2), w.HeightAt(t.X - t.S / 2, t.Z - t.S / 2))) - 1.5f, top = g + t.H;
                Box(t.X, (bas + top) / 2, t.Z, t.S, top - bas, t.S, 0, tower);
                Box(t.X, (bas + g + 1.4f) / 2, t.Z, t.S + 0.6f, g + 1.4f - bas, t.S + 0.6f, 0, plinth);     // цоколь
                Box(t.X, top - 0.8f, t.Z, t.S + 0.35f, 0.25f, t.S + 0.35f, 0, cap);                         // пояс под верхом
                Box(t.X, top - 0.07f, t.Z, t.S + 0.5f, 0.14f, t.S + 0.5f, 0, cap);                          // площадка
                // парапет кольцом и зубцы
                foreach (var (ex, ez) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    float px = t.X + ex * (t.S / 2 + 0.05f), pz = t.Z + ez * (t.S / 2 + 0.05f);
                    Box(px, top + 0.25f, pz, ez != 0 ? t.S + 0.5f : 0.4f, 0.5f, ex != 0 ? t.S + 0.5f : 0.4f, 0, tower);
                    for (int k = -2; k <= 2; k++)
                    {
                        float off = k * (t.S / 5);
                        Box(px + ez * off, top + 0.8f, pz + ex * off, ez != 0 ? 0.7f : 0.42f, 0.6f, ex != 0 ? 0.7f : 0.42f, 0, tower);
                    }
                }
                if (t.Keep)
                { // донжон: пояс посередине и башенка под высокой остроконечной крышей
                    Box(t.X, g + t.H * 0.55f, t.Z, t.S + 0.4f, 0.4f, t.S + 0.4f, 0, cap);
                    float cx = t.X + t.S / 2 - 1.6f, cz = t.Z - t.S / 2 + 1.6f;
                    bb.Prism(cx, top, cz, 1.7f, 1.7f, 4.2f, 8, tower * Tone(cx, cz));
                    bb.Prism(cx, top + 4.2f, cz, 2.1f, 0, 3.6f, 8, roof);
                }
                else if (t.S >= 5f)
                { // башенки-бартизаны только на внешних углах (от центра города наружу), под конусами крыш — силуэт замка
                    float r = MathF.Min(0.95f, t.S * 0.16f), dl = M.Hypot(t.X, t.Z);
                    float dx = dl > 1 ? t.X / dl : 0, dz = dl > 1 ? t.Z / dl : 1;
                    foreach (var (sx, sz) in new[] { (1, 1), (-1, 1), (1, -1), (-1, -1) })
                    {
                        if ((sx * dx + sz * dz) / 1.414f < 0.35f) continue;
                        float bx = t.X + sx * (t.S / 2 + r * 0.35f), bz = t.Z + sz * (t.S / 2 + r * 0.35f);
                        bb.Prism(bx, top - 2.2f, bz, r * 0.55f, r, 0.9f, 8, cap);                             // кронштейн
                        bb.Prism(bx, top - 1.3f, bz, r, r, 2.9f, 8, tower * Tone(bx, bz));
                        bb.Prism(bx, top + 1.6f, bz, r + 0.25f, 0, 1.9f, 8, (sx + sz) % 4 == 0 ? roof : roofDark);
                    }
                }
            }
            foreach (var r in F.Ramps)
            {
                float len = M.Hypot(r.Bx - r.Ax, r.Bz - r.Az), ux = (r.Bx - r.Ax) / len, uz = (r.Bz - r.Az) / len, yaw = MathF.Atan2(ux, uz) - M.PI / 2;
                float nx = -uz, nz = ux;
                int n = (int)MathF.Ceiling(len / 0.45f);
                for (int k = 0; k < n; k++)
                {
                    float t = (k + 0.5f) / n, x = r.Ax + ux * len * t, z = r.Az + uz * len * t, g = w.HeightAt(x, z), top = r.TopAt(t);
                    // ступени ровного светлого камня, чуть темнее через одну — видно, что это лестница
                    Box(x, (top + g - 0.6f) / 2, z, len / n + 0.02f, top - g + 0.6f, r.W, yaw, (k & 1) == 0 ? cap : cap * 0.93f);
                    // каменные борта по обе стороны — невысокий парапет вдоль марша (на верхней площадке их нет:
                    // она вровень с боевым ходом, с неё сходят вбок)
                    if (len * t > len - MathF.Min(2.5f, len * 0.25f) - 0.3f) continue;
                    foreach (float side in new[] { -1f, 1f })
                    {
                        float bx = x + nx * side * (r.W / 2 + 0.22f), bz = z + nz * side * (r.W / 2 + 0.22f), bg = w.HeightAt(bx, bz);
                        Box(bx, (top + 0.55f + bg - 0.6f) / 2, bz, len / n + 0.02f, top + 0.55f - bg + 0.6f, 0.44f, yaw, stone);
                    }
                }
            }
            foreach (var a in F.Arches)
            {
                float g = w.HeightAt(a.X, a.Z), yaw = MathF.Atan2(a.Ux, a.Uz) - M.PI / 2;
                float span = a.Citadel ? 7.2f : 8.6f;
                Box(a.X, g + a.H - 0.9f, a.Z, span, 1.8f, a.W, yaw, gateDark);              // арка над воротами
                Box(a.X, g + a.H - 1.9f, a.Z, span - 0.6f, 0.25f, a.W + 0.3f, yaw, cap);   // замковый пояс
                // створки ворот распахнуты к стенам проёма (дерево), проход свободен
                float px = MathF.Cos(yaw), pz = -MathF.Sin(yaw);
                foreach (float side in new[] { -1f, 1f })
                {
                    float gx = a.X + px * side * (span / 2 - 0.25f), gz = a.Z + pz * side * (span / 2 - 0.25f);
                    Box(gx, g + (a.H - 1.8f) / 2, gz, 0.22f, a.H - 1.8f, a.W * 0.8f, yaw, wood);
                }
            }
            Terraces(w, bb, R);
            return bb.Count > 0 ? bb.Build() : null;
        }

        /// <summary>
        /// Кладка уступов города: подпорные стены по краю уступа, скала замка и ров, стенки вдоль улиц-подъёмов,
        /// набережные реки. Каждая стенка — от нижней земли до верхней, куски по 2 м.
        /// </summary>
        static void Terraces(World w, BoxBuilder bb, Rng R)
        {
            var T = w.Town;
            Rgb wall = Rgb.Hex(0x93979a), bank = Rgb.Hex(0x7c8083), cap = Rgb.Hex(0xb0b4b6);
            void Box(float x, float y, float z, float sx, float sy, float sz, float yaw, Rgb c) => bb.Box(x, y, z, sx, sy, sz, yaw, c * Tone(x, z));
            bool InClimb(float x, float z, float pad)
            {
                foreach (var cl in T.Climbs)
                    if (M.SegDist(x, z, cl.Ax, cl.Az, cl.Bx, cl.Bz) < cl.W / 2 + pad) return true;
                return false;
            }
            // стенка по отрезку: верх — большая из высот по обе стороны, низ — меньшая
            void Face(float ax, float az, float bx, float bz, float thick, Rgb c, float probe = 2.2f, bool skipClimbs = true, bool skipFord = false)
            {
                float len = M.Hypot(bx - ax, bz - az);
                if (len < 0.1f) return;
                float ux = (bx - ax) / len, uz = (bz - az) / len, nx = -uz, nz = ux, yaw = MathF.Atan2(ux, uz) - M.PI / 2;
                for (float t = 0; t < len; t += 2)
                {
                    float l = MathF.Min(2.05f, len - t + 0.05f), x = ax + ux * (t + l / 2), z = az + uz * (t + l / 2);
                    if (skipClimbs && InClimb(x, z, 0.4f)) continue;
                    if (skipFord && T.River != null && M.Hypot(x - T.River.Ford.x, z - T.River.Ford.z) < 10) continue; // у брода — пологий спуск
                    float h1 = w.HeightAt(x + nx * probe, z + nz * probe), h2 = w.HeightAt(x - nx * probe, z - nz * probe);
                    float top = MathF.Max(h1, h2) + 0.12f, bas = MathF.Min(h1, h2) - 1.2f;
                    if (top - bas < 2.2f) continue; // ровно — стенка не нужна
                    Box(x, (bas + top) / 2, z, l, top - bas, thick, yaw, c);
                    Box(x, top + 0.1f, z, l, 0.2f, thick + 0.25f, yaw, cap); // карниз по верху
                }
            }
            // край уступа — по всей ширине города
            for (float x = -T.CX + 2; x < T.CX - 2; x += 2)
                Face(x, T.EdgeZ(x), x + 2, T.EdgeZ(x + 2), 2.6f, wall);
            // скала замка и ров
            var c = T.Castle;
            if (c != null)
            {
                var m = c.Moat;
                Face(c.MX0, c.MZ0 + 0.3f, c.MX0, c.MZ1, 2.4f, wall);
                Face(c.MX1, c.MZ1, c.MX1, c.MZ0 + 0.3f, 2.4f, wall);
                Face(c.MX0, c.MZ0, c.MX1, c.MZ0, 2.2f, wall, 2.2f, false);   // северная стенка рва — отвес скалы
                Face(m.X0, m.Z0, m.X1, m.Z0, 2.2f, wall);                    // южная стенка рва (кроме насыпи)
                Face(m.X0, m.Z0, m.X0, m.Z1, 2.2f, wall);
                Face(m.X1, m.Z0, m.X1, m.Z1, 2.2f, wall);
            }
            // стенки вдоль подъёмов: пандус врезан в уступ или поднят над землёй
            foreach (var cl in T.Climbs)
            {
                float len = M.Hypot(cl.Bx - cl.Ax, cl.Bz - cl.Az), ux = (cl.Bx - cl.Ax) / len, uz = (cl.Bz - cl.Az) / len, nx = -uz, nz = ux;
                foreach (float side in new[] { -1f, 1f })
                {
                    float o = cl.W / 2 + 0.7f;
                    Face(cl.Ax + nx * o * side, cl.Az + nz * o * side, cl.Bx + nx * o * side, cl.Bz + nz * o * side, 1.3f, wall, 1.4f, false);
                }
            }
            // набережные реки у города
            if (T.River != null)
            {
                var rv = T.River;
                for (int i = 1; i < rv.Pts.Count; i++)
                {
                    var a = rv.Pts[i - 1]; var b = rv.Pts[i];
                    if (MathF.Max(MathF.Abs(a.x), MathF.Abs(b.x)) > T.CX + 12) continue;
                    float dx = b.x - a.x, dz = b.z - a.z, l = M.Hypot(dx, dz), nx = -dz / l, nz = dx / l;
                    foreach (float side in new[] { -1f, 1f })
                    {
                        float o = rv.W / 2 + 0.6f;
                        Face(a.x + nx * o * side, a.z + nz * o * side, b.x + nx * o * side, b.z + nz * o * side, 1.4f, bank, 1.8f, false, true);
                    }
                }
            }
        }

        /// <summary>Деревянные мосты: доски настила, перила и опоры до дна ущелья.</summary>
        public static MeshData Bridges(World w)
        {
            if (w.Bridges.Count == 0) return null;
            var bb = new BoxBuilder();
            var R = new Rng(777);
            Rgb wood = Rgb.Hex(0x7a5534), beam = Rgb.Hex(0x5b3e25);
            void Box(float x, float y, float z, float sx, float sy, float sz, float yaw, Rgb c) => bb.Box(x, y, z, sx, sy, sz, yaw, c * M.Lerp(0.85f, 1.1f, R.F()));
            Rgb stone = Rgb.Hex(0x928a7d), stoneDark = Rgb.Hex(0x7a7266);
            foreach (var d in w.Bridges)
            {
                float yaw = MathF.Atan2(d.Ux, d.Uz), nx = -d.Uz, nz = d.Ux;
                if (d.Stone)
                { // каменный мост: настил, парапеты, быки до дна
                    int ns = (int)MathF.Ceiling(d.Len / 1.2f);
                    for (int k = 0; k < ns; k++)
                    {
                        float t = (k + 0.5f) / ns, x = d.Ax + d.Ux * d.Len * t, z = d.Az + d.Uz * d.Len * t, y = Decks.HeightOf(d, t, 0);
                        Box(x, y - 0.35f, z, d.W + 0.6f, 0.7f, d.Len / ns + 0.02f, yaw, stone);
                        foreach (float side in new[] { -1f, 1f })
                            Box(x + nx * side * (d.W / 2 + 0.15f), y + 0.45f, z + nz * side * (d.W / 2 + 0.15f), 0.5f, 0.95f, d.Len / ns + 0.02f, yaw, stoneDark);
                    }
                    foreach (float t in new[] { 0.36f, 0.64f })
                    {
                        float x = d.Ax + d.Ux * d.Len * t, z = d.Az + d.Uz * d.Len * t, y = Decks.HeightOf(d, t, 0), bottom = w.HeightAt(x, z) - 1;
                        if (y - bottom > 1.2f) Box(x, (y - 0.7f + bottom) / 2, z, d.W + 0.2f, y - 0.7f - bottom, 1.4f, yaw, stoneDark);
                    }
                    continue;
                }
                int n = (int)MathF.Ceiling(d.Len / 0.55f);
                for (int k = 0; k < n; k++)
                {
                    float t = (k + 0.5f) / n, x = d.Ax + d.Ux * d.Len * t, z = d.Az + d.Uz * d.Len * t, y = Decks.HeightOf(d, t, 0);
                    Box(x, y - 0.08f, z, d.W, 0.16f, d.Len / n - 0.06f, yaw, wood);
                }
                foreach (float side in new[] { -1f, 1f })
                {
                    float ox = nx * side * (d.W / 2 - 0.15f), oz = nz * side * (d.W / 2 - 0.15f);
                    // перила — кусками между столбами, по профилю настила
                    for (float t0 = 0; t0 < d.Len - 0.01f; t0 += 2.2f)
                    {
                        float t1 = MathF.Min(d.Len, t0 + 2.2f), y0 = Decks.HeightOf(d, t0 / d.Len, 0), y1 = Decks.HeightOf(d, t1 / d.Len, 0);
                        float pitch = MathF.Atan2(y1 - y0, t1 - t0), tm = (t0 + t1) / 2, seg = M.Hypot(t1 - t0, y1 - y0);
                        var rail = Mat4.Translation(d.Ax + d.Ux * tm + ox, (y0 + y1) / 2 + 1.05f, d.Az + d.Uz * tm + oz) * Mat4.RotationY(yaw)
                            * Mat4.Compose(0, 0, 0, MathF.Sin(-pitch / 2), 0, 0, MathF.Cos(-pitch / 2), 1, 1, 1) * Mat4.Compose(0, 0, 0, 0, 0, 0, 1, 0.12f, 0.12f, seg + 0.05f);
                        bb.Box(rail, new Rgb(0.4f, 0.4f, 0.4f));
                    }
                    for (float t = 0; t <= d.Len; t += 2.2f)
                    {
                        float x = d.Ax + d.Ux * t + ox, z = d.Az + d.Uz * t + oz, y = Decks.HeightOf(d, t / d.Len, 0);
                        Box(x, y + 0.55f, z, 0.14f, 1.1f, 0.14f, 0, beam);
                        if (t > 1.5f && t < d.Len - 1.5f && M.Round(t / 2.2f) % 2 == 0)
                        {
                            float bottom = w.HeightAt(x, z) - 1;
                            Box(x, (y + bottom) / 2, z, 0.35f, y - bottom, 0.35f, 0, beam); // опора до дна
                        }
                    }
                }
            }
            return bb.Build();
        }

        /// <summary>Каменные ограды, завалы и баррикады: камни рядами, верх местами осыпался.</summary>
        public static MeshData DryWalls(World w)
        {
            var bb = new BoxBuilder();
            for (int i = 0; i < w.Features.Walls.Count; i++) DryWall(bb, w, w.Features.Walls[i], i);
            return bb.Count > 0 ? bb.Build() : null;
        }

        /// <summary>
        /// Одна ограда (index — её номер: у каждой своё зерно, и пролом одной не перекладывает камни в других).
        /// Камень — рядами, верх местами осыпался; деревянная баррикада — колья и доски. Проломленная — осыпь
        /// камней по обе стороны или разбросанные доски.
        /// </summary>
        public static void DryWall(BoxBuilder bb, World w, FeatureWall wl, int index)
        {
            float len = M.Hypot(wl.Bx - wl.Ax, wl.Bz - wl.Az);
            if (len < 0.3f) return;
            var R = new Rng(991 + index * 7919);
            float dx = (wl.Bx - wl.Ax) / len, dz = (wl.Bz - wl.Az) / len, yaw = MathF.Atan2(dx, dz), nx = -dz, nz = dx;
            void Stone(float x, float y, float z, float yw, float sx, float sy, float sz, Rgb c, float tilt)
            {
                var rot = Mat4.Compose(0, 0, 0, MathF.Sin(tilt / 2), 0, 0, MathF.Cos(tilt / 2), 1, 1, 1) * Mat4.RotationY(yw + M.PI / 2);
                bb.Box(Mat4.Translation(x, y, z) * rot * Mat4.Compose(0, 0, 0, 0, 0, 0, 1, sx, sy, sz), c);
            }
            if (wl.Wood)
            {
                Rgb wood = Rgb.Hex(0x6b4a2e), dark = Rgb.Hex(0x4e3521);
                if (wl.Broken)
                { // доски вповалку
                    for (float s = 0.2f; s < len; s += M.Lerp(0.5f, 0.9f, R.F()))
                    {
                        float side = M.Lerp(-1.2f, 1.2f, R.F()), x = wl.Ax + dx * s + nx * side, z = wl.Az + dz * s + nz * side;
                        Stone(x, w.HeightAt(x, z) + 0.06f, z, yaw + M.Lerp(-1.2f, 1.2f, R.F()), M.Lerp(0.8f, 1.6f, R.F()), 0.08f, 0.22f, (R.Next() < 0.5 ? wood : dark) * M.Lerp(0.8f, 1.05f, R.F()), M.Lerp(-0.2f, 0.2f, R.F()));
                    }
                    return;
                }
                // колья через полметра и две доски поперёк
                for (float s = 0.1f; s < len; s += 0.5f)
                {
                    float x = wl.Ax + dx * s, z = wl.Az + dz * s, h = wl.H * M.Lerp(0.9f, 1.15f, R.F());
                    Stone(x, w.HeightAt(x, z) + h / 2, z, yaw, 0.14f, h, 0.14f, dark * M.Lerp(0.85f, 1.05f, R.F()), M.Lerp(-0.08f, 0.08f, R.F()));
                }
                for (int k = 0; k < 2; k++)
                {
                    float y0 = wl.H * (k == 0 ? 0.35f : 0.8f), mx = (wl.Ax + wl.Bx) / 2, mz = (wl.Az + wl.Bz) / 2;
                    Stone(mx + nx * 0.1f, w.HeightAt(mx, mz) + y0, mz + nz * 0.1f, yaw, len, 0.22f, 0.06f, wood * M.Lerp(0.85f, 1.05f, R.F()), 0);
                }
                return;
            }
            var baseC = Rgb.Hex(0x8c8479);
            if (wl.Broken)
            { // пролом: камни осыпались по обе стороны, наполовину в земле
                for (float s = 0; s < len; s += M.Lerp(0.35f, 0.55f, R.F()))
                {
                    float side = M.Lerp(-1.3f, 1.3f, R.F()), x = wl.Ax + dx * s + nx * side, z = wl.Az + dz * s + nz * side;
                    Stone(x, w.HeightAt(x, z) + 0.08f, z, yaw + M.Lerp(-0.8f, 0.8f, R.F()), M.Lerp(0.45f, 0.75f, R.F()), 0.36f, wl.T * M.Lerp(0.8f, 1.1f, R.F()), baseC * M.Lerp(0.72f, 1f, R.F()), M.Lerp(-0.4f, 0.4f, R.F()));
                }
                return;
            }
            int courses = wl.H > 1.2f ? 3 : 2;
            for (int course = 0; course < courses; course++)
            {
                float sy = course == 2 ? 0.3f : 0.52f, y0 = course == 0 ? 0.26f : course == 1 ? 0.78f : 1.2f;
                for (float s = (course % 2) * 0.35f; s < len; s += M.Lerp(0.62f, 0.8f, R.F()))
                {
                    if (course == courses - 1 && R.Next() < 0.35) continue; // верх ограды местами осыпался
                    float x = wl.Ax + dx * s, z = wl.Az + dz * s;
                    float yw = yaw + M.Lerp(-0.12f, 0.12f, R.F()), sx = M.Lerp(0.62f, 0.82f, R.F()), sz = wl.T * M.Lerp(0.9f, 1.1f, R.F()), c = M.Lerp(0.78f, 1.05f, R.F());
                    Stone(x, w.HeightAt(x, z) + y0, z, yw, sx, sy, sz, baseC * c, M.Lerp(-0.05f, 0.05f, R.F()));
                }
            }
        }

        /// <summary>Пучок травы: 5 изогнутых травинок с двух сторон (нормаль вверх).</summary>
        public static MeshData GrassTuft()
        {
            var pos = new List<float>();
            for (int b = 0; b < 5; b++)
            {
                float a = b * M.PI * 2 / 5 + (b % 2) * 0.4f;
                float sx = MathF.Cos(a), sz = MathF.Sin(a), ax = -sz * 0.05f, az = sx * 0.05f;
                float bx = sx * 0.06f, bz = sz * 0.06f, tx = sx * 0.22f, tz = sz * 0.22f, th = 0.42f + (b % 3) * 0.08f;
                pos.AddRange(new[] { bx - ax, 0, bz - az, bx + ax, 0, bz + az, tx, th, tz });
                pos.AddRange(new[] { bx + ax, 0, bz + az, bx - ax, 0, bz - az, tx, th, tz });
            }
            int n = pos.Count / 3;
            var m = new MeshData { Pos = pos.ToArray(), Nrm = new float[n * 3], Idx = new int[n] };
            for (int i = 0; i < n; i++) { m.Nrm[i * 3 + 1] = 1; m.Idx[i] = i; }
            return m;
        }

        /// <summary>Много копий меша (трава, цветы) с цветом каждой копии — в один меш.</summary>
        public static MeshData Scatter(MeshData proto, IList<Tuft> list, int from, int count)
        {
            int nv = proto.VertexCount, ni = proto.Idx.Length;
            var m = new MeshData { Pos = new float[count * nv * 3], Nrm = new float[count * nv * 3], Col = new float[count * nv * 3], Idx = new int[count * ni] };
            for (int k = 0; k < count; k++)
            {
                var t = list[from + k];
                var tr = Mat4.Translation(t.X, t.Y, t.Z) * Mat4.RotationY(t.Rot) * Mat4.Compose(0, 0, 0, 0, 0, 0, 1, t.Sx, t.Sy, t.Sz);
                for (int v = 0; v < nv; v++)
                {
                    var p = tr.Point(proto.Pos[v * 3], proto.Pos[v * 3 + 1], proto.Pos[v * 3 + 2]);
                    var n = tr.Dir(proto.Nrm[v * 3], proto.Nrm[v * 3 + 1], proto.Nrm[v * 3 + 2]).Normalized;
                    int o = (k * nv + v) * 3;
                    m.Pos[o] = p.x; m.Pos[o + 1] = p.y; m.Pos[o + 2] = p.z;
                    m.Nrm[o] = n.x; m.Nrm[o + 1] = n.y; m.Nrm[o + 2] = n.z;
                    m.Col[o] = t.Color.r; m.Col[o + 1] = t.Color.g; m.Col[o + 2] = t.Color.b;
                }
                for (int i = 0; i < ni; i++) m.Idx[k * ni + i] = proto.Idx[i] + k * nv;
            }
            return m;
        }

        /// <summary>Копия меша с преобразованием (для слияния домов города в один меш).</summary>
        public static MeshData Placed(MeshData src, float x, float y, float z, float rot, float k)
        {
            var m = src.Clone();
            m.Transform(Mat4.Translation(x, y, z) * Mat4.RotationY(rot) * Mat4.Scale(k));
            return m;
        }

        /// <summary>Икосаэдр радиуса r (цветок).</summary>
        public static MeshData Flower(float r = 0.07f)
        {
            float t = (1 + MathF.Sqrt(5)) / 2;
            float[] v = { -1, t, 0, 1, t, 0, -1, -t, 0, 1, -t, 0, 0, -1, t, 0, 1, t, 0, -1, -t, 0, 1, -t, t, 0, -1, t, 0, 1, -t, 0, -1, -t, 0, 1 };
            int[] f = { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8, 3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
            var m = new MeshData { Pos = new float[f.Length * 3], Nrm = new float[f.Length * 3], Idx = new int[f.Length] };
            for (int i = 0; i < f.Length; i += 3)
            {
                var a = new V3(v[f[i] * 3], v[f[i] * 3 + 1], v[f[i] * 3 + 2]).Normalized;
                var b = new V3(v[f[i + 1] * 3], v[f[i + 1] * 3 + 1], v[f[i + 1] * 3 + 2]).Normalized;
                var c = new V3(v[f[i + 2] * 3], v[f[i + 2] * 3 + 1], v[f[i + 2] * 3 + 2]).Normalized;
                var n = ((a + b + c) * (1f / 3)).Normalized;
                foreach (var (p, k) in new[] { (a, 0), (b, 1), (c, 2) })
                {
                    m.Pos[(i + k) * 3] = p.x * r; m.Pos[(i + k) * 3 + 1] = p.y * r; m.Pos[(i + k) * 3 + 2] = p.z * r;
                    m.Nrm[(i + k) * 3] = n.x; m.Nrm[(i + k) * 3 + 1] = n.y; m.Nrm[(i + k) * 3 + 2] = n.z;
                    m.Idx[i + k] = i + k;
                }
            }
            return m;
        }
    }
}
