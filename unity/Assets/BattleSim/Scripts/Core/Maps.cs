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
        /// <summary>Каменный мост (для отрисовки; деревянные — в горах).</summary>
        public bool Stone;
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
        public float SolidTop(float x, float z, float ground, bool parapet = true)
        {
            var L = Grid.Near(x, z);
            float best = float.NegativeInfinity;
            if (L != null)
                for (int i = 0; i < L.Count; i++)
                {
                    var d = L[i];
                    if (!d.Solid || !Locate(d, x, z, out float t, out float lat)) continue;
                    float h = HeightOf(d, t, ground);
                    if (parapet && d.Parapet > 0 && lat * d.OutSign > d.W * 0.25f) h += d.Parapet;
                    if (h > best) best = h;
                }
            return best;
        }

        /// <summary>Боевой ход стены под точкой (или null): для раскладки стрелков вдоль стены.</summary>
        public Deck WallAt(float x, float z)
        {
            var L = Grid.Near(x, z);
            if (L != null)
                foreach (var d in L)
                    if (d.Kind == DeckKind.Wall && Locate(d, x, z, out _, out _)) return d;
            return null;
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
        /// <summary>
        /// Отвесы между соседними клетками (биты: 1 — к x+1, 2 — к z+1, 4 — к x+1,z+1, 8 — к x-1,z+1). Высоты центров
        /// клеток могут отличаться на 1,8 м, а между ними — метровый уступ террасы на 30 см: шаг его не пускает, значит,
        /// и путь через него класть нельзя, иначе бойцы упираются в уступ, куда их ведёт поиск.
        /// </summary>
        readonly byte[] Sheer;
        /// <summary>
        /// Добавка к цене пути за крутизну склона в клетке: по крутому идти дольше и тяжелее, поэтому поиск выбирает
        /// дорогу — серпантин, подъём-улицу, — а не прямую через щель в уступе, где отряд застревает давкой.
        /// </summary>
        readonly float[] SlopeCost;

        readonly Search main;

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
            Sheer = new byte[n];
            SlopeCost = new float[n];
            Build(w);
            main = new Search(n);
            Region = new[] { Label(0), Label(1) };
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
                        if (depth > World.WadeMax) si = sc = 0;                     // глубокая вода
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
            // крутизна склона в клетке (по земле; настил ровный) — к цене пути
            for (int iz = 0; iz < D; iz++)
                for (int ix = 0; ix < D; ix++)
                {
                    int i = iz * D + ix;
                    if (Speed[0][i] == 0 && Speed[1][i] == 0) continue;
                    var p = Center(i);
                    if (w.OnDeck(p.x, p.z)) continue;
                    float h = c * 0.5f;
                    float gx = (w.HeightAt(p.x + h, p.z) - w.HeightAt(p.x - h, p.z)) / c, gz = (w.HeightAt(p.x, p.z + h) - w.HeightAt(p.x, p.z - h)) / c;
                    SlopeCost[i] = MathF.Max(0, M.Hypot(gx, gz) - 0.35f) * 2.5f;
                }
            // отвесы между центрами соседних клеток: профиль рельефа (с настилами) с шагом ~0,3 м, как правило шага бойца
            for (int iz = 0; iz < D; iz++)
                for (int ix = 0; ix < D; ix++)
                {
                    int i = iz * D + ix;
                    if (Speed[0][i] == 0 && Speed[1][i] == 0) continue;
                    var p = Center(i);
                    if (ix + 1 < D && SheerLine(w, p.x, p.z, p.x + c, p.z)) Sheer[i] |= 1;
                    if (iz + 1 < D && SheerLine(w, p.x, p.z, p.x, p.z + c)) Sheer[i] |= 2;
                    if (ix + 1 < D && iz + 1 < D && SheerLine(w, p.x, p.z, p.x + c, p.z + c)) Sheer[i] |= 4;
                    if (ix > 0 && iz + 1 < D && SheerLine(w, p.x, p.z, p.x - c, p.z + c)) Sheer[i] |= 8;
                }
            int b = 0;
            for (int iz = 0; iz < D; iz++)
                for (int ix = 0; ix < D; ix++)
                {
                    int i = iz * D + ix;
                    // и для конницы: ограды пехота перелезает, коню не перескочить — без этого обход не искался вовсе
                    if (Speed[0][i] == 0 || Speed[1][i] == 0) { b++; continue; }
                    if (ix + 1 < D && !Step(i, i + 1, false)) b++;
                    if (iz + 1 < D && !Step(i, i + D, false)) b++;
                }
            Barriers = b;
        }

        /// <summary>
        /// Связные области для пехоты [0] и конницы [1]: из клетки можно дойти только до клеток
        /// с тем же номером. Так недостижимая цель (другой уступ, остров) видна сразу, без поиска.
        /// </summary>
        public int[][] Region;

        int[] Label(int cls)
        {
            int D = Dim, n = D * D;
            var sp = Speed[cls];
            var R = new int[n];
            for (int i = 0; i < n; i++) R[i] = -1;
            var q = new int[n];
            int label = 0;
            for (int s0 = 0; s0 < n; s0++)
            {
                if (sp[s0] == 0 || R[s0] >= 0) continue;
                int qh = 0, qt = 0;
                q[qt++] = s0; R[s0] = label;
                while (qh < qt)
                {
                    int c = q[qh++], cx = c % D, cz = c / D;
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dz == 0) continue;
                            int nx = cx + dx, nz = cz + dz;
                            if (nx < 0 || nz < 0 || nx >= D || nz >= D) continue;
                            int ni = nz * D + nx;
                            if (R[ni] >= 0 || sp[ni] == 0) continue;
                            bool diag = dx != 0 && dz != 0;
                            if (!Step(c, ni, diag)) continue;
                            if (diag)
                            {
                                int a = cz * D + nx, b = nz * D + cx;
                                if (sp[a] == 0 || sp[b] == 0 || !Step(c, a, false) || !Step(c, b, false)) continue;
                            }
                            R[ni] = label; q[qt++] = ni;
                        }
                }
                label++;
            }
            return R;
        }

        /// <summary>Ближайшая к клетке g клетка области reg (или -1, если такой нет в радиусе 60 клеток).</summary>
        int NearestInRegion(int g, int reg, int cls)
        {
            int D = Dim, x0 = g % D, z0 = g / D;
            var R = Region[cls];
            for (int r = 1; r <= 60; r++)
            {
                int best = -1, bd = int.MaxValue;
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                        int x = x0 + dx, z = z0 + dz;
                        if (x < 0 || z < 0 || x >= D || z >= D) continue;
                        int i = z * D + x;
                        if (R[i] != reg) continue;
                        int dd = dx * dx + dz * dz;
                        if (dd < bd) { bd = dd; best = i; }
                    }
                if (best >= 0) return best;
            }
            return -1;
        }

        /// <summary>Можно ли дойти из (ax, az) в (bx, bz) — одна связная область.</summary>
        public bool Reachable(float ax, float az, float bx, float bz, int cls)
        {
            int a = Idx(ax, az), b = Idx(bx, bz);
            return a >= 0 && b >= 0 && Region[cls][a] >= 0 && Region[cls][a] == Region[cls][b];
        }

        /// <summary>Можно ли шагнуть между соседними клетками по высоте.</summary>
        public bool Step(int a, int b, bool diag)
        {
            if (MathF.Abs(Surf[a] - Surf[b]) > (diag ? 2.7f : 1.9f)) return false;
            int dx = b % Dim - a % Dim, dz = b / Dim - a / Dim;
            switch (dz * 3 + dx)
            {
                case 1: return (Sheer[a] & 1) == 0;
                case -1: return (Sheer[b] & 1) == 0;
                case 3: return (Sheer[a] & 2) == 0;
                case -3: return (Sheer[b] & 2) == 0;
                case 4: return (Sheer[a] & 4) == 0;
                case -4: return (Sheer[b] & 4) == 0;
                case 2: return (Sheer[a] & 8) == 0;
                case -2: return (Sheer[b] & 8) == 0;
                default: return true;
            }
        }

        /// <summary>Есть ли на прямой уступ круче, чем пускает шаг бойца (кроме края настила — въезда на мост, лестницу).</summary>
        static bool SheerLine(World w, float ax, float az, float bx, float bz)
        {
            float d = M.Hypot(bx - ax, bz - az);
            int n = Math.Max(2, (int)MathF.Ceiling(d / 0.3f));
            float step = d / n, prev = w.GroundAt(ax, az);
            bool prevDeck = w.OnDeck(ax, az);
            for (int k = 1; k <= n; k++)
            {
                float x = ax + (bx - ax) * k / n, z = az + (bz - az) * k / n, h = w.GroundAt(x, z);
                bool deck = w.OnDeck(x, z);
                if (MathF.Abs(h - prev) > (deck != prevDeck ? World.DeckStep + step * 0.5f : step * 1.3f + 0.02f)) return true;
                prev = h; prevDeck = deck;
            }
            return false;
        }

        public int Idx(float x, float z)
        {
            int ix = M.Floor((x + Half) / CellSize), iz = M.Floor((z + Half) / CellSize);
            return ix < 0 || iz < 0 || ix >= Dim || iz >= Dim ? -1 : iz * Dim + ix;
        }

        public V2 Center(int i) => new V2(-Half + (i % Dim + 0.5f) * CellSize, -Half + (i / Dim + 0.5f) * CellSize);
        public float SpeedAt(float x, float z, int cls) { int i = Idx(x, z); return i < 0 ? 1 : Speed[cls][i]; }
        public bool ConcealAt(float x, float z) { int i = Idx(x, z); return i >= 0 && Conceal[i] == 1; }
        public bool CanopyAt(float x, float z) { int i = Idx(x, z); return i >= 0 && Canopy[i] == 1; }

        /// <summary>Прямая не только свободна, но и полога: напрямую по ней идти не хуже, чем по найденной дороге.</summary>
        public const float Gentle = 0.75f;

        /// <summary>
        /// Свободна ли прямая (maxSlope — и не круче этого по цене склона): обход всех клеток, через которые она проходит (DDA), переходы — только через стороны клеток.
        /// Раньше проверялись точки через полклетки, и прямая, чиркнувшая угол ограды, считалась свободной — по такому
        /// спрямлённому пути точка отряда упиралась в ограду навсегда.
        /// </summary>
        public bool LineClear(float ax, float az, float bx, float bz, int cls, float maxSlope = float.PositiveInfinity)
        {
            var sp = Speed[cls];
            float fx = (ax + Half) / CellSize, fz = (az + Half) / CellSize, tx = (bx + Half) / CellSize, tz = (bz + Half) / CellSize;
            int x = M.Floor(fx), z = M.Floor(fz), x1 = M.Floor(tx), z1 = M.Floor(tz);
            if (x < 0 || z < 0 || x >= Dim || z >= Dim) return true;
            float dx = tx - fx, dz = tz - fz;
            int sx = dx > 0 ? 1 : -1, sz = dz > 0 ? 1 : -1;
            float tdx = dx != 0 ? MathF.Abs(1 / dx) : float.PositiveInfinity, tdz = dz != 0 ? MathF.Abs(1 / dz) : float.PositiveInfinity;
            float tmx = dx != 0 ? ((sx > 0 ? x + 1 - fx : fx - x) * tdx) : float.PositiveInfinity;
            float tmz = dz != 0 ? ((sz > 0 ? z + 1 - fz : fz - z) * tdz) : float.PositiveInfinity;
            int prev = z * Dim + x;
            for (int guard = 0; guard < 4 * Dim && (x != x1 || z != z1); guard++)
            {
                if (tmx < tmz) { if (tmx > 1) break; x += sx; tmx += tdx; }
                else if (tmz < tmx) { if (tmz > 1) break; z += sz; tmz += tdz; }
                else
                { // ровно через угол — должны быть проходимы обе соседние по сторонам клетки
                    if (tmx > 1) break;
                    int ca = z * Dim + x + sx, cb = (z + sz) * Dim + x;
                    if (x + sx < 0 || x + sx >= Dim || z + sz < 0 || z + sz >= Dim) return true;
                    if (sp[ca] == 0 || sp[cb] == 0 || !Step(prev, ca, false) || !Step(prev, cb, false)) return false;
                    x += sx; z += sz; tmx += tdx; tmz += tdz;
                }
                if (x < 0 || z < 0 || x >= Dim || z >= Dim) return true;
                int i = z * Dim + x;
                if (sp[i] == 0 || !Step(prev, i, false) || SlopeCost[i] > maxSlope) return false;
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

        /// <summary>Счётчики для замеров: сколько поисков пути и раскрытых клеток.</summary>
        public int StatCalls, StatExpanded, StatFailed;
        /// <summary>Сколько клеток перебрал последний законченный поиск.</summary>
        public int LastExpanded;

        /// <summary>
        /// Состояние поиска A*. Поиск можно провести разом (FindPath) или понемногу, по нескольку
        /// тысяч клеток за шаг боя (Begin + Continue), — тогда длинные пути в горах не дают рывков.
        /// </summary>
        public sealed class Search
        {
            internal readonly float[] gs;
            internal readonly int[] came, seen, done;
            internal readonly MinHeap heap = new MinHeap();
            internal int run, s, g, gx, gz, cls, maxExpand, bestC;
            internal float bestH, crowdCost, avoidX, avoidZ, avoidR, greed, bx, bz;
            internal bool goalOpen;
            public int Expanded;
            public bool Active;
            internal Search(int n) { gs = new float[n]; came = new int[n]; seen = new int[n]; done = new int[n]; }
        }

        public Search NewSearch() => new Search(Dim * Dim);

        /// <summary>A* по сетке с учётом перепадов высот; путь сглаживается до точек поворота.</summary>
        public List<V2> FindPath(float ax, float az, float bx, float bz, int cls, float crowdCost = 0, float avoidX = 0, float avoidZ = 0, float avoidR = 0, int maxExpand = 40000, float greed = 1.35f, float ay = float.NaN)
        {
            if (!Begin(main, ax, az, bx, bz, cls, crowdCost, avoidX, avoidZ, avoidR, maxExpand, greed, ay)) return null;
            Continue(main, int.MaxValue, out var path);
            return path;
        }

        /// <summary>Начать поиск пути; false — идти некуда (старт или цель вне поля, нет достижимого места рядом с целью).</summary>
        /// <summary>
        /// Стоящий на земле у края моста или стены попал в клетку, чей центр на настиле: путь — от ближайшей клетки его уровня,
        /// иначе поиск поведёт его поверху, куда ему не залезть.
        /// </summary>
        int NearestLevel(int s, float x, float z, float y, float[] sp)
        {
            int best = s, sx = s % Dim, sz = s / Dim; float bd = float.MaxValue;
            for (int dz = -2; dz <= 2; dz++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    int cx = sx + dx, cz = sz + dz;
                    if (cx < 0 || cz < 0 || cx >= Dim || cz >= Dim) continue;
                    int i = cz * Dim + cx;
                    if (sp[i] == 0 || MathF.Abs(Surf[i] - y) > 0.8f) continue;
                    var c = Center(i); float d = M.Hypot(c.x - x, c.z - z);
                    if (d < bd) { bd = d; best = i; }
                }
            return best;
        }

        public bool Begin(Search q, float ax, float az, float bx, float bz, int cls, float crowdCost = 0, float avoidX = 0, float avoidZ = 0, float avoidR = 0, int maxExpand = 40000, float greed = 1.35f, float ay = float.NaN)
        {
            StatCalls++;
            q.Active = false;
            var sp = Speed[cls];
            int s = Idx(ax, az), g = Idx(bx, bz);
            if (s < 0 || g < 0) return false;
            if (!float.IsNaN(ay) && MathF.Abs(Surf[s] - ay) > 1f) s = NearestLevel(s, ax, az, ay, sp);
            if (sp[s] == 0) s = NearestOpen(s, sp);
            if (s < 0) return false;
            bool goalOpen = sp[g] > 0;
            // цель недостижима (на другом уступе, за обрывом) — идём к ближайшему месту, куда дойти можно
            if (!goalOpen || Region[cls][g] != Region[cls][s])
            {
                goalOpen = false;
                g = NearestInRegion(g, Region[cls][s], cls);
            }
            if (g < 0) { StatFailed++; return false; }
            q.run++;
            q.s = s; q.g = g; q.gx = g % Dim; q.gz = g / Dim; q.cls = cls; q.maxExpand = maxExpand;
            q.crowdCost = crowdCost; q.avoidX = avoidX; q.avoidZ = avoidZ; q.avoidR = avoidR; q.greed = greed;
            q.bx = bx; q.bz = bz; q.goalOpen = goalOpen;
            q.heap.Clear();
            q.gs[s] = 0; q.came[s] = -1; q.seen[s] = q.run;
            q.bestC = s; q.bestH = H(s, q.gx, q.gz, Dim);
            q.heap.Push(s, q.bestH);
            q.Expanded = 0;
            q.Active = true;
            return true;
        }

        /// <summary>
        /// Продолжить поиск, перебрав не больше budget клеток. true — поиск закончен: path — путь
        /// (или путь к самому близкому к цели месту, если не уложились в maxExpand) либо null.
        /// </summary>
        public bool Continue(Search q, int budget, out List<V2> path)
        {
            path = null;
            if (!q.Active) return true;
            int D = Dim, rn = q.run, g = q.g, gx = q.gx, gz = q.gz;
            var sp = Speed[q.cls];
            var S = Surf;
            var gs = q.gs; var came = q.came; var seen = q.seen; var done = q.done; var heap = q.heap;
            float crowdCost = q.crowdCost, avoidR = q.avoidR, avoidX = q.avoidX, avoidZ = q.avoidZ, greed = q.greed;
            bool reached = false;
            while (heap.Size > 0 && q.Expanded < q.maxExpand)
            {
                if (budget <= 0) return false; // продолжим на следующем шаге
                int c = heap.Pop();
                if (done[c] == rn) continue;
                done[c] = rn; q.Expanded++; budget--;
                if (c == g) { reached = true; break; }
                float hc = H(c, gx, gz, D);
                if (hc < q.bestH) { q.bestH = hc; q.bestC = c; }
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
                        float ng = gs[c] + (diag ? 1.4142f : 1f) * (2f / (sp[c] + sp[ni]) + Crowd[ni] * crowdCost + SlopeCost[ni]) + MathF.Abs(S[ni] - S[c]) * 0.4f;
                        if (avoidR > 0)
                        {
                            float ox = -Half + (nx + 0.5f) * CellSize - avoidX, oz = -Half + (nz + 0.5f) * CellSize - avoidZ;
                            if (ox * ox + oz * oz < avoidR * avoidR) ng += 6;
                        }
                        if (seen[ni] != rn || ng < gs[ni]) { seen[ni] = rn; gs[ni] = ng; came[ni] = c; heap.Push(ni, ng + H(ni, gx, gz, D) * greed); }
                    }
            }
            q.Active = false;
            StatExpanded += q.Expanded; LastExpanded = q.Expanded;
            bool goalOpen = q.goalOpen;
            if (!reached)
            { // не уложились в перебор — путь до клетки, ближе всех подошедшей к цели (следующий поиск продолжит)
                if (q.maxExpand >= 40000 || q.bestC == q.s) { StatFailed++; return true; }
                g = q.bestC; goalOpen = false;
            }
            var cells = new List<int>();
            for (int c = g; c != -1; c = came[c]) cells.Add(c);
            cells.Reverse();
            var pts = new List<V2>(cells.Count);
            foreach (int i in cells) pts.Add(Center(i));
            if (goalOpen) pts[pts.Count - 1] = new V2(q.bx, q.bz);
            var output = new List<V2> { pts[0] };
            for (int i = 0; i < pts.Count - 1;)
            {
                int j = Math.Min(pts.Count - 1, i + 24);
                // спрямляем, не срезая по крутому то, что поиск обошёл по дороге (серпантин не превращается в прямую по склону)
                float steep = 0;
                for (int k = i; k <= j; k++) steep = MathF.Max(steep, SlopeCost[cells[k]]);
                while (j > i + 1)
                {
                    if (LineClear(pts[i].x, pts[i].z, pts[j].x, pts[j].z, q.cls, MathF.Max(Gentle, steep))) break;
                    j--;
                    steep = 0;
                    for (int k = i; k <= j; k++) steep = MathF.Max(steep, SlopeCost[cells[k]]);
                }
                output.Add(pts[j]);
                i = j;
            }
            path = output;
            return true;
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
