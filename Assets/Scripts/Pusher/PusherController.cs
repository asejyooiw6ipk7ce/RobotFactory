using System.Collections;
using UnityEngine;

public class PusherController : MonoBehaviour
{
    [Header("�����̾� ����")]

    // �����̾� ���ΰ� ���� ������ �����ϴ� �߾� �Ŵ����Դϴ�.
    [SerializeField] private ConveyorManager conveyorManager;

    // Ǫ�Ű� ��ġ�� ���� �����Դϴ�.
    [SerializeField] private ConveyorLineId sourceLineId;

    // ���� ���� Waypoints �迭���� Ǫ�Ű� �۵��� ������ �ε����Դϴ�.
    [SerializeField] private int stopWaypointIndex;

    // �ҷ�ǰ�� ���� ������ �����Դϴ�.
    [SerializeField] private ConveyorLineId destinationLineId = ConveyorLineId.Reject;

    // ������ ������ Waypoints �迭���� �̵��� ������ �ε����Դϴ�.
    [SerializeField] private int destinationStartWaypointIndex;

    // Reject ���ο� Waypoints�� ���� �� ����� ���� �����Դϴ�.
    // �� ������Ʈ�� �Ķ��� Z���� Reject �����̾� ���� ������ ���ϵ��� �����ϴ�.
    [SerializeField] private Transform destinationDirectionPoint;

    [Header("Ǫ�� ��ġ")]

    // ������ ��ǰ�� �̴� Ǫ�� ����Դϴ�.
    [SerializeField] private Transform pusherHead;

    // Ǫ�Ű� ��ҿ� ����� ��ġ�Դϴ�.
    [SerializeField] private Transform retractedPoint;

    // Ǫ�Ű� ��ǰ�� �� �� ������ ��ġ�Դϴ�.
    [SerializeField] private Transform extendedPoint;

    [Header("���� ����")]

    // Ǫ�� ��尡 �����̴� �ӵ��Դϴ�.
    [SerializeField] private float pusherSpeed = 0.8f;

    // ��ǰ�� �� �� Ǫ�Ű� ��� �ӹ��� �ð��Դϴ�.
    [SerializeField] private float holdTime = 0.2f;

    private Rigidbody pusherRigidbody;
    private bool isBusy;

    // �ҷ�ǰ�� �о Reject �������� �ѱ� �� ���� (ProcessController�� ���̽� ���� Ʈ���ŷ� ���)
    public event System.Action<GameObject> ItemPushed;

    private void Awake()
    {
        if (pusherHead == null)
            return;

        pusherRigidbody = pusherHead.GetComponent<Rigidbody>();

        if (pusherRigidbody == null)
        {
            Debug.LogError(
                "PusherController: Pusher Head�� Rigidbody�� �ʿ��մϴ�.",
                this);
            return;
        }

        // Ǫ�� ���� �߷¿� �������� �����鼭 ���� �浹�� �ϵ��� �����մϴ�.
        pusherRigidbody.isKinematic = true;
        pusherRigidbody.useGravity = false;
        pusherRigidbody.interpolation = RigidbodyInterpolation.Interpolate;

        // ������ �� Ǫ�� ��带 ��� ��ġ�� �����ϴ�.
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
                "PusherController: ConveyorManager�� ������� �ʾҽ��ϴ�.",
                this);
            return;
        }

        if (pusherHead == null)
            Debug.LogError("PusherController: Pusher Head�� ��� �ֽ��ϴ�.", this);

        if (pusherRigidbody == null)
            Debug.LogError("PusherController: Pusher Head�� Rigidbody�� ã�� ���߽��ϴ�.", this);

        if (retractedPoint == null)
            Debug.LogError("PusherController: Retracted Point�� ��� �ֽ��ϴ�.", this);

        if (extendedPoint == null)
            Debug.LogError("PusherController: Extended Point�� ��� �ֽ��ϴ�.", this);

        if (destinationLineId == sourceLineId)
        {
            Debug.LogError(
                "PusherController: ������ ������ ���� ���ΰ� �ٸ� Reject �������� �����ؾ� �մϴ�.",
                this);
        }

        // Reject ���ο� Waypoints�� ������ ���� �ε����� �迭 ���� ������ Ȯ���մϴ�.
        Transform[] destinationWaypoints =
            conveyorManager.GetLineWaypoints(destinationLineId);

        if (destinationWaypoints != null &&
            destinationWaypoints.Length > 0)
        {
            if (destinationStartWaypointIndex < 0 ||
                destinationStartWaypointIndex >= destinationWaypoints.Length)
            {
                Debug.LogError(
                    "PusherController: Destination Start Waypoint Index�� Reject ���� Waypoints ������ ������ϴ�.",
                    this);
            }
        }
        else if (destinationDirectionPoint == null)
        {
            // Waypoints�� ���ٸ� ���� �̵��� ����� ������ �ʿ��մϴ�.
            Debug.LogError(
                "PusherController: Reject ���ο� Waypoints�� �����Ƿ� Destination Direction Point�� �����ؾ� �մϴ�.",
                this);
        }

        if (pusherHead != null &&
            !HasSolidCollider(pusherHead.gameObject))
        {
            Debug.LogError(
                "PusherController: Pusher Head�� Is Trigger�� ���� Collider�� �ʿ��մϴ�.",
                this);
        }
    }

    // �Ŵ����� ��ǰ�� ������ ������ �˸��� ȣ��˴ϴ�.
    private void HandleItemArrivedAtStopPoint(
        ConveyorLineId arrivedLineId,
        int arrivedWaypointIndex,
        GameObject arrivedItem)
    {
        // Ǫ�Ű� ����ϴ� ������ ������ ���������� Ȯ���մϴ�.
        if (arrivedLineId != sourceLineId ||
            arrivedWaypointIndex != stopWaypointIndex)
        {
            return;
        }

        if (arrivedItem == null || isBusy)
            return;

        StartCoroutine(ProcessItem(arrivedItem));
    }

    // ���� ��ǰ�� �����Ű�� �ҷ�ǰ�� �о� Reject �������� �ѱ�ϴ�.
    private IEnumerator ProcessItem(GameObject arrivedItem)
    {
        isBusy = true;

        Item item = arrivedItem.GetComponent<Item>();
        ConveyorItemMover mover =
            arrivedItem.GetComponent<ConveyorItemMover>();

        // ���� ��ǰ�̸� Ǫ�Ÿ� �������� �ʰ� ���� ������ �ٽ� �����մϴ�.
        if (item == null || item.itemType != ItemType.Defective)
        {
            conveyorManager.ReleaseItemAndRunLine(
                sourceLineId,
                mover);

            isBusy = false;
            yield break;
        }

        // Ǫ�� ��ġ ������ �����ٸ� ��ǰ�� ���������� Ǯ���ݴϴ�.
        if (pusherRigidbody == null ||
            retractedPoint == null ||
            extendedPoint == null)
        {
            Debug.LogError(
                "PusherController: Ǫ�� ��ġ �Ǵ� Rigidbody ������ �������ϴ�.",
                this);

            conveyorManager.ReleaseItemAndRunLine(
                sourceLineId,
                mover);

            isBusy = false;
            yield break;
        }

        Rigidbody itemRigidbody = arrivedItem.GetComponent<Rigidbody>();

        // Reject �������� �ѱ� ���� ������ �̵� ��ũ��Ʈ�� �ʿ��մϴ�.
        if (mover == null ||
            itemRigidbody == null ||
            !HasSolidCollider(arrivedItem))
        {
            Debug.LogError(
                "PusherController: �ҷ�ǰ�� ConveyorItemMover, Rigidbody, Is Trigger�� ���� Collider�� �ʿ��մϴ�.",
                arrivedItem);

            conveyorManager.ReleaseItemAndRunLine(
                sourceLineId,
                mover);

            isBusy = false;
            yield break;
        }

        // Ǫ�Ű� �̴� ���� �̵� ��ũ��Ʈ�� ��ġ�� ����� �ʵ��� ���ϴ�.
        mover.enabled = false;

        // ���� Transform ��ġ�� ���� Rigidbody�� �ݿ��մϴ�.
        itemRigidbody.position = arrivedItem.transform.position;
        itemRigidbody.rotation = arrivedItem.transform.rotation;
        Physics.SyncTransforms();

        // ��ǰ�� �߷��� �����ϴ� ���� Rigidbody�� �Ӵϴ�.
        itemRigidbody.isKinematic = false;
        itemRigidbody.useGravity = true;
        itemRigidbody.collisionDetectionMode =
            CollisionDetectionMode.ContinuousDynamic;
        itemRigidbody.WakeUp();

        // Ǫ�� ��尡 ��ǰ�� ���������� Reject �����̾� ������ �Ӵϴ�.
        yield return MovePusherTo(extendedPoint);

        if (holdTime > 0f)
            yield return new WaitForSeconds(holdTime);

        // ��ǰ���� Ǫ�� ��带 ������ϴ�.
        yield return MovePusherTo(retractedPoint);

        // Ǫ�Ű� �� ���� ���� ������ �����ϰ� ���� �߷� �ӵ��� �����մϴ�.
        Vector3 itemVelocity = itemRigidbody.linearVelocity;
        itemRigidbody.linearVelocity =
            new Vector3(0f, itemVelocity.y, 0f);
        itemRigidbody.angularVelocity = Vector3.zero;

        // ���� ��ǰ�� Reject ���� �Ҽ����� �̵��ϵ��� ��θ� �ٲߴϴ�.
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

        // �ҷ�ǰ�� Reject �����̾�� �̵��� ������ �� ��ú��� ī���͸� �����մϴ�.
        // 베이스 불량 != Defective(완제품 불량)
        //ProcessCounters.Add(CounterType.Defective);

        // ��� ���� �� �̵� ��ũ��Ʈ�� �Ѱ� Reject ������ �����մϴ�.
        mover.enabled = true;
        conveyorManager.RunLine(destinationLineId);

        // ���� ���ε� �ٽ� �����մϴ�.
        conveyorManager.ReleaseItemAndRunLine(
            sourceLineId,
            mover);

        ItemPushed?.Invoke(arrivedItem);

        isBusy = false;
    }

    // Kinematic Rigidbody�� Ǫ�� ��带 ���� ������Ʈ �ֱ⿡ ���� �̵��մϴ�.
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

    // ������Ʈ �ڽ��̳� �ڽ� ������Ʈ�� ���� �浹�� Collider�� �ִ��� Ȯ���մϴ�.
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