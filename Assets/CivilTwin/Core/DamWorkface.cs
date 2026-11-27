using System;
using System.Collections.Generic;

namespace CivilTwin.Core
{
    /// <summary>댐 3D에 세우는 장비 종류</summary>
    public enum DamMachine { DumpTruck, Bulldozer, Roller, Excavator, DrillRig }

    /// <summary>장비 하나의 자리 (축 좌표 s, d)와 자세</summary>
    public readonly struct DamMachinePose
    {
        public readonly DamMachine Kind;
        public readonly double S, D;
        /// <summary>표고 (NaN이면 그 자리 지형 위)</summary>
        public readonly double Z;
        /// <summary>앞을 보는 방향 (s, d 성분)</summary>
        public readonly double HeadS, HeadD;
        /// <summary>덤프트럭: 적재함을 든 정도, 실은 흙 (0 ~ 1)</summary>
        public readonly double BedRaise, Load;

        public DamMachinePose(DamMachine kind, double s, double d, double z, double headS, double headD, double bedRaise = 0, double load = 0)
        {
            Kind = kind; S = s; D = d; Z = z; HeadS = headS; HeadD = headD; BedRaise = bedRaise; Load = load;
        }
    }

    /// <summary>
    /// 소양강댐 시공 단계 3D의 장비 배치 (2026-11-25). 날짜에 맞는 작업 자리에 장비를 놓고 움직인다:
    ///   가배수터널(1968.3 ~ 1969.10): 상·하류 터널 입구에 굴착기, 버력을 나르는 덤프트럭
    ///   유수전환(1969.10): 상류 가물막이 마루에 덤프트럭·불도저      가물막이 높이기(1970.3 ~ 10): 롤러·덤프트럭
    ///   기초굴착(1969.10.24 ~ 1970.1.1): 축에서 바깥으로 넓어지는 굴착 끝에 굴착기 + 덤프트럭
    ///   기초처리(1970.1 ~ 5): 댐 축을 따라 천공 장비
    ///   성토(1970.5.1 ~ 1972.11.10): 존마다 — 덤프트럭이 댐 축 방향으로 들어와 부리고, 불도저가 펴고, 롤러가 축과 나란히 다진다
    ///   여수로(1969.10.15 ~ 1973.6): 만들어진 끝에 굴착기 + 덤프트럭
    /// 장비 대수·동선·속도는 **그림용**이다 (자료의 실제 대수는 68종 205대). 성토 장비는 그날의 성토 윗면 안에만, 코어 장비는 코어 폭 안에만 놓는다.
    /// 계산은 순수 C# (테스트 DamWorkfaceTests).
    /// </summary>
    public static class DamWorkface
    {
        /// <summary>화면에서 잘 보이도록 실제보다 크게 그리는 배율</summary>
        public const double DrawScale = 3.0;

        /// <summary>존 하나에 장비 두 줄을 놓으려면 필요한 폭 (m, 3배로 그린 트럭 폭 약 8 m 기준)</summary>
        private const double TwoLaneWidth = 60.0, OneLaneWidth = 12.0;
        private const double RollerSpeed = 8.0, DozerSpeed = 6.0, TruckSpeed = 22.0, DumpSeconds = 5.0;

        public static List<DamMachinePose> At(DamStageModel m, DateTime date, double seconds, bool cut)
        {
            if (m == null) throw new ArgumentNullException(nameof(m));
            var list = new List<DamMachinePose>();
            Tunnels(list, m, date, seconds);
            Cofferdam(list, m, date, seconds, cut);
            Excavation(list, m, date, seconds, cut);
            Grouting(list, m, date, cut);
            Fill(list, m, date, seconds, cut);
            Spillway(list, m, date, seconds);
            return list;
        }

        /// <summary>표고 z에서 성토 윗면의 축 방향 범위 (계곡 사면 안쪽). cut이면 계곡 가운데까지만.</summary>
        public static void FillSpan(DamStageModel m, double z, bool cut, out double s0, out double s1)
        {
            double wider = Math.Max(0.0, z - m.Dam.FoundationElevation) / m.Valley.Slope;
            s0 = Math.Max(m.FloorLeftS - wider, -m.Dam.HalfLength);
            s1 = Math.Min(m.FloorRightS + wider, RightEnd(m));
            if (cut) s1 = Math.Min(s1, m.FloorCenterS);
        }

        /// <summary>댐 몸체의 오른쪽(+s) 끝: 여수로 옆벽 (공식 길이 530 m보다 바깥이라 체적 계산과는 관계없다)</summary>
        public static double RightEnd(DamStageModel m) => Math.Min(m.Dam.HalfLength, DamStageModel.SpillwayS - DamStageModel.SpillwayWidth / 2.0);

        // ── 왕복 ──────────────────────────────────────────────

        /// <summary>0 ↔ 1 왕복 비율과 방향 (길이 length를 속도 speed로). 트럭은 끝에서 DumpSeconds 동안 서서 부린다.</summary>
        public static void Shuttle(double seconds, double length, double speed, double dwell,
            out double u, out bool returning, out double dumping)
        {
            double go = length / speed, period = 2.0 * go + dwell;
            double t = seconds - Math.Floor(seconds / period) * period;
            dumping = 0.0;
            returning = false;
            if (t < go) { u = t / go; return; }
            t -= go;
            if (t < dwell)
            {
                u = 1.0;
                dumping = dwell > 0 ? t / dwell : 1.0;
                return;
            }
            returning = true;
            u = Math.Max(0.0, 1.0 - (t - dwell) / go);
        }

        private static void AddShuttle(List<DamMachinePose> list, DamMachine kind, double s0, double d0, double s1, double d1, double z,
            double speed, double seconds)
        {
            double length = Math.Sqrt((s1 - s0) * (s1 - s0) + (d1 - d0) * (d1 - d0));
            if (length < 1.0) return;
            bool truck = kind == DamMachine.DumpTruck;
            Shuttle(seconds, length, speed, truck ? DumpSeconds : 0.0, out double u, out bool back, out double dump);
            double hs = (s1 - s0) / length, hd = (d1 - d0) / length;
            double sign = back ? -1.0 : 1.0;
            double raise = truck ? (dump <= 0 ? 0 : dump < 0.5 ? dump * 2.0 : (1.0 - dump) * 2.0) : 0.0;
            double load = !truck ? 0.0 : back ? 0.0 : 1.0 - dump;
            list.Add(new DamMachinePose(kind, s0 + (s1 - s0) * u, d0 + (d1 - d0) * u, z, hs * sign, hd * sign, raise, load));
        }

        // ── 작업 자리 ─────────────────────────────────────────

        private static void Tunnels(List<DamMachinePose> list, DamStageModel m, DateTime date, double seconds)
        {
            for (int k = 0; k < 2; k++)
            {
                DateTime start = k == 0 ? DamStageModel.Tunnel1Start : new DateTime(1969, 6, 1);   // 2호 시작은 가정 (공정표와 같음)
                DateTime open = k == 0 ? DamStageModel.DiversionDate : DamStageModel.Tunnel2Open;
                if (date < start || date >= open) continue;
                List<double[]> p = m.TunnelPath(k);
                for (int end = 0; end < 2; end++)
                {
                    double[] portal = end == 0 ? p[0] : p[p.Count - 1];
                    double outward = end == 0 ? 1.0 : -1.0;   // 입구는 상류 쪽, 출구는 하류 쪽으로 나온다
                    list.Add(new DamMachinePose(DamMachine.Excavator, portal[0] + 14.0, portal[1] + outward * 26.0, double.NaN, -0.5, -outward));
                    AddShuttle(list, DamMachine.DumpTruck, portal[0], portal[1] + outward * 20.0, portal[0], portal[1] + outward * 110.0, double.NaN, 12.0, seconds + 4.0 * k + 7.0 * end);
                }
            }
        }

        private static void Cofferdam(List<DamMachinePose> list, DamStageModel m, DateTime date, double seconds, bool cut)
        {
            bool first = date >= DamStageModel.DiversionDate && date < DamStageModel.ExcavationStart;
            bool raise = date >= new DateTime(1970, 3, 1) && date < DamStageModel.CofferRaiseDate;   // [T] 1970.3 코어 다짐 ~ 10월 체절
            if (!first && !raise) return;
            double z = m.UpCofferCrest(date);
            FillSpan(m, z, false, out double s0, out double s1);
            s0 += 10.0; s1 -= 10.0;
            AddShuttle(list, DamMachine.DumpTruck, s0, m.UpCofferD, (s0 + s1) / 2.0 - 15.0, m.UpCofferD, z, TruckSpeed, seconds);
            AddShuttle(list, raise ? DamMachine.Roller : DamMachine.Bulldozer, (s0 + s1) / 2.0 + 15.0, m.UpCofferD, s1, m.UpCofferD, z, DozerSpeed, seconds);
        }

        private static void Excavation(List<DamMachinePose> list, DamStageModel m, DateTime date, double seconds, bool cut)
        {
            if (date < DamStageModel.ExcavationStart || date >= DamStageModel.GroutStart) return;
            double dug = m.Excavated(date);
            double right = cut ? m.FloorCenterS : m.FloorRightS;
            double width = right - m.FloorLeftS;
            for (int side = -1; side <= 1; side += 2)
            {
                double front = side * Math.Max(30.0, dug * (side > 0 ? m.Dam.UpstreamBase : m.Dam.DownstreamBase) - 20.0);
                for (int k = 0; k < (cut ? 1 : 2); k++)
                {
                    double s = m.FloorLeftS + width * (cut ? 0.5 : 0.28 + 0.44 * k);
                    list.Add(new DamMachinePose(DamMachine.Excavator, s, front, double.NaN, 0, side));
                    // 트럭: 굴착기 옆(90°)에서 싣고 축 쪽으로 나간다 → 그림에서는 실은 채 나가 부리고 돌아오는 왕복
                    AddShuttle(list, DamMachine.DumpTruck, s + 24.0, front, s + 24.0, side * 10.0, double.NaN, 12.0, seconds + 5.0 * k);
                }
                AddShuttle(list, DamMachine.Bulldozer, m.FloorLeftS + 8.0, front - side * 22.0, right - 8.0, front - side * 22.0, double.NaN, DozerSpeed, seconds + side);
            }
        }

        private static void Grouting(List<DamMachinePose> list, DamStageModel m, DateTime date, bool cut)
        {
            if (date < DamStageModel.GroutStart || date >= m.Zones.FillStart) return;
            double right = cut ? m.FloorCenterS : m.FloorRightS;
            const int count = 5;
            for (int k = 0; k < count; k++)
            {
                double s = m.FloorLeftS + (right - m.FloorLeftS) * (k + 0.5) / count;
                list.Add(new DamMachinePose(DamMachine.DrillRig, s, k % 2 == 0 ? 5.0 : -5.0, double.NaN, 1, 0));
            }
        }

        private static void Fill(List<DamMachinePose> list, DamStageModel m, DateTime date, double seconds, bool cut)
        {
            DamZoneSection z = m.Zones;
            if (date < z.FillStart || date >= z.FillEnd) return;
            double e = m.FillElevation(date);
            FillSpan(m, e, cut, out double s0, out double s1);
            s0 += 18.0; s1 -= 18.0;
            if (s1 - s0 < 60.0) return;
            double mid = (s0 + s1) / 2.0;
            double top = e + 0.3;   // 존 띠(+0.25 m) 위

            // 코어: 폭이 넓으면 두 줄 (롤러 | 불도저), 좁으면 롤러만
            double c = z.CoreHalfWidth(e);
            if (2.0 * c >= OneLaneWidth)
            {
                double lane = c >= 14.0 ? c * 0.45 : 0.0;
                AddShuttle(list, DamMachine.Roller, s0, lane, lane > 0 ? s1 : mid - 14.0, lane, top, RollerSpeed, seconds);
                AddShuttle(list, DamMachine.Bulldozer, lane > 0 ? s0 : mid + 14.0, -lane, s1, -lane, top, DozerSpeed, seconds + 9.0);
            }

            // 양쪽 사력: 덤프트럭(축 방향으로 들어와 부림) · 불도저(펴기) · 롤러(축과 나란히 다짐)
            for (int side = -1; side <= 1; side += 2)
            {
                double inner = c > 0 ? c + z.FilterWidth : 0.0;
                double outer = side > 0 ? z.UpstreamFace(e) : z.DownstreamFace(e);
                double w = outer - inner;
                if (w < OneLaneWidth) continue;
                double phase = side > 0 ? 0.0 : 6.0;
                if (w >= TwoLaneWidth)
                {
                    AddShuttle(list, DamMachine.DumpTruck, s0, side * (inner + w * 0.25), mid, side * (inner + w * 0.25), top, TruckSpeed, seconds + phase);
                    AddShuttle(list, DamMachine.DumpTruck, s0, side * (inner + w * 0.42), s0 + (s1 - s0) * 0.8, side * (inner + w * 0.42), top, TruckSpeed, seconds + phase + 8.0);
                    AddShuttle(list, DamMachine.Bulldozer, mid, side * (inner + w * 0.60), s1, side * (inner + w * 0.60), top, DozerSpeed, seconds + phase);
                    AddShuttle(list, DamMachine.Roller, s0, side * (inner + w * 0.80), s1, side * (inner + w * 0.80), top, RollerSpeed, seconds + phase + 15.0);
                }
                else
                {
                    AddShuttle(list, DamMachine.DumpTruck, s0, side * (inner + w * 0.5), mid - 14.0, side * (inner + w * 0.5), top, TruckSpeed, seconds + phase);
                    AddShuttle(list, DamMachine.Roller, mid + 14.0, side * (inner + w * 0.5), s1, side * (inner + w * 0.5), top, RollerSpeed, seconds + phase);
                }
            }
        }

        private static void Spillway(List<DamMachinePose> list, DamStageModel m, DateTime date, double seconds)
        {
            double built = m.SpillwayBuilt(date);
            if (!(built > 0) || built >= 1.0) return;
            double d = DamStageModel.SpillwayTopD + (DamStageModel.SpillwayEndD - DamStageModel.SpillwayTopD) * built;
            list.Add(new DamMachinePose(DamMachine.Excavator, DamStageModel.SpillwayS - 8.0, d - 22.0, double.NaN, 0, -1));
            AddShuttle(list, DamMachine.DumpTruck, DamStageModel.SpillwayS + 14.0, d - 20.0, DamStageModel.SpillwayS + 14.0, d - 100.0, double.NaN, 12.0, seconds);
        }

        // ── 화면 글 ───────────────────────────────────────────

        public static string NameOf(DamMachine k)
        {
            switch (k)
            {
                case DamMachine.DumpTruck: return "덤프트럭";
                case DamMachine.Bulldozer: return "불도저";
                case DamMachine.Roller: return "롤러";
                case DamMachine.Excavator: return "굴착기";
                default: return "천공 장비";
            }
        }

        /// <summary>"덤프트럭 4, 불도저 2, 롤러 3" (없으면 빈 글)</summary>
        public static string Summary(IList<DamMachinePose> list)
        {
            var parts = new List<string>();
            foreach (DamMachine k in new[] { DamMachine.Excavator, DamMachine.DumpTruck, DamMachine.Bulldozer, DamMachine.Roller, DamMachine.DrillRig })
            {
                int n = 0;
                foreach (DamMachinePose p in list) if (p.Kind == k) n++;
                if (n > 0) parts.Add(NameOf(k) + " " + n);
            }
            return string.Join(", ", parts);
        }
    }
}
