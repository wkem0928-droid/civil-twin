using CivilTwin.Core;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>
    /// 흙쌓기(펴고 다지기)와 터파기 작업효율 (2026-11-20). 값은 2025년 적용 표준품셈 원문과 2024 기계경비 산출표.
    /// </summary>
    public class EmbankmentTests
    {
        [Test]
        public void 롤러_작업량은_표준품셈_식이다()
        {
            // 8-2-9 롤러 (174 ~ 176쪽): Q = 1,000 × V × W × D × E × f / N
            // 진동롤러 10 t: V 4.0 km/h, W 1.9 m, 노체 D 0.30 m, N 6회, E 보통 0.60 → 228 m³/시간
            Assert.AreEqual(228.0, RollerProductivity.VibratoryRoller10t, 1e-9);
            // 하루 8시간 1,824 m³ — 3-4-1 흙쌓기 조(1,150 m³/일)보다 크다: 조 전체(펴기·살수)가 더 느리다
            Assert.AreEqual(1824.0, RollerProductivity.VibratoryRoller10t * 8.0, 1e-9);
            Assert.Greater(RollerProductivity.VibratoryRoller10t * 8.0, EmbankmentCrew.DailyCompacted30cm);
        }

        [Test]
        public void 흙쌓기_조_수는_공기에_맞춰_올림한다()
        {
            // 성토 783,796 m³를 333일에: 783,796 ÷ (1,150 × 333) = 2.047 → 3조
            Assert.AreEqual(3, EmbankmentCrew.CrewsToKeepUp(783796.0, 8.0, 333.0));
            // 571일이면 1.19 → 2조
            Assert.AreEqual(2, EmbankmentCrew.CrewsToKeepUp(783796.0, 8.0, 571.0));
            // 딱 맞으면 올리지 않는다: 1,150 × 10일 = 11,500 m³ → 1조
            Assert.AreEqual(1, EmbankmentCrew.CrewsToKeepUp(11500.0, 8.0, 10.0));
            // 하루 10시간이면 일당 시공량도 10/8배: 1,437.5 m³
            Assert.AreEqual(1437.5, EmbankmentCrew.DailyVolume(10.0), 1e-9);
            // 성토가 없으면 0조
            Assert.AreEqual(0, EmbankmentCrew.CrewsToKeepUp(0.0, 8.0, 100.0));
        }

        [Test]
        public void 흙쌓기_조의_m3당_장비비()
        {
            const double diesel = 1842.0, operatorWage = 283297.0, driverWage = 238936.0;
            // 진동롤러 10 t: 25,460 + 14.4 × 1,842 × 1.30 + 283,297 / 8 = 95,354.365
            Assert.AreEqual(95354.365, MachineCost.Hourly(MachineCostItem.VibratoryRoller10t_2024, diesel, operatorWage), 1e-6);
            // 모터그레이더 3.6 m: 43,769 + 16.2 × 1,842 × 1.39 + 35,412.125 = 120,659.281
            Assert.AreEqual(120659.281, MachineCost.Hourly(MachineCostItem.MotorGrader36m_2024, diesel, operatorWage), 1e-6);
            // 굴착기 0.6 m³: 21,957 + 10.2 × 1,842 × 1.22 + 35,412.125 = 80,290.973
            Assert.AreEqual(80290.973, MachineCost.Hourly(MachineCostItem.Excavator06m3_2024, diesel, operatorWage), 1e-6);
            // 살수차 16,000 L: 18,163 + 12.9 × 1,842 × 1.30 + 238,936 / 8 = 78,920.34
            Assert.AreEqual(78920.34, MachineCost.Hourly(MachineCostItem.WaterTruck16000L_2024, diesel, driverWage), 1e-6);

            // 조 하나 = 95,354.365 + 120,659.281 + 80,290.973 + 78,920.34 × 0.5 = 335,764.789원/시간
            Assert.AreEqual(335764.789, EmbankmentCrew.HourlyMachineCost(diesel, operatorWage, driverWage), 1e-6);
            // m³당 = 335,764.789 × 8 ÷ 1,150 = 2,335.755
            Assert.AreEqual(2335.7550539, EmbankmentCrew.CostPerCubicMeter(diesel, operatorWage, driverWage), 1e-6);
        }

        [Test]
        public void 터파기는_작업효율에서_0_05를_뺀다()
        {
            // 표준품셈 164쪽 주 ④: E 0.60 → 0.55 → 배율 0.55 / 0.60
            Assert.AreEqual(0.55 / 0.60, ExcavatorProductivity.TrenchRateFactor(0.60), 1e-12);

            // 굴착기 1대 77.76 m³/시간 × 8시간 = 622.08 m³/일. 터파기 1,850 m³:
            //   빼기 전 1,850 ÷ 622.08 = 2.97 → 3일,  뺀 뒤 1,850 ÷ 570.24 = 3.24 → 4일
            var durations = new BuildingDurations(5.0, 0.0, 7.0, 3.0, 3.0, 5.0);
            var input = new AlternativeInput("A", 1, 8.0, 1.0, 100000.0, 500000.0);
            var before = new SiteConditions(10000.0, 4000.0, 77.76, 2, 1850.0, durations);
            var after = new SiteConditions(10000.0, 4000.0, 77.76, 2, 1850.0, durations, null,
                ExcavatorProductivity.TrenchRateFactor(0.60));
            Assert.AreEqual(3.0, AlternativeAnalysis.Evaluate(input, before).ExcavationDays, 1e-12);
            Assert.AreEqual(4.0, AlternativeAnalysis.Evaluate(input, after).ExcavationDays, 1e-12);
        }

        [Test]
        public void 대안마다_흙쌓기_조_수를_적는다()
        {
            // 예전 식(토공 조건 없음): 굴착기 1대 → 토공 17일. 성토 4,000 ÷ (1,150 × 17) = 0.20 → 1조
            var durations = new BuildingDurations(5.0, 0.0, 7.0, 3.0, 3.0, 5.0);
            var site = new SiteConditions(10000.0, 4000.0, 77.76, 2, 480.0, durations);
            AlternativeResult r = AlternativeAnalysis.Evaluate(new AlternativeInput("A", 1, 8.0, 1.0, 100000.0, 500000.0), site);
            Assert.AreEqual(17, r.EarthworkDays);
            Assert.AreEqual(1, r.EmbankmentCrews);
        }
    }
}
