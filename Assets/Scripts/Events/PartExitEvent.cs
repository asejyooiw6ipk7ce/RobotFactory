namespace RobotFactory
{
    /// 부품이 출하·폐기·불량 컨베이어로 빠져나갈 때 발행되는 이벤트
    public readonly struct PartExitEvent
    {
        public readonly string PartId;
        public readonly ExitReason Reason;
        public readonly float Timestamp;

        public PartExitEvent(string partId, ExitReason reason, float timestamp)
        {
            PartId = partId;
            Reason = reason;
            Timestamp = timestamp;
        }
    }
}
