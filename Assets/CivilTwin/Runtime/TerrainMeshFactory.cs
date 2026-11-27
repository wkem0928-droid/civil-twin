using System;
using CivilTwin.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace CivilTwin.Runtime
{
    /// <summary>
    /// Core가 계산한 메시 데이터(double)를 Unity Mesh(float)로 옮긴다.
    /// 여기서는 계산을 하지 않는다. 형식만 바꾼다.
    /// </summary>
    public static class TerrainMeshFactory
    {
        public static Mesh CreateMesh(TerrainMeshData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            int vertexCount = data.Vertices.Length;
            var vertices = new Vector3[vertexCount];
            var uv = new Vector2[vertexCount];

            for (int row = 0; row < data.Rows; row++)
            {
                for (int col = 0; col < data.Columns; col++)
                {
                    int i = row * data.Columns + col;
                    vertices[i] = data.Vertices[i].ToVector3();
                    // 색 그림(텍스처)에서 이 꼭짓점이 가리키는 위치. 0 ~ 1 사이 값이다.
                    // 그림의 점 하나가 격자점 하나이고, col번째 점의 중심은 (col + 0.5) / 열 개수 에 있다.
                    uv[i] = new Vector2(
                        (col + 0.5f) / data.Columns,
                        (row + 0.5f) / data.Rows);
                }
            }

            var mesh = new Mesh { name = "TerrainMesh" };

            // 꼭짓점이 65,535개를 넘으면 번호를 32비트로 저장해야 한다.
            mesh.indexFormat = vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;

            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = data.Triangles;

            // 법선(면이 향하는 방향)은 조명 계산에 쓰인다. Unity가 삼각형에서 계산한다.
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
