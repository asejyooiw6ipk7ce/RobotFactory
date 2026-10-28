using System.Collections.Generic;
using System.Linq;

namespace RobotFactory
{
    /// 검사 이벤트와 처리 개수를 집계한다 (FR-12).
    /// 완제품 검사는 스테이션3의 검사 결과로 본다.
    public class StatsTracker
    {
        private readonly int recentCount;
        private readonly Dictionary<StationId, int> inspected = new Dictionary<StationId, int>();
        private readonly Dictionary<StationId, int> defects = new Dictionary<StationId, int>();
        private readonly Dictionary<StationId, Queue<bool>> recent = new Dictionary<StationId, Queue<bool>>(); // true = 불량
        private readonly Dictionary<CounterType, int> counters = new Dictionary<CounterType, int>();
        private int finalInspected;
        private int finalPassed;

        public StatsTracker(int recentCount)
        {
            this.recentCount = recentCount;
        }

        /// 최종 양품률(%). 완제품 검사가 없으면 null
        public double? FinalYieldRate =>
            finalInspected == 0 ? (double?)null : finalPassed * 100.0 / finalInspected;

        /// 스테이션 불량률(%). 검사가 없으면 null
        public double? StationDefectRate(StationId station)
        {
            int total = Get(inspected, station);
            return total == 0 ? (double?)null : Get(defects, station) * 100.0 / total;
        }

        /// 최근 검사 창(recentCount) 안의 불량률(%)
        public double RecentFailRate(StationId station)
        {
            int n = RecentCount(station);
            return n == 0 ? 0 : RecentFailCount(station) * 100.0 / n;
        }

        public int RecentCount(StationId station) =>
            recent.TryGetValue(station, out var queue) ? queue.Count : 0;

        public int RecentFailCount(StationId station) =>
            recent.TryGetValue(station, out var queue) ? queue.Count(isFail => isFail) : 0;

        public int Count(CounterType type) => Get(counters, type);

        public void Record(InspectEvent e)
        {
            bool fail = e.Result == InspectResult.Fail;

            Increase(inspected, e.StationId);
            if (fail) Increase(defects, e.StationId);

            if (!recent.TryGetValue(e.StationId, out var queue))
            {
                queue = new Queue<bool>();
                recent[e.StationId] = queue;
            }
            queue.Enqueue(fail);
            while (queue.Count > recentCount) queue.Dequeue();

            if (e.StationId == StationId.Station3)
            {
                finalInspected++;
                if (!fail) finalPassed++;
            }
        }

        public void Increment(CounterType type) => Increase(counters, type);

        private static void Increase<TKey>(Dictionary<TKey, int> dict, TKey key)
        {
            dict.TryGetValue(key, out int n);
            dict[key] = n + 1;
        }

        private static int Get<TKey>(Dictionary<TKey, int> dict, TKey key) =>
            dict.TryGetValue(key, out int n) ? n : 0;
    }
}
