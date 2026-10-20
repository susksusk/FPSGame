using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FpsGame.EditorTools
{
    /// <summary>
    /// iOS 打包：Windows 上导出 Xcode 工程（最终 ipa 需要 Mac + Xcode 编译）。
    /// 菜单：FPS → 构建 iOS 包 (Xcode 工程)。
    /// 仅使用跨平台 PlayerSettings API——未安装 iOS 模块时本脚本仍可编译，
    /// 但执行构建前必须先在 Unity Hub 里给 2022.3.48f1c1 添加 iOS Build Support 模块。
    /// </summary>
    public static class FpsBuildScript
    {
        private const string OutputDir = "Builds/iOS";
        private const string Scene = "Assets/Scenes/GameScene.unity";

        [MenuItem("FPS/构建 iOS 包 (Xcode 工程)")]
        public static void BuildIOS()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS)
            {
                Debug.Log("正在切换到 iOS 平台（首次切换需要一些时间，期间编辑器会重载）…");
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS))
                {
                    Debug.LogError("切换 iOS 平台失败：请先在 Unity Hub → 安装 → 2022.3.48f1c1 → 添加模块，勾选 iOS Build Support");
                    return;
                }
            }

            ApplyPlayerSettings();

            var options = new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = OutputDir,
                target = BuildTarget.iOS,
                options = BuildOptions.None
            };

            Debug.Log($"开始导出 iOS Xcode 工程到 {OutputDir} …");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"iOS Xcode 工程导出完成：{Path.GetFullPath(OutputDir)}\n" +
                          "下一步：把整个 Builds/iOS 文件夹拷到 Mac，用 Xcode 打开 Unity-iPhone.xcodeproj，" +
                          "Signing & Capabilities 里选好 Team 后即可 Build 到真机 / 出 ipa。");
                EditorUtility.RevealInFinder(OutputDir);
            }
            else
            {
                Debug.LogError($"iOS 构建失败：{report.summary.result}，共 {report.summary.totalErrors} 个错误（详见 Console）");
            }
        }

        /// <summary>打包设置：包名、IL2CPP、横屏（避免依赖 iOS 专属扩展 API）。</summary>
        private static void ApplyPlayerSettings()
        {
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.iOS, "com.lanfps.game");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);

            // 横屏 FPS：左右横屏允许，竖屏关闭
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
        }

        /// <summary>
        /// Info.plist 后处理：
        /// NSLocalNetworkUsageDescription —— 局域网 UDP 发现/联机对战必需（iOS 14+ 缺失时发广播会被静默拒绝）；
        /// ITSAppUsesNonExemptEncryption —— 加密合规声明（TestFlight/AppStore 提审需要，本项目未用加密）。
        /// </summary>
        private class IosPlistPostprocess : IPostprocessBuildWithReport
        {
            public int callbackOrder => 100;

            public void OnPostprocessBuild(BuildReport report)
            {
                if (report.summary.platform != BuildTarget.iOS) return;
                string plist = Path.Combine(report.summary.outputPath, "Info.plist");
                if (!File.Exists(plist)) return;

                string text = File.ReadAllText(plist);
                if (text.Contains("NSLocalNetworkUsageDescription")) return; // 已写入过（幂等）

                const string insert =
                    "\t<key>NSLocalNetworkUsageDescription</key>\n" +
                    "\t<string>用于在局域网内发现主机并与其它玩家联机对战</string>\n" +
                    "\t<key>ITSAppUsesNonExemptEncryption</key>\n" +
                    "\t<false/>\n";
                text = text.Replace("</dict>", insert + "</dict>");
                File.WriteAllText(plist, text);
                Debug.Log("Info.plist 已写入本地网络权限与加密合规声明");
            }
        }
    }
}
