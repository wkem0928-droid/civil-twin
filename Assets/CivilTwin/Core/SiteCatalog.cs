using System;
using System.Collections.Generic;

namespace CivilTwin.Core
{
    /// <summary>현장 하나의 정보: 지형 파일 이름, 시공 회사, 현장 이름.</summary>
    public readonly struct SiteInfo
    {
        /// <summary>파일 이름 (폴더 없이, 확장자 포함). 예: hyundai_soyanggang.asc</summary>
        public readonly string FileName;
        public readonly string Company;
        public readonly string SiteName;

        public SiteInfo(string fileName, string company, string siteName)
        {
            FileName = fileName;
            Company = company;
            SiteName = siteName;
        }

        /// <summary>화면에 보여 줄 이름. 회사를 알면 "회사 · 현장", 모르면 현장 이름만.</summary>
        public string DisplayName =>
            string.IsNullOrEmpty(Company) ? SiteName : Company + " · " + SiteName;
    }

    /// <summary>
    /// 현장 목록. 지형 파일 이름을 회사·현장 이름과 짝지어 둔다.
    ///
    /// 지형 파일(Data/sites/*.asc)은 Git에 넣지 않지만, 이 목록은 코드라서 Git에 남는다.
    /// 현장을 추가할 때는 Known 배열에 한 줄을 더한다. 화면에는 이 배열의 순서대로 나온다.
    /// 여기에는 Docs/SITES.md에서 시공사가 ✅(복수 자료로 확인)인 현장만 적는다.
    /// </summary>
    public static class SiteCatalog
    {
        public static readonly SiteInfo[] Known =
        {
            new SiteInfo("hyundai_soyanggang.asc", "현대건설", "소양강댐"),
            new SiteInfo("samsung_cheongsong.asc", "삼성물산", "청송양수발전소"),
            new SiteInfo("gs_buhang.asc", "GS건설", "김천 부항댐"),
            new SiteInfo("daewoo_inje_yangyang.asc", "대우건설", "인제양양터널"),
            new SiteInfo("dlenc_yecheon.asc", "DL이앤씨", "예천양수발전소"),
            new SiteInfo("lotte_anyang_seongnam.asc", "롯데건설", "안양~성남 고속도로 (청계산 구간)"),
            new SiteInfo("posco_sanseong.asc", "포스코이앤씨", "부산 산성터널"),
            new SiteInfo("hdc_seoul_chuncheon.asc", "IPARK현대산업개발", "서울~춘천 고속도로 (미사터널 구간)"),
            // 랜드마크 (8개 회사가 아닌 곳에 지원할 때, 2026-11-27): 인천대교 사장교
            new SiteInfo(BridgeStructures.IncheonFile, "랜드마크", "인천대교 (사장교)"),
        };

        /// <summary>
        /// 파일 이름으로 현장 정보를 찾는다. 대소문자는 구분하지 않는다.
        /// 목록에 없는 파일이면 회사는 비우고, 현장 이름은 확장자를 뗀 파일 이름으로 한다.
        /// </summary>
        public static SiteInfo Describe(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) throw new ArgumentException("파일 이름이 비어 있다.", nameof(fileName));

            int index = IndexInKnown(fileName);
            if (index >= 0) return Known[index];

            int dot = fileName.LastIndexOf('.');
            string name = dot > 0 ? fileName.Substring(0, dot) : fileName;
            return new SiteInfo(fileName, "", name);
        }

        /// <summary>
        /// 파일 이름들을 화면에 보여 줄 순서로 정렬해 현장 정보로 바꾼다.
        /// 목록(Known)에 있는 현장이 목록 순서대로 먼저 오고, 그 밖의 파일은 이름순으로 뒤에 온다.
        /// </summary>
        public static SiteInfo[] Arrange(IEnumerable<string> fileNames)
        {
            if (fileNames == null) throw new ArgumentNullException(nameof(fileNames));

            var known = new List<KeyValuePair<int, SiteInfo>>();
            var unknown = new List<SiteInfo>();

            foreach (string fileName in fileNames)
            {
                int index = IndexInKnown(fileName);
                if (index >= 0) known.Add(new KeyValuePair<int, SiteInfo>(index, Known[index]));
                else unknown.Add(Describe(fileName));
            }

            known.Sort((a, b) => a.Key.CompareTo(b.Key));
            unknown.Sort((a, b) => string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase));

            var result = new SiteInfo[known.Count + unknown.Count];
            int i = 0;
            foreach (KeyValuePair<int, SiteInfo> k in known) result[i++] = k.Value;
            foreach (SiteInfo u in unknown) result[i++] = u;
            return result;
        }

        /// <summary>
        /// 다음(step = +1) 또는 이전(step = -1) 현장의 번호. 끝을 넘으면 반대쪽 끝으로 돌아간다.
        /// 예: 3개 중 마지막(2)에서 다음 → 0,  첫 번째(0)에서 이전 → 2.
        /// </summary>
        public static int StepIndex(int current, int step, int count)
        {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));

            int next = (current + step) % count;
            if (next < 0) next += count;   // C#의 %는 음수에서 음수를 돌려주므로 보정한다
            return next;
        }

        private static int IndexInKnown(string fileName)
        {
            for (int i = 0; i < Known.Length; i++)
            {
                if (string.Equals(Known[i].FileName, fileName, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }
    }
}
