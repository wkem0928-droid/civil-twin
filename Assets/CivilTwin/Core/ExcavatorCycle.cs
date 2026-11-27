using System;

namespace CivilTwin.Core
{
    /// <summary>굴착기 자세 (도). 0은 모델을 만든 자세(버킷 이빨이 땅에 닿은 굴착 자세)다.</summary>
    public readonly struct ExcavatorPose
    {
        public readonly double Swing;      // 윗몸 선회각 (파는 자리 0 → 싣는 자리)
        public readonly double BoomRaise;  // 붐 들기 (+ 위로)
        public readonly double ArmOut;     // 암 펴기 (+ 기계에서 멀어짐, − 안으로 당김)
        public readonly double BucketOpen; // 버킷 열기 (+ 입을 벌림·흙을 쏟음, − 오므림)

        public ExcavatorPose(double swing, double boomRaise, double armOut, double bucketOpen)
        {
            Swing = swing;
            BoomRaise = boomRaise;
            ArmOut = armOut;
            BucketOpen = bucketOpen;
        }

        public static ExcavatorPose Lerp(ExcavatorPose a, ExcavatorPose b, double t) => new ExcavatorPose(
            a.Swing + (b.Swing - a.Swing) * t, a.BoomRaise + (b.BoomRaise - a.BoomRaise) * t,
            a.ArmOut + (b.ArmOut - a.ArmOut) * t, a.BucketOpen + (b.BucketOpen - a.BucketOpen) * t);
    }

    /// <summary>
    /// 굴착기 한 사이클의 동작 (보기용, 2026-11-20). 작업량 계산의 사이클 시간 Cm(2025 표준품셈, 기본 19초) 한 번에
    /// 굴착 → 적재 선회 → 덤프(트럭에 쏟기) → 복귀 선회를 한다. 표준품셈의 Cm이 바로 이 네 동작을 합친 시간이다.
    ///
    ///   단계         사이클 비율      자세 (붐 들기, 암 펴기, 버킷 열기)          선회
    ///   ① 굴착       0    ~ 0.30    A(−5, +35, +40) → B(0, −10, −35)          0
    ///   ② 적재 선회  0.30 ~ 0.55    B → C(+25, −10, −35)                      0 → 선회각
    ///   ③ 덤프       0.55 ~ 0.70    C → D(+20, +25, +60)                      선회각
    ///   ④ 복귀 선회  0.70 ~ 1.00    D → A                                     선회각 → 0
    ///
    /// 단계 사이는 부드럽게 (시작과 끝에서 느린 smoothstep: 3s² − 2s³).
    /// 손계산 예 (Cm 19초, 선회 90°): 5.7초(0.30) = B, 선회 0° / 8.075초(0.425) = 적재 선회 한가운데 → 선회 45°
    ///   / 10.45초(0.55) = C, 선회 90° / 13.3초(0.70) = D / 19초 = 다시 A.
    ///
    /// ⚠️ 단계 비율과 관절 각도는 화면을 이해하기 쉽게 정한 가정이다 (실제 장비의 측정값이 아님).
    /// </summary>
    public static class ExcavatorCycle
    {
        public const double DigEnd = 0.30, SwingLoadedEnd = 0.55, DumpEnd = 0.70;

        public static readonly ExcavatorPose DigStart = new ExcavatorPose(0, -5, 35, 40);       // A: 팔을 뻗고 버킷을 벌림
        public static readonly ExcavatorPose DigFinish = new ExcavatorPose(0, 0, -10, -35);     // B: 당겨서 퍼 담음
        public static readonly ExcavatorPose Lifted = new ExcavatorPose(0, 25, -10, -35);       // C: 들어 올림
        public static readonly ExcavatorPose Dumping = new ExcavatorPose(0, 20, 25, 60);        // D: 쏟음

        /// <summary>지금 단계 이름 (화면 설명용)</summary>
        public static string PhaseName(double seconds, double cycleSeconds)
        {
            double p = Fraction(seconds, cycleSeconds);
            if (p < DigEnd) return "굴착";
            if (p < SwingLoadedEnd) return "적재 선회";
            if (p < DumpEnd) return "덤프";
            return "복귀 선회";
        }

        /// <summary>시간(초)에서의 자세. swingDegrees = 파는 자리에서 트럭까지의 선회각.</summary>
        public static ExcavatorPose PoseAt(double seconds, double cycleSeconds, double swingDegrees)
        {
            double p = Fraction(seconds, cycleSeconds);
            ExcavatorPose a, b;
            double s0, s1, t;
            if (p < DigEnd)
            {
                a = DigStart; b = DigFinish; s0 = 0; s1 = 0; t = p / DigEnd;
            }
            else if (p < SwingLoadedEnd)
            {
                a = DigFinish; b = Lifted; s0 = 0; s1 = swingDegrees; t = (p - DigEnd) / (SwingLoadedEnd - DigEnd);
            }
            else if (p < DumpEnd)
            {
                a = Lifted; b = Dumping; s0 = swingDegrees; s1 = swingDegrees; t = (p - SwingLoadedEnd) / (DumpEnd - SwingLoadedEnd);
            }
            else
            {
                a = Dumping; b = DigStart; s0 = swingDegrees; s1 = 0; t = (p - DumpEnd) / (1.0 - DumpEnd);
            }

            double k = Smooth(t);
            ExcavatorPose joints = ExcavatorPose.Lerp(a, b, k);
            return new ExcavatorPose(s0 + (s1 - s0) * k, joints.BoomRaise, joints.ArmOut, joints.BucketOpen);
        }

        /// <summary>부드러운 보간: 3s² − 2s³ (s = 0 → 0, 0.5 → 0.5, 1 → 1, 양 끝에서 기울기 0)</summary>
        public static double Smooth(double s)
        {
            s = Math.Max(0.0, Math.Min(1.0, s));
            return s * s * (3.0 - 2.0 * s);
        }

        private static double Fraction(double seconds, double cycleSeconds)
        {
            if (!(cycleSeconds > 0)) throw new ArgumentOutOfRangeException(nameof(cycleSeconds));
            double p = seconds / cycleSeconds;
            return p - Math.Floor(p);
        }
    }
}
