using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleSim.Core
{
    public sealed class Hill { public float X, Z, R, H; }
    public sealed class Ridge { public float Ax, Az, Bx, Bz, R, H; }
    public sealed class Ravine { public V2[] Pts; public float W, D; }

    public sealed class Features
    {
        public List<Hill> Hills = new List<Hill>();
        public List<Ridge> Ridges = new List<Ridge>();
        public Ravine Ravine;
        public List<FeatureWall> Walls = new List<FeatureWall>();
    }

    public sealed class ForestTree { public float X, Z, Rot, K, Trunk; public int Kind; }
    public sealed class Watchtower { public float X, Z, Y; }

    public sealed class HighSpot { public float X, Z, H, Prom; }
    public sealed class LowSpot { public float X, Z, H, Depth; }
    public sealed class HideSpot { public float X, Z, Depth; }

    public sealed class Analysis
    {
        public float Step;
        public int N;
        public float[] Prom;
        public List<HighSpot> High;
        public List<LowSpot> Low;
        public List<HideSpot> Hide;
    }

    public enum CoverKind { Low, Wall, WallWalk, House }
    public struct CoverSpot { public float X, Z; public CoverKind Kind; }

    /// <summary>Размещение модели (дерево, камень): вид, позиция, поворот, масштаб.</summary>
    public struct Placement { public int Kind; public float X, Y, Z, Rot, Scale; }

    /// <summary>Пучок травы, камыш или цветок: позиция, поворот, масштаб по осям и цвет.</summary>
    public struct Tuft { public float X, Y, Z, Rot, Sx, Sy, Sz; public Rgb Color; }

    /// <summary>Отдельная постройка за полем боя: замок, мельница, сторожевая башня.</summary>
    public sealed class Landmark { public string Model; public float X, Y, Z, Rot, Height; }

    /// <summary>
    /// Мир: рельеф по типу местности, тактические особенности (холмы, овраги, ограды),
    /// настилы (мосты, стены), сетка проходимости и анализ высот для полководцев.
    /// Координаты — как в веб-версии (правая система: +z к зрителю); Unity-слой переводит их сам.
    /// </summary>
    public sealed class World
    {
        public const float Water = 0f;
        /// <summary>Глубже этого вброд не пройти (вода по пояс): так размечена сетка путей, так же проверяется каждый шаг.</summary>
        public const float WadeMax = 0.9f;
        /// <summary>Ступенька на край настила (въезд на мост, первая ступень лестницы) — шагом, без прыжка.</summary>
        public const float DeckStep = 0.3f;
        public const int TreeKinds = 7, RockKinds = 5;
        public static readonly string[] TreeModels = { "tree_single_A", "tree_single_B", "trees_A_large", "trees_A_medium", "trees_B_large", "trees_B_medium", "trees_B_small" };
        public static readonly float[] TreeHeights = { 6.5f, 7f, 9f, 7.5f, 9f, 7.5f, 6f };
        public static readonly string[] RockModels = { "rock_single_A", "rock_single_B", "rock_single_C", "rock_single_D", "rock_single_E" };

        public float Size, Half, Cell, Field;
        public int Res;
        public float[] H;
        public MapType Type;
        public bool Big;
        public Style Style;
        public Rng Rand;
        public Perlin Pn;
        double ohx, ohz, olx, olz, omx, omz, ocx, ocz;
        public CityPlan Town;
        public MountainPlan Mtn;
        public Decks Decks;
        public float[] PathMask;
        public List<Deck> Bridges = new List<Deck>();
        public List<Watchtower> Watchtowers = new List<Watchtower>();
        /// <summary>Места для стрелков на боевом ходу стен (у зубцов) и на башнях; Out — куда смотрит стена.</summary>
        public List<(V2 P, V2 Out)> WallSpots = new List<(V2, V2)>();
        public Features Features;
        public V2 Wind;
        public Obstacles Obs;
        public SpatialGrid<FeatureWall> WallGrid;
        public List<ForestTree> Trees = new List<ForestTree>();
        public NavGrid Nav;
        public Analysis An;
        public V3 SunDir;

        // Декор (раскладка; рисует Unity-слой)
        public List<Placement>[] TreeLists, RockLists;
        public List<Tuft> Grass = new List<Tuft>(), Reeds = new List<Tuft>(), FlowerList = new List<Tuft>();
        public List<Landmark> Landmarks = new List<Landmark>();

        public void SetScale(bool big)
        {
            Field = big ? 118 : 80;
            Size = Field * 2 + 280;
            Res = big ? 300 : 256;
            Half = Size / 2;
            Cell = Size / Res;
        }

        /// <summary>Строит карту. cityDefs нужны для города (размеры моделей домов); lowEnd — меньше травы.</summary>
        public void Generate(int seed, Style style, MapType type, bool big, IList<CityDef> cityDefs, bool lowEnd = false)
        {
            SetScale(big);
            Type = type; Big = big; Style = style;
            H = new float[(Res + 1) * (Res + 1)];
            var rand = Rand = new Rng(seed);
            Pn = new Perlin(rand);
            ohx = rand.Next() * 200; ohz = rand.Next() * 200; olx = rand.Next() * 200; olz = rand.Next() * 200;
            omx = rand.Next() * 200; omz = rand.Next() * 200; ocx = rand.Next() * 200; ocz = rand.Next() * 200;
            Town = type == MapType.City && cityDefs != null ? CityPlan.Make(rand, cityDefs, Field) : null;
            Mtn = type == MapType.Mountains ? MountainPlan.Make(rand, Field) : null;
            Decks = new Decks(Field);
            PathMask = null; Bridges = new List<Deck>(); Watchtowers = new List<Watchtower>();
            Features = MakeFeatures(rand, type);
            float wa = rand.F() * M.PI * 2, ws = rand.Next() < 0.2 ? 0 : 1 + rand.F() * 6;
            Wind = new V2(MathF.Cos(wa) * ws, MathF.Sin(wa) * ws);
            SunDir = style != null ? style.SunDir : new V3(0, 1, 0);

            for (int z = 0; z <= Res; z++)
                for (int x = 0; x <= Res; x++)
                    H[z * (Res + 1) + x] = RawHeight(x * Cell - Half, z * Cell - Half);
            if (Mtn != null) CarveMountains(rand);
            if (Town != null) AddFortress();

            // Препятствия: стволы леса, дома, мелочи; ограды — отдельной сеткой (их перелезают)
            Obs = new Obstacles(Field);
            WallGrid = new SpatialGrid<FeatureWall>(Field);
            foreach (var w in Features.Walls)
            {
                float len = M.Hypot(w.Bx - w.Ax, w.Bz - w.Az);
                WallGrid.Insert(w, (w.Ax + w.Bx) / 2, (w.Az + w.Bz) / 2, len / 2 + w.T);
            }
            // Сплошные ограды — препятствия: сквозь них не пройти, только через проёмы
            foreach (var w in Features.Walls)
            {
                if (!w.Solid) continue;
                float len = M.Hypot(w.Bx - w.Ax, w.Bz - w.Az);
                if (len < 0.3f) continue;
                float mx = (w.Ax + w.Bx) / 2, mz = (w.Az + w.Bz) / 2;
                Obs.AddRect(mx, mz, len / 2, w.T / 2, MathF.Atan2(w.Bz - w.Az, w.Bx - w.Ax), w.H, MathF.Min(HeightAt(w.Ax, w.Az), HeightAt(w.Bx, w.Bz)));
            }
            Trees = type == MapType.Forest ? PlantForest(rand) : new List<ForestTree>();
            foreach (var t in Trees) Obs.AddCircle(t.X, t.Z, t.Trunk, 7, HeightAt(t.X, t.Z));
            if (Town != null)
            {
                foreach (var b in Town.Buildings)
                {
                    b.Y = FootprintY(b);
                    if (b.Def.Kind == "well") Obs.AddCircle(b.X, b.Z, MathF.Max(b.Hx, b.Hz), b.Top, b.Y);
                    else Obs.AddRect(b.X, b.Z, b.Hx, b.Hz, b.Rot, b.Top, b.Y);
                }
                foreach (var p in Town.Props)
                {
                    float s = 1.1f / p.Def.H;
                    p.S = s; p.Y = HeightAt(p.X, p.Z);
                    Obs.AddCircle(p.X, p.Z, MathF.Max(p.Def.W, p.Def.D) * s * 0.5f, 1.1f, p.Y);
                }
            }
            foreach (var t in Watchtowers) Obs.AddCircle(t.X, t.Z, 3, 11, t.Y);
            Nav = new NavGrid(this);
            Analyze();
            if (style != null) PlanDecor(style, lowEnd);
        }

        /// <summary>Где армии выстраиваются перед боем (расстояние от центра до первой линии).</summary>
        public float SpawnZ => Town != null ? Town.CZ + 17 : Mtn != null ? Field * 0.68f : Field * (Big ? 0.3f : 0.28f);

        /// <summary>Высота, на которой стоит солдат: земля или настил (мост, боевой ход стены, лестница).</summary>
        public float GroundAt(float x, float z)
        {
            float g = HeightAt(x, z), d = Decks.Surface(x, z, g);
            return d > g ? d : g;
        }

        /// <summary>
        /// Множитель скорости на склоне; gy — подъём (+) или спуск (−) на метр пути. В гору тяжело; под пологую горку
        /// чуть быстрее, по крутому спуску — осторожно: и пеший, и конь не несутся вниз по откосу, сползая на полметра за кадр.
        /// </summary>
        public static float SlopeSpeed(float gy)
        {
            if (gy > 0) return MathF.Max(0.35f, 1 - gy * 1.4f);
            float d = -gy;
            return d < 0.25f ? 1 + d * 0.4f : MathF.Max(0.4f, 1.1f - (d - 0.25f) * 1.1f);
        }

        /// <summary>Земля или настил под точкой на высоте y — не настил над головой (тело, летящее под мостом).</summary>
        public float GroundBelow(float x, float z, float y)
        {
            float g = HeightAt(x, z), d = Decks.SurfaceBelow(x, z, g, y);
            return d > g ? d : g;
        }

        public bool InField(float x, float z, float margin = 0f) => MathF.Abs(x) <= Field - margin && MathF.Abs(z) <= Field - margin;

        public V2 ClampField(float x, float z, float m = 4f) => new V2(M.Clamp(x, -Field + m, Field - m), M.Clamp(z, -Field + m, Field - m));
        public V2 ClampField(V2 p, float m = 4f) => ClampField(p.x, p.z, m);

        // ---------------------------------------------------------------- тактический рельеф

        /// <summary>Холмы, овраги, гряды, островки и развалины — всё, за что можно воевать.</summary>
        Features MakeFeatures(Rng R, MapType type)
        {
            var f = new Features();
            float F = Field, S = F / 80;
            void Ruins(int count)
            {
                for (int i = 0; i < count; i++)
                {
                    float cx = M.Lerp(-F * 0.78f, F * 0.78f, R.F()), cz = M.Lerp(-F * 0.42f, F * 0.42f, R.F());
                    float ang = R.Next() < 0.6 ? M.Lerp(-0.35f, 0.35f, R.F()) : M.PI / 2 + M.Lerp(-0.35f, 0.35f, R.F());
                    float len = M.Lerp(10, 22, R.F()), dx = MathF.Cos(ang), dz = MathF.Sin(ang);
                    float s = -len / 2;
                    while (s < len / 2)
                    {
                        float e = MathF.Min(len / 2, s + M.Lerp(3.5f, 7, R.F()));
                        f.Walls.Add(new FeatureWall { Ax = cx + dx * s, Az = cz + dz * s, Bx = cx + dx * e, Bz = cz + dz * e, H = 1.35f, T = 0.5f });
                        s = e + M.Lerp(2.8f, 3.8f, R.F()); // проём — отряд проходит, но тесно
                    }
                }
            }

            if (type == MapType.City) { if (Town != null) f.Walls.AddRange(Town.Walls); return f; }
            if (type == MapType.Mountains) return f; // рельеф строит MountainPlan: террасы, ущелье, серпантины

            if (type == MapType.Swamp)
            {
                int n = M.Round(M.Lerp(6, 9, R.F()) * S);
                for (int i = 0; i < n; i++)
                {
                    var h = new Hill { X = M.Lerp(-F * 0.75f, F * 0.75f, R.F()) };
                    h.Z = M.Lerp(-F * 0.4f, F * 0.4f, R.F()); h.R = M.Lerp(6, 10, R.F()); h.H = M.Lerp(1.8f, 3.2f, R.F());
                    f.Hills.Add(h);
                }
                Ruins(1 + (int)(R.Next() * 2));
                return f;
            }

            // Поле и лес: холмы по сторонам, овраг между армиями, гряда на фланге
            foreach (float side in new[] { -1f, 1f })
            {
                var h = new Hill { X = M.Lerp(-F * 0.56f, F * 0.56f, R.F()) };
                h.Z = side * M.Lerp(F * 0.25f, F * 0.52f, R.F()); h.R = M.Lerp(11, 16, R.F()); h.H = M.Lerp(4.5f, 7.5f, R.F());
                f.Hills.Add(h);
            }
            if (R.Next() < 0.65)
            {
                float sgn = R.Next() < 0.5 ? -1 : 1;
                var h = new Hill { X = sgn * M.Lerp(F * 0.48f, F * 0.75f, R.F()) };
                h.Z = M.Lerp(-10, 10, R.F()); h.R = M.Lerp(9, 13, R.F()); h.H = M.Lerp(3.5f, 6, R.F());
                f.Hills.Add(h);
            }
            if (type == MapType.Field || R.Next() < 0.5)
            {
                float z0 = M.Lerp(-10, 10, R.F()), tilt = M.Lerp(-0.22f, 0.22f, R.F()), x0 = M.Lerp(-F * 0.72f, -F * 0.28f, R.F()), x1 = M.Lerp(F * 0.28f, F * 0.75f, R.F()), ph = R.F() * 6;
                var pts = new V2[17];
                for (int i = 0; i <= 16; i++) { float x = M.Lerp(x0, x1, i / 16f); pts[i] = new V2(x, z0 + x * tilt + MathF.Sin(x * 0.06f + ph) * 6); }
                float w = M.Lerp(6.5f, 9, R.F());
                float d = type == MapType.Forest ? M.Lerp(2.2f, 3, R.F()) : M.Lerp(3.4f, 4.4f, R.F());
                f.Ravine = new Ravine { Pts = pts, W = w, D = d };
            }
            if (R.Next() < 0.7)
            {
                float s = R.Next() < 0.5 ? -1 : 1, x = s * M.Lerp(F * 0.52f, F * 0.8f, R.F());
                var r = new Ridge { Ax = x, Az = M.Lerp(-F * 0.48f, -F * 0.1f, R.F()) };
                r.Bx = x + M.Lerp(-10, 10, R.F()); r.Bz = M.Lerp(F * 0.1f, F * 0.48f, R.F()); r.R = 4.5f; r.H = M.Lerp(2.6f, 3.6f, R.F());
                f.Ridges.Add(r);
            }
            Ruins(type == MapType.Forest ? 1 + (int)(R.Next() * 2) : M.Round((3 + (int)(R.Next() * 3)) * S));
            return f;
        }

        /// <summary>Лес: плотность чащи 0..1 (у мест построения армий — поляны).</summary>
        public float ForestAt(float x, float z)
        {
            if (Type != MapType.Forest || !InField(x, z)) return 0;
            float d = (float)Pn.N01(x * 0.024 + ocx, z * 0.024 + ocz);
            float mask = 1 - 0.8f * M.Smooth(Field * 0.46f, Field * 0.72f, MathF.Abs(z));
            return M.Smooth(0.4f, 0.6f, d) * mask;
        }

        /// <summary>Болото: камыш в мелкой воде и на топких берегах.</summary>
        public bool ReedsAt(float x, float z)
        {
            if (Type != MapType.Swamp) return false;
            float h = HeightAt(x, z);
            return h < 0.45f && h > -0.9f && Pn.N01(x * 0.09 + olx, z * 0.09 + olz) > 0.42;
        }

        List<ForestTree> PlantForest(Rng R)
        {
            var output = new List<ForestTree>();
            int tries = M.Round(Field * Field * 0.26f);
            for (int i = 0; i < tries; i++)
            {
                float x = M.Lerp(-Field + 2, Field - 2, R.F()), z = M.Lerp(-Field + 2, Field - 2, R.F());
                float f = ForestAt(x, z);
                if (f < 0.3f || R.Next() > f * 0.85f) continue;
                bool cluster = R.Next() < 0.22;
                var t = new ForestTree { X = x, Z = z, Kind = cluster ? 2 + (int)(R.Next() * 5) : (int)(R.Next() * 2) };
                t.Rot = R.F() * M.PI * 2; t.K = M.Lerp(0.95f, 1.4f, R.F()); t.Trunk = cluster ? 1.5f : 0.45f;
                output.Add(t);
            }
            return output;
        }

        /// <summary>Высота основания дома: по самому низкому углу, чтобы он не висел над землёй.</summary>
        float FootprintY(Building b)
        {
            float lo = float.PositiveInfinity;
            int[,] c = { { -1, -1 }, { 1, -1 }, { 1, 1 }, { -1, 1 }, { 0, 0 } };
            for (int i = 0; i < 5; i++) lo = MathF.Min(lo, HeightAt(b.X + c[i, 0] * b.Hx, b.Z + c[i, 1] * b.Hz));
            return lo - 0.15f;
        }

        /// <summary>Карта «выпуклости» поля и укромные места: по ним думают полководцы.</summary>
        void Analyze()
        {
            float step = 4;
            int n = (int)Math.Floor(Field * 2 / step) + 1;
            var prom = new float[n * n];
            for (int iz = 0; iz < n; iz++)
                for (int ix = 0; ix < n; ix++)
                {
                    float x = -Field + ix * step, z = -Field + iz * step, s = 0;
                    for (int a = 0; a < 8; a++) s += GroundAt(x + MathF.Cos(a * M.PI / 4) * 14, z + MathF.Sin(a * M.PI / 4) * 14);
                    prom[iz * n + ix] = GroundAt(x, z) - s / 8;
                }
            var high = new List<HighSpot>();
            var low = new List<LowSpot>();
            var hide = new List<HideSpot>();
            for (int iz = 1; iz < n - 1; iz++)
                for (int ix = 1; ix < n - 1; ix++)
                {
                    float p = prom[iz * n + ix], x = -Field + ix * step, z = -Field + iz * step, h = GroundAt(x, z);
                    if (Nav.SpeedAt(x, z, 0) == 0) continue;
                    if (p > 1.4f)
                    {
                        bool top = true;
                        for (int dz = -1; dz <= 1 && top; dz++)
                            for (int dx = -1; dx <= 1; dx++)
                                if ((dx != 0 || dz != 0) && GroundAt(x + dx * step, z + dz * step) > h) { top = false; break; }
                        if (top) high.Add(new HighSpot { X = x, Z = z, H = h, Prom = p });
                    }
                    if (p < -1.0f && h > Water - 0.3f) { low.Add(new LowSpot { X = x, Z = z, H = h, Depth = -p }); hide.Add(new HideSpot { X = x, Z = z, Depth = -p }); }
                    else if ((ix + iz) % 2 == 0 && Nav.ConcealAt(x, z)) hide.Add(new HideSpot { X = x, Z = z, Depth = 1.5f });
                }
            An = new Analysis { Step = step, N = n, Prom = prom, High = high.OrderByDescending(q => q.Prom).Take(16).ToList(), Low = low, Hide = hide };
        }

        public float ProminenceAt(float x, float z)
        {
            var a = An;
            int ix = M.Clamp(M.Round((x + Field) / a.Step), 0, a.N - 1);
            int iz = M.Clamp(M.Round((z + Field) / a.Step), 0, a.N - 1);
            return a.Prom[iz * a.N + ix];
        }

        /// <summary>Отряд укрыт от глаз: в низине, в чаще или в камыше.</summary>
        public bool ConcealedAt(float x, float z) => ProminenceAt(x, z) < -0.9f || Nav.ConcealAt(x, z);

        public bool Walkable(float x, float z, float pad = 0.3f) => Nav.SpeedAt(x, z, 0) > 0 && !TooDeep(x, z) && Obs.Hit(x, z, pad) == null && OnCellLevel(x, z);

        /// <summary>Точка на уровне своей клетки сетки путей (а не на земле под краем моста или стены, чья клетка числится настилом).</summary>
        public bool OnCellLevel(float x, float z) { int i = Nav.Idx(x, z); return i < 0 || MathF.Abs(GroundAt(x, z) - Nav.Surf[i]) < 1f; }

        /// <summary>Стоит ли точка на настиле (мост, стена, лестница), а не на земле.</summary>
        public bool OnDeck(float x, float z) { float g = HeightAt(x, z); return Decks.Surface(x, z, g) > g + 0.01f; }

        /// <summary>
        /// Омут в самой точке (настил моста — не вода). Клетка сетки путей — полтора метра: у края моста
        /// или крутой набережной её центр проходим, а край — уже над руслом.
        /// </summary>
        public bool TooDeep(float x, float z) => Water - GroundAt(x, z) > WadeMax;

        /// <summary>Ближайшее к (x, z) место, где можно стоять (по спирали, до ~14 м); не нашлось — сама точка.</summary>
        /// <summary>Ближайшая к (x, z) точка, где можно стоять; с y — только на этом уровне (не на стене над головой, не под обрывом).</summary>
        public V2 WalkableNear(float x, float z, float pad = 0.5f, float y = float.NaN)
        {
            for (int k = 0; k < 40; k++)
            {
                float a = k * 2.4f, r = k * 0.35f;
                var q = ClampField(x + MathF.Cos(a) * r, z + MathF.Sin(a) * r, 4);
                if (Walkable(q.x, q.z, pad) && (float.IsNaN(y) || MathF.Abs(GroundAt(q.x, q.z) - y) < 0.8f)) return q;
            }
            return float.IsNaN(y) ? new V2(x, z) : WalkableNear(x, z, pad);
        }

        /// <summary>Верх ограды в точке (или -∞, если ограды нет).</summary>
        public float WallTop(float x, float z)
        {
            var L = WallGrid.Near(x, z);
            if (L != null)
                for (int i = 0; i < L.Count; i++)
                {
                    var w = L[i];
                    if (M.SegDist(x, z, w.Ax, w.Az, w.Bx, w.Bz) < w.T * 0.5f + 0.1f) return HeightAt(x, z) + w.H;
                }
            return float.NegativeInfinity;
        }

        /// <summary>Рядом со сплошной оградой (в пределах extra от кладки) — укрытие.</summary>
        public bool NearWall(float x, float z, float extra = 1.2f)
        {
            var L = WallGrid.Near(x, z);
            if (L != null)
                for (int i = 0; i < L.Count; i++)
                {
                    var w = L[i];
                    if (w.Solid && M.SegDist(x, z, w.Ax, w.Az, w.Bx, w.Bz) < w.T * 0.5f + extra) return true;
                }
            return false;
        }

        /// <summary>
        /// Ограда между a и b не дальше maxFromB от b (цель прячется за ней).
        /// top — верх ограды; cross — точка пересечения.
        /// </summary>
        public bool WallBetween(float ax, float az, float bx, float bz, float maxFromB, out float top, out V2 cross)
        {
            top = 0; cross = default;
            float dx = bx - ax, dz = bz - az, d = M.Hypot(dx, dz);
            if (d < 0.1f) return false;
            float ux = dx / d, uz = dz / d;
            for (float k = 0; k <= maxFromB + 4; k += 4)
            {
                var L = WallGrid.Near(bx - ux * k, bz - uz * k);
                if (L == null) continue;
                for (int i = 0; i < L.Count; i++)
                {
                    var w = L[i];
                    if (!w.Solid) continue;
                    float ex = w.Bx - w.Ax, ez = w.Bz - w.Az, den = dx * ez - dz * ex;
                    if (MathF.Abs(den) < 1e-6f) continue;
                    float t = ((w.Ax - ax) * ez - (w.Az - az) * ex) / den, s = ((w.Ax - ax) * dz - (w.Az - az) * dx) / den;
                    if (t < 0 || t > 1 || s < -0.05f || s > 1.05f || (1 - t) * d > maxFromB) continue;
                    cross = new V2(ax + dx * t, az + dz * t);
                    top = HeightAt(cross.x, cross.z) + w.H;
                    return true;
                }
            }
            return false;
        }

        public bool InWall(float x, float z)
        {
            var L = WallGrid.Near(x, z);
            if (L != null)
                for (int i = 0; i < L.Count; i++)
                {
                    var w = L[i];
                    if (!w.Solid && M.SegDist(x, z, w.Ax, w.Az, w.Bx, w.Bz) < w.T * 0.5f + 0.35f) return true;
                }
            return false;
        }

        /// <summary>
        /// Прямая видимость: мешают склоны, ограды, стены, дома и густая чаща. lean — стрелок стоит
        /// у бойницы или за низкой оградой: в пределах lean метров от него зубцы и ограда не мешают
        /// (он выглядывает между зубцами, поверх плетня); так же и цель у самого края стены видна.
        /// </summary>
        public bool Los(float ax, float ay, float az, float bx, float by, float bz, float lean = 0)
        {
            float dx = bx - ax, dz = bz - az, d = M.Hypot(dx, dz);
            int n = Math.Max(2, (int)Math.Ceiling(d / 1.4f));
            float step = d / n, canopy = 0;
            bool forest = Type == MapType.Forest, houses = Obs.List.Count > 0;
            for (int i = 1; i < n; i++)
            {
                float t = (float)i / n, x = ax + dx * t, z = az + dz * t, y = ay + (by - ay) * t, g = HeightAt(x, z);
                if (g > y - 0.1f) return false;
                bool nearA = t * d < lean, nearB = lean > 0 && (1 - t) * d < 1.2f;
                if (Decks.SolidTop(x, z, g, !(nearA || nearB)) > y || Decks.ThinBlocks(x, z, y)) return false;
                if (WallTop(x, z) > y + (nearA ? 0.5f : 0)) return false;
                if (houses && Obs.Blocks(x, z, y)) return false;
                if (forest && y < g + 7 && Nav.CanopyAt(x, z) && (canopy += step) > 7) return false;
            }
            return true;
        }

        /// <summary>Места, где можно укрыться от стрелков врага (ec — центр вражеской армии).</summary>
        public List<CoverSpot> CoverCandidates(V2 ec)
        {
            var output = new List<CoverSpot>();
            foreach (var l in An.Hide) output.Add(new CoverSpot { X = l.X, Z = l.Z, Kind = CoverKind.Low });
            foreach (var w in Features.Walls)
            {
                float mx = (w.Ax + w.Bx) / 2, mz = (w.Az + w.Bz) / 2, l = M.Hypot(w.Bx - w.Ax, w.Bz - w.Az);
                if (l == 0) l = 1;
                float nx = -(w.Bz - w.Az) / l, nz = (w.Bx - w.Ax) / l, side = nx * (mx - ec.x) + nz * (mz - ec.z) > 0 ? 1 : -1;
                output.Add(new CoverSpot { X = mx + nx * 1.6f * side, Z = mz + nz * 1.6f * side, Kind = CoverKind.Wall });
            }
            if (Town != null)
            {
                foreach (var w in Town.Fort.Walls) output.Add(new CoverSpot { X = (w.Ax + w.Bx) / 2, Z = (w.Az + w.Bz) / 2, Kind = CoverKind.WallWalk });
                foreach (var b in Town.Buildings)
                {
                    var v = M.Norm2(b.X - ec.x, b.Z - ec.z);
                    float r = MathF.Max(b.Hx, b.Hz) + 2;
                    output.Add(new CoverSpot { X = b.X + v.x * r, Z = b.Z + v.z * r, Kind = CoverKind.House });
                }
            }
            return output;
        }

        // ---------------------------------------------------------------- горы: серпантины, мосты, сторожевые башни

        public float MaskAt(float x, float z)
        {
            float gx = M.Clamp((x + Half) / Cell, 0, Res), gz = M.Clamp((z + Half) / Cell, 0, Res);
            return PathMask[M.Round(gz) * (Res + 1) + M.Round(gx)];
        }

        /// <summary>
        /// Прокладываем тропы: профиль высоты вдоль тропы сглаживается до уклона не круче ~0,3,
        /// рельеф под тропой подрезается или подсыпается — так на террасах появляются
        /// пологие подъёмы и спуски. Концы троп у ущелья соединяют мосты.
        /// </summary>
        void CarveMountains(Rng R)
        {
            var Mt = Mtn;
            int V = Res + 1, n = V * V;
            var best = new float[n];
            for (int i = 0; i < n; i++) best[i] = 1e9f;
            var bestH = new float[n];
            var bestFord = new bool[n];
            PathMask = new float[n];
            float w = 6f, R0 = w / 2 + 3.5f;
            var raw = new Dictionary<MountainPath, float[]>();
            foreach (var p in Mt.Paths)
            {
                var pts = p.Pts = Spline.Catmull(p.Ctrl, 1);
                var prof = new float[pts.Count];
                for (int i = 0; i < pts.Count; i++) prof[i] = HeightAt(pts[i].x, pts[i].z);
                // к броду тропа спускается до самой воды: дно реки там — по колено
                if (p.Ford) for (int i = 0; i < pts.Count; i++) prof[i] = MathF.Max(prof[i], -0.55f);
                if (p.Ford) prof[prof.Length - 1] = -0.55f;
                else
                { // площадка перед мостом: у края ущелья рельеф уже опущен к реке — держим высоту террасы за ним, настил ляжет вровень
                    int k0 = pts.Count - 1;
                    while (k0 > 0 && M.PolyDist(pts[k0].x, pts[k0].z, Mt.GorgePts) < Mt.GorgeW / 2 + 4.5f) k0--;
                    for (int i = k0 + 1; i < pts.Count; i++) prof[i] = prof[k0];
                }
                raw[p] = prof;
            }
            // мост не круче 1:4 — иначе концы площадок подрезаем и подсыпаем навстречу друг другу
            foreach (var br in Mt.Bridges)
            {
                if (br.Paths.Count != 2) continue;
                float[] fa = raw[br.Paths[0]], fb = raw[br.Paths[1]];
                V2 ea = br.Paths[0].Pts[br.Paths[0].Pts.Count - 1], eb = br.Paths[1].Pts[br.Paths[1].Pts.Count - 1];
                float ha = fa[fa.Length - 1], hb = fb[fb.Length - 1], lim = M.Hypot(eb.x - ea.x, eb.z - ea.z) * 0.25f, mid = (ha + hb) / 2;
                if (MathF.Abs(ha - hb) <= lim) continue;
                float na = mid + M.Sign(ha - hb) * lim / 2, nb = mid - M.Sign(ha - hb) * lim / 2;
                for (int i = 0; i < fa.Length; i++) if (fa[i] == ha) fa[i] = na;
                for (int i = 0; i < fb.Length; i++) if (fb[i] == hb) fb[i] = nb;
            }
            foreach (var p in Mt.Paths)
            {
                var pts = p.Pts;
                var prof = raw[p];
                // у моста тропа шире: отряд собирается перед настилом, не толкаясь на краю обрыва
                var wide = new float[pts.Count];
                if (!p.Ford)
                {
                    float acc = 0;
                    for (int i = pts.Count - 1; i >= 0 && acc < 14; i--)
                    {
                        wide[i] = 1.5f * M.Smooth(14, 9, acc);
                        if (i > 0) acc += M.Hypot(pts[i].x - pts[i - 1].x, pts[i].z - pts[i - 1].z);
                    }
                }
                for (int it = 0; it < 4; it++)
                {
                    for (int i = 1; i < pts.Count; i++) { float d = M.Hypot(pts[i].x - pts[i - 1].x, pts[i].z - pts[i - 1].z) * 0.3f; prof[i] = M.Clamp(prof[i], prof[i - 1] - d, prof[i - 1] + d); }
                    for (int i = pts.Count - 2; i >= 0; i--) { float d = M.Hypot(pts[i].x - pts[i + 1].x, pts[i].z - pts[i + 1].z) * 0.3f; prof[i] = M.Clamp(prof[i], prof[i + 1] - d, prof[i + 1] + d); }
                }
                var sm = new float[prof.Length];
                for (int i = 0; i < prof.Length; i++)
                {
                    float a = 0; int c = 0;
                    for (int k = -3; k <= 3; k++) { int j = i + k; if (j >= 0 && j < prof.Length) { a += prof[j]; c++; } }
                    sm[i] = a / c;
                }
                p.Prof = sm;
                for (int i = 0; i < pts.Count; i++)
                {
                    float px = pts[i].x, pz = pts[i].z, r = R0 + wide[i];
                    int ix0 = Math.Max(0, M.Floor((px - r + Half) / Cell)), ix1 = Math.Min(Res, (int)MathF.Ceiling((px + r + Half) / Cell));
                    int iz0 = Math.Max(0, M.Floor((pz - r + Half) / Cell)), iz1 = Math.Min(Res, (int)MathF.Ceiling((pz + r + Half) / Cell));
                    for (int iz = iz0; iz <= iz1; iz++)
                        for (int ix = ix0; ix <= ix1; ix++)
                        {
                            int k = iz * V + ix;
                            float d = M.Hypot(ix * Cell - Half - px, iz * Cell - Half - pz) - wide[i];
                            if (d < best[k]) { best[k] = d; bestH[k] = sm[i]; bestFord[k] = p.Ford; }
                        }
                }
            }
            for (int k = 0; k < n; k++)
            {
                if (best[k] >= R0) continue;
                // само ущелье не засыпаем: под мостом — река, а не брод
                float cx = (k % V) * Cell - Half, cz = (k / V) * Cell - Half;
                if (!bestFord[k] && M.PolyDist(cx, cz, Mt.GorgePts) < Mt.GorgeW / 2 + 0.5f) continue;
                H[k] = M.Lerp(H[k], bestH[k], M.Smooth(R0, w / 2, best[k]));
                PathMask[k] = M.Smooth(w / 2 + 0.8f, w / 2 - 0.6f, best[k]);
            }
            // Мосты через ущелье: настил от конца одной тропы до конца другой
            Bridges = new List<Deck>();
            // брод через всё ущелье: дно поднимаем полосой поперёк реки
            foreach (var f in Mt.Fords)
            {
                float hw = Mt.GorgeW / 2 + 3, fw = 4.5f;
                int ix0 = Math.Max(0, M.Floor((f.x - fw - 4 + Half) / Cell)), ix1 = Math.Min(Res, (int)MathF.Ceiling((f.x + fw + 4 + Half) / Cell));
                int iz0 = Math.Max(0, M.Floor((f.z - hw - 4 + Half) / Cell)), iz1 = Math.Min(Res, (int)MathF.Ceiling((f.z + hw + 4 + Half) / Cell));
                for (int iz = iz0; iz <= iz1; iz++)
                    for (int ix = ix0; ix <= ix1; ix++)
                    {
                        float x = ix * Cell - Half, z = iz * Cell - Half;
                        float ax = MathF.Abs(x - f.x), az = MathF.Abs(z - Mt.Gz(x));
                        if (az > hw) continue;
                        int k = iz * V + ix;
                        float kx = M.Smooth(fw + 3, fw, ax);
                        if (kx <= 0) continue;
                        H[k] = MathF.Max(H[k], M.Lerp(H[k], -0.55f, kx));
                        PathMask[k] = MathF.Max(PathMask[k], kx * 0.8f);
                    }
            }
            foreach (var br in Mt.Bridges)
            {
                var pa = br.Paths[0].Side < 0 ? br.Paths[0] : br.Paths[1];
                var pb = br.Paths[0].Side < 0 ? br.Paths[1] : br.Paths[0];
                V2 a = pa.Pts[pa.Pts.Count - 1], b = pb.Pts[pb.Pts.Count - 1];
                float len = M.Hypot(b.x - a.x, b.z - a.z), ux = (b.x - a.x) / len, uz = (b.z - a.z) / len;
                var d = Decks.Add(new Deck
                {
                    Kind = DeckKind.Bridge, Ax = a.x - ux * 1.5f, Az = a.z - uz * 1.5f, Bx = b.x + ux * 1.5f, Bz = b.z + uz * 1.5f, W = 6f,
                    HA = pa.Prof[pa.Prof.Length - 1] + 0.05f, HB = pb.Prof[pb.Prof.Length - 1] + 0.05f, Rel = false, Walk = true, Solid = false,
                });
                Bridges.Add(d);
            }
            // под настилом земля не выше него: иначе у бровки ущелья настил уходит под землю и выныривает ступенькой
            foreach (var d in Bridges)
            {
                float r = d.Len / 2 + d.W;
                int ix0 = Math.Max(0, M.Floor((d.X - r + Half) / Cell)), ix1 = Math.Min(Res, (int)MathF.Ceiling((d.X + r + Half) / Cell));
                int iz0 = Math.Max(0, M.Floor((d.Z - r + Half) / Cell)), iz1 = Math.Min(Res, (int)MathF.Ceiling((d.Z + r + Half) / Cell));
                for (int iz = iz0; iz <= iz1; iz++)
                    for (int ix = ix0; ix <= ix1; ix++)
                    {
                        if (!Decks.Locate(d, ix * Cell - Half, iz * Cell - Half, out float t, out _)) continue;
                        int k = iz * V + ix;
                        H[k] = MathF.Min(H[k], Decks.HeightOf(d, t, 0) - 0.04f);
                    }
            }
            // Сторожевые башни над переправами и стенки поперёк троп
            Watchtowers = new List<Watchtower>();
            foreach (var p in Mt.Paths)
            {
                V2 end = p.Pts[p.Pts.Count - 1];
                float hEnd = p.Prof[p.Prof.Length - 1];
                for (int tries = 0; tries < 30; tries++)
                {
                    float a = R.F() * M.PI * 2, r = M.Lerp(9, 20, R.F()), x = end.x + MathF.Cos(a) * r, z = end.z + MathF.Sin(a) * r * 0.6f - p.Side * 6;
                    if (M.Sign(z - Mt.Gz(x)) != p.Side || MathF.Abs(z - Mt.Gz(x)) < Mt.GorgeW / 2 + 5) continue;
                    float y = HeightAt(x, z);
                    if (MaskAt(x, z) > 0.05f || y < hEnd - 1 || SlopeAt(x, z) > 0.08f) continue;
                    Watchtowers.Add(new Watchtower { X = x, Z = z, Y = y });
                    break;
                }
                // стенка с проходом поперёк тропы — рубеж обороны у моста
                int i = Math.Max(0, p.Pts.Count - 9);
                V2 q = p.Pts[i], q2 = p.Pts[Math.Max(0, i - 2)];
                float dx = q.x - q2.x, dz = q.z - q2.z, l = M.Hypot(dx, dz);
                if (l == 0) l = 1;
                float nx = -dz / l, nz = dx / l;
                Features.Walls.Add(new FeatureWall { Ax = q.x + nx * 2.2f, Az = q.z + nz * 2.2f, Bx = q.x + nx * 7.5f, Bz = q.z + nz * 7.5f, H = 1.35f, T = 0.6f });
                Features.Walls.Add(new FeatureWall { Ax = q.x - nx * 2.2f, Az = q.z - nz * 2.2f, Bx = q.x - nx * 7.5f, Bz = q.z - nz * 7.5f, H = 1.35f, T = 0.6f });
            }
        }

        // ---------------------------------------------------------------- город: крепостные стены, башни, лестницы

        void AddFortress()
        {
            var F = Town.Fort;
            WallSpots = new List<(V2, V2)>();
            foreach (var w in F.Walls)
            {
                float len = M.Hypot(w.Bx - w.Ax, w.Bz - w.Az);
                if (len < 6) continue;
                float ux = (w.Bx - w.Ax) / len, uz = (w.Bz - w.Az) / len;
                for (float t = 4; t <= len - 4; t += 7)
                {
                    float x = w.Ax + ux * t + w.Out.x * w.W * 0.15f, z = w.Az + uz * t + w.Out.z * w.W * 0.15f;
                    if (F.Towers.Any(tw => MathF.Abs(tw.X - x) < tw.S / 2 + 1.5f && MathF.Abs(tw.Z - z) < tw.S / 2 + 1.5f)) continue;
                    WallSpots.Add((new V2(x, z), w.Out));
                }
            }
            foreach (var w in F.Walls)
            {
                float len = M.Hypot(w.Bx - w.Ax, w.Bz - w.Az);
                if (len == 0) len = 1;
                float ux = (w.Bx - w.Ax) / len, uz = (w.Bz - w.Az) / len;
                float outSign = M.Sign(-uz * w.Out.x + ux * w.Out.z);
                if (outSign == 0) outSign = 1;
                Decks.Add(new Deck { Kind = DeckKind.Wall, Ax = w.Ax, Az = w.Az, Bx = w.Bx, Bz = w.Bz, W = w.W, HA = w.H, HB = w.H, Rel = true, Walk = true, Solid = true, Parapet = 1.1f, OutSign = outSign });
            }
            foreach (var t in F.Towers) Decks.Add(new Deck { Kind = DeckKind.Tower, Ax = t.X - t.S / 2, Az = t.Z, Bx = t.X + t.S / 2, Bz = t.Z, W = t.S, HA = t.H, HB = t.H, Rel = true, Walk = true, Solid = true });
            foreach (var r in F.Ramps) Decks.Add(new Deck { Kind = DeckKind.Ramp, Ax = r.Ax, Az = r.Az, Bx = r.Bx, Bz = r.Bz, W = r.W, HA = 0.15f, HB = r.H, Rel = true, Walk = true, Solid = true });
            // Каменные мосты через реку и мост через ров к воротам замка
            var spans = new List<Seg>();
            if (Town.River != null) spans.AddRange(Town.River.Bridges);
            if (Town.CastleBridge != null) spans.Add(Town.CastleBridge);
            foreach (var b in spans)
            {
                var d = Decks.Add(new Deck { Kind = DeckKind.Bridge, Ax = b.Ax, Az = b.Az, Bx = b.Bx, Bz = b.Bz, W = b.W, HA = HeightAt(b.Ax, b.Az) + 0.08f, HB = HeightAt(b.Bx, b.Bz) + 0.08f, Rel = false, Walk = true, Solid = false, Stone = true });
                Bridges.Add(d);
            }
            // Всход с боевого хода северной стены на верх донжона
            var c = Town.Castle;
            if (c?.KeepRamp != null)
            {
                var kr = c.KeepRamp;
                Decks.Add(new Deck { Kind = DeckKind.Ramp, Ax = kr.Ax, Az = kr.Az, Bx = kr.Bx, Bz = kr.Bz, W = kr.W, HA = HeightAt(kr.Ax, kr.Az) + c.WallH, HB = HeightAt(c.Keep.x, c.Keep.z) + c.KeepH, Rel = false, Walk = true, Solid = true });
            }
        }

        // ---------------------------------------------------------------- высоты

        float RawHeight(double x, double z)
        {
            var n = Pn;
            var F = Features;
            double FIELD = Field;
            double dist = Math.Max(Math.Abs(x), Math.Abs(z)) * 0.6 + Math.Sqrt(x * x + z * z) * 0.4;
            double hills = n.Fbm(x * 0.006 + ohx, z * 0.006 + ohz, 4);
            double detail = n.Fbm(x * 0.05 + ohx + 31.7, z * 0.05 + ohz - 11.3, 2);

            double field;
            if (Type == MapType.Swamp)
            {
                field = 0.35 + hills * 1.4 + detail * 0.35;
                double pool = n.N01(x * 0.028 + olx, z * 0.028 + olz);
                field -= Math.Max(0, pool - 0.42) * 7;
                field += M.Smooth(FIELD * 0.42, FIELD * 0.72, Math.Abs(z)) * 1.9; // у армий суше
            }
            else if (Type == MapType.City)
            {
                // Город уступами: посад, верхний город, скала замка; подъёмы-улицы и ров (река — ниже, после сглаживания)
                field = 2.2 + hills * 0.35 + detail * 0.08;
                if (Town != null) field += Town.Lift((float)x, (float)z);
            }
            else if (Type == MapType.Mountains)
            {
                // Долины у армий, между ними — массив террасами с обрывами, посередине ущелье с рекой
                var Mt = Mtn;
                double zz = Math.Abs(z), m = M.Smooth(FIELD * 0.62, FIELD * 0.4, zz);
                field = 3 + hills * 2.5 + detail * 0.4;
                if (m > 0)
                {
                    double mh = 8 + n.Ridged(x * 0.017 + omx, z * 0.017 + omz, 4) * 13 + hills * 3;
                    double q = mh / 5, fl = Math.Floor(q), fr = q - fl;
                    double soft = M.Smooth(0.42, 0.66, n.N01(x * 0.03 + olx, z * 0.03 + olz)); // где-то уступ пологий — естественный подъём
                    mh = (fl + M.Smooth(M.Lerp(0.74, 0.25, soft), M.Lerp(0.92, 0.99, soft), fr)) * 5;
                    field = M.Lerp(field, mh + detail * 0.5, m);
                }
                double dg = M.PolyDist((float)x, (float)z, Mt.GorgePts), hw = Mt.GorgeW / 2.0;
                if (dg < hw + 3) field = M.Lerp(field, -3.5, M.Smooth(hw + 3, hw - 1, dg));
            }
            else field = 2.6 + hills * 3.2 + detail * 0.35;

            if (dist < FIELD + 30)
            {
                foreach (var hl in F.Hills)
                {
                    double d2 = ((x - hl.X) * (x - hl.X) + (z - hl.Z) * (z - hl.Z)) / (hl.R * (double)hl.R);
                    if (d2 < 9) field += hl.H * Math.Exp(-d2);
                }
                foreach (var r in F.Ridges)
                {
                    double d = M.SegDist((float)x, (float)z, r.Ax, r.Az, r.Bx, r.Bz);
                    if (d < r.R * 3) field += r.H * Math.Exp(-((d / r.R) * (d / r.R)));
                }
                if (F.Ravine != null)
                {
                    var rv = F.Ravine;
                    double d = M.PolyDist((float)x, (float)z, rv.Pts);
                    if (d < rv.W) field -= rv.D * M.Smooth(rv.W, rv.W * 0.3, d);
                }
            }
            if (Type == MapType.Swamp || Type == MapType.Mountains) field = Math.Max(field, -3.5);
            else if (field < 1.2) field = 1.2 - (1.2 - field) * 0.3;
            if (Town?.River != null)
            { // река через посад: крутые набережные, у стены — брод
                var rv = Town.River;
                double d = M.PolyDist((float)x, (float)z, rv.Pts), hw = rv.W / 2;
                if (d < hw + 1.4)
                {
                    double bed = rv.Bed, fd = M.Hypot((float)x - rv.Ford.x, (float)z - rv.Ford.z);
                    if (fd < 11) bed = M.Lerp(-0.5, bed, M.Smooth(7, 11, fd)); // широкий брод перед проломом
                    field = M.Lerp(field, bed, M.Smooth(hw + 1.4, hw - 0.2, d));
                }
            }

            // Окрестности: холмы крупнее, озёра, горы по краям
            double outer = 3 + hills * 16 + detail * 0.9;
            double lake = n.N01(x * 0.011 + olx, z * 0.011 + olz);
            outer -= Math.Max(0, lake - 0.58) * 70 * M.Smooth(FIELD + 25, FIELD + 55, dist);
            double floor = M.Lerp(1.0, -30, M.Smooth(FIELD + 20, FIELD + 45, dist));
            if (outer < floor) outer = floor;
            double mt = M.Smooth(FIELD + 35, FIELD + 135, dist);
            outer += mt * mt * (30 + n.Ridged(x * 0.009 + omx, z * 0.009 + omz, 4) * 60);

            return (float)M.Lerp(field, outer, M.Smooth(FIELD - 5, FIELD + 40, dist));
        }

        public float Hv(int ix, int iz) => H[iz * (Res + 1) + ix];

        public float HeightAt(float x, float z)
        {
            float gx = M.Clamp((x + Half) / Cell, 0, Res - 0.0001f), gz = M.Clamp((z + Half) / Cell, 0, Res - 0.0001f);
            int ix = (int)gx, iz = (int)gz;
            float fx = gx - ix, fz = gz - iz;
            float h00 = Hv(ix, iz), h10 = Hv(ix + 1, iz), h01 = Hv(ix, iz + 1), h11 = Hv(ix + 1, iz + 1);
            if (fz >= fx) return h00 + (h11 - h01) * fx + (h01 - h00) * fz;
            return h00 + (h10 - h00) * fx + (h11 - h10) * fz;
        }

        public float SlopeAt(float x, float z)
        {
            const float e = 1;
            float dx = HeightAt(x - e, z) - HeightAt(x + e, z), dz = HeightAt(x, z - e) - HeightAt(x, z + e);
            return 1 - 2 * e / M.Hypot(dx, 2 * e, dz); // 0 = ровно, 1 = отвесно
        }

        /// <summary>Пересечение луча с рельефом: шагаем по лучу, затем уточняем делением пополам.</summary>
        public bool Raycast(V3 o, V3 d, out V3 hit)
        {
            float prev = 0;
            for (float t = 0; t < 1400; t += 1.5f)
            {
                V3 p = o + d * t;
                if (MathF.Abs(p.x) > Half || MathF.Abs(p.z) > Half) { prev = t; continue; }
                if (p.y <= HeightAt(p.x, p.z))
                {
                    float a = prev, b = t;
                    for (int i = 0; i < 14; i++) { float m = (a + b) / 2; V3 q = o + d * m; if (q.y <= HeightAt(q.x, q.z)) b = m; else a = m; }
                    hit = o + d * b;
                    hit.y = HeightAt(hit.x, hit.z);
                    return true;
                }
                prev = t;
            }
            hit = default;
            return false;
        }

        /// <summary>Мощёные улицы и площадь города: 0 — трава, 1 — камень.</summary>
        public float Paving(float x, float z)
        {
            var T = Town;
            if (T == null || MathF.Abs(x) > T.TX + 12 || MathF.Abs(z) > T.TZ + 12) return 0;
            foreach (var sq in T.Squares) if (MathF.Abs(x - sq.X) < sq.Hx && MathF.Abs(z - sq.Z) < sq.Hz) return 1;
            float best = float.PositiveInfinity;
            foreach (var s in T.Streets) best = MathF.Min(best, M.SegDist(x, z, s.Ax, s.Az, s.Bx, s.Bz) - s.W / 2);
            return M.Smooth(0.8f, -0.4f, best);
        }

        // ---------------------------------------------------------------- краски земли

        public const int TexSize = 512;

        /// <summary>Цвет земли (RGBA, строка = z). Значения линейные — Unity кладёт их в sRGB-текстуру, как веб-версия.</summary>
        public byte[] PaintTerrain(Style s, int tex = TexSize)
        {
            var data = new byte[tex * tex * 4];
            Rgb mud = Rgb.Hex(0x4f4a2c), cobble = Rgb.Hex(0x8b8274), floorC = Rgb.Hex(0x3f5328);
            for (int ty = 0; ty < tex; ty++)
                for (int tx = 0; tx < tex; tx++)
                {
                    float x = (tx + 0.5f) / tex * Size - Half, z = (ty + 0.5f) / tex * Size - Half;
                    float y = HeightAt(x, z), slope = SlopeAt(x, z);
                    float n1 = (float)Pn.N01(x * 0.035 + ocx, z * 0.035 + ocz);
                    float n2 = (float)Pn.N01(x * 0.16 + ocz, z * 0.16 + ocx);
                    float n3 = (float)Pn.N01(x * 0.012 + ocx * 0.5, z * 0.012 + ocz * 0.5);

                    Rgb c = s.GrassA.Lerp(s.GrassB, n1);
                    c = c.Lerp(s.Dry, M.Smooth(0.55f, 0.8f, n3) * 0.75f);
                    c = c.Lerp(s.Dirt, M.Smooth(0.72f, 0.85f, n2 * 0.6f + n1 * 0.4f) * 0.6f);
                    if (Type == MapType.Forest) c = c.Lerp(floorC, ForestAt(x, z) * 0.7f);
                    float shore = y - Water;
                    if (Type == MapType.Swamp)
                    {
                        c = c.Lerp(mud, M.Smooth(0.9f, 0.1f, shore) * 0.8f);
                        if (shore < 0) c = mud.Lerp(s.Underwater, M.Smooth(0, -1.5f, shore));
                    }
                    else
                    {
                        c = s.Sand.Lerp(c, M.Smooth(0.2f, 1.4f, shore + (n2 - 0.5f) * 0.6f));
                        if (shore < 0) c = s.Sand.Lerp(s.Underwater, M.Smooth(0, -5, shore));
                    }
                    c = c.Lerp(s.Rock, M.Smooth(0.07f, 0.16f, slope + (n2 - 0.5f) * 0.04f));
                    c = c.Lerp(s.Rock, M.Smooth(32, 48, y + (n1 - 0.5f) * 12));
                    c = c.Lerp(s.Snow, M.Smooth(s.SnowLine, s.SnowLine + 6, y + (n2 - 0.5f) * 10) * (1 - M.Smooth(0.2f, 0.3f, slope)));
                    if (Town != null) c = c.Lerp(cobble, Paving(x, z) * (0.75f + n2 * 0.2f));
                    if (PathMask != null) c = c.Lerp(s.Dirt, MaskAt(x, z) * 0.85f);
                    float k = 0.9f + n2 * 0.18f;
                    int i = (ty * tex + tx) * 4;
                    data[i] = (byte)M.Clamp(c.r * k * 255, 0, 255);
                    data[i + 1] = (byte)M.Clamp(c.g * k * 255, 0, 255);
                    data[i + 2] = (byte)M.Clamp(c.b * k * 255, 0, 255);
                    data[i + 3] = 255;
                }
            return data;
        }

        // ---------------------------------------------------------------- деревья, камни, трава, постройки

        V3? FlatSpot(float xMin, float xMax, float zMin, float zMax, float radius, int tries)
        {
            V3? best = null;
            float bestScore = float.PositiveInfinity;
            for (int i = 0; i < tries; i++)
            {
                float x = M.Lerp(xMin, xMax, Rand.F()), z = M.Lerp(zMin, zMax, Rand.F());
                float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
                for (int a = 0; a < 8; a++)
                {
                    float hx = HeightAt(x + MathF.Cos(a * M.PI / 4) * radius, z + MathF.Sin(a * M.PI / 4) * radius);
                    lo = MathF.Min(lo, hx); hi = MathF.Max(hi, hx);
                }
                if (lo < Water + 0.8f) continue;
                float score = hi - lo;
                if (score < bestScore) { bestScore = score; best = new V3(x, lo, z); }
            }
            return best;
        }

        /// <summary>Раскладка декора: деревья, камни, трава, цветы, камыш и постройки за полем боя.</summary>
        void PlanDecor(Style s, bool lowEnd)
        {
            var r = Rand;
            var T = Type;
            TreeLists = new List<Placement>[TreeKinds];
            for (int i = 0; i < TreeKinds; i++) TreeLists[i] = new List<Placement>();
            RockLists = new List<Placement>[RockKinds];
            for (int i = 0; i < RockKinds; i++) RockLists[i] = new List<Placement>();
            Grass = new List<Tuft>(); Reeds = new List<Tuft>(); FlowerList = new List<Tuft>(); Landmarks = new List<Landmark>();

            // Деревья рощами вокруг поля боя
            int tries = (int)Math.Floor(2400 * s.TreeDensity * (T == MapType.Forest ? 1.4f : T == MapType.Mountains ? 0.7f : 1) * (Big ? 1.4f : 1));
            for (int i = 0; i < tries; i++)
            {
                float x = M.Lerp(-Half + 6, Half - 6, r.F()), z = M.Lerp(-Half + 6, Half - 6, r.F());
                if (MathF.Abs(x) < Field + 7 && MathF.Abs(z) < Field + 7) continue;
                float y = HeightAt(x, z);
                if (y < Water + 0.8f || y > s.SnowLine + 4 || SlopeAt(x, z) > 0.12f) continue;
                double forest = Pn.N01(x * 0.013 + omx, z * 0.013 + omz);
                if (forest < 0.45 && r.Next() > 0.06) continue;
                int kind = (int)Math.Floor(r.Next() * TreeKinds);
                float rot = r.F() * M.PI * 2, k = M.Lerp(0.8f, 1.3f, r.F());
                TreeLists[kind].Add(new Placement { Kind = kind, X = x, Y = y - 0.2f, Z = z, Rot = rot, Scale = k });
            }
            // Лес на самом поле боя
            foreach (var t in Trees) TreeLists[t.Kind].Add(new Placement { Kind = t.Kind, X = t.X, Y = HeightAt(t.X, t.Z) - 0.2f, Z = t.Z, Rot = t.Rot, Scale = t.K });
            // Горы: редкие сосны на пологих склонах
            if (T == MapType.Mountains)
                for (int i = 0; i < Field * 3; i++)
                {
                    float x = M.Lerp(-Field, Field, r.F()), z = M.Lerp(-Field, Field, r.F());
                    if (SlopeAt(x, z) > 0.1f || Nav.SpeedAt(x, z, 0) == 0 || MaskAt(x, z) > 0.2f) continue; // на тропе деревья не растут
                    int kind = (int)(r.Next() * 2);
                    float rot = r.F() * M.PI * 2, k = M.Lerp(0.8f, 1.2f, r.F());
                    TreeLists[kind].Add(new Placement { Kind = kind, X = x, Y = HeightAt(x, z) - 0.2f, Z = z, Rot = rot, Scale = k });
                    Obs.AddCircle(x, z, 0.45f, 7, HeightAt(x, z));
                }

            // Камни: крупные снаружи, мелкие на поле (в горах — побольше)
            int rocks = (int)((T == MapType.Mountains ? 420 : 260) * (Big ? 1.5f : 1));
            for (int i = 0; i < rocks; i++)
            {
                float x = M.Lerp(-Half + 5, Half - 5, r.F()), z = M.Lerp(-Half + 5, Half - 5, r.F());
                bool inField = MathF.Abs(x) < Field + 4 && MathF.Abs(z) < Field + 4;
                if (inField && (r.Next() > (T == MapType.Mountains ? 0.6 : 0.3) || Paving(x, z) > 0 || Obs.Hit(x, z, 1) != null
                    || (PathMask != null && MaskAt(x, z) > 0.05f) || Decks.Surface(x, z, 0) > float.NegativeInfinity)) continue;
                float y = HeightAt(x, z);
                if (y < Water - 1.5f || y > 40) continue;
                float size = inField ? M.Lerp(0.5f, T == MapType.Mountains ? 2.5f : 1.1f, r.F()) : M.Lerp(1.2f, 4, r.F());
                int kind = (int)Math.Floor(r.Next() * RockKinds);
                float rot = r.F() * M.PI * 2;
                RockLists[kind].Add(new Placement { Kind = kind, X = x, Y = y - size * 0.3f, Z = z, Rot = rot, Scale = size });
                // валун по колено и выше — препятствие: сквозь него не проходят, обтекают (мелкие камни перешагивают)
                if (inField && size >= 0.8f) Obs.AddCircle(x, z, size * 0.42f, size * 0.7f, y);
            }

            // Трава и цветы (на болоте — камыш)
            int tuftCount = (int)Math.Floor((lowEnd ? 3500 : 7000) * s.GrassDensity * (Big ? 1.6f : 1));
            int flowerCount = (int)Math.Floor(tuftCount * s.Flowers);
            Rgb reedA = Rgb.Hex(0x8a8f45), reedB = Rgb.Hex(0xa99a55);
            int gi = 0, fi = 0;
            for (int tries2 = 0; gi < tuftCount && tries2 < tuftCount * 3; tries2++)
            {
                float x = M.Lerp(-Field - 30, Field + 30, r.F()), z = M.Lerp(-Field - 30, Field + 30, r.F());
                float y = HeightAt(x, z);
                bool reed = ReedsAt(x, z);
                if (!reed && (y < Water + 0.4f || Pn.N01(x * 0.05 + olx, z * 0.05 + olz) < 0.33)) continue;
                if (Town != null && (Paving(x, z) > 0.2f || Obs.Hit(x, z, 0.3f) != null)) continue;
                float rot = r.F() * M.PI * 2, k = M.Lerp(0.7f, 1.4f, r.F());
                Tuft tf;
                if (reed) tf = new Tuft { X = x, Y = MathF.Max(y, Water - 0.2f) - 0.05f, Z = z, Rot = rot, Sx = k * 1.1f, Sy = k * M.Lerp(3, 4.5f, r.F()), Sz = k * 1.1f };
                else tf = new Tuft { X = x, Y = y - 0.03f, Z = z, Rot = rot, Sx = k, Sy = k * M.Lerp(0.8f, 1.3f, r.F()), Sz = k };
                tf.Color = reed ? reedA.Lerp(reedB, r.F()) : s.Grass1.Lerp(s.Grass2, r.F());
                (reed ? Reeds : Grass).Add(tf);
                gi++;
                if (!reed && fi < flowerCount && r.Next() < s.Flowers * 1.2f)
                {
                    FlowerList.Add(new Tuft { X = x + 0.1f, Y = y + 0.33f * k, Z = z, Rot = rot, Sx = 1, Sy = 1, Sz = 1, Color = s.Flower[(int)Math.Floor(r.Next() * 3)] });
                    fi++;
                }
            }

            foreach (var t in Watchtowers) Landmarks.Add(new Landmark { Model = "tower", X = t.X, Y = t.Y + 0.6f, Z = t.Z, Rot = r.F() * M.PI * 2, Height = 11 });

            // Замки армий за их спинами, мельница и башни на холмах
            float F = Field;
            var blue = FlatSpot(-35, 35, -(F + 70), -(F + 24), 9, 60);
            var red = FlatSpot(-35, 35, F + 24, F + 70, 9, 60);
            if (blue.HasValue) Landmarks.Add(new Landmark { Model = "castle_blue", X = blue.Value.x, Y = blue.Value.y, Z = blue.Value.z, Rot = 0, Height = 24 });
            if (red.HasValue) Landmarks.Add(new Landmark { Model = "castle_red", X = red.Value.x, Y = red.Value.y, Z = red.Value.z, Rot = M.PI, Height = 24 });
            for (int i = 0; i < 3; i++)
            {
                float side = r.Next() < 0.5 ? -1 : 1;
                var spot = FlatSpot(side * (F + 70), side * (F + 20), -(F + 40), F + 40, 5, 40);
                if (spot.HasValue) Landmarks.Add(new Landmark { Model = i == 0 ? "windmill" : "tower", X = spot.Value.x, Y = spot.Value.y, Z = spot.Value.z, Rot = r.F() * M.PI * 2, Height = i == 0 ? 15 : 13 });
            }
        }
    }
}
