using UnityEngine;

public partial class AssemblySequence
{
    #region 클래스 다이어그램 필드 (UML Specification)

    // UML: - PartInfo currentPartInfo
    [SerializeField]
    private PartInfo currentPartInfo;

    // UML: - BaseInfo currentBase
    [SerializeField]
    private BaseInfo currentBase;

    [Header("로봇 컴포넌트 (UML)")]
    // UML: + ArmIK ik
    public ArmIK ik;

    // UML: + GripperController gripper
    public GripperController gripper;

    [Header("위치 기준점 (UML)")]
    [Tooltip("부품2(베이스) 정지 위치 / 결합 기준점")]
    // UML: + Transform basePoint
    public Transform basePoint;

    #endregion

    #region 조립 대상 및 공정 파라미터 (공정 설정)

    [Header("공정 대상 오브젝트")]
    [Tooltip("부품1: 파지할 클로 오브젝트 (비워두면 씬의 claw 자동 검색)")]
    public Transform clawPart;

    [Tooltip("부품2: 조립 대상 로봇 본체 (비워두면 씬의 moto_base 자동 검색)")]
    public Transform targetRobotBase;

    [Tooltip("조립 대상 소켓 (moto_base의 Socket 오브젝트, 비워두면 이름으로 자동 검색)")]
    public Transform baseSocket;

    [Tooltip("씬에 배치된 임시 조립 소켓 목표 (TestTarget, 비워두면 자동 검색)")]
    public Transform testTargetSocket;

    [Tooltip("부품1 정지 위치 (컨베이어 A 스토퍼 지점)")]
    public Transform partStopPoint;

    [Tooltip("완제품을 내려놓을 컨베이어 C 이송 위치 (선택 사항)")]
    public Transform outputConveyorPoint;

    [Tooltip("부품 불량 시 폐기할 폐기함 기준점 (RejectBin, 비워두면 로봇 기준 기본 좌표 자동 적용)")]
    public Transform rejectBin;

    [Header("자세 적응형 파지 (Adaptive Grasp)")]
    [Tooltip("체크 시 컨베이어 벨트 등에서 회전되어 들어오는 클로의 각도를 실시간 감지하여 최적 손목 각도(Yaw) 자동 계산")]
    public bool autoDetectClawYaw = true;

    [Tooltip("자동 감지 각도에 더할 미세 회전 오프셋 (도)")]
    public float clawYawOffset = 0f;

    [Tooltip("수동 지정 시 사용할 손목 각도 (autoDetectClawYaw=false 일 때 기본값)")]
    [Range(0f, 360f)]
    public float defaultPickYaw = 0f;

    [Tooltip("파지 하강 시 추가 높이 오프셋 (m). 음수면 더 깊이 내려감 (기본 -0.025m = 허브 중심 안착)")]
    public float pickHeightOffset = -0.025f;

    [Tooltip("클로 허브를 잡을 목표 손가락 간격 (mm). 기본 48mm (허브 외경에 밀착하여 관통 차단)")]
    [Range(30f, 60f)]
    public float gripWidthMm = 48f;

    [Tooltip("바닥 관통 방지 최소 안전 고도 (m)")]
    public float minSafeFloorY = 0.05f;

    [Header("수평 조립 연출 설정 (FR-07)")]
    [Tooltip("수직 접근/후퇴 높이 (m)")]
    public float approachHeight = 0.12f;

    [Tooltip("2단계 파지 수직 하강 속도 (m/s, 기본 0.18m/s - SmoothStep 가감속 적용)")]
    public float descendSpeed = 0.18f;

    [Tooltip("3단계 클로 수직 들어올리기(리프트) 속도 (m/s, 기본 0.28m/s - SmoothStep 가감속 적용)")]
    public float liftSpeed = 0.28f;

    [Tooltip("소켓 앞 수평 대기 이격 거리 (m, SRS 명세: 0.10m = 클로 선단 기준 소켓과의 유격)")]
    public float insertDistance = 0.10f;

    [Tooltip("그리퍼 끝(AttachPoint)에서 파지된 클로 결합 선단까지의 돌출 길이 보정 (m, 기본 0.13m)")]
    public float clawLengthOffset = 0.13f;

    [Tooltip("5단계 결합 깊이 보정 (m): Socket 기준에서 클로 루트 피벗(0,0,0)까지의 전방 이격 거리 (기본 0.114m = 원본 로봇 결합 깊이 일치)")]
    public float socketMountOffset = 0.114f;

    [Tooltip("4.2 소켓 앞 대기 지점까지 수평 직선 이동 속도 (m/s)")]
    public float alignLinearSpeed = 0.55f;

    [Tooltip("수평 삽입 전진 속도 (m/s)")]
    public float insertSpeed = 0.075f;

    [Tooltip("6단계 분리 후 상공 수직 상승 속도 (m/s, 기본 0.25m/s - SmoothStep 가감속 적용)")]
    public float retractLiftSpeed = 0.35f;

    [Tooltip("체크 시 차체(targetRobotBase)의 회전을 실시간 감지하여 소켓 삽입 방향을 자동 계산 (해제 시 customInsertDir 수동 사용)")]
    public bool autoDetectInsertDir = true;

    [Tooltip("소켓 삽입 접근 방향 (빨간 화살표 방향: (-1, 0, 0) = Vector3.left = 차체 결합면을 향해 진입)")]
    public Vector3 customInsertDir = Vector3.left;

    [Tooltip("4단계 수평 정렬 손목 Roll 각도 (도). 0도 = 클로 수직 정자세 유지")]
    [Range(0f, 360f)]
    public float alignWristYaw = 0f;

    [SerializeField, HideInInspector]
    private bool insertDirMigratedToLeft = false;

    [SerializeField, HideInInspector]
    private bool mountOffsetCalibratedV2 = false;

    [SerializeField, HideInInspector]
    private bool mountOffsetCalibratedV3 = false;

    [Header("단계별 연속 검증 설정")]
    [Tooltip("연속 실행할 목표 단계 (기본값: 3 = 1~3단계 연속 실행. 4단계 성공 시 4로 확장)")]
    [Range(1, 6)]
    public int continuousStepTarget = 3;

    [Header("결합 틀어짐 발생 설정 (FR-08)")]
    [Tooltip("조립 시 결합 불량 발생 확률 (0~1, SRS 기본값: 0.10 = 10%)")]
    [Range(0f, 1f)]
    public float defectProbability = 0.10f;

    /// <summary>
    /// 상위 제어기(ProcessController) 또는 외부에서 동적으로 결함 확률을 주입/조회할 수 있는 프로퍼티 (SRS FR-08: 0~100%, 기본 10%)
    /// ProcessController 없이도 인스펙터나 스크립트에서 단독 테스트 가능
    /// </summary>
    public float DefectProbability
    {
        get => defectProbability;
        set => defectProbability = Mathf.Clamp01(value);
    }

    [Tooltip("불량 발생 시 결합 각도 오차 범위 (도, SRS 명세: 5~20도)")]
    public Vector2 defectiveAngleRange = new Vector2(5f, 20f);

    [Tooltip("정상 결합 시 잔여 각도 오차 범위 (도, SRS 명세: 0~1.5도)")]
    public Vector2 normalAngleRange = new Vector2(0f, 1.5f);

    [Header("조립 실패 및 재시도 설정")]
    [Tooltip("조립 타임아웃 제한 시간 (초, 기본값: 20초)")]
    public float assemblyTimeoutSeconds = 20.0f;

    [Tooltip("타임아웃 시 최대 재시도 횟수 (기본값: 2회)")]
    [Range(0, 5)]
    public int maxRetryCount = 2;

    [Tooltip("1차 조립 시도 실패 확률 (기본값: 0.50 = 50% 실패)")]
    [Range(0f, 1f)]
    public float assemblyFailureProbability = 0.50f;

    [Tooltip("재시도 1회차 실패 확률 (기본값: 0.50 = 50% 실패)")]
    [Range(0f, 1f)]
    public float retry1FailureProbability = 0.50f;

    [Tooltip("재시도 2회차 실패 확률 (기본값: 0.50 = 50% 실패)")]
    [Range(0f, 1f)]
    public float retry2FailureProbability = 0.50f;

    [Tooltip("체크 시 1차 실패에서만 SRS 20초 타임아웃을 대기합니다 (체크 해제 시 1차 실패도 걸림 연출 후 빠르게 재시도). 재시도 회차부터는 대기 없이 즉시 걸림 연출 후 다음 단계로 진행됩니다.")]
    public bool waitFullTimeoutOnFirstFail = false;

    /// <summary>
    /// 상위 제어기(ProcessController) 또는 외부에서 동적으로 조립 실패율을 주입/조회할 수 있는 프로퍼티 (FR-07)
    /// </summary>
    public float AssemblyFailureProbability
    {
        get => assemblyFailureProbability;
        set => assemblyFailureProbability = Mathf.Clamp01(value);
    }

    /// <summary>
    /// 외부에서 타임아웃 제한 시간을 조회하거나 설정할 수 있는 프로퍼티 (기본 20초)
    /// </summary>
    public float AssemblyTimeoutSeconds
    {
        get => assemblyTimeoutSeconds;
        set => assemblyTimeoutSeconds = Mathf.Max(1.0f, value);
    }

    [Header("작업 반경 시각화 및 배치 검증")]
    [Tooltip("로봇 작업 반경 대비 주요 공정 위치(클로, 소켓, 폐기함, 컨베이어) 도달 가능 상태 가이드선 표시")]
    public bool showReachabilityGizmos = true;

    [Header("대기 자세 (Home, 로봇 베이스 기준 로컬 오프셋)")]
    public Vector3 homePosition = new Vector3(0f, 0.40f, 0.35f);
    public float homeJointSpeed = 90f;


    /// <summary>
    /// 로봇 베이스 위치를 기준으로 한 실제 월드 홈 좌표 반환
    /// </summary>
    public Vector3 GetHomeWorldPosition()
    {
        Transform baseTr = (ik != null ? ik.transform : transform);
        return baseTr.position + baseTr.rotation * homePosition;
    }

    /// <summary>
    /// 폐기함(RejectBin) 월드 좌표 반환 (지정되지 않은 경우 로봇 받침대 우측 기본 좌표 적용)
    /// </summary>
    public Vector3 GetRejectBinWorldPosition()
    {
        if (rejectBin != null) return rejectBin.position;
        Transform baseTr = (ik != null ? ik.transform : transform);
        return baseTr.position + baseTr.rotation * new Vector3(0.93f, 0.0f, -0.30f);
    }

    /// <summary>
    /// 폐기함(RejectBin)의 실제 크기/높이를 감지하여 상단 개구부 상공(투하 지점)의 월드 좌표를 정밀 반환
    /// </summary>
    public Vector3 GetRejectBinDropPosition()
    {
        if (rejectBin != null)
        {
            var colliders = rejectBin.GetComponentsInChildren<Collider>();
            if (colliders != null && colliders.Length > 0)
            {
                // 자식 콜라이더 전체의 바운즈를 결합하여 실제 물리적 정중앙과 상단 높이 계산
                Bounds combined = colliders[0].bounds;
                for (int i = 1; i < colliders.Length; i++)
                {
                    combined.Encapsulate(colliders[i].bounds);
                }
                // 상자의 물리적 정중앙 X, Z 및 최상단 높이보다 18cm 높은 상공 좌표
                return new Vector3(combined.center.x, combined.max.y + 0.18f, combined.center.z);
            }

            return new Vector3(rejectBin.position.x, rejectBin.position.y + 0.65f, rejectBin.position.z);
        }

        Transform baseTr = (ik != null ? ik.transform : transform);
        return baseTr.position + baseTr.rotation * new Vector3(0.93f, 0.65f, -0.30f);
    }

    #endregion
}
