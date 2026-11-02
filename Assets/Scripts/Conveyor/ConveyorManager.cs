using System;
using UnityEngine;

public class ConveyorManager : MonoBehaviour
{
    [Serializable]
    private class ConveyorLine
    {
        [Tooltip("이 설정이 관리할 라인 ID")]
        public ConveyorLineId lineId;

        [Tooltip("이 라인에 포함된 벨트 Renderer 전체")]
        public Renderer[] beltRenderers;

        [Tooltip("아이템이 이동할 경로. 진행 순서대로 지정합니다.")]
        public Transform[] waypoints;

        [Tooltip("도착 시 라인 전체를 멈출 웨이포인트 인덱스")]
        public int[] stopWaypointIndices;

        [Tooltip("이 라인의 벨트 애니메이션 및 아이템 이동 속도")]
        public float speed = 0.5f;

        // 현재 라인이 운전 상태인지 저장합니다.
        // 전체 일시정지 상태와 개별 라인 정지를 구분하는 데 사용합니다.
        [NonSerialized] public bool isRunning;
    }

    [Header("라인 설정")]

    // 라인마다 설정을 하나씩 등록합니다.
    // 같은 ID를 여러 개 만들지 말고, 한 라인의 Renderer와 경로를 한 설정에 모으세요.
    [SerializeField] private ConveyorLine[] lines;

    [Header("시작 설정")]

    // 체크하면 씬 시작 시 모든 라인을 운전합니다.
    [SerializeField] private bool startOnAwake;

    // 기존 셰이더의 프로퍼티 이름입니다.
    // 프로젝트 셰이더의 철자 "_Spped"를 그대로 사용합니다.
    private static readonly int SpeedIDRotor =
        Shader.PropertyToID("_Spped");

    private static readonly int SpeedIDBase =
        Shader.PropertyToID("_Speed");

    // 렌더러 머티리얼 속성을 갱신할 때 재사용합니다.
    private MaterialPropertyBlock propertyBlock;

    // 전체 일시정지 상태입니다.
    private bool isPaused;

    // 아이템이 정지 웨이포인트에 도착하면 작업 처리 쪽에 알립니다.
    // 매개변수: 라인 ID, 웨이포인트 인덱스, 도착한 아이템
    public event Action<ConveyorLineId, int, GameObject>
        OnItemArrivedAtStopPoint;

    public bool IsPaused => isPaused;

    private void Awake()
    {
        propertyBlock = new MaterialPropertyBlock();

        if (lines == null)
            return;

        // 시작 옵션을 각 라인에 적용합니다.
        foreach (ConveyorLine line in lines)
        {
            if (line == null)
                continue;

            line.isRunning = startOnAwake;
            ApplyLineSpeed(line);
        }
    }

    // ID에 해당하는 라인 설정을 찾습니다.
    private ConveyorLine FindLine(ConveyorLineId lineId)
    {
        if (lines != null)
        {
            foreach (ConveyorLine line in lines)
            {
                if (line != null && line.lineId == lineId)
                    return line;
            }
        }

        Debug.LogError(
            $"ConveyorManager: {lineId} 라인이 등록되지 않았습니다.",
            this);

        return null;
    }

    // 라인 상태와 속도를 셰이더에 적용합니다.
    private void ApplyLineSpeed(ConveyorLine line)
    {
        if (line == null || line.beltRenderers == null)
            return;

        // 정지 또는 전체 일시정지 상태에서는 0을 적용합니다.
        float shaderSpeed =
            line.isRunning && !isPaused
                ? Mathf.Max(0f, line.speed)
                : 0f;

        foreach (Renderer beltRenderer in line.beltRenderers)
        {
            if (beltRenderer == null)
                continue;

            // 기존 머티리얼 속성은 유지하고 컨베이어 속성만 변경합니다.
            beltRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(SpeedIDRotor, shaderSpeed);
            propertyBlock.SetFloat(SpeedIDBase, shaderSpeed);
            beltRenderer.SetPropertyBlock(propertyBlock);
        }
    }

    // 해당 라인의 현재 운전 상태를 반환합니다.
    public bool IsLineRunning(ConveyorLineId lineId)
    {
        ConveyorLine line = FindLine(lineId);

        return line != null &&
               line.isRunning &&
               !isPaused;
    }

    // 해당 라인이 움직이는 중이면 속도를 반환합니다.
    // 정지 또는 일시정지 상태면 0을 반환합니다.
    public float GetLineSpeed(ConveyorLineId lineId)
    {
        ConveyorLine line = FindLine(lineId);

        if (line == null || !line.isRunning || isPaused)
            return 0f;

        return Mathf.Max(0f, line.speed);
    }

    // 이동기가 사용할 경로를 반환합니다.
    public Transform[] GetLineWaypoints(ConveyorLineId lineId)
    {
        ConveyorLine line = FindLine(lineId);
        return line != null ? line.waypoints : null;
    }

    // 지정 인덱스가 라인 정지 지점인지 확인합니다.
    public bool IsStopWaypoint(ConveyorLineId lineId, int waypointIndex)
    {
        ConveyorLine line = FindLine(lineId);

        if (line == null || line.stopWaypointIndices == null)
            return false;

        foreach (int stopIndex in line.stopWaypointIndices)
        {
            if (stopIndex == waypointIndex)
                return true;
        }

        return false;
    }

    // 아이템이 정지 지점에 도착했을 때 호출합니다.
    // 라인을 먼저 정지시킨 뒤 작업 처리 쪽에 이벤트를 보냅니다.
    public void NotifyItemArrivedAtStopPoint(
        ConveyorLineId lineId,
        int waypointIndex,
        GameObject item)
    {
        StopLine(lineId);

        OnItemArrivedAtStopPoint?.Invoke(
            lineId,
            waypointIndex,
            item);
    }

    // 작업이 끝난 뒤 대기 중인 아이템을 해제하고 라인을 재가동합니다.
    public void ReleaseItemAndRunLine(
        ConveyorLineId lineId,
        ConveyorItemMover mover)
    {
        if (mover != null)
            mover.ReleaseWaitingAtStopPoint();

        RunLine(lineId);
    }

    // 특정 라인 하나를 운전합니다.
    public void RunLine(ConveyorLineId lineId)
    {
        ConveyorLine line = FindLine(lineId);
        if (line == null)
            return;

        line.isRunning = true;
        ApplyLineSpeed(line);
    }

    // 특정 라인 하나를 정지합니다.
    public void StopLine(ConveyorLineId lineId)
    {
        ConveyorLine line = FindLine(lineId);
        if (line == null)
            return;

        line.isRunning = false;
        ApplyLineSpeed(line);
    }

    // 등록된 모든 라인을 운전합니다.
    public void StartAll()
    {
        isPaused = false;

        if (lines == null)
            return;

        foreach (ConveyorLine line in lines)
        {
            if (line == null)
                continue;

            line.isRunning = true;
            ApplyLineSpeed(line);
        }
    }

    // 등록된 모든 라인을 정지합니다.
    public void StopAll()
    {
        if (lines == null)
            return;

        foreach (ConveyorLine line in lines)
        {
            if (line == null)
                continue;

            line.isRunning = false;
            ApplyLineSpeed(line);
        }
    }

    // 각 라인의 운전 상태를 보존한 채 전체 일시정지합니다.
    public void PauseAll()
    {
        isPaused = true;

        if (lines == null)
            return;

        foreach (ConveyorLine line in lines)
            ApplyLineSpeed(line);
    }

    // 전체 일시정지를 해제합니다.
    // 개별적으로 정지해 둔 라인은 계속 정지 상태를 유지합니다.
    public void ResumeAll()
    {
        isPaused = false;

        if (lines == null)
            return;

        foreach (ConveyorLine line in lines)
            ApplyLineSpeed(line);
    }

    // 특정 라인의 속도를 설정합니다.
    public void SetLineSpeed(ConveyorLineId lineId, float speed)
    {
        ConveyorLine line = FindLine(lineId);
        if (line == null)
            return;

        line.speed = Mathf.Max(0f, speed);
        ApplyLineSpeed(line);
    }

    // 모든 라인의 속도를 같은 값으로 설정합니다.
    public void SetAllSpeed(float speed)
    {
        if (lines == null)
            return;

        foreach (ConveyorLine line in lines)
        {
            if (line == null)
                continue;

            line.speed = Mathf.Max(0f, speed);
            ApplyLineSpeed(line);
        }
    }

    // 선택된 매니저의 라인 경로를 Scene 뷰에 표시합니다.
    private void OnDrawGizmosSelected()
    {
        if (lines == null)
            return;

        foreach (ConveyorLine line in lines)
        {
            if (line == null || line.waypoints == null)
                continue;

            Gizmos.color = Color.cyan;

            for (int i = 0; i < line.waypoints.Length - 1; i++)
            {
                if (line.waypoints[i] != null &&
                    line.waypoints[i + 1] != null)
                {
                    Gizmos.DrawLine(
                        line.waypoints[i].position,
                        line.waypoints[i + 1].position);
                }
            }

            // 정지 지점은 노란 구체로 표시합니다.
            if (line.stopWaypointIndices == null)
                continue;

            Gizmos.color = Color.yellow;

            foreach (int stopIndex in line.stopWaypointIndices)
            {
                if (stopIndex >= 0 &&
                    stopIndex < line.waypoints.Length &&
                    line.waypoints[stopIndex] != null)
                {
                    Gizmos.DrawSphere(
                        line.waypoints[stopIndex].position,
                        0.08f);
                }
            }
        }
    }
}