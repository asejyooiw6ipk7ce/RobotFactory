using System.Collections.Generic;
using UnityEngine;

/// 로봇 상태를 보관한다 (FR-13).
/// 상태 변경은 static Set으로 발행하고, 대시보드가 Apply로 받아 반영한다.
public class RobotStatusTracker
{
    private const int Robot2Id = 2;

    private readonly Dictionary<int, RobotState> states = new Dictionary<int, RobotState>();

    /// 다른 팀원 코드에서 호출하는 진입점. 예: RobotStatusTracker.Set(1, RobotState.Working);
    public static void Set(int robotId, RobotState state) => InspectEventBus.PublishRobotState(robotId, state);

    public void Apply(int robotId, RobotState state)
    {
        // Assembling은 로봇2 전용
        if (state == RobotState.Assembling && robotId != Robot2Id)
        {
            Debug.LogWarning($"로봇{robotId}은 조립 상태를 가질 수 없습니다.");
            return;
        }
        states[robotId] = state;
    }

    public RobotState Get(int robotId) =>
        states.TryGetValue(robotId, out var state) ? state : RobotState.Idle;
}
