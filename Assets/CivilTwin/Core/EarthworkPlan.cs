using System;

namespace CivilTwin.Core
{
    /// <summary>
    /// 토공 공기 계산 (Phase 5-4): 굴착(토사·암·토취)과 덤프트럭 운반 중 느린 쪽이 공기를 정한다.
    ///
    /// 1) 토량 균형 (5-1): 절토가 토사와 암의 혼합이므로 L, C는 부피 비율로 섞은 값을 쓴다.
    ///      L절토 = (토사 × L토사 + 암 × L암) / 절토,   C절토도 같은 방식
    ///    토취 흙은 토사로 본다 (L토사, C토사).
    /// 2) 굴착 일수 = (토사 + 토취) / 하루 굴착량 + 암 / (하루 굴착량 × 암 작업 비율)
    ///    토취장의 흙도 같은 굴착기 조가 판다고 본다.
    /// 3) 운반: 유용토는 현장 안 평균 운반거리, 사토는 사토장까지, 토취는 토취장에서. 모두 자연 상태 m³로 센다.
    ///      트럭 가동 시간 = Σ 부피 / 한 대의 시간당 운반량(거리)
    ///      운반 일수 = 트럭 가동 시간 / (트럭 대수 × 하루 작업 시간)
    ///    트럭 대수가 0이면 운반을 계산하지 않는다 (Phase 2 ~ 4의 계산과 같아진다).
    /// 4) 토공 공기 = 굴착 일수와 운반 일수 중 큰 값을 올림.
    ///
    /// 성토 다짐(흙쌓기 조)은 운반을 따라갈 만큼 넣는다고 보고, 필요한 조 수만 계산한다 (EmbankmentCrews, 2026-11-20).
    /// L = C = 1, 암 없음, 트럭 0대면 예전 식 max(절토, 성토) / 하루 작업량과 같다.
    /// </summary>
    public sealed class EarthworkPlan
    {
        // ---- 입력 ----
        public CutMaterials Materials { get; }
        public double FillVolume { get; }
        public double ExcavationPerDay { get; }
        public double RockRateRatio { get; }
        public int Trucks { get; }
        public double HoursPerDay { get; }

        // ---- 결과 ----
        public EarthBalance Balance { get; }
        public double ExcavationDays { get; }
        public double TruckHours { get; }
        public double HaulDays { get; }

        /// <summary>현장 안, 사토, 토취 운반에 대한 트럭 한 대의 시간당 운반량 (자연 m³/시간)</summary>
        public double OnSiteHourly { get; }
        public double SpoilHourly { get; }
        public double BorrowHourly { get; }

        public EarthworkPlan(
            CutMaterials materials, double fillVolume,
            double soilLoose, double soilCompact, double rockLoose, double rockCompact,
            double excavationPerDay, double rockRateRatio,
            int trucks, double hoursPerDay, DumpTruck truck, double truckLoadMinutes,
            double onSiteKm, double spoilKm, double borrowKm)
            : this(materials, fillVolume, soilLoose, soilCompact, rockLoose, rockCompact,
                excavationPerDay, rockRateRatio, trucks, hoursPerDay, truck, truck, truckLoadMinutes,
                onSiteKm, spoilKm, borrowKm)
        {
        }

        /// <summary>
        /// 현장 안 운반과 현장 밖 운반(사토·토취)에 다른 트럭 조건을 쓰는 경우 (5-5: 도로 종류에 따라 주행속도가 다르고,
        /// 현장 밖으로 나가는 트럭만 덮개·세륜 시간이 든다). 적재 시간 t1은 두 경우 같다고 본다 (같은 트럭·같은 흙).
        /// </summary>
        public EarthworkPlan(
            CutMaterials materials, double fillVolume,
            double soilLoose, double soilCompact, double rockLoose, double rockCompact,
            double excavationPerDay, double rockRateRatio,
            int trucks, double hoursPerDay, DumpTruck onSiteTruck, DumpTruck offSiteTruck, double truckLoadMinutes,
            double onSiteKm, double spoilKm, double borrowKm)
        {
            if (!(excavationPerDay > 0)) throw new ArgumentOutOfRangeException(nameof(excavationPerDay));
            if (!(rockRateRatio > 0)) throw new ArgumentOutOfRangeException(nameof(rockRateRatio));
            if (trucks < 0) throw new ArgumentOutOfRangeException(nameof(trucks));
            if (!(hoursPerDay > 0)) throw new ArgumentOutOfRangeException(nameof(hoursPerDay));

            Materials = materials;
            FillVolume = fillVolume;
            ExcavationPerDay = excavationPerDay;
            RockRateRatio = rockRateRatio;
            Trucks = trucks;
            HoursPerDay = hoursPerDay;

            double cut = materials.TotalVolume;
            double cutLoose = cut > 0 ? (materials.SoilVolume * soilLoose + materials.RockVolume * rockLoose) / cut : soilLoose;
            double cutCompact = cut > 0 ? (materials.SoilVolume * soilCompact + materials.RockVolume * rockCompact) / cut : soilCompact;
            Balance = new EarthBalance(cut, fillVolume, cutLoose, cutCompact, soilLoose, soilCompact);

            ExcavationDays = (materials.SoilVolume + Balance.BorrowVolume) / excavationPerDay
                + materials.RockVolume / (excavationPerDay * rockRateRatio);

            OnSiteHourly = onSiteTruck.HourlyNatural(onSiteKm, truckLoadMinutes);
            SpoilHourly = offSiteTruck.HourlyNatural(spoilKm, truckLoadMinutes);
            BorrowHourly = offSiteTruck.HourlyNatural(borrowKm, truckLoadMinutes);

            TruckHours = Balance.ReusedVolume / OnSiteHourly
                + Balance.SurplusVolume / SpoilHourly
                + Balance.BorrowVolume / BorrowHourly;
            HaulDays = trucks > 0 ? TruckHours / (trucks * hoursPerDay) : 0.0;
        }

        /// <summary>
        /// 4D 재생과 누적 그래프용 공정: 절토와 성토가 같은 날 시작해 공기 동안 고르게 진행되어 같은 날 끝난다고 본다.
        ///   하루 절토 = 절토 / 공기(올림 전),  하루 성토 = 성토 / 공기(올림 전)
        /// 할 일이 없으면 하루 작업량을 1로 둔다 (0으로 나누지 않도록).
        /// </summary>
        public EarthworkSchedule ToSchedule()
        {
            double days = Math.Max(ExcavationDays, HaulDays);
            double cut = Materials.TotalVolume;
            double cutPerDay = days > 0 && cut > 0 ? cut / days : 1.0;
            double fillPerDay = days > 0 && FillVolume > 0 ? FillVolume / days : 1.0;
            return new EarthworkSchedule(cut, FillVolume, cutPerDay, fillPerDay);
        }

        /// <summary>운반을 계산에 넣었는가 (트럭이 1대 이상)</summary>
        public bool IncludesHaul => Trucks > 0;

        /// <summary>토공 공기 (일) = 굴착과 운반 중 큰 쪽을 올림. 할 일이 없으면 0.</summary>
        public int TotalDays => (int)Math.Ceiling(Math.Max(ExcavationDays, HaulDays) - 1e-9);

        /// <summary>운반이 공기를 정하는가 (운반 일수 &gt; 굴착 일수)</summary>
        public bool HaulIsBottleneck => IncludesHaul && HaulDays > ExcavationDays;

        /// <summary>
        /// 성토를 토공 공기 안에 펴고 다지려면 필요한 흙쌓기 조 수 (표준품셈 3-4-1, 한 조 하루 1,150 m³). 성토가 없으면 0.
        /// 공기 계산은 "다짐 조는 이만큼 넣어 운반을 따라간다"고 본다 (조 수가 공기를 늦추지 않는다).
        /// </summary>
        public int EmbankmentCrews => EmbankmentCrew.CrewsToKeepUp(FillVolume, HoursPerDay, Math.Max(ExcavationDays, HaulDays));

        /// <summary>
        /// 토공 공기 안에 암을 다 깨려면 필요한 대형브레이커 대수 (표준품셈 8-2-13 보통암 암파쇄 3.4 m³/시간). 암이 없으면 0.
        /// 공기 계산에는 넣지 않는다 (흙쌓기 조처럼 "이만큼 넣어야 한다"를 보인다).
        /// </summary>
        public int BreakersToKeepUp => RockBreaker.ToKeepUp(Materials.RockVolume, HoursPerDay, Math.Max(ExcavationDays, HaulDays));

        /// <summary>공기 안에 암을 발파하려면 필요한 크롤러드릴 대수 (표준품셈 3-3, 공법별). 암이 없으면 0. 공기에는 넣지 않는다.</summary>
        public int DrillsToKeepUp(RockBreaker.BlastType type) =>
            RockBreaker.DrillsToKeepUp(Materials.RockVolume, HoursPerDay, Math.Max(ExcavationDays, HaulDays), type);

        /// <summary>운반이 굴착보다 늦지 않으려면 필요한 트럭 대수 (올림). 굴착할 것이 없으면 0.</summary>
        public int TrucksToKeepUp
        {
            get
            {
                if (ExcavationDays <= 0) return 0;
                return (int)Math.Ceiling(TruckHours / (HoursPerDay * ExcavationDays) - 1e-9);
            }
        }
    }
}
