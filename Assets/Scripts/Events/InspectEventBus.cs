using System;

public static class InspectEventBus{
    public static event Action<InspectEvent> Published;
    public static event Action<PartExitEvent> PartExited;
    public static event Action<CounterType> CounterAdded;
    public static event Action<int, RobotState> RobotStateChanged;

    public static void Publish(InspectEvent e){
        Published?.Invoke(e);
    }
    public static void PublishExit(PartExitEvent e){
        PartExited?.Invoke(e);
    }
    public static void PublishCounter(CounterType type){
        CounterAdded?.Invoke(type);
    }
    
    public static void PublishRobotState(int robotId, RobotState state){
        RobotStateChanged?.Invoke(robotId, state);
    }
}