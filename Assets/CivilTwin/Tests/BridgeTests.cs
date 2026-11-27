using System;
using System.Linq;
using CivilTwin.Core;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>
    /// 랜드마크 인천대교 (2026-11-27). 손계산 (Python으로 따로 계산):
    ///   구간: 사장교 8,487 ~ 9,967 (80 + 260 + 800 + 260 + 80 = 1,480), 접속교 각 889, 고가교 서 5,950 · 동 2,450 → 합계 11,658 m
    ///   주탑 측점 8,487 + 340 = 8,827, + 800 = 9,627
    ///   주탑 = 올림(238.5 ÷ 4 = 59.6) = 60 × 7 = 420일, 사장교 상판 = 올림(400 ÷ 12 = 33.3) = 34 × 7 + 14 = 252일
    ///   FCM 올림(889 ÷ 5) = 178일, FSLM 서 119경간 × 2 = 238일, 동 49 × 2 = 98일
    ///   주공정 준비 120 → 주탑 기초 270 → 주탑 420 → 상판 252 → 포장 120 = 1,182일 → 2008.9.25 (자료 준공 2009.10.1보다 371일 빠름)
    ///   상판 완료 2008.5.28 (자료 2009.6.22보다 390일 빠름)
    /// </summary>
    public class BridgeTests
    {
        [Test]
        public void 구간과_주탑_측점()
        {
            BridgeSpec b = BridgeStructures.Incheon;
            Assert.AreEqual(1480.0, b.CableStayed.Length, 1e-9);
            Assert.AreEqual(1480.0, b.CableStayedSpans.Sum(), 1e-9);
            Assert.AreEqual(11658.0, b.Segments.Sum(g => g.Length), 1e-9);
            Assert.AreEqual(8827.0, b.PylonStations[0], 1e-9);
            Assert.AreEqual(9627.0, b.PylonStations[1], 1e-9);
            Assert.Greater(b.AlignmentLength, b.Segments.Last().To);   // 노선 안에 다 들어간다
            Assert.AreEqual(17128.0, b.AlignmentLength, 2.0);          // OSM 영종 → 송도 한 방향
            b.PointAt(0, out double e0, out double n0);
            Assert.AreEqual(b.AlignmentEast[0], e0, 1e-6);
            Assert.AreEqual(b.AlignmentNorth[0], n0, 1e-6);
        }

        [Test]
        public void 표준_속도_공정()
        {
            BridgeSpec b = BridgeStructures.Incheon;
            Assert.AreEqual(420.0, StandardSchedules.PylonDays(b), 1e-9);
            Assert.AreEqual(252.0, StandardSchedules.CableStayedDeckDays(b), 1e-9);
            Assert.AreEqual(178.0, StandardSchedules.FcmDays(b.Segment("서측 접속교")), 1e-9);
            Assert.AreEqual(119, StandardSchedules.FslmSpans(b.Segment("서측 고가교")));
            Assert.AreEqual(98.0, StandardSchedules.FslmDays(b.Segment("동측 고가교")), 1e-9);

            ConstructionSchedule s = ScheduleCatalog.Find(BridgeStructures.IncheonFile);
            Assert.AreEqual(StructureKind.Bridge, s.Kind);
            CpmResult r = s.Logic();
            Assert.AreEqual(1182.0, r.ProjectDuration, 1e-9);
            Assert.AreEqual(new DateTime(2008, 9, 25), s.ProjectStart.AddDays(r.ProjectDuration));
            Assert.AreEqual(371.0, (s.Completion - s.ProjectStart.AddDays(r.ProjectDuration)).TotalDays, 1e-9);
            Assert.IsTrue(r.Critical[2] && r.Critical[3]);       // 주탑, 사장교 상판
            Assert.IsFalse(r.Critical[6]);                       // 서측 고가교 FSLM은 여유
            Assert.AreEqual(new DateTime(2008, 5, 28), s.Works[8].Start);   // 모든 상판이 끝난 날
        }

        /// <summary>
        /// 그날 다 된 부분 (3D용). 손계산:
        ///   준공일: 상판 = 고가교 5,950 + 2,450 + 접속교 (교각 6개 × 양쪽 889 ÷ 6 ÷ 2 = 889) × 2 + 사장교 1,480 = 11,658 m,
        ///           교각 = 고가교 (119 + 1) + (49 + 1) + 접속교 6 + 6 + 사장교 측경간 4 = 186개
        ///   주탑 작업 한가운데: 주탑 0.5 × 238.5 = 119.25 m, 케이블 0
        ///   사장교 상판 한가운데: 주탑마다 양쪽 0.5 × 400 = 200 m (측경간 340 m보다 짧음) → 800 m,
        ///           케이블 60 m 간격 3개 × 양쪽 × 주탑 2 = 12개
        /// </summary>
        [Test]
        public void 날짜별로_다_된_부분()
        {
            BridgeSpec b = BridgeStructures.Incheon;
            ConstructionSchedule s = ScheduleCatalog.Find(BridgeStructures.IncheonFile);
            BridgeState done = BridgeProgress.At(b, s, s.Completion);
            Assert.AreEqual(11658.0, done.DeckLength, 1e-6);
            Assert.AreEqual(186, done.Piers.Count);
            Assert.AreEqual(238.5, done.PylonHeight, 1e-9);

            ScheduleWork pylon = s.Works[2], deck = s.Works[3];
            BridgeState half = BridgeProgress.At(b, s, pylon.Start.AddDays(pylon.Days / 2.0));
            Assert.AreEqual(119.25, half.PylonHeight, 1e-9);
            Assert.AreEqual(0, half.Cables.Count);

            BridgeState arms = BridgeProgress.At(b, s, deck.Start.AddDays(deck.Days / 2.0));
            double cs = arms.Deck.Where(d => d.from >= b.CableStayed.From - 1 && d.to <= b.CableStayed.To + 1).Sum(d => d.to - d.from);
            Assert.AreEqual(800.0, cs, 1e-6);
            Assert.AreEqual(12, arms.Cables.Count);

            Assert.AreEqual(20.0, BridgeProgress.DeckHeight(b, 3000), 1e-9);    // 고가교
            Assert.AreEqual(77.0, BridgeProgress.DeckHeight(b, 9200), 1e-9);    // 사장교 (선박 74 + 3)
            Assert.AreEqual(48.5, BridgeProgress.DeckHeight(b, 8487 - 444.5), 1e-9);   // 접속교 가운데
        }

        [Test]
        public void 보고서에_자료_비교()
        {
            ConstructionSchedule s = ScheduleCatalog.Find(BridgeStructures.IncheonFile);
            string md = ConstructionReport.Markdown(s, null, "시험", ScheduleCatalog.ReportPart(s));
            StringAssert.Contains("# 교량 시공계획 보고서 — 인천대교", md);
            StringAssert.Contains("| 상판 완료 | 2009.6.22 (파이낸셜뉴스 2009-06-22) | 2008.5.28 | 390일 |", md);
            StringAssert.Contains("| 준공 | 2009.10.1 (국토교통부) | 2008.9.25 | 371일 |", md);
            StringAssert.Contains("| 사장교 | 8,487 ~ 9,967 | 1,480 |", md);
        }
    }
}
