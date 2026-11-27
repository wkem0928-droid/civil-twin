using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace CivilTwin.Core
{
    /// <summary>
    /// ESRI ASCII Grid (.asc) 파일을 읽어 표고 격자로 만든다.
    /// QGIS에서 DEM을 이 형식으로 내보낼 수 있다. 글자로 된 파일이라 메모장으로 열어 확인할 수 있다.
    ///
    /// 파일 모양:
    ///   ncols         4            ← 열 개수 (동서)
    ///   nrows         3            ← 행 개수 (남북)
    ///   xllcorner     200000       ← 남서쪽 "칸의 모서리" 동쪽 좌표   (또는 xllcenter = 칸의 중심)
    ///   yllcorner     600000       ← 남서쪽 "칸의 모서리" 북쪽 좌표   (또는 yllcenter)
    ///   cellsize      10           ← 칸 크기 (m)
    ///   NODATA_value  -9999        ← 빈 값을 뜻하는 숫자 (없을 수도 있다)
    ///   1 2 3 4                    ← 첫 줄이 가장 "북쪽" 행
    ///   5 6 7 8
    ///   9 10 11 12                 ← 마지막 줄이 가장 남쪽 행
    ///
    /// 우리 격자로 옮길 때 처리하는 세 가지:
    ///
    ///   1. 남북 뒤집기
    ///      파일은 북쪽 행부터, 우리 격자는 남쪽 행(row 0)부터다.
    ///      파일의 r번째 행 → 격자의 (nrows - 1 - r)번째 행
    ///
    ///   2. 반 칸 이동
    ///      파일의 값 하나는 "칸 하나"의 표고이고, 그 칸의 중심 위치를 대표한다.
    ///      우리 격자점은 그 중심에 둔다.
    ///        xllcorner 일 때: 남서쪽 격자점 동쪽좌표 = xllcorner + cellsize / 2
    ///        xllcenter 일 때: 남서쪽 격자점 동쪽좌표 = xllcenter
    ///
    ///   3. 빈 값
    ///      NODATA_value 와 같은 값은 NaN으로 바꾼다. 그 칸은 지형이 없는 것으로 다룬다.
    /// </summary>
    public static class AscGridReader
    {
        public static ElevationGrid ReadFile(string path)
        {
            using (var reader = new StreamReader(path, Encoding.UTF8))
            {
                return Read(reader);
            }
        }

        public static ElevationGrid Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            using (var reader = new StringReader(text))
            {
                return Read(reader);
            }
        }

        public static ElevationGrid Read(TextReader reader)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));

            int? columns = null;
            int? rows = null;
            double? xCorner = null, yCorner = null, xCenter = null, yCenter = null;
            double? cellSize = null, dx = null, dy = null;
            double? noData = null;

            // ---------- 머리말 ----------
            // "이름 값" 쌍을 읽다가, 이름 자리에 숫자가 나오면 거기서부터 표고 값이다.
            string firstValueToken = null;
            while (true)
            {
                string key = NextToken(reader);
                if (key == null) break;

                // 숫자이거나, 숫자가 아님을 뜻하는 "nan"이면 표고 값이 시작된 것이다.
                if (!char.IsLetter(key[0]) || string.Equals(key, "nan", StringComparison.OrdinalIgnoreCase))
                {
                    firstValueToken = key;
                    break;
                }

                string valueToken = NextToken(reader);
                if (valueToken == null)
                {
                    throw new FormatException($"머리말 '{key}' 뒤에 값이 없다.");
                }

                switch (key.ToLowerInvariant())
                {
                    case "ncols": columns = ParseInt(valueToken, key); break;
                    case "nrows": rows = ParseInt(valueToken, key); break;
                    case "xllcorner": xCorner = ParseNumber(valueToken, key); break;
                    case "yllcorner": yCorner = ParseNumber(valueToken, key); break;
                    case "xllcenter": xCenter = ParseNumber(valueToken, key); break;
                    case "yllcenter": yCenter = ParseNumber(valueToken, key); break;
                    case "cellsize": cellSize = ParseNumber(valueToken, key); break;
                    case "dx": dx = ParseNumber(valueToken, key); break;
                    case "dy": dy = ParseNumber(valueToken, key); break;
                    case "nodata_value": noData = ParseNumber(valueToken, key); break;
                    default:
                        throw new FormatException($"알 수 없는 머리말 항목: '{key}'");
                }
            }

            if (columns == null) throw new FormatException("머리말에 ncols 가 없다.");
            if (rows == null) throw new FormatException("머리말에 nrows 가 없다.");
            if (columns < 2 || rows < 2)
            {
                throw new FormatException($"ncols({columns})와 nrows({rows})는 2 이상이어야 한다.");
            }

            if (cellSize == null)
            {
                if (dx == null || dy == null) throw new FormatException("머리말에 cellsize 가 없다.");

                // 가로·세로 칸 크기가 다른 파일. 정사각형 칸만 지원한다.
                if (Math.Abs(dx.Value - dy.Value) > 1e-9 * Math.Max(Math.Abs(dx.Value), 1.0))
                {
                    throw new FormatException(
                        $"가로 칸 크기(dx={dx})와 세로 칸 크기(dy={dy})가 다르다. " +
                        "QGIS에서 미터 단위 투영좌표로 변환한 뒤 정사각형 칸으로 내보내야 한다.");
                }
                cellSize = dx;
            }
            if (!(cellSize > 0)) throw new FormatException($"cellsize({cellSize})는 0보다 커야 한다.");

            double half = cellSize.Value / 2.0;
            double originEasting;
            if (xCenter != null) originEasting = xCenter.Value;
            else if (xCorner != null) originEasting = xCorner.Value + half;
            else throw new FormatException("머리말에 xllcorner 또는 xllcenter 가 없다.");

            double originNorthing;
            if (yCenter != null) originNorthing = yCenter.Value;
            else if (yCorner != null) originNorthing = yCorner.Value + half;
            else throw new FormatException("머리말에 yllcorner 또는 yllcenter 가 없다.");

            // ---------- 표고 값 ----------
            int nc = columns.Value;
            int nr = rows.Value;
            long expected = (long)nc * nr;
            var elevations = new double[expected];

            long count = 0;
            string token = firstValueToken ?? NextToken(reader);
            while (token != null)
            {
                if (count >= expected)
                {
                    throw new FormatException(
                        $"표고 값이 ncols × nrows = {nc} × {nr} = {expected}개보다 많다.");
                }

                double value = ParseElevation(token, count);
                if (noData != null && IsSame(value, noData.Value)) value = double.NaN;

                int fileRow = (int)(count / nc);      // 0 = 가장 북쪽 행
                int col = (int)(count % nc);
                int gridRow = nr - 1 - fileRow;       // 남북 뒤집기
                elevations[(long)gridRow * nc + col] = value;

                count++;
                token = NextToken(reader);
            }

            if (count < expected)
            {
                throw new FormatException(
                    $"표고 값이 {count}개뿐이다. ncols × nrows = {nc} × {nr} = {expected}개가 필요하다.");
            }

            try
            {
                return new ElevationGrid(nc, nr, cellSize.Value, originEasting, originNorthing, elevations);
            }
            catch (ArgumentException e)
            {
                throw new FormatException("표고 격자를 만들 수 없다: " + e.Message, e);
            }
        }

        /// <summary>공백(빈칸, 탭, 줄바꿈)으로 나뉜 다음 낱말을 읽는다. 더 없으면 null.</summary>
        private static string NextToken(TextReader reader)
        {
            int c = reader.Read();
            while (c != -1 && char.IsWhiteSpace((char)c)) c = reader.Read();
            if (c == -1) return null;

            var sb = new StringBuilder(24);
            while (c != -1 && !char.IsWhiteSpace((char)c))
            {
                sb.Append((char)c);
                c = reader.Read();
            }
            return sb.ToString();
        }

        private static int ParseInt(string token, string key)
        {
            if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                throw new FormatException($"머리말 '{key}'의 값 '{token}'은 정수가 아니다.");
            }
            return value;
        }

        private static double ParseNumber(string token, string key)
        {
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                throw new FormatException($"머리말 '{key}'의 값 '{token}'은 숫자가 아니다.");
            }
            return value;
        }

        private static double ParseElevation(string token, long index)
        {
            if (string.Equals(token, "nan", StringComparison.OrdinalIgnoreCase)) return double.NaN;

            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                throw new FormatException($"{index + 1}번째 표고 값 '{token}'은 숫자가 아니다.");
            }
            return value;
        }

        /// <summary>
        /// 빈 값 판정. 글자로 적는 과정에서 끝자리가 달라질 수 있어 아주 작은 차이는 같다고 본다.
        /// (예: -3.4028234663852886e+38 과 -3.40282e+38)
        /// </summary>
        private static bool IsSame(double value, double noData)
        {
            if (double.IsNaN(value)) return true;
            if (double.IsNaN(noData)) return false;
            return Math.Abs(value - noData) <= 1e-5 * Math.Max(1.0, Math.Abs(noData));
        }
    }
}
