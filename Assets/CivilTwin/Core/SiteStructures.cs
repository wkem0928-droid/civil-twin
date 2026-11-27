using System;

namespace CivilTwin.Core
{
    /// <summary>
    /// 댐 하나의 자료 묶음 (2026-11-21). 댐 모드는 이 묶음만 보고 동작한다 → 같은 코드로 여러 회사의 댐을 쌓는다.
    /// 값마다 출처나 "가정"을 함께 적는다 (Docs/ASSUMPTIONS.md, Docs/SITES.md).
    /// </summary>
    public sealed class DamSpec
    {
        /// <summary>지형 파일 이름 (이 현장에서만 댐을 쌓을 수 있다)</summary>
        public string FileName;
        public string Company;
        public string Name;
        /// <summary>형식 (예: "중앙차수벽형 사력댐")</summary>
        public string TypeName;
        public double CrestElevation;
        public double Height;
        public double Length;
        /// <summary>공개 체적 (m³)</summary>
        public double OfficialVolume;
        /// <summary>사면 구배 출처 설명 (예: "공개 자료", "상류 가정 · 하류는 위성에서 잼")</summary>
        public string SlopeSource;

        /// <summary>실제 공사 기간 (달력)</summary>
        public DateTime Start, End;
        /// <summary>기간 표시 (예: "1967.4 ~ 1973.10")</summary>
        public string PeriodLabel;
        /// <summary>기간에 대한 설명 (예: "여수로·발전소 포함")</summary>
        public string PeriodNote;

        /// <summary>한 층 다짐 두께 (m)와 그 근거</summary>
        public double LiftMeters;
        public string LiftSource;

        /// <summary>복원한 계곡을 설명하는 말 (어느 쪽 사면을 맞췄는지) — 뒤에 "(1:n)을 … 대칭으로"가 붙는다</summary>
        public string FittedSide, MirroredSide;
        /// <summary>재료 설명 (예: "사력")</summary>
        public string Material;

        /// <summary>공사 전 계곡을 복원할 띠의 여유 (축 수직 방향, 상류·하류)</summary>
        public double UpstreamExtra, DownstreamExtra;

        /// <summary>보고서에 적을 가정 한 줄</summary>
        public string ReportAssumption;

        /// <summary>처음 화면의 카메라: 주시점 거리 (m)와 주시점의 마루 위 높이 (m). 댐 크기에 맞춰 눈으로 정한 보기용 값.</summary>
        public double CameraDistance, CameraAboveCrest;

        public Func<DamSection> CreateSection;
        public Func<ElevationGrid, DamSection, AbutmentValley> FitValley;

        public string Title => Company + " " + Name;

        /// <summary>실제 공사 달력 날수</summary>
        public int CalendarDays => (int)(End - Start).TotalDays;

        /// <summary>층 수 = 높이 ÷ 한 층 두께 (올림)</summary>
        public int Lifts => (int)Math.Ceiling(Height / LiftMeters - 1e-9);
    }

    /// <summary>
    /// 현장별 실제 구조물 자료 (2026-11-20). "그 회사가 시공한 ○○"을 보여 주려고 현장을 골랐으므로,
    /// 그 현장의 구조물을 공개 제원으로 만들어 계산 결과를 공개 값과 비교한다.
    /// </summary>
    public static class SiteStructures
    {
        // ---------- 현대건설 소양강댐 (중앙차수벽형 사력댐, 1967 ~ 1973) ----------

        public const string SoyanggangFile = "hyundai_soyanggang.asc";

        /// <summary>
        /// 공개 제원 (출처: 한국댐협회 "한국의 댐", K-water연구원 「소양강댐이 국가 및 지역에 미치는 사회경제적 편익산정」 2013):
        ///   마루 표고 EL 203.00 m, 높이 123 m, 길이 530 m, 체적 9,591,200 m³, 상류 사면 1:2.0 ~ 2.3, 하류 1:1.8 ~ 2.0, 저폭 540 m.
        /// </summary>
        public const double SoyanggangCrest = 203.0;
        public const double SoyanggangHeight = 123.0;
        public const double SoyanggangLength = 530.0;
        public const double SoyanggangOfficialVolume = 9591200.0;

        /// <summary>
        /// 소양강댐 단면. 축 중심·방향은 위성 지형에서 마루 능선(표고 195 ~ 203 m인 격자점 13개)에 직선을 맞춰 구했다:
        ///   중심 동 271,586.54 · 북 594,285.0, 축 방향 동쪽에서 −59.21° (북북서 ↔ 남남동), 상류(소양호)는 축의 왼쪽.
        /// 마루폭 11 m는 공개 자료에서 찾지 못해 저폭 540 m에서 역산한 추정값이다: 540 − 123 × (2.3 + 2.0) = 11.1.
        /// 상류 1:2.3, 하류 1:2.0은 공개 범위의 완만한 쪽 (저폭 540 m와 맞는 조합).
        /// HalfLength 450 m: 마루 길이 530 m의 절반보다 넉넉히 (양안에서는 지형이 마루보다 높아 저절로 끝난다).
        /// </summary>
        public static DamSection SoyanggangDam() => new DamSection(
            271586.54, 594285.0, -59.21,
            SoyanggangCrest, 11.0, 2.3, 2.0, SoyanggangCrest - SoyanggangHeight,
            upstreamIsLeft: true, halfLength: 450.0);

        /// <summary>
        /// 공사 전 계곡: 북쪽 양안(축 위 s = −480 ~ −270 m, 댐 밖의 자연 사면)에 직선을 맞추고, 남쪽은 같은 기울기로
        /// s = +330 m 지점을 지나게 한다. 남쪽 지형은 여수로·안부로 보이는 낮은 곳(s 240 ~ 300)이 있어 자연 사면으로 쓰지 않았다
        /// (그 기울기를 그대로 쓰면 체적이 2.2백만 m³로 터무니없이 작아진다 — 2026-11-20 시험 계산). 바닥은 기초 표고 EL 80 m.
        /// </summary>
        public static AbutmentValley SoyanggangValley(ElevationGrid surveyed, DamSection dam) =>
            AbutmentValley.FitFromSurvey(surveyed, dam, -480.0, -270.0, 330.0, dam.FoundationElevation);

        /// <summary>
        /// 실제 공사 기간: 1967년 4월 15일 착공 ~ 1973년 10월 15일 준공 (K-water 소양강댐 소개). 달력 날수 2,375일.
        /// 댐 축조뿐 아니라 가배수로·여수로·발전소 공사가 모두 들어 있는 기간이다.
        /// </summary>
        public static readonly DateTime SoyanggangStart = new DateTime(1967, 4, 15);
        public static readonly DateTime SoyanggangEnd = new DateTime(1973, 10, 15);
        public static int SoyanggangCalendarDays => (int)(SoyanggangEnd - SoyanggangStart).TotalDays;

        /// <summary>복원할 띠의 여유: 상류는 저수지 수면(183 m) 아래 계곡까지 150 m 더, 하류는 60 m 더.</summary>
        public const double SoyanggangUpstreamExtra = 150.0;
        public const double SoyanggangDownstreamExtra = 60.0;

        /// <summary>위성 지형에서 잰 하류 사면 (DamSlopeSurvey, 2026-11-21): 하천 바닥 79 m(하류 띠의 낮은 10 %), 축 s −150 ~ +150 → 1:2.06.
        /// 공개 1:2.0과 3 % 차이 → 같은 방법을 부항댐에 쓰고 이 비율로 보정한다.</summary>
        public const double SoyanggangRiverBed = 79.0;

        public static readonly DamSpec Soyanggang = new DamSpec
        {
            FileName = SoyanggangFile,
            Company = "현대건설",
            Name = "소양강댐",
            TypeName = "중앙차수벽형 사력댐",
            CrestElevation = SoyanggangCrest,
            Height = SoyanggangHeight,
            Length = SoyanggangLength,
            OfficialVolume = SoyanggangOfficialVolume,
            SlopeSource = "공개 자료",
            Start = SoyanggangStart,
            End = SoyanggangEnd,
            PeriodLabel = "1967.4 ~ 1973.10",
            PeriodNote = "여수로·발전소 포함",
            LiftMeters = 0.30,
            LiftSource = "표준품셈 3-4-1 흙쌓기 두께",
            FittedSide = "북쪽",
            MirroredSide = "남쪽",
            Material = "사력",
            UpstreamExtra = SoyanggangUpstreamExtra,
            DownstreamExtra = SoyanggangDownstreamExtra,
            ReportAssumption = "댐: 공개 제원(한국댐협회, K-water연구원 2013), 마루폭 11 m 역산, 공사 전 계곡 복원(양안 대칭), 재료 한 가지 (가정)",
            CameraDistance = SoyanggangLength * 2.6,
            CameraAboveCrest = 40.0,
            CreateSection = SoyanggangDam,
            FitValley = SoyanggangValley,
        };

        // ---------- GS건설 김천 부항댐 (콘크리트 표면차수벽형 석괴댐, 2006 ~ 2013) ----------

        public const string BuhangFile = "gs_buhang.asc";

        /// <summary>
        /// 공개 제원 (K-water 낙동강유역본부 김천부항댐 소개): 높이 64 m, 길이 472 m, 체적 2,162,000 m³, 마루 EL 201.4 m,
        /// 표면차수벽형 석괴댐. 시공: GS건설(본댐), 설계·시공 일괄입찰 (K-water 2009 감사결과보고서 3·9쪽).
        /// 단면: 하상 사력재를 주 축조존(3B, 다짐 두께 60 cm)에 배치한 CFRD (같은 보고서 17 ~ 18쪽).
        /// </summary>
        public const double BuhangCrest = 201.4;
        public const double BuhangHeight = 64.0;
        public const double BuhangLength = 472.0;
        public const double BuhangOfficialVolume = 2162000.0;

        /// <summary>위성 지형에서 하류 사면을 잴 때의 하천 바닥 (하류 띠 d −220 ~ −160 m의 낮은 10 %)</summary>
        public const double BuhangRiverBed = 151.0;

        /// <summary>
        /// 부항댐 단면 (2026-11-21).
        ///   축: 위성 지형의 마루 능선(표고 195 ~ 200 m 격자점 10개)에 직선을 맞춤 → 중심 동 290,550 · 북 376,374, 방향 47.78°
        ///       (남서 ↔ 북동), 능선 점의 축 이탈 평균 5.8 m. 상류(부항호)는 축의 왼쪽(북서).
        ///   사면: 공개 자료에서 찾지 못했다 (검증되지 않음).
        ///     하류 1:2.6 = 위성에서 잰 1:2.72 × 소양강댐 보정(공개 2.0 ÷ 위성 2.06). 하류면의 환경친화존(1B)을 포함한 겉모양이다.
        ///     상류 1:1.4 = 표면차수벽형 석괴댐에서 흔히 쓰는 값으로 **가정** (출처 확인 전). 상류면은 대부분 물속이라 위성으로 잴 수 없다.
        ///   마루폭 8 m: **가정** (출처 확인 전).
        ///   기초 EL 137.4 = 마루 201.4 − 높이 64 (소양강댐과 같은 규칙).
        /// </summary>
        public static DamSection BuhangDam() => new DamSection(
            290550.0, 376374.0, 47.78,
            BuhangCrest, 8.0, 1.4, 2.6, BuhangCrest - BuhangHeight,
            upstreamIsLeft: true, halfLength: 400.0);

        /// <summary>
        /// 공사 전 계곡: 남서쪽 양안(축 위 s = −450 ~ −320 m)에 직선을 맞추고(기울기 0.325 = 1:3.08), 북동쪽은 같은 기울기로
        /// s = +270 m 지점을 지나게 한다 (소양강댐과 같은 방법). 북동쪽만 따로 맞추면 0.28 ~ 0.39로 맞추는 구간에 따라 흔들려서 쓰지 않았다.
        /// </summary>
        public static AbutmentValley BuhangValley(ElevationGrid surveyed, DamSection dam) =>
            AbutmentValley.FitFromSurvey(surveyed, dam, -450.0, -320.0, 270.0, dam.FoundationElevation);

        /// <summary>
        /// 실제 공사 기간: 2006년 11월 착공 (K-water 소개 "11월 건설에 들어갔다", 날짜는 1일로 **가정**) ~
        /// 2013년 11월 20일 준공식 (국토교통부 보도, 아주경제 2013-11-19). 이설도로·여수로 등 전체 사업 기간이다.
        /// </summary>
        public static readonly DateTime BuhangStart = new DateTime(2006, 11, 1);
        public static readonly DateTime BuhangEnd = new DateTime(2013, 11, 20);

        public static readonly DamSpec Buhang = new DamSpec
        {
            FileName = BuhangFile,
            Company = "GS건설",
            Name = "부항댐",
            TypeName = "표면차수벽형 석괴댐",
            CrestElevation = BuhangCrest,
            Height = BuhangHeight,
            Length = BuhangLength,
            OfficialVolume = BuhangOfficialVolume,
            SlopeSource = "상류·마루폭 가정, 하류는 위성에서 잼",
            Start = BuhangStart,
            End = BuhangEnd,
            PeriodLabel = "2006.11 ~ 2013.11",
            PeriodNote = "이설도로 등 포함",
            LiftMeters = 0.60,
            LiftSource = "주 축조존 3B 공사시방, K-water 2009 감사보고서",
            FittedSide = "남서쪽",
            MirroredSide = "북동쪽",
            Material = "주 축조존의 하상 사력",
            UpstreamExtra = 100.0,
            DownstreamExtra = 60.0,
            ReportAssumption = "댐: 공개 제원(K-water), 사면은 공개 자료에 없어 하류 1:2.6은 위성에서 재고(소양강댐으로 보정) 상류 1:1.4·마루폭 8 m는 가정, "
                + "공사 전 계곡 복원(양안 대칭), 재료 한 가지 (가정)",
            CameraDistance = 760.0,
            CameraAboveCrest = 15.0,
            CreateSection = BuhangDam,
            FitValley = BuhangValley,
        };

        /// <summary>댐 모드를 쓸 수 있는 현장들</summary>
        public static readonly DamSpec[] Dams = { Soyanggang, Buhang };

        /// <summary>지형 파일 이름으로 그 현장의 댐을 찾는다 (없으면 null). 대소문자는 가리지 않는다.</summary>
        public static DamSpec FindDam(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            foreach (DamSpec d in Dams)
            {
                if (string.Equals(d.FileName, fileName, StringComparison.OrdinalIgnoreCase)) return d;
            }
            return null;
        }
    }
}
