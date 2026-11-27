using System;
using CivilTwin.Core;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>
    /// CPM 계산을 손계산과 비교한다.
    ///
    /// 시험 공정: A(3일), B(4일), C(2일, 선행 A), D(5일, 선행 A·B), E(3일, 선행 C·D)
    ///   전진: A 0→3, B 0→4, C 3→5, D 4→9, E 9→12   → 전체 공기 12일
    ///   후진: E 9→12, D 4→9, C 7→9, A 1→4, B 0→4
    ///   전체 여유: A 1, B 0, C 4, D 0, E 0   → 주공정선 B → D → E
    /// </summary>
    public class CpmTests
    {
        private const int A = 0, B = 1, C = 2, D = 3, E = 4;

        private static CpmResult TestNetwork()
        {
            return Cpm.Compute(new[]
            {
                new CpmActivity("A", 3.0),
                new CpmActivity("B", 4.0),
                new CpmActivity("C", 2.0, A),
                new CpmActivity("D", 5.0, A, B),
                new CpmActivity("E", 3.0, C, D),
            });
        }

        [Test]
        public void 전진_계산이_손계산과_같다()
        {
            CpmResult r = TestNetwork();

            CollectionAssert.AreEqual(new[] { 0.0, 0.0, 3.0, 4.0, 9.0 }, r.EarlyStart);
            CollectionAssert.AreEqual(new[] { 3.0, 4.0, 5.0, 9.0, 12.0 }, r.EarlyFinish);
            Assert.AreEqual(12.0, r.ProjectDuration, 1e-9);
        }

        [Test]
        public void 후진_계산이_손계산과_같다()
        {
            CpmResult r = TestNetwork();

            CollectionAssert.AreEqual(new[] { 1.0, 0.0, 7.0, 4.0, 9.0 }, r.LateStart);
            CollectionAssert.AreEqual(new[] { 4.0, 4.0, 9.0, 9.0, 12.0 }, r.LateFinish);
        }

        [Test]
        public void 여유와_주공정선이_손계산과_같다()
        {
            CpmResult r = TestNetwork();

            CollectionAssert.AreEqual(new[] { 1.0, 0.0, 4.0, 0.0, 0.0 }, r.TotalFloat);
            CollectionAssert.AreEqual(new[] { false, true, false, true, true }, r.Critical);

            // 자유 여유: A는 C가 3일에 시작하므로 0, C는 E가 9일에 시작하므로 9 − 5 = 4
            CollectionAssert.AreEqual(new[] { 0.0, 0.0, 4.0, 0.0, 0.0 }, r.FreeFloat);
        }

        [Test]
        public void 한_줄로_이어진_공정은_모두_주공정이고_공기는_합이다()
        {
            CpmResult r = Cpm.Compute(new[]
            {
                new CpmActivity("가", 2.0),
                new CpmActivity("나", 3.5, 0),
                new CpmActivity("다", 4.0, 1),
            });

            Assert.AreEqual(9.5, r.ProjectDuration, 1e-9);
            CollectionAssert.AreEqual(new[] { true, true, true }, r.Critical);
        }

        [Test]
        public void 여유가_있는_작업을_여유만큼_늘려도_공기는_그대로다()
        {
            // C의 전체 여유는 4일 → C를 2일에서 6일로 늘려도 12일, 7일이면 13일
            CpmResult six = Cpm.Compute(new[]
            {
                new CpmActivity("A", 3.0), new CpmActivity("B", 4.0),
                new CpmActivity("C", 6.0, A), new CpmActivity("D", 5.0, A, B), new CpmActivity("E", 3.0, C, D),
            });
            CpmResult seven = Cpm.Compute(new[]
            {
                new CpmActivity("A", 3.0), new CpmActivity("B", 4.0),
                new CpmActivity("C", 7.0, A), new CpmActivity("D", 5.0, A, B), new CpmActivity("E", 3.0, C, D),
            });

            Assert.AreEqual(12.0, six.ProjectDuration, 1e-9);
            Assert.AreEqual(13.0, seven.ProjectDuration, 1e-9);
            Assert.IsTrue(seven.Critical[C]);    // 이제 A → C → E 가 주공정선
            Assert.IsFalse(seven.Critical[D]);
        }

        [Test]
        public void 소요_일수가_0인_작업도_계산된다()
        {
            CpmResult r = Cpm.Compute(new[]
            {
                new CpmActivity("시작", 0.0),
                new CpmActivity("작업", 5.0, 0),
            });

            Assert.AreEqual(5.0, r.ProjectDuration, 1e-9);
            Assert.AreEqual(0.0, r.EarlyFinish[0], 1e-9);
            Assert.IsTrue(r.Critical[0]);
        }

        [Test]
        public void 작업이_없으면_공기는_0이다()
        {
            Assert.AreEqual(0.0, Cpm.Compute(new CpmActivity[0]).ProjectDuration, 1e-9);
        }

        [Test]
        public void 잘못된_입력은_거부한다()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CpmActivity("음수", -1.0));

            // 선행 작업이 자기 자신이거나 뒤 번호
            Assert.Throws<ArgumentException>(() => Cpm.Compute(new[] { new CpmActivity("가", 1.0, 0) }));
            Assert.Throws<ArgumentException>(() => Cpm.Compute(new[]
            {
                new CpmActivity("가", 1.0, 1),
                new CpmActivity("나", 1.0),
            }));
        }
    }
}
