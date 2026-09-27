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
            var walls = WorldMeshes.DryWalls(w);
            if (walls != null) Add("Walls", Own(Conv.ToMesh(walls, "walls")), white);
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

            // Город: все дома и мелочи — в один меш с общей текстурой
            if (w.Town != null)
            {
                var parts = new List<MeshData>();
                foreach (var b in w.Town.Buildings) parts.Add(WorldMeshes.Placed(lib.City[b.Def.Name].Data, b.X, b.Y, b.Z, b.Rot, b.S));
                foreach (var p in w.Town.Props) parts.Add(WorldMeshes.Placed(lib.City[p.Def.Name].Data, p.X, p.Y, p.Z, p.Rot, p.S));
                for (int i = 0; i < parts.Count; i += 40)
                {
                    var chunk = MeshData.Merge(parts.GetRange(i, Mathf.Min(40, parts.Count - i)));
                    Add("Town", Own(Conv.ToMesh(chunk, "town")), lib.CityMat);
                }
            }

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
    }
}
