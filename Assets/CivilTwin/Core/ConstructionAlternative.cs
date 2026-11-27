using System;

namespace CivilTwin.Core
{
    /// <summary>
    /// 시공 대안 하나의 조건. 사람이 정하는 값들이다.
    /// 단가와 간섭 비율은 가정값이다 (출처 확인 전).
    /// </summary>
    public readonly struct AlternativeInput
    {
        /// <summary>대안 이름 (예: "A")</summary>
        public readonly string Name;

        /// <summary>굴착기 대수</summary>
        public readonly int Machines;

        /// <summary>하루 작업 시간</summary>
        public readonly double HoursPerDay;

        /// <summary>장비 간섭 비율 r (1이면 간섭 없음). MachineInterference 참고.</summary>
        public readonly double InterferenceRatio;

        /// <summary>굴착기 한 대의 시간당 단가 (원/시간)</summary>
        public readonly double MachineHourlyRate;

        /// <summary>공사 기간에 비례하는 하루 비용 (원/일)</summary>
        public readonly double DailyOverhead;

        /// <summary>덤프트럭 대수 (0이면 운반을 계산하지 않는다)</summary>
        public readonly int Trucks;

        /// <summary>덤프트럭 한 대의 시간당 단가 (원/시간)</summary>
        public readonly double TruckHourlyRate;

        public AlternativeInput(
            string name, int machines, double hoursPerDay, double interferenceRatio,
            double machineHourlyRate, double dailyOverhead)
            : this(name, machines, hoursPerDay, interferenceRatio, machineHourlyRate, dailyOverhead, 0, 0.0)
        {
        }

        public AlternativeInput(
            string name, int machines, double hoursPerDay, double interferenceRatio,
            double machineHourlyRate, double dailyOverhead, int trucks, double truckHourlyRate)
        {
            if (trucks < 0) throw new ArgumentOutOfRangeException(nameof(trucks));
            if (!(truckHourlyRate >= 0)) throw new ArgumentOutOfRangeException(nameof(truckHourlyRate));
            Trucks = trucks;
            TruckHourlyRate = truckHourlyRate;

            if (machines < 1) throw new ArgumentOutOfRangeException(nameof(machines));
            if (!(hoursPerDay > 0)) throw new ArgumentOutOfRangeException(nameof(hoursPerDay));
            if (!(interferenceRatio > 0) || interferenceRatio > 1) throw new ArgumentOutOfRangeException(nameof(interferenceRatio));
            if (!(machineHourlyRate >= 0)) throw new ArgumentOutOfRangeException(nameof(machineHourlyRate));
            if (!(dailyOverhead >= 0)) throw new ArgumentOutOfRangeException(nameof(dailyOverhead));

            Name = name ?? "";
            Machines = machines;
            HoursPerDay = hoursPerDay;
            InterferenceRatio = interferenceRatio;
            MachineHourlyRate = machineHourlyRate;
            DailyOverhead = dailyOverhead;
        }
    }

    /// <summary>대안에 따라 달라지지 않는 현장 조건: 토공량, 굴착기 성능, 건물.</summary>
    public readonly struct SiteConditions
    {
        public readonly double CutVolume;
        public readonly double FillVolume;

        /// <summary>굴착기 한 대의 시간당 작업량 (m³/시간). ExcavatorProductivity.HourlyVolume.</summary>
        public readonly double HourlyVolumePerMachine;

        /// <summary>건물 층수. 0 = 건물 없음 (댐처럼 토공 구조물만 있는 현장, 2026-11-20) → 전체 공기 = 토공 공기.</summary>
        public readonly int Floors;

        /// <summary>터파기 부피 (m³) = 건물 바닥 넓이 × 기초 두께</summary>
        public readonly double ExcavationVolume;

        /// <summary>공종별 일수. 터파기 일수는 대안마다 다시 계산하므로 여기 값은 쓰지 않는다.</summary>
        public readonly BuildingDurations Durations;

        /// <summary>
        /// 5-4 토공 조건 (토사·암, 토량환산계수, 트럭, 운반 거리). null이면 예전 식(절토·성토 중 긴 쪽)으로 계산한다.
        /// </summary>
        public readonly EarthworkSetup Earthwork;

        private readonly double _trenchRateFactor;

        /// <summary>
        /// 터파기 작업량 배율 = (E − 0.05) / E (2026-11-20). 표준품셈 굴착기 작업효율 [주] ④ "터파기에 대하여는 0.05를 뺀 값으로 한다"
        /// (2025년 적용판 164쪽). 값을 주지 않으면 1 (예전 계산과 같다).
        /// </summary>
        public double TrenchRateFactor => _trenchRateFactor > 0 ? _trenchRateFactor : 1.0;

        public SiteConditions(
            double cutVolume, double fillVolume, double hourlyVolumePerMachine,
            int floors, double excavationVolume, BuildingDurations durations)
            : this(cutVolume, fillVolume, hourlyVolumePerMachine, floors, excavationVolume, durations, null)
        {
        }

        public SiteConditions(
            double cutVolume, double fillVolume, double hourlyVolumePerMachine,
            int floors, double excavationVolume, BuildingDurations durations, EarthworkSetup earthwork,
            double trenchRateFactor = 1.0)
        {
            if (!(trenchRateFactor > 0) || trenchRateFactor > 1) throw new ArgumentOutOfRangeException(nameof(trenchRateFactor));
            _trenchRateFactor = trenchRateFactor;
            Earthwork = earthwork;
            if (!(cutVolume >= 0)) throw new ArgumentOutOfRangeException(nameof(cutVolume));
            if (!(fillVolume >= 0)) throw new ArgumentOutOfRangeException(nameof(fillVolume));
            if (!(hourlyVolumePerMachine > 0)) throw new ArgumentOutOfRangeException(nameof(hourlyVolumePerMachine));
            if (floors < 0) throw new ArgumentOutOfRangeException(nameof(floors));
            if (!(excavationVolume >= 0)) throw new ArgumentOutOfRangeException(nameof(excavationVolume));

            CutVolume = cutVolume;
            FillVolume = fillVolume;
            HourlyVolumePerMachine = hourlyVolumePerMachine;
            Floors = floors;
            ExcavationVolume = excavationVolume;
            Durations = durations;
        }
    }

    /// <summary>대안 하나를 계산한 결과.</summary>
    public sealed class AlternativeResult
    {
        public AlternativeInput Input;

        /// <summary>간섭을 반영한 실효 대수</summary>
        public double EffectiveMachines;

        /// <summary>굴착기 전체의 하루 작업량 (m³/일)</summary>
        public double DailyVolume;

        /// <summary>토공 공기 (일, 올림)</summary>
        public int EarthworkDays;

        /// <summary>터파기 일수 (올림, 최소 1일)</summary>
        public double ExcavationDays;

        /// <summary>토공 + 건물 전체 공기 (일, 올림)</summary>
        public int ProjectDays;

        /// <summary>굴착기 가동 일수 = 토공 공기 + 터파기 일수</summary>
        public double MachineWorkingDays;

        /// <summary>장비비 (원) = 굴착기 + 덤프트럭</summary>
        public double EquipmentCost;

        /// <summary>그중 굴착기 (원)</summary>
        public double ExcavatorCost;

        /// <summary>그중 덤프트럭 (원) = 트럭 대수 × 토공 공기 × 하루 작업 시간 × 시간당 단가</summary>
        public double TruckCost;

        /// <summary>5-4 토공 계산 결과 (현장 조건에 토공 조건이 없으면 null)</summary>
        public EarthworkPlan Earthwork;

        /// <summary>기간 비례비 (원)</summary>
        public double TimeCost;

        /// <summary>비교용 합계 = 장비비 + 기간 비례비 (원). 공사비 전체가 아니다.</summary>
        public double TotalCost;

        /// <summary>이 대안의 공정표 (CPM 결과 포함)</summary>
        public BuildingSchedule Schedule;
        /// <summary>
        /// 성토를 토공 공기 안에 펴고 다지려면 필요한 흙쌓기 조 수 (표준품셈 3-4-1, 한 조 하루 1,150 m³, 2026-11-20).
        /// 흙쌓기 비용은 성토량 × m³당 단가라 대안마다 같으므로 비교용 합계에 넣지 않는다.
        /// </summary>
        public int EmbankmentCrews;
    }

    /// <summary>
    /// 시공 대안 분석: 조건(장비 대수, 작업 시간)을 바꾸면 공기와 비용이 어떻게 달라지는지 계산한다.
    /// 새 공식은 없고, 지금까지 만든 계산을 조건만 바꿔 순서대로 다시 돌린다.
    ///
    ///   1) 실효 대수    = MachineInterference(대수, 간섭 비율)
    ///   2) 하루 작업량  = 한 대의 시간당 작업량 × 하루 작업 시간 × 실효 대수
    ///   3) 토공 공기    = EarthworkSchedule (절토·성토 중 긴 쪽을 올림)
    ///   4) 터파기 일수  = 터파기 부피 ÷ (하루 작업량 × 터파기 배율) (올림, 최소 1일). 배율은 기본 1, 화면은 (E − 0.05) / E
    ///   5) 전체 공기    = BuildingSchedule (CPM)
    ///   6) 장비비       = 대수 × (토공 공기 + 터파기 일수) × 하루 작업 시간 × 시간당 단가
    ///      기간 비례비 = 전체 공기 × 하루 단가
    ///
    /// 손계산 예: 절토 10000, 성토 4000 m³, 한 대 77.76 m³/시간, 하루 8시간, 간섭 없음,
    ///            2층, 터파기 480 m³, 가설 5·기초 7·크레인 3·기둥 3·슬래브 5일,
    ///            굴착기 100,000원/시간, 기간 비례비 500,000원/일
    ///   1대: 622.08 m³/일 → 토공 16.08 → 17일, 터파기 1일, 전체 17 + 1 + 7 + 16 = 41일
    ///        장비비 1 × 18 × 8 × 10만 = 1,440만,  기간 2,050만  → 합 3,490만
    ///   2대: 1244.16 → 9일, 전체 33일.  장비비 2 × 10 × 8 × 10만 = 1,600만, 기간 1,650만 → 3,250만
    ///   4대: 2488.32 → 5일, 전체 29일.  장비비 4 × 6 × 8 × 10만 = 1,920만,  기간 1,450만 → 3,370만
    ///   8대: 4976.64 → 3일, 전체 29일 (가설공사 5일이 주공정이 되어 더 줄지 않는다).
    ///        장비비 8 × 4 × 8 × 10만 = 2,560만, 기간 1,450만 → 4,010만
    ///
    /// 읽는 법: 장비를 늘리면 토공은 빨라지지만, 다른 작업이 주공정이 되면 전체 공기는 더 줄지 않고 장비비만 는다.
    ///
    /// 원칙: 어느 대안이 "최적"이라고 정하지 않는다. 숫자를 나란히 보여 주고 판단은 사람이 한다.
    /// </summary>
    public static class AlternativeAnalysis
    {
        public static AlternativeResult Evaluate(AlternativeInput input, SiteConditions site)
        {
            double effective = MachineInterference.EffectiveMachines(input.Machines, input.InterferenceRatio);
            double daily = site.HourlyVolumePerMachine * input.HoursPerDay * effective;

            // 토공 공기: 토공 조건이 있으면 5-4 식 (굴착과 운반 중 느린 쪽), 없으면 예전 식.
            EarthworkPlan plan = null;
            int earthworkDays;
            if (site.Earthwork != null)
            {
                plan = site.Earthwork.Build(site.FillVolume, daily, input.Trucks, input.HoursPerDay);
                earthworkDays = plan.TotalDays;
            }
            else
            {
                earthworkDays = new EarthworkSchedule(site.CutVolume, site.FillVolume, daily, daily).TotalDays;
            }
            // 건물이 없으면(층수 0: 댐) 터파기·건물 공정이 없고 전체 공기 = 토공 공기다.
            bool hasBuilding = site.Floors > 0;
            // 터파기는 표준품셈 주에 따라 작업효율에서 0.05를 뺀다 (TrenchRateFactor).
            double excavationDays = hasBuilding ? BuildingSchedule.ExcavationDays(site.ExcavationVolume, daily * site.TrenchRateFactor) : 0.0;

            BuildingDurations d = site.Durations;
            BuildingSchedule schedule = hasBuilding
                ? new BuildingSchedule(site.Floors, earthworkDays, new BuildingDurations(
                    d.TemporaryDays, excavationDays, d.FoundationDays, d.CraneSetupDays,
                    d.ColumnDaysPerFloor, d.SlabDaysPerFloor))
                : null;
            int projectDays = hasBuilding ? schedule.ProjectDays : earthworkDays;

            double machineDays = earthworkDays + excavationDays;
            double excavatorCost = ConstructionCost.Equipment(
                input.Machines, machineDays, input.HoursPerDay, input.MachineHourlyRate);
            // 트럭은 토공 기간에만 쓴다고 본다 (터파기 흙의 운반은 넣지 않음).
            double truckCost = ConstructionCost.Equipment(
                input.Trucks, earthworkDays, input.HoursPerDay, input.TruckHourlyRate);
            double equipmentCost = excavatorCost + truckCost;
            double timeCost = ConstructionCost.TimeRelated(projectDays, input.DailyOverhead);

            return new AlternativeResult
            {
                Input = input,
                EffectiveMachines = effective,
                DailyVolume = daily,
                EarthworkDays = earthworkDays,
                ExcavationDays = excavationDays,
                ProjectDays = projectDays,
                MachineWorkingDays = machineDays,
                EquipmentCost = equipmentCost,
                ExcavatorCost = excavatorCost,
                TruckCost = truckCost,
                Earthwork = plan,
                TimeCost = timeCost,
                TotalCost = equipmentCost + timeCost,
                Schedule = schedule,
                EmbankmentCrews = plan != null
                    ? plan.EmbankmentCrews
                    : EmbankmentCrew.CrewsToKeepUp(site.FillVolume, input.HoursPerDay, earthworkDays),
            };
        }

        /// <summary>
        /// 굴착기 대수만 1대부터 maxMachines대까지 바꿔 가며 계산한다 (다른 조건은 baseInput 그대로).
        /// 돌려주는 배열의 [0]이 1대, [n − 1]이 n대다. 비교 그래프에 쓴다.
        /// </summary>
        public static AlternativeResult[] SweepMachines(AlternativeInput baseInput, SiteConditions site, int maxMachines)
        {
            if (maxMachines < 1) throw new ArgumentOutOfRangeException(nameof(maxMachines));

            var results = new AlternativeResult[maxMachines];
            for (int n = 1; n <= maxMachines; n++)
            {
                var input = new AlternativeInput(
                    n + "대", n, baseInput.HoursPerDay, baseInput.InterferenceRatio,
                    baseInput.MachineHourlyRate, baseInput.DailyOverhead,
                    baseInput.Trucks, baseInput.TruckHourlyRate);   // 트럭 대수는 그대로 둔다
                results[n - 1] = Evaluate(input, site);
            }
            return results;
        }
    }
}
