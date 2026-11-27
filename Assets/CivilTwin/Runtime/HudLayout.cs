using System.Collections.Generic;
using UnityEngine;

namespace CivilTwin.Runtime
{
    /// <summary>
    /// 화면 메뉴(버튼)가 차지하는 자리를 기록해, 그 위에서 누른 마우스가 지형 측정이나 카메라 회전으로 새지 않게 한다 (5-6).
    /// 메뉴는 OnGUI에서 자기 자리를 Register하고, 측정·카메라 도구는 Update에서 IsOverUi로 확인한다.
    /// OnGUI는 Update보다 늦게 불리므로, 지난 프레임과 이번 프레임의 자리를 함께 본다.
    /// 좌표: GUI 좌표(왼쪽 위가 0, 아래로 갈수록 y 증가). 마우스 좌표(왼쪽 아래가 0)는 IsOverUi 안에서 바꾼다.
    /// </summary>
    public static class HudLayout
    {
        private static List<Rect> _current = new List<Rect>();
        private static List<Rect> _previous = new List<Rect>();
        private static int _frame = -1;

        public static void Register(Rect guiRect)
        {
            if (_frame != Time.frameCount)
            {
                List<Rect> swap = _previous;
                _previous = _current;
                _current = swap;
                _current.Clear();
                _frame = Time.frameCount;
            }
            _current.Add(guiRect);
        }

        /// <summary>마우스 좌표(Input System, 왼쪽 아래 0)가 메뉴 위에 있으면 true</summary>
        public static bool IsOverUi(Vector2 screenPosition)
        {
            var gui = HudMode.MouseToGui(screenPosition);   // 글자 크기 배율을 뺀 GUI 좌표
            foreach (Rect r in _current) if (r.Contains(gui)) return true;
            foreach (Rect r in _previous) if (r.Contains(gui)) return true;
            return false;
        }

        /// <summary>현장이 바뀌거나 시작할 때 비운다.</summary>
        public static void Reset()
        {
            _current.Clear();
            _previous.Clear();
            _frame = -1;
        }
    }
}
