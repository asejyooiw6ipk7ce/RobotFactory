using UnityEditor;
using UnityEngine;

/// <summary>
/// [FR-05 / TC-06] 카메라 2 부품2 검사기 커스텀 에디터
/// - 실시간 스냅샷 미리보기 창 및 합격/불량 컬러 배지 표시
/// - TC-06 판정 기준 (양품 None / 불량 PartLoss) 단독 즉시 검증 도구
/// </summary>
[CustomEditor(typeof(Camera2Inspector))]
public class Camera2InspectorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // 1. 기본 인스펙터 프로퍼티 표시
        DrawDefaultInspector();

        Camera2Inspector insp = (Camera2Inspector)target;
        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

        // =====================================================================
        // [1] 비전 스냅샷 미리보기 패널
        // =====================================================================
        GUILayout.Label("📸 카메라 2 비전 캡처 미리보기", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical("box");

        BaseInfo currentDetected = insp.GetEffectiveTargetBase();
        string detectedName = currentDetected != null ? currentDetected.gameObject.name : "(감지된 부품 없음)";
        EditorGUILayout.LabelField("🎯 현재 검사 타겟:", detectedName, EditorStyles.boldLabel);

        if (insp.LastSnapshot != null)
        {
            Rect rect = GUILayoutUtility.GetAspectRect((float)insp.snapshotSize.x / insp.snapshotSize.y);
            GUI.DrawTexture(rect, insp.LastSnapshot, ScaleMode.ScaleToFit);

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("대상 부품 ID:", insp.LastPartId ?? "-", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("판정 불량 유형:", insp.LastDefectType.ToString(), EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            // 판정 결과 배지
            if (insp.LastResult.HasValue)
            {
                bool isPass = insp.LastResult.Value == InspectResult.Pass;
                GUI.backgroundColor = isPass ? new Color(0.2f, 0.85f, 0.35f) : new Color(1.0f, 0.35f, 0.35f);
                string badgeText = isPass ? "🟢 PASS (양품 - DefectType.None)" : "🔴 FAIL (불량 - DefectType.PartLoss 발생)";
                EditorGUILayout.HelpBox(badgeText, isPass ? MessageType.Info : MessageType.Error);
                GUI.backgroundColor = Color.white;
            }
        }
        else
        {
            EditorGUILayout.HelpBox("💡 아직 촬영된 스냅샷이 없습니다.\n아래 테스트 버튼을 누르면 캡처본이 표시됩니다.", MessageType.Info);
        }

        EditorGUILayout.EndVertical();

        // =====================================================================
        // [2] TC-06 단독 즉시 테스트 패널 (Play 모드 전용)
        // =====================================================================
        EditorGUILayout.Space(10);
        GUILayout.Label("🧪 TC-06 단독 즉시 검증 (Play 모드 전용)", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical("box");

        if (GUILayout.Button("📸 현재 타겟 부품2 즉시 촬영 & 판정", GUILayout.Height(30)))
        {
            if (Application.isPlaying)
            {
                ExecuteDirectInspection(insp, null);
            }
            else
            {
                EditorUtility.DisplayDialog("알림", "Play(▶) 모드에서 실행해주세요!", "확인");
            }
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = new Color(0.35f, 0.85f, 0.45f);
        if (GUILayout.Button("✅ [양품] 결함 없음 주입 & 판정\n(기대: Pass / None)", GUILayout.Height(38)))
        {
            if (Application.isPlaying)
            {
                ExecuteDirectInspection(insp, DefectType.None);
            }
            else
            {
                EditorUtility.DisplayDialog("알림", "Play(▶) 모드에서 실행해주세요!", "확인");
            }
        }

        GUI.backgroundColor = new Color(1.0f, 0.45f, 0.45f);
        if (GUILayout.Button("❌ [불량] 부품 상실(PartLoss) 주입 & 판정\n(기대: Fail / PartLoss)", GUILayout.Height(38)))
        {
            if (Application.isPlaying)
            {
                ExecuteDirectInspection(insp, DefectType.PartLoss);
            }
            else
            {
                EditorUtility.DisplayDialog("알림", "Play(▶) 모드에서 실행해주세요!", "확인");
            }
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.HelpBox($"💡 현재 타겟 [{detectedName}]에 결함 여부를 주입하고 즉시 촬영·판정합니다.", MessageType.None);
        EditorGUILayout.EndVertical();
    }

    private void ExecuteDirectInspection(Camera2Inspector insp, DefectType? forcedDefect)
    {
        BaseInfo target = insp.GetEffectiveTargetBase();
        if (target == null)
        {
            EditorUtility.DisplayDialog("오류", "씬에서 검사할 부품2(BaseInfo 또는 moto_base_error)를 찾을 수 없습니다.\nTarget Base 슬롯에 오브젝트를 드래그해 주세요.", "확인");
            return;
        }

        if (forcedDefect.HasValue)
        {
            target.defectType = forcedDefect.Value;
        }

        insp.InspectPart2(target);
        Repaint();
    }
}
