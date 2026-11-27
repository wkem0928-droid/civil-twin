using System;
using CivilTwin.Core;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>
    /// 표고 격자가 값을 올바른 위치에 저장하고 꺼내는지 확인한다.
    ///
    /// 예제 격자 (5열 × 3행, 간격 10 m, 남서쪽 모서리 = 동 200000, 북 600000):
    ///
    ///   row 2 (북) :  20  21  22  23  24
    ///   row 1      :  10  11  12  13  14
    ///   row 0 (남) :   0   1   2   3   4
    ///                col0 ............ col4
    ///                (서)             (동)
    /// </summary>
    public class ElevationGridTests
    {
        private const double Tolerance = 1e-9;

        private static ElevationGrid CreateSampleGrid()
        {
            double[] elevations =
            {
                0, 1, 2, 3, 4,          // row 0 (남쪽)
                10, 11, 12, 13, 14,     // row 1
                20, 21, 22, 23, 24,     // row 2 (북쪽)
            };
            return new ElevationGrid(5, 3, 10.0, 200000.0, 600000.0, elevations);
        }

        [Test]
        public void 열과_행으로_표고를_꺼낸다()
        {
            ElevationGrid grid = CreateSampleGrid();

            Assert.AreEqual(0.0, grid.GetElevation(0, 0), Tolerance, "남서쪽 모서리");
            Assert.AreEqual(4.0, grid.GetElevation(4, 0), Tolerance, "남동쪽 모서리");
            Assert.AreEqual(20.0, grid.GetElevation(0, 2), Tolerance, "북서쪽 모서리");
            Assert.AreEqual(24.0, grid.GetElevation(4, 2), Tolerance, "북동쪽 모서리");
            Assert.AreEqual(13.0, grid.GetElevation(3, 1), Tolerance, "col 3, row 1");
        }

        [Test]
        public void 격자점_좌표는_원점에_간격을_곱해_더한_값이다()
        {
            ElevationGrid grid = CreateSampleGrid();

            // 동쪽좌표 = 200000 + 3 × 10 = 200030
            Assert.AreEqual(200030.0, grid.GetEasting(3), Tolerance);
            // 북쪽좌표 = 600000 + 2 × 10 = 600020
            Assert.AreEqual(600020.0, grid.GetNorthing(2), Tolerance);
        }

        [Test]
        public void 전체_길이는_격자점_개수에서_1을_뺀_값에_간격을_곱한다()
        {
            ElevationGrid grid = CreateSampleGrid();

            // 격자점 5개 사이의 칸은 4개 → 4 × 10 = 40 m
            Assert.AreEqual(40.0, grid.WidthMeters, Tolerance);
            // 격자점 3개 사이의 칸은 2개 → 2 × 10 = 20 m
            Assert.AreEqual(20.0, grid.HeightMeters, Tolerance);
        }

        [Test]
        public void 최저_최고_표고를_구한다()
        {
            ElevationGrid grid = CreateSampleGrid();

            Assert.AreEqual(0.0, grid.MinElevation, Tolerance);
            Assert.AreEqual(24.0, grid.MaxElevation, Tolerance);
        }

        [Test]
        public void 표고_개수가_열_곱하기_행과_다르면_거부한다()
        {
            var tooFew = new double[14]; // 5 × 3 = 15개가 필요하다

            Assert.Throws<ArgumentException>(
                () => new ElevationGrid(5, 3, 10.0, 0.0, 0.0, tooFew));
        }

        [Test]
        public void 범위_밖의_격자점을_요청하면_거부한다()
        {
            ElevationGrid grid = CreateSampleGrid();

            Assert.Throws<ArgumentOutOfRangeException>(() => grid.GetElevation(5, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => grid.GetElevation(0, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => grid.GetElevation(-1, 0));
        }

        [Test]
        public void 원본_배열을_나중에_고쳐도_격자는_바뀌지_않는다()
        {
            double[] elevations = { 1, 2, 3, 4 };
            var grid = new ElevationGrid(2, 2, 1.0, 0.0, 0.0, elevations);

            elevations[0] = 999.0;

            Assert.AreEqual(1.0, grid.GetElevation(0, 0), Tolerance);
        }
    }
}
