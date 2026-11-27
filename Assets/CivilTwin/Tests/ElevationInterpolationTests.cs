using System;
using CivilTwin.Core;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>
    /// 격자점 사이의 표고 보간(삼각형 평면 보간)을 손계산과 비교한다.
    ///
    /// 예제 칸 (간격 10 m, 남서쪽 모서리 = 0, 0):
    ///
    ///   z01 = 30 ---- z11 = 60        대각선은 남서(z00) → 북동(z11)
    ///      |        /   |
    ///      |     /      |
    ///   z00 = 10 ---- z10 = 20
    ///
    /// 손계산 1: 점 (2.5, 7.5) → fx = 0.25, fz = 0.75 → fz ≥ fx 이므로 북서쪽 삼각형
    ///   표고 = z00 + fz × (z01 - z00) + fx × (z11 - z01)
    ///        = 10 + 0.75 × 20 + 0.25 × 30 = 32.5
    ///
    /// 손계산 2: 점 (7.5, 2.5) → fx = 0.75, fz = 0.25 → 남동쪽 삼각형
    ///   표고 = z00 + fx × (z10 - z00) + fz × (z11 - z10)
    ///        = 10 + 0.75 × 10 + 0.25 × 40 = 27.5
    ///
    /// 손계산 3: 점 (5, 5) → 대각선 위. 두 식 모두 35 (경계에서 끊기지 않는다)
    ///   10 + 0.5 × 20 + 0.5 × 30 = 35      10 + 0.5 × 10 + 0.5 × 40 = 35
    /// </summary>
    public class ElevationInterpolationTests
    {
        private const double Tolerance = 1e-9;

        private static ElevationGrid CreateCell()
        {
            double[] elevations =
            {
                10, 20,   // row 0 (남): z00, z10
                30, 60,   // row 1 (북): z01, z11
            };
            return new ElevationGrid(2, 2, 10.0, 0.0, 0.0, elevations);
        }

        private static ElevationGrid CreateHillGrid()
        {
            return new GaussianHill(200350.0, 600150.0, 10.0, 50.0, 100.0)
                .CreateGrid(101, 101, 5.0, 200000.0, 600000.0);
        }

        [Test]
        public void 북서쪽_삼각형_안의_점은_32점5다()
        {
            Assert.IsTrue(CreateCell().TryGetElevationAt(2.5, 7.5, out double z));
            Assert.AreEqual(32.5, z, Tolerance);
        }

        [Test]
        public void 남동쪽_삼각형_안의_점은_27점5다()
        {
            Assert.IsTrue(CreateCell().TryGetElevationAt(7.5, 2.5, out double z));
            Assert.AreEqual(27.5, z, Tolerance);
        }

        [Test]
        public void 대각선_위의_점은_35이고_양쪽에서_다가가도_이어진다()
        {
            ElevationGrid cell = CreateCell();

            Assert.IsTrue(cell.TryGetElevationAt(5.0, 5.0, out double onDiagonal));
            Assert.AreEqual(35.0, onDiagonal, Tolerance);

            cell.TryGetElevationAt(5.0 - 1e-7, 5.0 + 1e-7, out double northWest);
            cell.TryGetElevationAt(5.0 + 1e-7, 5.0 - 1e-7, out double southEast);
            Assert.AreEqual(35.0, northWest, 1e-5);
            Assert.AreEqual(35.0, southEast, 1e-5);
        }

        [Test]
        public void 격자점_위에서는_격자_표고_그대로다()
        {
            ElevationGrid cell = CreateCell();

            cell.TryGetElevationAt(0.0, 0.0, out double z00);
            cell.TryGetElevationAt(10.0, 0.0, out double z10);
            cell.TryGetElevationAt(0.0, 10.0, out double z01);
            cell.TryGetElevationAt(10.0, 10.0, out double z11);

            Assert.AreEqual(10.0, z00, Tolerance);
            Assert.AreEqual(20.0, z10, Tolerance);
            Assert.AreEqual(30.0, z01, Tolerance);
            Assert.AreEqual(60.0, z11, Tolerance);
        }

        [Test]
        public void 변_위에서는_두_모서리를_직선으로_이은_값이다()
        {
            ElevationGrid cell = CreateCell();

            // 남쪽 변 가운데: (10 + 20) / 2 = 15
            cell.TryGetElevationAt(5.0, 0.0, out double south);
            Assert.AreEqual(15.0, south, Tolerance);

            // 동쪽 변 가운데: (20 + 60) / 2 = 40
            cell.TryGetElevationAt(10.0, 5.0, out double east);
            Assert.AreEqual(40.0, east, Tolerance);
        }

        [Test]
        public void 격자_범위_밖은_표고가_없다()
        {
            ElevationGrid cell = CreateCell();

            Assert.IsFalse(cell.TryGetElevationAt(-0.001, 5.0, out _), "서쪽 밖");
            Assert.IsFalse(cell.TryGetElevationAt(10.001, 5.0, out _), "동쪽 밖");
            Assert.IsFalse(cell.TryGetElevationAt(5.0, -0.001, out _), "남쪽 밖");
            Assert.IsFalse(cell.TryGetElevationAt(5.0, 10.001, out _), "북쪽 밖");
            Assert.IsFalse(cell.TryGetElevationAt(double.NaN, 5.0, out _), "숫자가 아님");
        }

        [Test]
        public void 언덕_격자점에서는_수식_값과_정확히_같다()
        {
            // col 90, row 50 → 손계산 28.393972
            Assert.IsTrue(CreateHillGrid().TryGetElevationAt(200450.0, 600250.0, out double z));
            Assert.AreEqual(28.393972, z, 1e-6);
        }

        /// <summary>
        /// 격자점 사이에서는 실제 곡면을 평면 조각으로 근사하므로 오차가 생긴다.
        /// 이것은 계산 오류가 아니라 격자 간격(해상도) 때문에 생기는 근사 오차다.
        ///
        /// 이론 한계: 오차 ≤ (1/2) × R² × M
        ///   R = 삼각형 외접원 반지름 = 5 × √2 / 2 = 3.536 m
        ///   M = 곡면의 최대 곡률 = 높이 / σ² = 50 / 100² = 0.005 /m
        ///   → (1/2) × 12.5 × 0.005 = 0.031 m
        /// </summary>
        [Test]
        public void 언덕_격자점_사이의_근사_오차는_3점5cm_미만이다()
        {
            var hill = new GaussianHill(200350.0, 600150.0, 10.0, 50.0, 100.0);
            ElevationGrid grid = CreateHillGrid();

            double maxError = 0.0;
            for (double northing = 600000.0; northing <= 600500.0; northing += 1.3)
            {
                for (double easting = 200000.0; easting <= 200500.0; easting += 1.3)
                {
                    Assert.IsTrue(grid.TryGetElevationAt(easting, northing, out double z));
                    double error = Math.Abs(z - hill.ElevationAt(easting, northing));
                    if (error > maxError) maxError = error;
                }
            }

            Assert.Less(maxError, 0.035, $"최대 오차 {maxError:F4} m");
            Assert.Greater(maxError, 0.001, "오차가 0이면 보간이 아니라 수식을 그대로 쓴 것이다");
        }
    }
}
