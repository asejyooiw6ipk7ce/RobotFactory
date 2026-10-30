using System.Collections;
using UnityEngine;

public class ItemSpawner : MonoBehaviour
{
    [Header("----- 스폰 설정-----")]
    //생성할 오브젝트
    [SerializeField]
    private GameObject itemPrefab;
    //생성 위치
    [SerializeField]
    private Transform spawnPoint;
    //박스 수
    [SerializeField]
    private int maxCount = 20;
    //시간 간격
    [SerializeField]
    private float spawnInterval = 2.0f;

    [Header("생성 회전 설정")]
    [SerializeField]
    private Vector3 rotationOffset;
    [Header("생성후 진행방향 설정")]
    [SerializeField] private Transform moveDirectionPoint;

    //현재까지 생성된 개수
    private int currentSpawnCount = 0;

    [SerializeField]
    private ConveyorController conveyorController;
    void Start()
    {
        if (conveyorController == null)
        {
            Debug.LogError("ConveyorController가 연결되지 않았습니다.");
            return;
        }

        StartCoroutine(SpawnRoutine());
    }

    void Update()
    {

    }

    private IEnumerator SpawnRoutine()
    {
        while (currentSpawnCount < maxCount)
        {
            // 컨베이어가 작동 중인지 확인
            if (conveyorController != null &&
                conveyorController.isConveyorRunning == false)
            {
                SpawnItem();
                currentSpawnCount++;

                yield return new WaitForSeconds(spawnInterval);
            }
            else
            {
                yield return new WaitForSeconds(0.5f);
            }
        }
    }

    private void SpawnItem()
    {
        if (itemPrefab == null || spawnPoint == null)
            return;

        Quaternion spawnRotation = spawnPoint.rotation * Quaternion.Euler(rotationOffset);

        GameObject newItem = Instantiate(
            itemPrefab,
            spawnPoint.position,
            spawnRotation
        );

        // 이동 컴포넌트 초기화
        if (newItem.TryGetComponent(out ConveyorItemMover mover))
        {
            mover.Initialize(conveyorController, moveDirectionPoint.forward);
        }

        // Item 타입 설정
        if (newItem.TryGetComponent(out Item item))
        {
            ItemType randomType = Random.Range(0, 2) == 0
                ? ItemType.Normal
                : ItemType.Defective;

            item.itemType = randomType;
            item.shouldBeGrabbed = (randomType == ItemType.Normal);
        }
    }
}
