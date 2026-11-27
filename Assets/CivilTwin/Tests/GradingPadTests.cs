using System;
using CivilTwin.Core;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>
    /// 부지 정지 계획면(GradingPad)을 손계산과 비교한다.
    ///
    /// 시험 부지: 중심 (100, 100), 동서 40 m × 남북 20 m → 동 80 ~ 120, 북 90 ~ 110
    ///            계획고 50 m, 절토 1:1.0, 성토 1:1.5
    /// </summary>
    public class GradingPadTests
    {
        private static GradingPad TestPad()
        {
            return new GradingPad(100.0, 100.0, 40.0, 20.0, 50.0, 1.0, 1.5);
        }

        [Test]
        public void 부지_안은_원지반과_상관없이_계획고다()
        {
            GradingPad pad = TestPad();

            Assert.AreEqual(50.0, pad.DesignElevation(100.0, 100.0, 70.0), 1e-9);  // 높은 땅 → 깎는다
            Assert.AreEqual(50.0, pad.DesignElevation(100.0, 100.0, 30.0), 1e-9);  // 낮은 땅 → 쌓는다
            Assert.AreEqual(50.0, pad.DesignElevation(120.0, 110.0, 70.0), 1e-9);  // 북동 모퉁이 위 (d = 0)
        }

        [Test]
        public void 부지에서_떨어진_거리가_손계산과_같다()
        {
            GradingPad pad = TestPad();

            Assert.AreEqual(0.0, pad.DistanceFromPad(100.0, 100.0), 1e-9);   // 안
            Assert.AreEqual(10.0, pad.DistanceFromPad(130.0, 100.0), 1e-9);  // 동쪽 변에서 10 m
            Assert.AreEqual(10.0, pad.DistanceFromPad(70.0, 95.0), 1e-9);    // 서쪽 변에서 10 m
            Assert.AreEqual(7.0, pad.DistanceFromPad(100.0, 83.0), 1e-9);    // 남쪽 변에서 7 m

            // 북동 모퉁이 (120, 110)에서 동 3 m, 북 4 m → √(9 + 16) = 5
            Assert.AreEqual(5.0, pad.DistanceFromPad(123.0, 114.0), 1e-9);
        }

        [Test]
        public void 비탈면_표고가_손계산과_같다()
        {
            GradingPad pad = TestPad();

            // 동쪽 변에서 10 m: 절토 비탈 50 + 10/1.0 = 60, 성토 비탈 50 − 10/1.5 = 43.333333
            Assert.AreEqual(60.0, pad.DesignElevation(130.0, 100.0, 70.0), 1e-9);        // 절토
            Assert.AreEqual(55.0, pad.DesignElevation(130.0, 100.0, 55.0), 1e-9);        // 그대로
            Assert.AreEqual(43.333333, pad.DesignElevation(130.0, 100.0, 40.0), 1e-6);   // 성토

            // 모퉁이에서 5 m: 절토 비탈 55, 성토 비탈 50 − 5/1.5 = 46.666667
            Assert.AreEqual(55.0, pad.DesignElevation(123.0, 114.0, 80.0), 1e-9);
            Assert.AreEqual(46.666667, pad.DesignElevation(123.0, 114.0, 20.0), 1e-6);
        }

        [Test]
        public void 원지반이_비탈_한계와_같으면_그대로다()
        {
            GradingPad pad = TestPad();

            // 비탈면이 원지반과 만나는 점(비탈 끝)에서는 계획면 = 원지반
            Assert.AreEqual(60.0, pad.DesignElevation(130.0, 100.0, 60.0), 1e-9);
        }

        [Test]
        public void 계획면_격자가_손계산과_같다()
        {
            // 평평한 원지반 60 m, 21 × 21 격자점, 간격 5 m, 남서 (50, 50) → 동·북 50 ~ 150
            var z = new double[21 * 21];
            for (int i = 0; i < z.Length; i++) z[i] = 60.0;
            var ground = new ElevationGrid(21, 21, 5.0, 50.0, 50.0, z);

            ElevationGrid design = TestPad().BuildDesignGrid(ground);

            Assert.AreEqual(21, design.Columns);
            Assert.AreEqual(21, design.Rows);
            Assert.AreEqual(5.0, design.CellSize, 1e-9);
            Assert.AreEqual(50.0, design.OriginEasting, 1e-9);

            // (col 10, row 10) = (100, 100): 부지 중심 → 50
            Assert.AreEqual(50.0, design.GetElevation(10, 10), 1e-9);
            // (col 14, row 10) = (120, 100): 부지 동쪽 변 → 50
            Assert.AreEqual(50.0, design.GetElevation(14, 10), 1e-9);
            // (col 15, row 10) = (125, 100): 변에서 5 m → 절토 비탈 55
            Assert.AreEqual(55.0, design.GetElevation(15, 10), 1e-9);
            // (col 16, row 10) = (130, 100): 변에서 10 m → 절토 비탈 60 = 원지반 (비탈 끝)
            Assert.AreEqual(60.0, design.GetElevation(16, 10), 1e-9);
            // (col 18, row 10) = (140, 100): 비탈 밖 → 원지반 60
            Assert.AreEqual(60.0, design.GetElevation(18, 10), 1e-9);

            // 원지반 60이 계획고 50보다 높으므로 쌓는 곳은 없다: 최저 50, 최고 60
            Assert.AreEqual(50.0, design.MinElevation, 1e-9);
            Assert.AreEqual(60.0, design.MaxElevation, 1e-9);
        }

        [Test]
        public void 원지반이_빈_값이면_계획면도_빈_값이다()
        {
            Assert.IsTrue(double.IsNaN(TestPad().DesignElevation(100.0, 100.0, double.NaN)));
        }

        [Test]
        public void 잘못된_값은_거부한다()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new GradingPad(0, 0, 0, 20, 50, 1, 1.5));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GradingPad(0, 0, 40, -1, 50, 1, 1.5));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GradingPad(0, 0, 40, 20, 50, 0, 1.5));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GradingPad(0, 0, 40, 20, 50, 1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GradingPad(0, 0, 40, 20, double.NaN, 1, 1.5));
        }
    }
}
