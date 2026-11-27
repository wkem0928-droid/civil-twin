using CivilTwin.Core;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>
    /// 표고 격자 → 메시 데이터 변환을 검증한다.
    ///
    /// 작은 예제 (3열 × 2행, 간격 10 m, 원점 = 남서쪽 모서리):
    ///
    ///   row 1 (북) :  4   5   6        꼭짓점 번호:  3   4   5
    ///   row 0 (남) :  1   2   3                      0   1   2
    ///
    ///   꼭짓점 4 (col 1, row 1) → X = 10, Y = 5, Z = 10
    ///   칸 2개 → 삼각형 4개 → 번호 12개
    /// </summary>
    public class TerrainMeshBuilderTests
    {
        private const double Tolerance = 1e-6;

        private static TerrainMeshData BuildSmall()
        {
            double[] elevations = { 1, 2, 3, 4, 5, 6 };
            var grid = new ElevationGrid(3, 2, 10.0, 200000.0, 600000.0, elevations);
            var frame = new LocalFrame(200000.0, 600000.0, 0.0);
            return TerrainMeshBuilder.Build(grid, frame);
        }

        private static TerrainMeshData BuildHill()
        {
            var hill = new GaussianHill(200350.0, 600150.0, 10.0, 50.0, 100.0);
            ElevationGrid grid = hill.CreateGrid(101, 101, 5.0, 200000.0, 600000.0);
            // 로컬 원점을 격자 한가운데에 둔다.
            var frame = new LocalFrame(200250.0, 600250.0, 0.0);
            return TerrainMeshBuilder.Build(grid, frame);
        }

        [Test]
        public void 꼭짓점_개수는_열_곱하기_행이다()
        {
            Assert.AreEqual(6, BuildSmall().Vertices.Length);       // 3 × 2
            Assert.AreEqual(10201, BuildHill().Vertices.Length);    // 101 × 101
        }

        [Test]
        public void 삼각형_개수는_칸_수의_2배다()
        {
            Assert.AreEqual(4, BuildSmall().TriangleCount);         // 2 × 1 × 2
            Assert.AreEqual(20000, BuildHill().TriangleCount);      // 100 × 100 × 2
        }

        [Test]
        public void 꼭짓점_좌표는_격자점_좌표와_표고다()
        {
            TerrainMeshData mesh = BuildSmall();

            LocalPoint v0 = mesh.Vertices[0]; // col 0, row 0
            Assert.AreEqual(0.0, v0.X, Tolerance);
            Assert.AreEqual(1.0, v0.Y, Tolerance);
            Assert.AreEqual(0.0, v0.Z, Tolerance);

            LocalPoint v4 = mesh.Vertices[4]; // col 1, row 1
            Assert.AreEqual(10.0, v4.X, Tolerance, "X = 1 × 10");
            Assert.AreEqual(5.0, v4.Y, Tolerance, "표고");
            Assert.AreEqual(10.0, v4.Z, Tolerance, "Z = 1 × 10");

            LocalPoint v5 = mesh.Vertices[5]; // col 2, row 1
            Assert.AreEqual(20.0, v5.X, Tolerance, "X = 2 × 10");
            Assert.AreEqual(6.0, v5.Y, Tolerance, "표고");
            Assert.AreEqual(10.0, v5.Z, Tolerance, "Z = 1 × 10");
        }

        [Test]
        public void 첫_칸의_삼각형은_v00_v01_v11_그리고_v00_v11_v10이다()
        {
            int[] t = BuildSmall().Triangles;

            // 첫 칸: v00 = 0, v10 = 1, v01 = 3, v11 = 4
            Assert.AreEqual(new[] { 0, 3, 4, 0, 4, 1 }, new[] { t[0], t[1], t[2], t[3], t[4], t[5] });
        }

        [Test]
        public void 언덕_꼭대기_꼭짓점의_표고는_수식_값_60m다()
        {
            TerrainMeshData mesh = BuildHill();

            // 꼭대기 = col 70, row 30 → 번호 = 30 × 101 + 70 = 3100
            LocalPoint peak = mesh.Vertices[3100];

            // X = 200350 - 200250 = 100,  Z = 600150 - 600250 = -100,  Y = 60
            Assert.AreEqual(100.0, peak.X, Tolerance);
            Assert.AreEqual(60.0, peak.Y, Tolerance);
            Assert.AreEqual(-100.0, peak.Z, Tolerance);
        }

        [Test]
        public void 모든_꼭짓점의_표고가_격자_표고와_같다()
        {
            var hill = new GaussianHill(200350.0, 600150.0, 10.0, 50.0, 100.0);
            ElevationGrid grid = hill.CreateGrid(101, 101, 5.0, 200000.0, 600000.0);
            TerrainMeshData mesh = TerrainMeshBuilder.Build(grid, new LocalFrame(200250.0, 600250.0, 0.0));

            for (int row = 0; row < grid.Rows; row++)
            {
                for (int col = 0; col < grid.Columns; col++)
                {
                    double y = mesh.Vertices[row * grid.Columns + col].Y;
                    Assert.AreEqual(grid.GetElevation(col, row), y, 1e-12, $"col {col}, row {row}");
                }
            }
        }

        [Test]
        public void 모든_삼각형이_위쪽을_향한다()
        {
            TerrainMeshData mesh = BuildHill();

            for (int i = 0; i < mesh.Triangles.Length; i += 3)
            {
                LocalPoint a = mesh.Vertices[mesh.Triangles[i]];
                LocalPoint b = mesh.Vertices[mesh.Triangles[i + 1]];
                LocalPoint c = mesh.Vertices[mesh.Triangles[i + 2]];

                // 법선 = (b - a) × (c - a). 그 Y 성분이 양수면 윗면이 위를 향한다.
                double normalY = (b.Z - a.Z) * (c.X - a.X) - (b.X - a.X) * (c.Z - a.Z);

                Assert.Greater(normalY, 0.0, $"삼각형 {i / 3}");
            }
        }

        [Test]
        public void 삼각형_수평_면적의_합은_지형_전체_면적이다()
        {
            // 빈틈이나 겹침이 없다면, 위에서 본 삼각형 면적의 합 = 500 m × 500 m = 250,000 m²
            TerrainMeshData mesh = BuildHill();

            double total = 0.0;
            for (int i = 0; i < mesh.Triangles.Length; i += 3)
            {
                LocalPoint a = mesh.Vertices[mesh.Triangles[i]];
                LocalPoint b = mesh.Vertices[mesh.Triangles[i + 1]];
                LocalPoint c = mesh.Vertices[mesh.Triangles[i + 2]];

                // 수평 투영 삼각형의 면적 = |외적| / 2
                total += System.Math.Abs((b.Z - a.Z) * (c.X - a.X) - (b.X - a.X) * (c.Z - a.Z)) / 2.0;
            }

            Assert.AreEqual(250000.0, total, 1e-6);
        }

        [Test]
        public void 삼각형이_가리키는_꼭짓점_번호는_모두_범위_안이다()
        {
            TerrainMeshData mesh = BuildHill();

            foreach (int index in mesh.Triangles)
            {
                Assert.GreaterOrEqual(index, 0);
                Assert.Less(index, mesh.Vertices.Length);
            }
        }
    }
}
