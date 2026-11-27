using CivilTwin.Core;
using UnityEngine;

namespace CivilTwin.Runtime
{
    /// <summary>
    /// 범례: 화면 왼쪽 아래에 색 띠와 표고 숫자를 보여 준다.
    /// 색은 "이 지형 안에서의 상대 높이"라서, 범례가 없으면 색이 몇 m인지 알 수 없다.
    /// 임시 화면(IMGUI)이다. 나중에 UI Toolkit으로 바꾼다.
    /// </summary>
    public class ElevationLegend : MonoBehaviour
    {
        private const int BarWidth = 22;
        private const int BarHeight = 180;
        private const int Margin = 10;
        private const int Padding = 10;

        private ElevationColorRamp _ramp;
        private double _min;
        private double _max;
        private int _flatCount;

        private Texture2D _bar;
        private Texture2D _water;
        private GUIStyle _box;
        private GUIStyle _label;

        public void Initialize(ElevationColorRamp ramp, double minElevation, double maxElevation, int flatCount)
        {
            _ramp = ramp;
            _min = minElevation;
            _max = maxElevation;
            _flatCount = flatCount;

            if (_bar != null) Destroy(_bar);
            if (_water != null) Destroy(_water);

            // 색 띠 그림: 아래가 최저 표고, 위가 최고 표고.
            _bar = new Texture2D(1, BarHeight, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < BarHeight; y++)
            {
                double t = (double)y / (BarHeight - 1);
                _bar.SetPixel(0, y, HudStyle.Ui(TerrainColorTextureFactory.ToColor32(ramp.ColorAt(t))));
            }
            _bar.Apply();

            _water = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _water.SetPixel(0, 0, HudStyle.Ui(TerrainColorTextureFactory.ToColor32(TerrainColorTextureFactory.WaterColor)));
            _water.Apply();
        }

        private void OnDestroy()
        {
            if (_bar != null) Destroy(_bar);
            if (_water != null) Destroy(_water);
        }

        private void OnGUI()
        {
            HudMode.ApplyMatrix();   // 글자 크기 (메뉴 [화면])
            if (_ramp == null || _bar == null) return;
            if (!HudMode.Show(HudMode.Item.Legend)) return;
            // 터널 보기 중에는 아래 종단면이 그 자리를 쓴다 (2026-11-21)
            var tunnel = GetComponent<TunnelTool>();
            if (tunnel != null && tunnel.IsOn) return;
            var damStage = GetComponent<DamStage3D>();   // 댐 시공 단계 3D는 자기 범례를 쓴다
            if (damStage != null && damStage.Visible) return;
            var bridgeStage = GetComponent<BridgeStage3D>();   // 교량 3D도 화면을 넓게 쓴다
            if (bridgeStage != null && bridgeStage.Visible) return;

            if (_box == null)
            {
                _box = HudStyle.Box();   // 모든 글상자 공통 모양 (2026-11-20)
                _label = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
                _label.normal.textColor = HudStyle.Ui(Color.white);
            }

            bool showWater = _flatCount > 0;
            int boxWidth = 190;
            int boxHeight = Padding + 22 + BarHeight + (showWater ? 30 : 0) + Padding;
            float x = Margin;
            float y = HudMode.GuiHeight - Margin - boxHeight;

            GUI.Box(new Rect(x, y, boxWidth, boxHeight), GUIContent.none, _box);

            float cx = x + Padding;
            float cy = y + Padding;
            GUI.Label(new Rect(cx, cy, boxWidth - 2 * Padding, 20), "표고 (m)", _label);
            cy += 22;

            GUI.DrawTexture(new Rect(cx, cy, BarWidth, BarHeight), _bar, ScaleMode.StretchToFill);

            // 눈금 5개: 최고, 3/4, 가운데, 1/4, 최저. 색 띠의 기준점 위치와 같다.
            for (int i = 0; i <= 4; i++)
            {
                double t = i / 4.0;
                double elevation = _min + t * (_max - _min);
                float ty = cy + (float)((1.0 - t) * (BarHeight - 1)) - 10f;
                ty = Mathf.Clamp(ty, cy - 2f, cy + BarHeight - 18f);
                GUI.Label(new Rect(cx + BarWidth + 8, ty, 130, 20), elevation.ToString("F1"), _label);
            }
            cy += BarHeight;

            if (showWater)
            {
                cy += 8;
                GUI.DrawTexture(new Rect(cx, cy + 2, BarWidth, 16), _water, ScaleMode.StretchToFill);
                GUI.Label(new Rect(cx + BarWidth + 8, cy, 140, 20), "평평한 면 (수면 추정)", _label);
            }
        }
    }
}
