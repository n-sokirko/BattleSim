using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BattleSim.Core;
using UnityEngine;

namespace BattleSim
{
    /// <summary>Статичная модель одним мешем: высота исходной модели и материал.</summary>
    public sealed class StaticModel
    {
        public Mesh Mesh;
        public Material Mat;
        public float Height, W, D;
        public MeshData Data;
    }

    /// <summary>
    /// Модели KayKit и Quaternius (CC0) из Resources/Models: GLB и PNG в виде .bytes.
    /// Солдаты собираются в один меш, анимации запекаются, армии перекрашиваются.
    /// </summary>
    public sealed class ModelLibrary
    {
        /// <summary>Пехота по типу бойца (Defs.All), всадники — тоже по типу.</summary>
        public Dictionary<int, CrowdModel> Inf = new Dictionary<int, CrowdModel>();
        public Dictionary<int, CrowdModel> Rider = new Dictionary<int, CrowdModel>();
        public CrowdModel[] Horse = new CrowdModel[2];
        public V3[] Saddle = new V3[2];
        public float RiderHipsY = 0.8f;
        public StaticModel Bolt;
        public V3 BoltAxis;
        public float BoltNorm;
        public StaticModel[] Trees, Rocks;
        public List<CityDef> CityDefs = new List<CityDef>();
        public Dictionary<string, StaticModel> City = new Dictionary<string, StaticModel>();
        public Dictionary<string, StaticModel> Buildings = new Dictionary<string, StaticModel>();
        public Material CityMat, WorldMat, RockMat;
        readonly Dictionary<LeafShift, Material> foliage = new Dictionary<LeafShift, Material>();
        byte[] worldTexRgba;
        int worldTexW, worldTexH;

        readonly Dictionary<string, GltfDoc> docs = new Dictionary<string, GltfDoc>();
        readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();

        public static Shader LitShader => Shader.Find("BattleSim/Lit");

        public static Material NewLit(Texture tex, Color color, string name = "lit")
        {
            var m = new Material(LitShader) { name = name };
            m.SetTexture("_BaseMap", tex != null ? tex : Texture2D.whiteTexture);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Spec", 0.05f);
            return m;
        }

        GltfDoc Doc(string name)
        {
            if (docs.TryGetValue(name, out var d)) return d;
            var ta = Resources.Load<TextAsset>("Models/" + name);
            if (ta == null) throw new System.Exception("Нет модели " + name);
            d = GltfDoc.Parse(ta.bytes);
            docs[name] = d;
            return d;
        }

        /// <summary>RGBA картинки (строки сверху вниз, как в canvas) — для перекраски.</summary>
        byte[] Rgba(string png, out int w, out int h)
        {
            var ta = Resources.Load<TextAsset>("Models/" + png);
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            t.LoadImage(ta.bytes);
            w = t.width; h = t.height;
            var px = t.GetPixels32();
            Object.Destroy(t);
            var o = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = px[(h - 1 - y) * w + x];
                    int i = (y * w + x) * 4;
                    o[i] = c.r; o[i + 1] = c.g; o[i + 2] = c.b; o[i + 3] = c.a;
                }
            return o;
        }

        static Texture2D TexFromRgba(byte[] rgba, int w, int h, string name)
        {
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    px[(h - 1 - y) * w + x] = new Color32(rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]);
                }
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, anisoLevel = 4 };
            t.SetPixels32(px);
            t.Apply(true, true);
            return t;
        }

        Texture2D Tex(string png)
        {
            if (textures.TryGetValue(png, out var t)) return t;
            var rgba = Rgba(png, out int w, out int h);
            t = TexFromRgba(rgba, w, h, png);
            textures[png] = t;
            return t;
        }

        string ImageOf(GltfDoc d, MeshData m) => m.Image >= 0 && m.Image < d.Images.Count ? d.Images[m.Image] : null;

        readonly HashSet<string> loadedRaces = new HashSet<string>();

        /// <summary>Шаги загрузки моделей одной расы: три вида пехоты, конница и вожди.</summary>
        List<(string, System.Action)> RaceSteps(RaceDef rc)
        {
            var steps = new List<(string, System.Action)>();
            if (!loadedRaces.Add(rc.Key)) return steps;
            for (int i = 0; i < 3; i++)
            {
                var t = rc.Units[i];
                // конные бывают и не на месте конницы (батыры Степи — ударные): им всадник, а не пехотинец
                steps.Add((rc.Name + ": " + t.Name.ToLowerInvariant(), () => { if (t.Mount) Rider[t.Id] = MakeRider(t, 1.75f); else Inf[t.Id] = MakeInfantry(t); }));
            }
            if (rc.Hero != null) steps.Add((rc.Name + ": " + rc.Hero.Name.ToLowerInvariant(), () => Inf[rc.Hero.Id] = MakeInfantry(rc.Hero)));
            steps.Add((rc.Name + ": конница и вожди", () =>
            {
                Rider[rc.Units[3].Id] = MakeRider(rc.Units[3], 1.75f);
                Rider[rc.Cmd.Id] = MakeRider(rc.Cmd, 1.85f);
                Rider[rc.Msg.Id] = MakeRider(rc.Msg, 1.7f);
                Rider[rc.Cap.Id] = MakeRider(rc.Cap, 1.8f);
            }));
            return steps;
        }

        /// <summary>
        /// Модели расы — по требованию (при первом выборе расы): на телефоне все расы сразу — это лишние
        /// секунды загрузки и десятки мегабайт запечённых анимаций в памяти.
        /// </summary>
        public bool EnsureRace(RaceDef rc)
        {
            var steps = RaceSteps(rc);
            foreach (var s in steps) s.Item2();
            return steps.Count > 0;
        }

        /// <summary>Грузит всё по шагам (для полосы загрузки). progress — 0..1. Расы — только нужные на старте.</summary>
        public IEnumerator Load(System.Action<float, string> progress, IEnumerable<RaceDef> races)
        {
            var steps = new List<(string, System.Action)>();
            foreach (var race in races) steps.AddRange(RaceSteps(race));
            steps.Add(("Кони", () => { for (int h = 0; h < 2; h++) MakeHorse(h); }));
            steps.Add(("Посадка всадника", () =>
            {
                var knight = Doc("Knight");
                var rider = ModelKit.PrepareCharacter(knight, Defs.Types[3].Keep, 1.75f);
                RiderHipsY = ModelKit.NodeWorld(rider, "hips", new ClipLayer { Src = knight, Anim = knight.Anim("Sit_Chair_Idle"), Filter = s => !ModelKit.UpperBones.IsMatch(s) }, 0).y;
            }));
            steps.Add(("Лес и камни", MakeNature));
            steps.Add(("Город", MakeCity));
            steps.Add(("Замки", MakeBuildings));
            for (int i = 0; i < steps.Count; i++)
            {
                progress((float)i / steps.Count, steps[i].Item1);
                yield return null;
                steps[i].Item2();
            }
            progress(1, "Готово");
        }

        readonly Dictionary<string, Material[]> teamMats = new Dictionary<string, Material[]>();

        Material[] TeamMats(string model, string png, RaceDef race = null)
        {
            string key = model + "|" + (race?.Key ?? "");
            if (teamMats.TryGetValue(key, out var cached)) return cached;
            var mats = new Material[2];
            teamMats[key] = mats;
            var rgba0 = Rgba(png, out int w, out int h);
            ModelKit.RecolorRace(rgba0, w, h, model, race);
            for (int team = 0; team < 2; team++)
            {
                var rgba = (byte[])rgba0.Clone();
                ModelKit.RecolorCells(rgba, w, h, ModelKit.TeamCells(model), Defs.Teams[team]);
                var tex = TexFromRgba(rgba, w, h, model + "_team" + team);
                mats[team] = NewLit(tex, Conv.Col(ModelKit.TeamTint(Defs.Teams[team])), model + team);
                mats[team].SetFloat("_Spec", 0.12f);
            }
            return mats;
        }

        CrowdModel MakeInfantry(UnitDef t)
        {
            var doc = Doc(t.Model);
            var model = ModelKit.PrepareCharacter(doc, t.Keep, t.Mount ? 1.75f : 1.9f);
            var a = t.Anim;
            var defs = new List<ClipDef>();
            var seen = new HashSet<string>();
            void Add(string name, bool loop)
            {
                if (name == null || seen.Contains(name) || doc.Anim(name) == null) return;
                seen.Add(name);
                defs.Add(new ClipDef { Name = name, Loop = loop, Layers = { new ClipLayer { Src = doc, Anim = doc.Anim(name) } } });
            }
            foreach (var n in new[] { a.Idle, a.Run, "Walking_A", "Running_B", a.Cheer, a.Aim, a.Reload }) Add(n, true);
            foreach (var n in a.Attack.Concat(new[] { a.Melee, "Death_A", "Death_B", "Hit_A", "Hit_B", "Block_Hit", "Lie_Down", "Lie_StandUp" })) Add(n, false);
            // свои клипы (сделаны в Blender): стена щитов у мечников Руси, мертвецы Нави встают из земли
            if (t.ShieldWall)
            {
                Add("Shield_Wall_Idle", true); Add("Shield_Wall_Walk", true);
                if (!seen.Contains("Shield_Wall_Idle")) Add("Blocking", true);
            }
            if (t.Race != null && t.Race.Undead && t.Slot == 0) Add("Rise_Undead", false);
            return new CrowdModel(model, defs, TeamMats(t.Model, ImageOf(doc, model.Mesh), t.Race));
        }

        CrowdModel MakeRider(UnitDef t, float height)
        {
            var doc = Doc(t.Model);
            var knight = Doc("Knight");
            var model = ModelKit.PrepareCharacter(doc, t.Keep, height);
            System.Func<string, bool> upper = s => ModelKit.UpperBones.IsMatch(s), lower = s => !ModelKit.UpperBones.IsMatch(s);
            var sit = new ClipLayer { Src = knight, Anim = knight.Anim("Sit_Chair_Idle"), Filter = lower };
            var defs = new List<ClipDef> { new ClipDef { Name = "ride", Layers = { new ClipLayer { Src = knight, Anim = knight.Anim("Idle"), Filter = upper }, sit } } };
            var atk = t.Anim.Attack.Length > 0 ? t.Anim.Attack : Defs.Types[3].Anim.Attack;
            for (int i = 0; i < atk.Length; i++)
                defs.Add(new ClipDef { Name = "atk" + i, Loop = false, Layers = { new ClipLayer { Src = knight, Anim = knight.Anim(atk[i]), Filter = upper }, sit } });
            defs.Add(new ClipDef { Name = "Death_A", Loop = false, Layers = { new ClipLayer { Src = knight, Anim = knight.Anim("Death_A") } } });
            defs.Add(new ClipDef { Name = "Death_B", Loop = false, Layers = { new ClipLayer { Src = knight, Anim = knight.Anim("Death_B") } } });
            return new CrowdModel(model, defs, TeamMats(t.Model, ImageOf(doc, model.Mesh), t.Race));
        }

        void MakeHorse(int i)
        {
            var doc = Doc(i == 0 ? "Horse" : "White_Horse");
            var model = ModelKit.PrepareAnimal(doc, 2.9f);
            var defs = new[] { "Idle", "Walk", "Gallop", "Death" }.Select(n => new ClipDef { Name = n, Loop = n != "Death", Layers = { new ClipLayer { Src = doc, Anim = doc.Anim(n) } } }).ToList();
            var mat = NewLit(null, Color.white, "horse" + i);
            mat.SetFloat("_Spec", 0.08f);
            Horse[i] = new CrowdModel(model, defs, new[] { mat });
            Saddle[i] = new V3(0, model.Max.y * 0.8f, (model.Min.z + model.Max.z) / 2 - 0.1f);
        }

        StaticModel Static(string name, Material mat)
        {
            var doc = Doc(name);
            var data = ModelKit.MergeStatic(doc, out float w, out float d, out float h);
            var flipped = data.Clone();
            Conv.FlipV(flipped);
            return new StaticModel { Mesh = Conv.ToMesh(flipped, name), Mat = mat, Height = h, W = w, D = d, Data = flipped };
        }

        void MakeNature()
        {
            worldTexRgba = Rgba("hexagons_medieval.png", out worldTexW, out worldTexH);
            WorldMat = NewLit(Tex("hexagons_medieval.png"), Color.white, "world");
            RockMat = NewLit(Tex("hexagons_medieval.png"), Conv.Col(Rgb.Hex(0x8f877c)), "rock");
            Trees = World.TreeModels.Select(n => Static(n, WorldMat)).ToArray();
            Rocks = World.RockModels.Select(n => Static(n, RockMat)).ToArray();
            // Болт арбалета: вытянут вдоль самой длинной оси
            Bolt = Static("arrow", NewLit(Tex("rogue_texture.png"), Color.white, "bolt"));
            Bolt.Data.Bounds(out var mn, out var mx);
            float ax = mx.x - mn.x, ay = mx.y - mn.y, az = mx.z - mn.z;
            BoltAxis = ax > ay && ax > az ? new V3(1, 0, 0) : az > ay ? new V3(0, 0, 1) : new V3(0, 1, 0);
            BoltNorm = 0.95f / Mathf.Max(ax, Mathf.Max(ay, az));
        }

        /// <summary>Материал деревьев под биом: осень, зима, степь.</summary>
        public Material TreeMaterial(LeafShift leaf)
        {
            if (leaf == LeafShift.Summer) return WorldMat;
            if (foliage.TryGetValue(leaf, out var m)) return m;
            var rgba = (byte[])worldTexRgba.Clone();
            ModelKit.RecolorFoliage(rgba, leaf);
            m = NewLit(TexFromRgba(rgba, worldTexW, worldTexH, "foliage_" + leaf), Color.white, "trees_" + leaf);
            foliage[leaf] = m;
            return m;
        }

        void MakeCity()
        {
            CityMat = NewLit(Tex("hexagons_medieval.png"), Color.white, "city");
            foreach (var (kind, name) in CityPlan.Models)
            {
                var sm = Static("city_" + name, CityMat);
                City[name] = sm;
                CityDefs.Add(new CityDef { Kind = kind, Name = name, W = sm.W, D = sm.D, H = sm.Height, Index = CityDefs.Count });
            }
        }

        void MakeBuildings()
        {
            foreach (var n in new[] { "castle_blue", "castle_red", "windmill", "tower" })
                Buildings[n] = Static(n, CityMat);
        }
    }
}
