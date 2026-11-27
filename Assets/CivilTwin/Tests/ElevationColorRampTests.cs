using System;
using CivilTwin.Core;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>
    /// 표고 → 색 계산을 손계산과 비교한다.
    ///
    /// 기본 색 띠의 기준점 (위치: 빨강, 초록, 파랑):
    ///   0.00 : 0.20, 0.42, 0.24  짙은 초록
    ///   0.25 : 0.56, 0.68, 0.36  연두
    ///   0.50 : 0.86, 0.80, 0.52  누런색
    ///   0.75 : 0.60, 0.44, 0.30  갈색
    ///   1.00 : 0.95, 0.95, 0.93  흰색에 가까운 회색
    /// </summary>
    public class ElevationColorRampTests
    {
        private const double Tolerance = 1e-9;

        private static void AssertColor(double r, double g, double b, RgbColor actual)
        {
            Assert.AreEqual(r, actual.R, Tolerance, "빨강");
            Assert.AreEqual(g, actual.G, Tolerance, "초록");
            Assert.AreEqual(b, actual.B, Tolerance, "파랑");
        }

        [Test]
        public void 비율_0은_첫_색이고_1은_마지막_색이다()
        {
            ElevationColorRamp ramp = ElevationColorRamp.CreateDefault();

            AssertColor(0.20, 0.42, 0.24, ramp.ColorAt(0.0));
            AssertColor(0.95, 0.95, 0.93, ramp.ColorAt(1.0));
        }

        [Test]
        public void 기준점_위치에서는_그_기준점의_색이다()
        {
            ElevationColorRamp ramp = ElevationColorRamp.CreateDefault();

            AssertColor(0.56, 0.68, 0.36, ramp.ColorAt(0.25));
            AssertColor(0.86, 0.80, 0.52, ramp.ColorAt(0.50));
            AssertColor(0.60, 0.44, 0.30, ramp.ColorAt(0.75));
        }

        [Test]
        public void 두_기준점의_한가운데는_두_색의_평균이다()
        {
            ElevationColorRamp ramp = ElevationColorRamp.CreateDefault();

            // t = 0.125 는 0 과 0.25 의 한가운데.
            // 빨강 (0.20 + 0.56) / 2 = 0.38, 초록 (0.42 + 0.68) / 2 = 0.55, 파랑 (0.24 + 0.36) / 2 = 0.30
            AssertColor(0.38, 0.55, 0.30, ramp.ColorAt(0.125));
        }

        [Test]
        public void 두_기준점_사이는_거리에_비례해_섞는다()
        {
            ElevationColorRamp ramp = ElevationColorRamp.CreateDefault();

            // t = 0.6 은 0.5(누런색)와 0.75(갈색) 사이. s = (0.6 - 0.5) / 0.25 = 0.4
            // 빨강 0.86 + 0.4 × (0.60 - 0.86) = 0.756
            // 초록 0.80 + 0.4 × (0.44 - 0.80) = 0.656
            // 파랑 0.52 + 0.4 × (0.30 - 0.52) = 0.432
            AssertColor(0.756, 0.656, 0.432, ramp.ColorAt(0.6));
        }

        [Test]
        public void 범위를_벗어난_비율은_끝_색으로_고정한다()
        {
            ElevationColorRamp ramp = ElevationColorRamp.CreateDefault();

            AssertColor(0.20, 0.42, 0.24, ramp.ColorAt(-0.5));
            AssertColor(0.95, 0.95, 0.93, ramp.ColorAt(1.5));
        }

        [Test]
        public void 표고_150은_범위_100에서_300의_4분의_1_지점이다()
        {
            ElevationColorRamp ramp = ElevationColorRamp.CreateDefault();

            // t = (150 - 100) / (300 - 100) = 0.25 → 연두
            AssertColor(0.56, 0.68, 0.36, ramp.ColorAtElevation(150.0, 100.0, 300.0));
        }

        [Test]
        public void 최저_표고는_첫_색이고_최고_표고는_마지막_색이다()
        {
            ElevationColorRamp ramp = ElevationColorRamp.CreateDefault();

            AssertColor(0.20, 0.42, 0.24, ramp.ColorAtElevation(75.5, 75.5, 654.73));
            AssertColor(0.95, 0.95, 0.93, ramp.ColorAtElevation(654.73, 75.5, 654.73));
        }

        [Test]
        public void 빈_값은_회색이다()
        {
            ElevationColorRamp ramp = ElevationColorRamp.CreateDefault();

            AssertColor(0.50, 0.50, 0.50, ramp.ColorAtElevation(double.NaN, 100.0, 300.0));
        }

        [Test]
        public void 최저와_최고가_같으면_첫_색이다()
        {
            ElevationColorRamp ramp = ElevationColorRamp.CreateDefault();

            // 0으로 나누지 않아야 한다.
            AssertColor(0.20, 0.42, 0.24, ramp.ColorAtElevation(50.0, 50.0, 50.0));
        }

        [Test]
        public void 기준점이_잘못되면_만들_수_없다()
        {
            var gray = new RgbColor(0.5, 0.5, 0.5);
            var two = new[] { gray, gray };

            Assert.Throws<ArgumentException>(() => new ElevationColorRamp(new[] { 0.0 }, new[] { gray }, gray), "기준점 1개");
            Assert.Throws<ArgumentException>(() => new ElevationColorRamp(new[] { 0.1, 1.0 }, two, gray), "0에서 시작하지 않음");
            Assert.Throws<ArgumentException>(() => new ElevationColorRamp(new[] { 0.0, 0.9 }, two, gray), "1에서 끝나지 않음");
            Assert.Throws<ArgumentException>(() => new ElevationColorRamp(new[] { 0.0, 0.5, 1.0 }, two, gray), "개수가 다름");
            Assert.Throws<ArgumentException>(
                () => new ElevationColorRamp(new[] { 0.0, 0.6, 0.4, 1.0 }, new[] { gray, gray, gray, gray }, gray), "순서가 뒤바뀜");
        }
    }
}
