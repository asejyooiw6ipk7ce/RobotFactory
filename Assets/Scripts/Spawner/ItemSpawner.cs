using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ItemSpawner : MonoBehaviour
{
    [Header("������ ������ ����")]

    // ������ ������ �������Դϴ�.
    [SerializeField] private GameObject itemPrefab;

    // �������� ������ ��ġ�Դϴ�.
    [SerializeField] private Transform spawnPoint;

    // ������ �ִ� ������ ���Դϴ�.
    [SerializeField] private int maxCount = 20;

    // ������ ���� �����Դϴ�.
    [SerializeField] private float spawnInterval = 2f;

    [Header("������ ���� ����")]

    // ���� ȸ���� ��� ���⿡ ������ �������Դϴ�.
    [SerializeField] private Vector3 rotationOffset;

    // �����ϸ� �� Transform�� forward ������ �̵� �������� ����մϴ�.
    // ����θ� ItemSpawner�� forward ������ ����մϴ�.
    [SerializeField] private Transform moveDirectionPoint;

    [Header("�߾� �����̾� �Ŵ��� ����")]

    // ���� ��ġ�� ConveyorManager�Դϴ�.
    [SerializeField] private ConveyorManager conveyorManager;

    // �� �����ʰ� �������� ������ �����Դϴ�.
    [SerializeField] private ConveyorLineId lineId;

    // �ҷ�ǰ�� ������ Ȯ���Դϴ�. 0.5�� 50%�Դϴ�.
    [Header("�ҷ�ǰ ����")]
    [SerializeField, Range(0f, 1f)] private float defectiveProbability = 0.5f;

    // ���ݱ��� ������ ������ ���Դϴ�.
    private int currentSpawnCount;

    // ���� ������ ó���� ������ true�� �Ǿ� ���� �������� 1�� �����Ѵ�
    private bool readyToSpawn = true;

    // ProcessController sets this. When false, no new item is created (line still runs)
    public bool AllowSpawn { get; set; } = true;

    public ConveyorLineId LineId => lineId;

    // Items spawned by this spawner (so Reset can remove them)
    private readonly List<GameObject> spawnedItems = new List<GameObject>();

    private void Start()
    {
        if (itemPrefab == null)
        {
            Debug.LogError(
                "ItemSpawner: Item Prefab�� ������� �ʾҽ��ϴ�.",
                this);
            return;
        }

        if (spawnPoint == null)
        {
            Debug.LogError(
                "ItemSpawner: Spawn Point�� ������� �ʾҽ��ϴ�.",
                this);
            return;
        }

        if (conveyorManager == null)
        {
            Debug.LogError(
                "ItemSpawner: ConveyorManager�� ������� �ʾҽ��ϴ�.",
                this);
            return;
        }

        StartCoroutine(SpawnRoutine());
    }

    private IEnumerator SpawnRoutine()
    {
        while (currentSpawnCount < maxCount)
        {
            // ������ ������ ���� ���� ���� �����մϴ�.
            if (readyToSpawn && conveyorManager.IsLineRunning(lineId) && AllowSpawn)
            {
                if (SpawnItem())
                {
                    currentSpawnCount++;
                    readyToSpawn = false;
                }

                yield return new WaitForSeconds(spawnInterval);
            }
            else
            {
                // ������ ���� ���̸� ������ ��ٸ��ϴ�.
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

        // ���� �̵��� ���� Rigidbody�� ������ ��Ʈ���� �����ɴϴ�.
        Rigidbody itemRigidbody =
            newItem.GetComponent<Rigidbody>();

        if (itemRigidbody == null)
        {
            Debug.LogError(
                "ItemSpawner: ������ ������ ��Ʈ�� Rigidbody�� �ʿ��մϴ�.",
                newItem);

            Destroy(newItem);
            return false;
        }

        // ���� ���� ���� �ӵ��� �ʱ�ȭ�մϴ�.
        itemRigidbody.linearVelocity = Vector3.zero;
        itemRigidbody.angularVelocity = Vector3.zero;
        itemRigidbody.WakeUp();

        // �����տ� �̵��Ⱑ ������ �߰��մϴ�.
        spawnedItems.Add(newItem);

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

        // �ҷ� ���θ� �� ���� �������� ���մϴ�.
        bool isDefective = Random.value < defectiveProbability;

        // ���� Pusher�� ItemType�� Ȯ���ϹǷ� ���� ����� ����մϴ�.
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
                "ItemSpawner: ������ ������ ��Ʈ�� Item ������Ʈ�� �����ϴ�.",
                newItem);
        }

        // �ҷ�ǰ�̸� PartLoss�� ������ ��ǰ�� ��Ȱ��ȭ�մϴ�.
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
                    "ItemSpawner: �ҷ� �������ε� PartLoss ������Ʈ�� ã�� ���߽��ϴ�.",
                    newItem);
            }
        }

        return true;
    }

    // ProcessController�� ���� ������ ó��(��� �Ǵ� ���) �Ϸ� �� ȣ��
    public void NotifyItemCleared()
    {
        readyToSpawn = true;
    }

    // Reset: remove spawned items, clear the count, and restart spawning
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
            StartCoroutine(SpawnRoutine());
    }
}