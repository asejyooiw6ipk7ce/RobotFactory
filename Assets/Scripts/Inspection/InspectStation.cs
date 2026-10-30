using System.Collections;
using UnityEngine;

// 부품 도착(이 스크립트가 감지) 
// -> 벨트 정지(ConveyorController.StopConveyor)
// -> 끝난 후 -> 스토퍼 올리기(PartStopper.Stop) -> 올라간 뒤 -> 검사 자세 이동(ArmIK.MoveTo)
// -> 검사(StopperController 끄기;OnTriggerExit 안쓰기 위해) -> 벨트 재개(ConveyorController.ResumeConveyor) -> 끝난 후 -> 스토퍼 내리기(PartStopper.Resume)

public class InspectStation : MonoBehaviour
{
    public ConveyorController belt;
    public PartStopper stopper;
    public ArmIK ik;

    [Header("검사 자세 좌표")]
    public Transform inspectPose;

    private bool isInspecting = false;
    public int robotId;

    [ContextMenu("Test Inspect")]
    void TestInspect()
    {
        StartCoroutine(InspectSequence());
    }

    [Header("상태 대시보드 반영")]
    public StationId stationId = StationId.Station1;

    [Tooltip("임시 판정 결과 (실제 판정 붙이기 전까지 사용)")]
    public InspectResult testResult = InspectResult.Pass;
    public DefectType testDefect = DefectType.AngleError;

    private int serial;

    //부품 검사지점 도착 감지
    public void OnTriggerEnter(Collider other)
    { 
        if(isInspecting) return;
        StartCoroutine(InspectSequence());
    }

    public IEnumerator InspectSequence()
    {
        // 부품 도착 감지
        isInspecting = true; 

        // 1. 벨트 정지
        belt.StopConveyor();

        // 2. 스토퍼 올리기'
        yield return stopper.Stop();

        // 3. 검사 자세 이동
        RobotStatusTracker.Set(robotId, RobotState.Moving);
        //Dashboard가 RobotStateChanged를 구독 -> Dashboard가 상태 저장하고 화면 갱신 ; robots.Apply() -> Refresh();
        yield return ik.MoveTo(inspectPose.position);

        // 4. 검사
        RobotStatusTracker.Set(robotId, RobotState.Working);
        yield return new WaitForSeconds(5f);  //TODO 검사
        //검사가 끝난 뒤 이벤트 발행
        var defect = testResult == InspectResult.Pass ? DefectType.None : testDefect;
        InspectEventBus.Publish(new InspectEvent($"P{++serial:0000}", stationId, testResult, defect, Time.time));

        // 5. 벨트 재개
        RobotStatusTracker.Set(robotId, RobotState.Idle);
        belt.ResumeConveyor();
        yield return stopper.Resume();

        isInspecting = false;
    }
}
