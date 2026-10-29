using UnityEngine;

public class BaseInfo : PartBase
{
    public float floorHeight;
    public float topHeight;
    public Vector3 HoleCenter;
    public Transform socket;

    public bool Accepts(PartBase part)  //이 베이스에 이 부품을 끼울 수 있는가
    {
        //if (part.Kind == PartKind.Claw)
        {
            return true;
        }
        return false;
    }
}
