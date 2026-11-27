using System;
using CivilTwin.Core;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>현장 목록: 파일 이름 → 회사·현장 이름, 보여 줄 순서, 다음/이전 번호.</summary>
    public class SiteCatalogTests
    {
        [Test]
        public void 소양강댐_파일은_현대건설_소양강댐으로_표시한다()
        {
            SiteInfo site = SiteCatalog.Describe("hyundai_soyanggang.asc");

            Assert.AreEqual("현대건설", site.Company);
            Assert.AreEqual("소양강댐", site.SiteName);
            Assert.AreEqual("현대건설 · 소양강댐", site.DisplayName);
        }

        [Test]
        public void 파일_이름의_대소문자는_구분하지_않는다()
        {
            // Windows는 파일 이름의 대소문자를 구분하지 않는다.
            Assert.AreEqual("현대건설 · 소양강댐", SiteCatalog.Describe("Hyundai_Soyanggang.ASC").DisplayName);
        }

        [Test]
        public void 목록에_없는_파일은_확장자를_뗀_파일_이름으로_표시한다()
        {
            SiteInfo site = SiteCatalog.Describe("my_test_area.asc");

            Assert.AreEqual("", site.Company);
            Assert.AreEqual("my_test_area", site.DisplayName);
        }

        [Test]
        public void 목록에_있는_현장이_먼저_오고_나머지는_이름순이다()
        {
            SiteInfo[] sites = SiteCatalog.Arrange(new[] { "zzz.asc", "hyundai_soyanggang.asc", "aaa.asc" });

            Assert.AreEqual(3, sites.Length);
            Assert.AreEqual("hyundai_soyanggang.asc", sites[0].FileName);
            Assert.AreEqual("aaa.asc", sites[1].FileName);
            Assert.AreEqual("zzz.asc", sites[2].FileName);
        }

        [Test]
        public void 파일이_없으면_빈_목록이다()
        {
            Assert.AreEqual(0, SiteCatalog.Arrange(new string[0]).Length);
        }

        [Test]
        public void 다음_번호는_끝에서_처음으로_돌아간다()
        {
            Assert.AreEqual(1, SiteCatalog.StepIndex(0, +1, 3));
            Assert.AreEqual(2, SiteCatalog.StepIndex(1, +1, 3));
            Assert.AreEqual(0, SiteCatalog.StepIndex(2, +1, 3));
        }

        [Test]
        public void 이전_번호는_처음에서_끝으로_돌아간다()
        {
            Assert.AreEqual(2, SiteCatalog.StepIndex(0, -1, 3));
            Assert.AreEqual(0, SiteCatalog.StepIndex(1, -1, 3));
        }

        [Test]
        public void 현장이_하나뿐이면_번호는_그대로다()
        {
            Assert.AreEqual(0, SiteCatalog.StepIndex(0, +1, 1));
            Assert.AreEqual(0, SiteCatalog.StepIndex(0, -1, 1));
        }

        [Test]
        public void 현장이_없으면_번호를_구할_수_없다()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SiteCatalog.StepIndex(0, +1, 0));
        }

        [Test]
        public void 목록의_파일_이름은_서로_겹치지_않는다()
        {
            for (int i = 0; i < SiteCatalog.Known.Length; i++)
            {
                for (int j = i + 1; j < SiteCatalog.Known.Length; j++)
                {
                    Assert.IsFalse(
                        string.Equals(SiteCatalog.Known[i].FileName, SiteCatalog.Known[j].FileName, StringComparison.OrdinalIgnoreCase),
                        "겹치는 파일 이름: " + SiteCatalog.Known[i].FileName);
                }
            }
        }
    }
}
