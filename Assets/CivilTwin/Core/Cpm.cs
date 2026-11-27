using System;
using System.Collections.Generic;

namespace CivilTwin.Core
{
    /// <summary>
    /// 공정표의 작업 하나. 선행 작업이 모두 끝나야 시작할 수 있다 (끝 → 시작 관계, 간격 0일).
    /// </summary>
    public sealed class CpmActivity
    {
        /// <summary>작업 이름</summary>
        public string Name { get; }

        /// <summary>소요 일수 (0이면 시점만 나타내는 작업)</summary>
        public double Duration { get; }

        /// <summary>선행 작업의 번호들. 자기보다 앞 번호만 쓸 수 있다.</summary>
        public IReadOnlyList<int> Predecessors { get; }

        public CpmActivity(string name, double duration, params int[] predecessors)
        {
            if (!(duration >= 0) || double.IsInfinity(duration))
            {
                throw new ArgumentOutOfRangeException(nameof(duration), "소요 일수는 0 이상이어야 한다.");
            }

            Name = name ?? "";
            Duration = duration;
            Predecessors = predecessors ?? new int[0];
        }
    }

    /// <summary>CPM 계산 결과. 배열의 번호는 작업 번호와 같다. 날짜는 공사 시작을 0일로 센다.</summary>
    public sealed class CpmResult
    {
        /// <summary>가장 빠른 시작일 (ES)</summary>
        public double[] EarlyStart;

        /// <summary>가장 빠른 종료일 (EF = ES + 소요 일수)</summary>
        public double[] EarlyFinish;

        /// <summary>가장 늦은 시작일 (LS = LF − 소요 일수)</summary>
        public double[] LateStart;

        /// <summary>가장 늦은 종료일 (LF)</summary>
        public double[] LateFinish;

        /// <summary>전체 여유 (TF = LS − ES). 이만큼 늦어져도 전체 공기는 늘지 않는다.</summary>
        public double[] TotalFloat;

        /// <summary>자유 여유 (FF = 후속 작업의 가장 빠른 ES − EF). 이만큼 늦어져도 후속 작업이 밀리지 않는다.</summary>
        public double[] FreeFloat;

        /// <summary>주공정(전체 여유가 0인 작업)이면 true</summary>
        public bool[] Critical;

        /// <summary>전체 공기 (일)</summary>
        public double ProjectDuration;
    }

    /// <summary>
    /// CPM (Critical Path Method, 주공정법): 작업의 소요 일수와 선후행 관계로
    /// 각 작업의 가장 빠른·늦은 날짜, 여유, 주공정선을 구한다.
    ///
    /// 계산 방법:
    ///   1) 전진 계산 (앞에서 뒤로)
    ///        ES = 선행 작업들의 EF 중 가장 큰 값 (선행이 없으면 0)
    ///        EF = ES + 소요 일수
    ///        전체 공기 = 모든 EF 중 가장 큰 값
    ///   2) 후진 계산 (뒤에서 앞으로)
    ///        LF = 후속 작업들의 LS 중 가장 작은 값 (후속이 없으면 전체 공기)
    ///        LS = LF − 소요 일수
    ///   3) 여유
    ///        전체 여유 TF = LS − ES
    ///        자유 여유 FF = (후속 작업들의 ES 중 가장 작은 값, 후속이 없으면 전체 공기) − EF
    ///        TF = 0 인 작업이 주공정. 주공정이 하루 늦으면 전체 공기도 하루 늦는다.
    ///
    /// 손계산 예: A(3일), B(4일), C(2일, 선행 A), D(5일, 선행 A·B), E(3일, 선행 C·D)
    ///   전진: A 0→3, B 0→4, C 3→5, D 4→9 (A 3과 B 4 중 큰 값), E 9→12  → 전체 공기 12일
    ///   후진: E LF 12, LS 9 / D LF 9, LS 4 / C LF 9, LS 7 / A LF min(7, 4) = 4, LS 1 / B LF 4, LS 0
    ///   전체 여유: A 1, B 0, C 4, D 0, E 0   → 주공정선 B → D → E
    ///   자유 여유: A min(3, 4) − 3 = 0, C 9 − 5 = 4
    ///
    /// 한계: 관계는 "끝 → 시작"만 있고 간격(lag)은 0이다. 자원(인원·장비) 제한, 휴일, 날씨는 다루지 않는다.
    /// </summary>
    public static class Cpm
    {
        /// <summary>여유가 이 값 이하면 0으로 본다 (소수 계산의 오차 때문).</summary>
        public const double FloatTolerance = 1e-9;

        public static CpmResult Compute(IReadOnlyList<CpmActivity> activities)
        {
            if (activities == null) throw new ArgumentNullException(nameof(activities));

            int n = activities.Count;
            var result = new CpmResult
            {
                EarlyStart = new double[n],
                EarlyFinish = new double[n],
                LateStart = new double[n],
                LateFinish = new double[n],
                TotalFloat = new double[n],
                FreeFloat = new double[n],
                Critical = new bool[n],
            };

            // 1) 전진 계산. 선행 작업은 항상 앞 번호라서 번호 순서대로 한 번만 지나가면 된다.
            double duration = 0.0;
            for (int i = 0; i < n; i++)
            {
                CpmActivity activity = activities[i];
                if (activity == null) throw new ArgumentException("작업이 비어 있다: " + i, nameof(activities));

                double start = 0.0;
                foreach (int p in activity.Predecessors)
                {
                    if (p < 0 || p >= i)
                    {
                        throw new ArgumentException(
                            "선행 작업은 자기보다 앞 번호여야 한다 (작업 " + i + ", 선행 " + p + ").", nameof(activities));
                    }
                    start = Math.Max(start, result.EarlyFinish[p]);
                }

                result.EarlyStart[i] = start;
                result.EarlyFinish[i] = start + activity.Duration;
                duration = Math.Max(duration, result.EarlyFinish[i]);
            }
            result.ProjectDuration = duration;

            // 2) 후진 계산. 먼저 모두 "전체 공기"로 두고, 뒤 번호부터 선행 작업의 한계를 줄여 간다.
            var earliestSuccessorStart = new double[n];
            for (int i = 0; i < n; i++)
            {
                result.LateFinish[i] = duration;
                earliestSuccessorStart[i] = duration;
            }

            for (int i = n - 1; i >= 0; i--)
            {
                result.LateStart[i] = result.LateFinish[i] - activities[i].Duration;
                foreach (int p in activities[i].Predecessors)
                {
                    result.LateFinish[p] = Math.Min(result.LateFinish[p], result.LateStart[i]);
                    earliestSuccessorStart[p] = Math.Min(earliestSuccessorStart[p], result.EarlyStart[i]);
                }
            }

            // 3) 여유와 주공정
            for (int i = 0; i < n; i++)
            {
                result.TotalFloat[i] = result.LateStart[i] - result.EarlyStart[i];
                result.FreeFloat[i] = earliestSuccessorStart[i] - result.EarlyFinish[i];
                result.Critical[i] = result.TotalFloat[i] <= FloatTolerance;
            }

            return result;
        }
    }
}
