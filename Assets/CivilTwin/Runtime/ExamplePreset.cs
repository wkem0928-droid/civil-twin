using CivilTwin.Core;

namespace CivilTwin.Runtime
{
    /// <summary>
    /// "예시 불러오기" (2026-11-20 전체 검토에서 추가). README의 대표 예시를 버튼 하나로 다시 만든다.
    /// 처음 보는 사람(처음 보는 사람, 영상)이 이 프로그램의 핵심 — 장비는 대수가 아니라 조합 — 을 한 번에 보게 하려는 것이다.
    ///
    ///   현장: 현대건설 소양강댐 (hyundai_soyanggang.asc), 부지 중심 동 271,770.91 · 북 594,528.24, 처음 크기
    ///   조건: 굴착기 4대, 하루 8시간, 간섭 없음, 암 없음 → 균형 계획고
    ///   대안: A = 트럭 4대,  B = 트럭 8대,  C = 굴착기 8대 + 트럭 8대.  끝나면 지금 = B 조건, 비교표를 띄운다.
    ///
    /// 현장 파일이 없으면(지형 파일은 저장소에 없다) 지금 현장의 화면 가운데에 같은 순서로 만든다 — 숫자는 README와 다르다.
    /// </summary>
    public static class ExamplePreset
    {
        public const string SiteFile = "hyundai_soyanggang.asc";
        public const double CenterEasting = 271770.91;
        public const double CenterNorthing = 594528.24;

        /// <summary>예시를 만든다. 무엇을 했는지 한 줄로 돌려준다 (메뉴에 표시).</summary>
        public static string Apply(SiteSwitcher sites, GradingPlanTool grading, AlternativeTool alternatives)
        {
            if (sites == null || grading == null || alternatives == null) return "예시를 만들 수 없음";

            bool exact = sites.ShowByFileName(SiteFile);
            if (grading.IsDamMode) grading.Clear();   // 댐 축조 중이면 먼저 끝낸다 (토량환산계수 등을 되돌린 뒤 예시 값을 넣도록)

            grading.machines = 4;
            grading.trucks = 4;
            grading.hoursPerDay = 8.0;
            grading.interferenceRatio = 1.0;
            grading.rockDepth = RockLine.None;

            if (exact) grading.PlaceAt(CenterEasting, CenterNorthing);
            else grading.PlaceAtScreenCenter();
            if (!grading.HasPad) return "예시: 부지를 놓지 못함 (화면 가운데가 지형 밖)";

            grading.SetBalancedElevation();
            alternatives.Save(0);            // A: 굴착기 4, 트럭 4
            grading.ChangeTrucks(+4);
            alternatives.Save(1);            // B: 굴착기 4, 트럭 8
            grading.ChangeMachines(+4);
            alternatives.Save(2);            // C: 굴착기 8, 트럭 8
            grading.ChangeMachines(-4);      // 지금 = B

            return exact
                ? "예시: 소양강댐, 균형 계획고, A 트럭 4 · B 트럭 8 · C 굴착기 8 (README와 같은 조건)"
                : "예시: 소양강댐 파일이 없어 지금 현장 가운데에 만듦 (README와 숫자가 다름)";
        }
    }
}
