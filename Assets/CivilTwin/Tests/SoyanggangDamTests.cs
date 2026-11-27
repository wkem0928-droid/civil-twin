using System.IO;
using CivilTwin.Core;
using NUnit.Framework;
using UnityEngine;

namespace CivilTwin.Tests
{
    /// <summary>
    /// 실제 지형(소양강댐)에서 댐 체적을 계산해 공개 체적 9,591,200 m³와 비교한다.
    /// 지형 파일은 저장소에 없으므로(용량·이용 조건) 파일이 없는 PC에서는 건너뛴다.
    /// </summary>
    public class SoyanggangDamTests
    {
        private static ElevationGrid Surveyed()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Data", "sites", SiteStructures.SoyanggangFile));
            if (!File.Exists(path)) Assert.Ignore("DEM 파일이 없어 건너뜀: " + path);
            return AscGridReader.ReadFile(path);
        }

        [Test]
        public void 마루_능선은_축_위에_있다()
        {
            // 축 위(d = 0)의 위성 표고는 마루 근처여야 한다: 30 m 칸이라 공식 203 m보다 조금 낮은 195 ~ 203 m
            ElevationGrid g = Surveyed();
            DamSection dam = SiteStructures.SoyanggangDam();
            foreach (double s in new[] { -150.0, 0.0, 150.0 })
            {
                dam.FromAxis(s, 0.0, out double e, out double n);
                Assert.IsTrue(g.TryGetElevationAt(e, n, out double z));
                Assert.That(z, Is.InRange(195.0, 203.0), "s = " + s);
            }
        }

        [Test]
        public void 복원한_계곡_위_댐_체적이_공개_체적과_가깝다()
        {
            ElevationGrid surveyed = Surveyed();
            DamSection dam = SiteStructures.SoyanggangDam();
            AbutmentValley valley = SiteStructures.SoyanggangValley(surveyed, dam);
            ElevationGrid original = ValleyReconstruction.Rebuild(surveyed, dam, valley,
                SiteStructures.SoyanggangUpstreamExtra, SiteStructures.SoyanggangDownstreamExtra);
            EarthworkResult r = EarthworkVolume.Compute(original, dam.BuildDesignGrid(original));

            double ratio = r.FillVolume / SiteStructures.SoyanggangOfficialVolume - 1.0;
            TestContext.WriteLine("양안 기울기 " + valley.Slope.ToString("F3") + ", 체적 " + r.FillVolume.ToString("N0")
                + " m³, 공개 대비 " + (ratio * 100.0).ToString("F1") + " %");

            Assert.AreEqual(0.0, r.CutVolume, 1.0);
            // 2026-11-20 결과: 양안 기울기 0.567, 체적 9,395,915 m³ (공개 9,591,200 m³ 대비 −2.0 %).
            // 숫자가 바뀌면 계산이나 가정이 바뀐 것이므로 이 값으로 고정해 둔다 (±1,000 m³).
            Assert.AreEqual(9395915.0, r.FillVolume, 1000.0);
            Assert.That(ratio, Is.InRange(-0.15, 0.15), "공개 체적의 ±15 % 안");
        }
    }
}
