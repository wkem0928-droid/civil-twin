using System;
using UnityEngine;

namespace CivilTwin.Runtime
{
    /// <summary>
    /// 오른쪽 정보 글상자 (부지·댐·터널, 2026-11-21 개발자 의견 "글상자 가독성, 사용자가 원하는 것만 보게").
    ///
    ///   - 위쪽 메뉴(오른쪽 위) 바로 아래, 화면 오른쪽에 붙인다.
    ///   - 폭은 화면 폭의 45 %(420 ~ 580 px)까지만 쓰고 긴 줄은 접어 쓴다 → 지형 가운데를 덮지 않는다.
    ///   - 오른쪽 위의 [접기 ▴] / [펼치기 ▾] 버튼: 접으면 제목 한 줄만 남는다 (HudMode.InfoCollapsed, 다음 실행에도 남음).
    /// 버튼은 ControlBar와 같은 방식(Input System 마우스)으로 눌린다: 여기서 Targets(ClickTargets)에 올리고 ControlBar.Update가 확인한다.
    /// </summary>
    public static class InfoBox
    {
        public const float MinWidth = 420f, MaxWidth = 580f, WidthShare = 0.45f, Margin = 14f;

        /// <summary>오른쪽 글상자가 차지할 수 있는 가장 큰 폭 (여백 포함) — 다른 상자가 이 왼쪽에 놓이게</summary>
        public static float ReservedWidth => Mathf.Clamp(HudMode.GuiWidth * WidthShare, MinWidth, MaxWidth) + Margin;

        private static GUIStyle _wrap, _small;

        /// <summary>
        /// 그린 글상자 버튼들 (ControlBar가 누름을 확인하고, 자동 시연이 이름으로 찾는다).
        /// 목록 규칙은 ClickTargets에 있다 — 2026-11-26 실제 마우스로 버튼이 하나도 안 눌리던 버그
        /// (Editor.log 누름 기록 15줄 모두 "버튼 0개")를 고친 규칙이고, 테스트 ClickTargetsTests가 지킨다.
        /// </summary>
        private static readonly ClickTargets Targets = new ClickTargets();

        private static bool IsRepaint => Event.current != null && Event.current.type == EventType.Repaint;

        private static void NewFrameCheck() => Targets.BeginEvent(IsRepaint, Time.frameCount);

        /// <summary>
        /// 글상자를 그리고 그 자리를 돌려준다. top = 위쪽 끝 (메뉴 아래). 접혀 있으면 첫 줄만.
        /// </summary>
        /// <param name="headline">접었을 때 제목 대신 보일 한 줄 (제목 + 핵심 숫자). null이면 첫 줄</param>
        public static Rect Draw(string text, float top, string headline = null)
        {
            if (_wrap == null)
            {
                _wrap = HudStyle.Panel(15, wordWrap: true);
                _small = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
                _small.normal.textColor = HudStyle.Ui(HudStyle.Accent);
            }
            NewFrameCheck();

            bool collapsed = HudMode.InfoCollapsed;
            string shown = collapsed ? (headline ?? FirstLine(text)) : text;
            float maxWidth = Mathf.Clamp(HudMode.GuiWidth * WidthShare, MinWidth, MaxWidth);
            var content = new GUIContent(shown);
            float natural = _wrap.CalcSize(content).x + 4f;
            // 제목 줄 오른쪽에 버튼(70 px) 자리를 둔다
            float title = _wrap.CalcSize(new GUIContent(collapsed ? shown : FirstLine(text))).x + 84f;
            float width = Mathf.Clamp(Mathf.Max(natural, title), 260f, maxWidth);
            float height = _wrap.CalcHeight(content, width) + 2f;
            var rect = new Rect(HudMode.GuiWidth - width - Margin, top, width, height);

            GUI.Box(rect, shown, _wrap);
            HudLayout.Register(rect);

            CollapseButton(rect, collapsed, HudMode.ToggleInfoCollapsed, "글상자");
            return rect;
        }

        /// <summary>
        /// 상자 오른쪽 위에 [접기 ▴] / [펼치기 ▾] 작은 버튼을 그리고 누를 수 있게 올린다 (정보 글상자·대안 비교표·건물 글상자 공통).
        /// 같은 화면에 여럿 있으면 자동 시연은 "접기 @2"처럼 순서로 찾는다 (그린 순서).
        /// </summary>
        public static void CollapseButton(Rect box, bool collapsed, Action toggle, string id = "")
        {
            if (_small == null)
            {
                _small = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
                _small.normal.textColor = HudStyle.Ui(HudStyle.Accent);
            }
            NewFrameCheck();
            var button = new Rect(box.xMax - 76f, box.y + 5f, 70f, 20f);
            HudStyle.DrawBox(button);
            GUI.Label(button, collapsed ? "펼치기 ▾" : "접기 ▴", _small);
            Targets.Add(button, (collapsed ? "펼치기" : "접기") + (id.Length > 0 ? " " + id : ""), toggle, IsRepaint, Time.unscaledTime);
            HudLayout.Register(button);
        }

        private static GUIStyle _buttonLabel;

        /// <summary>
        /// 일반 버튼 (공정표 화면 등): 상자 + 가운데 글, 누르면 action. ControlBar.Update가 누름을 확인하고 자동 시연은 글로 찾는다.
        /// </summary>
        public static void Button(Rect r, string text, Action action, bool selected = false)
        {
            if (_buttonLabel == null)
            {
                _buttonLabel = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            }
            NewFrameCheck();
            HudStyle.DrawBox(r);
            if (selected)   // 고른 버튼은 바탕도 칠한다 (글자색만 바뀌어 눌렸는지 알기 어려웠다 — 2026-11-26)
            {
                Color prev = GUI.color;
                GUI.color = HudStyle.Ui(new Color(HudStyle.Accent.r, HudStyle.Accent.g, HudStyle.Accent.b, 0.28f));
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = prev;
            }
            _buttonLabel.normal.textColor = HudStyle.Ui(selected ? HudStyle.Accent : HudStyle.Text);
            GUI.Label(r, text, _buttonLabel);
            Targets.Add(r, text, action, IsRepaint, Time.unscaledTime);
        }

        /// <summary>마우스로 누른 GUI 좌표에 글상자 버튼이 있으면 실행하고 true</summary>
        public static bool TryClick(Vector2 gui) => Targets.TryClick(gui, Time.unscaledTime);

        public static bool TryFind(string text, out Vector2 center, int nth = 0) => Targets.TryFind(text, nth, Time.unscaledTime, out center);

        private static string FirstLine(string text)
        {
            int i = text.IndexOf('\n');
            string first = i < 0 ? text : text.Substring(0, i);
            // AppendLine은 Windows에서 "\r\n"을 붙인다. '\r'이 남으면 접은 글상자에 제목이 보이지 않았다 (2026-11-21 캡처).
            return first.TrimEnd('\r');
        }
    }
}
