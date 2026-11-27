using System;
using System.Collections.Generic;
using CivilTwin.Core;
using UnityEngine;

namespace CivilTwin.Runtime
{
    /// <summary>
    /// 댐 시공 단계 3D의 장비 모형 (2026-11-25). 자리와 자세는 Core/DamWorkface가 정하고, 여기서는 모형을 만들어 놓고 움직이기만 한다.
    /// 모형은 종류마다 만들어 두고 다시 쓴다 (날짜가 바뀔 때마다 새로 만들지 않는다). 화면에서 보이도록 3배로 그린다.
    /// </summary>
    internal sealed class DamFleet3D
    {
        private readonly Func<double, double, double, Vector3> _toWorld;   // (s, d, z) → 화면 좌표
        private readonly Func<double, double, double> _ground;             // (s, d) → 지형 표고 (없으면 NaN)
        private readonly PrimitiveKit _kit = new PrimitiveKit();
        private readonly Dictionary<DamMachine, List<Transform>> _pool = new Dictionary<DamMachine, List<Transform>>();
        private readonly Dictionary<Transform, ExcavatorRig> _rigs = new Dictionary<Transform, ExcavatorRig>();

        public Transform Root { get; }

        public DamFleet3D(Func<double, double, double, Vector3> toWorld, Func<double, double, double> ground)
        {
            _toWorld = toWorld;
            _ground = ground;
            Root = new GameObject("DamStage3D 장비").transform;
        }

        public void Show(List<DamMachinePose> poses, double seconds)
        {
            var used = new Dictionary<DamMachine, int>();
            foreach (DamMachinePose p in poses)
            {
                double z = double.IsNaN(p.Z) ? _ground(p.S, p.D) : p.Z;
                if (double.IsNaN(z)) continue;
                used.TryGetValue(p.Kind, out int index);
                used[p.Kind] = index + 1;
                Transform t = Get(p.Kind, index);
                Vector3 at = _toWorld(p.S, p.D, z);
                Vector3 forward = _toWorld(p.S + p.HeadS, p.D + p.HeadD, z) - at;
                forward.y = 0f;
                t.SetPositionAndRotation(at, forward.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(forward) : Quaternion.identity);

                if (p.Kind == DamMachine.DumpTruck)
                {
                    EquipmentModels.SetTruckBed(t, p.BedRaise);
                    EquipmentModels.SetTruckSoil(t, p.Load);
                }
                else if (p.Kind == DamMachine.Excavator && _rigs.TryGetValue(t, out ExcavatorRig rig))
                {
                    ExcavatorPose pose = ExcavatorCycle.PoseAt(seconds + 3.0 * index, 19.0, 90.0);   // Cm 19초, 선회 90° (표준품셈)
                    rig.Pose(pose.Swing, pose.BoomRaise, pose.ArmOut, pose.BucketOpen);
                }
            }
            foreach (KeyValuePair<DamMachine, List<Transform>> pair in _pool)
            {
                used.TryGetValue(pair.Key, out int n);
                for (int i = n; i < pair.Value.Count; i++) pair.Value[i].gameObject.SetActive(false);
            }
        }

        public void Destroy()
        {
            if (Root != null) UnityEngine.Object.Destroy(Root.gameObject);   // 기본 도형 메시는 공용이라 지우지 않는다
            _kit.DestroyMaterials();
            _pool.Clear();
            _rigs.Clear();
        }

        private Transform Get(DamMachine kind, int index)
        {
            if (!_pool.TryGetValue(kind, out List<Transform> list)) _pool[kind] = list = new List<Transform>();
            while (list.Count <= index)
            {
                Transform made = Make(kind);
                made.localScale = Vector3.one * (float)DamWorkface.DrawScale;
                list.Add(made);
            }
            Transform t = list[index];
            if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            return t;
        }

        private Transform Make(DamMachine kind)
        {
            switch (kind)
            {
                case DamMachine.DumpTruck: return EquipmentModels.DumpTruck(_kit, Root);
                case DamMachine.Bulldozer: return EquipmentModels.Bulldozer(_kit, Root);
                case DamMachine.Roller: return EquipmentModels.Roller(_kit, Root);
                case DamMachine.Excavator:
                    ExcavatorRig rig = EquipmentModels.Excavator(_kit, Root);
                    _rigs[rig.Root] = rig;
                    return rig.Root;
                default: return DrillRig();
            }
        }

        /// <summary>천공 장비 (보링기): 무한궤도 차대 + 세운 마스트 + 로드 + 펌프 상자. 치수는 보기용 어림값.</summary>
        private Transform DrillRig()
        {
            Transform root = _kit.Group(Root, "천공 장비", Vector3.zero);
            _kit.Track(root, "무한궤도 왼쪽", -0.8f, 2.6f, 0.6f, 0.4f);
            _kit.Track(root, "무한궤도 오른쪽", 0.8f, 2.6f, 0.6f, 0.4f);
            _kit.Box(root, "차대", new Vector3(0f, 0.8f, -0.2f), new Vector3(1.7f, 0.4f, 2.4f), PrimitiveKit.Gunmetal);
            _kit.Box(root, "엔진", new Vector3(0f, 1.4f, -0.8f), new Vector3(1.4f, 0.8f, 1.1f), PrimitiveKit.Orange);
            _kit.Box(root, "마스트", new Vector3(0f, 3.4f, 1.2f), new Vector3(0.45f, 5.6f, 0.35f), PrimitiveKit.Orange);
            _kit.Rod(root, "천공 로드", new Vector3(0f, 0f, 1.5f), new Vector3(0f, 6.0f, 1.5f), 0.14f, PrimitiveKit.Chrome);
            _kit.Box(root, "구동부", new Vector3(0f, 4.2f, 1.5f), new Vector3(0.5f, 0.6f, 0.4f), PrimitiveKit.Gunmetal);
            _kit.Hydraulic(root, "마스트 실린더", new Vector3(0f, 1.2f, 0.2f), new Vector3(0f, 3.0f, 1.1f), 0.16f);
            return root;
        }
    }
}
