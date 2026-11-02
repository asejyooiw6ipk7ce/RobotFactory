using System.Collections;
using UnityEngine;

public class ItemSpawner : MonoBehaviour
{
    [Header("스폰할 아이템 설정")]

    // 생성할 아이템 프리팹입니다.
    [SerializeField] private GameObject itemPrefab;

    // 아이템이 생성될 위치입니다.
    [SerializeField] private Transform spawnPoint;

    // 생성할 최대 아이템 수입니다.
    [SerializeField] private int maxCount = 20;

    // 아이템을 생성하는 간격입니다.
    [SerializeField] private float spawnInterval = 2f;

    [Header("아이템 방향 설정")]

    // 스폰 위치 회전에 더할 추가 회전값입니다.
    [SerializeField] private Vector3 rotationOffset;

    // 지정하면 이 Transform의 forward 방향으로 아이템을 이동시킵니다.
    // 비워두면 ItemSpawner 자신의 forward 방향을 사용합니다.
    [SerializeField] private Transform moveDirectionPoint;

    [Header("중앙 컨베이어 매니저 설정")]

    // 씬에 배치한 ConveyorManager를 연결합니다.
    [SerializeField] private ConveyorManager conveyorManager;

    // 이 스포너가 아이템을 투입할 라인을 지정합니다.
    [SerializeField] private ConveyorLineId lineId;

    // 지금까지 생성한 아이템 수입니다.
    private int currentSpawnCount;

    private void Start()
    {
        // 필수 참조가 비어 있으면 스폰을 시작하지 않습니다.
        if (itemPrefab == null)
        {
            Debug.LogError("ItemSpawner: Item Prefab이 연결되지 않았습니다.", this);
            return;
        }

        if (spawnPoint == null)
        {
            Debug.LogError("ItemSpawner: Spawn Point가 연결되지 않았습니다.", this);
            return;
        }

        if (conveyorManager == null)
        {
            Debug.LogError("ItemSpawner: ConveyorManager가 연결되지 않았습니다.", this);
            return;
        }

        // 컨베이어 라인이 운전 중일 때 스폰하도록 코루틴을 시작합니다.
        StartCoroutine(SpawnRoutine());
    }

    private IEnumerator SpawnRoutine()
    {
        while (currentSpawnCount < maxCount)
        {
            // 지정한 라인이 운전 중일 때만 아이템을 생성합니다.
            if (conveyorManager.IsLineRunning(lineId))
            {
                bool spawned = SpawnItem();

                // 실제 생성에 성공했을 때만 생성 수를 증가시킵니다.
                if (spawned)
                    currentSpawnCount++;

                yield return new WaitForSeconds(spawnInterval);
            }
            else
            {
                // 라인이 정지 중이면 잠시 기다렸다가 다시 확인합니다.
                yield return new WaitForSeconds(0.5f);
            }
        }
    }

    // 아이템을 생성하고 ConveyorItemMover를 초기화합니다.
    private bool SpawnItem()
    {
        if (itemPrefab == null || spawnPoint == null)
            return false;

        // 스폰 위치 회전에 추가 회전값을 적용합니다.
        Quaternion spawnRotation =
            spawnPoint.rotation * Quaternion.Euler(rotationOffset);

        GameObject newItem = Instantiate(
            itemPrefab,
            spawnPoint.position,
            spawnRotation);

        // 프리팹에 이동기가 없으면 런타임에 추가합니다.
        ConveyorItemMover mover =
            newItem.GetComponent<ConveyorItemMover>();

        if (mover == null)
            mover = newItem.AddComponent<ConveyorItemMover>();

        // 이동 방향 기준점이 있으면 그 방향을 사용하고,
        // 없으면 스포너 오브젝트의 앞 방향을 사용합니다.
        Vector3 direction = moveDirectionPoint != null
            ? moveDirectionPoint.forward
            : transform.forward;

        // 아이템 이동기에 매니저, 라인 ID, 이동 방향을 전달합니다.
        mover.Initialize(conveyorManager, lineId, direction);

        // Item 컴포넌트가 있는 프리팹이면 아이템 종류를 무작위로 지정합니다.
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