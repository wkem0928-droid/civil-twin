using CivilTwin.Core;
using CivilTwin.Runtime;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>
    /// 평평한 면(호수 수면 추정) 찾기를 작은 격자에서 손으로 센 결과와 비교한다.
    /// 규칙: 자기와 둘레 8개 점의 높이가 모두 같아야 평평한 면이다.
    /// 격자 배열은 남쪽 줄부터 적는다 (번호 = row × Columns + col).
    /// </summary>
    public class FlatSurfaceDetectorTests
    {
        private static ElevationGrid Grid(int columns, int rows, double[] z)
        {
            return new ElevationGrid(columns, rows, 10.0, 0.0, 0.0, z);
        }

        [Test]
        public void 모든_점의_높이가_같으면_전부_평평한_면이다()
        {
            ElevationGrid grid = Grid(3, 3, new[]
            {
                5.0, 5.0, 5.0,
                5.0, 5.0, 5.0,
                5.0, 5.0, 5.0,
            });

            bool[] flat = FlatSurfaceDetector.FindFlatNodes(grid);

            Assert.AreEqual(9, FlatSurfaceDetector.Count(flat));
        }

        [Test]
        public void 가운데_점만_높으면_평평한_면이_하나도_없다()
        {
            // 3 × 3 격자에서는 모든 점이 가운데 점과 이웃이다.
            ElevationGrid grid = Grid(3, 3, new[]
            {
                5.0, 5.0, 5.0,
                5.0, 6.0, 5.0,
                5.0, 5.0, 5.0,
            });

            bool[] flat = FlatSurfaceDetector.FindFlatNodes(grid);

            Assert.AreEqual(0, FlatSurfaceDetector.Count(flat));
        }

        [Test]
        public void 호수와_비탈이_만나면_비탈에_닿은_줄은_빠진다()
        {
            // 서쪽 3열은 호수(10 m), 동쪽 2열은 비탈(20 m, 30 m). 5 × 5 격자.
            //   col 0, 1 : 둘레가 모두 10 m → 평평한 면 (2열 × 5행 = 10개)
            //   col 2    : 동쪽 이웃이 20 m → 아님
            //   col 3, 4 : 이웃끼리 높이가 다름 → 아님
            var z = new double[25];
            for (int row = 0; row < 5; row++)
            {
                z[row * 5 + 0] = 10.0;
                z[row * 5 + 1] = 10.0;
                z[row * 5 + 2] = 10.0;
                z[row * 5 + 3] = 20.0;
                z[row * 5 + 4] = 30.0;
            }
            ElevationGrid grid = Grid(5, 5, z);

            bool[] flat = FlatSurfaceDetector.FindFlatNodes(grid);

            Assert.AreEqual(10, FlatSurfaceDetector.Count(flat));
            Assert.IsTrue(flat[2 * 5 + 0], "col 0");
            Assert.IsTrue(flat[2 * 5 + 1], "col 1");
            Assert.IsFalse(flat[2 * 5 + 2], "col 2 (호수 가장자리)");
            Assert.IsFalse(flat[2 * 5 + 3], "col 3");
            Assert.IsFalse(flat[2 * 5 + 4], "col 4");
        }

        [Test]
        public void 빈_값과_그_이웃은_평평한_면이_아니다()
        {
            // 남서쪽 모서리(col 0, row 0)가 빈 값.
            //   빈 값 자신 1개 + 그 이웃 3개 (1,0) (0,1) (1,1) = 4개가 빠진다 → 9 - 4 = 5개
            ElevationGrid grid = Grid(3, 3, new[]
            {
                double.NaN, 5.0, 5.0,
                5.0,        5.0, 5.0,
                5.0,        5.0, 5.0,
            });

            bool[] flat = FlatSurfaceDetector.FindFlatNodes(grid);

            Assert.AreEqual(5, FlatSurfaceDetector.Count(flat));
            Assert.IsFalse(flat[0], "빈 값");
            Assert.IsFalse(flat[1], "(1,0)");
            Assert.IsFalse(flat[3], "(0,1)");
            Assert.IsFalse(flat[4], "(1,1)");
            Assert.IsTrue(flat[8], "(2,2)");
        }

        [Test]
        public void 허용_차이_안의_높이는_같다고_본다()
        {
            ElevationGrid grid = Grid(3, 3, new[]
            {
                5.00, 5.00, 5.00,
                5.00, 5.02, 5.00,
                5.00, 5.00, 5.00,
            });

            Assert.AreEqual(0, FlatSurfaceDetector.Count(FlatSurfaceDetector.FindFlatNodes(grid)), "기본 허용 차이");
            Assert.AreEqual(9, FlatSurfaceDetector.Count(FlatSurfaceDetector.FindFlatNodes(grid, 0.05)), "허용 차이 5 cm");
        }

        [Test]
        public void 수식_언덕에는_평평한_면이_없다()
        {
            // 언덕은 모든 곳에서 높이가 조금씩 변한다. 평지처럼 보이는 가장자리도 수면으로 잡히면 안 된다.
            ElevationGrid grid = HillTerrainDemo.CreateGrid();

            Assert.AreEqual(0, FlatSurfaceDetector.Count(FlatSurfaceDetector.FindFlatNodes(grid)));
        }
    }
}
