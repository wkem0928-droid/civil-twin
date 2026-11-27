using System;

namespace CivilTwin.Core
{
    /// <summary>
    /// 토공 공기 계산(EarthworkPlan)에 필요한 현장 조건 중, 대안(장비 대수·작업 시간)에 따라 바뀌지 않는 것들을 묶는다.
    /// 토사·암 부피, 토량환산계수, 암 작업 비율, 트럭 제원(현장 안·밖), 운반 거리. 값의 출처는 Docs/ASSUMPTIONS.md.
    /// </summary>
    public sealed class EarthworkSetup
    {
        public CutMaterials Materials { get; }
        public double SoilLoose { get; }
        public double SoilCompact { get; }
        public double RockLoose { get; }
        public double RockCompact { get; }
        public double RockRateRatio { get; }

        /// <summary>현장 안 운반 트럭 (절토 → 성토)</summary>
        public DumpTruck Truck { get; }

        /// <summary>현장 밖 운반 트럭 (사토장·토취장)</summary>
        public DumpTruck OffSiteTruck { get; }

        public double TruckLoadMinutes { get; }
        public double OnSiteKm { get; }
        public double SpoilKm { get; }
        public double BorrowKm { get; }

        /// <summary>현장 안·밖에 같은 트럭 조건을 쓰는 경우</summary>
        public EarthworkSetup(
            CutMaterials materials,
            double soilLoose, double soilCompact, double rockLoose, double rockCompact, double rockRateRatio,
            DumpTruck truck, double truckLoadMinutes,
            double onSiteKm, double spoilKm, double borrowKm)
            : this(materials, soilLoose, soilCompact, rockLoose, rockCompact, rockRateRatio,
                truck, truck, truckLoadMinutes, onSiteKm, spoilKm, borrowKm)
        {
        }

        public EarthworkSetup(
            CutMaterials materials,
            double soilLoose, double soilCompact, double rockLoose, double rockCompact, double rockRateRatio,
            DumpTruck onSiteTruck, DumpTruck offSiteTruck, double truckLoadMinutes,
            double onSiteKm, double spoilKm, double borrowKm)
        {
            if (!(truckLoadMinutes >= 0)) throw new ArgumentOutOfRangeException(nameof(truckLoadMinutes));
            Materials = materials;
            SoilLoose = soilLoose;
            SoilCompact = soilCompact;
            RockLoose = rockLoose;
            RockCompact = rockCompact;
            RockRateRatio = rockRateRatio;
            Truck = onSiteTruck;
            OffSiteTruck = offSiteTruck;
            TruckLoadMinutes = truckLoadMinutes;
            OnSiteKm = onSiteKm;
            SpoilKm = spoilKm;
            BorrowKm = borrowKm;
        }

        /// <summary>대안의 하루 굴착량, 트럭 대수, 하루 작업 시간으로 토공 공기를 계산한다.</summary>
        public EarthworkPlan Build(double fillVolume, double excavationPerDay, int trucks, double hoursPerDay)
        {
            return new EarthworkPlan(
                Materials, fillVolume,
                SoilLoose, SoilCompact, RockLoose, RockCompact,
                excavationPerDay, RockRateRatio,
                trucks, hoursPerDay, Truck, OffSiteTruck, TruckLoadMinutes,
                OnSiteKm, SpoilKm, BorrowKm);
        }
    }
}
