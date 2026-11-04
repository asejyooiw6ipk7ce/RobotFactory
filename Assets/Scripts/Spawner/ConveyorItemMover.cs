using UnityEngine;

public class ConveyorItemMover : MonoBehaviour
{
    [Header("경로 이동 설정")]

    // 정지점에 도착했다고 판단할 수평 거리입니다.
    [SerializeField] private float waypointReachDistance = 0.05f;

    // 일반 경유 웨이포인트를 부드럽게 통과할 수평 거리입니다 (오버슈트 및 진동 방지).
    [SerializeField] private float passWaypointDistance = 0.25f;

    // 이동 경로를 따라 아이템의 Y축 방향(Yaw)도 돌릴지 설정합니다.
    [SerializeField] private bool rotateAlongPath = true;

    // 이동 방향으로 회전하는 속도입니다.
    [SerializeField] private float rotationSpeed = 8f;

    // 중앙 컨베이어 매니저와 라인 정보입니다.
    private ConveyorManager conveyorManager;
    private ConveyorLineId lineId;

    // 웨이포인트가 없을 때 사용할 직선 이동 방향입니다.
    private Vector3 moveDirection;

    // 아이템 모델에 적용할 회전 오프셋입니다.
    private Vector3 rotationOffset;

    // 현재 이동할 웨이포인트 인덱스입니다.
    private int waypointIndex;

    // 정지점에서 작업 완료를 기다리는 상태입니다.
    private bool isWaitingAtStopPoint;

    // 아이템 상태와 물리 이동을 제어하는 컴포넌트입니다.
    private Item item;
    private Rigidbody itemRigidbody;

    // 매니저 또는 Rigidbody 누락 경고를 한 번만 출력합니다.
    private bool hasWarnedMissingManager;
    private bool hasWarnedMissingRigidbody;

    public bool IsWaitingAtStopPoint => isWaitingAtStopPoint;
    public int CurrentWaypointIndex => waypointIndex;

    private void Awake()
    {
        item = GetComponent<Item>();
        itemRigidbody = GetComponent<Rigidbody>();
    }

    // 스포너가 아이템 생성 직후 매니저, 라인, 방향을 전달합니다.
    public void Initialize(
        ConveyorManager manager,
        ConveyorLineId targetLineId,
        Vector3 direction,
        Vector3 itemRotationOffset = default,
        int startWaypointIndex = 0)
    {
        conveyorManager = manager;
        lineId = targetLineId;
        rotationOffset = itemRotationOffset;

        moveDirection = direction.sqrMagnitude > 0f
            ? direction.normalized
            : transform.forward;

        waypointIndex = Mathf.Max(0, startWaypointIndex);
        isWaitingAtStopPoint = false;
        hasWarnedMissingManager = false;
        hasWarnedMissingRigidbody = false;

        if (item == null)
            item = GetComponent<Item>();

        if (itemRigidbody == null)
            itemRigidbody = GetComponent<Rigidbody>();

        if (itemRigidbody == null)
        {
            Debug.LogError(
                "ConveyorItemMover: 물리 이동을 위해 아이템 루트에 Rigidbody가 필요합니다.",
                this);
        }
    }

    // 동적 Rigidbody의 속도는 물리 프레임에서 갱신합니다.
    private void FixedUpdate()
    {
        if (conveyorManager == null)
        {
            StopHorizontalMotion();

            if (!hasWarnedMissingManager)
            {
                Debug.LogWarning(
                    "ConveyorItemMover: ConveyorManager가 연결되지 않았습니다.",
                    this);

                hasWarnedMissingManager = true;
            }

            return;
        }

        if (itemRigidbody == null)
        {
            if (!hasWarnedMissingRigidbody)
            {
                Debug.LogError(
                    "ConveyorItemMover: Rigidbody가 없어 아이템을 움직일 수 없습니다.",
                    this);

                hasWarnedMissingRigidbody = true;
            }

            return;
        }

        // 작업 대기 중이거나 로봇이 아이템을 인계받으면 수평 이동을 멈춥니다.
        if (isWaitingAtStopPoint ||
            (item != null && item.state != ItemState.Idle))
        {
            StopHorizontalMotion();
            return;
        }

        // 매니저가 라인을 멈추면 수평 이동 속도도 0으로 만듭니다.
        float speed = conveyorManager.GetLineSpeed(lineId);

        if (speed <= 0f)
        {
            StopHorizontalMotion();
            return;
        }

        Transform[] waypoints =
            conveyorManager.GetLineWaypoints(lineId);

        if (waypoints != null && waypoints.Length > 0)
        {
            MoveAlongWaypoints(waypoints, speed);
        }
        else
        {
            MoveStraight(speed);
        }
    }

    // 웨이포인트가 없는 경우 지정한 방향으로 이동합니다.
    private void MoveStraight(float speed)
    {
        Vector3 planarDirection =
            Vector3.ProjectOnPlane(moveDirection, Vector3.up);

        if (planarDirection.sqrMagnitude <= 0.0001f)
        {
            StopHorizontalMotion();
            return;
        }

        SetHorizontalVelocity(planarDirection.normalized * speed);

        if (rotateAlongPath)
            RotateAlongDirection(planarDirection);
    }

    // 웨이포인트를 순서대로 따라갑니다.
    private void MoveAlongWaypoints(
        Transform[] waypoints,
        float speed)
    {
        // 현재 지점이 비어 있거나 이미 도착했으면 다음 지점을 확인합니다.
        while (waypointIndex < waypoints.Length)
        {
            Transform target = waypoints[waypointIndex];

            if (target == null)
            {
                waypointIndex++;
                continue;
            }

            // 컨베이어가 수평이라고 보고 Y축 차이는 도착 거리에서 제외합니다.
            Vector3 directionToTarget =
                target.position - itemRigidbody.position;

            Vector3 planarDirection =
                Vector3.ProjectOnPlane(directionToTarget, Vector3.up);

            // 정지점은 정밀하게 감속/정지하고, 일반 경유점은 부드럽게 통과하여 오버슈트 진동을 방지합니다.
            // (Line C 완제품 양품의 경우 스토퍼 상승 및 정지 없이 직진 통과하도록 gameObject 전달)
            bool isStopPoint = conveyorManager.IsStopWaypoint(lineId, waypointIndex, gameObject);
            float thresholdDistance = isStopPoint ? waypointReachDistance : passWaypointDistance;

            if (planarDirection.magnitude > thresholdDistance)
            {
                SetHorizontalVelocity(planarDirection.normalized * speed);

                if (rotateAlongPath)
                    RotateAlongDirection(planarDirection);

                return;
            }

            // 정지 지점에 도착하면 아이템과 라인을 멈추고 이벤트를 보냅니다.
            if (isStopPoint)
            {
                isWaitingAtStopPoint = true;
                StopHorizontalMotion();

                conveyorManager.NotifyItemArrivedAtStopPoint(
                    lineId,
                    waypointIndex,
                    gameObject);

                return;
            }

            if (!isStopPoint && conveyorManager.IsStopWaypoint(lineId, waypointIndex))
            {
                Debug.Log($"[ConveyorItemMover] Line C 완제품('{gameObject.name}')은 조립성공품(양품)이므로 스토퍼(WP {waypointIndex})를 정지 없이 직진 통과합니다.", this);
            }

            // 일반 경유점(또는 양품 직진 통과 지점)이면 감속 없이 즉시 다음 웨이포인트로 넘어갑니다.
            waypointIndex++;
        }

        // 배열 끝까지 왔으면 경로가 끝난 것이므로 이동을 멈춥니다.
        StopHorizontalMotion();
    }

    // 수평 속도만 설정하고 Y축은 순수 물리/중력에 맡깁니다.
    private void SetHorizontalVelocity(Vector3 horizontalVelocity)
    {
        Vector3 currentVelocity = itemRigidbody.linearVelocity;

        itemRigidbody.linearVelocity = new Vector3(
            horizontalVelocity.x,
            currentVelocity.y,
            horizontalVelocity.z);
    }

    // 라인이 멈출 때 수평 이동만 정지시킵니다.
    // Y축 속도를 보존해 중력과 낙하가 계속 작동하게 합니다.
    private void StopHorizontalMotion()
    {
        if (itemRigidbody == null || itemRigidbody.isKinematic)
            return;

        Vector3 currentVelocity = itemRigidbody.linearVelocity;

        itemRigidbody.linearVelocity = new Vector3(
            0f,
            currentVelocity.y,
            0f);
    }

    // 이동 방향에 맞춰 Y축(Yaw) 회전만 적용합니다.
    // X/Z 회전을 건드리지 않아야 Rigidbody의 FreezeRotationX/Z 제약과 충돌하여 팽이처럼 도는 현상을 방지합니다.
    private void RotateAlongDirection(Vector3 direction)
    {
        Vector3 planarDirection =
            Vector3.ProjectOnPlane(direction, Vector3.up);

        if (planarDirection.sqrMagnitude <= 0.001f)
            return;

        float targetYaw = Mathf.Atan2(planarDirection.x, planarDirection.z) * Mathf.Rad2Deg + rotationOffset.y;
        Quaternion targetRotation = Quaternion.Euler(
            itemRigidbody.rotation.eulerAngles.x,
            targetYaw,
            itemRigidbody.rotation.eulerAngles.z);

        itemRigidbody.MoveRotation(
            Quaternion.Slerp(
                itemRigidbody.rotation,
                targetRotation,
                rotationSpeed * Time.fixedDeltaTime));
    }

    // 작업 완료 시 호출하면 다음 웨이포인트로 진행합니다.
    public void ReleaseWaitingAtStopPoint()
    {
        if (!isWaitingAtStopPoint)
            return;

        isWaitingAtStopPoint = false;
        waypointIndex++;
    }

    // 아이템을 다른 라인으로 이관할 때 사용합니다.
    public void TransferToConveyor(
        ConveyorManager manager,
        ConveyorLineId targetLineId,
        Vector3 direction,
        Vector3 itemRotationOffset = default,
        int startWaypointIndex = 0)
    {
        Initialize(
            manager,
            targetLineId,
            direction,
            itemRotationOffset,
            startWaypointIndex);
    }
}