using System;

namespace CivilTwin.Core
{
    /// <summary>
    /// 부지 정지 계획: 직사각형 평탄 부지(계획고가 일정한 면)와 그 둘레의 비탈면.
    /// 원지반 표고를 주면 "공사가 끝난 뒤의 표고(계획면 표고)"를 돌려준다.
    ///
    /// 구배 표기: 1 : n 은 "높이 1 에 수평 n" 이다 (예: 1:1.5 는 1 m 오르내릴 때 수평으로 1.5 m).
    ///
    /// 계산 방법:
    ///   1) 부지 가장자리에서 그 지점까지의 수평거리 d 를 구한다 (부지 안이면 d = 0).
    ///        dx = max(|동 − 중심 동| − 동서 폭 / 2, 0)
    ///        dy = max(|북 − 중심 북| − 남북 폭 / 2, 0)
    ///        d  = √(dx² + dy²)          (모퉁이 바깥은 둥글게 돌아간다)
    ///   2) 그 지점에서 비탈면이 가질 수 있는 높이의 한계를 구한다.
    ///        절토 비탈 높이 = 계획고 + d / 절토 구배 n   (부지에서 멀어질수록 올라간다)
    ///        성토 비탈 높이 = 계획고 − d / 성토 구배 n   (부지에서 멀어질수록 내려간다)
    ///   3) 원지반과 비교한다.
    ///        원지반이 절토 비탈보다 높으면 → 깎아서 절토 비탈 높이로 (절토)
    ///        원지반이 성토 비탈보다 낮으면 → 쌓아서 성토 비탈 높이로 (성토)
    ///        그 사이면                     → 손대지 않는다 (원지반 그대로)
    ///      부지 안(d = 0)은 두 한계가 모두 계획고라서 항상 계획고가 된다.
    ///
    /// 손계산 예: 계획고 50 m, 절토 1:1.0, 성토 1:1.5, 부지 가장자리에서 10 m 떨어진 지점
    ///   절토 비탈 높이 = 50 + 10 / 1.0 = 60.000000
    ///   성토 비탈 높이 = 50 − 10 / 1.5 = 43.333333
    ///   원지반 70 → 60 (절토),  원지반 55 → 55 (그대로),  원지반 40 → 43.333333 (성토)
    ///
    /// 한계: 소단(비탈 중간의 평평한 단), 배수 경사, 방향이 돌아간 부지는 다루지 않는다.
    /// </summary>
    public readonly struct GradingPad
    {
        /// <summary>부지 중심의 동쪽 좌표 (m)</summary>
        public readonly double CenterEasting;

        /// <summary>부지 중심의 북쪽 좌표 (m)</summary>
        public readonly double CenterNorthing;

        /// <summary>부지의 동서 방향 폭 (m)</summary>
        public readonly double WidthEastWest;

        /// <summary>부지의 남북 방향 폭 (m)</summary>
        public readonly double LengthNorthSouth;

        /// <summary>계획고: 평탄 부지의 표고 (m)</summary>
        public readonly double PadElevation;

        /// <summary>절토 비탈 구배 1:n 의 n (수평 / 높이)</summary>
        public readonly double CutSlopeRatio;

        /// <summary>성토 비탈 구배 1:n 의 n (수평 / 높이)</summary>
        public readonly double FillSlopeRatio;

        public GradingPad(
            double centerEasting,
            double centerNorthing,
            double widthEastWest,
            double lengthNorthSouth,
            double padElevation,
            double cutSlopeRatio,
            double fillSlopeRatio)
        {
            if (!(widthEastWest > 0)) throw new ArgumentOutOfRangeException(nameof(widthEastWest), "동서 폭은 0보다 커야 한다.");
            if (!(lengthNorthSouth > 0)) throw new ArgumentOutOfRangeException(nameof(lengthNorthSouth), "남북 폭은 0보다 커야 한다.");
            if (!(cutSlopeRatio > 0)) throw new ArgumentOutOfRangeException(nameof(cutSlopeRatio), "절토 구배 n은 0보다 커야 한다.");
            if (!(fillSlopeRatio > 0)) throw new ArgumentOutOfRangeException(nameof(fillSlopeRatio), "성토 구배 n은 0보다 커야 한다.");
            if (double.IsNaN(padElevation) || double.IsInfinity(padElevation))
            {
                throw new ArgumentOutOfRangeException(nameof(padElevation), "계획고가 숫자가 아니다.");
            }

            CenterEasting = centerEasting;
            CenterNorthing = centerNorthing;
            WidthEastWest = widthEastWest;
            LengthNorthSouth = lengthNorthSouth;
            PadElevation = padElevation;
            CutSlopeRatio = cutSlopeRatio;
            FillSlopeRatio = fillSlopeRatio;
        }

        /// <summary>부지 가장자리에서 그 지점까지의 수평거리 (m). 부지 안과 가장자리 위는 0.</summary>
        public double DistanceFromPad(double easting, double northing)
        {
            double dx = Math.Max(Math.Abs(easting - CenterEasting) - WidthEastWest / 2.0, 0.0);
            double dy = Math.Max(Math.Abs(northing - CenterNorthing) - LengthNorthSouth / 2.0, 0.0);
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// 그 지점의 계획면 표고 (m). 원지반이 빈 값(NaN)이면 계획면도 빈 값이다.
        /// </summary>
        public double DesignElevation(double easting, double northing, double groundElevation)
        {
            if (double.IsNaN(groundElevation)) return double.NaN;

            double d = DistanceFromPad(easting, northing);
            double cutLimit = PadElevation + d / CutSlopeRatio;
            double fillLimit = PadElevation - d / FillSlopeRatio;

            if (groundElevation > cutLimit) return cutLimit;    // 절토
            if (groundElevation < fillLimit) return fillLimit;  // 성토
            return groundElevation;                             // 손대지 않음
        }

        /// <summary>
        /// 원지반 격자와 같은 격자점에서 계획면 표고를 계산해 새 격자로 돌려준다.
        /// </summary>
        public ElevationGrid BuildDesignGrid(ElevationGrid ground)
        {
            if (ground == null) throw new ArgumentNullException(nameof(ground));

            var design = new double[ground.Columns * ground.Rows];
            for (int row = 0; row < ground.Rows; row++)
            {
                double northing = ground.GetNorthing(row);
                for (int col = 0; col < ground.Columns; col++)
                {
                    design[row * ground.Columns + col] =
                        DesignElevation(ground.GetEasting(col), northing, ground.GetElevation(col, row));
                }
            }

            return new ElevationGrid(
                ground.Columns, ground.Rows, ground.CellSize,
                ground.OriginEasting, ground.OriginNorthing, design);
        }
    }
}
