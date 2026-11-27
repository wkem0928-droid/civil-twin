using CivilTwin.Core;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>누적 토공량 그래프의 값(EarthworkCurve)을 손계산 표와 비교한다.</summary>
    public class EarthworkCurveTests
    {
        [Test]
        public void 날짜별_누적값이_손계산_표와_같다()
        {
            // 절토 10000, 성토 4000, 하루 1000 → 공기 10일
            var schedule = new EarthworkSchedule(10000, 4000, 1000, 1000);
            EarthworkCurve curve = EarthworkCurve.Sample(schedule, 10);

            Assert.AreEqual(11, curve.Days.Length);

            double[] expectedCut = { 0, 1000, 2000, 3000, 4000, 5000, 6000, 7000, 8000, 9000, 10000 };
            double[] expectedFill = { 0, 1000, 2000, 3000, 4000, 4000, 4000, 4000, 4000, 4000, 4000 };
            for (int i = 0; i <= 10; i++)
            {
                Assert.AreEqual(i, curve.Days[i], 1e-12);
                Assert.AreEqual(expectedCut[i], curve.Cut[i], 1e-9);
                Assert.AreEqual(expectedFill[i], curve.Fill[i], 1e-9);
            }

            // 공정률: 3일째 6000 / 14000, 5일째 9000 / 14000
            Assert.AreEqual(42.857143, curve.Percent[3], 1e-6);
            Assert.AreEqual(64.285714, curve.Percent[5], 1e-6);
            Assert.AreEqual(100.0, curve.Percent[10], 1e-9);
        }

        [Test]
        public void 구간을_잘게_나눠도_공정_계산과_같은_값이다()
        {
            // 절토 7.5일, 성토 2.2일 → 공기 8일. 구간 16개 → 0.5일 간격
            var schedule = new EarthworkSchedule(7500, 2200, 1000, 1000);
            EarthworkCurve curve = EarthworkCurve.Sample(schedule, 16);

            Assert.AreEqual(8.0, curve.Days[16], 1e-12);
            Assert.AreEqual(2.5, curve.Days[5], 1e-12);
            Assert.AreEqual(2500.0, curve.Cut[5], 1e-9);    // 2.5일 × 1000
            Assert.AreEqual(2200.0, curve.Fill[5], 1e-9);   // 2.2일째에 끝났다
            Assert.AreEqual(7500.0, curve.Cut[16], 1e-9);   // 7.5일째에 끝났다

            for (int i = 0; i < curve.Days.Length; i++)
            {
                Assert.AreEqual(schedule.CumulativeCut(curve.Days[i]), curve.Cut[i], 1e-12);
                Assert.AreEqual(schedule.CumulativeFill(curve.Days[i]), curve.Fill[i], 1e-12);
            }
        }

        [Test]
        public void 누적값은_줄어들지_않는다()
        {
            var schedule = new EarthworkSchedule(15042.8932, 3000, 2488.32, 2488.32);
            EarthworkCurve curve = EarthworkCurve.Sample(schedule, 100);

            for (int i = 1; i < curve.Days.Length; i++)
            {
                Assert.GreaterOrEqual(curve.Cut[i], curve.Cut[i - 1]);
                Assert.GreaterOrEqual(curve.Fill[i], curve.Fill[i - 1]);
            }
        }

        [Test]
        public void 토공이_없으면_모든_값이_0이다()
        {
            var schedule = new EarthworkSchedule(0, 0, 1000, 1000);
            EarthworkCurve curve = EarthworkCurve.Sample(schedule, 4);

            for (int i = 0; i < curve.Days.Length; i++)
            {
                Assert.AreEqual(0.0, curve.Days[i], 1e-12);
                Assert.AreEqual(0.0, curve.Cut[i], 1e-12);
                Assert.AreEqual(0.0, curve.Fill[i], 1e-12);
            }
        }

        [TestCase(15042.9, 20000.0)]
        [TestCase(10000.0, 10000.0)]
        [TestCase(4000.0, 5000.0)]
        [TestCase(1200.0, 2000.0)]
        [TestCase(999.0, 1000.0)]
        [TestCase(5000.1, 10000.0)]
        [TestCase(0.3, 0.5)]
        [TestCase(0.0, 1.0)]
        public void 세로축_최댓값은_읽기_쉬운_수로_올린다(double value, double expected)
        {
            Assert.AreEqual(expected, EarthworkCurve.NiceAxisMax(value), expected * 1e-9);
        }

        [Test]
        public void 구간_수가_1보다_작으면_오류다()
        {
            var schedule = new EarthworkSchedule(100, 100, 10, 10);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => EarthworkCurve.Sample(schedule, 0));
        }
    }
}
