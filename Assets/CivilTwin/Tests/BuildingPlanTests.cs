using System;
using CivilTwin.Core;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>
    /// 건물 계획(BuildingPlan)의 콘크리트 물량을 손계산과 비교한다.
    ///
    /// 시험 건물: 동서 30 m × 남북 20 m, 5층, 층고 3.5 m, 칸 수 5 × 4,
    ///            기둥 0.5 m, 슬래브 0.25 m, 기초 0.8 m
    /// </summary>
    public class BuildingPlanTests
    {
        private static BuildingPlan TestBuilding()
        {
            return new BuildingPlan(30.0, 20.0, 5, 3.5, 5, 4, 0.5, 0.25, 0.8);
        }

        [Test]
        public void 넓이와_높이가_손계산과_같다()
        {
            BuildingPlan b = TestBuilding();

            Assert.AreEqual(600.0, b.FloorArea, 1e-9);        // 30 × 20
            Assert.AreEqual(3000.0, b.GrossFloorArea, 1e-9);  // 600 × 5층
            Assert.AreEqual(17.5, b.Height, 1e-9);            // 5 × 3.5
        }

        [Test]
        public void 기둥_개수와_간격이_손계산과_같다()
        {
            BuildingPlan b = TestBuilding();

            Assert.AreEqual(30, b.ColumnsPerFloor);           // (5 + 1) × (4 + 1)
            Assert.AreEqual(6.0, b.SpanEastWest, 1e-9);       // 30 / 5
            Assert.AreEqual(5.0, b.SpanNorthSouth, 1e-9);     // 20 / 4
            Assert.AreEqual(3.25, b.ColumnClearHeight, 1e-9); // 3.5 − 0.25
        }

        [Test]
        public void 기초_부피가_손계산과_같다()
        {
            // 600 × 0.8 = 480
            Assert.AreEqual(480.0, TestBuilding().FoundationVolume, 1e-9);
        }

        [Test]
        public void 슬래브_부피가_손계산과_같다()
        {
            BuildingPlan b = TestBuilding();

            Assert.AreEqual(150.0, b.SlabVolumePerFloor, 1e-9);  // 600 × 0.25
            Assert.AreEqual(750.0, b.TotalSlabVolume, 1e-9);     // 150 × 5층
        }

        [Test]
        public void 기둥_부피가_손계산과_같다()
        {
            BuildingPlan b = TestBuilding();

            // 30개 × 0.5 × 0.5 × 3.25 = 24.375
            Assert.AreEqual(24.375, b.ColumnVolumePerFloor, 1e-9);
            Assert.AreEqual(121.875, b.TotalColumnVolume, 1e-9);  // 24.375 × 5층
        }

        [Test]
        public void 전체_부피는_기초_기둥_슬래브의_합이다()
        {
            // 480 + 121.875 + 750 = 1351.875
            Assert.AreEqual(1351.875, TestBuilding().TotalConcreteVolume, 1e-9);
        }

        [Test]
        public void 한_층_한_칸짜리_건물도_계산된다()
        {
            // 10 × 10 m, 1층, 층고 3 m, 칸 1 × 1 → 기둥 4개, 기둥 0.4 m, 슬래브 0.2 m, 기초 0.5 m
            var b = new BuildingPlan(10.0, 10.0, 1, 3.0, 1, 1, 0.4, 0.2, 0.5);

            Assert.AreEqual(4, b.ColumnsPerFloor);
            Assert.AreEqual(50.0, b.FoundationVolume, 1e-9);        // 100 × 0.5
            Assert.AreEqual(20.0, b.TotalSlabVolume, 1e-9);         // 100 × 0.2
            Assert.AreEqual(1.792, b.TotalColumnVolume, 1e-9);      // 4 × 0.16 × 2.8
            Assert.AreEqual(71.792, b.TotalConcreteVolume, 1e-9);
        }

        [Test]
        public void 층수를_두_배로_하면_기초는_그대로고_기둥과_슬래브만_두_배다()
        {
            BuildingPlan five = TestBuilding();
            var ten = new BuildingPlan(30.0, 20.0, 10, 3.5, 5, 4, 0.5, 0.25, 0.8);

            Assert.AreEqual(five.FoundationVolume, ten.FoundationVolume, 1e-9);
            Assert.AreEqual(2.0 * five.TotalSlabVolume, ten.TotalSlabVolume, 1e-9);
            Assert.AreEqual(2.0 * five.TotalColumnVolume, ten.TotalColumnVolume, 1e-9);
            Assert.AreEqual(2223.75, ten.TotalConcreteVolume, 1e-9);  // 480 + 243.75 + 1500
        }

        [Test]
        public void 잘못된_입력은_거부한다()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BuildingPlan(0.0, 20.0, 5, 3.5, 5, 4, 0.5, 0.25, 0.8));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BuildingPlan(30.0, -1.0, 5, 3.5, 5, 4, 0.5, 0.25, 0.8));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BuildingPlan(30.0, 20.0, 0, 3.5, 5, 4, 0.5, 0.25, 0.8));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BuildingPlan(30.0, 20.0, 5, 3.5, 0, 4, 0.5, 0.25, 0.8));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BuildingPlan(30.0, 20.0, 5, 3.5, 5, 0, 0.5, 0.25, 0.8));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BuildingPlan(30.0, 20.0, 5, 3.5, 5, 4, 0.0, 0.25, 0.8));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BuildingPlan(30.0, 20.0, 5, 3.5, 5, 4, 0.5, 0.0, 0.8));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BuildingPlan(30.0, 20.0, 5, 3.5, 5, 4, 0.5, 0.25, 0.0));

            // 층고가 슬래브 두께보다 작으면 기둥 높이가 음수가 된다
            Assert.Throws<ArgumentOutOfRangeException>(() => new BuildingPlan(30.0, 20.0, 5, 0.2, 5, 4, 0.5, 0.25, 0.8));
        }

        [Test]
        public void 한_층_일수를_표준품셈으로_검산한다()
        {
            // 6-3-1 거푸집 (조당 일): 기둥 복잡 30 m², 슬래브 보통 45 m² / 6-2-3 철근 건축: Type-Ⅰ 3.4 t, Type-Ⅱ 3.0 t / 6-1-4 펌프차 140 m³
            // 기둥 30개 × 4 × 0.5 × 3.25 = 195 m², 철근 24.375 × 0.2 = 4.875 t
            //   → 4.875 ÷ (3.4 × 2) + 195 ÷ (30 × 4) + 올림(24.375 ÷ 140) = 0.717 + 1.625 + 1 = 3.342일
            // 슬래브 600 + 100 × 0.25 = 625 m², 철근 150 × 0.1 = 15 t
            //   → 15 ÷ (3.0 × 2) + 625 ÷ (45 × 4) + 올림(150 ÷ 140) = 2.5 + 3.472 + 2 = 7.972일
            var c = new BuildingCycleCheck(TestBuilding());
            Assert.AreEqual(195.0, c.ColumnFormArea, 1e-9);
            Assert.AreEqual(625.0, c.SlabFormArea, 1e-9);
            Assert.AreEqual(4.875, c.ColumnRebarTons, 1e-9);
            Assert.AreEqual(15.0, c.SlabRebarTons, 1e-9);
            Assert.AreEqual(4.875 / 6.8 + 1.625 + 1.0, c.ColumnDays, 1e-9);
            Assert.AreEqual(2.5 + 625.0 / 180.0 + 2.0, c.SlabDays, 1e-9);
            Assert.AreEqual(4 * 7 + 2 * 8, c.Workers);
            // 매트기초 480 m³: 철근 48 t ÷ 6.8 = 7.059 + 옆면 100 × 0.8 = 80 m² ÷ (50 × 4) = 0.4 + 올림(480 ÷ 196 = 2.45) = 3 → 10.459일
            Assert.AreEqual(48.0, c.FoundationRebarTons, 1e-9);
            Assert.AreEqual(48.0 / 6.8 + 0.4 + 3.0, c.FoundationDays, 1e-9);
            // 작업조를 두 배로 하면 철근·거푸집은 반, 타설 일수는 그대로
            var d = new BuildingCycleCheck(TestBuilding(), 8, 4);
            Assert.AreEqual(4.875 / 13.6 + 195.0 / 240.0 + 1.0, d.ColumnDays, 1e-9);
            Assert.AreEqual(2.0, BuildingCycleCheck.PourDays(150.0));
            Assert.AreEqual(1.0, BuildingCycleCheck.PourDays(140.0));
        }
    }
}
