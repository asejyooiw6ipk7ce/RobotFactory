using UnityEngine;

public abstract class PartBase : MonoBehaviour
{
    public string id;
    public DefectType defectType;
    public abstract PartKind Kind {get;}
}
