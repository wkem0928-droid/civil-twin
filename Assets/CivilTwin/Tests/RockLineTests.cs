using System;
using CivilTwin.Core;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>암반선 깊이로 절토를 토사·암으로 나누는 계산을 손계산과 비교한다.</summary>
    public class RockLineTests
    {
        /// <summary>21 × 3 격자점, 칸 5 m → 동서 100 m × 남북 10 m = 1000 m². 표고 = f(x).</summary>
        private static ElevationGrid Grid(Func<double, double> elevationAtX)
        {
            var z = new double[21 * 3];
            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 21; col++)
                {
                    z[row * 21 + col] = elevationAtX(col * 5.0);
                }
            }
            return new ElevationGrid(21, 3, 5.0, 0.0, 0.0, z);
        }

        /// <summary>격자 전체를 덮는 부지 (비탈 없음)</summary>
        private static ElevationGrid Design(ElevationGrid ground, double padElevation)
        {
            return new GradingPad(50.0, 5.0, 100.0, 10.0, padElevation, 1.0, 1.5).BuildDesignGrid(ground);
        }

        [Test]
        public void 평지를_고르게_깎으면_깊이에_비례해_나뉜다()
        {
            // 원지반 60, 계획고 50 → 깊이 10 m × 1000 m² = 10000. 암반선 4 m → 토사 4000, 암 6000
            ElevationGrid ground = Grid(x => 60.0);
            CutMaterials m = RockLine.Split(ground, Design(ground, 50.0), 4.0);

            Assert.AreEqual(4000.0, m.SoilVolume, 1e-6);
            Assert.AreEqual(6000.0, m.RockVolume, 1e-6);
            Assert.AreEqual(1000.0, m.RockArea, 1e-6);
            Assert.AreEqual(0.6, m.RockRatio, 1e-9);
        }

        [Test]
        public void 경사면에서는_깊은_쪽에만_암이_나온다()
        {
            // 표고 = 0.1x, 계획고 4 → 깎는 깊이 h = 0.1x − 4 (x > 40). 전체 절토 = 50 × 6² = 1800
            // 암반선 2 m → 암은 h > 2 인 곳(x > 60): 50 × (6 − 2)² = 800, 토사 = 1800 − 800 = 1000
            // 암 면적 = (100 − 60) × 10 = 400 m²
            ElevationGrid ground = Grid(x => 0.1 * x);
            CutMaterials m = RockLine.Split(ground, Design(ground, 4.0), 2.0);

            Assert.AreEqual(1000.0, m.SoilVolume, 1e-6);
            Assert.AreEqual(800.0, m.RockVolume, 1e-6);
            Assert.AreEqual(400.0, m.RockArea, 1e-6);
        }

        [Test]
        public void 암반선이_깎는_깊이보다_깊으면_모두_토사다()
        {
            ElevationGrid ground = Grid(x => 60.0);
            CutMaterials deep = RockLine.Split(ground, Design(ground, 50.0), 15.0);
            CutMaterials none = RockLine.Split(ground, Design(ground, 50.0), RockLine.None);

            Assert.AreEqual(10000.0, deep.SoilVolume, 1e-6);
            Assert.AreEqual(0.0, deep.RockVolume, 1e-9);
            Assert.AreEqual(10000.0, none.SoilVolume, 1e-6);
            Assert.AreEqual(0.0, none.RockVolume, 1e-9);
        }

        [Test]
        public void 암반선이_지표면이면_모두_암이다()
        {
            ElevationGrid ground = Grid(x => 60.0);
            CutMaterials m = RockLine.Split(ground, Design(ground, 50.0), 0.0);

            Assert.AreEqual(0.0, m.SoilVolume, 1e-6);
            Assert.AreEqual(10000.0, m.RockVolume, 1e-6);
        }

        [Test]
        public void 언덕_비탈에서_토사와_암의_합은_절토량이고_암반선이_깊을수록_암이_준다()
        {
            ElevationGrid ground = new GaussianHill(200350.0, 600150.0, 10.0, 50.0, 100.0)
                .CreateGrid(101, 101, 5.0, 200000.0, 600000.0);
            ElevationGrid design = new GradingPad(200300.0, 600150.0, 80.0, 60.0, 40.0, 1.0, 1.5).BuildDesignGrid(ground);
            double cut = EarthworkVolume.Compute(ground, design).CutVolume;

            double previousRock = double.MaxValue;
            foreach (double depth in new[] { 0.0, 2.0, 5.0, 10.0 })
            {
                CutMaterials m = RockLine.Split(ground, design, depth);
                Assert.AreEqual(cut, m.TotalVolume, 1e-6);
                Assert.LessOrEqual(m.RockVolume, previousRock);
                previousRock = m.RockVolume;
            }
        }

        [Test]
        public void 음수_깊이는_거부한다()
        {
            ElevationGrid ground = Grid(x => 60.0);
            Assert.Throws<ArgumentOutOfRangeException>(() => RockLine.Split(ground, Design(ground, 50.0), -1.0));
        }
    }
}
