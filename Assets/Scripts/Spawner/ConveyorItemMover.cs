using UnityEngine;

public class ConveyorItemMover : MonoBehaviour
{
    [Header("이동 설정")]

    // 웨이포인트에 도착했다고 판단할 거리입니다.
    [SerializeField] private float waypointReachDistance = 0.03f;

    // 이동 방향을 향해 회전시킬지 설정합니다.
    [SerializeField] private bool rotateAlongPath = true;

    // 이동 방향으로 회전하는 속도입니다.
    [SerializeField] private float rotationSpeed = 8f;

    // 아이템을 제어할 중앙 매니저입니다.
    private ConveyorManager conveyorManager;

    // 아이템이 이동할 라인 ID입니다.
    private ConveyorLineId lineId;

    // 웨이포인트가 없을 때 사용할 직선 방향입니다.
    private Vector3 moveDirection;

    // 아이템 모델에 적용할 추가 회전값입니다.
    private Vector3 rotationOffset;

    // 현재 이동 목표 웨이포인트의 인덱스입니다.
    private int waypointIndex;

    // 작업 지점에서 대기 중인지 나타냅니다.
    private bool isWaitingAtStopPoint;

    // 아이템 운반 상태를 확인할 컴포넌트입니다.
    private Item item;

    // 연결 누락 경고를 한 번만 출력하기 위한 변수입니다.
    private bool hasWarnedMissingManager;

    public bool IsWaitingAtStopPoint => isWaitingAtStopPoint;
    public int CurrentWaypointIndex => waypointIndex;

    private void Awake()
    {
        item = GetComponent<Item>();
    }

    // ItemSpawner가 아이템 생성 후 호출합니다.
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

        // 방향 벡터가 비어 있으면 아이템의 현재 앞 방향을 사용합니다.
        moveDirection = direction.sqrMagnitude > 0f
            ? direction.normalized
            : transform.forward;

        waypointIndex = Mathf.Max(0, startWaypointIndex);
        isWaitingAtStopPoint = false;
        hasWarnedMissingManager = false;

        if (item == null)
            item = GetComponent<Item>();

        if (conveyorManager == null)
        {
            Debug.LogError(
                "ConveyorItemMover: ConveyorManager가 전달되지 않았습니다.",
                this);
        }
    }

    private void Update()
    {
        // 매니저 연결이 없으면 이동하지 않습니다.
        if (conveyorManager == null)
        {
            if (!hasWarnedMissingManager)
            {
                Debug.LogWarning(
                    "ConveyorItemMover: ConveyorManager가 연결되지 않았습니다.",
                    this);

                hasWarnedMissingManager = true;
            }

            return;
        }

        // 작업 대기 중인 아이템은 현재 위치에 머뭅니다.
        if (isWaitingAtStopPoint)
            return;

        // 로봇이 잡거나 다른 상태로 전환된 아이템은 컨베이어가 이동시키지 않습니다.
        if (item != null && item.state != ItemState.Idle)
            return;

        // 라인이 멈췄거나 일시정지 중이면 속도는 0입니다.
        float speed = conveyorManager.GetLineSpeed(lineId);

        if (speed <= 0f)
            return;

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

    // 웨이포인트가 없는 경우 직선으로 이동합니다.
    private void MoveStraight(float speed)
    {
        transform.position +=
            moveDirection * speed * Time.deltaTime;

        // 직선 이동에서도 이동 방향과 오프셋을 반영합니다.
        if (rotateAlongPath)
            RotateTowards(moveDirection);
    }

    // 현재 웨이포인트까지 이동합니다.
    private void MoveAlongWaypoints(
        Transform[] waypoints,
        float speed)
    {
        if (waypointIndex >= waypoints.Length)
            return;

        Transform target = waypoints[waypointIndex];

        // 웨이포인트가 비어 있으면 다음 항목으로 넘어갑니다.
        if (target == null)
        {
            waypointIndex++;
            return;
        }

        Vector3 directionToTarget =
            target.position - transform.position;

        // 목표 위치까지 이동합니다.
        transform.position = Vector3.MoveTowards(
            transform.position,
            target.position,
            speed * Time.deltaTime);

        // 이동 방향에 회전 오프셋을 더해 아이템의 방향을 맞춥니다.
        if (rotateAlongPath)
            RotateTowards(directionToTarget);

        if (Vector3.Distance(transform.position, target.position) >
            waypointReachDistance)
        {
            return;
        }

        // 도착한 아이템을 웨이포인트 위치에 맞춥니다.
        transform.position = target.position;

        // 작업 정지 지점에 도착하면 라인을 멈추고 대기합니다.
        if (conveyorManager.IsStopWaypoint(lineId, waypointIndex))
        {
            isWaitingAtStopPoint = true;

            conveyorManager.NotifyItemArrivedAtStopPoint(
                lineId,
                waypointIndex,
                gameObject);

            return;
        }

        // 정지 지점이 아니면 다음 웨이포인트로 진행합니다.
        waypointIndex++;
    }

    // 진행 방향을 바라보되 Rotation Offset을 계속 적용합니다.
    private void RotateTowards(Vector3 direction)
    {
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.0001f)
            return;

        Quaternion pathRotation =
            Quaternion.LookRotation(direction.normalized, Vector3.up);

        Quaternion offsetRotation =
            Quaternion.Euler(rotationOffset);

        Quaternion targetRotation =
            pathRotation * offsetRotation;

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            rotationSpeed * Time.deltaTime);
    }

    // 작업 완료 후 호출하면 다음 웨이포인트로 이동할 수 있습니다.
    public void ReleaseWaitingAtStopPoint()
    {
        if (!isWaitingAtStopPoint)
            return;

        isWaitingAtStopPoint = false;
        waypointIndex++;
    }

    // 아이템을 다른 컨베이어 라인으로 넘길 때 사용합니다.
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