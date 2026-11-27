using UnityEngine;

namespace CivilTwin.Runtime
{
    /// <summary>덤프트럭 적재함을 드는 부분 (2026-11-20): 적재함(뒤 경첩), 뒷문(위 경첩), 호이스트 실린더.</summary>
    internal sealed class DumpBedRig : MonoBehaviour
    {
        public Transform Bed, Gate;
        internal HydraulicLink Hoist;
        private float _raise = -1f;

        /// <summary>0 = 내림, 1 = 다 듦. 뒷문은 적재함이 기운 만큼 거꾸로 돌려 늘 아래로 늘어지게 한다.</summary>
        public void Raise(float raise)
        {
            raise = Mathf.Clamp01(raise);
            if (Mathf.Abs(raise - _raise) < 1e-4f) return;
            _raise = raise;
            float tilt = raise * EquipmentModels.MaxBedTilt;
            Bed.localEulerAngles = new Vector3(-tilt, 0f, 0f);
            Gate.localEulerAngles = new Vector3(tilt, 0f, 0f);
            Hoist?.Update();
        }
    }
}
