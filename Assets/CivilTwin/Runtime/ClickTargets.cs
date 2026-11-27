using System;
using System.Collections.Generic;
using UnityEngine;

namespace CivilTwin.Runtime
{
    /// <summary>
    /// 화면 버튼의 "누를 수 있는 자리" 목록 (InfoBox가 쓴다). GUI 함수를 부르지 않으므로 테스트에서 바로 확인할 수 있다.
    ///
    /// 규칙 (2026-11-26 실제 마우스 버그에서 나옴):
    ///   1. 목록은 새 프레임의 첫 그리기(Repaint)에서만 비운다. 마우스·Layout 이벤트에서는 건드리지 않는다.
    ///      Unity는 실제 마우스를 누르면 그 프레임 앞쪽에서 OnGUI에 마우스 이벤트를 먼저 보낸다. 이때 목록을 비우면
    ///      바로 뒤의 누름 확인(ControlBar.Update)이 빈 목록을 본다 → 버튼이 하나도 안 눌렸다.
    ///   2. 버튼은 그리기 때만 올린다 (한 프레임에 같은 버튼이 두 번 올라가지 않게).
    ///   3. 마지막으로 그린 지 FreshSeconds가 지난 목록은 누를 수 없다 (글상자를 숨기면 예전 버튼이 눌리지 않게).
    /// </summary>
    public sealed class ClickTargets
    {
        public const float FreshSeconds = 0.3f;

        private readonly List<(Rect rect, string text, Action action)> _items = new List<(Rect, string, Action)>();
        private int _frame = -1;
        private float _drawTime = float.NegativeInfinity;

        public int Count => _items.Count;

        /// <summary>OnGUI의 이벤트마다, 버튼을 그리기 전에 부른다.</summary>
        public void BeginEvent(bool isRepaint, int frame)
        {
            if (!isRepaint || _frame == frame) return;
            _items.Clear();
            _frame = frame;
        }

        /// <summary>버튼 하나를 올린다 (그리기 이벤트일 때만).</summary>
        public void Add(Rect rect, string text, Action action, bool isRepaint, float time)
        {
            if (!isRepaint) return;
            _items.Add((rect, text, action));
            _drawTime = time;
        }

        public bool IsFresh(float now) => now - _drawTime <= FreshSeconds;

        /// <summary>그 자리에 버튼이 있으면 실행하고 true</summary>
        public bool TryClick(Vector2 point, float now)
        {
            if (!IsFresh(now)) return false;
            foreach (var b in _items)
            {
                if (!b.rect.Contains(point)) continue;
                b.action?.Invoke();
                return true;
            }
            return false;
        }

        /// <summary>글자가 text로 시작하는 nth번째(0부터) 버튼의 가운데 (자동 시연용)</summary>
        public bool TryFind(string text, int nth, float now, out Vector2 center)
        {
            center = Vector2.zero;
            if (!IsFresh(now)) return false;
            int seen = 0;
            foreach (var b in _items)
            {
                if (!b.text.StartsWith(text, StringComparison.Ordinal)) continue;
                if (seen++ < nth) continue;
                center = b.rect.center;
                return true;
            }
            return false;
        }
    }
}
