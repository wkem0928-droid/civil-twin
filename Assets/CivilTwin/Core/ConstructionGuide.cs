using System;
using System.Collections.Generic;

namespace CivilTwin.Core
{
    /// <summary>현장에 놓는 장비의 종류.</summary>
    public enum EquipmentKind
    {
        Excavator,    // 굴착기
        DumpTruck,    // 덤프트럭
        Bulldozer,    // 불도저
        Roller,       // 롤러
        MixerTruck,   // 레미콘 트럭
        PumpTruck,    // 펌프카
        TowerCrane,   // 타워크레인
    }

    /// <summary>한 작업 단계의 설명: 제목, 쓰는 장비, "지금 무엇을 왜 하는가".</summary>
    public sealed class WorkGuide
    {
        public string Title { get; }
        public IReadOnlyList<EquipmentKind> Equipment { get; }

        /// <summary>무엇을 하는가</summary>
        public string What { get; }

        /// <summary>왜 하는가</summary>
        public string Why { get; }

        public WorkGuide(string title, EquipmentKind[] equipment, string what, string why)
        {
            Title = title;
            Equipment = equipment;
            What = what;
            Why = why;
        }
    }

    /// <summary>
    /// 현장 학습용 설명 자료: 작업 단계마다 쓰는 장비와 하는 일.
    ///
    /// 주의: 여기의 글은 일반적인 시공 순서를 쉽게 풀어 쓴 것이다.
    /// 시방서나 교재로 한 문장씩 확인하지는 않았다 (출처 확인 전).
    /// </summary>
    public static class ConstructionGuide
    {
        /// <summary>장비의 우리말 이름</summary>
        public static string EquipmentName(EquipmentKind kind)
        {
            switch (kind)
            {
                case EquipmentKind.Excavator: return "굴착기";
                case EquipmentKind.DumpTruck: return "덤프트럭";
                case EquipmentKind.Bulldozer: return "불도저";
                case EquipmentKind.Roller: return "롤러";
                case EquipmentKind.MixerTruck: return "레미콘 트럭";
                case EquipmentKind.PumpTruck: return "펌프카";
                case EquipmentKind.TowerCrane: return "타워크레인";
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        /// <summary>장비가 하는 일 한 줄</summary>
        public static string EquipmentRole(EquipmentKind kind)
        {
            switch (kind)
            {
                case EquipmentKind.Excavator: return "땅을 파서 흙을 덤프트럭에 싣는다";
                case EquipmentKind.DumpTruck: return "흙을 실어 나른다";
                case EquipmentKind.Bulldozer: return "부어 놓은 흙을 밀어서 편다";
                case EquipmentKind.Roller: return "편 흙을 눌러 다진다";
                case EquipmentKind.MixerTruck: return "공장에서 만든 콘크리트를 굳지 않게 돌리며 실어 온다";
                case EquipmentKind.PumpTruck: return "콘크리트를 관으로 밀어 부을 자리까지 보낸다";
                case EquipmentKind.TowerCrane: return "철근·거푸집 같은 자재를 위층으로 들어 올린다";
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        /// <summary>작업 종류에 맞는 설명</summary>
        public static WorkGuide For(BuildingWork work)
        {
            switch (work)
            {
                case BuildingWork.Earthwork:
                    return new WorkGuide(
                        "토공 (부지 정지)",
                        new[] { EquipmentKind.Excavator, EquipmentKind.DumpTruck, EquipmentKind.Bulldozer, EquipmentKind.Roller },
                        "높은 곳의 흙을 굴착기로 깎아 덤프트럭에 싣고, 낮은 곳에 부어 불도저로 펴고 롤러로 다진다.",
                        "건물을 지을 평평하고 단단한 땅(부지)을 만들기 위해서다.");

                case BuildingWork.TemporaryWorks:
                    return new WorkGuide(
                        "가설공사",
                        new EquipmentKind[0],
                        "현장 둘레에 울타리를 치고 사무실과 자재 둘 곳을 마련한다.",
                        "본공사를 안전하게, 끊기지 않게 하기 위한 준비다. 공사가 끝나면 치운다.");

                case BuildingWork.Excavation:
                    return new WorkGuide(
                        "터파기",
                        new[] { EquipmentKind.Excavator, EquipmentKind.DumpTruck },
                        "기초가 놓일 깊이까지 굴착기로 땅을 파고, 파낸 흙은 덤프트럭으로 실어 낸다.",
                        "기초를 땅속의 단단한 자리에 앉히기 위해서다.");

                case BuildingWork.Foundation:
                    return new WorkGuide(
                        "기초",
                        new[] { EquipmentKind.MixerTruck, EquipmentKind.PumpTruck },
                        "철근을 엮고 거푸집(틀)을 세운 뒤, 레미콘 트럭이 실어 온 콘크리트를 펌프카로 부어 넣는다.",
                        "기초는 건물 전체의 무게를 땅에 전달한다.");

                case BuildingWork.CraneSetup:
                    return new WorkGuide(
                        "타워크레인 설치",
                        new[] { EquipmentKind.TowerCrane },
                        "건물 옆에 타워크레인을 세운다.",
                        "위층 공사에 쓸 철근·거푸집 같은 무거운 자재를 들어 올리려면 필요하다.");

                case BuildingWork.Columns:
                    return new WorkGuide(
                        "기둥",
                        new[] { EquipmentKind.TowerCrane, EquipmentKind.MixerTruck, EquipmentKind.PumpTruck },
                        "기둥 철근을 세우고 거푸집을 댄 뒤 콘크리트를 부어 넣는다.",
                        "기둥은 위층의 무게를 받아 아래로 전달한다.");

                case BuildingWork.Slab:
                    return new WorkGuide(
                        "슬래브 (바닥판)",
                        new[] { EquipmentKind.TowerCrane, EquipmentKind.MixerTruck, EquipmentKind.PumpTruck },
                        "거푸집과 동바리(받침 기둥)를 놓고 철근을 깐 뒤 콘크리트를 부어 바닥판을 만든다.",
                        "슬래브는 사람과 물건의 무게를 받아 기둥으로 넘긴다. 굳을 때까지는 동바리가 받친다.");

                default:
                    throw new ArgumentOutOfRangeException(nameof(work));
            }
        }
    }
}
