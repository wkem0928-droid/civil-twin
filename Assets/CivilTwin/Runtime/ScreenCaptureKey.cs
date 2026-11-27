using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CivilTwin.Runtime
{
    /// <summary>
    /// 화면 저장 도구. F12를 누르면 지금 화면(글상자 포함)을 그림 파일로 저장한다.
    /// 다른 도구도 Request("이름")으로 저장을 부탁할 수 있다 (예: 대안 비교 화면이 바뀔 때).
    ///
    /// 저장 위치: 프로젝트 폴더/Logs/captures/시각_이름.png  (Logs 폴더는 Git에 올리지 않는다)
    /// 목적: 개발자가 캡처해서 붙여 넣지 않아도 Claude가 파일을 직접 열어 화면을 확인할 수 있게 한다.
    /// </summary>
    public sealed class ScreenCaptureKey : MonoBehaviour
    {
        public static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "captures"));

        private void Update()
        {
            if (!InputGate.Allowed) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f12Key.wasPressedThisFrame) Capture();
        }

        /// <summary>화면을 저장한다. 키(F12)와 화면 메뉴가 함께 쓴다.</summary>
        public void Capture()
        {
            StartCoroutine(SaveAtEndOfFrame("F12"));
        }

        /// <summary>이번 프레임이 다 그려진 뒤의 화면을 저장한다 (색이 맞는 ScreenGrab 방식).</summary>
        private System.Collections.IEnumerator SaveAtEndOfFrame(string label)
        {
            yield return new WaitForEndOfFrame();
            try
            {
                string name = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "_" + label + ".png";
                string path = Path.Combine(Folder, name);
                ScreenGrab.SavePng(path);
                Debug.Log("[CivilTwin] 화면 저장: " + path);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CivilTwin] 화면 저장 실패: " + e.Message);
            }
        }
    }
}
