namespace RobotFactory
{
    /// 부품이 공정 밖으로 나간 이유
    public enum ExitReason
    {
        Shipped,        // 출하 컨베이어
        Discarded,      // 폐기 박스
        DefectConveyor  // 불량 컨베이어
    }
}
