using System;
using System.IO;
using CivilTwin.Core;
using NUnit.Framework;
using UnityEngine;

namespace CivilTwin.Tests
{
    /// <summary>
    /// 소양강댐 시공 단계별 3D 지형 (2026-11-22). 날짜에 따라 강 → 가물막이 → 굴착 → 담수가 맞는 자리·높이에 생기는지 본다.
    /// 지형 파일이 없는 PC에서는 건너뛴다.
    /// </summary>
    public class DamStageModelTests
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

        private static void At(DamStageModel m, DateTime date, double s, double d, out double z, out StageCell cell)
        {
            ElevationGrid g = m.StageGrid(date, out StageCell[] cells);
            int i = m.NodeIndex(s, d);
            z = g.GetElevation(i % g.Columns, i / g.Columns);
            cell = cells[i];
        }

        [Test]
        public void 계곡_바닥과_호수_바닥_추정()
        {
            DamStageModel m = Model();
            TestContext.WriteLine($"바닥 s {m.FloorLeftS:0.0} ~ {m.FloorRightS:0.0}, 기울기 {m.Valley.Slope:0.000}");
            Assert.Less(m.FloorLeftS, m.FloorRightS);
            Assert.That(m.FloorRightS - m.FloorLeftS, Is.InRange(30.0, 300.0));   // 바닥 폭이 상식적인 범위

            // 띠 밖 상류의 호수 점은 기초 표고 ~ 수면 사이로 내려간다 (위성 수면 183 m보다 높지 않다)
            int lower = 0, total = 0;
            for (int i = 0; i < m.Rock.Columns * m.Rock.Rows; i += 7)
            {
                double z = m.Rock.GetElevation(i % m.Rock.Columns, i / m.Rock.Columns);
                if (double.IsNaN(z)) continue;
                Assert.GreaterOrEqual(z, m.Dam.FoundationElevation - 6.0);   // 위성의 하류 강(76 ~ 78 m)은 기초보다 조금 낮다
                total++;
                if (z <= m.Dam.FoundationElevation + 0.01) lower++;
            }
            Assert.Greater(lower, 0);
            Assert.Greater(total, lower);
        }

        [Test]
        public void 유수전환과_기초굴착()
        {
            DamStageModel m = Model();
            double cs = m.FloorCenterS, bed = m.Dam.FoundationElevation;

            // 유수전환 전: 댐 축의 계곡 바닥은 강 (퇴적층 위 86 m)
            At(m, new DateTime(1969, 9, 1), cs, 0, out double z, out StageCell c);
            Assert.AreEqual(StageCell.River, c);
            Assert.AreEqual(bed + DamStageModel.SedimentThickness, z, 1e-6);

            // 유수전환 뒤, 굴착 전: 마른 퇴적층. 가물막이 상류는 그대로 강
            At(m, new DateTime(1969, 10, 10), cs, 0, out z, out c);
            Assert.AreEqual(StageCell.Sediment, c);
            At(m, new DateTime(1969, 10, 10), cs, m.UpCofferD + 60.0, out _, out c);
            Assert.AreEqual(StageCell.River, c);

            // 굴착 끝 (1970.1.1): 댐 바닥 끝까지 암반 80 m
            At(m, new DateTime(1970, 1, 1), cs, m.Dam.UpstreamBase - 15.0, out z, out c);
            Assert.AreEqual(StageCell.Rock, c);
            Assert.AreEqual(bed, z, 1e-6);
            // 굴착 절반 (1969.11.27 무렵): 축 근처는 암반, 상류 끝은 아직 퇴적층
            DateTime half = DamStageModel.ExcavationStart.AddDays((DamStageModel.GroutStart - DamStageModel.ExcavationStart).TotalDays / 2.0);
            At(m, half, cs, 20.0, out _, out c);
            Assert.AreEqual(StageCell.Rock, c);
            At(m, half, cs, m.Dam.UpstreamBase - 15.0, out _, out c);
            Assert.AreEqual(StageCell.Sediment, c);
        }

        [Test]
        public void 가물막이_높이는_체절_때_올라간다()
        {
            // 12 m 격자점은 가물막이 축에서 최대 8.5 m 떨어진다 → 마루폭 10 m 밖 사면(1:2)에서 최대 1.75 m 낮게 읽힌다
            DamStageModel m = Model();
            At(m, new DateTime(1969, 11, 1), m.FloorCenterS, m.UpCofferD, out double z, out StageCell c);
            Assert.AreEqual(StageCell.Cofferdam, c);
            Assert.AreEqual(DamStageModel.UpCofferCrestLow, z, 2.0);
            At(m, new DateTime(1971, 1, 1), m.FloorCenterS, m.UpCofferD, out z, out _);
            Assert.AreEqual(DamStageModel.UpCofferCrestHigh, z, 2.0);
            At(m, new DateTime(1971, 1, 1), m.FloorCenterS, m.DownCofferD, out z, out c);
            Assert.AreEqual(StageCell.Cofferdam, c);
            Assert.AreEqual(DamStageModel.DownCofferCrest, z, 2.0);
            // 유수전환 전에는 가물막이가 없다
            At(m, new DateTime(1969, 9, 1), m.FloorCenterS, m.UpCofferD, out _, out c);
            Assert.AreNotEqual(StageCell.Cofferdam, c);
        }

        [Test]
        public void 담수는_상류만_채운다()
        {
            DamStageModel m = Model();
            DateTime d1 = new DateTime(1972, 2, 1);
            double level = m.ReservoirLevel(d1);
            Assert.AreEqual(148.61 - DamStageModel.ReservoirBelowFill, level, 0.01);   // 성토 EL 148.6 − 15 (그림용 규칙)
            Assert.IsNaN(m.ReservoirLevel(new DateTime(1972, 1, 31)));
            Assert.AreEqual(193.5, m.ReservoirLevel(new DateTime(1973, 10, 15)), 1e-9);

            At(m, d1, m.FloorCenterS, m.Dam.UpstreamBase + 100.0, out double z, out StageCell c);
            Assert.AreEqual(StageCell.Reservoir, c);
            Assert.AreEqual(level, z, 1e-9);
            At(m, d1, m.FloorCenterS, -m.Dam.DownstreamBase - 100.0, out _, out c);
            Assert.AreNotEqual(StageCell.Reservoir, c);   // 하류는 채우지 않는다

            // 축 하류(d ≤ 0)에는 저수지 점이 하나도 없다
            ElevationGrid g = m.StageGrid(new DateTime(1973, 10, 15), out StageCell[] cells);
            int reservoir = 0;
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i] != StageCell.Reservoir) continue;
                reservoir++;
                m.Dam.ToAxis(g.GetEasting(i % g.Columns), g.GetNorthing(i / g.Columns), out _, out double d);
                Assert.Greater(d, 0.0);
            }
            Assert.Greater(reservoir, 1000);
        }

        [Test]
        public void 가배수터널은_가물막이_바깥을_잇는다()
        {
            DamStageModel m = Model();
            for (int k = 0; k < 2; k++)
            {
                var p = m.TunnelPath(k);
                Assert.Greater(p[0][1], m.UpCofferD);                 // 입구: 상류 가물막이보다 상류
                Assert.Less(p[p.Count - 1][1], m.DownCofferD);         // 출구: 하류 가물막이보다 하류
                foreach (double[] q in p) Assert.Less(q[0], m.FloorLeftS);   // 계곡 바닥 밖(왼쪽 산)
            }
        }
    }
}
