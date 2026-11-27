using CivilTwin.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CivilTwin.Tests
{
    /// <summary>
    /// 화면 버튼 목록 규칙 (2026-11-26 실제 마우스 버그의 재발 방지).
    /// 실제 마우스를 누르면 한 프레임 안의 순서가 이렇다:
    ///   [프레임 N] Repaint(버튼 그림) → [프레임 N+1] OnGUI 마우스 이벤트 → Update(누름 확인) → Layout → Repaint
    /// 자동 시연의 가상 마우스는 가운데의 "OnGUI 마우스 이벤트"가 없어서 이 버그를 잡지 못했다. 여기서 그 순서를 그대로 흉내 낸다.
    /// </summary>
    public class ClickTargetsTests
    {
        private static readonly Rect StepButton = new Rect(100f, 150f, 80f, 22f);
        private static readonly Vector2 Inside = new Vector2(140f, 160f), Outside = new Vector2(10f, 10f);

        /// <summary>한 번의 OnGUI 이벤트에서 버튼 하나를 그리는 것과 같은 호출</summary>
        private static void Draw(ClickTargets t, bool repaint, int frame, float time, System.Action action)
        {
            t.BeginEvent(repaint, frame);
            t.Add(StepButton, "4 기초굴착", action, repaint, time);
        }

        [Test]
        public void 실제_마우스_순서_마우스_이벤트가_먼저_와도_버튼이_눌린다()
        {
            var t = new ClickTargets();
            int clicked = 0;
            Draw(t, repaint: false, frame: 1, time: 0.00f, () => clicked++);   // Layout
            Draw(t, repaint: true, frame: 1, time: 0.00f, () => clicked++);    // Repaint → 버튼 1개
            Assert.AreEqual(1, t.Count);

            Draw(t, repaint: false, frame: 2, time: 0.02f, () => clicked++);   // 다음 프레임 앞쪽의 마우스 이벤트 (예전엔 여기서 비워졌다)
            Assert.AreEqual(1, t.Count, "마우스 이벤트에서 목록을 비우면 안 된다");

            Assert.IsTrue(t.TryClick(Inside, 0.02f));                          // ControlBar.Update의 누름 확인
            Assert.AreEqual(1, clicked);
        }

        [Test]
        public void 한_프레임에_여러_번_그려도_버튼은_한_번만_올라간다()
        {
            var t = new ClickTargets();
            for (int frame = 1; frame <= 3; frame++)
            {
                Draw(t, false, frame, frame * 0.02f, null);   // Layout
                Draw(t, true, frame, frame * 0.02f, null);    // Repaint
            }
            Assert.AreEqual(1, t.Count);
        }

        [Test]
        public void 버튼_밖을_누르면_아무것도_안_한다()
        {
            var t = new ClickTargets();
            int clicked = 0;
            Draw(t, true, 1, 0f, () => clicked++);
            Assert.IsFalse(t.TryClick(Outside, 0f));
            Assert.AreEqual(0, clicked);
        }

        [Test]
        public void 오래전에_그린_버튼은_눌리지_않는다()
        {
            // 글상자를 숨기면 그리기가 멈춘다. 그 뒤 예전 자리를 눌러도 실행되면 안 된다.
            var t = new ClickTargets();
            int clicked = 0;
            Draw(t, true, 1, 1.0f, () => clicked++);
            Assert.IsTrue(t.TryClick(Inside, 1.0f + ClickTargets.FreshSeconds - 0.01f));
            Assert.IsFalse(t.TryClick(Inside, 1.0f + ClickTargets.FreshSeconds + 0.05f));
            Assert.AreEqual(1, clicked);
        }

        [Test]
        public void 자동_시연은_글자로_n번째_버튼을_찾는다()
        {
            var t = new ClickTargets();
            t.BeginEvent(true, 1);
            t.Add(new Rect(0f, 0f, 70f, 20f), "접기 글상자", null, true, 0f);
            t.Add(new Rect(0f, 100f, 70f, 20f), "접기 대안", null, true, 0f);
            Assert.IsTrue(t.TryFind("접기", 1, 0f, out Vector2 c));
            Assert.AreEqual(110f, c.y, 1e-4);
            Assert.IsFalse(t.TryFind("펼치기", 0, 0f, out _));
        }
    }
}
