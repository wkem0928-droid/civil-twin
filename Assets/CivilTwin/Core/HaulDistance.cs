using System;

namespace CivilTwin.Core
{
    /// <summary>
    /// 현장 안 평균 운반거리 (Phase 5-4). 깎는 흙의 무게중심에서 쌓는 흙의 무게중심까지의 수평거리로 본다.
    ///
    ///   절토 무게중심 = Σ (깎는 깊이 × 위치) / Σ 깎는 깊이     (격자점마다, 깊이 = 원지반 − 계획면 &gt; 0)
    ///   성토 무게중심 = Σ (쌓는 높이 × 위치) / Σ 쌓는 높이
    ///   평균 운반거리 = 두 무게중심 사이의 수평거리
    ///
    /// 격자점을 같은 넓이로 보는 근사다. 10 % 경사면 예(칸 5 m)에서 연속 계산 66.7 m 대신 70.0 m (5 % 차이).
    /// 실제 운반로는 직선이 아니고, 토적곡선(유토곡선)처럼 구간별로 나눠 배분하지도 않는다.
    /// </summary>
    public readonly struct HaulDistance
    {
        public readonly double CutEasting;
        public readonly double CutNorthing;
        public readonly double FillEasting;
        public readonly double FillNorthing;

        /// <summary>절토와 성토가 모두 있으면 true</summary>
        public readonly bool HasBoth;

        private HaulDistance(double cutE, double cutN, double fillE, double fillN, bool hasBoth)
        {
            CutEasting = cutE;
            CutNorthing = cutN;
            FillEasting = fillE;
            FillNorthing = fillN;
            HasBoth = hasBoth;
        }

        /// <summary>평균 운반거리 (m). 절토나 성토가 없으면 0.</summary>
        public double Meters
        {
            get
            {
                if (!HasBoth) return 0.0;
                double dx = FillEasting - CutEasting;
                double dy = FillNorthing - CutNorthing;
                return Math.Sqrt(dx * dx + dy * dy);
            }
        }

        public static HaulDistance Compute(ElevationGrid ground, ElevationGrid design)
        {
            if (ground == null) throw new ArgumentNullException(nameof(ground));
            if (design == null) throw new ArgumentNullException(nameof(design));

            double cutW = 0, cutE = 0, cutN = 0, fillW = 0, fillE = 0, fillN = 0;
            for (int row = 0; row < ground.Rows; row++)
            {
                double northing = ground.GetNorthing(row);
                for (int col = 0; col < ground.Columns; col++)
                {
                    double h = design.GetElevation(col, row) - ground.GetElevation(col, row);   // 양수 성토, 음수 절토
                    if (double.IsNaN(h)) continue;
                    double easting = ground.GetEasting(col);
                    if (h < 0)
                    {
                        cutW += -h; cutE += -h * easting; cutN += -h * northing;
                    }
                    else if (h > 0)
                    {
                        fillW += h; fillE += h * easting; fillN += h * northing;
                    }
                }
            }

            bool both = cutW > 0 && fillW > 0;
            return new HaulDistance(
                cutW > 0 ? cutE / cutW : double.NaN, cutW > 0 ? cutN / cutW : double.NaN,
                fillW > 0 ? fillE / fillW : double.NaN, fillW > 0 ? fillN / fillW : double.NaN,
                both);
        }
    }
}
