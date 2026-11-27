using System;
using System.Collections.Generic;

namespace CivilTwin.Core
{
    /// <summary>
    /// 댐 화면의 저수지(물) 표시 (2026-11-20). 계산(체적·공기)에는 쓰지 않고 보이는 모양과 색만 정한다.
    ///
    /// 위성 지형(DSM)에서 지금의 호수는 수면이 평평하게 찍혀 있다(FlatSurfaceDetector). 호수 바닥은 위성 자료에 없다.
    /// 그래서 댐 바로 위(복원한 계곡 띠)만 계곡으로 파 두면, 띠 끝에서 호수 수면과 계곡 바닥 사이가 절벽처럼 보인다.
    ///   - 완공 화면: 댐 상류의 수면보다 낮은 곳을 지금 수면 높이의 물로 채운다 (실제 완공 후 모습과 같다).
    ///   - 공사 전·공사 중 화면: 띠 밖의 호수는 "지금의 소양호(바닥 모름)"로 물색만 칠한다.
    /// </summary>
    public static class DamReservoir
    {
        /// <summary>
        /// 지금 수면 높이: 댐 상류(d &gt; 0) 일정 거리 안의 평평한 격자점(호수 수면) 표고의 가운데 값.
        /// 평평한 점이 없으면 NaN.
        /// </summary>
        public static double FindLakeLevel(ElevationGrid surveyed, bool[] flat, DamSection dam, double searchDistance)
        {
            if (surveyed == null) throw new ArgumentNullException(nameof(surveyed));
            if (flat == null) throw new ArgumentNullException(nameof(flat));
            if (dam == null) throw new ArgumentNullException(nameof(dam));

            var levels = new List<double>();
            for (int row = 0; row < surveyed.Rows; row++)
            {
                double northing = surveyed.GetNorthing(row);
                for (int col = 0; col < surveyed.Columns; col++)
                {
                    int i = row * surveyed.Columns + col;
                    if (!flat[i]) continue;
                    dam.ToAxis(surveyed.GetEasting(col), northing, out double s, out double d);
                    if (d <= 0.0 || d > searchDistance || Math.Abs(s) > searchDistance) continue;
                    double z = surveyed.GetElevation(col, row);
                    if (!double.IsNaN(z)) levels.Add(z);
                }
            }
            if (levels.Count == 0) return double.NaN;
            levels.Sort();
            return levels[levels.Count / 2];
        }

        /// <summary>
        /// 물 표시. shown = 지금 보이는 면, ground = 공사 전 계곡(복원), surveyed = 위성 지형.
        /// fillUpstream = true(완공)이면 댐 상류의 수면보다 낮은 곳을 수면 높이로 올리고 물로 표시한다.
        /// false이면 복원하지 않은(= 위성 그대로인) 호수 수면만 물로 표시한다.
        /// </summary>
        public static ElevationGrid Apply(ElevationGrid shown, ElevationGrid ground, ElevationGrid surveyed, bool[] flat,
            DamSection dam, double lakeLevel, bool fillUpstream, out bool[] water)
        {
            if (shown == null) throw new ArgumentNullException(nameof(shown));
            if (ground == null) throw new ArgumentNullException(nameof(ground));
            if (surveyed == null) throw new ArgumentNullException(nameof(surveyed));
            if (flat == null) throw new ArgumentNullException(nameof(flat));
            if (dam == null) throw new ArgumentNullException(nameof(dam));

            int columns = shown.Columns;
            var z = new double[columns * shown.Rows];
            water = new bool[z.Length];
            for (int row = 0; row < shown.Rows; row++)
            {
                double northing = shown.GetNorthing(row);
                for (int col = 0; col < columns; col++)
                {
                    int i = row * columns + col;
                    double h = shown.GetElevation(col, row);
                    bool untouched = Math.Abs(ground.GetElevation(col, row) - surveyed.GetElevation(col, row)) < 0.01;
                    if (flat[i] && untouched)
                    {
                        water[i] = true;                          // 위성에 찍힌 지금의 호수
                    }
                    else if (fillUpstream && !double.IsNaN(lakeLevel) && h < lakeLevel)
                    {
                        dam.ToAxis(shown.GetEasting(col), northing, out _, out double d);
                        if (d > 0.0 && !untouched)
                        {
                            h = lakeLevel;                        // 댐 상류의 파 둔 계곡 → 물로 채움
                            water[i] = true;
                        }
                    }
                    z[i] = h;
                }
            }
            return new ElevationGrid(columns, shown.Rows, shown.CellSize, shown.OriginEasting, shown.OriginNorthing, z);
        }
    }
}
