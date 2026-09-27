using System;

namespace BattleSim.Core
{
    /// <summary>
    /// Float-математика через System.Math: так ядро компилируется под любой профиль .NET
    /// (Unity, .NET Standard 2.0/2.1, обычный .NET для тестов) и ведёт себя одинаково.
    /// </summary>
    public static class MathF
    {
        public const float PI = (float)Math.PI;
        public static float Sqrt(float v) => (float)Math.Sqrt(v);
        public static float Sin(float v) => (float)Math.Sin(v);
        public static float Cos(float v) => (float)Math.Cos(v);
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float Abs(float v) => v < 0 ? -v : v;
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Floor(float v) => (float)Math.Floor(v);
        public static float Ceiling(float v) => (float)Math.Ceiling(v);
        public static float Pow(float a, float b) => (float)Math.Pow(a, b);
        public static float Exp(float v) => (float)Math.Exp(v);
        public static float Round(float v) => (float)Math.Round(v);
    }
}
