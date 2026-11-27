using System;
using System.Collections.Generic;

namespace CivilTwin.Core
{
    /// <summary>
    /// 터널 2차 라이닝 (현장 타설 콘크리트 라이닝)과 방수 (2026-11-21).
    ///
    /// 출처 — 2025년 적용 표준품셈 [토목부문] 3장 터널공사 (원문 PDF 334 ~ 335쪽):
    ///   3-3-1 터널 철재거푸집 설치·해체·이동 (회당): 형틀목공 6, 콘크리트공 2, 특별인부 1, 보통인부 2, 콘크리트 펌프(차) 1대,
    ///         소요일수(설치/콘크리트타설/해체/이동) 1일. 주 ①: 현장 조립이 끝난 철제거푸집 1 span(2차로급 도로)을 방수면에 설치·타설·양생·해체·이동.
    ///   3-4-1 터널 방수 (m²당): 방수공 0.011인, 보통인부 0.002인, 일체식 방수시트 1.15 m² (할증 포함).
    ///   3-4-2 작업대차 조립 및 해체 (회당): 조립 4일, 해체 2일 (방수 작업용 대차 L = 10 m).
    ///
    /// 가정 (표준품셈에 없는 값):
    ///   거푸집 1 span 길이 12 m, 라이닝 두께 30 cm, 라이닝 둘레 = 숏크리트 둘레(아치 + 측벽 28 m),
    ///   튜브마다 거푸집 2조(양 입구에서 하나씩 안쪽으로), 방수 대차 조립(4일)을 마치고 시작,
    ///   방수는 거푸집보다 앞서 끝나도록 사람을 맞춘다 (방수가 라이닝 속도를 정하지 않음).
    ///
    /// 기본 (관통 뒤): 거푸집 한 조가 맡는 길이 = 튜브 길이 ÷ 거푸집 수, span 수 = 올림(맡는 길이 ÷ span 길이),
    ///     라이닝 기간 = span 수 × 1일, 끝나는 날 = 관통일 + 대차 조립 4일 + 라이닝 기간.
    ///     손계산 (인제양양): 10,970.97 ÷ 2 = 5,485.5 m ÷ 12 = 457.1 → 458 span → 458일, 관통 927 + 4 + 458 = 1,389일.
    ///
    /// 대안 — 굴착과 병행: 강재 거푸집 아래로 버력 트럭이 지나갈 수 있어 실무에서는 막장 뒤에서 라이닝을 함께 치기도 한다.
    ///     공사 시작 + 대차 조립 4일부터, "다음 span 끝 + Lag(500 m, 가정)" 지점까지 파여 있을 때만 친다. 관통 뒤에는 제한이 없다.
    ///     손계산 (길이 1,000 m, 양 끝 막장 5 m/일 → 관통 100일, Lag 100 m, 거푸집 2조):
    ///       m번째 span은 (12m + 100) m가 파인 날, 곧 (12m + 100) ÷ 5일 이후 첫 정수 날에 친다 (막장이 거푸집보다 느리다).
    ///       첫 span: 112 ÷ 5 = 22.4 → 23일째. 관통(100일) 전 마지막: m = 32 → 484 ÷ 5 = 96.8 → 97일째 (384 m).
    ///       남은 500 − 384 = 116 m = 10 span → 100 ~ 109일째 → 110일에 끝 (관통 뒤 시작이면 146일).
    ///
    /// 계산 방법: 하루씩 나아가며, 거푸집마다 그날 칠 수 있으면 span 하나(남은 길이가 짧으면 그만큼)를 친다.
    /// 한계: 인버트·배수·포장·설비 공정은 없다. 병행할 때 갱내 운반·환기 간섭으로 굴진이 느려지는 것은 넣지 않았다.
    /// </summary>
    public sealed class TunnelLining
    {
        public const double DaysPerSpan = 1.0;          // 3-3-1 소요일수
        public const double CarriageAssemblyDays = 4.0; // 3-4-2 작업대차 조립
        public const double FormCarpenters = 6, ConcreteWorkers = 2, SpecialLaborers = 1, CommonLaborers = 2;   // 3-3-1 회당
        public const double WaterproofWorkerPerM2 = 0.011, WaterproofLaborerPerM2 = 0.002, SheetPerM2 = 1.15;  // 3-4-1

        public double SpanLength { get; }
        public double Thickness { get; }
        public int FormsPerTube { get; }
        public double Perimeter { get; }
        public double TubeLength { get; }
        public int Tubes { get; }
        public double BreakthroughDay { get; }
        /// <summary>굴착과 병행하면 true (막장에서 Lag만큼 뒤까지만 친다, 관통 뒤에는 제한 없음)</summary>
        public bool Concurrent { get; }
        /// <summary>병행할 때 라이닝 거푸집과 막장 사이에 두는 거리 (m) — 가정</summary>
        public double Lag { get; }
        /// <summary>처음 타설한 날 (거푸집 중 가장 먼저 시작한 것)</summary>
        public double StartDay { get; }
        public double FinishDay { get; }
        public double Duration => FinishDay - StartDay;

        /// <summary>거푸집 한 조가 맡는 길이 (m)</summary>
        public double SegmentLength => TubeLength / FormsPerTube;
        /// <summary>거푸집 한 조의 span 수 (올림)</summary>
        public int SpansPerForm => (int)Math.Ceiling(SegmentLength / SpanLength - 1e-9);
        /// <summary>튜브 하나의 타설 횟수 (거푸집마다 마지막 짧은 span 포함)</summary>
        public int PoursPerTube => FormsPerTube * SpansPerForm;

        private readonly double _earliest;                       // 첫날 (대차 조립이 끝난 날)
        private readonly List<double[]> _history = new List<double[]>();   // [i] = earliest + i일 아침의 거푸집별 친 길이

        /// <param name="excavated">(측점, 날) → 그날 파여 있는가. 병행(concurrent)일 때만 쓴다.</param>
        public TunnelLining(double tubeLength, int tubes, double breakthroughDay, double perimeter,
            int formsPerTube = 2, double spanLength = 12.0, double thickness = 0.30,
            bool concurrent = false, double lag = 500.0, Func<double, double, bool> excavated = null)
        {
            if (!(tubeLength > 0)) throw new ArgumentOutOfRangeException(nameof(tubeLength));
            if (tubes < 1) throw new ArgumentOutOfRangeException(nameof(tubes));
            if (!(breakthroughDay >= 0)) throw new ArgumentOutOfRangeException(nameof(breakthroughDay));
            if (!(perimeter > 0)) throw new ArgumentOutOfRangeException(nameof(perimeter));
            if (formsPerTube < 1) throw new ArgumentOutOfRangeException(nameof(formsPerTube));
            if (!(spanLength > 0)) throw new ArgumentOutOfRangeException(nameof(spanLength));
            if (!(thickness > 0)) throw new ArgumentOutOfRangeException(nameof(thickness));
            if (!(lag >= 0)) throw new ArgumentOutOfRangeException(nameof(lag));
            if (concurrent && excavated == null) throw new ArgumentNullException(nameof(excavated), "병행하려면 굴착 공정이 필요하다.");
            TubeLength = tubeLength;
            Tubes = tubes;
            Perimeter = perimeter;
            FormsPerTube = formsPerTube;
            SpanLength = spanLength;
            Thickness = thickness;
            BreakthroughDay = breakthroughDay;
            Concurrent = concurrent;
            Lag = lag;

            _earliest = (concurrent ? 0.0 : breakthroughDay) + CarriageAssemblyDays;
            var p = new double[formsPerTube];
            _history.Add((double[])p.Clone());
            double first = double.NaN;
            double day = _earliest;
            while (true)
            {
                bool allDone = true;
                for (int k = 0; k < formsPerTube; k++) if (p[k] < SegmentLength - 1e-9) allDone = false;
                if (allDone) break;
                if (day > _earliest + 200000.0) throw new InvalidOperationException("라이닝이 끝나지 않는다.");
                for (int k = 0; k < formsPerTube; k++)
                {
                    double step = Math.Min(spanLength, SegmentLength - p[k]);
                    if (step <= 1e-9) continue;
                    bool ok = day >= breakthroughDay;
                    if (!ok && concurrent)
                    {
                        Segment(k, out double s0, out int dir);
                        double target = Math.Max(0.0, Math.Min(tubeLength, s0 + dir * (p[k] + step + lag)));
                        ok = excavated(target, day);
                    }
                    if (!ok) continue;
                    p[k] += step;
                    if (double.IsNaN(first)) first = day;
                }
                day += DaysPerSpan;
                _history.Add((double[])p.Clone());
            }
            StartDay = double.IsNaN(first) ? _earliest : first;
            FinishDay = day;
        }

        /// <summary>관통 뒤에 시작 (기본)</summary>
        public static TunnelLining For(TunnelPlan plan, int formsPerTube = 2) =>
            new TunnelLining(plan.Alignment.Length, plan.Spec.Tubes, plan.Schedule.FinishDay, plan.Section.LiningPerimeter, formsPerTube);

        /// <summary>굴착과 병행 (막장에서 lag m 뒤까지)</summary>
        public static TunnelLining Concurrently(TunnelPlan plan, int formsPerTube = 2, double lag = 500.0) =>
            new TunnelLining(plan.Alignment.Length, plan.Spec.Tubes, plan.Schedule.FinishDay, plan.Section.LiningPerimeter, formsPerTube,
                concurrent: true, lag: lag, excavated: plan.Schedule.IsExcavated);

        /// <summary>
        /// 거푸집 k가 맡은 구간의 시작 측점과 방향. 짝수 번째는 구간의 서쪽 끝에서 동쪽으로, 홀수 번째는 동쪽 끝에서 서쪽으로
        /// (거푸집 2조면 양 입구에서 안쪽으로 들어간다).
        /// </summary>
        private void Segment(int k, out double start, out int direction)
        {
            double a = k * SegmentLength, b = (k + 1) * SegmentLength;
            if (k % 2 == 0) { start = a; direction = +1; }
            else { start = b; direction = -1; }
        }

        /// <summary>day일째에 거푸집 k가 친 길이 (m). 하루에 span 하나 (날 안에서는 직선으로 늘린다 — 화면용)</summary>
        public double FormProgress(int k, double day)
        {
            double x = (day - _earliest) / DaysPerSpan;
            if (x <= 0) return 0.0;
            int i = (int)Math.Floor(x);
            if (i >= _history.Count - 1) return _history[_history.Count - 1][k];
            double t = x - i;
            return _history[i][k] + t * (_history[i + 1][k] - _history[i][k]);
        }

        /// <summary>day일째 튜브 하나에서 라이닝을 친 길이 (m)</summary>
        public double LinedLength(double day)
        {
            double sum = 0.0;
            for (int k = 0; k < FormsPerTube; k++) sum += FormProgress(k, day);
            return sum;
        }

        /// <summary>측점 chainage가 day일째에 라이닝되어 있는가</summary>
        public bool IsLined(double chainage, double day)
        {
            int k = (int)Math.Floor(chainage / SegmentLength);
            if (k < 0) k = 0;
            if (k > FormsPerTube - 1) k = FormsPerTube - 1;
            double p = FormProgress(k, day);
            if (p <= 0) return false;
            Segment(k, out double start, out int dir);
            double d = (chainage - start) * dir;
            return d >= -1e-9 && d <= p + 1e-9;
        }

        /// <summary>day일째 거푸집 k의 위치 (측점, 아직 시작 전이면 시작점)</summary>
        public double FormPosition(int k, double day)
        {
            Segment(k, out double start, out int dir);
            return start + dir * FormProgress(k, day);
        }

        // ---------- 물량과 인력 (튜브 모두) ----------

        /// <summary>라이닝 콘크리트 (m³) = 둘레 × 두께 × 길이 × 튜브 수</summary>
        public double Concrete => Perimeter * Thickness * TubeLength * Tubes;
        /// <summary>한 번 타설량 (m³, 12 m span)</summary>
        public double ConcretePerPour => Perimeter * Thickness * SpanLength;
        /// <summary>방수 면적 (m²) = 둘레 × 길이 × 튜브 수</summary>
        public double WaterproofArea => Perimeter * TubeLength * Tubes;
        public double WaterproofSheet => WaterproofArea * SheetPerM2;
        /// <summary>라이닝 인력 (인) = 타설 횟수 × 11인 (3-3-1)</summary>
        public double LiningLabor => PoursPerTube * Tubes * (FormCarpenters + ConcreteWorkers + SpecialLaborers + CommonLaborers);
        /// <summary>방수 인력 (인) = 면적 × (0.011 + 0.002)</summary>
        public double WaterproofLabor => WaterproofArea * (WaterproofWorkerPerM2 + WaterproofLaborerPerM2);

        /// <summary>라이닝 속도를 따라가려면 하루에 필요한 방수공 (명, 튜브 모두) = 하루 라이닝 면적 × 0.011</summary>
        public double WaterproofWorkersPerDay => FormsPerTube * Tubes * SpanLength / DaysPerSpan * Perimeter * WaterproofWorkerPerM2;
    }
}
