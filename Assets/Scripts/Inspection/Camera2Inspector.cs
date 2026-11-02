using System.Collections;
using UnityEngine;

/// <summary>
/// [FR-05] 카메라 2 부품2(로봇베이스) 불량 검사 컴포넌트
/// - 부품2의 BaseInfo.defectType 플래그를 읽어 양품(Pass) / 불량(Fail - PartLoss) 판정
/// - 베이스의 회전(Yaw) 방향에 영향을 받지 않는 순수 추상화 판정
/// - Display 실시간 출력을 방해하지 않는 1회 수동 RenderTexture 스냅샷 캡처
/// - FR-11 InspectEvent 발행 및 대시보드(스테이션 2 불량률/이상 알림) 실시간 연동
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

    [Header("호환성 설정")]
    [Tooltip("Item 컴포넌트가 존재할 경우 itemType(Normal/Defective)과 BaseInfo.defectType 상호 동기화 여부")]
    public bool syncLegacyItem = true;

    // 런타임 결과 프로퍼티 (에디터 및 대시보드 표시용)
    public Texture2D LastSnapshot { get; private set; }
    public InspectResult? LastResult { get; private set; }
    public DefectType LastDefectType { get; private set; }
    public string LastPartId { get; private set; }

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
    /// aseInfo.defectType 플래그를 읽어 판정합니다.
    /// </summary>
    /// <param name="robotBaseInfo">검사 대상 부품2의 BaseInfo</param>
    /// <returns>양품이면 true, 불량이면 false</returns>
    public bool InspectPart2(BaseInfo robotBaseInfo)
    {
        // 비전 스냅샷 1회 캡처 (연출 및 UI 확인용)
        CaptureSnapshot();


        // 양품/불량 판정 (SRS 추상화 구현: BaseInfo.defectType 읽기)
        bool isPass = (robotBaseInfo != null) && (robotBaseInfo.defectType == DefectType.None);
        InspectResult result = isPass ? InspectResult.Pass : InspectResult.Fail;
        DefectType defect = isPass ? DefectType.None : DefectType.PartLoss; // SRS 규칙: 부품2 불량은 '일부 부품 상실(PartLoss)'

        if (robotBaseInfo != null)
        {
            robotBaseInfo.defectType = defect;
        }

        LastResult = result;
        LastDefectType = defect;

        // 부품 식별자(ID) 결정
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

        // 검사 이벤트 버스(FR-11) 발행
        InspectEventBus.Publish(new InspectEvent(partId, stationId, result, defect, Time.time));

        string resultTag = isPass ? "🟢 PASS (양품)" : "🔴 FAIL (불량 - PartLoss)";
        Debug.Log($"[Camera2Inspector] 📸 부품2 비전 검사 완료 | PartId={partId} ➔ {resultTag} | Defect={defect}");

        return isPass;
    }
}
