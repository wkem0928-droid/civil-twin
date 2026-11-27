using System;
using CivilTwin.Core;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>
    /// 소양강댐 존 단면과 날짜별 성토 높이 (2026-11-22). 손계산:
    ///   마루 203, 기초 80 (높이 123), 마루폭 11, 상류 1:2.3, 하류 1:2.0 → 저폭 11 + 123 × 4.3 = 539.9 m
    ///   전체 단면적 = (11 + 539.9) / 2 × 123 = 33,880.35 m²
    ///   코어: 윗면 EL 201, 폭 6, 구배 1:0.25 → 바닥 폭 6 + 2 × 0.25 × 121 = 66.5, 면적 (6 + 66.5) / 2 × 121 = 4,386.25 m²
    ///   필터: 한쪽 4 m × 2 × 121 = 968 m²
    ///   계곡 바닥 폭 Lb: V = Lb × ∫w(1 − h/H) + 530 × ∫w·h/H,  ∫w·h/H = 539.9 × 61.5 − 4.3 × 123² / 3 = 11,518.95
    ///     ∫w(1 − h/H) = 33,880.35 − 11,518.95 = 22,361.4 → Lb = (9,591,200 − 530 × 11,518.95) / 22,361.4 = 155.90 m
    ///   성토 1970.5.1 ~ 1972.11.10 = 924일 → 하루 9,591,200 / 924 = 10,380 m³ (Python 시제품과 같은 값)
    /// </summary>
    public class DamZoneSectionTests
    {
        [Test]
        public void 단면적은_사다리꼴과_존별_손계산()
        {
            DamZoneSection d = DamZoneSection.Soyanggang();
            Assert.AreEqual(539.9, d.TotalWidth(80.0), 1e-9);
            Assert.AreEqual(33880.35, d.Area(), 1e-6);
            Assert.AreEqual(4386.25, d.Area(DamZone.Core), 1e-6);
            Assert.AreEqual(968.0, d.Area(DamZone.Filter), 1e-6);
            Assert.AreEqual(33880.35 - 4386.25 - 968.0, d.Area(DamZone.Shell), 1e-6);
            Assert.AreEqual(66.5, d.Width(DamZone.Core, 80.0), 1e-9);
            Assert.AreEqual(0.0, d.Width(DamZone.Core, 202.0), 1e-9);   // 코어 윗면 위는 사력
        }

        [Test]
        public void 계곡_바닥_폭은_공식_체적에서()
        {
            DamZoneSection d = DamZoneSection.Soyanggang();
            Assert.AreEqual(155.90, d.ValleyBottomWidth, 0.01);
            Assert.AreEqual(9591200.0, d.TotalVolume, 1e-3);
            Assert.AreEqual(530.0, d.ValleyWidth(203.0), 1e-9);
            // 존별 체적 (손계산 ∫(66.5 − 0.5h)(155.9 + 3.0415h)dh, 0 ~ 121): 코어 126.6만 m³ (13.2 %), 필터 32.9만 m³ (3.4 %)
            Assert.AreEqual(1266420.0, d.ZoneVolume(DamZone.Core), 1.0);
            Assert.AreEqual(329032.0, d.ZoneVolume(DamZone.Filter), 1.0);
            Assert.AreEqual(d.TotalVolume, d.ZoneVolume(DamZone.Core) + d.ZoneVolume(DamZone.Filter) + d.ZoneVolume(DamZone.Shell), 1e-3);
            // 단면이 아래가 훨씬 넓어서 높이 아래 절반에 체적의 63 % (계곡은 아래가 좁지만 단면 폭 차이가 더 크다)
            Assert.AreEqual(0.6256, d.VolumeBelow(80.0 + 61.5) / d.TotalVolume, 1e-3);
        }

        [Test]
        public void 날짜별_성토_높이_일정_속도_가정()
        {
            DamZoneSection d = DamZoneSection.Soyanggang();
            Assert.AreEqual(10380.09, d.DailyVolume, 0.01);
            Assert.AreEqual(80.0, d.FillElevation(new DateTime(1970, 1, 1)), 1e-9);    // 성토 전
            Assert.AreEqual(203.0, d.FillElevation(new DateTime(1973, 1, 1)), 1e-9);   // 성토 후
            Assert.AreEqual(148.61, d.FillElevation(new DateTime(1972, 2, 1)), 0.01);  // 담수 개시: 높이 68.6 m
            // 8·19 홍수: 11.10 완공까지 일정 속도면 96.5 m — 기사 [D]의 "이미 123 m"와 다르다 (두 기록 비교는 아래 테스트)
            Assert.AreEqual(176.51, d.FillElevation(new DateTime(1972, 8, 19)), 0.01);
            double z = d.FillElevation(new DateTime(1971, 5, 1));
            Assert.AreEqual(d.FillFraction(new DateTime(1971, 5, 1)) * d.TotalVolume, d.VolumeBelow(z), 1.0);
        }

        /// <summary>
        /// 두 기록 비교 (2026-11-26): 기사 [D] "8·19 홍수 때 이미 123 m" ↔ 본댐 완공 1972.11.10.
        /// 손계산: 1970.5.1 ~ 1972.8.19 = 841일 → 9,591,200 / 841 = 11,404.52 m³/일 (완공 기록 924일 10,380.09의 1.0987배)
        ///   1971.5.1(365일째) 체적 비율 365 / 841 = 0.43401
        /// </summary>
        [Test]
        public void 기사대로_8월19일에_마루에_닿는_가정()
        {
            DamZoneSection d = DamZoneSection.Soyanggang(crestByFlood: true);
            Assert.AreEqual(new DateTime(1972, 8, 19), d.CrestDate);
            Assert.IsTrue(d.HasFinishing);
            Assert.AreEqual(841.0, d.FillDays, 1e-9);
            Assert.AreEqual(11404.52, d.DailyVolume, 0.01);
            Assert.AreEqual(203.0, d.FillElevation(new DateTime(1972, 8, 19)), 1e-9);   // 기사: 123 m
            Assert.AreEqual(203.0, d.FillElevation(new DateTime(1972, 10, 1)), 1e-9);   // 마무리 기간: 높이 그대로
            Assert.AreEqual(0.43401, d.FillFraction(new DateTime(1971, 5, 1)), 1e-5);

            // 같은 단면 (계곡 바닥 폭·체적은 그대로)
            DamZoneSection a = DamZoneSection.Soyanggang();
            Assert.IsFalse(a.HasFinishing);
            Assert.AreEqual(a.ValleyBottomWidth, d.ValleyBottomWidth, 1e-9);
            Assert.AreEqual(a.TotalVolume, d.TotalVolume, 1e-6);
            Assert.AreEqual(d.CrestDate, a.WithCrestDate(new DateTime(1972, 8, 19)).CrestDate);

            // 높이로는 26.5 m 차이지만 체적으로는 9.0 %, 하루 성토량으로는 9.9 %
            Assert.AreEqual(26.49, 203.0 - a.FillElevation(new DateTime(1972, 8, 19)), 0.01);
            Assert.AreEqual(1.0 - 841.0 / 924.0, 1.0 - a.FillFraction(new DateTime(1972, 8, 19)), 1e-9);
            Assert.AreEqual(924.0 / 841.0, d.DailyVolume / a.DailyVolume, 1e-9);
        }

        [Test]
        public void 마루_도달일은_성토_기간_안이어야_한다()
        {
            DamZoneSection a = DamZoneSection.Soyanggang();
            Assert.Throws<ArgumentOutOfRangeException>(() => a.WithCrestDate(new DateTime(1970, 5, 1)));    // 시작일과 같음
            Assert.Throws<ArgumentOutOfRangeException>(() => a.WithCrestDate(new DateTime(1972, 11, 11)));  // 완공 뒤
        }

        [Test]
        public void 단면이_성립하지_않으면_거부()
        {
            // 필터가 너무 넓어 사면 밖으로 나감
            Assert.Throws<ArgumentOutOfRangeException>(() => new DamZoneSection(203, 80, 11, 2.3, 2.0, 201, 6, 0.25, 300,
                530, 9591200, new DateTime(1970, 5, 1), new DateTime(1972, 11, 10)));
            // 체적이 너무 커서 계곡 바닥 폭이 마루 길이보다 넓어야 함
            Assert.Throws<ArgumentOutOfRangeException>(() => new DamZoneSection(203, 80, 11, 2.3, 2.0, 201, 6, 0.25, 4,
                530, 30000000, new DateTime(1970, 5, 1), new DateTime(1972, 11, 10)));
        }
    }
}
