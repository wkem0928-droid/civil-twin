using CivilTwin.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CivilTwin.Runtime
{
    /// <summary>
    /// 마우스와 키보드 입력을 읽어 궤도 카메라를 움직인다.
    /// 위치 계산은 Core의 OrbitCamera가 하고, 이 클래스는 입력을 전달하고 결과를 카메라에 적용한다.
    ///
    /// 조작:
    ///   왼쪽 버튼 드래그        : 회전 (좌우 = 방위각, 상하 = 고도각)
    ///   오른쪽/가운데 버튼 드래그 : 이동 (지형을 잡고 끄는 느낌)
    ///   휠                      : 확대/축소
    ///   W A S D 또는 방향키     : 이동
    ///   R                       : 처음 시점으로 되돌리기
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class OrbitCameraController : MonoBehaviour
    {
        [Tooltip("마우스를 1픽셀 움직일 때 도는 각도 (도)")]
        public float orbitDegreesPerPixel = 0.25f;

        [Tooltip("마우스를 1픽셀 움직일 때 이동량 = 카메라 거리 × 이 값")]
        public float panFractionPerPixel = 0.0015f;

        [Tooltip("휠 한 칸에 곱해지는 거리 배율 (1보다 작으면 다가감)")]
        public float zoomFactorPerNotch = 0.88f;

        [Tooltip("키보드 이동 속도 = 초당 카메라 거리 × 이 값")]
        public float keyPanFractionPerSecond = 0.6f;

        [Tooltip("움직임을 부드럽게 따라가는 빠르기. 클수록 즉각 반응한다.")]
        public float smoothing = 14f;

        private OrbitCamera _goal;        // 입력이 반영된 목표 상태
        private LocalPoint _startTarget;
        private double _startYaw;
        private double _startPitch;
        private double _startDistance;

        // 화면에 실제로 적용 중인 값. 목표를 부드럽게 따라간다.
        private Vector3 _shownTarget;
        private float _shownYaw;
        private float _shownPitch;
        private float _shownDistance;

        public OrbitCamera State => _goal;

        public void Initialize(OrbitCamera orbit)
        {
            _goal = orbit;
            _startTarget = orbit.Target;
            _startYaw = orbit.YawDegrees;
            _startPitch = orbit.PitchDegrees;
            _startDistance = orbit.Distance;
            SnapToGoal();
            ApplyToTransform();
        }

        private void Update()
        {
            if (_goal == null) return;
            if (!InputGate.Allowed) return;

            ReadMouse();
            ReadKeyboard();
        }

        private void LateUpdate()
        {
            if (_goal == null) return;

            // 지수 감쇠: 프레임 속도와 관계없이 같은 빠르기로 목표에 다가간다.
            float t = 1f - Mathf.Exp(-smoothing * Time.unscaledDeltaTime);

            _shownTarget = Vector3.Lerp(_shownTarget, _goal.Target.ToVector3(), t);
            _shownYaw = Mathf.LerpAngle(_shownYaw, (float)_goal.YawDegrees, t);
            _shownPitch = Mathf.Lerp(_shownPitch, (float)_goal.PitchDegrees, t);
            _shownDistance = Mathf.Lerp(_shownDistance, (float)_goal.Distance, t);

            ApplyToTransform();
        }

        private bool _dragFromUi;

        private void ReadMouse()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 delta = mouse.delta.ReadValue();

            // 화면 메뉴 위에서 누르기 시작한 드래그와 휠은 카메라를 움직이지 않는다 (5-6).
            bool anyPressedNow = mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame
                || mouse.middleButton.wasPressedThisFrame;
            if (anyPressedNow) _dragFromUi = HudLayout.IsOverUi(mouse.position.ReadValue());
            if (!mouse.leftButton.isPressed && !mouse.rightButton.isPressed && !mouse.middleButton.isPressed) _dragFromUi = false;
            if (_dragFromUi) return;
            if (HudLayout.IsOverUi(mouse.position.ReadValue()) && mouse.scroll.ReadValue().y != 0f) return;

            if (mouse.leftButton.isPressed)
            {
                // 마우스를 오른쪽으로 끌면 지형이 오른쪽으로 도는 것처럼 보이게 한다.
                _goal.Orbit(delta.x * orbitDegreesPerPixel, -delta.y * orbitDegreesPerPixel);
            }
            else if (mouse.rightButton.isPressed || mouse.middleButton.isPressed)
            {
                // 지형을 잡고 끄는 느낌: 마우스를 오른쪽으로 끌면 주시점은 왼쪽으로 간다.
                double metersPerPixel = _goal.Distance * panFractionPerPixel;
                _goal.Pan(-delta.x * metersPerPixel, -delta.y * metersPerPixel);
            }

            float scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0f)
            {
                // 장치와 버전에 따라 휠 한 칸이 1 또는 120으로 들어온다. 칸 수로 맞춘다.
                float notches = Mathf.Abs(scroll) >= 20f ? scroll / 120f : scroll;
                notches = Mathf.Clamp(notches, -3f, 3f);
                _goal.Zoom(Mathf.Pow(zoomFactorPerNotch, notches));
            }
        }

        private void ReadKeyboard()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            float right = 0f;
            float forward = 0f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) right += 1f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) right -= 1f;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) forward += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) forward -= 1f;

            if (right != 0f || forward != 0f)
            {
                double meters = _goal.Distance * keyPanFractionPerSecond * Time.unscaledDeltaTime;
                _goal.Pan(right * meters, forward * meters);
            }

            if (keyboard.rKey.wasPressedThisFrame) ResetView();
        }

        /// <summary>처음 시점(현장 전체)으로 되돌린다. 화면은 부드럽게 따라간다.</summary>
        public void ResetView()
        {
            if (_goal == null) return;
            _goal = new OrbitCamera(
                _startTarget, _startYaw, _startPitch, _startDistance,
                _goal.MinDistance, _goal.MaxDistance, _goal.MinPitchDegrees, _goal.MaxPitchDegrees);
        }

        /// <summary>
        /// 한 지점을 가까이에서 본다 (근접 현장 화면). 방향은 그대로 두고 주시점과 거리만 바꾼다.
        /// 장비처럼 작은 것을 보려면 더 다가갈 수 있어야 하므로 최소 거리도 줄인다.
        /// </summary>
        public void FocusOn(LocalPoint target, double distance, double minDistance)
        {
            if (_goal == null) return;
            _goal = new OrbitCamera(
                target, _goal.YawDegrees, _goal.PitchDegrees, distance,
                System.Math.Min(minDistance, _goal.MinDistance), _goal.MaxDistance,
                _goal.MinPitchDegrees, _goal.MaxPitchDegrees);
        }

        /// <summary>
        /// 정해진 방향에서 본다 (댐처럼 "어느 쪽에서 봐야 모양이 보이는" 구조물용, 2026-11-20).
        /// 방위각 0° = 카메라가 주시점의 남쪽에서 북쪽을 본다 (OrbitCamera.GetPosition과 같은 약속).
        /// </summary>
        public void LookFrom(LocalPoint target, double yawDegrees, double pitchDegrees, double distance, double minDistance)
        {
            if (_goal == null) return;
            _goal = new OrbitCamera(
                target, yawDegrees, pitchDegrees, distance,
                System.Math.Min(minDistance, _goal.MinDistance), _goal.MaxDistance,
                _goal.MinPitchDegrees, _goal.MaxPitchDegrees);
        }

        private void SnapToGoal()
        {
            _shownTarget = _goal.Target.ToVector3();
            _shownYaw = (float)_goal.YawDegrees;
            _shownPitch = (float)_goal.PitchDegrees;
            _shownDistance = (float)_goal.Distance;
        }

        private void ApplyToTransform()
        {
            // 화면에 적용할 때도 같은 계산식(Core)을 쓴다.
            var shown = new OrbitCamera(
                new LocalPoint(_shownTarget.x, _shownTarget.y, _shownTarget.z),
                _shownYaw, _shownPitch, _shownDistance,
                _goal.MinDistance, _goal.MaxDistance, _goal.MinPitchDegrees, _goal.MaxPitchDegrees);

            transform.position = shown.GetPosition().ToVector3();
            transform.LookAt(_shownTarget, Vector3.up);
        }
    }
}
