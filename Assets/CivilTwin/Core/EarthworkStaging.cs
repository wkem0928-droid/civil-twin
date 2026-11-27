using System;

namespace CivilTwin.Core
{
    /// <summary>
    /// 공사 도중의 지형 (4D): 원지반에서 계획면으로 가는 중간 단계의 표고를 만든다.
    ///
    /// 시공 순서의 가정:
    ///   절토는 위에서부터 깎아 내려간다.  "절토 기준 높이"보다 높은 흙을 먼저 걷어 낸다.
    ///   성토는 아래에서부터 쌓아 올라간다. "성토 기준 높이"보다 낮은 곳을 먼저 채운다.
    ///
    /// 중간 단계의 표고 (격자점마다):
    ///   깎는 곳 (계획면 &lt; 원지반): 표고 = max( 계획면, min(원지반, 절토 기준 높이) )
    ///   쌓는 곳 (계획면 &gt; 원지반): 표고 = min( 계획면, max(원지반, 성토 기준 높이) )
    ///   손대지 않는 곳            : 원지반 그대로
    ///
    /// 손계산 예: 원지반 60, 계획면 50 (10 m 깎는 곳)
    ///   절토 기준 높이 70 → min(60, 70) = 60 → max(50, 60) = 60   (아직 시작 전)
    ///   절토 기준 높이 55 → min(60, 55) = 55 → max(50, 55) = 55   (5 m 깎은 상태)
    ///   절토 기준 높이 40 → min(60, 40) = 40 → max(50, 40) = 50   (완료. 계획면 아래로는 안 깎는다)
    ///
    /// 날짜와 연결하는 방법:
    ///   공정 계산(EarthworkSchedule)이 "d일째까지 누적 절토량"을 알려 주면,
    ///   그만큼 깎인 상태가 되는 절토 기준 높이를 이분법으로 찾는다 (성토도 같다).
    ///   기준 높이를 내릴수록 깎은 양이 늘기만 하므로 이분법을 쓸 수 있다.
    ///
    /// 한계: 실제 시공은 장비 동선, 운반로, 층 두께에 따라 순서가 정해진다.
    ///       여기서는 "높이 순서"라는 단순한 가정만 쓴다.
    /// </summary>
    public sealed class EarthworkStaging
    {
        private const int BisectionSteps = 50;

        private readonly ElevationGrid _ground;
        private readonly ElevationGrid _design;

        // 토공이 있는 칸의 범위 (계산을 이 범위로 줄여 빠르게 한다)
        private readonly int _colMin;
        private readonly int _colMax;
        private readonly int _rowMin;
        private readonly int _rowMax;

        /// <summary>깎는 곳이 있는가</summary>
        public bool HasCut { get; }

        /// <summary>쌓는 곳이 있는가</summary>
        public bool HasFill { get; }

        /// <summary>깎는 곳 가운데 가장 높은 원지반 표고 (절토를 시작하는 높이)</summary>
        public double CutTop { get; }

        /// <summary>깎는 곳 가운데 가장 낮은 계획면 표고 (절토가 끝나는 높이)</summary>
        public double CutBottom { get; }

        /// <summary>쌓는 곳 가운데 가장 낮은 원지반 표고 (성토를 시작하는 높이)</summary>
        public double FillBottom { get; }

        /// <summary>쌓는 곳 가운데 가장 높은 계획면 표고 (성토가 끝나는 높이)</summary>
        public double FillTop { get; }

        /// <summary>전체 토공량 (완료 상태)</summary>
        public EarthworkResult Total { get; }

        public EarthworkStaging(ElevationGrid ground, ElevationGrid design)
        {
            if (ground == null) throw new ArgumentNullException(nameof(ground));
            if (design == null) throw new ArgumentNullException(nameof(design));
            if (ground.Columns != design.Columns || ground.Rows != design.Rows ||
                ground.CellSize != design.CellSize)
            {
                throw new ArgumentException("원지반과 계획면의 격자가 다르다.", nameof(design));
            }

            _ground = ground;
            _design = design;

            int colMin = int.MaxValue, colMax = -1, rowMin = int.MaxValue, rowMax = -1;
            double cutTop = double.NegativeInfinity, cutBottom = double.PositiveInfinity;
            double fillBottom = double.PositiveInfinity, fillTop = double.NegativeInfinity;

            for (int row = 0; row < ground.Rows; row++)
            {
                for (int col = 0; col < ground.Columns; col++)
                {
                    double g = ground.GetElevation(col, row);
                    double d = design.GetElevation(col, row);
                    bool cut = d < g;
                    bool fill = d > g;
                    if (!cut && !fill) continue;   // 같거나 빈 값

                    if (cut)
                    {
                        if (g > cutTop) cutTop = g;
                        if (d < cutBottom) cutBottom = d;
                    }
                    else
                    {
                        if (g < fillBottom) fillBottom = g;
                        if (d > fillTop) fillTop = d;
                    }

                    if (col < colMin) colMin = col;
                    if (col > colMax) colMax = col;
                    if (row < rowMin) rowMin = row;
                    if (row > rowMax) rowMax = row;
                }
            }

            HasCut = !double.IsInfinity(cutTop);
            HasFill = !double.IsInfinity(fillTop);
            CutTop = cutTop;
            CutBottom = cutBottom;
            FillBottom = fillBottom;
            FillTop = fillTop;

            if (colMax < 0)
            {
                // 토공이 전혀 없다.
                _colMin = 0; _colMax = -1; _rowMin = 0; _rowMax = -1;
            }
            else
            {
                // 토공이 있는 격자점에 닿는 칸까지 포함하도록 한 칸씩 넓힌다.
                _colMin = Math.Max(0, colMin - 1);
                _colMax = Math.Min(ground.Columns - 2, colMax);
                _rowMin = Math.Max(0, rowMin - 1);
                _rowMax = Math.Min(ground.Rows - 2, rowMax);
            }

            Total = DoneVolumes(double.NegativeInfinity, double.PositiveInfinity);
        }

        /// <summary>격자점 하나의 중간 단계 표고.</summary>
        public static double StageElevation(double ground, double design, double cutLevel, double fillLevel)
        {
            if (design < ground)
            {
                return Math.Max(design, Math.Min(ground, cutLevel));
            }
            if (design > ground)
            {
                return Math.Min(design, Math.Max(ground, fillLevel));
            }
            return ground;   // 같거나, 빈 값(NaN)
        }

        /// <summary>
        /// 중간 단계의 지형 격자.
        /// 절토 기준 높이가 아주 높고(+∞) 성토 기준 높이가 아주 낮으면(−∞) 원지반,
        /// 반대로 절토 −∞, 성토 +∞ 이면 계획면이 된다.
        /// </summary>
        public ElevationGrid BuildStageGrid(double cutLevel, double fillLevel)
        {
            int columns = _ground.Columns;
            int rows = _ground.Rows;
            var z = new double[columns * rows];
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < columns; col++)
                {
                    z[row * columns + col] = StageElevation(
                        _ground.GetElevation(col, row), _design.GetElevation(col, row), cutLevel, fillLevel);
                }
            }
            return new ElevationGrid(columns, rows, _ground.CellSize, _ground.OriginEasting, _ground.OriginNorthing, z);
        }

        /// <summary>
        /// 그 단계까지 작업한 토공량 (원지반과 중간 단계 지형의 차이).
        /// EarthworkVolume.Compute(원지반, BuildStageGrid(...))와 같은 값이다.
        /// </summary>
        public EarthworkResult DoneVolumes(double cutLevel, double fillLevel)
        {
            double triangleArea = _ground.CellSize * _ground.CellSize / 2.0;
            var sum = new EarthworkVolume.Accumulator();

            for (int row = _rowMin; row <= _rowMax; row++)
            {
                for (int col = _colMin; col <= _colMax; col++)
                {
                    double h00 = Difference(col, row, cutLevel, fillLevel);
                    double h10 = Difference(col + 1, row, cutLevel, fillLevel);
                    double h01 = Difference(col, row + 1, cutLevel, fillLevel);
                    double h11 = Difference(col + 1, row + 1, cutLevel, fillLevel);

                    sum.AddTriangle(h00, h01, h11, triangleArea);
                    sum.AddTriangle(h00, h10, h11, triangleArea);
                }
            }

            return new EarthworkResult(sum.Cut, sum.Fill, sum.CutArea, sum.FillArea);
        }

        private double Difference(int col, int row, double cutLevel, double fillLevel)
        {
            double g = _ground.GetElevation(col, row);
            return StageElevation(g, _design.GetElevation(col, row), cutLevel, fillLevel) - g;
        }

        /// <summary>
        /// 누적 절토량이 targetVolume이 되는 절토 기준 높이를 찾는다 (이분법).
        /// 0 이하이면 시작 전(CutTop), 전체 절토량 이상이면 완료(CutBottom).
        /// 깎는 곳이 없으면 +∞ (아무것도 깎지 않는다).
        /// </summary>
        public double FindCutLevel(double targetVolume)
        {
            if (!HasCut) return double.PositiveInfinity;
            if (targetVolume <= 0) return CutTop;
            if (targetVolume >= Total.CutVolume) return CutBottom;

            double low = CutBottom;   // 여기서는 전부 깎은 상태 (양이 많다)
            double high = CutTop;     // 여기서는 하나도 안 깎은 상태 (양이 0)
            for (int i = 0; i < BisectionSteps; i++)
            {
                double mid = 0.5 * (low + high);
                double done = DoneVolumes(mid, double.NegativeInfinity).CutVolume;
                if (done > targetVolume) low = mid;   // 너무 많이 깎았다 → 기준 높이를 올린다
                else high = mid;
            }
            return 0.5 * (low + high);
        }

        /// <summary>
        /// 누적 성토량이 targetVolume이 되는 성토 기준 높이를 찾는다 (이분법).
        /// 0 이하이면 시작 전(FillBottom), 전체 성토량 이상이면 완료(FillTop).
        /// 쌓는 곳이 없으면 −∞ (아무것도 쌓지 않는다).
        /// </summary>
        public double FindFillLevel(double targetVolume)
        {
            if (!HasFill) return double.NegativeInfinity;
            if (targetVolume <= 0) return FillBottom;
            if (targetVolume >= Total.FillVolume) return FillTop;

            double low = FillBottom;  // 하나도 안 쌓은 상태 (양이 0)
            double high = FillTop;    // 전부 쌓은 상태
            for (int i = 0; i < BisectionSteps; i++)
            {
                double mid = 0.5 * (low + high);
                double done = DoneVolumes(double.PositiveInfinity, mid).FillVolume;
                if (done < targetVolume) low = mid;   // 아직 모자란다 → 기준 높이를 올린다
                else high = mid;
            }
            return 0.5 * (low + high);
        }
    }
}
