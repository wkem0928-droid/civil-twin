using System;

namespace CivilTwin.Core
{
    /// <summary>
    /// 절토·성토 색칠. 높이 차 h = 계획면 − 원지반 을 색으로 바꾼다.
    ///   h &lt; 0 (절토, 깎는 곳) : 빨강. 깊이 깎을수록 진하다.
    ///   h &gt; 0 (성토, 쌓는 곳) : 파랑. 높이 쌓을수록 진하다.
    ///   |h| 가 1 cm 이하       : 손대지 않는 곳. 원래 지형 색을 회색 쪽으로 흐리게 한다
    ///                            (빨강·파랑이 눈에 잘 띄도록).
    ///
    /// 진하기: s = |h| / 최대 깊이(또는 최대 높이),  색 = 옅은 색 + s × (진한 색 − 옅은 색)
    /// 손계산 예: 최대 절토 깊이 10 m 일 때 5 m 깎는 곳 → s = 0.5
    ///            빨강 성분 = 1.00 + 0.5 × (0.70 − 1.00) = 0.85
    /// </summary>
    public static class CutFillColors
    {
        /// <summary>이 값(m) 이하의 높이 차는 "손대지 않음"으로 본다.</summary>
        public const double UntouchedTolerance = 0.01;

        // 2026-11-20 전체 검토: 예전 색(진한 원색 빨강·파랑)은 넓은 성토 부지가 짙은 파란 판으로 보여 화면을 압도했다.
        // 흙 느낌이 나는 적갈색(절토)과 차분한 청록(성토)으로 바꿨다. 깊을수록 진해지는 규칙은 그대로다.
        public static readonly RgbColor CutLight = new RgbColor(0.98, 0.86, 0.76);
        public static readonly RgbColor CutDark = new RgbColor(0.75, 0.33, 0.18);
        public static readonly RgbColor FillLight = new RgbColor(0.82, 0.90, 0.96);
        public static readonly RgbColor FillDark = new RgbColor(0.22, 0.45, 0.72);
        public static readonly RgbColor Gray = new RgbColor(0.55, 0.55, 0.55);

        /// <summary>손대지 않는 곳에서 원래 색을 회색 쪽으로 섞는 비율 (0 = 그대로, 1 = 완전 회색).</summary>
        public const double MuteAmount = 0.6;

        /// <summary>
        /// 높이 차 h 의 색.
        /// </summary>
        /// <param name="h">계획면 − 원지반 (m)</param>
        /// <param name="maxCutDepth">이 계획에서 가장 깊이 깎는 깊이 (m, 0 이상)</param>
        /// <param name="maxFillHeight">이 계획에서 가장 높이 쌓는 높이 (m, 0 이상)</param>
        /// <param name="terrainColor">그 지점의 원래 지형 색 (손대지 않는 곳에 쓴다)</param>
        public static RgbColor ColorFor(double h, double maxCutDepth, double maxFillHeight, RgbColor terrainColor)
        {
            if (double.IsNaN(h)) return Gray;

            if (h < -UntouchedTolerance)
            {
                return Mix(CutLight, CutDark, Ratio(-h, maxCutDepth));
            }
            if (h > UntouchedTolerance)
            {
                return Mix(FillLight, FillDark, Ratio(h, maxFillHeight));
            }
            return Mix(terrainColor, Gray, MuteAmount);
        }

        /// <summary>
        /// 가장 깊이 깎는 깊이와 가장 높이 쌓는 높이를 찾는다 (둘 다 0 이상, 격자점 기준).
        /// </summary>
        public static void FindMaxDepths(
            ElevationGrid ground, ElevationGrid design, out double maxCutDepth, out double maxFillHeight)
        {
            if (ground == null) throw new ArgumentNullException(nameof(ground));
            if (design == null) throw new ArgumentNullException(nameof(design));
            if (ground.Columns != design.Columns || ground.Rows != design.Rows)
            {
                throw new ArgumentException("원지반과 계획면의 격자 크기가 다르다.", nameof(design));
            }

            maxCutDepth = 0.0;
            maxFillHeight = 0.0;
            for (int row = 0; row < ground.Rows; row++)
            {
                for (int col = 0; col < ground.Columns; col++)
                {
                    double h = design.GetElevation(col, row) - ground.GetElevation(col, row);
                    if (double.IsNaN(h)) continue;
                    if (-h > maxCutDepth) maxCutDepth = -h;
                    if (h > maxFillHeight) maxFillHeight = h;
                }
            }
        }

        private static double Ratio(double value, double max)
        {
            if (!(max > 0)) return 1.0;
            double s = value / max;
            if (s < 0.0) return 0.0;
            if (s > 1.0) return 1.0;
            return s;
        }

        private static RgbColor Mix(RgbColor a, RgbColor b, double s)
        {
            return new RgbColor(
                a.R + s * (b.R - a.R),
                a.G + s * (b.G - a.G),
                a.B + s * (b.B - a.B));
        }
    }
}
