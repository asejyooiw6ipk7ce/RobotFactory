using System;
using UnityEngine;

public class ConveyorManager : MonoBehaviour
{
    [Serializable]
    private class ConveyorLine
    {
        [Tooltip("이 설정이 관리할 논리 라인 ID")]
        public ConveyorLineId lineId;

        [Tooltip("이 라인에 속한 벨트 렌더러들")]
        public Renderer[] beltRenderers;

        [Tooltip("이 라인에 적용할 벨트 셰이더 속도")]
        public float speed = 0.5f;

        // 현재 라인이 운전 상태인지 저장합니다.
        // 전체 일시정지와 구분하기 위해 별도로 보관합니다.
        [NonSerialized] public bool isRunning;
    }

    [Header("라인 설정")]

    // 인스펙터에서 LineA, LineB 등의 설정을 추가합니다.
    [SerializeField] private ConveyorLine[] lines;

    [Header("시작 설정")]

    // 체크하면 씬 시작 시 모든 라인을 운전 상태로 시작합니다.
    [SerializeField] private bool startOnAwake;

    // 기존 셰이더 속성 이름을 사용합니다.
    // "_Spped"는 프로젝트의 셰이더에 맞춰 기존 철자를 유지했습니다.
    private static readonly int SpeedIDRotor =
        Shader.PropertyToID("_Spped");

    private static readonly int SpeedIDBase =
        Shader.PropertyToID("_Speed");

    // 셰이더 속성을 갱신할 때 재사용합니다.
    private MaterialPropertyBlock propertyBlock;

    // 전체 일시정지 상태입니다.
    private bool isPaused;

    public bool IsPaused => isPaused;

    private void Awake()
    {
        propertyBlock = new MaterialPropertyBlock();

        // 씬 시작 상태를 각 라인에 적용합니다.
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

    // 라인의 현재 상태를 벨트 렌더러에 적용합니다.
    private void ApplyLineSpeed(ConveyorLine line)
    {
        if (line == null || line.beltRenderers == null)
            return;

        // 운전 중이며 전체 일시정지가 아닐 때만 셰이더를 움직입니다.
        float shaderSpeed =
            line.isRunning && !isPaused
                ? Mathf.Max(0f, line.speed)
                : 0f;

        foreach (Renderer beltRenderer in line.beltRenderers)
        {
            if (beltRenderer == null)
                continue;

            // 기존 머티리얼 속성은 유지하면서 속성 블록만 갱신합니다.
            beltRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(SpeedIDRotor, shaderSpeed);
            propertyBlock.SetFloat(SpeedIDBase, shaderSpeed);
            beltRenderer.SetPropertyBlock(propertyBlock);
        }
    }

    // ID에 해당하는 라인 하나를 운전합니다.
    public void RunLine(ConveyorLineId lineId)
    {
        ConveyorLine line = FindLine(lineId);
        if (line == null)
            return;

        line.isRunning = true;
        ApplyLineSpeed(line);
    }

    // ID에 해당하는 라인 하나를 정지합니다.
    public void StopLine(ConveyorLineId lineId)
    {
        ConveyorLine line = FindLine(lineId);
        if (line == null)
            return;

        line.isRunning = false;
        ApplyLineSpeed(line);
    }

    // ID에 해당하는 라인이 운전 상태인지 확인합니다.
    // 전체 일시정지 중이면 실제 이동 중이 아니므로 false를 반환합니다.
    public bool IsLineRunning(ConveyorLineId lineId)
    {
        ConveyorLine line = FindLine(lineId);

        return line != null &&
               line.isRunning &&
               !isPaused;
    }

    // 해당 라인이 실제 운전 중일 때만 설정 속도를 반환합니다.
    // 정지 또는 전체 일시정지 상태면 아이템 이동 속도로 0을 반환합니다.
    public float GetLineSpeed(ConveyorLineId lineId)
    {
        ConveyorLine line = FindLine(lineId);

        if (line == null || !line.isRunning || isPaused)
            return 0f;

        return line.speed;
    }

    // 등록된 모든 라인의 운전 상태를 켭니다.
    public void StartAll()
    {
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

    // 모든 라인을 일시정지합니다.
    // 각 라인의 운전 상태는 보존하므로 ResumeAll에서 원래 상태로 돌아갑니다.
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

    // 특정 라인의 속도를 바꾸고 즉시 셰이더에 적용합니다.
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
}