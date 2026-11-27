using CivilTwin.Core;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>
    /// 언덕 수식과, 그 수식으로 채운 표고 격자를 손계산과 비교한다.
    ///
    /// 시험 지형:
    ///   격자   : 101열 × 101행, 간격 5 m  → 500 m × 500 m
    ///   남서쪽 : 동 200000, 북 600000
    ///   언덕   : 중심 동 200350, 북 600150 (= col 70, row 30)
    ///            기준표고 10 m, 높이 50 m, σ = 100 m
    ///
    /// 중심을 일부러 한쪽으로 치우치게 두었다.
    /// 정중앙에 두면 열과 행을 뒤바꿔도 값이 같아서 실수를 찾지 못한다.
    ///
    /// 손계산 (표고 = 10 + 50 × exp(-d² / 20000)):
    ///   d =   0 m          : 10 + 50 × 1.000000 = 60.000000
    ///   d = 100 m (= σ)    : 10 + 50 × 0.606531 = 40.326533
    ///   d = 200 m (= 2σ)   : 10 + 50 × 0.135335 = 16.766764
    ///   d² = 20000 (대각선): 10 + 50 × 0.367879 = 28.393972
    /// </summary>
    public class GaussianHillTests
    {
        // 허용 오차 1e-6 m = 0.001 mm
        private const double Tolerance = 1e-6;

        private static GaussianHill CreateHill()
        {
            return new GaussianHill(200350.0, 600150.0, 10.0, 50.0, 100.0);
        }

        private static ElevationGrid CreateGrid()
        {
            return CreateHill().CreateGrid(101, 101, 5.0, 200000.0, 600000.0);
        }

        [Test]
        public void 수식_중심에서는_기준표고_더하기_높이다()
        {
            Assert.AreEqual(60.0, CreateHill().ElevationAt(200350.0, 600150.0), Tolerance);
        }

        [Test]
        public void 수식_중심에서_시그마만큼_떨어지면_높이의_약_60_7퍼센트다()
        {
            GaussianHill hill = CreateHill();

            // 동쪽으로 100 m, 북쪽으로 100 m — 방향과 관계없이 같은 값
            Assert.AreEqual(40.326533, hill.ElevationAt(200450.0, 600150.0), Tolerance);
            Assert.AreEqual(40.326533, hill.ElevationAt(200350.0, 600250.0), Tolerance);
        }

        [Test]
        public void 수식_아주_먼_곳은_기준표고에_가깝다()
        {
            // d = 1000 m (= 10σ) → exp(-50) ≈ 2 × 10⁻²² → 사실상 10.0
            Assert.AreEqual(10.0, CreateHill().ElevationAt(201350.0, 600150.0), Tolerance);
        }

        [Test]
        public void 격자_꼭대기_격자점은_col70_row30이다()
        {
            ElevationGrid grid = CreateGrid();

            Assert.AreEqual(60.0, grid.GetElevation(70, 30), Tolerance);
            Assert.AreEqual(60.0, grid.MaxElevation, Tolerance);
        }

        [Test]
        public void 격자_동쪽으로_20칸은_100m_떨어진_값이다()
        {
            // col 90 → 동 200450 → d = 100 m
            Assert.AreEqual(40.326533, CreateGrid().GetElevation(90, 30), Tolerance);
        }

        [Test]
        public void 격자_북쪽으로_40칸은_200m_떨어진_값이다()
        {
            // row 70 → 북 600350 → d = 200 m
            Assert.AreEqual(16.766764, CreateGrid().GetElevation(70, 70), Tolerance);
        }

        [Test]
        public void 격자_대각선_방향도_수식과_일치한다()
        {
            // col 90, row 50 → 동 +100 m, 북 +100 m → d² = 20000
            Assert.AreEqual(28.393972, CreateGrid().GetElevation(90, 50), Tolerance);
        }

        [Test]
        public void 격자_모든_격자점이_그_좌표의_수식_값과_같다()
        {
            GaussianHill hill = CreateHill();
            ElevationGrid grid = CreateGrid();

            for (int row = 0; row < grid.Rows; row++)
            {
                for (int col = 0; col < grid.Columns; col++)
                {
                    double expected = hill.ElevationAt(grid.GetEasting(col), grid.GetNorthing(row));
                    Assert.AreEqual(expected, grid.GetElevation(col, row), 1e-12, $"col {col}, row {row}");
                }
            }
        }
    }
}
