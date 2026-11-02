using UnityEngine;

public class ConveyorItemMover : MonoBehaviour
{
    // 이 아이템을 제어할 중앙 매니저입니다.
    private ConveyorManager conveyorManager;

    // 아이템이 따라갈 컨베이어 라인 ID입니다.
    private ConveyorLineId lineId;

    // 라인 경로가 아직 없을 때 사용할 직선 이동 방향입니다.
    private Vector3 moveDirection;

    // 매 프레임 찾지 않도록 아이템 컴포넌트를 저장합니다.
    private Item item;

    // 매니저 연결 누락 경고가 매 프레임 나오지 않게 합니다.
    private bool hasWarnedMissingManager;

    private void Awake()
    {
        // 아이템 상태를 확인할 Item 컴포넌트를 미리 가져옵니다.
        item = GetComponent<Item>();
    }

    // ItemSpawner가 새 아이템을 만들고 호출합니다.
    public void Initialize(
        ConveyorManager manager,
        ConveyorLineId targetLineId,
        Vector3 direction)
    {
        conveyorManager = manager;
        lineId = targetLineId;

        // 전달된 방향이 0이면 이 오브젝트의 앞 방향을 사용합니다.
        moveDirection = direction.sqrMagnitude > 0f
            ? direction.normalized
            : transform.forward;

        // 런타임에 AddComponent로 추가된 경우를 대비해 다시 확인합니다.
        if (item == null)
            item = GetComponent<Item>();

        // 초기화가 다시 호출되면 경고 상태도 초기화합니다.
        hasWarnedMissingManager = false;

        if (conveyorManager == null)
        {
            Debug.LogError(
                "ConveyorItemMover: ConveyorManager가 전달되지 않았습니다.",
                this);
        }
    }

    private void Update()
    {
        // 매니저가 없으면 어느 라인의 속도를 사용할지 알 수 없습니다.
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

        // 아이템이 파지되거나 전달된 상태라면 컨베이어가 움직이지 않습니다.
        if (item != null && item.state != ItemState.Idle)
            return;

        // 라인이 정지 또는 일시정지 상태면 0이 반환됩니다.
        float speed = conveyorManager.GetLineSpeed(lineId);

        if (speed <= 0f)
            return;

        // 매니저에서 가져온 라인 속도로 아이템을 직선 이동시킵니다.
        transform.position += moveDirection * speed * Time.deltaTime;
    }

    // 조립이 끝난 아이템을 다른 라인으로 넘길 때 호출할 수 있습니다.
    public void TransferToConveyor(
        ConveyorManager manager,
        ConveyorLineId targetLineId,
        Vector3 direction)
    {
        Initialize(manager, targetLineId, direction);
    }
}