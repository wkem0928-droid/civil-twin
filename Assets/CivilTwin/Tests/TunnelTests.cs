using System.IO;
using CivilTwin.Core;
using NUnit.Framework;
using UnityEngine;

namespace CivilTwin.Tests
{
    /// <summary>
    /// 터널 NATM 공정 (2026-11-21). 싸이클은 표준품셈 토목 3-2-1 C군, 지보 패턴은 한국도로공사 표준 패턴(Ex-TM 글 표 1).
    /// 손계산 값은 Python으로 같은 식을 따로 계산해 맞춘 것이다.
    /// </summary>
    public class TunnelTests
    {
        // ---------- NATM 1발파 싸이클 ----------

        [Test]
        public void 로더_작업량과_여굴()
        {
            // 8-2-5: 3600 × 5.0 × 0.55 × 1 × 0.55 / (1.8 × 8 + 18 + 14 = 46.4초) = 117.35 m³/시간 (흐트러진 상태)
            var crew = new NatmCrew();
            Assert.AreEqual(46.4, crew.LoaderCycleSeconds, 1e-9);
            Assert.AreEqual(117.3491, crew.LoaderHourlyLoose, 1e-3);
            // 3-1-2 여굴: 아치 20 × 0.155 + 측벽 8 × 0.15 + 바닥 12 × 0.125 = 5.8 m²
            Assert.AreEqual(5.8, TunnelSection.TwoLaneRoad().OverbreakArea, 1e-9);
        }

        [Test]
        public void 천공속도는_굴진장에_따라_다르다()
        {
            // 3-2-3 점보드릴: 1.2 m 이하 75 ~ 85, 2.0 이하 85 ~ 95, 3.0 이하 95 ~ 105, 초과 105 ~ 120 (가운데)
            Assert.AreEqual(80.0, NatmCycle.DrillSpeedCmPerMinute(1.2));
            Assert.AreEqual(90.0, NatmCycle.DrillSpeedCmPerMinute(1.5));
            Assert.AreEqual(100.0, NatmCycle.DrillSpeedCmPerMinute(2.5));
            Assert.AreEqual(112.5, NatmCycle.DrillSpeedCmPerMinute(4.0));
        }

        [Test]
        public void P3_싸이클_손계산()
        {
            // P-3 (굴진장 2.5 m, 숏크리트 9 cm, 록볼트 L 4 m @ 1.5 m, 강지보 없음)
            //   고정 = 30 + 17.5 + 55 + 27.5 + 10 + 45 + 20 + 10 + 15 = 230분
            //   T1 = 1.6 × 90 = 144공 × (2.5 / 0.9 × 100 cm) ÷ 100 cm/분 ÷ 3붐 = 133.33
            //   T2 = (90 + 5.8) × 2.5 × 1.6 ÷ 117.35 × 60 = 195.93
            //   T3 = 28 × 2.5 = 70 m² ÷ 64 × 60 = 65.625
            //   T5 = 70 × 0.09 = 6.3 m³ ÷ (18 × 0.55 × 0.85 = 8.415) × 60 = 44.92
            //   T6 = (28 / 1.5) × (2.5 / 1.5) = 31.11개 × (400 / 100 + 5 = 9분) ÷ 3 = 93.33
            //   합 763.14분 → 24시간 1,440 ÷ 763.14 = 1.887발파 × 2.5 = 4.717 m/일
            var c = new NatmCycle(SupportPattern.P3, TunnelSection.TwoLaneRoad(), new NatmCrew());
            Assert.AreEqual(31.1111, c.BoltsPerRound, 1e-3);
            Assert.AreEqual(763.1396, c.TotalMinutes, 1e-3);
            Assert.AreEqual(4.7174, c.AdvancePerDay(24.0), 1e-3);
            double t1 = 0, t2 = 0;
            foreach (CycleItem item in c.Items)
            {
                if (item.Name.StartsWith("천공 T1")) t1 = item.Minutes;
                if (item.Name.StartsWith("버력처리 T2")) t2 = item.Minutes;
            }
            Assert.AreEqual(133.3333, t1, 1e-3);
            Assert.AreEqual(195.9282, t2, 1e-3);
        }

        [Test]
        public void 상하반_분할_굴착_손계산()
        {
            // 3-2-1 주 ⑤ 비고(하반): 고정 작업 일부 65 %, T1 ~ T6은 하반 물량. 상반 = 면적 60 % (54 m²)·아치 20 m, 하반 = 36 m²·측벽 8 m
            // P-5 하반 (1.2 m): 고정 (30 + 17.5 + 55 + 45 + 42.5 + 20) × 0.65 + 27.5 + 10 + 10 + 15 = 199.0
            //   T1 = 1.6 × 36 × (1.2 / 0.9 × 100) ÷ 80 ÷ 3 = 32.0, T2 = (36 + 8 × 0.15 + 12 × 0.125) × 1.2 × 1.6 ÷ 117.35 × 60 = 37.99
            //   T3 = 8 × 1.2 ÷ 64 × 60 = 9.0, T5 = 8 × 1.2 × 0.16 ÷ 8.415 × 60 = 10.95, T6 = (8/1.5)(1.2/1.5) = 4.267개 × 10분 ÷ 3 = 14.22
            //   합 303.17분
            var section = TunnelSection.TwoLaneRoad();
            var p5 = new NatmCycle(SupportPattern.P5, section, new NatmCrew());
            Assert.AreEqual(303.1653, p5.BenchMinutes, 1e-3);
            // 1 m당 = 462.0 ÷ 1.2 + 303.2 ÷ 1.2 = 637.63분 → 24시간 2.258 m/일 (전단면으로 보면 566.2분 ÷ 1.2 m → 3.05 m/일)
            Assert.AreEqual(637.6290, p5.MinutesPerMeter, 1e-3);
            Assert.AreEqual(2.2584, p5.AdvancePerDay(24.0), 1e-3);
            // P-4 (상반 1.5 m / 하반 3.0 m): 491.67 ÷ 1.5 + 433.01 ÷ 3.0 = 472.12분/m → 3.050 m/일
            var p4 = new NatmCycle(SupportPattern.P4, section, new NatmCrew());
            Assert.AreEqual(491.6699, p4.TotalMinutes, 1e-3);
            Assert.AreEqual(433.0129, p4.BenchMinutes, 1e-3);
            Assert.AreEqual(3.0501, p4.AdvancePerDay(24.0), 1e-3);
            // 록볼트는 상반 + 하반 = 둘레 28 m ÷ 1.5² = 12.44개/m (전단면과 같다)
            Assert.AreEqual(28.0 / 2.25, p5.BoltsPerMeter, 1e-9);
            // 전단면 패턴은 하반이 없다
            Assert.AreEqual(0.0, new NatmCycle(SupportPattern.P3, section, new NatmCrew()).BenchMinutes);
        }

        [Test]
        public void 패턴별_싸이클과_로더_대수()
        {
            var section = TunnelSection.TwoLaneRoad();
            Assert.AreEqual(888.2656, new NatmCycle(SupportPattern.P1, section, new NatmCrew()).TotalMinutes, 1e-3);
            Assert.AreEqual(461.9895, new NatmCycle(SupportPattern.P5, section, new NatmCrew()).TotalMinutes, 1e-3);   // 상반
            // 좋은 암반(P-1)에서는 버력처리 T2(313분)가 가장 길다 → 로더 2대면 T2가 반 (156.7분 줄어듦)
            double one = new NatmCycle(SupportPattern.P1, section, new NatmCrew()).TotalMinutes;
            double two = new NatmCycle(SupportPattern.P1, section, new NatmCrew { Loaders = 2 }).TotalMinutes;
            Assert.AreEqual(313.4850 / 2.0, one - two, 1e-3);
        }

        [Test]
        public void 막장이_깊어질수록_버력_트럭이_더_필요하다()
        {
            // 8-2-8 적재기계 사용: Qt = 15 / 2.65 × 1.6 = 9.057 m³, n = 9.057 / (5 × 0.55) = 3.293회,
            //   적재 = 46.4 × 3.293 / (60 × 0.55) = 4.630분
            var crew = new NatmCrew();
            Assert.AreEqual(9.0566, TunnelMuckHaul.TruckLooseVolume(crew), 1e-3);
            Assert.AreEqual(3.2933, TunnelMuckHaul.LoaderCycles(crew), 1e-3);
            Assert.AreEqual(4.6304, TunnelMuckHaul.LoadMinutes(crew), 1e-3);
            // 깊이 0 (사토장까지 1 km): 4.63 + 4 + 3 + 0.8 + 0.42 = 12.85분 → 2.78 → 3대
            Assert.AreEqual(12.8504, TunnelMuckHaul.CycleMinutes(crew, 0.0), 1e-3);
            Assert.AreEqual(3, TunnelMuckHaul.TrucksNeeded(crew, 0.0));
            // 깊이 5 km (6 km): 4.63 + 24 + 18 + 1.22 = 47.85분 → 10.33 → 11대
            Assert.AreEqual(11, TunnelMuckHaul.TrucksNeeded(crew, 5000.0));
            // 로더 2대면 동시에 두 대를 싣는다 → 21대
            Assert.AreEqual(21, TunnelMuckHaul.TrucksNeeded(new NatmCrew { Loaders = 2 }, 5000.0));
        }

        // ---------- 막장 공정 ----------

        [Test]
        public void 양_끝_막장_두_개()
        {
            // 1,000 m를 양 끝에서 5 m/일씩 → 500 m씩 → 100일
            var s = new TunnelSchedule(1000.0, new[] { new TunnelFace(0.0, 1, 0.0, "서"), new TunnelFace(1000.0, -1, 0.0, "동") }, _ => 5.0);
            Assert.AreEqual(100.0, s.FinishDay, 1e-9);
            Assert.AreEqual(500.0, s.FacePosition(0, 100.0), 1e-9);
            Assert.AreEqual(250.0, s.ExcavatedLength(25.0), 1e-9);
            Assert.IsTrue(s.IsExcavated(100.0, 25.0));
            Assert.IsFalse(s.IsExcavated(500.0, 25.0));
        }

        [Test]
        public void 가운데_작업구로_막장을_더_열면_빨라진다()
        {
            // 40일째에 가운데(500 m)에서 양쪽으로 2개 더: 40일까지 양 끝 200 m씩 → 남은 300 m씩을 10 m/일 → 30일 → 70일
            var s = new TunnelSchedule(1000.0, new[]
            {
                new TunnelFace(0.0, 1, 0.0, "서"), new TunnelFace(1000.0, -1, 0.0, "동"),
                new TunnelFace(500.0, -1, 40.0, "가운데 → 서"), new TunnelFace(500.0, 1, 40.0, "가운데 → 동"),
            }, _ => 5.0);
            Assert.AreEqual(70.0, s.FinishDay, 1e-9);
            Assert.AreEqual(350.0, s.FacePosition(0, 70.0), 1e-9);
            Assert.AreEqual(350.0, s.FacePosition(2, 70.0), 1e-9);
            Assert.IsTrue(s.IsActive(2, 50.0));
            Assert.IsFalse(s.IsActive(2, 20.0));   // 아직 시작 전
        }

        [Test]
        public void 토피가_얕은_곳부터_나쁜_패턴()
        {
            // 토피 10개, 비율 P-1 50 %, P-5 50 % → 얕은 5개가 P-5(4), 깊은 5개가 P-1(0)
            double[] cover = { 5, 100, 200, 15, 300, 30, 400, 10, 500, 20 };
            int[] p = TunnelGround.AssignPatternsByCover(cover, new[] { 0.5, 0, 0, 0, 0.5 });
            CollectionAssert.AreEqual(new[] { 4, 0, 0, 4, 0, 4, 0, 4, 0, 4 }, p);
        }

        // ---------- 대우건설 인제양양터널 (지형 파일이 있을 때) ----------

        private static ElevationGrid InjeGrid()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Data", "sites", TunnelStructures.InjeFile));
            if (!File.Exists(path)) Assert.Ignore("DEM 파일이 없어 건너뜀: " + path);
            return AscGridReader.ReadFile(path);
        }

        [Test]
        public void 인제양양터널_선형과_토피()
        {
            TunnelSpec spec = TunnelStructures.Inje;
            Assert.AreEqual(10970.972, spec.AlignmentA().Length, 0.01);    // 공식 10,965 m와 0.06 %
            Assert.AreEqual(10972.1, spec.AlignmentB().Length, 0.1);
            Assert.AreEqual(348.0, spec.MiddleStartDay, 1e-9);             // 2010-05-01 → 2011-04-14
            Assert.AreEqual(878, spec.ActualDays);                          // → 2012-09-25

            var plan = new TunnelPlan(spec, InjeGrid(), 24.0, 1, true);
            Assert.AreEqual(549, plan.Cover.Length);
            double max = plan.MaxCover(out double at);
            Assert.AreEqual(524.62, max, 0.05);                             // 보도 "지표면에서 200 ~ 550 m 아래"
            Assert.AreEqual(8600.0, at, 1e-9);
            // 패턴 비율이 전국 시공 비율과 같게 나뉘었다 (표본 549개)
            Assert.AreEqual(0.366, plan.PatternShare(2), 0.002);
            Assert.AreEqual(0.184, plan.PatternShare(4), 0.002);
            Assert.AreEqual(4, plan.PatternAt(0.0), "입구(토피 얕음)는 P-5");
        }

        [Test]
        public void 인제양양터널_관통일을_실제와_비교한다()
        {
            ElevationGrid grid = InjeGrid();
            TunnelSpec spec = TunnelStructures.Inje;
            // 24시간 작업, 막장 4개(양 끝 + 사갱 2개, 사갱은 348일째부터), 로더 1대 → 927일 (실제 878일, +5.6 %)
            // (2026-11-21 전에는 P-4·P-5를 전단면으로 봐서 847.25일(−3.5 %)이었다. 3-2-1 주 ⑤ 하반 싸이클을 넣어 바뀜)
            var plan = new TunnelPlan(spec, grid, 24.0, 1, true);
            Assert.AreEqual(927.0, plan.Schedule.FinishDay, 1e-6);
            Assert.AreEqual(23.670, plan.CombinedDailyAdvance, 0.001);     // 보도 하루 평균 25 m
            TestContext.WriteLine("관통 " + plan.Schedule.FinishDay + "일 (실제 " + spec.ActualDays + "일), 두 튜브 하루 "
                + plan.CombinedDailyAdvance.ToString("F1") + " m");

            // 사갱이 없었다면: 막장 2개 → 1,420일 (493일 더 걸린다)
            Assert.AreEqual(1420.0, new TunnelPlan(spec, grid, 24.0, 1, false).Schedule.FinishDay, 1e-6);
            // 로더 2대 → 852.25일, 하루 20시간 → 1,077.75일
            Assert.AreEqual(852.25, new TunnelPlan(spec, grid, 24.0, 2, true).Schedule.FinishDay, 1e-6);
            Assert.AreEqual(1077.75, new TunnelPlan(spec, grid, 20.0, 1, true).Schedule.FinishDay, 1e-6);

            // 설계 단면으로 판 양: 90 × 10,970.97 × 2 = 1,974,775 m³ ↔ 보도 반출 토석 196만 m³ (+0.8 %)
            Assert.AreEqual(1974775.0, plan.DesignVolume, 1.0);
        }

        [Test]
        public void 터널_보고서에_계산_과정과_대안이_들어간다()
        {
            ElevationGrid grid = InjeGrid();
            TunnelSpec spec = TunnelStructures.Inje;
            var plan = new TunnelPlan(spec, grid, 24.0, 1, true);
            string md = TunnelReport.Markdown(plan, new TunnelPlan(spec, grid, 24.0, 1, false), new TunnelPlan(spec, grid, 24.0, 2, true), "2026-11-21 00:00");

            StringAssert.Contains("# 터널 굴착 계산 보고서 — 대우건설 인제양양터널", md);
            StringAssert.Contains("관통 **927일**", md);
            StringAssert.Contains("관통 = 878일, 하루 약 25 m → 계산이 +5.6 %.", md);
            StringAssert.Contains("| 사갱 없음 (막장 2개) | 1,420 | +493일 |", md);
            StringAssert.Contains("| 로더 2대 | 852 | −75일 |", md);
            StringAssert.Contains("| P-5 | 1.2 / 1.2 (상/하반) |", md);
            // 위험 시나리오 (지형을 주면): 용수 +30 %
            string withSurface = TunnelReport.Markdown(plan, null, null, "2026-11-21 00:00", grid);
            StringAssert.Contains("| 용수 많음 (싸이클 +30 %, 주 ⑦) |", withSurface);
            TestContext.WriteLine("용수 +30 %: " + new TunnelPlan(spec, grid, 24.0, 1, true, 0.3).Schedule.FinishDay + "일");
            StringAssert.Contains("| 5,000 m | 47.9 | 11대 |", md);        // 버력 트럭 (8-2-8)
            StringAssert.Contains("| 착암 | 천공 T1 | 133.3 |", md);        // P-3 싸이클 내역
            StringAssert.Contains("| | **합** | **763.1** | |", md);

            // 조건 조합표 (지형을 주면): 24시간·로더 1대 = 927 / 1,420일, 20시간·로더 1대 = 1,078 / 1,704일
            string matrix = TunnelReport.ConditionMatrix(spec, grid);
            StringAssert.Contains("| 24시간 | 1대 | 927 | 1,420 |", matrix);
            StringAssert.Contains("| 20시간 | 1대 | 1,078 | 1,704 |", matrix);
            StringAssert.Contains("| 24시간 | 2대 | 852 |", matrix);
            StringAssert.Contains("## 6. 지보재 물량", md);
            StringAssert.Contains("## 7. 2차 라이닝과 방수", md);
            StringAssert.Contains("| 2조 (기본) | 5,485 | 458 | 458 | 1,389일 (2014.2) |", md);
            StringAssert.Contains("## 9. 가정과 출처", md);
        }

        // ---------- 2차 라이닝 ----------

        [Test]
        public void 라이닝_손계산_1000m()
        {
            // 길이 1,000 m × 튜브 2개, 관통 100일, 둘레 28 m, 거푸집 튜브마다 2조, span 12 m, 두께 30 cm
            var l = new TunnelLining(1000.0, 2, 100.0, 28.0, 2, 12.0, 0.30);
            Assert.AreEqual(104.0, l.StartDay, 1e-9);                 // 관통 + 작업대차 조립 4일 (3-4-2)
            Assert.AreEqual(500.0, l.SegmentLength, 1e-9);
            Assert.AreEqual(42, l.SpansPerForm);                      // 500 ÷ 12 = 41.7 → 42
            Assert.AreEqual(146.0, l.FinishDay, 1e-9);                // 104 + 42 × 1일 (3-3-1)

            // 114일째: 거푸집마다 10 span = 120 m. 서쪽 거푸집은 0 → 120, 동쪽 거푸집은 1,000 → 880
            Assert.AreEqual(240.0, l.LinedLength(114.0), 1e-9);
            Assert.IsTrue(l.IsLined(50.0, 114.0));
            Assert.IsFalse(l.IsLined(130.0, 114.0));
            Assert.IsTrue(l.IsLined(950.0, 114.0));
            Assert.IsTrue(l.IsLined(880.0, 114.0));
            Assert.IsFalse(l.IsLined(870.0, 114.0));
            Assert.IsFalse(l.IsLined(50.0, 103.0));                   // 시작 전
            Assert.AreEqual(1000.0, l.LinedLength(146.0), 1e-9);

            // 물량·인력: 콘크리트 28 × 0.3 × 1,000 × 2 = 16,800 m³, 1회 28 × 0.3 × 12 = 100.8 m³
            Assert.AreEqual(16800.0, l.Concrete, 1e-6);
            Assert.AreEqual(100.8, l.ConcretePerPour, 1e-9);
            Assert.AreEqual(84, l.PoursPerTube);
            Assert.AreEqual(84 * 2 * 11.0, l.LiningLabor, 1e-9);      // 1,848인
            Assert.AreEqual(56000.0, l.WaterproofArea, 1e-6);
            Assert.AreEqual(56000.0 * 0.013, l.WaterproofLabor, 1e-6); // 728인
            Assert.AreEqual(2 * 2 * 12 * 28 * 0.011, l.WaterproofWorkersPerDay, 1e-9); // 14.8명
        }

        [Test]
        public void 인제양양터널_라이닝은_개통_전에_끝난다()
        {
            var plan = new TunnelPlan(TunnelStructures.Inje, InjeGrid(), 24.0, 1, true);
            TunnelLining l = TunnelLining.For(plan);
            // 10,970.97 ÷ 2 = 5,485.5 m ÷ 12 = 457.1 → 458 span. 관통 927 + 4 + 458 = 1,389일 (2014-02)
            Assert.AreEqual(458, l.SpansPerForm);
            Assert.AreEqual(1389.0, l.FinishDay, 1e-6);
            System.DateTime end = TunnelStructures.Inje.ExcavationStart.AddDays(l.FinishDay);
            Assert.Less(end, TunnelStructures.Inje.Opening);          // 개통 2017-06-30 전
            Assert.AreEqual(229, TunnelLining.For(plan, 4).SpansPerForm);
            Assert.AreEqual(915, TunnelLining.For(plan, 1).SpansPerForm);   // 10,971 ÷ 12 = 914.2 → 915

            // 굴착과 병행하면 관통 뒤 시작보다 일찍 끝나고, 관통보다 늦게 끝난다 (관통 뒤에도 남은 구간을 친다)
            TunnelLining c = TunnelLining.Concurrently(plan);
            Assert.Less(c.FinishDay, l.FinishDay);
            Assert.Greater(c.FinishDay, plan.Schedule.FinishDay);
            Assert.Less(c.StartDay, plan.Schedule.FinishDay);
            TestContext.WriteLine("병행: " + c.StartDay + " ~ " + c.FinishDay + "일 (관통 뒤: " + l.StartDay + " ~ " + l.FinishDay + "일)");
        }

        [Test]
        public void 용수_할증_30퍼센트()
        {
            // 3-2-1 주 ⑦: 용수로 지장을 받으면 싸이클을 30 %까지 늘린다. P-3: 763.14 × 1.3 = 992.08분 → 4.717 ÷ 1.3 = 3.629 m/일
            var section = TunnelSection.TwoLaneRoad();
            var wet = new NatmCycle(SupportPattern.P3, section, new NatmCrew { WaterIncrease = 0.3 });
            Assert.AreEqual(763.1396 * 1.3, wet.TotalMinutes, 1e-3);
            Assert.AreEqual(4.7174 / 1.3, wet.AdvancePerDay(24.0), 1e-3);
            double item = 0;
            foreach (CycleItem it in wet.Items) if (it.Name.StartsWith("용수 할증")) item = it.Minutes;
            Assert.AreEqual(763.1396 * 0.3, item, 1e-3);               // 할증이 싸이클 내역에 한 줄로 보인다
            // 분할 굴착은 상반·하반 모두 30 %
            var p5 = new NatmCycle(SupportPattern.P5, section, new NatmCrew { WaterIncrease = 0.3 });
            Assert.AreEqual(303.1653 * 1.3, p5.BenchMinutes, 1e-3);
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                new TunnelPlan(TunnelStructures.Inje, new ElevationGrid(2, 2, 30.0, 0.0, 0.0, new double[4]), 24.0, 1, true, 0.31));
        }

        [Test]
        public void 단면_모식도_기하()
        {
            // 마제형: πR²/2 + 2RH = 90 → R = 6, H = (90 − 56.549) ÷ 12 = 2.788 m
            var p3 = new TunnelCrossSection(SupportPattern.P3);
            Assert.AreEqual(2.7876, p3.SpringLine, 1e-3);
            Assert.AreEqual(90.0, p3.Area, 1e-9);
            double crown = p3.SpringLine + TunnelCrossSection.Radius;
            Assert.AreEqual(SectionPart.Opening, p3.Classify(0.0, crown - 1.0));
            Assert.AreEqual(SectionPart.Shotcrete, p3.Classify(0.0, crown + 0.1));      // 9 cm × 3배 = 27 cm 띠
            Assert.AreEqual(SectionPart.Bolt, p3.Classify(0.0, crown + 2.0));           // 마루의 록볼트 (L 4 m)
            Assert.AreEqual(SectionPart.Rock, p3.Classify(0.0, crown + 4.5));           // 록볼트 끝 너머
            Assert.AreEqual(SectionPart.Bolt, p3.Classify(TunnelCrossSection.Radius + 1.0, p3.SpringLine - 1.5));  // 측벽 록볼트 (1.5 m 아래)
            Assert.AreEqual(SectionPart.Opening, p3.Classify(0.1, p3.SpringLine));     // 전단면: 상하반 경계 없음
            Assert.AreEqual(SectionPart.Floor, p3.Classify(0.0, -0.1));

            // P-1 랜덤 록볼트: 마루(0°)에는 없다
            Assert.AreEqual(SectionPart.Rock, new TunnelCrossSection(SupportPattern.P1).Classify(0.0, crown + 2.0));
            // P-4: 상하반 경계(스프링라인 점선), 강지보(숏크리트 띠 가운데 = 12 cm × 3 ÷ 2 = 18 cm)
            var p4 = new TunnelCrossSection(SupportPattern.P4);
            Assert.AreEqual(SectionPart.BenchLine, p4.Classify(0.1, p4.SpringLine));
            Assert.AreEqual(SectionPart.SteelRib, p4.Classify(0.0, crown + 0.18));
            // 라이닝을 쳤으면 윤곽 안쪽 30 cm × 3 = 90 cm 띠
            Assert.AreEqual(SectionPart.Lining, new TunnelCrossSection(SupportPattern.P3, lined: true).Classify(0.0, crown - 0.5));
        }

        [Test]
        public void 굴착_작업인원_손계산()
        {
            // 3-2-5 C군: 1 + 1 + 1 + 1 + 1 + 1 + 보통인부 3 + 특별인부 4 + 화약취급공 1 = 14인 (소계 13 ~ 15의 가운데)
            Assert.AreEqual(14.0, TunnelCrew.PerFace, 1e-9);
            Assert.AreEqual(3, TunnelCrew.Shifts(24.0));
            Assert.AreEqual(3, TunnelCrew.Shifts(20.0));
            Assert.AreEqual(2, TunnelCrew.Shifts(16.0));

            // 1,000 m, 양 끝 막장 5 m/일 → 막장마다 100일. 튜브 1개, 24시간: 2 × 100 × 14 × 24 ÷ 8 = 8,400인·일
            var two = new TunnelSchedule(1000.0, new[] { new TunnelFace(0.0, +1, 0.0, "서"), new TunnelFace(1000.0, -1, 0.0, "동") }, _ => 5.0);
            Assert.AreEqual(100.0, two.WorkingDays(0), 1e-9);
            Assert.AreEqual(8400.0, TunnelCrew.LaborDays(two, 1, 24.0), 1e-6);
            Assert.AreEqual(2 * 14 * 3, TunnelCrew.PeopleOnDay(two, 1, 24.0, 50.0), 1e-9);   // 84명
            Assert.AreEqual(0.0, TunnelCrew.PeopleOnDay(two, 1, 24.0, 100.0), 1e-9);         // 관통 뒤

            // 40일째 가운데(500 m)에 막장 2개 추가 → 70일에 관통. 판 날 수 70 + 70 + 30 + 30 = 200 → 같은 8,400인·일
            // (같은 속도로 같은 길이를 파면 연인원은 같고, 기간만 줄어든다 — 사갱은 사람을 더 쓰는 것이 아니라 동시에 쓰는 것)
            var four = new TunnelSchedule(1000.0, new[]
            {
                new TunnelFace(0.0, +1, 0.0, "서"), new TunnelFace(1000.0, -1, 0.0, "동"),
                new TunnelFace(500.0, -1, 40.0, "가운데 서"), new TunnelFace(500.0, +1, 40.0, "가운데 동"),
            }, _ => 5.0);
            Assert.AreEqual(70.0, four.FinishDay, 1e-9);
            Assert.AreEqual(30.0, four.WorkingDays(2), 1e-9);
            Assert.AreEqual(8400.0, TunnelCrew.LaborDays(four, 1, 24.0), 1e-6);
            Assert.AreEqual(4 * 14 * 3, TunnelCrew.PeopleOnDay(four, 1, 24.0, 50.0), 1e-9);  // 168명
        }

        [Test]
        public void 라이닝_굴착과_병행_손계산()
        {
            // 길이 1,000 m, 양 끝 막장 5 m/일 → 관통 100일. 거푸집 2조, 막장과 거리 100 m
            var s = new TunnelSchedule(1000.0, new[] { new TunnelFace(0.0, +1, 0.0, "서"), new TunnelFace(1000.0, -1, 0.0, "동") }, _ => 5.0);
            Assert.AreEqual(100.0, s.FinishDay, 1e-9);
            var l = new TunnelLining(1000.0, 1, s.FinishDay, 28.0, 2, 12.0, 0.30, concurrent: true, lag: 100.0, excavated: s.IsExcavated);
            // m번째 span은 (12m + 100) ÷ 5일 이후 첫 날: 첫 span 22.4 → 23일째
            Assert.AreEqual(23.0, l.StartDay, 1e-9);
            // 관통 전 마지막 span m = 32: 484 ÷ 5 = 96.8 → 97일째 → 99일째에 384 m씩
            Assert.AreEqual(768.0, l.LinedLength(99.0), 1e-9);
            Assert.IsTrue(l.IsLined(380.0, 99.0));
            Assert.IsFalse(l.IsLined(390.0, 99.0));
            // 남은 116 m = 10 span (100 ~ 109일째) → 110일 (관통 뒤 시작이면 104 + 42 = 146일)
            Assert.AreEqual(110.0, l.FinishDay, 1e-9);
            Assert.AreEqual(146.0, new TunnelLining(1000.0, 1, 100.0, 28.0).FinishDay, 1e-9);
        }

        // ---------- 지보재 물량 ----------

        [Test]
        public void 지보재_1m당_물량_손계산()
        {
            // 둘레(아치 20 + 측벽 8) = 28 m. 숏크리트 = 28 × 두께, 반발 15 % → ÷ 0.85
            // 록볼트(격자) = 28 ÷ 간격², 랜덤(P-1) = 4개 ÷ 4.0 m. 강지보 = 1 ÷ 굴진장
            var section = TunnelSection.TwoLaneRoad();
            var crew = new NatmCrew();
            SupportPerMeter At(SupportPattern p) => new SupportPerMeter(new NatmCycle(p, section, crew), section, crew);

            SupportPerMeter p1 = At(SupportPattern.P1), p2 = At(SupportPattern.P2), p3 = At(SupportPattern.P3),
                p4 = At(SupportPattern.P4), p5 = At(SupportPattern.P5);
            Assert.AreEqual(1.40, p1.Shotcrete, 1e-9);
            Assert.AreEqual(1.0, p1.Bolts, 1e-9);
            Assert.AreEqual(3.0, p1.BoltLength, 1e-9);
            Assert.AreEqual(0.0, p1.SteelRibs, 1e-9);
            Assert.AreEqual(1.68, p2.Shotcrete, 1e-9);
            Assert.AreEqual(7.0, p2.Bolts, 1e-9);                     // 28 ÷ 2² = 7
            Assert.AreEqual(2.52, p3.Shotcrete, 1e-9);
            Assert.AreEqual(2.52 / 0.85, p3.ShotcretePlaced, 1e-9);   // 2.965
            Assert.AreEqual(28.0 / 2.25, p3.Bolts, 1e-9);             // 12.444
            Assert.AreEqual(28.0 / 2.25 * 4.0, p3.BoltLength, 1e-9);  // 49.78 m
            Assert.AreEqual(3.36, p4.Shotcrete, 1e-9);
            Assert.AreEqual(1.0 / 1.5, p4.SteelRibs, 1e-9);
            Assert.AreEqual(4.48, p5.Shotcrete, 1e-9);
            Assert.AreEqual(28.0 / 2.25, p5.Bolts, 1e-9);             // 굴진장과 상관없이 둘레 ÷ 간격²
            Assert.AreEqual(1.0 / 1.2, p5.SteelRibs, 1e-9);
        }

        [Test]
        public void 인제양양터널_지보재_합계와_공사중_누적()
        {
            ElevationGrid grid = InjeGrid();
            var plan = new TunnelPlan(TunnelStructures.Inje, grid, 24.0, 1, true);
            double length = plan.Alignment.Length;

            // 합계 = Σ 패턴 1 m당 물량 × 패턴 길이 × 튜브 2개 (Placed의 표본 반복과 다른 길로 계산)
            double shot = 0, bolts = 0, ribs = 0;
            for (int k = 0; k < 5; k++)
            {
                SupportPerMeter q = TunnelSupportQuantity.PerMeter(plan, k);
                double len = length * plan.PatternShare(k) * 2;
                shot += q.Shotcrete * len;
                bolts += q.Bolts * len;
                ribs += q.SteelRibs * len;
            }
            SupportTotals all = TunnelSupportQuantity.Total(plan);
            Assert.AreEqual(length * 2, all.Length, 1e-6);
            Assert.AreEqual(shot, all.Shotcrete, 1e-6);
            Assert.AreEqual(bolts, all.Bolts, 1e-6);
            Assert.AreEqual(ribs, all.SteelRibs, 1e-6);
            Assert.AreEqual(all.Shotcrete / 0.85, all.ShotcretePlaced, 1e-6);
            TestContext.WriteLine("숏크리트 " + all.Shotcrete.ToString("N0") + " m³, 록볼트 " + all.Bolts.ToString("N0") + "개, 강지보 " + all.SteelRibs.ToString("N0") + "개");

            // 공사 중: 0일 = 0, 관통일 = 합계, 중간은 판 길이를 따라간다 (표본 20 m × 막장 4개 × 튜브 2개 안쪽 차이)
            Assert.AreEqual(0.0, TunnelSupportQuantity.Placed(plan, 0.0).Shotcrete, 1e-9);
            Assert.AreEqual(all.Shotcrete, TunnelSupportQuantity.Placed(plan, plan.Schedule.FinishDay).Shotcrete, 1e-6);
            double prev = 0;
            foreach (double day in new[] { 100.0, 300.0, 500.0, 700.0 })
            {
                SupportTotals now = TunnelSupportQuantity.Placed(plan, day);
                Assert.Greater(now.Shotcrete, prev);
                prev = now.Shotcrete;
                Assert.AreEqual(plan.Schedule.ExcavatedLength(day) * 2, now.Length, 20.0 * 4 * 2 + 1e-6);
            }
        }

        // ---------- 포스코이앤씨 산성터널 (굴착 시작일·작업구 자료가 없는 터널) ----------

        [Test]
        public void 산성터널은_막장_두_개로_계산하고_실제와_직접_비교하지_않는다()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Data", "sites", TunnelStructures.SanseongFile));
            if (!File.Exists(path)) Assert.Ignore("DEM 파일이 없어 건너뜀: " + path);
            ElevationGrid grid = AscGridReader.ReadFile(path);

            TunnelSpec spec = TunnelStructures.Sanseong;
            Assert.AreSame(spec, TunnelStructures.FindTunnel("POSCO_Sanseong.asc"));
            Assert.IsFalse(spec.HasMiddleAccess);
            Assert.IsFalse(spec.ExcavationStartKnown);
            Assert.AreEqual(1169, spec.ActualDays);                          // 공사 착공 2013-08-01 ~ 관통 보도 2016-10-13 (굴착 외 기간 포함)
            Assert.AreEqual(4813.92, spec.AlignmentA().Length, 0.01);        // 보도 4.87 km

            // 사갱을 켜도 작업구 자료가 없으면 양 끝 막장 2개로 계산한다
            var plan = new TunnelPlan(spec, grid, 24.0, 1, true);
            Assert.IsFalse(plan.UseMiddleAccess);
            Assert.AreEqual(2, plan.Schedule.FaceCount);
            Assert.AreEqual(622.75, plan.Schedule.FinishDay, 1e-6);
            Assert.AreEqual(330.11, plan.MaxCover(out double at), 0.05);
            Assert.AreEqual(3080.0, at, 1e-9);
        }
    }
}
