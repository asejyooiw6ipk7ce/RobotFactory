using System.Collections;
using UnityEngine;

public enum ProcessState { Stopped, Running, Paused, Alarm }

public class ProcessController : MonoBehaviour
{
    [Header("연결")]
    [SerializeField] private ConveyorManager conveyors;     // LineA(베이스), LineB(클로)
    [SerializeField] private Camera2Inspector camera2;      // 베이스 검사 (WP2)
    [SerializeField] private InspectStation station1;       // 클로 검사 + 불량 폐기 (WP3)
    [SerializeField] private AssemblySequence assembly;     // 조립·완제품 검사 (WP1)

    public ProcessState State { get; private set; } = ProcessState.Stopped;
    public float SimTime { get; private set; }

    // 정지점에 도착해 대기 중인 아이템 (ConveyorManager 도착 이벤트로 설정됨)
    private GameObject waitingPart;   // LineB (클로)
    private GameObject waitingBase;   // LineA (베이스)

    // 검사를 통과해 클로를 기다리는 베이스 (검사 정지점 WP2에서 대기)
    private ConveyorItemMover heldBaseMover;
    private BaseInfo heldBaseInfo;

    private Coroutine cycle;

    private void OnEnable()
    {
        if (conveyors != null) conveyors.OnItemArrivedAtStopPoint += OnArrived;
    }

    private void OnDisable()
    {
        if (conveyors != null) conveyors.OnItemArrivedAtStopPoint -= OnArrived;
        Time.timeScale = 1f;   // 일시정지 상태로 꺼져도 시간이 멈춘 채 남지 않도록
    }

    private void Update()
    {
        if (State == ProcessState.Running) SimTime += Time.deltaTime;
    }

    public void Run()
    {
        if (State == ProcessState.Alarm) return;   // 알람은 Reset 후에만 재시작
        State = ProcessState.Running;
        Time.timeScale = 1f;                       // 일시정지에서 풀면 시간 재개
        conveyors.StartAll();
        if (cycle == null) cycle = StartCoroutine(RunCycle());
    }

    public void Pause()
    {
        if (State != ProcessState.Running) return;
        State = ProcessState.Paused;
        Time.timeScale = 0f;       // 로봇 이동·검사 대기·조립까지 모두 함께 멈춤
        conveyors.PauseAll();
    }

    public void Reset()
    {
        if (cycle != null) StopCoroutine(cycle);
        cycle = null;
        waitingPart = null;
        waitingBase = null;
        heldBaseMover = null;
        heldBaseInfo = null;
        Time.timeScale = 1f;       // 로봇 복귀 동작이 돌아야 하므로 시간 재개

        conveyors.ResumeAll();     // 일시정지 플래그 해제
        conveyors.StopAll();

        // 라인 위의 아이템 제거 및 스포너 재시작
        foreach (var spawner in FindObjectsByType<ItemSpawner>(FindObjectsSortMode.None))
            spawner.ClearSpawned();

        // 검사 스테이션(벨트·스토퍼·로봇1) 대기 상태로 복귀
        if (station1 != null) station1.ResetState();

        // 조립 로봇(로봇2) 대기 상태로 복귀
        assembly.ResetToInitialState();
        SimTime = 0f;
        State = ProcessState.Stopped;
    }

    // ConveyorManager가 정지점 도착을 알려줌
    private void OnArrived(ConveyorLineId line, int waypointIndex, GameObject item)
    {
        if (line == ConveyorLineId.LineB) waitingPart = item;
        else if (line == ConveyorLineId.LineA) waitingBase = item;
    }

    private IEnumerator RunCycle()
    {
        while (true)
        {
            // 1) 베이스 검사 (검사 대기 중인 베이스가 없을 때만 새로 검사)
            if (heldBaseMover == null)
            {
                yield return new WaitUntil(() =>
                    State == ProcessState.Running && waitingBase != null);

                var baseInfo = waitingBase.GetComponentInChildren<BaseInfo>(true);
                var baseMover = waitingBase.GetComponent<ConveyorItemMover>();
                waitingBase = null;

                if (baseInfo == null)
                    Debug.LogError("[ProcessController] 검사 정지점의 베이스에 BaseInfo가 없습니다.", this);

                bool baseOk = camera2.InspectPart2(baseInfo);
                if (!baseOk)
                {
                    // 불량 베이스 처리 방식은 미정 → 우선 흘려보냄
                    conveyors.ReleaseItemAndRunLine(ConveyorLineId.LineA, baseMover);
                    continue;
                }

                // 통과한 베이스는 클로를 기다리며 검사 정지점에 머문다
                heldBaseMover = baseMover;
                heldBaseInfo = baseInfo;
            }

            // 2) 클로 검사 (InspectStation, WP3). 불량이면 로봇1이 폐기함에 넣는다
            yield return new WaitUntil(() =>
                State == ProcessState.Running && waitingPart != null);

            var part = waitingPart.GetComponent<PartInfo>();
            var partMover = waitingPart.GetComponent<ConveyorItemMover>();
            var partItem = waitingPart.GetComponent<Item>();   // 클로의 판정 값(itemType)을 검사에 넘긴다
            waitingPart = null;

            yield return station1.InspectSequence(partItem);

            if (station1.LastResult == InspectResult.Fail)
            {
                // 폐기된 클로는 더 이상 경로를 따라가지 않도록 이동 스크립트 제거
                // 베이스는 검사 정지점에서 계속 대기 (다음 클로를 기다림)
                if (partMover != null) Destroy(partMover);
                conveyors.RunLine(ConveyorLineId.LineB);
                continue;
            }

            // 3) 양품 → 클로(LineB)와 베이스(LineA)를 조립 정지점(WP1)으로 이송
            var baseToAssemble = heldBaseMover;
            heldBaseMover = null;
            heldBaseInfo = null;

            conveyors.ReleaseItemAndRunLine(ConveyorLineId.LineB, partMover);
            conveyors.ReleaseItemAndRunLine(ConveyorLineId.LineA, baseToAssemble);

            // 4) 둘 다 조립 정지점에 도착할 때까지 대기
            yield return new WaitUntil(() =>
                State == ProcessState.Running && waitingPart != null && waitingBase != null);

            var assemblyPart = waitingPart.GetComponent<PartInfo>();
            var assemblyPartMover = waitingPart.GetComponent<ConveyorItemMover>();
            var assemblyBase = waitingBase.GetComponentInChildren<BaseInfo>(true);
            var assemblyBaseMover = waitingBase.GetComponent<ConveyorItemMover>();
            waitingPart = null;
            waitingBase = null;

            // 5) 조립 (AssemblySequence의 자동 시작은 꺼 둔 상태여야 함)
            assembly.NotifyPart1Ready(assemblyPart);
            assembly.NotifyPart2Ready(assemblyBase);
            yield return assembly.Assemble(-1f);

            // 6) 결과 확인
            if (!assembly.LastAssemblyOk)
            {
                State = ProcessState.Alarm;
                conveyors.StopAll();
                continue;   // Alarm 상태라 WaitUntil에서 멈춤 → Reset 전까지 대기
            }

            // 7) 조립 후 클로는 완제품의 일부 → 이동 스크립트 제거, 두 라인 재가동
            if (assemblyPartMover != null) Destroy(assemblyPartMover);
            conveyors.RunLine(ConveyorLineId.LineB);
            conveyors.ReleaseItemAndRunLine(ConveyorLineId.LineA, assemblyBaseMover);
        }
    }
}
