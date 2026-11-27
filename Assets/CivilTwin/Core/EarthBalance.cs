using System;

namespace CivilTwin.Core
{
    /// <summary>
    /// 토량 균형 (Phase 5-1). 흙의 부피는 상태에 따라 달라진다는 것을 반영해
    /// 깎은 흙으로 성토를 얼마나 채우고, 얼마가 남거나(사토) 모자라는지(토취) 계산한다.
    ///
    /// 흙의 세 가지 상태와 토량환산계수:
    ///   자연 상태 (땅속에 있을 때)          : 기준 1
    ///   흐트러진 상태 (파서 트럭에 실었을 때) : 자연 × L   (L > 1, 부푼다)
    ///   다져진 상태 (성토하고 다졌을 때)      : 자연 × C   (보통 C &lt; 1, 줄어든다)
    ///
    /// 단위 약속:
    ///   절토량 = 자연 상태 m³ (지형에서 깎아 내는 부피)
    ///   성토량 = 다져진 상태 m³ (계획면을 채워야 하는 부피)
    ///   운반량 = 흐트러진 상태 m³ (덤프트럭에 싣는 부피, 5-4에서 쓴다)
    ///
    /// 계산:
    ///   절토로 만들 수 있는 성토 = 절토 × C
    ///   남을 때 (절토 × C ≥ 성토):  유용토(자연) = 성토 / C,  사토(자연) = 절토 − 유용토,  토취 = 0
    ///   모자랄 때 (절토 × C &lt; 성토): 유용토(자연) = 절토,     사토 = 0,  토취(자연) = (성토 − 절토 × C) / C
    ///   운반량(흐트러진) = 자연 상태 부피 × L
    ///
    /// 가정: 토취장의 흙도 현장 흙과 같은 L, C를 가진다. 토사·암 구분은 5-3에서 다룬다.
    /// </summary>
    public readonly struct EarthBalance
    {
        /// <summary>절토량 (자연 상태 m³)</summary>
        public double CutVolume { get; }

        /// <summary>성토량 (다져진 상태 m³)</summary>
        public double FillVolume { get; }

        /// <summary>토량환산계수 L (흐트러진 / 자연)</summary>
        public double LooseFactor { get; }

        /// <summary>토량환산계수 C (다져진 / 자연)</summary>
        public double CompactFactor { get; }

        /// <summary>깎은 흙 중 성토에 다시 쓰는 양 (자연 상태 m³)</summary>
        public double ReusedVolume { get; }

        /// <summary>남아서 현장 밖으로 내보내는 흙 (자연 상태 m³)</summary>
        public double SurplusVolume { get; }

        /// <summary>모자라서 현장 밖에서 가져오는 흙 (자연 상태 m³, 토취장에서 파낼 양)</summary>
        public double BorrowVolume { get; }

        /// <summary>토취장 흙의 L (기본: 절토와 같음)</summary>
        public double BorrowLooseFactor { get; }

        /// <summary>토취장 흙의 C (기본: 절토와 같음)</summary>
        public double BorrowCompactFactor { get; }

        public EarthBalance(double cutVolume, double fillVolume, double looseFactor, double compactFactor)
            : this(cutVolume, fillVolume, looseFactor, compactFactor, looseFactor, compactFactor)
        {
        }

        /// <summary>
        /// 토취장 흙의 성질이 절토와 다를 때 (예: 절토에 암이 섞였고 토취는 토사). 모자란 다져진 부피를 토취 흙의 C로 나눈다:
        ///   토취(자연) = (성토 − 절토 × C) / C토취
        /// </summary>
        public EarthBalance(double cutVolume, double fillVolume, double looseFactor, double compactFactor,
            double borrowLooseFactor, double borrowCompactFactor)
        {
            if (!(borrowLooseFactor >= 1.0)) throw new ArgumentOutOfRangeException(nameof(borrowLooseFactor));
            if (!(borrowCompactFactor > 0.0)) throw new ArgumentOutOfRangeException(nameof(borrowCompactFactor));
            BorrowLooseFactor = borrowLooseFactor;
            BorrowCompactFactor = borrowCompactFactor;

            if (cutVolume < 0.0 || double.IsNaN(cutVolume)) throw new ArgumentOutOfRangeException(nameof(cutVolume));
            if (fillVolume < 0.0 || double.IsNaN(fillVolume)) throw new ArgumentOutOfRangeException(nameof(fillVolume));
            if (!(looseFactor >= 1.0)) throw new ArgumentOutOfRangeException(nameof(looseFactor), "L은 1 이상이어야 한다 (파낸 흙은 부푼다).");
            if (!(compactFactor > 0.0)) throw new ArgumentOutOfRangeException(nameof(compactFactor), "C는 0보다 커야 한다.");

            CutVolume = cutVolume;
            FillVolume = fillVolume;
            LooseFactor = looseFactor;
            CompactFactor = compactFactor;

            double needed = fillVolume / compactFactor;   // 성토를 채우는 데 필요한 자연 상태 흙
            if (cutVolume >= needed)
            {
                ReusedVolume = needed;
                SurplusVolume = cutVolume - needed;
                BorrowVolume = 0.0;
            }
            else
            {
                ReusedVolume = cutVolume;
                SurplusVolume = 0.0;
                BorrowVolume = (fillVolume - cutVolume * compactFactor) / borrowCompactFactor;
            }
        }

        /// <summary>절토를 다지면 되는 부피 (다져진 상태 m³) = 절토 × C</summary>
        public double CutAsCompacted => CutVolume * CompactFactor;

        /// <summary>유용토의 운반량 (흐트러진 상태 m³, 현장 안에서 절토 → 성토로 옮김)</summary>
        public double ReusedLooseVolume => ReusedVolume * LooseFactor;

        /// <summary>사토의 운반량 (흐트러진 상태 m³, 현장 밖으로)</summary>
        public double SurplusLooseVolume => SurplusVolume * LooseFactor;

        /// <summary>토취의 운반량 (흐트러진 상태 m³, 현장 안으로)</summary>
        public double BorrowLooseVolume => BorrowVolume * BorrowLooseFactor;
    }
}
