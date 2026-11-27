using CivilTwin.Core;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>절토·성토 색칠(CutFillColors)을 손계산과 비교한다. h = 계획면 − 원지반.</summary>
    public class CutFillColorsTests
    {
        private static readonly RgbColor Terrain = new RgbColor(0.20, 0.40, 0.20);

        [Test]
        public void 가장_깊이_깎는_곳은_진한_빨강이다()
        {
            RgbColor c = CutFillColors.ColorFor(-10.0, 10.0, 5.0, Terrain);

            // 진한 적갈색 (0.75, 0.33, 0.18) — 2026-11-20 색 바꿈
            Assert.AreEqual(0.75, c.R, 1e-9);
            Assert.AreEqual(0.33, c.G, 1e-9);
            Assert.AreEqual(0.18, c.B, 1e-9);
        }

        [Test]
        public void 절반_깊이는_옅은_빨강과_진한_빨강의_가운데다()
        {
            // s = 5 / 10 = 0.5 → R = 0.98 + 0.5 × (0.75 − 0.98) = 0.865,  G = 0.86 + 0.5 × (0.33 − 0.86) = 0.595,
            //                    B = 0.76 + 0.5 × (0.18 − 0.76) = 0.47
            RgbColor c = CutFillColors.ColorFor(-5.0, 10.0, 5.0, Terrain);

            Assert.AreEqual(0.865, c.R, 1e-9);
            Assert.AreEqual(0.595, c.G, 1e-9);
            Assert.AreEqual(0.47, c.B, 1e-9);
        }

        [Test]
        public void 가장_높이_쌓는_곳은_진한_파랑이다()
        {
            // 진한 청록 (0.22, 0.45, 0.72)
            RgbColor c = CutFillColors.ColorFor(5.0, 10.0, 5.0, Terrain);

            Assert.AreEqual(0.22, c.R, 1e-9);
            Assert.AreEqual(0.45, c.G, 1e-9);
            Assert.AreEqual(0.72, c.B, 1e-9);
        }

        [Test]
        public void 손대지_않는_곳은_원래_색을_회색_쪽으로_흐리게_한다()
        {
            // R = 0.20 + 0.6 × (0.55 − 0.20) = 0.41,  G = 0.40 + 0.6 × (0.55 − 0.40) = 0.49
            RgbColor c = CutFillColors.ColorFor(0.0, 10.0, 5.0, Terrain);

            Assert.AreEqual(0.41, c.R, 1e-9);
            Assert.AreEqual(0.49, c.G, 1e-9);
            Assert.AreEqual(0.41, c.B, 1e-9);

            // 1 cm 이하의 차이도 손대지 않는 곳으로 본다.
            RgbColor tiny = CutFillColors.ColorFor(0.005, 10.0, 5.0, Terrain);
            Assert.AreEqual(0.41, tiny.R, 1e-9);
        }

        [Test]
        public void 가장_깊은_절토와_가장_높은_성토를_찾는다()
        {
            // 원지반 (10, 20, 30, 40), 계획면 (25, 25, 25, 25) → h = +15, +5, −5, −15
            var ground = new ElevationGrid(2, 2, 10.0, 0.0, 0.0, new[] { 10.0, 20.0, 30.0, 40.0 });
            var design = new ElevationGrid(2, 2, 10.0, 0.0, 0.0, new[] { 25.0, 25.0, 25.0, 25.0 });

            CutFillColors.FindMaxDepths(ground, design, out double maxCut, out double maxFill);

            Assert.AreEqual(15.0, maxCut, 1e-9);
            Assert.AreEqual(15.0, maxFill, 1e-9);
        }

        [Test]
        public void 토공이_없으면_최대_깊이는_0이다()
        {
            var ground = new ElevationGrid(2, 2, 10.0, 0.0, 0.0, new[] { 10.0, 20.0, 30.0, 40.0 });

            CutFillColors.FindMaxDepths(ground, ground, out double maxCut, out double maxFill);

            Assert.AreEqual(0.0, maxCut, 1e-12);
            Assert.AreEqual(0.0, maxFill, 1e-12);
        }
    }
}
