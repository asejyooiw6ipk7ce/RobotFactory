using System.Collections;
using UnityEngine;

public class PusherController : MonoBehaviour
{
    [Header("컨베이어 설정")]

    // 컨베이어 라인과 정지 지점을 관리하는 중앙 매니저입니다.
    [SerializeField] private ConveyorManager conveyorManager;

    // 푸셔가 설치된 원래 라인입니다.
    [SerializeField] private ConveyorLineId sourceLineId;

    // 원래 라인 Waypoints 배열에서 푸셔가 작동할 정지점 인덱스입니다.
    [SerializeField] private int stopWaypointIndex;

    // 불량품을 보낼 목적지 라인입니다.
    [SerializeField] private ConveyorLineId destinationLineId = ConveyorLineId.Reject;

    // 목적지 라인의 Waypoints 배열에서 이동을 시작할 인덱스입니다.
    [SerializeField] private int destinationStartWaypointIndex;

    // Reject 라인에 Waypoints가 없을 때 사용할 진행 방향입니다.
    // 이 오브젝트의 파란색 Z축이 Reject 컨베이어 진행 방향을 향하도록 놓습니다.
    [SerializeField] private Transform destinationDirectionPoint;

    [Header("푸셔 위치")]

    // 실제로 부품을 미는 푸셔 헤드입니다.
    [SerializeField] private Transform pusherHead;

    // 푸셔가 평소에 대기할 위치입니다.
    [SerializeField] private Transform retractedPoint;

    // 푸셔가 부품을 밀 때 도달할 위치입니다.
    [SerializeField] private Transform extendedPoint;

    [Header("동작 설정")]

    // 푸셔 헤드가 움직이는 속도입니다.
    [SerializeField] private float pusherSpeed = 0.8f;

    // 부품을 민 뒤 푸셔가 잠시 머무는 시간입니다.
    [SerializeField] private float holdTime = 0.2f;

    private Rigidbody pusherRigidbody;
    private bool isBusy;

    private void Awake()
    {
        if (pusherHead == null)
            return;

        pusherRigidbody = pusherHead.GetComponent<Rigidbody>();

        if (pusherRigidbody == null)
        {
            Debug.LogError(
                "PusherController: Pusher Head에 Rigidbody가 필요합니다.",
                this);
            return;
        }

        // 푸셔 헤드는 중력에 떨어지지 않으면서 물리 충돌은 하도록 설정합니다.
        pusherRigidbody.isKinematic = true;
        pusherRigidbody.useGravity = false;
        pusherRigidbody.interpolation = RigidbodyInterpolation.Interpolate;

        // 시작할 때 푸셔 헤드를 대기 위치에 놓습니다.
        if (retractedPoint != null)
        {
            pusherRigidbody.position = retractedPoint.position;
            pusherRigidbody.rotation = retractedPoint.rotation;
        }
    }

    private void OnEnable()
    {
        if (conveyorManager != null)
        {
            conveyorManager.OnItemArrivedAtStopPoint +=
                HandleItemArrivedAtStopPoint;
        }
    }

    private void OnDisable()
    {
        if (conveyorManager != null)
        {
            conveyorManager.OnItemArrivedAtStopPoint -=
                HandleItemArrivedAtStopPoint;
        }
    }

    private void Start()
    {
        if (conveyorManager == null)
        {
            Debug.LogError(
                "PusherController: ConveyorManager가 연결되지 않았습니다.",
                this);
            return;
        }

        if (pusherHead == null)
            Debug.LogError("PusherController: Pusher Head가 비어 있습니다.", this);

        if (pusherRigidbody == null)
            Debug.LogError("PusherController: Pusher Head의 Rigidbody를 찾지 못했습니다.", this);

        if (retractedPoint == null)
            Debug.LogError("PusherController: Retracted Point가 비어 있습니다.", this);

        if (extendedPoint == null)
            Debug.LogError("PusherController: Extended Point가 비어 있습니다.", this);

        if (destinationLineId == sourceLineId)
        {
            Debug.LogError(
                "PusherController: 목적지 라인은 원래 라인과 다른 Reject 라인으로 설정해야 합니다.",
                this);
        }

        // Reject 라인에 Waypoints가 있으면 시작 인덱스가 배열 범위 안인지 확인합니다.
        Transform[] destinationWaypoints =
            conveyorManager.GetLineWaypoints(destinationLineId);

        if (destinationWaypoints != null &&
            destinationWaypoints.Length > 0)
        {
            if (destinationStartWaypointIndex < 0 ||
                destinationStartWaypointIndex >= destinationWaypoints.Length)
            {
                Debug.LogError(
                    "PusherController: Destination Start Waypoint Index가 Reject 라인 Waypoints 범위를 벗어났습니다.",
                    this);
            }
        }
        else if (destinationDirectionPoint == null)
        {
            // Waypoints가 없다면 직선 이동에 사용할 방향이 필요합니다.
            Debug.LogError(
                "PusherController: Reject 라인에 Waypoints가 없으므로 Destination Direction Point를 연결해야 합니다.",
                this);
        }

        if (pusherHead != null &&
            !HasSolidCollider(pusherHead.gameObject))
        {
            Debug.LogError(
                "PusherController: Pusher Head에 Is Trigger가 꺼진 Collider가 필요합니다.",
                this);
        }
    }

    // 매니저가 부품의 정지점 도착을 알리면 호출됩니다.
    private void HandleItemArrivedAtStopPoint(
        ConveyorLineId arrivedLineId,
        int arrivedWaypointIndex,
        GameObject arrivedItem)
    {
        // 푸셔가 담당하는 라인의 지정된 정지점인지 확인합니다.
        if (arrivedLineId != sourceLineId ||
            arrivedWaypointIndex != stopWaypointIndex)
        {
            return;
        }

        if (arrivedItem == null || isBusy)
            return;

        StartCoroutine(ProcessItem(arrivedItem));
    }

    // 정상 부품은 통과시키고 불량품은 밀어 Reject 라인으로 넘깁니다.
    private IEnumerator ProcessItem(GameObject arrivedItem)
    {
        isBusy = true;

        Item item = arrivedItem.GetComponent<Item>();
        ConveyorItemMover mover =
            arrivedItem.GetComponent<ConveyorItemMover>();

        // 정상 부품이면 푸셔를 움직이지 않고 원래 라인을 다시 시작합니다.
        if (item == null || item.itemType != ItemType.Defective)
        {
            conveyorManager.ReleaseItemAndRunLine(
                sourceLineId,
                mover);

            isBusy = false;
            yield break;
        }

        // 푸셔 위치 설정이 빠졌다면 부품을 정지점에서 풀어줍니다.
        if (pusherRigidbody == null ||
            retractedPoint == null ||
            extendedPoint == null)
        {
            Debug.LogError(
                "PusherController: 푸셔 위치 또는 Rigidbody 설정이 빠졌습니다.",
                this);

            conveyorManager.ReleaseItemAndRunLine(
                sourceLineId,
                mover);

            isBusy = false;
            yield break;
        }

        Rigidbody itemRigidbody = arrivedItem.GetComponent<Rigidbody>();

        // Reject 라인으로 넘길 때는 아이템 이동 스크립트도 필요합니다.
        if (mover == null ||
            itemRigidbody == null ||
            !HasSolidCollider(arrivedItem))
        {
            Debug.LogError(
                "PusherController: 불량품에 ConveyorItemMover, Rigidbody, Is Trigger가 꺼진 Collider가 필요합니다.",
                arrivedItem);

            conveyorManager.ReleaseItemAndRunLine(
                sourceLineId,
                mover);

            isBusy = false;
            yield break;
        }

        // 푸셔가 미는 동안 이동 스크립트가 위치를 덮어쓰지 않도록 끕니다.
        mover.enabled = false;

        // 현재 Transform 위치를 물리 Rigidbody에 반영합니다.
        itemRigidbody.position = arrivedItem.transform.position;
        itemRigidbody.rotation = arrivedItem.transform.rotation;
        Physics.SyncTransforms();

        // 부품은 중력을 유지하는 동적 Rigidbody로 둡니다.
        itemRigidbody.isKinematic = false;
        itemRigidbody.useGravity = true;
        itemRigidbody.collisionDetectionMode =
            CollisionDetectionMode.ContinuousDynamic;
        itemRigidbody.WakeUp();

        // 푸셔 헤드가 부품을 물리적으로 Reject 컨베이어 쪽으로 밉니다.
        yield return MovePusherTo(extendedPoint);

        if (holdTime > 0f)
            yield return new WaitForSeconds(holdTime);

        // 부품에서 푸셔 헤드를 떼어냅니다.
        yield return MovePusherTo(retractedPoint);

        // 푸셔가 민 뒤의 수평 관성을 정리하고 수직 중력 속도는 유지합니다.
        Vector3 itemVelocity = itemRigidbody.linearVelocity;
        itemRigidbody.linearVelocity =
            new Vector3(0f, itemVelocity.y, 0f);
        itemRigidbody.angularVelocity = Vector3.zero;

        // 이제 부품이 Reject 라인 소속으로 이동하도록 경로를 바꿉니다.
        Vector3 destinationDirection =
            destinationDirectionPoint != null
                ? destinationDirectionPoint.forward
                : transform.forward;

        item.state = ItemState.Idle;

        mover.TransferToConveyor(
            conveyorManager,
            destinationLineId,
            destinationDirection,
            Vector3.zero,
            destinationStartWaypointIndex);

        // 불량품이 Reject 컨베이어로 이동을 시작할 때 대시보드 카운터를 갱신합니다.
        ProcessCounters.Add(CounterType.Defective);

        // 경로 설정 후 이동 스크립트를 켜고 Reject 라인을 시작합니다.
        mover.enabled = true;
        conveyorManager.RunLine(destinationLineId);

        // 원래 라인도 다시 시작합니다.
        conveyorManager.ReleaseItemAndRunLine(
            sourceLineId,
            mover);

        isBusy = false;
    }

    // Kinematic Rigidbody인 푸셔 헤드를 물리 업데이트 주기에 맞춰 이동합니다.
    private IEnumerator MovePusherTo(Transform target)
    {
        if (target == null || pusherRigidbody == null)
            yield break;

        float moveSpeed = Mathf.Max(0.01f, pusherSpeed);

        while (Vector3.Distance(
                   pusherRigidbody.position,
                   target.position) > 0.001f)
        {
            yield return new WaitForFixedUpdate();

            Vector3 nextPosition = Vector3.MoveTowards(
                pusherRigidbody.position,
                target.position,
                moveSpeed * Time.fixedDeltaTime);

            Quaternion nextRotation = Quaternion.RotateTowards(
                pusherRigidbody.rotation,
                target.rotation,
                360f * Time.fixedDeltaTime);

            pusherRigidbody.MovePosition(nextPosition);
            pusherRigidbody.MoveRotation(nextRotation);
        }

        yield return new WaitForFixedUpdate();

        pusherRigidbody.MovePosition(target.position);
        pusherRigidbody.MoveRotation(target.rotation);
    }

    // 오브젝트 자신이나 자식 오브젝트에 실제 충돌용 Collider가 있는지 확인합니다.
    private bool HasSolidCollider(GameObject target)
    {
        Collider[] colliders =
            target.GetComponentsInChildren<Collider>(true);

        foreach (Collider targetCollider in colliders)
        {
            if (targetCollider != null &&
                targetCollider.enabled &&
                !targetCollider.isTrigger)
            {
                return true;
            }
        }

        return false;
    }
}