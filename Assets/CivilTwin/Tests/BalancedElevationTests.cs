using System;
using CivilTwin.Core;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>균형 계획고(절토 × C = 성토)를 손계산과 비교한다.</summary>
    public class BalancedElevationTests
    {
        /// <summary>
        /// 동쪽으로 10 % 올라가는 경사면: 표고 = 0.1 × x, x는 0 ~ 100 m, y는 0 ~ 10 m (칸 5 m, 21 × 3 격자점).
        /// 부지가 격자 전체를 덮어서 비탈이 생기지 않는다.
        /// 계획고 z일 때 (0 ≤ z ≤ 10):
        ///   절토 = 폭 10 × ∫(0.1x − z) dx (0.1x &gt; z 인 구간) = 10 × (10 − z)² / (2 × 0.1) = 50 (10 − z)²
        ///   성토 = 50 z²
        /// 평면이라 삼각형 점고법이 정확하다 (근사 오차 없음).
        /// </summary>
        private static ElevationGrid Slope(out GradingPad pad)
        {
            var z = new double[21 * 3];
            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 21; col++)
                {
                    z[row * 21 + col] = 0.1 * (col * 5.0);
                }
            }
            pad = new GradingPad(50.0, 5.0, 100.0, 10.0, 0.0, 1.0, 1.5);
            return new ElevationGrid(21, 3, 5.0, 0.0, 0.0, z);
        }

        [Test]
        public void 경사면의_토량이_손계산과_같다()
        {
            // 계획고 4 m → 절토 50 × 36 = 1800, 성토 50 × 16 = 800
            ElevationGrid ground = Slope(out GradingPad pad);
            BalancedElevation.Difference(ground, pad, 4.0, 1.0, out EarthworkResult r);

            Assert.AreEqual(1800.0, r.CutVolume, 1e-6);
            Assert.AreEqual(800.0, r.FillVolume, 1e-6);
        }

        [Test]
        public void C가_1이면_균형_계획고는_가운데_높이다()
        {
            // 50 (10 − z)² = 50 z² → z = 5, 절토 = 성토 = 1250
            ElevationGrid ground = Slope(out GradingPad pad);
            double z = BalancedElevation.Find(ground, pad, 1.0, out EarthworkResult r);

            Assert.AreEqual(5.0, z, 1e-6);
            Assert.AreEqual(1250.0, r.CutVolume, 1e-3);
            Assert.AreEqual(1250.0, r.FillVolume, 1e-3);
        }

        [Test]
        public void C가_0_9이면_흙이_줄어드는_만큼_계획고가_내려간다()
        {
            // 0.9 × 50 (10 − z)² = 50 z² → √0.9 (10 − z) = z → z = 10 √0.9 / (1 + √0.9) = 4.868330
            // 절토 = 50 × 5.131670² = 1316.702, 성토 = 50 × 4.868330² = 1185.032 (= 절토 × 0.9)
            ElevationGrid ground = Slope(out GradingPad pad);
            double z = BalancedElevation.Find(ground, pad, 0.9, out EarthworkResult r);

            double expected = 10.0 * Math.Sqrt(0.9) / (1.0 + Math.Sqrt(0.9));
            Assert.AreEqual(4.868330, expected, 1e-6);
            Assert.AreEqual(expected, z, 1e-6);
            Assert.AreEqual(1185.032, r.FillVolume, 1e-3);
            Assert.AreEqual(r.FillVolume, r.CutVolume * 0.9, 1e-3);
        }

        [Test]
        public void 언덕_비탈에서도_균형이_맞고_1_m_올리거나_내리면_어긋난다()
        {
            // 수식 언덕(간격 5 m)의 서쪽 비탈에 80 × 60 m 부지, 절토 1:1.0, 성토 1:1.5 (비탈까지 포함)
            ElevationGrid ground = new GaussianHill(200350.0, 600150.0, 10.0, 50.0, 100.0)
                .CreateGrid(101, 101, 5.0, 200000.0, 600000.0);
            var pad = new GradingPad(200250.0, 600150.0, 80.0, 60.0, 0.0, 1.0, 1.5);

            double z = BalancedElevation.Find(ground, pad, 0.9, out EarthworkResult r);

            Assert.Greater(r.CutVolume, 0.0);
            Assert.AreEqual(r.CutVolume * 0.9, r.FillVolume, 1e-3);
            Assert.Greater(BalancedElevation.Difference(ground, pad, z - 1.0, 0.9, out _), 0.0);   // 낮추면 흙이 남는다
            Assert.Less(BalancedElevation.Difference(ground, pad, z + 1.0, 0.9, out _), 0.0);      // 올리면 모자란다
        }

        [Test]
        public void 암이_섞이면_균형_계획고가_올라간다()
        {
            // 같은 10 % 경사면, 암반선 지표 아래 2 m, C토사 0.9, C암 1.2
            //   절토 = 50 (10 − z)², 암 = 50 (8 − z)², 토사 = 절토 − 암
            //   0.9 × 토사 + 1.2 × 암 = 0.9 × 절토 + 0.3 × 암 = 50 z²
            //   → 45 (10 − z)² + 15 (8 − z)² = 50 z² → z² − 114 z + 546 = 0 → z = (114 − √10812) / 2 = 5.009616
            // (토사만으로 보면 4.868330 — 암은 다지면 부피가 커져 같은 절토로 더 많이 채우므로 계획고가 올라간다)
            ElevationGrid ground = Slope(out GradingPad pad);
            double z = BalancedElevation.FindWithRock(ground, pad, 0.9, 1.2, 2.0, out _);

            Assert.AreEqual((114.0 - Math.Sqrt(10812.0)) / 2.0, z, 1e-6);
            Assert.AreEqual(5.009616, z, 1e-6);
        }

        [Test]
        public void 암반선이_없으면_토사만의_균형과_같다()
        {
            ElevationGrid ground = Slope(out GradingPad pad);
            double withRock = BalancedElevation.FindWithRock(ground, pad, 0.9, 1.2, RockLine.None, out _);
            Assert.AreEqual(BalancedElevation.Find(ground, pad, 0.9), withRock, 1e-12);
        }

        [Test]
        public void 잘못된_C는_거부한다()
        {
            ElevationGrid ground = Slope(out GradingPad pad);
            Assert.Throws<ArgumentOutOfRangeException>(() => BalancedElevation.Find(ground, pad, 0.0));
        }
    }
}
