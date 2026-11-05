using System.Collections;
using UnityEngine;

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

    [Header("LineA 정지 웨이포인트")]
    [Tooltip("베이스 비전 검사 위치의 LineA 정지 웨이포인트 인덱스")]
    [SerializeField] private int baseInspectionWaypointIndex = 0;
    [Tooltip("베이스 조립 위치의 LineA 정지 웨이포인트 인덱스")]
    [SerializeField] private int baseAssemblyWaypointIndex = 1;

    [Header("LineB 정지 웨이포인트")]
    [Tooltip("클로 비전 검사 위치의 LineB 정지 웨이포인트 인덱스")]
    [SerializeField] private int clawInspectionWaypointIndex = 0;
    [Tooltip("클로 조립 위치의 LineB 정지 웨이포인트 인덱스")]
    [SerializeField] private int clawAssemblyWaypointIndex = 4;

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

    // 정지점에 도착해 대기 중인 부품 (라인 및 정지점별 분리)
    private GameObject waitingBaseAtInspection;
    private GameObject waitingBaseAtAssembly;
    private GameObject waitingPartAtInspection;
    private GameObject waitingPartAtAssembly;

    // 조립 지점에 준비 완료된 부품
    private GameObject assemblyBaseObject;
    private GameObject assemblyClawObject;
    private GameObject passedClawObject;

    private Coroutine cycle;
    private Coroutine baseRoutine;
    private Coroutine clawRoutine;
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

        SyncWaypointIndicesWithConveyors();
    }

    /// <summary>
    /// ConveyorManager에 등록된 실제 정지 웨이포인트 인덱스를 동적으로 읽어와 검사/조립 인덱스를 자동 보정합니다.
    /// </summary>
    private void SyncWaypointIndicesWithConveyors()
    {
        if (conveyors == null) return;

        var lineAStops = conveyors.GetStopWaypointIndices(ConveyorLineId.LineA);
        if (lineAStops != null && lineAStops.Length > 0)
        {
            baseInspectionWaypointIndex = lineAStops[0];
            if (lineAStops.Length > 1)
                baseAssemblyWaypointIndex = lineAStops[lineAStops.Length - 1];
        }

        var lineBStops = conveyors.GetStopWaypointIndices(ConveyorLineId.LineB);
        if (lineBStops != null && lineBStops.Length > 0)
        {
            clawInspectionWaypointIndex = lineBStops[0];
            if (lineBStops.Length > 1)
                clawAssemblyWaypointIndex = lineBStops[lineBStops.Length - 1];
        }

        Debug.Log($"[ProcessController] 웨이포인트 정지점 동기화 완료: LineA(검사={baseInspectionWaypointIndex}, 조립={baseAssemblyWaypointIndex}) | LineB(검사={clawInspectionWaypointIndex}, 조립={clawAssemblyWaypointIndex})", this);
    }

    private void OnEnable()
    {
        if (conveyors != null) conveyors.OnItemArrivedAtStopPoint += OnArrived;
        if (basePusher != null) basePusher.ItemPushed += OnBasePushedToReject;
        RejectController.OnProductCleared += HandleProductCleared;
        LineCRejectController.OnProductCleared += HandleProductCleared;
    }

    private void OnDisable()
    {
        if (conveyors != null) conveyors.OnItemArrivedAtStopPoint -= OnArrived;
        if (basePusher != null) basePusher.ItemPushed -= OnBasePushedToReject;
        RejectController.OnProductCleared -= HandleProductCleared;
        LineCRejectController.OnProductCleared -= HandleProductCleared;
    }

    private void HandleProductCleared(CounterType counter)
    {
        Debug.Log($"[ProcessController] 상자 도달 및 파괴 확인(타입: {counter}).", this);

        // 불량 베이스는 푸셔가 Reject 라인으로 옮긴 직후가 아니라,
        // RejectController가 상자에서 실제 파괴한 뒤 다음 베이스를 허용합니다.
        if (counter == CounterType.DiscardedBase)
        {
            if (baseSpawner != null)
                baseSpawner.NotifyItemCleared();

            return;
        }

        // 최종 제품의 출하/불량 퇴출만 현재 조립 사이클 완료로 처리합니다.
        if (counter == CounterType.Shipped || counter == CounterType.Defective)
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
        Debug.Log("[ProcessController] 공정 시작 (Running)!", this);
        ChangeState(ProcessState.Running);
        conveyors.StartAll();
        if (cycle == null) cycle = StartCoroutine(RunCycle());
    }

    public void Pause()
    {
        if (State != ProcessState.Running) return;
        ChangeState(ProcessState.Paused);
        conveyors.PauseAll();
    }

    public void ResetProcess()
    {
        StopAllCoroutines();
        cycle = null;
        baseRoutine = null;
        clawRoutine = null;

        waitingBaseAtInspection = null;
        waitingBaseAtAssembly = null;
        waitingPartAtInspection = null;
        waitingPartAtAssembly = null;
        assemblyBaseObject = null;
        assemblyClawObject = null;
        passedClawObject = null;

        productClearedSignal = false;
        conveyors.ResumeAll();     // 일시정지 플래그 해제
        conveyors.StopAll();
        conveyors.RaiseStoppers(ConveyorLineId.LineC);
        assembly.ResetToInitialState();
        InspectEventBus.PublishReset();     // Reset 버튼 누르면 대시보드 초기화

        // 스포너 초기화: 남은 아이템 제거 + 생성 상태 복구 (안 하면 readyToSpawn이 false로 남아 재시작 후 생성이 안 됨)
        if (baseSpawner != null) baseSpawner.ClearSpawned();
        if (clawSpawner != null) clawSpawner.ClearSpawned();

        // 조립 성공률 통계 초기화
        totalAssembledCount = 0;
        successCount = 0;
        failCount = 0;
        successRate = 0f;

        SimTime = 0f;
        ChangeState(ProcessState.Stopped);
    }

    private bool IsLastStopWaypoint(ConveyorLineId lineId, int waypointIndex)
    {
        if (conveyors == null) return false;
        var stops = conveyors.GetStopWaypointIndices(lineId);
        return stops != null && stops.Length > 1 && stops[stops.Length - 1] == waypointIndex;
    }

    // ConveyorManager가 정지점 도착을 알려줌
    private void OnArrived(ConveyorLineId line, int waypointIndex, GameObject item)
    {
        Debug.Log($"[ProcessController] 정지점 도착 수신: 라인={line}, 웨이포인트={waypointIndex}, 아이템='{item?.name}'", this);

        if (line == ConveyorLineId.LineB)
        {
            if (waypointIndex == clawInspectionWaypointIndex)
            {
                waitingPartAtInspection = item;
                Debug.Log($"[ProcessController] 클로 검사 정지점 도착 저장: '{item?.name}'", this);
            }
            else if (waypointIndex == clawAssemblyWaypointIndex || IsLastStopWaypoint(ConveyorLineId.LineB, waypointIndex))
            {
                waitingPartAtAssembly = item;
                Debug.Log($"[ProcessController] 클로 조립 정지점 도착 저장: '{item?.name}'", this);
            }
            else
            {
                Debug.LogWarning($"[ProcessController] 처리 대상으로 설정되지 않은 LineB 웨이포인트 도착을 무시합니다: {waypointIndex}", this);
            }
        }
        else if (line == ConveyorLineId.LineA)
        {
            if (waypointIndex == baseInspectionWaypointIndex)
            {
                waitingBaseAtInspection = item;
                Debug.Log($"[ProcessController] 베이스 검사 정지점 도착 저장: '{item?.name}'", this);
            }
            else if (waypointIndex == baseAssemblyWaypointIndex || IsLastStopWaypoint(ConveyorLineId.LineA, waypointIndex))
            {
                waitingBaseAtAssembly = item;
                Debug.Log($"[ProcessController] 베이스 조립 정지점 도착 저장: '{item?.name}'", this);
            }
            else
            {
                Debug.LogWarning(
                    $"[ProcessController] 처리 대상으로 설정되지 않은 LineA 웨이포인트 도착을 무시합니다: {waypointIndex}",
                    this);
            }
        }
    }

    // 푸셔 이송 완료는 RejectBox 파괴 완료가 아니므로 여기서는 다음 스폰을 허용하지 않습니다.
    private void OnBasePushedToReject(GameObject item)
    {
        Debug.Log(
            $"[ProcessController] 베이스 '{item?.name}' 푸셔 이송 완료. RejectBox 파괴 완료 이벤트를 기다립니다.",
            this);
    }

    /// <summary>
    /// [Line A 병렬 코루틴] 베이스가 비전 검사를 통과하고 조립 정지점에 도착할 때까지 독립 진행
    /// </summary>
    private IEnumerator PrepareBaseRoutine()
    {
        while (State == ProcessState.Running)
        {
            // 1) 베이스가 검사 정지점에 도착할 때까지 대기
            Debug.Log("[ProcessController] [Line A] 베이스 검사 정지점 도착 대기 중...", this);
            yield return new WaitUntil(() =>
                State == ProcessState.Running && waitingBaseAtInspection != null);

            GameObject inspectionBaseObject = waitingBaseAtInspection;
            waitingBaseAtInspection = null;
            Debug.Log($"[ProcessController] [Line A] 베이스 '{inspectionBaseObject.name}' 도착 감지. 카메라2 검사 진행.", this);

            var robotBase = inspectionBaseObject.GetComponentInChildren<BaseInfo>(true);
            if (robotBase == null)
            {
                Debug.LogError($"[ProcessController] 정지점의 베이스 '{inspectionBaseObject.name}'에 BaseInfo가 없어 검사를 중단하고 알람으로 전환합니다.", this);
                ChangeState(ProcessState.Alarm, "베이스 정보(BaseInfo)가 없어 공정을 멈췄습니다");
                conveyors.StopAll();
                yield break;
            }
            var baseItem = inspectionBaseObject.GetComponent<Item>();

            // 2) 부품2(베이스) 비전 검사
            if (baseItem != null)
                robotBase.defectType = baseItem.itemType == ItemType.Defective ? DefectType.PartLoss : DefectType.None;
            bool baseOk = camera2.InspectPart2(robotBase);

            // 검사 결과를 Item에 반영해야 푸셔(Item.itemType 기준)가 올바르게 판단함
            if (baseItem != null)
                baseItem.itemType = baseOk ? ItemType.Normal : ItemType.Defective;

            // 3) LineA 이송: PusherController가 OnItemArrivedAtStopPoint 이벤트에 따라
            //    - 양품이면 ReleaseItemAndRunLine(LineA, mover)를 자체 호출하여 조립 정지점으로 이송함
            //    - 불량이면 푸셔로 밀어내어 RejectBox로 배출함
            if (!baseOk)
            {
                Debug.LogWarning("[ProcessController] [Line A] 베이스 불량(PartLoss) 판정. 푸셔 배출 및 다음 베이스 도착 대기...", this);
                continue; // 불량 배출 후 다음 스폰된 베이스 대기 루프
            }

            // 4) 양품 베이스가 조립 정지점에 도착할 때까지 대기
            Debug.Log("[ProcessController] [Line A] 양품 베이스 조립 정지점 도착 대기 중...", this);
            yield return new WaitUntil(() =>
                State == ProcessState.Running && waitingBaseAtAssembly != null);

            assemblyBaseObject = waitingBaseAtAssembly;
            waitingBaseAtAssembly = null;
            Debug.Log($"[ProcessController] [Line A 완료] 베이스 '{assemblyBaseObject.name}' 조립 위치 도착 및 대기!", this);
            yield break; // 준비 완료!
        }
    }

    /// <summary>
    /// [Line B 병렬 코루틴] 클로가 비전 검사를 통과하고 조립 정지점에 도착할 때까지 독립 진행
    /// </summary>
    private IEnumerator PrepareClawRoutine()
    {
        while (State == ProcessState.Running)
        {
            // 1) 클로가 검사 정지점에 도착할 때까지 대기 후 비전 검사 (불량이면 폐기하고 다음 클로까지 대기)
            Debug.Log("[ProcessController] [Line B] 클로 검사 정지점 도착 대기 중...", this);
            yield return new WaitUntil(() =>
                State == ProcessState.Running && waitingPartAtInspection != null);

            Debug.Log("[ProcessController] [Line B] 클로 도착 감지. 클로 비전 검사 시작...", this);
            yield return InspectClawUntilPass();

            if (!clawInspectPassed || passedClawObject == null)
            {
                Debug.LogError("[ProcessController] 클로 검사 실패로 공정을 중단하고 알람으로 전환합니다.", this);
                ChangeState(ProcessState.Alarm, "클로 검사를 할 수 없어 공정을 멈췄습니다");
                conveyors.StopAll();
                yield break;
            }

            // 2) 검사 통과한 클로를 LineB 조립 정지점(마지막 정지점)까지 이송한 뒤 도착을 기다림
            var clawGo = passedClawObject;
            var partMover = clawGo.GetComponent<ConveyorItemMover>() ?? clawGo.GetComponentInChildren<ConveyorItemMover>(true);

            var lineBStopIndices = conveyors.GetStopWaypointIndices(ConveyorLineId.LineB);
            bool hasMultipleStops = lineBStopIndices != null && lineBStopIndices.Length > 1;

            if (hasMultipleStops)
            {
                Debug.Log("[ProcessController] [Line B] 검사 통과 클로 조립 정지점으로 이송 시작.", this);
                conveyors.ReleaseItemAndRunLine(ConveyorLineId.LineB, partMover);

                Debug.Log("[ProcessController] [Line B] 클로 조립 정지점 도착 대기 중...", this);
                yield return new WaitUntil(() =>
                    State == ProcessState.Running && waitingPartAtAssembly != null);

                assemblyClawObject = waitingPartAtAssembly;
                waitingPartAtAssembly = null;
                Debug.Log($"[ProcessController] [Line B 완료] 클로 '{assemblyClawObject.name}' 조립 위치 도착 및 대기!", this);
            }
            else
            {
                Debug.LogWarning("[ProcessController] [Line B] Line B 정지점이 1개뿐입니다. 검사 위치에서 조립 진행.", this);
                assemblyClawObject = clawGo;
            }

            yield break; // 준비 완료!
        }
    }

    private IEnumerator RunCycle()
    {
        while (true)
        {
            if (State != ProcessState.Running)
                yield return new WaitUntil(() => State == ProcessState.Running);

            Debug.Log("[ProcessController] 🚀 [병렬 공정 시작] 베이스(Line A)와 클로(Line B) 동시 준비/이송 시작!", this);

            assemblyBaseObject = null;
            assemblyClawObject = null;

            baseRoutine = StartCoroutine(PrepareBaseRoutine());
            clawRoutine = StartCoroutine(PrepareClawRoutine());

            // 두 부품이 모두 각자의 조립 정지점에 도착할 때까지 병렬 대기
            yield return baseRoutine;
            yield return clawRoutine;

            if (State != ProcessState.Running || assemblyBaseObject == null || assemblyClawObject == null)
            {
                Debug.LogWarning("[ProcessController] 부품 준비 중 공정 중단 또는 알람 발생으로 이번 사이클을 재검토합니다.", this);
                continue;
            }

            Debug.Log($"[ProcessController] 🎯 [조립 준비 완료] 베이스('{assemblyBaseObject.name}')와 클로('{assemblyClawObject.name}')가 모두 조립 지점에 도착했습니다. 조립 시퀀스를 시작합니다!", this);

            var clawGo = assemblyClawObject;
            var part = clawGo.GetComponent<PartInfo>() ?? clawGo.GetComponentInChildren<PartInfo>(true);
            var partMover = clawGo.GetComponent<ConveyorItemMover>() ?? clawGo.GetComponentInChildren<ConveyorItemMover>(true);

            var assemblyBase = assemblyBaseObject.GetComponentInChildren<BaseInfo>(true);
            if (assemblyBase == null)
                Debug.LogError($"[ProcessController] 조립 정지점의 베이스 '{assemblyBaseObject.name}'에 BaseInfo가 없습니다.", this);
            var assemblyBaseMover = assemblyBaseObject.GetComponent<ConveyorItemMover>();

            // 6) 조립 (ProcessController가 조립 완료를 직접 기다림)
            LogPickPositions("조립", clawGo);
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

            // 7) 결과 확인
            bool isPartLoss = (assemblyBase != null && assemblyBase.defectType == DefectType.PartLoss);

            if (!assembly.LastAssemblyOk)
            {
                if (isPartLoss)
                {
                    Debug.LogWarning("[ProcessController] 조립 재시도 초과로 결합 실패(클로 폐기). 차체(PartLoss)를 Line C 불량 배출 라인으로 이송합니다.", this);
                }
                else
                {
                    Debug.LogError("[ProcessController] 로봇팔 조립 실패(참조 누락 또는 시스템 오류)로 공정을 중단하고 알람으로 전환합니다.", this);
                    ChangeState(ProcessState.Alarm, "조립 오류로 공정을 멈췄습니다");
                    conveyors.StopAll();
                    continue;   // Alarm 상태라 WaitUntil에서 멈춤 → Reset 전까지 대기
                }
            }

            // 카메라3 완제품 비전 검사 판정 확인 (Camera3Inspector)
            if (camera3 == null && assembly != null)
                camera3 = assembly.camera3Inspector;

            bool isProductPass = true;
            if (isPartLoss)
            {
                // 재시도 실패로 미결합된 차체는 무조건 불량 처리
                isProductPass = false;
            }
            else if (camera3 != null && camera3.LastResult.HasValue)
            {
                isProductPass = (camera3.LastResult.Value == InspectResult.Pass);
            }

            // 차체 Item 컴포넌트 불량 상태 동기화
            var baseItemOnAssembly = assemblyBaseObject.GetComponent<Item>();
            if (baseItemOnAssembly != null && !isProductPass)
            {
                baseItemOnAssembly.itemType = ItemType.Defective;
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
                string failReason = isPartLoss ? "조립 재시도 실패(PartLoss)" : "완제품 결합 각도 불량(AngleError)";
                Debug.LogWarning($"[ProcessController] ⭐ [조립 성공률] {successCount}/{totalAssembledCount}회 ({successRate:F1}%) | {failReason} 감지. Line C 푸셔 배출을 위해 무조건 이송을 진행합니다.", this);
            }

            // 8) 라인 가동 및 완제품 Line C 이송 (양품/불량 불문 무조건 이송)
            // 8-1) Line B(클로 공급 라인) 스토퍼 해제 및 라인 재가동
            conveyors.ReleaseItemAndRunLine(ConveyorLineId.Shipping, partMover);

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
    private void LogPickPositions(string tag, GameObject currentClaw = null)
    {
        Vector3 claw = currentClaw != null ? currentClaw.transform.position : (assemblyClawObject != null ? assemblyClawObject.transform.position : Vector3.zero);
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
        passedClawObject = null;

        while (true)
        {
            if (station1 == null || waitingPartAtInspection == null)
            {
                Debug.LogError("[ProcessController] 클로 검사를 할 수 없습니다 (station1 또는 waitingPartAtInspection이 없음).", this);
                yield break;
            }

            GameObject targetClaw = waitingPartAtInspection;
            waitingPartAtInspection = null;

            var clawItem = targetClaw.GetComponent<Item>()
                           ?? targetClaw.GetComponentInChildren<Item>(true)
                           ?? targetClaw.GetComponentInParent<Item>();
            var clawInfo = targetClaw.GetComponent<PartInfo>()
                           ?? targetClaw.GetComponentInChildren<PartInfo>(true)
                           ?? targetClaw.GetComponentInParent<PartInfo>();

            // 스포너 프리팹(claw.prefab 등)에 Item 또는 PartInfo가 누락되었을 때 자동 보완
            if (clawItem == null)
            {
                clawItem = targetClaw.AddComponent<Item>();
                clawItem.itemType = ItemType.Normal;
            }

            if (clawInfo == null)
            {
                clawInfo = targetClaw.AddComponent<PartInfo>();
            }

            LogPickPositions("검사", targetClaw);
            // 스포너가 정한 Item.itemType을 PartInfo.defectType에 반영 (develop의 InspectStation은 PartInfo 기준으로 판정)
            clawInfo.defectType = clawItem.itemType == ItemType.Defective ? DefectType.PartLoss : DefectType.None;
            yield return station1.InspectSequence(targetClaw.transform, clawInfo);

            if (station1.LastResult != InspectResult.Fail)
            {
                clawInspectPassed = true;
                passedClawObject = targetClaw;
                yield break;
            }

            // 불량 클로: 폐기 완료 → LineB 재가동(정지 상태면 스포너가 다음 클로를 생성하지 않음) 후 다음 클로 생성 → 도착을 기다림
            conveyors.ReleaseItemAndRunLine(ConveyorLineId.LineB, null);
            if (clawSpawner != null) clawSpawner.NotifyItemCleared();
            yield return new WaitUntil(() =>
                State == ProcessState.Running && waitingPartAtInspection != null);
        }
    }

    // 상태를 바꾸고 대시보드에 알린다
    private void ChangeState(ProcessState next, string reason = null)
    {
        if (State == next) return;
        State = next;
        InspectEventBus.PublishProcessState(next, reason);
    }
}
