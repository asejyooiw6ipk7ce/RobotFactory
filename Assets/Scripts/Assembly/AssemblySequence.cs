using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ==========================================================================================
// [AssemblySequence] 로봇 부품 조립 검수 스마트 공정 - 스테이션 2 조립 시퀀스
//
// 클래스 다이어그램(UML) 및 SRS 요구사항(FR-07 수평 조립, FR-08 결합 틀어짐) 완벽 준수:
// 1. 부품1(클로) 자세 적응형 파지(Grasp) & 그리퍼 관통 원천 차단
// 2. 부품2(로봇모듈 moto_base) 소켓 앞 수평 정렬(Horizontal Align)
// 3. 수평 방향 전진 삽입(Insert) 및 스냅 고정(Snap Attach)
// 4. 결합 틀어짐 오차 주입(FR-08) 및 PartInfo/BaseInfo에 DefectType 기록
// 5. 완제품 컨베이어 C(이송 버퍼)로 이동 및 릴리즈(Release)
// 6. 불량품 폐기함(RejectBin) 폐기(Reject) 및 재투입(Refeed) 지원
// ==========================================================================================


public partial class AssemblySequence : MonoBehaviour
{
    #region 상태 프로퍼티

    private bool isStepBusy = false;
    private bool isContinuousBusy = false;
    public bool IsBusy
    {
        get => isStepBusy || isContinuousBusy;
        private set => isStepBusy = value;
    }
    public string Status { get; private set; } = "대기";
    public bool LastAssemblyOk { get; private set; } = false;
    public float LastAngleError { get; private set; } = 0f;
    public bool LastRejectOk { get; private set; } = false;

    // 편의 접근자
    public PartInfo CurrentPart => currentPartInfo;
    public BaseInfo CurrentBase => currentBase;

    #endregion

    #region 초기 상태 백업 (반복 검증 및 리셋용)

    private Vector3 initialClawPosition;
    private Quaternion initialClawRotation;
    private Transform initialClawParent;
    private bool initialClawRecorded = false;

    #endregion

    #region 초기화 및 자동 연결

    void OnValidate()
    {
        EnsureInsertDirectionDefault();
    }

    void Awake()
    {
        EnsureInsertDirectionDefault();
        AutoFindReferences();
    }

    private void EnsureInsertDirectionDefault()
    {
        if (!insertDirMigratedToLeft)
        {
            // 기존 씬 직렬화 잔재(Vector3.forward = +Z)를 차체 소켓 방향(Vector3.left = -X)으로 1회 자동 보정
            if ((customInsertDir - Vector3.forward).sqrMagnitude < 0.01f || customInsertDir.sqrMagnitude < 0.001f)
            {
                customInsertDir = Vector3.left;
            }
            insertDirMigratedToLeft = true;
        }

        if (!mountOffsetCalibratedV3)
        {
            if (Mathf.Abs(socketMountOffset - 0.065f) < 0.005f || Mathf.Abs(socketMountOffset - 0.104f) < 0.002f || socketMountOffset < 0.001f)
            {
                socketMountOffset = 0.114f;
            }
            mountOffsetCalibratedV2 = true;
            mountOffsetCalibratedV3 = true;
        }
    }

    void Start()
    {
        AutoFindReferences();
        ApplyGripperCloseTarget();
        RecordInitialClawState();
    }


    /// <summary>
    /// 플레이 시작 시점의 클로 원위치(컨베이어 위)를 기억
    /// </summary>
    public void RecordInitialClawState()
    {
        if (clawPart != null && !initialClawRecorded)
        {
            initialClawPosition = clawPart.position;
            initialClawRotation = clawPart.rotation;
            initialClawParent = clawPart.parent;
            initialClawRecorded = true;
        }
    }


    /// [원클릭 초기화] 언제든 클로를 컨베이어 원위치로 되돌리고 로봇을 단정한 홈 자세로 복원
    /// </summary>
    [ContextMenu("초기 상태로 리셋 (Reset to Initial State)")]
    public void ResetToInitialState()
    {
        if (clawPart != null)
        {
            if (initialClawRecorded)
            {
                clawPart.SetParent(initialClawParent, true);
                clawPart.position = initialClawPosition;
                clawPart.rotation = initialClawRotation;
            }
            else if (partStopPoint != null)
            {
                clawPart.SetParent(null, true);
                clawPart.position = partStopPoint.position;
            }
            var rb = clawPart.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;
            Debug.Log("[AssemblySequence] 부품1(클로)이 컨베이어 초기 위치로 리셋되었습니다.");
        }

        if (gripper != null)
        {
            StartCoroutine(gripper.Open());
        }

        if (ik != null)
        {
            ik.pointDown = true;
            ik.useTargetLookDir = false;
            StartCoroutine(ik.MoveTo(GetHomeWorldPosition(), null));
        }

        IsBusy = false;
        Status = "대기 (초기화 완료)";
    }



    /// </summary>

    /// <summary>
    /// 클로 부품만 컨베이어 초기 위치로 깨끗하게 복원 (로봇 코루틴 간섭 없음)
    /// </summary>
    public void ResetClawToInitialState()
    {
        if (clawPart != null)
        {
            if (initialClawRecorded)
            {
                clawPart.SetParent(initialClawParent, true);
                clawPart.position = initialClawPosition;
                clawPart.rotation = initialClawRotation;
            }
            else if (partStopPoint != null)
            {
                clawPart.SetParent(null, true);
                clawPart.position = partStopPoint.position;
            }
            var rb = clawPart.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;
            Debug.Log("[AssemblySequence] 부품1(클로)이 컨베이어 초기 위치로 리셋되었습니다.");
        }
    }


    /// <summary>
    /// 모터 잠김 간섭 해제, 로봇 컴포넌트 자동 바인딩 및 씬의 부품1/부품2 탐색
    /// </summary>
    public void AutoFindReferences()
    {
        // 1. URDF 기본 Controller / FKRobot / JointControl 간섭 비활성화
        foreach (var mb in GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            string typeName = mb.GetType().Name;
            if (typeName == "Controller" || typeName == "FKRobot" || typeName == "JointControl")
            {
                mb.enabled = false;
            }
        }

        // 2. ArmIK 자동 연결
        if (ik == null) ik = GetComponent<ArmIK>();
        if (ik == null) ik = GetComponentInChildren<ArmIK>();
        if (ik == null) ik = FindAnyObjectByType<ArmIK>();
        if (ik != null) ik.AutoAssign();

        // 3. GripperController 자동 연결
        if (gripper == null) gripper = GetComponent<GripperController>();
        if (gripper == null) gripper = GetComponentInChildren<GripperController>();
        if (gripper == null) gripper = FindAnyObjectByType<GripperController>();
        if (gripper != null)
        {
            gripper.AutoFindFingers();
            ApplyGripperCloseTarget();
        }

        // 4. 부품1 (클로) 자동 탐색 및 콜라이더 보강
        if (clawPart == null)
        {
            GameObject claw = GameObject.Find("claw");
            if (claw == null)
            {
                foreach (var go in FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (go.name.ToLower().Contains("claw"))
                    {
                        claw = go;
                        break;
                    }
                }
            }
            if (claw != null) clawPart = claw.transform;
        }

        if (clawPart != null)
        {
            var rb = clawPart.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;

            // grap_platfrom_01에 견고한 BoxCollider 보강하여 파지 안정성 확보
            Transform graspPlatform = clawPart.Find("grap_platfrom_01");
            if (graspPlatform != null)
            {
                var box = graspPlatform.GetComponent<BoxCollider>();
                if (box == null) box = graspPlatform.gameObject.AddComponent<BoxCollider>();
                box.size = new Vector3(0.035f, 0.040f, 0.040f);
                box.center = Vector3.zero;
            }

            // PartInfo 바인딩 (팀원 클래스 정의 준수)
            if (currentPartInfo == null)
            {
                currentPartInfo = clawPart.GetComponent<PartInfo>();
                if (currentPartInfo == null)
                {
                    currentPartInfo = clawPart.gameObject.AddComponent<PartInfo>();
                    currentPartInfo.id = "claw_01";
                    currentPartInfo.defectType = DefectType.None;
                }
            }
        }

        // 5. 부품2 (로봇 베이스 moto_base) 자동 탐색
        if (targetRobotBase == null)
        {
            GameObject baseObj = GameObject.Find("moto_base");
            if (baseObj == null)
            {
                foreach (var go in FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    string lName = go.name.ToLower();
                    if (lName.Contains("moto_base") || lName.Contains("moto_bot"))
                    {
                        baseObj = go;
                        break;
                    }
                }
            }
            if (baseObj != null) targetRobotBase = baseObj.transform;
        }

        if (targetRobotBase != null)
        {
            if (currentBase == null)
            {
                currentBase = targetRobotBase.GetComponent<BaseInfo>();
                if (currentBase == null)
                {
                    currentBase = targetRobotBase.gameObject.AddComponent<BaseInfo>();
                    currentBase.id = "moto_base_01";
                    currentBase.defectType = DefectType.None;
                }
            }

            if (baseSocket == null)
            {
                foreach (var t in targetRobotBase.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name.Equals("Socket", System.StringComparison.OrdinalIgnoreCase))
                    {
                        baseSocket = t;
                        break;
                    }
                }
            }

            if (currentBase != null)
            {
                if (baseSocket != null) currentBase.socket = baseSocket;
                else if (currentBase.socket != null) baseSocket = currentBase.socket;
                else
                {
                    foreach (var t in targetRobotBase.GetComponentsInChildren<Transform>(true))
                    {
                        string lName = t.name.ToLower();
                        if (lName.Contains("socket") || lName.Contains("mount") || lName.Contains("slot") || lName.Contains("suctioncup"))
                        {
                            currentBase.socket = t;
                            baseSocket = t;
                            break;
                        }
                    }
                }
            }
        }

        // 6. 기준점 자동 탐색
        if (basePoint == null && targetRobotBase != null)
        {
            basePoint = targetRobotBase;
        }
        if (rejectBin == null)
        {
            GameObject binObj = GameObject.Find("RejectBin");
            if (binObj == null) binObj = GameObject.Find("reject_bin");
            if (binObj != null) rejectBin = binObj.transform;
        }

        // 7. TestTarget (사용자 지정 정밀 소켓 위치) 탐색
        if (testTargetSocket == null)
        {
            if (ik != null && ik.testTarget != null)
            {
                testTargetSocket = ik.testTarget;
            }
            if (testTargetSocket == null)
            {
                foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (t.name == "TestTarget")
                    {
                        testTargetSocket = t;
                        break;
                    }
                }
            }
        }
    }

    public void SetPart(PartInfo part)
    {
        currentPartInfo = part;
        clawPart = part != null ? part.transform : null;
    }

    public void SetPart(Transform partTransform)
    {
        clawPart = partTransform;
        currentPartInfo = partTransform != null ? partTransform.GetComponent<PartInfo>() : null;
    }

    public void SetBase(BaseInfo b)
    {
        currentBase = b;
        targetRobotBase = b != null ? b.transform : null;
    }

    public void SetBase(Transform baseTransform)
    {
        targetRobotBase = baseTransform;
        currentBase = baseTransform != null ? baseTransform.GetComponent<BaseInfo>() : null;
    }


    /// <summary>
    /// 그리퍼 닫힘 한계값을 gripWidthMm에 맞춰 설정하여 관통을 원천 차단
    /// </summary>
    public void ApplyGripperCloseTarget()
    {
        if (gripper == null) gripper = GetComponentInChildren<GripperController>();
        if (gripper != null)
        {
            float targetM = (48f - gripWidthMm) * 0.5f * 0.001f;
            gripper.leftClose = targetM;
            gripper.rightClose = targetM;
            gripper.useGraspCheck = false;
        }
    }

    #endregion

    #region 기하학 계산 및 자세 분석


    /// <summary>
    /// 클로가 현재 로봇 그리퍼에 파지(Attach)되어 들려있는 중인지 여부 판별
    /// </summary>
    public bool IsClawHeldByGripper()
    {
        if (clawPart == null || clawPart.parent == null) return false;
        if (gripper != null && (clawPart.parent == gripper.attachPoint || clawPart.parent.IsChildOf(gripper.transform))) return true;
        if (ik != null && ik.tip != null && (clawPart.parent == ik.tip || clawPart.parent.IsChildOf(ik.tip))) return true;
        return false;
    }


    /// <summary>
    /// 클로 허브(grap_platfrom_01)의 실제 월드 위치를 기반으로 최적 파지 높이 계산
    /// </summary>
    public Vector3 GetPickPosition()
    {
        if (clawPart == null)
        {
            if (partStopPoint != null) return partStopPoint.position;
            return new Vector3(-1.168f, 0.59f, -14.81f);
        }

        // [상태 오염 방지] 만약 클로가 이전 테스트 잔재로 차체(moto_base)나 소켓에 결합되어 있는 경우에만 복원
        // (그리퍼에 물려있는 도중에는 절대 분리/리셋하지 않음!)
        if (!IsClawHeldByGripper() && initialClawRecorded && clawPart.parent != initialClawParent)
        {
            Debug.LogWarning("[AssemblySequence] 클로가 이전 조립 잔재에 결합되어 있었습니다. 컨베이어 원래 위치로 자동 복원합니다.");
            clawPart.SetParent(initialClawParent, true);
            clawPart.position = initialClawPosition;
            clawPart.rotation = initialClawRotation;
        }

        Transform graspPlatform = clawPart.Find("grap_platfrom_01");
        Vector3 basePos = graspPlatform != null ? graspPlatform.position : clawPart.position + new Vector3(0f, 0.03f, 0f);
        float targetY = Mathf.Max(minSafeFloorY, basePos.y + pickHeightOffset);
        return new Vector3(basePos.x, targetY, basePos.z);
    }


    /// <summary>
    /// [동적 자세 인식] 클로의 현재 월드 회전각을 실시간 계산하여,
    /// X자 다리 간섭 없이 몸통 허브를 정확히 파지할 수 있는 최적 손목 각도(Yaw) 반환
    /// </summary>
    public float GetEffectiveYaw()
    {
        if (!autoDetectClawYaw || clawPart == null) return defaultPickYaw;

        Vector3 valley = Vector3.ProjectOnPlane(clawPart.up, Vector3.up).normalized;
        if (valley.sqrMagnitude < 0.001f)
        {
            valley = Vector3.ProjectOnPlane(clawPart.forward, Vector3.up).normalized;
        }

        float autoYaw = Mathf.Atan2(valley.x, valley.z) * Mathf.Rad2Deg + clawYawOffset;
        return Mathf.Repeat(autoYaw + 360f, 360f);
    }


    /// <summary>
    /// 부품2(moto_base)의 조립 슬롯 위치 및 수평 접근/삽입 벡터 정밀 산출 (Socket 1순위)
    /// </summary>
    public void GetSlotInfo(out Vector3 slotCenter, out Vector3 approachPos, out Vector3 insertDir)
    {
        // 1순위: moto_base의 실제 Socket 객체 (사용자가 추가한 결합 지점)
        Transform slotTransform = baseSocket;
        if (slotTransform == null && targetRobotBase != null)
        {
            foreach (var t in targetRobotBase.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.Equals("Socket", System.StringComparison.OrdinalIgnoreCase))
                {
                    slotTransform = t;
                    baseSocket = t;
                    break;
                }
            }
        }
        if (slotTransform == null && currentBase != null && currentBase.socket != null)
        {
            slotTransform = currentBase.socket;
            baseSocket = slotTransform;
        }

        // 2순위: 씬의 TestTarget
        if (slotTransform == null)
        {
            slotTransform = testTargetSocket;
            if (slotTransform == null && ik != null && ik.testTarget != null)
            {
                slotTransform = ik.testTarget;
                testTargetSocket = slotTransform;
            }
            if (slotTransform == null)
            {
                foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (t.name == "TestTarget")
                    {
                        slotTransform = t;
                        testTargetSocket = t;
                        break;
                    }
                }
            }
        }

        // 3순위: 기타 suctioncup 등
        if (slotTransform == null && targetRobotBase != null)
        {
            foreach (var t in targetRobotBase.GetComponentsInChildren<Transform>(true))
            {
                string lName = t.name.ToLower();
                if (lName.Contains("socket") || lName.Contains("mount") || lName.Contains("slot") || lName.Contains("suctioncup"))
                {
                    slotTransform = t;
                    if (currentBase != null && currentBase.socket == null)
                    {
                        currentBase.socket = t;
                    }
                    break;
                }
            }
        }

        if (slotTransform != null)
        {
            slotCenter = slotTransform.position;
        }
        else if (targetRobotBase != null)
        {
            slotCenter = targetRobotBase.position + new Vector3(0.0f, 0.55f, 0.0f);
        }
        else if (basePoint != null)
        {
            slotCenter = basePoint.position + new Vector3(0.0f, 0.55f, 0.0f);
        }
        else
        {
            slotCenter = new Vector3(-2.369f, 0.947f, -15.105f);
        }

        if (customInsertDir.sqrMagnitude > 0.001f)
        {
            insertDir = Vector3.ProjectOnPlane(customInsertDir, Vector3.up).normalized;
        }
        else if (targetRobotBase != null)
        {
            // moto_base의 뒤쪽(소켓 결합면 방향 = 현재 씬 기준 Vector3.left)
            insertDir = Vector3.ProjectOnPlane(-targetRobotBase.forward, Vector3.up).normalized;
        }
        else
        {
            insertDir = Vector3.left;
        }
        if (insertDir.sqrMagnitude < 0.001f) insertDir = Vector3.left;

        // 슬롯 앞 수평 대기 접근점: 클로 자체의 전방 돌출 길이(clawLengthOffset) + 소켓 앞 대기 유격(insertDistance) 만큼 이격
        approachPos = slotCenter - insertDir * (insertDistance + clawLengthOffset);
    }


    /// <summary>
    /// 소켓 입구(slotCenter)를 기준으로 클로 주름 링(edge_rings_01) 뒷면이 하우징 정면에 딱 밀착되도록 보정된 클로 루트 최종 장착 위치 산출
    /// </summary>
    public Vector3 GetSocketMountPosition(Vector3 slotCenter, Vector3 insertDir)
    {
        return slotCenter - insertDir * socketMountOffset;
    }


    /// <summary>
    /// 그리퍼가 클로를 파지한 상태에서 클로 루트(clawPart.position)가 최종 장착점(mountPos)에 정확히 안착하도록 하는 그리퍼 끝(ik.tip) 목표 위치 산출
    /// </summary>
    public Vector3 GetTipInsertGoal(Vector3 slotCenter, Vector3 insertDir)
    {
        Vector3 mountPos = GetSocketMountPosition(slotCenter, insertDir);
        if (clawPart != null && ik != null && ik.tip != null && IsClawHeldByGripper())
        {
            Vector3 tipToClaw = clawPart.position - ik.tip.position;
            return mountPos - tipToClaw;
        }
        return mountPos;
    }


    /// <summary>
    /// 그리퍼 끝에 클로를 결합(Attach)하여 들어올릴 때 완벽히 고정
    /// </summary>
    public void AttachClawToGripper()
    {
        if (clawPart == null) return;
        Transform parentPoint = (gripper != null && gripper.attachPoint != null)
            ? gripper.attachPoint
            : (ik != null && ik.tip != null ? ik.tip : transform);

        if (parentPoint != null)
        {
            var rb = clawPart.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            clawPart.SetParent(parentPoint, true);
            Debug.Log($"[AssemblySequence] 부품1(클로)을 '{parentPoint.name}'의 자식으로 결합(Attach) 완료.");
        }
    }


    /// <summary>
    /// 클로 부품을 moto_base 조립 소켓(TestTarget)에 정밀 스냅 고정하고 결합 각도 오차를 적용 (FR-07, FR-08)
    /// </summary>
    public void SnapClawToSocket(float angleError = 0f)
    {
        if (clawPart == null) return;

        // 1. 물리 안정화 (낙하 방지)
        var rb = clawPart.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // 2. 소켓 트랜스폼 확인 (baseSocket 우선)
        Transform socketTransform = baseSocket;
        if (socketTransform == null && targetRobotBase != null)
        {
            foreach (var t in targetRobotBase.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.Equals("Socket", System.StringComparison.OrdinalIgnoreCase))
                {
                    socketTransform = t;
                    baseSocket = t;
                    break;
                }
            }
        }
        if (socketTransform == null) socketTransform = testTargetSocket;
        if (socketTransform == null && currentBase != null && currentBase.socket != null)
            socketTransform = currentBase.socket;
        else if (socketTransform == null && targetRobotBase != null)
        {
            foreach (var t in targetRobotBase.GetComponentsInChildren<Transform>(true))
            {
                string lName = t.name.ToLower();
                if (lName.Contains("socket") || lName.Contains("mount") || lName.Contains("slot") || lName.Contains("suctioncup"))
                {
                    socketTransform = t;
                    if (currentBase != null && currentBase.socket == null) currentBase.socket = t;
                    break;
                }
            }
        }

        // 3. 소켓 또는 베이스에 정밀 스냅 고정
        GetSlotInfo(out Vector3 slotCenter, out _, out Vector3 insertDir);
        Vector3 targetPos = GetSocketMountPosition(slotCenter, insertDir);

        // 클로 프리팹 기준 로컬 +X(guide_01 / edge_rings_01)가 마스트 결합 방향(insertDir = -X)을 향하고,
        // 로컬 -X(suction_01 파란 석션컵 4개)가 차체 적재대(+X)를 향하도록 정방향 장착 회전 산출
        // (Socket의 월드 회전이 Identity(0,0,0)일 때 그대로 대입하면 클로가 180도 거꾸로 뒤집히는 현상 방지)
        Vector3 baseUp = targetRobotBase != null ? targetRobotBase.up : Vector3.up;
        Vector3 cleanInsertDir = Vector3.ProjectOnPlane(insertDir, baseUp).normalized;
        if (cleanInsertDir.sqrMagnitude < 0.001f) cleanInsertDir = Vector3.left;

        // 현재 그리퍼에 수평 파지된 상하 방향(+Y/-Y)을 유지하여 스냅 순간 180도 롤 점프 원천 차단
        Vector3 desiredUp = (Vector3.Dot(clawPart.up, baseUp) < -0.1f) ? -baseUp : baseUp;
        Vector3 desiredForward = Vector3.Cross(cleanInsertDir, desiredUp).normalized;
        Quaternion snapRotation = desiredForward.sqrMagnitude > 0.001f
            ? Quaternion.LookRotation(desiredForward, desiredUp)
            : clawPart.rotation;

        if (targetRobotBase != null)
        {
            clawPart.SetParent(targetRobotBase, true);
            clawPart.position = targetPos;
            clawPart.rotation = snapRotation;
        }
        else if (socketTransform != null)
        {
            Transform activeParent = (!socketTransform.gameObject.activeInHierarchy && socketTransform.parent != null)
                ? socketTransform.parent
                : socketTransform;
            clawPart.SetParent(activeParent, true);
            clawPart.position = targetPos;
            clawPart.rotation = snapRotation;
        }

        // 4. 결합 각도 오차(FR-08) 적용: 클로 삽입 샤프트 중심축(Local +X = Vector3.right) 기준 Roll 틀어짐 적용
        if (Mathf.Abs(angleError) > 0.001f)
        {
            clawPart.Rotate(Vector3.right, angleError, Space.Self);
        }

        // 5. 결합 결과 DefectType 판정 및 PartInfo / BaseInfo에 기록 (FR-08)
        bool isAngleDefect = Mathf.Abs(angleError) >= defectiveAngleRange.x;
        DefectType assignedDefect = isAngleDefect ? DefectType.AngleError : DefectType.None;

        if (currentPartInfo != null)
        {
            currentPartInfo.assemblyAngleError = angleError;
            currentPartInfo.defectType = assignedDefect;
        }
        if (currentBase != null)
        {
            currentBase.defectType = assignedDefect;
        }

        Debug.Log($"[AssemblySequence] 클로 부품이 소켓에 완벽 스냅 결합되었습니다. (결합 오차: {angleError:F2}°, 불량유형: {assignedDefect})");
    }

    #endregion

    #region 다이어그램 조립 시퀀스 (Assemble)

    public IEnumerator Assemble()
    {
        yield return Assemble(-1f);
    }


    /// <summary>
    /// [UML: + Assemble(float graspYaw) : IEnumerator]
    /// 전체 수평 조립 시퀀스 실행 (SRS FR-07 및 FR-08 준수)
    /// </summary>
    public IEnumerator Assemble(float graspYaw)
    {
        if (IsBusy) yield break;

        IsBusy = true;
        LastAssemblyOk = false;
        LastAngleError = 0f;
        Status = "0. 조립 시퀀스 준비 중...";
        Debug.Log($"[AssemblySequence] {Status}");

        AutoFindReferences();
        ApplyGripperCloseTarget();

        // 베이스와 부품 호환성 확인
        if (currentBase != null && currentPartInfo != null)
        {
            if (!currentBase.Accepts(currentPartInfo))
            {
                Debug.LogWarning("[AssemblySequence] currentBase가 해당 부품을 허용하지 않습니다.");
            }
        }

        // ArmIK 초기화 대기
        if (ik != null)
        {
            float waitTimer = 0f;
            while (!ik.Ready && waitTimer < 3f)
            {
                waitTimer += Time.deltaTime;
                yield return null;
            }
        }

        // 1. 그리퍼 초기 열기 (대기 상태)
        Status = "1. 그리퍼 개방 및 클로 감지";
        Debug.Log($"[AssemblySequence] {Status}");
        if (gripper != null) yield return gripper.Open();

        if (clawPart == null)
        {
            Debug.LogError("[AssemblySequence] 조립 대상 부품1(claw)을 찾을 수 없습니다.");
            Status = "오류: 부품1 없음";
            IsBusy = false;
            yield break;
        }

        // 손목 목표 각도 산출 (음수면 자동 감지)
        float targetYaw = (graspYaw >= 0f) ? graspYaw : GetEffectiveYaw();
        Vector3 pickPos = GetPickPosition();
        Vector3 pickApproach = pickPos + Vector3.up * approachHeight;
        float safeTransitY = pickApproach.y + 0.5f;
        Vector3 safeHighTransit = new Vector3(pickPos.x, safeTransitY, pickPos.z);

        // 2. 상공 경유하여 클로 상공으로 접근 (상공 고도 0.95m 경유로 팔꿈치를 위(Elbow-Up)로 확실히 유지)
        Status = $"2. 클로 상공으로 접근 (목표 손목 {targetYaw:F1}°)";
        Debug.Log($"[AssemblySequence] {Status}");
        if (ik != null)
        {
            ik.pointDown = true;
            ik.useTargetLookDir = false;
            Vector3 highSafeTransit = new Vector3(pickApproach.x, 0.95f, pickApproach.z);
            yield return ik.MoveTo(highSafeTransit, null);
            yield return ik.MoveTo(pickApproach, targetYaw);
        }

        // 3. 파지 하강 (Descend) & 그리퍼 닫기 (Grasp)
        Status = "3. 하강 및 그리퍼 파지 (Grasp)";
        Debug.Log($"[AssemblySequence] {Status}");
        if (ik != null)
        {
            ik.pointDown = true;
            ik.useTargetLookDir = false;
            yield return ik.MoveLinearSmooth(pickPos, descendSpeed, targetYaw);
        }

        if (gripper != null)
        {
            yield return gripper.Close();
        }

        AttachClawToGripper();
        yield return new WaitForSeconds(0.2f);

        // 4. 안전 상공 리프팅 (Lift)
        Status = "4. 클로 들어올리기 (Lift)";
        Debug.Log($"[AssemblySequence] {Status}");
        AttachClawToGripper();

        GetSlotInfo(out Vector3 slotCenter, out Vector3 insertApproach, out Vector3 insertDir);
        Vector3 curTipPos = (ik != null && ik.tip != null) ? ik.tip.position : transform.position;
        float safeY = Mathf.Max(curTipPos.y + approachHeight, slotCenter.y);
        Vector3 liftGoal = new Vector3(curTipPos.x, safeY, curTipPos.z);

        if (ik != null)
        {
            ik.pointDown = true;
            ik.useTargetLookDir = false;
            yield return ik.MoveLinearSmooth(liftGoal, liftSpeed, targetYaw);
        }

        yield return new WaitForSeconds(0.15f);

        // 5. [4.1 & 4.2] TestTarget 소켓 앞 수평 정렬 (Horizontal Align, FR-07)
        Status = "5. 그리퍼 수평 전환(4.1) 후 shoulder_link 회전(4.2) (소켓 앞 정렬)";
        Debug.Log($"[AssemblySequence] {Status}");

        if (ik != null)
        {
            // [4.1] 컨베이어 상공에서 그리퍼 끝 수평 회전 (차체 소켓 방향 <-X 지향)
            ik.pointDown = false;
            ik.useTargetLookDir = true;
            ik.targetLookDir = insertDir;
            yield return ik.MoveTo(liftGoal, alignWristYaw);
            yield return new WaitForSeconds(0.2f);

            // [4.2] 그리퍼가 소켓 방향(<-X)을 바라본 채 회전 각도에 맞춰 팔을 굽히며 소켓 앞 대기 위치까지 직선(Linear) 이동
            yield return ik.MoveLinearSmooth(insertApproach, alignLinearSpeed, alignWristYaw);
            yield return new WaitForSeconds(0.2f);
        }

        // 6. [4.3] Socket으로 수평 삽입 (Insert, FR-07)
        Status = "6. Socket으로 수평 삽입 (Insert)";
        Debug.Log($"[AssemblySequence] {Status}");
        if (ik != null)
        {
            Vector3 tipInsertGoal = GetTipInsertGoal(slotCenter, insertDir);
            ik.pointDown = false;
            ik.useTargetLookDir = true;
            ik.targetLookDir = insertDir;
            yield return ik.MoveLinearSmooth(tipInsertGoal, insertSpeed, alignWristYaw);
        }

        yield return new WaitForSeconds(0.2f);

        // 7. 스냅 고정 및 결합 틀어짐 적용 (FR-07, FR-08)
        Status = "7. 스냅 결합 및 결합 오차 적용 (FR-08)";
        Debug.Log($"[AssemblySequence] {Status}");

        // 결합 틀어짐 확률 계산 (FR-08)
        bool willBeDefective = (UnityEngine.Random.value < defectProbability);
        float angleError = willBeDefective
            ? UnityEngine.Random.Range(defectiveAngleRange.x, defectiveAngleRange.y)
            : UnityEngine.Random.Range(normalAngleRange.x, normalAngleRange.y);

        if (UnityEngine.Random.value < 0.5f) angleError = -angleError;
        LastAngleError = angleError;

        // 소켓에 완벽 스냅 결합 및 오차 주입 (내부에서 PartInfo / BaseInfo.defectType 기록)
        SnapClawToSocket(angleError);

        if (currentPartInfo != null)
        {
            currentPartInfo.assemblyAngleError = angleError;
        }

        DefectType finalDefect = willBeDefective ? DefectType.AngleError : DefectType.None;
        Debug.Log($"[AssemblySequence] 결합 완료 | 틀어짐 각도: {angleError:F2}° (불량 유형: {finalDefect})");

        yield return new WaitForSeconds(0.3f);

        // 8. 완제품 그리퍼 분리 (Release) 및 안전 후퇴
        Status = "8. 완제품 그리퍼 분리 (Release) 및 안전 후퇴";
        Debug.Log($"[AssemblySequence] {Status}");

        if (gripper != null)
        {
            yield return gripper.Open();
        }

        if (ik != null)
        {
            // 수평 후퇴
            ik.pointDown = false;
            ik.useTargetLookDir = true;
            ik.targetLookDir = insertDir;
            yield return ik.MoveLinear(insertApproach, 0.08f, alignWristYaw);

            // 상공으로 수직 상승 후 홈 복귀 (차체 간섭 방지)
            Vector3 highApproach = new Vector3(insertApproach.x, GetHomeWorldPosition().y + 0.25f, insertApproach.z);
            yield return ik.MoveLinearSmooth(highApproach, retractLiftSpeed, alignWristYaw);

            // 대기 자세로 복귀
            ik.pointDown = true;
            ik.useTargetLookDir = false;
            yield return ik.MoveTo(GetHomeWorldPosition(), 0f);
        }

        LastAssemblyOk = true;
        Status = $"조립 완료 (오차: {angleError:F1}°, 불량: {willBeDefective})";
        Debug.Log($"[AssemblySequence] {Status}");
        IsBusy = false;
    }

    #endregion

    #region 다이어그램 폐기 (Reject) 및 재투입 (Refeed)


    /// <summary>
    /// [UML: + Reject(bool keepDropped) : IEnumerator]
    /// 불량 부품을 파지하여 폐기함(RejectBin)으로 투하 (FR-04, FR-07)
    /// </summary>
    public IEnumerator Reject(bool keepDropped = false)
    {
        if (IsBusy) yield break;

        IsBusy = true;
        LastRejectOk = false;
        DefectType defectReason = (currentPartInfo != null) ? currentPartInfo.defectType : DefectType.AngleError;
        Status = $"[폐기] 시작: 불량 부품 처리 (유형: {defectReason})";
        Debug.Log($"[AssemblySequence] {Status}");

        AutoFindReferences();
        ApplyGripperCloseTarget();

        if (clawPart == null)
        {
            Status = "[폐기 실패] 폐기할 부품이 없습니다.";
            Debug.LogWarning($"[AssemblySequence] {Status}");
            IsBusy = false;
            yield break;
        }

        if (rejectBin == null)
        {
            Debug.LogWarning("[AssemblySequence] RejectBin이 지정되지 않아 기본 위치를 생성합니다.");
            GameObject newBin = GameObject.Find("RejectBin");
            if (newBin == null) newBin = new GameObject("RejectBin");
            newBin.transform.position = new Vector3(-0.4f, 0.2f, 0.3f);
            rejectBin = newBin.transform;
        }

        Vector3 pickPos = GetPickPosition();
        Vector3 pickApproach = pickPos + Vector3.up * approachHeight;
        float safeTransitY = pickApproach.y + 0.05f;
        Vector3 safeHighTransit = new Vector3(pickPos.x, safeTransitY, pickPos.z);
        float targetYaw = GetEffectiveYaw();

        // 1. 그리퍼 열기 및 클로 상공 접근
        if (gripper != null) yield return gripper.Open();
        if (ik != null)
        {
            ik.pointDown = true;
            yield return ik.MoveTo(safeHighTransit, null);
            yield return ik.MoveLinear(pickApproach, 0.08f, targetYaw);
            yield return ik.MoveLinear(pickPos, 0.05f, targetYaw);
        }

        // 2. 파지
        if (gripper != null) yield return gripper.Close();
        AttachClawToGripper();
        yield return new WaitForSeconds(0.2f);

        // 3. 폐기함 상공으로 이동
        Vector3 binPos = rejectBin.position;
        Vector3 binAbove = new Vector3(binPos.x, Mathf.Max(binPos.y + 0.35f, safeTransitY), binPos.z);
        Vector3 binDrop = new Vector3(binPos.x, binPos.y + rejectDropHeight, binPos.z);

        if (ik != null)
        {
            yield return ik.MoveLinear(safeHighTransit, 0.08f, targetYaw);
            yield return ik.MoveTo(binAbove, 0f);
            yield return ik.MoveLinear(binDrop, 0.08f, 0f);
        }

        // 4. 폐기함에 부품 놓기
        Status = "[폐기] 부품 투하";
        if (gripper != null) yield return gripper.Open();

        if (!keepDropped && clawPart != null)
        {
            var rb = clawPart.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
            }
            clawPart.SetParent(null, true);
        }

        yield return new WaitForSeconds(0.4f);

        // 5. 복귀
        if (ik != null)
        {
            yield return ik.MoveLinear(binAbove, 0.08f, 0f);
            yield return ik.MoveTo(GetHomeWorldPosition(), 0f);
        }

        LastRejectOk = true;
        Status = $"[폐기 완료] {defectReason} 불량 부품 폐기함 투하 완료";
        Debug.Log($"[AssemblySequence] {Status}");
        IsBusy = false;
    }


    /// <summary>
    /// [UML: + Refeed() : IEnumerator]
    /// 새 부품을 공급받아 재조립 실행
    /// </summary>
    public IEnumerator Refeed()
    {
        if (IsBusy) yield break;

        Status = "[재투입] 새 부품으로 재조립 준비";
        Debug.Log($"[AssemblySequence] {Status}");

        IsBusy = true;

        // 1. 부품만 컨베이어 원위치로 복원 (로봇 코루틴 간섭 방지)
        ResetClawToInitialState();

        // 2. 그리퍼가 닫혀있다면 개방 완료 대기
        if (gripper != null)
        {
            yield return gripper.Open();
        }

        // 3. 로봇이 홈 위치에서 벗어나 있다면 홈으로 완전 이동 대기
        if (ik != null)
        {
            ik.pointDown = true;
            ik.useTargetLookDir = false;
            Vector3 homePos = GetHomeWorldPosition();
            Vector3 curTip = (ik.tip != null) ? ik.tip.position : transform.position;
            if (Vector3.Distance(curTip, homePos) > 0.05f)
            {
                yield return ik.MoveTo(homePos, null);
            }
        }

        yield return new WaitForSeconds(0.2f);
        IsBusy = false;


        yield return Assemble(-1f);
    }

    #endregion
}
