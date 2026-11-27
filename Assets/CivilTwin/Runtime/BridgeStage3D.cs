using System;
using System.Collections.Generic;
using CivilTwin.Core;
using UnityEngine;

namespace CivilTwin.Runtime
{
    /// <summary>
    /// 인천대교 시공 단계 3D (2026-11-27). 시공 단계 화면의 [3D로 보기]에서 그날 다 된 부분(Core/BridgeProgress)을
    /// 실제 노선(OpenStreetMap → EPSG:5186) 위에 그린다: 상판(폭 30 m, 두께 3 m 그림용), 교각, 역Y 주탑, 케이블.
    /// 지형은 그 현장의 위성 지형 그대로(바다 0 m). 크기·모양은 그림용이고 자료는 구간 연장·경간·주탑 높이·선박 통과 높이다.
    /// </summary>
    public sealed class BridgeStage3D : MonoBehaviour
    {
        private const double DeckWidth = 30.0, DeckThickness = 3.0, PierSize = 6.0, LegSpread = 22.0;

        private LocalFrame _frame;
        private bool _hasFrame;
        private DamSectionView _view;
        private GameObject _root;
        private DateTime _shownDate = DateTime.MinValue;
        private bool _visible;
        private readonly Dictionary<Color, Material> _materials = new Dictionary<Color, Material>();

        public bool Visible => _visible;

        public void Initialize(LocalFrame frame, DamSectionView view)
        {
            _frame = frame;
            _hasFrame = true;
            _view = view;
            Hide();
        }

        private void LateUpdate()
        {
            BridgeSpec b = _view != null ? _view.Bridge : null;
            bool want = b != null && _view.IsOpen && _view.Is3D && _hasFrame;
            if (want && !_visible) { _visible = true; _shownDate = DateTime.MinValue; if (_root == null) _root = new GameObject("BridgeStage3D"); _root.SetActive(true); FocusCamera(b); }
            else if (!want && _visible) Hide();
            if (_visible && _view.CurrentDate != _shownDate) Rebuild(b);
        }

        private void Hide()
        {
            _visible = false;
            if (_root != null) _root.SetActive(false);
        }

        private void FocusCamera(BridgeSpec b)
        {
            var controller = GetComponent<OrbitCameraController>();
            if (controller == null) return;
            double mid = (b.PylonStations[0] + b.PylonStations[1]) / 2.0;
            b.PointAt(mid, out double e, out double n);
            b.PointAt(mid + 50, out double e2, out double n2);
            double heading = Math.Atan2(e2 - e, n2 - n) * 180.0 / Math.PI;   // 북에서 시계 방향
            controller.LookFrom(_frame.ToLocal(e, n, 190.0), heading + 60.0, 12.0, 1300.0, 150.0);   // 주탑 위쪽을 화면 가운데에 (위쪽 조작 상자 아래로 주탑이 보이게)   // 주경간 가운데를 남동쪽 낮은 곳에서
        }

        private void Rebuild(BridgeSpec b)
        {
            _shownDate = _view.CurrentDate;
            ConstructionSchedule s = _view.Schedule;
            if (s == null) return;
            BridgeState st = BridgeProgress.At(b, s, _shownDate);

            var deck = new MeshData(); var piers = new MeshData(); var pylons = new MeshData(); var cables = new MeshData();
            foreach (var (from, to) in st.Deck)
            {
                for (double a = from; a < to - 1e-6; a += 40.0)
                {
                    double c = Math.Min(to, a + 40.0);
                    Vector3 p0 = At(b, a, BridgeProgress.DeckHeight(b, a)), p1 = At(b, c, BridgeProgress.DeckHeight(b, c));
                    deck.Beam(p0, p1, (float)DeckWidth, (float)DeckThickness);
                }
            }
            foreach (double p in st.Piers)
            {
                double h = BridgeProgress.DeckHeight(b, p) - DeckThickness;
                piers.Beam(At(b, p, -5.0), At(b, p, h), (float)PierSize * 2f, (float)PierSize);
            }
            foreach (double ps in b.PylonStations)
            {
                if (st.FoundationProgress > 0) piers.Beam(At(b, ps, -8.0), At(b, ps, 6.0), 60f, 30f);   // 주탑 기초
                if (st.PylonHeight <= 0.5) continue;
                // 역Y: 두 다리가 상판 아래에서 벌어졌다가 높이 65 %에서 한 줄기로 모인다
                double joint = b.PylonHeight * 0.65, h = st.PylonHeight;
                Vector3 baseL = Side(b, ps, 6.0, -LegSpread), baseR = Side(b, ps, 6.0, LegSpread);
                Vector3 jointP = At(b, ps, joint);
                double legTop = Math.Min(h, joint);
                pylons.Beam(baseL, Vector3.Lerp(baseL, jointP, (float)(legTop / joint)), 7f, 7f);
                pylons.Beam(baseR, Vector3.Lerp(baseR, jointP, (float)(legTop / joint)), 7f, 7f);
                if (h > joint) pylons.Beam(jointP, At(b, ps, h), 9f, 9f);
            }
            foreach (var (pylon, d) in st.Cables)
            {
                double dist = Math.Abs(d - pylon);
                double anchor = b.PylonHeight * (0.70 + 0.28 * Math.Min(1.0, dist / 400.0));
                foreach (double side in new[] { -DeckWidth / 2.0 + 1.0, DeckWidth / 2.0 - 1.0 })
                    cables.Beam(Side(b, pylon, anchor, side * 0.15), Side(b, d, BridgeProgress.DeckHeight(b, d), side), 0.8f, 0.8f);
            }
            Put("Deck", deck, new Color(0.80f, 0.80f, 0.78f));
            Put("Piers", piers, new Color(0.62f, 0.62f, 0.60f));
            Put("Pylons", pylons, new Color(0.88f, 0.88f, 0.86f));
            Put("Cables", cables, new Color(0.95f, 0.85f, 0.35f));
        }

        private Vector3 At(BridgeSpec b, double station, double z)
        {
            b.PointAt(station, out double e, out double n);
            return _frame.ToLocal(e, n, z).ToVector3();
        }

        /// <summary>노선에 직각으로 offset m 옆 (오른쪽 +)</summary>
        private Vector3 Side(BridgeSpec b, double station, double z, double offset)
        {
            b.PointAt(station - 5, out double e0, out double n0);
            b.PointAt(station + 5, out double e1, out double n1);
            double len = Math.Sqrt((e1 - e0) * (e1 - e0) + (n1 - n0) * (n1 - n0));
            double ox = (n1 - n0) / len * offset, oy = -(e1 - e0) / len * offset;
            b.PointAt(station, out double e, out double n);
            return _frame.ToLocal(e + ox, n + oy, z).ToVector3();
        }

        private void Put(string name, MeshData data, Color color)
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
            filter.sharedMesh = data.ToMesh();
            go.SetActive(data.Count > 0);
        }

        private Material MaterialFor(Color color)
        {
            if (_materials.TryGetValue(color, out Material m)) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "BridgeStage" };
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", 0.2f);
            _materials[color] = m;
            return m;
        }

        /// <summary>두 점 사이의 각진 막대(폭 w, 두께 t) 삼각형들</summary>
        private sealed class MeshData
        {
            private readonly List<Vector3> _v = new List<Vector3>();
            private readonly List<int> _t = new List<int>();
            public int Count => _t.Count;

            public void Beam(Vector3 a, Vector3 b, float w, float t)
            {
                Vector3 axis = b - a;
                if (axis.sqrMagnitude < 1e-6f) return;
                Vector3 dir = axis.normalized;
                Vector3 side = Vector3.Cross(dir, Mathf.Abs(dir.y) > 0.95f ? Vector3.forward : Vector3.up).normalized * (w / 2f);
                Vector3 up = Vector3.Cross(side.normalized, dir).normalized * (t / 2f);
                Vector3[] c =
                {
                    a - side - up, a + side - up, a + side + up, a - side + up,
                    b - side - up, b + side - up, b + side + up, b - side + up,
                };
                int[,] faces = { { 0, 1, 2, 3 }, { 5, 4, 7, 6 }, { 4, 0, 3, 7 }, { 1, 5, 6, 2 }, { 3, 2, 6, 7 }, { 4, 5, 1, 0 } };
                Vector3 center = (a + b) / 2f;
                for (int f = 0; f < 6; f++)
                {
                    int s = _v.Count;
                    Vector3 q0 = c[faces[f, 0]], q1 = c[faces[f, 1]], q2 = c[faces[f, 2]];
                    for (int k = 0; k < 4; k++) _v.Add(c[faces[f, k]]);
                    // 바깥을 향하게: Unity는 시계 방향(앞에서 볼 때)이 앞면
                    Vector3 n = Vector3.Cross(q1 - q0, q2 - q0);
                    bool outward = Vector3.Dot(n, (q0 + q2) / 2f - center) > 0f;
                    if (outward) _t.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
                    else _t.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });
                }
            }

            public Mesh ToMesh()
            {
                var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                m.SetVertices(_v);
                m.SetTriangles(_t, 0);
                m.RecalculateNormals();
                m.RecalculateBounds();
                return m;
            }
        }
    }
}
