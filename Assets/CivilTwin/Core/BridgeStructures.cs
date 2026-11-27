using System;
using System.Collections.Generic;

namespace CivilTwin.Core
{
    /// <summary>교량의 한 구간 (측점 범위, 가설 공법, 경간)</summary>
    public sealed class BridgeSegment
    {
        public string Name = "", Method = "";
        /// <summary>노선 시작점에서 잰 거리 (m)</summary>
        public double From, To;
        /// <summary>대표 경간 (m). 사장교는 Spans에 경간 배치를 따로 둔다.</summary>
        public double Span;
        public double Length => To - From;
    }

    /// <summary>
    /// 랜드마크 교량 자료 (2026-11-27, 개발자: "회사 아닌 곳에 지원할 때 쓸 거 — 랜드마크·유명한 구조물").
    /// 노선은 OpenStreetMap(ODbL) 인천대교고속도로 한 방향(영종 → 송도)을 EPSG:5186으로 바꾼 것이다.
    /// OSM에서 bridge:structure=cable-stayed로 표시된 구간이 1,482 m로 공개 값 1,480 m와 0.1 % 차이 → 그 서쪽 끝을 사장교 시작으로 두고
    /// 국토교통부 구간 연장(사장교 1,480 · 접속교 동서 각 889 · 고가교 서 5,950 · 동 2,450 m)을 차례로 놓았다.
    /// </summary>
    public sealed class BridgeSpec
    {
        public string FileName, Company, Name, TypeName, Sources;
        public double[] AlignmentEast, AlignmentNorth;
        public readonly List<BridgeSegment> Segments = new List<BridgeSegment>();
        /// <summary>사장교 경간 배치 (m), 서 → 동</summary>
        public double[] CableStayedSpans;
        /// <summary>주탑 높이 (평균해수면 기준, m)와 선박 통과 높이 (m)</summary>
        public double PylonHeight, NavigationClearance;
        /// <summary>고가교 상판 높이 (해수면 위, m, 그림용 가정)</summary>
        public double ViaductDeckHeight;
        /// <summary>착공·준공 (자료), 공정 자료 확인점</summary>
        public DateTime Start, Completion;
        public readonly List<(DateTime date, string text, string source)> Checkpoints = new List<(DateTime, string, string)>();

        public BridgeSegment Segment(string name)
        {
            foreach (BridgeSegment s in Segments) if (s.Name == name) return s;
            return null;
        }

        public BridgeSegment CableStayed => Segment("사장교");

        /// <summary>주탑 측점 (사장교 시작 + 첫 두 경간, + 주경간)</summary>
        public double[] PylonStations
        {
            get
            {
                double a = CableStayed.From + CableStayedSpans[0] + CableStayedSpans[1];
                return new[] { a, a + CableStayedSpans[2] };
            }
        }

        /// <summary>노선 전체 길이 (m)</summary>
        public double AlignmentLength
        {
            get
            {
                double s = 0;
                for (int i = 1; i < AlignmentEast.Length; i++) s += Math.Sqrt(Sq(AlignmentEast[i] - AlignmentEast[i - 1]) + Sq(AlignmentNorth[i] - AlignmentNorth[i - 1]));
                return s;
            }
        }

        /// <summary>측점 → 좌표 (노선을 따라 직선 보간)</summary>
        public void PointAt(double station, out double east, out double north)
        {
            double s = 0;
            for (int i = 1; i < AlignmentEast.Length; i++)
            {
                double d = Math.Sqrt(Sq(AlignmentEast[i] - AlignmentEast[i - 1]) + Sq(AlignmentNorth[i] - AlignmentNorth[i - 1]));
                if (s + d >= station || i == AlignmentEast.Length - 1)
                {
                    double t = d > 0 ? Math.Max(0, Math.Min(1, (station - s) / d)) : 0;
                    east = AlignmentEast[i - 1] + (AlignmentEast[i] - AlignmentEast[i - 1]) * t;
                    north = AlignmentNorth[i - 1] + (AlignmentNorth[i] - AlignmentNorth[i - 1]) * t;
                    return;
                }
                s += d;
            }
            east = AlignmentEast[0]; north = AlignmentNorth[0];
        }

        private static double Sq(double v) => v * v;
    }

    public static class BridgeStructures
    {
        public const string IncheonFile = "landmark_incheon_bridge.asc";

        /// <summary>
        /// 인천대교 (2005.7 착공 ~ 2009.10 준공, 삼성물산 JV 주시공). 출처:
        ///   국토교통부 민자도로 정책 자료: 사장교 1,480 m(주경간 800 m, 주탑 238.5 m, 선박 통과 74 m), 접속교 1,778 m(동·서 각 889 m), 고가교 8,400 m(동 2,450 · 서 5,950 m), 2005.7 착공, 2009.10 준공
        ///   Arup·Structurae·위키백과: 경간 80 + 260 + 800 + 260 + 80 m, 고가교 50 m 경간 통째 프리캐스트(FSLM)
        ///   파이낸셜뉴스 2009-06-22: 상판과 구조물 모두 갖춤 / 이투데이 2008-03: 공정률 70 % / 외교부: 프리캐스트 FCM 최대 경간 145 m
        ///   노선: OpenStreetMap (ODbL)
        /// </summary>
        public static readonly BridgeSpec Incheon = Make();

        private static BridgeSpec Make()
        {
            var b = new BridgeSpec
            {
                FileName = IncheonFile,
                Company = "랜드마크",
                Name = "인천대교",
                TypeName = "사장교 + 접속교(프리캐스트 FCM) + 고가교(FSLM)",
                Sources = "국토교통부 민자도로 자료(구간 연장·주탑·착공·준공), Arup·Structurae(경간·FSLM), 파이낸셜뉴스 2009-06-22, 이투데이 2008-03, 외교부(FCM 145 m) / 노선: OpenStreetMap (ODbL)",
                AlignmentEast = new double[] {155981.0, 156000.9, 156006.0, 156163.3, 156251.1, 156308.3, 156338.2, 156436.0, 156493.7, 156505.6, 156528.8, 156584.5, 156636.4, 156688.1, 156726.0, 156750.9, 156778.0, 156814.3, 156830.2, 156856.4, 156881.7, 156897.5, 156919.6, 156924.4, 156978.8, 157000.5, 157379.2, 157600.1, 157810.6, 157985.0, 158166.9, 158329.7, 158414.0, 158503.3, 158739.9, 158939.3, 159096.0, 159216.3, 159283.9, 159414.8, 159570.0, 159845.5, 159889.6, 159987.3, 160092.7, 160168.0, 160271.8, 160380.0, 160482.6, 160603.2, 160703.3, 160811.2, 161015.1, 162275.8, 162406.1, 162576.8, 162758.2, 162895.7, 163039.2, 163221.6, 163427.1, 163626.3, 163845.2, 163992.7, 164123.6, 164263.5, 164381.1, 164522.3, 164644.7, 164752.1, 164869.8, 165013.7, 165468.4, 166911.9, 167001.0, 167052.6, 167136.1, 167203.3, 167261.7, 167331.2, 167391.9, 167455.6, 167542.6, 167614.8, 167694.5, 167756.7, 167875.1, 167982.7, 168071.6, 168177.1, 168254.0, 168335.7, 168403.0, 168490.2, 168569.8, 168592.9, 168684.9, 168721.4, 168832.0, 168915.3, 168987.9, 169040.4, 169122.7},
                AlignmentNorth = new double[] {542112.7, 542097.6, 542093.3, 541978.1, 541912.5, 541870.9, 541848.9, 541777.1, 541727.3, 541717.9, 541698.8, 541651.0, 541603.1, 541553.4, 541512.3, 541482.3, 541447.9, 541401.3, 541380.7, 541344.0, 541308.6, 541285.8, 541252.6, 541245.4, 541164.7, 541132.5, 540533.4, 540173.3, 539830.1, 539545.9, 539249.4, 538984.2, 538846.7, 538701.1, 538315.6, 537990.7, 537735.3, 537539.3, 537429.2, 537215.8, 536963.0, 536514.2, 536448.2, 536309.4, 536172.6, 536083.2, 535969.1, 535860.2, 535768.0, 535670.9, 535595.4, 535521.4, 535395.5, 534616.8, 534536.4, 534436.9, 534349.8, 534289.1, 534233.2, 534171.3, 534111.9, 534068.7, 534037.2, 534023.1, 534012.9, 534007.0, 534005.9, 534010.7, 534016.6, 534027.9, 534044.1, 534066.5, 534164.9, 534473.7, 534493.5, 534501.7, 534511.2, 534517.9, 534523.0, 534526.2, 534527.1, 534526.3, 534523.0, 534519.0, 534513.2, 534505.4, 534487.5, 534467.2, 534447.0, 534417.6, 534393.5, 534365.4, 534340.1, 534302.8, 534266.2, 534255.6, 534208.8, 534187.7, 534124.1, 534070.5, 534019.3, 533979.4, 533915.5},
                CableStayedSpans = new[] { 80.0, 260.0, 800.0, 260.0, 80.0 },
                PylonHeight = 238.5,
                NavigationClearance = 74.0,
                ViaductDeckHeight = 20.0,
                Start = new DateTime(2005, 7, 1),
                Completion = new DateTime(2009, 10, 1),
            };
            const double cs = 8487.0;   // OSM의 사장교 서쪽 끝 측점
            b.Segments.Add(new BridgeSegment { Name = "서측 고가교", Method = "FSLM (50 m 경간 통째 프리캐스트)", From = cs - 889 - 5950, To = cs - 889, Span = 50 });
            b.Segments.Add(new BridgeSegment { Name = "서측 접속교", Method = "프리캐스트 세그먼트 FCM", From = cs - 889, To = cs, Span = 145 });
            b.Segments.Add(new BridgeSegment { Name = "사장교", Method = "주탑 + 캔틸레버 상판 + 사장 케이블", From = cs, To = cs + 1480, Span = 800 });
            b.Segments.Add(new BridgeSegment { Name = "동측 접속교", Method = "프리캐스트 세그먼트 FCM", From = cs + 1480, To = cs + 1480 + 889, Span = 145 });
            b.Segments.Add(new BridgeSegment { Name = "동측 고가교", Method = "FSLM (50 m 경간 통째 프리캐스트)", From = cs + 1480 + 889, To = cs + 1480 + 889 + 2450, Span = 50 });
            b.Checkpoints.Add((new DateTime(2008, 3, 1), "공정률 70 %", "이투데이 2008-03"));
            b.Checkpoints.Add((new DateTime(2009, 6, 22), "상판과 구조물 모두 갖춤", "파이낸셜뉴스 2009-06-22"));
            return b;
        }

        public static readonly BridgeSpec[] Bridges = { Incheon };

        public static BridgeSpec Find(string fileName)
        {
            foreach (BridgeSpec b in Bridges) if (string.Equals(b.FileName, fileName, StringComparison.OrdinalIgnoreCase)) return b;
            return null;
        }
    }
}
