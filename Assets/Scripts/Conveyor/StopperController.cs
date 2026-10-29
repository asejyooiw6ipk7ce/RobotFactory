
using UnityEngine;

public class StopperController : MonoBehaviour
{
    private ConveyorController conveyorController;

    private void Awake()
    {
        Debug.Log("[Stopper] Awake 실행");

        if (transform.parent != null)
        {
            conveyorController =
                transform.parent.GetComponentInChildren<ConveyorController>();
        }

        if (conveyorController == null)
        {
            Debug.LogError("[Stopper] ConveyorController를 찾지 못했습니다.");
        }
        else
        {
            Debug.Log("[Stopper] ConveyorController 찾기 성공");
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        Debug.Log($"[Stopper] OnTriggerEnter 실행: {other.name}");

        if (conveyorController == null)
        {
            Debug.LogError("[Stopper] ConveyorController가 null입니다.");
            return;
        }

        conveyorController.StopConveyor();
    }

    private void OnTriggerExit(Collider other)
    {
        Debug.Log($"[Stopper] OnTriggerExit 실행: {other.name}");

        if (conveyorController == null)
            return;

        conveyorController.ResumeConveyor();
    }
}
