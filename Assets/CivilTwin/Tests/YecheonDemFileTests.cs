using System.IO;
using CivilTwin.Core;
using NUnit.Framework;
using UnityEngine;

namespace CivilTwin.Tests
{
    /// <summary>
    /// 예천양수발전소 DEM 파일을 우리 코드로 읽은 결과를 GDAL이 알려 준 값과 비교한다.
    /// 파일이 없는 PC에서는 건너뛴다 (DEM 파일은 Git에 넣지 않는다).
    ///
    /// 대상: Data/sites/dlenc_yecheon.asc
    ///   원본  : Copernicus DEM GLO-30, 타일 N36 E128
    ///   변환  : gdalwarp → EPSG:5186, 칸 크기 30 m, 197 × 189 칸
    ///   머리말: xllcorner 322740, yllcorner 459690, cellsize 30
    ///
    /// GDAL이 알려 준 값 (2026-11-15):
    ///   상부 저수지 수면 한가운데 (경도 128.3917, 위도 36.7423) = 동 324297.851, 북 461317.799, 표고 708.50 m
    ///   하부 저수지 수면 한가운데 (경도 128.4238, 위도 36.7603) = 동 327135.278, 북 463357.639, 표고 233.00 m
    ///
    /// 손계산 (상부):
    ///   열 번호 = (324297.851 - 322740) / 30 = 51.9 → 51
    ///   행 번호 = (461317.799 - 459690) / 30 = 54.3 → 54 (남쪽부터)
    ///   칸 중심 = 동 322740 + 51.5 × 30 = 324285,  북 459690 + 54.5 × 30 = 461325
    /// 손계산 (하부):
    ///   열 번호 = (327135.278 - 322740) / 30 = 146.5 → 146
    ///   행 번호 = (463357.639 - 459690) / 30 = 122.3 → 122
    ///   칸 중심 = 동 322740 + 146.5 × 30 = 327135,  북 459690 + 122.5 × 30 = 463365
    /// 수면 높이 차 = 708.50 - 233.00 = 475.50 m
    /// </summary>
    public class YecheonDemFileTests
    {
        private static ElevationGrid LoadOrSkip()
        {
            string path = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "Data", "sites", "dlenc_yecheon.asc"));

            if (!File.Exists(path))
            {
                Assert.Ignore("DEM 파일이 없어 건너뜀: " + path);
            }
            return AscGridReader.ReadFile(path);
        }

        [Test]
        public void 예천_파일의_크기와_위치가_GDAL_정보와_같다()
        {
            ElevationGrid grid = LoadOrSkip();

            Assert.AreEqual(197, grid.Columns);
            Assert.AreEqual(189, grid.Rows);
            Assert.AreEqual(30.0, grid.CellSize, 1e-9);

            // 남서쪽 격자점 = 모서리 + 반 칸 = (322740 + 15, 459690 + 15)
            Assert.AreEqual(322755.0, grid.OriginEasting, 1e-6);
            Assert.AreEqual(459705.0, grid.OriginNorthing, 1e-6);

            Assert.AreEqual(0, grid.NoDataCount);
        }

        [Test]
        public void 예천_파일의_최저_최고_표고가_GDAL_통계와_같다()
        {
            ElevationGrid grid = LoadOrSkip();

            // GDAL 통계: 최저 132.690, 최고 821.397. 파일에는 소수 둘째 자리까지 적혀 있다.
            Assert.AreEqual(132.69, grid.MinElevation, 0.01);
            Assert.AreEqual(821.40, grid.MaxElevation, 0.01);
        }

        [Test]
        public void 상부와_하부_저수지_수면_표고가_GDAL_값과_같다()
        {
            ElevationGrid grid = LoadOrSkip();

            // 상부 저수지
            Assert.AreEqual(324285.0, grid.GetEasting(51), 1e-6);
            Assert.AreEqual(461325.0, grid.GetNorthing(54), 1e-6);
            Assert.AreEqual(708.50, grid.GetElevation(51, 54), 0.01);

            // 하부 저수지
            Assert.AreEqual(327135.0, grid.GetEasting(146), 1e-6);
            Assert.AreEqual(463365.0, grid.GetNorthing(122), 1e-6);
            Assert.AreEqual(233.00, grid.GetElevation(146, 122), 0.01);

            // 두 수면의 높이 차 (양수발전의 낙차에 해당하는 값)
            Assert.AreEqual(475.50, grid.GetElevation(51, 54) - grid.GetElevation(146, 122), 0.02);
        }

        [Test]
        public void 상부와_하부_저수지가_평평한_면으로_잡힌다()
        {
            ElevationGrid grid = LoadOrSkip();

            bool[] flat = FlatSurfaceDetector.FindFlatNodes(grid);

            int upper = 0;
            int lower = 0;
            for (int row = 0; row < grid.Rows; row++)
            {
                for (int col = 0; col < grid.Columns; col++)
                {
                    if (!flat[row * grid.Columns + col]) continue;

                    double z = grid.GetElevation(col, row);
                    if (System.Math.Abs(z - 708.5) < 0.005) upper++;
                    if (System.Math.Abs(z - 233.0) < 0.005) lower++;
                }
            }

            Debug.Log($"[CivilTwin] 예천: 평평한 면 {FlatSurfaceDetector.Count(flat)}개, 708.50 m {upper}개, 233.00 m {lower}개");

            // 점 하나가 900 m². 100개면 9만 m²로, 저수지라면 넉넉히 넘어야 하는 크기다.
            Assert.Greater(upper, 100);
            Assert.Greater(lower, 100);
        }
    }
}
