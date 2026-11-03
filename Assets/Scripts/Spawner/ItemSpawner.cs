using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ItemSpawner : MonoBehaviour
{
    [Header("스폰할 아이템 프리팹 및 위치")]
    [Tooltip("스폰할 아이템 프리팹")]
    [SerializeField] private GameObject itemPrefab;

    [Tooltip("아이템이 생성될 위치 Transform")]
    [SerializeField] private Transform spawnPoint;

    [Tooltip("스폰 간격(초)")]
    [SerializeField] private float spawnInterval = 2f;

    [Header("스폰 방향 및 회전")]
    [Tooltip("생성 시 적용할 추가 회전 오프셋")]
    [SerializeField] private Vector3 rotationOffset;

    [Tooltip("이동 진행 방향 Transform (미지정 시 Spawner의 forward 사용)")]
    [SerializeField] private Transform moveDirectionPoint;

    [Header("컨베이어 라인 설정")]
    [Tooltip("중앙 컨베이어 매니저 참조")]
    [SerializeField] private ConveyorManager conveyorManager;

    [Tooltip("스포너가 속한 컨베이어 라인 ID")]
    [SerializeField] private ConveyorLineId lineId;

    [Header("불량 확률 설정")]
    [Tooltip("불량품 생성 확률 (0.0: 모두 양품, 1.0: 모두 불량)")]
    [SerializeField, Range(0f, 1f)] private float defectiveProbability = 0.5f;

    // 현재까지 생성된 누적 아이템 수
    private int currentSpawnCount;

    // 단일 부품 정책: 현재 라인에 활성화된 아이템이 없을 때만 true (1회 1개 생성)
    private bool readyToSpawn = true;

    // ProcessController에서 스폰 가능 여부 제어
    public bool AllowSpawn { get; set; } = true;

    public ConveyorLineId LineId => lineId;

    // Reset 시 파괴할 생성 아이템 목록
    private readonly List<GameObject> spawnedItems = new List<GameObject>();

    private Coroutine spawnRoutineInstance;

    private void Start()
    {
        if (itemPrefab == null)
        {
            Debug.LogError("ItemSpawner: Item Prefab이 설정되지 않았습니다.", this);
            return;
        }

        if (spawnPoint == null)
        {
            Debug.LogError("ItemSpawner: Spawn Point가 설정되지 않았습니다.", this);
            return;
        }

        if (conveyorManager == null)
        {
            Debug.LogError("ItemSpawner: ConveyorManager가 설정되지 않았습니다.", this);
            return;
        }

        StartSpawnRoutine();
    }

    private void StartSpawnRoutine()
    {
        if (spawnRoutineInstance != null)
            StopCoroutine(spawnRoutineInstance);

        spawnRoutineInstance = StartCoroutine(SpawnRoutine());
    }

    private IEnumerator SpawnRoutine()
    {
        while (true)
        {
            // 단일 부품 정책: 이전 부품이 퇴출되어 readyToSpawn == true이고, 라인이 가동 중일 때만 1개 생성
            if (readyToSpawn && conveyorManager.IsLineRunning(lineId) && AllowSpawn)
            {
                if (SpawnItem())
                {
                    currentSpawnCount++;
                    readyToSpawn = false; // 부품이 퇴출될 때까지 다음 생성 차단 (공정 내 1개 유지)
                    Debug.Log($"[ItemSpawner] ✨ 새 부품 스폰 완료! 라인: {lineId}, 누적: {currentSpawnCount}개", this);
                }

                yield return new WaitForSeconds(spawnInterval);
            }
            else
            {
                yield return new WaitForSeconds(0.3f);
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

        Rigidbody itemRigidbody = newItem.GetComponent<Rigidbody>();
        if (itemRigidbody == null)
        {
            Debug.LogError("ItemSpawner: 아이템 프리팹에 Rigidbody가 필요합니다.", newItem);
            Destroy(newItem);
            return false;
        }

        itemRigidbody.linearVelocity = Vector3.zero;
        itemRigidbody.angularVelocity = Vector3.zero;
        itemRigidbody.WakeUp();

        spawnedItems.Add(newItem);

        ConveyorItemMover mover = newItem.GetComponent<ConveyorItemMover>();
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

        // 불량 여부 결정
        bool isDefective = Random.value < defectiveProbability;

        Item item = newItem.GetComponent<Item>();
        if (item != null)
        {
            item.itemType = isDefective ? ItemType.Defective : ItemType.Normal;
            item.shouldBeGrabbed = !isDefective;
        }
        else
        {
            Debug.LogWarning("ItemSpawner: 아이템에 Item 컴포넌트가 없습니다.", newItem);
        }

        if (isDefective)
        {
            PartLoss partLoss = newItem.GetComponent<PartLoss>();
            if (partLoss != null)
            {
                partLoss.ApplyDefect(DefectType.PartLoss);
            }
        }

        return true;
    }

    /// <summary>
    /// ProcessController가 이전 아이템 퇴출(상자 도달/파괴) 완료 시 호출 (다음 사이클 부품 스폰 허용)
    /// </summary>
    public void NotifyItemCleared()
    {
        readyToSpawn = true;
        Debug.Log($"[ItemSpawner] 🔔 이전 부품 퇴출 완료 신호 수신 ➔ 다음 부품 스폰 준비 완료 (라인: {lineId})", this);

        // 스폰 코루틴이 멈춰있을 경우 즉시 재가동
        if (spawnRoutineInstance == null)
        {
            StartSpawnRoutine();
        }
    }

    // Reset: 생성된 아이템 정리 및 스포너 초기화
    public void ClearSpawned()
    {
        StopAllCoroutines();

        foreach (GameObject go in spawnedItems)
        {
            if (go != null) Destroy(go);
        }
        spawnedItems.Clear();
        currentSpawnCount = 0;
        readyToSpawn = true;

        if (itemPrefab != null && spawnPoint != null && conveyorManager != null)
            StartSpawnRoutine();
    }
}
