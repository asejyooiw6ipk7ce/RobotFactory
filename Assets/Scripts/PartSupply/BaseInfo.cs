using UnityEngine;

public class BaseInfo : PartBase
{
    public float floorHeight;
    public float topHeight;
    public Vector3 holeCenter;
    public Transform socket;
    public override PartKind Kind => PartKind.RobotModule;

    public bool Accepts(PartBase part)  //이 베이스에 이 부품을 끼울 수 있는가
    {
        return part.Kind == PartKind.Claw;
    }
}
