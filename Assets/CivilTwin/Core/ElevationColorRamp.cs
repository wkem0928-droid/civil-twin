using System;

namespace CivilTwin.Core
{
    /// <summary>색 하나. 빨강·초록·파랑을 각각 0(없음) ~ 1(가득)로 적는다.</summary>
    public readonly struct RgbColor
    {
        public readonly double R;
        public readonly double G;
        public readonly double B;

        public RgbColor(double r, double g, double b)
        {
            R = r;
            G = g;
            B = b;
        }
    }

    /// <summary>
    /// 표고를 색으로 바꾸는 "색 띠". 지형도의 단채(높이별 색칠)와 같은 것이다.
    ///
    /// 원리:
    ///   1) 표고를 0 ~ 1 사이의 비율 t로 바꾼다.   t = (표고 - 최저) / (최고 - 최저)
    ///   2) 색 띠에는 "기준점"이 여러 개 있다. 각 기준점은 (위치 t, 색)이다.
    ///   3) t가 두 기준점 사이에 있으면 두 색을 거리에 비례해 섞는다(직선 보간).
    ///        s = (t - 앞 위치) / (뒤 위치 - 앞 위치)
    ///        색 = 앞 색 + s × (뒤 색 - 앞 색)
    ///
    /// 주의: 색은 "이 지형 안에서의 상대 높이"를 뜻한다. 현장이 바뀌면 같은 색이 다른 표고가 된다.
    ///       그래서 화면에 범례(색과 표고의 대응표)를 반드시 함께 보여 준다.
    /// </summary>
    public sealed class ElevationColorRamp
    {
        private readonly double[] _positions;
        private readonly RgbColor[] _colors;

        /// <summary>빈 값(자료 없음) 자리에 칠할 색. 회색.</summary>
        public RgbColor NoDataColor { get; }

        public int StopCount => _positions.Length;

        public ElevationColorRamp(double[] positions, RgbColor[] colors, RgbColor noDataColor)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            if (colors == null) throw new ArgumentNullException(nameof(colors));
            if (positions.Length < 2) throw new ArgumentException("기준점은 2개 이상이어야 한다.");
            if (positions.Length != colors.Length) throw new ArgumentException("위치와 색의 개수가 다르다.");
            if (positions[0] != 0.0) throw new ArgumentException("첫 기준점의 위치는 0이어야 한다.");
            if (positions[positions.Length - 1] != 1.0) throw new ArgumentException("마지막 기준점의 위치는 1이어야 한다.");

            for (int i = 1; i < positions.Length; i++)
            {
                if (!(positions[i] > positions[i - 1]))
                {
                    throw new ArgumentException("기준점의 위치는 점점 커져야 한다.");
                }
            }

            _positions = (double[])positions.Clone();
            _colors = (RgbColor[])colors.Clone();
            NoDataColor = noDataColor;
        }

        /// <summary>
        /// 기본 색 띠. 낮은 곳부터: 짙은 초록 → 연두 → 누런색 → 갈색 → 흰색에 가까운 회색.
        /// 위치는 0, 0.25, 0.5, 0.75, 1 (표고 범위를 4등분).
        /// </summary>
        public static ElevationColorRamp CreateDefault()
        {
            return new ElevationColorRamp(
                new[] { 0.0, 0.25, 0.5, 0.75, 1.0 },
                new[]
                {
                    new RgbColor(0.20, 0.42, 0.24), // 짙은 초록
                    new RgbColor(0.56, 0.68, 0.36), // 연두
                    new RgbColor(0.86, 0.80, 0.52), // 누런색
                    new RgbColor(0.60, 0.44, 0.30), // 갈색
                    new RgbColor(0.95, 0.95, 0.93), // 흰색에 가까운 회색
                },
                new RgbColor(0.50, 0.50, 0.50));
        }

        /// <summary>비율 t(0 ~ 1)의 색. 범위를 벗어나면 끝 색으로 고정한다.</summary>
        public RgbColor ColorAt(double t)
        {
            if (double.IsNaN(t)) return NoDataColor;
            if (t <= 0.0) return _colors[0];
            if (t >= 1.0) return _colors[_colors.Length - 1];

            for (int i = 1; i < _positions.Length; i++)
            {
                if (t <= _positions[i])
                {
                    double s = (t - _positions[i - 1]) / (_positions[i] - _positions[i - 1]);
                    RgbColor a = _colors[i - 1];
                    RgbColor b = _colors[i];
                    return new RgbColor(
                        a.R + s * (b.R - a.R),
                        a.G + s * (b.G - a.G),
                        a.B + s * (b.B - a.B));
                }
            }

            return _colors[_colors.Length - 1];
        }

        /// <summary>
        /// 표고의 색. 최저 표고는 첫 색, 최고 표고는 마지막 색이 된다.
        /// 표고가 빈 값(NaN)이면 회색. 최저와 최고가 같으면(완전히 평평한 지형) 첫 색.
        /// </summary>
        public RgbColor ColorAtElevation(double elevation, double minElevation, double maxElevation)
        {
            if (double.IsNaN(elevation)) return NoDataColor;
            if (!(maxElevation > minElevation)) return _colors[0];

            return ColorAt((elevation - minElevation) / (maxElevation - minElevation));
        }
    }
}
