using System;
using System.Collections.Generic;
using System.Globalization;
using CivilTwin.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CivilTwin.Runtime
{
    /// <summary>
    /// 화면 위쪽 메뉴 (5-6). 키를 외우지 않아도 버튼으로 같은 동작을 한다.
    ///   1줄: ◀  회사 · 현장 (번호 / 전체)  ▶
    ///   2줄: [부지] [장비] [재생] [건물] [대안] [화면]  — 누르면 그 아래에 버튼이 펼쳐지고, 다시 누르면 접힌다.
    /// 버튼 이름 옆 괄호는 같은 일을 하는 키다 (메뉴를 쓰다 보면 키를 익히게 하려고).
    ///
    /// 버튼은 Unity의 GUI.Button이 아니라 직접 만든 것이다: OnGUI에서 그리면서 자리를 기억하고,
    /// Update에서 Input System 마우스가 그 자리를 눌렀는지 본다. 그래야 실제 마우스와 자동 시연(ScenarioRunner)의
    /// 가상 마우스로 똑같이 눌린다. 메뉴 자리는 HudLayout에 알려서 측정·카메라 도구가 그 클릭을 무시하게 한다.
    /// 화면 글자는 임시(IMGUI)다.
    /// </summary>
    public sealed class ControlBar : MonoBehaviour
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly string[] Sections = { "부지", "장비", "재생", "건물", "대안", "화면" };

        private const float Width = 470f, Top = 10f, RowHeight = 26f, Gap = 4f, Padding = 8f;

        private SiteSwitcher _sites;
        private GradingPlanTool _grading;
        private BuildingSiteTool _building;
        private AlternativeTool _alternatives;
        private TunnelTool _tunnel;   // 2026-11-21 터널 굴착 4D (같은 GameObject)

        private int _open = -1;   // 펼친 메뉴 (−1 = 접힘)
        private bool _showHelp;   // 키 안내 판 (F1, 2026-11-21)
        private bool _showSites;  // 현장 목록 (현장 이름을 누르면, 2026-11-21)

        /// <summary>키 안내 판의 내용: (키, 하는 일). README "조작" 표와 같다.</summary>
        private static readonly (string key, string what)[] HelpKeys =
        {
            ("왼쪽 드래그 / 오른쪽 드래그 / 휠", "카메라 회전 / 이동 / 확대"),
            ("W A S D · R", "카메라 이동 · 처음 시점"),
            ("[  ]", "이전·다음 현장"),
            ("클릭 두 번 · C", "두 점 거리·경사 측정 · 지우기"),
            ("G · E", "커서 위치에 부지 놓기 · 균형 계획고"),
            ("−  =  (Shift: 10 m)", "계획고 ∓1 m"),
            ("9  0 · X", "부지 크기 · 암반선 깊이"),
            (",  .  /  Shift + ,  .", "굴착기 대수 / 덤프트럭 대수"),
            (";  ' · I", "하루 작업 시간 · 장비 간섭 가정"),
            ("Space · N  M · B", "재생·멈춤 · 앞뒤로 · 처음"),
            ("V", "계획면 · 원지반 · 공사 중"),
            ("T · F · P · K  L", "건물과 장비 · 가까이 · 공정표 · 층수"),
            ("1 2 3 / Shift + 숫자 · O", "대안 저장 / 불러오기 · 비교표·그래프"),
            ("Tab", "글상자 한꺼번에 줄이기·숨기기"),
            ("F1 · F12", "이 안내 · 화면 저장"),
        };
        private string _exampleMessage = "";   // 예시 불러오기 결과 한 줄

        /// <summary>
        /// 메뉴 막대(펼친 메뉴 제외)가 차지하는 자리 (GUI 좌표). 다른 글상자가 이 자리를 피해 내려간다.
        /// 메뉴가 숨겨져 있으면 크기 0.
        /// </summary>
        public static Rect BarRect { get; private set; }

        private struct Button
        {
            public Rect Rect;
            public Action Action;
            public bool Enabled;
            public string Text;
        }

        /// <summary>
        /// 글자가 text로 시작하는 버튼의 가운데 (GUI 좌표). 자동 시연이 실제 마우스 클릭으로 버튼을 누를 때 쓴다.
        /// 같은 글자의 버튼이 여럿이면 nth번째(0부터).
        /// </summary>
        public bool TryFindButton(string text, int nth, out Vector2 guiCenter, out bool enabled)
        {
            int seen = 0;
            foreach (Button b in _clickable)
            {
                if (b.Text == null || !b.Text.StartsWith(text, StringComparison.Ordinal)) continue;
                if (seen++ < nth) continue;
                guiCenter = b.Rect.center;
                enabled = b.Enabled;
                return true;
            }
            enabled = true;
            if (InfoBox.TryFind(text, out guiCenter, nth - seen)) return true;   // 글상자의 [접기]/[펼치기]
            guiCenter = Vector2.zero;
            enabled = false;
            return false;
        }

        private readonly List<Button> _drawing = new List<Button>();   // 이번 그리기에서 만든 버튼
        private List<Button> _clickable = new List<Button>();           // 지난 그리기의 버튼 (Update에서 누름 판정)

        private GUIStyle _box, _label, _title, _button, _buttonHover, _buttonOff, _tabOn;

        public void Initialize(SiteSwitcher sites, GradingPlanTool grading, BuildingSiteTool building, AlternativeTool alternatives)
        {
            _sites = sites;
            _grading = grading;
            _building = building;
            _alternatives = alternatives;
            _tunnel = GetComponent<TunnelTool>();
        }

        /// <summary>자동 시연이 메뉴를 열 때 쓴다 (예: "부지"). 같은 메뉴면 접는다.</summary>
        public void Toggle(string section)
        {
            int index = Array.IndexOf(Sections, section);
            if (index >= 0) _open = _open == index ? -1 : index;
        }

        // ---------- 누름 판정 ----------

        private void Update()
        {
            if (!InputGate.Allowed) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.f1Key.wasPressedThisFrame) ToggleHelp();
                if (_showHelp && keyboard.escapeKey.wasPressedThisFrame) _showHelp = false;
            }
            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;

            Vector2 p = mouse.position.ReadValue();
            var gui = HudMode.MouseToGui(p);
            foreach (Button b in _clickable)
            {
                if (!b.Rect.Contains(gui)) continue;
                if (b.Enabled) b.Action();
                return;   // 메뉴 위를 누른 것은 한 번만 처리한다
            }
            InfoBox.TryClick(gui);   // 정보 글상자의 [접기]/[펼치기]
        }

        // ---------- 그리기 ----------

        private void OnGUI()
        {
            HudMode.ApplyMatrix();   // 글자 크기 (메뉴 [화면])
            if (_sites == null) return;
            // 다른 글상자보다 위에 그린다. 모든 이벤트에서 먼저 정해야 효과가 있다
            // (그리기 이벤트에서만 정했더니 펼친 메뉴가 부지 글상자 뒤에 깔렸다 — 2026-11-20 시연 캡처).
            GUI.depth = -10;
            if (Event.current.type != EventType.Repaint) return;
            EnsureStyles();
            _drawing.Clear();

            BarRect = Rect.zero;
            if (HudMode.Current == HudMode.Level.None)
            {
                _clickable = new List<Button>(_drawing);
                return;
            }

            if (HudMode.Current == HudMode.Level.TimeBarOnly)
            {
                // 글상자를 숨긴 동안에도 마우스로 되돌릴 수 있게 작은 버튼 하나만 둔다.
                var r = new Rect(HudMode.GuiWidth - 170f, Top, 160f, RowHeight);
                DrawButton(r, "글상자 보이기 (Tab)", HudMode.Cycle, true);
                _clickable = new List<Button>(_drawing);
                return;
            }

            // 오른쪽 위 모서리에 붙인다 (2026-11-21 개발자 의견: 가운데에 있으면 지형을 가린다).
            // 정보 글상자는 이 아래 오른쪽에 붙고, 왼쪽 위는 측정 안내·단면이 쓴다.
            float x = Mathf.Max(8f, HudMode.GuiWidth - Width - 10f);
            float y = Top;

            // 1줄: 현장
            Rect bar = new Rect(x, y, Width, RowHeight * 2 + Gap + Padding);
            GUI.Box(bar, GUIContent.none, _box);
            HudLayout.Register(bar);
            BarRect = bar;

            float arrow = 44f;
            DrawButton(new Rect(x + 4, y + 4, arrow, RowHeight - 4), "◀", () => _sites.Step(-1), _sites.CanStep);
            DrawButton(new Rect(x + Width - arrow - 4, y + 4, arrow, RowHeight - 4), "▶", () => _sites.Step(+1), _sites.CanStep);
            string site = _sites.SiteLabel;
            if (!string.IsNullOrEmpty(_sites.Message)) site += "   " + _sites.Message;
            // 현장 이름을 누르면 현장 목록이 펼쳐진다 (▶를 여러 번 누르지 않고 바로 고른다, 2026-11-21)
            DrawButton(new Rect(x + arrow + 8, y + 4, Width - 2 * arrow - 16, RowHeight - 4), site + (_showSites ? "  ▴" : "  ▾"),
                ToggleSites, _sites.SiteCount > 1, _showSites);

            // 2줄: 메뉴 이름
            float tabY = y + RowHeight + Gap;
            float tabWidth = (Width - 8f) / Sections.Length;
            for (int i = 0; i < Sections.Length; i++)
            {
                int index = i;
                var r = new Rect(x + 4 + tabWidth * i, tabY, tabWidth - 3, RowHeight - 2);
                DrawButton(r, Sections[i] + (_open == i ? " ▴" : " ▾"), () => { _open = _open == index ? -1 : index; _showSites = false; }, true, _open == i);
            }

            if (_showSites) DrawSites(x, bar.yMax + Gap);
            else if (_open >= 0) DrawPanel(x, bar.yMax + Gap);
            if (_showHelp) DrawHelp();

            _clickable = new List<Button>(_drawing);
        }

        // ---------- 펼친 메뉴 ----------

        private float _px, _py;   // 펼친 메뉴 안에서 다음 줄의 위치

        private void DrawPanel(float x, float y)
        {
            // 내용 줄 수를 먼저 세지 않고, 그린 뒤의 높이로 배경을 맞춘다 (배경을 먼저 그려야 하므로 두 번 계산한다).
            float height = PanelHeight(Sections[_open]);
            var back = new Rect(x, y, Width, height);
            GUI.Box(back, GUIContent.none, _box);
            HudLayout.Register(back);

            _px = x + Padding;
            _py = y + Padding;
            Fill(Sections[_open]);
        }

        private float PanelHeight(string section)
        {
            int rows = section switch
            {
                "부지" => 8,
                "장비" => _tunnel != null && _tunnel.IsOn ? 9 : 5,   // 터널: 조건 6줄 + 관통·라이닝·출처 3줄
                "재생" => 4,   // 날짜·버튼·속도·보는 면(터널은 설명)
                "건물" => 4,
                "대안" => (ReportSavedPath() != "" ? 9 : 8)
                          + (string.IsNullOrEmpty(_exampleMessage) ? 0 : 1),
                "화면" => 3 + HudMode.Items.Length + 3,   // 내용·Tab·글자 크기 + 항목 7줄 + 대안 비교·버튼·도움말
                _ => 1,
            };
            return Padding * 2 + rows * (RowHeight + Gap);
        }

        /// <summary>[대안] 메뉴에 보여 줄 마지막 보고서 경로: 터널 보기 중이면 터널 보고서, 아니면 부지 보고서</summary>
        private string ReportSavedPath()
        {
            if (_tunnel != null && _tunnel.IsOn) return _tunnel.LastReportPath ?? "";
            return _alternatives != null ? _alternatives.LastReportPath ?? "" : "";
        }

        private void Fill(string section)
        {
            bool pad = _grading != null && _grading.HasPad;
            switch (section)
            {
                case "부지":
                    // 댐 축조 (2026-11-20): 댐 현장(소양강댐, 부항댐 — SiteStructures.Dams)이면 공개 제원으로 댐을 쌓아 본다.
                    // 댐 중에는 부지 조절 버튼을 끈다.
                    // 터널 굴착 (2026-11-21): 터널 현장(인제양양터널 — TunnelStructures.Tunnels)이면 같은 자리의 버튼이 터널을 연다.
                    bool dam = _grading != null && _grading.IsDamMode;
                    bool tunnelOn = _tunnel != null && _tunnel.IsOn;
                    bool padEdit = pad && !dam;
                    DamSpec siteDam = _sites != null ? SiteStructures.FindDam(_sites.CurrentFileName) : null;
                    TunnelSpec siteTunnel = _sites != null ? TunnelStructures.FindTunnel(_sites.CurrentFileName) : null;
                    Label(dam ? _grading.DamSpec.Title + " (" + _grading.DamSpec.TypeName + ") — 공사 전 계곡 위에 쌓습니다"
                        : tunnelOn ? _tunnel.Spec.Title + " — NATM 굴착 공정 (조건은 [장비], 재생은 [재생])"
                        : pad ? "부지 " + _grading.PadSizeLabel + ",  계획고 " + _grading.PadElevation.ToString("F2", Invariant) + " m"
                              : "부지가 없습니다. 아래 버튼은 화면 가운데에 놓습니다 (G 키는 마우스 위치).");
                    if (siteTunnel != null && !dam)
                    {
                        Buttons((tunnelOn ? "터널 끝내기" : siteTunnel.Name + " 굴착 (NATM)",
                            () => { if (_tunnel.IsOn) _tunnel.Stop(); else _tunnel.StartTunnel(siteTunnel); }, _tunnel != null));
                    }
                    else
                    {
                        Buttons((dam ? "댐 끝내기 (지금 지형으로)"
                                     : siteDam != null ? siteDam.Name + " 쌓기 (공개 제원)" : "구조물 (댐·터널 현장에서만)",
                            () => { if (dam) _grading.Clear(); else _grading.StartDam(siteDam); }, dam || (siteDam != null && _grading != null)));
                    }
                    Buttons(("부지 놓기 (G)", () => _grading.PlaceAtScreenCenter(), _grading != null),
                            (dam ? "댐 지우기 (H)" : "부지 지우기 (H)", () => _grading.Clear(), pad));
                    Row("계획고", padEdit ? _grading.PadElevation.ToString("F2", Invariant) + " m" : "-",
                        ("−10", () => _grading.ChangeElevation(-10), padEdit), ("−1", () => _grading.ChangeElevation(-1), padEdit),
                        ("+1", () => _grading.ChangeElevation(+1), padEdit), ("+10", () => _grading.ChangeElevation(+10), padEdit));
                    Buttons(("균형 계획고 찾기 (E) — 절토 × C = 성토", () => _grading.SetBalancedElevation(), padEdit));
                    Row("부지 크기 (9 0)", dam ? "-" : _grading.PadSizeLabel,
                        ("−", () => _grading.Resize(-2), padEdit), ("+", () => _grading.Resize(+2), padEdit));
                    Row("암반선 깊이 (X)", _grading.RockDepthLabel, ("바꾸기", () => _grading.CycleRockDepth(), padEdit));
                    Row("보는 면 (V)", pad ? _grading.ViewName : "-", ("바꾸기", () => _grading.CycleView(), pad));
                    break;

                case "장비":
                    if (_tunnel != null && _tunnel.IsOn)
                    {
                        // 터널 조건 (대안): 하루 작업 시간, 로더 대수, 가운데 사갱 막장
                        Row("하루 작업 (터널)", _tunnel.hoursPerDay.ToString("0", Invariant) + "시간",
                            ("−2", () => _tunnel.ChangeHours(-2), true), ("+2", () => _tunnel.ChangeHours(+2), true));
                        Row("로더 (버력처리)", _tunnel.loaders + "대",
                            ("−", () => _tunnel.ChangeLoaders(-1), true), ("+", () => _tunnel.ChangeLoaders(+1), true));
                        Row("가운데 사갱 막장", !_tunnel.Spec.HasMiddleAccess ? "자료 없음 (막장 2개)"
                                : _tunnel.useMiddleAccess ? "사용 (막장 4개)" : "없음 (막장 2개)",
                            ("바꾸기", () => _tunnel.ToggleMiddleAccess(), _tunnel.Spec.HasMiddleAccess));
                        Row("라이닝 거푸집 (튜브마다)", _tunnel.formsPerTube + "조",
                            ("바꾸기", () => _tunnel.CycleForms(), true));
                        Row("용수 (싸이클 할증)", _tunnel.waterIncrease > 0 ? "+30 % (3-2-1 주 ⑦)" : "없음",
                            ("바꾸기", () => _tunnel.ToggleWater(), true));
                        Row("라이닝 시작", _tunnel.concurrentLining ? "굴착과 병행 (막장 500 m 뒤)" : "관통 뒤",
                            ("바꾸기", () => _tunnel.ToggleConcurrentLining(), true));
                        Label("관통 " + _tunnel.FinishDay.ToString("N0", Invariant) + "일"
                              + (_tunnel.Spec.ExcavationStartKnown ? " (실제 " + _tunnel.Spec.ActualDays.ToString("N0", Invariant) + "일)" : " (실제 굴착 기간은 공개되지 않음)"));
                        Label("라이닝 끝 " + _tunnel.EndDay.ToString("N0", Invariant) + "일 (관통 + 대차 조립 4일 + span당 1일, 3-3-1)");
                        Label("싸이클: 표준품셈 토목 3-2-1 C군 · 패턴: 한국도로공사 P-1 ~ P-5");
                        break;
                    }
                    Row("굴착기 ( , . )", _grading.machines + "대",
                        ("−", () => _grading.ChangeMachines(-1), true), ("+", () => _grading.ChangeMachines(+1), true));
                    Row("덤프트럭 (Shift + , .)", _grading.trucks + "대",
                        ("−", () => _grading.ChangeTrucks(-1), true), ("+", () => _grading.ChangeTrucks(+1), true));
                    Row("하루 작업 ( ; ' )", _grading.hoursPerDay.ToString("0.#", Invariant) + "시간",
                        ("−", () => _grading.ChangeHours(-1), true), ("+", () => _grading.ChangeHours(+1), true));
                    Row("장비 간섭 가정 (I)", "r = " + _grading.interferenceRatio.ToString("0.##", Invariant),
                        ("바꾸기", () => _grading.CycleInterference(), true));
                    Label(pad ? "토공 공기 " + _grading.EarthworkDays + "일 (굴착과 운반 중 느린 쪽)" : "부지를 놓으면 공기가 나옵니다.");
                    break;

                case "재생":
                    if (_tunnel != null && _tunnel.IsOn)
                    {
                        Label(_tunnel.Day.ToString("F0", Invariant) + "일째 / 관통 " + _tunnel.FinishDay.ToString("N0", Invariant) + "일 · 라이닝 끝 "
                              + _tunnel.EndDay.ToString("N0", Invariant) + "일"
                              + (_tunnel.IsPlaying ? "  (재생 중)" : ""));
                        Buttons(("처음 (B)", () => _tunnel.Restart(), true), ("◀ 10일 (N)", () => _tunnel.StepDay(-10), true),
                                (_tunnel.IsPlaying ? "멈춤 (Space)" : "재생 (Space)", () => _tunnel.TogglePlay(), true),
                                ("10일 ▶ (M)", () => _tunnel.StepDay(+10), true));
                        Row("재생 속도", "× " + HudMode.PlaybackSpeed.ToString("0.#", Invariant), ("느리게", () => HudMode.ChangePlaybackSpeed(-1), true), ("빠르게", () => HudMode.ChangePlaybackSpeed(+1), true));
                        Label("터널은 땅속이라 지형 위 띠와 아래 종단면으로 보여 줍니다");
                        break;
                    }
                    Label(pad ? _grading.Day.ToString("F1", Invariant) + "일째 / " + _grading.TimelineTotalDays + "일"
                                + (_grading.IsPlaying ? "  (재생 중)" : "")
                              : "부지를 놓아야 재생할 수 있습니다.");
                    Buttons(("처음 (B)", () => _grading.Restart(), pad), ("◀ 하루 (N)", () => _grading.StepDay(-1), pad),
                            (_grading.IsPlaying ? "멈춤 (Space)" : "재생 (Space)", () => _grading.TogglePlay(), pad),
                            ("하루 ▶ (M)", () => _grading.StepDay(+1), pad));
                    Row("재생 속도", "× " + HudMode.PlaybackSpeed.ToString("0.#", Invariant), ("느리게", () => HudMode.ChangePlaybackSpeed(-1), true), ("빠르게", () => HudMode.ChangePlaybackSpeed(+1), true));
                    Row("보는 면 (V)", pad ? _grading.ViewName : "-", ("바꾸기", () => _grading.CycleView(), pad));
                    break;

                case "건물":
                    bool on = _building != null && _building.IsOn;
                    bool damMode = _grading != null && _grading.IsDamMode;
                    bool models = _building != null && _building.ShowingModels;
                    Buttons((damMode ? (models ? "장비 끄기 (T)" : "장비 보기 (T)") : on ? "건물·장비 끄기 (T)" : "건물·장비 보기 (T)",
                        () => _building.ToggleBuilding(), pad));
                    Row("층수 (K L)", on ? _building.floors + "층" : "-",
                        ("−", () => _building.ChangeFloors(-1), on), ("+", () => _building.ChangeFloors(+1), on));
                    Buttons((_building != null && _building.IsNear ? "현장 전체 보기 (F)" : "부지 가까이 보기 (F)", () => _building.ToggleNearView(), pad),
                            (_building != null && _building.ShowingSchedule ? "설명 보기 (P)" : "공정표 보기 (P)", () => _building.ToggleTable(), on));
                    Label(damMode ? "댐 축조 중에는 건물 없이 토공 장비만 보입니다 (재생하면 쌓는 동안)"
                        : on ? "왼쪽 글상자: 물량, 공기, 지금 하는 일과 장비" : "건물은 부지를 놓은 뒤에 켤 수 있습니다.");
                    break;

                case "대안":
                    // 예시 불러오기 (2026-11-20): README의 대표 예시를 버튼 하나로 다시 만든다.
                    Buttons(("예시 불러오기: 가상 부지 + 5층 건물, 장비 A · B · C", () =>
                        _exampleMessage = ExamplePreset.Apply(_sites, _grading, _alternatives), _alternatives != null));
                    if (!string.IsNullOrEmpty(_exampleMessage)) Label(_exampleMessage);
                    bool alt = _alternatives != null && pad;
                    Row("지금 조건 저장 (1 2 3)", "",
                        ("A", () => _alternatives.Save(0), alt), ("B", () => _alternatives.Save(1), alt), ("C", () => _alternatives.Save(2), alt));
                    Row("불러오기 (Shift + 숫자)", "",
                        ("A", () => _alternatives.Load(0), alt && _alternatives.HasSaved(0)),
                        ("B", () => _alternatives.Load(1), alt && _alternatives.HasSaved(1)),
                        ("C", () => _alternatives.Load(2), alt && _alternatives.HasSaved(2)));
                    Row("비교 화면 (O)", alt ? _alternatives.ModeName : "-", ("바꾸기", () => _alternatives.CycleMode(), alt));
                    // 회사 구조물의 네트워크 공정표·시공 단계 (2026-11-22 소양강댐, 2026-11-27 모든 구조물 공통)
                    var scheduleView = GetComponent<ScheduleView>();
                    ConstructionSchedule sched = scheduleView != null ? scheduleView.Current : null;
                    string kindName = sched != null ? ConstructionReport.KindName(sched.Kind) : "";
                    Buttons((sched != null ? kindName + " 네트워크 공정표 (시공 순서 · CPM)" : "네트워크 공정표 (회사 구조물 현장에서)",
                        () => { scheduleView.Toggle(); _open = -1; }, sched != null));
                    var sectionView = GetComponent<DamSectionView>();   // 시공 단계 (댐: 존 단면)
                    bool stages = sched != null && sectionView != null && sectionView.Available;
                    Buttons((sched == null ? "시공 단계 (회사 구조물 현장에서)"
                            : sched.Name == "소양강댐" ? "댐 시공 단계 · 존 단면 (한 단계씩)" : kindName + " 시공 단계 (한 단계씩)",
                        () => { sectionView.Toggle(); _open = -1; }, stages));
                    bool tunnelReport = _tunnel != null && _tunnel.IsOn;   // 터널 보기 중이면 터널 보고서 (2026-11-21)
                    Buttons((tunnelReport ? "보고서 저장 (터널 Markdown) → Reports 폴더" : "보고서 저장 (Markdown · CSV) → Reports 폴더",
                        () => { if (tunnelReport) _tunnel.SaveReport(); else _alternatives.SaveReport(); }, alt || tunnelReport));
                    // 자동 문장 (5-7): 마지막으로 저장한 대안 → 지금. 숫자만 풀어 쓴다.
                    List<CivilTwin.Core.AlternativeResult> results = alt ? _alternatives.CurrentResults() : null;
                    Label(results != null && results.Count > 1
                        ? "자동 문장  " + CivilTwin.Core.AlternativeNarrative.Short(results[results.Count - 1], results[0])
                        : "조건(대수·작업 시간·간섭)만 저장, 결과는 지금 현장으로 다시 계산");
                    if (ReportSavedPath() != "")
                    {
                        Label("저장함: Reports/" + System.IO.Path.GetFileName(ReportSavedPath()));
                    }
                    break;

                case "화면":
                    Row("글상자 내용", HudMode.Detail ? "자세히" : "간단", (HudMode.Detail ? "간단히" : "자세히", HudMode.ToggleDetail, true));
                    Row("한꺼번에 (Tab)", "전체", ("줄이기", HudMode.Cycle, true));
                    Row("글자 크기", HudMode.TextScaleName, ("바꾸기", HudMode.CycleTextScale, true));
                    // 항목별 켜기·끄기 (2026-11-21): 고른 값은 다음 실행에도 남는다
                    foreach (HudMode.Item item in HudMode.Items)
                    {
                        HudMode.Item it = item;
                        bool itemOn = HudMode.IsOn(it);
                        Row(HudMode.ItemName(it), itemOn ? "켜짐" : "꺼짐", (itemOn ? "끄기" : "켜기", () => HudMode.Toggle(it), true));
                    }
                    bool altOn = _alternatives != null && _grading.HasPad;
                    Row("대안 비교 (O)", altOn ? _alternatives.ModeName : "-", ("바꾸기", () => _alternatives.CycleMode(), altOn));
                    var capture = GetComponent<ScreenCaptureKey>();
                    Buttons(("처음 상태로", HudMode.RestoreDefaults, true), ("키 안내 (F1)", ToggleHelp, true),
                            ("화면 저장 (F12)", () => capture.Capture(), capture != null));
                    Label("카메라: 왼쪽 드래그 회전 · 오른쪽 이동 · 휠 확대 · R 처음 · 측정: 두 번 클릭");
                    break;
            }
        }

        private void ToggleSites()
        {
            _showSites = !_showSites;
            if (_showSites) { _open = -1; _showHelp = false; }
        }

        /// <summary>
        /// 현장 목록: 번호 · 회사 · 현장 이름, 그 현장에 있는 구조물 모드(댐·터널) 표시. 누르면 바로 그 현장으로 가고 목록을 닫는다.
        /// </summary>
        private void DrawSites(float x, float y)
        {
            int n = _sites.SiteCount;
            float height = Padding * 2 + n * (RowHeight + Gap);
            var back = new Rect(x, y, Width, height);
            GUI.Box(back, GUIContent.none, _box);
            HudLayout.Register(back);
            float py = y + Padding;
            for (int i = 0; i < n; i++)
            {
                int index = i;
                string file = _sites.FileNameAt(i);
                string kind = file.Length == 0 ? "" : CivilTwin.Core.SiteStructures.FindDam(file) != null ? "   [댐]"
                    : CivilTwin.Core.TunnelStructures.FindTunnel(file) != null ? "   [터널]" : "";
                DrawButton(new Rect(x + Padding, py, Width - Padding * 2, RowHeight), (i + 1) + ". " + _sites.NameAt(i) + kind,
                    () => { _showSites = false; if (index != _sites.CurrentIndex) _sites.Show(index); }, true, i == _sites.CurrentIndex);
                py += RowHeight + Gap;
            }
        }

        /// <summary>키 안내를 열고 닫는다. 열 때는 펼친 메뉴를 접는다 (판 아래의 메뉴 버튼이 눌리지 않게).</summary>
        private void ToggleHelp()
        {
            _showHelp = !_showHelp;
            if (_showHelp) _open = -1;
        }

        /// <summary>키 안내 판: 화면 가운데, 키 | 하는 일 두 열. 닫기 버튼·F1·Esc로 닫는다.</summary>
        private void DrawHelp()
        {
            const float w = 600f, rowH = 22f;
            float h = 56f + HelpKeys.Length * rowH + RowHeight + 12f;
            var r = new Rect((HudMode.GuiWidth - w) / 2f, Mathf.Max(Top, (HudMode.GuiHeight - h) / 2f), w, h);
            // 반투명 상자를 세 번 겹쳐 거의 불투명하게 (뒤의 글상자가 비쳐 읽기 어려웠다 — 2026-11-21 캡처)
            for (int i = 0; i < 3; i++) GUI.Box(r, GUIContent.none, _box);
            HudLayout.Register(r);
            GUI.Label(new Rect(r.x + 14f, r.y + 8f, w - 28f, RowHeight), "키 안내 — 메뉴 버튼으로도 모두 할 수 있습니다 (괄호 안이 같은 키)", _title);
            float y = r.y + 40f;
            foreach (var (key, what) in HelpKeys)
            {
                GUI.Label(new Rect(r.x + 18f, y, 250f, rowH), key, _label);
                GUI.Label(new Rect(r.x + 280f, y, w - 296f, rowH), what, _label);
                y += rowH;
            }
            DrawButton(new Rect(r.x + w - 130f, r.yMax - RowHeight - 8f, 116f, RowHeight), "닫기 (F1·Esc)", () => _showHelp = false, true);
        }

        // ---------- 줄 그리기 도우미 ----------

        private void Label(string text)
        {
            GUI.Label(new Rect(_px, _py, Width - Padding * 2, RowHeight), text, _label);
            _py += RowHeight + Gap;
        }

        /// <summary>한 줄을 버튼 몇 개로 똑같이 나눈다.</summary>
        private void Buttons(params (string text, Action action, bool enabled)[] buttons)
        {
            float w = (Width - Padding * 2 - Gap * (buttons.Length - 1)) / buttons.Length;
            for (int i = 0; i < buttons.Length; i++)
            {
                var r = new Rect(_px + (w + Gap) * i, _py, w, RowHeight);
                DrawButton(r, buttons[i].text, buttons[i].action, buttons[i].enabled);
            }
            _py += RowHeight + Gap;
        }

        /// <summary>"이름   값   [버튼][버튼]…" 한 줄. 버튼은 오른쪽에 붙는다.</summary>
        private void Row(string name, string value, params (string text, Action action, bool enabled)[] buttons)
        {
            float bw = buttons.Length >= 3 ? 46f : 62f;
            float buttonsWidth = buttons.Length * bw + (buttons.Length - 1) * Gap;
            float right = _px + Width - Padding * 2;
            GUI.Label(new Rect(_px, _py, 170f, RowHeight), name, _label);
            GUI.Label(new Rect(_px + 172f, _py, right - buttonsWidth - _px - 176f, RowHeight), value, _title);
            for (int i = 0; i < buttons.Length; i++)
            {
                var r = new Rect(right - buttonsWidth + (bw + Gap) * i, _py, bw, RowHeight);
                DrawButton(r, buttons[i].text, buttons[i].action, buttons[i].enabled);
            }
            _py += RowHeight + Gap;
        }

        private void DrawButton(Rect r, string text, Action action, bool enabled, bool selected = false)
        {
            bool hover = false;
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 p = mouse.position.ReadValue();
                hover = r.Contains(HudMode.MouseToGui(p));
            }
            GUIStyle style = !enabled ? _buttonOff : (selected ? _tabOn : (hover ? _buttonHover : _button));
            GUI.Box(r, text, style);
            _drawing.Add(new Button { Rect = r, Action = action, Enabled = enabled, Text = text });
            HudLayout.Register(r);
        }

        private void EnsureStyles()
        {
            if (_box != null) return;

            // 메뉴 배경은 불투명하게: 반투명이면 펼친 메뉴 뒤의 글상자 글자가 비쳐 읽기 어렵다 (2026-11-20 캡처).
            _box = MakeButton(new Color(0.11f, 0.12f, 0.14f, 0.97f), Color.white);
            _label = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            _label.normal.textColor = HudStyle.Ui(new Color(0.85f, 0.88f, 0.92f));
            _title = new GUIStyle(_label) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
            _title.normal.textColor = HudStyle.Ui(Color.white);

            _button = MakeButton(new Color(0.22f, 0.25f, 0.30f, 0.95f), Color.white);
            _buttonHover = MakeButton(new Color(0.32f, 0.38f, 0.48f, 0.98f), Color.white);
            _buttonOff = MakeButton(new Color(0.18f, 0.18f, 0.20f, 0.8f), new Color(0.55f, 0.55f, 0.55f));
            _tabOn = MakeButton(new Color(0.90f, 0.70f, 0.20f, 0.98f), new Color(0.10f, 0.10f, 0.10f));
        }

        private static GUIStyle MakeButton(Color background, Color text)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, HudStyle.Ui(background));
            tex.Apply();
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                clipping = TextClipping.Clip,
            };
            style.normal.background = tex;
            style.normal.textColor = HudStyle.Ui(text);
            return style;
        }
    }
}
