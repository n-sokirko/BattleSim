using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BattleSim
{
    /// <summary>Скелет модели: имена костей, родители и точки вращения (в пространстве юнита).</summary>
    public class RigDef
    {
        public string[] Names;
        public int[] Parents;
        public Vector3[] Rest;
        public int Body, ArmR, ArmL;
        public int[] Legs;          // пехота: L, R; конница: FL, FR, BL, BR
        public bool Horse;
        public Bounds Bounds;

        public Matrix4x4[] Bindposes()
        {
            var bp = new Matrix4x4[Rest.Length];
            for (int i = 0; i < Rest.Length; i++) bp[i] = Matrix4x4.Translate(-Rest[i]);
            return bp;
        }

        public Vector3 LocalRest(int i) => Parents[i] < 0 ? Rest[i] : Rest[i] - Rest[Parents[i]];

        public static readonly RigDef Infantry = new RigDef
        {
            Names = new[] { "Body", "LegL", "LegR", "ArmR", "ArmL" },
            Parents = new[] { -1, -1, -1, 0, 0 },
            Rest = new[] { new Vector3(0f, 0.95f, 0f), new Vector3(-0.13f, 0.95f, 0f), new Vector3(0.13f, 0.95f, 0f), new Vector3(0.31f, 1.47f, 0f), new Vector3(-0.31f, 1.47f, 0f) },
            Body = 0, ArmR = 3, ArmL = 4, Legs = new[] { 1, 2 },
            Bounds = new Bounds(new Vector3(0f, 1.1f, 0.4f), new Vector3(2.6f, 3f, 4.2f))
        };

        public static readonly RigDef Rider = new RigDef
        {
            Names = new[] { "Body", "LegFL", "LegFR", "LegBL", "LegBR", "ArmR", "ArmL" },
            Parents = new[] { -1, -1, -1, -1, -1, 0, 0 },
            Rest = new[]
            {
                new Vector3(0f, 1.35f, 0f),
                new Vector3(-0.19f, 1.15f, 0.58f), new Vector3(0.19f, 1.15f, 0.58f),
                new Vector3(-0.19f, 1.15f, -0.58f), new Vector3(0.19f, 1.15f, -0.58f),
                new Vector3(0.3f, 2.33f, -0.1f), new Vector3(-0.3f, 2.33f, -0.1f)
            },
            Body = 0, ArmR = 5, ArmL = 6, Legs = new[] { 1, 2, 3, 4 }, Horse = true,
            Bounds = new Bounds(new Vector3(0f, 1.5f, 0.2f), new Vector3(3.5f, 4.2f, 4.8f))
        };
    }

    /// <summary>Экземпляр модели солдата в сцене.</summary>
    public class UnitView
    {
        public GameObject Go;
        public Transform Root;
        public Transform[] Bones;
        public RigDef Rig;
    }

    /// <summary>Строит (и кэширует) меши солдат из простых фигур.</summary>
    public static class UnitArt
    {
        static readonly Dictionary<int, Mesh> cache = new Dictionary<int, Mesh>();
        static Mesh arrowMesh;
        static readonly MeshKit kit = new MeshKit();

        public const int HorseVariants = 3;

        public static RigDef RigFor(UnitType t) => t == UnitType.Knight ? RigDef.Rider : RigDef.Infantry;

        public static UnitView Create(UnitType type, int team, int variant, Transform parent)
        {
            var rig = RigFor(type);
            var mesh = GetMesh(type, team, variant);

            var go = new GameObject(UnitStats.Of(type).Name);
            var root = go.transform;
            root.SetParent(parent, false);
            var bones = new Transform[rig.Rest.Length];
            for (int i = 0; i < bones.Length; i++) bones[i] = new GameObject(rig.Names[i]).transform;
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i].SetParent(rig.Parents[i] < 0 ? root : bones[rig.Parents[i]], false);
                bones[i].localPosition = rig.LocalRest(i);
                bones[i].localRotation = Quaternion.identity;
            }

            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = bones;
            smr.rootBone = root;
            smr.sharedMaterial = GameMaterials.Units;
            smr.localBounds = rig.Bounds;
            smr.updateWhenOffscreen = false;
            smr.quality = SkinQuality.Bone1;
            smr.shadowCastingMode = ShadowCastingMode.On;

            return new UnitView { Go = go, Root = root, Bones = bones, Rig = rig };
        }

        public static void ClearCache()
        {
            foreach (var m in cache.Values) if (m != null) Object.Destroy(m);
            cache.Clear();
        }

        static Mesh GetMesh(UnitType type, int team, int variant)
        {
            if (type != UnitType.Knight) variant = 0;
            int key = (int)type * 100 + team * 10 + variant;
            if (cache.TryGetValue(key, out var mesh) && mesh != null) return mesh;
            kit.Clear();
            if (type == UnitType.Knight) BuildKnight(team, variant);
            else BuildInfantry(type, team);
            mesh = kit.ToMesh(type + "_" + team, RigFor(type).Bindposes());
            cache[key] = mesh;
            return mesh;
        }

        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        static void Sword(Vector3 hand, float bladeLen)
        {
            kit.M = Matrix4x4.TRS(hand, Quaternion.LookRotation(V(0f, 0.5f, 0.866f), Vector3.up), Vector3.one);
            kit.Set(Palette.Leather).Box(V(0f, 0f, 0f), V(0.045f, 0.045f, 0.2f));
            kit.Set(Palette.Gold).Box(V(0f, 0f, -0.12f), V(0.07f, 0.07f, 0.06f));
            kit.Box(V(0f, 0f, 0.11f), V(0.28f, 0.05f, 0.05f));
            kit.Set(Palette.Steel).Box(V(0f, 0f, 0.14f + bladeLen * 0.5f), V(0.075f, 0.022f, bladeLen));
            kit.Cylinder(V(0f, 0f, 0.14f + bladeLen), V(0f, 0f, 0.26f + bladeLen), 0.04f, 0f, 4);
            kit.M = Matrix4x4.identity;
        }

        static void Arm(int bone, Vector3 shoulder, Vector3 hand, int sleeve)
        {
            kit.Bone = bone;
            kit.Set(sleeve).Cylinder(shoulder, hand + (shoulder - hand).normalized * 0.05f, 0.078f, 0.066f, 5);
            kit.Set(Palette.Skin).Ellipsoid(hand, V(0.068f, 0.068f, 0.068f), 5, 3);
        }

        static void Head(Vector3 c)
        {
            kit.Set(Palette.Skin).Ellipsoid(c, V(0.15f, 0.17f, 0.15f), 7, 5);
            kit.Cylinder(c + V(0f, -0.2f, -0.01f), c + V(0f, -0.1f, -0.01f), 0.07f, 0.07f, 5);
            kit.Set(Palette.Black);
            kit.Box(c + V(-0.055f, 0.02f, 0.145f), V(0.035f, 0.045f, 0.03f));
            kit.Box(c + V(0.055f, 0.02f, 0.145f), V(0.035f, 0.045f, 0.03f));
        }

        static void BuildInfantry(UnitType type, int team)
        {
            var rig = RigDef.Infantry;
            int main = Palette.TeamMain(team), dark = Palette.TeamDark(team), light = Palette.TeamLight(team);

            // Ноги
            for (int s = -1; s <= 1; s += 2)
            {
                kit.Bone = s < 0 ? rig.Legs[0] : rig.Legs[1];
                float x = 0.13f * s;
                kit.Set(dark).Cylinder(V(x, 0.98f, 0f), V(x, 0.14f, 0f), 0.095f, 0.075f, 5);
                kit.Set(Palette.Leather).Box(V(x, 0.08f, 0.04f), V(0.14f, 0.16f, 0.27f));
            }

            // Туловище и голова
            kit.Bone = rig.Body;
            kit.Set(dark).Box(V(0f, 0.93f, 0f), V(0.44f, 0.2f, 0.28f));
            kit.Set(type == UnitType.Archer ? light : main).Box(V(0f, 1.23f, 0f), V(0.48f, 0.56f, 0.3f));
            kit.Set(Palette.Leather).Box(V(0f, 1.0f, 0f), V(0.5f, 0.08f, 0.32f));
            kit.Set(Palette.Gold).Box(V(0f, 1.0f, 0.165f), V(0.08f, 0.08f, 0.02f));
            Vector3 head = V(0f, 1.67f, 0.01f);
            Head(head);

            switch (type)
            {
                case UnitType.Swordsman:
                    kit.Set(Palette.Steel);
                    kit.Box(V(-0.29f, 1.45f, 0f), V(0.2f, 0.12f, 0.32f));
                    kit.Box(V(0.29f, 1.45f, 0f), V(0.2f, 0.12f, 0.32f));
                    kit.Cylinder(V(0f, 1.71f, 0.01f), V(0f, 1.84f, 0.01f), 0.172f, 0.16f, 8);
                    kit.Ellipsoid(V(0f, 1.84f, 0.01f), V(0.16f, 0.08f, 0.16f), 8, 3);
                    kit.Box(V(0f, 1.66f, 0.17f), V(0.03f, 0.11f, 0.025f));
                    // Щит на груди слева
                    kit.Set(main).Box(V(-0.2f, 1.12f, 0.27f), V(0.52f, 0.66f, 0.06f));
                    kit.Set(Palette.Gold).Box(V(-0.2f, 1.12f, 0.305f), V(0.07f, 0.48f, 0.02f));
                    kit.Box(V(-0.2f, 1.2f, 0.305f), V(0.36f, 0.07f, 0.02f));
                    kit.Set(Palette.Steel).Ellipsoid(V(-0.2f, 1.12f, 0.31f), V(0.07f, 0.07f, 0.05f), 6, 3);
                    Arm(rig.ArmR, rig.Rest[rig.ArmR], V(0.33f, 0.92f, 0f), main);
                    Sword(V(0.33f, 0.92f, 0f), 0.8f);
                    Arm(rig.ArmL, rig.Rest[rig.ArmL], V(-0.33f, 0.92f, 0f), main);
                    break;

                case UnitType.Spearman:
                    kit.Set(Palette.Leather).Box(V(0f, 1.25f, 0f), V(0.5f, 0.42f, 0.32f));
                    kit.Set(main).Box(V(0f, 1.25f, 0.005f), V(0.3f, 0.43f, 0.32f));
                    kit.Set(Palette.SteelDark).Cylinder(V(0f, 1.72f, 0.01f), V(0f, 1.77f, 0.01f), 0.2f, 0.2f, 8);
                    kit.Set(Palette.Steel).Cylinder(V(0f, 1.76f, 0.01f), V(0f, 2.02f, 0.01f), 0.17f, 0f, 8);
                    Arm(rig.ArmR, rig.Rest[rig.ArmR], V(0.33f, 0.92f, 0f), main);
                    kit.Bone = rig.ArmR;
                    kit.Set(Palette.Wood).Cylinder(V(0.33f, 0.92f, -0.9f), V(0.33f, 0.92f, 2.0f), 0.03f, 0.03f, 4);
                    kit.Set(Palette.Steel).Cylinder(V(0.33f, 0.92f, 2.0f), V(0.33f, 0.92f, 2.4f), 0.065f, 0f, 4);
                    kit.Set(main).Box(V(0.33f, 0.99f, 1.8f), V(0.012f, 0.13f, 0.24f));
                    Arm(rig.ArmL, rig.Rest[rig.ArmL], V(-0.33f, 0.92f, 0f), main);
                    break;

                case UnitType.Archer:
                    kit.Set(Palette.Leather).Box(V(0f, 1.2f, 0f), V(0.5f, 0.44f, 0.315f));
                    kit.Set(Palette.Hair).Box(V(0f, 1.64f, -0.1f), V(0.29f, 0.24f, 0.1f));
                    kit.Set(main).Cylinder(V(0f, 1.76f, 0f), V(0f, 1.92f, -0.05f), 0.162f, 0.09f, 7);
                    kit.Set(Palette.Fletch).Box(V(0.12f, 1.9f, -0.08f), V(0.02f, 0.22f, 0.06f), Quaternion.Euler(-30f, 0f, 0f));
                    // Колчан за спиной
                    kit.Set(Palette.Leather).Cylinder(V(0.1f, 0.95f, -0.22f), V(0.18f, 1.52f, -0.26f), 0.075f, 0.075f, 6);
                    kit.Set(Palette.Fletch);
                    kit.Box(V(0.16f, 1.6f, -0.24f), V(0.03f, 0.14f, 0.04f));
                    kit.Box(V(0.2f, 1.58f, -0.28f), V(0.03f, 0.14f, 0.04f));
                    kit.Box(V(0.19f, 1.61f, -0.22f), V(0.03f, 0.14f, 0.04f));
                    Arm(rig.ArmR, rig.Rest[rig.ArmR], V(0.33f, 0.92f, 0f), light);
                    Arm(rig.ArmL, rig.Rest[rig.ArmL], V(-0.33f, 0.92f, 0f), light);
                    // Лук в левой руке: при прицеливании рука поднимается и лук встаёт вертикально.
                    kit.Bone = rig.ArmL;
                    Vector3 hand = V(-0.33f, 0.92f, 0f);
                    Vector3 prev = hand + V(0f, 0.16f, -0.72f);
                    kit.Set(Palette.Wood);
                    for (int i = 1; i <= 4; i++)
                    {
                        float sp = -1f + i * 0.5f;
                        Vector3 p = hand + V(0f, 0.16f * sp * sp, 0.72f * sp);
                        kit.Cylinder(prev, p, 0.03f, 0.03f, 4, false);
                        prev = p;
                    }
                    kit.Set(Palette.Bowstring).Cylinder(hand + V(0f, 0.16f, -0.72f), hand + V(0f, 0.16f, 0.72f), 0.01f, 0.01f, 3, false);
                    break;
            }
        }

        static void BuildKnight(int team, int variant)
        {
            var rig = RigDef.Rider;
            int main = Palette.TeamMain(team), dark = Palette.TeamDark(team), light = Palette.TeamLight(team);
            int horse = variant == 1 ? Palette.HorseGrey : variant == 2 ? Palette.HorseBlack : Palette.HorseBrown;

            // Ноги коня
            for (int i = 0; i < 4; i++)
            {
                int b = rig.Legs[i];
                kit.Bone = b;
                Vector3 p = rig.Rest[b];
                kit.Set(horse).Cylinder(p, p + V(0f, -1.02f, 0f), 0.1f, 0.07f, 5);
                kit.Set(Palette.HorseDark).Box(V(p.x, 0.07f, p.z + 0.02f), V(0.15f, 0.14f, 0.19f));
            }

            kit.Bone = rig.Body;
            // Конь
            kit.Set(horse);
            kit.Box(V(0f, 1.4f, 0f), V(0.56f, 0.58f, 1.5f));
            kit.Box(V(0f, 1.36f, 0.72f), V(0.5f, 0.52f, 0.3f));
            kit.Box(V(0f, 1.42f, -0.7f), V(0.54f, 0.54f, 0.3f));
            kit.Box(V(0f, 1.85f, 0.9f), V(0.3f, 0.78f, 0.36f), Quaternion.Euler(30f, 0f, 0f));
            kit.Box(V(0f, 2.08f, 1.28f), V(0.26f, 0.3f, 0.62f), Quaternion.Euler(25f, 0f, 0f));
            kit.Box(V(-0.08f, 2.36f, 1.08f), V(0.05f, 0.15f, 0.05f));
            kit.Box(V(0.08f, 2.36f, 1.08f), V(0.05f, 0.15f, 0.05f));
            kit.Set(Palette.Black);
            kit.Box(V(-0.135f, 2.16f, 1.2f), V(0.02f, 0.05f, 0.05f));
            kit.Box(V(0.135f, 2.16f, 1.2f), V(0.02f, 0.05f, 0.05f));
            kit.Set(Palette.HorseDark);
            kit.Box(V(0f, 1.95f, 0.74f), V(0.08f, 0.74f, 0.14f), Quaternion.Euler(30f, 0f, 0f));
            kit.Box(V(0f, 1.35f, -0.95f), V(0.1f, 0.62f, 0.12f), Quaternion.Euler(22f, 0f, 0f));
            // Попона и седло
            kit.Set(main).Box(V(0f, 1.28f, -0.02f), V(0.62f, 0.4f, 1.56f));
            kit.Set(light).Box(V(0f, 1.1f, -0.02f), V(0.63f, 0.05f, 1.57f));
            kit.Set(Palette.Leather).Box(V(0f, 1.73f, -0.05f), V(0.5f, 0.1f, 0.55f));

            // Всадник
            kit.Set(dark);
            kit.Box(V(-0.32f, 1.6f, 0.05f), V(0.13f, 0.5f, 0.16f));
            kit.Box(V(0.32f, 1.6f, 0.05f), V(0.13f, 0.5f, 0.16f));
            kit.Box(V(0f, 1.82f, -0.1f), V(0.44f, 0.2f, 0.3f));
            kit.Set(Palette.Leather);
            kit.Box(V(-0.33f, 1.33f, 0.1f), V(0.14f, 0.14f, 0.25f));
            kit.Box(V(0.33f, 1.33f, 0.1f), V(0.14f, 0.14f, 0.25f));
            kit.Set(main).Box(V(0f, 2.08f, -0.1f), V(0.48f, 0.54f, 0.3f));
            kit.Set(Palette.Steel);
            kit.Box(V(-0.28f, 2.33f, -0.1f), V(0.2f, 0.12f, 0.32f));
            kit.Box(V(0.28f, 2.33f, -0.1f), V(0.2f, 0.12f, 0.32f));
            kit.Box(V(0f, 2.57f, -0.09f), V(0.32f, 0.36f, 0.32f));
            kit.Set(Palette.Black).Box(V(0f, 2.59f, 0.075f), V(0.24f, 0.03f, 0.02f));
            kit.Set(Palette.Gold).Box(V(0f, 2.76f, -0.09f), V(0.34f, 0.04f, 0.34f));
            kit.Set(main).Ellipsoid(V(0f, 2.86f, -0.12f), V(0.06f, 0.12f, 0.2f), 6, 3);
            // Щит всадника
            kit.Set(main).Box(V(-0.37f, 2.0f, 0f), V(0.06f, 0.56f, 0.42f));
            kit.Set(Palette.Gold).Box(V(-0.405f, 2.0f, 0f), V(0.012f, 0.32f, 0.08f));
            kit.Box(V(-0.405f, 2.06f, 0f), V(0.012f, 0.08f, 0.26f));

            Arm(rig.ArmR, rig.Rest[rig.ArmR], V(0.34f, 1.8f, -0.1f), main);
            Sword(V(0.34f, 1.8f, -0.1f), 1.0f);
            Arm(rig.ArmL, rig.Rest[rig.ArmL], V(-0.3f, 1.86f, 0.12f), main);
        }

        public static Mesh ArrowMesh()
        {
            if (arrowMesh != null) return arrowMesh;
            kit.Clear();
            kit.Set(Palette.Wood).Box(V(0f, 0f, 0f), V(0.03f, 0.03f, 0.8f));
            kit.Set(Palette.Steel).Cylinder(V(0f, 0f, 0.4f), V(0f, 0f, 0.52f), 0.035f, 0f, 4);
            kit.Set(Palette.Fletch);
            kit.Box(V(0f, 0f, -0.34f), V(0.1f, 0.006f, 0.12f));
            kit.Box(V(0f, 0f, -0.34f), V(0.006f, 0.1f, 0.12f));
            arrowMesh = kit.ToMesh("Arrow");
            return arrowMesh;
        }
    }
}
