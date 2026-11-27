using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CivilTwin.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CivilTwin.Runtime
{
    /// <summary>
    /// 건물 시공 4D와 현장 학습 도구 (Phase 3). 부지 계획(GradingPlanTool) 위에서 동작한다.
    ///   - 부지 한가운데에 건물을 세우고 콘크리트 물량을 보여 준다 (Core/BuildingPlan)
    ///   - 토공 뒤에 건물 공정을 이어 붙여 CPM으로 계산한다 (Core/BuildingSchedule, Core/Cpm)
    ///   - 시간 막대의 날짜에 맞춰 건물이 층별로 올라간다
    ///   - 단계마다 쓰는 장비를 놓고 움직이며, "지금 무엇을 왜 하는가"를 글로 보여 준다
    ///     (Core/ConstructionGuide, Core/EquipmentMotion)
    ///
    /// 조작:
    ///   T      : 건물과 장비 보기 켜기 / 끄기 (부지를 먼저 놓아야 한다: G)
    ///   K / L  : 층수 줄이기 / 늘리기
    ///   F      : 부지 가까이 보기 ↔ 현장 전체 보기
    ///   P      : 공정표(CPM) 보기 / 숨기기
    ///   날짜는 부지 계획 도구의 키를 그대로 쓴다 (Space 재생, N / M 하루씩, B 처음, V 보는 면).
    ///
    /// 건물 치수와 공종별 일수는 계산 예를 위한 가정값이다.
    /// 장비의 움직임은 보기용이다 (공정 날짜와 속도가 다르다).
    /// 화면 글자는 임시(IMGUI)다.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class BuildingSiteTool : MonoBehaviour
    {
        private const int MaxFloors = 10;
        private const float EquipmentMargin = 22f;   // 건물 둘레에 장비가 서는 폭 (m)
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        // ---- 건물 가정값 (Core/BuildingPlan 참고) ----
        public double widthEastWest = 30.0;
        public double lengthNorthSouth = 20.0;
        public int floors = 5;
        public double floorHeight = 3.5;
        public int baysEastWest = 5;
        public int baysNorthSouth = 4;
        public double columnSize = 0.5;
        public double slabThickness = 0.25;
        public double foundationThickness = 0.8;

        // ---- 공종별 일수 가정값 (Core/BuildingSchedule 참고). 터파기 일수는 굴착기 작업량으로 계산한다. ----
        public double temporaryDays = 5.0;
        public double foundationDays = 7.0;
        public double craneSetupDays = 3.0;
        public double columnDaysPerFloor = 3.0;
        public double slabDaysPerFloor = 5.0;

        private GradingPlanTool _grading;
        private bool _on;
        private bool _near;
        private bool _showTable;

        private BuildingSchedule _schedule;
        private string _scheduleKey = "";
        private int[] _columnActivity;
        private int[] _slabActivity;

        // ---- 화면에 놓은 모양들 ----
        private PrimitiveKit _kit;
        private GameObject _root;
        private string _modelKey = "";
        private Transform _pit;
        private Transform _foundation;
        private Transform[] _columnGroups;
        private Transform[] _slabs;
        private float _slabWidth;

        private Transform _earthGroup;       // 토공 장비
        private Transform _buildingRoot;     // 건물 묶음 (댐 축조 중에는 감춘다)
        private ExcavatorRig _earthRig, _digRig;   // 굴착기 관절 (선회·붐·암·버킷)
        private Transform _earthExcavator, _earthTruck, _earthTruck2, _earthDozer, _earthRoller;
        private double _axisDegrees;   // 장비 배치 방향: 부지는 0 (동쪽), 댐은 댐 축 방향 (2026-11-20)
        private Transform _digGroup;         // 터파기 장비
        private Transform _digTruck;
        private Transform _concreteGroup;    // 콘크리트 타설 장비
        private Transform _mixerDrum;
        private Transform _crane, _craneMast, _craneTop;
        private float _haul;                 // 덤프트럭 편도 거리 (m)

        private GUIStyle _panel;
        private GUIStyle _cell;

        /// <summary>건물 보기가 켜져 있고 부지가 놓여 있으면 true. 댐 축조 중에는 건물이 없으므로 false.</summary>
        public bool IsOn => _on && _grading != null && _grading.HasPad && !_grading.IsDamMode;

        /// <summary>댐 축조 중 "장비 보기": 건물은 없이 토공 장비만 댐 위에 보인다 (2026-11-20).</summary>
        private bool EquipmentOnly => _on && _grading != null && _grading.HasPad && _grading.IsDamMode;

        /// <summary>장비(와 건물) 보기가 켜져 있으면 true — 메뉴 표시용</summary>
        public bool ShowingModels => IsOn || EquipmentOnly;

        /// <summary>토공과 건물을 합친 전체 공기 (일, 올림).</summary>
        public int ProjectDays
        {
            get
            {
                if (!IsOn) return 0;
                EnsureSchedule();
                return _schedule.ProjectDays;
            }
        }

        /// <summary>지금 조건의 건물 공정 (CPM). 부지가 없으면 null. 건물 보기를 꺼도 계산은 된다 (보고서용).</summary>
        public BuildingSchedule Schedule
        {
            get
            {
                if (_grading == null || !_grading.HasPad) return null;
                EnsureSchedule();
                return _schedule;
            }
        }

        /// <summary>지금 조건의 건물 계획 (크기·층수·물량)</summary>
        public BuildingPlan Plan => CurrentPlan();

        /// <summary>터파기 부피 (m³) = 건물 바닥 넓이 × 기초 두께. 대안 분석에서 쓴다.</summary>
        public double ExcavationVolume => CurrentPlan().FoundationVolume;

        /// <summary>공종별 일수 (터파기 일수는 대안마다 다시 계산하므로 0으로 둔다). 대안 분석에서 쓴다.</summary>
        public BuildingDurations BaseDurations()
        {
            return new BuildingDurations(
                temporaryDays, 0.0, foundationDays, craneSetupDays, columnDaysPerFloor, slabDaysPerFloor);
        }

        public void Initialize(GradingPlanTool grading)
        {
            DestroyModels();
            _grading = grading;
            _on = false;
            _near = false;
            _schedule = null;
            _scheduleKey = "";
        }

        private BuildingPlan CurrentPlan()
        {
            return new BuildingPlan(
                widthEastWest, lengthNorthSouth, floors, floorHeight,
                baysEastWest, baysNorthSouth, columnSize, slabThickness, foundationThickness);
        }

        /// <summary>
        /// 터파기 일수: 건물 바닥 넓이 × 기초 두께를 하루 굴착량으로 나눠 올림한다.
        /// 터파기는 표준품셈 주에 따라 작업효율에서 0.05를 뺀다 (하루 굴착량 × (E − 0.05) / E, 2026-11-20).
        /// </summary>
        private double ExcavationDays()
        {
            return BuildingSchedule.ExcavationDays(CurrentPlan().FoundationVolume, _grading.DailyVolume * _grading.TrenchRateFactor);
        }

        /// <summary>조건(층수, 토공 일수, 공종별 일수)이 바뀌었으면 공정을 다시 계산한다.</summary>
        private void EnsureSchedule()
        {
            int earthworkDays = _grading.EarthworkDays;
            double excavationDays = ExcavationDays();
            string key = string.Concat(
                floors.ToString(Invariant), "|", earthworkDays.ToString(Invariant), "|",
                excavationDays.ToString("R", Invariant), "|", temporaryDays.ToString("R", Invariant), "|",
                foundationDays.ToString("R", Invariant), "|", craneSetupDays.ToString("R", Invariant), "|",
                columnDaysPerFloor.ToString("R", Invariant), "|", slabDaysPerFloor.ToString("R", Invariant));
            if (_schedule != null && key == _scheduleKey) return;

            _schedule = new BuildingSchedule(floors, earthworkDays, new BuildingDurations(
                temporaryDays, excavationDays, foundationDays, craneSetupDays, columnDaysPerFloor, slabDaysPerFloor));
            _scheduleKey = key;

            _columnActivity = new int[floors];
            _slabActivity = new int[floors];
            for (int floor = 1; floor <= floors; floor++)
            {
                _columnActivity[floor - 1] = _schedule.IndexOf(BuildingWork.Columns, floor);
                _slabActivity[floor - 1] = _schedule.IndexOf(BuildingWork.Slab, floor);
            }
        }

        /// <summary>시간 막대에 쓰는 한 줄: 그 날짜에 하고 있는 작업들의 이름.</summary>
        public string StatusAt(double day)
        {
            if (!IsOn) return "";
            EnsureSchedule();
            if (day >= _schedule.ProjectDuration) return "공사 완료";

            var sb = new StringBuilder();
            for (int i = 0; i < _schedule.Activities.Count; i++)
            {
                if (!_schedule.IsActive(i, day)) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(_schedule.Activities[i].Name);
            }
            return sb.Length > 0 ? sb.ToString() : "대기";
        }

        private void Update()
        {
            if (_grading == null) return;
            if (!InputGate.Allowed) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (!_grading.HasPad)
            {
                _near = false;
                return;
            }

            if (keyboard.tKey.wasPressedThisFrame) ToggleBuilding();
            if (keyboard.fKey.wasPressedThisFrame) ToggleNearView();
            if (keyboard.kKey.wasPressedThisFrame) ChangeFloors(-1);
            if (keyboard.lKey.wasPressedThisFrame) ChangeFloors(+1);
            if (keyboard.pKey.wasPressedThisFrame) ToggleTable();
        }

        // ---------- 동작 (키와 화면 메뉴가 함께 쓴다) ----------

        /// <summary>부지 가까이 보기 중이면 true</summary>
        public bool IsNear => _near;

        /// <summary>공정표(CPM)를 보는 중이면 true</summary>
        public bool ShowingSchedule => _showTable;

        public void ToggleBuilding()
        {
            if (_grading == null || !_grading.HasPad) return;
            _on = !_on;
            _grading.ClampDayToTimeline();   // 끄면 시간 막대가 토공 공기로 짧아진다
        }

        public void ChangeFloors(int delta)
        {
            if (!IsOn) return;
            floors = Mathf.Clamp(floors + delta, 1, MaxFloors);
            _grading.ClampDayToTimeline();   // 층을 줄이면 전체 공기가 짧아진다
        }

        public void ToggleTable()
        {
            if (!IsOn) return;
            _showTable = !_showTable;
        }

        /// <summary>부지 가까이 보기 ↔ 현장 전체 보기. 가까이 볼 때는 건물이 화면에 꽉 차는 거리로 간다.</summary>
        public void ToggleNearView()
        {
            if (_grading == null || !_grading.HasPad) return;
            var controller = GetComponent<OrbitCameraController>();
            if (controller == null) return;

            _near = !_near;
            if (!_near)
            {
                controller.ResetView();
                return;
            }

            GradingPad pad = _grading.Pad;

            // 토공 중(토공 장비가 보일 때)에는 장비 쪽으로 더 다가간다: 굴착기와 싣는 트럭이 함께 보이게 (2026-11-20).
            // 건물 공사 중에는 건물 전체가 보이게.
            if (_earthGroup != null && _earthGroup.gameObject.activeInHierarchy)
            {
                Vector3 p = SurfacePoint(pad, _grading.IsDamMode ? 0.0 : 10.0, _grading.IsDamMode ? 0.0 : 3.0);
                controller.FocusOn(new LocalPoint(p.x, p.y, p.z), _grading.IsDamMode ? 110.0 : 40.0, 12.0);
                return;
            }

            BuildingPlan plan = CurrentPlan();
            double size = System.Math.Max(System.Math.Max(plan.WidthEastWest, plan.LengthNorthSouth), plan.Height);
            LocalPoint target = _grading.Frame.ToLocal(
                pad.CenterEasting, pad.CenterNorthing, pad.PadElevation + plan.Height / 3.0);
            controller.FocusOn(target, size * 3.0, 12.0);
        }

        private void LateUpdate()
        {
            if (_grading == null) return;

            // 건물 글상자가 왼쪽 아래의 표고 범례 자리까지 내려오므로, 건물 보기 중에는 범례를 감춘다.
            var legend = GetComponent<ElevationLegend>();
            var alternatives = GetComponent<AlternativeTool>();
            bool comparing = alternatives != null && alternatives.ShowingTable;   // 비교표도 왼쪽 아래에 나온다
            // 부지를 놓으면 지형이 절·성토 색으로 바뀌고 왼쪽 아래를 누적 그래프·시간 막대가 쓰므로 범례도 감춘다
            // (2026-11-20 자동 시연 캡처: 작은 화면에서 그래프가 범례 위에 겹침).
            if (legend != null) legend.enabled = !IsOn && !comparing && !_grading.HasPad;

            // 원지반(공사 전)을 볼 때는 건물을 감춘다.
            if ((!IsOn && !EquipmentOnly) || _grading.ShowsGround)
            {
                DestroyModels();
                return;
            }

            EnsureSchedule();
            GradingPad pad = _grading.Pad;

            string modelKey = string.Concat(
                _scheduleKey, "|", pad.CenterEasting.ToString("R", Invariant), "|", pad.CenterNorthing.ToString("R", Invariant),
                "|", pad.PadElevation.ToString("R", Invariant), "|", pad.WidthEastWest.ToString("R", Invariant),
                "|", pad.LengthNorthSouth.ToString("R", Invariant));
            if (_root == null || modelKey != _modelKey)
            {
                DestroyModels();
                BuildModels(pad);
                _modelKey = modelKey;
            }

            // 계획면(공사 후)을 볼 때는 다 지은 모습, 공사 중을 볼 때는 그 날짜의 모습.
            double day = _grading.ShowsStage ? _grading.Day : double.PositiveInfinity;
            ApplyProgress(day);
            if (EquipmentOnly)
            {
                // 댐 축조: 건물·터파기·콘크리트·크레인은 감추고, 토공 장비만 쌓는 동안 댐 위(지금 보이는 성토면)에 둔다.
                _buildingRoot.gameObject.SetActive(false);
                _digGroup.gameObject.SetActive(false);
                _concreteGroup.gameObject.SetActive(false);
                _crane.gameObject.SetActive(false);
                _earthGroup.gameObject.SetActive(_grading.ShowsStage && _grading.Day < _grading.EarthworkDays);
            }
            MoveEquipment(pad, day, Time.unscaledTime + EquipmentClockOffset);
        }

        // ---------- 모양 만들기 ----------

        private Vector3 PadPoint(GradingPad pad, double east, double north, double up)
        {
            return _grading.Frame.ToLocal(
                pad.CenterEasting + east, pad.CenterNorthing + north, pad.PadElevation + up).ToVector3();
        }

        /// <summary>
        /// 부지 중심에서 (동, 북)만큼 떨어진 곳의, 지금 보이는 지표면 위의 점.
        /// 댐 모드에서는 (동, 북)을 댐 축 방향으로 돌린다: "동" = 축을 따라, "북" = 축의 왼쪽(상류).
        /// </summary>
        private Vector3 SurfacePoint(GradingPad pad, double east, double north)
        {
            double a = _axisDegrees * System.Math.PI / 180.0;
            double easting = pad.CenterEasting + east * System.Math.Cos(a) - north * System.Math.Sin(a);
            double northing = pad.CenterNorthing + east * System.Math.Sin(a) + north * System.Math.Cos(a);
            double elevation = pad.PadElevation;

            ElevationGrid surface = _grading.ShownSurface;
            if (surface != null && surface.TryGetElevationAt(easting, northing, out double found)
                && !double.IsNaN(found))
            {
                elevation = found;
            }
            return _grading.Frame.ToLocal(easting, northing, elevation).ToVector3();
        }

        private void BuildModels(GradingPad pad)
        {
            BuildingPlan plan = CurrentPlan();
            _kit = new PrimitiveKit();
            _root = new GameObject("BuildingSite (demo)");
            Transform root = _root.transform;

            float w = (float)plan.WidthEastWest;
            float l = (float)plan.LengthNorthSouth;
            float cs = (float)plan.ColumnSize;
            float ts = (float)plan.SlabThickness;
            float tf = (float)plan.FoundationThickness;
            float clear = (float)plan.ColumnClearHeight;
            float h = (float)plan.FloorHeight;
            _slabWidth = w;

            // ---- 건물: 원점 = 건물 한가운데의 부지 표면. X = 동, Z = 북 ----
            Transform building = _kit.Group(root, "건물", PadPoint(pad, 0, 0, 0));
            _buildingRoot = building;

            // 터파기 자리(어두운 흙색)와 기초. 기초는 땅속에 있어서, 윗면만 부지 위로 8 cm 보이게 둔다.
            _pit = _kit.Box(building, "터파기", new Vector3(0f, 0.02f, 0f), new Vector3(w + 2f, 0.04f, l + 2f), PrimitiveKit.Soil);
            _foundation = _kit.Box(building, "기초", new Vector3(0f, 0.08f - tf / 2f, 0f), new Vector3(w, tf, l), PrimitiveKit.ConcreteDark);

            _columnGroups = new Transform[plan.Floors];
            _slabs = new Transform[plan.Floors];
            for (int floor = 0; floor < plan.Floors; floor++)
            {
                float y0 = floor * h;

                // 기둥 묶음: 묶음의 Y 배율을 0 → 1로 키우면 기둥이 아래에서 위로 자란다.
                // 화면에서는 기둥이 슬래브 밖으로 튀어나오지 않게 가장자리 기둥을 반 개만큼 안으로 들였다 (표시용).
                Transform columns = _kit.Group(building, (floor + 1) + "층 기둥", new Vector3(0f, y0, 0f));
                for (int i = 0; i <= plan.BaysEastWest; i++)
                {
                    for (int j = 0; j <= plan.BaysNorthSouth; j++)
                    {
                        float x = -w / 2f + cs / 2f + i * (w - cs) / plan.BaysEastWest;
                        float z = -l / 2f + cs / 2f + j * (l - cs) / plan.BaysNorthSouth;
                        _kit.Box(columns, "기둥", new Vector3(x, clear / 2f, z), new Vector3(cs, clear, cs), PrimitiveKit.Concrete);
                    }
                }
                _columnGroups[floor] = columns;

                _slabs[floor] = _kit.Box(building, (floor + 1) + "층 슬래브",
                    new Vector3(0f, y0 + clear + ts / 2f, 0f), new Vector3(w, ts, l), PrimitiveKit.Concrete);
            }

            // ---- 토공 장비: 부지 한가운데 (건물을 짓기 전이라 비어 있다) ----
            _haul = Mathf.Clamp((float)pad.WidthEastWest / 2f - 12f, 12f, 60f);
            _earthGroup = _kit.Group(root, "토공 장비", Vector3.zero);
            _earthRig = EquipmentModels.Excavator(_kit, _earthGroup);
            _earthExcavator = _earthRig.Root;
            _earthTruck = EquipmentModels.DumpTruck(_kit, _earthGroup);
            _earthTruck2 = EquipmentModels.DumpTruck(_kit, _earthGroup);
            _earthDozer = EquipmentModels.Bulldozer(_kit, _earthGroup);
            _earthRoller = EquipmentModels.Roller(_kit, _earthGroup);

            // ---- 터파기 장비: 건물 동쪽 ----
            _digGroup = _kit.Group(root, "터파기 장비", Vector3.zero);
            _digRig = EquipmentModels.Excavator(_kit, _digGroup);
            Transform digExcavator = _digRig.Root;
            digExcavator.position = PadPoint(pad, w / 2f + 5f, 0, 0);
            digExcavator.eulerAngles = new Vector3(0f, -90f, 0f);    // 서쪽(파는 자리)을 본다
            _digTruck = EquipmentModels.DumpTruck(_kit, _digGroup);

            // ---- 콘크리트 장비: 건물 남쪽. 펌프카 뒤(서쪽)의 호퍼에 레미콘 트럭이 뒤를 댄다 ----
            _concreteGroup = _kit.Group(root, "콘크리트 장비", Vector3.zero);
            float pumpNorth = -l / 2f - 8f;
            Transform pump = EquipmentModels.PumpTruck(_kit, _concreteGroup, (float)plan.Height + 3f, l / 2f + 8f);
            pump.position = PadPoint(pad, w / 4f, pumpNorth, 0);
            pump.eulerAngles = new Vector3(0f, 90f, 0f);              // 동쪽을 본다 → 붐은 북쪽(건물)으로
            Transform mixer = EquipmentModels.MixerTruck(_kit, _concreteGroup, out _mixerDrum);
            mixer.position = PadPoint(pad, w / 4f - 10f, pumpNorth, 0);
            mixer.eulerAngles = new Vector3(0f, 270f, 0f);

            // ---- 타워크레인: 건물 서쪽 ----
            _crane = EquipmentModels.TowerCrane(_kit, root, (float)plan.Height + 12f, w + 14f, out _craneMast, out _craneTop);
            _crane.position = PadPoint(pad, -w / 2f - 6f, 0, 0);
        }

        private void DestroyModels()
        {
            if (_root != null) Destroy(_root);
            _root = null;
            _modelKey = "";
            if (_kit != null) _kit.DestroyMaterials();
            _kit = null;
        }

        private void OnDestroy()
        {
            DestroyModels();
        }

        // ---------- 날짜에 맞춰 모양 바꾸기 ----------

        private void ApplyProgress(double day)
        {
            BuildingSchedule s = _schedule;
            double excavation = s.Fraction(s.IndexOf(BuildingWork.Excavation), day);
            double foundation = s.Fraction(s.IndexOf(BuildingWork.Foundation), day);

            // 터파기 자리는 서쪽에서 동쪽으로 넓어지고, 기초가 다 되면 가려지므로 감춘다.
            GrowEast(_pit, _slabWidth + 2f, foundation >= 1.0 ? 0.0 : excavation);
            GrowEast(_foundation, _slabWidth, foundation);

            for (int floor = 0; floor < _columnGroups.Length; floor++)
            {
                double columns = s.Fraction(_columnActivity[floor], day);
                _columnGroups[floor].gameObject.SetActive(columns > 0.0);
                _columnGroups[floor].localScale = new Vector3(1f, Mathf.Max(0.001f, (float)columns), 1f);

                GrowEast(_slabs[floor], _slabWidth, s.Fraction(_slabActivity[floor], day));
            }

            // 장비는 그 작업을 하는 날에만 보인다.
            _earthGroup.gameObject.SetActive(s.IsActive(s.IndexOf(BuildingWork.Earthwork), day));
            _digGroup.gameObject.SetActive(s.IsActive(s.IndexOf(BuildingWork.Excavation), day));

            bool pouring = s.IsActive(s.IndexOf(BuildingWork.Foundation), day);
            for (int floor = 0; floor < _columnActivity.Length && !pouring; floor++)
            {
                pouring = s.IsActive(_columnActivity[floor], day) || s.IsActive(_slabActivity[floor], day);
            }
            _concreteGroup.gameObject.SetActive(pouring);

            // 타워크레인: 설치하는 동안 기둥이 자라고, 다 서면 팔이 달린다. 공사가 끝나면 치운다.
            double crane = s.Fraction(s.IndexOf(BuildingWork.CraneSetup), day);
            _crane.gameObject.SetActive(crane > 0.0 && day < s.ProjectDuration);
            _craneMast.localScale = new Vector3(1f, Mathf.Max(0.001f, (float)crane), 1f);
            _craneTop.gameObject.SetActive(crane >= 1.0);
        }

        /// <summary>상자를 서쪽 끝에서 동쪽으로 fraction(0 ~ 1)만큼만 보이게 한다 (콘크리트를 부어 나가는 모습).</summary>
        private static void GrowEast(Transform box, float fullWidth, double fraction)
        {
            float f = Mathf.Clamp01((float)fraction);
            box.gameObject.SetActive(f > 0f);
            if (f <= 0f) return;

            Vector3 scale = box.localScale;
            Vector3 position = box.localPosition;
            scale.x = fullWidth * f;
            position.x = -fullWidth / 2f + fullWidth * f / 2f;
            box.localScale = scale;
            box.localPosition = position;
        }

        /// <summary>
        /// 장비 시계를 앞당기는 값 (초). 평소에는 0. 자동 시연 명령 "equipclock 263"이 "지금 장비 시각 = 263초"가 되게 맞춘다
        /// (트럭이 흙을 부리는 순간처럼 몇 분 뒤의 동작을 바로 찍으려고, 2026-11-20).
        /// </summary>
        public static double EquipmentClockOffset;

        /// <summary>자동 시연용: 토공 장비의 위치 (truck0, truck1, excavator, dozer, roller). 없거나 꺼져 있으면 false.</summary>
        public bool TryGetEquipmentPosition(string name, out Vector3 position)
        {
            Transform t = name switch
            {
                "truck0" => _earthTruck,
                "truck1" => _earthTruck2,
                "excavator" => _earthExcavator,
                "dozer" => _earthDozer,
                "roller" => _earthRoller,
                _ => null,
            };
            position = t != null ? t.position : Vector3.zero;
            return t != null && t.gameObject.activeInHierarchy;
        }

        /// <summary>장비의 움직임 (보기용). seconds는 화면에 흐르는 시간이다.</summary>
        private void MoveEquipment(GradingPad pad, double day, double seconds)
        {
            _axisDegrees = _grading.IsDamMode ? _grading.Dam.AxisDegrees : 0.0;
            // 굴착기 한 사이클 = 표준품셈 사이클 시간 Cm: 굴착 → 적재 선회 → 덤프 → 복귀 선회 (Core/ExcavatorCycle)
            ExcavatorPose pose = ExcavatorCycle.PoseAt(seconds, _grading.cycleSeconds, 90.0);

            if (_earthGroup.gameObject.activeSelf)
            {
                if (_grading.IsDamMode)
                {
                    // 댐 축조: 흙은 토취장에서 굴착기로 실어 오므로 댐 위에는 굴착기가 없다.
                    // 트럭 두 대가 댐 축을 따라 실어 와 부리고(갈 때 가득, 올 때 빈 차), 불도저가 펴고, 롤러가 다진다.
                    // 2026-11-20: 장비는 지금 쌓은 층의 평평한 면 안에서만 다닌다 (공사 초반 좁은 층에서 계곡 비탈을 타고 오르지 않게).
                    _earthExcavator.gameObject.SetActive(false);
                    double along = FlatExtent(pad, 1.0, 0.0, _haul), back = FlatExtent(pad, -1.0, 0.0, _haul);
                    double up = FlatExtent(pad, 0.0, 1.0, 24.0), down = FlatExtent(pad, 0.0, -1.0, 24.0);
                    double lo = -System.Math.Max(0.0, back - 6.0), hi = System.Math.Max(0.0, along - 6.0);
                    if (hi - lo < 4.0) { double mid = (hi + lo) / 2.0; lo = mid - 2.0; hi = mid + 2.0; }
                    double len = hi - lo;
                    double laneUp = System.Math.Min(2.5, System.Math.Max(0.0, up - 2.0));
                    double laneDown = -System.Math.Min(2.5, System.Math.Max(0.0, down - 2.0));
                    PlaceShuttle(_earthTruck, pad, lo, laneUp, len, 6.0, seconds, true, true, true);
                    PlaceShuttle(_earthTruck2, pad, lo, laneDown, len, 6.0, seconds + len / 6.0, true, true, true);
                    PlaceShuttle(_earthDozer, pad, lo + 0.1 * len, -System.Math.Min(10.0, System.Math.Max(0.0, down - 3.0)),
                        0.4 * len, 1.5, seconds, false, true);
                    PlaceShuttle(_earthRoller, pad, lo + 0.5 * len, -System.Math.Min(17.0, System.Math.Max(0.0, down - 3.0)),
                        0.4 * len, 1.0, seconds + 7.0, false, true);
                }
                else
                {
                    // 굴착기는 북쪽을 보고 파다가 동쪽의 덤프트럭 쪽으로 돌아 싣는다. 트럭 두 대가 번갈아 받는다:
                    // 한 대에 버킷 n번(표준품셈 n = 트럭 적재량 ÷ (q × K) 올림) → 흙이 한 칸씩 찬다 (Core/TruckLoadingCycle).
                    _earthExcavator.gameObject.SetActive(true);
                    _earthExcavator.position = SurfacePoint(pad, 0, 0);
                    TruckLoadingCycle loading = CurrentLoading();
                    ExcavatorPose loadPose = ExcavatorCycle.PoseAt(loading.ExcavatorTime(seconds), _grading.cycleSeconds, 90.0);
                    _earthRig.Pose(loadPose.Swing, loadPose.BoomRaise, loadPose.ArmOut, loadPose.BucketOpen);
                    PlaceLoadingTruck(_earthTruck, pad, loading.TruckAt(seconds, 0));
                    PlaceLoadingTruck(_earthTruck2, pad, loading.TruckAt(seconds, 1));
                    PlaceShuttle(_earthDozer, pad, -_haul / 2.0, -10.0, _haul, 1.5, seconds, false, true);
                    PlaceShuttle(_earthRoller, pad, -_haul / 2.0, -17.0, _haul, 1.0, seconds + 7.0, false, true);
                }
            }

            if (_digGroup.gameObject.activeSelf)
            {
                // 굴착기는 서쪽(터파기 자리)을 보고 파다가 북쪽의 덤프트럭 쪽으로 돈다.
                _digRig.Pose(pose.Swing, pose.BoomRaise, pose.ArmOut, pose.BucketOpen);
                PlaceShuttle(_digTruck, pad, _slabWidth / 2.0 + 6.0, 8.0, _haul, 6.0, seconds, true, false, true);
            }

            if (_concreteGroup.gameObject.activeSelf)
            {
                _mixerDrum.localEulerAngles = new Vector3(0f, (float)(seconds * 60.0 % 360.0), 0f);   // 1분에 10바퀴
            }

            if (_craneTop.gameObject.activeInHierarchy)
            {
                // 팔은 동쪽(건물)을 중심으로 좌우 50°씩, 40초에 한 번 오간다.
                float turn = (float)(EquipmentMotion.BackAndForth(seconds, 40.0) * 100.0 - 50.0);
                _craneTop.localEulerAngles = new Vector3(0f, 90f + turn, 0f);
            }
        }

        /// <summary>
        /// 동쪽으로 갔다가 돌아오는 장비를 놓는다. 출발점 = 부지 중심에서 (startEast, north).
        /// turnsAround가 true면 돌아올 때 머리를 돌리고(덤프트럭), false면 후진한다(불도저·롤러).
        /// </summary>
        private void PlaceShuttle(Transform machine, GradingPad pad, double startEast, double north,
            double oneWay, double speed, double seconds, bool turnsAround, bool followSurface, bool loadedOutbound = false)
        {
            double along = EquipmentMotion.ShuttleDistance(seconds, oneWay, speed);
            bool returning = EquipmentMotion.ShuttleIsReturning(seconds, oneWay, speed);

            machine.position = followSurface
                ? SurfacePoint(pad, startEast + along, north)
                : PadPoint(pad, startEast + along, north, 0);
            float heading = (float)(90.0 - _axisDegrees);
            machine.eulerAngles = new Vector3(0f, turnsAround && returning ? heading + 180f : heading, 0f);
            if (loadedOutbound) EquipmentModels.SetTruckSoil(machine, returning ? 0.0 : 1.0);
        }

        /// <summary>
        /// 지금 보이는 지표에서 부지 중심(댐이면 축 가운데)과 높이 차이 1.5 m 안으로 평평하게 이어지는 거리 (m).
        /// 방향은 (동, 북) — 댐이면 SurfacePoint가 축 방향으로 돌린다. 3 m 간격으로 maxDistance까지 본다.
        /// </summary>
        private double FlatExtent(GradingPad pad, double dirEast, double dirNorth, double maxDistance)
        {
            float y0 = SurfacePoint(pad, 0.0, 0.0).y;
            double reach = 0.0;
            for (double d = 3.0; d <= maxDistance + 1e-9; d += 3.0)
            {
                if (Mathf.Abs(SurfacePoint(pad, dirEast * d, dirNorth * d).y - y0) > 1.5f) break;
                reach = d;
            }
            return reach;
        }

        // ---------- 굴착기 → 트럭 적재 (2026-11-20) ----------

        private const double LoadingEast = 7.5;    // 싣는 자리: 굴착기에서 동쪽 7.5 m (선회 90°의 버킷 아래)
        private const double ReturnLane = 9.0;     // 돌아오는 길: 북쪽 9 m
        private const double TruckSpeed = 6.0;     // 보기용 속도 (m/초)
        private const double ExchangeSeconds = 4.0;
        private const double DumpSeconds = 8.0;   // 흙 부리기: 적재함을 들고, 쏟고, 내린다 (보기용. 표준품셈 t3 적하는 1.05분)

        /// <summary>지금 조건의 적재 모습: 버킷 횟수 n은 표준품셈 식(트럭 적재량 ÷ (q × K))을 올림한다.</summary>
        private TruckLoadingCycle CurrentLoading()
        {
            double n = _grading.Truck.LoaderCycles(1.0 / _grading.volumeConversion, _grading.bucketCapacity, _grading.bucketFactor);
            return new TruckLoadingCycle(_grading.cycleSeconds, TruckLoadingCycle.BucketsFor(n), ExchangeSeconds,
                _haul / TruckSpeed, ExchangeSeconds, DumpSeconds);
        }

        /// <summary>
        /// 트럭을 단계에 맞게 놓는다: 싣기(굴착기 옆, 동쪽을 봄) → 동쪽으로 나감 → 끝에서 돌아 뒤를 동쪽 끝으로 →
        /// 멈춰 적재함을 들고 흙을 부림 → 북쪽 길로 돌아옴 → 기다림 → 서쪽으로 돌아 싣는 자리로.
        /// </summary>
        private void PlaceLoadingTruck(Transform truck, GradingPad pad, TruckState state)
        {
            double far = LoadingEast + _haul;
            double r = ReturnLane / 2.0;
            double u = state.Progress;
            double x, z, dx, dz;
            switch (state.Phase)
            {
                case TruckPhase.Loading:
                    x = LoadingEast; z = 0.0; dx = 1.0; dz = 0.0;
                    break;
                case TruckPhase.Out:
                    x = LoadingEast + _haul * u; z = 0.0; dx = 1.0; dz = 0.0;
                    break;
                case TruckPhase.FarTurn:   // 동쪽 끝에서 반원 (북쪽으로 돌아 서쪽을 봄)
                    x = far + r * System.Math.Sin(System.Math.PI * u); z = r - r * System.Math.Cos(System.Math.PI * u);
                    dx = System.Math.Cos(System.Math.PI * u); dz = System.Math.Sin(System.Math.PI * u);
                    break;
                case TruckPhase.Dumping:   // 반원을 다 돈 자리 (서쪽을 봄 → 뒤가 동쪽 끝)
                    x = far; z = ReturnLane; dx = -1.0; dz = 0.0;
                    break;
                case TruckPhase.Back:
                    x = far - _haul * u; z = ReturnLane; dx = -1.0; dz = 0.0;
                    break;
                case TruckPhase.Waiting:
                    x = LoadingEast; z = ReturnLane; dx = -1.0; dz = 0.0;
                    break;
                default:                   // InTurn: 서쪽으로 반원 (남쪽으로 돌아 동쪽을 봄)
                    x = LoadingEast - r * System.Math.Sin(System.Math.PI * u); z = r + r * System.Math.Cos(System.Math.PI * u);
                    dx = -System.Math.Cos(System.Math.PI * u); dz = -System.Math.Sin(System.Math.PI * u);
                    break;
            }
            truck.position = SurfacePoint(pad, x, z);
            truck.eulerAngles = new Vector3(0f, (float)(System.Math.Atan2(dx, dz) * 180.0 / System.Math.PI - _axisDegrees), 0f);
            EquipmentModels.SetTruckSoil(truck, state.Soil);
            EquipmentModels.SetTruckBed(truck, state.BedRaise);
        }

        // ---------- 화면 글자 (임시 IMGUI) ----------

        private void OnGUI()
        {
            HudMode.ApplyMatrix();   // 글자 크기 (메뉴 [화면])
            if (_grading == null || !_grading.HasPad) return;
            if (!HudMode.Show(HudMode.Item.Info)) return;

            // 대안 비교표가 떠 있는 동안에는 자리를 비켜 준다 (비교표가 왼쪽 아래를 쓴다).
            var alternatives = GetComponent<AlternativeTool>();
            if (alternatives != null && alternatives.ShowingTable) return;

            if (_panel == null)
            {
                _panel = HudStyle.Panel(14, wordWrap: true);   // 모든 글상자 공통 모양 (2026-11-20)
                _panel.padding.right = 84;   // 오른쪽 위 [접기] 버튼 자리 (2026-11-21)

                _cell = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = false };
                _cell.normal.textColor = HudStyle.Ui(Color.white);
            }

            // 왼쪽 위의 측정 글상자 바로 아래에 둔다.
            var measure = GetComponent<MeasureTool>();
            float top = (measure != null ? measure.GuiBottom : 10f) + 8f;

            // P를 누르면 설명 글 대신 같은 자리에 공정표를 보여 준다
            // (옆에 따로 띄우면 작은 화면에서 오른쪽 글상자·그래프와 겹친다).
            if (IsOn && _showTable)
            {
                EnsureSchedule();
                DrawTable(10f, top);
                return;
            }

            // 건물을 끄면 상자도 그리지 않는다 (예전의 "[현장 보기]" 안내는 위쪽 메뉴가 대신한다).
            // 기본은 짧은 요약, "글상자 자세히"일 때만 무엇을·왜·장비 설명 전체 (2026-11-20 개발자 사용 의견).
            if (!IsOn) return;   // 건물을 끄면, 또는 댐 축조 중이면(건물 없음) 상자를 그리지 않는다
            string text = HudMode.Detail ? BuildText() : BuildShortText();
            float width = (HudMode.Detail ? 470f : 400f) + 72f;   // + [접기] 버튼 자리 (오른쪽 여백 84)
            if (HudMode.BuildingCollapsed)   // 접으면 첫 줄만 (2026-11-21)
            {
                int nl = text.IndexOf('\n');
                if (nl >= 0) text = text.Substring(0, nl).TrimEnd('\r');
            }
            float height = _panel.CalcHeight(new GUIContent(text), width);
            var rect = new Rect(10f, top, width, Mathf.Max(height, 30f));
            GUI.Box(rect, text, _panel);
            HudLayout.Register(rect);
            InfoBox.CollapseButton(rect, HudMode.BuildingCollapsed, HudMode.ToggleBuildingCollapsed, "건물");
        }

        /// <summary>건물 글상자의 짧은 판: 규모·공기 한 줄, 지금 하는 일 한두 줄.</summary>
        private string BuildShortText()
        {
            EnsureSchedule();
            BuildingPlan plan = CurrentPlan();
            BuildingSchedule s = _schedule;
            var sb = new StringBuilder();
            sb.AppendLine("[건물] " + plan.Floors + "층, 콘크리트 " + plan.TotalConcreteVolume.ToString("N1", Invariant)
                + " m³, 전체 공기 " + s.ProjectDays + "일");

            if (_grading.ShowsGround)
            {
                sb.Append("원지반 보기라 건물을 감췄다");
            }
            else if (!_grading.ShowsStage)
            {
                sb.Append("다 지은 모습 — 메뉴 [재생]으로 공사 순서대로 본다");
            }
            else
            {
                double day = _grading.Day;
                bool any = false;
                for (int i = 0; i < s.Activities.Count && day < s.ProjectDuration; i++)
                {
                    if (!s.IsActive(i, day)) continue;
                    WorkGuide guide = ConstructionGuide.For(s.WorkOf(i));
                    var names = new List<string>();
                    foreach (EquipmentKind kind in guide.Equipment) names.Add(ConstructionGuide.EquipmentName(kind));
                    sb.AppendLine("▶ " + s.Activities[i].Name + " " + (s.Fraction(i, day) * 100.0).ToString("F0", Invariant)
                        + " %  — " + string.Join(" · ", names));
                    any = true;
                }
                if (!any) sb.AppendLine(day >= s.ProjectDuration ? "공사 완료" : "대기");
                sb.Append("(무엇을·왜: 메뉴 [화면] → 글상자 자세히)");
            }
            return sb.ToString();
        }

        private string BuildText()
        {
            if (!_on)
            {
                return "[현장 보기]  위쪽 메뉴 [건물]: 건물과 장비 보기 (T)";
            }

            EnsureSchedule();
            BuildingPlan plan = CurrentPlan();
            GradingPad pad = _grading.Pad;
            BuildingSchedule s = _schedule;

            var sb = new StringBuilder();
            sb.AppendLine("[건물 계획]   (조작은 위쪽 메뉴 [건물])");
            sb.AppendLine("건물  동서 " + plan.WidthEastWest.ToString("0.#", Invariant) + " m × 남북 "
                + plan.LengthNorthSouth.ToString("0.#", Invariant) + " m, " + plan.Floors + "층 (층고 "
                + plan.FloorHeight.ToString("0.#", Invariant) + " m, 높이 " + plan.Height.ToString("0.#", Invariant) + " m)");
            sb.AppendLine("콘크리트  기초 " + plan.FoundationVolume.ToString("N1", Invariant)
                + " + 기둥 " + plan.TotalColumnVolume.ToString("N1", Invariant)
                + " + 슬래브 " + plan.TotalSlabVolume.ToString("N1", Invariant)
                + " = " + plan.TotalConcreteVolume.ToString("N1", Invariant) + " m³");
            sb.AppendLine("공기  토공 " + _grading.EarthworkDays + "일 → 전체 " + s.ProjectDays + "일 (CPM)");
            sb.AppendLine("주공정선  " + CriticalPathText(s));
            sb.AppendLine("여유  " + FloatText(s));
            sb.AppendLine("※ 건물 치수와 공종별 일수는 가정값이다.");

            if (pad.WidthEastWest < plan.WidthEastWest + 2 * EquipmentMargin
                || pad.LengthNorthSouth < plan.LengthNorthSouth + 2 * EquipmentMargin)
            {
                sb.AppendLine("※ 부지가 좁아 장비가 부지 밖에 놓일 수 있다 (0 키로 부지를 넓힌다).");
            }

            sb.AppendLine();
            if (_grading.ShowsGround)
            {
                sb.Append("보는 면이 원지반(공사 전)이라 건물을 감췄다. V로 바꾼다.");
            }
            else if (!_grading.ShowsStage)
            {
                sb.Append("보는 면이 계획면이라 다 지은 모습이다. Space를 누르면 공사 순서대로 재생한다.");
            }
            else
            {
                AppendNow(sb, s, _grading.Day);
            }
            return sb.ToString();
        }

        /// <summary>"지금 하는 일": 그 날짜에 하고 있는 작업마다 무엇을·왜·어떤 장비로 하는지 적는다.</summary>
        private static void AppendNow(StringBuilder sb, BuildingSchedule s, double day)
        {
            sb.AppendLine("[지금 하는 일]  " + day.ToString("F1", Invariant) + "일째");
            if (day >= s.ProjectDuration)
            {
                sb.Append("공사 완료. B를 누르면 처음으로 돌아간다.");
                return;
            }

            bool any = false;
            for (int i = 0; i < s.Activities.Count; i++)
            {
                if (!s.IsActive(i, day)) continue;
                any = true;

                WorkGuide guide = ConstructionGuide.For(s.WorkOf(i));
                string mark = s.Result.Critical[i]
                    ? "주공정"
                    : "여유 " + s.Result.TotalFloat[i].ToString("0.#", Invariant) + "일";
                sb.AppendLine("▶ " + s.Activities[i].Name + "  " + (s.Fraction(i, day) * 100.0).ToString("F0", Invariant)
                    + " %  (" + mark + ")");
                sb.AppendLine("   무엇을: " + guide.What);
                sb.AppendLine("   왜: " + guide.Why);
                foreach (EquipmentKind kind in guide.Equipment)
                {
                    sb.AppendLine("   · " + ConstructionGuide.EquipmentName(kind) + " — " + ConstructionGuide.EquipmentRole(kind));
                }
            }

            if (!any) sb.AppendLine("하는 작업이 없다.");
            sb.Append("※ 장비 움직임은 보기용이다. 설명 글은 출처 확인 전이다.");
        }

        /// <summary>주공정선을 한 줄로. 층별 작업은 한 덩어리로 줄여 쓴다.</summary>
        private static string CriticalPathText(BuildingSchedule s)
        {
            var sb = new StringBuilder();
            bool floorsCritical = false;
            for (int i = 0; i < s.Activities.Count; i++)
            {
                if (!s.Result.Critical[i]) continue;
                if (s.FloorOf(i) > 0)
                {
                    floorsCritical = true;
                    continue;
                }
                if (sb.Length > 0) sb.Append(" → ");
                sb.Append(s.Activities[i].Name);
            }
            if (floorsCritical) sb.Append(" → 층별 기둥·슬래브");
            return sb.ToString();
        }

        private static string FloatText(BuildingSchedule s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Activities.Count; i++)
            {
                if (s.Result.Critical[i]) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(s.Activities[i].Name + " " + s.Result.TotalFloat[i].ToString("0.#", Invariant) + "일");
            }
            return sb.Length > 0 ? sb.ToString() : "없음 (모든 작업이 주공정)";
        }

        /// <summary>공정표: 작업마다 기간, 가장 빠른 시작·종료, 가장 늦은 시작·종료, 전체 여유.</summary>
        private void DrawTable(float x, float y)
        {
            BuildingSchedule s = _schedule;
            float[] widths = { 130f, 44f, 50f, 50f, 50f, 50f, 44f, 56f };
            string[] heads = { "작업", "기간", "ES", "EF", "LS", "LF", "여유", "" };
            const float rowHeight = 18f;

            float tableWidth = 16f;
            foreach (float w in widths) tableWidth += w;
            float tableHeight = 14f + rowHeight * (s.Activities.Count + 3);
            HudStyle.DrawBox(new Rect(x, y, tableWidth, tableHeight));

            var yellow = new Color(1.0f, 0.82f, 0.25f);
            float cy = y + 6f;
            DrawRow(x + 8f, cy, new[] { tableWidth - 16f },
                new[] { "[공정표 CPM]  P: 설명으로 돌아가기   전체 공기 " + s.ProjectDays + "일   노랑 = 주공정" }, Color.white);
            cy += rowHeight;
            DrawRow(x + 8f, cy, widths, heads, Color.white);
            cy += rowHeight;

            for (int i = 0; i < s.Activities.Count; i++)
            {
                CpmResult r = s.Result;
                string[] cells =
                {
                    s.Activities[i].Name,
                    s.Activities[i].Duration.ToString("0.#", Invariant),
                    r.EarlyStart[i].ToString("0.#", Invariant),
                    r.EarlyFinish[i].ToString("0.#", Invariant),
                    r.LateStart[i].ToString("0.#", Invariant),
                    r.LateFinish[i].ToString("0.#", Invariant),
                    r.TotalFloat[i].ToString("0.#", Invariant),
                    r.Critical[i] ? "주공정" : "",
                };
                DrawRow(x + 8f, cy, widths, cells, r.Critical[i] ? yellow : Color.white);
                cy += rowHeight;
            }

            DrawRow(x + 8f, cy, new[] { tableWidth - 16f },
                new[] { "단위: 일.  ES·EF = 가장 빠른 시작·종료,  LS·LF = 가장 늦은 시작·종료" }, Color.white);
        }

        private void DrawRow(float x, float y, float[] widths, string[] cells, Color color)
        {
            Color previous = GUI.color;
            GUI.color = HudStyle.Ui(color);
            for (int i = 0; i < cells.Length; i++)
            {
                GUI.Label(new Rect(x, y, widths[i], 20f), cells[i], _cell);
                x += widths[i];
            }
            GUI.color = previous;
        }
    }
}
