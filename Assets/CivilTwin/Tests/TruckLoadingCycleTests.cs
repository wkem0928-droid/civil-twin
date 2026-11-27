using CivilTwin.Core;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>
    /// 굴착기가 트럭 두 대에 번갈아 싣는 모습(TruckLoadingCycle)을 손계산과 비교한다.
    /// Cm 19초, 버킷 13번, 바뀌는 시간 4초, 편도 10초, 회전 4초 → L = 247초, 주기 251초.
    /// </summary>
    public class TruckLoadingCycleTests
    {
        private static TruckLoadingCycle Cycle() => new TruckLoadingCycle(19.0, 13, 4.0, 10.0, 4.0);

        [Test]
        public void 버킷_횟수는_표준품셈_식을_올림한다()
        {
            // 15 t ÷ 1.7 t/m³ = 8.8235 m³(자연) × L 1.25 = 11.029 m³ ÷ (q 1.0 × K 0.9) = 12.25 → 13번
            double n = DumpTruck.OnSite2025.LoaderCycles(1.25, 1.0, 0.9);
            Assert.AreEqual(12.255, n, 0.001);
            Assert.AreEqual(13, TruckLoadingCycle.BucketsFor(n));
            Assert.AreEqual(13, TruckLoadingCycle.BucketsFor(13.0));
            Assert.AreEqual(1, TruckLoadingCycle.BucketsFor(0.2));
        }

        [Test]
        public void 굴착기는_퍼_든_자세에서_싣기를_시작하고_바뀌는_동안_기다린다()
        {
            TruckLoadingCycle c = Cycle();
            Assert.AreEqual(247.0, c.LoadSeconds, 1e-9);
            Assert.AreEqual(251.0, c.LoadPeriod, 1e-9);
            Assert.AreEqual(5.7, c.ExcavatorTime(0.0), 1e-9);      // 0.30 × 19
            Assert.AreEqual(105.7, c.ExcavatorTime(100.0), 1e-9);
            Assert.AreEqual(5.7, c.ExcavatorTime(249.0), 1e-9);    // 바뀌는 중: 퍼 든 채 기다림
            Assert.AreEqual(5.7, c.ExcavatorTime(251.0), 1e-9);    // 다음 트럭
        }

        [Test]
        public void 흙은_덤프할_때마다_한_칸씩_찬다()
        {
            TruckLoadingCycle c = Cycle();
            Assert.AreEqual(0.0, c.TruckAt(6.0, 0).Soil, 1e-12);
            Assert.AreEqual(1.0 / 13.0, c.TruckAt(6.2, 0).Soil, 1e-12);      // 0.325 × 19 = 6.175초
            Assert.AreEqual(2.0 / 13.0, c.TruckAt(25.2, 0).Soil, 1e-12);
            Assert.AreEqual(1.0, c.TruckAt(246.9, 0).Soil, 1e-12);          // 마지막 덤프 234.175초
            Assert.AreEqual(TruckPhase.Loading, c.TruckAt(246.9, 0).Phase);
        }

        [Test]
        public void 두_대가_번갈아_싣는다()
        {
            TruckLoadingCycle c = Cycle();
            TruckState a = c.TruckAt(250.0, 0);
            Assert.AreEqual(TruckPhase.Out, a.Phase);                       // 247초에 떠나 3초째
            Assert.AreEqual(0.3, a.Progress, 1e-9);
            Assert.AreEqual(1.0, a.Soil, 1e-12);

            TruckState b = c.TruckAt(249.0, 1);
            Assert.AreEqual(TruckPhase.InTurn, b.Phase);                    // 247 ~ 251초에 들어와 251초부터 적재
            Assert.AreEqual(0.5, b.Progress, 1e-9);
            Assert.AreEqual(TruckPhase.Loading, c.TruckAt(251.0, 1).Phase);
            Assert.AreEqual(TruckPhase.Back, c.TruckAt(247.0 + 10.0 + 4.0 + 5.0, 0).Phase);
            Assert.AreEqual(TruckPhase.Waiting, c.TruckAt(300.0, 0).Phase);
            Assert.AreEqual(TruckPhase.Loading, c.TruckAt(502.0, 0).Phase); // 한 바퀴 (2 × 251초)
        }

        [Test]
        public void 반대편_끝에서_적재함을_들어_흙을_부린다()
        {
            // 덤프 8초: 적재 0 ~ 247, 나감 ~ 257, 회전 ~ 261, 덤프 261 ~ 269, 돌아옴 ~ 279
            var c = new TruckLoadingCycle(19.0, 13, 4.0, 10.0, 4.0, 8.0);
            Assert.AreEqual(1.0, c.TruckAt(259.0, 0).Soil, 1e-12);          // 회전 중에는 아직 싣고 있다
            Assert.AreEqual(0.0, c.TruckAt(259.0, 0).BedRaise, 1e-12);

            TruckState d = c.TruckAt(265.0, 0);                              // 덤프 4초째 = 진행 0.5
            Assert.AreEqual(TruckPhase.Dumping, d.Phase);
            Assert.AreEqual(1.0, d.BedRaise, 1e-12);                         // 0.3 ~ 0.7은 다 든 채
            Assert.AreEqual(0.4, d.Soil, 1e-12);                             // 1 − (0.5 − 0.2) / 0.5
            Assert.AreEqual(0.15625, TruckLoadingCycle.BedRaise(0.075), 1e-12);   // smoothstep(0.25)
            Assert.AreEqual(1.0, TruckLoadingCycle.DumpSoil(0.1), 1e-12);    // 조금 기울어질 때까지는 그대로

            TruckState back = c.TruckAt(270.0, 0);
            Assert.AreEqual(TruckPhase.Back, back.Phase);
            Assert.AreEqual(0.0, back.Soil, 1e-12);
            Assert.AreEqual(0.0, back.BedRaise, 1e-12);
            // 한 바퀴는 그대로 2 × 251초 (기다리는 시간이 덤프만큼 준다)
            Assert.AreEqual(TruckPhase.InTurn, c.TruckAt(500.0, 0).Phase);
            Assert.AreEqual(TruckPhase.Loading, c.TruckAt(502.0, 0).Phase);
        }
    }
}
