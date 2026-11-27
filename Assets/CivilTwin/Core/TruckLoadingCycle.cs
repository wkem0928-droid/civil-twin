using System;

namespace CivilTwin.Core
{
    /// <summary>트럭 한 대의 지금 단계</summary>
    public enum TruckPhase { Loading, Out, FarTurn, Dumping, Back, Waiting, InTurn }

    /// <summary>트럭의 지금 상태: 단계, 단계 안의 진행(0 ~ 1), 적재함에 실린 흙(0 ~ 1), 적재함을 든 정도(0 = 내림, 1 = 다 듦)</summary>
    public readonly struct TruckState
    {
        public readonly TruckPhase Phase;
        public readonly double Progress;
        public readonly double Soil;
        public readonly double BedRaise;

        public TruckState(TruckPhase phase, double progress, double soil, double bedRaise = 0.0)
        {
            Phase = phase;
            Progress = progress;
            Soil = soil;
            BedRaise = bedRaise;
        }
    }

    /// <summary>
    /// 굴착기 한 대가 덤프트럭 두 대에 번갈아 싣는 모습 (보기용, 2026-11-20).
    ///
    /// 표준품셈의 적재 횟수 n = 트럭 적재량(흐트러진 상태) ÷ (q × K)를 올림한 만큼 굴착기가 퍼 담는다.
    ///   적재 시간 L = n × Cm,  한 번 싣는 주기 = L + G (G = 트럭이 바뀌는 시간)
    /// 트럭 두 대는 주기 하나씩 어긋나 있어서, 한 대가 싣는 동안 다른 한 대는 다녀온다(→ 굴착기가 쉬지 않는다).
    /// 트럭이 바뀌는 G 동안 굴착기는 흙을 퍼 든 채(굴착 끝 자세) 기다린다.
    ///
    /// 굴착기 시각: 싣는 중이면 (주기 안 시각 + 0.30 Cm) — 퍼 든 자세(굴착 끝)에서 시작해 선회·덤프를 n번,
    ///              바뀌는 중이면 0.30 Cm에 멈춤(퍼 든 자세). 그래서 자세가 끊기지 않는다.
    /// 흙: 덤프 한가운데(사이클 (0.55 + 0.70) / 2 = 0.625)를 지날 때마다 1/n씩 찬다 → 싣기 시작 뒤 0.325 Cm, 1.325 Cm, … 에 한 칸씩.
    /// 트럭이 바뀌는 회전 시간은 G보다 길면 안 된다 (떠나는 트럭과 겹친다) → 화면에서는 회전 = G.
    ///
    /// 흙 부리기 (2026-11-20): 반대편 끝에서 돌아 뒤를 부릴 곳으로 향한 뒤 멈춰 적재함을 든다 (표준품셈 t3 "적하").
    ///   덤프 시간 D를 셋으로 나눈다: 드는 중 0 ~ 0.3 (smoothstep), 든 채 0.3 ~ 0.7, 내리는 중 0.7 ~ 1.
    ///   흙은 0.2 ~ 0.7 동안 1 → 0으로 곧게 준다 (조금 기울어진 뒤부터 쏟아진다).
    ///   손계산 (D 8초): 덤프 시작 뒤 4초 = 진행 0.5 → 든 정도 1, 흙 1 − (0.5 − 0.2) / 0.5 = 0.4.
    ///   D = 0이면 예전처럼 반대편 회전을 시작할 때 흙이 사라진다.
    ///
    /// 손계산 예 (Cm 19초, n 13, G 4초): L = 247초, 주기 251초. 싣기 시작 뒤 6.175초 = 1/13, 25.175초 = 2/13 … 234.175초 = 13/13.
    ///   트럭 0은 0 ~ 247초 적재, 트럭 1은 251 ~ 498초 적재.
    /// ⚠️ 이동·회전 시간은 보기용 가정이다 (공기 계산의 운반 시간과 다르다).
    /// </summary>
    public sealed class TruckLoadingCycle
    {
        public double CycleSeconds { get; }
        public int Buckets { get; }
        public double ExchangeSeconds { get; }
        public double TravelSeconds { get; }
        public double TurnSeconds { get; }
        public double DumpSeconds { get; }

        public TruckLoadingCycle(double cycleSeconds, int buckets, double exchangeSeconds, double travelSeconds, double turnSeconds)
            : this(cycleSeconds, buckets, exchangeSeconds, travelSeconds, turnSeconds, 0.0)
        {
        }

        public TruckLoadingCycle(double cycleSeconds, int buckets, double exchangeSeconds, double travelSeconds, double turnSeconds,
            double dumpSeconds)
        {
            if (!(cycleSeconds > 0)) throw new ArgumentOutOfRangeException(nameof(cycleSeconds));
            if (buckets < 1) throw new ArgumentOutOfRangeException(nameof(buckets));
            if (!(exchangeSeconds > 0)) throw new ArgumentOutOfRangeException(nameof(exchangeSeconds));
            if (!(travelSeconds > 0)) throw new ArgumentOutOfRangeException(nameof(travelSeconds));
            if (!(turnSeconds > 0)) throw new ArgumentOutOfRangeException(nameof(turnSeconds));
            if (!(dumpSeconds >= 0)) throw new ArgumentOutOfRangeException(nameof(dumpSeconds));

            CycleSeconds = cycleSeconds;
            Buckets = buckets;
            ExchangeSeconds = exchangeSeconds;
            TurnSeconds = turnSeconds;

            // 트럭 한 대의 주기 2(L + G) 안에 적재 L, 다녀오기(2 × 이동 + 회전 + 덤프), 들어오는 회전이 들어가야 한다.
            //   2(L + G) − L − 2 × 회전 − D − 2 × 이동 ≥ 0  →  이동 ≤ (L + 2G − 2 × 회전 − D) / 2
            double maxDump = Math.Max(0.0, LoadSeconds + 2.0 * ExchangeSeconds - 2.0 * turnSeconds - 0.2);
            DumpSeconds = Math.Min(dumpSeconds, maxDump);
            double room = LoadSeconds + 2.0 * ExchangeSeconds - 2.0 * turnSeconds - DumpSeconds;
            TravelSeconds = Math.Min(travelSeconds, Math.Max(0.1, room / 2.0));
        }

        /// <summary>트럭 한 대를 채우는 버킷 횟수 n: 표준품셈 식(올림하지 않은 값)을 올림한다. 최소 1.</summary>
        public static int BucketsFor(double loaderCycles) => Math.Max(1, (int)Math.Ceiling(loaderCycles - 1e-9));

        /// <summary>적재 시간 L = n × Cm (초)</summary>
        public double LoadSeconds => Buckets * CycleSeconds;

        /// <summary>한 번 싣는 주기 L + G (초)</summary>
        public double LoadPeriod => LoadSeconds + ExchangeSeconds;

        /// <summary>굴착기 자세를 구할 사이클 시각 (ExcavatorCycle.PoseAt에 넣는다)</summary>
        public double ExcavatorTime(double seconds)
        {
            double tau = Mod(seconds, LoadPeriod);
            double dig = ExcavatorCycle.DigEnd * CycleSeconds;
            return tau < LoadSeconds ? tau + dig : dig;
        }

        /// <summary>트럭 index(0 또는 1)의 상태</summary>
        public TruckState TruckAt(double seconds, int index)
        {
            double period = 2.0 * LoadPeriod;
            double tau = Mod(seconds - index * LoadPeriod, period);
            double l = LoadSeconds;

            if (tau < l)
            {
                double first = (ExcavatorCycle.DumpEnd + ExcavatorCycle.SwingLoadedEnd) / 2.0 - ExcavatorCycle.DigEnd;   // 0.325
                int done = tau < first * CycleSeconds ? 0 : Math.Min(Buckets, (int)Math.Floor((tau - first * CycleSeconds) / CycleSeconds) + 1);
                return new TruckState(TruckPhase.Loading, tau / l, (double)done / Buckets);
            }
            double t = tau - l;
            if (t < TravelSeconds) return new TruckState(TruckPhase.Out, t / TravelSeconds, 1.0);
            t -= TravelSeconds;
            bool dumps = DumpSeconds > 0;
            if (t < TurnSeconds) return new TruckState(TruckPhase.FarTurn, t / TurnSeconds, dumps ? 1.0 : 0.0);   // 반대편 끝에서 돈다
            t -= TurnSeconds;
            if (t < DumpSeconds)
            {
                double u = t / DumpSeconds;
                return new TruckState(TruckPhase.Dumping, u, DumpSoil(u), BedRaise(u));
            }
            t -= DumpSeconds;
            if (t < TravelSeconds) return new TruckState(TruckPhase.Back, t / TravelSeconds, 0.0);
            t -= TravelSeconds;
            double wait = period - l - 2.0 * TravelSeconds - TurnSeconds - DumpSeconds - TurnSeconds;
            if (t < wait) return new TruckState(TruckPhase.Waiting, wait > 0 ? t / wait : 1.0, 0.0);
            t -= wait;
            return new TruckState(TruckPhase.InTurn, Math.Min(1.0, t / TurnSeconds), 0.0);
        }

        /// <summary>덤프 진행 u(0 ~ 1)에서 적재함을 든 정도: 0 ~ 0.3 들고, 0.7까지 든 채, 1까지 내린다.</summary>
        public static double BedRaise(double u)
        {
            if (u <= 0.0 || u >= 1.0) return 0.0;
            if (u < 0.3) return ExcavatorCycle.Smooth(u / 0.3);
            if (u <= 0.7) return 1.0;
            return ExcavatorCycle.Smooth((1.0 - u) / 0.3);
        }

        /// <summary>덤프 진행 u에서 남은 흙: 0.2까지 그대로, 0.7까지 곧게 줄어 0.</summary>
        public static double DumpSoil(double u)
        {
            if (u <= 0.2) return 1.0;
            if (u >= 0.7) return 0.0;
            return 1.0 - (u - 0.2) / 0.5;
        }

        private static double Mod(double a, double m)
        {
            double r = a % m;
            return r < 0 ? r + m : r;
        }
    }
}
