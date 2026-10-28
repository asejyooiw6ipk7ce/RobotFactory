namespace RobotFactory
{
    /// 불량 종류. SRS 용어를 따른다.
    public enum DefectType
    {
        None,       // 정상
        PartLoss,   // 일부 부품 상실 (부품1·2)
        AngleError  // 각도 오차 (완제품 클로)
    }
}
