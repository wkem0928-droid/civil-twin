using System;
using CivilTwin.Core;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>토량환산계수 L, C를 적용한 토량 균형을 손계산과 비교한다. (L 1.25, C 0.9는 계산 예를 위한 가정값)</summary>
    public class EarthBalanceTests
    {
        private const double L = 1.25;
        private const double C = 0.9;

        [Test]
        public void 흙이_남으면_사토가_생긴다()
        {
            // 절토 10000 (자연), 성토 4000 (다져진)
            // 필요한 자연 흙 = 4000 / 0.9 = 4444.444…, 사토 = 10000 − 4444.444… = 5555.555…
            var b = new EarthBalance(10000.0, 4000.0, L, C);

            Assert.AreEqual(4444.444444, b.ReusedVolume, 1e-6);
            Assert.AreEqual(5555.555556, b.SurplusVolume, 1e-6);
            Assert.AreEqual(0.0, b.BorrowVolume, 1e-9);
            Assert.AreEqual(9000.0, b.CutAsCompacted, 1e-9);
        }

        [Test]
        public void 흙이_모자라면_토취가_생긴다()
        {
            // 절토 1000 → 다지면 900. 성토 4000에 3100이 모자람 → 자연 상태로 3100 / 0.9 = 3444.444…
            var b = new EarthBalance(1000.0, 4000.0, L, C);

            Assert.AreEqual(1000.0, b.ReusedVolume, 1e-9);
            Assert.AreEqual(0.0, b.SurplusVolume, 1e-9);
            Assert.AreEqual(3444.444444, b.BorrowVolume, 1e-6);
        }

        [Test]
        public void 운반량은_자연_상태_부피에_L을_곱한다()
        {
            // 남는 경우: 유용 4444.444… × 1.25 = 5555.555…, 사토 5555.555… × 1.25 = 6944.444…
            var surplus = new EarthBalance(10000.0, 4000.0, L, C);
            Assert.AreEqual(5555.555556, surplus.ReusedLooseVolume, 1e-6);
            Assert.AreEqual(6944.444444, surplus.SurplusLooseVolume, 1e-6);

            // 모자라는 경우: 토취 3444.444… × 1.25 = 4305.555…
            var borrow = new EarthBalance(1000.0, 4000.0, L, C);
            Assert.AreEqual(4305.555556, borrow.BorrowLooseVolume, 1e-6);
        }

        [Test]
        public void 절토_곱하기_C가_성토와_같으면_남지도_모자라지도_않는다()
        {
            // 절토 1000 × 0.9 = 900 = 성토
            var b = new EarthBalance(1000.0, 900.0, L, C);

            Assert.AreEqual(1000.0, b.ReusedVolume, 1e-9);
            Assert.AreEqual(0.0, b.SurplusVolume, 1e-9);
            Assert.AreEqual(0.0, b.BorrowVolume, 1e-9);
        }

        [Test]
        public void 유용토와_사토의_합은_절토량이다()
        {
            // 흙은 사라지지 않는다: 자연 상태로 세면 유용 + 사토 = 절토, 유용 + 토취 = 성토 / C
            foreach (double fill in new[] { 0.0, 500.0, 900.0, 4000.0, 20000.0 })
            {
                var b = new EarthBalance(1000.0, fill, L, C);
                Assert.AreEqual(1000.0, b.ReusedVolume + b.SurplusVolume, 1e-9);
                Assert.AreEqual(fill / C, b.ReusedVolume + b.BorrowVolume, 1e-9);
            }
        }

        [Test]
        public void 계수가_1이면_예전_계산의_차이와_같다()
        {
            // L = C = 1이면 부피 변화가 없다 → 사토 = 절토 − 성토 (Phase 2의 NetVolume과 같은 뜻)
            var b = new EarthBalance(10000.0, 4000.0, 1.0, 1.0);
            Assert.AreEqual(6000.0, b.SurplusVolume, 1e-9);
        }

        [Test]
        public void 잘못된_계수는_거부한다()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new EarthBalance(100.0, 100.0, 0.9, 0.9));   // L < 1
            Assert.Throws<ArgumentOutOfRangeException>(() => new EarthBalance(100.0, 100.0, 1.25, 0.0));  // C = 0
            Assert.Throws<ArgumentOutOfRangeException>(() => new EarthBalance(-1.0, 100.0, 1.25, 0.9));   // 음수 절토
        }
    }
}
