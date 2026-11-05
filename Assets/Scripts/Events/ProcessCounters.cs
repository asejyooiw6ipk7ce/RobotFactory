// 처리 개수를 올리라는 요청을 받는 입구
// 요청 받으면 CounterAdded 이벤트 발생해 대시보드 숫자 올라감
// 흐름 : ProcessCounters.Add -> InspectEventBus.PublishCounter -> CounterAdded -> 대시보드

public static class ProcessCounters{
    public static void Add(CounterType type){
        InspectEventBus.PublishCounter(type);
    }
}