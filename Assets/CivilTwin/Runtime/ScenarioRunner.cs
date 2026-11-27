using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace CivilTwin.Runtime
{
    /// <summary>
    /// 자동 시연 실행기. Claude가 Unity 화면을 직접 확인하려고 만든 도구다 (2026-11-20).
    ///
    /// 쓰는 법 (Unity 창이 닫혀 있을 때, 명령줄에서):
    ///   Unity.exe -projectPath C:\Dev\CivilTwin -force-vulkan
    ///             -executeMethod CivilTwin.Runtime.ScenarioLauncher.Run
    ///             -civiltwinScenario Tools\Scenarios\이름.txt
    ///   → 에디터가 열리고, 씬을 열어 Play에 들어간 뒤, 시나리오 파일의 줄을 순서대로 실행하고, 끝나면 에디터를 닫는다.
    ///
    /// 결과: Logs/scenario/이름_시각/ 폴더에 log.txt(단계별 기록과 글상자 글)와 화면 그림.
    ///
    /// 시나리오 파일 문법 (한 줄에 하나, # 뒤는 설명):
    ///   wait 2.5                 2.5초 기다림
    ///   key G                    G 키를 한 번 누름 (Input System의 Key 이름: Period, Comma, Digit1, Space, LeftShift …)
    ///   key LeftShift+Period     여러 키를 함께 누름
    ///   mouse 0.5 0.5            마우스 커서를 화면 비율 위치(가로, 세로; 왼쪽 아래가 0 0)로 옮김
    ///   button 균형 계획고        위쪽 메뉴에서 글자가 이것으로 시작하는 버튼을 실제로 클릭 (같은 글자가 여럿이면 "button − @2")
    ///   capture 이름             화면 그림 저장 + 부지·대안 글을 로그에 기록
    ///   colortest 이름           화면 그림을 세 가지 방법으로 저장 (색 문제 비교용)
    ///   showroom 0 30 15 12      장비 전시장(EquipmentShowroom): 장비 번호(−1 전체), 방위각, 고도각, 거리(m)로 카메라를 놓음
    ///   excavator 5.7            전시장 굴착기를 사이클 5.7초의 자세로 멈춤 (Cm 19초, "excavator live"면 다시 움직임)
    ///   truckbed 0.5 0.4         전시장 덤프트럭의 적재함을 든 정도(0 ~ 1)와 실은 흙(0 ~ 1)
    ///   equipclock 263           현장 장비 시계를 263초로 맞춤 (트럭 덤프처럼 몇 분 뒤 동작을 바로 확인)
    ///   lookat truck0 200 25 30  현장 장비(truck0 truck1 excavator dozer roller) 또는 터널 위치(west east middle)를
    ///                            방위각·고도각·거리(m)로 봄
    ///   log 아무 글              로그에 글을 남김
    ///   quit                     끝내기 (파일 끝에 오면 자동으로 끝난다)
    ///
    /// 안전장치:
    ///   - 이 명령줄 인수가 없으면 아무것도 하지 않는다 (평소 Play에는 영향 없음).
    ///   - 시연 중에는 실제 키보드·마우스를 끄고 가상 키보드·마우스만 쓴다 (사용자가 다른 창에서 친 키가 섞이지 않게).
    ///   - 10분이 넘으면 스스로 끝낸다.
    /// </summary>
    public sealed class ScenarioRunner : MonoBehaviour
    {
        public const string ArgumentName = "-civiltwinScenario";
        private const float TimeLimitSeconds = 600f;

        /// <summary>자동 시연이 진행 중이면 true (InputGate가 창 선택과 관계없이 입력을 받게 한다)</summary>
        public static bool IsRunning { get; private set; }

        private string _scenarioPath;
        private string _outputFolder;
        private StreamWriter _log;
        private Keyboard _keyboard;
        private Mouse _mouse;
        private readonly List<InputDevice> _disabledDevices = new List<InputDevice>();
        private Vector2 _mousePosition;
        private float _startTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string path = FindArgument(ArgumentName);
            if (string.IsNullOrEmpty(path)) return;

            var go = new GameObject("ScenarioRunner");
            DontDestroyOnLoad(go);
            go.AddComponent<ScenarioRunner>()._scenarioPath = path;
        }

        /// <summary>명령줄에서 "-이름 값"의 값을 찾는다. 없으면 null.</summary>
        public static string FindArgument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            }
            return null;
        }

        private void Start()
        {
            IsRunning = true;
            _startTime = Time.realtimeSinceStartup;
            Application.runInBackground = true;

            string projectFolder = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string fullScenario = Path.IsPathRooted(_scenarioPath) ? _scenarioPath : Path.Combine(projectFolder, _scenarioPath);
            string name = Path.GetFileNameWithoutExtension(fullScenario);
            _outputFolder = Path.Combine(projectFolder, "Logs", "scenario", name + "_" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(_outputFolder);
            _log = new StreamWriter(Path.Combine(_outputFolder, "log.txt"), false, new System.Text.UTF8Encoding(false)) { AutoFlush = true };
            Log("시나리오: " + fullScenario);
            Log("화면: " + Screen.width + " × " + Screen.height);

            SetUpVirtualDevices();
            StartCoroutine(Run(fullScenario));
        }

        private void Update()
        {
            if (IsRunning && Time.realtimeSinceStartup - _startTime > TimeLimitSeconds)
            {
                Log("시간 제한(" + TimeLimitSeconds + "초)을 넘어 끝냅니다.");
                Finish();
            }
        }

        // ---------- 가상 키보드·마우스 ----------

        private void SetUpVirtualDevices()
        {
            // 창 선택과 관계없이 입력이 Game 화면으로 가게 한다 (설정 파일이 없어 메모리 안의 기본값만 바뀐다).
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            // 실제 키보드·마우스는 끈다 (사용자의 입력이 시연에 섞이지 않도록).
            foreach (InputDevice device in InputSystem.devices)
            {
                if ((device is Keyboard || device is Mouse) && device.enabled)
                {
                    InputSystem.DisableDevice(device);
                    _disabledDevices.Add(device);
                }
            }

            _keyboard = InputSystem.AddDevice<Keyboard>("ScenarioKeyboard");
            _mouse = InputSystem.AddDevice<Mouse>("ScenarioMouse");
            _keyboard.MakeCurrent();
            _mouse.MakeCurrent();
            _mousePosition = new Vector2(Screen.width / 2f, Screen.height / 2f);
            InputSystem.QueueStateEvent(_mouse, new MouseState { position = _mousePosition });
            Log("가상 키보드·마우스 준비 (실제 장치 " + _disabledDevices.Count + "개 끔)");
        }

        private void RestoreDevices()
        {
            if (_keyboard != null && _keyboard.added) InputSystem.RemoveDevice(_keyboard);
            if (_mouse != null && _mouse.added) InputSystem.RemoveDevice(_mouse);
            foreach (InputDevice device in _disabledDevices)
            {
                if (device.added) InputSystem.EnableDevice(device);
            }
            _disabledDevices.Clear();
        }

        private IEnumerator PressKeys(string combo)
        {
            var keys = new List<Key>();
            foreach (string part in combo.Split('+'))
            {
                if (!Enum.TryParse(part.Trim(), true, out Key key) || key == Key.None)
                {
                    Log("알 수 없는 키: " + part);
                    yield break;
                }
                keys.Add(key);
            }

            // 누름 → 한 프레임 → 뗌 → 한 프레임. 도구들은 누른 프레임에 wasPressedThisFrame으로 받는다.
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys.ToArray()));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;
            yield return null;
        }

        /// <summary>
        /// 화면 메뉴(ControlBar)에서 글자가 text로 시작하는 버튼을 찾아 가상 마우스로 실제 클릭한다.
        /// "button 부지" 처럼 쓰고, 같은 글자가 여럿이면 "button + @2" 처럼 @번호(1부터)를 붙인다.
        /// </summary>
        private IEnumerator ClickButton(string text)
        {
            // 순서 표시는 "@2" (# 은 시나리오의 설명 기호라서 쓸 수 없다 — 2026-11-20 처음엔 #으로 했다가 잘못 눌렸다)
            int nth = 0;
            int atAt = text.LastIndexOf(" @", StringComparison.Ordinal);
            if (atAt >= 0 && int.TryParse(text.Substring(atAt + 2), out int n))
            {
                nth = Mathf.Max(0, n - 1);
                text = text.Substring(0, atAt);
            }

            var bar = FindAnyObjectByType<ControlBar>();
            if (bar == null || !bar.TryFindButton(text, nth, out Vector2 gui, out bool enabled))
            {
                Log("  버튼을 찾지 못함: " + text);
                yield break;
            }
            if (!enabled) Log("  (꺼진 버튼: " + text + ")");

            _mousePosition = HudMode.GuiToMouse(gui);
            InputSystem.QueueStateEvent(_mouse, new MouseState { position = _mousePosition });
            yield return null;
            InputSystem.QueueStateEvent(_mouse, new MouseState { position = _mousePosition }.WithButton(MouseButton.Left, true));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(_mouse, new MouseState { position = _mousePosition });
            yield return null;
            yield return null;
        }

        // ---------- 시나리오 실행 ----------

        private IEnumerator Run(string path)
        {
            if (!File.Exists(path))
            {
                Log("시나리오 파일이 없습니다: " + path);
                Finish();
                yield break;
            }

            string[] lines = File.ReadAllLines(path, System.Text.Encoding.UTF8);
            for (int n = 0; n < lines.Length; n++)
            {
                string line = lines[n];
                int hash = line.IndexOf('#');
                if (hash >= 0) line = line.Substring(0, hash);
                line = line.Trim();
                if (line.Length == 0) continue;

                string[] parts = line.Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
                string command = parts[0].ToLowerInvariant();
                string rest = parts.Length > 1 ? parts[1].Trim() : "";
                Log("[" + (n + 1) + "] " + line);

                switch (command)
                {
                    case "wait":
                        yield return new WaitForSecondsRealtime(ParseFloat(rest, 1f));
                        break;
                    case "key":
                        yield return PressKeys(rest);
                        break;
                    case "mouse":
                        string[] xy = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (xy.Length == 2)
                        {
                            _mousePosition = new Vector2(ParseFloat(xy[0], 0.5f) * Screen.width, ParseFloat(xy[1], 0.5f) * Screen.height);
                            InputSystem.QueueStateEvent(_mouse, new MouseState { position = _mousePosition });
                            yield return null;
                            yield return null;
                        }
                        break;
                    case "button":
                        yield return ClickButton(rest);
                        break;
                    case "capture":
                        yield return Capture(SafeName(rest), false);
                        break;
                    case "colortest":
                        yield return Capture(SafeName(rest), true);
                        break;
                    case "showroom":
                        string[] v = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        EquipmentShowroom.Show(v.Length > 0 ? (int)ParseFloat(v[0], -1f) : -1,
                            v.Length > 1 ? ParseFloat(v[1], 30f) : 30f, v.Length > 2 ? ParseFloat(v[2], 15f) : 15f,
                            v.Length > 3 ? ParseFloat(v[3], 40f) : 40f);
                        yield return null;
                        yield return null;
                        break;
                    case "excavator":
                        EquipmentShowroom.FixedSeconds = rest.Trim() == "live" ? double.NaN : ParseFloat(rest, 0f);
                        Log("  굴착기 단계: " + (double.IsNaN(EquipmentShowroom.FixedSeconds) ? "움직임"
                            : CivilTwin.Core.ExcavatorCycle.PhaseName(EquipmentShowroom.FixedSeconds, 19.0)));
                        yield return null;
                        yield return null;
                        break;
                    case "truckbed":
                        string[] tb = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        EquipmentShowroom.SetTruck(tb.Length > 0 ? ParseFloat(tb[0], 0f) : 0f, tb.Length > 1 ? ParseFloat(tb[1], 1f) : 1f);
                        yield return null;
                        yield return null;
                        break;
                    case "equipclock":
                        BuildingSiteTool.EquipmentClockOffset = ParseFloat(rest, 0f) - Time.unscaledTime;
                        Log("  장비 시계 = " + rest.Trim() + "초");
                        yield return null;
                        break;
                    case "lookat":
                        string[] la = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        var site = FindAnyObjectByType<BuildingSiteTool>();
                        var orbitCam = Camera.main != null ? Camera.main.GetComponent<OrbitCameraController>() : null;
                        var tunnelTool = FindAnyObjectByType<TunnelTool>();
                        Vector3 at = Vector3.zero;
                        bool found = la.Length > 0 && orbitCam != null
                            && ((site != null && site.TryGetEquipmentPosition(la[0], out at))
                                || (tunnelTool != null && tunnelTool.TryGetPoint(la[0], out at)));
                        if (found)
                        {
                            orbitCam.LookFrom(new CivilTwin.Core.LocalPoint(at.x, at.y + 2.0, at.z),
                                la.Length > 1 ? ParseFloat(la[1], 200f) : 200f, la.Length > 2 ? ParseFloat(la[2], 25f) : 25f,
                                la.Length > 3 ? ParseFloat(la[3], 30f) : 30f, 5.0);
                        }
                        else Log("  lookat: 장비를 찾지 못함 (" + rest.Trim() + ")");
                        yield return null;
                        break;
                    case "log":
                        break;
                    case "quit":
                        Finish();
                        yield break;
                    default:
                        Log("알 수 없는 명령: " + command);
                        break;
                }
            }

            Log("시나리오 끝");
            Finish();
        }

        private IEnumerator Capture(string label, bool allMethods)
        {
            yield return new WaitForEndOfFrame();

            // 기본: 렌더 텍스처로 받아 위아래를 뒤집어 저장 (ScreenGrab 참고 — 이 PC에서 색이 맞는 방법)
            ScreenGrab.SavePng(Path.Combine(_outputFolder, label + ".png"));
            Log("  화면 저장: " + label + ".png (" + Screen.width + " × " + Screen.height + ")");

            if (allMethods)
            {
                // 색 비교용: Unity 기본 저장 두 방법 (이 PC에서는 색이 틀리게 나왔다, 2026-11-20)
                ScreenCapture.CaptureScreenshot(Path.Combine(_outputFolder, label + "_A-file.png"));
                Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(_outputFolder, label + "_B-texture.png"), tex.EncodeToPNG());
                Destroy(tex);
                Log("  색 비교용 그림 2장 추가 저장 (_A-file, _B-texture)");
                yield return null;
                yield return null;
            }

            // 화면의 글상자 글을 그대로 기록한다 (숫자 검산은 그림보다 이 글로 한다).
            var switcher = FindAnyObjectByType<SiteSwitcher>();
            if (switcher != null) Log("  현장: " + switcher.SiteLabel);
            var grading = FindAnyObjectByType<GradingPlanTool>();
            if (grading != null) Log("  --- 부지 글상자 ---\n" + grading.DescribeForLog());
            var alternatives = FindAnyObjectByType<AlternativeTool>();
            if (alternatives != null) Log("  --- 대안 ---\n" + alternatives.DescribeForLog());
            var tunnel = FindAnyObjectByType<TunnelTool>();
            if (tunnel != null && tunnel.IsOn) Log("  --- 터널 ---\n" + tunnel.DescribeForLog());
        }

        // ---------- 끝내기 ----------

        private bool _finished;

        private void Finish()
        {
            if (_finished) return;
            _finished = true;
            Log("끝냅니다 (" + (Time.realtimeSinceStartup - _startTime).ToString("F1", CultureInfo.InvariantCulture) + "초)");
            RestoreDevices();
            IsRunning = false;
            _log?.Dispose();
            _log = null;
#if UNITY_EDITOR
            // Play를 먼저 끝내고 닫으면, 그 사이 스크립트를 다시 읽어 들일 때 닫기 예약이 사라진다 (2026-11-20 실제로 겪음).
            // 그래서 Play 중에 바로 에디터를 닫는다.
            UnityEditor.EditorApplication.Exit(0);
#else
            Application.Quit();
#endif
        }

        private void OnDestroy()
        {
            if (IsRunning) RestoreDevices();
            IsRunning = false;
        }

        private void Log(string message)
        {
            string line = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message;
            Debug.Log("[CivilTwin 시연] " + message);
            _log?.WriteLine(line);
        }

        private static float ParseFloat(string text, float fallback)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;
        }

        private static string SafeName(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "capture_" + DateTime.Now.ToString("HHmmss");
            foreach (char c in Path.GetInvalidFileNameChars()) text = text.Replace(c, '_');
            return text.Replace(' ', '_');
        }
    }

#if UNITY_EDITOR
    /// <summary>명령줄의 -executeMethod로 부르는 입구: 씬을 열고 Play에 들어간다 (시연 내용은 ScenarioRunner가 실행).</summary>
    public static class ScenarioLauncher
    {
        public static void Run()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            UnityEditor.EditorApplication.EnterPlaymode();
        }
    }
#endif
}
