using System;

namespace CivilTwin.Core
{
    /// <summary>
    /// 덤프트럭 운반 작업량 (Phase 5-4, 5-5에서 출처 대조).
    /// 식의 출처: 2025년 적용 건설공사 표준품셈 [공통부문] 8-2-8 덤프트럭 (책 171 ~ 173쪽). 확인일 2026-11-20.
    ///
    ///   Q  = 60 × q × f × E / cm      (자연 상태 m³/시간, f = 1 / L, E = 0.9)
    ///   q  = T / γt × L                (흐트러진 상태 1회 적재량)
    ///      → 60 × q × f × E = 60 × (T / γt) × E 이므로 아래 코드는 자연 상태 적재량 qn = T / γt 로 계산한다.
    ///   cm = t1 + t2 + t3 + t4 + t5 + t6 (분)
    ///   t1 적재 시간 = cms × n / (60 × Es),  n = Qt / (q × k)   (적재기계 사이클 초, 작업효율, 버킷 용량·계수)
    ///                 ※ 원문 식에 n을 올림하라는 말이 없어 올림하지 않는다 (5-4 처음 판에서는 올림했었다).
    ///   t2 왕복 시간 = 운반거리 / 적재시 평균주행속도 + 운반거리 / 공차시 평균주행속도
    ///   t3 적하 시간 (차례를 기다리는 시간 포함), t4 적재장소 도착 ~ 적재 시작, t5 적재함 덮개 설치·해체, t6 세륜기 통과
    ///
    /// 한계: 무게로만 적재량을 정한다 (부피 한도는 보지 않음). 토사와 암의 단위중량을 구분하지 않는다.
    /// 운반로 경사, 교통 체증, 날씨는 보지 않는다.
    /// </summary>
    public readonly struct DumpTruck
    {
        /// <summary>적재 중량 T (t)</summary>
        public readonly double PayloadTons;
        /// <summary>흙의 자연 상태 단위중량 γt (t/m³)</summary>
        public readonly double UnitWeight;
        /// <summary>적재시 평균주행속도 (km/h)</summary>
        public readonly double LoadedSpeed;
        /// <summary>공차시 평균주행속도 (km/h)</summary>
        public readonly double EmptySpeed;
        /// <summary>t3 적하 시간 (분)</summary>
        public readonly double DumpMinutes;
        /// <summary>t4 적재장소 도착부터 적재 시작까지 (분)</summary>
        public readonly double WaitMinutes;
        /// <summary>t5 적재함 덮개 설치·해체 (분)</summary>
        public readonly double CoverMinutes;
        /// <summary>t6 세륜기 통과 (분)</summary>
        public readonly double WashMinutes;
        /// <summary>트럭 작업 효율 E</summary>
        public readonly double Efficiency;

        /// <summary>덮개·세륜 시간이 없는 경우 (t5 = t6 = 0)</summary>
        public DumpTruck(double payloadTons, double unitWeight, double loadedSpeed, double emptySpeed,
            double dumpMinutes, double waitMinutes, double efficiency)
            : this(payloadTons, unitWeight, loadedSpeed, emptySpeed, dumpMinutes, waitMinutes, 0.0, 0.0, efficiency)
        {
        }

        public DumpTruck(double payloadTons, double unitWeight, double loadedSpeed, double emptySpeed,
            double dumpMinutes, double waitMinutes, double coverMinutes, double washMinutes, double efficiency)
        {
            if (!(payloadTons > 0)) throw new ArgumentOutOfRangeException(nameof(payloadTons));
            if (!(unitWeight > 0)) throw new ArgumentOutOfRangeException(nameof(unitWeight));
            if (!(loadedSpeed > 0)) throw new ArgumentOutOfRangeException(nameof(loadedSpeed));
            if (!(emptySpeed > 0)) throw new ArgumentOutOfRangeException(nameof(emptySpeed));
            if (dumpMinutes < 0 || waitMinutes < 0 || coverMinutes < 0 || washMinutes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(dumpMinutes));
            }
            if (!(efficiency > 0 && efficiency <= 1)) throw new ArgumentOutOfRangeException(nameof(efficiency));

            PayloadTons = payloadTons;
            UnitWeight = unitWeight;
            LoadedSpeed = loadedSpeed;
            EmptySpeed = emptySpeed;
            DumpMinutes = dumpMinutes;
            WaitMinutes = waitMinutes;
            CoverMinutes = coverMinutes;
            WashMinutes = washMinutes;
            Efficiency = efficiency;
        }

        /// <summary>
        /// 현장 안 운반 (절토 → 성토). 표준품셈 2025 값 (책 쪽수):
        ///   γt 1.7 t/m³ — 점질토 "보통의 것" 1,500 ~ 1,700 kg/m³의 위 끝 (10쪽)
        ///   15 / 20 km/h — "교차가 가능한 산간지도로 및 제방도로, 미포장도로" (172쪽)
        ///   t3 1.05분 — 점질토·점토, 보통 (173쪽),  t4 0.42분 — "넓지는 않으나 불편없이 진입" (173쪽)
        ///   t5, t6 없음 — 현장 밖으로 나가지 않으므로 덮개·세륜이 필요 없다고 본다 (이 프로그램의 판단)
        ///   E 0.9 (172쪽). 15 t 트럭은 이 프로그램의 선택이다.
        /// </summary>
        public static DumpTruck OnSite2025 => new DumpTruck(15.0, 1.7, 15.0, 20.0, 1.05, 0.42, 0.0, 0.0, 0.9);

        /// <summary>
        /// 현장 밖 운반 (사토장·토취장). OnSite2025와 같고 다음만 다르다:
        ///   30 / 35 km/h — "2차로 교외 포장도로(2,000대/일 이상)" (172쪽)
        ///   t5 0.5분 — 자동덮개시설 (173쪽),  t6 1.5분 — 세륜기 통과 (173쪽)
        /// </summary>
        public static DumpTruck OffSite2025 => new DumpTruck(15.0, 1.7, 30.0, 35.0, 1.05, 0.42, 0.5, 1.5, 0.9);

        /// <summary>1회 적재량, 자연 상태 (m³) = T / γt</summary>
        public double NaturalPerTrip => PayloadTons / UnitWeight;

        /// <summary>1회 적재량, 흐트러진 상태 (m³) = T / γt × L</summary>
        public double LoosePerTrip(double looseFactor) => NaturalPerTrip * looseFactor;

        /// <summary>트럭 한 대를 채우는 적재기계 사이클 횟수 n = Qt / (q × k). 올림하지 않는다 (표준품셈 식 그대로).</summary>
        public double LoaderCycles(double looseFactor, double bucketCapacity, double bucketFactor)
        {
            if (!(bucketCapacity > 0 && bucketFactor > 0)) throw new ArgumentOutOfRangeException(nameof(bucketCapacity));
            return LoosePerTrip(looseFactor) / (bucketCapacity * bucketFactor);
        }

        /// <summary>적재 시간 t1 (분) = cms × n / (60 × Es)</summary>
        public double LoadMinutes(double looseFactor, double bucketCapacity, double bucketFactor,
            double loaderCycleSeconds, double loaderEfficiency)
        {
            if (!(loaderCycleSeconds > 0 && loaderEfficiency > 0)) throw new ArgumentOutOfRangeException(nameof(loaderCycleSeconds));
            double n = LoaderCycles(looseFactor, bucketCapacity, bucketFactor);
            return loaderCycleSeconds * n / (60.0 * loaderEfficiency);
        }

        /// <summary>왕복 시간 t2 (분) = (D / V1 + D / V2) × 60</summary>
        public double TravelMinutes(double distanceKm)
        {
            if (distanceKm < 0 || double.IsNaN(distanceKm)) throw new ArgumentOutOfRangeException(nameof(distanceKm));
            return (distanceKm / LoadedSpeed + distanceKm / EmptySpeed) * 60.0;
        }

        /// <summary>사이클 시간 cm (분) = t1 + t2 + t3 + t4 + t5 + t6</summary>
        public double CycleMinutes(double distanceKm, double loadMinutes)
        {
            return loadMinutes + TravelMinutes(distanceKm) + DumpMinutes + WaitMinutes + CoverMinutes + WashMinutes;
        }

        /// <summary>한 대의 시간당 운반량, 자연 상태 (m³/시간) = 60 × (T / γt) × E / cm</summary>
        public double HourlyNatural(double distanceKm, double loadMinutes)
        {
            return 60.0 * NaturalPerTrip * Efficiency / CycleMinutes(distanceKm, loadMinutes);
        }
    }
}
