using UnityEditor;
using UnityEngine;

namespace BattleSim.EditorTools
{
    /// <summary>Сборка игры: Windows (для проверки) и APK для Android. Меню BattleSim или командная строка (-executeMethod).</summary>
    public static class BattleSimBuild
    {
        const string ScenePath = "Assets/BattleSim/BattleSim.unity";

        [MenuItem("BattleSim/Собрать для Windows", priority = 20)]
        public static void BuildWindows()
        {
            var r = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/Windows/Secha.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            Debug.Log("Сборка Windows: " + r.summary.result + ", " + r.summary.totalSize / (1024 * 1024) + " МБ");
            if (Application.isBatchMode) EditorApplication.Exit(r.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }

        [MenuItem("BattleSim/Собрать APK для Android", priority = 21)]
        public static void BuildAndroid()
        {
            EditorUserBuildSettings.buildAppBundle = false;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            var r = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/Android/Secha.apk",
                target = BuildTarget.Android,
                options = BuildOptions.None,
            });
            Debug.Log("Сборка Android: " + r.summary.result + ", " + r.summary.totalSize / (1024 * 1024) + " МБ");
            if (Application.isBatchMode) EditorApplication.Exit(r.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }
    }
}
