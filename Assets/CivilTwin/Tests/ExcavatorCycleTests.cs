using CivilTwin.Core;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>굴착기 한 사이클 동작(ExcavatorCycle)을 손계산과 비교한다. Cm 19초, 선회 90°.</summary>
    public class ExcavatorCycleTests
    {
        private const double Cm = 19.0, Swing = 90.0;

        private static void AssertPose(ExcavatorPose expected, double swing, ExcavatorPose actual)
        {
            Assert.AreEqual(swing, actual.Swing, 1e-9, "선회");
            Assert.AreEqual(expected.BoomRaise, actual.BoomRaise, 1e-9, "붐");
            Assert.AreEqual(expected.ArmOut, actual.ArmOut, 1e-9, "암");
            Assert.AreEqual(expected.BucketOpen, actual.BucketOpen, 1e-9, "버킷");
        }

        [Test]
        public void 단계_경계의_자세()
        {
            AssertPose(ExcavatorCycle.DigStart, 0.0, ExcavatorCycle.PoseAt(0.0, Cm, Swing));     // 0초: A
            AssertPose(ExcavatorCycle.DigFinish, 0.0, ExcavatorCycle.PoseAt(5.7, Cm, Swing));    // 0.30 × 19 = 5.7초: B
            AssertPose(ExcavatorCycle.Lifted, 90.0, ExcavatorCycle.PoseAt(10.45, Cm, Swing));    // 0.55 × 19: C, 트럭 쪽
            AssertPose(ExcavatorCycle.Dumping, 90.0, ExcavatorCycle.PoseAt(13.3, Cm, Swing));    // 0.70 × 19: D
            AssertPose(ExcavatorCycle.DigStart, 0.0, ExcavatorCycle.PoseAt(19.0, Cm, Swing));    // 한 바퀴 → 다시 A
        }

        [Test]
        public void 적재_선회_한가운데는_45도()
        {
            // 0.425 × 19 = 8.075초: 적재 선회(0.30 ~ 0.55)의 절반 → smoothstep(0.5) = 0.5 → 45°, 붐은 0 → 25의 절반 12.5
            ExcavatorPose p = ExcavatorCycle.PoseAt(8.075, Cm, Swing);
            Assert.AreEqual(45.0, p.Swing, 1e-9);
            Assert.AreEqual(12.5, p.BoomRaise, 1e-9);
            Assert.AreEqual("적재 선회", ExcavatorCycle.PhaseName(8.075, Cm));
        }

        [Test]
        public void 단계_이름()
        {
            Assert.AreEqual("굴착", ExcavatorCycle.PhaseName(1.0, Cm));
            Assert.AreEqual("덤프", ExcavatorCycle.PhaseName(12.0, Cm));
            Assert.AreEqual("복귀 선회", ExcavatorCycle.PhaseName(15.0, Cm));
            Assert.AreEqual("굴착", ExcavatorCycle.PhaseName(19.0 + 1.0, Cm));   // 다음 사이클
        }

        [Test]
        public void 부드러운_보간()
        {
            // 3s² − 2s³: s = 0.25 → 0.1875 − 0.03125 = 0.15625
            Assert.AreEqual(0.0, ExcavatorCycle.Smooth(0.0), 1e-12);
            Assert.AreEqual(0.15625, ExcavatorCycle.Smooth(0.25), 1e-12);
            Assert.AreEqual(0.5, ExcavatorCycle.Smooth(0.5), 1e-12);
            Assert.AreEqual(1.0, ExcavatorCycle.Smooth(1.0), 1e-12);
        }
    }
}
