using UnityEngine;

namespace BattleSim
{
    /// <summary>
    /// Маленькая текстура-палитра 8x8. Все юниты, деревья и камни используют один материал,
    /// а цвет каждой грани задаётся UV-координатой нужной клетки палитры.
    /// </summary>
    public static class Palette
    {
        public const int Cols = 8, Rows = 8;

        // Армии
        public const int BlueMain = 0, BlueDark = 1, BlueLight = 2, RedMain = 3, RedDark = 4, RedLight = 5;
        public const int Skin = 6, Hair = 7, Steel = 8, SteelDark = 9, Wood = 10, Leather = 11;
        public const int Gold = 12, Cream = 13, Black = 14, Bowstring = 15;
        public const int HorseBrown = 16, HorseDark = 17, HorseGrey = 18, HorseBlack = 19, Fletch = 20, SkinDark = 21;
        // Природа (зависит от биома)
        public const int Trunk = 24, Leaf1 = 25, Leaf2 = 26, Leaf3 = 27, Pine1 = 28, Pine2 = 29;
        public const int Rock1 = 30, Rock2 = 31, Rock3 = 32, Grass1 = 33, Grass2 = 34;
        public const int Flower1 = 35, Flower2 = 36, Flower3 = 37, SnowCap = 38, Bush = 39;

        public static readonly Color BlueTeam = new Color(0.20f, 0.42f, 0.85f);
        public static readonly Color RedTeam = new Color(0.82f, 0.20f, 0.18f);

        public static Vector2 UV(int i) => new Vector2((i % Cols + 0.5f) / Cols, (i / Cols + 0.5f) / Rows);

        public static int TeamMain(int team) => team == 0 ? BlueMain : RedMain;
        public static int TeamDark(int team) => team == 0 ? BlueDark : RedDark;
        public static int TeamLight(int team) => team == 0 ? BlueLight : RedLight;

        static readonly Color[] colors = new Color[Cols * Rows];

        static void Put(int i, Color c) => colors[i] = c;

        public static Texture2D Build(WorldStyle s)
        {
            for (int i = 0; i < colors.Length; i++) colors[i] = Color.magenta;

            Put(BlueMain, BlueTeam); Put(BlueDark, new Color(0.13f, 0.24f, 0.52f)); Put(BlueLight, new Color(0.55f, 0.72f, 0.98f));
            Put(RedMain, RedTeam); Put(RedDark, new Color(0.50f, 0.12f, 0.11f)); Put(RedLight, new Color(0.98f, 0.62f, 0.50f));
            Put(Skin, new Color(0.93f, 0.74f, 0.60f)); Put(SkinDark, new Color(0.62f, 0.44f, 0.32f)); Put(Hair, new Color(0.35f, 0.24f, 0.15f));
            Put(Steel, new Color(0.78f, 0.80f, 0.84f)); Put(SteelDark, new Color(0.45f, 0.47f, 0.52f));
            Put(Wood, new Color(0.55f, 0.38f, 0.22f)); Put(Leather, new Color(0.40f, 0.27f, 0.16f));
            Put(Gold, new Color(0.95f, 0.76f, 0.25f)); Put(Cream, new Color(0.93f, 0.90f, 0.80f));
            Put(Black, new Color(0.08f, 0.08f, 0.09f)); Put(Bowstring, new Color(0.90f, 0.88f, 0.80f));
            Put(HorseBrown, new Color(0.47f, 0.30f, 0.18f)); Put(HorseDark, new Color(0.18f, 0.12f, 0.08f));
            Put(HorseGrey, new Color(0.78f, 0.76f, 0.72f)); Put(HorseBlack, new Color(0.14f, 0.13f, 0.13f));
            Put(Fletch, new Color(0.95f, 0.95f, 0.95f));

            Put(Trunk, s.Trunk); Put(Leaf1, s.Leaf1); Put(Leaf2, s.Leaf2); Put(Leaf3, s.Leaf3);
            Put(Pine1, s.Pine1); Put(Pine2, s.Pine2); Put(Rock1, s.Rock1); Put(Rock2, s.Rock2); Put(Rock3, s.Rock3);
            Put(Grass1, s.Grass1); Put(Grass2, s.Grass2); Put(Flower1, s.Flower1); Put(Flower2, s.Flower2); Put(Flower3, s.Flower3);
            Put(SnowCap, s.Snow); Put(Bush, s.Bush);

            var tex = new Texture2D(Cols, Rows, TextureFormat.RGBA32, false)
            {
                name = "BattleSimPalette",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            for (int i = 0; i < colors.Length; i++) tex.SetPixel(i % Cols, i / Cols, colors[i]);
            tex.Apply(false, true);
            return tex;
        }
    }
}
