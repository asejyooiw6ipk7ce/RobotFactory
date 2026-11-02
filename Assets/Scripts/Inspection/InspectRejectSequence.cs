using System.Collections;
using UnityEngine;

public partial class InspectRejectSequence : MonoBehaviour
{
    [Header("로봇 및 그리퍼 연결")]
    public ArmIK ik;
    public GripperController gripper;

    [Header("목표 위치 트랜스폼")]
    public Transform pickPoint;   // 불량 부품이 있는 위치
    public Transform rejectPose;  // 폐기 박스 위 놓는 위치

    [Header("이동 및 높이 설정")]
    public float liftHeight = 0.15f;               // 집기/놓기 전후 수직 상승 높이 (m)
    public float moveSpeed = 0.25f;                // 직선 이동 속도 (m/s)
    public float gripWidthMm = 48f;                // 그리퍼 목표 파지 폭 (mm)
    public float defaultPickYaw = 90f;             // 수직 하강 기본 손목 각도 (deg)
    public float safeTransitHeightOffset = 0.65f;  // 로봇 베이스 기준 안전 상공 통과 고도 오프셋 (m)

    [Header("폐기 결과 및 상태 모니터")]
    public int maxGraspTries = 3;                  // 파지 최대 재시도 횟수
    public bool LastRejectOK { get; private set; } // true: 폐기 성공, false: 잡기 실패 등으로 폐기 실패
    public bool IsBusy { get; private set; }       // 시퀀스 실행 중 여부
    public string Status { get; private set; } = "대기";

    private void Awake()
    {
        AutoFindReferences();
    }

    private void Start()
    {
        AutoFindReferences();
        ApplyGripperCloseTarget();
    }

    private void SetState(string status)
    {
        Status = status;
        Debug.Log($"[InspectRejectSequence] {Status}");
    }

    /// <summary>
    /// 로봇 및 그리퍼 컴포넌트 자동 탐색 및 연결
    /// </summary>
    public void AutoFindReferences()
    {
        if (ik == null)
        {
            ik = GetComponent<ArmIK>();
            if (ik == null) ik = GetComponentInParent<ArmIK>();
            if (ik == null) ik = FindAnyObjectByType<ArmIK>();
        }

        if (gripper == null)
        {
            gripper = GetComponentInChildren<GripperController>();
            if (gripper == null && ik != null)
            {
                gripper = ik.GetComponentInChildren<GripperController>();
            }
            if (gripper == null) gripper = FindAnyObjectByType<GripperController>();
        }
    }

    /// <summary>
    /// 그리퍼 닫힘 한계값을 gripWidthMm에 맞춰 설정하여 관통 방지 및 감지 안정화
    /// </summary>
    public void ApplyGripperCloseTarget()
    {
        if (gripper == null) AutoFindReferences();
        if (gripper != null)
        {
            float targetM = Mathf.Max(0f, (48f - gripWidthMm) * 0.5f * 0.001f);
            gripper.leftClose = targetM;
            gripper.rightClose = targetM;
            gripper.useGraspCheck = false;
        }
    }

    /// <summary>
    /// 불량 부품을 수직으로 파지하여 폐기함으로 배출하는 코루틴
    /// </summary>
    public IEnumerator Reject()
    {
        if (IsBusy) yield break;
        IsBusy = true;
        LastRejectOK = false;
        SetState("폐기 시퀀스 시작");

        AutoFindReferences();
        ApplyGripperCloseTarget();

        if (ik == null)
        {
            Debug.LogError("[InspectRejectSequence] ArmIK가 연결되지 않아 시퀀스를 중단합니다.");
            SetState("오류: ArmIK 누락");
            IsBusy = false;
            yield break;
        }

        // ArmIK 초기화 및 부호 캘리브레이션 대기
        float waitTimer = 0f;
        while (!ik.Ready && waitTimer < 3f)
        {
            waitTimer += Time.deltaTime;
            yield return null;
        }

        // 1. 초기 그리퍼 개방
        SetState("1. 그리퍼 개방");
        if (gripper != null)
        {
            yield return gripper.Open();
        }

        if (pickPoint == null || rejectPose == null)
        {
            Debug.LogError("[InspectRejectSequence] pickPoint 또는 rejectPose가 지정되지 않았습니다.");
            SetState("오류: 목표 좌표 누락");
            IsBusy = false;
            yield break;
        }

        Vector3 pick = pickPoint.position;
        Vector3 pickAbove = pick + Vector3.up * liftHeight;
        Vector3 reject = rejectPose.position;
        Vector3 rejectAbove = reject + Vector3.up * liftHeight;
        float targetYaw = defaultPickYaw;

        // 수직 하향 자세 고정
        ik.pointDown = true;
        ik.useTargetLookDir = false;

        // 2. 상공 안전 고도(Elbow-Up) 경유 후 픽업 상공 접근
        SetState("2. 상공 경유 수직 픽업 대기 위치 접근");
        float safeTransitY = Mathf.Max(pickAbove.y + 0.15f, ik.transform.position.y + safeTransitHeightOffset);
        Vector3 highSafeTransit = new Vector3(pickAbove.x, safeTransitY, pickAbove.z);
        yield return ik.MoveTo(highSafeTransit, null);
        yield return ik.MoveLinearSmooth(pickAbove, moveSpeed, targetYaw, false);

        bool grasped = false;

        // 3. 수직 하강 및 파지 루프
        for (int attempt = 1; attempt <= maxGraspTries; attempt++)
        {
            SetState($"3. 수직 하강 및 그리퍼 파지 (시도 {attempt}/{maxGraspTries})");
            ik.pointDown = true;
            ik.useTargetLookDir = false;
            yield return ik.MoveLinearSmooth(pick, moveSpeed, targetYaw, false);

            if (gripper != null)
            {
                yield return gripper.Close();

                if (gripper.LastGraspOK)
                {
                    grasped = true;
                    break;
                }

                Debug.LogWarning($"[InspectRejectSequence] 파지 실패 ({attempt}/{maxGraspTries}) - 재시도");
                yield return gripper.Open();
                yield return ik.MoveLinearSmooth(pickAbove, moveSpeed, targetYaw, false);
            }
            else
            {
                grasped = true;
                break;
            }
        }

        // 최대 시도 횟수 초과 시
        if (!grasped)
        {
            Debug.LogError("[InspectRejectSequence] 파지 최대 재시도 초과 - 폐기 실패");
            SetState("오류: 파지 실패");
            yield return ik.MoveLinearSmooth(pickAbove, moveSpeed, targetYaw, false);
            yield return ik.ReturnToHomeJoints();
            IsBusy = false;
            yield break;
        }

        // 4. 수직 리프팅 (Lift)
        SetState("4. 수직 리프팅");
        ik.pointDown = true;
        ik.useTargetLookDir = false;
        yield return ik.MoveLinearSmooth(pickAbove, moveSpeed, targetYaw);

        // 5. 폐기함 상공 이동
        SetState("5. 폐기함 상공 이동");
        yield return ik.MoveTo(rejectAbove);

        // 6. 폐기함 하강
        SetState("6. 폐기함 하강");
        yield return ik.MoveLinear(reject, moveSpeed);

        // 7. 그리퍼 개방 (부품 놓기)
        SetState("7. 부품 놓기");
        if (gripper != null)
        {
            yield return gripper.Open();
        }

        // 8. 폐기함 상공 복귀 (Commit 1: 기존 pickAbove 오타 수정하여 rejectAbove로 정상 탈출)
        SetState("8. 폐기함 상공 탈출");
        yield return ik.MoveLinear(rejectAbove, moveSpeed);

        // 9. 폐기 카운트 집계
        ProcessCounters.Add(CounterType.Discarded);

        // 10. 홈 대기 자세로 복귀
        SetState("10. 대기 자세 복귀");
        yield return ik.ReturnToHomeJoints();

        LastRejectOK = true;
        SetState("폐기 완료");
        IsBusy = false;
    }
}
