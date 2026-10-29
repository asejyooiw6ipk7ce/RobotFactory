using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ArmIK : MonoBehaviour
{
    #region Inspector

    [Header("관절 (비워두면 이름으로 자동 검색)")]
    public ArticulationBody[] joints = new ArticulationBody[0];
    static readonly string[] JointNames =
        { "shoulder_link", "arm_link", "elbow_link", "forearm_link", "wrist_link", "hand_link" };
    static readonly string[] URJointNames =
        { "shoulder_link", "upper_arm_link", "forearm_link", "wrist_1_link", "wrist_2_link", "wrist_3_link" };
    public Transform tip;                       // AttachPoint
    public ArticulationBody closingFinger;      // right_gripper or right_finger

    [Header("관절 회전 방향 (처음 한 번 자동 확인 후 저장)")]
    public bool signsCalibrated = false;
    public float[] jointSigns = new float[0];

    [Header("자세")]
    [Tooltip("그리퍼가 항상 수직 아래를 보도록")]
    public bool pointDown = true;
    [Tooltip("pointDown이 꺼졌을 때 그리퍼 끝이 바라볼 목표 방향 (기본: Vector3.forward = 수평 전방 정렬)")]
    public Vector3 targetLookDir = Vector3.forward;
    [Tooltip("pointDown이 꺼졌을 때 tip이 targetLookDir 방향을 수평으로 향하도록 정밀 제어 (기본 false: 기존 동작 100% 보존)")]
    public bool useTargetLookDir = false;
    [Tooltip("자세 오차 1 rad를 위치 오차 몇 m로 볼지")]
    public float orientationWeight = 0.2f;

    [Header("이동")]
    public float jointSpeed = 60f;         // 관절 이동 속도 (도/초)
    public float minMoveTime = 0.3f;
    public float linearStep = 0.01f;        // 직선 경로를 몇 m 간격으로 나눌지
    public int correctionPasses = 3;        // 도착 후 처짐 보정 횟수
    public float positionTolerance = 0.001f;

    [Header("테스트: IKTarget을 끌면 따라가기 (IKTarget의 Rotation Y = 손목 각도)")]
    public Transform testTarget;
    public bool followTestTarget = false;

    public bool Ready { get; private set; }
    public bool IsMoving { get; private set; }
    public bool LastSolveOk { get; private set; } = true;
    public float PositionError { get; private set; }   // 마지막 이동 후 실제 위치 오차 (m)
    public float AngleError { get; private set; }      // 마지막 이동 후 수직 오차 (도)
    public float YawError { get; private set; }        // 마지막 이동 후 손목 각도 오차 (도)

    #endregion

    #region 계산용 모델 변수

    private int n; //관절 개수 (n)
    private Transform baseT; //계산의 출발점 base link(움직이지 않는 받침)
    private Quaternion[] relRot0; //시작할때 각 링크가 부모 링크에 대해 돌아가있던 방향
    private Vector3[] pivotInParent,//각 관절의 회전 중심 위치 (부모 링크 기준)
        anchorPos,//각 관절의 회전 중심 위치 (자기 링크 기준)
        axisLocal;//각 관절이 도는 축(자기 링크 기준)
    private float[] theta0, //시작할때 각 관절 각도(도) 계산시 "여기서 몇도 더 돌았냐"
        lower, //각 관절이 돌수있는 최소 각도
        upper; //관절이 돌수있는 최대 각도
    private bool[] limited;//각도 범위 제한이 있는 관절인지
    private Vector3 tipLocal, //그리퍼 끝(AttachPoint)의 위치 (hand_link 기준)
        dirLocal, //그리퍼가 향하는 방향. (hand_link 기준) 아래를 보는지 확인할때 사용
        closeLocal;//손가락이 닫히는 축 (hand_link 기준) 손목 각도를 확인할 때 사용
    private float? activeYaw; //이번 이동의 목표 손목 각도 (null 이면 손목 각도가 자유). 보정때도 이 값을 사용.
    //float? 에서 물음표는 "값이 없음(null)"도 담을 수 있는 float.

    //이동 관리용
    private Vector3 lastFollowPos; //테스트시 마지막으로 따라간 IKTarget의 위치
    private float lastFollowYaw; //마지막으로 따라간 IKTarget의 회전 Y
    private bool firstMove = true; //첫 이동인지. 첫 이동때만 모델 예측과 실제위치를 비교해서 회전 방향 오류를 경고

    #endregion

    #region 1. 로봇 구조를 읽고, 계산용 모델 만들기

    //시작순서 관절 찾기 -> 계산용 모델 만들기 -> (처음이면) 회전 방향 확인
    //끝나면 Ready = true
    IEnumerator Start()
    {
        AutoAssign();
        BuildModel();

        if (n >= 6)
        {
            // 플레이 시작 시 바닥으로 주저앉지 않고 에디터의 꼿꼿한 초기 자세를 탄탄하게 유지
            HoldInitialPose();

            if (!signsCalibrated || jointSigns == null || jointSigns.Length != n)
                yield return CalibrateSigns();

            HoldInitialPose();
            Ready = true;
        }
        else
        {
            Debug.LogError("[ArmIK] 관절(joints) 설정이 불완전하여 기구학을 초기화할 수 없습니다.");
        }
    }

    /// <summary>
    /// 플레이 시작 시 바닥으로 주저앉지 않고 에디터의 꼿꼿한 초기 자세를 탄탄하게 유지
    /// </summary>
    public void HoldInitialPose()
    {
        if (joints == null || theta0 == null || theta0.Length < n) return;
        for (int j = 0; j < n; j++)
        {
            if (joints[j] == null) continue;
            var d = joints[j].xDrive;
            if (d.stiffness < 5000f) d.stiffness = 50000f;
            if (d.damping < 500f) d.damping = 5000f;
            if (d.forceLimit < 500f) d.forceLimit = 1000f;
            d.target = theta0[j];
            joints[j].xDrive = d;
        }
    }

    //관절, 그리퍼 끝(attachPoint), 오른쪽 손가락을 이름으로 찾아서 넣기
    //인스펙터에 값이 있으면 그대로 사용
    public void AutoAssign()
    {
        var bodies = GetComponentsInChildren<ArticulationBody>();
        if (joints == null || joints.Length == 0 || joints[0] == null)
        {
            // UR 로봇인지 두산 로봇인지 판별
            bool isUR = false;
            foreach (var b in bodies)
            {
                if (b.name == "upper_arm_link") { isUR = true; break; }
            }

            string[] namesToSearch = isUR ? URJointNames : JointNames;
            joints = new ArticulationBody[namesToSearch.Length];
            for (int i = 0; i < namesToSearch.Length; i++)
                foreach (var b in bodies)
                    if (b.name == namesToSearch[i]) joints[i] = b;
        }
        if (closingFinger == null)
            foreach (var b in bodies)
                if (b.name == "right_gripper" || b.name == "right_finger" || b.name == "robotiq_85_right_knuckle_link" || b.name == "robotiq_hande_right_finger" || b.name.Contains("right_finger")) closingFinger = b;
        if (tip == null)
        {
            foreach (var t in GetComponentsInChildren<Transform>())
                if (t.name == "AttachPoint") { tip = t; break; }
            if (tip == null)
            {
                foreach (var t in GetComponentsInChildren<Transform>())
                    if (t.name == "tool0" || t.name == "hand_link") { tip = t; break; }
            }
        }

        for (int i = 0; i < joints.Length; i++)
            if (joints[i] == null) Debug.LogError($"[ArmIK] {i}번 관절을 찾지 못했습니다. Inspector에서 직접 넣어 주세요.");
        if (tip == null) Debug.LogError("[ArmIK] AttachPoint를 찾지 못했습니다.");
        if (closingFinger == null) Debug.LogError("[ArmIK] right_gripper/right_finger를 찾지 못했습니다.");
    }

    // =========================================================================
    // [BuildModel] 로봇의 물리적 구조(링크/관절 정보)를 읽어와 가상의 기구학 모델 구축
    //  - Unity Transform(월드 좌표)은 로봇이 움직일 때마다 실시간으로 변하므로,
    //    IK/FK 계산을 위해 "부모 링크 기준의 불변하는 로컬 좌표/회전축/초기각도"를 미리 추출하여 캐싱합니다.
    // =========================================================================
    public void BuildModel()
    {
        if (joints == null || joints.Length < 6 || joints[0] == null)
        {
            AutoAssign();
        }

        if (joints == null || joints.Length == 0 || joints[0] == null)
        {
            Debug.LogError("[ArmIK] 관절(joints)이 설정되지 않아 모델을 생성할 수 없습니다.");
            return;
        }

        // 1. 관절 개수(n) 파악 및 배열 크기 할당 (예: 6축 로봇이면 n = 6)
        n = joints.Length;

        // 2. 기구학 계산의 기준점이 될 베이스 링크(움직이지 않는 받침대)의 Transform 저장
        //    (첫 번째 관절의 부모가 base_link가 됩니다)
        baseT = joints[0].transform.parent;

        // 3. 기구학 모델 계산에 필요한 각종 정보들을 담을 배열들을 관절 개수(n)만큼 생성
        relRot0 = new Quaternion[n];        // 초기 자세에서 각 링크가 부모 링크에 대해 기울어져 있는 기본 회전값
        pivotInParent = new Vector3[n];     // 각 관절의 회전 중심점(피벗) 위치 (부모 링크의 로컬 좌표계 기준)
        anchorPos = new Vector3[n];         // 각 관절의 회전 중심점 위치 (자신 링크의 로컬 좌표계 기준)
        axisLocal = new Vector3[n];         // 각 관절이 회전하는 축 단위 벡터 (자신 링크의 로컬 좌표계 기준)
        theta0 = new float[n];              // 모델 생성 시점의 각 관절 초기 각도 (단위: 도/Degree)
        lower = new float[n];               // 관절이 회전 가능한 최소 각도 한계
        upper = new float[n];               // 관절이 회전 가능한 최대 각도 한계
        limited = new bool[n];              // 해당 관절에 회전 각도 제한이 걸려 있는지 여부

        // 4. 각 관절(링크)을 순회하며 불변하는 기구학 파라미터를 계산하고 저장
        for (int j = 0; j < n; j++)
        {
            // 현재 관절 링크(c: Child)와 부모 링크(p: Parent)의 Transform 참조
            Transform c = joints[j].transform, p = c.parent;

            // 부모의 월드 회전을 역회전(Inverse)시켜, 부모 기준의 로컬 좌표계로 변환할 때 사용할 쿼터니언
            Quaternion inv = Quaternion.Inverse(p.rotation);

            // [부모 기준 초기 상대 회전]: 부모 회전의 역을 자식 월드 회전에 곱해 '부모 기준 자식의 초기 회전' 계산
            relRot0[j] = inv * c.rotation;

            // [자신 기준 회전 중심]: ArticulationBody에 설정된 자신 링크 원점 기준 관절 피벗(Anchor) 위치
            anchorPos[j] = joints[j].anchorPosition;

            // [자신 기준 회전축]: ArticulationBody의 회전축은 로컬 X축(Vector3.right)을 기준으로 anchorRotation만큼 회전된 방향
            axisLocal[j] = joints[j].anchorRotation * Vector3.right;

            // [부모 기준 회전 중심 위치]: 부모 링크 원점에서 바라본 해당 관절 회전 피벗의 위치
            //  = (부모 기준 자식 링크 원점 위치) + (부모 기준 자식 회전 * 자식 링크 원점에서 앵커까지의 오프셋)
            pivotInParent[j] = inv * (c.position - p.position) + relRot0[j] * anchorPos[j];

            // [현재 관절 각도]: ArticulationBody가 현재 물리적으로 위치한 관절 각도 (라디안 -> 디그리 변환하여 기준점으로 저장)
            theta0[j] = joints[j].jointPosition[0] * Mathf.Rad2Deg;

            // 유니티 물리 엔진(PhysX)의 관절 구동 모터 설정(xDrive) 정보 가져오기
            var d = joints[j].xDrive;

            // 회전 운동(Twist)에 가동 범위 제한(LimitedMotion)이 걸려 있는지 확인
            limited[j] = joints[j].twistLock == ArticulationDofLock.LimitedMotion;

            // 관절이 안전하게 움직일 수 있는 최소/최대 회전 한계 각도(도 단위) 저장
            lower[j] = d.lowerLimit;
            upper[j] = d.upperLimit;
        }

        // 5. 그리퍼 끝(End-Effector / AttachPoint) 및 손목 관련 기준 정보 계산
        // 마지막 링크(hand_link, 엔드 이펙터 베이스)의 Transform
        Transform hand = joints[n - 1].transform;

        // 손(hand_link)의 월드 회전을 역회전(Inverse)시켜 손 로컬 좌표계로 변환하기 위한 쿼터니언
        Quaternion handInv = Quaternion.Inverse(hand.rotation);

        // [손 기준 그리퍼 끝 위치]: 월드 상의 그리퍼 끝(tip) 위치를 손(hand_link) 로컬 좌표계 기준으로 변환하여 저장
        tipLocal = handInv * (tip.position - hand.position);

        // [손 기준 그리퍼 지향 벡터]: 손목 관절의 회전 피벗에서 그리퍼 끝을 향하는 로컬 방향 벡터 (그리퍼가 수직 아래를 보는지 확인할 때 기준 벡터)
        dirLocal = (tipLocal - anchorPos[n - 1]).normalized;

        // [손 기준 손가락 닫힘 축]: 그리퍼 손가락이 미끄러지며 닫히는 실제 물리적 축 벡터를 손(hand_link) 로컬 좌표계 기준으로 변환하여 저장 (손목 회전각 Yaw 제어 기준)
        closeLocal = handInv * ActualCloseAxis();
    }

    // =========================================================================
    // [CalibrateSigns] 관절 회전 방향(부호, +1 또는 -1) 자동 캘리브레이션
    //  - 유니티 ArticulationBody 모터에 양수(+) 각도를 주었을 때,
    //    우리가 수학적으로 모델링한 회전축(axisLocal) 기준 시계/반시계 방향과 일치하는지(+1), 반대인지(-1) 판별합니다.
    //  - 시작할 때 관절을 4도씩 살짝 움직여보고, 실제 물리적 회전과 수학적 예측 모델을 비교하여 jointSigns에 저장합니다.
    // =========================================================================
    IEnumerator CalibrateSigns()
    {
        // 1. 각 관절의 회전 부호(+1 또는 -1)를 저장할 배열 생성
        jointSigns = new float[n];

        // 2. 모든 관절을 안전한 방향으로 4도씩 살짝 회전하도록 물리 모터(xDrive)에 목표 각도 지령
        for (int j = 0; j < n; j++)
        {
            // 관절 가동 범위의 중간값 계산
            float mid = (lower[j] + upper[j]) * 0.5f;

            // 현재 각도가 중간값 이하이면 +4도, 중간값을 넘으면 최대 한계에 닿지 않도록 -4도로 목표 각도 설정
            SetTarget(j, theta0[j] + (theta0[j] <= mid ? 4f : -4f));
        }

        // 3. 물리 엔진(PhysX)이 관절을 실제로 목표 각도까지 회전시킬 시간을 줌 (0.3초 대기)
        yield return new WaitForSeconds(0.3f);

        // 4. 실제로 회전한 결과를 관찰하여, +1 방향인지 -1 방향인지 오차가 더 적은 쪽으로 판별
        for (int j = 0; j < n; j++)
        {
            Transform c = joints[j].transform;

            // [실제 회전 결과]: 물리 엔진으로 움직인 후, 부모 링크 기준으로 자식 링크가 실제로 회전해 있는 상대 회전값(쿼터니언)
            Quaternion actual = Quaternion.Inverse(c.parent.rotation) * c.rotation;

            // [실제 회전한 각도]: 초기 각도(theta0)에서 실제로 움직인 각도 변화량 (단위: 도/Degree)
            float moved = joints[j].jointPosition[0] * Mathf.Rad2Deg - theta0[j];

            // [부호가 +1일 때의 오차]: 수학 모델에서 +moved만큼 회전했다고 가정했을 때와 실제 회전(actual) 간의 각도 차이
            float ePlus = Quaternion.Angle(relRot0[j] * Quaternion.AngleAxis(moved, axisLocal[j]), actual);

            // [부호가 -1일 때의 오차]: 수학 모델에서 -moved만큼 회전했다고 가정했을 때와 실제 회전(actual) 간의 각도 차이
            float eMinus = Quaternion.Angle(relRot0[j] * Quaternion.AngleAxis(-moved, axisLocal[j]), actual);

            // 오차가 더 작은 쪽을 해당 관절의 올바른 회전 부호(+1 또는 -1)로 확정
            jointSigns[j] = ePlus <= eMinus ? 1f : -1f;

            // 측정이 끝났으므로 관절을 원래 초기 각도(theta0)로 되돌리도록 목표 설정
            SetTarget(j, theta0[j]);
        }

        // 5. 관절들이 원래 초기 위치로 안전하게 복귀할 때까지 대기 (0.3초 대기)
        yield return new WaitForSeconds(0.3f);

        // 6. 회전 방향 캘리브레이션 완료 플래그 활성화
        signsCalibrated = true;

        // 7. 판별된 각 관절의 부호 값을 콘솔 창에 출력하고 에디터 활용 팁 안내
        Debug.Log($"[ArmIK] 회전 방향 확인 완료: {string.Join(", ", jointSigns)}\n" +
                  "Play 중에 ArmIK 컴포넌트 메뉴(점 3개)에서 Copy Component, Play 끄고 Paste Component Values 하면 다음부터 움찔거림이 없습니다.");
    }

    #endregion

    #region 2. 목표 받기 - 위치 + 아래 보기 + 손목 각도

    // =========================================================================
    // [Update] 매 프레임마다 테스트 타겟(testTarget)의 위치/회전 변화를 감지하여 추적
    //  - Scene 뷰에서 마우스로 IKTarget을 드래그하여 움직일 때 로봇 팔이 실시간으로 따라옵니다.
    // =========================================================================
    void Update()
    {
        // [조건 검사: 조기 종료 (Early Return)]
        //  1) !Ready            : 초기 모델 생성 및 관절 부호 캘리브레이션이 아직 안 끝났으면 대기
        //  2) !followTestTarget : Inspector에서 'Follow Test Target' 체크박스가 꺼져 있으면 작동 안 함
        //  3) testTarget == null: 따라갈 대상 오브젝트가 지정되지 않았으면 무시 (에러 방지)
        //  4) IsMoving          : 이미 로봇 팔이 이전 목표로 이동 중이면 중복 실행 방지를 위해 대기
        if (!Ready || !followTestTarget || testTarget == null || IsMoving) return;

        // 테스트 타겟의 Y축 회전각(Yaw, 도 단위) 추출 -> 로봇 그리퍼 손목의 목표 회전각으로 사용
        float yaw = testTarget.eulerAngles.y;

        // [데드존(Deadzone) 필터링: 미세한 떨림에 의한 불필요한 IK 재계산 방지]
        //  - 위치 변화가 2mm(0.002m) 이상이거나, 회전각 변화가 1도 이상일 때만 새 이동 명령을 실행합니다.
        //  - (sqrMagnitude는 제곱근(sqrt) 연산을 생략하여 성능을 최적화한 거리 제곱 비교 방식)
        //  - (Mathf.DeltaAngle은 359도에서 1도로 넘어갈 때의 360도 경계 차이를 정확하게 계산)
        if ((testTarget.position - lastFollowPos).sqrMagnitude > 0.002f * 0.002f ||
            Mathf.Abs(Mathf.DeltaAngle(yaw, lastFollowYaw)) > 1f)
        {
            // 현재 명령을 내린 타겟의 위치와 각도를 최신 기준값으로 기억
            lastFollowPos = testTarget.position;
            lastFollowYaw = yaw;

            // 계산된 새 목표 위치와 손목 각도로 로봇 팔을 움직이는 코루틴 시작
            StartCoroutine(MoveTo(testTarget.position, yaw));
        }
    }

    public IEnumerator MoveTo(Vector3 goal, float? yawDeg = null)
    {
        // [미션] 빈칸을 채워 MoveTo를 완성하세요

        // 1. 이동 중임을 표시하세요 (IsMoving)
        IsMoving = true;

        // 2. 목표 손목 각도(yawDeg)를 activeYaw에 기억하세요 (아래 Solve가 이 값을 씁니다)
        activeYaw = yawDeg;

        // 3.목표 각도 계산을 통해 실제로 움직이기
        float[] current = CurrentTargets();          // 지금 관절 각도 6개 = 출발 자세
        float[] solution = new float[n];             // 계산 결과를 담을 빈 칸 = 도착 자세

        if (!pointDown && useTargetLookDir)
        {
            // 수평 조립 정렬 시 장거리 스윙에서도 팔꿈치 위(Elbow-Up) 자세를 유지하도록 원호 시드 연속 풀이 적용
            LastSolveOk = SolveArcContinuation(goal, activeYaw, current, solution);
        }
        else
        {
            LastSolveOk = Solve(goal, activeYaw, current, solution);
        }
        if (!LastSolveOk) Debug.LogWarning($"[ArmIK] {goal} (손목 {yawDeg}도) 에 정확히 닿는 자세가 없습니다. 가장 가까운 자세로 이동합니다.");

        yield return Drive(solution, MoveTime(current, solution), true);   // 이동 시간 = 가장 많이 도는 관절 기준

        // 4. Settle을 호출해서 관절이 다 따라올 때까지 기다리세요 (코루틴 사용법 생각해보기)
        yield return Settle();

        // 5. Correct로 목표 위치(goal)에 맞게 보정하세요  (코루틴 사용법 생각해보기)
        yield return Correct(goal);

        // 6. 이동이 끝났음을 표시하세요 (IsMoving)
        IsMoving = false;
    }
    // =========================================================================
    // [MoveLinear] 그리퍼 끝(End-Effector)이 목표 지점까지 완벽한 "직선"을 그리며 이동
    //  - 일반 MoveTo는 관절 각도만 보간하므로 끝부분이 곡선(호)을 그리며 휘어집니다.
    //  - MoveLinear는 물건을 집어올리거나(Pick) 내려놓을(Place) 때 충돌 없이 똑바로 움직이도록
    //    직선 경로를 1cm(linearStep) 단위로 잘게 쪼개어 경유점들을 차례대로 따라갑니다.
    // =========================================================================
    public IEnumerator MoveLinear(Vector3 goal, float speed, float? yawDeg = null)
    {
        // 1. 이동 시작 표시 (Update 등에서 중복 명령 방지)
        IsMoving = true;

        // 2. 목표 손목 회전 각도(Yaw) 기억 (Solve와 Correct에서 유지)
        activeYaw = yawDeg;

        // 3. 현재 6개 관절의 목표 각도(출발 자세) 가져오기
        float[] startAngles = CurrentTargets();

        // 4. 순기구학(FK)으로 현재 관절 각도에서 그리퍼 끝의 실제 3D 시작 위치(startPos) 계산
        //    (out _ 는 지금 필요 없는 방향/축 정보는 버린다는 의미)
        Forward(startAngles, out Vector3 startPos, out _, out _, null, null);

        // 5. 총 이동 거리(m) 계산
        float distance = Vector3.Distance(startPos, goal);

        // 6. 직선 경로를 몇 개의 경유점(Waypoints)으로 나눌지 분할 수 결정
        //    (예: 10cm 거리이고 linearStep이 1cm(0.01m)라면 pointCount = 10개 점)
        int pointCount = Mathf.Max(1, Mathf.CeilToInt(distance / linearStep));

        // 7. 경유점 하나를 이동하는 데 할당할 시간(초) 계산
        //    (거리 / 속도로 총 소요 시간을 구한 뒤 분할 수로 나눔. 물리 주기 FixedDeltaTime보다 작지 않게 방어)
        float pointTime = Mathf.Max(Time.fixedDeltaTime, distance / Mathf.Max(speed, 0.001f) / pointCount);

        // 8. 직선 상의 모든 경유점에 대해 미리 IK를 풀어 관절 각도들을 저장할 리스트 준비
        List<float[]> pathAngles = new List<float[]>();
        float[] prevAngles = startAngles; // 이전 경유점의 각도 (다음 점 IK 계산의 초기 추정값으로 사용해 연속성 확보)
        bool allOk = true;

        // 9. 출발점에서 도착점까지 일정한 간격으로 직선 경유점들을 보간하며 IK(Solve) 계산
        for (int i = 1; i <= pointCount; i++)
        {
            // 직선 상의 i번째 경유점 3D 위치 (0% ~ 100% 선형 보간)
            Vector3 point = Vector3.Lerp(startPos, goal, (float)i / pointCount);
            float[] pointAngles = new float[n];

            // 이전 점의 각도(prevAngles)를 시드값으로 넣어 가까운 관절 해를 빠르게 찾음
            allOk &= Solve(point, activeYaw, prevAngles, pointAngles);
            pathAngles.Add(pointAngles);

            // 다음 경유점의 계산 기준점으로 갱신
            prevAngles = pointAngles;
        }

        // 10. 경로 도중 로봇 팔이 닿을 수 없는 사각지대가 있는지 확인 및 경고
        LastSolveOk = allOk;
        if (!allOk) Debug.LogWarning($"[ArmIK] 직선 경로 일부가 닿을 수 없는 범위입니다. (목표 {goal}, 손목 {yawDeg}도)");

        // 11. 미리 계산된 경유점 관절 각도들을 순서대로 모터에 전달하여 직선 주행
        //    (smooth=false: 점과 점 사이를 등속 선형 보간하여 전체 경로가 일정한 속도로 이어지게 함)
        foreach (float[] pointAngles in pathAngles)
        {
            yield return Drive(pointAngles, pointTime, false);
        }

        // 12. 관절 모터가 목표 각도에 완전히 도달해 멈출 때까지 대기
        yield return Settle();

        // 13. 로봇 링크의 하중/처짐으로 인한 최종 위치 오차를 정밀 보정
        yield return Correct(goal);

        // 14. 이동 종료 표시
        IsMoving = false;
    }

    // =========================================================================
    // [MoveLinearSmooth] 장거리 수평 정렬 직선 이동 (회전 각도에 맞춰 팔을 실시간으로 굽히며 직선 주행)
    //  - 베이스(shoulder_link)가 회전하는 동안 손끝이 곡선(호)으로 튀어나가지 않도록,
    //    1cm 간격 직선 경유점마다 어깨·팔꿈치·손목 굽힘 각도를 연속 계산하고
    //    단일 물리 루프에서 부드러운 가감속(SmoothStep)으로 정밀 보간 주행합니다.
    // =========================================================================
    public IEnumerator MoveLinearSmooth(Vector3 goal, float speed, float? yawDeg = null)
    {
        IsMoving = true;
        activeYaw = yawDeg;

        float[] startAngles = CurrentTargets();
        Forward(startAngles, out Vector3 startPos, out _, out _, null, null);

        float distance = Vector3.Distance(startPos, goal);
        if (distance > 0.0005f)
        {
            int pointCount = Mathf.Max(1, Mathf.CeilToInt(distance / linearStep));
            List<float[]> pathAngles = new List<float[]>(pointCount + 1);
            pathAngles.Add(startAngles);

            float[] prevAngles = startAngles;
            float[] jointTravel = new float[n];
            bool allOk = true;

            for (int i = 1; i <= pointCount; i++)
            {
                Vector3 point = Vector3.Lerp(startPos, goal, (float)i / pointCount);
                float[] pointAngles = new float[n];
                allOk &= Solve(point, activeYaw, prevAngles, pointAngles);

                for (int j = 0; j < n; j++)
                {
                    jointTravel[j] += Mathf.Abs(pointAngles[j] - prevAngles[j]);
                }

                pathAngles.Add(pointAngles);
                prevAngles = pointAngles;
            }

            LastSolveOk = allOk;
            if (!allOk) Debug.LogWarning($"[ArmIK] 직선 스무스 경로 일부가 닿을 수 없는 범위입니다. (목표 {goal}, 손목 {yawDeg}도)");

            float maxTravelDeg = 0f;
            for (int j = 0; j < n; j++) maxTravelDeg = Mathf.Max(maxTravelDeg, jointTravel[j]);

            // 직선 목표 속도와 관절 모터 최대 회전 속도(SmoothStep 피크 1.5배 반영)를 모두 만족하는 주행 시간
            float duration = Mathf.Max(
                minMoveTime,
                Mathf.Max(distance / Mathf.Max(speed, 0.001f), (maxTravelDeg * 1.5f) / Mathf.Max(jointSpeed, 1f))
            );

            float t = 0f;
            while (t < 1f)
            {
                t += Time.fixedDeltaTime / duration;
                float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
                float exactIdx = s * pointCount;
                int idx0 = Mathf.Min(Mathf.FloorToInt(exactIdx), pointCount - 1);
                float frac = exactIdx - idx0;

                float[] a0 = pathAngles[idx0];
                float[] a1 = pathAngles[idx0 + 1];
                for (int j = 0; j < n; j++)
                {
                    SetTarget(j, Mathf.Lerp(a0[j], a1[j], frac));
                }

                yield return new WaitForFixedUpdate();
            }
        }

        yield return Settle();
        yield return Correct(goal);

        IsMoving = false;
    }

    #endregion

    #region 3. 머릿속 계산 - FK로 예측하고 고치기

    // =========================================================================
    // [Forward] 순기구학 (Forward Kinematics, FK)
    //  - 6개 관절 각도(angles)가 주어졌을 때, 어깨부터 손끝까지 뼈대를 순서대로 연결하여
    //    그리퍼 끝의 3D 위치(tipPos), 방향(tipDir), 손가락 축(closeAxis)을 계산합니다.
    //  - [참고: 왜 DH Table이나 4x4 행렬이 없나요?]
    //    Unity에서는 무거운 4x4 행렬 곱셈 대신, 수학적으로 동일하면서 훨씬 빠르고 직관적인
    //    'Vector3(위치) + Quaternion(회전)' 쌍을 이용해 부모->자식 변환 체인을 직접 계산합니다.
    // =========================================================================
    void Forward(float[] angles, out Vector3 tipPos, out Vector3 tipDir, out Vector3 closeAxis, Vector3[] jointAxes, Vector3[] jointPivots)
    {
        // 1. 계산의 출발점: 로봇 받침대(Base Link)의 월드 위치와 회전으로 초기화
        Vector3 linkPos = baseT.position;
        Quaternion linkRot = baseT.rotation;

        // 2. 어깨(0번)부터 손목(n-1번)까지 체인을 따라 각 링크의 위치와 회전을 순차적으로 계산
        for (int j = 0; j < n; j++)
        {
            // 2-1. [관절 피벗 월드 위치]: 부모 위치 + 부모 회전 * 부모 기준 관절 중심 오프셋
            Vector3 pivotWorld = linkPos + linkRot * pivotInParent[j];

            // 2-2. [실제 회전 각도]: 초기 각도(theta0) 대비 이번에 회전할 각도 변화량에 부호(+1/-1)를 곱함
            float turn = jointSigns[j] * (angles[j] - theta0[j]);

            // 2-3. [자식 링크 월드 회전]: 부모 월드 회전 * 초기 조립 오프셋 회전 * 이 관절의 로컬 회전
            //      (4x4 행렬의 R_parent * R_rel * R_joint 회전 합성 부분과 동일)
            Quaternion childRot = linkRot * relRot0[j] * Quaternion.AngleAxis(turn, axisLocal[j]);

            // 2-4. [자코비안 IK용 데이터 저장]: Solve()에서 자코비안 행렬을 구성할 때 필요한 월드 축과 피벗 위치 저장
            if (jointAxes != null)
            {
                jointAxes[j] = jointSigns[j] * (childRot * axisLocal[j]); // 관절 회전축의 월드 방향 단위 벡터
                jointPivots[j] = pivotWorld;                              // 관절의 월드 피벗 좌표
            }

            // 2-5. [자식 링크 원점 월드 위치]: 관절 피벗 위치에서 자식의 로컬 앵커 오프셋만큼 역으로 빼줌
            //      (피벗 위치 = 자식 원점 + 자식 회전 * anchorPos 이므로 역산)
            linkPos = pivotWorld - childRot * anchorPos[j];

            // 2-6. 다음 관절(j+1) 계산을 위해 현재 자식의 회전을 새로운 부모 회전으로 갱신
            linkRot = childRot;
        }

        // 3. 마지막 손 링크(hand_link)의 위치/회전에 손 로컬 기준 그리퍼 끝 오프셋을 더해 최종 출력값 도출
        tipPos = linkPos + linkRot * tipLocal;    // [그리퍼 끝 3D 위치]: 손 원점 + 손 회전 * 손 기준 끝점 오프셋
        tipDir = linkRot * dirLocal;              // [그리퍼 지향 벡터]: 손 회전 * 손 기준 끝 방향 벡터 (수직 판별용)
        closeAxis = linkRot * closeLocal;          // [손가락 닫힘 축]: 손 회전 * 손가락 슬라이드 축 (손목 Yaw 판별용)
    }

    // =========================================================================
    // [YawErrorVector] 손목 수평 회전각(Yaw)의 오차 벡터 및 각도 오차 계산
    //  - 손가락 닫힘 축(closeAxis)이 목표 각도(yawDeg)와 얼마나 어긋나 있는지 계산합니다.
    //  - 2지 그리퍼의 180도 대칭성을 고려하여 항상 최단 회전 경로를 찾아냅니다.
    //
    //  * [수학 용어 간단 정리]
    //    1) 내적(Dot)  : 두 방향이 "얼마나 같은 쪽을 보는지" 판별하는 숫자 (음수면 각도 차이 > 90도)
    //    2) 외적(Cross): "어느 축으로 얼마나 회전해야 겹치는지" 알려주는 회전축 오차 벡터
    // =========================================================================
    static Vector3 YawErrorVector(Vector3 closeAxis, float yawDeg, out float errorDeg)
    {
        // 1. 목표 각도(yawDeg)가 가리키는 수평 목표 방향 벡터 계산 (Y축 기준 회전)
        Vector3 targetDir = Quaternion.AngleAxis(yawDeg, Vector3.up) * Vector3.forward;

        // 2. 현재 손가락 닫힘 축(closeAxis)을 수평 바닥면(XZ 평면)에 투영하여 현재 수평 방향 벡터 추출
        Vector3 currentDir = Vector3.ProjectOnPlane(closeAxis, Vector3.up).normalized;

        // 3. [내적(Dot) 활용 - 그리퍼 180도 대칭성 처리]:
        //    - 내적값(Dot)이 음수(< 0)라는 것은 두 방향의 각도 차이가 90도를 넘는다는 뜻입니다.
        //    - 2지 집게는 앞뒤가 똑같으므로, 목표를 반대(-targetDir)로 뒤집으면 180도를 뺑 돌지 않고
        //      가장 가까운 쪽(90도 이내)으로만 손목을 살짝 돌려 잡을 수 있습니다.
        if (Vector3.Dot(currentDir, targetDir) < 0f) targetDir = -targetDir;

        // 4. 현재 방향과 목표 방향 사이의 각도 오차(단위: 도/Degree) 저장
        errorDeg = Vector3.Angle(currentDir, targetDir);

        // 5. [외적(Cross) 반환 - 회전 오차 축 벡터]:
        //    - currentDir에서 targetDir로 겹치기 위해 "돌려야 하는 회전축(Y축 방향)"과
        //      "회전해야 할 양(크기 = sin(오차각))"이 동시에 담긴 3D 벡터를 만듭니다.
        //    -> 자코비안 IK(Solve)가 이 외적 벡터를 보고 모터를 어느 방향으로 돌릴지 결정합니다.
        return Vector3.Cross(currentDir, targetDir);
    }

    // =========================================================================
    // [HorizontalRollError] 수평 지향(desiredLook) 상태에서 그리퍼 축 둘레의 손목 Roll 오차 계산
    //  - rollDeg = 0도일 때 손가락 닫힘 축(closeAxis)이 수직(Vector3.up)에 정렬되어 파지된 부품이 반듯하게 유지됩니다.
    //  - 그리퍼가 수직 아래에서 수평으로 회전하는 과도기에도 특이점 없이 연속적으로 추적합니다.
    // =========================================================================
    static float HorizontalRollError(Vector3 tipDir, Vector3 closeAxis, Vector3 desiredLook, float rollDeg, out float errorDeg)
    {
        Vector3 baseRollGoal = Quaternion.AngleAxis(rollDeg, desiredLook) * Vector3.up;
        Vector3 transportedGoal = Quaternion.FromToRotation(desiredLook, tipDir) * baseRollGoal;

        Vector3 curRoll = Vector3.ProjectOnPlane(closeAxis, tipDir);
        Vector3 goalRoll = Vector3.ProjectOnPlane(transportedGoal, tipDir);
        if (curRoll.sqrMagnitude < 1e-6f || goalRoll.sqrMagnitude < 1e-6f)
        {
            errorDeg = 0f;
            return 0f;
        }
        curRoll.Normalize();
        goalRoll.Normalize();

        // 2지 그리퍼 180도 대칭성 (현재 자세에서 가까운 쪽을 유지하여 부품 뒤집힘 방지)
        if (Vector3.Dot(curRoll, goalRoll) < 0f) goalRoll = -goalRoll;

        errorDeg = Vector3.Angle(curRoll, goalRoll);
        float sinErr = Mathf.Clamp(Vector3.Dot(Vector3.Cross(curRoll, goalRoll), tipDir), -1f, 1f);
        return Mathf.Asin(sinErr);
    }

    // =========================================================================
    // [SolveArcContinuation] 수평 정렬 상태에서 큰 각도로 베이스(shoulder_link)를 스윙할 때
    //  - 베이스 중심을 관통하는 직선 점프로 인해 팔꿈치/손목이 뒤집히는 국소해를 방지하기 위해,
    //    원통좌표 원호(Arc) 경유점들을 따라 시드(Seed)를 연속 전달하여 정자세(Elbow-Up) 해를 도출합니다.
    // =========================================================================
    bool SolveArcContinuation(Vector3 goal, float? yaw, float[] startAngles, float[] result)
    {
        Forward(startAngles, out Vector3 startPos, out _, out _, null, null);
        float dist = Vector3.Distance(startPos, goal);
        Vector3 pivot0 = baseT.position + baseT.rotation * pivotInParent[0];
        Vector3 hStart = new Vector3(startPos.x - pivot0.x, 0f, startPos.z - pivot0.z);
        Vector3 hGoal = new Vector3(goal.x - pivot0.x, 0f, goal.z - pivot0.z);

        if (dist > 0.25f && hStart.magnitude > 0.1f && hGoal.magnitude > 0.1f)
        {
            float angStart = Mathf.Atan2(hStart.x, hStart.z) * Mathf.Rad2Deg;
            float angGoal = Mathf.Atan2(hGoal.x, hGoal.z) * Mathf.Rad2Deg;
            float deltaDeg = Mathf.DeltaAngle(angStart, angGoal);
            int steps = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(Mathf.Abs(deltaDeg) / 20f, dist / 0.20f)), 2, 12);

            float rStart = hStart.magnitude;
            float rGoal = hGoal.magnitude;
            float[] seed = (float[])startAngles.Clone();
            float[] next = new float[n];

            for (int s = 1; s < steps; s++)
            {
                float t = (float)s / steps;
                float angRad = (angStart + deltaDeg * t) * Mathf.Deg2Rad;
                float r = Mathf.Lerp(rStart, rGoal, t);
                float y = Mathf.Lerp(startPos.y, goal.y, t);
                Vector3 arcPoint = new Vector3(pivot0.x + Mathf.Sin(angRad) * r, y, pivot0.z + Mathf.Cos(angRad) * r);
                Solve(arcPoint, yaw, seed, next);
                System.Array.Copy(next, seed, n);
            }

            return Solve(goal, yaw, seed, result);
        }

        return Solve(goal, yaw, startAngles, result);
    }

    // =========================================================================
    // [Solve] 역기구학 (Inverse Kinematics, IK) - 자코비안 수치 반복법
    //  - 목표 위치(goal)와 손목 각도(yaw)가 주어졌을 때, 손끝이 그 위치에 닿도록
    //    6개 관절 각도를 "오차를 줄여가며 조금씩 비틀어 맞추는" 최적화 알고리즘입니다.
    //  - 감쇠 최소제곱법(Damped Least Squares, DLS)을 사용하여 팔이 완전히 펴지는
    //    특이점(Singularity)에서도 로봇이 튀거나 굳지 않고 안전하게 계산됩니다.
    // =========================================================================
    bool Solve(Vector3 goal, float? yaw, float[] startAngles, float[] result)
    {
        // 1. 출발할 관절 각도 복사 (현재 자세를 시작 추정값(Seed)으로 사용)
        float[] angles = (float[])startAngles.Clone();
        Vector3[] jointAxes = new Vector3[n];    // 각 관절의 월드 회전축을 담을 배열
        Vector3[] jointPivots = new Vector3[n];  // 각 관절의 월드 회전 중심 좌표를 담을 배열

        // 2. 제어할 오차의 종류와 개수(errorCount) 결정
        //    - 기본: 3D 위치 오차 (X, Y, Z) = 3개
        //    - pointDown이 켜져 있으면: 수직 아래(down)를 보기 위한 기울기 오차 = 2개 추가
        //    - pointDown이 꺼져 있고 useTargetLookDir이면: 3D 목표 지향(desiredLook) 회전 오차 = 2개 추가
        //    - pointDown이 꺼져 있고 일반 모드이면: 지면과 나란한 '수평(Horizontal, tipDir.y = 0)' 오차 = 1개 추가
        //    - yaw 각도가 지정되어 있으면: 손목 회전(Yaw / Roll) 오차 = 1개 추가
        bool useLook = !pointDown && useTargetLookDir && targetLookDir.sqrMagnitude > 0.001f;
        Vector3 desiredLook = useLook ? Vector3.ProjectOnPlane(targetLookDir, Vector3.up).normalized : Vector3.zero;
        if (useLook && desiredLook.sqrMagnitude < 0.001f) useLook = false;

        bool useYaw = yaw.HasValue;
        int errorCount = 3 + (pointDown ? 2 : (useLook ? 2 : 1)) + (useYaw ? 1 : 0);

        // 회전 오차(각도)를 위치 오차(m)와 밸런스를 맞추기 위한 가중치
        float rotWeight = orientationWeight;

        // 3. 자코비안 행렬 및 연립방정식 풀이에 필요한 메모리 할당
        float[,] jacobian = new float[errorCount, n];       // 자코비안 행렬 J (크기: 오차수 x 관절수)
        float[] errors = new float[errorCount];             // 현재 발생한 오차값들 벡터
        float[,] matrixA = new float[errorCount, errorCount]; // DLS 연립방정식 정방행렬 (J * J^T + λ^2 * I)
        const float damping = 0.02f;                        // 특이점 폭주 방지용 감쇠 계수(λ)
        float yawErrorDeg = 0f;

        // 4. [수치 반복 루프]: 오차가 0.5mm 이내가 될 때까지 최대 300번 미세 조정 반복
        for (int iter = 0; iter < 300; iter++)
        {
            // 4-1. 현재 임시 각도(angles)로 손끝 위치, 방향, 관절축들을 순기구학(FK)으로 계산
            Forward(angles, out Vector3 tipPos, out Vector3 tipDir, out Vector3 closeAxis, jointAxes, jointPivots);

            // 4-2. 목표와의 오차 계산
            Vector3 posError = goal - tipPos;                                                                 // 위치 오차 (m)
            Vector3 dirError = pointDown ? Vector3.Cross(tipDir, Vector3.down) : Vector3.zero;                // 수직 지향 오차
            float horizError = (!pointDown && !useLook) ? -tipDir.y : 0f;                                     // 단순 수평 지향 오차 (tipDir.y -> 0)

            // 수평 목표 방향(desiredLook) 지향: 특이점(Gimbal Lock) 및 손목 스핀 축 간섭이 없는 접평면 정규직교 기저 회전 오차
            float lookAngleDeg = 0f;
            Vector3 lookRotErr = Vector3.zero;
            Vector3 lookB1 = Vector3.zero;
            Vector3 lookB2 = Vector3.zero;
            if (useLook)
            {
                lookAngleDeg = Vector3.Angle(tipDir, desiredLook);
                float lookAngleRad = lookAngleDeg * Mathf.Deg2Rad;
                Vector3 lookCross = Vector3.Cross(tipDir, desiredLook);
                float lookCrossMag = lookCross.magnitude;
                if (lookCrossMag > 1e-5f)
                    lookRotErr = (lookCross / lookCrossMag) * lookAngleRad;
                else if (Vector3.Dot(tipDir, desiredLook) < 0f)
                    lookRotErr = Vector3.up * Mathf.PI;

                Vector3 refUp = Mathf.Abs(tipDir.y) < 0.9f ? Vector3.up : Vector3.forward;
                lookB1 = Vector3.Cross(tipDir, refUp).normalized;
                lookB2 = Vector3.Cross(tipDir, lookB1).normalized;
            }

            Vector3 yawError = Vector3.zero;
            float rollErrorRad = 0f;
            if (useYaw)
            {
                if (useLook)
                    rollErrorRad = HorizontalRollError(tipDir, closeAxis, desiredLook, yaw.Value, out yawErrorDeg);
                else
                    yawError = YawErrorVector(closeAxis, yaw.Value, out yawErrorDeg);
            }

            // 4-3. [수렴 판정 / 조기 탈출]: 오차가 충분히 작으면 이미 정답을 찾았으므로 즉시 루프 종료!
            if (posError.magnitude < 0.0005f &&
                (!pointDown || Vector3.Angle(tipDir, Vector3.down) < 0.5f) &&
                (pointDown || (useLook ? lookAngleDeg < 0.5f : Mathf.Abs(tipDir.y) < 0.01f)) &&
                (!useYaw || yawErrorDeg < 0.5f)) break;

            // 4-4. 오차 벡터에 값 채우기
            errors[0] = posError.x; errors[1] = posError.y; errors[2] = posError.z;
            if (pointDown)
            {
                errors[3] = rotWeight * dirError.x;
                errors[4] = rotWeight * dirError.z;
            }
            else if (useLook)
            {
                errors[3] = rotWeight * Vector3.Dot(lookRotErr, lookB1);
                errors[4] = rotWeight * Vector3.Dot(lookRotErr, lookB2);
            }
            else
            {
                // 단순 수평 지향 (tipDir.y = 0)
                errors[3] = rotWeight * horizError;
            }
            if (useYaw)
            {
                int yawIdx = pointDown ? 5 : (useLook ? 5 : 4);
                errors[yawIdx] = rotWeight * (useLook ? rollErrorRad : yawError.y);
            }

            // 4-5. [자코비안(Jacobian) 행렬 구성]:
            //      "j번 관절을 1라디안 돌리면 손끝 오차가 어느 방향으로 얼마나 변하는가?"
            for (int j = 0; j < n; j++)
            {
                // 물리 공식 (v = ω x r): 회전축(jointAxes)과 피벗에서 손끝까지의 벡터 외적 -> 손끝의 순간 이동 방향
                Vector3 tipMove = Vector3.Cross(jointAxes[j], tipPos - jointPivots[j]);
                jacobian[0, j] = tipMove.x; jacobian[1, j] = tipMove.y; jacobian[2, j] = tipMove.z;

                // 관절 회전이 엔드이펙터 자세(수직/수평/손목 회전)에 미치는 영향
                if (pointDown)
                {
                    jacobian[3, j] = rotWeight * jointAxes[j].x;
                    jacobian[4, j] = rotWeight * jointAxes[j].z;
                }
                else if (useLook)
                {
                    jacobian[3, j] = rotWeight * Vector3.Dot(jointAxes[j], lookB1);
                    jacobian[4, j] = rotWeight * Vector3.Dot(jointAxes[j], lookB2);
                }
                else
                {
                    // 관절 j가 회전할 때 tipDir.y의 변화율 = (jointAxes[j] x tipDir).y
                    float dTy = jointAxes[j].z * tipDir.x - jointAxes[j].x * tipDir.z;
                    jacobian[3, j] = rotWeight * dTy;
                }
                if (useYaw)
                {
                    int yawIdx = pointDown ? 5 : (useLook ? 5 : 4);
                    jacobian[yawIdx, j] = rotWeight * (useLook ? Vector3.Dot(jointAxes[j], tipDir) : jointAxes[j].y);
                }
            }

            // 4-6. [감쇠 최소제곱법(DLS) 행렬 생성]: A = J * J^T + (damping^2 * I)
            //      특이점(팔이 다 펴짐)에서 역행렬이 무한대로 튀는 것을 대각선에 damping을 더해 안정화
            for (int row = 0; row < errorCount; row++)
            {
                for (int col = 0; col < errorCount; col++)
                {
                    float sum = 0f;
                    for (int k = 0; k < n; k++) sum += jacobian[row, k] * jacobian[col, k];
                    matrixA[row, col] = sum + (row == col ? damping * damping : 0f);
                }
            }

            // 4-7. 선형 연립방정식 (A * x = errors) 풀기 -> solved 벡터 도출
            float[] solved = SolveLinear(matrixA, errors, errorCount);
            if (solved == null) break; // 풀 수 없는 특이 상황이면 중단

            // 4-8. [각도 갱신]: 구한 해(solved)를 바탕으로 각 관절을 몇 도 돌려야 할지 계산하여 반영
            for (int j = 0; j < n; j++)
            {
                float deltaRad = 0f;
                for (int row = 0; row < errorCount; row++) deltaRad += jacobian[row, j] * solved[row];

                // 한 번에 너무 크게 돌아 로봇이 튀는 것을 방지하기 위해 1회당 최대 +-10도로 제한(Clamp)
                angles[j] += Mathf.Clamp(deltaRad * Mathf.Rad2Deg, -10f, 10f);

                // 관절 물리 가동 범위(lower ~ upper)를 벗어나지 않게 한계 적용
                if (limited[j]) angles[j] = Mathf.Clamp(angles[j], lower[j], upper[j]);
            }
        }

        // 5. 최종 계산된 최적의 각도를 결과 배열(result)에 복사
        System.Array.Copy(angles, result, n);

        // 6. 계산된 최종 각도로 서 있을 때 목표에 충분히 가까운지 검증하여 성공 여부(true/false) 반환
        Forward(angles, out Vector3 finalPos, out Vector3 finalDir, out Vector3 finalClose, null, null);
        float finalYawDeg = 0f;
        if (useYaw)
        {
            if (useLook)
                HorizontalRollError(finalDir, finalClose, desiredLook, yaw.Value, out finalYawDeg);
            else
                YawErrorVector(finalClose, yaw.Value, out finalYawDeg);
        }

        // 위치 오차 3mm 미만, 수직/수평 각도 오차 이내이면 IK 성공(true)으로 판정
        bool posOk = (goal - finalPos).magnitude < 0.003f;
        bool orientOk = pointDown
            ? (Vector3.Angle(finalDir, Vector3.down) < 3f)
            : (useLook ? (Vector3.Angle(finalDir, desiredLook) < 5f) : (Mathf.Abs(finalDir.y) < 0.08f));
        bool yawOk = !useYaw || finalYawDeg < 3f;
        return posOk && orientOk && yawOk;
    }

    // =========================================================================
    // [SolveLinear] 가우스 소거법 (Gaussian Elimination)으로 선형 연립방정식 (A * x = b) 풀기
    //  - 중고등학교 때 배운 '가감법(위 식에서 아래 식을 빼서 미지수 없애기)'의 컴퓨터 알고리즘입니다.
    //  - 부분 피보팅(Partial Pivoting)을 적용하여 0으로 나누는 오류와 부동소수점 오차를 방지합니다.
    //  - Solve()에서 "오차를 줄이기 위해 관절을 각각 얼만큼 비틀어야 하는가?"를 계산할 때 사용됩니다.
    // =========================================================================
    static float[] SolveLinear(float[,] A, float[] b, int n)
    {
        // 원본 데이터를 보존하기 위해 복사본 생성
        var M = (float[,])A.Clone(); // n x n 계수 행렬 M
        var v = (float[])b.Clone();   // n차원 결과 벡터 v (우변)

        // ---------------------------------------------------------------------
        // [1단계: 전진 소거 (Forward Elimination)]
        //  - 행렬의 대각선 아래 원소들을 모두 0으로 만들어 계단 모양(상삼각 행렬)으로 변형
        // ---------------------------------------------------------------------
        for (int col = 0; col < n; col++)
        {
            // [부분 피보팅 (Partial Pivoting)]:
            // 현재 열(col)에서 절댓값이 가장 큰 숫자를 가진 행(piv)을 탐색
            int piv = col;
            for (int r = col + 1; r < n; r++)
                if (Mathf.Abs(M[r, col]) > Mathf.Abs(M[piv, col])) piv = r;

            // 만약 가장 큰 값마저 거의 0(1e-9 미만)이라면 해가 없거나 무수히 많은 경우이므로 실패(null) 반환
            if (Mathf.Abs(M[piv, col]) < 1e-9f) return null;

            // 가장 큰 값을 가진 행을 현재 행(col)과 맞바꿈 (0으로 나눔 방지 및 오차 최소화)
            if (piv != col)
            {
                for (int c = 0; c < n; c++) { float t = M[col, c]; M[col, c] = M[piv, c]; M[piv, c] = t; }
                float tv = v[col]; v[col] = v[piv]; v[piv] = tv;
            }

            // 현재 행(col)을 이용해 그 아래에 있는 행(r)들의 col번째 원소를 0으로 소거
            for (int r = col + 1; r < n; r++)
            {
                float f = M[r, col] / M[col, col]; // 소거에 곱할 비율(계수)
                for (int c = col; c < n; c++) M[r, c] -= f * M[col, c]; // 아래 행의 원소들에서 뺌
                v[r] -= f * v[col];                                     // 우변 벡터도 동일하게 뺌
            }
        }

        // ---------------------------------------------------------------------
        // [2단계: 후진 대입 (Back Substitution)]
        //  - 가장 마지막 행(미지수가 1개뿐인 식)부터 값을 구한 뒤, 위로 거슬러 올라가며 대입
        // ---------------------------------------------------------------------
        var x = new float[n]; // 미지수들의 해를 담을 배열
        for (int r = n - 1; r >= 0; r--)
        {
            float s = v[r];
            // 이미 구한 뒤쪽 미지수들의 값을 곱해서 우변에서 빼줌
            for (int c = r + 1; c < n; c++) s -= M[r, c] * x[c];

            // 남은 자기 자신의 계수로 나누어 최종 미지수 x[r] 확정!
            x[r] = s / M[r, r];
        }

        return x; // 연립방정식의 해 벡터 반환
    }

    #endregion

    #region 4. 실제로 움직이기 - xDrive target 바꾸고 기다리기

    // =========================================================================
    // [MoveTime] 두 자세(a -> b) 사이를 이동할 때 걸려야 할 최소 이동 시간(초) 계산
    //  - 6개 관절 중 "가장 각도 변화량이 큰 관절(병목 관절)"을 찾아, 그 관절이
    //    지정된 회전 속도(jointSpeed, 예: 60도/초)를 넘지 않도록 시간을 계산합니다.
    // =========================================================================
    float MoveTime(float[] a, float[] b)
    {
        // 1. 6개 관절 중 가장 많이 돌아야 하는 최대 각도 차이(max) 탐색
        float max = 0f;
        for (int j = 0; j < n; j++) max = Mathf.Max(max, Mathf.Abs(b[j] - a[j]));

        // 2. (최대 각도 / 관절 속도)로 소요 시간을 계산하되,
        //    너무 짧은 이동이라도 급발진을 막기 위해 최소 시간(minMoveTime, 예: 0.3초)을 보장
        return Mathf.Max(minMoveTime, max / jointSpeed);
    }

    // =========================================================================
    // [Drive] 정해진 시간(duration) 동안 모터 목표 각도를 출발점에서 목표점까지 점진적 이동
    //  - 물리 엔진(PhysX) 주기(FixedDeltaTime)에 맞추어 매 스텝 각도를 부드럽게 보간합니다.
    // =========================================================================
    IEnumerator Drive(float[] goal, float duration, bool smooth)
    {
        // 1. 현재 모터 목표 각도들을 출발점(start)으로 저장
        float[] start = CurrentTargets();
        float t = 0f;

        // 2. duration초 동안 물리 업데이트 주기마다 진행률(t: 0.0 -> 1.0)을 누적하며 이동
        while (t < 1f)
        {
            t += Time.fixedDeltaTime / duration; // 물리 고정 시간(0.02초)씩 누적

            // smooth=true면 부드러운 출발/도착(SmoothStep S자 곡선), false면 균일한 등속 선형 보간
            float s = smooth ? Mathf.SmoothStep(0f, 1f, t) : Mathf.Clamp01(t);

            // 6개 관절 모터의 목표 각도(target)를 실시간 보간값으로 갱신
            for (int j = 0; j < n; j++) SetTarget(j, Mathf.Lerp(start[j], goal[j], s));

            // 다음 물리 연산 주기(약 0.02초)까지 대기
            yield return new WaitForFixedUpdate();
        }
    }

    // =========================================================================
    // [Settle] 모터 명령 후 실제 관절 링크들이 물리적으로 완전히 멈출 때까지 대기
    //  - 관성과 마찰 때문에 목표 각도에 명령을 줘도 실제 링크가 도달하는 데 시간이 걸립니다.
    // =========================================================================
    IEnumerator Settle()
    {
        float wait = 0f;
        // 최대 1초 동안 기다림 (그 전에 다 멈추면 조기 종료)
        while (wait < 1f)
        {
            bool done = true;
            for (int j = 0; j < n; j++)
                // 실제 관절 각도(jointPosition)와 모터 목표 각도(target)의 오차가 0.5도 초과이면 아직 움직이는 중
                if (Mathf.Abs(joints[j].jointPosition[0] * Mathf.Rad2Deg - joints[j].xDrive.target) > 0.5f)
                { done = false; break; }

            // 6개 관절이 모두 0.5도 이내로 도착하여 안정화되었으면 대기 종료
            if (done) break;

            wait += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }
    }

    // =========================================================================
    // [CurrentTargets] 현재 6개 관절 모터(xDrive)에 설정되어 있는 목표 각도들 반환
    // =========================================================================
    float[] CurrentTargets()
    {
        float[] targets = new float[n];
        for (int j = 0; j < n; j++)
        {
            targets[j] = joints[j].xDrive.target;
        }
        return targets;
    }

    // =========================================================================
    // [SetTarget] j번 관절 모터(xDrive)의 목표 각도를 deg도로 변경
    //  - ArticulationDrive는 구조체(struct)이므로 값을 바꾼 뒤 다시 대입해야 적용됩니다.
    // =========================================================================
    void SetTarget(int j, float deg)
    {
        var d = joints[j].xDrive;
        d.target = deg;
        joints[j].xDrive = d;
    }

    #endregion

    #region 5. 확인과 보정 - 처진 만큼 다시 보정

    // =========================================================================
    // [Correct] 로봇 팔의 무게/하중으로 인한 미세한 물리적 처짐(Sagging) 오차를 반복 보정
    //  - 스프링 강성 한계로 5mm 아래로 처졌다면, 목표를 5mm 위로 조준(aimPos)하여 다시 당깁니다.
    //  - 최대 correctionPasses(3회) 동안 오차가 허용치(positionTolerance: 1mm) 이내로 줄어들 때까지 보정합니다.
    // =========================================================================
    IEnumerator Correct(Vector3 goal)
    {
        Vector3 aimPos = goal; // 보정을 위해 가상으로 조준할 위치

        for (int pass = 0; pass <= correctionPasses; pass++)
        {
            // 실제 손끝 위치(tip.position)와 원래 목표(goal) 사이의 오차 벡터 측정
            Vector3 posError = goal - tip.position;

            // [첫 이동 안전 검사]: 첫 이동인데 오차가 2cm 이상 크다면 관절 회전 방향(jointSigns) 오류 의심
            if (pass == 0 && firstMove)
            {
                firstMove = false;
                if (posError.magnitude > 0.02f)
                    Debug.LogWarning("[ArmIK] 모델 예측과 실제 위치 차이가 큽니다. Joint Signs 값이 틀렸을 수 있습니다. Signs Calibrated 체크를 끄고 다시 확인하세요.");
            }

            // 오차가 허용 오차(1mm) 미만으로 충분히 작아졌거나 마지막 패스이면 보정 종료
            if (posError.magnitude < positionTolerance || pass == correctionPasses) break;

            // [적분 제어(I 제어) 원리]: 처진 만큼 반대 방향으로 조준점을 더 높여서 보상
            aimPos += posError;

            // 수정된 조준점(aimPos)으로 IK를 다시 풀어서 관절 목표 각도 계산
            float[] currentAngles = CurrentTargets();
            float[] solution = new float[n];
            Solve(aimPos, activeYaw, currentAngles, solution);

            // 미세 보정 이동 (0.2초 동안 짧고 부드럽게 이동 후 안정화 대기)
            yield return Drive(solution, 0.2f, true);
            yield return Settle();
        }

        // 최종 이동 완료 후 실제 오차값들을 측정하여 인스펙터/GUI 표시용 변수에 저장
        Vector3 actualDir = ActualDir();
        bool useLookNow = !pointDown && useTargetLookDir && targetLookDir.sqrMagnitude > 0.001f;
        Vector3 desiredLookNow = useLookNow ? Vector3.ProjectOnPlane(targetLookDir, Vector3.up).normalized : Vector3.down;
        if (useLookNow && desiredLookNow.sqrMagnitude < 0.001f) useLookNow = false;

        PositionError = (goal - tip.position).magnitude;          // 최종 위치 오차 (단위: m)
        AngleError = pointDown
            ? Vector3.Angle(actualDir, Vector3.down)
            : (useLookNow ? Vector3.Angle(actualDir, desiredLookNow) : Mathf.Abs(90f - Vector3.Angle(actualDir, Vector3.up)));
        YawError = 0f;
        if (activeYaw.HasValue)
        {
            float yawErrorDeg;
            if (useLookNow)
                HorizontalRollError(actualDir, ActualCloseAxis(), desiredLookNow, activeYaw.Value, out yawErrorDeg);
            else
                YawErrorVector(ActualCloseAxis(), activeYaw.Value, out yawErrorDeg);
            YawError = yawErrorDeg;                               // 손목 회전(Yaw / Roll) 각도 오차 (단위: 도)
        }
    }

    // =========================================================================
    // [ActualDir] 실제 손목 관절 중심에서 그리퍼 끝을 향하는 월드 지향 벡터 계산
    // =========================================================================
    Vector3 ActualDir()
    {
        Vector3 wristPivot = joints[n - 1].transform.TransformPoint(anchorPos[n - 1]);
        return (tip.position - wristPivot).normalized;
    }

    // =========================================================================
    // [ActualCloseAxis] 실제 그리퍼 손가락이 슬라이드하며 닫히는 월드 축 벡터 계산
    // =========================================================================
    Vector3 ActualCloseAxis()
    {
        if (closingFinger == null) return transform.right;
        Vector3 slideAxisLocal = closingFinger.anchorRotation * Vector3.right;
        return (closingFinger.transform.rotation * slideAxisLocal).normalized;
    }
    #endregion

    #region 화면 표시

    // =========================================================================
    // [OnGUI] Unity Game 화면 좌상단에 실시간 IK 상태 및 오차 정보를 GUI 박스로 표시
    // =========================================================================
    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(300, 10, 290, 95), GUI.skin.box);
        GUILayout.Label(!Ready ? "IK: 회전 방향 확인 중..." : IsMoving ? "IK: 이동 중" : "IK: 대기");
        GUILayout.Label($"마지막 오차: {PositionError * 100f:F2} cm / 수직 {AngleError:F1}도 / 손목 {YawError:F1}도");
        GUILayout.Label($"경로 계산: {(LastSolveOk ? "성공" : "닿을 수 없음")}");
        GUILayout.EndArea();
    }

    #endregion
}
