using System;

namespace CivilTwin.Core
{
    /// <summary>토공량 계산 결과. 모든 값은 0 이상이다 (Net만 음수가 될 수 있다).</summary>
    public readonly struct EarthworkResult
    {
        /// <summary>절토량: 깎아 내는 흙의 부피 (m³)</summary>
        public readonly double CutVolume;

        /// <summary>성토량: 쌓는 흙의 부피 (m³)</summary>
        public readonly double FillVolume;

        /// <summary>깎는 곳의 수평 면적 (m²)</summary>
        public readonly double CutArea;

        /// <summary>쌓는 곳의 수평 면적 (m²)</summary>
        public readonly double FillArea;

        public EarthworkResult(double cutVolume, double fillVolume, double cutArea, double fillArea)
        {
            CutVolume = cutVolume;
            FillVolume = fillVolume;
            CutArea = cutArea;
            FillArea = fillArea;
        }

        /// <summary>
        /// 절토량 − 성토량 (m³). 양수면 흙이 남고(사토), 음수면 흙이 모자란다(순성토).
        /// 토량환산계수(흙을 파면 부풀고 다지면 줄어드는 비율)는 적용하지 않은 값이다.
        /// </summary>
        public double NetVolume => CutVolume - FillVolume;
    }

    /// <summary>
    /// 원지반과 계획면의 높이 차이로 토공량을 구한다.
    ///
    /// 방법: 삼각형 점고법
    ///   화면의 메시와 똑같이 격자 한 칸을 대각선(남서 → 북동)으로 나눈 삼각형 2개를 쓴다.
    ///   세 꼭짓점의 높이 차를 h = 계획면 − 원지반 이라 하면 (h &gt; 0 성토, h &lt; 0 절토)
    ///
    ///   1) 세 꼭짓점이 모두 같은 쪽(모두 성토 또는 모두 절토)일 때
    ///        부피 = 삼각형 면적 × (h1 + h2 + h3) / 3
    ///
    ///   2) 한 삼각형 안에 절토와 성토가 섞여 있을 때
    ///      부호가 혼자 다른 꼭짓점을 h1, 나머지를 h2, h3 이라 하면
    ///      h1 쪽 부피 = 면적 × h1³ / ( 3 × (h1 − h2) × (h1 − h3) )
    ///      h1 쪽 면적 = 면적 × h1² / ( (h1 − h2) × (h1 − h3) )
    ///      반대쪽 부피 = 전체( 면적 × (h1 + h2 + h3) / 3 ) − h1 쪽 부피
    ///      (높이 차가 0이 되는 선이 삼각형을 가로지르고, 그 선으로 잘린 작은 삼각형이 h1 쪽이다.)
    ///
    ///   삼각형 면적 = 간격² / 2
    ///
    /// 손계산 예 (간격 10 m → 삼각형 면적 50 m²):
    ///   모두 절토, h = −2, −2, −2      → 절토 50 × 6 / 3 = 100 m³
    ///   섞임, h = +3, −3, −3           → 성토 50 × 27 / (3 × 6 × 6) = 12.5 m³
    ///                                     전체 50 × (−3) / 3 = −50 → 절토 = 12.5 − (−50) = 62.5 m³
    ///     (검산: 0이 되는 선은 두 변의 중점을 잇는다 → 성토 쪽은 면적 50 / 4 = 12.5 m², 평균 높이 3 / 3 = 1 m)
    ///
    /// 빈 값(NaN)이 있는 삼각형은 계산에서 뺀다.
    /// </summary>
    public static class EarthworkVolume
    {
        /// <summary>원지반 격자와 계획면 격자(같은 크기·위치)로 토공량을 구한다.</summary>
        public static EarthworkResult Compute(ElevationGrid ground, ElevationGrid design)
        {
            if (ground == null) throw new ArgumentNullException(nameof(ground));
            if (design == null) throw new ArgumentNullException(nameof(design));
            if (ground.Columns != design.Columns || ground.Rows != design.Rows ||
                ground.CellSize != design.CellSize ||
                ground.OriginEasting != design.OriginEasting || ground.OriginNorthing != design.OriginNorthing)
            {
                throw new ArgumentException("원지반과 계획면의 격자 크기·위치가 다르다.", nameof(design));
            }

            double triangleArea = ground.CellSize * ground.CellSize / 2.0;
            var sum = new Accumulator();

            for (int row = 0; row < ground.Rows - 1; row++)
            {
                for (int col = 0; col < ground.Columns - 1; col++)
                {
                    double h00 = design.GetElevation(col, row) - ground.GetElevation(col, row);
                    double h10 = design.GetElevation(col + 1, row) - ground.GetElevation(col + 1, row);
                    double h01 = design.GetElevation(col, row + 1) - ground.GetElevation(col, row + 1);
                    double h11 = design.GetElevation(col + 1, row + 1) - ground.GetElevation(col + 1, row + 1);

                    sum.AddTriangle(h00, h01, h11, triangleArea);  // 북서쪽 삼각형
                    sum.AddTriangle(h00, h10, h11, triangleArea);  // 남동쪽 삼각형
                }
            }

            return new EarthworkResult(sum.Cut, sum.Fill, sum.CutArea, sum.FillArea);
        }

        /// <summary>
        /// 삼각형 하나의 토공량. 꼭짓점 높이 차 h = 계획면 − 원지반 (양수 성토, 음수 절토).
        /// 손계산과 비교하기 쉽도록 따로 공개한다.
        /// </summary>
        public static EarthworkResult ComputeTriangle(double h1, double h2, double h3, double area)
        {
            if (!(area > 0)) throw new ArgumentOutOfRangeException(nameof(area), "면적은 0보다 커야 한다.");

            var sum = new Accumulator();
            sum.AddTriangle(h1, h2, h3, area);
            return new EarthworkResult(sum.Cut, sum.Fill, sum.CutArea, sum.FillArea);
        }

        /// <summary>삼각형별 토공량을 더해 가는 누적기. Core 안의 다른 계산(공정 단계)도 같은 식을 쓰도록 공유한다.</summary>
        internal struct Accumulator
        {
            public double Cut;
            public double Fill;
            public double CutArea;
            public double FillArea;

            public void AddTriangle(double a, double b, double c, double area)
            {
                if (double.IsNaN(a) || double.IsNaN(b) || double.IsNaN(c)) return;

                int positives = (a > 0 ? 1 : 0) + (b > 0 ? 1 : 0) + (c > 0 ? 1 : 0);
                int negatives = (a < 0 ? 1 : 0) + (b < 0 ? 1 : 0) + (c < 0 ? 1 : 0);
                double total = area * (a + b + c) / 3.0;

                if (negatives == 0)
                {
                    if (positives == 0) return;   // 세 점 모두 높이 차 0: 손대지 않는 곳
                    Fill += total;
                    FillArea += area;
                    return;
                }
                if (positives == 0)
                {
                    Cut += -total;
                    CutArea += area;
                    return;
                }

                // 절토와 성토가 섞인 삼각형. 부호가 혼자 다른 꼭짓점을 찾는다.
                // 양수가 하나뿐이면 그 점이, 아니면(양수 둘, 음수 하나) 음수인 점이 혼자다.
                double lone, o1, o2;
                bool loneIsPositive = positives == 1;
                if (loneIsPositive)
                {
                    if (a > 0) { lone = a; o1 = b; o2 = c; }
                    else if (b > 0) { lone = b; o1 = a; o2 = c; }
                    else { lone = c; o1 = a; o2 = b; }
                }
                else
                {
                    if (a < 0) { lone = a; o1 = b; o2 = c; }
                    else if (b < 0) { lone = b; o1 = a; o2 = c; }
                    else { lone = c; o1 = a; o2 = b; }
                }

                double denominator = (lone - o1) * (lone - o2);
                double loneVolume = area * lone * lone * lone / (3.0 * denominator);  // 부호는 lone을 따른다
                double loneArea = area * lone * lone / denominator;
                double otherVolume = total - loneVolume;
                double otherArea = area - loneArea;

                if (loneIsPositive)
                {
                    Fill += loneVolume;
                    FillArea += loneArea;
                    Cut += -otherVolume;
                    CutArea += otherArea;
                }
                else
                {
                    Cut += -loneVolume;
                    CutArea += loneArea;
                    Fill += otherVolume;
                    FillArea += otherArea;
                }
            }
        }
    }
}
