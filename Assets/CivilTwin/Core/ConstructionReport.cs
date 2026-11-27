using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CivilTwin.Core
{
    /// <summary>
    /// 구조물 종류마다 보고서에 더하는 부분 (댐: 성토 높이·존 물량·운반 검산 / 터널: 막장 공정 / 교량: 주탑·상판).
    /// 공통 보고서(요약·네트워크·논리 vs 실제·지연·단계·가정)는 ConstructionReport가 쓰고, 이 부분은 그 사이에 끼운다.
    /// </summary>
    public abstract class StructureReportPart
    {
        /// <summary>단계 표에 더할 열 이름 (없으면 null)</summary>
        public virtual string StepColumn => null;
        /// <summary>그 날짜의 열 값 (예: 성토 높이)</summary>
        public virtual string StepValue(DateTime date) => "";
        /// <summary>종류별 절을 쓴다. number = 다음 절 번호 (쓴 만큼 늘린다)</summary>
        public abstract void WriteSections(StringBuilder sb, ref int number);
        /// <summary>마지막 절 "가정"에 더할 줄들</summary>
        public virtual IEnumerable<string> LimitLines => new string[0];
        /// <summary>머리말의 "계산 결과" 설명 (예: "여유·주공정·높이")</summary>
        public virtual string Computed => "여유·주공정";
    }

    /// <summary>
    /// 시공계획 보고서 — 모든 구조물 공통 (2026-11-25 소양강댐 댐 보고서, 2026-11-27 일반화).
    /// 공정표·단계 화면의 숫자를 한 문서(Markdown)와 엑셀용 표(CSV)로 남긴다:
    /// 요약 → 네트워크(그림·표) → 논리와 실제 비교 → 지연 영향 → 시공 단계 → (구조물 종류별 절) → 자료와 맞지 않는 점·가정·출처.
    /// 문장은 계산값만 풀어 쓴다 (원인을 추정하지 않는다).
    /// </summary>
    public static class ConstructionReport
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        /// <summary>소양강댐처럼 존 단면이 있는 댐 (기존 호출 그대로)</summary>
        public static string Markdown(ConstructionSchedule s, DamZoneSection z, IList<ConstructionStep> steps,
            double[] delays, string generatedAt)
        {
            if (z == null) throw new ArgumentNullException(nameof(z));
            if (steps == null) throw new ArgumentNullException(nameof(steps));
            return Markdown(s, delays, generatedAt, new DamZoneReportPart(z), steps);
        }

        /// <param name="delays">공정표 화면에서 넣은 지연 (없으면 null)</param>
        /// <param name="part">구조물 종류별 절 (없으면 공통 절만)</param>
        /// <param name="steps">단계 (없으면 s.Steps)</param>
        public static string Markdown(ConstructionSchedule s, double[] delays, string generatedAt,
            StructureReportPart part = null, IList<ConstructionStep> steps = null)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            steps = steps ?? s.Steps;
            CpmResult r = s.Logic();
            var sb = new StringBuilder();

            sb.AppendLine("# " + KindName(s.Kind) + " 시공계획 보고서 — " + s.Title);
            sb.AppendLine();
            sb.AppendLine("- 만든 시각: " + generatedAt);
            sb.AppendLine("- 만든 프로그램: CIVIL TWIN (3D·4D 시공계획 시뮬레이터)");
            sb.AppendLine("- 날짜는 공개 자료, " + (part != null ? part.Computed : "여유·주공정") + "은 계산 결과다. 가정은 마지막 절에 나눠 적었다.");
            sb.AppendLine();

            Summary(sb, s, r, delays);
            Network(sb, s, r);
            Compare(sb, s, r);
            Delay(sb, s, r, delays);
            Steps(sb, s, part, steps);
            int number = 6;
            part?.WriteSections(sb, ref number);
            Limits(sb, s, part, number);
            return sb.ToString();
        }

        public static string KindName(StructureKind kind) =>
            kind == StructureKind.Dam ? "댐" : kind == StructureKind.Tunnel ? "터널" : "교량";

        // ── 1. 요약 ───────────────────────────────────────────

        private static void Summary(StringBuilder sb, ConstructionSchedule s, CpmResult r, double[] delays)
        {
            double actual = s.Day(s.Completion);
            sb.AppendLine("## 1. 요약");
            sb.AppendLine();
            sb.AppendLine("| 항목 | 값 |");
            sb.AppendLine("|---|---|");
            sb.AppendLine("| 착공 ~ 준공 (자료) | " + D(s.ProjectStart) + " ~ " + D(s.Completion) + " (" + N0(actual) + "일) |");
            sb.AppendLine("| 논리 공정 (CPM) | " + N0(r.ProjectDuration) + "일 → " + D(s.ProjectStart.AddDays(r.ProjectDuration)) + " |");
            sb.AppendLine("| 차이 | 실제가 " + N0(actual - r.ProjectDuration) + "일 길다 |");
            sb.AppendLine("| 주공정 | " + string.Join(" → ", CriticalNames(s, r)) + " |");
            int late = MostLate(s, r, out double lateDays);
            if (lateDays > 0.5)
                sb.AppendLine("| 여유를 가장 많이 넘겨 시작한 작업 (준공 전에 끝난 작업 중) | " + s.Works[late].Name + " (여유 " + N0(r.TotalFloat[late]) + "일, 넘긴 날 " + N0(lateDays) + "일) |");
            sb.AppendLine();
            sb.AppendLine("주공정의 작업은 하루 늦으면 전체도 하루 늦는다. "
                + (lateDays > 0.5 ? s.Works[late].Name + "는 주공정이 아니었지만 여유를 " + N0(lateDays) + "일 넘겨 늦게 시작했고, 실제 끝(" + D(s.Works[late].Finish)
                    + ")이 논리 공정의 끝보다 " + N0(s.Day(s.Works[late].Finish) - r.ProjectDuration) + "일 늦다. 늦게 시작한 이유는 자료에 없어 적지 않는다." : ""));
            sb.AppendLine();
            if (HasDelay(delays))
            {
                CpmResult dr = s.Logic(delays);
                sb.AppendLine("**화면에서 넣은 지연:** " + DelayText(s, delays) + " → 논리 공정 " + N0(dr.ProjectDuration) + "일 ("
                    + Signed(dr.ProjectDuration - r.ProjectDuration) + "일). 아래 표는 지연을 넣지 않은 기준 공정이다.");
                sb.AppendLine();
            }
        }

        // ── 2. 네트워크 ───────────────────────────────────────

        private static void Network(StringBuilder sb, ConstructionSchedule s, CpmResult r)
        {
            sb.AppendLine("## 2. 네트워크 공정표 (CPM)");
            sb.AppendLine();
            sb.AppendLine("```mermaid");
            sb.Append(Mermaid(s, r));
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("빨간 상자 = 주공정. 관계는 모두 끝 → 시작(FS), 착공일 " + D(s.ProjectStart) + " = 0일.");
            sb.AppendLine();
            sb.AppendLine("| 번호 | 작업 | 선행 | 기간 | ES | EF | LS | LF | 전체 여유 | 자유 여유 | 주공정 |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
            for (int i = 0; i < s.Works.Count; i++)
            {
                ScheduleWork w = s.Works[i];
                sb.AppendLine("| " + (i + 1) + " | " + w.Name + (w.Assumed ? " \\*" : "") + " | " + Preds(w) + " | " + N0(w.Days)
                    + " | " + N0(r.EarlyStart[i]) + " | " + N0(r.EarlyFinish[i]) + " | " + N0(r.LateStart[i]) + " | " + N0(r.LateFinish[i])
                    + " | " + N0(r.TotalFloat[i]) + " | " + N0(r.FreeFloat[i]) + " | " + (r.Critical[i] ? "●" : "") + " |");
            }
            sb.AppendLine();
            sb.AppendLine("\\* 날짜 일부 가정 (9절).  ES = 선행 EF 중 큰 값, EF = ES + 기간, LF = 후속 LS 중 작은 값, LS = LF − 기간, 전체 여유 = LS − ES.");
            sb.AppendLine();
        }

        /// <summary>GitHub에서 그림으로 보이는 네트워크 (Mermaid flowchart). 주공정은 빨강.</summary>
        public static string Mermaid(ConstructionSchedule s, CpmResult r)
        {
            var sb = new StringBuilder();
            sb.AppendLine("flowchart LR");
            for (int i = 0; i < s.Works.Count; i++)
                sb.AppendLine("  W" + (i + 1) + "[\"" + s.Works[i].Name + "<br/>" + N0(s.Works[i].Days) + "일, 여유 " + N0(r.TotalFloat[i]) + "일\"]");
            sb.AppendLine("  E([\"논리 준공<br/>" + N0(r.ProjectDuration) + "일\"])");
            var hasNext = new bool[s.Works.Count];
            for (int i = 0; i < s.Works.Count; i++)
                foreach (int p in s.Works[i].Predecessors)
                {
                    hasNext[p] = true;
                    sb.AppendLine("  W" + (p + 1) + (r.Critical[p] && r.Critical[i] ? " ==> " : " --> ") + "W" + (i + 1));
                }
            for (int i = 0; i < s.Works.Count; i++)
                if (!hasNext[i]) sb.AppendLine("  W" + (i + 1) + (r.Critical[i] ? " ==> " : " --> ") + "E");
            var critical = new List<string>();
            for (int i = 0; i < s.Works.Count; i++) if (r.Critical[i]) critical.Add("W" + (i + 1));
            sb.AppendLine("  classDef critical fill:#c0392b,color:#ffffff,stroke:#7b241c");
            sb.AppendLine("  class " + string.Join(",", critical) + " critical");
            return sb.ToString();
        }

        // ── 3. 논리와 실제 ────────────────────────────────────

        private static void Compare(StringBuilder sb, ConstructionSchedule s, CpmResult r)
        {
            sb.AppendLine("## 3. 논리 공정과 실제 날짜");
            sb.AppendLine();
            sb.AppendLine("| 작업 | 논리 시작 ~ 끝 | 실제 시작 ~ 끝 (자료) | 시작 차이 | 가장 늦은 시작을 넘긴 날 |");
            sb.AppendLine("|---|---|---|---|---|");
            for (int i = 0; i < s.Works.Count; i++)
            {
                ScheduleWork w = s.Works[i];
                double diff = s.Day(w.Start) - r.EarlyStart[i], late = s.LateBeyondFloat(r, i);
                sb.AppendLine("| " + w.Name + " | " + D(s.ProjectStart.AddDays(r.EarlyStart[i])) + " ~ " + D(s.ProjectStart.AddDays(r.EarlyFinish[i]))
                    + " | " + D(w.Start) + " ~ " + D(w.Finish) + " | " + (Math.Abs(diff) < 0.5 ? "0" : Signed(diff)) + "일 | "
                    + (late > 0.5 ? "**" + N0(late) + "일**" : "여유 안") + " |");
            }
            sb.AppendLine();
            int last = s.AsBuiltLast;
            sb.AppendLine("실제로 가장 나중에 끝난 작업은 " + s.Works[last].Name + " (" + D(s.Works[last].Finish) + ")이다.");
            sb.AppendLine();
        }

        // ── 4. 지연 영향 ──────────────────────────────────────

        /// <summary>작업 i만 days만큼 늦을 때 전체 공기가 늘어나는 날</summary>
        public static double DelayImpact(ConstructionSchedule s, int i, double days)
        {
            var d = new double[s.Works.Count];
            d[i] = days;
            return s.Logic(d).ProjectDuration - s.Logic().ProjectDuration;
        }

        private static void Delay(StringBuilder sb, ConstructionSchedule s, CpmResult r, double[] delays)
        {
            sb.AppendLine("## 4. 이 작업이 늦으면? (한 작업만 늦을 때 논리 준공이 늦어지는 날)");
            sb.AppendLine();
            sb.AppendLine("| 작업 | 30일 늦음 | 180일 늦음 | 400일 늦음 | 준공에 영향을 주기 시작하는 지연 |");
            sb.AppendLine("|---|---|---|---|---|");
            for (int i = 0; i < s.Works.Count; i++)
                sb.AppendLine("| " + s.Works[i].Name + " | " + Plus(DelayImpact(s, i, 30)) + " | " + Plus(DelayImpact(s, i, 180)) + " | " + Plus(DelayImpact(s, i, 400))
                    + " | " + (r.TotalFloat[i] < 0.5 ? "바로 (주공정)" : N0(r.TotalFloat[i]) + "일을 넘으면") + " |");
            sb.AppendLine();
        }

        // ── 5. 시공 단계 ──────────────────────────────────────

        private static void Steps(StringBuilder sb, ConstructionSchedule s, StructureReportPart part, IList<ConstructionStep> steps)
        {
            string column = part?.StepColumn;
            sb.AppendLine("## 5. 시공 단계");
            sb.AppendLine();
            sb.AppendLine("| 단계 | 날짜 | 내용 | 그날 하는 작업 |" + (column != null ? " " + column + " |" : ""));
            sb.AppendLine("|---|---|---|---|" + (column != null ? "---|" : ""));
            for (int k = 0; k < steps.Count; k++)
            {
                DateTime d = steps[k].Date;
                sb.AppendLine("| " + (k + 1) + " | " + D(d) + " | " + steps[k].Title + " | " + Active(s, d) + " |"
                    + (column != null ? " " + part.StepValue(d) + " |" : ""));
            }
            sb.AppendLine();
            sb.AppendLine("작업별로 무엇을·왜·장비:");
            sb.AppendLine();
            foreach (ScheduleWork w in s.Works)
                sb.AppendLine("- **" + w.Name + "** — " + w.What + " (왜: " + w.Why + ") 장비: " + w.Equipment + ".");
            sb.AppendLine();
        }

        // ── 6. 성토 높이 ──────────────────────────────────────

        internal static void Heights(StringBuilder sb, DamZoneSection z, int n)
        {
            sb.AppendLine("## " + n + ". 성토 계획 (일정 속도 가정)");
            sb.AppendLine();
            sb.AppendLine("성토 " + D(z.FillStart) + " ~ " + D(z.CrestDate) + " = " + N0(z.FillDays) + "일, 하루 평균 "
                + N0(z.TotalVolume) + " ÷ " + N0(z.FillDays) + " = **" + N0(z.DailyVolume) + " m³**."
                + (z.HasFinishing ? " (이 보고서는 기사 [D]의 \"8·19 홍수 때 이미 123 m\"를 따랐다: " + D(z.CrestDate) + "에 마루, " + D(z.FillEnd) + "까지 마무리.)" : ""));
            sb.AppendLine();
            sb.AppendLine("| 날짜 | 누적 성토 (만 m³) | 체적 비율 | 높이 (m) | 표고 EL | 앞 줄보다 오른 높이 (m) |");
            sb.AppendLine("|---|---|---|---|---|---|");
            double before = 0.0;
            for (DateTime d = z.FillStart; ; d = d.AddMonths(3))
            {
                DateTime at = d > z.FillEnd ? z.FillEnd : d;
                double f = z.FillFraction(at), e = z.FillElevation(at), h = e - z.FoundationElevation;
                sb.AppendLine("| " + D(at) + " | " + (f * z.TotalVolume / 10000.0).ToString("0.0", Invariant) + " | " + (f * 100).ToString("0.0", Invariant)
                    + " % | " + h.ToString("0.0", Invariant) + " | " + e.ToString("0.0", Invariant) + " | " + (h - before).ToString("0.0", Invariant) + " |");
                before = h;
                if (at >= z.FillEnd) break;
            }
            sb.AppendLine();
            sb.AppendLine("아래쪽은 단면이 넓어(저폭 " + z.TotalWidth(z.FoundationElevation).ToString("0.0", Invariant) + " m ↔ 마루폭 " + z.CrestWidth.ToString("0", Invariant)
                + " m) 같은 양을 쌓아도 처음에는 높이가 천천히, 나중에는 빨리 오른다. 높이 아래 절반에 체적의 "
                + (100.0 * z.VolumeBelow(z.FoundationElevation + z.Height / 2.0) / z.TotalVolume).ToString("0", Invariant) + " %가 있다.");
            sb.AppendLine();
            TwoRecords(sb, z);
        }

        /// <summary>
        /// 두 기록 비교 (2026-11-26): 본댐 완공 1972.11.10 [D][T] ↔ 같은 기사 [D] "8·19 홍수 때 이미 123 m 완성".
        /// 어느 쪽이 맞는지 정하지 않고, 각각이면 무엇이 필요한지 계산한다.
        /// </summary>
        public static void TwoRecords(StringBuilder sb, DamZoneSection z)
        {
            DateTime flood = DamZoneSection.SoyanggangFloodDate;
            DamZoneSection a = z.WithCrestDate(z.FillEnd), b = z.WithCrestDate(flood);
            double cycle = FillHaulCheck.SoyanggangCycleMinutes();
            sb.AppendLine("### 두 기록 비교 — 8·19 홍수 때 댐은 몇 m였나");
            sb.AppendLine();
            sb.AppendLine("기사 [D]는 8·19 홍수(" + D(flood) + ") 때 \"이미 123 m의 댐이 완성된 상태\"였다고 쓰고, 같은 기사가 본댐 완공을 " + D(z.FillEnd)
                + "로 적는다. 어느 쪽인지 정하지 않고 각각 계산한다 (월별 성토 자료 없음).");
            sb.AppendLine();
            sb.AppendLine("| 가정 | 쌓는 날 | 하루 성토량 (m³) | 8·19 홍수 날 높이 | 그날 체적 비율 | 8시간·일하는 날 60 % | 16시간·60 % |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach ((string name, DamZoneSection x) in new[] { ("완공 기록: " + D(z.FillEnd) + "에 마루", a), ("기사: " + D(flood) + "에 마루", b) })
            {
                FillHaulCheck h = FillHaulCheck.Soyanggang(x);
                double t8 = h.MinutesPerTrip(8.0, 0.6), t16 = h.MinutesPerTrip(16.0, 0.6);
                sb.AppendLine("| " + name + " | " + N0(x.FillDays) + "일 | " + N0(x.DailyVolume) + " | "
                    + (x.FillElevation(flood) - x.FoundationElevation).ToString("0.0", Invariant) + " m | "
                    + (100.0 * x.FillFraction(flood)).ToString("0.0", Invariant) + " % | "
                    + t8.ToString("0", Invariant) + "분 " + (t8 >= cycle ? "○" : "✕") + " | " + t16.ToString("0", Invariant) + "분 " + (t16 >= cycle ? "○" : "✕") + " |");
            }
            sb.AppendLine();
            double gapH = b.FillElevation(flood) - a.FillElevation(flood);
            sb.AppendLine("- 높이로는 " + gapH.ToString("0", Invariant) + " m 차이지만, 위쪽 " + gapH.ToString("0", Invariant) + " m에는 체적의 "
                + (100.0 * (1.0 - a.FillFraction(flood))).ToString("0", Invariant) + " %만 있다 (단면이 위로 갈수록 좁다). 그래서 하루 성토량 차이는 "
                + (100.0 * (b.DailyVolume / a.DailyVolume - 1.0)).ToString("0", Invariant) + " %다.");
            sb.AppendLine("- 운반 검산(8절, 왕복 " + cycle.ToString("0", Invariant) + "분)의 ○·✕는 두 가정이 같다 → 기사대로였어도 자료의 트럭으로 나를 수 있는 속도였다. "
                + "두 기록은 \"8·19 무렵 높이는 거의 다 올라갔고, 11.10까지 마무리\"로 함께 설명될 수 있다 (확인되지 않음).");
            sb.AppendLine();
        }

        // ── 7. 존별 물량 ──────────────────────────────────────

        internal static void Zones(StringBuilder sb, DamZoneSection z, int n)
        {
            sb.AppendLine("## " + n + ". 존별 물량 (존 치수는 가정)");
            sb.AppendLine();
            sb.AppendLine("| 존 | 하는 일 | 단면적 (m²) | 체적 (만 m³) | 비율 | 하루 평균 (m³) |");
            sb.AppendLine("|---|---|---|---|---|---|");
            double days = z.FillDays;
            foreach (DamZone zone in new[] { DamZone.Core, DamZone.Filter, DamZone.Shell })
            {
                double v = z.ZoneVolume(zone);
                sb.AppendLine("| " + (zone == DamZone.Core ? "코어 (점토)" : zone == DamZone.Filter ? "필터" : "사력") + " | "
                    + (zone == DamZone.Core ? "물을 막는다" : zone == DamZone.Filter ? "코어 흙이 씻겨 나가지 않게" : "무게로 전체를 버틴다")
                    + " | " + N0(z.Area(zone)) + " | " + (v / 10000.0).ToString("0.0", Invariant) + " | " + (100.0 * v / z.TotalVolume).ToString("0.0", Invariant)
                    + " % | " + N0(v / days) + " |");
            }
            sb.AppendLine("| 합계 | | " + N0(z.Area()) + " | " + (z.TotalVolume / 10000.0).ToString("0.0", Invariant) + " | 100.0 % | " + N0(z.DailyVolume) + " |");
            sb.AppendLine();
        }

        // ── 8. 운반 검산 ──────────────────────────────────────

        internal static void Haul(StringBuilder sb, DamZoneSection z, int n)
        {
            FillHaulCheck h = FillHaulCheck.Soyanggang(z);
            double cycle = FillHaulCheck.SoyanggangCycleMinutes();
            sb.AppendLine("## " + n + ". 운반 검산 — 자료의 덤프트럭으로 나를 수 있었나");
            sb.AppendLine();
            sb.AppendLine("- 하루 무게 = " + N0(h.DailyVolume) + " m³ × " + h.UnitWeight.ToString("0.0", Invariant) + " t/m³ (가정) = " + N0(h.DailyTons) + " t");
            sb.AppendLine("- 트럭 " + h.Trucks + "대가 한 바퀴 = 127 × 13.5 t + 20 × 32 t [T] = " + N0(h.FleetTons) + " t");
            sb.AppendLine("- 왕복 시간 = (적재 " + FillHaulCheck.SoyanggangLoadMinutes.ToString("0", Invariant) + "분(가정) + "
                + FillHaulCheck.SoyanggangHaulKm.ToString("0", Invariant) + " km ÷ 15 km/h + 4 km ÷ 20 km/h + 적하 1.05 + 대기 0.42) ÷ 0.9 (표준품셈 8-2-8) = **"
                + cycle.ToString("0.0", Invariant) + "분**");
            sb.AppendLine();
            sb.AppendLine("| 일하는 날 비율 | 대당 하루 왕복 | 8시간 작업: 한 번에 쓸 수 있는 시간 | 16시간 | 20시간 |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (double ratio in new[] { 1.0, 0.8, 0.6 })
            {
                sb.Append("| " + (ratio * 100).ToString("0", Invariant) + " % | " + h.TripsPerDay(ratio).ToString("0.0", Invariant) + "번");
                foreach (double hours in new[] { 8.0, 16.0, 20.0 })
                {
                    double t = h.MinutesPerTrip(hours, ratio);
                    sb.Append(" | " + t.ToString("0", Invariant) + "분 " + (t >= cycle ? "○" : "✕"));
                }
                sb.AppendLine(" |");
            }
            sb.AppendLine();
            sb.AppendLine("○ = 왕복 시간 " + cycle.ToString("0", Invariant) + "분보다 길다 (나를 수 있다). 트럭이 모두 본댐 성토에만 쓰였다고 본 어림이고, "
                + "운반거리 4 km는 점토 채취장 거리 [T]를 사력에도 같게 쓴 가정이다. 실제 작업 시간 자료는 없다.");
            sb.AppendLine();
        }

        // ── 9. 한계·출처 ──────────────────────────────────────

        private static void Limits(StringBuilder sb, ConstructionSchedule s, StructureReportPart part, int n)
        {
            sb.AppendLine("## " + n + ". 자료와 맞지 않는 점, 가정, 출처");
            sb.AppendLine();
            if (part != null) foreach (string line in part.LimitLines) sb.AppendLine(line);
            string assumed = AssumedWorks(s);
            if (assumed.Length > 0) sb.AppendLine("- **가정 (날짜):** " + assumed + "의 시작 또는 끝 날짜. \"월\"까지만 있는 날짜는 그 달 1일로 두었다.");
            sb.AppendLine("- **다루지 않은 것:** 작업 겹침(SS·FF 관계), 날씨·자원 제약, 비용." + (s.Notes.Length > 0 ? " " + s.Notes : ""));
            sb.AppendLine("- **출처:** " + s.Sources + (s.SourceNote.Length > 0 ? ". " + s.SourceNote : "."));
            sb.AppendLine();
            sb.AppendLine("| 작업 | 날짜의 근거 |");
            sb.AppendLine("|---|---|");
            foreach (ScheduleWork w in s.Works) sb.AppendLine("| " + w.Name + " | " + w.Source + " |");
            sb.AppendLine();
            sb.AppendLine("| 사건 | 날짜 | 출처 |");
            sb.AppendLine("|---|---|---|");
            foreach (var m in s.Milestones) sb.AppendLine("| " + m.text + " | " + D(m.date) + " | " + m.source + " |");
        }

        // ── CSV (엑셀) ────────────────────────────────────────

        /// <summary>작업표 (엑셀용). 쉼표가 든 글은 따옴표로 감싼다.</summary>
        public static string Csv(ConstructionSchedule s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            CpmResult r = s.Logic();
            var sb = new StringBuilder();
            sb.AppendLine("번호,작업,선행,기간(일),ES,EF,LS,LF,전체여유,자유여유,주공정,논리시작,논리끝,실제시작,실제끝,여유를 넘긴 날,날짜 근거");
            for (int i = 0; i < s.Works.Count; i++)
            {
                ScheduleWork w = s.Works[i];
                double late = Math.Max(0.0, s.LateBeyondFloat(r, i));
                string[] cells =
                {
                    (i + 1).ToString(Invariant), w.Name, Preds(w, ";"), Raw(w.Days), Raw(r.EarlyStart[i]), Raw(r.EarlyFinish[i]),
                    Raw(r.LateStart[i]), Raw(r.LateFinish[i]), Raw(r.TotalFloat[i]), Raw(r.FreeFloat[i]), r.Critical[i] ? "예" : "",
                    Iso(s.ProjectStart.AddDays(r.EarlyStart[i])), Iso(s.ProjectStart.AddDays(r.EarlyFinish[i])), Iso(w.Start), Iso(w.Finish), Raw(late), w.Source,
                };
                for (int k = 0; k < cells.Length; k++) cells[k] = Quote(cells[k]);
                sb.AppendLine(string.Join(",", cells));
            }
            return sb.ToString();
        }

        // ── 도움 함수 ─────────────────────────────────────────

        private static string Quote(string t) =>
            t.IndexOf(',') >= 0 || t.IndexOf('"') >= 0 ? "\"" + t.Replace("\"", "\"\"") + "\"" : t;

        private static List<string> CriticalNames(ConstructionSchedule s, CpmResult r)
        {
            var list = new List<string>();
            for (int i = 0; i < s.Works.Count; i++) if (r.Critical[i]) list.Add(s.Works[i].Name);
            return list;
        }

        private static int MostLate(ConstructionSchedule s, CpmResult r, out double days)
        {
            // 준공 전에 끝난 작업 중에서 (발전소 설비는 준공 뒤라 따로 적는다)
            int best = 0;
            days = double.MinValue;
            for (int i = 0; i < s.Works.Count; i++)
            {
                if (s.Works[i].Finish > s.Completion) continue;
                double late = s.LateBeyondFloat(r, i);
                if (late > days) { days = late; best = i; }
            }
            return best;
        }

        private static string Active(ConstructionSchedule s, DateTime d)
        {
            var list = new List<string>();
            foreach (ScheduleWork w in s.Works) if (w.Start <= d && d < w.Finish) list.Add(w.Name);
            return list.Count == 0 ? "-" : string.Join(", ", list);
        }

        private static string AssumedWorks(ConstructionSchedule s)
        {
            var list = new List<string>();
            foreach (ScheduleWork w in s.Works) if (w.Assumed) list.Add(w.Name);
            return string.Join(", ", list);
        }

        private static bool HasDelay(double[] delays)
        {
            if (delays == null) return false;
            foreach (double d in delays) if (Math.Abs(d) > 0.5) return true;
            return false;
        }

        private static string DelayText(ConstructionSchedule s, double[] delays)
        {
            var list = new List<string>();
            for (int i = 0; i < delays.Length && i < s.Works.Count; i++)
                if (Math.Abs(delays[i]) > 0.5) list.Add(s.Works[i].Name + " " + Signed(delays[i]) + "일");
            return string.Join(", ", list);
        }

        private static string Preds(ScheduleWork w, string separator = ", ")
        {
            if (w.Predecessors.Length == 0) return "-";
            var list = new List<string>();
            foreach (int p in w.Predecessors) list.Add((p + 1).ToString(Invariant));
            return string.Join(separator, list);
        }

        private static string D(DateTime d) => d.ToString("yyyy.M.d", Invariant);
        private static string Iso(DateTime d) => d.ToString("yyyy-MM-dd", Invariant);
        private static string N0(double v) => v.ToString("N0", Invariant);
        private static string Raw(double v) => v.ToString("0.##", Invariant);
        private static string Plus(double v) => v < 0.5 ? "0" : "+" + N0(v) + "일";
        private static string Signed(double v) => (v >= 0 ? "+" : "−") + Math.Abs(v).ToString("N0", Invariant);
    }

    /// <summary>존 단면이 있는 댐 (소양강댐): 단계 표에 성토 높이, 성토 계획·두 기록 비교, 존별 물량, 운반 검산</summary>
    public sealed class DamZoneReportPart : StructureReportPart
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private readonly DamZoneSection _z;

        public DamZoneReportPart(DamZoneSection z) { _z = z ?? throw new ArgumentNullException(nameof(z)); }

        public override string Computed => "여유·주공정·높이";
        public override string StepColumn => "성토 높이";

        public override string StepValue(DateTime d)
        {
            double h = _z.FillElevation(d) - _z.FoundationElevation;
            return h < 0.05 ? "-" : h.ToString("N0", Invariant) + " m (EL " + _z.FillElevation(d).ToString("N0", Invariant) + ")";
        }

        public override void WriteSections(StringBuilder sb, ref int number)
        {
            ConstructionReport.Heights(sb, _z, number++);
            ConstructionReport.Zones(sb, _z, number++);
            ConstructionReport.Haul(sb, _z, number++);
        }

        public override IEnumerable<string> LimitLines
        {
            get
            {
                DamZoneSection z = _z;
                DateTime flood = DamZoneSection.SoyanggangFloodDate;
                double h = z.WithCrestDate(z.FillEnd).FillElevation(flood) - z.FoundationElevation;
                yield return "- **맞지 않는 점:** 기사 [D]는 8·19 홍수(" + flood.ToString("yyyy.M.d", Invariant) + ") 때 이미 높이 " + z.Height.ToString("N0", Invariant)
                    + " m라고 쓰지만, 같은 기사의 본댐 완공 " + z.FillEnd.ToString("yyyy.M.d", Invariant) + "까지 일정 속도로 쌓았다면 그날 " + h.ToString("N0", Invariant)
                    + " m다. 6절 \"두 기록 비교\"에서 둘 다 계산했다 (높이 차이는 크지만 하루 성토량 차이는 약 10 %).";
                yield return "- **가정 (존):** 코어 윗면 EL " + z.CoreTopElevation.ToString("N0", Invariant) + ", 윗면 폭 " + z.CoreTopWidth.ToString("N0", Invariant)
                    + " m, 구배 1:" + z.CoreSlope.ToString("0.##", Invariant) + ", 필터 한쪽 " + z.FilterWidth.ToString("N0", Invariant)
                    + " m (도면 없음). 계곡 바닥 폭 " + z.ValleyBottomWidth.ToString("0.0", Invariant) + " m는 공식 체적에 맞춰 푼 값.";
                yield return "- **가정 (운반):** 다진 단위중량, 적재 시간, 사력 운반거리 (8절).";
            }
        }
    }
}
