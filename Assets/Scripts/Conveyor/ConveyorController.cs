
using UnityEngine;

public class ConveyorController : MonoBehaviour
{
    [Header("컨베이어 정지 설정")]
    public bool stopConveyor = false;

    [Header("속도 설정")]
    public float normalSpeed = 2f;

    // private Renderer targetRenderer; 
    private Renderer[] targetRenderers;
    private MaterialPropertyBlock propertyBlock;

    //private readonly int speedID = Shader.PropertyToID("_Spped");
    private readonly int speedIDRotor = Shader.PropertyToID("_Spped");
    private readonly int speedIDBaseColor = Shader.PropertyToID("_Speed");

    private void Awake()
    {
        // 현재 오브젝트의 Renderer 가져오기
        // targetRenderer = GetComponent<Renderer>();
        targetRenderers = GetComponentsInChildren<Renderer>();

        // if (targetRenderer == null)
        if (targetRenderers.Length == 0)
        {
            Debug.LogError("Renderer를 찾을 수 없습니다.", this);
            return;
        }

        propertyBlock = new MaterialPropertyBlock();
    }

    private void Update()
    {
        ApplyConveyorSpeed();
    }

    // 셰이더에 컨베이어 속도 적용
    public void ApplyConveyorSpeed()
    {
        // if (targetRenderer == null || propertyBlock == null)
        //     return;

        // targetRenderer.GetPropertyBlock(propertyBlock);

        // float speed = stopConveyor ? 0f : normalSpeed;
        // propertyBlock.SetFloat(speedID, speed);

        // targetRenderer.SetPropertyBlock(propertyBlock);

        if (propertyBlock == null) return;

        float speed = stopConveyor ? 0f : normalSpeed;

        foreach (Renderer r in targetRenderers)
        {
            r.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(speedIDRotor, speed);
            propertyBlock.SetFloat(speedIDBaseColor, speed);
            r.SetPropertyBlock(propertyBlock);
        }
    }

    // StopperController에서 호출할 정지 함수
    public void StopConveyor()
    {
        stopConveyor = true;
        ApplyConveyorSpeed();

       
    }

    // 필요할 때 컨베이어 재가동
    public void ResumeConveyor()
    {
        stopConveyor = false;
        ApplyConveyorSpeed();

       
    }
}
