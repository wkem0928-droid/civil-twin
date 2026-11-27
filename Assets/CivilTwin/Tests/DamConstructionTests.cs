using CivilTwin.Core;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>
    /// 소양강댐 시공 공정 (2026-11-22): 실제 날짜로 기간, 공사 논리로 선후행 → CPM. 손계산은 Python으로 같은 표를 따로 계산해 맞춘 값.
    /// 착공 1967-04-15 = 0일.
    /// </summary>
    public class DamConstructionTests
    {
        [Test]
        public void 작업_기간은_실제_날짜에서()
        {
            ConstructionSchedule s = ScheduleCatalog.Soyanggang();
            Assert.AreEqual(10, s.Works.Count);
            Assert.AreEqual(321.0, s.Works[0].Days, 1e-9);    // 준비 1967.4.15 ~ 1968.3.1
            Assert.AreEqual(579.0, s.Works[1].Days, 1e-9);    // 제1호 가배수터널 1968.3 ~ 1969.10
            Assert.AreEqual(924.0, s.Works[5].Days, 1e-9);    // 본댐 성토 1970.5.1 ~ 1972.11.10
            Assert.AreEqual(1354.0, s.Works[8].Days, 1e-9);   // 여수로 1969.10.15 ~ 1973.6.30
            Assert.AreEqual(2375.0, s.Day(s.Completion), 1e-9);  // 준공 1973.10.15
        }

        [Test]
        public void 논리_공정의_주공정은_물길_돌리기부터_성토까지()
        {
            // 준비 321 → 터널 579 → 유수전환 23 → 기초굴착 69 → 그라우팅 120 → 성토 924 = 2,036일 (1972.11.10)
            ConstructionSchedule s = ScheduleCatalog.Soyanggang();
            CpmResult r = s.Logic();
            Assert.AreEqual(2036.0, r.ProjectDuration, 1e-9);
            for (int i = 0; i <= 5; i++) Assert.IsTrue(r.Critical[i], s.Works[i].Name + "는 주공정");
            // 병행 작업의 여유: 여수로 2036 − (321 + 1354) = 361일, 발전소 2036 − (321 + 1296) = 419일
            Assert.AreEqual(361.0, r.TotalFloat[8], 1e-9);
            Assert.AreEqual(419.0, r.TotalFloat[9], 1e-9);
            Assert.IsFalse(r.Critical[8]);
        }

        [Test]
        public void 실제로는_여수로가_여유를_넘겨_늦게_시작했다()
        {
            // 여수로 가장 늦은 시작 LS = 2036 − 1354 = 682일, 실제 시작 1969.10.15 = 914일 → 232일 늦음
            // → 여수로가 1973.6.30(2,268일)에 끝나 본댐(2,036일) 뒤까지 공사가 이어졌다. 발전소 설비는 준공(2,375일) 뒤 1973.11.1
            ConstructionSchedule s = ScheduleCatalog.Soyanggang();
            CpmResult r = s.Logic();
            Assert.AreEqual(682.0, r.LateStart[8], 1e-9);
            Assert.AreEqual(232.0, s.LateBeyondFloat(r, 8), 1e-9);
            Assert.AreEqual(2268.0, s.Day(s.Works[8].Finish), 1e-9);
            Assert.AreEqual(9, s.AsBuiltLast);
        }

        [Test]
        public void 이_작업이_늦으면_준공이_며칠_늦나()
        {
            ConstructionSchedule s = ScheduleCatalog.Soyanggang();
            // 주공정(가배수터널)이 30일 늦으면 전체도 30일
            Assert.AreEqual(2066.0, s.Logic(new double[] { 0, 30 }).ProjectDuration, 1e-9);
            // 여수로는 여유 361일 안이면 그대로, 400일 늦으면 39일 늦음
            var late300 = new double[10]; late300[8] = 300;
            var late400 = new double[10]; late400[8] = 400;
            Assert.AreEqual(2036.0, s.Logic(late300).ProjectDuration, 1e-9);
            Assert.AreEqual(2075.0, s.Logic(late400).ProjectDuration, 1e-9);
        }
    }
}
