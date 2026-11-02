using System.Collections;
using UnityEngine;

public class ItemSpawner : MonoBehaviour
{
    [Header("스폰할 아이템 설정")]

    // 생성할 프리팹입니다.
    [SerializeField] private GameObject itemPrefab;

    // 아이템을 생성할 위치입니다.
    [SerializeField] private Transform spawnPoint;

    // 생성할 최대 아이템 수입니다.
    [SerializeField] private int maxCount = 20;

    // 아이템 생성 간격입니다.
    [SerializeField] private float spawnInterval = 2f;

    [Header("아이템 방향 설정")]

    // 스폰 회전과 이동 방향 회전에 계속 적용할 오프셋입니다.
    [SerializeField] private Vector3 rotationOffset;

    // 지정하면 이 Transform의 앞 방향을 이동 방향으로 사용합니다.
    // 비워두면 ItemSpawner의 앞 방향을 사용합니다.
    [SerializeField] private Transform moveDirectionPoint;

    [Header("중앙 컨베이어 매니저 설정")]

    // 씬에 배치한 중앙 ConveyorManager입니다.
    [SerializeField] private ConveyorManager conveyorManager;

    // 이 스포너가 아이템을 투입할 라인입니다.
    [SerializeField] private ConveyorLineId lineId;

    // 지금까지 생성한 아이템 수입니다.
    private int currentSpawnCount;

    private void Start()
    {
        if (itemPrefab == null)
        {
            Debug.LogError(
                "ItemSpawner: Item Prefab이 연결되지 않았습니다.",
                this);
            return;
        }

        if (spawnPoint == null)
        {
            Debug.LogError(
                "ItemSpawner: Spawn Point가 연결되지 않았습니다.",
                this);
            return;
        }

        if (conveyorManager == null)
        {
            Debug.LogError(
                "ItemSpawner: ConveyorManager가 연결되지 않았습니다.",
                this);
            return;
        }

        StartCoroutine(SpawnRoutine());
    }

    private IEnumerator SpawnRoutine()
    {
        while (currentSpawnCount < maxCount)
        {
            // 지정한 라인이 운전 중일 때만 생성합니다.
            if (conveyorManager.IsLineRunning(lineId))
            {
                if (SpawnItem())
                    currentSpawnCount++;

                yield return new WaitForSeconds(spawnInterval);
            }
            else
            {
                // 라인이 정지하면 스폰도 대기합니다.
                yield return new WaitForSeconds(0.5f);
            }
        }
    }

    private bool SpawnItem()
    {
        if (itemPrefab == null || spawnPoint == null)
            return false;

        // 스폰 위치 회전에 Rotation Offset을 적용합니다.
        Quaternion spawnRotation =
            spawnPoint.rotation * Quaternion.Euler(rotationOffset);

        GameObject newItem = Instantiate(
            itemPrefab,
            spawnPoint.position,
            spawnRotation);

        // 프리팹에 이동기가 없으면 추가합니다.
        ConveyorItemMover mover =
            newItem.GetComponent<ConveyorItemMover>();

        if (mover == null)
            mover = newItem.AddComponent<ConveyorItemMover>();

        // 지정한 방향 기준점이 없으면 스포너의 앞 방향을 사용합니다.
        Vector3 direction = moveDirectionPoint != null
            ? moveDirectionPoint.forward
            : transform.forward;

        // 이동기에도 회전 오프셋을 전달해 웨이포인트 이동 중 유지하게 합니다.
        mover.Initialize(
            conveyorManager,
            lineId,
            direction,
            rotationOffset);

        // Item 컴포넌트가 있는 프리팹이면 종류를 설정합니다.
        Item item = newItem.GetComponent<Item>();

        if (item != null)
        {
            ItemType randomType = Random.Range(0, 2) == 0
                ? ItemType.Normal
                : ItemType.Defective;

            item.itemType = randomType;
            item.shouldBeGrabbed = randomType == ItemType.Normal;
        }

        return true;
    }
}