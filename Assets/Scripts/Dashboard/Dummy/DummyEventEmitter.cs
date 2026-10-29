using UnityEngine;

namespace RobotFactory
{
    /// 검증용 더미. 다른 팀의 검사 로직이 붙으면 이 Dummy 폴더째 지운다.
    /// 1~2초마다 가짜 검사 이벤트와 처리 개수를 발행한다.
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
            var station = (StationId)Random.Range(1, 4); // 1~3
            bool fail = Random.value < RateOf(station);
            serial++;

            var result = fail ? InspectResult.Fail : InspectResult.Pass;
            var defect = fail
                ? (station == StationId.Station3 ? DefectType.AngleError : DefectType.PartLoss)
                : DefectType.None;

            InspectEventBus.PublishInspection(
                new InspectEvent($"DUMMY-{serial:0000}", station, result, defect, Time.time));

            // 처리 개수: 흐름에 맞춰 함께 증가
            switch (station)
            {
                case StationId.Station1:
                    ProcessCounters.Add(CounterType.Input);
                    if (fail) ProcessCounters.Add(CounterType.Discarded);
                    break;
                case StationId.Station2:
                    if (fail) ProcessCounters.Add(CounterType.Defective);
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
}
