using System;
using System.Collections.Generic;

namespace CivilTwin.Core
{
    /// <summary>건물 공사의 작업 종류.</summary>
    public enum BuildingWork
    {
        /// <summary>토공 (부지 정지): Phase 2의 절토·성토</summary>
        Earthwork,

        /// <summary>가설공사: 울타리, 현장 사무실 같은 준비</summary>
        TemporaryWorks,

        /// <summary>터파기: 기초가 놓일 깊이까지 땅을 판다</summary>
        Excavation,

        /// <summary>기초</summary>
        Foundation,

        /// <summary>타워크레인 설치</summary>
        CraneSetup,

        /// <summary>한 층의 기둥</summary>
        Columns,

        /// <summary>한 층의 슬래브</summary>
        Slab,
    }

    /// <summary>
    /// 공종별 소요 일수. 터파기를 뺀 값은 모두 **계산 예를 위한 가정**이다
    /// (표준품셈이나 현장 자료로 확인한 값이 아니다).
    /// </summary>
    public readonly struct BuildingDurations
    {
        public readonly double TemporaryDays;
        public readonly double ExcavationDays;
        public readonly double FoundationDays;
        public readonly double CraneSetupDays;
        public readonly double ColumnDaysPerFloor;
        public readonly double SlabDaysPerFloor;

        public BuildingDurations(
            double temporaryDays, double excavationDays, double foundationDays,
            double craneSetupDays, double columnDaysPerFloor, double slabDaysPerFloor)
        {
            if (!(temporaryDays >= 0)) throw new ArgumentOutOfRangeException(nameof(temporaryDays));
            if (!(excavationDays >= 0)) throw new ArgumentOutOfRangeException(nameof(excavationDays));
            if (!(foundationDays >= 0)) throw new ArgumentOutOfRangeException(nameof(foundationDays));
            if (!(craneSetupDays >= 0)) throw new ArgumentOutOfRangeException(nameof(craneSetupDays));
            if (!(columnDaysPerFloor >= 0)) throw new ArgumentOutOfRangeException(nameof(columnDaysPerFloor));
            if (!(slabDaysPerFloor >= 0)) throw new ArgumentOutOfRangeException(nameof(slabDaysPerFloor));

            TemporaryDays = temporaryDays;
            ExcavationDays = excavationDays;
            FoundationDays = foundationDays;
            CraneSetupDays = craneSetupDays;
            ColumnDaysPerFloor = columnDaysPerFloor;
            SlabDaysPerFloor = slabDaysPerFloor;
        }
    }

    /// <summary>
    /// 건물 공정: 토공 뒤에 건물 공사를 이어 붙인 공정표를 만들고 CPM으로 계산한다.
    ///
    /// 작업과 선후행 관계 (화살표 = "끝나야 시작"):
    ///
    ///   토공 ─────┐
    ///             ├→ 터파기 ─┬→ 기초 ──────────┐
    ///   가설공사 ─┘          └→ 타워크레인 설치 ─┴→ 1층 기둥 → 1층 슬래브 → 2층 기둥 → 2층 슬래브 → …
    ///
    ///   - 토공과 가설공사는 첫날 함께 시작한다. 둘 다 끝나야 터파기를 한다.
    ///   - 기초를 만드는 동안 타워크레인을 세운다. 둘 다 끝나야 1층 기둥을 올린다.
    ///   - 한 층은 기둥 → 슬래브 순서이고, 아래층 슬래브가 끝나야 위층 기둥을 올린다.
    ///
    /// 손계산 예: 2층, 토공 10일, 가설 5일, 터파기 2일, 기초 7일, 크레인 3일, 기둥 3일, 슬래브 5일
    ///   토공 0→10, 가설 0→5, 터파기 10→12, 기초 12→19, 크레인 12→15,
    ///   1층 기둥 19→22, 1층 슬래브 22→27, 2층 기둥 27→30, 2층 슬래브 30→35   → 전체 공기 35일
    ///   전체 여유: 가설 10 − 5 = 5일, 크레인 19 − 15 = 4일, 나머지는 0 (주공정)
    ///   검산: 주공정 합 = 10 + 2 + 7 + 2 × (3 + 5) = 35
    ///
    /// 화면에서는 모든 작업을 가장 빠른 시작일(ES)에 시작하는 것으로 보여 준다.
    ///
    /// 한계: 콘크리트가 굳는 기간, 마감·설비 공사, 인원·장비 제한, 휴일과 날씨는 다루지 않는다.
    /// </summary>
    public sealed class BuildingSchedule
    {
        private readonly List<CpmActivity> _activities = new List<CpmActivity>();
        private readonly List<BuildingWork> _works = new List<BuildingWork>();
        private readonly List<int> _floors = new List<int>();

        /// <summary>지상 층수</summary>
        public int Floors { get; }

        /// <summary>작업 목록 (번호 순서 = 공정표 순서)</summary>
        public IReadOnlyList<CpmActivity> Activities => _activities;

        /// <summary>CPM 계산 결과</summary>
        public CpmResult Result { get; }

        /// <summary>전체 공기 (일, 소수)</summary>
        public double ProjectDuration => Result.ProjectDuration;

        /// <summary>전체 공기를 올림한 날 수</summary>
        public int ProjectDays => (int)Math.Ceiling(Result.ProjectDuration - 1e-9);

        public BuildingSchedule(int floors, double earthworkDays, BuildingDurations durations)
        {
            if (floors < 1) throw new ArgumentOutOfRangeException(nameof(floors), "층수는 1 이상이어야 한다.");
            if (!(earthworkDays >= 0)) throw new ArgumentOutOfRangeException(nameof(earthworkDays));

            Floors = floors;

            int earthwork = Add("토공 (부지 정지)", BuildingWork.Earthwork, 0, earthworkDays);
            int temporary = Add("가설공사", BuildingWork.TemporaryWorks, 0, durations.TemporaryDays);
            int excavation = Add("터파기", BuildingWork.Excavation, 0, durations.ExcavationDays, earthwork, temporary);
            int foundation = Add("기초", BuildingWork.Foundation, 0, durations.FoundationDays, excavation);
            int crane = Add("타워크레인 설치", BuildingWork.CraneSetup, 0, durations.CraneSetupDays, excavation);

            int previousSlab = -1;
            for (int floor = 1; floor <= floors; floor++)
            {
                int columns = previousSlab < 0
                    ? Add(floor + "층 기둥", BuildingWork.Columns, floor, durations.ColumnDaysPerFloor, foundation, crane)
                    : Add(floor + "층 기둥", BuildingWork.Columns, floor, durations.ColumnDaysPerFloor, previousSlab);
                previousSlab = Add(floor + "층 슬래브", BuildingWork.Slab, floor, durations.SlabDaysPerFloor, columns);
            }

            Result = Cpm.Compute(_activities);
        }

        private int Add(string name, BuildingWork work, int floor, double days, params int[] predecessors)
        {
            _activities.Add(new CpmActivity(name, days, predecessors));
            _works.Add(work);
            _floors.Add(floor);
            return _activities.Count - 1;
        }

        /// <summary>작업의 종류</summary>
        public BuildingWork WorkOf(int activity)
        {
            return _works[activity];
        }

        /// <summary>작업이 속한 층 (기둥·슬래브만 1 이상, 나머지는 0)</summary>
        public int FloorOf(int activity)
        {
            return _floors[activity];
        }

        /// <summary>종류와 층으로 작업 번호를 찾는다. 없으면 −1.</summary>
        public int IndexOf(BuildingWork work, int floor = 0)
        {
            for (int i = 0; i < _works.Count; i++)
            {
                if (_works[i] == work && _floors[i] == floor) return i;
            }
            return -1;
        }

        /// <summary>
        /// day일째에 그 작업이 얼마나 끝났는가 (0 ~ 1). 가장 빠른 시작일에 시작해 일정한 속도로 한다고 본다.
        ///   진행 비율 = (day − ES) / 소요 일수   (0과 1 사이로 자른다)
        /// 소요 일수가 0인 작업은 ES가 되면 바로 1이다.
        /// </summary>
        public double Fraction(int activity, double day)
        {
            double start = Result.EarlyStart[activity];
            double duration = _activities[activity].Duration;

            if (!(duration > 0)) return day >= start ? 1.0 : 0.0;
            if (day <= start) return 0.0;
            if (day >= start + duration) return 1.0;
            return (day - start) / duration;
        }

        /// <summary>day일째에 그 작업을 하는 중인가 (시작일 이상, 종료일 미만).</summary>
        public bool IsActive(int activity, double day)
        {
            double start = Result.EarlyStart[activity];
            return day >= start && day < Result.EarlyFinish[activity];
        }

        /// <summary>
        /// 터파기 일수 = 터파기 부피 / 하루 굴착량 을 올림한 값 (최소 1일).
        /// 터파기 부피는 건물 바닥 넓이 × 기초 두께로 본다 (수직으로 파고 여유 폭은 없다고 가정).
        /// 손계산: 480 m³, 하루 622.08 m³ → 0.77 → 1일.   2000 m³ → 3.22 → 4일.
        /// </summary>
        public static double ExcavationDays(double excavationVolume, double dailyVolume)
        {
            if (!(excavationVolume >= 0)) throw new ArgumentOutOfRangeException(nameof(excavationVolume));
            if (!(dailyVolume > 0)) throw new ArgumentOutOfRangeException(nameof(dailyVolume));

            return Math.Max(1.0, Math.Ceiling(excavationVolume / dailyVolume - 1e-9));
        }
    }
}
