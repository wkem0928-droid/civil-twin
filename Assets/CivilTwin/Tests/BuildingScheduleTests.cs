using System;
using CivilTwin.Core;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>
    /// 건물 공정(BuildingSchedule)을 손계산과 비교한다.
    ///
    /// 시험 공정: 2층, 토공 10일, 가설 5일, 터파기 2일, 기초 7일, 크레인 3일, 기둥 3일, 슬래브 5일
    ///   토공 0→10, 가설 0→5, 터파기 10→12, 기초 12→19, 크레인 12→15,
    ///   1층 기둥 19→22, 1층 슬래브 22→27, 2층 기둥 27→30, 2층 슬래브 30→35   → 35일
    /// </summary>
    public class BuildingScheduleTests
    {
        private static BuildingDurations TestDurations()
        {
            return new BuildingDurations(5.0, 2.0, 7.0, 3.0, 3.0, 5.0);
        }

        private static BuildingSchedule TestSchedule()
        {
            return new BuildingSchedule(2, 10.0, TestDurations());
        }

        [Test]
        public void 작업_개수는_5개에_층마다_2개다()
        {
            Assert.AreEqual(9, TestSchedule().Activities.Count);                                 // 5 + 2 × 2
            Assert.AreEqual(25, new BuildingSchedule(10, 10.0, TestDurations()).Activities.Count);
        }

        [Test]
        public void 시작일과_종료일이_손계산과_같다()
        {
            BuildingSchedule s = TestSchedule();

            CollectionAssert.AreEqual(
                new[] { 0.0, 0.0, 10.0, 12.0, 12.0, 19.0, 22.0, 27.0, 30.0 }, s.Result.EarlyStart);
            CollectionAssert.AreEqual(
                new[] { 10.0, 5.0, 12.0, 19.0, 15.0, 22.0, 27.0, 30.0, 35.0 }, s.Result.EarlyFinish);
        }

        [Test]
        public void 전체_공기가_손계산과_같다()
        {
            BuildingSchedule s = TestSchedule();

            // 주공정 합: 10 + 2 + 7 + 2 × (3 + 5) = 35
            Assert.AreEqual(35.0, s.ProjectDuration, 1e-9);
            Assert.AreEqual(35, s.ProjectDays);
        }

        [Test]
        public void 가설공사와_크레인_설치에만_여유가_있다()
        {
            BuildingSchedule s = TestSchedule();
            int temporary = s.IndexOf(BuildingWork.TemporaryWorks);
            int crane = s.IndexOf(BuildingWork.CraneSetup);

            Assert.AreEqual(5.0, s.Result.TotalFloat[temporary], 1e-9);   // 10 − 5
            Assert.AreEqual(4.0, s.Result.TotalFloat[crane], 1e-9);       // 19 − 15

            for (int i = 0; i < s.Activities.Count; i++)
            {
                bool expected = i != temporary && i != crane;
                Assert.AreEqual(expected, s.Result.Critical[i], s.Activities[i].Name);
            }
        }

        [Test]
        public void 토공이_가설공사보다_짧으면_주공정선이_가설공사로_바뀐다()
        {
            // 토공 3일, 가설 5일 → 터파기는 5일에 시작. 토공의 여유는 2일.
            var s = new BuildingSchedule(2, 3.0, TestDurations());

            Assert.AreEqual(5.0, s.Result.EarlyStart[s.IndexOf(BuildingWork.Excavation)], 1e-9);
            Assert.IsTrue(s.Result.Critical[s.IndexOf(BuildingWork.TemporaryWorks)]);
            Assert.IsFalse(s.Result.Critical[s.IndexOf(BuildingWork.Earthwork)]);
            Assert.AreEqual(2.0, s.Result.TotalFloat[s.IndexOf(BuildingWork.Earthwork)], 1e-9);
            Assert.AreEqual(30.0, s.ProjectDuration, 1e-9);   // 5 + 2 + 7 + 16
        }

        [Test]
        public void 한_층을_더하면_공기가_기둥과_슬래브_일수만큼_는다()
        {
            var three = new BuildingSchedule(3, 10.0, TestDurations());
            Assert.AreEqual(43.0, three.ProjectDuration, 1e-9);   // 35 + 3 + 5
        }

        [Test]
        public void 작업의_종류와_층을_찾을_수_있다()
        {
            BuildingSchedule s = TestSchedule();

            int slab2 = s.IndexOf(BuildingWork.Slab, 2);
            Assert.AreEqual(8, slab2);
            Assert.AreEqual("2층 슬래브", s.Activities[slab2].Name);
            Assert.AreEqual(BuildingWork.Slab, s.WorkOf(slab2));
            Assert.AreEqual(2, s.FloorOf(slab2));
            Assert.AreEqual(-1, s.IndexOf(BuildingWork.Columns, 3));   // 3층은 없다
        }

        [Test]
        public void 진행_비율이_손계산과_같다()
        {
            BuildingSchedule s = TestSchedule();
            int foundation = s.IndexOf(BuildingWork.Foundation);   // 12 → 19 (7일)

            Assert.AreEqual(0.0, s.Fraction(foundation, 5.0), 1e-9);
            Assert.AreEqual(0.0, s.Fraction(foundation, 12.0), 1e-9);
            Assert.AreEqual(0.5, s.Fraction(foundation, 15.5), 1e-9);   // (15.5 − 12) / 7
            Assert.AreEqual(1.0, s.Fraction(foundation, 19.0), 1e-9);
            Assert.AreEqual(1.0, s.Fraction(foundation, 30.0), 1e-9);

            Assert.IsFalse(s.IsActive(foundation, 11.9));
            Assert.IsTrue(s.IsActive(foundation, 12.0));
            Assert.IsTrue(s.IsActive(foundation, 18.9));
            Assert.IsFalse(s.IsActive(foundation, 19.0));
        }

        [Test]
        public void 토공이_없어도_계산된다()
        {
            var s = new BuildingSchedule(1, 0.0, TestDurations());

            Assert.AreEqual(1.0, s.Fraction(s.IndexOf(BuildingWork.Earthwork), 0.0), 1e-9);
            Assert.AreEqual(22.0, s.ProjectDuration, 1e-9);   // 가설 5 + 2 + 7 + 3 + 5
        }

        [Test]
        public void 터파기_일수가_손계산과_같다()
        {
            Assert.AreEqual(1.0, BuildingSchedule.ExcavationDays(480.0, 622.08), 1e-9);    // 0.77 → 1
            Assert.AreEqual(4.0, BuildingSchedule.ExcavationDays(2000.0, 622.08), 1e-9);   // 3.22 → 4
            Assert.AreEqual(2.0, BuildingSchedule.ExcavationDays(2000.0, 1000.0), 1e-9);   // 딱 2일
            Assert.AreEqual(1.0, BuildingSchedule.ExcavationDays(0.0, 1000.0), 1e-9);      // 최소 1일
        }

        [Test]
        public void 잘못된_입력은_거부한다()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BuildingSchedule(0, 10.0, TestDurations()));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BuildingSchedule(2, -1.0, TestDurations()));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BuildingDurations(5.0, 2.0, -7.0, 3.0, 3.0, 5.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => BuildingSchedule.ExcavationDays(100.0, 0.0));
        }
    }
}
