using System;

namespace CivilTwin.Core
{
    /// <summary>
    /// 터널 굴착 작업 인원 (2026-11-21). 표준품셈 2025 [토목부문] 3-2-5 "터널굴착 1발파당 작업인원" 발파굴착 C군 (원문 333 ~ 334쪽):
    ///   작업반장 1, 점보드릴 운전원 1, 고소대차 운전원 1, 로더 운전원 1, 굴착기 운전원 1, 숏크리트머신 운전원 1,
    ///   보통인부 2 ~ 4 (가운데 3), 특별인부 4, 화약취급공 1  → 소계 13 ~ 15, 가운데 14인.
    ///   주: 본 품은 3-2-1 싸이클에 드는 인원이며 보조공법 인원은 제외. 터널 안 전기·환기·양수 설비 인력은 별도.
    ///   비고: 병렬터널처럼 한 작업조가 두 막장을 동시에 굴착하면 본 품의 59 %.
    ///
    /// 이 프로그램의 쓰임 (가정):
    ///   막장 하나에 작업조 하나가 붙고, 하루 작업 시간을 8시간 교대로 나눈다 → 교대 수 = 올림(하루 작업 시간 ÷ 8).
    ///   지금 터널 안 작업 인원 (하루 동안 드나드는 사람) = 파고 있는 막장 수 × 튜브 수 × 14 × 교대 수.
    ///   굴착 연인원 (인·일) = Σ 막장마다 판 날 수 × 튜브 수 × 14 × 하루 작업 시간 ÷ 8.
    /// 손계산: 막장 2개가 100일씩, 튜브 1개, 24시간 → 2 × 100 × 14 × 3 = 8,400인·일.
    /// 한계: 버력 운반 트럭 기사, 설비·보조공법·관리 인원은 넣지 않았다. 59 % 규정은 보고서에만 적는다.
    /// </summary>
    public static class TunnelCrew
    {
        public const double Foreman = 1, JumboOperator = 1, PlatformOperator = 1, LoaderOperator = 1, ExcavatorOperator = 1,
            ShotcreteOperator = 1, CommonLaborers = 3, SpecialLaborers = 4, Blaster = 1;

        /// <summary>막장 하나의 작업조 (C군 가운데 값) = 14인</summary>
        public const double PerFace = Foreman + JumboOperator + PlatformOperator + LoaderOperator + ExcavatorOperator
                                      + ShotcreteOperator + CommonLaborers + SpecialLaborers + Blaster;

        /// <summary>한 작업조가 병렬 두 막장을 함께 굴착할 때의 비율 (3-2-5 비고)</summary>
        public const double TwinFaceRatio = 0.59;

        public static int Shifts(double hoursPerDay)
        {
            if (!(hoursPerDay > 0) || hoursPerDay > 24) throw new ArgumentOutOfRangeException(nameof(hoursPerDay));
            return (int)Math.Ceiling(hoursPerDay / 8.0 - 1e-9);
        }

        /// <summary>day일째 파고 있는 막장 수 (튜브 하나)</summary>
        public static int ActiveFaces(TunnelSchedule s, double day)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            int n = 0;
            for (int i = 0; i < s.FaceCount; i++) if (s.IsActive(i, day)) n++;
            return n;
        }

        /// <summary>day일째 터널 굴착 인원 (명, 튜브 모두, 교대 포함)</summary>
        public static double PeopleOnDay(TunnelSchedule s, int tubes, double hoursPerDay, double day) =>
            ActiveFaces(s, day) * tubes * PerFace * Shifts(hoursPerDay);

        /// <summary>굴착 연인원 (인·일, 튜브 모두) = Σ 판 날 수 × 튜브 × 14 × 시간 ÷ 8</summary>
        public static double LaborDays(TunnelSchedule s, int tubes, double hoursPerDay)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            double days = 0.0;
            for (int i = 0; i < s.FaceCount; i++) days += s.WorkingDays(i);
            return days * tubes * PerFace * hoursPerDay / 8.0;
        }
    }
}
