using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace WingingIt.Editor
{
    /// Command-line friendly player builds. Outputs land in <project>/Builds/<Platform>/.
    /// Call from the CLI or eval, e.g. PlayerBuilder.BuildAndroid(). No menu items on purpose: menus can block on dialogs.
    public static class PlayerBuilder
    {
        const string ProductName = "Planes";

        public static void BuildWindows()
        {
            string outputDir = Path.Combine(OutputRoot(), "Windows");
            Build(BuildTarget.StandaloneWindows64, Path.Combine(outputDir, ProductName + ".exe"));
        }

        public static void BuildAndroid()
        {
            string outputDir = Path.Combine(OutputRoot(), "Android");
            Build(BuildTarget.Android, Path.Combine(outputDir, ProductName + ".apk"));
        }

        public static void BuildAll()
        {
            BuildWindows();
            BuildAndroid();
        }

        static void Build(BuildTarget target, string locationPathName)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(locationPathName));

            var scenes = new List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled) scenes.Add(scene.path);
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = locationPathName,
                target = target,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            Debug.Log($"[PlayerBuilder] {target}: {summary.result} in {summary.totalTime.TotalSeconds:F0}s, " +
                      $"{summary.totalErrors} errors -> {locationPathName} ({summary.totalSize / (1024 * 1024)} MB)");
        }

        static string OutputRoot()
        {
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds");
        }
    }
}
