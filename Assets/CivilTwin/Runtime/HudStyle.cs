using UnityEngine;

namespace CivilTwin.Runtime
{
    /// <summary>
    /// 화면 글상자의 공통 모양 (2026-11-20 전체 검토). 예전에는 Unity 기본 상자(반투명 회색)와 메뉴(불투명 짙은 색)가
    /// 섞여 있어 화면이 조각나 보였다. 모든 글상자가 같은 짙은 배경·글자색·여백을 쓰게 한다.
    /// </summary>
    public static class HudStyle
    {
        /// <summary>
        /// 글상자 배경색 (짙은 남회색, 조금 비침). 2026-11-21: 비침 0.88 → 0.94. 실행 파일(선형 색 공간에서 섞임)에서는
        /// 같은 반투명 상자가 에디터보다 밝게 보여 글자가 덜 읽혔다 — 불투명에 가깝게 해서 차이를 줄인다 (원인은 추정, 검증되지 않음).
        /// </summary>
        public static readonly Color Background = new Color(0.10f, 0.11f, 0.13f, 0.94f);

        /// <summary>제목 줄에 쓰는 강조색 (메뉴의 선택 탭과 같은 노랑)</summary>
        public static readonly Color Accent = new Color(0.95f, 0.75f, 0.25f);

        public static readonly Color Text = new Color(0.92f, 0.93f, 0.95f);

        private static Texture2D _backgroundTexture;

        private static int _playerLinear = -1;

        /// <summary>
        /// 화면 글상자(IMGUI)에 쓰는 색 보정 (2026-11-21). 실행 파일(선형 색 공간)에서는 같은 색이 에디터보다 밝게 나왔다
        /// (Windows가 찍은 실제 창 그림으로 확인: 짙은 남회색 상자가 회색으로 보임). 실행 파일에서만 색을 선형 값으로 바꿔 넘겨
        /// 화면에 나올 때 원래 색이 되게 한다. 에디터에서는 그대로 둔다. 원인(출력 단계의 sRGB 변환)은 추정이다.
        /// </summary>
        public static Color Ui(Color c)
        {
            if (_playerLinear < 0)
                _playerLinear = !Application.isEditor && QualitySettings.activeColorSpace == ColorSpace.Linear ? 1 : 0;
            return _playerLinear == 1 ? c.linear : c;
        }

        /// <summary>Color32용 (픽셀로 만드는 글상자 그림)</summary>
        public static Color32 Ui(Color32 c) => (Color32)Ui((Color)c);

        private static Texture2D BackgroundTexture
        {
            get
            {
                if (_backgroundTexture == null)
                {
                    _backgroundTexture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                    _backgroundTexture.SetPixel(0, 0, Ui(Background));
                    _backgroundTexture.Apply();
                }
                return _backgroundTexture;
            }
        }

        /// <summary>글이 들어가는 글상자 (왼쪽 위 정렬)</summary>
        public static GUIStyle Panel(int fontSize, bool wordWrap = false)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = fontSize,
                richText = false,
                wordWrap = wordWrap,
                padding = new RectOffset(12, 12, 9, 9),
            };
            style.normal.background = BackgroundTexture;
            style.normal.textColor = Ui(Text);
            return style;
        }

        /// <summary>글 없이 배경만 (표·그래프 상자)</summary>
        public static GUIStyle Box()
        {
            var style = new GUIStyle(GUI.skin.label);
            style.normal.background = BackgroundTexture;
            return style;
        }

        private static GUIStyle _box;

        /// <summary>배경 상자를 그린다 (GUI.Box(rect, GUIContent.none) 대신)</summary>
        public static void DrawBox(Rect rect)
        {
            if (_box == null) _box = Box();
            GUI.Box(rect, GUIContent.none, _box);
        }
    }
}
