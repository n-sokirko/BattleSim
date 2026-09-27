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
    /// При первом открытии проекта создаёт URP, сцену BattleSim и настраивает проект под Android.
    /// Повторить вручную: меню BattleSim → Настроить проект и открыть сцену.
    /// </summary>
    [InitializeOnLoad]
    public static class BattleSimSetup
    {
        const string Root = "Assets/BattleSim";
        const string SettingsDir = Root + "/Settings";
        const string ScenePath = Root + "/BattleSim.unity";
        static readonly string[] Shaders = { "BattleSim/Lit", "BattleSim/Water", "BattleSim/Unlit", "Skybox/Procedural" };

        static BattleSimSetup()
        {
            EditorApplication.delayCall += TryAutoSetup;
        }

        static void TryAutoSetup()
        {
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += TryAutoSetup; return; }
            if (File.Exists(ScenePath)) return;
            Setup();
        }

        [MenuItem("BattleSim/Настроить проект и открыть сцену", priority = 0)]
        public static void Setup()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureFolder(SettingsDir);
            ConfigurePipeline();
            ConfigureProject();
            CreateScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("<b>BattleSim готов!</b> Сцена " + ScenePath + " открыта — нажмите Play ▶");
        }

        /// <summary>Для запуска из командной строки: -executeMethod BattleSim.EditorTools.BattleSimSetup.BatchSetup</summary>
        public static void BatchSetup()
        {
            Setup();
            EditorApplication.Exit(0);
        }

        [MenuItem("BattleSim/Открыть сцену битвы", priority = 1)]
        public static void OpenScene()
        {
            if (!File.Exists(ScenePath)) { Setup(); return; }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        /// <summary>URP: свой ассет конвейера с тенями на 150 м, если в проекте его ещё нет.</summary>
        static void ConfigurePipeline()
        {
            var asset = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (asset == null)
            {
                string rdPath = SettingsDir + "/BattleSimRenderer.asset", apPath = SettingsDir + "/BattleSimURP.asset";
                var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rdPath);
                if (rd == null)
                {
                    rd = ScriptableObject.CreateInstance<UniversalRendererData>();
                    AssetDatabase.CreateAsset(rd, rdPath);
                }
                asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(apPath);
                if (asset == null)
                {
                    asset = UniversalRenderPipelineAsset.Create(rd);
                    AssetDatabase.CreateAsset(asset, apPath);
                }
                GraphicsSettings.defaultRenderPipeline = asset;
            }
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }
            asset.shadowDistance = 160f;
            asset.shadowCascadeCount = 2;
            asset.mainLightShadowmapResolution = 2048;
            asset.supportsHDR = true;
            asset.msaaSampleCount = 1;
            EditorUtility.SetDirty(asset);
        }

        static void ConfigureProject()
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.companyName = "Sokirko";
            PlayerSettings.productName = "Сеча";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.sokirko.secha");

            // Наши шейдеры ищутся по имени — включаем их в сборку
            var gs = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
            var so = new SerializedObject(gs);
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            foreach (var name in Shaders)
            {
                var sh = Shader.Find(name);
                if (sh == null) continue;
                bool has = false;
                for (int i = 0; i < arr.arraySize; i++) if (arr.GetArrayElementAtIndex(i).objectReferenceValue == sh) has = true;
                if (has) continue;
                arr.InsertArrayElementAtIndex(arr.arraySize);
                arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
            }
            // материалы создаются в коде, поэтому сборка не знает, что нужен инстансинг (солдаты, деревья)
            // и какой туман: оставляем все варианты, иначе армии в сборке невидимы
            so.FindProperty("m_InstancingStripping").intValue = 2; // Keep All
            so.FindProperty("m_FogStripping").intValue = 1;        // Custom: все три режима
            so.FindProperty("m_FogKeepLinear").boolValue = true;
            so.FindProperty("m_FogKeepExp").boolValue = true;
            so.FindProperty("m_FogKeepExp2").boolValue = true;
            so.ApplyModifiedProperties();
        }

        static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.003f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            var skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader != null)
            {
                string skyPath = SettingsDir + "/Sky.mat";
                var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
                if (sky == null) { sky = new Material(skyShader); AssetDatabase.CreateAsset(sky, skyPath); }
                RenderSettings.skybox = sky;
            }

            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.intensity = 1.2f;
            sunGo.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
            RenderSettings.sun = sun;

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            cam.farClipPlane = 3000f;
            camGo.AddComponent<UniversalAdditionalCameraData>();
            camGo.transform.position = new Vector3(0f, 50f, 120f);
            camGo.transform.LookAt(Vector3.zero);

            new GameObject("BattleSim").AddComponent<GameMain>();
            EditorSceneManager.SaveScene(scene, ScenePath);

            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == ScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
