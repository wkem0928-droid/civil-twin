using System;
using System.Collections.Generic;

namespace CivilTwin.Core
{
    /// <summary>
    /// 표준 공정으로 시공 공정 자료를 만든다 (2026-11-27, 모든 회사 구조물 공통 틀).
    ///
    /// 소양강댐처럼 중간 날짜가 공개 자료에 남아 있는 구조물은 드물다. 다른 구조물은 착공·준공(개통)과 몇몇 날짜만 확인된다.
    /// 그래서 구조물 종류마다 교과서적인 작업 순서와 선후행을 두고, 기간은 이 순서로 채운다:
    ///   ① 자료의 날짜 (Assumed = false)
    ///   ② 계산 (예: 터널 2차 라이닝 = 길이 ÷ 거푸집 1 span 12 m/일, TunnelLining과 같은 규칙)
    ///   ③ 표준 비율 (예: 댐은 소양강댐 실제 공정의 작업별 기간 비율) — Assumed = true, 출처 칸에 근거를 적는다
    /// 무엇을·왜·장비 설명은 일반적인 시공 지식(교과서 수준)이다.
    /// </summary>
    public static partial class StandardSchedules
    {
        // ── 터널 (NATM) ─────────────────────────────────────────

        /// <summary>갱구부 공사 기간 (가정, 일): 갱문 비탈면 보강·갱구 굴착</summary>
        public const int PortalDays = 60;
        /// <summary>착공 ~ 갱구부 시작 준비공사 (가정, 개월): 착공일이 자료에 없을 때</summary>
        public const int TunnelPrepMonths = 6;

        /// <summary>2차 라이닝 기간 (일) = 작업대차 조립 4 + 올림(튜브 길이 ÷ 거푸집 수 ÷ span 길이) — 튜브들은 동시에, 거푸집은 구간을 나눠 하루 1 span</summary>
        public static double LiningDays(double tubeLength, int formsPerTube = 2, double spanLength = 12.0) =>
            TunnelLining.CarriageAssemblyDays + Math.Ceiling(tubeLength / formsPerTube / spanLength - 1e-9) * TunnelLining.DaysPerSpan;

        /// <summary>
        /// NATM 도로 터널 공정: 준비 → 갱구부 → (사갱) → 본선 굴착 → 관통 → 방수·2차 라이닝 → 포장·설비 → 개통.
        /// 굴착 시작·관통·사갱 막장 시작·개통은 자료(TunnelSpec), 라이닝은 계산, 준비·갱구부·포장설비 기간은 가정.
        /// </summary>
        /// <param name="projectStart">착공일 (자료에 없으면 null → 굴착 시작 − 갱구부 − 준비 6개월, 가정)</param>
        /// <param name="excavationStartUnknown">굴착 시작일이 자료에 없으면 true → 착공 + 준비 6개월 + 갱구부 60일 (가정)</param>
        public static ConstructionSchedule Tunnel(TunnelSpec spec, DateTime? projectStart = null, string startSource = null, bool excavationStartUnknown = false)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            if (excavationStartUnknown && !projectStart.HasValue) throw new ArgumentException("굴착 시작일을 모르면 착공일이 있어야 한다.");
            DateTime excavationStart = excavationStartUnknown
                ? projectStart.Value.AddMonths(TunnelPrepMonths).AddDays(PortalDays) : spec.ExcavationStart;
            DateTime portalStart = excavationStart.AddDays(-PortalDays);
            DateTime start = projectStart ?? portalStart.AddMonths(-TunnelPrepMonths);
            if (start > portalStart) start = portalStart;
            DateTime accessReady = spec.MiddleAccessReady < excavationStart ? excavationStart : spec.MiddleAccessReady;
            double lining = LiningDays(spec.OfficialLength);
            DateTime liningStart = spec.Breakthrough, liningEnd = spec.Breakthrough.AddDays(lining);
            if (liningEnd > spec.Opening) liningEnd = spec.Opening;

            var s = new ConstructionSchedule
            {
                Title = spec.Company + " " + spec.Name + " (NATM 도로터널)",
                Name = spec.Name,
                Kind = StructureKind.Tunnel,
                ProjectStart = start,
                Completion = spec.Opening,
                Sources = spec.Sources,
                SourceNote = "준비·갱구부·포장설비 기간과 착공일" + (projectStart.HasValue ? "을 뺀 날짜" : "") + "는 표준 공정 가정이다 (StandardSchedules).",
                Notes = "갱내 운반·환기 간섭, 인버트·배수·설비의 세부 공종은 넣지 않았다.",
            };
            s.Works.Add(new ScheduleWork   // 0
            {
                Name = "준비공사", Start = start, Finish = portalStart, Assumed = true,
                Source = (projectStart.HasValue ? (startSource ?? "착공일 자료") + ", " : "착공일 자료 없음 → 굴착 시작 − 8개월 (가정), ") + "끝 = 갱구부 시작 (가정)",
                What = "공사용 도로, 현장 사무소, 전기·용수, 화약고, 버력(굴착한 암석) 적치장을 마련한다.",
                Why = "터널 장비(점보드릴·로더·숏크리트 기계)와 화약을 들이고 버력을 내보낼 길과 시설이 먼저 있어야 한다.",
                Equipment = "불도저, 굴착기, 덤프트럭",
            });
            s.Works.Add(new ScheduleWork   // 1
            {
                Name = "갱구부 공사", Start = portalStart, Finish = excavationStart, Predecessors = new[] { 0 }, Assumed = true,
                Source = "표준 " + PortalDays + "일 (가정), 끝 = 본선 굴착 시작 (자료)",
                What = "터널 입구 비탈면을 깎고 보강(록볼트·숏크리트·강관다단)한 뒤 갱문 자리를 만든다.",
                Why = "입구는 흙과 풍화암이라 약하다. 무너지지 않게 보강해야 안쪽을 발파로 팔 수 있다.",
                Equipment = "굴착기, 천공기, 숏크리트 기계",
            });
            int access = -1;
            if (spec.HasMiddleAccess)
            {
                access = s.Works.Count;
                s.Works.Add(new ScheduleWork   // 2
                {
                    Name = "사갱 굴착", Start = excavationStart, Finish = accessReady, Predecessors = new[] { 0 },
                    Source = "본선 굴착 시작 ~ 사갱 막장 시작 (자료)",
                    What = "산 옆에서 비스듬히 본선까지 작업 터널(사갱)을 판다.",
                    Why = "긴 터널을 양 끝에서만 파면 오래 걸린다. 가운데에서도 양쪽으로 파 들어가면 막장이 늘어 공기가 준다.",
                    Equipment = "점보드릴, 로더, 덤프트럭, 숏크리트 기계",
                });
            }
            int main = s.Works.Count;
            s.Works.Add(new ScheduleWork
            {
                Name = "본선 굴착 (양 끝 막장)", Start = excavationStart, Finish = spec.Breakthrough, Predecessors = new[] { 1 }, Assumed = !spec.BreakthroughKnown,
                Source = (excavationStartUnknown ? "굴착 시작 = 착공 + 준비 6개월 + 갱구부 60일 (가정)" : "굴착 시작 (자료)")
                    + (spec.BreakthroughKnown ? " ~ 관통 (자료)" : " ~ 관통 = 굴착 시작 + 올림(" + spec.OfficialLength.ToString("N0") + " ÷ 2 ÷ " + TunnelStructures.StandardFaceAdvance.ToString("0.00") + " m/일) (가정, 인제양양 계산 막장 평균)"),
                What = "천공 → 장약·발파 → 환기 → 버력 처리 → 부석 제거 → 숏크리트·록볼트(·강지보) 순서의 1발파 싸이클을 되풀이한다.",
                Why = "NATM은 암반이 스스로 버티게 하고 숏크리트·록볼트로 돕는다. 암반이 나쁠수록 한 번에 짧게 판다 (지보 패턴).",
                Equipment = "점보드릴(3붐), 로더, 덤프트럭, 숏크리트 기계, 록볼트 천공기",
            });
            int fromAccess = -1;
            if (access >= 0)
            {
                fromAccess = s.Works.Count;
                s.Works.Add(new ScheduleWork
                {
                    Name = "본선 굴착 (사갱 막장)", Start = accessReady, Finish = spec.Breakthrough, Predecessors = new[] { access },
                    Source = "사갱 막장 시작 ~ 관통 (자료)",
                    What = "사갱이 본선에 닿은 자리에서 양쪽으로 본선을 판다.",
                    Why = "막장을 늘려 관통을 앞당긴다.",
                    Equipment = "점보드릴, 로더, 덤프트럭, 숏크리트 기계",
                });
            }
            int lin = s.Works.Count;
            s.Works.Add(new ScheduleWork
            {
                Name = "방수·2차 라이닝", Start = liningStart, Finish = liningEnd,
                Predecessors = fromAccess >= 0 ? new[] { main, fromAccess } : new[] { main }, Assumed = true,
                Source = "계산: 대차 조립 4일 + 올림(" + spec.OfficialLength.ToString("N0") + " m ÷ 거푸집 2조 ÷ 12 m) × 1일 = " + lining.ToString("N0")
                    + "일 (표준품셈 3-3-1·3-4-2, span 12 m 가정), 관통 뒤 시작 (가정)",
                What = "방수 시트를 붙이고 강재 거푸집을 옮겨 가며 콘크리트 라이닝을 친다.",
                Why = "숏크리트만으로는 물이 새고 겉면이 거칠다. 라이닝이 터널을 오래 쓰게 한다.",
                Equipment = "방수 작업대차, 강재 거푸집(이동식), 펌프카, 레미콘",
            });
            s.Works.Add(new ScheduleWork
            {
                Name = "포장·설비", Start = liningEnd, Finish = spec.Opening, Predecessors = new[] { lin }, Assumed = true,
                Source = "라이닝 끝 ~ 개통 (남은 기간, 가정)",
                What = "배수·포장, 환기(제트팬)·조명·소방·전기·통신 설비를 갖춘다.",
                Why = "차가 다니는 터널은 연기 배출·조명·비상 설비가 있어야 개통할 수 있다.",
                Equipment = "포장 장비(피니셔·롤러), 고소작업차",
            });
            s.FocusWork = main;

            s.Milestones.Add((excavationStart, "본선 굴착 시작", excavationStartUnknown ? "가정" : "자료"));
            if (access >= 0) s.Milestones.Add((accessReady, "사갱 막장 시작", "자료"));
            s.Milestones.Add((spec.Breakthrough, spec.BreakthroughKnown ? "관통" : "관통 (가정)", spec.BreakthroughKnown ? "자료" : "가정"));
            s.Milestones.Add((spec.Opening, "개통", "자료"));

            DateTime half = excavationStart.AddDays((spec.Breakthrough - excavationStart).TotalDays / 2.0);
            s.Steps.Add(new ConstructionStep { Date = start, Title = "착공 — 준비공사", Text = "공사용 도로·사무소·화약고·버력 적치장을 만든다." });
            s.Steps.Add(new ConstructionStep { Date = portalStart, Title = "갱구부 공사", Text = "입구 비탈면을 보강하고 갱문 자리를 만든다. 입구는 약한 지반이라 짧게 끊어 판다." });
            s.Steps.Add(new ConstructionStep { Date = excavationStart, Title = "본선 굴착 시작", Text = "양 끝 입구에서 발파 굴착을 시작한다 (1발파 싸이클을 되풀이)." });
            if (access >= 0)
                s.Steps.Add(new ConstructionStep { Date = accessReady, Title = "사갱 막장 시작", Text = "사갱이 본선에 닿아 가운데에서도 양쪽으로 판다 → 막장이 늘어난다." });
            s.Steps.Add(new ConstructionStep { Date = half, Title = "굴착 절반", Text = "굴착 기간의 절반. 막장이 깊어질수록 버력 운반 거리가 길어진다." });
            s.Steps.Add(new ConstructionStep { Date = spec.Breakthrough, Title = "관통", Text = "마주 파 오던 막장이 만난다. 이제 안쪽 마무리(라이닝)로 넘어간다." });
            s.Steps.Add(new ConstructionStep { Date = liningEnd, Title = "라이닝 완료", Text = "방수와 콘크리트 라이닝을 끝낸다 (계산 기간)." });
            s.Steps.Add(new ConstructionStep { Date = spec.Opening, Title = "개통", Text = "포장·설비를 마치고 차가 다닌다." });
            return s;
        }

        // ── 댐 ─────────────────────────────────────────────────

        /// <summary>
        /// 소양강댐 실제 공정(1967.4.15 ~ 1973.10.15, 2,375일)의 작업별 기간 비율. 다른 댐의 표준 공정에 쓴다 (가정).
        /// 준비 321 · 가배수터널 579 · 유수전환 23 · 기초굴착 69 · 기초처리 120 · 본댐 축조 924일, 나머지 339일 = 담수·마무리.
        /// 여수로는 914일째 ~ 2,268일째 (병행).
        /// </summary>
        public static readonly double[] DamPhaseShare = { 321 / 2375.0, 579 / 2375.0, 23 / 2375.0, 69 / 2375.0, 120 / 2375.0, 924 / 2375.0 };
        public const double DamSpillwayStartShare = 914 / 2375.0, DamSpillwayEndShare = 2268 / 2375.0;
        /// <summary>표면차수벽형(CFRD)에서 본댐 축조 기간 중 차수벽 콘크리트에 쓰는 몫 (가정)</summary>
        public const double FaceSlabShare = 0.2;

        /// <summary>
        /// 댐 표준 공정: 준비 → 가배수터널 → 유수전환·가물막이 → 기초굴착 → 기초처리 → 본댐 축조 → (표면차수벽) → 담수·마무리, 여수로 병행.
        /// 착공·준공은 자료, 가운데는 소양강댐 실제 비율(DamPhaseShare)로 나눈 가정이다.
        /// </summary>
        /// <param name="faceSlab">표면차수벽형 석괴댐(CFRD)이면 true: 축조 뒤 상류면 콘크리트 슬래브 작업을 둔다</param>
        public static ConstructionSchedule Dam(DamSpec spec, bool faceSlab, string dateSource)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            double total = (spec.End - spec.Start).TotalDays;
            DateTime At(double share) => spec.Start.AddDays(Math.Round(share * total));
            var edges = new List<DateTime> { spec.Start };
            double acc = 0;
            foreach (double f in DamPhaseShare) { acc += f; edges.Add(At(acc)); }
            DateTime fillStart = edges[5], fillEnd = edges[6];
            DateTime slabStart = faceSlab ? fillStart.AddDays(Math.Round((fillEnd - fillStart).TotalDays * (1.0 - FaceSlabShare))) : fillEnd;
            const string ratio = "소양강댐 실제 공정 비율로 나눈 가정";

            var s = new ConstructionSchedule
            {
                Title = spec.Company + " " + spec.Name + " (" + spec.TypeName + ")",
                Name = spec.Name,
                Kind = StructureKind.Dam,
                ProjectStart = spec.Start,
                Completion = spec.End,
                Sources = dateSource,
                SourceNote = "착공·준공 말고 가운데 날짜는 모두 " + ratio + "이다 (StandardSchedules.DamPhaseShare).",
                Notes = "체적·사면은 댐 쌓기 계산(부지 → 댐 쌓기)과 같은 자료.",
            };
            void Add(string name, DateTime a, DateTime b, int[] pred, string what, string why, string equip, bool assumed = true, string source = null) =>
                s.Works.Add(new ScheduleWork { Name = name, Start = a, Finish = b, Predecessors = pred, Assumed = assumed, Source = source ?? ratio,
                    What = what, Why = why, Equipment = equip });

            Add("준비공사", edges[0], edges[1], new int[0], "진입도로, 가설 사무소, 전기·용수, 이설도로를 만든다.",
                "골짜기에 사람과 장비가 들어갈 길과 시설부터 있어야 한다.", "불도저, 굴착기, 덤프트럭", true, "착공 (자료) ~ " + ratio);
            Add("가배수터널", edges[1], edges[2], new[] { 0 }, "강 옆 산을 뚫어 강물이 댐 자리를 비켜 흐를 터널을 만든다.",
                "댐 자리를 말리려면 물길을 먼저 돌려야 한다.", "착암기·점보드릴, 화약, 로더, 덤프트럭");
            Add("유수전환·가물막이", edges[2], edges[3], new[] { 1 }, "상류에 임시 둑(가물막이)을 쌓아 강물을 가배수터널로 보낸다.",
                "댐 자리를 마른 작업장으로 만든다.", "덤프트럭(사석 투입), 불도저");
            Add("기초굴착", edges[3], edges[4], new[] { 2 }, "강바닥의 모래·자갈·풍화암을 걷어 내고 단단한 암반을 드러낸다.",
                "댐은 물이 새지 않는 단단한 기초 위에 앉아야 한다." + (faceSlab ? " 표면차수벽형은 차수벽 끝(플린스)이 앉을 자리가 특히 중요하다." : ""),
                "굴착기, 불도저(리퍼), 덤프트럭, 착암기");
            Add("기초처리 (그라우팅)", edges[4], edges[5], new[] { 3 }, "암반에 구멍을 뚫고 시멘트 물을 넣어 틈을 메운다 (차수 커튼).",
                "암반 틈으로 저수지 물이 새면 댐이 위험하다.", "그라우팅 천공기, 그라우트 펌프·믹서");
            Add(faceSlab ? "본댐 축조 (석괴)" : "본댐 성토", edges[5], slabStart, new[] { 4 },
                faceSlab ? "돌(석괴·하상 사력)을 층층이 펴고 진동롤러로 다진다. 상류면 바로 뒤는 고운 재료(쿠션존)로 받친다."
                         : "코어(점토)·필터·사력을 같은 높이로 층층이 펴고 다진다.",
                faceSlab ? "표면차수벽형은 돌 몸체가 무게로 버티고, 물은 상류면 콘크리트 슬래브가 막는다." : "점토 코어가 물을 막고, 바깥 사력이 무게로 버틴다.",
                "덤프트럭, 불도저, 진동롤러, 굴착기");
            int last = 5;
            if (faceSlab)
            {
                Add("표면차수벽 (슬래브)", slabStart, fillEnd, new[] { 5 },
                    "상류 사면 위에 철근 콘크리트 슬래브를 슬립폼으로 아래에서 위로 이어 친다. 이음에는 지수판을 넣는다.",
                    "이 슬래브가 물을 막는 벽이다. 축조가 다 오르고 침하가 어느 정도 끝난 뒤 쳐야 금이 덜 간다.",
                    "슬립폼, 콘크리트 펌프, 윈치, 레미콘", true, ratio + " (축조 기간의 " + (FaceSlabShare * 100).ToString("0") + " %, 가정)");
                last = 6;
            }
            int spill = s.Works.Count;
            Add("여수로", At(DamSpillwayStartShare), At(DamSpillwayEndShare), new[] { 0 }, "홍수 때 넘치는 물을 안전하게 흘려보낼 콘크리트 수로와 수문을 만든다.",
                "물이 댐 마루를 넘으면 필댐은 무너진다. 담수 전에 갖춰야 한다.", "굴착기, 착암기, 콘크리트 펌프, 크레인");
            Add("담수·마무리", fillEnd, spec.End, new[] { last, spill }, "가배수터널을 막고 물을 채우며 마루 도로·사면 보호·계측기를 마무리한다.",
                "물을 채우며 누수·변형을 계측해 댐이 안전한지 확인한다.", "크레인, 소형 장비", true, ratio + " ~ 준공 (자료)");
            s.FocusWork = 5;

            s.Milestones.Add((spec.Start, "착공", "자료"));
            s.Milestones.Add((edges[3], "유수전환 (가정)", "표준 비율"));
            s.Milestones.Add((fillEnd, faceSlab ? "축조·차수벽 완료 (가정)" : "성토 완료 (가정)", "표준 비율"));
            s.Milestones.Add((spec.End, "준공", "자료"));

            DateTime mid = fillStart.AddDays((slabStart - fillStart).TotalDays / 2.0);
            s.Steps.Add(new ConstructionStep { Date = edges[0], Title = "착공 — 공사 전 계곡", Text = "강이 계곡 바닥을 흐른다. 진입도로·가설 시설부터 만든다." });
            s.Steps.Add(new ConstructionStep { Date = edges[1], Title = "가배수터널", Text = "강물을 돌릴 터널을 옆 산 속에 뚫는다." });
            s.Steps.Add(new ConstructionStep { Date = edges[2], Title = "유수전환", Text = "가물막이로 강을 막아 물을 터널로 보낸다. 댐 자리가 마른 작업장이 된다." });
            s.Steps.Add(new ConstructionStep { Date = edges[3], Title = "기초굴착", Text = "강바닥의 모래·자갈·풍화암을 걷어 내 암반을 드러낸다." });
            s.Steps.Add(new ConstructionStep { Date = edges[4], Title = "기초처리", Text = "암반에 그라우트를 넣어 물길을 막는다 (차수 커튼)." });
            s.Steps.Add(new ConstructionStep { Date = edges[5], Title = faceSlab ? "축조 시작" : "성토 시작", Text = faceSlab ? "돌을 층층이 펴고 진동롤러로 다진다." : "코어·필터·사력을 같은 높이로 쌓는다." });
            s.Steps.Add(new ConstructionStep { Date = mid, Title = faceSlab ? "축조 절반" : "성토 절반", Text = "아래쪽은 단면이 넓어 같은 양을 쌓아도 높이가 천천히 오른다." });
            if (faceSlab) s.Steps.Add(new ConstructionStep { Date = slabStart, Title = "표면차수벽", Text = "상류면에 콘크리트 슬래브를 아래에서 위로 이어 친다 (슬립폼)." });
            s.Steps.Add(new ConstructionStep { Date = fillEnd, Title = "담수 시작", Text = "가배수터널을 막고 물을 채우며 계측한다." });
            s.Steps.Add(new ConstructionStep { Date = spec.End, Title = "준공", Text = "여수로·마무리를 끝내고 준공." });
            return s;
        }
    }
}
