using UnityEngine;

/// <summary>
/// 완제품/부품이 폐기 상자(RejectBox) 또는 출하 상자(ShippingBox)에 도달했을 때 처리하는 종단 컨트롤러
/// - 콜라이더 트리거 진입 시 완제품의 최상위 루트를 탐색하여 카운터 증가 및 오브젝트 파괴(Destroy)
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

    private void OnTriggerEnter(Collider other)
    {
        if (other == null) return;

        GameObject targetRoot = FindTargetRoot(other);

        if (targetRoot != null)
        {
            // 카운터 증가 (Discarded 또는 Shipped)
            ProcessCounters.Add(counterType);
            Debug.Log($"[RejectController] 📦 완제품 '{targetRoot.name}' 상자 도달 ➔ 카운터: {counterType} +1, 오브젝트 삭제 처리", this);

            // 오브젝트 파괴
            Destroy(targetRoot);

            // 공정 컨트롤러에 완제품 퇴출 완료 알림 (다음 사이클 부품 스폰 트리거)
            OnProductCleared?.Invoke(counterType);
        }
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

        // 3. 폴백: moto_base 문자열 탐색
        Transform current = col.transform;
        Transform fallback = null;
        while (current != null)
        {
            if (current.name.ToLowerInvariant().Contains("moto_base"))
            {
                fallback = current;
                break;
            }
            current = current.parent;
        }

        return fallback != null ? fallback.gameObject : null;
    }
}
