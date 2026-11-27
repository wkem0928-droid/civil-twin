using System.Globalization;
using CivilTwin.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CivilTwin.Runtime
{
    /// <summary>
    /// 부지 계획 도구 (Phase 2): 지형 위에 직사각형 부지를 놓고, 깎는 곳(빨강)·쌓는 곳(파랑),
    /// 토공량, 공기를 보여 주고, 시간에 따라 지형이 바뀌는 모습(4D)을 재생한다.
    /// 계산은 전부 Core가 한다 (GradingPad, EarthworkVolume, EarthworkSchedule, EarthworkStaging, CutFillColors).
    ///
    /// 조작:
    ///   G          : 마우스 커서가 가리키는 곳에 부지를 놓는다 (다시 누르면 옮긴다)
    ///   − / =      : 계획고를 1 m 내리기 / 올리기 (Shift를 누르면 10 m)
    ///   9 / 0      : 부지를 작게 / 크게 (격자 2칸씩)
    ///   , / .      : 굴착기 대수 줄이기 / 늘리기 (공기가 바뀐다)
    ///   V          : 보는 면 바꾸기 (계획면 → 원지반 → 공사 중)
    ///   Space      : 공사 진행 재생 / 멈춤 (처음부터 끝까지 약 10초)
    ///   N / M      : 하루 뒤로 / 앞으로
    ///   B          : 공사 시작 전(0일)으로
    ///   (오른쪽 아래에 누적 토공량 그래프가 나온다. 공사 중 보기에서는 지금 날짜가 노란 선으로 표시된다)
    ///   H          : 부지 계획 끄기
    ///   (건물과 장비는 BuildingSiteTool이 맡는다: T, K / L, F, P)
    ///
    /// 부지를 놓으면 원래 지형은 숨기고, 절·성토 색을 입힌 지형을 따로 만들어 보여 준다.
    /// 끄면 그 지형을 지우고 원래 지형을 다시 보인다.
    /// 화면 글자는 임시(IMGUI)다.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class GradingPlanTool : MonoBehaviour
    {
        private enum View
        {
            Design,   // 계획면 (공사 후)
            Ground,   // 원지반 (공사 전)
            Stage,    // 공사 중 (날짜에 따라 바뀐다)
        }

        private const string PlanObjectName = "GradingPlan (demo)";
        private const float PlaybackSeconds = 10f;
        private const double BuildingPlaybackSeconds = 25.0;
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        /// <summary>절토 비탈 구배 1:n 의 n</summary>
        public double cutSlopeRatio = 1.0;

        /// <summary>성토 비탈 구배 1:n 의 n</summary>
        public double fillSlopeRatio = 1.5;

        /// <summary>굴착기 대수. , 와 . 키로 바꾼다.</summary>
        public int machines = 4;

        // 굴착기 작업량과 흙의 성질. 대표 흙 = "점질토, 보통 조건"으로 정하고 2025 표준품셈 표의 범위 안 값을 쓴다 (5-5).
        // 출처와 쪽수는 Docs/ASSUMPTIONS.md. 책 쪽수: L·C 9쪽, K·E 164쪽, Cm 165쪽.
        public double bucketCapacity = 1.0;    // q: 버킷 용량 (m³) — 1.0 m³급 굴착기를 고른 것 (이 프로그램의 선택)
        public double bucketFactor = 0.9;      // K: "약간 단단한 토질 … 모래, 보통토 및 조건이 좋은 점토" 0.90
        public double volumeConversion = 0.8;  // f: 1 / L, L = 1.25 (점질토 1.25 ~ 1.35의 아래 끝)
        public double compactFactor = 0.9;     // C: 점질토 0.85 ~ 0.95의 가운데

        /// <summary>
        /// 암반선 깊이 (m): 원지반에서 이만큼 아래부터 암 (5-3). X 키로 없음 → 1 → 2 → 5 → 10 → 20 m → 없음.
        /// 사용자가 시추 자료 등을 보고 넣는 입력값이다. 기본은 암 없음.
        /// </summary>
        public double rockDepth = RockLine.None;
        private static readonly double[] RockDepthSteps = { RockLine.None, 1.0, 2.0, 5.0, 10.0, 20.0 };
        private CutMaterials _materials;

        /// <summary>절토의 토사·암 구분 (자연 상태 m³)</summary>
        public CutMaterials Materials => _materials;
        public double efficiency = 0.6;        // E: 자연상태·보통, "자갈섞인 흙, 점성토" 0.60

        /// <summary>부지를 처음 놓을 때의 최대 크기 (m). 소양강댐 현장의 처음 크기와 같다 (2026-11-20).</summary>
        private const double DefaultPadWidth = 420.0, DefaultPadLength = 300.0;
        public double cycleSeconds = 19.0;     // Cm: 1.0 ~ 1.2 m³, 선회 90° → 19초 (5-5에서 20 → 19로 고침)
        public double hoursPerDay = 8.0;       // 하루 작업 시간 ( ; 와 ' 키로 바꾼다 )

        /// <summary>
        /// 장비 간섭 비율 r (Core/MachineInterference). 1이면 간섭 없음. I 키로 1 → 0.95 → 0.9 → 1 순서로 바꾼다.
        /// 이 프로그램의 가정이며 기본값은 간섭 없음이다.
        /// </summary>
        public double interferenceRatio = 1.0;

        // ---- 5-4 운반과 암 (출처는 Docs/ASSUMPTIONS.md) ----

        /// <summary>덤프트럭 대수. Shift + , / . 로 바꾼다. 0이면 운반을 계산하지 않는다.</summary>
        public int trucks = 8;
        public double rockLooseFactor = 1.6;     // 암 L: 보통암 1.55 ~ 1.70 (표준품셈 8쪽)
        public double rockCompactFactor = 1.2;   // 암 C: 보통암 1.20 ~ 1.40의 아래 끝 (다지면 자연 상태보다 부피가 크다)
        public double rockRateRatio = 0.35;      // 암 굴착 작업량 / 토사 굴착 작업량 — **가정값, 출처 없음**
        public double spoilKm = 5.0;             // 사토장까지 편도 거리 (km) — 입력값(가정)
        public double borrowKm = 5.0;            // 토취장까지 편도 거리 (km) — 입력값(가정)
        private HaulDistance _haul;

        /// <summary>현장 안 운반 트럭 (15 t, 2025 표준품셈 값, 미포장 공사용 도로)</summary>
        public DumpTruck Truck => DumpTruck.OnSite2025;

        /// <summary>현장 밖 운반 트럭 (15 t, 2025 표준품셈 값, 2차로 교외 포장도로, 덮개·세륜 포함)</summary>
        public DumpTruck OffSiteTruck => DumpTruck.OffSite2025;

        /// <summary>현장 안 평균 운반거리 (절토 ↔ 성토 무게중심)</summary>
        public HaulDistance Haul => _haul;

        private ElevationGrid _ground;
        private LocalFrame _frame;
        private GameObject _original;

        private bool _hasPad;
        private double _centerEasting;
        private double _centerNorthing;
        private int _widthCells;
        private int _lengthCells;
        private double _padElevation;

        private View _view = View.Design;
        private double _day;
        private bool _playing;

        private ElevationGrid _design;
        private ElevationGrid _shown;
        private EarthworkResult _result;
        private EarthworkStaging _staging;
        private double _maxCutDepth;
        private double _maxFillHeight;
        private GameObject _planObject;
        private GUIStyle _style;
        private GUIStyle _barLabel;
        private GUIStyle _axisLabel;

        // 누적 토공량 그래프 (곡선은 그림 한 장으로 만들어 두고, 공정이 바뀔 때만 다시 그린다)
        private const int GraphWidth = 320;
        private const int GraphHeight = 150;
        private Texture2D _graph;
        private double _graphCut = -1.0;
        private double _graphFill = -1.0;
        private double _graphPerDay = -1.0;
        private double _graphFillPerDay = -1.0;
        private double _graphAxisMax = 1.0;

        public bool HasPad => _hasPad;
        public EarthworkResult Result => _result;

        /// <summary>지금 보고 있는 날짜 (공사 시작 후 며칠째)</summary>
        public double Day => _day;

        // ---- 건물 도구(BuildingSiteTool)가 읽어 가는 값들 ----

        /// <summary>보는 면이 원지반(공사 전)이면 true</summary>
        public bool ShowsGround => _view == View.Ground;

        /// <summary>보는 면이 공사 중(날짜에 따라 바뀜)이면 true</summary>
        public bool ShowsStage => _view == View.Stage;

        /// <summary>지금 화면에 보이는 지형 격자 (부지가 없으면 null)</summary>
        public ElevationGrid ShownSurface => _shown;

        public LocalFrame Frame => _frame;

        /// <summary>지금의 부지 계획. HasPad가 true일 때만 쓴다.</summary>
        public GradingPad Pad => CurrentPad();

        /// <summary>토공 공기 (일). 부지가 없으면 0.</summary>
        public int EarthworkDays => _hasPad ? CurrentSchedule().TotalDays : 0;

        /// <summary>굴착기 전체의 하루 작업량 (m³/일)</summary>
        public double DailyVolume =>
            HourlyVolume() * hoursPerDay * MachineInterference.EffectiveMachines(machines, interferenceRatio);

        /// <summary>토량 균형 (토사·암을 섞은 L, C). 부지가 있을 때만 의미가 있다.</summary>
        public EarthBalance Balance => CurrentPlan().Balance;

        /// <summary>
        /// 대안에 따라 바뀌지 않는 토공 조건 (5-4): 토사·암, 토량환산계수, 트럭, 운반 거리.
        /// 대안 비교 도구도 이것을 받아 같은 식으로 계산한다.
        /// </summary>
        public EarthworkSetup CurrentSetup()
        {
            double soilLoose = 1.0 / volumeConversion;
            DumpTruck truck = Truck;
            double loadMinutes = truck.LoadMinutes(soilLoose, bucketCapacity, bucketFactor, cycleSeconds, efficiency);
            return new EarthworkSetup(
                _materials, soilLoose, compactFactor, rockLooseFactor, rockCompactFactor, rockRateRatio,
                truck, OffSiteTruck, loadMinutes, _haul.Meters / 1000.0, spoilKm, borrowKm);
        }

        /// <summary>지금 조건의 토공 계산 (굴착과 운반 중 느린 쪽이 공기)</summary>
        public EarthworkPlan CurrentPlan()
        {
            return CurrentSetup().Build(_result.FillVolume, DailyVolume, trucks, hoursPerDay);
        }

        /// <summary>굴착기 한 대의 시간당 작업량 (m³/시간)</summary>
        public double HourlyVolumePerMachine => HourlyVolume();

        /// <summary>터파기 작업량 배율 (E − 0.05) / E — 표준품셈 164쪽 주 ④ (2026-11-20)</summary>
        public double TrenchRateFactor => ExcavatorProductivity.TrenchRateFactor(efficiency);

        /// <summary>시공 조건(대수, 하루 작업 시간, 간섭 비율)을 한꺼번에 바꾼다. 저장한 대안을 불러올 때 쓴다.</summary>
        public void ApplyConditions(int newMachines, double newHoursPerDay, double newInterferenceRatio, int newTrucks)
        {
            machines = Mathf.Max(1, newMachines);
            trucks = Mathf.Max(0, newTrucks);
            hoursPerDay = newHoursPerDay;
            interferenceRatio = newInterferenceRatio;
            if (!_hasPad) return;

            _playing = false;
            _day = System.Math.Max(0.0, System.Math.Min(_day, TimelineDays()));
            RefreshSurface();
        }

        /// <summary>
        /// 시간 막대의 끝 날짜. 건물 보기가 켜져 있으면 토공 + 건물 전체 공기, 아니면 토공 공기.
        /// </summary>
        private int TimelineDays()
        {
            var building = GetComponent<BuildingSiteTool>();
            return building != null && building.IsOn ? building.ProjectDays : CurrentSchedule().TotalDays;
        }

        public void Initialize(ElevationGrid ground, LocalFrame frame, GameObject originalTerrain)
        {
            // 이전 현장의 계획을 지운다. 이전 지형 오브젝트는 이미 지워졌을 수 있다.
            DestroyPlanObject();
            _hasPad = false;
            _design = null;
            _shown = null;
            _staging = null;

            if (_dam != null) RestoreSoilFactors();
            _dam = null;
            _ground = ground;
            _surveyed = ground;
            _frame = frame;
            _original = originalTerrain;
            _view = View.Design;
            _day = 0.0;
            _playing = false;
        }

        // ---------- 구조물 모드 (2026-11-20: 회사별 실제 구조물, 첫 번째는 소양강댐 사력댐) ----------
        //
        // 부지(직사각형 평탄면) 대신 댐 단면을 계획면으로 쓰고, 원지반은 "공사 전 계곡"(ValleyReconstruction)으로 바꾼다.
        // 그러면 토공량(= 댐 체적), 토량 균형(전부 토취), 장비 조합·공기, 4D(성토가 아래에서부터 = 댐이 층층이 올라감),
        // 대안 비교·보고서가 그대로 댐에 대해 계산된다.

        private ElevationGrid _surveyed;          // 위성 지형 (댐이 이미 있는 지금의 지형)
        private DamSection _dam;
        private DamSpec _damSpec;                 // 지금 쌓는 댐의 자료 (소양강댐, 부항댐 …)
        private double _savedVolumeConversion, _savedCompactFactor, _savedRockDepth;
        private bool[] _lakeFlat;                // 위성 지형에서 평평한 점 (호수 수면)
        private double _lakeLevel;               // 지금 수면 높이 (위성 지형에서)
        private bool[] _water;                   // 지금 보이는 면에서 물로 칠할 점
        private double _valleySlope;             // 복원한 계곡의 양안 기울기 (글상자에 가정으로 보여 준다)

        /// <summary>댐 축조 보기 중이면 true</summary>
        public bool IsDamMode => _dam != null;

        public DamSection Dam => _dam;

        /// <summary>지금 쌓는 댐의 자료 (댐 모드가 아니면 null)</summary>
        public DamSpec DamSpec => _damSpec;

        /// <summary>
        /// 댐 성토 재료의 토량환산계수: 사력(역, 礫) — 2025 표준품셈 8쪽 "역" L 1.10 ~ 1.20, C 1.05 ~ 1.10의 가운데.
        /// 실제 소양강댐은 코어(점토)·필터·사력·암 존으로 나뉘지만 여기서는 한 재료로 본다 (한계).
        /// </summary>
        public const double DamFillLoose = 1.15, DamFillCompact = 1.075;

        /// <summary>소양강댐 사력댐 축조를 시작한다 (예전 이름, StartDam(SiteStructures.Soyanggang)과 같다).</summary>
        public void StartSoyanggangDam() => StartDam(SiteStructures.Soyanggang);

        /// <summary>
        /// 댐 축조를 시작한다. 지금 현장이 그 댐의 현장이어야 한다 (지형 파일 이름으로 확인은 부르는 쪽에서, SiteStructures.FindDam).
        /// 원지반 = 공사 전 계곡 복원, 계획면 = 댐 단면.
        /// </summary>
        public void StartDam(DamSpec spec)
        {
            if (_surveyed == null || spec == null) return;
            DamSection dam = spec.CreateSection();
            AbutmentValley valley = spec.FitValley(_surveyed, dam);
            ElevationGrid original = ValleyReconstruction.Rebuild(_surveyed, dam, valley, spec.UpstreamExtra, spec.DownstreamExtra);

            if (_dam == null)
            {
                _savedVolumeConversion = volumeConversion;
                _savedCompactFactor = compactFactor;
                _savedRockDepth = rockDepth;
            }
            _dam = dam;
            _damSpec = spec;
            _valleySlope = valley.Slope;
            _lakeFlat = FlatSurfaceDetector.FindFlatNodes(_surveyed);
            _lakeLevel = DamReservoir.FindLakeLevel(_surveyed, _lakeFlat, dam, 2000.0);
            _ground = original;
            volumeConversion = 1.0 / DamFillLoose;
            compactFactor = DamFillCompact;
            rockDepth = RockLine.None;

            // 다른 도구(카메라·글상자·장비)가 쓰는 "부지" 값은 댐 둘레로 맞춘다.
            _centerEasting = dam.CenterEasting;
            _centerNorthing = dam.CenterNorthing;
            _padElevation = dam.CrestElevation;
            _widthCells = Mathf.Max(2, Mathf.RoundToInt((float)((dam.UpstreamBase + dam.DownstreamBase) / _ground.CellSize)));
            _lengthCells = Mathf.Max(2, Mathf.RoundToInt((float)(spec.Length / _ground.CellSize)));
            _hasPad = true;
            _playing = false;
            _view = View.Design;
            _day = 0.0;
            Rebuild();
            FocusCameraOnPad();
        }

        /// <summary>댐 모드를 끝내고 위성 지형으로 돌아간다.</summary>
        private void StopDamMode()
        {
            if (_dam == null) return;
            RestoreSoilFactors();
            _dam = null;
            _damSpec = null;
            _ground = _surveyed;
        }

        private void RestoreSoilFactors()
        {
            volumeConversion = _savedVolumeConversion;
            compactFactor = _savedCompactFactor;
            rockDepth = _savedRockDepth;
        }

        private void Update()
        {
            if (_ground == null) return;
            if (InputGate.Allowed) HandleKeys();
            if (_hasPad && _playing) AdvancePlayback();
        }

        /// <summary>키 입력 → 아래의 동작 메서드. 화면 메뉴(ControlBar)의 버튼도 같은 메서드를 부른다.</summary>
        private void HandleKeys()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.gKey.wasPressedThisFrame) PlaceAtCursor();
            if (!_hasPad) return;

            if (keyboard.hKey.wasPressedThisFrame) { Clear(); return; }

            bool shiftHeld = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            double step = shiftHeld ? 10.0 : 1.0;

            if (keyboard.minusKey.wasPressedThisFrame) ChangeElevation(-step);
            if (keyboard.equalsKey.wasPressedThisFrame) ChangeElevation(+step);
            if (keyboard.xKey.wasPressedThisFrame) CycleRockDepth();
            if (keyboard.eKey.wasPressedThisFrame) SetBalancedElevation();
            if (keyboard.digit9Key.wasPressedThisFrame) Resize(-2);
            if (keyboard.digit0Key.wasPressedThisFrame) Resize(+2);

            if (keyboard.vKey.wasPressedThisFrame) CycleView();
            // Shift를 누른 채면 덤프트럭 대수 (0대 = 운반 계산 안 함).
            if (keyboard.commaKey.wasPressedThisFrame) { if (shiftHeld) ChangeTrucks(-1); else ChangeMachines(-1); }
            if (keyboard.periodKey.wasPressedThisFrame) { if (shiftHeld) ChangeTrucks(+1); else ChangeMachines(+1); }
            if (keyboard.semicolonKey.wasPressedThisFrame) ChangeHours(-1.0);
            if (keyboard.quoteKey.wasPressedThisFrame) ChangeHours(+1.0);
            if (keyboard.iKey.wasPressedThisFrame) CycleInterference();

            if (keyboard.spaceKey.wasPressedThisFrame) TogglePlay();
            if (keyboard.nKey.wasPressedThisFrame) StepDay(-1.0);
            if (keyboard.mKey.wasPressedThisFrame) StepDay(+1.0);
            if (keyboard.bKey.wasPressedThisFrame) Restart();
        }

        private void AdvancePlayback()
        {
            int earthworkDays = CurrentSchedule().TotalDays;
            int totalDays = TimelineDays();

            // 토공 구간은 약 10초, 건물 구간은 약 25초에 재생한다.
            // (토공이 몇 년, 건물이 몇 달이면 같은 빠르기로는 건물이 순식간에 지나가 버리기 때문이다.)
            bool inBuilding = totalDays > earthworkDays && _day >= earthworkDays;
            double daysPerSecond = inBuilding
                ? (totalDays - earthworkDays) / BuildingPlaybackSeconds
                : System.Math.Max(earthworkDays, 1) / (double)PlaybackSeconds;
            _day += Time.unscaledDeltaTime * daysPerSecond * HudMode.PlaybackSpeed;   // 재생 속도 (메뉴 [재생])
            if (_day >= totalDays)
            {
                _day = totalDays;
                _playing = false;
            }
            RefreshSurface();
        }

        // ---------- 동작 (키와 화면 메뉴가 함께 쓴다) ----------

        /// <summary>재생 중이면 true</summary>
        public bool IsPlaying => _playing;

        /// <summary>지금 보는 면의 이름 (메뉴 표시용)</summary>
        public string ViewName => _view == View.Design ? "계획면" : (_view == View.Ground ? "원지반" : "공사 중");

        /// <summary>암반선 깊이 표시 (메뉴용)</summary>
        public string RockDepthLabel => double.IsPositiveInfinity(rockDepth) ? "없음" : rockDepth.ToString("0.#", Invariant) + " m";

        public double PadElevation => _padElevation;

        /// <summary>부지 크기 글 "420 × 300 m" (메뉴 표시용)</summary>
        public string PadSizeLabel => _hasPad
            ? (_widthCells * _ground.CellSize).ToString("N0", Invariant) + " × "
              + (_lengthCells * _ground.CellSize).ToString("N0", Invariant) + " m"
            : "-";

        /// <summary>시간 막대의 끝 날짜 (토공, 건물을 켜면 토공 + 건물). 부지가 없으면 0.</summary>
        public int TimelineTotalDays => _hasPad ? TimelineDays() : 0;

        // ---- 보고서(5-7)가 읽어 가는 값 ----
        public double CenterEasting => _centerEasting;
        public double CenterNorthing => _centerNorthing;
        public double PadWidth => _hasPad ? _widthCells * _ground.CellSize : 0.0;
        public double PadLength => _hasPad ? _lengthCells * _ground.CellSize : 0.0;
        public double MaxCutDepth => _maxCutDepth;
        public double MaxFillHeight => _maxFillHeight;

        /// <summary>
        /// 시간 막대의 끝 날짜가 바뀐 뒤(건물 켜기·끄기, 층수 변경) 지금 날짜를 그 안으로 맞춘다.
        /// (건물을 켠 채 353일째에서 건물을 끄면 "353일째 / 333일"로 남던 문제 — 2026-11-20 시연 캡처)
        /// </summary>
        public void ClampDayToTimeline()
        {
            if (!_hasPad) return;
            double end = TimelineDays();
            if (_day <= end) return;
            _day = end;
            RefreshSurface();
        }

        // 계획을 바꾸는 동작: 계획면과 토공량을 다시 계산한다.
        private void PlanChanged()
        {
            _playing = false;
            Rebuild();
        }

        public void ChangeElevation(double delta)
        {
            if (!_hasPad || _dam != null) return;   // 댐은 공개 제원(마루 EL 203)으로 고정
            _padElevation += delta;
            PlanChanged();
        }

        /// <summary>균형 계획고 (5-2): 절토 × C = 성토가 되는 계획고를 찾아 cm 단위로 맞춘다.</summary>
        public void SetBalancedElevation()
        {
            if (!_hasPad || _dam != null) return;
            // 암반선을 넣었으면 암의 C(다지면 커짐)도 반영한다 (2026-11-20 검토에서 고침 — 전에는 토사 C만 썼다).
            double balanced = BalancedElevation.FindWithRock(_ground, CurrentPad(), compactFactor, rockCompactFactor, rockDepth, out _);
            _padElevation = System.Math.Round(balanced, 2);
            PlanChanged();
        }

        /// <summary>암반선 깊이: 없음 → 1 → 2 → 5 → 10 → 20 m → 없음 (5-3). 암은 굴착이 느려 공기가 바뀐다 (5-4).</summary>
        public void CycleRockDepth()
        {
            if (!_hasPad || _dam != null) return;   // 댐은 쌓기만 해서 절토(토사·암)가 없다
            int index = System.Array.IndexOf(RockDepthSteps, rockDepth);
            rockDepth = RockDepthSteps[(index + 1) % RockDepthSteps.Length];   // 목록에 없는 값이면 처음(없음)으로
            PlanChanged();
        }

        /// <summary>부지 크기를 격자 칸 단위로 키우거나 줄인다 (가로·세로 함께).</summary>
        public void Resize(int deltaCells)
        {
            if (!_hasPad || _dam != null) return;
            _widthCells = Mathf.Max(2, _widthCells + deltaCells);
            _lengthCells = Mathf.Max(2, _lengthCells + deltaCells);
            PlanChanged();
        }

        // 보는 방법·장비 조건을 바꾸는 동작: 지형 모양만 다시 만든다.
        public void CycleView()
        {
            if (!_hasPad) return;
            _view = _view == View.Design ? View.Ground : (_view == View.Ground ? View.Stage : View.Design);
            _playing = false;
            RefreshSurface();
        }

        public void ChangeMachines(int delta)
        {
            machines = Mathf.Max(1, machines + delta);
            if (_hasPad) RefreshSurface();
        }

        public void ChangeTrucks(int delta)
        {
            trucks = Mathf.Max(0, trucks + delta);
            if (_hasPad) RefreshSurface();
        }

        /// <summary>하루 작업 시간 (4 ~ 12시간)</summary>
        public void ChangeHours(double delta)
        {
            hoursPerDay = System.Math.Max(4.0, System.Math.Min(12.0, hoursPerDay + delta));
            if (_hasPad) RefreshSurface();
        }

        /// <summary>장비 간섭 가정 r: 1 → 0.95 → 0.9 → 1</summary>
        public void CycleInterference()
        {
            interferenceRatio = interferenceRatio > 0.975 ? 0.95 : (interferenceRatio > 0.925 ? 0.9 : 1.0);
            if (_hasPad) RefreshSurface();
        }

        public void TogglePlay()
        {
            if (!_hasPad) return;
            _view = View.Stage;
            _playing = !_playing;
            if (_playing && _day >= TimelineDays()) _day = 0.0;   // 끝에서 누르면 처음부터 다시
            RefreshSurface();
        }

        public void Restart()
        {
            if (!_hasPad) return;
            _view = View.Stage;
            _playing = false;
            _day = 0.0;
            RefreshSurface();
        }

        public void StepDay(double delta)
        {
            if (!_hasPad) return;
            _view = View.Stage;
            _playing = false;
            _day = System.Math.Max(0.0, System.Math.Min(System.Math.Round(_day) + delta, TimelineDays()));   // 정수 날짜에 맞춘다
            RefreshSurface();
        }

        /// <summary>화면 가운데가 가리키는 지형 위의 점에 부지를 놓는다 (메뉴 버튼용 — 버튼 위치는 지형이 아니므로).</summary>
        public void PlaceAtScreenCenter()
        {
            var measure = GetComponent<MeasureTool>();
            if (measure == null) return;
            if (!measure.TryPick(new Vector2(Screen.width / 2f, Screen.height / 2f), out SurveyPoint picked)) return;
            PlaceAt(picked.Easting, picked.Northing);
        }

        /// <summary>마우스 커서가 가리키는 지형 위의 점을 부지 중심으로 삼는다.</summary>
        private void PlaceAtCursor()
        {
            Mouse mouse = Mouse.current;
            var measure = GetComponent<MeasureTool>();
            if (mouse == null || measure == null) return;
            if (!measure.TryPick(mouse.position.ReadValue(), out SurveyPoint picked)) return;

            PlaceAt(picked.Easting, picked.Northing);
        }

        /// <summary>
        /// 지정한 위치에 부지를 놓는다. 계획고는 그 자리 원지반 표고를 1 m 단위로 반올림한 값으로 시작한다
        /// (비탈진 땅이면 한쪽은 깎고 한쪽은 쌓게 된다).
        /// </summary>
        public void PlaceAt(double easting, double northing)
        {
            if (_ground == null) return;
            if (_dam != null)
            {
                // 댐 축조 중에 부지를 놓으면 댐 모드를 끝내고 위성 지형 위의 새 부지로 시작한다.
                StopDamMode();
                _hasPad = false;
            }
            if (!_ground.TryGetElevationAt(easting, northing, out double groundElevation)) return;

            if (!_hasPad)
            {
                // 처음 크기: 지형 폭의 약 10 % × 7 % (격자 칸 수로, 최소 4 × 3칸), 단 420 × 300 m를 넘지 않는다.
                // 2026-11-20: 비율만 쓰면 지형 자료가 큰 현장(인제양양 13.8 km)에서 부지가 1,380 m가 되어
                // 절토 깊이 228 m, 공기 96년 같은 비현실적인 첫 화면이 나왔다 (check-sites 자동 시연에서 발견).
                // 420 × 300 m는 소양강댐 현장의 처음 크기(136칸 × 10 %, 7 %)와 같아 README 예시는 그대로다.
                _widthCells = Mathf.Max(4, Mathf.Min(Mathf.RoundToInt(_ground.Columns * 0.10f),
                    Mathf.RoundToInt((float)(DefaultPadWidth / _ground.CellSize))));
                _lengthCells = Mathf.Max(3, Mathf.Min(Mathf.RoundToInt(_ground.Rows * 0.07f),
                    Mathf.RoundToInt((float)(DefaultPadLength / _ground.CellSize))));
            }

            _centerEasting = easting;
            _centerNorthing = northing;
            _padElevation = System.Math.Round(groundElevation);
            _hasPad = true;
            _playing = false;
            Rebuild();
            FocusCameraOnPad();
        }

        /// <summary>
        /// 카메라를 부지로 옮긴다. 현장 전체(수 km) 시점에서는 수백 m 부지가 작게 보이고 글상자에 가려
        /// "눌러도 보이지 않는다"는 의견이 있었다 (2026-11-20 개발자). 부지 대각선의 약 1.6배 거리에서 본다.
        /// 방향(방위각·기울기)은 그대로 둔다. R 키로 현장 전체 시점으로 돌아간다.
        /// </summary>
        private void FocusCameraOnPad()
        {
            var controller = GetComponent<OrbitCameraController>();
            if (controller == null || !_hasPad) return;
            double width = _widthCells * _ground.CellSize, length = _lengthCells * _ground.CellSize;
            double diagonal = System.Math.Sqrt(width * width + length * length);
            if (_dam != null)
            {
                // 댐은 하류 쪽에서 비스듬히(축에 수직에서 30° 돌려) 봐야 사면과 높이가 보인다.
                // 하류 방향 (동, 북) = 축 좌표 d = −1 쪽. 카메라 위치 = 주시점 − 수평거리 × (sin 방위, cos 방위).
                _dam.FromAxis(0.0, 0.0, out double e0, out double n0);
                _dam.FromAxis(0.0, -1.0, out double e1, out double n1);
                double yaw = System.Math.Atan2(-(e1 - e0), -(n1 - n0)) * 180.0 / System.Math.PI + 30.0;
                // 주시점은 마루보다 조금 위: 댐이 화면 가운데보다 아래(오른쪽 위 글상자 아래)에 오게 한다.
                // 거리·높이는 댐마다 다르다 (DamSpec.CameraDistance: 소양강댐 1,378 m, 부항댐 760 m).
                LocalPoint damTarget = _frame.ToLocal(_dam.CenterEasting, _dam.CenterNorthing, _dam.CrestElevation + _damSpec.CameraAboveCrest);
                controller.LookFrom(damTarget, yaw, 35.0, _damSpec.CameraDistance, 50.0);
                return;
            }
            LocalPoint target = _frame.ToLocal(_centerEasting, _centerNorthing, _padElevation);
            controller.FocusOn(target, diagonal * 1.6, diagonal * 0.1);
        }

        /// <summary>부지 계획을 끄고 원래 지형으로 돌아간다.</summary>
        public void Clear()
        {
            bool hadPad = _hasPad;
            StopDamMode();
            DestroyPlanObject();
            _hasPad = false;
            _design = null;
            _shown = null;
            _staging = null;
            _playing = false;
            _day = 0.0;
            _view = View.Design;
            DestroyGraph();

            if (_original != null) _original.GetComponent<MeshRenderer>().enabled = true;
            if (hadPad && _ground != null)
            {
                var measure = GetComponent<MeasureTool>();
                if (measure != null) measure.SetGrid(_ground);
            }
        }

        private GradingPad CurrentPad()
        {
            return new GradingPad(
                _centerEasting, _centerNorthing,
                _widthCells * _ground.CellSize, _lengthCells * _ground.CellSize,
                _padElevation, cutSlopeRatio, fillSlopeRatio);
        }

        private double HourlyVolume()
        {
            return ExcavatorProductivity.HourlyVolume(
                bucketCapacity, bucketFactor, volumeConversion, efficiency, cycleSeconds);
        }

        /// <summary>
        /// 지금의 토공량과 장비로 계산한 공정 (5-4부터): 공기는 굴착과 운반 중 느린 쪽이고,
        /// 절토와 성토는 그 공기 동안 고르게 진행되어 같은 날 끝난다고 본다 (EarthworkPlan.ToSchedule).
        /// </summary>
        private EarthworkSchedule CurrentSchedule()
        {
            return CurrentPlan().ToSchedule();
        }

        /// <summary>지금 화면에 보여 줄 지형 격자.</summary>
        private ElevationGrid ShownGrid()
        {
            ElevationGrid grid = BaseShownGrid();
            _water = null;
            if (_dam == null) return grid;
            // 댐: 저수지 물 표시 (계산에는 쓰지 않음). 완공 화면에서만 댐 상류를 물로 채운다.
            return DamReservoir.Apply(grid, _ground, _surveyed, _lakeFlat, _dam, _lakeLevel, _view == View.Design, out _water);
        }

        private ElevationGrid BaseShownGrid()
        {
            if (_view == View.Design) return _design;
            if (_view == View.Ground) return _ground;

            // 공사 중: 그 날짜까지의 누적 작업량만큼 깎고 쌓은 상태를 만든다.
            EarthworkSchedule schedule = CurrentSchedule();
            double cutLevel = _staging.FindCutLevel(schedule.CumulativeCut(_day));
            double fillLevel = _staging.FindFillLevel(schedule.CumulativeFill(_day));
            return _staging.BuildStageGrid(cutLevel, fillLevel);
        }

        /// <summary>계획이 바뀌었을 때: 계획면·토공량·색을 다시 계산하고 지형을 새로 만든다.</summary>
        private void Rebuild()
        {
            _design = _dam != null ? _dam.BuildDesignGrid(_ground) : CurrentPad().BuildDesignGrid(_ground);
            _result = EarthworkVolume.Compute(_ground, _design);
            _materials = RockLine.Split(_ground, _design, rockDepth);
            _haul = HaulDistance.Compute(_ground, _design);
            _staging = new EarthworkStaging(_ground, _design);
            CutFillColors.FindMaxDepths(_ground, _design, out _maxCutDepth, out _maxFillHeight);

            _day = System.Math.Max(0.0, System.Math.Min(_day, TimelineDays()));

            DestroyPlanObject();

            ElevationGrid shown = ShownGrid();
            _shown = shown;
            Texture2D colors = _dam != null
                ? TerrainColorTextureFactory.CreateDam(_ground, shown, ElevationColorRamp.CreateDefault(),
                    _dam.FoundationElevation, _dam.CrestElevation, _water)
                : TerrainColorTextureFactory.CreateCutFill(
                    _ground, _design, ElevationColorRamp.CreateDefault(), _maxCutDepth, _maxFillHeight);
            _planObject = HillTerrainDemo.CreateTerrainObject(shown, _frame, colors, PlanObjectName);

            if (_original != null) _original.GetComponent<MeshRenderer>().enabled = false;

            // 측정 도구도 지금 보이는 면을 재도록 맞춘다 (부지를 클릭하면 계획고가 나온다).
            var measure = GetComponent<MeasureTool>();
            if (measure != null) measure.SetGrid(shown);
        }

        /// <summary>보는 면이나 날짜만 바뀌었을 때: 색은 그대로 두고 지형 모양(메시)만 바꾼다.</summary>
        private void RefreshSurface()
        {
            if (_planObject == null || _design == null) return;

            ElevationGrid shown = ShownGrid();
            _shown = shown;
            var filter = _planObject.GetComponent<MeshFilter>();
            Mesh old = filter.sharedMesh;
            filter.sharedMesh = TerrainMeshFactory.CreateMesh(TerrainMeshBuilder.Build(shown, _frame));
            if (old != null) Destroy(old);

            // 댐: 쌓인 만큼만 사력 색으로 칠한다 (날짜가 바뀌면 칠할 곳도 바뀐다).
            if (_dam != null)
            {
                Material material = _planObject.GetComponent<MeshRenderer>().sharedMaterial;
                Texture oldColors = material.GetTexture("_BaseMap");
                material.SetTexture("_BaseMap", TerrainColorTextureFactory.CreateDam(_ground, shown,
                    ElevationColorRamp.CreateDefault(), _dam.FoundationElevation, _dam.CrestElevation, _water));
                if (oldColors != null) Destroy(oldColors);
            }

            // 재생 중에는 매 프레임 지형이 바뀌므로, 멈췄을 때만 측정 도구를 맞춘다
            // (맞출 때마다 찍어 둔 측정점이 지워지기 때문이다).
            if (!_playing)
            {
                var measure = GetComponent<MeasureTool>();
                if (measure != null) measure.SetGrid(shown);
            }
        }

        private void DestroyPlanObject()
        {
            if (_planObject == null) return;

            Mesh mesh = _planObject.GetComponent<MeshFilter>().sharedMesh;
            Material material = _planObject.GetComponent<MeshRenderer>().sharedMaterial;
            Texture colors = material != null ? material.GetTexture("_BaseMap") : null;

            Destroy(_planObject);
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
            if (colors != null) Destroy(colors);
            _planObject = null;
        }

        private void DestroyGraph()
        {
            if (_graph != null) Destroy(_graph);
            _graph = null;
        }

        private void OnDestroy()
        {
            DestroyPlanObject();
            DestroyGraph();
        }

        // ---------- 화면 글자 (임시 IMGUI) ----------

        private void OnGUI()
        {
            HudMode.ApplyMatrix();   // 글자 크기 (메뉴 [화면])
            if (_ground == null) return;

            if (_style == null)
            {
                _style = HudStyle.Panel(15);   // 모든 글상자 공통 모양 (2026-11-20)

                _barLabel = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
                _barLabel.normal.textColor = HudStyle.Ui(Color.white);

                _axisLabel = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = false };
                _axisLabel.normal.textColor = HudStyle.Ui(Color.white);
            }

            // Tab 키로 글상자를 끈 상태 (HudMode): 시간 막대만 오른쪽 아래에 남기거나, 아무것도 그리지 않는다.
            if (!HudMode.ShowBoxes)
            {
                if (HudMode.ShowTimeBar && _hasPad && _view == View.Stage && TimelineDays() > 0)
                {
                    const float barWidth = 418f;
                    DrawTimeBar(CurrentSchedule(), HudMode.GuiWidth - barWidth - 12f,
                        HudMode.GuiHeight - TimeBarHeight - 12f, barWidth);
                }
                return;
            }

            // 터널 보기 중에는 터널 도구가 글상자를 그린다 (부지가 없을 때의 [시작하기] 안내와 겹치지 않게, 2026-11-21).
            var tunnel = GetComponent<TunnelTool>();
            if (!_hasPad && tunnel != null && tunnel.IsOn) return;

            // 오른쪽 정보 글상자 (InfoBox, 2026-11-21): 오른쪽 위 메뉴 바로 아래, 폭 제한·줄바꿈, [접기] 버튼
            Rect bar = ControlBar.BarRect;
            float textY = bar.width > 0f ? bar.yMax + 8f : 10f;
            Rect textRect = HudMode.Show(HudMode.Item.Info)
                ? InfoBox.Draw(BuildText(), textY, Headline())
                : new Rect(HudMode.GuiWidth - 14f, textY, 0f, 0f);   // 글상자를 끄면 그래프가 그 자리로 올라간다

            if (_hasPad) DrawGraph(textRect);
        }

        /// <summary>
        /// 화면 오른쪽 아래의 누적 토공량 그래프. 가로 = 날짜(0 ~ 공기), 세로 = 누적 토공량(m³).
        /// 빨강 실선 = 절토, 파랑 점선 = 성토. "공사 중" 보기에서는 지금 날짜를 노란 세로선으로 표시한다.
        /// </summary>
        private void DrawGraph(Rect textRect)
        {
            EarthworkSchedule schedule = CurrentSchedule();
            int totalDays = schedule.TotalDays;
            if (totalDays <= 0)
            {
                // 토공이 없으면 그래프는 그리지 않는다. 건물 공정이 있으면 시간 막대만 오른쪽 아래에 그린다.
                if (_view == View.Stage && TimelineDays() > 0)
                {
                    const float barWidth = 418f;
                    DrawTimeBar(schedule, HudMode.GuiWidth - barWidth - 12f, HudMode.GuiHeight - TimeBarHeight - 12f, barWidth);
                }
                return;
            }

            // 건물 보기 중 재생할 때는 누적 그래프를 빼고 시간 막대만 오른쪽 아래에 둔다.
            // (그래프 + 시간 막대가 오른쪽 글상자 아래에 들어가지 않아 왼쪽으로 밀리면 건물 글상자를 덮는다 — 2026-11-20 자동 시연 캡처)
            // 대안 비교표·그래프가 왼쪽 아래에 떠 있을 때도 같다 (겹치므로 누적 그래프를 뺀다).
            var buildingTool = GetComponent<BuildingSiteTool>();
            var alternativeTool = GetComponent<AlternativeTool>();
            bool comparing = alternativeTool != null && alternativeTool.ShowingTable;
            // 누적 그래프는 "글상자 자세히"일 때만 그린다 (기본 화면에서 지형을 가리지 않게, 2026-11-20).
            // 2026-11-21: 메뉴 [화면]에서 따로 켜고 끈다 ("자세히"로 바꾸면 함께 켜진다)
            if (!HudMode.Show(HudMode.Item.Graph) || comparing || (_view == View.Stage && buildingTool != null && buildingTool.IsOn))
            {
                const float barWidth = 418f;
                if (_view == View.Stage)
                {
                    DrawTimeBar(schedule, HudMode.GuiWidth - barWidth - 12f, HudMode.GuiHeight - TimeBarHeight - 12f, barWidth);
                }
                return;
            }

            if (_graph == null || schedule.CutVolume != _graphCut || schedule.FillVolume != _graphFill
                || schedule.CutPerDay != _graphPerDay || schedule.FillPerDay != _graphFillPerDay)
            {
                BuildGraph(schedule);
            }

            const float leftPad = 84f, topPad = 28f, rightPad = 14f, bottomPad = 26f;
            float boxWidth = leftPad + GraphWidth + rightPad;
            float boxHeight = topPad + GraphHeight + bottomPad;
            float boxX = HudMode.GuiWidth - boxWidth - 12f;
            float boxY = HudMode.GuiHeight - boxHeight - 12f;

            // 화면이 작아 오른쪽 위 글상자와 겹치게 되면 (시간 막대 자리까지 포함해서) 글상자 왼쪽으로 옮긴다.
            float neededTop = boxY - (_view == View.Stage ? TimeBarHeight + 12f : 6f);
            if (textRect.yMax > neededTop) boxX = Mathf.Max(12f, textRect.xMin - boxWidth - 8f);

            // 시간 막대는 그래프 바로 위에 같은 폭으로 붙인다.
            if (_view == View.Stage) DrawTimeBar(schedule, boxX, boxY - 6f - TimeBarHeight, boxWidth);

            float plotX = boxX + leftPad;
            float plotY = boxY + topPad;

            HudStyle.DrawBox(new Rect(boxX, boxY, boxWidth, boxHeight));
            GUI.DrawTexture(new Rect(plotX, plotY, GraphWidth, GraphHeight), _graph, ScaleMode.StretchToFill);

            // 제목과 범례
            _axisLabel.alignment = TextAnchor.MiddleLeft;
            GUI.Label(new Rect(plotX, boxY + 4, 150, 20), "누적 토공량 (m³)", _axisLabel);
            Color previous = GUI.color;
            GUI.color = HudStyle.Ui(new Color(0.90f, 0.25f, 0.20f, 1f));
            GUI.Label(new Rect(plotX + GraphWidth - 150, boxY + 4, 70, 20), "━ 절토", _axisLabel);
            GUI.color = HudStyle.Ui(new Color(0.40f, 0.60f, 1.00f, 1f));
            GUI.Label(new Rect(plotX + GraphWidth - 80, boxY + 4, 80, 20), "┅ 성토", _axisLabel);
            GUI.color = previous;

            // 세로축 눈금: 0, 절반, 최댓값
            _axisLabel.alignment = TextAnchor.MiddleRight;
            GUI.Label(new Rect(boxX, plotY - 10, leftPad - 6, 20), _graphAxisMax.ToString("N0", Invariant), _axisLabel);
            GUI.Label(new Rect(boxX, plotY + GraphHeight / 2f - 10, leftPad - 6, 20),
                (_graphAxisMax / 2.0).ToString("N0", Invariant), _axisLabel);
            GUI.Label(new Rect(boxX, plotY + GraphHeight - 10, leftPad - 6, 20), "0", _axisLabel);

            // 가로축 눈금: 0일, 공기
            _axisLabel.alignment = TextAnchor.MiddleLeft;
            GUI.Label(new Rect(plotX, plotY + GraphHeight + 3, 60, 20), "0일", _axisLabel);
            _axisLabel.alignment = TextAnchor.MiddleRight;
            GUI.Label(new Rect(plotX + GraphWidth - 80, plotY + GraphHeight + 3, 80, 20), totalDays + "일", _axisLabel);

            // 지금 날짜 (공사 중 보기일 때만)
            if (_view == View.Stage)
            {
                float fraction = Mathf.Clamp01((float)(_day / totalDays));
                float markerX = plotX + (GraphWidth - 1) * fraction;
                GUI.color = HudStyle.Ui(new Color(0.95f, 0.75f, 0.15f, 1f));
                GUI.DrawTexture(new Rect(markerX - 1, plotY, 2, GraphHeight), Texture2D.whiteTexture);
                GUI.color = previous;

                // 토공이 끝난 뒤(건물 공사 중)에는 이 그래프의 범위 밖이므로 "토공 끝"이라고 적는다.
                _axisLabel.alignment = TextAnchor.MiddleCenter;
                GUI.Label(new Rect(plotX + GraphWidth / 2f - 60, plotY + GraphHeight + 3, 120, 20),
                    _day <= totalDays ? _day.ToString("F1", Invariant) + "일째" : "토공 끝", _axisLabel);
            }
        }

        /// <summary>그래프 곡선을 그림(텍스처)으로 만든다. 가로 한 점마다 Core의 누적값을 그대로 찍는다.</summary>
        private void BuildGraph(EarthworkSchedule schedule)
        {
            DestroyGraph();
            _graphCut = schedule.CutVolume;
            _graphFill = schedule.FillVolume;
            _graphPerDay = schedule.CutPerDay;
            _graphFillPerDay = schedule.FillPerDay;
            _graphAxisMax = EarthworkCurve.NiceAxisMax(System.Math.Max(schedule.CutVolume, schedule.FillVolume));

            EarthworkCurve curve = EarthworkCurve.Sample(schedule, GraphWidth - 1);

            var pixels = new Color32[GraphWidth * GraphHeight];
            var background = new Color32(18, 18, 18, 235);
            var gridLine = new Color32(75, 75, 75, 235);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = background;

            // 가로 눈금선: 세로축의 0, 25, 50, 75, 100 %
            for (int k = 0; k <= 4; k++)
            {
                int y = (GraphHeight - 1) * k / 4;
                for (int x = 0; x < GraphWidth; x++) pixels[y * GraphWidth + x] = gridLine;
            }

            // 절토와 성토는 하루 작업량이 같아 초반에 겹친다. 절토를 실선으로 먼저 그리고 성토를 점선으로 덧그린다.
            PlotCurve(pixels, curve.Cut, new Color32(230, 64, 50, 255), false);
            PlotCurve(pixels, curve.Fill, new Color32(100, 150, 255, 255), true);

            _graph = new Texture2D(GraphWidth, GraphHeight, TextureFormat.RGBA32, false)
            {
                name = "EarthworkGraph (demo)",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            for (int i = 0; i < pixels.Length; i++) pixels[i] = HudStyle.Ui(pixels[i]);   // 실행 파일 색 보정
            _graph.SetPixels32(pixels);
            _graph.Apply(false, true);
        }

        /// <summary>값 배열(가로 한 점에 하나)을 2픽셀 두께의 선으로 그린다. 텍스처는 아래쪽이 y = 0 이다.</summary>
        private void PlotCurve(Color32[] pixels, double[] values, Color32 color, bool dashed)
        {
            int previousY = 0;
            for (int x = 0; x < GraphWidth; x++)
            {
                double ratio = values[x] / _graphAxisMax;
                int y = Mathf.Clamp(Mathf.RoundToInt((float)(ratio * (GraphHeight - 1))), 0, GraphHeight - 1);
                int from = x == 0 ? y : Mathf.Min(previousY, y);
                int to = x == 0 ? y : Mathf.Max(previousY, y);
                previousY = y;

                if (dashed && (x / 5) % 2 == 1) continue;   // 5픽셀 그리고 5픽셀 쉰다

                for (int yy = from; yy <= Mathf.Min(to + 1, GraphHeight - 1); yy++)
                {
                    pixels[yy * GraphWidth + x] = color;
                }
            }
        }

        private const float TimeBarHeight = 56f;

        /// <summary>그래프 바로 위의 시간 막대 (상자의 왼쪽 위 모서리와 폭을 받는다). 채워진 길이 = 지난 날짜 / 공기.</summary>
        private void DrawTimeBar(EarthworkSchedule schedule, float boxX, float boxY, float boxWidth)
        {
            if (!HudMode.Show(HudMode.Item.TimeBar)) return;
            // 건물 보기가 켜져 있으면 막대 하나가 토공 + 건물 전체 공기를 나타낸다.
            var building = GetComponent<BuildingSiteTool>();
            bool withBuilding = building != null && building.IsOn;
            int totalDays = TimelineDays();
            float fraction = totalDays > 0 ? Mathf.Clamp01((float)(_day / totalDays)) : 1f;

            float width = boxWidth - 20f;
            float x = boxX + 10f;
            float y = boxY + 28f;

            string status = withBuilding
                ? building.StatusAt(_day)
                : "공정률 " + schedule.ProgressPercent(_day).ToString("F1", Invariant) + " %";

            HudStyle.DrawBox(new Rect(boxX, boxY, boxWidth, TimeBarHeight));
            GUI.Label(new Rect(x, y - 24, width, 22),
                (_playing ? "▶ " : "") + _day.ToString("F1", Invariant) + "일째 / " + totalDays + "일   " + status,
                _barLabel);

            Color previous = GUI.color;
            GUI.color = HudStyle.Ui(new Color(0.15f, 0.15f, 0.15f, 0.9f));
            GUI.DrawTexture(new Rect(x, y + 4, width, 16), Texture2D.whiteTexture);
            GUI.color = HudStyle.Ui(new Color(0.95f, 0.75f, 0.15f, 1f));
            GUI.DrawTexture(new Rect(x, y + 4, width * fraction, 16), Texture2D.whiteTexture);

            // 토공이 끝나는 날을 흰 눈금으로 표시한다 (그 오른쪽이 건물 공사).
            if (withBuilding && totalDays > 0)
            {
                float earthworkEnd = Mathf.Clamp01((float)schedule.TotalDays / totalDays);
                GUI.color = HudStyle.Ui(Color.white);
                GUI.DrawTexture(new Rect(x + width * earthworkEnd - 1f, y + 1, 2, 22), Texture2D.whiteTexture);
            }
            GUI.color = previous;
        }

        private string BuildText()
        {
            if (!_hasPad)
            {
                // 처음 여는 사람을 위한 안내: 무엇부터 누르면 되는지 (2026-11-20 검토)
                return "[시작하기]\n"
                    + "① 위쪽 메뉴 [부지] → 부지 놓기 (또는 G: 마우스 위치)\n"
                    + "② 한 번에 보기: [대안] → 예시 불러오기 (소양강댐 장비 조합 A · B · C)\n"
                    + "③ 화면이 복잡하면 [화면]에서 항목별로 끄기 · 글자 크기 · 키 안내(F1)";
            }

            // 건물 보기가 켜져 있으면 왼쪽에 건물 글상자가 크게 나오므로, 이쪽은 줄여서 보여 준다
            // (작은 화면에서 글상자·그래프·시간 막대가 서로 겹치지 않게).
            var building = GetComponent<BuildingSiteTool>();
            var alternatives = GetComponent<AlternativeTool>();
            // 기본은 짧은 글 (메뉴 [화면] → 글상자 자세히로 전체를 본다, 2026-11-20 개발자 사용 의견)
            if (!HudMode.Detail || (building != null && building.IsOn) || (alternatives != null && alternatives.ShowingTable))
            {
                return BuildShortText();
            }

            return BuildFullText();
        }

        /// <summary>자동 시연(ScenarioRunner)이 로그에 남기는 글: 화면 상태와 관계없이 자세한 글 전체.</summary>
        public string DescribeForLog() => _hasPad ? BuildFullText() : "(부지 없음)";

        /// <summary>댐 축조 중 글상자 머리: 공개 제원과, 계산한 체적을 공개 체적과 비교한 값</summary>
        /// <param name="brief">간단 글상자용 짧은 줄 (오른쪽 글상자 폭을 줄이며 긴 줄이 여러 번 접혀서, 2026-11-21)</param>
        private void AppendDamLines(System.Text.StringBuilder sb, bool brief = false)
        {
            DamSpec spec = _damSpec;
            double ratio = _result.FillVolume / spec.OfficialVolume - 1.0;
            sb.AppendLine("[" + spec.Title + " · " + spec.TypeName + " 축조]");
            if (brief)
            {
                int realDays = spec.CalendarDays, planDays = CurrentPlan().TotalDays;
                sb.AppendLine("제원    높이 " + _dam.Height.ToString("0", Invariant) + " m · 길이 " + spec.Length.ToString("0", Invariant)
                    + " m · 마루 EL " + _dam.CrestElevation.ToString("0.#", Invariant) + " m");
                sb.AppendLine("체적    " + Volume(_result.FillVolume) + " (공개 대비 " + (ratio >= 0 ? "+" : "−")
                    + System.Math.Abs(ratio * 100.0).ToString("F1", Invariant) + " %)");
                sb.AppendLine("실제    " + realDays.ToString("N0", Invariant) + "일 ↔ 토공만 " + planDays.ToString("N0", Invariant) + "일"
                    + (planDays > realDays ? " (장비 " + ((double)planDays / realDays).ToString("0.0", Invariant) + "배 필요)" : ""));
                return;
            }
            sb.AppendLine("제원    마루 EL " + _dam.CrestElevation.ToString("0.#", Invariant) + " m, 높이 " + _dam.Height.ToString("0", Invariant)
                + " m, 길이 " + spec.Length.ToString("0", Invariant) + " m, 사면 상류 1:"
                + _dam.UpstreamSlope.ToString("0.0", Invariant) + " · 하류 1:" + _dam.DownstreamSlope.ToString("0.0", Invariant) + " (" + spec.SlopeSource + ")");
            sb.AppendLine("체적    계산 " + Volume(_result.FillVolume) + "  ·  공개 " + Volume(spec.OfficialVolume)
                + "  (" + (ratio >= 0 ? "+" : "−") + System.Math.Abs(ratio * 100.0).ToString("F1", Invariant) + " %)");

            // 실제 공사 기간과 비교: 쉬는 날 없이 매일 일해도(달력 날수 = 작업 날수) 이 장비 조합으로 실제 기간 안에 끝나는가?
            // 쉬는 날을 넣으면 더 길어지므로, 여기서 나오는 "몇 배"는 적어도 그만큼이라는 아래 한계다.
            int real = spec.CalendarDays;
            int days = CurrentPlan().TotalDays;
            sb.AppendLine("실제    " + spec.PeriodLabel + " (" + real.ToString("N0", Invariant) + "일, " + spec.PeriodNote + ") ↔ 이 조합 토공만 "
                + days.ToString("N0", Invariant) + "일"
                + (days > real
                    ? " → 쉬는 날 없이도 장비가 " + ((double)days / real).ToString("0.0", Invariant) + "배 이상 필요"
                    : " (쉬는 날 없이 계산)"));
        }

        /// <summary>복원한 계곡과 재료의 가정 (글상자·보고서 공통)</summary>
        private string ValleyNote()
        {
            return "공사 전 계곡 = " + _damSpec.FittedSide + " 양안 사면(1:" + (1.0 / _valleySlope).ToString("0.00", Invariant)
                + ")을 " + _damSpec.MirroredSide + "에 대칭으로, 바닥 EL " + _dam.FoundationElevation.ToString("0.#", Invariant)
                + " m. 재료 한 가지(" + _damSpec.Material + ")";
        }

        /// <summary>보고서용 댐 줄들 (글상자의 댐 머리와 같은 내용, 앞의 [제목] 줄은 뺀다)</summary>
        public System.Collections.Generic.List<string> DamReportLines()
        {
            var lines = new System.Collections.Generic.List<string>();
            if (_dam == null) return lines;
            var sb = new System.Text.StringBuilder();
            AppendDamLines(sb);
            string[] all = sb.ToString().Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
            for (int i = 1; i < all.Length; i++) lines.Add(all[i].Trim());
            lines.Add("가정: " + ValleyNote());
            return lines;
        }

        private string BuildFullText()
        {
            var sb = new System.Text.StringBuilder();
            // 작은 화면(약 1100 × 500)에서도 넘치지 않게 줄 수를 줄였다 (2026-11-20, 자동 시연 캡처에서 아래가 잘림).
            if (_dam != null)
            {
                AppendDamLines(sb);
                sb.AppendLine("가정    " + ValleyNote());
            }
            else
            {
                sb.AppendLine("[부지 계획]   (조작은 위쪽 메뉴 [부지] [장비] [재생])");
                sb.AppendLine("부지    " + (_widthCells * _ground.CellSize).ToString("N0", Invariant) + " × "
                    + (_lengthCells * _ground.CellSize).ToString("N0", Invariant) + " m,  계획고 "
                    + _padElevation.ToString("F2", Invariant) + " m,  구배 절토 1:" + cutSlopeRatio.ToString("0.0#", Invariant)
                    + " · 성토 1:" + fillSlopeRatio.ToString("0.0#", Invariant));
            }
            sb.AppendLine("토공량  절토 " + Volume(_result.CutVolume) + " (최대 깊이 " + _maxCutDepth.ToString("F1", Invariant)
                + " m),  성토 " + Volume(_result.FillVolume) + " (최대 높이 " + _maxFillHeight.ToString("F1", Invariant) + " m)");

            if (double.IsPositiveInfinity(rockDepth))
            {
                sb.AppendLine("        토사·암 구분 없음 (모두 토사)");
            }
            else
            {
                sb.AppendLine("        토사 " + Volume(_materials.SoilVolume) + ",  암 " + Volume(_materials.RockVolume)
                    + " (" + (_materials.RockRatio * 100.0).ToString("F0", Invariant) + " %, 암반선 지표 아래 "
                    + rockDepth.ToString("0.#", Invariant) + " m 입력)");
            }

            // 토량 균형 (5-1): 깎은 흙은 다지면 부피가 바뀐다(C). 남으면 사토, 모자라면 토취.
            EarthBalance balance = Balance;
            bool borrow = balance.BorrowVolume > 0.5;
            sb.AppendLine("균형    L " + balance.LooseFactor.ToString("0.00", Invariant) + ", C "
                + balance.CompactFactor.ToString("0.00", Invariant) + (_dam != null ? " (표준품셈 역(礫) 범위의 가운데" : " (표준품셈 점질토")
                + (_materials.RockVolume > 0.5 ? "·보통암" : "") + "): 절토를 다지면 "
                + Volume(balance.CutAsCompacted) + " → "
                + (borrow
                    ? "토취 " + Volume(balance.BorrowVolume) + " (실어 올 양 " + balance.BorrowLooseVolume.ToString("N0", Invariant) + ")"
                    : "사토 " + Volume(balance.SurplusVolume) + " (실어 낼 양 " + balance.SurplusLooseVolume.ToString("N0", Invariant) + ")"));

            // 공정 (5-4): 굴착기 작업량 Q = 3600 × q × K × f × E / Cm, 덤프트럭 운반, 둘 중 느린 쪽이 공기.
            double hourly = HourlyVolume();
            EarthworkPlan plan = CurrentPlan();
            EarthworkSchedule schedule = plan.ToSchedule();
            sb.AppendLine("굴착    굴착기 " + machines + "대 × " + hourly.ToString("F2", Invariant) + " m³/시간 × "
                + hoursPerDay.ToString("0.#", Invariant) + "시간 = " + DailyVolume.ToString("N0", Invariant) + " m³/일 (토사)"
                + (_materials.RockVolume > 0.5 ? ", 암은 × " + rockRateRatio.ToString("0.##", Invariant) : ""));
            if (interferenceRatio < 1.0)
            {
                sb.AppendLine("        (간섭 가정 r = " + interferenceRatio.ToString("0.##", Invariant) + " → 실효 "
                    + MachineInterference.EffectiveMachines(machines, interferenceRatio).ToString("F2", Invariant) + "대)");
            }
            if (trucks > 0)
            {
                sb.AppendLine("운반    덤프트럭 " + trucks + "대 (15 t, Shift + , / .):  현장 안 "
                    + _haul.Meters.ToString("N0", Invariant) + " m,  사토·토취장 "
                    + spoilKm.ToString("0.#", Invariant) + " / " + borrowKm.ToString("0.#", Invariant) + " km");
            }
            else
            {
                sb.AppendLine("운반    덤프트럭 0대 → 운반은 계산하지 않음 (Shift + . 로 트럭 추가)");
            }
            sb.AppendLine("공기    굴착 " + plan.ExcavationDays.ToString("F1", Invariant) + "일"
                + (plan.IncludesHaul ? ", 운반 " + plan.HaulDays.ToString("F1", Invariant) + "일" : "")
                + " → " + plan.TotalDays + "일"
                + (plan.IncludesHaul ? (plan.HaulIsBottleneck ? "  (운반이 느리다)" : "  (굴착이 정한다)") : ""));
            if (plan.IncludesHaul)
            {
                sb.AppendLine("        굴착을 따라가려면 트럭 " + plan.TrucksToKeepUp + "대   (계수: 2025 표준품셈, 거리·암 비율은 가정)");
            }
            if (_result.FillVolume > 0.5)
            {
                // 2026-11-20: 펴고 다지기 (표준품셈 3-4-1 흙쌓기, 한 조 30 cm 다짐 1,150 m³/일)
                sb.AppendLine("다짐    흙쌓기 " + plan.EmbankmentCrews + "조 필요 (진동롤러 10 t 조, 조당 "
                    + EmbankmentCrew.DailyVolume(hoursPerDay).ToString("N0", Invariant) + " m³/일, 표준품셈 3-4-1)"
                    + (_dam != null ? ",  " + (_damSpec.LiftMeters * 100.0).ToString("0", Invariant) + " cm씩 "
                        + _damSpec.Lifts.ToString("N0", Invariant) + "층" : ""));
            }

            if (_materials.RockVolume > 0.5)
            {
                // 2026-11-21: 공기 계산의 암은 "깨진 암 싣기(× 0.35)"만 본다. 깨는 장비가 얼마나 필요한지 표준품셈 8-2-13으로 보인다.
                sb.AppendLine("암 깨기 공기 안에: 대형브레이커 " + plan.BreakersToKeepUp + "대 (보통암 " + RockBreaker.MediumRockHourly.ToString("0.0", Invariant)
                    + " m³/시간, 8-2-13) 또는 일반발파 크롤러드릴 " + plan.DrillsToKeepUp(RockBreaker.GeneralBlast) + "대 ("
                    + RockBreaker.GeneralBlast.HourlyPerDrill.ToString("0", Invariant) + " m³/시간, 3-3-5) — 공기에는 안 넣음");
            }

            if (_view == View.Stage)
            {
                sb.AppendLine("진행    " + _day.ToString("F1", Invariant) + "일째:  누적 절토 "
                    + Volume(schedule.CumulativeCut(_day)) + ",  누적 성토 " + Volume(schedule.CumulativeFill(_day)));
            }

            string viewName = _view == View.Design ? "계획면 (공사 후)"
                : (_view == View.Ground ? "원지반 (공사 전)" : "공사 중 (절토는 위부터, 성토는 아래부터)");
            sb.Append("보는 면: " + viewName + (_dam != null ? "     회갈색 = 지금까지 쌓은 댐" : "     적갈색 절토 · 청록 성토 (진할수록 깊다)"));
            return sb.ToString();
        }

        /// <summary>건물 보기가 켜져 있을 때의 짧은 글 (자세한 값은 T로 건물 보기를 끄면 다시 나온다).</summary>
        private string BuildShortText()
        {
            EarthworkPlan plan = CurrentPlan();
            var sb = new System.Text.StringBuilder();
            if (_dam != null)
            {
                AppendDamLines(sb, brief: true);
            }
            else
            {
                sb.AppendLine("[부지 계획]");
                sb.AppendLine("부지    동서 " + (_widthCells * _ground.CellSize).ToString("N0", Invariant)
                    + " m × 남북 " + (_lengthCells * _ground.CellSize).ToString("N0", Invariant)
                    + " m,  계획고 " + _padElevation.ToString("F2", Invariant) + " m");
            }
            EarthBalance balance = Balance;
            sb.AppendLine("토공량  절토 " + Volume(_result.CutVolume) + ",  성토 " + Volume(_result.FillVolume)
                + (balance.BorrowVolume > 0.5
                    ? ",  토취 " + Volume(balance.BorrowVolume)
                    : ",  사토 " + Volume(balance.SurplusVolume))
                + (_materials.RockVolume > 0.5 ? ",  암 " + (_materials.RockRatio * 100.0).ToString("F0", Invariant) + " %" : ""));
            sb.AppendLine("토공    굴착기 " + machines + "대 + 트럭 " + trucks + "대 × " + hoursPerDay.ToString("0.#", Invariant) + "시간"
                + (interferenceRatio < 1.0 ? " (간섭 r " + interferenceRatio.ToString("0.##", Invariant) + ")" : "")
                + " → " + plan.TotalDays + "일"
                + (plan.IncludesHaul ? (plan.HaulIsBottleneck ? " (운반이 느림)" : " (굴착이 정함)") : ""));

            string viewName = _view == View.Design ? "계획면 (공사 후)"
                : (_view == View.Ground ? "원지반 (공사 전)" : "공사 중");
            sb.Append("보는 면: " + viewName + "   (자세히: 메뉴 [화면])");
            return sb.ToString();
        }

        /// <summary>정보 글상자를 접었을 때의 한 줄: 제목 + 공기 (댐은 체적 차이도)</summary>
        private string Headline()
        {
            int days = CurrentPlan().TotalDays;
            if (_dam != null)
            {
                double ratio = _result.FillVolume / _damSpec.OfficialVolume - 1.0;
                return "[" + _damSpec.Title + "] 체적 " + (ratio >= 0 ? "+" : "−") + System.Math.Abs(ratio * 100.0).ToString("F1", Invariant)
                    + " % · 토공 " + days.ToString("N0", Invariant) + "일";
            }
            return "[부지 계획] 토공 " + days.ToString("N0", Invariant) + "일 · 계획고 " + _padElevation.ToString("F2", Invariant) + " m";
        }

        private static string Volume(double v)
        {
            return v.ToString("N0", Invariant) + " m³";
        }

        private static string Area(double a)
        {
            return a.ToString("N0", Invariant) + " m²";
        }
    }
}
