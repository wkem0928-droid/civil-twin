using System;
using System.Collections.Generic;
using System.IO;
using CivilTwin.Core;
using NUnit.Framework;
using UnityEngine;

namespace CivilTwin.Tests
{
    /// <summary>
    /// 소양강댐 시공 단계 3D의 장비 배치 (2026-11-25). 그날 하는 작업의 장비만, 그 작업 자리 안에 있는지 본다.
    /// 지형 파일이 없는 PC에서는 건너뛴다 (왕복 시험은 항상 한다).
    /// </summary>
    public class DamWorkfaceTests
    {
        private static DamStageModel _model;

        private static DamStageModel Model()
        {
            if (_model != null) return _model;
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Data", "sites", SiteStructures.SoyanggangFile));
            if (!File.Exists(path)) Assert.Ignore("DEM 파일이 없어 건너뜀: " + path);
            ElevationGrid g = AscGridReader.ReadFile(path);
            DamSpec spec = SiteStructures.Soyanggang;
            DamSection dam = spec.CreateSection();
            _model = new DamStageModel(g, dam, spec.FitValley(g, dam), DamZoneSection.Soyanggang(),
                spec.UpstreamExtra, spec.DownstreamExtra, SiteStructures.SoyanggangEnd, 12.0);
            return _model;
        }

        private static int Count(List<DamMachinePose> list, DamMachine k)
        {
            int n = 0;
            foreach (DamMachinePose p in list) if (p.Kind == k) n++;
            return n;
        }

        [Test]
        public void 왕복은_가서_부리고_돌아온다()
        {
            // 편도 100 m, 10 m/초 → 10초 가고 5초 서서 부리고 10초 돌아온다 (한 바퀴 25초)
            DamWorkface.Shuttle(4.0, 100, 10, 5, out double u, out bool back, out double dump);
            Assert.AreEqual(0.4, u, 1e-9);
            Assert.IsFalse(back);
            DamWorkface.Shuttle(12.5, 100, 10, 5, out u, out back, out dump);
            Assert.AreEqual(1.0, u, 1e-9);
            Assert.AreEqual(0.5, dump, 1e-9);
            DamWorkface.Shuttle(20.0, 100, 10, 5, out u, out back, out dump);
            Assert.AreEqual(0.5, u, 1e-9);
            Assert.IsTrue(back);
            DamWorkface.Shuttle(25.0 + 4.0, 100, 10, 5, out u, out back, out dump);
            Assert.AreEqual(0.4, u, 1e-9);   // 다음 바퀴
        }

        [Test]
        public void 단계마다_맞는_장비가_나온다()
        {
            DamStageModel m = Model();
            Assert.AreEqual(0, DamWorkface.At(m, new DateTime(1967, 6, 1), 0, false).Count);                 // 준비공사: 아직 없음
            Assert.AreEqual(2, Count(DamWorkface.At(m, new DateTime(1968, 6, 1), 0, false), DamMachine.Excavator));   // 1호 터널 양쪽 입구
            Assert.AreEqual(1, Count(DamWorkface.At(m, new DateTime(1969, 10, 10), 0, false), DamMachine.Bulldozer)); // 가물막이
            List<DamMachinePose> dig = DamWorkface.At(m, new DateTime(1969, 12, 1), 0, false);
            Assert.AreEqual(4 + 2 + 1, Count(dig, DamMachine.Excavator));   // 굴착 4 + 2호 터널 2 + 여수로 1
            Assert.AreEqual(5, Count(DamWorkface.At(m, new DateTime(1970, 2, 1), 0, false), DamMachine.DrillRig));
            List<DamMachinePose> fill = DamWorkface.At(m, new DateTime(1971, 5, 1), 0, false);
            Assert.AreEqual(3, Count(fill, DamMachine.Roller));             // 코어 1 + 양쪽 사력 2
            Assert.AreEqual(0, Count(fill, DamMachine.DrillRig));
            Assert.AreEqual(0, DamWorkface.At(m, new DateTime(1973, 10, 15), 0, false).Count);                // 준공
        }

        [Test]
        public void 성토_장비는_윗면과_존_안에_있다()
        {
            DamStageModel m = Model();
            DamZoneSection z = m.Zones;
            foreach (DateTime date in new[] { new DateTime(1970, 6, 1), new DateTime(1971, 5, 1), new DateTime(1972, 5, 1), new DateTime(1972, 10, 20) })
                foreach (bool cut in new[] { false, true })
                    for (double sec = 0; sec < 120; sec += 3.7)
                    {
                        double e = m.FillElevation(date);
                        DamWorkface.FillSpan(m, e, cut, out double s0, out double s1);
                        foreach (DamMachinePose p in DamWorkface.At(m, date, sec, cut))
                        {
                            if (double.IsNaN(p.Z) || Math.Abs(p.Z - e - 0.3) > 1e-9) continue;   // 성토 위의 장비만
                            Assert.That(p.S, Is.InRange(s0, s1), p.Kind + " " + date);
                            Assert.That(p.D, Is.InRange(-z.DownstreamFace(e), z.UpstreamFace(e)), p.Kind + " " + date);
                            if (p.Kind != DamMachine.DumpTruck) continue;
                            Assert.Greater(Math.Abs(p.D), z.CoreHalfWidth(e) + (z.CoreHalfWidth(e) > 0 ? z.FilterWidth : 0.0));   // 트럭은 사력 존에
                        }
                    }
        }

        [Test]
        public void 요약_글()
        {
            DamStageModel m = Model();
            Assert.AreEqual("", DamWorkface.Summary(DamWorkface.At(m, new DateTime(1973, 10, 15), 0, false)));
            StringAssert.Contains("롤러 3", DamWorkface.Summary(DamWorkface.At(m, new DateTime(1971, 5, 1), 0, false)));
        }
    }
}
