using UnityEngine;

public class PartLoss : MonoBehaviour
{
    public GameObject[] removableParts;
    public int removeIndex = 0;
    public void ApplyDefect(DefectType type)
    {
        if(type == DefectType.PartLoss)
        {
            if(type != DefectType.PartLoss) return;
            if(removeIndex < 0 || removeIndex >= removableParts.Length || removableParts[removeIndex] == null) return;
            removableParts[removeIndex].SetActive(false);  
        }
    }
}
