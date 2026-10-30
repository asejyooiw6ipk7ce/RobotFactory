using UnityEditor.Searcher;
using UnityEngine;

public enum ItemType
{
    Normal,
    Defective
}
public enum ItemState
{
    Idle, BeingCarried, Delivered
}

public class Item : MonoBehaviour
{
    public bool shouldBeGrabbed;
    public ItemType itemType;
    public ItemState state = ItemState.Idle;

}