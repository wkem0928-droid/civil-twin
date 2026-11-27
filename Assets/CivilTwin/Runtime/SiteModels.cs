using System.Collections.Generic;
using UnityEngine;

namespace CivilTwin.Runtime
{
    /// <summary>
    /// 상자와 원기둥을 조합해 건물 부재와 장비 모양을 만드는 도구 (Phase 3).
    /// 3D 모델 파일 없이 코드로만 만든 단순한 모양이다. 치수는 보기용 어림값이다.
    ///
    /// 방향 약속: 장비의 앞은 +Z, 오른쪽은 +X, 위는 +Y. 원점은 장비 한가운데의 땅바닥.
    /// 같은 색은 재질 하나를 함께 쓴다. 다 쓰면 DestroyMaterials로 지운다.
    /// </summary>
    internal sealed class PrimitiveKit
    {
        public static readonly Color Yellow = new Color(0.95f, 0.72f, 0.10f);
        public static readonly Color Orange = new Color(0.90f, 0.45f, 0.10f);
        public static readonly Color Red = new Color(0.78f, 0.16f, 0.12f);
        public static readonly Color White = new Color(0.90f, 0.90f, 0.88f);
        public static readonly Color Dark = new Color(0.13f, 0.13f, 0.14f);
        public static readonly Color Steel = new Color(0.50f, 0.52f, 0.55f);
        public static readonly Color Glass = new Color(0.35f, 0.55f, 0.70f);
        public static readonly Color Concrete = new Color(0.74f, 0.74f, 0.72f);
        public static readonly Color ConcreteDark = new Color(0.60f, 0.60f, 0.58f);
        public static readonly Color Soil = new Color(0.32f, 0.24f, 0.16f);
        // 2026-11-20 장비 모양 개선에서 더한 색
        public static readonly Color YellowDark = new Color(0.66f, 0.48f, 0.06f);
        public static readonly Color OrangeDark = new Color(0.60f, 0.29f, 0.07f);
        public static readonly Color Gunmetal = new Color(0.24f, 0.25f, 0.27f);   // 프레임·핀·실린더 통
        public static readonly Color Rubber = new Color(0.07f, 0.07f, 0.08f);     // 타이어·무한궤도
        public static readonly Color Chrome = new Color(0.82f, 0.84f, 0.86f);     // 유압 실린더 로드
        public static readonly Color Lamp = new Color(1.00f, 0.95f, 0.75f);

        private readonly Dictionary<Color, Material> _materials = new Dictionary<Color, Material>();
        private readonly Shader _shader;

        public PrimitiveKit()
        {
            _shader = Shader.Find("Universal Render Pipeline/Lit");
            if (_shader == null)
            {
                throw new System.InvalidOperationException(
                    "셰이더 'Universal Render Pipeline/Lit'을 찾지 못했다. URP가 설치되어 있는지 확인한다.");
            }
        }

        private Material MaterialFor(Color color)
        {
            if (_materials.TryGetValue(color, out Material material)) return material;

            material = new Material(_shader) { name = "SiteModel (demo)" };
            material.SetColor("_BaseColor", color);
            // 유리와 크롬은 매끈하게(빛이 비친다), 나머지는 칠한 쇠처럼 조금만.
            material.SetFloat("_Smoothness", color == Glass ? 0.85f : (color == Chrome ? 0.7f : 0.2f));
            _materials[color] = material;
            return material;
        }

        public void DestroyMaterials()
        {
            foreach (Material material in _materials.Values) Object.Destroy(material);
            _materials.Clear();
        }

        /// <summary>빈 묶음 (자식들을 함께 옮기거나 돌리는 용도).</summary>
        public Transform Group(Transform parent, string name, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            return go.transform;
        }

        /// <summary>상자. center는 부모 기준 중심, size는 가로(X)·높이(Y)·세로(Z) (m).</summary>
        public Transform Box(Transform parent, string name, Vector3 center, Vector3 size, Color color)
        {
            return Primitive(PrimitiveType.Cube, parent, name, center, size, Quaternion.identity, color);
        }

        /// <summary>회전한 상자. euler는 부모 기준 각도(도).</summary>
        public Transform Box(Transform parent, string name, Vector3 center, Vector3 size, Vector3 euler, Color color)
        {
            return Primitive(PrimitiveType.Cube, parent, name, center, size, Quaternion.Euler(euler), color);
        }

        /// <summary>두 점을 잇는 각진 부재 (붐·암·트러스 부재). width = 옆 폭, height = 위아래 두께.</summary>
        public Transform Beam(Transform parent, string name, Vector3 from, Vector3 to, float width, float height, Color color)
        {
            Vector3 d = to - from;
            float length = d.magnitude;
            if (length < 1e-4f) length = 1e-4f;
            Vector3 up = Mathf.Abs(Vector3.Dot(d / length, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
            return Primitive(PrimitiveType.Cube, parent, name, (from + to) / 2f,
                new Vector3(width, height, length), Quaternion.LookRotation(d / length, up), color);
        }

        /// <summary>두 점을 잇는 둥근 막대 (배기관·줄·유압 실린더).</summary>
        public Transform Rod(Transform parent, string name, Vector3 from, Vector3 to, float diameter, Color color)
        {
            Vector3 d = to - from;
            float length = Mathf.Max(1e-4f, d.magnitude);
            return Primitive(PrimitiveType.Cylinder, parent, name, (from + to) / 2f,
                new Vector3(diameter, length / 2f, diameter), Quaternion.FromToRotation(Vector3.up, d / length), color);
        }

        /// <summary>유압 실린더: 굵은 통(from 쪽)과 가는 크롬 로드(to 쪽).</summary>
        public void Hydraulic(Transform parent, string name, Vector3 from, Vector3 to, float diameter)
        {
            Vector3 mid = Vector3.Lerp(from, to, 0.6f);
            Rod(parent, name + " 통", from, mid, diameter, Gunmetal);
            Rod(parent, name + " 로드", mid, to, diameter * 0.55f, Chrome);
        }

        /// <summary>
        /// 움직이는 유압 실린더 (굴착기 동작용, 2026-11-20). 통은 baseFrame에, 로드 끝은 endFrame에 붙어 있다.
        /// 관절이 움직인 뒤 Update()를 부르면 두 핀 사이로 다시 놓인다 (통 길이는 그대로, 로드가 늘었다 줄었다 한다).
        /// </summary>
        public HydraulicLink LinkHydraulic(Transform parent, string name, Transform baseFrame, Vector3 baseLocal,
            Transform endFrame, Vector3 endLocal, float diameter)
        {
            Transform barrel = Rod(parent, name + " 통", Vector3.zero, Vector3.up, diameter, Gunmetal);
            Transform rod = Rod(parent, name + " 로드", Vector3.zero, Vector3.up, diameter * 0.55f, Chrome);
            var link = new HydraulicLink(parent, barrel, rod, baseFrame, baseLocal, endFrame, endLocal, diameter);
            link.Update();
            return link;
        }

        /// <summary>이미 만든 원기둥을 부모 기준 두 점 사이에 다시 놓는다.</summary>
        public static void PlaceRod(Transform rod, Vector3 from, Vector3 to, float diameter)
        {
            Vector3 d = to - from;
            float length = Mathf.Max(1e-4f, d.magnitude);
            rod.localPosition = (from + to) / 2f;
            rod.localRotation = Quaternion.FromToRotation(Vector3.up, d / length);
            rod.localScale = new Vector3(diameter, length / 2f, diameter);
        }

        /// <summary>타이어: 검은 고무와 가운데 쇠 휠 (축이 X 방향).</summary>
        public void Tire(Transform parent, string name, Vector3 center, float diameter, float width)
        {
            Wheel(parent, name, center, diameter, width, Rubber);
            Wheel(parent, name + " 휠", center, diameter * 0.55f, width + 0.04f, Steel);
        }

        /// <summary>
        /// 무한궤도 한 줄: 가운데 상자와 양 끝의 둥근 바퀴(유도륜·구동륜)로 고리 모양을 만들고, 바깥에 롤러 프레임을 붙인다.
        /// x = 궤도 중심의 가로 위치, length = 앞뒤 길이, height = 높이(= 끝 바퀴 지름), width = 슈 폭.
        /// </summary>
        public void Track(Transform parent, string name, float x, float length, float height, float width)
        {
            float r = height / 2f, straight = length - height;
            Box(parent, name, new Vector3(x, r, 0f), new Vector3(width, height, straight), Rubber);
            Wheel(parent, name + " 유도륜", new Vector3(x, r, straight / 2f), height, width, Rubber);
            Wheel(parent, name + " 구동륜", new Vector3(x, r, -straight / 2f), height, width, Rubber);
            float outer = x + Mathf.Sign(x) * (width / 2f + 0.03f);
            Box(parent, name + " 롤러 프레임", new Vector3(outer, r, 0f), new Vector3(0.06f, height * 0.42f, straight), Gunmetal);
            Wheel(parent, name + " 유도륜 축", new Vector3(outer, r, straight / 2f), height * 0.5f, 0.08f, Steel);
            Wheel(parent, name + " 구동륜 축", new Vector3(outer, r, -straight / 2f), height * 0.6f, 0.08f, Steel);
        }

        /// <summary>바퀴·드럼처럼 옆으로 누운 원기둥 (축이 X 방향).</summary>
        public Transform Wheel(Transform parent, string name, Vector3 center, float diameter, float width, Color color)
        {
            // Unity의 원기둥은 높이가 2라서 Y 배율은 길이의 절반이다.
            return Primitive(PrimitiveType.Cylinder, parent, name, center,
                new Vector3(diameter, width / 2f, diameter), Quaternion.Euler(0f, 0f, 90f), color);
        }

        /// <summary>임의 방향의 원기둥. euler가 (0, 0, 0)이면 축이 Y 방향.</summary>
        public Transform Cylinder(Transform parent, string name, Vector3 center, float diameter, float length,
            Vector3 euler, Color color)
        {
            return Primitive(PrimitiveType.Cylinder, parent, name, center,
                new Vector3(diameter, length / 2f, diameter), Quaternion.Euler(euler), color);
        }

        private Transform Primitive(PrimitiveType type, Transform parent, string name, Vector3 center,
            Vector3 scale, Quaternion rotation, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());   // 충돌 계산은 쓰지 않는다
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localRotation = rotation;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = MaterialFor(color);
            return go.transform;
        }
    }

    /// <summary>움직이는 유압 실린더 하나 (PrimitiveKit.LinkHydraulic으로 만든다).</summary>
    internal sealed class HydraulicLink
    {
        private readonly Transform _parent, _barrel, _rod, _baseFrame, _endFrame;
        private readonly Vector3 _baseLocal, _endLocal;
        private readonly float _diameter, _barrelLength;

        public HydraulicLink(Transform parent, Transform barrel, Transform rod, Transform baseFrame, Vector3 baseLocal,
            Transform endFrame, Vector3 endLocal, float diameter)
        {
            _parent = parent; _barrel = barrel; _rod = rod;
            _baseFrame = baseFrame; _baseLocal = baseLocal; _endFrame = endFrame; _endLocal = endLocal;
            _diameter = diameter;
            GetPoints(out Vector3 a, out Vector3 b);
            _barrelLength = 0.55f * Vector3.Distance(a, b);   // 만든 자세 길이의 55 %
        }

        private void GetPoints(out Vector3 a, out Vector3 b)
        {
            a = _parent.InverseTransformPoint(_baseFrame.TransformPoint(_baseLocal));
            b = _parent.InverseTransformPoint(_endFrame.TransformPoint(_endLocal));
        }

        /// <summary>두 핀 사이로 다시 놓는다: 통은 base 쪽 고정 길이, 로드는 통 안(80 % 지점)에서 end 핀까지.</summary>
        public void Update()
        {
            GetPoints(out Vector3 a, out Vector3 b);
            float length = Vector3.Distance(a, b);
            if (length < 1e-4f) return;
            Vector3 dir = (b - a) / length;
            float barrel = Mathf.Min(_barrelLength, length * 0.95f);
            PrimitiveKit.PlaceRod(_barrel, a, a + dir * barrel, _diameter);
            PrimitiveKit.PlaceRod(_rod, a + dir * barrel * 0.8f, b, _diameter * 0.55f);
        }
    }

    /// <summary>굴착기의 움직이는 부분 묶음 (2026-11-20).</summary>
    internal sealed class ExcavatorRig
    {
        public Transform Root, Upper, Boom, Arm, Bucket;
        public readonly List<HydraulicLink> Links = new List<HydraulicLink>();

        /// <summary>
        /// 자세를 적용한다 (각도는 CivilTwin.Core.ExcavatorPose와 같은 뜻, 도).
        /// Unity에서 X축 + 회전은 앞(+Z)을 아래로 숙이므로: 붐 들기·암 펴기·버킷 열기는 모두 X축 − 회전이다.
        /// </summary>
        public void Pose(double swing, double boomRaise, double armOut, double bucketOpen)
        {
            Upper.localEulerAngles = new Vector3(0f, (float)swing, 0f);
            Boom.localEulerAngles = new Vector3(-(float)boomRaise, 0f, 0f);
            Arm.localEulerAngles = new Vector3(-(float)armOut, 0f, 0f);
            Bucket.localEulerAngles = new Vector3(-(float)bucketOpen, 0f, 0f);
            foreach (HydraulicLink link in Links) link.Update();
        }
    }

    /// <summary>
    /// 장비 모양 만들기. 움직일 부분(선회하는 윗몸, 드럼 등)은 out으로 돌려준다.
    ///
    /// 2026-11-20 다시 만듦: 상자 3 ~ 5개짜리 모양이 "장난감 같다"는 개발자 의견으로, 실제 장비의 구조를 따라 부품을 나눴다.
    ///   굴착기 = 하부 주행체(무한궤도·롤러 프레임) + 선회 베어링 + 상부 선회체(엔진룸·평형추·운전실·수납함) + 굽은 붐 + 암 + 버킷(이빨)
    ///            + 유압 실린더 3종(붐·암·버킷, 크롬 로드)
    ///   덤프트럭 = 앞 1축 + 뒤 2축(복륜) + 캡오버 운전실(앞유리·그릴·범퍼·등·거울) + 보강 늑골이 있는 적재함 + 흙 더미
    ///   불도저·롤러(단일 드럼 진동롤러)·레미콘·펌프카(아웃트리거)·타워크레인(격자 마스트·삼각 지브·캣헤드) 도 같은 방식.
    /// 치수는 보기용 어림값이다 (굴착기 약 30 t급, 덤프트럭 15 t급). 표준품셈의 장비 규격과 같은 급으로 맞췄지만 도면 치수는 아니다.
    /// </summary>
    internal static class EquipmentModels
    {
        /// <summary>굴착기 (30 t급, 하부 길이 4.6 m, 작업 자세 길이 약 10 m). 관절(선회·붐·암·버킷)은 rig.Pose로 움직인다.</summary>
        public static ExcavatorRig Excavator(PrimitiveKit kit, Transform parent)
        {
            Transform root = kit.Group(parent, "굴착기", Vector3.zero);

            // ---- 하부 주행체 ----
            kit.Track(root, "무한궤도 왼쪽", -1.3f, 4.6f, 1.0f, 0.6f);
            kit.Track(root, "무한궤도 오른쪽", 1.3f, 4.6f, 1.0f, 0.6f);
            kit.Box(root, "하부 몸통", new Vector3(0f, 0.65f, 0f), new Vector3(2.0f, 0.55f, 2.2f), PrimitiveKit.Gunmetal);
            kit.Cylinder(root, "선회 베어링", new Vector3(0f, 1.08f, 0f), 2.0f, 0.3f, Vector3.zero, PrimitiveKit.Gunmetal);

            // ---- 상부 선회체 (앞 = +Z, 운전실은 왼쪽 앞) ----
            Transform upper = kit.Group(root, "윗몸", new Vector3(0f, 1.25f, 0f));
            kit.Box(upper, "선회 프레임", new Vector3(0f, 0.15f, -0.45f), new Vector3(2.9f, 0.3f, 4.3f), PrimitiveKit.Gunmetal);
            kit.Box(upper, "엔진룸", new Vector3(0f, 0.8f, -1.45f), new Vector3(2.9f, 1.0f, 1.9f), PrimitiveKit.Yellow);
            kit.Box(upper, "엔진 덮개", new Vector3(0.25f, 1.32f, -1.45f), new Vector3(1.9f, 0.05f, 1.5f), PrimitiveKit.YellowDark);
            kit.Wheel(upper, "평형추", new Vector3(0f, 0.85f, -2.4f), 1.1f, 2.9f, PrimitiveKit.Yellow);
            kit.Box(upper, "평형추 아래", new Vector3(0f, 0.36f, -2.4f), new Vector3(2.9f, 0.12f, 1.0f), PrimitiveKit.Gunmetal);
            kit.Box(upper, "수납함", new Vector3(0.95f, 0.8f, 0.55f), new Vector3(1.0f, 1.0f, 1.9f), PrimitiveKit.Yellow);
            kit.Rod(upper, "배기관", new Vector3(0.9f, 1.3f, -1.0f), new Vector3(0.9f, 1.95f, -1.0f), 0.14f, PrimitiveKit.Gunmetal);
            kit.Rod(upper, "난간", new Vector3(1.4f, 1.55f, -2.0f), new Vector3(1.4f, 1.55f, -0.6f), 0.05f, PrimitiveKit.Yellow);

            // 운전실: 유리 상자에 짙은 지붕·기둥·뒷벽
            kit.Box(upper, "운전실 유리", new Vector3(-0.85f, 1.12f, 0.85f), new Vector3(0.96f, 1.6f, 1.66f), PrimitiveKit.Glass);
            kit.Box(upper, "운전실 지붕", new Vector3(-0.85f, 1.98f, 0.85f), new Vector3(1.06f, 0.1f, 1.78f), PrimitiveKit.Gunmetal);
            kit.Box(upper, "운전실 뒷벽", new Vector3(-0.85f, 1.12f, 0.02f), new Vector3(1.0f, 1.6f, 0.1f), PrimitiveKit.Yellow);
            kit.Box(upper, "운전실 아래", new Vector3(-0.85f, 0.42f, 0.85f), new Vector3(1.0f, 0.24f, 1.7f), PrimitiveKit.Gunmetal);
            foreach (float px in new[] { -1.33f, -0.37f })
                kit.Box(upper, "운전실 앞기둥", new Vector3(px, 1.12f, 1.66f), new Vector3(0.07f, 1.6f, 0.07f), PrimitiveKit.Gunmetal);

            // ---- 작업 장치: 굽은 붐 → 암 → 버킷 (관절 셋). 만든 자세 = 버킷 이빨이 땅에 닿은 굴착 자세 ----
            // 관절마다 묶음을 하나씩 두고 자식으로 잇는다: 윗몸 ⊃ 붐(발 핀) ⊃ 암(붐 끝 핀) ⊃ 버킷(암 끝 핀).
            var rig = new ExcavatorRig { Root = root, Upper = upper };
            var boomOrigin = new Vector3(0.15f, 0.75f, 1.3f);
            rig.Boom = kit.Group(upper, "붐", boomOrigin);
            var knee = new Vector3(0f, 2.6f, 2.7f);
            var tip = new Vector3(0f, 2.8f, 5.6f);
            kit.Beam(rig.Boom, "붐 아래", Vector3.zero, knee, 0.55f, 0.7f, PrimitiveKit.Yellow);
            kit.Beam(rig.Boom, "붐 위", knee, tip, 0.5f, 0.6f, PrimitiveKit.Yellow);
            kit.Wheel(rig.Boom, "붐 꺾임", knee, 0.72f, 0.55f, PrimitiveKit.Yellow);
            kit.Wheel(rig.Boom, "붐 핀", Vector3.zero, 0.5f, 0.75f, PrimitiveKit.Gunmetal);

            rig.Arm = kit.Group(rig.Boom, "암", tip);
            var bucketPin = new Vector3(0f, -3.4f, 0.9f);   // 암 기준 (붐 끝 핀에서)
            var armTop = new Vector3(0f, 0.7f, -0.25f);
            kit.Beam(rig.Arm, "암대", Vector3.zero, bucketPin, 0.45f, 0.5f, PrimitiveKit.Yellow);
            kit.Beam(rig.Arm, "암 머리", Vector3.zero, armTop, 0.45f, 0.5f, PrimitiveKit.Yellow);
            kit.Wheel(rig.Arm, "암 핀", Vector3.zero, 0.45f, 0.62f, PrimitiveKit.Gunmetal);

            // 버킷: 둥근 바닥(판 3장) + 옆판 2장 + 이빨 5개. 입이 굴착기 쪽을 향해 말려 있다.
            rig.Bucket = kit.Group(rig.Arm, "버킷", bucketPin);
            var b1 = new Vector3(0f, -0.5f, 0.45f);
            var b2 = new Vector3(0f, -1.05f, 0.35f);
            var b3 = new Vector3(0f, -1.25f, -0.15f);
            kit.Beam(rig.Bucket, "버킷 등", Vector3.zero, b1, 1.3f, 0.12f, PrimitiveKit.Gunmetal);
            kit.Beam(rig.Bucket, "버킷 바닥", b1, b2, 1.3f, 0.12f, PrimitiveKit.Gunmetal);
            kit.Beam(rig.Bucket, "버킷 날", b2, b3, 1.3f, 0.12f, PrimitiveKit.Gunmetal);
            foreach (float sx in new[] { -0.63f, 0.63f })
                kit.Box(rig.Bucket, "버킷 옆판", new Vector3(sx, -0.62f, 0.15f), new Vector3(0.06f, 1.05f, 0.75f), PrimitiveKit.Gunmetal);
            for (int i = 0; i < 5; i++)
            {
                var x = new Vector3(-0.5f + i * 0.25f, 0f, 0f);
                kit.Beam(rig.Bucket, "버킷 이빨", b3 + x, b3 + x + new Vector3(0f, -0.08f, -0.28f), 0.1f, 0.1f, PrimitiveKit.Steel);
            }

            // 유압 실린더: 붐 2개(윗몸 → 붐 아래), 암(붐 위 → 암 머리), 버킷(암 머리 → 버킷 링크)
            foreach (float cx in new[] { -0.42f, 0.42f })
                rig.Links.Add(kit.LinkHydraulic(upper, "붐 실린더", upper, boomOrigin + new Vector3(cx, -0.4f, 0.3f),
                    rig.Boom, new Vector3(cx, 1.55f, 2.0f), 0.22f));
            rig.Links.Add(kit.LinkHydraulic(upper, "암 실린더", rig.Boom, new Vector3(0f, 3.05f, 2.9f), rig.Arm, armTop, 0.2f));
            rig.Links.Add(kit.LinkHydraulic(upper, "버킷 실린더", rig.Arm, new Vector3(0f, 0.5f, -0.1f),
                rig.Bucket, new Vector3(0f, 0.3f, 0.25f), 0.16f));
            return rig;
        }

        /// <summary>
        /// 트럭 운전실 (캡오버형, 앞이 frontZ). 덤프트럭·레미콘·펌프카가 함께 쓴다.
        /// 바닥 높이 1.35 m, 지붕 3.3 m, 폭 2.45 m, 길이 1.85 m.
        /// </summary>
        private static void TruckCab(PrimitiveKit kit, Transform root, float frontZ, Color body)
        {
            float cz = frontZ - 0.925f;
            kit.Box(root, "운전실", new Vector3(0f, 2.32f, cz), new Vector3(2.45f, 1.95f, 1.85f), body);
            kit.Box(root, "앞유리", new Vector3(0f, 2.78f, frontZ + 0.01f), new Vector3(2.15f, 0.85f, 0.04f), PrimitiveKit.Glass);
            kit.Box(root, "앞유리 아래 띠", new Vector3(0f, 2.28f, frontZ + 0.01f), new Vector3(2.45f, 0.1f, 0.04f), PrimitiveKit.Gunmetal);
            kit.Box(root, "그릴", new Vector3(0f, 1.78f, frontZ + 0.01f), new Vector3(1.7f, 0.55f, 0.04f), PrimitiveKit.Gunmetal);
            kit.Box(root, "범퍼", new Vector3(0f, 1.08f, frontZ + 0.05f), new Vector3(2.5f, 0.35f, 0.3f), PrimitiveKit.Gunmetal);
            kit.Box(root, "햇빛 가리개", new Vector3(0f, 3.26f, frontZ + 0.12f), new Vector3(2.3f, 0.07f, 0.3f), PrimitiveKit.Gunmetal);
            foreach (float side in new[] { -1f, 1f })
            {
                kit.Box(root, "옆유리", new Vector3(side * 1.235f, 2.78f, frontZ - 0.6f), new Vector3(0.04f, 0.75f, 0.95f), PrimitiveKit.Glass);
                kit.Box(root, "전조등", new Vector3(side * 0.95f, 1.12f, frontZ + 0.21f), new Vector3(0.38f, 0.16f, 0.03f), PrimitiveKit.Lamp);
                kit.Rod(root, "거울 팔", new Vector3(side * 1.22f, 3.0f, frontZ - 0.25f), new Vector3(side * 1.47f, 3.0f, frontZ - 0.1f), 0.04f, PrimitiveKit.Gunmetal);
                kit.Box(root, "거울", new Vector3(side * 1.5f, 2.8f, frontZ - 0.1f), new Vector3(0.06f, 0.45f, 0.2f), PrimitiveKit.Gunmetal);
                kit.Box(root, "발판", new Vector3(side * 1.12f, 1.0f, frontZ - 1.2f), new Vector3(0.3f, 0.07f, 0.5f), PrimitiveKit.Gunmetal);
            }
        }

        /// <summary>차대 (두 줄의 세로 보)</summary>
        private static void TruckFrame(PrimitiveKit kit, Transform root, float length, float centerZ)
        {
            kit.Box(root, "차대 왼쪽", new Vector3(-0.48f, 0.95f, centerZ), new Vector3(0.22f, 0.3f, length), PrimitiveKit.Gunmetal);
            kit.Box(root, "차대 오른쪽", new Vector3(0.48f, 0.95f, centerZ), new Vector3(0.22f, 0.3f, length), PrimitiveKit.Gunmetal);
        }

        /// <summary>바퀴: 앞축은 홑바퀴, 뒤축은 복륜(한쪽 두 개).</summary>
        private static void TruckWheels(PrimitiveKit kit, Transform root, float[] frontZ, float[] rearZ)
        {
            const float d = 1.05f;
            foreach (float z in frontZ)
                foreach (float side in new[] { -1f, 1f })
                    kit.Tire(root, "앞바퀴", new Vector3(side * 1.06f, d / 2f, z), d, 0.36f);
            foreach (float z in rearZ)
                foreach (float side in new[] { -1f, 1f })
                {
                    kit.Tire(root, "뒷바퀴 안", new Vector3(side * 0.8f, d / 2f, z), d, 0.28f);
                    kit.Tire(root, "뒷바퀴 밖", new Vector3(side * 1.11f, d / 2f, z), d, 0.28f);
                }
        }

        /// <summary>덤프트럭 모델 안의 실은 흙 묶음 이름 (SetTruckSoil로 양을 바꾼다)</summary>
        public const string SoilGroupName = "적재 흙";

        /// <summary>덤프트럭 모델 안의 적재함 묶음 이름 (뒤쪽 경첩을 중심으로 돈다)</summary>
        public const string BedGroupName = "적재함";

        /// <summary>적재함을 다 들었을 때의 각도 (도). 보기용 어림값.</summary>
        public const float MaxBedTilt = 50f;

        /// <summary>덤프트럭에 실린 흙의 양 (0 = 빈 차, 1 = 가득)</summary>
        public static void SetTruckSoil(Transform truck, double fraction)
        {
            Transform soil = truck.Find(BedGroupName + "/" + SoilGroupName);
            if (soil == null) soil = truck.Find(SoilGroupName);
            if (soil == null) return;
            float f = Mathf.Clamp01((float)fraction);
            soil.gameObject.SetActive(f > 0.01f);
            soil.localScale = new Vector3(1f, Mathf.Max(0.01f, f), 1f);
        }

        /// <summary>적재함을 든 정도 (0 = 내림, 1 = MaxBedTilt까지 듦). 2026-11-20: 흙 부리기 동작.</summary>
        public static void SetTruckBed(Transform truck, double raise)
        {
            var rig = truck.GetComponent<DumpBedRig>();
            if (rig != null) rig.Raise((float)raise);
        }

        /// <summary>덤프트럭 (15 t급, 3축, 길이 약 8.4 m).</summary>
        public static Transform DumpTruck(PrimitiveKit kit, Transform parent)
        {
            Transform root = kit.Group(parent, "덤프트럭", Vector3.zero);
            TruckFrame(kit, root, 7.8f, 0.2f);
            TruckCab(kit, root, 4.22f, PrimitiveKit.White);
            kit.Cylinder(root, "연료탱크", new Vector3(-1.0f, 1.05f, 1.3f), 0.6f, 1.1f, new Vector3(90f, 0f, 0f), PrimitiveKit.Steel);

            // 적재함: 바닥·옆판·앞판·뒷문, 위 테두리와 옆 보강 늑골.
            // 2026-11-20: 흙을 부릴 때 들 수 있게 뒤쪽 경첩(H)을 중심으로 한 묶음 "적재함"에 넣었다. 위치는 경첩 기준(− H).
            const float bz = -1.2f, bl = 5.4f;
            Vector3 h = new Vector3(0f, 1.4f, -3.8f);
            Transform bed = kit.Group(root, BedGroupName, h);
            kit.Box(bed, "적재함 바닥", new Vector3(0f, 1.45f, bz) - h, new Vector3(2.45f, 0.15f, bl), PrimitiveKit.OrangeDark);
            foreach (float side in new[] { -1f, 1f })
            {
                kit.Box(bed, "적재함 옆판", new Vector3(side * 1.2f, 2.1f, bz) - h, new Vector3(0.1f, 1.2f, bl), PrimitiveKit.Orange);
                kit.Box(bed, "적재함 테두리", new Vector3(side * 1.23f, 2.73f, bz) - h, new Vector3(0.16f, 0.12f, bl + 0.1f), PrimitiveKit.OrangeDark);
                foreach (float rz in new[] { -3.3f, -2.0f, -0.7f, 0.6f })
                    kit.Box(bed, "적재함 늑골", new Vector3(side * 1.27f, 2.1f, rz) - h, new Vector3(0.07f, 1.2f, 0.14f), PrimitiveKit.OrangeDark);
            }
            kit.Box(bed, "적재함 앞판", new Vector3(0f, 2.22f, bz + bl / 2f) - h, new Vector3(2.5f, 1.45f, 0.12f), PrimitiveKit.Orange);
            // 뒷문: 위쪽 경첩에 매달려 적재함을 들어도 아래로 늘어진다 (흙이 빠져나가는 길)
            Transform gate = kit.Group(bed, "적재함 뒷문 경첩", new Vector3(0f, 2.7f, bz - bl / 2f) - h);
            kit.Box(gate, "적재함 뒷문", new Vector3(0f, -0.6f, 0f), new Vector3(2.5f, 1.2f, 0.12f), PrimitiveKit.Orange);
            // 실은 흙: 묶음 "적재 흙"의 높이 배율(0 ~ 1)로 얼마나 찼는지 보여 준다 (바닥 1.52 m에서 위로 찬다).
            Transform soil = kit.Group(bed, SoilGroupName, new Vector3(0f, 1.52f, bz) - h);
            kit.Box(soil, "흙", new Vector3(0f, 1.03f, 0f), new Vector3(2.3f, 0.35f, bl - 0.3f), PrimitiveKit.Soil);
            kit.Box(soil, "흙 아래", new Vector3(0f, 0.43f, 0f), new Vector3(2.3f, 0.86f, bl - 0.3f), PrimitiveKit.Soil);
            kit.Box(soil, "흙 더미", new Vector3(0f, 0.98f, 0f), new Vector3(1.1f, 1.1f, bl - 0.8f), new Vector3(0f, 0f, 45f), PrimitiveKit.Soil);

            // 호이스트 실린더: 차대 앞쪽에서 적재함 앞쪽 밑으로. 다 든 자세에서 만들어 통 길이를 정한 뒤 내린다.
            var rig = root.gameObject.AddComponent<DumpBedRig>();
            rig.Bed = bed;
            rig.Gate = gate;
            bed.localEulerAngles = new Vector3(-MaxBedTilt, 0f, 0f);
            rig.Hoist = kit.LinkHydraulic(root, "호이스트 실린더", root, new Vector3(0f, 1.05f, 0.9f),
                bed, new Vector3(0f, -0.02f, 4.6f), 0.28f);
            rig.Raise(0f);

            kit.Box(root, "흙받이", new Vector3(-0.97f, 1.18f, -2.55f), new Vector3(0.75f, 0.05f, 2.6f), PrimitiveKit.Gunmetal);
            kit.Box(root, "흙받이", new Vector3(0.97f, 1.18f, -2.55f), new Vector3(0.75f, 0.05f, 2.6f), PrimitiveKit.Gunmetal);
            TruckWheels(kit, root, new[] { 3.0f }, new[] { -1.9f, -3.2f });
            return root;
        }

        /// <summary>불도저 (D6급, 배토판 포함 길이 약 5 m).</summary>
        public static Transform Bulldozer(PrimitiveKit kit, Transform parent)
        {
            Transform root = kit.Group(parent, "불도저", Vector3.zero);
            kit.Track(root, "무한궤도 왼쪽", -1.0f, 3.3f, 0.85f, 0.5f);
            kit.Track(root, "무한궤도 오른쪽", 1.0f, 3.3f, 0.85f, 0.5f);
            kit.Box(root, "궤도 덮개 왼쪽", new Vector3(-1.0f, 0.9f, 0f), new Vector3(0.62f, 0.06f, 3.0f), PrimitiveKit.Yellow);
            kit.Box(root, "궤도 덮개 오른쪽", new Vector3(1.0f, 0.9f, 0f), new Vector3(0.62f, 0.06f, 3.0f), PrimitiveKit.Yellow);
            kit.Box(root, "차체", new Vector3(0f, 0.75f, 0f), new Vector3(1.4f, 0.5f, 3.0f), PrimitiveKit.Gunmetal);

            kit.Box(root, "엔진 덮개", new Vector3(0f, 1.45f, 0.55f), new Vector3(1.3f, 0.95f, 1.7f), PrimitiveKit.Yellow);
            kit.Box(root, "라디에이터 그릴", new Vector3(0f, 1.4f, 1.41f), new Vector3(1.15f, 0.75f, 0.04f), PrimitiveKit.Gunmetal);
            kit.Rod(root, "배기관", new Vector3(0.35f, 1.9f, 0.9f), new Vector3(0.35f, 2.5f, 0.9f), 0.12f, PrimitiveKit.Gunmetal);

            kit.Box(root, "운전실 받침", new Vector3(0f, 1.15f, -0.9f), new Vector3(1.6f, 0.35f, 1.3f), PrimitiveKit.Yellow);
            kit.Box(root, "운전실 유리", new Vector3(0f, 1.95f, -0.9f), new Vector3(1.4f, 1.3f, 1.2f), PrimitiveKit.Glass);
            kit.Box(root, "운전실 지붕", new Vector3(0f, 2.66f, -0.9f), new Vector3(1.6f, 0.12f, 1.45f), PrimitiveKit.Yellow);
            foreach (float px in new[] { -0.7f, 0.7f })
                foreach (float pz in new[] { -0.3f, -1.5f })
                    kit.Box(root, "운전실 기둥", new Vector3(px, 1.95f, pz), new Vector3(0.08f, 1.3f, 0.08f), PrimitiveKit.Gunmetal);

            // 배토판: 아래는 뒤로, 위는 앞으로 기운 두 장으로 굽은 면을 흉내 낸다.
            kit.Box(root, "배토판 아래", new Vector3(0f, 0.42f, 2.42f), new Vector3(3.2f, 0.65f, 0.14f), new Vector3(-8f, 0f, 0f), PrimitiveKit.Yellow);
            kit.Box(root, "배토판 위", new Vector3(0f, 1.0f, 2.42f), new Vector3(3.2f, 0.6f, 0.14f), new Vector3(18f, 0f, 0f), PrimitiveKit.Yellow);
            kit.Box(root, "배토판 뒤 보강", new Vector3(0f, 0.75f, 2.27f), new Vector3(3.0f, 0.9f, 0.1f), PrimitiveKit.Gunmetal);
            kit.Box(root, "절삭 날", new Vector3(0f, 0.07f, 2.48f), new Vector3(3.2f, 0.12f, 0.18f), PrimitiveKit.Steel);
            foreach (float side in new[] { -1f, 1f })
            {
                kit.Box(root, "배토판 끝", new Vector3(side * 1.6f, 0.62f, 2.38f), new Vector3(0.1f, 1.1f, 0.4f), PrimitiveKit.Yellow);
                kit.Beam(root, "밀대", new Vector3(side * 1.0f, 0.55f, -0.2f), new Vector3(side * 1.1f, 0.5f, 2.2f), 0.2f, 0.3f, PrimitiveKit.Yellow);
                kit.Hydraulic(root, "배토판 실린더", new Vector3(side * 0.6f, 1.75f, 1.3f), new Vector3(side * 0.8f, 1.05f, 2.2f), 0.16f);
            }

            // 리퍼 (뒤쪽 갈고리)
            kit.Box(root, "리퍼 틀", new Vector3(0f, 0.95f, -1.85f), new Vector3(1.3f, 0.25f, 0.3f), PrimitiveKit.Gunmetal);
            kit.Beam(root, "리퍼 생크", new Vector3(0f, 1.0f, -1.95f), new Vector3(0f, 0.1f, -2.25f), 0.15f, 0.3f, PrimitiveKit.Gunmetal);
            return root;
        }

        /// <summary>롤러 (단일 드럼 진동롤러 10 t급: 앞은 쇠 드럼, 뒤는 고무 타이어, 가운데가 꺾인다. 길이 약 5.8 m).</summary>
        public static Transform Roller(PrimitiveKit kit, Transform parent)
        {
            Transform root = kit.Group(parent, "롤러", Vector3.zero);
            kit.Wheel(root, "다짐 드럼", new Vector3(0f, 0.75f, 1.75f), 1.5f, 2.1f, PrimitiveKit.Steel);
            foreach (float side in new[] { -1f, 1f })
                kit.Box(root, "드럼 틀 옆", new Vector3(side * 1.15f, 0.95f, 1.75f), new Vector3(0.14f, 1.15f, 1.9f), PrimitiveKit.Yellow);
            kit.Box(root, "드럼 틀 위", new Vector3(0f, 1.62f, 1.75f), new Vector3(2.44f, 0.22f, 0.6f), PrimitiveKit.Yellow);
            kit.Box(root, "꺾임 관절", new Vector3(0f, 1.05f, 0.55f), new Vector3(0.6f, 0.5f, 0.7f), PrimitiveKit.Gunmetal);

            kit.Box(root, "뒷몸(엔진)", new Vector3(0f, 1.4f, -1.45f), new Vector3(1.6f, 0.95f, 2.3f), PrimitiveKit.Yellow);
            kit.Box(root, "뒤 그릴", new Vector3(0f, 1.4f, -2.61f), new Vector3(1.4f, 0.6f, 0.04f), PrimitiveKit.Gunmetal);
            kit.Rod(root, "배기관", new Vector3(0.55f, 1.85f, -2.0f), new Vector3(0.55f, 2.45f, -2.0f), 0.1f, PrimitiveKit.Gunmetal);
            foreach (float side in new[] { -1f, 1f })
                kit.Tire(root, "뒷바퀴", new Vector3(side * 1.08f, 0.7f, -1.55f), 1.4f, 0.55f);

            kit.Box(root, "운전 발판", new Vector3(0f, 1.9f, -0.45f), new Vector3(1.7f, 0.12f, 1.3f), PrimitiveKit.Gunmetal);
            kit.Box(root, "운전실 유리", new Vector3(0f, 2.6f, -0.45f), new Vector3(1.3f, 1.25f, 1.15f), PrimitiveKit.Glass);
            kit.Box(root, "운전실 지붕", new Vector3(0f, 3.28f, -0.45f), new Vector3(1.5f, 0.12f, 1.4f), PrimitiveKit.Yellow);
            foreach (float px in new[] { -0.65f, 0.65f })
                foreach (float pz in new[] { 0.1f, -1.0f })
                    kit.Box(root, "운전실 기둥", new Vector3(px, 2.6f, pz), new Vector3(0.08f, 1.3f, 0.08f), PrimitiveKit.Gunmetal);
            return root;
        }

        /// <summary>레미콘 트럭 (6 m³급, 길이 약 8.6 m). drum = 도는 통.</summary>
        public static Transform MixerTruck(PrimitiveKit kit, Transform parent, out Transform drum)
        {
            Transform root = kit.Group(parent, "레미콘 트럭", Vector3.zero);
            TruckFrame(kit, root, 8.0f, 0.1f);
            TruckCab(kit, root, 4.3f, PrimitiveKit.White);
            kit.Cylinder(root, "물탱크", new Vector3(0f, 2.0f, 2.0f), 0.6f, 1.6f, new Vector3(0f, 0f, 90f), PrimitiveKit.White);

            // 통은 뒤쪽이 들린 채로 누워 있다 (받침 −78°: 통의 +Y 끝이 뒤 위). 통의 축(Y)을 중심으로 돌린다.
            Transform tilt = kit.Group(root, "통 받침", new Vector3(0f, 2.55f, -1.0f));
            tilt.localEulerAngles = new Vector3(-78f, 0f, 0f);
            drum = kit.Group(tilt, "통", Vector3.zero);
            kit.Cylinder(drum, "통 몸", Vector3.zero, 2.3f, 3.4f, Vector3.zero, PrimitiveKit.White);
            kit.Cylinder(drum, "통 앞 (좁아짐)", new Vector3(0f, -2.05f, 0f), 1.7f, 0.7f, Vector3.zero, PrimitiveKit.White);
            kit.Cylinder(drum, "통 뒤 (좁아짐)", new Vector3(0f, 2.15f, 0f), 1.6f, 1.0f, Vector3.zero, PrimitiveKit.White);
            kit.Cylinder(drum, "통 입구", new Vector3(0f, 2.8f, 0f), 0.8f, 0.35f, Vector3.zero, PrimitiveKit.Gunmetal);
            kit.Box(drum, "줄무늬", new Vector3(0f, 0f, 1.13f), new Vector3(0.5f, 3.3f, 0.1f), PrimitiveKit.Red);
            kit.Box(drum, "줄무늬 반대", new Vector3(0f, 0f, -1.13f), new Vector3(0.5f, 3.3f, 0.1f), PrimitiveKit.Red);

            kit.Box(root, "통 앞 받침대", new Vector3(0f, 1.75f, 1.0f), new Vector3(0.5f, 1.2f, 0.5f), PrimitiveKit.Gunmetal);
            kit.Box(root, "통 뒤 받침대", new Vector3(0f, 1.9f, -3.0f), new Vector3(1.6f, 1.3f, 0.4f), PrimitiveKit.Gunmetal);
            kit.Box(root, "투입구(호퍼)", new Vector3(0f, 3.45f, -3.95f), new Vector3(0.8f, 0.5f, 0.6f), PrimitiveKit.Gunmetal);
            kit.Beam(root, "배출 슈트", new Vector3(0f, 2.9f, -4.05f), new Vector3(0f, 2.2f, -4.7f), 0.45f, 0.12f, PrimitiveKit.Steel);
            TruckWheels(kit, root, new[] { 3.05f }, new[] { -1.9f, -3.2f });
            return root;
        }

        /// <summary>
        /// 펌프카 (4축, 길이 약 10.5 m). 붐은 차의 왼쪽(−X)으로 뻗는다.
        /// reachUp = 붐이 올라가야 할 높이, reachOut = 차에서 부을 자리까지의 수평거리 (m).
        /// </summary>
        public static Transform PumpTruck(PrimitiveKit kit, Transform parent, float reachUp, float reachOut)
        {
            Transform root = kit.Group(parent, "펌프카", Vector3.zero);
            TruckFrame(kit, root, 9.8f, 0f);
            TruckCab(kit, root, 5.1f, PrimitiveKit.White);
            kit.Box(root, "펌프 몸체", new Vector3(0f, 1.75f, -1.2f), new Vector3(2.3f, 1.0f, 5.4f), PrimitiveKit.Red);
            kit.Box(root, "공구함", new Vector3(0f, 1.55f, 2.4f), new Vector3(2.3f, 0.8f, 1.2f), PrimitiveKit.Red);
            kit.Box(root, "호퍼", new Vector3(0f, 1.35f, -4.6f), new Vector3(1.8f, 0.9f, 0.9f), PrimitiveKit.Steel);

            // 아웃트리거 (차가 넘어지지 않게 옆으로 펴는 다리)
            foreach (float side in new[] { -1f, 1f })
            {
                kit.Beam(root, "앞 아웃트리거", new Vector3(side * 1.0f, 0.95f, 1.8f), new Vector3(side * 3.4f, 0.95f, 2.8f), 0.3f, 0.3f, PrimitiveKit.Red);
                kit.Box(root, "앞 받침", new Vector3(side * 3.4f, 0.48f, 2.8f), new Vector3(0.25f, 0.95f, 0.25f), PrimitiveKit.Gunmetal);
                kit.Box(root, "앞 받침판", new Vector3(side * 3.4f, 0.04f, 2.8f), new Vector3(0.7f, 0.08f, 0.7f), PrimitiveKit.Steel);
                kit.Beam(root, "뒤 아웃트리거", new Vector3(side * 1.0f, 0.95f, -3.3f), new Vector3(side * 3.4f, 0.95f, -4.1f), 0.3f, 0.3f, PrimitiveKit.Red);
                kit.Box(root, "뒤 받침", new Vector3(side * 3.4f, 0.48f, -4.1f), new Vector3(0.25f, 0.95f, 0.25f), PrimitiveKit.Gunmetal);
                kit.Box(root, "뒤 받침판", new Vector3(side * 3.4f, 0.04f, -4.1f), new Vector3(0.7f, 0.08f, 0.7f), PrimitiveKit.Steel);
            }
            TruckWheels(kit, root, new[] { 4.0f, 2.75f }, new[] { -2.3f, -3.6f });

            // 붐: 회전대 위에 세운 첫 마디, 부을 자리 쪽으로 뻗는 둘째 마디, 아래로 늘어뜨린 호스.
            kit.Cylinder(root, "회전대", new Vector3(0f, 2.45f, 1.5f), 1.2f, 0.5f, Vector3.zero, PrimitiveKit.Gunmetal);
            Transform pivot = kit.Group(root, "붐", new Vector3(0f, 2.7f, 1.5f));
            pivot.localEulerAngles = new Vector3(0f, -90f, 0f);          // 차의 왼쪽을 향한다
            float lean = Mathf.Min(3f, reachOut * 0.3f);                  // 첫 마디가 앞으로 기운 수평거리
            var elbow = new Vector3(0f, reachUp, lean);
            kit.Beam(pivot, "첫 마디", Vector3.zero, elbow, 0.4f, 0.5f, PrimitiveKit.Red);
            kit.Hydraulic(pivot, "첫 마디 실린더", new Vector3(0f, 0.3f, 0.9f), Vector3.Lerp(Vector3.zero, elbow, 0.45f) + new Vector3(0f, 0f, 0.35f), 0.2f);
            kit.Wheel(pivot, "꺾임", elbow, 0.5f, 0.45f, PrimitiveKit.Gunmetal);

            float second = Mathf.Max(2f, reachOut - lean);
            var end = new Vector3(0f, reachUp - 0.3f, lean + second);
            kit.Beam(pivot, "둘째 마디", elbow, end, 0.32f, 0.42f, PrimitiveKit.Red);
            kit.Rod(pivot, "수송관", elbow + new Vector3(0.25f, 0.15f, 0f), end + new Vector3(0.25f, 0.15f, 0f), 0.13f, PrimitiveKit.Steel);
            kit.Rod(pivot, "호스", end, end + new Vector3(0f, -3.2f, 0f), 0.18f, PrimitiveKit.Rubber);
            return root;
        }

        /// <summary>
        /// 타워크레인 (격자 마스트, 삼각 지브, 캣헤드와 당김줄).
        /// mast = 높이가 자라는 기둥(Y 배율 0 ~ 1), top = 꼭대기에서 도는 팔 묶음 (지브는 +Z).
        /// </summary>
        public static Transform TowerCrane(PrimitiveKit kit, Transform parent, float height, float jibLength,
            out Transform mast, out Transform top)
        {
            Transform root = kit.Group(parent, "타워크레인", Vector3.zero);
            kit.Box(root, "기초", new Vector3(0f, 0.4f, 0f), new Vector3(5.0f, 0.8f, 5.0f), PrimitiveKit.Concrete);

            // 마스트: 네 모서리 기둥 + 면마다 지그재그 사재
            mast = kit.Group(root, "기둥", new Vector3(0f, 0.8f, 0f));
            const float h = 0.8f;
            foreach (float cx in new[] { -h, h })
                foreach (float cz in new[] { -h, h })
                    kit.Box(mast, "기둥 모서리", new Vector3(cx, height / 2f, cz), new Vector3(0.22f, height, 0.22f), PrimitiveKit.Yellow);
            int sections = Mathf.Max(1, Mathf.RoundToInt(height / 2.5f));
            float seg = height / sections;
            for (int i = 0; i < sections; i++)
            {
                float y0 = i * seg, y1 = (i + 1) * seg;
                float a = i % 2 == 0 ? -h : h;   // 위아래로 번갈아 꺾인다
                kit.Beam(mast, "사재", new Vector3(a, y0, h), new Vector3(-a, y1, h), 0.1f, 0.1f, PrimitiveKit.Yellow);
                kit.Beam(mast, "사재", new Vector3(-a, y0, -h), new Vector3(a, y1, -h), 0.1f, 0.1f, PrimitiveKit.Yellow);
                kit.Beam(mast, "사재", new Vector3(h, y0, a), new Vector3(h, y1, -a), 0.1f, 0.1f, PrimitiveKit.Yellow);
                kit.Beam(mast, "사재", new Vector3(-h, y0, -a), new Vector3(-h, y1, a), 0.1f, 0.1f, PrimitiveKit.Yellow);
            }

            top = kit.Group(root, "팔", new Vector3(0f, 0.8f + height, 0f));
            kit.Box(top, "회전대", new Vector3(0f, 0.3f, 0f), new Vector3(2.2f, 0.6f, 2.2f), PrimitiveKit.Gunmetal);
            kit.Box(top, "운전실", new Vector3(1.55f, 0.85f, 0.7f), new Vector3(1.2f, 1.5f, 1.5f), PrimitiveKit.Glass);
            kit.Box(top, "운전실 지붕", new Vector3(1.55f, 1.65f, 0.7f), new Vector3(1.3f, 0.1f, 1.6f), PrimitiveKit.Yellow);

            // 캣헤드 (꼭대기 뾰족탑)
            var apex = new Vector3(0f, 7.0f, 0f);
            foreach (float cx in new[] { -h, h })
                foreach (float cz in new[] { -h, h })
                    kit.Rod(top, "캣헤드", new Vector3(cx, 0.6f, cz), apex, 0.2f, PrimitiveKit.Yellow);

            // 지브: 아래 두 줄 + 위 한 줄의 삼각 트러스
            const float jw = 0.6f, jy0 = 0.9f, jy1 = 2.2f;
            foreach (float side in new[] { -jw, jw })
                kit.Box(top, "지브 아래 현", new Vector3(side, jy0, jibLength / 2f), new Vector3(0.22f, 0.22f, jibLength), PrimitiveKit.Yellow);
            kit.Box(top, "지브 위 현", new Vector3(0f, jy1, jibLength / 2f), new Vector3(0.22f, 0.22f, jibLength), PrimitiveKit.Yellow);
            int panels = Mathf.Max(2, Mathf.RoundToInt(jibLength / 3f));
            float panel = jibLength / panels;
            for (int j = 0; j < panels; j++)
            {
                float z0 = j * panel, z1 = (j + 1) * panel;
                foreach (float side in new[] { -jw, jw })
                {
                    kit.Beam(top, "지브 사재", new Vector3(side, jy0, z0), new Vector3(0f, jy1, z0 + panel / 2f), 0.1f, 0.1f, PrimitiveKit.Yellow);
                    kit.Beam(top, "지브 사재", new Vector3(0f, jy1, z0 + panel / 2f), new Vector3(side, jy0, z1), 0.1f, 0.1f, PrimitiveKit.Yellow);
                }
            }

            // 카운터 지브와 평형추
            const float cl = 12f;
            foreach (float side in new[] { -jw, jw })
                kit.Box(top, "카운터 지브", new Vector3(side, 0.95f, -cl / 2f), new Vector3(0.18f, 0.3f, cl), PrimitiveKit.Yellow);
            kit.Box(top, "통로", new Vector3(0f, 0.82f, -cl / 2f), new Vector3(1.4f, 0.06f, cl), PrimitiveKit.Gunmetal);
            kit.Box(top, "평형추", new Vector3(0f, -0.1f, -cl + 1.6f), new Vector3(2.0f, 2.2f, 2.6f), PrimitiveKit.ConcreteDark);

            // 당김줄: 캣헤드 꼭대기에서 지브와 카운터 지브로
            kit.Rod(top, "당김줄 앞", apex, new Vector3(0f, jy1, jibLength * 0.65f), 0.06f, PrimitiveKit.Gunmetal);
            kit.Rod(top, "당김줄 뒤", apex, new Vector3(0f, 1.0f, -cl + 0.5f), 0.06f, PrimitiveKit.Gunmetal);

            // 트롤리·와이어·훅·인양 자재
            float tz = jibLength * 0.6f;
            kit.Box(top, "트롤리", new Vector3(0f, 0.6f, tz), new Vector3(1.4f, 0.4f, 1.0f), PrimitiveKit.Gunmetal);
            kit.Rod(top, "와이어", new Vector3(0f, 0.4f, tz), new Vector3(0f, -7.0f, tz), 0.05f, PrimitiveKit.Gunmetal);
            kit.Box(top, "훅 블록", new Vector3(0f, -7.4f, tz), new Vector3(0.6f, 0.8f, 0.5f), PrimitiveKit.Red);
            kit.Rod(top, "줄걸이", new Vector3(0f, -7.8f, tz), new Vector3(0f, -8.1f, tz + 1.8f), 0.04f, PrimitiveKit.Gunmetal);
            kit.Rod(top, "줄걸이", new Vector3(0f, -7.8f, tz), new Vector3(0f, -8.1f, tz - 1.8f), 0.04f, PrimitiveKit.Gunmetal);
            kit.Box(top, "자재", new Vector3(0f, -8.35f, tz), new Vector3(1.2f, 0.5f, 4.0f), PrimitiveKit.Steel);
            return root;
        }
    }
}
