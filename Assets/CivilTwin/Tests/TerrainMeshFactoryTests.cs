using System.IO;
using CivilTwin.Core;
using CivilTwin.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CivilTwin.Tests
{
    /// <summary>
    /// Core의 메시 데이터가 Unity Mesh로 손실 없이 옮겨지는지 확인한다.
    /// float로 바뀌므로 허용 오차는 1 mm로 둔다.
    /// </summary>
    public class TerrainMeshFactoryTests
    {
        private const float ToleranceMeters = 0.001f;

        private static Mesh CreateHillMesh()
        {
            TerrainMeshData data = TerrainMeshBuilder.Build(
                HillTerrainDemo.CreateGrid(),
                HillTerrainDemo.CreateFrame());
            return TerrainMeshFactory.CreateMesh(data);
        }

        [Test]
        public void Unity_메시의_꼭짓점과_삼각형_개수가_같다()
        {
            Mesh mesh = CreateHillMesh();
            try
            {
                Assert.AreEqual(10201, mesh.vertexCount);
                Assert.AreEqual(20000 * 3, mesh.triangles.Length);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void Unity_메시의_꼭대기_꼭짓점이_로컬_100_60_마이너스100이다()
        {
            Mesh mesh = CreateHillMesh();
            try
            {
                Vector3 peak = mesh.vertices[3100]; // 30 × 101 + 70

                Assert.AreEqual(100f, peak.x, ToleranceMeters);
                Assert.AreEqual(60f, peak.y, ToleranceMeters);
                Assert.AreEqual(-100f, peak.z, ToleranceMeters);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void Unity_메시의_범위는_500m_곱하기_500m이고_최고점은_60m다()
        {
            Mesh mesh = CreateHillMesh();
            try
            {
                Bounds b = mesh.bounds;

                Assert.AreEqual(500f, b.size.x, ToleranceMeters, "동서 길이");
                Assert.AreEqual(500f, b.size.z, ToleranceMeters, "남북 길이");
                Assert.AreEqual(60f, b.max.y, ToleranceMeters, "최고 표고");
                Assert.AreEqual(-250f, b.min.x, ToleranceMeters, "서쪽 끝");
                Assert.AreEqual(250f, b.max.z, ToleranceMeters, "북쪽 끝");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void Unity_메시의_법선은_모두_위쪽을_향한다()
        {
            Mesh mesh = CreateHillMesh();
            try
            {
                foreach (Vector3 n in mesh.normals)
                {
                    Assert.Greater(n.y, 0f);
                }
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void 색_그림의_점_하나가_격자점_하나의_색이다()
        {
            // 2 × 2 격자 (남쪽 줄부터): 100, 300 / 150, 빈 값.  최저 100, 최고 300.
            //   (0,0) 100 m → t 0    → 짙은 초록 (0.20, 0.42, 0.24)
            //   (1,0) 300 m → t 1    → 흰색에 가까운 회색 (0.95, 0.95, 0.93)
            //   (0,1) 150 m → t 0.25 → 연두 (0.56, 0.68, 0.36)
            //   (1,1) 빈 값          → 회색 (0.50, 0.50, 0.50)
            var grid = new ElevationGrid(2, 2, 10.0, 0.0, 0.0, new[] { 100.0, 300.0, 150.0, double.NaN });
            Texture2D texture = TerrainColorTextureFactory.Create(grid, ElevationColorRamp.CreateDefault(), null);
            try
            {
                const float tol = 0.003f; // 색은 0 ~ 255 정수로 저장되므로 1/255 ≈ 0.004 보다 작은 오차가 생긴다

                Assert.AreEqual(2, texture.width);
                Assert.AreEqual(2, texture.height);

                Color a = texture.GetPixel(0, 0);
                Assert.AreEqual(0.20f, a.r, tol); Assert.AreEqual(0.42f, a.g, tol); Assert.AreEqual(0.24f, a.b, tol);

                Color b = texture.GetPixel(1, 0);
                Assert.AreEqual(0.95f, b.r, tol); Assert.AreEqual(0.95f, b.g, tol); Assert.AreEqual(0.93f, b.b, tol);

                Color c = texture.GetPixel(0, 1);
                Assert.AreEqual(0.56f, c.r, tol); Assert.AreEqual(0.68f, c.g, tol); Assert.AreEqual(0.36f, c.b, tol);

                Color d = texture.GetPixel(1, 1);
                Assert.AreEqual(0.50f, d.r, tol); Assert.AreEqual(0.50f, d.g, tol); Assert.AreEqual(0.50f, d.b, tol);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void 평평한_면으로_표시된_점은_수면_색이다()
        {
            var grid = new ElevationGrid(2, 2, 10.0, 0.0, 0.0, new[] { 100.0, 300.0, 150.0, 200.0 });
            var flat = new[] { false, false, false, true };
            Texture2D texture = TerrainColorTextureFactory.Create(grid, ElevationColorRamp.CreateDefault(), flat);
            try
            {
                Color water = texture.GetPixel(1, 1);
                Assert.AreEqual(0.25f, water.r, 0.003f);
                Assert.AreEqual(0.47f, water.g, 0.003f);
                Assert.AreEqual(0.70f, water.b, 0.003f);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void 꼭짓점은_색_그림에서_자기_격자점의_한가운데를_가리킨다()
        {
            // 101 × 101 격자. col 70, row 30 의 위치 = ((70 + 0.5) / 101, (30 + 0.5) / 101)
            Mesh mesh = CreateHillMesh();
            try
            {
                Vector2 uv = mesh.uv[3100];
                Assert.AreEqual(70.5f / 101f, uv.x, 1e-6f);
                Assert.AreEqual(30.5f / 101f, uv.y, 1e-6f);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        /// <summary>
        /// 지형을 실제로 그려서 Logs/terrain-preview.png 로 저장한다.
        /// 숫자 검증이 아니라, 화면에 무엇이 그려지는지 사람이 확인하기 위한 것이다.
        /// 그림이 한 가지 색으로만 채워졌다면(아무것도 안 그려짐) 실패로 본다.
        ///
        /// 알려진 문제 (2026-11-12, 원인 미확인):
        ///   이 PC의 배치 모드(Vulkan, 2019 드라이버)에서는 저장된 이미지의 색이 틀리게 나온다.
        ///   하늘색 배경이 연두색으로 저장된다. 지형의 모양과 위치는 맞다.
        ///   그래서 이 이미지로는 "모양"만 확인하고, 색은 Unity 화면에서 직접 본다.
        ///   자세한 내용: Docs/TROUBLESHOOTING.md #2
        /// </summary>
        [Test]
        public void 미리보기_이미지를_저장한다()
        {
            const int width = 1280;
            const int height = 720;

            GameObject terrain = HillTerrainDemo.CreateTerrainObject();
            var cameraObject = new GameObject("PreviewCamera (test)");
            var lightObject = new GameObject("PreviewLight (test)");
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var image = new Texture2D(width, height, TextureFormat.RGBA32, false);
            RenderTexture previous = RenderTexture.active;

            try
            {
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.2f;
                lightObject.transform.rotation = Quaternion.Euler(40f, 60f, 0f);

                var camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.60f, 0.75f, 0.90f);
                HillTerrainDemo.PlaceCamera(camera);
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.targetTexture = target;
                camera.Render();

                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();

                string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "terrain-preview.png"));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, image.EncodeToPNG());

                // 가운데 픽셀(지형)과 맨 위 가운데 픽셀(하늘)의 색이 달라야 한다.
                Color center = image.GetPixel(width / 2, height / 2);
                Color top = image.GetPixel(width / 2, height - 5);
                float difference = Mathf.Abs(center.r - top.r) + Mathf.Abs(center.g - top.g) + Mathf.Abs(center.b - top.b);

                Assert.Greater(difference, 0.05f, $"지형이 그려지지 않은 것 같다. 저장 위치: {path}");
            }
            finally
            {
                RenderTexture.active = previous;
                Mesh mesh = terrain.GetComponent<MeshFilter>().sharedMesh;
                Material material = terrain.GetComponent<MeshRenderer>().sharedMaterial;
                Texture colors = material.GetTexture("_BaseMap");
                Object.DestroyImmediate(terrain);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(colors);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(lightObject);
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(image);
            }
        }
    }
}
