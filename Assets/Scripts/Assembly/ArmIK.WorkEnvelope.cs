using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// ==========================================================================================
// [ArmIK.WorkEnvelope] 로봇팔 360도 작업 반경(Work Envelope) 2D/3D 시각화 및 배치 진단 모듈
//
// 1. 2D 바닥 평면 원형 디스크 (Flat 2D): 메인 씬 바닥 설비 및 컨베이어 배치에 최적화
// 2. 3D 상공 반구 돔 (Dome 3D): 실제 리프트, 소켓 결합 등 공중 작업 영역 입체 시각화
// 3. 3D 전체 구체 (Sphere 3D): 전방위 물리 도달 가능 공간 입체 표현
// 4. 관절 체인 기반 반경 자동 측정 및 공정 타겟 도달 가능 여부(Reachability) 실시간 판정
// ==========================================================================================

public partial class ArmIK : MonoBehaviour
{
    public enum EnvelopeViewMode
    {
        Flat2D,       // 바닥면 2D 원형 디스크 및 링 (설비 평면 배치용)
        Dome3D,       // 지상 3D 반구 돔 (공중 리프트 및 작업 영역 입체 표시)
        Sphere3D,     // 3D 전방위 구체 (전체 도달 가능 체적)
        Both          // 2D 바닥 가이드 + 3D 입체 돔 동시 표시
    }

    #region 작업 반경 인스펙터 설정

    [Header("작업 반경 시각화 모드 (Work Envelope)")]
    [Tooltip("시각화 뷰 모드: Flat2D(바닥 평면), Dome3D(3D 상공 반구), Sphere3D(3D 전체 구), Both(2D+3D 동시)")]
    public EnvelopeViewMode viewMode = EnvelopeViewMode.Flat2D;

    [Tooltip("360도 작업 반경 시각 표시 켜기/끄기")]
    public bool showWorkRadius = true;

    [Tooltip("체크 시 로봇 또는 컴포넌트가 선택되었을 때만 표시 (해제 시 씬에서 항상 표시)")]
    public bool showOnlyWhenSelected = false;

    [Tooltip("권장 안전 작업 영역(초록색 반투명 디스크/와이어) 표시 여부")]
    public bool showRecommendedZone = true;

    [Tooltip("로봇 베이스 근처 사각지대(최소 반경 주황색 링) 표시 여부")]
    public bool showInnerDeadzone = true;

    [Tooltip("360도 방위선 및 각도 눈금선(0°, 90°, 180°, 270° 등) 표시 여부")]
    public bool showAngleSpokes = true;

    [Tooltip("반경 수치 및 안내 텍스트 표시 여부")]
    public bool showRadiusLabels = true;

    [Tooltip("테스트 타겟(testTarget) 또는 tip까지의 거리 및 작업 반경 판정선 표시 여부")]
    public bool showTargetDistance = true;

    [Header("작업 반경 수치 (m, UR10e 기본값)")]
    [Tooltip("물리적 최대 도달 반경 (UR10e 기본: 약 1.35m ~ 1.40m)")]
    public float maxReachRadius = 1.35f;

    [Tooltip("자세 제어 및 수평/수직 이동이 안정적인 권장 작업 반경 (기본: 1.10m)")]
    public float recommendedRadius = 1.10f;

    [Tooltip("로봇 베이스 및 링크 간섭으로 도달할 수 없는 내부 최소 반경 (기본: 0.25m)")]
    public float innerDeadzoneRadius = 0.25f;

    [Tooltip("바닥면 시각화 높이 오프셋 (기본: 0.005m = Z-파이팅 방지)")]
    public float floorHeightOffset = 0.005f;

    #endregion

    #region 기구학 원점 및 도달 판정

    /// <summary>
    /// 작업 반경의 바닥 중심 월드 좌표 반환
    /// </summary>
    public Vector3 GetWorkCenter()
    {
        Transform t = baseT;
        if (t == null)
        {
            if (joints != null && joints.Length > 0 && joints[0] != null && joints[0].transform.parent != null)
                t = joints[0].transform.parent;
            else
            {
                foreach (var tr in GetComponentsInChildren<Transform>())
                {
                    if (tr.name == "base_link" || tr.name == "base") { t = tr; break; }
                }
            }
        }
        if (t == null) t = transform;

        Vector3 c = t.position;
        c.y += floorHeightOffset;
        return c;
    }

    /// <summary>
    /// 3D 기구학 어깨 회전 피벗(3D 구/돔의 중심점) 월드 좌표 반환
    /// </summary>
    public Vector3 GetShoulderCenter()
    {
        if (joints != null && joints.Length > 0 && joints[0] != null)
        {
            return joints[0].transform.position;
        }
        Vector3 bc = GetWorkCenter();
        return bc + Vector3.up * 0.18f; // UR10e 베이스 링크 높이 약 0.18m
    }

    /// <summary>
    /// 대상 3D 위치가 작업 반경 내에 있는지 판별
    /// 반환값: 0 = 권장 안전 반경 (OK, 초록), 1 = 한계 영역 (주의, 노랑), 2 = 도달 불가 (초과, 빨강), -1 = 중심 사각지대 (너무 가까움, 주황)
    /// </summary>
    public int EvaluateReach(Vector3 worldPos, out float horizontalDist, out float directDist)
    {
        Vector3 baseCenter = GetWorkCenter();
        Vector3 shoulderCenter = GetShoulderCenter();

        Vector3 hDiff = worldPos - baseCenter;
        hDiff.y = 0f;
        horizontalDist = hDiff.magnitude;

        directDist = Vector3.Distance(shoulderCenter, worldPos);

        // 3D 모드일 때는 3D 유클리드 거리 기준으로 정밀 판정, 2D 모드일 때는 수평 거리 기준 판정
        float testDist = (viewMode == EnvelopeViewMode.Dome3D || viewMode == EnvelopeViewMode.Sphere3D) ? directDist : horizontalDist;

        if (testDist < innerDeadzoneRadius) return -1;
        if (testDist <= recommendedRadius) return 0;
        if (testDist <= maxReachRadius) return 1;
        return 2;
    }

    /// <summary>
    /// 실제 관절 링크 길이를 합산하여 최대 도달 반경 자동 갱신
    /// </summary>
    [ContextMenu("관절 길이 기반 반경 자동 측정 (Auto Measure Radius)")]
    public void AutoMeasureRadius()
    {
        if (joints == null || joints.Length == 0)
        {
            AutoAssign();
        }

        if (joints != null && joints.Length >= 2)
        {
            float total = 0f;
            for (int i = 0; i < joints.Length - 1; i++)
            {
                if (joints[i] != null && joints[i + 1] != null)
                {
                    total += Vector3.Distance(joints[i].transform.position, joints[i + 1].transform.position);
                }
            }
            if (joints[joints.Length - 1] != null && tip != null)
            {
                total += Vector3.Distance(joints[joints.Length - 1].transform.position, tip.position);
            }

            if (total > 0.5f)
            {
                maxReachRadius = Mathf.Round(total * 100f) / 100f;
                recommendedRadius = Mathf.Round(maxReachRadius * 0.82f * 100f) / 100f;
                innerDeadzoneRadius = Mathf.Round(maxReachRadius * 0.18f * 100f) / 100f;
                Debug.Log($"[ArmIK] 관절 체인 측정 완료: 최대 {maxReachRadius:F2}m / 권장 {recommendedRadius:F2}m / 사각지대 {innerDeadzoneRadius:F2}m");
            }
        }
    }

    #endregion

    #region Scene 기즈모 시각화 (2D / 3D)

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!showWorkRadius || showOnlyWhenSelected) return;
        DrawWorkEnvelopeGizmo();
    }

    private void OnDrawGizmosSelected()
    {
        if (!showWorkRadius) return;
        if (showOnlyWhenSelected)
        {
            DrawWorkEnvelopeGizmo();
        }
        else
        {
            DrawSelectedHighlights();
        }
    }

    /// <summary>
    /// 작업 반경 기즈모 렌더링 메인 엔트리
    /// </summary>
    public void DrawWorkEnvelopeGizmo()
    {
        switch (viewMode)
        {
            case EnvelopeViewMode.Flat2D:
                Draw2DEnvelope();
                break;
            case EnvelopeViewMode.Dome3D:
                Draw3DEnvelope(isDome: true);
                break;
            case EnvelopeViewMode.Sphere3D:
                Draw3DEnvelope(isDome: false);
                break;
            case EnvelopeViewMode.Both:
                Draw2DEnvelope();
                Draw3DEnvelope(isDome: true);
                break;
        }

        // 테스트 타겟(testTarget) 거리 판정선
        if (showTargetDistance && testTarget != null)
        {
            DrawTargetDistanceGuide(GetWorkCenter(), testTarget.position, testTarget.name);
        }
    }

    /// <summary>
    /// [2D] 바닥 평면 360도 원형 시각 표시
    /// </summary>
    private void Draw2DEnvelope()
    {
        Vector3 center = GetWorkCenter();

        // 1. 권장 안전 작업 영역 (초록색 반투명 디스크 + 외곽 링)
        if (showRecommendedZone)
        {
            Handles.color = new Color(0.12f, 0.82f, 0.38f, 0.08f);
            Handles.DrawSolidDisc(center, Vector3.up, recommendedRadius);

            Handles.color = new Color(0.12f, 0.82f, 0.38f, 0.75f);
            Handles.DrawWireDisc(center, Vector3.up, recommendedRadius);
        }

        // 2. 최대 물리 도달 반경 (빨간색 경고 외곽 링 + 연한 노란색 주의 대역)
        Handles.color = new Color(0.95f, 0.75f, 0.15f, 0.03f);
        Handles.DrawSolidDisc(center, Vector3.up, maxReachRadius);

        Handles.color = new Color(0.95f, 0.22f, 0.22f, 0.75f);
        Handles.DrawWireDisc(center, Vector3.up, maxReachRadius);

        // 3. 내부 최소 반경 (사각지대 주황색 링)
        if (showInnerDeadzone && innerDeadzoneRadius > 0.01f)
        {
            Handles.color = new Color(1.0f, 0.2f, 0.1f, 0.12f);
            Handles.DrawSolidDisc(center, Vector3.up, innerDeadzoneRadius);

            Handles.color = new Color(1.0f, 0.45f, 0.1f, 0.80f);
            Handles.DrawWireDisc(center, Vector3.up, innerDeadzoneRadius);
        }

        // 4. 360도 방위선 및 각도 눈금
        if (showAngleSpokes)
        {
            float startR = innerDeadzoneRadius;
            float endR = maxReachRadius;

            Vector3 fwd = transform.forward;
            Vector3 rgt = transform.right;

            Handles.color = new Color(0.2f, 0.7f, 1.0f, 0.45f);
            Handles.DrawLine(center + fwd * startR, center + fwd * endR);
            Handles.DrawLine(center - fwd * startR, center - fwd * endR);
            Handles.DrawLine(center + rgt * startR, center + rgt * endR);
            Handles.DrawLine(center - rgt * startR, center - rgt * endR);

            Handles.color = new Color(0.8f, 0.8f, 0.8f, 0.20f);
            for (int deg = 45; deg < 360; deg += 90)
            {
                Vector3 dir = Quaternion.Euler(0, deg, 0) * transform.forward;
                Handles.DrawLine(center + dir * startR, center + dir * recommendedRadius);
            }
        }

        // 5. 텍스트 라벨
        if (showRadiusLabels)
        {
            GUIStyle labelStyle = new GUIStyle();
            labelStyle.fontSize = 11;
            labelStyle.fontStyle = FontStyle.Bold;

            labelStyle.normal.textColor = new Color(0.15f, 0.90f, 0.40f, 0.90f);
            Handles.Label(center + transform.right * recommendedRadius + Vector3.up * 0.02f, $"권장 작업 {recommendedRadius:F2}m (2D)", labelStyle);

            labelStyle.normal.textColor = new Color(0.95f, 0.30f, 0.30f, 0.90f);
            Handles.Label(center + transform.right * maxReachRadius + Vector3.up * 0.02f, $"최대 한계 {maxReachRadius:F2}m", labelStyle);

            if (showInnerDeadzone)
            {
                labelStyle.normal.textColor = new Color(1.0f, 0.50f, 0.15f, 0.85f);
                Handles.Label(center + transform.right * innerDeadzoneRadius + Vector3.up * 0.02f, $"최소 {innerDeadzoneRadius:F2}m", labelStyle);
            }

            GUIStyle dirStyle = new GUIStyle();
            dirStyle.fontSize = 10;
            dirStyle.normal.textColor = new Color(0.3f, 0.75f, 1.0f, 0.75f);
            Handles.Label(center + transform.forward * (maxReachRadius + 0.08f), "0° (Front)", dirStyle);
            Handles.Label(center + transform.right * (maxReachRadius + 0.08f), "90° (Right)", dirStyle);
            Handles.Label(center - transform.forward * (maxReachRadius + 0.08f), "180° (Back)", dirStyle);
            Handles.Label(center - transform.right * (maxReachRadius + 0.08f), "270° (Left)", dirStyle);
        }
    }

    /// <summary>
    /// [3D] 입체 상공 반구 돔(Dome) 또는 전체 구체(Sphere) 와이어프레임 시각 표시
    /// </summary>
    private void Draw3DEnvelope(bool isDome)
    {
        Vector3 origin = GetShoulderCenter();
        Vector3 floorCenter = GetWorkCenter();

        // 1. 권장 영역 3D 와이어 돔 / 구체 (초록색)
        if (showRecommendedZone)
        {
            Color recColor = new Color(0.12f, 0.85f, 0.38f, 0.65f);
            DrawWireSphereDome(origin, floorCenter.y, recommendedRadius, recColor, isDome);
        }

        // 2. 최대 한계 영역 3D 와이어 돔 / 구체 (빨간색)
        Color maxColor = new Color(0.95f, 0.25f, 0.25f, 0.65f);
        DrawWireSphereDome(origin, floorCenter.y, maxReachRadius, maxColor, isDome);

        // 3. 내부 사각지대 (주황색)
        if (showInnerDeadzone && innerDeadzoneRadius > 0.01f)
        {
            Color deadColor = new Color(1.0f, 0.45f, 0.1f, 0.60f);
            DrawWireSphereDome(origin, floorCenter.y, innerDeadzoneRadius, deadColor, isDome);
        }

        // 4. 3D 라벨
        if (showRadiusLabels)
        {
            GUIStyle s3d = new GUIStyle();
            s3d.fontSize = 11;
            s3d.fontStyle = FontStyle.Bold;

            s3d.normal.textColor = new Color(0.15f, 0.90f, 0.40f, 0.90f);
            Handles.Label(origin + Vector3.up * recommendedRadius, $"3D 권장 천장 {recommendedRadius:F2}m", s3d);

            s3d.normal.textColor = new Color(0.95f, 0.30f, 0.30f, 0.90f);
            Handles.Label(origin + Vector3.up * maxReachRadius + Vector3.right * 0.1f, $"3D 최대 정점 {maxReachRadius:F2}m", s3d);
        }
    }

    /// <summary>
    /// 3D 와이어 반구/구체 렌더링 헬퍼 (위도선 + 경도 수직호)
    /// </summary>
    private void DrawWireSphereDome(Vector3 center, float floorY, float radius, Color color, bool isDome)
    {
        Handles.color = color;

        // A. 수직 경도선 (수직 대원호 4개: 0°, 45°, 90°, 135°)
        float arcAngle = isDome ? 180f : 360f;
        Vector3 startVec = isDome ? transform.forward : transform.up;

        for (int deg = 0; deg < 180; deg += 45)
        {
            Quaternion rot = Quaternion.Euler(0, deg, 0);
            Vector3 normal = rot * transform.right;
            Vector3 from = rot * transform.forward;

            if (isDome)
            {
                // 지면 위 180도 반원 호
                Handles.DrawWireArc(center, normal, from, 180f, radius);
            }
            else
            {
                Handles.DrawWireDisc(center, normal, radius);
            }
        }

        // B. 수평 위도선 (높이별 수평 원선 4~5단)
        int steps = isDome ? 4 : 7;
        float minH = isDome ? 0f : -radius * 0.85f;
        float maxH = radius * 0.85f;

        for (int i = 1; i <= steps; i++)
        {
            float t = (float)i / (steps + 1);
            float h = Mathf.Lerp(minH, maxH, t);
            if (isDome && (center.y + h < floorY)) continue; // 지면 아래 링 생략

            float rAtH = Mathf.Sqrt(Mathf.Max(0f, radius * radius - h * h));
            if (rAtH > 0.05f)
            {
                Handles.color = new Color(color.r, color.g, color.b, color.a * 0.40f);
                Handles.DrawWireDisc(new Vector3(center.x, center.y + h, center.z), Vector3.up, rAtH);
            }
        }

        // 적도선 (중심 높이 메인 링)
        Handles.color = color;
        Handles.DrawWireDisc(center, Vector3.up, radius);
    }

    private void DrawSelectedHighlights()
    {
        if (showTargetDistance && tip != null)
        {
            Vector3 center = GetWorkCenter();
            DrawTargetDistanceGuide(center, tip.position, "Tip (AttachPoint)");
        }
    }

    private void DrawTargetDistanceGuide(Vector3 center, Vector3 targetPos, string targetName)
    {
        Vector3 shoulderCenter = GetShoulderCenter();
        Vector3 floorTarget = new Vector3(targetPos.x, center.y, targetPos.z);
        int reachCode = EvaluateReach(targetPos, out float hDist, out float dDist);

        Color lineColor;
        string statusText;
        switch (reachCode)
        {
            case 0:
                lineColor = new Color(0.15f, 0.90f, 0.40f, 0.90f);
                statusText = "안전 작업 범위 (OK)";
                break;
            case 1:
                lineColor = new Color(1.0f, 0.85f, 0.20f, 0.90f);
                statusText = "한계 근접 주의 (Marginal)";
                break;
            case -1:
                lineColor = new Color(1.0f, 0.50f, 0.15f, 0.90f);
                statusText = "사각지대 (너무 가까움)";
                break;
            default:
                lineColor = new Color(1.0f, 0.20f, 0.20f, 0.95f);
                statusText = "도달 불가 (Out of Reach!)";
                break;
        }

        Handles.color = lineColor;
        if (viewMode == EnvelopeViewMode.Flat2D)
        {
            Handles.DrawDottedLine(center, floorTarget, 3f);
            Handles.DrawLine(floorTarget, targetPos);
        }
        else
        {
            // 3D 모드: 어깨 원점에서 목표 지점으로의 3D 공간 직접 직선
            Handles.DrawLine(shoulderCenter, targetPos);
            Handles.DrawDottedLine(targetPos, floorTarget, 2f);
        }
        Handles.DrawWireDisc(floorTarget, Vector3.up, 0.03f);

        GUIStyle tagStyle = new GUIStyle();
        tagStyle.fontSize = 11;
        tagStyle.fontStyle = FontStyle.Bold;
        tagStyle.normal.textColor = lineColor;

        string distInfo = (viewMode == EnvelopeViewMode.Flat2D)
            ? $"수평 {hDist:F2}m"
            : $"3D직선 {dDist:F2}m (수평 {hDist:F2}m, 높이 {targetPos.y:F2}m)";

        Handles.Label(floorTarget + Vector3.up * 0.05f, $"{targetName}: {distInfo} / {statusText}", tagStyle);
    }

#endif

    #endregion
}
