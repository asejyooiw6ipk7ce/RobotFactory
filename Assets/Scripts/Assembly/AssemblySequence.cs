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
            yield return ik.ReturnToHomeJoints(1.2f);
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

        // 8. 불량 부품 폐기함(RejectBin) 자동 탐색
        if (rejectBin == null)
        {
            GameObject bin = GameObject.Find("RejectBin");
            if (bin == null) bin = GameObject.Find("reject_bin");
            if (bin == null) bin = GameObject.Find("Bin");
            if (bin != null) rejectBin = bin.transform;
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
        yield return Assemble(-1f, null, null, null, null);
    }

    public IEnumerator Assemble(float graspYaw)
    {
        yield return Assemble(graspYaw, null, null, null, null);
    }

    public IEnumerator Assemble(float graspYaw, float? overrideDefectProbability, float? forcedAngleError = null)
    {
        yield return Assemble(graspYaw, overrideDefectProbability, forcedAngleError, null, null);
    }

    public IEnumerator Assemble(float graspYaw, float? overrideDefectProbability, float? forcedAngleError, float? overrideFailureProbability)
    {
        yield return Assemble(graspYaw, overrideDefectProbability, forcedAngleError, overrideFailureProbability, null);
    }

    /// <summary>
    /// [UML: + Assemble(float graspYaw) : IEnumerator]
    /// 전체 수평 조립 시퀀스 실행 (SRS FR-07 및 FR-08 준수)
    /// - 20초 이내 결합 완료 감시 (WBS 6.6)
    /// - 결합 실패 시 소켓 앞 제자리 후퇴(8cm) 및 미세 정렬 후 최대 maxRetryCount(기본 2회) 재삽입 (텔레포트 100% 제거)
    /// - 4대 확정 분기 시나리오 지원:
    ///   1) 바로 성공 (0회 재시도): 1차 삽입 체결 완료 -> Idle
    ///   2) 재시도 1회 성공: 1차 실패 -> 소켓 앞 후퇴/미세 정렬 -> 2차 체결 완료 -> Idle
    ///   3) 재시도 2회 성공: 1, 2차 실패 -> 소켓 앞 후퇴/미세 정렬 -> 3차 체결 완료 -> Idle
    ///   4) 최종 2회 모두 실패: 3회 연속 실패 -> 소켓 후퇴 -> 폐기함(RejectBin) 이동 -> 클로 물리 투하(Drop) -> 차체 불량(PartLoss) 배출 -> RobotState.Error
    /// </summary>
    public IEnumerator Assemble(float graspYaw = -1f, float? overrideDefectProbability = null, float? forcedAngleError = null, float? overrideFailureProbability = null, int? forcedFailCount = null)
    {
        if (IsBusy) yield break;

        IsBusy = true;
        LastAssemblyOk = false;
        LastAngleError = 0f;
        CurrentRetryCount = 0;
        LastElapsedTime = 0f;

        float overallStartTime = Time.time;
        SetState(RobotState.Moving, "0. 조립 시퀀스 준비 중...");
        Debug.Log($"[AssemblySequence] (경과: 0.00초) {Status}");

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
        Debug.Log($"[AssemblySequence] (경과: {Time.time - overallStartTime:F2}초) {Status}");
        if (gripper != null) yield return gripper.Open();

        if (clawPart == null)
        {
            Debug.LogError("[AssemblySequence] 조립 대상 부품1(claw)을 찾을 수 없습니다.");
            SetState(RobotState.Error, "부품1 없음");
            LastAssemblyOk = false;
            IsBusy = false;
            yield break;
        }

        // 손목 목표 각도 산출 (음수면 자동 감지)
        float targetYaw = (graspYaw >= 0f) ? graspYaw : GetEffectiveYaw();
        Vector3 pickPos = GetPickPosition();
        Vector3 pickApproach = pickPos + Vector3.up * approachHeight;

        // 2. 상공 경유하여 클로 상공으로 접근 (상공 고도 0.95m 경유로 Elbow-Up 확실히 유지)
        SetState(RobotState.Moving, $"2. 클로 상공으로 접근 (목표 손목 {targetYaw:F1}°)");
        Debug.Log($"[AssemblySequence] (경과: {Time.time - overallStartTime:F2}초) {Status}");
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
        Debug.Log($"[AssemblySequence] (경과: {Time.time - overallStartTime:F2}초) {Status}");
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
        Debug.Log($"[AssemblySequence] (경과: {Time.time - overallStartTime:F2}초) {Status}");
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
        Debug.Log($"[AssemblySequence] (경과: {Time.time - overallStartTime:F2}초) {Status}");

        if (ik != null)
        {
            // [4.1] 컨베이어 상공에서 그리퍼 끝 수평 회전 (차체 소켓 방향 지향)
            ik.pointDown = false;
            ik.useTargetLookDir = true;
            ik.targetLookDir = insertDir;
            yield return ik.MoveTo(liftGoal, alignWristYaw);
            yield return new WaitForSeconds(0.2f);

            // [4.2] 소켓 앞 대기 위치(insertApproach)까지 이동
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

        // =====================================================================
        // [6~8단계: 소켓 결합 및 제자리 재시도 루프 (FR-07 WBS 6.6)]
        // =====================================================================
        Vector3 tipInsertGoal = GetTipInsertGoal(slotCenter, insertDir);
        int totalMaxAttempts = Mathf.Max(1, maxRetryCount + 1);
        bool finalSuccess = false;

        for (int attempt = 0; attempt < totalMaxAttempts; attempt++)
        {
            CurrentRetryCount = attempt;
            float attemptStartTime = Time.time;
            string retryPrefix = attempt > 0 ? $"[재시도 {attempt}/{maxRetryCount}] " : "";

            // 1. 이번 시도의 실패 여부 사전 판정 (기본 50% 확률 또는 에디터 강제 설정)
            bool failThisAttempt;
            if (forcedFailCount.HasValue)
            {
                failThisAttempt = (attempt < forcedFailCount.Value);
            }
            else
            {
                float failProb = GetFailureProbabilityForAttempt(attempt, overrideFailureProbability);
                failThisAttempt = (failProb > 0f && UnityEngine.Random.value < failProb);
            }

            // 2. 재시도 회차(attempt > 0)일 경우: 소켓 앞 8cm 제자리 후퇴 & 손목 미세 정렬 먼저 수행
            if (attempt > 0)
            {
                yield return RetryDisengageAndRealignRoutine(insertApproach, insertDir, alignWristYaw, attempt, overallStartTime);
            }

            // 3. 소켓 삽입 전진 실행
            if (failThisAttempt)
            {
                // [걸림 연출 강화] 소켓 끝까지 들어가지 못하고 60% 깊이에서 턱 걸린 후 덜컹거리는 저항 연출 (약 1.2초)
                yield return ExecuteJammingInsertionRoutine(insertApproach, tipInsertGoal, insertDir, alignWristYaw, attempt, totalMaxAttempts, overallStartTime);

                // [대기 시간 처리]
                // 1차 시도에서만 사용자가 waitFullTimeoutOnFirstFail을 켰을 때 20초 대기
                // 재시도 회차(attempt > 0)부터는 20초 대기 없이 걸림 연출 후 즉시 실패 판정!
                if (attempt == 0 && waitFullTimeoutOnFirstFail)
                {
                    Debug.LogWarning($"[AssemblySequence] ⏱️ {retryPrefix}1차 조립 실패 - 20초 타임아웃 감시 중...");
                    SetState(RobotState.Assembling, $"{retryPrefix}Socket 결합 저항/걸림 발생 (타임아웃 감시 중...)");
                    while ((Time.time - attemptStartTime) < assemblyTimeoutSeconds)
                    {
                        yield return null;
                    }
                    float timeoutElapsed = Time.time - attemptStartTime;
                    LastElapsedTime = timeoutElapsed;
                }
                else
                {
                    LastElapsedTime = Time.time - attemptStartTime;
                    Debug.LogWarning($"[AssemblySequence] ⏱️ {retryPrefix}조립 저항/걸림 확인 (경과: {LastElapsedTime:F2}초) - 20초 대기 없이 즉시 다음 단계 연계");
                }

                if (attempt < maxRetryCount)
                {
                    Debug.LogWarning($"[AssemblySequence] 🔄 {retryPrefix}조립 저항 확인. 소켓 앞에서 제자리 미세 정렬 재시도({attempt + 1}/{maxRetryCount})로 진행합니다.");
                    continue; // 다음 재시도 루프로 즉시 진행
                }
                else
                {
                    // 최대 재시도(2회) 모두 소진 -> 경우 4 진입
                    finalSuccess = false;
                    break;
                }
            }
            else
            {
                // [정상 삽입] 목표 위치까지 100% 매끄럽게 정상 진입
                SetState(RobotState.Assembling, $"{retryPrefix}6. Socket으로 수평 삽입 (Insert)");
                Debug.Log($"[AssemblySequence] (경과: {Time.time - overallStartTime:F2}초) {Status}");
                if (ik != null)
                {
                    ik.pointDown = false;
                    ik.useTargetLookDir = true;
                    ik.targetLookDir = insertDir;
                    yield return ik.MoveLinearSmooth(tipInsertGoal, insertSpeed, alignWristYaw);
                }
                yield return new WaitForSeconds(0.15f);

                // 조립 결합 체결 성공 (7~8단계 실행)
                yield return FinishSnapAndReleaseRoutine(slotCenter, insertApproach, insertDir, alignWristYaw, overrideDefectProbability, forcedAngleError, attempt, overallStartTime);
                finalSuccess = true;
                break;
            }
        }

        if (!finalSuccess)
        {
            // 경우 4: 최종 2회 모두 실패 -> 폐기함 투하 및 차체 불량(PartLoss) 배출 -> RobotState.Error
            yield return RejectClawAndHandleDefectRoutine(insertApproach, insertDir, alignWristYaw);
        }

        IsBusy = false;
    }

    /// <summary>
    /// 시도 인덱스(attempt: 0=1차, 1=재시도1, 2=재시도2)에 따른 실패 확률 반환 (기본값 각 50%)
    /// </summary>
    public float GetFailureProbabilityForAttempt(int attempt, float? overrideFailureProbability)
    {
        if (overrideFailureProbability.HasValue) return overrideFailureProbability.Value;
        switch (attempt)
        {
            case 0: return assemblyFailureProbability; // 기본 0.50 (50%)
            case 1: return retry1FailureProbability;   // 기본 0.50 (50%)
            case 2: return retry2FailureProbability;   // 기본 0.50 (50%)
            default: return 0.50f;
        }
    }

    /// <summary>
    /// [공차 불일치 걸림(Jamming) 연출]
    /// 소켓 입구 부근(약 60% 깊이)에서 '턱!' 걸려 멈춘 후, 억지로 밀어 넣으려는 전후 덜컹거림 및 손목 비틀림 저항 모션 연출 (약 1.2초 소요)
    /// </summary>
    private IEnumerator ExecuteJammingInsertionRoutine(Vector3 insertApproach, Vector3 tipInsertGoal, Vector3 insertDir, float alignWristYaw, int attempt, int totalMaxAttempts, float overallStartTime)
    {
        string retryPrefix = attempt > 0 ? $"[재시도 {attempt}/{maxRetryCount}] " : "";
        SetState(RobotState.Assembling, $"{retryPrefix}6. Socket으로 수평 삽입 시도 중...");

        if (ik != null)
        {
            ik.pointDown = false;
            ik.useTargetLookDir = true;
            ik.targetLookDir = insertDir;

            // 1. 소켓 입구 약 60% 지점까지만 진입 후 '턱!' 하고 걸림
            Vector3 jammedPos = Vector3.Lerp(insertApproach, tipInsertGoal, 0.60f);
            yield return ik.MoveLinearSmooth(jammedPos, insertSpeed * 1.3f, alignWristYaw, false);

            SetState(RobotState.Assembling, $"{retryPrefix}6. 소켓 입구 공차 걸림 발생! (Stuck/Resistance)");
            Debug.LogWarning($"[AssemblySequence] ⚠️ {retryPrefix}[공차 걸림 발생] 소켓 입구에서 클로가 걸려 진입 불가! 억지 진입 저항 연출 (시도 {attempt + 1}/{totalMaxAttempts})");

            // 2. 억지 진입 시도: 앞으로 1.2cm 억지로 쿵 밀어보지만 걸려 튕겨 나옴 (밀기 0.12s -> 되돌림 0.12s)
            yield return ik.MoveLinear(jammedPos + insertDir * 0.012f, 0.12f, alignWristYaw);
            yield return ik.MoveLinear(jammedPos, 0.12f, alignWristYaw);

            // 3. 손목 좌우 비틀기 저항 (Wiggle Resistance): 억지로 맞추려고 삐걱삐걱 흔들림
            yield return ik.RotateWristLive(alignWristYaw + 5.0f, 0.14f);
            yield return ik.RotateWristLive(alignWristYaw - 5.0f, 0.14f);
            yield return ik.RotateWristLive(alignWristYaw, 0.12f);

            // 4. 상하 미세 덜컹거림 (0.15s)
            yield return ik.MoveLinear(jammedPos + Vector3.up * 0.008f, 0.10f, alignWristYaw);
            yield return ik.MoveLinear(jammedPos, 0.10f, alignWristYaw);
        }

        yield return new WaitForSeconds(0.1f);
    }

    /// <summary>
    /// [소켓 앞 제자리 후퇴 및 미세 정렬 루틴 (FR-07 WBS 6.6)]
    /// 걸린 위치에서 소켓 앞 8cm로 안전 후퇴 후, 손목 미세 정렬(Re-align) 연출
    /// </summary>
    private IEnumerator RetryDisengageAndRealignRoutine(Vector3 insertApproach, Vector3 insertDir, float alignWristYaw, int attempt, float overallStartTime)
    {
        string retryPrefix = $"[재시도 {attempt}/{maxRetryCount}] ";
        Debug.LogWarning($"[AssemblySequence] 🔄 {retryPrefix}소켓 앞 8cm 제자리 후퇴 및 손목 미세 정렬 시작 (경과: {Time.time - overallStartTime:F2}초)");
        SetState(RobotState.Moving, $"{retryPrefix}소켓 앞 8cm 후퇴 (Disengage)");

        if (ik != null)
        {
            ik.pointDown = false;
            ik.useTargetLookDir = true;
            ik.targetLookDir = insertDir;

            // 1. 소켓 앞 8cm 후퇴
            yield return ik.MoveLinear(insertApproach, 0.25f, alignWristYaw);
            yield return new WaitForSeconds(0.1f);

            // 2. 손목 미세 정렬 연출
            SetState(RobotState.Working, $"{retryPrefix}손목 미세 정렬 (Re-align)");
            yield return ik.RotateWristLive(alignWristYaw + 3.0f, 0.2f);
            yield return ik.RotateWristLive(alignWristYaw, 0.2f);
            yield return new WaitForSeconds(0.1f);
        }
    }

    /// <summary>
    /// [최종 2회 재시도 모두 실패 시 폐기 및 불량 배출 루틴 (경우 4)]
    /// 소켓 안전 후퇴 -> RejectBin 상단 개구부 상공 선회 이동 -> 클로 물리 투하(Gravity Drop) -> 차체 PartLoss 배출 -> 로봇 홈 복귀 -> RobotState.Error
    /// </summary>
    private IEnumerator RejectClawAndHandleDefectRoutine(Vector3 insertApproach, Vector3 insertDir, float alignWristYaw)
    {
        Debug.LogError($"[AssemblySequence] ❌ [조립 최종 실패] 최대 재시도({maxRetryCount}회)를 모두 초과하였습니다. 클로 폐기함 투하 및 차체 불량 배출을 진행합니다.");
        SetState(RobotState.Moving, "최종 실패 - 소켓 안전 후퇴 및 폐기함 이동");

        AutoFindReferences(); // RejectBin 자동 탐색 보장

        // 1. 소켓에서 수평 안전 후퇴
        if (ik != null)
        {
            ik.pointDown = false;
            ik.useTargetLookDir = true;
            ik.targetLookDir = insertDir;
            yield return ik.MoveLinear(insertApproach, 0.25f, alignWristYaw);

            // 2. 상공으로 안전 상승 (차체 간섭 방지)
            Vector3 highApproach = new Vector3(insertApproach.x, GetHomeWorldPosition().y + 0.25f, insertApproach.z);
            yield return ik.MoveLinearSmooth(highApproach, retractLiftSpeed, alignWristYaw);

            // 3. 폐기함(RejectBin) 상단 개구부 상공으로 선회 이동
            Vector3 dropPos = GetRejectBinDropPosition();
            SetState(RobotState.Moving, $"폐기함 상공({dropPos.x:F2}, {dropPos.y:F2}, {dropPos.z:F2})으로 선회 이동 중...");
            ik.pointDown = true;
            ik.useTargetLookDir = false;
            yield return ik.MoveTo(dropPos, null);
        }

        yield return new WaitForSeconds(0.2f);

        // 4. 그리퍼 개방 및 클로 물리 낙하 (Gravity Drop)
        SetState(RobotState.Working, "폐기함으로 클로 물리 투하 (Gravity Drop)");
        if (gripper != null)
        {
            yield return gripper.Open();
        }

        if (clawPart != null)
        {
            clawPart.SetParent(null, true);
            var rb = clawPart.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.linearVelocity = Vector3.down * 0.4f; // 자연스러운 하향 낙하 초속
            }
        }

        yield return new WaitForSeconds(0.6f);

        // 5. 차체(베이스) 조립 불량(PartLoss) 기록 및 배출 이벤트 발행
        if (currentBase != null)
        {
            currentBase.defectType = DefectType.PartLoss;
        }
        else if (targetRobotBase != null)
        {
            var baseComp = targetRobotBase.GetComponent<PartBase>();
            if (baseComp != null) baseComp.defectType = DefectType.PartLoss;
        }

        string baseId = (currentBase != null && !string.IsNullOrEmpty(currentBase.id))
            ? currentBase.id
            : (targetRobotBase != null ? targetRobotBase.name : "BASE_MOTO");

        InspectEventBus.PublishExit(new PartExitEvent(baseId, ExitReason.Defective, DefectType.PartLoss, Time.time));
        Debug.LogWarning($"[AssemblySequence] 📢 차체 조립 불량(PartLoss) 배출 이벤트 발행: PartId={baseId}, ExitReason=Defective, DefectType=PartLoss");

        // 6. 로봇 홈 복귀
        if (ik != null)
        {
            SetState(RobotState.Moving, "로봇 안전 홈 복귀 중...");
            ik.pointDown = true;
            ik.useTargetLookDir = false;
            yield return ik.ReturnToHomeJoints(2.2f);
        }

        // 7. 로봇 관리자 점검 경보 (RobotState.Error)
        LastAssemblyOk = false;
        SetState(RobotState.Error, $"조립 오류 ({maxRetryCount}회 재시도 실패, 클로 폐기 및 베이스 불량 배출)");
        Debug.LogError($"[AssemblySequence] 🚨 관리자 점검 경보: {Status}");
    }

    /// <summary>
    /// 결합 성공 시 7단계 스냅 체결 및 8단계 그리퍼 분리, 안전 후퇴 및 홈 복귀 루틴
    /// </summary>
    private IEnumerator FinishSnapAndReleaseRoutine(Vector3 slotCenter, Vector3 insertApproach, Vector3 insertDir, float alignWristYaw, float? overrideDefectProbability, float? forcedAngleError, int attemptIndex, float overallStartTime)
    {
        string retryPrefix = attemptIndex > 0 ? $"[재시도 {attemptIndex}/{maxRetryCount}] " : "";

        // 7. 스냅 고정 및 결합 틀어짐 적용 (FR-07, FR-08)
        SetState(RobotState.Assembling, $"{retryPrefix}7. 스냅 결합 및 결합 오차 적용 (FR-08)");
        Debug.Log($"[AssemblySequence] (경과: {Time.time - overallStartTime:F2}초) {Status}");

        float angleError = forcedAngleError.HasValue
            ? forcedAngleError.Value
            : CalculateAngleError(overrideDefectProbability);

        LastAngleError = angleError;
        bool isAngleDefect = Mathf.Abs(angleError) >= defectiveAngleRange.x;

        // 소켓에 완벽 스냅 결합 및 오차 주입
        SnapClawToSocket(angleError);

        if (currentPartInfo != null)
        {
            currentPartInfo.assemblyAngleError = angleError;
        }

        LastAssemblyOk = true;

        float snapElapsed = Time.time - overallStartTime;
        LastElapsedTime = snapElapsed;

        DefectType finalDefect = isAngleDefect ? DefectType.AngleError : DefectType.None;
        Debug.Log($"[AssemblySequence] ⭐ {retryPrefix}[7단계 결합 완료] 결합 소요시간: {snapElapsed:F2}초 (기준: {assemblyTimeoutSeconds:F0}초 이내, 목표: 14~16초) | 틀어짐 각도: {angleError:F2}° (불량 유형: {finalDefect})");

        yield return new WaitForSeconds(0.15f);

        // 8. 완제품 그리퍼 분리 (Release) 및 안전 후퇴
        SetState(RobotState.Working, $"{retryPrefix}8. 완제품 그리퍼 분리 (Release) 및 안전 후퇴");
        Debug.Log($"[AssemblySequence] (경과: {Time.time - overallStartTime:F2}초) {Status}");

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
        float totalElapsed = Time.time - overallStartTime;
        string retrySuccessTag = attemptIndex > 0 ? $" [재시도 {attemptIndex}회 성공]" : "";
        SetState(RobotState.Idle, $"조립 완료{retrySuccessTag} (결합: {snapElapsed:F2}초, 전체: {totalElapsed:F2}초, 오차: {angleError:F1}°)");
        Debug.Log($"[AssemblySequence] 🏁 {retryPrefix}[시퀀스 전체 완료] 총 소요: {totalElapsed:F2}초 (결합 시점: {snapElapsed:F2}초) - {Status}");
    }

        /// <summary>
    /// 이전 시도 실패 시 하위 호환용 리셋 루틴
    /// </summary>
    public IEnumerator ResetAttemptRoutine()
    {
        ResetClawToInitialState();
        if (gripper != null) yield return gripper.Open();
        if (ik != null)
        {
            ik.pointDown = true;
            ik.useTargetLookDir = false;
            yield return ik.ReturnToHomeJoints(1.2f);
        }
        yield return new WaitForSeconds(0.2f);
    }

    /// <summary>
    /// 하위 호환용 복구 코루틴
    /// </summary>
    public IEnumerator RecoverFailedAttemptRoutine()
    {
        ResetClawToInitialState();
        if (gripper != null) yield return gripper.Open();
        if (ik != null)
        {
            ik.pointDown = true;
            ik.useTargetLookDir = false;
            yield return ik.ReturnToHomeJoints(1.5f);
        }
        yield return new WaitForSeconds(0.1f);
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
