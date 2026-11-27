using System;
using System.Collections.Generic;
using System.Globalization;

namespace CivilTwin.Core
{
    /// <summary>금액을 읽기 쉬운 한국식 단위로 적는다 (5-7).</summary>
    public static class KoreanMoney
    {
        /// <summary>
        /// 원 → "30억 5,350만 원", "6,210만 원", "0원". 만 원 아래는 반올림한다. 음수는 앞에 "−".
        /// 손계산 예: 3,053,500,000 → 30억 5,350만 원,  62,100,000 → 6,210만 원,  200,000,000 → 2억 원
        /// </summary>
        public static string Format(double won)
        {
            if (double.IsNaN(won)) return "-";
            string sign = won < 0 ? "−" : "";
            long man = (long)Math.Round(Math.Abs(won) / 10000.0, MidpointRounding.AwayFromZero);   // 만 원 단위
            if (man == 0) return "0원";

            long eok = man / 10000;
            long rest = man % 10000;
            var inv = CultureInfo.InvariantCulture;
            if (eok == 0) return sign + rest.ToString("N0", inv) + "만 원";
            if (rest == 0) return sign + eok.ToString("N0", inv) + "억 원";
            return sign + eok.ToString("N0", inv) + "억 " + rest.ToString("N0", inv) + "만 원";
        }
    }

    /// <summary>
    /// 계산 결과로 만드는 설명 문장 (5-7). **계산 엔진의 숫자만 풀어 말한다** — 새 판단을 더하지 않고,
    /// 어느 대안이 "최적"이라고 말하지 않는다. 화면과 보고서에서 "자동 문장"이라고 표시한다.
    /// </summary>
    public static class AlternativeNarrative
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        /// <summary>두 대안의 조건 차이: "덤프트럭 2대 → 4대" (여럿이면 쉼표로). 같으면 "조건이 같다".</summary>
        public static string ConditionChanges(AlternativeInput from, AlternativeInput to)
        {
            var parts = new List<string>();
            if (from.Machines != to.Machines) parts.Add("굴착기 " + from.Machines + "대 → " + to.Machines + "대");
            if (from.Trucks != to.Trucks) parts.Add("덤프트럭 " + from.Trucks + "대 → " + to.Trucks + "대");
            if (from.HoursPerDay != to.HoursPerDay)
            {
                parts.Add("하루 작업 " + from.HoursPerDay.ToString("0.#", Invariant) + "시간 → "
                    + to.HoursPerDay.ToString("0.#", Invariant) + "시간");
            }
            if (from.InterferenceRatio != to.InterferenceRatio)
            {
                parts.Add("간섭 가정 r " + from.InterferenceRatio.ToString("0.##", Invariant) + " → "
                    + to.InterferenceRatio.ToString("0.##", Invariant));
            }
            return parts.Count == 0 ? "조건이 같다" : string.Join(", ", parts);
        }

        /// <summary>
        /// 두 대안 비교 문장. 예:
        ///   "A → B (덤프트럭 2대 → 4대): 전체 공기는 56일 → 41일(15일 단축), 비교용 비용은 8,000만 원 → 6,210만 원(1,790만 원 감소)이다.
        ///    토공 공기를 정하는 쪽이 운반에서 굴착으로 바뀌었다."
        /// </summary>
        public static string Compare(AlternativeResult from, AlternativeResult to)
        {
            if (from == null) throw new ArgumentNullException(nameof(from));
            if (to == null) throw new ArgumentNullException(nameof(to));

            string changes = ConditionChanges(from.Input, to.Input);
            string head = Label(from) + " → " + Label(to) + " (" + changes + "): ";

            string sentence = head + "전체 공기는 " + from.ProjectDays + "일 → " + to.ProjectDays + "일("
                + DaysChange(to.ProjectDays - from.ProjectDays) + "), 비교용 비용은 "
                + KoreanMoney.Format(from.TotalCost) + " → " + KoreanMoney.Format(to.TotalCost) + "("
                + CostChange(to.TotalCost - from.TotalCost) + ")이다.";

            string reason = Reason(from, to);
            return reason.Length > 0 ? sentence + " " + reason : sentence;
        }

        /// <summary>메뉴처럼 좁은 곳에 쓰는 짧은 비교: "A → B: 공기 −15일, 비용 −1,790만 원"</summary>
        public static string Short(AlternativeResult from, AlternativeResult to)
        {
            int days = to.ProjectDays - from.ProjectDays;
            double cost = to.TotalCost - from.TotalCost;
            return Label(from) + " → " + Label(to) + ": 공기 " + (days > 0 ? "+" : (days < 0 ? "−" : "±")) + Math.Abs(days) + "일, 비용 "
                + (cost > 0.5 ? "+" + KoreanMoney.Format(cost) : (cost < -0.5 ? KoreanMoney.Format(cost) : "같음"));
        }

        /// <summary>
        /// 토공 계획 설명. 예: "굴착 332.5일, 운반 285.4일이라 굴착이 토공 공기 333일을 정한다.
        /// 운반이 굴착을 따라가려면 덤프트럭이 7대 필요하다(지금 8대)."
        /// </summary>
        public static string Describe(EarthworkPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            string excavation = plan.ExcavationDays.ToString("F1", Invariant);
            if (!plan.IncludesHaul)
            {
                return "덤프트럭을 0대로 두어 운반은 계산하지 않았다. 굴착 " + excavation + "일 → 토공 공기 " + plan.TotalDays + "일.";
            }

            string haul = plan.HaulDays.ToString("F1", Invariant);
            string who = plan.HaulIsBottleneck ? "운반" : "굴착";
            return "굴착 " + excavation + "일, 운반 " + haul + "일이라 " + who + "이 토공 공기 " + plan.TotalDays + "일을 정한다. "
                + "운반이 굴착을 따라가려면 덤프트럭이 " + plan.TrucksToKeepUp + "대 필요하다(지금 " + plan.Trucks + "대).";
        }

        // ---------- 도우미 ----------

        private static string Label(AlternativeResult r) => string.IsNullOrEmpty(r.Input.Name) ? "대안" : r.Input.Name;

        private static string DaysChange(int delta)
        {
            if (delta < 0) return (-delta) + "일 단축";
            if (delta > 0) return delta + "일 늘어남";
            return "같음";
        }

        private static string CostChange(double delta)
        {
            if (delta < -0.5) return KoreanMoney.Format(-delta) + " 감소";
            if (delta > 0.5) return KoreanMoney.Format(delta) + " 증가";
            return "같음";
        }

        /// <summary>왜 그렇게 되었는지: 계산 결과 안에 있는 사실만 (토공을 정하는 쪽, 주공정선의 시작).</summary>
        private static string Reason(AlternativeResult from, AlternativeResult to)
        {
            var reasons = new List<string>();

            if (from.Earthwork != null && to.Earthwork != null && from.Earthwork.IncludesHaul && to.Earthwork.IncludesHaul)
            {
                string a = from.Earthwork.HaulIsBottleneck ? "운반" : "굴착";
                string b = to.Earthwork.HaulIsBottleneck ? "운반" : "굴착";
                if (a != b) reasons.Add("토공 공기를 정하는 쪽이 " + a + "에서 " + b + "으로 바뀌었다.");
                else if (from.Input.Machines != to.Input.Machines && b == "운반")
                {
                    reasons.Add("토공은 여전히 운반이 정하므로 굴착기 대수를 바꾼 효과가 운반에 막힌다.");
                }
                else if (from.Input.Trucks != to.Input.Trucks && b == "굴착")
                {
                    reasons.Add("토공은 여전히 굴착이 정하므로 트럭 대수를 바꾼 효과가 굴착에 막힌다.");
                }
            }

            string startFrom = CriticalStart(from.Schedule);
            string startTo = CriticalStart(to.Schedule);
            if (startFrom != startTo)
            {
                if (startTo == "가설공사")
                {
                    reasons.Add("토공이 가설공사보다 짧아져 주공정선이 가설공사로 넘어갔다"
                        + (to.ProjectDays == from.ProjectDays ? "(그래서 전체 공기가 줄지 않았다)." : "."));
                }
                else if (startTo == "토공")
                {
                    reasons.Add("토공이 가설공사보다 길어져 주공정선이 토공에서 시작한다.");
                }
            }
            return string.Join(" ", reasons);
        }

        /// <summary>첫날 시작하는 두 작업(토공, 가설공사) 중 주공정인 쪽: "토공", "가설공사", "토공·가설"</summary>
        public static string CriticalStart(BuildingSchedule s)
        {
            if (s == null) return "토공";   // 건물이 없는 현장(댐): 공정이 토공 하나뿐이다
            bool earthwork = s.Result.Critical[s.IndexOf(BuildingWork.Earthwork)];
            bool temporary = s.Result.Critical[s.IndexOf(BuildingWork.TemporaryWorks)];
            if (earthwork && temporary) return "토공·가설";
            return earthwork ? "토공" : "가설공사";
        }
    }
}
