using System;

namespace CivilTwin.Core
{
    /// <summary>
    /// 굴착기(백호)의 시간당 작업량. 건설기계 시공능력 계산의 기본식이다.
    ///
    ///   Q = 3600 × q × K × f × E / Cm      (m³/시간)
    ///
    ///   q  : 버킷 용량 (m³)
    ///   K  : 버킷 계수 (버킷이 실제로 차는 비율. 흙의 종류에 따라 다르다)
    ///   f  : 토량환산계수 (자연 상태 토량으로 구하려면 f = 1 / L. L은 흙을 팠을 때 부푸는 비율)
    ///   E  : 작업 효율 (현장 조건, 대기 시간 등을 반영)
    ///   Cm : 1회 사이클 시간 (초) — 파고, 돌고, 붓고, 돌아오는 데 걸리는 시간
    ///   3600 / Cm = 한 시간에 하는 횟수
    ///
    /// 손계산 예: q 1.0 m³, K 0.9, L 1.25 → f 0.8, E 0.6, Cm 20초
    ///   Q = 3600 × 1.0 × 0.9 × 0.8 × 0.6 / 20 = 77.76 m³/시간
    ///   하루 8시간 → 622.08 m³/일
    ///
    /// 주의: 계수 값은 흙과 현장에 따라 달라진다. 여기의 기본값은 계산 예를 위한 가정이다.
    /// </summary>
    public static class ExcavatorProductivity
    {
        public static double HourlyVolume(
            double bucketCapacity, double bucketFactor, double volumeConversion,
            double efficiency, double cycleSeconds)
        {
            if (!(bucketCapacity > 0)) throw new ArgumentOutOfRangeException(nameof(bucketCapacity));
            if (!(bucketFactor > 0)) throw new ArgumentOutOfRangeException(nameof(bucketFactor));
            if (!(volumeConversion > 0)) throw new ArgumentOutOfRangeException(nameof(volumeConversion));
            if (!(efficiency > 0)) throw new ArgumentOutOfRangeException(nameof(efficiency));
            if (!(cycleSeconds > 0)) throw new ArgumentOutOfRangeException(nameof(cycleSeconds));

            return 3600.0 * bucketCapacity * bucketFactor * volumeConversion * efficiency / cycleSeconds;
        }

        /// <summary>
        /// 터파기 작업효율에서 빼는 값. 표준품셈 [공통부문] 8-2-3 굴착기, 작업효율 [주] ④ "터파기에 대하여는 0.05를 뺀 값으로 한다"
        /// (2025년 적용판 164쪽, 2026-11-20 반영).
        /// </summary>
        public const double TrenchEfficiencyReduction = 0.05;

        /// <summary>
        /// 터파기 작업량 배율 = (E − 0.05) / E. Q가 E에 비례하므로 하루 굴착량에 곱하면 된다.
        /// 손계산: E 0.60 → 0.55 / 0.60 = 0.916667.
        /// </summary>
        public static double TrenchRateFactor(double efficiency)
        {
            if (!(efficiency > TrenchEfficiencyReduction)) throw new ArgumentOutOfRangeException(nameof(efficiency));
            return (efficiency - TrenchEfficiencyReduction) / efficiency;
        }

        /// <summary>하루 작업량 (m³/일) = 시간당 작업량 × 하루 작업 시간 × 장비 대수</summary>
        public static double DailyVolume(double hourlyVolume, double hoursPerDay, int machines)
        {
            if (!(hourlyVolume > 0)) throw new ArgumentOutOfRangeException(nameof(hourlyVolume));
            if (!(hoursPerDay > 0)) throw new ArgumentOutOfRangeException(nameof(hoursPerDay));
            if (machines < 1) throw new ArgumentOutOfRangeException(nameof(machines));

            return hourlyVolume * hoursPerDay * machines;
        }
    }

    /// <summary>
    /// 토공 공정: 토공량과 하루 작업량으로 소요 일수와 날짜별 누적 작업량을 구한다.
    ///
    /// 가정 (단순화):
    ///   - 절토와 성토를 같은 날 시작해 함께 진행한다.
    ///   - 매일 일정한 양을 작업한다 (비, 휴일, 장비 고장은 없다).
    ///   - 토량환산계수에 따른 절토·성토 사이의 부피 변화는 다루지 않는다.
    ///
    /// 계산:
    ///   절토 일수 = 절토량 / 하루 절토 작업량,   성토 일수 = 성토량 / 하루 성토 작업량
    ///   공기(일) = 둘 중 긴 쪽을 올림
    ///   d일째까지 누적 절토량 = min(절토량, 하루 절토 작업량 × d)   (성토도 같다)
    ///   공정률(%) = (누적 절토량 + 누적 성토량) / (절토량 + 성토량) × 100
    ///
    /// 손계산 예: 절토 10000 m³, 성토 4000 m³, 하루 1000 m³씩
    ///   절토 10일, 성토 4일 → 공기 10일
    ///   3일째: 3000 + 3000 = 6000 → 6000 / 14000 = 42.857143 %
    ///   5일째: 5000 + 4000 = 9000 → 64.285714 %   (성토는 4일째에 끝났다)
    /// </summary>
    public readonly struct EarthworkSchedule
    {
        public readonly double CutVolume;
        public readonly double FillVolume;
        public readonly double CutPerDay;
        public readonly double FillPerDay;

        public EarthworkSchedule(double cutVolume, double fillVolume, double cutPerDay, double fillPerDay)
        {
            if (!(cutVolume >= 0)) throw new ArgumentOutOfRangeException(nameof(cutVolume));
            if (!(fillVolume >= 0)) throw new ArgumentOutOfRangeException(nameof(fillVolume));
            if (!(cutPerDay > 0)) throw new ArgumentOutOfRangeException(nameof(cutPerDay));
            if (!(fillPerDay > 0)) throw new ArgumentOutOfRangeException(nameof(fillPerDay));

            CutVolume = cutVolume;
            FillVolume = fillVolume;
            CutPerDay = cutPerDay;
            FillPerDay = fillPerDay;
        }

        /// <summary>절토에 걸리는 일수 (소수, 올림 전)</summary>
        public double CutDays => CutVolume / CutPerDay;

        /// <summary>성토에 걸리는 일수 (소수, 올림 전)</summary>
        public double FillDays => FillVolume / FillPerDay;

        /// <summary>공기 (일). 절토와 성토 중 긴 쪽을 올림한 값.</summary>
        public int TotalDays => (int)Math.Ceiling(Math.Max(CutDays, FillDays) - 1e-9);

        /// <summary>day일째까지의 누적 절토량 (m³)</summary>
        public double CumulativeCut(double day)
        {
            if (day <= 0) return 0.0;
            return Math.Min(CutVolume, CutPerDay * day);
        }

        /// <summary>day일째까지의 누적 성토량 (m³)</summary>
        public double CumulativeFill(double day)
        {
            if (day <= 0) return 0.0;
            return Math.Min(FillVolume, FillPerDay * day);
        }

        /// <summary>day일째의 공정률 (%). 토공이 없으면 100.</summary>
        public double ProgressPercent(double day)
        {
            double total = CutVolume + FillVolume;
            if (!(total > 0)) return 100.0;
            return (CumulativeCut(day) + CumulativeFill(day)) / total * 100.0;
        }
    }
}
