using System.Collections;
using UnityEngine;

/// <summary>
/// 컨베이어 정지점에 도착한 물품 중 불량품을 감지하여 밀어내는 푸셔 액추에이터 제어기
/// - 컨베이어 환승 모드: 불량품을 밀어서 맞은편 컨베이어(예: Reject 라인)로 이관 (Line A)
/// - 단순 밀어내기 모드: 목적지 컨베이어가 없을 경우 상자(RejectBox)로 물리 낙하 배출 (Line C)
/// </summary>
public class PusherController : MonoBehaviour
{
    [Header("컨베이어 설정")]
    [Tooltip("중앙 컨베이어 매니저 참조")]
    [SerializeField] private ConveyorManager conveyorManager;

    [Tooltip("푸셔가 위치한 소스 컨베이어 라인 ID")]
    [SerializeField] private ConveyorLineId sourceLineId;

    [Tooltip("소스 라인에서 푸셔가 감지할 정지점 웨이포인트 인덱스")]
    [SerializeField] private int stopWaypointIndex;

    [Tooltip("불량품을 보낼 목적지 컨베이어 라인 ID (상자로 밀어내는 경우 기본값 유지 가능)")]
    [SerializeField] private ConveyorLineId destinationLineId = ConveyorLineId.Reject;

    [Tooltip("목적지 컨베이어의 시작 웨이포인트 인덱스")]
    [SerializeField] private int destinationStartWaypointIndex;

    [Tooltip("목적지 컨베이어 진입 방향 기준점 Transform")]
    [SerializeField] private Transform destinationDirectionPoint;

    [Header("푸셔 기구부")]
    [Tooltip("물품을 밀어내는 푸셔 헤드 Transform")]
    [SerializeField] private Transform pusherHead;

    [Tooltip("푸셔 헤드의 대기(후퇴) 위치 Transform")]
    [SerializeField] private Transform retractedPoint;

    [Tooltip("푸셔 헤드의 전진 위치 Transform")]
    [SerializeField] private Transform extendedPoint;

    [Header("동작 파라미터")]
    [Tooltip("푸셔 헤드 전진/후퇴 속도")]
    [SerializeField] private float pusherSpeed = 0.8f;

    [Tooltip("전진 완료 후 유지 대기 시간(초)")]
    [SerializeField] private float holdTime = 0.2f;

    private Rigidbody pusherRigidbody;
    private bool isBusy;

    /// <summary>
    /// 불량품 배출 완료 시 발생하는 이벤트
    /// </summary>
    public event System.Action<GameObject> ItemPushed;

    private void Awake()
    {
        if (pusherHead == null)
            return;

        pusherRigidbody = pusherHead.GetComponent<Rigidbody>();
        if (pusherRigidbody == null)
        {
            Debug.LogError("PusherController: Pusher Head에 Rigidbody가 필요합니다.", this);
            return;
        }

        pusherRigidbody.isKinematic = true;
        pusherRigidbody.useGravity = false;
        pusherRigidbody.interpolation = RigidbodyInterpolation.Interpolate;

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
            conveyorManager.OnItemArrivedAtStopPoint += HandleItemArrivedAtStopPoint;
        }
    }

    private void OnDisable()
    {
        if (conveyorManager != null)
        {
            conveyorManager.OnItemArrivedAtStopPoint -= HandleItemArrivedAtStopPoint;
        }
    }

    private void Start()
    {
        if (conveyorManager == null)
        {
            Debug.LogError("PusherController: ConveyorManager가 연결되지 않았습니다.", this);
            return;
        }

        if (pusherHead == null)
            Debug.LogError("PusherController: Pusher Head가 지정되지 않았습니다.", this);

        if (retractedPoint == null)
            Debug.LogError("PusherController: Retracted Point가 지정되지 않았습니다.", this);

        if (extendedPoint == null)
            Debug.LogError("PusherController: Extended Point가 지정되지 않았습니다.", this);
    }

    /// <summary>
    /// 컨베이어 정지점 도착 이벤트 핸들러
    /// </summary>
    private void HandleItemArrivedAtStopPoint(
        ConveyorLineId arrivedLineId,
        int arrivedWaypointIndex,
        GameObject arrivedItem)
    {
        if (arrivedLineId != sourceLineId || arrivedWaypointIndex != stopWaypointIndex)
            return;

        if (arrivedItem == null || isBusy)
            return;

        StartCoroutine(ProcessItem(arrivedItem));
    }

    /// <summary>
    /// 도착 물품의 불량 여부를 종합 판정하여 배출 처리
    /// </summary>
    private IEnumerator ProcessItem(GameObject arrivedItem)
    {
        isBusy = true;

        Item item = arrivedItem.GetComponent<Item>() 
                 ?? arrivedItem.GetComponentInParent<Item>() 
                 ?? arrivedItem.GetComponentInChildren<Item>();
        ConveyorItemMover mover = arrivedItem.GetComponent<ConveyorItemMover>()
                               ?? arrivedItem.GetComponentInParent<ConveyorItemMover>()
                               ?? arrivedItem.GetComponentInChildren<ConveyorItemMover>();

        // 1. 불량 판정: 양품이면 푸셔를 밀지 않고 컨베이어 라인을 그대로 재가동
        if (!IsDefectiveItem(arrivedItem))
        {
            conveyorManager.ReleaseItemAndRunLine(sourceLineId, mover);
            isBusy = false;
            yield break;
        }

        // 2. 푸셔 기구부 및 부품 상태 검증
        if (pusherRigidbody == null || retractedPoint == null || extendedPoint == null)
        {
            Debug.LogError("PusherController: 푸셔 위치 또는 Rigidbody 설정이 누락되었습니다.", this);
            conveyorManager.ReleaseItemAndRunLine(sourceLineId, mover);
            isBusy = false;
            yield break;
        }

        Rigidbody itemRigidbody = arrivedItem.GetComponent<Rigidbody>()
                               ?? arrivedItem.GetComponentInParent<Rigidbody>()
                               ?? arrivedItem.GetComponentInChildren<Rigidbody>();

        if (mover == null || itemRigidbody == null || !HasSolidCollider(arrivedItem))
        {
            Debug.LogError("PusherController: 배출 대상에 ConveyorItemMover, Rigidbody, Solid Collider가 필요합니다.", arrivedItem);
            conveyorManager.ReleaseItemAndRunLine(sourceLineId, mover);
            isBusy = false;
            yield break;
        }

        // 3. 물리 밀어내기 준비: 이송 스크립트 일시 중지 및 물리/중력 활성화
        mover.enabled = false;

        itemRigidbody.position = arrivedItem.transform.position;
        itemRigidbody.rotation = arrivedItem.transform.rotation;
        Physics.SyncTransforms();

        itemRigidbody.isKinematic = false;
        itemRigidbody.useGravity = true;
        itemRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        itemRigidbody.WakeUp();

        // 4. 푸셔 헤드 전진 (물품 밀어내기)
        yield return MovePusherTo(extendedPoint);

        if (holdTime > 0f)
            yield return new WaitForSeconds(holdTime);

        // 5. 푸셔 헤드 원위치 복귀
        yield return MovePusherTo(retractedPoint);

        // 잔여 수평 속도 감쇄
        Vector3 itemVelocity = itemRigidbody.linearVelocity;
        itemRigidbody.linearVelocity = new Vector3(0f, itemVelocity.y, 0f);
        itemRigidbody.angularVelocity = Vector3.zero;

        // 6. 배출 모드 분기 (컨베이어 이관 vs 상자 단순 낙하)
        Transform[] destinationWaypoints = conveyorManager.GetLineWaypoints(destinationLineId);
        bool hasDestinationConveyor = (destinationLineId != sourceLineId) &&
                                      (destinationWaypoints != null && destinationWaypoints.Length > 0);

        if (hasDestinationConveyor)
        {
            // [모드 A: 컨베이어 환승 (Line A)]
            Vector3 destinationDirection = destinationDirectionPoint != null
                ? destinationDirectionPoint.forward
                : transform.forward;

            if (item != null) item.state = ItemState.Idle;

            mover.TransferToConveyor(
                conveyorManager,
                destinationLineId,
                destinationDirection,
                Vector3.zero,
                destinationStartWaypointIndex);

            ProcessCounters.Add(CounterType.Defective);
            mover.enabled = true;
            conveyorManager.RunLine(destinationLineId);
            conveyorManager.ReleaseItemAndRunLine(sourceLineId, mover);
        }
        else
        {
            // [모드 B: 단순 밀어내기 / 상자 자유 낙하 (Line C)]
            // mover.enabled = false 상태 유지 (상자 안으로 물리 중력 낙하)
            if (item != null) item.state = ItemState.Idle;

            // 소스 라인은 다음 작업을 위해 재가동 (완제품은 상자로 분리되었으므로 null 전달)
            conveyorManager.ReleaseItemAndRunLine(sourceLineId, null);
        }

        ItemPushed?.Invoke(arrivedItem);
        isBusy = false;
    }

    /// <summary>
    /// Item, BaseInfo, PartInfo의 결함 정보를 포괄 확인하는 종합 불량 판정 메서드
    /// </summary>
    private bool IsDefectiveItem(GameObject target)
    {
        if (target == null) return false;

        // 1. Item 컴포넌트 기준
        Item item = target.GetComponent<Item>() 
                 ?? target.GetComponentInParent<Item>() 
                 ?? target.GetComponentInChildren<Item>();
        if (item != null && item.itemType == ItemType.Defective)
            return true;

        // 2. BaseInfo 컴포넌트 기준 (차체 베이스 불량)
        BaseInfo baseInfo = target.GetComponent<BaseInfo>() 
                         ?? target.GetComponentInParent<BaseInfo>() 
                         ?? target.GetComponentInChildren<BaseInfo>();
        if (baseInfo != null && baseInfo.defectType != DefectType.None)
            return true;

        // 3. PartInfo 컴포넌트 기준 (클로 부품 불량)
        PartInfo partInfo = target.GetComponent<PartInfo>() 
                         ?? target.GetComponentInParent<PartInfo>() 
                         ?? target.GetComponentInChildren<PartInfo>();
        if (partInfo != null && partInfo.defectType != DefectType.None)
            return true;

        return false;
    }

    /// <summary>
    /// Kinematic Rigidbody를 목표 위치로 물리 이동
    /// </summary>
    private IEnumerator MovePusherTo(Transform target)
    {
        if (target == null || pusherRigidbody == null)
            yield break;

        float moveSpeed = Mathf.Max(0.01f, pusherSpeed);

        while (Vector3.Distance(pusherRigidbody.position, target.position) > 0.001f)
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

    /// <summary>
    /// 트리거가 아닌 유효한 충돌체(Solid Collider)가 있는지 확인
    /// </summary>
    private bool HasSolidCollider(GameObject target)
    {
        Collider[] colliders = target.GetComponentsInChildren<Collider>(true);

        foreach (Collider targetCollider in colliders)
        {
            if (targetCollider != null && targetCollider.enabled && !targetCollider.isTrigger)
            {
                return true;
            }
        }

        return false;
    }
}
