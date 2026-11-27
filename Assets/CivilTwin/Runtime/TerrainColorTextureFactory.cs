using System;
using CivilTwin.Core;
using UnityEngine;

namespace CivilTwin.Runtime
{
    /// <summary>
    /// 표고별 색을 그림(텍스처) 한 장으로 만든다. 지형 메시 위에 이 그림을 덮어 씌운다.
    /// 그림의 점 하나가 격자점 하나다 (가로 = 열 개수, 세로 = 행 개수).
    /// 색 계산은 Core가 한다. 여기서는 그 결과를 Unity 그림 형식으로 옮기기만 한다.
    /// </summary>
    public static class TerrainColorTextureFactory
    {
        /// <summary>평평한 면(수면 추정)에 칠하는 색. 파랑.</summary>
        public static readonly RgbColor WaterColor = new RgbColor(0.25, 0.47, 0.70);

        /// <param name="flat">평평한 면 표시(FlatSurfaceDetector의 결과). null이면 수면 색을 칠하지 않는다.</param>
        public static Texture2D Create(ElevationGrid grid, ElevationColorRamp ramp, bool[] flat)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (ramp == null) throw new ArgumentNullException(nameof(ramp));

            int columns = grid.Columns;
            int rows = grid.Rows;
            if (flat != null && flat.Length != columns * rows)
            {
                throw new ArgumentException("평평한 면 배열의 길이가 격자점 개수와 다르다.", nameof(flat));
            }

            var pixels = new Color32[columns * rows];
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < columns; col++)
                {
                    int i = row * columns + col;
                    RgbColor c = (flat != null && flat[i])
                        ? WaterColor
                        : ramp.ColorAtElevation(grid.GetElevation(col, row), grid.MinElevation, grid.MaxElevation);
                    pixels[i] = ToColor32(c);
                }
            }

            return ToTexture(pixels, columns, rows, "TerrainColorTexture");
        }

        /// <summary>
        /// 절토·성토 색 그림. 깎는 곳은 빨강, 쌓는 곳은 파랑, 손대지 않는 곳은 원래 지형 색을 흐리게.
        /// 색 계산은 Core(CutFillColors)가 한다.
        /// </summary>
        /// <param name="ground">원지반</param>
        /// <param name="design">계획면 (원지반과 같은 격자)</param>
        public static Texture2D CreateCutFill(
            ElevationGrid ground, ElevationGrid design, ElevationColorRamp ramp,
            double maxCutDepth, double maxFillHeight)
        {
            if (ground == null) throw new ArgumentNullException(nameof(ground));
            if (design == null) throw new ArgumentNullException(nameof(design));
            if (ramp == null) throw new ArgumentNullException(nameof(ramp));
            if (ground.Columns != design.Columns || ground.Rows != design.Rows)
            {
                throw new ArgumentException("원지반과 계획면의 격자 크기가 다르다.", nameof(design));
            }

            int columns = ground.Columns;
            int rows = ground.Rows;
            var pixels = new Color32[columns * rows];
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < columns; col++)
                {
                    double z = ground.GetElevation(col, row);
                    RgbColor terrain = ramp.ColorAtElevation(z, ground.MinElevation, ground.MaxElevation);
                    double h = design.GetElevation(col, row) - z;
                    pixels[row * columns + col] =
                        ToColor32(CutFillColors.ColorFor(h, maxCutDepth, maxFillHeight, terrain));
                }
            }

            return ToTexture(pixels, columns, rows, "CutFillColorTexture");
        }

        /// <summary>
        /// 댐 축조 그림 (2026-11-20): 지금 쌓인 곳(보이는 면이 원지반보다 높은 곳)은 사력 색, 나머지는 원래 지형 색.
        /// 절토·성토 색(파랑)으로 칠하면 댐 모양이 안 보여서, 실제 사력댐 사면(돌 붙임) 비슷한 회갈색으로 칠한다.
        /// 아래(기초)는 조금 어둡게, 위(마루)는 밝게 해 높이가 읽히게 한다.
        /// </summary>
        public static Texture2D CreateDam(ElevationGrid ground, ElevationGrid shown, ElevationColorRamp ramp,
            double foundationElevation, double crestElevation, bool[] water = null)
        {
            if (ground == null) throw new ArgumentNullException(nameof(ground));
            if (shown == null) throw new ArgumentNullException(nameof(shown));
            if (ramp == null) throw new ArgumentNullException(nameof(ramp));

            int columns = ground.Columns;
            int rows = ground.Rows;
            var low = new RgbColor(0.42, 0.40, 0.36);
            var high = new RgbColor(0.70, 0.67, 0.60);
            double span = Math.Max(1.0, crestElevation - foundationElevation);
            var pixels = new Color32[columns * rows];
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < columns; col++)
                {
                    double z0 = ground.GetElevation(col, row);
                    double z = shown.GetElevation(col, row);
                    RgbColor c;
                    if (water != null && water[row * columns + col])
                    {
                        c = WaterColor;
                    }
                    else if (z - z0 > 0.5)
                    {
                        double t = Math.Max(0.0, Math.Min(1.0, (z - foundationElevation) / span));
                        c = new RgbColor(low.R + (high.R - low.R) * t, low.G + (high.G - low.G) * t, low.B + (high.B - low.B) * t);
                    }
                    else
                    {
                        c = ramp.ColorAtElevation(z0, ground.MinElevation, ground.MaxElevation);
                    }
                    pixels[row * columns + col] = ToColor32(c);
                }
            }

            return ToTexture(pixels, columns, rows, "DamColorTexture");
        }

        private static Texture2D ToTexture(Color32[] pixels, int columns, int rows, string name)
        {
            var texture = new Texture2D(columns, rows, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,   // 가장자리 색이 반대편으로 번지지 않게
                filterMode = FilterMode.Bilinear,   // 격자점 사이는 부드럽게 섞는다
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        public static Color32 ToColor32(RgbColor c)
        {
            return new Color32(ToByte(c.R), ToByte(c.G), ToByte(c.B), 255);
        }

        private static byte ToByte(double v)
        {
            if (v <= 0.0) return 0;
            if (v >= 1.0) return 255;
            return (byte)Math.Round(v * 255.0);
        }
    }
}
