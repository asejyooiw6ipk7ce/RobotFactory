using System.Collections;
using UnityEngine;

//InspectStation에서 StartCoroutine(conveyor.Stop());

public class PartStopper : MonoBehaviour
{
    [Header("스토퍼")]
    public Transform stopper;
    public float stopperUpY = 1.01607f; //올렸을 때 높이(Y). 부품을 막는 위치
    public float stopperDownY = 0.704f;  //내렸을 때 높이. 부품이 위로 지나갈 수 있는 위치
    public float stopperMoveTime = 0.3f; //올리고 내리는 데 걸리는 시간

    [Header("움직이는 중인지")]
    private bool stopperMoving;

    public bool StopperUp { get; private set; } = false;  //스토퍼가 지금 올라가 있는지 (true:올라가있음 , false:내려가 있음)

    private void Start()
    {
        if (stopper == null) return;

        //StopperUp = false로 시작 = 스토퍼 내린 채 시작
        stopper.position = new Vector3(stopper.position.x, stopperDownY, stopper.position.z);
    }
    
    //스토퍼를 부드럽게 올리거나 내리는 코루틴 (동작)
    private IEnumerator MoveStopper(bool up)  
    {
        // 참고 : Factory/Cell/ConveyorLine.cs
        if (stopper == null) yield break;

        //1. 움직이는 중임을 표시
        stopperMoving = true;

        //출발 높이에서 목표 높이까지
        float from = stopper.position.y;
        float to = up ? stopperUpY : stopperDownY;

        //moveTime동안 조금씩 보간
        float elapsed = 0f;
        while (elapsed < stopperMoveTime)
        {
            elapsed += Time.deltaTime;

            float newY = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / stopperMoveTime));
            stopper.position = new Vector3(stopper.position.x,newY, stopper.position.z);
            yield return null;
        }

        //한번 더 목표 높이로 정확히 맞춤
        stopper.position = new Vector3(stopper.position.x, to, stopper.position.z);

        //상태를 갱신
        StopperUp = up;
        stopperMoving = false;
    }

    // 부품이 멈출 때 무엇을 해야하나 - 이미 올라가 있음 무시, 아니면 올림
    public IEnumerator Stop()  //이미 올라가 있음 할 일이 없음
    {
        //내려가는 중에 Stop()이 무시되는 문제 방지
        while (stopperMoving) yield return null;  

        //이미 올라가 있으면 yield break
        if (stopperMoving || StopperUp) yield break;

        //그 외는 yield return MoveStopper(true);
        yield return MoveStopper(true);
    }

    // 부품을 보낼 때 무엇을 해야하나 - 이미 내려가 있으면 무시, 아니면 내림
    public IEnumerator Resume()
    {
        if (stopperMoving || !StopperUp) yield break;
        yield return MoveStopper(false);
    }
}
