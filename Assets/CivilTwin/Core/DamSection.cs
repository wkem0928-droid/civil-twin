using System;

namespace CivilTwin.Core
{
    /// <summary>
    /// 필댐(사력댐) 단면 (2026-11-20, 회사별 구조물 1번째: 현대건설 소양강댐).
    /// 곧은 댐 축 하나와 사다리꼴 단면으로 본다:
    ///
    ///   축 좌표: s = 축 방향 거리 (중심에서), d = 축에 수직인 거리 (상류 쪽이 +)
    ///   댐 표면 표고 = 마루 표고 − max(0, |d| − 마루폭/2) / n     (n = 상류면이면 상류 구배, 하류면이면 하류 구배, 1:n)
    ///   계획면 = max(원지반, 댐 표면)   — 댐은 쌓기만 한다 (기초 굴착·코어 트렌치는 넣지 않음)
    ///   |s| &gt; HalfLength 이면 댐이 없다 (축을 따라 먼 곳의 낮은 땅에 엉뚱하게 쌓지 않도록)
    ///
    /// 이렇게 만든 계획면을 기존 토공량 계산(EarthworkVolume, 삼각형 점고법)에 넣으면 성토량 = 댐 체적이다.
    ///
    /// 한계: 존(코어·필터·사력·암) 구분 없이 한 재료로 본다. 축이 휘거나 마루가 기울어진 댐, 여수로·취수탑은 다루지 않는다.
    /// </summary>
    public sealed class DamSection
    {
        public double CenterEasting { get; }
        public double CenterNorthing { get; }

        /// <summary>댐 축 방향 (동쪽에서 반시계 방향 각도, 도). +s 방향이다.</summary>
        public double AxisDegrees { get; }

        public double CrestElevation { get; }
        public double CrestWidth { get; }

        /// <summary>상류면 구배 1:n 의 n</summary>
        public double UpstreamSlope { get; }

        /// <summary>하류면 구배 1:n 의 n</summary>
        public double DownstreamSlope { get; }

        /// <summary>기초 표고 (= 마루 표고 − 댐 높이). 원지반 복원에서 계곡 바닥으로 쓴다.</summary>
        public double FoundationElevation { get; }

        /// <summary>축 방향에서 왼쪽(반시계 90°)이 상류면 true</summary>
        public bool UpstreamIsLeft { get; }

        /// <summary>축 방향으로 댐이 있는 범위 (중심에서 ± 이 거리)</summary>
        public double HalfLength { get; }

        private readonly double _ux, _uy;   // 축 방향 단위 벡터
        private readonly double _nx, _ny;   // 상류 쪽 단위 법선

        public DamSection(double centerEasting, double centerNorthing, double axisDegrees,
            double crestElevation, double crestWidth, double upstreamSlope, double downstreamSlope,
            double foundationElevation, bool upstreamIsLeft, double halfLength)
        {
            if (!(crestWidth >= 0)) throw new ArgumentOutOfRangeException(nameof(crestWidth));
            if (!(upstreamSlope > 0) || !(downstreamSlope > 0)) throw new ArgumentOutOfRangeException(nameof(upstreamSlope), "구배 n은 0보다 커야 한다.");
            if (!(foundationElevation < crestElevation)) throw new ArgumentOutOfRangeException(nameof(foundationElevation), "기초는 마루보다 낮아야 한다.");
            if (!(halfLength > 0)) throw new ArgumentOutOfRangeException(nameof(halfLength));

            CenterEasting = centerEasting;
            CenterNorthing = centerNorthing;
            AxisDegrees = axisDegrees;
            CrestElevation = crestElevation;
            CrestWidth = crestWidth;
            UpstreamSlope = upstreamSlope;
            DownstreamSlope = downstreamSlope;
            FoundationElevation = foundationElevation;
            UpstreamIsLeft = upstreamIsLeft;
            HalfLength = halfLength;

            double a = axisDegrees * Math.PI / 180.0;
            _ux = Math.Cos(a);
            _uy = Math.Sin(a);
            double sign = upstreamIsLeft ? 1.0 : -1.0;
            _nx = -_uy * sign;
            _ny = _ux * sign;
        }

        /// <summary>댐 높이 (m)</summary>
        public double Height => CrestElevation - FoundationElevation;

        /// <summary>축에서 상류 끝(기초 높이에서)까지의 거리 = 마루폭/2 + 높이 × 상류 구배</summary>
        public double UpstreamBase => CrestWidth / 2.0 + Height * UpstreamSlope;

        /// <summary>축에서 하류 끝까지의 거리</summary>
        public double DownstreamBase => CrestWidth / 2.0 + Height * DownstreamSlope;

        /// <summary>측량 좌표 → 축 좌표 (s: 축 방향, d: 상류 쪽 +)</summary>
        public void ToAxis(double easting, double northing, out double s, out double d)
        {
            double dx = easting - CenterEasting, dy = northing - CenterNorthing;
            s = dx * _ux + dy * _uy;
            d = dx * _nx + dy * _ny;
        }

        /// <summary>축 좌표 → 측량 좌표</summary>
        public void FromAxis(double s, double d, out double easting, out double northing)
        {
            easting = CenterEasting + s * _ux + d * _nx;
            northing = CenterNorthing + s * _uy + d * _ny;
        }

        /// <summary>축에서 d 떨어진 곳의 댐 표면 표고 (원지반과 관계없이)</summary>
        public double SurfaceAt(double d)
        {
            double beyond = Math.Abs(d) - CrestWidth / 2.0;
            if (beyond <= 0.0) return CrestElevation;
            return CrestElevation - beyond / (d > 0 ? UpstreamSlope : DownstreamSlope);
        }

        /// <summary>그 지점의 계획면 표고 = max(원지반, 댐 표면). 원지반이 빈 값이면 빈 값.</summary>
        public double DesignElevation(double easting, double northing, double groundElevation)
        {
            if (double.IsNaN(groundElevation)) return double.NaN;
            ToAxis(easting, northing, out double s, out double d);
            if (Math.Abs(s) > HalfLength) return groundElevation;
            double dam = SurfaceAt(d);
            return dam > groundElevation ? dam : groundElevation;
        }

        public ElevationGrid BuildDesignGrid(ElevationGrid ground)
        {
            if (ground == null) throw new ArgumentNullException(nameof(ground));
            var z = new double[ground.Columns * ground.Rows];
            for (int row = 0; row < ground.Rows; row++)
            {
                double northing = ground.GetNorthing(row);
                for (int col = 0; col < ground.Columns; col++)
                {
                    z[row * ground.Columns + col] = DesignElevation(ground.GetEasting(col), northing, ground.GetElevation(col, row));
                }
            }
            return new ElevationGrid(ground.Columns, ground.Rows, ground.CellSize, ground.OriginEasting, ground.OriginNorthing, z);
        }
    }

    /// <summary>
    /// 위성 지형(이미 지어진 댐)에서 하류 사면 구배 1:n을 잰다 (2026-11-21). 공개 자료에 사면 구배가 없는 댐(부항댐)에 쓴다.
    ///
    /// 방법: 축 위 여러 지점 s마다, 마루(d = 0)에서 하류(d 음수)로 2 m씩 가며 표고가
    ///   높은 기준 = 바닥 + 0.7 × (마루 − 바닥),  낮은 기준 = 바닥 + 0.3 × (마루 − 바닥)
    /// 아래로 처음 내려가는 거리 d를 찾는다 (두 점 사이 직선 보간). n = (두 거리의 차) ÷ (두 표고의 차). 지점별 n의 평균.
    /// 위·아래 30 %를 빼는 이유: 30 m 격자는 마루 모서리와 하류 끝(하천 바닥과 만나는 곳)을 뭉개서 완만하게 보이게 한다.
    ///
    /// 검증: 공개 구배를 아는 소양강댐(하류 1:2.0)에서 1:2.06이 나온다 (3 % 차이). 그래서 다른 댐은 이 비율로 보정한다.
    /// 한계: 하류면의 소단·도로·환경친화존(흙을 덮은 부분)까지 포함한 겉모양의 구배다.
    /// </summary>
    public static class DamSlopeSurvey
    {
        public const double LowFraction = 0.3, HighFraction = 0.7, Step = 2.0;

        /// <summary>축 위 sFrom ~ sTo (sStep 간격) 지점들의 평균 하류 구배 n. 잴 수 있는 지점이 없으면 NaN.</summary>
        public static double MeasureDownstream(ElevationGrid surveyed, DamSection dam, double bedElevation,
            double sFrom, double sTo, double sStep, out int sections)
        {
            if (surveyed == null) throw new ArgumentNullException(nameof(surveyed));
            if (dam == null) throw new ArgumentNullException(nameof(dam));
            if (!(dam.CrestElevation > bedElevation)) throw new ArgumentOutOfRangeException(nameof(bedElevation));
            if (!(sStep > 0) || !(sTo >= sFrom)) throw new ArgumentOutOfRangeException(nameof(sStep));

            double zHigh = bedElevation + HighFraction * (dam.CrestElevation - bedElevation);
            double zLow = bedElevation + LowFraction * (dam.CrestElevation - bedElevation);
            double sum = 0.0;
            sections = 0;
            for (double s = sFrom; s <= sTo + 1e-9; s += sStep)
            {
                if (!TryCrossing(surveyed, dam, s, zHigh, out double dHigh)) continue;
                if (!TryCrossing(surveyed, dam, s, zLow, out double dLow)) continue;
                sum += (dHigh - dLow) / (zHigh - zLow);
                sections++;
            }
            return sections > 0 ? sum / sections : double.NaN;
        }

        /// <summary>축 위 s에서 하류로 가며 표고가 z 이하가 되는 첫 거리 d (음수). 400 m 안에 없으면 false.</summary>
        public static bool TryCrossing(ElevationGrid surveyed, DamSection dam, double s, double z, out double d)
        {
            d = double.NaN;
            dam.FromAxis(s, 0.0, out double e0, out double n0);
            if (!surveyed.TryGetElevationAt(e0, n0, out double prevZ)) return false;
            double prevD = 0.0;
            for (double x = -Step; x >= -400.0; x -= Step)
            {
                dam.FromAxis(s, x, out double e, out double n);
                if (!surveyed.TryGetElevationAt(e, n, out double zz)) return false;
                if (zz <= z)
                {
                    d = prevD + (z - prevZ) * (x - prevD) / (zz - prevZ);
                    return true;
                }
                prevD = x;
                prevZ = zz;
            }
            return false;
        }
    }
}
