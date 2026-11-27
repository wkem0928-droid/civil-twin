using System;

namespace CivilTwin.Core
{
    /// <summary>
    /// 댐 성토 운반 검산 (2026-11-25): "자료에 나온 덤프트럭으로 그 성토량을 나를 수 있었나?"
    ///
    ///   하루에 나를 무게   W = 하루 성토량(다진 상태 m³) × 다진 단위중량 (t/m³)
    ///   한 바퀴 무게       P = Σ 대수 × 적재량 (t)
    ///   대당 하루 왕복     N = W ÷ P ÷ 작업일 비율   (비·혹한기에 쉬면 일하는 날에 더 많이 날라야 한다)
    ///   한 번에 쓸 수 있는 시간 T = 하루 작업 시간 × 60 ÷ N (분)
    ///   → T가 실제 왕복 시간(사이클)보다 길면 가능하다.
    /// 왕복 시간은 표준품셈 8-2-8의 속도와 시간으로 어림한다 (적재 시간은 따로 준다).
    ///
    /// 한계: 트럭이 모두 같은 때 본댐 성토에만 쓰였다고 본다 (여수로·발전소·굴착에도 썼을 것이다). 가동률·교통 체증은 보지 않는다.
    /// </summary>
    public sealed class FillHaulCheck
    {
        /// <summary>하루 성토량 (다진 m³, 달력 날짜 기준)</summary>
        public double DailyVolume { get; }
        /// <summary>다진 상태 단위중량 (t/m³)</summary>
        public double UnitWeight { get; }
        /// <summary>모든 트럭이 한 바퀴 나르는 무게 (t)</summary>
        public double FleetTons { get; }
        /// <summary>트럭 대수</summary>
        public int Trucks { get; }

        public FillHaulCheck(double dailyVolume, double unitWeight, params (int count, double tons)[] fleet)
        {
            if (!(dailyVolume > 0)) throw new ArgumentOutOfRangeException(nameof(dailyVolume));
            if (!(unitWeight > 0)) throw new ArgumentOutOfRangeException(nameof(unitWeight));
            DailyVolume = dailyVolume;
            UnitWeight = unitWeight;
            foreach ((int count, double tons) in fleet ?? new (int, double)[0])
            {
                if (count < 0 || !(tons > 0)) throw new ArgumentOutOfRangeException(nameof(fleet));
                Trucks += count;
                FleetTons += count * tons;
            }
            if (!(FleetTons > 0)) throw new ArgumentOutOfRangeException(nameof(fleet), "트럭이 한 대도 없다.");
        }

        /// <summary>하루에 나를 무게 (t, 달력 날짜 기준)</summary>
        public double DailyTons => DailyVolume * UnitWeight;

        /// <summary>일하는 날 대당 왕복 횟수. workdayRatio = 일하는 날 ÷ 달력 날 (0 초과 1 이하)</summary>
        public double TripsPerDay(double workdayRatio)
        {
            if (!(workdayRatio > 0 && workdayRatio <= 1)) throw new ArgumentOutOfRangeException(nameof(workdayRatio));
            return DailyTons / FleetTons / workdayRatio;
        }

        /// <summary>왕복 한 번에 쓸 수 있는 시간 (분)</summary>
        public double MinutesPerTrip(double hoursPerDay, double workdayRatio)
        {
            if (!(hoursPerDay > 0 && hoursPerDay <= 24)) throw new ArgumentOutOfRangeException(nameof(hoursPerDay));
            return hoursPerDay * 60.0 / TripsPerDay(workdayRatio);
        }

        /// <summary>
        /// 왕복 시간 (분) = (적재 + 거리 ÷ 적재 속도 + 거리 ÷ 공차 속도 + 적하 + 대기) ÷ 작업 효율.
        /// 속도·적하·대기·효율은 truck의 값(표준품셈 8-2-8)을 쓴다. 적재량은 쓰지 않는다.
        /// </summary>
        public static double CycleMinutes(DumpTruck truck, double haulKm, double loadMinutes)
        {
            if (!(haulKm >= 0)) throw new ArgumentOutOfRangeException(nameof(haulKm));
            if (!(loadMinutes >= 0)) throw new ArgumentOutOfRangeException(nameof(loadMinutes));
            double travel = haulKm / truck.LoadedSpeed * 60.0 + haulKm / truck.EmptySpeed * 60.0;
            return (loadMinutes + travel + truck.DumpMinutes + truck.WaitMinutes) / truck.Efficiency;
        }

        // ── 소양강댐 ──────────────────────────────────────────

        /// <summary>다진 사력·점토의 단위중량 2.0 t/m³ (가정, 시험값 자료 없음)</summary>
        public const double SoyanggangUnitWeight = 2.0;
        /// <summary>점토 채취장까지 4 km [T]. 사력 채취장 거리는 자료가 없어 같은 거리로 둔다 (가정).</summary>
        public const double SoyanggangHaulKm = 4.0;
        /// <summary>적재 시간 (분, 가정)</summary>
        public const double SoyanggangLoadMinutes = 4.0;

        /// <summary>13.5 t 덤프트럭 127대 + 32 t 20대 [T]</summary>
        public static FillHaulCheck Soyanggang(DamZoneSection zones) =>
            new FillHaulCheck(zones.DailyVolume, SoyanggangUnitWeight, (127, 13.5), (20, 32.0));

        /// <summary>소양강댐 왕복 시간: 산간 미포장 15 / 20 km/h (표준품셈 8-2-8), 4 km</summary>
        public static double SoyanggangCycleMinutes() =>
            CycleMinutes(DumpTruck.OnSite2025, SoyanggangHaulKm, SoyanggangLoadMinutes);
    }
}
