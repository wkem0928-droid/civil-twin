using System;
using System.Collections.Generic;

namespace CivilTwin.Core
{
    /// <summary>터널 막장 하나: 시작 측점, 방향(+1 = 측점이 커지는 쪽), 시작하는 날, 이름</summary>
    public readonly struct TunnelFace
    {
        public readonly double Start;
        public readonly int Direction;
        public readonly double StartDay;
        public readonly string Name;

        public TunnelFace(double start, int direction, double startDay, string name)
        {
            if (direction != 1 && direction != -1) throw new ArgumentOutOfRangeException(nameof(direction));
            if (!(startDay >= 0)) throw new ArgumentOutOfRangeException(nameof(startDay));
            Start = start;
            Direction = direction;
            StartDay = startDay;
            Name = name ?? "";
        }
    }

    /// <summary>
    /// 터널 굴착 공정 (2026-11-21): 여러 막장이 동시에 파 들어가 서로 만나면(관통) 멈춘다.
    ///
    /// 방법: 시간을 dt(기본 0.25일)씩 나눠, 시작한 막장마다 "지금 위치의 하루 굴진(m/일) × dt"만큼 나아간다.
    /// 하루 굴진은 위치마다 다르다 (그 측점의 지보 패턴 — rateAt). 막장이 멈추는 곳:
    ///   +방향 막장은 앞쪽(측점이 더 큰 쪽)에서 가장 가까운 시작점에 있는 막장 중 −방향인 것의 지금 위치,
    ///   그런 막장이 없으면 그 시작점(아직 시작하지 않은 막장의 자리), 앞에 아무도 없으면 터널 끝.  (−방향도 대칭)
    /// 막장들이 판 길이의 합이 터널 길이가 되면 끝 (FinishDay).
    ///
    /// 손계산: 길이 1,000 m, 양 끝 막장 2개가 5 m/일 → 500 m씩 → 100일.
    ///         가운데(500 m)에서 40일째에 막장 2개를 더 열면: 40일까지 양 끝 200 m씩, 남은 300 m씩을 두 막장이 10 m/일 → 30일 → 70일.
    /// </summary>
    public sealed class TunnelSchedule
    {
        private readonly TunnelFace[] _faces;
        private readonly List<double[]> _history = new List<double[]>();   // [k] = k·dt일 뒤 막장 위치들

        public double Length { get; }
        public double Step { get; }
        public double FinishDay { get; }
        public int FaceCount => _faces.Length;
        public TunnelFace Face(int i) => _faces[i];

        public TunnelSchedule(double length, IReadOnlyList<TunnelFace> faces, Func<double, double> rateAt, double step = 0.25,
            double maxDays = 100000.0)
        {
            if (!(length > 0)) throw new ArgumentOutOfRangeException(nameof(length));
            if (faces == null || faces.Count == 0) throw new ArgumentException("막장이 하나 이상 있어야 한다.");
            if (rateAt == null) throw new ArgumentNullException(nameof(rateAt));
            if (!(step > 0)) throw new ArgumentOutOfRangeException(nameof(step));
            Length = length;
            Step = step;
            _faces = new TunnelFace[faces.Count];
            for (int i = 0; i < faces.Count; i++) _faces[i] = faces[i];

            var pos = new double[_faces.Length];
            for (int i = 0; i < pos.Length; i++) pos[i] = _faces[i].Start;
            _history.Add((double[])pos.Clone());

            double day = 0.0;
            while (true)
            {
                if (Excavated(pos) >= length - 1e-6) break;
                if (day > maxDays) throw new InvalidOperationException("관통하지 못한다 (막장이 만나지 않거나 굴진이 0).");
                for (int i = 0; i < _faces.Length; i++)
                {
                    TunnelFace f = _faces[i];
                    if (day < f.StartDay) continue;
                    double limit = Limit(i, pos);
                    double move = rateAt(pos[i]) * step;
                    pos[i] = f.Direction > 0 ? Math.Min(limit, pos[i] + move) : Math.Max(limit, pos[i] - move);
                }
                day += step;
                _history.Add((double[])pos.Clone());
            }
            FinishDay = day;
        }

        private double Excavated(double[] pos)
        {
            double sum = 0.0;
            for (int i = 0; i < pos.Length; i++) sum += Math.Abs(pos[i] - _faces[i].Start);
            return sum;
        }

        private double Limit(int i, double[] pos)
        {
            TunnelFace f = _faces[i];
            double nearest = f.Direction > 0 ? double.PositiveInfinity : double.NegativeInfinity;
            for (int j = 0; j < _faces.Length; j++)
            {
                double s = _faces[j].Start;
                if (f.Direction > 0 ? s > f.Start && s < nearest : s < f.Start && s > nearest) nearest = s;
            }
            if (double.IsInfinity(nearest)) return f.Direction > 0 ? Length : 0.0;
            for (int j = 0; j < _faces.Length; j++)
            {
                if (_faces[j].Start == nearest && _faces[j].Direction == -f.Direction) return pos[j];
            }
            return nearest;
        }

        /// <summary>day일째 막장 i의 위치 (측점, m). 기록 사이는 직선 보간.</summary>
        public double FacePosition(int i, double day)
        {
            if (day <= 0) return _history[0][i];
            double k = day / Step;
            int a = (int)Math.Floor(k);
            if (a >= _history.Count - 1) return _history[_history.Count - 1][i];
            double t = k - a;
            return _history[a][i] + t * (_history[a + 1][i] - _history[a][i]);
        }

        /// <summary>day일째까지 판 길이의 합 (m)</summary>
        public double ExcavatedLength(double day)
        {
            double sum = 0.0;
            for (int i = 0; i < _faces.Length; i++) sum += Math.Abs(FacePosition(i, day) - _faces[i].Start);
            return Math.Min(Length, sum);
        }

        /// <summary>측점 chainage가 day일째에 파여 있는가</summary>
        public bool IsExcavated(double chainage, double day)
        {
            for (int i = 0; i < _faces.Length; i++)
            {
                if (day < _faces[i].StartDay) continue;
                double a = _faces[i].Start, b = FacePosition(i, day);
                if (chainage >= Math.Min(a, b) - 1e-9 && chainage <= Math.Max(a, b) + 1e-9 && a != b) return true;
            }
            return false;
        }

        /// <summary>막장 i가 마지막으로 나아간 날 (만나서 멈춘 날). 기록 간격(Step) 단위.</summary>
        public double StopDay(int i)
        {
            for (int k = _history.Count - 1; k >= 1; k--)
            {
                if (Math.Abs(_history[k][i] - _history[k - 1][i]) > 1e-9) return k * Step;
            }
            return _faces[i].StartDay;
        }

        /// <summary>막장 i가 판 날 수 = 멈춘 날 − 시작한 날</summary>
        public double WorkingDays(int i) => Math.Max(0.0, StopDay(i) - _faces[i].StartDay);

        /// <summary>day일째에 아직 파고 있는 막장인가 (시작했고, 만나지 않았다)</summary>
        public bool IsActive(int i, double day)
        {
            if (day < _faces[i].StartDay || day >= FinishDay) return false;
            double before = FacePosition(i, Math.Max(0.0, day - Step));
            double after = FacePosition(i, day + Step);
            return Math.Abs(after - before) > 1e-9;
        }
    }
}
