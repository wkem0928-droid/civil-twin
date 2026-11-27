using System;

namespace CivilTwin.Core
{
    /// <summary>
    /// 건물 계획: 부지 위에 올리는 직사각형 철근콘크리트 건물과 그 콘크리트 물량.
    ///
    /// 건물의 구성 (단순화한 모델):
    ///   - 기초: 건물 바닥 전체에 까는 두꺼운 판 (매트기초, 온통기초)
    ///   - 기둥: 정사각형 단면. 동서로 (칸 수 + 1)줄, 남북으로 (칸 수 + 1)줄
    ///   - 슬래브: 층마다 기둥 위에 얹는 바닥판 (건물 바닥 전체 넓이). 1층 바닥은 기초가 대신한다.
    ///
    /// 계산 방법 (단위: 길이 m, 넓이 m², 부피 m³):
    ///   바닥 넓이        = 동서 폭 × 남북 폭
    ///   기둥 개수        = (동서 칸 수 + 1) × (남북 칸 수 + 1)
    ///   기초 부피        = 바닥 넓이 × 기초 두께
    ///   한 층 슬래브 부피 = 바닥 넓이 × 슬래브 두께
    ///   한 층 기둥 부피   = 기둥 개수 × 기둥 한 변² × (층고 − 슬래브 두께)
    ///   전체             = 기초 + 층수 × (한 층 기둥 + 한 층 슬래브)
    ///
    ///   기둥 높이를 (층고 − 슬래브 두께)로 잡는 이유: 층고는 아래층 바닥 윗면에서 위층 바닥 윗면까지다.
    ///   그 안에 슬래브 두께가 들어 있으므로, 기둥과 슬래브가 겹치는 부분을 두 번 세지 않으려면 빼야 한다.
    ///
    /// 손계산 예: 동서 30 m × 남북 20 m, 5층, 층고 3.5 m, 칸 수 5 × 4,
    ///            기둥 0.5 m, 슬래브 0.25 m, 기초 0.8 m
    ///   바닥 넓이   = 30 × 20 = 600 m²
    ///   기둥 개수   = 6 × 5 = 30개 (기둥 간격 동서 6 m, 남북 5 m)
    ///   기초        = 600 × 0.8 = 480 m³
    ///   한 층 슬래브 = 600 × 0.25 = 150 m³          → 5층 750 m³
    ///   한 층 기둥   = 30 × 0.5² × (3.5 − 0.25) = 24.375 m³ → 5층 121.875 m³
    ///   전체        = 480 + 750 + 121.875 = 1351.875 m³
    ///   건물 높이   = 5 × 3.5 = 17.5 m,  연면적 = 600 × 5 = 3000 m²
    ///
    /// 한계: 보, 벽, 계단, 지하층, 철근·거푸집 물량은 다루지 않는다 (보 없이 기둥이 슬래브를 직접 받치는 형태로 본다).
    ///       치수(기둥 크기, 슬래브·기초 두께)는 입력값일 뿐이고 구조 계산으로 정한 값이 아니다.
    /// </summary>
    public readonly struct BuildingPlan
    {
        /// <summary>건물의 동서 방향 폭 (m)</summary>
        public readonly double WidthEastWest;

        /// <summary>건물의 남북 방향 폭 (m)</summary>
        public readonly double LengthNorthSouth;

        /// <summary>지상 층수</summary>
        public readonly int Floors;

        /// <summary>층고: 아래층 바닥 윗면에서 위층 바닥 윗면까지 (m)</summary>
        public readonly double FloorHeight;

        /// <summary>동서 방향 칸 수 (기둥과 기둥 사이의 개수)</summary>
        public readonly int BaysEastWest;

        /// <summary>남북 방향 칸 수</summary>
        public readonly int BaysNorthSouth;

        /// <summary>정사각형 기둥의 한 변 (m)</summary>
        public readonly double ColumnSize;

        /// <summary>슬래브 두께 (m)</summary>
        public readonly double SlabThickness;

        /// <summary>기초(매트기초) 두께 (m)</summary>
        public readonly double FoundationThickness;

        public BuildingPlan(
            double widthEastWest,
            double lengthNorthSouth,
            int floors,
            double floorHeight,
            int baysEastWest,
            int baysNorthSouth,
            double columnSize,
            double slabThickness,
            double foundationThickness)
        {
            if (!(widthEastWest > 0)) throw new ArgumentOutOfRangeException(nameof(widthEastWest), "동서 폭은 0보다 커야 한다.");
            if (!(lengthNorthSouth > 0)) throw new ArgumentOutOfRangeException(nameof(lengthNorthSouth), "남북 폭은 0보다 커야 한다.");
            if (floors < 1) throw new ArgumentOutOfRangeException(nameof(floors), "층수는 1 이상이어야 한다.");
            if (baysEastWest < 1) throw new ArgumentOutOfRangeException(nameof(baysEastWest), "동서 칸 수는 1 이상이어야 한다.");
            if (baysNorthSouth < 1) throw new ArgumentOutOfRangeException(nameof(baysNorthSouth), "남북 칸 수는 1 이상이어야 한다.");
            if (!(columnSize > 0)) throw new ArgumentOutOfRangeException(nameof(columnSize), "기둥 크기는 0보다 커야 한다.");
            if (!(slabThickness > 0)) throw new ArgumentOutOfRangeException(nameof(slabThickness), "슬래브 두께는 0보다 커야 한다.");
            if (!(foundationThickness > 0)) throw new ArgumentOutOfRangeException(nameof(foundationThickness), "기초 두께는 0보다 커야 한다.");
            if (!(floorHeight > slabThickness)) throw new ArgumentOutOfRangeException(nameof(floorHeight), "층고는 슬래브 두께보다 커야 한다.");

            WidthEastWest = widthEastWest;
            LengthNorthSouth = lengthNorthSouth;
            Floors = floors;
            FloorHeight = floorHeight;
            BaysEastWest = baysEastWest;
            BaysNorthSouth = baysNorthSouth;
            ColumnSize = columnSize;
            SlabThickness = slabThickness;
            FoundationThickness = foundationThickness;
        }

        /// <summary>한 층의 바닥 넓이 (m²)</summary>
        public double FloorArea => WidthEastWest * LengthNorthSouth;

        /// <summary>연면적: 모든 층의 바닥 넓이 합 (m²)</summary>
        public double GrossFloorArea => FloorArea * Floors;

        /// <summary>건물 높이: 1층 바닥 윗면에서 지붕 슬래브 윗면까지 (m)</summary>
        public double Height => Floors * FloorHeight;

        /// <summary>동서 방향 기둥 간격 (m)</summary>
        public double SpanEastWest => WidthEastWest / BaysEastWest;

        /// <summary>남북 방향 기둥 간격 (m)</summary>
        public double SpanNorthSouth => LengthNorthSouth / BaysNorthSouth;

        /// <summary>한 층의 기둥 개수</summary>
        public int ColumnsPerFloor => (BaysEastWest + 1) * (BaysNorthSouth + 1);

        /// <summary>기둥 한 개의 높이: 층고에서 슬래브 두께를 뺀 값 (m)</summary>
        public double ColumnClearHeight => FloorHeight - SlabThickness;

        /// <summary>기초 콘크리트 부피 (m³)</summary>
        public double FoundationVolume => FloorArea * FoundationThickness;

        /// <summary>한 층의 슬래브 콘크리트 부피 (m³)</summary>
        public double SlabVolumePerFloor => FloorArea * SlabThickness;

        /// <summary>한 층의 기둥 콘크리트 부피 (m³)</summary>
        public double ColumnVolumePerFloor => ColumnsPerFloor * ColumnSize * ColumnSize * ColumnClearHeight;

        /// <summary>모든 층의 슬래브 부피 (m³)</summary>
        public double TotalSlabVolume => SlabVolumePerFloor * Floors;

        /// <summary>모든 층의 기둥 부피 (m³)</summary>
        public double TotalColumnVolume => ColumnVolumePerFloor * Floors;

        /// <summary>건물 전체 콘크리트 부피: 기초 + 기둥 + 슬래브 (m³)</summary>
        public double TotalConcreteVolume => FoundationVolume + TotalColumnVolume + TotalSlabVolume;
    }
}
