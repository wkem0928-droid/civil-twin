using UnityEngine;

namespace CivilTwin.Runtime
{
    /// <summary>
    /// 키·마우스 입력을 받아도 되는지. 평소에는 창이 선택되어 있을 때만 받는다 (다른 창에서 친 키가 들어오지 않도록).
    /// 자동 시연(ScenarioRunner) 중에는 창 선택과 관계없이 받는다 — 시연용 가상 키보드만 쓰고 실제 키보드는 꺼 두기 때문이다.
    /// </summary>
    public static class InputGate
    {
        public static bool Allowed => Application.isFocused || ScenarioRunner.IsRunning;
    }
}
