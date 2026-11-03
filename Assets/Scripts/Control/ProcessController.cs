using System.Collections;
using UnityEngine;

public enum ProcessState { Stopped, Running, Paused, Alarm }

public class ProcessController : MonoBehaviour
{
    [Header("연결")]
    [SerializeField] private ConveyorManager conveyors;     // LineA(베이스), LineB(클로), LineC(완제품)
    [SerializeField] private Camera2Inspector camera2;      // 부품2 검사
    [SerializeField] private Camera3Inspector camera3;      // 완제품(카메라3) 조립 검사
    [SerializeField] private AssemblySequence assembly;     // 조립·완제품 검사·폐기
    [SerializeField] private InspectStation station1;        // 클로 검사 (양품 통과 / 불량 폐기)
    [SerializeField] private PusherController basePusher;   // 불량 베이스를 불량 컨테이너로 밀어냄
    [SerializeField] private ItemSpawner baseSpawner;       // LineA 베이스 생성기
    [SerializeField] private ItemSpawner clawSpawner;       // LineB 클로 생성기

    [Header("조립 성공률 통계")]
    [Tooltip("총 완제품 조립/검사 수")]
    [SerializeField] private int totalAssembledCount = 0;
    [Tooltip("조립 성공(양품) 수")]
    [SerializeField] private int successCount = 0;
    [Tooltip("결합 각도 불량 수")]
    [SerializeField] private int failCount = 0;
    [Tooltip("조립 성공률 (%)")]
    [SerializeField] private float successRate = 0f;

    public ProcessState State { get; private set; } = ProcessState.Stopped;
    public float SimTime { get; private set; }

    // 정지점에 도착해 대기 중인 부품
    private GameObject waitingPart;   // LineB (클로)
    private GameObject waitingBase;   // LineA (베이스)
    private Coroutine cycle;
    private bool clawInspectPassed;   // 이번 사이클에서 클로가 검사를 '통과'했는지 (아니면 조립 금지)

    // 완제품 퇴출(RejectBox or ShippingBox 파괴) 완료 신호 (단일 부품 리스폰 정책용)
    private bool productClearedSignal;

    private void Awake()
    {
        if (assembly != null)
            assembly.autoStartWhenBothPartsReady = false;
        if (camera3 == null && assembly != null)
            camera3 = assembly.camera3Inspector;
        if (camera3 == null)
            camera3 = FindAnyObjectByType<Camera3Inspector>();
    }

    private void OnEnable()
    {
        if (conveyors != null) conveyors.OnItemArrivedAtStopPoint += OnArrived;
        if (basePusher != null) basePusher.ItemPushed += OnBaseDisposed;
        RejectController.OnProductCleared += HandleProductCleared;
    }

    private void OnDisable()
    {
        if (conveyors != null) conveyors.OnItemArrivedAtStopPoint -= OnArrived;
        if (basePusher != null) basePusher.ItemPushed -= OnBaseDisposed;
        RejectController.OnProductCleared -= HandleProductCleared;
    }

    private void HandleProductCleared(CounterType counter)
    {
        Debug.Log($"[ProcessController] 🏁 완제품 상자 도달 및 파괴 확인(타입: {counter}). 다음 부품 스폰 신호 수신.", this);
        productClearedSignal = true;
    }

    private void Update()
    {
        if (State == ProcessState.Running) SimTime += Time.deltaTime;
    }

    public void Run()
    {
        if (State == ProcessState.Alarm)
        {
            Debug.LogWarning("[ProcessController] 현재 알람 상태입니다. Reset을 먼저 수행한 후 다시 Run 해주세요.", this);
            return;
        }
        State = ProcessState.Running;
        Debug.Log("[ProcessController] 공정 시작 (Running)!", this);
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
        productClearedSignal = false;
        conveyors.ResumeAll();     // 일시정지 플래그 해제
        conveyors.StopAll();
        conveyors.RaiseStoppers(ConveyorLineId.LineC);
        assembly.ResetToInitialState();
        // 스포너 초기화: 남은 아이템 제거 + 생성 상태 복구 (안 하면 readyToSpawn이 false로 남아 재시작 후 생성이 안 됨)
        if (baseSpawner != null) baseSpawner.ClearSpawned();
        if (clawSpawner != null) clawSpawner.ClearSpawned();

        // 조립 성공률 통계 초기화
        totalAssembledCount = 0;
        successCount = 0;
        failCount = 0;
        successRate = 0f;

        SimTime = 0f;
        State = ProcessState.Stopped;
    }

    // ConveyorManager가 정지점 도착을 알려줌
    private void OnArrived(ConveyorLineId line, int waypointIndex, GameObject item)
    {
        Debug.Log($"[ProcessController] 정지점 도착 수신: 라인={line}, 웨이포인트={waypointIndex}, 아이템='{item?.name}'", this);
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
            Debug.Log("[ProcessController] [1단계] Line A 베이스 도착 대기 중...", this);
            yield return new WaitUntil(() =>
                State == ProcessState.Running && waitingBase != null);
            Debug.Log($"[ProcessController] [1단계 완료] 베이스 '{waitingBase.name}' 도착 감지. 카메라2 검사 진행.", this);

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
            //    - 불량 베이스: 푸셔가 밀어내면 OnBaseDisposed에서 새 베이스 생성
            //    - 양품 베이스: 단일 부품 정책에 따라 완제품 최종 퇴출 시까지 새 베이스 스폰 대기
            conveyors.ReleaseItemAndRunLine(ConveyorLineId.LineA, baseMover);
            if (!baseOk)
                continue;

            // 4) 양품 베이스가 조립 정지점에 도착할 때까지 대기
            Debug.Log("[ProcessController] [4단계] 양품 베이스 조립 정지점 도착 대기 중...", this);
            yield return new WaitUntil(() =>
                State == ProcessState.Running && waitingBase != null);
            Debug.Log($"[ProcessController] [4단계 완료] 베이스 '{waitingBase.name}' 조립 위치 도착.", this);

            // 5) 클로가 LineB 정지점에 도착할 때까지 대기 후 클로 검사 (불량이면 폐기하고 다음 클로까지 대기)
            Debug.Log("[ProcessController] [5단계] Line B 클로 도착 대기 중...", this);
            yield return new WaitUntil(() =>
                State == ProcessState.Running && waitingPart != null);
            Debug.Log($"[ProcessController] [5단계] 클로 '{waitingPart.name}' 도착 감지. 클로 비전 검사 시작...", this);

            yield return StartCoroutine(InspectClawUntilPass());
            if (!clawInspectPassed)
            {
                // 검사 통과 없이 조립으로 넘어가지 않도록 알람 정지
                Debug.LogError("[ProcessController] 클로 검사 실패로 공정을 중단하고 알람으로 전환합니다.", this);
                State = ProcessState.Alarm;
                conveyors.StopAll();
                continue;
            }

            // 5-1) 검사 통과한 클로를 LineB 조립 정지점(2번 정지점)까지 이송한 뒤 도착을 기다림
            //      (단, Line B에 정지점이 1개뿐인 경우 검사 위치에서 바로 조립 진행)
            var clawGo = waitingPart;
            var part = clawGo.GetComponent<PartInfo>() ?? clawGo.GetComponentInChildren<PartInfo>(true);
            var partMover = clawGo.GetComponent<ConveyorItemMover>() ?? clawGo.GetComponentInChildren<ConveyorItemMover>(true);

            var lineBStopIndices = conveyors.GetStopWaypointIndices(ConveyorLineId.LineB);
            bool hasMultipleStops = lineBStopIndices != null && lineBStopIndices.Length > 1;

            if (hasMultipleStops)
            {
                Debug.Log("[ProcessController] [5-1단계] 검사 통과 클로 조립 정지점으로 이송 시작.", this);
                waitingPart = null;
                conveyors.ReleaseItemAndRunLine(ConveyorLineId.LineB, partMover);
                Debug.Log("[ProcessController] [5-1단계] 클로 조립 정지점 도착 대기 중...", this);
                yield return new WaitUntil(() =>
                    State == ProcessState.Running && waitingPart == clawGo);
                Debug.Log("[ProcessController] [5-1단계 완료] 클로 조립 위치 도착!", this);
            }
            else
            {
                Debug.LogWarning("[ProcessController] [5-1단계] Line B 정지점(Stop Waypoint Indices)이 1개뿐입니다. 클로가 조립 로봇팔 위치로 이동하려면 ConveyorManager의 Line B에 2차 정지점(로봇팔2 앞 인덱스)을 등록해야 합니다.", this);
            }

            var assemblyBase = waitingBase.GetComponentInChildren<BaseInfo>(true);
            if (assemblyBase == null)
                Debug.LogError($"[ProcessController] 조립 정지점의 베이스 '{waitingBase.name}'에 BaseInfo가 없습니다.", this);
            var assemblyBaseMover = waitingBase.GetComponent<ConveyorItemMover>();

            // 6) 조립 (ProcessController가 조립 완료를 직접 기다림)
            LogPickPositions("조립");
            if (assembly != null)
            {
                // 중복 실행 및 충돌 방지를 위해 자동 시작 비활성화
                assembly.autoStartWhenBothPartsReady = false;
                assembly.SetPart(part != null ? part.transform : clawGo.transform);
                assembly.SetBase(assemblyBase);

                if (!assembly.IsBusy)
                {
                    yield return assembly.Assemble(-1f);
                }
                else
                {
                    // 만약 이미 시작된 상태라면 조립이 끝날 때까지 대기
                    yield return new WaitUntil(() => !assembly.IsBusy);
                }
            }

            // 조립한 클로만 비움 (다음 클로가 이미 도착해 기록됐다면 유지)
            if (waitingPart == clawGo) waitingPart = null;
            waitingBase = null;

            // 7) 결과 확인
            if (!assembly.LastAssemblyOk)
            {
                Debug.LogError("[ProcessController] 로봇팔 조립 실패로 공정을 중단하고 알람으로 전환합니다.", this);
                State = ProcessState.Alarm;
                conveyors.StopAll();
                continue;   // Alarm 상태라 WaitUntil에서 멈춤 → Reset 전까지 대기
            }

            // 카메라3 완제품 비전 검사 판정 확인 (Camera3Inspector)
            if (camera3 == null && assembly != null)
                camera3 = assembly.camera3Inspector;

            bool isProductPass = true;
            if (camera3 != null && camera3.LastResult.HasValue)
            {
                isProductPass = (camera3.LastResult.Value == InspectResult.Pass);
            }

            // [Commit 3] 조립 성공률 누적 (과제 6.8 요구사항)
            totalAssembledCount++;
            if (isProductPass) successCount++;
            else failCount++;
            successRate = totalAssembledCount > 0 ? (float)successCount / totalAssembledCount * 100f : 0f;

            if (isProductPass)
            {
                Debug.Log($"[ProcessController] ⭐ [조립 성공률] {successCount}/{totalAssembledCount}회 ({successRate:F1}%) | 완제품 양품 판정. 출하 라인(Line C)으로 이송합니다.", this);
            }
            else
            {
                Debug.LogWarning($"[ProcessController] ⭐ [조립 성공률] {successCount}/{totalAssembledCount}회 ({successRate:F1}%) | 완제품 결합 각도 불량(AngleError) 감지. Line C 푸셔 배출을 위해 무조건 이송을 진행합니다.", this);
            }

            // 8) 라인 가동 및 완제품 Line C 이송 (양품/불량 불문 무조건 이송)
            // 8-1) Line B(클로 공급 라인) 스토퍼 해제 및 라인 재가동
            conveyors.ReleaseItemAndRunLine(ConveyorLineId.LineB, partMover);

            // 8-2) Line C(완제품 라인)로 차체 이관 및 이송 가동
            if (assemblyBaseMover != null)
            {
                // 차체의 소속 라인을 LineA -> LineC로 전환 (좌측 Vector3.left 방향, 0번 웨이포인트부터 추적)
                assemblyBaseMover.TransferToConveyor(conveyors, ConveyorLineId.LineC, Vector3.left);
            }
            // Line C 스토퍼(WaitStopper 4) 하강 및 Line C 컨베이어 가동
            conveyors.LowerStoppers(ConveyorLineId.LineC);
            conveyors.RunLine(ConveyorLineId.LineC);

            // 8-3) [Commit 4] 단일 부품 정책: 완제품이 Line C를 거쳐 최종 상자(RejectBox 또는 ShippingBox)에 도달해 파괴될 때까지 대기
            productClearedSignal = false;
            Debug.Log("[ProcessController] [8-3단계] 완제품이 Line C를 거쳐 최종 상자(RejectBox/ShippingBox)에 퇴출될 때까지 대기 중...", this);

            yield return new WaitUntil(() => State != ProcessState.Running || productClearedSignal);

            if (State != ProcessState.Running)
                continue;

            Debug.Log("[ProcessController] [8-3단계 완료] 완제품 퇴출 완료 확인! 다음 사이클 부품 스폰을 시작합니다.", this);

            // 8-4) 완제품이 완전히 사라진 후에만 다음 사이클 베이스와 클로 스폰 허용
            if (baseSpawner != null) baseSpawner.NotifyItemCleared();
            if (clawSpawner != null) clawSpawner.NotifyItemCleared();

            // 8-5) Line A(베이스 공급 라인) 재가동: 새 베이스가 조립 정지점으로 진입할 수 있도록 재개
            conveyors.ReleaseItemAndRunLine(ConveyorLineId.LineA, null);
            conveyors.ReleaseItemAndRunLine(ConveyorLineId.LineB, null);
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
            if (station1 == null || waitingPart == null)
            {
                Debug.LogError("[ProcessController] 클로 검사를 할 수 없습니다 (station1 또는 waitingPart가 없음).", this);
                yield break;
            }

            var clawItem = waitingPart.GetComponent<Item>()
                           ?? waitingPart.GetComponentInChildren<Item>(true)
                           ?? waitingPart.GetComponentInParent<Item>();
            var clawInfo = waitingPart.GetComponent<PartInfo>()
                           ?? waitingPart.GetComponentInChildren<PartInfo>(true)
                           ?? waitingPart.GetComponentInParent<PartInfo>();

            // 스포너 프리팹(claw.prefab 등)에 Item 또는 PartInfo가 누락되었을 때 자동 보완
            if (clawItem == null)
            {
                clawItem = waitingPart.AddComponent<Item>();
                clawItem.itemType = ItemType.Normal;
            }

            if (clawInfo == null)
            {
                clawInfo = waitingPart.AddComponent<PartInfo>();
            }

            LogPickPositions("검사");
            // 스포너가 정한 Item.itemType을 PartInfo.defectType에 반영 (develop의 InspectStation은 PartInfo 기준으로 판정)
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
