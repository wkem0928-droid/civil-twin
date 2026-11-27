using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CivilTwin.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CivilTwin.Runtime
{
    /// <summary>
    /// 댐 시공 단계 · 존 단면 화면 (2026-11-22, 메뉴 [대안] → 댐 시공 단계 · 존 단면).
    /// 개발자 의견: "댐이 차례차례 어떻게 만들어지는지", "Space 한 번에 끝나지 않게", "공정이라도 차근차근 확실히".
    ///
    /// 댐 축에 수직인 단면(왼쪽 = 상류, 가로·세로 같은 축척)을 날짜에 맞춰 그린다:
    ///   강이 흐르는 계곡 → 유수전환(물이 가배수터널로) → 퇴적층 굴착 → 그라우트 커튼 → 코어·필터·사력을 같은 높이로 성토 → 담수 → 준공.
    /// 단계 버튼(12개)으로 바로 가거나 [◀ 이전 단계] [다음 단계 ▶], [◀ 1개월] [1개월 ▶]로 넘긴다.
    /// [재생]은 한 달씩 나아가다 **다음 단계에서 멈춘다** (한 번에 끝나지 않게).
    /// 오른쪽 글: 그 단계의 설명, 그날 진행 중인 작업(네트워크 공정표와 같은 자료), 성토 진행·존별 체적, 가정.
    /// 계산은 Core/DamZoneSection (테스트 DamZoneSectionTests).
    /// </summary>
    public sealed class DamSectionView : MonoBehaviour
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly Color CoreColor = new Color(0.78f, 0.50f, 0.28f);
        private static readonly Color FilterColor = new Color(0.93f, 0.84f, 0.55f);
        private static readonly Color ShellColor = new Color(0.60f, 0.61f, 0.60f);
        private static readonly Color RockColor = new Color(0.33f, 0.29f, 0.27f);
        private static readonly Color SedimentColor = new Color(0.62f, 0.55f, 0.40f);
        private static readonly Color WaterColor = new Color(0.30f, 0.55f, 0.92f, 0.75f);
        private static readonly Color GroutColor = new Color(0.55f, 0.85f, 1.00f);

        /// <summary>그림용 값 (자료 없음): 하상 퇴적층 두께, 그라우트 커튼 깊이</summary>
        private const double SedimentThickness = 6.0, CurtainDepth = 45.0;

        private static readonly DateTime DiversionDate = new DateTime(1969, 10, 1);
        private static readonly DateTime ExcavationStart = new DateTime(1969, 10, 24), GroutStart = new DateTime(1970, 1, 1);
        private static readonly DateTime ImpoundStart = new DateTime(1972, 2, 1);

        private SiteSwitcher _sites;
        private string _file = "";
        private ConstructionSchedule _schedule;
        private DamZoneSection _section;
        private List<ConstructionStep> _steps;
        private DateTime _date;
        private bool _playing;
        private float _playTimer;
        private GUIStyle _label, _small, _title, _text;

        public bool IsOpen { get; private set; }
        public bool Available => Load();
        public DateTime CurrentDate => _date;

        /// <summary>3D로 보는 중이면 true (DamStage3D가 이 값을 보고 장면을 켜고 끈다)</summary>
        public bool Is3D { get; private set; }

        /// <summary>3D에서 댐을 계곡 가운데에서 반 잘라 존 단면을 보이면 true</summary>
        public bool Cut3D { get; private set; }

        /// <summary>지금 쓰는 존 단면·성토 가정 (3D·보고서도 이것을 쓴다)</summary>
        public DamZoneSection Section => _section;

        /// <summary>교량 현장이면 그 교량 (BridgeStage3D가 본다), 지금 현장의 공정</summary>
        public BridgeSpec Bridge => _bridge;
        public ConstructionSchedule Schedule => _schedule;

        /// <summary>
        /// 성토 끝 가정 (2026-11-26): false = 본댐 완공 기록 1972.11.10에 마루, true = 기사 [D] "8·19 홍수 때 이미 123 m"대로 1972.8.19에 마루.
        /// 두 기록이 맞지 않아 어느 쪽인지 정하지 않고 바꿔 볼 수 있게 한다.
        /// </summary>
        public bool CrestByFlood { get; private set; }

        public void ToggleCrestScenario()
        {
            if (!Load() || _section == null) return;
            CrestByFlood = !CrestByFlood;
            _section = DamZoneSection.Soyanggang(CrestByFlood);
        }

        public void Initialize(SiteSwitcher sites)
        {
            _sites = sites;
            IsOpen = false;
        }

        public void Toggle()
        {
            IsOpen = !IsOpen && Available;
            _playing = false;
            if (IsOpen)
            {
                var schedule = GetComponent<ScheduleView>();
                if (schedule != null && schedule.IsOpen) schedule.Toggle();   // 두 화면은 하나씩
            }
        }

        /// <summary>공정표에서 넘어올 때: 그 날짜로 연다</summary>
        public void OpenAt(DateTime date)
        {
            if (!Available) return;
            _date = Clamp(date);
            if (!IsOpen) Toggle();
        }

        private bool Load()
        {
            string file = _sites != null ? _sites.CurrentFileName : "";
            if (file != _file)
            {
                _file = file;
                _schedule = ScheduleCatalog.Find(file);
                // 존 단면·3D는 소양강댐만 (자료가 있는 댐). 다른 구조물은 공통 그림(터널 종단 막대, 댐 단면 윤곽)으로 그린다 (2026-11-27).
                _section = _schedule != null && _schedule.Name == "소양강댐" ? DamZoneSection.Soyanggang(CrestByFlood) : null;
                _tunnel = _schedule != null && _schedule.Kind == StructureKind.Tunnel ? FindTunnel(file) : null;
                _dam = _schedule != null && _schedule.Kind == StructureKind.Dam && _section == null ? SiteStructures.FindDam(file) : null;
                _bridge = _schedule != null && _schedule.Kind == StructureKind.Bridge ? BridgeStructures.Find(file) : null;
                _plant = _schedule != null && _schedule.Kind == StructureKind.Dam && _section == null ? PumpedStorageStructures.Find(file) : null;
                Is3D = Is3D && (_section != null || _bridge != null);
                _steps = _schedule != null ? _schedule.Steps : null;
                if (_steps != null) _date = _steps[0].Date;
                if (_schedule == null) IsOpen = false;
            }
            return _schedule != null;
        }

        private int StepIndex
        {
            get
            {
                int k = 0;
                for (int i = 0; i < _steps.Count; i++) if (_steps[i].Date <= _date) k = i;
                return k;
            }
        }

        private DateTime Clamp(DateTime d)
        {
            DateTime a = _steps[0].Date, b = _steps[_steps.Count - 1].Date;
            return d < a ? a : d > b ? b : d;
        }

        private void GoStep(int k)
        {
            _playing = false;
            _date = _steps[Mathf.Clamp(k, 0, _steps.Count - 1)].Date;
        }

        private void MoveMonths(int months)
        {
            _playing = false;
            _date = Clamp(_date.AddMonths(months));
        }

        private void TogglePlay()
        {
            if (!_playing && StepIndex >= _steps.Count - 1) _date = _steps[0].Date;   // 끝에서 누르면 처음부터
            _playing = !_playing;
            _playTimer = 0f;
        }

        private void Update()
        {
            if (!IsOpen || !Load()) return;
            if (InputGate.Allowed)
            {
                Keyboard k = Keyboard.current;
                if (k != null && k.escapeKey.wasPressedThisFrame) { IsOpen = false; _playing = false; return; }
            }
            if (!_playing) return;

            // 한 달에 0.4초 (재생 속도 반영). 다음 단계 날짜에 닿으면 멈춘다.
            _playTimer += Time.unscaledDeltaTime * HudMode.PlaybackSpeed;
            while (_playTimer >= 0.4f && _playing)
            {
                _playTimer -= 0.4f;
                int k = StepIndex;
                DateTime next = k + 1 < _steps.Count ? _steps[k + 1].Date : _steps[k].Date;
                DateTime d = _date.AddMonths(1);
                if (d >= next) { _date = next; _playing = false; }
                else _date = d;
            }
        }

        private void OnGUI()
        {
            HudMode.ApplyMatrix();
            if (!IsOpen || !Load() || !HudMode.ShowBoxes) return;
            GUI.depth = -5;
            if (_label == null)
            {
                _label = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleLeft, wordWrap = false };
                _small = new GUIStyle(_label) { fontSize = 11 };
                _title = new GUIStyle(_label) { fontSize = 15 };
                _text = new GUIStyle(_label) { fontSize = 12, wordWrap = true, alignment = TextAnchor.UpperLeft, richText = true };
            }
            _label.normal.textColor = _small.normal.textColor = _title.normal.textColor = _text.normal.textColor = HudStyle.Ui(HudStyle.Text);

            float top = ControlBar.BarRect.height > 0f ? ControlBar.BarRect.yMax + 8f : 10f;
            // 3D: 위쪽 조작 줄만 상자로 두고 가운데는 3D 장면이 보이게 한다. 단면: 화면 전체.
            const float headerH = 128f;
            var panel = new Rect(12f, top, HudMode.GuiWidth - 24f, Is3D ? headerH : HudMode.GuiHeight - top - 12f);
            for (int i = 0; i < 2; i++) HudStyle.DrawBox(panel);
            HudLayout.Register(panel);

            int step = StepIndex;
            ConstructionStep st = _steps[step];
            double fill = _section != null ? _section.FillElevation(_date) : 0.0;

            // 1줄: 제목, 3D ↔ 단면, 닫기
            string heading = _bridge != null && Is3D ? _schedule.Name + " 시공 단계 — 3D (실제 노선, 마우스로 돌려 보기)   "
                : _section == null ? _schedule.Name + " 시공 단계 — " + GenericCaption() + "   "
                : Is3D ? "소양강댐 시공 단계 — 3D (마우스로 돌려 보기, 휠 확대)   "
                : "소양강댐 시공 단계 — 존 단면 (댐 축에 수직, 왼쪽 = 상류, 가로·세로 같은 축척)   ";
            GUI.Label(new Rect(panel.x + 12f, panel.y + 6f, panel.width - 410f, 22f), heading + _date.ToString("yyyy.M.d", Invariant), _title);
            if (_section != null)
                InfoBox.Button(new Rect(panel.xMax - 384f, panel.y + 6f, 164f, 22f),
                    CrestByFlood ? "마루 도달: 8·19 (기사)" : "마루 도달: 11.10 (완공)", ToggleCrestScenario, CrestByFlood);
            if (_section != null || _bridge != null)
                InfoBox.Button(new Rect(panel.xMax - 214f, panel.y + 6f, 110f, 22f), Is3D ? (_bridge != null ? "측면으로 보기" : "단면으로 보기") : "3D로 보기", () => Is3D = !Is3D, Is3D);
            InfoBox.Button(new Rect(panel.xMax - 96f, panel.y + 6f, 84f, 22f), "닫기 (Esc)", () => { IsOpen = false; _playing = false; });

            // 2줄: 넘기기·재생·공정표
            float bx = panel.x + 12f, by = panel.y + 34f;
            InfoBox.Button(new Rect(bx, by, 96f, 24f), "◀ 이전 단계", () => GoStep(StepIndex - (_date > _steps[StepIndex].Date ? 0 : 1)));
            InfoBox.Button(new Rect(bx + 100f, by, 96f, 24f), "다음 단계 ▶", () => GoStep(StepIndex + 1));
            InfoBox.Button(new Rect(bx + 206f, by, 72f, 24f), "◀ 1개월", () => MoveMonths(-1));
            InfoBox.Button(new Rect(bx + 282f, by, 72f, 24f), "1개월 ▶", () => MoveMonths(+1));
            InfoBox.Button(new Rect(bx + 364f, by, 120f, 24f), _playing ? "■ 멈춤" : "▶ 다음 단계까지", TogglePlay, _playing);
            InfoBox.Button(new Rect(bx + 494f, by, 150f, 24f), "공정표에서 이 날짜 보기", OpenSchedule);
            InfoBox.Button(new Rect(bx + 650f, by, 94f, 24f), "보고서 저장", () =>
            {
                var schedule = GetComponent<ScheduleView>();
                if (schedule != null) schedule.SaveReport();
            });
            GUI.Label(new Rect(bx + 756f, by, panel.xMax - bx - 768f, 24f),
                "단계 " + (step + 1) + " / " + _steps.Count + "  " + st.Title, _label);

            // 3줄: 단계 버튼
            float sy = by + 30f, sw = (panel.width - 24f) / _steps.Count;
            for (int i = 0; i < _steps.Count; i++)
            {
                int k = i;
                InfoBox.Button(new Rect(panel.x + 12f + i * sw, sy, sw - 4f, 22f), (i + 1) + " " + ShortName(_steps[i].Title), () => GoStep(k), i == step);
            }

            // 4줄: 시간 막대
            var timeline = new Rect(panel.x + 12f, sy + 28f, panel.width - 24f, 18f);
            DrawTimeline(timeline);

            if (Is3D)
            {
                if (_bridge != null)
                {
                    var box = new Rect(panel.x, HudMode.GuiHeight - 162f, 420f, 150f);   // 왼쪽 아래 (주탑을 가리지 않게)
                    HudStyle.DrawBox(box);
                    HudLayout.Register(box);
                    BridgeState bs = BridgeProgress.At(_bridge, _schedule, _date);
                    GUI.Label(new Rect(box.x + 8f, box.y + 6f, box.width - 16f, box.height - 12f),
                        "<b>" + st.Title + "</b>  (" + _date.ToString("yyyy.M.d", Invariant) + ")\n" + st.Text + "\n\n"
                        + "상판 " + (bs.DeckLength / 1000.0).ToString("0.00", Invariant) + " km 놓임, 교각 " + bs.Piers.Count + "개, 주탑 "
                        + bs.PylonHeight.ToString("0", Invariant) + " m, 케이블 " + bs.Cables.Count + "개 (그림용 60 m 간격)\n"
                        + "<color=#bbbbbb>노선: OpenStreetMap. 크기·순서는 표준 공정(가정)의 진행률로 그렸다.</color>", _text);
                }
                else Draw3DPanels(panel.yMax + 8f, st, fill);
                HudToast.Draw();
                return;
            }

            // 아래: 왼쪽 단면, 오른쪽 설명
            float contentTop = timeline.yMax + 8f;
            float textW = Mathf.Clamp(panel.width * 0.36f, 300f, 460f);
            var drawing = new Rect(panel.x + 12f, contentTop, panel.width - textW - 36f, panel.yMax - contentTop - 10f);
            var text = new Rect(drawing.xMax + 12f, contentTop, textW, drawing.height);
            if (_section != null)
            {
                DrawSection(drawing, fill);
                DrawText(text, st, fill);
            }
            else
            {
                if (_tunnel != null) DrawTunnelBar(drawing);
                else if (_dam != null) DrawDamOutline(drawing);
                else if (_bridge != null) DrawBridgeElevation(drawing);
                else if (_plant != null) DrawPumpedStorage(drawing);
                HudStyle.DrawBox(text);
                GUI.Label(new Rect(text.x + 8f, text.y + 6f, text.width - 16f, text.height - 12f), BuildGenericText(st), _text);
            }
            HudToast.Draw();
        }

        // ── 공통 그림 (소양강댐 말고 다른 구조물, 2026-11-27) ─────────────

        private TunnelSpec _tunnel;
        private DamSpec _dam;
        private static readonly Color ExcavatedColor = new Color(0.47f, 0.40f, 0.33f);
        private static readonly Color LiningColor = new Color(0.86f, 0.86f, 0.84f);
        private static readonly Color SlabColor = new Color(0.80f, 0.82f, 0.86f);

        private static TunnelSpec FindTunnel(string file)
        {
            foreach (TunnelSpec t in TunnelStructures.Tunnels)
                if (string.Equals(t.FileName, file, StringComparison.OrdinalIgnoreCase)) return t;
            return null;
        }

        private ScheduleWork Work(string name)
        {
            foreach (ScheduleWork w in _schedule.Works) if (w.Name == name) return w;
            return null;
        }

        private ScheduleWork MainWork() =>
            _schedule.Kind == StructureKind.Tunnel ? Work("본선 굴착 (양 끝 막장)")
            : _schedule.Kind == StructureKind.Bridge ? Work("사장교 상판·케이블")
            : Work("본댐 축조 (석괴)") ?? Work("본댐 성토") ?? Work("수로터널·수직갱");

        private string GenericCaption() =>
            _tunnel != null ? "터널 종단 (막장 진행·라이닝, 일정 속도 그림용)" : _dam != null ? "댐 단면 (왼쪽 = 상류, 축조 높이 그림용)"
            : _bridge != null ? "교량 측면 (왼쪽 = 영종, 세로 과장)"
            : _plant != null ? "상부댐 · 하부댐 단면과 지하 작업 (그림용)" : "공정";

        private BridgeSpec _bridge;
        private PumpedStoragePlant _plant;
        private static readonly Color DeckColor = new Color(0.82f, 0.82f, 0.80f);
        private static readonly Color PierColor = new Color(0.62f, 0.62f, 0.60f);
        private static readonly Color CableColor = new Color(1f, 0.92f, 0.55f, 0.9f);

        /// <summary>
        /// 교량 측면 (서 = 영종 → 동 = 송도). 그날 각 작업의 진행률(표준 공정, 그림용)로 교각·주탑·상판을 그린다.
        ///   고가교: 육지 쪽 끝에서 바다 쪽으로 50 m 경간이 하나씩 얹힌다 (FSLM).
        ///   접속교: 교각마다 양쪽으로 같은 길이만큼 내민다 (FCM).
        ///   사장교: 주탑이 오르고, 다 오르면 주탑에서 양쪽으로 상판을 내밀며 케이블을 건다. 주경간 가운데에서 만난다.
        /// 상판 높이: 고가교 해수면 위 20 m(가정), 주경간 선박 통과 높이 + 3 m, 접속교는 그 사이를 직선으로.
        /// </summary>
        private void DrawBridgeElevation(Rect r)
        {
            BridgeSpec b = _bridge;
            BridgeSegment wv = b.Segment("서측 고가교"), wa = b.Segment("서측 접속교"), cs = b.CableStayed, ea = b.Segment("동측 접속교"), ev = b.Segment("동측 고가교");
            double s0 = wv.From - 300, s1 = ev.To + 300;
            float seaY = r.y + r.height - 46f;
            float vscale = (seaY - r.y - 30f) / (float)(b.PylonHeight + 15.0);
            float X(double s) => r.x + 10f + (r.width - 20f) * (float)((s - s0) / (s1 - s0));
            float Y(double h) => seaY - (float)h * vscale;
            double csDeck = b.NavigationClearance + 3.0, vDeck = b.ViaductDeckHeight;
            double DeckH(double s) =>
                s >= cs.From && s <= cs.To ? csDeck
                : s > wa.From && s < cs.From ? vDeck + (csDeck - vDeck) * (s - wa.From) / wa.Length
                : s > cs.To && s < ea.To ? csDeck + (vDeck - csDeck) * (s - cs.To) / ea.Length
                : vDeck;

            Fill(Span(r.x, r.xMax, seaY, seaY + 30f), WaterColor);
            Fill(Span(r.x, X(wv.From), Y(8), seaY + 30f), new Color(0.40f, 0.47f, 0.33f));   // 영종도
            Fill(Span(X(ev.To), r.xMax, Y(8), seaY + 30f), new Color(0.40f, 0.47f, 0.33f));   // 송도

            double sub = Progress(Work("접속교·고가교 하부"));
            double fw = Progress(Work("서측 고가교 상판 (FSLM)")), fe = Progress(Work("동측 고가교 상판 (FSLM)"));
            double fcm = Progress(Work("접속교 상판 (FCM)"));
            double fdn = Progress(Work("주탑 기초")), py = Progress(Work("주탑 (" + b.PylonHeight.ToString("0.#", Invariant) + " m)"));
            double deck = Progress(MainWork());

            // 고가교: 교각(육지 쪽부터 하부 진행률만큼) + 얹힌 경간
            void Viaduct(BridgeSegment g, bool fromWest, double piers, double decks)
            {
                int n = StandardSchedules.FslmSpans(g);
                for (int i = 0; i <= n; i++)
                {
                    int k = fromWest ? i : n - i;
                    if (i > piers * n) break;
                    double s = g.From + g.Length * k / n;
                    if (i % 3 == 0) Fill(new Rect(X(s) - 0.5f, Y(vDeck), 1.5f, seaY - Y(vDeck)), PierColor);
                }
                double done = decks * g.Length;
                double a = fromWest ? g.From : g.To - done, c = fromWest ? g.From + done : g.To;
                if (done > 0) Fill(Span(X(a), X(c), Y(vDeck) - 2f, Y(vDeck) + 1f), DeckColor);
            }
            Viaduct(wv, true, sub, fw);
            Viaduct(ev, false, sub, fe);

            // 접속교: 교각 (145 m 대표 간격) + 양쪽으로 내민 상판
            foreach (BridgeSegment g in new[] { wa, ea })
            {
                int n = (int)Math.Round(g.Length / g.Span);
                for (int i = 1; i < n + 1; i++)
                {
                    double s = g.From + g.Length * (i - 0.5) / n;
                    if (sub < (double)i / (n + 1)) continue;
                    float top = Y(DeckH(s));
                    Fill(new Rect(X(s) - 1f, top, 2.5f, seaY - top), PierColor);
                    double arm = fcm * g.Length / n / 2.0;
                    if (arm > 0) Fill(Span(X(s - arm), X(s + arm), top - 2f, top + 1f), DeckColor);
                }
            }

            // 사장교: 주탑 기초 → 주탑 → 상판 내밀기 + 케이블
            double h = py * b.PylonHeight;
            foreach (double ps in b.PylonStations)
            {
                if (fdn > 0) Fill(Span(X(ps) - 5f, X(ps) + 5f, Y(0) - 3f, seaY + 6f), PierColor);
                if (h > 0) Fill(new Rect(X(ps) - 2f, Y(h), 4f, seaY - Y(h)), new Color(0.72f, 0.72f, 0.70f));
                double main = deck * b.CableStayedSpans[2] / 2.0;
                double side = Math.Min(b.CableStayedSpans[0] + b.CableStayedSpans[1], main);
                bool west = ps < cs.From + (cs.To - cs.From) / 2.0;
                double back = west ? ps - side : ps + side, front = west ? ps + main : ps - main;
                if (main > 0)
                {
                    Fill(Span(X(Math.Min(back, front)), X(Math.Max(back, front)), Y(csDeck) - 2f, Y(csDeck) + 1f), DeckColor);
                    // 케이블: 주탑 위쪽 1/3에서 상판까지 60 m 간격 (점선)
                    for (double d = 60; d <= main + 1e-6; d += 60)
                        foreach (double sign in new[] { -1.0, 1.0 })
                        {
                            double ds = sign * d;
                            if (Math.Abs(ds) > (sign > 0 == west ? main : side)) continue;
                            float x1 = X(ps), y1 = Y(b.PylonHeight * (0.68 + 0.3 * d / 400.0)), x2 = X(ps + ds), y2 = Y(csDeck);
                            for (int q = 1; q < 10; q++) Fill(new Rect(x1 + (x2 - x1) * q / 10f, y1 + (y2 - y1) * q / 10f, 1.5f, 1.5f), CableColor);
                        }
                }
                if (ps == b.PylonStations[0] && h > 0.5)   // 이름표는 한 주탑에만 (두 주탑이 가까워 겹친다)
                    GUI.Label(new Rect(X(ps) - 110f, Y(h) - 4f, 104f, 16f), "주탑 " + h.ToString("0", Invariant) + " m", _small);
            }
            if (deck >= 1.0) Fill(Span(X(cs.From), X(cs.To), Y(csDeck) - 2f, Y(csDeck) + 1f), DeckColor);
            // 선박 통과 높이 표시
            float mx = X((b.PylonStations[0] + b.PylonStations[1]) / 2.0);
            for (float yy = Y(b.NavigationClearance); yy < seaY; yy += 6f) Fill(new Rect(mx, yy, 1f, 3f), new Color(1f, 1f, 1f, 0.6f));
            GUI.Label(new Rect(mx + 4f, Y(b.NavigationClearance / 2.0), 120f, 16f), "선박 통과 " + b.NavigationClearance.ToString("0") + " m", _small);

            // 구간 이름
            foreach (BridgeSegment g in b.Segments)
            {
                float a = X(g.From), c = X(g.To);
                Fill(new Rect(a, seaY + 8f, 1f, 10f), new Color(1f, 1f, 1f, 0.6f));
                GUI.Label(new Rect(a + 2f, seaY + 8f, Mathf.Max(60f, c - a), 16f),
                    g.Name == "서측 접속교" || g.Name == "동측 접속교" ? "접속" : g.Name + " " + (g.Length / 1000.0).ToString("0.0#", Invariant) + " km", _small);
            }
            GUI.Label(new Rect(r.x + 4f, r.y + 2f, 300f, 16f), "← 영종도", _small);
            GUI.Label(new Rect(r.xMax - 70f, r.y + 2f, 70f, 16f), "송도 →", _small);
            float horizontal = (float)((s1 - s0) / (r.width - 20f));
            GUI.Label(new Rect(r.x + 4f, seaY + 26f, r.width, 16f), "세로를 약 " + (horizontal / (1f / vscale)).ToString("0", Invariant) + "배 과장 (가로 " + ((s1 - s0) / 1000.0).ToString("0.0", Invariant) + " km)", _small);
        }

        private double Progress(ScheduleWork w) => w == null ? 0.0 : Progress(w.Start, w.Finish);

        /// <summary>
        /// 터널 종단 막대: 막장은 모두 같은 속도로 판다고 보고(그림용), 관통일에 만나도록 속도를 정한다.
        ///   막장-일 합계 = 2 × (관통 − 굴착 시작) + 2 × (관통 − 사갱 막장 시작),  속도 v = 길이 ÷ 막장-일
        /// 라이닝은 양 끝에서 안쪽으로 같은 비율로 친다고 그린다 (거푸집 2조).
        /// </summary>
        private void DrawTunnelBar(Rect r)
        {
            TunnelSpec t = _tunnel;
            double L = t.OfficialLength;
            ScheduleWork main = MainWork(), fromAccess = Work("본선 굴착 (사갱 막장)"), lining = Work("방수·2차 라이닝");
            if (main == null) return;
            double days = (main.Finish - main.Start).TotalDays;
            double headingDays = 2.0 * days + (fromAccess != null ? 2.0 * (fromAccess.Finish - fromAccess.Start).TotalDays : 0.0);
            double v = L / headingDays;
            double tMain = Math.Max(0.0, Math.Min(days, (_date - main.Start).TotalDays));
            double west = v * tMain, east = L - v * tMain;
            double mid = L * t.MiddleAccessFraction, midHalf = 0.0;
            if (fromAccess != null) midHalf = v * Math.Max(0.0, Math.Min((fromAccess.Finish - fromAccess.Start).TotalDays, (_date - fromAccess.Start).TotalDays));
            double lined = lining != null ? Progress(lining) * L / 2.0 : 0.0;

            var bar = new Rect(r.x + 30f, r.y + r.height * 0.42f, r.width - 60f, 28f);
            float X(double s) => bar.x + bar.width * (float)(s / L);
            // 산(위쪽 그림용 윤곽) — 가운데가 높다
            for (int i = 0; i < 60; i++)
            {
                float x0 = bar.x + bar.width * i / 60f, w = bar.width / 60f;
                float h = (float)(Math.Sin(Math.PI * (i + 0.5) / 60.0) * r.height * 0.32);
                Fill(new Rect(x0, bar.y - h, w + 0.5f, h), new Color(0.36f, 0.42f, 0.30f, 0.6f));
            }
            Fill(bar, RockColor);
            Fill(Span(X(0), X(Math.Min(west, L)), bar.y, bar.yMax), ExcavatedColor);
            Fill(Span(X(Math.Max(east, 0)), X(L), bar.y, bar.yMax), ExcavatedColor);
            if (midHalf > 0) Fill(Span(X(Math.Max(0, mid - midHalf)), X(Math.Min(L, mid + midHalf)), bar.y, bar.yMax), ExcavatedColor);
            if (lined > 0)
            {
                Fill(Span(X(0), X(lined), bar.y + 9f, bar.yMax - 9f), LiningColor);
                Fill(Span(X(L - lined), X(L), bar.y + 9f, bar.yMax - 9f), LiningColor);
            }
            bool through = _date >= main.Finish;
            if (!through && _date >= main.Start)
            {
                foreach (double s in new[] { west, east }) Fill(new Rect(X(s) - 1.5f, bar.y - 6f, 3f, bar.height + 12f), HudStyle.Accent);
                if (midHalf > 0)
                    foreach (double s in new[] { mid - midHalf, mid + midHalf }) Fill(new Rect(X(s) - 1.5f, bar.y - 6f, 3f, bar.height + 12f), HudStyle.Accent);
            }
            if (fromAccess != null)   // 사갱 (비스듬한 작업 터널)
            {
                for (int i = 0; i < 12; i++) Fill(new Rect(X(mid) + i * 4f, bar.y - 4f - i * 3.5f, 4f, 3f), _date >= Work("사갱 굴착").Start ? ExcavatedColor : RockColor);
                GUI.Label(new Rect(X(mid) + 10f, bar.y - 62f, 120f, 16f), "사갱", _small);
            }
            GUI.Label(new Rect(bar.x - 4f, bar.yMax + 6f, 160f, 16f), "서쪽 입구 (0 m)", _small);
            GUI.Label(new Rect(bar.xMax - 150f, bar.yMax + 6f, 150f, 16f), "동쪽 입구 (" + L.ToString("N0", Invariant) + " m)", _small);
            double dug = through ? L : Math.Min(L, west + (L - east) + 2.0 * midHalf);
            GUI.Label(new Rect(bar.x, bar.yMax + 26f, bar.width, 16f),
                "굴착 " + (100.0 * dug / L).ToString("0", Invariant) + " % (" + dug.ToString("N0", Invariant) + " m, 튜브 하나 기준)   라이닝 "
                + (100.0 * 2.0 * lined / L).ToString("0", Invariant) + " %" + (through ? "   — 관통" : ""), _label);
            float lx = bar.x, ly = bar.yMax + 50f;
            lx = Legend(lx, ly, RockColor, "암반 (아직 안 팜)");
            lx = Legend(lx, ly, ExcavatedColor, "굴착");
            lx = Legend(lx, ly, LiningColor, "2차 라이닝");
            Legend(lx, ly, HudStyle.Accent, "막장");
        }

        /// <summary>
        /// 댐 단면 윤곽 (자료: 마루·높이·사면 구배). 축조 높이는 단면적이 시간에 비례해 는다고 보고(그림용) 이분법으로 구한다.
        /// 표면차수벽형이면 상류면에 슬래브를 아래에서 위로 그린다.
        /// </summary>
        private void DrawDamOutline(Rect r)
        {
            DamSection d = _dam.CreateSection();
            ScheduleWork fill = MainWork(), slab = Work("표면차수벽 (슬래브)"), impound = Work("담수·마무리");
            DrawDamProfile(r, d.CrestElevation - d.FoundationElevation, d.CrestWidth, d.UpstreamSlope, d.DownstreamSlope, d.FoundationElevation,
                Progress(fill), slab != null ? Progress(slab) : -1.0, impound != null && _date > impound.Start ? Progress(impound) : 0.0, "");
        }

        /// <summary>
        /// 양수발전소: 왼쪽 상부댐, 오른쪽 하부댐 (각 댐 높이 그림), 아래에 지하 작업(발전소 굴착·수로터널·설비) 진행 막대.
        /// 댐 진행: 작업 기간의 앞 80 %는 축조, 뒤 20 %는 상류면 차수벽 (그림용 가정).
        /// </summary>
        private void DrawPumpedStorage(Rect r)
        {
            PumpedStoragePlant p = _plant;
            float barsH = 70f;
            var left = new Rect(r.x, r.y, r.width / 2f - 6f, r.height - barsH);
            var right = new Rect(r.x + r.width / 2f + 6f, r.y, r.width / 2f - 6f, r.height - barsH);
            double H = Math.Max(p.Upper.Height, p.Lower.Height);
            void One(Rect rr, StorageDam d, ScheduleWork w)
            {
                double f = Progress(w);
                double fillF = Math.Min(1.0, f / 0.8), slabF = f <= 0.8 ? 0.0 : (f - 0.8) / 0.2;
                ScheduleWork imp = Work("담수·시운전");
                double water = imp != null && _date > imp.Start ? Progress(imp) : 0.0;
                DrawDamProfile(rr, d.Height, d.CrestWidth, d.UpstreamSlope, d.DownstreamSlope, double.IsNaN(d.CrestElevation) ? 0.0 : d.CrestElevation - d.Height,
                    fillF, slabF, water, d.Name + " — " + d.Height.ToString("0.#", Invariant) + " m × " + d.Length.ToString("N0", Invariant) + " m, " + d.TypeName, H);
            }
            One(left, p.Upper, Work("상부댐"));
            One(right, p.Lower, Work("하부댐"));
            float y = r.yMax - barsH + 6f;
            foreach (string name in new[] { "지하 발전소 굴착", "수로터널·수직갱", "발전설비 설치" })
            {
                ScheduleWork w = Work(name);
                double f = Progress(w);
                GUI.Label(new Rect(r.x, y, 130f, 16f), name, _small);
                var bar = new Rect(r.x + 130f, y + 4f, r.width - 220f, 9f);
                Fill(bar, RockColor);
                Fill(new Rect(bar.x, bar.y, bar.width * (float)f, bar.height), name == "발전설비 설치" ? HudStyle.Accent : ExcavatedColor);
                GUI.Label(new Rect(bar.xMax + 6f, y, 80f, 16f), (100.0 * f).ToString("0", Invariant) + " %", _small);
                y += 20f;
            }
        }

        /// <summary>
        /// 댐 단면 윤곽 (높이·사면 구배·마루폭). 축조 높이는 단면적이 진행률에 비례한다고 보고(그림용) 이분법으로 구한다.
        /// slab ≥ 0이면 표면차수벽을 상류면에 아래부터 그린다. water = 담수 진행률 (높이의 90 %까지).
        /// </summary>
        private void DrawDamProfile(Rect r, double H, double cw, double up, double dn, double foundationEl, double fillProgress, double slab, double water, string title, double scaleHeight = 0)
        {
            double W(double h) => cw + (H - h) * (up + dn);
            double A(double h) => h * (W(0) + W(h)) / 2.0;
            double target = fillProgress * A(H), lo = 0, hi = H;
            for (int i = 0; i < 50; i++) { double m = 0.5 * (lo + hi); if (A(m) < target) lo = m; else hi = m; }
            double h1 = fillProgress >= 1.0 ? H : 0.5 * (lo + hi);

            double refH = Math.Max(H, scaleHeight);
            double halfBase = (cw + refH * (up + dn)) / 2.0 + 40.0;
            float scale = Mathf.Min((r.width - 60f) / (float)(2 * halfBase), (r.height - 70f) / (float)(refH + 20.0));
            float cx = r.center.x, by = r.y + 20f + (float)(refH + 10.0) * scale;
            float PX(double x) => cx + (float)x * scale;
            float PY(double h) => by - (float)h * scale;
            double upToe = -(cw / 2.0 + H * up), dnToe = cw / 2.0 + H * dn;
            Fill(Span(r.x, r.xMax, by, by + 18f), RockColor);
            for (int i = 0; i <= 40; i++)
            {
                double f = i / 40.0;
                Fill(new Rect(PX(upToe + (-cw / 2.0 - upToe) * f) - 1f, PY(H * f) - 1f, 2f, 2f), new Color(1f, 1f, 1f, 0.5f));
                Fill(new Rect(PX(dnToe + (cw / 2.0 - dnToe) * f) - 1f, PY(H * f) - 1f, 2f, 2f), new Color(1f, 1f, 1f, 0.5f));
            }
            double strip = Math.Max(H / 120.0, 1.5 / scale);   // 띠 한 줄이 1.5 px보다 얇으면 화면에서 사라진다 (캡처로 발견)
            for (double h = 0; h < h1; h += strip)
            {
                double top = Math.Min(h + strip, h1), half = W(top) / 2.0, shift = (dn - up) * (H - top) / 2.0;
                Fill(Span(PX(shift - half), PX(shift + half), PY(top), PY(h)), ShellColor);
            }
            double slabTop = slab > 0 ? slab * H : 0.0;
            for (double h = 0; h < slabTop; h += 0.5)
            {
                double x = -(cw / 2.0 + (H - h) * up);
                Fill(new Rect(PX(x) - 3f, PY(h + 0.5), 4f, Mathf.Max(1f, 0.5f * scale)), SlabColor);
            }
            if (water > 0)
            {
                double wl = water * H * 0.9;
                Fill(Span(r.x, PX(-(cw / 2.0 + (H - wl) * up)), PY(wl), by), WaterColor);
            }
            GUI.Label(new Rect(r.x + 4f, r.y + 2f, r.width - 8f, 16f), title.Length > 0 ? title : "← 상류                          하류 →", _small);
            GUI.Label(new Rect(PX(dnToe) - 170f, PY(h1) - 18f, 300f, 16f),
                "축조 높이 " + h1.ToString("0", Invariant) + " / " + H.ToString("0", Invariant) + " m" + (foundationEl > 0 ? " (EL " + (foundationEl + h1).ToString("0.#", Invariant) + ")" : ""), _label);
            if (title.Length == 0)
            {
                float lx = r.x + 4f, ly = r.yMax - 20f;
                lx = Legend(lx, ly, ShellColor, "석괴 (축조)");
                if (slab >= 0) lx = Legend(lx, ly, SlabColor, "차수벽");
                Legend(lx, ly, WaterColor, "물");
            }
        }

        private string BuildGenericText(ConstructionStep st)
        {
            var sb = new StringBuilder();
            sb.Append("<b>").Append(st.Title).Append("</b>  (").Append(st.Date.ToString("yyyy.M.d", Invariant)).Append(")\n");
            sb.Append(st.Text).Append("\n\n");
            var active = new List<string>();
            int done = 0;
            foreach (ScheduleWork w in _schedule.Works)
            {
                if (w.Start <= _date && _date < w.Finish) active.Add(w.Name + " " + (100.0 * Progress(w)).ToString("0", Invariant) + " %");
                else if (w.Finish <= _date) done++;
            }
            sb.Append("<b>지금 하는 작업</b>: ").Append(active.Count > 0 ? string.Join(", ", active) : "없음").Append("   (끝난 작업 ")
              .Append(done).Append(" / ").Append(_schedule.Works.Count).Append(")\n\n");
            ScheduleWork main = MainWork();
            if (main != null)
                sb.Append("<b>").Append(main.Name).Append("</b>: ").Append(main.Start.ToString("yyyy.M.d", Invariant)).Append(" ~ ")
                  .Append(main.Finish.ToString("yyyy.M.d", Invariant)).Append(" (").Append(main.Days.ToString("N0", Invariant)).Append("일)")
                  .Append(main.Assumed ? " — 날짜 가정" : " — 자료").Append("\n");
            sb.Append("\n<color=#bbbbbb>날짜: ").Append(_schedule.SourceNote).Append(" 작업마다 근거는 공정표의 출처 칸. ");
            if (_tunnel != null) sb.Append("막장 진행은 일정 속도로 그린 그림이다. 실제 계산(지보 패턴·1발파 싸이클)은 [부지] → ").Append(_tunnel.Name).Append(" 굴착.");
            if (_dam != null) sb.Append("단면은 공개 높이·사면 구배, 높이는 단면적이 시간에 비례한다고 본 그림이다. 체적 계산은 [부지] → ").Append(_dam.Name).Append(" 쌓기.");
            if (_bridge != null) sb.Append("보고서에서 자료 확인점(2009.6 상판 완료, 2009.10 준공)과 비교한다. 노선: OpenStreetMap.");
            return sb.Append("</color>").ToString();
        }

        private void OpenSchedule()
        {
            var schedule = GetComponent<ScheduleView>();
            if (schedule == null) return;
            DateTime d = _date;
            IsOpen = false;
            _playing = false;
            schedule.OpenAt(d);
        }

        // ── 시간 막대: 1967 ~ 1974, 단계 점, 지금 날짜 ─────────────

        private void DrawTimeline(Rect r)
        {
            // 공정 전체 (첫 단계의 해 1월 1일 ~ 마지막 단계 다음 해 1월 1일)
            int y0 = _steps[0].Date.Year, y1 = _steps[_steps.Count - 1].Date.Year + 1;
            DateTime from = new DateTime(y0, 1, 1), to = new DateTime(y1, 1, 1);
            double span = (to - from).TotalDays;
            float X(DateTime d) => r.x + r.width * (float)((d - from).TotalDays / span);
            Fill(new Rect(r.x, r.center.y, r.width, 1f), new Color(1f, 1f, 1f, 0.3f));
            for (int y = y0; y <= y1; y++)
            {
                float x = X(new DateTime(y, 1, 1));
                Fill(new Rect(x, r.y + 3f, 1f, r.height - 6f), new Color(1f, 1f, 1f, 0.25f));
                if (y < y1) GUI.Label(new Rect(x + 2f, r.y, 40f, r.height), y.ToString(Invariant), _small);
            }
            if (_section != null)
            {
                // 본댐 성토 기간
                Fill(new Rect(X(_section.FillStart), r.center.y - 2f, X(_section.CrestDate) - X(_section.FillStart), 4f), ShellColor);
                if (_section.HasFinishing)   // 마루 도달 뒤 ~ 완공: 마무리 (옅게)
                    Fill(new Rect(X(_section.CrestDate), r.center.y - 1f, X(_section.FillEnd) - X(_section.CrestDate), 2f), new Color(ShellColor.r, ShellColor.g, ShellColor.b, 0.5f));
            }
            else
            {
                ScheduleWork main = MainWork();   // 터널: 본선 굴착 / 댐: 본댐 축조
                if (main != null) Fill(new Rect(X(main.Start), r.center.y - 2f, X(main.Finish) - X(main.Start), 4f), ShellColor);
            }
            foreach (ConstructionStep s in _steps) Fill(new Rect(X(s.Date) - 1f, r.center.y - 4f, 3f, 8f), HudStyle.Accent);
            float now = X(_date);
            Fill(new Rect(now - 1f, r.y - 2f, 3f, r.height + 4f), GroutColor);
        }

        // ── 단면 그림 ─────────────────────────────────────────

        private void DrawSection(Rect r, double fill)
        {
            DamZoneSection z = _section;
            double up = z.UpstreamFace(z.FoundationElevation) + 70.0, down = z.DownstreamFace(z.FoundationElevation) + 50.0;
            double zTop = SiteStructures.SoyanggangCrest + 14.0, zBottom = z.FoundationElevation - CurtainDepth - 8.0;
            float scale = Mathf.Min((r.width - 50f) / (float)(up + down), (r.height - 40f) / (float)(zTop - zBottom));
            float cx = r.x + 44f + (float)up * scale;   // 축의 화면 x
            float baseY = r.y + 8f + (float)(zTop - zBottom) * scale;
            float X(double d) => cx - (float)d * scale;   // 상류(+)가 왼쪽
            float Y(double e) => baseY - (float)(e - zBottom) * scale;
            double dLeft = up, dRight = -down;

            // 표고 눈금 (20 m)
            for (int e = 40; e <= 220; e += 20)
            {
                if (e < zBottom || e > zTop) continue;
                float y = Y(e);
                Fill(new Rect(X(dLeft), y, X(dRight) - X(dLeft), 1f), new Color(1f, 1f, 1f, 0.06f));
                GUI.Label(new Rect(r.x, y - 8f, 42f, 16f), "EL " + e, _small);
            }

            // 암반 (기초 아래)
            Fill(Span(X(dLeft), X(dRight), Y(z.FoundationElevation), Y(zBottom)), RockColor);

            // 하상 퇴적층: 기초굴착(1969.10.24 ~ 1970.1.1) 동안 축에서 바깥으로 걷어 낸다 (두께는 그림용)
            double dug = Progress(ExcavationStart, GroutStart);
            double digUp = dug * z.UpstreamFace(z.FoundationElevation), digDown = dug * z.DownstreamFace(z.FoundationElevation);
            float sedTop = Y(z.FoundationElevation + SedimentThickness), sedBottom = Y(z.FoundationElevation);
            Fill(Span(X(dLeft), X(digUp), sedTop, sedBottom), SedimentColor);
            Fill(Span(X(-digDown), X(dRight), sedTop, sedBottom), SedimentColor);

            // 강물: 유수전환 전에는 계곡 전체, 뒤에는 하류(가배수터널 출구 쪽)만
            float waterTop = Y(z.FoundationElevation + SedimentThickness + 3.0);
            if (_date < DiversionDate) Fill(Span(X(dLeft), X(dRight), waterTop, sedTop), WaterColor);
            else Fill(Span(X(-z.DownstreamFace(z.FoundationElevation) - 10.0), X(dRight), waterTop, sedTop), WaterColor);

            // 그라우트 커튼: 코어 바닥 가운데에서 아래로 (1970.1.1 ~ 1970.5.1 동안 깊어진다, 깊이는 그림용)
            double grout = Progress(GroutStart, _section.FillStart);
            if (grout > 0)
            {
                float y0 = Y(z.FoundationElevation), y1 = Y(z.FoundationElevation - CurtainDepth * grout);
                for (float yy = y0; yy < y1; yy += 5f) Fill(new Rect(X(0) - 1.5f, yy, 3f, 3f), GroutColor);
                for (int i = -2; i <= 2; i++)
                    if (i != 0) for (float yy = y0; yy < y1; yy += 5f) Fill(new Rect(X(i * 5.0) - 0.5f, yy + 2f, 1f, 2f), new Color(0.55f, 0.85f, 1f, 0.5f));
            }

            // 완성 모습(흐리게) + 지금까지 쌓은 존 (가로 띠로 칠함)
            float yCrest = Y(z.CrestElevation), yFound = Y(z.FoundationElevation), yFill = Y(fill);
            for (float y = yCrest; y < yFound; y += 1f)
            {
                double e = zBottom + (baseY - (y + 0.5f)) / scale;
                if (e > z.CrestElevation || e < z.FoundationElevation) continue;
                double u = z.UpstreamFace(e), dn = -z.DownstreamFace(e);
                if (e > fill)
                {
                    Fill(Span(X(u), X(dn), y, y + 1f), new Color(1f, 1f, 1f, 0.07f));
                    if (e <= z.CoreTopElevation)
                    {
                        double c0 = z.CoreHalfWidth(e);
                        Fill(new Rect(X(c0), y, 1f, 1f), new Color(1f, 0.8f, 0.6f, 0.25f));
                        Fill(new Rect(X(-c0), y, 1f, 1f), new Color(1f, 0.8f, 0.6f, 0.25f));
                    }
                    continue;
                }
                if (e > z.CoreTopElevation) { Fill(Span(X(u), X(dn), y, y + 1f), ShellColor); continue; }
                double c = z.CoreHalfWidth(e), f = c + z.FilterWidth;
                Fill(Span(X(u), X(f), y, y + 1f), ShellColor);
                Fill(Span(X(f), X(c), y, y + 1f), FilterColor);
                Fill(Span(X(c), X(-c), y, y + 1f), CoreColor);
                Fill(Span(X(-c), X(-f), y, y + 1f), FilterColor);
                Fill(Span(X(-f), X(dn), y, y + 1f), ShellColor);
            }
            // 완성 윤곽선 (사면)
            for (float y = yCrest; y < yFound; y += 2f)
            {
                double e = zBottom + (baseY - y) / scale;
                Fill(new Rect(X(z.UpstreamFace(e)), y, 1f, 1f), new Color(1f, 1f, 1f, 0.35f));
                Fill(new Rect(X(-z.DownstreamFace(e)), y, 1f, 1f), new Color(1f, 1f, 1f, 0.35f));
            }

            // 저수지 (담수 개시 뒤, 상류면에 붙여). 준공 전 수위는 그림용, 준공 때 상시만수위.
            if (_date >= ImpoundStart)
            {
                bool done = _date >= _schedule.Completion;
                double level = done ? DamZoneSection.SoyanggangNormalWaterLevel
                    : Math.Min(DamZoneSection.SoyanggangNormalWaterLevel, fill - 15.0);
                for (float y = Y(level); y < sedTop; y += 1f)
                {
                    double e = zBottom + (baseY - (y + 0.5f)) / scale;
                    double face = e >= z.FoundationElevation && e <= fill ? z.UpstreamFace(e) : z.UpstreamFace(z.FoundationElevation);
                    Fill(Span(X(dLeft), X(face), y, y + 1f), WaterColor);
                }
                GUI.Label(new Rect(X(dLeft) + 4f, Y(level) + 18f, 220f, 16f),
                    done ? "저수지 (준공: 상시만수위까지)" : "담수 중 (수위는 그림용)", _small);
            }

            // 기준선: 계획홍수위, 상시만수위 (점선)
            DashedLevel(X(dLeft), X(0) - 4f, Y(DamZoneSection.SoyanggangFloodWaterLevel), "계획홍수위 EL 198", new Color(1f, 0.55f, 0.45f, 0.8f));
            DashedLevel(X(dLeft), X(0) - 4f, Y(DamZoneSection.SoyanggangNormalWaterLevel), "상시만수위 EL 193.5", new Color(0.55f, 0.75f, 1f, 0.8f), true);

            // 성토 높이 표시
            if (fill > z.FoundationElevation + 0.5)
            {
                Fill(new Rect(X(z.UpstreamFace(fill)) - 6f, yFill, X(-z.DownstreamFace(fill)) - X(z.UpstreamFace(fill)) + 12f, 1.5f), HudStyle.Accent);
                GUI.Label(new Rect(X(-z.DownstreamFace(fill)) + 8f, yFill - 9f, 220f, 18f),
                    "성토 EL " + fill.ToString("0", Invariant) + " (높이 " + (fill - z.FoundationElevation).ToString("0", Invariant) + " m)", _label);
            }

            // 글자 표시
            GUI.Label(new Rect(X(dLeft) + 2f, Y(z.FoundationElevation - 10.0), 120f, 16f), "암반", _small);
            if (dug < 1.0) GUI.Label(new Rect(X(dRight) - 170f, sedTop - 16f, 170f, 16f), "하상 퇴적층 (두께는 그림용)", _small);
            if (_date >= DiversionDate && _date < _section.FillStart)   // 성토가 시작되면 그 자리를 덮으므로 그 전까지만
                GUI.Label(new Rect(X(dLeft) + 4f, waterTop - 32f, 240f, 16f), "강물 → 가배수터널로 돌림 (이 단면 밖)", _small);
            // 2단계 (터널 뚫기): 터널은 이 단면 밖이라 그림이 1단계와 같아 보였다 (2026-11-26 개발자 "2단계 클릭이 안 됨").
            // 그래서 굴착 진행률 막대를 단면 위에 보여 준다. 진행률은 3D와 같은 가정(착수 ~ 유수전환 일정 속도).
            if (_date >= DamStageModel.Tunnel1Start && _date < DiversionDate)
            {
                double t1 = DamStageModel.Progress(_date, DamStageModel.Tunnel1Start, DiversionDate);
                float tx = X(dLeft) + 4f, ty = waterTop - 74f;
                GUI.Label(new Rect(tx, ty, 420f, 16f), "가배수터널 뚫는 중 (이 단면 밖 산속 — [3D로 보기]에서 보임)", _label);
                Fill(new Rect(tx, ty + 20f, 200f, 8f), new Color(1f, 1f, 1f, 0.15f));
                Fill(new Rect(tx, ty + 20f, 200f * (float)t1, 8f), HudStyle.Accent);
                GUI.Label(new Rect(tx + 208f, ty + 15f, 220f, 16f),
                    "1호 터널 " + (t1 * 100.0).ToString("0", Invariant) + " % (진행은 그림용 일정 속도)", _small);
            }
            if (grout > 0) GUI.Label(new Rect(X(0) + 6f, Y(z.FoundationElevation - CurtainDepth * 0.6), 190f, 16f), "그라우트 커튼 (깊이는 그림용)", _small);
            GUI.Label(new Rect(X(dLeft), r.y, 80f, 16f), "← 상류", _small);
            GUI.Label(new Rect(X(dRight) - 60f, r.y, 60f, 16f), "하류 →", _small);

            // 범례
            float lx = r.x + 46f, ly = Mathf.Min(r.yMax - 16f, Y(zBottom) + 6f);   // 범례는 단면 바로 아래
            lx = Legend(lx, ly, CoreColor, "코어(점토)");
            lx = Legend(lx, ly, FilterColor, "필터");
            lx = Legend(lx, ly, ShellColor, "사력");
            lx = Legend(lx, ly, WaterColor, "물");
            Legend(lx, ly, GroutColor, "그라우트");
        }

        private float Legend(float x, float y, Color c, string name)
        {
            Fill(new Rect(x, y + 3f, 12f, 10f), c);
            GUI.Label(new Rect(x + 15f, y, 90f, 16f), name, _small);
            return x + 20f + _small.CalcSize(new GUIContent(name)).x + 10f;
        }

        private void DashedLevel(float x1, float x2, float y, string label, Color c, bool below = false)
        {
            for (float x = x1; x < x2; x += 8f) Fill(new Rect(x, y, 4f, 1f), c);
            GUI.Label(new Rect(x1 + 100f, below ? y + 1f : y - 15f, 160f, 16f), label, _small);   // 두 선이 가까워 하나는 위, 하나는 아래에 쓴다
        }

        // ── 오른쪽 설명 ────────────────────────────────────────

        private void DrawText(Rect r, ConstructionStep st, double fill)
        {
            Fill(new Rect(r.x - 6f, r.y, 1f, r.height), new Color(1f, 1f, 1f, 0.15f));
            GUI.Label(r, BuildText(st, fill), _text);
        }

        // ── 3D 보기: 오른쪽 설명 상자, 왼쪽 아래 범례·조작 ────────────

        private void Draw3DPanels(float top, ConstructionStep st, double fill)
        {
            float w = Mathf.Clamp(HudMode.GuiWidth * 0.3f, 300f, 380f);
            string content = BuildText(st, fill);
            float h = Mathf.Min(_text.CalcHeight(new GUIContent(content), w - 20f) + 16f, HudMode.GuiHeight - top - 12f);
            var box = new Rect(HudMode.GuiWidth - 12f - w, top, w, h);
            HudStyle.DrawBox(box);
            HudLayout.Register(box);
            GUI.Label(new Rect(box.x + 10f, box.y + 8f, box.width - 20f, box.height - 12f), content, _text);

            // 범례와 3D 조작 (왼쪽 아래)
            var legend = new Rect(12f, HudMode.GuiHeight - 12f - 62f, Mathf.Min(690f, HudMode.GuiWidth - w - 48f), 62f);
            HudStyle.DrawBox(legend);
            HudLayout.Register(legend);
            float lx = legend.x + 10f, ly = legend.y + 8f;
            lx = Legend(lx, ly, DamStage3D.CoreColor, "코어(점토)");
            lx = Legend(lx, ly, DamStage3D.FilterColor, "필터");
            lx = Legend(lx, ly, DamStage3D.ShellColor, "사력");
            lx = Legend(lx, ly, SedimentColor, "퇴적층");
            lx = Legend(lx, ly, RockColor, "암반");
            Legend(lx, ly, WaterColor, "물");
            float bx = legend.x + 10f, by = legend.y + 32f;
            InfoBox.Button(new Rect(bx, by, 150f, 22f), Cut3D ? "■ 반 잘라 보는 중" : "반 잘라 존 보기", () => Cut3D = !Cut3D, Cut3D);
            var stage = GetComponent<DamStage3D>();
            InfoBox.Button(new Rect(bx + 156f, by, 80f, 22f), "처음 시점", () => { if (stage != null) stage.FocusCamera(); });
            InfoBox.Button(new Rect(bx + 242f, by, 100f, 22f), "작업 자리로", () => { if (stage != null) stage.FocusWork(); });
            bool fleet = stage != null && stage.ShowFleet;
            InfoBox.Button(new Rect(bx + 348f, by, 92f, 22f), fleet ? "장비 숨기기" : "장비 보기", () => { if (stage != null) stage.ShowFleet = !stage.ShowFleet; });
            GUI.Label(new Rect(bx + 448f, by, legend.xMax - bx - 456f, 22f), "점선 = 땅속(터널·그라우트)·완성 마루", _small);
        }

        private string BuildText(ConstructionStep st, double fill)
        {
            var sb = new StringBuilder();
            sb.Append("<b>").Append(st.Title).Append("</b>  (").Append(st.Date.ToString("yyyy.M.d", Invariant)).Append(")\n");
            sb.Append(st.Text).Append("\n\n");

            // 그날 진행 중인 작업 (공정표와 같은 자료)
            var active = new List<string>();
            int done = 0;
            foreach (ScheduleWork w in _schedule.Works)
            {
                if (w.Start <= _date && _date < w.Finish) active.Add(w.Name);
                else if (w.Finish <= _date) done++;
            }
            sb.Append("<b>지금 하는 작업</b>: ").Append(active.Count > 0 ? string.Join(", ", active) : "없음 (모두 끝남)")
              .Append("   (끝난 작업 ").Append(done).Append(" / ").Append(_schedule.Works.Count).Append(")\n\n");

            // 성토
            DamZoneSection z = _section;
            double f = z.FillFraction(_date);
            sb.Append("<b>본댐 성토</b>: ").Append((f * 100).ToString("0", Invariant)).Append(" %  ")
              .Append(Man(f * z.TotalVolume)).Append(" / ").Append(Man(z.TotalVolume)).Append(" m³,  높이 ")
              .Append((fill - z.FoundationElevation).ToString("0", Invariant)).Append(" / 123 m\n");
            if (!Is3D) sb.Append("하루 평균 ").Append(Man(z.DailyVolume, "0.00")).Append(" m³ (").Append(z.FillStart.ToString("yyyy.M.d", Invariant))
              .Append(" ~ ").Append(z.CrestDate.ToString("yyyy.M.d", Invariant)).Append(" 일정 속도 가정")
              .Append(z.HasFinishing ? ", 그 뒤 " + z.FillEnd.ToString("yyyy.M.d", Invariant) + "까지 마무리" : "").Append(")\n");
            sb.Append("존별 체적: 코어 ").Append(Share(DamZone.Core)).Append(", 필터 ").Append(Share(DamZone.Filter))
              .Append(", 사력 ").Append(Share(DamZone.Shell)).Append("\n");

            // 검증: 자료와 가정 비교
            if (st.Date == DamZoneSection.SoyanggangFloodDate && _date == st.Date)
            {
                // 두 기록 비교: 11.10 완공까지 일정 속도 ↔ 8·19에 마루 (기사)
                DamZoneSection a = z.WithCrestDate(z.FillEnd), b = z.WithCrestDate(DamZoneSection.SoyanggangFloodDate);
                double ha = a.FillElevation(_date) - a.FoundationElevation;
                string more = (100.0 * (b.DailyVolume / a.DailyVolume - 1.0)).ToString("0", Invariant);
                if (Is3D)   // 3D 설명 상자는 작다 — 한 줄만
                    sb.Append("\n<b>두 기록</b>: 11.10 완공이면 ").Append(ha.ToString("0", Invariant)).Append(" m ↔ 기사 123 m (하루 성토량 +").Append(more).Append(" %)\n");
                else
                    sb.Append("\n<b>두 기록 비교</b>: 11.10 완공까지 일정 속도면 이날 ").Append(ha.ToString("0", Invariant)).Append(" m (체적 ")
                      .Append((100.0 * a.FillFraction(_date)).ToString("0", Invariant)).Append(" %). 위쪽 ")
                      .Append((z.Height - ha).ToString("0", Invariant)).Append(" m는 체적 ").Append((100.0 * (1.0 - a.FillFraction(_date))).ToString("0", Invariant))
                      .Append(" %뿐이라 하루 성토량은 ").Append(Man(a.DailyVolume, "0.00")).Append(" → ").Append(Man(b.DailyVolume, "0.00"))
                      .Append(" m³ (+").Append(more).Append(" %). [마루 도달]로 바꿔 본다.\n");
            }
            if (!Is3D && st.Date == ImpoundStart && _date == st.Date)
                sb.Append("\n담수를 시작한 날 계산 높이 ").Append((fill - z.FoundationElevation).ToString("0", Invariant))
                  .Append(" m (EL ").Append(fill.ToString("0", Invariant)).Append(").\n");

            if (Is3D)
            {
                var stage = GetComponent<DamStage3D>();
                if (stage != null && stage.FleetText.Length > 0)
                    sb.Append("\n<b>보이는 장비</b> (그림용, 3배 크게): ").Append(stage.FleetText).Append(" (실제 205대 [T])\n");
            }
            if (Is3D)   // 3D에서는 설명 상자가 장면을 가리지 않게 짧게
                return sb.Append("\n<color=#bbbbbb>가정 (자료 없음, 그림용): 존 치수, 호수 바닥, 가물막이 높이, 터널·여수로 위치. 공사 전 계곡은 위성 지형에서 복원.</color>").ToString();
            sb.Append("\n<color=#bbbbbb>가정: 코어 윗면 EL 201(계획홍수위 + 3 m)·폭 6 m·구배 1:0.25, 필터 4 m (자료 없음, 도면을 구하면 바꾼다). "
                + "계곡 바닥 폭 ").Append(z.ValleyBottomWidth.ToString("0", Invariant))
              .Append(" m는 체적 959만 m³에 맞춰 계산.</color>");
            return sb.ToString();
        }

        private string Share(DamZone zone)
        {
            double v = _section.ZoneVolume(zone);
            return Man(v) + " m³ (" + (100.0 * v / _section.TotalVolume).ToString("0", Invariant) + " %)";
        }

        private static string Man(double v, string format = "0.0") => (v / 10000.0).ToString(format, Invariant) + "만";

        private double Progress(DateTime a, DateTime b)
        {
            double t = (_date - a).TotalDays / (b - a).TotalDays;
            return t < 0 ? 0 : t > 1 ? 1 : t;
        }

        private static string ShortName(string title)
        {
            int i = title.IndexOf(" —", StringComparison.Ordinal);
            string s = i > 0 ? title.Substring(0, i) : title;
            i = s.IndexOf(" (", StringComparison.Ordinal);
            s = i > 0 ? s.Substring(0, i) : s;
            return s.Replace("본댐 ", "").Replace("제1호 가배수터널", "터널1").Replace("가배수터널 뚫기 시작", "가배수터널");
        }

        private static Rect Span(float xa, float xb, float ya, float yb) =>
            new Rect(Mathf.Min(xa, xb), Mathf.Min(ya, yb), Mathf.Abs(xb - xa), Mathf.Abs(yb - ya));

        private static void Fill(Rect r, Color c)
        {
            if (r.width <= 0f || r.height <= 0f) return;
            Color prev = GUI.color;
            GUI.color = HudStyle.Ui(c);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
        }
    }
}
