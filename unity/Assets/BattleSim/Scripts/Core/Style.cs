using System;

namespace BattleSim.Core
{
    /// <summary>Цвет в линейном пространстве (как THREE.Color в веб-версии: hex задаётся в sRGB и переводится в линейный).</summary>
    public struct Rgb
    {
        public float r, g, b;
        public Rgb(float r, float g, float b) { this.r = r; this.g = g; this.b = b; }

        static float ToLinear(float c) => c < 0.04045f ? c * 0.0773993808f : MathF.Pow(c * 0.9478672986f + 0.0521327014f, 2.4f);
        public static float ToSrgb(float c) => c < 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 0.41666f) - 0.055f;

        public static Rgb Hex(uint hex) => new Rgb(ToLinear(((hex >> 16) & 255) / 255f), ToLinear(((hex >> 8) & 255) / 255f), ToLinear((hex & 255) / 255f));

        public Rgb Lerp(Rgb o, float t) => new Rgb(r + (o.r - r) * t, g + (o.g - g) * t, b + (o.b - b) * t);
        public static Rgb operator *(Rgb c, float k) => new Rgb(c.r * k, c.g * k, c.b * k);

        /// <summary>Цвет в sRGB (для полей Unity, которые ждут «гамма»-цвет).</summary>
        public Rgb Srgb => new Rgb(ToSrgb(r), ToSrgb(g), ToSrgb(b));

        // HSL — как в THREE.Color (в линейном пространстве)
        public void ToHsl(out float h, out float s, out float l)
        {
            float max = MathF.Max(r, MathF.Max(g, b)), min = MathF.Min(r, MathF.Min(g, b));
            l = (min + max) / 2f;
            if (min == max) { h = 0; s = 0; return; }
            float d = max - min;
            s = l <= 0.5f ? d / (max + min) : d / (2f - max - min);
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h /= 6f;
        }

        static float Hue2Rgb(float p, float q, float t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1f / 6f) return p + (q - p) * 6 * t;
            if (t < 1f / 2f) return q;
            if (t < 2f / 3f) return p + (q - p) * 6 * (2f / 3f - t);
            return p;
        }

        public static Rgb FromHsl(float h, float s, float l)
        {
            h = ((h % 1f) + 1f) % 1f;
            s = M.Clamp01(s); l = M.Clamp01(l);
            if (s == 0) return new Rgb(l, l, l);
            float p = l <= 0.5f ? l * (1 + s) : l + s - l * s, q = 2 * l - p;
            return new Rgb(Hue2Rgb(q, p, h + 1f / 3f), Hue2Rgb(q, p, h), Hue2Rgb(q, p, h - 1f / 3f));
        }
    }

    public enum LeafShift { Summer, Autumn, Winter, Steppe }

    /// <summary>Биом и время суток: краски земли, небо, солнце и туман.</summary>
    public sealed class Style
    {
        public int Biome, Time;
        public string Title;
        public Rgb Rock = Rgb.Hex(0x7b7670), Snow = Rgb.Hex(0xf2f5fa), Sand = Rgb.Hex(0xd8c894), Dirt = Rgb.Hex(0x7a5d3f), Underwater = Rgb.Hex(0x4f6f5c);
        public Rgb Water = Rgb.Hex(0x2a6f86);
        public float WaterAlpha = 0.8f, SnowLine = 42f, TreeDensity = 1f, GrassDensity = 1f, Flowers = 0.15f;
        public bool SnowGround;
        public Rgb Grass1 = Rgb.Hex(0x5e9a2f), Grass2 = Rgb.Hex(0x86b440);
        public Rgb[] Flower = { Rgb.Hex(0xf2e14a), Rgb.Hex(0xf0f0f0), Rgb.Hex(0xd9534f) };
        public LeafShift Leaf = LeafShift.Summer;
        public Rgb GrassA, GrassB, Dry;
        // солнце и небо
        public float Elev, Azim, SunI, FogD, Turb, Rayleigh, Expo;
        public Rgb SunColor, Fog;

        public static Style Make(int biome, int time)
        {
            var s = new Style { Biome = biome, Time = time, Title = Defs.Biomes[biome] + ", " + Defs.Times[time] };
            if (biome == 0) { s.GrassA = Rgb.Hex(0x5f9a35); s.GrassB = Rgb.Hex(0x78ad3c); s.Dry = Rgb.Hex(0xa9a852); }
            if (biome == 1)
            {
                s.GrassA = Rgb.Hex(0x8a8a3a); s.GrassB = Rgb.Hex(0x9c8c3c); s.Dry = Rgb.Hex(0xb8904a); s.Grass1 = Rgb.Hex(0x8f8a3a); s.Grass2 = Rgb.Hex(0xb09a45);
                s.Flower = new[] { Rgb.Hex(0xe8b030), Rgb.Hex(0xc8602a), Rgb.Hex(0xa03a28) }; s.Flowers = 0.05f; s.Leaf = LeafShift.Autumn; s.Water = Rgb.Hex(0x255f70);
            }
            if (biome == 2)
            {
                s.GrassA = Rgb.Hex(0xe6ecf4); s.GrassB = Rgb.Hex(0xd7e0ec); s.Dry = Rgb.Hex(0x9aa08a); s.SnowGround = true; s.SnowLine = 18;
                s.Grass1 = Rgb.Hex(0x9aa37a); s.Grass2 = Rgb.Hex(0xb8bfa0); s.GrassDensity = 0.25f; s.Flowers = 0; s.Leaf = LeafShift.Winter;
                s.Underwater = Rgb.Hex(0x6a8a94); s.Sand = Rgb.Hex(0xc8ccc8); s.Water = Rgb.Hex(0x4b7c92);
            }
            if (biome == 3)
            {
                s.GrassA = Rgb.Hex(0xb4a55a); s.GrassB = Rgb.Hex(0xc2b066); s.Dry = Rgb.Hex(0xcfae6a); s.Dirt = Rgb.Hex(0x9a6a45); s.Rock = Rgb.Hex(0xa47e62);
                s.Grass1 = Rgb.Hex(0xb8a458); s.Grass2 = Rgb.Hex(0xd0bc6c); s.Flower = new[] { Rgb.Hex(0xe86a3a), Rgb.Hex(0xf0d060), Rgb.Hex(0xb06ac0) };
                s.Flowers = 0.08f; s.TreeDensity = 0.3f; s.GrassDensity = 1.2f; s.SnowLine = 60; s.Leaf = LeafShift.Steppe; s.Water = Rgb.Hex(0x2b6e74);
            }
            if (time == 0) { s.Elev = 52; s.Azim = -35; s.SunColor = Rgb.Hex(0xfff1dc); s.SunI = 2.6f; s.Fog = Rgb.Hex(0xb7c8d8); s.FogD = 0.0031f; s.Turb = 5; s.Rayleigh = 1.4f; s.Expo = 0.62f; }
            if (time == 1) { s.Elev = 9; s.Azim = -65; s.SunColor = Rgb.Hex(0xffb070); s.SunI = 2.9f; s.Fog = Rgb.Hex(0xd9a78a); s.FogD = 0.0036f; s.Turb = 9; s.Rayleigh = 3.2f; s.Expo = 0.58f; }
            if (time == 2) { s.Elev = 20; s.Azim = 115; s.SunColor = Rgb.Hex(0xffe2bf); s.SunI = 2.1f; s.Fog = Rgb.Hex(0xc3cad3); s.FogD = 0.0074f; s.Turb = 12; s.Rayleigh = 2.2f; s.Expo = 0.62f; }
            if (s.SnowGround) { s.Fog = s.Fog.Lerp(Rgb.Hex(0xd6dfe9), 0.5f); s.SunI *= 0.9f; }
            return s;
        }

        /// <summary>Под местность подстраиваем погоду и краски: над болотом туман, на грядах снег.</summary>
        public void Tweak(MapType type)
        {
            if (type == MapType.Swamp)
            {
                FogD *= 1.7f; WaterAlpha = 0.88f;
                Water = Rgb.Hex(0x3d4a2c); Underwater = Rgb.Hex(0x3c3f26);
                GrassA = GrassA.Lerp(Rgb.Hex(0x6b7a3a), 0.5f); GrassB = GrassB.Lerp(Rgb.Hex(0x7d8a40), 0.5f);
            }
            if (type == MapType.Mountains) SnowLine = MathF.Min(SnowLine, 19);
            if (type == MapType.Forest) TreeDensity *= 1.2f;
        }

        /// <summary>Направление на солнце (как setFromSphericalCoords в three.js).</summary>
        public V3 SunDir
        {
            get
            {
                float phi = (90 - Elev) * M.DEG, theta = Azim * M.DEG;
                float sp = MathF.Sin(phi);
                return new V3(sp * MathF.Sin(theta), MathF.Cos(phi), sp * MathF.Cos(theta));
            }
        }
    }
}
