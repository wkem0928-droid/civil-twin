using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CivilTwin.Core
{
    /// <summary>보고서에 들어갈 값 (Runtime이 화면 도구에서 모아 넘긴다).</summary>
    public sealed class ReportInput
    {
        public string SiteName = "";
        public string GeneratedAt = "";

        // 부지
        public double PadWidth, PadLength, PadElevation, CutSlopeRatio, FillSlopeRatio;
        public double CenterEasting, CenterNorthing;

        // 토공량 (자연 상태 절토, 다져진 상태 성토)
        public double CutVolume, FillVolume, MaxCutDepth, MaxFillHeight;
        public CutMaterials Materials;
        public string RockDepthLabel = "없음";

        // 장비
        public double HourlyPerMachine;
        public double OnSiteHaulMeters, SpoilKm, BorrowKm;

        /// <summary>흙쌓기(펴고 다지기) 다진 흙 1 m³의 장비비 (원/m³, 표준품셈 3-4-1). 0이면 보고서에 적지 않는다.</summary>
        public double EmbankmentCostPerM3;

        // 건물 (댐처럼 건물이 없는 현장이면 쓰지 않는다 — 지금 조건의 Schedule이 null)
        public BuildingPlan Building;

        /// <summary>구조물 (댐 모드, 2026-11-20). 있으면 1절 "부지" 대신 이 제목과 줄들을 적는다.</summary>
        public string StructureTitle;
        public List<string> StructureLines = new List<string>();

        /// <summary>[0] = 지금 조건, 뒤로 저장한 대안 (이름 A, B, C)</summary>
        public List<AlternativeResult> Alternatives = new List<AlternativeResult>();

        /// <summary>가정값·출처 한 줄씩 (보고서 끝에 그대로 적는다)</summary>
        public List<string> Assumptions = new List<string>();
    }

    /// <summary>
    /// 계산 결과 보고서 (5-7). Markdown(읽기용)과 CSV(엑셀용 대안 표)를 만든다.
    /// 숫자는 모두 계산 엔진의 결과이고, 문장은 AlternativeNarrative가 그 숫자로 만든 것이다.
    /// </summary>
    public static class ProjectReport
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        public static string Markdown(ReportInput r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            if (r.Alternatives.Count == 0) throw new ArgumentException("지금 조건의 결과가 필요하다.", nameof(r));

            AlternativeResult now = r.Alternatives[0];
            var sb = new StringBuilder();

            sb.AppendLine("# 시공계획 계산 보고서 — " + r.SiteName);
            sb.AppendLine();
            sb.AppendLine("- 만든 시각: " + r.GeneratedAt);
            sb.AppendLine("- 만든 프로그램: CIVIL TWIN (3D·4D 시공계획 시뮬레이터)");
            sb.AppendLine("- 숫자는 모두 계산 결과다. **\"자동 문장\"은 그 숫자를 풀어 쓴 것이고 새 판단을 더하지 않는다.** 어느 대안이 최적인지 정하지 않는다.");
            sb.AppendLine();

            // 1. 부지 (댐이면 구조물)
            if (!string.IsNullOrEmpty(r.StructureTitle))
            {
                sb.AppendLine("## 1. 구조물: " + r.StructureTitle);
                sb.AppendLine();
                foreach (string line in r.StructureLines) sb.AppendLine("- " + line);
                sb.AppendLine();
            }
            else
            {
            sb.AppendLine("## 1. 부지");
            sb.AppendLine();
            sb.AppendLine("| 항목 | 값 |");
            sb.AppendLine("|---|---|");
            sb.AppendLine("| 중심 좌표 (EPSG:5186) | 동 " + N2(r.CenterEasting) + ", 북 " + N2(r.CenterNorthing) + " |");
            sb.AppendLine("| 크기 | 동서 " + N0(r.PadWidth) + " m × 남북 " + N0(r.PadLength) + " m |");
            sb.AppendLine("| 계획고 | " + r.PadElevation.ToString("F2", Invariant) + " m |");
            sb.AppendLine("| 비탈 구배 | 절토 1:" + r.CutSlopeRatio.ToString("0.0#", Invariant) + ", 성토 1:" + r.FillSlopeRatio.ToString("0.0#", Invariant) + " |");
            sb.AppendLine();
            }

            // 2. 토공량과 토량 균형
            EarthworkPlan plan = now.Earthwork;
            sb.AppendLine("## 2. 토공량과 토량 균형");
            sb.AppendLine();
            sb.AppendLine("| 항목 | 값 |");
            sb.AppendLine("|---|---|");
            sb.AppendLine("| 절토량 (자연 상태) | " + Vol(r.CutVolume) + " (최대 깊이 " + r.MaxCutDepth.ToString("F1", Invariant) + " m) |");
            sb.AppendLine("| 성토량 (다져진 상태) | " + Vol(r.FillVolume) + " (최대 높이 " + r.MaxFillHeight.ToString("F1", Invariant) + " m) |");
            sb.AppendLine("| 절토 구분 | 토사 " + Vol(r.Materials.SoilVolume) + ", 암 " + Vol(r.Materials.RockVolume)
                + " (암반선: " + r.RockDepthLabel + ") |");
            if (plan != null)
            {
                EarthBalance b = plan.Balance;
                sb.AppendLine("| 토량환산계수 | L " + b.LooseFactor.ToString("0.00", Invariant) + ", C " + b.CompactFactor.ToString("0.00", Invariant) + " |");
                sb.AppendLine("| 절토를 다진 양 | " + Vol(b.CutAsCompacted) + " |");
                sb.AppendLine("| 유용토 (자연) | " + Vol(b.ReusedVolume) + " |");
                sb.AppendLine(b.BorrowVolume > 0.5
                    ? "| 토취 (자연 / 실어 올 양) | " + Vol(b.BorrowVolume) + " / " + Vol(b.BorrowLooseVolume) + " |"
                    : "| 사토 (자연 / 실어 낼 양) | " + Vol(b.SurplusVolume) + " / " + Vol(b.SurplusLooseVolume) + " |");
            }
            sb.AppendLine();

            // 3. 장비와 토공 공기
            sb.AppendLine("## 3. 장비와 토공 공기");
            sb.AppendLine();
            sb.AppendLine("| 항목 | 값 |");
            sb.AppendLine("|---|---|");
            sb.AppendLine("| 굴착기 | " + now.Input.Machines + "대 × " + r.HourlyPerMachine.ToString("F2", Invariant) + " m³/시간 × "
                + now.Input.HoursPerDay.ToString("0.#", Invariant) + "시간 = " + N0(now.DailyVolume) + " m³/일 (토사) |");
            sb.AppendLine("| 덤프트럭 | " + now.Input.Trucks + "대 (15 t) |");
            sb.AppendLine("| 운반 거리 | 현장 안 평균 " + N0(r.OnSiteHaulMeters) + " m, 사토장 " + r.SpoilKm.ToString("0.#", Invariant)
                + " km, 토취장 " + r.BorrowKm.ToString("0.#", Invariant) + " km |");
            if (plan != null)
            {
                sb.AppendLine("| 굴착 일수 | " + plan.ExcavationDays.ToString("F1", Invariant) + "일 |");
                sb.AppendLine("| 운반 일수 | " + (plan.IncludesHaul ? plan.HaulDays.ToString("F1", Invariant) + "일" : "계산 안 함 (트럭 0대)") + " |");
                if (plan.Materials.RockVolume > 0.5)
                {
                    sb.AppendLine("| 암 깨기 (참고) | 대형브레이커 " + plan.BreakersToKeepUp + "대 — 표준품셈 8-2-13 보통암 암파쇄 3.1 ~ 3.7 m³/시간의 가운데 3.4. "
                        + "공기 계산의 암 굴착(토사의 0.35배)은 깨진 암을 싣는 작업만 보므로, 공기 안에 다 깨려면 이만큼 필요하다 |");
                    var blasts = new System.Collections.Generic.List<string>();
                    foreach (RockBreaker.BlastType b in RockBreaker.BlastTypes)
                        blasts.Add(b.Name + " " + plan.DrillsToKeepUp(b) + "대 (" + b.DrillHoursPerM3.ToString("0.000", Invariant) + " 시간/m³)");
                    sb.AppendLine("| 발파로 깰 때 (참고) | 크롤러드릴 110 kW: " + string.Join(", ", blasts)
                        + " — 표준품셈 3-3-1 ~ 3-3-6. 공법은 가까운 시설의 진동 허용치로 정한다 (노천발파 설계·시공지침) |");
                }
            }
            sb.AppendLine("| **토공 공기** | **" + now.EarthworkDays + "일** |");
            sb.AppendLine("| 흙쌓기 조 (펴고 다지기) | " + now.EmbankmentCrews + "조 — 표준품셈 3-4-1, 한 조(진동롤러 10 t·모터그레이더 3.6 m·굴착기 0.6 m³·살수차 0.5대) 30 cm 다짐 1,150 m³/일 |");
            if (r.EmbankmentCostPerM3 > 0 && r.FillVolume > 0)
            {
                sb.AppendLine("| 흙쌓기 장비비 | 성토 " + Vol(r.FillVolume) + " × " + N0(r.EmbankmentCostPerM3) + "원/m³ = "
                    + KoreanMoney.Format(r.FillVolume * r.EmbankmentCostPerM3) + " (대안마다 같아 비교용 합계에서 뺌, 특별인부 노임 제외) |");
            }
            sb.AppendLine();
            if (plan != null) sb.AppendLine("> 자동 문장: " + AlternativeNarrative.Describe(plan));
            sb.AppendLine();

            // 4. 건물과 공정표 (건물이 없으면 토공만)
            BuildingPlan bp = r.Building;
            BuildingSchedule s = now.Schedule;
            if (s == null)   // 건물이 없는 현장(댐): 대안 계산에 공정표가 없다
            {
                sb.AppendLine("## 4. 공정");
                sb.AppendLine();
                sb.AppendLine("건물이 없는 현장이라 공정은 토공 하나다. 전체 공기 = 토공 공기 **" + now.ProjectDays + "일**.");
                sb.AppendLine();
            }
            else
            {
            sb.AppendLine("## 4. 건물과 공정표 (CPM)");
            sb.AppendLine();
            sb.AppendLine("건물: 동서 " + N0(bp.WidthEastWest) + " m × 남북 " + N0(bp.LengthNorthSouth) + " m, " + bp.Floors + "층 (층고 "
                + bp.FloorHeight.ToString("0.0#", Invariant) + " m). 콘크리트 기초 " + N1(bp.FoundationVolume) + " + 기둥 " + N1(bp.TotalColumnVolume)
                + " + 슬래브 " + N1(bp.TotalSlabVolume) + " = " + N1(bp.TotalConcreteVolume) + " m³.");
            sb.AppendLine();
            sb.AppendLine("| 작업 | 기간 | ES | EF | LS | LF | 여유 | 주공정 |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");
            for (int i = 0; i < s.Activities.Count; i++)
            {
                CpmResult c = s.Result;
                sb.AppendLine("| " + s.Activities[i].Name + " | " + D(s.Activities[i].Duration) + " | " + D(c.EarlyStart[i]) + " | " + D(c.EarlyFinish[i])
                    + " | " + D(c.LateStart[i]) + " | " + D(c.LateFinish[i]) + " | " + D(c.TotalFloat[i]) + " | " + (c.Critical[i] ? "●" : "") + " |");
            }
            sb.AppendLine();
            sb.AppendLine("전체 공기 **" + now.ProjectDays + "일**. 주공정선은 " + AlternativeNarrative.CriticalStart(s) + "에서 시작한다.");
            sb.AppendLine();
            AppendCycleCheck(sb, bp, s);
            }

            // 5. 대안 비교
            sb.AppendLine("## 5. 대안 비교");
            sb.AppendLine();
            sb.AppendLine("| 대안 | 굴착기 | 트럭 | 시간/일 | 간섭 r | 토공 | 전체 공기 | 굴착기비 | 트럭비 | 기간 비례비 | 합 | 주공정 시작 |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (AlternativeResult a in r.Alternatives)
            {
                sb.AppendLine("| " + a.Input.Name + " | " + a.Input.Machines + "대 | " + a.Input.Trucks + "대 | "
                    + a.Input.HoursPerDay.ToString("0.#", Invariant) + " | " + a.Input.InterferenceRatio.ToString("0.##", Invariant) + " | "
                    + a.EarthworkDays + "일 | " + a.ProjectDays + "일 | " + KoreanMoney.Format(a.ExcavatorCost) + " | " + KoreanMoney.Format(a.TruckCost)
                    + " | " + KoreanMoney.Format(a.TimeCost) + " | " + KoreanMoney.Format(a.TotalCost) + " | " + AlternativeNarrative.CriticalStart(a.Schedule) + " |");
            }
            sb.AppendLine();
            sb.AppendLine("비용은 굴착기·덤프트럭 장비비와 기간 비례비만 더한 **비교용 일부 항목**이다 (인건비·재료비 제외).");
            sb.AppendLine();
            if (r.Alternatives.Count > 1)
            {
                sb.AppendLine("자동 문장:");
                sb.AppendLine();
                foreach (string line in Comparisons(r.Alternatives)) sb.AppendLine("- " + line);
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("저장한 대안이 없다 (메뉴 [대안] → 저장 A / B / C).");
                sb.AppendLine();
            }

            // 6. 가정과 출처
            sb.AppendLine("## 6. 가정과 출처");
            sb.AppendLine();
            foreach (string a in r.Assumptions) sb.AppendLine("- " + a);
            return sb.ToString();
        }

        /// <summary>
        /// 비교 문장들: 저장한 대안끼리 차례로(A → B → C), 그다음 각 대안 → 지금.
        /// 조건이 똑같은 두 대안의 비교는 할 말이 없으므로 뺀다.
        /// </summary>
        public static List<string> Comparisons(IReadOnlyList<AlternativeResult> results)
        {
            var lines = new List<string>();
            void Add(AlternativeResult a, AlternativeResult b)
            {
                if (AlternativeNarrative.ConditionChanges(a.Input, b.Input) == "조건이 같다") return;
                lines.Add(AlternativeNarrative.Compare(a, b));
            }
            for (int i = 2; i < results.Count; i++) Add(results[i - 1], results[i]);
            for (int i = 1; i < results.Count; i++) Add(results[i], results[0]);
            return lines;
        }

        /// <summary>대안 표 CSV (원 단위 숫자, 엑셀에서 계산할 수 있게). 첫 줄은 머리.</summary>
        public static string Csv(IEnumerable<AlternativeResult> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine("대안,굴착기(대),덤프트럭(대),하루 작업(시간),간섭 r,토공(일),전체 공기(일),굴착 일수,운반 일수,굴착기비(원),트럭비(원),기간 비례비(원),합(원),주공정 시작,다짐 조(조)");
            foreach (AlternativeResult a in results)
            {
                EarthworkPlan p = a.Earthwork;
                sb.AppendLine(string.Join(",",
                    a.Input.Name, a.Input.Machines.ToString(Invariant), a.Input.Trucks.ToString(Invariant),
                    a.Input.HoursPerDay.ToString("0.##", Invariant), a.Input.InterferenceRatio.ToString("0.##", Invariant),
                    a.EarthworkDays.ToString(Invariant), a.ProjectDays.ToString(Invariant),
                    p != null ? p.ExcavationDays.ToString("F3", Invariant) : "",
                    p != null && p.IncludesHaul ? p.HaulDays.ToString("F3", Invariant) : "",
                    a.ExcavatorCost.ToString("F0", Invariant), a.TruckCost.ToString("F0", Invariant),
                    a.TimeCost.ToString("F0", Invariant), a.TotalCost.ToString("F0", Invariant),
                    AlternativeNarrative.CriticalStart(a.Schedule),
                    a.EmbankmentCrews.ToString(Invariant)));
            }
            return sb.ToString();
        }

        /// <summary>
        /// 4절 뒤: 공정표의 기둥·슬래브 일수(가정)를 표준품셈 6-1-4·6-2-3·6-3-1로 검산한 표 (BuildingCycleCheck). 작업조 2·4·6조로 비교.
        /// </summary>
        private static void AppendCycleCheck(StringBuilder sb, BuildingPlan bp, BuildingSchedule s)
        {
            double colAssumed = double.NaN, slabAssumed = double.NaN, foundationAssumed = double.NaN;
            foreach (CpmActivity a in s.Activities)
            {
                if (double.IsNaN(foundationAssumed) && a.Name == "기초") foundationAssumed = a.Duration;
                if (double.IsNaN(colAssumed) && a.Name.EndsWith("기둥")) colAssumed = a.Duration;
                if (double.IsNaN(slabAssumed) && a.Name.EndsWith("슬래브")) slabAssumed = a.Duration;
            }
            var b = new BuildingCycleCheck(bp);
            sb.AppendLine("### 한 층 일수 검산 (표준품셈, 공기 계산에는 쓰지 않음)");
            sb.AppendLine();
            sb.AppendLine("한 층: 기둥 거푸집 " + N0(b.ColumnFormArea) + " m²·철근 " + b.ColumnRebarTons.ToString("0.0", Invariant) + " t·콘크리트 "
                + N1(bp.ColumnVolumePerFloor) + " m³, 슬래브 거푸집 " + N0(b.SlabFormArea) + " m²·철근 " + b.SlabRebarTons.ToString("0.0", Invariant)
                + " t·콘크리트 " + N1(bp.SlabVolumePerFloor) + " m³ (철근량 기둥 200·슬래브 100 kg/m³는 가정).");
            sb.AppendLine();
            sb.AppendLine("| 작업조 (거푸집 / 철근) | 인원 | 기초 (일) | 기둥 (일) | 슬래브 (일) | 한 층 (일) |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (int f in new[] { 2, 4, 6 })
            {
                var c = new BuildingCycleCheck(bp, f, Math.Max(1, f / 2));
                sb.AppendLine("| " + f + "조 / " + Math.Max(1, f / 2) + "조 | " + c.Workers + "명 | " + c.FoundationDays.ToString("0.0", Invariant) + " | "
                    + c.ColumnDays.ToString("0.0", Invariant) + " | "
                    + c.SlabDays.ToString("0.0", Invariant) + " | " + (c.ColumnDays + c.SlabDays).ToString("0.0", Invariant) + " |");
            }
            sb.AppendLine("| **공정표 가정** | - | " + (double.IsNaN(foundationAssumed) ? "-" : D(foundationAssumed)) + " | " + D(colAssumed) + " | " + D(slabAssumed) + " | " + D(colAssumed + slabAssumed) + " |");
            sb.AppendLine();
            sb.AppendLine("식: 철근 ÷ (6-2-3 건축 조당 t: 기둥 Type-Ⅰ 3.4, 슬래브 Type-Ⅱ 3.0) + 거푸집 ÷ (6-3-1 조당 m²: 기둥 복잡 30, 슬래브 보통 45) "
                + "+ 올림(콘크리트 ÷ 6-1-4 펌프차 140 m³/일). 매트기초는 철근 100 kg/m³(가정)·옆면 거푸집 간단 50 m²·펌프차 f1 1.4 (196 m³/일). 동바리·양생 기간·층간 겹침은 넣지 않았다.");
            sb.AppendLine();
        }

        private static string N0(double v) => v.ToString("N0", Invariant);
        private static string N1(double v) => v.ToString("N1", Invariant);
        private static string N2(double v) => v.ToString("N2", Invariant);
        private static string Vol(double v) => v.ToString("N0", Invariant) + " m³";
        private static string D(double v) => v.ToString("0.#", Invariant);
    }
}
