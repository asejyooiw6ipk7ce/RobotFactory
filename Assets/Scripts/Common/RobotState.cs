namespace RobotFactory
{
    /// 로봇 상태. Assembling은 로봇2 전용이다.
    public enum RobotState
    {
        Idle,
        Moving,
        Working,
        Assembling,
        Error
    }
}
