using System;
using System.Collections.Generic;

namespace CivilTwin.Core
{
    /// <summary>그날 다리에서 다 된 부분 (3D 그리기용)</summary>
    public sealed class BridgeState
    {
        /// <summary>상판이 놓인 측점 구간들 (m)</summary>
        public readonly List<(double from, double to)> Deck = new List<(double, double)>();
        /// <summary>세워진 교각 측점</summary>
        public readonly List<double> Piers = new List<double>();
        /// <summary>주탑 높이 (m, 두 주탑 같음), 주탑 기초 진행률</summary>
        public double PylonHeight, FoundationProgress;
        /// <summary>케이블: (주탑 측점, 상판 측점)</summary>
        public readonly List<(double pylon, double deck)> Cables = new List<(double, double)>();

        public double DeckLength
        {
            get { double s = 0; foreach (var d in Deck) s += d.to - d.from; return s; }
        }
    }

    /// <summary>
    /// 교량 공정 진행 → 그날 다 된 부분 (2026-11-27, 인천대교 3D). 각 작업의 진행률(표준 공정, 그림용)로:
    ///   고가교 상판: 육지 쪽 끝에서 바다 쪽으로 진행률만큼 (FSLM)
    ///   접속교: 교각마다 양쪽으로 (진행률 × 경간 ÷ 2)만큼 (FCM)
    ///   사장교: 주탑 높이 = 진행률 × 주탑 높이, 상판은 주탑에서 양쪽으로 (진행률 × 주경간 ÷ 2), 바깥쪽은 측경간까지만. 끝나면 주경간이 이어진다.
    ///   케이블: 내민 상판 위 60 m마다 하나 (그림용 간격)
    ///   교각: 하부 진행률만큼 육지 쪽부터 (사장교 측경간 교각은 주탑 기초와 함께)
    /// </summary>
    public static class BridgeProgress
    {
        public const double CableSpacing = 60.0;

        /// <summary>상판 높이 (해수면 위, m): 고가교 ViaductDeckHeight, 사장교는 선박 통과 높이 + 3 m, 접속교는 그 사이를 직선으로 (그림용 가정)</summary>
        public static double DeckHeight(BridgeSpec b, double station)
        {
            BridgeSegment wa = b.Segment("서측 접속교"), cs = b.CableStayed, ea = b.Segment("동측 접속교");
            double csDeck = b.NavigationClearance + 3.0, v = b.ViaductDeckHeight;
            if (station >= cs.From && station <= cs.To) return csDeck;
            if (station > wa.From && station < cs.From) return v + (csDeck - v) * (station - wa.From) / wa.Length;
            if (station > cs.To && station < ea.To) return csDeck + (v - csDeck) * (station - cs.To) / ea.Length;
            return v;
        }

        private static double Progress(ConstructionSchedule s, string name, DateTime d)
        {
            foreach (ScheduleWork w in s.Works)
            {
                if (w.Name != name) continue;
                double t = (d - w.Start).TotalDays / (w.Finish - w.Start).TotalDays;
                return t < 0 ? 0 : t > 1 ? 1 : t;
            }
            return 0;
        }

        public static BridgeState At(BridgeSpec b, ConstructionSchedule s, DateTime date)
        {
            var st = new BridgeState();
            BridgeSegment wv = b.Segment("서측 고가교"), wa = b.Segment("서측 접속교"), cs = b.CableStayed, ea = b.Segment("동측 접속교"), ev = b.Segment("동측 고가교");
            double sub = Progress(s, "접속교·고가교 하부", date);
            double fw = Progress(s, "서측 고가교 상판 (FSLM)", date), fe = Progress(s, "동측 고가교 상판 (FSLM)", date);
            double fcm = Progress(s, "접속교 상판 (FCM)", date);
            st.FoundationProgress = Progress(s, "주탑 기초", date);
            st.PylonHeight = Progress(s, "주탑 (" + b.PylonHeight.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " m)", date) * b.PylonHeight;
            double deck = Progress(s, "사장교 상판·케이블", date);

            // 고가교
            foreach ((BridgeSegment g, bool fromWest, double f) in new[] { (wv, true, fw), (ev, false, fe) })
            {
                int n = StandardSchedules.FslmSpans(g);
                for (int i = 0; i <= n; i++)
                {
                    if (i > sub * n) break;
                    st.Piers.Add(fromWest ? g.From + g.Length * i / n : g.To - g.Length * i / n);
                }
                double done = f * g.Length;
                if (done > 0) st.Deck.Add(fromWest ? (g.From, g.From + done) : (g.To - done, g.To));
            }
            // 접속교
            foreach (BridgeSegment g in new[] { wa, ea })
            {
                int n = (int)Math.Round(g.Length / g.Span);
                for (int i = 1; i <= n; i++)
                {
                    if (sub < (double)i / (n + 1)) continue;
                    double p = g.From + g.Length * (i - 0.5) / n;
                    st.Piers.Add(p);
                    double arm = fcm * g.Length / n / 2.0;
                    if (arm > 0) st.Deck.Add((p - arm, p + arm));
                }
            }
            // 사장교
            if (st.FoundationProgress >= 1.0)
            {
                st.Piers.Add(cs.From); st.Piers.Add(cs.From + b.CableStayedSpans[0]);
                st.Piers.Add(cs.To - b.CableStayedSpans[4]); st.Piers.Add(cs.To);
            }
            double[] py = b.PylonStations;
            double main = deck * b.CableStayedSpans[2] / 2.0, side = Math.Min(b.CableStayedSpans[0] + b.CableStayedSpans[1], main);
            if (deck >= 1.0) st.Deck.Add((cs.From, cs.To));
            else if (main > 0)
            {
                st.Deck.Add((py[0] - side, py[0] + main));
                st.Deck.Add((py[1] - main, py[1] + side));
            }
            if (main > 0)
                for (int k = 0; k < 2; k++)
                {
                    bool west = k == 0;
                    for (double d = CableSpacing; d <= main + 1e-6; d += CableSpacing)
                    {
                        st.Cables.Add((py[k], west ? py[k] + d : py[k] - d));       // 주경간 쪽
                        if (d <= side + 1e-6) st.Cables.Add((py[k], west ? py[k] - d : py[k] + d));   // 측경간 쪽
                    }
                }
            st.Piers.Sort();
            return st;
        }
    }
}
