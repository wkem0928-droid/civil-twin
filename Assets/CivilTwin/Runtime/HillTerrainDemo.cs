using CivilTwin.Core;
using UnityEngine;

namespace CivilTwin.Runtime
{
    /// <summary>
    /// Phase 1-2 확인용 임시 코드.
    /// ▶(Play)를 누르면 수식 언덕 지형을 만들어 화면에 띄운다.
    /// 카메라 조작(1-3)과 실제 DEM(1-5)이 들어오면 이 파일은 교체한다.
    ///
    /// 시험 지형:
    ///   격자 101 × 101, 간격 5 m → 500 m × 500 m
    ///   남서쪽 모서리 : 동 200000, 북 600000
    ///   언덕 중심     : 동 200350, 북 600150, 기준표고 10 m, 높이 50 m, σ 100 m
    ///   로컬 원점     : 격자 한가운데 (동 200250, 북 600250, 표고 0)
    ///   → 꼭대기의 로컬 좌표는 (X 100, Y 60, Z -100)
    /// </summary>
    public static class HillTerrainDemo
    {
        public const string TerrainObjectName = "HillTerrain (demo)";

        public static ElevationGrid CreateGrid()
        {
            var hill = new GaussianHill(200350.0, 600150.0, 10.0, 50.0, 100.0);
            return hill.CreateGrid(101, 101, 5.0, 200000.0, 600000.0);
        }

        public static LocalFrame CreateFrame()
        {
            return new LocalFrame(200250.0, 600250.0, 0.0);
        }

        /// <summary>지형 메시를 가진 게임 오브젝트를 만든다.</summary>
        public static GameObject CreateTerrainObject()
        {
            return CreateTerrainObject(CreateGrid(), CreateFrame());
        }

        /// <summary>주어진 격자와 좌표 기준으로 지형 게임 오브젝트를 만든다.</summary>
        public static GameObject CreateTerrainObject(ElevationGrid grid, LocalFrame frame)
        {
            // 표고별 색칠: 낮은 곳은 초록, 높은 곳은 갈색·흰색. 평평한 면(수면 추정)은 파랑.
            bool[] flat = FlatSurfaceDetector.FindFlatNodes(grid);
            Texture2D colors = TerrainColorTextureFactory.Create(grid, ElevationColorRamp.CreateDefault(), flat);
            return CreateTerrainObject(grid, frame, colors, TerrainObjectName);
        }

        /// <summary>
        /// 주어진 격자를 메시로 만들고, 넘겨받은 색 그림을 입힌 게임 오브젝트를 만든다.
        /// 색 그림은 격자점 하나가 그림 점 하나여야 한다 (가로 = 열 개수, 세로 = 행 개수).
        /// </summary>
        public static GameObject CreateTerrainObject(ElevationGrid grid, LocalFrame frame, Texture2D colors, string name)
        {
            TerrainMeshData data = TerrainMeshBuilder.Build(grid, frame);
            Mesh mesh = TerrainMeshFactory.CreateMesh(data);

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new System.InvalidOperationException(
                    "셰이더 'Universal Render Pipeline/Lit'을 찾지 못했다. URP가 설치되어 있는지 확인한다.");
            }

            var material = new Material(shader) { name = "TerrainMaterial (demo)" };
            material.SetTexture("_BaseMap", colors);
            material.SetColor("_BaseColor", Color.white);   // 그림의 색을 그대로 쓴다
            material.SetFloat("_Smoothness", 0.05f);        // 번들거리지 않게

            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        /// <summary>언덕 전체가 보이도록 카메라를 남서쪽 위에 둔다.</summary>
        public static void PlaceCamera(Camera camera)
        {
            camera.transform.position = new Vector3(-300f, 260f, -480f);
            camera.transform.LookAt(new Vector3(50f, 20f, -50f));
            camera.nearClipPlane = 1f;
            camera.farClipPlane = 3000f;
        }

        /// <summary>격자점이 이 개수를 넘으면 이 PC(RX 570 4GB)에서 메시가 너무 무거워진다.</summary>
        public const long MaxGridPoints = 4_000_000;

        /// <summary>
        /// 지형 파일(.asc)을 읽는다. 읽지 못하거나 너무 크면 null을 돌려주고, 이유를 error에 담는다
        /// (콘솔에도 남긴다).
        /// </summary>
        public static ElevationGrid LoadDem(string path, out string error)
        {
            error = "";
            try
            {
                ElevationGrid grid = AscGridReader.ReadFile(path);

                long points = (long)grid.Columns * grid.Rows;
                if (points > MaxGridPoints)
                {
                    error = $"격자점이 너무 많다 ({points:N0}개, 한계 {MaxGridPoints:N0}개)";
                    Debug.LogError(
                        $"[CivilTwin] DEM이 너무 크다: {path}\n{grid.Columns} × {grid.Rows} = {points:N0} 격자점 " +
                        $"(한계 {MaxGridPoints:N0}). QGIS에서 범위를 줄이거나 칸 크기를 키워 다시 내보낸다.");
                    return null;
                }

                Debug.Log(
                    $"[CivilTwin] DEM 불러옴: {path}\n" +
                    $"격자 {grid.Columns} × {grid.Rows}, 칸 크기 {grid.CellSize} m, " +
                    $"범위 {grid.WidthMeters:N1} m × {grid.HeightMeters:N1} m, " +
                    $"표고 {grid.MinElevation:F2} ~ {grid.MaxElevation:F2} m, 빈 값 {grid.NoDataCount:N0}개");
                return grid;
            }
            catch (System.Exception e)
            {
                error = e.Message;
                Debug.LogError($"[CivilTwin] DEM을 읽지 못했다: {path}\n{e.Message}");
                return null;
            }
        }

        /// <summary>
        /// ▶(Play)를 누르면 자동으로 불린다. 주 카메라에 현장 전환 도구를 붙이고 첫 현장을 띄운다.
        /// 지형·카메라·측정 도구·범례를 만드는 일은 SiteSwitcher가 한다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateOnPlay()
        {
            if (GameObject.Find(TerrainObjectName) != null) return;

            Camera camera = Camera.main;
            if (camera == null)
            {
                Debug.LogError("[CivilTwin] 주 카메라(MainCamera 태그)가 없어 지형을 띄우지 못했다.");
                return;
            }

            // 배경 (2026-11-20 전체 검토): 기본 하늘 상자는 지평선 아래가 갈색빛 회색이라, 위에서 내려다보면
            // 지형 뒤가 흙탕물처럼 보였다 (1-5에서 원인을 못 찾고 남겨 둔 관찰). 지형이 잘 보이도록 옅은 하늘색 단색으로 바꾼다.
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.80f, 0.86f, 0.91f);

            var switcher = camera.GetComponent<SiteSwitcher>();
            if (switcher == null) switcher = camera.gameObject.AddComponent<SiteSwitcher>();
            switcher.Begin();
        }
    }
}
