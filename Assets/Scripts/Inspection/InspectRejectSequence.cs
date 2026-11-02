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
            // Debug.Log($"[Reject] pick={pick} tip={ik.tip.position}  posErr={ik.PositionError:F4}  angleErr={ik.AngleError:F1}");
            
            //[Debug test] 목표를 준 직후, 관절 별로 실제 각도와 목표 각도 로그 출력
            // 모든 관절 중 어깨 관절이 오차 제일 큼(65도)
            // for (int i = 0; i < ik.joints.Length; i++)
            // {
            //     var jb = ik.joints[i];
            //     if (jb.dofCount == 0) continue;
            //     Debug.Log($"[Joint {i} {jb.name}] actual={jb.jointPosition[0] * Mathf.Rad2Deg:F1}  target={jb.xDrive.target:F1}  lower={jb.xDrive.lowerLimit:F1}  upper={jb.xDrive.upperLimit:F1} ");
            //     Debug.Log($"[Joint {i} {jb.name}] stiffness={jb.xDrive.stiffness:F0}  damping={jb.xDrive.damping:F0}  forceLimit={jb.xDrive.forceLimit:F0}");
            //     // actual : 실제 관절 각도(도) , target : IK가 요청한 목표 각도(도) , lower/upper: 관절 한계 각도(도)
            //     // stiffness: 목쵸 각도로 세게 밀어주는 강도 , damping: 흔들림 잡아주는 정도 , forceLimit: 최대 힘
            // }

            //어깨 관절
            var jb = ik.joints[0];
            Debug.Log($"[Joint {0} {jb.name}] force={jb.jointForce[0]:F0}");
            Debug.Log($"[Joint {0} {jb.name}] actual={jb.jointPosition[0] * Mathf.Rad2Deg:F1}  target={jb.xDrive.target:F1}  lower={jb.xDrive.lowerLimit:F1}  upper={jb.xDrive.upperLimit:F1} ");

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
