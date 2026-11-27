using System;

namespace CivilTwin.Core
{
    /// <summary>
    /// 터널 버력 운반 (2026-11-21): 막장이 깊어질수록 덤프트럭이 몇 대 있어야 로더가 기다리지 않는가.
    /// 표준품셈 [공통부문] 8-2-8 덤프트럭 (171 ~ 173쪽)의 적재기계 사용 시 싸이클을 쓴다:
    ///
    ///   cmt = cms · n / (60 · Es) + t2 + t3 + t4        (t5 덮개, t6 세륜은 현장 안이라 0)
    ///     n  = Qt / (q · k)            Qt = 트럭 1대 적재량(흐트러진 m³) = T / γt × L
    ///     t2 = 거리/적재속도 + 거리/공차속도  (거리 = 막장 깊이 + 입구 → 사토장)
    ///   필요한 트럭 = 올림(cmt × 로더 대수 ÷ 적재 시간)   — 트럭이 돌아오기 전에 로더가 다음 트럭을 채운다
    ///
    /// 값: 트럭 15 t, γt 2.65 t/m³ (공통 1장 10쪽 화강암 2,600 ~ 2,700의 가운데 — 인제 구간 암종은 **가정**), L 1.6,
    ///     로더 q 5.0 m³, k 0.55, cms 46.4초, Es 0.55 (NatmCrew와 같다),
    ///     속도 적재 15 · 공차 20 km/h (8-2-8 표 "교차가 가능한 산간지도로 및 제방도로, 미포장도로" — 공사 중 터널 바닥을 여기에 맞춤, **가정**),
    ///     t3 0.8분 (표 "모래, 역, 호박돌" 보통 — 암 버력을 여기에 맞춤, **가정**), t4 0.42분, 입구 → 사토장 1 km (**가정**).
    /// 손계산: Qt = 15 / 2.65 × 1.6 = 9.057 m³, n = 9.057 / (5 × 0.55) = 3.293회, 적재 = 46.4 × 3.293 / (60 × 0.55) = 4.630분.
    ///   막장 깊이 0 (거리 1 km): t2 = 4 + 3 = 7분 → cmt 12.85분 → 2.78 → 3대.  깊이 5 km (6 km): t2 = 24 + 18 = 42분 → 47.85분 → 10.33 → 11대.
    /// 한계: 가운데 사갱으로 나가는 막장은 사갱 길이를 몰라 계산하지 않는다.
    /// </summary>
    public static class TunnelMuckHaul
    {
        public const double TruckTons = 15.0;
        public const double RockUnitWeight = 2.65;
        public const double LoadedKmh = 15.0, EmptyKmh = 20.0;
        public const double DumpMinutes = 0.8, ArriveMinutes = 0.42;
        public const double OutsideKm = 1.0;

        /// <summary>트럭 1대 적재량 (흐트러진 m³) = T / γt × L</summary>
        public static double TruckLooseVolume(NatmCrew crew) => TruckTons / RockUnitWeight * crew.RockLoose;

        /// <summary>트럭 1대를 채우는 로더 적재 횟수 n = Qt / (q · k)</summary>
        public static double LoaderCycles(NatmCrew crew) => TruckLooseVolume(crew) / (crew.LoaderBucket * crew.LoaderK);

        /// <summary>적재 시간 (분) = cms · n / (60 · Es)</summary>
        public static double LoadMinutes(NatmCrew crew) =>
            crew.LoaderCycleSeconds * LoaderCycles(crew) / (60.0 * crew.LoaderE);

        /// <summary>트럭 1회 싸이클 (분): 막장에서 사토장까지 (막장 깊이 m + 바깥 1 km)</summary>
        public static double CycleMinutes(NatmCrew crew, double faceDepthMeters)
        {
            if (!(faceDepthMeters >= 0)) throw new ArgumentOutOfRangeException(nameof(faceDepthMeters));
            double km = faceDepthMeters / 1000.0 + OutsideKm;
            return LoadMinutes(crew) + km / LoadedKmh * 60.0 + km / EmptyKmh * 60.0 + DumpMinutes + ArriveMinutes;
        }

        /// <summary>로더가 기다리지 않으려면 필요한 트럭 대수 (올림, 로더 대수만큼 동시에 싣는다)</summary>
        public static int TrucksNeeded(NatmCrew crew, double faceDepthMeters)
        {
            double ratio = CycleMinutes(crew, faceDepthMeters) * crew.Loaders / LoadMinutes(crew);
            return (int)Math.Ceiling(ratio - 1e-9);
        }
    }
}
