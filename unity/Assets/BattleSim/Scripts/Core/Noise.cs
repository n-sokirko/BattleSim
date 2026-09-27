using System;

namespace BattleSim.Core
{
    /// <summary>Классический 2D-шум Перлина. Noise() ≈ -1..1, N01() ≈ 0..1. Считается в double — как в веб-версии.</summary>
    public sealed class Perlin
    {
        readonly byte[] p = new byte[512];

        public Perlin(Rng rand)
        {
            var q = new int[256];
            for (int i = 0; i < 256; i++) q[i] = i;
            for (int i = 255; i > 0; i--)
            {
                int j = (int)Math.Floor(rand.Next() * (i + 1));
                (q[i], q[j]) = (q[j], q[i]);
            }
            for (int i = 0; i < 512; i++) p[i] = (byte)q[i & 255];
        }

        static double Grad(int h, double x, double y)
        {
            switch (h & 7)
            {
                case 0: return x + y;
                case 1: return -x + y;
                case 2: return x - y;
                case 3: return -x - y;
                case 4: return x;
                case 5: return -x;
                case 6: return y;
                default: return -y;
            }
        }

        static double L(double a, double b, double t) => a + (b - a) * t;

        public double Noise(double x, double y)
        {
            double fx = Math.Floor(x), fy = Math.Floor(y);
            int X = (int)fx & 255, Y = (int)fy & 255;
            x -= fx; y -= fy;
            double u = x * x * x * (x * (x * 6 - 15) + 10), v = y * y * y * (y * (y * 6 - 15) + 10);
            int a = p[X] + Y, b = p[X + 1] + Y;
            return L(L(Grad(p[a], x, y), Grad(p[b], x - 1, y), u), L(Grad(p[a + 1], x, y - 1), Grad(p[b + 1], x - 1, y - 1), u), v) * 0.72;
        }

        public double N01(double x, double y) => Noise(x, y) * 0.5 + 0.5;

        public double Fbm(double x, double y, int oct)
        {
            double s = 0, amp = 1, f = 1, norm = 0;
            for (int i = 0; i < oct; i++) { s += Noise(x * f, y * f) * amp; norm += amp; amp *= 0.5; f *= 2.03; }
            return s / norm;
        }

        public double Ridged(double x, double y, int oct)
        {
            double s = 0, amp = 1, f = 1, norm = 0;
            for (int i = 0; i < oct; i++) { double n = 1 - Math.Abs(Noise(x * f, y * f)); s += n * n * amp; norm += amp; amp *= 0.5; f *= 2.1; }
            return s / norm;
        }
    }
}
