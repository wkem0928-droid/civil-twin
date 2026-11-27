using System;
using CivilTwin.Core;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>굴착기 작업량과 토공 공정 계산을 손계산과 비교한다.</summary>
    public class EarthworkScheduleTests
    {
        [Test]
        public void 굴착기_시간당_작업량이_손계산과_같다()
        {
            // Q = 3600 × 1.0 × 0.9 × 0.8 × 0.6 / 20 = 77.76 m³/시간
            double q = ExcavatorProductivity.HourlyVolume(1.0, 0.9, 0.8, 0.6, 20.0);

            Assert.AreEqual(77.76, q, 1e-9);
        }

        [Test]
        public void 하루_작업량은_시간당_작업량에_시간과_대수를_곱한다()
        {
            // 77.76 × 8시간 × 1대 = 622.08,  × 4대 = 2488.32
            Assert.AreEqual(622.08, ExcavatorProductivity.DailyVolume(77.76, 8.0, 1), 1e-9);
            Assert.AreEqual(2488.32, ExcavatorProductivity.DailyVolume(77.76, 8.0, 4), 1e-9);
        }

        [Test]
        public void 사이클_시간이_두_배면_작업량은_절반이다()
        {
            double fast = ExcavatorProductivity.HourlyVolume(1.0, 0.9, 0.8, 0.6, 20.0);
            double slow = ExcavatorProductivity.HourlyVolume(1.0, 0.9, 0.8, 0.6, 40.0);

            Assert.AreEqual(fast / 2.0, slow, 1e-9);
        }

        [Test]
        public void 소요_일수가_손계산과_같다()
        {
            // 절토 10000, 성토 4000, 하루 1000씩 → 절토 10일, 성토 4일, 공기 10일
            var s = new EarthworkSchedule(10000.0, 4000.0, 1000.0, 1000.0);

            Assert.AreEqual(10.0, s.CutDays, 1e-9);
            Assert.AreEqual(4.0, s.FillDays, 1e-9);
            Assert.AreEqual(10, s.TotalDays);
        }

        [Test]
        public void 나누어_떨어지지_않으면_공기를_올림한다()
        {
            // 절토 10000 / 622.08 = 16.075 → 17일
            var s = new EarthworkSchedule(10000.0, 0.0, 622.08, 622.08);

            Assert.AreEqual(16.075103, s.CutDays, 1e-6);
            Assert.AreEqual(17, s.TotalDays);
        }

        [Test]
        public void 누적_작업량과_공정률이_손계산과_같다()
        {
            var s = new EarthworkSchedule(10000.0, 4000.0, 1000.0, 1000.0);

            // 3일째: 3000 + 3000 = 6000 → 42.857143 %
            Assert.AreEqual(3000.0, s.CumulativeCut(3), 1e-9);
            Assert.AreEqual(3000.0, s.CumulativeFill(3), 1e-9);
            Assert.AreEqual(42.857143, s.ProgressPercent(3), 1e-6);

            // 5일째: 성토는 4000에서 끝났다. 5000 + 4000 = 9000 → 64.285714 %
            Assert.AreEqual(5000.0, s.CumulativeCut(5), 1e-9);
            Assert.AreEqual(4000.0, s.CumulativeFill(5), 1e-9);
            Assert.AreEqual(64.285714, s.ProgressPercent(5), 1e-6);
        }

        [Test]
        public void 시작_전은_0이고_공기가_지나면_100이다()
        {
            var s = new EarthworkSchedule(10000.0, 4000.0, 1000.0, 1000.0);

            Assert.AreEqual(0.0, s.ProgressPercent(0), 1e-12);
            Assert.AreEqual(0.0, s.CumulativeCut(-1), 1e-12);
            Assert.AreEqual(100.0, s.ProgressPercent(10), 1e-9);
            Assert.AreEqual(100.0, s.ProgressPercent(99), 1e-9);
        }

        [Test]
        public void 토공이_없으면_공기는_0일이고_공정률은_100이다()
        {
            var s = new EarthworkSchedule(0.0, 0.0, 1000.0, 1000.0);

            Assert.AreEqual(0, s.TotalDays);
            Assert.AreEqual(100.0, s.ProgressPercent(0), 1e-12);
        }

        [Test]
        public void 잘못된_값은_거부한다()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new EarthworkSchedule(-1, 0, 1000, 1000));
            Assert.Throws<ArgumentOutOfRangeException>(() => new EarthworkSchedule(0, 0, 0, 1000));
            Assert.Throws<ArgumentOutOfRangeException>(() => ExcavatorProductivity.HourlyVolume(1, 0.9, 0.8, 0.6, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => ExcavatorProductivity.DailyVolume(77.76, 8, 0));
        }
    }
}
