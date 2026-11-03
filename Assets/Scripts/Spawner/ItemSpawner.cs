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

    // 아이템 생성 간격입니다.
    [SerializeField] private float spawnInterval = 2f;

    [Header("아이템 방향 설정")]

    // 스폰 회전과 경로 방향에 적용할 오프셋입니다.
    [SerializeField] private Vector3 rotationOffset;

    // 지정하면 이 Transform의 forward 방향을 이동 방향으로 사용합니다.
    // 비워두면 ItemSpawner의 forward 방향을 사용합니다.
    [SerializeField] private Transform moveDirectionPoint;

    [Header("중앙 컨베이어 매니저 설정")]

    // 씬에 배치한 ConveyorManager입니다.
    [SerializeField] private ConveyorManager conveyorManager;

    // 이 스포너가 아이템을 투입할 라인입니다.
    [SerializeField] private ConveyorLineId lineId;

    // 불량품이 생성될 확률입니다. 0.5는 50%입니다.
    [Header("불량품 설정")]
    [SerializeField, Range(0f, 1f)] private float defectiveProbability = 0.5f;

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
            // 라인이 실제 운전 중일 때만 아이템을 생성합니다.
            if (conveyorManager.IsLineRunning(lineId))
            {
                if (SpawnItem())
                    currentSpawnCount++;

                yield return new WaitForSeconds(spawnInterval);
            }
            else
            {
                // 라인이 정지 중이면 스폰도 기다립니다.
                yield return new WaitForSeconds(0.5f);
            }
        }
    }

    private bool SpawnItem()
    {
        if (itemPrefab == null || spawnPoint == null)
            return false;

        Quaternion spawnRotation =
            spawnPoint.rotation * Quaternion.Euler(rotationOffset);

        GameObject newItem = Instantiate(
            itemPrefab,
            spawnPoint.position,
            spawnRotation);

        // 물리 이동을 위해 Rigidbody를 프리팹 루트에서 가져옵니다.
        Rigidbody itemRigidbody =
            newItem.GetComponent<Rigidbody>();

        if (itemRigidbody == null)
        {
            Debug.LogError(
                "ItemSpawner: 아이템 프리팹 루트에 Rigidbody가 필요합니다.",
                newItem);

            Destroy(newItem);
            return false;
        }

        // 생성 직후 기존 속도를 초기화합니다.
        itemRigidbody.linearVelocity = Vector3.zero;
        itemRigidbody.angularVelocity = Vector3.zero;
        itemRigidbody.WakeUp();

        // 프리팹에 이동기가 없으면 추가합니다.
        ConveyorItemMover mover =
            newItem.GetComponent<ConveyorItemMover>();

        if (mover == null)
            mover = newItem.AddComponent<ConveyorItemMover>();

        Vector3 direction = moveDirectionPoint != null
            ? moveDirectionPoint.forward
            : transform.forward;

        mover.Initialize(
            conveyorManager,
            lineId,
            direction,
            rotationOffset);

        // 불량 여부를 한 번만 무작위로 정합니다.
        bool isDefective = Random.value < defectiveProbability;

        // 기존 Pusher가 ItemType을 확인하므로 판정 결과를 기록합니다.
        Item item = newItem.GetComponent<Item>();

        if (item != null)
        {
            item.itemType = isDefective
                ? ItemType.Defective
                : ItemType.Normal;

            item.shouldBeGrabbed = !isDefective;
        }
        else
        {
            Debug.LogWarning(
                "ItemSpawner: 아이템 프리팹 루트에 Item 컴포넌트가 없습니다.",
                newItem);
        }

        // 불량품이면 PartLoss에 지정된 부품을 비활성화합니다.
        if (isDefective)
        {
            PartLoss partLoss = newItem.GetComponent<PartLoss>();

            if (partLoss != null)
            {
                partLoss.ApplyDefect(DefectType.PartLoss);
            }
            else
            {
                Debug.LogWarning(
                    "ItemSpawner: 불량 아이템인데 PartLoss 컴포넌트를 찾지 못했습니다.",
                    newItem);
            }
        }

        return true;
    }
}