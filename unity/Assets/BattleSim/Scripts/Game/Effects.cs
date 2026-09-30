using System.Collections.Generic;
using BattleSim.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace BattleSim
{
    /// <summary>
    /// Эффекты боя: пыль и щепки от ударов, искры от удара по щиту, клубы пыли при гибели и натиске,
    /// брызги в воде, колдовство Нави, поединки богатырей. Частицы — мягкие круглые клубы лицом к камере
    /// (шейдер BattleSim/Puff), все рисуются копиями одним вызовом на 1023 штуки.
    /// </summary>
    public sealed class Effects
    {
        const int Max = 3000;
        readonly Vector3[] pos = new Vector3[Max], vel = new Vector3[Max];
        readonly float[] life = new float[Max], maxLife = new float[Max], size = new float[Max], grow = new float[Max], grav = new float[Max];
        readonly Color[] col = new Color[Max];
        int count;

        readonly Mesh mesh;
        readonly Material mat;
        readonly Matrix4x4[] mats = new Matrix4x4[1023];
        readonly Vector4[] cols = new Vector4[1023];
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        static readonly int InstColor = Shader.PropertyToID("_InstColor");

        static readonly Color Dust = new Color(0.66f, 0.6f, 0.5f, 0.34f), Chip = new Color(0.38f, 0.32f, 0.26f, 1f);
        static readonly Color Soul = new Color(0.55f, 0.95f, 0.7f, 0.6f), Gloom = new Color(0.28f, 0.24f, 0.3f, 0.5f), Hex = new Color(0.75f, 0.35f, 1f, 0.8f);
        static readonly Color Spark = new Color(1f, 0.85f, 0.45f, 1f), Flash = new Color(1f, 0.95f, 0.8f, 0.55f);
        static readonly Color Water = new Color(0.8f, 0.9f, 1f, 0.8f);

        public Effects()
        {
            // квадрат −1…1: шейдер разворачивает его к камере и скругляет
            mesh = new Mesh { name = "puff" };
            mesh.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) };
            mesh.uv = new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 4);
            var sh = Shader.Find("BattleSim/Puff") ?? Shader.Find("BattleSim/Unlit");
            mat = new Material(sh) { name = "fx", enableInstancing = true, renderQueue = 3100 };
        }

        void Add(Vector3 p, Vector3 v, float lifeS, float sz, float growS, float gravity, Color c)
        {
            if (count >= Max) return;
            int i = count++;
            pos[i] = p; vel[i] = v; life[i] = lifeS; maxLife[i] = lifeS; size[i] = sz; grow[i] = growS; grav[i] = gravity; col[i] = c;
        }

        static Vector3 Rnd(float s) => new Vector3(Random.Range(-s, s), Random.Range(-s, s), Random.Range(-s, s));

        /// <summary>Разворачивает события ядра в частицы.</summary>
        public void Spawn(List<FxEvent> events)
        {
            foreach (var e in events)
            {
                var p = Conv.U(e.Pos);
                var d = new Vector3(e.Dir.x, 0, -e.Dir.z);
                if (d.sqrMagnitude > 1e-4f) d.Normalize();
                switch (e.Kind)
                {
                    case FxKind.Hit:
                        for (int k = 0; k < 4; k++) Add(p + Rnd(0.15f), d * 0.6f + Vector3.up * 0.5f + Rnd(0.5f), Random.Range(0.35f, 0.6f), 0.12f, 0.5f, -0.5f, Dust);
                        for (int k = 0; k < 3; k++) Add(p, d * 2.2f + Vector3.up * 2.4f + Rnd(1.4f), 0.5f, 0.045f, 0, 9.8f, Chip);
                        break;
                    case FxKind.Block:
                        Add(p, Vector3.zero, 0.09f, 0.16f, 1.2f, 0, Flash);
                        for (int k = 0; k < 7; k++) Add(p + Rnd(0.1f), d * 2.5f + Vector3.up * 2f + Rnd(2.6f), Random.Range(0.25f, 0.4f), 0.035f, -0.05f, 9.8f, Spark);
                        break;
                    case FxKind.Kill:
                        for (int k = 0; k < 4; k++)
                            Add(p + new Vector3(Random.Range(-0.5f, 0.5f), 0.15f, Random.Range(-0.5f, 0.5f)), Rnd(0.35f) + Vector3.up * 0.3f, Random.Range(0.6f, 0.9f), 0.2f, 0.45f, -0.2f, Dust);
                        break;
                    case FxKind.Charge:
                        for (int k = 0; k < 6; k++) Add(p + Vector3.up * 0.6f + Rnd(0.3f), d * 3f + Vector3.up * 1.2f + Rnd(1f), Random.Range(0.4f, 0.7f), 0.16f, 0.9f, 1.5f, Dust);
                        break;
                    case FxKind.Splash:
                        for (int k = 0; k < 7; k++) Add(p, Vector3.up * Random.Range(2f, 3.4f) + Rnd(1.1f), Random.Range(0.4f, 0.6f), 0.05f, 0, 9.8f, Water);
                        Add(p, Vector3.zero, 0.4f, 0.15f, 1.6f, 0, new Color(1, 1, 1, 0.5f));
                        break;
                    case FxKind.Explosion:
                        Add(p + Vector3.up * 0.4f, Vector3.zero, 0.16f, 0.5f, 7f, 0, new Color(1f, 0.8f, 0.45f, 0.9f));
                        Add(p + Vector3.up * 0.6f, Vector3.up * 0.8f, 1.6f, 0.6f, 1.2f, -0.3f, new Color(0.22f, 0.2f, 0.19f, 0.55f)); // дым
                        for (int k = 0; k < 16; k++)
                        {
                            float a = k / 16f * Mathf.PI * 2;
                            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                            Add(p + dir * 0.4f + Vector3.up * 0.3f, dir * Random.Range(3f, 5f) + Vector3.up * Random.Range(0.3f, 1.2f), Random.Range(0.6f, 1.0f), 0.3f, 1.4f, 0.4f, Dust);
                        }
                        for (int k = 0; k < 10; k++) Add(p + Vector3.up * 0.3f, Rnd(3f) + Vector3.up * Random.Range(4f, 7f), 1.1f, 0.07f, 0, 9.8f, Chip);
                        break;
                    case FxKind.Roar:
                        for (int k = 0; k < 36; k++)
                        {
                            float a = k / 36f * Mathf.PI * 2;
                            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                            Add(p + dir * 1.5f + Vector3.up * 0.4f, dir * 34f, 1.3f, 0.45f, 0.6f, 0, new Color(1f, 0.25f, 0.15f, 0.7f));
                        }
                        break;
                    case FxKind.Cry:
                        for (int k = 0; k < 14; k++)
                        {
                            float a = k / 14f * Mathf.PI * 2;
                            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                            Add(p + dir * 2f + Vector3.up * 0.2f, dir * 7f, 0.7f, 0.18f, 0.6f, 0, new Color(1f, 0.92f, 0.6f, 0.55f));
                        }
                        break;
                    case FxKind.Crumble: // от дома отлетели куски
                        for (int k = 0; k < 6; k++) Add(p + Rnd(0.8f), Rnd(1.5f) + Vector3.up * 1.5f, Random.Range(0.8f, 1.2f), 0.09f, 0, 9.8f, Chip);
                        for (int k = 0; k < 3; k++) Add(p + Rnd(0.6f), Rnd(0.4f) + Vector3.down * 0.3f, Random.Range(1f, 1.5f), 0.35f, 0.8f, -0.2f, Dust);
                        break;
                    case FxKind.Collapse: // дом рухнул: столб пыли, кольцо пыли по земле, обломки
                        for (int k = 0; k < 10; k++) Add(p + new Vector3(Random.Range(-2.5f, 2.5f), Random.Range(0.3f, 3f), Random.Range(-2.5f, 2.5f)), Rnd(0.6f) + Vector3.up * Random.Range(0.6f, 1.6f), Random.Range(2.2f, 3.4f), 1.1f, 1.4f, -0.15f, Dust);
                        for (int k = 0; k < 24; k++)
                        {
                            float a = k / 24f * Mathf.PI * 2;
                            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                            Add(p + dir * 2.5f + Vector3.up * 0.3f, dir * Random.Range(2.5f, 4.5f) + Vector3.up * 0.3f, Random.Range(1.2f, 1.8f), 0.6f, 1.2f, 0.2f, Dust);
                        }
                        for (int k = 0; k < 18; k++) Add(p + Vector3.up * Random.Range(0.5f, 3f) + Rnd(1.5f), Rnd(3f) + Vector3.up * Random.Range(2f, 5f), 1.2f, 0.12f, 0, 9.8f, Chip);
                        break;
                    case FxKind.Shatter: // ящик, бочка, баррикада — в щепки
                        for (int k = 0; k < 12; k++) Add(p + Rnd(0.3f) + Vector3.up * 0.4f, d * 2f + Rnd(2.5f) + Vector3.up * Random.Range(2f, 4f), Random.Range(0.7f, 1.1f), 0.07f, 0, 9.8f, Chip);
                        for (int k = 0; k < 4; k++) Add(p + Rnd(0.4f) + Vector3.up * 0.3f, Rnd(0.8f) + Vector3.up * 0.4f, Random.Range(0.6f, 1f), 0.3f, 0.8f, -0.1f, Dust);
                        break;
                    case FxKind.Down:
                        for (int k = 0; k < 5; k++) Add(p + new Vector3(Random.Range(-0.4f, 0.4f), 0.15f, Random.Range(-0.4f, 0.4f)), Rnd(0.4f) + Vector3.up * 0.3f, 0.8f, 0.2f, 0.6f, 0, Dust);
                        break;
                    case FxKind.Raise:
                        // земля вспучивается, из неё тянутся зеленоватые клочья
                        for (int k = 0; k < 6; k++) Add(p + new Vector3(Random.Range(-0.5f, 0.5f), 0.1f, Random.Range(-0.5f, 0.5f)), Rnd(0.6f) + Vector3.up * 0.6f, Random.Range(0.8f, 1.3f), 0.25f, 0.5f, 0, Gloom);
                        for (int k = 0; k < 8; k++) Add(p + new Vector3(Random.Range(-0.4f, 0.4f), 0.2f, Random.Range(-0.4f, 0.4f)), Vector3.up * Random.Range(1.2f, 2.4f) + Rnd(0.3f), Random.Range(0.9f, 1.5f), 0.1f, 0.12f, -0.4f, Soul);
                        for (int k = 0; k < 5; k++) Add(p + Vector3.up * 0.1f, Rnd(1.2f) + Vector3.up * 2.5f, 0.8f, 0.05f, 0, 9.8f, Chip);
                        break;
                    case FxKind.Curse:
                        Add(p, Vector3.zero, 0.25f, 0.3f, 2.2f, 0, Hex);
                        for (int k = 0; k < 8; k++) Add(p + Rnd(0.2f), Rnd(1.6f), Random.Range(0.4f, 0.7f), 0.07f, -0.05f, -0.6f, Hex);
                        break;
                    case FxKind.Wall:
                    {
                        // строй сомкнул щиты: по фронту пробегает волна пыли и блеск
                        var side = new Vector3(d.z, 0, -d.x);
                        for (int k = -5; k <= 5; k++)
                        {
                            var q = p + side * (k * 0.9f) + d * 0.8f;
                            Add(q + Vector3.up * 0.2f, d * 0.8f + Vector3.up * 0.3f, 0.6f, 0.22f, 0.5f, 0, Dust);
                            if ((k & 1) == 0) Add(q + Vector3.up * 1.1f, Vector3.up * 0.4f, 0.18f, 0.12f, 0.6f, 0, Flash);
                        }
                        break;
                    }
                    case FxKind.Duel:
                        // круг расступившихся: кольцо пыли вокруг поединщиков
                        for (int k = 0; k < 28; k++)
                        {
                            float a = k / 28f * Mathf.PI * 2;
                            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                            Add(p + dir * 2.5f + Vector3.up * 0.2f, dir * 3.5f + Vector3.up * 0.2f, 0.9f, 0.3f, 0.7f, 0, Dust);
                        }
                        break;
                    case FxKind.Hero:
                        Add(p + Vector3.up * 0.5f, Vector3.zero, 0.2f, 0.5f, 6f, 0, Flash);
                        for (int k = 0; k < 14; k++) Add(p + new Vector3(Random.Range(-0.8f, 0.8f), 0.2f, Random.Range(-0.8f, 0.8f)), Rnd(1f) + Vector3.up * 0.6f, Random.Range(1f, 1.6f), 0.35f, 0.9f, -0.2f, Dust);
                        break;
                    case FxKind.BoltGround:
                        for (int k = 0; k < 2; k++) Add(p + Rnd(0.05f), Vector3.up * 0.4f + Rnd(0.3f), 0.45f, 0.08f, 0.35f, 0, Dust);
                        break;
                }
            }
        }

        /// <summary>Движение и угасание частиц; dt — время боя (в замедлении эффекты тоже медленные).</summary>
        public void Update(float dt)
        {
            if (dt <= 0) return;
            for (int i = 0; i < count; i++)
            {
                life[i] -= dt;
                if (life[i] <= 0)
                { // на место умершей — последняя
                    count--;
                    pos[i] = pos[count]; vel[i] = vel[count]; life[i] = life[count]; maxLife[i] = maxLife[count];
                    size[i] = size[count]; grow[i] = grow[count]; grav[i] = grav[count]; col[i] = col[count];
                    i--;
                    continue;
                }
                vel[i].y -= grav[i] * dt;
                vel[i] *= 1 - 1.8f * dt * (grav[i] > 5 ? 0.2f : 1f);
                pos[i] += vel[i] * dt;
                size[i] += grow[i] * dt;
            }
        }

        public void Draw()
        {
            for (int k = 0; k * 1023 < count; k++)
            {
                int n = Mathf.Min(1023, count - k * 1023);
                for (int j = 0; j < n; j++)
                {
                    int i = k * 1023 + j;
                    float t = life[i] / maxLife[i];
                    mats[j] = Matrix4x4.TRS(pos[i], Quaternion.identity, Vector3.one * Mathf.Max(0.01f, size[i]));
                    var c = col[i];
                    cols[j] = new Vector4(c.r, c.g, c.b, c.a * Mathf.Clamp01(t * 1.6f));
                }
                mpb.Clear();
                mpb.SetVectorArray(InstColor, cols);
                Graphics.DrawMeshInstanced(mesh, 0, mat, mats, n, mpb, ShadowCastingMode.Off, false, 0, null);
            }
        }

        public void Clear() => count = 0;
    }
}
