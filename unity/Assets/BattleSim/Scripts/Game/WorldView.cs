using System.Collections.Generic;
using BattleSim.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace BattleSim
{
    /// <summary>Всё, что видно из мира: рельеф, вода, стены, мосты, лес, камни, трава, город, замки.</summary>
    public sealed class WorldView
    {
        GameObject root;
        readonly List<Object> owned = new List<Object>();
        readonly List<InstancedSet> sets = new List<InstancedSet>();
        static Texture2D detailTex;
        static Mesh tuftProto, flowerProto;

        public void Clear()
        {
            if (root != null) Object.Destroy(root);
            foreach (var o in owned) if (o != null) Object.Destroy(o);
            owned.Clear();
            sets.Clear();
            root = null;
        }

        T Own<T>(T o) where T : Object { owned.Add(o); return o; }

        GameObject Add(string name, Mesh mesh, Material mat, bool castShadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = true;
            return go;
        }

        public void Build(World w, Style s, ModelLibrary lib, bool lowEnd)
        {
            Clear();
            root = new GameObject("World");
            BuildTerrain(w, s, lowEnd);
            BuildWater(w, s);
            var white = Own(ModelLibrary.NewLit(null, Color.white, "vertexColors"));
            world = w; library = lib; wallMat = white;
            BuildEnv();
            var fort = WorldMeshes.Fortress(w);
            if (fort != null) Add("Fortress", Own(Conv.ToMesh(fort, "fortress")), white);
            var bridges = WorldMeshes.Bridges(w);
            if (bridges != null) Add("Bridges", Own(Conv.ToMesh(bridges, "bridges")), white);
            BuildDecor(w, s, lib);
        }

        static Texture2D DetailTex()
        {
            if (detailTex != null) return detailTex;
            // Бесшовный шум: смешиваем четыре сдвинутые копии
            int n = 128;
            var pn = new Perlin(new Rng(7));
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    double u = (double)x / n, v = (double)y / n;
                    double F(double a, double b, double sc) => pn.N01(a * sc, b * sc);
                    double Tile(double sc) => F(u, v, sc) * (1 - u) * (1 - v) + F(u - 1, v, sc) * u * (1 - v) + F(u, v - 1, sc) * (1 - u) * v + F(u - 1, v - 1, sc) * u * v;
                    px[y * n + x] = new Color32((byte)Mathf.Clamp((float)(Tile(9) * 255), 0, 255), (byte)Mathf.Clamp((float)(Tile(3) * 255), 0, 255), 128, 255);
                }
            detailTex = new Texture2D(n, n, TextureFormat.RGBA32, true, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, name = "detail" };
            detailTex.SetPixels32(px);
            detailTex.Apply(true, true);
            return detailTex;
        }

        void BuildTerrain(World w, Style s, bool lowEnd)
        {
            int T = World.TexSize;
            var rgba = w.PaintTerrain(s, T);
            var tex = Own(new Texture2D(T, T, TextureFormat.RGBA32, true, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 8, name = "terrain" });
            tex.SetPixelData(rgba, 0);
            tex.Apply(true, true);
            var mat = Own(ModelLibrary.NewLit(tex, Color.white, "terrain"));
            mat.SetTexture("_DetailMap", DetailTex());
            mat.SetFloat("_Detail", 1);
            mat.SetFloat("_Spec", 0);
            int chunk = 64;
            for (int z0 = 0; z0 < w.Res; z0 += chunk)
                for (int x0 = 0; x0 < w.Res; x0 += chunk)
                {
                    var m = WorldMeshes.TerrainChunk(w, x0, z0, chunk);
                    Add("Terrain", Own(Conv.ToMesh(m, "terrain")), mat, !lowEnd);
                }
        }

        void BuildWater(World w, Style s)
        {
            float h = w.Half + 40;
            var mesh = Own(new Mesh { name = "water" });
            mesh.vertices = new[] { new Vector3(-h, World.Water, -h), new Vector3(h, World.Water, -h), new Vector3(h, World.Water, h), new Vector3(-h, World.Water, h) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            var mat = Own(new Material(Shader.Find("BattleSim/Water")) { name = "water" });
            mat.SetColor("_BaseColor", Conv.Col(s.Water, s.WaterAlpha));
            mat.SetColor("_SkyColor", Conv.Col(s.Fog));
            Add("Water", mesh, mat, false);
        }

        void BuildDecor(World w, Style s, ModelLibrary lib)
        {
            // Деревья и камни — инстансингом
            var treeMat = lib.TreeMaterial(s.Leaf);
            for (int k = 0; k < World.TreeKinds; k++)
            {
                var model = lib.Trees[k];
                var set = new InstancedSet(model.Mesh, treeMat, true);
                float norm = World.TreeHeights[k] / model.Height;
                foreach (var p in w.TreeLists[k]) set.Add(Matrix4x4.TRS(Conv.U(p.X, p.Y, p.Z), Conv.Yaw(p.Rot), Vector3.one * (norm * p.Scale)));
                if (set.Count > 0) sets.Add(set);
            }
            for (int k = 0; k < World.RockKinds; k++)
            {
                var model = lib.Rocks[k];
                var set = new InstancedSet(model.Mesh, model.Mat, true);
                float norm = 1f / model.Height;
                foreach (var p in w.RockLists[k]) set.Add(Matrix4x4.TRS(Conv.U(p.X, p.Y, p.Z), Conv.Yaw(p.Rot), Vector3.one * (norm * p.Scale)));
                if (set.Count > 0) sets.Add(set);
            }

            // Трава, камыш и цветы: слитые меши с цветами вершин (без теней, чтобы не тормозило)
            var grassMat = Own(ModelLibrary.NewLit(null, Color.white, "grass"));
            grassMat.SetFloat("_Spec", 0);
            grassMat.SetFloat("_Cull", 0);
            var tuft = WorldMeshes.GrassTuft();
            foreach (var list in new[] { w.Grass, w.Reeds })
                for (int i = 0; i < list.Count; i += 1500)
                    Add("Grass", Own(Conv.ToMesh(WorldMeshes.Scatter(tuft, list, i, Mathf.Min(1500, list.Count - i)), "grass")), grassMat, false);
            if (w.FlowerList.Count > 0)
            {
                var flower = WorldMeshes.Flower();
                for (int i = 0; i < w.FlowerList.Count; i += 2000)
                    Add("Flowers", Own(Conv.ToMesh(WorldMeshes.Scatter(flower, w.FlowerList, i, Mathf.Min(2000, w.FlowerList.Count - i)), "flowers")), grassMat, false);
            }

            // Город (дома и мелочи) — в BuildEnv: они ломаются, меши кусков перестраиваются

            // Замки армий, мельница, сторожевые башни
            foreach (var l in w.Landmarks)
            {
                if (!lib.Buildings.TryGetValue(l.Model, out var sm)) continue;
                var go = Add(l.Model, sm.Mesh, sm.Mat);
                go.transform.SetPositionAndRotation(Conv.U(l.X, l.Y - 0.6f, l.Z), Conv.Yaw(l.Rot));
                go.transform.localScale = Vector3.one * (l.Height / sm.Height);
            }
        }

        /// <summary>Инстансы (деревья, камни) рисуются каждый кадр.</summary>
        public void Draw()
        {
            foreach (var s in sets) s.Draw();
        }

        // ------------------------------------------------------------ окружение, которое меняется в бою (Core/Env.cs)

        /// <summary>Кусок слитого меша: дома и мелочи города (по 40) или ограды (по 16). Рухнуло что-то из него — перестраиваем кусок.</summary>
        sealed class EnvChunk
        {
            public GameObject Go;
            public Mesh Mesh;
            public bool Walls;
            public readonly List<Building> Houses = new List<Building>();
            public readonly List<Prop> Props = new List<Prop>();
            public readonly List<int> WallIdx = new List<int>();
        }

        World world;
        ModelLibrary library;
        Material wallMat;
        readonly List<EnvChunk> envChunks = new List<EnvChunk>();
        readonly Dictionary<EnvObj, EnvChunk> chunkOf = new Dictionary<EnvObj, EnvChunk>();
        readonly HashSet<EnvChunk> dirty = new HashSet<EnvChunk>();

        void BuildEnv()
        {
            envChunks.Clear(); chunkOf.Clear(); dirty.Clear();
            var walls = world.Features.Walls;
            for (int i = 0; i < walls.Count; i += 16)
            {
                var ch = new EnvChunk { Walls = true };
                for (int k = i; k < Mathf.Min(i + 16, walls.Count); k++)
                {
                    ch.WallIdx.Add(k);
                    if (walls[k].Env != null) chunkOf[walls[k].Env] = ch;
                }
                envChunks.Add(ch);
            }
            if (world.Town != null)
            {
                EnvChunk ch = null;
                int n = 0;
                void Next() { if (ch == null || n++ % 40 == 0) envChunks.Add(ch = new EnvChunk()); }
                foreach (var b in world.Town.Buildings) { Next(); ch.Houses.Add(b); if (b.Env != null) chunkOf[b.Env] = ch; }
                foreach (var p in world.Town.Props) { Next(); ch.Props.Add(p); if (p.Env != null) chunkOf[p.Env] = ch; }
            }
            foreach (var ch in envChunks) Rebuild(ch);
        }

        void Rebuild(EnvChunk ch)
        {
            MeshData data;
            if (ch.Walls)
            {
                var bb = new BoxBuilder();
                foreach (int k in ch.WallIdx) WorldMeshes.DryWall(bb, world, world.Features.Walls[k], k);
                data = bb.Count > 0 ? bb.Build() : null;
            }
            else
            {
                var parts = new List<MeshData>();
                foreach (var b in ch.Houses)
                {
                    if (b.Env == null || b.Env.State != EnvState.Ruined) { parts.Add(WorldMeshes.Placed(library.City[b.Def.Name].Data, b.X, b.Y, b.Z, b.Rot, b.S)); continue; }
                    // рухнул: на его месте — развалины из того же набора, длинной стороной вдоль дома и в его пятне
                    var ruin = library.City["destroyed"];
                    bool turn = (b.Hx < b.Hz) != (ruin.W < ruin.D);
                    float rw = Mathf.Max(0.1f, turn ? ruin.D : ruin.W), rd = Mathf.Max(0.1f, turn ? ruin.W : ruin.D);
                    float k = Mathf.Min(b.Hx * 2 / rw, b.Hz * 2 / rd);
                    parts.Add(WorldMeshes.Placed(ruin.Data, b.X, b.Y, b.Z, b.Rot + (turn ? Mathf.PI / 2 : 0), k));
                }
                foreach (var p in ch.Props)
                    if (p.Env == null || p.Env.State != EnvState.Ruined) parts.Add(WorldMeshes.Placed(library.City[p.Def.Name].Data, p.X, p.Y, p.Z, p.Rot, p.S));
                data = parts.Count > 0 ? MeshData.Merge(parts) : null;
            }
            if (ch.Mesh != null) { owned.Remove(ch.Mesh); Object.Destroy(ch.Mesh); ch.Mesh = null; }
            if (data == null) { if (ch.Go != null) ch.Go.SetActive(false); return; }
            ch.Mesh = Own(Conv.ToMesh(data, ch.Walls ? "walls" : "town"));
            if (ch.Go == null) ch.Go = Add(ch.Walls ? "Walls" : "Town", ch.Mesh, ch.Walls ? wallMat : library.CityMat);
            else { ch.Go.GetComponent<MeshFilter>().sharedMesh = ch.Mesh; ch.Go.SetActive(true); }
        }

        /// <summary>Что рухнуло или разбито с прошлого кадра — перестраиваем куски, где оно лежит (раз в кадр, не чаще).</summary>
        public void Sync()
        {
            var env = world?.Env;
            if (env == null || env.Changed.Count == 0) return;
            foreach (var o in env.Changed)
                if (chunkOf.TryGetValue(o, out var ch)) dirty.Add(ch);
            env.Changed.Clear();
            foreach (var ch in dirty) Rebuild(ch);
            dirty.Clear();
        }
    }
}
