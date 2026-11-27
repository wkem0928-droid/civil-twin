using System;

namespace CivilTwin.Core
{
    /// <summary>
    /// 암 깨기 — 대형브레이커 (2026-11-21). 표준품셈 2025 [공통부문] 8-2-13 대형브레이커 "나. 굴착" (원문 184쪽):
    ///   조합기계 = 대형브레이커 + 굴착기 0.6 ~ 0.8 m³, 작업능력 (m³/시간): 암파쇄 연암 4.5 ~ 5.5, 보통암 3.1 ~ 3.7 (터파기는 더 작다).
    ///
    /// 쓰는 이유: 토공 공기의 암 굴착은 "깨진 암을 굴착기로 싣는 작업량 = 토사의 0.35배"(가정, 파쇄암 K·E·f로 0.358배와 대조)만 본다.
    /// 그 앞의 "깨는 작업"은 공기 계산에 없다. 흙쌓기 조(EmbankmentCrew)처럼, 공기 안에 암을 다 깨려면 브레이커가 몇 대 필요한지 계산해 보인다.
    ///   필요 대수 = 올림(암 부피 ÷ (보통암 3.4 m³/시간 × 하루 작업 시간 × 공기))
    /// 손계산: 암 6,000 m³, 8시간, 공기 33.99일 → 6,000 ÷ (3.4 × 8 × 33.99 = 924.5) = 6.49 → 7대.
    /// 한계: 보통암으로 본다 (암 종류 자료 없음). 큰 암 굴착은 보통 발파를 쓴다 → 아래 발파(3-3) 크롤러드릴 대수도 함께 보인다.
    /// </summary>
    public static class RockBreaker
    {
        /// <summary>보통암 암파쇄 (m³/시간, 3.1 ~ 3.7의 가운데)</summary>
        public const double MediumRockHourly = 3.4;
        /// <summary>연암 암파쇄 (m³/시간, 4.5 ~ 5.5의 가운데)</summary>
        public const double SoftRockHourly = 5.0;

        public static int ToKeepUp(double rockVolume, double hoursPerDay, double days, double hourly = MediumRockHourly)
        {
            if (!(hoursPerDay > 0)) throw new ArgumentOutOfRangeException(nameof(hoursPerDay));
            if (!(hourly > 0)) throw new ArgumentOutOfRangeException(nameof(hourly));
            if (!(rockVolume > 0) || !(days > 0)) return 0;
            return (int)Math.Ceiling(rockVolume / (hourly * hoursPerDay * days) - 1e-9);
        }

        // ---------- 발파 (2026-11-21) ----------
        // 표준품셈 2025 [토목부문] 3-3-1 ~ 3-3-6 암발파 (m³당 유압식 크롤러드릴 110 kW 시간, 원문 66 ~ 67쪽).
        // 공법 구분은 국토교통부 「도로공사 노천발파 설계·시공지침」(주 ①) — 가까운 건물·시설의 진동 허용치가 공법을 정한다.
        // 주 ②: 천공·장약·전색·결선·발파·발파암 허물기 포함, 주 ③: TYPE-Ⅰ·Ⅱ는 대형브레이커 2차 파쇄 포함, 주 ④: 상차·운반 별도.

        /// <summary>발파 공법과 m³당 크롤러드릴 시간</summary>
        public readonly struct BlastType
        {
            public readonly string Name;
            public readonly double DrillHoursPerM3;
            public BlastType(string name, double drillHoursPerM3) { Name = name; DrillHoursPerM3 = drillHoursPerM3; }
            /// <summary>드릴 한 대의 시간당 발파량 (m³/시간) = 1 ÷ m³당 시간</summary>
            public double HourlyPerDrill => 1.0 / DrillHoursPerM3;
        }

        public static readonly BlastType[] BlastTypes =
        {
            new BlastType("TYPE-Ⅰ 미진동굴착", 0.100),
            new BlastType("TYPE-Ⅱ 정밀진동제어", 0.080),
            new BlastType("TYPE-Ⅲ 소규모진동제어", 0.049),
            new BlastType("TYPE-Ⅳ 중규모진동제어", 0.021),
            new BlastType("TYPE-Ⅴ 일반발파", 0.014),
            new BlastType("TYPE-Ⅵ 대규모발파", 0.012),
        };

        /// <summary>일반발파 (TYPE-Ⅴ, 3-3-5): 크롤러드릴 0.014 시간/m³ → 71.4 m³/시간</summary>
        public static BlastType GeneralBlast => BlastTypes[4];

        /// <summary>
        /// 공기 안에 암을 다 발파하려면 필요한 크롤러드릴 대수 = 올림(암 × m³당 드릴 시간 ÷ (하루 작업 시간 × 공기)).
        /// 손계산 (일반발파): 암 639,424 m³ × 0.014 = 8,952 시간 ÷ (8 × 770일 = 6,160) = 1.45 → 2대.
        /// </summary>
        public static int DrillsToKeepUp(double rockVolume, double hoursPerDay, double days, BlastType type)
        {
            if (!(hoursPerDay > 0)) throw new ArgumentOutOfRangeException(nameof(hoursPerDay));
            if (!(rockVolume > 0) || !(days > 0)) return 0;
            return (int)Math.Ceiling(rockVolume * type.DrillHoursPerM3 / (hoursPerDay * days) - 1e-9);
        }
    }
}
