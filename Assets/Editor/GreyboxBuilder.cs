using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SpatialRhythm.EditorTools
{
    /// <summary>
    /// 灰盒的一键出包。Windows 播放器只用于 Editor 阶段的冒烟验证；
    /// P0 的三条指标全部只有 iOS 真机能测。
    /// </summary>
    public static class GreyboxBuilder
    {
        private const string ScenePath = "Assets/Scenes/Greybox.unity";

        [MenuItem("SpatialRhythm/构建 Windows 冒烟包")]
        public static void BuildWindows()
        {
            string output = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "Build", "Windows", "SpatialRhythmGreybox.exe"));

            Directory.CreateDirectory(Path.GetDirectoryName(output));

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);

            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[GreyboxBuilder] 构建成功：{output}");
            }
            else
            {
                Debug.LogError($"[GreyboxBuilder] 构建失败：{report.summary.result}，" +
                               $"错误 {report.summary.totalErrors} 个");
            }
        }
    }
}
