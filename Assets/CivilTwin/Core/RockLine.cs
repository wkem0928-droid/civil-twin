using System;

namespace CivilTwin.Core
{
    /// <summary>절토를 토사와 암으로 나눈 결과 (자연 상태 m³).</summary>
    public readonly struct CutMaterials
    {
        /// <summary>토사 (암반선보다 위에서 깎는 흙)</summary>
        public readonly double SoilVolume;

        /// <summary>암 (암반선보다 아래에서 깎는 부분)</summary>
        public readonly double RockVolume;

        /// <summary>암을 깎는 수평 면적 (m²)</summary>
        public readonly double RockArea;

        /// <summary>원지반에서 암반선까지의 깊이 (m). 무한대면 암 없음.</summary>
        public readonly double RockDepth;

        public CutMaterials(double soilVolume, double rockVolume, double rockArea, double rockDepth)
        {
            SoilVolume = soilVolume;
            RockVolume = rockVolume;
            RockArea = rockArea;
            RockDepth = rockDepth;
        }

        public double TotalVolume => SoilVolume + RockVolume;

        /// <summary>절토 중 암의 비율 (0 ~ 1). 절토가 없으면 0.</summary>
        public double RockRatio => TotalVolume > 0.0 ? RockVolume / TotalVolume : 0.0;
    }

    /// <summary>
    /// 토사·암 구분 (Phase 5-3). 사용자가 "원지반에서 몇 m 아래부터 암"인지(암반선 깊이 D)를 입력하면
    /// 절토량을 토사와 암으로 나눈다.
    ///
    /// 방법: 암반선 = 원지반 − D 인 면이라고 본다. 그 면보다 아래에서 깎는 부분이 암이다.
    ///   암 부피 = (원지반 − D)와 계획면 사이의 절토량   ← 2-2의 삼각형 점고법을 그대로 쓴다
    ///   토사 부피 = 전체 절토량 − 암 부피
    /// 한 점에서 보면: 깎는 깊이 h에 대해 토사 = min(h, D), 암 = max(h − D, 0).
    ///
    /// 가정과 한계:
    ///   암반선이 원지반과 나란하다(어디서나 같은 깊이)고 본다. 실제 암반선은 시추 조사로 점마다 다르다.
    ///   풍화암·연암·경암을 나누지 않는다. 암의 작업량·토량환산계수는 5-4에서 다룬다.
    /// </summary>
    public static class RockLine
    {
        /// <summary>암이 없음을 뜻하는 깊이</summary>
        public const double None = double.PositiveInfinity;

        public static CutMaterials Split(ElevationGrid ground, ElevationGrid design, double rockDepth)
        {
            if (ground == null) throw new ArgumentNullException(nameof(ground));
            if (design == null) throw new ArgumentNullException(nameof(design));
            if (double.IsNaN(rockDepth) || rockDepth < 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(rockDepth), "암반선 깊이는 0 이상이어야 한다.");
            }

            double total = EarthworkVolume.Compute(ground, design).CutVolume;
            if (double.IsPositiveInfinity(rockDepth))
            {
                return new CutMaterials(total, 0.0, 0.0, rockDepth);
            }

            EarthworkResult rock = EarthworkVolume.Compute(Shift(ground, -rockDepth), design);
            double soil = Math.Max(0.0, total - rock.CutVolume);
            return new CutMaterials(soil, rock.CutVolume, rock.CutArea, rockDepth);
        }

        /// <summary>모든 표고에 같은 값을 더한 격자 (빈 값은 그대로).</summary>
        private static ElevationGrid Shift(ElevationGrid grid, double delta)
        {
            var values = new double[grid.Columns * grid.Rows];
            for (int row = 0; row < grid.Rows; row++)
            {
                for (int col = 0; col < grid.Columns; col++)
                {
                    values[row * grid.Columns + col] = grid.GetElevation(col, row) + delta;   // NaN + delta = NaN
                }
            }
            return new ElevationGrid(grid.Columns, grid.Rows, grid.CellSize, grid.OriginEasting, grid.OriginNorthing, values);
        }
    }
}
