// ==========================================================================================
// [인스펙터 테스트 GUI 활성화 플래그]
// 아래 #define ENABLE_INSPECTOR_TEST 앞에 슬래시 두 개(//)를 붙여 주석 처리하면
// 인스펙터의 모든 테스트 버튼 및 디버그 패널이 즉시 사라지고 기본 인스펙터로 전환됩니다.
// ==========================================================================================
#define ENABLE_INSPECTOR_TEST

#if UNITY_EDITOR && ENABLE_INSPECTOR_TEST
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(AssemblySequence))]
public class AssemblySequenceEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // 1. 기본 인스펙터 프로퍼티 표시
        DrawDefaultInspector();

        AssemblySequence seq = (AssemblySequence)target;
        EditorGUILayout.Space(10);

        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        GUILayout.Label("🛠️ [AssemblySequence] 통합 제어 및 검증 패널", EditorStyles.boldLabel);

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
            Debug.Log("[AssemblySequence] 참조 자동 연결 및 그리퍼 설정 완료!");
        }

        GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
        if (GUILayout.Button("🔄 부품 및 로봇 초기화 (Reset)", GUILayout.Height(28)))
        {
            if (Application.isPlaying) seq.ResetToInitialState();
            else EditorUtility.DisplayDialog("알림", "Play(▶) 모드에서 실행해주세요!", "확인");
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        // =====================================================================
        // [2] 로봇 작업 반경 및 공정 배치 진단 (메인 씬 통합 배치용)
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
            DrawTargetReachDiagnostic(seq.ik, "부품1 픽업 (Claw)", seq.partStopPoint != null ? seq.partStopPoint.position : (seq.clawPart != null ? seq.GetPickPosition() : (Vector3?)null));
            seq.GetSlotInfo(out Vector3 sc, out _, out _);
            DrawTargetReachDiagnostic(seq.ik, "부품2 소켓 (Socket)", sc != Vector3.zero ? (Vector3?)sc : null);
            DrawTargetReachDiagnostic(seq.ik, "폐기함 (RejectBin)", seq.rejectBin != null ? (Vector3?)seq.GetRejectBinDropPosition() : null);
            if (seq.outputConveyorPoint != null)
                DrawTargetReachDiagnostic(seq.ik, "완제품 배출 (Conveyor C)", seq.outputConveyorPoint.position);
        }
        else
        {
            EditorGUILayout.HelpBox("ArmIK가 연결되지 않았습니다. '참조 자동 연결' 버튼을 눌러주세요.", MessageType.Warning);
        }
        EditorGUILayout.EndVertical();

        // =====================================================================
        // [3] 확정 4대 분기 시나리오 원클릭 검증 (FR-07 / WBS 6.6)
        // =====================================================================
        EditorGUILayout.Space(6);
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("🧪 조립 4대 분기 시나리오 원클릭 검증 (FR-07 / WBS 6.6)", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = new Color(0.4f, 0.85f, 0.4f);
        if (GUILayout.Button("경우 1\n바로 성공 (0회)", GUILayout.Height(36)))
        {
            if (Application.isPlaying) seq.StartCoroutine(seq.Assemble(-1f, null, null, null, 0));
            else EditorUtility.DisplayDialog("알림", "Play(▶) 모드에서 실행해주세요!", "확인");
        }
        GUI.backgroundColor = new Color(0.35f, 0.75f, 1.0f);
        if (GUILayout.Button("경우 2\n재시도 1회 성공", GUILayout.Height(36)))
        {
            if (Application.isPlaying) seq.StartCoroutine(seq.Assemble(-1f, null, null, null, 1));
            else EditorUtility.DisplayDialog("알림", "Play(▶) 모드에서 실행해주세요!", "확인");
        }
        GUI.backgroundColor = new Color(0.95f, 0.75f, 0.25f);
        if (GUILayout.Button("경우 3\n재시도 2회 성공", GUILayout.Height(36)))
        {
            if (Application.isPlaying) seq.StartCoroutine(seq.Assemble(-1f, null, null, null, 2));
            else EditorUtility.DisplayDialog("알림", "Play(▶) 모드에서 실행해주세요!", "확인");
        }
        GUI.backgroundColor = new Color(1.0f, 0.45f, 0.45f);
        if (GUILayout.Button("경우 4\n최종 실패 & 폐기", GUILayout.Height(36)))
        {
            if (Application.isPlaying) seq.StartCoroutine(seq.Assemble(-1f, null, null, null, 3));
            else EditorUtility.DisplayDialog("알림", "Play(▶) 모드에서 실행해주세요!", "확인");
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        // 실패 확률 빠른 프리셋 버튼
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("실패 확률 프리셋:", GUILayout.Width(95));
        if (GUILayout.Button("✅ 기본 0% (정상 공정)", EditorStyles.miniButton))
        {
            seq.assemblyFailureProbability = 0f;
            seq.retry1FailureProbability = 0f;
            seq.retry2FailureProbability = 0f;
            EditorUtility.SetDirty(seq);
        }
        if (GUILayout.Button("🎲 시연용 50%", EditorStyles.miniButton))
        {
            seq.assemblyFailureProbability = 0.50f;
            seq.retry1FailureProbability = 0.50f;
            seq.retry2FailureProbability = 0.50f;
            EditorUtility.SetDirty(seq);
        }
        if (GUILayout.Button("❌ 100% (전부 실패)", EditorStyles.miniButton))
        {
            seq.assemblyFailureProbability = 1.0f;
            seq.retry1FailureProbability = 1.0f;
            seq.retry2FailureProbability = 1.0f;
            EditorUtility.SetDirty(seq);
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();

        // =====================================================================
        // [4] TC-11 완제품 비전 검사 원클릭 검증 (FR-09 카메라 3 연계)
        // =====================================================================
        EditorGUILayout.Space(6);
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("📷 TC-11 완제품 비전 검사 검증 (FR-09 카메라 3)", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = new Color(0.35f, 0.85f, 0.45f);
        if (GUILayout.Button("✅ [3.0° 양품] 전체 조립 검증\n(Assemble ➔ Camera3 Pass)", GUILayout.Height(36)))
        {
            if (Application.isPlaying) seq.StartCoroutine(seq.Assemble(-1f, null, 3.0f, 0f, 0));
            else EditorUtility.DisplayDialog("알림", "Play(▶) 모드에서 실행해주세요!", "확인");
        }
        GUI.backgroundColor = new Color(1.0f, 0.45f, 0.45f);
        if (GUILayout.Button("❌ [3.1° 불량] 전체 조립 검증\n(Assemble ➔ Camera3 Fail)", GUILayout.Height(36)))
        {
            if (Application.isPlaying) seq.StartCoroutine(seq.Assemble(-1f, null, 3.1f, 0f, 0));
            else EditorUtility.DisplayDialog("알림", "Play(▶) 모드에서 실행해주세요!", "확인");
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();

        // =====================================================================
        // [5] 메인 시퀀스 실행 및 실시간 상태 모니터
        // =====================================================================
        EditorGUILayout.Space(8);
        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = seq.IsBusy ? Color.yellow : new Color(0.2f, 0.85f, 0.35f);
        if (GUILayout.Button(seq.IsBusy ? "시퀀스 동작 중..." : "▶ 전체 수평 조립 실행 (Assemble)", GUILayout.Height(34)))
        {
            if (Application.isPlaying) seq.StartCoroutine(seq.Assemble(-1f));
            else EditorUtility.DisplayDialog("알림", "Play(▶) 모드에서 실행해주세요!", "확인");
        }

        GUI.backgroundColor = new Color(0.3f, 0.6f, 0.9f);
        if (GUILayout.Button("🔄 재투입 재조립 (Refeed)", GUILayout.Width(160), GUILayout.Height(34)))
        {
            if (Application.isPlaying) seq.StartCoroutine(seq.Refeed());
            else EditorUtility.DisplayDialog("알림", "Play(▶) 모드에서 실행해주세요!", "확인");
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        // 상태 및 마지막 결합 소요시간 표시
        EditorGUILayout.Space(4);
        string statusSummary = $"현재 상태: {seq.Status}";
        if (seq.LastElapsedTime > 0f)
        {
            statusSummary += $"\n⏱️ 최근 결합: {seq.LastElapsedTime:F2}초 (제한: {seq.assemblyTimeoutSeconds:F0}초) | 적용 각도 오차: {seq.LastAngleError:F2}°";
        }
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
