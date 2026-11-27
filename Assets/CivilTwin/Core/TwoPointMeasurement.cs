using System;

namespace CivilTwin.Core
{
    /// <summary>
    /// 두 점 A → B 사이의 측정값.
    ///
    /// 계산식 (ΔE = B동 - A동, ΔN = B북 - A북, ΔH = B표고 - A표고):
    ///   수평거리   = √(ΔE² + ΔN²)
    ///   고저차     = ΔH                    (+ 오르막, - 내리막)
    ///   사거리     = √(수평거리² + ΔH²)
    ///   경사(%)    = ΔH / 수평거리 × 100
    ///   경사각(°)  = atan(ΔH / 수평거리)
    ///   구배 1:n   = 수평거리 / |ΔH|       (수직 1 에 대한 수평 n)
    ///   방위각(°)  = atan2(ΔE, ΔN)         (북 0°, 시계 방향, 0 이상 360 미만)
    ///
    /// 수평거리가 0이면(같은 위치의 위아래) 경사와 방위각은 정의되지 않는다.
    /// </summary>
    public readonly struct TwoPointMeasurement
    {
        private const double RadiansToDegrees = 180.0 / Math.PI;

        public readonly SurveyPoint A;
        public readonly SurveyPoint B;

        /// <summary>수평거리 (m)</summary>
        public readonly double HorizontalDistance;

        /// <summary>고저차 (m). B가 높으면 양수.</summary>
        public readonly double HeightDifference;

        /// <summary>사거리 (m). 두 점을 직선으로 이은 실제 길이.</summary>
        public readonly double SlopeDistance;

        private TwoPointMeasurement(SurveyPoint a, SurveyPoint b)
        {
            A = a;
            B = b;

            double de = b.Easting - a.Easting;
            double dn = b.Northing - a.Northing;

            HorizontalDistance = Math.Sqrt(de * de + dn * dn);
            HeightDifference = b.Elevation - a.Elevation;
            SlopeDistance = Math.Sqrt(
                HorizontalDistance * HorizontalDistance +
                HeightDifference * HeightDifference);
        }

        public static TwoPointMeasurement Between(SurveyPoint a, SurveyPoint b)
        {
            return new TwoPointMeasurement(a, b);
        }

        /// <summary>경사와 방위각을 계산할 수 있는가 (수평거리가 0보다 큰가)</summary>
        public bool HasDirection => HorizontalDistance > 0;

        /// <summary>경사 (%). 정의되지 않으면 NaN.</summary>
        public double SlopePercent =>
            HasDirection ? HeightDifference / HorizontalDistance * 100.0 : double.NaN;

        /// <summary>경사각 (도). 정의되지 않으면 NaN.</summary>
        public double SlopeAngleDegrees =>
            HasDirection ? Math.Atan2(HeightDifference, HorizontalDistance) * RadiansToDegrees : double.NaN;

        /// <summary>구배 1:n 의 n. 평지(고저차 0)면 양의 무한대, 정의되지 않으면 NaN.</summary>
        public double SlopeRatioN
        {
            get
            {
                if (!HasDirection) return double.NaN;
                double rise = Math.Abs(HeightDifference);
                return rise > 0 ? HorizontalDistance / rise : double.PositiveInfinity;
            }
        }

        /// <summary>A에서 B를 바라보는 방위각 (도). 북 0°, 동 90°. 정의되지 않으면 NaN.</summary>
        public double AzimuthDegrees
        {
            get
            {
                if (!HasDirection) return double.NaN;
                double degrees = Math.Atan2(B.Easting - A.Easting, B.Northing - A.Northing) * RadiansToDegrees;
                if (degrees < 0) degrees += 360.0;
                return degrees;
            }
        }
    }
}
