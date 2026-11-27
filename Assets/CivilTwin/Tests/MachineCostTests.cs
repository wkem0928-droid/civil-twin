using CivilTwin.Core;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>
    /// 시간당 기계경비 = 손료 + 연료 × 경유 단가 × (1 + 잡재료) + 노임 / 8.
    /// 값: 2024 기계경비 산출표 (굴삭기 1.0 m³, 덤프트럭 15 t), 경유 1,842원/L, 2026 상반기 노임 (건설기계운전사 283,297, 화물차운전사 238,936원/일).
    /// </summary>
    public class MachineCostTests
    {
        [Test]
        public void 굴착기_1m3_시간당_기계경비()
        {
            // 27,901 + 19.5 × 1,842 × 1.22 (= 43,821.18) + 283,297 / 8 (= 35,412.125) = 107,134.305
            Assert.AreEqual(107134.305, MachineCost.Hourly(MachineCostItem.Excavator1m3_2024, 1842.0, 283297.0), 1e-6);
        }

        [Test]
        public void 덤프트럭_15t_시간당_기계경비()
        {
            // 19,631 + 15.9 × 1,842 × 1.38 (= 40,417.164) + 238,936 / 8 (= 29,867) = 89,915.164
            Assert.AreEqual(89915.164, MachineCost.Hourly(MachineCostItem.DumpTruck15t_2024, 1842.0, 238936.0), 1e-6);
        }

        [Test]
        public void 경유가_100원_오르면_굴착기는_시간당_2379원_오른다()
        {
            // 19.5 L × 100원 × 1.22 = 2,379원
            double a = MachineCost.Hourly(MachineCostItem.Excavator1m3_2024, 1842.0, 283297.0);
            double b = MachineCost.Hourly(MachineCostItem.Excavator1m3_2024, 1942.0, 283297.0);
            Assert.AreEqual(2379.0, b - a, 1e-6);
        }
    }
}
