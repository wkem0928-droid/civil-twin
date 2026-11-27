using System;

namespace CivilTwin.Core
{
    /// <summary>
    /// 장비의 단순한 움직임 계산 (보기용). 화면에 흐르는 시간(초)을 주면 장비의 자세·위치를 돌려준다.
    ///
    /// 기본 꼴은 "갔다가 되돌아오기"다. 한 주기를 반으로 나눠 앞 절반은 0 → 1, 뒤 절반은 1 → 0.
    ///   p = (시간 ÷ 주기)의 소수 부분
    ///   왕복 비율 = p가 0.5보다 작으면 2p, 아니면 2 − 2p
    ///
    /// 손계산 예:
    ///   굴착기 선회: 사이클 20초, 선회각 90° → 5초 45°, 10초 90°(덤프 쪽), 15초 45°, 25초 45°
    ///   덤프트럭 왕복: 편도 40 m, 속도 4 m/초 → 주기 20초. 5초 20 m, 10초 40 m, 12.5초 30 m(돌아오는 중)
    ///
    /// 주의: 이 움직임은 화면을 이해하기 쉽게 하려는 것이다. 공정표의 날짜와는 속도가 다르고,
    /// 실제 장비의 동선·가감속은 반영하지 않았다.
    /// </summary>
    public static class EquipmentMotion
    {
        /// <summary>왕복 비율 (0 → 1 → 0). 주기마다 되풀이한다.</summary>
        public static double BackAndForth(double seconds, double periodSeconds)
        {
            if (!(periodSeconds > 0)) throw new ArgumentOutOfRangeException(nameof(periodSeconds));

            double p = seconds / periodSeconds;
            p -= Math.Floor(p);
            return p < 0.5 ? 2.0 * p : 2.0 - 2.0 * p;
        }

        /// <summary>주기의 뒤 절반(되돌아오는 중)이면 true.</summary>
        public static bool IsReturning(double seconds, double periodSeconds)
        {
            if (!(periodSeconds > 0)) throw new ArgumentOutOfRangeException(nameof(periodSeconds));

            double p = seconds / periodSeconds;
            p -= Math.Floor(p);
            return p >= 0.5;
        }

        /// <summary>
        /// 굴착기 윗몸의 선회각 (도). 파는 자리(0°)에서 싣는 자리(선회각)까지 갔다가 돌아온다.
        /// 한 사이클 = 작업량 계산의 사이클 시간 Cm.
        /// </summary>
        public static double ExcavatorSwingDegrees(double seconds, double cycleSeconds, double swingDegrees)
        {
            return swingDegrees * BackAndForth(seconds, cycleSeconds);
        }

        /// <summary>
        /// 왕복하는 장비가 출발점에서 떨어진 거리 (m). 주기 = 2 × 편도 거리 ÷ 속도.
        /// </summary>
        public static double ShuttleDistance(double seconds, double oneWayMeters, double speedMetersPerSecond)
        {
            if (!(oneWayMeters > 0)) throw new ArgumentOutOfRangeException(nameof(oneWayMeters));
            if (!(speedMetersPerSecond > 0)) throw new ArgumentOutOfRangeException(nameof(speedMetersPerSecond));

            double period = 2.0 * oneWayMeters / speedMetersPerSecond;
            return oneWayMeters * BackAndForth(seconds, period);
        }

        /// <summary>왕복하는 장비가 되돌아오는 중이면 true.</summary>
        public static bool ShuttleIsReturning(double seconds, double oneWayMeters, double speedMetersPerSecond)
        {
            if (!(oneWayMeters > 0)) throw new ArgumentOutOfRangeException(nameof(oneWayMeters));
            if (!(speedMetersPerSecond > 0)) throw new ArgumentOutOfRangeException(nameof(speedMetersPerSecond));

            return IsReturning(seconds, 2.0 * oneWayMeters / speedMetersPerSecond);
        }
    }
}
