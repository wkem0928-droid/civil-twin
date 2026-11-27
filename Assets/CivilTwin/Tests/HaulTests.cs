using System;
using CivilTwin.Core;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>
    /// 5-4 덤프트럭 운반, 평균 운반거리, 토공 공기(굴착과 운반 중 느린 쪽)를 손계산과 비교한다.
    /// 식은 2025 표준품셈 8-2-8 덤프트럭의 구조를 따른다 (n은 올림하지 않음). 아래 시험용 트럭 값은 손계산을 쉽게 하려는 값이고,
    /// 실제 기본값(표준품셈 값)은 맨 아래 "표준품셈_2025_트럭" 테스트에서 따로 확인한다.
    /// 굴착기: q1 1.0 m³, K 0.9, Cms 20초, Es 0.6 → 한 대 77.76 m³/시간 × 8시간 = 622.08 m³/일 (2-4와 같음)
    /// 시험용 트럭: 15 t, γt 1.8, V1 20 / V2 30 km/h, t3 1분, t4 0.5분, t5 = t6 = 0, E 0.9
    /// </summary>
    public class HaulTests
    {
        private const double SoilL = 1.25, SoilC = 0.9, RockL = 1.6, RockC = 1.2;
        private static readonly DumpTruck Truck = new DumpTruck(15.0, 1.8, 20.0, 30.0, 1.0, 0.5, 0.9);

        private static double LoadMinutes => Truck.LoadMinutes(SoilL, 1.0, 0.9, 20.0, 0.6);

        // ---------- 덤프트럭 ----------

        [Test]
        public void 트럭_한_번에_싣는_양과_버킷_횟수()
        {
            // 자연 15 / 1.8 = 8.3333, 흐트러진 × 1.25 = 10.4167, n = 10.4167 / (1.0 × 0.9) = 11.574 (올림하지 않음)
            Assert.AreEqual(8.333333, Truck.NaturalPerTrip, 1e-6);
            Assert.AreEqual(10.416667, Truck.LoosePerTrip(SoilL), 1e-6);
            Assert.AreEqual(11.574074, Truck.LoaderCycles(SoilL, 1.0, 0.9), 1e-6);
        }

        [Test]
        public void 사이클_시간과_시간당_운반량이_손계산과_같다()
        {
            // t1 = 20 × 11.574 / (60 × 0.6) = 6.4300분
            Assert.AreEqual(6.430041, LoadMinutes, 1e-6);

            // 0.3 km: t2 = (0.3/20 + 0.3/30) × 60 = 1.5 → Cm = 6.4300 + 1.5 + 1 + 0.5 = 9.4300분
            //         Q = 60 × 8.3333 × 0.9 / 9.4300 = 47.7198 m³/시간
            Assert.AreEqual(1.5, Truck.TravelMinutes(0.3), 1e-9);
            Assert.AreEqual(9.430041, Truck.CycleMinutes(0.3, LoadMinutes), 1e-6);
            Assert.AreEqual(47.719834, Truck.HourlyNatural(0.3, LoadMinutes), 1e-6);

            // 5 km: t2 = 15 + 10 = 25 → Cm = 32.9300 → Q = 13.6653
            Assert.AreEqual(25.0, Truck.TravelMinutes(5.0), 1e-9);
            Assert.AreEqual(13.665334, Truck.HourlyNatural(5.0, LoadMinutes), 1e-6);
        }

        [Test]
        public void 굴착기_한_대에_필요한_트럭은_거리에_따라_늘어난다()
        {
            // 굴착기 77.76 m³/시간 ÷ 트럭 한 대 → 0.3 km 1.63대, 5 km 5.69대
            Assert.AreEqual(1.6295, 77.76 / Truck.HourlyNatural(0.3, LoadMinutes), 1e-4);
            Assert.AreEqual(5.6903, 77.76 / Truck.HourlyNatural(5.0, LoadMinutes), 1e-4);
        }

        // ---------- 평균 운반거리 ----------

        [Test]
        public void 경사면의_평균_운반거리는_무게중심_사이_거리다()
        {
            // 표고 = 0.1x (x 0 ~ 100 m, 칸 5 m), 계획고 4 m, 부지가 전체를 덮음
            // 격자점 근사: 절토 무게중심 81.667 m, 성토 11.667 m → 70.0 m (연속 계산은 80 − 13.33 = 66.67 m)
            var z = new double[21 * 3];
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 21; col++)
                    z[row * 21 + col] = 0.1 * (col * 5.0);
            var ground = new ElevationGrid(21, 3, 5.0, 0.0, 0.0, z);
            ElevationGrid design = new GradingPad(50.0, 5.0, 100.0, 10.0, 4.0, 1.0, 1.5).BuildDesignGrid(ground);

            HaulDistance d = HaulDistance.Compute(ground, design);

            Assert.IsTrue(d.HasBoth);
            Assert.AreEqual(81.666667, d.CutEasting, 1e-6);
            Assert.AreEqual(11.666667, d.FillEasting, 1e-6);
            Assert.AreEqual(70.0, d.Meters, 1e-6);
        }

        [Test]
        public void 성토가_없으면_평균_운반거리는_0이다()
        {
            var ground = new ElevationGrid(3, 3, 5.0, 0.0, 0.0, new double[] { 60, 60, 60, 60, 60, 60, 60, 60, 60 });
            ElevationGrid design = new GradingPad(5.0, 5.0, 10.0, 10.0, 50.0, 1.0, 1.5).BuildDesignGrid(ground);

            Assert.IsFalse(HaulDistance.Compute(ground, design).HasBoth);
            Assert.AreEqual(0.0, HaulDistance.Compute(ground, design).Meters, 1e-9);
        }

        // ---------- 토공 공기 ----------

        private static EarthworkPlan Plan(double soil, double rock, double fill, int trucks,
            double soilL = SoilL, double soilC = SoilC)
        {
            return new EarthworkPlan(
                new CutMaterials(soil, rock, 0.0, rock > 0 ? 2.0 : RockLine.None), fill,
                soilL, soilC, RockL, RockC,
                622.08, 0.35,
                trucks, 8.0, Truck, LoadMinutes,
                0.3, 5.0, 5.0);
        }

        [Test]
        public void 트럭이_모자라면_운반이_공기를_정한다()
        {
            // 절토 10000, 성토 4000 → 유용 4444.444, 사토 5555.556
            // 굴착 10000 / 622.08 = 16.075일
            // 트럭 가동 = 4444.444 / 47.7198 + 5555.556 / 13.6653 = 499.680시간
            // 트럭 2대: 499.680 / (2 × 8) = 31.230일 → 공기 32일 (운반이 느림)
            EarthworkPlan p = Plan(10000.0, 0.0, 4000.0, 2);

            Assert.AreEqual(16.075103, p.ExcavationDays, 1e-6);
            Assert.AreEqual(499.679927, p.TruckHours, 1e-6);
            Assert.AreEqual(31.229995, p.HaulDays, 1e-6);
            Assert.AreEqual(32, p.TotalDays);
            Assert.IsTrue(p.HaulIsBottleneck);

            // 굴착을 따라가려면 499.680 / (8 × 16.075) = 3.89 → 4대
            Assert.AreEqual(4, p.TrucksToKeepUp);
        }

        [Test]
        public void 트럭이_충분하면_굴착이_공기를_정한다()
        {
            // 트럭 4대: 499.680 / 32 = 15.615일 < 굴착 16.075일 → 공기 17일
            EarthworkPlan p = Plan(10000.0, 0.0, 4000.0, 4);

            Assert.AreEqual(15.614998, p.HaulDays, 1e-6);
            Assert.AreEqual(17, p.TotalDays);
            Assert.IsFalse(p.HaulIsBottleneck);
        }

        [Test]
        public void 암은_작업_비율만큼_굴착이_느리다()
        {
            // 토사 4000, 암 6000, 암 작업 비율 0.35 → 4000 / 622.08 + 6000 / 217.728 = 6.430 + 27.557 = 33.987 → 34일
            // 절토의 L, C는 부피 비율로 섞는다: C = (4000 × 0.9 + 6000 × 1.2) / 10000 = 1.08, L = 1.46
            EarthworkPlan p = Plan(4000.0, 6000.0, 0.0, 0);

            Assert.AreEqual(33.987360, p.ExcavationDays, 1e-6);
            Assert.AreEqual(34, p.TotalDays);
            Assert.AreEqual(1.08, p.Balance.CompactFactor, 1e-9);
            Assert.AreEqual(1.46, p.Balance.LooseFactor, 1e-9);
        }

        [Test]
        public void 암을_공기_안에_깨려면_브레이커가_몇_대_필요한가()
        {
            // 표준품셈 8-2-13 대형브레이커 보통암 암파쇄 3.1 ~ 3.7 m³/시간 → 3.4
            // 암 6,000 m³, 8시간, 공기 33.987일 → 6,000 ÷ (3.4 × 8 × 33.987 = 924.45) = 6.49 → 7대
            EarthworkPlan p = Plan(4000.0, 6000.0, 0.0, 0);
            Assert.AreEqual(7, p.BreakersToKeepUp);
            Assert.AreEqual(0, Plan(10000.0, 0.0, 4000.0, 2).BreakersToKeepUp);          // 암이 없으면 0
            Assert.AreEqual(2, RockBreaker.ToKeepUp(1000.0, 8.0, 20.0));                // 1,000 ÷ 544 = 1.84 → 2
            Assert.AreEqual(2, RockBreaker.ToKeepUp(1000.0, 8.0, 20.0, RockBreaker.SoftRockHourly));  // 연암 5.0: 1,000 ÷ 800 = 1.25 → 2
            Assert.AreEqual(1, RockBreaker.ToKeepUp(1000.0, 8.0, 25.0, RockBreaker.SoftRockHourly));  // 1,000 ÷ 1,000 = 1.0 → 1
        }

        [Test]
        public void 발파로_깨면_크롤러드릴이_몇_대_필요한가()
        {
            // 표준품셈 3-3-5 일반발파 TYPE-Ⅴ: 크롤러드릴 0.014 시간/m³ → 71.4 m³/시간 (브레이커 3.4의 21배)
            Assert.AreEqual(1.0 / 0.014, RockBreaker.GeneralBlast.HourlyPerDrill, 1e-9);
            // 암 639,424 m³, 8시간, 770일: 639,424 × 0.014 = 8,951.9시간 ÷ 6,160 = 1.45 → 2대
            Assert.AreEqual(2, RockBreaker.DrillsToKeepUp(639424.0, 8.0, 770.0, RockBreaker.GeneralBlast));
            // 미진동굴착 TYPE-Ⅰ (0.100): 63,942시간 ÷ 6,160 = 10.4 → 11대 — 진동 제한이 크면 드릴이 5배 넘게 필요
            Assert.AreEqual(11, RockBreaker.DrillsToKeepUp(639424.0, 8.0, 770.0, RockBreaker.BlastTypes[0]));
            // 앞 시험의 암 6,000 m³, 33.987일: 84 ÷ 271.9 = 0.31 → 1대
            Assert.AreEqual(1, Plan(4000.0, 6000.0, 0.0, 0).DrillsToKeepUp(RockBreaker.GeneralBlast));
            Assert.AreEqual(6, RockBreaker.BlastTypes.Length);
        }

        [Test]
        public void 토취는_토사의_C로_나눈다()
        {
            // 토사 1000 + 암 1000 → C절토 = 1.05, 다지면 2100. 성토 5000에 2900이 모자람
            // 토취(토사) = 2900 / 0.9 = 3222.222
            EarthworkPlan p = Plan(1000.0, 1000.0, 5000.0, 0);

            Assert.AreEqual(1.05, p.Balance.CompactFactor, 1e-9);
            Assert.AreEqual(3222.222222, p.Balance.BorrowVolume, 1e-6);
            Assert.AreEqual(3222.222222 * 1.25, p.Balance.BorrowLooseVolume, 1e-6);
        }

        [Test]
        public void 계수가_1이고_운반을_빼면_예전_공기와_같다()
        {
            // 예전 식: max(절토, 성토) / 하루 작업량 → 절토 10000, 성토 4000 → 16.075 → 17일
            //          절토 4000, 성토 10000 → 같은 17일 (토취 6000을 판다)
            Assert.AreEqual(17, Plan(10000.0, 0.0, 4000.0, 0, 1.0, 1.0).TotalDays);
            Assert.AreEqual(17, Plan(4000.0, 0.0, 10000.0, 0, 1.0, 1.0).TotalDays);
            Assert.AreEqual(10000.0 / 622.08, Plan(4000.0, 0.0, 10000.0, 0, 1.0, 1.0).ExcavationDays, 1e-9);
        }

        [Test]
        public void 재생용_공정은_절토와_성토가_같은_날_끝난다()
        {
            // 트럭 2대 → 공기(올림 전) 31.230일. 하루 절토 10000 / 31.230, 하루 성토 4000 / 31.230
            EarthworkSchedule s = Plan(10000.0, 0.0, 4000.0, 2).ToSchedule();

            Assert.AreEqual(32, s.TotalDays);
            Assert.AreEqual(10000.0 / 31.229995, s.CutPerDay, 1e-3);
            Assert.AreEqual(s.CutDays, s.FillDays, 1e-9);
            Assert.AreEqual(5000.0, s.CumulativeCut(31.229995 / 2.0), 1e-3);
        }

        // ---------- 대안 분석에 연결 ----------

        private static SiteConditions SiteWithHaul()
        {
            // Phase 4 손계산과 같은 건물 (2층, 터파기 480, 가설 5·기초 7·크레인 3·기둥 3·슬래브 5일)
            var setup = new EarthworkSetup(
                new CutMaterials(10000.0, 0.0, 0.0, RockLine.None), SoilL, SoilC, RockL, RockC, 0.35,
                Truck, LoadMinutes, 0.3, 5.0, 5.0);
            return new SiteConditions(10000.0, 4000.0, 77.76, 2, 480.0,
                new BuildingDurations(5.0, 0.0, 7.0, 3.0, 3.0, 5.0), setup);
        }

        [Test]
        public void 대안_분석에_트럭_대수와_트럭_비용이_들어간다()
        {
            // 굴착기 1대, 8시간, 굴착기 10만 원/시간, 트럭 5만 원/시간, 기간 50만 원/일
            // 트럭 2대: 토공 32일 (운반이 느림), 터파기 1일, 전체 32 + 1 + 7 + 2 × 8 = 56일
            //   굴착기 1 × 33 × 8 × 10만 = 2,640만, 트럭 2 × 32 × 8 × 5만 = 2,560만, 기간 56 × 50만 = 2,800만 → 8,000만
            AlternativeResult two = AlternativeAnalysis.Evaluate(
                new AlternativeInput("A", 1, 8.0, 1.0, 100000.0, 500000.0, 2, 50000.0), SiteWithHaul());

            Assert.AreEqual(32, two.EarthworkDays);
            Assert.AreEqual(56, two.ProjectDays);
            Assert.AreEqual(26400000.0, two.ExcavatorCost, 1e-6);
            Assert.AreEqual(25600000.0, two.TruckCost, 1e-6);
            Assert.AreEqual(80000000.0, two.TotalCost, 1e-6);
            Assert.IsTrue(two.Earthwork.HaulIsBottleneck);

            // 트럭 4대: 토공 17일, 전체 41일
            //   굴착기 1 × 18 × 8 × 10만 = 1,440만, 트럭 4 × 17 × 8 × 5만 = 2,720만, 기간 2,050만 → 6,210만
            AlternativeResult four = AlternativeAnalysis.Evaluate(
                new AlternativeInput("B", 1, 8.0, 1.0, 100000.0, 500000.0, 4, 50000.0), SiteWithHaul());

            Assert.AreEqual(17, four.EarthworkDays);
            Assert.AreEqual(41, four.ProjectDays);
            Assert.AreEqual(62100000.0, four.TotalCost, 1e-6);
        }

        [Test]
        public void 대수만_바꾸는_그래프에서도_트럭_대수는_그대로다()
        {
            AlternativeResult[] sweep = AlternativeAnalysis.SweepMachines(
                new AlternativeInput("지금", 1, 8.0, 1.0, 100000.0, 500000.0, 2, 50000.0), SiteWithHaul(), 3);

            foreach (AlternativeResult r in sweep) Assert.AreEqual(2, r.Input.Trucks);
            // 트럭 2대로는 운반이 31.2일 걸려서 굴착기를 늘려도 토공은 32일에서 줄지 않는다
            Assert.AreEqual(32, sweep[0].EarthworkDays);
            Assert.AreEqual(32, sweep[2].EarthworkDays);
        }

        [Test]
        public void 표준품셈_2025_트럭()
        {
            // 화면 기본값 (2025 표준품셈 값, 출처는 Docs/ASSUMPTIONS.md). 굴착기 Cms 20초·Es 0.6·K 0.9로 적재 시간만 맞춰 비교한다.
            // 자연 15 / 1.7 = 8.8235, n = 8.8235 × 1.25 / 0.9 = 12.255, t1 = 20 × 12.255 / 36 = 6.8083분
            DumpTruck on = DumpTruck.OnSite2025, off = DumpTruck.OffSite2025;
            double t1 = on.LoadMinutes(SoilL, 1.0, 0.9, 20.0, 0.6);
            Assert.AreEqual(6.808279, t1, 1e-6);

            // 현장 안 0.3 km, 15 / 20 km/h: t2 = (0.02 + 0.015) × 60 = 2.1 → cm = 6.8083 + 2.1 + 1.05 + 0.42 = 10.3783
            //   Q = 60 × 8.8235 × 0.9 / 10.3783 = 45.9104
            Assert.AreEqual(10.378279, on.CycleMinutes(0.3, t1), 1e-6);
            Assert.AreEqual(45.910367, on.HourlyNatural(0.3, t1), 1e-6);

            // 현장 밖 5 km, 30 / 35 km/h, 덮개 0.5 + 세륜 1.5: t2 = (5/30 + 5/35) × 60 = 18.5714
            //   cm = 6.8083 + 18.5714 + 1.05 + 0.42 + 0.5 + 1.5 = 28.8497 → Q = 16.5156
            Assert.AreEqual(28.849707, off.CycleMinutes(5.0, t1), 1e-6);
            Assert.AreEqual(16.515612, off.HourlyNatural(5.0, t1), 1e-6);
        }

        [Test]
        public void 할_일이_없으면_공기는_0이다()
        {
            EarthworkPlan p = Plan(0.0, 0.0, 0.0, 2);
            Assert.AreEqual(0, p.TotalDays);
            Assert.AreEqual(0, p.TrucksToKeepUp);
        }
    }
}
