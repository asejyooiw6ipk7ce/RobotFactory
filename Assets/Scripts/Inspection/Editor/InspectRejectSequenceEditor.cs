// ==========================================================================================
// [인스펙터 테스트 GUI 활성화 플래그]
// 아래 #define ENABLE_INSPECTOR_TEST 앞에 슬래시 두 개(//)를 붙여 주석 처리하면
// 인스펙터의 모든 테스트 버튼 및 디버그 패널이 즉시 사라지고 기본 인스펙터로 전환됩니다.
// ==========================================================================================
#define ENABLE_INSPECTOR_TEST

#if UNITY_EDITOR && ENABLE_INSPECTOR_TEST
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(InspectRejectSequence))]
public class InspectRejectSequenceEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // 1. 기본 인스펙터 프로퍼티 표시
        DrawDefaultInspector();

        InspectRejectSequence seq = (InspectRejectSequence)target;
        EditorGUILayout.Space(10);

        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        GUILayout.Label("🛠️ [InspectRejectSequence] 통합 제어 및 검증 패널", EditorStyles.boldLabel);

        // =====================================================================
        // [1] 유틸리티: 참조 자동 연결 & 초기화
        // =====================================================================
        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("🔧 참조 자동 연결 (Auto Wire)", GUILayout.Height(28)))
        {
            seq.AutoFindReferences();
            seq.ApplyGripperCloseTarget();
            EditorUtility.SetDirty(seq);
            Debug.Log("[InspectRejectSequence] 참조 자동 연결 및 그리퍼 폭 설정 완료!");
        }

        GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
        if (GUILayout.Button("🔄 부품 및 로봇 초기화 (Reset)", GUILayout.Height(28)))
        {
            if (Application.isPlaying)
            {
                seq.ResetToInitialState();
            }
            else
            {
                EditorUtility.DisplayDialog("알림", "Play(▶) 모드에서 실행해주세요!", "확인");
            }
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        // =====================================================================
        // [2] 로봇 작업 반경 및 공정 배치 진단 (Work Envelope)
        // =====================================================================
        EditorGUILayout.Space(6);
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("🌐 로봇 작업 반경 및 공정 배치 진단 (Work Envelope)", EditorStyles.boldLabel);

        if (seq.ik != null)
        {
            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = seq.ik.showWorkRadius ? new Color(0.3f, 0.9f, 0.5f) : new Color(0.85f, 0.85f, 0.85f);
            string toggleBtnText = seq.ik.showWorkRadius ? "🟢 360° 작업 반경 표시 [ON]" : "⚪ 360° 작업 반경 표시 [OFF]";
            if (GUILayout.Button(toggleBtnText, GUILayout.Height(24)))
            {
                seq.ik.showWorkRadius = !seq.ik.showWorkRadius;
                EditorUtility.SetDirty(seq.ik);
                SceneView.RepaintAll();
            }
            GUI.backgroundColor = Color.white;

            bool newReachGizmo = EditorGUILayout.ToggleLeft("배치 가이드선 표시", seq.showReachabilityGizmos, GUILayout.Width(125));
            if (newReachGizmo != seq.showReachabilityGizmos)
            {
                seq.showReachabilityGizmos = newReachGizmo;
                EditorUtility.SetDirty(seq);
                SceneView.RepaintAll();
            }
            EditorGUILayout.EndHorizontal();

            // 2D / 3D 뷰 모드 전환 탭
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("시각화 모드:", GUILayout.Width(75));
            ArmIK.EnvelopeViewMode curMode = seq.ik.viewMode;
            if (GUILayout.Button("2D 평면", curMode == ArmIK.EnvelopeViewMode.Flat2D ? EditorStyles.miniButtonMid : EditorStyles.miniButton))
            {
                seq.ik.viewMode = ArmIK.EnvelopeViewMode.Flat2D;
                EditorUtility.SetDirty(seq.ik);
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("3D 상공 돔", curMode == ArmIK.EnvelopeViewMode.Dome3D ? EditorStyles.miniButtonMid : EditorStyles.miniButton))
            {
                seq.ik.viewMode = ArmIK.EnvelopeViewMode.Dome3D;
                EditorUtility.SetDirty(seq.ik);
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("2D+3D 동시", curMode == ArmIK.EnvelopeViewMode.Both ? EditorStyles.miniButtonMid : EditorStyles.miniButton))
            {
                seq.ik.viewMode = ArmIK.EnvelopeViewMode.Both;
                EditorUtility.SetDirty(seq.ik);
                SceneView.RepaintAll();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);
            DrawTargetReachDiagnostic(seq.ik, "불량 부품 픽업 (Pick)", seq.pickPoint != null ? (Vector3?)seq.GetPickPosition() : null);
            DrawTargetReachDiagnostic(seq.ik, "폐기함 투하 (RejectBin)", seq.rejectBin != null || seq.rejectPose != null ? (Vector3?)seq.GetRejectBinDropPosition() : null);
        }
        else
        {
            EditorGUILayout.HelpBox("ArmIK가 연결되지 않았습니다. '참조 자동 연결' 버튼을 눌러주세요.", MessageType.Warning);
        }
        EditorGUILayout.EndVertical();

        // =====================================================================
        // [3] 인스펙터 단계별 단독 실행 검증 (Step 1~4)
        // =====================================================================
        EditorGUILayout.Space(6);
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("🧪 인스펙터 단계별 단독 실행 검증 (Step-by-Step)", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = new Color(0.4f, 0.85f, 0.4f);
        if (GUILayout.Button("1단계\n상공 접근", GUILayout.Height(36)))
        {
            if (Application.isPlaying) seq.RunStep1_Approach();
            else EditorUtility.DisplayDialog("알림", "Play(▶) 모드에서 실행해주세요!", "확인");
        }
        GUI.backgroundColor = new Color(0.35f, 0.75f, 1.0f);
        if (GUILayout.Button("2단계\n하강 및 파지", GUILayout.Height(36)))
        {
            if (Application.isPlaying) seq.RunStep2_Grasp();
            else EditorUtility.DisplayDialog("알림", "Play(▶) 모드에서 실행해주세요!", "확인");
        }
        GUI.backgroundColor = new Color(0.95f, 0.75f, 0.25f);
        if (GUILayout.Button("3단계\n수직 리프팅", GUILayout.Height(36)))
        {
            if (Application.isPlaying) seq.RunStep3_Lift();
            else EditorUtility.DisplayDialog("알림", "Play(▶) 모드에서 실행해주세요!", "확인");
        }
        GUI.backgroundColor = new Color(1.0f, 0.45f, 0.45f);
        if (GUILayout.Button("4단계\n폐기 투하·홈", GUILayout.Height(36)))
        {
            if (Application.isPlaying) seq.RunStep4_DropAndReturn();
            else EditorUtility.DisplayDialog("알림", "Play(▶) 모드에서 실행해주세요!", "확인");
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();

        // =====================================================================
        // [4] 메인 시퀀스 전체 실행 및 실시간 모니터
        // =====================================================================
        EditorGUILayout.Space(8);
        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = seq.IsBusy ? Color.yellow : new Color(0.2f, 0.85f, 0.35f);
        string mainBtnLabel = seq.IsBusy ? "시퀀스 동작 중..." : "▶ 전체 불량 부품 폐기 실행 (Reject)";
        if (GUILayout.Button(mainBtnLabel, GUILayout.Height(34)))
        {
            if (Application.isPlaying) seq.RunRejectTest();
            else EditorUtility.DisplayDialog("알림", "Play(▶) 모드에서 실행해주세요!", "확인");
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        // 실시간 상태 표시
        EditorGUILayout.Space(4);
        string graspStatus = (seq.gripper != null) ? (seq.gripper.LastGraspOK ? "성공" : "미파지/대기") : "그리퍼 미연결";
        string rejectStatus = seq.LastRejectOK ? "성공" : "대기/실패";
        string statusSummary = $"현재 상태: {seq.Status}\n그리퍼 파지: [{graspStatus}] | 최근 폐기 결과: [{rejectStatus}]";
        EditorGUILayout.HelpBox(statusSummary, MessageType.None);
    }

    private void DrawTargetReachDiagnostic(ArmIK ik, string targetName, Vector3? pos)
    {
        if (!pos.HasValue)
        {
            EditorGUILayout.LabelField($" - {targetName}:", "(미지정)", EditorStyles.miniLabel);
            return;
        }

        int code = ik.EvaluateReach(pos.Value, out float hDist, out _);
        string statusStr;
        Color col;
        switch (code)
        {
            case 0:
                statusStr = "🟢 안전 권장 범위 (OK)";
                col = new Color(0.1f, 0.6f, 0.2f);
                break;
            case 1:
                statusStr = "🟡 한계 근접 (Marginal)";
                col = new Color(0.7f, 0.5f, 0.1f);
                break;
            case -1:
                statusStr = "🟠 중심 사각지대 (너무 가까움)";
                col = new Color(0.8f, 0.4f, 0.1f);
                break;
            default:
                statusStr = "🔴 도달 불가 (Out of Reach!)";
                col = new Color(0.8f, 0.1f, 0.1f);
                break;
        }

        GUIStyle s = new GUIStyle(EditorStyles.miniLabel);
        s.normal.textColor = col;
        s.fontStyle = FontStyle.Bold;
        EditorGUILayout.LabelField($" - {targetName}: {hDist:F2}m", statusStr, s);
    }
}
#endif

