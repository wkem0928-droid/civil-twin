using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CivilTwin.Core
{
    /// <summary>
    /// 교량 표준 공정 (2026-11-27, 랜드마크 인천대교). 중간 날짜 자료가 없어(착공 2005.7, 준공 2009.10, 2008.3 공정률 70 %, 2009.6 상판 완료만 확인)
    /// 작업 기간을 **표준 시공 속도(가정)**와 물량으로 계산한다. 그 결과를 자료의 확인점과 비교한다 (BridgeReportPart).
    ///
    ///   주탑 = 올림(높이 ÷ 한 번에 올리는 높이 4 m) × 7일          (자동상승 거푸집, 가정)
    ///   사장교 상판 = 올림(주경간 ÷ 2 ÷ 세그먼트 12 m) × 7일 + 키 세그먼트 14일   (주탑마다 양쪽으로 동시에 내밀기, 가정)
    ///   접속교 상판 = 올림(구간 길이 ÷ 하루 5 m)                     (프리캐스트 FCM, 동·서 동시, 가정)
    ///   고가교 상판 = 경간 수(길이 ÷ 50 m) × 2일                      (FSLM 런칭 거더 한 대씩, 가정)
    ///   준비 120일, 주탑 기초 270일, 접속교·고가교 하부 365일, 포장·부대 120일 (가정)
    /// </summary>
    public static partial class StandardSchedules
    {
        public const double BridgePrepDays = 120, PylonFoundationDays = 270, SubstructureDays = 365, BridgeFinishingDays = 120;
        public const double PylonLiftMeters = 4.0, PylonLiftDays = 7.0;
        public const double DeckSegmentMeters = 12.0, DeckSegmentDays = 7.0, KeySegmentDays = 14.0;
        public const double FcmMetersPerDay = 5.0, FslmDaysPerSpan = 2.0;

        public static double PylonDays(BridgeSpec b) => Math.Ceiling(b.PylonHeight / PylonLiftMeters - 1e-9) * PylonLiftDays;
        public static double CableStayedDeckDays(BridgeSpec b) => Math.Ceiling(b.CableStayedSpans[2] / 2.0 / DeckSegmentMeters - 1e-9) * DeckSegmentDays + KeySegmentDays;
        public static double FcmDays(BridgeSegment s) => Math.Ceiling(s.Length / FcmMetersPerDay - 1e-9);
        public static int FslmSpans(BridgeSegment s) => (int)Math.Round(s.Length / s.Span);
        public static double FslmDays(BridgeSegment s) => FslmSpans(s) * FslmDaysPerSpan;

        /// <summary>교량 공정 (작업 날짜 = 표준 속도로 계산한 논리 공정, 모두 가정). 준공일만 자료.</summary>
        public static ConstructionSchedule Bridge(BridgeSpec b)
        {
            if (b == null) throw new ArgumentNullException(nameof(b));
            BridgeSegment wv = b.Segment("서측 고가교"), wa = b.Segment("서측 접속교"), ea = b.Segment("동측 접속교"), ev = b.Segment("동측 고가교");
            var s = new ConstructionSchedule
            {
                Title = b.Name + " (" + b.TypeName + ")",
                Name = b.Name,
                Kind = StructureKind.Bridge,
                ProjectStart = b.Start,
                Completion = b.Completion,
                Sources = b.Sources,
                SourceNote = "착공 2005.7·준공 2009.10은 자료(날짜는 1일로 가정)이고, 작업 기간은 모두 표준 시공 속도로 계산한 가정이다 (StandardSchedules.Bridge).",
                Notes = "해상 작업일(파도·바람), 작업선·크레인 수, 강재 제작 기간은 넣지 않았다.",
            };
            var finish = new List<DateTime>();
            void Add(string name, double days, int[] pred, string source, string what, string why, string equip)
            {
                DateTime start = b.Start;
                foreach (int p in pred) if (finish[p] > start) start = finish[p];
                DateTime end = start.AddDays(days);
                finish.Add(end);
                s.Works.Add(new ScheduleWork { Name = name, Start = start, Finish = end, Predecessors = pred, Assumed = true, Source = source,
                    What = what, Why = why, Equipment = equip });
            }
            string N0(double v) => v.ToString("N0", CultureInfo.InvariantCulture);
            Add("준비공사", BridgePrepDays, new int[0], "표준 " + N0(BridgePrepDays) + "일 (가정)",
                "가설 잔교·작업선 계류장, 프리캐스트 제작장(세그먼트·50 m 거더)을 만든다.",
                "바다 위 공사라 자재를 만들 곳과 배로 나를 길이 먼저 있어야 한다.", "작업선, 해상 크레인, 바지선");
            Add("주탑 기초", PylonFoundationDays, new[] { 0 }, "표준 " + N0(PylonFoundationDays) + "일 (가정)",
                "바다 밑 암반까지 대구경 현장타설말뚝을 박고 그 위에 기초 콘크리트를 친다.",
                "주탑과 케이블이 받는 상판 무게가 모두 이 기초로 내려간다.", "해상 천공기(RCD), 해상 크레인, 콘크리트 펌프선");
            Add("주탑 (" + b.PylonHeight.ToString("0.#", CultureInfo.InvariantCulture) + " m)", PylonDays(b), new[] { 1 },
                "계산: 올림(" + b.PylonHeight.ToString("0.#", CultureInfo.InvariantCulture) + " ÷ " + PylonLiftMeters.ToString("0") + " m) × " + PylonLiftDays.ToString("0") + "일 = " + N0(PylonDays(b)) + "일 (자동상승 거푸집 속도 가정)",
                "자동상승(클라이밍) 거푸집으로 한 단씩 콘크리트를 쳐 올린다 (역Y자 주탑).",
                "주탑이 다 올라가야 케이블을 걸어 상판을 내밀 수 있다.", "자동상승 거푸집, 타워크레인, 콘크리트 펌프");
            Add("사장교 상판·케이블", CableStayedDeckDays(b), new[] { 2 },
                "계산: 올림(" + N0(b.CableStayedSpans[2]) + " ÷ 2 ÷ " + DeckSegmentMeters.ToString("0") + " m) × " + DeckSegmentDays.ToString("0") + "일 + 키 세그먼트 " + KeySegmentDays.ToString("0") + "일 = " + N0(CableStayedDeckDays(b)) + "일 (속도 가정)",
                "주탑에서 양쪽으로 상판 세그먼트를 하나씩 내밀고(캔틸레버), 세그먼트마다 케이블을 걸어 당긴다. 가운데에서 만나면 키 세그먼트로 잇는다.",
                "800 m 주경간 밑에는 배가 다녀야 해서 받침 없이 주탑과 케이블만으로 지지한다.", "데릭 크레인(세그먼트 인양), 케이블 인장 잭, 해상 크레인");
            Add("접속교·고가교 하부", SubstructureDays, new[] { 0 }, "표준 " + N0(SubstructureDays) + "일 (가정)",
                "기초 말뚝과 교각을 세운다 (고가교 50 m마다, 접속교 최대 145 m마다).", "상판을 올려놓을 받침이다.", "해상 천공기, 교각 거푸집, 해상 크레인");
            Add("접속교 상판 (FCM)", FcmDays(wa), new[] { 4 },
                "계산: 올림(" + N0(wa.Length) + " m ÷ 하루 " + FcmMetersPerDay.ToString("0") + " m) = " + N0(FcmDays(wa)) + "일, 동·서 동시 (속도 가정)",
                "교각을 중심으로 양쪽에 같은 무게로 프리캐스트 세그먼트를 붙여 나간다 (균형 캔틸레버, 최대 경간 145 m).",
                "깊은 바다 위라 동바리를 세울 수 없다. 양쪽 무게를 맞추며 교각에서 내밀어 간다.", "세그먼트 인양 장비, 프리스트레싱 잭");
            Add("서측 고가교 상판 (FSLM)", FslmDays(wv), new[] { 4 },
                "계산: " + FslmSpans(wv) + "경간(" + N0(wv.Length) + " ÷ 50 m) × " + FslmDaysPerSpan.ToString("0") + "일 = " + N0(FslmDays(wv)) + "일 (런칭 거더 1대, 속도 가정)",
                "제작장에서 만든 50 m 상판 한 경간(약 1,300 t)을 통째로 런칭 거더로 옮겨 교각 위에 얹는다.",
                "같은 모양 경간이 많아 공장처럼 미리 만들어 얹으면 해상 작업이 줄고 빠르다.", "런칭 거더(FSLM 장비), 운반대차");
            Add("동측 고가교 상판 (FSLM)", FslmDays(ev), new[] { 4 },
                "계산: " + FslmSpans(ev) + "경간 × " + FslmDaysPerSpan.ToString("0") + "일 = " + N0(FslmDays(ev)) + "일 (속도 가정)",
                "서측과 같은 방법.", "같은 모양 경간을 공장식으로 반복한다.", "런칭 거더, 운반대차");
            Add("포장·부대공", BridgeFinishingDays, new[] { 3, 5, 6, 7 }, "표준 " + N0(BridgeFinishingDays) + "일 (가정)",
                "교면 방수·포장, 방호울타리, 조명, 신축이음, 계측 설비를 갖춘다.", "차가 다니려면 상판 위 마감과 안전 시설이 있어야 한다.", "아스팔트 피니셔, 롤러, 고소작업차");
            s.FocusWork = 2;

            s.Milestones.Add((b.Start, "착공", "자료"));
            foreach (var c in b.Checkpoints) s.Milestones.Add(c);
            s.Milestones.Add((b.Completion, "준공", "자료"));

            ScheduleWork pylon = s.Works[2], deck = s.Works[3], fslm = s.Works[6];
            DateTime Mid(ScheduleWork w) => w.Start.AddDays(w.Days / 2.0);
            s.Steps.Add(new ConstructionStep { Date = b.Start, Title = "착공 — 바다", Text = "영종도와 송도 사이 바다. 제작장과 작업선 계류장부터 만든다." });
            s.Steps.Add(new ConstructionStep { Date = s.Works[1].Start, Title = "주탑 기초", Text = "바다 밑 암반까지 말뚝을 박는다. 같은 때 고가교·접속교 교각도 세운다." });
            s.Steps.Add(new ConstructionStep { Date = Mid(pylon), Title = "주탑 오르기", Text = "자동상승 거푸집으로 한 단씩 쳐 올린다. 고가교는 50 m 상판을 통째로 얹어 나간다." });
            s.Steps.Add(new ConstructionStep { Date = pylon.Finish, Title = "주탑 완성", Text = "주탑 " + b.PylonHeight.ToString("0.#", CultureInfo.InvariantCulture) + " m. 이제 케이블을 걸며 상판을 내민다." });
            s.Steps.Add(new ConstructionStep { Date = Mid(deck), Title = "상판 내밀기", Text = "주탑에서 양쪽으로 세그먼트를 붙이고 케이블로 당긴다 (균형을 맞추며)." });
            s.Steps.Add(new ConstructionStep { Date = deck.Finish.AddDays(-KeySegmentDays), Title = "키 세그먼트", Text = "주경간 가운데에서 두 상판이 만나 이어진다 — 선박 통과 높이 " + b.NavigationClearance.ToString("0") + " m." });
            s.Steps.Add(new ConstructionStep { Date = s.Works[8].Start, Title = "상판 완료 (계산)", Text = "표준 속도로는 이날 모든 상판이 끝난다. 자료는 2009.6에 \"상판·구조물 모두 갖춤\"." });
            s.Steps.Add(new ConstructionStep { Date = b.Completion, Title = "준공", Text = "포장·부대공을 마치고 준공 (2009.10)." });
            s.Steps.Sort((x, y) => x.Date.CompareTo(y.Date));
            return s;
        }
    }

    /// <summary>교량 보고서 부분: 구간 구성, 표준 속도 계산, 자료 확인점과 비교</summary>
    public sealed class BridgeReportPart : StructureReportPart
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private readonly BridgeSpec _b;
        private readonly ConstructionSchedule _s;

        public BridgeReportPart(BridgeSpec b, ConstructionSchedule s) { _b = b; _s = s; }

        public override string Computed => "여유·주공정·작업 기간";

        public override void WriteSections(StringBuilder sb, ref int number)
        {
            sb.AppendLine("## " + number++ + ". 구간 구성 (서 → 동)");
            sb.AppendLine();
            sb.AppendLine("| 구간 | 측점 (m) | 길이 (m) | 가설 공법 | 경간 |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (BridgeSegment g in _b.Segments)
                sb.AppendLine("| " + g.Name + " | " + N0(g.From) + " ~ " + N0(g.To) + " | " + N0(g.Length) + " | " + g.Method + " | "
                    + (g.Name == "사장교" ? string.Join(" + ", Array.ConvertAll(_b.CableStayedSpans, v => N0(v))) : (g.Span == 50 ? "50 m × " + StandardSchedules.FslmSpans(g) : "최대 " + N0(g.Span) + " m")) + " |");
            sb.AppendLine();
            sb.AppendLine("노선은 OpenStreetMap에서 받은 인천대교고속도로(영종 → 송도)다. OSM의 사장교 구간이 1,482 m로 공개 값 1,480 m와 0.1 % 차이라, 그 서쪽 끝(측점 8,487 m)을 사장교 시작으로 두고 국토교통부 구간 연장을 차례로 놓았다. 주탑 측점은 "
                + N0(_b.PylonStations[0]) + " · " + N0(_b.PylonStations[1]) + " m.");
            sb.AppendLine();

            CpmResult r = _s.Logic();
            DateTime logicEnd = _s.ProjectStart.AddDays(r.ProjectDuration);
            DateTime deckEnd = _s.Works[8].Start;
            sb.AppendLine("## " + number++ + ". 표준 속도로 계산한 공정과 자료 비교");
            sb.AppendLine();
            sb.AppendLine("| 확인점 | 자료 | 표준 속도 계산 | 차이 |");
            sb.AppendLine("|---|---|---|---|");
            var dataDeck = _b.Checkpoints[_b.Checkpoints.Count - 1].date;
            sb.AppendLine("| 상판 완료 | " + D(dataDeck) + " (" + _b.Checkpoints[_b.Checkpoints.Count - 1].source + ") | " + D(deckEnd) + " | " + N0((dataDeck - deckEnd).TotalDays) + "일 |");
            sb.AppendLine("| 준공 | " + D(_s.Completion) + " (국토교통부) | " + D(logicEnd) + " | " + N0((_s.Completion - logicEnd).TotalDays) + "일 |");
            sb.AppendLine();
            sb.AppendLine("- 주공정은 주탑 기초 → 주탑 → 사장교 상판 (교량 전체 공기를 사장교가 정한다). 고가교 FSLM은 여유가 있다.");
            sb.AppendLine("- 표준 속도로는 실제보다 " + N0((_s.Completion - logicEnd).TotalDays) + "일 빨리 끝난다. 실제 작업별 기간 자료가 없어 어느 작업이 더 걸렸는지는 정하지 않는다 (해상 기상, 강재 제작, 설계 변경 2004.12 주경간 700 → 800 m 등은 후보일 뿐).");
            sb.AppendLine("- 2008.3 \"공정률 70 %\"는 금액 기준일 수 있어 날짜 비교만 했다.");
            sb.AppendLine();
        }

        public override IEnumerable<string> LimitLines
        {
            get
            {
                yield return "- **가정 (속도):** 주탑 " + StandardSchedules.PylonLiftMeters.ToString("0") + " m/" + StandardSchedules.PylonLiftDays.ToString("0") + "일, 사장교 세그먼트 "
                    + StandardSchedules.DeckSegmentMeters.ToString("0") + " m/" + StandardSchedules.DeckSegmentDays.ToString("0") + "일, 접속교 FCM " + StandardSchedules.FcmMetersPerDay.ToString("0")
                    + " m/일, FSLM 1경간/" + StandardSchedules.FslmDaysPerSpan.ToString("0") + "일, 준비·기초·하부·포장 기간 — 출처 없음.";
                yield return "- **가정 (그림):** 상판 높이(고가교 해수면 위 " + _b.ViaductDeckHeight.ToString("0") + " m, 주경간은 선박 통과 높이 + 3 m), 교각 간격은 구간 대표 경간.";
            }
        }

        private static string N0(double v) => v.ToString("N0", Invariant);
        private static string D(DateTime d) => d.ToString("yyyy.M.d", Invariant);
    }
}
