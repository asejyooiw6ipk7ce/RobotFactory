using UnityEngine;

public class RejectController : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        // 콜라이더가 자식 오브젝트에 있어도 moto_base 루트 오브젝트를 찾아 삭제합니다.
        Transform current = other.transform;
        Transform motoBase = null;

        while (current != null)
        {
            if (current.name.ToLowerInvariant().Contains("moto_base"))
            {
                motoBase = current;
                break;
            }

            current = current.parent;
        }

        if (motoBase != null)
        {
            ProcessCounters.Add(CounterType.Discarded);
            Destroy(motoBase.gameObject);
        }
    }
}