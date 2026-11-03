using System.Collections;
using UnityEngine;

public enum ProcessState { Stopped, Running, Paused, Alarm }

public class ProcessController : MonoBehaviour
{
    [Header("연결")]
    [SerializeField] private ConveyorManager conveyors;     // LineA(베이스), LineB(클로)
    [SerializeField] private Camera2Inspector camera2;      // 부품2 검사
    [SerializeField] private AssemblySequence assembly;     // 조립·완제품 검사·폐기
    [SerializeField] private InspectStation station1;        // 클로 검사 (양품 통과 / 불량 폐기)
    [SerializeField] private PusherController basePusher;   // 불량 베이스를 불량 컨테이너로 밀어냄
    [SerializeField] private ItemSpawner baseSpawner;       // LineA 베이스 생성기
    [SerializeField] private ItemSpawner clawSpawner;       // LineB 클로 생성기

    public ProcessState State { get; private set; } = ProcessState.Stopped;
    public float SimTime { get; private set; }

    // 정지점에 도착해 대기 중인 부품
    private GameObject waitingPart;   // LineB (클로)
    private GameObject waitingBase;   // LineA (베이스)
    private Coroutine cycle;
    private bool clawInspectPassed;   // 이번 사이클에서 클로가 검사를 '통과'했는지 (아니면 조립 금지)

    private void OnEnable()
    {
        if (conveyors != null) conveyors.OnItemArrivedAtStopPoint += OnArrived;
        if (basePusher != null) basePusher.ItemPushed += OnBaseDisposed;
    }

    private void OnDisable()
    {
        if (conveyors != null) conveyors.OnItemArrivedAtStopPoint -= OnArrived;
        if (basePusher != null) basePusher.ItemPushed -= OnBaseDisposed;
    }

    private void Update()
    {
        if (State == ProcessState.Running) SimTime += Time.deltaTime;
    }

    public void Run()
    {
        if (State == ProcessState.Alarm) return;   // 알람은 Reset 후에만 재시작
        State = ProcessState.Running;
        conveyors.StartAll();
        if (cycle == null) cycle = StartCoroutine(RunCycle());
    }

    public void Pause()
    {
        if (State != ProcessState.Running) return;
        State = ProcessState.Paused;
        conveyors.PauseAll();
    }

    public void Reset()
    {
        if (cycle != null) StopCoroutine(cycle);
        cycle = null;
        waitingPart = null;
        waitingBase = null;
        conveyors.ResumeAll();     // 일시정지 플래그 해제
        conveyors.StopAll();
        assembly.ResetToInitialState();
        // 스포너 초기화: 남은 아이템 제거 + 생성 상태 복구 (안 하면 readyToSpawn이 false로 남아 재시작 후 생성이 안 됨)
        if (baseSpawner != null) baseSpawner.ClearSpawned();
        if (clawSpawner != null) clawSpawner.ClearSpawned();
        SimTime = 0f;
        State = ProcessState.Stopped;
    }

    // ConveyorManager가 정지점 도착을 알려줌
    private void OnArrived(ConveyorLineId line, int waypointIndex, GameObject item)
    {
        if (line == ConveyorLineId.LineB) waitingPart = item;
        else if (line == ConveyorLineId.LineA) waitingBase = item;
    }

    // 푸셔가 불량 베이스를 밀어내 불량 컨테이너로 보낸 뒤 호출 → 다음 베이스 생성
    private void OnBaseDisposed(GameObject item)
    {
        if (baseSpawner != null) baseSpawner.NotifyItemCleared();
    }

    private IEnumerator RunCycle()
    {
        while (true)
        {
            // 1) 베이스가 검사 정지점에 도착할 때까지 대기 (일시정지·알람 중에는 진행 안 함)
            yield return new WaitUntil(() =>
                State == ProcessState.Running && waitingBase != null);

            var robotBase = waitingBase.GetComponentInChildren<BaseInfo>(true);
            if (robotBase == null)
            {
                Debug.LogError($"[ProcessController] 정지점의 베이스 '{waitingBase.name}'에 BaseInfo가 없어 검사를 중단하고 알람으로 전환합니다.", this);
                State = ProcessState.Alarm;
                conveyors.StopAll();
                continue;
            }
            var baseMover = waitingBase.GetComponent<ConveyorItemMover>();   // waitingBase는 아래에서 null이 되므로 먼저 잡아 둠
            var baseItem = waitingBase.GetComponent<Item>();

            // 2) 부품2(베이스) 비전 검사
            bool baseOk = camera2.InspectPart2(robotBase);
            waitingBase = null;   // 검사 정지점 도착 처리 완료 (다음 도착은 조립 정지점)

            // 검사 결과를 Item에 반영해야 푸셔(Item.itemType 기준)가 올바르게 판단함
            if (baseItem != null)
                baseItem.itemType = baseOk ? ItemType.Normal : ItemType.Defective;

            // 3) LineA를 풀어 베이스를 이송
            //    - 불량: 푸셔가 밀어내면 OnBaseDisposed에서 다음 베이스 생성
            //    - 양품: 조립 정지점으로 이동했으므로 바로 다음 베이스 생성
            conveyors.ReleaseItemAndRunLine(ConveyorLineId.LineA, baseMover);
            if (baseOk && baseSpawner != null)
                baseSpawner.NotifyItemCleared();
            if (!baseOk)
                continue;

            // 4) 양품 베이스가 조립 정지점에 도착할 때까지 대기
            yield return new WaitUntil(() =>
                State == ProcessState.Running && waitingBase != null);

            // 5) 클로가 LineB 정지점에 도착할 때까지 대기 후 클로 검사 (불량이면 폐기하고 다음 클로까지 대기)
            yield return new WaitUntil(() =>
                State == ProcessState.Running && waitingPart != null);
            yield return InspectClawUntilPass();
            if (!clawInspectPassed)
            {
                // 검사 통과 없이 조립으로 넘어가지 않도록 알람 정지
                State = ProcessState.Alarm;
                conveyors.StopAll();
                continue;
            }

            // 5-1) 검사 통과한 클로를 LineB 조립 정지점(2번 정지점)까지 이송한 뒤 도착을 기다림
            //      (검사 정지점에서 바로 조립하면 클로가 조립 위치에 없음)
            var clawGo = waitingPart;
            var part = clawGo.GetComponent<PartInfo>();
            var partMover = clawGo.GetComponent<ConveyorItemMover>();
            waitingPart = null;
            conveyors.ReleaseItemAndRunLine(ConveyorLineId.LineB, partMover);
            yield return new WaitUntil(() =>
                State == ProcessState.Running && waitingPart == clawGo);
            // 이 클로가 조립 정지점에 도착한 뒤에만 다음 클로 생성 (앞 클로 이동 중 다음 클로가 검사 정지점에 닿으면 LineB 전체가 멈춤)
            if (clawSpawner != null) clawSpawner.NotifyItemCleared();

            var assemblyBase = waitingBase.GetComponentInChildren<BaseInfo>(true);
            if (assemblyBase == null)
                Debug.LogError($"[ProcessController] 조립 정지점의 베이스 '{waitingBase.name}'에 BaseInfo가 없습니다.", this);
            var assemblyBaseMover = waitingBase.GetComponent<ConveyorItemMover>();

            // 6) 조립 (AssemblySequence의 자동 시작은 꺼 둔 상태여야 함)
            LogPickPositions("조립");
            assembly.NotifyPart1Ready(part);
            assembly.NotifyPart2Ready(assemblyBase);
            yield return assembly.Assemble(-1f);

            // 조립한 클로만 비움 (다음 클로가 이미 도착해 기록됐다면 유지)
            if (waitingPart == clawGo) waitingPart = null;
            waitingBase = null;

            // 7) 결과 확인
            if (!assembly.LastAssemblyOk)
            {
                State = ProcessState.Alarm;
                conveyors.StopAll();
                continue;   // Alarm 상태라 WaitUntil에서 멈춤 → Reset 전까지 대기
            }

            // 8) 두 라인 재가동 (완제품 배출·카운터는 AssemblySequence/Camera3가 처리)
            conveyors.ReleaseItemAndRunLine(ConveyorLineId.LineB, partMover);
            conveyors.ReleaseItemAndRunLine(ConveyorLineId.LineA, assemblyBaseMover);
        }
    }

    // [좌표 디버그] 검사/조립 픽업 좌표 비교용 로그 (확인 후 제거)
    private void LogPickPositions(string tag)
    {
        Vector3 claw = waitingPart != null ? waitingPart.transform.position : Vector3.zero;
        Vector3 inspectPos = (station1 != null && station1.inspectPose != null) ? station1.inspectPose.position : Vector3.zero;
        Vector3 rejectPos = (station1 != null && station1.reject != null && station1.reject.pickPoint != null) ? station1.reject.pickPoint.position : Vector3.zero;
        Vector3 assemblyPick = assembly != null ? assembly.GetPickPosition() : Vector3.zero;
        Debug.Log($"[PosLog:{tag}] 클로 위치={claw} | 검사 inspectPose={inspectPos} | 폐기 pickPoint={rejectPos} | 조립 GetPickPosition={assemblyPick}", this);
    }

    // 클로가 검사를 통과할 때까지 반복한다.
    // 통과: 스토퍼 재개는 InspectSequence 안에서 처리되고, 클로 생성 트리거를 보낸다.
    // 불량: InspectSequence 안에서 로봇팔이 폐기 후 스토퍼를 재개하고, 다음 클로가 올 때까지 대기한다.
    private IEnumerator InspectClawUntilPass()
    {
        clawInspectPassed = false;
        while (true)
        {
            var clawItem = waitingPart.GetComponent<Item>();
            if (station1 == null || clawItem == null)
            {
                Debug.LogError("[ProcessController] 클로 검사를 할 수 없습니다 (station1 또는 클로의 Item이 없음).", this);
                yield break;   // clawInspectPassed는 false 유지 → RunCycle에서 알람
            }

            LogPickPositions("검사");
            // 스포너가 정한 Item.itemType을 PartInfo.defectType에 반영 (develop의 InspectStation은 PartInfo 기준으로 판정)
            var clawInfo = waitingPart.GetComponent<PartInfo>();
            if (clawInfo != null)
                clawInfo.defectType = clawItem.itemType == ItemType.Defective ? DefectType.PartLoss : DefectType.None;
            yield return station1.InspectSequence(waitingPart.transform, clawInfo);

            if (station1.LastResult != InspectResult.Fail)
            {
                clawInspectPassed = true;   // 다음 클로 생성은 이 클로가 조립 정지점에 도착한 뒤 (RunCycle)
                yield break;
            }

            // 불량 클로: 폐기 완료 → LineB 재가동(정지 상태면 스포너가 다음 클로를 생성하지 않음) 후 다음 클로 생성 → 도착을 기다림
            conveyors.ReleaseItemAndRunLine(ConveyorLineId.LineB, null);
            if (clawSpawner != null) clawSpawner.NotifyItemCleared();
            waitingPart = null;
            yield return new WaitUntil(() =>
                State == ProcessState.Running && waitingPart != null);
        }
    }
}
