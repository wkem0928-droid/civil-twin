using System;

namespace CivilTwin.Core
{
    /// <summary>
    /// 수식으로 만든 종 모양 언덕. 메시가 정확한지 검증하기 위한 시험용 지형이다.
    /// 어느 지점이든 정답 표고를 수식으로 계산할 수 있다.
    ///
    /// 수식:
    ///   d² = (동쪽좌표 - 중심 동쪽좌표)² + (북쪽좌표 - 중심 북쪽좌표)²
    ///   표고 = 기준표고 + 높이 × exp( -d² / (2 × σ²) )
    ///
    /// 성질 (손계산 확인용):
    ///   중심(d = 0)   : 기준표고 + 높이 × 1.000000
    ///   d = σ         : 기준표고 + 높이 × 0.606531   (exp(-0.5))
    ///   d = 2σ        : 기준표고 + 높이 × 0.135335   (exp(-2))
    ///   아주 먼 곳    : 기준표고에 가까워진다
    /// </summary>
    public readonly struct GaussianHill
    {
        /// <summary>언덕 꼭대기의 동쪽 좌표 (m)</summary>
        public readonly double CenterEasting;

        /// <summary>언덕 꼭대기의 북쪽 좌표 (m)</summary>
        public readonly double CenterNorthing;

        /// <summary>주변 평지의 표고 (m)</summary>
        public readonly double BaseElevation;

        /// <summary>평지에서 꼭대기까지의 높이 (m)</summary>
        public readonly double Height;

        /// <summary>언덕의 퍼짐 정도 σ (m). 클수록 완만하다.</summary>
        public readonly double Sigma;

        public GaussianHill(
            double centerEasting,
            double centerNorthing,
            double baseElevation,
            double height,
            double sigma)
        {
            if (!(sigma > 0)) throw new ArgumentOutOfRangeException(nameof(sigma), "σ는 0보다 커야 한다.");

            CenterEasting = centerEasting;
            CenterNorthing = centerNorthing;
            BaseElevation = baseElevation;
            Height = height;
            Sigma = sigma;
        }

        /// <summary>지정한 지점의 표고 (m)</summary>
        public double ElevationAt(double easting, double northing)
        {
            double dx = easting - CenterEasting;
            double dy = northing - CenterNorthing;
            double distanceSquared = dx * dx + dy * dy;
            return BaseElevation + Height * Math.Exp(-distanceSquared / (2.0 * Sigma * Sigma));
        }

        /// <summary>격자점마다 수식 값을 넣은 표고 격자를 만든다.</summary>
        public ElevationGrid CreateGrid(
            int columns,
            int rows,
            double cellSize,
            double originEasting,
            double originNorthing)
        {
            var elevations = new double[columns * rows];
            for (int row = 0; row < rows; row++)
            {
                double northing = originNorthing + row * cellSize;
                for (int col = 0; col < columns; col++)
                {
                    double easting = originEasting + col * cellSize;
                    elevations[row * columns + col] = ElevationAt(easting, northing);
                }
            }
            return new ElevationGrid(columns, rows, cellSize, originEasting, originNorthing, elevations);
        }
    }
}
