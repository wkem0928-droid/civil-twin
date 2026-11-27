using CivilTwin.Core;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>
    /// 5-7 자동 설명 문장과 보고서. 숫자는 Phase 4(대안 손계산 표)와 5-4(덤프트럭 2대·4대) 손계산과 같은 예를 쓴다.
    /// </summary>
    public class NarrativeReportTests
    {
        // ---------- 금액 표기 ----------

        [Test]
        public void 금액은_억과_만_단위로_적는다()
        {
            Assert.AreEqual("30억 5,350만 원", KoreanMoney.Format(3053500000.0));
            Assert.AreEqual("6,210만 원", KoreanMoney.Format(62100000.0));
            Assert.AreEqual("2억 원", KoreanMoney.Format(200000000.0));
            Assert.AreEqual("0원", KoreanMoney.Format(0.0));
            Assert.AreEqual("−1,790만 원", KoreanMoney.Format(-17900000.0));
        }

        // ---------- 5-4 예: 굴착기 1대, 절토 10000, 성토 4000, 트럭 2대 / 4대 ----------

        private static readonly DumpTruck TestTruck = new DumpTruck(15.0, 1.8, 20.0, 30.0, 1.0, 0.5, 0.9);

        private static SiteConditions SiteWithHaul()
        {
            double load = TestTruck.LoadMinutes(1.25, 1.0, 0.9, 20.0, 0.6);
            var setup = new EarthworkSetup(new CutMaterials(10000.0, 0.0, 0.0, RockLine.None),
                1.25, 0.9, 1.6, 1.2, 0.35, TestTruck, load, 0.3, 5.0, 5.0);
            return new SiteConditions(10000.0, 4000.0, 77.76, 2, 480.0,
                new BuildingDurations(5.0, 0.0, 7.0, 3.0, 3.0, 5.0), setup);
        }

        private static AlternativeResult Haul(string name, int trucks)
        {
            return AlternativeAnalysis.Evaluate(
                new AlternativeInput(name, 1, 8.0, 1.0, 100000.0, 500000.0, trucks, 50000.0), SiteWithHaul());
        }

        [Test]
        public void 트럭을_늘린_비교_문장()
        {
            // 트럭 2대: 56일, 8,000만 원 (운반이 정함) / 4대: 41일, 6,210만 원 (굴착이 정함) — HaulTests와 같은 손계산
            string s = AlternativeNarrative.Compare(Haul("A", 2), Haul("B", 4));

            Assert.AreEqual(
                "A → B (덤프트럭 2대 → 4대): 전체 공기는 56일 → 41일(15일 단축), 비교용 비용은 8,000만 원 → 6,210만 원(1,790만 원 감소)이다. "
                + "토공 공기를 정하는 쪽이 운반에서 굴착으로 바뀌었다.", s);
        }

        [Test]
        public void 짧은_비교_문장()
        {
            Assert.AreEqual("A → B: 공기 −15일, 비용 −1,790만 원", AlternativeNarrative.Short(Haul("A", 2), Haul("B", 4)));
        }

        [Test]
        public void 토공_계획_설명_문장()
        {
            // 굴착 16.075일, 운반 31.230일 → 32일, 따라가려면 3.89 → 4대
            Assert.AreEqual(
                "굴착 16.1일, 운반 31.2일이라 운반이 토공 공기 32일을 정한다. 운반이 굴착을 따라가려면 덤프트럭이 4대 필요하다(지금 2대).",
                AlternativeNarrative.Describe(Haul("A", 2).Earthwork));
        }

        // ---------- Phase 4 예 (운반 없음): 굴착기 4대 → 8대 ----------

        [Test]
        public void 주공정선이_가설공사로_넘어가면_그_이유를_말한다()
        {
            // 4-2 손계산: 4대 토공 5일(가설 5일과 같아 둘 다 주공정) 전체 29일 3,370만 / 8대 토공 3일 전체 29일 4,010만
            var site = new SiteConditions(10000.0, 4000.0, 77.76, 2, 480.0, new BuildingDurations(5.0, 0.0, 7.0, 3.0, 3.0, 5.0));
            AlternativeResult four = AlternativeAnalysis.Evaluate(new AlternativeInput("A", 4, 8.0, 1.0, 100000.0, 500000.0), site);
            AlternativeResult eight = AlternativeAnalysis.Evaluate(new AlternativeInput("B", 8, 8.0, 1.0, 100000.0, 500000.0), site);

            Assert.AreEqual("토공·가설", AlternativeNarrative.CriticalStart(four.Schedule));
            Assert.AreEqual("가설공사", AlternativeNarrative.CriticalStart(eight.Schedule));
            Assert.AreEqual(
                "A → B (굴착기 4대 → 8대): 전체 공기는 29일 → 29일(같음), 비교용 비용은 3,370만 원 → 4,010만 원(640만 원 증가)이다. "
                + "토공이 가설공사보다 짧아져 주공정선이 가설공사로 넘어갔다(그래서 전체 공기가 줄지 않았다).",
                AlternativeNarrative.Compare(four, eight));
        }

        // ---------- 보고서 ----------

        private static ReportInput Input()
        {
            var r = new ReportInput
            {
                SiteName = "시험 현장",
                GeneratedAt = "2026-11-20 12:00",
                PadWidth = 100, PadLength = 50, PadElevation = 50, CutSlopeRatio = 1.0, FillSlopeRatio = 1.5,
                CutVolume = 10000, FillVolume = 4000,
                Materials = new CutMaterials(10000, 0, 0, RockLine.None),
                HourlyPerMachine = 77.76, OnSiteHaulMeters = 300, SpoilKm = 5, BorrowKm = 5,
                Building = new BuildingPlan(30.0, 20.0, 2, 3.5, 5, 4, 0.5, 0.25, 0.8),
            };
            AlternativeResult now = Haul("지금", 2);
            r.Alternatives.Add(now);
            r.Alternatives.Add(Haul("A", 2));
            r.Alternatives.Add(Haul("B", 4));
            r.Assumptions.Add("단가는 가정값");
            return r;
        }

        [Test]
        public void 보고서에_계산값과_자동_문장이_들어간다()
        {
            string md = ProjectReport.Markdown(Input());

            StringAssert.Contains("# 시공계획 계산 보고서 — 시험 현장", md);
            StringAssert.Contains("| **토공 공기** | **32일** |", md);
            // 콘크리트: 기초 480 + 기둥 24.375 × 2 = 48.75 + 슬래브 150 × 2 = 300 → 828.75
            StringAssert.Contains("= 828.8 m³", md);
            // CPM 첫 줄: 토공 32일, ES 0, EF 32, 주공정
            StringAssert.Contains("| 토공 (부지 정지) | 32 | 0 | 32 | 0 | 32 | 0 | ● |", md);
            StringAssert.Contains("전체 공기 **56일**", md);
            // 대안 비교 문장: A → B, 그리고 각 대안 → 지금
            StringAssert.Contains("- A → B (덤프트럭 2대 → 4대): 전체 공기는 56일 → 41일(15일 단축)", md);
            StringAssert.Contains("- B → 지금 (덤프트럭 4대 → 2대): 전체 공기는 41일 → 56일(15일 늘어남)", md);
            // A와 지금은 조건이 같아(트럭 2대) 비교 문장을 만들지 않는다
            StringAssert.DoesNotContain("A → 지금", md);
            StringAssert.Contains("- 단가는 가정값", md);
        }

        [Test]
        public void 대안_CSV는_원_단위_숫자로_적는다()
        {
            string csv = ProjectReport.Csv(Input().Alternatives);
            string[] lines = csv.Replace("\r", "").Split('\n');

            StringAssert.StartsWith("대안,굴착기(대),덤프트럭(대)", lines[0]);
            // 지금: 굴착기 1, 트럭 2, 8시간, r 1, 토공 32, 전체 56, 굴착 16.075, 운반 31.230, 2,640만 / 2,560만 / 2,800만 / 8,000만
            // 다짐 조: 성토 4,000 ÷ (1,150 × 31.23일) = 0.11 → 1조
            Assert.AreEqual("지금,1,2,8,1,32,56,16.075,31.230,26400000,25600000,28000000,80000000,토공,1", lines[1]);
        }
    }
}
