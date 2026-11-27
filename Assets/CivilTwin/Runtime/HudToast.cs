using UnityEngine;

namespace CivilTwin.Runtime
{
    /// <summary>잠깐 떴다 사라지는 안내 한 줄 (2026-11-25). Show로 띄우고, 화면을 그리는 쪽이 OnGUI 끝에서 Draw를 부른다.</summary>
    public static class HudToast
    {
        private const float Seconds = 6f;
        private static string _text = "";
        private static float _until;

        public static void Show(string text)
        {
            _text = text ?? "";
            _until = Time.unscaledTime + Seconds;
        }

        public static void Draw()
        {
            if (_text.Length == 0 || Time.unscaledTime > _until) return;
            var style = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            style.normal.textColor = HudStyle.Ui(HudStyle.Text);
            float w = Mathf.Min(style.CalcSize(new GUIContent(_text)).x + 32f, HudMode.GuiWidth - 40f);
            var r = new Rect((HudMode.GuiWidth - w) / 2f, HudMode.GuiHeight * 0.5f - 18f, w, 36f);
            for (int i = 0; i < 3; i++) HudStyle.DrawBox(r);   // 뒤 글이 비치지 않게 겹쳐 그린다
            GUI.Label(r, _text, style);
        }
    }
}
