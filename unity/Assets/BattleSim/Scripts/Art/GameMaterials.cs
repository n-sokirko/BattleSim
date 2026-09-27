using UnityEngine;
using UnityEngine.Rendering;

namespace BattleSim
{
    /// <summary>
    /// Материалы игры. Шаблоны лежат в Resources (их создаёт меню BattleSim → Настроить проект),
    /// поэтому нужные шейдеры гарантированно попадают в сборку под Android.
    /// Если шаблонов нет, материалы создаются из шейдера URP Lit прямо в игре.
    /// </summary>
    public static class GameMaterials
    {
        public const string LitName = "BattleSimLit";
        public const string WaterName = "BattleSimWater";
        public const string SkyName = "BattleSimSky";

        public static Material Lit { get; private set; }
        public static Material Water { get; private set; }
        public static Material Units { get; private set; }

        static Texture2D palette;

        public static void Init()
        {
            Lit = LoadOrCreate(LitName, false);
            Water = LoadOrCreate(WaterName, true);
            Units = new Material(Lit) { name = "Units" };
            SetFloat(Units, "_Smoothness", 0.22f);
        }

        public static void ApplyPalette(WorldStyle style)
        {
            if (palette != null) Object.Destroy(palette);
            palette = Palette.Build(style);
            SetMainTexture(Units, palette);
        }

        static Material LoadOrCreate(string name, bool transparent)
        {
            var res = Resources.Load<Material>(name);
            if (res != null) return new Material(res) { name = name };

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null)
            {
                Debug.LogError("BattleSim: не найден шейдер URP Lit. Создайте проект по шаблону Universal 3D.");
                shader = Shader.Find("Hidden/InternalErrorShader");
            }
            var m = new Material(shader) { name = name };
            Configure(m, transparent);
            return m;
        }

        /// <summary>Общая настройка материала (используется и в игре, и в редакторе при создании ассетов).</summary>
        public static void Configure(Material m, bool transparent)
        {
            SetColor(m, Color.white);
            SetFloat(m, "_Metallic", 0f);
            SetFloat(m, "_Smoothness", transparent ? 0.93f : 0.12f);
            SetFloat(m, "_Glossiness", transparent ? 0.93f : 0.12f);
            if (transparent) MakeTransparent(m);
        }

        public static void MakeTransparent(Material m)
        {
            bool builtIn = m.shader != null && m.shader.name == "Standard";
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            SetFloat(m, "_SrcBlend", (float)BlendMode.SrcAlpha);
            SetFloat(m, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            SetFloat(m, "_ZWrite", 0f);
            if (builtIn)
            {
                SetFloat(m, "_Mode", 3f);
                m.EnableKeyword("_ALPHABLEND_ON");
                return;
            }
            SetFloat(m, "_Surface", 1f);
            SetFloat(m, "_Blend", 0f);
            SetFloat(m, "_SrcBlendAlpha", (float)BlendMode.One);
            SetFloat(m, "_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetShaderPassEnabled("DepthOnly", false);
            m.SetShaderPassEnabled("ShadowCaster", false);
        }

        public static void SetMainTexture(Material m, Texture tex)
        {
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
        }

        public static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        public static void SetFloat(Material m, string prop, float v)
        {
            if (m.HasProperty(prop)) m.SetFloat(prop, v);
        }
    }
}
