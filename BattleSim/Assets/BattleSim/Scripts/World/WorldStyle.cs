using UnityEngine;

namespace BattleSim
{
    /// <summary>Биом (цвета земли и растений) + время суток (свет, туман, небо).</summary>
    public class WorldStyle
    {
        public static readonly string[] BiomeNames = { "Лето", "Осень", "Зима", "Степь" };
        public static readonly string[] TimeNames = { "День", "Закат", "Туманное утро" };

        public int Biome, Time;
        public string Title => BiomeNames[Biome] + ", " + TimeNames[Time].ToLowerInvariant();

        // Земля
        public Color GrassA, GrassB, Dry, Dirt, Sand, Rock, Snow, Underwater;
        public float SnowLine = 42f;
        public bool SnowGround;

        // Растения и камни (идут в палитру)
        public Color Trunk, Leaf1, Leaf2, Leaf3, Pine1, Pine2, Rock1, Rock2, Rock3, Grass1, Grass2, Flower1, Flower2, Flower3, Bush;
        public float TreeDensity = 1f, PineShare = 0.4f, GrassDensity = 1f, FlowerShare = 0.15f;
        public bool BareTrees;

        public Color Water;

        // Свет
        public Vector3 SunEuler;
        public Color SunColor;
        public float SunIntensity;
        public Color AmbSky, AmbEquator, AmbGround, Fog;
        public float FogDensity;
        public Color SkyTint, SkyGround;
        public float Atmosphere, Exposure;

        static Color C(float r, float g, float b) => new Color(r, g, b);
        static Color C(int hex) => new Color(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f);

        public static WorldStyle Create(int biome, int time)
        {
            var s = new WorldStyle { Biome = biome, Time = time };
            s.ApplyBiome();
            s.ApplyTime();
            return s;
        }

        void ApplyBiome()
        {
            // Общие значения
            Rock = C(0x7b7670); Snow = C(0xf4f6fb); Sand = C(0xd8c894); Dirt = C(0x7a5d3f);
            Underwater = C(0x4f6f5c); Trunk = C(0x6b4a2f);
            Rock1 = C(0x8a8680); Rock2 = C(0x6d6a66); Rock3 = C(0xa09a90);
            Water = new Color(0.13f, 0.40f, 0.50f, 0.78f);

            switch (Biome)
            {
                case 0: // Лето
                    GrassA = C(0x5f9a35); GrassB = C(0x78ad3c); Dry = C(0xa9a852);
                    Leaf1 = C(0x4f8f2e); Leaf2 = C(0x3f7a28); Leaf3 = C(0x6aa83a);
                    Pine1 = C(0x2f5e32); Pine2 = C(0x3a6e3a);
                    Grass1 = C(0x5e9a2f); Grass2 = C(0x86b440); Bush = C(0x4a8a30);
                    Flower1 = C(0xf2e14a); Flower2 = C(0xf0f0f0); Flower3 = C(0xd9534f);
                    TreeDensity = 1f; PineShare = 0.35f; FlowerShare = 0.18f;
                    break;
                case 1: // Осень
                    GrassA = C(0x8a8a3a); GrassB = C(0x9c8c3c); Dry = C(0xb8904a);
                    Leaf1 = C(0xe0892a); Leaf2 = C(0xc4462a); Leaf3 = C(0xe8c040);
                    Pine1 = C(0x335a33); Pine2 = C(0x3f6a3a);
                    Grass1 = C(0x8f8a3a); Grass2 = C(0xb09a45); Bush = C(0xb0582c);
                    Flower1 = C(0xe8b030); Flower2 = C(0xc8602a); Flower3 = C(0xa03a28);
                    TreeDensity = 1f; PineShare = 0.3f; FlowerShare = 0.05f;
                    Water = new Color(0.14f, 0.34f, 0.40f, 0.8f);
                    break;
                case 2: // Зима
                    GrassA = C(0xe6ecf4); GrassB = C(0xd7e0ec); Dry = C(0x9aa08a);
                    SnowGround = true; SnowLine = 18f;
                    Leaf1 = C(0x6b4a2f); Leaf2 = C(0x5a3e28); Leaf3 = C(0x7a5a3a);
                    Pine1 = C(0x2c4c3a); Pine2 = C(0x365a44);
                    Grass1 = C(0x9aa37a); Grass2 = C(0xb8bfa0); Bush = C(0x7a7f68);
                    Flower1 = Flower2 = Flower3 = C(0xffffff);
                    TreeDensity = 0.9f; PineShare = 0.8f; GrassDensity = 0.25f; FlowerShare = 0f; BareTrees = true;
                    Underwater = C(0x6a8a94); Sand = C(0xc8ccc8);
                    Water = new Color(0.30f, 0.48f, 0.58f, 0.85f);
                    break;
                default: // Степь
                    GrassA = C(0xb4a55a); GrassB = C(0xc2b066); Dry = C(0xcfae6a);
                    Dirt = C(0x9a6a45); Rock = C(0xa47e62); Rock1 = C(0xb08a6a); Rock2 = C(0x8e6a52); Rock3 = C(0xc49a78);
                    Leaf1 = C(0x7a9a3a); Leaf2 = C(0x6a8a36); Leaf3 = C(0x94a848);
                    Pine1 = C(0x4a6a3a); Pine2 = C(0x5a7a40);
                    Grass1 = C(0xb8a458); Grass2 = C(0xd0bc6c); Bush = C(0x8a9a48);
                    Flower1 = C(0xe86a3a); Flower2 = C(0xf0d060); Flower3 = C(0xb06ac0);
                    TreeDensity = 0.25f; PineShare = 0.2f; FlowerShare = 0.08f; GrassDensity = 1.2f;
                    SnowLine = 60f;
                    Water = new Color(0.16f, 0.42f, 0.46f, 0.8f);
                    break;
            }
        }

        void ApplyTime()
        {
            switch (Time)
            {
                case 0: // День
                    SunEuler = new Vector3(52f, -35f, 0f);
                    SunColor = C(1f, 0.95f, 0.86f); SunIntensity = 1.45f;
                    AmbSky = C(0.52f, 0.62f, 0.78f); AmbEquator = C(0.50f, 0.55f, 0.55f); AmbGround = C(0.30f, 0.28f, 0.24f);
                    Fog = C(0.70f, 0.79f, 0.88f); FogDensity = 0.0030f;
                    SkyTint = C(0.45f, 0.52f, 0.62f); Atmosphere = 1.0f; Exposure = 1.25f;
                    break;
                case 1: // Закат
                    SunEuler = new Vector3(14f, -65f, 0f);
                    SunColor = C(1f, 0.70f, 0.42f); SunIntensity = 1.6f;
                    AmbSky = C(0.42f, 0.46f, 0.62f); AmbEquator = C(0.62f, 0.50f, 0.44f); AmbGround = C(0.24f, 0.19f, 0.16f);
                    Fog = C(0.86f, 0.66f, 0.52f); FogDensity = 0.0036f;
                    SkyTint = C(0.50f, 0.46f, 0.50f); Atmosphere = 1.55f; Exposure = 1.15f;
                    break;
                default: // Туманное утро
                    SunEuler = new Vector3(22f, 115f, 0f);
                    SunColor = C(1f, 0.88f, 0.74f); SunIntensity = 1.2f;
                    AmbSky = C(0.58f, 0.62f, 0.70f); AmbEquator = C(0.60f, 0.60f, 0.62f); AmbGround = C(0.32f, 0.30f, 0.28f);
                    Fog = C(0.74f, 0.77f, 0.82f); FogDensity = 0.0072f;
                    SkyTint = C(0.50f, 0.52f, 0.56f); Atmosphere = 1.2f; Exposure = 1.1f;
                    break;
            }
            SkyGround = C(0.37f, 0.35f, 0.34f);

            if (SnowGround)
            {
                // Снег отражает много света: чуть холоднее и светлее.
                AmbGround = Color.Lerp(AmbGround, C(0.6f, 0.64f, 0.7f), 0.6f);
                Fog = Color.Lerp(Fog, C(0.82f, 0.86f, 0.92f), 0.5f);
                SunIntensity *= 0.9f;
            }
        }
    }
}
