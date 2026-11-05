using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// [FR-05] 카메라 2 부품2(로봇베이스) 불량 검사 컴포넌트
/// - 부품2의 BaseInfo.defectType 플래그를 읽어 양품(Pass) / 불량(Fail - PartLoss) 판정
/// - 베이스의 회전(Yaw) 방향에 영향을 받지 않는 순수 추상화 판정
/// - Display 실시간 출력을 방해하지 않는 1회 수동 RenderTexture 스냅샷 캡처
/// - FR-11 InspectEvent 발행 및 대시보드(스테이션 2 불량률/이상 알림) 실시간 연동
/// - 상위 스테이션 제어기 연동용 비동기 코루틴 및 완료 콜백/이벤트 제공
/// </summary>
public class Camera2Inspector : MonoBehaviour
{
    [Header("카메라 설정")]
    [Tooltip("촬영에 사용할 카메라 컴포넌트 (비워두면 동일 오브젝트의 Camera 자동 탐색)")]
    public Camera captureCamera;

    [Tooltip("검사 스테이션 ID (기본: Station2)")]
    public StationId stationId = StationId.Station2;

    [Tooltip("카메라 근평면 거리 (m, 기본 0.05m)")]
    [Range(0.01f, 0.3f)]
    public float nearClipPlane = 0.05f;

    [Tooltip("스냅샷 캡처 해상도")]
    public Vector2Int snapshotSize = new Vector2Int(512, 384);

    [Header("검사 대상 부품 (수동 지정 또는 자동 탐색)")]
    [Tooltip("검사할 특정 부품2 (비워두면 카메라 앞 가장 가까운 베이스 또는 moto_base_error 자동 감지)")]
    public BaseInfo targetBase;

    // 런타임 결과 프로퍼티 (에디터 및 대시보드 표시용)
    public Texture2D LastSnapshot { get; private set; }
    public InspectResult? LastResult { get; private set; }
    public DefectType LastDefectType { get; private set; }
    public string LastPartId { get; private set; }

    /// <summary>
    /// 검사 판정 완료 시 외부에서 구독 가능한 C# 이벤트 (부품2 BaseInfo, 판정 결과)
    /// </summary>
    public event Action<BaseInfo, InspectResult> OnInspectionCompleted;

    private int serialNumber = 0;

    private void Reset()
    {
        ApplyCameraSettings();
    }

    private void OnValidate()
    {
        ApplyCameraSettings();
    }

    private void Awake()
    {
        ApplyCameraSettings();
    }

    private void OnDestroy()
    {
        if (LastSnapshot != null)
        {
            Destroy(LastSnapshot);
            LastSnapshot = null;
        }
    }

    private void ApplyCameraSettings()
    {
        if (captureCamera == null)
        {
            captureCamera = GetComponent<Camera>();
        }

        if (captureCamera != null)
        {
            captureCamera.nearClipPlane = nearClipPlane;
        }
    }

    /// <summary>
    /// Display 실시간 출력을 방해하지 않고 임시 RenderTexture를 바인딩하여 1프레임 스냅샷을 캡처
    /// </summary>
    public Texture2D CaptureSnapshot()
    {
        ApplyCameraSettings();
        if (captureCamera == null) return null;

        RenderTexture prevCamRT = captureCamera.targetTexture;
        RenderTexture prevActive = RenderTexture.active;
        RenderTexture tempRT = RenderTexture.GetTemporary(snapshotSize.x, snapshotSize.y, 24, RenderTextureFormat.ARGB32);

        try
        {
            captureCamera.targetTexture = tempRT;
            captureCamera.Render();
            RenderTexture.active = tempRT;

            if (LastSnapshot != null)
            {
                Destroy(LastSnapshot);
                LastSnapshot = null;
            }

            LastSnapshot = new Texture2D(snapshotSize.x, snapshotSize.y, TextureFormat.RGB24, false);
            LastSnapshot.ReadPixels(new Rect(0, 0, snapshotSize.x, snapshotSize.y), 0, 0);
            LastSnapshot.Apply();
        }
        finally
        {
            captureCamera.targetTexture = prevCamRT;
            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(tempRT);
        }

        return LastSnapshot;
    }

    /// <summary>
    /// [FR-05] 부품2(로봇베이스) 검사 및 판정 수행
    /// 베이스가 어떤 방향(Yaw)을 바라보고 있더라도 BaseInfo.defectType 플래그를 읽어 정확히 판정합니다.
    /// </summary>
    /// <param name="robotBaseInfo">검사 대상 부품2의 BaseInfo (null이면 GetEffectiveTargetBase 자동 호출)</param>
    /// <returns>양품이면 true, 불량이면 false</returns>
    public bool InspectPart2(BaseInfo robotBaseInfo = null)
    {
        if (robotBaseInfo == null)
        {
            robotBaseInfo = GetEffectiveTargetBase();
        }

        // 1. 비전 스냅샷 1회 캡처 (연출 및 UI 확인용)
        CaptureSnapshot();

        // 2. 양품/불량 판정 (SRS 추상화 구현: BaseInfo.defectType 읽기)
        bool isPass = (robotBaseInfo != null) && (robotBaseInfo.defectType == DefectType.None);
        InspectResult result = isPass ? InspectResult.Pass : InspectResult.Fail;
        DefectType defect = isPass ? DefectType.None : DefectType.PartLoss; // SRS 규칙: 부품2 불량은 '일부 부품 상실(PartLoss)'

        if (robotBaseInfo != null)
        {
            robotBaseInfo.defectType = defect;
        }

        LastResult = result;
        LastDefectType = defect;

        // 3. 부품 식별자(ID) 결정
        string partId = null;
        if (robotBaseInfo != null && !string.IsNullOrEmpty(robotBaseInfo.id))
        {
            partId = robotBaseInfo.id;
        }
        else
        {
            partId = $"BASE-{++serialNumber:0000}";
            if (robotBaseInfo != null) robotBaseInfo.id = partId;
        }
        LastPartId = partId;

        // 4. 검사 이벤트 버스(FR-11) 발행
        InspectEventBus.Publish(new InspectEvent(partId, stationId, result, defect, Time.time));

        // 5. C# 이벤트 알림
        OnInspectionCompleted?.Invoke(robotBaseInfo, result);

        string objName = robotBaseInfo != null ? robotBaseInfo.gameObject.name : "null";
        string resultTag = isPass ? "🟢 PASS (양품)" : "🔴 FAIL (불량 - PartLoss)";
        Debug.Log($"[Camera2Inspector] 📸 부품2 비전 검사 완료 | Target={objName}, PartId={partId} ➔ {resultTag} | Defect={defect}");

        return isPass;
    }

    /// <summary>
    /// 상위 공정 제어기에서 호출할 검사 코루틴 (안정 대기 시간 및 완료 콜백 지원)
    /// </summary>
    public IEnumerator CaptureAndInspectRoutine(BaseInfo robotBaseInfo, Action<InspectResult> onCompleted = null, float settleDelay = 0.1f)
    {
        if (settleDelay > 0f)
        {
            yield return new WaitForSeconds(settleDelay);
        }

        bool isPass = InspectPart2(robotBaseInfo);
        onCompleted?.Invoke(isPass ? InspectResult.Pass : InspectResult.Fail);
    }

    /// <summary>
    /// GameObject로부터 BaseInfo를 찾아 즉시 검사 수행
    /// </summary>
    public bool InspectFromObject(GameObject target)
    {
        if (target == null) return false;
        BaseInfo baseInfo = target.GetComponentInParent<BaseInfo>();
        if (baseInfo == null) baseInfo = target.GetComponentInChildren<BaseInfo>();
        if (baseInfo == null)
        {
            baseInfo = target.AddComponent<BaseInfo>();
            string lowerName = target.name.ToLower();
            if (lowerName.Contains("error") || lowerName.Contains("defect"))
            {
                baseInfo.defectType = DefectType.PartLoss;
            }
        }
        return InspectPart2(baseInfo);
    }

    /// <summary>
    /// 검사 대상 부품2를 스마트하게 탐색 (1. targetBase 지정값 ➔ 2. 카메라 앞 가장 가까운 베이스 ➔ 3. moto_base_error 오브젝트)
    /// </summary>
    public BaseInfo GetEffectiveTargetBase()
    {
        if (targetBase != null) return targetBase;

        // 1순위: 카메라 위치에서 가장 가까운 BaseInfo 탐색 (조립대의 먼 베이스 배제)
        BaseInfo nearest = null;
        float minDistance = float.MaxValue;
        foreach (var b in FindObjectsByType<BaseInfo>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            float d = Vector3.Distance(transform.position, b.transform.position);
            if (d < minDistance)
            {
                minDistance = d;
                nearest = b;
            }
        }

        // 만약 가장 가까운 BaseInfo가 카메라 기준 5m 이내에 있으면 채택
        if (nearest != null && minDistance < 5.0f)
        {
            return nearest;
        }

        // 2순위: BaseInfo가 아직 안 붙어 있는 moto_base_error 오브젝트가 씬에 있는지 확인
        foreach (var go in FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (go.name.ToLower().Contains("moto_base_error"))
            {
                var b = go.GetComponent<BaseInfo>() ?? go.AddComponent<BaseInfo>();
                b.defectType = DefectType.PartLoss;
                return b;
            }
        }

        return nearest;
    }
}
