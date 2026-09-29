using UnityEditor;
using UnityEngine;

namespace BattleSim.EditorTools
{
    /// <summary>Сборка игры: Windows (для проверки) и APK для Android. Меню BattleSim или командная строка (-executeMethod).</summary>
    public static class BattleSimBuild
    {
        const string ScenePath = "Assets/BattleSim/BattleSim.unity";

        /// <summary>Куда класть сборку: -customBuildPath из командной строки (так его передаёт GameCI в CI) или путь по умолчанию.</summary>
        static string OutPath(string fallback)
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-customBuildPath");
            return i >= 0 && i + 1 < args.Length && args[i + 1].Length > 0 && !args[i + 1].StartsWith("-") ? args[i + 1] : fallback;
        }

        [MenuItem("BattleSim/Собрать для Windows", priority = 20)]
        public static void BuildWindows()
        {
            BattleSimSetup.IncludeShaders();
            var r = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = OutPath("Builds/Windows/Secha.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            Debug.Log("Сборка Windows: " + r.summary.result + ", " + r.summary.totalSize / (1024 * 1024) + " МБ");
            if (Application.isBatchMode) EditorApplication.Exit(r.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }

        [MenuItem("BattleSim/Собрать APK для Android", priority = 21)]
        public static void BuildAndroid()
        {
            BattleSimSetup.IncludeShaders();
            EditorUserBuildSettings.buildAppBundle = false;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            var r = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = OutPath("Builds/Android/Secha.apk"),
                target = BuildTarget.Android,
                options = BuildOptions.None,
            });
            Debug.Log("Сборка Android: " + r.summary.result + ", " + r.summary.totalSize / (1024 * 1024) + " МБ");
            if (Application.isBatchMode) EditorApplication.Exit(r.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }
    }
}
