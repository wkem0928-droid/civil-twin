using System;
using System.Collections.Generic;

namespace CivilTwin.Core
{
    /// <summary>
    /// 터널 선형 (2026-11-21): 평면은 꺾은선(측량 좌표 점들), 종단은 두 입구 노면 표고 사이의 일정한 경사.
    /// 측점(chainage) = 시작 입구에서 꺾은선을 따라 잰 거리 (m).
    /// </summary>
    public sealed class TunnelAlignment
    {
        private readonly double[] _e, _n, _cum;

        public double Length { get; }
        public double StartRoadElevation { get; }
        public double EndRoadElevation { get; }

        public TunnelAlignment(double[] eastings, double[] northings, double startRoadElevation, double endRoadElevation)
        {
            if (eastings == null || northings == null) throw new ArgumentNullException(nameof(eastings));
            if (eastings.Length != northings.Length || eastings.Length < 2) throw new ArgumentException("점이 2개 이상이고 개수가 같아야 한다.");
            _e = (double[])eastings.Clone();
            _n = (double[])northings.Clone();
            _cum = new double[_e.Length];
            for (int i = 1; i < _e.Length; i++)
            {
                double dx = _e[i] - _e[i - 1], dy = _n[i] - _n[i - 1];
                _cum[i] = _cum[i - 1] + Math.Sqrt(dx * dx + dy * dy);
            }
            Length = _cum[_cum.Length - 1];
            if (!(Length > 0)) throw new ArgumentException("길이가 0이다.");
            StartRoadElevation = startRoadElevation;
            EndRoadElevation = endRoadElevation;
        }

        /// <summary>종단 경사 (노면, 시작 → 끝이 오르막이면 +)</summary>
        public double Grade => (EndRoadElevation - StartRoadElevation) / Length;

        /// <summary>측점의 노면 표고 (직선 보간)</summary>
        public double RoadElevation(double chainage) =>
            StartRoadElevation + (EndRoadElevation - StartRoadElevation) * Clamp(chainage) / Length;

        /// <summary>측점의 평면 위치</summary>
        public void PointAt(double chainage, out double easting, out double northing)
        {
            int i = Segment(chainage, out double t);
            easting = _e[i] + t * (_e[i + 1] - _e[i]);
            northing = _n[i] + t * (_n[i + 1] - _n[i]);
        }

        /// <summary>측점의 진행 방향 단위 벡터 (동, 북)</summary>
        public void DirectionAt(double chainage, out double east, out double north)
        {
            int i = Segment(chainage, out _);
            double dx = _e[i + 1] - _e[i], dy = _n[i + 1] - _n[i];
            double len = Math.Sqrt(dx * dx + dy * dy);
            east = dx / len;
            north = dy / len;
        }

        private double Clamp(double chainage) => Math.Max(0.0, Math.Min(Length, chainage));

        private int Segment(double chainage, out double t)
        {
            double c = Clamp(chainage);
            int i = Array.BinarySearch(_cum, c);
            if (i < 0) i = ~i - 1;
            if (i >= _cum.Length - 1) i = _cum.Length - 2;
            if (i < 0) i = 0;
            double seg = _cum[i + 1] - _cum[i];
            t = seg > 0 ? (c - _cum[i]) / seg : 0.0;
            return i;
        }
    }

    /// <summary>
    /// 터널 위의 지표 (2026-11-21): 토피와 지보 패턴 배정.
    /// </summary>
    public static class TunnelGround
    {
        /// <summary>
        /// 측점 0, step, 2·step, … 마다 토피 = 지표 표고 − (노면 + 천장 높이). 지표가 없는 곳은 NaN.
        /// 지표는 위성 DSM이라 나무 높이가 섞여 있다 (수 m — 수백 m 토피에는 작은 차이).
        /// </summary>
        public static double[] CoverProfile(ElevationGrid surface, TunnelAlignment alignment, double crownAboveRoad, double step)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));
            if (alignment == null) throw new ArgumentNullException(nameof(alignment));
            if (!(step > 0)) throw new ArgumentOutOfRangeException(nameof(step));
            int n = (int)Math.Floor(alignment.Length / step + 1e-9) + 1;
            var cover = new double[n];
            for (int i = 0; i < n; i++)
            {
                double ch = i * step;
                alignment.PointAt(ch, out double e, out double nn);
                cover[i] = surface.TryGetElevationAt(e, nn, out double z)
                    ? z - (alignment.RoadElevation(ch) + crownAboveRoad)
                    : double.NaN;
            }
            return cover;
        }

        /// <summary>
        /// 지보 패턴 배정 (**가정**): 토피가 얕은 곳일수록 나쁜 지반(풍화·이완)으로 보고, 얕은 순서대로 P-5, P-4, … P-1을
        /// 주어진 비율(shares[0] = P-1 … shares[4] = P-5)만큼 나눠 준다. 표본 하나의 순위 위치 = (순위 + 0.5) / 개수.
        /// 실제 패턴은 막장 관찰과 암반 분류(RMR)로 정한다 — 이것은 지질 자료가 없을 때의 대체 방법이다.
        /// 돌려주는 값: 표본마다 패턴 번호 (0 = P-1 … 4 = P-5).
        /// </summary>
        public static int[] AssignPatternsByCover(double[] cover, double[] shares)
        {
            if (cover == null) throw new ArgumentNullException(nameof(cover));
            if (shares == null || shares.Length == 0) throw new ArgumentException(nameof(shares));
            int n = cover.Length;
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            // 얕은 순 (NaN은 가장 얕게), 같으면 측점 순 (안정 정렬)
            Array.Sort(order, (a, b) =>
            {
                double ca = double.IsNaN(cover[a]) ? double.NegativeInfinity : cover[a];
                double cb = double.IsNaN(cover[b]) ? double.NegativeInfinity : cover[b];
                int c = ca.CompareTo(cb);
                return c != 0 ? c : a.CompareTo(b);
            });

            double total = 0.0;
            foreach (double s in shares) total += s;
            var result = new int[n];
            for (int rank = 0; rank < n; rank++)
            {
                double f = (rank + 0.5) / n;
                double acc = 0.0;
                int chosen = 0;
                for (int k = shares.Length - 1; k >= 0; k--)   // 나쁜 패턴(번호 큰 것)부터 채운다
                {
                    acc += shares[k] / total;
                    chosen = k;
                    if (f <= acc + 1e-12) break;
                }
                result[order[rank]] = chosen;
            }
            return result;
        }
    }
}
