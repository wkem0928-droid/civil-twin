using System;

namespace CivilTwin.Core
{
    /// <summary>
    /// 누적 토공량 그래프(공정률 곡선)에 쓸 값: 가로축 = 날짜, 세로축 = 그날까지의 누적 절토량·성토량.
    ///
    /// 새 계산식은 없다. 공정 계산(EarthworkSchedule)의 누적값을 일정한 간격으로 뽑아 표로 만든다.
    ///   i번째 날짜 = 공기 × i / 구간 수        (i = 0 … 구간 수)
    ///   누적 절토량 = min(절토량, 하루 절토 작업량 × 날짜)   (성토도 같다)
    ///
    /// 손계산 예: 절토 10000 m³, 성토 4000 m³, 하루 1000 m³ → 공기 10일. 구간 10개로 뽑으면
    ///   날짜   0     1     2     3     4     5   …   10
    ///   절토   0  1000  2000  3000  4000  5000   … 10000
    ///   성토   0  1000  2000  3000  4000  4000   …  4000   (성토는 4일째에 끝나 그 뒤로 평평하다)
    ///
    /// 세로축 눈금의 최댓값은 읽기 쉬운 수(1, 2, 5 × 10ⁿ)로 올린다.
    ///   15042.9 → 20000,   4000 → 5000,   10000 → 10000
    /// </summary>
    public sealed class EarthworkCurve
    {
        /// <summary>날짜 (일). 0부터 공기까지 같은 간격.</summary>
        public double[] Days { get; }

        /// <summary>그 날짜까지의 누적 절토량 (m³)</summary>
        public double[] Cut { get; }

        /// <summary>그 날짜까지의 누적 성토량 (m³)</summary>
        public double[] Fill { get; }

        /// <summary>그 날짜의 공정률 (%)</summary>
        public double[] Percent { get; }

        private EarthworkCurve(double[] days, double[] cut, double[] fill, double[] percent)
        {
            Days = days;
            Cut = cut;
            Fill = fill;
            Percent = percent;
        }

        /// <summary>공정을 intervals개 구간으로 나눠 (intervals + 1)개의 점을 뽑는다.</summary>
        public static EarthworkCurve Sample(EarthworkSchedule schedule, int intervals)
        {
            if (intervals < 1) throw new ArgumentOutOfRangeException(nameof(intervals));

            int count = intervals + 1;
            var days = new double[count];
            var cut = new double[count];
            var fill = new double[count];
            var percent = new double[count];
            double totalDays = schedule.TotalDays;

            for (int i = 0; i < count; i++)
            {
                double day = totalDays * i / intervals;
                days[i] = day;
                cut[i] = schedule.CumulativeCut(day);
                fill[i] = schedule.CumulativeFill(day);
                percent[i] = schedule.ProgressPercent(day);
            }

            return new EarthworkCurve(days, cut, fill, percent);
        }

        /// <summary>
        /// 세로축 최댓값: value 이상인 가장 작은 "1, 2, 5 × 10ⁿ" 꼴의 수.
        /// value가 0 이하이면 1.
        /// </summary>
        public static double NiceAxisMax(double value)
        {
            if (!(value > 0)) return 1.0;

            double power = Math.Pow(10.0, Math.Floor(Math.Log10(value)));
            double mantissa = value / power;   // 1 이상 10 미만
            const double tolerance = 1e-9;

            if (mantissa <= 1.0 + tolerance) return 1.0 * power;
            if (mantissa <= 2.0 + tolerance) return 2.0 * power;
            if (mantissa <= 5.0 + tolerance) return 5.0 * power;
            return 10.0 * power;
        }
    }
}
