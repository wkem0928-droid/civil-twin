using System.IO;
using CivilTwin.Core;
using NUnit.Framework;
using UnityEngine;

namespace CivilTwin.Tests
{
    /// <summary>
    /// 회사 구조물 ②: GS건설 김천 부항댐 (2026-11-21). 공개 제원(K-water): 높이 64 m, 길이 472 m, 체적 2,162,000 m³, 마루 EL 201.4 m.
    /// 사면 구배는 공개 자료에서 찾지 못해 하류는 위성 지형에서 재고(소양강댐으로 보정), 상류·마루폭은 가정했다.
    /// 지형 파일은 저장소에 없으므로(용량·이용 조건) 파일이 없는 PC에서는 건너뛴다.
    /// </summary>
    public class BuhangDamTests
    {
        private static ElevationGrid Read(string file)
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Data", "sites", file));
            if (!File.Exists(path)) Assert.Ignore("DEM 파일이 없어 건너뜀: " + path);
            return AscGridReader.ReadFile(path);
        }

        [Test]
        public void 마루_능선은_축_위에_있다()
        {
            // 축 위(d = 0)의 위성 표고: 30 m 칸이 좁은 마루를 주변과 평균해 공식 201.4 m보다 낮은 194 ~ 197 m
            ElevationGrid g = Read(SiteStructures.BuhangFile);
            DamSection dam = SiteStructures.BuhangDam();
            foreach (double s in new[] { -150.0, 0.0, 150.0 })
            {
                dam.FromAxis(s, 0.0, out double e, out double n);
                Assert.IsTrue(g.TryGetElevationAt(e, n, out double z));
                Assert.That(z, Is.InRange(190.0, 202.0), "s = " + s);
            }
            // 상류(축의 왼쪽, d > 0) 60 m는 부항호 수면(179 m), 하류 180 m는 하천 바닥(약 152 m)
            dam.FromAxis(0.0, 60.0, out double ue, out double un);
            dam.FromAxis(0.0, -180.0, out double de, out double dn);
            g.TryGetElevationAt(ue, un, out double up);
            g.TryGetElevationAt(de, dn, out double down);
            Assert.That(up, Is.InRange(178.0, 182.0));
            Assert.That(down, Is.InRange(148.0, 156.0));
        }

        [Test]
        public void 위성에서_잰_하류_사면을_소양강댐으로_보정한다()
        {
            // 방법 검증: 공개 하류 1:2.0인 소양강댐에서 같은 방법으로 재면 1:2.06 (3 % 차이)
            double soy = DamSlopeSurvey.MeasureDownstream(Read(SiteStructures.SoyanggangFile), SiteStructures.SoyanggangDam(),
                SiteStructures.SoyanggangRiverBed, -150.0, 150.0, 30.0, out int soySections);
            Assert.AreEqual(10, soySections);
            Assert.AreEqual(2.0577, soy, 0.001);

            // 부항댐: 위성 1:2.72 → 보정 1:2.72 × (2.0 / 2.06) = 1:2.64 → 단면에는 1:2.6을 쓴다
            double buhang = DamSlopeSurvey.MeasureDownstream(Read(SiteStructures.BuhangFile), SiteStructures.BuhangDam(),
                SiteStructures.BuhangRiverBed, -150.0, 90.0, 30.0, out int buSections);
            Assert.AreEqual(9, buSections);
            Assert.AreEqual(2.7151, buhang, 0.001);
            double calibrated = buhang * 2.0 / soy;
            Assert.AreEqual(2.639, calibrated, 0.001);
            Assert.AreEqual(2.6, SiteStructures.BuhangDam().DownstreamSlope, 1e-12);
            Assert.That(System.Math.Abs(calibrated - 2.6), Is.LessThan(0.05), "단면의 하류 구배는 보정한 값을 0.1 단위로 내린 것");
        }

        [Test]
        public void 복원한_계곡_위_댐_체적이_공개_체적과_가깝다()
        {
            ElevationGrid surveyed = Read(SiteStructures.BuhangFile);
            DamSpec spec = SiteStructures.Buhang;
            DamSection dam = spec.CreateSection();
            AbutmentValley valley = spec.FitValley(surveyed, dam);
            ElevationGrid original = ValleyReconstruction.Rebuild(surveyed, dam, valley, spec.UpstreamExtra, spec.DownstreamExtra);
            EarthworkResult r = EarthworkVolume.Compute(original, dam.BuildDesignGrid(original));

            double ratio = r.FillVolume / spec.OfficialVolume - 1.0;
            TestContext.WriteLine("양안 기울기 " + valley.Slope.ToString("F3") + ", 체적 " + r.FillVolume.ToString("N0")
                + " m³, 공개 대비 " + (ratio * 100.0).ToString("F1") + " %");

            Assert.AreEqual(0.0, r.CutVolume, 1.0);
            Assert.AreEqual(0.325, valley.Slope, 0.001);
            // 2026-11-21 결과: 2,326,422 m³ (공개 2,162,000 m³ 대비 +7.6 %). 가정(상류 1:1.3 ~ 1:1.5, 하류 1:2.5 ~ 1:2.7,
            // 마루폭 6 ~ 10 m)을 바꾸면 +5 ~ +10 % — 공개 구배를 쓴 소양강댐(−2.0 %)보다 불확실하다.
            Assert.AreEqual(2326422.0, r.FillVolume, 1000.0);
            Assert.That(ratio, Is.InRange(-0.15, 0.15), "공개 체적의 ±15 % 안");
        }

        [Test]
        public void 댐_자료_묶음()
        {
            Assert.AreSame(SiteStructures.Buhang, SiteStructures.FindDam("GS_Buhang.asc"));
            Assert.AreSame(SiteStructures.Soyanggang, SiteStructures.FindDam(SiteStructures.SoyanggangFile));
            Assert.IsNull(SiteStructures.FindDam("daewoo_inje_yangyang.asc"));
            // 2006-11-01(날짜는 가정) ~ 2013-11-20 준공식 = 2,576일, 소양강댐 2,375일
            Assert.AreEqual(2576, SiteStructures.Buhang.CalendarDays);
            Assert.AreEqual(2375, SiteStructures.Soyanggang.CalendarDays);
            // 층 수: 부항 주 축조존 60 cm → 64 ÷ 0.6 = 106.7 → 107층, 소양강 30 cm → 410층
            Assert.AreEqual(107, SiteStructures.Buhang.Lifts);
            Assert.AreEqual(410, SiteStructures.Soyanggang.Lifts);
            Assert.AreEqual(137.4, SiteStructures.BuhangDam().FoundationElevation, 1e-9);
        }
    }
}
