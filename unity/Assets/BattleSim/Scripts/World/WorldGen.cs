using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BattleSim
{
    /// <summary>
    /// Процедурный мир: рельеф с холмами и горами, озёра, раскрашенная текстура земли,
    /// деревья, камни и трава. В центре всегда ровное поле боя без воды.
    /// </summary>
    public class WorldGen
    {
        public const float Size = 440f;
        public const int Res = 256;                 // клеток рельефа на сторону
        public const float Cell = Size / Res;
        public const float Half = Size * 0.5f;
        public const float WaterLevel = 0f;
        public const float FieldHalf = 80f;         // половина стороны поля боя
        const int Chunks = 4;                       // рельеф режется на 4x4 куска
        const int TexRes = 512;

        readonly float[] h = new float[(Res + 1) * (Res + 1)];
        readonly List<Object> owned = new List<Object>();
        float ox, oz, lx, lz, mx, mz, cx1, cz1;
        System.Random rng;

        public Transform Root { get; private set; }
        public WorldStyle Style { get; private set; }

        public void Generate(int seed, WorldStyle style, Material terrainTemplate, Material paletteMat, Material waterMat)
        {
            Cleanup();
            Style = style;
            Root = new GameObject("World").transform;
            rng = new System.Random(seed);
            ox = Rand(0f, 200f); oz = Rand(0f, 200f);
            lx = Rand(0f, 200f); lz = Rand(0f, 200f);
            mx = Rand(0f, 200f); mz = Rand(0f, 200f);
            cx1 = Rand(0f, 200f); cz1 = Rand(0f, 200f);

            for (int z = 0; z <= Res; z++)
                for (int x = 0; x <= Res; x++)
                    h[z * (Res + 1) + x] = RawHeight(x * Cell - Half, z * Cell - Half);

            var colorMap = BuildColorMap(style);
            var terrainMat = new Material(terrainTemplate) { name = "Terrain" };
            GameMaterials.SetMainTexture(terrainMat, colorMap);
            owned.Add(terrainMat);
            BuildTerrain(terrainMat);
            BuildWater(waterMat, style);
            BuildDecor(style, paletteMat);
        }

        public void Cleanup()
        {
            if (Root != null) Object.Destroy(Root.gameObject);
            Root = null;
            foreach (var o in owned) if (o != null) Object.Destroy(o);
            owned.Clear();
        }

        float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        // ---------------------------------------------------------------- высоты

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        static float Fbm(float x, float z, int octaves)
        {
            float sum = 0f, amp = 1f, freq = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += (Mathf.PerlinNoise(x * freq, z * freq) * 2f - 1f) * amp;
                norm += amp; amp *= 0.5f; freq *= 2.03f;
            }
            return sum / norm;
        }

        static float Ridged(float x, float z, int octaves)
        {
            float sum = 0f, amp = 1f, freq = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                float n = 1f - Mathf.Abs(Mathf.PerlinNoise(x * freq, z * freq) * 2f - 1f);
                sum += n * n * amp;
                norm += amp; amp *= 0.5f; freq *= 2.1f;
            }
            return sum / norm;
        }

        /// <summary>"Скруглённый квадрат": 0 в центре, растёт к краям.</summary>
        static float EdgeDist(float x, float z)
        {
            return Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) * 0.6f + Mathf.Sqrt(x * x + z * z) * 0.4f;
        }

        float RawHeight(float x, float z)
        {
            float dist = EdgeDist(x, z);
            float hills = Fbm(x * 0.006f + ox, z * 0.006f + oz, 4);
            float detail = Fbm(x * 0.05f + ox + 31.7f, z * 0.05f + oz - 11.3f, 2);

            // Поле боя: мягкие холмы, всегда выше воды.
            float field = 2.6f + hills * 4.5f + detail * 0.35f;
            if (field < 1.2f) field = 1.2f - (1.2f - field) * 0.3f;

            // Окрестности: холмы покрупнее, озёра, горы по краям.
            float outer = 3f + hills * 16f + detail * 0.9f;
            float lake = Mathf.PerlinNoise(x * 0.011f + lx, z * 0.011f + lz);
            outer -= Mathf.Max(0f, lake - 0.58f) * 70f * Smooth(105f, 135f, dist);
            float floor = Mathf.Lerp(1.0f, -30f, Smooth(100f, 125f, dist));
            if (outer < floor) outer = floor;
            float mt = Smooth(115f, 215f, dist);
            outer += mt * mt * (30f + Ridged(x * 0.009f + mx, z * 0.009f + mz, 4) * 60f);

            return Mathf.Lerp(field, outer, Smooth(FieldHalf - 5f, FieldHalf + 40f, dist));
        }

        float H(int ix, int iz) => h[iz * (Res + 1) + ix];

        /// <summary>Точная высота поверхности меша в точке (x, z).</summary>
        public float HeightAt(float x, float z)
        {
            float gx = Mathf.Clamp((x + Half) / Cell, 0f, Res - 0.0001f);
            float gz = Mathf.Clamp((z + Half) / Cell, 0f, Res - 0.0001f);
            int ix = (int)gx, iz = (int)gz;
            float fx = gx - ix, fz = gz - iz;
            float h00 = H(ix, iz), h10 = H(ix + 1, iz), h01 = H(ix, iz + 1), h11 = H(ix + 1, iz + 1);
            if (fz >= fx) return h00 + (h11 - h01) * fx + (h01 - h00) * fz;
            return h00 + (h10 - h00) * fx + (h11 - h10) * fz;
        }

        Vector3 GridNormal(int ix, int iz)
        {
            float hl = H(Mathf.Max(ix - 1, 0), iz), hr = H(Mathf.Min(ix + 1, Res), iz);
            float hd = H(ix, Mathf.Max(iz - 1, 0)), hu = H(ix, Mathf.Min(iz + 1, Res));
            return new Vector3(hl - hr, 2f * Cell, hd - hu).normalized;
        }

        public Vector3 NormalAt(float x, float z)
        {
            const float e = 1f;
            float hl = HeightAt(x - e, z), hr = HeightAt(x + e, z), hd = HeightAt(x, z - e), hu = HeightAt(x, z + e);
            return new Vector3(hl - hr, 2f * e, hd - hu).normalized;
        }

        public static bool InField(Vector3 p, float margin = 0f)
        {
            return Mathf.Abs(p.x) <= FieldHalf - margin && Mathf.Abs(p.z) <= FieldHalf - margin;
        }

        /// <summary>Пересечение луча с рельефом (пошагово + уточнение делением пополам).</summary>
        public bool Raycast(Ray ray, out Vector3 hit, float maxDist = 1200f)
        {
            hit = Vector3.zero;
            const float step = 1.5f;
            float prevT = 0f;
            for (float t = 0f; t <= maxDist; t += step)
            {
                Vector3 p = ray.GetPoint(t);
                if (Mathf.Abs(p.x) > Half || Mathf.Abs(p.z) > Half) { if (t > 0f && IsOutsideGoingOut(ray, p)) return false; prevT = t; continue; }
                if (p.y <= HeightAt(p.x, p.z))
                {
                    float a = prevT, b = t;
                    for (int i = 0; i < 14; i++)
                    {
                        float m = (a + b) * 0.5f;
                        Vector3 q = ray.GetPoint(m);
                        if (q.y <= HeightAt(q.x, q.z)) b = m; else a = m;
                    }
                    hit = ray.GetPoint(b);
                    hit.y = HeightAt(hit.x, hit.z);
                    return true;
                }
                prevT = t;
            }
            return false;
        }

        static bool IsOutsideGoingOut(Ray ray, Vector3 p)
        {
            return (p.x > Half && ray.direction.x > 0f) || (p.x < -Half && ray.direction.x < 0f) ||
                   (p.z > Half && ray.direction.z > 0f) || (p.z < -Half && ray.direction.z < 0f);
        }

        // ---------------------------------------------------------------- текстура земли

        Texture2D BuildColorMap(WorldStyle s)
        {
            var px = new Color32[TexRes * TexRes];
            for (int ty = 0; ty < TexRes; ty++)
            {
                for (int tx = 0; tx < TexRes; tx++)
                {
                    float x = (tx + 0.5f) / TexRes * Size - Half;
                    float z = (ty + 0.5f) / TexRes * Size - Half;
                    float y = HeightAt(x, z);
                    float slope = 1f - NormalAt(x, z).y;
                    float n1 = Mathf.PerlinNoise(x * 0.035f + cx1, z * 0.035f + cz1);
                    float n2 = Mathf.PerlinNoise(x * 0.16f + cz1, z * 0.16f + cx1);
                    float n3 = Mathf.PerlinNoise(x * 0.012f + cx1 * 0.5f, z * 0.012f + cz1 * 0.5f);

                    Color c = Color.Lerp(s.GrassA, s.GrassB, n1);
                    c = Color.Lerp(c, s.Dry, Smooth(0.55f, 0.8f, n3) * 0.75f);
                    c = Color.Lerp(c, s.Dirt, Smooth(0.72f, 0.85f, n2 * 0.6f + n1 * 0.4f) * 0.6f);

                    float shore = y - WaterLevel;
                    c = Color.Lerp(s.Sand, c, Smooth(0.2f, 1.4f, shore + (n2 - 0.5f) * 0.6f));
                    if (shore < 0f) c = Color.Lerp(s.Sand, s.Underwater, Smooth(0f, -5f, shore));

                    c = Color.Lerp(c, s.Rock, Smooth(0.22f, 0.4f, slope + (n2 - 0.5f) * 0.12f));
                    c = Color.Lerp(c, s.Rock, Smooth(32f, 48f, y + (n1 - 0.5f) * 12f));
                    float snow = Smooth(s.SnowLine, s.SnowLine + 6f, y + (n2 - 0.5f) * 10f) * (1f - Smooth(0.45f, 0.65f, slope));
                    c = Color.Lerp(c, s.Snow, snow);
                    if (s.SnowGround && shore > 0.2f)
                        c = Color.Lerp(c, Color.Lerp(s.GrassA, s.GrassB, n1), (1f - Smooth(0.3f, 0.5f, slope)) * 0.85f);

                    c *= 0.9f + n2 * 0.18f;
                    c.a = 1f;
                    px[ty * TexRes + tx] = c;
                }
            }
            var tex = new Texture2D(TexRes, TexRes, TextureFormat.RGBA32, true)
            {
                name = "TerrainColors",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4
            };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            owned.Add(tex);
            return tex;
        }

        // ---------------------------------------------------------------- меши рельефа и воды

        void BuildTerrain(Material mat)
        {
            int per = Res / Chunks;
            int vw = per + 1;
            for (int cz = 0; cz < Chunks; cz++)
            {
                for (int cx = 0; cx < Chunks; cx++)
                {
                    var v = new Vector3[vw * vw];
                    var n = new Vector3[vw * vw];
                    var uv = new Vector2[vw * vw];
                    for (int z = 0; z < vw; z++)
                    {
                        for (int x = 0; x < vw; x++)
                        {
                            int gx = cx * per + x, gz = cz * per + z;
                            int i = z * vw + x;
                            v[i] = new Vector3(gx * Cell - Half, H(gx, gz), gz * Cell - Half);
                            n[i] = GridNormal(gx, gz);
                            uv[i] = new Vector2(gx / (float)Res, gz / (float)Res);
                        }
                    }
                    var t = new int[per * per * 6];
                    int k = 0;
                    for (int z = 0; z < per; z++)
                    {
                        for (int x = 0; x < per; x++)
                        {
                            int i00 = z * vw + x, i10 = i00 + 1, i01 = i00 + vw, i11 = i01 + 1;
                            t[k++] = i00; t[k++] = i01; t[k++] = i11;
                            t[k++] = i00; t[k++] = i11; t[k++] = i10;
                        }
                    }
                    var mesh = new Mesh { name = "Terrain" };
                    mesh.vertices = v; mesh.normals = n; mesh.uv = uv; mesh.triangles = t;
                    mesh.RecalculateBounds();
                    owned.Add(mesh);
                    var r = AddRenderer("Terrain " + cx + "," + cz, mesh, mat);
                    r.shadowCastingMode = ShadowCastingMode.On;
                }
            }
        }

        void BuildWater(Material template, WorldStyle s)
        {
            var mat = new Material(template) { name = "Water" };
            GameMaterials.SetColor(mat, s.Water);
            owned.Add(mat);
            float e = Half + 30f;
            var mesh = new Mesh { name = "Water" };
            mesh.vertices = new[] { new Vector3(-e, WaterLevel, -e), new Vector3(-e, WaterLevel, e), new Vector3(e, WaterLevel, e), new Vector3(e, WaterLevel, -e) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            owned.Add(mesh);
            var r = AddRenderer("Water", mesh, mat);
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        MeshRenderer AddRenderer(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            return r;
        }

        // ---------------------------------------------------------------- деревья, камни, трава

        void BuildDecor(WorldStyle s, Material paletteMat)
        {
            int n = Chunks * Chunks;
            var solid = new MeshKit[n];
            var grass = new MeshKit[n];
            for (int i = 0; i < n; i++) { solid[i] = new MeshKit(); grass[i] = new MeshKit(); }
            int ChunkOf(float x, float z)
            {
                int cx = Mathf.Clamp((int)((x + Half) / Size * Chunks), 0, Chunks - 1);
                int cz = Mathf.Clamp((int)((z + Half) / Size * Chunks), 0, Chunks - 1);
                return cz * Chunks + cx;
            }

            // Деревья: рощами, вне поля боя, не в воде и не на скалах.
            int treeTries = (int)(2600 * s.TreeDensity);
            for (int i = 0; i < treeTries; i++)
            {
                float x = Rand(-Half + 6f, Half - 6f), z = Rand(-Half + 6f, Half - 6f);
                if (Mathf.Abs(x) < FieldHalf + 6f && Mathf.Abs(z) < FieldHalf + 6f) continue;
                float y = HeightAt(x, z);
                if (y < WaterLevel + 0.8f || y > s.SnowLine + 4f) continue;
                if (NormalAt(x, z).y < 0.8f) continue;
                float forest = Mathf.PerlinNoise(x * 0.013f + mx, z * 0.013f + mz);
                if (forest < 0.45f && rng.NextDouble() > 0.06) continue;
                var kit = solid[ChunkOf(x, z)];
                float scale = Rand(0.8f, 1.35f);
                kit.M = Matrix4x4.TRS(new Vector3(x, y - 0.15f, z), Quaternion.Euler(0f, Rand(0f, 360f), 0f), Vector3.one * scale);
                if (rng.NextDouble() < s.PineShare) Pine(kit, s);
                else if (s.BareTrees) BareTree(kit);
                else LeafTree(kit);
            }

            // Кусты по краю поля.
            for (int i = 0; i < 220 * s.TreeDensity + 40; i++)
            {
                float x = Rand(-FieldHalf - 30f, FieldHalf + 30f), z = Rand(-FieldHalf - 30f, FieldHalf + 30f);
                if (Mathf.Abs(x) < FieldHalf - 4f && Mathf.Abs(z) < FieldHalf - 4f) continue;
                float y = HeightAt(x, z);
                if (y < WaterLevel + 0.5f) continue;
                var kit = solid[ChunkOf(x, z)];
                kit.M = Matrix4x4.TRS(new Vector3(x, y - 0.1f, z), Quaternion.Euler(0f, Rand(0f, 360f), 0f), Vector3.one * Rand(0.7f, 1.4f));
                kit.Color = Palette.Bush;
                kit.Ellipsoid(new Vector3(0f, 0.45f, 0f), new Vector3(0.8f, 0.6f, 0.8f), 6, 4, 0.12f, rng.Next());
                kit.Ellipsoid(new Vector3(0.5f, 0.35f, 0.2f), new Vector3(0.5f, 0.45f, 0.5f), 6, 3, 0.12f, rng.Next());
            }

            // Камни: крупные снаружи, мелкие на поле.
            for (int i = 0; i < 520; i++)
            {
                float x = Rand(-Half + 5f, Half - 5f), z = Rand(-Half + 5f, Half - 5f);
                bool inField = Mathf.Abs(x) < FieldHalf + 4f && Mathf.Abs(z) < FieldHalf + 4f;
                float size = inField ? Rand(0.25f, 0.6f) : Rand(0.8f, 4.5f);
                if (inField && rng.NextDouble() > 0.35) continue;
                float y = HeightAt(x, z);
                if (y < WaterLevel - 1.5f) continue;
                var kit = solid[ChunkOf(x, z)];
                kit.M = Matrix4x4.TRS(new Vector3(x, y - size * 0.25f, z), Quaternion.Euler(Rand(-10f, 10f), Rand(0f, 360f), Rand(-10f, 10f)), Vector3.one);
                kit.Color = Palette.Rock1 + rng.Next(3);
                kit.Ellipsoid(Vector3.zero, new Vector3(size * Rand(0.9f, 1.4f), size * Rand(0.6f, 0.9f), size), 6, 4, 0.22f, rng.Next());
            }

            // Трава и цветы на поле и вокруг.
            int tufts = (int)(5200 * s.GrassDensity);
            for (int i = 0; i < tufts; i++)
            {
                float x = Rand(-FieldHalf - 25f, FieldHalf + 25f), z = Rand(-FieldHalf - 25f, FieldHalf + 25f);
                float y = HeightAt(x, z);
                if (y < WaterLevel + 0.4f) continue;
                if (Mathf.PerlinNoise(x * 0.05f + lx, z * 0.05f + lz) < 0.35f) continue;
                var kit = grass[ChunkOf(x, z)];
                kit.M = Matrix4x4.TRS(new Vector3(x, y - 0.03f, z), Quaternion.Euler(0f, Rand(0f, 360f), 0f), Vector3.one * Rand(0.7f, 1.3f));
                bool flower = rng.NextDouble() < s.FlowerShare;
                GrassTuft(kit, flower ? Palette.Flower1 + rng.Next(3) : -1);
            }

            for (int i = 0; i < n; i++)
            {
                if (solid[i].VertexCount > 0)
                {
                    var m = solid[i].ToMesh("Decor");
                    owned.Add(m);
                    AddRenderer("Decor " + i, m, paletteMat).shadowCastingMode = ShadowCastingMode.On;
                }
                if (grass[i].VertexCount > 0)
                {
                    var m = grass[i].ToMesh("Grass");
                    owned.Add(m);
                    AddRenderer("Grass " + i, m, paletteMat).shadowCastingMode = ShadowCastingMode.Off;
                }
            }
        }

        void LeafTree(MeshKit k)
        {
            k.Color = Palette.Trunk;
            k.Cylinder(Vector3.zero, new Vector3(0f, 2.4f, 0f), 0.24f, 0.15f, 5);
            k.Cylinder(new Vector3(0f, 1.6f, 0f), new Vector3(0.7f, 2.6f, 0.2f), 0.09f, 0.05f, 4);
            int leaf = Palette.Leaf1 + rng.Next(3);
            k.Color = leaf;
            k.Ellipsoid(new Vector3(0f, 3.4f, 0f), new Vector3(1.6f, 1.35f, 1.6f), 7, 4, 0.1f, rng.Next());
            k.Color = Palette.Leaf1 + rng.Next(3);
            k.Ellipsoid(new Vector3(0.8f, 2.9f, 0.35f), new Vector3(1.0f, 0.9f, 1.0f), 6, 4, 0.1f, rng.Next());
            k.Color = leaf;
            k.Ellipsoid(new Vector3(-0.5f, 4.1f, -0.3f), new Vector3(0.95f, 0.85f, 0.95f), 6, 4, 0.1f, rng.Next());
        }

        void Pine(MeshKit k, WorldStyle s)
        {
            k.Color = Palette.Trunk;
            k.Cylinder(Vector3.zero, new Vector3(0f, 1.4f, 0f), 0.22f, 0.16f, 5);
            int col = rng.NextDouble() < 0.5 ? Palette.Pine1 : Palette.Pine2;
            float[] baseY = { 1.0f, 2.5f, 3.8f };
            float[] height = { 2.8f, 2.4f, 2.1f };
            float[] rad = { 1.7f, 1.3f, 0.9f };
            for (int i = 0; i < 3; i++)
            {
                k.Color = s.SnowGround && i == 2 ? Palette.SnowCap : col;
                k.Cylinder(new Vector3(0f, baseY[i], 0f), new Vector3(0f, baseY[i] + height[i], 0f), rad[i], 0f, 7);
            }
        }

        void BareTree(MeshKit k)
        {
            k.Color = Palette.Trunk;
            k.Cylinder(Vector3.zero, new Vector3(0f, 3.2f, 0f), 0.22f, 0.1f, 5);
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f + Rand(-25f, 25f);
                Vector3 dir = Quaternion.Euler(0f, a, 0f) * new Vector3(0.9f, 1.1f, 0f);
                Vector3 from = new Vector3(0f, 1.6f + i * 0.4f, 0f);
                k.Cylinder(from, from + dir * Rand(0.8f, 1.3f), 0.07f, 0.03f, 4);
            }
            k.Color = Palette.SnowCap;
            k.Ellipsoid(new Vector3(0f, 3.25f, 0f), new Vector3(0.18f, 0.08f, 0.18f), 5, 2);
        }

        void GrassTuft(MeshKit k, int flowerColor)
        {
            int blades = 4;
            for (int b = 0; b < blades; b++)
            {
                k.Color = rng.NextDouble() < 0.5 ? Palette.Grass1 : Palette.Grass2;
                float a = b * Mathf.PI * 2f / blades + Rand(-0.4f, 0.4f);
                Vector3 side = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 across = new Vector3(-side.z, 0f, side.x) * 0.05f;
                Vector3 root = side * 0.06f;
                Vector3 tip = side * Rand(0.12f, 0.25f) + Vector3.up * Rand(0.28f, 0.5f);
                k.TriBoth(root - across, root + across, tip);
            }
            if (flowerColor >= 0)
            {
                k.Color = flowerColor;
                k.Box(new Vector3(0f, 0.42f, 0f), new Vector3(0.09f, 0.06f, 0.09f), Quaternion.Euler(0f, 45f, 0f));
            }
        }
    }
}
