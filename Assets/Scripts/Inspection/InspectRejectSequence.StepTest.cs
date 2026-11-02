using System.Collections;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public partial class InspectRejectSequence
{
    #region 인스펙터 단계별 검증 및 초기화 (Step-by-Step Test)

    [Header("시각화 기즈모 설정")]
    public bool showReachabilityGizmos = true;

    // 부품 초기 상태 기록 변수
    private bool initialPartRecorded = false;
    private Vector3 initialPartPosition;
    private Quaternion initialPartRotation;
    private Transform initialPartParent;
    private bool initialPartIsKinematic;
    private bool initialPartUseGravity;

    /// <summary>
    /// 대상 부품의 초기 위치, 회전 및 물리 상태 기록
    /// </summary>
    public void RecordInitialPartState()
    {
        Transform part = DetectOrGetTargetPart();
        if (part == null || initialPartRecorded) return;

        initialPartPosition = part.position;
        initialPartRotation = part.rotation;
        initialPartParent = part.parent;

        var rb = part.GetComponent<Rigidbody>();
        if (rb != null)
        {
            initialPartIsKinematic = rb.isKinematic;
            initialPartUseGravity = rb.useGravity;
        }

        initialPartRecorded = true;
    }

    /// <summary>
    /// 부품을 초기 컨베이어/픽업 위치로 즉시 복원
    /// </summary>
    public void ResetPartToInitialState()
    {
        Transform part = targetPart;
        if (part == null) part = DetectOrGetTargetPart();

        if (part != null && initialPartRecorded)
        {
            part.SetParent(initialPartParent, true);
            part.position = initialPartPosition;
            part.rotation = initialPartRotation;

            var rb = part.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = initialPartIsKinematic;
                rb.useGravity = initialPartUseGravity;
            }

            var mover = part.GetComponentInParent<ConveyorItemMover>();
            if (mover == null) mover = part.GetComponentInChildren<ConveyorItemMover>();
            if (mover != null) mover.enabled = true;
        }
    }

    /// <summary>
    /// 로봇과 부품을 최초 시작 상태로 완전 초기화
    /// </summary>
    public void ResetToInitialState()
    {
        StopAllCoroutines();
        IsBusy = false;
        StartCoroutine(ResetToInitialStateRoutine());
    }

    private IEnumerator ResetToInitialStateRoutine()
    {
        IsBusy = true;
        SetState("초기화 진행 중...");

        // 1. 부품 초기 상태 복원
        ResetPartToInitialState();

        // 2. 그리퍼 열기
        if (gripper != null)
        {
            yield return gripper.Open();
        }

        // 3. 로봇 홈 자세 복귀
        if (ik != null)
        {
            ik.pointDown = true;
            ik.useTargetLookDir = false;
            yield return ik.ReturnToHomeJoints(2.0f);
        }

        IsBusy = false;
        LastRejectOK = false;
        SetState("초기화 완료 (대기)");
    }

    /// <summary>
    /// 인스펙터 원클릭 전체 폐기 실행 래퍼
    /// </summary>
    public void RunRejectTest()
    {
        if (IsBusy) return;
        RecordInitialPartState();
        StartCoroutine(Reject());
    }

    // =========================================================================
    // [1단계] 픽업 상공 접근 (Approach)
    // =========================================================================
    public void RunStep1_Approach()
    {
        if (IsBusy) return;
        StartCoroutine(Step1Routine());
    }

    private IEnumerator Step1Routine()
    {
        IsBusy = true;
        SetState("1단계: 픽업 상공 안전 접근");
        AutoFindReferences();
        ApplyGripperCloseTarget();
        RecordInitialPartState();

        if (gripper != null) yield return gripper.Open();

        DetectOrGetTargetPart();
        Vector3 pickPos = GetPickPosition();
        Vector3 pickApproach = pickPos + Vector3.up * liftHeight;
        float targetYaw = GetEffectiveYaw();

        if (ik != null)
        {
            // IK 초기화 대기
            float waitTimer = 0f;
            while (!ik.Ready && waitTimer < 3f)
            {
                waitTimer += Time.deltaTime;
                yield return null;
            }

            ik.pointDown = true;
            ik.useTargetLookDir = false;

            float safeTransitY = Mathf.Max(pickApproach.y + 0.15f, ik.transform.position.y + safeTransitHeightOffset);
            Vector3 highSafeTransit = new Vector3(pickApproach.x, safeTransitY, pickApproach.z);
            yield return ik.MoveTo(highSafeTransit, null);
            yield return ik.MoveLinearSmooth(pickApproach, moveSpeed, targetYaw, false);
        }

        SetState("1단계 완료: 픽업 상공 접근 성공");
        IsBusy = false;
    }

    // =========================================================================
    // [2단계] 수직 하강 및 그리퍼 파지 (Grasp)
    // =========================================================================
    public void RunStep2_Grasp()
    {
        if (IsBusy) return;
        StartCoroutine(Step2Routine());
    }

    private IEnumerator Step2Routine()
    {
        IsBusy = true;
        SetState("2단계: 수직 하강 및 그리퍼 파지");
        AutoFindReferences();
        ApplyGripperCloseTarget();
        RecordInitialPartState();

        Vector3 pickPos = GetPickPosition();
        float targetYaw = GetEffectiveYaw();

        if (gripper != null) yield return gripper.Open();

        if (ik != null)
        {
            ik.pointDown = true;
            ik.useTargetLookDir = false;
            yield return ik.MoveLinearSmooth(pickPos, moveSpeed, targetYaw, false);
        }

        if (gripper != null) yield return gripper.Close();
        AttachPartToGripper();
        yield return new WaitForSeconds(0.2f);

        SetState("2단계 완료: 파지 및 결합 완료");
        IsBusy = false;
    }

    // =========================================================================
    // [3단계] 수직 상공 들어올리기 (Lift)
    // =========================================================================
    public void RunStep3_Lift()
    {
        if (IsBusy) return;
        StartCoroutine(Step3Routine());
    }

    private IEnumerator Step3Routine()
    {
        IsBusy = true;
        SetState("3단계: 수직 상공 들어올리기 (Lift)");
        AttachPartToGripper();

        Vector3 pickPos = GetPickPosition();
        Vector3 pickApproach = pickPos + Vector3.up * liftHeight;
        float targetYaw = GetEffectiveYaw();

        if (ik != null)
        {
            ik.pointDown = true;
            ik.useTargetLookDir = false;
            yield return ik.MoveLinearSmooth(pickApproach, moveSpeed, targetYaw);
        }

        yield return new WaitForSeconds(0.15f);
        SetState("3단계 완료: 수직 리프팅 완료");
        IsBusy = false;
    }

    // =========================================================================
    // [4단계] 폐기함 이동, 중력 투하 및 홈 복귀 (Drop & Return)
    // =========================================================================
    public void RunStep4_DropAndReturn()
    {
        if (IsBusy) return;
        StartCoroutine(Step4Routine());
    }

    private IEnumerator Step4Routine()
    {
        IsBusy = true;
        SetState("4단계: 폐기함 선회 이동 및 중력 투하");
        AutoFindReferences();

        Vector3 dropPos = GetRejectBinDropPosition();

        if (ik != null)
        {
            ik.pointDown = true;
            ik.useTargetLookDir = false;
            yield return ik.MoveTo(dropPos, null);
            yield return new WaitForSeconds(0.15f);
        }

        if (gripper != null) yield return gripper.Open();
        ReleasePartWithGravity();
        yield return new WaitForSeconds(0.5f);

        ProcessCounters.Add(CounterType.Discarded);

        if (ik != null)
        {
            SetState("4단계: 안전 홈 복귀 중...");
            ik.pointDown = true;
            ik.useTargetLookDir = false;
            yield return ik.ReturnToHomeJoints();
        }

        LastRejectOK = true;
        SetState("4단계 완료: 폐기 및 홈 복귀 완료");
        IsBusy = false;
    }

    // =========================================================================
    // 기즈모 시각화 (작업 반경 및 배치 가이드선)
    // =========================================================================
    private void OnDrawGizmosSelected()
    {
        Vector3 pickPos = GetPickPosition();
        Vector3 pickApproach = pickPos + Vector3.up * liftHeight;
        Vector3 dropPos = GetRejectBinDropPosition();

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(pickPos, 0.035f);
        Gizmos.DrawLine(pickPos, pickApproach);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(pickApproach, 0.035f);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(dropPos, 0.05f);

        if (showReachabilityGizmos && ik != null)
        {
#if UNITY_EDITOR
            if (ik.showWorkRadius)
            {
                ik.DrawWorkEnvelopeGizmo();
            }

            Vector3 center = ik.GetWorkCenter();
            if (pickPos != Vector3.zero) DrawReachGuideLine(center, pickPos, "불량 부품 (Pick)");
            if (dropPos != Vector3.zero) DrawReachGuideLine(center, dropPos, "폐기함 (RejectBin)");
#endif
        }
    }

#if UNITY_EDITOR
    private void DrawReachGuideLine(Vector3 center, Vector3 targetPos, string label)
    {
        Vector3 floorTarget = new Vector3(targetPos.x, center.y, targetPos.z);
        int reachCode = ik.EvaluateReach(targetPos, out float hDist, out _);

        Color col;
        string status;
        switch (reachCode)
        {
            case 0:
                col = new Color(0.15f, 0.90f, 0.40f, 0.85f);
                status = "OK";
                break;
            case 1:
                col = new Color(1.0f, 0.85f, 0.20f, 0.85f);
                status = "한계 근접";
                break;
            case -1:
                col = new Color(1.0f, 0.50f, 0.15f, 0.85f);
                status = "사각지대";
                break;
            default:
                col = new Color(1.0f, 0.20f, 0.20f, 0.95f);
                status = "도달 불가!";
                break;
        }

        Handles.color = col;
        Handles.DrawDottedLine(center, floorTarget, 2.5f);
        Handles.DrawLine(floorTarget, targetPos);
        Handles.DrawWireDisc(floorTarget, Vector3.up, 0.035f);

        GUIStyle style = new GUIStyle();
        style.fontSize = 10;
        style.fontStyle = FontStyle.Bold;
        style.normal.textColor = col;
        Handles.Label(floorTarget + Vector3.up * 0.02f, $"{label}: {hDist:F2}m [{status}]", style);
    }
#endif

    #endregion
}

