using System;
using System.Collections.Generic;

namespace CivilTwin.Core
{
    /// <summary>단계별 3D 지형에서 격자점 하나가 무엇인지 (색을 정하는 데 쓴다)</summary>
    public enum StageCell : byte { Natural, Sediment, Rock, River, Reservoir, Cofferdam }

    /// <summary>
    /// 소양강댐 시공 단계별 3D 지형 (2026-11-22, 설계안 4단계). 존 단면 화면과 같은 날짜를 받아 그날의 지형을 만든다.
    ///
    /// 1. 공사 전 암반면 (Rock)
    ///    - 위성 지형(이미 댐과 호수가 있음)에서 댐 둘레 띠는 복원한 계곡 단면(AbutmentValley)으로 내린다 (댐 모드와 같은 방법).
    ///    - 띠 밖의 호수 수면(평평한 점, 기초 + 30 m보다 높음)은 바닥 자료가 없다. 물가에서의 거리 × 양안 기울기만큼
    ///      내려가고 기초 표고에서 멈추는 바닥으로 **추정**한다 (그림용). 이렇게 해야 담수 전 화면에 호수가 떠 있지 않다.
    ///    - 띠 끝에서 호수 바닥 추정으로 150 m에 걸쳐 섞는다 (계단이 생기지 않게).
    /// 2. 공사 전 지표 (Natural) = 암반면 + 하상 퇴적층 6 m (계곡 바닥에만, 하류 띠 끝에서 0으로 줄어든다. 두께는 그림용)
    /// 3. 날짜별 (StageGrid):
    ///    - 유수전환(1969.10.1) 전: 계곡 바닥 전체가 강물. 뒤: 상·하류 가물막이 사이는 마른 땅.
    ///    - 가물막이: 상류 마루 EL 100 → 1970.10 체절 때 EL 115, 하류 EL 95, 마루폭 10 m, 1:2 (높이·위치는 그림용).
    ///    - 기초굴착(1969.10.24 ~ 1970.1.1): 댐 바닥 안의 퇴적층을 축에서 바깥으로 걷어 암반을 드러낸다.
    ///    - 담수(1972.2.1 ~): 수위 = min(상시만수위 193.5, 성토 높이 − 15) (존 단면 화면과 같은 그림용 규칙), 준공 때 193.5.
    ///      상류(d &gt; 0)에서 이어진, 수위보다 낮은 점만 채운다 (축 하류로 새지 않게, 다른 골짜기를 채우지 않게).
    /// 댐 몸체(존별 성토)는 지형과 따로 그린다 (Runtime/DamStage3D). 성토 높이는 DamZoneSection.FillElevation.
    /// </summary>
    public sealed class DamStageModel
    {
        public static readonly DateTime DiversionDate = new DateTime(1969, 10, 1);
        public static readonly DateTime ExcavationStart = new DateTime(1969, 10, 24);
        public static readonly DateTime GroutStart = new DateTime(1970, 1, 1);
        public static readonly DateTime CofferRaiseDate = new DateTime(1970, 10, 1);
        public static readonly DateTime Tunnel1Start = new DateTime(1968, 3, 1);
        public static readonly DateTime Tunnel2Open = new DateTime(1970, 6, 1);
        public static readonly DateTime Tunnel1Closed = new DateTime(1971, 11, 1);
        public static readonly DateTime ImpoundStart = new DateTime(1972, 2, 1);
        public static readonly DateTime SpillwayStart = new DateTime(1969, 10, 15), SpillwayEnd = new DateTime(1973, 6, 30);

        /// <summary>그림용 값 (자료 없음)</summary>
        public const double SedimentThickness = 6.0;
        public const double UpCofferOffset = 40.0, DownCofferOffset = 25.0;          // 댐 바닥 끝에서 가물막이 축까지
        public const double UpCofferCrestLow = 100.0, UpCofferCrestHigh = 115.0, DownCofferCrest = 95.0;
        public const double CofferCrestWidth = 10.0, CofferSlope = 2.0;
        public const double BlendLength = 150.0;
        public const double ReservoirBelowFill = 15.0;

        public DamSection Dam { get; }
        public DamZoneSection Zones { get; }
        public AbutmentValley Valley { get; }
        public DateTime Completion { get; }

        /// <summary>공사 전 암반면과 지표 (같은 격자)</summary>
        public ElevationGrid Rock { get; }
        public ElevationGrid Natural { get; }

        /// <summary>계곡 바닥이 기초 표고와 만나는 축 위의 두 점 (s)</summary>
        public double FloorLeftS { get; }
        public double FloorRightS { get; }

        public double BandUpstreamD { get; }
        public double BandDownstreamD { get; }
        public double UpCofferD => Dam.UpstreamBase + UpCofferOffset;
        public double DownCofferD => -(Dam.DownstreamBase + DownCofferOffset);

        private readonly double[] _s, _d;
        private readonly bool[] _inBand, _floor, _river;
        private readonly int _seed;

        public DamStageModel(ElevationGrid surveyed, DamSection dam, AbutmentValley valley, DamZoneSection zones,
            double upstreamExtra, double downstreamExtra, DateTime completion, double cellSize)
        {
            if (surveyed == null) throw new ArgumentNullException(nameof(surveyed));
            if (dam == null) throw new ArgumentNullException(nameof(dam));
            if (zones == null) throw new ArgumentNullException(nameof(zones));
            if (!(cellSize > 0)) throw new ArgumentOutOfRangeException(nameof(cellSize));

            Dam = dam;
            Zones = zones;
            Valley = valley;
            Completion = completion;
            double bed = dam.FoundationElevation;
            BandUpstreamD = dam.UpstreamBase + upstreamExtra;
            BandDownstreamD = -(dam.DownstreamBase + downstreamExtra);
            FloorLeftS = valley.LeftS + (valley.LeftZ - bed) / valley.Slope;
            FloorRightS = valley.RightS - (valley.RightZ - bed) / valley.Slope;

            ElevationGrid lakeBed = CarveLake(surveyed, bed, valley.Slope);
            bool[] coarseFlat = FlatSurfaceDetector.FindFlatNodes(surveyed);

            int columns = (int)Math.Floor(surveyed.WidthMeters / cellSize) + 1;
            int rows = (int)Math.Floor(surveyed.HeightMeters / cellSize) + 1;
            int count = columns * rows;
            var rock = new double[count];
            var natural = new double[count];
            _s = new double[count];
            _d = new double[count];
            _inBand = new bool[count];
            _floor = new bool[count];
            _river = new bool[count];

            for (int row = 0; row < rows; row++)
            {
                double n = surveyed.OriginNorthing + row * cellSize;
                for (int col = 0; col < columns; col++)
                {
                    int i = row * columns + col;
                    double e = surveyed.OriginEasting + col * cellSize;
                    dam.ToAxis(e, n, out double s, out double d);
                    _s[i] = s;
                    _d[i] = d;
                    if (!lakeBed.TryGetElevationAt(e, n, out double g)) g = double.NaN;
                    surveyed.TryGetElevationAt(e, n, out double sat);

                    double z = g;
                    bool inS = Math.Abs(s) <= dam.HalfLength;
                    if (!double.IsNaN(g) && inS)
                    {
                        double v = Math.Min(g, valley.Elevation(s));
                        double w = BandWeight(d);       // 1 = 띠 안, 0 = 띠 밖, 사이는 섞음
                        z = g + (v - g) * w;
                        _inBand[i] = d >= BandDownstreamD && d <= BandUpstreamD;
                    }
                    rock[i] = z;

                    // 퇴적층: 암반면이 기초 표고(계곡 바닥)인 곳. 하류 띠 끝에서 0으로.
                    double t = 0.0;
                    if (!double.IsNaN(z) && z <= bed + 0.01)
                    {
                        _floor[i] = true;
                        t = SedimentThickness * Taper(d, -dam.DownstreamBase, BandDownstreamD);
                    }
                    natural[i] = z + t;

                    // 강: 계곡 바닥 + 위성의 하류 강 (평평하고 낮은 곳, 또는 기초 표고 이하)
                    bool lowFlat = NearestFlat(surveyed, coarseFlat, e, n) && sat < bed + 30.0;
                    _river[i] = _floor[i] || (!double.IsNaN(sat) && (lowFlat || sat <= bed + 1.0) && !_inBand[i] && d < 0);
                }
            }

            Rock = new ElevationGrid(columns, rows, cellSize, surveyed.OriginEasting, surveyed.OriginNorthing, rock);
            Natural = new ElevationGrid(columns, rows, cellSize, surveyed.OriginEasting, surveyed.OriginNorthing, natural);

            // 담수의 시작점: 상류 띠 끝, 계곡 바닥 가운데
            _seed = NodeIndex((FloorLeftS + FloorRightS) / 2.0, BandUpstreamD - 20.0);
        }

        private double BandWeight(double d)
        {
            if (d >= BandDownstreamD && d <= BandUpstreamD) return 1.0;
            double gap = d > BandUpstreamD ? d - BandUpstreamD : BandDownstreamD - d;
            return gap >= BlendLength ? 0.0 : 1.0 - gap / BlendLength;
        }

        /// <summary>d가 full 쪽이면 1, zero 쪽이면 0, 사이는 직선</summary>
        private static double Taper(double d, double full, double zero)
        {
            if (d >= full) return 1.0;
            if (d <= zero) return 0.0;
            return (d - zero) / (full - zero);
        }

        private static bool NearestFlat(ElevationGrid g, bool[] flat, double e, double n)
        {
            int col = (int)Math.Round((e - g.OriginEasting) / g.CellSize);
            int row = (int)Math.Round((n - g.OriginNorthing) / g.CellSize);
            if (col < 0 || row < 0 || col >= g.Columns || row >= g.Rows) return false;
            return flat[row * g.Columns + col];
        }

        /// <summary>
        /// 호수 바닥 추정 (그림용): 호수 점(평평하고 기초 + 30 m보다 높은 점)의 표고를
        /// max(기초 표고, 수면 − 물가에서의 거리 × 기울기)로 내린다. 거리는 8방향 거리 변환(두 번 훑기).
        /// </summary>
        public static ElevationGrid CarveLake(ElevationGrid surveyed, double bed, double slope)
        {
            bool[] flat = FlatSurfaceDetector.FindFlatNodes(surveyed);
            int c = surveyed.Columns, r = surveyed.Rows;
            var dist = new double[c * r];
            for (int row = 0; row < r; row++)
                for (int col = 0; col < c; col++)
                {
                    int i = row * c + col;
                    double z = surveyed.GetElevation(col, row);
                    bool lake = flat[i] && !double.IsNaN(z) && z > bed + 30.0;
                    dist[i] = lake ? double.MaxValue : 0.0;
                }
            double a = surveyed.CellSize, b = surveyed.CellSize * Math.Sqrt(2.0);
            for (int row = 0; row < r; row++)
                for (int col = 0; col < c; col++)
                {
                    int i = row * c + col;
                    if (dist[i] == 0.0) continue;
                    if (col > 0) dist[i] = Math.Min(dist[i], dist[i - 1] + a);
                    if (row > 0) dist[i] = Math.Min(dist[i], dist[i - c] + a);
                    if (col > 0 && row > 0) dist[i] = Math.Min(dist[i], dist[i - c - 1] + b);
                    if (col < c - 1 && row > 0) dist[i] = Math.Min(dist[i], dist[i - c + 1] + b);
                }
            for (int row = r - 1; row >= 0; row--)
                for (int col = c - 1; col >= 0; col--)
                {
                    int i = row * c + col;
                    if (dist[i] == 0.0) continue;
                    if (col < c - 1) dist[i] = Math.Min(dist[i], dist[i + 1] + a);
                    if (row < r - 1) dist[i] = Math.Min(dist[i], dist[i + c] + a);
                    if (col < c - 1 && row < r - 1) dist[i] = Math.Min(dist[i], dist[i + c + 1] + b);
                    if (col > 0 && row < r - 1) dist[i] = Math.Min(dist[i], dist[i + c - 1] + b);
                }
            var z2 = new double[c * r];
            for (int row = 0; row < r; row++)
                for (int col = 0; col < c; col++)
                {
                    int i = row * c + col;
                    double z = surveyed.GetElevation(col, row);
                    z2[i] = dist[i] > 0.0 && dist[i] < double.MaxValue ? Math.Max(bed, z - dist[i] * slope) : z;
                }
            return new ElevationGrid(c, r, surveyed.CellSize, surveyed.OriginEasting, surveyed.OriginNorthing, z2);
        }

        /// <summary>축 좌표 (s, d)에 가장 가까운 격자점 번호</summary>
        public int NodeIndex(double s, double d)
        {
            Dam.FromAxis(s, d, out double e, out double n);
            int col = (int)Math.Round((e - Rock.OriginEasting) / Rock.CellSize);
            int row = (int)Math.Round((n - Rock.OriginNorthing) / Rock.CellSize);
            col = Math.Max(0, Math.Min(Rock.Columns - 1, col));
            row = Math.Max(0, Math.Min(Rock.Rows - 1, row));
            return row * Rock.Columns + col;
        }

        // ── 날짜별 값 ─────────────────────────────────────────

        public static double Progress(DateTime date, DateTime a, DateTime b)
        {
            double t = (date - a).TotalDays / (b - a).TotalDays;
            return t < 0 ? 0 : t > 1 ? 1 : t;
        }

        /// <summary>기초굴착 진행 (0 ~ 1)</summary>
        public double Excavated(DateTime date) => Progress(date, ExcavationStart, GroutStart);

        /// <summary>그라우팅 진행 (0 ~ 1, 성토 시작까지)</summary>
        public double Grouted(DateTime date) => Progress(date, GroutStart, Zones.FillStart);

        public double FillElevation(DateTime date) => Zones.FillElevation(date);

        /// <summary>저수지 수위 (담수 전이면 NaN). 준공 전은 그림용 규칙, 준공 때 상시만수위.</summary>
        public double ReservoirLevel(DateTime date)
        {
            if (date < ImpoundStart) return double.NaN;
            if (date >= Completion) return DamZoneSection.SoyanggangNormalWaterLevel;
            return Math.Min(DamZoneSection.SoyanggangNormalWaterLevel, FillElevation(date) - ReservoirBelowFill);
        }

        public double UpCofferCrest(DateTime date) => date >= CofferRaiseDate ? UpCofferCrestHigh : UpCofferCrestLow;

        /// <summary>그날의 지형과 점별 종류</summary>
        public ElevationGrid StageGrid(DateTime date, out StageCell[] cells)
        {
            int count = _s.Length;
            var z = new double[count];
            cells = new StageCell[count];
            bool diverted = date >= DiversionDate;
            double dug = Excavated(date);
            double digUp = dug * Dam.UpstreamBase, digDown = dug * Dam.DownstreamBase;
            double upCrest = UpCofferCrest(date);

            for (int i = 0; i < count; i++)
            {
                double h = Natural.GetElevation(i % Natural.Columns, i / Natural.Columns);
                double d = _d[i];
                StageCell cell = _floor[i] ? StageCell.Sediment : StageCell.Natural;
                bool betweenCoffers = _inBand[i] && d > DownCofferD && d < UpCofferD;

                if (_river[i] && !(diverted && betweenCoffers)) cell = StageCell.River;

                // 기초굴착: 댐 바닥 안의 퇴적층을 걷어 낸다
                if (_floor[i] && _inBand[i] && d <= digUp && d >= -digDown && dug > 0)
                {
                    h = Rock.GetElevation(i % Rock.Columns, i / Rock.Columns);
                    cell = StageCell.Rock;
                }

                // 가물막이
                if (diverted && _inBand[i])
                {
                    double up = Coffer(d, UpCofferD, upCrest), down = Coffer(d, DownCofferD, DownCofferCrest);
                    double top = Math.Max(up, down);
                    if (top > h) { h = top; cell = StageCell.Cofferdam; }
                }
                z[i] = h;
                if (cell == StageCell.Natural && h <= Dam.FoundationElevation + 0.01) cell = StageCell.Sediment;
                cells[i] = cell;
            }

            double level = ReservoirLevel(date);
            if (!double.IsNaN(level)) Flood(z, cells, level);

            return new ElevationGrid(Natural.Columns, Natural.Rows, Natural.CellSize, Natural.OriginEasting, Natural.OriginNorthing, z);
        }

        private static double Coffer(double d, double center, double crest)
        {
            double beyond = Math.Abs(d - center) - CofferCrestWidth / 2.0;
            return beyond <= 0 ? crest : crest - beyond / CofferSlope;
        }

        /// <summary>상류(d &gt; 0)에서 시작점과 이어진, 수위보다 낮은 점을 수위로 채운다 (4방향).</summary>
        private void Flood(double[] z, StageCell[] cells, double level)
        {
            int c = Natural.Columns, r = Natural.Rows;
            if (!(z[_seed] < level) || _d[_seed] <= 0) return;
            var seen = new bool[z.Length];
            var queue = new Queue<int>();
            queue.Enqueue(_seed);
            seen[_seed] = true;
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                z[i] = level;
                cells[i] = StageCell.Reservoir;
                int col = i % c, row = i / c;
                TryAdd(col - 1, row); TryAdd(col + 1, row); TryAdd(col, row - 1); TryAdd(col, row + 1);
            }

            void TryAdd(int col, int row)
            {
                if (col < 0 || row < 0 || col >= c || row >= r) return;
                int j = row * c + col;
                if (seen[j] || _d[j] <= 0 || !(z[j] < level)) return;
                seen[j] = true;
                queue.Enqueue(j);
            }
        }

        // ── 그림용 위치 (자료 없음) ─────────────────────────────

        /// <summary>
        /// 가배수터널 중심선 (축 좌표 s, d): 왼쪽(−s) 양안 산 속. 입구는 상류 가물막이보다 상류, 출구는 하류 가물막이보다 하류.
        /// 터널 수(2개)와 순서는 자료 [K], 위치·길이는 그림용 (도면 없음). k = 0이면 1호, 1이면 2호.
        /// </summary>
        public List<double[]> TunnelPath(int k)
        {
            double inside = FloorLeftS - 110.0 - 40.0 * k;
            double edge = FloorLeftS - 15.0 - 15.0 * k;
            return new List<double[]>
            {
                new[] { edge, UpCofferD + 70.0 },
                new[] { inside, UpCofferD - 40.0 },
                new[] { inside, DownCofferD + 40.0 },
                new[] { edge, DownCofferD - 45.0 },
            };
        }

        /// <summary>여수로 중심선 s와 d 범위: 위성 지형에서 하류로 내려가는 낮은 길(s ≈ 350)에서 읽은 위치 (도면으로 검증되지 않음).</summary>
        public const double SpillwayS = 350.0, SpillwayWidth = 40.0, SpillwayTopD = 40.0, SpillwayEndD = -320.0;

        public double SpillwayBuilt(DateTime date) => Progress(date, SpillwayStart, SpillwayEnd);

        /// <summary>계곡 바닥 가운데 (s): 반 잘라 볼 때 자르는 자리</summary>
        public double FloorCenterS => (FloorLeftS + FloorRightS) / 2.0;
    }
}
