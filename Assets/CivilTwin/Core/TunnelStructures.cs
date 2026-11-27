using System;
using System.Collections.Generic;

namespace CivilTwin.Core
{
    /// <summary>
    /// 터널 하나의 자료 묶음 (2026-11-21). 터널 모드는 이 묶음만 보고 동작한다.
    /// 값마다 출처나 "가정"을 함께 적는다 (Docs/ASSUMPTIONS.md, Docs/SITES.md).
    /// </summary>
    public sealed class TunnelSpec
    {
        public string FileName, Company, Name, Consortium;
        /// <summary>공식 길이 (m)</summary>
        public double OfficialLength;
        public int Tubes;
        public int LanesPerTube;

        /// <summary>두 튜브의 평면 선형 (EPSG:5186, 서 → 동)</summary>
        public double[] TubeAEast, TubeANorth, TubeBEast, TubeBNorth;

        /// <summary>입구 노면 표고 (서쪽, 동쪽) — 위성 지형에서 입구 바깥 50 m 안의 가장 낮은 값 (가정)</summary>
        public double WestRoadElevation, EastRoadElevation;
        public double ReportedGradePercent;

        /// <summary>굴착 시작 (달까지 공개, 날짜는 가정), 관통, 가운데 사갱 구간 관통, 개통</summary>
        public DateTime ExcavationStart, Breakthrough, MiddleAccessReady, Opening;
        /// <summary>가운데 작업구(사갱)가 본선과 만나는 곳 (터널 길이에 대한 비율)</summary>
        public double MiddleAccessFraction;

        /// <summary>보도된 하루 평균 굴착 (m/일, 두 튜브 합), 반출 토석 (m³), 토피 범위 설명</summary>
        public double ReportedDailyAdvance, ReportedMuck;
        public string ReportedCover;

        /// <summary>보고서·글상자에 적을 출처 한 줄</summary>
        public string Sources;

        /// <summary>
        /// 굴착 시작일이 공개되어 있는가. false면 ExcavationStart는 공사 착공일이고, 착공 ~ 관통에는 굴착 외 기간(설계·보상·갱구부 공사 등)이
        /// 들어 있어 계산한 굴착 기간과 직접 비교하지 않는다 (산성터널, 2026-11-21).
        /// </summary>
        public bool ExcavationStartKnown = true;

        /// <summary>
        /// 관통일이 공개되어 있는가 (2026-11-27). false면 Breakthrough는 가정이다:
        /// 착공 + 준비 6개월 + 갱구부 60일 + 올림(길이 ÷ 2 ÷ 막장 평균 3.86 m/일 — 인제양양터널 계산값) (TunnelStructures.AssumedBreakthrough).
        /// </summary>
        public bool BreakthroughKnown = true;

        /// <summary>가운데 작업구(사갱)가 있는 터널인가</summary>
        public bool HasMiddleAccess => MiddleAccessFraction > 0.0;

        public string Title => Company + " " + Name;

        /// <summary>가운데 막장이 시작하는 날 (굴착 시작부터)</summary>
        public double MiddleStartDay => (MiddleAccessReady - ExcavationStart).TotalDays;

        /// <summary>실제 굴착 기간 (굴착 시작 ~ 관통, 달력 날수)</summary>
        public int ActualDays => (int)(Breakthrough - ExcavationStart).TotalDays;

        public TunnelAlignment AlignmentA() => new TunnelAlignment(TubeAEast, TubeANorth, WestRoadElevation, EastRoadElevation);
        public TunnelAlignment AlignmentB() => new TunnelAlignment(TubeBEast, TubeBNorth, WestRoadElevation, EastRoadElevation);
    }

    /// <summary>
    /// 터널 공정 계획 하나 (2026-11-21): 선형 + 토피 + 지보 패턴 배정 + 패턴별 NATM 싸이클 + 막장 공정.
    /// 두 튜브는 길이가 거의 같아(0.01 % 차이) 같은 공정으로 동시에 판다고 본다 (튜브마다 막장 조).
    /// </summary>
    public sealed class TunnelPlan
    {
        public const double SampleStep = 20.0;

        public TunnelSpec Spec { get; }
        public TunnelAlignment Alignment { get; }
        public TunnelSection Section { get; }
        public NatmCrew Crew { get; }
        public double HoursPerDay { get; }
        public bool UseMiddleAccess { get; }
        public double[] Cover { get; }
        public int[] Patterns { get; }
        public NatmCycle[] Cycles { get; }
        public TunnelSchedule Schedule { get; }

        /// <param name="waterIncrease">용수 할증 (0 ~ 0.3, 표준품셈 3-2-1 주 ⑦). 모든 구간에 같게 준다 (어느 구간에 물이 나는지 자료 없음)</param>
        public TunnelPlan(TunnelSpec spec, ElevationGrid surface, double hoursPerDay, int loaders, bool useMiddleAccess, double waterIncrease = 0.0)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            if (surface == null) throw new ArgumentNullException(nameof(surface));
            Spec = spec;
            HoursPerDay = hoursPerDay;
            UseMiddleAccess = useMiddleAccess && spec.HasMiddleAccess;
            Alignment = spec.AlignmentA();
            Section = TunnelSection.TwoLaneRoad();
            if (!(waterIncrease >= 0.0) || waterIncrease > 0.3 + 1e-9) throw new ArgumentOutOfRangeException(nameof(waterIncrease), "주 ⑦: 30 %까지");
            Crew = new NatmCrew { Loaders = loaders, WaterIncrease = waterIncrease };
            Cover = TunnelGround.CoverProfile(surface, Alignment, Section.CrownAboveRoad, SampleStep);
            Patterns = TunnelGround.AssignPatternsByCover(Cover, SupportPattern.AsBuiltShare2014);
            Cycles = new NatmCycle[SupportPattern.Standard.Length];
            for (int k = 0; k < Cycles.Length; k++) Cycles[k] = new NatmCycle(SupportPattern.Standard[k], Section, Crew);

            double length = Alignment.Length;
            var faces = new List<TunnelFace>
            {
                new TunnelFace(0.0, +1, 0.0, "서쪽 입구"),
                new TunnelFace(length, -1, 0.0, "동쪽 입구"),
            };
            if (UseMiddleAccess)
            {
                double mid = length * spec.MiddleAccessFraction;
                faces.Add(new TunnelFace(mid, -1, spec.MiddleStartDay, "사갱 → 서쪽"));
                faces.Add(new TunnelFace(mid, +1, spec.MiddleStartDay, "사갱 → 동쪽"));
            }
            Schedule = new TunnelSchedule(length, faces, RateAt);
        }

        /// <summary>측점의 지보 패턴 번호 (0 = P-1 … 4 = P-5): 가장 가까운 표본 (20 m 간격)</summary>
        public int PatternAt(double chainage)
        {
            int i = (int)Math.Round(chainage / SampleStep);
            if (i < 0) i = 0;
            if (i > Patterns.Length - 1) i = Patterns.Length - 1;
            return Patterns[i];
        }

        /// <summary>측점에서 막장 하나의 하루 굴진 (m/일)</summary>
        public double RateAt(double chainage) => Cycles[PatternAt(chainage)].AdvancePerDay(HoursPerDay);

        /// <summary>패턴 k가 차지하는 길이 비율 (표본 수 기준)</summary>
        public double PatternShare(int k)
        {
            int count = 0;
            foreach (int p in Patterns) if (p == k) count++;
            return (double)count / Patterns.Length;
        }

        /// <summary>터널 전체의 평균 하루 굴진 (막장 하나, m/일) = 길이 ÷ Σ(구간 길이 ÷ 그 구간 굴진)</summary>
        public double AverageFaceAdvance
        {
            get
            {
                double days = 0.0;
                for (int k = 0; k < Cycles.Length; k++) days += PatternShare(k) / Cycles[k].AdvancePerDay(HoursPerDay);
                return 1.0 / days;
            }
        }

        /// <summary>가장 깊은 토피 (m)와 그 측점</summary>
        public double MaxCover(out double chainage)
        {
            double best = double.NegativeInfinity;
            chainage = 0.0;
            for (int i = 0; i < Cover.Length; i++)
            {
                if (Cover[i] > best) { best = Cover[i]; chainage = i * SampleStep; }
            }
            return best;
        }

        /// <summary>설계 단면으로 판 양 (자연 상태 m³, 튜브 모두, 여굴 제외)</summary>
        public double DesignVolume => Section.Area * Alignment.Length * Spec.Tubes;

        /// <summary>두 튜브 합계 하루 평균 굴착 (m/일) = 튜브 수 × 길이 ÷ 관통 일수</summary>
        public double CombinedDailyAdvance => Spec.Tubes * Alignment.Length / Schedule.FinishDay;
    }

    /// <summary>터널 현장 목록 (2026-11-21)</summary>
    public static class TunnelStructures
    {
        public const string InjeFile = "daewoo_inje_yangyang.asc";

        // ---- 산성터널 평면 선형 (EPSG:5186, m, 0.1 m 반올림) — OpenStreetMap 길 629747854(서 → 동), 629747845(동 → 서, 뒤집음) ----
        private static readonly double[] SanseongAEast = { 384629.0, 384952.0, 385021.4, 385105.8, 385167.4, 385226.7, 385314.8, 389384.8 };
        private static readonly double[] SanseongANorth = { 295931.1, 296079.4, 296104.6, 296118.8, 296124.1, 296122.7, 296114.2, 295716.0 };
        private static readonly double[] SanseongBEast = { 384628.9, 384935.1, 385004.8, 385054.6, 385096.8, 385180.5, 385318.2, 389389.5 };
        private static readonly double[] SanseongBNorth = { 295970.5, 296109.4, 296135.5, 296147.7, 296154.6, 296157.6, 296146.8, 295732.5 };

        // ---- 인제양양터널 평면 선형 (EPSG:5186, m, 0.1 m 반올림) ---- (Inje보다 먼저 선언: 정적 필드는 적힌 순서로 초기화된다)

        private static readonly double[] TubeAEast =
        {
            323208.2, 323316.5, 323398.0, 323569.7, 323680.1, 323821.6, 324026.5, 324124.4,
            324327.8, 324510.5, 324674.4, 324851.9, 325042.5, 325276.0, 325453.7, 325652.3,
            325819.9, 326036.7, 326223.3, 326424.7, 326612.8, 326796.9, 326994.0, 327211.1,
            327456.9, 327647.9, 327852.0, 328100.8, 328262.4, 328493.5, 328689.9, 328941.1,
            329147.5, 329370.5, 329570.9, 329816.1, 330024.2, 330249.0, 330472.6, 330704.4,
            330908.9, 331117.4, 331291.8, 331505.3, 331725.1, 331946.6, 332130.3, 332307.1,
            332549.3, 332751.3, 332956.5, 333064.1, 333258.9, 333392.5, 333456.9, 333586.4,
            333688.6, 333771.3,
        };

        private static readonly double[] TubeANorth =
        {
            597779.4, 597856.9, 597909.0, 598006.6, 598063.8, 598133.3, 598220.2, 598260.9,
            598327.1, 598378.5, 598415.0, 598450.1, 598475.7, 598492.4, 598505.2, 598495.6,
            598486.4, 598465.2, 598432.9, 598393.6, 598340.2, 598285.8, 598229.7, 598175.2,
            598133.3, 598099.4, 598071.4, 598045.5, 598034.2, 598032.1, 598031.9, 598042.6,
            598058.2, 598082.2, 598111.6, 598153.5, 598199.6, 598256.8, 598325.9, 598388.5,
            598444.6, 598482.9, 598505.8, 598521.7, 598534.9, 598525.2, 598509.7, 598499.1,
            598512.1, 598540.6, 598596.7, 598634.5, 598720.9, 598796.3, 598836.3, 598927.0,
            599014.9, 599090.9,
        };

        private static readonly double[] TubeBEast =
        {
            323184.8, 323320.8, 323468.3, 323635.6, 323850.3, 324080.6, 324300.8, 324510.0,
            324616.0, 324845.3, 325036.7, 325271.2, 325451.6, 325655.2, 325842.0, 326040.0,
            326223.6, 326414.2, 326615.4, 326806.1, 326998.2, 327217.2, 327463.0, 327648.0,
            327853.8, 328101.6, 328321.1, 328490.2, 328700.8, 328940.2, 329156.2, 329364.7,
            329585.0, 329811.1, 330021.2, 330248.1, 330475.9, 330705.4, 330910.0, 331118.7,
            331289.9, 331507.8, 331716.6, 331946.1, 332128.1, 332299.3, 332551.4, 332751.7,
            332954.6, 333123.3, 333247.1, 333360.9, 333555.7, 333661.6, 333746.8,
        };

        private static readonly double[] TubeBNorth =
        {
            597804.1, 597900.8, 597985.7, 598078.0, 598184.6, 598277.3, 598353.4, 598412.7,
            598436.5, 598481.9, 598509.5, 598524.3, 598537.3, 598528.1, 598518.8, 598497.8,
            598465.1, 598427.1, 598375.2, 598313.2, 598262.4, 598207.9, 598159.2, 598129.3,
            598105.4, 598076.9, 598071.9, 598063.8, 598065.3, 598075.2, 598092.3, 598110.3,
            598146.5, 598185.9, 598229.9, 598290.2, 598358.3, 598425.3, 598477.1, 598514.4,
            598539.8, 598555.9, 598564.8, 598559.9, 598541.8, 598531.0, 598542.4, 598574.0,
            598626.6, 598692.1, 598749.0, 598813.1, 598942.8, 599032.4, 599115.3,
        };

        /// <summary>
        /// 대우건설 인제양양터널 (서울양양고속도로, 2017-06-30 개통). 출처는 Sources 참고.
        ///   길이 10,965 m (왕복 4차로 = 2차로 튜브 2개 — OpenStreetMap에서도 2차로 일방통행 길 2개),
        ///   굴착 2010년 5월 시작 ~ 2012-09-25 관통 (약 2년 5개월), 하루 평균 약 25 m, 반출 토석 196만 m³,
        ///   "네 방향 동시 굴착" — 양 끝 입구 + 터널 한가운데의 사갱 1개소 (2011-04-14 사갱 구간 관통),
        ///   수직갱 2곳(212 m, 307 m, 환기용), 지표면에서 200 ~ 550 m 아래, 동쪽으로 1.95 % 내리막.
        ///   대우건설·금호건설·계룡건설·삼환기업·동일건설 (대우컨소시엄).
        /// 평면 선형: OpenStreetMap 길 504160887(서 → 동 튜브)과 466472010(동 → 서 튜브, 뒤집어 저장) — © OpenStreetMap contributors, ODbL.
        ///   GDAL gdaltransform으로 EPSG:4326 → 5186 변환 (소양강댐 기준점으로 변환 확인). 길이 10,971 m (공식 10,965 m와 0.06 %).
        /// 입구 노면: 서 420 m, 동 216 m (위성 지형, 입구 바깥 50 m 안의 가장 낮은 값) → 1.86 % (보도 1.95 %와 5 % 차이).
        /// </summary>
        public static readonly TunnelSpec Inje = new TunnelSpec
        {
            FileName = InjeFile,
            Company = "대우건설",
            Name = "인제양양터널",
            Consortium = "대우건설·금호건설·계룡건설·삼환기업·동일건설",
            OfficialLength = 10965.0,
            Tubes = 2,
            LanesPerTube = 2,
            TubeAEast = TubeAEast,
            TubeANorth = TubeANorth,
            TubeBEast = TubeBEast,
            TubeBNorth = TubeBNorth,
            WestRoadElevation = 420.0,
            EastRoadElevation = 216.0,
            ReportedGradePercent = 1.95,
            ExcavationStart = new DateTime(2010, 5, 1),
            Breakthrough = new DateTime(2012, 9, 25),
            MiddleAccessReady = new DateTime(2011, 4, 14),
            Opening = new DateTime(2017, 6, 30),
            MiddleAccessFraction = 0.5,
            ReportedDailyAdvance = 25.0,
            ReportedMuck = 1960000.0,
            ReportedCover = "200 ~ 550 m",
            Sources = "길이·날짜·사갱·하루 25 m·토석 196만 m³: 국민일보 2012-09-25, 폴리뉴스 2017-06-14, 위키백과 / 선형: OpenStreetMap (ODbL)",
        };

        public const string SanseongFile = "posco_sanseong.asc";

        /// <summary>
        /// 포스코이앤씨(당시 포스코건설) 부산 산성터널. 출처: 포스코 뉴스룸 2016-10-13 「포스코건설, 부산 산성터널 무재해 관통 성공」 —
        ///   "포스코건설 컨소시엄이 4.87km 길이 터널을 포함해 도로 총 5.62km", 나틈(NATM) 공법, 공사 2013년 8월 ~ 2018년 9월,
        ///   총공사비 1,925억 원, 인근 주거시설·학교로 발파 진동·소음 관리가 어려웠음. 관통일은 "최근"(기사일 2016-10-13).
        /// **공개되지 않은 것:** 굴착 시작일, 막장 구성(사갱 여부), 단면, 지질 → 막장은 양 끝 2개로 보고(가정), 실제 기간과 직접 비교하지 않는다.
        /// 컨소시엄에서 포스코의 지분·주간사 여부도 확인하지 못했다.
        /// 평면 선형: OpenStreetMap '화명대로' 터널 구간 2개 (2차로 일방통행) — 4,814 m / 4,819 m. 입구 노면 서 88.2 m, 동 57.6 m (인제와 같은 방법).
        /// </summary>
        public static readonly TunnelSpec Sanseong = new TunnelSpec
        {
            FileName = SanseongFile,
            Company = "포스코이앤씨",
            Name = "산성터널",
            Consortium = "포스코건설 컨소시엄 (지분·주간사 여부 미확인)",
            OfficialLength = 4870.0,
            Tubes = 2,
            LanesPerTube = 2,
            TubeAEast = SanseongAEast,
            TubeANorth = SanseongANorth,
            TubeBEast = SanseongBEast,
            TubeBNorth = SanseongBNorth,
            WestRoadElevation = 88.2,
            EastRoadElevation = 57.6,
            ReportedGradePercent = 0.0,
            ExcavationStart = new DateTime(2013, 8, 1),
            ExcavationStartKnown = false,
            Breakthrough = new DateTime(2016, 10, 13),
            MiddleAccessReady = new DateTime(2013, 8, 1),
            Opening = new DateTime(2018, 9, 30),
            MiddleAccessFraction = 0.0,
            ReportedDailyAdvance = 0.0,
            ReportedMuck = 0.0,
            ReportedCover = "",
            Sources = "포스코 뉴스룸 2016-10-13 (4.87 km, NATM, 공사 2013.8 ~ 2018.9) / 선형: OpenStreetMap (ODbL)",
        };

        /// <summary>관통일을 모를 때 쓰는 막장 평균 굴진 (m/일): 인제양양터널 계산(지보 패턴 5종, 상하반 분할)의 막장 평균 (README)</summary>
        public const double StandardFaceAdvance = 3.86;

        /// <summary>관통일 가정: 착공 + 6개월 + 60일 + 올림(길이 ÷ 2 ÷ 3.86)</summary>
        public static DateTime AssumedBreakthrough(DateTime constructionStart, double length) =>
            constructionStart.AddMonths(StandardSchedules.TunnelPrepMonths).AddDays(StandardSchedules.PortalDays + Math.Ceiling(length / 2.0 / StandardFaceAdvance - 1e-9));

        // ── 롯데건설 청계산3터널 · HDC 미사터널 (2026-11-27, 남은 회사 구조물) ─────────────

        public const string CheonggyeFile = "lotte_anyang_seongnam.asc";
        private static readonly double[] CheonggyeAEast = {201558.2, 201964.1, 202134.9, 202241.8, 202412.9, 202536.1, 202672.0, 202847.9, 203049.0, 203346.7, 203632.5, 203875.7, 204136.1, 204296.5};
        private static readonly double[] CheonggyeANorth = {533285.2, 533360.6, 533379.8, 533386.0, 533391.0, 533386.6, 533377.4, 533356.8, 533319.4, 533252.6, 533198.4, 533160.0, 533127.3, 533110.0};
        private static readonly double[] CheonggyeBEast = {201590.9, 202005.0, 202133.6, 202243.5, 202403.7, 202526.3, 202668.1, 202838.4, 203038.7, 203443.1, 203872.6, 204112.7, 204293.3};
        private static readonly double[] CheonggyeBNorth = {533255.8, 533331.2, 533344.9, 533351.7, 533355.3, 533353.9, 533343.2, 533323.2, 533286.4, 533195.4, 533124.8, 533095.5, 533074.5};

        /// <summary>
        /// 롯데건설 제2경인고속도로(안양~성남) 청계산3터널. 롯데건설이 사업을 착공 (경기일보 2012-06-03 "지난 1일 착공", 총 21.32 km, 터널 4개소),
        /// 개통 2017-09-27 (OpenStreetMap start_date — 위키 일부는 2019로 적어 확인 필요). 이 현장 지형 안에서 가장 긴 터널이다.
        /// **공개되지 않은 것:** 터널 공식 길이·굴착 시작일·관통일·막장 구성 → 길이는 OSM 두 튜브 평균, 관통일은 가정(AssumedBreakthrough), 막장은 양 끝 2개.
        /// 평면 선형: OpenStreetMap 제2경인고속도로 tunnel:name=청계산3터널 두 튜브 (2,770 m / 2,734 m). 입구 노면 서 183.7 m, 동 199.8 m (인제와 같은 방법).
        /// </summary>
        public static readonly TunnelSpec Cheonggye = new TunnelSpec
        {
            FileName = CheonggyeFile,
            Company = "롯데건설",
            Name = "청계산3터널",
            Consortium = "제이경인연결고속도로(주), 롯데건설 착공 (공구·지분 미확인)",
            OfficialLength = 2752.0,
            Tubes = 2,
            LanesPerTube = 2,
            TubeAEast = CheonggyeAEast,
            TubeANorth = CheonggyeANorth,
            TubeBEast = CheonggyeBEast,
            TubeBNorth = CheonggyeBNorth,
            WestRoadElevation = 183.7,
            EastRoadElevation = 199.8,
            ReportedGradePercent = 0.0,
            ExcavationStart = new DateTime(2012, 6, 1),
            ExcavationStartKnown = false,
            Breakthrough = AssumedBreakthrough(new DateTime(2012, 6, 1), 2752.0),
            BreakthroughKnown = false,
            MiddleAccessReady = new DateTime(2012, 6, 1),
            Opening = new DateTime(2017, 9, 27),
            MiddleAccessFraction = 0.0,
            ReportedCover = "",
            Sources = "경기일보 2012-06-03 (안양~성남 착공, 롯데건설) / 개통 2017-09-27: OpenStreetMap start_date / 길이·선형: OpenStreetMap (ODbL)",
        };

        public const string MisaFile = "hdc_seoul_chuncheon.asc";
        private static readonly double[] MisaAEast = {248411.8, 248453.7, 248543.9, 248671.3, 248820.5, 248932.9, 249106.0, 249251.9, 250232.5};
        private static readonly double[] MisaANorth = {567810.8, 567874.0, 567982.0, 568116.2, 568241.4, 568319.7, 568421.8, 568491.4, 568912.5};
        private static readonly double[] MisaBEast = {248386.9, 248438.2, 248530.1, 248661.5, 248812.5, 249014.5, 249153.4, 249710.0, 250228.5};
        private static readonly double[] MisaBNorth = {567830.3, 567890.7, 567997.6, 568126.1, 568250.2, 568384.6, 568459.8, 568704.0, 568948.4};

        /// <summary>
        /// HDC현대산업개발 서울~춘천 고속도로 미사터널 (현대산업개발 토목 실적 "서울-춘천고속도로 미사터널 외 2,200 NATM").
        /// 착공 2004.8 (머니투데이 2009-07-02, 학술 논문), 개통 2009-07-15 (Korea Times). 현대산업개발이 최초 제안자 (2001.9).
        /// **공개되지 않은 것:** 굴착 시작일·관통일·막장 구성 → 관통일은 가정(AssumedBreakthrough), 막장은 양 끝 2개.
        /// 평면 선형: OpenStreetMap 서울양양고속도로 tunnel:name=미사터널 두 튜브 (2,163 m / 2,181 m, OSM length 태그 2,193 / 2,205 m). 입구 노면 서 139.2 m, 동 116.7 m.
        /// </summary>
        public static readonly TunnelSpec Misa = new TunnelSpec
        {
            FileName = MisaFile,
            Company = "HDC현대산업개발",
            Name = "미사터널",
            Consortium = "서울춘천고속도로(주) 민간투자사업, 현대산업개발 제안·시공 실적 (공구 범위 미확인)",
            OfficialLength = 2200.0,
            Tubes = 2,
            LanesPerTube = 2,
            TubeAEast = MisaAEast,
            TubeANorth = MisaANorth,
            TubeBEast = MisaBEast,
            TubeBNorth = MisaBNorth,
            WestRoadElevation = 139.2,
            EastRoadElevation = 116.7,
            ReportedGradePercent = 0.0,
            ExcavationStart = new DateTime(2004, 8, 1),
            ExcavationStartKnown = false,
            Breakthrough = AssumedBreakthrough(new DateTime(2004, 8, 1), 2200.0),
            BreakthroughKnown = false,
            MiddleAccessReady = new DateTime(2004, 8, 1),
            Opening = new DateTime(2009, 7, 15),
            MiddleAccessFraction = 0.0,
            ReportedCover = "",
            Sources = "HDC현대산업개발 토목 실적 (미사터널 2,200 m NATM) / 착공 2004.8: 머니투데이 2009-07-02 / 개통 2009-07-15: Korea Times / 선형: OpenStreetMap (ODbL)",
        };

        public static readonly TunnelSpec[] Tunnels = { Inje, Sanseong, Cheonggye, Misa };

        /// <summary>지형 파일 이름으로 그 현장의 터널을 찾는다 (없으면 null)</summary>
        public static TunnelSpec FindTunnel(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            foreach (TunnelSpec t in Tunnels)
            {
                if (string.Equals(t.FileName, fileName, StringComparison.OrdinalIgnoreCase)) return t;
            }
            return null;
        }

    }
}
