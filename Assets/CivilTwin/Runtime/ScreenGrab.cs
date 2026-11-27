using System.IO;
using UnityEngine;

namespace CivilTwin.Runtime
{
    /// <summary>
    /// 화면을 PNG로 저장한다 (글상자 포함). 반드시 프레임 끝(WaitForEndOfFrame 뒤)에 불러야 한다.
    ///
    /// 왜 이 방법인가 (2026-11-20, 자동 시연으로 세 방법을 비교):
    ///   ScreenCapture.CaptureScreenshot, CaptureScreenshotAsTexture → 이 PC(Vulkan)에서 색이 틀리다 (보라·하늘색).
    ///   CaptureScreenshotIntoRenderTexture → 색은 맞고 위아래만 뒤집힌다. 그래서 이것을 쓰고 줄 순서를 뒤집는다.
    /// 다른 그래픽 API(DX11 등)에서도 뒤집히는지는 검증되지 않았다.
    /// </summary>
    public static class ScreenGrab
    {
        public static void SavePng(string path)
        {
            int width = Screen.width, height = Screen.height;
            var rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            ScreenCapture.CaptureScreenshotIntoRenderTexture(rt);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            RenderTexture.active = previous;

            // 위아래 뒤집기: 줄 단위로 순서를 바꾼다.
            Color32[] pixels = tex.GetPixels32();
            var flipped = new Color32[pixels.Length];
            for (int y = 0; y < height; y++)
            {
                System.Array.Copy(pixels, y * width, flipped, (height - 1 - y) * width, width);
            }
            tex.SetPixels32(flipped);
            tex.Apply();

            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            File.WriteAllBytes(path, tex.EncodeToPNG());

            Object.Destroy(tex);
            rt.Release();
            Object.Destroy(rt);
        }
    }
}
