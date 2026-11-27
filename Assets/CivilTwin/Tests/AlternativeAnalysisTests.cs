using System;
using CivilTwin.Core;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>
    /// 시공 대안 분석(장비 간섭, 공사비, 대안 평가)을 손계산과 비교한다.
    ///
    /// 시험 현장: 절토 10000, 성토 4000 m³, 굴착기 한 대 77.76 m³/시간, 2층, 터파기 480 m³,
    ///            가설 5·기초 7·크레인 3·기둥 3·슬래브 5일
    /// 시험 단가: 굴착기 100,000원/시간, 기간 비례비 500,000원/일, 하루 8시간
    /// </summary>
    public class AlternativeAnalysisTests
    {
        private static SiteConditions TestSite()
        {
            return new SiteConditions(10000.0, 4000.0, 77.76, 2, 480.0,
                new BuildingDurations(5.0, 0.0, 7.0, 3.0, 3.0, 5.0));
        }

        private static AlternativeResult Evaluate(int machines, double ratio = 1.0)
        {
            return AlternativeAnalysis.Evaluate(
                new AlternativeInput("시험", machines, 8.0, ratio, 100000.0, 500000.0), TestSite());
        }

        [Test]
        public void 건물이_없는_현장은_전체_공기가_토공_공기다()
        {
            // 댐 모드(2026-11-20): 층수 0 → 터파기·건물 공정 없음. 같은 토공량의 건물 현장과 토공 공기는 같고,
            // 전체 공기 = 토공 공기, 기간 비례비 = 토공 공기 × 500,000원.
            var damSite = new SiteConditions(10000.0, 4000.0, 77.76, 0, 480.0, new BuildingDurations(5.0, 0.0, 7.0, 3.0, 3.0, 5.0));
            var input = new AlternativeInput("시험", 2, 8.0, 1.0, 100000.0, 500000.0);
            AlternativeResult dam = AlternativeAnalysis.Evaluate(input, damSite);
            AlternativeResult building = AlternativeAnalysis.Evaluate(input, TestSite());

            Assert.AreEqual(building.EarthworkDays, dam.EarthworkDays);
            Assert.AreEqual(dam.EarthworkDays, dam.ProjectDays);
            Assert.Greater(building.ProjectDays, dam.ProjectDays);
            Assert.AreEqual(0.0, dam.ExcavationDays, 1e-12);
            Assert.IsNull(dam.Schedule);
            Assert.AreEqual(dam.ProjectDays * 500000.0, dam.TimeCost, 1e-6);
            Assert.AreEqual("토공", AlternativeNarrative.CriticalStart(dam.Schedule));
        }

        [Test]
        public void 실효_대수가_손계산과_같다()
        {
            Assert.AreEqual(1.0, MachineInterference.EffectiveMachines(1, 0.9), 1e-9);
            Assert.AreEqual(1.9, MachineInterference.EffectiveMachines(2, 0.9), 1e-9);     // 1 + 0.9
            Assert.AreEqual(3.439, MachineInterference.EffectiveMachines(4, 0.9), 1e-9);   // 1 + 0.9 + 0.81 + 0.729
            Assert.AreEqual(4.0, MachineInterference.EffectiveMachines(4, 1.0), 1e-9);     // 간섭 없음
        }

        [Test]
        public void 장비를_늘릴수록_한_대당_몫은_줄어든다()
        {
            double previousShare = double.MaxValue;
            for (int n = 1; n <= 10; n++)
            {
                double effective = MachineInterference.EffectiveMachines(n, 0.9);
                Assert.LessOrEqual(effective, n);
                Assert.Less(effective / n, previousShare + 1e-12);
                previousShare = effective / n;
            }
        }

        [Test]
        public void 공사비_식이_손계산과_같다()
        {
            Assert.AreEqual(19200000.0, ConstructionCost.Equipment(4, 6.0, 8.0, 100000.0), 1e-6);   // 4 × 6 × 8 × 10만
            Assert.AreEqual(14500000.0, ConstructionCost.TimeRelated(29.0, 500000.0), 1e-6);        // 29 × 50만
        }

        [Test]
        public void 굴착기_1대_대안이_손계산과_같다()
        {
            AlternativeResult r = Evaluate(1);

            Assert.AreEqual(622.08, r.DailyVolume, 1e-9);
            Assert.AreEqual(17, r.EarthworkDays);                // 10000 / 622.08 = 16.08 → 17
            Assert.AreEqual(1.0, r.ExcavationDays, 1e-9);        // 480 / 622.08 = 0.77 → 1
            Assert.AreEqual(41, r.ProjectDays);                  // 17 + 1 + 7 + 2 × 8
            Assert.AreEqual(14400000.0, r.EquipmentCost, 1e-6);  // 1 × 18 × 8 × 10만
            Assert.AreEqual(20500000.0, r.TimeCost, 1e-6);       // 41 × 50만
            Assert.AreEqual(34900000.0, r.TotalCost, 1e-6);
        }

        [Test]
        public void 굴착기_2대와_4대_대안이_손계산과_같다()
        {
            AlternativeResult two = Evaluate(2);
            Assert.AreEqual(9, two.EarthworkDays);                 // 10000 / 1244.16 = 8.04 → 9
            Assert.AreEqual(33, two.ProjectDays);
            Assert.AreEqual(16000000.0, two.EquipmentCost, 1e-6);  // 2 × 10 × 8 × 10만
            Assert.AreEqual(32500000.0, two.TotalCost, 1e-6);

            AlternativeResult four = Evaluate(4);
            Assert.AreEqual(5, four.EarthworkDays);                // 10000 / 2488.32 = 4.02 → 5
            Assert.AreEqual(29, four.ProjectDays);
            Assert.AreEqual(19200000.0, four.EquipmentCost, 1e-6); // 4 × 6 × 8 × 10만
            Assert.AreEqual(33700000.0, four.TotalCost, 1e-6);
        }

        [Test]
        public void 장비를_8대로_늘려도_가설공사가_주공정이라_공기는_줄지_않는다()
        {
            AlternativeResult four = Evaluate(4);
            AlternativeResult eight = Evaluate(8);

            Assert.AreEqual(3, eight.EarthworkDays);                // 10000 / 4976.64 = 2.01 → 3
            Assert.AreEqual(four.ProjectDays, eight.ProjectDays);   // 둘 다 29일
            Assert.AreEqual(25600000.0, eight.EquipmentCost, 1e-6); // 8 × 4 × 8 × 10만
            Assert.AreEqual(40100000.0, eight.TotalCost, 1e-6);

            BuildingSchedule s = eight.Schedule;
            Assert.IsTrue(s.Result.Critical[s.IndexOf(BuildingWork.TemporaryWorks)]);
            Assert.IsFalse(s.Result.Critical[s.IndexOf(BuildingWork.Earthwork)]);
            Assert.AreEqual(2.0, s.Result.TotalFloat[s.IndexOf(BuildingWork.Earthwork)], 1e-9);   // 5 − 3
        }

        [Test]
        public void 간섭을_넣으면_하루_작업량이_줄고_공기가_늘_수_있다()
        {
            // 4대, r = 0.9 → 실효 3.439대 → 622.08 × 3.439 = 2139.33312 m³/일 → 10000 / 2139.33 = 4.67 → 5일 (그대로)
            AlternativeResult four = Evaluate(4, 0.9);
            Assert.AreEqual(2139.33312, four.DailyVolume, 1e-6);
            Assert.AreEqual(5, four.EarthworkDays);

            // 2대, r = 0.5 → 실효 1.5대 → 933.12 m³/일 → 10.72 → 11일 (간섭이 없으면 9일)
            AlternativeResult two = Evaluate(2, 0.5);
            Assert.AreEqual(933.12, two.DailyVolume, 1e-9);
            Assert.AreEqual(11, two.EarthworkDays);
            Assert.AreEqual(35, two.ProjectDays);   // 11 + 1 + 7 + 16
        }

        [Test]
        public void 대수별_계산이_대안_하나씩_계산한_값과_같다()
        {
            AlternativeResult[] sweep = AlternativeAnalysis.SweepMachines(
                new AlternativeInput("기준", 4, 8.0, 1.0, 100000.0, 500000.0), TestSite(), 8);

            Assert.AreEqual(8, sweep.Length);

            // 손계산 표: 1대 41일 3,490만 / 2대 33일 3,250만 / 4대 29일 3,370만 / 8대 29일 4,010만
            Assert.AreEqual(41, sweep[0].ProjectDays);
            Assert.AreEqual(34900000.0, sweep[0].TotalCost, 1e-6);
            Assert.AreEqual(33, sweep[1].ProjectDays);
            Assert.AreEqual(32500000.0, sweep[1].TotalCost, 1e-6);
            Assert.AreEqual(29, sweep[3].ProjectDays);
            Assert.AreEqual(33700000.0, sweep[3].TotalCost, 1e-6);
            Assert.AreEqual(29, sweep[7].ProjectDays);
            Assert.AreEqual(40100000.0, sweep[7].TotalCost, 1e-6);

            // 3대: 1866.24 m³/일 → 5.36 → 6일, 전체 6 + 1 + 7 + 16 = 30일
            //      장비비 3 × 7 × 8 × 10만 = 1,680만, 기간 1,500만 → 3,180만
            Assert.AreEqual(3, sweep[2].Input.Machines);
            Assert.AreEqual(30, sweep[2].ProjectDays);
            Assert.AreEqual(31800000.0, sweep[2].TotalCost, 1e-6);
        }

        [Test]
        public void 장비를_늘리면_공기는_늘지_않는다()
        {
            AlternativeResult[] sweep = AlternativeAnalysis.SweepMachines(
                new AlternativeInput("기준", 1, 8.0, 0.9, 100000.0, 500000.0), TestSite(), 12);

            for (int i = 1; i < sweep.Length; i++)
            {
                Assert.LessOrEqual(sweep[i].ProjectDays, sweep[i - 1].ProjectDays);
            }
        }

        [Test]
        public void 잘못된_입력은_거부한다()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => MachineInterference.EffectiveMachines(0, 0.9));
            Assert.Throws<ArgumentOutOfRangeException>(() => MachineInterference.EffectiveMachines(2, 0.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => MachineInterference.EffectiveMachines(2, 1.1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AlternativeInput("x", 0, 8.0, 1.0, 1.0, 1.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AlternativeInput("x", 1, 0.0, 1.0, 1.0, 1.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => ConstructionCost.Equipment(1, -1.0, 8.0, 1.0));
        }
    }
}
