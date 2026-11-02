using UnityEngine;

public class ConveyorItemMover : MonoBehaviour
{
    [Header("이동 설정")]
    [SerializeField]
    private float moveSpeed = 2f;

    private ConveyorController conveyorController;
    private Vector3 moveDirection;
    


    // 스포너에서 컨베이어 정보 전달
    public void Initialize(ConveyorController controller, Vector3 direction)
    {
        conveyorController = controller;
        moveDirection = direction.normalized;
        Debug.Log("이동 초기화 완료: " + moveDirection);
    }

    private void Update()
    {
        

        if (conveyorController == null)
        {
            Debug.LogWarning("ConveyorController 연결 안 됨");
            return;
        }

        if (conveyorController.isConveyorRunning)
        {
            Debug.Log("컨베이어 정지 상태");
            return;
        }

        Item item = GetComponent<Item>();

        if (item != null && item.state != ItemState.Idle)
        {
            Debug.Log("아이템 운반 상태");
            return;
        }

        transform.position += moveDirection * moveSpeed * Time.deltaTime;
    }
}