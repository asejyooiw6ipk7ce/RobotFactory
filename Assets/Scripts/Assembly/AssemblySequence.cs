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

    [Header("스테이션 식별 (UML)")]
    [Tooltip("현재 시퀀스가 속한 스테이션 식별자")]
    public StationId stationId = StationId.Station2;

    private bool isStepBusy = false;
    private bool isContinuousBusy = false;
    public bool IsBusy
    {
        get => isStepBusy || isContinuousBusy;
        private set => isStepBusy = value;
    }
    [Tooltip("로봇 번호 (스테이션 2 = 로봇 2)")]
    public int robotId = 2;

    /// <summary>
    /// [SSOT] 로봇 2의 시스템 상태 (대시보드 및 공정 이벤트와 100% 동기화)
    /// </summary>
    public RobotState CurrentRobotState { get; private set; } = RobotState.Idle;
    private string stepDetail = "대기";

    /// <summary>
    /// [계산 프로퍼티] RobotState와 세부 진행 설명을 통합 반환 (에디터 인스펙터 및 디버그용)
    /// </summary>
    public string Status => string.IsNullOrEmpty(stepDetail)
        ? GetRobotStateKorean(CurrentRobotState)
        : $"[{GetRobotStateKorean(CurrentRobotState)}] {stepDetail}";

    /// <summary>
    /// 로봇 상태 및 세부 공정 단계를 단일 진입점으로 통합 갱신합니다.
    /// 상태 변경 시 RobotStatusTracker를 통해 대시보드 및 이벤트 버스로 즉시 브로드캐스트됩니다.
    /// </summary>
    public void SetState(RobotState newState, string detail = null)
    {
        bool stateChanged = CurrentRobotState != newState;
        CurrentRobotState = newState;
        stepDetail = detail;

        if (stateChanged)
        {
            RobotStatusTracker.Set(robotId, newState);
        }
    }

    private static string GetRobotStateKorean(RobotState state)
    {
        return state switch
        {
            RobotState.Idle => "대기",
            RobotState.Moving => "이동",
            RobotState.Working => "작업",
            RobotState.Assembling => "조립",
            RobotState.Error => "오류",
            _ => state.ToString()
        };
    }
    public bool LastAssemblyOk { get; private set; } = false;
    public float LastAngleError { get; private set; } = 0f;
    public int CurrentRetryCount { get; private set; } = 0;
    public float LastElapsedTime { get; private set; } = 0f;

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


    /// <summary>
    /// 로봇과 부품을 완전히 초기 상태로 복원하는 코루틴 (동기식 대기 보장)
    /// </summary>
    public IEnumerator ResetToInitialStateRoutine()
    {
        IsBusy = true;
        SetState(RobotState.Moving, "초기화 진행 중...");

        // 1. 클로 복원
        ResetClawToInitialState();

        // 2. 그리퍼 열기
        if (gripper != null)
        {
            yield return gripper.Open();
        }

        // 3. 로봇 암 초기 자세로 안전 복귀 (Joint Space 기준)
        if (ik != null)
        {
            ik.pointDown = true;
            ik.useTargetLookDir = false;
            yield return ik.ReturnToHomeJoints(0.8f);
        }

        yield return new WaitForSeconds(0.1f);
        IsBusy = false;
        SetState(RobotState.Idle, "초기화 완료");
    }

    /// <summary>
    /// [원클릭 초기화] 언제든 클로를 컨베이어 원위치로 되돌리고 로봇을 단정한 홈 자세로 복원
    /// </summary>
    [ContextMenu("초기 상태로 리셋 (Reset to Initial State)")]
    public void ResetToInitialState()
    {
        StopAllCoroutines();
        isStepBusy = false;
        isContinuousBusy = false;
        StartCoroutine(ResetToInitialStateRoutine());
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
        if (ik != null)
        {
            ik.AutoAssign();
            if (ik.jointSpeed < 100f) ik.jointSpeed = 110f;
        }

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
    /// (순수 좌표 산출 함수 - 클로의 부모나 트랜스폼을 임의로 변경/리셋하지 않음)
    /// </summary>
    public Vector3 GetPickPosition()
    {
        // 클로가 현재 그리퍼에 파지 중이거나 이미 차체/소켓에 결합된 상태라면, 컨베이어 초기 픽업 기준점 반환
        if (IsClawHeldByGripper() || (initialClawRecorded && clawPart != null && clawPart.parent != initialClawParent))
        {
            if (initialClawRecorded)
            {
                float fallbackY = Mathf.Max(minSafeFloorY, initialClawPosition.y + pickHeightOffset);
                return new Vector3(initialClawPosition.x, fallbackY, initialClawPosition.z);
            }
            if (partStopPoint != null) return partStopPoint.position;
        }

        if (clawPart == null)
        {
            if (partStopPoint != null) return partStopPoint.position;
            return new Vector3(-1.168f, 0.59f, -14.81f);
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

        if (autoDetectInsertDir)
        {
            Vector3 candidate = Vector3.zero;
            if (targetRobotBase != null)
            {
                candidate = Vector3.ProjectOnPlane(-targetRobotBase.forward, Vector3.up).normalized;
            }
            else if (currentBase != null)
            {
                candidate = Vector3.ProjectOnPlane(-currentBase.transform.forward, Vector3.up).normalized;
            }
            else if (slotTransform != null && slotTransform.parent != null && slotTransform == baseSocket)
            {
                candidate = Vector3.ProjectOnPlane(-slotTransform.parent.forward, Vector3.up).normalized;
            }

            if (candidate.sqrMagnitude > 0.001f)
            {
                insertDir = candidate;
            }
            else
            {
                insertDir = (customInsertDir.sqrMagnitude > 0.001f)
                    ? Vector3.ProjectOnPlane(customInsertDir, Vector3.up).normalized
                    : Vector3.left;
            }
        }
        else
        {
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
    /// 로봇 중심(ik.GetWorkCenter())과 선분 a -> b의 XZ 평면상 최단 거리를 측정하여
    /// 내부 사각지대(Inner Deadzone)를 관통하는지 여부 판별
    /// </summary>
    private bool DoesSegmentCrossDeadzone(Vector3 a, Vector3 b)
    {
        if (ik == null) return false;

        Vector3 center = ik.GetWorkCenter();
        Vector2 p = new Vector2(center.x, center.z);
        Vector2 pA = new Vector2(a.x, a.z);
        Vector2 pB = new Vector2(b.x, b.z);

        Vector2 ab = pB - pA;
        float abSqr = ab.sqrMagnitude;
        Vector2 closestPoint;
        if (abSqr < 0.0001f)
        {
            closestPoint = pA;
        }
        else
        {
            float t = Mathf.Clamp01(Vector2.Dot(p - pA, ab) / abSqr);
            closestPoint = pA + t * ab;
        }

        float dist = Vector2.Distance(p, closestPoint);
        float threshold = Mathf.Max(ik.innerDeadzoneRadius + 0.05f, 0.28f);
        return dist < threshold;
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
    /// <summary>
    /// SRS FR-08 결합 틀어짐 각도 오차 산출 (난수 생성)
    /// - 0~1 사이 난수가 불량 확률보다 작으면: 5°~20° 사이 균등 난수 (불량)
    /// - 그렇지 않으면: 0°~1.5° 사이 균등 난수 (정상 잔여 오차)
    /// </summary>
    public float CalculateAngleError(float? customDefectProbability = null)
    {
        float prob = customDefectProbability.HasValue ? Mathf.Clamp01(customDefectProbability.Value) : defectProbability;
        bool willBeDefective = (UnityEngine.Random.value < prob);
        float angleError = willBeDefective
            ? UnityEngine.Random.Range(defectiveAngleRange.x, defectiveAngleRange.y)
            : UnityEngine.Random.Range(normalAngleRange.x, normalAngleRange.y);

        if (UnityEngine.Random.value < 0.5f) angleError = -angleError;
        return angleError;
    }

    public IEnumerator Assemble()
    {
        yield return Assemble(-1f, null, null);
    }

    public IEnumerator Assemble(float graspYaw)
    {
        yield return Assemble(graspYaw, null, null);
    }

    /// <summary>
    /// [UML: + Assemble(float graspYaw) : IEnumerator]
    /// 전체 수평 조립 시퀀스 실행 (SRS FR-07 및 FR-08 준수)
    /// - 상위 제어기(ProcessController)가 overrideDefectProbability 또는 forcedAngleError를 주입할 수 있음
    /// - 인자 미지정 시 인스펙터의 DefectProbability 및 자체 계산으로 단독 실행
    /// </summary>
    public IEnumerator Assemble(float graspYaw, float? overrideDefectProbability, float? forcedAngleError = null)
    {
        if (IsBusy) yield break;

        IsBusy = true;
        LastAssemblyOk = false;
        LastAngleError = 0f;
        float sequenceStartTime = Time.time;
        LastElapsedTime = 0f;
        SetState(RobotState.Moving, "0. 조립 시퀀스 준비 중...");
        Debug.Log($"[AssemblySequence] (경과: {Time.time - sequenceStartTime:F2}초) {Status}");

        AutoFindReferences();
        ApplyGripperCloseTarget();

        // [새 조립 시작 안전 복원] 클로가 이전 조립 잔재(차체/소켓 등)에 결합되어 있다면 컨베이어 초기 위치로 복원
        if (initialClawRecorded && clawPart != null && clawPart.parent != initialClawParent)
        {
            ResetClawToInitialState();
        }

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
        SetState(RobotState.Working, "1. 그리퍼 개방 및 클로 감지");
        Debug.Log($"[AssemblySequence] (경과: {Time.time - sequenceStartTime:F2}초) {Status}");
        if (gripper != null) yield return gripper.Open();

        if (clawPart == null)
        {
            Debug.LogError("[AssemblySequence] 조립 대상 부품1(claw)을 찾을 수 없습니다.");
            SetState(RobotState.Error, "부품1 없음");
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
        SetState(RobotState.Moving, $"2. 클로 상공으로 접근 (목표 손목 {targetYaw:F1}°)");
        Debug.Log($"[AssemblySequence] (경과: {Time.time - sequenceStartTime:F2}초) {Status}");
        if (ik != null)
        {
            ik.pointDown = true;
            ik.useTargetLookDir = false;
            Vector3 highSafeTransit = new Vector3(pickApproach.x, 0.95f, pickApproach.z);
            yield return ik.MoveTo(highSafeTransit, null);
            yield return ik.MoveLinearSmooth(pickApproach, 0.40f, targetYaw, false);
        }

        // 3. 파지 하강 (Descend) & 그리퍼 닫기 (Grasp)
        SetState(RobotState.Working, "3. 하강 및 그리퍼 파지 (Grasp)");
        Debug.Log($"[AssemblySequence] (경과: {Time.time - sequenceStartTime:F2}초) {Status}");
        if (ik != null)
        {
            ik.pointDown = true;
            ik.useTargetLookDir = false;
            yield return ik.MoveLinearSmooth(pickPos, descendSpeed, targetYaw, false);
        }

        if (gripper != null)
        {
            yield return gripper.Close();
        }

        AttachClawToGripper();
        yield return new WaitForSeconds(0.2f);

        // 4. 안전 상공 리프팅 (Lift)
        SetState(RobotState.Working, "4. 클로 들어올리기 (Lift)");
        Debug.Log($"[AssemblySequence] (경과: {Time.time - sequenceStartTime:F2}초) {Status}");
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
        SetState(RobotState.Moving, "5. 그리퍼 수평 전환(4.1) 후 shoulder_link 회전(4.2) (소켓 앞 정렬)");
        Debug.Log($"[AssemblySequence] (경과: {Time.time - sequenceStartTime:F2}초) {Status}");

        if (ik != null)
        {
            // [4.1] 컨베이어 상공에서 그리퍼 끝 수평 회전 (차체 소켓 방향 <-X 지향)
            ik.pointDown = false;
            ik.useTargetLookDir = true;
            ik.targetLookDir = insertDir;
            yield return ik.MoveTo(liftGoal, alignWristYaw);
            yield return new WaitForSeconds(0.2f);

            // [4.2] 그리퍼가 소켓 방향(<-X)을 바라본 채 회전 각도에 맞춰 팔을 굽히며 소켓 앞 대기 위치까지 이동 (사각지대 관통 시 원호 스윙)
            Vector3 startPos = (ik.tip != null) ? ik.tip.position : liftGoal;
            if (DoesSegmentCrossDeadzone(startPos, insertApproach))
            {
                yield return ik.MoveTo(insertApproach, alignWristYaw);
            }
            else
            {
                yield return ik.MoveLinearSmooth(insertApproach, alignLinearSpeed, alignWristYaw);
            }
            yield return new WaitForSeconds(0.2f);
        }

        // 6. [4.3] Socket으로 수평 삽입 (Insert, FR-07)
        SetState(RobotState.Assembling, "6. Socket으로 수평 삽입 (Insert)");
        Debug.Log($"[AssemblySequence] (경과: {Time.time - sequenceStartTime:F2}초) {Status}");
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
        SetState(RobotState.Assembling, "7. 스냅 결합 및 결합 오차 적용 (FR-08)");
        Debug.Log($"[AssemblySequence] (경과: {Time.time - sequenceStartTime:F2}초) {Status}");

        // 결합 틀어짐 확률 계산 (FR-08)
        // 결합 틀어짐 각도 결정 (직접 지정된 오차 -> 오버라이드 확률 -> 기본 DefectProbability 순)
        float angleError = forcedAngleError.HasValue
            ? forcedAngleError.Value
            : CalculateAngleError(overrideDefectProbability);

        LastAngleError = angleError;
        bool isAngleDefect = Mathf.Abs(angleError) >= defectiveAngleRange.x;

        // 소켓에 완벽 스냅 결합 및 오차 주입 (내부에서 PartInfo / BaseInfo.defectType 기록)
        SnapClawToSocket(angleError);

        if (currentPartInfo != null)
        {
            currentPartInfo.assemblyAngleError = angleError;
        }

        LastAssemblyOk = true; // ★ 7단계 스냅 결합 완료 즉시 조립 성공 판정 (20초 이내 결합 요건 만족)

        float snapElapsed = Time.time - sequenceStartTime;
        LastElapsedTime = snapElapsed;

        DefectType finalDefect = isAngleDefect ? DefectType.AngleError : DefectType.None;
        Debug.Log($"[AssemblySequence] ⭐ [7단계 결합 완료] 결합 소요시간: {snapElapsed:F2}초 (기준: 20초 이내, 목표: 14~16초) | 틀어짐 각도: {angleError:F2}° (불량 유형: {finalDefect})");

        yield return new WaitForSeconds(0.15f);

        // 8. 완제품 그리퍼 분리 (Release) 및 안전 후퇴
        SetState(RobotState.Working, "8. 완제품 그리퍼 분리 (Release) 및 안전 후퇴");
        Debug.Log($"[AssemblySequence] (경과: {Time.time - sequenceStartTime:F2}초) {Status}");

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
            yield return ik.MoveLinear(insertApproach, 0.18f, alignWristYaw);

            // 상공으로 수직 상승 후 홈 복귀 (차체 간섭 방지)
            Vector3 highApproach = new Vector3(insertApproach.x, GetHomeWorldPosition().y + 0.25f, insertApproach.z);
            yield return ik.MoveLinearSmooth(highApproach, retractLiftSpeed, alignWristYaw);

            // 대기 자세(Home)로 초기 관절 각도로 안전 복귀
            ik.pointDown = true;
            ik.useTargetLookDir = false;
            yield return ik.ReturnToHomeJoints(2.2f);
        }

        LastAssemblyOk = true;
        float totalElapsed = Time.time - sequenceStartTime;
        SetState(RobotState.Idle, $"조립 완료 (결합: {snapElapsed:F2}초, 전체: {totalElapsed:F2}초, 오차: {angleError:F1}°)");
        Debug.Log($"[AssemblySequence] 🏁 [시퀀스 전체 완료] 총 소요: {totalElapsed:F2}초 (결합 시점: {snapElapsed:F2}초) - {Status}");
        IsBusy = false;
    }

    #endregion

    #region 다이어그램 폐기 (Reject) 및 재투입 (Refeed)


    /// <summary>
    /// [UML: + Refeed() : IEnumerator]
    /// 새 부품을 공급받아 재조립 실행
    /// </summary>
    public IEnumerator Refeed()
    {
        if (IsBusy) yield break;

        SetState(RobotState.Idle, "[재투입] 새 부품으로 재조립 준비");
        Debug.Log($"[AssemblySequence] {Status}");

        // 1. 로봇 및 부품을 완벽하게 홈 상태로 동기 대기 리셋
        yield return ResetToInitialStateRoutine();
        yield return new WaitForSeconds(0.2f);

        // 2. 조립 시퀀스 실행
        yield return Assemble(-1f);
    }

    #endregion
}
