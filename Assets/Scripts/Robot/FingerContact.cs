using UnityEngine;

// 손가락 하나가 지금 부품에 닿았는지 알려주는 역할
public class FingerContact : MonoBehaviour
{
    public float holdTime = 0.1f;

    private Rigidbody touching; // 마지막으로 닿은 부품
    private float lastTouchTime = -1f; // 마지막으로 닿은 물리 시간 (-1은 한 번도 안 닿은 것)

    public Rigidbody TouchingPart
    {
        get
        {
            if (touching == null || Time.fixedTime - lastTouchTime > holdTime) return null;
            return touching;
        }
    }

    void OnCollisionEnter(Collision collision) => Record(collision);

    void OnCollisionStay(Collision collision)
    {
        Record(collision);
    }

    private void Record(Collision collision)
    {
        if (collision == null || collision.collider == null) return;

        // 부품 본체 가져오기 (자식 콜라이더인 경우 attachedRigidbody 참조)
        Rigidbody rb = collision.collider.attachedRigidbody;

        if (rb != null)
        {
            // 로봇 자체 부품이 아닌 외부 오브젝트(부품)와의 충돌이면 감지
            if (rb.transform.root != transform.root)
            {
                touching = rb;
                lastTouchTime = Time.fixedTime;
            }
        }
    }

    public void Clear()
    {
        touching = null;
        lastTouchTime = -1;
    }
}
