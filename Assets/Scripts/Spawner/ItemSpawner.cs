using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ItemSpawner : MonoBehaviour
{
    [Header("?Š¤?°?•  ?•„?´?…œ ?”„ë¦¬íŒ¹ ë°? ?œ„ì¹?")]
    [Tooltip("?Š¤?°?•  ?•„?´?…œ ?”„ë¦¬íŒ¹")]
    [SerializeField] private GameObject itemPrefab;

    [Tooltip("?•„?´?…œ?´ ?ƒ?„±?  ?œ„ì¹? Transform")]
    [SerializeField] private Transform spawnPoint;

    [Tooltip("?Š¤?° ê°„ê²©(ì´?)")]
    [SerializeField] private float spawnInterval = 2f;

    [Header("?Š¤?° ë°©í–¥ ë°? ?šŒ? „")]
    [Tooltip("?ƒ?„± ?‹œ ? ?š©?•  ì¶”ê?? ?šŒ? „ ?˜¤?”„?…‹")]
    [SerializeField] private Vector3 rotationOffset;

    [Tooltip("?´?™ ì§„í–‰ ë°©í–¥ Transform (ë¯¸ì??? • ?‹œ Spawner?˜ forward ?‚¬?š©)")]
    [SerializeField] private Transform moveDirectionPoint;

    [Header("ì»¨ë² ?´?–´ ?¼?¸ ?„¤? •")]
    [Tooltip("ì¤‘ì•™ ì»¨ë² ?´?–´ ë§¤ë‹ˆ??? ì°¸ì¡°")]
    [SerializeField] private ConveyorManager conveyorManager;

    [Tooltip("?Š¤?¬?„ˆê°? ?†?•œ ì»¨ë² ?´?–´ ?¼?¸ ID")]
    [SerializeField] private ConveyorLineId lineId;

    [Header("ë¶ˆëŸ‰ ?™•ë¥? ?„¤? •")]
    [Tooltip("ë¶ˆëŸ‰?’ˆ ?ƒ?„± ?™•ë¥? (0.0: ëª¨ë‘ ?–‘?’ˆ, 1.0: ëª¨ë‘ ë¶ˆëŸ‰)")]
    [SerializeField, Range(0f, 1f)] private float defectiveProbability = 0.5f;

    // ?˜„?¬ê¹Œì?? ?ƒ?„±?œ ?ˆ„?  ?•„?´?…œ ?ˆ˜
    private int currentSpawnCount;

    // ?‹¨?¼ ë¶??’ˆ ? •ì±?: ?˜„?¬ ?¼?¸?— ?™œ?„±?™”?œ ?•„?´?…œ?´ ?—†?„ ?•Œë§? true (1?šŒ 1ê°? ?ƒ?„±)
    private bool readyToSpawn = true;

    // ProcessController?—?„œ ?Š¤?° ê°??Š¥ ?—¬ë¶? ? œ?–´
    public bool AllowSpawn { get; set; } = true;

    public ConveyorLineId LineId => lineId;

    // Reset ?‹œ ?ŒŒê´´í•  ?ƒ?„± ?•„?´?…œ ëª©ë¡
    private readonly List<GameObject> spawnedItems = new List<GameObject>();

    private Coroutine spawnRoutineInstance;

    private void Start()
    {
        if (itemPrefab == null)
        {
            Debug.LogError("ItemSpawner: Item Prefab?´ ?„¤? •?˜ì§? ?•Š?•˜?Šµ?‹ˆ?‹¤.", this);
            return;
        }

        if (spawnPoint == null)
        {
            Debug.LogError("ItemSpawner: Spawn Pointê°? ?„¤? •?˜ì§? ?•Š?•˜?Šµ?‹ˆ?‹¤.", this);
            return;
        }

        if (conveyorManager == null)
        {
            Debug.LogError("ItemSpawner: ConveyorManagerê°? ?„¤? •?˜ì§? ?•Š?•˜?Šµ?‹ˆ?‹¤.", this);
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
            // ?‹¨?¼ ë¶??’ˆ ? •ì±?: ?´? „ ë¶??’ˆ?´ ?‡´ì¶œë˜?–´ readyToSpawn == true?´ê³?, ?¼?¸?´ ê°??™ ì¤‘ì¼ ?•Œë§? 1ê°? ?ƒ?„±
            if (readyToSpawn && conveyorManager.IsLineRunning(lineId) && AllowSpawn)
            {
                if (SpawnItem())
                {
                    currentSpawnCount++;
                    readyToSpawn = false; // ë¶??’ˆ?´ ?‡´ì¶œë  ?•Œê¹Œì?? ?‹¤?Œ ?ƒ?„± ì°¨ë‹¨ (ê³µì • ?‚´ 1ê°? ?œ ì§?)
                    ProcessCounters.Add(lineId == ConveyorLineId.LineA ? CounterType.InputBase : CounterType.InputClaw);
                    Debug.Log($"[ItemSpawner] ?œ¨ ?ƒˆ ë¶??’ˆ ?Š¤?° ?™„ë£?! ?¼?¸: {lineId}, ?ˆ„? : {currentSpawnCount}ê°?", this);
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
            Debug.LogError("ItemSpawner: ?•„?´?…œ ?”„ë¦¬íŒ¹?— Rigidbodyê°? ?•„?š”?•©?‹ˆ?‹¤.", newItem);
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

        // ë¶ˆëŸ‰ ?—¬ë¶? ê²°ì •
        bool isDefective = Random.value < defectiveProbability;

        Item item = newItem.GetComponent<Item>();
        if (item != null)
        {
            item.itemType = isDefective ? ItemType.Defective : ItemType.Normal;
            item.shouldBeGrabbed = !isDefective;
        }
        else
        {
            Debug.LogWarning("ItemSpawner: ?•„?´?…œ?— Item ì»´í¬?„Œ?Š¸ê°? ?—†?Šµ?‹ˆ?‹¤.", newItem);
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
    /// ProcessControllerê°? ?´? „ ?•„?´?…œ ?‡´ì¶?(?ƒ? ?„?‹¬/?ŒŒê´?) ?™„ë£? ?‹œ ?˜¸ì¶? (?‹¤?Œ ?‚¬?´?´ ë¶??’ˆ ?Š¤?° ?—ˆ?š©)
    /// </summary>
    public void NotifyItemCleared()
    {
        readyToSpawn = true;
        Debug.Log($"[ItemSpawner] ?Ÿ”? ?´? „ ë¶??’ˆ ?‡´ì¶? ?™„ë£? ?‹ ?˜¸ ?ˆ˜?‹  ?” ?‹¤?Œ ë¶??’ˆ ?Š¤?° ì¤?ë¹? ?™„ë£? (?¼?¸: {lineId})", this);

        // ?Š¤?° ì½”ë£¨?‹´?´ ë©ˆì¶°?ˆ?„ ê²½ìš° ì¦‰ì‹œ ?¬ê°??™
        if (spawnRoutineInstance == null)
        {
            StartSpawnRoutine();
        }
    }

    // Reset: ?ƒ?„±?œ ?•„?´?…œ ? •ë¦? ë°? ?Š¤?¬?„ˆ ì´ˆê¸°?™”
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
