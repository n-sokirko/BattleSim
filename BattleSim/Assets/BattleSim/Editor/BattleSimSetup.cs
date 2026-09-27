using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BattleSim.EditorTools
{
    /// <summary>
    /// При первом открытии проекта создаёт сцену BattleSim, материалы и настраивает проект под Android.
    /// Повторить вручную: меню BattleSim → Настроить проект и открыть сцену.
    /// </summary>
    [InitializeOnLoad]
    public static class BattleSimSetup
    {
        const string Root = "Assets/BattleSim";
        const string ResDir = Root + "/Resources";
        const string ScenePath = Root + "/BattleSim.unity";

        static BattleSimSetup()
        {
            EditorApplication.delayCall += TryAutoSetup;
        }

        static string AutoKey => "BattleSim.AutoSetup." + Application.dataPath.GetHashCode();

        static void TryAutoSetup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryAutoSetup;
                return;
            }
            if (File.Exists(ScenePath) || EditorPrefs.GetBool(AutoKey, false)) return;
            EditorPrefs.SetBool(AutoKey, true);
            Setup();
        }

        [MenuItem("BattleSim/Настроить проект и открыть сцену", priority = 0)]
        public static void Setup()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            if (GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset == null)
            {
                EditorUtility.DisplayDialog("BattleSim",
                    "В проекте не включён URP (Universal Render Pipeline).\n\n" +
                    "Проще всего создать новый проект в Unity Hub по шаблону «Universal 3D» и скопировать туда папку BattleSim.",
                    "Понятно");
            }

            EnsureFolder(Root);
            EnsureFolder(ResDir);
            CreateMaterials();
            CreateScene();
            ConfigureProject();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("<b>BattleSim готов!</b> Сцена " + ScenePath + " открыта — нажмите Play ▶");
        }

        [MenuItem("BattleSim/Открыть сцену битвы", priority = 1)]
        public static void OpenScene()
        {
            if (!File.Exists(ScenePath)) { Setup(); return; }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void CreateMaterials()
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) lit = Shader.Find("Standard");

            CreateIfMissing(ResDir + "/" + GameMaterials.LitName + ".mat", () =>
            {
                var m = new Material(lit);
                GameMaterials.Configure(m, false);
                return m;
            });
            CreateIfMissing(ResDir + "/" + GameMaterials.WaterName + ".mat", () =>
            {
                var m = new Material(lit);
                GameMaterials.Configure(m, true);
                GameMaterials.SetColor(m, new Color(0.13f, 0.40f, 0.50f, 0.78f));
                return m;
            });
            Shader sky = Shader.Find("Skybox/Procedural");
            if (sky != null) CreateIfMissing(ResDir + "/" + GameMaterials.SkyName + ".mat", () => new Material(sky));
        }

        static void CreateIfMissing(string path, System.Func<Material> make)
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
            AssetDatabase.CreateAsset(make(), path);
        }

        static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var sky = AssetDatabase.LoadAssetAtPath<Material>(ResDir + "/" + GameMaterials.SkyName + ".mat");
            if (sky != null) RenderSettings.skybox = sky;
            // Туман включён в сцене — так Unity сохранит нужные варианты шейдеров в сборке.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.003f;
            RenderSettings.fogColor = new Color(0.7f, 0.79f, 0.88f);
            RenderSettings.ambientMode = AmbientMode.Trilight;

            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.intensity = 1.4f;
            sunGo.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
            RenderSettings.sun = sun;

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            cam.farClipPlane = 1400f;
            cam.transform.position = new Vector3(0f, 50f, -120f);
            cam.transform.LookAt(Vector3.zero);
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;

            new GameObject("BattleSim").AddComponent<BattleGame>();

            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        static void ConfigureProject()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == ScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();

            // Только горизонтальная ориентация экрана
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            // Тени дальше, чтобы солдаты отбрасывали их при обзоре издалека
            var assets = new HashSet<UniversalRenderPipelineAsset>();
            if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset def) assets.Add(def);
            for (int i = 0; i < QualitySettings.names.Length; i++)
                if (QualitySettings.GetRenderPipelineAssetAt(i) is UniversalRenderPipelineAsset q) assets.Add(q);
            foreach (var a in assets)
            {
                if (a.shadowDistance < 150f)
                {
                    a.shadowDistance = 150f;
                    EditorUtility.SetDirty(a);
                }
            }
        }
    }
}
