using System;

namespace CivilTwin.Core
{
    /// <summary>
    /// 건물 한 층의 기둥·슬래브 일수를 표준품셈으로 검산한다 (2026-11-21). 공정표는 가정값(기둥 3일·슬래브 5일)을 그대로 쓰고,
    /// 이 계산은 "그 가정이 몇 개 작업조에 해당하는가"를 보인다 (공기 계산을 바꾸지 않는다).
    ///
    /// 출처 — 2025년 적용 표준품셈 [공통부문] 6장 철근콘크리트공사 (원문 글자 123 ~ 127쪽):
    ///   6-3-1 합판거푸집 설치 및 해체 (일당, 형틀목공 5 + 보통인부 2): 복잡 30 m² (건축 라멘구조의 보·기둥), 보통 45 m² (건축 슬래브)
    ///   6-2-3 철근 현장조립 건축 (일당, 철근공 6 + 보통인부 2): Type-Ⅰ 3.4 t (지름 13 mm 이하 철근이 50 % 미만 — 기둥으로 봄),
    ///         Type-Ⅱ 3.0 t (13 mm 이하가 50 % 이상 — 슬래브로 봄)
    ///   6-1-4 콘크리트 펌프차 타설 (일당): 기준시공량 철근구조물 8 ~ 12 cm 125, 15 cm 130, 18 cm 이상 140 m³
    ///         × f1(Type-Ⅱ 벽·기둥·보·슬래브 1.0) × f2(현장조건 Type-Ⅱ 1.0) = 140 m³
    ///
    /// 식 (한 층, 순서대로: 철근 → 거푸집 → 타설):
    ///   기둥 거푸집 = 기둥 수 × 4 × 한 변 × 순높이,  슬래브 거푸집 = 바닥 면적 + 둘레 × 두께
    ///   철근 = 콘크리트 부피 × 철근량(가정: 기둥 200 kg/m³, 슬래브 100 kg/m³ — 출처 미확인)
    ///   일수 = 철근 ÷ (조당 t × 철근 조) + 거푸집 ÷ (조당 m² × 거푸집 조) + 올림(콘크리트 ÷ 140)  (펌프차는 하루 한 번 셋팅)
    /// 손계산 (30 × 20 m, 기둥 6 × 5 = 30개 0.5 m, 순높이 3.25 m, 슬래브 0.25 m, 거푸집 4조, 철근 2조):
    ///   기둥: 철근 24.375 m³ × 0.2 = 4.875 t ÷ 6.8 = 0.717 + 거푸집 195 m² ÷ 120 = 1.625 + 타설 1 = 3.34일
    ///   슬래브: 철근 150 m³ × 0.1 = 15 t ÷ 6.0 = 2.5 + 거푸집 625 m² ÷ 180 = 3.472 + 타설 올림(150 ÷ 140) = 2 → 7.97일
    /// 한계: 보·벽·계단이 없는 모형, 동바리·양생 기간·층간 겹침 작업은 넣지 않았다.
    /// </summary>
    public sealed class BuildingCycleCheck
    {
        public const double FormComplexPerCrew = 30.0, FormNormalPerCrew = 45.0;     // 6-3-1
        public const double RebarTypeIPerCrew = 3.4, RebarTypeIIPerCrew = 3.0;     // 6-2-3 건축
        public const double PumpPerDay = 140.0;                                    // 6-1-4 철근구조물 18 cm 이상 × f1 1.0 × f2 1.0
        public const double FormSimplePerCrew = 50.0;                              // 6-3-1 간단 (기초 등 간단한 구조)
        public const double MatPumpPerDay = 140.0 * 1.4;                          // 6-1-4 f1 Type-Ⅰ (매트기초) 1.4 → 196 m³/일
        /// <summary>기초 철근량 (kg/m³) — 가정, 출처 미확인</summary>
        public const double FoundationRebarKgPerM3 = 100.0;

        public int FormworkCrews { get; }
        public int RebarCrews { get; }
        public double ColumnRebarKgPerM3 { get; }
        public double SlabRebarKgPerM3 { get; }

        public double ColumnFormArea { get; }
        public double SlabFormArea { get; }
        public double ColumnRebarTons { get; }
        public double SlabRebarTons { get; }
        public double ColumnDays { get; }
        public double SlabDays { get; }
        /// <summary>
        /// 매트기초 일수 = 철근(100 kg/m³, Type-Ⅰ 3.4 t) + 옆면 거푸집(둘레 × 두께, 간단 50 m²) + 올림(부피 ÷ 196).
        /// 손계산 (30 × 20 × 0.8 m = 480 m³, 거푸집 4조·철근 2조): 48 t ÷ 6.8 = 7.06 + 80 m² ÷ 200 = 0.4 + 올림(2.45) = 3 → 10.46일
        /// </summary>
        public double FoundationDays { get; }
        public double FoundationRebarTons { get; }

        public BuildingCycleCheck(BuildingPlan plan, int formworkCrews = 4, int rebarCrews = 2,
            double columnRebarKgPerM3 = 200.0, double slabRebarKgPerM3 = 100.0)
        {
            if (formworkCrews < 1 || rebarCrews < 1) throw new ArgumentOutOfRangeException(nameof(formworkCrews));
            FormworkCrews = formworkCrews;
            RebarCrews = rebarCrews;
            ColumnRebarKgPerM3 = columnRebarKgPerM3;
            SlabRebarKgPerM3 = slabRebarKgPerM3;

            ColumnFormArea = plan.ColumnsPerFloor * 4.0 * plan.ColumnSize * plan.ColumnClearHeight;
            SlabFormArea = plan.FloorArea + 2.0 * (plan.WidthEastWest + plan.LengthNorthSouth) * plan.SlabThickness;
            ColumnRebarTons = plan.ColumnVolumePerFloor * columnRebarKgPerM3 / 1000.0;
            SlabRebarTons = plan.SlabVolumePerFloor * slabRebarKgPerM3 / 1000.0;

            ColumnDays = ColumnRebarTons / (RebarTypeIPerCrew * rebarCrews) + ColumnFormArea / (FormComplexPerCrew * formworkCrews)
                         + PourDays(plan.ColumnVolumePerFloor);
            SlabDays = SlabRebarTons / (RebarTypeIIPerCrew * rebarCrews) + SlabFormArea / (FormNormalPerCrew * formworkCrews)
                       + PourDays(plan.SlabVolumePerFloor);

            FoundationRebarTons = plan.FoundationVolume * FoundationRebarKgPerM3 / 1000.0;
            double edge = 2.0 * (plan.WidthEastWest + plan.LengthNorthSouth) * plan.FoundationThickness;
            FoundationDays = FoundationRebarTons / (RebarTypeIPerCrew * rebarCrews) + edge / (FormSimplePerCrew * formworkCrews)
                             + (plan.FoundationVolume > 0 ? Math.Ceiling(plan.FoundationVolume / MatPumpPerDay - 1e-9) : 0.0);
        }

        /// <summary>펌프차 타설 일수 = 올림(부피 ÷ 140) — 하루 한 번 셋팅이 기준 (6-1-4 1. 나.)</summary>
        public static double PourDays(double volume) => volume > 0 ? Math.Ceiling(volume / PumpPerDay - 1e-9) : 0.0;

        /// <summary>한 층 인원 (명): 거푸집 조 × 7 + 철근 조 × 8</summary>
        public int Workers => FormworkCrews * 7 + RebarCrews * 8;
    }
}
