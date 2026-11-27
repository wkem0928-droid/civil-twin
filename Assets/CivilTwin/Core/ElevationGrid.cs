using System;

namespace CivilTwin.Core
{
    /// <summary>
    /// 표고 격자 (DEM). 일정한 간격의 격자점마다 표고 값을 하나씩 가진다.
    ///
    /// 배치:
    ///   - 열(col)은 서 → 동, 행(row)은 남 → 북 방향으로 증가한다.
    ///   - (col 0, row 0)은 남서쪽 모서리 격자점이다.
    ///   - 격자점의 좌표:
    ///       동쪽좌표 = OriginEasting  + col × CellSize
    ///       북쪽좌표 = OriginNorthing + row × CellSize
    ///
    /// 표고 배열은 한 줄로 저장한다: 번호 = row × Columns + col
    /// </summary>
    public sealed class ElevationGrid
    {
        private readonly double[] _elevations;

        /// <summary>동서 방향 격자점 개수</summary>
        public int Columns { get; }

        /// <summary>남북 방향 격자점 개수</summary>
        public int Rows { get; }

        /// <summary>격자 간격 (m)</summary>
        public double CellSize { get; }

        /// <summary>남서쪽 모서리 격자점의 동쪽 좌표 (m)</summary>
        public double OriginEasting { get; }

        /// <summary>남서쪽 모서리 격자점의 북쪽 좌표 (m)</summary>
        public double OriginNorthing { get; }

        /// <summary>가장 낮은 표고 (m)</summary>
        public double MinElevation { get; }

        /// <summary>가장 높은 표고 (m)</summary>
        public double MaxElevation { get; }

        public ElevationGrid(
            int columns,
            int rows,
            double cellSize,
            double originEasting,
            double originNorthing,
            double[] elevations)
        {
            if (columns < 2) throw new ArgumentOutOfRangeException(nameof(columns), "열은 2개 이상이어야 한다.");
            if (rows < 2) throw new ArgumentOutOfRangeException(nameof(rows), "행은 2개 이상이어야 한다.");
            if (!(cellSize > 0)) throw new ArgumentOutOfRangeException(nameof(cellSize), "격자 간격은 0보다 커야 한다.");
            if (elevations == null) throw new ArgumentNullException(nameof(elevations));
            if (elevations.Length != columns * rows)
            {
                throw new ArgumentException(
                    $"표고 개수({elevations.Length})가 열×행({columns}×{rows}={columns * rows})과 다르다.",
                    nameof(elevations));
            }

            Columns = columns;
            Rows = rows;
            CellSize = cellSize;
            OriginEasting = originEasting;
            OriginNorthing = originNorthing;

            // 바깥에서 원본 배열을 고쳐도 격자가 바뀌지 않도록 복사해 둔다.
            _elevations = (double[])elevations.Clone();

            // 빈 값(NaN)은 건너뛰고 최저·최고 표고를 구한다.
            double min = double.PositiveInfinity;
            double max = double.NegativeInfinity;
            int noData = 0;
            for (int i = 0; i < _elevations.Length; i++)
            {
                double z = _elevations[i];
                if (double.IsNaN(z))
                {
                    noData++;
                    continue;
                }
                if (double.IsInfinity(z))
                {
                    throw new ArgumentException($"표고 {i}번이 무한대다.", nameof(elevations));
                }
                if (z < min) min = z;
                if (z > max) max = z;
            }

            if (noData == _elevations.Length)
            {
                throw new ArgumentException("모든 표고가 빈 값이다.", nameof(elevations));
            }

            MinElevation = min;
            MaxElevation = max;
            NoDataCount = noData;
        }

        /// <summary>
        /// 빈 값(데이터 없음)인 격자점의 개수.
        /// 빈 값은 내부에서 NaN으로 보관한다. 실제 DEM에는 바다, 측량 범위 밖 등
        /// 표고가 없는 칸이 있고, 파일에서는 -9999 같은 약속된 숫자로 적혀 있다.
        /// </summary>
        public int NoDataCount { get; }

        /// <summary>격자점 (col, row)이 빈 값인가</summary>
        public bool IsNoData(int col, int row)
        {
            return double.IsNaN(GetElevation(col, row));
        }

        /// <summary>동서 방향 전체 길이 (m) = (열 개수 - 1) × 간격</summary>
        public double WidthMeters => (Columns - 1) * CellSize;

        /// <summary>남북 방향 전체 길이 (m) = (행 개수 - 1) × 간격</summary>
        public double HeightMeters => (Rows - 1) * CellSize;

        /// <summary>격자점 (col, row)의 표고 (m). 빈 값이면 NaN.</summary>
        public double GetElevation(int col, int row)
        {
            if (col < 0 || col >= Columns) throw new ArgumentOutOfRangeException(nameof(col));
            if (row < 0 || row >= Rows) throw new ArgumentOutOfRangeException(nameof(row));
            return _elevations[row * Columns + col];
        }

        /// <summary>
        /// 임의 지점의 표고를 구한다 (격자점 사이는 보간).
        /// 지점이 격자 범위 밖이면 false를 돌려준다.
        ///
        /// 보간 방법: 삼각형 평면 보간 (TIN)
        ///   화면에 그리는 메시와 똑같이, 격자 한 칸을 대각선(남서 → 북동)으로 나눈
        ///   삼각형 2개 중 지점이 속한 삼각형의 평면에서 표고를 읽는다.
        ///   그래서 "화면에 보이는 지표면"과 "계산한 표고"가 정확히 일치한다.
        ///
        ///   칸 안에서의 위치를 0~1로 나타낸다:
        ///     fx = (동쪽좌표 - 칸의 서쪽 변) / 간격
        ///     fz = (북쪽좌표 - 칸의 남쪽 변) / 간격
        ///
        ///   네 모서리 표고: z00(남서), z10(남동), z01(북서), z11(북동)
        ///
        ///   fz ≥ fx (북서쪽 삼각형): 표고 = z00 + fz × (z01 - z00) + fx × (z11 - z01)
        ///   fz &lt; fx (남동쪽 삼각형): 표고 = z00 + fx × (z10 - z00) + fz × (z11 - z10)
        /// </summary>
        public bool TryGetElevationAt(double easting, double northing, out double elevation)
        {
            elevation = double.NaN;

            double gx = (easting - OriginEasting) / CellSize;
            double gz = (northing - OriginNorthing) / CellSize;

            // NaN은 어떤 비교에서도 false라서, "범위 안"을 확인하는 형태로 쓴다.
            bool inside = gx >= 0 && gz >= 0 && gx <= Columns - 1 && gz <= Rows - 1;
            if (!inside) return false;

            int col = (int)Math.Floor(gx);
            int row = (int)Math.Floor(gz);

            // 동쪽 끝·북쪽 끝 변 위의 점은 마지막 칸에 속하게 한다.
            if (col > Columns - 2) col = Columns - 2;
            if (row > Rows - 2) row = Rows - 2;

            double fx = gx - col;
            double fz = gz - row;

            double z00 = _elevations[row * Columns + col];
            double z10 = _elevations[row * Columns + col + 1];
            double z01 = _elevations[(row + 1) * Columns + col];
            double z11 = _elevations[(row + 1) * Columns + col + 1];

            if (fz >= fx)
            {
                elevation = z00 + fz * (z01 - z00) + fx * (z11 - z01);
            }
            else
            {
                elevation = z00 + fx * (z10 - z00) + fz * (z11 - z10);
            }

            // 삼각형의 세 꼭짓점 중 하나라도 빈 값(NaN)이면 계산 결과도 NaN이 된다.
            // 그 삼각형은 화면에도 그리지 않으므로 "지형 없음"으로 처리한다.
            if (double.IsNaN(elevation)) return false;
            return true;
        }

        /// <summary>col번째 열의 동쪽 좌표 (m)</summary>
        public double GetEasting(int col)
        {
            return OriginEasting + col * CellSize;
        }

        /// <summary>row번째 행의 북쪽 좌표 (m)</summary>
        public double GetNorthing(int row)
        {
            return OriginNorthing + row * CellSize;
        }
    }
}
