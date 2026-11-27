using System;

namespace CivilTwin.Core
{
    /// <summary>
    /// 한 점(주시점)을 중심으로 도는 카메라의 위치 계산.
    /// Unity에 의존하지 않는 순수 계산이라 손계산과 비교할 수 있다.
    ///
    /// 용어:
    ///   주시점 (Target) : 카메라가 바라보는 지면의 한 점
    ///   방위각 (Yaw)    : 카메라가 바라보는 수평 방향. 북 = 0°, 동 = 90°, 남 = 180°, 서 = 270°
    ///   고도각 (Pitch)  : 카메라가 수평면보다 얼마나 위에 있는가. 0° = 수평, 90° = 바로 위
    ///   거리 (Distance) : 주시점에서 카메라까지의 직선 거리 (m)
    ///
    /// 카메라 위치:
    ///   수평거리 = 거리 × cos(고도각)
    ///   X = 주시점X - 수평거리 × sin(방위각)
    ///   Z = 주시점Z - 수평거리 × cos(방위각)
    ///   Y = 주시점Y + 거리 × sin(고도각)
    ///
    ///   (북쪽을 바라보려면 카메라는 주시점의 남쪽에 있어야 하므로 빼기다.)
    /// </summary>
    public sealed class OrbitCamera
    {
        private const double DegreesToRadians = Math.PI / 180.0;

        public LocalPoint Target { get; private set; }
        public double YawDegrees { get; private set; }
        public double PitchDegrees { get; private set; }
        public double Distance { get; private set; }

        public double MinPitchDegrees { get; }
        public double MaxPitchDegrees { get; }
        public double MinDistance { get; }
        public double MaxDistance { get; }

        public OrbitCamera(
            LocalPoint target,
            double yawDegrees,
            double pitchDegrees,
            double distance,
            double minDistance = 5.0,
            double maxDistance = 5000.0,
            double minPitchDegrees = 5.0,
            double maxPitchDegrees = 89.0)
        {
            if (!(minDistance > 0)) throw new ArgumentOutOfRangeException(nameof(minDistance));
            if (maxDistance < minDistance) throw new ArgumentOutOfRangeException(nameof(maxDistance));
            if (maxPitchDegrees < minPitchDegrees) throw new ArgumentOutOfRangeException(nameof(maxPitchDegrees));

            MinDistance = minDistance;
            MaxDistance = maxDistance;
            MinPitchDegrees = minPitchDegrees;
            MaxPitchDegrees = maxPitchDegrees;

            Target = target;
            YawDegrees = WrapDegrees(yawDegrees);
            PitchDegrees = Clamp(pitchDegrees, minPitchDegrees, maxPitchDegrees);
            Distance = Clamp(distance, minDistance, maxDistance);
        }

        /// <summary>카메라 위치 (로컬 좌표, m)</summary>
        public LocalPoint GetPosition()
        {
            double yaw = YawDegrees * DegreesToRadians;
            double pitch = PitchDegrees * DegreesToRadians;
            double horizontal = Distance * Math.Cos(pitch);

            return new LocalPoint(
                Target.X - horizontal * Math.Sin(yaw),
                Target.Y + Distance * Math.Sin(pitch),
                Target.Z - horizontal * Math.Cos(yaw));
        }

        /// <summary>
        /// 주시점을 중심으로 돈다.
        /// 방위각은 0° 이상 360° 미만으로 되돌리고, 고도각은 한계 안에 가둔다.
        /// (고도각이 90°를 넘으면 화면이 뒤집히고, 0° 아래면 땅속으로 들어간다.)
        /// </summary>
        public void Orbit(double deltaYawDegrees, double deltaPitchDegrees)
        {
            YawDegrees = WrapDegrees(YawDegrees + deltaYawDegrees);
            PitchDegrees = Clamp(PitchDegrees + deltaPitchDegrees, MinPitchDegrees, MaxPitchDegrees);
        }

        /// <summary>
        /// 확대/축소. 거리에 배율을 곱한다.
        /// 배율 0.5 = 절반 거리로 다가감, 2.0 = 두 배로 멀어짐.
        /// 더하기가 아니라 곱하기를 쓰는 이유: 멀리서는 빠르게, 가까이서는 세밀하게 움직인다.
        /// </summary>
        public void Zoom(double factor)
        {
            if (!(factor > 0)) throw new ArgumentOutOfRangeException(nameof(factor), "배율은 0보다 커야 한다.");
            Distance = Clamp(Distance * factor, MinDistance, MaxDistance);
        }

        /// <summary>
        /// 주시점을 수평으로 옮긴다 (화면 기준 오른쪽, 앞쪽).
        ///   앞쪽 방향   = ( sin(방위각), 0,  cos(방위각) )
        ///   오른쪽 방향 = ( cos(방위각), 0, -sin(방위각) )
        /// 표고(Y)는 바꾸지 않는다.
        /// </summary>
        public void Pan(double rightMeters, double forwardMeters)
        {
            double yaw = YawDegrees * DegreesToRadians;
            double sin = Math.Sin(yaw);
            double cos = Math.Cos(yaw);

            Target = new LocalPoint(
                Target.X + rightMeters * cos + forwardMeters * sin,
                Target.Y,
                Target.Z - rightMeters * sin + forwardMeters * cos);
        }

        private static double WrapDegrees(double degrees)
        {
            double wrapped = degrees % 360.0;
            if (wrapped < 0) wrapped += 360.0;
            return wrapped;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
