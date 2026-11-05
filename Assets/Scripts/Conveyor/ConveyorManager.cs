using System;
using System.Collections;
using UnityEngine;

public class ConveyorManager : MonoBehaviour
{
    [Serializable]
    public class ConveyorLine
    {
        [Tooltip("해당 컨베이어 라인의 식별 ID")]
        public ConveyorLineId lineId;

        [Tooltip("이 라인에 포함된 벨트 Renderer 컴포넌트 목록")]
        public Renderer[] beltRenderers;

        [Tooltip("경로 웨이포인트 목록. 순서대로 이동합니다.")]
        public Transform[] waypoints;

        [Tooltip("정지해야 하는 웨이포인트의 인덱스 목록")]
        public int[] stopWaypointIndices;

        [Tooltip("해당 라인의 정지점에 위치한 스토퍼 컴포넌트 목록")]
        public PartStopper[] stoppers;

        [Tooltip("이 라인의 벨트 텍스처 애니메이션 및 물체 이동 속도")]
        public float speed = 0.5f;

        // 현재 라인이 가동 중인지 여부
        [NonSerialized] public bool isRunning;
    }

    [Header("컨베이어 라인 목록")]
    [SerializeField] private ConveyorLine[] lines;

    [Header("초기 동작 설정")]
    [Tooltip("체크하면 씬 시작 시 모든 라인이 자동으로 가동됩니다.")]
    [SerializeField] private bool startOnAwake = true;

    // 셰이더 프로퍼티 ID
    private static readonly int SpeedIDRotor = Shader.PropertyToID("_Spped");
    private static readonly int SpeedIDBase = Shader.PropertyToID("_Speed");

    private MaterialPropertyBlock propertyBlock;
    private bool isPaused;

    // 아이템이 정지 웨이포인트에 도착했을 때 외부에 알리는 이벤트 (라인 ID, 웨이포인트 인덱스, 도착 아이템)
    public event Action<ConveyorLineId, int, GameObject> OnItemArrivedAtStopPoint;

    public bool IsPaused => isPaused;

    private void Awake()
    {
        propertyBlock = new MaterialPropertyBlock();

        if (lines == null)
            return;

        foreach (ConveyorLine line in lines)
        {
            if (line == null)
                continue;

            line.isRunning = startOnAwake;
            ApplyLineSpeed(line);
        }
    }

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

        Debug.LogError($"ConveyorManager: {lineId} 라인을 찾을 수 없습니다.", this);
        return null;
    }

    private void ApplyLineSpeed(ConveyorLine line)
    {
        if (line == null || line.beltRenderers == null)
            return;

        float shaderSpeed = line.isRunning && !isPaused
            ? Mathf.Max(0f, line.speed)
            : 0f;

        foreach (Renderer beltRenderer in line.beltRenderers)
        {
            if (beltRenderer == null)
                continue;

            beltRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(SpeedIDRotor, shaderSpeed);
            propertyBlock.SetFloat(SpeedIDBase, shaderSpeed);
            beltRenderer.SetPropertyBlock(propertyBlock);
        }
    }

    public bool IsLineRunning(ConveyorLineId lineId)
    {
        ConveyorLine line = FindLine(lineId);
        return line != null && line.isRunning && !isPaused;
    }

    public float GetLineSpeed(ConveyorLineId lineId)
    {
        ConveyorLine line = FindLine(lineId);
        if (line == null || !line.isRunning || isPaused)
            return 0f;

        return Mathf.Max(0f, line.speed);
    }

    public Transform[] GetLineWaypoints(ConveyorLineId lineId)
    {
        ConveyorLine line = FindLine(lineId);
        return line != null ? line.waypoints : null;
    }

    public int[] GetStopWaypointIndices(ConveyorLineId lineId)
    {
        ConveyorLine line = FindLine(lineId);
        return line != null ? line.stopWaypointIndices : null;
    }

    /// <summary>
    /// 대상 물체가 불량품인지 종합 판정합니다. (Item, BaseInfo, PartInfo 결함 여부 확인)
    /// </summary>
    public static bool IsDefectiveItem(GameObject target)
    {
        if (target == null) return false;

        Item item = target.GetComponent<Item>() 
                 ?? target.GetComponentInParent<Item>() 
                 ?? target.GetComponentInChildren<Item>();
        if (item != null && item.itemType == ItemType.Defective)
            return true;

        BaseInfo baseInfo = target.GetComponent<BaseInfo>() 
                         ?? target.GetComponentInParent<BaseInfo>() 
                         ?? target.GetComponentInChildren<BaseInfo>();
        if (baseInfo != null && baseInfo.defectType != DefectType.None)
            return true;

        PartInfo partInfo = target.GetComponent<PartInfo>() 
                         ?? target.GetComponentInParent<PartInfo>() 
                         ?? target.GetComponentInChildren<PartInfo>();
        if (partInfo != null && partInfo.defectType != DefectType.None)
            return true;

        return false;
    }

    public bool IsStopWaypoint(ConveyorLineId lineId, int waypointIndex, GameObject itemObject = null)
    {
        ConveyorLine line = FindLine(lineId);
        if (line == null || line.stopWaypointIndices == null)
            return false;

        foreach (int stopIndex in line.stopWaypointIndices)
        {
            if (stopIndex == waypointIndex)
            {
                // Line C(완제품 라인): 조립성공품(양품)인 경우 푸셔/스토퍼 지점을 정지 없이 직진 통과
                if (lineId == ConveyorLineId.LineC && itemObject != null && !IsDefectiveItem(itemObject))
                {
                    return false;
                }

                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 아이템이 정지 웨이포인트에 도달했을 때 호출됩니다.
    /// 라인을 멈추고 스토퍼를 올린 뒤 외부에 이벤트를 발행합니다.
    /// </summary>
    public void NotifyItemArrivedAtStopPoint(
        ConveyorLineId lineId,
        int waypointIndex,
        GameObject item)
    {
        // Line C에서 조립성공품(양품)인 경우 스토퍼 상승 및 라인 정지를 생략
        if (lineId == ConveyorLineId.LineC && item != null && !IsDefectiveItem(item))
        {
            Debug.Log($"[ConveyorManager] Line C 조립성공품('{item.name}') 도착: 스토퍼 상승 및 벨트 정지를 생략합니다.", this);
            return;
        }

        StopLine(lineId);
        RaiseStoppers(lineId);

        OnItemArrivedAtStopPoint?.Invoke(
            lineId,
            waypointIndex,
            item);
    }

    /// <summary>
    /// 정지점에 대기 중인 물체와 라인을 다시 동작시킵니다.
    /// 스토퍼를 내리고(통과 허용), 아이템 대기 상태를 해제하며, 라인을 재가동합니다.
    /// </summary>
    public void ReleaseItemAndRunLine(
        ConveyorLineId lineId,
        ConveyorItemMover mover)
    {
        LowerStoppers(lineId);

        if (mover != null)
            mover.ReleaseWaitingAtStopPoint();

        RunLine(lineId);
    }

    /// <summary>
    /// 해당 라인에 연결된 스토퍼들을 내립니다. (부품 통과 허용)
    /// </summary>
    public void LowerStoppers(ConveyorLineId lineId)
    {
        ConveyorLine line = FindLine(lineId);
        if (line == null || line.stoppers == null)
            return;

        foreach (PartStopper stopper in line.stoppers)
        {
            if (stopper != null)
                StartCoroutine(stopper.Resume());
        }
    }

    /// <summary>
    /// 해당 라인에 연결된 스토퍼들을 올립니다. (부품 정지 가로막음)
    /// </summary>
    public void RaiseStoppers(ConveyorLineId lineId)
    {
        ConveyorLine line = FindLine(lineId);
        if (line == null || line.stoppers == null)
            return;

        foreach (PartStopper stopper in line.stoppers)
        {
            if (stopper != null)
                StartCoroutine(stopper.Stop());
        }
    }

    public void RunLine(ConveyorLineId lineId)
    {
        ConveyorLine line = FindLine(lineId);
        if (line == null)
            return;

        line.isRunning = true;
        ApplyLineSpeed(line);
    }

    public void StopLine(ConveyorLineId lineId)
    {
        ConveyorLine line = FindLine(lineId);
        if (line == null)
            return;

        line.isRunning = false;
        ApplyLineSpeed(line);
    }

    public void StartAll()
    {
        isPaused = false;
        if (lines == null) return;

        foreach (ConveyorLine line in lines)
        {
            if (line == null) continue;
            line.isRunning = true;
            ApplyLineSpeed(line);
        }
    }

    public void StopAll()
    {
        if (lines == null) return;

        foreach (ConveyorLine line in lines)
        {
            if (line == null) continue;
            line.isRunning = false;
            ApplyLineSpeed(line);
        }
    }

    public void PauseAll()
    {
        isPaused = true;
        if (lines == null) return;

        foreach (ConveyorLine line in lines)
            ApplyLineSpeed(line);
    }

    public void ResumeAll()
    {
        isPaused = false;
        if (lines == null) return;

        foreach (ConveyorLine line in lines)
            ApplyLineSpeed(line);
    }

    public void SetLineSpeed(ConveyorLineId lineId, float speed)
    {
        ConveyorLine line = FindLine(lineId);
        if (line == null) return;

        line.speed = Mathf.Max(0f, speed);
        ApplyLineSpeed(line);
    }

    public void SetAllSpeed(float speed)
    {
        if (lines == null) return;

        foreach (ConveyorLine line in lines)
        {
            if (line == null) continue;
            line.speed = Mathf.Max(0f, speed);
            ApplyLineSpeed(line);
        }
    }

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
                if (line.waypoints[i] != null && line.waypoints[i + 1] != null)
                {
                    Gizmos.DrawLine(line.waypoints[i].position, line.waypoints[i + 1].position);
                }
            }

            if (line.stopWaypointIndices == null)
                continue;

            Gizmos.color = Color.yellow;

            foreach (int stopIndex in line.stopWaypointIndices)
            {
                if (stopIndex >= 0 && stopIndex < line.waypoints.Length && line.waypoints[stopIndex] != null)
                {
                    Gizmos.DrawSphere(line.waypoints[stopIndex].position, 0.08f);
                }
            }
        }
    }
}
