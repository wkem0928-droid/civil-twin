using System;

namespace CivilTwin.Core
{
    /// <summary>
    /// 장비 간섭: 같은 현장에 장비를 늘리면 서로 기다리고 비켜 주느라 한 대당 작업량이 줄어든다.
    /// 그래서 "n대를 넣으면 n배"라고 보지 않고, 추가되는 장비마다 일정 비율로 덜 일한다고 가정한다.
    ///
    ///   실효 대수 = 1 + r + r² + … + r^(n−1) = (1 − r^n) / (1 − r)      (r = 간섭 비율, 0 < r ≤ 1)
    ///   r = 1 이면 간섭이 없다 (실효 대수 = n).
    ///
    /// 손계산 예: r = 0.9
    ///   1대 → 1,   2대 → 1 + 0.9 = 1.9,   4대 → 1 + 0.9 + 0.81 + 0.729 = 3.439
    ///
    /// 주의: 이 식과 r 값은 **이 프로그램의 가정**이다. 표준품셈이나 현장 자료에서 온 것이 아니다.
    /// 실제 간섭은 현장 넓이, 운반로, 장비 조합에 따라 달라진다.
    /// </summary>
    public static class MachineInterference
    {
        public static double EffectiveMachines(int machines, double ratio)
        {
            if (machines < 1) throw new ArgumentOutOfRangeException(nameof(machines), "장비는 1대 이상이어야 한다.");
            if (!(ratio > 0) || ratio > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(ratio), "간섭 비율은 0보다 크고 1 이하여야 한다.");
            }

            if (ratio == 1.0) return machines;
            return (1.0 - Math.Pow(ratio, machines)) / (1.0 - ratio);
        }
    }

    /// <summary>
    /// 공사비 계산 (대안 비교용). 단위는 원.
    ///
    ///   장비비       = 대수 × 가동 일수 × 하루 작업 시간 × 시간당 단가
    ///   기간 비례비 = 전체 공기(일) × 하루 단가      (현장 관리비처럼 공사가 길어질수록 느는 비용)
    ///
    /// 손계산 예: 굴착기 4대 × 6일 × 8시간 × 100,000원 = 19,200,000원
    ///            29일 × 500,000원 = 14,500,000원
    ///
    /// 포함하지 않는 것: 덤프트럭 운반비, 인건비, 재료비(콘크리트·철근), 다른 장비(롤러, 펌프카, 크레인)의 비용.
    /// 그래서 이 금액은 "공사비 전체"가 아니라 **대안끼리 비교하기 위한 일부 항목의 합**이다.
    /// 단가는 가정값이다 (출처 확인 전).
    /// </summary>
    public static class ConstructionCost
    {
        public static double Equipment(int machines, double days, double hoursPerDay, double hourlyRate)
        {
            if (machines < 0) throw new ArgumentOutOfRangeException(nameof(machines));
            if (!(days >= 0)) throw new ArgumentOutOfRangeException(nameof(days));
            if (!(hoursPerDay >= 0)) throw new ArgumentOutOfRangeException(nameof(hoursPerDay));
            if (!(hourlyRate >= 0)) throw new ArgumentOutOfRangeException(nameof(hourlyRate));

            return machines * days * hoursPerDay * hourlyRate;
        }

        public static double TimeRelated(double projectDays, double dailyRate)
        {
            if (!(projectDays >= 0)) throw new ArgumentOutOfRangeException(nameof(projectDays));
            if (!(dailyRate >= 0)) throw new ArgumentOutOfRangeException(nameof(dailyRate));

            return projectDays * dailyRate;
        }
    }
}
