using System;
using System.Collections.Generic;
using System.Globalization;
using CivilTwin.Core;
using UnityEngine;

namespace CivilTwin.Runtime
{
    /// <summary>
    /// 소양강댐 시공 단계별 3D (2026-11-22, 설계안 4단계). 존 단면 화면에서 [3D로 보기]를 누르면 같은 날짜·단계를 3D로 그린다.
    ///
    /// 지형(그날의 계곡·강·가물막이·굴착·저수지)은 Core/DamStageModel, 댐 몸체는 여기서 따로 만든다:
    ///   - 사력(겉): 축 좌표 (s, d) 격자에서 높이 = min(성토 높이, 완성 댐 표면). 땅보다 낮은 부분은 지형에 가려진다.
    ///   - 코어·필터: 성토 윗면에 띠로 칠한다 (실제 공사 중 윗면에 보이는 모습). 폭은 DamZoneSection.
    ///   - [반 잘라 보기]: 계곡 가운데에서 댐을 잘라 단면(존 색)을 보여 준다.
    ///   - 여수로: 위성 지형의 낮은 길(s ≈ 350)에 콘크리트 띠가 공정에 맞춰 늘어난다 (위치는 추정).
    /// 땅속의 것(가배수터널, 그라우트 커튼)과 이름표는 화면 위에 점선·글로 그린다 (OnGUI).
    /// 3D가 켜져 있는 동안 다른 지형·모형은 숨기고, 끄면 되돌린다.
    /// </summary>
    public sealed class DamStage3D : MonoBehaviour
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        public static readonly Color CoreColor = new Color(0.78f, 0.50f, 0.28f);
        public static readonly Color FilterColor = new Color(0.93f, 0.84f, 0.55f);
        public static readonly Color ShellColor = new Color(0.52f, 0.52f, 0.51f);
        public static readonly Color ConcreteColor = new Color(0.88f, 0.89f, 0.91f);
        private static readonly Color PortalColor = new Color(0.25f, 0.25f, 0.27f);

        private static readonly RgbColor SedimentRgb = new RgbColor(0.66, 0.58, 0.42);
        private static readonly RgbColor RockRgb = new RgbColor(0.40, 0.36, 0.33);
        private static readonly RgbColor RiverRgb = new RgbColor(0.28, 0.52, 0.78);
        private static readonly RgbColor ReservoirRgb = new RgbColor(0.20, 0.40, 0.66);
        private static readonly RgbColor CofferRgb = new RgbColor(0.55, 0.47, 0.36);

        private const double TerrainCell = 12.0, DamStepS = 6.0, DamStepD = 3.0;

        private ElevationGrid _surveyed;
        private LocalFrame _frame;
        private DamSectionView _view;
        private DamStageModel _model;
        private ElevationGrid _stage;
        private GameObject _root, _terrain;
        private readonly Dictionary<Color, Material> _materials = new Dictionary<Color, Material>();
        private readonly List<Renderer> _hidden = new List<Renderer>();
        private DateTime _shownDate = DateTime.MinValue;
        private bool _shownCut, _visible;
        private GUIStyle _tag;
        private DamFleet3D _fleet;

        /// <summary>장비를 그릴지 ([장비 숨기기]로 끈다)</summary>
        public bool ShowFleet { get; set; } = true;

        /// <summary>지금 보이는 장비 요약 ("굴착기 4, 덤프트럭 6", 없으면 빈 글)</summary>
        public string FleetText { get; private set; } = "";

        /// <summary>장비가 움직이는 시계 (자동 시연의 equipclock과 같은 시계)</summary>
        private static double FleetSeconds => Time.unscaledTime + BuildingSiteTool.EquipmentClockOffset;

        public bool Visible => _visible;
        public DamStageModel Model => _model;

        public void Initialize(ElevationGrid surveyed, LocalFrame frame, DamSectionView view)
        {
            Hide();
            DestroyScene();
            _surveyed = surveyed;
            _frame = frame;
            _view = view;
            _model = null;
        }

        private void LateUpdate()
        {
            bool want = _view != null && _view.IsOpen && _view.Is3D && _view.Section != null && _surveyed != null;   // 3D는 존 단면이 있는 소양강댐만
            if (want && !_visible) Show();
            else if (!want && _visible) Hide();
            if (_visible && _model != null && _view.Section != null && _model.Zones != _view.Section)
            {
                _model = BuildModel();   // 성토 가정([마루 도달])이 바뀌었다
                _shownDate = DateTime.MinValue;
            }
            if (_visible && (_view.CurrentDate != _shownDate || _view.Cut3D != _shownCut)) Refresh();
            if (_visible) UpdateFleet();
        }

        private void UpdateFleet()
        {
            List<DamMachinePose> poses = ShowFleet
                ? DamWorkface.At(_model, _view.CurrentDate, FleetSeconds, _view.Cut3D)
                : new List<DamMachinePose>();
            if (_fleet == null) _fleet = new DamFleet3D(P, Ground);
            _fleet.Root.gameObject.SetActive(true);
            _fleet.Show(poses, FleetSeconds);
            FleetText = DamWorkface.Summary(poses);
        }

        private void Show()
        {
            if (_model == null) _model = BuildModel();
            // 다른 지형·모형은 숨긴다 (끄면 되돌린다)
            _hidden.Clear();
            foreach (MeshRenderer r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || (_root != null && r.transform.IsChildOf(_root.transform))) continue;
                if (_fleet != null && r.transform.IsChildOf(_fleet.Root)) continue;
                r.enabled = false;
                _hidden.Add(r);
            }
            if (_root == null) _root = new GameObject("DamStage3D");
            _root.SetActive(true);
            _visible = true;
            _shownDate = DateTime.MinValue;
            Refresh();
            FocusCamera();
        }

        /// <summary>존 단면 화면과 같은 성토 가정으로 단계 모형을 만든다</summary>
        private DamStageModel BuildModel()
        {
            DamSpec spec = SiteStructures.Soyanggang;
            DamSection dam = spec.CreateSection();
            AbutmentValley valley = spec.FitValley(_surveyed, dam);
            return new DamStageModel(_surveyed, dam, valley, _view.Section ?? DamZoneSection.Soyanggang(),
                spec.UpstreamExtra, spec.DownstreamExtra, SiteStructures.SoyanggangEnd, TerrainCell);
        }

        private void Hide()
        {
            if (_root != null) _root.SetActive(false);
            if (_fleet != null && _fleet.Root != null) _fleet.Root.gameObject.SetActive(false);
            foreach (Renderer r in _hidden) if (r != null) r.enabled = true;
            _hidden.Clear();
            _visible = false;
        }

        /// <summary>처음 시점: 하류 쪽, 여수로(+s) 쪽에서 비스듬히 (반 잘라 보기의 단면이 보이는 방향)</summary>
        public void FocusCamera()
        {
            var controller = GetComponent<OrbitCameraController>();
            if (controller == null || _model == null) return;
            DamSection dam = _model.Dam;
            dam.FromAxis(0, 0, out double e0, out double n0);
            dam.FromAxis(0, -1, out double e1, out double n1);
            dam.FromAxis(1, 0, out double e2, out double n2);
            double a = 62.0 * Math.PI / 180.0;   // 하류에서 여수로 쪽으로 62° 돌림: 하류면·저수지·잘라 본 단면이 함께 보인다
            double oe = (e1 - e0) * Math.Cos(a) + (e2 - e0) * Math.Sin(a);
            double on = (n1 - n0) * Math.Cos(a) + (n2 - n0) * Math.Sin(a);
            double yaw = Math.Atan2(-oe, -on) * 180.0 / Math.PI;
            // 위쪽 조작 상자와 오른쪽 설명 상자를 피해, 댐이 화면의 왼쪽 아래 빈 곳에 오도록 주시점을 오른쪽·위로 옮긴다
            // (화각 60°, 가로:세로 약 2.2 기준으로 눈으로 맞춘 값).
            const double distance = 950.0;
            double ry = yaw * Math.PI / 180.0;
            double rightE = Math.Cos(ry), rightN = -Math.Sin(ry);
            dam.FromAxis(_model.FloorCenterS, 60.0, out double te, out double tn);
            te += rightE * distance * 0.28;
            tn += rightN * distance * 0.28;
            controller.LookFrom(_frame.ToLocal(te, tn, 140.0 + distance * 0.12), yaw, 36.0, distance, 50.0);
        }

        /// <summary>작업 자리 가까이: 그날 본댐 쪽 작업(가물막이·굴착·그라우트·성토 윗면)을 낮은 각도로 본다. 터널만 뚫는 때는 터널 입구.</summary>
        public void FocusWork()
        {
            var controller = GetComponent<OrbitCameraController>();
            if (controller == null || _model == null) return;
            DateTime date = _view.CurrentDate;
            double s = _model.FloorCenterS, d = 0.0, z = Math.Max(_model.Dam.FoundationElevation, _model.FillElevation(date));
            if (date < DamStageModel.DiversionDate && date >= DamStageModel.Tunnel1Start)
            {
                double[] p = _model.TunnelPath(0)[3];   // 하류 쪽 입구
                s = p[0]; d = p[1] - 40.0; z = double.IsNaN(Ground(s, d)) ? z : Ground(s, d);
            }
            else if (date >= DamStageModel.DiversionDate && date < DamStageModel.ExcavationStart) { d = _model.UpCofferD; z = _model.UpCofferCrest(date); }
            else if (date >= _model.Zones.FillEnd) { s = DamStageModel.SpillwayS; d = DamStageModel.SpillwayTopD + (DamStageModel.SpillwayEndD - DamStageModel.SpillwayTopD) * _model.SpillwayBuilt(date) - 30.0; z = double.IsNaN(Ground(s, d)) ? z : Ground(s, d); }
            else if (_view.Cut3D) s -= 60.0;
            DamSection dam = _model.Dam;
            dam.FromAxis(0, 0, out double e0, out double n0);
            dam.FromAxis(1, 0, out double e1, out double n1);
            dam.FromAxis(0, -1, out double e2, out double n2);
            double a = 55.0 * Math.PI / 180.0;   // 하류에서 여수로 쪽으로 돌린 방향
            double oe = (e2 - e0) * Math.Cos(a) + (e1 - e0) * Math.Sin(a), on = (n2 - n0) * Math.Cos(a) + (n1 - n0) * Math.Sin(a);
            double yaw = Math.Atan2(-oe, -on) * 180.0 / Math.PI;
            const double distance = 330.0;
            double ry = yaw * Math.PI / 180.0;
            dam.FromAxis(s, d, out double te, out double tn);
            te += Math.Cos(ry) * distance * 0.22;   // 오른쪽 설명 상자를 피한다
            tn -= Math.Sin(ry) * distance * 0.22;
            controller.LookFrom(_frame.ToLocal(te, tn, z + distance * 0.08), yaw, 28.0, distance, 30.0);
        }

        private void OnDestroy()
        {
            Hide();
            DestroyScene();
            foreach (Material m in _materials.Values) if (m != null) Destroy(m);
            _materials.Clear();
        }

        private void DestroyScene()
        {
            if (_fleet != null) { _fleet.Destroy(); _fleet = null; }
            if (_root == null) return;
            foreach (MeshFilter f in _root.GetComponentsInChildren<MeshFilter>(true)) if (f.sharedMesh != null) Destroy(f.sharedMesh);
            if (_terrain != null)
            {
                Material m = _terrain.GetComponent<MeshRenderer>().sharedMaterial;
                if (m != null) { Texture t = m.GetTexture("_BaseMap"); if (t != null) Destroy(t); Destroy(m); }
            }
            Destroy(_root);
            _root = null;
            _terrain = null;
        }

        // ── 다시 그리기 ───────────────────────────────────────

        private void Refresh()
        {
            DateTime date = _view.CurrentDate;
            bool cut = _view.Cut3D;
            _shownDate = date;
            _shownCut = cut;

            _stage = _model.StageGrid(date, out StageCell[] cells);
            Texture2D colors = Colors(_stage, cells);
            if (_terrain == null)
            {
                _terrain = HillTerrainDemo.CreateTerrainObject(_stage, _frame, colors, "DamStage 지형");
                _terrain.transform.SetParent(_root.transform, false);
            }
            else
            {
                var filter = _terrain.GetComponent<MeshFilter>();
                Mesh old = filter.sharedMesh;
                filter.sharedMesh = TerrainMeshFactory.CreateMesh(TerrainMeshBuilder.Build(_stage, _frame));
                if (old != null) Destroy(old);
                Material m = _terrain.GetComponent<MeshRenderer>().sharedMaterial;
                Texture oldTex = m.GetTexture("_BaseMap");
                m.SetTexture("_BaseMap", colors);
                if (oldTex != null) Destroy(oldTex);
            }

            double fill = _model.FillElevation(date);
            SetMesh("사력", BuildShell(fill, cut), ShellColor);
            SetMesh("코어", BuildTopStrip(fill, cut, DamZone.Core), CoreColor);
            SetMesh("필터", BuildTopStrip(fill, cut, DamZone.Filter), FilterColor);
            SetMesh("단면 코어", cut ? BuildFace(fill, DamZone.Core) : null, CoreColor);
            SetMesh("단면 필터", cut ? BuildFace(fill, DamZone.Filter) : null, FilterColor);
            SetMesh("단면 사력", cut ? BuildFace(fill, DamZone.Shell) : null, ShellColor);
            SetMesh("여수로", BuildSpillway(_model.SpillwayBuilt(date)), ConcreteColor);
            SetMesh("터널 입구", BuildPortals(date), PortalColor);
        }

        private Texture2D Colors(ElevationGrid g, StageCell[] cells)
        {
            ElevationColorRamp ramp = ElevationColorRamp.CreateDefault();
            var pixels = new Color32[cells.Length];
            for (int i = 0; i < cells.Length; i++)
            {
                RgbColor c;
                switch (cells[i])
                {
                    case StageCell.Sediment: c = SedimentRgb; break;
                    case StageCell.Rock: c = RockRgb; break;
                    case StageCell.River: c = RiverRgb; break;
                    case StageCell.Reservoir: c = ReservoirRgb; break;
                    case StageCell.Cofferdam: c = CofferRgb; break;
                    default: c = ramp.ColorAtElevation(g.GetElevation(i % g.Columns, i / g.Columns), _surveyed.MinElevation, _surveyed.MaxElevation); break;
                }
                pixels[i] = TerrainColorTextureFactory.ToColor32(c);
            }
            var tex = new Texture2D(g.Columns, g.Rows, TextureFormat.RGBA32, false)
            {
                name = "DamStageColors", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
            };
            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        private void SetMesh(string name, Mesh mesh, Color color)
        {
            Transform t = _root.transform.Find(name);
            GameObject go;
            if (t == null)
            {
                go = new GameObject(name);
                go.transform.SetParent(_root.transform, false);
                go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>().sharedMaterial = MaterialFor(color);
            }
            else go = t.gameObject;
            var filter = go.GetComponent<MeshFilter>();
            if (filter.sharedMesh != null) Destroy(filter.sharedMesh);
            filter.sharedMesh = mesh;
            go.SetActive(mesh != null);
        }

        private Material MaterialFor(Color color)
        {
            if (_materials.TryGetValue(color, out Material m)) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "DamStage" };
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", 0.1f);
            _materials[color] = m;
            return m;
        }

        // ── 댐 몸체 ───────────────────────────────────────────

        private double Ground(double s, double d)
        {
            _model.Dam.FromAxis(s, d, out double e, out double n);
            return _stage.TryGetElevationAt(e, n, out double z) ? z : double.NaN;
        }

        private Vector3 P(double s, double d, double z)
        {
            _model.Dam.FromAxis(s, d, out double e, out double n);
            return _frame.ToLocal(e, n, z).ToVector3();
        }

        /// <summary>(s, d) 격자 사각형이 위를 보게 하려면 순서를 뒤집어야 하는지 (축 방향·상류 쪽에 따라 다르다)</summary>
        private bool FlipUp()
        {
            Vector3 a = P(0, 0, 0), b = P(0, 1, 0), c = P(1, 0, 0);
            return Vector3.Cross(b - a, c - a).y < 0f;
        }

        private static void Quad(List<int> t, int a, int b, int c, int d, bool flip)
        {
            if (!flip) { t.Add(a); t.Add(b); t.Add(c); t.Add(a); t.Add(c); t.Add(d); }
            else { t.Add(a); t.Add(c); t.Add(b); t.Add(a); t.Add(d); t.Add(c); }
        }

        private static Mesh ToMesh(List<Vector3> v, List<int> t, string name)
        {
            if (t.Count == 0) return null;
            var mesh = new Mesh { name = name };
            mesh.indexFormat = v.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(v);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>댐 몸체의 축 방향 끝. 오른쪽은 여수로 옆벽까지 (그 바깥의 움푹한 땅에 떨어진 조각이 그려지지 않게, 2026-11-25).</summary>
        private double SMax(bool cut) => cut ? _model.FloorCenterS : DamWorkface.RightEnd(_model);

        /// <summary>사력(겉): 높이 = min(성토 높이, 완성 표면). 사각형 네 꼭짓점이 모두 땅 아래면 뺀다.</summary>
        private Mesh BuildShell(double fill, bool cut)
        {
            DamSection dam = _model.Dam;
            if (fill <= dam.FoundationElevation + 0.05) return null;
            double s0 = -dam.HalfLength, s1 = SMax(cut), d0 = -dam.DownstreamBase, d1 = dam.UpstreamBase;
            int ns = (int)Math.Ceiling((s1 - s0) / DamStepS) + 1, nd = (int)Math.Ceiling((d1 - d0) / DamStepD) + 1;
            var v = new List<Vector3>(ns * nd);
            var above = new bool[ns * nd];
            for (int i = 0; i < ns; i++)
            {
                double s = s0 + (s1 - s0) * i / (ns - 1);
                for (int j = 0; j < nd; j++)
                {
                    double d = d0 + (d1 - d0) * j / (nd - 1);
                    double h = Math.Min(fill, dam.SurfaceAt(d));
                    double g = Ground(s, d);
                    above[i * nd + j] = double.IsNaN(g) || h > g + 0.2;
                    v.Add(P(s, d, h));
                }
            }
            var t = new List<int>();
            bool flip = FlipUp();
            for (int i = 0; i < ns - 1; i++)
                for (int j = 0; j < nd - 1; j++)
                {
                    int a = i * nd + j, b = a + 1, c = a + nd + 1, d = a + nd;
                    if (above[a] || above[b] || above[c] || above[d]) Quad(t, a, b, c, d, flip);
                }
            return ToMesh(v, t, "DamShell");
        }

        private void ZoneSpan(DamZone zone, double z, int side, out double inner, out double outer)
        {
            DamZoneSection zs = _model.Zones;
            double c = zs.CoreHalfWidth(z);
            switch (zone)
            {
                case DamZone.Core: inner = 0; outer = c; break;
                case DamZone.Filter: inner = c; outer = c > 0 ? c + zs.FilterWidth : 0; break;
                default:
                    inner = c > 0 ? c + zs.FilterWidth : 0;
                    outer = side > 0 ? zs.UpstreamFace(z) : zs.DownstreamFace(z);
                    break;
            }
        }

        /// <summary>성토 윗면의 코어·필터 띠 (코어 윗면 EL 201보다 낮을 때만)</summary>
        private Mesh BuildTopStrip(double fill, bool cut, DamZone zone)
        {
            DamSection dam = _model.Dam;
            if (fill <= dam.FoundationElevation + 0.5 || fill > _model.Zones.CoreTopElevation) return null;
            double y = fill + 0.25, s0 = -dam.HalfLength, s1 = SMax(cut);
            int ns = (int)Math.Ceiling((s1 - s0) / DamStepS) + 1;
            var v = new List<Vector3>();
            var t = new List<int>();
            bool flip = FlipUp();
            for (int side = -1; side <= 1; side += 2)
            {
                if (zone == DamZone.Core && side > 0) continue;   // 코어는 한 띠 (−c ~ +c)
                ZoneSpan(zone, fill, side, out double inner, out double outer);
                double da = zone == DamZone.Core ? -outer : side * inner, db = zone == DamZone.Core ? outer : side * outer;
                if (da > db) { double x = da; da = db; db = x; }
                int start = v.Count;
                var wet = new bool[ns];
                for (int i = 0; i < ns; i++)
                {
                    double s = s0 + (s1 - s0) * i / (ns - 1);
                    double g = Ground(s, 0);
                    wet[i] = double.IsNaN(g) || g < fill;
                    v.Add(P(s, da, y));
                    v.Add(P(s, db, y));
                }
                for (int i = 0; i < ns - 1; i++)
                {
                    if (!wet[i] && !wet[i + 1]) continue;
                    int a = start + 2 * i;
                    Quad(t, a, a + 1, a + 3, a + 2, flip);
                }
            }
            return ToMesh(v, t, "DamStrip");
        }

        /// <summary>반 잘라 보기의 단면 (s = 계곡 가운데, 양면). 존 경계는 높이에 따라 직선이라 사다리꼴로 정확하다.</summary>
        private Mesh BuildFace(double fill, DamZone zone)
        {
            DamZoneSection zs = _model.Zones;
            double found = zs.FoundationElevation;
            if (fill <= found + 0.05) return null;
            double s = _model.FloorCenterS + 0.05;
            var slabs = new List<double[]>();
            double coreTop = zs.CoreTopElevation;
            slabs.Add(new[] { found, Math.Min(fill, coreTop) });
            if (fill > coreTop) slabs.Add(new[] { coreTop, fill });

            var v = new List<Vector3>();
            var t = new List<int>();
            foreach (double[] slab in slabs)
            {
                double za = slab[0], zb = slab[1] - (slab[1] < fill ? 0 : 0);
                if (zb - za < 1e-6) continue;
                double zMid = (za + zb) / 2.0;
                for (int side = -1; side <= 1; side += 2)
                {
                    if (zone == DamZone.Core && side > 0) continue;
                    double ia, oa, ib, ob;
                    ZoneSpan(zone, za, side, out ia, out oa);
                    ZoneSpan(zone, zb - 1e-6, side, out ib, out ob);
                    ZoneSpan(zone, zMid, side, out double im, out double om);
                    if (om - im < 1e-6) continue;
                    double a0, a1, b0, b1;   // 아래 변 a0 ~ a1, 위 변 b0 ~ b1 (d)
                    if (zone == DamZone.Core) { a0 = -oa; a1 = oa; b0 = -ob; b1 = ob; }
                    else { a0 = side * ia; a1 = side * oa; b0 = side * ib; b1 = side * ob; }
                    // 양면: 앞·뒤 꼭짓점을 따로 둔다 (같은 꼭짓점을 쓰면 법선이 0이 되어 하얗게 번쩍인다)
                    for (int back = 0; back < 2; back++)
                    {
                        int k = v.Count;
                        v.Add(P(s, a0, za)); v.Add(P(s, a1, za)); v.Add(P(s, b1, zb)); v.Add(P(s, b0, zb));
                        Quad(t, k, k + 1, k + 2, k + 3, back == 1);
                    }
                }
            }
            return ToMesh(v, t, "DamFace");
        }

        // ── 여수로·터널 입구 ───────────────────────────────────

        private Mesh BuildSpillway(double built)
        {
            if (built <= 0) return null;
            double dTop = DamStageModel.SpillwayTopD, dEnd = DamStageModel.SpillwayEndD;
            double dLast = dTop + (dEnd - dTop) * built;
            double s0 = DamStageModel.SpillwayS - DamStageModel.SpillwayWidth / 2.0, s1 = s0 + DamStageModel.SpillwayWidth;
            var v = new List<Vector3>();
            var t = new List<int>();
            bool flip = FlipUp();
            int n = Math.Max(2, (int)Math.Ceiling((dTop - dLast) / 6.0) + 1);
            var floor = new double[n];
            for (int i = 0; i < n; i++)
            {
                double d = dTop + (dLast - dTop) * i / (n - 1);
                double ga = Ground(s0, d), gb = Ground(s1, d), gc = Ground((s0 + s1) / 2.0, d);
                // 바닥은 가로로 평평하게, 땅에 묻히지 않도록 세 점 중 높은 쪽에 맞춘다 (옆벽이 아래 틈을 가린다)
                floor[i] = Math.Max(Math.Max(double.IsNaN(ga) ? 0 : ga, double.IsNaN(gb) ? 0 : gb), double.IsNaN(gc) ? 0 : gc) + 0.6;
                v.Add(P(s0, d, floor[i]));
                v.Add(P(s1, d, floor[i]));
            }
            for (int i = 0; i < n - 1; i++) Quad(t, 2 * i, 2 * i + 2, 2 * i + 3, 2 * i + 1, !flip);   // d가 줄어드는 순서라 뒤집는다

            // 옆벽 (높이 5 m, 아래로 10 m 묻힘, 양면)
            foreach (double sw in new[] { s0, s1 })
            {
                for (int back = 0; back < 2; back++)
                {
                    int k = v.Count;
                    for (int i = 0; i < n; i++)
                    {
                        double d = dTop + (dLast - dTop) * i / (n - 1);
                        v.Add(P(sw, d, floor[i] - 10.0));
                        v.Add(P(sw, d, floor[i] + 5.0));
                    }
                    for (int i = 0; i < n - 1; i++) Quad(t, k + 2 * i, k + 2 * i + 1, k + 2 * i + 3, k + 2 * i + 2, back == 1);
                }
            }
            return ToMesh(v, t, "Spillway");
        }

        private Mesh BuildPortals(DateTime date)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            for (int k = 0; k < 2; k++)
            {
                if (date < (k == 0 ? DamStageModel.Tunnel1Start : DamStageModel.Tunnel2Open)) continue;
                List<double[]> path = _model.TunnelPath(k);
                foreach (double[] p in new[] { path[0], path[path.Count - 1] })
                {
                    double g = Ground(p[0], p[1]);
                    if (double.IsNaN(g)) continue;
                    Box(v, t, p[0], p[1], g, 16.0, 12.0);
                }
            }
            return ToMesh(v, t, "Portals");
        }

        /// <summary>축 방향에 맞춘 상자 (가로 = 세로 = size, 높이 h), 바닥은 z0 − 4 (땅에 묻히게)</summary>
        private void Box(List<Vector3> v, List<int> t, double s, double d, double z0, double size, double h)
        {
            double r = size / 2.0, lo = z0 - 4.0, hi = z0 + h;
            int k = v.Count;
            double[][] c = { new[] { s - r, d - r }, new[] { s + r, d - r }, new[] { s + r, d + r }, new[] { s - r, d + r } };
            foreach (double[] q in c) v.Add(P(q[0], q[1], lo));
            foreach (double[] q in c) v.Add(P(q[0], q[1], hi));
            bool flip = FlipUp();
            Quad(t, k + 4, k + 5, k + 6, k + 7, !flip);   // 윗면
            for (int i = 0; i < 4; i++)   // 옆면: 면마다 꼭짓점을 따로 (양면, 법선이 서로 지우지 않게)
            {
                for (int back = 0; back < 2; back++)
                {
                    int m = v.Count;
                    v.Add(v[k + i]); v.Add(v[k + (i + 1) % 4]); v.Add(v[k + 4 + (i + 1) % 4]); v.Add(v[k + 4 + i]);
                    Quad(t, m, m + 1, m + 2, m + 3, back == 1);
                }
            }
        }

        // ── 화면 위 점선·이름표 ────────────────────────────────

        private void OnGUI()
        {
            if (!_visible || _model == null || _stage == null) return;
            HudMode.ApplyMatrix();
            GUI.depth = -4;
            if (_tag == null)
            {
                _tag = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            }
            _tag.normal.textColor = HudStyle.Ui(HudStyle.Text);
            Camera cam = GetComponent<Camera>();
            if (cam == null) return;
            DateTime date = _view.CurrentDate;
            DamSection dam = _model.Dam;
            double fill = _model.FillElevation(date);

            // 완성 마루 (목표)
            if (fill < dam.CrestElevation - 1.0)
            {
                for (double s = -dam.HalfLength; s <= SMax(_view.Cut3D); s += 12.0)
                {
                    double g = Ground(s, 0);
                    if (!double.IsNaN(g) && g < dam.CrestElevation) Dot(cam, P(s, 0, dam.CrestElevation), new Color(1f, 1f, 1f, 0.55f), 2f);
                }
                Tag(cam, P(-120.0, 0, dam.CrestElevation + 14.0), "완성 마루 EL 203 (점선)");
            }

            // 가배수터널 (산 속). 이름표는 하나로 합친다 (두 터널이 가까워 겹친다)
            string tunnelText = "";
            Vector3 tunnelAt = Vector3.zero;
            for (int k = 0; k < 2; k++)
            {
                DateTime start = k == 0 ? DamStageModel.Tunnel1Start : DamStageModel.Tunnel2Open;
                if (date < start) continue;
                DateTime open = k == 0 ? DamStageModel.DiversionDate : DamStageModel.Tunnel2Open;
                DateTime closed = k == 0 ? DamStageModel.Tunnel1Closed : _model.Zones.FillEnd;
                double dug = k == 0 ? DamStageModel.Progress(date, start, open) : 1.0;
                string state;
                Color c;
                if (date >= closed) { state = "막음"; c = new Color(0.6f, 0.6f, 0.6f, 0.8f); }
                else if (date >= open) { state = "강물이 흐름"; c = new Color(0.45f, 0.75f, 1f, 0.95f); }
                else { state = "뚫는 중 " + (dug * 100).ToString("0", Invariant) + " %"; c = new Color(1f, 0.6f, 0.2f, 0.95f); }
                List<double[]> path = _model.TunnelPath(k);
                double total = 0;
                for (int i = 0; i + 1 < path.Count; i++) total += Dist(path[i], path[i + 1]);
                double run = 0;
                for (int i = 0; i + 1 < path.Count; i++)
                {
                    double len = Dist(path[i], path[i + 1]);
                    for (double u = 0; u < len; u += 8.0)
                    {
                        double f = (run + u) / total;
                        if (f > dug / 2.0 && f < 1.0 - dug / 2.0) continue;   // 양쪽 입구에서 뚫어 들어간다 (그림용)
                        double s = path[i][0] + (path[i + 1][0] - path[i][0]) * u / len;
                        double d = path[i][1] + (path[i + 1][1] - path[i][1]) * u / len;
                        double g = Ground(s, d);
                        if (!double.IsNaN(g)) Dot(cam, P(s, d, g + 2.0), c, 3f);
                    }
                    run += len;
                }
                double[] mid = { (path[1][0] + path[2][0]) / 2.0, (path[1][1] + path[2][1]) / 2.0 };   // 터널 가운데
                double gm = Ground(mid[0], mid[1]);
                if (k == 0 && !double.IsNaN(gm)) tunnelAt = P(mid[0], mid[1], gm + 25.0);
                tunnelText += (k == 0 ? "가배수터널 (산 속) 1호: " : " · 2호: ") + state;
            }

            if (tunnelText.Length > 0 && tunnelAt != Vector3.zero) Tag(cam, tunnelAt, tunnelText);

            // 그라우트 커튼 (성토로 덮이기 전까지)
            double grout = _model.Grouted(date);
            if (grout > 0 && fill < dam.FoundationElevation + 3.0)
            {
                for (double s = _model.FloorLeftS - 80.0; s <= _model.FloorRightS + 80.0; s += 6.0)
                {
                    double g = Ground(s, 0);
                    if (!double.IsNaN(g)) Dot(cam, P(s, 0, g + 1.0), new Color(0.55f, 0.85f, 1f, 0.95f), 3f);
                }
                Tag(cam, P(_model.FloorCenterS, 0, dam.FoundationElevation + 30.0),
                    "그라우트 커튼 " + (grout * 100).ToString("0", Invariant) + " % (땅속 45 m, 그림용)");
            }

            // 가물막이·여수로·저수지·존
            double cs = _model.FloorCenterS;
            if (date >= DamStageModel.DiversionDate)
            {
                double water = _model.ReservoirLevel(date);   // 물에 잠긴 가물막이에는 이름표를 달지 않는다
                if (double.IsNaN(water) || water < _model.UpCofferCrest(date))
                    Tag(cam, P(cs, _model.UpCofferD, _model.UpCofferCrest(date) + 14.0), "상류 가물막이 EL " + _model.UpCofferCrest(date).ToString("0", Invariant));
                Tag(cam, P(cs, _model.DownCofferD, DamStageModel.DownCofferCrest + 14.0), "하류 가물막이");
            }
            else
            {
                Tag(cam, P(cs, -dam.DownstreamBase, dam.FoundationElevation + 20.0), "강물 (상류 → 하류)");
            }
            if (date >= DamStageModel.ExcavationStart && date < DamStageModel.GroutStart)
                Tag(cam, P(cs, 0, dam.FoundationElevation + 12.0), "기초굴착: 퇴적층을 걷어 암반을 드러냄 " + (_model.Excavated(date) * 100).ToString("0", Invariant) + " %");
            double built = _model.SpillwayBuilt(date);
            if (built > 0)
            {
                double gs = Ground(DamStageModel.SpillwayS, -100.0);
                if (!double.IsNaN(gs)) Tag(cam, P(DamStageModel.SpillwayS, -100.0, gs + 20.0), "여수로 " + (built * 100).ToString("0", Invariant) + " % (위치 추정)");
            }
            double level = _model.ReservoirLevel(date);
            if (!double.IsNaN(level))
                Tag(cam, P(cs, dam.UpstreamBase + 200.0, level + 10.0), (date >= _model.Completion ? "저수지 상시만수위 EL " : "담수 중 EL ") + level.ToString("0.#", Invariant) + (date >= _model.Completion ? "" : " (그림용)"));
            if (fill > dam.FoundationElevation + 2.0 && fill < _model.Zones.CoreTopElevation && !_view.Cut3D)
                Tag(cam, P(cs, 0, fill + 8.0), "성토 EL " + fill.ToString("0", Invariant) + " — 가운데 갈색 띠가 코어(점토)");
            if (_view.Cut3D && fill > dam.FoundationElevation + 2.0)
                Tag(cam, P(cs, 0, (fill + dam.FoundationElevation) / 2.0), "코어 | 필터 | 사력 (잘라 본 단면)");
        }

        private static double Dist(double[] a, double[] b) => Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]));

        private static bool ToGui(Camera cam, Vector3 world, out Vector2 gui)
        {
            Vector3 sp = cam.WorldToScreenPoint(world);
            gui = HudMode.MouseToGui(new Vector2(sp.x, sp.y));
            return sp.z > 0f;
        }

        private static void Dot(Camera cam, Vector3 world, Color c, float size)
        {
            if (!ToGui(cam, world, out Vector2 g)) return;
            Color prev = GUI.color;
            GUI.color = HudStyle.Ui(c);
            GUI.DrawTexture(new Rect(g.x - size / 2f, g.y - size / 2f, size, size), Texture2D.whiteTexture);
            GUI.color = prev;
        }

        private void Tag(Camera cam, Vector3 world, string text)
        {
            if (!ToGui(cam, world, out Vector2 g)) return;
            Vector2 size = _tag.CalcSize(new GUIContent(text));
            var r = new Rect(g.x - size.x / 2f - 4f, g.y - 9f, size.x + 8f, 18f);
            if (HudLayout.IsOverUi(HudMode.GuiToMouse(r.center))) return;   // 글상자 밑은 그리지 않는다
            Color prev = GUI.color;
            GUI.color = HudStyle.Ui(new Color(0.08f, 0.09f, 0.11f, 0.78f));
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
            GUI.Label(r, text, _tag);
        }
    }
}
