using System;

namespace RobotFactory
{
    /// 검사·이탈·처리 개수·로봇 상태 이벤트 채널.
    /// 구독자가 없어도 발행은 오류 없이 끝난다.
    public static class InspectEventBus
    {
        public static event Action<InspectEvent> InspectionCompleted;
        public static event Action<PartExitEvent> PartExited;
        public static event Action<CounterType> CounterAdded;
        public static event Action<int, RobotState> RobotStateChanged;

        public static void PublishInspection(InspectEvent e) => InspectionCompleted?.Invoke(e);

        public static void PublishPartExit(PartExitEvent e) => PartExited?.Invoke(e);

        public static void PublishCounter(CounterType type) => CounterAdded?.Invoke(type);

        public static void PublishRobotState(int robotId, RobotState state) => RobotStateChanged?.Invoke(robotId, state);
    }
}
