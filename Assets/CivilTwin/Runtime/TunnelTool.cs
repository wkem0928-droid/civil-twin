using System.Collections.Generic;
using System.Globalization;
using CivilTwin.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CivilTwin.Runtime
{
    /// <summary>
    /// 터널 굴착 4D (2026-11-21, 회사 구조물 ③: 대우건설 인제양양터널). 터널 현장에서 [부지] → "○○터널 굴착 (NATM)".
    ///
    /// 계산은 Core/TunnelStructures(TunnelPlan): 위성 지형의 토피 → 지보 패턴 배정 → 표준품셈 C군 싸이클 → 막장 공정.
    /// 화면:
    ///   - 지형 위의 띠: 터널 두 튜브의 평면 위치. 판 곳은 지보 패턴 색, 아직 안 판 곳은 회색 (터널은 땅속이라 위에 그린다)
    ///   - 노란 기둥: 지금 파고 있는 막장 (멀리서도 보이게 높게)
    ///   - 아래 종단면: 위성 지표, 터널 노면, 판 곳(패턴 색), 막장, 사갱 위치
    ///   - 오른쪽 위 글상자: 제원, 막장, 싸이클, 예측 관통일 ↔ 실제, 대안(사갱 없음·로더 2대)
    /// 조작: Space 재생·멈춤, B 처음, N / M 10일 뒤로·앞으로 (터널 모드일 때만. 메뉴 [재생]·[장비]도 같은 동작)
    /// </summary>
    public sealed class TunnelTool : MonoBehaviour
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private const float PlaybackSeconds = 20f;
        // 띠는 두 튜브(서로 약 25 m 떨어짐)를 함께 덮는 폭 하나로 그린다 — 몇 km 떨어진 시점에서도 보이게 (2026-11-21 시연 캡처)
        private const double RibbonWidth = 70.0, RibbonLift = 6.0, StepDays = 10.0;

        /// <summary>패턴 색 (P-1 ~ P-5)과 아직 안 판 곳</summary>
        public static readonly Color[] PatternColors =
        {
            new Color(0.25f, 0.50f, 0.95f), new Color(0.20f, 0.75f, 0.80f), new Color(0.35f, 0.78f, 0.30f),
            new Color(0.97f, 0.62f, 0.15f), new Color(0.88f, 0.22f, 0.15f),
        };
        private static readonly Color RemainingColor = new Color(0.45f, 0.45f, 0.47f);
        private static readonly Color BeaconColor = new Color(1.0f, 0.88f, 0.15f);
        /// <summary>2차 라이닝을 친 곳 (콘크리트 색)</summary>
        public static readonly Color LinedColor = new Color(0.90f, 0.90f, 0.86f);

        // ---- 조건 (메뉴 [장비]에서 바꾼다) ----
        public double hoursPerDay = 24.0;
        public int loaders = 1;
        public bool useMiddleAccess = true;
        /// <summary>2차 라이닝 철재거푸집 수 (튜브마다, 1 · 2 · 4조)</summary>
        public int formsPerTube = 2;
        /// <summary>2차 라이닝을 굴착과 병행하면 true (막장 500 m 뒤까지, 가정). false면 관통 뒤에 시작</summary>
        public bool concurrentLining = false;
        /// <summary>용수 할증 (0 또는 0.3, 표준품셈 3-2-1 주 ⑦)</summary>
        public double waterIncrease = 0.0;

        private ElevationGrid _grid;
        private LocalFrame _frame;
        private GradingPlanTool _grading;

        private TunnelSpec _spec;
        private TunnelPlan _plan, _planNoMiddle, _planTwoLoaders;
        private TunnelAlignment _tubeB;
        private TunnelLining _lining;
        private double _day;
        private bool _playing;
        private double _drawnDay = double.NaN;

        private GameObject _root;
        private PrimitiveKit _kit;
        private Mesh _ribbonA, _ribbonB;
        private Material[] _materials;
        private readonly List<Transform> _beacons = new List<Transform>();
        private Transform _westTruck, _eastTruck;   // 입구 밖으로 버력을 실어 나르는 덤프트럭 (보기용)
        private GUIStyle _style, _small, _tiny;
        private Texture2D _sectionTexture;   // 지보 패턴 단면 모식도 (자세히 모드)
        private string _sectionKey = "";
        private Rect _textRect, _profileRect;

        public bool IsOn => _spec != null;
        public TunnelSpec Spec => _spec;
        public TunnelPlan Plan => _plan;
        public double Day => _day;
        public bool IsPlaying => _playing;
        public double FinishDay => _plan != null ? _plan.Schedule.FinishDay : 0.0;
        public TunnelLining Lining => _lining;
        /// <summary>재생 끝 = 2차 라이닝이 끝나는 날 (관통 뒤에도 이어진다)</summary>
        public double EndDay => _lining != null ? _lining.FinishDay : FinishDay;

        public void Initialize(ElevationGrid grid, LocalFrame frame, GradingPlanTool grading)
        {
            Stop();
            _grid = grid;
            _frame = frame;
            _grading = grading;
        }

        // ---------- 시작·끝 ----------

        public void StartTunnel(TunnelSpec spec)
        {
            if (spec == null || _grid == null) return;
            Stop();
            if (_grading != null && (_grading.HasPad || _grading.IsDamMode)) _grading.Clear();
            _spec = spec;
            _tubeB = spec.AlignmentB();
            _day = 0.0;
            _playing = false;
            Recalculate();
            BuildObjects();
            FocusCamera();
        }

        public void Stop()
        {
            _spec = null;
            _plan = _planNoMiddle = _planTwoLoaders = null;
            _lining = null;
            _playing = false;
            _drawnDay = double.NaN;
            DestroyObjects();
        }

        /// <summary>조건이 바뀌면 공정을 다시 계산한다 (대안 두 가지도 함께: 사갱 없음, 로더 2대)</summary>
        private void Recalculate()
        {
            _plan = new TunnelPlan(_spec, _grid, hoursPerDay, loaders, useMiddleAccess, waterIncrease);
            _planNoMiddle = _plan.UseMiddleAccess ? new TunnelPlan(_spec, _grid, hoursPerDay, loaders, false, waterIncrease) : null;
            _planTwoLoaders = loaders < 2 ? new TunnelPlan(_spec, _grid, hoursPerDay, 2, useMiddleAccess, waterIncrease) : null;
            _lining = concurrentLining ? TunnelLining.Concurrently(_plan, formsPerTube) : TunnelLining.For(_plan, formsPerTube);
            _day = System.Math.Min(_day, EndDay);
            _drawnDay = double.NaN;
        }

        // ---------- 조작 (키와 메뉴가 함께 쓴다) ----------

        public void TogglePlay()
        {
            if (!IsOn) return;
            _playing = !_playing;
            if (_playing && _day >= EndDay) _day = 0.0;
        }

        public void Restart()
        {
            if (!IsOn) return;
            _day = 0.0;
            _playing = false;
        }

        public void StepDay(double delta)
        {
            if (!IsOn) return;
            _playing = false;
            _day = System.Math.Max(0.0, System.Math.Min(EndDay, System.Math.Round(_day / StepDays) * StepDays + delta));
        }

        public void ChangeHours(double delta)
        {
            if (!IsOn) return;
            hoursPerDay = System.Math.Max(8.0, System.Math.Min(24.0, hoursPerDay + delta));
            Recalculate();
        }

        public void ChangeLoaders(int delta)
        {
            if (!IsOn) return;
            loaders = Mathf.Clamp(loaders + delta, 1, 3);
            Recalculate();
        }

        /// <summary>라이닝 거푸집 수를 1 → 2 → 4 → 1조로 바꾼다</summary>
        public void CycleForms()
        {
            if (!IsOn) return;
            formsPerTube = formsPerTube == 1 ? 2 : formsPerTube == 2 ? 4 : 1;
            Recalculate();
        }

        /// <summary>용수 할증 없음 ↔ +30 % (표준품셈 3-2-1 주 ⑦)</summary>
        public void ToggleWater()
        {
            if (!IsOn) return;
            waterIncrease = waterIncrease > 0 ? 0.0 : 0.3;
            Recalculate();
        }

        /// <summary>라이닝을 관통 뒤 ↔ 굴착과 병행으로 바꾼다</summary>
        public void ToggleConcurrentLining()
        {
            if (!IsOn) return;
            concurrentLining = !concurrentLining;
            Recalculate();
        }

        public void ToggleMiddleAccess()
        {
            if (!IsOn || !_spec.HasMiddleAccess) return;   // 가운데 작업구 자료가 없는 터널(산성터널)은 바꿀 수 없다
            useMiddleAccess = !useMiddleAccess;
            Recalculate();
            BuildBeacons();
        }

        private void Update()
        {
            if (!IsOn) return;
            // 부지를 새로 놓으면(G 키 등) 터널 보기를 끝낸다 (두 공정이 같은 키를 쓰므로).
            if (_grading != null && _grading.HasPad) { Stop(); return; }

            if (InputGate.Allowed && Keyboard.current != null)
            {
                Keyboard k = Keyboard.current;
                if (k.spaceKey.wasPressedThisFrame) TogglePlay();
                if (k.bKey.wasPressedThisFrame) Restart();
                if (k.nKey.wasPressedThisFrame) StepDay(-StepDays);
                if (k.mKey.wasPressedThisFrame) StepDay(+StepDays);
            }

            if (_playing)
            {
                _day += Time.unscaledDeltaTime * EndDay / PlaybackSeconds * HudMode.PlaybackSpeed;   // 재생 속도 (메뉴 [재생])
                if (_day >= EndDay) { _day = EndDay; _playing = false; }
            }
            if (double.IsNaN(_drawnDay) || System.Math.Abs(_day - _drawnDay) > 0.1) RefreshObjects();
            PlacePortalTrucks(Time.unscaledTime);
        }

        // ---------- 3D: 띠, 막장 기둥, 입구 ----------

        private void BuildObjects()
        {
            DestroyObjects();
            _root = new GameObject("Tunnel (demo)");
            _kit = new PrimitiveKit();
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            // 묶음: 패턴 색 5개, 안 판 곳(회색), 라이닝 친 곳(콘크리트 색)
            _materials = new Material[PatternColors.Length + 2];
            for (int k = 0; k < _materials.Length; k++)
            {
                _materials[k] = new Material(unlit) { name = "Tunnel ribbon" };
                _materials[k].SetColor("_BaseColor", k < PatternColors.Length ? PatternColors[k]
                    : k == PatternColors.Length ? RemainingColor : LinedColor);
            }
            _ribbonA = CreateRibbon("터널 평면 위치 (두 튜브)", _plan.Alignment);

            // 입구: 튜브마다 양 끝에 콘크리트 갱문
            foreach (TunnelAlignment a in new[] { _plan.Alignment, _tubeB })
            {
                AddPortal(a, 0.0, -1.0);
                AddPortal(a, a.Length, +1.0);
            }
            BuildBeacons();
            _westTruck = EquipmentModels.DumpTruck(_kit, _root.transform);
            _eastTruck = EquipmentModels.DumpTruck(_kit, _root.transform);
            RefreshObjects();
        }

        /// <summary>
        /// 입구 막장이 파는 동안 입구 밖 160 m를 오가는 덤프트럭 (나갈 때 버력을 싣고, 들어올 때 빈 차). 보기용 — 속도 6 m/초.
        /// 실제 버력 운반 대수·거리는 계산하지 않았다 (C군은 로더가 버력처리를 정하고 트럭은 충분하다고 본다).
        /// </summary>
        private void PlacePortalTrucks(double seconds)
        {
            if (_westTruck == null || _plan == null) return;
            PlaceTruck(_westTruck, 0, 0.0, -1.0, seconds);
            PlaceTruck(_eastTruck, 1, _plan.Alignment.Length, +1.0, seconds + 13.0);
        }

        private void PlaceTruck(Transform truck, int face, double chainage, double outward, double seconds)
        {
            bool active = _plan.Schedule.FaceCount > face && _plan.Schedule.IsActive(face, _day);
            truck.gameObject.SetActive(active);
            if (!active) return;
            const double oneWay = 160.0, speed = 6.0;
            double along = EquipmentMotion.ShuttleDistance(seconds, oneWay, speed);
            bool returning = EquipmentMotion.ShuttleIsReturning(seconds, oneWay, speed);
            TunnelAlignment a = _plan.Alignment;
            a.PointAt(chainage, out double e, out double n);
            a.DirectionAt(chainage, out double ue, out double un);
            double d = 12.0 + along;
            double te = e + ue * outward * d, tn = n + un * outward * d;
            truck.position = ToWorld(te, tn, Ground(te, tn));
            var heading = new Vector3((float)(ue * outward), 0f, (float)(un * outward));
            truck.rotation = Quaternion.LookRotation(returning ? -heading : heading, Vector3.up);
            EquipmentModels.SetTruckSoil(truck, returning ? 0.0 : 1.0);
        }

        /// <summary>자동 시연용 위치: west(서쪽 입구), east(동쪽 입구), middle(사갱 연결부) — 지표 위</summary>
        public bool TryGetPoint(string name, out Vector3 position)
        {
            position = Vector3.zero;
            if (!IsOn) return false;
            TunnelAlignment a = _plan.Alignment;
            double ch = name == "west" ? 0.0 : name == "east" ? a.Length : name == "middle" ? a.Length * 0.5 : -1.0;
            if (ch < 0) return false;
            a.PointAt(ch, out double e, out double n);
            position = ToWorld(e, n, Ground(e, n));
            return true;
        }

        private void AddPortal(TunnelAlignment a, double chainage, double outward)
        {
            a.PointAt(chainage, out double e, out double n);
            a.DirectionAt(chainage, out double ue, out double un);
            double z = a.RoadElevation(chainage);
            Transform portal = _kit.Group(_root.transform, "갱문", ToWorld(e, n, z));
            portal.rotation = Quaternion.LookRotation(new Vector3((float)(ue * outward), 0f, (float)(un * outward)), Vector3.up);
            _kit.Box(portal, "갱문 벽", new Vector3(0f, 6f, 0f), new Vector3(16f, 12f, 3f), PrimitiveKit.Concrete);
            _kit.Box(portal, "갱문 입구", new Vector3(0f, 4f, 1.6f), new Vector3(11f, 7.5f, 0.2f), PrimitiveKit.Dark);
        }

        private void BuildBeacons()
        {
            foreach (Transform b in _beacons) if (b != null) Destroy(b.gameObject);
            _beacons.Clear();
            if (_root == null) return;
            for (int i = 0; i < _plan.Schedule.FaceCount; i++)
            {
                Transform beacon = _kit.Group(_root.transform, "막장 " + _plan.Schedule.Face(i).Name, Vector3.zero);
                _kit.Box(beacon, "기둥", new Vector3(0f, 150f, 0f), new Vector3(45f, 300f, 45f), BeaconColor);
                _beacons.Add(beacon);
            }
        }

        private Mesh CreateRibbon(string name, TunnelAlignment a)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            var mesh = new Mesh { name = name };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            int n = (int)System.Math.Floor(a.Length / TunnelPlan.SampleStep) + 2;
            var vertices = new Vector3[n * 2];
            for (int i = 0; i < n; i++)
            {
                double ch = System.Math.Min(a.Length, i * TunnelPlan.SampleStep);
                a.PointAt(ch, out double e, out double nn);
                a.DirectionAt(ch, out double ue, out double un);
                double le = e - un * RibbonWidth / 2.0, ln = nn + ue * RibbonWidth / 2.0;
                double re = e + un * RibbonWidth / 2.0, rn = nn - ue * RibbonWidth / 2.0;
                vertices[2 * i] = ToWorld(le, ln, Ground(le, ln) + RibbonLift);
                vertices[2 * i + 1] = ToWorld(re, rn, Ground(re, rn) + RibbonLift);
            }
            mesh.vertices = vertices;
            mesh.subMeshCount = _materials.Length;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = _materials;
            return mesh;
        }

        private void RefreshObjects()
        {
            if (_root == null || _plan == null) return;
            FillRibbon(_ribbonA, _plan.Alignment);
            TunnelSchedule s = _plan.Schedule;
            for (int i = 0; i < _beacons.Count && i < s.FaceCount; i++)
            {
                bool active = s.IsActive(i, _day);
                _beacons[i].gameObject.SetActive(active);
                if (!active) continue;
                double ch = s.FacePosition(i, _day);
                _plan.Alignment.PointAt(ch, out double e, out double n);
                _beacons[i].position = ToWorld(e, n, Ground(e, n));
            }
            _drawnDay = _day;
        }

        /// <summary>띠의 구간마다 색 묶음을 다시 정한다: 라이닝 친 곳 = 콘크리트 색, 판 곳 = 그 측점의 지보 패턴 색, 안 판 곳 = 회색</summary>
        private void FillRibbon(Mesh mesh, TunnelAlignment a)
        {
            if (mesh == null) return;
            int segments = mesh.vertexCount / 2 - 1;
            var lists = new List<int>[_materials.Length];
            for (int k = 0; k < lists.Length; k++) lists[k] = new List<int>();
            double scale = _plan.Alignment.Length / a.Length;   // 튜브 2는 길이가 0.01 % 달라 비율로 맞춘다
            for (int i = 0; i < segments; i++)
            {
                double mid = System.Math.Min(a.Length, (i + 0.5) * TunnelPlan.SampleStep) * scale;
                int k = _lining != null && _lining.IsLined(mid, _day) ? PatternColors.Length + 1
                    : _plan.Schedule.IsExcavated(mid, _day) ? _plan.PatternAt(mid) : PatternColors.Length;
                int v = 2 * i;
                lists[k].AddRange(new[] { v, v + 2, v + 1, v + 1, v + 2, v + 3 });
            }
            for (int k = 0; k < lists.Length; k++) mesh.SetTriangles(lists[k], k, k == lists.Length - 1);
            mesh.RecalculateBounds();
        }

        private void DestroyObjects()
        {
            if (_sectionTexture != null) Destroy(_sectionTexture);
            _sectionTexture = null;
            _sectionKey = "";
            _beacons.Clear();
            _westTruck = _eastTruck = null;
            if (_ribbonA != null) Destroy(_ribbonA);
            if (_ribbonB != null) Destroy(_ribbonB);
            _ribbonA = _ribbonB = null;
            if (_materials != null) foreach (Material m in _materials) if (m != null) Destroy(m);
            _materials = null;
            if (_kit != null) _kit.DestroyMaterials();
            _kit = null;
            if (_root != null) Destroy(_root);
            _root = null;
        }

        private void OnDestroy() => DestroyObjects();

        private double Ground(double e, double n) =>
            _grid.TryGetElevationAt(e, n, out double z) ? z : 0.0;

        private Vector3 ToWorld(double e, double n, double z) => _frame.ToLocal(e, n, z).ToVector3();

        private void FocusCamera()
        {
            var controller = GetComponent<OrbitCameraController>();
            if (controller == null) return;
            TunnelAlignment a = _plan.Alignment;
            a.PointAt(a.Length / 2.0, out double e, out double n);
            // 남쪽에서 북쪽을 보며(방위 0°) 터널이 화면을 가로지르게, 길이의 0.62배 거리, 38° 내려다봄
            controller.LookFrom(_frame.ToLocal(e, n, Ground(e, n) - 250.0), 0.0, 38.0, a.Length * 0.62, 50.0);
        }

        // ---------- 글 ----------

        /// <summary>제원·공정·실제 비교 (글상자와 자동 시연 로그가 함께 쓴다)</summary>
        /// <summary>
        /// 간단 글상자 (2026-11-21): 오른쪽 글상자 폭을 화면의 45 %로 줄이면서 긴 줄이 여러 번 접혀 글상자가 종단면까지 내려왔다
        /// (1100 × 505 캡처). 핵심 다섯 줄만 짧게 — 관통·실제, 진행, 대안, 라이닝, 조건. 나머지는 [화면] → 자세히(BuildText).
        /// </summary>
        public string BuildShortText()
        {
            if (!IsOn) return "";
            TunnelSchedule s = _plan.Schedule;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[" + _spec.Title + " · NATM 굴착]");
            string actual;
            if (_spec.ExcavationStartKnown)
            {
                double diff = s.FinishDay / _spec.ActualDays - 1.0;
                actual = "실제 " + N0(_spec.ActualDays) + "일 " + (diff >= 0 ? "+" : "−") + System.Math.Abs(diff * 100.0).ToString("F1", Invariant) + " %";
            }
            else actual = "실제 굴착 기간 비공개";
            sb.AppendLine("관통    " + N0(s.FinishDay) + "일 (" + actual + ") · 막장 평균 " + _plan.AverageFaceAdvance.ToString("F2", Invariant) + " m/일");
            double dug = s.ExcavatedLength(_day);
            sb.AppendLine("진행    " + _day.ToString("F0", Invariant) + "일째 " + N0(dug) + " / " + N0(s.Length) + " m ("
                + (dug / s.Length * 100.0).ToString("F1", Invariant) + " %) · 막장 " + TunnelCrew.ActiveFaces(s, _day) + "개 "
                + N0(TunnelCrew.PeopleOnDay(s, _spec.Tubes, hoursPerDay, _day)) + "명");
            var alt = new List<string>();
            if (_planNoMiddle != null) alt.Add("사갱 없음 " + N0(_planNoMiddle.Schedule.FinishDay) + "일");
            if (_planTwoLoaders != null) alt.Add("로더 2대 " + N0(_planTwoLoaders.Schedule.FinishDay) + "일");
            if (alt.Count > 0) sb.AppendLine("대안    " + string.Join(" · ", alt));
            sb.AppendLine("라이닝  " + N0(_lining.StartDay) + " ~ " + N0(_lining.FinishDay) + "일 ("
                + (_lining.Concurrent ? "굴착과 병행" : "관통 뒤") + " · 거푸집 " + _lining.FormsPerTube + "조)");
            sb.Append("조건    " + hoursPerDay.ToString("0", Invariant) + "시간 · 로더 " + loaders + "대 · "
                + (_plan.UseMiddleAccess ? "사갱 사용" : "막장 2개")
                + (waterIncrease > 0 ? " · 용수 +" + (waterIncrease * 100).ToString("0", Invariant) + " %" : "")
                + "   (자세히: [화면])");
            return sb.ToString();
        }

        public string BuildText()
        {
            if (!IsOn) return "";
            TunnelSchedule s = _plan.Schedule;
            double maxCover = _plan.MaxCover(out _);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[" + _spec.Title + " · NATM 굴착]");
            sb.AppendLine("제원    길이 " + N0(_spec.OfficialLength) + " m × 튜브 " + _spec.Tubes + "개 (" + _spec.LanesPerTube
                + "차로), 단면 " + N0(_plan.Section.Area) + " m², 토피 최대 " + N0(maxCover) + " m"
                + (string.IsNullOrEmpty(_spec.ReportedCover) ? "" : " (보도 " + _spec.ReportedCover + ")"));
            sb.AppendLine("막장    " + (_plan.UseMiddleAccess
                    ? "양 끝 입구 + 가운데 사갱 2개 (" + N0(_spec.MiddleStartDay) + "일째부터)"
                    : _spec.HasMiddleAccess ? "양 끝 입구 2개 (사갱 없음)" : "양 끝 입구 2개 (작업구 자료 없음 — 가정)")
                + " · 하루 " + hoursPerDay.ToString("0", Invariant) + "시간 · 로더 " + loaders + "대"
                + (waterIncrease > 0 ? " · 용수 +" + (waterIncrease * 100).ToString("0", Invariant) + " %" : ""));
            var cyc = new System.Text.StringBuilder();
            for (int k = 0; k < _plan.Cycles.Length; k++)
            {
                NatmCycle c = _plan.Cycles[k];
                if (k > 0) cyc.Append(" · ");
                cyc.Append(c.Pattern.Name + " " + (c.TotalMinutes / 60.0).ToString("0.0", Invariant) + "h/"
                    + c.Pattern.Advance.ToString("0.0", Invariant) + "m");
                if (c.Pattern.IsBench)   // 상하반 분할: 하반 싸이클을 + 로 붙인다
                    cyc.Append("+" + (c.BenchMinutes / 60.0).ToString("0.0", Invariant) + "h/" + c.Pattern.BenchAdvance.ToString("0.0", Invariant) + "m");
            }
            sb.AppendLine("싸이클  " + cyc + "  (표준품셈 토목 3-2-1 C군)");
            sb.AppendLine("굴진    막장 평균 " + _plan.AverageFaceAdvance.ToString("F2", Invariant) + " m/일 → 관통 " + N0(s.FinishDay)
                + "일, 두 튜브 하루 " + _plan.CombinedDailyAdvance.ToString("F1", Invariant) + " m");
            if (_spec.ExcavationStartKnown)
            {
                double diff = s.FinishDay / _spec.ActualDays - 1.0;
                sb.AppendLine("실제    " + _spec.ExcavationStart.ToString("yyyy.M", Invariant) + " ~ " + _spec.Breakthrough.ToString("yyyy.M.d", Invariant)
                    + " 관통 (" + N0(_spec.ActualDays) + "일" + (_spec.ReportedDailyAdvance > 0 ? ", 하루 약 " + N0(_spec.ReportedDailyAdvance) + " m" : "")
                    + ") ↔ 계산 " + (diff >= 0 ? "+" : "−") + System.Math.Abs(diff * 100.0).ToString("F1", Invariant) + " %");
            }
            else
            {
                // 굴착 시작일이 공개되지 않은 터널: 착공 ~ 관통에는 굴착 외 기간이 들어 있어 % 비교를 하지 않는다
                sb.AppendLine(_spec.BreakthroughKnown
                    ? "실제    공사 " + _spec.ExcavationStart.ToString("yyyy.M", Invariant) + " 착공 ~ " + _spec.Breakthrough.ToString("yyyy.M", Invariant)
                      + " 관통 보도 (" + N0(_spec.ActualDays) + "일) — 굴착 시작일이 공개되지 않아 굴착 기간과 직접 비교하지 않음"
                    : "실제    공사 " + _spec.ExcavationStart.ToString("yyyy.M", Invariant) + " 착공 ~ " + _spec.Opening.ToString("yyyy.M.d", Invariant)
                      + " 개통만 확인 — 굴착 시작일·관통일이 공개되지 않아 비교하지 않음");
            }
            var alt = new List<string>();
            if (_planNoMiddle != null)
                alt.Add("사갱이 없으면 " + N0(_planNoMiddle.Schedule.FinishDay) + "일 (+" + N0(_planNoMiddle.Schedule.FinishDay - s.FinishDay) + "일)");
            if (_planTwoLoaders != null)
                alt.Add("로더 2대면 " + N0(_planTwoLoaders.Schedule.FinishDay) + "일 (−" + N0(s.FinishDay - _planTwoLoaders.Schedule.FinishDay) + "일)");
            if (alt.Count > 0) sb.AppendLine("대안    " + string.Join(" · ", alt));
            // 버력 운반 (2026-11-21): 입구 막장이 깊어질수록 로더가 기다리지 않으려면 트럭이 더 필요하다 (표준품셈 8-2-8)
            double westDepth = s.FacePosition(0, _day), eastDepth = s.Length - s.FacePosition(1, _day);
            double westFinal = s.FacePosition(0, s.FinishDay), eastFinal = s.Length - s.FacePosition(1, s.FinishDay);
            sb.AppendLine("버력    입구 막장 깊이 서 " + N0(westDepth) + " m · 동 " + N0(eastDepth) + " m → 트럭 서 "
                + TunnelMuckHaul.TrucksNeeded(_plan.Crew, westDepth) + "대 · 동 " + TunnelMuckHaul.TrucksNeeded(_plan.Crew, eastDepth)
                + "대 (가장 깊을 때 " + TunnelMuckHaul.TrucksNeeded(_plan.Crew, System.Math.Max(westFinal, eastFinal)) + "대, 표준품셈 8-2-8)");
            double dug = s.ExcavatedLength(_day);
            sb.AppendLine("진행    " + _day.ToString("F0", Invariant) + "일째: 튜브마다 " + N0(dug) + " m / " + N0(s.Length) + " m ("
                + (dug / s.Length * 100.0).ToString("F1", Invariant) + " %), 버력 " + N0(dug * _plan.Section.Area * _spec.Tubes) + " m³");
            // 작업 인원 (2026-11-21): 표준품셈 3-2-5 C군 막장 하나 14인 × 교대 (8시간, 가정)
            int faces = TunnelCrew.ActiveFaces(s, _day), shifts = TunnelCrew.Shifts(hoursPerDay);
            sb.AppendLine("인원    지금 막장 " + faces + "개 × 튜브 " + _spec.Tubes + " × " + N0(TunnelCrew.PerFace) + "인 × " + shifts + "교대 = "
                + N0(TunnelCrew.PeopleOnDay(s, _spec.Tubes, hoursPerDay, _day)) + "명 · 굴착 연인원 "
                + N0(TunnelCrew.LaborDays(s, _spec.Tubes, hoursPerDay)) + "인·일 (3-2-5)");
            // 2차 라이닝 (2026-11-21): 관통 뒤 방수 대차 조립 4일 → 철재거푸집 1 span(12 m, 가정)을 하루에 하나 (표준품셈 3-3-1)
            double lined = _lining.LinedLength(_day);
            sb.AppendLine("라이닝  " + (_lining.Concurrent ? "굴착과 병행(막장 " + N0(_lining.Lag) + " m 뒤), " : "관통 뒤, ")
                + "거푸집 튜브마다 " + _lining.FormsPerTube + "조 × 하루 " + N0(_lining.SpanLength) + " m (3-3-1) → "
                + N0(_lining.StartDay) + " ~ " + N0(_lining.FinishDay) + "일" + LiningDateText()
                + (_day > _lining.StartDay ? ", 지금 " + N0(lined) + " m (" + (lined / s.Length * 100.0).ToString("F0", Invariant) + " %)" : ""));
            if (HudMode.Detail)
            {
                // 지보재 (2026-11-21): 그날까지 판 구간에 들어간 양 / 터널 전체 (설계 두께·간격, 튜브 모두)
                SupportTotals now = TunnelSupportQuantity.Placed(_plan, _day), all = TunnelSupportQuantity.Total(_plan);
                sb.AppendLine("라이닝  콘크리트 " + N0(_lining.Concrete) + " m³ (1회 " + N0(_lining.ConcretePerPour) + " m³, 두께 "
                    + (_lining.Thickness * 100).ToString("0", Invariant) + " cm 가정) · 방수 " + N0(_lining.WaterproofArea) + " m² → 방수공 하루 "
                    + _lining.WaterproofWorkersPerDay.ToString("0.0", Invariant) + "명 (3-4-1)");
                sb.AppendLine("지보재  숏크리트 " + N0(now.Shotcrete) + " / " + N0(all.Shotcrete) + " m³ · 록볼트 " + N0(now.Bolts) + " / "
                    + N0(all.Bolts) + "개 · 강지보 " + N0(now.SteelRibs) + " / " + N0(all.SteelRibs) + "개");
                // 지금 판 길이가 가장 긴 패턴(대표 P-3)의 싸이클을 풀어 보인다
                NatmCycle c = _plan.Cycles[2];
                sb.AppendLine("— " + c.Pattern.Name + " 1발파 싸이클 (굴진장 " + c.Pattern.Advance.ToString("0.0", Invariant) + " m, 숏크리트 "
                    + (c.Pattern.Shotcrete * 100).ToString("0", Invariant) + " cm, 록볼트 " + c.BoltsPerRound.ToString("0.0", Invariant) + "개):");
                var line = new System.Text.StringBuilder();
                foreach (CycleItem item in c.Items)
                {
                    if (line.Length > 70) { sb.AppendLine("    " + line); line.Clear(); }
                    if (line.Length > 0) line.Append(" · ");
                    line.Append(item.Name + " " + item.Minutes.ToString("0", Invariant));
                }
                if (line.Length > 0) sb.AppendLine("    " + line);
                sb.AppendLine("    합 " + c.TotalMinutes.ToString("0", Invariant) + "분 (분)");
            }
            // 가정 줄은 "자세히"에서만 (간단 모드에서 글상자가 종단면 제목을 덮지 않게, 2026-11-21 캡처)
            if (HudMode.Detail)
                sb.AppendLine("가정    패턴 비율 = 고속도로 터널 실제 시공 비율(2014, 107개소), 토피가 얕을수록 나쁜 패턴 · 계산 근거: [대안] → 보고서 저장");
            return sb.ToString().TrimEnd();
        }

        /// <summary>자동 시연 로그용: 글상자 전체 + 패턴 비율</summary>
        /// <summary>마지막으로 저장한 터널 보고서 경로 (없으면 빈 글)</summary>
        public string LastReportPath { get; private set; } = "";

        /// <summary>터널 보고서를 Markdown으로 저장한다 (메뉴 [대안] → 보고서 저장, 터널 보기 중). 저장한 경로를 돌려준다.</summary>
        public string SaveReport()
        {
            if (!IsOn) return "";
            string folder = AlternativeTool.ReportFolder;
            System.IO.Directory.CreateDirectory(folder);
            string stamp = System.DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string path = System.IO.Path.Combine(folder, "터널보고서_" + _spec.Company + "_" + _spec.Name + "_" + stamp + ".md");
            string md = TunnelReport.Markdown(_plan, _planNoMiddle, _planTwoLoaders, System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"), _grid);
            System.IO.File.WriteAllText(path, md, new System.Text.UTF8Encoding(false));
            LastReportPath = path;
            Debug.Log("[CivilTwin] 터널 보고서 저장: " + path);
            return path;
        }

        public string DescribeForLog()
        {
            if (!IsOn) return "(터널 없음)";
            var sb = new System.Text.StringBuilder(BuildText());
            sb.AppendLine();
            for (int k = 0; k < _plan.Cycles.Length; k++)
            {
                sb.AppendLine(_plan.Cycles[k].Pattern.Name + ": 길이 비율 " + (_plan.PatternShare(k) * 100.0).ToString("F1", Invariant)
                    + " %, 싸이클 " + _plan.Cycles[k].TotalMinutes.ToString("F1", Invariant) + "분"
                    + (_plan.Cycles[k].Pattern.IsBench ? " + 하반 " + _plan.Cycles[k].BenchMinutes.ToString("F1", Invariant) + "분" : "") + ", 막장 하루 "
                    + _plan.Cycles[k].AdvancePerDay(hoursPerDay).ToString("F2", Invariant) + " m");
            }
            return sb.ToString();
        }

        private static string N0(double v) => v.ToString("N0", Invariant);

        /// <summary>굴착 시작일을 아는 터널이면 라이닝 끝 날짜와 실제 개통일을 함께 적는다</summary>
        private string LiningDateText()
        {
            if (!_spec.ExcavationStartKnown) return "";
            string end = _spec.ExcavationStart.AddDays(_lining.FinishDay).ToString("yyyy.M", Invariant);
            return " (" + end + " 끝" + (_spec.Opening > _spec.ExcavationStart ? ", 실제 개통 " + _spec.Opening.ToString("yyyy.M", Invariant) : "") + ")";
        }

        // ---------- 화면 (IMGUI) ----------

        private void OnGUI()
        {
            HudMode.ApplyMatrix();   // 글자 크기 (메뉴 [화면])
            if (!IsOn || _plan == null) return;
            if (_style == null)
            {
                _style = HudStyle.Panel(14);
                _small = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = false };
                _small.normal.textColor = HudStyle.Ui(Color.white);
                _tiny = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = false, alignment = TextAnchor.MiddleCenter };
                _tiny.normal.textColor = HudStyle.Ui(Color.white);
            }

            bool profile = HudMode.Show(HudMode.Item.TimeBar);
            _profileRect = profile ? ProfileRect() : Rect.zero;
            // 종단면을 먼저 그린다: 작은 화면에서 "자세히" 글상자가 길어지면 글상자가 위에 덮여 읽히게 (2026-11-21 캡처)
            if (profile) DrawProfile();
            if (HudMode.Show(HudMode.Item.Section)) DrawSection();
            // 정보 글상자 (InfoBox): 오른쪽 위 메뉴 아래, 폭 제한·줄바꿈, [접기] 버튼. 이름표보다 먼저 자리를 정한다.
            _textRect = Rect.zero;
            if (HudMode.Show(HudMode.Item.Info))
            {
                Rect bar = ControlBar.BarRect;
                double dugNow = _plan.Schedule.ExcavatedLength(_day);
                string headline = "[" + _spec.Name + "] 관통 " + N0(_plan.Schedule.FinishDay) + "일 · " + _day.ToString("F0", Invariant) + "일째 "
                    + (dugNow / _plan.Schedule.Length * 100.0).ToString("F0", Invariant) + " %";
                _textRect = InfoBox.Draw(HudMode.Detail ? BuildText() : BuildShortText(), bar.width > 0f ? bar.yMax + 8f : 10f, headline);
            }
            if (HudMode.Show(HudMode.Item.Labels)) DrawLabels();   // 글상자·메뉴·종단면과 겹치는 이름표는 뺀다
        }

        // ---------- 지보 패턴 단면 모식도 (자세히 모드, 2026-11-21) ----------

        // 폭 150: 1,100 px 창에서 "자세히" 글상자(오른쪽, 폭 약 900)와 겹치지 않게 (2026-11-21 캡처)
        private const int SectionPixels = 150;
        private const double SectionExaggerate = 5.0;

        /// <summary>
        /// 왼쪽에 서쪽 입구 막장 위치의 단면을 그린다: 숏크리트(회색, 두께 5배로 과장), 록볼트(노랑), 강지보(남색), 상하반 경계(흰 점선),
        /// 라이닝(그 자리를 쳤으면 흰 띠). 기하는 Core/TunnelCrossSection, 픽셀마다 Classify로 칠한다.
        /// </summary>
        private void DrawSection()
        {
            TunnelSchedule s = _plan.Schedule;
            double ch = System.Math.Max(0.0, s.FacePosition(0, _day) - 1.0);
            int k = _plan.PatternAt(ch);
            bool lined = _lining != null && _lining.IsLined(ch, _day);
            string key = k + (lined ? "L" : "");
            var section = new TunnelCrossSection(SupportPattern.Standard[k], _plan.Section.Area, lined, 0.30, SectionExaggerate);
            if (_sectionTexture == null || key != _sectionKey)
            {
                if (_sectionTexture != null) Destroy(_sectionTexture);
                _sectionTexture = BuildSectionTexture(section);
                _sectionKey = key;
            }

            float w = SectionPixels, h = _sectionTexture.height;
            var box = new Rect(12f, 62f, w + 16f, h + 92f);
            if (_profileRect.height > 0f && box.yMax > _profileRect.y - 6f) box.height = _profileRect.y - 6f - box.y;
            HudStyle.DrawBox(box);
            GUI.Label(new Rect(box.x + 8f, box.y + 4f, w, 18f), "서쪽 막장 단면 " + N0(ch) + " m", _small);
            GUI.DrawTexture(new Rect(box.x + 8f, box.y + 24f, w, h), _sectionTexture);
            SupportPattern p = SupportPattern.Standard[k];
            string line1 = p.Name + (p.IsBench ? " 상하반 " + p.Advance.ToString("0.0", Invariant) + "/" + p.BenchAdvance.ToString("0.0", Invariant) + " m"
                                               : " 전단면 " + p.Advance.ToString("0.0", Invariant) + " m");
            string line2 = "숏크리트 " + (p.Shotcrete * 100).ToString("0", Invariant) + " cm" + (p.SteelRib ? " · 강지보" : "") + (lined ? " · 라이닝" : "");
            string line3 = p.BoltSpacing > 0 ? "록볼트 L " + p.BoltLength.ToString("0", Invariant) + " m @ " + p.BoltSpacing.ToString("0.0", Invariant) + " m"
                                             : "록볼트 랜덤 L " + p.BoltLength.ToString("0", Invariant) + " m";
            GUI.Label(new Rect(box.x + 8f, box.y + 26f + h, w + 8f, 18f), line1, _small);
            GUI.Label(new Rect(box.x + 8f, box.y + 44f + h, w + 8f, 18f), line2, _small);
            GUI.Label(new Rect(box.x + 8f, box.y + 62f + h, w + 8f, 18f), line3, _small);
        }

        private static Texture2D BuildSectionTexture(TunnelCrossSection section)
        {
            double half = section.HalfWidth, top = section.Top, bottom = section.Bottom;
            double mPerPx = 2.0 * half / SectionPixels;
            section.LineHalfWidth = mPerPx * 0.6;   // 선이 1픽셀 남짓으로 끊기지 않게
            int width = SectionPixels, height = Mathf.Max(1, (int)System.Math.Round((top - bottom) / mPerPx));
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[width * height];
            Color32 rock = HudStyle.Ui(new Color32(120, 92, 62, 255)), opening = HudStyle.Ui(new Color32(28, 30, 36, 255)),
                shot = HudStyle.Ui(new Color32(190, 190, 186, 255)), rib = HudStyle.Ui(new Color32(60, 80, 150, 255)),
                bolt = HudStyle.Ui(new Color32(250, 205, 50, 255)), lining = HudStyle.Ui(new Color32(235, 235, 225, 255)),
                bench = HudStyle.Ui(new Color32(255, 255, 255, 255)), floor = HudStyle.Ui(new Color32(110, 110, 110, 255));
            for (int j = 0; j < height; j++)
            {
                double y = bottom + (j + 0.5) * mPerPx;   // 텍스처의 j = 0이 아래쪽
                for (int i = 0; i < width; i++)
                {
                    double x = -half + (i + 0.5) * mPerPx;
                    Color32 c;
                    switch (section.Classify(x, y))
                    {
                        case SectionPart.Opening: c = opening; break;
                        case SectionPart.Shotcrete: c = shot; break;
                        case SectionPart.SteelRib: c = rib; break;
                        case SectionPart.Bolt: c = bolt; break;
                        case SectionPart.Lining: c = lining; break;
                        case SectionPart.BenchLine: c = bench; break;
                        case SectionPart.Floor: c = floor; break;
                        default: c = rock; break;
                    }
                    px[j * width + i] = c;
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        /// <summary>입구·사갱 이름표 (3D 위치를 화면 좌표로)</summary>
        private void DrawLabels()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            TunnelAlignment a = _plan.Alignment;
            Label(cam, a, 0.0, "서쪽 입구");
            Label(cam, a, a.Length, "동쪽 입구");
            if (_plan.UseMiddleAccess) Label(cam, a, a.Length * _spec.MiddleAccessFraction, "사갱 연결부 (가운데, 위치 가정)");
        }

        private void Label(Camera cam, TunnelAlignment a, double chainage, string text)
        {
            a.PointAt(chainage, out double e, out double n);
            Vector3 p = cam.WorldToScreenPoint(ToWorld(e, n, Ground(e, n) + 220.0));
            if (p.z <= 0f) return;
            // 상자 폭을 글 길이에 맞춘다 (긴 이름표가 상자 밖으로 나오던 것, 2026-11-21 캡처)
            float w = _tiny.CalcSize(new GUIContent(text)).x + 16f;
            Vector2 g = HudMode.MouseToGui(new Vector2(p.x, p.y));   // 3D → 화면 픽셀 → GUI 좌표 (글자 크기 배율)
            var r = new Rect(g.x - w / 2f, g.y - 12f, w, 22f);
            if (r.Overlaps(_textRect) || r.Overlaps(_profileRect) || r.Overlaps(ControlBar.BarRect)) return;
            HudStyle.DrawBox(r);
            GUI.Label(r, text, _tiny);
        }

        /// <summary>
        /// 아래 종단면: 가로 = 측점 0 ~ 길이, 세로 = 표고. 갈색 = 위성 지표 아래 땅, 굵은 선 = 터널 (판 곳은 패턴 색, 안 판 곳 회색),
        /// 노란 세로선 = 지금 막장, 흰 점선 = 사갱 연결부, 흰 짧은 기둥 = 치고 있는 라이닝 거푸집.
        /// </summary>
        private static Rect ProfileRect()
        {
            // 오른쪽 정보 글상자(화면 폭의 45 %)를 피해 왼쪽에 둔다 (2026-11-21)
            float right = HudMode.Show(HudMode.Item.Info) ? HudMode.GuiWidth - InfoBox.ReservedWidth - 8f : HudMode.GuiWidth - 12f;
            float width = Mathf.Clamp(right - 12f, 420f, 920f), height = HudMode.ProfileCollapsed ? 32f : 186f;   // 접으면 제목 한 줄
            return new Rect(12f, HudMode.GuiHeight - height - 12f, width, height);
        }

        private void DrawProfile()
        {
            Rect box = ProfileRect();
            float width = box.width, height = box.height, x0 = box.x, y0 = box.y;
            HudStyle.DrawBox(box);

            TunnelSchedule s = _plan.Schedule;
            TunnelAlignment a = _plan.Alignment;
            double dug = s.ExcavatedLength(_day);
            bool narrow = width < 800f || HudMode.ProfileCollapsed;   // 오른쪽 정보 글상자를 피해 좁아졌을 때 (2026-11-21)
            InfoBox.CollapseButton(box, HudMode.ProfileCollapsed, HudMode.ToggleProfileCollapsed, "종단면");
            HudLayout.Register(box);
            GUI.Label(new Rect(x0 + 10f, y0 + 6f, width - 96f, 22f),
                (_playing ? "▶ " : "") + _day.ToString("F0", Invariant) + "일째 / 관통 " + N0(s.FinishDay) + "일 · 라이닝 끝 " + N0(EndDay) + "일   "
                + (narrow ? "" : "튜브마다 ") + N0(dug) + " m (" + (dug / s.Length * 100.0).ToString("F1", Invariant) + " %)"
                + (narrow ? "" : "   종단면: 갈색 = 위성 지표, 선 = 터널 노면 "
                    + (a.Grade < 0 ? "내리막 " : "오르막 ") + System.Math.Abs(a.Grade * 100.0).ToString("0.00", Invariant) + " % (서 → 동)"), _small);

            if (HudMode.ProfileCollapsed) return;   // 접으면 제목 줄만 (2026-11-21)
            float px = x0 + 46f, py = y0 + 32f, pw = width - 60f, ph = height - 68f;
            double zMin = System.Math.Min(a.StartRoadElevation, a.EndRoadElevation) - 40.0;
            double zMax = zMin;
            for (int i = 0; i < _plan.Cover.Length; i++)
            {
                double ch = i * TunnelPlan.SampleStep;
                double g = _plan.Cover[i] + a.RoadElevation(ch) + _plan.Section.CrownAboveRoad;
                if (g > zMax) zMax = g;
            }
            zMax += 20.0;

            Color prev = GUI.color;
            int columns = Mathf.Max(2, Mathf.FloorToInt(pw));
            for (int c = 0; c < columns; c++)
            {
                double ch = a.Length * (c + 0.5) / columns;
                int idx = Mathf.Clamp((int)System.Math.Round(ch / TunnelPlan.SampleStep), 0, _plan.Cover.Length - 1);
                double road = a.RoadElevation(ch);
                double ground = _plan.Cover[idx] + road + _plan.Section.CrownAboveRoad;
                float yg = py + ph * (float)((zMax - ground) / (zMax - zMin));
                GUI.color = HudStyle.Ui(new Color(0.47f, 0.36f, 0.24f, 0.95f));
                GUI.DrawTexture(new Rect(px + c, yg, 1.3f, py + ph - yg), Texture2D.whiteTexture);   // 1.3: 글자 크기 배율에서 틈이 안 보이게
                float yr = py + ph * (float)((zMax - road - 8.0) / (zMax - zMin));
                GUI.color = HudStyle.Ui(_lining.IsLined(ch, _day) ? LinedColor
                    : s.IsExcavated(ch, _day) ? PatternColors[_plan.PatternAt(ch)] : RemainingColor);
                GUI.DrawTexture(new Rect(px + c, yr - 1f, 1.3f, 5f), Texture2D.whiteTexture);
            }

            // 사갱 연결부와 막장
            if (_plan.UseMiddleAccess)
            {
                float xm = px + pw * (float)_spec.MiddleAccessFraction;
                GUI.color = HudStyle.Ui(new Color(1f, 1f, 1f, 0.8f));
                for (float yy = py; yy < py + ph; yy += 8f) GUI.DrawTexture(new Rect(xm, yy, 1f, 4f), Texture2D.whiteTexture);
                GUI.color = prev;
                GUI.Label(new Rect(xm - 40f, py - 2f, 80f, 16f), "사갱", _tiny);
            }
            GUI.color = HudStyle.Ui(BeaconColor);
            for (int i = 0; i < s.FaceCount; i++)
            {
                if (!s.IsActive(i, _day)) continue;
                float xf = px + pw * (float)(s.FacePosition(i, _day) / a.Length);
                GUI.DrawTexture(new Rect(xf - 1f, py, 3f, ph), Texture2D.whiteTexture);
            }
            // 라이닝 거푸집 (치는 중인 것만): 노면 선 위에 흰 짧은 기둥
            if (_lining != null)
            {
                GUI.color = HudStyle.Ui(LinedColor);
                for (int k = 0; k < _lining.FormsPerTube; k++)
                {
                    double p = _lining.FormProgress(k, _day);
                    if (p <= 0 || p >= _lining.SegmentLength - 1e-6) continue;
                    double fc = _lining.FormPosition(k, _day);
                    float xf = px + pw * (float)(fc / a.Length);
                    float yr = py + ph * (float)((zMax - a.RoadElevation(fc) - 8.0) / (zMax - zMin));
                    GUI.DrawTexture(new Rect(xf - 2f, yr - 22f, 5f, 22f), Texture2D.whiteTexture);
                }
            }
            GUI.color = prev;

            // 축과 범례
            GUI.Label(new Rect(px - 6f, py + ph + 2f, 60f, 16f), "0 km", _small);
            GUI.Label(new Rect(px + pw - 40f, py + ph + 2f, 60f, 16f), (a.Length / 1000.0).ToString("0.0", Invariant) + " km", _small);
            GUI.Label(new Rect(x0 + 4f, py - 4f, 44f, 16f), "EL " + N0(zMax), _small);
            GUI.Label(new Rect(x0 + 4f, py + ph - 14f, 44f, 16f), N0(zMin), _small);
            // 범례: 넓으면 비율까지, 좁으면 이름만 "0 km"와 끝 거리 사이에 고르게 (2026-11-21)
            float lx = narrow ? px + 48f : px + 150f;
            float step = narrow ? (pw - 110f) / (PatternColors.Length + 2) : 78f;
            for (int k = 0; k < PatternColors.Length; k++)
            {
                GUI.color = HudStyle.Ui(PatternColors[k]);
                GUI.DrawTexture(new Rect(lx, py + ph + 6f, 12f, 10f), Texture2D.whiteTexture);
                GUI.color = prev;
                GUI.Label(new Rect(lx + 14f, py + ph + 1f, 120f, 16f),
                    SupportPattern.Standard[k].Name + (narrow ? "" : " " + (_plan.PatternShare(k) * 100.0).ToString("0", Invariant) + "%"), _small);
                lx += step;
            }
            GUI.color = HudStyle.Ui(BeaconColor);
            GUI.DrawTexture(new Rect(lx, py + ph + 6f, 12f, 10f), Texture2D.whiteTexture);
            GUI.color = prev;
            GUI.Label(new Rect(lx + 14f, py + ph + 1f, 120f, 16f), "막장", _small);
            lx += narrow ? step : 52f;
            GUI.color = HudStyle.Ui(LinedColor);
            GUI.DrawTexture(new Rect(lx, py + ph + 6f, 12f, 10f), Texture2D.whiteTexture);
            GUI.color = prev;
            GUI.Label(new Rect(lx + 14f, py + ph + 1f, 120f, 16f), "라이닝", _small);
        }
    }
}
