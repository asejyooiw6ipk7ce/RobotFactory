using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 완제품/부품이 폐기 상자(RejectBox) 또는 출하 상자(ShippingBox)에 도달했을 때 처리하는 종단 컨트롤러
/// - 상자 내부 트리거(Trigger) 진입 시 완제품의 최상위 루트를 탐색하여 카운터 증가 및 오브젝트 파괴(Destroy)
/// - 파괴 완료 시 OnProductCleared 이벤트를 발행하여 ProcessController가 다음 부품을 스폰할 수 있도록 알림
/// </summary>
public class RejectController : MonoBehaviour
{
    [Header("처리 카운터 설정")]
    [Tooltip("상자에 도달했을 때 증가시킬 카운터 (폐기 상자: Discarded, 출하 상자: Shipped)")]
    [SerializeField] private CounterType counterType = CounterType.Discarded;

    /// <summary>
    /// 완제품이 상자에 도착해 완전히 파괴(퇴출)되었을 때 발생하는 정적 이벤트
    /// </summary>
    public static event System.Action<CounterType> OnProductCleared;

    // 중복 처리(파괴 중 다중 충돌 이벤트) 방지용 집합
    private readonly HashSet<GameObject> processedObjects = new HashSet<GameObject>();

    private void Awake()
    {
        // 1. 유니티 물리 규칙: 충돌/트리거 이벤트를 안정적으로 수신하기 위해 Rigidbody(Kinematic) 확인/부착
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        // 2. 상자 내부 감지용 BoxCollider(Trigger) 설정
        // - 중요: 상자 바깥으로 트리거가 삐져나가 컨베이어를 지나가는 물체를 잘못 감지하지 않도록
        //   벽면 안쪽(가로/세로 80% 크기)으로 안전하게 축소하고 내부 높이만 형성합니다.
        BoxCollider boxCol = GetComponent<BoxCollider>();
        if (boxCol != null)
        {
            boxCol.isTrigger = true;
            float safeX = (boxCol.size.x >= 9f) ? 8.0f : boxCol.size.x * 0.85f;
            float safeZ = (boxCol.size.z >= 9f) ? 8.0f : boxCol.size.z * 0.85f;
            float safeHeight = 2.0f;

            boxCol.size = new Vector3(safeX, safeHeight, safeZ);
            boxCol.center = new Vector3(boxCol.center.x, safeHeight * 0.5f, boxCol.center.z);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other == null) return;
        ProcessIncomingObject(other);
    }

    /// <summary>
    /// 상자 내부로 낙하 진입한 물체가 완제품/부품인지 식별 후 카운터 집계, 오브젝트 파괴, 이벤트 발행 수행
    /// </summary>
    public void ProcessIncomingObject(Collider col)
    {
        if (col == null) return;

        GameObject targetRoot = FindTargetRoot(col);
        if (targetRoot == null) return;

        // 이미 처리 중이거나 비활성화된 오브젝트 중복 처리 방지
        if (processedObjects.Contains(targetRoot) || !targetRoot.activeSelf) return;
        processedObjects.Add(targetRoot);

        // 카운터 증가 (Discarded 또는 Shipped)
        ProcessCounters.Add(counterType);
        Debug.Log($"[RejectController] 📦 완제품/부품 '{targetRoot.name}' 상자 도달 감지! ➔ 카운터: {counterType} +1, 오브젝트 즉시 파괴(Destroy) 완료", this);

        // 추가 물리 충돌 방지를 위해 즉시 비활성화 후 오브젝트 파괴
        targetRoot.SetActive(false);
        Destroy(targetRoot);

        // 공정 컨트롤러에 완제품 퇴출 완료 알림 (다음 사이클 부품 스폰 트리거)
        OnProductCleared?.Invoke(counterType);
    }

    /// <summary>
    /// 콜라이더로부터 BaseInfo, Item 컴포넌트를 가진 완제품 루트 오브젝트 탐색
    /// </summary>
    private GameObject FindTargetRoot(Collider col)
    {
        if (col == null) return null;

        // 1. BaseInfo 컴포넌트 기준 (완제품 차체 루트)
        BaseInfo baseInfo = col.GetComponentInParent<BaseInfo>();
        if (baseInfo != null) return baseInfo.gameObject;

        // 2. Item 컴포넌트 기준
        Item item = col.GetComponentInParent<Item>();
        if (item != null) return item.gameObject;

        // 3. 최상위 루트 이름에 moto_base가 포함된 경우
        if (col.transform.root != null && col.transform.root.name.ToLowerInvariant().Contains("moto_base"))
            return col.transform.root.gameObject;

        // 4. 부모 계층 역추적 탐색
        Transform current = col.transform;
        while (current != null)
        {
            if (current.name.ToLowerInvariant().Contains("moto_base"))
            {
                return current.gameObject;
            }
            current = current.parent;
        }

        return null;
    }
}
