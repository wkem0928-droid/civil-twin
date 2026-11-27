using System.IO;
using CivilTwin.Core;
using NUnit.Framework;
using UnityEngine;

namespace CivilTwin.Tests
{
    /// <summary>
    /// 실제 DEM 파일을 우리 코드로 읽은 결과를, 그 파일을 만든 도구(GDAL)가 알려 준 값과 비교한다.
    /// 우리 읽기 코드가 남북 뒤집기와 반 칸 이동을 실제 파일에서도 맞게 하는지 확인하는 것이 목적이다.
    ///
    /// DEM 파일은 용량과 이용 조건 때문에 Git에 넣지 않는다.
    /// 그래서 파일이 없는 PC에서는 이 시험을 건너뛴다(실패로 치지 않는다).
    ///
    /// 대상: Data/sites/hyundai_soyanggang.asc
    ///   원본  : Copernicus DEM GLO-30, 타일 N37 E127
    ///   변환  : gdalwarp → EPSG:5186, 칸 크기 30 m, 136 × 136 칸
    ///   머리말: xllcorner 269550, yllcorner 592230, cellsize 30
    ///
    /// GDAL이 알려 준 값 (2026-11-14):
    ///   소양강댐 지점 (경도 127.8144, 위도 37.9456) = 동 271584.158, 북 594274.667
    ///   그 지점이 속한 칸의 표고 = 197.03 m
    ///
    /// 손계산:
    ///   그 칸의 열 번호 = (271584.158 - 269550) / 30 = 67.8 → 67
    ///   그 칸의 행 번호 = (594274.667 - 592230) / 30 = 68.2 → 68 (남쪽부터)
    ///   칸 중심 = 동 269550 + 67.5 × 30 = 271575,  북 592230 + 68.5 × 30 = 594285
    /// </summary>
    public class RealDemFileTests
    {
        private static ElevationGrid LoadOrSkip()
        {
            string path = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "Data", "sites", "hyundai_soyanggang.asc"));

            if (!File.Exists(path))
            {
                Assert.Ignore("DEM 파일이 없어 건너뜀: " + path);
            }
            return AscGridReader.ReadFile(path);
        }

        [Test]
        public void 소양강댐_파일의_크기와_위치가_GDAL_정보와_같다()
        {
            ElevationGrid grid = LoadOrSkip();

            Assert.AreEqual(136, grid.Columns);
            Assert.AreEqual(136, grid.Rows);
            Assert.AreEqual(30.0, grid.CellSize, 1e-9);

            // 남서쪽 격자점 = 모서리 + 반 칸 = (269550 + 15, 592230 + 15)
            Assert.AreEqual(269565.0, grid.OriginEasting, 1e-6);
            Assert.AreEqual(592245.0, grid.OriginNorthing, 1e-6);

            Assert.AreEqual(0, grid.NoDataCount);
        }

        [Test]
        public void 소양강댐_파일의_최저_최고_표고가_GDAL_통계와_같다()
        {
            ElevationGrid grid = LoadOrSkip();

            // GDAL 통계: 최저 75.5, 최고 654.726. 파일에는 소수 둘째 자리까지 적혀 있다.
            Assert.AreEqual(75.5, grid.MinElevation, 0.01);
            Assert.AreEqual(654.73, grid.MaxElevation, 0.01);
        }

        [Test]
        public void 소양호_수면이_평평한_면으로_잡힌다()
        {
            ElevationGrid grid = LoadOrSkip();

            bool[] flat = FlatSurfaceDetector.FindFlatNodes(grid);

            // 평평한 면 가운데 표고가 183.00 m(소양호 수면)인 점의 개수를 센다.
            int lake = 0;
            for (int row = 0; row < grid.Rows; row++)
            {
                for (int col = 0; col < grid.Columns; col++)
                {
                    if (flat[row * grid.Columns + col] &&
                        System.Math.Abs(grid.GetElevation(col, row) - 183.0) < 0.005)
                    {
                        lake++;
                    }
                }
            }

            Debug.Log($"[CivilTwin] 평평한 면 {FlatSurfaceDetector.Count(flat)}개, 그중 183.00 m인 점 {lake}개");

            // 점 하나가 30 m × 30 m = 900 m². 100개면 9만 m²로, 호수라면 넉넉히 넘어야 하는 크기다.
            Assert.Greater(lake, 100);
        }

        [Test]
        public void 소양강댐_지점이_속한_칸의_표고가_GDAL_값과_같다()
        {
            ElevationGrid grid = LoadOrSkip();

            Assert.AreEqual(271575.0, grid.GetEasting(67), 1e-6);
            Assert.AreEqual(594285.0, grid.GetNorthing(68), 1e-6);
            Assert.AreEqual(197.03, grid.GetElevation(67, 68), 0.01);

            // 남북을 뒤집지 않았다면 이 값은 다른 곳(row 67)의 표고가 된다.
            Assert.IsTrue(grid.TryGetElevationAt(271575.0, 594285.0, out double z));
            Assert.AreEqual(197.03, z, 0.01);
        }
    }
}
