namespace RobotFactory
{
    /// 처리 개수 진입점. 다른 팀원은 이 한 줄로 개수를 올린다.
    /// 예: ProcessCounters.Add(CounterType.Shipped);
    public static class ProcessCounters
    {
        public static void Add(CounterType type) => InspectEventBus.PublishCounter(type);
    }
}
