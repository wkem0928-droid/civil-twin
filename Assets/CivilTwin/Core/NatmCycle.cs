using System;
using System.Collections.Generic;

namespace CivilTwin.Core
{
    /// <summary>
    /// 터널 지보 패턴 하나 (2026-11-21). 한국도로공사 고속도로 터널의 표준 굴착·지보 패턴
    /// (박권제·설운호, 「신개념 고속도로 터널공법(Ex-TM) 소개」, 터널과 지하공간 기술기사 17권 4호, 표 1).
    /// </summary>
    public readonly struct SupportPattern
    {
        public readonly string Name;
        /// <summary>1발파 굴진장 (m). 상하반 분할 패턴은 상반 값</summary>
        public readonly double Advance;
        /// <summary>하반 1발파 굴진장 (m). 0이면 전단면 굴착. 표 1: P-4 "1.5/3.0", P-5 "1.2/1.2" (상반/하반)</summary>
        public readonly double BenchAdvance;
        /// <summary>숏크리트 두께 (m)</summary>
        public readonly double Shotcrete;
        /// <summary>강섬유보강 숏크리트면 true (와이어메시 시간 T4를 넣지 않는다 — 표준품셈 3-2-1 주 ③)</summary>
        public readonly bool SteelFiber;
        public readonly double BoltLength;
        /// <summary>록볼트 간격 (m, 종·횡 같음). 0이면 랜덤 배치</summary>
        public readonly double BoltSpacing;
        /// <summary>강지보가 있으면 true (지보 설치 시간이 든다)</summary>
        public readonly bool SteelRib;

        public SupportPattern(string name, double advance, double shotcrete, bool steelFiber,
            double boltLength, double boltSpacing, bool steelRib, double benchAdvance = 0.0)
        {
            if (!(benchAdvance >= 0)) throw new ArgumentOutOfRangeException(nameof(benchAdvance));
            if (!(advance > 0)) throw new ArgumentOutOfRangeException(nameof(advance));
            if (!(shotcrete >= 0)) throw new ArgumentOutOfRangeException(nameof(shotcrete));
            if (!(boltLength > 0)) throw new ArgumentOutOfRangeException(nameof(boltLength));
            if (!(boltSpacing >= 0)) throw new ArgumentOutOfRangeException(nameof(boltSpacing));
            Name = name ?? "";
            Advance = advance;
            Shotcrete = shotcrete;
            SteelFiber = steelFiber;
            BoltLength = boltLength;
            BoltSpacing = boltSpacing;
            SteelRib = steelRib;
            BenchAdvance = benchAdvance;
        }

        /// <summary>상하반 분할 굴착이면 true</summary>
        public bool IsBench => BenchAdvance > 0;

        // 표 1 (괄호 안의 P-3 강지보 "(50×20×30)"는 필요할 때만 → 넣지 않음, 가정)
        public static readonly SupportPattern P1 = new SupportPattern("P-1", 4.0, 0.05, false, 3.0, 0.0, false);
        public static readonly SupportPattern P2 = new SupportPattern("P-2", 4.0, 0.06, true, 3.0, 2.0, false);
        public static readonly SupportPattern P3 = new SupportPattern("P-3", 2.5, 0.09, true, 4.0, 1.5, false);
        public static readonly SupportPattern P4 = new SupportPattern("P-4", 1.5, 0.12, true, 4.0, 1.5, true, 3.0);
        public static readonly SupportPattern P5 = new SupportPattern("P-5", 1.2, 0.16, true, 4.0, 1.5, true, 1.2);

        /// <summary>P-1 ~ P-5 (번호 순)</summary>
        public static readonly SupportPattern[] Standard = { P1, P2, P3, P4, P5 };

        /// <summary>
        /// 실제 시공된 패턴 비율: 2014년 건설 중인 고속도로 터널 107개소 223 km (같은 글 그림 3 "시공 시").
        /// P-1 11.4 %, P-2 17.2 %, P-3 36.6 %, P-4 16.4 %, P-5 7.0 % + P-6 5.6 % + RP-1·2·3 5.8 % → P-5로 묶어 18.4 % (가정).
        /// 인제양양터널의 실제 지질 자료가 없어서 이 전국 비율을 대표로 쓴다.
        /// </summary>
        public static readonly double[] AsBuiltShare2014 = { 0.114, 0.172, 0.366, 0.164, 0.184 };
    }

    /// <summary>
    /// 2차로 도로터널 단면. 굴착단면적 90 m²는 한국도로공사 글(위)의 그림 1 "굴착단면적 ≒ 90.0 m²".
    /// 인제양양터널 보도 값으로 대조: 반출 토석 196만 m³ ÷ (튜브 2개 × 10,965 m) = 89.4 m².
    /// 둘레(아치 20 m, 측벽 8 m, 바닥 12 m)와 노면 위 천장 높이 7.5 m는 **가정**.
    /// 상하반 분할 굴착: 상반 = 굴착면적의 60 %, 아치 둘레 / 하반 = 40 %, 측벽 둘레 (**가정**. 55 ~ 65 %로 바꿔도 m당 시간은 0.1 % 안쪽으로 같다).
    /// 여굴 두께는 표준품셈 토목 3-1-2의 가운데 값: 아치 15.5 cm (12 ~ 19), 측벽 15 cm (12 ~ 18), 바닥 12.5 cm (10 ~ 15).
    /// </summary>
    public sealed class TunnelSection
    {
        public double Area = 90.0;
        public double ArchLength = 20.0;
        public double WallLength = 8.0;
        public double BottomWidth = 12.0;
        public double CrownAboveRoad = 7.5;
        public double OverbreakArch = 0.155, OverbreakWall = 0.15, OverbreakBottom = 0.125;
        public double UpperShare = 0.6;

        /// <summary>숏크리트·록볼트를 하는 둘레 = 아치 + 측벽 (m)</summary>
        public double LiningPerimeter => ArchLength + WallLength;

        /// <summary>여굴 면적 (m²) = Σ 둘레 × 여굴 두께. 손계산: 20 × 0.155 + 8 × 0.15 + 12 × 0.125 = 5.8</summary>
        public double OverbreakArea => ArchLength * OverbreakArch + WallLength * OverbreakWall + BottomWidth * OverbreakBottom;

        public static TunnelSection TwoLaneRoad() => new TunnelSection();
    }

    /// <summary>
    /// 막장 하나의 장비와 작업 조건. 출처 있는 값과 가정을 나눠 적는다.
    /// </summary>
    public sealed class NatmCrew
    {
        /// <summary>점보드릴 붐 수 — 표준품셈 토목 3-2-4 C군(2차로 이상) "점보드릴(3붐)"</summary>
        public int Booms = 3;
        /// <summary>막장 1 m²당 발파공 수 — **가정** (표준품셈은 단면 10 m² 미만 소형터널 표만 있다)</summary>
        public double HolesPerSquareMeter = 1.6;
        /// <summary>발파 효율 (1발파 진행 ÷ 천공 길이) — **가정**</summary>
        public double PullEfficiency = 0.9;

        /// <summary>로더 버킷 5.0 m³ — 3-2-4 C군 "로더 5.0 m³"</summary>
        public double LoaderBucket = 5.0;
        /// <summary>버킷계수 0.55 — 공통 8-2-5 "발파 또는 리퍼로 깎은 암괴"</summary>
        public double LoaderK = 0.55;
        /// <summary>작업효율 0.55 — 8-2-5 파쇄암 흐트러진 상태 "양호" (주 ①: 터널 내 암버력 적재 시)</summary>
        public double LoaderE = 0.55;
        /// <summary>
        /// 1회 사이클 시간 (초) = m × ℓ + t1 + t2 = 1.8 × 8 + 18 + 14 = 46.4 — 8-2-5 타이어식 m 1.8, 표준 편도 8 m,
        /// t1 = 산적상태에서 담을 때 "곤란한 경우" 18초 (발파암이라 곤란으로 봄, 가정), t2 = 14초
        /// </summary>
        public double LoaderCycleSeconds = 1.8 * 8.0 + 18.0 + 14.0;
        public int Loaders = 1;
        /// <summary>암 토량환산계수 L = 1.6 (공통 1장 보통암, Docs/ASSUMPTIONS.md와 같은 값)</summary>
        public double RockLoose = 1.6;

        /// <summary>뿜어붙이기 기계 능력 (m³/시간) — **가정**</summary>
        public double ShotcreteMachine = 18.0;
        /// <summary>뿜어붙이기 효율 E = 0.55 — 3-2-1 주 ⑪</summary>
        public double ShotcreteEfficiency = 0.55;
        /// <summary>반발 손실률 — **가정** 15 %</summary>
        public double ShotcreteLoss = 0.15;
        /// <summary>랜덤 록볼트(P-1)의 1발파당 개수 — **가정**</summary>
        public double RandomBolts = 4.0;
        /// <summary>용수 할증 (싸이클에 더하는 비율). 3-2-1 주 ⑦ "용수발생으로 굴착작업에 지장을 받는 경우 굴착 사이클을 30 %까지 증가". 기본 0</summary>
        public double WaterIncrease = 0.0;

        /// <summary>로더 한 대의 흐트러진 상태 작업량 (m³/시간) = 3600 · q · K · f · E / cm (f = 1). 손계산 117.35</summary>
        public double LoaderHourlyLoose =>
            3600.0 * LoaderBucket * LoaderK * 1.0 * LoaderE / LoaderCycleSeconds;
    }

    /// <summary>사이클 시간의 한 줄 (작업 이름, 분, 근거)</summary>
    public readonly struct CycleItem
    {
        public readonly string Group, Name, Source;
        public readonly double Minutes;

        public CycleItem(string group, string name, double minutes, string source)
        {
            Group = group;
            Name = name;
            Minutes = minutes;
            Source = source;
        }
    }

    /// <summary>
    /// NATM 1발파당 싸이클 시간 (2026-11-21). 표준품셈 [토목부문] 3-2-1 "터널굴착 1발파당 싸이클 시간" C군(2차로 이상)의
    /// 작업 목록을 그대로 쓴다. 표가 범위로 준 값은 가운데, "공사물량"(T1 ~ T6)은 아래 식으로 계산한다.
    ///
    ///   T1 천공       = 발파공 수 × 천공 길이 ÷ 천공속도 ÷ 붐 수      (천공속도: 3-2-3 점보드릴, 굴진장별 가운데)
    ///   T2 버력처리   = (단면적 + 여굴) × 굴진장 × L ÷ 로더 작업량       (8-2-5 로더)
    ///   T3 면정리     = 숏크리트 면적 ÷ 64 m²/시간                      (3-2-1 주 ⑨)
    ///   T4 와이어메시 = 0 (강섬유보강 숏크리트 — 주 ③. P-1 일반 숏크리트도 메시 없이 본다, 가정)
    ///   T5 뿜어붙이기 = 숏크리트 부피 ÷ (q × E × (1 − 손실률))          (주 ⑪)
    ///   T6 록볼트     = 개수 × (천공 + 공내청소 1 + 충진 2 + 정착 2분) ÷ 붐 수 (붐이 나눠 한다고 가정)
    ///   괄호로 표시된 C군 준비시간(숏크리트 타설준비, 록볼트 설치준비)은 동시작업이라 싸이클에서 뺀다 (주 ④).
    ///
    /// 하루 굴진 = 하루 작업 시간 × 60 ÷ 싸이클(분) × 굴진장.
    /// 손계산 (P-3, 굴진장 2.5 m): 고정 230분 + T1 133.33 + T2 195.93 + T3 65.63 + T5 44.92 + T6 93.33 = 763.14분
    ///   → 24시간이면 1,440 ÷ 763.14 = 1.887발파 × 2.5 m = 4.72 m/일.
    ///
    /// 상하반 분할 굴착 (P-4, P-5, 2026-11-21): 3-2-1 주 ⑤ "상하반 분할굴착시 하반의 경우 비고를 따른다" — 비고(하반) 열:
    ///   천공준비·측량 및 마킹·장약 및 발파·부석제거·지보설치·잔재제거 65 %, 환기·버력처리 준비·장비점검·이동 및 기타 100 %,
    ///   T1 ~ T6은 공사물량 (하반의 면적·둘레·굴진장으로 다시 계산).
    ///   상반 싸이클은 상반 면적·아치 둘레·아치 여굴, 하반 싸이클은 하반 면적·측벽 둘레·측벽과 바닥 여굴로 계산한다.
    ///   같은 장비·같은 조가 번갈아 한다고 보고: 1 m당 시간 = 상반 싸이클 ÷ 상반 굴진장 + 하반 싸이클 ÷ 하반 굴진장.
    ///   손계산 (P-5 하반, 1.2 m, 면적 36 m², 측벽 둘레 8 m): 고정 (30 + 17.5 + 55 + 45 + 42.5 + 20) × 0.65 + 27.5 + 10 + 10 + 15 = 199.0,
    ///     T1 = 1.6 × 36 × 133.3 cm ÷ 80 ÷ 3 = 32.0, T2 = (36 + 2.7) × 1.2 × 1.6 ÷ 117.35 × 60 = 38.0, T3 = 8 × 1.2 ÷ 64 × 60 = 9.0,
    ///     T5 = 8 × 1.2 × 0.16 ÷ 8.415 × 60 = 11.0, T6 = 4.27개 × 10분 ÷ 3 = 14.2 → 303.2분.
    ///   P-5: 상반 462.0분 ÷ 1.2 m + 하반 303.2분 ÷ 1.2 m = 637.6분/m → 24시간 2.26 m/일 (전단면으로 볼 때 3.05 m/일).
    /// 한계: 보조공법(포어폴링 등)·용수·장비 고장 없음. 상반과 하반을 다른 장비로 동시에 하는 경우는 다루지 않는다.
    /// </summary>
    public sealed class NatmCycle
    {
        public SupportPattern Pattern { get; }
        public IReadOnlyList<CycleItem> Items => _items;
        /// <summary>1발파 싸이클 (분). 분할 굴착이면 상반 싸이클</summary>
        public double TotalMinutes { get; }
        /// <summary>하반 1발파 싸이클 (분). 전단면이면 0</summary>
        public double BenchMinutes { get; }
        /// <summary>1발파 록볼트 개수 (분할 굴착이면 상반)</summary>
        public double BoltsPerRound { get; }
        /// <summary>하반 1발파 록볼트 개수</summary>
        public double BenchBoltsPerRound { get; }
        /// <summary>1 m 굴착에 드는 시간 (분/m)</summary>
        public double MinutesPerMeter => TotalMinutes / Pattern.Advance + (Pattern.IsBench ? BenchMinutes / Pattern.BenchAdvance : 0.0);
        /// <summary>1 m당 록볼트 (개/m)</summary>
        public double BoltsPerMeter => BoltsPerRound / Pattern.Advance + (Pattern.IsBench ? BenchBoltsPerRound / Pattern.BenchAdvance : 0.0);

        private readonly List<CycleItem> _items = new List<CycleItem>();

        public NatmCycle(SupportPattern pattern, TunnelSection section, NatmCrew crew)
        {
            if (section == null) throw new ArgumentNullException(nameof(section));
            if (crew == null) throw new ArgumentNullException(nameof(crew));
            if (crew.Booms < 1 || crew.Loaders < 1) throw new ArgumentOutOfRangeException(nameof(crew));
            Pattern = pattern;
            if (!pattern.IsBench)
            {
                TotalMinutes = AddStage("", pattern.Advance, section.Area, section.OverbreakArea, section.LiningPerimeter, pattern, crew, 1.0,
                    out double bolts);
                BoltsPerRound = bolts;
                return;
            }
            double upperArea = section.Area * section.UpperShare;
            TotalMinutes = AddStage("상반 ", pattern.Advance, upperArea, section.ArchLength * section.OverbreakArch, section.ArchLength,
                pattern, crew, 1.0, out double upperBolts);
            BoltsPerRound = upperBolts;
            BenchMinutes = AddStage("하반 ", pattern.BenchAdvance, section.Area - upperArea,
                section.WallLength * section.OverbreakWall + section.BottomWidth * section.OverbreakBottom, section.WallLength,
                pattern, crew, BenchFactor, out double benchBolts);
            BenchBoltsPerRound = benchBolts;
        }

        /// <summary>3-2-1 비고(하반): 고정 시간 중 65 %로 줄이는 작업의 비율</summary>
        public const double BenchFactor = 0.65;

        /// <summary>
        /// 싸이클 한 단계 (전단면, 상반 또는 하반)의 작업을 더하고 합(분)을 돌려준다.
        /// f65 = 비고(하반)에서 65 %인 작업에 곱하는 값 (전단면·상반 1.0, 하반 0.65). 100 %인 작업과 T1 ~ T6(공사물량)에는 곱하지 않는다.
        /// </summary>
        private double AddStage(string prefix, double adv, double area, double overbreak, double lining, SupportPattern pattern, NatmCrew crew,
            double f65, out double boltsPerRound)
        {
            int start = _items.Count;
            double speed = DrillSpeedCmPerMinute(adv);
            string pct = f65 < 1.0 ? " × 65 %" : "";

            Add(prefix + "착암", "천공준비 (내공측량·암판정)", 30.0 * f65, "3-2-1 C군" + pct);
            Add(prefix + "착암", "측량 및 마킹", 17.5 * f65, "3-2-1 C군 15 ~ 20" + pct);
            double holes = crew.HolesPerSquareMeter * area;
            Add(prefix + "착암", "천공 T1", holes * (adv / crew.PullEfficiency * 100.0) / speed / crew.Booms, "발파공 × 천공 길이 ÷ 3-2-3 속도 ÷ 붐");
            Add(prefix + "착암", "장약 및 발파", 55.0 * f65, "3-2-1 C군 50 ~ 60" + pct);
            Add(prefix + "착암", "환기", 27.5, "3-2-1 C군 25 ~ 30");

            Add(prefix + "버력처리", "버력처리 준비", 10.0, "3-2-1 C군");
            double loose = (area + overbreak) * adv * crew.RockLoose;
            Add(prefix + "버력처리", "버력처리 T2", loose / (crew.LoaderHourlyLoose * crew.Loaders) * 60.0, "흐트러진 버력 ÷ 8-2-5 로더 작업량");
            Add(prefix + "버력처리", "부석제거 및 뒷정리", 45.0 * f65, "3-2-1 C군 40 ~ 50" + pct);

            double face = lining * adv;
            Add(prefix + "숏크리트", "바닥청소 및 면정리 T3", face / 64.0 * 60.0, "3-2-1 주 ⑨ 64 m²/시간");
            if (pattern.SteelRib) Add(prefix + "숏크리트", "지보 설치 (강지보)", 42.5 * f65, "3-2-1 C군 40 ~ 45" + pct);
            double shot = crew.ShotcreteMachine * crew.ShotcreteEfficiency * (1.0 - crew.ShotcreteLoss);
            Add(prefix + "숏크리트", "뿜어붙이기 T5", face * pattern.Shotcrete / shot * 60.0, "부피 ÷ (q × 0.55 × (1 − 손실률))");
            Add(prefix + "숏크리트", "잔재 제거", 20.0 * f65, "3-2-1 C군" + pct);
            Add(prefix + "숏크리트", "장비 점검", 10.0, "3-2-1 C군");

            boltsPerRound = pattern.BoltSpacing > 0
                ? (lining / pattern.BoltSpacing) * (adv / pattern.BoltSpacing)
                : crew.RandomBolts;
            double perBolt = pattern.BoltLength * 100.0 / speed + 1.0 + 2.0 + 2.0;
            Add(prefix + "록볼트", "록볼트 T6 (천공·청소·충진·정착)", boltsPerRound * perBolt / crew.Booms, "개수 × (천공 + 1 + 2 + 2분) ÷ 붐");
            Add(prefix + "록볼트", "이동 및 기타", 15.0, "3-2-1 C군");

            double sum = 0.0;
            for (int i = start; i < _items.Count; i++) sum += _items[i].Minutes;
            if (crew.WaterIncrease > 0)
            {
                Add(prefix + "용수", "용수 할증 +" + (crew.WaterIncrease * 100.0).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " %",
                    sum * crew.WaterIncrease, "3-2-1 주 ⑦ (30 %까지)");
                sum *= 1.0 + crew.WaterIncrease;
            }
            return sum;
        }

        private void Add(string group, string name, double minutes, string source) =>
            _items.Add(new CycleItem(group, name, minutes, source));

        /// <summary>막장 하나의 하루 굴진 (m/일) = 하루 작업 분 ÷ 1 m당 시간 (전단면이면 = 하루 발파 횟수 × 굴진장)</summary>
        public double AdvancePerDay(double hoursPerDay)
        {
            if (!(hoursPerDay > 0) || hoursPerDay > 24) throw new ArgumentOutOfRangeException(nameof(hoursPerDay));
            return hoursPerDay * 60.0 / MinutesPerMeter;
        }

        /// <summary>
        /// 점보드릴 천공속도 (cm/분) — 표준품셈 토목 3-2-3, 굴진장별 범위의 가운데:
        /// 1.2 m 이하 80 (75 ~ 85), 2.0 이하 90 (85 ~ 95), 3.0 이하 100 (95 ~ 105), 3.0 초과 112.5 (105 ~ 120).
        /// </summary>
        public static double DrillSpeedCmPerMinute(double advance)
        {
            if (advance <= 1.2) return 80.0;
            if (advance <= 2.0) return 90.0;
            if (advance <= 3.0) return 100.0;
            return 112.5;
        }
    }
}
