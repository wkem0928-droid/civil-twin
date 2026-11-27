using System;

namespace CivilTwin.Core
{
    /// <summary>
    /// 직선(시선)이 지형과 만나는 점을 찾는다. 화면을 클릭한 지점의 좌표를 구할 때 쓴다.
    ///
    /// 방법:
    ///   1. 시선을 따라 "격자 간격의 절반"씩 전진한다.
    ///   2. 각 위치에서  차이 = 시선의 높이 - 지형의 표고  를 계산한다.
    ///   3. 차이가 양수(지형 위)에서 0 이하(지형 아래)로 바뀌는 구간을 찾는다.
    ///   4. 그 구간을 절반씩 좁혀 가며(이분법) 만나는 점을 정밀하게 구한다.
    ///      60번 반복하면 구간이 2^60분의 1로 줄어들어 오차는 사실상 0이다.
    ///
    /// 한계:
    ///   전진 간격보다 얇은 지형(폭이 격자 반 칸 미만인 뾰족한 능선)은
    ///   건너뛸 수 있다. 격자 지형에서는 한 칸보다 얇은 형상이 드물다.
    /// </summary>
    public static class TerrainRaycaster
    {
        private const int BisectionIterations = 60;

        /// <param name="grid">표고 격자</param>
        /// <param name="origin">시선의 시작점 (측량 좌표)</param>
        /// <param name="directionEast">시선 방향의 동쪽 성분</param>
        /// <param name="directionNorth">시선 방향의 북쪽 성분</param>
        /// <param name="directionUp">시선 방향의 위쪽 성분</param>
        /// <param name="maxDistance">찾을 최대 거리 (m)</param>
        /// <param name="hit">만나는 점 (측량 좌표). 표고는 지형 표고다.</param>
        public static bool TryHit(
            ElevationGrid grid,
            SurveyPoint origin,
            double directionEast,
            double directionNorth,
            double directionUp,
            double maxDistance,
            out SurveyPoint hit)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));

            hit = default;

            double length = Math.Sqrt(
                directionEast * directionEast +
                directionNorth * directionNorth +
                directionUp * directionUp);
            if (!(length > 0)) return false;

            double de = directionEast / length;
            double dn = directionNorth / length;
            double du = directionUp / length;

            double step = grid.CellSize * 0.5;
            bool hasPrevious = false;
            double previousT = 0.0;

            for (double t = 0.0; t <= maxDistance; t += step)
            {
                double height = origin.Elevation + du * t;

                // 가장 높은 지형보다 위에서 위쪽으로 가고 있으면 더 볼 필요가 없다.
                if (du >= 0 && height > grid.MaxElevation) return false;

                if (!TryDifference(grid, origin, de, dn, du, t, out double difference))
                {
                    // 격자 범위 밖 — 지형이 없다.
                    hasPrevious = false;
                    continue;
                }

                if (difference <= 0)
                {
                    // 직전 위치가 지형 위였을 때만 "위에서 아래로 뚫고 들어간" 것이다.
                    if (!hasPrevious) return false;

                    double low = previousT;  // 지형 위
                    double high = t;         // 지형 아래
                    for (int i = 0; i < BisectionIterations; i++)
                    {
                        double middle = 0.5 * (low + high);
                        if (TryDifference(grid, origin, de, dn, du, middle, out double d) && d > 0)
                        {
                            low = middle;
                        }
                        else
                        {
                            high = middle;
                        }
                    }

                    double easting = origin.Easting + de * high;
                    double northing = origin.Northing + dn * high;
                    if (!grid.TryGetElevationAt(easting, northing, out double elevation)) return false;

                    hit = new SurveyPoint(easting, northing, elevation);
                    return true;
                }

                hasPrevious = true;
                previousT = t;
            }

            return false;
        }

        /// <summary>시선 위 거리 t 지점에서 (시선 높이 - 지형 표고). 격자 밖이면 false.</summary>
        private static bool TryDifference(
            ElevationGrid grid, SurveyPoint origin,
            double de, double dn, double du, double t,
            out double difference)
        {
            double easting = origin.Easting + de * t;
            double northing = origin.Northing + dn * t;

            if (!grid.TryGetElevationAt(easting, northing, out double ground))
            {
                difference = double.NaN;
                return false;
            }

            difference = (origin.Elevation + du * t) - ground;
            return true;
        }
    }
}
