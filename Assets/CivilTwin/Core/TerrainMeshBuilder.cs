using System;

namespace CivilTwin.Core
{
    /// <summary>
    /// 지형 메시의 재료: 꼭짓점 목록과 삼각형 목록.
    /// Unity에 의존하지 않는 순수 데이터라서 자동 테스트로 검증할 수 있다.
    /// </summary>
    public sealed class TerrainMeshData
    {
        /// <summary>동서 방향 꼭짓점 개수</summary>
        public int Columns { get; }

        /// <summary>남북 방향 꼭짓점 개수</summary>
        public int Rows { get; }

        /// <summary>꼭짓점 좌표 (로컬 좌표, m). 번호 = row × Columns + col</summary>
        public LocalPoint[] Vertices { get; }

        /// <summary>삼각형 목록. 꼭짓점 번호 3개가 삼각형 1개다.</summary>
        public int[] Triangles { get; }

        public TerrainMeshData(int columns, int rows, LocalPoint[] vertices, int[] triangles)
        {
            Columns = columns;
            Rows = rows;
            Vertices = vertices ?? throw new ArgumentNullException(nameof(vertices));
            Triangles = triangles ?? throw new ArgumentNullException(nameof(triangles));
        }

        /// <summary>삼각형 개수</summary>
        public int TriangleCount => Triangles.Length / 3;
    }

    /// <summary>
    /// 표고 격자를 삼각형 메시로 바꾼다.
    ///
    /// 꼭짓점:
    ///   격자점 하나가 꼭짓점 하나가 된다. 표고 값은 그대로 쓴다 (보간하지 않는다).
    ///   개수 = 열 × 행
    ///
    /// 삼각형:
    ///   격자 한 칸(사각형)을 대각선으로 잘라 삼각형 2개로 만든다.
    ///   개수 = (열 - 1) × (행 - 1) × 2   (빈 값 격자점에 닿는 삼각형은 뺀다)
    ///
    ///     v01 ---- v11        v00 = 남서, v10 = 남동
    ///      |     / |          v01 = 북서, v11 = 북동
    ///      |   /   |
    ///      | /     |          삼각형 1: v00 → v01 → v11
    ///     v00 ---- v10        삼각형 2: v00 → v11 → v10
    ///
    ///   꼭짓점을 위에서 볼 때 시계 방향으로 나열한다.
    ///   Unity는 시계 방향으로 보이는 쪽을 앞면으로 그리므로, 지형의 윗면이 보인다.
    /// </summary>
    public static class TerrainMeshBuilder
    {
        public static TerrainMeshData Build(ElevationGrid grid, LocalFrame frame)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));

            int columns = grid.Columns;
            int rows = grid.Rows;

            // 빈 값(NaN) 격자점은 삼각형에 쓰지 않는다. 꼭짓점 번호 체계(row × Columns + col)를
            // 유지하려고 자리는 남겨 두되, 좌표에 NaN이 들어가지 않도록 최저 표고를 넣어 둔다.
            var valid = new bool[columns * rows];
            var vertices = new LocalPoint[columns * rows];
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < columns; col++)
                {
                    int i = row * columns + col;
                    double elevation = grid.GetElevation(col, row);
                    valid[i] = !double.IsNaN(elevation);

                    vertices[i] = frame.ToLocal(
                        grid.GetEasting(col),
                        grid.GetNorthing(row),
                        valid[i] ? elevation : grid.MinElevation);
                }
            }

            var triangles = new int[(columns - 1) * (rows - 1) * 6];
            int t = 0;
            for (int row = 0; row < rows - 1; row++)
            {
                for (int col = 0; col < columns - 1; col++)
                {
                    int v00 = row * columns + col;   // 남서
                    int v10 = v00 + 1;               // 남동
                    int v01 = v00 + columns;         // 북서
                    int v11 = v01 + 1;               // 북동

                    // 세 꼭짓점이 모두 유효한 삼각형만 만든다.
                    if (valid[v00] && valid[v01] && valid[v11])
                    {
                        triangles[t++] = v00;
                        triangles[t++] = v01;
                        triangles[t++] = v11;
                    }

                    if (valid[v00] && valid[v11] && valid[v10])
                    {
                        triangles[t++] = v00;
                        triangles[t++] = v11;
                        triangles[t++] = v10;
                    }
                }
            }

            if (t < triangles.Length) Array.Resize(ref triangles, t);

            return new TerrainMeshData(columns, rows, vertices, triangles);
        }
    }
}
