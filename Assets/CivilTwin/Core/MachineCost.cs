using System;

namespace CivilTwin.Core
{
    /// <summary>건설기계 한 종류의 기계경비 자료 (「건설기계의 기계경비 산출표」 한 줄).</summary>
    public readonly struct MachineCostItem
    {
        /// <summary>시간당 손료 (원/시간)</summary>
        public readonly double OwnershipPerHour;

        /// <summary>주연료 (경유 L/시간)</summary>
        public readonly double FuelLitersPerHour;

        /// <summary>잡재료 = 주연료비의 이 비율 (0.22 = 22 %)</summary>
        public readonly double MiscRatio;

        /// <summary>조종원 (인/일)</summary>
        public readonly double OperatorsPerDay;

        public MachineCostItem(double ownershipPerHour, double fuelLitersPerHour, double miscRatio, double operatorsPerDay)
        {
            if (!(ownershipPerHour >= 0)) throw new ArgumentOutOfRangeException(nameof(ownershipPerHour));
            if (!(fuelLitersPerHour >= 0)) throw new ArgumentOutOfRangeException(nameof(fuelLitersPerHour));
            if (!(miscRatio >= 0)) throw new ArgumentOutOfRangeException(nameof(miscRatio));
            if (!(operatorsPerDay >= 0)) throw new ArgumentOutOfRangeException(nameof(operatorsPerDay));
            OwnershipPerHour = ownershipPerHour;
            FuelLitersPerHour = fuelLitersPerHour;
            MiscRatio = miscRatio;
            OperatorsPerDay = operatorsPerDay;
        }

        /// <summary>
        /// 굴삭기(무한궤도) 1.00 m³, 분류번호 0201-0100: 손료 27,901원/시간, 경유 19.5 L/시간, 잡재료 22 %, 조종원 1인.
        /// 출처: 2024년 건설기계의 기계경비 산출표 4쪽 (www.cmpi.or.kr, 대한건설협회 월간 거래가격). 확인일 2026-11-20.
        /// </summary>
        public static MachineCostItem Excavator1m3_2024 => new MachineCostItem(27901.0, 19.5, 0.22, 1.0);

        /// <summary>
        /// 덤프트럭 15.0 ton, 분류번호 0602-0150: 손료 19,631원/시간, 경유 15.9 L/시간, 잡재료 38 %, 조종원 1인.
        /// 출처: 같은 산출표 7쪽.
        /// </summary>
        public static MachineCostItem DumpTruck15t_2024 => new MachineCostItem(19631.0, 15.9, 0.38, 1.0);

        // ---- 흙쌓기 조 (표준품셈 3-4-1, 2026-11-20 추가). 출처: 같은 산출표 (www.cmpi.or.kr, 2024년도 건설기계 경비산출표) ----

        /// <summary>진동롤러(자주식) 10.0 ton, 1306-0100: 손료 25,460원/시간, 경유 14.4 L/시간, 잡재료 30 %, 조종원 1인 (8쪽)</summary>
        public static MachineCostItem VibratoryRoller10t_2024 => new MachineCostItem(25460.0, 14.4, 0.30, 1.0);

        /// <summary>모터그레이더(일반용) 3.6 m, 0502-0036: 손료 43,769원/시간, 경유 16.2 L/시간, 잡재료 39 %, 조종원 1인 (6쪽)</summary>
        public static MachineCostItem MotorGrader36m_2024 => new MachineCostItem(43769.0, 16.2, 0.39, 1.0);

        /// <summary>굴삭기(무한궤도) 0.60 m³, 0201-0060: 손료 21,957원/시간, 경유 10.2 L/시간, 잡재료 22 %, 조종원 1인 (4쪽)</summary>
        public static MachineCostItem Excavator06m3_2024 => new MachineCostItem(21957.0, 10.2, 0.22, 1.0);

        /// <summary>물탱크(살수차) 16,000 L, 7204-0160: 손료 18,163원/시간, 경유 12.9 L/시간, 잡재료 30 %, 조종원 1인 (22쪽)</summary>
        public static MachineCostItem WaterTruck16000L_2024 => new MachineCostItem(18163.0, 12.9, 0.30, 1.0);
    }

    /// <summary>
    /// 시간당 기계경비 (5-5 단가 출처). 표준품셈 [공통부문] 8-1 "기계경비 적산요령"의 구성을 따른다:
    ///   기계경비 = 기계손료 + 운전경비 (주연료 + 잡재료 + 운전 노무비)
    ///   시간당 = 손료 + 연료 L/시간 × 경유 단가 × (1 + 잡재료 비율) + 조종원 수 × 일 노임 / 8시간
    ///
    /// 손계산 (굴착기 1.0 m³, 경유 1,842원/L, 건설기계운전사 283,297원/일):
    ///   27,901 + 19.5 × 1,842 × 1.22 + 283,297 / 8 = 27,901 + 43,821.18 + 35,412.13 = 107,134.3원/시간
    /// (덤프트럭 15 t, 화물차운전사 238,936원/일): 19,631 + 15.9 × 1,842 × 1.38 + 29,867 = 89,915.2원/시간
    ///
    /// 한계: 운전 노무비는 하루 8시간 기준이다 (연장 근무 가산은 넣지 않음). 상여금·퇴직급여 충당금,
    /// 자동덮개 손료(15 t 414원/시간)는 넣지 않았다. 경유 값은 시기마다 바뀌는 입력값이다.
    /// </summary>
    public static class MachineCost
    {
        public const double StandardHoursPerDay = 8.0;

        public static double Hourly(MachineCostItem item, double dieselPricePerLiter, double operatorDailyWage)
        {
            if (!(dieselPricePerLiter >= 0)) throw new ArgumentOutOfRangeException(nameof(dieselPricePerLiter));
            if (!(operatorDailyWage >= 0)) throw new ArgumentOutOfRangeException(nameof(operatorDailyWage));

            double fuel = item.FuelLitersPerHour * dieselPricePerLiter;
            return item.OwnershipPerHour + fuel * (1.0 + item.MiscRatio)
                + item.OperatorsPerDay * operatorDailyWage / StandardHoursPerDay;
        }
    }
}
