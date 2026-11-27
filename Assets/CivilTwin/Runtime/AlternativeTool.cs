using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CivilTwin.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CivilTwin.Runtime
{
    /// <summary>
    /// 시공 대안 비교 도구 (Phase 4). 지금의 시공 조건을 A / B / C로 저장해 두고,
    /// 같은 현장(같은 부지, 같은 건물)에서 공기와 비용이 어떻게 달라지는지 표로 나란히 보여 준다.
    /// 계산은 Core/AlternativeAnalysis가 한다. 저장하는 것은 "조건"뿐이고, 결과는 볼 때마다 지금 현장으로 다시 계산한다.
    ///
    /// 조작:
    ///   O                : 대안 비교표 → 비교 그래프 → 숨기기 (누를 때마다 바뀐다)
    ///   1 / 2 / 3        : 지금 조건을 A / B / C로 저장
    ///   Shift + 1 / 2 / 3 : 저장한 A / B / C 조건을 불러와 적용 (공기, 공정표, 4D 재생이 그 조건으로 바뀐다)
    ///   조건은 부지 계획 도구의 키로 바꾼다: , / . 굴착기 대수   ; / ' 하루 작업 시간   I 장비 간섭 가정
    ///
    /// 원칙: 어느 대안이 "최적"이라고 표시하지 않는다. 숫자만 나란히 보여 준다.
    /// 비용은 굴착기 장비비와 기간 비례비만 더한 것이고 단가는 가정값이다.
    /// 화면 글자는 임시(IMGUI)다.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class AlternativeTool : MonoBehaviour
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly string[] Names = { "A", "B", "C" };

        // ---- 단가 (5-5에서 출처 확인, Docs/ASSUMPTIONS.md) ----
        // 시간당 기계경비 = 손료 + 연료 × 경유 단가 × (1 + 잡재료) + 노임 / 8  (Core/MachineCost)

        /// <summary>경유 단가 (원/L). 2026년 11월 첫째 주 전국 평균 1,842.0원 (오피넷 인용 보도). 시기마다 바뀌는 입력값.</summary>
        public double dieselPrice = 1842.0;

        /// <summary>건설기계운전사 일 노임 (원/일). 2026년 상반기 건설업 시중노임단가 (대한건설협회, 2차 출처로 확인).</summary>
        public double machineOperatorWage = 283297.0;

        /// <summary>화물차운전사 일 노임 (원/일). 같은 출처.</summary>
        public double truckDriverWage = 238936.0;

        /// <summary>굴착기(1.0 m³) 한 대의 시간당 기계경비 (원/시간) — 약 107,134원</summary>
        public double machineHourlyRate => MachineCost.Hourly(MachineCostItem.Excavator1m3_2024, dieselPrice, machineOperatorWage);

        /// <summary>덤프트럭(15 t) 한 대의 시간당 기계경비 (원/시간) — 약 89,915원</summary>
        public double truckHourlyRate => MachineCost.Hourly(MachineCostItem.DumpTruck15t_2024, dieselPrice, truckDriverWage);

        /// <summary>
        /// 공사 기간에 비례하는 하루 비용 (원/일, 현장 관리비 같은 것). **가정값, 출처 없음** —
        /// 실제로는 간접공사비 요율(제비율)로 정해지고 공사 규모에 따라 달라서 하나의 단가로 가져올 수 없다.
        /// </summary>
        public double dailyOverhead = 500000.0;

        private GradingPlanTool _grading;
        private BuildingSiteTool _building;
        private readonly AlternativeInput[] _saved = new AlternativeInput[3];
        private readonly bool[] _hasSaved = new bool[3];
        private enum Mode { Off, Table, Graph }
        private Mode _mode = Mode.Off;
        private GUIStyle _cell;
        private GUIStyle _small;

        /// <summary>그래프에 그리는 굴착기 대수의 범위 (1대 ~ 이 값). 지금 대수가 더 크면 거기까지 늘린다.</summary>
        private const int GraphMachines = 12;

        /// <summary>
        /// 비교표가 화면에 떠 있으면 true (다른 글상자들이 자리를 비켜 준다).
        /// 네트워크 공정표·시공 단계 화면을 여는 동안에는 그리지 않는다
        /// (2026-11-27 사용설명서 확인: 댐 3D 단계 화면의 아래 버튼 줄을 비교표가 덮었다).
        /// </summary>
        public bool ShowingTable => _mode != Mode.Off && _grading != null && _grading.HasPad && HudMode.ShowBoxes && !StructureViewOpen;

        private bool StructureViewOpen
        {
            get
            {
                var schedule = GetComponent<ScheduleView>();
                var section = GetComponent<DamSectionView>();
                return (schedule != null && schedule.IsOpen) || (section != null && section.IsOpen);
            }
        }

        /// <summary>비교표·그래프가 마지막으로 그려진 자리 (GUI 좌표). 건물 글상자가 겹치지 않는지 볼 때 쓴다.</summary>
        public Rect LastRect { get; private set; }

        /// <summary>비교표·그래프를 닫는다 (건물 공정표를 열 때, 2026-11-27).</summary>
        public void HideTable() => _mode = Mode.Off;

        /// <summary>비교표·그래프를 연다. 같은 왼쪽 자리를 쓰는 건물 공정표(CPM)는 닫는다.</summary>
        private void SetMode(Mode mode)
        {
            _mode = mode;
            if (mode != Mode.Off && _building != null) _building.HideSchedule();
        }

        public void Initialize(GradingPlanTool grading, BuildingSiteTool building)
        {
            // 현장이 바뀌면 저장한 대안을 지운다 (다른 현장의 조건과 섞어 비교하지 않도록).
            _grading = grading;
            _building = building;
            _mode = Mode.Off;
            for (int i = 0; i < _hasSaved.Length; i++) _hasSaved[i] = false;
        }

        private AlternativeInput CurrentInput(string name)
        {
            return new AlternativeInput(
                name, _grading.machines, _grading.hoursPerDay, _grading.interferenceRatio,
                machineHourlyRate, dailyOverhead, _grading.trucks, truckHourlyRate);
        }

        private SiteConditions CurrentSite()
        {
            // 5-4: 토공 조건(토사·암, L·C, 트럭, 운반 거리)을 함께 넘겨서 부지 도구와 같은 식으로 공기를 계산한다.
            EarthworkResult earthwork = _grading.Result;
            return new SiteConditions(
                earthwork.CutVolume, earthwork.FillVolume, _grading.HourlyVolumePerMachine,
                _grading.IsDamMode ? 0 : _building.floors,   // 댐은 건물이 없다 → 전체 공기 = 토공 공기
                _building.ExcavationVolume, _building.BaseDurations(), _grading.CurrentSetup(),
                _grading.TrenchRateFactor);   // 터파기: 작업효율 − 0.05 (표준품셈 164쪽 주 ④)
        }

        /// <summary>흙쌓기(펴고 다지기) 다진 흙 1 m³의 장비비 (원/m³, 표준품셈 3-4-1 조 ÷ 1,150 m³/일). 특별인부 노임 제외.</summary>
        public double EmbankmentCostPerM3 => EmbankmentCrew.CostPerCubicMeter(dieselPrice, machineOperatorWage, truckDriverWage);

        /// <summary>지금 성토량의 흙쌓기 장비비 (원). 성토량에만 비례해 대안마다 같다 → 비교용 합계에는 넣지 않는다.</summary>
        public double EmbankmentCost => _grading != null && _grading.HasPad ? _grading.Result.FillVolume * EmbankmentCostPerM3 : 0.0;

        private void Update()
        {
            if (_grading == null || _building == null) return;
            if (!InputGate.Allowed) return;
            if (!_grading.HasPad) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.oKey.wasPressedThisFrame) CycleMode();

            bool shift = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            HandleSlot(0, keyboard.digit1Key.wasPressedThisFrame, shift);
            HandleSlot(1, keyboard.digit2Key.wasPressedThisFrame, shift);
            HandleSlot(2, keyboard.digit3Key.wasPressedThisFrame, shift);
        }

        private void HandleSlot(int slot, bool pressed, bool shift)
        {
            if (!pressed) return;
            if (shift) Load(slot); else Save(slot);
        }

        // ---------- 보고서 (5-7) ----------

        /// <summary>보고서 폴더: 프로젝트 폴더/Reports (Git에 올리지 않는다)</summary>
        public static string ReportFolder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Reports"));

        /// <summary>마지막으로 저장한 보고서 경로 (없으면 빈 글)</summary>
        public string LastReportPath { get; private set; } = "";

        /// <summary>지금 조건과 저장한 대안들의 결과 (보고서·메뉴 문장용). [0] = 지금.</summary>
        public List<AlternativeResult> CurrentResults()
        {
            var list = new List<AlternativeResult>();
            if (_grading == null || _building == null || !_grading.HasPad) return list;
            SiteConditions site = CurrentSite();
            list.Add(AlternativeAnalysis.Evaluate(CurrentInput("지금"), site));
            for (int i = 0; i < _saved.Length; i++)
            {
                if (_hasSaved[i]) list.Add(AlternativeAnalysis.Evaluate(_saved[i], site));
            }
            return list;
        }

        /// <summary>
        /// 보고서를 Markdown(읽기용)과 CSV(대안 표, 엑셀용)로 저장한다. 저장한 Markdown 경로를 돌려준다 (부지가 없으면 빈 글).
        /// </summary>
        public string SaveReport()
        {
            List<AlternativeResult> results = CurrentResults();
            if (results.Count == 0) return "";

            var sites = GetComponent<SiteSwitcher>();
            string siteName = sites != null ? sites.CurrentName : "현장";
            var input = new ReportInput
            {
                SiteName = siteName,
                GeneratedAt = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                CenterEasting = _grading.CenterEasting,
                CenterNorthing = _grading.CenterNorthing,
                PadWidth = _grading.PadWidth,
                PadLength = _grading.PadLength,
                PadElevation = _grading.PadElevation,
                CutSlopeRatio = _grading.cutSlopeRatio,
                FillSlopeRatio = _grading.fillSlopeRatio,
                CutVolume = _grading.Result.CutVolume,
                FillVolume = _grading.Result.FillVolume,
                MaxCutDepth = _grading.MaxCutDepth,
                MaxFillHeight = _grading.MaxFillHeight,
                Materials = _grading.Materials,
                RockDepthLabel = _grading.RockDepthLabel,
                HourlyPerMachine = _grading.HourlyVolumePerMachine,
                OnSiteHaulMeters = _grading.Haul.Meters,
                SpoilKm = _grading.spoilKm,
                BorrowKm = _grading.borrowKm,
                EmbankmentCostPerM3 = EmbankmentCostPerM3,
                Building = _building.Plan,   // 댐이면 보고서가 쓰지 않는다 (공정표가 없으므로)
                Alternatives = results,
            };
            if (_grading.IsDamMode)
            {
                input.StructureTitle = _grading.DamSpec.Title + " (" + _grading.DamSpec.TypeName + ")";
                input.StructureLines.AddRange(_grading.DamReportLines());
            }
            input.Assumptions.Add(_grading.IsDamMode
                ? "흙·굴착기·덤프트럭 계수: 2025년 적용 건설공사 표준품셈, 댐 재료 = 역(礫) L 1.15 · C 1.075 (범위의 가운데) (쪽수는 저장소의 Docs/ASSUMPTIONS.md)"
                : "흙·굴착기·덤프트럭 계수: 2025년 적용 건설공사 표준품셈, 대표 흙 = 점질토·보통 조건 (쪽수는 저장소의 Docs/ASSUMPTIONS.md)");
            input.Assumptions.Add("기계경비: 굴착기 " + machineHourlyRate.ToString("N0", Invariant) + "원/시간, 덤프트럭 "
                + truckHourlyRate.ToString("N0", Invariant) + "원/시간 = 손료 + 연료 × 경유 " + dieselPrice.ToString("N0", Invariant)
                + "원/L × (1 + 잡재료) + 노임/8 (2024 건설기계의 기계경비 산출표, 2026 상반기 건설업 시중노임단가)");
            input.Assumptions.Add("흙쌓기 조: 표준품셈 3-4-1 일당 1,150 m³(30 cm). 장비비 " + EmbankmentCostPerM3.ToString("N0", Invariant)
                + "원/m³ = (진동롤러 10 t + 모터그레이더 3.6 m + 굴착기 0.6 m³ + 살수차 16,000 L × 0.5) 시간당 기계경비 × 8 ÷ 1,150. 특별인부 노임 제외, 살수차 조종원은 화물차운전사로 봄 (가정)");
            input.Assumptions.Add("터파기 작업효율 = E − 0.05 (표준품셈 164쪽 주 ④)");
            input.Assumptions.Add("기간 비례비 " + Money(dailyOverhead) + "원/일 (가정값, 출처 없음 — 실제로는 간접공사비 요율로 정한다)");
            input.Assumptions.Add("암 굴착 작업량 = 토사의 " + _grading.rockRateRatio.ToString("0.##", Invariant) + "배, 사토장·토취장 거리, 장비 간섭 식 (가정값)");
            input.Assumptions.Add(_grading.IsDamMode
                ? _grading.DamSpec.ReportAssumption
                : "건물 치수와 공종별 일수 (가설·기초·크레인·기둥·슬래브), 비탈 구배 (가정값)");
            input.Assumptions.Add("지형: Copernicus DEM GLO-30 (약 30 m 칸, DSM) — 설계 정밀도가 아니다");

            Directory.CreateDirectory(ReportFolder);
            string stamp = System.DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string safeSite = siteName;
            foreach (char c in Path.GetInvalidFileNameChars()) safeSite = safeSite.Replace(c, '_');
            safeSite = safeSite.Replace(' ', '_').Replace('·', '-');
            string md = Path.Combine(ReportFolder, "보고서_" + safeSite + "_" + stamp + ".md");
            string csv = Path.Combine(ReportFolder, "대안표_" + safeSite + "_" + stamp + ".csv");
            File.WriteAllText(md, ProjectReport.Markdown(input), new System.Text.UTF8Encoding(false));
            File.WriteAllText(csv, ProjectReport.Csv(results), new System.Text.UTF8Encoding(true));   // 엑셀이 한글을 읽도록 BOM
            LastReportPath = md;
            Debug.Log("[CivilTwin] 보고서 저장: " + md);
            return md;
        }

        // ---------- 동작 (키와 화면 메뉴가 함께 쓴다) ----------

        /// <summary>비교표 → 그래프 → 닫기</summary>
        public void CycleMode()
        {
            if (_grading == null || !_grading.HasPad) return;
            SetMode(_mode == Mode.Off ? Mode.Table : (_mode == Mode.Table ? Mode.Graph : Mode.Off));
        }

        /// <summary>지금 화면 상태 이름 (메뉴 표시용)</summary>
        public string ModeName => _mode == Mode.Off ? "닫힘" : (_mode == Mode.Table ? "비교표" : "그래프");

        public bool HasSaved(int slot) => slot >= 0 && slot < _hasSaved.Length && _hasSaved[slot];

        /// <summary>지금 조건을 A / B / C(slot 0 / 1 / 2)로 저장한다. 저장하면 표를 바로 보여 준다.</summary>
        public void Save(int slot)
        {
            if (_grading == null || !_grading.HasPad) return;
            _saved[slot] = CurrentInput(Names[slot]);
            _hasSaved[slot] = true;
            if (_mode == Mode.Off) SetMode(Mode.Table);
        }

        /// <summary>저장한 조건을 불러와 적용한다 (공기·공정표·4D 재생이 그 조건으로 바뀐다).</summary>
        public void Load(int slot)
        {
            if (!HasSaved(slot)) return;
            AlternativeInput input = _saved[slot];
            _grading.ApplyConditions(input.Machines, input.HoursPerDay, input.InterferenceRatio, input.Trucks);
        }

        /// <summary>
        /// 자동 시연(ScenarioRunner)이 로그에 남기는 글: 지금·A·B·C 줄을 표와 같은 칸으로 적는다.
        /// 장비비는 굴착기와 트럭으로 나눠서 함께 적는다 (검산용).
        /// </summary>
        public string DescribeForLog()
        {
            if (_grading == null || _building == null || !_grading.HasPad) return "(부지 없음)";

            SiteConditions site = CurrentSite();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("대안 | 굴착기 | 트럭 | 시간/일 | 간섭 r | 토공 | 다짐 조 | 전체 공기 | 장비비 | 기간 비례비 | 합 | 주공정 시작 | (굴착기비, 트럭비, 굴착 일수, 운반 일수)");
            AppendLogRow(sb, AlternativeAnalysis.Evaluate(CurrentInput("지금"), site));
            for (int i = 0; i < _saved.Length; i++)
            {
                if (_hasSaved[i]) AppendLogRow(sb, AlternativeAnalysis.Evaluate(_saved[i], site));
                else sb.AppendLine(Names[i] + " (비어 있음)");
            }
            return sb.ToString();
        }

        private static void AppendLogRow(System.Text.StringBuilder sb, AlternativeResult r)
        {
            sb.Append(string.Join(" | ", Cells(r)));
            sb.Append(" | (" + Money(r.ExcavatorCost) + ", " + Money(r.TruckCost));
            if (r.Earthwork != null)
            {
                sb.Append(", " + r.Earthwork.ExcavationDays.ToString("F3", Invariant) + "일, "
                    + r.Earthwork.HaulDays.ToString("F3", Invariant) + "일");
            }
            sb.AppendLine(")");
        }

        // ---------- 화면 글자 (임시 IMGUI) ----------

        private void OnGUI()
        {
            HudMode.ApplyMatrix();   // 글자 크기 (메뉴 [화면])
            if (!ShowingTable) return;

            if (_cell == null)
            {
                _cell = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = false };
                _cell.normal.textColor = HudStyle.Ui(Color.white);

                _small = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = false };
                _small.normal.textColor = HudStyle.Ui(Color.white);
            }

            if (_mode == Mode.Graph)
            {
                DrawGraph();
                return;
            }

            SiteConditions site = CurrentSite();
            if (HudMode.TableCollapsed)
            {
                // 접은 표 (2026-11-21): 지금 조건과 저장한 대안의 전체 공기·합 한 줄
                AlternativeResult cur = AlternativeAnalysis.Evaluate(CurrentInput("지금"), site);
                var parts = new System.Collections.Generic.List<string>();
                for (int i = 0; i < _saved.Length; i++)
                    if (_hasSaved[i]) parts.Add(Names[i] + " " + AlternativeAnalysis.Evaluate(_saved[i], site).ProjectDays + "일");
                string line = "[대안 비교] 지금 " + cur.ProjectDays + "일 · " + Money(cur.TotalCost)
                    + (parts.Count > 0 ? "   |   " + string.Join(" · ", parts) : "   (저장한 대안 없음)");
                float lw = _cell.CalcSize(new GUIContent(line)).x + 104f;
                var box = new Rect(10f, HudMode.GuiHeight - 30f - 12f, lw, 30f);
                LastRect = box;
                HudStyle.DrawBox(box);
                HudLayout.Register(box);
                GUI.color = HudStyle.Ui(Color.white);
                GUI.Label(new Rect(box.x + 10f, box.y + 6f, lw - 96f, 20f), line, _cell);
                InfoBox.CollapseButton(box, true, HudMode.ToggleTableCollapsed, "표");
                return;
            }
            float[] widths = { 44f, 50f, 44f, 48f, 44f, 54f, 52f, 68f, 76f, 76f, 76f, 70f };   // 전체 공기 칸: 머리글이 옆 칸에 붙어 보여 58 → 68
            string[] heads = { "대안", "굴착기", "트럭", "시간/일", "간섭 r", "토공", "다짐 조", "전체 공기", "장비비", "기간 비례비", "합", "주공정 시작" };
            const float rowHeight = 18f;
            const int rows = 9;   // 안내 1 + 머리 1 + 지금 1 + 대안 3 + 주석 3

            float tableWidth = 16f;
            foreach (float w in widths) tableWidth += w;
            float tableHeight = 14f + rowHeight * rows;
            float x = 10f;
            float y = HudMode.GuiHeight - tableHeight - 12f;
            LastRect = new Rect(x, y, tableWidth, tableHeight);
            HudStyle.DrawBox(LastRect);
            InfoBox.CollapseButton(new Rect(x, y, tableWidth, tableHeight), false, HudMode.ToggleTableCollapsed, "표");

            float[] full = { tableWidth - 16f };
            float cx = x + 8f;
            float cy = y + 6f;

            DrawRow(cx, cy, full, new[] { "[대안 비교]   저장·불러오기·그래프는 메뉴 [대안]  (키: 1 2 3, Shift + 숫자, O)" }, Color.white);
            cy += rowHeight;
            DrawRow(cx, cy, widths, heads, new Color(0.75f, 0.85f, 1.0f));
            cy += rowHeight;

            var yellow = new Color(1.0f, 0.82f, 0.25f);
            AlternativeInput now = CurrentInput("지금");
            DrawRow(cx, cy, widths, Cells(AlternativeAnalysis.Evaluate(now, site)), yellow);
            cy += rowHeight;

            for (int i = 0; i < _saved.Length; i++)
            {
                if (_hasSaved[i])
                {
                    DrawRow(cx, cy, widths, Cells(AlternativeAnalysis.Evaluate(_saved[i], site)), Color.white);
                }
                else
                {
                    DrawRow(cx, cy, full, new[] { Names[i] + "     (비어 있음 — " + (i + 1) + " 키로 저장)" }, new Color(0.7f, 0.7f, 0.7f));
                }
                cy += rowHeight;
            }

            DrawRow(cx, cy, full, new[]
            {
                (_grading.IsDamMode ? "공기 = 토공(굴착·운반 중 느린 쪽), 댐이라 건물 없음." : "공기 = 토공(굴착·운반 중 느린 쪽) + 건물 " + _building.floors + "층.")
                + "  비용 = 장비비(굴착기·트럭) + 기간 비례비만"
            }, Color.white);
            cy += rowHeight;
            DrawRow(cx, cy, full, new[]
            {
                "다짐 조: 흙쌓기 조 수 (표준품셈 3-4-1, 조당 1,150 m³/일). 장비비 "
                + KoreanMoney.Format(EmbankmentCost) + "은 대안마다 같아 합에서 뺌"
            }, Color.white);
            cy += rowHeight;
            DrawRow(cx, cy, full, new[]
            {
                "단가: 굴착기 " + Money(machineHourlyRate) + "/시간, 트럭 " + Money(truckHourlyRate)
                + "/시간 (기계경비 산출표), 기간 " + Money(dailyOverhead) + "/일 (가정).  최적안을 정하지 않음"
            }, Color.white);
        }

        // ---------- 비교 그래프 (4-4) ----------

        private static readonly Color EarthworkColor = new Color(0.80f, 0.36f, 0.24f);
        private static readonly Color BuildingColor = new Color(0.62f, 0.64f, 0.68f);
        private static readonly Color EquipmentColor = new Color(0.93f, 0.60f, 0.16f);
        private static readonly Color TimeColor = new Color(0.36f, 0.58f, 0.92f);
        private static readonly Color NowColor = new Color(1.0f, 0.90f, 0.30f);

        /// <summary>
        /// 굴착기 대수(가로)에 따른 전체 공기(왼쪽)와 비용(오른쪽)을 막대로 그린다.
        /// 대수만 바꾸고 하루 작업 시간과 간섭 가정은 지금 조건 그대로 둔다. 값은 Core/AlternativeAnalysis.SweepMachines.
        ///   왼쪽 막대: 아래 = 토공 공기, 위 = 그 뒤의 건물 공사 (합 = 전체 공기)
        ///   오른쪽 막대: 아래 = 장비비, 위 = 기간 비례비 (합 = 비교용 비용)
        /// 지금 대수의 막대 위에는 노란 표시를, 저장한 대안의 대수 아래에는 A / B / C를 적는다.
        /// </summary>
        private void DrawGraph()
        {
            AlternativeInput now = CurrentInput("지금");
            int count = Mathf.Clamp(Mathf.Max(GraphMachines, now.Machines), 1, 24);
            AlternativeResult[] sweep = AlternativeAnalysis.SweepMachines(now, CurrentSite(), count);
            AlternativeResult current = sweep[Mathf.Clamp(now.Machines, 1, count) - 1];

            double maxDays = 0.0, maxCost = 0.0;
            foreach (AlternativeResult r in sweep)
            {
                maxDays = System.Math.Max(maxDays, r.ProjectDays);
                maxCost = System.Math.Max(maxCost, r.TotalCost);
            }
            double daysAxis = EarthworkCurve.NiceAxisMax(System.Math.Max(maxDays, 1.0));
            double costAxis = EarthworkCurve.NiceAxisMax(System.Math.Max(maxCost, 1.0));

            const float boxWidth = 640f, plotWidth = 250f, plotHeight = 120f, rowHeight = 18f;
            float boxHeight = 14f + rowHeight * 2 + 4f + 22f + plotHeight + 36f + 22f;
            float x = 10f;
            float y = HudMode.GuiHeight - boxHeight - 12f;
            LastRect = new Rect(x, y, boxWidth, boxHeight);
            HudStyle.DrawBox(LastRect);

            float[] full = { boxWidth - 16f };
            float cy = y + 6f;
            DrawRow(x + 8f, cy, full, new[]
            {
                "[대안 그래프]  O: 닫기   굴착기 대수에 따른 공기와 비용   (트럭 " + now.Trucks + "대, 하루 "
                + now.HoursPerDay.ToString("0.#", Invariant)
                + "시간, 간섭 r " + now.InterferenceRatio.ToString("0.##", Invariant) + " 기준)"
            }, Color.white);
            cy += rowHeight;
            DrawRow(x + 8f, cy, full, new[]
            {
                "지금 " + now.Machines + "대:  전체 공기 " + current.ProjectDays + "일,  비용 "
                + Money(current.TotalCost) + " 원   (노란 표시)"
            }, NowColor);
            cy += rowHeight + 4f;

            float plotY = cy + 22f;
            float leftPlot = x + 56f;
            float rightPlot = x + 56f + plotWidth + 70f;

            DrawLegend(leftPlot, cy, "전체 공기 (일)", "토공", EarthworkColor, "건물", BuildingColor);
            DrawLegend(rightPlot, cy, "비용 (억 원)", "장비비", EquipmentColor, "기간 비례비", TimeColor);

            DrawAxis(leftPlot, plotY, plotWidth, plotHeight,
                daysAxis.ToString("0", Invariant), (daysAxis / 2.0).ToString("0", Invariant));
            DrawAxis(rightPlot, plotY, plotWidth, plotHeight,
                (costAxis / 1e8).ToString("0.#", Invariant), (costAxis / 2e8).ToString("0.#", Invariant));

            float slot = plotWidth / count;
            float bar = Mathf.Max(3f, slot * 0.68f);
            for (int i = 0; i < count; i++)
            {
                AlternativeResult r = sweep[i];
                float bx = (slot - bar) / 2f + slot * i;
                bool isNow = r.Input.Machines == now.Machines;

                DrawStack(leftPlot + bx, plotY, bar, plotHeight,
                    r.EarthworkDays / daysAxis, EarthworkColor, r.ProjectDays / daysAxis, BuildingColor, isNow);
                DrawStack(rightPlot + bx, plotY, bar, plotHeight,
                    r.EquipmentCost / costAxis, EquipmentColor, r.TotalCost / costAxis, TimeColor, isNow);

                // 가로축: 대수 (막대가 좁으면 하나 건너 하나만 적는다)와 저장한 대안의 이름
                if (count <= 12 || i % 2 == 0)
                {
                    string label = (i + 1).ToString(Invariant);
                    DrawCentered(leftPlot + slot * i, plotY + plotHeight + 1f, slot, label, Color.white);
                    DrawCentered(rightPlot + slot * i, plotY + plotHeight + 1f, slot, label, Color.white);
                }
                string saved = SavedNamesAt(r.Input.Machines, now);
                if (saved.Length > 0)
                {
                    DrawCentered(leftPlot + slot * i - 10f, plotY + plotHeight + 15f, slot + 20f, saved, NowColor);
                    DrawCentered(rightPlot + slot * i - 10f, plotY + plotHeight + 15f, slot + 20f, saved, NowColor);
                }
            }

            _small.alignment = TextAnchor.MiddleLeft;
            GUI.Label(new Rect(x + 8f, y + boxHeight - 24f, boxWidth - 16f, 20f),
                "가로 = 굴착기 대수. 비용 = 장비비 + 기간 비례비, 단가·간섭은 가정값. A·B·C는 시간·간섭이 같을 때만",
                _small);
        }

        /// <summary>그 대수로 저장한 대안의 이름들. 하루 작업 시간과 간섭이 지금 조건과 같은 것만 (같은 그래프 위의 값이라서).</summary>
        private string SavedNamesAt(int machines, AlternativeInput now)
        {
            string names = "";
            for (int i = 0; i < _saved.Length; i++)
            {
                if (!_hasSaved[i] || _saved[i].Machines != machines) continue;
                if (_saved[i].HoursPerDay != now.HoursPerDay || _saved[i].InterferenceRatio != now.InterferenceRatio) continue;
                if (_saved[i].Trucks != now.Trucks) continue;
                names += Names[i];
            }
            return names;
        }

        private void DrawLegend(float x, float y, string title, string lower, Color lowerColor, string upper, Color upperColor)
        {
            _small.alignment = TextAnchor.MiddleLeft;
            GUI.Label(new Rect(x, y, 100f, 18f), title, _small);

            Color previous = GUI.color;
            GUI.color = HudStyle.Ui(lowerColor);
            GUI.DrawTexture(new Rect(x + 104f, y + 5f, 10f, 10f), Texture2D.whiteTexture);
            GUI.color = HudStyle.Ui(upperColor);
            GUI.DrawTexture(new Rect(x + 164f, y + 5f, 10f, 10f), Texture2D.whiteTexture);
            GUI.color = previous;

            GUI.Label(new Rect(x + 118f, y, 46f, 18f), lower, _small);
            GUI.Label(new Rect(x + 178f, y, 80f, 18f), upper, _small);
        }

        /// <summary>그림 바탕, 가로 눈금선(0, 절반, 최댓값)과 세로축 숫자.</summary>
        private void DrawAxis(float x, float y, float width, float height, string maxLabel, string halfLabel)
        {
            Color previous = GUI.color;
            GUI.color = HudStyle.Ui(new Color(0.07f, 0.07f, 0.07f, 0.92f));
            GUI.DrawTexture(new Rect(x, y, width, height), Texture2D.whiteTexture);
            GUI.color = HudStyle.Ui(new Color(0.30f, 0.30f, 0.30f, 1f));
            GUI.DrawTexture(new Rect(x, y, width, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x, y + height / 2f, width, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x, y + height - 1f, width, 1f), Texture2D.whiteTexture);
            GUI.color = previous;

            _small.alignment = TextAnchor.MiddleRight;
            GUI.Label(new Rect(x - 54f, y - 9f, 50f, 18f), maxLabel, _small);
            GUI.Label(new Rect(x - 54f, y + height / 2f - 9f, 50f, 18f), halfLabel, _small);
            GUI.Label(new Rect(x - 54f, y + height - 9f, 50f, 18f), "0", _small);
        }

        /// <summary>두 칸으로 쌓은 막대. lower와 total은 세로축 최댓값에 대한 비율 (0 ~ 1).</summary>
        private static void DrawStack(float x, float plotY, float width, float plotHeight,
            double lower, Color lowerColor, double total, Color upperColor, bool marked)
        {
            float lowerHeight = plotHeight * Mathf.Clamp01((float)lower);
            float totalHeight = plotHeight * Mathf.Clamp01((float)total);
            float bottom = plotY + plotHeight;

            Color previous = GUI.color;
            GUI.color = HudStyle.Ui(upperColor);
            GUI.DrawTexture(new Rect(x, bottom - totalHeight, width, totalHeight - lowerHeight), Texture2D.whiteTexture);
            GUI.color = HudStyle.Ui(lowerColor);
            GUI.DrawTexture(new Rect(x, bottom - lowerHeight, width, lowerHeight), Texture2D.whiteTexture);
            if (marked)
            {
                GUI.color = HudStyle.Ui(NowColor);
                GUI.DrawTexture(new Rect(x - 2f, bottom - totalHeight - 5f, width + 4f, 3f), Texture2D.whiteTexture);
            }
            GUI.color = previous;
        }

        private void DrawCentered(float x, float y, float width, string text, Color color)
        {
            Color previous = GUI.color;
            GUI.color = HudStyle.Ui(color);
            _small.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(x, y, width, 16f), text, _small);
            GUI.color = previous;
        }

        private static string[] Cells(AlternativeResult r)
        {
            return new[]
            {
                r.Input.Name,
                r.Input.Machines + "대",
                r.Input.Trucks + "대",
                r.Input.HoursPerDay.ToString("0.#", Invariant),
                r.Input.InterferenceRatio.ToString("0.##", Invariant),
                r.EarthworkDays + "일",
                r.EmbankmentCrews + "조",
                r.ProjectDays + "일",
                Money(r.EquipmentCost),
                Money(r.TimeCost),
                Money(r.TotalCost),
                AlternativeNarrative.CriticalStart(r.Schedule),   // 건물이 없으면(댐) "토공"
            };
        }

        /// <summary>
        /// 화면에 보이는 금액. 1억 원 이상은 억 원 (소수 한 자리), 그 아래는 만 원 (예: 3,251,820,000 → "32.5억", 500,000 → "50만").
        /// 2026-11-27 사용설명서 확인: 표의 13 px 글자에서 천 단위 쉼표가 마침표처럼 보여 "306,132만"이 "306.132만"으로 읽혔다
        /// → 쉼표가 필요 없는 단위로 바꿨다. 보고서(Markdown·CSV)의 금액은 원 단위 그대로다.
        /// </summary>
        public static string Money(double won)
        {
            if (System.Math.Abs(won) >= 1e8) return (won / 1e8).ToString("0.0", Invariant) + "억";
            return (won / 10000.0).ToString("0", Invariant) + "만";
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
