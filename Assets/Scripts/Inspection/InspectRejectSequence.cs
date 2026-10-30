using System.Collections;
using UnityEngine;

public class InspectRejectSequence : MonoBehaviour
{
    public ArmIK ik;
    public GripperController gripper;

    public Transform pickPoint;   //불량 부품이 있는 위치
    public Transform rejectPose;  //폐기 박스 위 놓는 위치

    public float liftHeight = 0.1f; //집기,놓기 전후로 위로 올라갈 높이 (m)
    public float moveSpeed = 0.2f; //직선 이동 속도

    [Header("마지막 폐기 결과")]
    public float maxGraspTries = 100;
    public bool LastRejectOK {get; private set;}  //true: 폐기 성공 , false: 잡기 실패 등으로 폐기 실패

    //검사 결과 fail
    public IEnumerator Reject()
    {
        LastRejectOK = false; //시작할 때는 실패로 둠

        Vector3 pick = pickPoint.position;
        Vector3 pickAbove = pick + Vector3.up * liftHeight;
        Vector3 reject = rejectPose.position;
        Vector3 rejectAbove = reject + Vector3.up * liftHeight;
        //1. 집을 위치 위로 이동
        yield return ik.MoveTo(pickAbove);

        bool grasped = false;

        for(int attempt = 1;attempt <= maxGraspTries; attempt++)
        {
            //2. 내려가
            yield return ik.MoveLinear(pick, moveSpeed);

            //3. gripper.Close() ; 잡기
            yield return gripper.Close();

            //LastGraspOK 확인
            if(gripper.LastGraspOK)
            {
                grasped = true;
                break;
            }

            //잡기 실패시 다시 시도
            Debug.LogWarning($"[Reject] 잡기 실패 ({attempt}/{maxGraspTries}) - 다시 시도");
            yield return gripper.Open();
            yield return ik.MoveLinear(pickAbove, moveSpeed);
        }

        //다시시도 횟수 초과
        if (!grasped)
        {
            Debug.LogError("[Reject] 잡기 최대 횟수 초과 - 폐기 실패");
            yield return ik.MoveLinear(pickAbove, moveSpeed);
            yield return ik.ReturnToHomeJoints();
            yield break;   // 폐기 실패로 끝냄
        }

        //4. 올라가
        yield return ik.MoveLinear(pickAbove, moveSpeed);

        //5. rejectPose 위로 이동
        yield return ik.MoveTo(rejectAbove);

        //6. 내려가
        yield return ik.MoveLinear(reject, moveSpeed);

        //7. gripper.Open() ; 놓기
        yield return gripper.Open();

        //8. 올라오기
        yield return ik.MoveLinear(pickAbove, moveSpeed);

        //9. 카운트 ; ProcessCounters.Add(CounterType.Discarded)
        yield return ik.MoveLinear(rejectAbove, moveSpeed);
        ProcessCounters.Add(CounterType.Discarded);

        //10. 대기 자세로 복귀
        yield return ik.ReturnToHomeJoints();

        LastRejectOK = true; //모든 단계가 끝났을 때만 성공으로 바꿈
    }

}
