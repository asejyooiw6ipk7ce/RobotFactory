using System.Collections;
using UnityEngine;

/// <summary>
/// [FR-09] 카메라 3 완제품 결합 각도 검사 및 메인 씬 통합 구도 보존 컴포넌트
/// - 소켓 결합 및 비틀림 완료 후 완제품을 촬영하여 각도 오차(thresholdAngle, 기본 3.0°) 검사
/// - Display 3 실시간 렌더링을 방해하지 않는 1회 수동 RenderTexture 스냅샷 캡처
/// - PartInfo 및 BaseInfo의 defectType 최종 동기화 및 FR-11 InspectEvent 발행
/// - 로봇2/소켓 기준 상대 오프셋 보존 및 원클릭 자동 정렬 지원
/// </summary>
public class Camera3Inspector : MonoBehaviour
{
    private const float AngleEpsilon = 0.0001f;

    [Header("카메라 설정")]
    [Tooltip("촬영에 사용할 카메라 컴포넌트 (비워두면 동일 오브젝트의 Camera 자동 탐색)")]
    public Camera captureCamera;

    [Tooltip("검사 스테이션 ID")]
    public StationId stationId = StationId.Station3;

    [Header("검사 판정 기준")]
    [Tooltip("불량 판정 각도 오차 기준 (도, 기본 3.0°. 오차 > 3.0° 이면 불량)")]
    [Range(0.1f, 15f)]
    public float thresholdAngle = 3.0f;

    [Tooltip("카메라 근평면 거리 (m, 기본 0.05m). 클로 근접 시 앞면 잘림 방지")]
    [Range(0.01f, 0.3f)]
    public float nearClipPlane = 0.05f;

    [Tooltip("스냅샷 캡처 해상도")]
    public Vector2Int snapshotSize = new Vector2Int(512, 384);

    [Tooltip("추락 안전 가드 바닥 기준 높이 (m, 기본 -1.0m)")]
    public float dropFloorY = -1.0f;

    [Tooltip("검사 시 ProcessCounters.Add(CounterType.Inspected) 호출 여부")]
    public bool publishInspectedCounter = true;

    [Tooltip("검사 직후 불량(Defective) 카운터 증가 및 PartExitEvent 자동 발행 여부 (출하 Shipped는 출하 상자 RejectController가 집계)")]
    public bool publishBranchCounterAndExit = true;

    [Header("메인 씬 통합 대비 상대 오프셋 (ChangminScene 실측치)")]
    [Tooltip("로봇2 베이스 기준 카메라 위치 로컬 오프셋")]
    public Vector3 offsetFromRobotPos = new Vector3(-0.215f, 0.2409f, 0.6874f);

    [Tooltip("로봇2 베이스 기준 카메라 회전(오일러각) 로컬 오프셋")]
    public Vector3 offsetFromRobotEuler = new Vector3(0f, -90f, 0f);

    [Tooltip("조립 소켓 기준 상대 오프셋 (X=우측, Y=상하, Z=소켓 정면 거리)")]
    public Vector3 offsetFromSocketLocal = new Vector3(0.013f, -0.006f, 0.404f);

    // 런타임 결과 프로퍼티 (에디터 및 대시보드 표시용)
    public Texture2D LastSnapshot { get; private set; }
    public float LastMeasuredAngle { get; private set; }
    public InspectResult? LastResult { get; private set; }
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

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(transform.position, transform.forward * 0.5f);
    }

    /// <summary>
    /// Display 3 실시간 출력을 방해하지 않고 임시 RenderTexture를 바인딩하여 1프레임 스냅샷을 캡처
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
    /// 완제품 결합 각도 검사 및 판정 수행
    /// </summary>
    public bool InspectProduct(PartInfo clawPartInfo, BaseInfo robotBaseInfo)
    {
        // 1. 추락 안전 가드 (FR-09 예외 처리: 바닥 이하 추락 시 삭제 및 에러 로그, 이벤트 미발행)
        if ((clawPartInfo != null && clawPartInfo.transform.position.y < dropFloorY) ||
            (robotBaseInfo != null && robotBaseInfo.transform.position.y < dropFloorY))
        {
            Debug.LogError("[Camera3Inspector] ⚠️ 완제품이 검사 영역 바닥 아래로 추락하여 삭제 처리합니다 (검사 이벤트 미발행).");
            if (clawPartInfo != null) Destroy(clawPartInfo.gameObject);
            if (robotBaseInfo != null) Destroy(robotBaseInfo.gameObject);
            return false;
        }

        // 2. 비전 스냅샷 1회 캡처
        CaptureSnapshot();

        // 3. 결합 각도 오차 측정 및 판정 (Mathf.Abs(angleError) <= thresholdAngle)
        float angleError = clawPartInfo != null ? clawPartInfo.assemblyAngleError : 0f;
        LastMeasuredAngle = angleError;

        bool isPass = Mathf.Abs(angleError) <= (thresholdAngle + AngleEpsilon);
        InspectResult result = isPass ? InspectResult.Pass : InspectResult.Fail;
        DefectType defect = isPass ? DefectType.None : DefectType.AngleError;
        LastResult = result;

        // 4. 완제품(PartInfo 및 BaseInfo)의 DefectType 최종 동기화
        if (clawPartInfo != null) clawPartInfo.defectType = defect;
        if (robotBaseInfo != null) robotBaseInfo.defectType = defect;

        // [Commit 1] 완제품 Item 컴포넌트의 itemType 동기화 (푸셔 판정용)
        Item baseItem = null;
        if (robotBaseInfo != null)
        {
            baseItem = robotBaseInfo.GetComponent<Item>() 
                    ?? robotBaseInfo.GetComponentInParent<Item>() 
                    ?? robotBaseInfo.GetComponentInChildren<Item>()
                    ?? (robotBaseInfo.transform.root != null ? robotBaseInfo.transform.root.GetComponent<Item>() : null);
            if (baseItem != null)
            {
                baseItem.itemType = isPass ? ItemType.Normal : ItemType.Defective;
            }
        }

        Item clawItem = null;
        if (clawPartInfo != null)
        {
            clawItem = clawPartInfo.GetComponent<Item>() 
                    ?? clawPartInfo.GetComponentInParent<Item>() 
                    ?? clawPartInfo.GetComponentInChildren<Item>()
                    ?? (clawPartInfo.transform.root != null ? clawPartInfo.transform.root.GetComponent<Item>() : null);
            if (clawItem != null)
            {
                clawItem.itemType = isPass ? ItemType.Normal : ItemType.Defective;
            }
        }

        // 5. 부품 식별자(ID) 결정
        string partId = null;
        if (robotBaseInfo != null && !string.IsNullOrEmpty(robotBaseInfo.id)) partId = robotBaseInfo.id;
        else if (clawPartInfo != null && !string.IsNullOrEmpty(clawPartInfo.id)) partId = clawPartInfo.id;
        else partId = $"PROD-{++serialNumber:0000}";
        LastPartId = partId;

        // 6. 검사 이벤트 버스(FR-11) 발행 및 공정 카운터 증가
        InspectEventBus.Publish(new InspectEvent(partId, stationId, result, defect, Time.time));

        if (publishInspectedCounter)
        {
            ProcessCounters.Add(CounterType.Inspected);
        }

        // 7. 불량(Defective) 카운터 및 공정 이탈 이벤트(PartExitEvent) 발행 (FR-10, FR-12)
        //    불량 개수는 판정 시점에 센다. 출하(Shipped)는 출하 상자(RejectController)에 도착할 때 센다.
        if (publishBranchCounterAndExit)
        {
            ExitReason exitReason = isPass ? ExitReason.Shipped : ExitReason.Defective;

            if (!isPass) ProcessCounters.Add(CounterType.Defective);
            InspectEventBus.PublishExit(new PartExitEvent(partId, exitReason, defect, Time.time));

            // 조립에 사용되어 완제품에 통합된 부품1(클로)도 공정 위 대기 부품에서 소모되었음을 알림
            if (clawPartInfo != null && !string.IsNullOrEmpty(clawPartInfo.id) && clawPartInfo.id != partId)
            {
                InspectEventBus.PublishExit(new PartExitEvent(clawPartInfo.id, exitReason, defect, Time.time));
            }
        }

        string resultTag = isPass ? "🟢 PASS (양품)" : "🔴 FAIL (불량 - AngleError)";
        Debug.Log($"[Camera3Inspector] 📸 완제품 비전 검사 완료 | PartId={partId}, 각도오차={angleError:F2}° (기준: {thresholdAngle:F1}°) ➔ {resultTag} | Defect={defect}");

        return isPass;
    }

    /// <summary>
    /// 조립 시퀀스에서 호출할 검사 코루틴 (안정 대기 시간 포함)
    /// </summary>
    public IEnumerator CaptureAndInspectRoutine(PartInfo clawPartInfo, BaseInfo robotBaseInfo, float settleDelay = 0.1f)
    {
        if (settleDelay > 0f)
        {
            yield return new WaitForSeconds(settleDelay);
        }

        InspectProduct(clawPartInfo, robotBaseInfo);
    }

    // =========================================================================
    // [메인 씬 통합용 구도 보존 및 자동 정렬 유틸리티]
    // =========================================================================

    /// <summary>
    /// 현재 카메라 위치/각도를 로봇 루트 기준으로 계산하여 오프셋으로 저장
    /// </summary>
    public void SavePoseRelativeToRobot(Transform robotRoot)
    {
        if (robotRoot == null) return;
        offsetFromRobotPos = robotRoot.InverseTransformPoint(transform.position);
        offsetFromRobotEuler = (Quaternion.Inverse(robotRoot.rotation) * transform.rotation).eulerAngles;
        Debug.Log($"[Camera3Inspector] 💾 로봇2 기준 상대 오프셋 저장 완료: Pos={offsetFromRobotPos}, Rot={offsetFromRobotEuler}");
    }

    /// <summary>
    /// 저장된 오프셋을 기반으로 로봇 루트 위치에 맞춰 카메라 자동 정렬
    /// </summary>
    public void AlignToRobot(Transform robotRoot)
    {
        if (robotRoot == null) return;
        transform.position = robotRoot.TransformPoint(offsetFromRobotPos);
        transform.rotation = robotRoot.rotation * Quaternion.Euler(offsetFromRobotEuler);
        Debug.Log($"[Camera3Inspector] 🤖 로봇2 기준으로 카메라 정렬 완료: {transform.position}");
    }

    /// <summary>
    /// 조립 소켓 위치 및 삽입 방향을 기준으로 카메라 정면 배치 자동 정렬
    /// </summary>
    public void AlignToSocket(Vector3 slotCenter, Vector3 insertDir)
    {
        if (slotCenter == Vector3.zero || insertDir == Vector3.zero) return;

        Vector3 cleanInsert = Vector3.ProjectOnPlane(insertDir, Vector3.up).normalized;
        if (cleanInsert.sqrMagnitude < 0.001f) cleanInsert = Vector3.left;

        Vector3 rightDir = Vector3.Cross(Vector3.up, cleanInsert).normalized;
        Vector3 camPos = slotCenter - cleanInsert * offsetFromSocketLocal.z
                         + rightDir * offsetFromSocketLocal.x
                         + Vector3.up * offsetFromSocketLocal.y;

        transform.position = camPos;
        transform.rotation = Quaternion.LookRotation(cleanInsert, Vector3.up);
        Debug.Log($"[Camera3Inspector] 🎯 소켓 기준으로 카메라 정렬 완료: {transform.position}");
    }
}

