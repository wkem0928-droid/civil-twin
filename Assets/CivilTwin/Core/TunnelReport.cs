using System;
using System.Globalization;
using System.Text;

namespace CivilTwin.Core
{
    /// <summary>
    /// 터널 굴착 계산 보고서 (2026-11-21, Markdown). 화면의 터널 글상자에 나온 숫자가 어떤 식과 값에서 왔는지 한 문서로 남긴다:
    /// 터널 제원 → 지보 패턴과 1발파 싸이클 → P-3 싸이클 내역 → 막장 공정 → 대안 → 지보재 물량 → 2차 라이닝 → 버력 운반 → 가정과 출처.
    /// </summary>
    public static class TunnelReport
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        /// <param name="plan">지금 조건의 공정</param>
        /// <param name="withoutMiddle">사갱 없는 대안 (없으면 null)</param>
        /// <param name="twoLoaders">로더 2대 대안 (없으면 null)</param>
        /// <param name="surface">주면 5절에 조건 조합표(하루 시간 × 로더 × 사갱)를 더한다 (공정을 다시 계산하므로 지형이 필요하다)</param>
        public static string Markdown(TunnelPlan plan, TunnelPlan withoutMiddle, TunnelPlan twoLoaders, string generatedAt,
            ElevationGrid surface = null)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            TunnelSpec spec = plan.Spec;
            TunnelSchedule s = plan.Schedule;
            TunnelAlignment a = plan.Alignment;
            var sb = new StringBuilder();

            sb.AppendLine("# 터널 굴착 계산 보고서 — " + spec.Title);
            sb.AppendLine();
            sb.AppendLine("- 만든 시각: " + generatedAt);
            sb.AppendLine("- 만든 프로그램: CIVIL TWIN (3D·4D 시공계획 시뮬레이터)");
            sb.AppendLine("- 숫자는 모두 계산 결과다. 표준품셈에서 가져온 값과 이 프로그램의 가정은 9절에 나눠 적었다.");
            sb.AppendLine();

            // 1. 터널
            double maxCover = plan.MaxCover(out double maxAt);
            sb.AppendLine("## 1. 터널");
            sb.AppendLine();
            sb.AppendLine("| 항목 | 값 |");
            sb.AppendLine("|---|---|");
            sb.AppendLine("| 시공 | " + spec.Consortium + " |");
            sb.AppendLine("| 길이 | 공식 " + N0(spec.OfficialLength) + " m, 계산 선형 " + N1(a.Length) + " m × 튜브 " + spec.Tubes + "개 (" + spec.LanesPerTube + "차로) |");
            sb.AppendLine("| 굴착 단면 | " + N0(plan.Section.Area) + " m² (+ 여굴 " + plan.Section.OverbreakArea.ToString("0.0", Invariant) + " m²) |");
            sb.AppendLine("| 입구 노면 | 서 " + N1(a.StartRoadElevation) + " m, 동 " + N1(a.EndRoadElevation) + " m → 경사 "
                + (a.Grade < 0 ? "내리막 " : "오르막 ") + Math.Abs(a.Grade * 100.0).ToString("0.00", Invariant) + " % (서 → 동)" + (spec.ReportedGradePercent > 0 ? " (보도 " + spec.ReportedGradePercent.ToString("0.00", Invariant) + " %)" : "") + " |");
            sb.AppendLine("| 토피 최대 | " + N0(maxCover) + " m (측점 " + N0(maxAt) + " m)" + (string.IsNullOrEmpty(spec.ReportedCover) ? "" : ", 보도 " + spec.ReportedCover) + " |");
            sb.AppendLine("| 설계 단면 굴착량 | " + N0(plan.DesignVolume) + " m³ (자연 상태, 튜브 모두)" + (spec.ReportedMuck > 0 ? ", 보도 반출 토석 " + N0(spec.ReportedMuck) + " m³" : "") + " |");
            sb.AppendLine();

            // 2. 패턴과 싸이클
            sb.AppendLine("## 2. 지보 패턴과 1발파 싸이클 (하루 " + plan.HoursPerDay.ToString("0", Invariant) + "시간, 로더 " + plan.Crew.Loaders + "대)");
            sb.AppendLine();
            sb.AppendLine("| 패턴 | 굴진장 (m) | 숏크리트 (cm) | 록볼트 | 강지보 | 길이 비율 | 싸이클 (분) | 막장 하루 굴진 (m) |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");
            for (int k = 0; k < plan.Cycles.Length; k++)
            {
                NatmCycle c = plan.Cycles[k];
                SupportPattern p = c.Pattern;
                sb.AppendLine("| " + p.Name + " | " + p.Advance.ToString("0.0", Invariant) + (p.IsBench ? " / " + p.BenchAdvance.ToString("0.0", Invariant) + " (상/하반)" : "")
                    + " | " + (p.Shotcrete * 100.0).ToString("0", Invariant)
                    + (p.SteelFiber ? " (강섬유)" : " (일반)") + " | " + (p.BoltSpacing > 0 ? "L " + p.BoltLength.ToString("0", Invariant) + " m @ " + p.BoltSpacing.ToString("0.0", Invariant) + " m" : "랜덤 L " + p.BoltLength.ToString("0", Invariant) + " m")
                    + " (" + c.BoltsPerMeter.ToString("0.0", Invariant) + "개/m) | " + (p.SteelRib ? "있음" : "-") + " | " + (plan.PatternShare(k) * 100.0).ToString("0.0", Invariant)
                    + " % | " + c.TotalMinutes.ToString("0.0", Invariant) + (p.IsBench ? " + 하반 " + c.BenchMinutes.ToString("0.0", Invariant) : "")
                    + " | " + c.AdvancePerDay(plan.HoursPerDay).ToString("0.00", Invariant) + " |");
            }
            sb.AppendLine();
            sb.AppendLine("막장 평균 하루 굴진 = 길이 ÷ Σ(패턴 구간 길이 ÷ 그 패턴의 하루 굴진) = **" + plan.AverageFaceAdvance.ToString("0.00", Invariant) + " m/일**.");
            sb.AppendLine();
            sb.AppendLine("상하반 분할 굴착(P-4, P-5): 3-2-1 주 ⑤에 따라 하반 싸이클은 비고(하반) 열 — 천공준비·측량·장약·부석제거·지보설치·잔재제거 65 %, "
                + "환기·버력 준비·장비점검·이동 100 %, T1 ~ T6은 하반 물량으로 계산. 같은 조가 번갈아 한다고 보고 1 m당 시간 = 상반 싸이클 ÷ 상반 굴진장 + 하반 싸이클 ÷ 하반 굴진장.");
            sb.AppendLine();

            // 3. P-3 싸이클 내역
            NatmCycle c3 = plan.Cycles[2];
            sb.AppendLine("## 3. " + c3.Pattern.Name + " 1발파 싸이클 내역 (표준품셈 토목 3-2-1 C군)");
            sb.AppendLine();
            sb.AppendLine("| 구분 | 작업 | 분 | 근거 |");
            sb.AppendLine("|---|---|---|---|");
            foreach (CycleItem item in c3.Items)
            {
                sb.AppendLine("| " + item.Group + " | " + item.Name + " | " + item.Minutes.ToString("0.0", Invariant) + " | " + item.Source + " |");
            }
            sb.AppendLine("| | **합** | **" + c3.TotalMinutes.ToString("0.0", Invariant) + "** | |");
            sb.AppendLine();

            // 4. 막장 공정
            sb.AppendLine("## 4. 막장 공정");
            sb.AppendLine();
            sb.AppendLine("| 막장 | 시작 측점 (m) | 방향 | 시작일 | 만난 곳 (m) | 판 길이 (m) | 판 날 수 |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            for (int i = 0; i < s.FaceCount; i++)
            {
                TunnelFace f = s.Face(i);
                double end = s.FacePosition(i, s.FinishDay);
                sb.AppendLine("| " + f.Name + " | " + N0(f.Start) + " | " + (f.Direction > 0 ? "동쪽으로" : "서쪽으로") + " | " + N0(f.StartDay) + "일째 | "
                    + N0(end) + " | " + N0(Math.Abs(end - f.Start)) + " | " + N0(s.WorkingDays(i)) + "일 |");
            }
            sb.AppendLine();
            sb.AppendLine("관통 **" + N0(s.FinishDay) + "일**, 두 튜브 하루 평균 " + plan.CombinedDailyAdvance.ToString("0.0", Invariant) + " m.");
            int shifts = TunnelCrew.Shifts(plan.HoursPerDay);
            sb.AppendLine("작업 인원 (표준품셈 3-2-5 C군, 막장 하나에 " + N0(TunnelCrew.PerFace) + "인: 작업반장·점보드릴·고소대차·로더·굴착기·숏크리트머신 운전원 각 1, "
                + "보통인부 3, 특별인부 4, 화약취급공 1): 하루 " + plan.HoursPerDay.ToString("0", Invariant) + "시간 = " + shifts + "교대(8시간, 가정) → "
                + "막장 " + s.FaceCount + "개가 모두 팔 때 " + N0(s.FaceCount * spec.Tubes * TunnelCrew.PerFace * shifts) + "명, 굴착 연인원 **"
                + N0(TunnelCrew.LaborDays(s, spec.Tubes, plan.HoursPerDay)) + "인·일** (Σ 판 날 수 × 튜브 " + spec.Tubes + " × 14 × " + plan.HoursPerDay.ToString("0", Invariant) + " ÷ 8). "
                + "3-2-5 비고: 병렬 두 막장을 한 작업조가 함께 굴착하면 59 %. 버력 트럭 기사·설비·관리 인원은 넣지 않았다.");
            if (spec.ExcavationStartKnown)
            {
                double diff = s.FinishDay / spec.ActualDays - 1.0;
                sb.AppendLine("실제: " + spec.ExcavationStart.ToString("yyyy.M", Invariant) + " 굴착 시작 ~ " + spec.Breakthrough.ToString("yyyy.M.d", Invariant)
                    + " 관통 = " + N0(spec.ActualDays) + "일" + (spec.ReportedDailyAdvance > 0 ? ", 하루 약 " + N0(spec.ReportedDailyAdvance) + " m" : "")
                    + " → 계산이 " + (diff >= 0 ? "+" : "−") + Math.Abs(diff * 100.0).ToString("0.0", Invariant) + " %.");
            }
            else
            {
                sb.AppendLine(spec.BreakthroughKnown
                    ? "실제: 공사 " + spec.ExcavationStart.ToString("yyyy.M", Invariant) + " 착공 ~ " + spec.Breakthrough.ToString("yyyy.M", Invariant)
                      + " 관통 보도 (" + N0(spec.ActualDays) + "일). 굴착 시작일이 공개되지 않아 굴착 기간과 직접 비교하지 않는다."
                    : "실제: 공사 " + spec.ExcavationStart.ToString("yyyy.M", Invariant) + " 착공 ~ " + spec.Opening.ToString("yyyy.M.d", Invariant)
                      + " 개통만 확인 (" + N0((spec.Opening - spec.ExcavationStart).TotalDays) + "일). 굴착 시작일·관통일이 공개되지 않아 비교하지 않는다.");
            }
            sb.AppendLine();

            // 누적 굴진 (100일 간격): 공정 곡선 대신 표로
            sb.AppendLine("| 날짜 | 튜브마다 판 길이 (m) | 공정률 |");
            sb.AppendLine("|---|---|---|");
            for (double day = 0.0; day < s.FinishDay; day += 100.0)
            {
                double dug = s.ExcavatedLength(day);
                sb.AppendLine("| " + N0(day) + "일 | " + N0(dug) + " | " + (dug / s.Length * 100.0).ToString("0.0", Invariant) + " % |");
            }
            sb.AppendLine("| " + N0(s.FinishDay) + "일 (관통) | " + N0(s.Length) + " | 100.0 % |");
            sb.AppendLine();

            // 5. 대안
            sb.AppendLine("## 5. 대안");
            sb.AppendLine();
            sb.AppendLine("| 대안 | 관통 (일) | 차이 |");
            sb.AppendLine("|---|---|---|");
            sb.AppendLine("| 지금 (" + (plan.UseMiddleAccess ? "막장 4개" : "막장 2개") + ", 로더 " + plan.Crew.Loaders + "대) | " + N0(s.FinishDay) + " | - |");
            if (withoutMiddle != null) AppendAlternative(sb, "사갱 없음 (막장 2개)", withoutMiddle, s.FinishDay);
            if (twoLoaders != null) AppendAlternative(sb, "로더 2대", twoLoaders, s.FinishDay);
            if (surface != null && plan.Crew.WaterIncrease < 0.3 - 1e-9)
            {
                // 위험 시나리오: 용수가 전 구간에서 굴착을 방해하면 (3-2-1 주 ⑦ 최대 30 %)
                AppendAlternative(sb, "용수 많음 (싸이클 +30 %, 주 ⑦)",
                    new TunnelPlan(spec, surface, plan.HoursPerDay, plan.Crew.Loaders, plan.UseMiddleAccess, 0.3), s.FinishDay);
            }
            sb.AppendLine();
            if (surface != null) sb.Append(ConditionMatrix(spec, surface));

            sb.Append(SupportSection(plan));
            sb.Append(LiningSection(plan, spec));

            // 8. 버력 운반
            sb.AppendLine("## 8. 버력 운반 (입구 막장, 표준품셈 8-2-8)");
            sb.AppendLine();
            sb.AppendLine("트럭 1대 " + TunnelMuckHaul.TruckLooseVolume(plan.Crew).ToString("0.00", Invariant) + " m³(흐트러진 상태), 로더 "
                + TunnelMuckHaul.LoaderCycles(plan.Crew).ToString("0.00", Invariant) + "회 = 적재 " + TunnelMuckHaul.LoadMinutes(plan.Crew).ToString("0.00", Invariant)
                + "분. 로더가 기다리지 않으려면:");
            sb.AppendLine();
            sb.AppendLine("| 막장 깊이 | 트럭 싸이클 (분) | 필요한 트럭 |");
            sb.AppendLine("|---|---|---|");
            for (double depth = 0.0; depth <= 5000.0 + 1e-9; depth += 1000.0)
            {
                sb.AppendLine("| " + N0(depth) + " m | " + TunnelMuckHaul.CycleMinutes(plan.Crew, depth).ToString("0.0", Invariant) + " | "
                    + TunnelMuckHaul.TrucksNeeded(plan.Crew, depth) + "대 |");
            }
            sb.AppendLine();

            // 9. 가정과 출처
            sb.AppendLine("## 9. 가정과 출처");
            sb.AppendLine();
            sb.AppendLine("- 터널 자료: " + spec.Sources);
            sb.AppendLine("- 1발파 싸이클: 2025년 적용 표준품셈 [토목부문] 3-2-1 C군(고정 시간은 범위의 가운데), 3-2-3 천공속도, 3-2-4 장비 조합, 3-1-2 여굴, [공통부문] 8-2-5 로더");
            sb.AppendLine("- 지보 패턴·실제 시공 비율·굴착단면적 90 m²: 한국도로공사 박권제·설운호 「신개념 고속도로 터널공법(Ex-TM) 소개」 표 1, 그림 3, 그림 1");
            sb.AppendLine("- 가정: 발파공 1.6공/m², 발파 효율 90 %, 뿜어붙이기 18 m³/시간·반발 15 %, 랜덤 록볼트 4개, 단면 둘레(아치 20·측벽 8·바닥 12 m), 상반 면적 60 %(분할 굴착), 붐이 록볼트를 나눠 함");
            sb.AppendLine("- 가정: 패턴 위치는 토피가 얕을수록 나쁜 패턴 (지질 자료 없음), 하루 " + plan.HoursPerDay.ToString("0", Invariant) + "시간 작업, 용수·고장·중단 없음");
            sb.AppendLine("- 가정: 버력 트럭 속도 적재 15·공차 20 km/h (8-2-8 미포장도로), 적하 0.8분, 사토장 1 km, 화강암 2.65 t/m³");
            sb.AppendLine("- 가정: 지보재 물량은 설계 두께·간격으로 (여굴 채움·인버트·2차 라이닝 제외), 강지보는 1발파마다 하나");
            sb.AppendLine("- 2차 라이닝: 표준품셈 [토목부문] 3-3-1 철재거푸집(회당 1일, 형틀목공 6·콘크리트공 2·특별인부 1·보통인부 2), 3-4-1 방수(m²당 방수공 0.011·보통인부 0.002, 시트 1.15 m²), 3-4-2 작업대차 조립 4일");
            sb.AppendLine("- 가정: 거푸집 1 span 12 m, 라이닝 두께 30 cm, 라이닝 둘레 = 숏크리트 둘레, 관통 뒤 시작 (실제로는 굴착 중에 시작하기도 한다), 방수는 라이닝보다 앞서 끝남");
            sb.AppendLine("- 지형: Copernicus DEM GLO-30 (약 30 m 칸, DSM)");
            return sb.ToString();
        }

        /// <summary>
        /// 6절 지보재 물량: 패턴별 1 m당 물량 × 패턴 길이 → 터널 전체 (튜브 모두). 식은 TunnelSupportQuantity 설명.
        /// </summary>
        public static string SupportSection(TunnelPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            var sb = new StringBuilder();
            int t = plan.Spec.Tubes;
            sb.AppendLine("## 6. 지보재 물량 (설계 두께·간격, 튜브 " + t + "개)");
            sb.AppendLine();
            sb.AppendLine("| 패턴 | 길이 (m, 튜브마다) | 숏크리트 (m³/m) | 록볼트 (개/m) | 강지보 (개/m) | 숏크리트 (m³) | 록볼트 (개) | 강지보 (개) |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");
            for (int k = 0; k < plan.Cycles.Length; k++)
            {
                SupportPerMeter q = TunnelSupportQuantity.PerMeter(plan, k);
                double len = TunnelSupportQuantity.PatternLength(plan, k);
                sb.AppendLine("| " + plan.Cycles[k].Pattern.Name + " | " + N0(len) + " | " + q.Shotcrete.ToString("0.00", Invariant) + " | "
                    + q.Bolts.ToString("0.00", Invariant) + " | " + q.SteelRibs.ToString("0.000", Invariant) + " | " + N0(q.Shotcrete * len * t) + " | "
                    + N0(q.Bolts * len * t) + " | " + N0(q.SteelRibs * len * t) + " |");
            }
            SupportTotals all = TunnelSupportQuantity.Total(plan);
            sb.AppendLine("| **합** | " + N0(all.Length / t) + " | | | | **" + N0(all.Shotcrete) + "** | **" + N0(all.Bolts) + "** | **" + N0(all.SteelRibs) + "** |");
            sb.AppendLine();
            sb.AppendLine("숏크리트 설계량 = 둘레(아치 + 측벽 " + N0(plan.Section.LiningPerimeter) + " m) × 두께, 반발 "
                + (plan.Crew.ShotcreteLoss * 100.0).ToString("0", Invariant) + " %를 더한 타설량 **" + N0(all.ShotcretePlaced) + " m³**. "
                + "록볼트 = 둘레 ÷ 간격² (랜덤은 1발파 개수 ÷ 굴진장), 길이 합 " + N0(all.BoltLength) + " m. 강지보 = 1 ÷ 굴진장 (가정).");
            sb.AppendLine();
            return sb.ToString();
        }

        /// <summary>7절 2차 라이닝과 방수: 거푸집 1·2·4조별 기간, 물량, 인력. 식은 TunnelLining 설명.</summary>
        public static string LiningSection(TunnelPlan plan, TunnelSpec spec)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            var sb = new StringBuilder();
            TunnelLining l = TunnelLining.For(plan);
            sb.AppendLine("## 7. 2차 라이닝과 방수 (표준품셈 토목 3-3-1, 3-4-1, 3-4-2)");
            sb.AppendLine();
            sb.AppendLine("관통(" + N0(plan.Schedule.FinishDay) + "일) 뒤 방수 작업대차 조립 " + N0(TunnelLining.CarriageAssemblyDays)
                + "일 → 철재거푸집 1 span(" + N0(l.SpanLength) + " m, 가정)을 하루에 하나씩 친다 (설치·타설·양생·해체·이동 1일).");
            sb.AppendLine();
            sb.AppendLine("| 거푸집 (튜브마다) | 한 조가 맡는 길이 (m) | span 수 | 라이닝 기간 (일) | 끝나는 날 |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (int forms in new[] { 1, 2, 4 })
            {
                TunnelLining x = TunnelLining.For(plan, forms);
                string date = spec.ExcavationStartKnown ? " (" + spec.ExcavationStart.AddDays(x.FinishDay).ToString("yyyy.M", Invariant) + ")" : "";
                sb.AppendLine("| " + forms + "조" + (forms == 2 ? " (기본)" : "") + " | " + N0(x.SegmentLength) + " | " + x.SpansPerForm + " | "
                    + N0(x.Duration) + " | " + N0(x.FinishDay) + "일" + date + " |");
            }
            foreach (int forms in new[] { 2, 4 })
            {
                TunnelLining x = TunnelLining.Concurrently(plan, forms);
                string date = spec.ExcavationStartKnown ? " (" + spec.ExcavationStart.AddDays(x.FinishDay).ToString("yyyy.M", Invariant) + ")" : "";
                sb.AppendLine("| " + forms + "조, 굴착과 병행 (막장 " + N0(x.Lag) + " m 뒤) | " + N0(x.SegmentLength) + " | " + x.SpansPerForm + " | "
                    + N0(x.Duration) + " (" + N0(x.StartDay) + "일째부터) | " + N0(x.FinishDay) + "일" + date + " |");
            }
            sb.AppendLine();
            sb.AppendLine("병행: 강재 거푸집 아래로 버력 트럭이 지나갈 수 있어 막장 뒤에서 라이닝을 함께 치는 방법. 거푸집은 다음 span 끝에서 "
                + "500 m(가정) 앞까지 파여 있을 때만 치고, 관통 뒤에는 제한 없이 친다. 갱내 운반·환기 간섭으로 굴진이 느려지는 것은 넣지 않았다.");
            sb.AppendLine();
            if (spec.ExcavationStartKnown && spec.Opening > spec.ExcavationStart)
            {
                sb.AppendLine("실제 개통 " + spec.Opening.ToString("yyyy.M.d", Invariant) + " (굴착 시작 뒤 " + N0((spec.Opening - spec.ExcavationStart).TotalDays)
                    + "일). 개통까지에는 포장·설비·도로 전체 공사가 들어 있어 라이닝 기간과 직접 비교하지 않는다 (라이닝이 개통 전에 끝나는지만 본다).");
                sb.AppendLine();
            }
            sb.AppendLine("| 항목 | 값 | 식 |");
            sb.AppendLine("|---|---|---|");
            sb.AppendLine("| 라이닝 콘크리트 | " + N0(l.Concrete) + " m³ | 둘레 " + N0(l.Perimeter) + " m × 두께 " + l.Thickness.ToString("0.00", Invariant) + " m × 길이 × 튜브 " + l.Tubes + "개 |");
            sb.AppendLine("| 1회 타설량 | " + N0(l.ConcretePerPour) + " m³ | 둘레 × 두께 × " + N0(l.SpanLength) + " m (펌프차 1대, 3-3-1) |");
            sb.AppendLine("| 타설 횟수 | " + N0(l.PoursPerTube * l.Tubes) + "회 | 거푸집 수 × span 수 × 튜브 수 |");
            sb.AppendLine("| 라이닝 인력 | " + N0(l.LiningLabor) + "인 | 회당 11인 (형틀목공 6, 콘크리트공 2, 특별인부 1, 보통인부 2) |");
            sb.AppendLine("| 방수 면적 | " + N0(l.WaterproofArea) + " m² (시트 " + N0(l.WaterproofSheet) + " m²) | 둘레 × 길이 × 튜브 수, 시트 × 1.15 |");
            sb.AppendLine("| 방수 인력 | " + N0(l.WaterproofLabor) + "인 | 면적 × (0.011 + 0.002) |");
            sb.AppendLine("| 라이닝을 따라가는 방수공 | 하루 " + l.WaterproofWorkersPerDay.ToString("0.0", Invariant) + "명 | 거푸집 " + l.FormsPerTube * l.Tubes + "조 × " + N0(l.SpanLength) + " m × 둘레 × 0.011 |");
            sb.AppendLine();
            return sb.ToString();
        }

        /// <summary>
        /// 조건 조합표: 하루 작업 시간 16 · 20 · 24시간 × 로더 1 · 2대 × (사갱 있음 · 없음)의 관통일.
        /// 시공 대안 검토에서 "무엇을 바꾸면 얼마나 줄어드나"를 한눈에 본다.
        /// </summary>
        public static string ConditionMatrix(TunnelSpec spec, ElevationGrid surface)
        {
            var sb = new StringBuilder();
            bool middle = spec.HasMiddleAccess;
            sb.AppendLine("조건 조합 (관통 일수):");
            sb.AppendLine();
            sb.AppendLine(middle ? "| 하루 작업 | 로더 | 막장 4개 (사갱) | 막장 2개 |" : "| 하루 작업 | 로더 | 막장 2개 |");
            sb.AppendLine(middle ? "|---|---|---|---|" : "|---|---|---|");
            foreach (double hours in new[] { 16.0, 20.0, 24.0 })
            {
                foreach (int loaders in new[] { 1, 2 })
                {
                    var two = new TunnelPlan(spec, surface, hours, loaders, false);
                    string row = "| " + hours.ToString("0", Invariant) + "시간 | " + loaders + "대 | ";
                    if (middle) row += N0(new TunnelPlan(spec, surface, hours, loaders, true).Schedule.FinishDay) + " | ";
                    sb.AppendLine(row + N0(two.Schedule.FinishDay) + " |");
                }
            }
            sb.AppendLine();
            return sb.ToString();
        }

        private static void AppendAlternative(StringBuilder sb, string name, TunnelPlan alt, double baseDays)
        {
            double d = alt.Schedule.FinishDay - baseDays;
            sb.AppendLine("| " + name + " | " + N0(alt.Schedule.FinishDay) + " | " + (d >= 0 ? "+" : "−") + N0(Math.Abs(d)) + "일 |");
        }

        private static string N0(double v) => v.ToString("N0", Invariant);
        private static string N1(double v) => v.ToString("N1", Invariant);
    }
}
