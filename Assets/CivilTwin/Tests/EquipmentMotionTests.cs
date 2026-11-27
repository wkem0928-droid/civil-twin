using System;
using CivilTwin.Core;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>장비 움직임 계산(EquipmentMotion)과 단계 설명 자료(ConstructionGuide)를 확인한다.</summary>
    public class EquipmentMotionTests
    {
        [Test]
        public void 왕복_비율이_손계산과_같다()
        {
            // 주기 20초: 0초 0, 5초 0.5, 10초 1, 15초 0.5, 20초 0 (다시 처음)
            Assert.AreEqual(0.0, EquipmentMotion.BackAndForth(0.0, 20.0), 1e-9);
            Assert.AreEqual(0.5, EquipmentMotion.BackAndForth(5.0, 20.0), 1e-9);
            Assert.AreEqual(1.0, EquipmentMotion.BackAndForth(10.0, 20.0), 1e-9);
            Assert.AreEqual(0.5, EquipmentMotion.BackAndForth(15.0, 20.0), 1e-9);
            Assert.AreEqual(0.0, EquipmentMotion.BackAndForth(20.0, 20.0), 1e-9);
            Assert.AreEqual(0.3, EquipmentMotion.BackAndForth(43.0, 20.0), 1e-9);   // 43초 = 2주기 + 3초
        }

        [Test]
        public void 굴착기_선회각이_손계산과_같다()
        {
            // 사이클 20초, 선회각 90°
            Assert.AreEqual(45.0, EquipmentMotion.ExcavatorSwingDegrees(5.0, 20.0, 90.0), 1e-9);
            Assert.AreEqual(90.0, EquipmentMotion.ExcavatorSwingDegrees(10.0, 20.0, 90.0), 1e-9);
            Assert.AreEqual(45.0, EquipmentMotion.ExcavatorSwingDegrees(15.0, 20.0, 90.0), 1e-9);
            Assert.AreEqual(45.0, EquipmentMotion.ExcavatorSwingDegrees(25.0, 20.0, 90.0), 1e-9);
        }

        [Test]
        public void 덤프트럭_왕복_위치가_손계산과_같다()
        {
            // 편도 40 m, 속도 4 m/초 → 주기 20초
            Assert.AreEqual(20.0, EquipmentMotion.ShuttleDistance(5.0, 40.0, 4.0), 1e-9);
            Assert.AreEqual(40.0, EquipmentMotion.ShuttleDistance(10.0, 40.0, 4.0), 1e-9);
            Assert.AreEqual(30.0, EquipmentMotion.ShuttleDistance(12.5, 40.0, 4.0), 1e-9);

            Assert.IsFalse(EquipmentMotion.ShuttleIsReturning(5.0, 40.0, 4.0));
            Assert.IsTrue(EquipmentMotion.ShuttleIsReturning(12.5, 40.0, 4.0));
        }

        [Test]
        public void 움직임은_범위를_벗어나지_않는다()
        {
            for (int i = 0; i <= 1000; i++)
            {
                double t = i * 0.137;
                double d = EquipmentMotion.ShuttleDistance(t, 40.0, 4.0);
                Assert.GreaterOrEqual(d, 0.0);
                Assert.LessOrEqual(d, 40.0);
            }
        }

        [Test]
        public void 잘못된_입력은_거부한다()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => EquipmentMotion.BackAndForth(1.0, 0.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => EquipmentMotion.ShuttleDistance(1.0, 0.0, 4.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => EquipmentMotion.ShuttleDistance(1.0, 40.0, 0.0));
        }

        [Test]
        public void 모든_작업에_설명이_있다()
        {
            foreach (BuildingWork work in Enum.GetValues(typeof(BuildingWork)))
            {
                WorkGuide guide = ConstructionGuide.For(work);
                Assert.IsNotEmpty(guide.Title, work.ToString());
                Assert.IsNotEmpty(guide.What, work.ToString());
                Assert.IsNotEmpty(guide.Why, work.ToString());
            }
        }

        [Test]
        public void 모든_장비에_이름과_역할이_있다()
        {
            foreach (EquipmentKind kind in Enum.GetValues(typeof(EquipmentKind)))
            {
                Assert.IsNotEmpty(ConstructionGuide.EquipmentName(kind));
                Assert.IsNotEmpty(ConstructionGuide.EquipmentRole(kind));
            }
        }

        [Test]
        public void 단계별_장비가_계획과_같다()
        {
            CollectionAssert.AreEqual(
                new[] { EquipmentKind.Excavator, EquipmentKind.DumpTruck, EquipmentKind.Bulldozer, EquipmentKind.Roller },
                ConstructionGuide.For(BuildingWork.Earthwork).Equipment);
            CollectionAssert.AreEqual(
                new[] { EquipmentKind.Excavator, EquipmentKind.DumpTruck },
                ConstructionGuide.For(BuildingWork.Excavation).Equipment);
            CollectionAssert.AreEqual(
                new[] { EquipmentKind.MixerTruck, EquipmentKind.PumpTruck },
                ConstructionGuide.For(BuildingWork.Foundation).Equipment);
            CollectionAssert.Contains(ConstructionGuide.For(BuildingWork.Slab).Equipment, EquipmentKind.TowerCrane);
            CollectionAssert.IsEmpty(ConstructionGuide.For(BuildingWork.TemporaryWorks).Equipment);
        }
    }
}
