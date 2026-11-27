using System.IO;
using CivilTwin.Core;
using NUnit.Framework;
using UnityEngine;

namespace CivilTwin.Tests
{
    /// <summary>
    /// 회사별 현장 DEM 파일을 우리 코드로 읽은 결과를 GDAL이 알려 준 값과 비교한다.
    /// 현장마다 같은 검사를 하므로, 검사 하나에 현장별 값을 한 줄씩 넣는 방식([TestCase])을 쓴다.
    /// 파일이 없는 PC에서는 건너뛴다 (DEM 파일은 Git에 넣지 않는다).
    ///
    /// 값의 출처와 만든 방법은 Docs/SITES.md의 "현장별 기록"에 있다 (2026-11-15, GDAL 3.x / QGIS 3.44.15).
    ///
    /// 손계산 방법 (모든 현장 공통):
    ///   남서쪽 격자점 = 모서리(xllcorner, yllcorner) + 반 칸(15 m)
    ///   어떤 지점이 속한 칸의 열 번호 = (동 − xllcorner) / 30 의 정수 부분
    ///   어떤 지점이 속한 칸의 행 번호 = (북 − yllcorner) / 30 의 정수 부분 (남쪽부터)
    ///   예) 부항댐 지점 동 290521.13, 북 376335.70, 모서리 (287280, 373260)
    ///       열 = 3241.13 / 30 = 108.04 → 108,  행 = 3075.70 / 30 = 102.5 → 102
    /// </summary>
    public class SiteDemFileTests
    {
        private static ElevationGrid LoadOrSkip(string fileName)
        {
            string path = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "Data", "sites", fileName));

            if (!File.Exists(path))
            {
                Assert.Ignore("DEM 파일이 없어 건너뜀: " + path);
            }
            return AscGridReader.ReadFile(path);
        }

        // 파일 이름, 열 수, 행 수, xllcorner, yllcorner, 최저 표고, 최고 표고
        [TestCase("samsung_cheongsong.asc", 214, 228, 377100.0, 421440.0, 182.19, 789.69)]
        [TestCase("gs_buhang.asc", 183, 165, 287280.0, 373260.0, 132.52, 473.04)]
        [TestCase("daewoo_inje_yangyang.asc", 459, 222, 321630.0, 594240.0, 124.54, 1112.85)]
        [TestCase("lotte_anyang_seongnam.asc", 217, 223, 200160.0, 529830.0, 54.16, 612.11)]
        [TestCase("posco_sanseong.asc", 248, 228, 383340.0, 292800.0, 0.50, 705.01)]
        [TestCase("hdc_seoul_chuncheon.asc", 414, 299, 244050.0, 562920.0, 48.96, 628.35)]
        [TestCase("landmark_incheon_bridge.asc", 505, 373, 155250.0, 531780.0, -10.00, 168.93)]   // 랜드마크 인천대교 (2026-11-27), 바다 = 0 m로 평평
        public void 파일의_크기_위치_표고_범위가_GDAL_정보와_같다(
            string fileName, int columns, int rows, double xllCorner, double yllCorner, double min, double max)
        {
            ElevationGrid grid = LoadOrSkip(fileName);

            Assert.AreEqual(columns, grid.Columns);
            Assert.AreEqual(rows, grid.Rows);
            Assert.AreEqual(30.0, grid.CellSize, 1e-9);

            // 남서쪽 격자점 = 모서리 + 반 칸
            Assert.AreEqual(xllCorner + 15.0, grid.OriginEasting, 1e-6);
            Assert.AreEqual(yllCorner + 15.0, grid.OriginNorthing, 1e-6);

            Assert.AreEqual(0, grid.NoDataCount);
            Assert.AreEqual(min, grid.MinElevation, 0.01);
            Assert.AreEqual(max, grid.MaxElevation, 0.01);
        }

        // 파일 이름, 열 번호, 행 번호, GDAL이 알려 준 표고, 그 지점이 무엇인지
        [TestCase("samsung_cheongsong.asc", 90, 74, 588.00, "상부 저수지(노래호) 수면")]
        [TestCase("samsung_cheongsong.asc", 127, 164, 258.00, "하부 저수지(청송호) 수면")]
        [TestCase("gs_buhang.asc", 108, 102, 192.51, "부항댐 지점")]
        [TestCase("gs_buhang.asc", 78, 113, 179.00, "부항호 수면")]
        [TestCase("daewoo_inje_yangyang.asc", 52, 117, 427.60, "인제양양터널 서쪽 입구")]
        [TestCase("daewoo_inje_yangyang.asc", 404, 161, 242.40, "인제양양터널 동쪽 입구")]
        [TestCase("daewoo_inje_yangyang.asc", 228, 139, 645.61, "인제양양터널 중간 지점의 지표")]
        [TestCase("posco_sanseong.asc", 43, 105, 97.43, "산성터널 서쪽 입구")]
        [TestCase("posco_sanseong.asc", 201, 97, 72.69, "산성터널 동쪽 입구")]
        [TestCase("posco_sanseong.asc", 122, 101, 377.89, "산성터널 중간 지점의 지표")]
        [TestCase("lotte_anyang_seongnam.asc", 46, 115, 196.41, "청계산3터널 서쪽 입구")]
        [TestCase("lotte_anyang_seongnam.asc", 137, 109, 220.39, "청계산3터널 동쪽 입구")]
        [TestCase("hdc_seoul_chuncheon.asc", 145, 162, 147.27, "미사터널 서쪽 입구")]
        [TestCase("hdc_seoul_chuncheon.asc", 354, 253, 119.04, "강촌 나들목")]
        public void 아는_지점의_표고가_GDAL_값과_같다(
            string fileName, int col, int row, double expected, string what)
        {
            ElevationGrid grid = LoadOrSkip(fileName);

            Assert.AreEqual(expected, grid.GetElevation(col, row), 0.01, what);

            // 격자점 좌표로 다시 물어도 같은 값이 나와야 한다 (남북 뒤집기 확인).
            Assert.IsTrue(grid.TryGetElevationAt(grid.GetEasting(col), grid.GetNorthing(row), out double z), what);
            Assert.AreEqual(expected, z, 0.01, what);
        }
    }
}
