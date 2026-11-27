using CivilTwin.Core;
using UnityEngine;

namespace CivilTwin.Runtime
{
    /// <summary>
    /// Core의 계산 결과(double)를 Unity가 쓰는 형식(float, Vector3)으로 바꾼다.
    /// double → float 변환은 화면에 그리기 직전, 이곳에서만 한다.
    /// 계산은 항상 Core에서 double로 끝낸다.
    /// </summary>
    public static class LocalPointExtensions
    {
        public static Vector3 ToVector3(this LocalPoint p)
        {
            return new Vector3((float)p.X, (float)p.Y, (float)p.Z);
        }
    }
}
