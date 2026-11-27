using CivilTwin.Core;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>
    /// 두 점 측정을 손계산과 비교한다.
    ///
    /// 예제: A (동 200000, 북 600000, 표고 10) → B (동 200030, 북 600040, 표고 20)
    ///   ΔE = 30, ΔN = 40, ΔH = 10
    ///   수평거리 = √(30² + 40²) = √2500            = 50.000000 m
    ///   고저차   =                                   +10.000000 m
    ///   사거리   = √(50² + 10²) = √2600            = 50.990195 m
    ///   경사     = 10 / 50 × 100                   = 20.000000 %
    ///   경사각   = atan(10 / 50) = atan(0.2)       = 11.309932 °
    ///   구배     = 50 / 10                         = 1 : 5
    ///   방위각   = atan2(30, 40) = atan(0.75)      = 36.869898 °
    /// </summary>
    public class TwoPointMeasurementTests
    {
        private const double Tolerance = 1e-6;

        private static readonly SurveyPoint A = new SurveyPoint(200000.0, 600000.0, 10.0);
        private static readonly SurveyPoint B = new SurveyPoint(200030.0, 600040.0, 20.0);

        [Test]
        public void 수평거리_고저차_사거리()
        {
            TwoPointMeasurement m = TwoPointMeasurement.Between(A, B);

            Assert.AreEqual(50.0, m.HorizontalDistance, Tolerance);
            Assert.AreEqual(10.0, m.HeightDifference, Tolerance);
            Assert.AreEqual(50.990195, m.SlopeDistance, Tolerance);
        }

        [Test]
        public void 경사_퍼센트_경사각_구배()
        {
            TwoPointMeasurement m = TwoPointMeasurement.Between(A, B);

            Assert.AreEqual(20.0, m.SlopePercent, Tolerance);
            Assert.AreEqual(11.309932, m.SlopeAngleDegrees, Tolerance);
            Assert.AreEqual(5.0, m.SlopeRatioN, Tolerance);
        }

        [Test]
        public void 방위각()
        {
            Assert.AreEqual(36.869898, TwoPointMeasurement.Between(A, B).AzimuthDegrees, Tolerance);
        }

        [Test]
        public void 반대_방향으로_재면_부호와_방위각이_뒤집힌다()
        {
            TwoPointMeasurement m = TwoPointMeasurement.Between(B, A);

            Assert.AreEqual(50.0, m.HorizontalDistance, Tolerance, "거리는 같다");
            Assert.AreEqual(-10.0, m.HeightDifference, Tolerance, "내리막");
            Assert.AreEqual(-20.0, m.SlopePercent, Tolerance);
            Assert.AreEqual(-11.309932, m.SlopeAngleDegrees, Tolerance);
            Assert.AreEqual(5.0, m.SlopeRatioN, Tolerance, "구배 1:n 은 부호가 없다");
            Assert.AreEqual(216.869898, m.AzimuthDegrees, Tolerance, "36.869898 + 180");
        }

        [Test]
        public void 동서남북_방위각은_0_90_180_270도다()
        {
            var center = new SurveyPoint(200000.0, 600000.0, 0.0);

            Assert.AreEqual(0.0, TwoPointMeasurement.Between(center, new SurveyPoint(200000.0, 600100.0, 0.0)).AzimuthDegrees, Tolerance, "북");
            Assert.AreEqual(90.0, TwoPointMeasurement.Between(center, new SurveyPoint(200100.0, 600000.0, 0.0)).AzimuthDegrees, Tolerance, "동");
            Assert.AreEqual(180.0, TwoPointMeasurement.Between(center, new SurveyPoint(200000.0, 599900.0, 0.0)).AzimuthDegrees, Tolerance, "남");
            Assert.AreEqual(270.0, TwoPointMeasurement.Between(center, new SurveyPoint(199900.0, 600000.0, 0.0)).AzimuthDegrees, Tolerance, "서");
        }

        [Test]
        public void 평지에서는_경사_0이고_구배는_무한대다()
        {
            var flatB = new SurveyPoint(200030.0, 600040.0, 10.0);

            TwoPointMeasurement m = TwoPointMeasurement.Between(A, flatB);

            Assert.AreEqual(0.0, m.SlopePercent, Tolerance);
            Assert.AreEqual(0.0, m.SlopeAngleDegrees, Tolerance);
            Assert.IsTrue(double.IsPositiveInfinity(m.SlopeRatioN));
            Assert.AreEqual(m.HorizontalDistance, m.SlopeDistance, Tolerance, "평지에서는 사거리 = 수평거리");
        }

        [Test]
        public void 같은_위치의_위아래_두_점은_경사와_방위각이_정의되지_않는다()
        {
            var above = new SurveyPoint(200000.0, 600000.0, 25.0);

            TwoPointMeasurement m = TwoPointMeasurement.Between(A, above);

            Assert.IsFalse(m.HasDirection);
            Assert.AreEqual(0.0, m.HorizontalDistance, Tolerance);
            Assert.AreEqual(15.0, m.HeightDifference, Tolerance);
            Assert.AreEqual(15.0, m.SlopeDistance, Tolerance);
            Assert.IsTrue(double.IsNaN(m.SlopePercent));
            Assert.IsTrue(double.IsNaN(m.SlopeAngleDegrees));
            Assert.IsTrue(double.IsNaN(m.SlopeRatioN));
            Assert.IsTrue(double.IsNaN(m.AzimuthDegrees));
        }

        [Test]
        public void 측량좌표와_로컬좌표를_오가도_측정값은_같다()
        {
            // 화면에서는 로컬 좌표를 쓰지만, 측정은 측량 좌표로 한다.
            var frame = new LocalFrame(200250.0, 600250.0, 0.0);

            SurveyPoint a = frame.ToSurvey(frame.ToLocal(A));
            SurveyPoint b = frame.ToSurvey(frame.ToLocal(B));
            TwoPointMeasurement m = TwoPointMeasurement.Between(a, b);

            Assert.AreEqual(50.0, m.HorizontalDistance, Tolerance);
            Assert.AreEqual(20.0, m.SlopePercent, Tolerance);
            Assert.AreEqual(36.869898, m.AzimuthDegrees, Tolerance);
        }
    }
}
