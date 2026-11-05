using System.Collections;
using UnityEngine;

// 부품 도착(OnTriggerEnter 감지)
// -> 벨트 정지(ConveyorController.StopConveyor)
// -> 스토퍼 올리기(PartStopper.Stop)
// -> 검사 자세 이동(ArmIK.MoveTo - 선충돌 방지 상공 대기)
// -> 검사 (PartInfo 기반 불량 자동 판정 또는 임시 설정값)
// -> 불량 시: InspectRejectSequence.Reject() 폐기
// -> 이벤트 발행(InspectEventBus.Publish)
// -> 벨트 재개(ConveyorController.ResumeConveyor) 및 스토퍼 내리기(PartStopper.Resume)

public class InspectStation : MonoBehaviour
{
    [Header("컨베이어 및 로봇 제어 연결")]
    public ConveyorController belt;
    public PartStopper stopper;
    public ArmIK ik;

    [Header("검사 자세 좌표 및 높이")]
    public Transform inspectPose;
    public float inspectHeightOffset = 0.15f; // inspectPose 기준 상공 안전 대기 높이 (부품 충돌 방지)

    private bool isInspecting = false;

    [Tooltip("true면 트리거 진입 시 자동으로 검사를 시작합니다. ProcessController가 직접 호출하는 경우 false로 둡니다.")]
    public bool autoStartOnTrigger = false;

    // 마지막 검사 결과 (ProcessController가 폐기 여부 확인용으로 사용)
    public InspectResult LastResult { get; private set; }
    public int robotId = 1;

    [Header("상태 대시보드 반영")]
    public StationId stationId = StationId.Station1;

    [Tooltip("임시 판정 결과 (부품에 PartInfo가 없을 때 대체 사용)")]
    public InspectResult testResult = InspectResult.Pass;
    public DefectType testDefect = DefectType.AngleError;
    public bool autoDetectPartDefect = true; // 부품의 PartInfo.defectType 자동 판정 여부

    private int serial;

    [Header("폐기 시퀀스 연결")]
    public InspectRejectSequence reject;

    [Header("에러 및 중단 상태")]
    private bool failed = false; // 폐기 실패 등으로 멈춘 상태

    [ContextMenu("Test Inspect")]
    void TestInspect()
    {
        if (isInspecting) return;
        StartCoroutine(InspectSequence(null, null));
    }

    [ContextMenu("Clear Failure")]
    public void ClearFailure()
    {
        if (!failed) return;
        failed = false;
        isInspecting = false;
        if (belt != null) belt.ResumeConveyor();
        if (stopper != null) StartCoroutine(stopper.Resume());
        RobotStatusTracker.Set(robotId, RobotState.Idle);
    }

    // 부품 검사지점 도착 감지
    public void OnTriggerEnter(Collider other)
    {
        if (!autoStartOnTrigger || isInspecting || failed) return;

        // 도착한 부품 감지 (로봇 본체 제외)
        if (ik != null && other.transform.root == ik.transform.root) return;
        if (other.transform.root == transform.root) return;

        Transform arrivedPart = (other.attachedRigidbody != null)
            ? other.attachedRigidbody.transform
            : (other.transform.parent != null ? other.transform.parent : other.transform);

        PartInfo info = arrivedPart.GetComponentInChildren<PartInfo>();
        if (info == null && other.attachedRigidbody != null)
        {
            info = other.attachedRigidbody.GetComponent<PartInfo>();
        }

        StartCoroutine(InspectSequence(arrivedPart, info));
    }

    public IEnumerator InspectSequence(Transform arrivedPart, PartInfo info)
    {
        isInspecting = true;

        // 1. 벨트 정지
        if (belt != null) belt.StopConveyor();

        // 2. 스토퍼 올리기
        if (stopper != null)
        {
            yield return stopper.Stop();
        }

        // 3. 검사 자세 이동 (부품 선충돌 방지를 위해 inspectPose 상공에서 수직 하향 대기)
        RobotStatusTracker.Set(robotId, RobotState.Moving);

        if (ik != null && inspectPose != null)
        {
            Vector3 safeInspectPos = inspectPose.position + Vector3.up * inspectHeightOffset;
            float dist = (safeInspectPos - ik.transform.position).magnitude;
            if (dist < 1.5f)
            {
                ik.pointDown = true;
                ik.useTargetLookDir = false;
                yield return ik.MoveTo(safeInspectPos, null);
            }
            else
            {
                Debug.LogWarning($"[InspectStation] inspectPose({inspectPose.position})가 로봇 작업반경(거리: {dist:F1}m)을 벗어나 제자리 비전 검사를 진행합니다.", this);
            }
        }

        // 4. 검사 진행
        RobotStatusTracker.Set(robotId, RobotState.Working);
        yield return new WaitForSeconds(1.5f); // 검사 대기

        // 불량 여부 판정 (도착한 부품의 PartInfo 우선 반영)
        bool isFail = false;
        DefectType defect = DefectType.None;

        if (autoDetectPartDefect && info != null)
        {
            if (info.defectType != DefectType.None)
            {
                isFail = true;
                defect = info.defectType;
            }
        }
        else
        {
            isFail = (testResult == InspectResult.Fail);
            defect = isFail ? testDefect : DefectType.None;
        }

        InspectResult finalResult = isFail ? InspectResult.Fail : InspectResult.Pass;
        LastResult = finalResult;

        // 5. 검사 결과가 Fail이면 폐기 시퀀스 실행
        if (finalResult == InspectResult.Fail && reject != null)
        {
            if (arrivedPart != null)
            {
                reject.SetTargetPart(arrivedPart);
            }

            yield return reject.Reject();

            if (!reject.LastRejectOK)
            {
                Debug.LogError("[InspectStation] 폐기 실패 - 부품이 벨트 위에 남아 있음. 관리자 조치가 필요합니다.");
                failed = true;
                RobotStatusTracker.Set(robotId, RobotState.Error);
                yield break; // 벨트 재개하지 않고 정지 상태 유지
            }
        }

        // 6. 검사 완료 이벤트 발행
        string partId = (info != null && !string.IsNullOrEmpty(info.id)) ? info.id : $"P{++serial:0000}";
        InspectEventBus.Publish(new InspectEvent(partId, stationId, finalResult, defect, Time.time));

        // 7. 벨트 재개 및 스토퍼 내리기
        RobotStatusTracker.Set(robotId, RobotState.Idle);
        if (belt != null) belt.ResumeConveyor();
        if (stopper != null) yield return stopper.Resume();

        isInspecting = false;
    }
}
