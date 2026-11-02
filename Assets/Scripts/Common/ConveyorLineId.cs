// 컨베이어 구간을 구분하는 ID입니다.
// 인스펙터와 스크립트에서 같은 값을 사용해 라인을 지정합니다.
public enum ConveyorLineId
{
    LineA,    // 부품 1 공급 및 이동 라인
    LineB,    // 부품 2 공급 및 이동 라인
    LineC,    // 조립 완제품 이동 라인
    Reject,   // 추후 불량품 분기 라인
    Shipping  // 추후 출하 라인
}