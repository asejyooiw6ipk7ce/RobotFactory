using System.Collections;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public partial class AssemblySequence
{
    #region 인스펙터 1클릭 단계별 검증 및 연속 실행 (Step-by-Step Test)


    /// <summary>
    /// 지정한 목표 단계(기본 1~3단계)까지 연속으로 자동 실행
    /// </summary>
    public void RunStepsContinuous(int maxStep = -1)
    {
        if (IsBusy) return;
        int target = maxStep > 0 ? maxStep : continuousStepTarget;
        StartCoroutine(ContinuousStepsRoutine(target));
    }

    private IEnumerator ContinuousStepsRoutine(int maxStep)
    {
        isContinuousBusy = true;
        SetState(RobotState.Working, $"1~{maxStep}단계 연속 실행 시작");
        Debug.Log($"[AssemblySequence] ▶ {Status}");

        // 1단계. 클로 상공 안전 접근
        if (maxStep >= 1)
        {
            yield return Step1Routine();
            if (clawPart == null)
            {
                isContinuousBusy = false;
                IsBusy = false;
                yield break;
            }
            yield return new WaitForSeconds(0.15f);
        }

        // 2단계. 하강 및 그리퍼 파지
        if (maxStep >= 2)
        {
            yield return Step2Routine();
            yield return new WaitForSeconds(0.15f);
        }

        // 3단계. 상공 들어올리기 (Lift)
        if (maxStep >= 3)
        {
            yield return Step3Routine();
            yield return new WaitForSeconds(0.15f);
        }

        // 4단계. 소켓 앞 수평 정렬
        if (maxStep >= 4)
        {
            yield return Step4Routine();
            yield return new WaitForSeconds(0.15f);
        }

        // 5단계. 수평 삽입 및 결합
        if (maxStep >= 5)
        {
            yield return Step5Routine();
            yield return new WaitForSeconds(0.15f);
        }

        // 6단계. 그리퍼 분리 및 대기 복귀
        if (maxStep >= 6)
        {
            yield return Step6Routine();
            yield return new WaitForSeconds(0.15f);
        }

        isContinuousBusy = false;
        IsBusy = false;
        SetState(RobotState.Idle, $"1~{maxStep}단계 연속 실행 완료");
        Debug.Log($"[AssemblySequence] ✅ {Status}");
    }

    public void RunStep1_Approach()
    {
        if (IsBusy) return;
        StartCoroutine(Step1Routine());
    }

    private IEnumerator Step1Routine()
    {
        IsBusy = true;
        SetState(RobotState.Moving, "1단계: 클로 상공 안전 접근");
        Debug.Log($"[AssemblySequence] {Status}");
        AutoFindReferences();
        ApplyGripperCloseTarget();

        // [새 공정 시작 안전 복원] 클로가 이전 조립 잔재(차체/소켓 등)에 결합되어 있다면 컨베이어 초기 위치로 복원
        if (initialClawRecorded && clawPart != null && clawPart.parent != initialClawParent)
        {
            ResetClawToInitialState();
        }

        if (gripper != null) yield return gripper.Open();
        if (clawPart == null) { IsBusy = false; yield break; }

        Vector3 pickPos = GetPickPosition();
        Vector3 pickApproach = pickPos + Vector3.up * approachHeight;
        float targetYaw = GetEffectiveYaw();

        if (ik != null)
        {
            ik.pointDown = true;
            ik.useTargetLookDir = false;
            // 상공 안전 고도(0.95m)를 경유하여 팔꿈치를 위(Elbow-Up)로 확실히 세운 후 하강 대기 위치로 접근
            ik.useTargetLookDir = false;
            Vector3 highSafeTransit = new Vector3(pickApproach.x, 0.95f, pickApproach.z);
            yield return ik.MoveTo(highSafeTransit, null);
            yield return ik.MoveLinearSmooth(pickApproach, 0.40f, targetYaw, false);
        }

        SetState(RobotState.Moving, "1단계 완료: 클로 상공 접근 성공");
        Debug.Log($"[AssemblySequence] {Status}");
        IsBusy = false;
    }

    public void RunStep2_Grasp()
    {
        if (IsBusy) return;
        StartCoroutine(Step2Routine());
    }

    private IEnumerator Step2Routine()
    {
        IsBusy = true;
        SetState(RobotState.Working, "2단계: 하강 및 그리퍼 파지");
        Debug.Log($"[AssemblySequence] {Status}");
        AutoFindReferences();
        ApplyGripperCloseTarget();

        // [새 공정 시작 안전 복원] 2단계 단독 실행 시 클로가 이전 조립 잔재(차체 등)에 남아있다면 복원
        if (initialClawRecorded && clawPart != null && clawPart.parent != initialClawParent)
        {
            ResetClawToInitialState();
        }

        if (clawPart == null) { IsBusy = false; yield break; }
        Vector3 pickPos = GetPickPosition();
        float targetYaw = GetEffectiveYaw();

        if (gripper != null) yield return gripper.Open();
        if (ik != null)
        {
            ik.pointDown = true;
            ik.useTargetLookDir = false;
            yield return ik.MoveLinearSmooth(pickPos, descendSpeed, targetYaw, false);
        }
        if (gripper != null) yield return gripper.Close();

        AttachClawToGripper();
        yield return new WaitForSeconds(0.2f);

        SetState(RobotState.Working, "2단계 완료: 파지 및 결합 완료");
        IsBusy = false;
    }

    public void RunStep3_Lift()
    {
        if (IsBusy) return;
        StartCoroutine(Step3Routine());
    }

    private IEnumerator Step3Routine()
    {
        IsBusy = true;
        SetState(RobotState.Working, "3단계: 상공 들어올리기 (Lift)");
        Debug.Log($"[AssemblySequence] {Status}");

        // 클로가 그리퍼에 단단히 결합되어 있는지 보장 (낙하 방지)
        AttachClawToGripper();

        GetSlotInfo(out Vector3 slotCenter, out _, out _);
        Vector3 curTipPos = (ik != null && ik.tip != null) ? ik.tip.position : transform.position;
        // TestTarget 소켓 높이(slotCenter.y)까지 넉넉하게 들어올림
        float safeLiftY = Mathf.Max(curTipPos.y + approachHeight, slotCenter.y);
        Vector3 liftGoal = new Vector3(curTipPos.x, safeLiftY, curTipPos.z);
        float targetYaw = GetEffectiveYaw();

        if (ik != null)
        {
            ik.pointDown = true;
            ik.useTargetLookDir = false;
            yield return ik.MoveLinearSmooth(liftGoal, liftSpeed, targetYaw);
        }

        SetState(RobotState.Working, "3단계 완료: 상공 리프팅 완료");
        Debug.Log($"[AssemblySequence] {Status}");
        IsBusy = false;
    }

    public void RunStep4_Align()
    {
        if (IsBusy) return;
        StartCoroutine(Step4Routine());
    }

    private IEnumerator Step4Routine()
    {
        IsBusy = true;
        SetState(RobotState.Moving, "4단계: 수평 정렬 (4.1 수평 회전 -> 4.2 베이스 회전 -> 4.3 정렬)");
        Debug.Log($"[AssemblySequence] {Status}");

        EnsureInsertDirectionDefault();
        AutoFindReferences();
        if (IsClawHeldByGripper())
        {
            AttachClawToGripper();
        }
        GetSlotInfo(out Vector3 slotCenter, out Vector3 insertApproach, out Vector3 insertDir);

        if (ik != null)
        {
            Vector3 curTipPos = (ik.tip != null) ? ik.tip.position : transform.position;
            float safeY = Mathf.Max(curTipPos.y, slotCenter.y);
            Vector3 horizontalTurnPos = new Vector3(curTipPos.x, safeY, curTipPos.z);

            // [4.1] 클로를 잡은 부분이 수평으로 회전 (차체 소켓 방향 Vector3.left = <-X 지향)
            Debug.Log($"[AssemblySequence] 4.1: 컨베이어 상공에서 그리퍼 끝 수평 회전 (목표={insertDir}, 손목Roll={alignWristYaw:F1}°)");
            ik.pointDown = false;
            ik.useTargetLookDir = true;
            ik.targetLookDir = insertDir;
            yield return ik.MoveTo(horizontalTurnPos, alignWristYaw);
            yield return new WaitForSeconds(0.2f);

            // [4.2] 그리퍼가 소켓(<-X)을 똑바로 바라본 채 베이스 회전 각도에 맞춰 팔을 굽혀주며 소켓 앞 10cm 대기 지점(insertApproach)까지 이동 (사각지대 관통 시 원호 우회)
            Debug.Log($"[AssemblySequence] 4.2: 팔 굽힘 연동 수평 이동 -> 소켓 앞 대기 위치({insertApproach})");
            if (DoesSegmentCrossDeadzone(horizontalTurnPos, insertApproach))
            {
                yield return ik.MoveTo(insertApproach, alignWristYaw);
            }
            else
            {
                yield return ik.MoveLinearSmooth(insertApproach, alignLinearSpeed, alignWristYaw);
            }
            yield return new WaitForSeconds(0.2f);
        }

        SetState(RobotState.Moving, "4단계 완료: 소켓 앞 수평 정렬 성공");
        Debug.Log($"[AssemblySequence] {Status}");
        IsBusy = false;
    }

    public void RunStep5_Insert(bool applyDefectAngle = true)
    {
        if (IsBusy) return;
        StartCoroutine(Step5Routine(applyDefectAngle));
    }

    private IEnumerator Step5Routine(bool applyDefectAngle = true)
    {
        IsBusy = true;
        SetState(RobotState.Assembling, "5단계: 수평 삽입 및 스냅 결합 (Socket 기준)");
        Debug.Log($"[AssemblySequence] {Status}");

        GetSlotInfo(out Vector3 slotCenter, out Vector3 insertApproach, out Vector3 insertDir);

        if (ik != null)
        {
            // [4.3] 파지된 클로 루트가 정확히 Socket(slotCenter)에 도달하도록 그리퍼 끝(ik.tip) 목표점을 보정하여 수평 직진 삽입
            Vector3 tipInsertGoal = GetTipInsertGoal(slotCenter, insertDir);
            ik.pointDown = false;
            ik.useTargetLookDir = true;
            ik.targetLookDir = insertDir;
            yield return ik.MoveLinearSmooth(tipInsertGoal, insertSpeed, alignWristYaw);
        }

        // 스냅 결합 안착 (설정된 불량률에 따른 각도 오차 적용)
        float angleError = applyDefectAngle ? CalculateAngleError() : 0f;
        SnapClawToSocket(angleError);
        LastAngleError = angleError;

        SetState(RobotState.Assembling, $"5단계 완료: 수평 삽입 및 스냅 완료 (오차: {angleError:F1}°)");
        Debug.Log($"[AssemblySequence] {Status}");
        IsBusy = false;
    }

    public void RunStep6_Release()
    {
        if (IsBusy) return;
        StartCoroutine(Step6Routine());
    }

    private IEnumerator Step6Routine()
    {
        IsBusy = true;
        SetState(RobotState.Working, "6단계: 그리퍼 분리 및 대기 복귀");
        Debug.Log($"[AssemblySequence] {Status}");

        // 1. 그리퍼 개방 (분리)
        if (gripper != null)
        {
            yield return gripper.Open();
        }

        // 2. 수평 후퇴
        GetSlotInfo(out Vector3 slotCenter, out Vector3 insertApproach, out Vector3 insertDir);

        if (ik != null)
        {
            ik.pointDown = false;
            ik.useTargetLookDir = true;
            ik.targetLookDir = insertDir;
            yield return ik.MoveLinear(insertApproach, 0.08f, alignWristYaw);

            // 상공으로 수직 상승 후 홈 복귀 (차체 간섭 방지)
            Vector3 highApproach = new Vector3(insertApproach.x, GetHomeWorldPosition().y + 0.25f, insertApproach.z);
            yield return ik.MoveLinearSmooth(highApproach, retractLiftSpeed, alignWristYaw);

            // 3. 홈 대기 자세로 복귀 (초기 관절 각도로 안전 복귀)
            ik.pointDown = true;
            ik.useTargetLookDir = false;
            yield return ik.ReturnToHomeJoints(2.2f);
        }

        SetState(RobotState.Idle, "6단계 완료: 전체 복귀 완료");
        IsBusy = false;
    }


    /// Play 모드 중 손목 각도만 미세 조정
    /// </summary>
    public IEnumerator RotateWristLive(float targetYaw)
    {
        if (ik == null) yield break;
        Vector3 curPos = ik.tip != null ? ik.tip.position : transform.position;
        yield return ik.MoveTo(curPos, targetYaw);
    }

    #endregion

    #region Scene 기즈모 시각화

    private void OnDrawGizmosSelected()
    {
        if (clawPart != null)
        {
            Vector3 pos = GetPickPosition();
            Vector3 valleyA = Vector3.ProjectOnPlane(clawPart.up, Vector3.up).normalized;
            Vector3 valleyB = Vector3.ProjectOnPlane(clawPart.forward, Vector3.up).normalized;

            Gizmos.color = Color.green;
            Gizmos.DrawLine(pos - valleyA * 0.15f, pos + valleyA * 0.15f);

            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(pos - valleyB * 0.15f, pos + valleyB * 0.15f);
        }

        GetSlotInfo(out Vector3 slotCenter, out Vector3 approachPos, out Vector3 insertDir);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(approachPos, 0.035f); // 4단계 수평 접근점

        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(slotCenter, 0.035f);  // 5단계 조립 완성 슬롯
        Gizmos.DrawLine(approachPos, slotCenter);    // 수평 삽입 경로

        // 공정 배치 위치 및 로봇 작업 반경 도달 가이드선
        if (showReachabilityGizmos && ik != null)
        {
#if UNITY_EDITOR
            // AssemblySequence 선택 시에도 로봇 360도 작업 반경 원형 시각 표시
            if (ik.showWorkRadius)
            {
                ik.DrawWorkEnvelopeGizmo();
            }

            Vector3 center = ik.GetWorkCenter();

            // 1. 부품1 픽업점
            Vector3 pickPos = partStopPoint != null ? partStopPoint.position : (clawPart != null ? GetPickPosition() : Vector3.zero);
            if (pickPos != Vector3.zero) DrawReachGuideLine(center, pickPos, "부품1 (Pick)");

            // 2. 부품2 소켓 조립점
            if (slotCenter != Vector3.zero) DrawReachGuideLine(center, slotCenter, "부품2 (Socket)");

            // 3. 폐기함
            Vector3 rejectPos = GetRejectBinDropPosition();
            if (rejectBin != null || rejectPos != Vector3.zero) DrawReachGuideLine(center, rejectPos, "폐기함 (RejectBin)");

            // 4. 완제품 배출 컨베이어
            if (outputConveyorPoint != null) DrawReachGuideLine(center, outputConveyorPoint.position, "완제품 (Conveyor)");
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
