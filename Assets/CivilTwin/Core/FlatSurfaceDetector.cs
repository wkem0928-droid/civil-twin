using System;

namespace CivilTwin.Core
{
    /// <summary>
    /// 격자에서 "완전히 평평한 면"을 찾는다. 호수 수면을 추정하는 데 쓴다.
    ///
    /// 원리:
    ///   위성 DEM은 호수 수면을 높이 하나로 평평하게 적어 둔다 (예: 소양호 183.00 m).
    ///   자연 지면은 이웃한 점끼리 높이가 정확히 같은 일이 거의 없다.
    ///   그래서 "자기와 둘레 8개 점의 높이가 모두 같은 점"을 평평한 면으로 본다.
    ///
    /// 한계:
    ///   - 이것은 추정이다. 수면이라는 정보가 DEM에 들어 있는 것이 아니다.
    ///   - 흐르는 강은 경사가 있어 잡히지 않는다.
    ///   - 호수 가장자리 한 줄은 둘레에 비탈이 있어 잡히지 않는다 (호수가 격자 한 칸만큼 작게 나온다).
    ///   - 매립지처럼 사람이 평평하게 만든 땅이 같은 값으로 적혀 있으면 수면으로 잘못 잡힐 수 있다.
    /// </summary>
    public static class FlatSurfaceDetector
    {
        /// <summary>표고가 같다고 보는 기본 허용 차이: 0.001 mm. 사실상 "값이 똑같다"는 뜻이다.</summary>
        public const double DefaultTolerance = 1e-6;

        /// <summary>
        /// 격자점마다 평평한 면인지 표시한 배열을 돌려준다. 번호는 격자와 같다 (row × Columns + col).
        /// 격자 가장자리의 점은 격자 안에 있는 이웃만 본다.
        /// 자기나 이웃 중 하나라도 빈 값이면 평평한 면이 아니다.
        /// </summary>
        public static bool[] FindFlatNodes(ElevationGrid grid, double tolerance = DefaultTolerance)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (tolerance < 0.0) throw new ArgumentOutOfRangeException(nameof(tolerance));

            int columns = grid.Columns;
            int rows = grid.Rows;
            var flat = new bool[columns * rows];

            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < columns; col++)
                {
                    flat[row * columns + col] = IsFlat(grid, col, row, tolerance);
                }
            }

            return flat;
        }

        private static bool IsFlat(ElevationGrid grid, int col, int row, double tolerance)
        {
            double z = grid.GetElevation(col, row);
            if (double.IsNaN(z)) return false;

            for (int dr = -1; dr <= 1; dr++)
            {
                for (int dc = -1; dc <= 1; dc++)
                {
                    if (dr == 0 && dc == 0) continue;

                    int c = col + dc;
                    int r = row + dr;
                    if (c < 0 || c >= grid.Columns || r < 0 || r >= grid.Rows) continue;

                    double neighbor = grid.GetElevation(c, r);
                    if (double.IsNaN(neighbor)) return false;
                    if (Math.Abs(neighbor - z) > tolerance) return false;
                }
            }

            return true;
        }

        /// <summary>평평한 면으로 표시된 점의 개수.</summary>
        public static int Count(bool[] flat)
        {
            if (flat == null) throw new ArgumentNullException(nameof(flat));

            int count = 0;
            foreach (bool f in flat)
            {
                if (f) count++;
            }
            return count;
        }
    }
}
