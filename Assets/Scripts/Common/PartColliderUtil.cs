using UnityEngine;

public class PartColliderUtil 
{
    /// <summary>
    /// 폐기 낙하가 시작될 때 부품의 BoxCollider를 켠다.
    /// Part1 프리팹은 이송 중 충돌을 막으려고 BoxCollider가 꺼져 있으므로,
    /// 폐기 상자 RejectController의 트리거가 감지하려면 낙하 시점에 켜야 한다.
    /// </summary>
    public static void EnableDropCollider(Transform part)
    {
        if (part == null) return;

        BoxCollider box = part.GetComponent<BoxCollider>();
        if (box == null) box = part.GetComponentInChildren<BoxCollider>(true);

        if (box != null)
        {
            box.enabled = true;
        }
    }
}
