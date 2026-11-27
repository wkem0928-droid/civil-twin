using UnityEngine;

namespace CivilTwin.Runtime
{
    /// <summary>
    /// 장비 전시장 (2026-11-20). 자동 시연에서 장비 모양을 가까이 확인하려고 만든 도구다.
    /// 현장 지형에서 멀리 떨어진 곳(동쪽 20 km)에 평평한 바닥을 깔고 모든 장비를 한 줄로 세운 뒤,
    /// 카메라를 그쪽으로 옮긴다. 평소 사용에는 나타나지 않는다 (시나리오 명령 "showroom"으로만).
    /// </summary>
    public sealed class EquipmentShowroom : MonoBehaviour
    {
        private static readonly Vector3 Origin = new Vector3(20000f, 0f, 0f);

        /// <summary>장비 순서 (시나리오의 focus 번호): 0 굴착기, 1 덤프트럭, 2 불도저, 3 롤러, 4 레미콘, 5 펌프카, 6 타워크레인</summary>
        private static readonly float[] Slots = { -36f, -22f, -9f, 2f, 13f, 26f, 48f };

        private static EquipmentShowroom _instance;
        private PrimitiveKit _kit;
        private ExcavatorRig _excavator;
        private Transform _truck;

        /// <summary>전시장 덤프트럭의 적재함을 든 정도(0 ~ 1)와 실은 흙(0 ~ 1). 시나리오 명령 "truckbed" (2026-11-20).</summary>
        public static void SetTruck(float bedRaise, float soil)
        {
            if (_instance == null || _instance._truck == null) return;
            EquipmentModels.SetTruckBed(_instance._truck, bedRaise);
            EquipmentModels.SetTruckSoil(_instance._truck, soil);
        }

        /// <summary>
        /// 전시장 굴착기의 사이클 시각(초). NaN이면 Play 시간을 따라 계속 움직이고, 값을 주면 그 시각의 자세로 멈춘다
        /// (시나리오 명령 "excavator 5.7" — 단계 경계 자세를 정확히 찍으려고).
        /// </summary>
        public static double FixedSeconds = double.NaN;

        private void Update()
        {
            if (_excavator == null) return;
            double t = double.IsNaN(FixedSeconds) ? Time.time : FixedSeconds;
            CivilTwin.Core.ExcavatorPose p = CivilTwin.Core.ExcavatorCycle.PoseAt(t, 19.0, 90.0);
            _excavator.Pose(p.Swing, p.BoomRaise, p.ArmOut, p.BucketOpen);
        }

        /// <summary>전시장을 (처음이면 만들고) 보여 주고, 카메라를 focus 장비(−1이면 전체 가운데) 쪽으로 돌린다.</summary>
        public static void Show(int focus, float yawDegrees, float pitchDegrees, float distance)
        {
            if (_instance == null)
            {
                var go = new GameObject("장비 전시장 (자동 시연용)");
                _instance = go.AddComponent<EquipmentShowroom>();
                _instance.Build();
            }

            Camera camera = Camera.main;
            if (camera == null) return;
            var orbit = camera.GetComponent<OrbitCameraController>();
            if (orbit != null) orbit.enabled = false;   // 카메라를 직접 놓는다

            float x = focus >= 0 && focus < Slots.Length ? Slots[focus] : 6f;
            float height = focus == 6 ? 18f : 2.0f;
            Vector3 target = Origin + new Vector3(x, height, 0f);
            float yaw = yawDegrees * Mathf.Deg2Rad, pitch = pitchDegrees * Mathf.Deg2Rad;
            float horizontal = distance * Mathf.Cos(pitch);
            camera.transform.position = target + new Vector3(-horizontal * Mathf.Sin(yaw), distance * Mathf.Sin(pitch), -horizontal * Mathf.Cos(yaw));
            camera.transform.LookAt(target, Vector3.up);
        }

        private void Build()
        {
            _kit = new PrimitiveKit();
            Transform root = transform;
            root.position = Origin;
            _kit.Box(root, "바닥", new Vector3(5f, -0.05f, 0f), new Vector3(120f, 0.1f, 50f), new Color(0.55f, 0.50f, 0.42f));

            _excavator = EquipmentModels.Excavator(_kit, root);
            Place(_excavator.Root, 0);
            _truck = EquipmentModels.DumpTruck(_kit, root);
            Place(_truck, 1);
            Place(EquipmentModels.Bulldozer(_kit, root), 2);
            Place(EquipmentModels.Roller(_kit, root), 3);
            Place(EquipmentModels.MixerTruck(_kit, root, out _), 4);
            Place(EquipmentModels.PumpTruck(_kit, root, 14f, 12f), 5);
            Place(EquipmentModels.TowerCrane(_kit, root, 30f, 40f, out _, out Transform top), 6);
            top.localEulerAngles = new Vector3(0f, -60f, 0f);
        }

        private static void Place(Transform model, int slot)
        {
            model.localPosition = new Vector3(Slots[slot], 0f, 0f);
            model.localEulerAngles = new Vector3(0f, 30f, 0f);   // 비스듬히 세워 앞과 옆이 함께 보이게
        }

        private void OnDestroy()
        {
            if (_kit != null) _kit.DestroyMaterials();
        }
    }
}
