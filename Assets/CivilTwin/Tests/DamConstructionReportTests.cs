using System;
using CivilTwin.Core;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>
    /// 댐 시공계획 보고서와 운반 검산 (2026-11-25). 손계산 (Python으로 따로 계산한 값):
    ///   하루 9,591,200 ÷ 924 = 10,380.09 m³ × 2.0 = 20,760.2 t,  한 바퀴 127 × 13.5 + 20 × 32 = 2,354.5 t
    ///   대당 왕복 20,760.2 ÷ 2,354.5 = 8.817번 (매일), 60 %만 일하면 14.695번
    ///   한 번에 쓸 수 있는 시간: 8시간 ÷ 8.817 = 54.4분, 60 %면 32.7분, 16시간 60 %면 65.3분
    ///   왕복 (4 + 4/15 × 60 + 4/20 × 60 + 1.05 + 0.42) ÷ 0.9 = 37.19분
    /// </summary>
    public class ConstructionReportTests
    {
        private static string Report(double[] delays = null) => ConstructionReport.Markdown(
            ScheduleCatalog.Soyanggang(), DamZoneSection.Soyanggang(), DamZoneSection.SoyanggangSteps(), delays, "시험");

        [Test]
        public void 운반_검산_손계산()
        {
            FillHaulCheck h = FillHaulCheck.Soyanggang(DamZoneSection.Soyanggang());
            Assert.AreEqual(147, h.Trucks);
            Assert.AreEqual(2354.5, h.FleetTons, 1e-9);
            Assert.AreEqual(20760.17, h.DailyTons, 0.01);
            Assert.AreEqual(8.8172, h.TripsPerDay(1.0), 1e-4);
            Assert.AreEqual(14.6954, h.TripsPerDay(0.6), 1e-4);
            Assert.AreEqual(54.44, h.MinutesPerTrip(8, 1.0), 0.01);
            Assert.AreEqual(32.66, h.MinutesPerTrip(8, 0.6), 0.01);
            Assert.AreEqual(65.33, h.MinutesPerTrip(16, 0.6), 0.01);
            Assert.AreEqual(37.189, FillHaulCheck.SoyanggangCycleMinutes(), 1e-3);
        }

        /// <summary>
        /// 두 기록 비교 표 (2026-11-26). 손계산: 기사대로면 하루 11,404.52 m³ → 22,809.0 t ÷ 2,354.5 = 9.6874번,
        /// 60 %면 16.146번 → 8시간 29.7분 ✕, 16시간 59.5분 ○ — 완공 기록(32.7 ✕, 65.3 ○)과 ○·✕가 같다.
        /// </summary>
        [Test]
        public void 두_기록_비교_표()
        {
            string md = Report();
            StringAssert.Contains("### 두 기록 비교", md);
            StringAssert.Contains("| 완공 기록: 1972.11.10에 마루 | 924일 | 10,380 | 96.5 m | 91.0 % | 33분 ✕ | 65분 ○ |", md);
            StringAssert.Contains("| 기사: 1972.8.19에 마루 | 841일 | 11,405 | 123.0 m | 100.0 % | 30분 ✕ | 59분 ○ |", md);
            StringAssert.Contains("하루 성토량 차이는 10 %", md);

            FillHaulCheck h = FillHaulCheck.Soyanggang(DamZoneSection.Soyanggang(crestByFlood: true));
            Assert.AreEqual(9.6874, h.TripsPerDay(1.0), 1e-4);
            Assert.AreEqual(29.73, h.MinutesPerTrip(8, 0.6), 0.01);
            Assert.AreEqual(59.46, h.MinutesPerTrip(16, 0.6), 0.01);

            // 기사 가정으로 만든 보고서는 6절 첫 줄에 그 사실을 적는다
            string article = ConstructionReport.Markdown(ScheduleCatalog.Soyanggang(), DamZoneSection.Soyanggang(crestByFlood: true),
                DamZoneSection.SoyanggangSteps(), null, "시험");
            StringAssert.Contains("1970.5.1 ~ 1972.8.19 = 841일", article);
            StringAssert.Contains("1972.11.10까지 마무리", article);
        }

        [Test]
        public void 운반_검산_잘못된_값은_거부()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FillHaulCheck(1000, 2.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FillHaulCheck(1000, 0, (1, 10.0)));
            var h = new FillHaulCheck(1000, 2.0, (10, 10.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => h.TripsPerDay(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => h.MinutesPerTrip(25, 1));
        }

        [Test]
        public void 지연_영향은_여유를_넘는_만큼()
        {
            ConstructionSchedule s = ScheduleCatalog.Soyanggang();
            Assert.AreEqual(30.0, ConstructionReport.DelayImpact(s, 5, 30), 1e-9);    // 본댐 성토 (주공정)
            Assert.AreEqual(0.0, ConstructionReport.DelayImpact(s, 8, 180), 1e-9);    // 여수로 여유 361일 안
            Assert.AreEqual(39.0, ConstructionReport.DelayImpact(s, 8, 400), 1e-9);   // 400 − 361
        }

        [Test]
        public void 보고서에_계산값이_들어간다()
        {
            string md = Report();
            TestContext.WriteLine(md);
            StringAssert.Contains("| 논리 공정 (CPM) | 2,036일 → 1972.11.10 |", md);
            StringAssert.Contains("준비공사 → 제1호 가배수터널 → 유수전환·가물막이 → 본댐 기초굴착 → 기초처리 (그라우팅) → 본댐 성토", md);
            StringAssert.Contains("여수로 (여유 361일, 넘긴 날 232일)", md);
            StringAssert.Contains("| 9 | 여수로 | 1 | 1,354 | 321 | 1,675 | 682 | 2,036 | 361 | 361 |  |", md);
            StringAssert.Contains("**10,380 m³**", md);
            StringAssert.Contains("| 60 % | 14.7번 | 33분 ✕ | 65분 ○ | 82분 ○ |", md);
            StringAssert.Contains("일정 속도로 쌓았다면 그날 97 m다", md);
            StringAssert.Contains("```mermaid", md);
            StringAssert.DoesNotContain("화면에서 넣은 지연", md);
        }

        [Test]
        public void 화면에서_넣은_지연은_요약에_적는다()
        {
            var d = new double[10];
            d[1] = 30;
            StringAssert.Contains("**화면에서 넣은 지연:** 제1호 가배수터널 +30일 → 논리 공정 2,066일 (+30일)", Report(d));
        }

        [Test]
        public void CSV는_머리줄과_작업_10줄_열_수가_같다()
        {
            string[] lines = ConstructionReport.Csv(ScheduleCatalog.Soyanggang()).Replace("\r", "").TrimEnd('\n').Split('\n');
            Assert.AreEqual(11, lines.Length);
            int columns = lines[0].Split(',').Length;
            foreach (string line in lines)
            {
                int n = 1;
                bool quoted = false;
                foreach (char c in line)
                {
                    if (c == '"') quoted = !quoted;
                    else if (c == ',' && !quoted) n++;
                }
                Assert.AreEqual(columns, n, line);
            }
            StringAssert.StartsWith("6,본댐 성토,5,924,1112,2036,1112,2036,0,0,예,1970-05-01,1972-11-10,1970-05-01,1972-11-10,0,", lines[6]);
        }
    }
}
