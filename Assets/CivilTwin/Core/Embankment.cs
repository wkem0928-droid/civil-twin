using System;

namespace CivilTwin.Core
{
    /// <summary>
    /// 롤러 한 대의 시간당 다짐량 (표준품셈 [공통부문] 8-2-9 롤러, 2025년 적용판 174 ~ 176쪽).
    ///
    ///   Q = 1,000 × V × W × D × E × f / N      (m³/시간, 다져진 상태)
    ///
    ///   V : 다짐 속도 (km/시간)          진동롤러(자주식) 10 t, 노체·축제 → 4.0
    ///   W : 유효 다짐 폭 (m)             진동롤러 10 t → 1.9
    ///   D : 펴는 흙의 두께 (m)           노체 30 cm (다진 상태의 두께)
    ///   E : 작업효율                     노체·축제·노상, 보통 → 0.60
    ///   N : 소요 다짐 횟수               노체, 진동롤러 10 t → 6회 (다짐도 90 % 이상)
    ///   f : 체적환산계수                 다진 상태 그대로 셀 때 1
    ///
    /// 손계산: 1,000 × 4.0 × 1.9 × 0.30 × 0.60 × 1 / 6 = 228 m³/시간 → 하루 8시간 1,824 m³.
    ///
    /// ⚠️ 표준품셈은 "흙쌓기에 쓰는 롤러는 해당 항목의 일당 시공량을 우선 적용한다"고 적고 있다 (174쪽 ※).
    /// 그래서 공기 계산에는 아래 EmbankmentCrew(3-4-1 흙쌓기, 하루 1,150 m³)를 쓰고, 이 식은 대조용이다.
    /// 롤러 혼자는 1,824 m³/일이지만 조 전체(펴기·살수·다짐)는 1,150 m³/일로 더 느리다.
    /// </summary>
    public static class RollerProductivity
    {
        public const double VibratoryRoller10tSpeedKmh = 4.0;
        public const double VibratoryRoller10tWidth = 1.9;
        public const double SubgradeLiftMeters = 0.30;
        public const double SubgradeEfficiency = 0.60;
        public const int VibratoryRoller10tPasses = 6;

        public static double HourlyCompacted(double speedKmh, double widthMeters, double liftMeters,
            double efficiency, int passes, double volumeConversion = 1.0)
        {
            if (!(speedKmh > 0)) throw new ArgumentOutOfRangeException(nameof(speedKmh));
            if (!(widthMeters > 0)) throw new ArgumentOutOfRangeException(nameof(widthMeters));
            if (!(liftMeters > 0)) throw new ArgumentOutOfRangeException(nameof(liftMeters));
            if (!(efficiency > 0)) throw new ArgumentOutOfRangeException(nameof(efficiency));
            if (passes < 1) throw new ArgumentOutOfRangeException(nameof(passes));
            if (!(volumeConversion > 0)) throw new ArgumentOutOfRangeException(nameof(volumeConversion));
            return 1000.0 * speedKmh * widthMeters * liftMeters * efficiency * volumeConversion / passes;
        }

        /// <summary>진동롤러 10 t, 노체 30 cm, 6회, 보통 → 228 m³/시간</summary>
        public static double VibratoryRoller10t =>
            HourlyCompacted(VibratoryRoller10tSpeedKmh, VibratoryRoller10tWidth, SubgradeLiftMeters,
                SubgradeEfficiency, VibratoryRoller10tPasses);
    }

    /// <summary>
    /// 흙쌓기(펴고 다지기) 조 (2026-11-20). 표준품셈 [공통부문] 3-4-1 흙쌓기 ('25년 신설, 2025년 적용판 70쪽).
    ///
    ///   한 조 = 특별인부 1인 + 모터그레이더(3.6 m) 1대 + 진동롤러(자주식, 10 t) 1대 + 굴착기(0.6 m³) 1대 + 살수차(16,000 L) 0.5대
    ///   일당 시공량: 두께 30 cm(다짐도 90 % 이상) 1,150 m³,  두께 20 cm(95 % 이상) 770 m³
    ///   "체적환산계수를 기 반영한 것으로 다짐상태의 토량에 적용한다" → 계획 성토량(다진 상태)에 그대로 쓴다.
    ///
    /// 지금까지는 "다짐은 운반을 따라간다"고만 가정했다. 이제 따라가려면 몇 조가 필요한지 계산한다:
    ///   필요한 조 수 = 올림( 성토량 ÷ (1,150 × 하루 작업 시간 / 8 × 토공 일수) )
    /// 손계산: 성토 783,796 m³, 토공 333일, 8시간 → 783,796 ÷ (1,150 × 333) = 2.05 → 3조.
    ///
    /// 비용: 조 하나의 시간당 장비비 = 진동롤러 + 모터그레이더 + 굴착기 0.6 m³ + 살수차 × 0.5 (기계경비, MachineCost.Hourly).
    ///   m³당 장비비 = 시간당 × 8 ÷ 1,150.  흙쌓기 비용 = 성토량 × m³당 장비비 → 성토량에만 비례하므로 대안마다 같다.
    /// 손계산 (경유 1,842원/L, 건설기계운전사 283,297원/일, 화물차운전사 238,936원/일):
    ///   진동롤러 95,354.37 + 모터그레이더 120,659.28 + 굴착기 0.6 80,290.97 + 살수차 78,920.34 × 0.5
    ///   = 335,764.79원/시간 → × 8 ÷ 1,150 = 2,335.76원/m³
    /// ⚠️ 특별인부 노임은 넣지 않았다 (시중노임단가 원자료를 확인하지 못함). 살수차 조종원은 화물차운전사로 봤다 (가정).
    /// ⚠️ 하루 작업 시간이 8시간이 아니면 일당 시공량을 시간에 비례시킨다 (가정).
    /// </summary>
    public static class EmbankmentCrew
    {
        /// <summary>두께 30 cm, 다짐도 90 % 이상: 한 조의 하루(8시간) 시공량 (m³, 다진 상태)</summary>
        public const double DailyCompacted30cm = 1150.0;

        /// <summary>두께 20 cm, 다짐도 95 % 이상: 한 조의 하루 시공량 (m³)</summary>
        public const double DailyCompacted20cm = 770.0;

        /// <summary>한 조의 하루 시공량 (m³/일). 하루 작업 시간에 비례시킨다 (8시간 = 1,150 m³).</summary>
        public static double DailyVolume(double hoursPerDay)
        {
            if (!(hoursPerDay > 0)) throw new ArgumentOutOfRangeException(nameof(hoursPerDay));
            return DailyCompacted30cm * hoursPerDay / MachineCost.StandardHoursPerDay;
        }

        /// <summary>성토를 days일 안에 다 펴고 다지려면 필요한 조 수 (올림). 성토나 일수가 없으면 0.</summary>
        public static int CrewsToKeepUp(double fillVolume, double hoursPerDay, double days)
        {
            if (!(fillVolume > 0) || !(days > 0)) return 0;
            return (int)Math.Ceiling(fillVolume / (DailyVolume(hoursPerDay) * days) - 1e-9);
        }

        /// <summary>조 하나의 시간당 장비비 (원/시간). 특별인부 노임은 넣지 않는다.</summary>
        public static double HourlyMachineCost(double dieselPricePerLiter, double operatorDailyWage, double truckDriverDailyWage)
        {
            return MachineCost.Hourly(MachineCostItem.VibratoryRoller10t_2024, dieselPricePerLiter, operatorDailyWage)
                + MachineCost.Hourly(MachineCostItem.MotorGrader36m_2024, dieselPricePerLiter, operatorDailyWage)
                + MachineCost.Hourly(MachineCostItem.Excavator06m3_2024, dieselPricePerLiter, operatorDailyWage)
                + 0.5 * MachineCost.Hourly(MachineCostItem.WaterTruck16000L_2024, dieselPricePerLiter, truckDriverDailyWage);
        }

        /// <summary>다진 흙 1 m³의 장비비 (원/m³) = 시간당 장비비 × 8 ÷ 1,150</summary>
        public static double CostPerCubicMeter(double dieselPricePerLiter, double operatorDailyWage, double truckDriverDailyWage)
        {
            return HourlyMachineCost(dieselPricePerLiter, operatorDailyWage, truckDriverDailyWage)
                * MachineCost.StandardHoursPerDay / DailyCompacted30cm;
        }
    }
}
