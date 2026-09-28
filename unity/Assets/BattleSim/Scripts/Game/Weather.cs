using BattleSim.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace BattleSim
{
    /// <summary>
    /// Погода вокруг камеры: зимой идёт снег, осенью кружат листья, в степи несёт пыль, летом висят пылинки;
    /// туманным утром — морось. Частицы живут в коробке вокруг точки, куда смотрит камера, и заворачиваются
    /// по краям — сколько ни летай, погода вокруг. Мягкие клубы шейдера BattleSim/Puff, один вызов отрисовки.
    /// </summary>
    public sealed class Weather
    {
        const int Batch = 1023;
        readonly Mesh quad;
        readonly Material mat;
        readonly Matrix4x4[] m = new Matrix4x4[Batch];
        readonly Vector4[] c = new Vector4[Batch];
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        static readonly int InstColor = Shader.PropertyToID("_InstColor");

        Vector3[] pos = new Vector3[0];
        float[] seed = new float[0];
        int count;
        float fall, size, drift, flutter;
        Color col;
        const float Half = 32, Height = 26;

        public Weather()
        {
            quad = new Mesh { name = "weather" };
            quad.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) };
            quad.uv = new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
            quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            quad.bounds = new Bounds(Vector3.zero, Vector3.one * 4);
            var sh = Shader.Find("BattleSim/Puff") ?? Shader.Find("BattleSim/Unlit");
            mat = new Material(sh) { name = "weather", enableInstancing = true, renderQueue = 3050 };
        }

        /// <summary>Погода под биом и время суток карты. lowEnd — вдвое меньше частиц.</summary>
        public void Setup(Style s, bool lowEnd)
        {
            int n;
            switch (s.Biome)
            {
                case 2: n = 900; fall = 1.3f; size = 0.07f; drift = 0.6f; flutter = 0.5f; col = new Color(1f, 1f, 1f, 0.85f); break;          // зима: снег
                case 1: n = 120; fall = 0.9f; size = 0.09f; drift = 1.2f; flutter = 1.4f; col = new Color(0.85f, 0.45f, 0.15f, 0.9f); break;  // осень: листья
                case 3: n = 260; fall = -0.05f; size = 0.14f; drift = 2.2f; flutter = 0.3f; col = new Color(0.8f, 0.68f, 0.45f, 0.22f); break; // степь: пыль по ветру
                default: n = 160; fall = 0.05f; size = 0.035f; drift = 0.25f; flutter = 0.6f; col = new Color(1f, 0.95f, 0.7f, 0.5f); break;   // лето: пылинки
            }
            if (s.Time == 2 && s.Biome != 2) { n = 700; fall = 5f; size = 0.025f; drift = 0.3f; flutter = 0; col = new Color(0.75f, 0.8f, 0.88f, 0.45f); } // туманное утро — морось
            if (lowEnd) n /= 2;
            count = n;
            pos = new Vector3[n]; seed = new float[n];
            for (int i = 0; i < n; i++)
            {
                pos[i] = new Vector3(Random.Range(-Half, Half), Random.Range(0, Height), Random.Range(-Half, Half));
                seed[i] = Random.value * 100;
            }
        }

        public void Draw(Vector3 focus, Vector3 wind, float dt, float time)
        {
            if (count == 0) return;
            var w = new Vector3(wind.x, 0, wind.z) * drift * 0.35f;
            int k = 0;
            for (int i = 0; i < count; i++)
            {
                var p = pos[i];
                float f = flutter * Mathf.Sin(time * 1.3f + seed[i]);
                p += (w + new Vector3(f * 0.6f, -fall + f * 0.2f, f * 0.4f * Mathf.Cos(seed[i]))) * dt;
                // коробка вокруг точки камеры: вылетело — заходит с другой стороны
                if (p.y < 0) p.y += Height; else if (p.y > Height) p.y -= Height;
                if (p.x < -Half) p.x += 2 * Half; else if (p.x > Half) p.x -= 2 * Half;
                if (p.z < -Half) p.z += 2 * Half; else if (p.z > Half) p.z -= 2 * Half;
                pos[i] = p;
                var wp = new Vector3(focus.x + p.x, focus.y - 3 + p.y, focus.z + p.z);
                float sz = size * (0.7f + 0.6f * Mathf.Repeat(seed[i], 1));
                m[k] = Matrix4x4.TRS(wp, Quaternion.identity, Vector3.one * sz);
                c[k] = col;
                if (++k == Batch) { Flush(k); k = 0; }
            }
            if (k > 0) Flush(k);
        }

        void Flush(int n)
        {
            mpb.Clear();
            mpb.SetVectorArray(InstColor, c);
            Graphics.DrawMeshInstanced(quad, 0, mat, m, n, mpb, ShadowCastingMode.Off, false, 0, null);
        }
    }
}
