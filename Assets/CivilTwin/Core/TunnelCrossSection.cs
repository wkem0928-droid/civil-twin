using System;

namespace CivilTwin.Core
{
    /// <summary>단면 모식도의 한 점이 무엇인가</summary>
    public enum SectionPart { Rock, Opening, Shotcrete, SteelRib, Bolt, Lining, BenchLine, Floor }

    /// <summary>
    /// 터널 단면 모식도 (2026-11-21, 학습용). 지보 패턴 하나의 구성(숏크리트 두께, 록볼트 길이·간격, 강지보, 상하반 분할, 2차 라이닝)을
    /// 한 장의 그림으로 보이기 위한 기하. 화면(Runtime)은 각 픽셀을 Classify로 칠한다.
    ///
    /// 모양: 마제형(말굽) 단순화 — 반지름 R의 반원 아치(스프링라인 높이 H 위) + 높이 H의 연직 측벽 + 평평한 바닥.
    ///   굴착 면적 = πR²/2 + 2R·H. R = 6.0 m로 두고 면적 90 m²가 되게 H를 정한다: H = (90 − π·36/2) ÷ 12 = 2.788 m.
    ///   (아치 둘레 πR = 18.85 m, 측벽 2H = 5.58 m — 계산에 쓰는 둘레 가정 20 m · 8 m와 조금 다르다. 그림은 모식도다.)
    /// 상하반 경계는 스프링라인(y = H)으로 그린다 (상반 면적 56.5 m² = 63 %, 계산 가정 60 %와 비슷).
    /// 좌표: x = 가운데에서 오른쪽 +, y = 바닥에서 위 + (m).
    /// 숏크리트·라이닝 두께는 그대로 그리면 너무 얇아 보이므로 Exaggerate배(기본 3) 두껍게 그린다.
    /// </summary>
    public sealed class TunnelCrossSection
    {
        public const double Radius = 6.0;
        public double SpringLine { get; }
        public SupportPattern Pattern { get; }
        public bool Lined { get; }
        public double LiningThickness { get; }
        public double Exaggerate { get; }
        /// <summary>록볼트·강지보·상하반 경계선의 반폭 (m). 그림 해상도에 맞춰 바꾼다 (화면 1픽셀 ≒ 0.14 m)</summary>
        public double LineHalfWidth { get; set; } = 0.06;

        public TunnelCrossSection(SupportPattern pattern, double area = 90.0, bool lined = false, double liningThickness = 0.30, double exaggerate = 3.0)
        {
            if (!(area > Math.PI * Radius * Radius / 2.0)) throw new ArgumentOutOfRangeException(nameof(area));
            Pattern = pattern;
            SpringLine = (area - Math.PI * Radius * Radius / 2.0) / (2.0 * Radius);
            Lined = lined;
            LiningThickness = liningThickness;
            Exaggerate = exaggerate;
        }

        /// <summary>굴착 면적 (m²) = πR²/2 + 2RH</summary>
        public double Area => Math.PI * Radius * Radius / 2.0 + 2.0 * Radius * SpringLine;

        /// <summary>굴착 윤곽에서 바깥쪽으로 잰 거리 (m, 안쪽이면 음수). 바닥 아래는 바닥까지의 거리.</summary>
        public double OutsideDistance(double x, double y)
        {
            if (y >= SpringLine)
            {
                double r = Math.Sqrt(x * x + (y - SpringLine) * (y - SpringLine));
                return r - Radius;
            }
            if (y >= 0.0) return Math.Abs(x) - Radius;
            return Math.Max(-y, Math.Abs(x) - Radius);
        }

        public SectionPart Classify(double x, double y)
        {
            double d = OutsideDistance(x, y);
            double shot = Pattern.Shotcrete * Exaggerate;
            double lining = Lined ? LiningThickness * Exaggerate : 0.0;

            if (y < 0.0)
            {
                return y > -0.25 && Math.Abs(x) <= Radius ? SectionPart.Floor : SectionPart.Rock;
            }
            if (d < 0.0)
            {
                // 굴착된 안쪽: 라이닝(윤곽 안쪽 띠), 상하반 경계선, 빈 공간
                if (Lined && d > -lining) return SectionPart.Lining;
                if (Pattern.IsBench && Math.Abs(y - SpringLine) < LineHalfWidth && (((int)Math.Floor((x + 20.0) / 0.5)) % 2 == 0)) return SectionPart.BenchLine;
                return SectionPart.Opening;
            }
            if (d <= shot)
            {
                if (Pattern.SteelRib && Math.Abs(d - shot / 2.0) < Math.Max(0.07, LineHalfWidth)) return SectionPart.SteelRib;
                return SectionPart.Shotcrete;
            }
            if (d <= Pattern.BoltLength && IsOnBolt(x, y)) return SectionPart.Bolt;
            return SectionPart.Rock;
        }

        /// <summary>
        /// 록볼트 선 위인가. 아치는 각도 간격 = 간격 ÷ R (마루 0°에서 양쪽으로), 측벽은 높이 간격 = 간격 (스프링라인에서 아래로).
        /// 랜덤 배치(P-1)는 아치에 4개(±20°, ±55°)만 그린다.
        /// </summary>
        public bool IsOnBolt(double x, double y)
        {
            double halfWidth = LineHalfWidth;
            if (y >= SpringLine)
            {
                double ang = Math.Atan2(x, y - SpringLine);          // 마루 = 0, 오른쪽 +
                double r = Math.Sqrt(x * x + (y - SpringLine) * (y - SpringLine));
                if (Pattern.BoltSpacing <= 0)
                {
                    foreach (double deg in new[] { -55.0, -20.0, 20.0, 55.0 })
                        if (Math.Abs(ang - deg * Math.PI / 180.0) * r < halfWidth) return true;
                    return false;
                }
                double step = Pattern.BoltSpacing / Radius;
                double k = Math.Round(ang / step);
                if (Math.Abs(k * step) > Math.PI / 2.0 + 1e-9) return false;
                return Math.Abs(ang - k * step) * r < halfWidth;
            }
            if (Pattern.BoltSpacing <= 0 || y < 0.3) return false;
            double j = Math.Round((SpringLine - y) / Pattern.BoltSpacing);
            if (j < 1) return false;   // 스프링라인 위치는 아치 쪽에서 그렸다
            return Math.Abs((SpringLine - y) - j * Pattern.BoltSpacing) < halfWidth;
        }

        /// <summary>그림 범위 (m): 가로 ±(R + 최대 록볼트 + 여유), 세로 −0.6 ~ H + R + 최대 록볼트 + 여유</summary>
        public double HalfWidth => Radius + Pattern.BoltLength + 0.6;
        public double Top => SpringLine + Radius + Pattern.BoltLength + 0.6;
        public double Bottom => -0.6;
    }
}
