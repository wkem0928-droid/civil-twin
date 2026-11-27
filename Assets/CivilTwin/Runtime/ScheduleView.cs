using System;
using System.Globalization;
using CivilTwin.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CivilTwin.Runtime
{
    /// <summary>
    /// 네트워크 공정표 화면 — 모든 회사 구조물 공통 (2026-11-22 소양강댐 댐 공정표, 2026-11-27 일반화. 메뉴 [대안] → ○○ 네트워크 공정표).
    ///
    /// 가로 = 날짜(시간 눈금 네트워크, 실무 공정표 형식), 세로 = 작업. 작업마다
    ///   굵은 막대 = 논리 공정 (CPM의 ES ~ EF, 주공정 빨강 · 나머지 파랑), 가는 회색 줄 = 여유 (EF ~ LF),
    ///   아래 노란 줄 = 실제 날짜 (자료), 회색 꺾은 선 = 선행 → 후행 관계.
    ///   세로 점선 = 사건 (설계 변경, 터널 폐쇄, 담수, 8·19 홍수, 준공).
    /// 작업 이름을 누르면 아래에 무엇을·왜·장비·출처와 ES/LS/여유, 실제와의 차이가 나온다.
    /// [+30일] [−30일]로 그 작업을 늦추면 CPM을 다시 계산해 논리 준공이 며칠 늦어지는지 보인다.
    /// 계산은 Core/ConstructionSchedule (테스트 DamConstructionTests 등).
    /// </summary>
    public sealed class ScheduleView : MonoBehaviour
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly Color CriticalColor = new Color(0.90f, 0.28f, 0.22f);
        private static readonly Color NormalColor = new Color(0.35f, 0.58f, 0.95f);
        private static readonly Color ActualColor = new Color(1.00f, 0.82f, 0.25f);
        private static readonly Color LineColor = new Color(0.75f, 0.78f, 0.82f, 0.85f);

        private SiteSwitcher _sites;
        private ConstructionSchedule _schedule;
        private string _scheduleFile = "";
        private double[] _delays;
        private int _selected;   // 처음엔 자료의 FocusWork (소양강댐: 본댐 성토)
        private GUIStyle _label, _small, _title, _text;

        public bool IsOpen { get; private set; }

        /// <summary>지금 현장에 시공 공정 자료가 있으면 true</summary>
        public bool Available => Schedule != null;

        /// <summary>지금 현장의 시공 공정 자료 (없으면 null)</summary>
        public ConstructionSchedule Current => Schedule;

        public void Initialize(SiteSwitcher sites)
        {
            _sites = sites;
            IsOpen = false;
        }

        public void Toggle()
        {
            IsOpen = !IsOpen && Available;
            _marker = null;
            if (IsOpen)
            {
                var section = GetComponent<DamSectionView>();
                if (section != null && section.IsOpen) section.Toggle();   // 두 화면은 하나씩
            }
        }

        /// <summary>단면 화면에서 넘어올 때: 그 날짜에 하늘색 세로선을 긋고 연다</summary>
        public void OpenAt(DateTime date)
        {
            if (!Available) return;
            if (!IsOpen) Toggle();
            _marker = date;
        }

        private DateTime? _marker;

        private ConstructionSchedule Schedule
        {
            get
            {
                string file = _sites != null ? _sites.CurrentFileName : "";
                if (file != _scheduleFile)
                {
                    _scheduleFile = file;
                    _schedule = ScheduleCatalog.Find(file);
                    _delays = _schedule != null ? new double[_schedule.Works.Count] : null;
                    _selected = _schedule != null ? _schedule.FocusWork : 0;
                    if (_schedule == null) IsOpen = false;
                }
                return _schedule;
            }
        }

        private void Update()
        {
            if (!IsOpen || !InputGate.Allowed) return;
            Keyboard k = Keyboard.current;
            if (k != null && k.escapeKey.wasPressedThisFrame) IsOpen = false;
        }

        private void OnGUI()
        {
            HudMode.ApplyMatrix();
            if (!IsOpen || Schedule == null || !HudMode.ShowBoxes) return;
            GUI.depth = -5;   // 다른 글상자 위, 메뉴(−10) 아래
            if (_label == null)
            {
                _label = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleLeft, wordWrap = false };
                _small = new GUIStyle(_label) { fontSize = 11 };
                _title = new GUIStyle(_label) { fontSize = 15 };
                _text = new GUIStyle(_label) { fontSize = 12, wordWrap = true, alignment = TextAnchor.UpperLeft };
            }
            _label.normal.textColor = _small.normal.textColor = _title.normal.textColor = _text.normal.textColor = HudStyle.Ui(HudStyle.Text);

            ConstructionSchedule s = Schedule;
            CpmResult logic = s.Logic(_delays);
            CpmResult baseLogic = s.Logic();

            float top = ControlBar.BarRect.height > 0f ? ControlBar.BarRect.yMax + 8f : 10f;
            var panel = new Rect(12f, top, HudMode.GuiWidth - 24f, HudMode.GuiHeight - top - 12f);
            for (int i = 0; i < 2; i++) HudStyle.DrawBox(panel);   // 두 번 겹쳐 거의 불투명하게
            HudLayout.Register(panel);

            // 제목·닫기
            double logicEnd = logic.ProjectDuration, baseEnd = baseLogic.ProjectDuration, actualEnd = s.Day(s.Completion);
            GUI.Label(new Rect(panel.x + 12f, panel.y + 6f, panel.width - 340f, 22f),
                ShortTitle(s.Title) + " 네트워크 공정표 — 논리 " + N0(logicEnd) + "일 (" + Date(s, logicEnd) + ")"
                + (Math.Abs(logicEnd - baseEnd) > 0.5 ? " [지연 " + Signed(logicEnd - baseEnd) + "일]" : "")
                + " · 실제 " + N0(actualEnd) + "일 (" + s.Completion.ToString("yyyy.M.d", Invariant) + ")", _title);
            InfoBox.Button(new Rect(panel.xMax - 96f, panel.y + 6f, 84f, 22f), "닫기 (Esc)", () => IsOpen = false);
            InfoBox.Button(new Rect(panel.xMax - 214f, panel.y + 6f, 112f, 22f), s.Name == "소양강댐" ? "존 단면 보기" : "단계 보기", OpenSection);
            InfoBox.Button(new Rect(panel.xMax - 314f, panel.y + 6f, 94f, 22f), "보고서 저장", () => SaveReport());

            // 영역 나누기
            const float nameW = 150f, detailH = 118f, axisH = 34f, legendH = 34f;   // 범례 두 줄
            float chartX = panel.x + 12f + nameW, chartW = panel.width - nameW - 28f;
            float rowsTop = panel.y + 34f + axisH;
            float rowsH = panel.height - 34f - axisH - legendH - detailH - 14f;
            int n = s.Works.Count;
            float rowH = Mathf.Max(14f, rowsH / n);

            DateTime from = new DateTime(s.ProjectStart.Year, 1, 1), to = new DateTime(s.Completion.Year + 1, 1, 1);
            double span = (to - from).TotalDays, offset = (s.ProjectStart - from).TotalDays;
            float X(double day) => chartX + chartW * (float)((day + offset) / span);

            // 연도 눈금
            for (int y = from.Year; y <= to.Year; y++)
            {
                float x = X((new DateTime(y, 1, 1) - s.ProjectStart).TotalDays);
                Fill(new Rect(x, rowsTop - 4f, 1f, rowH * n + 4f), new Color(1f, 1f, 1f, 0.10f));
                if (y < to.Year) GUI.Label(new Rect(x + 2f, rowsTop - 18f, 60f, 16f), y.ToString(Invariant), _small);
            }

            // 사건 (세로 점선 + 번호, 아래 범례 줄에 설명)
            for (int m = 0; m < s.Milestones.Count; m++)
            {
                float x = X(s.Day(s.Milestones[m].date));
                for (float yy = rowsTop; yy < rowsTop + rowH * n; yy += 6f) Fill(new Rect(x, yy, 1f, 3f), new Color(1f, 0.9f, 0.6f, 0.55f));
                GUI.Label(new Rect(x - 8f, rowsTop - 34f + (m % 2) * 0f, 20f, 16f), Circled(m), _small);
            }

            // 작업 행
            for (int i = 0; i < n; i++)
            {
                ScheduleWork w = s.Works[i];
                float y = rowsTop + i * rowH;
                bool crit = logic.Critical[i];
                int index = i;
                InfoBox.Button(new Rect(panel.x + 8f, y + 1f, nameW, rowH - 2f), (crit ? "● " : "") + w.Name, () => _selected = index, i == _selected);

                // 논리 공정 막대, 여유, 실제 날짜
                float x1 = X(logic.EarlyStart[i]), x2 = X(logic.EarlyFinish[i]);
                Fill(new Rect(x1, y + rowH * 0.18f, Mathf.Max(2f, x2 - x1), rowH * 0.42f), crit ? CriticalColor : NormalColor);
                if (logic.TotalFloat[i] > 0.5) Fill(new Rect(x2, y + rowH * 0.37f, X(logic.LateFinish[i]) - x2, 2f), new Color(0.8f, 0.8f, 0.8f, 0.7f));
                float a1 = X(s.Day(w.Start)), a2 = X(s.Day(w.Finish));
                Fill(new Rect(a1, y + rowH * 0.68f, Mathf.Max(2f, a2 - a1), Mathf.Max(3f, rowH * 0.16f)), ActualColor);
                if (i == _selected) Fill(new Rect(chartX, y, chartW, 1f), new Color(1f, 0.82f, 0.25f, 0.5f));

                // 선행 → 후행 (꺾은 선: 선행 끝에서 가로로, 후행 시작에서 세로로)
                foreach (int p in w.Predecessors)
                {
                    float py = rowsTop + p * rowH + rowH * 0.39f, cy = y + rowH * 0.39f;
                    float px = X(logic.EarlyFinish[p]);
                    if (x1 > px) Fill(new Rect(px, py, x1 - px, 1f), LineColor);
                    Fill(new Rect(x1, Mathf.Min(py, cy), 1f, Mathf.Abs(cy - py)), LineColor);
                }
            }

            // 실제 준공·논리 준공 세로선
            Fill(new Rect(X(actualEnd), rowsTop, 2f, rowH * n), ActualColor);
            Fill(new Rect(X(logicEnd), rowsTop, 2f, rowH * n), CriticalColor);
            if (_marker.HasValue)   // 단면 화면의 날짜
            {
                float mx = X(s.Day(_marker.Value));
                Fill(new Rect(mx - 1f, rowsTop - 4f, 3f, rowH * n + 4f), new Color(0.55f, 0.85f, 1f, 0.9f));
                bool right = mx + 110f < panel.xMax;   // 오른쪽 끝이면 선 왼쪽에 쓴다
                GUI.Label(new Rect(right ? mx + 4f : mx - 104f, rowsTop + rowH * n - 16f, 100f, 16f), "단면 " + _marker.Value.ToString("yyyy.M.d", Invariant), _small);
            }

            // 범례 + 사건 설명
            float ly = rowsTop + rowH * n + 2f;
            GUI.Label(new Rect(panel.x + 12f, ly, panel.width - 24f, 16f),
                "막대: 빨강 = 주공정, 파랑 = 여유 있음, 회색 줄 = 여유, 노랑 = 실제 날짜 / 세로선: 빨강 = 논리 준공, 노랑 = 실제 준공 / ● = 주공정 작업", _small);
            var events = new System.Text.StringBuilder();
            for (int m = 0; m < s.Milestones.Count; m++)
                events.Append(Circled(m) + " " + s.Milestones[m].date.ToString("yy.M", Invariant) + " " + Short(s.Milestones[m].text) + "   ");
            GUI.Label(new Rect(panel.x + 12f, ly + 16f, panel.width - 24f, 16f), events.ToString(), _small);

            // 선택한 작업 설명 + 지연 버튼
            DrawDetail(s, logic, baseEnd, new Rect(panel.x + 12f, panel.yMax - detailH - 8f, panel.width - 24f, detailH));
            HudToast.Draw();
        }

        /// <summary>
        /// 시공계획 보고서(Markdown)와 작업표(CSV, 엑셀용)를 Reports 폴더에 저장한다 (2026-11-25 댐, 2026-11-27 공통).
        /// 화면에서 넣은 지연도 요약에 적는다. 저장한 Markdown 경로를 돌려준다 (자료 없는 현장이면 빈 글).
        /// </summary>
        public string SaveReport()
        {
            ConstructionSchedule s = Schedule;
            if (s == null) return "";
            string folder = AlternativeTool.ReportFolder;
            System.IO.Directory.CreateDirectory(folder);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", Invariant);
            string kind = ConstructionReport.KindName(s.Kind);
            string md = System.IO.Path.Combine(folder, kind + "시공보고서_" + s.Name + "_" + stamp + ".md");
            string csv = System.IO.Path.Combine(folder, kind + "작업표_" + s.Name + "_" + stamp + ".csv");
            string text = ConstructionReport.Markdown(s, _delays, DateTime.Now.ToString("yyyy-MM-dd HH:mm", Invariant), ReportPart(s));
            System.IO.File.WriteAllText(md, text, new System.Text.UTF8Encoding(false));
            System.IO.File.WriteAllText(csv, ConstructionReport.Csv(s), new System.Text.UTF8Encoding(true));   // 엑셀이 한글을 읽도록 BOM
            LastReportPath = md;
            HudToast.Show("저장함: Reports/" + System.IO.Path.GetFileName(md) + "  +  작업표 CSV");
            Debug.Log("[CivilTwin] " + kind + " 보고서 저장: " + md);
            return md;
        }

        /// <summary>
        /// 구조물 종류별 보고서 부분. 존 단면이 있는 댐(소양강댐)은 단계 화면의 [마루 도달] 가정을 따른다.
        /// </summary>
        private StructureReportPart ReportPart(ConstructionSchedule s)
        {
            if (s.Name == "소양강댐")
            {
                var sectionView = GetComponent<DamSectionView>();
                DamZoneSection zones = sectionView != null && sectionView.Section != null ? sectionView.Section : DamZoneSection.Soyanggang();
                return new DamZoneReportPart(zones);
            }
            return ScheduleCatalog.ReportPart(s);
        }

        /// <summary>마지막으로 저장한 보고서 경로 (없으면 빈 글)</summary>
        public string LastReportPath { get; private set; } = "";

        /// <summary>존 단면 화면으로: 표시한 날짜가 있으면 그 날짜, 없으면 고른 작업의 실제 시작일</summary>
        private void OpenSection()
        {
            var section = GetComponent<DamSectionView>();
            if (section == null) return;
            DateTime d = _marker ?? Schedule.Works[Mathf.Clamp(_selected, 0, Schedule.Works.Count - 1)].Start;
            IsOpen = false;
            section.OpenAt(d);
        }

        private void DrawDetail(ConstructionSchedule s, CpmResult logic, double baseEnd, Rect r)
        {
            int i = Mathf.Clamp(_selected, 0, s.Works.Count - 1);
            ScheduleWork w = s.Works[i];
            Fill(new Rect(r.x, r.y - 4f, r.width, 1f), new Color(1f, 1f, 1f, 0.15f));
            double late = s.LateBeyondFloat(logic, i);
            string head = w.Name + (logic.Critical[i] ? "  [주공정]" : "  [여유 " + N0(logic.TotalFloat[i]) + "일]")
                + "   논리 " + Date(s, logic.EarlyStart[i]) + " ~ " + Date(s, logic.EarlyFinish[i]) + " (" + N0(w.Days + _delays[i]) + "일), 가장 늦은 시작 " + Date(s, logic.LateStart[i])
                + "   실제 " + w.Start.ToString("yyyy.M.d", Invariant) + " ~ " + w.Finish.ToString("yyyy.M.d", Invariant)
                + (late > 0.5 ? " → 여유를 " + N0(late) + "일 넘겨 늦게 시작" : "");
            GUI.Label(new Rect(r.x, r.y, r.width - 250f, 20f), head, _label);
            string body = "무엇을: " + w.What + "\n왜: " + w.Why + "\n장비: " + w.Equipment
                + "\n출처: " + w.Source + (w.Assumed ? "  (날짜 일부 가정)" : "");
            GUI.Label(new Rect(r.x, r.y + 22f, r.width - 250f, r.height - 22f), body, _text);

            // 지연 넣어 보기
            float bx = r.xMax - 240f;
            GUI.Label(new Rect(bx, r.y, 240f, 20f), "이 작업이 늦으면?  (지금 " + Signed(_delays[i]) + "일)", _label);
            InfoBox.Button(new Rect(bx, r.y + 24f, 74f, 24f), "+30일", () => _delays[i] += 30);
            InfoBox.Button(new Rect(bx + 80f, r.y + 24f, 74f, 24f), "−30일", () => _delays[i] = Math.Max(-w.Days + 1, _delays[i] - 30));
            InfoBox.Button(new Rect(bx + 160f, r.y + 24f, 74f, 24f), "지연 지우기", () => Array.Clear(_delays, 0, _delays.Length));
            double d = logic.ProjectDuration - baseEnd;
            GUI.Label(new Rect(bx, r.y + 54f, 240f, 60f),
                "논리 준공 " + N0(logic.ProjectDuration) + "일 (" + Signed(d) + "일)\n"
                + (logic.Critical[i] ? "주공정이라 늦은 만큼 준공도 늦다" : "여유 " + N0(logic.TotalFloat[i]) + "일 안에서는 준공이 그대로"), _text);
        }

        private static void Fill(Rect r, Color c)
        {
            Color prev = GUI.color;
            GUI.color = HudStyle.Ui(c);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
        }

        private static string Date(ConstructionSchedule s, double day) => s.ProjectStart.AddDays(day).ToString("yyyy.M.d", Invariant);
        private static string N0(double v) => v.ToString("N0", Invariant);
        private static string Signed(double v) => (v >= 0 ? "+" : "−") + Math.Abs(v).ToString("N0", Invariant);
        private static string Circled(int m) => "①②③④⑤⑥⑦⑧⑨".Substring(Math.Min(m, 8), 1);
        private static string ShortTitle(string t)
        {
            int i = t.IndexOf(" (", StringComparison.Ordinal);
            return i > 0 ? t.Substring(0, i) : t;
        }

        private static string Short(string t)
        {
            int i = t.IndexOf(" —", StringComparison.Ordinal);
            return i > 0 ? t.Substring(0, i) : t;
        }
    }
}
