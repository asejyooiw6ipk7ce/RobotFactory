using UnityEngine;

public class LineCRejectController : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        Debug.Log($"RejectBox 트리거 진입: {other.name}", this);

        // 충돌한 오브젝트부터 부모 방향으로 올라가며 moto_base를 찾습니다.
        Transform current = other.transform;

        while (current != null)
        {
            if (current.name.ToLowerInvariant().Contains("moto_base"))
            {
                Debug.Log($"삭제 대상: {current.name}", this);
                Destroy(current.gameObject);
                
                return;
            }

            current = current.parent;
        }

        Debug.Log($"moto_base 부모를 찾지 못함: {other.name}", this);
    }
}