using UnityEngine;

/// 검증용 더미. 다른 팀의 검사 로직이 붙으면 이 Dummy 폴더째 지운다.
/// 스테이션 무작위 골라 - 10% 확률로 합격/불량을 정함 - AngleError/PartLoss/None 붙임 - 검사 이벤트 발행(InspectEventBus.Publish()) - 대시보드가 받아 화면 갱신 - 처리 개수 카운터 올라감
public class DummyEventEmitter : MonoBehaviour
{
    [Header("스테이션별 불량 확률 (0~1)")]
    [SerializeField, Range(0f, 1f)] private float station1DefectRate = 0.1f;
    [SerializeField, Range(0f, 1f)] private float station2DefectRate = 0.1f;
    [SerializeField, Range(0f, 1f)] private float station3DefectRate = 0.1f;

    [Header("발행 간격 (초)")]
    [SerializeField, Min(0.1f)] private float minInterval = 1f;
    [SerializeField, Min(0.1f)] private float maxInterval = 2f;

    private float nextEmitTime;
    private int serial;

    private void Start()
    {
        ScheduleNext();
    }

    private void Update()
    {
        if (Time.time < nextEmitTime) return;

        EmitOne();
        ScheduleNext();
    }

    private void ScheduleNext()
    {
        nextEmitTime = Time.time + Random.Range(minInterval, Mathf.Max(minInterval, maxInterval));
    }

    private void EmitOne()
    {
        //1. 스테이션 무작위로 고름(1~3)
        var station = (StationId)Random.Range(0, 3); // 1~3
        //2. 성공/불량 고름
        bool fail = Random.value < RateOf(station);   //랜덤값이 0.1보다 작으면 불량(true) = 랜덤값이 거기에 떨어질 확률 10%                                   //stationNDefectRate(10%로 동일되어있음)
        serial++;

        // InspectResult 입력 ; fail값에 따라 result에 Fail/Pass 저장
        var result = fail ? InspectResult.Fail : InspectResult.Pass;

        //3. DefectType 입력 ; fail이면 AngleError,PartLoss / sucess면 None
        var defect = fail
            ? (station == StationId.Station3 ? DefectType.AngleError : DefectType.PartLoss)
            : DefectType.None;

        //4. InspectEventBus.Publish() -> 대시보드는 이걸 구독하고 있어서 받을 수 있음
        InspectEventBus.Publish(
            new InspectEvent($"DUMMY-{serial:0000}", station, result, defect, Time.time));

        // 처리 개수: 흐름에 맞춰 함께 증가
        switch (station)
        {
            case StationId.Station1:
                ProcessCounters.Add(CounterType.Input);  //스테이션1 투입 +1
                if (fail) ProcessCounters.Add(CounterType.Discarded); //fail true면 스테이션1 불량 +1
                break;
            case StationId.Station2:
                if (fail) ProcessCounters.Add(CounterType.Defective); //fail true면 스테이션2 불량 +1
                break;
            case StationId.Station3:
                ProcessCounters.Add(CounterType.Inspected);
                ProcessCounters.Add(fail ? CounterType.Defective : CounterType.Shipped);
                break;
        }
    }

    private float RateOf(StationId station)
    {
        switch (station)
        {
            case StationId.Station1: return station1DefectRate;
            case StationId.Station2: return station2DefectRate;
            default: return station3DefectRate;
        }
    }
}
