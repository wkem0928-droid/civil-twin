using System;
using System.Collections.Generic;

namespace CivilTwin.Core
{
    /// <summary>필댐의 재료 구역(존)</summary>
    public enum DamZone
    {
        /// <summary>가운데 점토 코어 — 물을 막는다</summary>
        Core,
        /// <summary>코어 양옆 필터(전이대 포함) — 코어 흙이 씻겨 나가지 않게</summary>
        Filter,
        /// <summary>바깥 사력(자갈·암석) — 무게로 전체를 버틴다</summary>
        Shell,
    }

    /// <summary>
    /// 중앙차수벽형 사력댐의 존 단면과 날짜별 성토 높이 (2026-11-22, 댐 시공 단계 시뮬레이터 3단계).
    ///
    /// 단면 (d = 댐 축에서 떨어진 거리, 상류 쪽 +, z = 표고):
    ///   상류면  d = 마루폭/2 + (마루 − z) × 상류 구배,   하류면  d = −(마루폭/2 + (마루 − z) × 하류 구배)
    ///   코어 반폭  = 코어 윗면 폭/2 + (코어 윗면 − z) × 코어 구배   (z ≤ 코어 윗면)
    ///   필터 = 코어 양옆으로 가로 폭 FilterWidth씩,  사력 = 나머지
    ///
    /// 계곡 (3D 체적용): 표고 z에서 댐 길이 L(z)가 기초의 바닥 폭 Lb에서 마루 길이까지 직선으로 넓어진다고 본다.
    ///   체적 V = ∫ (그 표고의 단면 폭) × L(z) dz.  Lb는 공식 체적과 같아지도록 푼다 (소양강댐: 960만 m³ → Lb ≈ 156 m).
    ///   → 계곡은 아래가 좁지만 단면은 아래가 훨씬 넓다 (저폭 540 m ↔ 마루폭 11 m). 그래서 체적의 63 %가 높이 아래 절반에 있고,
    ///     같은 하루 성토량이라도 처음에는 높이가 천천히 오르고 위로 갈수록 빨리 오른다.
    ///
    /// 날짜별 성토: 성토 시작 ~ 마루 도달일(CrestDate) 동안 하루 성토량이 일정하다고 본다 (가정). 코어·필터·사력은 같은 높이로 함께 올린다
    ///   (중앙 코어형 필댐은 존을 같은 층으로 펴고 다지는 것이 일반적). 마루 도달일 ~ 성토 완료(FillEnd)는 마무리 기간(높이 그대로).
    ///   마루 도달일을 주지 않으면 성토 완료일과 같다.
    ///   소양강댐은 두 기록이 맞지 않아(2026-11-26) 둘 다 계산한다: 본댐 완공 1972.11.10 [D][T] ↔ 같은 기사 [D]의
    ///   "8·19 홍수(1972.8.19) 당시 이미 123 m 완성". 높이로는 26 m 차이지만 위쪽은 단면이 좁아 체적으로는 9 %, 하루 성토량으로는 10 % 차이다.
    ///
    /// 한계: 존 치수(코어 윗면 폭·구배, 필터 폭)는 공개 자료에서 찾지 못한 **가정**이다. 사면 보호(리프랩)·소단·마루 도로는 넣지 않았다.
    /// 계산은 순수 C# (테스트 DamZoneSectionTests).
    /// </summary>
    public sealed class DamZoneSection
    {
        public double CrestElevation { get; }
        public double FoundationElevation { get; }
        public double CrestWidth { get; }
        public double UpstreamSlope { get; }
        public double DownstreamSlope { get; }
        public double CoreTopElevation { get; }
        public double CoreTopWidth { get; }
        /// <summary>코어 양쪽 면의 구배 1:n 의 n (높이 1 m 내려갈 때 한쪽으로 넓어지는 폭)</summary>
        public double CoreSlope { get; }
        /// <summary>필터 한쪽의 가로 폭 (m)</summary>
        public double FilterWidth { get; }
        public double CrestLength { get; }
        /// <summary>기초 표고에서 댐 길이 (계곡 바닥 폭, 체적에 맞춰 푼 값)</summary>
        public double ValleyBottomWidth { get; }

        public DateTime FillStart { get; }
        public DateTime FillEnd { get; }

        /// <summary>마루 높이에 닿은 날 (FillStart 초과 ~ FillEnd). 이날까지 일정 속도로 쌓고, 그 뒤 FillEnd까지는 마무리.</summary>
        public DateTime CrestDate { get; }

        /// <summary>마루 도달 뒤 성토 완료까지 마무리 기간이 있으면 true</summary>
        public bool HasFinishing => CrestDate < FillEnd;

        private readonly double _officialVolume;

        public DamZoneSection(double crestElevation, double foundationElevation, double crestWidth,
            double upstreamSlope, double downstreamSlope,
            double coreTopElevation, double coreTopWidth, double coreSlope, double filterWidth,
            double crestLength, double officialVolume, DateTime fillStart, DateTime fillEnd, DateTime? crestDate = null)
        {
            if (!(foundationElevation < crestElevation)) throw new ArgumentOutOfRangeException(nameof(foundationElevation));
            if (!(crestWidth >= 0) || !(upstreamSlope > 0) || !(downstreamSlope > 0)) throw new ArgumentOutOfRangeException(nameof(upstreamSlope));
            if (!(coreTopElevation > foundationElevation && coreTopElevation <= crestElevation)) throw new ArgumentOutOfRangeException(nameof(coreTopElevation));
            if (!(coreTopWidth > 0) || !(coreSlope >= 0) || !(filterWidth >= 0)) throw new ArgumentOutOfRangeException(nameof(coreTopWidth));
            if (!(crestLength > 0) || !(officialVolume > 0)) throw new ArgumentOutOfRangeException(nameof(crestLength));
            if (!(fillEnd > fillStart)) throw new ArgumentOutOfRangeException(nameof(fillEnd));
            DateTime crest = crestDate ?? fillEnd;
            if (!(crest > fillStart && crest <= fillEnd)) throw new ArgumentOutOfRangeException(nameof(crestDate), "마루 도달일은 성토 시작 뒤, 성토 완료 이전이어야 한다.");

            CrestElevation = crestElevation;
            FoundationElevation = foundationElevation;
            CrestWidth = crestWidth;
            UpstreamSlope = upstreamSlope;
            DownstreamSlope = downstreamSlope;
            CoreTopElevation = coreTopElevation;
            CoreTopWidth = coreTopWidth;
            CoreSlope = coreSlope;
            FilterWidth = filterWidth;
            CrestLength = crestLength;
            FillStart = fillStart;
            FillEnd = fillEnd;
            CrestDate = crest;
            _officialVolume = officialVolume;

            // 코어 + 필터가 사면 밖으로 나가면 단면이 성립하지 않는다 (기초와 코어 윗면에서 확인 — 둘 다 직선이라 그 사이도 안쪽)
            foreach (double z in new[] { foundationElevation, coreTopElevation })
            {
                double inner = CoreHalfWidth(z) + filterWidth;
                if (inner > UpstreamFace(z) || inner > DownstreamFace(z))
                    throw new ArgumentOutOfRangeException(nameof(filterWidth), "코어와 필터가 댐 사면 밖으로 나간다.");
            }

            // V(Lb)는 Lb에 대해 직선 → 두 값으로 풀기
            ValleyBottomWidth = 0.0;
            double v0 = VolumeWith(0.0, CrestElevation, null);
            double v1 = VolumeWith(1.0, CrestElevation, null);
            double lb = (officialVolume - v0) / (v1 - v0);
            if (!(lb > 0) || lb > crestLength) throw new ArgumentOutOfRangeException(nameof(officialVolume), "체적에 맞는 계곡 바닥 폭이 0 ~ 마루 길이 밖이다.");
            ValleyBottomWidth = lb;
        }

        public double Height => CrestElevation - FoundationElevation;

        /// <summary>같은 단면에 마루 도달일만 바꾼 것 (두 기록 비교용)</summary>
        public DamZoneSection WithCrestDate(DateTime crestDate) => new DamZoneSection(
            CrestElevation, FoundationElevation, CrestWidth, UpstreamSlope, DownstreamSlope,
            CoreTopElevation, CoreTopWidth, CoreSlope, FilterWidth, CrestLength, _officialVolume, FillStart, FillEnd, crestDate);

        /// <summary>쌓는 날 수 (성토 시작 ~ 마루 도달)</summary>
        public double FillDays => (CrestDate - FillStart).TotalDays;

        /// <summary>축에서 상류면까지 거리 (양수)</summary>
        public double UpstreamFace(double z) => CrestWidth / 2.0 + (CrestElevation - z) * UpstreamSlope;

        /// <summary>축에서 하류면까지 거리 (양수)</summary>
        public double DownstreamFace(double z) => CrestWidth / 2.0 + (CrestElevation - z) * DownstreamSlope;

        /// <summary>코어 반폭 (코어 윗면보다 높으면 0)</summary>
        public double CoreHalfWidth(double z) => z > CoreTopElevation ? 0.0 : CoreTopWidth / 2.0 + (CoreTopElevation - z) * CoreSlope;

        /// <summary>표고 z에서 그 존의 가로 폭 합 (양쪽 합)</summary>
        public double Width(DamZone zone, double z)
        {
            if (z < FoundationElevation || z > CrestElevation) return 0.0;
            double total = UpstreamFace(z) + DownstreamFace(z);
            bool belowCoreTop = z <= CoreTopElevation;
            double core = belowCoreTop ? 2.0 * CoreHalfWidth(z) : 0.0;
            double filter = belowCoreTop ? 2.0 * FilterWidth : 0.0;
            switch (zone)
            {
                case DamZone.Core: return core;
                case DamZone.Filter: return filter;
                default: return total - core - filter;
            }
        }

        /// <summary>단면 전체 폭</summary>
        public double TotalWidth(double z) => z < FoundationElevation || z > CrestElevation ? 0.0 : UpstreamFace(z) + DownstreamFace(z);

        /// <summary>표고 z에서 댐 길이 (계곡 폭)</summary>
        public double ValleyWidth(double z) => ValleyBottomWidth + (CrestLength - ValleyBottomWidth) * (z - FoundationElevation) / Height;

        /// <summary>단면적 (m², 댐 1 m 길이당). zone = null이면 전체.</summary>
        public double Area(DamZone? zone = null) => Integrate(z => zone.HasValue ? Width(zone.Value, z) : TotalWidth(z), FoundationElevation, CrestElevation);

        /// <summary>기초부터 표고 z까지 쌓은 체적 (m³). zone = null이면 전체.</summary>
        public double VolumeBelow(double z, DamZone? zone = null) => VolumeWith(ValleyBottomWidth, Math.Min(z, CrestElevation), zone);

        public double TotalVolume => VolumeBelow(CrestElevation);

        public double ZoneVolume(DamZone zone) => VolumeBelow(CrestElevation, zone);

        /// <summary>체적 v만큼 쌓았을 때의 높이 (표고). 이분법.</summary>
        public double ElevationForVolume(double v)
        {
            if (v <= 0) return FoundationElevation;
            if (v >= TotalVolume) return CrestElevation;
            double lo = FoundationElevation, hi = CrestElevation;
            for (int i = 0; i < 60; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (VolumeBelow(mid) < v) lo = mid; else hi = mid;
            }
            return 0.5 * (lo + hi);
        }

        /// <summary>성토 기간 중 하루 평균 성토량 (m³/일, 일정 속도 가정, 성토 시작 ~ 마루 도달)</summary>
        public double DailyVolume => TotalVolume / FillDays;

        /// <summary>그 날까지 쌓은 비율 0 ~ 1 (일정 속도 가정, 마루 도달일에 1)</summary>
        public double FillFraction(DateTime date)
        {
            double f = (date - FillStart).TotalDays / FillDays;
            return f < 0 ? 0 : f > 1 ? 1 : f;
        }

        /// <summary>그 날의 성토 높이 (표고). 성토 전이면 기초 표고.</summary>
        public double FillElevation(DateTime date) => ElevationForVolume(FillFraction(date) * TotalVolume);

        private double VolumeWith(double bottomWidth, double z, DamZone? zone)
        {
            double h = Height;
            Func<double, double> length = e => bottomWidth + (CrestLength - bottomWidth) * (e - FoundationElevation) / h;
            return Integrate(e => (zone.HasValue ? Width(zone.Value, e) : TotalWidth(e)) * length(e), FoundationElevation, z);
        }

        /// <summary>
        /// 적분: 코어 윗면에서 폭이 꺾이므로(코어·필터가 끝남) 그 앞뒤를 나눠, 각 구간을 2점 가우스 적분으로 계산한다.
        /// 각 구간 안에서 피적분 함수는 2차식(폭 1차 × 길이 1차)이라 2점 가우스가 정확하다 (3차까지 정확).
        /// 끝점을 계산하지 않으므로 꺾이는 점에서 값이 튀는 문제가 없다.
        /// </summary>
        private double Integrate(Func<double, double> f, double a, double b)
        {
            if (b <= a) return 0.0;
            if (a < CoreTopElevation && CoreTopElevation < b) return Gauss(f, a, CoreTopElevation) + Gauss(f, CoreTopElevation, b);
            return Gauss(f, a, b);
        }

        private static double Gauss(Func<double, double> f, double a, double b)
        {
            double m = 0.5 * (a + b), r = 0.5 * (b - a), g = r / Math.Sqrt(3.0);
            return r * (f(m - g) + f(m + g));
        }

        // ── 소양강댐 ───────────────────────────────────────────────

        /// <summary>8·19 홍수 (1972.8.19). 대한경제 [D]: "이때 이미 123 m의 댐이 완성된 상태" — 같은 기사의 본댐 완공 1972.11.10과 맞지 않는다.</summary>
        public static readonly DateTime SoyanggangFloodDate = new DateTime(1972, 8, 19);

        /// <summary>상시만수위 EL 193.5 m, 계획홍수위 EL 198.0 m (학술 논문 제원 표, koreascience — 검색 결과로 확인, 원문 표 미열람)</summary>
        public const double SoyanggangNormalWaterLevel = 193.5, SoyanggangFloodWaterLevel = 198.0;

        /// <summary>코어 윗면 = 마루 − 2 m: 계획홍수위(198)보다 3 m 높게 (가정). 코어 윗면 폭 6 m, 구배 1:0.25, 필터 한쪽 4 m (가정).</summary>
        public const double SoyanggangCoreTopBelowCrest = 2.0, SoyanggangCoreTopWidth = 6.0, SoyanggangCoreSlope = 0.25, SoyanggangFilterWidth = 4.0;

        /// <summary>
        /// 현대건설 소양강댐 존 단면.
        ///   확인한 값: 마루 EL 203 m, 높이 123 m, 길이 530 m, 체적 959만 m³, 사면 상류 1:2.3 · 하류 1:2.0 (SiteStructures 참고), 성토 1970.5.1 ~ 1972.11.10 [T]
        ///   추정: 마루폭 11 m (저폭에서 역산)   가정: 코어·필터 치수 (위 상수)
        /// crestByFlood = true: 기사 [D]대로 8·19 홍수 날 마루에 닿았다고 보고, 그 뒤 11.10까지는 마무리 (비교용 시나리오).
        /// </summary>
        public static DamZoneSection Soyanggang(bool crestByFlood = false) => new DamZoneSection(
            SiteStructures.SoyanggangCrest, SiteStructures.SoyanggangCrest - SiteStructures.SoyanggangHeight, 11.0, 2.3, 2.0,
            SiteStructures.SoyanggangCrest - SoyanggangCoreTopBelowCrest, SoyanggangCoreTopWidth, SoyanggangCoreSlope, SoyanggangFilterWidth,
            SiteStructures.SoyanggangLength, SiteStructures.SoyanggangOfficialVolume,
            new DateTime(1970, 5, 1), new DateTime(1972, 11, 10), crestByFlood ? SoyanggangFloodDate : (DateTime?)null);

        /// <summary>단면 화면에서 넘겨 볼 단계 (날짜는 ScheduleCatalog.Soyanggang의 작업·사건과 같다)</summary>
        public static List<ConstructionStep> SoyanggangSteps() => new List<ConstructionStep>
        {
            new ConstructionStep { Date = new DateTime(1967, 4, 15), Title = "착공 — 공사 전 계곡",
                Text = "강이 계곡 바닥을 흐른다. 먼저 진입도로·가설 시설을 만든다 (준비공사)." },
            new ConstructionStep { Date = new DateTime(1968, 3, 1), Title = "가배수터널 뚫기 시작",
                Text = "강물을 돌릴 제1호 가배수터널을 옆 산 속에 뚫는다 (이 단면 밖). 강은 아직 그대로 흐른다. 1968.8 콘크리트댐 → 사력댐 설계 변경." },
            new ConstructionStep { Date = new DateTime(1969, 10, 1), Title = "유수전환 — 강물을 터널로",
                Text = "가물막이로 강을 막아 물을 가배수터널로 보낸다. 댐 자리가 마른 작업장이 된다." },
            new ConstructionStep { Date = new DateTime(1969, 10, 24), Title = "본댐 기초굴착",
                Text = "강바닥의 모래·자갈·풍화암을 걷어 내 단단한 암반을 드러낸다. 특히 코어가 앉을 자리." },
            new ConstructionStep { Date = new DateTime(1970, 1, 1), Title = "기초처리 (그라우팅)",
                Text = "코어 아래 암반에 구멍을 뚫고 시멘트 물을 넣어 틈을 막는다 (차수 커튼). 혹한기에도 계속했다." },
            new ConstructionStep { Date = new DateTime(1970, 5, 1), Title = "본댐 성토 시작",
                Text = "코어(점토)·필터·사력을 같은 높이로 층층이 펴고 다진다. 아래쪽은 단면이 넓어(저폭 약 540 m) 한 층에 흙이 많이 든다." },
            new ConstructionStep { Date = new DateTime(1971, 5, 1), Title = "성토 1년째",
                Text = "1년 동안 체적의 약 40 %를 쌓았지만 높이는 3분의 1쯤. 위로 갈수록 단면이 좁아져 높이가 빨리 오른다." },
            new ConstructionStep { Date = new DateTime(1971, 11, 1), Title = "제1호 가배수터널 폐쇄",
                Text = "물은 제2호 가배수터널(1970.6 통수)로 흐른다." },
            new ConstructionStep { Date = new DateTime(1972, 2, 1), Title = "담수 개시",
                Text = "저수지에 물을 채우기 시작한다. 댐이 물을 막을 만큼 높아졌다." },
            new ConstructionStep { Date = new DateTime(1972, 8, 19), Title = "8·19 홍수",
                Text = "기사 [D]: \"이미 123 m의 댐이 완성된 상태\"로 홍수 조절 (유입 7,640 → 방류 1,640 t/s). 같은 기사의 본댐 완공은 1972.11.10 — 두 기록이 다르다." },
            new ConstructionStep { Date = new DateTime(1972, 11, 10), Title = "성토 완료",
                Text = "본댐 축조 완료, 제2호 가배수터널 폐쇄 [K][T]." },
            new ConstructionStep { Date = new DateTime(1973, 10, 15), Title = "준공",
                Text = "여수로(1973.6)·발전소(1973.11)까지 마치고 준공. 상시만수위 EL 193.5 m." },
        };
    }
}
