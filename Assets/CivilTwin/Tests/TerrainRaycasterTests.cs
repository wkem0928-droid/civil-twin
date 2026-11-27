using CivilTwin.Core;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>
    /// 시선이 지형과 만나는 점을 손계산과 비교한다.
    /// 모든 격자는 101 × 101, 간격 5 m, 남서쪽 모서리 = 동 200000, 북 600000.
    ///
    /// 손계산 1 (평지, 표고 10 m):
    ///   시작 (200050, 600050, 110), 방향 (동 1, 북 0, 위 -1) = 45° 내리막
    ///   높이 100 m 를 내려가는 동안 동쪽으로 100 m 전진
    ///   → 만나는 점 (200150, 600050, 10)
    ///
    /// 손계산 2 (동쪽으로 10 % 오르막, 표고 = 0.1 × (동 - 200000)):
    ///   시작 (200100, 600250, 60), 방향 (1, 0, -1). 동쪽으로 s 만큼 갔을 때
    ///     시선 높이 = 60 - s
    ///     지형 표고 = 0.1 × (100 + s) = 10 + 0.1 s
    ///   60 - s = 10 + 0.1 s → s = 50 / 1.1 = 45.454545
    ///   → 만나는 점 동 200145.454545, 표고 14.545455
    /// </summary>
    public class TerrainRaycasterTests
    {
        private const double Tolerance = 1e-6;
        private const double MaxDistance = 5000.0;

        private static ElevationGrid CreateFlat(double elevation)
        {
            var values = new double[101 * 101];
            for (int i = 0; i < values.Length; i++) values[i] = elevation;
            return new ElevationGrid(101, 101, 5.0, 200000.0, 600000.0, values);
        }

        private static ElevationGrid CreateEastwardSlope()
        {
            var values = new double[101 * 101];
            for (int row = 0; row < 101; row++)
            {
                for (int col = 0; col < 101; col++)
                {
                    values[row * 101 + col] = 0.1 * (col * 5.0);
                }
            }
            return new ElevationGrid(101, 101, 5.0, 200000.0, 600000.0, values);
        }

        private static ElevationGrid CreateHill()
        {
            return new GaussianHill(200350.0, 600150.0, 10.0, 50.0, 100.0)
                .CreateGrid(101, 101, 5.0, 200000.0, 600000.0);
        }

        [Test]
        public void 평지에_45도로_내려가는_시선()
        {
            var origin = new SurveyPoint(200050.0, 600050.0, 110.0);

            bool found = TerrainRaycaster.TryHit(CreateFlat(10.0), origin, 1, 0, -1, MaxDistance, out SurveyPoint hit);

            Assert.IsTrue(found);
            Assert.AreEqual(200150.0, hit.Easting, Tolerance);
            Assert.AreEqual(600050.0, hit.Northing, Tolerance);
            Assert.AreEqual(10.0, hit.Elevation, Tolerance);
        }

        [Test]
        public void 경사면에_45도로_내려가는_시선()
        {
            var origin = new SurveyPoint(200100.0, 600250.0, 60.0);

            bool found = TerrainRaycaster.TryHit(CreateEastwardSlope(), origin, 1, 0, -1, MaxDistance, out SurveyPoint hit);

            Assert.IsTrue(found);
            Assert.AreEqual(200145.454545, hit.Easting, Tolerance);
            Assert.AreEqual(600250.0, hit.Northing, Tolerance);
            Assert.AreEqual(14.545455, hit.Elevation, Tolerance);
        }

        [Test]
        public void 언덕_꼭대기를_수직으로_내려다보면_표고_60m다()
        {
            var origin = new SurveyPoint(200350.0, 600150.0, 1000.0);

            bool found = TerrainRaycaster.TryHit(CreateHill(), origin, 0, 0, -1, MaxDistance, out SurveyPoint hit);

            Assert.IsTrue(found);
            Assert.AreEqual(200350.0, hit.Easting, Tolerance);
            Assert.AreEqual(600150.0, hit.Northing, Tolerance);
            Assert.AreEqual(60.0, hit.Elevation, Tolerance);
        }

        [Test]
        public void 수직으로_내려다본_점의_표고는_보간_표고와_같다()
        {
            ElevationGrid grid = CreateHill();
            grid.TryGetElevationAt(200287.3, 600193.7, out double expected);

            var origin = new SurveyPoint(200287.3, 600193.7, 500.0);
            bool found = TerrainRaycaster.TryHit(grid, origin, 0, 0, -1, MaxDistance, out SurveyPoint hit);

            Assert.IsTrue(found);
            Assert.AreEqual(expected, hit.Elevation, Tolerance);
        }

        [Test]
        public void 비스듬한_시선이_언덕과_만나는_점은_시선_위에_있고_지표면_위에_있다()
        {
            ElevationGrid grid = CreateHill();
            var origin = new SurveyPoint(200000.0, 600000.0, 150.0);

            // 남서쪽 모서리 위 150 m 에서 언덕 쪽(동북동 아래)을 본다.
            bool found = TerrainRaycaster.TryHit(grid, origin, 350, 150, -110, MaxDistance, out SurveyPoint hit);
            Assert.IsTrue(found);

            // 지표면 위: 만나는 점의 표고 = 그 위치의 보간 표고
            grid.TryGetElevationAt(hit.Easting, hit.Northing, out double ground);
            Assert.AreEqual(ground, hit.Elevation, Tolerance);

            // 시선 위: 동쪽으로 간 비율만큼 북쪽과 높이도 변했다.
            double ratio = (hit.Easting - origin.Easting) / 350.0;
            Assert.AreEqual(origin.Northing + 150.0 * ratio, hit.Northing, 1e-5);
            Assert.AreEqual(origin.Elevation - 110.0 * ratio, hit.Elevation, 1e-5);
        }

        [Test]
        public void 격자_밖에서_시작해_안으로_들어오는_시선()
        {
            // 서쪽 50 m 밖에서 시작. 높이 100 m 를 내려가는 동안 동쪽으로 100 m → 동 200050
            var origin = new SurveyPoint(199950.0, 600250.0, 110.0);

            bool found = TerrainRaycaster.TryHit(CreateFlat(10.0), origin, 1, 0, -1, MaxDistance, out SurveyPoint hit);

            Assert.IsTrue(found);
            Assert.AreEqual(200050.0, hit.Easting, Tolerance);
            Assert.AreEqual(10.0, hit.Elevation, Tolerance);
        }

        [Test]
        public void 하늘을_향한_시선은_만나지_않는다()
        {
            var origin = new SurveyPoint(200250.0, 600250.0, 100.0);

            Assert.IsFalse(TerrainRaycaster.TryHit(CreateHill(), origin, 1, 0, 0.5, MaxDistance, out _));
        }

        [Test]
        public void 격자_밖을_내려다보는_시선은_만나지_않는다()
        {
            var origin = new SurveyPoint(190000.0, 600250.0, 100.0);

            Assert.IsFalse(TerrainRaycaster.TryHit(CreateHill(), origin, 0, 0, -1, MaxDistance, out _));
        }

        [Test]
        public void 최대_거리보다_먼_지형은_만나지_않는다()
        {
            // 만나는 점까지의 거리 = 100 × √2 = 141.42 m
            var origin = new SurveyPoint(200050.0, 600050.0, 110.0);

            Assert.IsFalse(TerrainRaycaster.TryHit(CreateFlat(10.0), origin, 1, 0, -1, 100.0, out _));
            Assert.IsTrue(TerrainRaycaster.TryHit(CreateFlat(10.0), origin, 1, 0, -1, 150.0, out _));
        }

        [Test]
        public void 방향이_0이면_만나지_않는다()
        {
            var origin = new SurveyPoint(200250.0, 600250.0, 100.0);

            Assert.IsFalse(TerrainRaycaster.TryHit(CreateHill(), origin, 0, 0, 0, MaxDistance, out _));
        }
    }
}
