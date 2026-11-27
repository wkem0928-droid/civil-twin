using System;
using System.Linq;
using CivilTwin.Core;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>
    /// 표준 공정으로 만든 회사 구조물 공정 (2026-11-27). 손계산 (Python으로 따로 계산):
    ///   인제양양터널: 굴착 시작 2010.5.1 − 갱구부 60일 = 2010.3.2, − 준비 6개월 = 착공 2009.9.2 (가정)
    ///     준비 181일, 갱구부 60일, 본선 굴착 2010.5.1 ~ 관통 2012.9.25 = 878일
    ///     라이닝 = 대차 4 + 올림(10,965 ÷ 2 ÷ 12 = 456.9) = 461일 → 2013.12.30, 포장·설비 ~ 개통 2017.6.30 = 1,278일
    ///     양 끝 경로 181 + 60 + 878 = 1,119일 > 사갱 경로 181 + 348 + 530 = 1,059일 → 양 끝 막장이 주공정, 전체 2,858일
    ///   산성터널: 착공 2013.8.1 + 6개월 + 60일 = 굴착 시작 2014.4.2 (가정), 관통 2016.10.13까지 925일,
    ///     라이닝 4 + 올림(4,870 ÷ 24 = 202.9) = 207일
    ///   부항댐: 2006.11.1 ~ 2013.11.20 = 2,576일을 소양강댐 비율(321·579·23·69·120·924 / 2,375)로 나눔
    ///     → 가배수터널 2007.10.15, 유수전환 2009.7.4, 기초굴착 2009.7.29, 기초처리 2009.10.12, 축조 2010.2.19,
    ///       축조 끝 2012.11.17, 차수벽은 축조 기간의 마지막 20 % → 2012.5.1
    /// </summary>
    public class StandardSchedulesTests
    {
        [Test]
        public void 라이닝_기간은_길이에서_계산()
        {
            Assert.AreEqual(461.0, StandardSchedules.LiningDays(10965.0), 1e-9);
            Assert.AreEqual(207.0, StandardSchedules.LiningDays(4870.0), 1e-9);
            Assert.AreEqual(4.0 + 42.0, StandardSchedules.LiningDays(1000.0), 1e-9);   // 1,000 ÷ 2 ÷ 12 = 41.7 → 42 (TunnelLining 손계산과 같음)
        }

        [Test]
        public void 인제양양터널_공정()
        {
            ConstructionSchedule s = ScheduleCatalog.Find(TunnelStructures.InjeFile);
            Assert.IsNotNull(s);
            Assert.AreEqual(StructureKind.Tunnel, s.Kind);
            Assert.AreEqual(new DateTime(2009, 9, 2), s.ProjectStart);
            Assert.AreEqual(7, s.Works.Count);   // 준비, 갱구부, 사갱, 본선(양 끝), 본선(사갱), 라이닝, 포장·설비
            ScheduleWork main = s.Works.Single(w => w.Name == "본선 굴착 (양 끝 막장)");
            Assert.AreEqual(878.0, main.Days, 1e-9);
            Assert.IsFalse(main.Assumed);
            ScheduleWork lining = s.Works.Single(w => w.Name == "방수·2차 라이닝");
            Assert.AreEqual(new DateTime(2013, 12, 30), lining.Finish);
            Assert.IsTrue(lining.Assumed);

            CpmResult r = s.Logic();
            Assert.AreEqual(2858.0, r.ProjectDuration, 1e-9);
            Assert.AreEqual(2858.0, s.Day(s.Completion), 1e-9);
            Assert.IsTrue(r.Critical[s.Works.IndexOf(main)]);
            Assert.AreEqual(60.0, r.TotalFloat[s.Works.FindIndex(w => w.Name == "사갱 굴착")], 1e-9);   // 1,119 − 1,059
        }

        [Test]
        public void 산성터널은_굴착_시작일을_가정()
        {
            ConstructionSchedule s = ScheduleCatalog.Find(TunnelStructures.SanseongFile);
            Assert.AreEqual(new DateTime(2013, 8, 1), s.ProjectStart);
            Assert.AreEqual(5, s.Works.Count);   // 사갱 없음: 준비, 갱구부, 본선, 라이닝, 포장·설비
            ScheduleWork main = s.Works.Single(w => w.Name == "본선 굴착 (양 끝 막장)");
            Assert.AreEqual(new DateTime(2014, 4, 2), main.Start);
            Assert.AreEqual(925.0, main.Days, 1e-9);
            StringAssert.Contains("가정", main.Source);
            Assert.AreEqual(207.0, s.Works.Single(w => w.Name == "방수·2차 라이닝").Days, 1e-9);
        }

        /// <summary>
        /// 관통일이 공개되지 않은 터널 (2026-11-27): 굴착 시작 = 착공 + 6개월 + 60일, 관통 = + 올림(길이 ÷ 2 ÷ 3.86)
        ///   청계산3터널: 2012.6.1 → 2013.1.30, 올림(2,752 ÷ 2 ÷ 3.86 = 356.5) = 357일 → 2014.1.22, 라이닝 4 + 올림(2,752 ÷ 24) = 119일
        ///   미사터널:    2004.8.1 → 2005.4.2,  올림(2,200 ÷ 2 ÷ 3.86 = 285.0) = 285일 → 2006.1.12, 라이닝 96일
        /// </summary>
        [TestCase("lotte_anyang_seongnam.asc", "청계산3터널", 2013, 1, 30, 357.0, 2014, 1, 22, 119.0)]
        [TestCase("hdc_seoul_chuncheon.asc", "미사터널", 2005, 4, 2, 285.0, 2006, 1, 12, 96.0)]
        public void 관통일을_모르는_터널은_가정한_관통일로(string file, string name, int ey, int em, int ed, double days, int by, int bm, int bd, double lining)
        {
            TunnelSpec t = TunnelStructures.FindTunnel(file);
            Assert.AreEqual(name, t.Name);
            Assert.IsFalse(t.BreakthroughKnown);
            Assert.AreEqual(new DateTime(by, bm, bd), t.Breakthrough);
            ConstructionSchedule s = ScheduleCatalog.Find(file);
            ScheduleWork main = s.Works.Single(w => w.Name == "본선 굴착 (양 끝 막장)");
            Assert.AreEqual(new DateTime(ey, em, ed), main.Start);
            Assert.AreEqual(days, main.Days, 1e-9);
            Assert.IsTrue(main.Assumed);
            StringAssert.Contains("3.86", main.Source);
            Assert.AreEqual(lining, s.Works.Single(w => w.Name == "방수·2차 라이닝").Days, 1e-9);
            Assert.IsTrue(s.Milestones.Any(m => m.text == "관통 (가정)"));
        }

        /// <summary>
        /// 양수발전소 (2026-11-27). 청송 2000.9.1 ~ 2006.12.1 = 2,282일 × 비율: 지하 발전소 12 ~ 45 % → 2001.6.2 ~ 2003.6.25.
        /// 주공정 준비 274 + 발전소 753 + 설비 981 + 시운전 274 = 2,282일 (= 실제). 예천 하부댐은 자료 2009.9.1 ~ 2010.6.30 (302일).
        /// </summary>
        [Test]
        public void 양수발전소_공정()
        {
            ConstructionSchedule c = ScheduleCatalog.Find(PumpedStorageStructures.CheongsongFile);
            Assert.AreEqual(7, c.Works.Count);
            ScheduleWork cave = c.Works.Single(w => w.Name == "지하 발전소 굴착");
            Assert.AreEqual(new DateTime(2001, 6, 2), cave.Start);
            Assert.AreEqual(new DateTime(2003, 6, 25), cave.Finish);
            CpmResult r = c.Logic();
            Assert.AreEqual(2282.0, r.ProjectDuration, 1e-9);
            Assert.IsTrue(r.Critical[c.Works.IndexOf(cave)]);
            Assert.IsTrue(r.Critical[c.Works.FindIndex(w => w.Name == "발전설비 설치")]);

            ConstructionSchedule y = ScheduleCatalog.Find(PumpedStorageStructures.YecheonFile);
            ScheduleWork lower = y.Works.Single(w => w.Name == "하부댐");
            Assert.AreEqual(new DateTime(2009, 9, 1), lower.Start);
            Assert.AreEqual(302.0, lower.Days, 1e-9);
            Assert.IsFalse(lower.Assumed);
            Assert.AreEqual(2586.0, y.Logic().ProjectDuration, 1e-9);
            string md = ConstructionReport.Markdown(y, null, "시험", ScheduleCatalog.ReportPart(y));
            StringAssert.Contains("| 상부댐 | CFRD (콘크리트 표면차수벽형 석괴댐) | 73 | 620 | 222 | EL 711 |", md);
        }

        [Test]
        public void 부항댐은_소양강댐_비율로_나눈_표준_공정()
        {
            ConstructionSchedule s = ScheduleCatalog.Find(SiteStructures.BuhangFile);
            Assert.AreEqual(StructureKind.Dam, s.Kind);
            Assert.AreEqual(9, s.Works.Count);
            Assert.AreEqual(new DateTime(2007, 10, 15), s.Works[1].Start);
            Assert.AreEqual(new DateTime(2009, 7, 4), s.Works[2].Start);
            Assert.AreEqual(new DateTime(2010, 2, 19), s.Works[5].Start);
            Assert.AreEqual(new DateTime(2012, 5, 1), s.Works[6].Start);    // 표면차수벽
            Assert.AreEqual(new DateTime(2012, 11, 17), s.Works[6].Finish);
            Assert.AreEqual(s.Completion, s.Works[8].Finish);
            Assert.AreEqual(2576.0, s.Logic().ProjectDuration, 1e-9);      // 주공정 사슬이 준공일까지 이어진다
            Assert.IsTrue(s.Works.Skip(1).Take(7).All(w => w.Assumed));
        }

        [Test]
        public void 모든_구조물_공정은_보고서와_단계가_만들어진다()
        {
            foreach (string file in ScheduleCatalog.SiteFiles)
            {
                ConstructionSchedule s = ScheduleCatalog.Find(file);
                Assert.Greater(s.Steps.Count, 4, file);
                for (int k = 1; k < s.Steps.Count; k++) Assert.LessOrEqual(s.Steps[k - 1].Date, s.Steps[k].Date, file + " 단계 날짜 순서");
                foreach (ScheduleWork w in s.Works) Assert.Greater(w.Days, 0.0, file + " " + w.Name);
                string md = ConstructionReport.Markdown(s, null, "시험", ScheduleCatalog.ReportPart(s));
                StringAssert.Contains("## 5. 시공 단계", md, file);
                StringAssert.Contains("자료와 맞지 않는 점, 가정, 출처", md, file);
                Assert.IsNotEmpty(ConstructionReport.Csv(s), file);
            }
        }
    }
}
