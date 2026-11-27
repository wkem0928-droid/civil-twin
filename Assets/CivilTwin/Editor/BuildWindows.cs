using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace CivilTwin.EditorTools
{
    /// <summary>
    /// Windows 실행 파일 빌드 (2026-11-21). 명령줄(Unity 창이 닫혀 있을 때):
    ///   Unity.exe -batchmode -quit -projectPath C:\Dev\CivilTwin -force-vulkan
    ///             -executeMethod CivilTwin.EditorTools.BuildWindows.Build -logFile Logs\build.log
    /// 결과: Builds\Windows\CivilTwin.exe (+ CivilTwin_Data). 지형 파일(Data\sites\*.asc)도 실행 파일 옆 Data\sites로 복사한다
    /// (프로그램이 "실행 파일 폴더\Data\sites"를 읽는다 — SiteSwitcher.SitesFolder).
    ///
    /// 그래픽 API는 Direct3D11을 먼저, Vulkan을 다음으로 둔다. 보통 PC는 DX11로 열리고, 이 개발 PC처럼 오래된 AMD 드라이버로
    /// DX11이 멈추는 PC에서는 "CivilTwin.exe -force-vulkan"으로 연다 (Docs/TROUBLESHOOTING.md).
    /// 스크립트 백엔드는 Mono (IL2CPP는 C++ 빌드 도구가 따로 필요해서 쓰지 않는다).
    /// </summary>
    public static class BuildWindows
    {
        public const string OutputFolder = "Builds/Windows";

        /// <summary>
        /// 코드가 실행 중에 Shader.Find로 찾는 셰이더(URP Lit, Unlit)를 빌드에 꼭 넣는다: 쓰는 재질을 Resources 폴더에 둔다.
        /// (재질이 없으면 빌드가 셰이더를 빼서, 실행 파일에서 Shader.Find가 null이 된다.)
        /// </summary>
        private static void KeepRuntimeShaders()
        {
            const string folder = "Assets/CivilTwin/Resources";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/CivilTwin", "Resources");
            foreach (string shaderName in new[] { "Universal Render Pipeline/Lit", "Universal Render Pipeline/Unlit" })
            {
                string path = folder + "/KeepShader_" + shaderName.Substring(shaderName.LastIndexOf('/') + 1) + ".mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) continue;
                Shader shader = Shader.Find(shaderName);
                if (shader == null) { Debug.LogError("[CivilTwin] 셰이더 없음: " + shaderName); continue; }
                AssetDatabase.CreateAsset(new Material(shader), path);
            }
            AssetDatabase.SaveAssets();
        }

        public static void Build()
        {
            KeepRuntimeShaders();
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string folder = Path.Combine(project, OutputFolder);
            Directory.CreateDirectory(folder);

            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,
                new[] { GraphicsDeviceType.Direct3D11, GraphicsDeviceType.Vulkan });
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.productName = "CivilTwin";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;

            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/SampleScene.unity" },
                locationPathName = Path.Combine(folder, "CivilTwin.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary s = report.summary;
            Debug.Log("[CivilTwin] 빌드 결과: " + s.result + ", 크기 " + (s.totalSize / (1024.0 * 1024.0)).ToString("F1") + " MB, "
                + s.totalTime.TotalSeconds.ToString("F0") + "초, 경고 " + s.totalWarnings + ", 오류 " + s.totalErrors + " → " + options.locationPathName);
            if (s.result != BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
                return;
            }

            // 지형 파일 복사 (저장소에는 없다 — 이 PC의 Data\sites에서)
            string sites = Path.Combine(project, "Data", "sites");
            string target = Path.Combine(folder, "Data", "sites");
            int copied = 0;
            if (Directory.Exists(sites))
            {
                Directory.CreateDirectory(target);
                foreach (string file in Directory.GetFiles(sites, "*.asc"))
                {
                    File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
                    copied++;
                }
            }
            Debug.Log("[CivilTwin] 지형 파일 " + copied + "개 복사 → " + target);

            // 메모장용 사용설명서를 실행 파일 옆에 둔다 (2026-11-27, 배포용 zip에 함께 들어간다)
            string manual = Path.Combine(project, "Docs", "사용설명서.txt");
            if (File.Exists(manual))
            {
                File.Copy(manual, Path.Combine(folder, "사용설명서.txt"), true);
                Debug.Log("[CivilTwin] 사용설명서 복사 → " + folder);
            }
            else Debug.LogWarning("[CivilTwin] 사용설명서 없음: " + manual);
        }
    }
}
