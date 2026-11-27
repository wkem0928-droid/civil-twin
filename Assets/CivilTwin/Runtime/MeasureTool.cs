using System.Globalization;
using CivilTwin.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CivilTwin.Runtime
{
    /// <summary>
    /// 지형을 클릭해 표고와 두 점 사이의 거리·고저차·경사를 잰다.
    /// 계산은 전부 Core가 한다. 이 클래스는 클릭을 전달하고 결과를 화면에 보여 준다.
    ///
    /// 조작:
    ///   왼쪽 버튼 클릭 (끌지 않고 눌렀다 떼기) : 점 찍기. 첫 번째 = A, 두 번째 = B, 세 번째 = 새 A
    ///   C 또는 Esc                             : 지우기
    ///
    /// 왼쪽 버튼을 누른 채 끌면 카메라 회전이므로, 5픽셀 넘게 움직였으면 클릭으로 보지 않는다.
    ///
    /// 화면 글자는 임시로 IMGUI(OnGUI)를 쓴다. UI 단계에서 UI Toolkit으로 바꾼다.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class MeasureTool : MonoBehaviour
    {
        private const float ClickMovePixels = 5f;
        private const int ProfileSegments = 64;
        private const double MaxRayDistance = 20000.0;

        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        private ElevationGrid _grid;
        private LocalFrame _frame;
        private Camera _camera;

        private bool _hasA;
        private bool _hasB;
        private SurveyPoint _a;
        private SurveyPoint _b;
        private TwoPointMeasurement _measurement;

        private bool _hasHover;
        private SurveyPoint _hover;

        private Vector2 _pressPosition;
        private bool _pressTracked;

        private Transform _markerA;
        private Transform _markerB;
        private LineRenderer _line;
        private GUIStyle _style;

        public bool HasA => _hasA;
        public bool HasB => _hasB;
        public TwoPointMeasurement Measurement => _measurement;

        public void Initialize(ElevationGrid grid, LocalFrame frame)
        {
            _grid = grid;
            _frame = frame;
            _camera = GetComponent<Camera>();

            // 현장을 바꿀 때 다시 불리므로, 표식과 선은 처음 한 번만 만든다.
            if (_markerA == null) _markerA = CreateMarker("Measure A", new Color(0.90f, 0.15f, 0.15f));
            if (_markerB == null) _markerB = CreateMarker("Measure B", new Color(0.15f, 0.35f, 0.95f));
            if (_line == null) _line = CreateLine();
            ClearPoints();
        }

        /// <summary>
        /// 재는 대상 지형을 바꾼다 (예: 원지반 ↔ 계획면). 좌표 기준은 그대로다.
        /// 이미 찍은 점은 이전 지형의 표고라서 지운다.
        /// </summary>
        public void SetGrid(ElevationGrid grid)
        {
            if (grid == null) throw new System.ArgumentNullException(nameof(grid));
            _grid = grid;
            ClearPoints();
        }

        /// <summary>화면 위치(픽셀)가 가리키는 지형 위의 점을 구한다.</summary>
        public bool TryPick(Vector2 screenPosition, out SurveyPoint point)
        {
            Ray ray = _camera.ScreenPointToRay(screenPosition);

            // Unity의 시선(로컬 좌표, float)을 측량 좌표(double)로 옮겨 Core에 넘긴다.
            SurveyPoint origin = _frame.ToSurvey(new LocalPoint(ray.origin.x, ray.origin.y, ray.origin.z));

            return TerrainRaycaster.TryHit(
                _grid, origin,
                ray.direction.x,   // 동쪽 = X
                ray.direction.z,   // 북쪽 = Z
                ray.direction.y,   // 위쪽 = Y
                MaxRayDistance,
                out point);
        }

        public void AddPoint(SurveyPoint point)
        {
            if (!_hasA || _hasB)
            {
                // 첫 점이거나, 이미 두 점이 있으면 새로 시작한다.
                _a = point;
                _hasA = true;
                _hasB = false;
            }
            else
            {
                _b = point;
                _hasB = true;
                _measurement = TwoPointMeasurement.Between(_a, _b);
            }
            RefreshVisuals();
        }

        public void ClearPoints()
        {
            _hasA = false;
            _hasB = false;
            RefreshVisuals();
        }

        private void Update()
        {
            if (_grid == null) return;
            if (!InputGate.Allowed) return;

            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 position = mouse.position.ReadValue();

                _hasHover = TryPick(position, out _hover);

                if (mouse.leftButton.wasPressedThisFrame)
                {
                    _pressPosition = position;
                    _pressTracked = !HudLayout.IsOverUi(position);   // 화면 메뉴를 누른 것은 측정점이 아니다 (5-6)
                }

                if (mouse.leftButton.wasReleasedThisFrame && _pressTracked)
                {
                    _pressTracked = false;
                    bool isClick = (position - _pressPosition).magnitude <= ClickMovePixels;
                    if (isClick && TryPick(position, out SurveyPoint picked))
                    {
                        AddPoint(picked);
                    }
                }
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                (keyboard.cKey.wasPressedThisFrame || keyboard.escapeKey.wasPressedThisFrame))
            {
                ClearPoints();
            }
        }

        private void LateUpdate()
        {
            if (_grid == null) return;

            // 멀리서 봐도 보이도록, 표식과 선의 크기를 카메라 거리에 비례시킨다.
            if (_hasA) ScaleMarker(_markerA);
            if (_hasB) ScaleMarker(_markerB);
            if (_hasB)
            {
                Vector3 middle = 0.5f * (_markerA.position + _markerB.position);
                _line.widthMultiplier = Mathf.Max(0.05f, Vector3.Distance(_camera.transform.position, middle) * 0.004f);
            }
        }

        private void RefreshVisuals()
        {
            if (_markerA == null) return;

            _markerA.gameObject.SetActive(_hasA);
            _markerB.gameObject.SetActive(_hasB);
            _line.gameObject.SetActive(_hasB);

            if (_hasA) _markerA.position = _frame.ToLocal(_a).ToVector3();
            if (_hasB) _markerB.position = _frame.ToLocal(_b).ToVector3();

            if (_hasB)
            {
                // 두 점을 잇는 수평 직선을 따라 지표면 표고를 읽어, 지형에 걸쳐진 선으로 그린다.
                _line.positionCount = ProfileSegments + 1;
                for (int i = 0; i <= ProfileSegments; i++)
                {
                    double t = (double)i / ProfileSegments;
                    double easting = _a.Easting + (_b.Easting - _a.Easting) * t;
                    double northing = _a.Northing + (_b.Northing - _a.Northing) * t;

                    if (!_grid.TryGetElevationAt(easting, northing, out double elevation))
                    {
                        elevation = _a.Elevation + (_b.Elevation - _a.Elevation) * t;
                    }

                    // 지표면에 묻히지 않도록 살짝(0.3 m) 띄운다. 표시용이며 계산값과는 무관하다.
                    var local = _frame.ToLocal(easting, northing, elevation + 0.3);
                    _line.SetPosition(i, local.ToVector3());
                }
            }
        }

        private void ScaleMarker(Transform marker)
        {
            float distance = Vector3.Distance(_camera.transform.position, marker.position);
            marker.localScale = Vector3.one * Mathf.Max(0.3f, distance * 0.012f);
        }

        private static Material CreateUnlitMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                throw new System.InvalidOperationException(
                    "셰이더 'Universal Render Pipeline/Unlit'을 찾지 못했다.");
            }
            var material = new Material(shader);
            material.SetColor("_BaseColor", color);
            return material;
        }

        private static Transform CreateMarker(string name, Color color)
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = name;
            Destroy(sphere.GetComponent<Collider>());
            sphere.GetComponent<MeshRenderer>().sharedMaterial = CreateUnlitMaterial(color);
            return sphere.transform;
        }

        private static LineRenderer CreateLine()
        {
            var go = new GameObject("Measure Line");
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.sharedMaterial = CreateUnlitMaterial(new Color(1.0f, 0.85f, 0.10f));
            line.numCapVertices = 4;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        // ---------- 화면 글자 (임시 IMGUI) ----------

        private void OnGUI()
        {
            HudMode.ApplyMatrix();   // 글자 크기 (메뉴 [화면])
            if (_grid == null) return;
            if (!HudMode.Show(HudMode.Item.Measure))
            {
                GuiBottom = 2f;   // 건물 글상자 등이 왼쪽 위로 올라오게
                return;
            }

            if (_style == null)
            {
                _style = HudStyle.Panel(14);   // 모든 글상자 공통 모양 (2026-11-20)
            }

            string text = BuildText();
            Vector2 size = _style.CalcSize(new GUIContent(text));
            GUI.Box(new Rect(10, 10, size.x + 4, size.y + 4), text, _style);
            GuiBottom = 10 + size.y + 4;
        }

        /// <summary>이 글상자의 아래쪽 끝 (화면 픽셀). 다른 글상자를 그 아래에 놓을 때 쓴다.</summary>
        public float GuiBottom { get; private set; } = 10f;

        private string BuildText()
        {
            var sb = new System.Text.StringBuilder();
            // 점을 찍지 않았고 "글상자 자세히"도 아니면 한 줄만 (2026-11-20 개발자 사용 의견: 화면이 글상자로 덮임)
            if (!_hasA && !HudMode.Detail)
            {
                return "[측정] 지형을 두 번 클릭하면 거리·경사";
            }
            sb.AppendLine("[측정]  클릭: 점 찍기   C: 지우기   R: 시점 복귀");
            sb.AppendLine(_hasHover ? "커서  " + Format(_hover) : "커서  (지형 밖)");

            if (_hasA) sb.AppendLine("A     " + Format(_a));
            if (_hasB)
            {
                sb.AppendLine("B     " + Format(_b));
                sb.AppendLine();
                sb.AppendLine("수평거리  " + Meters(_measurement.HorizontalDistance));
                sb.AppendLine("고저차    " + Signed(_measurement.HeightDifference) + " m");
                sb.AppendLine("사거리    " + Meters(_measurement.SlopeDistance));

                if (_measurement.HasDirection)
                {
                    string ratio = double.IsPositiveInfinity(_measurement.SlopeRatioN)
                        ? "평지"
                        : "1:" + _measurement.SlopeRatioN.ToString("F2", Invariant);

                    sb.AppendLine("경사      " + Signed(_measurement.SlopePercent) + " %   ("
                        + Signed(_measurement.SlopeAngleDegrees) + "°,  " + ratio + ")");
                    sb.AppendLine("방위각    " + _measurement.AzimuthDegrees.ToString("F2", Invariant) + "°  (A → B)");
                }
                else
                {
                    sb.AppendLine("경사      (같은 위치라서 정의되지 않음)");
                }
            }
            else if (_hasA)
            {
                sb.AppendLine("두 번째 점을 클릭하세요.");
            }

            return sb.ToString().TrimEnd();
        }

        private static string Format(SurveyPoint p)
        {
            return "동 " + p.Easting.ToString("N2", Invariant)
                + "   북 " + p.Northing.ToString("N2", Invariant)
                + "   표고 " + p.Elevation.ToString("F2", Invariant) + " m";
        }

        private static string Meters(double value)
        {
            return value.ToString("N2", Invariant) + " m";
        }

        private static string Signed(double value)
        {
            return value.ToString("+0.00;-0.00;0.00", Invariant);
        }
    }
}
