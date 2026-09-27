using System;
using System.Collections.Generic;

namespace BattleSim.Core
{
    // ------------------------------------------------------------------ препятствия: дома (прямоугольники) и стволы (круги)

    public sealed class Obstacle
    {
        public bool Rect;
        public float X, Z, Hx, Hz, C, S, R, Top, Ground;
    }

    public sealed class Obstacles
    {
        public readonly SpatialGrid<Obstacle> Grid;
        public List<Obstacle> List => Grid.All;

        public Obstacles(float field) { Grid = new SpatialGrid<Obstacle>(field); }

        /// <summary>Прямоугольник (дом): центр, полуразмеры, поворот, высота крыши над землёй.</summary>
        public void AddRect(float x, float z, float hx, float hz, float ang, float top, float ground)
        {
            var o = new Obstacle { Rect = true, X = x, Z = z, Hx = hx, Hz = hz, C = MathF.Cos(ang), S = MathF.Sin(ang), Top = top, Ground = ground };
            Grid.Insert(o, x, z, M.Hypot(hx, hz));
        }

        public void AddCircle(float x, float z, float r, float top, float ground)
        {
            Grid.Insert(new Obstacle { Rect = false, X = x, Z = z, R = r, Top = top, Ground = ground }, x, z, r);
        }

        /// <summary>Точка внутри препятствия (с запасом pad)? Возвращает препятствие или null.</summary>
        public Obstacle Hit(float x, float z, float pad = 0f)
        {
            var L = Grid.Near(x, z);
            if (L == null) return null;
            for (int i = 0; i < L.Count; i++)
            {
                var o = L[i];
                float dx = x - o.X, dz = z - o.Z;
                if (o.Rect)
                {
                    float lx = dx * o.C + dz * o.S, lz = -dx * o.S + dz * o.C;
                    if (MathF.Abs(lx) < o.Hx + pad && MathF.Abs(lz) < o.Hz + pad) return o;
                }
                else if (dx * dx + dz * dz < (o.R + pad) * (o.R + pad)) return o;
            }
            return null;
        }

        /// <summary>Выталкивает круг радиуса r из препятствий (солдат скользит вдоль стены).</summary>
        public void PushOut(ref float px, ref float pz, float r)
        {
            var L = Grid.Near(px, pz);
            if (L == null || L.Count == 0) return;
            for (int i = 0; i < L.Count; i++)
            {
                var o = L[i];
                float dx = px - o.X, dz = pz - o.Z;
                if (o.Rect)
                {
                    float lx = dx * o.C + dz * o.S, lz = -dx * o.S + dz * o.C;
                    float ex = o.Hx + r, ez = o.Hz + r;
                    if (MathF.Abs(lx) >= ex || MathF.Abs(lz) >= ez) continue;
                    if (ex - MathF.Abs(lx) < ez - MathF.Abs(lz)) lx = (lx != 0 ? M.Sign(lx) : 1f) * ex;
                    else lz = (lz != 0 ? M.Sign(lz) : 1f) * ez;
                    px = o.X + lx * o.C - lz * o.S;
                    pz = o.Z + lx * o.S + lz * o.C;
                }
                else
                {
                    float d = M.Hypot(dx, dz), m = o.R + r;
                    if (d >= m) continue;
                    float k = d > 1e-4f ? m / d : 0f;
                    px = o.X + (d > 1e-4f ? dx * k : m);
                    pz = o.Z + dz * k;
                }
            }
        }

        /// <summary>Перекрывает ли препятствие точку на высоте y (для прямой видимости и болтов).</summary>
        public bool Blocks(float x, float z, float y)
        {
            var o = Hit(x, z);
            return o != null && y < o.Ground + o.Top;
        }
    }

    // ------------------------------------------------------------------ настилы: мосты, стены, лестницы, башни

    public enum DeckKind { Bridge, Wall, Ramp, Tower }

    /// <summary>
    /// Всё, что выше земли: мост над ущельем, крепостная стена с боевым ходом поверху,
    /// лестница-пандус от земли до боевого хода, площадка башни.
    /// Сегмент от A до B шириной W; высота hA→hB — абсолютная или над рельефом (Rel).
    /// Walk — по нему ходят; Solid — сплошная кладка до земли (закрывает обзор и болты).
    /// </summary>
    public sealed class Deck
    {
        public DeckKind Kind;
        public float Ax, Az, Bx, Bz, W, HA, HB, Parapet, OutSign;
        public bool Rel, Walk, Solid;
        public float Len, Ux, Uz, X, Z;
    }

    public sealed class Decks
    {
        public readonly SpatialGrid<Deck> Grid;
        public List<Deck> List => Grid.All;

        public Decks(float field) { Grid = new SpatialGrid<Deck>(field); }

        public Deck Add(Deck d)
        {
            d.Len = M.Hypot(d.Bx - d.Ax, d.Bz - d.Az);
            if (d.Len == 0f) d.Len = 0.01f;
            d.Ux = (d.Bx - d.Ax) / d.Len; d.Uz = (d.Bz - d.Az) / d.Len;
            d.X = (d.Ax + d.Bx) / 2; d.Z = (d.Az + d.Bz) / 2;
            Grid.Insert(d, d.X, d.Z, d.Len / 2 + d.W);
            return d;
        }

        /// <summary>Положение точки на настиле: t вдоль (0..1) и боковое смещение; false — мимо.</summary>
        public static bool Locate(Deck d, float x, float z, out float t, out float lat)
        {
            float px = x - d.Ax, pz = z - d.Az, along = px * d.Ux + pz * d.Uz;
            t = 0; lat = 0;
            if (along < -0.05f || along > d.Len + 0.05f) return false;
            lat = -px * d.Uz + pz * d.Ux;
            if (MathF.Abs(lat) > d.W / 2) return false;
            t = M.Clamp(along / d.Len, 0f, 1f);
            return true;
        }

        public static float HeightOf(Deck d, float t, float ground)
        {
            float h = d.HA + (d.HB - d.HA) * t;
            return d.Rel ? ground + h : h;
        }

        /// <summary>Высота настила, по которому можно пройти в точке (или -∞).</summary>
        public float Surface(float x, float z, float ground)
        {
            var L = Grid.Near(x, z);
            float best = float.NegativeInfinity;
            if (L != null)
                for (int i = 0; i < L.Count; i++)
                {
                    var d = L[i];
                    if (!d.Walk || !Locate(d, x, z, out float t, out _)) continue;
                    float h = HeightOf(d, t, ground);
                    if (h > best) best = h;
                }
            return best;
        }

        /// <summary>Верх сплошной кладки в точке (с зубцами на внешнем краю стены) — для обзора и болтов.</summary>
        public float SolidTop(float x, float z, float ground)
        {
            var L = Grid.Near(x, z);
            float best = float.NegativeInfinity;
            if (L != null)
                for (int i = 0; i < L.Count; i++)
                {
                    var d = L[i];
                    if (!d.Solid || !Locate(d, x, z, out float t, out float lat)) continue;
                    float h = HeightOf(d, t, ground);
                    if (d.Parapet > 0 && lat * d.OutSign > d.W * 0.25f) h += d.Parapet;
                    if (h > best) best = h;
                }
            return best;
        }

        /// <summary>Тонкий настил моста перекрывает точку на высоте y?</summary>
        public bool ThinBlocks(float x, float z, float y)
        {
            var L = Grid.Near(x, z);
            if (L != null)
                for (int i = 0; i < L.Count; i++)
                {
                    var d = L[i];
                    if (d.Solid || !d.Walk || !Locate(d, x, z, out float t, out _)) continue;
                    float h = HeightOf(d, t, 0f);
                    if (y < h && y > h - 0.7f) return true;
                }
            return false;
        }
    }

    // ------------------------------------------------------------------ навигация: многоуровневая сетка + A*

    public sealed class MinHeap
    {
        int[] n = new int[256];
        float[] f = new float[256];
        int count;
        public int Size => count;
        public void Clear() => count = 0;

        public void Push(int node, float fv)
        {
            if (count == n.Length) { Array.Resize(ref n, count * 2); Array.Resize(ref f, count * 2); }
            int i = count++;
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (f[p] <= fv) break;
                n[i] = n[p]; f[i] = f[p]; i = p;
            }
            n[i] = node; f[i] = fv;
        }

        public int Pop()
        {
            int top = n[0];
            count--;
            if (count > 0)
            {
                int last = n[count];
                float lf = f[count];
                int i = 0;
                for (;;)
                {
                    int l = i * 2 + 1, r = l + 1, m = i;
                    float mf = lf;
                    if (l < count && f[l] < mf) { m = l; mf = f[l]; }
                    if (r < count && f[r] < mf) { m = r; mf = f[r]; }
                    if (m == i) break;
                    n[i] = n[m]; f[i] = f[m]; i = m;
                }
                n[i] = last; f[i] = lf;
            }
            return top;
        }
    }

    /// <summary>
    /// Сетка 1,5×1,5 м: высота поверхности (земля или настил), скорость для пехоты (0)
    /// и конницы (1), 0 — не пройти. Между соседними клетками можно шагнуть, только
    /// если перепад высот не больше ~1,9 м: так обрывы, стены и берега ущелья
    /// непроходимы, а лестницы, пандусы и тропы — проходимы.
    /// </summary>
    public sealed class NavGrid
    {
        public readonly float CellSize = 1.5f, Half;
        public readonly int Dim;
        public readonly float[][] Speed;
        public readonly float[] Surf;
        public readonly byte[] Conceal, Canopy;
        /// <summary>Загрузка дорог: сколько отрядов недавно проложили путь через клетку (затухает).</summary>
        public readonly float[] Crowd;
        public int Barriers;

        readonly MinHeap heap = new MinHeap();
        readonly float[] gs;
        readonly int[] came, seen, done;
        int run;

        public NavGrid(World w)
        {
            Half = w.Field + 2;
            Dim = (int)Math.Ceiling(Half * 2 / CellSize);
            int n = Dim * Dim;
            Speed = new[] { new float[n], new float[n] };
            Surf = new float[n];
            Conceal = new byte[n];
            Crowd = new float[n];
            Canopy = new byte[n];
            Build(w);
            gs = new float[n]; came = new int[n]; seen = new int[n]; done = new int[n];
        }

        void Build(World w)
        {
            int D = Dim;
            float c = CellSize;
            for (int iz = 0; iz < D; iz++)
                for (int ix = 0; ix < D; ix++)
                {
                    int i = iz * D + ix;
                    float x = -Half + (ix + 0.5f) * c, z = -Half + (iz + 0.5f) * c;
                    float si = 1, sc = 1;
                    byte hide = 0, canopy = 0;
                    float g = w.HeightAt(x, z), deck = w.Decks.Surface(x, z, g);
                    bool onDeck = deck > g + 0.05f;
                    Surf[i] = onDeck ? deck : g;
                    if (!onDeck)
                    {
                        float depth = World.Water - g;
                        if (depth > 1.1f) si = sc = 0;                              // глубокая вода
                        else if (depth > 0.4f) { si *= 0.35f; sc *= 0.25f; }        // брод
                        else if (depth > -0.35f) { si *= 0.6f; sc *= 0.42f; }       // топь у берега
                        float f = w.ForestAt(x, z);
                        if (f > 0.5f) { si *= 0.8f; sc *= 0.5f; hide = 1; canopy = 1; } // чаща
                        else if (f > 0.3f) { si *= 0.92f; sc *= 0.75f; }
                        if (w.ReedsAt(x, z)) hide = 1;                               // камыш
                        if (w.Decks.SolidTop(x, z, g) > g + 1) si = sc = 0;          // сплошная кладка без хода
                    }
                    var ob = w.Obs.Hit(x, z, 0.4f);
                    if (ob != null && (ob.Rect || ob.R > 1)) si = sc = 0;           // дом, колодец, башня
                    Speed[0][i] = si; Speed[1][i] = sc;
                    Conceal[i] = hide; Canopy[i] = canopy;
                }
            int b = 0;
            for (int iz = 0; iz < D; iz++)
                for (int ix = 0; ix < D; ix++)
                {
                    int i = iz * D + ix;
                    if (Speed[0][i] == 0) { b++; continue; }
                    if (ix + 1 < D && !Step(i, i + 1, false)) b++;
                    if (iz + 1 < D && !Step(i, i + D, false)) b++;
                }
            Barriers = b;
        }

        /// <summary>Можно ли шагнуть между соседними клетками по высоте.</summary>
        public bool Step(int a, int b, bool diag) => MathF.Abs(Surf[a] - Surf[b]) <= (diag ? 2.7f : 1.9f);

        public int Idx(float x, float z)
        {
            int ix = M.Floor((x + Half) / CellSize), iz = M.Floor((z + Half) / CellSize);
            return ix < 0 || iz < 0 || ix >= Dim || iz >= Dim ? -1 : iz * Dim + ix;
        }

        public V2 Center(int i) => new V2(-Half + (i % Dim + 0.5f) * CellSize, -Half + (i / Dim + 0.5f) * CellSize);
        public float SpeedAt(float x, float z, int cls) { int i = Idx(x, z); return i < 0 ? 1 : Speed[cls][i]; }
        public bool ConcealAt(float x, float z) { int i = Idx(x, z); return i >= 0 && Conceal[i] == 1; }
        public bool CanopyAt(float x, float z) { int i = Idx(x, z); return i >= 0 && Canopy[i] == 1; }

        public bool LineClear(float ax, float az, float bx, float bz, int cls)
        {
            float d = M.Hypot(bx - ax, bz - az);
            int n = Math.Max(1, (int)Math.Ceiling(d / (CellSize * 0.5f)));
            var sp = Speed[cls];
            int prev = Idx(ax, az);
            for (int k = 1; k <= n; k++)
            {
                float t = (float)k / n;
                int i = Idx(ax + (bx - ax) * t, az + (bz - az) * t);
                if (i < 0 || i == prev) continue;
                if (sp[i] == 0 || (prev >= 0 && !Step(prev, i, true))) return false;
                prev = i;
            }
            return true;
        }

        int NearestOpen(int i, float[] sp)
        {
            int D = Dim, x0 = i % D, z0 = i / D;
            for (int r = 1; r < 10; r++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                        int x = x0 + dx, z = z0 + dz;
                        if (x >= 0 && z >= 0 && x < D && z < D && sp[z * D + x] > 0) return z * D + x;
                    }
            return -1;
        }

        /// <summary>A* по сетке с учётом перепадов высот; путь сглаживается до точек поворота.</summary>
        public List<V2> FindPath(float ax, float az, float bx, float bz, int cls, float crowdCost = 0, float avoidX = 0, float avoidZ = 0, float avoidR = 0)
        {
            int D = Dim;
            var sp = Speed[cls];
            var S = Surf;
            int s = Idx(ax, az), g = Idx(bx, bz);
            if (s < 0 || g < 0) return null;
            if (sp[s] == 0) s = NearestOpen(s, sp);
            bool goalOpen = sp[g] > 0;
            if (!goalOpen) g = NearestOpen(g, sp);
            if (s < 0 || g < 0) return null;
            int rn = ++run;
            int gx = g % D, gz = g / D;
            heap.Clear();
            gs[s] = 0; came[s] = -1; seen[s] = rn;
            heap.Push(s, H(s, gx, gz, D));
            int expanded = 0;
            while (heap.Size > 0 && expanded < 40000)
            {
                int c = heap.Pop();
                if (done[c] == rn) continue;
                done[c] = rn; expanded++;
                if (c == g) break;
                int cx = c % D, cz = c / D;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int nx = cx + dx, nz = cz + dz;
                        if (nx < 0 || nz < 0 || nx >= D || nz >= D) continue;
                        int ni = nz * D + nx;
                        bool diag = dx != 0 && dz != 0;
                        if (sp[ni] == 0 || done[ni] == rn || !Step(c, ni, diag)) continue;
                        if (diag)
                        { // не срезаем углы домов и обрывов
                            int a = cz * D + nx, b2 = nz * D + cx;
                            if (sp[a] == 0 || sp[b2] == 0 || !Step(c, a, false) || !Step(c, b2, false)) continue;
                        }
                        float ng = gs[c] + (diag ? 1.4142f : 1f) * (2f / (sp[c] + sp[ni]) + Crowd[ni] * crowdCost) + MathF.Abs(S[ni] - S[c]) * 0.4f;
                        if (avoidR > 0)
                        {
                            float ox = -Half + (nx + 0.5f) * CellSize - avoidX, oz = -Half + (nz + 0.5f) * CellSize - avoidZ;
                            if (ox * ox + oz * oz < avoidR * avoidR) ng += 6;
                        }
                        if (seen[ni] != rn || ng < gs[ni]) { seen[ni] = rn; gs[ni] = ng; came[ni] = c; heap.Push(ni, ng + H(ni, gx, gz, D)); }
                    }
            }
            if (done[g] != rn) return null;
            var cells = new List<int>();
            for (int c = g; c != -1; c = came[c]) cells.Add(c);
            cells.Reverse();
            var pts = new List<V2>(cells.Count);
            foreach (int i in cells) pts.Add(Center(i));
            if (goalOpen) pts[pts.Count - 1] = new V2(bx, bz);
            var output = new List<V2> { pts[0] };
            for (int i = 0; i < pts.Count - 1;)
            {
                int j = Math.Min(pts.Count - 1, i + 24);
                while (j > i + 1 && !LineClear(pts[i].x, pts[i].z, pts[j].x, pts[j].z, cls)) j--;
                output.Add(pts[j]);
                i = j;
            }
            return output;
        }

        /// <summary>Отряд пойдёт этим путём: отмечаем загрузку полосой шириной ~6 м.</summary>
        public void MarkCrowd(List<V2> path, float amount)
        {
            if (path == null) return;
            for (int k = 1; k < path.Count; k++)
            {
                float ax = path[k - 1].x, az = path[k - 1].z, bx = path[k].x, bz = path[k].z, d = M.Hypot(bx - ax, bz - az);
                int n = Math.Max(1, (int)(d / CellSize));
                for (int j = 0; j <= n; j++)
                {
                    float x = ax + (bx - ax) * j / n, z = az + (bz - az) * j / n;
                    for (int dz = -2; dz <= 2; dz++)
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            int i = Idx(x + dx * CellSize, z + dz * CellSize);
                            if (i >= 0) Crowd[i] = MathF.Min(Crowd[i] + amount / (n + 1) * 2, 6);
                        }
                }
            }
        }

        public void DecayCrowd(float k)
        {
            for (int i = 0; i < Crowd.Length; i++) if (Crowd[i] > 0) Crowd[i] *= k;
        }

        static float H(int i, int gx, int gz, int D)
        {
            int dx = Math.Abs(i % D - gx), dz = Math.Abs(i / D - gz);
            return dx + dz - 0.5858f * Math.Min(dx, dz);
        }
    }

    // ------------------------------------------------------------------ сплайн для извилистых дорог

    public static class Spline
    {
        /// <summary>Кривая Катмулла-Рома через контрольные точки, шаг ~step метров.</summary>
        public static List<V2> Catmull(List<V2> ctrl, float step = 1f)
        {
            var output = new List<V2>();
            var P = new List<V2>(ctrl.Count + 2) { ctrl[0] };
            P.AddRange(ctrl);
            P.Add(ctrl[ctrl.Count - 1]);
            for (int i = 1; i < P.Count - 2; i++)
            {
                V2 p0 = P[i - 1], p1 = P[i], p2 = P[i + 1], p3 = P[i + 2];
                int n = Math.Max(2, (int)Math.Ceiling(M.Hypot(p2.x - p1.x, p2.z - p1.z) / step));
                for (int k = 0; k < n; k++)
                {
                    float t = (float)k / n, t2 = t * t, t3 = t2 * t;
                    output.Add(new V2(F(p0.x, p1.x, p2.x, p3.x, t, t2, t3), F(p0.z, p1.z, p2.z, p3.z, t, t2, t3)));
                }
            }
            output.Add(ctrl[ctrl.Count - 1]);
            return output;
        }

        static float F(float a, float b, float c, float d, float t, float t2, float t3) =>
            0.5f * (2 * b + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t2 + (-a + 3 * b - 3 * c + d) * t3);
    }
}
