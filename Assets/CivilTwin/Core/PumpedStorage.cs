using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CivilTwin.Core
{
    /// <summary>양수발전소의 댐 하나 (공개 제원)</summary>
    public sealed class StorageDam
    {
        public string Name = "", TypeName = "";
        /// <summary>형식이 공개 자료로 확인되었는가 (아니면 그림은 표준 석괴댐 단면, 가정)</summary>
        public bool TypeKnown;
        public double Height, Length, Volume;
        /// <summary>댐 마루 표고 (모르면 NaN)</summary>
        public double CrestElevation = double.NaN;
        /// <summary>그림용 사면 구배와 마루폭 (가정: 표면차수벽형 석괴댐에서 흔히 쓰는 1:1.4, 마루폭 8 m)</summary>
        public double UpstreamSlope = 1.4, DownstreamSlope = 1.4, CrestWidth = 8.0;
    }

    /// <summary>
    /// 순양수식 양수발전소 (2026-11-27, 남은 회사 구조물: 삼성물산 청송양수, DL이앤씨 예천양수).
    /// 양수발전소는 댐 하나가 아니라 **상부댐 + 하부댐 + 수로터널(수직갱) + 지하 발전소 + 발전설비**가 함께 가는 공사다.
    /// </summary>
    public sealed class PumpedStoragePlant
    {
        public string FileName = "", Company = "", Name = "", Builders = "", Sources = "";
        public double CapacityMW;
        public DateTime Start, Completion;
        public StorageDam Upper, Lower;
        /// <summary>자료로 확인한 작업 날짜 (작업 이름 → 시작·끝). 나머지는 표준 비율(가정).</summary>
        public readonly Dictionary<string, (DateTime start, DateTime finish, string source)> KnownWorks = new Dictionary<string, (DateTime, DateTime, string)>();
        public readonly List<(DateTime date, string text, string source)> Events = new List<(DateTime, string, string)>();
    }

    public static class PumpedStorageStructures
    {
        public const string CheongsongFile = "samsung_cheongsong.asc", YecheonFile = "dlenc_yecheon.asc";

        /// <summary>
        /// 삼성물산(공동 시공) 청송양수발전소. 한수원 운영 발전소 소개: 상부댐 89.8 m · 400 m · 2,300천 m³, 하부댐 62 m · 300 m · 707천 m³,
        /// 300 MW × 2기, 준공 2006.12, 상업발전 1호기 2006.9. 착공 2000.9 (디지털청송문화대전·방문기). 댐 형식·마루 표고는 공개 자료에서 찾지 못함.
        /// </summary>
        public static readonly PumpedStoragePlant Cheongsong = MakeCheongsong();
        /// <summary>
        /// DL이앤씨(당시 대림산업, 삼환기업·풍림산업과 토건) 예천양수발전소. 한수원 사업추진현황: 상부댐 CFRD 73 m × 620 m · EL 711 · 축조 222만 m³,
        /// 하부댐 63 m × 535 m · EL 242 · 205만 m³, 공사 2004.11 ~ 2011.12, 800 MW. 하부댐: 2009.9 굴착 시작 ~ 2010.6 축조 완료, 2010.8 담수 (일렉트릭파워·세계일보 2010).
        /// </summary>
        public static readonly PumpedStoragePlant Yecheon = MakeYecheon();

        public static readonly PumpedStoragePlant[] Plants = { Cheongsong, Yecheon };

        public static PumpedStoragePlant Find(string fileName)
        {
            foreach (PumpedStoragePlant p in Plants) if (string.Equals(p.FileName, fileName, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        private static PumpedStoragePlant MakeCheongsong()
        {
            var p = new PumpedStoragePlant
            {
                FileName = CheongsongFile, Company = "삼성물산", Name = "청송양수발전소", Builders = "삼성물산 (공동 시공, 지분 미확인)",
                CapacityMW = 600, Start = new DateTime(2000, 9, 1), Completion = new DateTime(2006, 12, 1),
                Sources = "한국수력원자력 운영 발전소 소개 (댐 규모·용량·준공 2006.12) / 착공 2000.9: 디지털청송문화대전·방문기",
                Upper = new StorageDam { Name = "상부댐 (노래호)", TypeName = "형식 미확인", Height = 89.8, Length = 400, Volume = 2300000 },
                Lower = new StorageDam { Name = "하부댐 (청송호)", TypeName = "형식 미확인", Height = 62, Length = 300, Volume = 707000 },
            };
            p.Events.Add((new DateTime(2006, 9, 1), "1호기 상업발전", "한수원"));
            return p;
        }

        private static PumpedStoragePlant MakeYecheon()
        {
            var p = new PumpedStoragePlant
            {
                FileName = YecheonFile, Company = "DL이앤씨", Name = "예천양수발전소", Builders = "대림산업(주), 삼환기업(주), 풍림산업(주) (토건)",
                CapacityMW = 800, Start = new DateTime(2004, 11, 1), Completion = new DateTime(2011, 12, 1),
                Sources = "한국수력원자력 사업추진현황 (댐 형식·규모·마루 표고·축조량·공사기간·시공사) / 하부댐 일정: 일렉트릭파워·세계일보 2010-08",
                Upper = new StorageDam { Name = "상부댐", TypeName = "CFRD (콘크리트 표면차수벽형 석괴댐)", TypeKnown = true, Height = 73, Length = 620, Volume = 2220000, CrestElevation = 711 },
                Lower = new StorageDam { Name = "하부댐 (송월호)", TypeName = "CFRD (2007 보도, 한수원 표의 하부댐 칸은 비어 있음)", TypeKnown = true, Height = 63, Length = 535, Volume = 2050000, CrestElevation = 242 },
            };
            p.KnownWorks["하부댐"] = (new DateTime(2009, 9, 1), new DateTime(2010, 6, 30), "2009.9 굴착 시작 ~ 2010.6 축조 완료 (일렉트릭파워 2010-08)");
            p.Events.Add((new DateTime(2010, 8, 9), "하부댐 담수", "세계일보 2010-08-09"));
            return p;
        }
    }

    public static partial class StandardSchedules
    {
        /// <summary>
        /// 양수발전소 표준 공정의 작업별 (시작, 끝) — 전체 기간에 대한 비율 (가정, 출처 없음).
        /// 지하 발전소·수로가 가장 길고(공기를 정함), 댐 두 개는 중간에 함께, 설비는 발전소 굴착 뒤, 마지막에 담수·시운전.
        /// </summary>
        public static readonly Dictionary<string, (double a, double b)> PumpedStorageShare = new Dictionary<string, (double, double)>
        {
            { "준비공사·진입도로", (0.00, 0.12) },
            { "지하 발전소 굴착", (0.12, 0.45) },
            { "수로터널·수직갱", (0.12, 0.62) },
            { "상부댐", (0.30, 0.75) },
            { "하부댐", (0.30, 0.75) },
            { "발전설비 설치", (0.45, 0.88) },
            { "담수·시운전", (0.88, 1.00) },
        };

        public static ConstructionSchedule PumpedStorage(PumpedStoragePlant p)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            double total = (p.Completion - p.Start).TotalDays;
            DateTime At(double f) => p.Start.AddDays(Math.Round(f * total));
            var s = new ConstructionSchedule
            {
                Title = p.Company + " " + p.Name + " (순양수식, " + p.CapacityMW.ToString("N0", CultureInfo.InvariantCulture) + " MW)",
                Name = p.Name,
                Kind = StructureKind.Dam,
                ProjectStart = p.Start,
                Completion = p.Completion,
                Sources = p.Sources,
                SourceNote = "착공·준공" + (p.KnownWorks.Count > 0 ? "과 하부댐 날짜" : "") + "는 자료, 나머지 작업 기간은 표준 비율 가정이다 (StandardSchedules.PumpedStorageShare).",
                Notes = "상·하부댐의 유수전환·기초처리, 수압관로 강관 제작, 전기·제어 설비는 따로 나누지 않았다.",
            };
            string[] names = { "준비공사·진입도로", "지하 발전소 굴착", "수로터널·수직갱", "상부댐", "하부댐", "발전설비 설치", "담수·시운전" };
            int[][] preds = { new int[0], new[] { 0 }, new[] { 0 }, new[] { 0 }, new[] { 0 }, new[] { 1 }, new[] { 2, 3, 4, 5 } };
            string[][] text =
            {
                new[] { "산 위 상부댐과 산 아래 하부댐 자리까지 공사용 도로를 내고 가설 시설을 만든다.", "높이 차가 수백 m인 두 저수지를 오가야 한다.", "불도저, 굴착기, 덤프트럭" },
                new[] { "산 속에 수차·발전기가 들어갈 큰 동굴(지하 발전소)을 발파로 판다.", "물이 떨어지는 높이를 크게 하고 지상 공간을 줄이려고 땅속에 둔다.", "점보드릴, 로더, 덤프트럭, 록볼트·숏크리트" },
                new[] { "상부댐에서 발전소, 발전소에서 하부댐으로 물이 오가는 수로터널과 수직갱을 판다 (수압관로).", "밤에는 물을 퍼 올리고 낮에는 떨어뜨려 발전한다 — 이 길이 양수발전의 핵심이다.", "점보드릴, 레이즈보링(수직갱), 강관 라이닝" },
                new[] { "산 위 상부 저수지의 댐을 쌓는다 (석괴를 층층이 다지고 상류면에 차수벽).", "퍼 올린 물을 담아 두는 저수지다.", "덤프트럭, 불도저, 진동롤러, 슬립폼" },
                new[] { "하부 저수지의 댐: 물길을 돌리고 기초를 판 뒤 석괴를 쌓고 차수벽을 친다.", "발전에 쓴 물을 받았다가 다시 퍼 올린다.", "굴착기, 덤프트럭, 진동롤러, 슬립폼" },
                new[] { "펌프 겸 수차(가역식 펌프수차)와 발전전동기, 변압기를 설치한다.", "같은 기계가 밤에는 펌프, 낮에는 발전기로 돈다.", "천장 크레인, 대형 트레일러" },
                new[] { "두 저수지에 물을 채우고 양수·발전을 시운전한다.", "누수·변형을 계측하며 설비를 검사한다.", "계측 설비" },
            };
            for (int i = 0; i < names.Length; i++)
            {
                (double a, double b) = PumpedStorageShare[names[i]];
                bool known = p.KnownWorks.TryGetValue(names[i], out var k);
                s.Works.Add(new ScheduleWork
                {
                    Name = names[i], Start = known ? k.start : At(a), Finish = known ? k.finish : At(b), Predecessors = preds[i], Assumed = !known,
                    Source = known ? k.source + " (자료)" : "표준 비율 " + (a * 100).ToString("0") + " ~ " + (b * 100).ToString("0") + " % (가정)",
                    What = text[i][0], Why = text[i][1], Equipment = text[i][2],
                });
            }
            s.FocusWork = 2;
            s.Milestones.Add((p.Start, "착공", "자료"));
            foreach (var e in p.Events) s.Milestones.Add(e);
            s.Milestones.Add((p.Completion, "준공", "자료"));

            ScheduleWork cave = s.Works[1], up = s.Works[3], low = s.Works[4], eq = s.Works[5];
            s.Steps.Add(new ConstructionStep { Date = p.Start, Title = "착공 — 진입도로", Text = "산 위·아래 댐 자리와 지하 발전소 입구까지 길을 낸다." });
            s.Steps.Add(new ConstructionStep { Date = cave.Start, Title = "지하 발전소·수로", Text = "산 속에 발전소 동굴과 수로터널을 판다 (공기를 정하는 작업)." });
            s.Steps.Add(new ConstructionStep { Date = up.Start.AddDays(up.Days / 2), Title = "상부댐 축조", Text = "산 위 저수지의 댐이 오른다." });
            s.Steps.Add(new ConstructionStep { Date = low.Start.AddDays(low.Days / 2), Title = "하부댐 축조", Text = "아래 저수지의 댐이 오른다." });
            s.Steps.Add(new ConstructionStep { Date = eq.Start.AddDays(eq.Days / 2), Title = "발전설비 설치", Text = "지하 발전소에 펌프수차·발전전동기를 넣는다." });
            s.Steps.Add(new ConstructionStep { Date = s.Works[6].Start, Title = "담수·시운전", Text = "두 저수지에 물을 채우고 양수·발전을 시험한다." });
            s.Steps.Add(new ConstructionStep { Date = p.Completion, Title = "준공", Text = "상업 운전." });
            s.Steps.Sort((x, y) => x.Date.CompareTo(y.Date));
            return s;
        }
    }

    /// <summary>양수발전소 보고서 부분: 두 댐 제원표</summary>
    public sealed class PumpedStorageReportPart : StructureReportPart
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private readonly PumpedStoragePlant _p;
        public PumpedStorageReportPart(PumpedStoragePlant p) { _p = p; }

        public override void WriteSections(StringBuilder sb, ref int number)
        {
            sb.AppendLine("## " + number++ + ". 두 댐 (공개 제원)");
            sb.AppendLine();
            sb.AppendLine("| 댐 | 형식 | 높이 (m) | 길이 (m) | 체적·축조량 (만 m³) | 마루 표고 |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (StorageDam d in new[] { _p.Upper, _p.Lower })
                sb.AppendLine("| " + d.Name + " | " + d.TypeName + " | " + d.Height.ToString("0.#", Invariant) + " | " + d.Length.ToString("N0", Invariant) + " | "
                    + (d.Volume / 10000.0).ToString("0.#", Invariant) + " | " + (double.IsNaN(d.CrestElevation) ? "-" : "EL " + d.CrestElevation.ToString("0", Invariant)) + " |");
            sb.AppendLine();
            sb.AppendLine("시공: " + _p.Builders + ". 설비 " + _p.CapacityMW.ToString("N0", Invariant) + " MW. 양수발전은 남는 전기로 밤에 물을 퍼 올리고, 전기가 많이 필요한 낮에 떨어뜨려 발전한다.");
            sb.AppendLine();
        }

        public override IEnumerable<string> LimitLines
        {
            get
            {
                yield return "- **가정 (그림):** 두 댐 단면은 사면 1:1.4, 마루폭 8 m의 석괴댐으로 그렸다 (형식·구배 도면 없음)." + (_p.Upper.TypeKnown ? "" : " 이 발전소는 댐 형식도 공개 자료에서 찾지 못했다.");
            }
        }
    }
}
