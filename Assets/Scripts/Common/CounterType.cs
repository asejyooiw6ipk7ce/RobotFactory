namespace RobotFactory
{
    /// 처리 개수 5종 (FR-12)
    public enum CounterType
    {
        Input,          // 투입
        Inspected,      // 검사 완료 (완제품 검사 수)
        Discarded,      // 폐기
        DefectConveyor, // 불량 컨베이어 이동
        Shipped         // 출하
    }
}
