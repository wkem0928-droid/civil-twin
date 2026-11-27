using CivilTwin.Core;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>
    /// 궤도 카메라의 위치 계산을 손계산과 비교한다.
    ///
    /// 손계산 1: 주시점 (0, 0, 0), 방위각 0°(북), 고도각 30°, 거리 100 m
    ///   수평거리 = 100 × cos30° = 86.602540
    ///   X = 0 - 86.602540 × sin0° = 0
    ///   Z = 0 - 86.602540 × cos0° = -86.602540   (주시점의 남쪽)
    ///   Y = 0 + 100 × sin30°      = 50
    ///
    /// 손계산 2: 방위각 45°(북동), 고도각 45°, 거리 200 m
    ///   수평거리 = 200 × cos45° = 141.421356
    ///   X = -141.421356 × sin45° = -100
    ///   Z = -141.421356 × cos45° = -100
    ///   Y =  200 × sin45°        = 141.421356
    /// </summary>
    public class OrbitCameraTests
    {
        private const double Tolerance = 1e-6;

        private static OrbitCamera Create(double yaw, double pitch, double distance)
        {
            return new OrbitCamera(new LocalPoint(0, 0, 0), yaw, pitch, distance);
        }

        [Test]
        public void 북쪽을_볼_때_카메라는_주시점의_남쪽_위에_있다()
        {
            LocalPoint p = Create(0, 30, 100).GetPosition();

            Assert.AreEqual(0.0, p.X, Tolerance);
            Assert.AreEqual(50.0, p.Y, Tolerance);
            Assert.AreEqual(-86.602540, p.Z, Tolerance);
        }

        [Test]
        public void 동쪽을_볼_때_카메라는_주시점의_서쪽에_있다()
        {
            // 방위각 90°, 고도각 5°(하한) 대신 계산을 단순하게 하려고 고도각 60° 사용
            // 수평거리 = 100 × cos60° = 50 → X = -50, Z = 0, Y = 100 × sin60° = 86.602540
            LocalPoint p = Create(90, 60, 100).GetPosition();

            Assert.AreEqual(-50.0, p.X, Tolerance);
            Assert.AreEqual(86.602540, p.Y, Tolerance);
            Assert.AreEqual(0.0, p.Z, Tolerance);
        }

        [Test]
        public void 북동쪽_45도_고도각_45도_거리_200m()
        {
            LocalPoint p = Create(45, 45, 200).GetPosition();

            Assert.AreEqual(-100.0, p.X, Tolerance);
            Assert.AreEqual(141.421356, p.Y, Tolerance);
            Assert.AreEqual(-100.0, p.Z, Tolerance);
        }

        [Test]
        public void 주시점이_원점이_아니면_그만큼_평행이동한다()
        {
            var camera = new OrbitCamera(new LocalPoint(100, 60, -100), 0, 30, 100);

            LocalPoint p = camera.GetPosition();

            Assert.AreEqual(100.0, p.X, Tolerance);
            Assert.AreEqual(110.0, p.Y, Tolerance);           // 60 + 50
            Assert.AreEqual(-186.602540, p.Z, Tolerance);     // -100 - 86.602540
        }

        [Test]
        public void 카메라와_주시점_사이의_거리는_항상_지정한_거리다()
        {
            OrbitCamera camera = Create(123, 37, 250);

            LocalPoint p = camera.GetPosition();
            double distance = System.Math.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z);

            Assert.AreEqual(250.0, distance, Tolerance);
        }

        [Test]
        public void 회전하면_방위각이_0도_이상_360도_미만으로_되돌아온다()
        {
            OrbitCamera camera = Create(350, 30, 100);

            camera.Orbit(20, 0);    // 350 + 20 = 370 → 10
            Assert.AreEqual(10.0, camera.YawDegrees, Tolerance);

            camera.Orbit(-30, 0);   // 10 - 30 = -20 → 340
            Assert.AreEqual(340.0, camera.YawDegrees, Tolerance);
        }

        [Test]
        public void 고도각은_5도와_89도_사이에_갇힌다()
        {
            OrbitCamera camera = Create(0, 30, 100);

            camera.Orbit(0, 100);
            Assert.AreEqual(89.0, camera.PitchDegrees, Tolerance, "위쪽 한계");

            camera.Orbit(0, -200);
            Assert.AreEqual(5.0, camera.PitchDegrees, Tolerance, "아래쪽 한계");
        }

        [Test]
        public void 확대는_거리에_배율을_곱한다()
        {
            OrbitCamera camera = Create(0, 30, 100);

            camera.Zoom(0.5);
            Assert.AreEqual(50.0, camera.Distance, Tolerance);

            camera.Zoom(4.0);
            Assert.AreEqual(200.0, camera.Distance, Tolerance);
        }

        [Test]
        public void 거리는_최소_최대_한계에_갇힌다()
        {
            var camera = new OrbitCamera(new LocalPoint(0, 0, 0), 0, 30, 100, minDistance: 10, maxDistance: 1000);

            camera.Zoom(0.001);
            Assert.AreEqual(10.0, camera.Distance, Tolerance);

            camera.Zoom(1000000);
            Assert.AreEqual(1000.0, camera.Distance, Tolerance);
        }

        [Test]
        public void 북쪽을_볼_때_오른쪽은_동쪽이고_앞쪽은_북쪽이다()
        {
            OrbitCamera camera = Create(0, 30, 100);

            camera.Pan(10, 0);
            Assert.AreEqual(10.0, camera.Target.X, Tolerance, "오른쪽 10 m = 동쪽 10 m");
            Assert.AreEqual(0.0, camera.Target.Z, Tolerance);

            camera.Pan(0, 20);
            Assert.AreEqual(10.0, camera.Target.X, Tolerance);
            Assert.AreEqual(20.0, camera.Target.Z, Tolerance, "앞쪽 20 m = 북쪽 20 m");
        }

        [Test]
        public void 동쪽을_볼_때_오른쪽은_남쪽이고_앞쪽은_동쪽이다()
        {
            OrbitCamera camera = Create(90, 30, 100);

            camera.Pan(10, 0);
            Assert.AreEqual(0.0, camera.Target.X, Tolerance);
            Assert.AreEqual(-10.0, camera.Target.Z, Tolerance, "오른쪽 10 m = 남쪽 10 m");

            camera.Pan(0, 20);
            Assert.AreEqual(20.0, camera.Target.X, Tolerance, "앞쪽 20 m = 동쪽 20 m");
            Assert.AreEqual(-10.0, camera.Target.Z, Tolerance);
        }

        [Test]
        public void 이동해도_주시점의_표고와_카메라_거리는_그대로다()
        {
            var camera = new OrbitCamera(new LocalPoint(0, 12.5, 0), 40, 30, 100);

            camera.Pan(33, -47);

            Assert.AreEqual(12.5, camera.Target.Y, Tolerance);
            Assert.AreEqual(100.0, camera.Distance, Tolerance);
        }
    }
}
