using System.Collections.Generic;
using System.IO;
using CivilTwin.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CivilTwin.Runtime
{
    /// <summary>
    /// 현장 전환: Data/sites 폴더의 지형 파일(.asc)을 모두 찾아, 키를 눌러 넘겨 본다.
    ///   ] 또는 PageDown : 다음 현장      [ 또는 PageUp : 이전 현장
    /// 목록의 마지막에는 항상 "시험용 수식 언덕"이 있다 (높이를 수식으로 아는 검산용 지형).
    ///
    /// 현장이 바뀌면 지형·카메라·측정 도구·범례를 모두 새 지형에 맞춰 다시 만든다.
    /// 화면의 메시와 측정 도구가 반드시 같은 격자·같은 좌표 기준을 쓰게 하기 위해서다.
    /// 화면 표시는 임시(IMGUI)다. 나중에 UI Toolkit으로 바꾼다.
    /// </summary>
    public class SiteSwitcher : MonoBehaviour
    {
        private struct Entry
        {
            public SiteInfo Info;
            public string Path;   // null이면 수식 언덕
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private int _index = -1;
        private GameObject _terrain;
        private string _message = "";

        public int SiteCount => _entries.Count;
        public int CurrentIndex => _index;

        /// <summary>번호 i 현장의 이름 (현장 목록용)</summary>
        public string NameAt(int i) => _entries[i].Info.DisplayName;

        /// <summary>번호 i 현장의 지형 파일 이름 (수식 언덕이면 "")</summary>
        public string FileNameAt(int i) => _entries[i].Path != null ? Path.GetFileName(_entries[i].Path) : "";
        public string CurrentName => _index >= 0 ? _entries[_index].Info.DisplayName : "";

        /// <summary>지금 현장의 지형 파일 이름 (수식 언덕이면 "")</summary>
        public string CurrentFileName => _index >= 0 && _entries[_index].Path != null ? Path.GetFileName(_entries[_index].Path) : "";

        /// <summary>프로젝트 폴더/Data/sites</summary>
        public static string SitesFolder =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Data", "sites"));

        /// <summary>현장 목록을 만들고 첫 번째 현장을 띄운다.</summary>
        public void Begin()
        {
            _entries.Clear();
            HudMode.Reset();

            string folder = SitesFolder;
            if (Directory.Exists(folder))
            {
                var names = new List<string>();
                foreach (string path in Directory.GetFiles(folder, "*.asc"))
                {
                    names.Add(Path.GetFileName(path));
                }

                foreach (SiteInfo info in SiteCatalog.Arrange(names))
                {
                    _entries.Add(new Entry { Info = info, Path = Path.Combine(folder, info.FileName) });
                }
            }

            _entries.Add(new Entry { Info = new SiteInfo("", "", "시험용 수식 언덕"), Path = null });

            // 첫 현장을 띄운다. 읽지 못하면 다음 것으로 넘어간다 (마지막의 수식 언덕은 항상 성공한다).
            for (int i = 0; i < _entries.Count; i++)
            {
                if (Show(i)) return;
            }
        }

        private void Update()
        {
            HudMode.Poll();   // Tab: 글상자 켜기·끄기

            if (_entries.Count < 2) return;
            if (!InputGate.Allowed) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.rightBracketKey.wasPressedThisFrame || keyboard.pageDownKey.wasPressedThisFrame) Step(+1);
            if (keyboard.leftBracketKey.wasPressedThisFrame || keyboard.pageUpKey.wasPressedThisFrame) Step(-1);
        }

        /// <summary>다음(+1)·이전(−1) 현장. 키와 화면 메뉴가 함께 쓴다.</summary>
        public void Step(int step)
        {
            if (_entries.Count < 2 || step == 0) return;

            // 읽지 못하는 파일은 건너뛴다. 한 바퀴를 다 돌면 멈춘다.
            _message = "";
            int candidate = _index;
            for (int tries = 0; tries < _entries.Count - 1; tries++)
            {
                candidate = SiteCatalog.StepIndex(candidate, step, _entries.Count);
                if (Show(candidate)) return;
            }
        }

        /// <summary>파일 이름(예: hyundai_soyanggang.asc)으로 현장을 찾아 띄운다. 없거나 읽지 못하면 false.</summary>
        public bool ShowByFileName(string fileName)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Path != null
                    && string.Equals(Path.GetFileName(_entries[i].Path), fileName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return i == _index || Show(i);
                }
            }
            return false;
        }

        /// <summary>번호 index의 현장을 띄운다. 파일을 읽지 못하면 화면을 그대로 두고 false.</summary>
        public bool Show(int index)
        {
            Entry entry = _entries[index];

            string error = "";
            ElevationGrid grid = entry.Path == null
                ? HillTerrainDemo.CreateGrid()
                : HillTerrainDemo.LoadDem(entry.Path, out error);
            if (grid == null)
            {
                // 건너뛴 이유를 화면에 남긴다 (자세한 내용은 콘솔).
                _message = "읽지 못해 건너뜀: " + Path.GetFileName(entry.Path) + " — " + error;
                return false;
            }

            // 로컬 원점은 지형 한가운데, 표고 0. (수식 언덕이면 동 200250, 북 600250)
            var frame = new LocalFrame(
                grid.OriginEasting + grid.WidthMeters / 2.0,
                grid.OriginNorthing + grid.HeightMeters / 2.0,
                0.0);

            DestroyTerrain();
            _terrain = HillTerrainDemo.CreateTerrainObject(grid, frame);
            _index = index;

            Camera camera = GetComponent<Camera>();

            // 지형 크기에 맞춰 카메라 거리와 보이는 범위를 정한다.
            double size = System.Math.Max(grid.WidthMeters, grid.HeightMeters);
            camera.nearClipPlane = Mathf.Max(0.5f, (float)(size * 0.001));
            camera.farClipPlane = (float)(size * 20.0);

            // 궤도 카메라: 지형 한가운데를 바라보며 시작한다.
            // 방위각 35°(북동쪽을 봄), 고도각 30°, 거리 = 지형 크기 × 1.3 (언덕이면 650 m)
            var orbit = new OrbitCamera(
                new LocalPoint(0.0, (grid.MinElevation + grid.MaxElevation) / 2.0, 0.0),
                yawDegrees: 35.0,
                pitchDegrees: 30.0,
                distance: size * 1.3,
                minDistance: System.Math.Max(5.0, grid.CellSize * 2.0),
                maxDistance: size * 6.0);

            GetOrAdd<OrbitCameraController>().Initialize(orbit);
            GetOrAdd<MeasureTool>().Initialize(grid, frame);

            // 부지 계획 도구: 현장이 바뀌면 이전 계획은 지우고 새 지형으로 다시 시작한다.
            GradingPlanTool grading = GetOrAdd<GradingPlanTool>();
            grading.Initialize(grid, frame, _terrain);

            // 건물 시공 도구 (Phase 3): 부지 계획 위에 건물과 장비를 올린다.
            BuildingSiteTool building = GetOrAdd<BuildingSiteTool>();
            building.Initialize(grading);

            // 시공 대안 비교 도구 (Phase 4): 조건을 A / B / C로 저장해 공기와 비용을 나란히 본다.
            GetOrAdd<AlternativeTool>().Initialize(grading, building);

            // 터널 굴착 4D (2026-11-21): 터널 현장에서 [부지] → ○○터널 굴착. 현장이 바뀌면 끈다.
            GetOrAdd<TunnelTool>().Initialize(grid, frame, grading);

            // 화면 저장 (F12): Claude가 파일로 화면을 확인하는 데 쓴다.
            GetOrAdd<ScreenCaptureKey>();

            // 화면 메뉴 (5-6): 키를 몰라도 버튼으로 같은 동작을 한다.
            GetOrAdd<ControlBar>().Initialize(this, grading, building, GetComponent<AlternativeTool>());
            GetOrAdd<ScheduleView>().Initialize(this);   // 댐 네트워크 공정표 (2026-11-22)
            GetOrAdd<DamSectionView>().Initialize(this);    // 댐 시공 단계 · 존 단면 (2026-11-22)
            GetOrAdd<DamStage3D>().Initialize(grid, frame, GetComponent<DamSectionView>());   // 같은 단계의 3D (2026-11-22)
            GetOrAdd<BridgeStage3D>().Initialize(frame, GetComponent<DamSectionView>());     // 교량 단계 3D (2026-11-27, 인천대교)
            HudLayout.Reset();

            // 범례: 색이 몇 m를 뜻하는지 보여 준다. 현장마다 표고 범위가 다르므로 다시 만든다.
            int flatCount = FlatSurfaceDetector.Count(FlatSurfaceDetector.FindFlatNodes(grid));
            GetOrAdd<ElevationLegend>().Initialize(
                ElevationColorRamp.CreateDefault(), grid.MinElevation, grid.MaxElevation, flatCount);

            return true;
        }

        private T GetOrAdd<T>() where T : Component
        {
            T component = GetComponent<T>();
            return component != null ? component : gameObject.AddComponent<T>();
        }

        /// <summary>이전 지형과 거기에 딸린 메시·재질·색 그림을 지운다 (지우지 않으면 메모리에 계속 쌓인다).</summary>
        private void DestroyTerrain()
        {
            if (_terrain == null) return;

            Mesh mesh = _terrain.GetComponent<MeshFilter>().sharedMesh;
            Material material = _terrain.GetComponent<MeshRenderer>().sharedMaterial;
            Texture colors = material != null ? material.GetTexture("_BaseMap") : null;

            // 같은 이름의 지형이 한 프레임 동안 둘이 되지 않도록 이름을 먼저 바꾼다.
            _terrain.name = "(지우는 중)";
            Destroy(_terrain);
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
            if (colors != null) Destroy(colors);
            _terrain = null;
        }

        private void OnDestroy()
        {
            DestroyTerrain();
        }

        // 현장 이름은 화면 메뉴(ControlBar) 맨 위 줄에 나온다 (5-6). 예전의 가운데 위 글상자는 없앴다.

        /// <summary>"회사 · 현장   (번호 / 전체)" — 메뉴 표시용</summary>
        public string SiteLabel => _index < 0 ? "" : $"{CurrentName}   ({_index + 1} / {_entries.Count})";

        /// <summary>파일을 읽지 못해 건너뛴 경우의 안내 (없으면 빈 글)</summary>
        public string Message => _message;

        public bool CanStep => _entries.Count > 1;
    }
}
