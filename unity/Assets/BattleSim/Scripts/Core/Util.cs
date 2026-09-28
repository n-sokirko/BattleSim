using System;
using System.Collections.Generic;

namespace BattleSim.Core
{
    /// <summary>Математика и мелкие помощники. Ядро игры не зависит от Unity: его можно гонять и тестировать без движка.</summary>
    public static class M
    {
        public const float PI = (float)Math.PI;
        public const float DEG = PI / 180f;

        public static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
        public static double Clamp(double v, double a, double b) => v < a ? a : v > b ? b : v;
        public static int Clamp(int v, int a, int b) => v < a ? a : v > b ? b : v;
        public static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static double Lerp(double a, double b, double t) => a + (b - a) * t;

        public static float Smooth(float a, float b, float x)
        {
            float t = Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        public static double Smooth(double a, double b, double x)
        {
            double t = Clamp((x - a) / (b - a), 0.0, 1.0);
            return t * t * (3.0 - 2.0 * t);
        }

        /// <summary>Значение ломаной, заданной равномерно по [0, 1] (профиль высоты вдоль стены), в доле t.</summary>
        public static float Sample(float[] p, float t)
        {
            float f = Clamp01(t) * (p.Length - 1);
            int i = Math.Min((int)f, p.Length - 2);
            return i < 0 ? p[0] : Lerp(p[i], p[i + 1], f - i);
        }

        public static float Hypot(float x, float z) => MathF.Sqrt(x * x + z * z);
        public static float Hypot(float x, float y, float z) => MathF.Sqrt(x * x + y * y + z * z);

        /// <summary>Как Math.sign в JS: для нуля — ноль.</summary>
        public static float Sign(float v) => v > 0f ? 1f : v < 0f ? -1f : 0f;

        public static float WrapAngle(float a) => MathF.Atan2(MathF.Sin(a), MathF.Cos(a));

        /// <summary>Расстояние от точки до отрезка в плоскости XZ.</summary>
        public static float SegDist(float x, float z, float ax, float az, float bx, float bz)
        {
            float vx = bx - ax, vz = bz - az, l2 = vx * vx + vz * vz;
            float t = l2 > 0f ? Clamp(((x - ax) * vx + (z - az) * vz) / l2, 0f, 1f) : 0f;
            return Hypot(x - ax - vx * t, z - az - vz * t);
        }

        /// <summary>Расстояние от точки до ломаной.</summary>
        public static float PolyDist(float x, float z, IList<V2> pts)
        {
            float best = float.PositiveInfinity;
            for (int i = 1; i < pts.Count; i++)
            {
                float d = SegDist(x, z, pts[i - 1].x, pts[i - 1].z, pts[i].x, pts[i].z);
                if (d < best) best = d;
            }
            return best;
        }

        public static V2 Norm2(float x, float z)
        {
            float l = Hypot(x, z);
            if (l == 0f) l = 1f;
            return new V2(x / l, z / l);
        }

        /// <summary>Как Math.round в JS: половинки округляются вверх.</summary>
        public static int Round(float v) => (int)MathF.Floor(v + 0.5f);
        public static int Round(double v) => (int)Math.Floor(v + 0.5);

        public static int Floor(float v) => (int)MathF.Floor(v);
        public static int Floor(double v) => (int)Math.Floor(v);
    }

    /// <summary>Точка на плоскости земли (x, z).</summary>
    public struct V2
    {
        public float x, z;
        public V2(float x, float z) { this.x = x; this.z = z; }
        public static float Dist(V2 a, V2 b) => M.Hypot(a.x - b.x, a.z - b.z);
        public override string ToString() => "(" + x.ToString("0.0") + ", " + z.ToString("0.0") + ")";
    }

    public struct V3
    {
        public float x, y, z;
        public V3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public V2 XZ => new V2(x, z);
        public static V3 operator +(V3 a, V3 b) => new V3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static V3 operator -(V3 a, V3 b) => new V3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static V3 operator *(V3 a, float k) => new V3(a.x * k, a.y * k, a.z * k);
        public float Length => M.Hypot(x, y, z);
        public V3 Normalized { get { float l = Length; return l > 1e-9f ? this * (1f / l) : new V3(0, 0, 0); } }
        public static V3 Lerp(V3 a, V3 b, float t) => new V3(M.Lerp(a.x, b.x, t), M.Lerp(a.y, b.y, t), M.Lerp(a.z, b.z, t));
    }

    /// <summary>Генератор mulberry32 — тот же, что в веб-версии: одинаковый сид даёт одинаковую карту.</summary>
    public sealed class Rng
    {
        uint a;
        public Rng(int seed) { a = (uint)seed; }
        public Rng(uint seed) { a = seed; }

        public double Next()
        {
            unchecked
            {
                a += 0x6d2b79f5u;
                uint t = (a ^ (a >> 15)) * (1u | a);
                t = (t + ((t ^ (t >> 7)) * (61u | t))) ^ t;
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }

        public float F() => (float)Next();
        public float Range(float a0, float b0) => M.Lerp(a0, b0, (float)Next());
        public int Int(int n) => (int)(Next() * n);
        public bool Chance(double p) => Next() < p;

        /// <summary>Общий генератор для случайностей боя (в вебе это Math.random).</summary>
        public static Rng Shared = new Rng(Environment.TickCount);
        public static float Rand() => (float)Shared.Next();
    }

    /// <summary>Сетка для быстрого поиска объектов рядом с точкой (дома, стволы, настилы, ограды).</summary>
    public sealed class SpatialGrid<T>
    {
        public readonly float Cell, Half;
        public readonly int Dim;
        readonly List<T>[] grid;
        public readonly List<T> All = new List<T>();
        static readonly List<T> Empty = new List<T>();

        public SpatialGrid(float field, float cell = 8f)
        {
            Cell = cell; Half = field + 12f;
            Dim = (int)Math.Ceiling(Half * 2f / Cell);
            grid = new List<T>[Dim * Dim];
        }

        public void Insert(T item, float x, float z, float r)
        {
            All.Add(item);
            int x0 = M.Clamp(M.Floor((x - r + Half) / Cell), 0, Dim - 1), x1 = M.Clamp(M.Floor((x + r + Half) / Cell), 0, Dim - 1);
            int z0 = M.Clamp(M.Floor((z - r + Half) / Cell), 0, Dim - 1), z1 = M.Clamp(M.Floor((z + r + Half) / Cell), 0, Dim - 1);
            for (int iz = z0; iz <= z1; iz++)
                for (int ix = x0; ix <= x1; ix++)
                {
                    int i = iz * Dim + ix;
                    (grid[i] ??= new List<T>()).Add(item);
                }
        }

        /// <summary>Объекты клетки, где лежит точка (null — точка вне сетки).</summary>
        public List<T> Near(float x, float z)
        {
            int ix = M.Floor((x + Half) / Cell), iz = M.Floor((z + Half) / Cell);
            if (ix < 0 || iz < 0 || ix >= Dim || iz >= Dim) return null;
            return grid[iz * Dim + ix] ?? Empty;
        }
    }
}
