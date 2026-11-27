using System;

namespace CivilTwin.Core
{
    /// <summary>
    /// 균형 계획고 찾기 (Phase 5-2). 깎은 흙을 다져서 성토를 딱 채우는 계획고, 즉
    ///   절토량 × C = 성토량   (사토도 토취도 없음)
    /// 이 되는 계획고를 이분법으로 찾는다. 부지의 위치·크기·비탈 구배는 그대로 두고 계획고만 바꾼다.
    ///
    /// 왜 이분법인가:
    ///   계획고를 올리면 절토는 줄고 성토는 는다. 그래서 차이 F(z) = 절토 × C − 성토 는
    ///   계획고 z가 높을수록 작아지기만 한다 (단조 감소).
    ///   지형의 가장 낮은 표고에서는 성토가 0이라 F ≥ 0, 가장 높은 표고에서는 절토가 0이라 F ≤ 0.
    ///   그 사이 어딘가에 F = 0인 곳이 있고, 구간을 반씩 줄여 가면 찾을 수 있다.
    ///
    /// 한계: 운반 거리, 시공성, 배수, 주변 도로 높이 같은 실제 계획고 결정 요소는 보지 않는다.
    /// "흙이 남지도 모자라지도 않는 높이"라는 하나의 기준일 뿐 최적 계획고가 아니다.
    /// </summary>
    public static class BalancedElevation
    {
        /// <summary>구간을 반으로 줄이는 횟수. 표고 범위 1000 m도 2^40으로 나누면 1 nm보다 작다.</summary>
        public const int Iterations = 40;

        /// <summary>
        /// 균형 계획고 (m). pad의 계획고는 무시하고 나머지(위치, 크기, 구배)만 쓴다.
        /// 지형이 부지에 닿지 않으면(빈 값뿐) 절토·성토가 모두 0이라 어느 높이든 균형이고, 그때는 범위의 가운데를 돌려준다.
        /// </summary>
        public static double Find(ElevationGrid ground, GradingPad pad, double compactFactor)
        {
            return Find(ground, pad, compactFactor, out _);
        }

        /// <summary>균형 계획고와 그 높이에서의 토공량.</summary>
        public static double Find(ElevationGrid ground, GradingPad pad, double compactFactor, out EarthworkResult result)
        {
            if (ground == null) throw new ArgumentNullException(nameof(ground));
            if (!(compactFactor > 0.0)) throw new ArgumentOutOfRangeException(nameof(compactFactor), "C는 0보다 커야 한다.");

            double low = ground.MinElevation;    // F(low) ≥ 0 (성토 없음)
            double high = ground.MaxElevation;   // F(high) ≤ 0 (절토 없음)
            if (double.IsNaN(low) || double.IsNaN(high))
            {
                throw new ArgumentException("지형에 표고 값이 하나도 없다.", nameof(ground));
            }

            for (int i = 0; i < Iterations; i++)
            {
                double mid = (low + high) / 2.0;
                if (Difference(ground, pad, mid, compactFactor, out _) > 0.0)
                {
                    low = mid;    // 아직 흙이 남는다 → 계획고를 올린다
                }
                else
                {
                    high = mid;   // 흙이 모자라거나 딱 맞는다 → 계획고를 내린다
                }
            }

            double z = (low + high) / 2.0;
            Difference(ground, pad, z, compactFactor, out result);
            return z;
        }

        /// <summary>
        /// 절토에 암이 섞인 경우의 균형 계획고 (5-3 암반선과 함께 쓴다, 2026-11-20 전체 검토에서 추가).
        ///   F(z) = 토사 × C토사 + 암 × C암 − 성토
        /// 암은 다지면 부피가 커져서(C &gt; 1) 같은 절토로 성토를 더 많이 채운다 → 균형 계획고가 토사만일 때보다 올라간다.
        /// 계획고를 올리면 토사·암이 모두 줄고(암반선이 지표와 나란하므로) 성토는 늘어 F는 여전히 단조 감소한다.
        /// 암반선이 없으면(무한대) Find(…, soilCompact)와 같다.
        /// </summary>
        public static double FindWithRock(ElevationGrid ground, GradingPad pad, double soilCompact, double rockCompact,
            double rockDepth, out EarthworkResult result)
        {
            if (ground == null) throw new ArgumentNullException(nameof(ground));
            if (!(soilCompact > 0.0) || !(rockCompact > 0.0)) throw new ArgumentOutOfRangeException(nameof(soilCompact), "C는 0보다 커야 한다.");
            if (double.IsPositiveInfinity(rockDepth)) return Find(ground, pad, soilCompact, out result);

            double low = ground.MinElevation, high = ground.MaxElevation;
            if (double.IsNaN(low) || double.IsNaN(high)) throw new ArgumentException("지형에 표고 값이 하나도 없다.", nameof(ground));

            for (int i = 0; i < Iterations; i++)
            {
                double mid = (low + high) / 2.0;
                if (DifferenceWithRock(ground, pad, mid, soilCompact, rockCompact, rockDepth, out _) > 0.0) low = mid;
                else high = mid;
            }

            double z = (low + high) / 2.0;
            DifferenceWithRock(ground, pad, z, soilCompact, rockCompact, rockDepth, out result);
            return z;
        }

        /// <summary>F(z) = 토사 × C토사 + 암 × C암 − 성토 (다져진 상태 m³)</summary>
        public static double DifferenceWithRock(ElevationGrid ground, GradingPad pad, double padElevation,
            double soilCompact, double rockCompact, double rockDepth, out EarthworkResult result)
        {
            var trial = new GradingPad(
                pad.CenterEasting, pad.CenterNorthing, pad.WidthEastWest, pad.LengthNorthSouth,
                padElevation, pad.CutSlopeRatio, pad.FillSlopeRatio);
            ElevationGrid design = trial.BuildDesignGrid(ground);
            result = EarthworkVolume.Compute(ground, design);
            CutMaterials m = RockLine.Split(ground, design, rockDepth);
            return m.SoilVolume * soilCompact + m.RockVolume * rockCompact - result.FillVolume;
        }

        /// <summary>F(z) = 절토 × C − 성토 (다져진 상태 m³). 양수면 흙이 남고, 음수면 모자란다.</summary>
        public static double Difference(ElevationGrid ground, GradingPad pad, double padElevation, double compactFactor,
            out EarthworkResult result)
        {
            var trial = new GradingPad(
                pad.CenterEasting, pad.CenterNorthing, pad.WidthEastWest, pad.LengthNorthSouth,
                padElevation, pad.CutSlopeRatio, pad.FillSlopeRatio);
            result = EarthworkVolume.Compute(ground, trial.BuildDesignGrid(ground));
            return result.CutVolume * compactFactor - result.FillVolume;
        }
    }
}
