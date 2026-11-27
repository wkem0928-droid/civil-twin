using System;
using CivilTwin.Core;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>
    /// 필댐 단면(DamSection)과 공사 전 계곡 복원(ValleyReconstruction)을 손계산과 비교한다.
    /// 시험 계곡: 축이 동쪽(+x)을 향하고 상류는 북쪽(+y). 바닥 폭 100 m(표고 0), 양안 1:1 사면 → 원지반 = max(0, |x| − 50).
    /// 시험 댐: 높이 50 m, 마루폭 10 m, 상·하류 1:2.
    /// </summary>
    public class DamTests
    {
        private const double Cell = 5.0;
        private const int N = 61;          // −150 ~ +150 m

        private static ElevationGrid Valley()
        {
            var z = new double[N * N];
            for (int row = 0; row < N; row++)
                for (int col = 0; col < N; col++)
                    z[row * N + col] = Math.Max(0.0, Math.Abs(-150.0 + col * Cell) - 50.0);
            return new ElevationGrid(N, N, Cell, -150.0, -150.0, z);
        }

        private static DamSection TestDam() => new DamSection(0.0, 0.0, 0.0, 50.0, 10.0, 2.0, 2.0, 0.0, true, 200.0);

        [Test]
        public void 축_좌표는_축_방향과_상류_쪽으로_잰다()
        {
            // 축 30°, 중심 (100, 200): 축 방향 100 m → s 100, d 0 / 왼쪽(상류) 20 m → s 0, d +20
            var dam = new DamSection(100.0, 200.0, 30.0, 50.0, 10.0, 2.0, 2.0, 0.0, true, 300.0);
            double a = Math.PI / 6.0;
            dam.ToAxis(100.0 + 100.0 * Math.Cos(a), 200.0 + 100.0 * Math.Sin(a), out double s, out double d);
            Assert.AreEqual(100.0, s, 1e-9);
            Assert.AreEqual(0.0, d, 1e-9);

            dam.ToAxis(100.0 - 20.0 * Math.Sin(a), 200.0 + 20.0 * Math.Cos(a), out s, out d);
            Assert.AreEqual(0.0, s, 1e-9);
            Assert.AreEqual(20.0, d, 1e-9);

            dam.FromAxis(s, d, out double e, out double n);
            Assert.AreEqual(100.0 - 20.0 * Math.Sin(a), e, 1e-9);
            Assert.AreEqual(200.0 + 20.0 * Math.Cos(a), n, 1e-9);
        }

        [Test]
        public void 댐_표면은_마루에서_구배대로_내려간다()
        {
            // 마루 50, 마루폭 10 → |d| ≤ 5 는 50. 상류 1:2.3이면 d = +28 에서 50 − 23 / 2.3 = 40, 하류 1:2 이면 d = −25 에서 50 − 20 / 2 = 40
            var dam = new DamSection(0.0, 0.0, 0.0, 50.0, 10.0, 2.3, 2.0, 0.0, true, 200.0);
            Assert.AreEqual(50.0, dam.SurfaceAt(4.0), 1e-9);
            Assert.AreEqual(40.0, dam.SurfaceAt(28.0), 1e-9);
            Assert.AreEqual(40.0, dam.SurfaceAt(-25.0), 1e-9);
            Assert.AreEqual(5.0 + 50.0 * 2.3, dam.UpstreamBase, 1e-9);
            Assert.AreEqual(5.0 + 50.0 * 2.0, dam.DownstreamBase, 1e-9);
        }

        [Test]
        public void 사다리꼴_계곡_위_댐_체적이_손계산과_같다()
        {
            // 높이 t (0 ~ 50)에서: 댐 단면 폭 = 10 + 4 (50 − t), 계곡 폭 = 100 + 2t
            //   체적 = ∫ (210 − 4t)(100 + 2t) dt = 21000 t + 10 t² − 8 t³ / 3 |₀⁵⁰ = 741,666.67 m³
            // 격자(5 m, 삼각형 점고법) 결과는 742,500 m³ (+0.11 %): 댐 사면과 계곡 사면이 만나는 선이 격자선과 어긋나서다.
            ElevationGrid ground = Valley();
            EarthworkResult r = EarthworkVolume.Compute(ground, TestDam().BuildDesignGrid(ground));

            Assert.AreEqual(0.0, r.CutVolume, 1e-9);
            Assert.AreEqual(742500.0, r.FillVolume, 1e-3);
            Assert.AreEqual(741666.6667, r.FillVolume, 741666.6667 * 0.002);
        }

        [Test]
        public void 댐이_있는_지형에서_계곡을_다시_찾는다()
        {
            // "측량 지형" = 계곡 위에 댐이 지어진 모습. 오른쪽(+x) 양안 x 60 ~ 100 에 직선을 맞추면 기울기 1,
            // 왼쪽은 같은 기울기로 x = −100 (표고 50)을 지난다 → 원래 계곡과 같아야 한다.
            ElevationGrid valley = Valley();
            DamSection dam = TestDam();
            ElevationGrid surveyed = dam.BuildDesignGrid(valley);

            // 맞춤 구간 x 60 ~ 100 은 댐 마루(50) 아래라 댐에 덮여 있다 → 댐 축(d = 0) 위에서는 댐 표면이 보인다.
            // 그래서 축 위가 아니라 댐 밖(양안 위, x 110 ~ 140)에서 맞춘다.
            AbutmentValley fit = AbutmentValley.FitFromSurvey(surveyed, dam, 110.0, 140.0, -110.0, 0.0);
            Assert.AreEqual(1.0, fit.Slope, 1e-9);
            Assert.AreEqual(0.0, fit.Elevation(0.0), 1e-9);
            Assert.AreEqual(25.0, fit.Elevation(75.0), 1e-9);
            Assert.AreEqual(25.0, fit.Elevation(-75.0), 1e-9);

            ElevationGrid rebuilt = ValleyReconstruction.Rebuild(surveyed, dam, fit, 0.0, 0.0);
            for (int row = 0; row < N; row++)
                for (int col = 0; col < N; col++)
                    Assert.AreEqual(valley.GetElevation(col, row), rebuilt.GetElevation(col, row), 1e-9);

            // 복원한 원지반 위에 다시 댐을 쌓으면 처음 체적과 같다
            EarthworkResult r = EarthworkVolume.Compute(rebuilt, dam.BuildDesignGrid(rebuilt));
            Assert.AreEqual(742500.0, r.FillVolume, 1e-3);
        }

        [Test]
        public void 저수지_물은_완공_화면에서만_댐_상류를_채운다()
        {
            // "위성 지형": 댐(시험 댐)이 있고, 상류 끝(y > 105)부터는 수면 EL 40 의 호수(평평)
            ElevationGrid valley = Valley();
            DamSection dam = TestDam();
            ElevationGrid built = dam.BuildDesignGrid(valley);
            var z = new double[N * N];
            for (int row = 0; row < N; row++)
                for (int col = 0; col < N; col++)
                {
                    double y = -150.0 + row * Cell;
                    z[row * N + col] = y > 105.0 ? Math.Max(valley.GetElevation(col, row), 40.0) : built.GetElevation(col, row);
                }
            var surveyed = new ElevationGrid(N, N, Cell, -150.0, -150.0, z);
            var fit = new AbutmentValley(0.0, 1.0, -50.0, 0.0, 50.0, 0.0);
            ElevationGrid ground = ValleyReconstruction.Rebuild(surveyed, dam, fit, 10.0, 0.0);   // 상류 y ≤ 115 까지 계곡 복원
            bool[] flat = FlatSurfaceDetector.FindFlatNodes(surveyed);

            double level = DamReservoir.FindLakeLevel(surveyed, flat, dam, 2000.0);
            Assert.AreEqual(40.0, level, 1e-9);

            int Index(double x, double y) => (int)((y + 150.0) / Cell) * N + (int)((x + 150.0) / Cell);
            ElevationGrid design = dam.BuildDesignGrid(ground);

            // 완공: 복원한 상류 띠(y 110)의 계곡 바닥 0 → 수면 40 의 물, 하류(y −110)는 그대로, 마루(50)는 물 위
            ElevationGrid done = DamReservoir.Apply(design, ground, surveyed, flat, dam, level, true, out bool[] water);
            Assert.AreEqual(40.0, done.GetElevation(30, Index(0, 110) / N), 1e-9);
            Assert.IsTrue(water[Index(0, 110)]);
            Assert.AreEqual(0.0, done.GetElevation(30, Index(0, -110) / N), 1e-9);
            Assert.IsTrue(water[Index(0, -110)]);                   // 하류 강바닥도 평평 → 물색 (높이는 그대로)
            Assert.AreEqual(50.0, done.GetElevation(30, Index(0, 0) / N), 1e-9);
            Assert.IsFalse(water[Index(0, 0)]);
            Assert.IsTrue(water[Index(0, 140)]);                    // 띠 밖 호수

            // 공사 전: 띠 안은 계곡 그대로(물 없음), 띠 밖 호수만 물색
            ElevationGrid before = DamReservoir.Apply(ground, ground, surveyed, flat, dam, level, false, out water);
            Assert.AreEqual(0.0, before.GetElevation(30, Index(0, 110) / N), 1e-9);
            Assert.IsFalse(water[Index(0, 110)]);
            Assert.IsTrue(water[Index(0, 140)]);
        }

        [Test]
        public void 소양강댐_실제_공사_날수()
        {
            // 1967.4.15 ~ 1973.4.15 = 6년 = 365 × 6 + 윤일 2 (1968, 1972) = 2,192일
            // 4.15 ~ 10.15 = 15 + 31 + 30 + 31 + 31 + 30 + 15 = 183일  →  합 2,375일
            Assert.AreEqual(2375, SiteStructures.SoyanggangCalendarDays);
        }

        [Test]
        public void 잘못된_값은_거부한다()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new DamSection(0, 0, 0, 50, 10, 0, 2, 0, true, 100));   // 구배 0
            Assert.Throws<ArgumentOutOfRangeException>(() => new DamSection(0, 0, 0, 50, 10, 2, 2, 60, true, 100));  // 기초가 마루보다 높음
            Assert.Throws<ArgumentOutOfRangeException>(() => new AbutmentValley(0, 0, -10, 0, 10, 0));               // 기울기 0
        }
    }
}
