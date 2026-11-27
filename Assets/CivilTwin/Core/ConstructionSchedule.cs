using System;
using System.Collections.Generic;

namespace CivilTwin.Core
{
    /// <summary>댐 공사의 작업 하나: 실제 시작·끝 날짜(자료), 선행 작업, 출처, 설명</summary>
    public sealed class ScheduleWork
    {
        public string Name;
        public DateTime Start, Finish;
        /// <summary>선행 작업 번호 (끝 → 시작). 자기보다 앞 번호만.</summary>
        public int[] Predecessors = new int[0];
        /// <summary>날짜의 출처 (예: "[K] 1968.3 착수, 1969.10 통수")</summary>
        public string Source = "";
        /// <summary>날짜 일부가 가정이면 true (예: 끝나는 날을 다음 작업 시작으로 둠)</summary>
        public bool Assumed;
        public string Note = "";
        /// <summary>무엇을 하는가 / 왜 하는가 / 주요 장비 (학습용 설명, 일반적인 댐 시공 지식)</summary>
        public string What = "", Why = "", Equipment = "";

        public double Days => (Finish - Start).TotalDays;
    }

    /// <summary>구조물 종류 — 단계 화면의 그림과 보고서의 종류별 절을 고른다</summary>
    public enum StructureKind { Dam, Tunnel, Bridge }

    /// <summary>단계 화면에서 한 단계씩 넘겨 볼 날짜 (제목·설명)</summary>
    public sealed class ConstructionStep
    {
        public DateTime Date;
        public string Title = "", Text = "";
    }

    /// <summary>
    /// 구조물 시공 공정 — 모든 회사 구조물이 같이 쓰는 틀 (2026-11-27 일반화, 처음은 2026-11-22 소양강댐 댐 시공 공정).
    /// 구조물마다 다른 것은 자료(작업·사건·단계·출처)뿐이고, 공정표·단계 화면·보고서는 이 틀 하나로 그린다.
    ///
    /// 처음 만든 때 (2026-11-22, 개발자 의견: "댐이 차례차례 어떻게 만들어지는지", "네트워크 공정표", "실무에서 쓸 법하게").
    ///
    /// 공개 자료의 실제 날짜로 작업 기간을 정하고, 공사 논리(유수전환 → 기초 → 성토 …)로 선후행을 이어 CPM을 계산한다.
    /// 그러면 두 가지 공정을 비교할 수 있다:
    ///   ① 논리 공정 (CPM): 각 작업이 선행 작업이 끝나자마자 시작한다고 볼 때의 날짜·여유·주공정
    ///   ② 실제 공정: 자료의 날짜
    ///   → 실제로 늦게 시작한 작업(여유를 다 써 버린 작업)이 무엇이고, 무엇이 준공일을 정했는지 설명할 수 있다.
    /// 한계: 관계는 끝 → 시작만 (겹치는 작업은 앞 작업이 끝난 날을 경계로 나눠 가정), 자원·날씨·설계 승인 같은 외부 제약은 없다.
    /// </summary>
    public sealed class ConstructionSchedule
    {
        public string Title;
        /// <summary>짧은 이름 (보고서 파일 이름, 화면 제목): 예 "소양강댐"</summary>
        public string Name = "";
        /// <summary>현장 지형 파일 이름 (ScheduleCatalog.Find가 채운다)</summary>
        public string SiteFile = "";
        public StructureKind Kind = StructureKind.Dam;
        /// <summary>단계 화면에서 넘겨 볼 단계 (날짜 순)</summary>
        public readonly List<ConstructionStep> Steps = new List<ConstructionStep>();
        /// <summary>공정표를 처음 열 때 고르는 작업 번호 (가장 보여 주고 싶은 작업)</summary>
        public int FocusWork;
        public DateTime ProjectStart;
        /// <summary>공식 준공일</summary>
        public DateTime Completion;
        public readonly List<ScheduleWork> Works = new List<ScheduleWork>();
        /// <summary>날짜만 있는 사건 (작업 기간이 아닌 것): (날짜, 내용, 출처)</summary>
        public readonly List<(DateTime date, string text, string source)> Milestones = new List<(DateTime, string, string)>();
        public string Sources = "";
        /// <summary>출처 뒤에 붙일 설명 (예: "[K]는 검색 결과 요약으로만 확인했다")</summary>
        public string SourceNote = "";
        /// <summary>보고서 "다루지 않은 것"에 더할 말</summary>
        public string Notes = "";

        /// <summary>착공일부터 센 날 수</summary>
        public double Day(DateTime date) => (date - ProjectStart).TotalDays;

        /// <summary>논리 공정 (CPM). extraDays[i]만큼 작업 i를 늘려 "이 작업이 늦으면?"을 볼 수 있다.</summary>
        public CpmResult Logic(double[] extraDays = null)
        {
            var list = new List<CpmActivity>();
            for (int i = 0; i < Works.Count; i++)
            {
                double d = Works[i].Days + (extraDays != null && i < extraDays.Length ? extraDays[i] : 0.0);
                list.Add(new CpmActivity(Works[i].Name, d, Works[i].Predecessors));
            }
            return Cpm.Compute(list);
        }

        /// <summary>실제로 마지막에 끝난 작업 번호 (그 작업이 실제 공사의 끝을 정했다)</summary>
        public int AsBuiltLast
        {
            get
            {
                int last = 0;
                for (int i = 1; i < Works.Count; i++) if (Works[i].Finish > Works[last].Finish) last = i;
                return last;
            }
        }

        /// <summary>작업 i가 논리상 가장 늦게 시작해도 되는 날(LS)보다 실제로 며칠 늦게 시작했나 (양수면 여유를 넘겨 늦음)</summary>
        public double LateBeyondFloat(CpmResult logic, int i) => Day(Works[i].Start) - logic.LateStart[i];
    }

    /// <summary>회사 구조물(과 랜드마크)의 시공 공정 자료 — 현장 지형 파일 하나에 공정 자료 하나</summary>
    public static partial class ScheduleCatalog
    {
        private static readonly Dictionary<string, Func<ConstructionSchedule>> BySite =
            new Dictionary<string, Func<ConstructionSchedule>>(StringComparer.OrdinalIgnoreCase)
            {
                { SiteStructures.SoyanggangFile, Soyanggang },
                { SiteStructures.BuhangFile, () => StandardSchedules.Dam(SiteStructures.Buhang, true,
                    "착공 2006.11 (달만 공개, 날짜는 1일로 가정)·준공 2013.11.20: SiteStructures.Buhang 자료 (K-water 2009 감사결과보고서, 보도)") },
                { TunnelStructures.InjeFile, () => StandardSchedules.Tunnel(TunnelStructures.Inje) },
                { TunnelStructures.SanseongFile, () => StandardSchedules.Tunnel(TunnelStructures.Sanseong,
                    new DateTime(2013, 8, 1), "착공 2013.8 (포스코 뉴스룸 2016-10-13, 날짜는 1일로 가정)", excavationStartUnknown: true) },
                { TunnelStructures.CheonggyeFile, () => StandardSchedules.Tunnel(TunnelStructures.Cheonggye,
                    new DateTime(2012, 6, 1), "착공 2012.6.1 (경기일보 2012-06-03)", excavationStartUnknown: true) },
                { TunnelStructures.MisaFile, () => StandardSchedules.Tunnel(TunnelStructures.Misa,
                    new DateTime(2004, 8, 1), "착공 2004.8 (머니투데이 2009-07-02, 날짜는 1일로 가정)", excavationStartUnknown: true) },
                { PumpedStorageStructures.CheongsongFile, () => StandardSchedules.PumpedStorage(PumpedStorageStructures.Cheongsong) },
                { PumpedStorageStructures.YecheonFile, () => StandardSchedules.PumpedStorage(PumpedStorageStructures.Yecheon) },
                { BridgeStructures.IncheonFile, () => StandardSchedules.Bridge(BridgeStructures.Incheon) },
            };

        /// <summary>현장 지형 파일 이름으로 시공 공정 자료를 찾는다 (없으면 null)</summary>
        public static ConstructionSchedule Find(string fileName)
        {
            if (fileName == null || !BySite.TryGetValue(fileName, out Func<ConstructionSchedule> make)) return null;
            ConstructionSchedule s = make();
            s.SiteFile = fileName;
            return s;
        }

        /// <summary>공정 자료가 있는 현장 파일 이름들</summary>
        public static IEnumerable<string> SiteFiles => BySite.Keys;

        /// <summary>구조물 종류별 보고서 부분 (없으면 null = 공통 절만). 존 단면 댐(소양강댐)은 화면 쪽에서 만든다.</summary>
        public static StructureReportPart ReportPart(ConstructionSchedule s)
        {
            if (s == null) return null;
            BridgeSpec b = s.Kind == StructureKind.Bridge ? BridgeStructures.Find(s.SiteFile) : null;
            if (b != null) return new BridgeReportPart(b, s);
            PumpedStoragePlant p = PumpedStorageStructures.Find(s.SiteFile);
            return p != null ? new PumpedStorageReportPart(p) : null;
        }

        /// <summary>
        /// 현대건설 소양강댐 (1967.4.15 착공 ~ 1973.10.15 준공). 출처:
        ///   [K] K-water연구원 「소양강댐이 국가 및 지역에 미치는 사회 경제적 편익산정에 대한 연구」(2013) 연표 — 검색 결과 요약으로 확인, 원문 미열람
        ///   [D] 대한경제 「건설로 읽는 현대사 (20)」 2023-06-30 / [T] 티티씨뉴스 「소양강댐 준공 50주년 ③」 2023-10-16 / [E] 한국민족문화대백과사전
        /// 날짜가 "월"까지만 있으면 그 달 1일로 둔다. 끝나는 날이 자료에 없으면 다음 작업의 시작일로 둔다 (Assumed = true).
        /// </summary>
        public static ConstructionSchedule Soyanggang()
        {
            var s = new ConstructionSchedule
            {
                Title = "현대건설 소양강댐 (중앙차수벽형 사력댐)",
                Name = "소양강댐",
                Kind = StructureKind.Dam,
                FocusWork = 5,   // 본댐 성토
                ProjectStart = new DateTime(1967, 4, 15),
                Completion = new DateTime(1973, 10, 15),
                Sources = "[K] K-water연구원 보고서(2013) 연표, [D] 대한경제 2023-06-30, [T] 티티씨뉴스 2023-10-16, [E] 한국민족문화대백과사전",
                SourceNote = "[K]는 검색 결과 요약으로만 확인했다 (원문 미열람).",
                Notes = "발전소 설비는 준공 뒤 1973.11.1에 끝났다 (자료).",
            };
            s.Works.Add(new ScheduleWork   // 0
            {
                Name = "준비공사", Start = new DateTime(1967, 4, 15), Finish = new DateTime(1968, 3, 1), Assumed = true,
                Source = "[T] 1967.4.15 착수, [D] 1차연도: 가배수로 일부·가설사무소·진입도로. 끝 = 제1호 가배수터널 착수(가정)",
                What = "진입도로, 가설 사무소·숙소, 전기·물 공급, 장비 반입로를 만든다.",
                Why = "깊은 산골짜기라 사람과 장비가 들어갈 길과 시설부터 있어야 한다.",
                Equipment = "불도저, 굴착기, 덤프트럭",
            });
            s.Works.Add(new ScheduleWork   // 1
            {
                Name = "제1호 가배수터널", Start = new DateTime(1968, 3, 1), Finish = new DateTime(1969, 10, 1), Predecessors = new[] { 0 },
                Source = "[K] 1968.3 착수, 1969.10 통수",
                What = "강 옆 산을 뚫어 강물이 댐 자리를 비켜 흐를 터널을 만든다.",
                Why = "강바닥(하상)에 댐을 쌓으려면 그 자리를 말려야 한다. 물길을 먼저 돌린다.",
                Equipment = "착암기(천공), 화약, 로더, 덤프트럭, 콘크리트 라이닝",
            });
            s.Works.Add(new ScheduleWork   // 2
            {
                Name = "유수전환·가물막이", Start = new DateTime(1969, 10, 1), Finish = new DateTime(1969, 10, 24), Predecessors = new[] { 1 }, Assumed = true,
                Source = "[K] 1969.10 가배수터널 통수·가물막이 체절. 끝 = 기초굴착 시작(가정)",
                What = "상류에 임시 둑(가물막이)을 막아 강물을 가배수터널로 보낸다.",
                Why = "댐 자리를 마른 작업장으로 만든다.",
                Equipment = "덤프트럭(사석 투입), 불도저",
            });
            s.Works.Add(new ScheduleWork   // 3
            {
                Name = "본댐 기초굴착", Start = new DateTime(1969, 10, 24), Finish = new DateTime(1970, 1, 1), Predecessors = new[] { 2 }, Assumed = true,
                Source = "[D][T] 1969.10.24 시작. 끝 = 그라우팅 시작(가정)",
                What = "강바닥의 모래·자갈·풍화암을 걷어 내고 단단한 암반을 드러낸다.",
                Why = "댐(특히 점토 코어)은 물이 새지 않는 단단한 기초 위에 앉아야 한다.",
                Equipment = "굴착기, 불도저(리퍼), 덤프트럭, 착암기",
            });
            s.Works.Add(new ScheduleWork   // 4
            {
                Name = "기초처리 (그라우팅)", Start = new DateTime(1970, 1, 1), Finish = new DateTime(1970, 5, 1), Predecessors = new[] { 3 }, Assumed = true,
                Source = "[T] 1970.1 본댐 그라우팅 사진. 순서 기초굴착 → 기초처리 → 성토 [T]. 끝 = 성토 시작(가정)",
                What = "암반에 구멍을 뚫고 시멘트 물(그라우트)을 넣어 틈을 메운다 (코어 아래 차수 커튼).",
                Why = "암반 틈으로 저수지 물이 새면 댐이 위험하다.",
                Equipment = "보링기, 그라우트 펌프·믹서",
            });
            s.Works.Add(new ScheduleWork   // 5
            {
                Name = "본댐 성토", Start = new DateTime(1970, 5, 1), Finish = new DateTime(1972, 11, 10), Predecessors = new[] { 4 },
                Source = "[T] 1970.5.1 착공 ~ 1972.11.10 완공",
                What = "가운데 점토 코어, 그 양옆 필터, 바깥 사력(자갈·암석)을 층층이 펴고 다진다.",
                Why = "점토 코어가 물을 막고, 필터가 코어 흙이 씻겨 나가지 않게 하고, 무거운 사력이 전체를 버틴다.",
                Equipment = "덤프트럭 13.5 t 127대·32 t 20대 등 205대 [T], 불도저, 롤러",
            });
            s.Works.Add(new ScheduleWork   // 6
            {
                Name = "제2호 가배수터널", Start = new DateTime(1969, 6, 1), Finish = new DateTime(1970, 6, 1), Predecessors = new[] { 0 }, Assumed = true,
                Source = "[K] 1970.6 통수. 시작일은 자료 없음(가정 1년 전)",
                What = "두 번째 물길 터널. 홍수 때 물을 더 많이 돌린다.",
                Why = "제2호 개통 전 홍수로 본댐이 위험했던 일이 있었다 [T].",
                Equipment = "착암기, 로더, 덤프트럭",
            });
            s.Works.Add(new ScheduleWork   // 7
            {
                Name = "상류 가물막이 (코퍼댐)", Start = new DateTime(1970, 3, 1), Finish = new DateTime(1970, 10, 1), Predecessors = new[] { 2 },
                Source = "[T] 1970.3 코어 다짐, 1970.10 체절 (사진 설명)",
                What = "상류에 높은 임시 댐을 쌓아 공사장을 홍수로부터 지킨다.",
                Why = "본댐이 높아지는 동안 큰 물이 와도 작업장이 잠기지 않게.",
                Equipment = "덤프트럭, 불도저, 롤러",
            });
            s.Works.Add(new ScheduleWork   // 8
            {
                Name = "여수로", Start = new DateTime(1969, 10, 15), Finish = new DateTime(1973, 6, 30), Predecessors = new[] { 0 },
                Source = "[D] 1969.10 중순 굴착 시작 ~ 1973.6 말 (45개월)",
                What = "저수지가 넘치기 전에 물을 안전하게 흘려보낼 수로와 수문을 만든다.",
                Why = "흙·돌로 된 사력댐은 물이 넘치면(월류) 무너진다. 여수로는 필수.",
                Equipment = "착암기, 굴착기, 덤프트럭, 콘크리트 장비",
            });
            s.Works.Add(new ScheduleWork   // 9
            {
                Name = "발전소", Start = new DateTime(1970, 4, 15), Finish = new DateTime(1973, 11, 1), Predecessors = new[] { 0 },
                Source = "[D][T] 1970.4 중순 착공 ~ 1973.11.1 설비 완료",
                What = "수압관(펜스톡)과 발전기를 설치한다.",
                Why = "다목적댐: 홍수 조절·용수 공급과 함께 전기를 만든다.",
                Equipment = "굴착기, 크레인, 콘크리트 장비",
            });

            s.Milestones.Add((new DateTime(1968, 8, 1), "사력댐으로 설계 변경", "[D]"));
            s.Milestones.Add((new DateTime(1971, 11, 1), "제1호 터널 폐쇄", "[K]"));
            s.Milestones.Add((new DateTime(1972, 2, 1), "담수 개시", "[T] 사진 설명"));
            s.Milestones.Add((new DateTime(1972, 8, 19), "8·19 홍수 — 댐이 홍수를 조절 (유입 7,640 t/s, 방류 1,640 t/s)", "[D]"));
            s.Milestones.Add((new DateTime(1972, 11, 10), "성토 완료·제2호 터널 폐쇄", "[K][T]"));
            s.Milestones.Add((new DateTime(1973, 10, 15), "준공", "[D][E]"));
            s.Steps.AddRange(DamZoneSection.SoyanggangSteps());
            return s;
        }
    }
}
