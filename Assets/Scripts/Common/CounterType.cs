// 값이 씬/프리팹에 숫자로 저장되므로(RejectController.counterType 등) 기존 번호는 바꾸지 말고 새 항목은 뒤에 추가한다.
public enum CounterType
{
    InputBase = 0,      // 베이스 투입 (LineA 스포너 생성 시)
    InputClaw = 1,      // 클로 투입 (LineB 스포너 생성 시)
    Inspected = 2,      // 검사 완료 (Camera3 판정 시)
    DiscardedClaw = 3,  // 클로 폐기 (폐기함 도착 시)
    DiscardedBase = 4,  // 베이스 폐기 (폐기 상자 도착 시)
    Defective = 5,      // 완제품 불량 (Camera3 판정 시)
    Shipped = 6         // 출하 (출하 상자 도착 시)
}
