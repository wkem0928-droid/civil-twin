using System;
using CivilTwin.Core;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>
    /// 토공량 계산(EarthworkVolume)을 손계산과 비교한다.
    /// 높이 차 h = 계획면 − 원지반 (양수 성토, 음수 절토).
    /// </summary>
    public class EarthworkVolumeTests
    {
        private static ElevationGrid Grid2x2(double z00, double z10, double z01, double z11, double cell = 10.0)
        {
            // 배열 순서: (col 0,row 0), (col 1,row 0), (col 0,row 1), (col 1,row 1)
            return new ElevationGrid(2, 2, cell, 0.0, 0.0, new[] { z00, z10, z01, z11 });
        }

        [Test]
        public void 삼각형이_모두_절토면_면적_곱하기_평균_깊이다()
        {
            // 면적 50, h = −2, −2, −2 → 절토 50 × 6 / 3 = 100
            EarthworkResult r = EarthworkVolume.ComputeTriangle(-2, -2, -2, 50.0);

            Assert.AreEqual(100.0, r.CutVolume, 1e-9);
            Assert.AreEqual(0.0, r.FillVolume, 1e-9);
            Assert.AreEqual(50.0, r.CutArea, 1e-9);
            Assert.AreEqual(0.0, r.FillArea, 1e-9);
        }

        [Test]
        public void 삼각형이_모두_성토면_면적_곱하기_평균_높이다()
        {
            // 면적 50, h = 1, 3, 4 → 성토 50 × 8 / 3 = 133.333333
            EarthworkResult r = EarthworkVolume.ComputeTriangle(1, 3, 4, 50.0);

            Assert.AreEqual(133.333333, r.FillVolume, 1e-6);
            Assert.AreEqual(0.0, r.CutVolume, 1e-9);
        }

        [Test]
        public void 절토와_성토가_섞인_삼각형이_손계산과_같다()
        {
            // 면적 50, h = +3, −3, −3
            //   성토 = 50 × 27 / (3 × 6 × 6) = 12.5,  성토 면적 = 50 × 9 / 36 = 12.5
            //   전체 = 50 × (−3) / 3 = −50 → 절토 = 62.5,  절토 면적 = 37.5
            EarthworkResult r = EarthworkVolume.ComputeTriangle(3, -3, -3, 50.0);

            Assert.AreEqual(12.5, r.FillVolume, 1e-9);
            Assert.AreEqual(62.5, r.CutVolume, 1e-9);
            Assert.AreEqual(12.5, r.FillArea, 1e-9);
            Assert.AreEqual(37.5, r.CutArea, 1e-9);
            Assert.AreEqual(50.0, r.NetVolume, 1e-9);   // 절토 − 성토
        }

        [Test]
        public void 꼭짓점_순서를_바꿔도_결과가_같다()
        {
            EarthworkResult a = EarthworkVolume.ComputeTriangle(3, -3, -3, 50.0);
            EarthworkResult b = EarthworkVolume.ComputeTriangle(-3, 3, -3, 50.0);
            EarthworkResult c = EarthworkVolume.ComputeTriangle(-3, -3, 3, 50.0);

            Assert.AreEqual(a.CutVolume, b.CutVolume, 1e-9);
            Assert.AreEqual(a.CutVolume, c.CutVolume, 1e-9);
            Assert.AreEqual(a.FillVolume, b.FillVolume, 1e-9);
            Assert.AreEqual(a.FillVolume, c.FillVolume, 1e-9);
        }

        [Test]
        public void 절토_하나_성토_둘인_삼각형은_부호만_반대다()
        {
            // h = −3, +3, +3 은 위 시험의 절토·성토를 맞바꾼 것
            EarthworkResult r = EarthworkVolume.ComputeTriangle(-3, 3, 3, 50.0);

            Assert.AreEqual(12.5, r.CutVolume, 1e-9);
            Assert.AreEqual(62.5, r.FillVolume, 1e-9);
        }

        [Test]
        public void 한_꼭짓점이_0인_섞인_삼각형이_손계산과_같다()
        {
            // 면적 50, h = +2, −2, 0
            //   성토 = 50 × 8 / (3 × 4 × 2) = 16.666667,  전체 = 0 → 절토 = 16.666667
            EarthworkResult r = EarthworkVolume.ComputeTriangle(2, -2, 0, 50.0);

            Assert.AreEqual(16.666667, r.FillVolume, 1e-6);
            Assert.AreEqual(16.666667, r.CutVolume, 1e-6);
            Assert.AreEqual(25.0, r.FillArea, 1e-9);
            Assert.AreEqual(25.0, r.CutArea, 1e-9);
        }

        [Test]
        public void 높이_차가_모두_0이면_토공이_없다()
        {
            EarthworkResult r = EarthworkVolume.ComputeTriangle(0, 0, 0, 50.0);

            Assert.AreEqual(0.0, r.CutVolume, 1e-12);
            Assert.AreEqual(0.0, r.FillVolume, 1e-12);
            Assert.AreEqual(0.0, r.CutArea, 1e-12);
            Assert.AreEqual(0.0, r.FillArea, 1e-12);
        }

        [Test]
        public void 한_칸_전체를_2m_깎으면_면적_곱하기_2다()
        {
            // 칸 10 m × 10 m = 100 m², 원지반 12, 계획면 10 → 절토 200
            EarthworkResult r = EarthworkVolume.Compute(Grid2x2(12, 12, 12, 12), Grid2x2(10, 10, 10, 10));

            Assert.AreEqual(200.0, r.CutVolume, 1e-9);
            Assert.AreEqual(0.0, r.FillVolume, 1e-9);
            Assert.AreEqual(100.0, r.CutArea, 1e-9);
        }

        [Test]
        public void 한_칸의_성토량이_사각형_점고법과_같다()
        {
            // 원지반 0, 계획면 (남서 1, 남동 2, 북서 3, 북동 4)
            //   북서 삼각형 50 × (1 + 3 + 4) / 3 = 133.333333
            //   남동 삼각형 50 × (1 + 2 + 4) / 3 = 116.666667
            //   합 250 = 사각형 점고법 100 × (1 + 2 + 3 + 4) / 4
            EarthworkResult r = EarthworkVolume.Compute(Grid2x2(0, 0, 0, 0), Grid2x2(1, 2, 3, 4));

            Assert.AreEqual(250.0, r.FillVolume, 1e-9);
            Assert.AreEqual(0.0, r.CutVolume, 1e-9);
        }

        [Test]
        public void 평지에_판_부지의_절토량이_손계산과_같다()
        {
            // 원지반: 평평한 60 m, 21 × 21 격자점, 간격 5 m, 남서 (50, 50)
            // 부지: 중심 (100, 100), 40 m × 20 m, 계획고 50 m, 절토 1:1.0 → 깊이 10 m, 비탈 폭 10 m
            //
            // 손계산 (격자 계산값):
            //   부지 바닥      40 × 20 × 10                         =  8000
            //   네 변의 비탈   단면 ½ × 10 × 10 = 50, 길이 합 120   =  6000
            //   모퉁이 4곳     격자 삼각형으로 계산                 =  1042.8932
            //     북동·남서 모퉁이 281.5566 × 2, 북서·남동 모퉁이 239.8900 × 2
            //     (대각선 방향이 한쪽으로 고정이라 모퉁이마다 값이 다르다)
            //   합                                                  = 15042.8932
            //
            // 참고: 모퉁이를 원뿔로 본 정확한 값은 4 × (π/2) × (500 − 1000/3) = 1047.1976,
            //       합 15047.1976 이다. 격자 간격 5 m에서 차이는 4.30 m³ (0.03 %).
            var z = new double[21 * 21];
            for (int i = 0; i < z.Length; i++) z[i] = 60.0;
            var ground = new ElevationGrid(21, 21, 5.0, 50.0, 50.0, z);
            var pad = new GradingPad(100.0, 100.0, 40.0, 20.0, 50.0, 1.0, 1.5);

            EarthworkResult r = EarthworkVolume.Compute(ground, pad.BuildDesignGrid(ground));

            Assert.AreEqual(15042.8932, r.CutVolume, 1e-3);
            Assert.AreEqual(0.0, r.FillVolume, 1e-9);

            // 정확한 값(원뿔 모퉁이)과의 차이가 0.1 % 안이다.
            double exact = 8000.0 + 6000.0 + 4.0 * (Math.PI / 2.0) * (500.0 - 1000.0 / 3.0);
            Assert.AreEqual(exact, r.CutVolume, exact * 0.001);
        }

        [Test]
        public void 빈_값이_있는_삼각형은_계산에서_뺀다()
        {
            // 북동 점이 빈 값이면 두 삼각형 모두 그 점을 쓰므로 토공량은 0이다.
            EarthworkResult r = EarthworkVolume.Compute(
                Grid2x2(12, 12, 12, double.NaN), Grid2x2(10, 10, 10, 10));

            Assert.AreEqual(0.0, r.CutVolume, 1e-12);
            Assert.AreEqual(0.0, r.CutArea, 1e-12);
        }

        [Test]
        public void 격자가_다르면_거부한다()
        {
            var a = Grid2x2(0, 0, 0, 0, 10.0);
            var b = Grid2x2(0, 0, 0, 0, 5.0);

            Assert.Throws<ArgumentException>(() => EarthworkVolume.Compute(a, b));
        }
    }
}
