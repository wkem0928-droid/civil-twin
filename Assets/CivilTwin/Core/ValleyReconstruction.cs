using System;

namespace CivilTwin.Core
{
    /// <summary>
    /// 댐 지점의 공사 전 계곡 모양 (축 방향 단면). 양안(양쪽 산비탈)을 직선 사면으로, 바닥을 평평하게 본다:
    ///   원지반(s) = max(바닥 표고,  왼쪽 기준점 표고 + k × (왼쪽 기준점 s − s),  오른쪽 기준점 표고 + k × (s − 오른쪽 기준점 s))
    /// k = 사면 기울기 (수평 1 m당 오르는 높이).
    /// </summary>
    public readonly struct AbutmentValley
    {
        public readonly double BedElevation;
        public readonly double Slope;
        public readonly double LeftS, LeftZ;    // 왼쪽(−s 쪽) 사면이 지나는 점
        public readonly double RightS, RightZ;  // 오른쪽(+s 쪽) 사면이 지나는 점

        public AbutmentValley(double bedElevation, double slope, double leftS, double leftZ, double rightS, double rightZ)
        {
            if (!(slope > 0)) throw new ArgumentOutOfRangeException(nameof(slope), "사면 기울기는 0보다 커야 한다.");
            BedElevation = bedElevation;
            Slope = slope;
            LeftS = leftS;
            LeftZ = leftZ;
            RightS = rightS;
            RightZ = rightZ;
        }

        public double Elevation(double s)
        {
            double left = LeftZ + Slope * (LeftS - s);
            double right = RightZ + Slope * (s - RightS);
            return Math.Max(BedElevation, Math.Max(left, right));
        }

        /// <summary>
        /// 측량 지형(댐이 이미 있는 지형)에서 계곡 모양을 정한다.
        /// 한쪽 양안의 자연 사면(축 위 s = fitFrom ~ fitTo 구간)에 직선을 맞춰 기울기 k를 구하고,
        /// 반대쪽은 같은 기울기로 mirrorS 지점(측량 표고)을 지나게 한다.
        /// 쓰는 이유: 댐 지점의 원래 계곡은 지금 댐에 덮여 있어 직접 알 수 없다. 덮이지 않은 양안 사면을 연장한다.
        /// </summary>
        public static AbutmentValley FitFromSurvey(ElevationGrid surveyed, DamSection dam,
            double fitFrom, double fitTo, double mirrorS, double bedElevation, double step = 10.0)
        {
            if (surveyed == null) throw new ArgumentNullException(nameof(surveyed));
            if (dam == null) throw new ArgumentNullException(nameof(dam));
            if (!(fitTo > fitFrom)) throw new ArgumentException("fitFrom < fitTo 여야 한다.");

            double n = 0, ms = 0, mz = 0;
            for (double s = fitFrom; s <= fitTo + 1e-9; s += step)
            {
                dam.FromAxis(s, 0.0, out double e, out double nn);
                if (!surveyed.TryGetElevationAt(e, nn, out double z)) continue;
                n++; ms += s; mz += z;
            }
            if (n < 2) throw new ArgumentException("맞출 표고 점이 부족하다.");
            ms /= n; mz /= n;
            double sxx = 0, sxz = 0;
            for (double s = fitFrom; s <= fitTo + 1e-9; s += step)
            {
                dam.FromAxis(s, 0.0, out double e, out double nn);
                if (!surveyed.TryGetElevationAt(e, nn, out double z)) continue;
                sxx += (s - ms) * (s - ms);
                sxz += (s - ms) * (z - mz);
            }
            double m = sxz / sxx;              // dz/ds
            double k = Math.Abs(m);

            dam.FromAxis(mirrorS, 0.0, out double me, out double mn);
            if (!surveyed.TryGetElevationAt(me, mn, out double mirrorZ)) throw new ArgumentException("mirrorS 지점에 표고가 없다.");

            if (m < 0)
            {
                // 맞춘 쪽이 왼쪽(−s): s가 줄수록 높아진다. 기준점은 맞춘 구간의 안쪽 끝.
                double leftZ = mz + m * (fitTo - ms);
                return new AbutmentValley(bedElevation, k, fitTo, leftZ, mirrorS, mirrorZ);
            }
            double rightZ = mz + m * (fitFrom - ms);
            return new AbutmentValley(bedElevation, k, mirrorS, mirrorZ, fitFrom, rightZ);
        }
    }

    /// <summary>
    /// 공사 전 원지반 복원 (2026-11-20). 위성 지형(DSM)에는 댐이 이미 지어져 있고 상류는 저수지 수면이라,
    /// 댐 둘레의 띠(축 수직 방향으로 하류 끝 − downstreamExtra ~ 상류 끝 + upstreamExtra, 축 방향 |s| ≤ HalfLength)에서
    ///   복원 표고 = min(측량 표고, 계곡 단면(s))
    /// 로 바꾼다. 계곡은 축에 수직 방향으로 단면이 같다(프리즘)고 본다. min을 쓰는 이유: 댐이 있는 곳은 측량 표고가
    /// 원지반보다 높으므로 계곡으로 내리고, 양안의 자연 지형이 계곡 직선보다 낮은 곳은 측량 그대로 둔다.
    /// 한계: 실제 계곡은 상·하류로 가며 폭과 바닥이 바뀐다. 저수지 바닥(수면 아래)은 알 수 없다.
    /// </summary>
    public static class ValleyReconstruction
    {
        public static ElevationGrid Rebuild(ElevationGrid surveyed, DamSection dam, AbutmentValley valley,
            double upstreamExtra, double downstreamExtra)
        {
            if (surveyed == null) throw new ArgumentNullException(nameof(surveyed));
            if (dam == null) throw new ArgumentNullException(nameof(dam));

            double dMax = dam.UpstreamBase + upstreamExtra;
            double dMin = -(dam.DownstreamBase + downstreamExtra);
            var z = new double[surveyed.Columns * surveyed.Rows];
            for (int row = 0; row < surveyed.Rows; row++)
            {
                double northing = surveyed.GetNorthing(row);
                for (int col = 0; col < surveyed.Columns; col++)
                {
                    double g = surveyed.GetElevation(col, row);
                    dam.ToAxis(surveyed.GetEasting(col), northing, out double s, out double d);
                    if (!double.IsNaN(g) && Math.Abs(s) <= dam.HalfLength && d >= dMin && d <= dMax)
                    {
                        g = Math.Min(g, valley.Elevation(s));
                    }
                    z[row * surveyed.Columns + col] = g;
                }
            }
            return new ElevationGrid(surveyed.Columns, surveyed.Rows, surveyed.CellSize,
                surveyed.OriginEasting, surveyed.OriginNorthing, z);
        }
    }
}
