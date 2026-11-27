using CivilTwin.Core;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>
    /// 공사 중간 단계 지형(EarthworkStaging)을 손계산과 비교한다.
    /// 절토는 위에서부터, 성토는 아래에서부터 진행한다는 가정이다.
    /// </summary>
    public class EarthworkStagingTests
    {
        private const double Inf = double.PositiveInfinity;

        /// <summary>평지 60 m에 부지(40 × 20 m, 계획고 50, 절토 1:1.0)를 파는 시험. 전체 절토 15042.8932 m³.</summary>
        private static EarthworkStaging FlatGroundPad(out ElevationGrid ground, out ElevationGrid design)
        {
            var z = new double[21 * 21];
            for (int i = 0; i < z.Length; i++) z[i] = 60.0;
            ground = new ElevationGrid(21, 21, 5.0, 50.0, 50.0, z);
            design = new GradingPad(100.0, 100.0, 40.0, 20.0, 50.0, 1.0, 1.5).BuildDesignGrid(ground);
            return new EarthworkStaging(ground, design);
        }

        [Test]
        public void 깎는_곳의_중간_표고가_손계산과_같다()
        {
            // 원지반 60, 계획면 50
            Assert.AreEqual(60.0, EarthworkStaging.StageElevation(60, 50, 70, -Inf), 1e-12);  // 시작 전
            Assert.AreEqual(55.0, EarthworkStaging.StageElevation(60, 50, 55, -Inf), 1e-12);  // 5 m 깎음
            Assert.AreEqual(50.0, EarthworkStaging.StageElevation(60, 50, 40, -Inf), 1e-12);  // 완료 (더 깎지 않는다)
        }

        [Test]
        public void 쌓는_곳의_중간_표고가_손계산과_같다()
        {
            // 원지반 40, 계획면 50
            Assert.AreEqual(40.0, EarthworkStaging.StageElevation(40, 50, Inf, 30), 1e-12);   // 시작 전
            Assert.AreEqual(46.0, EarthworkStaging.StageElevation(40, 50, Inf, 46), 1e-12);   // 6 m 쌓음
            Assert.AreEqual(50.0, EarthworkStaging.StageElevation(40, 50, Inf, 99), 1e-12);   // 완료 (더 쌓지 않는다)
        }

        [Test]
        public void 손대지_않는_곳과_빈_값은_그대로다()
        {
            Assert.AreEqual(60.0, EarthworkStaging.StageElevation(60, 60, 10, 90), 1e-12);
            Assert.IsTrue(double.IsNaN(EarthworkStaging.StageElevation(double.NaN, 50, 10, 90)));
        }

        [Test]
        public void 시작과_끝_높이와_전체_토공량을_안다()
        {
            EarthworkStaging staging = FlatGroundPad(out _, out _);

            Assert.IsTrue(staging.HasCut);
            Assert.IsFalse(staging.HasFill);
            Assert.AreEqual(60.0, staging.CutTop, 1e-9);      // 깎는 곳의 가장 높은 원지반
            Assert.AreEqual(50.0, staging.CutBottom, 1e-9);   // 깎는 곳의 가장 낮은 계획면
            Assert.AreEqual(15042.8932, staging.Total.CutVolume, 1e-3);
            Assert.AreEqual(0.0, staging.Total.FillVolume, 1e-9);
        }

        [Test]
        public void 시작_단계는_원지반이고_끝_단계는_계획면이다()
        {
            EarthworkStaging staging = FlatGroundPad(out ElevationGrid ground, out ElevationGrid design);

            ElevationGrid start = staging.BuildStageGrid(Inf, -Inf);
            ElevationGrid end = staging.BuildStageGrid(-Inf, Inf);

            for (int row = 0; row < ground.Rows; row++)
            {
                for (int col = 0; col < ground.Columns; col++)
                {
                    Assert.AreEqual(ground.GetElevation(col, row), start.GetElevation(col, row), 1e-12);
                    Assert.AreEqual(design.GetElevation(col, row), end.GetElevation(col, row), 1e-12);
                }
            }
        }

        [Test]
        public void 절반_깊이까지_깎은_양이_손계산과_같다()
        {
            // 절토 기준 높이 55 m: 부지와 그 둘레 5 m 까지는 5 m 깎였고, 5 ~ 10 m 구간은 5 → 0 으로 줄어든다.
            //   부지              40 × 20 × 5                       = 4000
            //   둘레 0 ~ 5 m      폭 5 × 길이 합 120 × 깊이 5       = 3000
            //   둘레 5 ~ 10 m     폭 5 × 길이 합 120 × 평균 2.5     = 1500
            //   모퉁이 4곳        격자 삼각형으로 계산              =  917.8932
            //     (북동·남서 239.8900 × 2, 북서·남동 219.0566 × 2)
            //   합                                                  = 9417.8932
            EarthworkStaging staging = FlatGroundPad(out ElevationGrid ground, out _);

            EarthworkResult done = staging.DoneVolumes(55.0, -Inf);
            Assert.AreEqual(9417.8932, done.CutVolume, 1e-3);

            // 같은 값을 "중간 단계 격자를 만든 뒤 일반 토공량 계산"으로도 얻어야 한다.
            EarthworkResult viaGrid = EarthworkVolume.Compute(ground, staging.BuildStageGrid(55.0, -Inf));
            Assert.AreEqual(done.CutVolume, viaGrid.CutVolume, 1e-6);
        }

        [Test]
        public void 누적_절토량에서_절토_기준_높이를_거꾸로_찾는다()
        {
            EarthworkStaging staging = FlatGroundPad(out _, out _);

            // 위 시험의 반대 방향: 9417.8932 m³ 를 깎았다면 기준 높이는 55 m
            Assert.AreEqual(55.0, staging.FindCutLevel(9417.8932), 1e-4);

            Assert.AreEqual(60.0, staging.FindCutLevel(0.0), 1e-9);        // 시작 전
            Assert.AreEqual(50.0, staging.FindCutLevel(1e9), 1e-9);        // 완료
        }

        [Test]
        public void 깎을수록_기준_높이는_내려간다()
        {
            EarthworkStaging staging = FlatGroundPad(out _, out _);

            double previous = staging.FindCutLevel(0.0);
            for (int i = 1; i <= 10; i++)
            {
                double level = staging.FindCutLevel(staging.Total.CutVolume * i / 10.0);
                Assert.LessOrEqual(level, previous + 1e-9);
                previous = level;
            }
        }

        [Test]
        public void 누적_성토량에서_성토_기준_높이를_거꾸로_찾는다()
        {
            // 한 칸 10 × 10 m, 원지반 0, 계획면 10 → 전체 성토 1000 m³. 4 m 까지 쌓으면 400 m³.
            var ground = new ElevationGrid(2, 2, 10.0, 0.0, 0.0, new[] { 0.0, 0.0, 0.0, 0.0 });
            var design = new ElevationGrid(2, 2, 10.0, 0.0, 0.0, new[] { 10.0, 10.0, 10.0, 10.0 });
            var staging = new EarthworkStaging(ground, design);

            Assert.IsFalse(staging.HasCut);
            Assert.IsTrue(staging.HasFill);
            Assert.AreEqual(1000.0, staging.Total.FillVolume, 1e-9);
            Assert.AreEqual(400.0, staging.DoneVolumes(Inf, 4.0).FillVolume, 1e-9);
            Assert.AreEqual(4.0, staging.FindFillLevel(400.0), 1e-6);

            // 깎는 곳이 없으면 절토 기준 높이는 +∞ (아무것도 깎지 않는다)
            Assert.IsTrue(double.IsPositiveInfinity(staging.FindCutLevel(100.0)));
        }

        [Test]
        public void 토공이_없으면_전체_토공량이_0이다()
        {
            var ground = new ElevationGrid(2, 2, 10.0, 0.0, 0.0, new[] { 1.0, 2.0, 3.0, 4.0 });
            var staging = new EarthworkStaging(ground, ground);

            Assert.IsFalse(staging.HasCut);
            Assert.IsFalse(staging.HasFill);
            Assert.AreEqual(0.0, staging.Total.CutVolume, 1e-12);
            Assert.AreEqual(0.0, staging.Total.FillVolume, 1e-12);
        }
    }
}
