public enum CounterType
{
    InputBase,  // 베이스 투입 (LineA 스포너 생성)
    InputClaw,  // 클로 투입 (LineB 스포너 생성)
    Inspected,  // 검사 완료 (완제품 검사 수)
    DiscardedClaw,  // 클로 폐기 (폐기함)
    DiscardedBase,  // 베이스 폐기 (리젝트 끝)
    Defective,  // 완제품 불량 (카메라3 Fail)
    Shipped     // 출하
}
