using UnityEngine;
using UnityEngine.InputSystem;

namespace CivilTwin.Runtime
{
    /// <summary>
    /// 화면 글상자 켜기·끄기.
    ///
    /// 1) Tab 키 (한꺼번에): 전체 보기 → 시간 막대만 → 모두 숨김 → (다시 전체 보기).
    ///    지형과 건물을 가리지 않고 보거나, 깨끗한 화면을 캡처·녹화할 때 쓴다.
    /// 2) 항목별 (2026-11-21, 개발자 의견 "사용자가 보기 편하게, 껐다 켰다 기능"): 메뉴 [화면]에서 하나씩 켜고 끈다.
    ///    정보 글상자 · 측정 안내 · 표고 범례 · 누적 그래프 · 터널 단면 · 이름표 · 시간 막대(터널은 종단면).
    ///    고른 값은 다음 실행에도 남는다 (PlayerPrefs). 자동 시연(-civiltwinScenario)일 때는 읽지 않아 결과가 늘 같다.
    /// 3) 글상자 내용 간단 / 자세히. "자세히"로 바꾸면 누적 그래프와 터널 단면도 함께 켜진다 (그다음은 따로 끌 수 있다).
    /// 글상자를 그리는 도구들이 그리기 전에 이 값을 확인한다.
    /// </summary>
    public static class HudMode
    {
        public enum Level
        {
            All,           // 글상자, 그래프, 범례, 시간 막대 모두 (항목별 끄기는 따로 적용)
            TimeBarOnly,   // 시간 막대만
            None,          // 아무것도 그리지 않는다
        }

        /// <summary>항목별로 켜고 끄는 화면 요소</summary>
        public enum Item { Info, Measure, Legend, Graph, Section, Labels, TimeBar }

        public static readonly Item[] Items = { Item.Info, Item.Measure, Item.Legend, Item.Graph, Item.Section, Item.Labels, Item.TimeBar };

        public static string ItemName(Item item)
        {
            switch (item)
            {
                case Item.Info: return "정보 글상자";
                case Item.Measure: return "측정 안내";
                case Item.Legend: return "표고 범례";
                case Item.Graph: return "누적 토공 그래프";
                case Item.Section: return "터널 단면 모식도";
                case Item.Labels: return "이름표 (터널)";
                case Item.TimeBar: return "시간 막대·종단면";
                default: return item.ToString();
            }
        }

        // 기본: 그래프와 단면은 끔 (2026-11-20 개발자가 써 보니 기본 화면에서 지형이 가려졌다), 나머지는 켬
        private static bool DefaultOn(Item item) => item != Item.Graph && item != Item.Section;

        private static readonly bool[] _on = new bool[7];
        private const string PrefsKey = "CivilTwin.Hud.";

        public static Level Current { get; private set; } = Level.All;

        // ---------- 글자 크기 (2026-11-21) ----------
        // 화면 글상자·메뉴 전체를 GUI.matrix로 키운다. 큰 모니터나 시연 영상에서 글이 작을 때 쓴다.
        // 배치 계산은 GuiWidth·GuiHeight(화면 픽셀 ÷ 배율)로, 마우스·3D 위치는 MouseToGui·ScreenToGui로 바꿔 쓴다.
        public static readonly float[] TextScales = { 0.85f, 1.0f, 1.25f, 1.5f };
        private static float _chosenScale = 1.0f;

        /// <summary>
        /// 실제로 쓰는 배율 = 고른 배율, 단 창이 작으면 GUI 영역이 1000 × 500보다 작아지지 않게 줄인다
        /// (1100 × 505 창에서 1.25배로 하면 터널 글상자가 화면 밖으로 나가고 메뉴 아래가 잘렸다 — 2026-11-21 캡처).
        /// </summary>
        public static float TextScale => Mathf.Min(_chosenScale, MaxScale);

        /// <summary>지금 창 크기에서 쓸 수 있는 가장 큰 배율 (작게 0.85는 늘 가능)</summary>
        public static float MaxScale => Mathf.Max(TextScales[0], Mathf.Min(Screen.width / 1000f, Screen.height / 500f));

        public static string ScaleName(float s) =>
            s < 0.9f ? "작게" : s < 1.1f ? "보통" : s < 1.4f ? "크게" : "아주 크게";

        public static string TextScaleName =>
            ScaleName(_chosenScale) + (TextScale < _chosenScale - 1e-4f ? " → 창이 작아 × " + TextScale.ToString("0.##") : "");

        /// <summary>작게 → 보통 → 크게 → 아주 크게 → 작게 (지금 창에 맞지 않는 크기는 건너뛴다)</summary>
        public static void CycleTextScale()
        {
            int i = System.Array.IndexOf(TextScales, _chosenScale);
            for (int step = 1; step <= TextScales.Length; step++)
            {
                float next = TextScales[(i + step) % TextScales.Length];
                if (next <= MaxScale + 1e-4f) { _chosenScale = next; break; }
            }
            Save();
        }

        /// <summary>OnGUI 첫 줄에서 부른다</summary>
        public static void ApplyMatrix() => GUI.matrix = Matrix4x4.Scale(new Vector3(TextScale, TextScale, 1f));

        /// <summary>배율을 뺀 GUI 좌표계의 화면 폭·높이</summary>
        public static float GuiWidth => Screen.width / TextScale;
        public static float GuiHeight => Screen.height / TextScale;

        /// <summary>마우스 좌표(Input System, 왼쪽 아래 0, 화면 픽셀) → GUI 좌표(왼쪽 위 0, 배율 뺌)</summary>
        public static Vector2 MouseToGui(Vector2 screen) => new Vector2(screen.x, Screen.height - screen.y) / TextScale;

        /// <summary>GUI 좌표 → 마우스 좌표 (자동 시연이 버튼을 누를 때)</summary>
        public static Vector2 GuiToMouse(Vector2 gui) => new Vector2(gui.x * TextScale, Screen.height - gui.y * TextScale);

        /// <summary>
        /// 글상자를 자세히 보여 줄지 (메뉴 [화면] → 글상자 자세히). 기본은 간단.
        /// 2026-11-20 개발자가 실제로 써 보니 약 1100 × 520 화면에서 자세한 글상자와 그래프가 지형 한가운데를 덮어
        /// 부지를 놓아도 보이지 않았다. 그래서 기본은 몇 줄 요약만 보이고, 자세한 값은 켤 때만 나온다.
        /// </summary>
        public static bool Detail { get; private set; }

        static HudMode() => ResetItems();

        /// <summary>오른쪽 정보 글상자를 제목 한 줄로 접었는가 (글상자 오른쪽 위 [접기] 버튼, 다음 실행에도 남음)</summary>
        public static bool InfoCollapsed { get; private set; }

        public static void ToggleInfoCollapsed()
        {
            InfoCollapsed = !InfoCollapsed;
            Save();
        }

        /// <summary>대안 비교표를 한 줄 요약으로 접었는가 (표 오른쪽 위 [접기])</summary>
        public static bool TableCollapsed { get; private set; }

        public static void ToggleTableCollapsed()
        {
            TableCollapsed = !TableCollapsed;
            Save();
        }

        // ---------- 재생 속도 (2026-11-21) ----------
        public static readonly float[] PlaybackSpeeds = { 0.5f, 1f, 2f, 4f };
        /// <summary>재생 배속 (토공·건물·터널 공통). 기본 1 = 토공 10초, 건물 25초, 터널 20초에 끝까지</summary>
        public static float PlaybackSpeed { get; private set; } = 1f;

        public static void ChangePlaybackSpeed(int step)
        {
            int i = System.Array.IndexOf(PlaybackSpeeds, PlaybackSpeed);
            if (i < 0) i = 1;
            PlaybackSpeed = PlaybackSpeeds[Mathf.Clamp(i + step, 0, PlaybackSpeeds.Length - 1)];
            Save();
        }

        /// <summary>터널 종단면을 제목 한 줄로 접었는가</summary>
        public static bool ProfileCollapsed { get; private set; }

        public static void ToggleProfileCollapsed()
        {
            ProfileCollapsed = !ProfileCollapsed;
            Save();
        }

        /// <summary>왼쪽 건물 글상자를 한 줄로 접었는가</summary>
        public static bool BuildingCollapsed { get; private set; }

        public static void ToggleBuildingCollapsed()
        {
            BuildingCollapsed = !BuildingCollapsed;
            Save();
        }

        /// <summary>간단 ↔ 자세히. 자세히로 바꾸면 누적 그래프·터널 단면도 켜고, 간단으로 바꾸면 끈다.</summary>
        public static void ToggleDetail()
        {
            Detail = !Detail;
            Set(Item.Graph, Detail);
            Set(Item.Section, Detail);
            Save();
        }

        /// <summary>그 항목을 그려도 되면 true (Tab 단계도 함께 본다: 시간 막대만 단계에서는 시간 막대만)</summary>
        public static bool Show(Item item)
        {
            if (!_on[(int)item]) return false;
            return item == Item.TimeBar ? ShowTimeBar : ShowBoxes;
        }

        public static bool IsOn(Item item) => _on[(int)item];

        public static void Toggle(Item item)
        {
            _on[(int)item] = !_on[(int)item];
            Save();
        }

        public static void Set(Item item, bool on) => _on[(int)item] = on;

        /// <summary>모든 항목을 처음 값으로 (메뉴 [화면] → 처음 상태로)</summary>
        public static void RestoreDefaults()
        {
            Current = Level.All;
            Detail = false;
            _chosenScale = 1.0f;
            InfoCollapsed = TableCollapsed = BuildingCollapsed = ProfileCollapsed = false;
            PlaybackSpeed = 1f;
            ResetItems();
            Save();
        }

        /// <summary>글상자·그래프·범례를 그려도 되면 true (Tab 단계)</summary>
        public static bool ShowBoxes => Current == Level.All;

        /// <summary>시간 막대를 그려도 되면 true (Tab 단계)</summary>
        public static bool ShowTimeBar => Current != Level.None;

        private static int _polledFrame = -1;

        /// <summary>실행을 시작할 때 전체 보기로 되돌리고, 자동 시연이 아니면 지난번 항목 설정을 읽는다.</summary>
        public static void Reset()
        {
            Current = Level.All;
            Detail = false;
            _chosenScale = 1.0f;
            InfoCollapsed = TableCollapsed = BuildingCollapsed = ProfileCollapsed = false;
            PlaybackSpeed = 1f;
            _polledFrame = -1;
            ResetItems();
            if (!IsScenarioRun()) Load();
        }

        private static void ResetItems()
        {
            foreach (Item item in Items) _on[(int)item] = DefaultOn(item);
        }

        private static bool IsScenarioRun()
        {
            foreach (string arg in System.Environment.GetCommandLineArgs())
                if (arg == "-civiltwinScenario") return true;
            return false;
        }

        private static void Load()
        {
            foreach (Item item in Items)
            {
                string key = PrefsKey + item;
                if (PlayerPrefs.HasKey(key)) _on[(int)item] = PlayerPrefs.GetInt(key) != 0;
            }
            Detail = PlayerPrefs.GetInt(PrefsKey + "Detail", 0) != 0;
            float scale = PlayerPrefs.GetFloat(PrefsKey + "TextScale", 1.0f);
            if (System.Array.IndexOf(TextScales, scale) >= 0) _chosenScale = scale;
            InfoCollapsed = PlayerPrefs.GetInt(PrefsKey + "InfoCollapsed", 0) != 0;
            TableCollapsed = PlayerPrefs.GetInt(PrefsKey + "TableCollapsed", 0) != 0;
            BuildingCollapsed = PlayerPrefs.GetInt(PrefsKey + "BuildingCollapsed", 0) != 0;
            ProfileCollapsed = PlayerPrefs.GetInt(PrefsKey + "ProfileCollapsed", 0) != 0;
            float speed = PlayerPrefs.GetFloat(PrefsKey + "PlaybackSpeed", 1f);
            if (System.Array.IndexOf(PlaybackSpeeds, speed) >= 0) PlaybackSpeed = speed;
        }

        private static void Save()
        {
            if (IsScenarioRun()) return;   // 자동 시연은 개발자의 설정을 바꾸지 않는다
            foreach (Item item in Items) PlayerPrefs.SetInt(PrefsKey + item, _on[(int)item] ? 1 : 0);
            PlayerPrefs.SetInt(PrefsKey + "Detail", Detail ? 1 : 0);
            PlayerPrefs.SetFloat(PrefsKey + "TextScale", _chosenScale);
            PlayerPrefs.SetInt(PrefsKey + "InfoCollapsed", InfoCollapsed ? 1 : 0);
            PlayerPrefs.SetInt(PrefsKey + "TableCollapsed", TableCollapsed ? 1 : 0);
            PlayerPrefs.SetInt(PrefsKey + "BuildingCollapsed", BuildingCollapsed ? 1 : 0);
            PlayerPrefs.SetInt(PrefsKey + "ProfileCollapsed", ProfileCollapsed ? 1 : 0);
            PlayerPrefs.SetFloat(PrefsKey + "PlaybackSpeed", PlaybackSpeed);
            PlayerPrefs.Save();
        }

        /// <summary>Tab 키를 읽는다. 한 프레임에 여러 번 불려도 한 번만 처리한다.</summary>
        public static void Poll()
        {
            if (_polledFrame == Time.frameCount) return;
            _polledFrame = Time.frameCount;

            if (!InputGate.Allowed) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.tabKey.wasPressedThisFrame) return;

            Cycle();
        }

        /// <summary>전체 → 시간 막대만 → 모두 숨김 → 전체. 키와 화면 메뉴가 함께 쓴다.</summary>
        public static void Cycle()
        {
            Current = Current == Level.All ? Level.TimeBarOnly
                : (Current == Level.TimeBarOnly ? Level.None : Level.All);
        }
    }
}
