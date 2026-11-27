using System;
using CivilTwin.Core;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>
    /// 좌표 변환을 손계산과 비교한다.
    ///
    /// 예제 (EPSG:5186 중부원점 근처의 가상 좌표):
    ///   원점   : 동 200000.00, 북 600000.00, 표고  0.00
    ///   측점 A : 동 200123.45, 북 600067.89, 표고 35.20
    ///
    /// 손계산:
    ///   X = 200123.45 - 200000.00 = 123.45
    ///   Z = 600067.89 - 600000.00 =  67.89
    ///   Y =     35.20 -      0.00 =  35.20
    /// </summary>
    public class LocalFrameTests
    {
        // 허용 오차 1e-6 m = 0.001 mm
        private const double Tolerance = 1e-6;

        [Test]
        public void ToLocal_손계산과_일치한다()
        {
            var frame = new LocalFrame(200000.00, 600000.00, 0.00);

            LocalPoint p = frame.ToLocal(200123.45, 600067.89, 35.20);

            Assert.AreEqual(123.45, p.X, Tolerance, "X(동쪽)");
            Assert.AreEqual(35.20, p.Y, Tolerance, "Y(표고)");
            Assert.AreEqual(67.89, p.Z, Tolerance, "Z(북쪽)");
        }

        [Test]
        public void ToLocal_원점_표고를_뺀다()
        {
            // 원점 표고 30.00 → Y = 35.20 - 30.00 = 5.20
            var frame = new LocalFrame(200000.00, 600000.00, 30.00);

            LocalPoint p = frame.ToLocal(200123.45, 600067.89, 35.20);

            Assert.AreEqual(5.20, p.Y, Tolerance);
        }

        [Test]
        public void 로컬좌표를_다시_측량좌표로_되돌리면_원래_값이다()
        {
            var frame = new LocalFrame(200000.00, 600000.00, 30.00);

            LocalPoint p = frame.ToLocal(200123.45, 600067.89, 35.20);

            Assert.AreEqual(200123.45, frame.ToEasting(p), Tolerance);
            Assert.AreEqual(600067.89, frame.ToNorthing(p), Tolerance);
            Assert.AreEqual(35.20, frame.ToElevation(p), Tolerance);
        }

        [Test]
        public void 원점_오프셋이_필요한_이유_float는_큰_좌표에서_mm가_어긋난다()
        {
            // float는 200000 근처에서 1/64 m (약 15.6 mm) 간격으로만 값을 저장한다.
            // 200123.45 에 가장 가까운 값은 200123.453125 → 오차 약 3.1 mm
            double original = 200123.45;
            double storedAsFloat = (float)original;
            double errorWithoutOffset = Math.Abs(storedAsFloat - original);

            // 원점을 빼고 나면 123.45 → float 오차는 0.01 mm 미만
            double local = original - 200000.00;
            double localStoredAsFloat = (float)local;
            double errorWithOffset = Math.Abs(localStoredAsFloat - local);

            Assert.Greater(errorWithoutOffset, 0.001, "오프셋 없이는 1 mm 넘게 어긋난다");
            Assert.Less(errorWithOffset, 0.00001, "오프셋을 쓰면 0.01 mm 미만이다");
        }
    }
}
