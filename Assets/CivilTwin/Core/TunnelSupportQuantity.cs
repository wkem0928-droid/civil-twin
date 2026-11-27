using System;

namespace CivilTwin.Core
{
    /// <summary>
    /// 터널 지보재 물량 (2026-11-21). 지보 패턴 표(한국도로공사 Ex-TM 글 표 1)의 숏크리트 두께·록볼트 길이와 간격·강지보 유무에서
    /// 1 m당 물량을 내고, 패턴이 차지하는 길이를 곱해 터널 전체 물량을 낸다. 공사 중에는 그날까지 판 구간만 더한다 (4D).
    ///
    ///   숏크리트 설계량 (m³/m) = 둘레(아치 + 측벽) × 두께                     예) P-3: 28 × 0.09 = 2.52
    ///   숏크리트 타설량        = 설계량 ÷ (1 − 반발 손실률)                   (싸이클의 뿜어붙이기 T5와 같은 손실률)
    ///   록볼트 (개/m)          = 1발파 개수 ÷ 굴진장 (분할 굴착은 상반 + 하반) 격자 배치면 = 둘레 ÷ 간격²  예) 28 ÷ 1.5² = 12.44
    ///   록볼트 길이 (m/m)      = 개수 × 록볼트 길이
    ///   강지보 (개/m)          = 1 ÷ 굴진장 (1발파마다 하나, 가정)             예) P-4: 1 ÷ 1.5 = 0.667
    ///
    /// 한계: 여굴을 메우는 숏크리트, 인버트·바닥, 2차 라이닝 콘크리트, 갱문·보조공법은 넣지 않았다. 강지보 간격 = 굴진장은 가정.
    /// </summary>
    public readonly struct SupportPerMeter
    {
        public readonly double Shotcrete;        // 설계량 m³/m
        public readonly double ShotcretePlaced;  // 반발 포함 m³/m
        public readonly double Bolts;            // 개/m
        public readonly double BoltLength;       // m/m
        public readonly double SteelRibs;        // 개/m

        public SupportPerMeter(NatmCycle cycle, TunnelSection section, NatmCrew crew)
        {
            if (cycle == null) throw new ArgumentNullException(nameof(cycle));
            if (section == null) throw new ArgumentNullException(nameof(section));
            if (crew == null) throw new ArgumentNullException(nameof(crew));
            SupportPattern p = cycle.Pattern;
            Shotcrete = section.LiningPerimeter * p.Shotcrete;
            ShotcretePlaced = Shotcrete / (1.0 - crew.ShotcreteLoss);
            Bolts = cycle.BoltsPerMeter;
            BoltLength = Bolts * p.BoltLength;
            SteelRibs = p.SteelRib ? 1.0 / p.Advance : 0.0;
        }
    }

    /// <summary>터널 전체 (또는 어느 날까지)의 지보재 합계. 튜브 수를 곱한 값이다.</summary>
    public readonly struct SupportTotals
    {
        public readonly double Length, Shotcrete, ShotcretePlaced, Bolts, BoltLength, SteelRibs;

        public SupportTotals(double length, double shotcrete, double placed, double bolts, double boltLength, double ribs)
        {
            Length = length;
            Shotcrete = shotcrete;
            ShotcretePlaced = placed;
            Bolts = bolts;
            BoltLength = boltLength;
            SteelRibs = ribs;
        }
    }

    public static class TunnelSupportQuantity
    {
        /// <summary>패턴 k의 1 m당 물량</summary>
        public static SupportPerMeter PerMeter(TunnelPlan plan, int k)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            return new SupportPerMeter(plan.Cycles[k], plan.Section, plan.Crew);
        }

        /// <summary>패턴 k가 차지하는 길이 (m, 튜브 하나) = 길이 × 패턴 비율</summary>
        public static double PatternLength(TunnelPlan plan, int k) => plan.Alignment.Length * plan.PatternShare(k);

        /// <summary>터널 전체 물량 (튜브 모두)</summary>
        public static SupportTotals Total(TunnelPlan plan) => Placed(plan, double.PositiveInfinity);

        /// <summary>
        /// day일째까지 판 구간의 물량 (튜브 모두). 패턴 표본(20 m 간격) 하나가 길이 ÷ 표본 수만큼을 대표한다
        /// (PatternShare와 같은 나눔) — 그래서 관통한 날의 값이 Total과 같다.
        /// </summary>
        public static SupportTotals Placed(TunnelPlan plan, double day)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            int n = plan.Patterns.Length;
            double each = plan.Alignment.Length / n;
            var per = new SupportPerMeter[plan.Cycles.Length];
            for (int k = 0; k < per.Length; k++) per[k] = PerMeter(plan, k);

            bool all = day >= plan.Schedule.FinishDay;
            double len = 0, shot = 0, placed = 0, bolts = 0, boltLen = 0, ribs = 0;
            for (int i = 0; i < n; i++)
            {
                if (!all && !plan.Schedule.IsExcavated(i * TunnelPlan.SampleStep, day)) continue;
                SupportPerMeter q = per[plan.Patterns[i]];
                len += each;
                shot += q.Shotcrete * each;
                placed += q.ShotcretePlaced * each;
                bolts += q.Bolts * each;
                boltLen += q.BoltLength * each;
                ribs += q.SteelRibs * each;
            }
            int t = plan.Spec.Tubes;
            return new SupportTotals(len * t, shot * t, placed * t, bolts * t, boltLen * t, ribs * t);
        }
    }
}
