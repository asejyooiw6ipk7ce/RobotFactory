using System.Collections.Generic;

namespace RobotFactory
{
    /// 스테이션별 최근 검사 불량률로 이상 알림을 판정한다 (FR-14).
    /// 알림은 스테이션당 하나이며, 이미 떠 있으면 값만 갱신한다. 공정은 멈추지 않는다.
    public class AlarmMonitor
    {
        private readonly StatsTracker stats;
        private readonly float thresholdPercent;
        private readonly int minInspections;
        private readonly Dictionary<StationId, double> active = new Dictionary<StationId, double>();

        public AlarmMonitor(StatsTracker stats, float thresholdPercent, int minInspections)
        {
            this.stats = stats;
            this.thresholdPercent = thresholdPercent;
            this.minInspections = minInspections;
        }

        /// 표시 중인 알림 (스테이션 → 최근 불량률)
        public IReadOnlyDictionary<StationId, double> Active => active;

        public float ThresholdPercent => thresholdPercent;

        public void Evaluate(StationId station)
        {
            // 검사 수가 기준보다 적으면 판정하지 않는다
            if (stats.RecentCount(station) < minInspections)
            {
                active.Remove(station);
                return;
            }

            double rate = stats.RecentFailRate(station);
            if (rate > thresholdPercent)
            {
                active[station] = rate; // 새로 만들지 않고 값만 갱신
            }
            else
            {
                active.Remove(station); // 기준 이하로 내려가면 해제
            }
        }
    }
}
