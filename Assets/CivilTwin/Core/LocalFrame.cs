namespace CivilTwin.Core
{
    /// <summary>
    /// 로컬 좌표의 한 점. 단위는 미터.
    /// X = 동쪽(+), Y = 표고(+위), Z = 북쪽(+)
    /// </summary>
    public readonly struct LocalPoint
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;

        public LocalPoint(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    /// <summary>
    /// 측량 좌표(투영좌표, 예: EPSG:5186)를 화면용 로컬 좌표로 바꾼다.
    ///
    /// 왜 필요한가:
    ///   Unity는 위치를 float(유효숫자 약 7자리)로 저장한다.
    ///   동쪽 좌표 200123.45 m 를 float에 넣으면 약 3 mm 가 어긋난다.
    ///   그래서 원본은 double로 보관하고, 현장 근처에 원점을 하나 정해
    ///   그 차이값(작은 숫자)만 Unity에 넘긴다.
    ///
    /// 계산식:
    ///   X = 동쪽좌표 - 원점 동쪽좌표
    ///   Z = 북쪽좌표 - 원점 북쪽좌표
    ///   Y = 표고     - 원점 표고
    /// </summary>
    public readonly struct LocalFrame
    {
        /// <summary>원점의 동쪽 좌표 (m)</summary>
        public readonly double OriginEasting;

        /// <summary>원점의 북쪽 좌표 (m)</summary>
        public readonly double OriginNorthing;

        /// <summary>원점의 표고 (m)</summary>
        public readonly double OriginElevation;

        public LocalFrame(double originEasting, double originNorthing, double originElevation)
        {
            OriginEasting = originEasting;
            OriginNorthing = originNorthing;
            OriginElevation = originElevation;
        }

        /// <summary>측량 좌표 → 로컬 좌표</summary>
        public LocalPoint ToLocal(double easting, double northing, double elevation)
        {
            return new LocalPoint(
                easting - OriginEasting,
                elevation - OriginElevation,
                northing - OriginNorthing);
        }

        /// <summary>측량 좌표의 점 → 로컬 좌표</summary>
        public LocalPoint ToLocal(SurveyPoint p)
        {
            return ToLocal(p.Easting, p.Northing, p.Elevation);
        }

        /// <summary>로컬 좌표 → 측량 좌표의 점</summary>
        public SurveyPoint ToSurvey(LocalPoint p)
        {
            return new SurveyPoint(ToEasting(p), ToNorthing(p), ToElevation(p));
        }

        /// <summary>로컬 좌표 → 측량 좌표의 동쪽 좌표 (m)</summary>
        public double ToEasting(LocalPoint p)
        {
            return p.X + OriginEasting;
        }

        /// <summary>로컬 좌표 → 측량 좌표의 북쪽 좌표 (m)</summary>
        public double ToNorthing(LocalPoint p)
        {
            return p.Z + OriginNorthing;
        }

        /// <summary>로컬 좌표 → 표고 (m)</summary>
        public double ToElevation(LocalPoint p)
        {
            return p.Y + OriginElevation;
        }
    }
}
