using System;
using System.Globalization;
using System.Text;
using CivilTwin.Core;
using NUnit.Framework;

namespace CivilTwin.Tests
{
    /// <summary>
    /// .asc 파일 읽기를 손으로 만든 작은 파일과 비교한다.
    ///
    /// 시험 파일 (4열 × 3행, 칸 크기 10 m, 남서쪽 칸의 모서리 = 동 200000, 북 600000):
    ///
    ///   파일에 적힌 순서            지도에서의 위치
    ///   1  2  3  4   ← 첫 줄        북쪽 행 (칸 중심 북 600025)
    ///   5  6  7  8                  가운데 행 (북 600015)
    ///   9 10 11 12   ← 끝 줄        남쪽 행 (북 600005)
    ///
    /// 손으로 구한 정답:
    ///   남서쪽 격자점 좌표 = 모서리 + 반 칸 = (200005, 600005)
    ///   격자 (col 0, row 0) = 남서 =  9      격자 (col 3, row 0) = 남동 = 12
    ///   격자 (col 0, row 2) = 북서 =  1      격자 (col 3, row 2) = 북동 =  4
    /// </summary>
    public class AscGridReaderTests
    {
        private const double Tolerance = 1e-9;

        private const string CornerFile =
            "ncols 4\n" +
            "nrows 3\n" +
            "xllcorner 200000\n" +
            "yllcorner 600000\n" +
            "cellsize 10\n" +
            "NODATA_value -9999\n" +
            "1 2 3 4\n" +
            "5 6 7 8\n" +
            "9 10 11 12\n";

        [Test]
        public void 머리말의_크기와_칸_크기를_읽는다()
        {
            ElevationGrid grid = AscGridReader.Parse(CornerFile);

            Assert.AreEqual(4, grid.Columns);
            Assert.AreEqual(3, grid.Rows);
            Assert.AreEqual(10.0, grid.CellSize, Tolerance);
            Assert.AreEqual(0, grid.NoDataCount);
        }

        [Test]
        public void 파일의_첫_줄은_북쪽_행이다()
        {
            ElevationGrid grid = AscGridReader.Parse(CornerFile);

            Assert.AreEqual(9.0, grid.GetElevation(0, 0), Tolerance, "남서");
            Assert.AreEqual(12.0, grid.GetElevation(3, 0), Tolerance, "남동");
            Assert.AreEqual(1.0, grid.GetElevation(0, 2), Tolerance, "북서");
            Assert.AreEqual(4.0, grid.GetElevation(3, 2), Tolerance, "북동");
            Assert.AreEqual(7.0, grid.GetElevation(2, 1), Tolerance, "가운데 행, col 2");
        }

        [Test]
        public void xllcorner는_칸의_모서리라서_격자점은_반_칸_안쪽이다()
        {
            ElevationGrid grid = AscGridReader.Parse(CornerFile);

            // 200000 + 10 / 2 = 200005,  600000 + 10 / 2 = 600005
            Assert.AreEqual(200005.0, grid.OriginEasting, Tolerance);
            Assert.AreEqual(600005.0, grid.OriginNorthing, Tolerance);

            // 북동쪽 격자점: 200005 + 3 × 10 = 200035,  600005 + 2 × 10 = 600025
            Assert.AreEqual(200035.0, grid.GetEasting(3), Tolerance);
            Assert.AreEqual(600025.0, grid.GetNorthing(2), Tolerance);
        }

        [Test]
        public void xllcenter는_칸의_중심이라서_그대로_격자점이다()
        {
            string centerFile = CornerFile
                .Replace("xllcorner 200000", "xllcenter 200005")
                .Replace("yllcorner 600000", "yllcenter 600005");

            ElevationGrid grid = AscGridReader.Parse(centerFile);

            Assert.AreEqual(200005.0, grid.OriginEasting, Tolerance);
            Assert.AreEqual(600005.0, grid.OriginNorthing, Tolerance);
        }

        [Test]
        public void 칸_중심의_표고는_파일_값_그대로다()
        {
            ElevationGrid grid = AscGridReader.Parse(CornerFile);

            // 값 7 이 적힌 칸: col 2, 가운데 행 → 중심 (200025, 600015)
            Assert.IsTrue(grid.TryGetElevationAt(200025.0, 600015.0, out double z));
            Assert.AreEqual(7.0, z, Tolerance);
        }

        [Test]
        public void 빈_값은_표고가_없는_것으로_다룬다()
        {
            // 값 6 (col 1, 가운데 행)을 빈 값으로 바꾼다.
            string file = CornerFile.Replace("5 6 7 8", "5 -9999 7 8");

            ElevationGrid grid = AscGridReader.Parse(file);

            Assert.AreEqual(1, grid.NoDataCount);
            Assert.IsTrue(grid.IsNoData(1, 1));
            Assert.IsFalse(grid.IsNoData(0, 1));

            // 최저·최고 표고에 -9999 가 섞이지 않는다.
            Assert.AreEqual(1.0, grid.MinElevation, Tolerance);
            Assert.AreEqual(12.0, grid.MaxElevation, Tolerance);

            // 빈 값 격자점 바로 옆(같은 삼각형 안)은 표고가 없다.
            Assert.IsFalse(grid.TryGetElevationAt(200016.0, 600016.0, out _));
        }

        [Test]
        public void 빈_값에_닿는_삼각형은_메시에서_빠진다()
        {
            // 칸 6개 → 삼각형 12개. 빈 값 격자점 (col 1, row 1) 에 닿는 삼각형은 6개:
            //   남서 칸 2개, 남동 칸 1개, 북서 칸 1개, 북동 칸 2개
            // → 12 - 6 = 6개가 남는다.
            string file = CornerFile.Replace("5 6 7 8", "5 -9999 7 8");
            ElevationGrid grid = AscGridReader.Parse(file);

            TerrainMeshData mesh = TerrainMeshBuilder.Build(grid, new LocalFrame(200000.0, 600000.0, 0.0));

            Assert.AreEqual(6, mesh.TriangleCount);

            int noDataVertex = 1 * 4 + 1;
            foreach (int index in mesh.Triangles)
            {
                Assert.AreNotEqual(noDataVertex, index, "빈 값 꼭짓점을 쓰는 삼각형이 남아 있다");
            }
            foreach (LocalPoint v in mesh.Vertices)
            {
                Assert.IsFalse(double.IsNaN(v.Y), "꼭짓점 좌표에 NaN이 들어가면 안 된다");
            }
        }

        [Test]
        public void 빈_값이_있으면_시선은_그_구멍을_지나간다()
        {
            string file = CornerFile.Replace("5 6 7 8", "5 -9999 7 8");
            ElevationGrid grid = AscGridReader.Parse(file);

            // 빈 값 격자점 (200015, 600015) 바로 위에서 수직으로 내려다본다.
            var origin = new SurveyPoint(200015.0, 600015.0, 100.0);

            Assert.IsFalse(TerrainRaycaster.TryHit(grid, origin, 0, 0, -1, 1000.0, out _));
        }

        [Test]
        public void 머리말_대소문자와_빈칸_탭_줄바꿈_모양이_달라도_읽는다()
        {
            string messy =
                "NCOLS\t4\r\n" +
                "NROWS   3\r\n" +
                "XLLCORNER  200000.0\r\n" +
                "YLLCORNER  600000.0\r\n" +
                "CELLSIZE   10.0\r\n" +
                "nodata_value  -9999\r\n" +
                "  1 2\r\n3 4 5 6 7 8\r\n\r\n 9.0 1.0e1 11 12   ";

            ElevationGrid grid = AscGridReader.Parse(messy);

            Assert.AreEqual(9.0, grid.GetElevation(0, 0), Tolerance);
            Assert.AreEqual(10.0, grid.GetElevation(1, 0), Tolerance, "1.0e1 = 10");
            Assert.AreEqual(4.0, grid.GetElevation(3, 2), Tolerance);
        }

        [Test]
        public void NODATA_value_줄이_없어도_읽는다()
        {
            string file = CornerFile.Replace("NODATA_value -9999\n", "");

            ElevationGrid grid = AscGridReader.Parse(file);

            Assert.AreEqual(0, grid.NoDataCount);
            Assert.AreEqual(12.0, grid.GetElevation(3, 0), Tolerance);
        }

        [Test]
        public void dx와_dy가_같으면_cellsize_대신_쓴다()
        {
            string file = CornerFile.Replace("cellsize 10\n", "dx 10\ndy 10\n");

            Assert.AreEqual(10.0, AscGridReader.Parse(file).CellSize, Tolerance);
        }

        [Test]
        public void dx와_dy가_다르면_거부한다()
        {
            // 위경도(도 단위) 그대로 내보내면 가로·세로 칸 크기가 달라진다.
            string file = CornerFile.Replace("cellsize 10\n", "dx 10\ndy 12\n");

            var e = Assert.Throws<FormatException>(() => AscGridReader.Parse(file));
            StringAssert.Contains("dx", e.Message);
        }

        [Test]
        public void 표고_값이_모자라면_거부한다()
        {
            string file = CornerFile.Replace("9 10 11 12\n", "9 10 11\n");

            var e = Assert.Throws<FormatException>(() => AscGridReader.Parse(file));
            StringAssert.Contains("11개", e.Message);
        }

        [Test]
        public void 표고_값이_남으면_거부한다()
        {
            Assert.Throws<FormatException>(() => AscGridReader.Parse(CornerFile + "13\n"));
        }

        [Test]
        public void 머리말에_ncols가_없으면_거부한다()
        {
            string file = CornerFile.Replace("ncols 4\n", "");

            var e = Assert.Throws<FormatException>(() => AscGridReader.Parse(file));
            StringAssert.Contains("ncols", e.Message);
        }

        [Test]
        public void 숫자가_아닌_표고_값은_거부한다()
        {
            string file = CornerFile.Replace("5 6 7 8", "5 6,5 7 8");

            var e = Assert.Throws<FormatException>(() => AscGridReader.Parse(file));
            StringAssert.Contains("6,5", e.Message);
        }

        /// <summary>
        /// 언덕 격자를 .asc 글자로 적었다가 다시 읽으면 원래 격자와 같아야 한다.
        /// 남북 뒤집기나 반 칸 이동을 하나라도 틀리면 이 시험에서 걸린다.
        /// (언덕 중심을 한쪽으로 치우치게 두었기 때문에 뒤집힘이 값의 차이로 드러난다.)
        /// </summary>
        [Test]
        public void 언덕_격자를_asc로_적었다가_읽으면_원래와_같다()
        {
            ElevationGrid original = new GaussianHill(200350.0, 600150.0, 10.0, 50.0, 100.0)
                .CreateGrid(101, 101, 5.0, 200000.0, 600000.0);

            ElevationGrid loaded = AscGridReader.Parse(WriteAsc(original));

            Assert.AreEqual(original.Columns, loaded.Columns);
            Assert.AreEqual(original.Rows, loaded.Rows);
            Assert.AreEqual(original.CellSize, loaded.CellSize, Tolerance);
            Assert.AreEqual(original.OriginEasting, loaded.OriginEasting, Tolerance);
            Assert.AreEqual(original.OriginNorthing, loaded.OriginNorthing, Tolerance);

            for (int row = 0; row < original.Rows; row++)
            {
                for (int col = 0; col < original.Columns; col++)
                {
                    Assert.AreEqual(
                        original.GetElevation(col, row),
                        loaded.GetElevation(col, row),
                        1e-9,
                        $"col {col}, row {row}");
                }
            }

            // 꼭대기는 여전히 col 70, row 30 이고 표고 60 m 다.
            Assert.AreEqual(60.0, loaded.GetElevation(70, 30), 1e-6);
        }

        /// <summary>시험용: 격자를 .asc 글자로 적는다 (북쪽 행부터, xllcorner 기준).</summary>
        private static string WriteAsc(ElevationGrid grid)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append("ncols ").Append(grid.Columns.ToString(inv)).Append('\n');
            sb.Append("nrows ").Append(grid.Rows.ToString(inv)).Append('\n');
            sb.Append("xllcorner ").Append((grid.OriginEasting - grid.CellSize / 2.0).ToString("R", inv)).Append('\n');
            sb.Append("yllcorner ").Append((grid.OriginNorthing - grid.CellSize / 2.0).ToString("R", inv)).Append('\n');
            sb.Append("cellsize ").Append(grid.CellSize.ToString("R", inv)).Append('\n');
            sb.Append("NODATA_value -9999\n");

            for (int row = grid.Rows - 1; row >= 0; row--)
            {
                for (int col = 0; col < grid.Columns; col++)
                {
                    if (col > 0) sb.Append(' ');
                    sb.Append(grid.GetElevation(col, row).ToString("R", inv));
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}
