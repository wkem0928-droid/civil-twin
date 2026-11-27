namespace CivilTwin.Core
{
    /// <summary>
    /// 측량 좌표의 한 점 (투영좌표, 예: EPSG:5186). 단위는 미터.
    /// 화면용 로컬 좌표(LocalPoint)와 구분하기 위해 따로 둔다.
    /// 측정 결과는 항상 이 좌표로 보고한다.
    /// </summary>
    public readonly struct SurveyPoint
    {
        /// <summary>동쪽 좌표 (m)</summary>
        public readonly double Easting;

        /// <summary>북쪽 좌표 (m)</summary>
        public readonly double Northing;

        /// <summary>표고 (m)</summary>
        public readonly double Elevation;

        public SurveyPoint(double easting, double northing, double elevation)
        {
            Easting = easting;
            Northing = northing;
            Elevation = elevation;
        }
    }
}
